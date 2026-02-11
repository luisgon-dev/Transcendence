using Camille.Enums;

namespace Transcendence.Service.Core.Services.Jobs;

public static class RefreshLockKeys
{
    public const string SummonerRefreshPrefix = "summoner-refresh:";
    public const string ApiPriorityRefreshPrefix = "refresh-priority:api:";

    public static string BuildSummonerRefreshKey(PlatformRoute platform, string gameName, string tagLine)
    {
        return
            $"{SummonerRefreshPrefix}{platform}:{gameName.Trim().ToUpperInvariant()}:{tagLine.Trim().ToUpperInvariant()}";
    }

    public static string BuildApiPriorityKey(PlatformRoute platform, string gameName, string tagLine)
    {
        return
            $"{ApiPriorityRefreshPrefix}{platform}:{gameName.Trim().ToUpperInvariant()}:{tagLine.Trim().ToUpperInvariant()}";
    }
}
