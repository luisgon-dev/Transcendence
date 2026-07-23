using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Account;
using Transcendence.Data.Repositories.Implementations;
using Transcendence.Service.Core.Tests.Support;

namespace Transcendence.Service.Core.Tests;

public class RankRepositoryTests
{
    [Fact]
    public async Task AddOrUpdateRank_DoesNotDuplicateMatchingHistoricalSnapshot()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<TranscendenceContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new SqliteCompatibleTranscendenceContext(options);
        await db.Database.EnsureCreatedAsync();

        var summoner = new Summoner
        {
            Id = Guid.NewGuid(),
            PlatformRegion = "NA1",
            Region = "americas",
            Puuid = $"puuid-{Guid.NewGuid():N}",
            GameName = "RankTester",
            TagLine = "NA1",
            GameNameNormalized = "ranktester",
            TagLineNormalized = "na1",
            SummonerLevel = 100,
            UpdatedAt = DateTime.UtcNow
        };
        var current = new Rank
        {
            Id = Guid.NewGuid(),
            QueueType = "RANKED_SOLO_5x5",
            Tier = "GOLD",
            RankNumber = "II",
            LeaguePoints = 50,
            Wins = 20,
            Losses = 18,
            SummonerId = summoner.Id,
            Summoner = summoner
        };
        var existingSnapshot = new HistoricalRank
        {
            Id = Guid.NewGuid(),
            QueueType = current.QueueType,
            Tier = current.Tier,
            RankNumber = current.RankNumber,
            LeaguePoints = current.LeaguePoints,
            Wins = current.Wins,
            Losses = current.Losses,
            DateRecorded = DateTime.UtcNow.AddMinutes(-5),
            Summoner = summoner
        };
        db.AddRange(summoner, current, existingSnapshot);
        await db.SaveChangesAsync();

        var incoming = new Rank
        {
            QueueType = current.QueueType,
            Tier = "GOLD",
            RankNumber = "I",
            LeaguePoints = 25,
            Wins = 21,
            Losses = 18
        };

        var repository = new RankRepository(db);
        await repository.AddOrUpdateRank(summoner, [incoming]);
        await db.SaveChangesAsync();

        (await db.HistoricalRanks.CountAsync()).Should().Be(1);
        current.RankNumber.Should().Be("I");
        current.LeaguePoints.Should().Be(25);
    }
}
