using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Transcendence.Service.Core.Services.Analytics.Interfaces;
using Transcendence.Service.Core.Services.Analytics.Models;

namespace Transcendence.WebAPI.Controllers;

[ApiController]
[Route("api/lol/analytics")]
[EnableRateLimiting("expensive-read")]
[ProducesResponseType(StatusCodes.Status429TooManyRequests)]
public sealed class BuildResourceAnalyticsController(IBuildResourceAnalyticsService service) : ControllerBase
{
    [HttpGet("items")]
    [ProducesResponseType(typeof(BuildResourceAnalyticsIndexResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetItems(
        [FromQuery] string? region = null,
        [FromQuery] string? patch = null,
        CancellationToken ct = default) =>
        Ok(await service.GetItemsAsync(region, patch, ct));

    [HttpGet("items/{itemId:int}")]
    [ProducesResponseType(typeof(BuildResourceAnalyticsDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetItem(
        int itemId,
        [FromQuery] string? region = null,
        [FromQuery] string? patch = null,
        CancellationToken ct = default)
    {
        var result = await service.GetItemAsync(itemId, region, patch, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("runes")]
    [ProducesResponseType(typeof(BuildResourceAnalyticsIndexResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRunes(
        [FromQuery] string? region = null,
        [FromQuery] string? patch = null,
        CancellationToken ct = default) =>
        Ok(await service.GetRunesAsync(region, patch, ct));

    [HttpGet("runes/{runeId:int}")]
    [ProducesResponseType(typeof(BuildResourceAnalyticsDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRune(
        int runeId,
        [FromQuery] string? region = null,
        [FromQuery] string? patch = null,
        CancellationToken ct = default)
    {
        var result = await service.GetRuneAsync(runeId, region, patch, ct);
        return result is null ? NotFound() : Ok(result);
    }
}
