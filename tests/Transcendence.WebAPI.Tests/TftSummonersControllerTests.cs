using FluentAssertions;
using Hangfire;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Moq;
using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Core.Services.RiotApi.DTOs;
using Transcendence.Service.Core.Services.Tft.Interfaces;
using Transcendence.Service.Core.Services.Tft.Models;
using Transcendence.WebAPI.Controllers;

namespace Transcendence.WebAPI.Tests;

public class TftSummonersControllerTests
{
    [Fact]
    public async Task GetByRiotId_ReturnsAcceptedWhenSummonerMissing()
    {
        var readService = new Mock<ITftSummonerReadService>();
        var refreshLockRepository = new Mock<IRefreshLockRepository>();

        readService
            .Setup(x => x.GetProfileByRiotIdAsync("NA1", "name", "tag", It.IsAny<CancellationToken>()))
            .ReturnsAsync((TftSummonerProfileDto?)null);
        refreshLockRepository
            .Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Transcendence.Data.Models.Service.RefreshLock?)null);

        var controller = BuildController(readService.Object, refreshLockRepository.Object);
        controller.Url = new StaticUrlHelper("https://localhost/api/tft/summoners/na1/name/tag");

        var result = await controller.GetByRiotId("na1", "name", "tag", CancellationToken.None);

        var accepted = result.Should().BeOfType<AcceptedResult>().Subject;
        var payload = accepted.Value.Should().BeOfType<SummonerAcceptedResponse>().Subject;
        payload.Message.Should().Contain("Use the refresh endpoint");
        payload.Poll.Should().Be("https://localhost/api/tft/summoners/na1/name/tag");
    }

    [Fact]
    public async Task RefreshByRiotId_WhenLockHeld_ReturnsRetryHint()
    {
        var refreshLockRepository = new Mock<IRefreshLockRepository>();
        var backgroundJobClient = new Mock<IBackgroundJobClient>();
        refreshLockRepository
            .Setup(x => x.TryAcquireAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        refreshLockRepository
            .Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Transcendence.Data.Models.Service.RefreshLock
            {
                Key = "tft:summoner-refresh:NA1:NAME:TAG",
                LockedUntilUtc = DateTime.UtcNow.AddSeconds(30)
            });

        var controller = BuildController(
            refreshLockRepository: refreshLockRepository.Object,
            backgroundJobClient: backgroundJobClient.Object);
        controller.Url = new StaticUrlHelper("https://localhost/api/tft/summoners/na1/name/tag");

        var result = await controller.RefreshByRiotId("na1", "name", "tag", CancellationToken.None);

        var accepted = result.Should().BeOfType<AcceptedResult>().Subject;
        var payload = accepted.Value.Should().BeOfType<SummonerAcceptedResponse>().Subject;
        payload.Message.Should().Be("Refresh in process");
        payload.RetryAfterSeconds.Should().BeGreaterThan(0);
        backgroundJobClient.Verify(
            x => x.Create(It.IsAny<Hangfire.Common.Job>(), It.IsAny<Hangfire.States.IState>()),
            Times.Never);
    }

    [Fact]
    public async Task GetRecentMatches_ReturnsPersistedMatches()
    {
        var summonerId = Guid.NewGuid();
        var readService = new Mock<ITftSummonerReadService>();
        readService
            .Setup(x => x.GetRecentMatchesAsync(summonerId, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TftPagedMatchesDto(
                [
                    new TftRecentMatchSummaryDto(
                        "TFT_1",
                        1_700_000_000_000,
                        1,
                        9,
                        6,
                        4,
                        120,
                        13,
                        "Cyber City",
                        "14.5",
                        ["Tiniest Titan"],
                        [],
                        [])
                ],
                1,
                20,
                1,
                1));

        var controller = BuildController(readService.Object);

        var result = await controller.GetRecentMatches(summonerId, 1, 20, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = ok.Value.Should().BeOfType<TftPagedMatchesDto>().Subject;
        payload.Items.Should().ContainSingle();
        payload.Items[0].MatchId.Should().Be("TFT_1");
    }

    private static TftSummonersController BuildController(
        ITftSummonerReadService? readService = null,
        IRefreshLockRepository? refreshLockRepository = null,
        IBackgroundJobClient? backgroundJobClient = null)
    {
        return new TftSummonersController(
            readService ?? Mock.Of<ITftSummonerReadService>(),
            refreshLockRepository ?? Mock.Of<IRefreshLockRepository>(),
            backgroundJobClient ?? Mock.Of<IBackgroundJobClient>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<TftSummonersController>.Instance)
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
        public string? Action(UrlActionContext actionContext) => url;
        public string? Content(string? contentPath) => contentPath;
        public bool IsLocalUrl(string? urlToTest) => true;
        public string? Link(string? routeName, object? values) => url;
        public string? RouteUrl(UrlRouteContext routeContext) => url;
    }
}
