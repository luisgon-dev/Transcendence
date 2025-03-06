using Hangfire;
using ProjectSyndraBackend.Service.Services;

namespace ProjectSyndraBackend.Service;

public class Worker(ILogger<Worker> logger, IBackgroundJobClient backgroundJobClient, IMatchDataGatheringService gatheringService) : BackgroundService
{
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        gatheringService.Init(); 
        return base.StartAsync(cancellationToken);
    }

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