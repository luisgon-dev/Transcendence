using Transcendence.Data.Models.LoL.Static;

namespace Transcendence.Data.Models.LoL.Match;

public enum RuneSelectionTree
{
    Primary = 0,
    Secondary = 1,
    StatShards = 2
}

public class MatchParticipantRune
{
    public Guid MatchParticipantId { get; set; }
    public int RuneId { get; set; }
    public string PatchVersion { get; set; } = string.Empty;
    public RuneSelectionTree SelectionTree { get; set; } = RuneSelectionTree.Primary;
    public int SelectionIndex { get; set; }
    public int StyleId { get; set; }

    public MatchParticipant MatchParticipant { get; set; } = null!;
    public RuneVersion RuneVersion { get; set; } = null!;
}
