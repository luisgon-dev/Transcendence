using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Account;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Service.Core.Services.Analytics.Interfaces;
using Transcendence.Service.Core.Services.Analytics.Models;
using Transcendence.Service.Core.Services.LiveGame.Implementations;
using Transcendence.Service.Core.Services.LiveGame.Models;
using Transcendence.Service.Core.Tests.Support;
using StoredMatch = Transcendence.Data.Models.LoL.Match.Match;

namespace Transcendence.Service.Core.Tests;

public sealed class LiveGameAnalysisServiceTests
{
    [Fact]
    public async Task Analyze_uses_recent_ratio_streak_and_champion_pool_instead_of_lifetime_percent()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TranscendenceContext>().UseSqlite(connection).Options;
        await using var db = new SqliteCompatibleTranscendenceContext(options);
        await db.Database.EnsureCreatedAsync();

        var summoner = new Summoner
        {
            Id = Guid.NewGuid(),
            Puuid = "player-puuid",
            GameName = "Player",
            TagLine = "NA1",
            PlatformRegion = "NA1",
            Region = "AMERICAS",
            Ranks =
            [
                new Rank
                {
                    Id = Guid.NewGuid(),
                    QueueType = "RANKED_SOLO_5x5",
                    Tier = "DIAMOND",
                    RankNumber = "II",
                    LeaguePoints = 40
                }
            ]
        };
        db.Summoners.Add(summoner);

        var outcomes = new[] { true, true, true, false, true };
        var champions = new[] { 24, 24, 24, 86, 86 };
        for (var index = 0; index < outcomes.Length; index++)
        {
            var match = new StoredMatch
            {
                Id = Guid.NewGuid(),
                MatchId = $"NA1_{index}",
                MatchDate = 10_000 - index,
                Duration = 1800,
                QueueId = 420,
                Status = FetchStatus.Success
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
                ChampionId = champions[index],
                Win = outcomes[index],
                Kills = 6,
                Deaths = 3,
                Assists = 6
            });
        }
        await db.SaveChangesAsync();

        var analytics = new Mock<IChampionAnalyticsService>();
        analytics
            .Setup(service => service.GetWinRatesAsync(24, It.IsAny<ChampionAnalyticsFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChampionWinRateSummary(
                24,
                "16.14",
                [new ChampionWinRateDto(24, "TOP", "DIAMOND", 100, 52, 0.52, 0.1, 0.02, 1, 10, "16.14")]));
        var service = new LiveGameAnalysisService(db, analytics.Object);
        var liveGame = new LiveGameResponseDto(
            "in_game",
            "NA1",
            "game",
            "420",
            "11",
            DateTime.UtcNow,
            60,
            [new LiveGameParticipantDto("player-puuid", "Player#NA1", "sid", 100, 24, 4, 12, 1, [8005], 8000, 8300)],
            DateTime.UtcNow,
            0);

        var result = await service.AnalyzeAsync("NA1", liveGame);

        var participant = result.Participants.Should().ContainSingle().Subject;
        participant.RecentGames.Should().Be(5);
        participant.RecentWinRate.Should().BeApproximately(0.8, 0.001);
        participant.RecentKda.Should().BeApproximately(4, 0.001);
        participant.CurrentStreak.Should().Be(3);
        participant.ChampionPool.Select(entry => (entry.ChampionId, entry.Games))
            .Should().Equal((24, 3), (86, 2));
        result.Teams.Should().ContainSingle().Which.AverageRecentWinRate.Should().BeApproximately(0.8, 0.001);
    }
}
