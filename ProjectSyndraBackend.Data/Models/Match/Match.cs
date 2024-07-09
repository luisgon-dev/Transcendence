namespace ProjectSyndraBackend.Data.Models.Match;

public class Match
{
    public string? MatchId { get; set; }
    public DateTime MatchDate { get; set; }
    public int Duration { get; set; }
    
    public ICollection<MatchDetail> MatchDetails { get; set; }
    
    
}