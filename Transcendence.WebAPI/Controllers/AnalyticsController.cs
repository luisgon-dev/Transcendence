using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Transcendence.Service.Core.Services.Analytics.Interfaces;
using Transcendence.Service.Core.Services.Analytics.Models;
using Transcendence.Service.Core.Services.Analytics;
using Transcendence.Service.Core.Services.Jobs.Configuration;
using Transcendence.WebAPI.Models;

namespace Transcendence.WebAPI.Controllers;

[ApiController]
[Route("api/lol/analytics")]
[EnableRateLimiting("expensive-read")]
[ProducesResponseType(StatusCodes.Status429TooManyRequests)]
public class AnalyticsController(
    IChampionAnalyticsService analyticsService,
    IAnalyticsPatchQueryService patchQueryService,
    IOptions<MultiRegionIngestionOptions> multiRegionOptions,
    IAnalyticsPublicationService publicationService) : ControllerBase
{
    /// <summary>
    /// Get the published champion tier list in display order, with grades and one-based ranks.
    /// Rows below the scope's sample floor are hidden by default.
    /// </summary>
    /// <param name="role">Optional role filter (TOP, JUNGLE, MIDDLE, BOTTOM, UTILITY). Leave empty for unified tier list across all roles.</param>
    /// <param name="rankTier">Optional rank tier filter (ALL, EMERALD_PLUS, MASTER_PLUS, or an individual tier from IRON through CHALLENGER)</param>
    /// <param name="region">Optional platform region filter (for example NA1, EUW1, KR). Defaults to ALL/global.</param>
    /// <param name="queue">Optional queue family. Defaults to ranked solo/duo.</param>
    /// <param name="patch">Optional patch version. Defaults to the newest published patch for the queue.</param>
    /// <param name="includeLowSample">Include low-sample rows in the board. Counts and sample metadata describe the full scope in either view.</param>
    /// <param name="ct">Cancellation token</param>
    [HttpGet("tierlist")]
    [ProducesResponseType(typeof(TierListBoardResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTierList(
        [FromQuery] string? role = null,
        [FromQuery] string? rankTier = null,
        [FromQuery] string? region = null,
        [FromQuery] string? queue = null,
        [FromQuery] string? patch = null,
        [FromQuery] bool includeLowSample = false,
        CancellationToken ct = default)
    {
        if (!AnalyticsQueueCatalog.IsSupported(queue))
            return BadRequest("Invalid queue. Expected solo, aram, arena, or flex.");

        var publication = await publicationService.ResolveAsync(queue, patch, ct, rankTier);
        var tierList = await analyticsService.GetPublishedTierListAsync(role, rankTier, region, queue, publication, ct);
        return Ok(TierListBoardResponse.From(tierList, includeLowSample));
    }

    [HttpGet("published-patches")]
    [ProducesResponseType(typeof(IReadOnlyList<PublishedAnalyticsPatchOptionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetPublishedPatches(
        [FromQuery] string? queue = null,
        CancellationToken ct = default,
        [FromQuery] string? rankTier = null)
    {
        if (!AnalyticsQueueCatalog.IsSupported(queue))
            return BadRequest("Invalid queue. Expected solo, aram, arena, or flex.");

        return Ok(await publicationService.GetPatchOptionsAsync(queue, ct, rankTier));
    }

    [HttpGet("regions")]
    [ProducesResponseType(typeof(IReadOnlyList<AnalyticsRegionDto>), StatusCodes.Status200OK)]
    public IActionResult GetRegions()
    {
        return Ok(AnalyticsRegionCatalog.BuildAvailableRegions(multiRegionOptions.Value));
    }

    [HttpGet("patches")]
    [ProducesResponseType(typeof(IReadOnlyList<AnalyticsPatchOptionDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPatches(
        [FromQuery] string? queue = null,
        CancellationToken ct = default)
    {
        if (!AnalyticsQueueCatalog.IsSupported(queue))
            return BadRequest("Invalid queue. Expected solo, aram, arena, or flex.");

        return Ok(await patchQueryService.GetPatchOptionsAsync(queue, ct));
    }

    [HttpGet("status")]
    [ProducesResponseType(typeof(AnalyticsPatchStatusDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStatus(CancellationToken ct)
    {
        return Ok(await patchQueryService.GetActivePatchStatusAsync(ct));
    }

}
