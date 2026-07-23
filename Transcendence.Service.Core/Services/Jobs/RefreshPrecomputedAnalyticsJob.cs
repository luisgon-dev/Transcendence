using System.Diagnostics;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Transcendence.Data;
using Transcendence.Service.Core.Services.Analytics.Interfaces;

namespace Transcendence.Service.Core.Services.Jobs;

/// <summary>
/// Rebuilds the precomputed champion-analytics aggregate tables (win-rate/pick-rate/role-rank source +
/// ban-rate numerator/denominator) for the active patch on a cadence, so the analytics read path serves
/// win-rates and the tier list as fast indexed lookups instead of raw-match scans. The heavy GROUP BY /
/// distinct-match work runs here, off the request path. Resolving the active patch each run means a patch
/// rollover is picked up automatically on the next cycle. Runs on the reserved
/// <see cref="HangfireQueues.AnalyticsWarm"/> lane so it is unaffected by shared-queue saturation.
/// </summary>
[DisableConcurrentExecution(timeoutInSeconds: 60 * 60)]
public class RefreshPrecomputedAnalyticsJob(
    TranscendenceContext db,
    IPrecomputedAnalyticsRefresher refresher,
    IChampionAnalyticsService analyticsService,
    ILogger<RefreshPrecomputedAnalyticsJob> logger)
{
    [Queue(HangfireQueues.AnalyticsWarm)]
    public async Task ExecuteAsync(CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();

        var patch = await db.Patches
            .AsNoTracking()
            .Where(p => p.IsActive)
            .Select(p => p.Version)
            .FirstOrDefaultAsync(ct);

        if (string.IsNullOrWhiteSpace(patch))
        {
            logger.LogWarning("Precompute refresh skipped: no active patch found.");
            return;
        }

        var result = await refresher.RefreshAllAsync(patch, ct);

        // Now that the precomputed atoms for this patch are committed, drop the patch-scoped
        // analytics read-cache so the served data and the "updated N ago" freshness label advance
        // together. Scoped to the current patch tag only (CacheTags.ForPatch) so other patches, the
        // pro roster, and playrate entries stay warm.
        await analyticsService.InvalidateAnalyticsCacheForPatchAsync(patch, ct);

        logger.LogInformation(
            "Precompute refresh patch {Patch} completed in {ElapsedMs}ms: {RoleTier} role-tier, {ScopeMatch} scope-match, {Ban} ban, {Matchup} matchup, {BuildResource} build-resource, {Build} build, {Pro} pro rows.",
            patch,
            stopwatch.ElapsedMilliseconds,
            result.Core.RoleTierRows,
            result.Core.ScopeMatchCountRows,
            result.Core.BanScopeRows,
            result.MatchupRows,
            result.BuildResourceRows,
            result.BuildRows,
            result.ProRows);
    }
}
