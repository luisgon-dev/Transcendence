using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Account;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Data.Models.LoL.Static;
using Transcendence.Service.Core.Services.Analytics.Implementations;
using Transcendence.Service.Core.Services.Analytics.Models;
using Transcendence.Service.Core.Tests.Support;

namespace Transcendence.Service.Core.Tests;

public class ChampionAnalyticsComputeServiceTests
{
    [Fact]
    public async Task ComputeTierListAsync_DoesNotPopulatePatchMovementFromPreviousPatchHotPath()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<TranscendenceContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new SqliteCompatibleTranscendenceContext(options);
        await db.Database.EnsureCreatedAsync();

        db.Patches.AddRange(
            new Patch
            {
                Version = "15.1",
                ReleaseDate = DateTime.UtcNow.AddDays(-21),
                DetectedAt = DateTime.UtcNow.AddDays(-21),
                IsActive = false
            },
            new Patch
            {
                Version = "15.2",
                ReleaseDate = DateTime.UtcNow.AddDays(-2),
                DetectedAt = DateTime.UtcNow.AddDays(-2),
                IsActive = true
            });

        SeedMatch(db, "15.1", "NA1_1", summonerName: "PreviousPatch", championId: 266, role: "TOP", win: true);
        SeedMatch(db, "15.2", "NA1_2", summonerName: "CurrentPatch", championId: 266, role: "TOP", win: true);
        await db.SaveChangesAsync();

        var service = new ChampionWinRateComputeService(
            db,
            Options.Create(new ChampionAnalyticsComputeOptions
            {
                MinimumGamesRequired = 1,
                EarlyPatchMinimumGamesRequired = 1,
                BootstrapPatchMinimumGamesRequired = 1,
                BootstrapWindowHours = 24,
                ProvisionalWindowHours = 96,
                MaturingWindowHours = 240
            }),
            Options.Create(new TieringOptions()));

        var result = await service.ComputeTierListAsync("TOP", null, null, "15.2", CancellationToken.None);

        result.Should().ContainSingle();
        result[0].Movement.Should().BeNull();
        result[0].PreviousTier.Should().BeNull();
    }

    [Fact]
    public async Task ComputeWinRatesAsync_AggregatesInSqlAndComputesChampionLevelBanRate()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<TranscendenceContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new SqliteCompatibleTranscendenceContext(options);
        await db.Database.EnsureCreatedAsync();

        db.Patches.Add(new Patch
        {
            Version = "15.2",
            ReleaseDate = DateTime.UtcNow.AddDays(-2),
            DetectedAt = DateTime.UtcNow.AddDays(-2),
            IsActive = true
        });

        // Champion 266 played TOP twice (1 win, 1 loss)...
        SeedMatch(db, "15.2", "NA1_A", summonerName: "PlayerA", championId: 266, role: "TOP", win: true);
        SeedMatch(db, "15.2", "NA1_B", summonerName: "PlayerB", championId: 266, role: "TOP", win: false);
        // ...and banned in a third, in-scope match (a different champion was actually played there).
        SeedMatch(db, "15.2", "NA1_C", summonerName: "PlayerC", championId: 64, role: "JUNGLE", win: true);
        await db.SaveChangesAsync();

        var bannedMatch = db.Matches.Single(m => m.MatchId == "NA1_C");
        db.MatchBans.Add(new MatchBan
        {
            MatchId = bannedMatch.Id,
            Match = bannedMatch,
            TeamId = 100,
            PickTurn = 1,
            ChampionId = 266
        });
        await db.SaveChangesAsync();

        var service = new ChampionWinRateComputeService(
            db,
            Options.Create(new ChampionAnalyticsComputeOptions
            {
                MinimumGamesRequired = 1,
                EarlyPatchMinimumGamesRequired = 1,
                BootstrapPatchMinimumGamesRequired = 1,
                BootstrapWindowHours = 24,
                ProvisionalWindowHours = 96,
                MaturingWindowHours = 240
            }),
            Options.Create(new TieringOptions()));

        var result = await service.ComputeWinRatesAsync(
            266, new ChampionAnalyticsFilter(), "15.2", CancellationToken.None);

        // The (role, tier) aggregation is now done in SQL; only the grouped row comes back.
        result.Should().ContainSingle();
        var row = result[0];
        row.Role.Should().Be("TOP");
        row.Games.Should().Be(2);
        row.Wins.Should().Be(1);
        row.WinRate.Should().BeApproximately(0.5, 0.0001);
        row.PickRate.Should().BeApproximately(1.0, 0.0001);
        // P7.1: champion-level ban rate = 1 banned match / 3 matches in scope. The old per-group
        // played-and-banned intersection was structurally ~0, so this asserts the fix (non-zero).
        row.BanRate.Should().BeApproximately(1.0 / 3.0, 0.0001);
    }

    [Fact]
    public async Task ComputeWinRatesAsync_PickRateIsShareOfAllPicksInRoleTier_NotChampionRoleShare()
    {
        // P7.1: pick rate must be this champion's games in a (role, tier) over ALL champions' games in
        // that same (role, tier) — not the champion's own role distribution. Seed two TOP champions so
        // the two semantics diverge: champ 266 plays TOP twice, champ 999 plays TOP eight times. The
        // true pick rate for 266 is 2/10 = 0.2; the old role-share denominator (266's own 2 games)
        // would have read 2/2 = 1.0.
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<TranscendenceContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new SqliteCompatibleTranscendenceContext(options);
        await db.Database.EnsureCreatedAsync();

        db.Patches.Add(new Patch
        {
            Version = "15.2",
            ReleaseDate = DateTime.UtcNow.AddDays(-2),
            DetectedAt = DateTime.UtcNow.AddDays(-2),
            IsActive = true
        });

        SeedMatch(db, "15.2", "NA1_266_1", summonerName: "P266a", championId: 266, role: "TOP", win: true);
        SeedMatch(db, "15.2", "NA1_266_2", summonerName: "P266b", championId: 266, role: "TOP", win: false);
        for (var i = 0; i < 8; i++)
        {
            SeedMatch(db, "15.2", $"NA1_999_{i}", summonerName: $"P999_{i}", championId: 999, role: "TOP", win: i % 2 == 0);
        }
        await db.SaveChangesAsync();

        var service = new ChampionWinRateComputeService(
            db,
            Options.Create(new ChampionAnalyticsComputeOptions
            {
                MinimumGamesRequired = 1,
                EarlyPatchMinimumGamesRequired = 1,
                BootstrapPatchMinimumGamesRequired = 1,
                BootstrapWindowHours = 24,
                ProvisionalWindowHours = 96,
                MaturingWindowHours = 240
            }),
            Options.Create(new TieringOptions()));

        var result = await service.ComputeWinRatesAsync(
            266, new ChampionAnalyticsFilter(), "15.2", CancellationToken.None);

        result.Should().ContainSingle();
        var row = result[0];
        row.Role.Should().Be("TOP");
        row.Games.Should().Be(2);
        // 2 of the 10 TOP picks in scope — a true pick rate, not the champion's own role share (1.0).
        row.PickRate.Should().BeApproximately(0.2, 0.0001);
    }

    [Fact]
    public async Task ComputeWinRatesAsync_RoleRankAndPopulation_RankChampionsByWinRate()
    {
        // Role rank/population is computed for every (role, tier) in one batched query (was an N+1 that
        // re-scanned the whole (role, tier) population per result row). Three EMERALD TOP champions with
        // distinct win rates (100% > 67% > 33%) → the queried champion (200, 67%) ranks #2 of 3.
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TranscendenceContext>().UseSqlite(connection).Options;
        await using var db = new SqliteCompatibleTranscendenceContext(options);
        await db.Database.EnsureCreatedAsync();

        db.Patches.Add(new Patch
        {
            Version = "15.2",
            ReleaseDate = DateTime.UtcNow.AddDays(-2),
            DetectedAt = DateTime.UtcNow.AddDays(-2),
            IsActive = true
        });

        void SeedRanked(int champ, int idx, bool win) =>
            SeedMatch(db, "15.2", $"NA1_{champ}_{idx}", summonerName: $"P{champ}_{idx}",
                championId: champ, role: "TOP", win: win, rankTier: "EMERALD");

        // 100: 3-0 (100%), 200: 2-1 (67%), 300: 1-2 (33%)
        SeedRanked(100, 0, true); SeedRanked(100, 1, true); SeedRanked(100, 2, true);
        SeedRanked(200, 0, true); SeedRanked(200, 1, true); SeedRanked(200, 2, false);
        SeedRanked(300, 0, true); SeedRanked(300, 1, false); SeedRanked(300, 2, false);
        await db.SaveChangesAsync();

        var service = new ChampionWinRateComputeService(
            db,
            Options.Create(new ChampionAnalyticsComputeOptions
            {
                MinimumGamesRequired = 1,
                EarlyPatchMinimumGamesRequired = 1,
                BootstrapPatchMinimumGamesRequired = 1,
                BootstrapWindowHours = 24,
                ProvisionalWindowHours = 96,
                MaturingWindowHours = 240
            }),
            Options.Create(new TieringOptions()));

        var result = await service.ComputeWinRatesAsync(
            200, new ChampionAnalyticsFilter { RankTier = "EMERALD_PLUS" }, "15.2", CancellationToken.None);

        var row = result.Single(r => r is { Role: "TOP", RankTier: "EMERALD" });
        row.RoleRank.Should().Be(2);        // 2nd of 3 by win rate
        row.RolePopulation.Should().Be(3);
        row.WinRate.Should().BeApproximately(2.0 / 3.0, 0.0001);
    }

    [Fact]
    public async Task ComputeProChampionPlayrateAsync_RanksChampionsAndHonorsScope()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<TranscendenceContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new SqliteCompatibleTranscendenceContext(options);
        await db.Database.EnsureCreatedAsync();

        db.Patches.Add(new Patch
        {
            Version = "15.2",
            ReleaseDate = DateTime.UtcNow.AddDays(-2),
            DetectedAt = DateTime.UtcNow.AddDays(-2),
            IsActive = true
        });

        // One official pro and one auto-discovered high-elo one-trick.
        db.TrackedProSummoners.Add(new TrackedProSummoner
        {
            Id = Guid.NewGuid(),
            Puuid = "pro-puuid",
            PlatformRegion = "NA1",
            ProName = "Faker",
            TeamName = "T1",
            IsPro = true,
            IsHighEloOtp = false,
            IsActive = true
        });
        db.TrackedProSummoners.Add(new TrackedProSummoner
        {
            Id = Guid.NewGuid(),
            Puuid = "otp-puuid",
            PlatformRegion = "NA1",
            IsPro = false,
            IsHighEloOtp = true,
            IsActive = true
        });

        // Pro: champion 266 twice (1W/1L). OTP: champion 64 once (W).
        var pro = SeedProSummoner(db, "pro-puuid", "Faker");
        var otp = SeedProSummoner(db, "otp-puuid", "Otp");
        SeedParticipantMatch(db, "15.2", "NA1_1", pro, championId: 266, role: "TOP", win: true);
        SeedParticipantMatch(db, "15.2", "NA1_2", pro, championId: 266, role: "TOP", win: false);
        SeedParticipantMatch(db, "15.2", "NA1_3", otp, championId: 64, role: "JUNGLE", win: true);
        await db.SaveChangesAsync();

        var service = CreateProService(db);

        var proOnly = await service.ComputeProChampionPlayrateAsync(null, "pro", "15.2", CancellationToken.None);
        proOnly.Champions.Should().ContainSingle();
        proOnly.Champions[0].ChampionId.Should().Be(266);
        proOnly.Champions[0].Games.Should().Be(2);
        proOnly.Champions[0].Wins.Should().Be(1);
        proOnly.Champions[0].WinRate.Should().BeApproximately(0.5, 0.0001);
        proOnly.Champions[0].UniquePlayers.Should().Be(1);

        var highEloOnly = await service.ComputeProChampionPlayrateAsync(null, "highelo", "15.2", CancellationToken.None);
        highEloOnly.Champions.Should().ContainSingle();
        highEloOnly.Champions[0].ChampionId.Should().Be(64);

        var all = await service.ComputeProChampionPlayrateAsync(null, "all", "15.2", CancellationToken.None);
        all.Champions.Select(c => c.ChampionId).Should().BeEquivalentTo(new[] { 266, 64 });
        all.Champions[0].ChampionId.Should().Be(266); // most games first
    }

    [Fact]
    public async Task ComputeProBuildsAsync_HonorsRosterScope()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<TranscendenceContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new SqliteCompatibleTranscendenceContext(options);
        await db.Database.EnsureCreatedAsync();

        db.Patches.Add(new Patch
        {
            Version = "15.2",
            ReleaseDate = DateTime.UtcNow.AddDays(-2),
            DetectedAt = DateTime.UtcNow.AddDays(-2),
            IsActive = true
        });

        db.TrackedProSummoners.Add(new TrackedProSummoner
        {
            Id = Guid.NewGuid(),
            Puuid = "pro-puuid",
            PlatformRegion = "NA1",
            ProName = "Pro",
            TeamName = "Team",
            IsPro = true,
            IsHighEloOtp = false,
            IsActive = true
        });
        db.TrackedProSummoners.Add(new TrackedProSummoner
        {
            Id = Guid.NewGuid(),
            Puuid = "otp-puuid",
            PlatformRegion = "NA1",
            IsPro = false,
            IsHighEloOtp = true,
            IsActive = true
        });

        var pro = SeedProSummoner(db, "pro-puuid", "Pro");
        var otp = SeedProSummoner(db, "otp-puuid", "Otp");
        SeedParticipantMatch(db, "15.2", "NA1_1", pro, championId: 266, role: "TOP", win: true);
        SeedParticipantMatch(db, "15.2", "NA1_2", otp, championId: 266, role: "TOP", win: true);
        await db.SaveChangesAsync();

        var service = CreateProService(db);

        var proOnly = await service.ComputeProBuildsAsync(266, null, "TOP", "pro", "15.2", CancellationToken.None);
        proOnly.Scope.Should().Be("pro");
        proOnly.RecentProMatches.Should().ContainSingle();
        proOnly.TopPlayers.Should().ContainSingle(p => p.PlayerName == "Pro");

        var highEloOnly = await service.ComputeProBuildsAsync(266, null, "TOP", "highelo", "15.2", CancellationToken.None);
        highEloOnly.Scope.Should().Be("highelo");
        highEloOnly.RecentProMatches.Should().ContainSingle();
        highEloOnly.TopPlayers.Should().ContainSingle(p => p.PlayerName == "Otp#NA1");

        var all = await service.ComputeProBuildsAsync(266, null, "TOP", "all", "15.2", CancellationToken.None);
        all.Scope.Should().Be("all");
        all.RecentProMatches.Should().HaveCount(2);
        all.TopPlayers.Select(p => p.PlayerName).Should().BeEquivalentTo(new[] { "Pro", "Otp#NA1" });
    }

    [Fact]
    public async Task ComputeBuildsAsync_PopulatesSectionedTimingAwareBuildPath()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<TranscendenceContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new SqliteCompatibleTranscendenceContext(options);
        await db.Database.EnsureCreatedAsync();

        db.Patches.Add(new Patch
        {
            Version = "15.2",
            ReleaseDate = DateTime.UtcNow.AddDays(-20),
            DetectedAt = DateTime.UtcNow.AddDays(-20),
            IsActive = true
        });
        await db.SaveChangesAsync();

        const int championId = 100;
        const string role = "MIDDLE";

        // Final-inventory items need backing ItemVersion rows (FK + completed-item classification).
        foreach (var itemId in new[] { 6672, 3031, 3072, 6694 })
        {
            db.ItemVersions.Add(new ItemVersion
            {
                ItemId = itemId,
                PatchVersion = "15.2",
                Name = $"Item {itemId}",
                Tags = ["Damage"],
                BuildsFrom = [1038],
                BuildsInto = [],
                InStore = true,
                PriceTotal = 3000
            });
        }

        var purchases = new (int ItemId, int TimestampMs, BuildItemCategory Category)[]
        {
            (1055, 5_000, BuildItemCategory.Starter),       // Doran's Blade
            (6672, 600_000, BuildItemCategory.Legendary),   // Kraken Slayer @ 10:00 (1st core)
            (3006, 700_000, BuildItemCategory.Boots),       // Berserker's Greaves
            (3031, 900_000, BuildItemCategory.Legendary),   // Infinity Edge (2nd core)
            (3072, 1_200_000, BuildItemCategory.Legendary), // Bloodthirster (3rd core)
            (6694, 1_500_000, BuildItemCategory.Legendary)  // Lord Dominik's (4th / situational)
        };

        for (var i = 0; i < 10; i++)
        {
            SeedBuildParticipant(
                db,
                patch: "15.2",
                matchId: $"NA1_BUILD_{i}",
                championId: championId,
                role: role,
                win: i < 6,
                spell1: 4,
                spell2: 12,
                finalItems: [6672, 3031, 3072, 6694],
                purchases: purchases,
                firstThree: "QWE",
                maxOrder: "Q>E>W");
        }

        await db.SaveChangesAsync();

        var service = CreateBuildService(db);

        var response = await service.ComputeBuildsAsync(championId, role, rankTier: null, region: null, patch: "15.2", CancellationToken.None);

        response.Builds.Should().NotBeEmpty();

        response.SummonerSpells.Should().NotBeNull();
        response.SummonerSpells!.Should().ContainSingle();
        response.SummonerSpells![0].Spell1Id.Should().Be(4);
        response.SummonerSpells![0].Spell2Id.Should().Be(12);
        response.SummonerSpells![0].Games.Should().Be(10);

        response.SkillOrder.Should().NotBeNull();
        response.SkillOrder!.FirstThree.Should().Be("QWE");
        response.SkillOrder!.MaxOrder.Should().Be("Q>E>W");
        response.SkillOrder!.Games.Should().Be(10);
        response.SkillOrder!.WinRate.Should().BeApproximately(0.6, 0.0001);

        response.StartingItems.Should().NotBeNull();
        response.StartingItems!.Should().ContainSingle();
        response.StartingItems![0].Items.Should().Equal(1055);

        response.Boots.Should().NotBeNull();
        response.Boots!.Select(b => b.ItemId).Should().Contain(3006);

        response.CoreBuildPath.Should().NotBeNull();
        response.CoreBuildPath!.Select(c => c.ItemId).Should().Equal(6672, 3031, 3072);
        response.CoreBuildPath![0].AvgCompletionMinute.Should().BeApproximately(10.0, 0.01);

        response.SituationalSlots.Should().NotBeNull();
        response.SituationalSlots!.Should().ContainSingle();
        response.SituationalSlots![0].Slot.Should().Be(4);
        response.SituationalSlots![0].Options.Select(o => o.ItemId).Should().Contain(6694);
    }

    private static ChampionProComputeService CreateProService(TranscendenceContext db) =>
        new(
            db,
            Options.Create(new ChampionAnalyticsComputeOptions
            {
                MinimumGamesRequired = 1,
                EarlyPatchMinimumGamesRequired = 1,
                BootstrapPatchMinimumGamesRequired = 1,
                BootstrapWindowHours = 24,
                ProvisionalWindowHours = 96,
                MaturingWindowHours = 240
            }));

    private static ChampionBuildComputeService CreateBuildService(TranscendenceContext db) =>
        new(
            db,
            Options.Create(new ChampionAnalyticsComputeOptions
            {
                MinimumGamesRequired = 1,
                EarlyPatchMinimumGamesRequired = 1,
                BootstrapPatchMinimumGamesRequired = 1,
                BootstrapWindowHours = 24,
                ProvisionalWindowHours = 96,
                MaturingWindowHours = 240
            }),
            NullLogger<ChampionBuildComputeService>.Instance);

    private static Summoner SeedProSummoner(TranscendenceContext db, string puuid, string name, string platform = "NA1")
    {
        var summoner = new Summoner
        {
            Id = Guid.NewGuid(),
            PlatformRegion = platform,
            Region = "americas",
            GameName = name,
            TagLine = platform,
            Puuid = puuid,
            SummonerName = name,
            RiotSummonerId = Guid.NewGuid().ToString("N")
        };
        db.Summoners.Add(summoner);
        return summoner;
    }

    private static void SeedParticipantMatch(
        TranscendenceContext db,
        string patch,
        string matchId,
        Summoner summoner,
        int championId,
        string role,
        bool win)
    {
        var match = new Transcendence.Data.Models.LoL.Match.Match
        {
            Id = Guid.NewGuid(),
            MatchId = matchId,
            MatchDate = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Duration = 1800,
            Patch = patch,
            QueueId = 420,
            QueueFamily = "RANKED_SOLO_DUO",
            QueueType = "420",
            Status = FetchStatus.Success,
            PlatformRegion = summoner.PlatformRegion,
            FetchedAt = DateTime.UtcNow
        };

        var participant = new MatchParticipant
        {
            Id = Guid.NewGuid(),
            MatchId = match.Id,
            Match = match,
            SummonerId = summoner.Id,
            Summoner = summoner,
            Puuid = summoner.Puuid,
            ParticipantId = 1,
            TeamId = 100,
            ChampionId = championId,
            TeamPosition = role,
            Win = win
        };

        db.Matches.Add(match);
        db.MatchParticipants.Add(participant);
    }

    private static void SeedMatch(
        TranscendenceContext db,
        string patch,
        string matchId,
        string summonerName,
        int championId,
        string role,
        bool win,
        string? rankTier = null)
    {
        var summoner = new Summoner
        {
            Id = Guid.NewGuid(),
            PlatformRegion = "NA1",
            Region = "americas",
            GameName = summonerName,
            TagLine = "NA1",
            Puuid = Guid.NewGuid().ToString("N"),
            SummonerName = summonerName,
            RiotSummonerId = Guid.NewGuid().ToString("N")
        };

        if (rankTier != null)
        {
            db.Ranks.Add(new Rank
            {
                Id = Guid.NewGuid(),
                SummonerId = summoner.Id,
                QueueType = "RANKED_SOLO_5x5",
                Tier = rankTier
            });
        }

        var match = new Transcendence.Data.Models.LoL.Match.Match
        {
            Id = Guid.NewGuid(),
            MatchId = matchId,
            MatchDate = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Duration = 1800,
            Patch = patch,
            QueueId = 420,
            QueueFamily = "RANKED_SOLO_DUO",
            QueueType = "420",
            Status = FetchStatus.Success,
            PlatformRegion = "NA1",
            FetchedAt = DateTime.UtcNow
        };

        var participant = new MatchParticipant
        {
            Id = Guid.NewGuid(),
            MatchId = match.Id,
            Match = match,
            SummonerId = summoner.Id,
            Summoner = summoner,
            Puuid = summoner.Puuid,
            ParticipantId = 1,
            TeamId = 100,
            ChampionId = championId,
            TeamPosition = role,
            Win = win
        };

        db.Summoners.Add(summoner);
        db.Matches.Add(match);
        db.MatchParticipants.Add(participant);
    }

    private static void SeedBuildParticipant(
        TranscendenceContext db,
        string patch,
        string matchId,
        int championId,
        string role,
        bool win,
        int spell1,
        int spell2,
        int[] finalItems,
        (int ItemId, int TimestampMs, BuildItemCategory Category)[] purchases,
        string firstThree,
        string maxOrder)
    {
        var summoner = new Summoner
        {
            Id = Guid.NewGuid(),
            PlatformRegion = "NA1",
            Region = "americas",
            GameName = matchId,
            TagLine = "NA1",
            Puuid = Guid.NewGuid().ToString("N"),
            SummonerName = matchId,
            RiotSummonerId = Guid.NewGuid().ToString("N")
        };

        var match = new Transcendence.Data.Models.LoL.Match.Match
        {
            Id = Guid.NewGuid(),
            MatchId = matchId,
            MatchDate = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Duration = 1800,
            Patch = patch,
            QueueId = 420,
            QueueFamily = "RANKED_SOLO_DUO",
            QueueType = "420",
            Status = FetchStatus.Success,
            PlatformRegion = "NA1",
            FetchedAt = DateTime.UtcNow
        };

        var participant = new MatchParticipant
        {
            Id = Guid.NewGuid(),
            MatchId = match.Id,
            Match = match,
            SummonerId = summoner.Id,
            Summoner = summoner,
            Puuid = summoner.Puuid,
            ParticipantId = 1,
            TeamId = 100,
            ChampionId = championId,
            TeamPosition = role,
            Win = win,
            SummonerSpell1Id = spell1,
            SummonerSpell2Id = spell2
        };

        db.Summoners.Add(summoner);
        db.Matches.Add(match);
        db.MatchParticipants.Add(participant);

        for (var slot = 0; slot < finalItems.Length; slot++)
        {
            db.MatchParticipantItems.Add(new MatchParticipantItem
            {
                MatchParticipantId = participant.Id,
                SlotIndex = slot,
                ItemId = finalItems[slot],
                PatchVersion = patch
            });
        }

        for (var index = 0; index < purchases.Length; index++)
        {
            db.MatchParticipantItemPurchases.Add(new MatchParticipantItemPurchase
            {
                MatchId = match.Id,
                Match = match,
                ParticipantId = 1,
                PurchaseIndex = index,
                ItemId = purchases[index].ItemId,
                TimestampMs = purchases[index].TimestampMs,
                Category = purchases[index].Category
            });
        }

        db.MatchParticipantSkillOrders.Add(new MatchParticipantSkillOrder
        {
            MatchId = match.Id,
            Match = match,
            ParticipantId = 1,
            Sequence = firstThree,
            FirstThree = firstThree,
            MaxOrder = maxOrder
        });
    }
}
