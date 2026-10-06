using System.Text.Json;
using Camille.Enums;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Transcendence.Data;
using Transcendence.Service.Core.Services.Jobs;
using Transcendence.Service.Core.Services.Jobs.Interfaces;

namespace Transcendence.Service.Core.Services.Operations;

/// <summary>Recoverable outbox dispatch. A duplicate after a crash retains the same execution ID.</summary>
public sealed class BackgroundOperationDispatcher(TranscendenceContext db, IBackgroundJobClient jobs)
    : IBackgroundOperationDispatcher
{
    public async Task DispatchAsync(Guid executionId, CancellationToken ct = default)
    {
        await db.Database.OpenConnectionAsync(ct);
        var connection = db.Database.GetDbConnection();
        await using var acquire = connection.CreateCommand();
        acquire.CommandText = "SELECT pg_try_advisory_lock(hashtextextended(@resource, 0))";
        var parameter = acquire.CreateParameter();
        parameter.ParameterName = "resource";
        parameter.Value = $"operation-dispatch:{executionId:N}";
        acquire.Parameters.Add(parameter);
        var held = false;
        try
        {
            held = (bool)(await acquire.ExecuteScalarAsync(ct))!;
            if (!held) return;
            var row = await db.BackgroundOperations.AsNoTracking().SingleAsync(x => x.Id == executionId, ct);
            if (row.DispatchedAtUtc is not null || OperationStatuses.IsTerminal(row.Status)) return;
            var dispatch = JsonSerializer.Deserialize<OperationDispatch>(row.DispatchJson)!;
            var job = row.Kind switch
            {
                OperationKinds.SummonerRefresh => Job.FromExpression<ISummonerRefreshJob>(x => x.RefreshByRiotId(
                    row.Id, row.GameName, row.TagLine, Enum.Parse<PlatformRoute>(row.PlatformRegion),
                    dispatch.LockHandle!, dispatch.PriorityLockHandle, dispatch.RequestedByUserAccountId, CancellationToken.None)),
                OperationKinds.LiveGameProbe => Job.FromExpression<ILiveGameProbeJob>(x => x.ProbeAsync(
                    row.Id, row.PlatformRegion, row.GameName, row.TagLine, dispatch.LockHandle!, CancellationToken.None)),
                OperationKinds.FullHistory => Job.FromExpression<FullHistoryBackfillJob>(x => x.ProcessAsync(
                    row.Id, dispatch.SummonerId!.Value, dispatch.RequestedByUserAccountId, CancellationToken.None)),
                _ => throw new InvalidOperationException("Unknown durable operation kind.")
            };
            var queue = row.Kind == OperationKinds.FullHistory ? HangfireQueues.HistoryBackfill : HangfireQueues.RefreshHigh;
            var jobId = jobs.Create(job, new EnqueuedState(queue));
            await db.BackgroundOperations.Where(x => x.Id == executionId && x.DispatchedAtUtc == null)
                .ExecuteUpdateAsync(set => set.SetProperty(x => x.DispatchedAtUtc, DateTime.UtcNow)
                    .SetProperty(x => x.HangfireJobId, jobId), ct);
        }
        finally
        {
            if (held)
            {
                await using var release = connection.CreateCommand();
                release.CommandText = "SELECT pg_advisory_unlock(hashtextextended(@resource, 0))";
                var releaseParameter = release.CreateParameter();
                releaseParameter.ParameterName = "resource"; releaseParameter.Value = parameter.Value;
                release.Parameters.Add(releaseParameter);
                await release.ExecuteScalarAsync(CancellationToken.None);
            }
            await db.Database.CloseConnectionAsync();
        }
    }
}

public sealed class OperationDispatchService(IServiceScopeFactory scopes, ILogger<OperationDispatchService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<TranscendenceContext>();
                var pending = await db.BackgroundOperations.AsNoTracking()
                    .Where(x => x.DispatchedAtUtc == null && x.Status != OperationStatuses.Succeeded
                        && x.Status != OperationStatuses.Partial && x.Status != OperationStatuses.Failed)
                    .OrderBy(x => x.QueuedAtUtc).Select(x => x.Id).Take(100).ToListAsync(stoppingToken);
                foreach (var id in pending)
                {
                    using var dispatchScope = scopes.CreateScope();
                    try { await dispatchScope.ServiceProvider.GetRequiredService<IBackgroundOperationDispatcher>().DispatchAsync(id, stoppingToken); }
                    catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                    { logger.LogWarning(ex, "Durable operation dispatch deferred for {OperationId}.", id); }
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            { logger.LogWarning(ex, "Durable operation dispatch scan deferred."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
