namespace Transcendence.Service.Core.Services.Analytics.Interfaces;

public interface IBuildResourceSnapshotRefresher
{
    Task<BuildResourceSnapshotRefreshResult> RefreshAsync(
        string patch,
        bool forceFullRebuild,
        CancellationToken ct);
}

public sealed record BuildResourceSnapshotRefreshResult(
    Guid SnapshotId,
    string Patch,
    bool FullRebuild,
    int ProcessedMatchCount,
    int ResourceRows,
    int PopulationRows);
