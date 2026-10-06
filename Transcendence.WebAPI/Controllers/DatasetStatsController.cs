using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Transcendence.Service.Core.Services.Analytics.Interfaces;
using Transcendence.Service.Core.Services.Analytics.Models;

namespace Transcendence.WebAPI.Controllers;

[ApiController]
[Route("api/lol/analytics/dataset")]
[EnableRateLimiting("search-read")]
[ProducesResponseType(StatusCodes.Status429TooManyRequests)]
public sealed class DatasetStatsController(IDatasetStatsService datasetStatsService) : ControllerBase
{
    /// <summary>
    /// Public scale and freshness of the stored match corpus: matches stored, ingested in the last 24
    /// hours and per day over the last 7 days, platforms crawled, approximate players indexed, the active
    /// patch, and when a match was last ingested. Served from a snapshot the worker recomputes every
    /// 5 minutes; a request never counts rows.
    /// </summary>
    /// <response code="200">The latest snapshot. <c>computedAtUtc</c> says how old it is.</response>
    /// <response code="404">The worker has not computed a snapshot yet (a fresh database).</response>
    [HttpGet]
    [ProducesResponseType(typeof(DatasetStatsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var stats = await datasetStatsService.GetAsync(ct);
        if (stats is null)
            return NotFound();

        Response.Headers.CacheControl = "public, max-age=60, stale-while-revalidate=300";
        return Ok(stats);
    }
}
