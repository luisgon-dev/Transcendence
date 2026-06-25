using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Account;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Service.Core.Services.Analytics.Implementations;
using Transcendence.Service.Core.Services.Analytics.Models;
using Transcendence.Service.Core.Tests.Support;

namespace Transcendence.Service.Core.Tests;

/// <summary>
/// Correctness gate for the precomputed matchups: for a seeded lane-pair + timeline dataset, the stats-backed
/// read (rolling the per-tier <c>ChampionMatchupStat</c> atoms up to a rank scope) must reproduce the live
/// lane-pair self-join compute EXACTLY across rank scopes — counters, favorable, all-matchups (set + order),
/// win rates, avg gold/xp diffs, timeline coverage/sample/freshness.
/// </summary>
public class ChampionMatchupStatsEquivalenceTests
{
    private const string Patch = "15.2";
    private static readonly DateTime T0 = new(2026, 6, 24, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Matchups_StatsPath_EqualsRawCompute_AcrossRankScopes()
    {
        await using var ctx = await SeededAsync();
        await new PrecomputedAnalyticsRefresher(ctx.Db, NullLogger<PrecomputedAnalyticsRefresher>.Instance)
            .RefreshMatchupsAsync(Patch, CancellationToken.None);
        var svc = Service(ctx.Db);

        foreach (var tier in new string?[] { null, "ALL", "EMERALD_PLUS", "EMERALD", "DIAMOND" })
        {
            // region null = ALL (the only precomputed scope); both paths see the same seeded data.
            var raw = await svc.ComputeMatchupsAsync(100, "MIDDLE", tier, null, Patch, CancellationToken.None);
            var stats = await svc.ComputeMatchupsFromStatsAsync(100, "MIDDLE", tier, null, Patch, CancellationToken.None);

            stats.Should().BeEquivalentTo(raw, o => o.WithStrictOrdering(),
                $"matchups for champ 100 MIDDLE scope={tier ?? "ALL"}");
        }
    }

    [Fact]
    public async Task Matchups_SpecificRegion_FallsBackToRawCompute()
    {
        await using var ctx = await SeededAsync();
        await new PrecomputedAnalyticsRefresher(ctx.Db, NullLogger<PrecomputedAnalyticsRefresher>.Instance)
            .RefreshMatchupsAsync(Patch, CancellationToken.None);
        var svc = Service(ctx.Db);

        // A specific region isn't precomputed → must equal the raw region-scoped compute.
        var raw = await svc.ComputeMatchupsAsync(100, "MIDDLE", "EMERALD_PLUS", "NA1", Patch, CancellationToken.None);
        var stats = await svc.ComputeMatchupsFromStatsAsync(100, "MIDDLE", "EMERALD_PLUS", "NA1", Patch, CancellationToken.None);
        stats.Should().BeEquivalentTo(raw, o => o.WithStrictOrdering());
    }

    private static ChampionAnalyticsComputeService Service(TranscendenceContext db) =>
        new(db,
            Options.Create(new ChampionAnalyticsComputeOptions
            {
                MinimumGamesRequired = 1,
                EarlyPatchMinimumGamesRequired = 1,
                BootstrapPatchMinimumGamesRequired = 1,
                BootstrapWindowHours = 24,
                ProvisionalWindowHours = 96,
                MaturingWindowHours = 240
            }),
            NullLogger<ChampionAnalyticsComputeService>.Instance);

    private static async Task<SeededContext> SeededAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TranscendenceContext>().UseSqlite(connection).Options;
        var db = new SqliteCompatibleTranscendenceContext(options);
        await db.Database.EnsureCreatedAsync();

        // champ 100 MIDDLE vs 200 — a counter (champ behind). 3 EMERALD games (0W) + 1 DIAMOND game (1W).
        SeedLanePair(db, "m1", 100, "EMERALD", "MIDDLE", win: false, opponent: 200, (cg: 4000, cx: 5000, og: 5200, ox: 6000, at: T0));
        SeedLanePair(db, "m2", 100, "EMERALD", "MIDDLE", win: false, opponent: 200, (cg: 4200, cx: 5200, og: 5000, ox: 5800, at: T0.AddMinutes(5)));
        SeedLanePair(db, "m3", 100, "EMERALD", "MIDDLE", win: false, opponent: 200, timeline: null); // no timeline
        SeedLanePair(db, "m4", 100, "DIAMOND", "MIDDLE", win: true, opponent: 200, (cg: 6000, cx: 7000, og: 5000, ox: 6200, at: T0.AddMinutes(10)));

        // champ 100 MIDDLE vs 300 — favorable. 3 EMERALD games (3W), all with timelines (champ ahead).
        SeedLanePair(db, "m5", 100, "EMERALD", "MIDDLE", win: true, opponent: 300, (cg: 7000, cx: 8000, og: 5500, ox: 6500, at: T0));
        SeedLanePair(db, "m6", 100, "EMERALD", "MIDDLE", win: true, opponent: 300, (cg: 6800, cx: 7800, og: 5600, ox: 6400, at: T0.AddMinutes(2)));
        SeedLanePair(db, "m7", 100, "EMERALD", "MIDDLE", win: true, opponent: 300, (cg: 6900, cx: 7900, og: 5400, ox: 6300, at: T0.AddMinutes(3)));

        // champ 100 MIDDLE vs 400 — even. 2 DIAMOND games (1W). 1 with timeline.
        SeedLanePair(db, "m8", 100, "DIAMOND", "MIDDLE", win: true, opponent: 400, (cg: 5500, cx: 6500, og: 5400, ox: 6400, at: T0.AddMinutes(7)));
        SeedLanePair(db, "m9", 100, "DIAMOND", "MIDDLE", win: false, opponent: 400, timeline: null);

        // unranked champ-side game (only visible under scope "all").
        SeedLanePair(db, "m10", 100, null, "MIDDLE", win: true, opponent: 300, (cg: 6000, cx: 7000, og: 5800, ox: 6700, at: T0.AddMinutes(1)));

        await db.SaveChangesAsync();
        return new SeededContext(connection, db);
    }

    private static void SeedLanePair(
        TranscendenceContext db, string matchId, int championId, string? championTier, string role, bool win,
        int opponent, (int cg, int cx, int og, int ox, DateTime at)? timeline = null)
    {
        var champSummoner = NewSummoner();
        var oppSummoner = NewSummoner();
        if (championTier != null)
            db.Ranks.Add(new Rank { Id = Guid.NewGuid(), SummonerId = champSummoner.Id, QueueType = "RANKED_SOLO_5x5", Tier = championTier });

        var match = new Transcendence.Data.Models.LoL.Match.Match
        {
            Id = Guid.NewGuid(),
            MatchId = matchId,
            MatchDate = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Duration = 1800,
            Patch = Patch,
            QueueId = 420,
            QueueFamily = "RANKED_SOLO_DUO",
            QueueType = "420",
            Status = FetchStatus.Success,
            PlatformRegion = "NA1",
            FetchedAt = DateTime.UtcNow
        };

        db.Summoners.Add(champSummoner);
        db.Summoners.Add(oppSummoner);
        db.Matches.Add(match);
        db.MatchParticipants.Add(NewParticipant(match, champSummoner, participantId: 1, teamId: 100, championId, role, win));
        db.MatchParticipants.Add(NewParticipant(match, oppSummoner, participantId: 2, teamId: 200, opponent, role, !win));

        if (timeline is { } t)
        {
            db.MatchParticipantTimelineSnapshots.Add(NewSnapshot(match, participantId: 1, t.cg, t.cx, t.at));
            db.MatchParticipantTimelineSnapshots.Add(NewSnapshot(match, participantId: 2, t.og, t.ox, t.at));
        }
    }

    private static Summoner NewSummoner() => new()
    {
        Id = Guid.NewGuid(),
        PlatformRegion = "NA1",
        Region = "americas",
        GameName = Guid.NewGuid().ToString("N")[..8],
        TagLine = "NA1",
        Puuid = Guid.NewGuid().ToString("N"),
        SummonerName = "s",
        RiotSummonerId = Guid.NewGuid().ToString("N")
    };

    private static MatchParticipant NewParticipant(
        Transcendence.Data.Models.LoL.Match.Match match, Summoner summoner, int participantId, int teamId,
        int championId, string role, bool win) => new()
    {
        Id = Guid.NewGuid(),
        MatchId = match.Id,
        Match = match,
        SummonerId = summoner.Id,
        Summoner = summoner,
        Puuid = summoner.Puuid,
        ParticipantId = participantId,
        TeamId = teamId,
        ChampionId = championId,
        TeamPosition = role,
        Win = win
    };

    private static MatchParticipantTimelineSnapshot NewSnapshot(
        Transcendence.Data.Models.LoL.Match.Match match, int participantId, int gold, int xp, DateTime at) => new()
    {
        MatchId = match.Id,
        Match = match,
        ParticipantId = participantId,
        MinuteMark = 15,
        Gold = gold,
        Xp = xp,
        Cs = 0,
        Level = 0,
        FrameTimestampMs = 900000,
        DerivedAtUtc = at
    };

    private sealed class SeededContext(SqliteConnection connection, TranscendenceContext db) : IAsyncDisposable
    {
        public TranscendenceContext Db { get; } = db;
        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
