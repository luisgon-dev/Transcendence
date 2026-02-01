using Transcendence.Data.Models.LoL.Static;

namespace Transcendence.Data.Models.LoL.Match;

public class MatchParticipantRune
{
    public Guid MatchParticipantId { get; set; }
    public int RuneId { get; set; }
    public string PatchVersion { get; set; }

    public MatchParticipant MatchParticipant { get; set; }
    public RuneVersion RuneVersion { get; set; }
}