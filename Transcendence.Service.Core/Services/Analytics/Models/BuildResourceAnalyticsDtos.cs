using Transcendence.Data.Models.LoL.Analytics;

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
    IReadOnlyList<BuildResourceChampionStatDto> ChampionStats,
    // Items only, and null until Build Lab has counted purchases of it on this patch.
    ItemPurchaseTimingDto? Purchases = null);

/// <summary>
/// When an item is completed, from the match timelines Build Lab counts: as which legendary in the
/// build (or as the first boots), how often, how late, and how those games went.
/// </summary>
/// <param name="Kind"><c>ITEM</c> for a legendary, <c>BOOTS</c> for boots (a single first-pair stage).</param>
/// <param name="Region">The region counted; <c>ALL</c> when a region had too few games of its own.</param>
public record ItemPurchaseTimingDto(
    string Kind,
    string Patch,
    string Region,
    int Games,
    double? AverageMinutes,
    IReadOnlyList<ItemPurchaseStageDto> Stages);

/// <param name="Stage">1 for the first completed legendary, 2 for the second, and so on; 1 for boots.</param>
/// <param name="ShareOfStage">This item's share of every item completed at this stage.</param>
/// <param name="StageWinRate">The win rate of every item completed at this stage.</param>
/// <param name="AdjustedWinRate">
/// Standardized to the stage's gold-difference mix, so an item usually finished while ahead is
/// compared with other items finished while ahead (see <c>BuildLabEstimator</c>).
/// </param>
public record ItemPurchaseStageDto(
    int Stage,
    int Games,
    double ShareOfStage,
    double WinRate,
    double AdjustedWinRate,
    double StageWinRate,
    double ConfidenceLow,
    double ConfidenceHigh,
    double? AverageMinutes,
    bool IsLowSample);

/// <summary>One (family, stage, gold bucket) sum of Build Lab item counts.</summary>
public record ItemPurchaseCount(
    BuildLabFamily Family,
    short Stage,
    short GoldBucket,
    int Games,
    int Wins,
    long TimingSeconds);
