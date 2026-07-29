using System.Diagnostics;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Transcendence.Data;
using Transcendence.Service.Core.Services.Analytics.Interfaces;

namespace Transcendence.Service.Core.Services.Jobs;

/// <summary>
/// Refreshes serialized champion build responses independently from tabular and matchup analytics.
/// The current snapshot set remains readable until the refresher atomically replaces the patch.
/// </summary>
[DisableConcurrentExecution(timeoutInSeconds: 2 * 60 * 60)]
[AutomaticRetry(Attempts = 0)]
public sealed class RefreshChampionBuildSnapshotsJob(
    TranscendenceContext db,
    IPrecomputedAnalyticsRefresher refresher,
    IChampionAnalyticsService analyticsService,
    ILogger<RefreshChampionBuildSnapshotsJob> logger)
{
    [Queue(HangfireQueues.AnalyticsWarm)]
    public async Task ExecuteAsync(CancellationToken ct)
    {
        var patch = await db.Patches
            .AsNoTracking()
            .Where(candidate => candidate.IsActive)
            .Select(candidate => candidate.Version)
            .FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(patch))
        {
            logger.LogWarning("Champion build snapshot refresh skipped: no active patch found.");
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        int rows;
        try
        {
            rows = await refresher.RefreshBuildsAsync(patch, ct);
        }
        finally
        {
            await analyticsService.InvalidateAnalyticsCacheForPatchAsync(
                patch,
                CancellationToken.None);
        }

        logger.LogInformation(
            "Champion build snapshot refresh patch {Patch} completed in {ElapsedMs}ms: {Rows} snapshots.",
            patch,
            stopwatch.ElapsedMilliseconds,
            rows);
    }
}
