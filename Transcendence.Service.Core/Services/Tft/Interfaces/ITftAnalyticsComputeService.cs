using Transcendence.Service.Core.Services.Tft.Models;

namespace Transcendence.Service.Core.Services.Tft.Interfaces;

public interface ITftAnalyticsComputeService
{
    Task<IReadOnlyList<TftCompListItemDto>> ComputeCompListAsync(string? rankTier, string? region, CancellationToken ct = default);
    Task<TftCompDetailDto?> ComputeCompDetailAsync(string compSlug, string? rankTier, string? region, CancellationToken ct = default);
}
