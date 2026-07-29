using Hangfire;
using Transcendence.Service.Core.Services.Analytics.Interfaces;
using Transcendence.Service.Core.Services.Diagnostics;

namespace Transcendence.Service.Core.Services.Jobs;

[AutomaticRetry(Attempts = 0)]
[DisableConcurrentExecution(timeoutInSeconds: 10 * 60)]
public sealed class PromoteBuildLabGenerationJob(
    IBuildLabGenerationCoordinator coordinator,
    BuildLabTelemetry telemetry,
    ILogger<PromoteBuildLabGenerationJob> logger)
{
    [Queue(HangfireQueues.AnalyticsWarm)]
    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        int promoted;
        try
        {
            promoted = await coordinator.PromoteReadyCandidatesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The coordinator already isolates per-candidate faults, so a throw here means the lease
            // reaper or the candidate query failed and the whole tick produced nothing.
            telemetry.RecordPromotionFailed();
            throw;
        }

        if (promoted > 0)
            logger.LogInformation("Promoted {GenerationCount} Build Lab generation(s).", promoted);
    }
}
