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
/// Validates the precompute aggregation (<see cref="PrecomputedAnalyticsRefresher"/>) against a hand-computed
/// fixture — especially the parts the adversarial equivalence review flagged as risky: the non-additive
/// distinct-match ban denominator/numerator and the explicit global PlatformRegion="ALL" rows.
/// </summary>
public class PrecomputedAnalyticsRefresherTests
{
    private const string Patch = "15.2";

    [Fact]
    public async Task RefreshTabularCore_RoleTierStats_AggregateGamesAndWinsPerRegionTierChampionRole()
    {
        await using var ctx = await SeededAsync();
        await Refresh(ctx.Db);

        var rows = await ctx.Db.ChampionRoleTierStats.AsNoTracking().ToListAsync();

        // One row per (region, tier, champion, role); Games/Wins are the additive atoms.
        Stat(rows, "NA1", "EMERALD", 100, "TOP").Should().Be((2, 1));   // M1 win, M2 loss
        Stat(rows, "NA1", "DIAMOND", 100, "TOP").Should().Be((1, 1));   // M3 win
        Stat(rows, "NA1", "EMERALD", 100, "MIDDLE").Should().Be((1, 1)); // M4 win
        Stat(rows, "NA1", "EMERALD", 200, "TOP").Should().Be((1, 0));   // M5 loss
        Stat(rows, "NA1", "UNRANKED", 100, "TOP").Should().Be((1, 1));  // M6 win, no rank
        Stat(rows, "EUW1", "EMERALD", 100, "TOP").Should().Be((1, 1));  // M7 win
        Stat(rows, "EUW1", "GOLD", 100, "TOP").Should().Be((1, 0));     // M8 loss

        rows.Should().HaveCount(7);
        rows.Should().OnlyContain(r => r.Patch == Patch);
    }

    [Fact]
    public async Task RefreshTabularCore_ScopeMatchCounts_PerRegionAndExplicitGlobalAll()
    {
        await using var ctx = await SeededAsync();
        await Refresh(ctx.Db);

        var rows = await ctx.Db.ScopeMatchCountStats.AsNoTracking().ToListAsync();

        // ALL scope = every ranked match (each fixture match has one assigned-role participant).
        Total(rows, "NA1", "ALL").Should().Be(6);   // M1..M6
        Total(rows, "EUW1", "ALL").Should().Be(2);  // M7, M8
        Total(rows, "ALL", "ALL").Should().Be(8);   // global, NOT necessarily the per-region sum

        // EMERALD_PLUS = EMERALD + DIAMOND (+ master/gm/chall, none here).
        Total(rows, "NA1", "EMERALD_PLUS").Should().Be(5); // M1,M2,M4,M5 + M3
        Total(rows, "EUW1", "EMERALD_PLUS").Should().Be(1); // M7
        Total(rows, "ALL", "EMERALD_PLUS").Should().Be(6);

        // Exact tiers.
        Total(rows, "NA1", "EMERALD").Should().Be(4);
        Total(rows, "ALL", "EMERALD").Should().Be(5);
        Total(rows, "NA1", "DIAMOND").Should().Be(1);
        Total(rows, "EUW1", "GOLD").Should().Be(1);

        // Empty scopes (IRON/BRONZE/SILVER/MASTER/...) produce no rows at all.
        rows.Should().NotContain(r => r.RankScope == "IRON");
        rows.Should().NotContain(r => r.TotalMatches == 0);
    }

    [Fact]
    public async Task RefreshTabularCore_BanCounts_DistinctPerScopeWithGlobalAllRow()
    {
        await using var ctx = await SeededAsync();
        await Refresh(ctx.Db);

        var rows = await ctx.Db.ChampionBanScopeStats.AsNoTracking().Where(r => r.ChampionId == 999).ToListAsync();

        // Champ 999 banned in M1 (NA1, EMERALD), M3 (NA1, DIAMOND), M7 (EUW1, EMERALD).
        Banned(rows, "NA1", "ALL").Should().Be(2);          // M1, M3
        Banned(rows, "EUW1", "ALL").Should().Be(1);         // M7
        Banned(rows, "ALL", "ALL").Should().Be(3);          // M1, M3, M7 (global distinct)

        Banned(rows, "NA1", "EMERALD_PLUS").Should().Be(2); // M1, M3
        Banned(rows, "EUW1", "EMERALD_PLUS").Should().Be(1);// M7
        Banned(rows, "ALL", "EMERALD_PLUS").Should().Be(3);

        Banned(rows, "NA1", "EMERALD").Should().Be(1);      // M1
        Banned(rows, "EUW1", "EMERALD").Should().Be(1);     // M7
        Banned(rows, "ALL", "EMERALD").Should().Be(2);      // M1, M7

        Banned(rows, "NA1", "DIAMOND").Should().Be(1);      // M3
        Banned(rows, "ALL", "DIAMOND").Should().Be(1);

        // Not banned in the GOLD match (M8) → no GOLD ban rows for 999.
        rows.Should().NotContain(r => r.RankScope == "GOLD");
    }

    [Fact]
    public async Task RefreshTabularCore_DerivedBanRate_MatchesHandComputed()
    {
        await using var ctx = await SeededAsync();
        await Refresh(ctx.Db);

        // The read derives BanRate = BannedMatches / TotalMatches by point-looking-up the same (region, scope).
        double BanRate(string region, string scope)
        {
            var total = ctx.Db.ScopeMatchCountStats.Single(r => r.PlatformRegion == region && r.RankScope == scope).TotalMatches;
            var banned = ctx.Db.ChampionBanScopeStats.SingleOrDefault(r => r.ChampionId == 999 && r.PlatformRegion == region && r.RankScope == scope)?.BannedMatches ?? 0;
            return (double)banned / total;
        }

        BanRate("NA1", "EMERALD").Should().BeApproximately(1.0 / 4, 1e-9);
        BanRate("ALL", "EMERALD").Should().BeApproximately(2.0 / 5, 1e-9);
        BanRate("ALL", "ALL").Should().BeApproximately(3.0 / 8, 1e-9);
    }

    [Fact]
    public async Task RefreshTabularCore_IsIdempotent_ReplacesPatchRowsWithoutDuplicating()
    {
        await using var ctx = await SeededAsync();

        await Refresh(ctx.Db);
        var firstRoleTier = await ctx.Db.ChampionRoleTierStats.AsNoTracking().CountAsync();
        var firstBans = await ctx.Db.ChampionBanScopeStats.AsNoTracking().CountAsync();

        await Refresh(ctx.Db);
        var secondRoleTier = await ctx.Db.ChampionRoleTierStats.AsNoTracking().CountAsync();
        var secondBans = await ctx.Db.ChampionBanScopeStats.AsNoTracking().CountAsync();

        secondRoleTier.Should().Be(firstRoleTier);
        secondBans.Should().Be(firstBans);
    }

    // ---- helpers ----

    private static async Task Refresh(TranscendenceContext db)
    {
        var compute = new ChampionAnalyticsComputeService(
            db,
            Options.Create(new ChampionAnalyticsComputeOptions { MinimumGamesRequired = 1 }),
            NullLogger<ChampionAnalyticsComputeService>.Instance);
        var build = new ChampionBuildComputeService(
            db,
            Options.Create(new ChampionAnalyticsComputeOptions { MinimumGamesRequired = 1 }),
            NullLogger<ChampionBuildComputeService>.Instance);
        var refresher = new PrecomputedAnalyticsRefresher(db, compute, build, NullLogger<PrecomputedAnalyticsRefresher>.Instance);
        await refresher.RefreshTabularCoreAsync(Patch, CancellationToken.None);
    }

    private static (int Games, int Wins) Stat(IEnumerable<Transcendence.Data.Models.LoL.Analytics.ChampionRoleTierStat> rows,
        string region, string tier, int champ, string role)
    {
        var r = rows.Single(x => x.PlatformRegion == region && x.RankTier == tier && x.ChampionId == champ && x.Role == role);
        return (r.Games, r.Wins);
    }

    private static int Total(IEnumerable<Transcendence.Data.Models.LoL.Analytics.ScopeMatchCountStat> rows, string region, string scope) =>
        rows.Single(x => x.PlatformRegion == region && x.RankScope == scope).TotalMatches;

    private static int Banned(IEnumerable<Transcendence.Data.Models.LoL.Analytics.ChampionBanScopeStat> rows, string region, string scope) =>
        rows.Single(x => x.PlatformRegion == region && x.RankScope == scope).BannedMatches;

    /// <summary>Seeds the shared fixture (M1..M8 + bans) and returns an open in-memory context.</summary>
    private static async Task<SeededContext> SeededAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TranscendenceContext>().UseSqlite(connection).Options;
        var db = new SqliteCompatibleTranscendenceContext(options);
        await db.Database.EnsureCreatedAsync();

        var m1 = SeedOne(db, "NA1_1", "NA1", champ: 100, role: "TOP", win: true, tier: "EMERALD");
        SeedOne(db, "NA1_2", "NA1", champ: 100, role: "TOP", win: false, tier: "EMERALD");
        var m3 = SeedOne(db, "NA1_3", "NA1", champ: 100, role: "TOP", win: true, tier: "DIAMOND");
        SeedOne(db, "NA1_4", "NA1", champ: 100, role: "MIDDLE", win: true, tier: "EMERALD");
        SeedOne(db, "NA1_5", "NA1", champ: 200, role: "TOP", win: false, tier: "EMERALD");
        SeedOne(db, "NA1_6", "NA1", champ: 100, role: "TOP", win: true, tier: null); // UNRANKED
        var m7 = SeedOne(db, "EUW1_7", "EUW1", champ: 100, role: "TOP", win: true, tier: "EMERALD");
        SeedOne(db, "EUW1_8", "EUW1", champ: 100, role: "TOP", win: false, tier: "GOLD");

        SeedBan(db, m1, championId: 999);
        SeedBan(db, m3, championId: 999);
        SeedBan(db, m7, championId: 999);

        await db.SaveChangesAsync();
        return new SeededContext(connection, db);
    }

    private static Transcendence.Data.Models.LoL.Match.Match SeedOne(
        TranscendenceContext db, string matchId, string region, int champ, string role, bool win, string? tier)
    {
        var summoner = new Summoner
        {
            Id = Guid.NewGuid(),
            PlatformRegion = region,
            Region = "americas",
            GameName = matchId,
            TagLine = region,
            Puuid = Guid.NewGuid().ToString("N"),
            SummonerName = matchId,
            RiotSummonerId = Guid.NewGuid().ToString("N")
        };

        if (tier != null)
        {
            db.Ranks.Add(new Rank
            {
                Id = Guid.NewGuid(),
                SummonerId = summoner.Id,
                QueueType = "RANKED_SOLO_5x5",
                Tier = tier
            });
        }

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
            PlatformRegion = region,
            FetchedAt = DateTime.UtcNow
        };

        db.Summoners.Add(summoner);
        db.Matches.Add(match);
        db.MatchParticipants.Add(new MatchParticipant
        {
            Id = Guid.NewGuid(),
            MatchId = match.Id,
            Match = match,
            SummonerId = summoner.Id,
            Summoner = summoner,
            Puuid = summoner.Puuid,
            ParticipantId = 1,
            TeamId = 100,
            ChampionId = champ,
            TeamPosition = role,
            Win = win
        });
        return match;
    }

    private static void SeedBan(TranscendenceContext db, Transcendence.Data.Models.LoL.Match.Match match, int championId)
    {
        db.MatchBans.Add(new MatchBan
        {
            MatchId = match.Id,
            Match = match,
            TeamId = 200,
            PickTurn = 1,
            ChampionId = championId
        });
    }

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
