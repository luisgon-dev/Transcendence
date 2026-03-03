namespace Transcendence.Service.Core.Services.Jobs.Configuration;

public class WorkerJobScheduleOptions
{
    public string DetectPatchCron { get; set; } = "0 */6 * * *";
    public string RetryFailedMatchesCron { get; set; } = "0 * * * *";
    public string RefreshChampionAnalyticsDailyCron { get; set; } = "0 4 * * *";
    public string RefreshChampionAnalyticsAdaptiveCron { get; set; } = "*/30 * * * *";
    public string RefreshChampionAnalyticsRampCron { get; set; } = "*/5 * * * *";
    public string ChampionAnalyticsIngestionCron { get; set; } = "*/30 * * * *";
    public string ChampionAnalyticsIngestionRampCron { get; set; } = "*/2 * * * *";
    public string SummonerMaintenanceCron { get; set; } = "*/20 * * * *";
    public string SummonerMaintenanceRampCron { get; set; } = "*/5 * * * *";
    public string MatchTimelineBackfillCron { get; set; } = "*/10 * * * *";
    public string RuneSelectionIntegrityBackfillCron { get; set; } = "*/15 * * * *";
    public string LiveGamePollingCron { get; set; } = "*/2 * * * *";
    public bool EnableAdaptiveAnalyticsRefresh { get; set; } = true;
    public bool EnableNewPatchRamp { get; set; } = true;
    public bool EnableChampionAnalyticsIngestion { get; set; } = true;
    public bool EnableSummonerMaintenance { get; set; } = true;
    public bool EnableMatchTimelineBackfill { get; set; } = true;
    public bool EnableRuneSelectionIntegrityBackfill { get; set; } = true;
    public bool CleanupOnStartup { get; set; } = false;
    public bool RunPatchDetectionOnStartup { get; set; } = false;
}
