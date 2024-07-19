namespace ProjectSyndraBackend.Data.Models.Match;

public class Match
{
    public string? MatchId { get; set; }
    public long MatchDate { get; set; }
    public int Duration { get; set; }
    public string? Patch { get; set; }
    public string? EndOfGameResult { get; set; }

    public ICollection<MatchDetail> MatchDetails { get; set; } = new List<MatchDetail>();
    public ICollection<MatchSummoner> MatchSummoners { get; set; } = new List<MatchSummoner>();
}