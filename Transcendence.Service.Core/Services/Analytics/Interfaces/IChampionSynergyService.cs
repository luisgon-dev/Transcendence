using Transcendence.Service.Core.Services.Analytics.Models;

namespace Transcendence.Service.Core.Services.Analytics.Interfaces;

public interface IChampionSynergyService
{
    Task RefreshSnapshotAsync(int championId, string role, string rankTier, string patch, CancellationToken ct = default);
    Task<ChampionSynergiesResponse> GetSynergiesAsync(
        int championId,
        string role,
        string? rankTier,
        string? region,
        string? queueFamily,
        string? patch,
        CancellationToken ct = default);
}
