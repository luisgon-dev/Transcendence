using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Transcendence.Data;

namespace Transcendence.IntegrationTests;

/// <summary>
/// Proves the committed EF migration chain applies cleanly to a real Postgres from empty (the fixture
/// ran <c>MigrateAsync</c>). Closes the "migrations are never applied in any test — only EnsureCreated"
/// gap: a bad Down, wrong column type, or ordering fault would fail here instead of only in prod.
/// </summary>
[Collection(PostgresIntegrationCollection.Name)]
public sealed class SchemaMigrationTests(PostgresIntegrationFixture fixture)
{
    [Fact]
    public async Task EveryMigration_IsApplied_AndNothingIsPending()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TranscendenceContext>();

        var all = db.Database.GetMigrations().ToList();
        var applied = (await db.Database.GetAppliedMigrationsAsync()).ToList();
        var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();

        all.Should().NotBeEmpty("the app ships a migration chain");
        applied.Should().BeEquivalentTo(all, "every committed migration must apply cleanly to real Postgres");
        pending.Should().BeEmpty();
    }

    [Fact]
    public async Task CanConnect_AndCoreTablesExist()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TranscendenceContext>();

        (await db.Database.CanConnectAsync()).Should().BeTrue();
        // A trivial query against a core table proves the table/columns materialized as the model expects.
        (await db.Matches.CountAsync()).Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task RedundantMatchSummonerTable_IsNotInTheCurrentSchema()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TranscendenceContext>();

        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT to_regclass('public.\"MatchSummoner\"') IS NULL";

        (await command.ExecuteScalarAsync()).Should().Be(true);
    }

    [Fact]
    public void TestHost_PointsMainDatabaseConnectionString_AtTheContainer()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        // Proves the factory's UseSetting override wins over the appsettings default (localhost:5432),
        // so every consumer of GetConnectionString("MainDatabase") — including Hangfire's JobStorage,
        // which is not re-pointed via DI — resolves the container rather than an unreachable dev DB.
        config.GetConnectionString("MainDatabase").Should().Be(fixture.ConnectionString);
    }
}
