namespace Transcendence.Data.Models.Tft.Match;

public class TftMatchParticipantUnit
{
    public Guid Id { get; set; }
    public Guid ParticipantId { get; set; }
    public string CharacterId { get; set; } = string.Empty;
    public string? Name { get; set; }
    public int Rarity { get; set; }
    public int Tier { get; set; }
    public string? Chosen { get; set; }
    public int[] Items { get; set; } = [];
    public string[] ItemNames { get; set; } = [];

    public required TftMatchParticipant Participant { get; set; }
}
