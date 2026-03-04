using Hangfire;
using Microsoft.Extensions.Options;
using Transcendence.Service.Core.Services.Extensions;
using Transcendence.Service.Core.Services.Jobs.Configuration;

namespace Transcendence.Service.Workers;

public class DevelopmentWorker(
    JobStorage jobStorage,
    IOptions<WorkerJobScheduleOptions> options,
    IWorkerRecurringJobPolicy recurringJobPolicy,
    IRecurringJobManager recurringJobManager,
    ILogger<DevelopmentWorker> logger)
    : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TryRemoveInvalidRecurringJobs();

        var schedule = options.Value;
        if (schedule.CleanupOnStartup)
            TryCleanupHangfireJobs();

        var profileName = recurringJobPolicy.ResolveProfile(schedule);
        var descriptors = recurringJobPolicy.BuildDescriptors(schedule);

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
            "Development worker configured recurring jobs with profile {Profile}: {ConfiguredJobs}",
            profileName,
            configuredJobs);

        return Task.CompletedTask;
    }

    private void CleanupHangfireJobs()
    {
        // clear any queued job or failed jobs
        JobStorage.Current?.GetMonitoringApi()?.PurgeJobs();

        foreach (var recurringJobId in recurringJobPolicy.KnownJobIds)
            RecurringJob.RemoveIfExists(recurringJobId);

        logger.LogInformation("Cleared all jobs");
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
}
