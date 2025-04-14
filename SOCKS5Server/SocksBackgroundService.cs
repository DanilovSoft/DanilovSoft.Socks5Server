using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DanilovSoft.Socks5Server;

internal sealed class SocksBackgroundService(ILogger<SocksBackgroundService> logger, IConfiguration configuration) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        int port = int.Parse(configuration.GetSection("Port").Value!);

        return RunServer(port, stoppingToken);
    }

    private async Task RunServer(int port, CancellationToken ct)
    {
        using var server = new SocksServer(port);
        logger.LogInformation("SOCKS5 v{Version}", typeof(SocksServer).Assembly.GetName().Version?.ToString(3) ?? "1.0.0");
        logger.LogInformation("Listening port {Port}", server.Port);

        bool stoppedGracefully = await server.RunAsync(ct);

        if (stoppedGracefully)
        {
            logger.LogInformation("Process stopped gracefully");
        }
        else
        {
            logger.LogInformation("Process stopped abnormally");
        }
    }
}
