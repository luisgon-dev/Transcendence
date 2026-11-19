using Hangfire;
using Transcendence.Service.Core.Services.Analytics.Implementations;
using Transcendence.Service.Core.Services.Operations;

namespace Transcendence.Service.Core.Services.Jobs;

[DisableConcurrentExecution(timeoutInSeconds: 1)]
[AutomaticRetry(Attempts = 0)]
[StorageMaintenanceWork]
public sealed class RefreshRequestedChampionSectionsJob(ChampionScopedSectionService sections)
{
    [Queue(HangfireQueues.AnalyticsWarm)]
    public Task ExecuteAsync(CancellationToken ct) => sections.DrainAsync(ct);
}
