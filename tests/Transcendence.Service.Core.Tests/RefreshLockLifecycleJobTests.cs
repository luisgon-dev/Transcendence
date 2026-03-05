using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Static;
using Transcendence.Data.Models.Service;
using Transcendence.Data.Repositories.Implementations;
using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Core.Services.Jobs;
using Transcendence.Service.Core.Services.Jobs.Configuration;

namespace Transcendence.Service.Core.Tests;

public class RefreshLockLifecycleJobTests
{
    [Fact]
    public async Task ExecuteAsync_DeletesOnlyLocksExpiredBeyondForensicsWindow()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<TranscendenceContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new TestSqliteTranscendenceContext(options);
        await db.Database.EnsureCreatedAsync();

        var now = DateTime.UtcNow;
        var oldExpiredKey = "lock:expired:old";
        var recentExpiredKey = "lock:expired:recent";
        var activeKey = "lock:active";

        db.RefreshLocks.AddRange(
            new RefreshLock
            {
                Id = Guid.NewGuid(),
                Key = oldExpiredKey,
                CreatedAtUtc = now.AddHours(-6),
                LockedUntilUtc = now.AddHours(-2)
            },
            new RefreshLock
            {
                Id = Guid.NewGuid(),
                Key = recentExpiredKey,
                CreatedAtUtc = now.AddHours(-2),
                LockedUntilUtc = now.AddMinutes(-10)
            },
            new RefreshLock
            {
                Id = Guid.NewGuid(),
                Key = activeKey,
                CreatedAtUtc = now.AddMinutes(-5),
                LockedUntilUtc = now.AddMinutes(20)
            });
        await db.SaveChangesAsync();

        var repository = new RefreshLockRepository(db);
        var job = new RefreshLockLifecycleJob(
            repository,
            CreateScheduleOptions(forensicsWindowMinutes: 30, batchSize: 25, maxBatches: 2),
            Mock.Of<ILogger<RefreshLockLifecycleJob>>());

        await job.ExecuteAsync(CancellationToken.None);

        var remainingKeys = await db.RefreshLocks
            .AsNoTracking()
            .Select(x => x.Key)
            .ToListAsync();

        remainingKeys.Should().Contain(recentExpiredKey);
        remainingKeys.Should().Contain(activeKey);
        remainingKeys.Should().NotContain(oldExpiredKey);
    }

    [Fact]
    public async Task ExecuteAsync_UsesConfiguredBatchSizeAndStopsAtMaxBatches()
    {
        var repository = new Mock<IRefreshLockRepository>();
        var cutoffs = new List<DateTime>();

        repository
            .Setup(x => x.DeleteExpiredAsync(It.IsAny<DateTime>(), 2, It.IsAny<CancellationToken>()))
            .Callback<DateTime, int, CancellationToken>((cutoff, _, _) => cutoffs.Add(cutoff))
            .ReturnsAsync(2);
        repository
            .Setup(x => x.GetGrowthSnapshotAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RefreshLockGrowthSnapshot(ActiveCount: 5, ExpiredCount: 8));

        var job = new RefreshLockLifecycleJob(
            repository.Object,
            CreateScheduleOptions(forensicsWindowMinutes: 30, batchSize: 2, maxBatches: 3),
            Mock.Of<ILogger<RefreshLockLifecycleJob>>());

        await job.ExecuteAsync(CancellationToken.None);

        repository.Verify(
            x => x.DeleteExpiredAsync(It.IsAny<DateTime>(), 2, It.IsAny<CancellationToken>()),
            Times.Exactly(3));
        repository.Verify(
            x => x.GetGrowthSnapshotAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Once);

        cutoffs.Should().HaveCount(3);
        cutoffs.Should().OnlyContain(cutoff => cutoff <= DateTime.UtcNow.AddMinutes(-29));
    }

    [Fact]
    public async Task ExecuteAsync_WhenCleanupThrows_DoesNotPropagateException()
    {
        var repository = new Mock<IRefreshLockRepository>();
        repository
            .Setup(x => x.DeleteExpiredAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated cleanup failure"));

        var job = new RefreshLockLifecycleJob(
            repository.Object,
            CreateScheduleOptions(),
            Mock.Of<ILogger<RefreshLockLifecycleJob>>());

        Func<Task> act = async () => await job.ExecuteAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
        repository.Verify(
            x => x.GetGrowthSnapshotAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static IOptions<WorkerJobScheduleOptions> CreateScheduleOptions(
        int forensicsWindowMinutes = 30,
        int batchSize = 250,
        int maxBatches = 8,
        bool enabled = true) =>
        Options.Create(new WorkerJobScheduleOptions
        {
            EnableRefreshLockLifecycleCleanup = enabled,
            RefreshLockLifecycleForensicsWindowMinutes = forensicsWindowMinutes,
            RefreshLockLifecycleCleanupBatchSize = batchSize,
            RefreshLockLifecycleCleanupMaxBatchesPerRun = maxBatches
        });

    private sealed class TestSqliteTranscendenceContext(DbContextOptions<TranscendenceContext> options)
        : TranscendenceContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<ItemVersion>()
                .Property(x => x.BuildsFrom)
                .HasDefaultValueSql("'[]'");
            modelBuilder.Entity<ItemVersion>()
                .Property(x => x.BuildsInto)
                .HasDefaultValueSql("'[]'");
        }
    }
}
