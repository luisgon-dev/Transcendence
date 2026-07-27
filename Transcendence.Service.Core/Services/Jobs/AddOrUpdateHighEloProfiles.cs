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

            // Older versions treated every Master+ account as an OTP. Clear those unverified,
            // auto-created rows before rebuilding the roster from match evidence below.
            await context.TrackedProSummoners
                .Where(x =>
                    x.PlatformRegion == platform.ToString() &&
                    !x.IsPro &&
                    x.IsHighEloOtp &&
                    x.OtpEvaluatedAtUtc == null &&
                    x.ProName == null &&
                    x.TeamName == null)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.IsHighEloOtp, false)
                    .SetProperty(x => x.IsActive, false)
                    .SetProperty(x => x.Source, "riot-high-elo")
                    .SetProperty(x => x.UpdatedAtUtc, DateTime.UtcNow), stoppingToken);

            foreach (var summonerPuuid in summonerPuuids)
            {
                try
                {
                    var summoner =
                        await summonerService.GetSummonerByPuuidAsync(summonerPuuid, platform, stoppingToken);
                    await summonerRepository.AddOrUpdateSummonerAsync(summoner, stoppingToken);
                    var isOtp = await ReconcileTrackedOtpAsync(summoner, platform, stoppingToken);
                    pendingChanges++;
                    if (isOtp)
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

    private async Task<bool> ReconcileTrackedOtpAsync(
        Data.Models.LoL.Account.Summoner summoner,
        PlatformRoute platform,
        CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(summoner.Puuid))
            return false;

        var nowUtc = DateTime.UtcNow;
        var platformRegion = platform.ToString();
        var championIds = await context.MatchParticipants
            .AsNoTracking()
            .Where(participant =>
                participant.SummonerId == summoner.Id &&
                participant.Match.PlatformRegion == platformRegion &&
                participant.Match.Status == Data.Models.LoL.Match.FetchStatus.Success &&
                (participant.Match.QueueId == QueueCatalog.RankedSoloDuoQueueId ||
                 (participant.Match.QueueId == 0 &&
                  participant.Match.QueueType == QueueCatalog.RankedSoloDuoQueueId.ToString())))
            .OrderByDescending(participant => participant.Match.MatchDate)
            .Take(50)
            .Select(participant => participant.ChampionId)
            .ToListAsync(stoppingToken);
        var qualification = EvaluateOtp(championIds);
        var existing = await context.TrackedProSummoners
            .FirstOrDefaultAsync(
                x => x.Puuid == summoner.Puuid && x.PlatformRegion == platformRegion,
                stoppingToken);

        if (existing == null)
        {
            if (!qualification.IsQualified)
                return false;

            context.TrackedProSummoners.Add(new TrackedProSummoner
            {
                Id = Guid.NewGuid(),
                Puuid = summoner.Puuid,
                PlatformRegion = platformRegion,
                GameName = summoner.GameName,
                TagLine = summoner.TagLine,
                IsPro = false,
                IsHighEloOtp = true,
                IsActive = true,
                Source = "riot-high-elo",
                LastVerifiedAtUtc = nowUtc,
                OtpChampionId = qualification.ChampionId,
                OtpGames = qualification.ChampionGames,
                OtpSampleSize = qualification.SampleSize,
                OtpEvaluatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc,
                CreatedAtUtc = nowUtc
            });
            return true;
        }

        existing.GameName = summoner.GameName;
        existing.TagLine = summoner.TagLine;
        existing.IsHighEloOtp = qualification.IsQualified;
        if (qualification.IsQualified)
            existing.IsActive = true;
        else if (!existing.IsPro && existing.Source == "riot-high-elo")
            existing.IsActive = false;
        if (!existing.IsPro)
            existing.Source = "riot-high-elo";
        existing.LastVerifiedAtUtc = nowUtc;
        existing.OtpChampionId = qualification.ChampionId;
        existing.OtpGames = qualification.ChampionGames;
        existing.OtpSampleSize = qualification.SampleSize;
        existing.OtpEvaluatedAtUtc = nowUtc;
        existing.UpdatedAtUtc = nowUtc;
        return qualification.IsQualified;
    }

    public static OtpQualification EvaluateOtp(IReadOnlyCollection<int> championIds)
    {
        const int sampleSize = 50;
        const int requiredChampionGames = 30;
        if (championIds.Count < sampleSize)
            return new OtpQualification(false, null, 0, championIds.Count);

        var topChampion = championIds
            .Take(sampleSize)
            .GroupBy(championId => championId)
            .Select(group => new { ChampionId = group.Key, Games = group.Count() })
            .OrderByDescending(row => row.Games)
            .ThenBy(row => row.ChampionId)
            .First();
        return new OtpQualification(
            topChampion.Games >= requiredChampionGames,
            topChampion.ChampionId,
            topChampion.Games,
            sampleSize);
    }

    public sealed record OtpQualification(
        bool IsQualified,
        int? ChampionId,
        int ChampionGames,
        int SampleSize);
}
