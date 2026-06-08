namespace Transcendence.Service.Core.Services.RiotApi.DTOs;

/// <summary>
/// Full match details including all participants with items, runes, and spells.
/// </summary>
public record MatchDetailDto(
    string MatchId,
    long MatchDate,
    int Duration,
    int QueueId,
    string QueueType,
    string? Patch,
    IReadOnlyList<ParticipantDetailDto> Participants,
    IReadOnlyList<TeamBansDto> Bans,
    IReadOnlyList<TeamObjectivesDto> Objectives
);

/// <summary>
/// Champions banned by one team, in pick-turn order.
/// </summary>
public record TeamBansDto(
    int TeamId,
    IReadOnlyList<int> BannedChampionIds
);

/// <summary>
/// Objective tallies for one team (kills + who got it first). Populated on new
/// ingestion only — absent/zero for matches ingested before Phase 3.
/// </summary>
public record TeamObjectivesDto(
    int TeamId,
    bool FirstBlood,
    ObjectiveStatDto Baron,
    ObjectiveStatDto Dragon,
    ObjectiveStatDto RiftHerald,
    ObjectiveStatDto Horde,
    ObjectiveStatDto Tower,
    ObjectiveStatDto Inhibitor
);

public record ObjectiveStatDto(int Kills, bool First);

/// <summary>
/// Per-minute team gold/xp totals for a match, for the gold/xp-diff curve.
/// Empty Frames when the match has no (or too few) timeline snapshots.
/// </summary>
public record MatchTimelineDto(
    string MatchId,
    int Duration,
    IReadOnlyList<TimelineFrameDto> Frames
);

public record TimelineFrameDto(
    int MinuteMark,
    int BlueGold,
    int RedGold,
    int BlueXp,
    int RedXp
);

/// <summary>
/// Complete participant data for a match including all stats, items, runes, and spells.
/// </summary>
public record ParticipantDetailDto(
    string? Puuid,
    string? GameName,
    string? TagLine,
    int TeamId,
    int ChampionId,
    string? TeamPosition,
    bool Win,
    int Kills,
    int Deaths,
    int Assists,
    int ChampLevel,
    int GoldEarned,
    int TotalDamageDealtToChampions,
    int PhysicalDamageDealtToChampions,
    int MagicDamageDealtToChampions,
    int TrueDamageDealtToChampions,
    int VisionScore,
    int TotalMinionsKilled,
    int NeutralMinionsKilled,
    int SummonerSpell1Id,
    int SummonerSpell2Id,
    IReadOnlyList<int> Items,
    ParticipantRunesDto Runes
);

/// <summary>
/// Rune selections for a participant.
/// </summary>
/// <remarks>
/// Structure mirrors Riot's rune system:
/// - Primary tree: 4 rune selections (keystone + 3 lesser runes)
/// - Secondary tree: 2 rune selections
/// - Stat shards: 3 selections (offense, flex, defense)
/// </remarks>
public record ParticipantRunesDto(
    int PrimaryStyleId,
    int SubStyleId,
    IReadOnlyList<int> PrimarySelections,
    IReadOnlyList<int> SubSelections,
    IReadOnlyList<int> StatShards
);
