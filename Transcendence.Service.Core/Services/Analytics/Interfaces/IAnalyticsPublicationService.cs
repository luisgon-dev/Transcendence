using Transcendence.Service.Core.Services.Analytics.Models;

namespace Transcendence.Service.Core.Services.Analytics.Interfaces;

public interface IAnalyticsPublicationService
{
    Task<AnalyticsPublication> ResolveAsync(string? queueFamily, string? requestedPatch, CancellationToken ct, string? rankTier = null);

    Task<IReadOnlyList<PublishedAnalyticsPatchOptionDto>> GetPatchOptionsAsync(
        string? queueFamily, CancellationToken ct, string? rankTier = null);
}
