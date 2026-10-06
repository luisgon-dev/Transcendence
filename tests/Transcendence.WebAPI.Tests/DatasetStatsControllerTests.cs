using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Transcendence.Service.Core.Services.Analytics.Interfaces;
using Transcendence.Service.Core.Services.Analytics.Models;
using Transcendence.WebAPI.Controllers;

namespace Transcendence.WebAPI.Tests;

public class DatasetStatsControllerTests
{
    [Fact]
    public async Task Get_ReturnsTheStoredSnapshot_WithAShortPublicCacheLifetime()
    {
        var stats = new DatasetStatsDto(
            MatchesStored: 567_944,
            MatchesLast24Hours: 21_325,
            MatchesPerDayLast7Days: 23_482,
            ActivePatch: "16.19",
            ActivePatchMatches: 234_302,
            PlayersIndexedEstimate: 4_803_099,
            DatabaseSizeBytes: 302L * 1024 * 1024 * 1024,
            CrawledPlatforms: ["NA1", "EUW1"],
            Platforms: [new DatasetPlatformStatsDto("NA1", "North America", 83_027, 2_624)],
            LastMatchIngestedAtUtc: new DateTime(2026, 10, 6, 6, 46, 30, DateTimeKind.Utc),
            ComputedAtUtc: new DateTime(2026, 10, 6, 6, 50, 0, DateTimeKind.Utc));
        var service = new Mock<IDatasetStatsService>();
        service.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(stats);
        var controller = CreateController(service.Object);

        var result = await controller.Get(CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(stats);
        controller.Response.Headers.CacheControl.ToString()
            .Should().Be("public, max-age=60, stale-while-revalidate=300");
    }

    [Fact]
    public async Task Get_ReturnsNotFound_BeforeTheWorkerHasComputedASnapshot()
    {
        var service = new Mock<IDatasetStatsService>();
        service.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync((DatasetStatsDto?)null);
        var controller = CreateController(service.Object);

        var result = await controller.Get(CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
        controller.Response.Headers.CacheControl.Should().BeEmpty("a missing snapshot must not be cached downstream");
    }

    [Fact]
    public void Get_DeclaresEveryStatusItReturns()
    {
        var responses = ResponseTypeContractAssertions.DeclaredResponses<DatasetStatsController>(
            nameof(DatasetStatsController.Get));

        responses.Should().ContainKey(StatusCodes.Status200OK).WhoseValue.Should().Be(typeof(DatasetStatsDto));
        responses.Should().ContainKey(StatusCodes.Status404NotFound).WhoseValue.Should().Be(typeof(ProblemDetails));
        responses.Should().ContainKey(StatusCodes.Status429TooManyRequests);
    }

    private static DatasetStatsController CreateController(IDatasetStatsService service) =>
        new(service)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
}
