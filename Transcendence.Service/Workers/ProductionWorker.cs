using Hangfire;
using Microsoft.Extensions.Options;
using Transcendence.Service.Core.Services.Extensions;
using Transcendence.Service.Core.Services.Jobs;
using Transcendence.Service.Core.Services.Jobs.Configuration;
using Transcendence.Service.Workers.Startup;

namespace Transcendence.Service.Workers;

public class ProductionWorker(
    ILogger<ProductionWorker> logger,
    IBackgroundJobClient backgroundJobClient,
    JobStorage jobStorage,
    IOptions<WorkerJobScheduleOptions> options,
    IWorkerRecurringJobPolicy recurringJobPolicy,
    IWorkerStartupIntegrityService startupIntegrityService,
    IStartupPatchRolloverService startupPatchRolloverService) : BackgroundService
{
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        var schedule = options.Value;
        if (schedule.CleanupOnStartup)
            TryCleanupHangfireJobs();

        TryRemoveInvalidRecurringJobs();

        var profileName = recurringJobPolicy.ResolveProfile(schedule);
        var descriptors = recurringJobPolicy.BuildDescriptors(schedule);
        var startupResult = await startupIntegrityService.EvaluateAsync(cancellationToken);

        LogStartupIntegritySummary(profileName, startupResult);

        if (startupResult.Status == WorkerStartupIntegrityStatus.FailFast)
        {
            throw new InvalidOperationException(
                $"Worker startup integrity failed mandatory verification after attempt {startupResult.Attempt}/{startupResult.MaxAttempts}. " +
                $"Failures: {string.Join("; ", startupResult.MandatoryFailures)}");
        }

        var patchRolloverResult = await startupPatchRolloverService.PrepareAsync(cancellationToken);
        if (patchRolloverResult.PatchRolloverDetected)
        {
            logger.LogWarning(
                "Startup patch rollover state: previous={PreviousPatch}, latest={LatestPatch}, completed={Completed}, purgedEnqueued={PurgedEnqueued}, purgedScheduled={PurgedScheduled}.",
                patchRolloverResult.PreviousPatch ?? "none",
                patchRolloverResult.LatestPatch ?? "unknown",
                patchRolloverResult.PatchDetectionCompleted,
                patchRolloverResult.PurgedEnqueuedJobs,
                patchRolloverResult.PurgedScheduledJobs);
        }

        var descriptorsById = descriptors.ToDictionary(
            descriptor => descriptor.JobId,
            StringComparer.OrdinalIgnoreCase);

        var configuredJobs = string.Join(
            ", ",
            descriptors.Select(descriptor =>
                $"{descriptor.JobId}={(descriptor.IsEnabled ? descriptor.CronExpression : "disabled")}"));

        logger.LogInformation(
            "Recurring jobs configured with profile {Profile}: {ConfiguredJobs}",
            profileName,
            configuredJobs);

        EnqueueStartupAnalyticsBootstrap(descriptorsById, patchRolloverResult);

        await base.StartAsync(cancellationToken);
    }

    private void EnqueueStartupAnalyticsBootstrap(
        IReadOnlyDictionary<string, WorkerRecurringJobDescriptor> descriptorsById,
        StartupPatchRolloverResult patchRolloverResult)
    {
        if (IsRecurringJobEnabled(
                descriptorsById,
                WorkerRecurringJobPolicy.RefreshBuildResourceAnalyticsJobId))
        {
            TryEnqueueStartupJob(
                "startup-build-atlas-bootstrap",
                () => backgroundJobClient.Enqueue<RefreshBuildResourceAnalyticsJob>(
                    job => job.ExecuteAsync(true, false, CancellationToken.None)));
        }

        if (patchRolloverResult.PatchRolloverDetected &&
            patchRolloverResult.PatchDetectionCompleted &&
            IsRecurringJobEnabled(descriptorsById, WorkerRecurringJobPolicy.ChampionAnalyticsIngestionJobId))
        {
            TryEnqueueStartupJob(
                "startup-champion-analytics-bootstrap",
                () => backgroundJobClient.Enqueue<ChampionAnalyticsIngestionJob>(
                    job => job.ExecuteAsync(CancellationToken.None)));
        }
    }

    private void CleanupHangfireJobs()
    {
        JobStorage.Current?.GetMonitoringApi()?.PurgeJobs();

        foreach (var recurringJobId in recurringJobPolicy.KnownJobIds)
            RecurringJob.RemoveIfExists(recurringJobId);

        logger.LogInformation("Cleared queued and recurring jobs due to CleanupOnStartup=true.");
    }

    private static bool IsRecurringJobEnabled(
        IReadOnlyDictionary<string, WorkerRecurringJobDescriptor> descriptorsById,
        string recurringJobId) =>
        descriptorsById.TryGetValue(recurringJobId, out var descriptor) && descriptor.IsEnabled;

    private void LogStartupIntegritySummary(string profileName, WorkerStartupIntegrityResult startupResult)
    {
        logger.LogInformation(
            "Startup integrity summary for profile {Profile}: status={Status}, attempt={Attempt}/{MaxAttempts}, mandatoryVerified={VerifiedMandatoryCount}, mandatoryFailures={MandatoryFailureCount}, optionalFailures={OptionalFailureCount}.",
            profileName,
            startupResult.Status,
            startupResult.Attempt,
            startupResult.MaxAttempts,
            startupResult.VerifiedMandatoryJobIds.Count,
            startupResult.MandatoryFailures.Count,
            startupResult.OptionalFailures.Count);

        if (startupResult.OptionalFailures.Count > 0)
        {
            logger.LogWarning(
                "Startup integrity optional failures for profile {Profile}: {OptionalFailures}",
                profileName,
                string.Join("; ", startupResult.OptionalFailures));
        }
    }

    private void TryRemoveInvalidRecurringJobs()
    {
        try
        {
            var removed = jobStorage.RemoveInvalidRecurringJobs(
                logger,
                legacyRecurringJobIds:
                [
                    "cache-warmup",
                    "cache-warmup-analytics",
                    "analytics-cache-warmup",
                    // Removed when the per-patch ramp variants were folded into the self-pacing base jobs.
                    "refresh-champion-analytics-ramp",
                    "champion-analytics-ingestion-ramp",
                    "summoner-maintenance-ramp"
                ],
                legacyTypeNameFragments:
                [
                    "CacheWarmupJob"
                ]);

            if (removed > 0)
                logger.LogWarning("Removed {Count} invalid recurring jobs during startup cleanup.", removed);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to clean invalid recurring jobs during startup. Continuing startup.");
        }
    }

    private void TryCleanupHangfireJobs()
    {
        try
        {
            CleanupHangfireJobs();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to clean Hangfire jobs during startup. Continuing startup.");
        }
    }

    private string? TryEnqueueStartupJob(string operationName, Func<string> enqueue)
    {
        try
        {
            var jobId = enqueue();
            logger.LogInformation("Queued {OperationName} as job {JobId}.", operationName, jobId);
            return jobId;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to queue {OperationName}. Continuing startup.", operationName);
            return null;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
            // wait for 1 minute
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
    }
}
