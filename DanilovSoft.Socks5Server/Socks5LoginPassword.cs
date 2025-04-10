using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net.Sockets;
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
internal readonly struct Socks5LoginPassword(string login, string password) : IEquatable<Socks5LoginPassword>
{
    private readonly bool _isInitialized = true;
    public readonly string? Login = login;
    public readonly string? Password = password;

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
