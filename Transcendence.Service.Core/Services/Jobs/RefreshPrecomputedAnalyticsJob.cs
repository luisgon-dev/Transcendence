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
[AutomaticRetry(Attempts = 0)]
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

        PrecomputedAnalyticsFullRefreshResult result;
        try
        {
            result = await refresher.RefreshAllAsync(patch, ct);
        }
        finally
        {
            // Phases publish independently. Invalidate even when a later phase fails so any earlier
            // committed surface and its freshness label become visible immediately.
            await analyticsService.InvalidateAnalyticsCacheForPatchAsync(patch, CancellationToken.None);
            await analyticsService.InvalidateProAnalyticsCacheAsync(CancellationToken.None);
        }

        logger.LogInformation(
            "Precompute refresh patch {Patch} completed in {ElapsedMs}ms: {RoleTier} role-tier, {ScopeMatch} scope-match, {Ban} ban, {Matchup} matchup, {Build} build, {Pro} pro rows.",
            patch,
            stopwatch.ElapsedMilliseconds,
            result.Core.RoleTierRows,
            result.Core.ScopeMatchCountRows,
            result.Core.BanScopeRows,
            result.MatchupRows,
            result.BuildRows,
            result.ProRows);
    }
}
