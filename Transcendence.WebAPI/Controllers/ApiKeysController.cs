using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Transcendence.Service.Core.Services.Auth.Interfaces;
using Transcendence.Service.Core.Services.Auth.Models;
using Transcendence.WebAPI.Security;

namespace Transcendence.WebAPI.Controllers;

[ApiController]
[Route("api/auth/keys")]
[Authorize(Policy = AuthPolicies.AppOnly)]
public class ApiKeysController(IApiKeyService apiKeyService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ApiKeyListItem>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var result = await apiKeyService.ListAsync(ct);
        return Ok(result);
    }

    [HttpPost]
    [ProducesResponseType(typeof(ApiKeyCreateResult), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] ApiKeyCreateRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Name is required.");

        var created = await apiKeyService.CreateAsync(request, ct);
        return CreatedAtAction(nameof(List), new { id = created.Id }, created);
    }

    [HttpPost("{id:guid}/revoke")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Revoke([FromRoute] Guid id, CancellationToken ct)
    {
        var revoked = await apiKeyService.RevokeAsync(id, ct);
        if (!revoked) return NotFound();
        return Ok(new { message = "API key revoked." });
    }

    [HttpPost("{id:guid}/rotate")]
    [ProducesResponseType(typeof(ApiKeyCreateResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Rotate([FromRoute] Guid id, CancellationToken ct)
    {
        var rotated = await apiKeyService.RotateAsync(id, ct);
        if (rotated == null) return NotFound();
        return Ok(rotated);
    }
}
