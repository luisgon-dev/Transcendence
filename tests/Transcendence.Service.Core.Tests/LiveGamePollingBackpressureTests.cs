using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Transcendence.Data;
using Transcendence.Data.Models.LiveGame;
using Transcendence.Data.Models.LoL.Account;
using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Core.Services.Jobs;
using Transcendence.Service.Core.Services.Jobs.Configuration;
using Transcendence.Service.Core.Services.Jobs.Interfaces;
using Transcendence.Service.Core.Services.LiveGame.Interfaces;
using Transcendence.Service.Core.Services.LiveGame.Models;
using Transcendence.Service.Core.Tests.Support;

namespace Transcendence.Service.Core.Tests;

public sealed class LiveGamePollingBackpressureTests
{
    [Fact]
    public async Task PausedRegionPreservesStoredGameAndOtherRegionsStillRun()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TranscendenceContext>().UseSqlite(connection).Options;
        await using var db = new SqliteCompatibleTranscendenceContext(options);
        await db.Database.EnsureCreatedAsync();
        var now = DateTime.UtcNow;
        db.Summoners.AddRange(
            Summoner("First", "NA1", now), Summoner("Second", "NA1", now.AddMinutes(-1)),
            Summoner("Other", "EUW1", now.AddMinutes(-2)));
        await db.SaveChangesAsync();
        var polling = new Mock<ILiveGamePollingService>();
        polling.Setup(x => x.FetchCurrentGameAsync("NA1", "First", "TAG", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new LiveGameProbeDeferredException("NA1", TimeSpan.FromSeconds(30)));
        polling.Setup(x => x.FetchCurrentGameAsync("EUW1", "Other", "TAG", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LiveGameResponseDto("offline", "EUW1", null, null, null, null, null, [], now, 0));
        var original = new LiveGameSnapshot { Id = Guid.NewGuid(), State = "in_game", ObservedAtUtc = now.AddMinutes(-3),
            NextPollAtUtc = now.AddMinutes(-1), Puuid = "First", PlatformRegion = "NA1", PayloadJson = "stored" };
        var snapshots = new Mock<ILiveGameSnapshotRepository>();
        snapshots.Setup(x => x.GetLatestByPuuidAsync("First", "NA1", It.IsAny<CancellationToken>())).ReturnsAsync(original);
        var job = new LiveGamePollingJob(db, Mock.Of<ISummonerBootstrapService>(), polling.Object, snapshots.Object,
            Mock.Of<IRefreshLockRepository>(), Options.Create(new LiveGamePollingJobOptions {
                PauseWhileChampionAnalyticsUnavailable = false, PollOnlyFavoriteSummoners = false,
                MaxTrackedSummonersPerRun = 10, MaxRiotRequestsPerRun = 10 }),
            Options.Create(new ChampionAnalyticsIngestionJobOptions { PauseWhenApiPriorityRefreshActive = false }),
            NullLogger<LiveGamePollingJob>.Instance);

        await job.ExecuteAsync();

        polling.Verify(x => x.FetchCurrentGameAsync("NA1", "Second", "TAG", It.IsAny<CancellationToken>()), Times.Never);
        polling.Verify(x => x.FetchCurrentGameAsync("EUW1", "Other", "TAG", It.IsAny<CancellationToken>()), Times.Once);
        snapshots.Verify(x => x.AddAsync(It.Is<LiveGameSnapshot>(s => s.PlatformRegion == "EUW1"),
            It.IsAny<CancellationToken>()), Times.Once);
        snapshots.Verify(x => x.AddAsync(It.Is<LiveGameSnapshot>(s => s.PlatformRegion == "NA1"),
            It.IsAny<CancellationToken>()), Times.Never);
        original.State.Should().Be("in_game");
        original.ObservedAtUtc.Should().Be(now.AddMinutes(-3));
    }

    private static Summoner Summoner(string name, string region, DateTime updatedAt) => new() {
        Id = Guid.NewGuid(), Puuid = name, GameName = name, TagLine = "TAG", PlatformRegion = region,
        Region = region == "NA1" ? "AMERICAS" : "EUROPE", UpdatedAt = updatedAt
    };
}
