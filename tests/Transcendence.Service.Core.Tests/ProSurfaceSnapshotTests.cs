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
/// Pro-builds + pro-playrate are precomputed as a durable response cache (the refresh persists the exact
/// live compute output), so the gate is: a read served from the snapshot equals the live compute (the
/// serialize → store → deserialize round-trip is exact), and a specific region falls back to raw.
/// </summary>
public class ProSurfaceSnapshotTests
{
    private const string Patch = "15.2";

    [Fact]
    public async Task ProSurfacesFromStats_EqualLiveCompute_AndFallBackForSpecificRegion()
    {
        await using var ctx = await SeededAsync();
        var pro = ProService(ctx.Db);
        var refresher = new PrecomputedAnalyticsRefresher(ctx.Db, BuildService(ctx.Db), pro, Options.Create(new TieringOptions()), NullLogger<PrecomputedAnalyticsRefresher>.Instance);

        var snapshots = await refresher.RefreshProSurfacesAsync(Patch, CancellationToken.None);
        // pro-playrate (3 scopes) + pro-builds for (266, TOP) x 3 scopes.
        snapshots.Should().Be(6);

        // pro-playrate: snapshot read == live compute, for each scope.
        foreach (var scope in new[] { "all", "pro", "highelo" })
        {
            var raw = await pro.ComputeProChampionPlayrateAsync(null, scope, Patch, CancellationToken.None);
            var stats = await pro.ComputeProChampionPlayrateFromStatsAsync(null, scope, Patch, CancellationToken.None);
            stats.Should().BeEquivalentTo(raw, $"pro-playrate scope={scope}");
        }

        // pro-builds: snapshot read == live compute, for each scope.
        foreach (var scope in new[] { "all", "pro", "highelo" })
        {
            var raw = await pro.ComputeProBuildsAsync(266, null, "TOP", scope, Patch, CancellationToken.None);
            var stats = await pro.ComputeProBuildsFromStatsAsync(266, null, "TOP", scope, Patch, CancellationToken.None);
            stats.Should().BeEquivalentTo(raw, $"pro-builds scope={scope}");
            raw.CommonBuilds.Should().OnlyContain(build => build.Items.Count > 0);
        }

        var allBuilds = await pro.ComputeProBuildsAsync(266, null, "TOP", "all", Patch, CancellationToken.None);
        allBuilds.CommonBuilds.Should().ContainSingle();
        allBuilds.CommonBuilds[0].Items.Should().Equal(3078);

        // A specific region is not precomputed → falls back to live compute.
        var rawNa = await pro.ComputeProBuildsAsync(266, "NA1", "TOP", "all", Patch, CancellationToken.None);
        var statsNa = await pro.ComputeProBuildsFromStatsAsync(266, "NA1", "TOP", "all", Patch, CancellationToken.None);
        statsNa.Should().BeEquivalentTo(rawNa);

        var rawPlNa = await pro.ComputeProChampionPlayrateAsync("NA1", "all", Patch, CancellationToken.None);
        var statsPlNa = await pro.ComputeProChampionPlayrateFromStatsAsync("NA1", "all", Patch, CancellationToken.None);
        statsPlNa.Should().BeEquivalentTo(rawPlNa);
    }

    [Fact]
    public async Task ProSurfacesFromStats_FallBackToLive_WhenNoSnapshot()
    {
        await using var ctx = await SeededAsync();
        var pro = ProService(ctx.Db);
        // No refresh → no snapshots.
        var raw = await pro.ComputeProBuildsAsync(266, null, "TOP", "all", Patch, CancellationToken.None);
        var stats = await pro.ComputeProBuildsFromStatsAsync(266, null, "TOP", "all", Patch, CancellationToken.None);
        stats.Should().BeEquivalentTo(raw);
    }

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

    private static ChampionBuildComputeService BuildService(TranscendenceContext db) =>
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

    private static async Task<SeededContext> SeededAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TranscendenceContext>().UseSqlite(connection).Options;
        var db = new SqliteCompatibleTranscendenceContext(options);
        await db.Database.EnsureCreatedAsync();

        db.Patches.Add(new Patch { Version = Patch, ReleaseDate = DateTime.UtcNow.AddDays(-2), DetectedAt = DateTime.UtcNow.AddDays(-2), IsActive = true });
        db.ItemVersions.Add(new ItemVersion
        {
            ItemId = 3078,
            PatchVersion = Patch,
            Name = "Trinity Force",
            PriceTotal = 3333
        });

        db.TrackedProSummoners.Add(new TrackedProSummoner { Id = Guid.NewGuid(), Puuid = "pro-puuid", PlatformRegion = "NA1", ProName = "Pro", TeamName = "Team", IsPro = true, IsHighEloOtp = false, IsActive = true });
        db.TrackedProSummoners.Add(new TrackedProSummoner { Id = Guid.NewGuid(), Puuid = "otp-puuid", PlatformRegion = "NA1", IsPro = false, IsHighEloOtp = true, IsActive = true });

        SeedProMatch(db, "pro-puuid", "Pro", "NA1_PRO", championId: 266, role: "TOP", matchDate: 2_000, finalItems: [3078]);
        SeedProMatch(db, "otp-puuid", "Otp", "NA1_OTP", championId: 266, role: "TOP", matchDate: 1_000, finalItems: []);

        await db.SaveChangesAsync();
        return new SeededContext(connection, db);
    }

    private static void SeedProMatch(
        TranscendenceContext db,
        string puuid,
        string name,
        string matchId,
        int championId,
        string role,
        long matchDate,
        IReadOnlyList<int> finalItems)
    {
        var summoner = new Summoner
        {
            Id = Guid.NewGuid(),
            PlatformRegion = "NA1",
            Region = "americas",
            GameName = name,
            TagLine = "NA1",
            Puuid = puuid,
            SummonerName = name,
            RiotSummonerId = Guid.NewGuid().ToString("N")
        };
        var match = new Transcendence.Data.Models.LoL.Match.Match
        {
            Id = Guid.NewGuid(),
            MatchId = matchId,
            MatchDate = matchDate,
            Duration = 1800,
            Patch = Patch,
            QueueId = 420,
            QueueFamily = "RANKED_SOLO_DUO",
            QueueType = "420",
            Status = FetchStatus.Success,
            PlatformRegion = "NA1",
            FetchedAt = DateTime.UtcNow
        };
        db.Summoners.Add(summoner);
        db.Matches.Add(match);
        var participant = new MatchParticipant
        {
            Id = Guid.NewGuid(),
            MatchId = match.Id,
            Match = match,
            SummonerId = summoner.Id,
            Summoner = summoner,
            Puuid = puuid,
            ParticipantId = 1,
            TeamId = 100,
            ChampionId = championId,
            TeamPosition = role,
            Win = true,
            SummonerSpell1Id = 4,
            SummonerSpell2Id = 12
        };
        db.MatchParticipants.Add(participant);

        for (var slot = 0; slot < finalItems.Count; slot++)
        {
            db.MatchParticipantItems.Add(new MatchParticipantItem
            {
                MatchParticipantId = participant.Id,
                MatchParticipant = participant,
                SlotIndex = slot,
                ItemId = finalItems[slot],
                PatchVersion = Patch
            });
        }
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
