using Camille.Enums;

namespace Transcendence.Service.Core.Services.Tft.Interfaces;

public interface ITftSummonerRefreshJob
{
    Task RefreshByRiotId(
        string gameName,
        string tagLine,
        PlatformRoute platformRoute,
        string lockKey,
        string? priorityLockKey,
        CancellationToken ct = default);
}
