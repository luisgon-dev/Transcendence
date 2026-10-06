using Transcendence.Service.Core.Services.LiveGame.Models;
using System.ComponentModel.DataAnnotations;

namespace Transcendence.Service.Core.Services.Operations;

public static class OperationKinds
{
    public const string SummonerRefresh = "summoner_refresh";
    public const string LiveGameProbe = "live_game_probe";
    public const string FullHistory = "full_history";
}

public static class OperationStatuses
{
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Retrying = "retrying";
    public const string Succeeded = "succeeded";
    public const string Partial = "partial";
    public const string Failed = "failed";
    public static bool IsTerminal(string status) => status is Succeeded or Partial or Failed;
}

public enum OperationOwnerKind { User, Application }
public sealed record OperationOwner(OperationOwnerKind Kind, Guid Id);
public sealed record OperationResource(string PlatformRegion, string GameName, string TagLine);
public sealed record TrackedOperation(Guid ExecutionId, Guid OperationId);
public sealed record OperationDispatch(string? LockHandle, string? PriorityLockHandle,
    Guid? RequestedByUserAccountId, Guid? SummonerId = null);
public sealed record OperationAcceptedResponse([property: Required] Guid OperationId, string StatusUrl,
    [property: Required] int RetryAfterSeconds);
public sealed record OperationPhase(string Name, string Status, Guid? OperationId);
public sealed record OperationResult(
    Guid? SummonerId = null,
    Guid? SnapshotId = null,
    DateTime? ObservedAtUtc = null,
    DateTime? ProfileUpdatedAtUtc = null,
    DateTime? RecentImportCompletedAtUtc = null,
    int? PersistedMatchCount = null,
    int? DeferredMatchCount = null,
    int? FailedMatchCount = null,
    IReadOnlyList<string>? WarningCodes = null,
    LiveGameResponseDto? LiveGame = null);
public sealed record OperationStatusResponse(
    [property: Required] Guid OperationId, string Kind, string PlatformRegion, string GameName, string TagLine,
    string Status, [property: Required] int RetryAfterSeconds,
    [property: Required] DateTime QueuedAtUtc, [property: Required] DateTime UpdatedAtUtc,
    DateTime? CompletedAtUtc, string? ErrorCode,
    IReadOnlyList<OperationPhase> Phases, OperationResult Result);

public interface IBackgroundOperationTracker
{
    Task<TrackedOperation> CreateAsync(string kind, string resourceKey, OperationResource resource,
        Guid leaseToken, OperationOwner owner, OperationDispatch dispatch, CancellationToken ct = default);
    Task<TrackedOperation> JoinAsync(string resourceKey, Guid leaseToken, OperationOwner owner,
        CancellationToken ct = default);
    Task<OperationStatusResponse?> GetAsync(Guid requestId, IReadOnlyCollection<OperationOwner> owners,
        CancellationToken ct = default);
    Task<bool> StartAsync(Guid id, CancellationToken ct = default);
    Task SetPhaseAsync(Guid id, string phase, string status, CancellationToken ct = default);
    Task CompleteAsync(Guid id, string status, OperationResult result, CancellationToken ct = default);
    Task RetryAsync(Guid id, string errorCode, DateTime? retryAtUtc = null, CancellationToken ct = default);
    Task FailAsync(Guid id, string errorCode, CancellationToken ct = default);
    Task<Guid> CreateChildAsync(Guid parentId, string kind, string resourceKey, Guid summonerId, CancellationToken ct = default);
}

public interface IBackgroundOperationDispatcher
{
    Task DispatchAsync(Guid executionId, CancellationToken ct = default);
}
