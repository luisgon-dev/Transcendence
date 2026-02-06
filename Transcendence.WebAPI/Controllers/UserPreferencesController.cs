using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Transcendence.Service.Core.Services.Auth.Interfaces;
using Transcendence.Service.Core.Services.Auth.Models;
using Transcendence.WebAPI.Security;

namespace Transcendence.WebAPI.Controllers;

[ApiController]
[Route("api/users/me")]
[Authorize(Policy = AuthPolicies.UserOnly)]
public class UserPreferencesController(IUserPreferencesService userPreferencesService) : ControllerBase
{
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
