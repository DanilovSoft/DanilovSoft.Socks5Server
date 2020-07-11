using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace DanilovSoft.Socks5Server
{
    /// <summary>
    /// +----+------+----------+------+----------+
    /// |VER | ULEN |  UNAME   | PLEN |  PASSWD  |
    /// +----+------+----------+------+----------+
    /// | 1  |  1   | 1 to 255 |  1   | 1 to 255 |
    /// +----+------+----------+------+----------+
    /// </summary>
    [StructLayout(LayoutKind.Auto)]
    internal readonly struct Socks5LoginPassword
    {
        public const int MaximumSize = 513;

        public readonly bool IsInitialized;
        public readonly string? Login;
        public readonly string? Password;

        public static async Task<Socks5LoginPassword> ReceiveAsync(ManagedTcpSocket managedTcp, Memory<byte> buffer)
        {
            Debug.Assert(buffer.Length >= MaximumSize);

            SocketReceiveResult rcvResult = await managedTcp.ReceiveAsync(buffer).ConfigureAwait(false);
            if (!rcvResult.ReceiveSuccess)
                return default;

            byte version = buffer.Span[0];
            if (version != 1)
                throw new InvalidOperationException($"Не верный номер версии. Получено {version}, ожидалось 1");

            byte ulen = buffer.Span[1];
            buffer = buffer.Slice(2);

            string login = Encoding.UTF8.GetString(buffer.Slice(0, ulen).Span);

            buffer = buffer.Slice(ulen);
            byte plen = buffer.Span[0];
            string password = Encoding.UTF8.GetString(buffer.Slice(1, plen).Span);

            return new Socks5LoginPassword(login, password);
        }

        public Socks5LoginPassword(string login, string password)
        {
            Login = login;
            Password = password;
            IsInitialized = true;
        }
    }
}
