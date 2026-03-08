using Camille.Enums;

namespace Transcendence.Service.Core.Services.Jobs;

public static class TftRefreshLockKeys
{
    public const string SummonerRefreshPrefix = "tft:summoner-refresh:";
    public const string ApiPriorityRefreshPrefix = "tft:refresh-priority:api:";

    public static string BuildCanonicalIdentity(PlatformRoute platform, string gameName, string tagLine)
    {
        return $"{platform.ToString().Trim().ToUpperInvariant()}:{NormalizeRiotIdPart(gameName)}:{NormalizeRiotIdPart(tagLine)}";
    }

    public static string BuildSummonerRefreshKey(PlatformRoute platform, string gameName, string tagLine)
    {
        return $"{SummonerRefreshPrefix}{BuildCanonicalIdentity(platform, gameName, tagLine)}";
    }

    public static string BuildApiPriorityKey(PlatformRoute platform, string gameName, string tagLine)
    {
        return $"{ApiPriorityRefreshPrefix}{BuildCanonicalIdentity(platform, gameName, tagLine)}";
    }

    private static string NormalizeRiotIdPart(string value)
    {
        return value.Trim().ToUpperInvariant();
    }
}
