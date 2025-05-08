namespace ProjectSyndraBackend.Data.Models.LoL.Match;

public class Match
{
    public Guid Id { get; set; } // Primary key
    public string? ExternalMatchId { get; set; } // Riot's match ID
    public long MatchDate { get; set; }
    public int Duration { get; set; }
    public string? Patch { get; set; }
    public string? QueueType { get; set; }
    public string? EndOfGameResult { get; set; }

    public ICollection<MatchDetail> MatchDetails { get; set; } = new List<MatchDetail>();
    public ICollection<MatchSummoner> MatchSummoners { get; set; } = new List<MatchSummoner>();
}
