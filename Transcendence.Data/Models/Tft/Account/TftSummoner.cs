using Transcendence.Data.Models.Tft.Match;

namespace Transcendence.Data.Models.Tft.Account;

public class TftSummoner
{
    public Guid Id { get; set; }
    public string? RiotSummonerId { get; set; }
    public int ProfileIconId { get; set; }
    public long SummonerLevel { get; set; }
    public long RevisionDate { get; set; }
    public string? Puuid { get; set; }
    public string? GameName { get; set; }
    public string? TagLine { get; set; }
    public string? GameNameNormalized { get; set; }
    public string? TagLineNormalized { get; set; }
    public string? AccountId { get; set; }
    public required string PlatformRegion { get; set; }
    public required string Region { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<TftRank> Ranks { get; set; } = new List<TftRank>();
    public ICollection<TftHistoricalRank> HistoricalRanks { get; set; } = new List<TftHistoricalRank>();
    public ICollection<TftSummonerIngestionCursor> IngestionCursors { get; } = [];
    public ICollection<TftMatchParticipant> MatchParticipants { get; } = [];
}
