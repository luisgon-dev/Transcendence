using ProjectSyndraBackend.Data.Models.LoL.Account;

namespace ProjectSyndraBackend.Data.Models.LoL.Match;

public class MatchSummoner
{
    public Guid? MatchId { get; set; }
    public Match? Match { get; set; }

    public Guid? SummonerId { get; set; }
    public Summoner? Summoner { get; set; }
}