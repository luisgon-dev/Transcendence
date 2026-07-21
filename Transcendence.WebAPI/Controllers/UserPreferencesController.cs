using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;
using Transcendence.Service.Core.Services.Auth.Interfaces;
using Transcendence.Service.Core.Services.Auth.Models;
using Transcendence.WebAPI.Security;

namespace Transcendence.WebAPI.Controllers;

[ApiController]
[Route("api/users/me")]
[Authorize(Policy = AuthPolicies.UserOnly)]
public class UserPreferencesController(
    IUserPreferencesService userPreferencesService,
    IRiotRsoService riotRsoService) : ControllerBase
{
    [HttpGet("riot-account")]
    [ProducesResponseType(typeof(RiotAccountLinkDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRiotAccount(CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var link = await riotRsoService.GetLinkAsync(userId, ct);
        return link == null ? NotFound() : Ok(link);
    }

    [HttpPost("riot-account/complete")]
    [EnableRateLimiting("auth-login")]
    [ProducesResponseType(typeof(RiotAccountLinkDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> CompleteRiotLink(
        [FromBody] RiotRsoCompleteRequest request,
        CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        try
        {
            return Ok(await riotRsoService.CompleteLinkAsync(userId, request.Code, request.Region, ct));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (RiotAccountAlreadyLinkedException)
        {
            return Conflict("That Riot account is already linked, or this account already has a different Riot identity.");
        }
        catch (RiotRsoUnavailableException)
        {
            return Problem(
                title: "Riot linking unavailable",
                detail: "Riot account linking is not configured right now.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (RiotRsoExchangeException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpDelete("riot-account")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UnlinkRiotAccount(CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        if (!await riotRsoService.UnlinkAsync(userId, ct))
            return Conflict("Riot-only accounts cannot be unlinked until another sign-in method is added.");
        return NoContent();
    }

    [HttpGet("favorites")]
    [ProducesResponseType(typeof(IReadOnlyList<FavoriteSummonerDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetFavorites(CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var favorites = await userPreferencesService.GetFavoritesAsync(userId, ct);
        return Ok(favorites);
    }

    [HttpPost("favorites")]
    [ProducesResponseType(typeof(FavoriteSummonerDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AddFavorite([FromBody] AddFavoriteRequest request, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();

        try
        {
            var favorite = await userPreferencesService.AddFavoriteAsync(userId, request, ct);
            return Ok(favorite);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpDelete("favorites/{favoriteId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveFavorite([FromRoute] Guid favoriteId, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var removed = await userPreferencesService.RemoveFavoriteAsync(userId, favoriteId, ct);
        if (!removed) return NotFound();
        return NoContent();
    }

    [HttpGet("preferences")]
    [ProducesResponseType(typeof(UserPreferencesDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPreferences(CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var preferences = await userPreferencesService.GetPreferencesAsync(userId, ct);
        return Ok(preferences);
    }

    [HttpPut("preferences")]
    [ProducesResponseType(typeof(UserPreferencesDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdatePreferences([FromBody] UpdateUserPreferencesRequest request,
        CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var preferences = await userPreferencesService.UpdatePreferencesAsync(userId, request, ct);
        return Ok(preferences);
    }

    private bool TryGetUserId(out Guid userId)
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(claim, out userId);
    }
}
