using System.Reflection;
using FluentAssertions;
using Hangfire;
using Transcendence.Service.Core.Services.Jobs;

namespace Transcendence.Service.Core.Tests;

/// <summary>
/// Guards the analytics jobs' lane isolation: they MUST run on the reserved
/// <see cref="HangfireQueues.AnalyticsWarm"/> queue, which is served by its own dedicated
/// <c>BackgroundJobServer</c> worker pool (Program.cs). That is what guarantees they always run on
/// schedule and never wait in line behind Riot-rate-limited ingestion/discovery jobs (which live on the
/// main and discovery pools). If a refactor dropped the <c>[Queue]</c> attribute, the job would fall back
/// to the shared "default" queue on the rate-limited main pool — exactly the stall this test prevents.
/// </summary>
public class AnalyticsLaneIsolationTests
{
    [Theory]
    [InlineData(typeof(RefreshPrecomputedAnalyticsJob))]
    [InlineData(typeof(RefreshBuildResourceAnalyticsJob))]
    [InlineData(typeof(WarmDefaultChampionProfilesJob))]
    public void AnalyticsJob_RunsOnDedicatedAnalyticsWarmLane(Type jobType)
    {
        var execute = jobType.GetMethod(nameof(WarmDefaultChampionProfilesJob.ExecuteAsync));
        execute.Should().NotBeNull($"{jobType.Name} must expose ExecuteAsync");

        var queue = execute!.GetCustomAttribute<QueueAttribute>();
        queue.Should().NotBeNull(
            $"{jobType.Name}.ExecuteAsync must carry [Queue(AnalyticsWarm)] so it runs on the dedicated, " +
            "rate-limit-free analytics pool and never queues behind Riot-throttled jobs");
        queue!.Queue.Should().Be(HangfireQueues.AnalyticsWarm);
    }
}
