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

    public required Match Match { get; set; }
}
