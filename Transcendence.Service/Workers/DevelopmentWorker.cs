using Hangfire;
using Microsoft.Extensions.Options;
using Transcendence.Service.Core.Services.Extensions;
using Transcendence.Service.Core.Services.Jobs.Configuration;
using Transcendence.Service.Workers.Startup;

namespace Transcendence.Service.Workers;

public class DevelopmentWorker(
    JobStorage jobStorage,
    IOptions<WorkerJobScheduleOptions> options,
    IWorkerRecurringJobPolicy recurringJobPolicy,
    IWorkerStartupIntegrityService startupIntegrityService,
    ILogger<DevelopmentWorker> logger)
    : BackgroundService
{
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        TryRemoveInvalidRecurringJobs();

        var schedule = options.Value;
        if (schedule.CleanupOnStartup)
            TryCleanupHangfireJobs();

        var profileName = recurringJobPolicy.ResolveProfile(schedule);
        var startupResult = await startupIntegrityService.EvaluateAsync(cancellationToken);

        logger.LogInformation(
            "Startup integrity summary for development profile {Profile}: status={Status}, attempt={Attempt}/{MaxAttempts}, mandatoryVerified={VerifiedMandatoryCount}, mandatoryFailures={MandatoryFailureCount}, optionalFailures={OptionalFailureCount}.",
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
                "Startup integrity optional failures for development profile {Profile}: {OptionalFailures}",
                profileName,
                string.Join("; ", startupResult.OptionalFailures));
        }

        if (startupResult.Status == WorkerStartupIntegrityStatus.FailFast)
        {
            throw new InvalidOperationException(
                $"Development worker startup integrity failed mandatory verification after attempt {startupResult.Attempt}/{startupResult.MaxAttempts}. " +
                $"Failures: {string.Join("; ", startupResult.MandatoryFailures)}");
        }

        await base.StartAsync(cancellationToken);
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;

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
}
