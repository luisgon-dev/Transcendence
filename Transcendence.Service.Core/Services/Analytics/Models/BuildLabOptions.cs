namespace Transcendence.Service.Core.Services.Analytics.Models;

/// <summary>Bound to <c>Analytics:BuildLab</c>.</summary>
public sealed class BuildLabOptions
{
    /// <summary>
    /// Turns Build Lab on end to end: serving, the stats refresh, AND the detailed timeline capture
    /// (one-minute frames, item lifecycle events, event payloads) the refresh reads. Turning it off
    /// stops that capture, so the data it needs stops arriving.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>Matches counted per transaction.</summary>
    public int MatchBatchSize { get; set; } = 25;

    /// <summary>
    /// Matches counted per job run. Bounds a run so the one-time backfill of a fresh patch proceeds over
    /// several scheduled runs instead of occupying a worker slot for hours.
    /// </summary>
    public int MaxMatchesPerRun { get; set; } = 500;

    /// <summary>Patches before the active one that the refresh keeps topping up with late matches.</summary>
    public int PriorPatchesToRefresh { get; set; } = 2;

    /// <summary>Patches whose counts are kept; older ones are deleted.</summary>
    public int PatchesToRetain { get; set; } = 4;

    public int CommandTimeoutSeconds { get; set; } = 60;

    /// <summary>Stop starting batches after this elapsed budget. Committed batches remain counted.</summary>
    public int MaxRunSeconds { get; set; } = 120;
    /// <summary>Yield between transactions to keep foreground reads responsive.</summary>
    public int BatchDelayMilliseconds { get; set; } = 500;
    /// <summary>Cluster WAL growth budget per run; includes other writers and deliberately fails closed.</summary>
    public int MaxWalMegabytesPerRun { get; set; } = 32;
}
