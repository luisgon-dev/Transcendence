namespace Transcendence.Data.Models.LoL.Account;

public class SummonerSeasonChampionStat
{
    public Guid Id { get; set; }
    public Guid SummonerId { get; set; }
    public Summoner? Summoner { get; set; }
    public string SeasonKey { get; set; } = string.Empty;
    public string QueueScope { get; set; } = string.Empty;
    public int ChampionId { get; set; }
    public int Games { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    public long TotalKills { get; set; }
    public long TotalDeaths { get; set; }
    public long TotalAssists { get; set; }
    public long TotalVisionScore { get; set; }
    public long TotalDamageToChamps { get; set; }
    public long TotalCs { get; set; }
    public long TotalDurationSeconds { get; set; }
    public int AggregationVersion { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
