using System.Diagnostics;
using System.Data;
using System.Data.Common;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Analytics;
using Transcendence.Service.Core.Services.Analytics.Interfaces;

namespace Transcendence.Service.Core.Services.Jobs;

/// <summary>
/// Independently advances Build Atlas without depending on the champion precompute pipeline.
/// Generation promotion is handled by the refresher; this job only resolves the active patch and
/// applies bootstrap/no-op policy.
/// </summary>
public sealed class RefreshBuildResourceAnalyticsJob(
    TranscendenceContext db,
    IBuildResourceSnapshotRefresher refresher,
    ILogger<RefreshBuildResourceAnalyticsJob> logger)
{
    private const string ExecutionLockResource = "transcendence:build-atlas-refresh";

    [Queue(HangfireQueues.AnalyticsWarm)]
    [DisableConcurrentExecution(timeoutInSeconds: 60 * 60)]
    public async Task ExecuteAsync(bool onlyIfMissing, bool forceFullRebuild, CancellationToken ct)
    {
        var patch = await db.Patches.AsNoTracking()
            .Where(candidate => candidate.IsActive)
            .Select(candidate => candidate.Version)
            .FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(patch))
        {
            logger.LogWarning("Build Atlas refresh skipped: no active patch found.");
            return;
        }

        var executionLock = await TryAcquireExecutionLockAsync(ct);
        if (!executionLock.Acquired)
        {
            logger.LogInformation(
                "Build Atlas refresh skipped for patch {Patch}: another generation is already running.",
                patch);
            return;
        }

        try
        {
            if (onlyIfMissing)
            {
                var ready = await db.BuildResourceSnapshots.AsNoTracking().AnyAsync(snapshot =>
                    snapshot.Patch == patch &&
                    snapshot.IsActive &&
                    snapshot.Status == BuildResourceSnapshotStatus.Ready, ct);
                if (ready)
                {
                    logger.LogInformation(
                        "Build Atlas bootstrap skipped for patch {Patch}: an active snapshot already exists.",
                        patch);
                    return;
                }
            }

            var stopwatch = Stopwatch.StartNew();
            var result = await refresher.RefreshAsync(patch, forceFullRebuild, ct);
            logger.LogInformation(
                "Build Atlas refresh patch {Patch} completed in {ElapsedMs}ms: snapshot={SnapshotId}, full={Full}, newMatches={Matches}, resourceRows={ResourceRows}, populationRows={PopulationRows}.",
                patch,
                stopwatch.ElapsedMilliseconds,
                result.SnapshotId,
                result.FullRebuild,
                result.ProcessedMatchCount,
                result.ResourceRows,
                result.PopulationRows);
        }
        finally
        {
            await ReleaseExecutionLockAsync(executionLock);
        }
    }

    private async Task<ExecutionLock> TryAcquireExecutionLockAsync(CancellationToken ct)
    {
        if (!string.Equals(
                db.Database.ProviderName,
                "Npgsql.EntityFrameworkCore.PostgreSQL",
                StringComparison.Ordinal))
            return new ExecutionLock(Acquired: true, UsesPostgresAdvisoryLock: false, OpenedConnection: false);

        var connection = db.Database.GetDbConnection();
        var openedConnection = connection.State != ConnectionState.Open;
        if (openedConnection)
            await db.Database.OpenConnectionAsync(ct);

        try
        {
            await using var command = CreateAdvisoryLockCommand(
                connection,
                "SELECT pg_try_advisory_lock(hashtextextended(@resource, 0));");
            var result = await command.ExecuteScalarAsync(ct);
            var acquired = result is true;
            if (!acquired && openedConnection)
                await db.Database.CloseConnectionAsync();
            return new ExecutionLock(acquired, UsesPostgresAdvisoryLock: acquired, openedConnection);
        }
        catch
        {
            if (openedConnection)
                await db.Database.CloseConnectionAsync();
            throw;
        }
    }

    private async Task ReleaseExecutionLockAsync(ExecutionLock executionLock)
    {
        if (!executionLock.UsesPostgresAdvisoryLock)
            return;

        var connection = db.Database.GetDbConnection();
        try
        {
            await using var command = CreateAdvisoryLockCommand(
                connection,
                "SELECT pg_advisory_unlock(hashtextextended(@resource, 0));");
            await command.ExecuteScalarAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to release the Build Atlas PostgreSQL advisory lock.");
        }
        finally
        {
            if (executionLock.OpenedConnection)
                await db.Database.CloseConnectionAsync();
        }
    }

    private static DbCommand CreateAdvisoryLockCommand(DbConnection connection, string commandText)
    {
        var command = connection.CreateCommand();
        command.CommandText = commandText;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "resource";
        parameter.Value = ExecutionLockResource;
        command.Parameters.Add(parameter);
        return command;
    }

    private readonly record struct ExecutionLock(
        bool Acquired,
        bool UsesPostgresAdvisoryLock,
        bool OpenedConnection);
}
