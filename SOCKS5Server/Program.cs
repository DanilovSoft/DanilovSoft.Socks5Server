using Microsoft.Extensions.Configuration;
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

        CancellationTokenSource cts = new();
        CancellationToken cancellationToken = cts.Token;
        Console.CancelKeyPress += (_, e) =>
        {
            Console.WriteLine("Canceling...");
            cts.Cancel();
            e.Cancel = true;
        };

        await ListenAsync(port, cancellationToken);
    }

    private static async Task ListenAsync(int port, CancellationToken ct)
    {
        using var listener = new Socks5Listener(port);
        Console.WriteLine($"Listening port {listener.Port}");

        await listener.ListenAsync(ct);
    }
}
