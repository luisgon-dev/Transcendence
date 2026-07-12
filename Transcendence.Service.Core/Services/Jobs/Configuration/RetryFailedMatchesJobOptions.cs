namespace Transcendence.Service.Core.Services.Jobs.Configuration;

public class RetryFailedMatchesJobOptions
{
    public int MaxMatchesPerRun { get; set; } = 20;
    public int MinimumMinutesSinceLastAttempt { get; set; } = 15;

    /// <summary>
    /// Per-run cap on reviving matches that were wrongly flipped to
    /// <c>PermanentlyUnfetchable</c> by the old rate-gate-as-failure bug (identified by their
    /// error signature). Revives them to <c>TemporaryFailure</c> so the corrected fetch path
    /// re-evaluates them. Genuine 404/gone rows are never touched. Set to <c>0</c> to disable.
    /// </summary>
    public int RevivePermanentlyUnfetchablePerRun { get; set; } = 25;
}
