using System.Data.Common;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Transcendence.Service.Core.Services.Analytics.Interfaces;
using Transcendence.Service.Core.Services.Analytics.Models;
using Transcendence.Service.Core.Services.Auth.Interfaces;
using Transcendence.Service.Core.Services.Auth.Models;
using Transcendence.WebAPI.Security;

namespace Transcendence.WebAPI.Controllers;

[ApiController]
[Route("api/admin/analytics/build-lab")]
[Authorize(Policy = AuthPolicies.AdminOnly)]
public sealed class AdminBuildLabController(
    IBuildLabGenerationCoordinator coordinator,
    IAdminAuditService adminAuditService) : ControllerBase
{
    private const string UniqueViolationSqlState = "23505";
    private const string GenerationTargetType = "build-lab-generation";

    [HttpGet]
    [ProducesResponseType(typeof(BuildLabGenerationAdminResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken ct) =>
        Ok(await coordinator.GetAdminStatusAsync(ct));

    [HttpPost("generations/{generationId:guid}/promote")]
    [EnableRateLimiting("admin-write")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Promote(Guid generationId, CancellationToken ct)
    {
        bool promoted;
        try
        {
            promoted = await coordinator.PromoteCandidateAsync(generationId, ActorEmail(), ct);
        }
        catch (Exception ex) when (IsUniqueViolation(ex))
        {
            await WriteAuditAsync("analytics.buildlab.promote", generationId, false,
                new { error = "A competing promotion already owns the active pointer." }, ct);
            return Conflict("Another generation was promoted concurrently. Retry once it settles.");
        }

        await WriteAuditAsync("analytics.buildlab.promote", generationId, promoted,
            promoted ? null : new { error = "The generation is not a valid candidate or failed its gates." }, ct);
        return promoted
            ? NoContent()
            : Conflict("The generation is not a valid candidate or did not pass its quality gates.");
    }

    [HttpPost("generations/{generationId:guid}/rollback")]
    [EnableRateLimiting("admin-write")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Rollback(Guid generationId, CancellationToken ct)
    {
        bool rolledBack;
        try
        {
            rolledBack = await coordinator.RollbackAsync(generationId, ActorEmail(), ct);
        }
        catch (Exception ex) when (IsUniqueViolation(ex))
        {
            await WriteAuditAsync("analytics.buildlab.rollback", generationId, false,
                new { error = "A competing promotion already owns the active pointer." }, ct);
            return Conflict("Another generation was promoted concurrently. Retry once it settles.");
        }

        await WriteAuditAsync("analytics.buildlab.rollback", generationId, rolledBack,
            rolledBack ? null : new { error = "The generation cannot be made active." }, ct);
        return rolledBack ? NoContent() : NotFound();
    }

    [HttpPost("generations/{generationId:guid}/fail")]
    [EnableRateLimiting("admin-write")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Fail(
        Guid generationId,
        [FromBody] BuildLabFailGenerationRequest? request,
        CancellationToken ct)
    {
        var reason = string.IsNullOrWhiteSpace(request?.Reason) ? null : request.Reason.Trim();
        var failed = await coordinator.FailGenerationAsync(generationId, reason, ActorEmail(), ct);
        await WriteAuditAsync("analytics.buildlab.fail", generationId, failed, new { reason }, ct);
        return failed
            ? NoContent()
            : NotFound();
    }

    private static bool IsUniqueViolation(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            if (current is DbException { SqlState: UniqueViolationSqlState })
                return true;
        }

        return false;
    }

    private string? ActorEmail() => User.FindFirstValue(ClaimTypes.Name);

    private async Task WriteAuditAsync(
        string action,
        Guid generationId,
        bool isSuccess,
        object? metadata,
        CancellationToken ct)
    {
        var actorId = TryGetGuidClaim(ClaimTypes.NameIdentifier);
        var requestId = Request.Headers["x-trn-request-id"].ToString();
        await adminAuditService.WriteAsync(new AdminAuditWriteRequest(
            ActorUserAccountId: actorId,
            ActorEmail: ActorEmail(),
            Action: action,
            TargetType: GenerationTargetType,
            TargetId: generationId.ToString(),
            RequestId: string.IsNullOrWhiteSpace(requestId) ? null : requestId,
            IsSuccess: isSuccess,
            Metadata: metadata
        ), ct);
    }

    private Guid? TryGetGuidClaim(string claimType)
    {
        var value = User.FindFirstValue(claimType);
        return Guid.TryParse(value, out var parsed) ? parsed : null;
    }
}
