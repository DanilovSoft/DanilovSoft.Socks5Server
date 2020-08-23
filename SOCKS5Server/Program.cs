using System;
using System.Threading;

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
                var task = listener.ListenAsync(default);

                var left = Console.CursorLeft;
                var top = Console.CursorTop;
                while (!task.IsCompleted)
                {
                    Console.SetCursorPosition(left, top);
                    Console.WriteLine($"Connections: {listener.ConnectionsCount.ToString().PadRight(10)}");
                    Thread.Sleep(200);
                }
            }
        }
    }
}
