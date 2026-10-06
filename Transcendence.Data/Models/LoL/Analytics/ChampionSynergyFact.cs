namespace Transcendence.Data.Models.LoL.Analytics;

// Baseline rows have PartnerParticipantId=0; pair rows describe one qualifying same-team partner.
// No Match foreign key: these compact analytics facts survive raw-detail retention.
public sealed class ChampionSynergyFact
{
    public Guid MatchId { get; set; }
    public int ParticipantId { get; set; }
    public int PartnerParticipantId { get; set; }
    public Guid SummonerId { get; set; }
    public string Patch { get; set; } = "";
    public string QueueFamily { get; set; } = "";
    public string PlatformRegion { get; set; } = "";
    public int ChampionId { get; set; }
    public string Role { get; set; } = "";
    public bool Win { get; set; }
    public int PartnerChampionId { get; set; }
    public string PartnerRole { get; set; } = "";
}

public sealed class ChampionSynergySourceMatch
{
    public Guid MatchId { get; set; }
    public string Patch { get; set; } = "";
    public DateTime MaterializedAtUtc { get; set; }
}
