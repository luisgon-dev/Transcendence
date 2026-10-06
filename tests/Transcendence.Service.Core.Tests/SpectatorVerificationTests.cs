using System.Net;
using FluentAssertions;
using Camille.Enums;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Transcendence.Data.Models.LiveGame;
using Transcendence.Data.Models.LoL.Account;
using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Core.Services.Jobs;
using Transcendence.Service.Core.Services.LiveGame.Implementations;
using Transcendence.Service.Core.Services.LiveGame.Interfaces;
using Transcendence.Service.Core.Services.Operations;
using Transcendence.Service.Core.Services.RiotApi;
using Transcendence.Service.Core.Tests.Support;

namespace Transcendence.Service.Core.Tests;

public sealed class SpectatorVerificationTests
{
    [Theory]
    [InlineData(404, null, true)]
    [InlineData(204, null, false)]
    [InlineData(422, null, false)]
    [InlineData(200, "null", false)]
    [InlineData(200, "{broken", false)]
    public async Task ExecutedProbeJobOnlyCertifiesSavedVerifiedObservation(int status, string? body, bool verified)
    {
        using var stub = new RiotApiServiceHttpTests.RiotApiStub { Response = ((HttpStatusCode)status, body) };
        using var services = BuildServices();
        var summoners = new Mock<ISummonerRepository>();
        summoners.Setup(x => x.FindByRiotIdAsync("NA1", "Player", "TAG", null,
            It.IsAny<CancellationToken>())).ReturnsAsync(new Summoner { Id = Guid.NewGuid(), Puuid = "stored-puuid" });
        var snapshots = new Mock<ILiveGameSnapshotRepository>();
        var operations = OperationTrackerMock.Create();
        var job = new LiveGameProbeJob(summoners.Object, BuildPolling(stub, services), snapshots.Object,
            Mock.Of<IRefreshLockRepository>(), NullLogger<LiveGameProbeJob>.Instance, operations.Object);
        var id = Guid.NewGuid();
        var act = () => job.ProbeAsync(id, "NA1", "Player", "TAG", "probe");

        if (verified)
        {
            await act();
            snapshots.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
            operations.Verify(x => x.CompleteAsync(id, OperationStatuses.Succeeded,
                It.Is<OperationResult>(r => r.SnapshotId != null && r.ObservedAtUtc != null &&
                    r.LiveGame != null && r.LiveGame.State == "offline"), It.IsAny<CancellationToken>()), Times.Once);
        }
        else
        {
            await act.Should().ThrowAsync<Exception>();
            snapshots.Verify(x => x.AddAsync(It.IsAny<LiveGameSnapshot>(), It.IsAny<CancellationToken>()), Times.Never);
            operations.Verify(x => x.CompleteAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<OperationResult>(),
                It.IsAny<CancellationToken>()), Times.Never);
            operations.Verify(x => x.RetryAsync(id, "probe_failed", null, It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Theory]
    [InlineData(204, null)]
    [InlineData(422, null)]
    [InlineData(403, null)]
    [InlineData(500, null)]
    [InlineData(200, "null")]
    [InlineData(200, "{}")]
    [InlineData(200, "{broken")]
    [InlineData(200, "{\"gameId\":123,\"gameStartTime\":1760000000000,\"mapId\":11,\"participants\":[null]}")]
    public async Task UnverifiedResponsesNeverBecomeOfflineObservations(int status, string? body)
    {
        using var stub = new RiotApiServiceHttpTests.RiotApiStub { Response = ((HttpStatusCode)status, body) };
        using var services = BuildServices();
        var service = BuildPolling(stub, services);

        var act = () => service.ProbeCurrentGameAsync("NA1", "Player", "TAG");
        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task ActualSpectator404ProducesVerifiedOfflineObservation()
    {
        using var stub = new RiotApiServiceHttpTests.RiotApiStub { Response = (HttpStatusCode.NotFound, null) };
        using var services = BuildServices();
        var response = await BuildPolling(stub, services).ProbeCurrentGameAsync("NA1", "Player", "TAG");
        response.State.Should().Be("offline");
        response.Participants.Should().BeEmpty();
    }

    [Fact]
    public async Task Spectator429PausesRegionalGateAndDoesNotInventOfflineState()
    {
        using var stub = new RiotApiServiceHttpTests.RiotApiStub
        {
            Response = (HttpStatusCode.TooManyRequests, null), RetryAfterSeconds = 7
        };
        using var services = BuildServices();
        var gate = new Mock<IRiotRateGate>();
        gate.Setup(x => x.AcquireAsync("NA1", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var act = () => BuildPolling(stub, services, gate).ProbeCurrentGameAsync("NA1", "Player", "TAG");

        await act.Should().ThrowAsync<Exception>();
        gate.Verify(x => x.Pause("NA1", TimeSpan.FromSeconds(7)), Times.Once);
    }

    [Fact]
    public async Task ValidActiveGamePayloadIsReadWithoutSDKQueueEnums()
    {
        using var stub = new RiotApiServiceHttpTests.RiotApiStub
        {
            Response = (HttpStatusCode.OK,
                """{"gameId":123,"gameStartTime":1760000000000,"gameLength":20,"gameQueueConfigId":9999,"mapId":11,"participants":[{"puuid":"p","teamId":100,"championId":103,"spell1Id":4,"spell2Id":14,"profileIconId":1}]}""")
        };
        var game = await stub.BuildSpectatorClient().GetCurrentGameAsync(PlatformRoute.NA1, "p");
        game!.GameQueueConfigId.Should().Be(9999);
        game.Participants.Should().ContainSingle();
    }

    private static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHybridCache();
        return services.BuildServiceProvider();
    }

    private static RiotLiveGamePollingService BuildPolling(RiotApiServiceHttpTests.RiotApiStub stub, ServiceProvider services,
        Mock<IRiotRateGate>? rateGate = null)
    {
        var summoners = new Mock<ISummonerRepository>();
        summoners.Setup(x => x.FindByRiotIdAsync("NA1", "Player", "TAG", null,
            It.IsAny<CancellationToken>())).ReturnsAsync(new Summoner { Id = Guid.NewGuid(), Puuid = "stored-puuid" });
        rateGate ??= new Mock<IRiotRateGate>();
        rateGate.Setup(x => x.AcquireAsync("NA1", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        return new RiotLiveGamePollingService(stub.BuildContext(), summoners.Object, rateGate.Object,
            services.GetRequiredService<HybridCache>(), Mock.Of<ILiveGameAnalysisService>(),
            NullLogger<RiotLiveGamePollingService>.Instance, stub.BuildSpectatorClient());
    }
}
