namespace Transcendence.Data.Models.LoL.Static;

public class Patch
{
    public string Version { get; set; } = string.Empty;
    public DateTime ReleaseDate { get; set; }
    public DateTime? DetectedAt { get; set; }  // When we first detected this patch
    public bool IsActive { get; set; } = true;  // Current active patch
}