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

/// <summary>
/// Builds are precomputed as a durable response cache (the refresh persists the exact live
/// <c>ComputeBuildsAsync</c> output), so the gate is: a read served from the snapshot equals the live
/// compute (the serialize → store → deserialize round-trip is exact), and a specific tier/region falls back
/// to raw. Identical builds are seeded so the (otherwise non-deterministic) representative pick is unambiguous.
/// </summary>
public class ChampionBuildSnapshotTests
{
    private const string Patch = "15.2";
    private const int ChampionId = 100;
    private const string Role = "MIDDLE";

    [Fact]
    public async Task BuildsFromStats_EqualLiveCompute_ForPrecomputedScopes_AndFallBackOtherwise()
    {
        await using var ctx = await SeededAsync();
        var svc = Service(ctx.Db);
        var refresher = new PrecomputedAnalyticsRefresher(ctx.Db, svc, ProService(ctx.Db), Options.Create(new TieringOptions()), NullLogger<PrecomputedAnalyticsRefresher>.Instance);

        await refresher.RefreshTabularCoreAsync(Patch, CancellationToken.None);
        var snapshots = await refresher.RefreshBuildsAsync(Patch, CancellationToken.None);

        // One played (champion, role) x 2 precomputed scopes.
        snapshots.Should().Be(2);

        // ALL scope: participants are UNRANKED so this carries the real build — the rich round-trip case.
        var rawAll = await svc.ComputeBuildsAsync(ChampionId, Role, null, null, Patch, CancellationToken.None);
        var statsAll = await svc.ComputeBuildsFromStatsAsync(ChampionId, Role, null, null, Patch, CancellationToken.None);
        statsAll.Should().BeEquivalentTo(rawAll, "the snapshot is the live compute's own serialized output");
        statsAll.Builds.Should().NotBeEmpty("the ALL scope includes the unranked participants");

        // EMERALD_PLUS scope: no emerald+ participants → empty, and still equal to the live compute.
        var rawEm = await svc.ComputeBuildsAsync(ChampionId, Role, "EMERALD_PLUS", null, Patch, CancellationToken.None);
        var statsEm = await svc.ComputeBuildsFromStatsAsync(ChampionId, Role, "EMERALD_PLUS", null, Patch, CancellationToken.None);
        statsEm.Should().BeEquivalentTo(rawEm);

        // A specific tier is not precomputed → falls back to the live compute.
        var rawDia = await svc.ComputeBuildsAsync(ChampionId, Role, "DIAMOND", null, Patch, CancellationToken.None);
        var statsDia = await svc.ComputeBuildsFromStatsAsync(ChampionId, Role, "DIAMOND", null, Patch, CancellationToken.None);
        statsDia.Should().BeEquivalentTo(rawDia);

        // A specific region is not precomputed → falls back to the live compute.
        var rawNa = await svc.ComputeBuildsAsync(ChampionId, Role, null, "NA1", Patch, CancellationToken.None);
        var statsNa = await svc.ComputeBuildsFromStatsAsync(ChampionId, Role, null, "NA1", Patch, CancellationToken.None);
        statsNa.Should().BeEquivalentTo(rawNa);
    }

    [Fact]
    public async Task BuildsFromStats_FallsBackToLiveCompute_WhenNoSnapshot()
    {
        await using var ctx = await SeededAsync();
        var svc = Service(ctx.Db);
        // No refresh run → no snapshots.
        var raw = await svc.ComputeBuildsAsync(ChampionId, Role, null, null, Patch, CancellationToken.None);
        var stats = await svc.ComputeBuildsFromStatsAsync(ChampionId, Role, null, null, Patch, CancellationToken.None);
        stats.Should().BeEquivalentTo(raw);
    }

    private static ChampionBuildComputeService Service(TranscendenceContext db) =>
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
            NullLogger<ChampionBuildComputeService>.Instance);

    private static ChampionProComputeService ProService(TranscendenceContext db) =>
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

    private static async Task<SeededContext> SeededAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TranscendenceContext>().UseSqlite(connection).Options;
        var db = new SqliteCompatibleTranscendenceContext(options);
        await db.Database.EnsureCreatedAsync();

        db.Patches.Add(new Patch { Version = Patch, ReleaseDate = DateTime.UtcNow.AddDays(-20), DetectedAt = DateTime.UtcNow.AddDays(-20), IsActive = true });
        foreach (var itemId in new[] { 6672, 3031, 3072, 6694 })
            db.ItemVersions.Add(new ItemVersion { ItemId = itemId, PatchVersion = Patch, Name = $"Item {itemId}", Tags = ["Damage"], BuildsFrom = [1038], BuildsInto = [], InStore = true, PriceTotal = 3000 });

        var purchases = new (int ItemId, int TimestampMs, BuildItemCategory Category)[]
        {
            (1055, 5_000, BuildItemCategory.Starter),
            (6672, 600_000, BuildItemCategory.Legendary),
            (3006, 700_000, BuildItemCategory.Boots),
            (3031, 900_000, BuildItemCategory.Legendary),
            (3072, 1_200_000, BuildItemCategory.Legendary),
            (6694, 1_500_000, BuildItemCategory.Legendary)
        };

        // 30 identical builds (= MinBuildGames), so the (champion, role) pair is precomputed and the
        // dominant build is unambiguous (no ties → deterministic representative).
        for (var i = 0; i < 30; i++)
            SeedBuild(db, $"NA1_B{i}", win: i < 18, finalItems: [6672, 3031, 3072, 6694], purchases: purchases);

        await db.SaveChangesAsync();
        return new SeededContext(connection, db);
    }

    private static void SeedBuild(
        TranscendenceContext db, string matchId, bool win, int[] finalItems,
        (int ItemId, int TimestampMs, BuildItemCategory Category)[] purchases)
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
            Patch = Patch,
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
            ChampionId = ChampionId,
            TeamPosition = Role,
            Win = win,
            SummonerSpell1Id = 4,
            SummonerSpell2Id = 12
        };

        db.Summoners.Add(summoner);
        db.Matches.Add(match);
        db.MatchParticipants.Add(participant);

        for (var slot = 0; slot < finalItems.Length; slot++)
            db.MatchParticipantItems.Add(new MatchParticipantItem { MatchParticipantId = participant.Id, SlotIndex = slot, ItemId = finalItems[slot], PatchVersion = Patch });

        for (var index = 0; index < purchases.Length; index++)
            db.MatchParticipantItemPurchases.Add(new MatchParticipantItemPurchase { MatchId = match.Id, Match = match, ParticipantId = 1, PurchaseIndex = index, ItemId = purchases[index].ItemId, TimestampMs = purchases[index].TimestampMs, Category = purchases[index].Category });

        db.MatchParticipantSkillOrders.Add(new MatchParticipantSkillOrder { MatchId = match.Id, Match = match, ParticipantId = 1, Sequence = "QWE", FirstThree = "QWE", MaxOrder = "Q>E>W" });
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
