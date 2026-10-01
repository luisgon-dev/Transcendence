namespace Transcendence.Service.Core.Services.Jobs;

/// <summary>
/// Hangfire queue names, and the set of queues the worker host actually serves. Every <c>[Queue]</c>
/// attribute and every <c>AddHangfireServer</c> registration references these constants: a job sent to
/// a queue no server listens on is accepted by Hangfire and then never runs, with nothing failing.
/// <c>HangfireQueueRoutingTests</c> holds every <c>[Queue]</c> attribute to <see cref="Served"/>.
/// </summary>
public static class HangfireQueues
{
    /// <summary>User-initiated work (profile refreshes, live-game probes). Highest priority on the main pool.</summary>
    public const string RefreshHigh = "refresh-high";

    /// <summary>Hangfire's default queue: jobs without a <c>[Queue]</c> attribute land here.</summary>
    public const string Default = "default";

    /// <summary>Broad background maintenance (ladder crawl, lock cleanup). Lowest priority on the main pool.</summary>
    public const string RefreshLow = "refresh-low";

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

    /// <summary>
    /// Reserved lane for match discovery: the per-region champion-analytics ingestion + summoner
    /// maintenance producers and the analytics summoner-refresh consumers they enqueue. Served by its
    /// own dedicated pool so discovery (which actually fetches new current-patch matches) is never
    /// starved behind the broad <c>refresh-low</c> maintenance backlog. This is the heaviest pipeline
    /// and was previously the only one without an isolated lane.
    /// </summary>
    public const string Discovery = "discovery";

    /// <summary>
    /// Reserved lane for signed-in manual profile full-history backfills. These jobs page through a
    /// player's Match-V5 searchable account history and persist compact per-summoner facts that survive
    /// match-detail archive pruning, so they are intentionally isolated from the quick refresh queues.
    /// </summary>
    public const string HistoryBackfill = "history-backfill";

    /// <summary>Queues the main worker pool serves, highest priority first.</summary>
    public static readonly IReadOnlyList<string> MainServer = [RefreshHigh, Default, RefreshLow];

    /// <summary>Every queue some worker pool serves. A queue outside this set is never drained.</summary>
    public static readonly IReadOnlySet<string> Served = new HashSet<string>(
        [.. MainServer, AnalyticsWarm, TimelineIngest, Discovery, HistoryBackfill],
        StringComparer.Ordinal);
}
