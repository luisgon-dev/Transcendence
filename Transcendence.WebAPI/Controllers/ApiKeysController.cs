using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Transcendence.Service.Core.Services.Auth.Interfaces;
using Transcendence.Service.Core.Services.Auth.Models;
using Transcendence.WebAPI.Security;

namespace Transcendence.WebAPI.Controllers;

[ApiController]
[Route("api/auth/keys")]
[Authorize(Policy = AuthPolicies.AdminOnly)]
public class ApiKeysController(IApiKeyService apiKeyService, IAdminAuditService adminAuditService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ApiKeyListItem>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var result = await apiKeyService.ListAsync(ct);
        return Ok(result);
    }

    [HttpPost]
    [EnableRateLimiting("admin-write")]
    [ProducesResponseType(typeof(ApiKeyCreateResult), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] ApiKeyCreateRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Name is required.");

        var created = await apiKeyService.CreateAsync(request, ct);
        await WriteAuditAsync("api-keys.create", "api-key", created.Id.ToString(), true, new
        {
            created.Name,
            created.Prefix,
            created.ExpiresAt
        }, ct);
        return CreatedAtAction(nameof(List), new { id = created.Id }, created);
    }

    [HttpPost("{id:guid}/revoke")]
    [EnableRateLimiting("admin-write")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Revoke([FromRoute] Guid id, CancellationToken ct)
    {
        var revoked = await apiKeyService.RevokeAsync(id, ct);
        if (!revoked) return NotFound();
        await WriteAuditAsync("api-keys.revoke", "api-key", id.ToString(), true, null, ct);
        return Ok(new { message = "API key revoked." });
    }

    [HttpPost("{id:guid}/rotate")]
    [EnableRateLimiting("admin-write")]
    [ProducesResponseType(typeof(ApiKeyCreateResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Rotate([FromRoute] Guid id, CancellationToken ct)
    {
        var rotated = await apiKeyService.RotateAsync(id, ct);
        if (rotated == null) return NotFound();
        await WriteAuditAsync("api-keys.rotate", "api-key", id.ToString(), true, new { rotated.Prefix }, ct);
        return Ok(rotated);
    }

    private async Task WriteAuditAsync(
        string action,
        string targetType,
        string targetId,
        bool isSuccess,
        object? metadata,
        CancellationToken ct)
    {
        var actorIdRaw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        Guid? actorId = Guid.TryParse(actorIdRaw, out var parsed) ? parsed : null;
        var actorEmail = User.FindFirstValue(ClaimTypes.Name);
        var requestId = Request.Headers["x-trn-request-id"].ToString();

        await adminAuditService.WriteAsync(new AdminAuditWriteRequest(
            actorId,
            actorEmail,
            action,
            targetType,
            targetId,
            string.IsNullOrWhiteSpace(requestId) ? null : requestId,
            isSuccess,
            metadata), ct);
    }
}
