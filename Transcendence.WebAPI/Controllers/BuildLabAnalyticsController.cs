using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Transcendence.Service.Core.Services.Analytics.Interfaces;
using Transcendence.Service.Core.Services.Analytics.Models;

namespace Transcendence.WebAPI.Controllers;

[ApiController]
[Route("api/lol/analytics/build-lab")]
[EnableRateLimiting("expensive-read")]
[ProducesResponseType(StatusCodes.Status429TooManyRequests)]
public sealed class BuildLabAnalyticsController(IBuildLabService service) : ControllerBase
{
    [HttpGet("{championId:int}")]
    [ProducesResponseType(typeof(BuildLabResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<BuildLabResponse>> Get(
        [FromRoute] int championId,
        [FromQuery] string role,
        [FromQuery] int? opponentChampionId = null,
        [FromQuery] string? patch = null,
        [FromQuery] string? region = null,
        [FromQuery] string section = "items",
        [FromQuery] string mode = "supported",
        [FromQuery] int[]? itemPath = null,
        [FromQuery] int[]? runeSelections = null,
        [FromQuery] int[]? spellPair = null,
        CancellationToken ct = default)
    {
        try
        {
            return Ok(await service.GetAsync(
                new BuildLabQuery(
                    championId,
                    role,
                    opponentChampionId,
                    patch,
                    region,
                    section,
                    mode,
                    itemPath ?? [],
                    runeSelections ?? [],
                    spellPair ?? []),
                ct));
        }
        catch (ArgumentException exception)
        {
            // Model-binding failures on this route already emit ProblemDetails; validation must not
            // answer the same status with a bare text/plain body.
            return ValidationProblem(detail: CleanMessage(exception));
        }
    }

    private static string CleanMessage(ArgumentException exception) =>
        exception.ParamName == null
            ? exception.Message
            : exception.Message.Replace(
                $" (Parameter '{exception.ParamName}')", string.Empty, StringComparison.Ordinal);
}
