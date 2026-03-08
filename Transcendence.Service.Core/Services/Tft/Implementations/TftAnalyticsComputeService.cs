using Microsoft.EntityFrameworkCore;
using Transcendence.Data;
using Transcendence.Data.Models.Tft.Match;
using Transcendence.Service.Core.Services.Tft.Interfaces;
using Transcendence.Service.Core.Services.Tft.Models;

namespace Transcendence.Service.Core.Services.Tft.Implementations;

public class TftAnalyticsComputeService(TranscendenceContext context) : ITftAnalyticsComputeService
{
    public async Task<IReadOnlyList<TftCompListItemDto>> ComputeCompListAsync(string? rankTier, string? region, CancellationToken ct = default)
    {
        var activeSet = await context.TftSets.Where(x => x.IsActive).Select(x => (int?)x.Number).FirstOrDefaultAsync(ct);
        var participants = await BaseQuery(rankTier, region, activeSet, ct).ToListAsync(ct);
        if (participants.Count == 0)
            return [];

        var total = participants.Count;
        return participants
            .GroupBy(BuildCompKey, StringComparer.Ordinal)
            .Select(group =>
            {
                var exemplar = group.OrderBy(x => x.Placement).First();
                var units = exemplar.Units
                    .OrderByDescending(x => x.Tier)
                    .ThenByDescending(x => x.Rarity)
                    .Take(4)
                    .Select(MapUnit)
                    .ToList();
                var traits = exemplar.Traits
                    .OrderByDescending(x => x.Style ?? 0)
                    .ThenByDescending(x => x.NumUnits)
                    .Take(4)
                    .Select(MapTrait)
                    .ToList();

                return new TftCompListItemDto(
                    Slugify(group.Key),
                    BuildCompName(exemplar),
                    exemplar.Match.SetNumber,
                    exemplar.Match.SetCoreName,
                    exemplar.Match.Patch,
                    NormalizeRegion(region),
                    NormalizeRankTier(rankTier),
                    group.Average(x => x.Placement),
                    group.Count(x => x.Placement <= 4) / (double)group.Count(),
                    group.Count(x => x.Placement == 1) / (double)group.Count(),
                    group.Count() / (double)total,
                    group.Count(),
                    "stable",
                    units,
                    traits,
                    exemplar.Augments.Take(3).ToList());
            })
            .OrderBy(x => x.AvgPlacement)
            .ThenByDescending(x => x.Top4Rate)
            .ThenByDescending(x => x.WinRate)
            .ThenByDescending(x => x.SampleSize)
            .ToList();
    }

    public async Task<TftCompDetailDto?> ComputeCompDetailAsync(string compSlug, string? rankTier, string? region, CancellationToken ct = default)
    {
        var list = await ComputeCompListAsync(rankTier, region, ct);
        var summary = list.FirstOrDefault(x => x.CompSlug == compSlug);
        if (summary == null)
            return null;

        var activeSet = summary.SetNumber ?? 0;
        var items = await context.TftItemVersions
            .AsNoTracking()
            .Where(x => x.SetNumber == activeSet)
            .OrderBy(x => x.Name)
            .Take(10)
            .Select(x => new TftStaticEntityDto(x.ApiName, x.Name, x.Description, x.Icon))
            .ToListAsync(ct);

        var augments = await context.TftAugmentVersions
            .AsNoTracking()
            .Where(x => x.SetNumber == activeSet)
            .OrderBy(x => x.Name)
            .Take(10)
            .Select(x => new TftStaticEntityDto(x.ApiName, x.Name, x.Description, x.Icon))
            .ToListAsync(ct);

        return new TftCompDetailDto(summary, items, augments);
    }

    private IQueryable<TftMatchParticipant> BaseQuery(string? rankTier, string? region, int? activeSet, CancellationToken ct)
    {
        var query = context.TftMatchParticipants
            .AsNoTracking()
            .Include(x => x.Match)
            .Include(x => x.Units)
            .Include(x => x.Traits)
            .Include(x => x.Summoner)
            .ThenInclude(x => x.Ranks)
            .Where(x => x.Match.Status == TftFetchStatus.Success);

        if (activeSet.HasValue)
            query = query.Where(x => x.Match.SetNumber == activeSet.Value);

        var normalizedRegion = NormalizeRegion(region);
        if (normalizedRegion != "ALL")
            query = query.Where(x => x.Match.PlatformRegion == normalizedRegion);

        var normalizedRankTier = NormalizeRankTier(rankTier);
        if (normalizedRankTier != "all")
            query = query.Where(x => x.Summoner.Ranks.Any(r => MatchesRankTier(r.Tier, normalizedRankTier)));

        return query;
    }

    private static string BuildCompKey(TftMatchParticipant participant)
    {
        var traits = participant.Traits
            .Where(x => (x.Style ?? 0) > 0)
            .OrderByDescending(x => x.Style ?? 0)
            .ThenByDescending(x => x.NumUnits)
            .Take(2)
            .Select(x => NormalizeToken(x.Name));
        var units = participant.Units
            .OrderByDescending(x => x.Tier)
            .ThenByDescending(x => x.Rarity)
            .Take(2)
            .Select(x => NormalizeToken(x.Name ?? x.CharacterId));
        return string.Join("-", traits.Concat(units));
    }

    private static string BuildCompName(TftMatchParticipant participant)
    {
        var traits = participant.Traits
            .Where(x => (x.Style ?? 0) > 0)
            .OrderByDescending(x => x.Style ?? 0)
            .ThenByDescending(x => x.NumUnits)
            .Take(2)
            .Select(x => x.Name);
        var units = participant.Units
            .OrderByDescending(x => x.Tier)
            .ThenByDescending(x => x.Rarity)
            .Take(2)
            .Select(x => x.Name ?? x.CharacterId);
        return string.Join(" / ", traits.Concat(units));
    }

    private static TftUnitSummaryDto MapUnit(TftMatchParticipantUnit unit)
    {
        return new TftUnitSummaryDto(unit.CharacterId, unit.Name, unit.Rarity, unit.Tier, unit.Items);
    }

    private static TftTraitSummaryDto MapTrait(TftMatchParticipantTrait trait)
    {
        return new TftTraitSummaryDto(trait.Name, trait.NumUnits, trait.TierCurrent, trait.Style);
    }

    private static string Slugify(string value)
    {
        return value.Trim().ToLowerInvariant();
    }

    private static string NormalizeToken(string value)
    {
        return value.Trim().ToUpperInvariant().Replace(" ", string.Empty);
    }

    private static string NormalizeRegion(string? region)
    {
        return string.IsNullOrWhiteSpace(region) ? "ALL" : region.Trim().ToUpperInvariant();
    }

    private static string NormalizeRankTier(string? rankTier)
    {
        return string.IsNullOrWhiteSpace(rankTier) ? "EMERALD_PLUS" : rankTier.Trim().ToUpperInvariant();
    }

    private static bool MatchesRankTier(string tier, string requestedTier)
    {
        if (requestedTier == "all")
            return true;

        if (requestedTier == "EMERALD_PLUS")
        {
            return tier is "EMERALD" or "DIAMOND" or "MASTER" or "GRANDMASTER" or "CHALLENGER";
        }

        return string.Equals(tier, requestedTier, StringComparison.OrdinalIgnoreCase);
    }
}
