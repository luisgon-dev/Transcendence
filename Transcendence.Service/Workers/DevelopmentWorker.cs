using Hangfire;
using Microsoft.Extensions.Options;
using Transcendence.Service.Core.Services.Extensions;
using Transcendence.Service.Core.Services.Jobs;
using Transcendence.Service.Core.Services.Jobs.Configuration;

namespace Transcendence.Service.Workers;

public class DevelopmentWorker(
    IBackgroundJobClient backgroundJobClient,
    JobStorage jobStorage,
    IOptions<WorkerJobScheduleOptions> options,
    ILogger<DevelopmentWorker> logger)
    : BackgroundService
{
    private const string DetectPatchJobId = "detect-patch";
    private const string RetryFailedMatchesJobId = "retry-failed-matches";
    private const string RefreshChampionAnalyticsJobId = "refresh-champion-analytics";
    private const string RefreshChampionAnalyticsAdaptiveJobId = "refresh-champion-analytics-adaptive";
    private const string ChampionAnalyticsIngestionJobId = "champion-analytics-ingestion";
    private const string PollLiveGamesJobId = "poll-live-games";

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
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

        var schedule = options.Value;
        if (schedule.CleanupOnStartup)
            CleanupHangfireJobs();

        // Schedule patch detection every 6 hours
        RecurringJob.AddOrUpdate<UpdateStaticDataJob>(
            DetectPatchJobId,
            job => job.Execute(CancellationToken.None),
            schedule.DetectPatchCron,
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

        RecurringJob.AddOrUpdate<RetryFailedMatchesJob>(
            RetryFailedMatchesJobId,
            job => job.Execute(CancellationToken.None),
            schedule.RetryFailedMatchesCron,
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

        // Schedule analytics refresh daily at 4 AM UTC
        RecurringJob.AddOrUpdate<RefreshChampionAnalyticsJob>(
            RefreshChampionAnalyticsJobId,
            job => job.ExecuteAsync(CancellationToken.None),
            schedule.RefreshChampionAnalyticsDailyCron,
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

        if (schedule.EnableAdaptiveAnalyticsRefresh)
        {
            RecurringJob.AddOrUpdate<RefreshChampionAnalyticsJob>(
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
            RecurringJob.AddOrUpdate<ChampionAnalyticsIngestionJob>(
                ChampionAnalyticsIngestionJobId,
                job => job.ExecuteAsync(CancellationToken.None),
                schedule.ChampionAnalyticsIngestionCron,
                new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
        }
        else
        {
            RecurringJob.RemoveIfExists(ChampionAnalyticsIngestionJobId);
        }

        RecurringJob.AddOrUpdate<LiveGamePollingJob>(
            PollLiveGamesJobId,
            job => job.ExecuteAsync(CancellationToken.None),
            schedule.LiveGamePollingCron,
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

        if (schedule.RunPatchDetectionOnStartup)
            backgroundJobClient.Enqueue<UpdateStaticDataJob>(x => x.Execute(CancellationToken.None));

        // One-time backfill: fix matches ingested before the FetchStatus bug was fixed
        backgroundJobClient.Enqueue<BackfillMatchStatusJob>(job => job.ExecuteAsync(CancellationToken.None));

        return Task.CompletedTask;
    }

    private void CleanupHangfireJobs()
    {
        // clear any queued job or failed jobs
        JobStorage.Current?.GetMonitoringApi()?.PurgeJobs();
        RecurringJob.RemoveIfExists(DetectPatchJobId);
        RecurringJob.RemoveIfExists(RetryFailedMatchesJobId);
        RecurringJob.RemoveIfExists(RefreshChampionAnalyticsJobId);
        RecurringJob.RemoveIfExists(RefreshChampionAnalyticsAdaptiveJobId);
        RecurringJob.RemoveIfExists(ChampionAnalyticsIngestionJobId);
        RecurringJob.RemoveIfExists(PollLiveGamesJobId);
        logger.LogInformation("Cleared all jobs");
    }
}
