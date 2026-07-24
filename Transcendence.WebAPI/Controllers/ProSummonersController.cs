using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Transcendence.Service.Core.Services.Auth.Interfaces;
using Transcendence.Service.Core.Services.Auth.Models;
using Transcendence.Service.Core.Services.ProSummoners.Interfaces;
using Transcendence.Service.Core.Services.Refresh.Interfaces;
using Transcendence.Service.Core.Services.RiotApi;
using Transcendence.Service.Core.Services.RiotApi.DTOs;
using Transcendence.WebAPI.Security;

namespace Transcendence.WebAPI.Controllers;

[ApiController]
[Route("api/admin/pro-summoners")]
[Authorize(Policy = AuthPolicies.AdminOnly)]
public class ProSummonersController(
    IAdminAuditService adminAuditService,
    ITrackedProSummonerService trackedProSummonerService,
    ISummonerRefreshCoordinator refreshCoordinator) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(List<TrackedProSummonerDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] bool? isActive = null, CancellationToken ct = default)
    {
        return Ok(await trackedProSummonerService.ListAsync(isActive, ct));
    }

    [HttpPost]
    [EnableRateLimiting("admin-write")]
    [ProducesResponseType(typeof(TrackedProSummonerDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] UpsertTrackedProSummonerRequest request,
        CancellationToken ct = default)
    {
        var outcome = await trackedProSummonerService.CreateAsync(request, ct);
        if (!outcome.IsSuccess)
            return BadRequest(outcome.ValidationError);

        var created = outcome.Value!;
        await WriteAuditAsync("pro-summoners.create", created.Id.ToString(), new
        {
            created.Puuid,
            created.PlatformRegion,
            created.ProName,
            created.TeamName,
            created.IsActive
        }, ct);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(TrackedProSummonerDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById([FromRoute] Guid id, CancellationToken ct = default)
    {
        var entity = await trackedProSummonerService.GetByIdAsync(id, ct);
        return entity == null ? NotFound() : Ok(entity);
    }

    [HttpPut("{id:guid}")]
    [EnableRateLimiting("admin-write")]
    [ProducesResponseType(typeof(TrackedProSummonerDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] UpsertTrackedProSummonerRequest request,
        CancellationToken ct = default)
    {
        var updated = await trackedProSummonerService.UpdateAsync(id, request, ct);
        if (updated == null)
            return NotFound();
        await WriteAuditAsync("pro-summoners.update", updated.Id.ToString(), new
        {
            updated.Puuid,
            updated.PlatformRegion,
            updated.ProName,
            updated.TeamName,
            updated.IsActive
        }, ct);
        return Ok(updated);
    }

    [HttpDelete("{id:guid}")]
    [EnableRateLimiting("admin-write")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete([FromRoute] Guid id, CancellationToken ct = default)
    {
        if (!await trackedProSummonerService.DeleteAsync(id, ct))
            return NotFound();
        await WriteAuditAsync("pro-summoners.delete", id.ToString(), null, ct);
        return NoContent();
    }

    [HttpGet("candidates")]
    [ProducesResponseType(typeof(List<ProPlayerDiscoveryCandidateDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListCandidates(
        [FromQuery] string status = "pending",
        CancellationToken ct = default)
    {
        return Ok(await trackedProSummonerService.ListCandidatesAsync(status, ct));
    }

    [HttpPost("candidates/{id:guid}/approve")]
    [EnableRateLimiting("admin-write")]
    [ProducesResponseType(typeof(TrackedProSummonerDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ApproveCandidate(
        [FromRoute] Guid id,
        [FromBody] ApproveProPlayerCandidateRequest request,
        CancellationToken ct = default)
    {
        var outcome = await trackedProSummonerService.ApproveCandidateAsync(id, request, ct);
        if (!outcome.IsSuccess)
            return BadRequest(outcome.ValidationError);

        var approved = outcome.Value!;
        await WriteAuditAsync("pro-summoners.candidate.approve", id.ToString(), new
        {
            approved.Id,
            approved.Puuid,
            approved.PlatformRegion,
            approved.ProName,
            approved.TeamName,
            approved.Source
        }, ct);
        return Ok(approved);
    }

    [HttpPost("candidates/{id:guid}/reject")]
    [EnableRateLimiting("admin-write")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RejectCandidate([FromRoute] Guid id, CancellationToken ct = default)
    {
        if (!await trackedProSummonerService.RejectCandidateAsync(id, ct))
            return NotFound();
        await WriteAuditAsync("pro-summoners.candidate.reject", id.ToString(), null, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/refresh")]
    [EnableRateLimiting("admin-write")]
    [ProducesResponseType(
        typeof(SummonerAcceptedResponse),
        StatusCodes.Status202Accepted,
        Description =
            "Accepted. Returns \"Refresh queued\" when the refresh lock is acquired, or \"Refresh in process\" with retryAfterSeconds when contention is detected.")]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Refresh([FromRoute] Guid id, CancellationToken ct = default)
    {
        var entity = await trackedProSummonerService.GetByIdAsync(id, ct);
        if (entity == null)
            return NotFound();

        if (string.IsNullOrWhiteSpace(entity.GameName) || string.IsNullOrWhiteSpace(entity.TagLine))
            return BadRequest("Cannot refresh: summoner has no gameName/tagLine.");

        if (!PlatformRouteParser.TryParse(entity.PlatformRegion, out var platform))
            return BadRequest($"Unsupported platform region '{entity.PlatformRegion}'.");

        var pollUrl = Url.ActionLink(nameof(GetById), null, new { id });

        var outcome = await refreshCoordinator.EnqueueRefreshAsync(
            entity.GameName,
            entity.TagLine,
            platform,
            pollUrl,
            HttpContext.TraceIdentifier,
            null,
            "pro-summoners-controller",
            ct);

        if (!outcome.WasQueued)
            return Accepted(new SummonerAcceptedResponse("Refresh in process", outcome.PollUrl, outcome.RetryAfterSeconds));

        await WriteAuditAsync("pro-summoners.refresh", entity.Id.ToString(), new
        {
            entity.Puuid,
            entity.PlatformRegion,
            entity.GameName,
            entity.TagLine
        }, ct);

        return Accepted(new SummonerAcceptedResponse(
            "Refresh queued",
            outcome.PollUrl));
    }

    private async Task WriteAuditAsync(string action, string targetId, object? metadata, CancellationToken ct)
    {
        var actorIdRaw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        Guid? actorId = Guid.TryParse(actorIdRaw, out var parsed) ? parsed : null;
        var actorEmail = User.FindFirstValue(ClaimTypes.Name);
        var requestId = Request.Headers["x-trn-request-id"].ToString();
        await adminAuditService.WriteAsync(new AdminAuditWriteRequest(
            actorId,
            actorEmail,
            action,
            "tracked-pro-summoner",
            targetId,
            string.IsNullOrWhiteSpace(requestId) ? null : requestId,
            true,
            metadata), ct);
    }
}
