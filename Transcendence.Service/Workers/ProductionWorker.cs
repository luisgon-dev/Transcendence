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
    IRecurringJobManager recurringJobManager) : BackgroundService
{
    private const string DetectPatchJobId = "detect-patch";
    private const string RetryFailedMatchesJobId = "retry-failed-matches";
    private const string RefreshChampionAnalyticsJobId = "refresh-champion-analytics";
    private const string RefreshChampionAnalyticsAdaptiveJobId = "refresh-champion-analytics-adaptive";
    private const string ChampionAnalyticsIngestionJobId = "champion-analytics-ingestion";
    private const string SummonerMaintenanceJobId = "summoner-maintenance";
    private const string MatchTimelineBackfillJobId = "match-timeline-backfill";
    private const string RuneSelectionIntegrityBackfillJobId = "rune-selection-integrity-backfill";
    private const string PollLiveGamesJobId = "poll-live-games";

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        var schedule = options.Value;
        if (schedule.CleanupOnStartup)
            TryCleanupHangfireJobs();

        TryRemoveInvalidRecurringJobs();

        TryConfigureRecurringJob(
            DetectPatchJobId,
            schedule.DetectPatchCron,
            () => recurringJobManager.AddOrUpdate<UpdateStaticDataJob>(
                DetectPatchJobId,
                x => x.Execute(CancellationToken.None),
                schedule.DetectPatchCron,
                new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc }));

        TryConfigureRecurringJob(
            RetryFailedMatchesJobId,
            schedule.RetryFailedMatchesCron,
            () => recurringJobManager.AddOrUpdate<RetryFailedMatchesJob>(
                RetryFailedMatchesJobId,
                job => job.Execute(CancellationToken.None),
                schedule.RetryFailedMatchesCron,
                new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc }));

        TryConfigureRecurringJob(
            RefreshChampionAnalyticsJobId,
            schedule.RefreshChampionAnalyticsDailyCron,
            () => recurringJobManager.AddOrUpdate<RefreshChampionAnalyticsJob>(
                RefreshChampionAnalyticsJobId,
                job => job.ExecuteAsync(CancellationToken.None),
                schedule.RefreshChampionAnalyticsDailyCron,
                new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc }));

        if (schedule.EnableAdaptiveAnalyticsRefresh)
        {
            TryConfigureRecurringJob(
                RefreshChampionAnalyticsAdaptiveJobId,
                schedule.RefreshChampionAnalyticsAdaptiveCron,
                () => recurringJobManager.AddOrUpdate<RefreshChampionAnalyticsJob>(
                    RefreshChampionAnalyticsAdaptiveJobId,
                    job => job.ExecuteAdaptiveAsync(CancellationToken.None),
                    schedule.RefreshChampionAnalyticsAdaptiveCron,
                    new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc }));
        }
        else
        {
            TryRemoveRecurringJob(RefreshChampionAnalyticsAdaptiveJobId);
        }

        if (schedule.EnableChampionAnalyticsIngestion)
        {
            TryConfigureRecurringJob(
                ChampionAnalyticsIngestionJobId,
                schedule.ChampionAnalyticsIngestionCron,
                () => recurringJobManager.AddOrUpdate<ChampionAnalyticsIngestionJob>(
                    ChampionAnalyticsIngestionJobId,
                    job => job.ExecuteAsync(CancellationToken.None),
                    schedule.ChampionAnalyticsIngestionCron,
                    new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc }));
        }
        else
        {
            TryRemoveRecurringJob(ChampionAnalyticsIngestionJobId);
        }

        if (schedule.EnableSummonerMaintenance)
        {
            TryConfigureRecurringJob(
                SummonerMaintenanceJobId,
                schedule.SummonerMaintenanceCron,
                () => recurringJobManager.AddOrUpdate<SummonerMaintenanceJob>(
                    SummonerMaintenanceJobId,
                    job => job.ExecuteAsync(CancellationToken.None),
                    schedule.SummonerMaintenanceCron,
                    new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc }));
        }
        else
        {
            TryRemoveRecurringJob(SummonerMaintenanceJobId);
        }

        if (schedule.EnableMatchTimelineBackfill)
        {
            TryConfigureRecurringJob(
                MatchTimelineBackfillJobId,
                schedule.MatchTimelineBackfillCron,
                () => recurringJobManager.AddOrUpdate<MatchTimelineBackfillJob>(
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
                () => recurringJobManager.AddOrUpdate<RuneSelectionIntegrityBackfillJob>(
                    RuneSelectionIntegrityBackfillJobId,
                    job => job.ExecuteAsync(CancellationToken.None),
                    schedule.RuneSelectionIntegrityBackfillCron,
                    new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc }));
        }
        else
        {
            TryRemoveRecurringJob(RuneSelectionIntegrityBackfillJobId);
        }

        TryConfigureRecurringJob(
            PollLiveGamesJobId,
            schedule.LiveGamePollingCron,
            () => recurringJobManager.AddOrUpdate<LiveGamePollingJob>(
                PollLiveGamesJobId,
                job => job.ExecuteAsync(CancellationToken.None),
                schedule.LiveGamePollingCron,
                new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc }));

        logger.LogInformation(
            "Recurring jobs configured: patch={PatchCron}, retry={RetryCron}, analyticsDaily={AnalyticsDailyCron}, analyticsAdaptive={AnalyticsAdaptiveCron}, analyticsIngestion={AnalyticsIngestionCron}, maintenance={MaintenanceCron}, timelineBackfill={TimelineBackfillCron}, runeIntegrity={RuneIntegrityCron}, livePolling={LivePollingCron}",
            schedule.DetectPatchCron,
            schedule.RetryFailedMatchesCron,
            schedule.RefreshChampionAnalyticsDailyCron,
            schedule.EnableAdaptiveAnalyticsRefresh ? schedule.RefreshChampionAnalyticsAdaptiveCron : "disabled",
            schedule.EnableChampionAnalyticsIngestion ? schedule.ChampionAnalyticsIngestionCron : "disabled",
            schedule.EnableSummonerMaintenance ? schedule.SummonerMaintenanceCron : "disabled",
            schedule.EnableMatchTimelineBackfill ? schedule.MatchTimelineBackfillCron : "disabled",
            schedule.EnableRuneSelectionIntegrityBackfill ? schedule.RuneSelectionIntegrityBackfillCron : "disabled",
            schedule.LiveGamePollingCron);

        EnqueueStartupAnalyticsBootstrap(schedule);

        // One-time backfill: fix matches ingested before the FetchStatus bug was fixed
        TryEnqueueStartupJob(
            "startup-backfill-match-status",
            () => backgroundJobClient.Enqueue<BackfillMatchStatusJob>(job => job.ExecuteAsync(CancellationToken.None)));
        TryEnqueueStartupJob(
            "startup-rune-selection-integrity-backfill",
            () => backgroundJobClient.Enqueue<RuneSelectionIntegrityBackfillJob>(
                job => job.ExecuteAsync(CancellationToken.None)));
        if (schedule.EnableMatchTimelineBackfill)
            TryEnqueueStartupJob(
                "startup-match-timeline-backfill",
                () => backgroundJobClient.Enqueue<MatchTimelineBackfillJob>(job => job.ExecuteAsync(CancellationToken.None)));

        return base.StartAsync(cancellationToken);
    }

    private void EnqueueStartupAnalyticsBootstrap(WorkerJobScheduleOptions schedule)
    {
        string? patchJobId = null;
        if (schedule.RunPatchDetectionOnStartup)
        {
            patchJobId = TryEnqueueStartupJob(
                "startup-patch-detection",
                () => backgroundJobClient.Enqueue<UpdateStaticDataJob>(job => job.Execute(CancellationToken.None)));
        }

        if (schedule.EnableChampionAnalyticsIngestion)
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
        RecurringJob.RemoveIfExists(DetectPatchJobId);
        RecurringJob.RemoveIfExists(RetryFailedMatchesJobId);
        RecurringJob.RemoveIfExists(RefreshChampionAnalyticsJobId);
        RecurringJob.RemoveIfExists(RefreshChampionAnalyticsAdaptiveJobId);
        RecurringJob.RemoveIfExists(ChampionAnalyticsIngestionJobId);
        RecurringJob.RemoveIfExists(SummonerMaintenanceJobId);
        RecurringJob.RemoveIfExists(MatchTimelineBackfillJobId);
        RecurringJob.RemoveIfExists(RuneSelectionIntegrityBackfillJobId);
        RecurringJob.RemoveIfExists(PollLiveGamesJobId);
        logger.LogInformation("Cleared queued and recurring jobs due to CleanupOnStartup=true.");
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
