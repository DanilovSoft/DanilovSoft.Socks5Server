using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace DanilovSoft.Socks5Server;

/// <summary>
/// Максимальный размер 257 байт.
/// </summary>
[StructLayout(LayoutKind.Auto)]
[DebuggerDisplay(@"\{default = {this == default}\}")]
internal readonly struct SocksAuthRequest(Socks5AuthMethod[]? authMethods) : IEquatable<SocksAuthRequest>
{
    public Socks5AuthMethod[]? AuthMethods { get; } = authMethods;

    public bool Equals([AllowNull] SocksAuthRequest other) => AuthMethods == other.AuthMethods;
    public override bool Equals(object? obj) => obj is SocksAuthRequest o && Equals(other: o);
    public override int GetHashCode() => AuthMethods?.GetHashCode() ?? 0;
    public static bool operator ==(in SocksAuthRequest left, in SocksAuthRequest right) => left.Equals(other: right);
    public static bool operator !=(in SocksAuthRequest left, in SocksAuthRequest right) => !(left == right);
}
