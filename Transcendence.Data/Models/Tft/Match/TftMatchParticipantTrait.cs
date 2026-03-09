namespace Transcendence.Data.Models.Tft.Match;

public class TftMatchParticipantTrait
{
    public Guid Id { get; set; }
    public Guid ParticipantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int NumUnits { get; set; }
    public int? Style { get; set; }
    public int TierCurrent { get; set; }
    public int? TierTotal { get; set; }

    public required TftMatchParticipant Participant { get; set; }
}
