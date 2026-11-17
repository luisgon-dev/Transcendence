using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Transcendence.WebAPI.Telemetry;

namespace Transcendence.WebAPI.Controllers;

[ApiController]
[Route("api/desktop-telemetry")]
[AllowAnonymous]
[EnableRateLimiting("desktop-telemetry")]
[RequestSizeLimit(16384)]
public sealed class DesktopTelemetryController(DesktopTelemetry telemetry) : ControllerBase
{
    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public IActionResult Post([FromBody] DesktopTelemetryBatch batch)
    {
        if (!DesktopTelemetry.IsValid(batch)) return BadRequest(new ProblemDetails
            { Status = 400, Title = "Invalid desktop telemetry batch." });
        if (!telemetry.TryConsumeBudget()) return StatusCode(StatusCodes.Status429TooManyRequests);
        telemetry.Record(batch);
        return NoContent();
    }
}
