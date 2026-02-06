using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Transcendence.Service.Core.Services.Analytics.Interfaces;
using Transcendence.Service.Core.Services.Analytics.Models;
using Transcendence.WebAPI.Security;

namespace Transcendence.WebAPI.Controllers;

[ApiController]
[Route("api/analytics")]
public class AnalyticsController(IChampionAnalyticsService analyticsService) : ControllerBase
{
    /// <summary>
    /// Get champion tier list ranked by composite score (70% win rate + 30% pick rate).
    /// Returns S/A/B/C/D tiers with movement indicators from previous patch.
    /// Data is cached for 24 hours.
    /// </summary>
    /// <param name="role">Optional role filter (TOP, JUNGLE, MIDDLE, BOTTOM, UTILITY). Leave empty for unified tier list across all roles.</param>
    /// <param name="rankTier">Optional rank tier filter (IRON, BRONZE, SILVER, GOLD, PLATINUM, EMERALD, DIAMOND, MASTER, GRANDMASTER, CHALLENGER)</param>
    /// <param name="ct">Cancellation token</param>
    [HttpGet("tierlist")]
    [ProducesResponseType(typeof(TierListResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTierList(
        [FromQuery] string? role = null,
        [FromQuery] string? rankTier = null,
        CancellationToken ct = default)
    {
        var tierList = await analyticsService.GetTierListAsync(role, rankTier, ct);
        return Ok(tierList);
    }

    /// <summary>
    /// [Admin] Invalidate all analytics cache.
    /// Triggers cache refresh on next request.
    /// </summary>
    [HttpPost("cache/invalidate")]
    [Authorize(Policy = AuthPolicies.AppOnly)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> InvalidateCache(CancellationToken ct = default)
    {
        await analyticsService.InvalidateAnalyticsCacheAsync(ct);
        return Ok(new { message = "Analytics cache invalidated. Data will refresh on next request." });
    }
}
