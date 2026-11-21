namespace Transcendence.Service.Core.Services.Analytics;

/// <summary>
/// Canonical rank-tier groupings shared across LoL analytics reads and materialization.
/// </summary>
public static class RankTierCatalog
{
    private static readonly IReadOnlyList<string> MasterPlusTiers = ["MASTER", "GRANDMASTER", "CHALLENGER"];
    private static readonly IReadOnlyList<string> EmeraldPlusTiers = ["EMERALD", "DIAMOND", .. MasterPlusTiers];

    /// <summary>All individual competitive solo-queue tiers, low → high (the modern ladder includes PLATINUM).</summary>
    public static readonly IReadOnlyList<string> AllTiers =
        ["IRON", "BRONZE", "SILVER", "GOLD", "PLATINUM", "EMERALD", "DIAMOND", "MASTER", "GRANDMASTER", "CHALLENGER"];

    /// <summary>Sentinel <c>RankTier</c> for a participant with no current solo-queue rank row.</summary>
    public const string Unranked = "UNRANKED";

    /// <summary>Unfiltered rank scope (includes unranked participants/matches).</summary>
    public const string AllScope = "ALL";

    public const string EmeraldPlusScope = "EMERALD_PLUS";
    public const string MasterPlusScope = "MASTER_PLUS";

    /// <summary>
    /// Rank scopes with persisted all-region tier grades and trends.
    /// </summary>
    public static readonly IReadOnlyList<string> PersistedGradeScopes = [AllScope, EmeraldPlusScope, MasterPlusScope];

    /// <summary>Rank scopes with precomputed distinct-match and ban counts.</summary>
    public static readonly IReadOnlyList<string> RankScopeTokens =
        [.. PersistedGradeScopes, .. AllTiers];

    public static string NormalizeScopeToken(string? rankTier) => string.IsNullOrWhiteSpace(rankTier)
        ? AllScope
        : rankTier.Trim().ToUpperInvariant().Replace("+", "_PLUS");

    /// <summary>
    /// The individual <c>RankTier</c> values a scope token covers, for rolling up
    /// <c>ChampionRoleTierStat</c> rows. <see cref="AllScope"/> returns <c>null</c> (no tier filter —
    /// includes <see cref="Unranked"/>); a composite returns its tiers and an exact tier returns itself.
    /// </summary>
    public static IReadOnlyList<string>? ResolveScopeTiers(string scopeToken) =>
        scopeToken switch
        {
            AllScope => null,
            EmeraldPlusScope => EmeraldPlusTiers,
            MasterPlusScope => MasterPlusTiers,
            _ => [scopeToken],
        };
}
