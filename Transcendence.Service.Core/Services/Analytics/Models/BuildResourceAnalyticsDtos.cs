namespace Transcendence.Service.Core.Services.Analytics.Models;

/// <summary>
/// Champion-and-role performance for players who selected a specific item or rune.
/// Rates are ratios in the 0..1 range. They describe correlation in the observed match corpus,
/// not the causal strength of the item or rune.
/// </summary>
public record BuildResourceChampionStatDto(
    int ChampionId,
    string Role,
    int Games,
    int Wins,
    double WinRate,
    double PickRate,
    double ShareOfResourceUses);

public record BuildResourceAnalyticsEntryDto(
    int ResourceId,
    string Name,
    string? Description,
    int Games,
    int Wins,
    double WinRate,
    double PickRate,
    IReadOnlyList<BuildResourceChampionStatDto> TopChampions);

public record BuildResourceAnalyticsIndexResponse(
    string ResourceType,
    string Patch,
    string Region,
    int TotalParticipantGames,
    IReadOnlyList<BuildResourceAnalyticsEntryDto> Entries);

public record BuildResourceAnalyticsDetailResponse(
    string ResourceType,
    string Patch,
    string Region,
    int TotalParticipantGames,
    BuildResourceAnalyticsEntryDto Resource,
    IReadOnlyList<BuildResourceChampionStatDto> ChampionStats);
