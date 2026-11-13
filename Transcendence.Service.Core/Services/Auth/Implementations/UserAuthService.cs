using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Transcendence.Data.Models.Auth;
using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Core.Services.Auth.Interfaces;
using Transcendence.Service.Core.Services.Auth.Models;

namespace Transcendence.Service.Core.Services.Auth.Implementations;

public class UserAuthService(
    IUserAccountRepository userAccountRepository,
    IJwtService jwtService,
    IDataProtectionProvider dataProtectionProvider,
    ILogger<UserAuthService> logger) : IUserAuthService
{
    private const int RefreshTokenDays = 7;
    private const int PasswordIterations = 310_000;
    // A syntactically valid current-cost hash. The actual hash bytes are deliberately arbitrary:
    // unknown-account logins verify against it only to consume the same PBKDF2 work as a bad
    // password for an existing account, without keeping a usable dummy credential in the binary.
    private const string DummyPasswordHash =
        "pbkdf2$310000$AAAAAAAAAAAAAAAAAAAAAA==$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";
    internal const int MinimumPasswordLength = 12;
    // Per-account brute-force lockout, independent of the per-IP rate limiter.
    private const int MaxFailedLoginAttempts = 10;
    private const int LockoutDurationMinutes = 15;
    private static readonly TimeSpan RotationRecoveryWindow = TimeSpan.FromSeconds(10);
    private readonly IDataProtector _refreshProtector = dataProtectionProvider.CreateProtector("Transcendence.RefreshRotation.v1");

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
        var now = DateTime.UtcNow;
        var emailNormalized = NormalizeEmail(request.Email);
        var user = await userAccountRepository.GetByEmailNormalizedAsync(emailNormalized, ct);
        if (user == null)
        {
            _ = VerifyPassword(request.Password, DummyPasswordHash, out _);
            return null;
        }

        // Per-account lockout: a distributed/rotating-IP brute force against ONE account is throttled
        // here even when each individual IP stays under its own rate-limit partition.
        if (user.LockoutUntilUtc is { } lockedUntil && lockedUntil > now)
        {
            logger.LogWarning("Login blocked: account {UserAccountId} is locked out until {LockoutUntilUtc:o}.",
                user.Id, lockedUntil);
            return null;
        }

        if (!VerifyPassword(request.Password, user.PasswordHash, out var storedIterations))
        {
            user.FailedLoginAttempts++;
            if (user.FailedLoginAttempts >= MaxFailedLoginAttempts)
            {
                user.LockoutUntilUtc = now.AddMinutes(LockoutDurationMinutes);
                logger.LogWarning(
                    "Account {UserAccountId} locked for {Minutes}m after {Attempts} consecutive failed logins.",
                    user.Id, LockoutDurationMinutes, user.FailedLoginAttempts);
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
            logger.LogInformation("Upgraded password hash cost factor for user {UserAccountId}", user.Id);
        }

        var response = await IssueTokensAsync(user, ct);
        await userAccountRepository.SaveChangesAsync(ct);
        return response;
    }

    public async Task<AuthTokenResponse?> RefreshAsync(RefreshRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken)) return null;
        var hash = jwtService.HashRefreshToken(request.RefreshToken);
        var current = await userAccountRepository.GetRefreshTokenByHashAsync(hash, ct);
        var now = DateTime.UtcNow;
        if (current is null || current.ExpiresAtUtc <= now) return null;
        if (current.RevokedAtUtc is null)
        {
            var raw = jwtService.GenerateRefreshToken();
            var replacement = new UserRefreshToken
            {
                Id = Guid.NewGuid(), UserAccountId = current.UserAccountId,
                FamilyId = FamilyOf(current), TokenHash = jwtService.HashRefreshToken(raw),
                CreatedAtUtc = now, ExpiresAtUtc = now.AddDays(RefreshTokenDays)
            };
            if (await userAccountRepository.TryRotateRefreshTokenAsync(hash, replacement, _refreshProtector.Protect(raw), now, ct))
                return Tokens(current.UserAccount, raw);
            // A different request may have claimed this token while we read it.
            current = await userAccountRepository.GetRefreshTokenByHashAsync(hash, ct);
            if (current is null) return null;
        }

        now = DateTime.UtcNow;
        if (current.ReplacedByTokenHash is null) return null; // logout/reset, never recover
        if (current.RevokedAtUtc is { } revoked && revoked <= now && now - revoked <= RotationRecoveryWindow)
            return await RecoverRotationAsync(current, now, ct);

        // Pre-migration revoked tokens have no family identity: retain the old fail-closed
        // user-wide replay response until those tokens expire, without guessing a family.
        var count = current.FamilyId == Guid.Empty
            ? await userAccountRepository.RevokeAllActiveRefreshTokensForUserAsync(current.UserAccountId, ct)
            : await userAccountRepository.RevokeRefreshTokenFamilyAsync(current.UserAccountId, current.FamilyId, ct);
        if (current.FamilyId == Guid.Empty) await userAccountRepository.SaveChangesAsync(ct);
        logger.LogWarning("Refresh-token replay for user {UserAccountId}; revoked {Count} token(s) in its session family.",
            current.UserAccountId, count);
        return null;
    }

    private async Task<AuthTokenResponse?> RecoverRotationAsync(UserRefreshToken original, DateTime now, CancellationToken ct)
    {
        var current = original;
        // Bound traversal if another request already rotated a successor. Never retain raw tokens.
        for (var hop = 0; hop < 4 && current.ProtectedReplacementToken is { } protectedToken; hop++)
        {
            string raw;
            try { raw = _refreshProtector.Unprotect(protectedToken); }
            catch (CryptographicException error)
            {
                // A key-ring outage is not an invalid user credential. BFFs preserve cookies on 5xx.
                throw new InvalidOperationException("Refresh recovery is temporarily unavailable.", error);
            }
            if (jwtService.HashRefreshToken(raw) != current.ReplacedByTokenHash) return null;
            var next = await userAccountRepository.GetRefreshTokenByHashAsync(current.ReplacedByTokenHash!, ct);
            if (next is null || next.UserAccountId != original.UserAccountId || FamilyOf(next) != FamilyOf(original)
                || next.ExpiresAtUtc <= now) return null;
            if (next.RevokedAtUtc is null) return Tokens(next.UserAccount, raw);
            if (next.RevokedAtUtc is not { } revoked || revoked > now || now - revoked > RotationRecoveryWindow) return null;
            current = next;
        }
        return null;
    }

    private AuthTokenResponse Tokens(UserAccount user, string refreshToken) => new(
        jwtService.GenerateAccessToken(user), refreshToken, jwtService.GetAccessTokenExpirationUtc());

    private static Guid FamilyOf(UserRefreshToken token) => token.FamilyId == Guid.Empty ? token.Id : token.FamilyId;

    public async Task LogoutAsync(RefreshRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
            return;

        var tokenHash = jwtService.HashRefreshToken(request.RefreshToken);
        var revoked = await userAccountRepository.RevokeActiveRefreshTokenByHashAsync(tokenHash, ct);
        if (revoked)
            await userAccountRepository.SaveChangesAsync(ct);
    }

    public async Task<AuthTokenResponse> SignInExternalAsync(UserAccount user, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        user.FailedLoginAttempts = 0;
        user.LockoutUntilUtc = null;
        user.LastLoginAtUtc = now;
        user.UpdatedAtUtc = now;
        var response = await IssueTokensAsync(user, ct);
        await userAccountRepository.SaveChangesAsync(ct);
        return response;
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
            FamilyId = Guid.NewGuid(),
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
        if (email.Trim().EndsWith("@rso.invalid", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("This email domain is reserved for external sign-in.", nameof(email));
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

}
