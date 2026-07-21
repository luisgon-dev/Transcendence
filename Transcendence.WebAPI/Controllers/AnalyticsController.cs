using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Transcendence.Service.Core.Services.Analytics.Interfaces;
using Transcendence.Service.Core.Services.Analytics.Models;
using Transcendence.Service.Core.Services.Jobs.Configuration;
using Transcendence.WebAPI.Models.Common;
using Transcendence.WebAPI.Security;

namespace Transcendence.WebAPI.Controllers;

[ApiController]
[Route("api/lol/analytics")]
[EnableRateLimiting("expensive-read")]
[ProducesResponseType(StatusCodes.Status429TooManyRequests)]
public class AnalyticsController(
    IChampionAnalyticsService analyticsService,
    IAnalyticsPatchQueryService patchQueryService,
    IOptions<MultiRegionIngestionOptions> multiRegionOptions) : ControllerBase
{
    /// <summary>
    /// Get champion tier list ranked by composite score (70% win rate + 30% pick rate).
    /// Returns S/A/B/C/D tiers with movement indicators from previous patch.
    /// Data is cached for 24 hours.
    /// </summary>
    /// <param name="role">Optional role filter (TOP, JUNGLE, MIDDLE, BOTTOM, UTILITY). Leave empty for unified tier list across all roles.</param>
    /// <param name="rankTier">Optional rank tier filter (ALL, EMERALD_PLUS, IRON, BRONZE, SILVER, GOLD, PLATINUM, EMERALD, DIAMOND, MASTER, GRANDMASTER, CHALLENGER)</param>
    /// <param name="region">Optional platform region filter (for example NA1, EUW1, KR). Defaults to ALL/global.</param>
    /// <param name="patch">Optional patch version. Defaults to the active analytics patch.</param>
    /// <param name="ct">Cancellation token</param>
    [HttpGet("tierlist")]
    [ProducesResponseType(typeof(TierListResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTierList(
        [FromQuery] string? role = null,
        [FromQuery] string? rankTier = null,
        [FromQuery] string? region = null,
        [FromQuery] string? patch = null,
        CancellationToken ct = default)
    {
        var tierList = await analyticsService.GetTierListAsync(role, rankTier, region, patch, ct);
        return Ok(tierList);
    }

    [HttpGet("regions")]
    [ProducesResponseType(typeof(IReadOnlyList<AnalyticsRegionDto>), StatusCodes.Status200OK)]
    public IActionResult GetRegions()
    {
        return Ok(AnalyticsRegionCatalog.BuildAvailableRegions(multiRegionOptions.Value));
    }

    [HttpGet("patches")]
    [ProducesResponseType(typeof(IReadOnlyList<AnalyticsPatchOptionDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPatches(CancellationToken ct = default)
    {
        return Ok(await patchQueryService.GetPatchOptionsAsync(ct));
    }

    [HttpGet("status")]
    [ProducesResponseType(typeof(AnalyticsPatchStatusDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStatus(CancellationToken ct)
    {
        return Ok(await patchQueryService.GetActivePatchStatusAsync(ct));
    }

    /// <summary>
    /// [Admin] Invalidate all analytics cache.
    /// Triggers cache refresh on next request.
    /// </summary>
    [HttpPost("cache/invalidate")]
    [Authorize(Policy = AuthPolicies.AppOnly)]
    [ProducesResponseType(typeof(OperationResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> InvalidateCache(CancellationToken ct = default)
    {
        await analyticsService.InvalidateAnalyticsCacheAsync(ct);
        return Ok(new OperationResult("Analytics cache invalidated. Data will refresh on next request."));
    }
}
