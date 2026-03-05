using Camille.Enums;
using FluentAssertions;
using Hangfire;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Account;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Data.Models.LoL.Static;
using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Core.Services.Diagnostics;
using Transcendence.Service.Core.Services.Jobs;
using Transcendence.Service.Core.Services.Jobs.Configuration;
using Transcendence.Service.Core.Services.Jobs.Interfaces;
using Transcendence.Service.Core.Services.RiotApi;
using Transcendence.Service.Core.Services.RiotApi.Interfaces;
using MatchEntity = Transcendence.Data.Models.LoL.Match.Match;

namespace Transcendence.Service.Core.Tests;

public class SummonerRefreshJobTests
{
    [Fact]
    public async Task RefreshByRiotId_ReleasesMainAndPriorityLocks_WhenSummonerLookupFails()
    {
        await using var harness = await SummonerRefreshJobHarness.CreateAsync();

        harness.SummonerService
            .Setup(x => x.GetSummonerByRiotIdAsync("name", "tag", PlatformRoute.NA1, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("lookup failed"));

        Func<Task> act = async () =>
            await harness.Job.RefreshByRiotId("name", "tag", PlatformRoute.NA1, "lock:main", "lock:priority");

        await act.Should().ThrowAsync<InvalidOperationException>();
        harness.RefreshLockRepository.Verify(x => x.ReleaseAsync("lock:main", It.IsAny<CancellationToken>()), Times.Once);
        harness.RefreshLockRepository.Verify(x => x.ReleaseAsync("lock:priority", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RefreshByRiotId_ReleasesOnlyMainLock_WhenPriorityLockIsNull()
    {
        await using var harness = await SummonerRefreshJobHarness.CreateAsync();

        harness.SummonerService
            .Setup(x => x.GetSummonerByRiotIdAsync("name", "tag", PlatformRoute.NA1, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("lookup failed"));

        Func<Task> act = async () =>
            await harness.Job.RefreshByRiotId("name", "tag", PlatformRoute.NA1, "lock:main", null);

        await act.Should().ThrowAsync<InvalidOperationException>();
        harness.RefreshLockRepository.Verify(x => x.ReleaseAsync("lock:main", It.IsAny<CancellationToken>()), Times.Once);
        harness.RefreshLockRepository.Verify(
            x => x.ReleaseAsync(It.Is<string>(k => k != "lock:main"), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RefreshByRiotId_WhenTelemetryThrows_DoesNotBlockRefreshFlow()
    {
        await using var harness = await SummonerRefreshJobHarness.CreateAsync();

        harness.LockTelemetry
            .Setup(x => x.RecordLifecycleOutcome(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Throws(new InvalidOperationException("telemetry unavailable"));

        harness.SummonerService
            .Setup(x => x.GetSummonerByRiotIdAsync("name", "tag", PlatformRoute.NA1, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("lookup failed"));

        Func<Task> act = async () =>
            await harness.Job.RefreshByRiotId("name", "tag", PlatformRoute.NA1, "lock:main", "lock:priority");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("lookup failed");
        harness.RefreshLockRepository.Verify(x => x.ReleaseAsync("lock:main", It.IsAny<CancellationToken>()), Times.Once);
        harness.RefreshLockRepository.Verify(x => x.ReleaseAsync("lock:priority", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RefreshByRiotId_PropagatesCancellationAndStillReleasesLocks()
    {
        await using var harness = await SummonerRefreshJobHarness.CreateAsync();
        var summoner = harness.SeedSummoner("name", "tag", "puuid-cancel");
        await harness.Db.SaveChangesAsync();

        using var cts = new CancellationTokenSource();

        harness.SummonerService
            .Setup(x => x.GetSummonerByRiotIdAsync("name", "tag", PlatformRoute.NA1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(summoner);

        harness.SummonerRepository
            .Setup(x => x.AddOrUpdateSummonerAsync(summoner, It.IsAny<CancellationToken>()))
            .ReturnsAsync(summoner);

        harness.RiotMatchIdsClient
            .Setup(x => x.GetMatchIdsByPuuidAsync(
                It.IsAny<RegionalRoute>(),
                "puuid-cancel",
                It.IsAny<int>(),
                It.IsAny<long?>(),
                It.IsAny<Queue?>(),
                It.IsAny<long?>(),
                It.IsAny<int>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(["NA1_cancel_1"]);

        harness.MatchService
            .Setup(x => x.GetMatchDetailsAsync(
                "NA1_cancel_1",
                It.IsAny<RegionalRoute>(),
                PlatformRoute.NA1,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        Func<Task> act = async () => await harness.Job.RefreshByRiotId(
            "name",
            "tag",
            PlatformRoute.NA1,
            "lock:main",
            "lock:priority",
            cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        harness.RefreshLockRepository.Verify(x => x.ReleaseAsync("lock:main", It.IsAny<CancellationToken>()), Times.Once);
        harness.RefreshLockRepository.Verify(x => x.ReleaseAsync("lock:priority", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RefreshForAnalytics_ExitsEarly_WhenApiPriorityDemandIsActive()
    {
        await using var harness = await SummonerRefreshJobHarness.CreateAsync();

        harness.RefreshLockRepository
            .Setup(x => x.AnyActiveByPrefixAsync(RefreshLockKeys.ApiPriorityRefreshPrefix, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await harness.Job.RefreshForAnalytics(
            "name",
            "tag",
            PlatformRoute.NA1,
            "lock:low",
            startTimeEpochSeconds: 0,
            currentPatch: "14.2",
            includeAllModes: true);

        harness.SummonerRepository.Verify(
            x => x.FindByRiotIdAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Func<IQueryable<Summoner>, IQueryable<Summoner>>?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        harness.RiotMatchIdsClient.Verify(
            x => x.GetMatchIdsByPuuidAsync(
                It.IsAny<RegionalRoute>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<long?>(),
                It.IsAny<Queue?>(),
                It.IsAny<long?>(),
                It.IsAny<int>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        harness.RefreshLockRepository.Verify(x => x.ReleaseAsync("lock:low", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RefreshForAnalytics_Skips_WhenSummonerIsMissing()
    {
        await using var harness = await SummonerRefreshJobHarness.CreateAsync();
        harness.RefreshLockRepository
            .Setup(x => x.AnyActiveByPrefixAsync(RefreshLockKeys.ApiPriorityRefreshPrefix, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        harness.SummonerRepository
            .Setup(x => x.FindByRiotIdAsync(
                "NA1",
                "name",
                "tag",
                It.IsAny<Func<IQueryable<Summoner>, IQueryable<Summoner>>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Summoner?)null);

        await harness.Job.RefreshForAnalytics(
            "name",
            "tag",
            PlatformRoute.NA1,
            "lock:low",
            startTimeEpochSeconds: 0,
            currentPatch: "14.2",
            includeAllModes: true);

        harness.RiotMatchIdsClient.Verify(
            x => x.GetMatchIdsByPuuidAsync(
                It.IsAny<RegionalRoute>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<long?>(),
                It.IsAny<Queue?>(),
                It.IsAny<long?>(),
                It.IsAny<int>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        harness.RefreshLockRepository.Verify(x => x.ReleaseAsync("lock:low", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RefreshForAnalytics_IncludeAllModesFalse_OnlyFetchesRankedHead()
    {
        await using var harness = await SummonerRefreshJobHarness.CreateAsync();
        var summoner = harness.SeedSummoner("name", "tag", "puuid-ranked-only");
        await harness.Db.SaveChangesAsync();
        var previousUpdatedAt = summoner.UpdatedAt;

        harness.RefreshLockRepository
            .Setup(x => x.AnyActiveByPrefixAsync(RefreshLockKeys.ApiPriorityRefreshPrefix, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        harness.SummonerRepository
            .Setup(x => x.FindByRiotIdAsync(
                "NA1",
                "name",
                "tag",
                It.IsAny<Func<IQueryable<Summoner>, IQueryable<Summoner>>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(summoner);

        harness.RiotMatchIdsClient
            .Setup(x => x.GetMatchIdsByPuuidAsync(
                It.IsAny<RegionalRoute>(),
                "puuid-ranked-only",
                It.IsAny<int>(),
                It.IsAny<long?>(),
                It.IsAny<Queue?>(),
                It.IsAny<long?>(),
                It.IsAny<int>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(["NA1_ranked_1"]);

        harness.MatchService
            .Setup(x => x.GetMatchDetailsLightweightAsync(
                "NA1_ranked_1",
                It.IsAny<RegionalRoute>(),
                PlatformRoute.NA1,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildMatch("NA1_ranked_1", QueueCatalog.RankedSoloDuoQueueId));

        await harness.Job.RefreshForAnalytics(
            "name",
            "tag",
            PlatformRoute.NA1,
            "lock:low",
            startTimeEpochSeconds: 0,
            currentPatch: "14.2",
            includeAllModes: false);

        harness.RiotMatchIdsClient.Verify(
            x => x.GetMatchIdsByPuuidAsync(
                It.IsAny<RegionalRoute>(),
                "puuid-ranked-only",
                It.IsAny<int>(),
                It.IsAny<long?>(),
                Queue.SUMMONERS_RIFT_5V5_RANKED_SOLO,
                0,
                0,
                "ranked",
                It.IsAny<CancellationToken>()),
            Times.Once);

        harness.RiotMatchIdsClient.Verify(
            x => x.GetMatchIdsByPuuidAsync(
                It.IsAny<RegionalRoute>(),
                "puuid-ranked-only",
                It.IsAny<int>(),
                It.IsAny<long?>(),
                It.Is<Queue?>(q => q == null),
                It.IsAny<long?>(),
                It.IsAny<int>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        harness.Db.Matches.Should().ContainSingle(m => m.MatchId == "NA1_ranked_1");
        summoner.UpdatedAt.Should().BeAfter(previousUpdatedAt);
        harness.RefreshLockRepository.Verify(x => x.ReleaseAsync("lock:low", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RefreshForAnalytics_PreemptsAllModes_WhenPriorityDemandAppearsAfterRankedHead()
    {
        await using var harness = await SummonerRefreshJobHarness.CreateAsync();
        var summoner = harness.SeedSummoner("name", "tag", "puuid-preempt");
        await harness.Db.SaveChangesAsync();

        harness.RefreshLockRepository
            .SetupSequence(
                x => x.AnyActiveByPrefixAsync(RefreshLockKeys.ApiPriorityRefreshPrefix, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false) // initial guard
            .ReturnsAsync(false) // shouldStop at ranked page start
            .ReturnsAsync(false) // shouldStop before ranked match fetch
            .ReturnsAsync(true); // includeAllModes gate

        harness.SummonerRepository
            .Setup(x => x.FindByRiotIdAsync(
                "NA1",
                "name",
                "tag",
                It.IsAny<Func<IQueryable<Summoner>, IQueryable<Summoner>>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(summoner);

        harness.RiotMatchIdsClient
            .Setup(x => x.GetMatchIdsByPuuidAsync(
                It.IsAny<RegionalRoute>(),
                "puuid-preempt",
                It.IsAny<int>(),
                It.IsAny<long?>(),
                It.IsAny<Queue?>(),
                It.IsAny<long?>(),
                It.IsAny<int>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(["NA1_ranked_1"]);

        harness.MatchService
            .Setup(x => x.GetMatchDetailsLightweightAsync(
                "NA1_ranked_1",
                It.IsAny<RegionalRoute>(),
                PlatformRoute.NA1,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildMatch("NA1_ranked_1", QueueCatalog.RankedSoloDuoQueueId));

        await harness.Job.RefreshForAnalytics(
            "name",
            "tag",
            PlatformRoute.NA1,
            "lock:low",
            startTimeEpochSeconds: 0,
            currentPatch: "14.2",
            includeAllModes: true);

        harness.RiotMatchIdsClient.Verify(
            x => x.GetMatchIdsByPuuidAsync(
                It.IsAny<RegionalRoute>(),
                "puuid-preempt",
                It.IsAny<int>(),
                It.IsAny<long?>(),
                Queue.SUMMONERS_RIFT_5V5_RANKED_SOLO,
                It.IsAny<long?>(),
                It.IsAny<int>(),
                "ranked",
                It.IsAny<CancellationToken>()),
            Times.Once);
        harness.RiotMatchIdsClient.Verify(
            x => x.GetMatchIdsByPuuidAsync(
                It.IsAny<RegionalRoute>(),
                "puuid-preempt",
                It.IsAny<int>(),
                It.IsAny<long?>(),
                It.Is<Queue?>(q => q == null),
                It.IsAny<long?>(),
                It.IsAny<int>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RefreshByRiotId_FetchesRankedAndAllModesThenBackfill_AndReleasesLocks()
    {
        await using var harness = await SummonerRefreshJobHarness.CreateAsync();
        var summoner = harness.SeedSummoner("name", "tag", "puuid-refresh");
        await harness.Db.SaveChangesAsync();

        harness.SummonerService
            .Setup(x => x.GetSummonerByRiotIdAsync("name", "tag", PlatformRoute.NA1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(summoner);

        harness.SummonerRepository
            .Setup(x => x.AddOrUpdateSummonerAsync(summoner, It.IsAny<CancellationToken>()))
            .ReturnsAsync(summoner);

        var ids = new System.Collections.Generic.Queue<IReadOnlyList<string>>(
        [
            ["NA1_ranked_1"],
            ["NA1_aram_1"],
            []
        ]);

        harness.RiotMatchIdsClient
            .Setup(x => x.GetMatchIdsByPuuidAsync(
                It.IsAny<RegionalRoute>(),
                "puuid-refresh",
                It.IsAny<int>(),
                It.IsAny<long?>(),
                It.IsAny<Queue?>(),
                It.IsAny<long?>(),
                It.IsAny<int>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => ids.Count > 0 ? ids.Dequeue() : []);

        harness.MatchService
            .Setup(x => x.GetMatchDetailsAsync(
                "NA1_ranked_1",
                It.IsAny<RegionalRoute>(),
                PlatformRoute.NA1,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildMatch("NA1_ranked_1", QueueCatalog.RankedSoloDuoQueueId));

        harness.MatchService
            .Setup(x => x.GetMatchDetailsAsync(
                "NA1_aram_1",
                It.IsAny<RegionalRoute>(),
                PlatformRoute.NA1,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildMatch("NA1_aram_1", 450));

        await harness.Job.RefreshByRiotId("name", "tag", PlatformRoute.NA1, "lock:main", "lock:priority");

        harness.RiotMatchIdsClient.Verify(
            x => x.GetMatchIdsByPuuidAsync(
                It.IsAny<RegionalRoute>(),
                "puuid-refresh",
                It.IsAny<int>(),
                It.IsAny<long?>(),
                Queue.SUMMONERS_RIFT_5V5_RANKED_SOLO,
                It.IsAny<long?>(),
                It.IsAny<int>(),
                "ranked",
                It.IsAny<CancellationToken>()),
            Times.Once);

        harness.RiotMatchIdsClient.Verify(
            x => x.GetMatchIdsByPuuidAsync(
                It.IsAny<RegionalRoute>(),
                "puuid-refresh",
                It.IsAny<int>(),
                It.IsAny<long?>(),
                It.Is<Queue?>(q => q == null),
                It.IsAny<long?>(),
                It.IsAny<int>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));

        harness.Db.Matches.Should().Contain(m => m.MatchId == "NA1_ranked_1");
        harness.Db.Matches.Should().Contain(m => m.MatchId == "NA1_aram_1");
        harness.RefreshLockRepository.Verify(x => x.ReleaseAsync("lock:main", It.IsAny<CancellationToken>()), Times.Once);
        harness.RefreshLockRepository.Verify(x => x.ReleaseAsync("lock:priority", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RefreshByRiotId_WhenApiPriorityDemandIsActive_StillFetchesAllModesForManualRefresh()
    {
        await using var harness = await SummonerRefreshJobHarness.CreateAsync();
        var summoner = harness.SeedSummoner("name", "tag", "puuid-manual-all-modes");
        await harness.Db.SaveChangesAsync();

        harness.RefreshLockRepository
            .Setup(x => x.AnyActiveByPrefixAsync(RefreshLockKeys.ApiPriorityRefreshPrefix, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        harness.SummonerService
            .Setup(x => x.GetSummonerByRiotIdAsync("name", "tag", PlatformRoute.NA1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(summoner);

        harness.SummonerRepository
            .Setup(x => x.AddOrUpdateSummonerAsync(summoner, It.IsAny<CancellationToken>()))
            .ReturnsAsync(summoner);

        var ids = new System.Collections.Generic.Queue<IReadOnlyList<string>>(
        [
            ["NA1_ranked_manual"],
            ["NA1_aram_manual"],
            []
        ]);

        harness.RiotMatchIdsClient
            .Setup(x => x.GetMatchIdsByPuuidAsync(
                It.IsAny<RegionalRoute>(),
                "puuid-manual-all-modes",
                It.IsAny<int>(),
                It.IsAny<long?>(),
                It.IsAny<Queue?>(),
                It.IsAny<long?>(),
                It.IsAny<int>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => ids.Count > 0 ? ids.Dequeue() : []);

        harness.MatchService
            .Setup(x => x.GetMatchDetailsAsync(
                "NA1_ranked_manual",
                It.IsAny<RegionalRoute>(),
                PlatformRoute.NA1,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildMatch("NA1_ranked_manual", QueueCatalog.RankedSoloDuoQueueId));
        harness.MatchService
            .Setup(x => x.GetMatchDetailsAsync(
                "NA1_aram_manual",
                It.IsAny<RegionalRoute>(),
                PlatformRoute.NA1,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildMatch("NA1_aram_manual", 450));

        await harness.Job.RefreshByRiotId("name", "tag", PlatformRoute.NA1, "lock:main", "lock:priority");

        harness.RiotMatchIdsClient.Verify(
            x => x.GetMatchIdsByPuuidAsync(
                It.IsAny<RegionalRoute>(),
                "puuid-manual-all-modes",
                It.IsAny<int>(),
                It.IsAny<long?>(),
                Queue.SUMMONERS_RIFT_5V5_RANKED_SOLO,
                It.IsAny<long?>(),
                It.IsAny<int>(),
                "ranked",
                It.IsAny<CancellationToken>()),
            Times.Once);
        harness.RiotMatchIdsClient.Verify(
            x => x.GetMatchIdsByPuuidAsync(
                It.IsAny<RegionalRoute>(),
                "puuid-manual-all-modes",
                It.IsAny<int>(),
                It.IsAny<long?>(),
                It.Is<Queue?>(q => q == null),
                It.IsAny<long?>(),
                It.IsAny<int>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));

        harness.Db.Matches.Should().Contain(m => m.MatchId == "NA1_ranked_manual");
        harness.Db.Matches.Should().Contain(m => m.MatchId == "NA1_aram_manual");
    }

    private static MatchEntity BuildMatch(string matchId, int queueId)
    {
        return new MatchEntity
        {
            Id = Guid.NewGuid(),
            MatchId = matchId,
            MatchDate = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Duration = 1800,
            Patch = "14.2",
            QueueId = queueId,
            QueueFamily = QueueCatalog.ResolveQueueFamily(queueId),
            QueueType = queueId == QueueCatalog.RankedSoloDuoQueueId ? "RANKED_SOLO_5x5" : QueueCatalog.ResolveQueueLabel(queueId),
            Status = FetchStatus.Success
        };
    }

    private sealed class SummonerRefreshJobHarness : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly ServiceProvider _services;

        private SummonerRefreshJobHarness(
            SqliteConnection connection,
            TestSqliteTranscendenceContext db,
            ServiceProvider services,
            SummonerRefreshJob job,
            Mock<ISummonerService> summonerService,
            Mock<ISummonerRepository> summonerRepository,
            Mock<IMatchRepository> matchRepository,
            Mock<IMatchService> matchService,
            Mock<IRefreshLockRepository> refreshLockRepository,
            Mock<IRiotMatchIdsClient> riotMatchIdsClient,
            Mock<IBackgroundJobClient> backgroundJobClient,
            Mock<IRefreshLockLifecycleTelemetry> lockTelemetry)
        {
            _connection = connection;
            Db = db;
            _services = services;
            Job = job;
            SummonerService = summonerService;
            SummonerRepository = summonerRepository;
            MatchRepository = matchRepository;
            MatchService = matchService;
            RefreshLockRepository = refreshLockRepository;
            RiotMatchIdsClient = riotMatchIdsClient;
            BackgroundJobClient = backgroundJobClient;
            LockTelemetry = lockTelemetry;
        }

        public TestSqliteTranscendenceContext Db { get; }
        public SummonerRefreshJob Job { get; }

        public Mock<ISummonerService> SummonerService { get; }
        public Mock<ISummonerRepository> SummonerRepository { get; }
        public Mock<IMatchRepository> MatchRepository { get; }
        public Mock<IMatchService> MatchService { get; }
        public Mock<IRefreshLockRepository> RefreshLockRepository { get; }
        public Mock<IRiotMatchIdsClient> RiotMatchIdsClient { get; }
        public Mock<IBackgroundJobClient> BackgroundJobClient { get; }
        public Mock<IRefreshLockLifecycleTelemetry> LockTelemetry { get; }

        public static async Task<SummonerRefreshJobHarness> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<TranscendenceContext>()
                .UseSqlite(connection)
                .Options;

            var db = new TestSqliteTranscendenceContext(options);
            await db.Database.EnsureCreatedAsync();

            var serviceCollection = new ServiceCollection();
            serviceCollection.AddLogging();
            serviceCollection.AddHybridCache();
            var services = serviceCollection.BuildServiceProvider();

            var summonerService = new Mock<ISummonerService>();
            var summonerRepository = new Mock<ISummonerRepository>();
            var matchRepository = new Mock<IMatchRepository>();
            var matchService = new Mock<IMatchService>();
            var refreshLockRepository = new Mock<IRefreshLockRepository>();
            var riotMatchIdsClient = new Mock<IRiotMatchIdsClient>();
            var backgroundJobClient = new Mock<IBackgroundJobClient>();
            var lockTelemetry = new Mock<IRefreshLockLifecycleTelemetry>();

            refreshLockRepository
                .Setup(x => x.ReleaseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            refreshLockRepository
                .Setup(x => x.AnyActiveByPrefixAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);

            matchRepository
                .Setup(x => x.GetExistingMatchIdsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((IEnumerable<string> ids, CancellationToken _) =>
                    ids
                        .Where(id => db.Matches.AsNoTracking().Any(m => m.MatchId == id))
                        .ToHashSet(StringComparer.Ordinal));

            matchRepository
                .Setup(x => x.AddMatchAsync(It.IsAny<MatchEntity>(), It.IsAny<CancellationToken>()))
                .Returns<MatchEntity, CancellationToken>((match, _) =>
                {
                    db.Matches.Add(match);
                    return Task.CompletedTask;
                });

            riotMatchIdsClient
                .Setup(x => x.GetMatchIdsByPuuidAsync(
                    It.IsAny<RegionalRoute>(),
                    It.IsAny<string>(),
                    It.IsAny<int>(),
                    It.IsAny<long?>(),
                    It.IsAny<Queue?>(),
                    It.IsAny<long?>(),
                    It.IsAny<int>(),
                    It.IsAny<string?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);

            var ingestionOptions = Options.Create(new MatchIngestionOptions
            {
                MatchIdsPageSize = 20,
                HighPriorityRankedPages = 1,
                HighPriorityAllModesHeadPages = 1,
                HighPriorityNonRankedBackfillMaxPages = 1,
                LowPriorityRankedPages = 1,
                LowPriorityAllModesHeadPages = 1,
                LowPriorityNonRankedBackfillMaxPages = 1
            });
            var timelineOptions = Options.Create(new TimelineIngestionOptions { Enabled = false });

            var job = new SummonerRefreshJob(
                summonerService.Object,
                summonerRepository.Object,
                matchRepository.Object,
                matchService.Object,
                db,
                refreshLockRepository.Object,
                services.GetRequiredService<ILogger<SummonerRefreshJob>>(),
                riotMatchIdsClient.Object,
                backgroundJobClient.Object,
                services.GetRequiredService<HybridCache>(),
                ingestionOptions,
                timelineOptions,
                lockTelemetry.Object);

            return new SummonerRefreshJobHarness(
                connection,
                db,
                services,
                job,
                summonerService,
                summonerRepository,
                matchRepository,
                matchService,
                refreshLockRepository,
                riotMatchIdsClient,
                backgroundJobClient,
                lockTelemetry);
        }

        public Summoner SeedSummoner(string gameName, string tagLine, string puuid)
        {
            var summoner = new Summoner
            {
                Id = Guid.NewGuid(),
                PlatformRegion = "NA1",
                Region = "AMERICAS",
                GameName = gameName,
                TagLine = tagLine,
                GameNameNormalized = gameName.ToUpperInvariant(),
                TagLineNormalized = tagLine.ToUpperInvariant(),
                Puuid = puuid,
                SummonerLevel = 200,
                UpdatedAt = DateTime.UtcNow.AddHours(-1)
            };
            Db.Summoners.Add(summoner);
            return summoner;
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _services.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

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
