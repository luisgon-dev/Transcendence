using Camille.Enums;
using Camille.RiotGames;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Account;
using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Core.Services.Jobs.Configuration;
using Transcendence.Service.Core.Services.RiotApi;
using Transcendence.Service.Core.Services.RiotApi.Interfaces;

namespace Transcendence.Service.Core.Services.Jobs;

// ReSharper disable once ClassNeverInstantiated.Global
public class AddOrUpdateHighEloProfiles(
    LeagueRiotApiContext riotApiContext,
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
            var rosterUpdates = 0;

            var challengerLeague = await riotApiContext.Api.LeagueV4()
                .GetChallengerLeagueAsync(platform, QueueType.RANKED_SOLO_5x5, stoppingToken);
            var grandmasterLeague = await riotApiContext.Api.LeagueV4()
                .GetGrandmasterLeagueAsync(platform, QueueType.RANKED_SOLO_5x5, stoppingToken);
            var masterLeague = await riotApiContext.Api.LeagueV4()
                .GetMasterLeagueAsync(platform, QueueType.RANKED_SOLO_5x5, stoppingToken);

            var summonerPuuids = challengerLeague.Entries.Select(x => x.Puuid)
                .Concat(grandmasterLeague.Entries.Select(x => x.Puuid))
                .Concat(masterLeague.Entries.Select(x => x.Puuid))
                .Where(puuid => !string.IsNullOrWhiteSpace(puuid))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            logger.LogInformation(
                "High-elo profile refresh starting for {Platform}: {Count} summoners to process.",
                platform,
                summonerPuuids.Count);

            foreach (var summonerPuuid in summonerPuuids)
            {
                try
                {
                    var summoner =
                        await summonerService.GetSummonerByPuuidAsync(summonerPuuid, platform, stoppingToken);
                    await summonerRepository.AddOrUpdateSummonerAsync(summoner, stoppingToken);
                    await UpsertTrackedHighValueSummonerAsync(summoner, platform, stoppingToken);
                    pendingChanges++;
                    rosterUpdates++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Isolate per summoner: a single account that fails to enrich (e.g. a Riot payload the
                    // client can't parse, or a dead riot-id) must not abort the whole platform's refresh.
                    logger.LogWarning(ex,
                        "High-elo profile refresh skipped summoner {Puuid} on {Platform}.", summonerPuuid, platform);
                    continue;
                }

                if (pendingChanges < saveBatchSize)
                    continue;

                await context.SaveChangesAsync(stoppingToken);
                pendingChanges = 0;
            }

            if (pendingChanges > 0)
                await context.SaveChangesAsync(stoppingToken);

            logger.LogInformation(
                "High-elo profile refresh completed for {Platform}. UpdatedSummoners={UpdatedSummoners}, TrackedRosterUpdates={TrackedRosterUpdates}.",
                platform,
                summonerPuuids.Count,
                rosterUpdates);
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

    private async Task UpsertTrackedHighValueSummonerAsync(
        Data.Models.LoL.Account.Summoner summoner,
        PlatformRoute platform,
        CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(summoner.Puuid))
            return;

        var nowUtc = DateTime.UtcNow;
        var platformRegion = platform.ToString();
        var existing = await context.TrackedProSummoners
            .FirstOrDefaultAsync(
                x => x.Puuid == summoner.Puuid && x.PlatformRegion == platformRegion,
                stoppingToken);

        if (existing == null)
        {
            context.TrackedProSummoners.Add(new TrackedProSummoner
            {
                Id = Guid.NewGuid(),
                Puuid = summoner.Puuid,
                PlatformRegion = platformRegion,
                GameName = summoner.GameName,
                TagLine = summoner.TagLine,
                IsPro = false,
                // These are auto-discovered Challenger/GM/Master players — flag them so the
                // "high-elo" / "all" pro-analytics scopes can include them (IsPro stays curated).
                IsHighEloOtp = true,
                IsActive = true,
                UpdatedAtUtc = nowUtc,
                CreatedAtUtc = nowUtc
            });
            return;
        }

        existing.GameName = summoner.GameName;
        existing.TagLine = summoner.TagLine;
        existing.IsHighEloOtp = true;
        existing.IsActive = true;
        existing.UpdatedAtUtc = nowUtc;
    }
}
