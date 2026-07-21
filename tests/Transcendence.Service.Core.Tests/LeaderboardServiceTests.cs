using FluentAssertions;
using Moq;
using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Core.Services.Leaderboards.Implementations;
using Transcendence.Service.Core.Services.RiotApi;

namespace Transcendence.Service.Core.Tests;

public sealed class LeaderboardServiceTests
{
    [Fact]
    public async Task Regional_leaderboard_preserves_ladder_order_and_assigns_positions()
    {
        var repository = new Mock<ILeaderboardRepository>();
        repository.Setup(x => x.GetRegionalAsync("NA1", false, 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new RegionalLeaderboardRow(Guid.NewGuid(), "First", "NA1", 1, "CHALLENGER", "I", 900, 100, 50, DateTime.UtcNow),
                new RegionalLeaderboardRow(Guid.NewGuid(), "Second", "NA1", 2, "GRANDMASTER", "I", 700, 80, 40, DateTime.UtcNow)
            ]);
        var service = new LeaderboardService(repository.Object);

        var result = await service.GetAsync("NA1", "solo", null, null, 2, 5);

        result.Queue.Should().Be(QueueCatalog.QueueFamilyRankedSoloDuo);
        result.Entries.Select(entry => entry.Position).Should().Equal(1, 2);
        result.Entries.Select(entry => entry.GameName).Should().Equal("First", "Second");
    }

    [Fact]
    public async Task Champion_leaderboard_sorts_by_sample_then_rank_and_calculates_rates()
    {
        var repository = new Mock<ILeaderboardRepository>();
        repository.Setup(x => x.GetChampionAsync("KR", 420, 157, "MIDDLE", 10, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                Row("Diamond", "DIAMOND", 20, 15, 40, 20, 50, 100, 40, 60),
                Row("Master", "MASTER", 30, 12, 40, 20, 50, 100, 40, 60),
                Row("MoreGames", "PLATINUM", 5, 3, 50, 30, 60, 120, 60, 80)
            ]);
        var service = new LeaderboardService(repository.Object);

        var result = await service.GetAsync("KR", "solo", 157, "mid", 10, 10);

        result.Role.Should().Be("MIDDLE");
        result.Entries.Select(entry => entry.GameName).Should().Equal("MoreGames", "Master", "Diamond");
        result.Entries[0].ChampionWinRate.Should().Be(60);
        result.Entries[0].ChampionKda.Should().Be(3);
    }

    [Theory]
    [InlineData("flex", QueueCatalog.QueueFamilyRankedFlex)]
    [InlineData("RANKED_FLEX_SR", QueueCatalog.QueueFamilyRankedFlex)]
    [InlineData("anything", QueueCatalog.QueueFamilyRankedSoloDuo)]
    public void NormalizeQueue_maps_supported_aliases(string input, string expected)
    {
        LeaderboardService.NormalizeQueue(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("mid", "MIDDLE")]
    [InlineData("support", "UTILITY")]
    [InlineData("invalid", null)]
    public void NormalizeRole_maps_supported_aliases(string input, string? expected)
    {
        LeaderboardService.NormalizeRole(input).Should().Be(expected);
    }

    private static ChampionLeaderboardRow Row(
        string gameName,
        string tier,
        int leaguePoints,
        int rankedWins,
        int games,
        int wins,
        long kills,
        long assists,
        long deaths,
        int rankedLosses) =>
        new(
            Guid.NewGuid(),
            gameName,
            "TAG",
            1,
            tier,
            "I",
            leaguePoints,
            rankedWins,
            rankedLosses,
            games,
            wins,
            kills,
            deaths,
            assists,
            DateTime.UtcNow);
}
