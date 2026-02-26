using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Hosting;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Transcendence.Data.Models.Auth;
using Transcendence.Service.Core.Services.Auth.Interfaces;

namespace Transcendence.Service.Core.Services.Auth.Implementations;

public class JwtService(IConfiguration configuration, IHostEnvironment hostEnvironment) : IJwtService
{
    private const string DevelopmentFallbackSigningKey = "CHANGE_THIS_DEV_ONLY_KEY_32_CHARS_MINIMUM";

    private readonly string _issuer = configuration["Auth:Jwt:Issuer"] ?? "Transcendence";
    private readonly string _audience = configuration["Auth:Jwt:Audience"] ?? "TranscendenceClients";
    private readonly string _signingKey = ResolveSigningKey(configuration["Auth:Jwt:Key"], hostEnvironment);
    private readonly int _accessTokenMinutes = ParseInt(configuration["Auth:Jwt:AccessTokenMinutes"], 15);

    public string GenerateAccessToken(UserAccount user)
    {
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_signingKey)),
            SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Email)
        };

        foreach (var role in user.Roles.Select(x => x.Role).Distinct(StringComparer.Ordinal))
            claims.Add(new Claim(ClaimTypes.Role, role));

        var expires = GetAccessTokenExpirationUtc();
        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: claims,
            expires: expires,
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public DateTime GetAccessTokenExpirationUtc()
    {
        return DateTime.UtcNow.AddMinutes(_accessTokenMinutes);
    }

    public string GenerateRefreshToken()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
    }

    public string HashRefreshToken(string refreshToken)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static int ParseInt(string? value, int fallback)
    {
        return int.TryParse(value, out var parsed) ? parsed : fallback;
    }

    private static string ResolveSigningKey(string? configuredKey, IHostEnvironment hostEnvironment)
    {
        if (string.IsNullOrWhiteSpace(configuredKey))
        {
            if (hostEnvironment.IsDevelopment())
                return DevelopmentFallbackSigningKey;

            throw new InvalidOperationException(
                "Missing Auth:Jwt:Key configuration. Configure a secure signing key outside Development.");
        }

        var signingKey = configuredKey.Trim();
        if (!hostEnvironment.IsDevelopment()
            && string.Equals(signingKey, DevelopmentFallbackSigningKey, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Auth:Jwt:Key is using the development placeholder. Configure a secure signing key outside Development.");
        }

        return signingKey;
    }
}
