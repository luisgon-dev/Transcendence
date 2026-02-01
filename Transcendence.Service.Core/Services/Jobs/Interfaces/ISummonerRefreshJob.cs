using Camille.Enums;

namespace Transcendence.Service.Core.Services.Jobs.Interfaces;

public interface ISummonerRefreshJob
{
    Task RefreshByRiotId(string gameName, string tagLine, PlatformRoute platformRoute, string lockKey,
        CancellationToken ct = default);
}