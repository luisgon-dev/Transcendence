namespace Transcendence.Service.Core.Services.Analytics.Models;

/// <summary>
/// Win rate data for a specific champion, role, and rank tier combination.
/// </summary>
public record ChampionWinRateDto(
    int ChampionId,
    string Role,
    string RankTier,
    int Games,
    int Wins,
    double WinRate,     // 0.0 to 1.0
    double PickRate,    // 0.0 to 1.0
    double BanRate,     // 0.0 to 1.0
    int? RoleRank,
    int? RolePopulation,
    string Patch
);

/// <summary>
/// Summary of champion win rates across all roles and tiers.
/// </summary>
public record ChampionWinRateSummary(
    int ChampionId,
    string Patch,
    List<ChampionWinRateDto> ByRoleTier
);
