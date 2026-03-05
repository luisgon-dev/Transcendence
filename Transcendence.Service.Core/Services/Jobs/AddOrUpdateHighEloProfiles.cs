using Camille.Enums;
using Camille.RiotGames;
using Hangfire;
using Microsoft.Extensions.Options;
using Transcendence.Data;
using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Core.Services.Jobs.Configuration;
using Transcendence.Service.Core.Services.RiotApi;
using Transcendence.Service.Core.Services.RiotApi.Interfaces;

namespace Transcendence.Service.Core.Services.Jobs;

// ReSharper disable once ClassNeverInstantiated.Global
public class AddOrUpdateHighEloProfiles(
    RiotGamesApi riotGamesApi,
    TranscendenceContext context,
    ILogger<AddOrUpdateHighEloProfiles> logger,
    ISummonerService summonerService,
    ISummonerRepository summonerRepository,
    IRefreshLockRepository refreshLockRepository,
    IOptions<MultiRegionIngestionOptions> multiRegionOptions)
{
    [Queue("refresh-low")]
    public async Task Execute(CancellationToken stoppingToken)
    {
        var multiRegion = multiRegionOptions.Value;

        if (multiRegion.Enabled && multiRegion.Regions.Count > 0)
        {
            var enabledRegions = multiRegion.Regions.Where(r => r.Enabled).ToList();
            foreach (var regionConfig in enabledRegions)
            {
                if (!PlatformRouteParser.TryParse(regionConfig.Region, out var platform))
                {
                    logger.LogWarning(
                        "High-elo profile refresh skipping region: invalid platform {PlatformRegion}.",
                        regionConfig.Region);
                    continue;
                }

                await ExecuteForPlatformAsync(platform, stoppingToken);
            }
        }
        else
        {
            await ExecuteForPlatformAsync(PlatformRoute.NA1, stoppingToken);
        }
    }

    [Queue("refresh-low")]
    public async Task ExecuteForRegionAsync(string region, CancellationToken stoppingToken)
    {
        if (!PlatformRouteParser.TryParse(region, out var platform))
        {
            logger.LogWarning(
                "High-elo profile refresh skipping: invalid platform region {PlatformRegion}.",
                region);
            return;
        }

        await ExecuteForPlatformAsync(platform, stoppingToken);
    }

    private async Task ExecuteForPlatformAsync(PlatformRoute platform, CancellationToken stoppingToken)
    {
        var lockKey = $"high-elo-refresh:{platform}";
        var lockTtl = TimeSpan.FromMinutes(30);

        var acquired = await refreshLockRepository.TryAcquireAsync(lockKey, lockTtl, stoppingToken);
        if (!acquired)
        {
            logger.LogDebug("High-elo profile refresh skipped for {Platform}: lock held.", platform);
            return;
        }

        try
        {
            const int saveBatchSize = 50;
            var pendingChanges = 0;

            var challengerLeague = await riotGamesApi.LeagueV4()
                .GetChallengerLeagueAsync(platform, QueueType.RANKED_SOLO_5x5, stoppingToken);
            var grandmasterLeague = await riotGamesApi.LeagueV4()
                .GetGrandmasterLeagueAsync(platform, QueueType.RANKED_SOLO_5x5, stoppingToken);
            var masterLeague = await riotGamesApi.LeagueV4()
                .GetMasterLeagueAsync(platform, QueueType.RANKED_SOLO_5x5, stoppingToken);

            var summonerPuuids = challengerLeague.Entries.Select(x => x.Puuid)
                .Concat(grandmasterLeague.Entries.Select(x => x.Puuid))
                .Concat(masterLeague.Entries.Select(x => x.Puuid))
                .ToList();

            logger.LogInformation(
                "High-elo profile refresh starting for {Platform}: {Count} summoners to process.",
                platform,
                summonerPuuids.Count);

            foreach (var summonerPuuid in summonerPuuids)
            {
                var summoner =
                    await summonerService.GetSummonerByPuuidAsync(summonerPuuid, platform, stoppingToken);
                await summonerRepository.AddOrUpdateSummonerAsync(summoner, stoppingToken);
                logger.LogInformation("Summoner {SummonerName} added or updated on {Platform}",
                    summoner.SummonerName, platform);
                pendingChanges++;

                if (pendingChanges < saveBatchSize)
                    continue;

                await context.SaveChangesAsync(stoppingToken);
                pendingChanges = 0;
            }

            if (pendingChanges > 0)
                await context.SaveChangesAsync(stoppingToken);

            logger.LogInformation("All summoners added or updated for {Platform}", platform);
        }
        finally
        {
            try
            {
                await refreshLockRepository.ReleaseAsync(lockKey, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "High-elo profile refresh failed to release lock {LockKey}.", lockKey);
            }
        }
    }
}
