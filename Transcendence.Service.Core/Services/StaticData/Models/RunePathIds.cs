namespace Transcendence.Service.Core.Services.StaticData.Models;

/// <summary>
/// Shared path-id boundaries for Riot rune metadata. CommunityDragon exposes
/// stat shards through a synthetic path rather than a real rune style.
/// </summary>
public static class RunePathIds
{
    public const int StatMods = 5000;

    public static bool IsRealRunePath(int pathId) => pathId is > 0 and < StatMods;

    public static bool IsStatModPath(int pathId) => pathId >= StatMods;
}
