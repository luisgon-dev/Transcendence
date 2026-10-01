using FluentAssertions;
using Hangfire;
using Hangfire.Common;
using Hangfire.PostgreSql;
using Hangfire.PostgreSql.Factories;
using Hangfire.States;
using Microsoft.Extensions.Options;
using Transcendence.Service.Core.Services.Extensions;
using Transcendence.Service.Core.Services.Jobs;
using Transcendence.Service.Core.Services.Jobs.Configuration;

namespace Transcendence.IntegrationTests;

/// <summary>
/// The queue a job lands in is decided by Hangfire at enqueue time (the recurring job's queue, then any
/// <c>[Queue]</c> filter), so reading attributes alone does not prove routing. These tests register the
/// worker's real recurring-job set against real Hangfire storage on Postgres, trigger every job, and
/// read back where each one was enqueued. <c>pro-roster-discovery</c> sat on an unserved
/// <c>maintenance</c> queue from 2026-07-23 to 2026-09-30, accepted and never run.
/// </summary>
[Collection(PostgresIntegrationCollection.Name)]
public sealed class HangfireQueueRoutingPostgresTests(PostgresIntegrationFixture fixture)
{
    [Fact]
    public void Every_recurring_job_is_enqueued_on_a_served_queue()
    {
        var storage = CreateStorage();
        var manager = new RecurringJobManager(storage);
        // Disabled jobs are routed too: a profile can turn any of them on. Retired IDs exist only so
        // startup can remove them, and refuse registration.
        string[] retired =
        [
            WorkerRecurringJobPolicy.RetiredCreateBuildLabGenerationJobId,
            WorkerRecurringJobPolicy.RetiredPromoteBuildLabGenerationJobId
        ];
        var descriptors = new WorkerRecurringJobPolicy(Options.Create(new WorkerSchedulingProfileOptions()))
            .BuildDescriptors(new WorkerJobScheduleOptions())
            .Where(descriptor => !retired.Contains(descriptor.JobId))
            .ToList();

        foreach (var descriptor in descriptors)
        {
            descriptor.Apply(manager);
            manager.Trigger(descriptor.JobId);
        }

        var queues = storage.GetMonitoringApi().Queues()
            .Where(queue => queue.Length > 0)
            .ToDictionary(queue => queue.Name, queue => queue.Length);

        queues.Values.Sum().Should().Be(descriptors.Count, "each recurring job was triggered once");
        queues.Keys.Should().OnlyContain(queue => HangfireQueues.Served.Contains(queue),
            "a job on a queue no worker pool serves is enqueued and never runs");
        storage.GetMonitoringApi().FindUnservedQueueBacklog(HangfireQueues.Served).Should().BeEmpty();
    }

    [Fact]
    public void Unserved_queue_backlog_reports_only_queues_no_pool_serves()
    {
        var storage = CreateStorage();
        var client = new BackgroundJobClient(storage);
        client.Create(Job.FromExpression(() => Console.WriteLine("stranded")), new EnqueuedState("maintenance"));
        client.Create(Job.FromExpression(() => Console.WriteLine("stranded")), new EnqueuedState("maintenance"));
        client.Create(Job.FromExpression(() => Console.WriteLine("served")), new EnqueuedState(HangfireQueues.Default));

        storage.GetMonitoringApi().FindUnservedQueueBacklog(HangfireQueues.Served)
            .Should().Equal(new Dictionary<string, long> { ["maintenance"] = 2 });
    }

    // Each test gets its own schema so jobs from one never count toward another's queues.
    private PostgreSqlStorage CreateStorage()
    {
        GlobalConfiguration.Configuration
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings();

        var options = new PostgreSqlStorageOptions
        {
            SchemaName = $"hangfire_routing_{Guid.NewGuid():N}"[..28],
            PrepareSchemaIfNecessary = true
        };
        return new PostgreSqlStorage(new NpgsqlConnectionFactory(fixture.ConnectionString, options), options);
    }
}
