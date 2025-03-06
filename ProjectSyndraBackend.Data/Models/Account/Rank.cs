namespace ProjectSyndraBackend.Data.Models.Account;

public class Rank
{
    public int RankId { get; set; }
    public string Tier { get; set; } = "";
    public string RankNumber { get; set; } = "";
    public int LeaguePoints { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    public string QueueType { get; set; } = "";
    public required Summoner Summoner { get; set; }
    
}