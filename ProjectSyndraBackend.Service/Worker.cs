using Hangfire;

namespace ProjectSyndraBackend.Service;

public class Worker(ILogger<Worker> logger, IBackgroundJobClient backgroundJobClient) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // init the hangfire server
        while (!stoppingToken.IsCancellationRequested)
        {
            // wait for 1 minute
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}