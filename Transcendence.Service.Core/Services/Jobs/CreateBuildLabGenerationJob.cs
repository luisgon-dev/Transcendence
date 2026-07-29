using Hangfire;
using Transcendence.Service.Core.Services.Analytics.Interfaces;
using Transcendence.Service.Core.Services.Diagnostics;

namespace Transcendence.Service.Core.Services.Jobs;

[AutomaticRetry(Attempts = 0)]
[DisableConcurrentExecution(timeoutInSeconds: 30 * 60)]
public sealed class CreateBuildLabGenerationJob(
    IBuildLabGenerationCoordinator coordinator,
    BuildLabTelemetry telemetry,
    ILogger<CreateBuildLabGenerationJob> logger)
{
    [Queue(HangfireQueues.AnalyticsWarm)]
    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        Guid? generationId;
        try
        {
            generationId = await coordinator.CreatePendingGenerationAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Recorded here because the coordinator cannot observe its own throw, and this job runs
            // daily with no retry: a failed tick means no generation exists for a whole day.
            telemetry.RecordGenerationCreationFailed();
            throw;
        }

        if (generationId.HasValue)
        {
            logger.LogInformation("Created pending Build Lab dataset generation {GenerationId}.", generationId);
        }
        else
        {
            // A skipped tick is recorded so the dashboard can tell "nothing to do" from "job dead":
            // the create event on the success path is emitted by the coordinator.
            telemetry.RecordGenerationSkipped();
            logger.LogDebug("Build Lab dataset generation was not created on this tick.");
        }
    }
}
