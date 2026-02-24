using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Transcendence.Service.Core.Services.LiveGame.Interfaces;
using Transcendence.Service.Core.Services.LiveGame.Models;
using Transcendence.WebAPI.Security;

namespace Transcendence.WebAPI.Controllers;

[ApiController]
[Route("api/summoners")]
[Authorize(Policy = AuthPolicies.AppOnly)]
[EnableRateLimiting("expensive-read")]
[ProducesResponseType(StatusCodes.Status429TooManyRequests)]
public class LiveGameController(ILiveGameService liveGameService) : ControllerBase
{
    /// <summary>
    /// Returns current live game state for a Riot ID.
    /// If not currently in game, returns state=offline.
    /// </summary>
    [HttpGet("{region}/{gameName}/{tagLine}/live-game")]
    [ProducesResponseType(typeof(LiveGameResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetCurrentGame(
        [FromRoute] string region,
        [FromRoute] string gameName,
        [FromRoute] string tagLine,
        CancellationToken ct)
    {
        try
        {
            var result = await liveGameService.GetCurrentGameAsync(region, gameName, tagLine, ct);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}
