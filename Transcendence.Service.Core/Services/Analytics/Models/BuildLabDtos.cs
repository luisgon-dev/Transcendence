namespace Transcendence.Service.Core.Services.Analytics.Models;

/// <summary>What the numbers were computed from.</summary>
public record BuildLabCoverageDto(
    // Patches pooled into the answer, newest first.
    IReadOnlyList<string> IncludedPatches,
    // Weight each included patch carries, in the same order; older patches count for less.
    IReadOnlyList<double> PatchWeights,
    // Matches counted across the included patches.
    long CountedMatches,
    DateTime? LastCountedAtUtc,
    // Regions the counted matches come from.
    IReadOnlyList<string> IncludedRegions,
    string RankScope);

public record BuildLabContextDto(
    int ChampionId,
    string Role,
    int? OpponentChampionId,
    string? RequestedPatch,
    string RequestedRegion,
    string Section,
    string Mode);

public record BuildLabOptionDto(
    string ActionKey,
    IReadOnlyList<int> ActionIds,
    // Games in which this option was chosen at this decision (patch-weighted).
    double Games,
    // Share of the decision's games that chose this option.
    double PickRate,
    double WinRate,
    // Win rate after standardizing to the decision's own gold-difference mix, so an option usually
    // bought while ahead is not credited for the lead. Equals WinRate for pregame choices.
    double AdjustedWinRate,
    // AdjustedWinRate minus the decision's overall win rate.
    double Lift,
    double ConfidenceLow,
    double ConfidenceHigh,
    double? AverageTimingMinutes,
    // Too few games for the interval to mean much; shown, but never ranked first.
    bool IsLowSample);

public record BuildLabStageDto(
    string Family,
    int Stage,
    string Label,
    // Games that reached this decision (patch-weighted).
    double Games,
    double WinRate,
    // MATCHUP, REGION or ALL: the population the options were counted in.
    string Scope,
    // True when the requested matchup or region was too thin and ALL was used instead.
    bool IsFallback,
    IReadOnlyList<BuildLabOptionDto> Options);

public record BuildLabResponse(
    bool Available,
    BuildLabContextDto Context,
    BuildLabCoverageDto Coverage,
    IReadOnlyList<int> SelectedPath,
    IReadOnlyList<BuildLabStageDto> Stages,
    string? UnavailableReason);

public record ChampionRecommendationSummary(
    bool Available,
    BuildLabCoverageDto Coverage,
    BuildLabOptionDto? FirstItem,
    BuildLabOptionDto? RunePage,
    BuildLabOptionDto? SpellPair,
    string? UnavailableReason);

public record BuildLabQuery(
    int ChampionId,
    string Role,
    int? OpponentChampionId,
    string? Patch,
    string? Region,
    string Section,
    string Mode,
    IReadOnlyList<int> ItemPath,
    IReadOnlyList<int> RuneSelections);
