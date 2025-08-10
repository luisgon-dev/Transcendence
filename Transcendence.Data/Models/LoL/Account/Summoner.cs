using Transcendence.Data.Models.LoL.Match;

namespace Transcendence.Data.Models.LoL.Account;

public class Summoner
{
    public Guid Id { get; set; }
    public string? RiotSummonerId { get; set; }
    public string? SummonerName { get; set; }
    public int ProfileIconId { get; set; }
    public long SummonerLevel { get; set; }
    public long RevisionDate { get; set; }
    public string? Puuid { get; set; }
    public string? GameName { get; set; }
    public string? TagLine { get; set; }
    public string? AccountId { get; set; }
    public required string? PlatformRegion { get; set; }
    public required string? Region { get; set; }
    public List<Match.Match> Matches { get; } = [];
    public List<MatchSummoner> MatchSummoners { get; } = [];
    public ICollection<Rank> Ranks { get; set; } = new List<Rank>();
}