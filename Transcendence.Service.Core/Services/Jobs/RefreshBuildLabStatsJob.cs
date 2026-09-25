using System.Data;
using System.Data.Common;
using System.Diagnostics;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Transcendence.Data;
using Transcendence.Service.Core.Services.Analytics.Interfaces;
using Transcendence.Service.Core.Services.Analytics.Models;
using Transcendence.Service.Core.Services.Diagnostics;

namespace Transcendence.Service.Core.Services.Jobs;

/// <summary>
/// Tops up the Build Lab stats with matches counted since the last run. Each run is bounded by
/// <see cref="BuildLabOptions.MaxMatchesPerRun"/>, so a fresh patch backfills across several runs.
/// </summary>
public sealed class RefreshBuildLabStatsJob(
    TranscendenceContext db,
    IBuildLabStatsRefresher refresher,
    IOptions<BuildLabOptions> options,
    BuildLabTelemetry telemetry,
    ILogger<RefreshBuildLabStatsJob> logger)
{
    private const string ExecutionLockResource = "transcendence:build-lab-refresh";

    [Queue(HangfireQueues.AnalyticsWarm)]
    [DisableConcurrentExecution(timeoutInSeconds: 60 * 60)]
    [AutomaticRetry(Attempts = 0)]
    public async Task ExecuteAsync(CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            telemetry.RecordRun("skipped");
            return;
        }

        var acquired = await TryAcquireLockAsync(ct);
        if (!acquired)
        {
            logger.LogInformation("Build Lab refresh skipped: another refresh is already running.");
            telemetry.RecordRun("skipped");
            return;
        }

        try
        {
            var stopwatch = Stopwatch.StartNew();
            var result = await refresher.RefreshAsync(ct);
            telemetry.RecordMatchesCounted(result.MatchesCounted);
            telemetry.RecordSuccess(result.Backlog);
            telemetry.RecordRun("success");
            logger.LogInformation(
                "Build Lab refresh counted {Matches} matches into {Rows} stat rows in {ElapsedMs}ms across patches {Patches}; {Backlog} matches still to count.",
                result.MatchesCounted,
                result.StatRowsWritten,
                stopwatch.ElapsedMilliseconds,
                string.Join(",", result.Patches),
                result.Backlog);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            telemetry.RecordRun("error");
            throw;
        }
        finally
        {
            await ReleaseLockAsync();
        }
    }

    private bool UsesPostgres =>
        string.Equals(db.Database.ProviderName, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal);

    // Held on the context's own connection for the whole run, so it is released with the session if the
    // worker dies mid-run and never needs a timeout.
    private async Task<bool> TryAcquireLockAsync(CancellationToken ct)
    {
        if (!UsesPostgres)
            return true;
        await db.Database.OpenConnectionAsync(ct);
        await using var command = LockCommand("SELECT pg_try_advisory_lock(hashtextextended(@resource, 0));");
        if (await command.ExecuteScalarAsync(ct) is true)
            return true;
        await db.Database.CloseConnectionAsync();
        return false;
    }

    private async Task ReleaseLockAsync()
    {
        if (!UsesPostgres)
            return;
        try
        {
            await using var command = LockCommand("SELECT pg_advisory_unlock(hashtextextended(@resource, 0));");
            await command.ExecuteScalarAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to release the Build Lab refresh advisory lock.");
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private DbCommand LockCommand(string sql)
    {
        var connection = db.Database.GetDbConnection();
        var command = connection.CreateCommand();
        command.CommandText = sql;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "resource";
        parameter.Value = ExecutionLockResource;
        command.Parameters.Add(parameter);
        return command;
    }
}
