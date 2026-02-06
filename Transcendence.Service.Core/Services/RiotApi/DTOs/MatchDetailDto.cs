namespace Transcendence.Service.Core.Services.RiotApi.DTOs;

/// <summary>
/// Full match details including all participants with items, runes, and spells.
/// </summary>
public record MatchDetailDto(
    string MatchId,
    long MatchDate,
    int Duration,
    string QueueType,
    string? Patch,
    IReadOnlyList<ParticipantDetailDto> Participants
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
