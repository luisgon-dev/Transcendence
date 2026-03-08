using System.Text.Json.Serialization;

namespace Transcendence.Data.Models.Tft.Account;

public class TftHistoricalRank
{
    public Guid Id { get; set; }
    public Guid SummonerId { get; set; }
    public string? QueueType { get; set; }
    public string? Tier { get; set; }
    public string? RankNumber { get; set; }
    public int LeaguePoints { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    public DateTime DateRecorded { get; set; }

    [JsonIgnore] public TftSummoner? Summoner { get; set; }
}
