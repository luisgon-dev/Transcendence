namespace Transcendence.Service.Core.Services.Jobs.Configuration;

public class WorkerJobScheduleOptions
{
    public string Profile { get; set; } = "default";
    public string DefaultProfile { get; set; } = "default";
    public string DetectPatchCron { get; set; } = "0 */6 * * *";
    public string RetryFailedMatchesCron { get; set; } = "0 * * * *";
    public string RefreshChampionAnalyticsDailyCron { get; set; } = "0 4 * * *";
    // Heartbeat cadence — the job self-paces internally (adaptive cooldown / ramp-vs-steady interval),
    // so these fire often and the job decides whether a tick does real work.
    public string RefreshChampionAnalyticsAdaptiveCron { get; set; } = "*/5 * * * *";
    public string WarmDefaultChampionProfilesCron { get; set; } = "0 * * * *";
    // Tabular core only; heavier response snapshots and matchups have independent ownership below.
    public string RefreshPrecomputedAnalyticsCron { get; set; } = "30 * * * *";
    // Matchups are incremental/resumable and must not wait behind the much longer build snapshot sweep.
    public string RefreshChampionMatchupsCron { get; set; } = "35 * * * *";
    // Full serialized build responses are expensive and atomically replaced; refresh them less often.
    public string RefreshChampionBuildSnapshotsCron { get; set; } = "10 */6 * * *";
    // Lightweight roster-backed snapshots run independently so a heavy matchup timeout cannot stale pro surfaces.
    public string RefreshProAnalyticsCron { get; set; } = "20 * * * *";
    // Independent from the champion precompute job, which may run long or fail on matchup aggregation.
    public string RefreshBuildResourceAnalyticsCron { get; set; } = "40 * * * *";
    public string CreateBuildLabGenerationCron { get; set; } = "15 2 * * *";
    public string PromoteBuildLabGenerationCron { get; set; } = "*/10 * * * *";
    public string ChampionAnalyticsIngestionCron { get; set; } = "*/2 * * * *";
    public string SummonerMaintenanceCron { get; set; } = "*/5 * * * *";
    public string MatchTimelineBackfillCron { get; set; } = "*/10 * * * *";
    public string RuneSelectionIntegrityBackfillCron { get; set; } = "*/15 * * * *";
    public string LiveGamePollingCron { get; set; } = "*/2 * * * *";
    public string RefreshLockLifecycleCleanupCron { get; set; } = "*/5 * * * *";
    public string IngestionHealthAlertCron { get; set; } = "*/5 * * * *";
    public bool EnableIngestionHealthAlert { get; set; } = true;
    public bool EnableAdaptiveAnalyticsRefresh { get; set; } = true;
    public bool EnableWarmDefaultChampionProfiles { get; set; } = true;
    public bool EnableRefreshPrecomputedAnalytics { get; set; } = true;
    public bool EnableRefreshChampionMatchups { get; set; } = true;
    public bool EnableRefreshChampionBuildSnapshots { get; set; } = true;
    public bool EnableRefreshProAnalytics { get; set; } = true;
    public bool EnableRefreshBuildResourceAnalytics { get; set; } = true;
    public bool EnableCreateBuildLabGeneration { get; set; }
    public bool EnablePromoteBuildLabGeneration { get; set; }
    public bool EnableChampionAnalyticsIngestion { get; set; } = true;
    public bool EnableSummonerMaintenance { get; set; } = true;
    public bool EnableMatchTimelineBackfill { get; set; } = true;
    public bool EnableRuneSelectionIntegrityBackfill { get; set; } = true;
    public bool EnableHighEloProfileRefresh { get; set; } = true;
    public string HighEloProfileRefreshCron { get; set; } = "0 */12 * * *";
    public bool EnableProRosterDiscovery { get; set; } = true;
    public string ProRosterDiscoveryCron { get; set; } = "15 3 * * *";
    public bool EnableRefreshLockLifecycleCleanup { get; set; } = true;
    public int RefreshLockLifecycleForensicsWindowMinutes { get; set; } = 30;
    public int RefreshLockLifecycleCleanupBatchSize { get; set; } = 250;
    public int RefreshLockLifecycleCleanupMaxBatchesPerRun { get; set; } = 8;
    public bool CleanupOnStartup { get; set; } = false;
    public bool RunPatchDetectionOnStartup { get; set; } = false;
    public bool PurgeBacklogOnPatchRolloverOnStartup { get; set; } = true;
    public int StartupIntegrityMaxAttempts { get; set; } = 3;
    public int StartupIntegrityRetryBackoffSeconds { get; set; } = 2;
}
