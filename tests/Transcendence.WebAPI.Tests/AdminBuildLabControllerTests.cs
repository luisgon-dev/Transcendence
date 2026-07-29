using System.Data.Common;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Transcendence.Service.Core.Services.Analytics.Interfaces;
using Transcendence.Service.Core.Services.Analytics.Models;
using Transcendence.Service.Core.Services.Auth.Interfaces;
using Transcendence.Service.Core.Services.Auth.Models;
using Transcendence.WebAPI.Controllers;

namespace Transcendence.WebAPI.Tests;

public sealed class AdminBuildLabControllerTests
{
    private const string ActorEmail = "admin@test.local";
    private static readonly Guid ActorId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid GenerationId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    [Fact]
    public async Task Get_ReturnsTheGenerationLedger()
    {
        var coordinator = new Mock<IBuildLabGenerationCoordinator>();
        var status = new BuildLabGenerationAdminResponse([], 12, 4);
        coordinator
            .Setup(x => x.GetAdminStatusAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(status);
        var controller = BuildController(coordinator.Object, out _);

        var result = await controller.Get(CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeSameAs(status);
    }

    // ---- promote ----

    [Fact]
    public async Task Promote_ReturnsNoContentAndAuditsSuccess_WhenTheCandidateIsPromoted()
    {
        var coordinator = new Mock<IBuildLabGenerationCoordinator>();
        coordinator
            .Setup(x => x.PromoteCandidateAsync(GenerationId, ActorEmail, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var controller = BuildController(coordinator.Object, out var audit);

        var result = await controller.Promote(GenerationId, CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
        audit.Verify(
            x => x.WriteAsync(
                It.Is<AdminAuditWriteRequest>(request =>
                    request.Action == "analytics.buildlab.promote" &&
                    request.TargetType == "build-lab-generation" &&
                    request.TargetId == GenerationId.ToString() &&
                    request.ActorEmail == ActorEmail &&
                    request.ActorUserAccountId == ActorId &&
                    request.RequestId == "req-123" &&
                    request.IsSuccess),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Promote_ReturnsConflictAndAuditsFailure_WhenTheCandidateFailsItsGates()
    {
        var coordinator = new Mock<IBuildLabGenerationCoordinator>();
        coordinator
            .Setup(x => x.PromoteCandidateAsync(GenerationId, ActorEmail, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var controller = BuildController(coordinator.Object, out var audit);

        var result = await controller.Promote(GenerationId, CancellationToken.None);

        result.Should().BeOfType<ConflictObjectResult>().Subject
            .StatusCode.Should().Be(StatusCodes.Status409Conflict);
        audit.Verify(
            x => x.WriteAsync(
                It.Is<AdminAuditWriteRequest>(request =>
                    request.Action == "analytics.buildlab.promote" && !request.IsSuccess),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Promote_ReturnsConflict_WhenAConcurrentPromotionWinsThePointer()
    {
        var coordinator = new Mock<IBuildLabGenerationCoordinator>();
        coordinator
            .Setup(x => x.PromoteCandidateAsync(GenerationId, ActorEmail, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UniqueViolationDbException());
        var controller = BuildController(coordinator.Object, out var audit);

        var result = await controller.Promote(GenerationId, CancellationToken.None);

        result.Should().BeOfType<ConflictObjectResult>();
        audit.Verify(
            x => x.WriteAsync(
                It.Is<AdminAuditWriteRequest>(request =>
                    request.Action == "analytics.buildlab.promote" && !request.IsSuccess),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Promote_ReturnsConflict_WhenTheUniqueViolationIsWrappedByEfCore()
    {
        var coordinator = new Mock<IBuildLabGenerationCoordinator>();
        coordinator
            .Setup(x => x.PromoteCandidateAsync(GenerationId, ActorEmail, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("update failed", new UniqueViolationDbException()));
        var controller = BuildController(coordinator.Object, out _);

        var result = await controller.Promote(GenerationId, CancellationToken.None);

        result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public async Task Promote_LetsUnrelatedDatabaseFailuresSurface()
    {
        var coordinator = new Mock<IBuildLabGenerationCoordinator>();
        coordinator
            .Setup(x => x.PromoteCandidateAsync(GenerationId, ActorEmail, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("statement timeout"));
        var controller = BuildController(coordinator.Object, out _);

        await Assert.ThrowsAsync<TimeoutException>(
            () => controller.Promote(GenerationId, CancellationToken.None));
    }

    [Fact]
    public async Task Promote_ConflictBodyIsNormalizedToProblemDetails()
    {
        var coordinator = new Mock<IBuildLabGenerationCoordinator>();
        coordinator
            .Setup(x => x.PromoteCandidateAsync(GenerationId, ActorEmail, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var controller = BuildController(coordinator.Object, out _);

        var result = await controller.Promote(GenerationId, CancellationToken.None);
        var response = await ActionResultExecution.ExecuteAsync(result);

        response.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        response.ContentType.Should().StartWith("application/problem+json");
        response.Body.Should().Contain("\"detail\"");
    }

    // ---- rollback ----

    [Fact]
    public async Task Rollback_ReturnsNoContent_WhenThePreviousGenerationIsRestored()
    {
        var coordinator = new Mock<IBuildLabGenerationCoordinator>();
        coordinator
            .Setup(x => x.RollbackAsync(GenerationId, ActorEmail, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var controller = BuildController(coordinator.Object, out var audit);

        var result = await controller.Rollback(GenerationId, CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
        audit.Verify(
            x => x.WriteAsync(
                It.Is<AdminAuditWriteRequest>(request =>
                    request.Action == "analytics.buildlab.rollback" && request.IsSuccess),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Rollback_ReturnsNotFound_WhenTheGenerationCannotBeMadeActive()
    {
        var coordinator = new Mock<IBuildLabGenerationCoordinator>();
        coordinator
            .Setup(x => x.RollbackAsync(GenerationId, ActorEmail, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var controller = BuildController(coordinator.Object, out var audit);

        var result = await controller.Rollback(GenerationId, CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
        audit.Verify(
            x => x.WriteAsync(
                It.Is<AdminAuditWriteRequest>(request =>
                    request.Action == "analytics.buildlab.rollback" && !request.IsSuccess),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Rollback_ReturnsConflict_WhenAConcurrentPromotionWinsThePointer()
    {
        var coordinator = new Mock<IBuildLabGenerationCoordinator>();
        coordinator
            .Setup(x => x.RollbackAsync(GenerationId, ActorEmail, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UniqueViolationDbException());
        var controller = BuildController(coordinator.Object, out _);

        var result = await controller.Rollback(GenerationId, CancellationToken.None);

        result.Should().BeOfType<ConflictObjectResult>();
    }

    // ---- fail ----

    [Fact]
    public async Task Fail_TrimsTheSuppliedReason()
    {
        var coordinator = new Mock<IBuildLabGenerationCoordinator>();
        coordinator
            .Setup(x => x.FailGenerationAsync(
                GenerationId, "modeler crashed", ActorEmail, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var controller = BuildController(coordinator.Object, out var audit);

        var result = await controller.Fail(
            GenerationId, new BuildLabFailGenerationRequest("  modeler crashed  "), CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
        coordinator.Verify(
            x => x.FailGenerationAsync(
                GenerationId, "modeler crashed", ActorEmail, It.IsAny<CancellationToken>()),
            Times.Once);
        audit.Verify(
            x => x.WriteAsync(
                It.Is<AdminAuditWriteRequest>(request =>
                    request.Action == "analytics.buildlab.fail" && request.IsSuccess),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Fail_NormalizesABlankReasonToNull(string? reason)
    {
        var coordinator = new Mock<IBuildLabGenerationCoordinator>();
        coordinator
            .Setup(x => x.FailGenerationAsync(GenerationId, null, ActorEmail, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var controller = BuildController(coordinator.Object, out _);

        var result = await controller.Fail(
            GenerationId, new BuildLabFailGenerationRequest(reason), CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
        coordinator.Verify(
            x => x.FailGenerationAsync(GenerationId, null, ActorEmail, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Fail_AcceptsAnAbsentBody()
    {
        var coordinator = new Mock<IBuildLabGenerationCoordinator>();
        coordinator
            .Setup(x => x.FailGenerationAsync(GenerationId, null, ActorEmail, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var controller = BuildController(coordinator.Object, out _);

        var result = await controller.Fail(GenerationId, null, CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
    }

    [Fact]
    public async Task Fail_ReturnsNotFound_WhenTheGenerationIsAlreadySettled()
    {
        var coordinator = new Mock<IBuildLabGenerationCoordinator>();
        coordinator
            .Setup(x => x.FailGenerationAsync(
                GenerationId, It.IsAny<string?>(), ActorEmail, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var controller = BuildController(coordinator.Object, out var audit);

        var result = await controller.Fail(
            GenerationId, new BuildLabFailGenerationRequest("stale"), CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
        audit.Verify(
            x => x.WriteAsync(
                It.Is<AdminAuditWriteRequest>(request =>
                    request.Action == "analytics.buildlab.fail" && !request.IsSuccess),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ---- actor attribution ----

    [Fact]
    public async Task Promote_AuditsWithoutAnActorId_WhenTheSubjectClaimIsNotAGuid()
    {
        var coordinator = new Mock<IBuildLabGenerationCoordinator>();
        coordinator
            .Setup(x => x.PromoteCandidateAsync(GenerationId, ActorEmail, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var audit = new Mock<IAdminAuditService>();
        var controller = BuildController(coordinator.Object, audit, subjectClaim: "not-a-guid", requestId: null);

        await controller.Promote(GenerationId, CancellationToken.None);

        audit.Verify(
            x => x.WriteAsync(
                It.Is<AdminAuditWriteRequest>(request =>
                    request.ActorUserAccountId == null &&
                    request.RequestId == null &&
                    request.ActorEmail == ActorEmail),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ---- helpers ----

    private static AdminBuildLabController BuildController(
        IBuildLabGenerationCoordinator coordinator,
        out Mock<IAdminAuditService> audit)
    {
        audit = new Mock<IAdminAuditService>();
        return BuildController(coordinator, audit);
    }

    private static AdminBuildLabController BuildController(
        IBuildLabGenerationCoordinator coordinator,
        Mock<IAdminAuditService> audit,
        string? subjectClaim = null,
        string? requestId = "req-123")
    {
        audit
            .Setup(x => x.WriteAsync(It.IsAny<AdminAuditWriteRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var httpContext = new DefaultHttpContext
        {
            RequestServices = MvcServices.Instance,
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, subjectClaim ?? ActorId.ToString()),
                new Claim(ClaimTypes.Name, ActorEmail)
            ], "test"))
        };
        if (requestId != null)
            httpContext.Request.Headers["x-trn-request-id"] = requestId;

        return new AdminBuildLabController(coordinator, audit.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
    }

    /// <summary>Stands in for Npgsql's unique-violation error (SQLSTATE 23505) without a live database.</summary>
    private sealed class UniqueViolationDbException() : DbException("duplicate key value violates unique constraint")
    {
        public override string? SqlState => "23505";
    }
}
