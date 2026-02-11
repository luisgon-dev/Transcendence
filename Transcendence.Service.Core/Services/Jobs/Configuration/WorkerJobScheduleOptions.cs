namespace Transcendence.Service.Core.Services.Jobs.Configuration;

public class WorkerJobScheduleOptions
{
    public string DetectPatchCron { get; set; } = "0 */6 * * *";
    public string RetryFailedMatchesCron { get; set; } = "0 * * * *";
    public string RefreshChampionAnalyticsDailyCron { get; set; } = "0 4 * * *";
    public string RefreshChampionAnalyticsAdaptiveCron { get; set; } = "*/30 * * * *";
    public string ChampionAnalyticsIngestionCron { get; set; } = "*/30 * * * *";
    public string RuneSelectionIntegrityBackfillCron { get; set; } = "*/15 * * * *";
    public string LiveGamePollingCron { get; set; } = "*/2 * * * *";
    public bool EnableAdaptiveAnalyticsRefresh { get; set; } = true;
    public bool EnableChampionAnalyticsIngestion { get; set; } = true;
    public bool EnableRuneSelectionIntegrityBackfill { get; set; } = true;
    public bool CleanupOnStartup { get; set; } = false;
    public bool RunPatchDetectionOnStartup { get; set; } = false;
}
