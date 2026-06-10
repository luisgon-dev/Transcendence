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
    private static readonly string[] PrimaryTiers = { "Gold", "Platinum", "Emerald", "Diamond", "EMERALD_PLUS" };

    public async Task ExecuteAsync(CancellationToken ct)
    {
        await ExecuteInternalAsync("daily", ct);
    }

    [Queue(HangfireQueues.AnalyticsWarm)]
    public async Task ExecuteAdaptiveAsync(CancellationToken ct)
    {
        var patchState = await GetCurrentPatchStateAsync(ct);
        if (string.IsNullOrWhiteSpace(patchState.Version))
        {
            logger.LogWarning("Adaptive analytics refresh skipped because no active patch is available.");
            return;
        }

        var currentPatch = patchState.Version!;
        var useRampTuning = patchState.IsRampActive;
        var now = DateTime.UtcNow;
        var effectiveLookbackMinutes = Math.Max(5,
            useRampTuning ? options.Value.RampAdaptiveLookbackMinutes : options.Value.AdaptiveLookbackMinutes);
        var effectiveMinIntervalMinutes = Math.Max(5,
            useRampTuning
                ? options.Value.RampMinimumRefreshIntervalMinutes
                : options.Value.MinimumRefreshIntervalMinutes);
        var effectiveForceRefreshHours = Math.Max(1,
            useRampTuning ? options.Value.RampForceRefreshAfterHours : options.Value.ForceRefreshAfterHours);
        var effectiveThreshold = Math.Max(1,
            useRampTuning ? options.Value.RampAdaptiveNewMatchesThreshold : options.Value.AdaptiveNewMatchesThreshold);

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

        await ExecuteInternalAsync($"{reason}{(useRampTuning ? ", ramp-mode" : string.Empty)}", ct, useRampTuning);
    }

    private async Task ExecuteInternalAsync(string triggerReason, CancellationToken ct, bool useRampTuning = false)
    {
        logger.LogInformation("Starting champion analytics refresh ({TriggerReason}, ramp={Ramp})", triggerReason,
            useRampTuning);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var patchState = await GetCurrentPatchStateAsync(ct);
            var currentPatch = patchState.Version;
            if (string.IsNullOrWhiteSpace(currentPatch))
            {
                logger.LogWarning("Analytics refresh skipped because no active patch was found.");
                return;
            }

            await staticDataService.EnsureStaticDataForPatchAsync(currentPatch, ct);

            // Step 1: Invalidate only this patch's analytics cache. Keeps other patches, the pro
            // roster, and pro-playrate entries warm so a routine current-patch refresh does not
            // cold-start every cached entry at once.
            logger.LogInformation("Invalidating analytics cache for patch {Patch}", currentPatch);
            await analyticsService.InvalidateAnalyticsCacheForPatchAsync(currentPatch, ct);

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
                        await analyticsService.GetTierListAsync(role, tier, null, null, ct);
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
                    await analyticsService.GetTierListAsync(role, null, null, null, ct);
                    logger.LogDebug("Pre-warmed tier list: {Role}/all", role);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to pre-warm tier list {Role}/all", role);
                }
            }

            // Step 4: Pre-warm unified tier lists
            try
            {
                await analyticsService.GetTierListAsync("ALL", null, null, null, ct);
                await analyticsService.GetTierListAsync("ALL", "EMERALD_PLUS", null, null, ct);
                logger.LogDebug("Pre-warmed unified tier list variants");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to pre-warm unified tier list variants");
            }

            // Step 5: Pre-warm win rates / builds / matchups (and, bounded, pro-builds) for the
            // top champions per role at the SAME parameters the champion page reads — rankTier =
            // the frontend default (Emerald+), win rates with no role filter (the profile endpoint
            // reads the full by-role table to resolve the most-played lane). Warming the wrong tier
            // means real requests never hit the pre-warmed keys.
            var preWarmCount = 0;
            var proBuildPreWarmCount = 0;
            var preWarmTier = string.IsNullOrWhiteSpace(options.Value.PreWarmRankTier)
                ? null
                : options.Value.PreWarmRankTier.Trim();
            var championsPerRole = Math.Max(
                1,
                useRampTuning ? options.Value.RampChampionsPerRoleToPreWarm : options.Value.ChampionsPerRoleToPreWarm);
            var proBuildChampionsPerRole = Math.Max(0, options.Value.ProBuildChampionsPerRoleToPreWarm);
            foreach (var role in Roles)
            {
                var roleChampions = popularChampions
                    .Where(c => c.Role == role)
                    .Take(championsPerRole)
                    .ToList();

                for (var i = 0; i < roleChampions.Count; i++)
                {
                    var champ = roleChampions[i];
                    try
                    {
                        await analyticsService.GetWinRatesAsync(
                            champ.ChampionId,
                            new ChampionAnalyticsFilter(RankTier: preWarmTier),
                            ct);

                        await analyticsService.GetBuildsAsync(champ.ChampionId, role, preWarmTier, null, null, ct);
                        await analyticsService.GetMatchupsAsync(champ.ChampionId, role, preWarmTier, null, null, ct);

                        preWarmCount++;

                        // Pro-builds compute is heavier, so warm a smaller bounded set. role-scoped
                        // here mirrors the page default (no role -> most-played lane == this role).
                        if (options.Value.PreWarmProBuilds && i < proBuildChampionsPerRole)
                        {
                            await analyticsService.GetProBuildsAsync(champ.ChampionId, null, role, null, null, ct);
                            proBuildPreWarmCount++;
                        }

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
                "Analytics refresh complete ({TriggerReason}). Pre-warmed {Count} champion/role combinations ({ProBuilds} pro-builds) at tier {Tier} in {Elapsed}ms",
                triggerReason,
                preWarmCount,
                proBuildPreWarmCount,
                preWarmTier ?? "all",
                stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logger.LogError(ex, "Analytics refresh failed after {Elapsed}ms", stopwatch.ElapsedMilliseconds);
            throw;
        }
    }

    private async Task<PatchState> GetCurrentPatchStateAsync(CancellationToken ct)
    {
        var patch = await db.Patches
            .AsNoTracking()
            .Where(p => p.IsActive)
            .Select(p => new { p.Version, p.ReleaseDate })
            .FirstOrDefaultAsync(ct);

        if (patch == null || string.IsNullOrWhiteSpace(patch.Version))
            return new PatchState(null, false);

        var releaseUtc = patch.ReleaseDate.Kind == DateTimeKind.Utc
            ? patch.ReleaseDate
            : DateTime.SpecifyKind(patch.ReleaseDate, DateTimeKind.Utc);
        var rampHours = Math.Max(1, options.Value.NewPatchRampHours);
        var isRampActive = DateTime.UtcNow < releaseUtc.AddHours(rampHours);

        return new PatchState(patch.Version, isRampActive);
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

    private sealed record PatchState(string? Version, bool IsRampActive);
}
