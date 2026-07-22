using System.Text.Json;
using FluentAssertions;
using Moq;
using Transcendence.Data.Models.LiveGame;
using Transcendence.Data.Models.LoL.Account;
using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Core.Services.LiveGame.Implementations;
using Transcendence.Service.Core.Services.LiveGame.Models;

namespace Transcendence.Service.Core.Tests;

public sealed class StoredLiveGameServiceTests
{
    [Fact]
    public async Task GetCurrentGame_restores_the_complete_worker_payload_and_recomputes_age()
    {
        var observedAt = DateTime.UtcNow.AddSeconds(-45);
        var response = new LiveGameResponseDto(
            "in_game",
            "NA1",
            "game-1",
            "420",
            "11",
            observedAt.AddMinutes(-10),
            600,
            [new LiveGameParticipantDto("puuid", "Player#NA1", "sid", 100, 24, 4, 12, 1, [8005, 5008], 8000, 8300)],
            observedAt,
            0,
            new LiveGameAnalysisDto(
                observedAt,
                [new LiveGameParticipantAnalysisDto(
                    "puuid",
                    100,
                    24,
                    "DIAMOND",
                    "I",
                    50,
                    0.6,
                    3.1,
                    0.51,
                    20,
                    3,
                    [new LiveGameChampionPoolEntryDto(24, 8, 0.625)])],
                []));

        var summonerRepository = new Mock<ISummonerRepository>();
        summonerRepository
            .Setup(repository => repository.FindByRiotIdAsync(
                "NA1",
                "Player",
                "NA1",
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Summoner
            {
                Id = Guid.NewGuid(),
                Puuid = "puuid",
                PlatformRegion = "NA1",
                Region = "AMERICAS"
            });
        var snapshotRepository = new Mock<ILiveGameSnapshotRepository>();
        snapshotRepository
            .Setup(repository => repository.GetLatestByPuuidAsync("puuid", "NA1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LiveGameSnapshot
            {
                Id = Guid.NewGuid(),
                SummonerId = Guid.NewGuid(),
                Puuid = "puuid",
                PlatformRegion = "NA1",
                State = "in_game",
                GameId = "game-1",
                PayloadJson = JsonSerializer.Serialize(response),
                ObservedAtUtc = observedAt,
                NextPollAtUtc = observedAt.AddMinutes(1)
            });

        var service = new StoredLiveGameService(summonerRepository.Object, snapshotRepository.Object);

        var result = await service.GetCurrentGameAsync("na", "Player", "NA1");

        result.Participants.Should().ContainSingle();
        result.Participants[0].PerkIds.Should().Equal(8005, 5008);
        result.Analysis!.Participants[0].CurrentStreak.Should().Be(3);
        result.Analysis.Participants[0].ChampionPool.Should().ContainSingle(entry => entry.ChampionId == 24);
        result.LastUpdatedUtc.Should().Be(observedAt);
        result.DataAgeSeconds.Should().BeInRange(44, 50);
    }

    [Fact]
    public async Task GetCurrentGame_falls_back_to_legacy_state_only_snapshot()
    {
        var summonerRepository = new Mock<ISummonerRepository>();
        summonerRepository
            .Setup(repository => repository.FindByRiotIdAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Summoner
            {
                Id = Guid.NewGuid(),
                Puuid = "legacy-puuid",
                PlatformRegion = "NA1",
                Region = "AMERICAS"
            });
        var snapshotRepository = new Mock<ILiveGameSnapshotRepository>();
        snapshotRepository
            .Setup(repository => repository.GetLatestByPuuidAsync(
                "legacy-puuid",
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LiveGameSnapshot
            {
                Id = Guid.NewGuid(),
                SummonerId = Guid.NewGuid(),
                Puuid = "legacy-puuid",
                PlatformRegion = "NA1",
                State = "in_game",
                GameId = "legacy-game",
                ObservedAtUtc = DateTime.UtcNow,
                NextPollAtUtc = DateTime.UtcNow
            });

        var service = new StoredLiveGameService(summonerRepository.Object, snapshotRepository.Object);
        var result = await service.GetCurrentGameAsync("na", "Player", "NA1");

        result.State.Should().Be("in_game");
        result.GameId.Should().Be("legacy-game");
        result.Participants.Should().BeEmpty();
    }
}
