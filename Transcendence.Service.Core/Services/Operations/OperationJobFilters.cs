using Hangfire;
using Hangfire.Common;
using Hangfire.Server;
using Hangfire.States;
using Hangfire.Storage;
using Microsoft.EntityFrameworkCore;
using Transcendence.Data;
using Transcendence.Service.Core.Services.Jobs;
using Transcendence.Service.Core.Services.Jobs.Interfaces;
using System.Text.Json;
using Transcendence.Data.Models.LoL.Account;

namespace Transcendence.Service.Core.Services.Operations;

public sealed class OperationExecutionBusyException : Exception;

/// <summary>PostgreSQL session locks serialize duplicate and same-resource jobs across workers.</summary>
public sealed class OperationExecutionFilter(IServiceScopeFactory scopes) : JobFilterAttribute, IServerFilter, IElectStateFilter
{
    private const string HeldLock = "operation-execution-lock";
    public void OnPerforming(PerformingContext context)
    {
        if (TrackedId(context.BackgroundJob.Job) is not { } id) return;
        var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TranscendenceContext>();
        var held = new SessionLocks(scope, db);
        try
        {
            db.Database.OpenConnection();
            if (!held.TryAcquire($"operation-execution:{id:N}")) throw new OperationExecutionBusyException();
            var row = db.BackgroundOperations.AsNoTracking().Where(x => x.Id == id)
                .Select(x => new { x.Status, x.ResourceKey }).Single();
            if (OperationStatuses.IsTerminal(row.Status)) { held.Dispose(); context.Canceled = true; return; }
            if (!held.TryAcquire($"operation-resource:{row.ResourceKey}")) throw new OperationExecutionBusyException();
            context.Items[HeldLock] = held;
        }
        catch { held.Dispose(); throw; }
    }
    public void OnPerformed(PerformedContext context)
    {
        if (context.Items.TryGetValue(HeldLock, out var held)) ((IDisposable)held).Dispose();
    }
    public void OnStateElection(ElectStateContext context)
    {
        // Busy duplicates are deferred without consuming the real execution's retry/failure budget.
        if (TrackedId(context.BackgroundJob.Job) is not null && context.CandidateState is FailedState failed
            && failed.Exception is OperationExecutionBusyException)
            context.CandidateState = new ScheduledState(TimeSpan.FromSeconds(15)) { Reason = "operation_busy" };
    }
    internal static Guid? TrackedId(Job job) => job.Args.FirstOrDefault() is Guid id && id != Guid.Empty
        && (((job.Type == typeof(ISummonerRefreshJob) || job.Type == typeof(SummonerRefreshJob))
                && job.Method.Name == nameof(ISummonerRefreshJob.RefreshByRiotId))
            || ((job.Type == typeof(ILiveGameProbeJob) || job.Type == typeof(LiveGameProbeJob))
                && job.Method.Name == nameof(ILiveGameProbeJob.ProbeAsync))
            || (job.Type == typeof(FullHistoryBackfillJob) && job.Method.Name == nameof(FullHistoryBackfillJob.ProcessAsync)))
        ? id : null;

    private sealed class SessionLocks(IServiceScope scope, TranscendenceContext db) : IDisposable
    {
        private readonly List<string> acquired = [];
        public bool TryAcquire(string resource)
        {
            using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT pg_try_advisory_lock(hashtextextended(@resource, 0))";
            var parameter = command.CreateParameter(); parameter.ParameterName = "resource"; parameter.Value = resource;
            command.Parameters.Add(parameter);
            if (!(bool)command.ExecuteScalar()!) return false;
            acquired.Add(resource);
            return true;
        }
        public void Dispose()
        {
            try
            {
                foreach (var resource in acquired)
                {
                    using var command = db.Database.GetDbConnection().CreateCommand();
                    command.CommandText = "SELECT pg_advisory_unlock(hashtextextended(@resource, 0))";
                    var parameter = command.CreateParameter(); parameter.ParameterName = "resource"; parameter.Value = resource;
                    command.Parameters.Add(parameter); command.ExecuteScalar();
                }
            }
            finally { acquired.Clear(); scope.Dispose(); }
        }
    }
}

/// <summary>Observe the actual elected state after AutomaticRetry has had its chance.</summary>
public sealed class OperationOutcomeFilter(IServiceScopeFactory scopes) : JobFilterAttribute, IApplyStateFilter, IElectStateFilter
{
    public void OnStateElection(ElectStateContext context)
    {
        if (OperationExecutionFilter.TrackedId(context.BackgroundJob.Job) is not { } id
            || context.CandidateState is not ScheduledState scheduled || scheduled.Reason == "operation_busy"
            || !context.TraversedStates.OfType<FailedState>().Any()) return;
        using var scope = scopes.CreateScope();
        scope.ServiceProvider.GetRequiredService<IBackgroundOperationTracker>()
            .RetryAsync(id, "worker_retrying", scheduled.EnqueueAt).GetAwaiter().GetResult();
    }
    public void OnStateApplied(ApplyStateContext context, IWriteOnlyTransaction transaction)
    {
        if (OperationExecutionFilter.TrackedId(context.BackgroundJob.Job) is not { } id) return;
        using var scope = scopes.CreateScope();
        var tracker = scope.ServiceProvider.GetRequiredService<IBackgroundOperationTracker>();
        if (context.NewState is FailedState)
        {
            tracker.FailAsync(id, "worker_failed").GetAwaiter().GetResult();
            MarkHistoryFailed(scope.ServiceProvider, id);
        }
        else if (context.NewState is DeletedState)
        {
            tracker.FailAsync(id, "job_cancelled").GetAwaiter().GetResult();
            MarkHistoryFailed(scope.ServiceProvider, id);
        }
        // Hangfire success may be an explicit continuation; only domain jobs certify completion.
    }
    public void OnStateUnapplied(ApplyStateContext context, IWriteOnlyTransaction transaction) { }
    private static void MarkHistoryFailed(IServiceProvider provider, Guid id)
    {
        var db = provider.GetRequiredService<TranscendenceContext>();
        var row = db.BackgroundOperations.AsNoTracking().Single(x => x.Id == id);
        if (row.Kind != OperationKinds.FullHistory || row.Status != OperationStatuses.Failed) return;
        var dispatch = JsonSerializer.Deserialize<OperationDispatch>(row.DispatchJson)!;
        db.SummonerFullHistoryBackfills.Where(x => x.SummonerId == dispatch.SummonerId
            && x.OperationId == id
            && x.Scope == SummonerFullHistoryScopes.FullHistory
            && x.Status != SummonerFullHistoryBackfillStatuses.Completed
            && x.Status != SummonerFullHistoryBackfillStatuses.CompletedWithGaps)
            .ExecuteUpdate(set => set.SetProperty(x => x.Status, SummonerFullHistoryBackfillStatuses.Failed)
                .SetProperty(x => x.LastErrorMessage, "history_worker_failed")
                .SetProperty(x => x.UpdatedAtUtc, DateTime.UtcNow));
    }
}

public static class OperationWorkerExtensions
{
    public static IServiceCollection AddBackgroundOperationWorker(this IServiceCollection services)
    {
        services.AddSingleton<OperationExecutionFilter>(provider => new(provider.GetRequiredService<IServiceScopeFactory>()) { Order = -1 });
        services.AddSingleton<OperationOutcomeFilter>(provider => new(provider.GetRequiredService<IServiceScopeFactory>()) { Order = 1000 });
        services.AddHostedService<OperationDispatchService>();
        return services;
    }
    public static IGlobalConfiguration UseBackgroundOperationTracking(this IGlobalConfiguration configuration, IServiceProvider provider) =>
        configuration.UseFilter(provider.GetRequiredService<OperationExecutionFilter>())
            .UseFilter(provider.GetRequiredService<OperationOutcomeFilter>());
}
