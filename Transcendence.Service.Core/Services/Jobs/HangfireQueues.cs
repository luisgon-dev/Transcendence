namespace Transcendence.Service.Core.Services.Jobs;

/// <summary>
/// Hangfire queue names. Most queues are referenced as inline literals at the worker host
/// (<c>refresh-high</c>/<c>default</c>/<c>refresh-low</c> and their <c>tft-*</c> peers); this
/// constant exists because the reserved analytics lane is referenced from several places
/// (the dedicated <c>AddHangfireServer</c> registration plus the jobs' <c>[Queue]</c> attributes)
/// and must not drift.
/// </summary>
public static class HangfireQueues
{
    /// <summary>
    /// Reserved lane for the jobs that keep champion analytics warm and fresh
    /// (default-profile warm + adaptive/ramp analytics refresh). Served by its own dedicated
    /// <c>BackgroundJobServer</c> worker pool that the main pool does not touch, so these jobs
    /// always have free workers no matter how backed up the shared refresh queues get.
    /// </summary>
    public const string AnalyticsWarm = "analytics-warm";

    /// <summary>
    /// Reserved lane for per-match timeline ingestion. Served by its own dedicated worker pool so a
    /// large re-ingestion backlog drains at the Riot rate limit without being starved by the (much
    /// larger) shared <c>refresh-low</c> backlog — and without the timeline jobs starving it in turn.
    /// </summary>
    public const string TimelineIngest = "timeline-ingest";
}
