using Camille.Enums;
using FluentAssertions;
using Hangfire;
using Hangfire.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Static;
using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Core.Services.Diagnostics;
using Transcendence.Service.Core.Services.Jobs;
using Transcendence.Service.Core.Services.Jobs.Configuration;
using Transcendence.Service.Core.Services.Jobs.Interfaces;
using Transcendence.Service.Core.Services.Jobs.Priority;
using Transcendence.Service.Core.Services.RiotApi;
using Transcendence.Service.Core.Services.RiotApi.Interfaces;
using Transcendence.Service.Core.Tests.Support;

namespace Transcendence.Service.Core.Tests;

public class CancellationPropagationTests
{
    [Fact]
    public async Task RefreshByRiotId_WithPreCanceledToken_PropagatesCancellationAndStillAttemptsLockRelease()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<TranscendenceContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new TranscendenceContext(options);

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();
        serviceCollection.AddHybridCache();
        var services = serviceCollection.BuildServiceProvider();

        var refreshLockRepository = new Mock<IRefreshLockRepository>();
        refreshLockRepository
            .Setup(x => x.ReleaseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var job = new SummonerRefreshJob(
            Mock.Of<ISummonerService>(),
            Mock.Of<ISummonerRepository>(),
            Mock.Of<IMatchRepository>(),
            Mock.Of<IMatchService>(),
            Mock.Of<IChampionMasteryService>(),
            Mock.Of<IChampionMasteryRepository>(),
            db,
            refreshLockRepository.Object,
            services.GetRequiredService<ILogger<SummonerRefreshJob>>(),
            Mock.Of<IRiotMatchIdsClient>(),
            Mock.Of<IBackgroundJobClient>(),
            services.GetRequiredService<HybridCache>(),
            Options.Create(new MatchIngestionOptions()),
            Options.Create(new TimelineIngestionOptions { Enabled = false }));

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = async () => await job.RefreshByRiotId(
            "name",
            "tag",
            PlatformRoute.NA1,
            "lock:main",
            "lock:priority",
            cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        refreshLockRepository.Verify(x => x.ReleaseAsync("lock:main", It.IsAny<CancellationToken>()), Times.Once);
        refreshLockRepository.Verify(x => x.ReleaseAsync("lock:priority", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteForRegionAsync_WithPreCanceledToken_PropagatesCancellationWithoutQueueing()
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
            Version = "15.2",
            IsActive = true,
            ReleaseDate = DateTime.UtcNow,
            DetectedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var refreshLocks = new Mock<IRefreshLockRepository>();
        refreshLocks.Setup(x => x.AnyActiveByPrefixAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, CancellationToken>((_, token) =>
            {
                token.ThrowIfCancellationRequested();
                return Task.FromResult(false);
            });

        var backgroundJobs = new Mock<IBackgroundJobClient>();
        var bootstrap = new Mock<ISummonerBootstrapService>();
        bootstrap.Setup(x => x.EnsureSeededFromChallengerAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var job = new ChampionAnalyticsIngestionJob(
            db,
            bootstrap.Object,
            refreshLocks.Object,
            backgroundJobs.Object,
            new IngestionPriorityScoringPolicy(Options.Create(new IngestionPriorityPolicyOptions())),
            new AdaptiveThroughputBudgetPolicy(Options.Create(new AdaptiveThroughputBudgetOptions())),
            new StarvationGuardrailPolicy(Options.Create(new StarvationGuardrailOptions
            {
                Enabled = true,
                MaxEligibleDeferAgeMinutes = 50_000
            })),
            Mock.Of<IIngestionThroughputTelemetry>(),
            Mock.Of<IQueueDepthProbe>(),
            Options.Create(new ChampionAnalyticsIngestionJobOptions()),
            Options.Create(new MultiRegionIngestionOptions()),
            Mock.Of<IWorkerHeartbeat>(),
            Mock.Of<ILogger<ChampionAnalyticsIngestionJob>>());

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = async () => await job.ExecuteForRegionAsync("NA1", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        backgroundJobs.Verify(x => x.Create(It.IsAny<Job>(), It.IsAny<Hangfire.States.IState>()), Times.Never);
    }

}
