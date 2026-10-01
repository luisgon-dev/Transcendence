using System.Reflection;
using FluentAssertions;
using Hangfire;
using Transcendence.Service.Core.Services.Jobs;

namespace Transcendence.Service.Core.Tests.Jobs;

/// <summary>
/// Hangfire accepts a job for any queue name, and a job in a queue no worker pool serves simply never
/// runs. <c>ProRosterDiscoveryJob</c> sat on an unserved <c>maintenance</c> queue from 2026-07-23 with
/// nothing failing. Every <c>[Queue]</c> in the job assembly must name a queue the worker serves.
/// </summary>
public class HangfireQueueRoutingTests
{
    private const BindingFlags AllDeclared =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static |
        BindingFlags.DeclaredOnly;

    [Fact]
    public void Every_queue_attribute_names_a_served_queue()
    {
        var routes = QueueRoutes().ToList();

        routes.Should().NotBeEmpty("the job assembly routes jobs with [Queue]");
        routes.Where(route => !HangfireQueues.Served.Contains(route.Queue))
            .Select(route => $"{route.Member} -> {route.Queue}")
            .Should().BeEmpty("a job on a queue no worker pool serves is enqueued and never runs");
    }

    [Fact]
    public void Pro_roster_discovery_runs_on_the_default_queue()
    {
        QueueRoutes().Should().Contain(($"{nameof(ProRosterDiscoveryJob)}.{nameof(ProRosterDiscoveryJob.ExecuteAsync)}",
            HangfireQueues.Default));
    }

    private static IEnumerable<(string Member, string Queue)> QueueRoutes()
    {
        foreach (var type in typeof(HangfireQueues).Assembly.GetTypes())
        {
            foreach (var attribute in type.GetCustomAttributes<QueueAttribute>(inherit: false))
                yield return (type.Name, attribute.Queue);

            foreach (var method in type.GetMethods(AllDeclared))
            foreach (var attribute in method.GetCustomAttributes<QueueAttribute>(inherit: false))
                yield return ($"{type.Name}.{method.Name}", attribute.Queue);
        }
    }
}
