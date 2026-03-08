namespace Transcendence.Data.Models.Tft.Static;

public class TftSet
{
    public int Number { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Mutator { get; set; }
    public string? CoreName { get; set; }
    public bool IsActive { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
