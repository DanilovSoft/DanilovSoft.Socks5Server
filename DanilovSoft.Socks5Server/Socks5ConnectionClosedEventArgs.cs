using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DanilovSoft.Socks5Server
{
    public sealed class Socks5ConnectionClosedEventArgs : EventArgs
    {
        public string? UserName { get; }

        public Socks5ConnectionClosedEventArgs(string? userName)
        {
            UserName = userName;
        }
    }
}
