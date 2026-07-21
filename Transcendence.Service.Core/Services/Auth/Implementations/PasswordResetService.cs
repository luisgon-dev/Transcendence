using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Transcendence.Data.Models.Auth;
using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Core.Services.Auth.Interfaces;
using Transcendence.Service.Core.Services.Auth.Models;

namespace Transcendence.Service.Core.Services.Auth.Implementations;

public sealed class PasswordResetService(
    IUserAccountRepository userAccountRepository,
    IPasswordResetEmailSender emailSender,
    IOptions<PasswordResetOptions> options,
    ILogger<PasswordResetService> logger) : IPasswordResetService
{
    public async Task<bool> InitiateAsync(PasswordResetRequest request, CancellationToken ct = default)
    {
        var settings = options.Value;
        if (!settings.IsConfigured())
        {
            logger.LogWarning("Password reset requested while SMTP delivery is not configured.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.Email))
            return true;

        var user = await userAccountRepository.GetByEmailNormalizedAsync(
            request.Email.Trim().ToUpperInvariant(), ct);
        if (user == null)
            return true;

        var rawToken = CreateToken();
        var token = new UserPasswordResetToken
        {
            Id = Guid.NewGuid(),
            UserAccountId = user.Id,
            TokenHash = HashToken(rawToken),
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(settings.TokenLifetimeMinutes)
        };

        await userAccountRepository.RevokeActivePasswordResetTokensForUserAsync(user.Id, ct);
        await userAccountRepository.AddPasswordResetTokenAsync(token, ct);
        await userAccountRepository.SaveChangesAsync(ct);

        var baseUri = new Uri(settings.PublicBaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        var resetUrl = new Uri(baseUri, $"account/reset-password?token={Uri.EscapeDataString(rawToken)}");
        try
        {
            await emailSender.SendAsync(user.Email, resetUrl, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Keep the public response non-enumerating; operations still gets a delivery failure with
            // a stable internal identifier and no account email/PII.
            logger.LogError(ex, "Password reset email delivery failed for user {UserAccountId}.", user.Id);
        }

        return true;
    }

    public async Task<bool> CompleteAsync(PasswordResetCompleteRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Token) ||
            string.IsNullOrWhiteSpace(request.NewPassword) ||
            request.NewPassword.Length < UserAuthService.MinimumPasswordLength)
            return false;

        var resetToken = await userAccountRepository.GetActivePasswordResetTokenAsync(
            HashToken(request.Token), ct);
        if (resetToken == null)
            return false;

        var now = DateTime.UtcNow;
        resetToken.UserAccount.PasswordHash = UserAuthService.HashPasswordForStorage(request.NewPassword);
        resetToken.UserAccount.FailedLoginAttempts = 0;
        resetToken.UserAccount.LockoutUntilUtc = null;
        resetToken.UserAccount.UpdatedAtUtc = now;
        resetToken.UsedAtUtc = now;

        await userAccountRepository.RevokeActivePasswordResetTokensForUserAsync(
            resetToken.UserAccountId, ct);
        await userAccountRepository.RevokeAllActiveRefreshTokensForUserAsync(
            resetToken.UserAccountId, ct);
        await userAccountRepository.SaveChangesAsync(ct);
        return true;
    }

    private static string CreateToken()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string HashToken(string token)
    {
        return Convert.ToBase64String(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));
    }
}
