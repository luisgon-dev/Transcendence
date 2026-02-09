using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Transcendence.Service.Core.Services.Auth.Interfaces;
using Transcendence.Service.Core.Services.Auth.Models;
using Transcendence.WebAPI.Security;

namespace Transcendence.WebAPI.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(IUserAuthService userAuthService) : ControllerBase
{
    [HttpPost("register")]
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
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("login")]
    [ProducesResponseType(typeof(AuthTokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var result = await userAuthService.LoginAsync(request, ct);
        if (result == null) return Unauthorized("Invalid email or password.");
        return Ok(result);
    }

    [HttpPost("refresh")]
    [ProducesResponseType(typeof(AuthTokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request, CancellationToken ct)
    {
        var result = await userAuthService.RefreshAsync(request, ct);
        if (result == null) return Unauthorized("Invalid or expired refresh token.");
        return Ok(result);
    }

    [HttpPost("password-reset")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> InitiatePasswordReset([FromBody] PasswordResetRequest request, CancellationToken ct)
    {
        await userAuthService.InitiatePasswordResetAsync(request, ct);
        return Ok(new { message = "If the account exists, a reset flow has been initiated." });
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
