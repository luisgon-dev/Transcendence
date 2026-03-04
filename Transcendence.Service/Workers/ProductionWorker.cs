using Hangfire;
using Microsoft.Extensions.Options;
using Transcendence.Service.Core.Services.Extensions;
using Transcendence.Service.Core.Services.Jobs;
using Transcendence.Service.Core.Services.Jobs.Configuration;

namespace Transcendence.Service.Workers;

public class ProductionWorker(
    ILogger<ProductionWorker> logger,
    IBackgroundJobClient backgroundJobClient,
    JobStorage jobStorage,
    IOptions<WorkerJobScheduleOptions> options,
    IWorkerRecurringJobPolicy recurringJobPolicy,
    IRecurringJobManager recurringJobManager) : BackgroundService
{
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        var schedule = options.Value;
        if (schedule.CleanupOnStartup)
            TryCleanupHangfireJobs();

        TryRemoveInvalidRecurringJobs();

        var profileName = recurringJobPolicy.ResolveProfile(schedule);
        var descriptors = recurringJobPolicy.BuildDescriptors(schedule);
        var descriptorsById = descriptors.ToDictionary(
            descriptor => descriptor.JobId,
            StringComparer.OrdinalIgnoreCase);

        foreach (var descriptor in descriptors)
        {
            if (descriptor.IsEnabled)
            {
                TryConfigureRecurringJob(
                    descriptor.JobId,
                    descriptor.CronExpression,
                    () => descriptor.Apply(recurringJobManager));
            }
            else
            {
                TryRemoveRecurringJob(descriptor.JobId);
            }
        }

        var configuredJobs = string.Join(
            ", ",
            descriptors.Select(descriptor =>
                $"{descriptor.JobId}={(descriptor.IsEnabled ? descriptor.CronExpression : "disabled")}"));

        logger.LogInformation(
            "Recurring jobs configured with profile {Profile}: {ConfiguredJobs}",
            profileName,
            configuredJobs);

        EnqueueStartupAnalyticsBootstrap(schedule, descriptorsById);

        // One-time backfill: fix matches ingested before the FetchStatus bug was fixed
        TryEnqueueStartupJob(
            "startup-backfill-match-status",
            () => backgroundJobClient.Enqueue<BackfillMatchStatusJob>(job => job.ExecuteAsync(CancellationToken.None)));
        TryEnqueueStartupJob(
            "startup-rune-selection-integrity-backfill",
            () => backgroundJobClient.Enqueue<RuneSelectionIntegrityBackfillJob>(
                job => job.ExecuteAsync(CancellationToken.None)));
        if (IsRecurringJobEnabled(descriptorsById, WorkerRecurringJobPolicy.MatchTimelineBackfillJobId))
            TryEnqueueStartupJob(
                "startup-match-timeline-backfill",
                () => backgroundJobClient.Enqueue<MatchTimelineBackfillJob>(job => job.ExecuteAsync(CancellationToken.None)));

        return base.StartAsync(cancellationToken);
    }

    private void EnqueueStartupAnalyticsBootstrap(
        WorkerJobScheduleOptions schedule,
        IReadOnlyDictionary<string, WorkerRecurringJobDescriptor> descriptorsById)
    {
        string? patchJobId = null;
        if (schedule.RunPatchDetectionOnStartup &&
            IsRecurringJobEnabled(descriptorsById, WorkerRecurringJobPolicy.DetectPatchJobId))
        {
            patchJobId = TryEnqueueStartupJob(
                "startup-patch-detection",
                () => backgroundJobClient.Enqueue<UpdateStaticDataJob>(job => job.Execute(CancellationToken.None)));
        }

        if (IsRecurringJobEnabled(descriptorsById, WorkerRecurringJobPolicy.ChampionAnalyticsIngestionJobId))
        {
            if (!string.IsNullOrWhiteSpace(patchJobId))
            {
                try
                {
                    backgroundJobClient.ContinueJobWith<ChampionAnalyticsIngestionJob>(
                        patchJobId,
                        job => job.ExecuteAsync(CancellationToken.None));
                    logger.LogInformation("Queued startup champion analytics ingestion continuation after {PatchJobId}.",
                        patchJobId);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex,
                        "Failed to queue startup champion analytics ingestion continuation after {PatchJobId}. Falling back to immediate enqueue.",
                        patchJobId);
                    TryEnqueueStartupJob(
                        "startup-champion-analytics-ingestion-fallback",
                        () => backgroundJobClient.Enqueue<ChampionAnalyticsIngestionJob>(
                            job => job.ExecuteAsync(CancellationToken.None)));
                }
            }
            else
            {
                TryEnqueueStartupJob(
                    "startup-champion-analytics-ingestion",
                    () => backgroundJobClient.Enqueue<ChampionAnalyticsIngestionJob>(
                        job => job.ExecuteAsync(CancellationToken.None)));
            }
        }

        if (!IsRecurringJobEnabled(descriptorsById, WorkerRecurringJobPolicy.RefreshChampionAnalyticsAdaptiveJobId))
            return;

        if (!string.IsNullOrWhiteSpace(patchJobId))
        {
            try
            {
                backgroundJobClient.ContinueJobWith<RefreshChampionAnalyticsJob>(
                    patchJobId,
                    job => job.ExecuteAdaptiveAsync(CancellationToken.None));
                logger.LogInformation("Queued startup adaptive analytics refresh continuation after {PatchJobId}.",
                    patchJobId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Failed to queue startup adaptive analytics refresh continuation after {PatchJobId}. Falling back to immediate enqueue.",
                    patchJobId);
                TryEnqueueStartupJob(
                    "startup-adaptive-analytics-refresh-fallback",
                    () => backgroundJobClient.Enqueue<RefreshChampionAnalyticsJob>(
                        job => job.ExecuteAdaptiveAsync(CancellationToken.None)));
            }
        }
        else
        {
            TryEnqueueStartupJob(
                "startup-adaptive-analytics-refresh",
                () => backgroundJobClient.Enqueue<RefreshChampionAnalyticsJob>(
                    job => job.ExecuteAdaptiveAsync(CancellationToken.None)));
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
                    "analytics-cache-warmup"
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

    private void TryConfigureRecurringJob(string jobId, string cronExpression, Action configure)
    {
        try
        {
            configure();
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to configure recurring job {RecurringJobId} with cron {CronExpression}. Continuing startup.",
                jobId,
                cronExpression);
            TryRemoveRecurringJob(jobId);
        }
    }

    private void TryRemoveRecurringJob(string jobId)
    {
        try
        {
            RecurringJob.RemoveIfExists(jobId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to remove recurring job {RecurringJobId}. Continuing startup.",
                jobId);
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
