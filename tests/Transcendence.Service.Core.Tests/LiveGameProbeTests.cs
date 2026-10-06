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
using Transcendence.Data.Models.Service;
using Transcendence.Service.Core.Services.Operations;
using Transcendence.Service.Core.Tests.Support;

namespace Transcendence.Service.Core.Tests;

public sealed class LiveGameProbeTests
{
    [Fact]
    public async Task PersistenceFailureNeverCertifiesAProbeAndLeavesItRetrying()
    {
        var summoner = new Summoner { Id = Guid.NewGuid(), Puuid = "p" };
        var summoners = new Mock<ISummonerRepository>();
        summoners.Setup(x => x.FindByRiotIdAsync("NA1", "Player", "TAG", null,
            It.IsAny<CancellationToken>())).ReturnsAsync(summoner);
        var polling = new Mock<ILiveGamePollingService>();
        polling.Setup(x => x.ProbeCurrentGameAsync("NA1", "Player", "TAG", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LiveGameResponseDto("offline", "NA1", null, null, null, null, null, [], DateTime.UtcNow, 0));
        var snapshots = new Mock<ILiveGameSnapshotRepository>();
        snapshots.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database unavailable"));
        var operations = OperationTrackerMock.Create();
        var job = new LiveGameProbeJob(summoners.Object, polling.Object, snapshots.Object,
            Mock.Of<IRefreshLockRepository>(), NullLogger<LiveGameProbeJob>.Instance, operations.Object);
        var operationId = Guid.NewGuid();
        var act = () => job.ProbeAsync(operationId, "NA1", "Player", "TAG", "lock");

        await act.Should().ThrowAsync<InvalidOperationException>();
        operations.Verify(x => x.RetryAsync(operationId, "probe_failed", null, It.IsAny<CancellationToken>()), Times.Once);
        operations.Verify(x => x.CompleteAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<OperationResult>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MissingStoredAccountIsFailedWithoutInventingAnOfflineSnapshot()
    {
        var snapshots = new Mock<ILiveGameSnapshotRepository>();
        var operations = OperationTrackerMock.Create();
        var polling = new Mock<ILiveGamePollingService>();
        var job = new LiveGameProbeJob(Mock.Of<ISummonerRepository>(), polling.Object, snapshots.Object,
            Mock.Of<IRefreshLockRepository>(), NullLogger<LiveGameProbeJob>.Instance, operations.Object);
        var operationId = Guid.NewGuid();

        await job.ProbeAsync(operationId, "NA1", "Missing", "TAG", "lock");

        operations.Verify(x => x.FailAsync(operationId, "summoner_missing", It.IsAny<CancellationToken>()), Times.Once);
        snapshots.Verify(x => x.AddAsync(It.IsAny<LiveGameSnapshot>(), It.IsAny<CancellationToken>()), Times.Never);
        polling.Verify(x => x.ProbeCurrentGameAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Coordinator_coalesces_duplicate_probe_requests()
    {
        var locks = new Mock<IRefreshLockRepository>();
        var token = Guid.NewGuid();
        locks.SetupSequence(repository => repository.TryAcquireOwnedAsync(
                It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(token)
            .ReturnsAsync((Guid?)null);
        locks.Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RefreshLock { Key = "probe", OwnerToken = token });
        var executionId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var tracker = new Mock<IBackgroundOperationTracker>();
        tracker.Setup(x => x.CreateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<OperationResource>(),
            token, It.IsAny<OperationOwner>(), It.IsAny<OperationDispatch>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TrackedOperation(executionId, requestId));
        tracker.Setup(x => x.JoinAsync(It.IsAny<string>(), token, It.IsAny<OperationOwner>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TrackedOperation(executionId, requestId));
        var jobs = new Mock<IBackgroundJobClient>();
        jobs.Setup(client => client.Create(It.IsAny<Job>(), It.IsAny<IState>())).Returns("probe-1");
        var dispatcher = new Mock<IBackgroundOperationDispatcher>();
        dispatcher.Setup(x => x.DispatchAsync(executionId, It.IsAny<CancellationToken>()))
            .Returns<Guid, CancellationToken>((_, _) =>
            {
                jobs.Object.Create(Job.FromExpression(() => Console.WriteLine("tracked-test")), new EnqueuedState());
                return Task.CompletedTask;
            });
        var coordinator = new LiveGameProbeCoordinator(
            locks.Object,
            tracker.Object,
            dispatcher.Object,
            NullLogger<LiveGameProbeCoordinator>.Instance);

        var owner = new OperationOwner(OperationOwnerKind.Application, Guid.NewGuid());
        var queued = await coordinator.EnqueueAsync(PlatformRoute.NA1, " Player ", " tag ", owner);
        var duplicate = await coordinator.EnqueueAsync(PlatformRoute.NA1, "Player", "tag", owner);

        queued.WasQueued.Should().BeTrue();
        duplicate.WasQueued.Should().BeFalse();
        duplicate.OperationId.Should().Be(queued.OperationId);
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
        var operations = OperationTrackerMock.Create();
        operations.Setup(x => x.CompleteAsync(It.IsAny<Guid>(), OperationStatuses.Succeeded, It.IsAny<OperationResult>(),
                It.IsAny<CancellationToken>()))
            .Callback<Guid, string, OperationResult, CancellationToken>((_, _, result, _) =>
            {
                snapshots.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
                result.SnapshotId.Should().Be(persisted!.Id);
                result.ObservedAtUtc.Should().Be(persisted.ObservedAtUtc);
                result.LiveGame!.LastUpdatedUtc.Should().Be(persisted.ObservedAtUtc);
                result.LiveGame.GameId.Should().Be("game-1");
            }).Returns(Task.CompletedTask);
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
            NullLogger<LiveGameProbeJob>.Instance, operations.Object);

        await job.ProbeAsync(
            Guid.NewGuid(),
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
