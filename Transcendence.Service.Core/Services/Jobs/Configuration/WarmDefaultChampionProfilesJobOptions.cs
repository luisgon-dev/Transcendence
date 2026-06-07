namespace Transcendence.Service.Core.Services.Jobs.Configuration;

/// <summary>
/// Controls the hourly job that keeps every champion's DEFAULT profile-page analytics warm
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
    public int MaxConcurrency { get; set; } = 3;

    /// <summary>Also warm the lane-scoped pro-builds default per champion (heavier compute path).</summary>
    public bool IncludeProBuilds { get; set; } = true;
}
