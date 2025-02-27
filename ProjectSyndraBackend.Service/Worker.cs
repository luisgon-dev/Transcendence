using Hangfire;
using ProjectSyndraBackend.Service.Services.Recurring_Jobs;

namespace ProjectSyndraBackend.Service;

public class Worker(ILogger<Worker> logger, IServiceScopeFactory scopeFactory) : BackgroundService
{
    private BackgroundJobServer server;

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Hangfire server starting");
        server = new BackgroundJobServer();
        return base.StartAsync(cancellationToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        server.Dispose();
        await base.StopAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // init the hangfire server
        while (!stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Hangfire server running at, {time}", DateTimeOffset.Now);

            // wait for 1 minute
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}