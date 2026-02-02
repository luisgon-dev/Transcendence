using Hangfire;
using Transcendence.Service.Core.Services.Jobs;

namespace Transcendence.Service.Workers;

public class ProductionWorker(
    ILogger<ProductionWorker> logger,
    IRecurringJobManager recurringJobManager) : BackgroundService
{
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        // Schedule patch detection every 6 hours
        recurringJobManager.AddOrUpdate<UpdateStaticDataJob>(
            "detect-patch",
            x => x.Execute(CancellationToken.None),
            "0 */6 * * *"); // Every 6 hours at minute 0

        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
            // wait for 1 minute
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
    }
}