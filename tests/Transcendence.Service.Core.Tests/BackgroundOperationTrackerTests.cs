using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Transcendence.Data;
using Transcendence.Service.Core.Services.Operations;
using Transcendence.Service.Core.Tests.Support;

namespace Transcendence.Service.Core.Tests;

public sealed class BackgroundOperationTrackerTests
{
    [Fact]
    public void Worker_filters_can_be_resolved_with_valid_Hangfire_orders()
    {
        using var provider = new ServiceCollection().AddBackgroundOperationWorker().BuildServiceProvider();
        provider.GetRequiredService<OperationExecutionFilter>().Order.Should().Be(-1);
        provider.GetRequiredService<OperationOutcomeFilter>().Order.Should().BeGreaterThan(20);
    }

    [Fact]
    public async Task Coalescing_uses_shared_execution_but_separate_typed_owner_requests()
    {
        await using var harness = await Harness.CreateAsync();
        var identity = Guid.NewGuid();
        var user = new OperationOwner(OperationOwnerKind.User, identity);
        var app = new OperationOwner(OperationOwnerKind.Application, identity);
        var lease = Guid.NewGuid();
        var first = await harness.Tracker.CreateAsync(OperationKinds.SummonerRefresh, "resource", Resource,
            lease, user, Dispatch);
        var repeated = await harness.Tracker.JoinAsync("resource", lease, user);
        var coalesced = await harness.Tracker.JoinAsync("resource", lease, app);
        repeated.Should().Be(first);
        coalesced.ExecutionId.Should().Be(first.ExecutionId);
        coalesced.OperationId.Should().NotBe(first.OperationId);
        (await harness.Tracker.GetAsync(first.OperationId, [app])).Should().BeNull();
        (await harness.Tracker.GetAsync(first.OperationId, [user])).Should().NotBeNull();
        (await harness.Tracker.GetAsync(coalesced.OperationId, [user])).Should().BeNull();
        (await harness.Tracker.GetAsync(coalesced.OperationId, [app, user])).Should().NotBeNull();
    }

    [Fact]
    public async Task Terminal_outcomes_are_monotonic_and_survive_new_context()
    {
        await using var harness = await Harness.CreateAsync();
        var owner = new OperationOwner(OperationOwnerKind.User, Guid.NewGuid());
        var tracked = await harness.Tracker.CreateAsync(OperationKinds.SummonerRefresh, "resource", Resource,
            Guid.NewGuid(), owner, Dispatch);
        (await harness.Tracker.StartAsync(tracked.ExecutionId)).Should().BeTrue();
        await harness.Tracker.SetPhaseAsync(tracked.ExecutionId, "profile", OperationStatuses.Succeeded);
        await harness.Tracker.SetPhaseAsync(tracked.ExecutionId, "recentHistory", OperationStatuses.Partial);
        await harness.Tracker.CompleteAsync(tracked.ExecutionId, OperationStatuses.Partial,
            new OperationResult(PersistedMatchCount: 3, DeferredMatchCount: 2));
        await harness.Tracker.FailAsync(tracked.ExecutionId, "late_failure");
        await harness.Tracker.RetryAsync(tracked.ExecutionId, "late_retry");
        (await harness.Tracker.StartAsync(tracked.ExecutionId)).Should().BeFalse();
        await using var fresh = new SqliteCompatibleTranscendenceContext(harness.Options);
        var status = await new BackgroundOperationTracker(fresh).GetAsync(tracked.OperationId, [owner]);
        status!.Status.Should().Be(OperationStatuses.Partial);
        status.Result.PersistedMatchCount.Should().Be(3);
        status.Result.DeferredMatchCount.Should().Be(2);
        status.RetryAfterSeconds.Should().Be(0);
        status.CompletedAtUtc.Should().NotBeNull();
        status.ErrorCode.Should().BeNull();
    }

    [Fact]
    public async Task Full_history_child_is_independently_owned_and_not_parent_completion()
    {
        await using var harness = await Harness.CreateAsync();
        var owner = new OperationOwner(OperationOwnerKind.User, Guid.NewGuid());
        var tracked = await harness.Tracker.CreateAsync(OperationKinds.SummonerRefresh, "resource", Resource,
            Guid.NewGuid(), owner, Dispatch);
        var child = await harness.Tracker.CreateChildAsync(tracked.ExecutionId, OperationKinds.FullHistory, "history", Guid.NewGuid());
        (await harness.Tracker.CreateChildAsync(tracked.ExecutionId, OperationKinds.FullHistory, "history", Guid.NewGuid()))
            .Should().Be(child);
        await harness.Tracker.CompleteAsync(tracked.ExecutionId, OperationStatuses.Succeeded, new OperationResult());
        var parent = await harness.Tracker.GetAsync(tracked.OperationId, [owner]);
        parent!.Status.Should().Be(OperationStatuses.Succeeded);
        var childRequest = parent.Phases.Single(x => x.Name == "fullHistory");
        childRequest.Status.Should().Be(OperationStatuses.Queued);
        childRequest.OperationId.Should().NotBeNull();
        var childStatus = await harness.Tracker.GetAsync(childRequest.OperationId!.Value, [owner]);
        childStatus!.Kind.Should().Be(OperationKinds.FullHistory);
        await harness.Tracker.FailAsync(child, "history_failed");
        (await harness.Tracker.GetAsync(tracked.OperationId, [owner]))!.Status.Should().Be(OperationStatuses.Succeeded);
        (await harness.Tracker.GetAsync(childRequest.OperationId.Value, [owner]))!.Status.Should().Be(OperationStatuses.Failed);
    }

    [Fact]
    public async Task Late_join_inherits_child_access_without_exposing_other_owner_request()
    {
        await using var harness = await Harness.CreateAsync();
        var firstOwner = new OperationOwner(OperationOwnerKind.User, Guid.NewGuid());
        var nextOwner = new OperationOwner(OperationOwnerKind.User, Guid.NewGuid());
        var lease = Guid.NewGuid();
        var first = await harness.Tracker.CreateAsync(OperationKinds.SummonerRefresh, "resource", Resource,
            lease, firstOwner, Dispatch);
        await harness.Tracker.CreateChildAsync(first.ExecutionId, OperationKinds.FullHistory, "history", Guid.NewGuid());
        var joined = await harness.Tracker.JoinAsync("resource", lease, nextOwner);
        var firstChild = (await harness.Tracker.GetAsync(first.OperationId, [firstOwner]))!.Phases.Single(x => x.Name == "fullHistory");
        var nextChild = (await harness.Tracker.GetAsync(joined.OperationId, [nextOwner]))!.Phases.Single(x => x.Name == "fullHistory");
        firstChild.OperationId!.Value.Should().NotBe(nextChild.OperationId!.Value);
        (await harness.Tracker.GetAsync(firstChild.OperationId!.Value, [nextOwner])).Should().BeNull();
        (await harness.Tracker.GetAsync(nextChild.OperationId!.Value, [nextOwner])).Should().NotBeNull();
    }

    [Fact]
    public async Task Retry_is_bounded_and_error_codes_cannot_contain_sensitive_exception_text()
    {
        await using var harness = await Harness.CreateAsync();
        var owner = new OperationOwner(OperationOwnerKind.Application, Guid.NewGuid());
        var tracked = await harness.Tracker.CreateAsync(OperationKinds.LiveGameProbe, "resource", Resource,
            Guid.NewGuid(), owner, Dispatch);
        await harness.Tracker.RetryAsync(tracked.ExecutionId, "rate_limited", DateTime.UtcNow.AddDays(1));
        var status = await harness.Tracker.GetAsync(tracked.OperationId, [owner]);
        status!.RetryAfterSeconds.Should().Be(30);
        var bad = () => harness.Tracker.FailAsync(tracked.ExecutionId, "Bearer secret-token");
        await bad.Should().ThrowAsync<ArgumentException>();
        await harness.Tracker.FailAsync(tracked.ExecutionId, "probe_failed");
        (await harness.Tracker.GetAsync(tracked.OperationId, [owner]))!.RetryAfterSeconds.Should().Be(0);
    }

    private static readonly OperationResource Resource = new("NA1", "Player", "NA1");
    private static readonly OperationDispatch Dispatch = new("owned-lock", null, null);
    private sealed class Harness(SqliteConnection connection, DbContextOptions<TranscendenceContext> options,
        SqliteCompatibleTranscendenceContext db) : IAsyncDisposable
    {
        public DbContextOptions<TranscendenceContext> Options => options;
        public BackgroundOperationTracker Tracker { get; } = new(db);
        public static async Task<Harness> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<TranscendenceContext>().UseSqlite(connection).Options;
            var db = new SqliteCompatibleTranscendenceContext(options);
            await db.Database.EnsureCreatedAsync();
            return new Harness(connection, options, db);
        }
        public async ValueTask DisposeAsync() { await db.DisposeAsync(); await connection.DisposeAsync(); }
    }
}
