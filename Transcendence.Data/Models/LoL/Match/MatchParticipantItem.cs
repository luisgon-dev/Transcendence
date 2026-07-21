namespace Transcendence.Data.Models.LoL.Match;

public class MatchParticipantItem
{
    public Guid MatchParticipantId { get; set; }
    public int SlotIndex { get; set; }
    public int ItemId { get; set; }
    public string PatchVersion { get; set; } = string.Empty;

    public MatchParticipant MatchParticipant { get; set; } = null!;
}
