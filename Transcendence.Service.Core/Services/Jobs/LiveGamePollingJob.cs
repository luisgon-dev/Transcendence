using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Transcendence.Data;
using Transcendence.Data.Models.LiveGame;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Core.Services.Jobs.Configuration;
using Transcendence.Service.Core.Services.Jobs.Interfaces;
using Transcendence.Service.Core.Services.LiveGame.Interfaces;
using Transcendence.Service.Core.Services.LiveGame.Models;

namespace Transcendence.Service.Core.Services.Jobs;

[DisableConcurrentExecution(timeoutInSeconds: 5 * 60)]
public class LiveGamePollingJob(
    TranscendenceContext db,
    ISummonerBootstrapService bootstrapService,
    ILiveGameService liveGameService,
    ILiveGameSnapshotRepository snapshotRepository,
    IRefreshLockRepository refreshLockRepository,
    IOptions<LiveGamePollingJobOptions> options,
    IOptions<ChampionAnalyticsIngestionJobOptions> analyticsIngestionOptions,
    ILogger<LiveGamePollingJob> logger)
{
    private sealed record TrackedSummonerCandidate(
        Guid Id,
        string Puuid,
        string GameName,
        string TagLine,
        string PlatformRegion,
        DateTime UpdatedAt);

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        if (analyticsIngestionOptions.Value.PauseWhenApiPriorityRefreshActive &&
            await refreshLockRepository.AnyActiveByPrefixAsync(RefreshLockKeys.ApiPriorityRefreshPrefix, ct))
        {
            logger.LogInformation("Live game polling skipped: active high-priority API refresh demand detected.");
            return;
        }

        await bootstrapService.EnsureSeededFromChallengerAsync(ct);

        var jobOptions = options.Value;
        if (jobOptions.PauseWhileChampionAnalyticsUnavailable)
        {
            var currentPatch = await db.Patches
                .AsNoTracking()
                .Where(p => p.IsActive)
                .Select(p => p.Version)
                .FirstOrDefaultAsync(ct);

            if (string.IsNullOrWhiteSpace(currentPatch))
            {
                logger.LogInformation(
                    "Live game polling skipped: champion analytics is prioritized and no active patch is available.");
                return;
            }

            var requiredMatches = Math.Max(1, analyticsIngestionOptions.Value.MinimumSuccessfulMatchesForCurrentPatch);
            var successfulMatchesForPatch = await db.Matches
                .AsNoTracking()
                .Where(m => m.Status == FetchStatus.Success && m.Patch == currentPatch)
                .CountAsync(ct);

            if (successfulMatchesForPatch < requiredMatches)
            {
                logger.LogInformation(
                    "Live game polling skipped: champion analytics is prioritized and patch {Patch} has {MatchCount}/{RequiredMatchCount} successful matches.",
                    currentPatch,
                    successfulMatchesForPatch,
                    requiredMatches);
                return;
            }

            var hasRoleDataForAnalytics = await db.MatchParticipants
                .AsNoTracking()
                .AnyAsync(mp => mp.Match.Status == FetchStatus.Success
                                && mp.Match.Patch == currentPatch
                                && mp.TeamPosition != null
                                && !string.IsNullOrWhiteSpace(mp.TeamPosition), ct);

            if (!hasRoleDataForAnalytics)
            {
                logger.LogInformation(
                    "Live game polling skipped: champion analytics is prioritized and patch {Patch} has no role-tagged participant data yet.",
                    currentPatch);
                return;
            }
        }

        var maxTrackedPerRun = Math.Max(1, jobOptions.MaxTrackedSummonersPerRun);
        var maxRiotRequestsPerRun = Math.Max(1, jobOptions.MaxRiotRequestsPerRun);
        var now = DateTime.UtcNow;

        var trackedSummonerQuery = db.Summoners
            .AsNoTracking()
            .Where(s => s.Puuid != null && s.GameName != null && s.TagLine != null && s.PlatformRegion != null);

        if (jobOptions.PollOnlyFavoriteSummoners)
        {
            var favorites = db.UserFavoriteSummoners.AsNoTracking();
            var hasAnyFavorites = await favorites.AnyAsync(ct);
            if (!hasAnyFavorites)
            {
                logger.LogInformation(
                    "Live game polling: no favorites configured; polling tracked summoners instead.");
            }

            if (hasAnyFavorites && jobOptions.RespectUserLivePollingPreference)
            {
                var preferences = db.UserPreferences.AsNoTracking();
                trackedSummonerQuery = trackedSummonerQuery.Where(s => favorites.Any(f =>
                    f.SummonerPuuid == s.Puuid &&
                    f.PlatformRegion == s.PlatformRegion &&
                    !preferences.Any(p => p.UserAccountId == f.UserAccountId && !p.LivePollingEnabled)));
            }
            else if (hasAnyFavorites)
            {
                trackedSummonerQuery = trackedSummonerQuery.Where(s =>
                    favorites.Any(f => f.SummonerPuuid == s.Puuid && f.PlatformRegion == s.PlatformRegion));
            }
        }

        var trackedSummoners = await trackedSummonerQuery
            .OrderByDescending(s => s.UpdatedAt)
            .Take(maxTrackedPerRun)
            .Select(s => new TrackedSummonerCandidate(
                s.Id,
                s.Puuid!,
                s.GameName!,
                s.TagLine!,
                s.PlatformRegion!,
                s.UpdatedAt))
            .ToListAsync(ct);

        if (trackedSummoners.Count == 0)
        {
            logger.LogInformation("Live game polling skipped: no summoners are eligible under current job filters.");
            return;
        }

        var processed = 0;
        var attemptedRequests = 0;
        var pendingSnapshots = 0;
        foreach (var summoner in trackedSummoners)
        {
            if (attemptedRequests >= maxRiotRequestsPerRun)
            {
                logger.LogInformation(
                    "Live game polling request budget reached ({RequestCount}/{RequestBudget}). Ending cycle early.",
                    attemptedRequests,
                    maxRiotRequestsPerRun);
                break;
            }

            var latest = await snapshotRepository.GetLatestByPuuidAsync(
                summoner.Puuid!,
                summoner.PlatformRegion!,
                ct);

            if (latest != null && latest.NextPollAtUtc > now)
                continue;

            try
            {
                attemptedRequests++;
                var response = await liveGameService.GetCurrentGameAsync(
                    summoner.PlatformRegion!,
                    summoner.GameName!,
                    summoner.TagLine!,
                    ct);

                var nextPollAt = now.Add(LiveGamePollingState.GetNextInterval(response.State));
                var snapshot = new LiveGameSnapshot
                {
                    Id = Guid.NewGuid(),
                    SummonerId = summoner.Id,
                    Puuid = summoner.Puuid!,
                    PlatformRegion = summoner.PlatformRegion!,
                    State = response.State,
                    GameId = response.GameId,
                    ObservedAtUtc = now,
                    NextPollAtUtc = nextPollAt
                };

                await snapshotRepository.AddAsync(snapshot, ct);
                pendingSnapshots++;
                processed++;

                if (latest?.State == "in_game" && response.State == "offline")
                {
                    logger.LogInformation(
                        "Live game ended for {Region}/{Puuid}, previous game {GameId}",
                        summoner.PlatformRegion,
                        summoner.Puuid,
                        latest.GameId);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Polling failed for {Region}/{GameName}#{TagLine}",
                    summoner.PlatformRegion,
                    summoner.GameName,
                    summoner.TagLine);
            }
        }

        if (pendingSnapshots > 0)
            await snapshotRepository.SaveChangesAsync(ct);

        logger.LogInformation(
            "Live game polling cycle complete. Processed {Count} summoners, attempted {RequestCount} Riot calls.",
            processed,
            attemptedRequests);
    }
}
