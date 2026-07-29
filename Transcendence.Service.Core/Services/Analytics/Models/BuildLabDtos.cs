namespace Transcendence.Service.Core.Services.Analytics.Models;

public record BuildLabProvenanceDto(
    Guid? GenerationId,
    string DatasetVersion,
    string ModelVersion,
    string StaticDataVersion,
    DateTime? SourceCutoffUtc,
    DateTime? GeneratedAtUtc,
    long MatchCount,
    string RankScope,
    IReadOnlyList<string> IncludedPatches,
    IReadOnlyList<string> IncludedRegions);

public record BuildLabContextDto(
    int ChampionId,
    string Role,
    int? OpponentChampionId,
    string RequestedPatch,
    string EffectivePatch,
    string RequestedRegion,
    string EffectiveRegion,
    string Section,
    string Mode);

public record AdjustedActionEstimateDto(
    string ActionKey,
    IReadOnlyList<int> ActionIds,
    double? AdjustedWpa,
    double? ConfidenceLow,
    double? ConfidenceHigh,
    double? RawWinRate,
    double? PickRate,
    long ObservedCount,
    double EffectiveSampleSize,
    double? AverageTimingMinutes,
    string EvidenceQuality,
    string FallbackScope,
    string RegionScope,
    string BaselineDefinition,
    bool IsPublishable,
    string? UnavailableReason);

public record BuildLabStageDto(
    string Family,
    int Stage,
    string Label,
    IReadOnlyList<AdjustedActionEstimateDto> Candidates);

public record BuildLabPathEstimateDto(
    IReadOnlyList<int> ItemPath,
    double? EstimatedWinProbability,
    double? AdjustedLift,
    double? ConfidenceLow,
    double? ConfidenceHigh,
    long ObservedCount,
    double EffectiveSampleSize,
    bool IsPublishable,
    string? UnavailableReason);

public record BuildLabResponse(
    bool Available,
    BuildLabContextDto Context,
    BuildLabProvenanceDto Provenance,
    IReadOnlyList<int> SelectedPath,
    BuildLabPathEstimateDto? PathEstimate,
    IReadOnlyList<BuildLabStageDto> Stages,
    string? UnavailableReason);

public record ChampionRecommendationSummary(
    bool Available,
    BuildLabProvenanceDto Provenance,
    AdjustedActionEstimateDto? FirstItem,
    AdjustedActionEstimateDto? Rune,
    AdjustedActionEstimateDto? SpellPair,
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
    IReadOnlyList<int> RuneSelections,
    IReadOnlyList<int> SpellPair);
