using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Account;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Data.Models.LoL.Static;
using Transcendence.Service.Core.Services.Analytics.Implementations;
using Transcendence.Service.Core.Services.Cache;
using Transcendence.Service.Core.Tests.Support;
using MatchEntity = Transcendence.Data.Models.LoL.Match.Match;

namespace Transcendence.Service.Core.Tests;

public sealed class ChampionSynergyServiceTests
{
    [Fact]
    public async Task TopSynergies_OnlyCountSameTeamJunglePartners()
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
            Version = "16.14",
            ReleaseDate = DateTime.UtcNow.AddDays(-4),
            DetectedAt = DateTime.UtcNow.AddDays(-4),
            IsActive = true
        });

        for (var index = 0; index < 4; index++)
        {
            var match = new MatchEntity
            {
                Id = Guid.NewGuid(),
                MatchId = $"NA1_SYNERGY_{index}",
                Patch = "16.14",
                QueueId = 420,
                QueueFamily = "RANKED_SOLO_DUO",
                Status = FetchStatus.Success,
                PlatformRegion = "NA1"
            };
            db.Matches.Add(match);
            var focalWin = index < 3;
            AddParticipant(db, match, index * 10 + 1, 266, "TOP", 100, focalWin);
            AddParticipant(db, match, index * 10 + 2, 64, "JUNGLE", 100, focalWin);
            AddParticipant(db, match, index * 10 + 3, 40, "UTILITY", 100, focalWin);
            AddParticipant(db, match, index * 10 + 4, 120, "JUNGLE", 200, !focalWin);
        }
        await db.SaveChangesAsync();

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();
        serviceCollection.AddHybridCache();
        var services = serviceCollection.BuildServiceProvider();
        var service = new ChampionSynergyService(
            db,
            services.GetRequiredService<HybridCache>(),
            new AnalyticsPatchQueryService(db));

        var result = await service.GetSynergiesAsync(266, "TOP", null, "NA1", "solo", null);

        result.TotalGames.Should().Be(4);
        result.BaselineWinRate.Should().BeApproximately(0.75, 0.0001);
        var partner = result.BestPartners.Should().ContainSingle().Subject;
        partner.PartnerChampionId.Should().Be(64);
        partner.PartnerRole.Should().Be("JUNGLE");
        partner.Games.Should().Be(4);
        partner.PickRate.Should().BeApproximately(1, 0.0001);
        partner.WinRateDelta.Should().BeApproximately(0, 0.0001);

        var materializer = new ChampionSynergyFactMaterializer(db, Options.Create(new ChampionSynergyFactOptions
        {
            MatchBatchSize = 2, MaxMatchesPerRun = 2, BatchDelayMilliseconds = 0
        }));
        (await materializer.RefreshAsync(CancellationToken.None)).Should().Be(2);
        (await db.AnalyticsResponseSnapshots.AnyAsync(row => row.Feature == ChampionSynergyFactMaterializer.CoverageFeature))
            .Should().BeFalse("a partially materialized patch must continue using complete raw data");
        var cache = services.GetRequiredService<HybridCache>();
        await cache.RemoveByTagAsync("analytics");
        (await service.GetSynergiesAsync(266, "TOP", null, "NA1", "solo", null)).Should().BeEquivalentTo(result);
        (await materializer.RefreshAsync(CancellationToken.None)).Should().Be(2);
        (await materializer.RefreshAsync(CancellationToken.None)).Should().Be(0);
        (await db.ChampionSynergySourceMatches.CountAsync()).Should().Be(4);
        (await db.AnalyticsResponseSnapshots.AnyAsync(row => row.Feature == ChampionSynergyFactMaterializer.CoverageFeature))
            .Should().BeTrue();
        await cache.RemoveByTagAsync("analytics");
        (await service.GetSynergiesAsync(266, "TOP", null, "NA1", "solo", null)).Should().BeEquivalentTo(result);
        await db.Matches.IgnoreQueryFilters().ExecuteDeleteAsync();
        await cache.RemoveByTagAsync("analytics");
        (await service.GetSynergiesAsync(266, "TOP", null, "NA1", "solo", "16.14")).Should().BeEquivalentTo(result,
            "compact facts must survive deletion of the archived match details");
    }

    [Fact]
    public async Task Synergies_SurviveThePatchTagClear_ThatPrecomputedRefreshesIssue()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TranscendenceContext>().UseSqlite(connection).Options;
        await using var db = new SqliteCompatibleTranscendenceContext(options);
        await db.Database.EnsureCreatedAsync();
        db.Patches.Add(new Patch
        {
            Version = "16.14",
            ReleaseDate = DateTime.UtcNow.AddDays(-4),
            DetectedAt = DateTime.UtcNow.AddDays(-4),
            IsActive = true
        });
        void AddGame(int index)
        {
            var match = new MatchEntity
            {
                Id = Guid.NewGuid(),
                MatchId = $"NA1_SYNERGY_CACHE_{index}",
                Patch = "16.14",
                QueueId = 420,
                QueueFamily = "RANKED_SOLO_DUO",
                Status = FetchStatus.Success,
                PlatformRegion = "NA1"
            };
            db.Matches.Add(match);
            AddParticipant(db, match, 1, 266, "TOP", 100, true);
            AddParticipant(db, match, 2, 64, "JUNGLE", 100, true);
        }
        for (var index = 0; index < 4; index++)
            AddGame(index);
        await db.SaveChangesAsync();

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();
        serviceCollection.AddHybridCache();
        var services = serviceCollection.BuildServiceProvider();
        var cache = services.GetRequiredService<HybridCache>();
        var service = new ChampionSynergyService(db, cache, new AnalyticsPatchQueryService(db));

        (await service.GetSynergiesAsync(266, "TOP", null, "NA1", "solo", null)).TotalGames.Should().Be(4);
        AddGame(4);
        await db.SaveChangesAsync();

        // The precomputed-analytics, matchup and build-snapshot refreshes clear this tag hourly.
        await cache.RemoveByTagAsync(CacheTags.ForPatch("16.14"));
        (await service.GetSynergiesAsync(266, "TOP", null, "NA1", "solo", null)).TotalGames
            .Should().Be(4, "a refresh of other tables must not force every synergy to be recomputed");

        // A deliberate analytics-wide clear still recomputes.
        await cache.RemoveByTagAsync("analytics");
        (await service.GetSynergiesAsync(266, "TOP", null, "NA1", "solo", null)).TotalGames.Should().Be(5);
    }

    [Fact]
    public async Task Deployment_ReusesExistingSynergyCacheKeys_WithoutAColdRawScan()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new SqliteCompatibleTranscendenceContext(
            new DbContextOptionsBuilder<TranscendenceContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var collection = new ServiceCollection();
        collection.AddHybridCache();
        await using var provider = collection.BuildServiceProvider();
        var cache = provider.GetRequiredService<HybridCache>();
        var prior = new Transcendence.Service.Core.Services.Analytics.Models.ChampionSynergiesResponse(
            266, "TOP", "all", "NA1", "16.14", "RANKED_SOLO_DUO", 100, 55, 0.55, []);
        await cache.SetAsync("analytics:synergies:v1:266:TOP:all:NA1:RANKED_SOLO_DUO:16.14", prior);
        var service = new ChampionSynergyService(db, cache, new AnalyticsPatchQueryService(db));
        (await service.GetSynergiesAsync(266, "TOP", null, "NA1", "solo", "16.14")).Should().BeEquivalentTo(prior,
            "the raw/facts response contract is unchanged, so deployment must preserve existing warm cache entries");
        db.AnalyticsResponseSnapshots.Add(new Transcendence.Data.Models.LoL.Analytics.AnalyticsResponseSnapshot
        {
            Id = Guid.NewGuid(), Feature = "synergies", Patch = "16.14",
            ScopeKey = "analytics:synergies:v2:266:TOP:all:NA1:RANKED_SOLO_DUO:16.14",
            ComputedAtUtc = DateTime.UtcNow, Payload = System.Text.Json.JsonSerializer.Serialize(prior)
        });
        await db.SaveChangesAsync();
        await cache.RemoveAsync("analytics:synergies:v1:266:TOP:all:NA1:RANKED_SOLO_DUO:16.14");
        (await service.GetSynergiesAsync(266, "TOP", null, "NA1", "solo", "16.14")).Should().BeEquivalentTo(prior,
            "a cold cache must reuse the durable scopes introduced by the initial deployment");
    }

    private static void AddParticipant(
        TranscendenceContext db,
        MatchEntity match,
        int participantId,
        int championId,
        string role,
        int teamId,
        bool win)
    {
        var summoner = new Summoner
        {
            Id = Guid.NewGuid(),
            Puuid = $"synergy-{match.MatchId}-{participantId}",
            PlatformRegion = "NA1",
            Region = "AMERICAS"
        };
        db.Summoners.Add(summoner);
        db.MatchParticipants.Add(new MatchParticipant
        {
            Id = Guid.NewGuid(),
            MatchId = match.Id,
            Match = match,
            SummonerId = summoner.Id,
            Summoner = summoner,
            ParticipantId = participantId,
            ChampionId = championId,
            TeamPosition = role,
            TeamId = teamId,
            Win = win
        });
    }
}
