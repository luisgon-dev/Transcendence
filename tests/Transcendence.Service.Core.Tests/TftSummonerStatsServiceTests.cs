using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Transcendence.Data;
using Transcendence.Data.Models.Tft.Account;
using Transcendence.Service.Core.Services.Tft.Implementations;
using Transcendence.Service.Core.Tests.Support;

namespace Transcendence.Service.Core.Tests;

public class TftSummonerStatsServiceTests
{
    [Fact]
    public async Task GetSummonerOverviewAsync_NoMatches_ReturnsEmptyStats()
    {
        await using var harness = await Harness.CreateAsync();
        var summoner = harness.CreateSummoner("TestPlayer", "NA1");
        await harness.Db.SaveChangesAsync();

        var result = await harness.Service.GetSummonerOverviewAsync(summoner.Id, 20, CancellationToken.None);

        result.TotalMatches.Should().Be(0);
        result.AveragePlacement.Should().Be(0);
        result.Top4Rate.Should().Be(0);
        result.WinRate.Should().Be(0);
        result.RecentPerformance.Should().BeEmpty();
    }

    [Fact]
    public async Task GetChampionStatsAsync_NoUnits_ReturnsEmptyList()
    {
        await using var harness = await Harness.CreateAsync();
        var summoner = harness.CreateSummoner("TestPlayer", "NA1");
        await harness.Db.SaveChangesAsync();

        var result = await harness.Service.GetChampionStatsAsync(summoner.Id, 10, CancellationToken.None);

        result.Should().BeEmpty();
    }

    private sealed class Harness : IAsyncDisposable
    {
        public SqliteConnection Connection { get; }
        public SqliteCompatibleTranscendenceContext Db { get; }
        public TftSummonerStatsService Service { get; }

        private Harness(SqliteConnection connection, SqliteCompatibleTranscendenceContext db, TftSummonerStatsService service)
        {
            Connection = connection;
            Db = db;
            Service = service;
        }

        public static async Task<Harness> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<TranscendenceContext>()
                .UseSqlite(connection)
                .Options;
            var db = new SqliteCompatibleTranscendenceContext(options);
            await db.Database.EnsureCreatedAsync();

            return new Harness(connection, db, new TftSummonerStatsService(db));
        }

        public TftSummoner CreateSummoner(string gameName, string tagLine)
        {
            var uniqueId = Guid.NewGuid().ToString()[..8];
            var summoner = new TftSummoner
            {
                Id = Guid.NewGuid(),
                Puuid = $"PUUID-{gameName}-{tagLine}-{uniqueId}",
                GameName = gameName,
                TagLine = tagLine,
                GameNameNormalized = gameName.ToUpperInvariant(),
                TagLineNormalized = tagLine.ToUpperInvariant(),
                PlatformRegion = "NA1",
                Region = "NA",
                SummonerLevel = 100,
                ProfileIconId = 1,
                UpdatedAt = DateTime.UtcNow
            };
            Db.TftSummoners.Add(summoner);
            return summoner;
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }
}
