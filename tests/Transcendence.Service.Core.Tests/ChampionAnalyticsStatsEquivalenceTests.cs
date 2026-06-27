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
/// The correctness gate for the stats-backed read path: for a rich seeded dataset, the precomputed
/// aggregates (built by <see cref="PrecomputedAnalyticsRefresher"/>) must reproduce the raw compute's DTOs
/// EXACTLY across every scope/region/tier/role combination — win rate, pick rate, role rank/population,
/// ban rate, conservative win rate, composite score, tier grade and ordering.
/// </summary>
public class ChampionAnalyticsStatsEquivalenceTests
{
    private const string Patch = "15.2";
    private static readonly string?[] Regions = [null, "NA1", "EUW1"];                 // null = ALL
    private static readonly string?[] WinRateTiers = [null, "ALL", "EMERALD_PLUS", "EMERALD", "DIAMOND"];
    private static readonly string?[] WinRateRoles = [null, "TOP", "MIDDLE"];

    [Fact]
    public async Task WinRates_StatsPath_EqualsRawCompute_AcrossAllScopes()
    {
        await using var ctx = await SeededAsync();
        await Refresh(ctx.Db);
        var svc = Service(ctx.Db);

        foreach (var champ in new[] { 100, 200, 300 })
        foreach (var region in Regions)
        foreach (var tier in WinRateTiers)
        foreach (var role in WinRateRoles)
        {
            var filter = new ChampionAnalyticsFilter(RankTier: tier, Region: region, Role: role);
            var raw = await svc.ComputeWinRatesAsync(champ, filter, Patch, CancellationToken.None);
            var stats = await svc.ComputeWinRatesFromStatsAsync(champ, filter, Patch, CancellationToken.None);

            stats.Should().BeEquivalentTo(raw, o => o.WithStrictOrdering(),
                $"win rates for champ {champ} tier={tier ?? "ALL"} region={region ?? "ALL"} role={role ?? "ALL"}");
        }
    }

    [Fact]
    public async Task TierList_UnifiedRole_StatsPath_EqualsRawCompute_IncludingBanRate()
    {
        await using var ctx = await SeededAsync();
        await Refresh(ctx.Db);
        var svc = Service(ctx.Db);

        foreach (var region in Regions)
        foreach (var tier in new string?[] { null, "ALL", "EMERALD_PLUS", "EMERALD" })
        {
            var raw = await svc.ComputeTierListAsync(null, tier, region, Patch, CancellationToken.None);
            var stats = await svc.ComputeTierListFromStatsAsync(null, tier, region, Patch, CancellationToken.None);

            // Unified tier list: live's ban denominator is also role-independent, so EVERYTHING matches.
            stats.Should().BeEquivalentTo(raw, o => o.WithStrictOrdering(),
                $"unified tier list tier={tier ?? "ALL"} region={region ?? "ALL"}");
        }
    }

    [Fact]
    public async Task TierList_RoleFiltered_StatsPath_EqualsRawCompute_ExceptIntentionallyRoleIndependentBanRate()
    {
        await using var ctx = await SeededAsync();
        await Refresh(ctx.Db);
        var svc = Service(ctx.Db);

        foreach (var role in new[] { "TOP", "MIDDLE" })
        foreach (var region in Regions)
        foreach (var tier in new string?[] { null, "EMERALD_PLUS", "EMERALD" })
        {
            var raw = await svc.ComputeTierListAsync(role, tier, region, Patch, CancellationToken.None);
            var stats = await svc.ComputeTierListFromStatsAsync(role, tier, region, Patch, CancellationToken.None);

            // Tiering/ordering/win/pick rates match exactly; BanRate is deliberately role-independent now
            // (it no longer role-scopes the denominator), so it is excluded from the equivalence assertion.
            stats.Should().BeEquivalentTo(raw, o => o.WithStrictOrdering().Excluding(e => e.BanRate),
                $"role-filtered tier list role={role} tier={tier ?? "ALL"} region={region ?? "ALL"}");
        }
    }

    [Fact]
    public async Task StatsPath_FallsBackToRawCompute_WhenNoAggregatesForPatch()
    {
        await using var ctx = await SeededAsync();
        // Deliberately do NOT refresh — no aggregate rows exist.
        var svc = Service(ctx.Db);

        var filter = new ChampionAnalyticsFilter(RankTier: "EMERALD_PLUS", Region: "NA1", Role: "TOP");
        var raw = await svc.ComputeWinRatesAsync(100, filter, Patch, CancellationToken.None);
        var stats = await svc.ComputeWinRatesFromStatsAsync(100, filter, Patch, CancellationToken.None);
        stats.Should().BeEquivalentTo(raw, o => o.WithStrictOrdering());

        var rawTl = await svc.ComputeTierListAsync("TOP", "EMERALD_PLUS", "NA1", Patch, CancellationToken.None);
        var statsTl = await svc.ComputeTierListFromStatsAsync("TOP", "EMERALD_PLUS", "NA1", Patch, CancellationToken.None);
        statsTl.Should().BeEquivalentTo(rawTl, o => o.WithStrictOrdering());
    }

    [Fact]
    public async Task GetAnalyticsComputedAt_ReturnsRefreshTimestamp_OrNullWhenNoAggregates()
    {
        await using var ctx = await SeededAsync();
        var before = DateTime.UtcNow;
        await Refresh(ctx.Db);
        var svc = Service(ctx.Db);

        var computedAt = await svc.GetAnalyticsComputedAtAsync(Patch, CancellationToken.None);
        computedAt.Should().NotBeNull();
        computedAt!.Value.Should().BeOnOrAfter(before.AddSeconds(-5));

        (await svc.GetAnalyticsComputedAtAsync("99.9", CancellationToken.None)).Should().BeNull();
    }

    // ---- harness ----

    private static ChampionWinRateComputeService Service(TranscendenceContext db) =>
        new(db,
            Options.Create(new ChampionAnalyticsComputeOptions
            {
                MinimumGamesRequired = 1,
                EarlyPatchMinimumGamesRequired = 1,
                BootstrapPatchMinimumGamesRequired = 1,
                BootstrapWindowHours = 24,
                ProvisionalWindowHours = 96,
                MaturingWindowHours = 240
            }));

    private static ChampionAnalyticsComputeService ComputeService(TranscendenceContext db) =>
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

    private static async Task Refresh(TranscendenceContext db) =>
        await new PrecomputedAnalyticsRefresher(db, ComputeService(db), NullLogger<PrecomputedAnalyticsRefresher>.Instance)
            .RefreshTabularCoreAsync(Patch, CancellationToken.None);

    private static async Task<SeededContext> SeededAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TranscendenceContext>().UseSqlite(connection).Options;
        var db = new SqliteCompatibleTranscendenceContext(options);
        await db.Database.EnsureCreatedAsync();

        // (region, tier, champion, role): N wins + M losses → multiple champions per (role, tier) so role-rank
        // ordering (winRate desc, games desc, id asc) and pick-rate denominators are non-trivial.
        AddGames(db, "NA1", "EMERALD", 100, "TOP", wins: 3, losses: 1);
        AddGames(db, "NA1", "EMERALD", 200, "TOP", wins: 1, losses: 1);
        AddGames(db, "NA1", "EMERALD", 300, "TOP", wins: 2, losses: 0);
        AddGames(db, "NA1", "EMERALD", 100, "MIDDLE", wins: 1, losses: 0);
        AddGames(db, "NA1", "EMERALD", 200, "MIDDLE", wins: 2, losses: 1);
        AddGames(db, "NA1", "DIAMOND", 100, "TOP", wins: 1, losses: 1);
        AddGames(db, "NA1", "DIAMOND", 300, "TOP", wins: 1, losses: 0);
        AddGames(db, "NA1", null, 100, "TOP", wins: 1, losses: 0);        // UNRANKED
        AddGames(db, "EUW1", "EMERALD", 100, "TOP", wins: 1, losses: 0);
        AddGames(db, "EUW1", "EMERALD", 300, "TOP", wins: 0, losses: 1);
        AddGames(db, "EUW1", "GOLD", 200, "TOP", wins: 1, losses: 1);

        // Bans across regions/tiers (champ 999 never played; champ 100 also banned somewhere).
        var banA = AddGames(db, "NA1", "EMERALD", 400, "JUNGLE", wins: 1, losses: 0).Single();
        var banB = AddGames(db, "NA1", "EMERALD", 400, "JUNGLE", wins: 0, losses: 1).Single();
        var banC = AddGames(db, "EUW1", "EMERALD", 400, "JUNGLE", wins: 1, losses: 0).Single();
        var banD = AddGames(db, "NA1", "DIAMOND", 400, "JUNGLE", wins: 1, losses: 0).Single();
        SeedBan(db, banA, 999);
        SeedBan(db, banB, 999);
        SeedBan(db, banC, 999);
        SeedBan(db, banD, 100);

        await db.SaveChangesAsync();
        return new SeededContext(connection, db);
    }

    private static List<Transcendence.Data.Models.LoL.Match.Match> AddGames(
        TranscendenceContext db, string region, string? tier, int champ, string role, int wins, int losses)
    {
        var matches = new List<Transcendence.Data.Models.LoL.Match.Match>();
        for (var i = 0; i < wins + losses; i++)
            matches.Add(AddGame(db, region, tier, champ, role, win: i < wins));
        return matches;
    }

    private static Transcendence.Data.Models.LoL.Match.Match AddGame(
        TranscendenceContext db, string region, string? tier, int champ, string role, bool win)
    {
        var summoner = new Summoner
        {
            Id = Guid.NewGuid(),
            PlatformRegion = region,
            Region = "americas",
            GameName = Guid.NewGuid().ToString("N")[..8],
            TagLine = region,
            Puuid = Guid.NewGuid().ToString("N"),
            SummonerName = "s",
            RiotSummonerId = Guid.NewGuid().ToString("N")
        };
        if (tier != null)
            db.Ranks.Add(new Rank { Id = Guid.NewGuid(), SummonerId = summoner.Id, QueueType = "RANKED_SOLO_5x5", Tier = tier });

        var match = new Transcendence.Data.Models.LoL.Match.Match
        {
            Id = Guid.NewGuid(),
            MatchId = Guid.NewGuid().ToString("N"),
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

    private static void SeedBan(TranscendenceContext db, Transcendence.Data.Models.LoL.Match.Match match, int championId) =>
        db.MatchBans.Add(new MatchBan { MatchId = match.Id, Match = match, TeamId = 200, PickTurn = 1, ChampionId = championId });

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
