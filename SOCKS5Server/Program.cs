using System;

namespace DanilovSoft.Socks5Server
{
    class Program
    {
        static void Main()
        {
            using (var listener = new Socks5Listener(1080))
            {
                Console.WriteLine("Running");
                listener.ListenAsync(default).GetAwaiter().GetResult();
            }
        }
    }
}
