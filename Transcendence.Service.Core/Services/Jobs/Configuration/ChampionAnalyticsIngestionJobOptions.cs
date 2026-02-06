namespace Transcendence.Service.Core.Services.Jobs.Configuration;

public class ChampionAnalyticsIngestionJobOptions
{
    public int MinimumSuccessfulMatchesForCurrentPatch { get; set; } = 200;
    public int DataStaleAfterMinutes { get; set; } = 180;
    public int MaxCandidateSummonersPerRun { get; set; } = 30;
    public int MaxRefreshJobsToQueuePerRun { get; set; } = 2;
    public int RefreshLockMinutes { get; set; } = 10;
    public bool PrioritizeFavoriteSummoners { get; set; } = true;
    public bool FallbackToTrackedSummoners { get; set; } = true;
}
