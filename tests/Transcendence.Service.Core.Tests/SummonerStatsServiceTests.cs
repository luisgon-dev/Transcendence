using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Account;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Data.Models.LoL.Static;
using Transcendence.Service.Core.Services.Analysis.Exceptions;
using Transcendence.Service.Core.Services.Analysis.Implementations;
using Transcendence.Service.Core.Services.RiotApi;
using Transcendence.Service.Core.Tests.Support;

namespace Transcendence.Service.Core.Tests;

public class SummonerStatsServiceTests
{
    [Fact]
    public async Task GetSummonerOverviewAsync_FiltersToRankedSoloQueue()
    {
        await using var harness = await SummonerStatsHarness.CreateAsync();
        var summoner = harness.CreateSummoner();

        harness.AddParticipant(
            summoner,
            queueId: QueueCatalog.RankedSoloDuoQueueId,
            queueFamily: QueueCatalog.QueueFamilyRankedSoloDuo,
            queueType: "RANKED_SOLO_5x5",
            matchDate: 2000,
            duration: 1800,
            kills: 10,
            deaths: 2,
            assists: 8,
            win: true);

        harness.AddParticipant(
            summoner,
            queueId: 450,
            queueFamily: QueueCatalog.QueueFamilyAram,
            queueType: "ARAM",
            matchDate: 3000,
            duration: 1200,
            kills: 20,
            deaths: 1,
            assists: 10,
            win: true);

        await harness.Db.SaveChangesAsync();

        var result = await harness.Service.GetSummonerOverviewAsync(summoner.Id, 20, CancellationToken.None);

        result.TotalMatches.Should().Be(1);
        result.Wins.Should().Be(1);
        result.Losses.Should().Be(0);
        result.RecentPerformance.Should().ContainSingle();
        result.AvgKills.Should().Be(10);
    }

    [Fact]
    public async Task GetRecentMatchesAsync_NormalizesInputsAndAppliesQueueIdFilter()
    {
        await using var harness = await SummonerStatsHarness.CreateAsync();
        var summoner = harness.CreateSummoner();

        harness.AddParticipant(
            summoner,
            queueId: QueueCatalog.RankedSoloDuoQueueId,
            queueFamily: QueueCatalog.QueueFamilyRankedSoloDuo,
            queueType: "RANKED_SOLO_5x5",
            matchDate: 1000,
            duration: 1800,
            kills: 5,
            deaths: 3,
            assists: 6,
            win: true);

        harness.AddParticipant(
            summoner,
            queueId: 0,
            queueFamily: QueueCatalog.QueueFamilyCustom,
            queueType: QueueCatalog.RankedSoloDuoQueueId.ToString(),
            matchDate: 2000,
            duration: 1600,
            kills: 7,
            deaths: 4,
            assists: 9,
            win: false);

        harness.AddParticipant(
            summoner,
            queueId: 450,
            queueFamily: QueueCatalog.QueueFamilyAram,
            queueType: "ARAM",
            matchDate: 3000,
            duration: 1200,
            kills: 20,
            deaths: 2,
            assists: 14,
            win: true);

        await harness.Db.SaveChangesAsync();

        var result = await harness.Service.GetRecentMatchesAsync(
            summoner.Id,
            page: 0,
            pageSize: 999,
            queueFamily: "not-valid",
            queueIds: [QueueCatalog.RankedSoloDuoQueueId],
            CancellationToken.None);

        result.Page.Should().Be(1);
        result.PageSize.Should().Be(20);
        result.TotalCount.Should().Be(2);
        result.Items.Should().OnlyContain(x => x.QueueId == 420 || x.QueueType == "420");
        result.Items.Should().OnlyContain(x => x.Items.Count == 7);
    }

    [Fact]
    public async Task GetRoleBreakdownAsync_NormalizesRoleText()
    {
        await using var harness = await SummonerStatsHarness.CreateAsync();
        var summoner = harness.CreateSummoner();

        harness.AddParticipant(
            summoner,
            queueId: QueueCatalog.RankedSoloDuoQueueId,
            queueFamily: QueueCatalog.QueueFamilyRankedSoloDuo,
            queueType: "RANKED_SOLO_5x5",
            matchDate: 1000,
            duration: 1800,
            kills: 2,
            deaths: 5,
            assists: 9,
            teamPosition: "top",
            win: false);

        harness.AddParticipant(
            summoner,
            queueId: QueueCatalog.RankedSoloDuoQueueId,
            queueFamily: QueueCatalog.QueueFamilyRankedSoloDuo,
            queueType: "RANKED_SOLO_5x5",
            matchDate: 2000,
            duration: 1800,
            kills: 6,
            deaths: 3,
            assists: 11,
            teamPosition: null,
            win: true);

        await harness.Db.SaveChangesAsync();

        var result = await harness.Service.GetRoleBreakdownAsync(summoner.Id, CancellationToken.None);

        result.Select(x => x.Role).Should().Contain(["TOP", "UNKNOWN"]);
    }

    [Fact]
    public async Task GetMatchDetailAsync_ReturnsNullWhenMatchDoesNotExist()
    {
        await using var harness = await SummonerStatsHarness.CreateAsync();

        var result = await harness.Service.GetMatchDetailAsync("NA1_404", CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetMatchDetailAsync_MapsOrderedItemsAndStructuredRunes()
    {
        await using var harness = await SummonerStatsHarness.CreateAsync();
        var summoner = harness.CreateSummoner();
        var patch = harness.EnsurePatch("14.2");

        harness.Db.ItemVersions.AddRange(
            new ItemVersion { ItemId = 1001, PatchVersion = patch.Version, Name = "Boots", Description = "Boots" },
            new ItemVersion { ItemId = 3006, PatchVersion = patch.Version, Name = "Greaves", Description = "Greaves" });

        harness.Db.RuneVersions.AddRange(
            new RuneVersion
            {
                RuneId = 8005,
                PatchVersion = patch.Version,
                Name = "Press the Attack",
                RunePathId = 8000,
                Slot = 0
            },
            new RuneVersion
            {
                RuneId = 9111,
                PatchVersion = patch.Version,
                Name = "Triumph",
                RunePathId = 8000,
                Slot = 1
            },
            new RuneVersion
            {
                RuneId = 8473,
                PatchVersion = patch.Version,
                Name = "Bone Plating",
                RunePathId = 8400,
                Slot = 1
            },
            new RuneVersion
            {
                RuneId = 5008,
                PatchVersion = patch.Version,
                Name = "Adaptive Force",
                RunePathId = 5000,
                Slot = 0
            });

        var participant = harness.AddParticipant(
            summoner,
            queueId: QueueCatalog.RankedSoloDuoQueueId,
            queueFamily: QueueCatalog.QueueFamilyRankedSoloDuo,
            queueType: "RANKED_SOLO_5x5",
            matchDate: 4000,
            duration: 1800,
            kills: 8,
            deaths: 2,
            assists: 7,
            teamPosition: "TOP",
            win: true,
            patch: patch.Version);

        harness.Db.MatchParticipantItems.AddRange(
            new MatchParticipantItem
            {
                MatchParticipantId = participant.Id,
                SlotIndex = 3,
                ItemId = 3006,
                PatchVersion = patch.Version
            },
            new MatchParticipantItem
            {
                MatchParticipantId = participant.Id,
                SlotIndex = 0,
                ItemId = 1001,
                PatchVersion = patch.Version
            });

        harness.Db.MatchParticipantRunes.AddRange(
            new MatchParticipantRune
            {
                MatchParticipantId = participant.Id,
                RuneId = 8005,
                PatchVersion = patch.Version,
                SelectionTree = RuneSelectionTree.Primary,
                SelectionIndex = 0,
                StyleId = 8000
            },
            new MatchParticipantRune
            {
                MatchParticipantId = participant.Id,
                RuneId = 9111,
                PatchVersion = patch.Version,
                SelectionTree = RuneSelectionTree.Primary,
                SelectionIndex = 1,
                StyleId = 8000
            },
            new MatchParticipantRune
            {
                MatchParticipantId = participant.Id,
                RuneId = 8473,
                PatchVersion = patch.Version,
                SelectionTree = RuneSelectionTree.Secondary,
                SelectionIndex = 0,
                StyleId = 8400
            },
            new MatchParticipantRune
            {
                MatchParticipantId = participant.Id,
                RuneId = 5008,
                PatchVersion = patch.Version,
                SelectionTree = RuneSelectionTree.StatShards,
                SelectionIndex = 0,
                StyleId = 0
            });

        await harness.Db.SaveChangesAsync();
        var matchId = participant.Match.MatchId!;

        var detail = await harness.Service.GetMatchDetailAsync(matchId, CancellationToken.None);

        detail.Should().NotBeNull();
        detail!.Participants.Should().ContainSingle();
        detail.Participants[0].Items.Should().Equal([1001, 3006]);
        detail.Participants[0].Runes.PrimaryStyleId.Should().Be(8000);
        detail.Participants[0].Runes.SubStyleId.Should().Be(8400);
        detail.Participants[0].Runes.PrimarySelections.Should().Equal([8005, 9111]);
        detail.Participants[0].Runes.SubSelections.Should().Equal([8473]);
        detail.Participants[0].Runes.StatShards.Should().Equal([5008]);
    }

    [Fact]
    public async Task GetRankHistoryAsync_FiltersByQueueAndOrdersOldestFirst()
    {
        await using var harness = await SummonerStatsHarness.CreateAsync();
        var summoner = harness.CreateSummoner();

        harness.Db.HistoricalRanks.AddRange(
            new HistoricalRank
            {
                Id = Guid.NewGuid(), Summoner = summoner, QueueType = "RANKED_SOLO_5x5",
                Tier = "GOLD", RankNumber = "II", LeaguePoints = 40, Wins = 50, Losses = 40,
                DateRecorded = new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc)
            },
            new HistoricalRank
            {
                Id = Guid.NewGuid(), Summoner = summoner, QueueType = "RANKED_SOLO_5x5",
                Tier = "SILVER", RankNumber = "I", LeaguePoints = 80, Wins = 30, Losses = 28,
                DateRecorded = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new HistoricalRank
            {
                Id = Guid.NewGuid(), Summoner = summoner, QueueType = "RANKED_FLEX_SR",
                Tier = "BRONZE", RankNumber = "III", LeaguePoints = 10, Wins = 5, Losses = 9,
                DateRecorded = new DateTime(2024, 1, 3, 0, 0, 0, DateTimeKind.Utc)
            });

        await harness.Db.SaveChangesAsync();

        var solo = await harness.Service.GetRankHistoryAsync(summoner.Id, "RANKED_SOLO_5x5", CancellationToken.None);
        solo.Should().HaveCount(2);
        solo.Should().OnlyContain(x => x.QueueType == "RANKED_SOLO_5x5");
        solo.Select(x => x.Tier).Should().Equal(["SILVER", "GOLD"]); // oldest first

        var all = await harness.Service.GetRankHistoryAsync(summoner.Id, null, CancellationToken.None);
        all.Should().HaveCount(3);
        all.Select(x => x.Tier).Should().Equal(["SILVER", "GOLD", "BRONZE"]); // by DateRecorded asc
    }

    [Fact]
    public async Task GetMatchDetailAsync_IncludesBansGroupedByTeamOrderedByPickTurn()
    {
        await using var harness = await SummonerStatsHarness.CreateAsync();
        var summoner = harness.CreateSummoner();

        var participant = harness.AddParticipant(
            summoner,
            queueId: QueueCatalog.RankedSoloDuoQueueId,
            queueFamily: QueueCatalog.QueueFamilyRankedSoloDuo,
            queueType: "RANKED_SOLO_5x5",
            matchDate: 5000,
            duration: 1800,
            kills: 1,
            deaths: 1,
            assists: 1);

        harness.Db.Set<MatchBan>().AddRange(
            new MatchBan { Match = participant.Match, MatchId = participant.MatchId, TeamId = 200, PickTurn = 4, ChampionId = 222 },
            new MatchBan { Match = participant.Match, MatchId = participant.MatchId, TeamId = 100, PickTurn = 3, ChampionId = 64 },
            new MatchBan { Match = participant.Match, MatchId = participant.MatchId, TeamId = 100, PickTurn = 1, ChampionId = 17 });

        await harness.Db.SaveChangesAsync();

        var detail = await harness.Service.GetMatchDetailAsync(participant.Match.MatchId!, CancellationToken.None);

        detail.Should().NotBeNull();
        detail!.Bans.Should().HaveCount(2);
        detail.Bans.Single(b => b.TeamId == 100).BannedChampionIds.Should().Equal([17, 64]); // ordered by pick turn
        detail.Bans.Single(b => b.TeamId == 200).BannedChampionIds.Should().Equal([222]);
    }

    [Fact]
    public async Task GetChampionStatsAsync_WrapsUnexpectedException()
    {
        var harness = await SummonerStatsHarness.CreateAsync();
        await harness.Db.DisposeAsync();

        Func<Task> act = async () =>
            await harness.Service.GetChampionStatsAsync(Guid.NewGuid(), 10, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<SummonerStatsComputationException>();
        exception.Which.InnerException.Should().BeOfType<ObjectDisposedException>();

        await harness.DisposeAsync();
    }

    private sealed class SummonerStatsHarness : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly ServiceProvider _services;
        private int _matchNumber = 1;
        private int _participantNumber = 1;

        private SummonerStatsHarness(
            SqliteConnection connection,
            SqliteCompatibleTranscendenceContext db,
            ServiceProvider services,
            SummonerStatsService service)
        {
            _connection = connection;
            Db = db;
            _services = services;
            Service = service;
        }

        public SqliteCompatibleTranscendenceContext Db { get; }
        public SummonerStatsService Service { get; }

        public static async Task<SummonerStatsHarness> CreateAsync()
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

            var service = new SummonerStatsService(
                db,
                services.GetRequiredService<HybridCache>(),
                services.GetRequiredService<ILogger<SummonerStatsService>>());

            return new SummonerStatsHarness(connection, db, services, service);
        }

        public Summoner CreateSummoner()
        {
            var summoner = new Summoner
            {
                Id = Guid.NewGuid(),
                PlatformRegion = "NA1",
                Region = "americas",
                Puuid = $"puuid-{Guid.NewGuid():N}",
                GameName = "TestPlayer",
                TagLine = "NA1",
                GameNameNormalized = "testplayer",
                TagLineNormalized = "na1",
                SummonerLevel = 100,
                UpdatedAt = DateTime.UtcNow
            };
            Db.Summoners.Add(summoner);
            return summoner;
        }

        public Patch EnsurePatch(string version)
        {
            var existing = Db.Patches.Local.FirstOrDefault(x => x.Version == version);
            if (existing != null)
                return existing;

            existing = Db.Patches.FirstOrDefault(x => x.Version == version);
            if (existing != null)
                return existing;

            var patch = new Patch
            {
                Version = version,
                ReleaseDate = DateTime.UtcNow,
                IsActive = true
            };
            Db.Patches.Add(patch);
            return patch;
        }

        public MatchParticipant AddParticipant(
            Summoner summoner,
            int queueId,
            string queueFamily,
            string queueType,
            long matchDate,
            int duration,
            int kills,
            int deaths,
            int assists,
            string? teamPosition = "TOP",
            bool win = true,
            string patch = "14.2")
        {
            EnsurePatch(patch);
            var match = new Match
            {
                Id = Guid.NewGuid(),
                MatchId = $"NA1_{_matchNumber++}",
                MatchDate = matchDate,
                Duration = duration,
                Patch = patch,
                QueueId = queueId,
                QueueFamily = queueFamily,
                QueueType = queueType,
                Status = FetchStatus.Success
            };

            var participant = new MatchParticipant
            {
                Id = Guid.NewGuid(),
                Match = match,
                MatchId = match.Id,
                Summoner = summoner,
                SummonerId = summoner.Id,
                Puuid = summoner.Puuid,
                ParticipantId = _participantNumber++,
                TeamId = 100,
                ChampionId = 266,
                TeamPosition = teamPosition,
                Win = win,
                Kills = kills,
                Deaths = deaths,
                Assists = assists,
                ChampLevel = 18,
                GoldEarned = 12000,
                TotalDamageDealtToChampions = 18000,
                VisionScore = 25,
                TotalMinionsKilled = 180,
                NeutralMinionsKilled = 12,
                SummonerSpell1Id = 4,
                SummonerSpell2Id = 12
            };

            Db.MatchParticipants.Add(participant);
            return participant;
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _services.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

}
