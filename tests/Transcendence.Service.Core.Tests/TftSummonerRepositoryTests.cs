using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Transcendence.Data;
using Transcendence.Data.Models.Tft.Account;
using Transcendence.Data.Models.Tft.Match;
using Transcendence.Data.Models.Tft.Static;
using Transcendence.Data.Repositories.Implementations;
using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Core.Tests.Support;

namespace Transcendence.Service.Core.Tests;

public class TftSummonerRepositoryTests
{
    [Fact]
    public async Task SearchByPrefixAsync_PrioritizesExactTaglineMatch()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<TranscendenceContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new SqliteCompatibleTranscendenceContext(options);
        await db.Database.EnsureCreatedAsync();

        db.TftSets.Add(new TftSet
        {
            Number = 13,
            Name = "Cyber City",
            IsActive = true
        });
        SeedSummoner(db, "NA1", "Name", "KR10", "TFT_1");
        SeedSummoner(db, "NA1", "Name", "KR1", "TFT_2");
        await db.SaveChangesAsync();

        var repository = new TftSummonerRepository(db, Mock.Of<ITftRankRepository>());

        var result = await repository.SearchByPrefixAsync("na1", "name", "kr1", 10, CancellationToken.None);

        result.Should().HaveCount(2);
        result[0].TagLine.Should().Be("KR1");
    }

    [Fact]
    public async Task AddOrUpdateAsync_MatchesExistingSummonerByNormalizedRiotId()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<TranscendenceContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new SqliteCompatibleTranscendenceContext(options);
        await db.Database.EnsureCreatedAsync();

        var existing = new TftSummoner
        {
            Id = Guid.NewGuid(),
            Puuid = "original-puuid",
            GameName = "Soju",
            TagLine = "Milk",
            GameNameNormalized = "SOJU",
            TagLineNormalized = "MILK",
            PlatformRegion = "NA1",
            Region = "AMERICAS",
            ProfileIconId = 1,
            SummonerLevel = 10
        };

        db.TftSummoners.Add(existing);
        await db.SaveChangesAsync();

        var repository = new TftSummonerRepository(db, Mock.Of<ITftRankRepository>());
        var incoming = new TftSummoner
        {
            Puuid = "updated-puuid",
            GameName = " soju ",
            TagLine = " milk ",
            PlatformRegion = "na1",
            Region = "AMERICAS",
            ProfileIconId = 2,
            SummonerLevel = 20
        };

        var persisted = await repository.AddOrUpdateAsync(incoming, CancellationToken.None);

        persisted.Id.Should().Be(existing.Id);
        persisted.Puuid.Should().Be("updated-puuid");
        persisted.GameName.Should().Be("soju");
        persisted.TagLine.Should().Be("milk");
        persisted.PlatformRegion.Should().Be("NA1");
        db.TftSummoners.Should().HaveCount(1);
    }

    private static void SeedSummoner(TranscendenceContext db, string platformRegion, string gameName, string tagLine, string matchId)
    {
        var summoner = new TftSummoner
        {
            Id = Guid.NewGuid(),
            Puuid = $"{platformRegion}-{gameName}-{tagLine}",
            GameName = gameName,
            TagLine = tagLine,
            GameNameNormalized = gameName.ToUpperInvariant(),
            TagLineNormalized = tagLine.ToUpperInvariant(),
            PlatformRegion = platformRegion,
            Region = platformRegion
        };
        var match = new TftMatch
        {
            Id = Guid.NewGuid(),
            MatchId = matchId,
            MatchDate = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            DurationSeconds = 1800,
            Patch = "14.5",
            SetNumber = 13,
            SetCoreName = "Cyber City",
            PlatformRegion = platformRegion,
            Status = TftFetchStatus.Success,
            FetchedAtUtc = DateTime.UtcNow
        };
        var participant = new TftMatchParticipant
        {
            Id = Guid.NewGuid(),
            Match = match,
            MatchId = match.Id,
            Summoner = summoner,
            SummonerId = summoner.Id,
            Puuid = summoner.Puuid!,
            Placement = 1,
            Level = 9,
            LastRound = 6,
            PlayersEliminated = 5,
            GoldLeft = 10,
            TotalDamageToPlayers = 100,
            TimeEliminatedSeconds = 1500
        };

        db.TftSummoners.Add(summoner);
        db.TftMatches.Add(match);
        db.TftMatchParticipants.Add(participant);
    }
}
