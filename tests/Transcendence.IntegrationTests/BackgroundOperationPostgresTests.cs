using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Hangfire;
using Hangfire.Common;
using Hangfire.PostgreSql;
using Hangfire.PostgreSql.Factories;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Transcendence.Data;
using Transcendence.Data.Models.Auth;
using Transcendence.Data.Models.LoL.Account;
using Transcendence.Service.Core.Services.Auth.Interfaces;
using Transcendence.Service.Core.Services.Auth.Models;
using Transcendence.Service.Core.Services.Jobs;
using Transcendence.Service.Core.Services.Jobs.Interfaces;
using Transcendence.Service.Core.Services.Operations;

namespace Transcendence.IntegrationTests;

[Collection(PostgresIntegrationCollection.Name)]
public sealed class BackgroundOperationPostgresTests(PostgresIntegrationFixture fixture)
{
    private static readonly OperationResource Resource = new("NA1", "Operation Player", "NA1");

    [Fact]
    public async Task Durable_dispatch_is_idempotent_across_concurrent_new_contexts()
    {
        var tracked = await CreateAsync();
        var storage = Storage();
        await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            using var scope = fixture.Factory.Services.CreateScope();
            await new BackgroundOperationDispatcher(Db(scope), new BackgroundJobClient(storage))
                .DispatchAsync(tracked.ExecutionId);
        }));
        using var read = fixture.Factory.Services.CreateScope();
        var row = await Db(read).BackgroundOperations.AsNoTracking().SingleAsync(x => x.Id == tracked.ExecutionId);
        row.DispatchedAtUtc.Should().NotBeNull();
        row.HangfireJobId.Should().NotBeNull();
        storage.GetMonitoringApi().Queues().Sum(x => x.Length).Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Lost_dispatch_ack_recovers_with_same_execution_identity(bool persistedBeforeCrash)
    {
        var tracked = await CreateAsync();
        var storage = Storage();
        var realClient = new BackgroundJobClient(storage);
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var dispatcher = new BackgroundOperationDispatcher(Db(scope), new InterruptedClient(realClient, persistedBeforeCrash));
            await FluentActions.Invoking(() => dispatcher.DispatchAsync(tracked.ExecutionId)).Should().ThrowAsync<IOException>();
        }
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            (await Db(scope).BackgroundOperations.AsNoTracking().SingleAsync(x => x.Id == tracked.ExecutionId))
                .DispatchedAtUtc.Should().BeNull();
            await new BackgroundOperationDispatcher(Db(scope), realClient).DispatchAsync(tracked.ExecutionId);
        }
        // Queue constants are authoritative; never assume their spelling.
        var jobs = storage.GetMonitoringApi().Queues().SelectMany(q =>
            storage.GetMonitoringApi().EnqueuedJobs(q.Name, 0, 10)).Select(x => x.Value).ToList();
        jobs.Should().HaveCount(persistedBeforeCrash ? 2 : 1);
        jobs.Should().OnlyContain(x => (Guid)x.Job.Args[0] == tracked.ExecutionId);
    }

    [Fact]
    public async Task Elected_retry_then_exhausted_failure_is_terminal_and_monotonic()
    {
        var tracked = await CreateAsync();
        var storage = Storage();
        using var provider = FiltersProvider();
        var filters = new JobFilterCollection();
        filters.Add(provider.GetRequiredService<OperationExecutionFilter>());
        filters.Add(new AutomaticRetryAttribute { Attempts = 1, DelaysInSeconds = [1], Order = 20 });
        filters.Add(provider.GetRequiredService<OperationOutcomeFilter>());
        var client = new BackgroundJobClient(storage, filters);
        string jobId;
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            await new BackgroundOperationDispatcher(Db(scope), client).DispatchAsync(tracked.ExecutionId);
            jobId = (await Db(scope).BackgroundOperations.AsNoTracking().SingleAsync(x => x.Id == tracked.ExecutionId)).HangfireJobId!;
        }
        // A redirected election returns false for the originally requested Failed state.
        client.ChangeState(jobId, new FailedState(new IOException("internal exception must not be exposed"))).Should().BeFalse();
        (await StatusAsync(tracked)).Status.Should().Be(OperationStatuses.Retrying);
        using (var connection = storage.GetConnection()) connection.SetJobParameter(jobId, "RetryCount", "1");
        client.ChangeState(jobId, new FailedState(new IOException("secret detail"))).Should().BeTrue();
        var failed = await StatusAsync(tracked);
        failed.Status.Should().Be(OperationStatuses.Failed);
        failed.ErrorCode.Should().Be("worker_failed");
        failed.CompletedAtUtc.Should().NotBeNull();
        client.ChangeState(jobId, new ScheduledState(TimeSpan.FromSeconds(1))).Should().BeTrue();
        (await StatusAsync(tracked)).Status.Should().Be(OperationStatuses.Failed);
    }

    [Fact]
    public async Task Busy_deferral_does_not_consume_retry_budget_or_mark_domain_failure()
    {
        var tracked = await CreateAsync();
        var storage = Storage();
        using var provider = FiltersProvider();
        var filters = new JobFilterCollection();
        filters.Add(provider.GetRequiredService<OperationExecutionFilter>());
        filters.Add(new AutomaticRetryAttribute { Attempts = 1, Order = 20 });
        filters.Add(provider.GetRequiredService<OperationOutcomeFilter>());
        var client = new BackgroundJobClient(storage, filters);
        using var scope = fixture.Factory.Services.CreateScope();
        await new BackgroundOperationDispatcher(Db(scope), client).DispatchAsync(tracked.ExecutionId);
        var row = await Db(scope).BackgroundOperations.AsNoTracking().SingleAsync(x => x.Id == tracked.ExecutionId);
        client.ChangeState(row.HangfireJobId!, new FailedState(new OperationExecutionBusyException())).Should().BeFalse();
        using var connection = storage.GetConnection();
        connection.GetStateData(row.HangfireJobId!).Name.Should().Be("Scheduled");
        connection.GetJobParameter(row.HangfireJobId!, "RetryCount").Should().BeNull();
        (await StatusAsync(tracked)).Status.Should().Be(OperationStatuses.Queued);
        client.ChangeState(row.HangfireJobId!, new DeletedState()).Should().BeTrue();
        (await StatusAsync(tracked)).ErrorCode.Should().Be("job_cancelled");
    }

    [Fact]
    public async Task Real_worker_shutdown_releases_execution_lock_and_resumes_same_operation()
    {
        var tracked = await CreateAsync();
        var storage = Storage();
        using var provider = FiltersProvider();
        var filters = new JobFilterCollection();
        filters.Add(provider.GetRequiredService<OperationExecutionFilter>());
        filters.Add(new AutomaticRetryAttribute { Attempts = 1, Order = 20 });
        filters.Add(provider.GetRequiredService<OperationOutcomeFilter>());
        var body = new InterruptibleProbe(provider);
        var options = new BackgroundJobServerOptions
        {
            Activator = new ProbeActivator(body), FilterProvider = filters,
            Queues = [HangfireQueues.RefreshHigh], WorkerCount = 2,
            CancellationCheckInterval = TimeSpan.FromMilliseconds(100),
            SchedulePollingInterval = TimeSpan.FromMilliseconds(100),
            ShutdownTimeout = TimeSpan.FromSeconds(5)
        };
        using (var server = new BackgroundJobServer(options, storage))
        {
            using var scope = fixture.Factory.Services.CreateScope();
            await new BackgroundOperationDispatcher(Db(scope), new BackgroundJobClient(storage, filters))
                .DispatchAsync(tracked.ExecutionId);
            await body.Started.Task.WaitAsync(TimeSpan.FromSeconds(20));
            (await StatusAsync(tracked)).Status.Should().Be(OperationStatuses.Running);
            var row = await Db(scope).BackgroundOperations.AsNoTracking().SingleAsync(x => x.Id == tracked.ExecutionId);
            using var connection = storage.GetConnection();
            var duplicate = new BackgroundJobClient(storage, filters).Create(
                connection.GetJobData(row.HangfireJobId!).Job, new EnqueuedState(HangfireQueues.RefreshHigh));
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (connection.GetStateData(duplicate)?.Name != "Scheduled" && DateTime.UtcNow < deadline)
                await Task.Delay(50);
            connection.GetStateData(duplicate).Name.Should().Be("Scheduled");
            connection.GetJobParameter(duplicate, "RetryCount").Should().BeNull();
            body.Runs.Should().Be(1, "a duplicate must not execute while the original holds its PostgreSQL lock");
        }
        // Hangfire returns interrupted processing work to its queue. A new server
        // must acquire the released PostgreSQL session lock and finish that identity.
        using (var restarted = new BackgroundJobServer(options, storage))
        {
            await body.Completed.Task.WaitAsync(TimeSpan.FromSeconds(20));
            (await StatusAsync(tracked)).Status.Should().Be(OperationStatuses.Succeeded);
            body.Runs.Should().Be(2);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Exhausted_history_failure_updates_only_its_matching_progress_row(bool newerCursorOwner)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = Db(scope);
        var summoner = new Summoner { Id = Guid.NewGuid(), Puuid = Guid.NewGuid().ToString(),
            GameName = "History", TagLine = "NA1", PlatformRegion = "NA1", Region = "AMERICAS" };
        db.Summoners.Add(summoner);
        await db.SaveChangesAsync();
        var tracker = scope.ServiceProvider.GetRequiredService<IBackgroundOperationTracker>();
        var parent = await tracker.CreateAsync(OperationKinds.SummonerRefresh, Guid.NewGuid().ToString(), Resource,
            Guid.NewGuid(), owner, new("unused", null, null));
        var child = await tracker.CreateChildAsync(parent.ExecutionId, OperationKinds.FullHistory,
            $"history:{summoner.Id}", summoner.Id);
        db.SummonerFullHistoryBackfills.Add(new SummonerFullHistoryBackfill
        {
            Id = Guid.NewGuid(), SummonerId = summoner.Id, OperationId = newerCursorOwner ? Guid.NewGuid() : child,
            Scope = SummonerFullHistoryScopes.FullHistory, Status = SummonerFullHistoryBackfillStatuses.Running
        });
        await db.SaveChangesAsync();
        var storage = Storage();
        using var provider = FiltersProvider();
        var filters = new JobFilterCollection();
        filters.Add(new AutomaticRetryAttribute { Attempts = 0, Order = 20 });
        filters.Add(provider.GetRequiredService<OperationOutcomeFilter>());
        var client = new BackgroundJobClient(storage, filters);
        await new BackgroundOperationDispatcher(db, client).DispatchAsync(child);
        var row = await db.BackgroundOperations.AsNoTracking().SingleAsync(x => x.Id == child);
        client.ChangeState(row.HangfireJobId!, new FailedState(new IOException("internal history failure"))).Should().BeTrue();
        var stored = await db.SummonerFullHistoryBackfills.AsNoTracking().SingleAsync(x => x.SummonerId == summoner.Id);
        stored.Status.Should().Be(newerCursorOwner ? SummonerFullHistoryBackfillStatuses.Running : SummonerFullHistoryBackfillStatuses.Failed);
        (await db.BackgroundOperations.AsNoTracking().SingleAsync(x => x.Id == child)).Status.Should().Be(OperationStatuses.Failed);
    }

    [Fact]
    public async Task Real_authentication_distinguishes_user_and_application_even_with_both_headers()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var user = new UserAccount { Id = Guid.NewGuid(), Email = "operation@test.local", EmailNormalized = "OPERATION@TEST.LOCAL" };
        var token = scope.ServiceProvider.GetRequiredService<IJwtService>().GenerateAccessToken(user);
        var key = await scope.ServiceProvider.GetRequiredService<IApiKeyService>().CreateAsync(new ApiKeyCreateRequest("operation-test"));
        var tracker = scope.ServiceProvider.GetRequiredService<IBackgroundOperationTracker>();
        var userOp = await tracker.CreateAsync(OperationKinds.SummonerRefresh, Guid.NewGuid().ToString(), Resource,
            Guid.NewGuid(), new(OperationOwnerKind.User, user.Id), new("unused", null, user.Id));
        var appOp = await tracker.CreateAsync(OperationKinds.LiveGameProbe, Guid.NewGuid().ToString(), Resource,
            Guid.NewGuid(), new(OperationOwnerKind.Application, key.Id), new("unused", null, null));
        using var anonymous = fixture.Factory.CreateClient();
        (await anonymous.GetAsync(Path(userOp))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using var app = fixture.Factory.CreateClient();
        app.DefaultRequestHeaders.Add("X-API-Key", key.PlaintextKey);
        (await app.GetAsync(Path(userOp))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var signedIn = fixture.Factory.CreateClient();
        signedIn.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        (await signedIn.GetAsync(Path(appOp))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        signedIn.DefaultRequestHeaders.Add("X-API-Key", key.PlaintextKey);
        foreach (var op in new[] { userOp, appOp })
        {
            var response = await signedIn.GetAsync(Path(op));
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            response.Headers.CacheControl!.NoStore.Should().BeTrue();
        }
    }

    private static string Path(TrackedOperation op) => $"/api/lol/operations/{op.OperationId}";
    private static TranscendenceContext Db(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<TranscendenceContext>();
    private readonly OperationOwner owner = new(OperationOwnerKind.User, Guid.NewGuid());
    private async Task<TrackedOperation> CreateAsync()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IBackgroundOperationTracker>().CreateAsync(
            OperationKinds.LiveGameProbe, Guid.NewGuid().ToString(), Resource, Guid.NewGuid(), owner, new("unused", null, null));
    }
    private async Task<OperationStatusResponse> StatusAsync(TrackedOperation op)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<IBackgroundOperationTracker>().GetAsync(op.OperationId, [owner]))!;
    }
    private ServiceProvider FiltersProvider() => new ServiceCollection().AddLogging()
        .AddDbContext<TranscendenceContext>(x => x.UseNpgsql(fixture.ConnectionString))
        .AddScoped<IBackgroundOperationTracker, BackgroundOperationTracker>()
        .AddBackgroundOperationWorker().BuildServiceProvider();
    private PostgreSqlStorage Storage()
    {
        var options = new PostgreSqlStorageOptions { SchemaName = $"operations_{Guid.NewGuid():N}"[..28],
            PrepareSchemaIfNecessary = true, QueuePollInterval = TimeSpan.FromMilliseconds(100) };
        return new(new NpgsqlConnectionFactory(fixture.ConnectionString, options), options);
    }
    private sealed class ProbeActivator(InterruptibleProbe body) : JobActivator
    {
        public override object ActivateJob(Type jobType) => jobType == typeof(ILiveGameProbeJob)
            ? body : throw new InvalidOperationException("Unexpected integration job.");
    }
    private sealed class InterruptibleProbe(IServiceProvider services) : ILiveGameProbeJob
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Runs;
        public async Task ProbeAsync(Guid operationId, string platformRegion, string gameName,
            string tagLine, string lockHandle, CancellationToken ct = default)
        {
            using var scope = services.CreateScope();
            var tracker = scope.ServiceProvider.GetRequiredService<IBackgroundOperationTracker>();
            (await tracker.StartAsync(operationId, ct)).Should().BeTrue();
            if (Interlocked.Increment(ref Runs) == 1)
            {
                Started.TrySetResult();
                await Task.Delay(Timeout.Infinite, ct);
            }
            await tracker.CompleteAsync(operationId, OperationStatuses.Succeeded, new OperationResult(), ct);
            Completed.TrySetResult();
        }
    }
    private sealed class InterruptedClient(IBackgroundJobClient inner, bool enqueueBeforeThrow) : IBackgroundJobClient
    {
        public string Create(Job job, IState state)
        {
            if (enqueueBeforeThrow) inner.Create(job, state);
            throw new IOException("simulated process loss around enqueue acknowledgement");
        }
        public bool ChangeState(string jobId, IState state, string? expectedState) => inner.ChangeState(jobId, state, expectedState);
    }
}
