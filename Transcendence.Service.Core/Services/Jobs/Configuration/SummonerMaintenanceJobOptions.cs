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

    // Whether low-priority maintenance refreshes may widen past current-patch ranked-head into all-modes
    // head + non-ranked backfill when the adaptive budget reports IncludeAllModes. DEFAULT OFF: on the
    // yield-limited personal Riot key, an uncovered summoner's all-modes/non-ranked backfill pulls its
    // entire ancient match history through the rate gate (~20+ min, old-patch yield), which can saturate
    // the discovery lane. Non-ranked profile backfill is handled on-demand by the high-priority
    // RefreshByRiotId path instead. Re-enable only when the key budget genuinely supports it.
    public bool EnableAllModesWidening { get; set; }
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
