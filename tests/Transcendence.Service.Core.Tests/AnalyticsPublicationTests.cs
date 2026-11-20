using System.Data.Common;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Analytics;
using Transcendence.Data.Models.LoL.Static;
using Transcendence.Service.Core.Services.Analytics.Implementations;
using Transcendence.Service.Core.Services.Analytics.Interfaces;
using Transcendence.Service.Core.Services.Analytics.Models;
using Transcendence.Service.Core.Services.Jobs.Configuration;
using Transcendence.Service.Core.Services.RiotApi;
using Transcendence.Service.Core.Tests.Support;

namespace Transcendence.Service.Core.Tests;

public class AnalyticsPublicationTests
{
    private const string Previous = "16.13";
    private const string Current = "16.14";
    private const string Solo = QueueCatalog.QueueFamilyRankedSoloDuo;
    private const string Aram = QueueCatalog.QueueFamilyAram;

    [Fact]
    public async Task Rollover_UsesQueueLocalPublicationUntilTheNewScopedRollupCommits()
    {
        await using var harness = await Harness.CreateAsync();
        var previous = AddPatch(harness.Db, Previous, -14, true);
        var current = AddPatch(harness.Db, Current, -1, false);
        AddPublication(harness.Db, Previous, Solo, 41);
        AddPublication(harness.Db, Current, Aram, 50, role: "ALL");
        harness.Db.ChampionScopeGradeStats.Add(Grade(Current, Solo, "ALL", 160));
        harness.Db.ChampionRoleTierStats.AddRange(
            Atom(Previous, Solo, 100, region: "NA1"),
            Atom(Previous, Solo, 200, rank: "IRON"),
            Atom(Previous, Solo, 300, role: "JUNGLE"));
        await harness.Db.SaveChangesAsync();

        (await harness.Publication.ResolveAsync("solo", null, default)).Selection
            .Should().Be(new AnalyticsPatchSelection(AnalyticsPatchSelectionState.Ready, Previous, null, Previous));

        previous.IsActive = false;
        current.IsActive = true;
        await harness.Db.SaveChangesAsync();

        var rollover = await harness.Publication.ResolveAsync("solo", null, default);
        rollover.Selection.Should().Be(new AnalyticsPatchSelection(AnalyticsPatchSelectionState.Ready, Current, null, Previous));
        var tierList = await harness.Analytics.GetPublishedTierListAsync(" top ", "diamond", "kr", "solo", rollover, default);
        tierList.PatchSelection.Should().Be(rollover.Selection);
        tierList.Should().BeEquivalentTo(new { Patch = Previous, Role = "TOP", RankTier = "DIAMOND", Region = "KR", QueueFamily = Solo });
        tierList.Entries.Should().ContainSingle().Which.Games.Should().Be(41);
        var grade = await harness.Analytics.GetPublishedGradeAsync(103, "TOP", "DIAMOND", "KR", "solo", rollover, default);
        grade!.Games.Should().Be(tierList.Entries[0].Games);

        var filter = new ChampionAnalyticsFilter(RankTier: "diamond", Region: "kr", Role: "top", QueueFamily: "solo");
        var winRates = await harness.Analytics.GetPublishedWinRatesAsync(103, filter, rollover, default);
        winRates.ByRoleTier.Should().ContainSingle().Which.Should()
            .BeEquivalentTo(new { Patch = Previous, Role = "TOP", RankTier = "DIAMOND", Games = 41 });
        (await harness.Analytics.GetPublishedTrendAsync(103, "TOP", null, "KR", Solo, rollover, default))
            .Points.Should().BeEmpty();

        var pending = await harness.Publication.ResolveAsync("solo", Current, default);
        pending.Selection.Should().Be(new AnalyticsPatchSelection(AnalyticsPatchSelectionState.Preparing, Current, Current, null));
        var readCount = harness.Guard.ReadCount;
        var empty = await harness.Analytics.GetPublishedTierListAsync("TOP", "DIAMOND", "KR", "solo", pending, default);
        empty.Entries.Should().BeEmpty();
        empty.Patch.Should().Be(Current);
        empty.PatchSelection.Should().Be(pending.Selection);
        harness.Guard.ReadCount.Should().Be(readCount);

        var options = await harness.Publication.GetPatchOptionsAsync("solo", default);
        options.Select(x => (x.Patch, x.State, x.MatchCount, x.QueueFamily)).Should().Equal(
            (Current, AnalyticsPatchSelectionState.Preparing, 0, Solo),
            (Previous, AnalyticsPatchSelectionState.Ready, 41, Solo));
        (await harness.Publication.ResolveAsync("aram", null, default)).Selection
            .Should().Be(new AnalyticsPatchSelection(AnalyticsPatchSelectionState.Ready, Current, null, Current));
        (await harness.Publication.ResolveAsync("arena", null, default)).Selection
            .Should().Be(new AnalyticsPatchSelection(AnalyticsPatchSelectionState.Preparing, Current, null, null));
        (await harness.Publication.ResolveAsync("solo", "16.9", default)).Selection
            .Should().Be(new AnalyticsPatchSelection(AnalyticsPatchSelectionState.Unavailable, Current, "16.9", null));

        await using (var transaction = await harness.Db.Database.BeginTransactionAsync())
        {
            harness.Db.ChampionRoleTierStats.Add(Atom(Current, Solo, 160));
            harness.Db.ChampionScopeGradeStats.Add(Grade(Current, Solo, "TOP", 160));
            await harness.Db.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        var ready = await harness.Publication.ResolveAsync("solo", null, default);
        ready.Selection.Should().Be(new AnalyticsPatchSelection(AnalyticsPatchSelectionState.Ready, Current, null, Current));
        (await harness.Publication.ResolveAsync("solo", Current, default)).Selection
            .Should().Be(new AnalyticsPatchSelection(AnalyticsPatchSelectionState.Ready, Current, Current, Current));
        var historical = await harness.Publication.ResolveAsync(Solo, $" {Previous} ", default);
        historical.Selection.Should().Be(new AnalyticsPatchSelection(AnalyticsPatchSelectionState.Ready, Current, Previous, Previous));
        (await harness.Analytics.GetPublishedTrendAsync(103, "TOP", null, "ALL", Solo, historical, default))
            .Points.Select(x => x.Patch).Should().Equal(Previous);
        (await harness.Analytics.GetPublishedTierListAsync("TOP", "DIAMOND", "KR", "solo", ready, default))
            .Entries.Should().ContainSingle().Which.Games.Should().Be(160);
    }

    [Fact]
    public async Task StrictReads_IgnoreLegacyCacheAndStayMaterializedIfStatsDisappear()
    {
        await using var harness = await Harness.CreateAsync();
        AddPatch(harness.Db, Current, 0, true);
        AddPublication(harness.Db, Current, Solo, 33);
        await harness.Db.SaveChangesAsync();
        var publication = await harness.Publication.ResolveAsync(Solo, Current, default);

        var legacy = new Mock<IChampionWinRateComputeService>();
        legacy.Setup(x => x.ComputeTierListFromStatsAsync("TOP", "DIAMOND", "KR", Current, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new TierListEntry(103, "TOP", TierGrade.S, 0.2, 0.8, 0.2, 0.1, 999, null, null)]);
        var filter = new ChampionAnalyticsFilter(RankTier: "DIAMOND", Region: "KR", Role: "TOP", QueueFamily: Solo, Patch: Current);
        legacy.Setup(x => x.ComputeWinRatesFromStatsAsync(103, filter, Current, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ChampionWinRateDto(103, "TOP", "DIAMOND", 999, 999, 1, 1, 0, 1, 1, Current)]);
        var legacyAnalytics = harness.CreateAnalytics(legacy.Object);
        (await legacyAnalytics.GetTierListAsync("TOP", "DIAMOND", "KR", Solo, Current, default))
            .Entries.Should().ContainSingle().Which.Games.Should().Be(999);
        (await legacyAnalytics.GetWinRatesAsync(103, filter, default)).ByRoleTier.Should().ContainSingle().Which.Games.Should().Be(999);

        (await harness.Analytics.GetPublishedTierListAsync("TOP", "DIAMOND", "KR", Solo, publication, default))
            .Entries.Should().ContainSingle().Which.Games.Should().Be(33);
        (await harness.Analytics.GetPublishedWinRatesAsync(103, filter, publication, default))
            .ByRoleTier.Should().ContainSingle().Which.Games.Should().Be(33);

        await harness.Db.ChampionRoleTierStats.ExecuteDeleteAsync();
        await harness.Analytics.InvalidateAnalyticsCacheForPatchAsync(Current, default);
        (await harness.Analytics.GetPublishedTierListAsync("TOP", "DIAMOND", "KR", Solo, publication, default))
            .Entries.Should().BeEmpty();
        (await harness.Analytics.GetPublishedWinRatesAsync(103, filter, publication, default)).ByRoleTier.Should().BeEmpty();
    }

    [Fact]
    public async Task PublishedPatchOptions_BoundTheHistoryAndOmitUnpublishedPatches()
    {
        await using var harness = await Harness.CreateAsync();
        for (var number = 1; number <= 18; number++)
        {
            var version = $"16.{number}";
            AddPatch(harness.Db, version, number - 18, number == 18);
            if (number < 17)
                AddPublication(harness.Db, version, Solo, number);
        }
        harness.Db.ChampionRoleTierStats.Add(Atom("16.17", Solo, 17));
        await harness.Db.SaveChangesAsync();

        var entries = await harness.Publication.GetPatchOptionsAsync(Solo, default);

        entries.Select(x => x.Patch).Should().Equal(
            ["16.18", "16.16", "16.15", "16.14", "16.13", "16.12", "16.11", "16.10", "16.9", "16.8", "16.7", "16.6"]);
        entries[0].State.Should().Be(AnalyticsPatchSelectionState.Preparing);
        entries[1].MatchCount.Should().Be(16);
        (await harness.Publication.ResolveAsync(Solo, "16.17", default)).Selection.State.Should().Be(AnalyticsPatchSelectionState.Unavailable);
    }

    [Fact]
    public async Task MasterPlus_UsesScopePublicationAndDistinguishesZeroGamesFromPreparing()
    {
        await using var harness = await Harness.CreateAsync();
        AddPatch(harness.Db, "16.13", -14, false);
        AddPatch(harness.Db, "16.14", 0, true);
        AddPublication(harness.Db, "16.13", Solo, 80);
        AddPublication(harness.Db, "16.14", Solo, 60);
        harness.Db.ScopeMatchCountStats.Add(new ScopeMatchCountStat
        {
            Patch = "16.13", QueueFamily = Solo, PlatformRegion = "ALL", RankScope = "MASTER_PLUS", TotalMatches = 12
        });
        await harness.Db.SaveChangesAsync();

        (await harness.Publication.ResolveAsync(Solo, null, default)).Selection.EffectivePatch.Should().Be("16.14");
        (await harness.Publication.ResolveAsync(Solo, null, default, "master+")).Selection.EffectivePatch.Should().Be("16.13");
        (await harness.Publication.ResolveAsync(Solo, "16.14", default, "MASTER_PLUS")).Selection.State
            .Should().Be(AnalyticsPatchSelectionState.Preparing);
        (await harness.Publication.GetPatchOptionsAsync(Solo, default, "MASTER_PLUS"))[1].MatchCount.Should().Be(12);

        harness.Db.ScopeMatchCountStats.Add(new ScopeMatchCountStat
        {
            Patch = "16.14", QueueFamily = Solo, PlatformRegion = "ALL", RankScope = "MASTER_PLUS", TotalMatches = 0
        });
        await harness.Db.SaveChangesAsync();

        var published = (await harness.Publication.GetPatchOptionsAsync(Solo, default, "MASTER_PLUS"))[0];
        published.State.Should().Be(AnalyticsPatchSelectionState.Ready);
        published.MatchCount.Should().Be(0);
    }

    private static Patch AddPatch(TranscendenceContext db, string version, int daysAgo, bool active)
    {
        var patch = new Patch { Version = version, ReleaseDate = DateTime.UtcNow.AddDays(daysAgo), IsActive = active };
        db.Patches.Add(patch);
        return patch;
    }

    private static void AddPublication(TranscendenceContext db, string patch, string queue, int games, string role = "TOP")
    {
        db.ChampionRoleTierStats.Add(Atom(patch, queue, games, role: role));
        db.ChampionScopeGradeStats.Add(Grade(patch, queue, "ALL", games));
        if (role != "ALL")
            db.ChampionScopeGradeStats.Add(Grade(patch, queue, role, games));
        db.ScopeMatchCountStats.Add(new ScopeMatchCountStat
        {
            Patch = patch, QueueFamily = queue, PlatformRegion = "ALL", RankScope = "ALL", TotalMatches = games
        });
    }

    private static ChampionRoleTierStat Atom(
        string patch, string queue, int games, string region = "KR", string rank = "DIAMOND", string role = "TOP") => new()
    {
        Patch = patch, QueueFamily = queue, ChampionId = 103, Role = role,
        PlatformRegion = region, RankTier = rank, Games = games, Wins = games / 2, ComputedAtUtc = DateTime.UtcNow
    };

    private static ChampionScopeGradeStat Grade(string patch, string queue, string role, int games) => new()
    {
        Patch = patch, QueueFamily = queue, ChampionId = 103, Role = role, PrimaryRole = queue == Aram ? "ALL" : "TOP",
        PlatformRegion = "ALL", RankScope = "ALL", Games = games, Wins = games / 2, ComputedAtUtc = DateTime.UtcNow
    };

    private sealed class Harness : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly ServiceProvider _services;
        private readonly IOptions<ChampionAnalyticsComputeOptions> _computeOptions = Options.Create(new ChampionAnalyticsComputeOptions());

        private Harness(SqliteConnection connection, TranscendenceContext db, NoRawAnalytics interceptor)
        {
            _connection = connection;
            Db = db;
            Guard = interceptor;
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddHybridCache();
            _services = services.BuildServiceProvider();
            Publication = new AnalyticsPublicationService(Db);
            Analytics = CreateAnalytics(new ChampionWinRateComputeService(Db, _computeOptions, Options.Create(new TieringOptions())));
        }

        public TranscendenceContext Db { get; }
        public AnalyticsPublicationService Publication { get; }
        public ChampionAnalyticsService Analytics { get; }
        public NoRawAnalytics Guard { get; }

        public static async Task<Harness> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var interceptor = new NoRawAnalytics();
            var options = new DbContextOptionsBuilder<TranscendenceContext>().UseSqlite(connection).AddInterceptors(interceptor).Options;
            var db = new SqliteCompatibleTranscendenceContext(options);
            await db.Database.EnsureCreatedAsync();
            return new Harness(connection, db, interceptor);
        }

        public ChampionAnalyticsService CreateAnalytics(IChampionWinRateComputeService compute) => new(
            Db, _services.GetRequiredService<HybridCache>(), Mock.Of<IChampionMatchupComputeService>(), compute,
            Mock.Of<IChampionBuildComputeService>(), Mock.Of<IChampionProComputeService>(), _computeOptions,
            Options.Create(new MultiRegionIngestionOptions { Regions = [new RegionConfig { Region = "KR", Enabled = true }] }));

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _services.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class NoRawAnalytics : DbCommandInterceptor
    {
        public int ReadCount { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            ReadCount++;
            command.CommandText.Should().NotContain("\"Matches\"").And.NotContain("\"MatchParticipants\"")
                .And.NotContain("\"Summoners\"").And.NotContain("\"Ranks\"");
            return ValueTask.FromResult(result);
        }
    }
}
