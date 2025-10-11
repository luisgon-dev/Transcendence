using Transcendence.Data.Models.LoL.Static;
namespace Transcendence.Data.Models.LoL.Match;

public class MatchParticipantItem
{
    public Guid MatchParticipantId { get; set; }
    public int ItemId { get; set; }
    public string PatchVersion { get; set; }

    public MatchParticipant MatchParticipant { get; set; }
    public ItemVersion ItemVersion { get; set; }
}
