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

    // Snowball frontier: prefer never-refreshed summoners discovered as participants of freshly-ingested
    // matches (stub rows minted by MatchService with UpdatedAt = MinValue). They are guaranteed-active and
    // uncovered, so they maximize new matches per refresh. This is the primary breadth lever.
    public bool PrioritizeSnowballFrontier { get; set; } = true;

    // Long-tail rotation: random starting offset into the stale fallback pool so successive runs walk
    // different slices instead of re-selecting the same stalest head. Bounded so the Skip stays cheap on
    // the (PlatformRegion, UpdatedAt) index. 0 disables rotation (deterministic stalest-first).
    public int FallbackRotationMaxOffset { get; set; } = 50000;
    public bool PauseWhenApiPriorityRefreshActive { get; set; } = true;
    public int NewPatchRampHours { get; set; } = 48;

    // Self-pacing: the single recurring registration fires at a fast heartbeat; the dispatcher acquires a
    // pacing lock with a TTL of the interval below (ramp while within NewPatchRampHours of a patch
    // release, steady otherwise) and skips the fan-out while it is held. This replaces the old separate
    // steady + ramp recurring jobs with one self-paced job that tightens cadence on a fresh patch.
    public int SelfPaceRampIntervalMinutes { get; set; } = 2;
    public int SelfPaceSteadyIntervalMinutes { get; set; } = 5;

    public int RampDataStaleAfterMinutes { get; set; } = 30;
    public int RampMaxCandidateSummonersPerRun { get; set; } = 250;
    public int RampMinRefreshJobsToQueuePerRun { get; set; } = 6;
    public int RampMaxRefreshJobsToQueuePerRun { get; set; } = 20;

    // Discovery-lane backpressure. The producers fan refresh consumers onto the discovery queue; if the
    // queue is already deep, the 10 discovery workers — not producer output — are the bottleneck, so
    // enqueuing more is waste and risks the unbounded regrowth that caused the original ~100k clog. Above
    // the soft cap the per-run queue target scales linearly toward zero; at/above the hard cap the
    // producer enqueues nothing this cycle and waits for the workers to drain. A generous hard cap still
    // leaves the 10 workers ~hours of runway, so a legitimate new-patch ramp is never throttled early.
    public int DiscoveryQueueBackpressureSoftCap { get; set; } = 5000;
    public int DiscoveryQueueBackpressureHardCap { get; set; } = 10000;

    public List<string> HighEloTiers { get; set; } =
    [
        "CHALLENGER",
        "GRANDMASTER",
        "MASTER",
        "DIAMOND",
        "EMERALD"
    ];
}
