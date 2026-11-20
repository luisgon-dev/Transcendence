using Microsoft.EntityFrameworkCore;
using Transcendence.Data;
using Transcendence.Service.Core.Services.Analytics.Interfaces;
using Transcendence.Service.Core.Services.Analytics.Models;

namespace Transcendence.Service.Core.Services.Analytics.Implementations;

public sealed class AnalyticsPublicationService(TranscendenceContext db) : IAnalyticsPublicationService
{
    private const int PatchOptionLimit = 12;

    public async Task<AnalyticsPublication> ResolveAsync(
        string? queueFamily, string? requestedPatch, CancellationToken ct, string? rankTier = null)
    {
        var queue = AnalyticsQueueCatalog.Normalize(queueFamily);
        var requested = string.IsNullOrWhiteSpace(requestedPatch) ? null : requestedPatch.Trim();
        var patches = ReadPatches(queue, RankTierCatalog.NormalizeScopeToken(rankTier) == RankTierCatalog.MasterPlusScope);
        var active = await FindActivePatchAsync(patches, ct);

        var selected = requested == null || requested == active?.Version
            ? active
            : await patches.FirstOrDefaultAsync(p => p.Version == requested, ct);
        if (selected?.ComputedAtUtc != null)
            return Ready(selected, active?.Version, requested);

        if (requested != null && requested != active?.Version)
            return new AnalyticsPublication(new(AnalyticsPatchSelectionState.Unavailable, active?.Version, requested, null));

        if (requested == null)
        {
            var previous = await EarlierPatches(patches, active)
                .Where(p => p.ComputedAtUtc != null)
                .OrderByDescending(p => p.ReleaseDate).ThenByDescending(p => p.Version)
                .FirstOrDefaultAsync(ct);
            if (previous != null)
                return Ready(previous, active?.Version, requested);
        }

        return new AnalyticsPublication(new(AnalyticsPatchSelectionState.Preparing, active?.Version, requested, null));
    }

    public async Task<IReadOnlyList<PublishedAnalyticsPatchOptionDto>> GetPatchOptionsAsync(
        string? queueFamily, CancellationToken ct, string? rankTier = null)
    {
        var queue = AnalyticsQueueCatalog.Normalize(queueFamily);
        var scope = RankTierCatalog.NormalizeScopeToken(rankTier);
        var patches = ReadPatches(queue, scope == RankTierCatalog.MasterPlusScope);
        var active = await FindActivePatchAsync(patches, ct);
        var counts = db.ScopeMatchCountStats.AsNoTracking().Where(x => x.QueueFamily == queue &&
            x.PlatformRegion == AnalyticsRegionCatalog.GlobalRegionCode && x.RankScope == scope);
        var options = await EarlierPatches(patches, active).Where(p => p.ComputedAtUtc != null)
            .OrderByDescending(p => p.ReleaseDate).ThenByDescending(p => p.Version)
            .Take(active == null ? PatchOptionLimit : PatchOptionLimit - 1)
            .Select(p => new PublishedAnalyticsPatchOptionDto(
                p.Version, p.ReleaseDate, p.DetectedAt, p.IsActive,
                counts.Where(x => x.Patch == p.Version).Select(x => x.TotalMatches).FirstOrDefault(),
                queue, AnalyticsPatchSelectionState.Ready))
            .ToListAsync(ct);

        if (active != null)
        {
            var ready = active.ComputedAtUtc.HasValue;
            var matchCount = ready
                ? await counts.Where(x => x.Patch == active.Version)
                    .Select(x => x.TotalMatches).FirstOrDefaultAsync(ct)
                : 0;
            options.Insert(0, new PublishedAnalyticsPatchOptionDto(
                active.Version, active.ReleaseDate, active.DetectedAt, true, matchCount, queue,
                ready ? AnalyticsPatchSelectionState.Ready : AnalyticsPatchSelectionState.Preparing));
        }

        return options;
    }

    private IQueryable<PatchPublication> ReadPatches(string queue, bool requireMasterPlus) =>
        db.Patches.AsNoTracking().Select(p => new PatchPublication
        {
            Version = p.Version,
            ReleaseDate = p.ReleaseDate,
            DetectedAt = p.DetectedAt,
            IsActive = p.IsActive,
            ComputedAtUtc = db.ChampionRoleTierStats.Any(x => x.Patch == p.Version && x.QueueFamily == queue)
                && (!requireMasterPlus || db.ScopeMatchCountStats.Any(x => x.Patch == p.Version &&
                    x.QueueFamily == queue && x.PlatformRegion == AnalyticsRegionCatalog.GlobalRegionCode &&
                    x.RankScope == RankTierCatalog.MasterPlusScope))
                ? db.ChampionScopeGradeStats
                    .Where(x => x.Patch == p.Version && x.QueueFamily == queue &&
                                x.PlatformRegion == AnalyticsRegionCatalog.GlobalRegionCode &&
                                x.RankScope == RankTierCatalog.AllScope && x.Role == AnalyticsQueueCatalog.AllRoles)
                    .Select(x => (DateTime?)x.ComputedAtUtc).FirstOrDefault()
                : null
        });

    private static Task<PatchPublication?> FindActivePatchAsync(IQueryable<PatchPublication> patches, CancellationToken ct) =>
        patches.Where(p => p.IsActive)
            .OrderByDescending(p => p.ReleaseDate).ThenByDescending(p => p.Version)
            .FirstOrDefaultAsync(ct);

    private static IQueryable<PatchPublication> EarlierPatches(IQueryable<PatchPublication> patches, PatchPublication? active) =>
        active == null ? patches : patches.Where(p => p.ReleaseDate < active.ReleaseDate ||
            (p.ReleaseDate == active.ReleaseDate && string.Compare(p.Version, active.Version) < 0));

    private static AnalyticsPublication Ready(PatchPublication patch, string? active, string? requested) => new(
        new(AnalyticsPatchSelectionState.Ready, active, requested, patch.Version), patch.ReleaseDate, patch.ComputedAtUtc);

    private sealed class PatchPublication
    {
        public required string Version { get; init; }
        public DateTime ReleaseDate { get; init; }
        public DateTime? DetectedAt { get; init; }
        public bool IsActive { get; init; }
        public DateTime? ComputedAtUtc { get; init; }
    }
}
