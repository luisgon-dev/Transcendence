using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Account;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Data.Repositories.Implementations;

namespace Transcendence.IntegrationTests;

[Collection(PostgresIntegrationCollection.Name)]
public sealed class LeaderboardRepositoryPostgresTests(PostgresIntegrationFixture fixture)
{
    [Fact]
    public async Task Queries_RegionalAndChampionBoards_OnRealPostgres()
    {
        var region = $"T{Guid.NewGuid():N}"[..8].ToUpperInvariant();
        var now = DateTime.UtcNow;
        await using var db = NewDb();

        db.RankedSeasons.Add(new RankedSeason
        {
            SeasonKey = $"t{Guid.NewGuid():N}"[..12],
            DisplayName = "Leaderboard repository test",
            StartUtc = now.AddMinutes(-5),
            IsActive = true
        });

        var challenger = AddSummoner(db, region, "Alpha", "TEST", "CHALLENGER", 850, 40, 10, now);
        var diamond = AddSummoner(db, region, "Bravo", "TEST", "DIAMOND", 75, 25, 20, now);

        AddGame(db, challenger, region, now, role: "MIDDLE", win: true, kills: 8, deaths: 2, assists: 6);
        AddGame(db, challenger, region, now, role: "MIDDLE", win: false, kills: 2, deaths: 4, assists: 4);
        AddGame(db, challenger, region, now, role: "TOP", win: true, kills: 5, deaths: 1, assists: 3);
        AddGame(db, diamond, region, now, role: "MIDDLE", win: true, kills: 6, deaths: 3, assists: 3);
        await db.SaveChangesAsync();

        var repository = new LeaderboardRepository(db);

        var regional = await repository.GetRegionalAsync(region, rankedFlex: false, limit: 10);
        regional.Select(row => row.GameName).Should().Equal("Alpha", "Bravo");

        var champion = await repository.GetChampionAsync(
            region, queueId: 420, championId: 157, role: "MIDDLE", minimumGames: 1, limit: 10);
        champion.Should().HaveCount(2);
        var alpha = champion.Single(row => row.GameName == "Alpha");
        alpha.ChampionGames.Should().Be(2);
        alpha.ChampionWins.Should().Be(1);
        alpha.TotalKills.Should().Be(10);
        alpha.TotalDeaths.Should().Be(6);
        alpha.TotalAssists.Should().Be(10);
        champion.Single(row => row.GameName == "Bravo").ChampionGames.Should().Be(1);
    }

    private TranscendenceContext NewDb() =>
        new(new DbContextOptionsBuilder<TranscendenceContext>()
            .UseNpgsql(fixture.ConnectionString)
            .Options);

    private static Summoner AddSummoner(
        TranscendenceContext db,
        string region,
        string gameName,
        string tagLine,
        string tier,
        int leaguePoints,
        int wins,
        int losses,
        DateTime updatedAt)
    {
        var summoner = new Summoner
        {
            Id = Guid.NewGuid(),
            PlatformRegion = region,
            Region = "americas",
            GameName = gameName,
            TagLine = tagLine,
            GameNameNormalized = gameName.ToUpperInvariant(),
            TagLineNormalized = tagLine,
            Puuid = Guid.NewGuid().ToString("N"),
            RiotSummonerId = Guid.NewGuid().ToString("N"),
            ProfileIconId = 29,
            UpdatedAt = updatedAt
        };
        db.Summoners.Add(summoner);
        db.Ranks.Add(new Rank
        {
            Id = Guid.NewGuid(),
            SummonerId = summoner.Id,
            Summoner = summoner,
            QueueType = "RANKED_SOLO_5x5",
            Tier = tier,
            RankNumber = "I",
            LeaguePoints = leaguePoints,
            Wins = wins,
            Losses = losses,
            UpdatedAt = updatedAt
        });
        return summoner;
    }

    private static void AddGame(
        TranscendenceContext db,
        Summoner summoner,
        string region,
        DateTime now,
        string role,
        bool win,
        int kills,
        int deaths,
        int assists)
    {
        var match = new Match
        {
            Id = Guid.NewGuid(),
            MatchId = Guid.NewGuid().ToString("N"),
            MatchDate = new DateTimeOffset(now).ToUnixTimeMilliseconds(),
            Duration = 1800,
            QueueId = 420,
            QueueType = "420",
            Status = FetchStatus.Success,
            PlatformRegion = region,
            FetchedAt = now
        };
        db.Matches.Add(match);
        db.MatchParticipants.Add(new MatchParticipant
        {
            Id = Guid.NewGuid(),
            MatchId = match.Id,
            Match = match,
            SummonerId = summoner.Id,
            Summoner = summoner,
            Puuid = summoner.Puuid,
            ParticipantId = 1,
            TeamId = 100,
            ChampionId = 157,
            TeamPosition = role,
            Win = win,
            Kills = kills,
            Deaths = deaths,
            Assists = assists
        });
    }
}
