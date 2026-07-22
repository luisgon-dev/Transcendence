using FluentAssertions;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Account;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Core.Services.Jobs;
using Transcendence.Service.Core.Services.Jobs.Configuration;
using Transcendence.Service.Core.Services.Jobs.Interfaces;
using Transcendence.Service.Core.Services.RiotApi;
using Transcendence.Service.Core.Services.RiotApi.Implementations;
using Transcendence.Service.Core.Services.RiotApi.Interfaces;
using Transcendence.Service.Core.Services.StaticData.Interfaces;
using Transcendence.Service.Core.Tests.Support;

// Match collides with Moq.Match; the domain entity is what we mean throughout this file.
using DataMatch = Transcendence.Data.Models.LoL.Match.Match;

namespace Transcendence.Service.Core.Tests;

// Regression coverage for the P0 finding: transient Riot rate-gate backpressure must NOT be counted
// as a fetch failure (it previously accumulated RetryCount and flipped fetchable matches to the
// terminal, globally-filtered PermanentlyUnfetchable status), and matches wrongly flipped by that bug
// must be revivable without disturbing genuine 404/gone rows.
public sealed class RateGateBackpressureTests
{
    private static async Task<TranscendenceContext> CreateContextAsync()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TranscendenceContext>()
            .UseSqlite(connection)
            .Options;
        var context = new SqliteCompatibleTranscendenceContext(options);
        await context.Database.EnsureCreatedAsync();
        return context;
    }

    [Fact]
    public async Task MatchIdClient_WhenRateGateExhausted_ReturnsDeferredSentinelInsteadOfEmptyPage()
    {
        var rateGate = new Mock<IRiotRateGate>();
        rateGate
            .Setup(g => g.AcquireAsync("AMERICAS", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var client = new RiotMatchIdsClient(null!, rateGate.Object);

        var result = await client.GetMatchIdsByPuuidAsync(
            Camille.Enums.RegionalRoute.AMERICAS,
            "puuid",
            100,
            null,
            null,
            null,
            0,
            null);

        result.Should().BeNull("null is the explicit retry-later outcome; an empty page means end-of-history");
    }

    [Fact]
    public async Task FullHistoryBackfill_WhenMatchIdPageIsDeferred_RemainsRunningAndEnqueuesContinuation()
    {
        await using var context = await CreateContextAsync();
        var summoner = new Summoner
        {
            Id = Guid.NewGuid(),
            Puuid = "puuid-deferred",
            GameName = "Deferred",
            TagLine = "NA1",
            PlatformRegion = "NA1",
            Region = "AMERICAS"
        };
        context.Summoners.Add(summoner);
        await context.SaveChangesAsync();

        var matchIds = new Mock<IRiotMatchIdsClient>();
        matchIds
            .Setup(client => client.GetMatchIdsByPuuidAsync(
                It.IsAny<Camille.Enums.RegionalRoute>(),
                summoner.Puuid,
                It.IsAny<int>(),
                It.IsAny<long?>(),
                It.IsAny<Camille.Enums.Queue?>(),
                It.IsAny<long?>(),
                It.IsAny<int>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<string>?)null);

        var locks = new Mock<IRefreshLockRepository>();
        locks
            .Setup(repository => repository.TryAcquireAsync(
                It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        locks
            .Setup(repository => repository.ReleaseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var backgroundJobs = new Mock<IBackgroundJobClient>();
        backgroundJobs
            .Setup(client => client.Create(It.IsAny<Job>(), It.IsAny<IState>()))
            .Returns("continuation-job");

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();
        serviceCollection.AddHybridCache();
        await using var services = serviceCollection.BuildServiceProvider();
        var job = new FullHistoryBackfillJob(
            context,
            null!,
            Mock.Of<IRiotRateGate>(),
            matchIds.Object,
            backgroundJobs.Object,
            services.GetRequiredService<HybridCache>(),
            Options.Create(new FullHistoryBackfillJobOptions
            {
                Enabled = true,
                PageSize = 100,
                MaxPagesPerRun = 1,
                MaxFailureRetriesPerRun = 0,
                MinimumMatchStartEpochSeconds = 0
            }),
            locks.Object,
            NullLogger<FullHistoryBackfillJob>.Instance);

        await job.ProcessAsync(summoner.Id, null, CancellationToken.None);

        var backfill = await context.SummonerFullHistoryBackfills.SingleAsync();
        backfill.Status.Should().Be(SummonerFullHistoryBackfillStatuses.Running);
        backfill.CompletedAtUtc.Should().BeNull();
        backfill.PagesScanned.Should().Be(0);
        backgroundJobs.Verify(
            client => client.Create(It.IsAny<Job>(), It.IsAny<IState>()),
            Times.Once,
            "a deferred page must be retried instead of terminating the backfill");
    }

    [Fact]
    public async Task FetchMatchWithRetryAsync_WhenRateGateExhausted_DefersAsTemporaryWithoutIncrementingRetryCount()
    {
        await using var context = await CreateContextAsync();

        var rateGate = new Mock<IRiotRateGate>();
        rateGate
            .Setup(g => g.AcquireAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false); // budget exhausted → must be a no-op deferral, not a failure

        var matchRepository = new Mock<IMatchRepository>();
        matchRepository
            .Setup(r => r.GetMatchByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DataMatch?)null); // brand-new match id

        // The gate check short-circuits before any Riot call, so the Riot client and the other
        // collaborators are never dereferenced on this path.
        var service = new MatchService(
            riotApiContext: null!,
            context: context,
            matchRepository: matchRepository.Object,
            summonerService: Mock.Of<ISummonerService>(),
            summonerRepository: Mock.Of<ISummonerRepository>(),
            staticDataService: Mock.Of<IStaticDataService>(),
            rateGate: rateGate.Object,
            fetchOptions: Options.Create(new MatchFetchOptions()),
            logger: NullLogger<MatchService>.Instance);

        var result = await service.FetchMatchWithRetryAsync("NA1_1000000001", "AMERICAS", CancellationToken.None);

        result.Should().BeFalse();

        var saved = await context.Matches.SingleAsync(m => m.MatchId == "NA1_1000000001");
        saved.Status.Should().Be(FetchStatus.TemporaryFailure);
        saved.RetryCount.Should().Be(0, "rate-gate backpressure is transient and must never count as a failed attempt");
        saved.Status.Should().NotBe(FetchStatus.PermanentlyUnfetchable);
    }

    [Fact]
    public async Task FetchMatchWithRetryAsync_UsesConfiguredRetentionWindow()
    {
        await using var context = await CreateContextAsync();
        var match = new DataMatch
        {
            Id = Guid.NewGuid(),
            MatchId = "NA1_OLD",
            MatchDate = DateTimeOffset.UtcNow.AddDays(-2).ToUnixTimeMilliseconds(),
            Status = FetchStatus.Unfetched
        };
        context.Matches.Add(match);
        await context.SaveChangesAsync();

        var repository = new Mock<IMatchRepository>();
        repository
            .Setup(r => r.GetMatchByIdAsync(match.MatchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(match);
        var gate = new Mock<IRiotRateGate>(MockBehavior.Strict);
        var service = new MatchService(
            null!,
            context,
            repository.Object,
            Mock.Of<ISummonerService>(),
            Mock.Of<ISummonerRepository>(),
            Mock.Of<IStaticDataService>(),
            gate.Object,
            Options.Create(new MatchFetchOptions { RetentionDays = 1, MaxRetryAttempts = 5 }),
            NullLogger<MatchService>.Instance);

        var result = await service.FetchMatchWithRetryAsync(match.MatchId, "AMERICAS");

        result.Should().BeFalse();
        match.Status.Should().Be(FetchStatus.OutsideRetentionWindow);
        match.LastErrorMessage.Should().Contain("1 days");
        gate.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task FetchMatchWithRetryAsync_UsesConfiguredTerminalRetryCount()
    {
        await using var context = await CreateContextAsync();
        var match = new DataMatch
        {
            Id = Guid.NewGuid(),
            MatchId = "NA1_RETRY",
            Status = FetchStatus.TemporaryFailure,
            RetryCount = 1
        };
        context.Matches.Add(match);
        await context.SaveChangesAsync();

        var repository = new Mock<IMatchRepository>();
        repository
            .Setup(r => r.GetMatchByIdAsync(match.MatchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(match);
        var gate = new Mock<IRiotRateGate>();
        gate
            .Setup(g => g.AcquireAsync("AMERICAS", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var service = new MatchService(
            null!,
            context,
            repository.Object,
            Mock.Of<ISummonerService>(),
            Mock.Of<ISummonerRepository>(),
            Mock.Of<IStaticDataService>(),
            gate.Object,
            Options.Create(new MatchFetchOptions { RetentionDays = 730, MaxRetryAttempts = 2 }),
            NullLogger<MatchService>.Instance);

        var result = await service.FetchMatchWithRetryAsync(match.MatchId, "AMERICAS");

        result.Should().BeFalse();
        match.RetryCount.Should().Be(2);
        match.Status.Should().Be(FetchStatus.PermanentlyUnfetchable);
    }

    [Fact]
    public async Task Execute_RevivesRateGateMisclassifiedMatches_ButLeavesGenuine404sTerminal()
    {
        await using var context = await CreateContextAsync();

        var old = DateTime.UtcNow.AddDays(-1);
        var rateGateVictim = new DataMatch
        {
            MatchId = "NA1_1",
            Status = FetchStatus.PermanentlyUnfetchable,
            RetryCount = 5,
            LastAttemptAt = old,
            LastErrorMessage = "Riot rate gate exhausted for match NA1_1 (AMERICAS); retry later."
        };
        var genuinelyGone = new DataMatch
        {
            MatchId = "NA1_2",
            Status = FetchStatus.PermanentlyUnfetchable,
            RetryCount = 5,
            LastAttemptAt = old,
            LastErrorMessage = "Riot API returned null (match not found / gone)."
        };
        context.Matches.AddRange(rateGateVictim, genuinelyGone);
        await context.SaveChangesAsync();

        var matchService = new Mock<IMatchService>();
        matchService
            .Setup(s => s.FetchMatchWithRetryAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true); // no-op; we only assert the persisted status transitions

        var job = new RetryFailedMatchesJob(
            context,
            matchService.Object,
            Options.Create(new RetryFailedMatchesJobOptions { RevivePermanentlyUnfetchablePerRun = 25 }),
            Options.Create(new ChampionAnalyticsIngestionJobOptions { PauseWhenApiPriorityRefreshActive = false }),
            Mock.Of<IRefreshLockRepository>(),
            NullLogger<RetryFailedMatchesJob>.Instance);

        await job.Execute(CancellationToken.None);

        var victim = await context.Matches.IgnoreQueryFilters().SingleAsync(m => m.MatchId == "NA1_1");
        victim.Status.Should().Be(FetchStatus.TemporaryFailure, "it was wrongly killed by rate pressure and should be revived");
        victim.RetryCount.Should().Be(0);

        var gone = await context.Matches.IgnoreQueryFilters().SingleAsync(m => m.MatchId == "NA1_2");
        gone.Status.Should().Be(FetchStatus.PermanentlyUnfetchable, "a genuine 404 must stay terminal");
    }
}
