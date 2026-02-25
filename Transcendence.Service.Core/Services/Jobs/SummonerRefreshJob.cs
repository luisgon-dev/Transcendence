using Camille.Enums;
using Camille.RiotGames;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Account;
using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Core.Services.Jobs.Configuration;
using Transcendence.Service.Core.Services.Jobs.Interfaces;
using Transcendence.Service.Core.Services.RiotApi;
using Transcendence.Service.Core.Services.RiotApi.Interfaces;
using DataMatch = Transcendence.Data.Models.LoL.Match.Match;

namespace Transcendence.Service.Core.Services.Jobs;

public class SummonerRefreshJob(
    ISummonerService summonerService,
    ISummonerRepository summonerRepository,
    IMatchRepository matchRepository,
    IMatchService matchService,
    TranscendenceContext db,
    IRefreshLockRepository refreshLockRepository,
    ILogger<SummonerRefreshJob> logger,
    RiotGamesApi riotGamesApi,
    IBackgroundJobClient backgroundJobClient,
    HybridCache cache,
    IOptions<MatchIngestionOptions> ingestionOptions,
    IOptions<TimelineIngestionOptions> timelineIngestionOptions) : ISummonerRefreshJob
{
    private sealed record BackfillSyncResult(int PersistedCount, bool StoppedEarly, bool HadFetchFailure);

    [Queue("refresh-high")]
    public async Task RefreshByRiotId(string gameName, string tagLine, PlatformRoute platformRoute, string lockKey,
        string? priorityLockKey, CancellationToken ct = default)
    {
        try
        {
            var options = ingestionOptions.Value;

            // Fetch/update summoner first.
            var summoner = await summonerService.GetSummonerByRiotIdAsync(gameName, tagLine, platformRoute, ct);
            await summonerRepository.AddOrUpdateSummonerAsync(summoner, ct);
            await db.SaveChangesAsync(ct);

            var regional = platformRoute.ToRegional();
            var pageSize = Math.Max(1, options.MatchIdsPageSize);

            var rankedHeadPersisted = await SyncMatchWindowAsync(
                gameName,
                tagLine,
                summoner.Puuid!,
                regional,
                platformRoute,
                pageSize,
                Math.Max(1, options.HighPriorityRankedPages),
                Queue.SUMMONERS_RIFT_5V5_RANKED_SOLO,
                "ranked",
                startTimeEpochSeconds: null,
                endTimeEpochSeconds: null,
                requiredPatch: null,
                lightweight: false,
                matchFilter: match => QueueCatalog.IsRankedAnalyticsQueue(match.QueueId),
                shouldStop: null,
                ct);

            var allModesHeadPersisted = await SyncMatchWindowAsync(
                gameName,
                tagLine,
                summoner.Puuid!,
                regional,
                platformRoute,
                pageSize,
                Math.Max(1, options.HighPriorityAllModesHeadPages),
                queue: null,
                type: null,
                startTimeEpochSeconds: null,
                endTimeEpochSeconds: null,
                requiredPatch: null,
                lightweight: false,
                matchFilter: match => QueueCatalog.IsInDefaultHistoryScope(match.QueueId),
                shouldStop: null,
                ct);

            // Exhaustive non-ranked backfill up to a conservative safety cap.
            var nonRankedBackfillResult = await SyncNonRankedBackfillWithCursorAsync(
                gameName,
                tagLine,
                summoner.Id,
                summoner.Puuid!,
                regional,
                platformRoute,
                pageSize,
                Math.Max(1, options.HighPriorityNonRankedBackfillMaxPages),
                lightweight: false,
                shouldStop: null,
                ct);

            await InvalidateStatsCacheAsync(summoner.Id, ct);

            logger.LogInformation(
                "[Refresh] Completed refresh for {GameName}#{Tag} on {Platform}. Persisted rankedHead={RankedHead}, allModesHead={AllModesHead}, nonRankedBackfill={NonRankedBackfill}.",
                gameName,
                tagLine,
                platformRoute,
                rankedHeadPersisted,
                allModesHeadPersisted,
                nonRankedBackfillResult.PersistedCount);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Refresh] Error refreshing {GameName}#{Tag} on {Platform}", gameName, tagLine,
                platformRoute);
            throw;
        }
        finally
        {
            await ReleaseLockSafeAsync(lockKey, ct, "[Refresh]");
            if (!string.IsNullOrWhiteSpace(priorityLockKey))
                await ReleaseLockSafeAsync(priorityLockKey, ct, "[Refresh]");
        }
    }

    [Queue("refresh-low")]
    public async Task RefreshForAnalytics(string gameName, string tagLine, PlatformRoute platformRoute, string lockKey,
        long startTimeEpochSeconds, string currentPatch, bool includeAllModes, CancellationToken ct = default)
    {
        try
        {
            if (await refreshLockRepository.AnyActiveByPrefixAsync(RefreshLockKeys.ApiPriorityRefreshPrefix, ct))
            {
                logger.LogInformation(
                    "[AnalyticsRefresh] Skipping {GameName}#{Tag} because high-priority API refresh demand is active.",
                    gameName,
                    tagLine);
                return;
            }

            var summoner = await summonerRepository.FindByRiotIdAsync(
                platformRoute.ToString(), gameName, tagLine, cancellationToken: ct);
            if (summoner == null || string.IsNullOrWhiteSpace(summoner.Puuid))
            {
                logger.LogWarning("[AnalyticsRefresh] Summoner {GameName}#{Tag} not found in DB, skipping",
                    gameName, tagLine);
                return;
            }

            var options = ingestionOptions.Value;
            var regional = platformRoute.ToRegional();
            var pageSize = Math.Max(1, options.MatchIdsPageSize);

            async Task<bool> ShouldStopAsync()
            {
                return await refreshLockRepository.AnyActiveByPrefixAsync(RefreshLockKeys.ApiPriorityRefreshPrefix, ct);
            }

            var rankedHeadPersisted = await SyncMatchWindowAsync(
                gameName,
                tagLine,
                summoner.Puuid,
                regional,
                platformRoute,
                pageSize,
                Math.Max(1, options.LowPriorityRankedPages),
                Queue.SUMMONERS_RIFT_5V5_RANKED_SOLO,
                "ranked",
                startTimeEpochSeconds,
                endTimeEpochSeconds: null,
                requiredPatch: currentPatch,
                lightweight: true,
                matchFilter: match => QueueCatalog.IsRankedAnalyticsQueue(match.QueueId),
                shouldStop: ShouldStopAsync,
                ct);

            var allModesHeadPersisted = 0;
            var nonRankedBackfillPersisted = 0;

            if (includeAllModes && !await ShouldStopAsync())
            {
                allModesHeadPersisted = await SyncMatchWindowAsync(
                    gameName,
                    tagLine,
                    summoner.Puuid,
                    regional,
                    platformRoute,
                    pageSize,
                    Math.Max(1, options.LowPriorityAllModesHeadPages),
                    queue: null,
                    type: null,
                    startTimeEpochSeconds: null,
                    endTimeEpochSeconds: null,
                    requiredPatch: null,
                    lightweight: true,
                    matchFilter: match => QueueCatalog.IsInDefaultHistoryScope(match.QueueId),
                    shouldStop: ShouldStopAsync,
                    ct);

                nonRankedBackfillPersisted = (await SyncNonRankedBackfillWithCursorAsync(
                    gameName,
                    tagLine,
                    summoner.Id,
                    summoner.Puuid,
                    regional,
                    platformRoute,
                    pageSize,
                    Math.Max(1, options.LowPriorityNonRankedBackfillMaxPages),
                    lightweight: true,
                    shouldStop: ShouldStopAsync,
                    ct)).PersistedCount;
            }

            summoner.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            logger.LogInformation(
                "[AnalyticsRefresh] Completed for {GameName}#{Tag} on {Platform}. includeAllModes={IncludeAllModes}, persisted rankedHead={RankedHead}, allModesHead={AllModesHead}, nonRankedBackfill={NonRankedBackfill}.",
                gameName,
                tagLine,
                platformRoute,
                includeAllModes,
                rankedHeadPersisted,
                allModesHeadPersisted,
                nonRankedBackfillPersisted);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[AnalyticsRefresh] Error refreshing {GameName}#{Tag} on {Platform}",
                gameName, tagLine, platformRoute);
            throw;
        }
        finally
        {
            await ReleaseLockSafeAsync(lockKey, ct, "[AnalyticsRefresh]");
        }
    }

    private async Task<int> SyncMatchWindowAsync(
        string gameName,
        string tagLine,
        string puuid,
        RegionalRoute regional,
        PlatformRoute platformRoute,
        int pageSize,
        int maxPages,
        Queue? queue,
        string? type,
        long? startTimeEpochSeconds,
        long? endTimeEpochSeconds,
        string? requiredPatch,
        bool lightweight,
        Func<DataMatch, bool> matchFilter,
        Func<Task<bool>>? shouldStop,
        CancellationToken ct)
    {
        if (maxPages <= 0)
            return 0;

        var persistedCount = 0;
        var seenIds = new HashSet<string>(StringComparer.Ordinal);

        for (var page = 0; page < maxPages; page++)
        {
            if (shouldStop != null && await shouldStop())
            {
                logger.LogInformation(
                    "[Refresh] Stopping window sync for {GameName}#{Tag} due to high-priority demand.",
                    gameName,
                    tagLine);
                break;
            }

            var start = page * pageSize;
            var pageIds = (await riotGamesApi.MatchV5()
                    .GetMatchIdsByPUUIDAsync(regional, puuid, pageSize, endTimeEpochSeconds, queue,
                        startTimeEpochSeconds, start,
                        type, ct))
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Where(id => seenIds.Add(id))
                .ToList();

            if (pageIds.Count == 0)
                break;

            var existingMatchIds = await matchRepository.GetExistingMatchIdsAsync(pageIds, ct);
            var pendingIds = pageIds
                .Where(id => !existingMatchIds.Contains(id))
                .ToList();

            if (pendingIds.Count == 0)
            {
                if (pageIds.Count < pageSize)
                    break;

                continue;
            }

            var matchesToPersist = new List<DataMatch>(pendingIds.Count);
            foreach (var matchId in pendingIds)
            {
                if (shouldStop != null && await shouldStop())
                    break;

                try
                {
                    var match = lightweight
                        ? await matchService.GetMatchDetailsLightweightAsync(matchId, regional, platformRoute, ct)
                        : await matchService.GetMatchDetailsAsync(matchId, regional, platformRoute, ct);

                    if (match == null)
                    {
                        logger.LogInformation("[Refresh] Match {MatchId} failed to fetch for {GameName}#{Tag}",
                            matchId,
                            gameName,
                            tagLine);
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(requiredPatch) &&
                        !string.Equals(match.Patch, requiredPatch, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!matchFilter(match))
                        continue;

                    matchesToPersist.Add(match);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "[Refresh] Error fetching match {MatchId} for {GameName}#{Tag}",
                        matchId,
                        gameName,
                        tagLine);
                }
            }

            if (matchesToPersist.Count > 0)
            {
                var persistedMatches = await PersistMatchesAsync(matchesToPersist, gameName, tagLine, ct);
                persistedCount += persistedMatches.Count;
                await EnqueueTimelineForRankedMatchesAsync(persistedMatches, ct);
            }

            if (pageIds.Count < pageSize)
                break;
        }

        return persistedCount;
    }

    private async Task<BackfillSyncResult> SyncNonRankedBackfillWithCursorAsync(
        string gameName,
        string tagLine,
        Guid summonerId,
        string puuid,
        RegionalRoute regional,
        PlatformRoute platformRoute,
        int pageSize,
        int maxPages,
        bool lightweight,
        Func<Task<bool>>? shouldStop,
        CancellationToken ct)
    {
        if (maxPages <= 0)
            return new BackfillSyncResult(0, false, false);

        var existingCursor = await db.SummonerIngestionCursors
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.SummonerId == summonerId && c.Scope == SummonerIngestionScopes.NonRankedBackfill,
                ct);

        var persistedCount = 0;
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        var backfillBeforeEpochSeconds = existingCursor?.BackfillBeforeEpochSeconds;
        var oldestSeenEpochSeconds = long.MaxValue;
        var stoppedEarly = false;
        var hadFetchFailure = false;

        for (var page = 0; page < maxPages; page++)
        {
            if (shouldStop != null && await shouldStop())
            {
                stoppedEarly = true;
                logger.LogInformation(
                    "[Refresh] Stopping non-ranked backfill for {GameName}#{Tag} due to high-priority demand.",
                    gameName,
                    tagLine);
                break;
            }

            var start = page * pageSize;
            var pageIds = (await riotGamesApi.MatchV5()
                    .GetMatchIdsByPUUIDAsync(
                        regional,
                        puuid,
                        pageSize,
                        backfillBeforeEpochSeconds,
                        queue: null,
                        startTime: null,
                        start: start,
                        type: null,
                        cancellationToken: ct))
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Where(id => seenIds.Add(id))
                .ToList();

            if (pageIds.Count == 0)
                break;

            var existingRows = await db.Matches
                .AsNoTracking()
                .Where(m => m.MatchId != null && pageIds.Contains(m.MatchId))
                .Select(m => new { m.MatchId, m.MatchDate })
                .ToListAsync(ct);

            foreach (var row in existingRows)
            {
                if (row.MatchDate <= 0)
                    continue;

                var epochSeconds = row.MatchDate / 1000;
                if (epochSeconds < oldestSeenEpochSeconds)
                    oldestSeenEpochSeconds = epochSeconds;
            }

            var existingMatchIds = existingRows
                .Select(x => x.MatchId)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToHashSet(StringComparer.Ordinal);

            var pendingIds = pageIds
                .Where(id => !existingMatchIds.Contains(id))
                .ToList();

            var matchesToPersist = new List<DataMatch>(pendingIds.Count);
            foreach (var matchId in pendingIds)
            {
                if (shouldStop != null && await shouldStop())
                {
                    stoppedEarly = true;
                    break;
                }

                try
                {
                    var match = lightweight
                        ? await matchService.GetMatchDetailsLightweightAsync(matchId, regional, platformRoute, ct)
                        : await matchService.GetMatchDetailsAsync(matchId, regional, platformRoute, ct);

                    if (match == null)
                    {
                        hadFetchFailure = true;
                        logger.LogInformation("[Refresh] Match {MatchId} failed to fetch for {GameName}#{Tag}",
                            matchId,
                            gameName,
                            tagLine);
                        continue;
                    }

                    if (match.MatchDate > 0)
                    {
                        var epochSeconds = match.MatchDate / 1000;
                        if (epochSeconds < oldestSeenEpochSeconds)
                            oldestSeenEpochSeconds = epochSeconds;
                    }

                    if (!QueueCatalog.IsInDefaultHistoryScope(match.QueueId) ||
                        QueueCatalog.IsRankedAnalyticsQueue(match.QueueId))
                        continue;

                    matchesToPersist.Add(match);
                }
                catch (Exception ex)
                {
                    hadFetchFailure = true;
                    logger.LogError(ex, "[Refresh] Error fetching non-ranked backfill match {MatchId} for {GameName}#{Tag}",
                        matchId, gameName, tagLine);
                }
            }

            if (matchesToPersist.Count > 0)
            {
                var persistedMatches = await PersistMatchesAsync(matchesToPersist, gameName, tagLine, ct);
                persistedCount += persistedMatches.Count;
                await EnqueueTimelineForRankedMatchesAsync(persistedMatches, ct);
            }

            if (pageIds.Count < pageSize)
                break;
        }

        long? nextBackfillBefore = backfillBeforeEpochSeconds;
        if (!stoppedEarly && !hadFetchFailure && oldestSeenEpochSeconds != long.MaxValue)
        {
            var candidate = Math.Max(0, oldestSeenEpochSeconds - 1);
            nextBackfillBefore = !nextBackfillBefore.HasValue
                                 || candidate < nextBackfillBefore.Value
                ? candidate
                : nextBackfillBefore;
        }

        await UpsertCursorAsync(
            summonerId,
            SummonerIngestionScopes.NonRankedBackfill,
            nextBackfillBefore,
            persistedCount,
            existingCursor?.ConsecutiveNoopRuns ?? 0,
            ct);

        return new BackfillSyncResult(persistedCount, stoppedEarly, hadFetchFailure);
    }

    private async Task UpsertCursorAsync(
        Guid summonerId,
        string scope,
        long? backfillBeforeEpochSeconds,
        int persistedCount,
        int previousNoopRuns,
        CancellationToken ct)
    {
        var cursor = await db.SummonerIngestionCursors
            .FirstOrDefaultAsync(c => c.SummonerId == summonerId && c.Scope == scope, ct);

        if (cursor == null)
        {
            var summoner = await db.Summoners.FirstOrDefaultAsync(s => s.Id == summonerId, ct);
            if (summoner == null)
                return;

            cursor = new SummonerIngestionCursor
            {
                SummonerId = summonerId,
                Summoner = summoner,
                Scope = scope,
                BackfillBeforeEpochSeconds = backfillBeforeEpochSeconds,
                LastRunAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
                ConsecutiveNoopRuns = persistedCount > 0 ? 0 : previousNoopRuns + 1
            };
            db.SummonerIngestionCursors.Add(cursor);
        }
        else
        {
            cursor.BackfillBeforeEpochSeconds = backfillBeforeEpochSeconds;
            cursor.LastRunAtUtc = DateTime.UtcNow;
            cursor.UpdatedAtUtc = DateTime.UtcNow;
            cursor.ConsecutiveNoopRuns = persistedCount > 0 ? 0 : previousNoopRuns + 1;
            cursor.Version++;
        }

        await db.SaveChangesAsync(ct);
    }

    private Task EnqueueTimelineForRankedMatchesAsync(IReadOnlyList<DataMatch> matches, CancellationToken ct)
    {
        if (!timelineIngestionOptions.Value.Enabled || matches.Count == 0)
            return Task.CompletedTask;

        foreach (var match in matches)
        {
            if (match.MatchId == null)
                continue;

            if (!QueueCatalog.IsRankedAnalyticsQueue(match.QueueId))
                continue;

            backgroundJobClient.Enqueue<MatchTimelineIngestionJob>(
                job => job.IngestMatchTimelineAsync(match.MatchId, CancellationToken.None));
        }

        return Task.CompletedTask;
    }

    private async Task ReleaseLockSafeAsync(string lockKey, CancellationToken ct, string operation)
    {
        try
        {
            await refreshLockRepository.ReleaseAsync(lockKey, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "{Operation} Failed to release refresh lock {LockKey}", operation, lockKey);
        }
    }

    private async Task<List<DataMatch>> PersistMatchesAsync(
        IReadOnlyList<DataMatch> matches,
        string gameName,
        string tagLine,
        CancellationToken ct)
    {
        var persisted = new List<DataMatch>(matches.Count);
        if (matches.Count == 0)
            return persisted;

        try
        {
            foreach (var match in matches)
                await matchRepository.AddMatchAsync(match, ct);

            await db.SaveChangesAsync(ct);
            persisted.AddRange(matches);
            return persisted;
        }
        catch (DbUpdateException ex) when (MatchPersistenceErrorClassifier.IsDuplicateMatchIdViolation(ex))
        {
            db.ChangeTracker.Clear();
            logger.LogInformation(
                "[Refresh] Duplicate match detected while persisting batch for {GameName}#{Tag}. Falling back to per-match persistence.",
                gameName,
                tagLine);
        }
        catch (Exception ex)
        {
            db.ChangeTracker.Clear();
            logger.LogError(
                ex,
                "[Refresh] Unexpected error while persisting batch for {GameName}#{Tag}. Falling back to per-match persistence.",
                gameName,
                tagLine);
        }

        foreach (var match in matches)
        {
            try
            {
                await matchRepository.AddMatchAsync(match, ct);
                await db.SaveChangesAsync(ct);
                persisted.Add(match);
            }
            catch (DbUpdateException ex) when (MatchPersistenceErrorClassifier.IsDuplicateMatchIdViolation(ex))
            {
                db.ChangeTracker.Clear();
                logger.LogInformation(
                    "[Refresh] Match {MatchId} already exists. Skipping duplicate insert for {GameName}#{Tag}.",
                    match.MatchId,
                    gameName,
                    tagLine);
            }
            catch (Exception ex)
            {
                db.ChangeTracker.Clear();
                logger.LogError(ex, "[Refresh] Error persisting match {MatchId} for {GameName}#{Tag}", match.MatchId,
                    gameName, tagLine);
            }
        }

        return persisted;
    }

    private async Task InvalidateStatsCacheAsync(Guid summonerId, CancellationToken ct)
    {
        await cache.RemoveByTagAsync($"summoner-stats:{summonerId}", ct);
    }
}
