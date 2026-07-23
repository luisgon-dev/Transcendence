using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Account;
using Transcendence.Data.Models.LoL.Analytics;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Data.Models.LoL.Static;
using Transcendence.Service.Core.Services.Analytics;
using Transcendence.Service.Core.Services.Analytics.Implementations;
using Transcendence.Service.Core.Services.Analytics.Interfaces;
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
    public async Task RefreshTabularCore_RegionUsesHistoricalMatchInsteadOfTransferredSummoner()
    {
        await using var ctx = await SeededAsync();
        var transferredParticipant = await ctx.Db.MatchParticipants
            .Include(x => x.Match)
            .Include(x => x.Summoner)
            .FirstAsync(x => x.Match.PlatformRegion == "NA1");
        transferredParticipant.Summoner.PlatformRegion = "EUW1";
        await ctx.Db.SaveChangesAsync();

        await Refresh(ctx.Db);

        var rows = await ctx.Db.ScopeMatchCountStats.AsNoTracking()
            .Where(x => x.RankScope == "ALL")
            .ToListAsync();
        Total(rows, "NA1", "ALL").Should().Be(6);
        Total(rows, "EUW1", "ALL").Should().Be(2);
        Total(rows, "ALL", "ALL").Should().Be(8);
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
        var firstGrades = await ctx.Db.ChampionScopeGradeStats.AsNoTracking().CountAsync();

        await Refresh(ctx.Db);
        var secondRoleTier = await ctx.Db.ChampionRoleTierStats.AsNoTracking().CountAsync();
        var secondBans = await ctx.Db.ChampionBanScopeStats.AsNoTracking().CountAsync();
        var secondGrades = await ctx.Db.ChampionScopeGradeStats.AsNoTracking().CountAsync();

        secondRoleTier.Should().Be(firstRoleTier);
        secondBans.Should().Be(firstBans);
        firstGrades.Should().BeGreaterThan(0);
        secondGrades.Should().Be(firstGrades); // the grade table is replaced per patch, not duplicated
    }

    [Fact]
    public async Task RefreshAll_WhenFinalPhaseFails_RollsBackEveryEarlierSurface()
    {
        await using var ctx = await SeededAsync();
        await Refresh(ctx.Db);
        Total(await ctx.Db.ScopeMatchCountStats.AsNoTracking().ToListAsync(), "NA1", "ALL").Should().Be(6);

        SeedOne(ctx.Db, "NA1_9", "NA1", champ: 100, role: "TOP", win: true, tier: "EMERALD");
        await ctx.Db.SaveChangesAsync();

        var build = new ChampionBuildComputeService(
            ctx.Db,
            Options.Create(new ChampionAnalyticsComputeOptions { MinimumGamesRequired = 1 }),
            NullLogger<ChampionBuildComputeService>.Instance);
        var pro = new Mock<IChampionProComputeService>();
        pro.Setup(x => x.ComputeProChampionPlayrateAsync(
                null,
                It.IsAny<string>(),
                Patch,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated final-phase failure"));
        var refresher = new PrecomputedAnalyticsRefresher(
            ctx.Db,
            build,
            pro.Object,
            Options.Create(new TieringOptions()),
            NullLogger<PrecomputedAnalyticsRefresher>.Instance);

        var act = () => refresher.RefreshAllAsync(Patch, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        ctx.Db.ChangeTracker.Clear();
        var persisted = await ctx.Db.ScopeMatchCountStats.AsNoTracking().ToListAsync();
        Total(persisted, "NA1", "ALL").Should().Be(6,
            "the previous complete snapshot remains visible when a later phase fails");
    }

    [Fact]
    public async Task RefreshBuildResources_DeduplicatesParticipantUsageAndExcludesStatShards()
    {
        await using var ctx = await SeededAsync();
        ctx.Db.Patches.Add(new Patch
        {
            Version = Patch,
            ReleaseDate = DateTime.UtcNow,
            IsActive = true
        });
        ctx.Db.ItemVersions.Add(new ItemVersion
        {
            ItemId = 3078,
            PatchVersion = Patch,
            Name = "Trinity Force",
            BuildsFrom = [3057],
            BuildsInto = [],
            Tags = ["Damage"],
            InStore = true,
            PriceTotal = 3333
        });
        ctx.Db.RuneVersions.Add(new RuneVersion
        {
            RuneId = 8005,
            PatchVersion = Patch,
            Name = "Press the Attack",
            RunePathId = 8000,
            Slot = 0
        });
        var participant = await ctx.Db.MatchParticipants
            .SingleAsync(row => row.Match.MatchId == "NA1_1");
        ctx.Db.MatchParticipantItems.AddRange(
            new MatchParticipantItem
            {
                MatchParticipantId = participant.Id,
                SlotIndex = 0,
                ItemId = 3078,
                PatchVersion = Patch
            },
            new MatchParticipantItem
            {
                MatchParticipantId = participant.Id,
                SlotIndex = 1,
                ItemId = 3078,
                PatchVersion = Patch
            });
        ctx.Db.MatchParticipantRunes.AddRange(
            new MatchParticipantRune
            {
                MatchParticipantId = participant.Id,
                RuneId = 8005,
                PatchVersion = Patch,
                SelectionTree = RuneSelectionTree.Primary,
                SelectionIndex = 0
            },
            new MatchParticipantRune
            {
                MatchParticipantId = participant.Id,
                RuneId = 8005,
                PatchVersion = Patch,
                SelectionTree = RuneSelectionTree.StatShards,
                SelectionIndex = 0
            });
        await ctx.Db.SaveChangesAsync();

        var refresher = new PrecomputedAnalyticsRefresher(
            ctx.Db,
            Mock.Of<IChampionBuildComputeService>(),
            Mock.Of<IChampionProComputeService>(),
            Options.Create(new TieringOptions()),
            NullLogger<PrecomputedAnalyticsRefresher>.Instance);

        var count = await refresher.RefreshBuildResourcesAsync(Patch, CancellationToken.None);

        count.Should().Be(2);
        var rows = await ctx.Db.BuildResourceStats.AsNoTracking().ToListAsync();
        rows.Should().ContainSingle(row =>
            row.ResourceType == "item" && row.ResourceId == 3078 &&
            row.PlatformRegion == "NA1" && row.Games == 1 && row.Wins == 1);
        rows.Should().ContainSingle(row =>
            row.ResourceType == "rune" && row.ResourceId == 8005 &&
            row.PlatformRegion == "NA1" && row.Games == 1 && row.Wins == 1);
    }

    [Fact]
    public async Task RefreshTabularCore_ScopeGradeStats_PersistsPerRoleAndPrimaryRoleOverviewRows_ForGlobalDefaultScopes()
    {
        await using var ctx = await SeededAsync();
        var result = await Refresh(ctx.Db);

        var grades = await ctx.Db.ChampionScopeGradeStats.AsNoTracking().ToListAsync();

        // Grades are persisted only for the synthetic global region and the two web-default scopes.
        grades.Should().OnlyContain(g => g.PlatformRegion == "ALL");
        grades.Should().OnlyContain(g => g.Patch == Patch);
        grades.Select(g => g.RankScope).Distinct().Should().BeEquivalentTo(new[] { "ALL", "EMERALD_PLUS" });

        foreach (var scope in new[] { "ALL", "EMERALD_PLUS" })
        {
            var scoped = grades.Where(g => g.RankScope == scope).ToList();

            // One row per played (champion, lane).
            scoped.Should().Contain(g => g.Role == "TOP" && g.ChampionId == 100);
            scoped.Should().Contain(g => g.Role == "TOP" && g.ChampionId == 200);
            scoped.Should().Contain(g => g.Role == "MIDDLE" && g.ChampionId == 100);

            // Plus a synthetic Role="ALL" overview row carrying the champion's most-played lane.
            var overview100 = scoped.Single(g => g.Role == "ALL" && g.ChampionId == 100);
            overview100.PrimaryRole.Should().Be("TOP"); // 100 plays TOP more than MIDDLE in every scope
            scoped.Should().Contain(g => g.Role == "ALL" && g.ChampionId == 200);
        }

        // No Patch rows were seeded → no previous patch resolvable → every grade is NEW with no previous tier.
        grades.Should().OnlyContain(g => g.Movement == (int)TierMovement.NEW);
        grades.Should().OnlyContain(g => g.PreviousTier == null);

        // 3 per-role + 2 overview rows per scope, two scopes.
        result.GradeRows.Should().Be(10);
        result.GradeRows.Should().Be(grades.Count);
    }

    [Fact]
    public async Task RefreshTabularCore_PersistedGrades_EqualTheSharedScorerOverTheSameAtoms()
    {
        await using var ctx = await SeededAsync();
        await Refresh(ctx.Db);

        var roleTier = await ctx.Db.ChampionRoleTierStats.AsNoTracking().ToListAsync();
        var scopeMatches = await ctx.Db.ScopeMatchCountStats.AsNoTracking().ToListAsync();
        var bans = await ctx.Db.ChampionBanScopeStats.AsNoTracking().ToListAsync();
        var grades = await ctx.Db.ChampionScopeGradeStats.AsNoTracking().ToListAsync();

        var options = new TieringOptions();

        foreach (var scope in new[] { "ALL", "EMERALD_PLUS" })
        {
            var tiersInScope = RankTierCatalog.ResolveScopeTiers(scope);

            // region=ALL aggregate: sum atoms across every region and the tiers in scope (mirrors the refresher).
            var aggregated = roleTier
                .Where(r => tiersInScope == null || tiersInScope.Contains(r.RankTier))
                .GroupBy(r => new { r.ChampionId, r.Role })
                .Select(g => new ChampionTierScorer.RoleGames(
                    g.Key.ChampionId, g.Key.Role, g.Sum(x => x.Games), g.Sum(x => x.Wins)))
                .ToList();

            var totalScopeMatches = scopeMatches
                .Where(x => x.PlatformRegion == "ALL" && x.RankScope == scope)
                .Select(x => x.TotalMatches)
                .FirstOrDefault();

            var banByChampion = bans
                .Where(x => x.PlatformRegion == "ALL" && x.RankScope == scope)
                .GroupBy(x => x.ChampionId)
                .ToDictionary(g => g.Key, g => g.First().BannedMatches);

            var score = ChampionTierScorer.ScoreScope(aggregated, banByChampion, totalScopeMatches, options);

            foreach (var s in score.PerRole)
            {
                var persisted = grades.Single(g => g.RankScope == scope && g.Role == s.Role && g.ChampionId == s.ChampionId);
                persisted.Tier.Should().Be((int)s.Tier);
                persisted.StrengthScore.Should().BeApproximately(s.StrengthScore, 1e-9);
                persisted.WinRate.Should().BeApproximately(s.WinRate, 1e-9);
                persisted.PickRate.Should().BeApproximately(s.PickRate, 1e-9);
                persisted.BanRate.Should().BeApproximately(s.BanRate, 1e-9);
                persisted.ContestedScore.Should().BeApproximately(s.ContestedScore, 1e-9);
                persisted.RoleBaseline.Should().BeApproximately(s.RoleBaseline, 1e-9);
                persisted.PriorStrength.Should().BeApproximately(s.PriorStrength, 1e-9);
                persisted.Games.Should().Be(s.Games);
                persisted.Wins.Should().Be(s.Wins);
                persisted.IsLowSample.Should().Be(s.IsLowSample);
            }

            foreach (var s in score.Overview)
            {
                var persisted = grades.Single(g => g.RankScope == scope && g.Role == "ALL" && g.ChampionId == s.ChampionId);
                persisted.Tier.Should().Be((int)s.Tier);
                persisted.StrengthScore.Should().BeApproximately(s.StrengthScore, 1e-9);
                persisted.PrimaryRole.Should().Be(s.Role); // the overview carries the graded (primary) role
            }
        }
    }

    [Fact]
    public async Task RefreshTabularCore_Movement_IsResolvedAgainstThePreviousPatchGrades()
    {
        await using var ctx = await SeededAsync();

        // A previous patch (earlier release) and the current patch, so movement is resolvable.
        ctx.Db.Patches.AddRange(
            new Patch { Version = "15.1", ReleaseDate = DateTime.UtcNow.AddDays(-21), DetectedAt = DateTime.UtcNow.AddDays(-21), IsActive = false },
            new Patch { Version = Patch, ReleaseDate = DateTime.UtcNow.AddDays(-2), DetectedAt = DateTime.UtcNow.AddDays(-2), IsActive = true });

        // Previous-patch grades for the region=ALL / scope=ALL rows that all land at Tier B this patch:
        //   (TOP,100) was C → moves UP; (MIDDLE,100) was B → SAME; (TOP,200) was S → moves DOWN.
        SeedPreviousGrade(ctx.Db, "15.1", "ALL", "TOP", champ: 100, tier: TierGrade.C);
        SeedPreviousGrade(ctx.Db, "15.1", "ALL", "MIDDLE", champ: 100, tier: TierGrade.B);
        SeedPreviousGrade(ctx.Db, "15.1", "ALL", "TOP", champ: 200, tier: TierGrade.S);
        await ctx.Db.SaveChangesAsync();

        await Refresh(ctx.Db);

        var grades = await ctx.Db.ChampionScopeGradeStats.AsNoTracking()
            .Where(g => g.Patch == Patch && g.RankScope == "ALL")
            .ToListAsync();

        var top100 = grades.Single(g => g.Role == "TOP" && g.ChampionId == 100);
        top100.Tier.Should().Be((int)TierGrade.B);
        top100.PreviousTier.Should().Be((int)TierGrade.C);
        top100.Movement.Should().Be((int)TierMovement.UP);   // B (2) is better than C (3)

        var mid100 = grades.Single(g => g.Role == "MIDDLE" && g.ChampionId == 100);
        mid100.Tier.Should().Be((int)TierGrade.B);
        mid100.PreviousTier.Should().Be((int)TierGrade.B);
        mid100.Movement.Should().Be((int)TierMovement.SAME);

        var top200 = grades.Single(g => g.Role == "TOP" && g.ChampionId == 200);
        top200.Tier.Should().Be((int)TierGrade.B);
        top200.PreviousTier.Should().Be((int)TierGrade.S);
        top200.Movement.Should().Be((int)TierMovement.DOWN); // B (2) is worse than S (0)

        // The EMERALD_PLUS scope had no prior grades seeded → its rows are all still NEW.
        var emeraldPlus = await ctx.Db.ChampionScopeGradeStats.AsNoTracking()
            .Where(g => g.Patch == Patch && g.RankScope == "EMERALD_PLUS")
            .ToListAsync();
        emeraldPlus.Should().OnlyContain(g => g.Movement == (int)TierMovement.NEW && g.PreviousTier == null);
    }

    // ---- helpers ----

    private static async Task<PrecomputedAnalyticsRefreshResult> Refresh(TranscendenceContext db, string patch = Patch)
    {
        var build = new ChampionBuildComputeService(
            db,
            Options.Create(new ChampionAnalyticsComputeOptions { MinimumGamesRequired = 1 }),
            NullLogger<ChampionBuildComputeService>.Instance);
        var pro = new ChampionProComputeService(
            db,
            Options.Create(new ChampionAnalyticsComputeOptions { MinimumGamesRequired = 1 }));
        var refresher = new PrecomputedAnalyticsRefresher(db, build, pro, Options.Create(new TieringOptions()), NullLogger<PrecomputedAnalyticsRefresher>.Instance);
        return await refresher.RefreshTabularCoreAsync(patch, CancellationToken.None);
    }

    private static void SeedPreviousGrade(
        TranscendenceContext db, string patch, string scope, string role, int champ, TierGrade tier) =>
        db.ChampionScopeGradeStats.Add(new ChampionScopeGradeStat
        {
            Id = Guid.NewGuid(),
            Patch = patch,
            PlatformRegion = "ALL",
            RankScope = scope,
            Role = role,
            ChampionId = champ,
            PrimaryRole = role,
            Tier = (int)tier,
            ComputedAtUtc = DateTime.UtcNow
        });

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
