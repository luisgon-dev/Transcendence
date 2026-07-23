namespace Transcendence.Data.Models.LoL.Analytics;

/// <summary>
/// Additive item/rune usage for one patch, region, champion, and role. Build Atlas rolls these
/// worker-computed rows up by region instead of scanning raw participant resources on request.
/// </summary>
public class BuildResourceStat
{
    public Guid Id { get; set; }
    public string Patch { get; set; } = "";
    public string PlatformRegion { get; set; } = "";
    public string ResourceType { get; set; } = "";
    public int ResourceId { get; set; }
    public int ChampionId { get; set; }
    public string Role { get; set; } = "";
    public int Games { get; set; }
    public int Wins { get; set; }
    public DateTime ComputedAtUtc { get; set; }
}
