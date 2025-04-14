using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DanilovSoft.Socks5Server;

class Program
{
    private static Task Main()
    {
        var host = new HostBuilder()
            .UseConsoleLifetime()
            .ConfigureAppConfiguration((context, configurationBuilder) =>
            {
                configurationBuilder
                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false);
            })
            .ConfigureLogging((hostBuilder, loggingBuilder) => 
            {
                loggingBuilder.AddConfiguration(hostBuilder.Configuration.GetSection("Logging"));
                loggingBuilder.AddSimpleConsole(c => 
                {
                    c.SingleLine = true;
                    c.IncludeScopes = false;
                });
            })
            .ConfigureServices((context, services) =>
            {
                services.AddHostedService<SocksBackgroundService>();
            })
            .Build();

        return host.RunAsync();
    }
}
