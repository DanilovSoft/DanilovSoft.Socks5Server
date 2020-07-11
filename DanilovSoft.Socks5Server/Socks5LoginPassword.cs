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
    internal readonly struct Socks5LoginPassword : IEquatable<Socks5LoginPassword>
    {
        public const int MaximumSize = 513;

        private readonly bool IsInitialized;
        public readonly string? Login;
        public readonly string? Password;

        public static async Task<Socks5LoginPassword> ReceiveAsync(ManagedTcpSocket managedTcp, Memory<byte> buffer)
        {
            Debug.Assert(buffer.Length >= MaximumSize);

            SocketReceiveResult socErr = await managedTcp.ReceiveAsync(buffer).ConfigureAwait(false);
            if (socErr.Count == 0)
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

        public bool Equals([AllowNull] Socks5LoginPassword other)
        {
            return other.IsInitialized == IsInitialized;
        }

        public Socks5LoginPassword(string login, string password)
        {
            Login = login;
            Password = password;
            IsInitialized = true;
        }

        public override bool Equals(object? obj)
        {
            return obj is Socks5LoginPassword password && Equals(password);
        }

        public override int GetHashCode()
        {
            return IsInitialized.GetHashCode();
        }

        public static bool operator ==(Socks5LoginPassword left, Socks5LoginPassword right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(Socks5LoginPassword left, Socks5LoginPassword right)
        {
            return !(left == right);
        }
    }
}
