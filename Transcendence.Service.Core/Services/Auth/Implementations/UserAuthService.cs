using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Transcendence.Data.Models.Auth;
using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Core.Services.Auth.Interfaces;
using Transcendence.Service.Core.Services.Auth.Models;

namespace Transcendence.Service.Core.Services.Auth.Implementations;

public class UserAuthService(
    IUserAccountRepository userAccountRepository,
    IJwtService jwtService,
    IOptions<AdminBootstrapOptions> adminBootstrapOptions,
    ILogger<UserAuthService> logger) : IUserAuthService
{
    private const int RefreshTokenDays = 7;
    private const int PasswordIterations = 310_000;
    internal const int MinimumPasswordLength = 12;
    // Per-account brute-force lockout, independent of the per-IP rate limiter.
    private const int MaxFailedLoginAttempts = 10;
    private const int LockoutDurationMinutes = 15;
    private readonly HashSet<string> _bootstrapAdminEmails = adminBootstrapOptions.Value.Emails
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Select(NormalizeEmail)
        .ToHashSet(StringComparer.Ordinal);

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
        await EnsureBootstrapAdminRoleAsync(user, ct);

        var response = await IssueTokensAsync(user, ct);
        await userAccountRepository.SaveChangesAsync(ct);
        return response;
    }

    public async Task<AuthTokenResponse?> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var emailNormalized = NormalizeEmail(request.Email);
        var user = await userAccountRepository.GetByEmailNormalizedAsync(emailNormalized, ct);
        if (user == null) return null;

        // Per-account lockout: a distributed/rotating-IP brute force against ONE account is throttled
        // here even when each individual IP stays under its own rate-limit partition.
        if (user.LockoutUntilUtc is { } lockedUntil && lockedUntil > now)
        {
            logger.LogWarning("Login blocked: account {Email} is locked out until {LockoutUntilUtc:o}.",
                user.Email, lockedUntil);
            return null;
        }

        if (!VerifyPassword(request.Password, user.PasswordHash, out var storedIterations))
        {
            user.FailedLoginAttempts++;
            if (user.FailedLoginAttempts >= MaxFailedLoginAttempts)
            {
                user.LockoutUntilUtc = now.AddMinutes(LockoutDurationMinutes);
                logger.LogWarning(
                    "Account {Email} locked for {Minutes}m after {Attempts} consecutive failed logins.",
                    user.Email, LockoutDurationMinutes, user.FailedLoginAttempts);
            }
            user.UpdatedAtUtc = now;
            await userAccountRepository.SaveChangesAsync(ct);
            return null;
        }

        // Successful auth clears the counter and any (now-expired) lockout window.
        user.FailedLoginAttempts = 0;
        user.LockoutUntilUtc = null;
        user.LastLoginAtUtc = now;
        user.UpdatedAtUtc = now;
        if (storedIterations < PasswordIterations)
        {
            user.PasswordHash = HashPassword(request.Password);
            logger.LogInformation("Upgraded password hash cost factor for {Email}", user.Email);
        }
        await EnsureBootstrapAdminRoleAsync(user, ct);

        var response = await IssueTokensAsync(user, ct);
        await userAccountRepository.SaveChangesAsync(ct);
        return response;
    }

    public async Task<AuthTokenResponse?> RefreshAsync(RefreshRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
            return null;

        var tokenHash = jwtService.HashRefreshToken(request.RefreshToken);
        var currentToken = await userAccountRepository.GetRefreshTokenByHashAsync(tokenHash, ct);
        if (currentToken == null) return null;

        if (currentToken.RevokedAtUtc != null)
        {
            // Refresh-token reuse detection: a token that was already ROTATED (revoked with a successor)
            // is being presented again — the legitimate client would be using the successor, so this
            // signals the token was stolen. Revoke the whole family so neither the attacker nor a
            // possibly-compromised client can keep refreshing; the user must re-authenticate. A token
            // revoked by logout (no successor) is just replayed/stale — fail without nuking a fresh session.
            if (currentToken.ReplacedByTokenHash != null)
            {
                var revokedCount = await userAccountRepository
                    .RevokeAllActiveRefreshTokensForUserAsync(currentToken.UserAccountId, ct);
                await userAccountRepository.SaveChangesAsync(ct);
                logger.LogWarning(
                    "Refresh-token reuse detected for user {UserAccountId}; revoked {Count} active token(s) in the family.",
                    currentToken.UserAccountId, revokedCount);
            }

            return null;
        }

        if (currentToken.ExpiresAtUtc <= DateTime.UtcNow)
            return null;

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

    public async Task LogoutAsync(RefreshRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
            return;

        var tokenHash = jwtService.HashRefreshToken(request.RefreshToken);
        var revoked = await userAccountRepository.RevokeActiveRefreshTokenByHashAsync(tokenHash, ct);
        if (revoked)
            await userAccountRepository.SaveChangesAsync(ct);
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
        if (string.IsNullOrWhiteSpace(password) || password.Length < MinimumPasswordLength)
            throw new ArgumentException($"Password must be at least {MinimumPasswordLength} characters.", nameof(password));
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

    internal static string HashPasswordForStorage(string password) => HashPassword(password);

    private static bool VerifyPassword(string password, string storedHash, out int storedIterations)
    {
        storedIterations = 0;
        var parts = storedHash.Split('$');
        if (parts.Length != 4 || !parts[0].Equals("pbkdf2", StringComparison.OrdinalIgnoreCase))
            return false;

        if (!int.TryParse(parts[1], out var iterations) || iterations <= 0)
            return false;

        byte[] salt;
        byte[] expectedHash;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expectedHash = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        if (salt.Length == 0 || expectedHash.Length == 0)
            return false;

        byte[] actualHash;
        try
        {
            actualHash = Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                iterations,
                HashAlgorithmName.SHA256,
                expectedHash.Length);
        }
        catch (ArgumentException)
        {
            return false;
        }

        storedIterations = iterations;
        return CryptographicOperations.FixedTimeEquals(expectedHash, actualHash);
    }

    private async Task EnsureBootstrapAdminRoleAsync(UserAccount user, CancellationToken ct)
    {
        if (_bootstrapAdminEmails.Count == 0)
            return;

        if (!_bootstrapAdminEmails.Contains(user.EmailNormalized))
            return;

        var hasAdminRole = user.Roles.Any(x => string.Equals(x.Role, SystemRoles.Admin, StringComparison.Ordinal));
        if (hasAdminRole)
            return;

        var role = new UserRole
        {
            UserAccountId = user.Id,
            Role = SystemRoles.Admin,
            GrantedAtUtc = DateTime.UtcNow,
            GrantedBy = "bootstrap:auth"
        };

        user.Roles.Add(role);
        await userAccountRepository.AddRoleAsync(role, ct);
        logger.LogInformation("Granted admin bootstrap role during auth flow for {Email}", user.Email);
    }
}
