using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
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

    private static UserAuthService BuildService(IUserAccountRepository repo, IJwtService jwt)
    {
        return new UserAuthService(
            repo,
            jwt,
            Options.Create(new AdminBootstrapOptions()),
            NullLogger<UserAuthService>.Instance);
    }
}
