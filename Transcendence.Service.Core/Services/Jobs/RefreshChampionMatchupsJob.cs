using System.Diagnostics;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Transcendence.Data;
using Transcendence.Service.Core.Services.Analytics.Interfaces;

namespace Transcendence.Service.Core.Services.Jobs;

/// <summary>
/// Advances the narrow matchup facts and resumable immutable matchup generation independently from
/// unrelated analytics surfaces. A failed run leaves its building generation available to resume.
/// </summary>
[DisableConcurrentExecution(timeoutInSeconds: 2 * 60 * 60)]
[AutomaticRetry(Attempts = 0)]
public sealed class RefreshChampionMatchupsJob(
    TranscendenceContext db,
    IPrecomputedAnalyticsRefresher refresher,
    IChampionAnalyticsService analyticsService,
    ILogger<RefreshChampionMatchupsJob> logger)
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
            logger.LogWarning("Champion matchup refresh skipped: no active patch found.");
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        int rows;
        try
        {
            rows = await refresher.RefreshMatchupsAsync(patch, ct);
        }
        finally
        {
            await analyticsService.InvalidateAnalyticsCacheForPatchAsync(
                patch,
                CancellationToken.None);
        }

        logger.LogInformation(
            "Champion matchup refresh patch {Patch} completed in {ElapsedMs}ms: {Rows} active rows.",
            patch,
            stopwatch.ElapsedMilliseconds,
            rows);
    }
}
