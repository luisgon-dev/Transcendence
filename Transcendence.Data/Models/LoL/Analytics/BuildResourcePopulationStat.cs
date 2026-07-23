namespace Transcendence.Data.Models.LoL.Analytics;

/// <summary>
/// Participant denominator for one Build Atlas generation, region, champion, and role.
/// It is promoted with the resource atoms so pick rates never mix snapshot generations.
/// </summary>
public class BuildResourcePopulationStat
{
    public Guid Id { get; set; }
    public Guid SnapshotId { get; set; }
    public BuildResourceSnapshot Snapshot { get; set; } = null!;
    public string PlatformRegion { get; set; } = "";
    public int ChampionId { get; set; }
    public string Role { get; set; } = "";
    public int Games { get; set; }
}
