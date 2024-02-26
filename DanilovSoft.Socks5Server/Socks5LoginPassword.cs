using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;

namespace DanilovSoft.Socks5Server;

/// <summary>
/// +----+------+----------+------+----------+
/// |VER | ULEN |  UNAME   | PLEN |  PASSWD  |
/// +----+------+----------+------+----------+
/// | 1  |  1   | 1 to 255 |  1   | 1 to 255 |
/// +----+------+----------+------+----------+
/// </summary>
[StructLayout(LayoutKind.Auto)]
[DebuggerDisplay(@"\{default = {this == default}\}")]
internal readonly struct Socks5LoginPassword : IEquatable<Socks5LoginPassword>
{
    public const int MaximumSize = 513;

    private readonly bool _isInitialized;
    public readonly string? Login;
    public readonly string? Password;

    // ctor
    private Socks5LoginPassword(string login, string password)
    {
        Login = login;
        Password = password;
        _isInitialized = true;
    }

    public static async Task<Socks5LoginPassword> ReceiveAsync(ManagedTcpSocket managedTcp, Memory<byte> buffer)
    {
        Debug.Assert(buffer.Length >= MaximumSize);

        var rcvResult = await managedTcp.ReceiveAsync(buffer).ConfigureAwait(false);
        if (!rcvResult.ReceiveSuccess)
        {
            return default;
        }

        var version = buffer.Span[0];
        if (version != 1)
        {
            throw new InvalidOperationException($"Не верный номер версии. Получено {version}, ожидалось 1");
        }

        var ulen = buffer.Span[1];
        buffer = buffer.Slice(2);

        var login = Encoding.UTF8.GetString(buffer.Slice(0, ulen).Span);

        buffer = buffer.Slice(ulen);
        var plen = buffer.Span[0];
        var password = Encoding.UTF8.GetString(buffer.Slice(1, plen).Span);

        return new Socks5LoginPassword(login, password);
    }

    public bool Equals([AllowNull] Socks5LoginPassword other)
    {
        return _isInitialized == other._isInitialized;
    }

    public static bool operator ==(in Socks5LoginPassword left, in Socks5LoginPassword right)
    {
        return left.Equals(other: right);
    }

    public static bool operator !=(in Socks5LoginPassword left, in Socks5LoginPassword right)
    {
        return !(left == right);
    }

    public override bool Equals(object? obj)
    {
        return obj is Socks5LoginPassword o && Equals(other: o);
    }

    public override int GetHashCode()
    {
        return 0;
    }
}
