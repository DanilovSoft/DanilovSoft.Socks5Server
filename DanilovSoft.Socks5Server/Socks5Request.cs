﻿using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;

namespace DanilovSoft.Socks5Server;

/// <summary>
/// Максимальный размер 262 байт.
/// </summary>
[StructLayout(LayoutKind.Auto)]
[DebuggerDisplay(@"\{default = {this == default}\}")]
internal readonly struct Socks5Request : IEquatable<Socks5Request>
{
    public const int MaximumSize = 262;
    /// <summary>
    /// Поддерживается только ConnectTcp.
    /// </summary>
    public Socks5Command Command { get; }
    public AddressType Address { get; }
    public IPAddress? IPAddress { get; }
    public string? DomainName { get; }
    public ushort Port { get; }

    internal static async ValueTask<Socks5Request> ReceiveRequest(ManagedTcpSocket managedTcp, Memory<byte> buffer)
    {
        Debug.Assert(buffer.Length >= MaximumSize);

        // Как минимум должно быть 4 байта.
        var rcvResult = await managedTcp.ReceiveBlockAsync(buffer[..4]).ConfigureAwait(false);
        if (!rcvResult.ReceiveSuccess)
        {
            return default;
        }

        var version = buffer.Span[0];
        if (version != 5)
        {
            throw new InvalidOperationException($"Не верный номер версии Socks. Получено {version}, ожидалось 5");
        }

        var command = (Socks5Command)buffer.Span[1];
        if (!Enum.IsDefined(typeof(Socks5Command), command))
        {
            throw new InvalidOperationException("Ошибка протокола SOCKS 5");
        }

        // Зарезервированный байт, должен быть 0x00
        if (buffer.Span[2] != 0)
        {
            throw new InvalidOperationException("Ошибка протокола SOCKS 5. Зарезервированный байт должен быть 0x00");
        }

        var address = (AddressType)buffer.Span[3];

        string? domainName = null;
        IPAddress? ipAddress = null;

        // Сдвинуть бефер вперёд.
        buffer = buffer[4..];

        switch (address)
        {
            case AddressType.IPv4:
                {
                    var ipv4span = buffer[..4];
                    rcvResult = await managedTcp.ReceiveBlockAsync(ipv4span).ConfigureAwait(false);
                    if (!rcvResult.ReceiveSuccess)
                    {
                        return default;
                    }

                    ipAddress = new IPAddress(ipv4span.ToArray());
                    
                    // Сдвинуть бефер вперёд.
                    buffer = buffer[4..];

                    break;
                }
            case AddressType.DomainName:
                {
                    // 1 байт с длиной строки.
                    rcvResult = await managedTcp.ReceiveAsync(buffer[..1]).ConfigureAwait(false);
                    if (!rcvResult.ReceiveSuccess)
                    {
                        return default;
                    }

                    // Размер строки в байтах.
                    var strLen = buffer.Span[0];

                    var hostSpan = buffer.Slice(1, strLen);

                    // Читаем всю строку.
                    rcvResult = await managedTcp.ReceiveBlockAsync(hostSpan).ConfigureAwait(false);
                    if (!rcvResult.ReceiveSuccess)
                    {
                        return default;
                    }

                    domainName = Encoding.ASCII.GetString(buffer.Slice(1, strLen).Span);

                    // Сдвинуть бефер вперёд.
                    buffer = buffer[(1 + strLen)..];

                    break;
                }
            case AddressType.IPv6:
                {
                    var ipv6span = buffer[..16];
                    rcvResult = await managedTcp.ReceiveBlockAsync(ipv6span).ConfigureAwait(false);
                    if (!rcvResult.ReceiveSuccess)
                    {
                        return default;
                    }

                    ipAddress = new IPAddress(ipv6span.ToArray());

                    // Сдвинуть бефер вперёд.
                    buffer = buffer[16..];

                    break;
                }
            default:
                throw new InvalidOperationException("Ошибка протокола SOCKS 5");
        }

        // Последний сегмент — 2 байта, номер порта.
        var portSpan = buffer[..2];
        rcvResult = await managedTcp.ReceiveBlockAsync(portSpan).ConfigureAwait(false);
        if (!rcvResult.ReceiveSuccess)
        {
            return default;
        }

        var port = (ushort)((portSpan.Span[0] << 8) | portSpan.Span[1]);

        return new Socks5Request(command, address, ipAddress, domainName, port);
    }

    public bool Equals([AllowNull] Socks5Request other)
    {
        return IPAddress == other.IPAddress
        && DomainName == other.DomainName;
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
                    var addressSpan = span[4..];
                    switch (Address)
                    {
                        case AddressType.IPv4:
                            addressLen = 4;
                            IPAddress = new IPAddress(addressSpan[..4].ToArray());
                            DomainName = default;
                            break;
                        case AddressType.DomainName:
                            addressLen = addressSpan[0] + 1;
                            DomainName = Encoding.ASCII.GetString(addressSpan.Slice(1, addressSpan[0]));
                            IPAddress = default;
                            break;
                        case AddressType.IPv6:
                            addressLen = 16;
                            IPAddress = new IPAddress(addressSpan[..16].ToArray());
                            DomainName = default;
                            break;
                        default:
                            throw new InvalidOperationException("Ошибка протокола SOCKS 5");
                    }

                    var portSpan = addressSpan[addressLen..];

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

    public static bool operator !=(Socks5Request left, Socks5Request right)
    {
        return !(left == right);
    }

    public static bool operator ==(Socks5Request left, Socks5Request right)
    {
        return left.Equals(other: right);
    }

    public override bool Equals(object? obj)
    {
        return obj is Socks5Request o && Equals(other: o);
    }

    public override int GetHashCode()
    {
        return 0;
    }
}
