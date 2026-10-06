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
using Match = Transcendence.Data.Models.LoL.Match.Match;

namespace Transcendence.IntegrationTests;

[Collection(PostgresIntegrationCollection.Name)]
public sealed class CompactSynergyPostgresTests(PostgresIntegrationFixture fixture)
{
    [Fact]
    public async Task Facts_EqualRawAcrossScopes_ResumeWithoutDoubleCounting_AndSurviveArchiving()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TranscendenceContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Patches.ExecuteUpdateAsync(update => update.SetProperty(patch => patch.IsActive, false));
        var patch = $"syn-{Guid.NewGuid():N}"[..12];
        db.Patches.Add(new Patch { Version = patch, IsActive = true, ReleaseDate = DateTime.UtcNow });
        var roles = new[] { "TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY" };
        var champions = new[] { 266, 64, 103, 22, 40 };
        foreach (var region in new[] { "NA1", "EUW1" })
        foreach (var queue in new[] { 420, 440 })
        foreach (var tier in new[] { "EMERALD", "GOLD" })
        for (var game = 0; game < 6; game++)
        {
            var match = new Match { Id = Guid.NewGuid(), MatchId = $"{region}_{Guid.NewGuid():N}", Patch = patch,
                PlatformRegion = region, QueueId = queue, QueueType = queue.ToString(), Status = FetchStatus.Success };
            if (game == 0 && queue == 420) match.QueueId = 0; // legacy queue type
            if (game == 0 && queue == 440) { match.QueueId = 0; match.QueueType = ""; match.QueueFamily = "RANKED_FLEX"; }
            if (game == 1 && queue == 420) match.QueueFamily = "RANKED_FLEX"; // qualifies for both raw scopes
            db.Matches.Add(match);
            for (var participant = 0; participant < 10; participant++)
            {
                var summoner = new Summoner { Id = Guid.NewGuid(), Puuid = Guid.NewGuid().ToString(), PlatformRegion = region, Region = "AMERICAS" };
                db.Summoners.Add(summoner);
                db.Ranks.Add(new Rank { Id = Guid.NewGuid(), SummonerId = summoner.Id, Tier = tier,
                    QueueType = queue == 420 ? "RANKED_SOLO_5x5" : "RANKED_FLEX_SR" });
                db.MatchParticipants.Add(new MatchParticipant { Id = Guid.NewGuid(), MatchId = match.Id, Match = match, Summoner = summoner,
                    SummonerId = summoner.Id, ParticipantId = participant + 1, ChampionId = champions[participant % 5] + (participant < 5 ? 0 : 1000),
                    TeamPosition = roles[participant % 5], TeamId = participant < 5 ? 100 : 200, Win = (game < 4) == (participant < 5) });
            }
        }
        await db.SaveChangesAsync();
        var comparisons = new Dictionary<(int Champion, string Role, string? Tier, string Region, string Queue), ChampionSynergiesResponse>();
        await using (var cache = Cache())
        {
            var raw = new ChampionSynergyService(db, cache.GetRequiredService<HybridCache>(), new AnalyticsPatchQueryService(db));
            foreach (var tier in new[] { null, "EMERALD_PLUS", "GOLD" })
            foreach (var region in new[] { "ALL", "NA1", "EUW1" })
            foreach (var queue in new[] { "solo", "flex" })
            for (var i = 0; i < roles.Length; i++)
                comparisons[(champions[i], roles[i], tier, region, queue)] =
                    await raw.GetSynergiesAsync(champions[i], roles[i], tier, region, queue, patch);
        }
        var materializer = new ChampionSynergyFactMaterializer(db, Options.Create(new ChampionSynergyFactOptions
        {
            PatchesToMaterialize = 1, MatchBatchSize = 7, MaxMatchesPerRun = 20, BatchDelayMilliseconds = 0
        }));
        (await materializer.RefreshAsync(CancellationToken.None)).Should().Be(20);
        (await db.AnalyticsResponseSnapshots.AnyAsync(row => row.Feature == ChampionSynergyFactMaterializer.CoverageFeature && row.Patch == patch))
            .Should().BeFalse();
        (await materializer.RefreshAsync(CancellationToken.None)).Should().Be(20);
        (await materializer.RefreshAsync(CancellationToken.None)).Should().Be(8);
        (await materializer.RefreshAsync(CancellationToken.None)).Should().Be(0);
        (await db.ChampionSynergySourceMatches.CountAsync(row => row.Patch == patch)).Should().Be(48);
        (await db.AnalyticsResponseSnapshots.AnyAsync(row => row.Feature == ChampionSynergyFactMaterializer.CoverageFeature && row.Patch == patch))
            .Should().BeTrue("only complete coverage enables the facts read path");
        await db.Matches.IgnoreQueryFilters().Where(row => row.Patch == patch).ExecuteDeleteAsync();
        await using var factsCache = Cache();
        var facts = new ChampionSynergyService(db, factsCache.GetRequiredService<HybridCache>(), new AnalyticsPatchQueryService(db));
        foreach (var (filters, expected) in comparisons)
            (await facts.GetSynergiesAsync(filters.Champion, filters.Role, filters.Tier, filters.Region, filters.Queue, patch))
                .Should().BeEquivalentTo(expected, $"facts must equal raw for {filters}, after raw matches are archived");
        comparisons.Values.Should().OnlyContain(result => result.TotalGames > 0 && result.BestPartners.Count > 0);
        await db.Ranks.ExecuteUpdateAsync(update => update.SetProperty(rank => rank.Tier, "GOLD"));
        await factsCache.GetRequiredService<HybridCache>().RemoveByTagAsync("analytics");
        (await facts.GetSynergiesAsync(266, "TOP", "EMERALD_PLUS", "ALL", "solo", patch)).TotalGames.Should().Be(0,
            "rank attribution uses current ranks rather than freezing rank at materialization");
    }

    [Fact]
    public async Task ExpiredBuildLabCleanup_IsBounded_AndKeepsLedgerUntilStatsAreGone()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TranscendenceContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Patches.ExecuteUpdateAsync(update => update.SetProperty(patch => patch.IsActive, false));
        var retained = $"keep-{Guid.NewGuid():N}"[..12];
        var expired = $"000-{Guid.NewGuid():N}"[..12];
        db.Patches.Add(new Patch { Version = retained, IsActive = true, ReleaseDate = DateTime.UtcNow });
        for (var i = 0; i < 601; i++)
            db.BuildLabOptionStats.Add(new BuildLabOptionStat { ChampionId = i + 1, Patch = expired });
        for (var i = 0; i < 10; i++)
            db.BuildLabProcessedMatches.Add(new BuildLabProcessedMatch { MatchId = Guid.NewGuid(), Patch = expired, ProcessedAtUtc = DateTime.UtcNow });
        db.BuildLabCoverage.Add(new BuildLabCoverage { Patch = expired, Region = "ALL", Matches = 10, LastCountedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var refresher = new BuildLabStatsRefresher(db, Options.Create(new BuildLabOptions
        {
            PatchesToRetain = 1, PriorPatchesToRefresh = 0, BatchDelayMilliseconds = 0
        }), NullLogger<BuildLabStatsRefresher>.Instance);
        await refresher.RefreshAsync(CancellationToken.None);
        (await db.BuildLabOptionStats.CountAsync(row => row.Patch == expired)).Should().Be(101);
        (await db.BuildLabProcessedMatches.CountAsync(row => row.Patch == expired)).Should().Be(10);
        await refresher.RefreshAsync(CancellationToken.None);
        (await db.BuildLabOptionStats.CountAsync(row => row.Patch == expired)).Should().Be(0);
        (await db.BuildLabProcessedMatches.CountAsync(row => row.Patch == expired)).Should().Be(0);
        (await db.BuildLabCoverage.CountAsync(row => row.Patch == expired)).Should().Be(0);
    }

    private static ServiceProvider Cache()
    {
        var services = new ServiceCollection();
        services.AddHybridCache();
        return services.BuildServiceProvider();
    }
}
