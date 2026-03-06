using FluentAssertions;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Account;
using Transcendence.Data.Models.Service;
using Transcendence.Data.Models.LoL.Static;
using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Core.Services.Diagnostics;
using Transcendence.Service.Core.Services.Jobs;
using Transcendence.Service.Core.Services.Jobs.Configuration;
using Transcendence.Service.Core.Services.Jobs.Interfaces;
using Transcendence.Service.Core.Services.Jobs.Priority;

namespace Transcendence.Service.Core.Tests;

public class SummonerMaintenanceJobTests
{
    [Fact]
    public async Task ExecuteAsync_WhenMultiRegionEnabled_EnqueuesOneProducerPerEnabledRegion()
    {
        var adaptivePolicy = new FixedAdaptiveBudgetPolicy(new AdaptiveThroughputBudgetDecision(
            AdaptiveThroughputBudgetMode.Balanced,
            MaxCandidates: 4,
            QueueTarget: 1,
            IncludeAllModes: false,
            CoverageRatio: 0.4d,
            BacklogAgeMinutes: 90d,
            RecentVelocityPerHour: 3d,
            CandidatePressureRatio: 1.2d));

        await using var harness = await Harness.CreateAsync(
            adaptivePolicy,
            new MultiRegionIngestionOptions
            {
                Enabled = true,
                Regions =
                [
                    new() { Region = "NA1", Enabled = true },
                    new() { Region = "EUW1", Enabled = true },
                    new() { Region = "KR", Enabled = false },
                    new() { Region = "  na1  ", Enabled = true }
                ]
            });

        var queuedJobs = new List<Job>();
        harness.BackgroundJobClient
            .Setup(x => x.Create(It.IsAny<Job>(), It.IsAny<IState>()))
            .Callback<Job, IState>((job, _) => queuedJobs.Add(job))
            .Returns("job-1");

        await harness.Job.ExecuteAsync(CancellationToken.None);

        queuedJobs.Should().HaveCount(2);
        queuedJobs.Select(job => job.Method.Name).Should().OnlyContain(name => name == nameof(SummonerMaintenanceJob.ExecuteForRegionAsync));
        queuedJobs.Select(job => job.Args[0]).Should().BeEquivalentTo(["NA1", "EUW1"]);
    }

    [Fact]
    public async Task ExecuteRampAsync_UsesPatchFirstScoringOrderBeforeAdaptiveQueueTruncation()
    {
        var adaptivePolicy = new FixedAdaptiveBudgetPolicy(new AdaptiveThroughputBudgetDecision(
            AdaptiveThroughputBudgetMode.Balanced,
            MaxCandidates: 10,
            QueueTarget: 1,
            IncludeAllModes: false,
            CoverageRatio: 0.3d,
            BacklogAgeMinutes: 90d,
            RecentVelocityPerHour: 3d,
            CandidatePressureRatio: 1.5d));

        await using var harness = await Harness.CreateAsync(adaptivePolicy);
        harness.SeedActivePatch("15.2", DateTime.UtcNow.AddHours(-2));
        harness.SeedSummoner("PatchFirst", "NA1", DateTime.UtcNow.AddHours(-6));
        harness.SeedSummoner("NonPatchSecond", "NA1", DateTime.UtcNow.AddMinutes(-40));
        await harness.Db.SaveChangesAsync();

        var queuedJobs = new List<Job>();
        harness.BackgroundJobClient
            .Setup(x => x.Create(It.IsAny<Job>(), It.IsAny<IState>()))
            .Callback<Job, IState>((job, _) => queuedJobs.Add(job))
            .Returns("job-1");

        await harness.Job.ExecuteRampAsync(CancellationToken.None);

        queuedJobs.Should().ContainSingle();
        queuedJobs[0].Args[0].Should().Be("PatchFirst");
        queuedJobs[0].Args[6].Should().Be(false);
    }

    [Fact]
    public async Task ExecuteRampAsync_WhenAdaptivePolicyReturnsZeroQueueTarget_DoesNotQueueRefreshJobs()
    {
        var adaptivePolicy = new FixedAdaptiveBudgetPolicy(new AdaptiveThroughputBudgetDecision(
            AdaptiveThroughputBudgetMode.HighPressure,
            MaxCandidates: 1,
            QueueTarget: 0,
            IncludeAllModes: false,
            CoverageRatio: 0.2d,
            BacklogAgeMinutes: 180d,
            RecentVelocityPerHour: 1d,
            CandidatePressureRatio: 2d));

        await using var harness = await Harness.CreateAsync(adaptivePolicy);
        harness.SeedActivePatch("15.2", DateTime.UtcNow.AddHours(-2));
        harness.SeedSummoner("QueuedNever", "NA1", DateTime.UtcNow.AddHours(-6));
        await harness.Db.SaveChangesAsync();

        await harness.Job.ExecuteRampAsync(CancellationToken.None);

        harness.BackgroundJobClient.Verify(
            x => x.Create(It.IsAny<Job>(), It.IsAny<IState>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteRampAsync_WhenApiPriorityIsActive_AllowsProgressDuringForcedCatchUpWindow()
    {
        var adaptivePolicy = new FixedAdaptiveBudgetPolicy(new AdaptiveThroughputBudgetDecision(
            AdaptiveThroughputBudgetMode.HighPressure,
            MaxCandidates: 1,
            QueueTarget: 0,
            IncludeAllModes: false,
            CoverageRatio: 0.2d,
            BacklogAgeMinutes: 180d,
            RecentVelocityPerHour: 1d,
            CandidatePressureRatio: 2d));

        await using var harness = await Harness.CreateAsync(adaptivePolicy);
        harness.SeedActivePatch("15.2", DateTime.UtcNow.AddHours(-2));
        harness.SeedSummoner("MaintenanceCatchUp", "NA1", DateTime.UtcNow.AddHours(-12));
        await harness.Db.SaveChangesAsync();

        var catchUpKey = RefreshLockKeys.BuildStarvationGuardrailCatchUpKey(nameof(SummonerMaintenanceJob));
        harness.RefreshLockRepository
            .Setup(x => x.AnyActiveByPrefixAsync(RefreshLockKeys.ApiPriorityRefreshPrefix, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        harness.RefreshLockRepository
            .Setup(x => x.GetAsync(catchUpKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RefreshLock
            {
                Id = Guid.NewGuid(),
                Key = catchUpKey,
                CreatedAtUtc = DateTime.UtcNow.AddMinutes(-1),
                LockedUntilUtc = DateTime.UtcNow.AddMinutes(5)
            });

        var queuedJobs = new List<Job>();
        harness.BackgroundJobClient
            .Setup(x => x.Create(It.IsAny<Job>(), It.IsAny<IState>()))
            .Callback<Job, IState>((job, _) => queuedJobs.Add(job))
            .Returns("job-1");

        await harness.Job.ExecuteRampAsync(CancellationToken.None);

        harness.BackgroundJobClient.Verify(
            x => x.Create(It.IsAny<Job>(), It.IsAny<IState>()),
            Times.Once);
        queuedJobs.Should().ContainSingle();
        queuedJobs[0].Args[3].Should().BeOfType<string>()
            .Which.Should().EndWith("|forced-catch-up");
    }

    [Fact]
    public async Task ExecuteRampAsync_WhenApiPriorityIsActiveAndGuardrailIsNotForced_DoesNotQueueRefreshJobs()
    {
        var adaptivePolicy = new FixedAdaptiveBudgetPolicy(new AdaptiveThroughputBudgetDecision(
            AdaptiveThroughputBudgetMode.Balanced,
            MaxCandidates: 2,
            QueueTarget: 2,
            IncludeAllModes: false,
            CoverageRatio: 0.8d,
            BacklogAgeMinutes: 20d,
            RecentVelocityPerHour: 12d,
            CandidatePressureRatio: 1d));

        await using var harness = await Harness.CreateAsync(adaptivePolicy);
        harness.SeedActivePatch("15.2", DateTime.UtcNow.AddHours(-2));
        harness.SeedSummoner("MaintenanceBlockedOne", "NA1", DateTime.UtcNow.AddHours(-12));
        harness.SeedSummoner("MaintenanceBlockedTwo", "NA1", DateTime.UtcNow.AddHours(-11));
        await harness.Db.SaveChangesAsync();

        harness.RefreshLockRepository
            .Setup(x => x.AnyActiveByPrefixAsync(RefreshLockKeys.ApiPriorityRefreshPrefix, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await harness.Job.ExecuteRampAsync(CancellationToken.None);

        harness.BackgroundJobClient.Verify(
            x => x.Create(It.IsAny<Job>(), It.IsAny<IState>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteForRegionAsync_WhenApiPriorityIsActiveAndRegionHasNoPatchSuccess_QueuesColdStartProgress()
    {
        var adaptivePolicy = new FixedAdaptiveBudgetPolicy(new AdaptiveThroughputBudgetDecision(
            AdaptiveThroughputBudgetMode.HighPressure,
            MaxCandidates: 1,
            QueueTarget: 0,
            IncludeAllModes: false,
            CoverageRatio: 0d,
            BacklogAgeMinutes: double.MaxValue,
            RecentVelocityPerHour: 0d,
            CandidatePressureRatio: 1d));

        await using var harness = await Harness.CreateAsync(adaptivePolicy);
        harness.SeedActivePatch("15.2", DateTime.UtcNow.AddHours(-2));
        harness.SeedSummoner("MaintenanceSeed", "EUW1", DateTime.UtcNow.AddHours(-12), platformRegion: "EUW1");
        await harness.Db.SaveChangesAsync();

        harness.RefreshLockRepository
            .Setup(x => x.AnyActiveByPrefixAsync(RefreshLockKeys.ApiPriorityRefreshPrefix, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var queuedJobs = new List<Job>();
        harness.BackgroundJobClient
            .Setup(x => x.Create(It.IsAny<Job>(), It.IsAny<IState>()))
            .Callback<Job, IState>((job, _) => queuedJobs.Add(job))
            .Returns("job-1");

        await harness.Job.ExecuteForRegionAsync("EUW1", CancellationToken.None);

        queuedJobs.Should().ContainSingle();
        queuedJobs[0].Args[2].Should().Be(Camille.Enums.PlatformRoute.EUW1);
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private Harness(
            SqliteConnection connection,
            TestSqliteTranscendenceContext db,
            SummonerMaintenanceJob job,
            Mock<IBackgroundJobClient> backgroundJobClient,
            Mock<IRefreshLockRepository> refreshLockRepository)
        {
            _connection = connection;
            Db = db;
            Job = job;
            BackgroundJobClient = backgroundJobClient;
            RefreshLockRepository = refreshLockRepository;
        }

        public TestSqliteTranscendenceContext Db { get; }
        public SummonerMaintenanceJob Job { get; }
        public Mock<IBackgroundJobClient> BackgroundJobClient { get; }
        public Mock<IRefreshLockRepository> RefreshLockRepository { get; }

        public static async Task<Harness> CreateAsync(
            IAdaptiveThroughputBudgetPolicy adaptiveBudgetPolicy,
            MultiRegionIngestionOptions? multiRegionOptions = null)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<TranscendenceContext>()
                .UseSqlite(connection)
                .Options;

            var db = new TestSqliteTranscendenceContext(options);
            await db.Database.EnsureCreatedAsync();

            var refreshLocks = new Mock<IRefreshLockRepository>();
            refreshLocks.Setup(x => x.AnyActiveByPrefixAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);
            refreshLocks.Setup(x => x.TryAcquireAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            refreshLocks.Setup(x => x.ReleaseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var backgroundJobs = new Mock<IBackgroundJobClient>();
            backgroundJobs.Setup(x => x.Create(It.IsAny<Job>(), It.IsAny<IState>()))
                .Returns("job-1");

            var scoringPolicy = new IngestionPriorityScoringPolicy(Options.Create(new IngestionPriorityPolicyOptions()));
            var starvationGuardrailPolicy = new StarvationGuardrailPolicy(Options.Create(new StarvationGuardrailOptions
            {
                Enabled = true,
                MaxEligibleDeferAgeMinutes = 50_000
            }));
            var job = new SummonerMaintenanceJob(
                db,
                backgroundJobs.Object,
                refreshLocks.Object,
                scoringPolicy,
                adaptiveBudgetPolicy,
                starvationGuardrailPolicy,
                Mock.Of<IIngestionThroughputTelemetry>(),
                Options.Create(new SummonerMaintenanceJobOptions
                {
                    MaxCandidateSummonersPerRun = 10,
                    MaxRefreshJobsToQueuePerRun = 4,
                    DataStaleAfterMinutes = 90,
                    RefreshLockMinutes = 10,
                    PrioritizeFavoriteSummoners = false,
                    PauseWhenApiPriorityRefreshActive = true,
                    NewPatchRampHours = 48,
                    RampMaxCandidateSummonersPerRun = 20,
                    RampMaxRefreshJobsToQueuePerRun = 6,
                    RampDataStaleAfterMinutes = 30
                }),
                Options.Create(new ChampionAnalyticsIngestionJobOptions
                {
                    MinimumSuccessfulMatchesForCurrentPatch = 50,
                    TargetSuccessfulMatchesForCurrentPatch = 100
                }),
                Options.Create(multiRegionOptions ?? new MultiRegionIngestionOptions()),
                Mock.Of<ILogger<SummonerMaintenanceJob>>());

            return new Harness(connection, db, job, backgroundJobs, refreshLocks);
        }

        public void SeedActivePatch(string version, DateTime releaseUtc)
        {
            foreach (var patch in Db.Patches)
                patch.IsActive = false;

            Db.Patches.Add(new Patch
            {
                Version = version,
                IsActive = true,
                ReleaseDate = releaseUtc,
                DetectedAt = releaseUtc
            });
        }

        public void SeedSummoner(
            string gameName,
            string tagLine,
            DateTime updatedAtUtc,
            string platformRegion = "NA1")
        {
            Db.Summoners.Add(new Summoner
            {
                Id = Guid.NewGuid(),
                PlatformRegion = platformRegion,
                Region = platformRegion is "EUW1" or "EUN1" ? "europe" : platformRegion is "KR" ? "asia" : "americas",
                Puuid = $"puuid-{Guid.NewGuid():N}",
                GameName = gameName,
                TagLine = tagLine,
                GameNameNormalized = gameName.ToUpperInvariant(),
                TagLineNormalized = tagLine.ToUpperInvariant(),
                SummonerLevel = 100,
                UpdatedAt = updatedAtUtc
            });
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class FixedAdaptiveBudgetPolicy(AdaptiveThroughputBudgetDecision decision)
        : IAdaptiveThroughputBudgetPolicy
    {
        public int VelocityLookbackMinutes => 30;

        public AdaptiveThroughputBudgetDecision ComputeBudget(AdaptiveThroughputBudgetInput input) => decision;
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
