namespace Transcendence.Data.Models.LoL.Analytics;

/// <summary>
/// Additive item/rune usage for one completed Build Atlas snapshot, region, champion, and role.
/// </summary>
public class BuildResourceStat
{
    public Guid Id { get; set; }
    public Guid SnapshotId { get; set; }
    public BuildResourceSnapshot Snapshot { get; set; } = null!;
    public string PlatformRegion { get; set; } = "";
    public string ResourceType { get; set; } = "";
    public int ResourceId { get; set; }
    public int ChampionId { get; set; }
    public string Role { get; set; } = "";
    public int Games { get; set; }
    public int Wins { get; set; }
}
