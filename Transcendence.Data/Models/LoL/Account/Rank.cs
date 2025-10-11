using System.Text.Json.Serialization;
namespace Transcendence.Data.Models.LoL.Account;

public class Rank
{
    public Guid Id { get; set; }
    public string Tier { get; set; } = "";
    public string RankNumber { get; set; } = "";
    public int LeaguePoints { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    public string QueueType { get; set; } = "";
    public Guid SummonerId { get; set; }
    [JsonIgnore]
    public Summoner? Summoner { get; set; }
}
