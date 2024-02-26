using Microsoft.Extensions.Configuration;
using System.Net;
using System.Net.Sockets;

namespace DanilovSoft.Socks5Server;

class Program
{
    public static IConfigurationRoot? configuration;

    private static async Task Main(string[] args)
    {
        var config = new ConfigurationBuilder();

        var baseDir = Directory.GetParent(AppContext.BaseDirectory);
        if (baseDir != null)
        {
            config.SetBasePath(baseDir.FullName);
        }

        configuration = config
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables()
            .AddCommandLine(args)
            .Build();

        //TestShutdown();

        var port = configuration.GetValue<int>("Port");
        //if (Environment.UserInteractive)
        //{
        //    using (var listener = new Socks5Listener(port))
        //    {
        //        Console.WriteLine($"Port: {listener.Port}");
        //        Task task = listener.ListenAsync(default);

        //        int left = Console.CursorLeft;
        //        int top = Console.CursorTop;
        //        while (!task.IsCompleted)
        //        {
        //            Console.SetCursorPosition(left, top);
        //            Console.WriteLine($"Connections: {listener.ConnectionsCount.ToString().PadRight(10)}");
        //            await Task.Delay(200);
        //        }
        //    }
        //}
        //else
        {
            using (var listener = new Socks5Listener(port))
            {
                await listener.ListenAsync(CancellationToken.None);
            }
        }
    }

    private static void TestShutdown()
    {
        Socket tcp = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        ManagedTcpSocket mTcp = new(tcp);
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
