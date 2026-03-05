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
using Transcendence.Service.Core.Services.Jobs;
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

    [Fact]
    public async Task RefreshByRiotId_WhenLockHeld_ReturnsRetryHintWithPollLink()
    {
        var refreshLockRepository = new Mock<IRefreshLockRepository>();
        var lockExpiry = DateTime.UtcNow.AddSeconds(45);
        refreshLockRepository
            .Setup(x => x.TryAcquireAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        refreshLockRepository
            .Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Data.Models.Service.RefreshLock
            {
                Key = "summoner-refresh:NA1:NAME:TAG",
                LockedUntilUtc = lockExpiry
            });

        var controller = BuildController(
            refreshLockRepository: refreshLockRepository.Object,
            backgroundJobClient: Mock.Of<IBackgroundJobClient>());
        controller.Url = new StaticUrlHelper("https://localhost/api/summoners/na1/name/tag");

        var result = await controller.RefreshByRiotId("na1", "name", "tag", CancellationToken.None);

        var accepted = result.Should().BeOfType<AcceptedResult>().Subject;
        var payload = accepted.Value.Should().BeOfType<SummonerAcceptedResponse>().Subject;
        payload.Message.Should().Be("Refresh in process");
        payload.Poll.Should().Be("https://localhost/api/summoners/na1/name/tag");
        payload.RetryAfterSeconds.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task RefreshByRiotId_UsesCanonicalLockKeyNormalizationForContentionChecks()
    {
        var refreshLockRepository = new Mock<IRefreshLockRepository>();
        var observedKeys = new List<string>();
        refreshLockRepository
            .Setup(x => x.TryAcquireAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns<string, TimeSpan, CancellationToken>((key, _, _) =>
            {
                observedKeys.Add(key);
                return Task.FromResult(false);
            });
        refreshLockRepository
            .Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Data.Models.Service.RefreshLock
            {
                Key = "summoner-refresh:NA1:NAME:TAG",
                LockedUntilUtc = DateTime.UtcNow.AddSeconds(30)
            });

        var controller = BuildController(
            refreshLockRepository: refreshLockRepository.Object,
            backgroundJobClient: Mock.Of<IBackgroundJobClient>());
        controller.Url = new StaticUrlHelper("https://localhost/api/summoners/na1/name/tag");

        await controller.RefreshByRiotId("na1", "  Name  ", "  tag ", CancellationToken.None);

        observedKeys.Should().ContainSingle();
        observedKeys[0].Should()
            .Be(RefreshLockKeys.BuildSummonerRefreshKey(Camille.Enums.PlatformRoute.NA1, "Name", "tag"));
        refreshLockRepository.Verify(
            x => x.GetAsync(observedKeys[0], It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static SummonersController BuildController(
        ISummonerRepository? summonerRepository = null,
        IRefreshLockRepository? refreshLockRepository = null,
        IBackgroundJobClient? backgroundJobClient = null,
        ISummonerStatsService? statsService = null,
        IMultiSearchService? multiSearchService = null)
    {
        return new SummonersController(
            summonerRepository ?? Mock.Of<ISummonerRepository>(),
            refreshLockRepository ?? Mock.Of<IRefreshLockRepository>(),
            backgroundJobClient ?? Mock.Of<IBackgroundJobClient>(),
            statsService ?? Mock.Of<ISummonerStatsService>(),
            multiSearchService ?? Mock.Of<IMultiSearchService>())
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
