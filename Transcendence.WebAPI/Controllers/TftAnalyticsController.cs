using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Transcendence.Service.Core.Services.Tft.Interfaces;
using Transcendence.Service.Core.Services.Tft.Models;
using Transcendence.WebAPI.Security;

namespace Transcendence.WebAPI.Controllers;

[ApiController]
[Route("api/tft/analytics")]
[EnableRateLimiting("expensive-read")]
[ProducesResponseType(StatusCodes.Status429TooManyRequests)]
public class TftAnalyticsController(ITftAnalyticsService analyticsService) : ControllerBase
{
    [HttpGet("regions")]
    public async Task<IActionResult> GetRegions(CancellationToken ct = default)
    {
        return Ok(await analyticsService.GetRegionsAsync(ct));
    }

    [HttpGet("comps")]
    public async Task<IActionResult> GetComps([FromQuery] string? rankTier = null, [FromQuery] string? region = null,
        CancellationToken ct = default)
    {
        return Ok(await analyticsService.GetCompListAsync(rankTier, region, ct));
    }

    [HttpGet("comps/{compSlug}")]
    public async Task<IActionResult> GetComp([FromRoute] string compSlug, [FromQuery] string? rankTier = null,
        [FromQuery] string? region = null, CancellationToken ct = default)
    {
        var result = await analyticsService.GetCompDetailAsync(compSlug, rankTier, region, ct);
        return result == null ? NotFound() : Ok(result);
    }

    [HttpGet("champions")]
    public async Task<IActionResult> GetChampions(CancellationToken ct = default)
    {
        return Ok(await analyticsService.GetChampionsAsync(ct));
    }

    [HttpGet("champions/{championId}")]
    public async Task<IActionResult> GetChampion([FromRoute] string championId, CancellationToken ct = default)
    {
        var result = await analyticsService.GetChampionAsync(championId, ct);
        return result == null ? NotFound() : Ok(result);
    }

    [HttpGet("items")]
    [ProducesResponseType(typeof(IReadOnlyList<TftStaticEntityDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetItems(CancellationToken ct = default)
    {
        return Ok(await analyticsService.GetItemsAsync(ct));
    }

    [HttpGet("items/{itemId}")]
    [ProducesResponseType(typeof(TftStaticEntityDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetItem([FromRoute] string itemId, CancellationToken ct = default)
    {
        var result = await analyticsService.GetItemAsync(itemId, ct);
        return result == null ? NotFound() : Ok(result);
    }

    [HttpGet("traits")]
    public async Task<IActionResult> GetTraits(CancellationToken ct = default)
    {
        return Ok(await analyticsService.GetTraitsAsync(ct));
    }

    [HttpGet("traits/{traitId}")]
    public async Task<IActionResult> GetTrait([FromRoute] string traitId, CancellationToken ct = default)
    {
        var result = await analyticsService.GetTraitAsync(traitId, ct);
        return result == null ? NotFound() : Ok(result);
    }

    [HttpGet("augments")]
    public async Task<IActionResult> GetAugments(CancellationToken ct = default)
    {
        return Ok(await analyticsService.GetAugmentsAsync(ct));
    }

    [HttpGet("augments/{augmentId}")]
    public async Task<IActionResult> GetAugment([FromRoute] string augmentId, CancellationToken ct = default)
    {
        var result = await analyticsService.GetAugmentAsync(augmentId, ct);
        return result == null ? NotFound() : Ok(result);
    }

    [HttpPost("cache/invalidate")]
    [Authorize(Policy = AuthPolicies.AppOnly)]
    public async Task<IActionResult> InvalidateCache(CancellationToken ct = default)
    {
        await analyticsService.InvalidateCacheAsync(ct);
        return Ok(new { message = "TFT analytics cache invalidated." });
    }
}
