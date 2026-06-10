namespace Transcendence.Service.Core.Services.Jobs.Configuration;

/// <summary>
/// Controls when a newly-detected Data Dragon patch is promoted to the active analytics patch.
/// Data Dragon publishes the new version when patch notes drop, but the game-build <c>gameVersion</c>
/// reported in match data lags by hours/days and rolls out region-by-region. Promoting on the Data
/// Dragon signal makes ingestion target a patch no games report yet, so it discards every real match.
/// Instead we promote only once the new patch's gameVersion has actually rolled out across regions.
/// </summary>
public class PatchPromotionOptions
{
    /// <summary>A region counts as "rolled out" once it has at least this many Success matches on the new patch.</summary>
    public int MinMatchesPerRegionToCount { get; set; } = 200;

    /// <summary>Promote once at least this many regions have rolled out (broad availability, not just the early regions).</summary>
    public int MinRegionsRolledOut { get; set; } = 5;

    /// <summary>Stuck-guard: promote regardless once the new patch has been the Data Dragon latest this long.</summary>
    public int MaxWaitHoursBeforeForcePromote { get; set; } = 72;
}
