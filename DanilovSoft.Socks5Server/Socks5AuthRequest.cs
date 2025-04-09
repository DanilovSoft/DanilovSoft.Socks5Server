using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using DanilovSoft.Socks5Server.TcpSocket;

namespace DanilovSoft.Socks5Server;

/// <summary>
/// Максимальный размер 257 байт.
/// </summary>
[StructLayout(LayoutKind.Auto)]
[DebuggerDisplay(@"\{default = {this == default}\}")]
internal readonly struct Socks5AuthRequest : IEquatable<Socks5AuthRequest>
{
    public const int MaximumSize = 257;
    public Socks5AuthMethod[]? AuthMethods { get; }

    private Socks5AuthRequest(Socks5AuthMethod[]? authMethods)
    {
        AuthMethods = authMethods;
    }

    public static async ValueTask<Socks5AuthRequest> ReceiveAsync(ManagedTcpSocket managedTcp, Memory<byte> buffer, CancellationToken ct = default)
    {
        Debug.Assert(buffer.Length >= MaximumSize);

        // Как минимум должно быть 2 байта.
        var rcvResult = await managedTcp.ReceiveExactAsync(buffer[..2], ct).ConfigureAwait(false);
        if (!rcvResult.ReceiveSuccess)
        {
            return new Socks5AuthRequest(authMethods: null);
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

        rcvResult = await managedTcp.ReceiveExactAsync(authSpan, ct).ConfigureAwait(false);
        if (!rcvResult.ReceiveSuccess)
        {
            return new Socks5AuthRequest(authMethods: null);
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

        return new Socks5AuthRequest(authMethods);
    }

    public bool Equals([AllowNull] Socks5AuthRequest other) => AuthMethods == other.AuthMethods;
    public override bool Equals(object? obj) => obj is Socks5AuthRequest o && Equals(other: o);
    public override int GetHashCode() => AuthMethods?.GetHashCode() ?? 0;
    public static bool operator ==(in Socks5AuthRequest left, in Socks5AuthRequest right) => left.Equals(other: right);
    public static bool operator !=(in Socks5AuthRequest left, in Socks5AuthRequest right) => !(left == right);
}
