using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Account;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Data.Repositories.Implementations;
using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Core.Tests.Support;

namespace Transcendence.Service.Core.Tests;

using LolMatch = Transcendence.Data.Models.LoL.Match.Match;

public class IdentifierLookupSafetyTests
{
    [Fact]
    public async Task SummonerRepository_FindByRiotIdAsync_DoesNotRequireEncryptedIdentifiers()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<TranscendenceContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new SqliteCompatibleTranscendenceContext(options);
        await db.Database.EnsureCreatedAsync();

        db.Summoners.Add(new Summoner
        {
            Id = Guid.NewGuid(),
            Puuid = "na1-soju-milk",
            RiotSummonerId = "stale-summoner-id",
            AccountId = "stale-account-id",
            GameName = "Soju",
            TagLine = "Milk",
            GameNameNormalized = "SOJU",
            TagLineNormalized = "MILK",
            PlatformRegion = "NA1",
            Region = "AMERICAS"
        });
        await db.SaveChangesAsync();

        var repository = new SummonerRepository(db, Mock.Of<IRankRepository>());

        var result = await repository.FindByRiotIdAsync("NA1", "soju", "milk", cancellationToken: CancellationToken.None);

        result.Should().NotBeNull();
        result!.Puuid.Should().Be("na1-soju-milk");
    }

    [Fact]
    public async Task SummonerRepository_FindByRiotIdAsync_SupportsCollectionIncludes()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<TranscendenceContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new SqliteCompatibleTranscendenceContext(options);
        await db.Database.EnsureCreatedAsync();

        var summonerId = Guid.NewGuid();
        var summoner = new Summoner
        {
            Id = summonerId,
            Puuid = "duplicate-newer",
            GameName = "Soju",
            TagLine = "Milk",
            GameNameNormalized = "SOJU",
            TagLineNormalized = "MILK",
            PlatformRegion = "NA1",
            Region = "AMERICAS",
            UpdatedAt = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
            Ranks =
            [
                new Rank
                {
                    SummonerId = summonerId,
                    QueueType = "RANKED_SOLO_5x5",
                    Tier = "CHALLENGER",
                    RankNumber = "I",
                    LeaguePoints = 1000,
                    Wins = 10,
                    Losses = 2
                }
            ],
            HistoricalRanks =
            [
                new HistoricalRank
                {
                    QueueType = "RANKED_SOLO_5x5",
                    Tier = "GRANDMASTER",
                    RankNumber = "I",
                    LeaguePoints = 800,
                    Wins = 20,
                    Losses = 10,
                    DateRecorded = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc)
                }
            ]
        };

        foreach (var rank in summoner.Ranks)
            rank.Summoner = summoner;
        foreach (var historicalRank in summoner.HistoricalRanks)
            historicalRank.Summoner = summoner;

        db.Summoners.Add(summoner);
        await db.SaveChangesAsync();

        var repository = new SummonerRepository(db, Mock.Of<IRankRepository>());

        var result = await repository.FindByRiotIdAsync(
            "NA1",
            "soju",
            "milk",
            q => q.Include(x => x.Ranks).Include(x => x.HistoricalRanks),
            CancellationToken.None);

        result.Should().NotBeNull();
        result!.Puuid.Should().Be("duplicate-newer");
        result.Ranks.Should().ContainSingle();
        result.HistoricalRanks.Should().ContainSingle();
    }

    [Fact]
    public async Task SummonerRepository_SearchByPrefixAsync_DoesNotRequireEncryptedIdentifiers()
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
            Puuid = "na1-soju-milk",
            RiotSummonerId = "stale-summoner-id",
            AccountId = "stale-account-id",
            GameName = "Soju",
            TagLine = "Milk",
            GameNameNormalized = "SOJU",
            TagLineNormalized = "MILK",
            PlatformRegion = "NA1",
            Region = "AMERICAS"
        };
        var match = new LolMatch
        {
            Id = Guid.NewGuid(),
            MatchId = "NA1_1",
            MatchDate = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Duration = 1800,
            Patch = "15.1",
            QueueId = 420,
            QueueFamily = "RANKED",
            QueueType = "RANKED_SOLO_5x5",
            PlatformRegion = "NA1",
            Status = FetchStatus.Success
        };
        var participant = new MatchParticipant
        {
            Id = Guid.NewGuid(),
            Match = match,
            MatchId = match.Id,
            Summoner = summoner,
            SummonerId = summoner.Id,
            Puuid = summoner.Puuid,
            ParticipantId = 1,
            TeamId = 100,
            ChampionId = 1
        };

        db.Summoners.Add(summoner);
        db.Matches.Add(match);
        db.MatchParticipants.Add(participant);
        await db.SaveChangesAsync();

        var repository = new SummonerRepository(db, Mock.Of<IRankRepository>());

        var result = await repository.SearchByPrefixAsync("NA1", "so", null, 10, CancellationToken.None);

        result.Should().ContainSingle();
        result[0].GameName.Should().Be("Soju");
    }
}
