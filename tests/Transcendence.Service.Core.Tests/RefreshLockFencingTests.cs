using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Transcendence.Data;
using Transcendence.Data.Repositories.Implementations;
using Transcendence.Service.Core.Services.Jobs;
using Transcendence.Service.Core.Tests.Support;

namespace Transcendence.Service.Core.Tests;

// P0 finding: refresh locks were released by KEY, so a stale holder whose lease had already expired
// (and been re-acquired by someone else) would free the NEW owner's lock, letting a third refresh in.
// Fenced acquire/release rotate a per-acquisition token and release only when it still matches.
public sealed class RefreshLockFencingTests
{
    private static async Task<TranscendenceContext> CreateContextAsync()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TranscendenceContext>().UseSqlite(connection).Options;
        var context = new SqliteCompatibleTranscendenceContext(options);
        await context.Database.EnsureCreatedAsync();
        return context;
    }

    [Fact]
    public async Task ReleaseOwnedAsync_FromAStaleHolder_DoesNotFreeAReacquiredLock()
    {
        await using var context = await CreateContextAsync();
        var repo = new RefreshLockRepository(context);
        const string key = "summoner-refresh:NA1:KRONIC:NA1";

        // Holder A acquires.
        var tokenA = await repo.TryAcquireOwnedAsync(key, TimeSpan.FromMinutes(15));
        tokenA.Should().NotBeNull();

        // A's lease elapses (its consumer ran late, past TTL).
        var row = await context.RefreshLocks.SingleAsync(x => x.Key == key);
        row.LockedUntilUtc = DateTime.UtcNow.AddMinutes(-1);
        await context.SaveChangesAsync();

        // Holder B re-acquires the now-expired lock and gets a fresh, different token.
        var tokenB = await repo.TryAcquireOwnedAsync(key, TimeSpan.FromMinutes(15));
        tokenB.Should().NotBeNull();
        tokenB!.Value.Should().NotBe(tokenA!.Value);

        // A (finally running) releases with its STALE token — must be a no-op.
        await repo.ReleaseOwnedAsync(key, tokenA.Value);

        var afterStaleRelease = await repo.GetAsync(key);
        afterStaleRelease!.LockedUntilUtc.Should().BeAfter(DateTime.UtcNow,
            "B's lock must survive A's stale release");
        afterStaleRelease.OwnerToken.Should().Be(tokenB.Value);

        // B releases with its own token — succeeds.
        await repo.ReleaseOwnedAsync(key, tokenB.Value);
        var afterOwnerRelease = await repo.GetAsync(key);
        afterOwnerRelease!.LockedUntilUtc.Should().BeOnOrBefore(DateTime.UtcNow,
            "the true owner's release frees the lock");
    }

    [Fact]
    public async Task TryAcquireOwnedAsync_WhenActivelyHeld_ReturnsNull()
    {
        await using var context = await CreateContextAsync();
        var repo = new RefreshLockRepository(context);
        const string key = "refresh-priority:api:NA1:KRONIC:NA1";

        var first = await repo.TryAcquireOwnedAsync(key, TimeSpan.FromMinutes(15));
        first.Should().NotBeNull();

        var second = await repo.TryAcquireOwnedAsync(key, TimeSpan.FromMinutes(15));
        second.Should().BeNull("the lock is still actively held");
    }

    [Fact]
    public void OwnedHandle_RoundTrips_AndLegacyPlainKeyYieldsNoToken()
    {
        var key = "summoner-refresh:NA1:KRONIC:NA1";
        var token = Guid.NewGuid();

        var handle = RefreshLockKeys.BuildOwnedHandle(key, token);
        var (parsedKey, parsedToken) = RefreshLockKeys.ParseOwnedHandle(handle);
        parsedKey.Should().Be(key);
        parsedToken.Should().Be(token);

        // A legacy job enqueued before fencing carries a plain key → no token → release-by-key fallback.
        var (legacyKey, legacyToken) = RefreshLockKeys.ParseOwnedHandle(key);
        legacyKey.Should().Be(key);
        legacyToken.Should().BeNull();
    }
}
