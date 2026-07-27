namespace Transcendence.Data.Models.LoL.Analytics;

public enum ChampionMatchupSnapshotStatus
{
    Building = 0,
    Ready = 1,
    Failed = 2,
    Retired = 3
}

/// <summary>
/// Immutable matchup aggregate generation. Batch output is written while the generation is
/// <see cref="ChampionMatchupSnapshotStatus.Building"/> and readers only resolve the active Ready row.
/// A failed execution can resume the same generation without exposing partial data.
/// </summary>
public class ChampionMatchupSnapshot
{
    public Guid Id { get; set; }
    public string Patch { get; set; } = "";
    public ChampionMatchupSnapshotStatus Status { get; set; } = ChampionMatchupSnapshotStatus.Building;
    public bool IsActive { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? LastAttemptAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime SourceCutoffUtc { get; set; }
    public int AttemptCount { get; set; }
    public int SourceFactCount { get; set; }
    public int TotalChampionCount { get; set; }
    public int ProcessedChampionCount { get; set; }
    public string? FailureReason { get; set; }

    public ICollection<ChampionMatchupStat> Stats { get; set; } = new List<ChampionMatchupStat>();
    public ICollection<ChampionMatchupRankSnapshot> RankSnapshots { get; set; } =
        new List<ChampionMatchupRankSnapshot>();
}
