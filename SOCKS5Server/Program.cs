using System;

namespace DanilovSoft.Socks5Server
{
    class Program
    {
        static void Main()
        {
            const int port = 1080;
            using (var listener = new Socks5Listener(port))
            {
                Console.WriteLine($"Port: {port}");
                listener.ListenAsync(default).GetAwaiter().GetResult();
            }
        }
    }
}
