using ProjectSyndraBackend.Data.Models.LoL.Account;

namespace ProjectSyndraBackend.Data.Models.LoL.Match;

public class MatchSummoner
{
    public Guid Id { get; set; }
    
    // Use GUID foreign keys for relationships
    public Guid MatchId { get; set; }
    public Match? Match { get; set; }

    public Guid SummonerId { get; set; }
    public Summoner? Summoner { get; set; }
    
    // Keep external IDs for reference
    public string? ExternalMatchId { get; set; }
    public string? ExternalSummonerId { get; set; }
}
