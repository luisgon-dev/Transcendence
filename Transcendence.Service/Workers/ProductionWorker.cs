using Hangfire;
using Transcendence.Service.Core.Services.Jobs;

namespace Transcendence.Service.Workers;

public class ProductionWorker(
    ILogger<ProductionWorker> logger,
    IRecurringJobManager recurringJobManager) : BackgroundService
{
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        recurringJobManager.AddOrUpdate<UpdateStaticDataJob>("updateStaticData", x => x.Execute(CancellationToken.None),
            Cron.Daily);
        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
            // wait for 1 minute
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
    }
}