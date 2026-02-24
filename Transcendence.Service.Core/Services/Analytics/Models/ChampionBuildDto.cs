namespace Transcendence.Service.Core.Services.Analytics.Models;

/// <summary>
/// A specific item + rune build with performance stats.
/// </summary>
public record ChampionBuildDto(
    List<int> Items,              // Completed build-impact item IDs only
    List<int> CoreItems,          // Items that appear in 70%+ of all games (not just this build)
    List<int> SituationalItems,   // Items in this build that aren't core
    int PrimaryStyleId,           // Primary rune tree (e.g., Precision)
    int SubStyleId,               // Secondary rune tree (e.g., Domination)
    List<int> PrimaryRunes,       // 4 runes from primary tree
    List<int> SubRunes,           // 2 runes from sub tree
    List<int> StatShards,         // 3 stat shards
    int Games,                    // Number of games with this exact build
    double WinRate                // Win rate for this build (0.0 to 1.0)
);

/// <summary>
/// Response containing top builds for a champion.
/// </summary>
public record ChampionBuildsResponse(
    int ChampionId,
    string Role,
    string RankTier,
    string Patch,
    List<int> GlobalCoreItems,    // Items core across ALL builds for this champion
    List<ChampionBuildDto> Builds // Top 3 builds ordered by (games * winRate)
);

/// <summary>
/// Skill order for ability maxing (requires Timeline API - placeholder for future).
/// NOTE: Phase 3 does not fetch Timeline data. Skill order will be null until
/// Timeline API integration is added.
/// </summary>
public record SkillOrderDto(
    string FirstThree,            // e.g., "QWE" or "QEW"
    string MaxOrder               // e.g., "Q>E>W"
);
