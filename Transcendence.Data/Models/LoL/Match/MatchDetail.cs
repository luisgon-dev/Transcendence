using Transcendence.Data.Models.LoL.Account;

namespace Transcendence.Data.Models.LoL.Match;

public class MatchDetail
{
    public Guid Id { get; set; }
    public int Kills { get; set; }
    public int Deaths { get; set; }
    public int Assists { get; set; }

    public bool Win { get; set; }

    public string Lane { get; set; }
    public string Role { get; set; }

    public int SummonerSpell1 { get; set; }

    public int SummonerSpell2 { get; set; }
    public int ChampionId { get; set; }

    public string ChampionName { get; set; }

    public List<int> Items { get; set; } = [];

    public Runes Runes { get; set; }
    public Guid MatchId { get; set; }
    public Match Match { get; set; }
    public Guid SummonerId { get; set; }
    public Summoner Summoner { get; set; }
}