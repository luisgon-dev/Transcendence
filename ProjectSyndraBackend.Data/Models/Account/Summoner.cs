using ProjectSyndraBackend.Data.Models.Match;

namespace ProjectSyndraBackend.Data.Models.Account;

public class Summoner
{
    public string? SummonerName;
    public string? Puuid;
    public string? GameName;
    public string? TagLine;
    public ICollection<MatchSummoner> MatchSummoners { get; set; } = new List<MatchSummoner>();
    
}