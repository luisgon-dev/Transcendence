using FluentAssertions;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Transcendence.Service.Core.Services.Jobs;
using Transcendence.Service.Core.Services.Jobs.Configuration;
using Transcendence.Service.Workers;
using Transcendence.Service.Workers.Startup;

namespace Transcendence.Service.Core.Tests;

public class ProductionWorkerTests
{
    [Fact]
    public async Task StartAsync_WhenPatchRolloverIsNotDetected_QueuesBuildAtlasBootstrap()
    {
        var harness = new Harness();
        harness.StartupPatchRolloverService
            .Setup(x => x.PrepareAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StartupPatchRolloverResult(
                PatchRolloverDetected: false,
                PatchDetectionCompleted: false,
                PreviousPatch: "15.1",
                LatestPatch: "15.1",
                PurgedEnqueuedJobs: 0,
                PurgedScheduledJobs: 0));

        await harness.Worker.StartAsync(CancellationToken.None);
        await harness.Worker.StopAsync(CancellationToken.None);

        harness.BackgroundJobs.Verify(
            x => x.Create(
                It.Is<Job>(job =>
                    job.Type == typeof(RefreshBuildResourceAnalyticsJob) &&
                    job.Method.Name == nameof(RefreshBuildResourceAnalyticsJob.ExecuteAsync)),
                It.IsAny<IState>()),
            Times.Once);
    }

    [Fact]
    public async Task StartAsync_WhenPatchRolloverIsDetected_QueuesBuildAtlasAndPatchBootstraps()
    {
        var harness = new Harness();
        harness.StartupPatchRolloverService
            .Setup(x => x.PrepareAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StartupPatchRolloverResult(
                PatchRolloverDetected: true,
                PatchDetectionCompleted: true,
                PreviousPatch: "15.1",
                LatestPatch: "15.2",
                PurgedEnqueuedJobs: 4,
                PurgedScheduledJobs: 2));

        var createdJobs = new List<Job>();
        harness.BackgroundJobs
            .Setup(x => x.Create(It.IsAny<Job>(), It.IsAny<IState>()))
            .Callback<Job, IState>((job, _) => createdJobs.Add(job))
            .Returns("job-1");

        await harness.Worker.StartAsync(CancellationToken.None);
        await harness.Worker.StopAsync(CancellationToken.None);

        createdJobs.Should().HaveCount(2);
        createdJobs.Should().Contain(job =>
            job.Type == typeof(RefreshBuildResourceAnalyticsJob) &&
            job.Method.Name == nameof(RefreshBuildResourceAnalyticsJob.ExecuteAsync));
        createdJobs.Should().Contain(job =>
            job.Type == typeof(ChampionAnalyticsIngestionJob) &&
            job.Method.Name == nameof(ChampionAnalyticsIngestionJob.ExecuteAsync));
    }

    private sealed class Harness
    {
        public Harness()
        {
            Policy.SetupGet(x => x.KnownJobIds).Returns([
                WorkerRecurringJobPolicy.DetectPatchJobId,
                WorkerRecurringJobPolicy.RetryFailedMatchesJobId,
                WorkerRecurringJobPolicy.RefreshBuildResourceAnalyticsJobId,
                WorkerRecurringJobPolicy.ChampionAnalyticsIngestionJobId,
                WorkerRecurringJobPolicy.RefreshLockLifecycleCleanupJobId
            ]);
            Policy.Setup(x => x.ResolveProfile(It.IsAny<WorkerJobScheduleOptions>())).Returns("stable");
            Policy.Setup(x => x.BuildDescriptors(It.IsAny<WorkerJobScheduleOptions>()))
                .Returns([
                    Descriptor(WorkerRecurringJobPolicy.DetectPatchJobId),
                    Descriptor(WorkerRecurringJobPolicy.RetryFailedMatchesJobId),
                    Descriptor(WorkerRecurringJobPolicy.RefreshBuildResourceAnalyticsJobId),
                    Descriptor(WorkerRecurringJobPolicy.ChampionAnalyticsIngestionJobId),
                    Descriptor(WorkerRecurringJobPolicy.RefreshLockLifecycleCleanupJobId)
                ]);

            StartupIntegrityService
                .Setup(x => x.EvaluateAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new WorkerStartupIntegrityResult(
                    WorkerStartupIntegrityStatus.Healthy,
                    "stable",
                    1,
                    1,
                    [
                        WorkerRecurringJobPolicy.DetectPatchJobId,
                        WorkerRecurringJobPolicy.RetryFailedMatchesJobId,
                        WorkerRecurringJobPolicy.ChampionAnalyticsIngestionJobId,
                        WorkerRecurringJobPolicy.RefreshLockLifecycleCleanupJobId
                    ],
                    [],
                    []));

            Worker = new ProductionWorker(
                Mock.Of<ILogger<ProductionWorker>>(),
                BackgroundJobs.Object,
                JobStorage.Object,
                Options.Create(new WorkerJobScheduleOptions
                {
                    Profile = "stable",
                    DefaultProfile = "stable",
                    RunPatchDetectionOnStartup = true
                }),
                Policy.Object,
                StartupIntegrityService.Object,
                StartupPatchRolloverService.Object);
        }

        public Mock<IBackgroundJobClient> BackgroundJobs { get; } = new();
        public Mock<JobStorage> JobStorage { get; } = new();
        public Mock<IWorkerRecurringJobPolicy> Policy { get; } = new();
        public Mock<IWorkerStartupIntegrityService> StartupIntegrityService { get; } = new();
        public Mock<IStartupPatchRolloverService> StartupPatchRolloverService { get; } = new();
        public ProductionWorker Worker { get; }

        private static WorkerRecurringJobDescriptor Descriptor(string jobId) =>
            new(
                jobId,
                "0 */6 * * *",
                "test",
                IsEnabled: true,
                IsMandatoryBaseline: true,
                ConfigureRecurringJob: (_, _) => { });
    }
}
