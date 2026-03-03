using FluentAssertions;
using Hangfire;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Moq;
using Transcendence.Data.Models.LoL.Account;
using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Core.Services.Analysis.Exceptions;
using Transcendence.Service.Core.Services.Analysis.Interfaces;
using Transcendence.Service.Core.Services.Analysis.Models;
using Transcendence.Service.Core.Services.RiotApi.DTOs;
using Transcendence.WebAPI.Controllers;

namespace Transcendence.WebAPI.Tests;

public class SummonersControllerTests
{
    [Fact]
    public async Task GetByRiotId_ReturnsBadRequestForInvalidRegion()
    {
        var controller = BuildController();

        var result = await controller.GetByRiotId("bad-region", "name", "tag", CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GetByRiotId_WhenStatsServiceFails_PropagatesException()
    {
        var summonerRepository = new Mock<ISummonerRepository>();
        var refreshLockRepository = new Mock<IRefreshLockRepository>();
        var backgroundJobClient = new Mock<IBackgroundJobClient>();
        var statsService = new Mock<ISummonerStatsService>();
        var summoner = new Summoner
        {
            Id = Guid.NewGuid(),
            PlatformRegion = "NA1",
            Region = "americas",
            Puuid = "puuid",
            GameName = "name",
            TagLine = "tag",
            GameNameNormalized = "name",
            TagLineNormalized = "tag",
            SummonerLevel = 200,
            UpdatedAt = DateTime.UtcNow
        };

        summonerRepository
            .Setup(x => x.FindByRiotIdAsync(
                "NA1",
                "name",
                "tag",
                It.IsAny<Func<IQueryable<Summoner>, IQueryable<Summoner>>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(summoner);

        statsService
            .Setup(x => x.GetSummonerOverviewAsync(summoner.Id, 20, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SummonerStatsComputationException("Failed to compute overview stats.", new Exception("boom")));

        var controller = BuildController(
            summonerRepository.Object,
            refreshLockRepository.Object,
            backgroundJobClient.Object,
            statsService.Object);

        Func<Task> act = async () => await controller.GetByRiotId("na1", "name", "tag", CancellationToken.None);

        await act.Should().ThrowAsync<SummonerStatsComputationException>();
    }

    [Fact]
    public async Task GetByRiotId_ReturnsAcceptedWhenSummonerMissing()
    {
        var summonerRepository = new Mock<ISummonerRepository>();
        var refreshLockRepository = new Mock<IRefreshLockRepository>();
        var backgroundJobClient = new Mock<IBackgroundJobClient>();
        var statsService = new Mock<ISummonerStatsService>();

        summonerRepository
            .Setup(x => x.FindByRiotIdAsync(
                "NA1",
                "name",
                "tag",
                It.IsAny<Func<IQueryable<Summoner>, IQueryable<Summoner>>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Summoner?)null);

        refreshLockRepository.Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Data.Models.Service.RefreshLock?)null);

        var controller = BuildController(
            summonerRepository.Object,
            refreshLockRepository.Object,
            backgroundJobClient.Object,
            statsService.Object);
        controller.Url = new StaticUrlHelper("https://localhost/api/summoners/na1/name/tag");

        var result = await controller.GetByRiotId("na1", "name", "tag", CancellationToken.None);

        var accepted = result.Should().BeOfType<AcceptedResult>().Subject;
        var payload = accepted.Value.Should().BeOfType<SummonerAcceptedResponse>().Subject;
        payload.Message.Should().Contain("Summoner not found");
    }

    private static SummonersController BuildController(
        ISummonerRepository? summonerRepository = null,
        IRefreshLockRepository? refreshLockRepository = null,
        IBackgroundJobClient? backgroundJobClient = null,
        ISummonerStatsService? statsService = null)
    {
        return new SummonersController(
            summonerRepository ?? Mock.Of<ISummonerRepository>(),
            refreshLockRepository ?? Mock.Of<IRefreshLockRepository>(),
            backgroundJobClient ?? Mock.Of<IBackgroundJobClient>(),
            statsService ?? Mock.Of<ISummonerStatsService>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }

    private sealed class StaticUrlHelper(string url) : IUrlHelper
    {
        private static readonly ActionContext StaticActionContext =
            new(new DefaultHttpContext(), new RouteData(), new ActionDescriptor());

        public ActionContext ActionContext => StaticActionContext;

        public string? Action(UrlActionContext actionContext)
        {
            return url;
        }

        public string? Content(string? contentPath)
        {
            return contentPath;
        }

        public bool IsLocalUrl(string? urlToTest)
        {
            return true;
        }

        public string? Link(string? routeName, object? values)
        {
            return url;
        }

        public string? RouteUrl(UrlRouteContext routeContext)
        {
            return url;
        }
    }
}
