using System.Diagnostics;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Analytics;
using Transcendence.Service.Core.Services.Analytics.Interfaces;

namespace Transcendence.Service.Core.Services.Jobs;

/// <summary>
/// Independently advances Build Atlas without depending on the champion precompute pipeline.
/// Generation promotion is handled by the refresher; this job only resolves the active patch and
/// applies bootstrap/no-op policy.
/// </summary>
public sealed class RefreshBuildResourceAnalyticsJob(
    TranscendenceContext db,
    IBuildResourceSnapshotRefresher refresher,
    ILogger<RefreshBuildResourceAnalyticsJob> logger)
{
    [Queue(HangfireQueues.AnalyticsWarm)]
    [DisableConcurrentExecution(timeoutInSeconds: 60 * 60)]
    public async Task ExecuteAsync(bool onlyIfMissing, bool forceFullRebuild, CancellationToken ct)
    {
        var patch = await db.Patches.AsNoTracking()
            .Where(candidate => candidate.IsActive)
            .Select(candidate => candidate.Version)
            .FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(patch))
        {
            logger.LogWarning("Build Atlas refresh skipped: no active patch found.");
            return;
        }

        if (onlyIfMissing)
        {
            var ready = await db.BuildResourceSnapshots.AsNoTracking().AnyAsync(snapshot =>
                snapshot.Patch == patch &&
                snapshot.IsActive &&
                snapshot.Status == BuildResourceSnapshotStatus.Ready, ct);
            if (ready)
            {
                logger.LogInformation(
                    "Build Atlas bootstrap skipped for patch {Patch}: an active snapshot already exists.",
                    patch);
                return;
            }
        }

        var stopwatch = Stopwatch.StartNew();
        var result = await refresher.RefreshAsync(patch, forceFullRebuild, ct);
        logger.LogInformation(
            "Build Atlas refresh patch {Patch} completed in {ElapsedMs}ms: snapshot={SnapshotId}, full={Full}, newMatches={Matches}, resourceRows={ResourceRows}, populationRows={PopulationRows}.",
            patch,
            stopwatch.ElapsedMilliseconds,
            result.SnapshotId,
            result.FullRebuild,
            result.ProcessedMatchCount,
            result.ResourceRows,
            result.PopulationRows);
    }
}
