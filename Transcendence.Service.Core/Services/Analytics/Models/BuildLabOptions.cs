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
    public int MatchBatchSize { get; set; } = 500;

    /// <summary>
    /// Matches counted per job run. Bounds a run so the one-time backfill of a fresh patch proceeds over
    /// several scheduled runs instead of occupying a worker slot for hours.
    /// </summary>
    public int MaxMatchesPerRun { get; set; } = 20_000;

    /// <summary>Patches before the active one that the refresh keeps topping up with late matches.</summary>
    public int PriorPatchesToRefresh { get; set; } = 2;

    /// <summary>Patches whose counts are kept; older ones are deleted.</summary>
    public int PatchesToRetain { get; set; } = 4;

    public int CommandTimeoutSeconds { get; set; } = 600;
}
