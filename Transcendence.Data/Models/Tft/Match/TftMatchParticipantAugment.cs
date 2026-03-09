namespace Transcendence.Data.Models.Tft.Match;

public class TftMatchParticipantAugment
{
    public Guid Id { get; set; }
    public Guid ParticipantId { get; set; }
    public int SlotIndex { get; set; }
    public string ApiName { get; set; } = string.Empty;

    public required TftMatchParticipant Participant { get; set; }
}
