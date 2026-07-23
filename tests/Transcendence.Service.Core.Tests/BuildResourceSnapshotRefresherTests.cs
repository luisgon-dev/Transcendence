using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Account;
using Transcendence.Data.Models.LoL.Analytics;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Data.Models.LoL.Static;
using Transcendence.Service.Core.Services.Analytics.Implementations;
using Transcendence.Service.Core.Services.Analytics.Models;
using Transcendence.Service.Core.Tests.Support;
using MatchEntity = Transcendence.Data.Models.LoL.Match.Match;

namespace Transcendence.Service.Core.Tests;

public sealed class BuildResourceSnapshotRefresherTests
{
    [Fact]
    public async Task RefreshAsync_PromotesCompleteGenerationAndDeduplicatesParticipantResources()
    {
        await using var harness = await Harness.CreateAsync();
        harness.AddMatch("MATCH_ONE", win: true);
        await harness.Db.SaveChangesAsync();

        var result = await harness.Refresher.RefreshAsync(
            Harness.PatchVersion, forceFullRebuild: true, CancellationToken.None);

        result.FullRebuild.Should().BeTrue();
        result.ProcessedMatchCount.Should().Be(1);
        var snapshot = await harness.Db.BuildResourceSnapshots.AsNoTracking().SingleAsync();
        snapshot.Id.Should().Be(result.SnapshotId);
        snapshot.Status.Should().Be(BuildResourceSnapshotStatus.Ready);
        snapshot.IsActive.Should().BeTrue();
        snapshot.ProcessedMatchCount.Should().Be(1);
        snapshot.CompletedAtUtc.Should().NotBeNull();

        var item = await harness.Db.BuildResourceStats.AsNoTracking()
            .SingleAsync(row => row.SnapshotId == snapshot.Id && row.ResourceType == "item");
        item.Games.Should().Be(1, "duplicate final inventory slots count as one participant use");
        item.Wins.Should().Be(1);

        var rune = await harness.Db.BuildResourceStats.AsNoTracking()
            .SingleAsync(row => row.SnapshotId == snapshot.Id && row.ResourceType == "rune");
        rune.Games.Should().Be(1);
        (await harness.Db.BuildResourcePopulationStats.AsNoTracking()
                .SingleAsync(row => row.SnapshotId == snapshot.Id))
            .Games.Should().Be(1);
        (await harness.Db.BuildResourceProcessedMatches.CountAsync(row => row.SnapshotId == snapshot.Id))
            .Should().Be(1);
    }

    [Fact]
    public async Task RefreshAsync_IncrementallyPromotesNewMatchesAndNoopsWhenCurrent()
    {
        await using var harness = await Harness.CreateAsync();
        harness.AddMatch("MATCH_ONE", win: true);
        await harness.Db.SaveChangesAsync();
        var first = await harness.Refresher.RefreshAsync(
            Harness.PatchVersion, forceFullRebuild: true, CancellationToken.None);

        harness.AddMatch("MATCH_TWO", win: false);
        await harness.Db.SaveChangesAsync();
        var second = await harness.Refresher.RefreshAsync(
            Harness.PatchVersion, forceFullRebuild: false, CancellationToken.None);

        second.SnapshotId.Should().NotBe(first.SnapshotId);
        second.FullRebuild.Should().BeFalse();
        second.ProcessedMatchCount.Should().Be(1);
        var active = await harness.Db.BuildResourceSnapshots.AsNoTracking()
            .SingleAsync(snapshot => snapshot.IsActive);
        active.Id.Should().Be(second.SnapshotId);
        active.Status.Should().Be(BuildResourceSnapshotStatus.Ready);
        active.ProcessedMatchCount.Should().Be(2);
        (await harness.Db.BuildResourceSnapshots.AsNoTracking()
                .SingleAsync(snapshot => snapshot.Id == first.SnapshotId))
            .Status.Should().Be(BuildResourceSnapshotStatus.Retired);

        var item = await harness.Db.BuildResourceStats.AsNoTracking()
            .SingleAsync(row => row.SnapshotId == active.Id && row.ResourceType == "item");
        item.Games.Should().Be(2);
        item.Wins.Should().Be(1);
        (await harness.Db.BuildResourcePopulationStats.AsNoTracking()
                .SingleAsync(row => row.SnapshotId == active.Id))
            .Games.Should().Be(2);

        var snapshotCount = await harness.Db.BuildResourceSnapshots.CountAsync();
        var noOp = await harness.Refresher.RefreshAsync(
            Harness.PatchVersion, forceFullRebuild: false, CancellationToken.None);

        noOp.SnapshotId.Should().Be(active.Id);
        noOp.ProcessedMatchCount.Should().Be(0);
        (await harness.Db.BuildResourceSnapshots.CountAsync()).Should().Be(snapshotCount);
        (await harness.Db.BuildResourceSnapshots.CountAsync(snapshot =>
            snapshot.Status == BuildResourceSnapshotStatus.Building)).Should().Be(0);
    }

    private sealed class Harness : IAsyncDisposable
    {
        public const string PatchVersion = "16.14";
        private readonly SqliteConnection connection;
        private int participantNumber;

        private Harness(
            SqliteConnection connection,
            SqliteCompatibleTranscendenceContext db,
            BuildResourceSnapshotRefresher refresher)
        {
            this.connection = connection;
            Db = db;
            Refresher = refresher;
        }

        public SqliteCompatibleTranscendenceContext Db { get; }
        public BuildResourceSnapshotRefresher Refresher { get; }

        public static async Task<Harness> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<TranscendenceContext>()
                .UseSqlite(connection)
                .Options;
            var db = new SqliteCompatibleTranscendenceContext(options);
            await db.Database.EnsureCreatedAsync();
            db.Patches.Add(new Patch
            {
                Version = PatchVersion,
                ReleaseDate = DateTime.UtcNow.AddDays(-2),
                DetectedAt = DateTime.UtcNow.AddDays(-2),
                IsActive = true
            });
            db.ItemVersions.Add(new ItemVersion
            {
                ItemId = 3078,
                PatchVersion = PatchVersion,
                Name = "Trinity Force",
                BuildsFrom = [3057],
                BuildsInto = [],
                Tags = ["Damage"],
                InStore = true,
                PriceTotal = 3333
            });
            db.RuneVersions.Add(new RuneVersion
            {
                RuneId = 8005,
                PatchVersion = PatchVersion,
                Name = "Press the Attack",
                RunePathId = 8000,
                RunePathName = "Precision",
                Slot = 0
            });
            await db.SaveChangesAsync();

            var refresher = new BuildResourceSnapshotRefresher(
                db,
                Options.Create(new BuildResourceSnapshotOptions
                {
                    MatchBatchSize = 50,
                    CommandTimeoutSeconds = 30
                }),
                NullLogger<BuildResourceSnapshotRefresher>.Instance);
            return new Harness(connection, db, refresher);
        }

        public void AddMatch(string matchId, bool win)
        {
            participantNumber++;
            var match = new MatchEntity
            {
                Id = Guid.NewGuid(),
                MatchId = matchId,
                MatchDate = DateTimeOffset.UtcNow.AddMinutes(participantNumber).ToUnixTimeMilliseconds(),
                Patch = PatchVersion,
                QueueId = 420,
                QueueFamily = "RANKED_SOLO_DUO",
                QueueType = "420",
                PlatformRegion = "NA1",
                Status = FetchStatus.Success,
                FetchedAt = DateTime.UtcNow.AddMinutes(participantNumber)
            };
            var summoner = new Summoner
            {
                Id = Guid.NewGuid(),
                Puuid = $"puuid-{participantNumber}",
                PlatformRegion = "NA1",
                Region = "AMERICAS"
            };
            var participant = new MatchParticipant
            {
                Id = Guid.NewGuid(),
                MatchId = match.Id,
                Match = match,
                SummonerId = summoner.Id,
                Summoner = summoner,
                ParticipantId = 1,
                ChampionId = 266,
                TeamPosition = "TOP",
                Win = win
            };
            participant.Items.Add(new MatchParticipantItem
            {
                MatchParticipantId = participant.Id,
                SlotIndex = 0,
                ItemId = 3078,
                PatchVersion = PatchVersion
            });
            participant.Items.Add(new MatchParticipantItem
            {
                MatchParticipantId = participant.Id,
                SlotIndex = 1,
                ItemId = 3078,
                PatchVersion = PatchVersion
            });
            participant.Runes.Add(new MatchParticipantRune
            {
                MatchParticipantId = participant.Id,
                RuneId = 8005,
                PatchVersion = PatchVersion,
                SelectionTree = RuneSelectionTree.Primary,
                SelectionIndex = 0,
                StyleId = 8000
            });

            Db.Matches.Add(match);
            Db.Summoners.Add(summoner);
            Db.MatchParticipants.Add(participant);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
