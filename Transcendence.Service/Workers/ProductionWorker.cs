using Hangfire;
using Transcendence.Service.Services.Jobs;
using Transcendence.Service.Services.RiotApi.Interfaces;

namespace Transcendence.Service.Workers;

public class ProductionWorker(
    ILogger<ProductionWorker> logger,
    IRecurringJobManager recurringJobManager,
    IMatchDataGatheringService gatheringService) : BackgroundService
{
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        gatheringService.Init();
        recurringJobManager.AddOrUpdate<UpdateStaticDataJob>("updateStaticData", x => x.Execute(CancellationToken.None), Cron.Daily);
        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // init the hangfire server
        while (!stoppingToken.IsCancellationRequested)
            // wait for 1 minute
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
    }
}