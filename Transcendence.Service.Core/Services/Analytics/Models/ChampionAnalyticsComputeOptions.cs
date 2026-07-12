namespace Transcendence.Service.Core.Services.Analytics.Models;

public class ChampionAnalyticsComputeOptions
{
    public int MinimumGamesRequired { get; set; } = 100;
    public int MaturingPatchMinimumGamesRequired { get; set; } = 70;
    public int EarlyPatchMinimumGamesRequired { get; set; } = 40;
    public int BootstrapPatchMinimumGamesRequired { get; set; } = 10;
    public int BootstrapWindowHours { get; set; } = 24;
    public int ProvisionalWindowHours { get; set; } = 96;
    public int MaturingWindowHours { get; set; } = 240;

    /// <summary>
    /// Upper bound on tracked-pro participant rows materialized per pro-builds computation.
    /// Caps the cost of the heavy item/rune collection projection so the wide
    /// (role=ALL + scope=all + region=ALL) pool cannot command-timeout. Rows are taken
    /// most-recent-first, which matches the "recent pro/high-MMR builds" surface.
    /// </summary>
    public int ProBuildMaxParticipantRows { get; set; } = 1500;

    /// <summary>
    /// Cache lifetime (minutes) applied to a freshly-computed empty / zero-sample analytics payload
    /// instead of the 24h analytics TTL. Keeps "no data yet" answers cheap to serve while letting
    /// newly-ingested games surface within minutes rather than waiting out the 24h patch-tag
    /// invalidation. Kept small (single-digit-to-low-double-digit minutes).
    /// </summary>
    public int EmptyResultTtlMinutes { get; set; } = 10;
}
