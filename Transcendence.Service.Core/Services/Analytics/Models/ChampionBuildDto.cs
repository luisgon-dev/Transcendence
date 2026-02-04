namespace Transcendence.Service.Core.Services.Analytics.Models;

/// <summary>
/// Individual build recommendation with item and rune configuration.
/// </summary>
public record ChampionBuildDto(
    List<int> Items,               // Full item build (6 items + trinket)
    List<int> CoreItems,           // Core items with 70%+ appearance rate
    List<int> SituationalItems,    // Items with <70% appearance rate
    RuneTreeDto Runes,             // Rune configuration
    int Games,                      // Number of games with this build
    int Wins,                       // Number of wins
    double WinRate                  // 0.0 to 1.0
);

/// <summary>
/// Rune tree configuration for a build.
/// </summary>
public record RuneTreeDto(
    int KeystoneId,                 // Primary keystone rune
    int PrimaryStyleId,             // Primary rune path ID
    string PrimaryStyleName,        // Primary path name (e.g., "Domination")
    List<int> PrimaryRunes,         // All runes from primary tree
    int SecondaryStyleId,           // Secondary rune path ID
    string SecondaryStyleName,      // Secondary path name
    List<int> SecondaryRunes        // All runes from secondary tree
);

/// <summary>
/// Skill order placeholder for future Timeline API implementation.
/// </summary>
public record SkillOrderDto(
    string Order,                   // e.g., "Q>W>E" or "Q>E>W"
    double Frequency                // 0.0 to 1.0
);

/// <summary>
/// Top 3 build recommendations for a champion in a specific role/rank/patch.
/// </summary>
public record ChampionBuildsResponse(
    int ChampionId,
    string Role,
    string RankTier,
    string Patch,
    List<int> GlobalCoreItems,      // Core items across ALL builds (70%+ global appearance)
    List<ChampionBuildDto> Builds,  // Top 3 builds ordered by (games * winRate)
    SkillOrderDto? SkillOrder       // Placeholder - requires Timeline API
);
