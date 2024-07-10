using ProjectSyndraBackend.Data.Models.Account;

namespace ProjectSyndraBackend.Data.Models.Match;

public class MatchDetail
{
    public int Id { get; set; }
    public int Kills { get; set; }
    public int Deaths { get; set; }
    public int Assists { get; set; }
    public List<string> Items { get; set; } = [];
    public List<string> Runes { get; set; } = [];
    public string MatchId { get; set; }
    public Match Match { get; set; }
    public string SummonerId { get; set; }
    public Summoner Summoner { get; set; }
}