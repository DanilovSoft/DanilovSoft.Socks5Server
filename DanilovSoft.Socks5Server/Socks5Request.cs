using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace DanilovSoft.Socks5Server
{
    /// <summary>
    /// Максимальный размер 262 байт.
    /// </summary>
    [StructLayout(LayoutKind.Auto)]
    internal readonly struct Socks5Request
    {
        public const int MaximumSize = 262;
        public bool IsEmpty => (IPAddress == null && DomainName == null);

        /// <summary>
        /// Поддерживается только ConnectTcp.
        /// </summary>
        public Socks5Command Command { get; }
        public AddressType Address { get; }
        public IPAddress? IPAddress { get; }
        public string? DomainName { get; }
        public ushort Port { get; }

        internal static async ValueTask<Socks5Request> ReceiveRequestAsync(ManagedTcpSocket managedTcp, Memory<byte> buffer)
        {
            Debug.Assert(buffer.Length >= MaximumSize);

            // Как минимум должно быть 4 байта.
            SocketReceiveResult rcvResult = await managedTcp.ReceiveBlockAsync(buffer.Slice(0, 4)).ConfigureAwait(false);
            if (!rcvResult.ReceiveSuccess)
                return default;

            byte version = buffer.Span[0];
            if (version != 5)
                throw new InvalidOperationException($"Не верный номер версии Socks. Получено {version}, ожидалось 5");

            var command = (Socks5Command)buffer.Span[1];
            if (!Enum.IsDefined(typeof(Socks5Command), command))
                throw new InvalidOperationException("Ошибка протокола SOCKS 5");

            // Зарезервированный байт, должен быть 0x00
            if (buffer.Span[2] != 0)
                throw new InvalidOperationException("Ошибка протокола SOCKS 5. Зарезервированный байт должен быть 0x00");

            var address = (AddressType)buffer.Span[3];

            string? domainName = null;
            IPAddress? ipAddress = null;

            // Сдвинуть бефер вперёд.
            buffer = buffer.Slice(4);

            switch (address)
            {
                case AddressType.IPv4:
                    {
                        Memory<byte> ipv4span = buffer.Slice(0, 4);
                        rcvResult = await managedTcp.ReceiveBlockAsync(ipv4span).ConfigureAwait(false);
                        if (!rcvResult.ReceiveSuccess)
                            return default;

                        ipAddress = new IPAddress(ipv4span.ToArray());
                        
                        // Сдвинуть бефер вперёд.
                        buffer = buffer.Slice(4);

                        break;
                    }
                case AddressType.DomainName:
                    {
                        // 1 байт с длиной строки.
                        rcvResult = await managedTcp.ReceiveAsync(buffer.Slice(0, 1)).ConfigureAwait(false);
                        if (!rcvResult.ReceiveSuccess)
                            return default;

                        // Размер строки в байтах.
                        byte strLen = buffer.Span[0];

                        Memory<byte> hostSpan = buffer.Slice(1, strLen);

                        // Читаем всю строку.
                        rcvResult = await managedTcp.ReceiveBlockAsync(hostSpan).ConfigureAwait(false);
                        if (!rcvResult.ReceiveSuccess)
                            return default;

                        domainName = Encoding.ASCII.GetString(buffer.Slice(1, strLen).Span);

                        // Сдвинуть бефер вперёд.
                        buffer = buffer.Slice(1 + strLen);

                        break;
                    }
                case AddressType.IPv6:
                    {
                        Memory<byte> ipv6span = buffer.Slice(0, 16);
                        rcvResult = await managedTcp.ReceiveBlockAsync(ipv6span).ConfigureAwait(false);
                        if (!rcvResult.ReceiveSuccess)
                            return default;

                        ipAddress = new IPAddress(ipv6span.ToArray());

                        // Сдвинуть бефер вперёд.
                        buffer = buffer.Slice(16);

                        break;
                    }
                default:
                    throw new InvalidOperationException("Ошибка протокола SOCKS 5");
            }

            // Последний сегмент — 2 байта, номер порта.
            Memory<byte> portSpan = buffer.Slice(0, 2);
            rcvResult = await managedTcp.ReceiveBlockAsync(portSpan).ConfigureAwait(false);
            if (!rcvResult.ReceiveSuccess)
                return default;

            var port = (ushort)((portSpan.Span[0] << 8) | portSpan.Span[1]);

            return new Socks5Request(command, address, ipAddress, domainName, port);
        }

        private Socks5Request(Socks5Command command, AddressType address, IPAddress? ipAddress, string? domainName, ushort port)
        {
            Command = command;
            Address = address;
            IPAddress = ipAddress;
            DomainName = domainName;
            Port = port;
        }

        // ctor
        /// <exception cref="InvalidOperationException"/>
        public Socks5Request(ReadOnlySpan<byte> span)
        {
            if (span.Length >= 6)
            {
                Command = (Socks5Command)span[1];

                if (Enum.IsDefined(typeof(Socks5Command), Command))
                {
                    // Зарезервированный байт, должен быть 0x00
                    if (span[2] == 0)
                    {
                        Address = (AddressType)span[3];

                        int addressLen;
                        ReadOnlySpan<byte> addressSpan = span.Slice(4);
                        switch (Address)
                        {
                            case AddressType.IPv4:
                                addressLen = 4;
                                IPAddress = new IPAddress(addressSpan.Slice(0, 4).ToArray());
                                DomainName = default;
                                break;
                            case AddressType.DomainName:
                                addressLen = addressSpan[0] + 1;
                                DomainName = Encoding.ASCII.GetString(addressSpan.Slice(1, addressSpan[0]));
                                IPAddress = default;
                                break;
                            case AddressType.IPv6:
                                addressLen = 16;
                                IPAddress = new IPAddress(addressSpan.Slice(0, 16).ToArray());
                                DomainName = default;
                                break;
                            default:
                                throw new InvalidOperationException("Ошибка протокола SOCKS 5");
                        }

                        var portSpan = addressSpan.Slice(addressLen);

                        Port = (ushort)((portSpan[0] << 8) | portSpan[1]);

                        return;
                    }
                    else
                    {
                        ThrowHelper.ThrowInvalidOperationException("Ошибка протокола SOCKS 5. Зарезервированный байт должен быть 0x00");
                    }
                }
                else
                {
                    ThrowHelper.ThrowInvalidOperationException("Ошибка протокола SOCKS 5");
                }
            }
            else
            {
                ThrowHelper.ThrowInvalidOperationException("Ошибка протокола SOCKS 5");
            }

            Command = default;
            Address = default;
            IPAddress = default;
            DomainName = default;
            Port = default;
        }
    }
}
