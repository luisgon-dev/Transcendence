namespace Transcendence.Data.Models.Tft.Static;

public class TftUnitVersion
{
    public Guid Id { get; set; }
    public string ApiName { get; set; } = string.Empty;
    public int SetNumber { get; set; }
    public string Name { get; set; } = string.Empty;
    public string CharacterName { get; set; } = string.Empty;
    public int Cost { get; set; }
    public string? Icon { get; set; }
    public string? SquareIcon { get; set; }
    public string? TileIcon { get; set; }
    public string? Role { get; set; }
    public string[] Traits { get; set; } = [];
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
