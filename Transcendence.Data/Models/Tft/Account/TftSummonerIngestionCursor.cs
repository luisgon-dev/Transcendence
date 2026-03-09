namespace Transcendence.Data.Models.Tft.Account;

public class TftSummonerIngestionCursor
{
    public Guid SummonerId { get; set; }
    public string Scope { get; set; } = string.Empty;
    public long? BackfillBeforeEpochSeconds { get; set; }
    public DateTime LastRunAtUtc { get; set; } = DateTime.UtcNow;
    public int ConsecutiveNoopRuns { get; set; }
    public int Version { get; set; } = 1;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public required TftSummoner Summoner { get; set; }
}
