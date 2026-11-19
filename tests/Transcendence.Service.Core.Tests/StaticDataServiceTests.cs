using System.Net;
using System.Text;
using FluentAssertions;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Static;
using Transcendence.Service.Core.Services.Cache;
using Transcendence.Service.Core.Services.Jobs;
using Transcendence.Service.Core.Services.Jobs.Configuration;
using Transcendence.Service.Core.Services.StaticData.Implementations;
using Transcendence.Service.Core.Tests.Support;

namespace Transcendence.Service.Core.Tests;

public class StaticDataServiceTests
{
    [Fact]
    public async Task DetectAndRefreshAsync_BootstrapQueuesAfterCommitAndInvalidatesTheActivePointer()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TranscendenceContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new SqliteCompatibleTranscendenceContext(options);
        await db.Database.EnsureCreatedAsync();

        var cache = new Mock<ICacheService>();
        cache.Setup(service => service.GetOrCreateAsync(
                It.IsAny<string>(),
                It.IsAny<Func<CancellationToken, Task<bool>>>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<string[]?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var httpFactory = new Mock<IHttpClientFactory>();
        httpFactory.Setup(factory => factory.CreateClient(It.IsAny<string>()))
            .Returns(new HttpClient(new JsonResponseHandler("[\"16.14.1\"]")));
        var jobs = new Mock<IBackgroundJobClient>(MockBehavior.Strict);
        KickoffObservation? kickoff = null;
        jobs.Setup(x => x.Create(It.IsAny<Job>(), It.IsAny<EnqueuedState>()))
            .Returns((Job job, IState _) =>
            {
                kickoff = ObserveKickoff(db, job);
                return "job";
            });
        var service = new StaticDataService(
            db,
            httpFactory.Object,
            cache.Object,
            Options.Create(new PatchPromotionOptions()),
            NullLogger<StaticDataService>.Instance,
            Options.Create(new WorkerJobScheduleOptions()),
            jobs.Object);

        await service.DetectAndRefreshAsync();

        (await db.Patches.SingleAsync()).Version.Should().Be("16.14");
        (await db.Patches.SingleAsync()).IsActive.Should().BeTrue();
        cache.Verify(
            value => value.RemoveAsync(AnalyticsCacheKeys.ActivePatch, It.IsAny<CancellationToken>()),
            Times.Once);
        jobs.Verify(x => x.Create(It.IsAny<Job>(), It.IsAny<EnqueuedState>()), Times.Once);
        AssertCommittedKickoff(kickoff, "16.14");
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task DetectAndRefreshAsync_QueuesOnlyForADurableTransition(bool enabled, bool queueFails)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TranscendenceContext>().UseSqlite(connection).Options;
        await using var db = new SqliteCompatibleTranscendenceContext(options);
        await db.Database.EnsureCreatedAsync();
        db.Patches.Add(new Patch { Version = "16.13", ReleaseDate = DateTime.UtcNow.AddDays(-14), IsActive = true });
        await db.SaveChangesAsync();

        var cache = new Mock<ICacheService>();
        cache.Setup(service => service.GetOrCreateAsync(
                It.IsAny<string>(), It.IsAny<Func<CancellationToken, Task<bool>>>(), It.IsAny<TimeSpan?>(),
                It.IsAny<TimeSpan?>(), It.IsAny<string[]?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var httpFactory = new Mock<IHttpClientFactory>();
        httpFactory.Setup(factory => factory.CreateClient(It.IsAny<string>()))
            .Returns(new HttpClient(new JsonResponseHandler("[\"16.14.1\"]")));
        var jobs = new Mock<IBackgroundJobClient>(MockBehavior.Strict);
        KickoffObservation? kickoff = null;
        jobs.Setup(x => x.Create(It.IsAny<Job>(), It.IsAny<EnqueuedState>()))
            .Returns((Job job, IState _) =>
            {
                kickoff = ObserveKickoff(db, job);
                return queueFails ? throw new InvalidOperationException("Hangfire unavailable") : "job";
            });
        var service = new StaticDataService(db, httpFactory.Object, cache.Object,
            Options.Create(new PatchPromotionOptions { MaxWaitHoursBeforeForcePromote = 1 }),
            NullLogger<StaticDataService>.Instance,
            Options.Create(new WorkerJobScheduleOptions { EnableRefreshPrecomputedAnalytics = enabled }), jobs.Object);

        await service.DetectAndRefreshAsync();

        (await db.Patches.SingleAsync(p => p.IsActive)).Version.Should().Be("16.13");
        jobs.VerifyNoOtherCalls();

        var detected = await db.Patches.SingleAsync(p => p.Version == "16.14");
        detected.DetectedAt = DateTime.UtcNow.AddHours(-2);
        await db.SaveChangesAsync();
        await service.DetectAndRefreshAsync();
        await service.DetectAndRefreshAsync();

        (await db.Patches.AsNoTracking().SingleAsync(p => p.IsActive)).Version.Should().Be("16.14");
        jobs.Verify(x => x.Create(It.IsAny<Job>(), It.IsAny<EnqueuedState>()), Times.Exactly(enabled ? 1 : 0));
        cache.Verify(x => x.RemoveAsync(AnalyticsCacheKeys.ActivePatch, It.IsAny<CancellationToken>()), Times.Once);
        if (enabled)
            AssertCommittedKickoff(kickoff, "16.14");
    }

    private static KickoffObservation ObserveKickoff(TranscendenceContext db, Job job) => new(
        job, db.Database.CurrentTransaction != null, db.Patches.AsNoTracking().Single(p => p.IsActive).Version);

    private static void AssertCommittedKickoff(KickoffObservation? kickoff, string patch)
    {
        kickoff.Should().NotBeNull();
        kickoff!.InTransaction.Should().BeFalse();
        kickoff.ActivePatch.Should().Be(patch);
        kickoff.Job.Type.Should().Be<RefreshPrecomputedAnalyticsJob>();
        kickoff.Job.Method.Name.Should().Be(nameof(RefreshPrecomputedAnalyticsJob.ExecuteForPatchAsync));
        kickoff.Job.Args[0].Should().Be(patch);
    }

    private sealed record KickoffObservation(Job Job, bool InTransaction, string ActivePatch);

    [Theory]
    // One removal per memoized static-data fetch: runes, items, and champion balance data.
    [InlineData(false, 3)]
    [InlineData(true, 0)]
    public async Task EnsureStaticDataForPatchAsync_RemovesOnlyNonAuthoritativeCacheEntries(
        bool shouldCache,
        int expectedRemoveCalls)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TranscendenceContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new SqliteCompatibleTranscendenceContext(options);
        await db.Database.EnsureCreatedAsync();
        db.Patches.Add(new Patch
        {
            Version = "16.14",
            ReleaseDate = DateTime.UtcNow,
            DetectedAt = DateTime.UtcNow,
            IsActive = true
        });
        await db.SaveChangesAsync();

        var cache = new Mock<ICacheService>();
        cache.Setup(service => service.GetOrCreateAsync(
                It.IsAny<string>(),
                It.IsAny<Func<CancellationToken, Task<bool>>>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<string[]?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(shouldCache);

        var service = new StaticDataService(
            db,
            Mock.Of<IHttpClientFactory>(),
            cache.Object,
            Options.Create(new PatchPromotionOptions()),
            NullLogger<StaticDataService>.Instance,
            Options.Create(new WorkerJobScheduleOptions()),
            Mock.Of<IBackgroundJobClient>(MockBehavior.Strict));

        await service.EnsureStaticDataForPatchAsync("16.14");

        cache.Verify(
            value => value.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Exactly(expectedRemoveCalls));
    }

    private sealed class JsonResponseHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });
    }
}
