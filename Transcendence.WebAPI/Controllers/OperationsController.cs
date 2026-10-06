using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Transcendence.Service.Core.Services.Operations;
using Transcendence.WebAPI.Security;

namespace Transcendence.WebAPI.Controllers;

[ApiController]
[Route("api/lol/operations")]
[Authorize(Policy = AuthPolicies.AppOrUser)]
[EnableRateLimiting("expensive-read")]
public sealed class OperationsController(IBackgroundOperationTracker operations) : ControllerBase
{
    /// <summary>Read only an operation requested by this authenticated user or application.</summary>
    [HttpGet("{operationId:guid}")]
    [ProducesResponseType(typeof(OperationStatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get([FromRoute] Guid operationId, CancellationToken ct)
    {
        // AppOrUser merges principals. Never read its first NameIdentifier: with both headers it
        // could pair the application's ID with a user grant (or vice versa).
        var owners = new List<OperationOwner>();
        await AddOwnerAsync(AuthPolicies.ApiKeyScheme, OperationOwnerKind.Application, owners);
        await AddOwnerAsync(JwtBearerDefaults.AuthenticationScheme, OperationOwnerKind.User, owners);
        if (owners.Count == 0) return Unauthorized();
        Response.Headers.CacheControl = "no-store";
        var result = await operations.GetAsync(operationId, owners, ct);
        return result is null ? NotFound() : Ok(result);
    }

    private async Task AddOwnerAsync(string scheme, OperationOwnerKind kind, List<OperationOwner> owners)
    {
        var authenticated = await HttpContext.AuthenticateAsync(scheme);
        if (authenticated.Succeeded && Guid.TryParse(authenticated.Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id))
            owners.Add(new OperationOwner(kind, id));
    }
}
