using Microsoft.Extensions.Configuration;
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace DanilovSoft.Socks5Server
{
    class Program
    {
        public static IConfigurationRoot? configuration;

        static void Main(string[] args)
        {
            // Build configuration
            var config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetParent(AppContext.BaseDirectory).FullName)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                .AddEnvironmentVariables();

            if (args != null)
            {
                config.AddCommandLine(args);
            }
            configuration = config.Build();

            //TestShutdown();

            int port = configuration.GetValue<int>("Port");
            using (var listener = new Socks5Listener(port))
            {
                Console.WriteLine($"Port: {listener.Port}");
                Task task = listener.ListenAsync(default);

                int left = Console.CursorLeft;
                int top = Console.CursorTop;
                while (!task.IsCompleted)
                {
                    Console.SetCursorPosition(left, top);
                    Console.WriteLine($"Connections: {listener.ConnectionsCount.ToString().PadRight(10)}");
                    Thread.Sleep(200);
                }
            }
        }

        static void TestShutdown()
        {
            Socket tcp = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            var mTcp = new ManagedTcpSocket(tcp);
            mTcp.Client.Connect("google.com", 80);

            DelayShutdown(tcp);

            SocketReceiveResult n;
            try
            {
                n = mTcp.ReceiveAsync(new byte[1024]).AsTask().Result;
            }
            catch (Exception)
            {

            }
        }

        static void DelayShutdown(Socket tcp)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                Thread.Sleep(5_000);
                tcp.Disconnect(false);
            });
        }
    }
}
