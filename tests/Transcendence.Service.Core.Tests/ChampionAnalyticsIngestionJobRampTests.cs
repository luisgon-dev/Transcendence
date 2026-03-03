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
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Data.Models.LoL.Static;
using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Core.Services.Jobs;
using Transcendence.Service.Core.Services.Jobs.Configuration;
using Transcendence.Service.Core.Services.Jobs.Interfaces;

namespace Transcendence.Service.Core.Tests;

public class ChampionAnalyticsIngestionJobRampTests
{
    [Fact]
    public async Task ExecuteRampAsync_WhenRampWindowInactive_DoesNotQueueRefreshJobs()
    {
        await using var harness = await Harness.CreateAsync();
        harness.SeedActivePatch("15.1", DateTime.UtcNow.AddHours(-100));
        harness.SeedSummoner("OldPatchOne", "NA1");
        await harness.Db.SaveChangesAsync();

        await harness.Job.ExecuteRampAsync(CancellationToken.None);

        harness.BackgroundJobClient.Verify(
            x => x.Create(It.IsAny<Job>(), It.IsAny<IState>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteRampAsync_WhenRampWindowActive_UsesRampQueueingLimits()
    {
        await using var harness = await Harness.CreateAsync();
        harness.SeedActivePatch("15.2", DateTime.UtcNow.AddHours(-2));
        for (var i = 0; i < 8; i++)
            harness.SeedSummoner($"Ramp{i}", "NA1");
        await harness.Db.SaveChangesAsync();

        await harness.Job.ExecuteRampAsync(CancellationToken.None);

        harness.BackgroundJobClient.Verify(
            x => x.Create(It.IsAny<Job>(), It.IsAny<IState>()),
            Times.AtLeast(6));
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private Harness(
            SqliteConnection connection,
            TestSqliteTranscendenceContext db,
            ChampionAnalyticsIngestionJob job,
            Mock<IBackgroundJobClient> backgroundJobClient)
        {
            _connection = connection;
            Db = db;
            Job = job;
            BackgroundJobClient = backgroundJobClient;
        }

        public TestSqliteTranscendenceContext Db { get; }
        public ChampionAnalyticsIngestionJob Job { get; }
        public Mock<IBackgroundJobClient> BackgroundJobClient { get; }

        public static async Task<Harness> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<TranscendenceContext>()
                .UseSqlite(connection)
                .Options;

            var db = new TestSqliteTranscendenceContext(options);
            await db.Database.EnsureCreatedAsync();

            var bootstrap = new Mock<ISummonerBootstrapService>();
            bootstrap.Setup(x => x.EnsureSeededFromChallengerAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(0);

            var refreshLocks = new Mock<IRefreshLockRepository>();
            refreshLocks.Setup(x => x.AnyActiveByPrefixAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);
            refreshLocks.Setup(x => x.TryAcquireAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            var backgroundJobs = new Mock<IBackgroundJobClient>();
            backgroundJobs.Setup(x => x.Create(It.IsAny<Job>(), It.IsAny<IState>()))
                .Returns("job-1");

            var job = new ChampionAnalyticsIngestionJob(
                db,
                bootstrap.Object,
                refreshLocks.Object,
                backgroundJobs.Object,
                Options.Create(new ChampionAnalyticsIngestionJobOptions
                {
                    MinimumSuccessfulMatchesForCurrentPatch = 50,
                    TargetSuccessfulMatchesForCurrentPatch = 100,
                    DataStaleAfterMinutes = 90,
                    MaxCandidateSummonersPerRun = 5,
                    MinRefreshJobsToQueuePerRun = 1,
                    MaxRefreshJobsToQueuePerRun = 2,
                    RefreshLockMinutes = 10,
                    PrioritizeFavoriteSummoners = false,
                    FallbackToTrackedSummoners = true,
                    PauseWhenApiPriorityRefreshActive = true,
                    NewPatchRampHours = 48,
                    RampDataStaleAfterMinutes = 30,
                    RampMaxCandidateSummonersPerRun = 100,
                    RampMinRefreshJobsToQueuePerRun = 6,
                    RampMaxRefreshJobsToQueuePerRun = 10
                }),
                Mock.Of<ILogger<ChampionAnalyticsIngestionJob>>());

            return new Harness(connection, db, job, backgroundJobs);
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

        public void SeedSummoner(string gameName, string tagLine)
        {
            Db.Summoners.Add(new Summoner
            {
                Id = Guid.NewGuid(),
                PlatformRegion = "NA1",
                Region = "americas",
                Puuid = $"puuid-{Guid.NewGuid():N}",
                GameName = gameName,
                TagLine = tagLine,
                GameNameNormalized = gameName.ToUpperInvariant(),
                TagLineNormalized = tagLine.ToUpperInvariant(),
                SummonerLevel = 100,
                UpdatedAt = DateTime.UtcNow.AddHours(-10)
            });
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
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
