using Hangfire;
using Microsoft.Extensions.Options;
using Transcendence.Service.Core.Services.Extensions;
using Transcendence.Service.Core.Services.Jobs;
using Transcendence.Service.Core.Services.Jobs.Configuration;

namespace Transcendence.Service.Workers;

public class DevelopmentWorker(
    JobStorage jobStorage,
    IOptions<WorkerJobScheduleOptions> options,
    ILogger<DevelopmentWorker> logger)
    : BackgroundService
{
    private const string DetectPatchJobId = "detect-patch";
    private const string RetryFailedMatchesJobId = "retry-failed-matches";
    private const string RefreshChampionAnalyticsJobId = "refresh-champion-analytics";
    private const string RefreshChampionAnalyticsAdaptiveJobId = "refresh-champion-analytics-adaptive";
    private const string RefreshChampionAnalyticsRampJobId = "refresh-champion-analytics-ramp";
    private const string ChampionAnalyticsIngestionJobId = "champion-analytics-ingestion";
    private const string ChampionAnalyticsIngestionRampJobId = "champion-analytics-ingestion-ramp";
    private const string SummonerMaintenanceJobId = "summoner-maintenance";
    private const string SummonerMaintenanceRampJobId = "summoner-maintenance-ramp";
    private const string MatchTimelineBackfillJobId = "match-timeline-backfill";
    private const string RuneSelectionIntegrityBackfillJobId = "rune-selection-integrity-backfill";
    private const string PollLiveGamesJobId = "poll-live-games";

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TryRemoveInvalidRecurringJobs();

        var schedule = options.Value;
        if (schedule.CleanupOnStartup)
            TryCleanupHangfireJobs();

        // Development worker intentionally runs analytics-only recurring jobs.
        TryRemoveNonAnalyticsRecurringJobs();
        logger.LogInformation("Development worker is configured for analytics-only recurring jobs.");

        // Schedule analytics refresh daily at 4 AM UTC
        TryConfigureRecurringJob(
            RefreshChampionAnalyticsJobId,
            schedule.RefreshChampionAnalyticsDailyCron,
            () => RecurringJob.AddOrUpdate<RefreshChampionAnalyticsJob>(
                RefreshChampionAnalyticsJobId,
                job => job.ExecuteAsync(CancellationToken.None),
                schedule.RefreshChampionAnalyticsDailyCron,
                new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc }));

        if (schedule.EnableAdaptiveAnalyticsRefresh)
        {
            TryConfigureRecurringJob(
                RefreshChampionAnalyticsAdaptiveJobId,
                schedule.RefreshChampionAnalyticsAdaptiveCron,
                () => RecurringJob.AddOrUpdate<RefreshChampionAnalyticsJob>(
                    RefreshChampionAnalyticsAdaptiveJobId,
                    job => job.ExecuteAdaptiveAsync(CancellationToken.None),
                    schedule.RefreshChampionAnalyticsAdaptiveCron,
                    new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc }));

            if (schedule.EnableNewPatchRamp)
            {
                TryConfigureRecurringJob(
                    RefreshChampionAnalyticsRampJobId,
                    schedule.RefreshChampionAnalyticsRampCron,
                    () => RecurringJob.AddOrUpdate<RefreshChampionAnalyticsJob>(
                        RefreshChampionAnalyticsRampJobId,
                        job => job.ExecuteRampAsync(CancellationToken.None),
                        schedule.RefreshChampionAnalyticsRampCron,
                        new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc }));
            }
            else
            {
                TryRemoveRecurringJob(RefreshChampionAnalyticsRampJobId);
            }
        }
        else
        {
            TryRemoveRecurringJob(RefreshChampionAnalyticsAdaptiveJobId);
            TryRemoveRecurringJob(RefreshChampionAnalyticsRampJobId);
        }

        if (schedule.EnableChampionAnalyticsIngestion)
        {
            TryConfigureRecurringJob(
                ChampionAnalyticsIngestionJobId,
                schedule.ChampionAnalyticsIngestionCron,
                () => RecurringJob.AddOrUpdate<ChampionAnalyticsIngestionJob>(
                    ChampionAnalyticsIngestionJobId,
                    job => job.ExecuteAsync(CancellationToken.None),
                    schedule.ChampionAnalyticsIngestionCron,
                    new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc }));

            if (schedule.EnableNewPatchRamp)
            {
                TryConfigureRecurringJob(
                    ChampionAnalyticsIngestionRampJobId,
                    schedule.ChampionAnalyticsIngestionRampCron,
                    () => RecurringJob.AddOrUpdate<ChampionAnalyticsIngestionJob>(
                        ChampionAnalyticsIngestionRampJobId,
                        job => job.ExecuteRampAsync(CancellationToken.None),
                        schedule.ChampionAnalyticsIngestionRampCron,
                        new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc }));
            }
            else
            {
                TryRemoveRecurringJob(ChampionAnalyticsIngestionRampJobId);
            }
        }
        else
        {
            TryRemoveRecurringJob(ChampionAnalyticsIngestionJobId);
            TryRemoveRecurringJob(ChampionAnalyticsIngestionRampJobId);
        }

        if (schedule.EnableSummonerMaintenance)
        {
            TryConfigureRecurringJob(
                SummonerMaintenanceJobId,
                schedule.SummonerMaintenanceCron,
                () => RecurringJob.AddOrUpdate<SummonerMaintenanceJob>(
                    SummonerMaintenanceJobId,
                    job => job.ExecuteAsync(CancellationToken.None),
                    schedule.SummonerMaintenanceCron,
                    new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc }));

            if (schedule.EnableNewPatchRamp)
            {
                TryConfigureRecurringJob(
                    SummonerMaintenanceRampJobId,
                    schedule.SummonerMaintenanceRampCron,
                    () => RecurringJob.AddOrUpdate<SummonerMaintenanceJob>(
                        SummonerMaintenanceRampJobId,
                        job => job.ExecuteRampAsync(CancellationToken.None),
                        schedule.SummonerMaintenanceRampCron,
                        new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc }));
            }
            else
            {
                TryRemoveRecurringJob(SummonerMaintenanceRampJobId);
            }
        }
        else
        {
            TryRemoveRecurringJob(SummonerMaintenanceJobId);
            TryRemoveRecurringJob(SummonerMaintenanceRampJobId);
        }

        if (schedule.EnableMatchTimelineBackfill)
        {
            TryConfigureRecurringJob(
                MatchTimelineBackfillJobId,
                schedule.MatchTimelineBackfillCron,
                () => RecurringJob.AddOrUpdate<MatchTimelineBackfillJob>(
                    MatchTimelineBackfillJobId,
                    job => job.ExecuteAsync(CancellationToken.None),
                    schedule.MatchTimelineBackfillCron,
                    new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc }));
        }
        else
        {
            TryRemoveRecurringJob(MatchTimelineBackfillJobId);
        }

        if (schedule.EnableRuneSelectionIntegrityBackfill)
        {
            TryConfigureRecurringJob(
                RuneSelectionIntegrityBackfillJobId,
                schedule.RuneSelectionIntegrityBackfillCron,
                () => RecurringJob.AddOrUpdate<RuneSelectionIntegrityBackfillJob>(
                    RuneSelectionIntegrityBackfillJobId,
                    job => job.ExecuteAsync(CancellationToken.None),
                    schedule.RuneSelectionIntegrityBackfillCron,
                    new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc }));
        }
        else
        {
            TryRemoveRecurringJob(RuneSelectionIntegrityBackfillJobId);
        }

        return Task.CompletedTask;
    }

    private void TryRemoveNonAnalyticsRecurringJobs()
    {
        TryRemoveRecurringJob(DetectPatchJobId);
        TryRemoveRecurringJob(RetryFailedMatchesJobId);
        TryRemoveRecurringJob(PollLiveGamesJobId);
    }

    private void CleanupHangfireJobs()
    {
        // clear any queued job or failed jobs
        JobStorage.Current?.GetMonitoringApi()?.PurgeJobs();
        RecurringJob.RemoveIfExists(DetectPatchJobId);
        RecurringJob.RemoveIfExists(RetryFailedMatchesJobId);
        RecurringJob.RemoveIfExists(RefreshChampionAnalyticsJobId);
        RecurringJob.RemoveIfExists(RefreshChampionAnalyticsAdaptiveJobId);
        RecurringJob.RemoveIfExists(RefreshChampionAnalyticsRampJobId);
        RecurringJob.RemoveIfExists(ChampionAnalyticsIngestionJobId);
        RecurringJob.RemoveIfExists(ChampionAnalyticsIngestionRampJobId);
        RecurringJob.RemoveIfExists(SummonerMaintenanceJobId);
        RecurringJob.RemoveIfExists(SummonerMaintenanceRampJobId);
        RecurringJob.RemoveIfExists(MatchTimelineBackfillJobId);
        RecurringJob.RemoveIfExists(RuneSelectionIntegrityBackfillJobId);
        RecurringJob.RemoveIfExists(PollLiveGamesJobId);
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
