using System.Text.Json;
using System.IO.Compression;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Account;
using Transcendence.Data.Models.LoL.Analytics;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Data.Models.LoL.Static;
using Transcendence.Data.Models.Service;
using Transcendence.Service.Core.Services.Operations;

namespace Transcendence.IntegrationTests;

[Collection(PostgresIntegrationCollection.Name)]
public sealed class MatchArchivePostgresTests(PostgresIntegrationFixture fixture) : IAsyncLifetime
{
    private readonly string name = "archive_" + Guid.NewGuid().ToString("N");
    private readonly string root = Path.Combine(Path.GetTempPath(), "archive-" + Guid.NewGuid().ToString("N"));
    private TranscendenceContext db = null!;
    private MatchArchiveOptions settings = null!;
    private MatchArchiveTelemetry telemetry = null!;
    private MatchArchiveRetention retention = null!;

    public async Task InitializeAsync()
    {
        await using var admin = new NpgsqlConnection(fixture.ConnectionString);
        await admin.OpenAsync();
        await using (var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", admin)) await command.ExecuteNonQueryAsync();
        var connection = new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { Database = name, Pooling = false };
        db = new TranscendenceContext(new DbContextOptionsBuilder<TranscendenceContext>()
            .UseNpgsql(connection.ConnectionString, pg => pg.MigrationsAssembly("Transcendence.Service")).Options);
        await db.Database.MigrateAsync();
        await db.Patches.ExecuteUpdateAsync(set => set.SetProperty(x => x.IsActive, false));
        db.Patches.AddRange(
            new Patch { Version = "archive-old", IsActive = false, ReleaseDate = DateTime.UtcNow.AddDays(-90) },
            new Patch { Version = "archive-current", IsActive = true, ReleaseDate = DateTime.UtcNow });
        await db.SaveChangesAsync();
        Directory.CreateDirectory(root);
        var maintenance = Path.Combine(root, "maintenance");
        Directory.CreateDirectory(maintenance);
        await File.WriteAllTextAsync(Path.Combine(maintenance, "run.lock"), "");
        var pressure = Path.Combine(root, "pressure");
        await File.WriteAllTextAsync(pressure, "full avg60=0.00\n");
        settings = new MatchArchiveOptions
        {
            Enabled = true, Directory = Path.Combine(root, "nas"), MaintenanceDirectory = maintenance,
            KeepPatches = 1, BatchDelayMilliseconds = 0, IoPressureFile = pressure,
            MaxWalMegabytesPerRun = 4096, CommandTimeoutSeconds = 120
        };
        Directory.CreateDirectory(settings.Directory);
        telemetry = new MatchArchiveTelemetry();
        retention = new MatchArchiveRetention(db, Options.Create(settings), new TestDestination(), telemetry, NullLogger<MatchArchiveRetention>.Instance);
    }

    private sealed class TestDestination : IMatchArchiveDestination
    {
        public void Verify(string directory) => Directory.Exists(directory).Should().BeTrue();
    }

    private async Task<Match> SeedAsync(bool facts)
    {
        var match = new Match { Id = Guid.NewGuid(), MatchId = "NA1_" + Guid.NewGuid().ToString("N"),
            Patch = "archive-old", Status = FetchStatus.Success, QueueId = 420, QueueType = "420" };
        var summoner = new Summoner { Id = Guid.NewGuid(), Puuid = Guid.NewGuid().ToString(), PlatformRegion = "NA1", Region = "AMERICAS" };
        db.Matches.Add(match);
        db.Summoners.Add(summoner);
        db.MatchParticipants.Add(new MatchParticipant { Id = Guid.NewGuid(), MatchId = match.Id,
            Match = match, Summoner = summoner, SummonerId = summoner.Id, ParticipantId = 1, ChampionId = 266, TeamPosition = "line one\nline two with \"quoted\" data" });
        db.SummonerMatchFacts.Add(new SummonerMatchFact { Id = Guid.NewGuid(), SummonerId = summoner.Id,
            MatchId = match.MatchId, Puuid = summoner.Puuid!, SeasonKey = "2026", Patch = match.Patch });
        if (facts) db.ChampionSynergySourceMatches.Add(new ChampionSynergySourceMatch
            { MatchId = match.Id, Patch = match.Patch!, MaterializedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();
        return match;
    }

    [Fact]
    public async Task Streams_complete_cascade_then_prunes_and_preserves_compact_facts()
    {
        var match = await SeedAsync(facts: true);
        await retention.RunAsync(CancellationToken.None);
        var chunk = await db.MatchArchiveChunks.SingleAsync();
        chunk.CompletedAtUtc.Should().NotBeNull();
        chunk.PrunedMatches.Should().Be(1);
        (await db.Matches.IgnoreQueryFilters().AnyAsync(x => x.Id == match.Id)).Should().BeFalse();
        (await db.MatchParticipants.IgnoreQueryFilters().AnyAsync(x => x.MatchId == match.Id)).Should().BeFalse();
        (await db.ChampionSynergySourceMatches.AnyAsync(x => x.MatchId == match.Id)).Should().BeTrue();
        (await db.SummonerMatchFacts.AnyAsync(x => x.MatchId == match.MatchId)).Should().BeTrue();
        (await db.MatchArchiveEntries.CountAsync()).Should().Be(0);
        var directory = Path.Combine(settings.Directory, chunk.RelativePath);
        File.Exists(Path.Combine(directory, "_DONE")).Should().BeTrue();
        var manifest = JsonSerializer.Deserialize<ArchiveManifest>(chunk.ManifestJson, MatchArchiveFiles.Json)!;
        manifest.Rows.Should().HaveCount(12);
        manifest.Rows["Matches"].Count.Should().Be(1);
        foreach (var file in manifest.Rows)
            await MatchArchiveFiles.VerifyAsync(Path.Combine(directory, file.Key + ".csv.gz"), file.Value, CancellationToken.None);
        await retention.RunAsync(CancellationToken.None);
        (await db.MatchArchiveChunks.CountAsync()).Should().Be(1);

        // Prove the cold export can reconstruct deleted rows, including quoted newlines and CSV quoting.
        // Parent-first catalog order and explicit header columns also tolerate physical column reordering.
        await db.Database.OpenConnectionAsync();
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        foreach (var table in MatchArchiveCatalog.Build(db.Model, chunk.Id))
        {
            await using var file = File.OpenRead(Path.Combine(directory, table.Name + ".csv.gz"));
            await using var gzip = new GZipStream(file, CompressionMode.Decompress);
            using var reader = new StreamReader(gzip);
            var header = (await reader.ReadLineAsync())!;
            var columns = string.Join(",", header.Split(',').Select(MatchArchiveCatalog.Quote));
            await using var writer = await connection.BeginTextImportAsync(
                $"COPY {MatchArchiveCatalog.Quote(table.Name)} ({columns}) FROM STDIN WITH (FORMAT CSV)");
            await writer.WriteAsync(await reader.ReadToEndAsync());
        }
        await db.Database.CloseConnectionAsync();
        (await db.Matches.IgnoreQueryFilters().AnyAsync(x => x.Id == match.Id)).Should().BeTrue();
        (await db.MatchParticipants.IgnoreQueryFilters().SingleAsync(x => x.MatchId == match.Id)).TeamPosition
            .Should().Be("line one\nline two with \"quoted\" data");
        (await db.SummonerMatchFacts.CountAsync(x => x.MatchId == match.MatchId)).Should().Be(1);
    }

    [Fact]
    public async Task Corrupt_verified_export_never_allows_prune_or_overwrites_original()
    {
        var match = await SeedAsync(facts: false);
        await retention.RunAsync(CancellationToken.None);
        var chunk = await db.MatchArchiveChunks.SingleAsync();
        chunk.VerifiedAtUtc.Should().NotBeNull();
        chunk.CompletedAtUtc.Should().BeNull();
        var file = Path.Combine(settings.Directory, chunk.RelativePath, "Matches.csv.gz");
        await File.AppendAllTextAsync(file, "corrupt");
        db.ChampionSynergySourceMatches.Add(new ChampionSynergySourceMatch
            { MatchId = match.Id, Patch = match.Patch!, MaterializedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();
        Func<Task> run = () => retention.RunAsync(CancellationToken.None);
        await run.Should().ThrowAsync<InvalidDataException>();
        (await db.Matches.IgnoreQueryFilters().AnyAsync(x => x.Id == match.Id)).Should().BeTrue();
        (await db.MatchArchiveEntries.CountAsync()).Should().Be(1);
        chunk.PrunedMatches.Should().Be(0);
    }

    [Fact]
    public async Task Promoted_patch_is_protected_when_verified_chunk_resumes()
    {
        await SeedAsync(facts: false);
        await retention.RunAsync(CancellationToken.None);
        await db.Patches.ExecuteUpdateAsync(set => set.SetProperty(x => x.IsActive, false));
        await db.Patches.Where(x => x.Version == "archive-old").ExecuteUpdateAsync(set => set.SetProperty(x => x.IsActive, true));
        await retention.RunAsync(CancellationToken.None);
        (await db.Matches.IgnoreQueryFilters().CountAsync()).Should().Be(1);
        (await db.MatchArchiveChunks.SingleAsync()).PrunedMatches.Should().Be(0);
    }

    [Fact]
    public async Task Adopts_legacy_frozen_ids_and_resumes_without_discarding_progress()
    {
        var match = await SeedAsync(facts: true);
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE _patch_archive_pending (\"Id\" uuid PRIMARY KEY)");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO _patch_archive_pending (\"Id\") VALUES ({match.Id})");
        var relative = "archive-old/chunk-legacy";
        var directory = Path.Combine(settings.Directory, relative);
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "Matches.csv.gz"), "incomplete old export");
        var state = Path.Combine(settings.MaintenanceDirectory, "current");
        var originalState = $"archive-old\n/mnt/user/backup/transcendence-match-archive/{relative}\n1\n0\n";
        await File.WriteAllTextAsync(state, originalState);
        await retention.AdoptLegacyAsync("/mnt/user/backup/transcendence-match-archive", CancellationToken.None);
        await retention.AdoptLegacyAsync("/mnt/user/backup/transcendence-match-archive", CancellationToken.None);
        await File.WriteAllTextAsync(state, originalState); // Replayed input after DB commit/receipt interruption.
        await retention.AdoptLegacyAsync("/mnt/user/backup/transcendence-match-archive", CancellationToken.None);
        var chunk = await db.MatchArchiveChunks.SingleAsync();
        chunk.LegacySource.Should().BeTrue();
        chunk.RelativePath.Should().Be(relative);
        (await db.MatchArchiveEntries.SingleAsync()).MatchId.Should().Be(match.Id);
        (await File.ReadAllTextAsync(Path.Combine(directory, "Matches.csv.gz"))).Should().Be("incomplete old export");
        await retention.RunAsync(CancellationToken.None);
        chunk.CompletedAtUtc.Should().NotBeNull();
        (await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM _patch_archive_pending").SingleAsync()).Should().Be(0);
        await retention.RunAsync(CancellationToken.None);
        (await db.MatchArchiveChunks.CountAsync()).Should().Be(1);
    }

    public async Task DisposeAsync()
    {
        telemetry?.Dispose();
        if (db is not null) await db.DisposeAsync();
        await using var admin = new NpgsqlConnection(fixture.ConnectionString);
        await admin.OpenAsync();
        await using (var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)", admin)) await command.ExecuteNonQueryAsync();
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
