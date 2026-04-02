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
    public bool PrioritizeTrackedHighValueSummoners { get; set; } = true;
    public bool PrioritizeRankedHighEloSummoners { get; set; } = true;
    public bool FallbackToTrackedSummoners { get; set; } = true;
    public bool PauseWhenApiPriorityRefreshActive { get; set; } = true;
    public int NewPatchRampHours { get; set; } = 48;
    public int RampDataStaleAfterMinutes { get; set; } = 30;
    public int RampMaxCandidateSummonersPerRun { get; set; } = 250;
    public int RampMinRefreshJobsToQueuePerRun { get; set; } = 6;
    public int RampMaxRefreshJobsToQueuePerRun { get; set; } = 20;
    public List<string> HighEloTiers { get; set; } =
    [
        "CHALLENGER",
        "GRANDMASTER",
        "MASTER",
        "DIAMOND",
        "EMERALD"
    ];
}
