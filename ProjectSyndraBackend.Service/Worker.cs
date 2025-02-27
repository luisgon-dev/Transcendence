using Hangfire;
using ProjectSyndraBackend.Service.Services.Recurring_Jobs;

namespace ProjectSyndraBackend.Service;

public class Worker(ILogger<Worker> logger) : BackgroundService
{

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // init the hangfire server
        while (!stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("TEST, {time}", DateTimeOffset.Now);

            // wait for 1 minute
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}