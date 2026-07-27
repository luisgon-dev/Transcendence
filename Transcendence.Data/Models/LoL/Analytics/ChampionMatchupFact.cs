namespace Transcendence.Data.Models.LoL.Analytics;

/// <summary>
/// Narrow, durable lane-pair fact derived from immutable match participants and minute-15 timeline
/// snapshots. It intentionally has no foreign key to Matches so archive/prune jobs cannot remove the
/// analytics source. Current rank is joined separately when a generation starts.
/// </summary>
public class ChampionMatchupFact
{
    public Guid Id { get; set; }
    public Guid MatchId { get; set; }
    public int ChampionParticipantId { get; set; }
    public Guid SummonerId { get; set; }
    public string Patch { get; set; } = "";
    public int ChampionId { get; set; }
    public string Role { get; set; } = "";
    public int OpponentChampionId { get; set; }
    public bool Win { get; set; }
    public bool HasTimeline { get; set; }
    public int GoldDiffAt15 { get; set; }
    public int XpDiffAt15 { get; set; }
    public DateTime? TimelineDerivedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
