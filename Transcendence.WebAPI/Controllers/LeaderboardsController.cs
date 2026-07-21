using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Transcendence.Service.Core.Services.Leaderboards.Implementations;
using Transcendence.Service.Core.Services.Leaderboards.Interfaces;
using Transcendence.Service.Core.Services.Leaderboards.Models;
using Transcendence.Service.Core.Services.RiotApi;

namespace Transcendence.WebAPI.Controllers;

[ApiController]
[Route("api/lol/leaderboards")]
[ProducesResponseType(StatusCodes.Status429TooManyRequests)]
public sealed class LeaderboardsController(ILeaderboardService leaderboardService) : ControllerBase
{
    [HttpGet]
    [EnableRateLimiting("expensive-read")]
    [ProducesResponseType(typeof(LeaderboardResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Get(
        [FromQuery] string region = "na",
        [FromQuery] string queue = "solo",
        [FromQuery] int? championId = null,
        [FromQuery] string? role = null,
        [FromQuery] int limit = 100,
        [FromQuery] int minimumChampionGames = 5,
        CancellationToken ct = default)
    {
        if (!PlatformRouteParser.TryParse(region, out var platform))
            return BadRequest($"Unsupported region '{region}'. Use a platform like NA1, EUW1, EUN1, KR, etc.");
        if (!string.Equals(queue, "solo", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(queue, "flex", StringComparison.OrdinalIgnoreCase))
            return BadRequest("queue must be 'solo' or 'flex'.");
        if (championId is <= 0)
            return BadRequest("championId must be a positive integer.");
        if (championId is not null && role is not null &&
            LeaderboardService.NormalizeRole(role) is null)
            return BadRequest("role must be TOP, JUNGLE, MIDDLE, BOTTOM, or UTILITY.");
        if (limit is < 1 or > 100)
            return BadRequest("limit must be between 1 and 100.");
        if (minimumChampionGames is < 1 or > 100)
            return BadRequest("minimumChampionGames must be between 1 and 100.");

        var result = await leaderboardService.GetAsync(
            platform.ToString(),
            queue,
            championId,
            role,
            limit,
            minimumChampionGames,
            ct);
        Response.Headers.CacheControl = "public, max-age=60, stale-while-revalidate=300";
        return Ok(result);
    }
}
