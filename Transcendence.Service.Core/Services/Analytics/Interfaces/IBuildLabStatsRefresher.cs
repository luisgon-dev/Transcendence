namespace Transcendence.Service.Core.Services.Analytics.Interfaces;

public interface IBuildLabStatsRefresher
{
    /// <summary>
    /// Counts the build decisions of eligible matches not yet counted, up to the per-run budget, for
    /// the active patch first and then the patches before it.
    /// </summary>
    Task<BuildLabRefreshResult> RefreshAsync(CancellationToken ct);
}

public sealed record BuildLabRefreshResult(
    IReadOnlyList<string> Patches,
    int MatchesCounted,
    int StatRowsWritten,
    long Backlog);
