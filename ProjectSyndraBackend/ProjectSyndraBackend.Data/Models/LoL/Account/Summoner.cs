using ProjectSyndraBackend.Data.Models.LoL.Match;

namespace ProjectSyndraBackend.Data.Models.LoL.Account;

public class Summoner
{
    public Guid Id { get; set; } // Primary key
    public string? ExternalSummonerId { get; set; } // Renamed from SummonerId
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
    public ICollection<MatchSummoner> MatchSummoners { get; set; } = new List<MatchSummoner>();
    public ICollection<Rank> Ranks { get; set; } = new List<Rank>();
}
