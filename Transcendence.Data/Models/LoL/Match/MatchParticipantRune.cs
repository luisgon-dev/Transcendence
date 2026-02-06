using Transcendence.Data.Models.LoL.Static;

namespace Transcendence.Data.Models.LoL.Match;

public class MatchParticipantRune
{
    public Guid MatchParticipantId { get; set; }
    public int RuneId { get; set; }
    public string PatchVersion { get; set; } = string.Empty;

    public MatchParticipant MatchParticipant { get; set; } = null!;
    public RuneVersion RuneVersion { get; set; } = null!;
}
