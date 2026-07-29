using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Static;
using Transcendence.Service.Core.Services.Analytics.Interfaces;
using Transcendence.Service.Core.Services.Jobs;
using Transcendence.Service.Core.Tests.Support;

namespace Transcendence.Service.Core.Tests;

public class AnalyticsRefreshJobIsolationTests
{
    [Fact]
    public async Task TabularJob_DoesNotInvokeBuildProOrMatchupSurfaces()
    {
        await using var harness = await CreateHarnessAsync();
        var expected = new PrecomputedAnalyticsRefreshResult(1, 2, 3, 4);
        harness.Refresher
            .Setup(refresher => refresher.RefreshTabularCoreAsync("16.14", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);
        var job = new RefreshPrecomputedAnalyticsJob(
            harness.Db,
            harness.Refresher.Object,
            harness.Analytics.Object,
            NullLogger<RefreshPrecomputedAnalyticsJob>.Instance);

        await job.ExecuteAsync(CancellationToken.None);

        harness.Refresher.Verify(
            refresher => refresher.RefreshTabularCoreAsync("16.14", It.IsAny<CancellationToken>()),
            Times.Once);
        harness.Refresher.Verify(
            refresher => refresher.RefreshAllAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        harness.Refresher.Verify(
            refresher => refresher.RefreshBuildsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        harness.Refresher.Verify(
            refresher => refresher.RefreshMatchupsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        harness.Refresher.Verify(
            refresher => refresher.RefreshProSurfacesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task MatchupJob_InvokesOnlyMatchupSurface()
    {
        await using var harness = await CreateHarnessAsync();
        harness.Refresher
            .Setup(refresher => refresher.RefreshMatchupsAsync("16.14", It.IsAny<CancellationToken>()))
            .ReturnsAsync(12);
        var job = new RefreshChampionMatchupsJob(
            harness.Db,
            harness.Refresher.Object,
            harness.Analytics.Object,
            NullLogger<RefreshChampionMatchupsJob>.Instance);

        await job.ExecuteAsync(CancellationToken.None);

        harness.Refresher.Verify(
            refresher => refresher.RefreshMatchupsAsync("16.14", It.IsAny<CancellationToken>()),
            Times.Once);
        harness.Refresher.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task BuildSnapshotJob_InvokesOnlyBuildSurface()
    {
        await using var harness = await CreateHarnessAsync();
        harness.Refresher
            .Setup(refresher => refresher.RefreshBuildsAsync("16.14", It.IsAny<CancellationToken>()))
            .ReturnsAsync(34);
        var job = new RefreshChampionBuildSnapshotsJob(
            harness.Db,
            harness.Refresher.Object,
            harness.Analytics.Object,
            NullLogger<RefreshChampionBuildSnapshotsJob>.Instance);

        await job.ExecuteAsync(CancellationToken.None);

        harness.Refresher.Verify(
            refresher => refresher.RefreshBuildsAsync("16.14", It.IsAny<CancellationToken>()),
            Times.Once);
        harness.Refresher.VerifyNoOtherCalls();
    }

    private static async Task<Harness> CreateHarnessAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TranscendenceContext>()
            .UseSqlite(connection)
            .Options;
        var db = new SqliteCompatibleTranscendenceContext(options);
        await db.Database.EnsureCreatedAsync();
        db.Patches.Add(new Patch
        {
            Version = "16.14",
            ReleaseDate = DateTime.UtcNow,
            IsActive = true
        });
        await db.SaveChangesAsync();

        var analytics = new Mock<IChampionAnalyticsService>();
        analytics
            .Setup(service => service.InvalidateAnalyticsCacheForPatchAsync(
                "16.14",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return new Harness(connection, db, new Mock<IPrecomputedAnalyticsRefresher>(), analytics);
    }

    private sealed class Harness(
        SqliteConnection connection,
        TranscendenceContext db,
        Mock<IPrecomputedAnalyticsRefresher> refresher,
        Mock<IChampionAnalyticsService> analytics) : IAsyncDisposable
    {
        public TranscendenceContext Db { get; } = db;
        public Mock<IPrecomputedAnalyticsRefresher> Refresher { get; } = refresher;
        public Mock<IChampionAnalyticsService> Analytics { get; } = analytics;

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
