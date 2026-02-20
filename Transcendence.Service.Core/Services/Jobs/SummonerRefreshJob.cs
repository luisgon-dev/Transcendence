using Camille.Enums;
using Camille.RiotGames;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Transcendence.Data;
using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Core.Services.Jobs.Interfaces;
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
    HybridCache cache) : ISummonerRefreshJob
{
    [Queue("refresh-high")]
    public async Task RefreshByRiotId(string gameName, string tagLine, PlatformRoute platformRoute, string lockKey,
        string? priorityLockKey, CancellationToken ct = default)
    {
        try
        {
            // Fetch/update summoner
            var summoner = await summonerService.GetSummonerByRiotIdAsync(gameName, tagLine, platformRoute, ct);
            await summonerRepository.AddOrUpdateSummonerAsync(summoner, ct);
            await db.SaveChangesAsync(ct);

            // Fetch a small window of recent ranked solo match IDs for this summoner
            var regional = platformRoute.ToRegional();
            var matchIds = await riotGamesApi.MatchV5()
                .GetMatchIdsByPUUIDAsync(regional, summoner.Puuid!, 20, null,
                    Camille.Enums.Queue.SUMMONERS_RIFT_5V5_RANKED_SOLO,
                    null, null, "ranked", ct);

            // Defensively dedupe IDs returned by the API and process each match once.
            var pending = matchIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var existingMatchIds = await matchRepository.GetExistingMatchIdsAsync(pending, ct);
            var matchesToPersist = new List<DataMatch>();

            foreach (var matchId in pending.Where(id => !existingMatchIds.Contains(id)))
            {
                try
                {
                    var match = await matchService.GetMatchDetailsAsync(matchId, regional, platformRoute, ct);
                    if (match == null)
                    {
                        logger.LogInformation("[Refresh] Match {MatchId} failed to fetch for {GameName}#{Tag}", matchId,
                            gameName, tagLine);
                        continue;
                    }

                    matchesToPersist.Add(match);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "[Refresh] Error fetching match {MatchId} for {GameName}#{Tag}", matchId,
                        gameName, tagLine);
                }
            }

            await PersistMatchesAsync(matchesToPersist, gameName, tagLine, ct);

            // After matches saved, invalidate stats cache for this summoner
            await InvalidateStatsCacheAsync(summoner.Id, ct);

            logger.LogInformation("[Refresh] Completed refresh for {GameName}#{Tag} on {Platform}", gameName, tagLine,
                platformRoute);
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
        long startTimeEpochSeconds, string currentPatch, CancellationToken ct = default)
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

            // Look up summoner from DB instead of calling Riot API (saves 2+ API calls)
            var summoner = await summonerRepository.FindByRiotIdAsync(
                platformRoute.ToString(), gameName, tagLine, cancellationToken: ct);
            if (summoner == null || string.IsNullOrWhiteSpace(summoner.Puuid))
            {
                logger.LogWarning("[AnalyticsRefresh] Summoner {GameName}#{Tag} not found in DB, skipping",
                    gameName, tagLine);
                return;
            }

            var regional = platformRoute.ToRegional();

            // Pass startTime to only get matches from the current patch
            var matchIds = await riotGamesApi.MatchV5()
                .GetMatchIdsByPUUIDAsync(regional, summoner.Puuid!, 20, null,
                    Camille.Enums.Queue.SUMMONERS_RIFT_5V5_RANKED_SOLO,
                    startTimeEpochSeconds, null, "ranked", ct);

            var pending = matchIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var existingMatchIds = await matchRepository.GetExistingMatchIdsAsync(pending, ct);
            var matchesToPersist = new List<DataMatch>();

            foreach (var matchId in pending.Where(id => !existingMatchIds.Contains(id)))
            {
                if (await refreshLockRepository.AnyActiveByPrefixAsync(RefreshLockKeys.ApiPriorityRefreshPrefix, ct))
                {
                    logger.LogInformation(
                        "[AnalyticsRefresh] Stopping low-priority refresh for {GameName}#{Tag} due to active high-priority API refresh demand.",
                        gameName,
                        tagLine);
                    break;
                }

                try
                {
                    // Use lightweight method that batch-queries summoners and creates stubs
                    var match = await matchService.GetMatchDetailsLightweightAsync(matchId, regional, platformRoute, ct);
                    if (match == null)
                    {
                        logger.LogInformation("[AnalyticsRefresh] Match {MatchId} failed to fetch for {GameName}#{Tag}",
                            matchId, gameName, tagLine);
                        continue;
                    }

                    // Skip matches not on current patch (handles patch boundary edge cases)
                    if (!string.Equals(match.Patch, currentPatch, StringComparison.OrdinalIgnoreCase))
                    {
                        logger.LogInformation(
                            "[AnalyticsRefresh] Skipping match {MatchId} - patch {Patch} does not match current {CurrentPatch}",
                            matchId, match.Patch, currentPatch);
                        continue;
                    }

                    matchesToPersist.Add(match);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "[AnalyticsRefresh] Error fetching match {MatchId} for {GameName}#{Tag}",
                        matchId, gameName, tagLine);
                }
            }

            await PersistMatchesAsync(matchesToPersist, gameName, tagLine, ct);

            // Update summoner's UpdatedAt so candidate selection deprioritizes them next cycle
            summoner.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            logger.LogInformation(
                "[AnalyticsRefresh] Completed for {GameName}#{Tag} on {Platform} - persisted {Count} current-patch matches",
                gameName, tagLine, platformRoute, matchesToPersist.Count);
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

    private async Task PersistMatchesAsync(
        IReadOnlyList<DataMatch> matches,
        string gameName,
        string tagLine,
        CancellationToken ct)
    {
        if (matches.Count == 0)
            return;

        try
        {
            foreach (var match in matches)
                await matchRepository.AddMatchAsync(match, ct);

            await db.SaveChangesAsync(ct);
            return;
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
    }

    private async Task InvalidateStatsCacheAsync(Guid summonerId, CancellationToken ct)
    {
        await cache.RemoveByTagAsync($"summoner-stats:{summonerId}", ct);
    }

}
