using Hangfire;
using Hangfire.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Transcendence.Data;
using Transcendence.Service.Core.Services.Extensions;
using Transcendence.Service.Core.Services.Jobs.Configuration;
using Transcendence.Service.Core.Services.StaticData.Interfaces;

namespace Transcendence.Service.Workers.Startup;

public interface IStartupPatchRolloverService
{
    Task<StartupPatchRolloverResult> PrepareAsync(CancellationToken cancellationToken);
}

public sealed record StartupPatchRolloverResult(
    bool PatchRolloverDetected,
    bool PatchDetectionCompleted,
    string? PreviousPatch,
    string? LatestPatch,
    int PurgedEnqueuedJobs,
    int PurgedScheduledJobs);

public sealed class StartupPatchRolloverService(
    ILogger<StartupPatchRolloverService> logger,
    IServiceScopeFactory scopeFactory,
    JobStorage jobStorage,
    IOptions<WorkerJobScheduleOptions> optionsAccessor) : IStartupPatchRolloverService
{
    public async Task<StartupPatchRolloverResult> PrepareAsync(CancellationToken cancellationToken)
    {
        var schedule = optionsAccessor.Value;
        if (!schedule.RunPatchDetectionOnStartup)
            return new StartupPatchRolloverResult(false, false, null, null, 0, 0);

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TranscendenceContext>();
        var staticDataService = scope.ServiceProvider.GetRequiredService<IStaticDataService>();

        string? latestPatch;
        try
        {
            latestPatch = await staticDataService.GetLatestPatchVersionAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Startup patch rollover preflight failed while fetching the latest patch version.");
            return new StartupPatchRolloverResult(false, false, null, null, 0, 0);
        }

        if (string.IsNullOrWhiteSpace(latestPatch))
            return new StartupPatchRolloverResult(false, false, null, null, 0, 0);

        var activePatch = await db.Patches
            .AsNoTracking()
            .Where(p => p.IsActive)
            .Select(p => p.Version)
            .FirstOrDefaultAsync(cancellationToken);

        if (string.Equals(activePatch, latestPatch, StringComparison.OrdinalIgnoreCase))
            return new StartupPatchRolloverResult(false, false, activePatch, latestPatch, 0, 0);

        var purgeSummary = schedule.PurgeBacklogOnPatchRolloverOnStartup && !string.IsNullOrWhiteSpace(activePatch)
            ? TryPurgeBacklog(activePatch!, latestPatch)
            : new HangfireExtensions.HangfireBacklogPurgeSummary(0, 0);

        try
        {
            await staticDataService.DetectAndRefreshAsync(cancellationToken);
            logger.LogWarning(
                "Startup patch rollover completed. PreviousPatch={PreviousPatch}, LatestPatch={LatestPatch}, PurgedEnqueued={PurgedEnqueued}, PurgedScheduled={PurgedScheduled}.",
                activePatch ?? "none",
                latestPatch,
                purgeSummary.EnqueuedJobs,
                purgeSummary.ScheduledJobs);

            return new StartupPatchRolloverResult(
                PatchRolloverDetected: true,
                PatchDetectionCompleted: true,
                PreviousPatch: activePatch,
                LatestPatch: latestPatch,
                PurgedEnqueuedJobs: purgeSummary.EnqueuedJobs,
                PurgedScheduledJobs: purgeSummary.ScheduledJobs);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Startup patch rollover failed after detecting patch skew. PreviousPatch={PreviousPatch}, LatestPatch={LatestPatch}.",
                activePatch ?? "none",
                latestPatch);

            return new StartupPatchRolloverResult(
                PatchRolloverDetected: true,
                PatchDetectionCompleted: false,
                PreviousPatch: activePatch,
                LatestPatch: latestPatch,
                PurgedEnqueuedJobs: purgeSummary.EnqueuedJobs,
                PurgedScheduledJobs: purgeSummary.ScheduledJobs);
        }
    }

    private HangfireExtensions.HangfireBacklogPurgeSummary TryPurgeBacklog(string previousPatch, string latestPatch)
    {
        try
        {
            var summary = jobStorage.GetMonitoringApi().PurgeEnqueuedAndScheduledJobs();
            logger.LogWarning(
                "Purged Hangfire backlog during startup patch rollover. PreviousPatch={PreviousPatch}, LatestPatch={LatestPatch}, PurgedEnqueued={PurgedEnqueued}, PurgedScheduled={PurgedScheduled}.",
                previousPatch,
                latestPatch,
                summary.EnqueuedJobs,
                summary.ScheduledJobs);
            return summary;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to purge Hangfire backlog during startup patch rollover. PreviousPatch={PreviousPatch}, LatestPatch={LatestPatch}.",
                previousPatch,
                latestPatch);
            return new HangfireExtensions.HangfireBacklogPurgeSummary(0, 0);
        }
    }
}
