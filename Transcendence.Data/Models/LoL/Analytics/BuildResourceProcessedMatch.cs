namespace Transcendence.Data.Models.LoL.Analytics;

/// <summary>
/// Durable inclusion ledger for incremental Build Atlas refreshes. Rows belonging to Building or
/// Failed generations do not suppress retries; rows from Ready/Retired generations are committed.
/// </summary>
public class BuildResourceProcessedMatch
{
    public Guid SnapshotId { get; set; }
    public BuildResourceSnapshot Snapshot { get; set; } = null!;
    public Guid MatchId { get; set; }
}
