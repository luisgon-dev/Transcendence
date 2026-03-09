namespace Transcendence.Data.Models.Tft.Static;

public class TftTraitVersion
{
    public Guid Id { get; set; }
    public string ApiName { get; set; } = string.Empty;
    public int SetNumber { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Icon { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
