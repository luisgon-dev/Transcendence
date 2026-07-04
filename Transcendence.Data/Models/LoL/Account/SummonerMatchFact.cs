namespace Transcendence.Data.Models.LoL.Account;

public class SummonerMatchFact
{
    public Guid Id { get; set; }
    public Guid SummonerId { get; set; }
    public Summoner? Summoner { get; set; }
    public string MatchId { get; set; } = string.Empty;
    public string Puuid { get; set; } = string.Empty;
    public string? PlatformRegion { get; set; }
    public string? RegionalRoute { get; set; }
    public long MatchDate { get; set; }
    public string SeasonKey { get; set; } = string.Empty;
    public string? Patch { get; set; }
    public int QueueId { get; set; }
    public string? QueueType { get; set; }
    public string? QueueFamily { get; set; }
    public int DurationSeconds { get; set; }
    public string? EndOfGameResult { get; set; }
    public int ParticipantId { get; set; }
    public int TeamId { get; set; }
    public int ChampionId { get; set; }
    public string? TeamPosition { get; set; }
    public string? IndividualPosition { get; set; }
    public bool Win { get; set; }
    public int Kills { get; set; }
    public int Deaths { get; set; }
    public int Assists { get; set; }
    public int VisionScore { get; set; }
    public int TotalDamageDealtToChampions { get; set; }
    public int TotalMinionsKilled { get; set; }
    public int NeutralMinionsKilled { get; set; }
    public int SummonerSpell1Id { get; set; }
    public int SummonerSpell2Id { get; set; }
    public bool? GameEndedInEarlySurrender { get; set; }
    public bool? GameEndedInSurrender { get; set; }
    public bool? TeamEarlySurrendered { get; set; }
    public bool CountsTowardRankedTotal { get; set; }
    public int RankedCountClassifierVersion { get; set; }
    public string? RankedCountExclusionReason { get; set; }
    public DateTime FetchedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
