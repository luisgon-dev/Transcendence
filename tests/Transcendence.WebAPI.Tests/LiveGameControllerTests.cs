using Camille.Enums;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Moq;
using Transcendence.Service.Core.Services.LiveGame.Interfaces;
using Transcendence.Service.Core.Services.LiveGame.Models;
using Transcendence.WebAPI.Controllers;

namespace Transcendence.WebAPI.Tests;

public sealed class LiveGameControllerTests
{
    [Fact]
    public async Task ProbeCurrentGame_normalizes_region_and_returns_poll_contract()
    {
        var coordinator = new Mock<ILiveGameProbeCoordinator>();
        coordinator.Setup(service => service.EnqueueAsync(
                PlatformRoute.NA1,
                "Kevsx",
                "The1",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LiveGameProbeOutcome(true, 2));
        var url = new Mock<IUrlHelper>();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "https";
        httpContext.Request.Host = new HostString("localhost");
        url.SetupGet(helper => helper.ActionContext)
            .Returns(new ActionContext(httpContext, new RouteData(), new ActionDescriptor()));
        url.Setup(helper => helper.Action(It.IsAny<UrlActionContext>()))
            .Returns("https://localhost/api/lol/summoners/NA1/Kevsx/The1/live-game");
        var controller = new LiveGameController(Mock.Of<ILiveGameService>(), coordinator.Object)
        {
            Url = url.Object
        };

        var result = await controller.ProbeCurrentGame("na", "Kevsx", "The1", CancellationToken.None);

        var accepted = result.Should().BeOfType<AcceptedResult>().Subject;
        var payload = accepted.Value.Should().BeOfType<LiveGameProbeAcceptedResponse>().Subject;
        payload.Status.Should().Be("queued");
        payload.RetryAfterSeconds.Should().Be(2);
        payload.Poll.Should().EndWith("/NA1/Kevsx/The1/live-game");
    }

    [Fact]
    public async Task ProbeCurrentGame_rejects_invalid_region_without_enqueueing()
    {
        var coordinator = new Mock<ILiveGameProbeCoordinator>();
        var controller = new LiveGameController(Mock.Of<ILiveGameService>(), coordinator.Object);

        var result = await controller.ProbeCurrentGame("invalid", "Kevsx", "The1", CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
        coordinator.Verify(service => service.EnqueueAsync(
            It.IsAny<PlatformRoute>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }
}
