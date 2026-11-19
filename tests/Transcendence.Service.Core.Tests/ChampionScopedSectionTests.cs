using FluentAssertions;
using Hangfire;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Account;
using Transcendence.Data.Models.LoL.Analytics;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Service.Core.Services.Analytics.Implementations;
using Transcendence.Service.Core.Services.Analytics.Models;
using Transcendence.Service.Core.Services.Jobs;
using Transcendence.Service.Core.Services.Jobs.Configuration;
using Transcendence.Service.Core.Services.Operations;
using Transcendence.Service.Core.Tests.Support;

namespace Transcendence.Service.Core.Tests;

public sealed class ChampionScopedSectionTests
{
    private static readonly ChampionSectionScope Solo = new("26.8", "RANKED_SOLO_DUO", null, "emerald+", "top", 103);

    [Fact]
    public void Canonical_key_is_bounded_and_preserves_all_scope_dimensions()
    {
        var scope = Solo.Normalize()!;
        scope.Key.Should().Be("RANKED_SOLO_DUO:ALL:EMERALD_PLUS:TOP:103");
        ChampionSectionScope.FromKey(scope.Patch, scope.Key).Should().Be(scope);
        (scope with { Region = " na1 ", RankTier = null }).Normalize()!.Key.Should().Be("RANKED_SOLO_DUO:NA1:ALL:TOP:103");
        (scope with { QueueFamily = "RANKED_FLEX", RankTier = "GOLD" }).Normalize()!.Key.Should().Be("RANKED_FLEX:ALL:GOLD:TOP:103");
        (scope with { QueueFamily = "ARAM", Role = null }).Normalize()!.Key.Should().Be("ARAM:ALL:EMERALD_PLUS:ALL:103");

        (scope with { Patch = new string('2', 33) }).Normalize().Should().BeNull();
        (scope with { Region = "NA1:EUW1" }).Normalize().Should().BeNull();
        (scope with { RankTier = "EMERALD_MINUS" }).Normalize().Should().BeNull();
        (scope with { QueueFamily = "FLEX" }).Normalize().Should().BeNull();
        (scope with { Role = "ALL" }).Normalize().Should().BeNull();
        ChampionSectionScope.FromKey(scope.Patch, scope.Key.Replace("103", "0103")).Should().BeNull();
    }

    [Fact]
    public void Newest_exact_result_is_ready_even_if_a_refresh_is_due()
    {
        var now = DateTime.UtcNow;
        var canonical = new ChampionScopedSection<string>(ChampionSectionState.Ready, now.AddHours(-4), "canonical");
        var exact = new ChampionScopedSection<string>(ChampionSectionState.Ready, now.AddHours(-3), "exact");

        var winner = ChampionScopedSectionService.Latest(canonical, exact);
        winner.Should().Be(exact);
        ChampionScopedSectionService.NeedsRefresh(winner, now).Should().BeTrue();
        winner!.State.Should().Be(ChampionSectionState.Ready);

        ChampionScopedSectionService.Latest(canonical with { ComputedAtUtc = now }, exact)!.Data.Should().Be("canonical");
        ChampionScopedSectionService.Latest(canonical with { ComputedAtUtc = null }, exact).Should().Be(exact);
    }

    [Fact]
    public void Demand_retries_with_backoff_before_becoming_unavailable()
    {
        var now = DateTime.UtcNow;
        var pending = new ChampionScopedSectionService.AttemptState(0, now);
        pending.IsDue(now).Should().BeTrue();

        var first = pending.Fail(now);
        first.IsDue(now.AddSeconds(59)).Should().BeFalse();
        first.IsDue(now.AddMinutes(1)).Should().BeTrue();

        var second = first.Fail(first.NextAttemptAtUtc);
        second.IsDue(now.AddMinutes(2)).Should().BeFalse();
        second.IsDue(now.AddMinutes(3)).Should().BeTrue();
        second.Fail(second.NextAttemptAtUtc).IsDue(now.AddDays(1)).Should().BeFalse();
    }

    [Fact]
    public void Worker_uses_the_shared_maintenance_gate_and_the_existing_warm_lane()
    {
        var jobType = typeof(RefreshRequestedChampionSectionsJob);
        StorageMaintenanceFilter.UsesGate(jobType).Should().BeTrue();
        var job = jobType.GetMethod(nameof(RefreshRequestedChampionSectionsJob.ExecuteAsync))!;
        ((QueueAttribute)Attribute.GetCustomAttribute(job, typeof(QueueAttribute))!).Queue.Should().Be(HangfireQueues.AnalyticsWarm);
        var schedule = new WorkerJobScheduleOptions();
        var policy = new WorkerRecurringJobPolicy(Options.Create(new WorkerSchedulingProfileOptions()));
        var descriptor = policy.BuildDescriptors(schedule).Single(x => x.JobId == WorkerRecurringJobPolicy.RefreshRequestedChampionSectionsJobId);
        descriptor.IsEnabled.Should().BeTrue();
        descriptor.CronExpression.Should().Be("* * * * *");

        schedule.EnableRefreshRequestedChampionSections = false;
        policy.BuildDescriptors(schedule).Single(x => x.JobId == descriptor.JobId).IsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task Strict_reads_use_exact_materialized_data_and_keep_section_readiness_independent()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = await OpenDbAsync(connection);
        var scope = Solo.Normalize()!;
        var now = DateTime.UtcNow;
        var canonical = new ChampionBuildsResponse(scope.ChampionId, scope.Role!, scope.RankTier!, scope.Region!, scope.Patch, [3078], []);
        db.ChampionBuildSnapshots.Add(new ChampionBuildSnapshot
        {
            Patch = scope.Patch, ChampionId = scope.ChampionId, Role = scope.Role!,
            RankScope = scope.RankTier!, Payload = BuildSnapshotSerialization.Serialize(canonical), ComputedAtUtc = now
        });
        db.AnalyticsResponseSnapshots.Add(new AnalyticsResponseSnapshot
        {
            Patch = scope.Patch, ScopeKey = scope.Key, Feature = ChampionScopedSectionService.BuildOutput, ComputedAtUtc = now.AddMinutes(1),
            Payload = AnalyticsSnapshotSerialization.Serialize(canonical with { GlobalCoreItems = [3031] })
        });
        await db.SaveChangesAsync();

        var builds = Builds(db);
        var matchups = new ChampionMatchupComputeService(db);
        var service = new ChampionScopedSectionService(db, builds, matchups, Options.Create(new MatchArchiveOptions()),
            Options.Create(new WorkerJobScheduleOptions()), NullLogger<ChampionScopedSectionService>.Instance);
        var exact = await service.GetAsync(Solo, scopedRoleGames: 5, CancellationToken.None);
        exact.Builds.State.Should().Be(ChampionSectionState.Ready);
        exact.Builds.ComputedAtUtc.Should().Be(now.AddMinutes(1));
        exact.Builds.Data!.GlobalCoreItems.Should().Equal(3031);
        exact.Matchups.State.Should().Be(ChampionSectionState.Unavailable);

        var region = await service.GetAsync(Solo with { Region = "NA1" }, scopedRoleGames: 5, CancellationToken.None);
        region.Builds.State.Should().Be(ChampionSectionState.Unavailable);
        region.Matchups.State.Should().Be(ChampionSectionState.Unavailable);
    }

    [Fact]
    public async Task Worker_compute_rejects_excess_actual_input_and_missing_source()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = await OpenDbAsync(connection);
        var scope = (Solo with { RankTier = null, Region = "NA1" }).Normalize()!;
        for (var i = 0; i < 2; i++)
        {
            var match = new Match
            {
                MatchId = Guid.NewGuid().ToString("N"), Patch = scope.Patch, QueueId = 420,
                QueueFamily = scope.QueueFamily, Status = FetchStatus.Success, PlatformRegion = scope.Region
            };
            AddParticipant(db, match, scope.ChampionId, 100, 1);
            AddParticipant(db, match, 99, 200, 2);
        }
        await db.SaveChangesAsync();

        var builds = Builds(db);
        var matchups = new ChampionMatchupComputeService(db);
        (await builds.ComputeScopedBuildsAsync(scope, 1, CancellationToken.None)).Should().BeNull();
        (await matchups.ComputeScopedMatchupsAsync(scope, 1, CancellationToken.None)).Should().BeNull();

        (await builds.ComputeScopedBuildsAsync(scope, 2, CancellationToken.None)).Should().NotBeNull();
        var completed = await matchups.ComputeScopedMatchupsAsync(scope, 2, CancellationToken.None);
        completed!.AllMatchups.Should().ContainSingle().Which.Games.Should().Be(2);

        var noGames = scope with { Region = "EUW1" };
        (await builds.ComputeScopedBuildsAsync(noGames, 2, CancellationToken.None)).Should().BeNull();
        (await matchups.ComputeScopedMatchupsAsync(noGames, 2, CancellationToken.None)).Should().BeNull();
    }

    private static async Task<TranscendenceContext> OpenDbAsync(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<TranscendenceContext>().UseSqlite(connection).Options;
        var db = new SqliteCompatibleTranscendenceContext(options);
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    private static ChampionBuildComputeService Builds(TranscendenceContext db) =>
        new(db, Options.Create(new ChampionAnalyticsComputeOptions()), NullLogger<ChampionBuildComputeService>.Instance);

    private static void AddParticipant(TranscendenceContext db, Match match, int championId, int team, int participantId)
    {
        var token = Guid.NewGuid().ToString("N");
        var participant = new MatchParticipant
        {
            Match = match,
            Summoner = new Summoner { Puuid = token, RiotSummonerId = token, PlatformRegion = "NA1", Region = "americas" },
            TeamPosition = "TOP", ParticipantId = participantId, ChampionId = championId, TeamId = team, Win = team == 100
        };
        db.MatchParticipants.Add(participant);
    }
}
