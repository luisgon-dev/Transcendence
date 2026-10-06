using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Service.Core.Services.Analytics.Implementations;
using Transcendence.Service.Core.Services.Analytics.Models;
using Transcendence.Service.Core.Services.Jobs.Configuration;

namespace Transcendence.IntegrationTests;

/// <summary>
/// The dataset-stats refresher on real PostgreSQL: the grouped <c>count(*) FILTER</c> pass, the
/// <c>pg_class.reltuples</c> estimate and <c>pg_database_size</c> (none of which SQLite can run), then the
/// public endpoint serving the stored snapshot through the real host.
/// </summary>
[Collection(PostgresIntegrationCollection.Name)]
public sealed class DatasetStatsPostgresTests(PostgresIntegrationFixture fixture)
{
    [Fact]
    public async Task Refresh_ComputesOnPostgres_AndTheEndpointServesTheSnapshot()
    {
        // The collection shares one database, so scope the assertions to a platform no other test uses.
        var platform = $"D{Guid.NewGuid():N}"[..8].ToUpperInvariant();
        var now = DateTime.UtcNow;
        await using var db = NewDb();
        AddMatch(db, platform, now.AddHours(-1), FetchStatus.Success);
        AddMatch(db, platform, now.AddDays(-2), FetchStatus.Success);
        AddMatch(db, platform, now.AddMinutes(-5), FetchStatus.TemporaryFailure);
        await db.SaveChangesAsync();
        // A never-analyzed table has reltuples = -1, which the refresher reports as unknown.
        await db.Database.ExecuteSqlRawAsync("ANALYZE \"Summoners\"");

        var refresher = new DatasetStatsRefresher(db, Options.Create(new MultiRegionIngestionOptions
        {
            Regions = [new RegionConfig { Region = platform }]
        }));
        var stats = await refresher.RefreshAsync(now);

        var row = stats.Platforms.Should().ContainSingle(p => p.Platform == platform).Subject;
        row.MatchesStored.Should().Be(2);
        row.MatchesLast24Hours.Should().Be(1);
        stats.MatchesStored.Should().BeGreaterThanOrEqualTo(2);
        stats.CrawledPlatforms.Should().Equal(platform);
        stats.PlayersIndexedEstimate.Should().NotBeNull().And.BeGreaterThanOrEqualTo(0);
        stats.DatabaseSizeBytes.Should().BeGreaterThan(0);
        stats.LastMatchIngestedAtUtc.Should().NotBeNull();
        stats.LastMatchIngestedAtUtc!.Value.Kind.Should().Be(DateTimeKind.Utc);

        var client = fixture.Factory.CreateClient();
        var response = await client.GetAsync("/api/lol/analytics/dataset");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var served = await response.Content.ReadFromJsonAsync<DatasetStatsDto>();
        served.Should().BeEquivalentTo(stats);
    }

    private TranscendenceContext NewDb() =>
        new(new DbContextOptionsBuilder<TranscendenceContext>()
            .UseNpgsql(fixture.ConnectionString)
            .Options);

    private static void AddMatch(TranscendenceContext db, string platform, DateTime fetchedAt, FetchStatus status) =>
        db.Matches.Add(new Match
        {
            Id = Guid.NewGuid(),
            MatchId = $"{platform}_{Guid.NewGuid():N}",
            MatchDate = new DateTimeOffset(fetchedAt).ToUnixTimeMilliseconds(),
            Duration = 1800,
            Patch = "16.19",
            QueueId = 420,
            QueueFamily = "RANKED_SOLO_DUO",
            QueueType = "420",
            PlatformRegion = platform,
            Status = status,
            FetchedAt = fetchedAt
        });
}
