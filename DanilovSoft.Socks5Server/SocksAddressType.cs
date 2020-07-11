using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DanilovSoft.Socks5Server
{
    internal enum AddressType
    {
        IPv4 = 0x01,
        DomainName = 0x03,
        IPv6 = 0x04
    }
}
