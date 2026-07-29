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
    public const string WarmDefaultChampionProfilesJobId = "warm-default-champion-profiles";
    public const string ChampionAnalyticsIngestionJobId = "champion-analytics-ingestion";
    public const string SummonerMaintenanceJobId = "summoner-maintenance";
    public const string MatchTimelineBackfillJobId = "match-timeline-backfill";
    public const string RuneSelectionIntegrityBackfillJobId = "rune-selection-integrity-backfill";
    public const string PollLiveGamesJobId = "poll-live-games";
    public const string HighEloProfileRefreshJobId = "high-elo-profile-refresh";
    public const string ProRosterDiscoveryJobId = "pro-roster-discovery";
    public const string RefreshLockLifecycleCleanupJobId = "refresh-lock-lifecycle-cleanup";
    public const string IngestionHealthAlertJobId = "ingestion-health-alert";
    public const string RefreshPrecomputedAnalyticsJobId = "refresh-precomputed-analytics";
    public const string RefreshChampionMatchupsJobId = "refresh-champion-matchups";
    public const string RefreshChampionBuildSnapshotsJobId = "refresh-champion-build-snapshots";
    public const string RefreshProAnalyticsJobId = "refresh-pro-analytics";
    public const string RefreshBuildResourceAnalyticsJobId = "refresh-build-resource-analytics";
    public const string CreateBuildLabGenerationJobId = "create-build-lab-generation";
    public const string PromoteBuildLabGenerationJobId = "promote-build-lab-generation";

    private static readonly HashSet<string> MandatoryBaselineJobIds = new(StringComparer.OrdinalIgnoreCase)
    {
        DetectPatchJobId,
        RetryFailedMatchesJobId,
        ChampionAnalyticsIngestionJobId,
        RefreshLockLifecycleCleanupJobId
    };

    private static readonly string[] KnownJobIdValues =
    [
        DetectPatchJobId,
        RetryFailedMatchesJobId,
        RefreshChampionAnalyticsJobId,
        RefreshChampionAnalyticsAdaptiveJobId,
        WarmDefaultChampionProfilesJobId,
        ChampionAnalyticsIngestionJobId,
        SummonerMaintenanceJobId,
        MatchTimelineBackfillJobId,
        RuneSelectionIntegrityBackfillJobId,
        PollLiveGamesJobId,
        HighEloProfileRefreshJobId,
        ProRosterDiscoveryJobId,
        RefreshLockLifecycleCleanupJobId,
        IngestionHealthAlertJobId,
        RefreshPrecomputedAnalyticsJobId,
        RefreshChampionMatchupsJobId,
        RefreshChampionBuildSnapshotsJobId,
        RefreshProAnalyticsJobId,
        RefreshBuildResourceAnalyticsJobId,
        CreateBuildLabGenerationJobId,
        PromoteBuildLabGenerationJobId
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
                WarmDefaultChampionProfilesJobId,
                "Jobs:Schedule:WarmDefaultChampionProfilesCron",
                schedule.WarmDefaultChampionProfilesCron,
                schedule.EnableWarmDefaultChampionProfiles,
                ConfigureWarmDefaultChampionProfiles),
            CreateDescriptor(
                RefreshPrecomputedAnalyticsJobId,
                "Jobs:Schedule:RefreshPrecomputedAnalyticsCron",
                schedule.RefreshPrecomputedAnalyticsCron,
                schedule.EnableRefreshPrecomputedAnalytics,
                ConfigureRefreshPrecomputedAnalytics),
            CreateDescriptor(
                RefreshChampionMatchupsJobId,
                "Jobs:Schedule:RefreshChampionMatchupsCron",
                schedule.RefreshChampionMatchupsCron,
                schedule.EnableRefreshChampionMatchups,
                ConfigureRefreshChampionMatchups),
            CreateDescriptor(
                RefreshChampionBuildSnapshotsJobId,
                "Jobs:Schedule:RefreshChampionBuildSnapshotsCron",
                schedule.RefreshChampionBuildSnapshotsCron,
                schedule.EnableRefreshChampionBuildSnapshots,
                ConfigureRefreshChampionBuildSnapshots),
            CreateDescriptor(
                RefreshProAnalyticsJobId,
                "Jobs:Schedule:RefreshProAnalyticsCron",
                schedule.RefreshProAnalyticsCron,
                schedule.EnableRefreshProAnalytics,
                ConfigureRefreshProAnalytics),
            CreateDescriptor(
                RefreshBuildResourceAnalyticsJobId,
                "Jobs:Schedule:RefreshBuildResourceAnalyticsCron",
                schedule.RefreshBuildResourceAnalyticsCron,
                schedule.EnableRefreshBuildResourceAnalytics,
                ConfigureRefreshBuildResourceAnalytics),
            CreateDescriptor(
                CreateBuildLabGenerationJobId,
                "Jobs:Schedule:CreateBuildLabGenerationCron",
                schedule.CreateBuildLabGenerationCron,
                schedule.EnableCreateBuildLabGeneration,
                ConfigureCreateBuildLabGeneration),
            CreateDescriptor(
                PromoteBuildLabGenerationJobId,
                "Jobs:Schedule:PromoteBuildLabGenerationCron",
                schedule.PromoteBuildLabGenerationCron,
                schedule.EnablePromoteBuildLabGeneration,
                ConfigurePromoteBuildLabGeneration),
            CreateDescriptor(
                ChampionAnalyticsIngestionJobId,
                "Jobs:Schedule:ChampionAnalyticsIngestionCron",
                schedule.ChampionAnalyticsIngestionCron,
                schedule.EnableChampionAnalyticsIngestion,
                ConfigureChampionAnalyticsIngestion),
            CreateDescriptor(
                SummonerMaintenanceJobId,
                "Jobs:Schedule:SummonerMaintenanceCron",
                schedule.SummonerMaintenanceCron,
                schedule.EnableSummonerMaintenance,
                ConfigureSummonerMaintenance),
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
                HighEloProfileRefreshJobId,
                "Jobs:Schedule:HighEloProfileRefreshCron",
                schedule.HighEloProfileRefreshCron,
                schedule.EnableHighEloProfileRefresh,
                ConfigureHighEloProfileRefresh),
            CreateDescriptor(
                ProRosterDiscoveryJobId,
                "Jobs:Schedule:ProRosterDiscoveryCron",
                schedule.ProRosterDiscoveryCron,
                schedule.EnableProRosterDiscovery,
                ConfigureProRosterDiscovery),
            CreateDescriptor(
                PollLiveGamesJobId,
                "Jobs:Schedule:LiveGamePollingCron",
                schedule.LiveGamePollingCron,
                isEnabled: false,
                ConfigureLiveGamePolling),
            CreateDescriptor(
                RefreshLockLifecycleCleanupJobId,
                "Jobs:Schedule:RefreshLockLifecycleCleanupCron",
                schedule.RefreshLockLifecycleCleanupCron,
                schedule.EnableRefreshLockLifecycleCleanup,
                ConfigureRefreshLockLifecycleCleanup),
            CreateDescriptor(
                IngestionHealthAlertJobId,
                "Jobs:Schedule:IngestionHealthAlertCron",
                schedule.IngestionHealthAlertCron,
                schedule.EnableIngestionHealthAlert,
                ConfigureIngestionHealthAlert)
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

    private static void ConfigureWarmDefaultChampionProfiles(
        IRecurringJobManager recurringJobManager,
        string cronExpression) =>
        recurringJobManager.AddOrUpdate<WarmDefaultChampionProfilesJob>(
            WarmDefaultChampionProfilesJobId,
            job => job.ExecuteAsync(CancellationToken.None),
            cronExpression,
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

    private static void ConfigureRefreshPrecomputedAnalytics(
        IRecurringJobManager recurringJobManager,
        string cronExpression) =>
        recurringJobManager.AddOrUpdate<RefreshPrecomputedAnalyticsJob>(
            RefreshPrecomputedAnalyticsJobId,
            job => job.ExecuteAsync(CancellationToken.None),
            cronExpression,
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

    private static void ConfigureRefreshChampionMatchups(
        IRecurringJobManager recurringJobManager,
        string cronExpression) =>
        recurringJobManager.AddOrUpdate<RefreshChampionMatchupsJob>(
            RefreshChampionMatchupsJobId,
            job => job.ExecuteAsync(CancellationToken.None),
            cronExpression,
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

    private static void ConfigureRefreshChampionBuildSnapshots(
        IRecurringJobManager recurringJobManager,
        string cronExpression) =>
        recurringJobManager.AddOrUpdate<RefreshChampionBuildSnapshotsJob>(
            RefreshChampionBuildSnapshotsJobId,
            job => job.ExecuteAsync(CancellationToken.None),
            cronExpression,
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

    private static void ConfigureRefreshProAnalytics(
        IRecurringJobManager recurringJobManager,
        string cronExpression) =>
        recurringJobManager.AddOrUpdate<RefreshProAnalyticsJob>(
            RefreshProAnalyticsJobId,
            job => job.ExecuteAsync(CancellationToken.None),
            cronExpression,
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

    private static void ConfigureRefreshBuildResourceAnalytics(
        IRecurringJobManager recurringJobManager,
        string cronExpression) =>
        recurringJobManager.AddOrUpdate<RefreshBuildResourceAnalyticsJob>(
            RefreshBuildResourceAnalyticsJobId,
            job => job.ExecuteAsync(false, false, CancellationToken.None),
            cronExpression,
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

    private static void ConfigureCreateBuildLabGeneration(
        IRecurringJobManager recurringJobManager,
        string cronExpression) =>
        recurringJobManager.AddOrUpdate<CreateBuildLabGenerationJob>(
            CreateBuildLabGenerationJobId,
            job => job.ExecuteAsync(CancellationToken.None),
            cronExpression,
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

    private static void ConfigurePromoteBuildLabGeneration(
        IRecurringJobManager recurringJobManager,
        string cronExpression) =>
        recurringJobManager.AddOrUpdate<PromoteBuildLabGenerationJob>(
            PromoteBuildLabGenerationJobId,
            job => job.ExecuteAsync(CancellationToken.None),
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

    private static void ConfigureSummonerMaintenance(
        IRecurringJobManager recurringJobManager,
        string cronExpression) =>
        recurringJobManager.AddOrUpdate<SummonerMaintenanceJob>(
            SummonerMaintenanceJobId,
            job => job.ExecuteAsync(CancellationToken.None),
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

    private static void ConfigureHighEloProfileRefresh(
        IRecurringJobManager recurringJobManager,
        string cronExpression) =>
        recurringJobManager.AddOrUpdate<AddOrUpdateHighEloProfiles>(
            HighEloProfileRefreshJobId,
            job => job.Execute(CancellationToken.None),
            cronExpression,
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

    private static void ConfigureProRosterDiscovery(
        IRecurringJobManager recurringJobManager,
        string cronExpression) =>
        recurringJobManager.AddOrUpdate<ProRosterDiscoveryJob>(
            ProRosterDiscoveryJobId,
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

    private static void ConfigureIngestionHealthAlert(
        IRecurringJobManager recurringJobManager,
        string cronExpression) =>
        recurringJobManager.AddOrUpdate<IngestionHealthAlertJob>(
            IngestionHealthAlertJobId,
            job => job.ExecuteAsync(CancellationToken.None),
            cronExpression,
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
}
