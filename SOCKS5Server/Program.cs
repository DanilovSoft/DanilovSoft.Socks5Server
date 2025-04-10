﻿using Microsoft.Extensions.Configuration;
using System.Diagnostics.CodeAnalysis;

namespace DanilovSoft.Socks5Server;

class Program
{
    [RequiresUnreferencedCode("Calls Microsoft.Extensions.Configuration.ConfigurationBinder.GetValue<T>(String)")]
    private static async Task Main(string[] args)
    {
        ConfigurationBuilder config = new();

        var baseDir = Directory.GetParent(AppContext.BaseDirectory);
        if (baseDir != null)
        {
            config.SetBasePath(baseDir.FullName);
        }

        var configuration = config
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables()
            .AddCommandLine(args)
            .Build();

        int port = configuration.GetValue<int>("Port");

        try
        {
            await RunServer(port);
        }
        catch (OperationCanceledException)
        {
            return;
        }
    }

    private static async Task RunServer(int port)
    {
        using var server = new Socks5Server(port);
        Console.WriteLine($"Listening port {server.Port}");

        CancellationTokenSource cts = new();
        CancellationToken cancellationToken = cts.Token;
        Console.CancelKeyPress += (_, e) =>
        {
            if (server.ActiveConnections is { } connections && connections > 0)
            {
                Console.WriteLine($"Closing {connections} connections...");
            }
            else
            {
                Console.WriteLine("Stopping...");
            }
            cts.Cancel();
            e.Cancel = true;
        };

        bool stoppedGracefully = await server.RunAsync(cancellationToken);

        Console.WriteLine(stoppedGracefully ? "Stopped gracefully!" : "Stopped abnormally");
    }
}
