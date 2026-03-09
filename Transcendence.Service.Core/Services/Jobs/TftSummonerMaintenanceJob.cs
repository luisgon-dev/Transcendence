using Camille.Enums;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Transcendence.Data;
using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Core.Services.RiotApi;
using Transcendence.Service.Core.Services.Tft.Interfaces;

namespace Transcendence.Service.Core.Services.Jobs;

[DisableConcurrentExecution(timeoutInSeconds: 20 * 60)]
public class TftSummonerMaintenanceJob(
    TranscendenceContext context,
    IBackgroundJobClient backgroundJobClient,
    IRefreshLockRepository refreshLockRepository,
    ILogger<TftSummonerMaintenanceJob> logger)
{
    [Queue("tft-refresh-low")]
    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var staleCutoffUtc = DateTime.UtcNow.AddHours(-12);
        var candidates = await context.TftSummoners
            .AsNoTracking()
            .Where(summoner => summoner.UpdatedAt <= staleCutoffUtc && summoner.GameName != null && summoner.TagLine != null)
            .OrderBy(summoner => summoner.UpdatedAt)
            .Take(25)
            .Select(summoner => new { summoner.PlatformRegion, summoner.GameName, summoner.TagLine })
            .ToListAsync(ct);

        var queued = 0;
        foreach (var candidate in candidates)
        {
            if (!PlatformRouteParser.TryParse(candidate.PlatformRegion, out var platform))
                continue;

            var lockKey = TftRefreshLockKeys.BuildSummonerRefreshKey(platform, candidate.GameName!, candidate.TagLine!);
            if (!await refreshLockRepository.TryAcquireAsync(lockKey, TimeSpan.FromMinutes(15), ct))
                continue;

            backgroundJobClient.Enqueue<ITftSummonerRefreshJob>(job =>
                job.RefreshByRiotId(candidate.GameName!, candidate.TagLine!, platform, lockKey, null, CancellationToken.None));
            queued++;
        }

        logger.LogInformation("Queued {Count} TFT maintenance refresh jobs.", queued);
    }
}
