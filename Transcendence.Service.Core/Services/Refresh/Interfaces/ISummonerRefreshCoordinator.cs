using Camille.Enums;

namespace Transcendence.Service.Core.Services.Refresh.Interfaces;

public interface ISummonerRefreshCoordinator
{
    Task<RefreshEnqueueOutcome> EnqueueRefreshAsync(
        string gameName,
        string tagLine,
        PlatformRoute platform,
        string? pollUrl,
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

public sealed record RefreshEnqueueOutcome(bool WasQueued, string? PollUrl, int? RetryAfterSeconds)
{
    public static RefreshEnqueueOutcome Queued(string? pollUrl) => new(true, pollUrl, null);

    public static RefreshEnqueueOutcome InProgress(string? pollUrl, int retryAfterSeconds) =>
        new(false, pollUrl, retryAfterSeconds);
}

public sealed record RefreshProgress(int RetryAfterSeconds);
