using Camille.Enums;

namespace Transcendence.Service.Core.Services.Jobs.Interfaces;

public interface ISummonerRefreshJob
{
    Task RefreshByRiotId(string gameName, string tagLine, PlatformRoute platformRoute, string lockKey,
        string? priorityLockKey, CancellationToken ct = default);

    Task RefreshForAnalytics(string gameName, string tagLine, PlatformRoute platformRoute, string lockKey,
        long startTimeEpochSeconds, string currentPatch, CancellationToken ct = default);
}
