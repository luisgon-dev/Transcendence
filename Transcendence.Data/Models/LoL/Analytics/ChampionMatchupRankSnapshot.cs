namespace Transcendence.Data.Models.LoL.Analytics;

/// <summary>
/// Current-rank attribution frozen when a matchup generation starts. This preserves the existing
/// "current rank" contract while allowing champion batches to commit and resume independently.
/// </summary>
public class ChampionMatchupRankSnapshot
{
    public Guid SnapshotId { get; set; }
    public ChampionMatchupSnapshot Snapshot { get; set; } = null!;
    public Guid SummonerId { get; set; }
    public string RankTier { get; set; } = "";
}
