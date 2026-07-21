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
    public string Puuid { get; set; } = null!;
    public string? GameName { get; set; }
    public string? TagLine { get; set; }
    public string? GameNameNormalized { get; set; }
    public string? TagLineNormalized { get; set; }
    public string? AccountId { get; set; }
    public required string? PlatformRegion { get; set; }
    public required string? Region { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;  // When summoner profile was last fetched (also the analytics coverage timestamp)
    // Most recent game-creation time across this summoner's ingested matches — a true activity
    // signal for candidate selection (distinct from UpdatedAt, which is fetch/coverage recency).
    // Null until the summoner next appears in an ingested match. Maintained in MatchService.
    public DateTime? LastActiveAtUtc { get; set; }
    public ICollection<MatchParticipant> MatchParticipants { get; } = [];
    public ICollection<SummonerIngestionCursor> IngestionCursors { get; } = [];
    public ICollection<Rank> Ranks { get; set; } = new List<Rank>();
    public ICollection<HistoricalRank> HistoricalRanks { get; set; } = new List<HistoricalRank>();
    public ICollection<ChampionMastery> ChampionMasteries { get; set; } = new List<ChampionMastery>();
    public ICollection<SummonerFullHistoryBackfill> FullHistoryBackfills { get; set; } = new List<SummonerFullHistoryBackfill>();
    public ICollection<SummonerMatchFact> MatchFacts { get; set; } = new List<SummonerMatchFact>();
    public ICollection<SummonerSeasonOverviewStat> SeasonOverviewStats { get; set; } = new List<SummonerSeasonOverviewStat>();
    public ICollection<SummonerSeasonChampionStat> SeasonChampionStats { get; set; } = new List<SummonerSeasonChampionStat>();
    public ICollection<SummonerSeasonCoverage> SeasonCoverages { get; set; } = new List<SummonerSeasonCoverage>();
}
