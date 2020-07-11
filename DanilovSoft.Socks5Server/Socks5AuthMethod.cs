using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DanilovSoft.Socks5Server
{
    internal enum Socks5AuthMethod
    {
        NoAuth = 0x00,
        GSSAPI = 0x01,
        LoginAndPassword = 0x02,
        NotSupported = 0xFF
    }
}
