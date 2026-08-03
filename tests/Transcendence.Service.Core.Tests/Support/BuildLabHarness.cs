using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Transcendence.Data;

namespace Transcendence.Service.Core.Tests.Support;

/// <summary>
/// Records every statement the provider executes, so a test can assert that a short-circuit issued no
/// database work at all or that a shared lookup was resolved exactly once.
/// </summary>
internal sealed class RecordingCommandInterceptor : DbCommandInterceptor
{
    private readonly List<string> statements = [];

    public IReadOnlyList<string> Statements
    {
        get
        {
            lock (statements)
                return [.. statements];
        }
    }

    public int CountContaining(string fragment) =>
        Statements.Count(statement => statement.Contains(fragment, StringComparison.Ordinal));

    public void Clear()
    {
        lock (statements)
            statements.Clear();
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        Record(command);
        return base.ReaderExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Record(command);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result)
    {
        Record(command);
        return base.NonQueryExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Record(command);
        return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result)
    {
        Record(command);
        return base.ScalarExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        Record(command);
        return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
    }

    private void Record(DbCommand command)
    {
        lock (statements)
            statements.Add(command.CommandText);
    }
}

/// <summary>
/// SQLite-backed harness for the Build Lab services, matching the connection/context pattern the
/// other analytics tests use. A single open in-memory connection is shared by every context the
/// harness hands out so a test can exercise a job that runs across scoped contexts.
/// </summary>
internal sealed class BuildLabHarness : IAsyncDisposable
{
    private readonly SqliteConnection connection;
    private readonly ServiceProvider services;
    private readonly DbContextOptions<TranscendenceContext> contextOptions;
    private readonly List<TranscendenceContext> tracked = [];

    private BuildLabHarness(
        SqliteConnection connection,
        ServiceProvider services,
        DbContextOptions<TranscendenceContext> contextOptions,
        RecordingCommandInterceptor sql)
    {
        this.connection = connection;
        this.services = services;
        this.contextOptions = contextOptions;
        Sql = sql;
        Db = NewContext();
    }

    public RecordingCommandInterceptor Sql { get; }

    /// <summary>Primary context, for seeding and for services that only need one scope.</summary>
    public TranscendenceContext Db { get; }

    public HybridCache Cache => services.GetRequiredService<HybridCache>();

    public TranscendenceContext NewContext()
    {
        var context = new SqliteCompatibleTranscendenceContext(contextOptions);
        tracked.Add(context);
        return context;
    }

    /// <param name="withPromotionLockShim">
    /// Registers no-op SQLite implementations of the PostgreSQL advisory-lock functions the promotion
    /// transaction issues. The shim only lets the statement execute; cross-process serialisation is
    /// not modelled and remains a PostgreSQL-only concern.
    /// </param>
    /// <param name="modelerHoldsLock">
    /// Whether a live modeler is holding the modeling advisory lock. The coordinator decides an
    /// abandoned run by whether it can take that lock, so this is what a SQLite harness has to fake.
    /// </param>
    public static async Task<BuildLabHarness> CreateAsync(
        bool withPromotionLockShim = true,
        bool modelerHoldsLock = false)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        if (withPromotionLockShim)
        {
            connection.CreateFunction<string, long, long>(
                "hashtextextended",
                (text, seed) => (text ?? string.Empty).GetHashCode(StringComparison.Ordinal) + seed);
            connection.CreateFunction<long, long>("pg_advisory_xact_lock", key => key);
            // Acquired only when no modeler holds it, which is exactly the reaper's liveness question.
            connection.CreateFunction<long, bool>("pg_try_advisory_lock", _ => !modelerHoldsLock);
            connection.CreateFunction<long, bool>("pg_advisory_unlock", _ => true);
        }

        var sql = new RecordingCommandInterceptor();
        var contextOptions = new DbContextOptionsBuilder<TranscendenceContext>()
            .UseSqlite(connection)
            .AddInterceptors(sql)
            .Options;

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();
        serviceCollection.AddHybridCache();
        var services = serviceCollection.BuildServiceProvider();

        var harness = new BuildLabHarness(connection, services, contextOptions, sql);
        await harness.Db.Database.EnsureCreatedAsync();
        harness.Sql.Clear();
        return harness;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var context in tracked)
            await context.DisposeAsync();
        await services.DisposeAsync();
        await connection.DisposeAsync();
    }
}
