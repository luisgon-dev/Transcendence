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
    public async Task GetProfile_ChoosesMostPlayedRoleAndReturnsAggregate()
    {
        var service = new Mock<IChampionAnalyticsService>();
        service
            .Setup(x => x.GetWinRatesAsync(
                103,
                It.Is<ChampionAnalyticsFilter>(filter =>
                    filter.RankTier == "EMERALD_PLUS" &&
                    filter.Region == "KR" &&
                    filter.Patch == "15.1"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChampionWinRateSummary(
                103,
                "15.1",
                [
                    new ChampionWinRateDto(103, "MIDDLE", "EMERALD_PLUS", 20, 11, 0.55, 0.2, 0.01, 1, 50, "15.1"),
                    new ChampionWinRateDto(103, "TOP", "EMERALD_PLUS", 8, 3, 0.375, 0.08, 0.01, 12, 50, "15.1")
                ]));
        service
            .Setup(x => x.GetBuildsAsync(103, "MIDDLE", "EMERALD_PLUS", "KR", "15.1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChampionBuildsResponse(103, "MIDDLE", "EMERALD_PLUS", "KR", "15.1", [], []));
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
        var controller = new ChampionAnalyticsController(service.Object, null);

        var result = await controller.GetProfile(103, null, "EMERALD_PLUS", "KR", "15.1", CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = ok.Value.Should().BeOfType<ChampionProfileAnalyticsResponse>().Subject;
        payload.EffectiveRole.Should().Be("MIDDLE");
        payload.WinRates.ByRoleTier.Should().HaveCount(2);
        payload.Builds.Role.Should().Be("MIDDLE");
        payload.Matchups.Role.Should().Be("MIDDLE");
    }

    [Fact]
    public async Task GetProBuilds_DefaultsToMostPlayedRole_WhenRoleOmitted()
    {
        var service = new Mock<IChampionAnalyticsService>();
        service
            .Setup(x => x.GetWinRatesAsync(
                103,
                It.Is<ChampionAnalyticsFilter>(filter =>
                    filter.Role == null &&
                    filter.Region == "KR" &&
                    filter.Patch == "15.1"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChampionWinRateSummary(
                103,
                "15.1",
                [
                    new ChampionWinRateDto(103, "MIDDLE", "EMERALD_PLUS", 20, 11, 0.55, 0.2, 0.01, 1, 50, "15.1"),
                    new ChampionWinRateDto(103, "TOP", "EMERALD_PLUS", 8, 3, 0.375, 0.08, 0.01, 12, 50, "15.1")
                ]));
        service
            .Setup(x => x.GetProBuildsAsync(103, "KR", "MIDDLE", null, "15.1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChampionProBuildsResponse(103, "15.1", "MIDDLE", "KR", "all", [], [], []));
        var controller = new ChampionAnalyticsController(service.Object, null);

        var result = await controller.GetProBuilds(103, "KR", null, null, "15.1", CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = ok.Value.Should().BeOfType<ChampionProBuildsResponse>().Subject;
        payload.Role.Should().Be("MIDDLE");
        service.Verify(
            x => x.GetProBuildsAsync(103, "KR", "MIDDLE", null, "15.1", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetProBuilds_UsesExplicitRole_WithoutResolvingMostPlayed()
    {
        var service = new Mock<IChampionAnalyticsService>();
        service
            .Setup(x => x.GetProBuildsAsync(103, "KR", "TOP", "pro", "15.1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChampionProBuildsResponse(103, "15.1", "TOP", "KR", "pro", [], [], []));
        var controller = new ChampionAnalyticsController(service.Object, null);

        var result = await controller.GetProBuilds(103, "KR", "TOP", "pro", "15.1", CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
        service.Verify(
            x => x.GetProBuildsAsync(103, "KR", "TOP", "pro", "15.1", It.IsAny<CancellationToken>()),
            Times.Once);
        service.Verify(
            x => x.GetWinRatesAsync(It.IsAny<int>(), It.IsAny<ChampionAnalyticsFilter>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetProBuilds_RejectsUnknownRole()
    {
        var service = new Mock<IChampionAnalyticsService>();
        var controller = new ChampionAnalyticsController(service.Object, null);

        var result = await controller.GetProBuilds(103, null, "BANANA", null, null, CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        service.Verify(
            x => x.GetProBuildsAsync(
                It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

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
        var controller = new ChampionAnalyticsController(service.Object, null);

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
        var controller = new ChampionAnalyticsController(service.Object, null);

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
        var controller = new ChampionAnalyticsController(service.Object, null);

        var result = await controller.GetMatchups(103, "MIDDLE", "EMERALD_PLUS", "KR", "15.1", CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
        service.Verify(x => x.GetMatchupsAsync(103, "MIDDLE", "EMERALD_PLUS", "KR", "15.1", It.IsAny<CancellationToken>()), Times.Once);
    }
}
