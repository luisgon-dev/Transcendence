namespace Transcendence.Data.Models.LoL.Analytics;

/// <summary>
/// Precomputed lane-matchup aggregate for one <c>(Patch, RankTier, ChampionId, Role, OpponentChampionId)</c>
/// — the structured replacement for the live lane-pair self-join + timeline-diff scan in
/// <c>ComputeMatchupsAsync</c>, at the global (all-region) scope.
/// <para>
/// <see cref="RankTier"/> is the <i>champion</i> side's current solo tier ("UNRANKED" when absent) — the
/// matchup read only scopes the champion side; the opponent is never filtered. Every measure is additive
/// over tier/role (Games is a lane-PAIR count, not a distinct-match count, so it sums freely), so the read
/// rolls the requested rank scope up with <c>SUM</c> (and <c>MAX</c> for <see cref="LatestTimelineAtUtc"/>).
/// Rank scope "all" includes the UNRANKED rows; EMERALD_PLUS / exact-tier reads sum only their tiers
/// (mirroring the live EXISTS-on-current-rank filter, which excludes unranked players).
/// </para>
/// <para>
/// Region is intentionally NOT a key column: matchups are stored only at the all-region scope (the default
/// and dominant case). The much rarer region-filtered matchup read falls back to the live compute — keeping
/// this table ~10× smaller (the full region×tier grain would be 1–2M rows/patch, too heavy to rebuild hourly).
/// </para>
/// </summary>
public class ChampionMatchupStat
{
    public Guid Id { get; set; }
    public Guid? SnapshotId { get; set; }
    public ChampionMatchupSnapshot? Snapshot { get; set; }

    public string Patch { get; set; } = "";
    public string RankTier { get; set; } = "";
    public int ChampionId { get; set; }
    public string Role { get; set; } = "";
    public int OpponentChampionId { get; set; }

    /// <summary>Lane-pair count (one per champion-side player vs their lane opponent). Additive.</summary>
    public int Games { get; set; }
    public int Wins { get; set; }

    /// <summary>Lane pairs where BOTH players have a minute-15 timeline snapshot (the avg-diff denominator).</summary>
    public int TimelineGames { get; set; }

    /// <summary>SUM over both-present pairs of (champion gold − opponent gold) at 15. Read avg = Sum / TimelineGames.</summary>
    public long SumGoldDiffAt15 { get; set; }

    /// <summary>SUM over both-present pairs of (champion xp − opponent xp) at 15.</summary>
    public long SumXpDiffAt15 { get; set; }

    /// <summary>MAX over champion-side-present pairs of the snapshot's DerivedAtUtc (timeline freshness).</summary>
    public DateTime? LatestTimelineAtUtc { get; set; }

    public DateTime ComputedAtUtc { get; set; }
}
