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

public sealed class PasswordResetServiceTests
{
    private static readonly PasswordResetOptions ConfiguredOptions = new()
    {
        Enabled = true,
        PublicBaseUrl = "https://transcend.kronic.one",
        TokenLifetimeMinutes = 30,
        Smtp = new SmtpOptions
        {
            Host = "smtp.example.com",
            FromAddress = "no-reply@example.com"
        }
    };

    [Fact]
    public async Task Initiate_and_complete_rotate_password_and_revoke_sessions()
    {
        var user = new UserAccount
        {
            Id = Guid.NewGuid(),
            Email = "player@example.com",
            EmailNormalized = "PLAYER@EXAMPLE.COM",
            PasswordHash = UserAuthService.HashPasswordForStorage("old-password-123")
        };
        var repository = new Mock<IUserAccountRepository>();
        var sender = new Mock<IPasswordResetEmailSender>();
        UserPasswordResetToken? storedToken = null;
        Uri? deliveredUrl = null;

        repository.Setup(x => x.GetByEmailNormalizedAsync(user.EmailNormalized, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        repository.Setup(x => x.AddPasswordResetTokenAsync(
                It.IsAny<UserPasswordResetToken>(), It.IsAny<CancellationToken>()))
            .Callback<UserPasswordResetToken, CancellationToken>((token, _) => storedToken = token)
            .Returns(Task.CompletedTask);
        sender.Setup(x => x.SendAsync(user.Email, It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
            .Callback<string, Uri, CancellationToken>((_, url, _) => deliveredUrl = url)
            .Returns(Task.CompletedTask);

        var service = CreateService(repository, sender);
        (await service.InitiateAsync(new PasswordResetRequest(user.Email))).Should().BeTrue();

        storedToken.Should().NotBeNull();
        deliveredUrl.Should().NotBeNull();
        var rawToken = Uri.UnescapeDataString(deliveredUrl!.Query["?token=".Length..]);
        rawToken.Should().NotBeNullOrWhiteSpace();
        storedToken!.TokenHash.Should().NotBe(rawToken, "only a SHA-256 hash is stored");

        repository.Setup(x => x.GetActivePasswordResetTokenAsync(
                storedToken.TokenHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(storedToken);
        storedToken.UserAccount = user;
        var previousHash = user.PasswordHash;

        (await service.CompleteAsync(new PasswordResetCompleteRequest(rawToken!, "new-password-456")))
            .Should().BeTrue();

        user.PasswordHash.Should().NotBe(previousHash);
        storedToken.UsedAtUtc.Should().NotBeNull();
        repository.Verify(x => x.RevokeAllActiveRefreshTokensForUserAsync(
            user.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Initiate_does_not_reveal_unknown_accounts()
    {
        var repository = new Mock<IUserAccountRepository>();
        var sender = new Mock<IPasswordResetEmailSender>();
        repository.Setup(x => x.GetByEmailNormalizedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserAccount?)null);
        var service = CreateService(repository, sender);

        (await service.InitiateAsync(new PasswordResetRequest("missing@example.com"))).Should().BeTrue();

        sender.Verify(x => x.SendAsync(It.IsAny<string>(), It.IsAny<Uri>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Complete_rejects_invalid_or_short_credentials()
    {
        var repository = new Mock<IUserAccountRepository>();
        var service = CreateService(repository, new Mock<IPasswordResetEmailSender>());

        (await service.CompleteAsync(new PasswordResetCompleteRequest("", "new-password-456"))).Should().BeFalse();
        (await service.CompleteAsync(new PasswordResetCompleteRequest("token", "short"))).Should().BeFalse();
        repository.Verify(x => x.GetActivePasswordResetTokenAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static PasswordResetService CreateService(
        Mock<IUserAccountRepository> repository,
        Mock<IPasswordResetEmailSender> sender) =>
        new(
            repository.Object,
            sender.Object,
            Options.Create(ConfiguredOptions),
            NullLogger<PasswordResetService>.Instance);
}
