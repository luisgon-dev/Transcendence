namespace Transcendence.Service.Core.Services.Jobs.Configuration;

public class SummonerMaintenanceJobOptions
{
    public int MaxCandidateSummonersPerRun { get; set; } = 60;
    public int MaxRefreshJobsToQueuePerRun { get; set; } = 4;
    public int DataStaleAfterMinutes { get; set; } = 90;
    public int RefreshLockMinutes { get; set; } = 10;
    public bool PrioritizeFavoriteSummoners { get; set; } = true;
    public bool PrioritizeTrackedHighValueSummoners { get; set; } = true;
    public bool PrioritizeRankedHighEloSummoners { get; set; } = true;
    public bool PauseWhenApiPriorityRefreshActive { get; set; } = true;
    public int NewPatchRampHours { get; set; } = 48;

    // Self-pacing intervals for the single self-paced recurring registration (see
    // ChampionAnalyticsIngestionJobOptions for the mechanism). Maintenance runs less aggressively than
    // ingestion, so its steady cadence is longer.
    public int SelfPaceRampIntervalMinutes { get; set; } = 5;
    public int SelfPaceSteadyIntervalMinutes { get; set; } = 10;

    public int RampMaxCandidateSummonersPerRun { get; set; } = 250;
    public int RampMaxRefreshJobsToQueuePerRun { get; set; } = 12;
    public int RampDataStaleAfterMinutes { get; set; } = 30;
    public List<string> HighEloTiers { get; set; } =
    [
        "CHALLENGER",
        "GRANDMASTER",
        "MASTER",
        "DIAMOND",
        "EMERALD"
    ];
}
