using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Transcendence.Service.Core.Services.Analytics.Interfaces;
using Transcendence.Service.Core.Services.Analytics.Models;
using Transcendence.WebAPI.Controllers;

namespace Transcendence.WebAPI.Tests;

public sealed class SavedBuildsControllerTests
{
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SavedBuildId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ShareId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    // ---- reads ----

    [Fact]
    public async Task List_ForwardsThePagingWindowForTheAuthenticatedUser()
    {
        var service = new Mock<ISavedBuildService>();
        var page = new SavedBuildListDto([Build()], 2, 25, 30, false);
        service
            .Setup(x => x.ListAsync(UserId, 2, 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync(page);
        var controller = BuildController(service.Object);

        var result = await controller.List(2, 25, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeSameAs(page);
    }

    // ---- create ----

    [Fact]
    public async Task Create_ReturnsCreatedWithTheStoredBuild()
    {
        var service = new Mock<ISavedBuildService>();
        var created = Build();
        service
            .Setup(x => x.CreateAsync(UserId, It.IsAny<SaveBuildRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(created);
        var controller = BuildController(service.Object);

        var result = await controller.Create(Request(), CancellationToken.None);

        var response = result.Should().BeOfType<CreatedAtActionResult>().Subject;
        response.StatusCode.Should().Be(StatusCodes.Status201Created);
        response.Value.Should().BeSameAs(created);
    }

    [Fact]
    public async Task Create_RejectedRequest_AnswersWithProblemDetailsRatherThanABareString()
    {
        var controller = BuildController(CreateThrows(new ArgumentException("Name is required.", "request")));

        var result = await controller.Create(Request(), CancellationToken.None);

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        badRequest.Value.Should().BeAssignableTo<ProblemDetails>();
        badRequest.Value.Should().NotBeOfType<string>();
    }

    [Fact]
    public async Task Create_RejectedRequest_StripsTheParameterNameSuffixFromTheDetail()
    {
        var controller = BuildController(CreateThrows(new ArgumentException("Name is required.", "request")));

        var result = await controller.Create(Request(), CancellationToken.None);

        var problem = ProblemFrom(result);
        problem.Detail.Should().Be("Name is required.");
        problem.Detail.Should().NotContain("Parameter");
        problem.Status.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task Create_RejectedRequest_IsWrittenAsApplicationProblemJson()
    {
        var controller = BuildController(CreateThrows(new ArgumentException("Name is required.", "request")));

        var result = await controller.Create(Request(), CancellationToken.None);
        var response = await ActionResultExecution.ExecuteAsync(result);

        response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        response.ContentType.Should().StartWith("application/problem+json");
        response.Body.Should().Contain("\"title\"").And.Contain("\"status\"").And.Contain("\"detail\"");
        response.Body.Should().NotContain("Parameter");
    }

    [Fact]
    public async Task Create_OverTheAccountLimit_AnswersWithAConflictProblemDetails()
    {
        var controller = BuildController(CreateThrows(new SavedBuildLimitExceededException(200)));

        var result = await controller.Create(Request(), CancellationToken.None);

        var conflict = result.Should().BeOfType<ObjectResult>().Subject;
        conflict.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        var problem = conflict.Value.Should().BeAssignableTo<ProblemDetails>().Subject;
        problem.Detail.Should().Contain("200");
    }

    // ---- update ----

    [Fact]
    public async Task Update_ReturnsTheUpdatedBuild()
    {
        var service = new Mock<ISavedBuildService>();
        var updated = Build();
        service
            .Setup(x => x.UpdateAsync(UserId, SavedBuildId, It.IsAny<SaveBuildRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(updated);
        var controller = BuildController(service.Object);

        var result = await controller.Update(SavedBuildId, Request(), CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeSameAs(updated);
    }

    [Fact]
    public async Task Update_ReturnsNotFound_WhenTheBuildBelongsToSomeoneElseOrIsGone()
    {
        var service = new Mock<ISavedBuildService>();
        service
            .Setup(x => x.UpdateAsync(UserId, SavedBuildId, It.IsAny<SaveBuildRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SavedBuildDto?)null);
        var controller = BuildController(service.Object);

        var result = await controller.Update(SavedBuildId, Request(), CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task Update_RejectedRequest_StripsTheParameterNameSuffixFromTheDetail()
    {
        var service = new Mock<ISavedBuildService>();
        service
            .Setup(x => x.UpdateAsync(UserId, SavedBuildId, It.IsAny<SaveBuildRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("Item path may not repeat an item.", "request"));
        var controller = BuildController(service.Object);

        var result = await controller.Update(SavedBuildId, Request(), CancellationToken.None);

        var problem = ProblemFrom(result);
        problem.Detail.Should().Be("Item path may not repeat an item.");
        problem.Detail.Should().NotContain("Parameter");
    }

    /// <summary>
    /// The PUT previously declared only 200/404 while already returning 400 for a rejected body, so the
    /// generated TypeScript client had no typed branch for the validation failure. The 401 the action can
    /// also emit is contributed by the UserOnly policy (pinned in <see cref="EndpointAuthorizationPolicyTests"/>)
    /// and is left undeclared across the JWT surface, matching UserPreferencesController.
    /// </summary>
    [Fact]
    public void Update_DeclaresEveryBodyCarryingStatusItCanReturn()
    {
        var declared = ResponseTypeContractAssertions.DeclaredResponses<SavedBuildsController>(
            nameof(SavedBuildsController.Update));

        declared.Should().BeEquivalentTo(new Dictionary<int, Type>
        {
            [StatusCodes.Status200OK] = typeof(SavedBuildDto),
            [StatusCodes.Status400BadRequest] = typeof(ValidationProblemDetails),
            [StatusCodes.Status404NotFound] = typeof(void),
            // Declared once at controller level: every action answers 401 from its own claim guard.
            [StatusCodes.Status401Unauthorized] = typeof(void)
        });
    }

    [Fact]
    public void Create_DeclaresEveryBodyCarryingStatusItCanReturn()
    {
        var declared = ResponseTypeContractAssertions.DeclaredResponses<SavedBuildsController>(
            nameof(SavedBuildsController.Create));

        declared.Should().BeEquivalentTo(new Dictionary<int, Type>
        {
            [StatusCodes.Status201Created] = typeof(SavedBuildDto),
            [StatusCodes.Status400BadRequest] = typeof(ValidationProblemDetails),
            [StatusCodes.Status409Conflict] = typeof(ProblemDetails),
            [StatusCodes.Status401Unauthorized] = typeof(void)
        });
    }

    [Fact]
    public void Repair_DeclaresTheValidationBranchItCanReturn()
    {
        var declared = ResponseTypeContractAssertions.DeclaredResponses<SavedBuildsController>(
            nameof(SavedBuildsController.Repair));

        declared.Should().BeEquivalentTo(new Dictionary<int, Type>
        {
            [StatusCodes.Status200OK] = typeof(SavedBuildDto),
            [StatusCodes.Status400BadRequest] = typeof(ValidationProblemDetails),
            [StatusCodes.Status404NotFound] = typeof(void),
            [StatusCodes.Status401Unauthorized] = typeof(void)
        });
    }

    // ---- repair ----

    [Fact]
    public async Task Repair_ReturnsTheRepairedBuild()
    {
        var service = new Mock<ISavedBuildService>();
        var repaired = Build();
        service
            .Setup(x => x.RepairAsync(
                UserId, SavedBuildId, It.IsAny<SavedBuildRepairRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(repaired);
        var controller = BuildController(service.Object);

        var result = await controller.Repair(
            SavedBuildId, new SavedBuildRepairRequest([new SavedBuildRepairChoice(3006, "DROP", null)]),
            CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeSameAs(repaired);
    }

    [Fact]
    public async Task Repair_RejectedChoice_StripsTheParameterNameSuffixFromTheDetail()
    {
        var service = new Mock<ISavedBuildService>();
        service
            .Setup(x => x.RepairAsync(
                UserId, SavedBuildId, It.IsAny<SavedBuildRepairRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("REPLACE requires a replacement item.", "request"));
        var controller = BuildController(service.Object);

        var result = await controller.Repair(
            SavedBuildId, new SavedBuildRepairRequest(null), CancellationToken.None);

        ProblemFrom(result).Detail.Should().Be("REPLACE requires a replacement item.");
    }

    [Fact]
    public async Task Repair_ReturnsNotFound_WhenTheBuildIsNotTheCallers()
    {
        var service = new Mock<ISavedBuildService>();
        service
            .Setup(x => x.RepairAsync(
                UserId, SavedBuildId, It.IsAny<SavedBuildRepairRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SavedBuildDto?)null);
        var controller = BuildController(service.Object);

        var result = await controller.Repair(
            SavedBuildId, new SavedBuildRepairRequest(null), CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
    }

    // ---- delete: idempotent ----

    [Fact]
    public async Task Delete_ReturnsNoContent_WhenTheBuildWasRemoved()
    {
        var service = new Mock<ISavedBuildService>();
        service
            .Setup(x => x.DeleteAsync(UserId, SavedBuildId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var controller = BuildController(service.Object);

        var result = await controller.Delete(SavedBuildId, CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
    }

    [Fact]
    public async Task Delete_ReturnsNoContent_WhenThereWasNothingLeftToRemove()
    {
        var service = new Mock<ISavedBuildService>();
        service
            .Setup(x => x.DeleteAsync(UserId, SavedBuildId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var controller = BuildController(service.Object);

        var result = await controller.Delete(SavedBuildId, CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
        result.Should().NotBeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task Delete_IsIdempotentAcrossRepeatedCalls()
    {
        var service = new Mock<ISavedBuildService>();
        var callCount = 0;
        service
            .Setup(x => x.DeleteAsync(UserId, SavedBuildId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => ++callCount == 1);
        var controller = BuildController(service.Object);

        var first = await controller.Delete(SavedBuildId, CancellationToken.None);
        var second = await controller.Delete(SavedBuildId, CancellationToken.None);
        var third = await controller.Delete(SavedBuildId, CancellationToken.None);

        first.Should().BeOfType<NoContentResult>();
        second.Should().BeOfType<NoContentResult>();
        third.Should().BeOfType<NoContentResult>();
        service.Verify(
            x => x.DeleteAsync(UserId, SavedBuildId, It.IsAny<CancellationToken>()),
            Times.Exactly(3));
    }

    [Fact]
    public void Delete_DeclaresOnlyTheNoContentBranch()
    {
        var declared = ResponseTypeContractAssertions.DeclaredResponses<SavedBuildsController>(
            nameof(SavedBuildsController.Delete));

        declared.Keys.Should().BeEquivalentTo(
            [StatusCodes.Status204NoContent, StatusCodes.Status401Unauthorized]);
        declared.Should().NotContainKey(
            StatusCodes.Status404NotFound, "the delete is idempotent, so it never answers 404");
    }

    // ---- share ----

    [Fact]
    public async Task Share_ReturnsTheShareToken()
    {
        var service = new Mock<ISavedBuildService>();
        service
            .Setup(x => x.ShareAsync(UserId, SavedBuildId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SavedBuildShareDto(ShareId));
        var controller = BuildController(service.Object);

        var result = await controller.Share(SavedBuildId, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeOfType<SavedBuildShareDto>().Subject.ShareId.Should().Be(ShareId);
    }

    [Fact]
    public async Task Share_ReturnsNotFound_WhenTheBuildIsNotTheCallers()
    {
        var service = new Mock<ISavedBuildService>();
        service
            .Setup(x => x.ShareAsync(UserId, SavedBuildId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SavedBuildShareDto?)null);
        var controller = BuildController(service.Object);

        var result = await controller.Share(SavedBuildId, CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task RevokeShare_ReturnsNoContent_WhenTheTokenWasRevoked()
    {
        var service = new Mock<ISavedBuildService>();
        service
            .Setup(x => x.RevokeShareAsync(UserId, SavedBuildId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var controller = BuildController(service.Object);

        var result = await controller.RevokeShare(SavedBuildId, CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
    }

    [Fact]
    public async Task RevokeShare_ReturnsNotFound_WhenThereWasNoShareToRevoke()
    {
        var service = new Mock<ISavedBuildService>();
        service
            .Setup(x => x.RevokeShareAsync(UserId, SavedBuildId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var controller = BuildController(service.Object);

        var result = await controller.RevokeShare(SavedBuildId, CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
    }

    // ---- claim handling: a JWT that passed the policy but carries no usable subject ----

    [Fact]
    public async Task List_ReturnsUnauthorized_WhenTheSubjectClaimIsNotAGuid()
    {
        var service = new Mock<ISavedBuildService>();
        var controller = BuildController(service.Object, subject: "not-a-guid");

        var result = await controller.List(null, null, CancellationToken.None);

        result.Should().BeOfType<UnauthorizedResult>();
        service.Verify(
            x => x.ListAsync(It.IsAny<Guid>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Delete_ReturnsUnauthorizedWithoutTouchingTheStore_WhenTheSubjectClaimIsMissing()
    {
        var service = new Mock<ISavedBuildService>(MockBehavior.Strict);
        var controller = BuildController(service.Object, subject: null);

        var result = await controller.Delete(SavedBuildId, CancellationToken.None);

        result.Should().BeOfType<UnauthorizedResult>();
        service.VerifyNoOtherCalls();
    }

    // ---- helpers ----

    private static ISavedBuildService CreateThrows(Exception exception)
    {
        var service = new Mock<ISavedBuildService>();
        service
            .Setup(x => x.CreateAsync(UserId, It.IsAny<SaveBuildRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);
        return service.Object;
    }

    private static ProblemDetails ProblemFrom(IActionResult result) =>
        result
            .Should().BeOfType<BadRequestObjectResult>().Subject
            .Value.Should().BeAssignableTo<ProblemDetails>().Subject;

    private static SavedBuildsController BuildController(
        ISavedBuildService service,
        string? subject = "default")
    {
        var claims = new List<Claim>();
        if (subject == "default")
            claims.Add(new Claim(ClaimTypes.NameIdentifier, UserId.ToString()));
        else if (subject != null)
            claims.Add(new Claim(ClaimTypes.NameIdentifier, subject));

        var httpContext = new DefaultHttpContext
        {
            RequestServices = MvcServices.Instance,
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))
        };

        return new SavedBuildsController(service)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
    }

    private static SaveBuildRequest Request() =>
        new("Lethality rush", 103, "MIDDLE", null, "16.14", "KR", "supported", [3006], [8010], 4, 14);

    private static SavedBuildDto Build() =>
        new(
            SavedBuildId,
            "Lethality rush",
            103,
            "MIDDLE",
            null,
            "16.14",
            "KR",
            "supported",
            [3006],
            [8010],
            4,
            14,
            null,
            null,
            false,
            "CURRENT",
            [],
            [],
            null,
            new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 7, 2, 0, 0, 0, DateTimeKind.Utc));
}
