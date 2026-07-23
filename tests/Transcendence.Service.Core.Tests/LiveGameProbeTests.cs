using Camille.Enums;
using FluentAssertions;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Transcendence.Data.Models.LiveGame;
using Transcendence.Data.Models.LoL.Account;
using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Core.Services.Jobs;
using Transcendence.Service.Core.Services.LiveGame.Implementations;
using Transcendence.Service.Core.Services.LiveGame.Interfaces;
using Transcendence.Service.Core.Services.LiveGame.Models;

namespace Transcendence.Service.Core.Tests;

public sealed class LiveGameProbeTests
{
    [Fact]
    public async Task Coordinator_coalesces_duplicate_probe_requests()
    {
        var locks = new Mock<IRefreshLockRepository>();
        locks.SetupSequence(repository => repository.TryAcquireOwnedAsync(
                It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid())
            .ReturnsAsync((Guid?)null);
        var jobs = new Mock<IBackgroundJobClient>();
        jobs.Setup(client => client.Create(It.IsAny<Job>(), It.IsAny<IState>())).Returns("probe-1");
        var coordinator = new LiveGameProbeCoordinator(
            locks.Object,
            jobs.Object,
            NullLogger<LiveGameProbeCoordinator>.Instance);

        var queued = await coordinator.EnqueueAsync(PlatformRoute.NA1, " Player ", " tag ");
        var duplicate = await coordinator.EnqueueAsync(PlatformRoute.NA1, "Player", "tag");

        queued.WasQueued.Should().BeTrue();
        duplicate.WasQueued.Should().BeFalse();
        jobs.Verify(client => client.Create(It.IsAny<Job>(), It.IsAny<IState>()), Times.Once);
        locks.Verify(repository => repository.TryAcquireOwnedAsync(
            RefreshLockKeys.BuildLiveGameProbeKey(PlatformRoute.NA1, "Player", "tag"),
            It.IsAny<TimeSpan>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Probe_job_persists_fresh_payload_and_releases_owned_lock()
    {
        var summoner = new Summoner
        {
            Id = Guid.NewGuid(),
            Puuid = "probe-puuid",
            PlatformRegion = "NA1",
            GameName = "Player",
            TagLine = "TAG"
        };
        var summoners = new Mock<ISummonerRepository>();
        summoners.Setup(repository => repository.FindByRiotIdAsync(
                "NA1",
                "Player",
                "TAG",
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(summoner);
        var polling = new Mock<ILiveGamePollingService>();
        polling.Setup(service => service.ProbeCurrentGameAsync(
                "NA1",
                "Player",
                "TAG",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LiveGameResponseDto(
                "in_game",
                "NA1",
                "game-1",
                "400",
                "11",
                DateTime.UtcNow,
                30,
                [],
                DateTime.UtcNow,
                0));
        LiveGameSnapshot? persisted = null;
        var snapshots = new Mock<ILiveGameSnapshotRepository>();
        snapshots.Setup(repository => repository.AddAsync(
                It.IsAny<LiveGameSnapshot>(),
                It.IsAny<CancellationToken>()))
            .Callback<LiveGameSnapshot, CancellationToken>((snapshot, _) => persisted = snapshot)
            .Returns(Task.CompletedTask);
        snapshots.Setup(repository => repository.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var locks = new Mock<IRefreshLockRepository>();
        locks.Setup(repository => repository.ReleaseOwnedAsync(
                It.IsAny<string>(),
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var ownerToken = Guid.NewGuid();
        var lockKey = RefreshLockKeys.BuildLiveGameProbeKey(PlatformRoute.NA1, "Player", "TAG");
        var job = new LiveGameProbeJob(
            summoners.Object,
            polling.Object,
            snapshots.Object,
            locks.Object,
            NullLogger<LiveGameProbeJob>.Instance);

        await job.ProbeAsync(
            "NA1",
            "Player",
            "TAG",
            RefreshLockKeys.BuildOwnedHandle(lockKey, ownerToken));

        persisted.Should().NotBeNull();
        persisted!.SummonerId.Should().Be(summoner.Id);
        persisted.State.Should().Be("in_game");
        persisted.GameId.Should().Be("game-1");
        persisted.PayloadJson.Should().Contain("game-1");
        locks.Verify(repository => repository.ReleaseOwnedAsync(
            lockKey,
            ownerToken,
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
