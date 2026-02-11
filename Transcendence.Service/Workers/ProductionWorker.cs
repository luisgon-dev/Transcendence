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
    private const string PollLiveGamesJobId = "poll-live-games";

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        var schedule = options.Value;
        if (schedule.CleanupOnStartup)
            CleanupHangfireJobs();

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

        recurringJobManager.AddOrUpdate<UpdateStaticDataJob>(
            DetectPatchJobId,
            x => x.Execute(CancellationToken.None),
            schedule.DetectPatchCron,
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

        recurringJobManager.AddOrUpdate<RetryFailedMatchesJob>(
            RetryFailedMatchesJobId,
            job => job.Execute(CancellationToken.None),
            schedule.RetryFailedMatchesCron,
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

        recurringJobManager.AddOrUpdate<RefreshChampionAnalyticsJob>(
            RefreshChampionAnalyticsJobId,
            job => job.ExecuteAsync(CancellationToken.None),
            schedule.RefreshChampionAnalyticsDailyCron,
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

        if (schedule.EnableAdaptiveAnalyticsRefresh)
        {
            recurringJobManager.AddOrUpdate<RefreshChampionAnalyticsJob>(
                RefreshChampionAnalyticsAdaptiveJobId,
                job => job.ExecuteAdaptiveAsync(CancellationToken.None),
                schedule.RefreshChampionAnalyticsAdaptiveCron,
                new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
        }
        else
        {
            RecurringJob.RemoveIfExists(RefreshChampionAnalyticsAdaptiveJobId);
        }

        if (schedule.EnableChampionAnalyticsIngestion)
        {
            recurringJobManager.AddOrUpdate<ChampionAnalyticsIngestionJob>(
                ChampionAnalyticsIngestionJobId,
                job => job.ExecuteAsync(CancellationToken.None),
                schedule.ChampionAnalyticsIngestionCron,
                new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
        }
        else
        {
            RecurringJob.RemoveIfExists(ChampionAnalyticsIngestionJobId);
        }

        recurringJobManager.AddOrUpdate<LiveGamePollingJob>(
            PollLiveGamesJobId,
            job => job.ExecuteAsync(CancellationToken.None),
            schedule.LiveGamePollingCron,
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

        logger.LogInformation(
            "Recurring jobs configured: patch={PatchCron}, retry={RetryCron}, analyticsDaily={AnalyticsDailyCron}, analyticsAdaptive={AnalyticsAdaptiveCron}, analyticsIngestion={AnalyticsIngestionCron}, livePolling={LivePollingCron}",
            schedule.DetectPatchCron,
            schedule.RetryFailedMatchesCron,
            schedule.RefreshChampionAnalyticsDailyCron,
            schedule.EnableAdaptiveAnalyticsRefresh ? schedule.RefreshChampionAnalyticsAdaptiveCron : "disabled",
            schedule.EnableChampionAnalyticsIngestion ? schedule.ChampionAnalyticsIngestionCron : "disabled",
            schedule.LiveGamePollingCron);

        EnqueueStartupAnalyticsBootstrap(schedule);

        // One-time backfill: fix matches ingested before the FetchStatus bug was fixed
        backgroundJobClient.Enqueue<BackfillMatchStatusJob>(job => job.ExecuteAsync(CancellationToken.None));

        return base.StartAsync(cancellationToken);
    }

    private void EnqueueStartupAnalyticsBootstrap(WorkerJobScheduleOptions schedule)
    {
        string? patchJobId = null;
        if (schedule.RunPatchDetectionOnStartup)
        {
            patchJobId = backgroundJobClient.Enqueue<UpdateStaticDataJob>(job => job.Execute(CancellationToken.None));
            logger.LogInformation("Queued startup patch detection job.");
        }

        if (schedule.EnableChampionAnalyticsIngestion)
        {
            if (!string.IsNullOrWhiteSpace(patchJobId))
            {
                backgroundJobClient.ContinueJobWith<ChampionAnalyticsIngestionJob>(
                    patchJobId,
                    job => job.ExecuteAsync(CancellationToken.None));
            }
            else
            {
                backgroundJobClient.Enqueue<ChampionAnalyticsIngestionJob>(job => job.ExecuteAsync(CancellationToken.None));
            }

            logger.LogInformation("Queued startup champion analytics ingestion job.");
        }

        if (!string.IsNullOrWhiteSpace(patchJobId))
        {
            backgroundJobClient.ContinueJobWith<RefreshChampionAnalyticsJob>(
                patchJobId,
                job => job.ExecuteAdaptiveAsync(CancellationToken.None));
        }
        else
        {
            backgroundJobClient.Enqueue<RefreshChampionAnalyticsJob>(job => job.ExecuteAdaptiveAsync(CancellationToken.None));
        }

        logger.LogInformation("Queued startup adaptive analytics refresh job.");
    }

    private void CleanupHangfireJobs()
    {
        JobStorage.Current?.GetMonitoringApi()?.PurgeJobs();
        RecurringJob.RemoveIfExists(DetectPatchJobId);
        RecurringJob.RemoveIfExists(RetryFailedMatchesJobId);
        RecurringJob.RemoveIfExists(RefreshChampionAnalyticsJobId);
        RecurringJob.RemoveIfExists(RefreshChampionAnalyticsAdaptiveJobId);
        RecurringJob.RemoveIfExists(ChampionAnalyticsIngestionJobId);
        RecurringJob.RemoveIfExists(PollLiveGamesJobId);
        logger.LogInformation("Cleared queued and recurring jobs due to CleanupOnStartup=true.");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
            // wait for 1 minute
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
    }
}
