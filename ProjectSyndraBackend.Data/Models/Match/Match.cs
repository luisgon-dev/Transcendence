namespace ProjectSyndraBackend.Data.Models.Match;

public class Match
{
    public string? MatchId { get; set; }
    public DateTime MatchDate { get; set; }
    public int Duration { get; set; }
    public string? Patch { get; set; }

    public ICollection<MatchDetail> MatchDetails { get; set; } = new List<MatchDetail>();
    public ICollection<MatchSummoner> MatchSummoners { get; set; } = new List<MatchSummoner>();
}