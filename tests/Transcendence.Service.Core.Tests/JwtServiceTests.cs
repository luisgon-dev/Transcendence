using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Transcendence.Data.Models.Auth;
using Transcendence.Service.Core.Services.Auth.Implementations;

namespace Transcendence.Service.Core.Tests;

public sealed class JwtServiceTests
{
    private const string Issuer = "Transcendence.Tests";
    private const string Audience = "Transcendence.TestClients";
    private const string SigningKey = "test-signing-key-with-at-least-thirty-two-bytes";

    [Fact]
    public void GenerateAccessToken_creates_a_valid_signed_token_with_identity_and_distinct_roles()
    {
        var service = CreateService();
        var user = new UserAccount
        {
            Id = Guid.NewGuid(),
            Email = "player@example.com",
            Roles =
            [
                new UserRole { Role = "admin" },
                new UserRole { Role = "admin" },
                new UserRole { Role = "analyst" }
            ]
        };

        var token = service.GenerateAccessToken(user);

        var principal = new JwtSecurityTokenHandler().ValidateToken(token, new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = Issuer,
            ValidateAudience = true,
            ValidAudience = Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
            NameClaimType = ClaimTypes.Name,
            RoleClaimType = ClaimTypes.Role
        }, out var validatedToken);

        validatedToken.Should().BeOfType<JwtSecurityToken>();
        principal.FindFirst(ClaimTypes.NameIdentifier)!.Value.Should().Be(user.Id.ToString());
        principal.Identity!.Name.Should().Be(user.Email);
        principal.FindAll(ClaimTypes.Role).Select(x => x.Value)
            .Should().BeEquivalentTo(["admin", "analyst"]);
        principal.IsInRole("admin").Should().BeTrue();
    }

    [Fact]
    public void GenerateAccessToken_uses_the_configured_expiration()
    {
        var before = DateTime.UtcNow.AddMinutes(4);
        var service = CreateService(accessTokenMinutes: 5);

        var token = new JwtSecurityTokenHandler().ReadJwtToken(service.GenerateAccessToken(new UserAccount
        {
            Id = Guid.NewGuid(),
            Email = "player@example.com"
        }));

        token.ValidTo.Should().BeOnOrAfter(before);
        token.ValidTo.Should().BeOnOrBefore(DateTime.UtcNow.AddMinutes(5));
    }

    [Fact]
    public void Refresh_tokens_are_random_and_hashing_is_deterministic()
    {
        var service = CreateService();

        var first = service.GenerateRefreshToken();
        var second = service.GenerateRefreshToken();

        Convert.FromBase64String(first).Should().HaveCount(64);
        first.Should().NotBe(second);
        service.HashRefreshToken(first).Should().Be(service.HashRefreshToken(first));
        service.HashRefreshToken(first).Should().NotBe(service.HashRefreshToken(second));
    }

    private static JwtService CreateService(int accessTokenMinutes = 15)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:Jwt:Issuer"] = Issuer,
                ["Auth:Jwt:Audience"] = Audience,
                ["Auth:Jwt:Key"] = SigningKey,
                ["Auth:Jwt:AccessTokenMinutes"] = accessTokenMinutes.ToString()
            })
            .Build();
        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(x => x.EnvironmentName).Returns("Production");
        return new JwtService(configuration, environment.Object);
    }
}
