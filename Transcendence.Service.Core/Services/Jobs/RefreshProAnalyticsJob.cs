using System.Diagnostics;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Transcendence.Data;
using Transcendence.Service.Core.Services.Analytics.Interfaces;

namespace Transcendence.Service.Core.Services.Jobs;

/// <summary>
/// Refreshes the roster-backed pro playrate and build snapshots independently from the heavier
/// all-analytics refresh. Matchup aggregation can time out on the production dataset; keeping this
/// small phase separate prevents an unrelated failure from leaving pro roster changes invisible.
/// </summary>
[DisableConcurrentExecution(timeoutInSeconds: 30 * 60)]
public sealed class RefreshProAnalyticsJob(
    TranscendenceContext db,
    IPrecomputedAnalyticsRefresher refresher,
    IChampionAnalyticsService analyticsService,
    ILogger<RefreshProAnalyticsJob> logger)
{
    [Queue(HangfireQueues.AnalyticsWarm)]
    public async Task ExecuteAsync(CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var patch = await db.Patches
            .AsNoTracking()
            .Where(candidate => candidate.IsActive)
            .Select(candidate => candidate.Version)
            .FirstOrDefaultAsync(ct);

        if (string.IsNullOrWhiteSpace(patch))
        {
            logger.LogWarning("Pro analytics refresh skipped: no active patch found.");
            return;
        }

        var rows = await refresher.RefreshProSurfacesAsync(patch, ct);
        await analyticsService.InvalidateProAnalyticsCacheAsync(ct);

        logger.LogInformation(
            "Pro analytics refresh patch {Patch} completed in {ElapsedMs}ms: {Rows} snapshots.",
            patch,
            stopwatch.ElapsedMilliseconds,
            rows);
    }
}
