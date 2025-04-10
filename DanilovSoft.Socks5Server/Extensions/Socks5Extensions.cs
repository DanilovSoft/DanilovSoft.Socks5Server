using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using DanilovSoft.Socks5Server.TcpSocket;

namespace DanilovSoft.Socks5Server.Extensions;

internal static class Socks5Extensions
{
    public static async ValueTask<SocksAuthRequest> ReceiveAuthRequest(this Socket socket, Memory<byte> buffer, CancellationToken ct = default)
    {
        const int MaximumSize = 257;

        Debug.Assert(buffer.Length >= MaximumSize);

        // Как минимум должно быть 2 байта.
        var rcvResult = await socket.ReceiveExactAsync(buffer[..2], ct);
        if (!rcvResult.ReceiveSuccess)
        {
            return new SocksAuthRequest(authMethods: null);
        }

        var version = buffer.Span[0];
        if (version != 5)
        {
            throw new InvalidOperationException($"Не верный номер версии Socks. Получено {version}, ожидалось 5");
        }

        // Количество поддерживаемых методов аутентификации.
        var authCount = buffer.Span[1];

        // Номера методов аутентификации, переменная длина, 1 байт для каждого поддерживаемого метода.
        var authSpan = buffer.Slice(2, authCount);

        rcvResult = await socket.ReceiveExactAsync(authSpan, ct);
        if (!rcvResult.ReceiveSuccess)
        {
            return new SocksAuthRequest(authMethods: null);
        }

        var authMethods = new Socks5AuthMethod[authSpan.Length];
        for (var i = 0; i < authSpan.Length; i++)
        {
            var a = (Socks5AuthMethod)authSpan.Span[i];
            if (Enum.IsDefined(a))
            {
                authMethods[i] = a;
            }
            else
            {
                ThrowHelper.ThrowException(new InvalidOperationException("Ошибка протокола SOCKS 5"));
            }
        }

        return new SocksAuthRequest(authMethods);
    }

    public static async Task<Socks5LoginPassword> ReceiveSocks5Login(this Socket socket, Memory<byte> buffer, CancellationToken ct = default)
    {
        const int MaximumSize = 513;

        Debug.Assert(buffer.Length >= MaximumSize);

        SocketReceiveResult rcvResult = await socket.Receive2(buffer, ct).ConfigureAwait(false);
        if (!rcvResult.ReceiveSuccess)
        {
            return default;
        }

        return Parse(buffer.Span);

        static Socks5LoginPassword Parse(Span<byte> buffer)
        {
            var version = buffer[0];
            if (version != 1)
            {
                ThrowHelper.InvalidSocks5Version(version);
            }

            var ulen = buffer[1];
            buffer = buffer[2..];

            string login = Encoding.UTF8.GetString(buffer[..ulen]);

            buffer = buffer[ulen..];
            var plen = buffer[0];

            string password = Encoding.UTF8.GetString(buffer.Slice(1, plen));

            return new Socks5LoginPassword(login, password);
        }
    }

    public static async ValueTask<SocksRequest> ReceiveSocks5Request(this Socket socket, Memory<byte> buffer, CancellationToken ct = default)
    {
        const int MaximumSize = 262;

        Debug.Assert(buffer.Length >= MaximumSize);

        // Как минимум должно быть 4 байта.
        var rcvResult = await socket.ReceiveExactAsync(buffer[..4], ct).ConfigureAwait(false);
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
        if (!Enum.IsDefined(command))
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
                    rcvResult = await socket.ReceiveExactAsync(ipv4span, ct);
                    if (!rcvResult.ReceiveSuccess)
                    {
                        return default;
                    }

                    ipAddress = new IPAddress(ipv4span.Span);

                    // Сдвинуть бефер вперёд.
                    buffer = buffer[4..];

                    break;
                }
            case AddressType.DomainName:
                {
                    // 1 байт с длиной строки.
                    rcvResult = await socket.Receive2(buffer[..1], ct);
                    if (!rcvResult.ReceiveSuccess)
                    {
                        return default;
                    }

                    // Размер строки в байтах.
                    var strLen = buffer.Span[0];

                    var hostSpan = buffer.Slice(1, strLen);

                    // Читаем всю строку.
                    rcvResult = await socket.ReceiveExactAsync(hostSpan, ct);
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
                    rcvResult = await socket.ReceiveExactAsync(ipv6span, ct);
                    if (!rcvResult.ReceiveSuccess)
                    {
                        return default;
                    }

                    ipAddress = new IPAddress(ipv6span.Span);

                    // Сдвинуть бефер вперёд.
                    buffer = buffer[16..];

                    break;
                }
            default:
                ThrowHelper.ThrowInvalidOperationException("Ошибка протокола SOCKS 5");
                break;
        }

        // Последний сегмент — 2 байта, номер порта.
        var portSpan = buffer[..2];
        rcvResult = await socket.ReceiveExactAsync(portSpan, ct);
        if (!rcvResult.ReceiveSuccess)
        {
            return default;
        }

        var port = (ushort)((portSpan.Span[0] << 8) | portSpan.Span[1]);

        return new SocksRequest(command, address, ipAddress, domainName, port);
    }
}
