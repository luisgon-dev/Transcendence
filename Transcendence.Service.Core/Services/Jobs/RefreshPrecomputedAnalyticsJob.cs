using System.Diagnostics;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Transcendence.Data;
using Transcendence.Service.Core.Services.Analytics.Interfaces;

namespace Transcendence.Service.Core.Services.Jobs;

/// <summary>
/// Rebuilds only the tabular champion-analytics core (win-rate/pick-rate/role-rank source +
/// ban-rate numerator/denominator). Build snapshots and matchup generations have independent jobs so a
/// long build sweep cannot delay matchup freshness, and neither surface extends this job's ownership.
/// </summary>
[DisableConcurrentExecution(timeoutInSeconds: 30 * 60)]
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

        PrecomputedAnalyticsRefreshResult result;
        try
        {
            result = await refresher.RefreshTabularCoreAsync(patch, ct);
        }
        finally
        {
            // The replacement is transactional. Invalidate in finally so a commit that completed before
            // a cancellation/follow-up failure is not hidden behind a stale process cache.
            await analyticsService.InvalidateAnalyticsCacheForPatchAsync(patch, CancellationToken.None);
        }

        logger.LogInformation(
            "Tabular analytics refresh patch {Patch} completed in {ElapsedMs}ms: {RoleTier} role-tier, {ScopeMatch} scope-match, {Ban} ban, {Grade} grade rows.",
            patch,
            stopwatch.ElapsedMilliseconds,
            result.RoleTierRows,
            result.ScopeMatchCountRows,
            result.BanScopeRows,
            result.GradeRows);
    }
}
