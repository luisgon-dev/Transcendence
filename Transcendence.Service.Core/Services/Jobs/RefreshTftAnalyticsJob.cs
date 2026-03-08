using Hangfire;
using Transcendence.Service.Core.Services.Tft.Interfaces;

namespace Transcendence.Service.Core.Services.Jobs;

[DisableConcurrentExecution(timeoutInSeconds: 30 * 60)]
public class RefreshTftAnalyticsJob(
    ITftStaticDataService staticDataService,
    ITftAnalyticsService analyticsService,
    ILogger<RefreshTftAnalyticsJob> logger)
{
    [Queue("tft-default")]
    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        await staticDataService.EnsureStaticDataAsync(ct);
        await analyticsService.InvalidateCacheAsync(ct);

        await analyticsService.GetRegionsAsync(ct);
        await analyticsService.GetChampionsAsync(ct);
        await analyticsService.GetItemsAsync(ct);
        await analyticsService.GetTraitsAsync(ct);
        await analyticsService.GetAugmentsAsync(ct);
        await analyticsService.GetCompListAsync(null, null, ct);

        logger.LogInformation("TFT analytics cache refresh completed.");
    }
}
