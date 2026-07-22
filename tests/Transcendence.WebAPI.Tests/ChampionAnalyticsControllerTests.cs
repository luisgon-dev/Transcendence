using FluentAssertions;
using Microsoft.AspNetCore.Http;
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
            .Setup(x => x.GetBuildsAsync(103, "MIDDLE", "EMERALD_PLUS", "KR", "RANKED_SOLO_DUO", "15.1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChampionBuildsResponse(103, "MIDDLE", "EMERALD_PLUS", "KR", "15.1", [], []));
        service
            .Setup(x => x.GetMatchupsAsync(103, "MIDDLE", "EMERALD_PLUS", "KR", "RANKED_SOLO_DUO", "15.1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChampionMatchupsResponse
            {
                ChampionId = 103,
                Role = "MIDDLE",
                RankTier = "EMERALD_PLUS",
                Region = "KR",
                Patch = "15.1"
            });
        service.Setup(x => x.GetTrendAsync(
                103, "MIDDLE", "EMERALD_PLUS", "RANKED_SOLO_DUO", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChampionTrendResponse(103, "RANKED_SOLO_DUO", "MIDDLE", "EMERALD_PLUS", "ALL", []));
        var synergy = new Mock<IChampionSynergyService>();
        synergy.Setup(x => x.GetSynergiesAsync(
                103, "MIDDLE", "EMERALD_PLUS", "KR", "RANKED_SOLO_DUO", "15.1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChampionSynergiesResponse(
                103, "MIDDLE", "EMERALD_PLUS", "KR", "15.1", "RANKED_SOLO_DUO", 20, 11, 0.55,
                [new ChampionSynergyEntryDto(64, "JUNGLE", 10, 6, 0.6, 0.5, 0.05, 0.01)]));
        var controller = new ChampionAnalyticsController(service.Object, null, synergy.Object);

        var result = await controller.GetProfile(103, null, "EMERALD_PLUS", "KR", null, "15.1", CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = ok.Value.Should().BeOfType<ChampionProfileAnalyticsResponse>().Subject;
        payload.EffectiveRole.Should().Be("MIDDLE");
        payload.WinRates.ByRoleTier.Should().HaveCount(2);
        payload.Builds.Role.Should().Be("MIDDLE");
        payload.Matchups.Role.Should().Be("MIDDLE");
        payload.Synergies!.BestPartners.Should().ContainSingle();
    }

    [Fact]
    public async Task GetSynergies_ReturnsServiceUnavailable_WhenSynergyServiceIsNotRegistered()
    {
        var analytics = new Mock<IChampionAnalyticsService>();
        var controller = new ChampionAnalyticsController(analytics.Object, null);

        var result = await controller.GetSynergies(
            103, "MIDDLE", "EMERALD_PLUS", "KR", "flex", "15.1", CancellationToken.None);

        var unavailable = result.Result.Should().BeOfType<StatusCodeResult>().Subject;
        unavailable.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
    }

    [Fact]
    public async Task GetProfile_CollapsesRoleForAramAndForwardsQueueToEverySurface()
    {
        var service = new Mock<IChampionAnalyticsService>();
        service.Setup(x => x.GetWinRatesAsync(
                103,
                It.Is<ChampionAnalyticsFilter>(filter => filter.QueueFamily == "ARAM"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChampionWinRateSummary(103, "15.1", [], QueueFamily: "ARAM"));
        service.Setup(x => x.GetBuildsAsync(
                103, "ALL", null, null, "ARAM", "15.1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChampionBuildsResponse(103, "ALL", "all", "ALL", "15.1", [], [], QueueFamily: "ARAM"));
        service.Setup(x => x.GetMatchupsAsync(
                103, "ALL", null, null, "ARAM", "15.1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChampionMatchupsResponse { ChampionId = 103, Role = "ALL", Patch = "15.1", QueueFamily = "ARAM" });
        service.Setup(x => x.GetTrendAsync(
                103, "ALL", null, "ARAM", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChampionTrendResponse(103, "ARAM", "ALL", "all", "ALL", []));

        var controller = new ChampionAnalyticsController(service.Object, null);
        var result = await controller.GetProfile(103, null, null, null, "aram", "15.1", CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = ok.Value.Should().BeOfType<ChampionProfileAnalyticsResponse>().Subject;
        payload.EffectiveRole.Should().Be("ALL");
        payload.QueueFamily.Should().Be("ARAM");
        service.Verify(x => x.GetGradeAsync(
            103, "ALL", null, null, "ARAM", "15.1", It.IsAny<CancellationToken>()), Times.Once);
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
    public async Task GetWinRates_RejectsUnsupportedQueueBeforeCallingService()
    {
        var service = new Mock<IChampionAnalyticsService>();
        var controller = new ChampionAnalyticsController(service.Object, null);

        var result = await controller.GetWinRates(
            103, "EMERALD_PLUS", "KR", "MIDDLE", "15.1", "twisted-treeline", CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
        service.Verify(x => x.GetWinRatesAsync(
            It.IsAny<int>(), It.IsAny<ChampionAnalyticsFilter>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetBuilds_ForwardsPatchAndQueueFilters()
    {
        var service = new Mock<IChampionAnalyticsService>();
        service
            .Setup(x => x.GetBuildsAsync(103, "MIDDLE", "EMERALD_PLUS", "KR", "RANKED_FLEX", "15.1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChampionBuildsResponse(103, "MIDDLE", "EMERALD_PLUS", "KR", "15.1", [], [], QueueFamily: "RANKED_FLEX"));
        var controller = new ChampionAnalyticsController(service.Object, null);

        var result = await controller.GetBuilds(103, "MIDDLE", "EMERALD_PLUS", "KR", "flex", "15.1", CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
        service.Verify(x => x.GetBuildsAsync(103, "MIDDLE", "EMERALD_PLUS", "KR", "RANKED_FLEX", "15.1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetMatchups_ForwardsPatchAndQueueFilters()
    {
        var service = new Mock<IChampionAnalyticsService>();
        service
            .Setup(x => x.GetMatchupsAsync(103, "MIDDLE", "EMERALD_PLUS", "KR", "RANKED_FLEX", "15.1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChampionMatchupsResponse
            {
                ChampionId = 103,
                Role = "MIDDLE",
                RankTier = "EMERALD_PLUS",
                Region = "KR",
                Patch = "15.1"
            });
        var controller = new ChampionAnalyticsController(service.Object, null);

        var result = await controller.GetMatchups(103, "MIDDLE", "EMERALD_PLUS", "KR", "flex", "15.1", CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
        service.Verify(x => x.GetMatchupsAsync(103, "MIDDLE", "EMERALD_PLUS", "KR", "RANKED_FLEX", "15.1", It.IsAny<CancellationToken>()), Times.Once);
    }
}
