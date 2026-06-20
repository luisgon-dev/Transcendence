using Camille.Enums;
using Camille.RiotGames;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Transcendence.Data;
using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Core.Services.Jobs.Configuration;
using Transcendence.Service.Core.Services.RiotApi;
using Transcendence.Service.Core.Services.Tft.Interfaces;

namespace Transcendence.Service.Core.Services.Tft.Implementations;

public class TftSummonerBootstrapService(
    TftRiotApiContext riotApiContext,
    TranscendenceContext context,
    ITftSummonerService summonerService,
    ITftSummonerRepository summonerRepository,
    IRefreshLockRepository refreshLockRepository,
    IOptions<MultiRegionIngestionOptions> multiRegionOptions,
    IRiotRateGate rateGate,
    ILogger<TftSummonerBootstrapService> logger) : ITftSummonerBootstrapService
{
    private const string RankedTftQueue = "RANKED_TFT";

    public async Task EnsureSeededFromTopLadderAsync(CancellationToken ct = default)
    {
        var hasTrackedSummoners = await context.TftSummoners.AsNoTracking().AnyAsync(ct);
        if (hasTrackedSummoners)
            return;

        var regions = multiRegionOptions.Value.Enabled
            ? multiRegionOptions.Value.Regions.Where(r => r.Enabled).Select(r => r.Region).ToList()
            : ["NA1", "EUW1", "EUN1", "KR"];

        foreach (var region in regions)
        {
            if (!PlatformRouteParser.TryParse(region, out var platform))
            {
                logger.LogWarning("Skipping TFT bootstrap for invalid region {Region}.", region);
                continue;
            }

            await SeedPlatformAsync(platform, ct);
        }
    }

    [Queue("tft-refresh-low")]
    public async Task SeedPlatformAsync(PlatformRoute platform, CancellationToken ct = default)
    {
        var lockKey = $"tft:bootstrap:{platform}";
        if (!await refreshLockRepository.TryAcquireAsync(lockKey, TimeSpan.FromMinutes(30), ct))
            return;

        try
        {
            // Pace each league pull under the per-region Riot budget (shared gate with the LoL vertical),
            // keyed by the platform's regional route. If the budget stays exhausted past the gate's max wait,
            // skip seeding this platform this run — it is idempotent and lock-guarded, so a later run retries.
            var routingKey = platform.ToRegional().ToString();

            if (!await rateGate.AcquireAsync(routingKey, ct))
                return;
            var challengerLeague = await riotApiContext.Api.TftLeagueV1().GetChallengerLeagueAsync(platform, RankedTftQueue, ct);

            if (!await rateGate.AcquireAsync(routingKey, ct))
                return;
            var grandmasterLeague = await riotApiContext.Api.TftLeagueV1().GetGrandmasterLeagueAsync(platform, RankedTftQueue, ct);

            if (!await rateGate.AcquireAsync(routingKey, ct))
                return;
            var masterLeague = await riotApiContext.Api.TftLeagueV1().GetMasterLeagueAsync(platform, RankedTftQueue, ct);

            var puuids = challengerLeague.Entries.Select(entry => entry.Puuid)
                .Concat(grandmasterLeague.Entries.Select(entry => entry.Puuid))
                .Concat(masterLeague.Entries.Select(entry => entry.Puuid))
                .Where(puuid => !string.IsNullOrWhiteSpace(puuid))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            foreach (var puuid in puuids)
            {
                var summoner = await summonerService.GetSummonerByPuuidAsync(puuid, platform, ct);
                await summonerRepository.AddOrUpdateAsync(summoner, ct);
            }

            await context.SaveChangesAsync(ct);
            logger.LogInformation("Seeded {Count} TFT ladder summoners for {Platform}.", puuids.Count, platform);
        }
        finally
        {
            await refreshLockRepository.ReleaseAsync(lockKey, ct);
        }
    }
}
