using FluentAssertions;
using Transcendence.Service.Core.Services.Analytics;
using Transcendence.Service.Core.Services.Analytics.Models;

namespace Transcendence.Service.Core.Tests;

public class ChampionRoleResolverTests
{
    [Fact]
    public void PickMostPlayed_AggregatesNormalizedLaneRowsAndIgnoresInvalidRoles()
    {
        var rows = new[]
        {
            Row(" middle ", 30),
            Row("MIDDLE", 20),
            Row("TOP", 40),
            Row("INVALID", 10_000),
            Row("JUNGLE", -10)
        };

        ChampionRoleResolver.PickMostPlayed(rows).Should().Be("MIDDLE");
    }

    [Fact]
    public void PickMostPlayed_UsesAStableRoleTieBreakAndHandlesNoRows()
    {
        ChampionRoleResolver.PickMostPlayed([Row("TOP", 10), Row("MIDDLE", 10)])
            .Should().Be("MIDDLE");
        ChampionRoleResolver.PickMostPlayed(null).Should().BeNull();
        ChampionRoleResolver.PickMostPlayed([]).Should().BeNull();
    }

    private static ChampionWinRateDto Row(string role, int games) => new(
        ChampionId: 1,
        Role: role,
        RankTier: "all",
        Games: games,
        Wins: 0,
        WinRate: 0,
        PickRate: 0,
        BanRate: 0,
        RoleRank: 0,
        RolePopulation: 0,
        Patch: "16.14");
}
