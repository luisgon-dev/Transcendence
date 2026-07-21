namespace Transcendence.Service.Core.Services.Analytics.Models;

/// <summary>A durable, patch-scoped champion grade point used for trend charts.</summary>
public record ChampionTrendPointDto(
    string Patch,
    DateTime ReleasedAtUtc,
    TierGrade Tier,
    int Games,
    double WinRate,
    double PickRate,
    double BanRate,
    double StrengthScore,
    bool IsLowSample);

/// <summary>
/// Patch-over-patch champion performance at the persisted global analytics grain.
/// </summary>
public record ChampionTrendResponse(
    int ChampionId,
    string QueueFamily,
    string Role,
    string RankScope,
    string Region,
    List<ChampionTrendPointDto> Points);
