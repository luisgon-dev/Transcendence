namespace Transcendence.Data.Models.LoL.Analytics;

/// <summary>
/// Precomputed distinct banned-match count for one <c>(Patch, PlatformRegion, RankScope, ChampionId)</c> —
/// the numerator for champion ban rate, paired with <see cref="ScopeMatchCountStat"/> as the denominator
/// (<c>BanRate = BannedMatches / TotalMatches</c>).
/// <para>
/// Keyed by the rank-<i>scope</i> token, and read by <b>point lookup</b> (an explicit synthetic
/// <see cref="PlatformRegion"/> = <c>"ALL"</c> row for region=ALL, a concrete platform row otherwise) —
/// never summed over tiers or regions — for the same distinct-match non-additivity reasons documented on
/// <see cref="ScopeMatchCountStat"/>. Role-independent (one row serves both the win-rate and tier-list pages).
/// </para>
/// </summary>
public class ChampionBanScopeStat
{
    public Guid Id { get; set; }

    public string Patch { get; set; } = "";

    public string QueueFamily { get; set; } = "RANKED_SOLO_DUO";

    /// <summary>Concrete platform (e.g. "NA1") or the synthetic global <c>"ALL"</c> row.</summary>
    public string PlatformRegion { get; set; } = "";

    /// <summary>Rank-scope token: "ALL", "EMERALD_PLUS", or an exact tier (e.g. "DIAMOND").</summary>
    public string RankScope { get; set; } = "";

    public int ChampionId { get; set; }

    /// <summary>Distinct matches in scope where this champion was banned.</summary>
    public int BannedMatches { get; set; }

    public DateTime ComputedAtUtc { get; set; }
}
