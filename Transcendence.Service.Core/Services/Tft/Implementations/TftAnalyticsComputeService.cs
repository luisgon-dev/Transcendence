using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Transcendence.Data;
using Transcendence.Data.Models.Tft.Match;
using Transcendence.Service.Core.Services.Tft.Configuration;
using Transcendence.Service.Core.Services.Tft.Interfaces;
using Transcendence.Service.Core.Services.Tft.Models;

namespace Transcendence.Service.Core.Services.Tft.Implementations;

public class TftAnalyticsComputeService(
    TranscendenceContext context,
    IOptions<TftAnalyticsComputeOptions>? optionsAccessor = null) : ITftAnalyticsComputeService
{
    private readonly TftAnalyticsComputeOptions options = optionsAccessor?.Value ?? new TftAnalyticsComputeOptions();

    public async Task<IReadOnlyList<TftCompListItemDto>> ComputeCompListAsync(string? rankTier, string? region, CancellationToken ct = default)
    {
        var activeSet = await context.TftSets.Where(x => x.IsActive).Select(x => (int?)x.Number).FirstOrDefaultAsync(ct);
        var normalizedRegion = NormalizeRegion(region);
        var normalizedRankTier = NormalizeRankTier(rankTier);
        // Keep comp aggregation bounded to a recent match slice so request-time grouping does not scan the full TFT corpus.
        var matchIds = await BuildMatchIdQuery(normalizedRegion, activeSet)
            .Take(options.MaxRecentMatchesPerSlice)
            .ToListAsync(ct);
        if (matchIds.Count == 0)
            return [];

        var participants = await BuildParticipantQuery(matchIds, normalizedRankTier)
            .ToListAsync(ct);
        if (participants.Count == 0)
            return [];

        var participantIds = participants.Select(x => x.Id).ToArray();
        var unitsByParticipant = await context.TftMatchParticipantUnits
            .AsNoTracking()
            .Where(x => participantIds.Contains(x.ParticipantId))
            .Select(x => new UnitRow(
                x.ParticipantId,
                x.CharacterId,
                x.Name,
                x.Rarity,
                x.Tier,
                x.Items))
            .ToListAsync(ct);
        var traitsByParticipant = await context.TftMatchParticipantTraits
            .AsNoTracking()
            .Where(x => participantIds.Contains(x.ParticipantId))
            .Select(x => new TraitRow(
                x.ParticipantId,
                x.Name,
                x.NumUnits,
                x.TierCurrent,
                x.Style))
            .ToListAsync(ct);

        var unitLookup = unitsByParticipant
            .GroupBy(x => x.ParticipantId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<UnitRow>)group.ToList());
        var traitLookup = traitsByParticipant
            .GroupBy(x => x.ParticipantId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<TraitRow>)group.ToList());

        var total = participants.Count;
        return participants
            .GroupBy(
                participant => BuildCompKey(
                    unitLookup.GetValueOrDefault(participant.Id, []),
                    traitLookup.GetValueOrDefault(participant.Id, [])),
                StringComparer.Ordinal)
            .Select(group =>
            {
                var exemplar = group.OrderBy(x => x.Placement).First();
                var exemplarUnits = unitLookup.GetValueOrDefault(exemplar.Id, []);
                var exemplarTraits = traitLookup.GetValueOrDefault(exemplar.Id, []);
                var units = exemplarUnits
                    .OrderByDescending(x => x.Tier)
                    .ThenByDescending(x => x.Rarity)
                    .Take(4)
                    .Select(MapUnit)
                    .ToList();
                var traits = exemplarTraits
                    .OrderByDescending(x => x.Style ?? 0)
                    .ThenByDescending(x => x.NumUnits)
                    .Take(4)
                    .Select(MapTrait)
                    .ToList();

                return new TftCompListItemDto(
                    Slugify(group.Key),
                    BuildCompName(exemplarUnits, exemplarTraits),
                    exemplar.SetNumber,
                    exemplar.SetCoreName,
                    exemplar.Patch,
                    normalizedRegion,
                    ToRankTierDisplay(normalizedRankTier),
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

    private IQueryable<Guid> BuildMatchIdQuery(string normalizedRegion, int? activeSet)
    {
        var query = context.TftMatches
            .AsNoTracking()
            .Where(x => x.Status == TftFetchStatus.Success);

        if (activeSet.HasValue)
            query = query.Where(x => x.SetNumber == activeSet.Value);

        if (normalizedRegion != "ALL")
            query = query.Where(x => x.PlatformRegion == normalizedRegion);

        return query
            .OrderByDescending(x => x.MatchDate)
            .Select(x => x.Id);
    }

    private IQueryable<ParticipantRow> BuildParticipantQuery(IReadOnlyCollection<Guid> matchIds, string normalizedRankTier)
    {
        var query = context.TftMatchParticipants
            .AsNoTracking()
            .Where(x => matchIds.Contains(x.MatchId));

        if (normalizedRankTier != "all")
        {
            if (normalizedRankTier == "EMERALD_PLUS")
            {
                query = query.Where(x => x.Summoner.Ranks.Any(r =>
                    r.Tier == "EMERALD"
                    || r.Tier == "DIAMOND"
                    || r.Tier == "MASTER"
                    || r.Tier == "GRANDMASTER"
                    || r.Tier == "CHALLENGER"));
            }
            else
            {
                query = query.Where(x => x.Summoner.Ranks.Any(r => r.Tier == normalizedRankTier));
            }
        }

        return query.Select(x => new ParticipantRow(
            x.Id,
            x.MatchId,
            x.Placement,
            x.Augments,
            x.Match.SetNumber,
            x.Match.SetCoreName,
            x.Match.Patch,
            x.Match.PlatformRegion));
    }

    private static string BuildCompKey(IReadOnlyList<UnitRow> units, IReadOnlyList<TraitRow> traits)
    {
        var compTraits = traits
            .Where(x => (x.Style ?? 0) > 0)
            .OrderByDescending(x => x.Style ?? 0)
            .ThenByDescending(x => x.NumUnits)
            .Take(2)
            .Select(x => NormalizeToken(x.Name));
        var compUnits = units
            .OrderByDescending(x => x.Tier)
            .ThenByDescending(x => x.Rarity)
            .Take(2)
            .Select(x => NormalizeToken(x.Name ?? x.CharacterId));
        return string.Join("-", compTraits.Concat(compUnits));
    }

    private static string BuildCompName(IReadOnlyList<UnitRow> units, IReadOnlyList<TraitRow> traits)
    {
        var compTraits = traits
            .Where(x => (x.Style ?? 0) > 0)
            .OrderByDescending(x => x.Style ?? 0)
            .ThenByDescending(x => x.NumUnits)
            .Take(2)
            .Select(x => x.Name);
        var compUnits = units
            .OrderByDescending(x => x.Tier)
            .ThenByDescending(x => x.Rarity)
            .Take(2)
            .Select(x => x.Name ?? x.CharacterId);
        return string.Join(" / ", compTraits.Concat(compUnits));
    }

    private static TftUnitSummaryDto MapUnit(UnitRow unit)
    {
        return new TftUnitSummaryDto(unit.CharacterId, unit.Name, unit.Rarity, unit.Tier, unit.Items);
    }

    private static TftTraitSummaryDto MapTrait(TraitRow trait)
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
        if (string.IsNullOrWhiteSpace(rankTier))
            return "EMERALD_PLUS";

        var normalized = rankTier.Trim().ToUpperInvariant();
        return normalized == "ALL" ? "all" : normalized;
    }

    private static string ToRankTierDisplay(string normalizedRankTier) =>
        normalizedRankTier == "all" ? "ALL" : normalizedRankTier;

    private sealed record ParticipantRow(
        Guid Id,
        Guid MatchId,
        int Placement,
        string[] Augments,
        int? SetNumber,
        string? SetCoreName,
        string? Patch,
        string PlatformRegion
    );

    private sealed record UnitRow(
        Guid ParticipantId,
        string CharacterId,
        string? Name,
        int Rarity,
        int Tier,
        int[] Items
    );

    private sealed record TraitRow(
        Guid ParticipantId,
        string Name,
        int NumUnits,
        int TierCurrent,
        int? Style
    );
}
