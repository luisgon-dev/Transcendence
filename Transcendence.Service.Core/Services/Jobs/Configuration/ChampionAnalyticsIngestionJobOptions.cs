namespace Transcendence.Service.Core.Services.Jobs.Configuration;

public class ChampionAnalyticsIngestionJobOptions
{
    public int MinimumSuccessfulMatchesForCurrentPatch { get; set; } = 200;
    public int TargetSuccessfulMatchesForCurrentPatch { get; set; } = 400;
    public int DataStaleAfterMinutes { get; set; } = 180;
    public int MaxCandidateSummonersPerRun { get; set; } = 75;
    public int MinRefreshJobsToQueuePerRun { get; set; } = 1;
    public int MaxRefreshJobsToQueuePerRun { get; set; } = 6;
    public int RefreshLockMinutes { get; set; } = 10;
    public bool PrioritizeFavoriteSummoners { get; set; } = true;
    public bool FallbackToTrackedSummoners { get; set; } = true;
    public bool PauseWhenApiPriorityRefreshActive { get; set; } = true;
}
