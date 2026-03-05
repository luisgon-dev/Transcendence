using Hangfire;
using Microsoft.Extensions.Options;
using Transcendence.Service.Core.Services.Jobs;

namespace Transcendence.Service.Core.Services.Jobs.Configuration;

public interface IWorkerRecurringJobPolicy
{
    IReadOnlyList<WorkerRecurringJobDescriptor> BuildDescriptors(WorkerJobScheduleOptions schedule);
    string ResolveProfile(WorkerJobScheduleOptions schedule);
    IReadOnlyCollection<string> KnownJobIds { get; }
}

public sealed record WorkerRecurringJobDescriptor(
    string JobId,
    string CronExpression,
    string CronSource,
    bool IsEnabled,
    bool IsMandatoryBaseline,
    Action<IRecurringJobManager, string> ConfigureRecurringJob)
{
    public void Apply(IRecurringJobManager recurringJobManager) =>
        ConfigureRecurringJob(recurringJobManager, CronExpression);
}

public sealed class WorkerRecurringJobPolicy(
    IOptions<WorkerSchedulingProfileOptions> profileOptionsAccessor) : IWorkerRecurringJobPolicy
{
    public const string DetectPatchJobId = "detect-patch";
    public const string RetryFailedMatchesJobId = "retry-failed-matches";
    public const string RefreshChampionAnalyticsJobId = "refresh-champion-analytics";
    public const string RefreshChampionAnalyticsAdaptiveJobId = "refresh-champion-analytics-adaptive";
    public const string RefreshChampionAnalyticsRampJobId = "refresh-champion-analytics-ramp";
    public const string ChampionAnalyticsIngestionJobId = "champion-analytics-ingestion";
    public const string ChampionAnalyticsIngestionRampJobId = "champion-analytics-ingestion-ramp";
    public const string SummonerMaintenanceJobId = "summoner-maintenance";
    public const string SummonerMaintenanceRampJobId = "summoner-maintenance-ramp";
    public const string MatchTimelineBackfillJobId = "match-timeline-backfill";
    public const string RuneSelectionIntegrityBackfillJobId = "rune-selection-integrity-backfill";
    public const string PollLiveGamesJobId = "poll-live-games";
    public const string RefreshLockLifecycleCleanupJobId = "refresh-lock-lifecycle-cleanup";

    private static readonly HashSet<string> MandatoryBaselineJobIds = new(StringComparer.OrdinalIgnoreCase)
    {
        DetectPatchJobId,
        RetryFailedMatchesJobId,
        RefreshChampionAnalyticsJobId,
        ChampionAnalyticsIngestionJobId,
        SummonerMaintenanceJobId,
        RefreshLockLifecycleCleanupJobId
    };

    private static readonly string[] KnownJobIdValues =
    [
        DetectPatchJobId,
        RetryFailedMatchesJobId,
        RefreshChampionAnalyticsJobId,
        RefreshChampionAnalyticsAdaptiveJobId,
        RefreshChampionAnalyticsRampJobId,
        ChampionAnalyticsIngestionJobId,
        ChampionAnalyticsIngestionRampJobId,
        SummonerMaintenanceJobId,
        SummonerMaintenanceRampJobId,
        MatchTimelineBackfillJobId,
        RuneSelectionIntegrityBackfillJobId,
        PollLiveGamesJobId,
        RefreshLockLifecycleCleanupJobId
    ];

    private readonly WorkerSchedulingProfileOptions profileOptions = profileOptionsAccessor.Value;

    public IReadOnlyCollection<string> KnownJobIds => KnownJobIdValues;

    public string ResolveProfile(WorkerJobScheduleOptions schedule)
    {
        if (!string.IsNullOrWhiteSpace(schedule.Profile))
            return schedule.Profile.Trim();

        if (!string.IsNullOrWhiteSpace(schedule.DefaultProfile))
            return schedule.DefaultProfile.Trim();

        return "default";
    }

    public IReadOnlyList<WorkerRecurringJobDescriptor> BuildDescriptors(WorkerJobScheduleOptions schedule)
    {
        var descriptors = new List<WorkerRecurringJobDescriptor>
        {
            CreateDescriptor(
                DetectPatchJobId,
                "Jobs:Schedule:DetectPatchCron",
                schedule.DetectPatchCron,
                isEnabled: true,
                ConfigureDetectPatch),
            CreateDescriptor(
                RetryFailedMatchesJobId,
                "Jobs:Schedule:RetryFailedMatchesCron",
                schedule.RetryFailedMatchesCron,
                isEnabled: true,
                ConfigureRetryFailedMatches),
            CreateDescriptor(
                RefreshChampionAnalyticsJobId,
                "Jobs:Schedule:RefreshChampionAnalyticsDailyCron",
                schedule.RefreshChampionAnalyticsDailyCron,
                isEnabled: true,
                ConfigureRefreshChampionAnalytics),
            CreateDescriptor(
                RefreshChampionAnalyticsAdaptiveJobId,
                "Jobs:Schedule:RefreshChampionAnalyticsAdaptiveCron",
                schedule.RefreshChampionAnalyticsAdaptiveCron,
                schedule.EnableAdaptiveAnalyticsRefresh,
                ConfigureRefreshChampionAnalyticsAdaptive),
            CreateDescriptor(
                RefreshChampionAnalyticsRampJobId,
                "Jobs:Schedule:RefreshChampionAnalyticsRampCron",
                schedule.RefreshChampionAnalyticsRampCron,
                schedule.EnableAdaptiveAnalyticsRefresh && schedule.EnableNewPatchRamp,
                ConfigureRefreshChampionAnalyticsRamp),
            CreateDescriptor(
                ChampionAnalyticsIngestionJobId,
                "Jobs:Schedule:ChampionAnalyticsIngestionCron",
                schedule.ChampionAnalyticsIngestionCron,
                schedule.EnableChampionAnalyticsIngestion,
                ConfigureChampionAnalyticsIngestion),
            CreateDescriptor(
                ChampionAnalyticsIngestionRampJobId,
                "Jobs:Schedule:ChampionAnalyticsIngestionRampCron",
                schedule.ChampionAnalyticsIngestionRampCron,
                schedule.EnableChampionAnalyticsIngestion && schedule.EnableNewPatchRamp,
                ConfigureChampionAnalyticsIngestionRamp),
            CreateDescriptor(
                SummonerMaintenanceJobId,
                "Jobs:Schedule:SummonerMaintenanceCron",
                schedule.SummonerMaintenanceCron,
                schedule.EnableSummonerMaintenance,
                ConfigureSummonerMaintenance),
            CreateDescriptor(
                SummonerMaintenanceRampJobId,
                "Jobs:Schedule:SummonerMaintenanceRampCron",
                schedule.SummonerMaintenanceRampCron,
                schedule.EnableSummonerMaintenance && schedule.EnableNewPatchRamp,
                ConfigureSummonerMaintenanceRamp),
            CreateDescriptor(
                MatchTimelineBackfillJobId,
                "Jobs:Schedule:MatchTimelineBackfillCron",
                schedule.MatchTimelineBackfillCron,
                schedule.EnableMatchTimelineBackfill,
                ConfigureMatchTimelineBackfill),
            CreateDescriptor(
                RuneSelectionIntegrityBackfillJobId,
                "Jobs:Schedule:RuneSelectionIntegrityBackfillCron",
                schedule.RuneSelectionIntegrityBackfillCron,
                schedule.EnableRuneSelectionIntegrityBackfill,
                ConfigureRuneSelectionIntegrityBackfill),
            CreateDescriptor(
                PollLiveGamesJobId,
                "Jobs:Schedule:LiveGamePollingCron",
                schedule.LiveGamePollingCron,
                isEnabled: true,
                ConfigureLiveGamePolling),
            CreateDescriptor(
                RefreshLockLifecycleCleanupJobId,
                "Jobs:Schedule:RefreshLockLifecycleCleanupCron",
                schedule.RefreshLockLifecycleCleanupCron,
                schedule.EnableRefreshLockLifecycleCleanup,
                ConfigureRefreshLockLifecycleCleanup)
        };

        var profileName = ResolveProfile(schedule);
        if (string.IsNullOrWhiteSpace(profileName))
            return descriptors;

        if (!profileOptions.Profiles.TryGetValue(profileName, out var profile))
            return descriptors;

        for (var i = 0; i < descriptors.Count; i++)
        {
            var descriptor = descriptors[i];
            if (!profile.JobOverrides.TryGetValue(descriptor.JobId, out var jobOverride))
                continue;

            var hasCronOverride = !string.IsNullOrWhiteSpace(jobOverride.Cron);
            var cronExpression = hasCronOverride ? jobOverride.Cron!.Trim() : descriptor.CronExpression;
            var cronSource = hasCronOverride
                ? $"Jobs:SchedulingProfiles:Profiles:{profileName}:JobOverrides:{descriptor.JobId}:Cron"
                : descriptor.CronSource;

            descriptors[i] = descriptor with
            {
                CronExpression = cronExpression,
                CronSource = cronSource,
                IsEnabled = jobOverride.Enabled ?? descriptor.IsEnabled,
                IsMandatoryBaseline = jobOverride.MandatoryBaseline ?? descriptor.IsMandatoryBaseline
            };
        }

        return descriptors;
    }

    private static WorkerRecurringJobDescriptor CreateDescriptor(
        string jobId,
        string cronSource,
        string cronExpression,
        bool isEnabled,
        Action<IRecurringJobManager, string> configureRecurringJob) =>
        new(
            jobId,
            cronExpression,
            cronSource,
            isEnabled,
            MandatoryBaselineJobIds.Contains(jobId),
            configureRecurringJob);

    private static void ConfigureDetectPatch(IRecurringJobManager recurringJobManager, string cronExpression) =>
        recurringJobManager.AddOrUpdate<UpdateStaticDataJob>(
            DetectPatchJobId,
            x => x.Execute(CancellationToken.None),
            cronExpression,
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

    private static void ConfigureRetryFailedMatches(IRecurringJobManager recurringJobManager, string cronExpression) =>
        recurringJobManager.AddOrUpdate<RetryFailedMatchesJob>(
            RetryFailedMatchesJobId,
            job => job.Execute(CancellationToken.None),
            cronExpression,
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

    private static void ConfigureRefreshChampionAnalytics(
        IRecurringJobManager recurringJobManager,
        string cronExpression) =>
        recurringJobManager.AddOrUpdate<RefreshChampionAnalyticsJob>(
            RefreshChampionAnalyticsJobId,
            job => job.ExecuteAsync(CancellationToken.None),
            cronExpression,
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

    private static void ConfigureRefreshChampionAnalyticsAdaptive(
        IRecurringJobManager recurringJobManager,
        string cronExpression) =>
        recurringJobManager.AddOrUpdate<RefreshChampionAnalyticsJob>(
            RefreshChampionAnalyticsAdaptiveJobId,
            job => job.ExecuteAdaptiveAsync(CancellationToken.None),
            cronExpression,
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

    private static void ConfigureRefreshChampionAnalyticsRamp(
        IRecurringJobManager recurringJobManager,
        string cronExpression) =>
        recurringJobManager.AddOrUpdate<RefreshChampionAnalyticsJob>(
            RefreshChampionAnalyticsRampJobId,
            job => job.ExecuteRampAsync(CancellationToken.None),
            cronExpression,
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

    private static void ConfigureChampionAnalyticsIngestion(
        IRecurringJobManager recurringJobManager,
        string cronExpression) =>
        recurringJobManager.AddOrUpdate<ChampionAnalyticsIngestionJob>(
            ChampionAnalyticsIngestionJobId,
            job => job.ExecuteAsync(CancellationToken.None),
            cronExpression,
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

    private static void ConfigureChampionAnalyticsIngestionRamp(
        IRecurringJobManager recurringJobManager,
        string cronExpression) =>
        recurringJobManager.AddOrUpdate<ChampionAnalyticsIngestionJob>(
            ChampionAnalyticsIngestionRampJobId,
            job => job.ExecuteRampAsync(CancellationToken.None),
            cronExpression,
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

    private static void ConfigureSummonerMaintenance(
        IRecurringJobManager recurringJobManager,
        string cronExpression) =>
        recurringJobManager.AddOrUpdate<SummonerMaintenanceJob>(
            SummonerMaintenanceJobId,
            job => job.ExecuteAsync(CancellationToken.None),
            cronExpression,
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

    private static void ConfigureSummonerMaintenanceRamp(
        IRecurringJobManager recurringJobManager,
        string cronExpression) =>
        recurringJobManager.AddOrUpdate<SummonerMaintenanceJob>(
            SummonerMaintenanceRampJobId,
            job => job.ExecuteRampAsync(CancellationToken.None),
            cronExpression,
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

    private static void ConfigureMatchTimelineBackfill(
        IRecurringJobManager recurringJobManager,
        string cronExpression) =>
        recurringJobManager.AddOrUpdate<MatchTimelineBackfillJob>(
            MatchTimelineBackfillJobId,
            job => job.ExecuteAsync(CancellationToken.None),
            cronExpression,
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

    private static void ConfigureRuneSelectionIntegrityBackfill(
        IRecurringJobManager recurringJobManager,
        string cronExpression) =>
        recurringJobManager.AddOrUpdate<RuneSelectionIntegrityBackfillJob>(
            RuneSelectionIntegrityBackfillJobId,
            job => job.ExecuteAsync(CancellationToken.None),
            cronExpression,
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

    private static void ConfigureLiveGamePolling(IRecurringJobManager recurringJobManager, string cronExpression) =>
        recurringJobManager.AddOrUpdate<LiveGamePollingJob>(
            PollLiveGamesJobId,
            job => job.ExecuteAsync(CancellationToken.None),
            cronExpression,
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

    private static void ConfigureRefreshLockLifecycleCleanup(
        IRecurringJobManager recurringJobManager,
        string cronExpression) =>
        recurringJobManager.AddOrUpdate<RefreshLockLifecycleJob>(
            RefreshLockLifecycleCleanupJobId,
            job => job.ExecuteAsync(CancellationToken.None),
            cronExpression,
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
}
