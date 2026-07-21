using FluentAssertions;
using Hangfire;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using System.Security.Claims;
using Transcendence.Data.Models.LoL.Account;
using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Core.Services.Analysis.Exceptions;
using Transcendence.Service.Core.Services.Analysis.Interfaces;
using Transcendence.Service.Core.Services.Analysis.Models;
using Transcendence.Service.Core.Services.Diagnostics;
using Transcendence.Service.Core.Services.Jobs;
using Transcendence.Service.Core.Services.Refresh.Implementations;
using Transcendence.Service.Core.Services.Summoners.Implementations;
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
            .Setup(x => x.FindByRiotIdWithRanksAsync(
                "NA1",
                "name",
                "tag",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(summoner);

        statsService
            .Setup(x => x.GetActiveSeasonProfileStatsAsync(summoner.Id, 5, 20, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SummonerStatsComputationException("Failed to compute active-season profile stats.", new Exception("boom")));

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
            .Setup(x => x.FindByRiotIdWithRanksAsync(
                "NA1",
                "name",
                "tag",
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
        var backgroundJobClient = new Mock<IBackgroundJobClient>();
        var lockTelemetry = new Mock<IRefreshLockLifecycleTelemetry>();
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
            backgroundJobClient: backgroundJobClient.Object,
            refreshLockTelemetry: lockTelemetry.Object);
        controller.Url = new StaticUrlHelper("https://localhost/api/summoners/na1/name/tag");

        var result = await controller.RefreshByRiotId("na1", "name", "tag", CancellationToken.None);

        var accepted = result.Should().BeOfType<AcceptedResult>().Subject;
        var payload = accepted.Value.Should().BeOfType<SummonerAcceptedResponse>().Subject;
        payload.Message.Should().Be("Refresh in process");
        payload.Poll.Should().Be("https://localhost/api/summoners/na1/name/tag");
        payload.RetryAfterSeconds.Should().BeGreaterThan(0);
        var expectedKey = RefreshLockKeys.BuildSummonerRefreshKey(Camille.Enums.PlatformRoute.NA1, "name", "tag");
        lockTelemetry.Verify(
            x => x.RecordLifecycleOutcome(expectedKey, "contention", "summoners-controller"),
            Times.Once);
        lockTelemetry.Verify(
            x => x.RecordContentionWaitHint(
                expectedKey,
                It.Is<int>(waitHint => waitHint == payload.RetryAfterSeconds),
                "summoners-controller"),
            Times.Once);
        backgroundJobClient.Verify(
            x => x.Create(It.IsAny<Hangfire.Common.Job>(), It.IsAny<Hangfire.States.IState>()),
            Times.Never);
    }

    [Fact]
    public async Task RefreshByRiotId_UsesCanonicalLockKeyNormalizationForContentionChecks()
    {
        var refreshLockRepository = new Mock<IRefreshLockRepository>();
        var observedKeys = new List<string>();
        refreshLockRepository
            .Setup(x => x.TryAcquireOwnedAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns<string, TimeSpan, CancellationToken>((key, _, _) =>
            {
                observedKeys.Add(key);
                return Task.FromResult<Guid?>(null);
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

    [Fact]
    public async Task RefreshByRiotId_WhenPriorityLockUnavailable_RecordsMainAcquireAndPriorityContentionTelemetry()
    {
        var expectedMainKey = RefreshLockKeys.BuildSummonerRefreshKey(Camille.Enums.PlatformRoute.NA1, "Name", "Tag");
        var expectedPriorityKey =
            RefreshLockKeys.BuildApiPriorityKey(Camille.Enums.PlatformRoute.NA1, "Name", "Tag");
        var refreshLockRepository = new Mock<IRefreshLockRepository>();
        refreshLockRepository
            .Setup(x => x.TryAcquireOwnedAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns<string, TimeSpan, CancellationToken>((key, _, _) =>
                Task.FromResult<Guid?>(key == expectedMainKey ? Guid.NewGuid() : null));
        var backgroundJobClient = new Mock<IBackgroundJobClient>();
        backgroundJobClient
            .Setup(x => x.Create(It.IsAny<Hangfire.Common.Job>(), It.IsAny<Hangfire.States.IState>()))
            .Returns("job-1");
        var lockTelemetry = new Mock<IRefreshLockLifecycleTelemetry>();

        var controller = BuildController(
            refreshLockRepository: refreshLockRepository.Object,
            backgroundJobClient: backgroundJobClient.Object,
            refreshLockTelemetry: lockTelemetry.Object);
        controller.Url = new StaticUrlHelper("https://localhost/api/summoners/na1/name/tag");

        var result = await controller.RefreshByRiotId("na1", "Name", "Tag", CancellationToken.None);

        var accepted = result.Should().BeOfType<AcceptedResult>().Subject;
        var payload = accepted.Value.Should().BeOfType<SummonerAcceptedResponse>().Subject;
        payload.Message.Should().Be("Refresh queued");
        payload.Poll.Should().Be("https://localhost/api/summoners/na1/name/tag");
        payload.RetryAfterSeconds.Should().BeNull();

        lockTelemetry.Verify(
            x => x.RecordLifecycleOutcome(expectedMainKey, "acquired", "summoners-controller"),
            Times.Once);
        lockTelemetry.Verify(
            x => x.RecordLifecycleOutcome(expectedPriorityKey, "contention", "summoners-controller"),
            Times.Once);
        lockTelemetry.Verify(
            x => x.RecordContentionWaitHint(expectedPriorityKey, 900, "summoners-controller"),
            Times.Once);
        backgroundJobClient.Verify(
            x => x.Create(It.IsAny<Hangfire.Common.Job>(), It.IsAny<Hangfire.States.IState>()),
            Times.Once);
    }

    private static SummonersController BuildController(
        ISummonerRepository? summonerRepository = null,
        IRefreshLockRepository? refreshLockRepository = null,
        IBackgroundJobClient? backgroundJobClient = null,
        ISummonerStatsService? statsService = null,
        IMultiSearchService? multiSearchService = null,
        IRefreshLockLifecycleTelemetry? refreshLockTelemetry = null)
    {
        var effectiveSummonerRepository = summonerRepository ?? Mock.Of<ISummonerRepository>();
        var effectiveRefreshLockRepository = refreshLockRepository ?? Mock.Of<IRefreshLockRepository>();
        var effectiveBackgroundJobClient = backgroundJobClient ?? Mock.Of<IBackgroundJobClient>();
        return new SummonersController(
            new SummonerProfileService(
                effectiveSummonerRepository,
                statsService ?? Mock.Of<ISummonerStatsService>()),
            new SummonerRefreshCoordinator(
                effectiveRefreshLockRepository,
                effectiveBackgroundJobClient,
                refreshLockTelemetry ?? Mock.Of<IRefreshLockLifecycleTelemetry>(),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<SummonerRefreshCoordinator>.Instance),
            multiSearchService ?? Mock.Of<IMultiSearchService>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = BuildUser()
                }
            }
        };
    }

    private static ClaimsPrincipal BuildUser()
    {
        return new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())],
            "unit-test"));
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
