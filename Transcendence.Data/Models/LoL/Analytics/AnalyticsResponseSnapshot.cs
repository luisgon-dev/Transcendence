namespace Transcendence.Data.Models.LoL.Analytics;

/// <summary>
/// A durable, precomputed analytics RESPONSE for one <c>(Feature, ScopeKey, Patch)</c> — the same
/// response-cache approach as <see cref="ChampionBuildSnapshot"/>, generalized for the response-shaped
/// surfaces that don't decompose into structured columns. The refresh job runs the exact live compute and
/// persists its serialized response here, so a cold read becomes a point lookup + deserialize instead of a
/// raw scan; equivalence is trivial (the stored value is the live compute's own output).
/// <para>
/// Used for the roster-filtered pro surfaces (where the scope is a read-time intersection, so a structured
/// decomposition isn't equivalence-preserving):
/// <list type="bullet">
/// <item><c>Feature="proplayrate"</c>, <c>ScopeKey</c> = the roster scope token ("all"/"pro"/"highelo").</item>
/// <item><c>Feature="probuilds"</c>, <c>ScopeKey</c> = <c>"{championId}:{role}:{scope}"</c>.</item>
/// </list>
/// All at the all-region scope; a specific region (or a role/scope not precomputed) falls back to live compute.
/// </para>
/// </summary>
public class AnalyticsResponseSnapshot
{
    public Guid Id { get; set; }

    /// <summary>Surface discriminator: "probuilds" | "proplayrate".</summary>
    public string Feature { get; set; } = "";

    /// <summary>Feature-specific scope key (see the class summary).</summary>
    public string ScopeKey { get; set; } = "";

    public string Patch { get; set; } = "";

    /// <summary>The serialized response DTO (JSON), returned to callers verbatim.</summary>
    public string Payload { get; set; } = "";

    public DateTime ComputedAtUtc { get; set; }
}
