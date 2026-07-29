using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Transcendence.Service.Core.Services.Analytics.Interfaces;
using Transcendence.Service.Core.Services.Analytics.Models;
using Transcendence.WebAPI.Security;

namespace Transcendence.WebAPI.Controllers;

[ApiController]
[Route("api/users/me/lol/saved-builds")]
[Authorize(Policy = AuthPolicies.UserOnly)]
// Every action guards on the NameIdentifier claim and returns 401 when it is missing or unparsable,
// so the declaration belongs to the whole controller — without it the generated client gets an
// untyped 401 branch with no ProblemDetails body.
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
public sealed class SavedBuildsController(ISavedBuildService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(SavedBuildListDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct) =>
        TryGetUserId(out var userId)
            ? Ok(await service.ListAsync(userId, page, pageSize, ct))
            : Unauthorized();

    [HttpPost]
    [ProducesResponseType(typeof(SavedBuildDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] SaveBuildRequest request, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized();
        try
        {
            var created = await service.CreateAsync(userId, request, ct);
            return CreatedAtAction(nameof(List), new { }, created);
        }
        catch (SavedBuildLimitExceededException exception)
        {
            return Problem(detail: exception.Message, statusCode: StatusCodes.Status409Conflict);
        }
        catch (ArgumentException exception)
        {
            return ValidationProblem(detail: Describe(exception));
        }
    }

    [HttpPut("{savedBuildId:guid}")]
    [ProducesResponseType(typeof(SavedBuildDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        Guid savedBuildId,
        [FromBody] SaveBuildRequest request,
        CancellationToken ct)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized();
        try
        {
            var updated = await service.UpdateAsync(userId, savedBuildId, request, ct);
            return updated == null ? NotFound() : Ok(updated);
        }
        catch (ArgumentException exception)
        {
            return ValidationProblem(detail: Describe(exception));
        }
    }

    [HttpPost("{savedBuildId:guid}/repair")]
    [ProducesResponseType(typeof(SavedBuildDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Repair(
        Guid savedBuildId,
        [FromBody] SavedBuildRepairRequest request,
        CancellationToken ct)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized();
        try
        {
            var repaired = await service.RepairAsync(userId, savedBuildId, request, ct);
            return repaired == null ? NotFound() : Ok(repaired);
        }
        catch (ArgumentException exception)
        {
            return ValidationProblem(detail: Describe(exception));
        }
    }

    // Idempotent: a repeated delete of a build this user no longer has is a success, not a 404.
    [HttpDelete("{savedBuildId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid savedBuildId, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized();
        await service.DeleteAsync(userId, savedBuildId, ct);
        return NoContent();
    }

    [HttpPost("{savedBuildId:guid}/share")]
    [ProducesResponseType(typeof(SavedBuildShareDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Share(Guid savedBuildId, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized();
        var shared = await service.ShareAsync(userId, savedBuildId, ct);
        return shared == null ? NotFound() : Ok(shared);
    }

    [HttpDelete("{savedBuildId:guid}/share")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevokeShare(Guid savedBuildId, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized();
        return await service.RevokeShareAsync(userId, savedBuildId, ct) ? NoContent() : NotFound();
    }

    // ArgumentException.Message appends "(Parameter 'request')", which is an implementation detail.
    private static string Describe(ArgumentException exception)
    {
        var marker = exception.Message.IndexOf(" (Parameter '", StringComparison.Ordinal);
        return marker < 0 ? exception.Message : exception.Message[..marker];
    }

    private bool TryGetUserId(out Guid userId)
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(claim, out userId);
    }
}
