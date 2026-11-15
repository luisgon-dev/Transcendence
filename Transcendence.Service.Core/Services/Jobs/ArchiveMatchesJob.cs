using Hangfire;
using Transcendence.Service.Core.Services.Operations;

namespace Transcendence.Service.Core.Services.Jobs;

[DisableConcurrentExecution(1)]
[AutomaticRetry(Attempts = 0)]
public sealed class ArchiveMatchesJob(MatchArchiveRetention retention, MatchArchiveTelemetry telemetry)
{
    [Queue(HangfireQueues.Maintenance)]
    public async Task ExecuteAsync(CancellationToken ct)
    {
        try { await retention.RunAsync(ct); }
        catch (Exception) when (!ct.IsCancellationRequested) { telemetry.Failed(); throw; }
    }
}
