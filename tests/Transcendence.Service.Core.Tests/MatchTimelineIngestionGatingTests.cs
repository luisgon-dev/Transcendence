using System.Net;
using System.Text.Json;
using FluentAssertions;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Account;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Data.Models.LoL.Static;
using Transcendence.Service.Core.Services.Analytics.Models;
using Transcendence.Service.Core.Services.Jobs;
using Transcendence.Service.Core.Services.Jobs.Configuration;
using Transcendence.Service.Core.Services.RiotApi;
using Transcendence.Service.Core.Tests.Support;
using DataMatch = Transcendence.Data.Models.LoL.Match.Match;

namespace Transcendence.Service.Core.Tests;

/// <summary>
/// The whole Build Lab change set must be inert until <c>Analytics:BuildLab:Enabled</c> is on: the box
/// is disk-constrained and the Riot key is a low-rate personal one. These tests pin that a flags-OFF
/// ingest writes nothing to the three Build Lab child tables, keeps the coarse frame cadence, and does
/// not consider an already-ingested v1 match stale (which would re-fetch every timeline for nothing).
/// The timeline itself comes from the loopback <see cref="RiotApiServiceHttpTests.RiotApiStub"/>, so
/// the real Camille request/parse path runs without touching the network.
/// </summary>
public class MatchTimelineIngestionGatingTests
{
    private const int LegendaryItemId = 3153;
    private const string Patch = "15.2";

    [Theory]
    [InlineData(false, MatchTimelineIngestionJob.BaselineTimelineSchemaVersion)]
    [InlineData(true, MatchTimelineIngestionJob.CurrentTimelineSchemaVersion)]
    public void TargetSchemaVersion_TracksTheBuildLabFlag(bool buildLabEnabled, int expected)
    {
        MatchTimelineIngestionJob.TargetSchemaVersion(buildLabEnabled).Should().Be(expected);
    }

    [Fact]
    public async Task Ingest_WithBuildLabDisabled_WritesNoBuildLabRowsAndStampsBaselineSchema()
    {
        await using var harness = await BuildLabHarness.CreateAsync();
        var match = await SeedMatchAsync(harness.Db);
        using var stub = new RiotApiServiceHttpTests.RiotApiStub { Response = (HttpStatusCode.OK, BuildTimelineJson(match)) };

        await BuildJob(harness.Db, stub, buildLabEnabled: false)
            .IngestMatchTimelineAsync(match.MatchId!, CancellationToken.None);

        await using var assertions = harness.NewContext();
        // The three tables the feature introduces: raw payloads, lossless item lifecycle, rank context.
        (await assertions.MatchTimelineEventPayloads.CountAsync()).Should().Be(0);
        (await assertions.MatchParticipantItemEvents.CountAsync()).Should().Be(0);
        (await assertions.MatchParticipantRankContexts.CountAsync()).Should().Be(0);

        // v1 capture still happens — the flag gates the extras, not the job.
        (await assertions.MatchParticipantItemPurchases.CountAsync()).Should().BeGreaterThan(0);
        (await assertions.MatchParticipantSkillOrders.CountAsync()).Should().BeGreaterThan(0);

        var state = await assertions.MatchTimelineFetchStates.SingleAsync();
        state.Status.Should().Be(MatchTimelineFetchStatus.Success);
        state.SchemaVersion.Should().Be(MatchTimelineIngestionJob.BaselineTimelineSchemaVersion);

        // Frames stay on the configured 2-minute cadence (plus the analytics anchor), not one per minute.
        var marks = await assertions.MatchParticipantTimelineSnapshots
            .Select(snapshot => snapshot.MinuteMark)
            .Distinct()
            .OrderBy(mark => mark)
            .ToListAsync();
        marks.Should().Equal(2, 4, 6, 15);
    }

    [Fact]
    public async Task Ingest_WithBuildLabEnabled_WritesEveryBuildLabTableAndStampsV2()
    {
        await using var harness = await BuildLabHarness.CreateAsync();
        var match = await SeedMatchAsync(harness.Db);
        using var stub = new RiotApiServiceHttpTests.RiotApiStub { Response = (HttpStatusCode.OK, BuildTimelineJson(match)) };

        await BuildJob(harness.Db, stub, buildLabEnabled: true)
            .IngestMatchTimelineAsync(match.MatchId!, CancellationToken.None);

        await using var assertions = harness.NewContext();
        (await assertions.MatchTimelineEventPayloads.CountAsync()).Should().BeGreaterThan(0);
        (await assertions.MatchParticipantItemEvents.CountAsync()).Should().BeGreaterThan(0);
        (await assertions.MatchParticipantRankContexts.CountAsync()).Should().Be(2);

        var state = await assertions.MatchTimelineFetchStates.SingleAsync();
        state.SchemaVersion.Should().Be(MatchTimelineIngestionJob.CurrentTimelineSchemaVersion);

        // One-minute cadence, so the Build Lab feature frames are leak-free.
        var marks = await assertions.MatchParticipantTimelineSnapshots
            .Select(snapshot => snapshot.MinuteMark)
            .Distinct()
            .OrderBy(mark => mark)
            .ToListAsync();
        marks.Should().Equal(1, 2, 3, 4, 5, 6, 15);
    }

    [Fact]
    public async Task Ingest_WithBuildLabDisabled_TreatsAnExistingV1RowAsFreshAndSkipsTheRiotFetch()
    {
        await using var harness = await BuildLabHarness.CreateAsync();
        var match = await SeedMatchAsync(harness.Db);
        harness.Db.MatchTimelineFetchStates.Add(new MatchTimelineFetchState
        {
            MatchId = match.Id,
            Match = match,
            Status = MatchTimelineFetchStatus.Success,
            SchemaVersion = MatchTimelineIngestionJob.BaselineTimelineSchemaVersion,
            SourcePatch = Patch,
            LastSuccessAtUtc = DateTime.UtcNow.AddDays(-1)
        });
        await harness.Db.SaveChangesAsync();

        // Any fetch would fail against this stub, and the job stamps LastAttemptAtUtc before calling
        // Riot — so an untouched state row is proof no request was made and no budget was spent.
        using var stub = new RiotApiServiceHttpTests.RiotApiStub
        {
            Response = (HttpStatusCode.InternalServerError, null)
        };

        await BuildJob(harness.Db, stub, buildLabEnabled: false)
            .IngestMatchTimelineAsync(match.MatchId!, CancellationToken.None);

        await using var assertions = harness.NewContext();
        var state = await assertions.MatchTimelineFetchStates.SingleAsync();
        state.LastAttemptAtUtc.Should().BeNull();
        state.RetryCount.Should().Be(0);
        state.Status.Should().Be(MatchTimelineFetchStatus.Success);
        state.SchemaVersion.Should().Be(MatchTimelineIngestionJob.BaselineTimelineSchemaVersion);
        (await assertions.MatchParticipantTimelineSnapshots.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Ingest_WithBuildLabEnabled_TreatsAnExistingV1RowAsStaleAndReingests()
    {
        await using var harness = await BuildLabHarness.CreateAsync();
        var match = await SeedMatchAsync(harness.Db);
        harness.Db.MatchTimelineFetchStates.Add(new MatchTimelineFetchState
        {
            MatchId = match.Id,
            Match = match,
            Status = MatchTimelineFetchStatus.Success,
            SchemaVersion = MatchTimelineIngestionJob.BaselineTimelineSchemaVersion,
            SourcePatch = Patch,
            LastSuccessAtUtc = DateTime.UtcNow.AddDays(-1)
        });
        await harness.Db.SaveChangesAsync();
        using var stub = new RiotApiServiceHttpTests.RiotApiStub { Response = (HttpStatusCode.OK, BuildTimelineJson(match)) };

        await BuildJob(harness.Db, stub, buildLabEnabled: true)
            .IngestMatchTimelineAsync(match.MatchId!, CancellationToken.None);

        await using var assertions = harness.NewContext();
        var state = await assertions.MatchTimelineFetchStates.SingleAsync();
        state.SchemaVersion.Should().Be(MatchTimelineIngestionJob.CurrentTimelineSchemaVersion);
        (await assertions.MatchParticipantRankContexts.CountAsync()).Should().Be(2);
    }

    /// <summary>
    /// The soundness half of the flag-dependent target version: a match ingested while the flag was on
    /// can still be re-ingested with it off (its state was left non-Success), and that re-ingest rewrites
    /// the snapshots at the coarse cadence. The row must therefore stop claiming v2 — otherwise the
    /// generation cohort would trust Build Lab extras that no longer match the captured frames — while
    /// the corpus already on disk is left intact for the next flag-on ingest.
    /// </summary>
    [Fact]
    public async Task Ingest_WithBuildLabDisabled_DropsAV2RowToBaselineAndLeavesTheCapturedCorpusIntact()
    {
        await using var harness = await BuildLabHarness.CreateAsync();
        var match = await SeedMatchAsync(harness.Db);
        harness.Db.MatchTimelineFetchStates.Add(new MatchTimelineFetchState
        {
            MatchId = match.Id,
            Match = match,
            // Not Success, so the staleness short-circuit does not apply and the ingest actually runs.
            Status = MatchTimelineFetchStatus.TemporaryFailure,
            RetryCount = 1,
            SchemaVersion = MatchTimelineIngestionJob.CurrentTimelineSchemaVersion,
            SourcePatch = Patch,
            LastSuccessAtUtc = DateTime.UtcNow.AddDays(-1)
        });
        await harness.Db.SaveChangesAsync();

        // The corpus a previous flag-on ingest left behind, written through its own scope so the job's
        // context does not track it (as in production, where each ingest runs in a fresh scope).
        await using (var priorIngest = harness.NewContext())
        {
            var priorMatch = await priorIngest.Matches.SingleAsync();
            priorIngest.MatchParticipantItemEvents.Add(new MatchParticipantItemEvent
            {
                MatchId = priorMatch.Id,
                Match = priorMatch,
                ParticipantId = 1,
                EventIndex = 0,
                EventType = MatchItemEventType.Purchased,
                TimestampMs = 61_000,
                ItemId = LegendaryItemId,
                IsBuildRelevant = true
            });
            priorIngest.MatchParticipantRankContexts.Add(new MatchParticipantRankContext
            {
                MatchId = priorMatch.Id,
                Match = priorMatch,
                ParticipantId = 1,
                Tier = "DIAMOND",
                Source = "STORED_SOLO_RANK"
            });
            priorIngest.MatchTimelineEventPayloads.Add(new MatchTimelineEventPayload
            {
                MatchId = priorMatch.Id,
                Match = priorMatch,
                EventIndex = 0,
                TimestampMs = 61_000,
                EventType = TimelineBuildParser.ChampionKillType,
                PayloadJson = """{"type":"CHAMPION_KILL"}"""
            });
            await priorIngest.SaveChangesAsync();
        }

        using var stub = new RiotApiServiceHttpTests.RiotApiStub { Response = (HttpStatusCode.OK, BuildTimelineJson(match)) };

        await BuildJob(harness.Db, stub, buildLabEnabled: false)
            .IngestMatchTimelineAsync(match.MatchId!, CancellationToken.None);

        await using var assertions = harness.NewContext();
        var state = await assertions.MatchTimelineFetchStates.SingleAsync();
        state.Status.Should().Be(MatchTimelineFetchStatus.Success);
        state.SchemaVersion.Should().Be(MatchTimelineIngestionJob.BaselineTimelineSchemaVersion);

        // The re-ingest really happened, at the coarse cadence.
        var marks = await assertions.MatchParticipantTimelineSnapshots
            .Select(snapshot => snapshot.MinuteMark)
            .Distinct()
            .OrderBy(mark => mark)
            .ToListAsync();
        marks.Should().Equal(2, 4, 6, 15);

        // Untouched, not rewritten: the three Build Lab tables keep exactly the rows the flag-on ingest left.
        (await assertions.MatchParticipantItemEvents.ToListAsync())
            .Should().ContainSingle().Which.ItemId.Should().Be(LegendaryItemId);
        (await assertions.MatchParticipantRankContexts.ToListAsync())
            .Should().ContainSingle().Which.Tier.Should().Be("DIAMOND");
        (await assertions.MatchTimelineEventPayloads.ToListAsync())
            .Should().ContainSingle().Which.PayloadJson.Should().Be("""{"type":"CHAMPION_KILL"}""");
    }

    private static MatchTimelineIngestionJob BuildJob(
        TranscendenceContext db,
        RiotApiServiceHttpTests.RiotApiStub stub,
        bool buildLabEnabled)
    {
        var rateGate = new Mock<IRiotRateGate>();
        rateGate.Setup(gate => gate.AcquireAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        return new MatchTimelineIngestionJob(
            db,
            stub.BuildContext(),
            Mock.Of<IBackgroundJobClient>(),
            rateGate.Object,
            Options.Create(new TimelineIngestionOptions { MinuteMark = 15, FrameIntervalMinutes = 2 }),
            Options.Create(new BuildLabModelingOptions { Enabled = buildLabEnabled }),
            NullLogger<MatchTimelineIngestionJob>.Instance);
    }

    private static async Task<DataMatch> SeedMatchAsync(TranscendenceContext db)
    {
        db.Patches.Add(new Patch
        {
            Version = Patch,
            ReleaseDate = DateTime.UtcNow.AddDays(-2),
            DetectedAt = DateTime.UtcNow.AddDays(-2),
            IsActive = true
        });
        db.ItemVersions.Add(new ItemVersion
        {
            ItemId = LegendaryItemId,
            PatchVersion = Patch,
            Name = "Blade of the Ruined King",
            Tags = ["Damage"],
            BuildsFrom = [1042, 1053],
            BuildsInto = [],
            InStore = true,
            PriceTotal = 3200
        });

        var match = new DataMatch
        {
            Id = Guid.NewGuid(),
            MatchId = "NA1_1",
            MatchDate = DateTimeOffset.UtcNow.AddHours(-3).ToUnixTimeMilliseconds(),
            Duration = 390,
            Patch = Patch,
            QueueId = 420,
            QueueFamily = "RANKED_SOLO_DUO",
            QueueType = "420",
            Status = FetchStatus.Success,
            PlatformRegion = "NA1",
            FetchedAt = DateTime.UtcNow
        };
        db.Matches.Add(match);

        foreach (var participantId in new[] { 1, 2 })
        {
            var summoner = new Summoner
            {
                Id = Guid.NewGuid(),
                PlatformRegion = "NA1",
                Region = "americas",
                GameName = $"Player{participantId}",
                TagLine = "NA1",
                Puuid = $"PUUID-{participantId}",
                SummonerName = $"Player{participantId}",
                RiotSummonerId = Guid.NewGuid().ToString("N")
            };
            db.Summoners.Add(summoner);
            db.Ranks.Add(new Rank
            {
                Id = Guid.NewGuid(),
                SummonerId = summoner.Id,
                QueueType = "RANKED_SOLO_5x5",
                Tier = "EMERALD",
                RankNumber = "II",
                LeaguePoints = 42,
                UpdatedAt = DateTime.UtcNow.AddHours(-4)
            });
            db.MatchParticipants.Add(new MatchParticipant
            {
                Id = Guid.NewGuid(),
                MatchId = match.Id,
                Match = match,
                SummonerId = summoner.Id,
                Summoner = summoner,
                Puuid = summoner.Puuid,
                ParticipantId = participantId,
                TeamId = participantId == 1 ? 100 : 200,
                ChampionId = 266,
                TeamPosition = "TOP",
                Win = participantId == 1
            });
        }

        await db.SaveChangesAsync();
        return match;
    }

    /// <summary>
    /// A minimal Match-V5 timeline: seven one-minute frames for two participants, one legendary
    /// purchase, skill level-ups, and a champion kill (the payload table's only other consumer).
    /// </summary>
    private static string BuildTimelineJson(DataMatch match)
    {
        var frames = new List<object>();
        for (var minute = 0; minute <= 6; minute++)
        {
            var timestamp = minute * 60_000;
            var events = new List<object>();
            if (minute is >= 1 and <= 3)
            {
                events.Add(new
                {
                    type = "SKILL_LEVEL_UP",
                    participantId = 1,
                    skillSlot = minute,
                    levelUpType = "NORMAL",
                    timestamp
                });
            }

            if (minute == 4)
            {
                events.Add(new { type = "ITEM_PURCHASED", participantId = 1, itemId = LegendaryItemId, timestamp });
                events.Add(new { type = "CHAMPION_KILL", killerId = 1, victimId = 2, timestamp });
            }

            frames.Add(new
            {
                timestamp,
                events,
                participantFrames = new Dictionary<string, object>
                {
                    ["1"] = ParticipantFrame(1, minute),
                    ["2"] = ParticipantFrame(2, minute)
                }
            });
        }

        return JsonSerializer.Serialize(new
        {
            metadata = new { dataVersion = "2", matchId = match.MatchId, participants = new[] { "PUUID-1", "PUUID-2" } },
            info = new
            {
                frameInterval = 60_000,
                participants = new[]
                {
                    new { participantId = 1, puuid = "PUUID-1" },
                    new { participantId = 2, puuid = "PUUID-2" }
                },
                frames
            }
        });
    }

    private static object ParticipantFrame(int participantId, int minute) => new
    {
        participantId,
        currentGold = 100 * minute,
        totalGold = 500 + (300 * minute),
        level = 1 + minute,
        xp = 400 * minute,
        minionsKilled = 6 * minute,
        jungleMinionsKilled = minute,
        goldPerSecond = 0,
        timeEnemySpentControlled = 0
    };
}
