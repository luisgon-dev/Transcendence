using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Transcendence.Service.Core.Services.Analytics.Interfaces;
using Transcendence.Service.Core.Services.Analytics.Models;

namespace Transcendence.WebAPI.Controllers;

/// <summary>
/// Cross-champion pro / high-elo analytics (playrate ranking + public roster).
/// Kept on its own route to avoid colliding with ChampionAnalyticsController's {championId}.
/// </summary>
[ApiController]
[Route("api/lol/analytics/pro")]
[EnableRateLimiting("expensive-read")]
[ProducesResponseType(StatusCodes.Status429TooManyRequests)]
public class ProAnalyticsController(IChampionAnalyticsService analyticsService) : ControllerBase
{
    /// <summary>
    /// Champions ranked by how often tracked pros / high-elo players pick them.
    /// </summary>
    /// <param name="region">Optional region filter (e.g., NA, EUW, KR). Defaults to ALL.</param>
    /// <param name="scope">Roster scope: "pro", "highelo", or "all". Defaults to all.</param>
    /// <param name="patch">Optional patch version. Defaults to the active analytics patch.</param>
    /// <param name="ct">Cancellation token</param>
    [HttpGet("champions")]
    [ProducesResponseType(typeof(ProChampionPlayrateResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ProChampionPlayrateResponse>> GetProChampionPlayrate(
        [FromQuery] string? region = null,
        [FromQuery] string? scope = null,
        [FromQuery] string? patch = null,
        CancellationToken ct = default)
    {
        var result = await analyticsService.GetProChampionPlayrateAsync(region, scope, patch, ct);
        return Ok(result);
    }

    /// <summary>
    /// Public roster of tracked pro players (display name, team, region).
    /// </summary>
    /// <param name="region">Optional region filter (e.g., NA, EUW, KR). Defaults to ALL.</param>
    /// <param name="ct">Cancellation token</param>
    [HttpGet("players")]
    [ProducesResponseType(typeof(ProRosterResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ProRosterResponse>> GetProRoster(
        [FromQuery] string? region = null,
        CancellationToken ct = default)
    {
        var result = await analyticsService.GetProRosterAsync(region, ct);
        return Ok(result);
    }
}
