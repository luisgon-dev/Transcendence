using Transcendence.Service.Core.Services.Analytics.Models;

namespace Transcendence.Service.Core.Services.Analytics.Interfaces;

public interface IBuildLabService
{
    Task<BuildLabResponse> GetAsync(BuildLabQuery query, CancellationToken ct = default);

    Task<ChampionRecommendationSummary> GetChampionRecommendationAsync(
        int championId,
        string role,
        int? opponentChampionId,
        string? patch,
        string? region,
        CancellationToken ct = default);
}
