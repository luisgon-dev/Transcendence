namespace Transcendence.Data.Models.LoL.Match;

public class MatchBan
{
    public Guid MatchId { get; set; }
    public required Match Match { get; set; }
    public int TeamId { get; set; }
    public int PickTurn { get; set; }
    public int ChampionId { get; set; }
}
