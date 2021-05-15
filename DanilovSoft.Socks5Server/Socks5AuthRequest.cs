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
    /// Максимальный размер 257 байт.
    /// </summary>
    [StructLayout(LayoutKind.Auto)]
    [DebuggerDisplay(@"\{default = {this == default}\}")]
    internal readonly struct Socks5AuthRequest : IEquatable<Socks5AuthRequest>
    {
        public const int MaximumSize = 257;
        public Socks5AuthMethod[]? AuthMethods { get; }

        // ctor.
        public Socks5AuthRequest(ReadOnlySpan<byte> span)
        {
            if (span.Length < 3)
                throw new InvalidOperationException($"Ожидалось получить больше 2 байта, получено: {span.Length}");

            byte version = span[0];
            if (version != 5)
                throw new InvalidOperationException($"Не верный номер версии Socks. Получено {version}, ожидалось 5");

            // Количество поддерживаемых методов аутентификации.
            byte authCount = span[1];

            // Номера методов аутентификации, переменная длина, 1 байт для каждого поддерживаемого метода.
            ReadOnlySpan<byte> auth = span.Slice(2);

            AuthMethods = (new Socks5AuthMethod[auth.Length]);
            for (int i = 0; i < auth.Length; i++)
            {
                var a = (Socks5AuthMethod)auth[i];
                if (!Enum.IsDefined(typeof(Socks5AuthMethod), a))
                    throw new InvalidOperationException("Ошибка протокола SOCKS 5");

                AuthMethods[i] = a;
            }
        }

        // ctor.
        private Socks5AuthRequest(Socks5AuthMethod[]? authMethods)
        {
            AuthMethods = authMethods;
        }

        public static async ValueTask<Socks5AuthRequest> ReceiveAsync(ManagedTcpSocket managedTcp, Memory<byte> buffer)
        {
            Debug.Assert(buffer.Length >= MaximumSize);

            // Как минимум должно быть 2 байта.
            SocketReceiveResult rcvResult = await managedTcp.ReceiveBlockAsync(buffer.Slice(0, 2)).ConfigureAwait(false);
            if (!rcvResult.ReceiveSuccess)
                return new Socks5AuthRequest(authMethods: null);

            byte version = buffer.Span[0];
            if (version != 5)
                throw new InvalidOperationException($"Не верный номер версии Socks. Получено {version}, ожидалось 5");

            // Количество поддерживаемых методов аутентификации.
            byte authCount = buffer.Span[1];

            // Номера методов аутентификации, переменная длина, 1 байт для каждого поддерживаемого метода.
            Memory<byte> authSpan = buffer.Slice(2, authCount);

            rcvResult = await managedTcp.ReceiveBlockAsync(authSpan).ConfigureAwait(false);
            if (!rcvResult.ReceiveSuccess)
                return new Socks5AuthRequest(authMethods: null);

            var authMethods = new Socks5AuthMethod[authSpan.Length];
            for (int i = 0; i < authSpan.Length; i++)
            {
                Socks5AuthMethod a = (Socks5AuthMethod)authSpan.Span[i];
                if (Enum.IsDefined(typeof(Socks5AuthMethod), a))
                {
                    authMethods[i] = a;
                }
                else
                {
                    ThrowHelper.ThrowException(new InvalidOperationException("Ошибка протокола SOCKS 5"));
                }
            }

            return new Socks5AuthRequest(authMethods);
        }

        public bool Equals([AllowNull] Socks5AuthRequest other)
        {
            return AuthMethods == other.AuthMethods;
        }

        public override bool Equals(object? obj)
        {
            return obj is Socks5AuthRequest o && Equals(other: o);
        }

        public override int GetHashCode()
        {
            return AuthMethods?.GetHashCode() ?? 0;
        }

        public static bool operator ==(in Socks5AuthRequest left, in Socks5AuthRequest right)
        {
            return left.Equals(other: right);
        }

        public static bool operator !=(in Socks5AuthRequest left, in Socks5AuthRequest right)
        {
            return !(left == right);
        }
    }
}
