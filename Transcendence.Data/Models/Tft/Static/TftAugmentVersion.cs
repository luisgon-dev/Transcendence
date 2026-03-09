namespace Transcendence.Data.Models.Tft.Static;

public class TftAugmentVersion
{
    public Guid Id { get; set; }
    public string ApiName { get; set; } = string.Empty;
    public int SetNumber { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Icon { get; set; }
    public string[] AssociatedTraits { get; set; } = [];
    public string[] IncompatibleTraits { get; set; } = [];
    public string[] Tags { get; set; } = [];
    public bool Unique { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
