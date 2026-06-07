namespace Transcendence.Service.Core.Services.Jobs.Configuration;

public class RefreshChampionAnalyticsJobOptions
{
    public int PopularChampionsTakeCount { get; set; } = 100;
    public int ChampionsPerRoleToPreWarm { get; set; } = 12;

    /// <summary>
    /// Rank tier to pre-warm champion win-rate/build/matchup aggregates at. Must match the
    /// frontend champion-page default (Emerald+) so the pre-warmed cache keys are the ones the
    /// page actually reads — otherwise the page always cold-computes after each invalidation.
    /// </summary>
    public string PreWarmRankTier { get; set; } = "EMERALD_PLUS";

    /// <summary>Also pre-warm the (role-scoped) pro-builds aggregate for popular champions.</summary>
    public bool PreWarmProBuilds { get; set; } = true;

    /// <summary>
    /// Champions per role to pre-warm pro-builds for. Pro-builds compute is heavier than the
    /// standard aggregates, so this is bounded separately (and lower) than ChampionsPerRoleToPreWarm.
    /// </summary>
    public int ProBuildChampionsPerRoleToPreWarm { get; set; } = 8;
    public int AdaptiveNewMatchesThreshold { get; set; } = 500;
    public int AdaptiveLookbackMinutes { get; set; } = 30;
    public int MinimumRefreshIntervalMinutes { get; set; } = 120;
    public int ForceRefreshAfterHours { get; set; } = 24;
    public bool EnqueueIngestionWhenNoPopularChampions { get; set; } = true;
    public int NewPatchRampHours { get; set; } = 48;
    public int RampChampionsPerRoleToPreWarm { get; set; } = 20;
    public int RampAdaptiveNewMatchesThreshold { get; set; } = 100;
    public int RampAdaptiveLookbackMinutes { get; set; } = 10;
    public int RampMinimumRefreshIntervalMinutes { get; set; } = 10;
    public int RampForceRefreshAfterHours { get; set; } = 2;
}
