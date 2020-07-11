using System;
using System.Collections.Generic;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;

namespace DanilovSoft.Socks5Server
{
    internal static class GlobalVars
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsNotLoopback(this IPEndPoint endPoint)
        {
            return !endPoint.Equals(IPAddress.Loopback)
                && !endPoint.Equals(IPAddress.IPv6Loopback);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsNotLoopback(this DnsEndPoint endPoint)
        {
            return !"localhost".Equals(endPoint.Host, StringComparison.OrdinalIgnoreCase);
        }
    }
}
