using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Transcendence.Data;
using Transcendence.Data.Models.Service;

namespace Transcendence.Service.Core.Services.Operations;

public sealed class BackgroundOperationTracker(TranscendenceContext db) : IBackgroundOperationTracker
{
    public async Task<TrackedOperation> CreateAsync(string kind, string resourceKey, OperationResource resource,
        Guid leaseToken, OperationOwner owner, OperationDispatch dispatch, CancellationToken ct = default)
    {
        var execution = new BackgroundOperation
        {
            Id = Guid.NewGuid(), Kind = kind, ResourceKey = resourceKey, LeaseToken = leaseToken,
            PlatformRegion = resource.PlatformRegion, GameName = resource.GameName, TagLine = resource.TagLine,
            PhasesJson = JsonSerializer.Serialize(InitialPhases(kind)), DispatchJson = JsonSerializer.Serialize(dispatch)
        };
        var request = NewRequest(execution.Id, owner);
        db.BackgroundOperations.Add(execution);
        db.BackgroundOperationRequests.Add(request);
        await db.SaveChangesAsync(ct);
        return new TrackedOperation(execution.Id, request.Id);
    }

    public async Task<TrackedOperation> JoinAsync(string resourceKey, Guid leaseToken, OperationOwner owner,
        CancellationToken ct = default)
    {
        // The lease owner publishes its durable execution before enqueue. Contention can arrive in
        // that short interval; bound the wait rather than returning an untracked acceptance.
        BackgroundOperation? execution = null;
        for (var attempt = 0; attempt < 20 && execution is null; attempt++)
        {
            execution = await db.BackgroundOperations.AsNoTracking().SingleOrDefaultAsync(
                x => x.ResourceKey == resourceKey && x.LeaseToken == leaseToken, ct);
            if (execution is null) await Task.Delay(50, ct);
        }
        if (execution is null) throw new InvalidOperationException("The pending execution is not yet available.");
        var request = await GrantAsync(execution.Id, owner, ct);
        var children = await db.BackgroundOperations.AsNoTracking()
            .Where(x => x.ParentId == execution.Id).Select(x => x.Id).ToListAsync(ct);
        foreach (var child in children) await GrantAsync(child, owner, ct);
        return new TrackedOperation(execution.Id, request.Id);
    }

    public async Task<OperationStatusResponse?> GetAsync(Guid requestId, IReadOnlyCollection<OperationOwner> owners,
        CancellationToken ct = default)
    {
        var request = await db.BackgroundOperationRequests.AsNoTracking()
            .Include(x => x.Execution).SingleOrDefaultAsync(x => x.Id == requestId, ct);
        if (request is null || !owners.Contains(OwnerOf(request))) return null;
        var execution = request.Execution;
        var phases = JsonSerializer.Deserialize<Dictionary<string, string>>(execution.PhasesJson)!;
        var phaseDtos = phases.Select(x => new OperationPhase(x.Key, x.Value, null)).ToList();
        var children = await db.BackgroundOperations.AsNoTracking()
            .Where(x => x.ParentId == execution.Id).ToListAsync(ct);
        foreach (var child in children)
        {
            // A join racing child creation inherits access from its authorized parent request.
            var childRequest = await GrantAsync(child.Id, OwnerOf(request), ct);
            phaseDtos.Add(new OperationPhase("fullHistory", child.Status, childRequest.Id));
        }
        var retry = OperationStatuses.IsTerminal(execution.Status) ? 0 : execution.RetryAtUtc is { } at
            ? (int)Math.Clamp(Math.Ceiling((at - DateTime.UtcNow).TotalSeconds), 1, 30) : 2;
        var result = JsonSerializer.Deserialize<OperationResult>(execution.ResultJson)!;
        if (result.LiveGame is { } game && result.ObservedAtUtc is { } observation)
            result = result with { LiveGame = game with { LastUpdatedUtc = observation,
                DataAgeSeconds = (int)Math.Clamp((DateTime.UtcNow - observation).TotalSeconds, 0, int.MaxValue) } };
        return new OperationStatusResponse(request.Id, execution.Kind, execution.PlatformRegion,
            execution.GameName, execution.TagLine, execution.Status, retry,
            execution.QueuedAtUtc, execution.UpdatedAtUtc, execution.CompletedAtUtc, execution.ErrorCode,
            phaseDtos, result);
    }

    public async Task<bool> StartAsync(Guid id, CancellationToken ct = default) =>
        await MutateAsync(id, row => { row.Status = OperationStatuses.Running; row.RetryAtUtc = null; row.ErrorCode = null; }, ct);

    public async Task SetPhaseAsync(Guid id, string phase, string status, CancellationToken ct = default)
    {
        ValidateStatus(status);
        if (phase is not ("profile" or "recentHistory" or "fullHistory" or "mastery"))
            throw new ArgumentException("Unknown operation phase.", nameof(phase));
        await MutateAsync(id, row =>
        {
            var phases = JsonSerializer.Deserialize<Dictionary<string, string>>(row.PhasesJson)!;
            phases[phase] = status;
            row.PhasesJson = JsonSerializer.Serialize(phases);
        }, ct);
    }

    public async Task CompleteAsync(Guid id, string status, OperationResult result, CancellationToken ct = default)
    {
        if (!OperationStatuses.IsTerminal(status)) throw new ArgumentException("Completion must be terminal.", nameof(status));
        if (result.WarningCodes is { } warnings) foreach (var warning in warnings) ValidateCode(warning);
        await MutateAsync(id, row =>
        {
            row.Status = status; row.ResultJson = JsonSerializer.Serialize(result);
            row.CompletedAtUtc = DateTime.UtcNow; row.RetryAtUtc = null; row.ErrorCode = null;
        }, ct);
    }

    public async Task RetryAsync(Guid id, string errorCode, DateTime? retryAtUtc = null, CancellationToken ct = default)
    {
        ValidateCode(errorCode);
        await MutateAsync(id, row =>
        {
            row.Status = OperationStatuses.Retrying; row.ErrorCode = errorCode; row.RetryAtUtc = retryAtUtc;
        }, ct);
    }

    public async Task FailAsync(Guid id, string errorCode, CancellationToken ct = default)
    {
        ValidateCode(errorCode);
        await MutateAsync(id, row =>
        {
            row.Status = OperationStatuses.Failed; row.ErrorCode = errorCode;
            row.CompletedAtUtc = DateTime.UtcNow; row.RetryAtUtc = null;
            var phases = JsonSerializer.Deserialize<Dictionary<string, string>>(row.PhasesJson)!;
            foreach (var phase in phases.Keys.ToArray())
                if (!OperationStatuses.IsTerminal(phases[phase])) phases[phase] = OperationStatuses.Failed;
            row.PhasesJson = JsonSerializer.Serialize(phases);
        }, ct);
    }

    public async Task<Guid> CreateChildAsync(Guid parentId, string kind, string resourceKey, Guid summonerId,
        CancellationToken ct = default)
    {
        var existing = await db.BackgroundOperations.AsNoTracking()
            .SingleOrDefaultAsync(x => x.ParentId == parentId && x.Kind == kind, ct);
        if (existing is not null) return existing.Id;
        var parent = await db.BackgroundOperations.AsNoTracking().SingleAsync(x => x.Id == parentId, ct);
        var child = new BackgroundOperation
        {
            Id = Guid.NewGuid(), ParentId = parentId, Kind = kind, ResourceKey = resourceKey,
            PlatformRegion = parent.PlatformRegion, GameName = parent.GameName, TagLine = parent.TagLine,
            PhasesJson = JsonSerializer.Serialize(InitialPhases(kind)),
            DispatchJson = JsonSerializer.Serialize(new OperationDispatch(null, null,
                JsonSerializer.Deserialize<OperationDispatch>(parent.DispatchJson)!.RequestedByUserAccountId, summonerId))
        };
        var parentRequests = await db.BackgroundOperationRequests.AsNoTracking()
            .Where(x => x.ExecutionId == parentId).ToListAsync(ct);
        var requests = parentRequests.Select(x => NewRequest(child.Id, OwnerOf(x))).ToList();
        db.BackgroundOperations.Add(child);
        db.BackgroundOperationRequests.AddRange(requests);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            db.Entry(child).State = EntityState.Detached;
            foreach (var request in requests) db.Entry(request).State = EntityState.Detached;
            existing = await db.BackgroundOperations.AsNoTracking()
                .SingleOrDefaultAsync(x => x.ParentId == parentId && x.Kind == kind, ct);
            if (existing is null) throw;
            return existing.Id;
        }
        return child.Id;
    }

    private async Task<BackgroundOperationRequest> GrantAsync(Guid executionId, OperationOwner owner, CancellationToken ct)
    {
        var ownerKind = owner.Kind.ToString();
        var existing = await db.BackgroundOperationRequests.AsNoTracking().SingleOrDefaultAsync(
            x => x.ExecutionId == executionId && x.OwnerKind == ownerKind && x.OwnerId == owner.Id, ct);
        if (existing is not null) return existing;
        var request = NewRequest(executionId, owner);
        db.BackgroundOperationRequests.Add(request);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            db.Entry(request).State = EntityState.Detached;
            existing = await db.BackgroundOperationRequests.AsNoTracking().SingleOrDefaultAsync(
                x => x.ExecutionId == executionId && x.OwnerKind == ownerKind && x.OwnerId == owner.Id, ct);
            if (existing is null) throw;
            return existing;
        }
        return request;
    }

    private async Task<bool> MutateAsync(Guid id, Action<BackgroundOperation> update, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var row = await db.BackgroundOperations.AsNoTracking().SingleAsync(x => x.Id == id, ct);
            if (OperationStatuses.IsTerminal(row.Status)) return false;
            var revision = row.Revision;
            update(row);
            row.UpdatedAtUtc = DateTime.UtcNow;
            var updated = await db.BackgroundOperations.Where(x => x.Id == id && x.Revision == revision)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(x => x.Status, row.Status).SetProperty(x => x.ErrorCode, row.ErrorCode)
                    .SetProperty(x => x.PhasesJson, row.PhasesJson).SetProperty(x => x.ResultJson, row.ResultJson)
                    .SetProperty(x => x.UpdatedAtUtc, row.UpdatedAtUtc).SetProperty(x => x.CompletedAtUtc, row.CompletedAtUtc)
                    .SetProperty(x => x.RetryAtUtc, row.RetryAtUtc).SetProperty(x => x.Revision, revision + 1), ct);
            if (updated == 1) return true;
        }
        throw new DbUpdateConcurrencyException("Operation state changed repeatedly; retry the job.");
    }

    private static BackgroundOperationRequest NewRequest(Guid executionId, OperationOwner owner) => new()
    {
        Id = Guid.NewGuid(), ExecutionId = executionId, OwnerKind = owner.Kind.ToString(), OwnerId = owner.Id
    };
    private static OperationOwner OwnerOf(BackgroundOperationRequest request) =>
        new(Enum.Parse<OperationOwnerKind>(request.OwnerKind), request.OwnerId);
    private static Dictionary<string, string> InitialPhases(string kind) => kind switch
    {
        OperationKinds.SummonerRefresh => new() { ["profile"] = OperationStatuses.Queued, ["recentHistory"] = OperationStatuses.Queued },
        OperationKinds.FullHistory => new() { ["fullHistory"] = OperationStatuses.Queued },
        _ => new()
    };
    private static void ValidateCode(string code)
    {
        if (code.Length > 80 || !Regex.IsMatch(code, "^[a-z][a-z0-9_]*$"))
            throw new ArgumentException("Operation error codes must be bounded identifiers, never exception messages.");
    }
    private static void ValidateStatus(string status)
    {
        if (status is not (OperationStatuses.Queued or OperationStatuses.Running or OperationStatuses.Retrying
            or OperationStatuses.Succeeded or OperationStatuses.Partial or OperationStatuses.Failed))
            throw new ArgumentException("Unknown operation status.", nameof(status));
    }
}
