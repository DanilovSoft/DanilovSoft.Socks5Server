using System.Net;
using System.Runtime.CompilerServices;

namespace DanilovSoft.Socks5Server;

internal static class ExtensionMethods
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsLoopback(this IPEndPoint endPoint)
    {
        return endPoint.Equals(IPAddress.Loopback) || endPoint.Equals(IPAddress.IPv6Loopback);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsLoopback(this DnsEndPoint endPoint)
    {
        return "localhost".Equals(endPoint.Host, StringComparison.OrdinalIgnoreCase);
    }
}
