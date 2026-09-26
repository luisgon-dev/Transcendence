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
using Transcendence.Service.Core.Services.Analytics.Interfaces;
using Transcendence.Service.Core.Services.Analytics.Models;

namespace Transcendence.IntegrationTests;

/// <summary>
/// Build Lab end to end on the real migrated schema: the refresher's unnest/ON CONFLICT upsert and
/// ledger insert, then the service reading those rows back. SQLite cannot run either statement, so
/// this is the only place they execute before prod.
/// </summary>
[Collection(PostgresIntegrationCollection.Name)]
public sealed class BuildLabRealPostgresTests(PostgresIntegrationFixture fixture)
{
    private const int Ahri = 103;
    private const int Zed = 238;
    private const int Luden = 6655;
    private const int Malignance = 3118;

    [Fact]
    public async Task Refresh_CountsEachMatchOnce_AndTheServiceReadsTheCountsBack()
    {
        var patch = await SeedPatchAsync();
        await using (var db = NewDb())
        {
            // Ahri's first item: Luden's in 6 games (4 wins), Malignance in 5 (1 win).
            for (var index = 0; index < 6; index++)
                AddGame(db, patch, ahriWins: index < 4, firstItem: Luden, ahriGoldLead: 0);
            for (var index = 0; index < 5; index++)
                AddGame(db, patch, ahriWins: index < 1, firstItem: Malignance, ahriGoldLead: 0);
            // Not eligible: the timeline was only captured at the baseline schema.
            AddGame(db, patch, ahriWins: true, firstItem: Malignance, ahriGoldLead: 0, timelineSchema: 1);
            await db.SaveChangesAsync();
        }

        var first = await RefreshAsync();
        var second = await RefreshAsync();

        first.MatchesCounted.Should().Be(11);
        second.MatchesCounted.Should().Be(0, "the ledger must stop a match from being counted twice");
        await using (var db = NewDb())
        {
            var firstItems = await db.BuildLabOptionStats.AsNoTracking()
                .Where(row => row.Patch == patch && row.ChampionId == Ahri && row.Region == "ALL" &&
                              row.OpponentChampionId == 0 && row.Family == BuildLabFamily.Item && row.Stage == 1)
                .ToListAsync();
            firstItems.Should().HaveCount(2);
            firstItems.Single(row => row.ActionKey == $"{Luden}").Should()
                .Match<BuildLabOptionStat>(row => row.Games == 6 && row.Wins == 4 && row.TimingSecondsSum == 6 * 700);
            firstItems.Single(row => row.ActionKey == $"{Malignance}").Should()
                .Match<BuildLabOptionStat>(row => row.Games == 5 && row.Wins == 1);
            // Counted in the matchup and region scopes too, and the second item only globally.
            (await db.BuildLabOptionStats.CountAsync(row =>
                row.Patch == patch && row.ChampionId == Ahri && row.OpponentChampionId == Zed &&
                row.Family == BuildLabFamily.Item && row.Stage == 1)).Should().Be(2);
            (await db.BuildLabOptionStats.CountAsync(row =>
                row.Patch == patch && row.ChampionId == Ahri && row.Region == "KR" &&
                row.Family == BuildLabFamily.Item && row.Stage == 1)).Should().Be(2);
            (await db.BuildLabOptionStats.CountAsync(row =>
                row.Patch == patch && row.OpponentChampionId != 0 &&
                row.Family == BuildLabFamily.Item && row.Stage == 2)).Should().Be(0);
            (await db.BuildLabProcessedMatches.CountAsync(row => row.Patch == patch)).Should().Be(11);
        }

        var response = await ServiceGetAsync(new BuildLabQuery(
            Ahri, "MIDDLE", null, patch, null, "items", "supported", [], []));

        response.Available.Should().BeTrue();
        response.Coverage.IncludedPatches.Should().Equal(patch);
        response.Coverage.CountedMatches.Should().Be(11);
        var firstItemStage = response.Stages.Single(stage => stage.Family == "ITEM" && stage.Stage == 1);
        firstItemStage.Games.Should().Be(11);
        firstItemStage.WinRate.Should().BeApproximately(5.0 / 11, 1e-9);
        firstItemStage.Options.Select(option => option.ActionKey).Should().Equal($"{Luden}", $"{Malignance}");
        var luden = firstItemStage.Options[0];
        luden.WinRate.Should().BeApproximately(4.0 / 6, 1e-9);
        luden.PickRate.Should().BeApproximately(6.0 / 11, 1e-9);
        luden.AverageTimingMinutes.Should().BeApproximately(700 / 60.0, 1e-9);
        luden.IsLowSample.Should().BeTrue();
        response.Stages.Should().Contain(stage => stage.Family == "STARTER");
        response.Stages.Should().Contain(stage => stage.Family == "BOOTS");

        // Conditioning on the first item reads the second-item cell under that prefix.
        var afterLuden = await ServiceGetAsync(new BuildLabQuery(
            Ahri, "MIDDLE", null, patch, null, "items", "supported", [Luden], []));
        afterLuden.Stages.Single(stage => stage.Family == "ITEM").Should()
            .Match<BuildLabStageDto>(stage =>
                stage.Stage == 2 && stage.Games == 6);

        // Eleven games vs Zed is below the matchup threshold, so the stage answers from all games and says so.
        var vsZed = await ServiceGetAsync(new BuildLabQuery(
            Ahri, "MIDDLE", Zed, patch, null, "items", "supported", [], []));
        var matchupStage = vsZed.Stages.Single(stage => stage.Family == "ITEM");
        matchupStage.Scope.Should().Be("ALL");
        matchupStage.IsFallback.Should().BeTrue();

        var runes = await ServiceGetAsync(new BuildLabQuery(
            Ahri, "MIDDLE", null, patch, null, "runes", "common", [], []));
        runes.Stages.Single(stage => stage.Family == "RUNE_PAGE").Options.Single().ActionIds
            .Should().Equal(8112, 8143, 8304);
        var spells = await ServiceGetAsync(new BuildLabQuery(
            Ahri, "MIDDLE", null, patch, null, "spells", "common", [], []));
        spells.Stages.Single().Options.Single().ActionKey.Should().Be("4+14");
    }

    [Fact]
    public async Task Refresh_BucketsDecisionsByTeamGoldDifference_AndTheAdjustedRateRemovesTheLead()
    {
        var patch = await SeedPatchAsync();
        await using (var db = NewDb())
        {
            // Luden's is only ever bought while far ahead and always wins; Malignance only while even
            // and wins half. Within each bucket neither is better than its peers, so standardizing to
            // the shared gold mix must pull Luden's raw 100% well down.
            for (var index = 0; index < 6; index++)
                AddGame(db, patch, ahriWins: true, firstItem: Luden, ahriGoldLead: 4000);
            for (var index = 0; index < 6; index++)
                AddGame(db, patch, ahriWins: index % 2 == 0, firstItem: Malignance, ahriGoldLead: 0);
            await db.SaveChangesAsync();
        }

        await RefreshAsync();

        await using (var db = NewDb())
        {
            var buckets = await db.BuildLabOptionStats.AsNoTracking()
                .Where(row => row.Patch == patch && row.ChampionId == Ahri && row.Region == "ALL" &&
                              row.OpponentChampionId == 0 && row.Family == BuildLabFamily.Item && row.Stage == 1)
                .ToDictionaryAsync(row => row.ActionKey, row => row.GoldBucket);
            buckets[$"{Luden}"].Should().Be(4);
            buckets[$"{Malignance}"].Should().Be(2);
        }

        var response = await ServiceGetAsync(new BuildLabQuery(
            Ahri, "MIDDLE", null, patch, null, "items", "supported", [], []));
        var firstItem = response.Stages.Single(stage => stage.Family == "ITEM").Options;
        var luden = firstItem.Single(option => option.ActionKey == $"{Luden}");
        var malignance = firstItem.Single(option => option.ActionKey == $"{Malignance}");
        luden.WinRate.Should().Be(1.0);
        malignance.WinRate.Should().Be(0.5);
        // Each item matched its own gold bucket exactly, so neither shows any lift over the other:
        // both land on the decision's overall 9/12.
        luden.AdjustedWinRate.Should().BeApproximately(0.75, 1e-9);
        malignance.AdjustedWinRate.Should().BeApproximately(0.75, 1e-9);
    }

    [Fact]
    public async Task Refresh_NeverCountsAnOpeningBuyThatCostsMoreThanTheStartingGold()
    {
        var patch = await SeedPatchAsync();
        await using (var db = NewDb())
        {
            AddGame(db, patch, ahriWins: true, firstItem: Luden, ahriGoldLead: 0);
            // Doran's Ring + Amplifying Tome = 800g: not something anyone can start with.
            AddGame(db, patch, ahriWins: true, firstItem: Luden, ahriGoldLead: 0, extraOpeningItems: [1052]);
            await db.SaveChangesAsync();
        }

        await RefreshAsync();

        await using var check = NewDb();
        var starters = await check.BuildLabOptionStats.AsNoTracking()
            .Where(row => row.Patch == patch && row.Region == "ALL" && row.OpponentChampionId == 0 &&
                          row.Family == BuildLabFamily.Starter)
            .ToListAsync();
        starters.Should().ContainSingle().Which.Should()
            .Match<BuildLabOptionStat>(row => row.ActionKey == "1056" && row.Games == 1);
        // The rest of that game still counts: only the impossible start is dropped.
        (await check.BuildLabOptionStats.Where(row =>
                row.Patch == patch && row.Region == "ALL" && row.OpponentChampionId == 0 &&
                row.Family == BuildLabFamily.Item && row.Stage == 1)
            .SumAsync(row => row.Games)).Should().Be(2);
    }

    [Fact]
    public async Task Migrations_PinTheSourceTablesMatchCardinality_SoTheBatchReadUsesTheIndex()
    {
        // A sampled ANALYZE badly underestimates distinct MatchIds on these clustered tables, which on
        // prod turned every 500-match batch read into a 21 GB sequential scan. The pin is the fix.
        await using var db = NewDb();
        var attributeOptions = await db.Database
            .SqlQueryRaw<string>(
                """
                SELECT c.relname || '.' || array_to_string(a.attoptions, ',') AS "Value"
                FROM pg_attribute a JOIN pg_class c ON c.oid = a.attrelid
                WHERE c.relname IN ('MatchParticipantItemEvents', 'MatchParticipantTimelineSnapshots')
                  AND a.attname = 'MatchId'
                """)
            .ToListAsync();
        attributeOptions.Should().BeEquivalentTo(
            "MatchParticipantItemEvents.n_distinct=-0.0023",
            "MatchParticipantTimelineSnapshots.n_distinct=-0.0037");

        var tableOptions = await db.Database
            .SqlQueryRaw<string>(
                """
                SELECT array_to_string(reloptions, ',') AS "Value" FROM pg_class
                WHERE relname = 'MatchParticipantItemEvents'
                """)
            .SingleAsync();
        tableOptions.Should().Contain("autovacuum_analyze_scale_factor=0.005");
    }

    private async Task<BuildLabRefreshResult> RefreshAsync()
    {
        await using var db = NewDb();
        var refresher = new BuildLabStatsRefresher(
            db,
            Options.Create(new BuildLabOptions { Enabled = true, MatchBatchSize = 2, PatchesToRetain = 50 }),
            NullLogger<BuildLabStatsRefresher>.Instance);
        return await refresher.RefreshAsync(CancellationToken.None);
    }

    private async Task<BuildLabResponse> ServiceGetAsync(BuildLabQuery query)
    {
        await using var db = NewDb();
        var services = new ServiceCollection();
        services.AddHybridCache();
        await using var provider = services.BuildServiceProvider();
        var service = new BuildLabService(
            db,
            provider.GetRequiredService<HybridCache>(),
            Options.Create(new BuildLabOptions { Enabled = true }));
        return await service.GetAsync(query, CancellationToken.None);
    }

    // The newest release date on the shared container, so this test's patch is the one refreshed first.
    private async Task<string> SeedPatchAsync()
    {
        var patch = $"bl-{Guid.NewGuid():N}"[..12];
        await using var db = NewDb();
        db.Patches.Add(new Patch
        {
            Version = patch,
            ReleaseDate = DateTime.UtcNow.AddYears(50).AddSeconds(Random.Shared.Next(1, 1_000_000)),
            DetectedAt = DateTime.UtcNow,
            IsActive = true
        });
        // Opening buys are priced against the patch, so the items the games start with need versions.
        foreach (var (itemId, price) in new[] { (1056, 400), (1052, 400), (3340, 0) })
            db.ItemVersions.Add(new ItemVersion { ItemId = itemId, PatchVersion = patch, Name = $"Item {itemId}", PriceTotal = price });
        await db.SaveChangesAsync();
        return patch;
    }

    private static void AddGame(
        TranscendenceContext db,
        string patch,
        bool ahriWins,
        int firstItem,
        int ahriGoldLead,
        int timelineSchema = 2,
        int[]? extraOpeningItems = null)
    {
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
            PlatformRegion = "KR",
            FetchedAt = DateTime.UtcNow
        };
        db.Matches.Add(match);
        db.MatchTimelineFetchStates.Add(new MatchTimelineFetchState
        {
            MatchId = match.Id,
            Match = match,
            Status = MatchTimelineFetchStatus.Success,
            SchemaVersion = timelineSchema
        });

        var ahri = AddParticipant(db, match, participantId: 1, teamId: 100, Ahri, ahriWins);
        AddParticipant(db, match, participantId: 6, teamId: 200, Zed, !ahriWins);
        db.MatchParticipantRunes.AddRange(
            Rune(ahri, RuneSelectionTree.Primary, 0, 8112),
            Rune(ahri, RuneSelectionTree.Primary, 1, 8143),
            Rune(ahri, RuneSelectionTree.Secondary, 0, 8304));

        var index = 0;
        void Item(int participantId, int atSeconds, int itemId, BuildItemCategory category) =>
            db.MatchParticipantItemEvents.Add(new MatchParticipantItemEvent
            {
                MatchId = match.Id,
                Match = match,
                ParticipantId = participantId,
                EventIndex = ++index,
                EventType = MatchItemEventType.Purchased,
                TimestampMs = atSeconds * 1000,
                ItemId = itemId,
                IsBuildRelevant = true,
                BuildCategory = category
            });
        Item(1, 5, 1056, BuildItemCategory.Starter);
        foreach (var extra in extraOpeningItems ?? [])
            Item(1, 8, extra, BuildItemCategory.Starter);
        Item(1, 480, 3020, BuildItemCategory.Boots);
        Item(1, 700, firstItem, BuildItemCategory.Legendary);
        Item(1, 1200, 3089, BuildItemCategory.Legendary);

        // Frames at minute 11, the minute the first item is bought (700s).
        void Frame(int participantId, int gold) =>
            db.MatchParticipantTimelineSnapshots.Add(new MatchParticipantTimelineSnapshot
            {
                MatchId = match.Id,
                Match = match,
                ParticipantId = participantId,
                MinuteMark = 11,
                Gold = gold,
                FrameTimestampMs = 660_000,
                DerivedAtUtc = DateTime.UtcNow
            });
        Frame(1, 5000 + ahriGoldLead);
        Frame(6, 5000);
    }

    private static MatchParticipant AddParticipant(
        TranscendenceContext db, Match match, int participantId, int teamId, int championId, bool win)
    {
        var summoner = new Summoner
        {
            Id = Guid.NewGuid(),
            PlatformRegion = "KR",
            Region = "asia",
            GameName = Guid.NewGuid().ToString("N")[..8],
            TagLine = "KR1",
            Puuid = Guid.NewGuid().ToString("N"),
            SummonerName = "s",
            RiotSummonerId = Guid.NewGuid().ToString("N")
        };
        db.Summoners.Add(summoner);
        var participant = new MatchParticipant
        {
            Id = Guid.NewGuid(),
            MatchId = match.Id,
            Match = match,
            SummonerId = summoner.Id,
            Summoner = summoner,
            Puuid = summoner.Puuid,
            ParticipantId = participantId,
            TeamId = teamId,
            ChampionId = championId,
            TeamPosition = "MIDDLE",
            Win = win,
            SummonerSpell1Id = 14,
            SummonerSpell2Id = 4
        };
        db.MatchParticipants.Add(participant);
        return participant;
    }

    private static MatchParticipantRune Rune(
        MatchParticipant participant, RuneSelectionTree tree, int index, int runeId) =>
        new()
        {
            MatchParticipantId = participant.Id,
            MatchParticipant = participant,
            RuneId = runeId,
            PatchVersion = participant.Match.Patch!,
            SelectionTree = tree,
            SelectionIndex = index
        };

    private TranscendenceContext NewDb() =>
        new(new DbContextOptionsBuilder<TranscendenceContext>()
            .UseNpgsql(fixture.ConnectionString)
            .Options);
}
