using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Data.Models.LoL.Static;
using Transcendence.Service.Core.Services.Analytics.Implementations;
using Transcendence.Service.Core.Services.Jobs.Configuration;
using Transcendence.Service.Core.Tests.Support;
using MatchEntity = Transcendence.Data.Models.LoL.Match.Match;

namespace Transcendence.Service.Core.Tests;

/// <summary>
/// The public dataset stats: the worker's refresher computes every match figure from one grouped pass
/// and stores the result as a single snapshot row; the read service serves that row and never computes.
/// </summary>
public class DatasetStatsTests
{
    // 14:00 UTC, so "today" (excluded from the 7-day average) has already started.
    private static readonly DateTime AsOf = new(2026, 10, 6, 14, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Compute_CountsStoredMatchesAndWindows_FromSuccessfulMatchesOnly()
    {
        await using var ctx = await CreateAsync();
        Seed(ctx.Db);
        await ctx.Db.SaveChangesAsync();

        var stats = await Refresher(ctx.Db).ComputeAsync(AsOf, CancellationToken.None);

        // 6 successful matches; the temporary failure is not a stored match.
        stats.MatchesStored.Should().Be(6);
        // Fetched within 24h of AsOf: 2h ago (NA1), 20h ago (KR). 30h ago is outside.
        stats.MatchesLast24Hours.Should().Be(2);
        // Last 7 complete UTC days (Sep 29 .. Oct 5): 20h ago (Oct 5 18:00), 30h ago (Oct 5 08:00),
        // 3 days ago and 6.5 days ago. Today's (2h ago) and the 10-day-old match are outside. 4 / 7 → 1.
        stats.MatchesPerDayLast7Days.Should().Be(1);
        stats.ActivePatch.Should().Be("16.19");
        stats.ActivePatchMatches.Should().Be(4);
        stats.LastMatchIngestedAtUtc.Should().Be(AsOf.AddHours(-2));
        stats.ComputedAtUtc.Should().Be(AsOf);

        stats.Platforms.Select(p => (p.Platform, p.MatchesStored, p.MatchesLast24Hours))
            .Should().Equal(("NA1", 3L, 1L), ("KR", 2L, 1L), ("EUW1", 1L, 0L));
        stats.Platforms[0].Label.Should().Be("North America");

        // Configured crawl set, enabled only, normalized and de-duplicated.
        stats.CrawledPlatforms.Should().Equal("NA1", "KR", "EUW1");

        // Catalog-only figures are PostgreSQL-specific; SQLite reports them as unknown, never as 0.
        stats.PlayersIndexedEstimate.Should().BeNull();
        stats.DatabaseSizeBytes.Should().BeNull();
    }

    [Fact]
    public async Task Compute_RoundsTheSevenDayAverageToWholeMatches()
    {
        await using var ctx = await CreateAsync();
        for (var i = 0; i < 11; i++)
            AddMatch(ctx.Db, $"NA1_{i}", "NA1", AsOf.Date.AddDays(-1).AddMinutes(i), "16.19");
        await ctx.Db.SaveChangesAsync();

        var stats = await Refresher(ctx.Db).ComputeAsync(AsOf, CancellationToken.None);

        // 11 / 7 = 1.57 → 2. No active patch is set, so no match counts as on it.
        stats.MatchesPerDayLast7Days.Should().Be(2);
        stats.ActivePatch.Should().BeNull();
        stats.ActivePatchMatches.Should().Be(0);
    }

    [Fact]
    public async Task Compute_EmptyDatabase_ReturnsZeros()
    {
        await using var ctx = await CreateAsync();

        var stats = await Refresher(ctx.Db).ComputeAsync(AsOf, CancellationToken.None);

        stats.MatchesStored.Should().Be(0);
        stats.MatchesLast24Hours.Should().Be(0);
        stats.MatchesPerDayLast7Days.Should().Be(0);
        stats.Platforms.Should().BeEmpty();
        stats.LastMatchIngestedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task Refresh_StoresOneSnapshotRow_AndTheReadServiceServesIt()
    {
        await using var ctx = await CreateAsync();
        Seed(ctx.Db);
        await ctx.Db.SaveChangesAsync();
        var refresher = Refresher(ctx.Db);

        (await Service(ctx.Db).GetAsync()).Should().BeNull("nothing has been computed yet");

        await refresher.RefreshAsync(AsOf, CancellationToken.None);
        AddMatch(ctx.Db, "NA1_late", "NA1", AsOf.AddMinutes(5), "16.19");
        await ctx.Db.SaveChangesAsync();
        var latest = await refresher.RefreshAsync(AsOf.AddMinutes(10), CancellationToken.None);

        // The second refresh updates the row in place instead of adding another.
        var rows = await ctx.Db.AnalyticsResponseSnapshots
            .Where(x => x.Feature == AnalyticsSnapshotSerialization.DatasetStatsFeature)
            .ToListAsync();
        rows.Should().ContainSingle();
        rows[0].Patch.Should().Be(AnalyticsSnapshotSerialization.PatchIndependent);

        // A fresh service (cold cache) reads exactly what the refresher computed.
        var served = await Service(ctx.Db).GetAsync();
        served.Should().BeEquivalentTo(latest);
        served!.MatchesStored.Should().Be(7);
    }

    [Fact]
    public async Task Refresh_LeavesTheRowAlone_WhenAPatchsProSnapshotsAreReplaced()
    {
        await using var ctx = await CreateAsync();
        Seed(ctx.Db);
        await ctx.Db.SaveChangesAsync();
        await Refresher(ctx.Db).RefreshAsync(AsOf, CancellationToken.None);

        // RefreshProSurfacesAsync clears a patch's rows before rewriting them.
        await ctx.Db.AnalyticsResponseSnapshots.Where(x => x.Patch == "16.19").ExecuteDeleteAsync();

        (await Service(ctx.Db).GetAsync()).Should().NotBeNull();
    }

    private static DatasetStatsRefresher Refresher(TranscendenceContext db) =>
        new(db, Options.Create(new MultiRegionIngestionOptions
        {
            Regions =
            [
                new RegionConfig { Region = "NA1" },
                new RegionConfig { Region = "kr" },
                new RegionConfig { Region = "EUW1" },
                new RegionConfig { Region = "NA1" },
                new RegionConfig { Region = "OC1", Enabled = false }
            ]
        }));

    private static DatasetStatsService Service(TranscendenceContext db)
    {
        var services = new ServiceCollection();
        services.AddHybridCache();
        var cache = services.BuildServiceProvider().GetRequiredService<HybridCache>();
        return new DatasetStatsService(db, cache);
    }

    private static void Seed(TranscendenceContext db)
    {
        db.Patches.Add(new Patch { Version = "16.19", IsActive = true, DetectedAt = AsOf.AddDays(-13) });
        db.Patches.Add(new Patch { Version = "16.18", IsActive = false, DetectedAt = AsOf.AddDays(-27) });

        AddMatch(db, "NA1_1", "NA1", AsOf.AddHours(-2), "16.19");
        AddMatch(db, "NA1_2", "NA1", AsOf.AddDays(-3), "16.19");
        AddMatch(db, "NA1_3", "NA1", AsOf.AddDays(-10), "16.18");
        AddMatch(db, "KR_1", "KR", AsOf.AddHours(-20), "16.19");
        AddMatch(db, "KR_2", "KR", AsOf.AddHours(-30), "16.19");
        AddMatch(db, "EUW1_1", "EUW1", AsOf.AddDays(-6.5), "16.18");
        AddMatch(db, "EUW1_failed", "EUW1", AsOf.AddHours(-1), "16.19", FetchStatus.TemporaryFailure);
    }

    private static void AddMatch(
        TranscendenceContext db,
        string matchId,
        string platform,
        DateTime fetchedAt,
        string patch,
        FetchStatus status = FetchStatus.Success) =>
        db.Matches.Add(new MatchEntity
        {
            Id = Guid.NewGuid(),
            MatchId = matchId,
            MatchDate = new DateTimeOffset(fetchedAt).ToUnixTimeMilliseconds(),
            Duration = 1800,
            Patch = patch,
            QueueId = 420,
            QueueFamily = "RANKED_SOLO_DUO",
            QueueType = "420",
            PlatformRegion = platform,
            Status = status,
            FetchedAt = fetchedAt
        });

    private static async Task<SqliteContext> CreateAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TranscendenceContext>().UseSqlite(connection).Options;
        var db = new SqliteCompatibleTranscendenceContext(options);
        await db.Database.EnsureCreatedAsync();
        return new SqliteContext(connection, db);
    }

    private sealed class SqliteContext(SqliteConnection connection, TranscendenceContext db) : IAsyncDisposable
    {
        public TranscendenceContext Db { get; } = db;

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
