using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Account;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Data.Models.LoL.Static;
using Transcendence.Service.Core.Services.Analytics.Implementations;
using Transcendence.Service.Core.Tests.Support;
using MatchEntity = Transcendence.Data.Models.LoL.Match.Match;

namespace Transcendence.Service.Core.Tests;

public sealed class BuildResourceAnalyticsServiceTests
{
    [Fact]
    public async Task ItemAndRuneAnalytics_UseParticipantLevelPickRatesAndChampionDenominators()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TranscendenceContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new SqliteCompatibleTranscendenceContext(options);
        await db.Database.EnsureCreatedAsync();

        const string patch = "16.14";
        db.Patches.Add(new Patch
        {
            Version = patch,
            ReleaseDate = DateTime.UtcNow.AddDays(-5),
            DetectedAt = DateTime.UtcNow.AddDays(-5),
            IsActive = true
        });
        db.ItemVersions.Add(new ItemVersion
        {
            ItemId = 3078,
            PatchVersion = patch,
            Name = "Trinity Force",
            Description = "Threefold strikes",
            BuildsFrom = [3057, 3044, 3067],
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
            Description = "Attack champions three times",
            RunePathId = 8000,
            RunePathName = "Precision",
            Slot = 0
        });

        var match = new MatchEntity
        {
            Id = Guid.NewGuid(),
            MatchId = "NA1_RESOURCE_TEST",
            Patch = patch,
            QueueId = 420,
            QueueFamily = "RANKED_SOLO_DUO",
            Status = FetchStatus.Success,
            PlatformRegion = "NA1"
        };
        db.Matches.Add(match);

        AddParticipant(db, match, 1, 266, "TOP", win: true, includeResources: true);
        AddParticipant(db, match, 2, 266, "TOP", win: false, includeResources: false);
        AddParticipant(db, match, 3, 64, "JUNGLE", win: false, includeResources: true);
        await db.SaveChangesAsync();

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();
        serviceCollection.AddHybridCache();
        var services = serviceCollection.BuildServiceProvider();
        var service = new BuildResourceAnalyticsService(
            db,
            services.GetRequiredService<HybridCache>(),
            new AnalyticsPatchQueryService(db));

        var items = await service.GetItemsAsync("NA1", null);
        var runes = await service.GetRunesAsync("NA1", null);
        var detail = await service.GetItemAsync(3078, "NA1", null);
        var item = items.Entries.Should().ContainSingle().Subject;
        var rune = runes.Entries.Should().ContainSingle().Subject;

        items.TotalParticipantGames.Should().Be(3);
        item.ResourceId.Should().Be(3078);
        item.Games.Should().Be(2);
        item.WinRate.Should().BeApproximately(0.5, 0.0001);
        item.PickRate.Should().BeApproximately(2.0 / 3.0, 0.0001);
        item.TopChampions.Single(x => x.ChampionId == 266).PickRate.Should().BeApproximately(0.5, 0.0001);
        rune.ResourceId.Should().Be(8005);
        rune.Games.Should().Be(2);
        detail.Should().NotBeNull();
        detail!.ChampionStats.Should().HaveCount(2);
        detail.Resource.TopChampions.Should().HaveCount(2);
    }

    private static void AddParticipant(
        TranscendenceContext db,
        MatchEntity match,
        int participantId,
        int championId,
        string role,
        bool win,
        bool includeResources)
    {
        var summoner = new Summoner
        {
            Id = Guid.NewGuid(),
            Puuid = $"puuid-{participantId}",
            PlatformRegion = "NA1",
            Region = "AMERICAS"
        };
        var participant = new MatchParticipant
        {
            Id = Guid.NewGuid(),
            MatchId = match.Id,
            Match = match,
            SummonerId = summoner.Id,
            Summoner = summoner,
            ParticipantId = participantId,
            ChampionId = championId,
            TeamPosition = role,
            Win = win
        };
        if (includeResources)
        {
            participant.Items.Add(new MatchParticipantItem
            {
                MatchParticipantId = participant.Id,
                SlotIndex = 0,
                ItemId = 3078,
                PatchVersion = "16.14"
            });
            participant.Runes.Add(new MatchParticipantRune
            {
                MatchParticipantId = participant.Id,
                RuneId = 8005,
                PatchVersion = "16.14",
                SelectionTree = RuneSelectionTree.Primary,
                SelectionIndex = 0,
                StyleId = 8000
            });
        }

        db.Summoners.Add(summoner);
        db.MatchParticipants.Add(participant);
    }
}
