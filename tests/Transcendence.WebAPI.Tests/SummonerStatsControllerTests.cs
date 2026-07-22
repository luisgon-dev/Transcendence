using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Transcendence.Service.Core.Services.Analysis.Interfaces;
using Transcendence.Service.Core.Services.Analysis.Models;
using Transcendence.Service.Core.Services.RiotApi.DTOs;
using Transcendence.WebAPI.Controllers;
using Transcendence.WebAPI.Models.Stats;

namespace Transcendence.WebAPI.Tests;

public class SummonerStatsControllerTests
{
    [Fact]
    public async Task GetOverview_ReturnsMappedDto()
    {
        var summonerId = Guid.NewGuid();
        var service = new Mock<ISummonerStatsService>();
        service.Setup(x => x.GetSummonerOverviewAsync(summonerId, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SummonerOverviewStats(
                summonerId,
                TotalMatches: 2,
                Wins: 1,
                Losses: 1,
                WinRate: 50,
                AvgKills: 8,
                AvgDeaths: 4,
                AvgAssists: 9,
                KdaRatio: 4.25,
                AvgCsPerMin: 6.8,
                AvgVisionScore: 23,
                AvgDamageToChamps: 19000,
                AvgGameDurationMin: 31,
                RecentPerformance:
                [
                    new RecentPerformancePoint("NA1_1", true, 10, 2, 8, 7.4, 30, 24000)
                ]));

        var controller = BuildController(service.Object);

        var actionResult = await controller.GetOverview(summonerId, 20, CancellationToken.None);

        var ok = actionResult.Should().BeOfType<OkObjectResult>().Subject;
        var payload = ok.Value.Should().BeOfType<SummonerOverviewDto>().Subject;
        payload.SummonerId.Should().Be(summonerId);
        payload.TotalMatches.Should().Be(2);
        payload.RecentPerformance.Should().ContainSingle();
    }

    [Fact]
    public async Task GetMatchDetail_ReturnsNotFoundWhenServiceReturnsNull()
    {
        var service = new Mock<ISummonerMatchHistoryService>();
        service.Setup(x => x.GetMatchDetailAsync("NA1_404", It.IsAny<CancellationToken>()))
            .ReturnsAsync((MatchDetailDto?)null);

        var controller = BuildController(Mock.Of<ISummonerStatsService>(), service.Object);

        var result = await controller.GetMatchDetail(Guid.NewGuid(), "NA1_404", CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
    }

    private static SummonerStatsController BuildController(
        ISummonerStatsService service,
        ISummonerMatchHistoryService? matchHistoryService = null)
    {
        return new SummonerStatsController(service, matchHistoryService ?? Mock.Of<ISummonerMatchHistoryService>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }
}
