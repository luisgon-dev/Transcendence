using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Transcendence.Data;

namespace Transcendence.IntegrationTests;

/// <summary>
/// Shared fixture: starts ONE Postgres 18 container (matching prod's major) for the whole collection,
/// stands up the <see cref="TestWebAppFactory"/> against it, and applies the REAL migration chain via
/// <c>Database.MigrateAsync()</c> — which is itself a test: it proves the committed migrations apply
/// cleanly to a real Postgres from empty (the unit suite only ever builds the schema via
/// <c>EnsureCreated</c>, so migration SQL was previously unverified).
/// </summary>
public sealed class PostgresIntegrationFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("transcendence")
        .WithUsername("trn_test")
        .WithPassword("trn_test")
        .Build();

    public TestWebAppFactory Factory { get; private set; } = default!;

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        try
        {
            Factory = new TestWebAppFactory(_container.GetConnectionString());

            // Building Factory.Services runs the host's startup (bootstrap admin early-returns with no
            // configured emails, so no DB access) — then we apply migrations to the empty container.
            using var scope = Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TranscendenceContext>();
            await db.Database.MigrateAsync();
        }
        catch
        {
            // A failure here (bad migration, startup fault) must not leak the started container/host —
            // DisposeAsync is not reliably invoked for a collection fixture whose InitializeAsync threw.
            if (Factory is not null)
                await Factory.DisposeAsync();
            await _container.DisposeAsync();
            throw;
        }
    }

    public async Task DisposeAsync()
    {
        if (Factory is not null)
            await Factory.DisposeAsync();
        await _container.DisposeAsync();
    }
}

/// <summary>
/// Binds every integration test class to the single shared container + host (one container per run).
/// </summary>
[CollectionDefinition(Name)]
public sealed class PostgresIntegrationCollection : ICollectionFixture<PostgresIntegrationFixture>
{
    public const string Name = "postgres-integration";
}
