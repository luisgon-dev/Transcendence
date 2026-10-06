namespace Transcendence.Service.Core.Services.Jobs.Configuration;

/// <summary>
/// Controls the bounded recurring job that keeps every champion's DEFAULT profile-page analytics warm
/// (gap-free refresh-ahead), so the page is a permanent cache hit with fresh stats.
/// </summary>
public class WarmDefaultChampionProfilesJobOptions
{
    /// <summary>
    /// Rank tier to warm at. MUST match the frontend champion-page default (Emerald+, from
    /// <c>DEFAULT_TIERLIST_RANK_TIER</c> in <c>apps/web/lib/ranks.ts</c>) or the warmed keys
    /// will not be the ones the page reads.
    /// </summary>
    public string RankTier { get; set; } = "EMERALD_PLUS";

    /// <summary>Only warm champions with at least this many ranked games on the active patch (skips dead/disabled picks).</summary>
    public int MinimumGamesToWarm { get; set; } = 50;

    /// <summary>Max champions warmed concurrently. Each runs on its own DI scope / DbContext, so &gt;1 is safe; keep modest to yield DB to ingestion.</summary>
    public int MaxConcurrency { get; set; } = 1;
    public int MaxChampionsPerRun { get; set; } = 20;
    public int MaxRunSeconds { get; set; } = 120;

    /// <summary>Also warm the lane-scoped pro-builds default per champion (heavier compute path).</summary>
    public bool IncludeProBuilds { get; set; } = true;

    /// <summary>
    /// Command timeout for the default-lane synergy fill. Synergies are computed live from the
    /// participant tables; at the 30s connection default, 20-26% of champions failed every run on prod
    /// (2026-10-06), threw the partial work away, and left those pages to compute synergies on a
    /// request. The profile no longer waits for a miss, so this fill is what keeps the section filled.
    /// </summary>
    public int SynergyCommandTimeoutSeconds { get; set; } = 60;
}
