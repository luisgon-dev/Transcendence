namespace Transcendence.Data.Models.Tft.Static;

public class TftPatch
{
    public string Version { get; set; } = string.Empty;
    public DateTime DetectedAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; }
    public int? ActiveSetNumber { get; set; }
    public string? ActiveSetCoreName { get; set; }
}
