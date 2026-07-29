using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Transcendence.Service.Core.Services.Analytics.Interfaces;
using Transcendence.Service.Core.Services.Analytics.Models;
using Transcendence.WebAPI.Controllers;

namespace Transcendence.WebAPI.Tests;

public sealed class BuildLabAnalyticsControllerTests
{
    [Fact]
    public async Task Get_ForwardsEveryFilterToTheServiceAndDefaultsEmptySelections()
    {
        var service = new Mock<IBuildLabService>();
        var response = BuildResponse();
        service
            .Setup(x => x.GetAsync(
                It.Is<BuildLabQuery>(query =>
                    query.ChampionId == 103 &&
                    query.Role == "MIDDLE" &&
                    query.OpponentChampionId == 64 &&
                    query.Patch == "16.14" &&
                    query.Region == "KR" &&
                    query.Section == "items" &&
                    query.Mode == "supported" &&
                    query.ItemPath.Count == 0 &&
                    query.RuneSelections.Count == 0 &&
                    query.SpellPair.Count == 0),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);
        var controller = BuildController(service.Object);

        var result = await controller.Get(
            103, "MIDDLE", 64, "16.14", "KR", "items", "supported", null, null, null, CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeSameAs(response);
    }

    [Fact]
    public async Task Get_ForwardsSuppliedSelectionArrays()
    {
        var service = new Mock<IBuildLabService>();
        service
            .Setup(x => x.GetAsync(It.IsAny<BuildLabQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildResponse());
        var controller = BuildController(service.Object);

        await controller.Get(
            103, "MIDDLE", null, null, null, "runes", "raw", [3006, 3153], [8010], [4, 14],
            CancellationToken.None);

        service.Verify(
            x => x.GetAsync(
                It.Is<BuildLabQuery>(query =>
                    query.Section == "runes" &&
                    query.Mode == "raw" &&
                    query.ItemPath.SequenceEqual(new[] { 3006, 3153 }) &&
                    query.RuneSelections.SequenceEqual(new[] { 8010 }) &&
                    query.SpellPair.SequenceEqual(new[] { 4, 14 })),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Get_RejectedQuery_AnswersWithProblemDetailsRatherThanABareString()
    {
        var controller = BuildController(ThrowingService(
            new ArgumentException("Unsupported section 'runez'.", "query")));

        var result = await controller.Get(
            103, "MIDDLE", null, null, null, "runez", "supported", null, null, null, CancellationToken.None);

        var badRequest = result.Result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        badRequest.Value.Should().BeAssignableTo<ProblemDetails>();
        badRequest.Value.Should().NotBeOfType<string>();
    }

    [Fact]
    public async Task Get_RejectedQuery_StripsTheParameterNameSuffixFromTheDetail()
    {
        var controller = BuildController(ThrowingService(
            new ArgumentException("Unsupported section 'runez'.", "query")));

        var result = await controller.Get(
            103, "MIDDLE", null, null, null, "runez", "supported", null, null, null, CancellationToken.None);

        var problem = ProblemFrom(result);
        problem.Detail.Should().Be("Unsupported section 'runez'.");
        problem.Detail.Should().NotContain("Parameter");
        problem.Status.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task Get_RejectedQueryWithoutParameterName_KeepsTheMessageIntact()
    {
        var controller = BuildController(ThrowingService(new ArgumentException("Role is required.")));

        var result = await controller.Get(
            103, "", null, null, null, "items", "supported", null, null, null, CancellationToken.None);

        ProblemFrom(result).Detail.Should().Be("Role is required.");
    }

    [Fact]
    public async Task Get_RejectedQuery_DoesNotLeakTheParameterNameForAnyArgumentExceptionSubtype()
    {
        var controller = BuildController(ThrowingService(
            new ArgumentOutOfRangeException("query", "Champion id must be positive.")));

        var result = await controller.Get(
            -1, "MIDDLE", null, null, null, "items", "supported", null, null, null, CancellationToken.None);

        var problem = ProblemFrom(result);
        problem.Detail.Should().Be("Champion id must be positive.");
        problem.Detail.Should().NotContain("query");
    }

    [Fact]
    public async Task Get_RejectedQuery_IsWrittenAsApplicationProblemJson()
    {
        var controller = BuildController(ThrowingService(
            new ArgumentException("Unsupported section 'runez'.", "query")));

        var result = await controller.Get(
            103, "MIDDLE", null, null, null, "runez", "supported", null, null, null, CancellationToken.None);
        var response = await ActionResultExecution.ExecuteAsync(result.Result!);

        response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        response.ContentType.Should().StartWith("application/problem+json");
        response.Body.Should().Contain("\"title\"").And.Contain("\"status\"").And.Contain("\"detail\"");
        response.Body.Should().NotContain("Parameter");
    }

    [Fact]
    public void Get_DeclaresTheProblemDetailsBadRequestBranchForTheGeneratedClient()
    {
        var declared = ResponseTypeContractAssertions.DeclaredResponses<BuildLabAnalyticsController>(nameof(BuildLabAnalyticsController.Get));

        declared.Should().Contain(StatusCodes.Status200OK, typeof(BuildLabResponse));
        declared.Should().Contain(StatusCodes.Status400BadRequest, typeof(ValidationProblemDetails));
    }

    private static IBuildLabService ThrowingService(Exception exception)
    {
        var service = new Mock<IBuildLabService>();
        service
            .Setup(x => x.GetAsync(It.IsAny<BuildLabQuery>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);
        return service.Object;
    }

    private static ProblemDetails ProblemFrom(ActionResult<BuildLabResponse> result) =>
        result.Result
            .Should().BeOfType<BadRequestObjectResult>().Subject
            .Value.Should().BeAssignableTo<ProblemDetails>().Subject;

    private static BuildLabAnalyticsController BuildController(IBuildLabService service) =>
        new(service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { RequestServices = MvcServices.Instance }
            }
        };

    private static BuildLabResponse BuildResponse() =>
        new(
            Available: true,
            Context: new BuildLabContextDto(103, "MIDDLE", null, "16.14", "16.14", "KR", "KR", "items", "supported"),
            Provenance: new BuildLabProvenanceDto(
                Guid.Empty, "dataset-1", "model-1", "static-1", null, null, 0, "EMERALD_PLUS", [], []),
            SelectedPath: [],
            PathEstimate: null,
            Stages: [],
            UnavailableReason: null);
}
