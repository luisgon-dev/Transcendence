namespace Transcendence.Data.Models.LoL.Analytics;

/// <summary>
/// Precomputed distinct-match count for one <c>(Patch, PlatformRegion, RankScope)</c> — the denominator
/// for champion ban rate (paired with <see cref="ChampionBanScopeStat"/> as the numerator) and any other
/// "share of matches in scope" metric.
/// <para>
/// Distinct-match counts are <b>not</b> additive over rank tiers — a single match spans up to ten players'
/// tiers, so it belongs to several individual-tier buckets at once. This table is therefore keyed by the
/// rank-<i>scope</i> token (<c>ALL</c>, <c>EMERALD_PLUS</c>, or an exact tier such as <c>DIAMOND</c>) rather
/// than an individual tier.
/// </para>
/// <para>
/// They are also <b>not</b> safely additive over platform region: a match's region is derived from a
/// nullable <c>Summoner.PlatformRegion</c>, so a per-region SUM is not provably equal to the live global
/// <c>COUNT(DISTINCT MatchId)</c>. The refresh therefore materializes an explicit synthetic
/// <see cref="PlatformRegion"/> = <c>"ALL"</c> row (computed globally, with no region filter) alongside the
/// per-platform rows; the read does a single <b>point lookup</b> — the <c>"ALL"</c> row for region=ALL, or
/// a concrete platform row for a specific region — and never sums regions.
/// </para>
/// <para>
/// Ban rate is <b>role-independent</b> (bans happen at champ select, before role assignment): a single
/// scope row serves both the champion win-rate page and the tier list, which now agree on a champion's
/// ban rate. (Previously the tier-list path role-scoped its denominator — an artifact inconsistent with the
/// already role-independent win-rate path.)
/// </para>
/// </summary>
public class ScopeMatchCountStat
{
    public Guid Id { get; set; }

    public string Patch { get; set; } = "";

    /// <summary>Concrete platform (e.g. "NA1") or the synthetic global <c>"ALL"</c> row.</summary>
    public string PlatformRegion { get; set; } = "";

    /// <summary>Rank-scope token: "ALL", "EMERALD_PLUS", or an exact tier (e.g. "DIAMOND").</summary>
    public string RankScope { get; set; } = "";

    /// <summary>Distinct ranked-solo matches in scope.</summary>
    public int TotalMatches { get; set; }

    public DateTime ComputedAtUtc { get; set; }
}
