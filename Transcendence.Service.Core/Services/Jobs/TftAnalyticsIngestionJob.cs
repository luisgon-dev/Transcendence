using Camille.Enums;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Transcendence.Data;
using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Core.Services.Jobs.Configuration;
using Transcendence.Service.Core.Services.RiotApi;
using Transcendence.Service.Core.Services.Tft.Interfaces;

namespace Transcendence.Service.Core.Services.Jobs;

[DisableConcurrentExecution(timeoutInSeconds: 20 * 60)]
public class TftAnalyticsIngestionJob(
    TranscendenceContext context,
    IBackgroundJobClient backgroundJobClient,
    IRefreshLockRepository refreshLockRepository,
    ITftSummonerBootstrapService bootstrapService,
    ITftStaticDataService staticDataService,
    IOptions<MultiRegionIngestionOptions> multiRegionOptions,
    ILogger<TftAnalyticsIngestionJob> logger)
{
    [Queue("tft-default")]
    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        await staticDataService.EnsureStaticDataAsync(ct);
        await bootstrapService.EnsureSeededFromTopLadderAsync(ct);

        var regions = multiRegionOptions.Value.Enabled
            ? multiRegionOptions.Value.Regions.Where(r => r.Enabled).Select(r => r.Region).ToList()
            : ["NA1", "EUW1", "EUN1", "KR"];

        foreach (var region in regions)
        {
            await ExecuteForRegionAsync(region, ct);
        }
    }

    [Queue("tft-default")]
    public async Task ExecuteForRegionAsync(string region, CancellationToken ct = default)
    {
        var candidates = await context.TftSummoners
            .AsNoTracking()
            .Where(summoner => summoner.PlatformRegion == region)
            .OrderBy(summoner => summoner.UpdatedAt)
            .Take(25)
            .Select(summoner => new { summoner.PlatformRegion, summoner.GameName, summoner.TagLine })
            .ToListAsync(ct);

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate.GameName) || string.IsNullOrWhiteSpace(candidate.TagLine))
                continue;

            if (!PlatformRouteParser.TryParse(candidate.PlatformRegion, out var platform))
                continue;

            var lockKey = TftRefreshLockKeys.BuildSummonerRefreshKey(platform, candidate.GameName, candidate.TagLine);
            if (!await refreshLockRepository.TryAcquireAsync(lockKey, TimeSpan.FromMinutes(15), ct))
                continue;

            backgroundJobClient.Enqueue<ITftSummonerRefreshJob>(job =>
                job.RefreshByRiotId(candidate.GameName, candidate.TagLine, platform, lockKey, null, CancellationToken.None));
        }

        logger.LogInformation("Queued TFT ingestion refreshes for {Count} summoners in {Region}.", candidates.Count, region);
    }
}
