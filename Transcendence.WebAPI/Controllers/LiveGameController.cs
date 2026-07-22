using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Transcendence.Service.Core.Services.LiveGame.Interfaces;
using Transcendence.Service.Core.Services.LiveGame.Models;
using Transcendence.Service.Core.Services.RiotApi;
using Transcendence.WebAPI.Security;

namespace Transcendence.WebAPI.Controllers;

[ApiController]
[Route("api/lol/summoners")]
[Authorize(Policy = AuthPolicies.AppOnly)]
[EnableRateLimiting("expensive-read")]
[ProducesResponseType(StatusCodes.Status429TooManyRequests)]
public class LiveGameController(
    ILiveGameService liveGameService,
    ILiveGameProbeCoordinator liveGameProbeCoordinator) : ControllerBase
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

    /// <summary>
    /// Queues a fresh Spectator-V5 probe on the credentialed worker. Duplicate requests for the same
    /// Riot ID are coalesced while the probe is in flight.
    /// </summary>
    [HttpPost("{region}/{gameName}/{tagLine}/live-game/probe")]
    [ProducesResponseType(typeof(LiveGameProbeAcceptedResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ProbeCurrentGame(
        [FromRoute] string region,
        [FromRoute] string gameName,
        [FromRoute] string tagLine,
        CancellationToken ct)
    {
        if (!PlatformRouteParser.TryParse(region, out var platform))
            return BadRequest($"Unsupported platform region '{region}'.");

        var poll = Url.ActionLink(nameof(GetCurrentGame), values: new
        {
            region = platform.ToString(),
            gameName,
            tagLine
        });
        var outcome = await liveGameProbeCoordinator.EnqueueAsync(platform, gameName, tagLine, ct);
        return Accepted(new LiveGameProbeAcceptedResponse(
            outcome.WasQueued ? "queued" : "in_progress",
            poll,
            outcome.RetryAfterSeconds));
    }
}
