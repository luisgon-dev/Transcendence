using System.Text.Json.Serialization;

namespace Transcendence.Data.Models.Tft.Account;

public class TftRank
{
    public Guid Id { get; set; }
    public string Tier { get; set; } = string.Empty;
    public string RankNumber { get; set; } = string.Empty;
    public int LeaguePoints { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    public string QueueType { get; set; } = string.Empty;
    public Guid SummonerId { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [JsonIgnore] public TftSummoner? Summoner { get; set; }
}
