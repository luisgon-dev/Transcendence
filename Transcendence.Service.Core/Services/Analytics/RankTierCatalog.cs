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

    /// <summary>All individual competitive solo-queue tiers, low → high.</summary>
    public static readonly IReadOnlyList<string> AllTiers =
        ["IRON", "BRONZE", "SILVER", "GOLD", "EMERALD", "DIAMOND", "MASTER", "GRANDMASTER", "CHALLENGER"];

    /// <summary>Sentinel <c>RankTier</c> for a participant with no current solo-queue rank row.</summary>
    public const string Unranked = "UNRANKED";

    /// <summary>Unfiltered rank scope (includes unranked participants/matches).</summary>
    public const string AllScope = "ALL";

    /// <summary>High-elo composite scope token.</summary>
    public const string EmeraldPlusScope = "EMERALD_PLUS";

    /// <summary>
    /// Rank-scope tokens the analytics surface can request — and that the refresh job precomputes the
    /// distinct-match (ban-rate) denominators for: the unfiltered <see cref="AllScope"/>, the high-elo
    /// <see cref="EmeraldPlusScope"/> composite, and every exact tier. Mirrors <c>ParseRankTierScope</c>
    /// in ChampionAnalyticsComputeService (which normalizes "EMERALD+" → "EMERALD_PLUS").
    /// </summary>
    public static readonly IReadOnlyList<string> RankScopeTokens =
        [AllScope, EmeraldPlusScope, .. AllTiers];

    /// <summary>
    /// The individual <c>RankTier</c> values a scope token covers, for rolling up
    /// <c>ChampionRoleTierStat</c> rows. <see cref="AllScope"/> returns <c>null</c> (no tier filter —
    /// includes <see cref="Unranked"/>); <see cref="EmeraldPlusScope"/> returns the high tiers; an exact
    /// tier returns just itself.
    /// </summary>
    public static IReadOnlyList<string>? ResolveScopeTiers(string scopeToken) =>
        scopeToken switch
        {
            AllScope => null,
            EmeraldPlusScope => EmeraldPlusTiers,
            _ => [scopeToken],
        };
}
