using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Diagnostics;
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
    private static UserAuthService BuildService(IJwtService? jwtService = null)
    {
        var repo = new Mock<IUserAccountRepository>();
        if (jwtService == null)
        {
            var jwt = new Mock<IJwtService>();
            jwt.Setup(x => x.GenerateAccessToken(It.IsAny<UserAccount>())).Returns("access");
            jwt.Setup(x => x.GetAccessTokenExpirationUtc()).Returns(DateTime.UtcNow.AddMinutes(15));
            jwt.Setup(x => x.GenerateRefreshToken()).Returns("refresh");
            jwt.Setup(x => x.HashRefreshToken(It.IsAny<string>())).Returns("refresh-hash");
            jwtService = jwt.Object;
        }

        UserAccount? stored = null;
        repo.Setup(x => x.GetByEmailNormalizedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => stored);
        repo.Setup(x => x.AddUserAsync(It.IsAny<UserAccount>(), It.IsAny<CancellationToken>()))
            .Callback<UserAccount, CancellationToken>((u, _) => stored = u)
            .Returns(Task.CompletedTask);

        return new UserAuthService(repo.Object, jwtService,
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
    public async Task Login_with_unknown_account_still_runs_current_cost_password_derivation()
    {
        var svc = BuildService();
        var stopwatch = Stopwatch.StartNew();

        (await svc.LoginAsync(new LoginRequest("missing@example.com", WrongPassword), default)).Should().BeNull();

        stopwatch.Stop();
        stopwatch.Elapsed.Should().BeGreaterThan(
            TimeSpan.FromMilliseconds(10),
            "the unknown-account path must not skip the 310,000-iteration PBKDF2 timing defense");
    }

    [Fact]
    public async Task Login_with_real_jwt_service_handles_unknown_bad_and_success_paths()
    {
        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(x => x.EnvironmentName).Returns("Production");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:Jwt:Issuer"] = "Transcendence.Tests",
                ["Auth:Jwt:Audience"] = "Transcendence.TestClients",
                ["Auth:Jwt:Key"] = "test-signing-key-with-at-least-thirty-two-bytes"
            })
            .Build();
        var svc = BuildService(new JwtService(configuration, environment.Object));

        (await svc.LoginAsync(new LoginRequest(Email, Password), default)).Should().BeNull();
        await svc.RegisterAsync(new RegisterRequest(Email, Password), default);
        (await svc.LoginAsync(new LoginRequest(Email, WrongPassword), default)).Should().BeNull();

        var result = await svc.LoginAsync(new LoginRequest(Email, Password), default);

        result.Should().NotBeNull();
        new JwtSecurityTokenHandler().ReadJwtToken(result!.AccessToken).Issuer
            .Should().Be("Transcendence.Tests");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-password-hash")]
    [InlineData("pbkdf2$not-an-int$c2FsdA==$aGFzaA==")]
    [InlineData("pbkdf2$0$c2FsdA==$aGFzaA==")]
    [InlineData("pbkdf2$1000$not-base64$also-not-base64")]
    [InlineData("pbkdf2$1000$$")]
    public async Task Login_with_malformed_stored_hash_rejects_without_throwing(string storedHash)
    {
        var user = BuildUser(storedHash);
        var (svc, repo) = BuildService(user);

        var act = async () => await svc.LoginAsync(new LoginRequest(Email, Password), default);

        (await act.Should().NotThrowAsync()).Which.Should().BeNull();
        user.FailedLoginAttempts.Should().Be(1);
        repo.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Login_with_legacy_cost_hash_upgrades_to_current_iterations()
    {
        const int legacyIterations = 10_000;
        var user = BuildUser(HashPassword(Password, legacyIterations));
        var originalHash = user.PasswordHash;
        var (svc, repo) = BuildService(user);

        var result = await svc.LoginAsync(new LoginRequest(Email, Password), default);

        result.Should().NotBeNull();
        user.PasswordHash.Should().NotBe(originalHash);
        user.PasswordHash.Should().StartWith("pbkdf2$310000$");
        repo.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
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

    private static (UserAuthService Service, Mock<IUserAccountRepository> Repository) BuildService(
        UserAccount stored)
    {
        var repo = new Mock<IUserAccountRepository>();
        var jwt = new Mock<IJwtService>();
        jwt.Setup(x => x.GenerateAccessToken(stored)).Returns("access");
        jwt.Setup(x => x.GetAccessTokenExpirationUtc()).Returns(DateTime.UtcNow.AddMinutes(15));
        jwt.Setup(x => x.GenerateRefreshToken()).Returns("refresh");
        jwt.Setup(x => x.HashRefreshToken("refresh")).Returns("refresh-hash");
        repo.Setup(x => x.GetByEmailNormalizedAsync(Email.ToUpperInvariant(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);

        var service = new UserAuthService(
            repo.Object,
            jwt.Object,
            Options.Create(new AdminBootstrapOptions()),
            NullLogger<UserAuthService>.Instance);
        return (service, repo);
    }

    private static UserAccount BuildUser(string passwordHash) => new()
    {
        Id = Guid.NewGuid(),
        Email = Email,
        EmailNormalized = Email.ToUpperInvariant(),
        PasswordHash = passwordHash
    };

    private static string HashPassword(string password, int iterations)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            32);
        return $"pbkdf2${iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }
}
