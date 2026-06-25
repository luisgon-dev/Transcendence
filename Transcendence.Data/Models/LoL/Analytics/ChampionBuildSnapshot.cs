namespace Transcendence.Data.Models.LoL.Analytics;

/// <summary>
/// A durable, precomputed champion-builds response for one <c>(Patch, ChampionId, Role, RankScope)</c> at
/// the all-region scope. Builds are document-shaped (a nested, timing-ordered build path), so unlike the
/// tabular win-rate/matchup aggregates they are <b>not</b> decomposed into structured columns — instead the
/// refresh job runs the exact live <c>ComputeBuildsAsync</c> and persists its serialized response here, so a
/// cold read becomes a point lookup + deserialize instead of a heavy raw scan. Because the stored value is
/// the live compute's own output, equivalence is trivial (no behavior change).
/// <para>
/// Only the common rank scopes (<c>EMERALD_PLUS</c> — the page default — and <c>ALL</c>) are precomputed,
/// at the all-region scope; a specific tier or region read falls back to the live compute.
/// </para>
/// </summary>
public class ChampionBuildSnapshot
{
    public Guid Id { get; set; }

    public string Patch { get; set; } = "";
    public int ChampionId { get; set; }
    public string Role { get; set; } = "";

    /// <summary>Rank-scope token: "EMERALD_PLUS" or "ALL".</summary>
    public string RankScope { get; set; } = "";

    /// <summary>The serialized <c>ChampionBuildsResponse</c> (JSON), returned to callers verbatim.</summary>
    public string Payload { get; set; } = "";

    public DateTime ComputedAtUtc { get; set; }
}
