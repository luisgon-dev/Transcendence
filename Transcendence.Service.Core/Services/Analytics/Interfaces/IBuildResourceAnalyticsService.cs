using Transcendence.Service.Core.Services.Analytics.Models;

namespace Transcendence.Service.Core.Services.Analytics.Interfaces;

public interface IBuildResourceAnalyticsService
{
    Task<BuildResourceAnalyticsIndexResponse> GetItemsAsync(
        string? region,
        string? patch,
        CancellationToken ct = default);

    Task<BuildResourceAnalyticsDetailResponse?> GetItemAsync(
        int itemId,
        string? region,
        string? patch,
        CancellationToken ct = default);

    Task<BuildResourceAnalyticsIndexResponse> GetRunesAsync(
        string? region,
        string? patch,
        CancellationToken ct = default);

    Task<BuildResourceAnalyticsDetailResponse?> GetRuneAsync(
        int runeId,
        string? region,
        string? patch,
        CancellationToken ct = default);
}
