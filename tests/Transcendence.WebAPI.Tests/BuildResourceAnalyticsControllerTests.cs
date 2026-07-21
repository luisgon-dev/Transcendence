using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Transcendence.Service.Core.Services.Analytics.Interfaces;
using Transcendence.Service.Core.Services.Analytics.Models;
using Transcendence.WebAPI.Controllers;

namespace Transcendence.WebAPI.Tests;

public sealed class BuildResourceAnalyticsControllerTests
{
    [Fact]
    public async Task GetItems_ForwardsFilters()
    {
        var service = new Mock<IBuildResourceAnalyticsService>();
        service.Setup(x => x.GetItemsAsync("KR", "16.14", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BuildResourceAnalyticsIndexResponse("item", "16.14", "KR", 0, []));
        var controller = new BuildResourceAnalyticsController(service.Object);

        var result = await controller.GetItems("KR", "16.14", CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        service.Verify(x => x.GetItemsAsync("KR", "16.14", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetRune_ReturnsNotFoundWhenResourceIsAbsent()
    {
        var service = new Mock<IBuildResourceAnalyticsService>();
        service.Setup(x => x.GetRuneAsync(9999, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((BuildResourceAnalyticsDetailResponse?)null);
        var controller = new BuildResourceAnalyticsController(service.Object);

        var result = await controller.GetRune(9999, null, null, CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
    }
}
