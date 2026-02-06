using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Transcendence.Service.Core.Services.Analytics.Interfaces;
using Transcendence.Service.Core.Services.Analytics.Models;
using Transcendence.WebAPI.Security;

namespace Transcendence.WebAPI.Controllers;

[ApiController]
[Route("api/analytics/champions")]
public class ChampionAnalyticsController(IChampionAnalyticsService analyticsService) : ControllerBase
{
    /// <summary>
    /// Get champion win rates by role and rank tier.
    /// Only returns data for champion/role/tier combinations with 100+ games.
    /// Data is cached for 24 hours.
    /// </summary>
    /// <param name="championId">Champion ID (e.g., 1 for Annie)</param>
    /// <param name="rankTier">Optional rank tier filter (Iron, Bronze, Silver, Gold, Platinum, Emerald, Diamond, Master, Grandmaster, Challenger)</param>
    /// <param name="region">Optional region filter (e.g., NA1, EUW1)</param>
    /// <param name="role">Optional role filter (TOP, JUNGLE, MIDDLE, BOTTOM, UTILITY)</param>
    /// <param name="ct">Cancellation token</param>
    [HttpGet("{championId}/winrates")]
    [ProducesResponseType(typeof(ChampionWinRateSummary), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetWinRates(
        [FromRoute] int championId,
        [FromQuery] string? rankTier = null,
        [FromQuery] string? region = null,
        [FromQuery] string? role = null,
        CancellationToken ct = default)
    {
        if (championId <= 0)
            return BadRequest("Invalid champion ID. Must be positive integer.");

        var filter = new ChampionAnalyticsFilter(
            RankTier: rankTier,
            Region: region,
            Role: role
        );

        var summary = await analyticsService.GetWinRatesAsync(championId, filter, ct);
        return Ok(summary);
    }

    /// <summary>
    /// Get top 3 builds for a champion in a role.
    /// Builds include items and runes bundled together.
    /// Core items (70%+ appearance) are distinguished from situational.
    /// </summary>
    /// <param name="championId">Champion ID</param>
    /// <param name="role">Role: TOP, JUNGLE, MIDDLE, BOTTOM, UTILITY</param>
    /// <param name="rankTier">Optional: Filter by rank tier</param>
    [HttpGet("{championId}/builds")]
    [ProducesResponseType(typeof(ChampionBuildsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ChampionBuildsResponse>> GetBuilds(
        int championId,
        [FromQuery] string role,
        [FromQuery] string? rankTier = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(role))
            return BadRequest("Role parameter is required");

        var result = await analyticsService.GetBuildsAsync(championId, role, rankTier, ct);
        return Ok(result);
    }

    /// <summary>
    /// Get matchup data (counters and favorable matchups) for a champion in a role.
    /// Matchups are lane-specific (e.g., Mid vs Mid).
    /// </summary>
    /// <param name="championId">Champion ID</param>
    /// <param name="role">Role: TOP, JUNGLE, MIDDLE, BOTTOM, UTILITY</param>
    /// <param name="rankTier">Optional: Filter by rank tier</param>
    [HttpGet("{championId}/matchups")]
    [ProducesResponseType(typeof(ChampionMatchupsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ChampionMatchupsResponse>> GetMatchups(
        int championId,
        [FromQuery] string role,
        [FromQuery] string? rankTier = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(role))
            return BadRequest("Role parameter is required");

        var result = await analyticsService.GetMatchupsAsync(championId, role, rankTier, ct);
        return Ok(result);
    }

    /// <summary>
    /// Invalidates all analytics cache entries.
    /// Used when patch changes or significant data updates occur.
    /// </summary>
    [HttpPost("cache/invalidate")]
    [Authorize(Policy = AuthPolicies.AppOnly)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> InvalidateCache(CancellationToken ct)
    {
        await analyticsService.InvalidateAnalyticsCacheAsync(ct);
        return Ok(new { message = "Analytics cache invalidated successfully" });
    }
}
