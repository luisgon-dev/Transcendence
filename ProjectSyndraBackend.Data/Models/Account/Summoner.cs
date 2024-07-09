using ProjectSyndraBackend.Data.Models.Match;

namespace ProjectSyndraBackend.Data.Models.Account;

public class Summoner
{
    public string? SummonerId { get; set; }
    public string? SummonerName { get; set; }
    public string? Puuid { get; set; }
    public string? GameName { get; set; }
    public string? TagLine { get; set; }
    public ICollection<MatchSummoner> MatchSummoners { get; set; } = new List<MatchSummoner>();
    
}