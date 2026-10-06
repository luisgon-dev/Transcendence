using FluentAssertions;
using Moq;
using Transcendence.Data.Models.LoL.Account;
using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Core.Services.Analysis.Interfaces;
using Transcendence.Service.Core.Services.Analysis.Models;
using Transcendence.Service.Core.Services.Summoners.Implementations;

namespace Transcendence.Service.Core.Tests;

public class SummonerProfileServiceTests
{
    [Fact]
    public async Task GetProfileByRiotIdAsync_ReadsOnlyTheLatestMatchDate_NotAHistoryPage()
    {
        var summoner = new Summoner
        {
            Id = Guid.NewGuid(),
            Puuid = "puuid-profile",
            PlatformRegion = "NA1",
            GameName = "Kronic",
            TagLine = "NA1",
            UpdatedAt = DateTime.UtcNow
        };
        var repository = new Mock<ISummonerRepository>();
        repository
            .Setup(x => x.FindByRiotIdWithRanksAsync("NA1", "Kronic", "NA1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(summoner);
        var stats = new Mock<ISummonerStatsService>();
        stats
            .Setup(x => x.GetActiveSeasonProfileStatsAsync(summoner.Id, 5, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SummonerSeasonProfileStats(
                "2026", "Season 2026", "RANKED_SOLO_DUO",
                new SummonerOverviewStats(summoner.Id, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, []),
                [],
                null));
        stats
            .Setup(x => x.GetPlayedWithAsync(summoner.Id, 100, 6, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        stats
            .Setup(x => x.GetTopMasteryAsync(summoner.Id, 6, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var history = new Mock<ISummonerMatchHistoryService>(MockBehavior.Strict);
        const long latest = 1_790_000_000_000;
        history
            .Setup(x => x.GetLatestMatchDateAsync(summoner.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(latest);

        var service = new SummonerProfileService(repository.Object, stats.Object, history.Object);

        var profile = await service.GetProfileByRiotIdAsync("NA1", "Kronic", "NA1");

        profile.Should().NotBeNull();
        profile!.StatsAge!.FetchedAt.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(latest).UtcDateTime);
        // Strict mock: building a recent-matches page (items, runes, team scores) would throw here.
        history.Verify(x => x.GetLatestMatchDateAsync(summoner.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetProfileByRiotIdAsync_LeavesStatsAgeEmpty_WhenNoMatchIsStored()
    {
        var summoner = new Summoner { Id = Guid.NewGuid(), Puuid = "puuid-empty", PlatformRegion = "NA1" };
        var repository = new Mock<ISummonerRepository>();
        repository
            .Setup(x => x.FindByRiotIdWithRanksAsync("NA1", "Nobody", "NA1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(summoner);
        var stats = new Mock<ISummonerStatsService>();
        stats
            .Setup(x => x.GetActiveSeasonProfileStatsAsync(summoner.Id, 5, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SummonerSeasonProfileStats(
                "2026", "Season 2026", "RANKED_SOLO_DUO",
                new SummonerOverviewStats(summoner.Id, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, []),
                [],
                null));
        stats.Setup(x => x.GetPlayedWithAsync(summoner.Id, 100, 6, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        stats.Setup(x => x.GetTopMasteryAsync(summoner.Id, 6, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var history = new Mock<ISummonerMatchHistoryService>(MockBehavior.Strict);
        history
            .Setup(x => x.GetLatestMatchDateAsync(summoner.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((long?)null);

        var profile = await new SummonerProfileService(repository.Object, stats.Object, history.Object)
            .GetProfileByRiotIdAsync("NA1", "Nobody", "NA1");

        profile!.StatsAge.Should().BeNull();
    }
}
