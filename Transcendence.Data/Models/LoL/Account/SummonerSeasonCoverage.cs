namespace Transcendence.Data.Models.LoL.Account;

public class SummonerSeasonCoverage
{
    public Guid Id { get; set; }
    public Guid SummonerId { get; set; }
    public Summoner? Summoner { get; set; }
    public string SeasonKey { get; set; } = string.Empty;
    public string QueueScope { get; set; } = string.Empty;
    public string BackfillStatus { get; set; } = SummonerFullHistoryBackfillStatuses.Queued;
    public int CompletedMatchCount { get; set; }
    public int? RiotWins { get; set; }
    public int? RiotLosses { get; set; }
    public int? RiotTotal { get; set; }
    public int? RankedCountDelta { get; set; }
    public string? CoverageStatus { get; set; }
    public int ClassifierVersion { get; set; }
    public DateTime? LastComparedAtUtc { get; set; }
    public DateTime? LastBackfilledAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
