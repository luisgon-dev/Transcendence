using ProjectSyndraBackend.Data.Models.Match;

namespace ProjectSyndraBackend.Data.Models.Account;

public class Summoner
{
    public string? SummonerId { get; set; }
    public string? RiotSummonerId { get; set; }
    public string? SummonerName { get; set; }
    public int ProfileIconId { get; set; }
    public long SummonerLevel { get; set; }
    public long RevisionDate { get; set; }
    public string? Puuid { get; set; }
    public string? GameName { get; set; }
    public string? TagLine { get; set; }
    public string? AccountId { get; set; }
    public ICollection<MatchSummoner> MatchSummoners { get; set; } = new List<MatchSummoner>();
    public ICollection<Rank> Ranks { get; set; } = new List<Rank>();
}