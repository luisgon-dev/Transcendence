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
using System.Security.Claims;
using Transcendence.Data.Models.Service;
using Transcendence.Service.Core.Services.Operations;

namespace Transcendence.WebAPI.Tests;

public sealed class LiveGameControllerTests
{
    [Fact]
    public async Task ProbeCurrentGame_normalizes_region_and_returns_operation_contract()
    {
        var coordinator = new Mock<ILiveGameProbeCoordinator>();
        coordinator.Setup(service => service.EnqueueAsync(
                PlatformRoute.NA1,
                "Kevsx",
                "The1",
                It.Is<OperationOwner>(owner => owner.Kind == OperationOwnerKind.Application),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LiveGameProbeOutcome(true, Guid.NewGuid(), 2));
        var url = new Mock<IUrlHelper>();
        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], "ApiKey"));
        httpContext.Request.Scheme = "https";
        httpContext.Request.Host = new HostString("localhost");
        url.SetupGet(helper => helper.ActionContext)
            .Returns(new ActionContext(httpContext, new RouteData(), new ActionDescriptor()));
        url.Setup(helper => helper.Action(It.IsAny<UrlActionContext>()))
            .Returns("https://localhost/api/lol/summoners/NA1/Kevsx/The1/live-game");
        var controller = new LiveGameController(Mock.Of<ILiveGameService>(), coordinator.Object)
        {
            Url = url.Object,
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };

        var result = await controller.ProbeCurrentGame("na", "Kevsx", "The1", CancellationToken.None);

        var accepted = result.Should().BeOfType<AcceptedResult>().Subject;
        var payload = accepted.Value.Should().BeOfType<OperationAcceptedResponse>().Subject;
        payload.OperationId.Should().NotBeEmpty();
        payload.RetryAfterSeconds.Should().Be(2);
        payload.StatusUrl.Should().Be($"/api/lol/operations/{payload.OperationId}");
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
            It.IsAny<OperationOwner>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }
}
