using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Transcendence.Data;
using Transcendence.Service.Core.Tests.Support;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Core.Services.Leaderboards.Implementations;
using Transcendence.Service.Core.Services.Diagnostics;
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
        using var services = BuildServices();
        var service = CreateService(repository.Object, services);

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
        using var services = BuildServices();
        var service = CreateService(repository.Object, services);

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

    [Fact]
    public async Task Champion_leaderboard_reuses_cached_response_for_identical_filters()
    {
        var repository = new Mock<ILeaderboardRepository>();
        repository.Setup(x => x.GetChampionAsync("KR", 420, 157, "MIDDLE", 10, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Row("Cached", "MASTER", 20, 10, 20, 12, 30, 40, 20, 5)]);
        using var services = BuildServices();
        var service = CreateService(repository.Object, services);

        var first = await service.GetAsync("kr", "solo", 157, "mid", 10, 10);
        var second = await service.GetAsync("KR", "RANKED_SOLO_DUO", 157, "MIDDLE", 10, 10);

        second.Should().BeEquivalentTo(first);
        repository.Verify(
            x => x.GetChampionAsync("KR", 420, 157, "MIDDLE", 10, 10, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DurableRegionalSnapshot_ServesColdLimits_AndSurvivesFailedRefresh()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new SqliteCompatibleTranscendenceContext(
            new DbContextOptionsBuilder<TranscendenceContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var repository = new Mock<ILeaderboardRepository>(MockBehavior.Strict);
        repository.Setup(x => x.GetRegionalAsync("NA1", false, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new RegionalLeaderboardRow(Guid.NewGuid(), "First", "NA1", 1, "CHALLENGER", "I", 900, 100, 50, DateTime.UtcNow),
                new RegionalLeaderboardRow(Guid.NewGuid(), "Second", "NA1", 2, "MASTER", "I", 700, 80, 40, DateTime.UtcNow)
            ]);
        using (var warmCache = BuildServices())
            await new LeaderboardService(repository.Object, warmCache.GetRequiredService<HybridCache>(),
                new LeaderboardTelemetry(), db).RefreshRegionalAsync("NA1", "solo");
        repository.Reset();
        using var coldCache = BuildServices();
        var reader = new LeaderboardService(repository.Object, coldCache.GetRequiredService<HybridCache>(),
            new LeaderboardTelemetry(), db);
        (await reader.GetAsync("na1", "solo", null, null, 1, 5)).Entries.Select(row => row.GameName).Should().Equal("First");
        repository.VerifyNoOtherCalls();
        repository.Setup(x => x.GetRegionalAsync("NA1", false, 100, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database temporarily unavailable"));
        await reader.Invoking(service => service.RefreshRegionalAsync("NA1", "solo")).Should().ThrowAsync<InvalidOperationException>();
        using var restartedCache = BuildServices();
        var restarted = new LeaderboardService(repository.Object, restartedCache.GetRequiredService<HybridCache>(),
            new LeaderboardTelemetry(), db);
        (await restarted.GetAsync("NA1", "solo", null, null, 100, 5)).Entries.Should().HaveCount(2);
        repository.Verify(x => x.GetRegionalAsync("NA1", false, 100, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddHybridCache();
        return services.BuildServiceProvider();
    }

    private static LeaderboardService CreateService(
        ILeaderboardRepository repository,
        IServiceProvider services) =>
        new(
            repository,
            services.GetRequiredService<HybridCache>(),
            new LeaderboardTelemetry());

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
