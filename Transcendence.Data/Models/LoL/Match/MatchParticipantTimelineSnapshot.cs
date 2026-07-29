namespace Transcendence.Data.Models.LoL.Match;

public class MatchParticipantTimelineSnapshot
{
    public Guid MatchId { get; set; }
    public int ParticipantId { get; set; }
    public int MinuteMark { get; set; }

    public int Gold { get; set; }
    public int CurrentGold { get; set; }
    public int Xp { get; set; }
    public int Cs { get; set; }
    public int LaneCs { get; set; }
    public int JungleCs { get; set; }
    public int Level { get; set; }
    public int FrameTimestampMs { get; set; }
    public DateTime DerivedAtUtc { get; set; }
    public string? QualityFlags { get; set; }

    public required Match Match { get; set; }
}
