using Microsoft.EntityFrameworkCore;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Service.Core.Services.Analytics.Interfaces;
using Transcendence.Service.Core.Services.Analytics.Models;
using Transcendence.Service.Core.Services.RiotApi;

namespace Transcendence.Service.Core.Services.Analytics.Implementations;

public sealed class AnalyticsPatchQueryService(TranscendenceContext db) : IAnalyticsPatchQueryService
{
    public async Task<IReadOnlyList<AnalyticsPatchOptionDto>> GetPatchOptionsAsync(
        CancellationToken ct = default) =>
        await GetPatchOptionsAsync(QueueCatalog.QueueFamilyRankedSoloDuo, ct);

    public async Task<IReadOnlyList<AnalyticsPatchOptionDto>> GetPatchOptionsAsync(
        string? queueFamily,
        CancellationToken ct = default)
    {
        var normalizedQueue = AnalyticsQueueCatalog.Normalize(queueFamily);
        var queueMatches = db.Matches
            .AsNoTracking()
            .Where(m => m.Status == FetchStatus.Success)
            .Where(m => m.Patch != null && m.Patch != string.Empty);

        queueMatches = normalizedQueue switch
        {
            QueueCatalog.QueueFamilyAram => queueMatches.Where(m =>
                m.QueueFamily == QueueCatalog.QueueFamilyAram ||
                m.QueueId == 450 ||
                (m.QueueId == 0 && m.QueueType == "450")),
            QueueCatalog.QueueFamilyArena => queueMatches.Where(m =>
                m.QueueFamily == QueueCatalog.QueueFamilyArena ||
                m.QueueId == 1700 || m.QueueId == 1710 || m.QueueId == 1810 ||
                m.QueueId == 1820 || m.QueueId == 1830 || m.QueueId == 1840),
            QueueCatalog.QueueFamilyRankedFlex => queueMatches.Where(m =>
                m.QueueFamily == QueueCatalog.QueueFamilyRankedFlex ||
                m.QueueId == QueueCatalog.RankedFlexQueueId ||
                (m.QueueId == 0 && m.QueueType == QueueCatalog.RankedFlexQueueId.ToString())),
            _ => queueMatches.Where(m =>
                m.QueueFamily == QueueCatalog.QueueFamilyRankedSoloDuo ||
                m.QueueId == QueueCatalog.RankedSoloDuoQueueId ||
                (m.QueueId == 0 && m.QueueType == QueueCatalog.RankedSoloDuoQueueId.ToString()))
        };

        var matchCounts = await queueMatches
            .GroupBy(m => m.Patch!)
            .Select(g => new PatchMatchCount(g.Key, g.Count()))
            .ToListAsync(ct);

        var matchCountsByPatch = matchCounts.ToDictionary(x => x.Patch, x => x.Count);
        var patchesWithMatches = matchCountsByPatch.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var patchMetadata = await db.Patches
            .AsNoTracking()
            .Where(p => p.IsActive || patchesWithMatches.Contains(p.Version))
            .Select(p => new PatchMetadata(p.Version, p.ReleaseDate, p.DetectedAt, p.IsActive))
            .ToListAsync(ct);

        var optionsByPatch = patchMetadata
            .Select(p => new AnalyticsPatchOptionDto(
                p.Version,
                p.ReleaseDate,
                p.DetectedAt,
                p.IsActive,
                matchCountsByPatch.GetValueOrDefault(p.Version),
                normalizedQueue))
            .ToDictionary(x => x.Patch, StringComparer.OrdinalIgnoreCase);

        foreach (var matchCount in matchCounts)
        {
            optionsByPatch.TryAdd(matchCount.Patch, new AnalyticsPatchOptionDto(
                matchCount.Patch,
                null,
                null,
                false,
                matchCount.Count,
                normalizedQueue));
        }

        return optionsByPatch.Values
            .OrderByDescending(x => x.IsActive)
            .ThenByDescending(x => x.ReleasedAtUtc ?? x.DetectedAtUtc ?? DateTime.MinValue)
            .ThenByDescending(x => x.Patch)
            .ToList();
    }

    public async Task<AnalyticsPatchStatusDto> GetActivePatchStatusAsync(
        CancellationToken ct = default)
    {
        var activePatch = await db.Patches
            .AsNoTracking()
            .Where(p => p.IsActive)
            .Select(p => new AnalyticsPatchStatusDto(p.Version, p.ReleaseDate, p.DetectedAt))
            .FirstOrDefaultAsync(ct);

        return activePatch ?? new AnalyticsPatchStatusDto(null, null, null);
    }

    private sealed record PatchMatchCount(string Patch, int Count);

    private sealed record PatchMetadata(string Version, DateTime? ReleaseDate, DateTime? DetectedAt, bool IsActive);
}
