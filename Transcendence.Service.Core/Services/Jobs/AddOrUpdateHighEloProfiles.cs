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
    private const string SoloQueueType = "RANKED_SOLO_5x5";
    private static readonly TimeSpan FullRefreshInterval = TimeSpan.FromHours(24);

    // One run at a time across both entry points. A pass over every region outlasts the two-hour cron,
    // and the old per-region 30-minute lock lapsed mid-region, so runs stacked up until they held all
    // eight main-pool workers and starved the default queue, ingestion dispatcher included. The lease is
    // renewed as the run progresses, so it only lapses when the worker died mid-run.
    private const string RunLockKey = "high-elo-refresh";
    private static readonly TimeSpan RunLockLease = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan RunLockReleaseTimeout = TimeSpan.FromSeconds(5);

    internal sealed record ApexLeagueState(
        string Puuid, string Tier, string Division, int LeaguePoints, int Wins, int Losses);

    [Queue(HangfireQueues.RefreshLow)]
    public async Task Execute(CancellationToken stoppingToken)
    {
        if (await TryAcquireRunAsync(stoppingToken) is not { } runOwner)
            return;

        try
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

                    if (!await ExecuteForPlatformAsync(platform, runOwner, stoppingToken))
                        return;
                }
            }
            else
            {
                await ExecuteForPlatformAsync(PlatformRoute.NA1, runOwner, stoppingToken);
            }
        }
        finally
        {
            await ReleaseRunAsync(runOwner);
        }
    }

    [Queue(HangfireQueues.RefreshLow)]
    public async Task ExecuteForRegionAsync(string region, CancellationToken stoppingToken)
    {
        if (!PlatformRouteParser.TryParse(region, out var platform))
        {
            logger.LogWarning(
                "High-elo profile refresh skipping: invalid platform region {PlatformRegion}.",
                region);
            return;
        }

        if (await TryAcquireRunAsync(stoppingToken) is not { } runOwner)
            return;

        try
        {
            await ExecuteForPlatformAsync(platform, runOwner, stoppingToken);
        }
        finally
        {
            await ReleaseRunAsync(runOwner);
        }
    }

    private async Task<Guid?> TryAcquireRunAsync(CancellationToken stoppingToken)
    {
        var runOwner = await refreshLockRepository.TryAcquireOwnedAsync(RunLockKey, RunLockLease, stoppingToken);
        if (runOwner is null)
            logger.LogInformation("High-elo profile refresh skipped: the previous run is still in progress.");
        return runOwner;
    }

    private async Task<bool> TryRenewRunAsync(Guid runOwner, PlatformRoute platform, CancellationToken stoppingToken)
    {
        if (await refreshLockRepository.TryRenewOwnedAsync(RunLockKey, runOwner, RunLockLease, stoppingToken))
            return true;

        logger.LogWarning("High-elo profile refresh stopped at {Platform}: another run took over its lock.", platform);
        return false;
    }

    private async Task ReleaseRunAsync(Guid runOwner)
    {
        // Not the job token: a run cancelled by shutdown should still free the lock for the next one.
        using var releaseTimeout = new CancellationTokenSource(RunLockReleaseTimeout);
        try
        {
            await refreshLockRepository.ReleaseOwnedAsync(RunLockKey, runOwner, releaseTimeout.Token);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "High-elo profile refresh failed to release lock {LockKey}.", RunLockKey);
        }
    }

    // Returns false when the run lost its lock and must stop.
    private async Task<bool> ExecuteForPlatformAsync(
        PlatformRoute platform,
        Guid runOwner,
        CancellationToken stoppingToken)
    {
        if (!await TryRenewRunAsync(runOwner, platform, stoppingToken))
            return false;

        const int saveBatchSize = 50;
        var pendingChanges = 0;
        var rosterUpdates = 0;

        var challengerLeague = await riotApiContext.Api.LeagueV4()
            .GetChallengerLeagueAsync(platform, QueueType.RANKED_SOLO_5x5, stoppingToken);
        var grandmasterLeague = await riotApiContext.Api.LeagueV4()
            .GetGrandmasterLeagueAsync(platform, QueueType.RANKED_SOLO_5x5, stoppingToken);
        var masterLeague = await riotApiContext.Api.LeagueV4()
            .GetMasterLeagueAsync(platform, QueueType.RANKED_SOLO_5x5, stoppingToken);

        var leagueStates = new[] { challengerLeague, grandmasterLeague, masterLeague }
            .SelectMany(league => league.Entries.Select(entry => new ApexLeagueState(
                entry.Puuid, league.Tier.ToString(), entry.Rank.ToString(), entry.LeaguePoints, entry.Wins, entry.Losses)))
            .Where(state => !string.IsNullOrWhiteSpace(state.Puuid))
            .GroupBy(state => state.Puuid, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var unchanged = await FindUnchangedSinceLastRefreshAsync(
            context, leagueStates, DateTime.UtcNow, stoppingToken);
        var summonerPuuids = leagueStates.Keys.Where(puuid => !unchanged.Contains(puuid)).ToList();

        logger.LogInformation(
            "High-elo profile refresh starting for {Platform}: {Count} summoners to process, {Skipped} unchanged since their last refresh.",
            platform,
            summonerPuuids.Count,
            unchanged.Count);

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
            if (!await TryRenewRunAsync(runOwner, platform, stoppingToken))
                return false;
        }

        if (pendingChanges > 0)
            await context.SaveChangesAsync(stoppingToken);

        logger.LogInformation(
            "High-elo profile refresh completed for {Platform}. UpdatedSummoners={UpdatedSummoners}, TrackedRosterUpdates={TrackedRosterUpdates}.",
            platform,
            summonerPuuids.Count,
            rosterUpdates);
        return true;
    }

    // Apex players whose ladder entry (tier, division, LP, wins, losses) is exactly what we stored at
    // their last refresh have not played a ranked game since, so re-fetching their profile would change
    // nothing -- yet it cost three Riot calls each (summoner, account, league), every two hours, for
    // ~11K players on EUW alone. That spent the same per-region budget match ingestion draws from:
    // ~22 Riot calls per ingested match on prod. Each player is still re-fetched at least once a day,
    // which picks up renames, profile icons and any late-ingested games for the one-trick evaluation.
    internal static async Task<HashSet<string>> FindUnchangedSinceLastRefreshAsync(
        TranscendenceContext context,
        IReadOnlyDictionary<string, ApexLeagueState> leagueStates,
        DateTime nowUtc,
        CancellationToken stoppingToken)
    {
        var refreshedSince = nowUtc - FullRefreshInterval;
        var unchanged = new HashSet<string>(StringComparer.Ordinal);
        foreach (var chunk in leagueStates.Keys.Chunk(1_000))
        {
            var stored = await context.Summoners.AsNoTracking()
                .Where(summoner => chunk.Contains(summoner.Puuid) && summoner.UpdatedAt >= refreshedSince)
                .Select(summoner => new
                {
                    summoner.Puuid,
                    Solo = summoner.Ranks
                        .Where(rank => rank.QueueType == SoloQueueType)
                        .Select(rank => new { rank.Tier, rank.RankNumber, rank.LeaguePoints, rank.Wins, rank.Losses })
                        .FirstOrDefault()
                })
                .ToListAsync(stoppingToken);
            foreach (var row in stored)
            {
                var current = leagueStates[row.Puuid!];
                if (row.Solo is { } solo &&
                    solo.Tier == current.Tier &&
                    solo.RankNumber == current.Division &&
                    solo.LeaguePoints == current.LeaguePoints &&
                    solo.Wins == current.Wins &&
                    solo.Losses == current.Losses)
                {
                    unchanged.Add(row.Puuid!);
                }
            }
        }

        return unchanged;
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
