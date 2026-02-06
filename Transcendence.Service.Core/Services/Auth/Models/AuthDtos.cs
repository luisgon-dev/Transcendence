namespace Transcendence.Service.Core.Services.Auth.Models;

public record RegisterRequest(string Email, string Password);
public record LoginRequest(string Email, string Password);
public record RefreshRequest(string RefreshToken);
public record PasswordResetRequest(string Email);

public record AuthTokenResponse(
    string AccessToken,
    string RefreshToken,
    DateTime AccessTokenExpiresAtUtc,
    string TokenType = "Bearer"
);
