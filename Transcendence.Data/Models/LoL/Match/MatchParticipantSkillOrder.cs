namespace Transcendence.Data.Models.LoL.Match;

/// <summary>
/// Per-participant skill-leveling summary derived from the match timeline
/// <c>SKILL_LEVEL_UP</c> events. The first-three and max-order strings are precomputed at
/// ingestion so build aggregation is a cheap string group-by.
/// </summary>
public class MatchParticipantSkillOrder
{
    public Guid MatchId { get; set; }
    public int ParticipantId { get; set; }

    /// <summary>Full ordered leveling sequence as comma-separated ability letters, e.g. "Q,W,E,Q,R,...".</summary>
    public string Sequence { get; set; } = string.Empty;

    /// <summary>The first three skill points spent as concatenated letters, e.g. "QWE".</summary>
    public string FirstThree { get; set; } = string.Empty;

    /// <summary>Basic-ability max priority (R excluded) ordered by points spent, e.g. "Q&gt;E&gt;W".</summary>
    public string MaxOrder { get; set; } = string.Empty;

    public required Match Match { get; set; }
}
