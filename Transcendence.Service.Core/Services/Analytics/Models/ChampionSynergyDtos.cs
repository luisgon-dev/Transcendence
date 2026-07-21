namespace Transcendence.Service.Core.Services.Analytics.Models;

public record ChampionSynergyEntryDto(
    int PartnerChampionId,
    string PartnerRole,
    int Games,
    int Wins,
    double WinRate,
    double PickRate,
    double WinRateDelta,
    double ConfidenceScore);

public record ChampionSynergiesResponse(
    int ChampionId,
    string Role,
    string RankTier,
    string Region,
    string Patch,
    string QueueFamily,
    int TotalGames,
    int TotalWins,
    double BaselineWinRate,
    IReadOnlyList<ChampionSynergyEntryDto> BestPartners);
