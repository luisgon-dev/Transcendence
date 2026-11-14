using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Transcendence.Data;
using Transcendence.Data.Models.Auth;
using Transcendence.Data.Repositories.Implementations;
using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Core.Services.Auth.Implementations;
using Transcendence.Service.Core.Services.Auth.Interfaces;
using Transcendence.Service.Core.Services.Auth.Models;
using Transcendence.Service.Core.Services.Operations;

namespace Transcendence.IntegrationTests;

[Collection(PostgresIntegrationCollection.Name)]
public sealed class AuthTokenConcurrencyTests(PostgresIntegrationFixture fixture)
{
    private readonly IDataProtectionProvider protection = new EphemeralDataProtectionProvider();
    private const string Password = "Regression-password-123!";
    private TranscendenceContext NewDb() => new(new DbContextOptionsBuilder<TranscendenceContext>()
        .UseNpgsql(fixture.ConnectionString).Options);

    private UserAuthService Auth(IUserAccountRepository repository)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtService>();
        return new UserAuthService(repository, jwt, protection, NullLogger<UserAuthService>.Instance);
    }

    private async Task<(Guid Id, string Email, AuthTokenResponse Tokens)> RegisterAsync()
    {
        await using var db = NewDb();
        var email = $"audit-{Guid.NewGuid():N}@example.invalid";
        var result = await Auth(new UserAccountRepository(db)).RegisterAsync(new(email, Password));
        return (await db.UserAccounts.Where(x => x.Email == email).Select(x => x.Id).SingleAsync(), email, result);
    }

    [Fact]
    public async Task Retention_scrubs_recovery_secrets_but_keeps_unexpired_replay_evidence()
    {
        var user = await RegisterAsync();
        await using var db = NewDb();
        var original = await db.UserRefreshTokens.SingleAsync(x => x.UserAccountId == user.Id);
        original.RevokedAtUtc = DateTime.UtcNow.AddMinutes(-1);
        original.ProtectedReplacementToken = "encrypted-test-secret";
        var expired = new UserRefreshToken { Id = Guid.NewGuid(), UserAccountId = user.Id,
            TokenHash = Guid.NewGuid().ToString(), ExpiresAtUtc = DateTime.UtcNow.AddDays(-2) };
        db.UserRefreshTokens.Add(expired); await db.SaveChangesAsync();
        using var telemetry = new OperationTelemetry();
        await new OperationMaintenance(db, Options.Create(new OperationRetentionOptions()), telemetry)
            .RunBatchAsync(DateTime.UtcNow);
        (await db.UserRefreshTokens.AsNoTracking().AnyAsync(x => x.Id == expired.Id)).Should().BeFalse();
        var retained = await db.UserRefreshTokens.AsNoTracking().SingleAsync(x => x.Id == original.Id);
        retained.TokenHash.Should().Be(original.TokenHash);
        retained.ProtectedReplacementToken.Should().BeNull();
        retained.RevokedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task Concurrent_refreshes_recover_one_successor_and_do_not_revoke_it()
    {
        var user = await RegisterAsync();
        await using var left = NewDb();
        await using var right = NewDb();
        var gate = new ReadGate();
        var results = await Task.WhenAll(
            Auth(ReadBarrier.Wrap(new UserAccountRepository(left), gate, "GetRefreshTokenByHashAsync"))
                .RefreshAsync(new(user.Tokens.RefreshToken)),
            Auth(ReadBarrier.Wrap(new UserAccountRepository(right), gate, "GetRefreshTokenByHashAsync"))
                .RefreshAsync(new(user.Tokens.RefreshToken)));
        results.Should().OnlyContain(x => x != null);
        results[0]!.RefreshToken.Should().Be(results[1]!.RefreshToken);
        await using var check = NewDb();
        (await check.UserRefreshTokens.CountAsync(x => x.UserAccountId == user.Id && x.RevokedAtUtc == null))
            .Should().Be(1);
        var retry = await Auth(new UserAccountRepository(check)).RefreshAsync(new(user.Tokens.RefreshToken));
        retry!.RefreshToken.Should().Be(results[0]!.RefreshToken);
    }

    [Fact]
    public async Task Replay_outside_recovery_window_revokes_only_its_session_family()
    {
        var user = await RegisterAsync();
        await using var db = NewDb();
        var service = Auth(new UserAccountRepository(db));
        var rotated = await service.RefreshAsync(new(user.Tokens.RefreshToken));
        var otherSession = await service.LoginAsync(new(user.Email, Password));
        using var scope = fixture.Factory.Services.CreateScope();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtService>();
        var originalHash = jwt.HashRefreshToken(user.Tokens.RefreshToken);
        await db.UserRefreshTokens.Where(x => x.TokenHash == originalHash)
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.RevokedAtUtc, DateTime.UtcNow.AddMinutes(-1)));
        (await service.RefreshAsync(new(user.Tokens.RefreshToken))).Should().BeNull();
        var rotatedHash = jwt.HashRefreshToken(rotated!.RefreshToken);
        var otherHash = jwt.HashRefreshToken(otherSession!.RefreshToken);
        (await db.UserRefreshTokens.AsNoTracking().SingleAsync(x => x.TokenHash == rotatedHash)).RevokedAtUtc.Should().NotBeNull();
        (await db.UserRefreshTokens.AsNoTracking().SingleAsync(x => x.TokenHash == otherHash)).RevokedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task Concurrent_password_resets_consume_the_token_once_and_revoke_sessions()
    {
        var user = await RegisterAsync();
        var raw = Guid.NewGuid().ToString();
        await using (var seed = NewDb())
        {
            seed.UserPasswordResetTokens.Add(new UserPasswordResetToken
            {
                Id = Guid.NewGuid(), UserAccountId = user.Id,
                TokenHash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(raw))),
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(30)
            });
            await seed.SaveChangesAsync();
        }
        await using var left = NewDb();
        await using var right = NewDb();
        var gate = new ReadGate();
        PasswordResetService Reset(IUserAccountRepository repository) => new(repository, new NoEmail(),
            Options.Create(new PasswordResetOptions()), NullLogger<PasswordResetService>.Instance);
        var attempts = await Task.WhenAll(
            Reset(ReadBarrier.Wrap(new UserAccountRepository(left), gate, "GetActivePasswordResetTokenAsync"))
                .CompleteAsync(new(raw, "First-new-password-123!")),
            Reset(ReadBarrier.Wrap(new UserAccountRepository(right), gate, "GetActivePasswordResetTokenAsync"))
                .CompleteAsync(new(raw, "Second-new-password-123!")));
        attempts.Count(x => x).Should().Be(1);
        await using var check = NewDb();
        (await check.UserRefreshTokens.CountAsync(x => x.UserAccountId == user.Id && x.RevokedAtUtc == null)).Should().Be(0);
    }

    [Fact]
    public async Task Logout_during_rotation_revokes_the_successor()
    {
        var user = await RegisterAsync();
        await using var db = NewDb();
        var service = Auth(new UserAccountRepository(db));
        await service.RefreshAsync(new(user.Tokens.RefreshToken));
        await service.LogoutAsync(new(user.Tokens.RefreshToken));
        (await db.UserRefreshTokens.CountAsync(x => x.UserAccountId == user.Id && x.RevokedAtUtc == null)).Should().Be(0);
        (await service.RefreshAsync(new(user.Tokens.RefreshToken))).Should().BeNull();
    }

    [Fact]
    public async Task Public_registration_does_not_grant_configured_admin_email_and_operator_grant_is_audited()
    {
        var user = await RegisterAsync();
        using var scope = fixture.Factory.Services.CreateScope();
        var legacy = scope.ServiceProvider.GetRequiredService<IOptions<AdminBootstrapOptions>>().Value;
        var previous = legacy.Emails;
        legacy.Emails = [user.Email];
        try
        {
            await using var db = NewDb();
            var auth = Auth(new UserAccountRepository(db));
            await auth.LoginAsync(new(user.Email, Password));
            (await db.UserRoles.AnyAsync(x => x.UserAccountId == user.Id)).Should().BeFalse();
            var bootstrap = new AdminBootstrapService(new UserAccountRepository(db), new AdminAuditService(db));
            (await bootstrap.GrantAsync(user.Id)).Should().BeTrue();
            (await bootstrap.GrantAsync(user.Id)).Should().BeFalse();
            (await db.UserRoles.CountAsync(x => x.UserAccountId == user.Id && x.Role == SystemRoles.Admin)).Should().Be(1);
            (await db.AdminAuditEvents.CountAsync(x => x.TargetId == user.Id.ToString() && x.Action == "admin.bootstrap"))
                .Should().Be(1);
            var invalid = () => auth.RegisterAsync(new RegisterRequest("riot-reserved@rso.invalid", Password));
            await invalid.Should().ThrowAsync<ArgumentException>();
        }
        finally { legacy.Emails = previous; }
    }

    private sealed class NoEmail : IPasswordResetEmailSender
    {
        public Task SendAsync(string email, Uri url, CancellationToken ct = default) => Task.CompletedTask;
    }

    public sealed class ReadGate
    {
        private int arrived;
        private readonly TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task JoinAsync()
        {
            if (Interlocked.Increment(ref arrived) == 2) gate.TrySetResult();
            await gate.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    public class ReadBarrier : DispatchProxy
    {
        public IUserAccountRepository Target = null!;
        public ReadGate Gate = null!;
        public string MethodName = "";
        public static IUserAccountRepository Wrap(IUserAccountRepository target, ReadGate gate, string method)
        {
            var proxy = Create<IUserAccountRepository, ReadBarrier>();
            var barrier = (ReadBarrier)proxy;
            barrier.Target = target; barrier.Gate = gate; barrier.MethodName = method;
            return proxy;
        }
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            var result = method!.Invoke(Target, args);
            if (method.Name != MethodName) return result;
            return result switch
            {
                Task<UserRefreshToken?> task => ReadThenWaitAsync(task),
                Task<UserPasswordResetToken?> task => ReadThenWaitAsync(task),
                _ => throw new InvalidOperationException("Unexpected read barrier method.")
            };
        }
        private async Task<T> ReadThenWaitAsync<T>(Task<T> task)
        {
            var result = await task;
            await Gate.JoinAsync();
            return result;
        }
    }
}
