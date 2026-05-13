using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Transcendence.Service.Core.Services.Analytics.Interfaces;
using Transcendence.Service.Core.Services.Analytics.Models;
using Transcendence.WebAPI.Controllers;

namespace Transcendence.WebAPI.Tests;

public class ChampionAnalyticsControllerTests
{
    [Fact]
    public async Task GetWinRates_ForwardsPatchFilter()
    {
        var service = new Mock<IChampionAnalyticsService>();
        service
            .Setup(x => x.GetWinRatesAsync(
                103,
                It.Is<ChampionAnalyticsFilter>(filter => filter.Patch == "15.1"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChampionWinRateSummary(103, "15.1", []));
        var controller = new ChampionAnalyticsController(service.Object);

        var result = await controller.GetWinRates(103, "EMERALD_PLUS", "KR", "MIDDLE", "15.1", CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        service.Verify(x => x.GetWinRatesAsync(
            103,
            It.Is<ChampionAnalyticsFilter>(filter => filter.Patch == "15.1"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetBuilds_ForwardsPatchFilter()
    {
        var service = new Mock<IChampionAnalyticsService>();
        service
            .Setup(x => x.GetBuildsAsync(103, "MIDDLE", "EMERALD_PLUS", "KR", "15.1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChampionBuildsResponse(103, "MIDDLE", "EMERALD_PLUS", "KR", "15.1", [], []));
        var controller = new ChampionAnalyticsController(service.Object);

        var result = await controller.GetBuilds(103, "MIDDLE", "EMERALD_PLUS", "KR", "15.1", CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
        service.Verify(x => x.GetBuildsAsync(103, "MIDDLE", "EMERALD_PLUS", "KR", "15.1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetMatchups_ForwardsPatchFilter()
    {
        var service = new Mock<IChampionAnalyticsService>();
        service
            .Setup(x => x.GetMatchupsAsync(103, "MIDDLE", "EMERALD_PLUS", "KR", "15.1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChampionMatchupsResponse
            {
                ChampionId = 103,
                Role = "MIDDLE",
                RankTier = "EMERALD_PLUS",
                Region = "KR",
                Patch = "15.1"
            });
        var controller = new ChampionAnalyticsController(service.Object);

        var result = await controller.GetMatchups(103, "MIDDLE", "EMERALD_PLUS", "KR", "15.1", CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
        service.Verify(x => x.GetMatchupsAsync(103, "MIDDLE", "EMERALD_PLUS", "KR", "15.1", It.IsAny<CancellationToken>()), Times.Once);
    }
}
