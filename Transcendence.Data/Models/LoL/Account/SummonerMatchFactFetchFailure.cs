namespace Transcendence.Data.Models.LoL.Account;

public class SummonerMatchFactFetchFailure
{
    public Guid Id { get; set; }
    public Guid SummonerId { get; set; }
    public Summoner? Summoner { get; set; }
    public string MatchId { get; set; } = string.Empty;
    public string? PlatformRegion { get; set; }
    public string? RegionalRoute { get; set; }
    public int AttemptCount { get; set; }
    public string? LastErrorMessage { get; set; }
    public DateTime FirstAttemptAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastAttemptAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAtUtc { get; set; }
}
