using System.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Static;
using Transcendence.Service.Core.Services.Analytics.Interfaces;
using Transcendence.Service.Core.Services.Jobs;

namespace Transcendence.IntegrationTests;

[Collection(PostgresIntegrationCollection.Name)]
public sealed class BuildResourceRefreshLockPostgresTests(PostgresIntegrationFixture fixture)
{
    [Fact]
    public async Task ExecuteAsync_WhenAnotherSessionOwnsAdvisoryLock_SkipsRefresh()
    {
        const string lockResource = "transcendence:build-atlas-refresh";
        var patch = $"lock-{Guid.NewGuid():N}"[..12];
        await using var lockDb = NewDb();
        await using var jobDb = NewDb();
        lockDb.Patches.Add(new Patch
        {
            Version = patch,
            ReleaseDate = DateTime.UtcNow,
            DetectedAt = DateTime.UtcNow,
            IsActive = true
        });
        await lockDb.SaveChangesAsync();

        await lockDb.Database.OpenConnectionAsync();
        await using var acquire = lockDb.Database.GetDbConnection().CreateCommand();
        acquire.CommandText = "SELECT pg_try_advisory_lock(hashtextextended(@resource, 0));";
        var acquireParameter = acquire.CreateParameter();
        acquireParameter.ParameterName = "resource";
        acquireParameter.Value = lockResource;
        acquire.Parameters.Add(acquireParameter);
        (await acquire.ExecuteScalarAsync()).Should().Be(true);

        try
        {
            var refresher = new RecordingRefresher();
            var job = new RefreshBuildResourceAnalyticsJob(
                jobDb,
                refresher,
                NullLogger<RefreshBuildResourceAnalyticsJob>.Instance);

            await job.ExecuteAsync(onlyIfMissing: false, forceFullRebuild: false, CancellationToken.None);

            refresher.CallCount.Should().Be(0);
            jobDb.Database.GetDbConnection().State.Should().Be(ConnectionState.Closed);
        }
        finally
        {
            await using var release = lockDb.Database.GetDbConnection().CreateCommand();
            release.CommandText = "SELECT pg_advisory_unlock(hashtextextended(@resource, 0));";
            var releaseParameter = release.CreateParameter();
            releaseParameter.ParameterName = "resource";
            releaseParameter.Value = lockResource;
            release.Parameters.Add(releaseParameter);
            await release.ExecuteScalarAsync();

            await lockDb.Patches
                .Where(candidate => candidate.Version == patch)
                .ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task ExecuteAsync_WhenRefreshFails_ReleasesAdvisoryLock()
    {
        const string lockResource = "transcendence:build-atlas-refresh";
        var patch = $"lock-{Guid.NewGuid():N}"[..12];
        await using var jobDb = NewDb();
        await using var verifyDb = NewDb();
        jobDb.Patches.Add(new Patch
        {
            Version = patch,
            ReleaseDate = DateTime.UtcNow,
            DetectedAt = DateTime.UtcNow,
            IsActive = true
        });
        await jobDb.SaveChangesAsync();

        try
        {
            var job = new RefreshBuildResourceAnalyticsJob(
                jobDb,
                new ThrowingRefresher(),
                NullLogger<RefreshBuildResourceAnalyticsJob>.Instance);

            var act = () => job.ExecuteAsync(
                onlyIfMissing: false,
                forceFullRebuild: false,
                CancellationToken.None);

            await act.Should().ThrowAsync<InvalidOperationException>();
            jobDb.Database.GetDbConnection().State.Should().Be(ConnectionState.Closed);

            await verifyDb.Database.OpenConnectionAsync();
            await using var acquire = verifyDb.Database.GetDbConnection().CreateCommand();
            acquire.CommandText = "SELECT pg_try_advisory_lock(hashtextextended(@resource, 0));";
            var parameter = acquire.CreateParameter();
            parameter.ParameterName = "resource";
            parameter.Value = lockResource;
            acquire.Parameters.Add(parameter);
            (await acquire.ExecuteScalarAsync()).Should().Be(true);

            await using var release = verifyDb.Database.GetDbConnection().CreateCommand();
            release.CommandText = "SELECT pg_advisory_unlock(hashtextextended(@resource, 0));";
            var releaseParameter = release.CreateParameter();
            releaseParameter.ParameterName = "resource";
            releaseParameter.Value = lockResource;
            release.Parameters.Add(releaseParameter);
            await release.ExecuteScalarAsync();
        }
        finally
        {
            await jobDb.Patches
                .Where(candidate => candidate.Version == patch)
                .ExecuteDeleteAsync();
        }
    }

    private TranscendenceContext NewDb() =>
        new(new DbContextOptionsBuilder<TranscendenceContext>()
            .UseNpgsql(fixture.ConnectionString)
            .Options);

    private sealed class RecordingRefresher : IBuildResourceSnapshotRefresher
    {
        public int CallCount { get; private set; }

        public Task<BuildResourceSnapshotRefreshResult> RefreshAsync(
            string patch,
            bool forceFullRebuild,
            CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(new BuildResourceSnapshotRefreshResult(
                Guid.NewGuid(), patch, forceFullRebuild, 0, 0, 0));
        }
    }

    private sealed class ThrowingRefresher : IBuildResourceSnapshotRefresher
    {
        public Task<BuildResourceSnapshotRefreshResult> RefreshAsync(
            string patch,
            bool forceFullRebuild,
            CancellationToken ct) =>
            throw new InvalidOperationException("Expected test failure.");
    }
}
