namespace Transcendence.Service.Core.Services.Analytics.Models;

public sealed class SavedBuildOptions
{
    public int MaximumPerUser { get; set; } = 200;
    public int DefaultPageSize { get; set; } = 50;
    public int MaximumPageSize { get; set; } = 200;
    /// <summary>Win-probability fraction: 0.005 is half a percentage point.</summary>
    public double MaterialLiftDelta { get; set; } = 0.005;
}

public sealed class SavedBuildLimitExceededException(int limit)
    : Exception($"A maximum of {limit} saved builds is allowed per account. Delete one before saving another.");

public record SaveBuildRequest(
    string Name,
    int ChampionId,
    string Role,
    int? OpponentChampionId,
    string? Patch,
    string? Region,
    string? RankingMode,
    IReadOnlyList<int>? ItemPath,
    IReadOnlyList<int>? RuneSelections,
    int? Spell1Id,
    int? Spell2Id);

public record SavedBuildDto(
    Guid Id,
    string Name,
    int ChampionId,
    string Role,
    int? OpponentChampionId,
    string Patch,
    string Region,
    string RankingMode,
    IReadOnlyList<int> ItemPath,
    IReadOnlyList<int> RuneSelections,
    int? Spell1Id,
    int? Spell2Id,
    Guid? SourceGenerationId,
    Guid? CurrentGenerationId,
    bool AnalyticsChanged,
    string CompatibilityStatus,
    IReadOnlyList<int> UnavailableItemIds,
    IReadOnlyList<SavedBuildUnavailableItemDto> UnavailableItems,
    Guid? ShareId,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

/// <summary>Reason is RETIRED (absent from the active patch) or REMOVED_FROM_STORE (present but unbuyable).</summary>
public record SavedBuildUnavailableItemDto(int ItemId, string Reason);

public record SavedBuildListDto(
    IReadOnlyList<SavedBuildDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    bool HasMore);

/// <summary>Action is DROP or REPLACE. REPLACE requires a ReplacementItemId that is valid on the active patch.</summary>
public record SavedBuildRepairChoice(int ItemId, string Action, int? ReplacementItemId);

public record SavedBuildRepairRequest(IReadOnlyList<SavedBuildRepairChoice>? Choices);

public record SavedBuildShareDto(Guid ShareId);

public record PublicSavedBuildDto(
    string Name,
    int ChampionId,
    string Role,
    int? OpponentChampionId,
    string Patch,
    string Region,
    string RankingMode,
    IReadOnlyList<int> ItemPath,
    IReadOnlyList<int> RuneSelections,
    int? Spell1Id,
    int? Spell2Id,
    Guid? SourceGenerationId,
    Guid? CurrentGenerationId,
    bool AnalyticsChanged,
    string CompatibilityStatus,
    IReadOnlyList<int> UnavailableItemIds,
    IReadOnlyList<SavedBuildUnavailableItemDto> UnavailableItems,
    DateTime UpdatedAtUtc);
