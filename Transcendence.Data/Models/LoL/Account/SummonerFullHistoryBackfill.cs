namespace Transcendence.Data.Models.LoL.Account;

public static class SummonerFullHistoryBackfillStatuses
{
    public const string Queued = "QUEUED";
    public const string Running = "RUNNING";
    public const string Completed = "COMPLETED";
    public const string CompletedWithGaps = "COMPLETED_WITH_GAPS";
    public const string Failed = "FAILED";
}

public class SummonerFullHistoryBackfill
{
    public Guid Id { get; set; }
    public Guid SummonerId { get; set; }
    public Summoner? Summoner { get; set; }
    public string Scope { get; set; } = SummonerFullHistoryScopes.FullHistory;
    public string Status { get; set; } = SummonerFullHistoryBackfillStatuses.Queued;
    public Guid? RequestedByUserAccountId { get; set; }
    public DateTime RequestedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public long? CursorEndEpochSeconds { get; set; }
    public int PagesScanned { get; set; }
    public int MatchIdsDiscovered { get; set; }
    public int FactsPersisted { get; set; }
    public int SkippedExistingFacts { get; set; }
    public int DetailFetchFailures { get; set; }
    public string? LastErrorMessage { get; set; }
}

public static class SummonerFullHistoryScopes
{
    public const string FullHistory = "FULL_HISTORY";
}
