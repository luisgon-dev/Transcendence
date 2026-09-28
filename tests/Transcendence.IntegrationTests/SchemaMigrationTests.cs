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
    public async Task MatchTables_VacuumOnInsertOften_SoCurrentPatchReadsStayIndexOnly()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TranscendenceContext>();
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT relname, array_to_string(reloptions, ',') FROM pg_class
            WHERE relname IN ('MatchParticipants', 'Matches') ORDER BY relname
            """;
        var options = new Dictionary<string, string>();
        await using (var reader = await command.ExecuteReaderAsync())
            while (await reader.ReadAsync())
                options[reader.GetString(0)] = reader.GetString(1);

        options["MatchParticipants"].Should().Contain("autovacuum_vacuum_insert_scale_factor=0.005")
            .And.Contain("autovacuum_vacuum_insert_threshold=10000");
        options["Matches"].Should().Contain("autovacuum_vacuum_insert_scale_factor=0.01")
            .And.Contain("autovacuum_vacuum_scale_factor=0.01");
    }

    [Fact]
    public async Task ItemAndRuneTables_PinParticipantCardinality_SoBatchReadsUseThePrimaryKey()
    {
        // ANALYZE underestimated distinct participants >10x on these clustered tables, which turned
        // every Build Atlas batch into a full scan of the 14 GB rune table. The pin is the fix.
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TranscendenceContext>();
        var pins = await db.Database.SqlQueryRaw<string>("""
            SELECT c.relname || '.' || array_to_string(a.attoptions, ',') AS "Value"
            FROM pg_attribute a JOIN pg_class c ON c.oid = a.attrelid
            WHERE c.relname IN ('MatchParticipantRunes', 'MatchParticipantItems') AND a.attname = 'MatchParticipantId'
            """).ToListAsync();

        pins.Should().BeEquivalentTo(
            "MatchParticipantRunes.n_distinct=-0.117",
            "MatchParticipantItems.n_distinct=-0.163");
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
