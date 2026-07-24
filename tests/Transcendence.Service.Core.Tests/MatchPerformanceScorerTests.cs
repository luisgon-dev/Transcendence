using FluentAssertions;
using Transcendence.Service.Core.Services.Analysis;

namespace Transcendence.Service.Core.Tests;

public class MatchPerformanceScorerTests
{
    [Fact]
    public void Score_LabelsTheTopWinningTeammateMvp()
    {
        var carry = Player(win: true, kills: 14, deaths: 2, assists: 9, damage: 34000, gold: 16500, vision: 22, cs: 270);
        var inputs = new[]
        {
            carry,
            Player(win: true, kills: 3, deaths: 5, assists: 8, damage: 13000, gold: 10500, vision: 18, cs: 150),
            Player(win: true, kills: 2, deaths: 4, assists: 15, damage: 11000, gold: 9800, vision: 45, cs: 35),
            Player(win: true, kills: 5, deaths: 6, assists: 7, damage: 19000, gold: 12000, vision: 16, cs: 190),
            Player(win: true, kills: 4, deaths: 4, assists: 10, damage: 17000, gold: 11800, vision: 20, cs: 175)
        };

        var result = MatchPerformanceScorer.Score(inputs);

        result[carry.ParticipantId].Label.Should().Be("MVP");
        result[carry.ParticipantId].TeamRank.Should().Be(1);
        result[carry.ParticipantId].TeamSize.Should().Be(5);
        result[carry.ParticipantId].Score.Should().BeInRange(1, 10);
    }

    [Fact]
    public void Score_LabelsTheTopLosingTeammateAce()
    {
        var standout = Player(win: false, kills: 8, deaths: 3, assists: 12, damage: 28000, gold: 14500, vision: 31, cs: 220);
        var inputs = new[]
        {
            standout,
            Player(win: false, kills: 1, deaths: 8, assists: 4, damage: 9000, gold: 9200, vision: 15, cs: 130),
            Player(win: false, kills: 2, deaths: 7, assists: 7, damage: 12000, gold: 9800, vision: 38, cs: 30),
            Player(win: false, kills: 3, deaths: 9, assists: 5, damage: 15000, gold: 10500, vision: 13, cs: 160),
            Player(win: false, kills: 2, deaths: 6, assists: 6, damage: 13000, gold: 10100, vision: 18, cs: 145)
        };

        var result = MatchPerformanceScorer.Score(inputs);

        result[standout.ParticipantId].Label.Should().Be("ACE");
        result[standout.ParticipantId].TeamRank.Should().Be(1);
    }

    [Fact]
    public void Score_AllowsVisionAndParticipationToOffsetLowFarm()
    {
        var support = Player(
            win: true,
            kills: 2,
            deaths: 1,
            assists: 25,
            damage: 17500,
            gold: 9500,
            vision: 82,
            cs: 28);
        var farmLeader = Player(
            win: true,
            kills: 6,
            deaths: 7,
            assists: 5,
            damage: 24000,
            gold: 15800,
            vision: 12,
            cs: 305);
        var inputs = new[]
        {
            support,
            farmLeader,
            Player(win: true, kills: 5, deaths: 5, assists: 8, damage: 21000, gold: 13200, vision: 18, cs: 210),
            Player(win: true, kills: 4, deaths: 6, assists: 9, damage: 19000, gold: 12400, vision: 20, cs: 185),
            Player(win: true, kills: 3, deaths: 4, assists: 11, damage: 18000, gold: 11700, vision: 25, cs: 165)
        };

        var result = MatchPerformanceScorer.Score(inputs);

        result[support.ParticipantId].TeamRank.Should().BeLessThan(result[farmLeader.ParticipantId].TeamRank);
        result[support.ParticipantId].KillParticipation.Should().BeGreaterThan(0.8);
        result[support.ParticipantId].VisionShare.Should().BeGreaterThan(0.5);
    }

    [Fact]
    public void Score_ReturnsNeutralFiniteResultForASingleParticipant()
    {
        var participant = Player(win: true);

        var result = MatchPerformanceScorer.Score([participant])[participant.ParticipantId];

        result.Score.Should().Be(5.5);
        result.TeamRank.Should().Be(1);
        result.TeamSize.Should().Be(1);
        result.Label.Should().BeNull();
    }

    private static MatchPerformanceScorer.Input Player(
        bool win,
        int kills = 5,
        int deaths = 5,
        int assists = 5,
        int damage = 15000,
        int gold = 12000,
        int vision = 20,
        int cs = 180) =>
        new(
            Guid.Empty,
            Guid.NewGuid(),
            TeamId: 100,
            win,
            kills,
            deaths,
            assists,
            gold,
            damage,
            vision,
            cs,
            DurationSeconds: 1800);
}
