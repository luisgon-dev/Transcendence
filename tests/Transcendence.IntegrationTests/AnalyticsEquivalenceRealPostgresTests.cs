using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Account;
using Transcendence.Data.Models.LoL.Analytics;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Data.Models.LoL.Static;
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

    [Fact]
    public async Task AramTierList_StatsPath_EqualsRawCompute_OnRealPostgres()
    {
        var patch = UniquePatch();
        await using var db = NewDb();
        await SeedAsync(db, patch);
        await RefreshAsync(db, patch);
        var svc = WinRateService(db);

        (await db.ChampionRoleTierStats.CountAsync(x => x.Patch == patch && x.QueueFamily == "ARAM"))
            .Should().BeGreaterThan(0, "the queue-aware refresher must materialize ARAM atoms");

        var raw = await svc.ComputeTierListAsync(null, "EMERALD_PLUS", "NA1", "ARAM", patch, CancellationToken.None);
        var stats = await svc.ComputeTierListFromStatsAsync(null, "EMERALD_PLUS", "NA1", "ARAM", patch, CancellationToken.None);

        raw.Should().NotBeEmpty();
        stats.Should().BeEquivalentTo(raw, options => options.WithStrictOrdering()
            .Excluding(row => row.Movement).Excluding(row => row.PreviousTier));
        stats.Should().OnlyContain(row => row.Role == "ALL");
    }

    [Fact]
    public async Task BuildAtlas_GenerationReadPath_PreservesExactCountsAcrossIncrementalPromotion()
    {
        var patch = UniquePatch();
        await using var db = NewDb();
        db.Patches.Add(new Patch
        {
            Version = patch,
            ReleaseDate = DateTime.UtcNow,
            DetectedAt = DateTime.UtcNow,
            IsActive = false
        });
        db.ItemVersions.Add(new ItemVersion
        {
            ItemId = 3078,
            PatchVersion = patch,
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
            PatchVersion = patch,
            Name = "Press the Attack",
            RunePathId = 8000,
            RunePathName = "Precision",
            Slot = 0
        });
        AddBuildResourceGame(db, patch, "build-one", win: true);
        await db.SaveChangesAsync();

        var refresher = new BuildResourceSnapshotRefresher(
            db,
            Options.Create(new BuildResourceSnapshotOptions
            {
                MatchBatchSize = 50,
                CommandTimeoutSeconds = 120
            }),
            NullLogger<BuildResourceSnapshotRefresher>.Instance);
        var first = await refresher.RefreshAsync(patch, forceFullRebuild: true, CancellationToken.None);

        AddBuildResourceGame(db, patch, "build-two", win: false);
        await db.SaveChangesAsync();
        var second = await refresher.RefreshAsync(patch, forceFullRebuild: false, CancellationToken.None);

        second.SnapshotId.Should().NotBe(first.SnapshotId);
        second.ProcessedMatchCount.Should().Be(1);
        var active = await db.BuildResourceSnapshots.AsNoTracking()
            .SingleAsync(snapshot => snapshot.Patch == patch && snapshot.IsActive);
        active.Status.Should().Be(BuildResourceSnapshotStatus.Ready);
        active.ProcessedMatchCount.Should().Be(2);

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();
        serviceCollection.AddHybridCache();
        var services = serviceCollection.BuildServiceProvider();
        await using (services)
        {
            var service = new BuildResourceAnalyticsService(
                db,
                services.GetRequiredService<HybridCache>(),
                new AnalyticsPatchQueryService(db));
            var items = await service.GetItemsAsync("NA1", patch);
            var runes = await service.GetRunesAsync("NA1", patch);

            items.TotalParticipantGames.Should().Be(2);
            var item = items.Entries.Should().ContainSingle().Subject;
            item.Games.Should().Be(2);
            item.Wins.Should().Be(1);
            item.PickRate.Should().Be(1);
            runes.Entries.Should().ContainSingle().Which.Games.Should().Be(2);
        }
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

        AddGames(db, patch, "NA1", "EMERALD", 100, "", wins: 3, losses: 1, queueId: 450, queueFamily: "ARAM");
        AddGames(db, patch, "NA1", "EMERALD", 200, "", wins: 1, losses: 2, queueId: 450, queueFamily: "ARAM");

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
        TranscendenceContext db, string patch, string region, string? tier, int champ, string role, int wins, int losses,
        int queueId = 420, string queueFamily = "RANKED_SOLO_DUO")
    {
        var matches = new List<Match>();
        for (var i = 0; i < wins + losses; i++)
            matches.Add(AddGame(
                db, patch, region, tier, champ, role, win: i < wins,
                queueId: queueId, queueFamily: queueFamily));
        return matches;
    }

    private static Match AddGame(
        TranscendenceContext db, string patch, string region, string? tier, int champ, string role, bool win,
        int queueId, string queueFamily)
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
            QueueId = queueId,
            QueueFamily = queueFamily,
            QueueType = queueId.ToString(),
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

    private static void AddBuildResourceGame(
        TranscendenceContext db,
        string patch,
        string matchId,
        bool win)
    {
        var summoner = new Summoner
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
        var match = new Match
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
            ChampionId = 266,
            TeamPosition = "TOP",
            Win = win
        };
        participant.Items.Add(new MatchParticipantItem
        {
            MatchParticipantId = participant.Id,
            SlotIndex = 0,
            ItemId = 3078,
            PatchVersion = patch
        });
        participant.Runes.Add(new MatchParticipantRune
        {
            MatchParticipantId = participant.Id,
            RuneId = 8005,
            PatchVersion = patch,
            SelectionTree = RuneSelectionTree.Primary,
            SelectionIndex = 0,
            StyleId = 8000
        });
        db.Summoners.Add(summoner);
        db.Matches.Add(match);
        db.MatchParticipants.Add(participant);
    }
}
