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

// Covers the P1 "auth crypto is untested" gap for the password path, plus the new per-account lockout.
// Exercises the real PBKDF2 hash/verify (register writes the hash, login must verify it) end-to-end.
public sealed class AuthLockoutAndCryptoTests
{
    private const string Email = "player@example.com";
    private const string Password = "correct-horse-battery"; // >= 12 chars
    private const string WrongPassword = "nope-nope-nope-1";

    // A UserAuthService wired to an in-memory single-account repo that mutates the stored account in
    // place, so state (failed-attempt counter, lockout) carries across successive Login calls.
    private static UserAuthService BuildService()
    {
        var repo = new Mock<IUserAccountRepository>();
        var jwt = new Mock<IJwtService>();
        jwt.Setup(x => x.GenerateAccessToken(It.IsAny<UserAccount>())).Returns("access");
        jwt.Setup(x => x.GetAccessTokenExpirationUtc()).Returns(DateTime.UtcNow.AddMinutes(15));
        jwt.Setup(x => x.GenerateRefreshToken()).Returns("refresh");
        jwt.Setup(x => x.HashRefreshToken(It.IsAny<string>())).Returns("refresh-hash");

        UserAccount? stored = null;
        repo.Setup(x => x.GetByEmailNormalizedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => stored);
        repo.Setup(x => x.AddUserAsync(It.IsAny<UserAccount>(), It.IsAny<CancellationToken>()))
            .Callback<UserAccount, CancellationToken>((u, _) => stored = u)
            .Returns(Task.CompletedTask);

        return new UserAuthService(repo.Object, jwt.Object,
            Options.Create(new AdminBootstrapOptions()), NullLogger<UserAuthService>.Instance);
    }

    [Fact]
    public async Task Register_then_login_roundtrips_the_pbkdf2_hash()
    {
        var svc = BuildService();
        (await svc.RegisterAsync(new RegisterRequest(Email, Password), default)).Should().NotBeNull();

        var login = await svc.LoginAsync(new LoginRequest(Email, Password), default);
        login.Should().NotBeNull("the PBKDF2 hash written at register must verify at login");
    }

    [Fact]
    public async Task Login_with_wrong_password_returns_null()
    {
        var svc = BuildService();
        await svc.RegisterAsync(new RegisterRequest(Email, Password), default);

        (await svc.LoginAsync(new LoginRequest(Email, WrongPassword), default)).Should().BeNull();
    }

    [Fact]
    public async Task Login_locks_account_after_max_failures_then_refuses_even_the_correct_password()
    {
        var svc = BuildService();
        await svc.RegisterAsync(new RegisterRequest(Email, Password), default);

        for (var i = 0; i < 10; i++)
            (await svc.LoginAsync(new LoginRequest(Email, WrongPassword), default)).Should().BeNull();

        (await svc.LoginAsync(new LoginRequest(Email, Password), default))
            .Should().BeNull("the account is locked out even for the correct password");
    }

    [Fact]
    public async Task Successful_login_clears_the_failure_counter()
    {
        var svc = BuildService();
        await svc.RegisterAsync(new RegisterRequest(Email, Password), default);

        await svc.LoginAsync(new LoginRequest(Email, WrongPassword), default);
        await svc.LoginAsync(new LoginRequest(Email, WrongPassword), default);
        (await svc.LoginAsync(new LoginRequest(Email, Password), default)).Should().NotBeNull();

        // Counter reset: a single subsequent failure is nowhere near the lockout threshold.
        await svc.LoginAsync(new LoginRequest(Email, WrongPassword), default);
        (await svc.LoginAsync(new LoginRequest(Email, Password), default))
            .Should().NotBeNull("a successful login reset the failed-attempt counter");
    }
}
