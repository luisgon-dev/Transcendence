using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;
using Transcendence.Service.Core.Services.Auth.Interfaces;
using Transcendence.Service.Core.Services.Auth.Models;
using Transcendence.WebAPI.Security;

namespace Transcendence.WebAPI.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(
    IUserAuthService userAuthService,
    IPasswordResetService passwordResetService,
    IRiotRsoService riotRsoService) : ControllerBase
{
    [HttpPost("riot/authorize")]
    [EnableRateLimiting("auth-login")]
    [ProducesResponseType(typeof(RiotAuthorizationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public IActionResult RiotAuthorize([FromBody] RiotAuthorizationRequest request)
    {
        try
        {
            return Ok(riotRsoService.CreateAuthorization(request.State));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (RiotRsoUnavailableException)
        {
            return Problem(
                title: "Riot sign-in unavailable",
                detail: "Riot account linking is not configured right now.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    [HttpPost("riot/complete")]
    [EnableRateLimiting("auth-login")]
    [ProducesResponseType(typeof(RiotRsoAuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> CompleteRiotLogin(
        [FromBody] RiotRsoCompleteRequest request,
        CancellationToken ct)
    {
        try
        {
            return Ok(await riotRsoService.CompleteLoginAsync(request.Code, request.Region, ct));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (RiotRsoUnavailableException)
        {
            return Problem(
                title: "Riot sign-in unavailable",
                detail: "Riot account linking is not configured right now.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (RiotRsoExchangeException ex)
        {
            return Unauthorized(ex.Message);
        }
    }

    [HttpPost("register")]
    [EnableRateLimiting("auth-register")]
    [ProducesResponseType(typeof(AuthTokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken ct)
    {
        try
        {
            var result = await userAuthService.RegisterAsync(request, ct);
            return Ok(result);
        }
        catch (InvalidOperationException)
        {
            return Conflict("Registration failed.");
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("login")]
    [EnableRateLimiting("auth-login")]
    [ProducesResponseType(typeof(AuthTokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var result = await userAuthService.LoginAsync(request, ct);
        if (result == null) return Unauthorized("Invalid email or password.");
        return Ok(result);
    }

    [HttpPost("refresh")]
    [EnableRateLimiting("auth-refresh")]
    [ProducesResponseType(typeof(AuthTokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request, CancellationToken ct)
    {
        var result = await userAuthService.RefreshAsync(request, ct);
        if (result == null) return Unauthorized("Invalid or expired refresh token.");
        return Ok(result);
    }

    [HttpPost("logout")]
    [EnableRateLimiting("auth-refresh")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout([FromBody] RefreshRequest request, CancellationToken ct)
    {
        await userAuthService.LogoutAsync(request, ct);
        return NoContent();
    }

    [HttpPost("password-reset")]
    [EnableRateLimiting("auth-register")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> InitiatePasswordReset([FromBody] PasswordResetRequest request, CancellationToken ct)
    {
        if (!await passwordResetService.InitiateAsync(request, ct))
            return Problem(
                title: "Password recovery unavailable",
                detail: "Password recovery is not configured right now. Please try again later.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        return Ok(new { message = "If the account exists, a reset flow has been initiated." });
    }

    [HttpPost("password-reset/complete")]
    [EnableRateLimiting("auth-register")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CompletePasswordReset(
        [FromBody] PasswordResetCompleteRequest request,
        CancellationToken ct)
    {
        if (!await passwordResetService.CompleteAsync(request, ct))
            return BadRequest("The reset link is invalid or expired, or the password does not meet requirements.");
        return NoContent();
    }

    [HttpGet("me")]
    [Authorize(Policy = AuthPolicies.AppOrUser)]
    [ProducesResponseType(typeof(AuthMeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult Me()
    {
        var subject = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var name = User.FindFirstValue(ClaimTypes.Name);
        var roles = User.FindAll(ClaimTypes.Role).Select(x => x.Value).ToArray();

        return Ok(new AuthMeResponse(
            subject,
            name,
            roles,
            User.Identity?.AuthenticationType));
    }
}
