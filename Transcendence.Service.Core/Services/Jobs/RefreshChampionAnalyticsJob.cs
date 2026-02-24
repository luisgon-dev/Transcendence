using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Service.Core.Services.Analytics.Interfaces;
using Transcendence.Service.Core.Services.Analytics.Models;
using Transcendence.Service.Core.Services.Jobs.Configuration;
using Transcendence.Service.Core.Services.StaticData.Interfaces;

namespace Transcendence.Service.Core.Services.Jobs;

/// <summary>
/// Daily job to refresh champion analytics cache.
/// Runs at 4 AM UTC to minimize user impact.
/// </summary>
[DisableConcurrentExecution(timeoutInSeconds: 60 * 60)]
public class RefreshChampionAnalyticsJob(
    IChampionAnalyticsService analyticsService,
    IStaticDataService staticDataService,
    TranscendenceContext db,
    IBackgroundJobClient backgroundJobClient,
    IDistributedCache distributedCache,
    IOptions<RefreshChampionAnalyticsJobOptions> options,
    ILogger<RefreshChampionAnalyticsJob> logger)
{
    private const string LastRefreshAtCacheKey = "jobs:analytics-refresh:last-success-at";
    private const string LastRefreshPatchCacheKey = "jobs:analytics-refresh:last-patch";
    private static readonly DistributedCacheEntryOptions RefreshStateCacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(90)
    };

    // Popular roles to pre-warm
    private static readonly string[] Roles = { "TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY" };

    // Primary rank tiers to pre-warm (covers majority of player base)
    private static readonly string[] PrimaryTiers = { "Gold", "Platinum", "Emerald", "Diamond" };

    public async Task ExecuteAsync(CancellationToken ct)
    {
        await ExecuteInternalAsync("daily", ct);
    }

    public async Task ExecuteAdaptiveAsync(CancellationToken ct)
    {
        var currentPatch = await GetCurrentPatchAsync(ct);
        if (string.IsNullOrWhiteSpace(currentPatch))
        {
            logger.LogWarning("Adaptive analytics refresh skipped because no active patch is available.");
            return;
        }

        var now = DateTime.UtcNow;
        var effectiveLookbackMinutes = Math.Max(5, options.Value.AdaptiveLookbackMinutes);
        var effectiveMinIntervalMinutes = Math.Max(5, options.Value.MinimumRefreshIntervalMinutes);
        var effectiveForceRefreshHours = Math.Max(1, options.Value.ForceRefreshAfterHours);
        var effectiveThreshold = Math.Max(1, options.Value.AdaptiveNewMatchesThreshold);

        var lastRefreshAt = await GetLastRefreshAtUtcAsync(ct);
        var lastRefreshPatch = await distributedCache.GetStringAsync(LastRefreshPatchCacheKey, ct);

        var patchChanged = !string.Equals(lastRefreshPatch, currentPatch, StringComparison.OrdinalIgnoreCase);
        var stale = !lastRefreshAt.HasValue ||
                    now - lastRefreshAt.Value >= TimeSpan.FromHours(effectiveForceRefreshHours);
        var cooldownPassed = !lastRefreshAt.HasValue ||
                             now - lastRefreshAt.Value >= TimeSpan.FromMinutes(effectiveMinIntervalMinutes);

        if (!cooldownPassed)
        {
            logger.LogDebug(
                "Adaptive analytics refresh skipped due to cooldown. Last refresh at {LastRefreshAtUtc}.",
                lastRefreshAt);
            return;
        }

        var sinceUtc = now.AddMinutes(-effectiveLookbackMinutes);
        var newlyFetchedMatches = await db.Matches
            .AsNoTracking()
            .Where(m => m.Status == FetchStatus.Success
                        && m.Patch == currentPatch
                        && m.FetchedAt != null
                        && m.FetchedAt >= sinceUtc)
            .CountAsync(ct);

        var thresholdMet = newlyFetchedMatches >= effectiveThreshold;
        if (!patchChanged && !stale && !thresholdMet)
        {
            logger.LogInformation(
                "Adaptive analytics refresh skipped. New matches {NewMatches}/{Threshold}, stale={Stale}, patchChanged={PatchChanged}.",
                newlyFetchedMatches,
                effectiveThreshold,
                stale,
                patchChanged);
            return;
        }

        var reason = patchChanged
            ? $"adaptive-patch-change ({lastRefreshPatch ?? "none"} -> {currentPatch})"
            : stale
                ? $"adaptive-stale ({effectiveForceRefreshHours}h)"
                : $"adaptive-threshold ({newlyFetchedMatches} matches/{effectiveLookbackMinutes}m)";

        await ExecuteInternalAsync(reason, ct);
    }

    private async Task ExecuteInternalAsync(string triggerReason, CancellationToken ct)
    {
        logger.LogInformation("Starting champion analytics refresh ({TriggerReason})", triggerReason);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var currentPatch = await GetCurrentPatchAsync(ct);
            if (string.IsNullOrWhiteSpace(currentPatch))
            {
                logger.LogWarning("Analytics refresh skipped because no active patch was found.");
                return;
            }

            await staticDataService.EnsureStaticDataForPatchAsync(currentPatch, ct);

            // Step 1: Invalidate all analytics cache
            logger.LogInformation("Invalidating analytics cache");
            await analyticsService.InvalidateAnalyticsCacheAsync(ct);

            // Step 2: Get popular champions to pre-warm
            var popularChampions = await GetPopularChampionsAsync(currentPatch, ct);
            logger.LogInformation("Pre-warming cache for {Count} popular champions", popularChampions.Count);
            if (popularChampions.Count == 0 && options.Value.EnqueueIngestionWhenNoPopularChampions)
            {
                backgroundJobClient.Enqueue<ChampionAnalyticsIngestionJob>(job =>
                    job.ExecuteAsync(CancellationToken.None));
                logger.LogWarning(
                    "No popular champions found for patch {Patch}. Queued ChampionAnalyticsIngestionJob to backfill match data.",
                    currentPatch);
            }

            // Step 3: Pre-warm tier lists (high value, relatively few combinations)
            foreach (var role in Roles)
            {
                foreach (var tier in PrimaryTiers)
                {
                    try
                    {
                        await analyticsService.GetTierListAsync(role, tier, ct);
                        logger.LogDebug("Pre-warmed tier list: {Role}/{Tier}", role, tier);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to pre-warm tier list {Role}/{Tier}", role, tier);
                    }
                }

                // Also pre-warm "all tiers" tier list per role
                try
                {
                    await analyticsService.GetTierListAsync(role, null, ct);
                    logger.LogDebug("Pre-warmed tier list: {Role}/all", role);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to pre-warm tier list {Role}/all", role);
                }
            }

            // Step 4: Pre-warm unified tier list
            try
            {
                await analyticsService.GetTierListAsync("ALL", null, ct);
                logger.LogDebug("Pre-warmed unified tier list");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to pre-warm unified tier list");
            }

            // Step 5: Pre-warm win rates, builds, matchups for top 20 champions per role
            var preWarmCount = 0;
            var championsPerRole = Math.Max(1, options.Value.ChampionsPerRoleToPreWarm);
            foreach (var role in Roles)
            {
                var roleChampions = popularChampions
                    .Where(c => c.Role == role)
                    .Take(championsPerRole)
                    .ToList();

                foreach (var champ in roleChampions)
                {
                    try
                    {
                        await analyticsService.GetWinRatesAsync(
                            champ.ChampionId,
                            new ChampionAnalyticsFilter(Role: role),
                            ct);

                        await analyticsService.GetBuildsAsync(champ.ChampionId, role, null, ct);
                        await analyticsService.GetMatchupsAsync(champ.ChampionId, role, null, ct);

                        preWarmCount++;
                        logger.LogDebug("Pre-warmed analytics for champion {ChampId} in {Role}",
                            champ.ChampionId, role);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to pre-warm analytics for champion {ChampId} in {Role}",
                            champ.ChampionId, role);
                    }
                }
            }

            await SaveRefreshStateAsync(currentPatch, ct);

            stopwatch.Stop();
            logger.LogInformation(
                "Analytics refresh complete ({TriggerReason}). Pre-warmed {Count} champion/role combinations in {Elapsed}ms",
                triggerReason,
                preWarmCount,
                stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logger.LogError(ex, "Analytics refresh failed after {Elapsed}ms", stopwatch.ElapsedMilliseconds);
            throw;
        }
    }

    private Task<string?> GetCurrentPatchAsync(CancellationToken ct)
    {
        return db.Patches
            .AsNoTracking()
            .Where(p => p.IsActive)
            .Select(p => p.Version)
            .FirstOrDefaultAsync(ct);
    }

    private async Task<List<(int ChampionId, string Role, int Games)>> GetPopularChampionsAsync(
        string currentPatch,
        CancellationToken ct)
    {
        var effectiveTakeCount = Math.Max(25, options.Value.PopularChampionsTakeCount);

        var popular = await db.MatchParticipants
            .AsNoTracking()
            .Where(mp => mp.Match.Patch == currentPatch
                      && mp.Match.Status == Data.Models.LoL.Match.FetchStatus.Success
                      && mp.TeamPosition != null)
            .GroupBy(mp => new { mp.ChampionId, mp.TeamPosition })
            .Select(g => new
            {
                g.Key.ChampionId,
                Role = g.Key.TeamPosition!,
                Games = g.Count()
            })
            .OrderByDescending(x => x.Games)
            .Take(effectiveTakeCount)
            .ToListAsync(ct);

        return popular.Select(p => (p.ChampionId, p.Role, p.Games)).ToList();
    }

    private async Task<DateTime?> GetLastRefreshAtUtcAsync(CancellationToken ct)
    {
        var value = await distributedCache.GetStringAsync(LastRefreshAtCacheKey, ct);
        if (DateTime.TryParse(value, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed))
        {
            return parsed.ToUniversalTime();
        }

        return null;
    }

    private async Task SaveRefreshStateAsync(string patch, CancellationToken ct)
    {
        var now = DateTime.UtcNow.ToString("O");
        await distributedCache.SetStringAsync(LastRefreshAtCacheKey, now, RefreshStateCacheOptions, ct);
        await distributedCache.SetStringAsync(LastRefreshPatchCacheKey, patch, RefreshStateCacheOptions, ct);
    }
}
