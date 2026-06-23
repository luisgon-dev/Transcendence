namespace Transcendence.Service.Core.Services.Analytics;

/// <summary>
/// Canonical rank-tier groupings shared across LoL + TFT analytics. The Emerald+ set is the default
/// "high-elo" analytics scope; it was previously copy-pasted as an inline <c>|| </c>-chain in several
/// places (ChampionAnalyticsComputeService, TftAnalyticsComputeService). Exposed as a list so EF Core
/// translates <c>EmeraldPlusTiers.Contains(tier)</c> to a SQL <c>IN (...)</c> — equivalent to the chain.
/// </summary>
public static class RankTierCatalog
{
    /// <summary>Solo-queue tiers that make up the "Emerald and above" scope (used for ordering too: highest elo last).</summary>
    public static readonly IReadOnlyList<string> EmeraldPlusTiers =
        ["EMERALD", "DIAMOND", "MASTER", "GRANDMASTER", "CHALLENGER"];
}
