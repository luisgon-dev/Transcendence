using FluentAssertions;
using Hangfire;
using Hangfire.Common;
using Hangfire.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Transcendence.Service.Core.Services.Jobs;
using Transcendence.Service.Core.Services.Jobs.Configuration;
using Transcendence.Service.Workers;
using Transcendence.Service.Workers.Startup;

namespace Transcendence.Service.Core.Tests;

public class WorkerStartupIntegrityTests
{
    [Fact]
    public async Task EvaluateAsync_WhenMandatoryJobsVerify_ReturnsHealthyOutcomeAndUpdatesState()
    {
        var harness = new StartupIntegrityHarness();
        harness.Schedule.StartupIntegrityMaxAttempts = 2;
        harness.Schedule.StartupIntegrityRetryBackoffSeconds = 0;

        harness.Descriptors =
        [
            CreateDescriptor("mandatory-a", isMandatory: true, isEnabled: true),
            CreateDescriptor("optional-a", isMandatory: false, isEnabled: true)
        ];

        harness.HashEntries["recurring-job:mandatory-a"] = CreateRecurringHashPayload<UpdateStaticDataJob>(
            job => job.Execute(CancellationToken.None));

        var service = harness.CreateService();

        var result = await service.EvaluateAsync(CancellationToken.None);

        result.Status.Should().Be(WorkerStartupIntegrityStatus.Healthy);
        result.VerifiedMandatoryJobIds.Should().ContainSingle().Which.Should().Be("mandatory-a");
        result.MandatoryFailures.Should().BeEmpty();
        harness.State.Current.Status.Should().Be(WorkerStartupIntegrityStatus.Healthy);
    }

    [Fact]
    public async Task EvaluateAsync_WhenOptionalRegistrationFails_ReturnsDegradedOutcome()
    {
        var harness = new StartupIntegrityHarness();
        harness.Schedule.StartupIntegrityMaxAttempts = 2;
        harness.Schedule.StartupIntegrityRetryBackoffSeconds = 0;

        harness.Descriptors =
        [
            CreateDescriptor("mandatory-a", isMandatory: true, isEnabled: true),
            CreateDescriptor(
                "optional-b",
                isMandatory: false,
                isEnabled: true,
                configure: () => throw new InvalidOperationException("optional registration failed"))
        ];

        harness.HashEntries["recurring-job:mandatory-a"] = CreateRecurringHashPayload<UpdateStaticDataJob>(
            job => job.Execute(CancellationToken.None));

        var service = harness.CreateService();

        var result = await service.EvaluateAsync(CancellationToken.None);

        result.Status.Should().Be(WorkerStartupIntegrityStatus.Degraded);
        result.MandatoryFailures.Should().BeEmpty();
        result.OptionalFailures.Should().ContainSingle(failure => failure.Contains("optional-b"));
        harness.State.Current.Status.Should().Be(WorkerStartupIntegrityStatus.Degraded);
    }

    [Fact]
    public async Task EvaluateAsync_WhenMandatoryVerificationFailsAfterRetryBudget_ReturnsFailFast()
    {
        var harness = new StartupIntegrityHarness();
        harness.Schedule.StartupIntegrityMaxAttempts = 2;
        harness.Schedule.StartupIntegrityRetryBackoffSeconds = 0;

        harness.Descriptors =
        [
            CreateDescriptor("mandatory-a", isMandatory: true, isEnabled: true)
        ];

        var service = harness.CreateService();

        var result = await service.EvaluateAsync(CancellationToken.None);

        result.Status.Should().Be(WorkerStartupIntegrityStatus.FailFast);
        result.Attempt.Should().Be(2);
        result.MandatoryFailures.Should().Contain(failure =>
            failure.Contains("mandatory-a") &&
            failure.Contains("hash is missing"));
        harness.Connection.Verify(
            x => x.GetAllEntriesFromHash("recurring-job:mandatory-a"),
            Times.Exactly(2));
        harness.State.Current.Status.Should().Be(WorkerStartupIntegrityStatus.FailFast);
    }

    [Fact]
    public async Task DevelopmentWorker_StartAsync_WhenIntegrityFails_ThrowsInvalidOperationException()
    {
        var startupService = new Mock<IWorkerStartupIntegrityService>();
        startupService.Setup(x => x.EvaluateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkerStartupIntegrityResult(
                WorkerStartupIntegrityStatus.FailFast,
                ProfileName: "development",
                Attempt: 3,
                MaxAttempts: 3,
                VerifiedMandatoryJobIds: [],
                MandatoryFailures: ["mandatory-a: missing recurring-job hash"],
                OptionalFailures: []));

        var policy = new Mock<IWorkerRecurringJobPolicy>();
        policy.SetupGet(x => x.KnownJobIds).Returns([]);
        policy.Setup(x => x.ResolveProfile(It.IsAny<WorkerJobScheduleOptions>())).Returns("development");

        var worker = new DevelopmentWorker(
            Mock.Of<JobStorage>(),
            Options.Create(new WorkerJobScheduleOptions()),
            policy.Object,
            startupService.Object,
            Mock.Of<ILogger<DevelopmentWorker>>());

        Func<Task> act = async () => await worker.StartAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*failed mandatory verification*");
    }

    private static WorkerRecurringJobDescriptor CreateDescriptor(
        string jobId,
        bool isMandatory,
        bool isEnabled,
        Action? configure = null) =>
        new(
            JobId: jobId,
            CronExpression: "*/5 * * * *",
            CronSource: "test",
            IsEnabled: isEnabled,
            IsMandatoryBaseline: isMandatory,
            ConfigureRecurringJob: (_, _) => configure?.Invoke());

    private static Dictionary<string, string> CreateRecurringHashPayload<T>(
        System.Linq.Expressions.Expression<Action<T>> expression) where T : class
    {
        var invocationData = InvocationData.SerializeJob(Job.FromExpression(expression));
        return new Dictionary<string, string>
        {
            ["Cron"] = "*/5 * * * *",
            ["Job"] = invocationData.SerializePayload()
        };
    }

    private sealed class StartupIntegrityHarness
    {
        public StartupIntegrityHarness()
        {
            Policy.Setup(x => x.ResolveProfile(It.IsAny<WorkerJobScheduleOptions>())).Returns("default");
            Policy.Setup(x => x.BuildDescriptors(It.IsAny<WorkerJobScheduleOptions>()))
                .Returns(() => Descriptors);
            Storage.Setup(x => x.GetConnection()).Returns(Connection.Object);
            Connection.Setup(x => x.GetAllEntriesFromHash(It.IsAny<string>()))
                .Returns((string key) => HashEntries.TryGetValue(key, out var hash)
                    ? new Dictionary<string, string>(hash, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, string>());
        }

        public WorkerJobScheduleOptions Schedule { get; } = new();
        public WorkerStartupIntegrityState State { get; } = new();
        public Mock<IWorkerRecurringJobPolicy> Policy { get; } = new();
        public Mock<Hangfire.IRecurringJobManager> RecurringJobManager { get; } = new();
        public Mock<JobStorage> Storage { get; } = new();
        public Mock<IStorageConnection> Connection { get; } = new();
        public List<WorkerRecurringJobDescriptor> Descriptors { get; set; } = [];
        public Dictionary<string, Dictionary<string, string>> HashEntries { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public WorkerStartupIntegrityService CreateService() =>
            new(
                Mock.Of<ILogger<WorkerStartupIntegrityService>>(),
                Options.Create(Schedule),
                Policy.Object,
                RecurringJobManager.Object,
                Storage.Object,
                State);
    }
}
