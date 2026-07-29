namespace Transcendence.Data.Models.LoL.Match;

/// <summary>
/// Best rank observation already present in Transcendence when a timeline is ingested. This is
/// provenance, not a claim that Riot supplied historical match-time rank.
/// </summary>
public class MatchParticipantRankContext
{
    public Guid MatchId { get; set; }
    public int ParticipantId { get; set; }
    public string? Tier { get; set; }
    public string? Division { get; set; }
    public int? LeaguePoints { get; set; }
    public DateTime? ObservedAtUtc { get; set; }
    /// <summary>
    /// Signed seconds between match start and the rank observation: negative when the rank was
    /// observed BEFORE the match, positive when observed after. The sign is load-bearing — a
    /// post-match observation is a post-outcome variable, so any cohort filter keyed on
    /// <see cref="Tier"/> must be able to exclude it.
    /// </summary>
    public long? ObservationOffsetSeconds { get; set; }
    public string? Source { get; set; }

    public required Match Match { get; set; }
}
