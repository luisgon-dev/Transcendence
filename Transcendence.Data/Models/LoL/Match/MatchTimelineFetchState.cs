namespace Transcendence.Data.Models.LoL.Match;

public enum MatchTimelineFetchStatus
{
    Unfetched = 0,
    Success = 1,
    TemporaryFailure = 2,
    PermanentlyFailed = 3,
    NotApplicable = 4
}

public class MatchTimelineFetchState
{
    public Guid MatchId { get; set; }
    public MatchTimelineFetchStatus Status { get; set; } = MatchTimelineFetchStatus.Unfetched;
    public int RetryCount { get; set; }
    public DateTime? LastAttemptAtUtc { get; set; }
    public DateTime? LastSuccessAtUtc { get; set; }
    public string? LastError { get; set; }
    public string? SourcePatch { get; set; }

    /// <summary>
    /// Ingestion schema the row was last written with. Bumped when the timeline job starts
    /// deriving new data (ordered item purchases, skill orders) so already-<see cref="MatchTimelineFetchStatus.Success"/>
    /// matches are re-ingested once to backfill it.
    /// </summary>
    public int SchemaVersion { get; set; }

    public required Match Match { get; set; }
}
