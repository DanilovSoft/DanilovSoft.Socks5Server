using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DanilovSoft.Socks5Server
{
    public sealed class Socks5ConnectionOpenedEventArgs : EventArgs
    {
        public string? UserName { get; }

        public Socks5ConnectionOpenedEventArgs(string? userName)
        {
            Console.WriteLine("OK");
            UserName = userName;
        }
    }
}
