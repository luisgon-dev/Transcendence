using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Transcendence.Data.Models.Auth;
using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Core.Services.Auth.Implementations;
using Transcendence.Service.Core.Services.Auth.Interfaces;
using Transcendence.Service.Core.Services.Auth.Models;

namespace Transcendence.Service.Core.Tests;

public class UserAuthServiceTests
{
    [Fact]
    public async Task LogoutAsync_WhenRefreshTokenProvided_RevokesAndSaves()
    {
        var repo = new Mock<IUserAccountRepository>();
        var jwt = new Mock<IJwtService>();
        jwt.Setup(x => x.HashRefreshToken("raw-token")).Returns("hash-token");
        repo.Setup(x => x.RevokeActiveRefreshTokenByHashAsync("hash-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var service = BuildService(repo.Object, jwt.Object);

        await service.LogoutAsync(new RefreshRequest("raw-token"), CancellationToken.None);

        repo.Verify(x => x.RevokeActiveRefreshTokenByHashAsync("hash-token", It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LogoutAsync_WhenRefreshTokenMissing_DoesNothing()
    {
        var repo = new Mock<IUserAccountRepository>();
        var jwt = new Mock<IJwtService>();

        var service = BuildService(repo.Object, jwt.Object);

        await service.LogoutAsync(new RefreshRequest(""), CancellationToken.None);

        repo.Verify(x => x.RevokeActiveRefreshTokenByHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        repo.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RegisterAsync_WhenPasswordTooShort_RequiresTwelveCharacters()
    {
        var repo = new Mock<IUserAccountRepository>();
        var jwt = new Mock<IJwtService>();

        var service = BuildService(repo.Object, jwt.Object);

        var act = async () => await service.RegisterAsync(
            new RegisterRequest("user@example.com", "shortpass"),
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*at least 12 characters*");
    }

    [Fact]
    public async Task RefreshAsync_WhenRotatedTokenReused_RevokesFamilyAndReturnsNull()
    {
        var repo = new Mock<IUserAccountRepository>();
        var jwt = new Mock<IJwtService>();
        var userId = Guid.NewGuid();
        jwt.Setup(x => x.HashRefreshToken("stolen-raw")).Returns("stolen-hash");
        repo.Setup(x => x.GetRefreshTokenByHashAsync("stolen-hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserRefreshToken
            {
                Id = Guid.NewGuid(),
                UserAccountId = userId,
                TokenHash = "stolen-hash",
                ExpiresAtUtc = DateTime.UtcNow.AddDays(1),
                RevokedAtUtc = DateTime.UtcNow.AddMinutes(-5), // already rotated
                ReplacedByTokenHash = "successor-hash"
            });
        repo.Setup(x => x.RevokeAllActiveRefreshTokensForUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);

        var service = BuildService(repo.Object, jwt.Object);

        var result = await service.RefreshAsync(new RefreshRequest("stolen-raw"), CancellationToken.None);

        result.Should().BeNull();
        repo.Verify(x => x.RevokeAllActiveRefreshTokensForUserAsync(userId, It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(x => x.AddRefreshTokenAsync(It.IsAny<UserRefreshToken>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RefreshAsync_WhenLoggedOutTokenReplayed_ReturnsNullWithoutFamilyRevoke()
    {
        var repo = new Mock<IUserAccountRepository>();
        var jwt = new Mock<IJwtService>();
        jwt.Setup(x => x.HashRefreshToken("loggedout-raw")).Returns("loggedout-hash");
        repo.Setup(x => x.GetRefreshTokenByHashAsync("loggedout-hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserRefreshToken
            {
                Id = Guid.NewGuid(),
                UserAccountId = Guid.NewGuid(),
                TokenHash = "loggedout-hash",
                ExpiresAtUtc = DateTime.UtcNow.AddDays(1),
                RevokedAtUtc = DateTime.UtcNow.AddMinutes(-5), // revoked by logout
                ReplacedByTokenHash = null                     // no successor
            });

        var service = BuildService(repo.Object, jwt.Object);

        var result = await service.RefreshAsync(new RefreshRequest("loggedout-raw"), CancellationToken.None);

        result.Should().BeNull();
        repo.Verify(x => x.RevokeAllActiveRefreshTokensForUserAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RefreshAsync_WhenActiveToken_RotatesAndIssuesTokens()
    {
        var repo = new Mock<IUserAccountRepository>();
        var jwt = new Mock<IJwtService>();
        var userId = Guid.NewGuid();
        jwt.Setup(x => x.HashRefreshToken("active-raw")).Returns("active-hash");
        jwt.Setup(x => x.GenerateRefreshToken()).Returns("new-raw");
        jwt.Setup(x => x.HashRefreshToken("new-raw")).Returns("new-hash");
        jwt.Setup(x => x.GenerateAccessToken(It.IsAny<UserAccount>())).Returns("access-jwt");
        jwt.Setup(x => x.GetAccessTokenExpirationUtc()).Returns(DateTime.UtcNow.AddMinutes(15));
        var current = new UserRefreshToken
        {
            Id = Guid.NewGuid(),
            UserAccountId = userId,
            TokenHash = "active-hash",
            ExpiresAtUtc = DateTime.UtcNow.AddDays(1),
            RevokedAtUtc = null,
            UserAccount = new UserAccount { Id = userId }
        };
        repo.Setup(x => x.GetRefreshTokenByHashAsync("active-hash", It.IsAny<CancellationToken>())).ReturnsAsync(current);

        var service = BuildService(repo.Object, jwt.Object);

        var result = await service.RefreshAsync(new RefreshRequest("active-raw"), CancellationToken.None);

        result.Should().NotBeNull();
        result!.AccessToken.Should().Be("access-jwt");
        result.RefreshToken.Should().Be("new-raw");
        repo.Verify(x => x.RevokeRefreshTokenAsync(current, "new-hash", It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(x => x.AddRefreshTokenAsync(
            It.Is<UserRefreshToken>(t => t.TokenHash == "new-hash"), It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(x => x.RevokeAllActiveRefreshTokensForUserAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RefreshAsync_WhenTokenNotFound_ReturnsNull()
    {
        var repo = new Mock<IUserAccountRepository>();
        var jwt = new Mock<IJwtService>();
        jwt.Setup(x => x.HashRefreshToken("nope-raw")).Returns("nope-hash");
        repo.Setup(x => x.GetRefreshTokenByHashAsync("nope-hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserRefreshToken?)null);

        var service = BuildService(repo.Object, jwt.Object);

        var result = await service.RefreshAsync(new RefreshRequest("nope-raw"), CancellationToken.None);

        result.Should().BeNull();
    }

    private static UserAuthService BuildService(IUserAccountRepository repo, IJwtService jwt)
    {
        return new UserAuthService(
            repo,
            jwt,
            Options.Create(new AdminBootstrapOptions()),
            NullLogger<UserAuthService>.Instance);
    }
}
