using System.Security.Cryptography;
using Transcendence.Data.Models.Auth;
using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Core.Services.Auth.Interfaces;
using Transcendence.Service.Core.Services.Auth.Models;

namespace Transcendence.Service.Core.Services.Auth.Implementations;

public class UserAuthService(
    IUserAccountRepository userAccountRepository,
    IJwtService jwtService,
    ILogger<UserAuthService> logger) : IUserAuthService
{
    private const int RefreshTokenDays = 7;
    private const int PasswordIterations = 100_000;

    public async Task<AuthTokenResponse> RegisterAsync(RegisterRequest request, CancellationToken ct = default)
    {
        ValidateCredentials(request.Email, request.Password);

        var emailNormalized = NormalizeEmail(request.Email);
        var existing = await userAccountRepository.GetByEmailNormalizedAsync(emailNormalized, ct);
        if (existing != null)
            throw new InvalidOperationException("Email is already registered.");

        var user = new UserAccount
        {
            Id = Guid.NewGuid(),
            Email = request.Email.Trim(),
            EmailNormalized = emailNormalized,
            PasswordHash = HashPassword(request.Password),
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        await userAccountRepository.AddUserAsync(user, ct);

        var response = await IssueTokensAsync(user, ct);
        await userAccountRepository.SaveChangesAsync(ct);
        return response;
    }

    public async Task<AuthTokenResponse?> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var emailNormalized = NormalizeEmail(request.Email);
        var user = await userAccountRepository.GetByEmailNormalizedAsync(emailNormalized, ct);
        if (user == null) return null;

        if (!VerifyPassword(request.Password, user.PasswordHash))
            return null;

        user.LastLoginAtUtc = DateTime.UtcNow;
        user.UpdatedAtUtc = DateTime.UtcNow;

        var response = await IssueTokensAsync(user, ct);
        await userAccountRepository.SaveChangesAsync(ct);
        return response;
    }

    public async Task<AuthTokenResponse?> RefreshAsync(RefreshRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
            return null;

        var tokenHash = jwtService.HashRefreshToken(request.RefreshToken);
        var currentToken = await userAccountRepository.GetActiveRefreshTokenAsync(tokenHash, ct);
        if (currentToken == null) return null;

        var user = currentToken.UserAccount;
        var newRefreshToken = jwtService.GenerateRefreshToken();
        var newRefreshHash = jwtService.HashRefreshToken(newRefreshToken);

        await userAccountRepository.RevokeRefreshTokenAsync(currentToken, newRefreshHash, ct);

        var replacement = new UserRefreshToken
        {
            Id = Guid.NewGuid(),
            UserAccountId = user.Id,
            TokenHash = newRefreshHash,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(RefreshTokenDays)
        };

        await userAccountRepository.AddRefreshTokenAsync(replacement, ct);
        await userAccountRepository.SaveChangesAsync(ct);

        return new AuthTokenResponse(
            AccessToken: jwtService.GenerateAccessToken(user),
            RefreshToken: newRefreshToken,
            AccessTokenExpiresAtUtc: jwtService.GetAccessTokenExpirationUtc()
        );
    }

    public Task InitiatePasswordResetAsync(PasswordResetRequest request, CancellationToken ct = default)
    {
        // Placeholder for email integration in a later phase.
        // Intentionally does not disclose whether an account exists.
        logger.LogInformation("Password reset requested for {Email}", request.Email?.Trim());
        return Task.CompletedTask;
    }

    private async Task<AuthTokenResponse> IssueTokensAsync(UserAccount user, CancellationToken ct)
    {
        var accessToken = jwtService.GenerateAccessToken(user);
        var refreshToken = jwtService.GenerateRefreshToken();
        var refreshHash = jwtService.HashRefreshToken(refreshToken);

        var refreshEntity = new UserRefreshToken
        {
            Id = Guid.NewGuid(),
            UserAccountId = user.Id,
            TokenHash = refreshHash,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(RefreshTokenDays)
        };

        await userAccountRepository.AddRefreshTokenAsync(refreshEntity, ct);

        return new AuthTokenResponse(
            AccessToken: accessToken,
            RefreshToken: refreshToken,
            AccessTokenExpiresAtUtc: jwtService.GetAccessTokenExpirationUtc()
        );
    }

    private static string NormalizeEmail(string email)
    {
        return email.Trim().ToUpperInvariant();
    }

    private static void ValidateCredentials(string email, string password)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email is required.", nameof(email));
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
            throw new ArgumentException("Password must be at least 8 characters.", nameof(password));
    }

    private static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            PasswordIterations,
            HashAlgorithmName.SHA256,
            32);

        return $"pbkdf2${PasswordIterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    private static bool VerifyPassword(string password, string storedHash)
    {
        var parts = storedHash.Split('$');
        if (parts.Length != 4 || !parts[0].Equals("pbkdf2", StringComparison.OrdinalIgnoreCase))
            return false;

        if (!int.TryParse(parts[1], out var iterations))
            return false;

        var salt = Convert.FromBase64String(parts[2]);
        var expectedHash = Convert.FromBase64String(parts[3]);
        var actualHash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            expectedHash.Length);

        return CryptographicOperations.FixedTimeEquals(expectedHash, actualHash);
    }
}
