namespace Transcendence.Data.Models.LoL.Analytics;

/// <summary>
/// Durable source-materialization ledger. Timeline backfill can advance
/// <see cref="LatestTimelineDerivedAtUtc"/> and cause only that match's narrow facts to be rebuilt.
/// </summary>
public class ChampionMatchupSourceMatch
{
    public Guid MatchId { get; set; }
    public string Patch { get; set; } = "";
    public int ParticipantCount { get; set; }
    public int TimelineSnapshotCount { get; set; }
    public DateTime? LatestTimelineDerivedAtUtc { get; set; }
    public DateTime ProcessedAtUtc { get; set; }
}
