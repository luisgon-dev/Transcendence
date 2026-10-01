using Camille.Enums;
using Transcendence.Service.Core.Services.Operations;

namespace Transcendence.Service.Core.Services.Refresh.Interfaces;

public interface ISummonerRefreshCoordinator
{
    Task<RefreshEnqueueOutcome> EnqueueRefreshAsync(
        string gameName,
        string tagLine,
        PlatformRoute platform,
        OperationOwner owner,
        string traceId,
        Guid? requestedByUserAccountId,
        string telemetrySource,
        CancellationToken ct = default);

    Task<RefreshProgress?> GetProgressAsync(
        string gameName,
        string tagLine,
        PlatformRoute platform,
        string telemetrySource,
        CancellationToken ct = default);
}

public sealed record RefreshEnqueueOutcome(bool WasQueued, Guid OperationId, int RetryAfterSeconds)
{
    public static RefreshEnqueueOutcome Queued(Guid operationId) => new(true, operationId, 2);

    public static RefreshEnqueueOutcome InProgress(Guid operationId) => new(false, operationId, 2);
}

public sealed record RefreshProgress(int RetryAfterSeconds);
