using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Transcendence.Service.Core.Services.Leaderboards.Interfaces;
using Transcendence.Service.Core.Services.Leaderboards.Models;
using Transcendence.WebAPI.Controllers;

namespace Transcendence.WebAPI.Tests;

public sealed class LeaderboardsControllerTests
{
    [Fact]
    public async Task Get_rejects_invalid_region_without_calling_service()
    {
        var service = new Mock<ILeaderboardService>();
        var controller = new LeaderboardsController(service.Object);

        var result = await controller.Get("moon", "solo", null, null, 100, 5);

        result.Should().BeOfType<BadRequestObjectResult>();
        service.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("aram", null)]
    [InlineData("solo", "river")]
    public async Task Get_rejects_unsupported_queue_or_champion_role(string queue, string? role)
    {
        var service = new Mock<ILeaderboardService>();
        var controller = new LeaderboardsController(service.Object);

        var result = await controller.Get("na", queue, 157, role, 100, 5);

        result.Should().BeOfType<BadRequestObjectResult>();
        service.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(101, 5)]
    [InlineData(10, 0)]
    [InlineData(10, 101)]
    public async Task Get_rejects_out_of_range_limits(int limit, int minimumGames)
    {
        var controller = new LeaderboardsController(Mock.Of<ILeaderboardService>());

        var result = await controller.Get("na", "solo", null, null, limit, minimumGames);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Get_forwards_normalized_platform_and_filters()
    {
        var service = new Mock<ILeaderboardService>();
        var response = new LeaderboardResponse("NA1", "RANKED_SOLO_DUO", 157, "MIDDLE", DateTime.UtcNow, []);
        service.Setup(x => x.GetAsync("NA1", "solo", 157, "middle", 25, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);
        var controller = new LeaderboardsController(service.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var result = await controller.Get("na", "solo", 157, "middle", 25, 10);

        result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(response);
        controller.Response.Headers.CacheControl.ToString().Should().Contain("stale-while-revalidate=300");
    }
}
