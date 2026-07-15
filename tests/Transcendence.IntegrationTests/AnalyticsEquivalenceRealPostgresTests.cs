using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Account;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Service.Core.Services.Analytics.Implementations;
using Transcendence.Service.Core.Services.Analytics.Models;

namespace Transcendence.IntegrationTests;

/// <summary>
/// Mirrors the SQLite <c>ChampionAnalyticsStatsEquivalenceTests</c> against REAL Postgres/Npgsql: the
/// precomputed-stats read path must reproduce the raw compute's DTOs exactly, now exercising real
/// GROUP BY / tie-break ordering / NULL collation / integer-array columns that SQLite and the EF
/// InMemory provider cannot faithfully model. Each test is scoped to a unique patch, so tests are
/// isolated on the shared container without teardown (every analytics query filters by patch).
/// </summary>
[Collection(PostgresIntegrationCollection.Name)]
public sealed class AnalyticsEquivalenceRealPostgresTests(PostgresIntegrationFixture fixture)
{
    private static readonly string?[] Regions = [null, "NA1", "EUW1"];                 // null = ALL
    private static readonly string?[] Tiers = [null, "ALL", "EMERALD_PLUS", "EMERALD", "DIAMOND"];
    private static readonly string?[] Roles = [null, "TOP", "MIDDLE"];

    [Fact]
    public async Task WinRates_StatsPath_EqualsRawCompute_OnRealPostgres()
    {
        var patch = UniquePatch();
        await using var db = NewDb();
        await SeedAsync(db, patch);
        await RefreshAsync(db, patch);
        var svc = WinRateService(db);

        // The stats read path silently falls back to raw compute when no aggregate rows exist for the
        // patch (ComputeWinRatesFromStatsAsync → !HasStatsAsync → raw). Assert the refresher actually
        // populated the aggregate table, else the equivalence below is raw-vs-raw (tautological) and a
        // broken refresher would still pass green.
        (await db.ChampionRoleTierStats.CountAsync(x => x.Patch == patch))
            .Should().BeGreaterThan(0, "RefreshTabularCoreAsync must populate aggregate rows so the stats path is exercised, not the raw fallback");

        var nonEmptyComparisons = 0;
        foreach (var champ in new[] { 100, 200, 300 })
        foreach (var region in Regions)
        foreach (var tier in Tiers)
        foreach (var role in Roles)
        {
            var filter = new ChampionAnalyticsFilter(RankTier: tier, Region: region, Role: role);
            var raw = await svc.ComputeWinRatesAsync(champ, filter, patch, CancellationToken.None);
            var stats = await svc.ComputeWinRatesFromStatsAsync(champ, filter, patch, CancellationToken.None);

            if (raw.Count > 0) nonEmptyComparisons++;
            stats.Should().BeEquivalentTo(raw, o => o.WithStrictOrdering(),
                $"win rates for champ {champ} tier={tier ?? "ALL"} region={region ?? "ALL"} role={role ?? "ALL"} must match on Postgres");
        }

        nonEmptyComparisons.Should().BeGreaterThan(0,
            "at least some scopes must yield rows so the stats-vs-raw comparisons are load-bearing, not empty==empty");
    }

    [Fact]
    public async Task UnifiedTierList_StatsPath_EqualsRawCompute_OnRealPostgres()
    {
        var patch = UniquePatch();
        await using var db = NewDb();
        await SeedAsync(db, patch);
        await RefreshAsync(db, patch);
        var svc = WinRateService(db);

        // Same fallback guard as the win-rate test: prove the aggregates exist so the stats tier-list
        // path is genuinely exercised rather than transparently deferring to raw compute.
        (await db.ChampionRoleTierStats.CountAsync(x => x.Patch == patch))
            .Should().BeGreaterThan(0, "RefreshTabularCoreAsync must populate aggregate rows so the stats path is exercised, not the raw fallback");

        var nonEmptyComparisons = 0;
        foreach (var region in Regions)
        foreach (var tier in new string?[] { null, "ALL", "EMERALD_PLUS", "EMERALD" })
        {
            var raw = await svc.ComputeTierListAsync(null, tier, region, patch, CancellationToken.None);
            var stats = await svc.ComputeTierListFromStatsAsync(null, tier, region, patch, CancellationToken.None);

            if (raw.Count > 0) nonEmptyComparisons++;
            // Movement/PreviousTier are persisted-only on region=ALL grades — excluded, mirroring the
            // SQLite equivalence gate.
            stats.Should().BeEquivalentTo(raw, o => o.WithStrictOrdering()
                    .Excluding(e => e.Movement).Excluding(e => e.PreviousTier),
                $"unified tier list tier={tier ?? "ALL"} region={region ?? "ALL"} must match on Postgres");
        }

        nonEmptyComparisons.Should().BeGreaterThan(0,
            "at least some tier-list scopes must be non-empty so the comparisons are load-bearing");
    }

    // ---- harness (ported from ChampionAnalyticsStatsEquivalenceTests, real Npgsql context) ----

    private TranscendenceContext NewDb() =>
        new(new DbContextOptionsBuilder<TranscendenceContext>()
            .UseNpgsql(fixture.ConnectionString)
            .Options);

    private static string UniquePatch() => $"eqv-{Guid.NewGuid():N}"[..12];

    private static ChampionAnalyticsComputeOptions ComputeOptions() => new()
    {
        MinimumGamesRequired = 1,
        EarlyPatchMinimumGamesRequired = 1,
        BootstrapPatchMinimumGamesRequired = 1,
        BootstrapWindowHours = 24,
        ProvisionalWindowHours = 96,
        MaturingWindowHours = 240
    };

    private static ChampionWinRateComputeService WinRateService(TranscendenceContext db) =>
        new(db, Options.Create(ComputeOptions()), Options.Create(new TieringOptions()));

    private static async Task RefreshAsync(TranscendenceContext db, string patch)
    {
        var buildService = new ChampionBuildComputeService(db, Options.Create(ComputeOptions()),
            NullLogger<ChampionBuildComputeService>.Instance);
        var proService = new ChampionProComputeService(db, Options.Create(ComputeOptions()));
        await new PrecomputedAnalyticsRefresher(db, buildService, proService,
                Options.Create(new TieringOptions()), NullLogger<PrecomputedAnalyticsRefresher>.Instance)
            .RefreshTabularCoreAsync(patch, CancellationToken.None);
    }

    private static async Task SeedAsync(TranscendenceContext db, string patch)
    {
        AddGames(db, patch, "NA1", "EMERALD", 100, "TOP", wins: 3, losses: 1);
        AddGames(db, patch, "NA1", "EMERALD", 200, "TOP", wins: 1, losses: 1);
        AddGames(db, patch, "NA1", "EMERALD", 300, "TOP", wins: 2, losses: 0);
        AddGames(db, patch, "NA1", "EMERALD", 100, "MIDDLE", wins: 1, losses: 0);
        AddGames(db, patch, "NA1", "EMERALD", 200, "MIDDLE", wins: 2, losses: 1);
        AddGames(db, patch, "NA1", "DIAMOND", 100, "TOP", wins: 1, losses: 1);
        AddGames(db, patch, "NA1", "DIAMOND", 300, "TOP", wins: 1, losses: 0);
        AddGames(db, patch, "NA1", null, 100, "TOP", wins: 1, losses: 0);          // UNRANKED
        AddGames(db, patch, "EUW1", "EMERALD", 100, "TOP", wins: 1, losses: 0);
        AddGames(db, patch, "EUW1", "EMERALD", 300, "TOP", wins: 0, losses: 1);
        AddGames(db, patch, "EUW1", "GOLD", 200, "TOP", wins: 1, losses: 1);

        var banA = AddGames(db, patch, "NA1", "EMERALD", 400, "JUNGLE", wins: 1, losses: 0).Single();
        var banB = AddGames(db, patch, "NA1", "EMERALD", 400, "JUNGLE", wins: 0, losses: 1).Single();
        var banC = AddGames(db, patch, "EUW1", "EMERALD", 400, "JUNGLE", wins: 1, losses: 0).Single();
        var banD = AddGames(db, patch, "NA1", "DIAMOND", 400, "JUNGLE", wins: 1, losses: 0).Single();
        SeedBan(db, banA, 999);
        SeedBan(db, banB, 999);
        SeedBan(db, banC, 999);
        SeedBan(db, banD, 100);

        await db.SaveChangesAsync();
    }

    private static List<Match> AddGames(
        TranscendenceContext db, string patch, string region, string? tier, int champ, string role, int wins, int losses)
    {
        var matches = new List<Match>();
        for (var i = 0; i < wins + losses; i++)
            matches.Add(AddGame(db, patch, region, tier, champ, role, win: i < wins));
        return matches;
    }

    private static Match AddGame(
        TranscendenceContext db, string patch, string region, string? tier, int champ, string role, bool win)
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

        var match = new Match
        {
            Id = Guid.NewGuid(),
            MatchId = Guid.NewGuid().ToString("N"),
            MatchDate = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Duration = 1800,
            Patch = patch,
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

    private static void SeedBan(TranscendenceContext db, Match match, int championId) =>
        db.MatchBans.Add(new MatchBan { MatchId = match.Id, Match = match, TeamId = 200, PickTurn = 1, ChampionId = championId });
}
