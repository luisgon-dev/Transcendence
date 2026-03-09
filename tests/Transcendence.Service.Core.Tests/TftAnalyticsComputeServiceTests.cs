using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Transcendence.Data;
using Transcendence.Data.Models.Tft.Account;
using Transcendence.Data.Models.Tft.Match;
using Transcendence.Data.Models.Tft.Static;
using Transcendence.Service.Core.Services.Tft.Configuration;
using Transcendence.Service.Core.Services.Tft.Implementations;
using Transcendence.Service.Core.Tests.Support;

namespace Transcendence.Service.Core.Tests;

public class TftAnalyticsComputeServiceTests
{
    [Fact]
    public async Task ComputeCompListAsync_DefaultRankTier_UsesEmeraldPlusAndExcludesLowerRanks()
    {
        await using var harness = await Harness.CreateAsync();
        harness.SeedParticipant("NA1", "Alpha", "NA1", "CHALLENGER", 1, "TFT13_Annie", "Annie", "Arcana", "Tiniest Titan");
        harness.SeedParticipant("NA1", "Bravo", "NA1", "GOLD", 2, "TFT13_DrMundo", "Dr. Mundo", "Bruiser", "Silver Spoon");
        harness.SeedParticipant("KR", "Charlie", "KR1", "MASTER", 3, "TFT13_Zyra", "Zyra", "Invoker", "Cluttered Mind");
        await harness.Db.SaveChangesAsync();

        var result = await harness.Service.ComputeCompListAsync(null, null, CancellationToken.None);

        result.Should().HaveCount(2);
        result.Should().OnlyContain(x => x.RankTier == "EMERALD_PLUS" && x.Region == "ALL");
        result.Select(x => x.Name).Should().NotContain("Bruiser / Dr. Mundo");
    }

    [Fact]
    public async Task ComputeCompListAsync_AllRankTier_IsCaseInsensitiveAndRespectsRegionFilter()
    {
        await using var harness = await Harness.CreateAsync();
        harness.SeedParticipant("NA1", "Alpha", "NA1", "CHALLENGER", 1, "TFT13_Annie", "Annie", "Arcana", "Tiniest Titan");
        harness.SeedParticipant("NA1", "Bravo", "NA1", "GOLD", 2, "TFT13_DrMundo", "Dr. Mundo", "Bruiser", "Silver Spoon");
        harness.SeedParticipant("KR", "Charlie", "KR1", "MASTER", 3, "TFT13_Zyra", "Zyra", "Invoker", "Cluttered Mind");
        await harness.Db.SaveChangesAsync();

        var result = await harness.Service.ComputeCompListAsync("ALL", "na1", CancellationToken.None);

        result.Should().HaveCount(2);
        result.Should().OnlyContain(x => x.RankTier == "ALL" && x.Region == "NA1");
        result.Select(x => x.Name).Should().BeEquivalentTo(["Arcana / Annie", "Bruiser / Dr. Mundo"]);
    }

    [Fact]
    public async Task ComputeCompDetailAsync_ReturnsSummaryMatchingListSlug()
    {
        await using var harness = await Harness.CreateAsync();
        harness.SeedParticipant("NA1", "Alpha", "NA1", "CHALLENGER", 1, "TFT13_Annie", "Annie", "Arcana", "Tiniest Titan");
        await harness.Db.SaveChangesAsync();

        var list = await harness.Service.ComputeCompListAsync(null, null, CancellationToken.None);
        var detail = await harness.Service.ComputeCompDetailAsync(list[0].CompSlug, null, null, CancellationToken.None);

        detail.Should().NotBeNull();
        detail!.Summary.CompSlug.Should().Be(list[0].CompSlug);
        detail.CoreItems.Select(x => x.ApiName).Should().Contain("TFT13_Item_BlueBuff");
        detail.CoreAugments.Select(x => x.ApiName).Should().Contain("TFT13_Augment_PandorasBench");
    }

    [Fact]
    public async Task ComputeCompListAsync_UsesConfiguredRecentMatchWindow()
    {
        await using var harness = await Harness.CreateAsync(new TftAnalyticsComputeOptions
        {
            MaxRecentMatchesPerSlice = 1
        });
        harness.SeedParticipant("NA1", "Alpha", "NA1", "CHALLENGER", 1, "TFT13_Annie", "Annie", "Arcana", "Tiniest Titan");
        harness.SeedParticipant("NA1", "Bravo", "NA1", "CHALLENGER", 1, "TFT13_Zed", "Zed", "Slayer", "Silver Spoon");
        await harness.Db.SaveChangesAsync();

        var result = await harness.Service.ComputeCompListAsync(null, null, CancellationToken.None);

        result.Should().ContainSingle();
        result[0].Name.Should().Be("Slayer / Zed");
    }

    private sealed class Harness : IAsyncDisposable
    {
        private int _matchNumber = 1;

        private Harness(SqliteConnection connection, SqliteCompatibleTranscendenceContext db, TftAnalyticsComputeService service)
        {
            Connection = connection;
            Db = db;
            Service = service;
        }

        public SqliteConnection Connection { get; }
        public SqliteCompatibleTranscendenceContext Db { get; }
        public TftAnalyticsComputeService Service { get; }

        public static async Task<Harness> CreateAsync(TftAnalyticsComputeOptions? computeOptions = null)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<TranscendenceContext>()
                .UseSqlite(connection)
                .Options;
            var db = new SqliteCompatibleTranscendenceContext(options);
            await db.Database.EnsureCreatedAsync();

            db.TftSets.Add(new TftSet { Number = 13, Name = "Cyber City", IsActive = true });
            db.TftItemVersions.Add(new TftItemVersion
            {
                Id = Guid.NewGuid(),
                SetNumber = 13,
                ApiName = "TFT13_Item_BlueBuff",
                Name = "Blue Buff"
            });
            db.TftAugmentVersions.Add(new TftAugmentVersion
            {
                Id = Guid.NewGuid(),
                SetNumber = 13,
                ApiName = "TFT13_Augment_PandorasBench",
                Name = "Pandora's Bench"
            });
            await db.SaveChangesAsync();

            return new Harness(
                connection,
                db,
                new TftAnalyticsComputeService(
                    db,
                    Options.Create(computeOptions ?? new TftAnalyticsComputeOptions())));
        }

        public void SeedParticipant(
            string platformRegion,
            string gameName,
            string tagLine,
            string tier,
            int placement,
            string characterId,
            string unitName,
            string traitName,
            string augment)
        {
            var summoner = new TftSummoner
            {
                Id = Guid.NewGuid(),
                Puuid = $"{platformRegion}-{gameName}",
                GameName = gameName,
                TagLine = tagLine,
                GameNameNormalized = gameName.ToUpperInvariant(),
                TagLineNormalized = tagLine.ToUpperInvariant(),
                PlatformRegion = platformRegion,
                Region = platformRegion
            };
            summoner.Ranks.Add(new TftRank
            {
                Id = Guid.NewGuid(),
                SummonerId = summoner.Id,
                QueueType = "RANKED_TFT",
                Tier = tier,
                RankNumber = "I"
            });

            var match = new TftMatch
            {
                Id = Guid.NewGuid(),
                MatchId = $"TFT_{_matchNumber++}",
                MatchDate = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                DurationSeconds = 1800,
                Patch = "14.5",
                SetNumber = 13,
                SetCoreName = "Cyber City",
                PlatformRegion = platformRegion,
                Status = TftFetchStatus.Success,
                FetchedAtUtc = DateTime.UtcNow
            };

            var participant = new TftMatchParticipant
            {
                Id = Guid.NewGuid(),
                Match = match,
                MatchId = match.Id,
                Summoner = summoner,
                SummonerId = summoner.Id,
                Puuid = summoner.Puuid!,
                RiotIdGameName = gameName,
                RiotIdTagLine = tagLine,
                Placement = placement,
                Level = 9,
                LastRound = 6,
                PlayersEliminated = 4,
                GoldLeft = 10,
                TotalDamageToPlayers = 100,
                TimeEliminatedSeconds = 1200,
                Augments = [augment]
            };

            participant.Units.Add(new TftMatchParticipantUnit
            {
                Id = Guid.NewGuid(),
                Participant = participant,
                ParticipantId = participant.Id,
                CharacterId = characterId,
                Name = unitName,
                Rarity = 2,
                Tier = 2,
                Items = [1, 2, 3]
            });
            participant.Traits.Add(new TftMatchParticipantTrait
            {
                Id = Guid.NewGuid(),
                Participant = participant,
                ParticipantId = participant.Id,
                Name = traitName,
                NumUnits = 4,
                TierCurrent = 2,
                Style = 4
            });

            Db.TftSummoners.Add(summoner);
            Db.TftMatches.Add(match);
            Db.TftMatchParticipants.Add(participant);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }
}
