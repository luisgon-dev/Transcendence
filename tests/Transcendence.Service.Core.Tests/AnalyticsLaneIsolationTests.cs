using System.Reflection;
using FluentAssertions;
using Hangfire;
using Transcendence.Service.Core.Services.Jobs;

namespace Transcendence.Service.Core.Tests;

// Cache refreshes and heavy batch work have separate reserved worker pools.
public class AnalyticsLaneIsolationTests
{
    [Theory]
    [InlineData(typeof(RefreshPrecomputedAnalyticsJob), HangfireQueues.AnalyticsBatch)]
    [InlineData(typeof(RefreshChampionBuildSnapshotsJob), HangfireQueues.AnalyticsBatch)]
    [InlineData(typeof(RefreshChampionMatchupsJob), HangfireQueues.AnalyticsBatch)]
    [InlineData(typeof(RefreshProAnalyticsJob), HangfireQueues.AnalyticsWarm)]
    [InlineData(typeof(RefreshBuildResourceAnalyticsJob), HangfireQueues.AnalyticsBatch)]
    [InlineData(typeof(WarmDefaultChampionProfilesJob), HangfireQueues.AnalyticsWarm)]
    [InlineData(typeof(RefreshDatasetStatsJob), HangfireQueues.AnalyticsWarm)]
    [InlineData(typeof(RefreshBuildLabStatsJob), HangfireQueues.AnalyticsBatch)]
    [InlineData(typeof(RefreshChampionSynergyFactsJob), HangfireQueues.AnalyticsBatch)]
    [InlineData(typeof(RefreshLeaderboardsJob), HangfireQueues.AnalyticsWarm)]
    public void AnalyticsJob_RunsOnItsReservedLane(Type jobType, string expectedQueue)
    {
        var execute = jobType.GetMethod(nameof(WarmDefaultChampionProfilesJob.ExecuteAsync));
        execute.Should().NotBeNull($"{jobType.Name} must expose ExecuteAsync");

        var queue = execute!.GetCustomAttribute<QueueAttribute>();
        queue.Should().NotBeNull(
            $"{jobType.Name}.ExecuteAsync must carry [Queue(AnalyticsWarm)] so it runs on the dedicated, " +
            "rate-limit-free analytics pool and never queues behind Riot-throttled jobs");
        queue!.Queue.Should().Be(expectedQueue);
    }
}
