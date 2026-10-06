using Hangfire;
using Transcendence.Service.Core.Services.Analytics.Implementations;

namespace Transcendence.Service.Core.Services.Jobs;

[DisableConcurrentExecution(600)]
[AutomaticRetry(Attempts = 0)]
public sealed class RefreshChampionSynergyFactsJob(ChampionSynergyFactMaterializer materializer)
{
    [Queue(HangfireQueues.AnalyticsBatch)]
    public Task ExecuteAsync(CancellationToken ct) => materializer.RefreshAsync(ct);
}
