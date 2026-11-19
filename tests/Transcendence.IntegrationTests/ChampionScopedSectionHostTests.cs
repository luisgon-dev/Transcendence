using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Account;
using Transcendence.Data.Models.LoL.Analytics;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Data.Models.LoL.Static;
using Transcendence.Data.Models.Service;
using Transcendence.Service.Core.Services.Analytics.Implementations;
using Transcendence.Service.Core.Services.Analytics.Models;
using Transcendence.Service.Core.Services.Jobs;

namespace Transcendence.IntegrationTests;

[Collection(PostgresIntegrationCollection.Name)]
public sealed class ChampionScopedSectionHostTests(PostgresIntegrationFixture fixture)
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private const string Flex = "RANKED_FLEX";
    private const string Solo = "RANKED_SOLO_DUO";

    [Fact]
    public async Task Pending_sections_publish_exact_data_and_keep_the_last_success_during_refresh()
    {
        var scope = new ChampionSectionScope("997.1", Flex, "NA1", "GOLD", "TOP", 103).Normalize()!;
        await using var host = fixture.Factory.Services.CreateAsyncScope();
        var db = host.ServiceProvider.GetRequiredService<TranscendenceContext>();
        db.Patches.Add(new Patch { Version = scope.Patch, ReleaseDate = DateTime.UtcNow, IsActive = true });
        db.ChampionRoleTierStats.Add(RoleStat(scope));
        for (var i = 0; i < 3; i++)
            AddGame(db, scope, opponent: 101, item: 3078);

        AddGame(db, scope with { Region = "EUW1" }, opponent: 102, item: 3031);
        AddGame(db, scope with { RankTier = "DIAMOND" }, opponent: 104, item: 3031);
        AddGame(db, scope with { QueueFamily = Solo }, opponent: 105, item: 3031);
        AddGame(db, scope with { Role = "JUNGLE" }, opponent: 106, item: 3031);
        await db.SaveChangesAsync();

        try
        {
            var invalid = await ReadAsync(scope with { RankTier = "DIAMOND" });
            invalid.Builds.State.Should().Be(ChampionSectionState.Unavailable);
            invalid.Matchups.State.Should().Be(ChampionSectionState.Unavailable);

            var pending = await ReadAsync(scope);
            pending.Builds.State.Should().Be(ChampionSectionState.Preparing);
            pending.Matchups.State.Should().Be(ChampionSectionState.Preparing);
            (await ReadAsync(scope)).Should().Be(pending);

            (await OwnedRows(db, scope.Patch).CountAsync()).Should().Be(2);
            await DrainAsync();
            var ready = await ReadAsync(scope);
            ready.Builds.State.Should().Be(ChampionSectionState.Ready);
            ready.Builds.Data!.GlobalCoreItems.Should().Equal(3078);
            ready.Builds.Data.Builds.Should().ContainSingle().Which.Games.Should().Be(3);
            ready.Builds.Data.QueueFamily.Should().Be(Flex);
            ready.Builds.Data.Region.Should().Be("NA1");
            ready.Builds.Data.RankTier.Should().Be("GOLD");
            ready.Builds.ComputedAtUtc.Should().NotBeNull();
            ready.Matchups.State.Should().Be(ChampionSectionState.Ready);
            var matchup = ready.Matchups.Data!.AllMatchups.Should().ContainSingle().Which;
            matchup.OpponentChampionId.Should().Be(101);
            matchup.Games.Should().Be(3);
            ready.Matchups.Data.QueueFamily.Should().Be(Flex);
            ready.Matchups.Data.Region.Should().Be("NA1");
            ready.Matchups.Data.RankTier.Should().Be("GOLD");
            ready.Matchups.ComputedAtUtc.Should().NotBeNull();
            (await OwnedRows(db, scope.Patch).CountAsync()).Should().Be(2);

            var stale = DateTime.UtcNow.AddHours(-3);
            await OwnedRows(db, scope.Patch)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.ComputedAtUtc, stale));
            var refreshing = await ReadAsync(scope);
            refreshing.Builds.State.Should().Be(ChampionSectionState.Ready);
            refreshing.Builds.Data.Should().BeEquivalentTo(ready.Builds.Data);
            refreshing.Matchups.State.Should().Be(ChampionSectionState.Ready);
            refreshing.Matchups.Data.Should().BeEquivalentTo(ready.Matchups.Data);
            (await OwnedRows(db, scope.Patch).CountAsync()).Should().Be(4);

            await DrainAsync();
            var refreshed = await ReadAsync(scope);
            refreshed.Builds.ComputedAtUtc.Should().BeAfter(ready.Builds.ComputedAtUtc!.Value);
            refreshed.Matchups.ComputedAtUtc.Should().BeAfter(ready.Matchups.ComputedAtUtc!.Value);
            (await OwnedRows(db, scope.Patch).CountAsync()).Should().Be(2);
        }
        finally
        {
            await OwnedRows(db, scope.Patch).ExecuteDeleteAsync();
            await db.Matches.Where(row => row.Patch == scope.Patch).ExecuteDeleteAsync();
            await db.ChampionRoleTierStats.Where(row => row.Patch == scope.Patch).ExecuteDeleteAsync();
            await db.Patches.Where(row => row.Version == scope.Patch).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task Global_pending_cap_and_nonblocking_admission_are_enforced_across_host_scopes()
    {
        var scope = new ChampionSectionScope("997.2", Flex, "NA1", "GOLD", "TOP", 103).Normalize()!;
        await using var host = fixture.Factory.Services.CreateAsyncScope();
        var db = host.ServiceProvider.GetRequiredService<TranscendenceContext>();
        var now = DateTime.UtcNow;
        db.Patches.Add(new Patch { Version = scope.Patch, ReleaseDate = now, IsActive = true });
        db.ChampionRoleTierStats.Add(RoleStat(scope));
        db.AnalyticsResponseSnapshots.AddRange(Enumerable.Range(0, ChampionScopedSectionService.PendingLimit - 1)
            .Select(i => Demand(scope with { ChampionId = 1_000 + i }, ChampionScopedSectionService.BuildDemand, now)));
        await db.SaveChangesAsync();

        try
        {
            await using (var held = await db.Database.BeginTransactionAsync())
            {
                await db.Database.ExecuteSqlRawAsync(
                    "SELECT pg_advisory_xact_lock(hashtextextended('champion-section-demand-v1', 0))");
                var blocked = await ReadAsync(scope);
                blocked.Builds.State.Should().Be(ChampionSectionState.Unavailable);
                blocked.Matchups.State.Should().Be(ChampionSectionState.Unavailable);
                await held.RollbackAsync();
            }

            var capped = await ReadAsync(scope);
            capped.Builds.State.Should().Be(ChampionSectionState.Preparing);
            capped.Matchups.State.Should().Be(ChampionSectionState.Unavailable);
            (await ReadAsync(scope)).Should().Be(capped);
            (await OwnedRows(db, scope.Patch).CountAsync()).Should().Be(ChampionScopedSectionService.PendingLimit);
        }
        finally
        {
            await OwnedRows(db, scope.Patch).ExecuteDeleteAsync();
            await db.ChampionRoleTierStats.Where(row => row.Patch == scope.Patch).ExecuteDeleteAsync();
            await db.Patches.Where(row => row.Version == scope.Patch).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task Retained_history_with_pruned_source_never_publishes_ready_empty()
    {
        var scope = new ChampionSectionScope("997.3", Flex, "NA1", "GOLD", "TOP", 103).Normalize()!;
        const string active = "997.4";
        await using var host = fixture.Factory.Services.CreateAsyncScope();
        var db = host.ServiceProvider.GetRequiredService<TranscendenceContext>();
        var now = DateTime.UtcNow;
        var previousActive = await db.Patches.Where(row => row.IsActive).Select(row => row.Version).ToListAsync();
        await db.Patches.Where(row => row.IsActive).ExecuteUpdateAsync(update => update.SetProperty(row => row.IsActive, false));
        db.Patches.AddRange(
            new Patch { Version = active, ReleaseDate = now.AddYears(5), IsActive = true },
            new Patch { Version = scope.Patch, ReleaseDate = now.AddYears(5).AddDays(-1), IsActive = false });
        db.ChampionRoleTierStats.Add(RoleStat(scope));
        db.MatchArchiveChunks.Add(new MatchArchiveChunk
        {
            Patch = scope.Patch, RelativePath = scope.Patch, FrozenMatches = 3, PrunedMatches = 3,
            CreatedAtUtc = now, VerifiedAtUtc = now, CompletedAtUtc = now
        });
        await db.SaveChangesAsync();

        try
        {
            (await ReadAsync(scope)).Builds.State.Should().Be(ChampionSectionState.Preparing);
            await DrainAsync();
            var unavailable = await ReadAsync(scope);
            unavailable.Builds.State.Should().Be(ChampionSectionState.Unavailable);
            unavailable.Builds.Data.Should().BeNull();
            unavailable.Matchups.State.Should().Be(ChampionSectionState.Unavailable);
            unavailable.Matchups.Data.Should().BeNull();
            (await OwnedRows(db, scope.Patch).AnyAsync(row =>
                row.Feature == ChampionScopedSectionService.BuildOutput || row.Feature == ChampionScopedSectionService.MatchupOutput))
                .Should().BeFalse();
        }
        finally
        {
            await OwnedRows(db, scope.Patch).ExecuteDeleteAsync();
            await db.MatchArchiveChunks.Where(row => row.Patch == scope.Patch).ExecuteDeleteAsync();
            await db.ChampionRoleTierStats.Where(row => row.Patch == scope.Patch).ExecuteDeleteAsync();
            await db.Patches.Where(row => row.Version == scope.Patch || row.Version == active).ExecuteDeleteAsync();
            await db.Patches.Where(row => previousActive.Contains(row.Version))
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsActive, true));
        }
    }

    private async Task<ChampionScopedSections> ReadAsync(ChampionSectionScope scope)
    {
        await using var host = fixture.Factory.Services.CreateAsyncScope();
        return await host.ServiceProvider.GetRequiredService<ChampionScopedSectionService>().GetAsync(scope, 3, Ct);
    }

    private async Task DrainAsync()
    {
        await using var host = fixture.Factory.Services.CreateAsyncScope();
        var job = ActivatorUtilities.CreateInstance<RefreshRequestedChampionSectionsJob>(host.ServiceProvider);
        await job.ExecuteAsync(Ct);
    }

    private static IQueryable<AnalyticsResponseSnapshot> OwnedRows(TranscendenceContext db, string patch) =>
        db.AnalyticsResponseSnapshots.Where(row => row.Patch == patch &&
            (row.Feature == ChampionScopedSectionService.BuildDemand || row.Feature == ChampionScopedSectionService.MatchupDemand ||
             row.Feature == ChampionScopedSectionService.BuildOutput || row.Feature == ChampionScopedSectionService.MatchupOutput));

    private static AnalyticsResponseSnapshot Demand(ChampionSectionScope scope, string feature, DateTime at) =>
        new()
        {
            Patch = scope.Patch, ScopeKey = scope.Key, Feature = feature, ComputedAtUtc = at,
            Payload = AnalyticsSnapshotSerialization.Serialize(new ChampionScopedSectionService.AttemptState(0, at))
        };

    private static ChampionRoleTierStat RoleStat(ChampionSectionScope scope) =>
        new()
        {
            Patch = scope.Patch, Role = scope.Role!, PlatformRegion = scope.Region!, RankTier = scope.RankTier!,
            QueueFamily = scope.QueueFamily, ChampionId = scope.ChampionId, Games = 3, Wins = 3,
            ComputedAtUtc = DateTime.UtcNow
        };

    private static void AddGame(TranscendenceContext db, ChampionSectionScope scope, int opponent, int item)
    {
        var match = new Match
        {
            MatchId = Guid.NewGuid().ToString("N"), Patch = scope.Patch, PlatformRegion = scope.Region,
            QueueId = scope.QueueFamily == Flex ? 440 : 420, QueueFamily = scope.QueueFamily,
            Status = FetchStatus.Success, Duration = 1500, FetchedAt = DateTime.UtcNow
        };
        var player = Participant(match, scope, scope.ChampionId, 100, id: 1);
        player.Items.Add(new MatchParticipantItem { SlotIndex = 0, ItemId = item, PatchVersion = scope.Patch });
        db.MatchParticipants.AddRange(player, Participant(match, scope, opponent, 200, id: 2));
    }

    private static MatchParticipant Participant(Match match, ChampionSectionScope scope, int champion, int team, int id)
    {
        var token = Guid.NewGuid().ToString("N");
        var summoner = new Summoner
        {
            Puuid = token, RiotSummonerId = token, PlatformRegion = scope.Region, Region = "americas"
        };
        summoner.Ranks.Add(new Rank
        {
            QueueType = scope.QueueFamily == Flex ? "RANKED_FLEX_SR" : "RANKED_SOLO_5x5", Tier = scope.RankTier!
        });
        return new MatchParticipant
        {
            Match = match, Summoner = summoner, ChampionId = champion, TeamPosition = scope.Role,
            ParticipantId = id, TeamId = team, Win = team == 100
        };
    }
}
