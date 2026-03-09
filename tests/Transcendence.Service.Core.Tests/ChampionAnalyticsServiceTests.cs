using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Static;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Service.Core.Services.Analytics.Implementations;
using Transcendence.Service.Core.Services.Analytics.Interfaces;
using Transcendence.Service.Core.Services.Analytics.Models;
using Transcendence.Service.Core.Services.Jobs.Configuration;
using Transcendence.Service.Core.Tests.Support;

namespace Transcendence.Service.Core.Tests;

public class ChampionAnalyticsServiceTests
{
    [Fact]
    public async Task GetTierListAsync_NoActivePatch_DoesNotFallbackToStoredMatches()
    {
        await using var harness = await Harness.CreateAsync();
        harness.Db.Matches.Add(new Transcendence.Data.Models.LoL.Match.Match
        {
            Id = Guid.NewGuid(),
            MatchId = "NA1_legacy",
            MatchDate = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Duration = 1800,
            Patch = "14.5",
            QueueId = 420,
            QueueFamily = "RANKED_SOLO_DUO",
            QueueType = "RANKED_SOLO_5x5",
            Status = FetchStatus.Success,
            FetchedAt = DateTime.UtcNow
        });
        await harness.Db.SaveChangesAsync();

        var result = await harness.Service.GetTierListAsync("ALL", null, null, CancellationToken.None);

        result.Patch.Should().Be("Unknown");
        result.Entries.Should().BeEmpty();
        result.Sample.Should().NotBeNull();
        result.Sample!.SampleStatus.Should().Be(AnalyticsSampleStatus.NoData);
    }

    [Fact]
    public async Task GetWinRatesAsync_EarlyPatchBelowThreshold_ReturnsLowSample()
    {
        await using var harness = await Harness.CreateAsync();
        harness.SetActivePatch("15.1", DateTime.UtcNow.AddHours(-2));
        harness.ComputeService
            .Setup(x => x.ComputeWinRatesAsync(
                103,
                It.IsAny<ChampionAnalyticsFilter>(),
                "15.1",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new ChampionWinRateDto(103, "MIDDLE", "EMERALD_PLUS", 30, 17, 0.566, 0.12, 0.03, 1, 40, "15.1")
            ]);
        await harness.Db.SaveChangesAsync();

        var result = await harness.Service.GetWinRatesAsync(
            103,
            new ChampionAnalyticsFilter(RankTier: "EMERALD_PLUS"),
            CancellationToken.None);

        result.Sample.Should().NotBeNull();
        result.Sample!.SampleStatus.Should().Be(AnalyticsSampleStatus.LowSample);
        result.Sample.IsEarlyPatchWindow.Should().BeTrue();
        result.Sample.MinimumRecommendedSampleSize.Should().Be(40);
        result.Sample.SampleSize.Should().Be(30);
    }

    [Fact]
    public async Task GetWinRatesAsync_MaturePatchThresholdMet_ReturnsSufficient()
    {
        await using var harness = await Harness.CreateAsync();
        harness.SetActivePatch("15.1", DateTime.UtcNow.AddHours(-120));
        harness.ComputeService
            .Setup(x => x.ComputeWinRatesAsync(
                266,
                It.IsAny<ChampionAnalyticsFilter>(),
                "15.1",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new ChampionWinRateDto(266, "TOP", "EMERALD_PLUS", 160, 80, 0.5, 0.2, 0.04, 1, 40, "15.1")
            ]);
        await harness.Db.SaveChangesAsync();

        var result = await harness.Service.GetWinRatesAsync(
            266,
            new ChampionAnalyticsFilter(RankTier: "EMERALD_PLUS"),
            CancellationToken.None);

        result.Sample.Should().NotBeNull();
        result.Sample!.SampleStatus.Should().Be(AnalyticsSampleStatus.Sufficient);
        result.Sample.IsEarlyPatchWindow.Should().BeFalse();
        result.Sample.MinimumRecommendedSampleSize.Should().Be(100);
    }

    [Fact]
    public async Task GetTierListAsync_UnsupportedRegion_FallsBackToGlobal()
    {
        await using var harness = await Harness.CreateAsync();
        harness.SetActivePatch("15.2", DateTime.UtcNow.AddHours(-6));
        await harness.Db.SaveChangesAsync();

        await harness.Service.GetTierListAsync("ALL", null, "OCE1", CancellationToken.None);

        harness.ComputeService.Verify(x => x.ComputeTierListAsync(
            "ALL",
            null,
            "ALL",
            "15.2",
            It.IsAny<CancellationToken>()));
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly ServiceProvider _services;

        private Harness(
            SqliteConnection connection,
            SqliteCompatibleTranscendenceContext db,
            ServiceProvider services,
            Mock<IChampionAnalyticsComputeService> computeService,
            ChampionAnalyticsService service)
        {
            _connection = connection;
            Db = db;
            _services = services;
            ComputeService = computeService;
            Service = service;
        }

        public SqliteCompatibleTranscendenceContext Db { get; }
        public Mock<IChampionAnalyticsComputeService> ComputeService { get; }
        public ChampionAnalyticsService Service { get; }

        public static async Task<Harness> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<TranscendenceContext>()
                .UseSqlite(connection)
                .Options;

            var db = new SqliteCompatibleTranscendenceContext(options);
            await db.Database.EnsureCreatedAsync();

            var serviceCollection = new ServiceCollection();
            serviceCollection.AddLogging();
            serviceCollection.AddHybridCache();
            var services = serviceCollection.BuildServiceProvider();

            var compute = new Mock<IChampionAnalyticsComputeService>();
            compute
            .Setup(x => x.ComputeTierListAsync(
                    It.IsAny<string>(),
                    It.IsAny<string?>(),
                    It.IsAny<string?>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);

            var service = new ChampionAnalyticsService(
                db,
                services.GetRequiredService<HybridCache>(),
                compute.Object,
                Options.Create(new ChampionAnalyticsComputeOptions
                {
                    MinimumGamesRequired = 100,
                    EarlyPatchMinimumGamesRequired = 40,
                    EarlyPatchWindowHours = 72
                }),
                Options.Create(new MultiRegionIngestionOptions
                {
                    Regions =
                    [
                        new RegionConfig { Region = "NA1", Enabled = true },
                        new RegionConfig { Region = "EUW1", Enabled = true },
                        new RegionConfig { Region = "KR", Enabled = true }
                    ]
                }));

            return new Harness(connection, db, services, compute, service);
        }

        public void SetActivePatch(string version, DateTime releaseUtc)
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

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _services.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

}
