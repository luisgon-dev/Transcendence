using Camille.Enums;

namespace Transcendence.Service.Core.Services.Jobs;

public static class RefreshLockKeys
{
    public const string SummonerRefreshPrefix = "summoner-refresh:";
    public const string ApiPriorityRefreshPrefix = "refresh-priority:api:";

    public static string BuildCanonicalIdentity(PlatformRoute platform, string gameName, string tagLine)
    {
        return BuildCanonicalIdentity(platform.ToString(), gameName, tagLine);
    }

    public static string BuildCanonicalIdentity(string platformRegion, string gameName, string tagLine)
    {
        return
            $"{NormalizePlatform(platformRegion)}:{NormalizeRiotIdPart(gameName)}:{NormalizeRiotIdPart(tagLine)}";
    }

    public static string BuildSummonerRefreshKey(PlatformRoute platform, string gameName, string tagLine)
    {
        return $"{SummonerRefreshPrefix}{BuildCanonicalIdentity(platform, gameName, tagLine)}";
    }

    public static string BuildApiPriorityKey(PlatformRoute platform, string gameName, string tagLine)
    {
        return $"{ApiPriorityRefreshPrefix}{BuildCanonicalIdentity(platform, gameName, tagLine)}";
    }

    public static string NormalizePlatform(string platformRegion)
    {
        return platformRegion.Trim().ToUpperInvariant();
    }

    public static string NormalizeRiotIdPart(string value)
    {
        return value.Trim().ToUpperInvariant();
    }
}
