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
    double WinRate,               // Win rate for this build (0.0 to 1.0)
    double PickRate = 0           // Share of all scoped games on this exact build (0.0 to 1.0)
);

/// <summary>
/// A summoner spell pair (normalized so the lower id is first) with usage stats.
/// </summary>
public record SummonerSpellPairDto(
    int Spell1Id,
    int Spell2Id,
    int Games,
    double WinRate,
    double PickRate = 0
);

/// <summary>
/// A starting-item set (sorted item ids) with usage stats.
/// </summary>
public record StarterItemSetDto(
    List<int> Items,
    int Games,
    double WinRate,
    double PickRate = 0
);

/// <summary>
/// A single item option (used for boots and situational slots) with usage stats.
/// </summary>
public record ItemChoiceDto(
    int ItemId,
    int Games,
    double WinRate,
    double PickRate = 0
);

/// <summary>
/// One step of the ordered core build path, with the average minute the item was completed.
/// </summary>
public record CoreItemStepDto(
    int ItemId,
    int Games,
    double WinRate,
    double AvgCompletionMinute,
    double PickRate = 0
);

/// <summary>
/// A late/situational item slot (4th, 5th, 6th) with the top alternative options.
/// </summary>
public record SituationalSlotDto(
    int Slot,
    List<ItemChoiceDto> Options
);

/// <summary>
/// Response containing top builds for a champion plus the sectioned, timing-aware build path
/// (summoner spells, skill order, starters, boots, ordered core with completion timing, and
/// 4th/5th/6th situational options). The sectioned fields are optional and degrade to null when
/// timeline-derived data is unavailable.
/// </summary>
public record ChampionBuildsResponse(
    int ChampionId,
    string Role,
    string RankTier,
    string Region,
    string Patch,
    List<int> GlobalCoreItems,    // Items core across ALL builds for this champion
    List<ChampionBuildDto> Builds, // Top 3 builds ordered by (games * winRate)
    AnalyticsSampleMetadata? Sample = null,
    List<SummonerSpellPairDto>? SummonerSpells = null,
    SkillOrderDto? SkillOrder = null,
    List<StarterItemSetDto>? StartingItems = null,
    List<ItemChoiceDto>? Boots = null,
    List<CoreItemStepDto>? CoreBuildPath = null,
    List<SituationalSlotDto>? SituationalSlots = null,
    string QueueFamily = "RANKED_SOLO_DUO"
);

public record ProMatchBuildDto(
    string MatchId,
    string? PlayerName,
    string? TeamName,
    bool Win,
    long PlayedAt,
    List<int> Items,              // Build-relevant items in purchase order (final inventory as fallback)
    int PrimaryStyleId,
    int SubStyleId,
    List<int> PrimaryRunes,
    List<int> SubRunes,
    List<int> StatShards,
    int Spell1Id = 0,
    int Spell2Id = 0,
    SkillOrderDto? SkillOrder = null
);

public record ProPlayerSummaryDto(
    string? PlayerName,
    string? TeamName,
    int Games,
    double WinRate
);

public record CommonProBuildDto(
    List<int> Items,
    int Games,
    double WinRate
);

public record ChampionProBuildsResponse(
    int ChampionId,
    string Patch,
    string Role,
    string Region,
    string Scope,
    List<ProMatchBuildDto> RecentProMatches,
    List<ProPlayerSummaryDto> TopPlayers,
    List<CommonProBuildDto> CommonBuilds,
    AnalyticsSampleMetadata? Sample = null
);

/// <summary>
/// Skill order derived from match-timeline SKILL_LEVEL_UP events: the most common opening
/// three points and basic-ability max priority, with the usage stats of the dominant priority.
/// </summary>
public record SkillOrderDto(
    string FirstThree,            // e.g., "QWE" or "QEW"
    string MaxOrder,              // e.g., "Q>E>W"
    int Games = 0,
    double WinRate = 0,
    double PickRate = 0
);
