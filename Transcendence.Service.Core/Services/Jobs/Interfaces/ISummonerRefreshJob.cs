using Camille.Enums;
using Hangfire;

namespace Transcendence.Service.Core.Services.Jobs.Interfaces;

public interface ISummonerRefreshJob
{
    // Jobs are enqueued via this interface (Enqueue<ISummonerRefreshJob>(...)), so Hangfire resolves
    // the [Queue] from the INTERFACE method — attributes on the implementation are ignored here.
    [Queue("refresh-high")]
    Task RefreshByRiotId(string gameName, string tagLine, PlatformRoute platformRoute, string lockKey,
        string? priorityLockKey, Guid? requestedByUserAccountId = null, CancellationToken ct = default);

    [Queue(HangfireQueues.Discovery)]
    Task RefreshForAnalytics(string gameName, string tagLine, PlatformRoute platformRoute, string lockKey,
        long startTimeEpochSeconds, string currentPatch, bool includeAllModes, CancellationToken ct = default);
}
