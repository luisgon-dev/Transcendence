namespace Transcendence.Data.Models.LoL.Analytics;

public enum BuildResourceSnapshotStatus
{
    Building = 0,
    Ready = 1,
    Failed = 2,
    Retired = 3
}

/// <summary>
/// Generation manifest for Build Atlas. Readers use only the active <see cref="Ready"/> generation,
/// so batch processing and failed refreshes never expose partial aggregates.
/// </summary>
public class BuildResourceSnapshot
{
    public Guid Id { get; set; }
    public string Patch { get; set; } = "";
    public BuildResourceSnapshotStatus Status { get; set; } = BuildResourceSnapshotStatus.Building;
    public bool IsActive { get; set; }
    public bool IsFullRebuild { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public int ProcessedMatchCount { get; set; }
    public string? FailureReason { get; set; }

    public ICollection<BuildResourceStat> ResourceStats { get; set; } = new List<BuildResourceStat>();
    public ICollection<BuildResourcePopulationStat> PopulationStats { get; set; } =
        new List<BuildResourcePopulationStat>();
    public ICollection<BuildResourceProcessedMatch> ProcessedMatches { get; set; } =
        new List<BuildResourceProcessedMatch>();
}
