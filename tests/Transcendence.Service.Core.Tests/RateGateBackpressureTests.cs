using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Core.Services.Jobs;
using Transcendence.Service.Core.Services.Jobs.Configuration;
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
            logger: NullLogger<MatchService>.Instance);

        var result = await service.FetchMatchWithRetryAsync("NA1_1000000001", "AMERICAS", CancellationToken.None);

        result.Should().BeFalse();

        var saved = await context.Matches.SingleAsync(m => m.MatchId == "NA1_1000000001");
        saved.Status.Should().Be(FetchStatus.TemporaryFailure);
        saved.RetryCount.Should().Be(0, "rate-gate backpressure is transient and must never count as a failed attempt");
        saved.Status.Should().NotBe(FetchStatus.PermanentlyUnfetchable);
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
