using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Account;
using Transcendence.Service.Core.Services.Analysis;
using Transcendence.Service.Core.Queries;
using Transcendence.Service.Core.Services.Jobs;
using Transcendence.Service.Core.Services.Analysis.Exceptions;
using Transcendence.Service.Core.Services.Analysis.Interfaces;
using Transcendence.Service.Core.Services.Analysis.Models;
using Transcendence.Service.Core.Services.RiotApi;
using Transcendence.Service.Core.Services.RiotApi.DTOs;
using Transcendence.Service.Core.Services.StaticData.Models;
using RuneSelectionTree = Transcendence.Data.Models.LoL.Match.RuneSelectionTree;

namespace Transcendence.Service.Core.Services.Analysis.Implementations;

public class SummonerMatchHistoryService(
    TranscendenceContext db,
    HybridCache cache,
    ILogger<SummonerMatchHistoryService> logger)
    : ISummonerMatchHistoryService
{
    // Versioned because both payloads now carry required performance summaries.
    // A new prefix avoids deserializing pre-deploy distributed-cache entries that
    // were written against the older record constructors.
    private const string RecentMatchesCacheKeyPrefix = "stats:recent:v2:";
    private const string LatestMatchDateCacheKeyPrefix = "stats:latest-match-date:";
    private const string MatchDetailCacheKeyPrefix = "match:detail:v2:";
    private const string MatchTimelineCacheKeyPrefix = "match:timeline:";
    private const string SummonerStatsCacheTagPrefix = "summoner-stats:";

    private static readonly HybridCacheEntryOptions StatsCacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(2)
    };

    private static readonly HybridCacheEntryOptions MatchDetailCacheOptions = new()
    {
        Expiration = TimeSpan.FromHours(1),
        LocalCacheExpiration = TimeSpan.FromMinutes(15)
    };

    public async Task<PagedResult<RecentMatchSummary>> GetRecentMatchesAsync(
        Guid summonerId,
        int page,
        int pageSize,
        string? queueFamily,
        IReadOnlyCollection<int>? queueIds,
        int? championId,
        bool includeFacets,
        CancellationToken ct)
    {
        if (page <= 0) page = 1;
        if (pageSize <= 0 || pageSize > 100) pageSize = 20;
        var normalizedFamily = NormalizeQueueFamily(queueFamily);
        var normalizedQueueIds = NormalizeQueueIds(queueIds);
        var queueIdsCacheKey = normalizedQueueIds.Count > 0
            ? string.Join(",", normalizedQueueIds)
            : "-";
        var cacheKey =
            $"{RecentMatchesCacheKeyPrefix}{summonerId}:{page}:{pageSize}:{normalizedFamily}:{queueIdsCacheKey}:{championId?.ToString() ?? "-"}:{includeFacets}";
        return await ExecuteStatsRequestAsync(
            "Failed to compute recent matches.",
            async token => await cache.GetOrCreateAsync(
                cacheKey,
                async cancel => await ComputeRecentMatchesAsync(
                    summonerId,
                    page,
                    pageSize,
                    normalizedFamily,
                    normalizedQueueIds,
                    championId is > 0 ? championId : null,
                    includeFacets,
                    cancel),
                StatsCacheOptions,
                tags: new[] { BuildSummonerStatsTag(summonerId) },
                cancellationToken: token),
            ct);
    }

    public async Task<long?> GetLatestMatchDateAsync(Guid summonerId, CancellationToken ct)
    {
        var cacheKey = $"{LatestMatchDateCacheKeyPrefix}{summonerId}";
        return await ExecuteStatsRequestAsync(
            "Failed to load the latest match date.",
            async token => await cache.GetOrCreateAsync(
                cacheKey,
                async cancel => await db.MatchParticipants
                    .AsNoTracking()
                    .Where(mp => mp.SummonerId == summonerId)
                    .MaxAsync(mp => (long?)mp.Match.MatchDate, cancel),
                StatsCacheOptions,
                tags: new[] { BuildSummonerStatsTag(summonerId) },
                cancellationToken: token),
            ct);
    }

    private async Task<PagedResult<RecentMatchSummary>> ComputeRecentMatchesAsync(
        Guid summonerId,
        int page,
        int pageSize,
        string queueFamily,
        IReadOnlyList<int> queueIds,
        int? championId,
        bool includeFacets,
        CancellationToken ct)
    {
        var baseQuery = db.MatchParticipants
            .AsNoTracking()
            .Where(mp => mp.SummonerId == summonerId);
        var facets = includeFacets
            ? await LoadMatchHistoryFacetsAsync(baseQuery, ct)
            : null;
        var query = ApplyRecentMatchFilters(
                baseQuery,
                queueFamily,
                queueIds,
                championId)
            .AsNoTracking()
            .OrderByDescending(mp => mp.Match.MatchDate)
            .ThenByDescending(mp => mp.Match.MatchId);

        var total = await query.CountAsync(ct);

        List<RecentMatchProjection> participantData;
        try
        {
            participantData = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(mp => new RecentMatchProjection(
                    mp.Id,
                    mp.MatchId,
                    mp.Match.MatchId,
                    mp.Match.MatchDate,
                    mp.Match.Duration,
                    mp.Match.QueueId,
                    mp.Match.QueueType,
                    mp.Match.Patch,
                    mp.Win,
                    mp.ChampionId,
                    mp.TeamPosition,
                    mp.Kills,
                    mp.Deaths,
                    mp.Assists,
                    mp.VisionScore,
                    mp.TotalDamageDealtToChampions,
                    mp.TotalMinionsKilled,
                    mp.NeutralMinionsKilled,
                    mp.SummonerSpell1Id,
                    mp.SummonerSpell2Id))
                .ToListAsync(ct);
        }
        catch (Exception ex) when (ShouldUseConservativeRecentMatchRead(ex))
        {
            logger.LogWarning(
                ex,
                "Bulk recent-match projection failed for summoner {SummonerId}. Retrying with conservative per-row loading.",
                summonerId);
            participantData = await LoadRecentMatchPageConservativelyAsync(query, page, pageSize, ct);
        }

        if (participantData.Count == 0)
            return new PagedResult<RecentMatchSummary>([], page, pageSize, total, facets);

        // The page projection contains only the viewed summoner. Load the bounded set of
        // participants from those matches once so the displayed score is relative to real
        // teammates, rather than an opaque absolute KDA threshold.
        var performanceByParticipant = await LoadMatchPerformanceAsync(
            participantData.Select(value => value.MatchEntityId).Distinct().ToList(),
            ct);

        // Get items and runes for these participants. The participant ids come from the filtered page
        // query above, so the item/rune query filters (a join back through MatchParticipants to
        // Matches per row) cannot exclude anything; IgnoreQueryFilters reads the rows by primary key
        // alone. Items are read flat and grouped here: a grouped projection made EF emit the whole
        // filtered join twice.
        var participantIds = participantData.Select(p => p.ParticipantId).Distinct().ToList();

        var itemRows = await db.Set<Data.Models.LoL.Match.MatchParticipantItem>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(i => participantIds.Contains(i.MatchParticipantId))
            .OrderBy(i => i.MatchParticipantId)
            .ThenBy(i => i.SlotIndex)
            .Select(i => new { i.MatchParticipantId, i.ItemId })
            .ToListAsync(ct);
        var itemsByParticipant = itemRows
            .GroupBy(i => i.MatchParticipantId)
            .ToDictionary(g => g.Key, g => g.Select(i => i.ItemId).ToList());

        // Get runes with explicit selection hierarchy (plus metadata fallback fields)
        var runeRows = await db.Set<Data.Models.LoL.Match.MatchParticipantRune>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(r => participantIds.Contains(r.MatchParticipantId))
            .Select(r => new
            {
                r.MatchParticipantId,
                r.RuneId,
                r.PatchVersion,
                r.SelectionTree,
                r.SelectionIndex,
                r.StyleId
            })
            .ToListAsync(ct);

        var runesByParticipant = runeRows
            .GroupBy(r => r.MatchParticipantId)
            .ToDictionary(
                g => g.Key,
                g => g
                    .Select(r => new StoredRuneSelection(
                        r.RuneId,
                        r.SelectionTree,
                        r.SelectionIndex,
                        r.StyleId))
                    .ToList());

        // Get rune metadata for all runes we need to process
        var allRuneIds = runeRows.Select(r => r.RuneId).Distinct().ToList();
        var patches = participantData
            .Select(p => NormalizePatchVersion(p.Patch))
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct()
            .ToList();

        var runeMetadataRows = allRuneIds.Count > 0
            ? await db.RuneVersions
                .AsNoTracking()
                .Where(rv =>
                    allRuneIds.Contains(rv.RuneId) &&
                    (patches.Count == 0 || patches.Contains(rv.PatchVersion)))
                .Select(rv => new { rv.RuneId, rv.PatchVersion, rv.RunePathId, rv.Slot })
                .ToListAsync(ct)
            : [];

        var runeMetadataByPatch = runeMetadataRows
            .GroupBy(rv => new RunePatchKey(rv.RuneId, NormalizePatchVersion(rv.PatchVersion)))
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var first = g.First();
                    return new RuneSelectionMetadata(first.RunePathId, first.Slot);
                });

        var runeMetadataByRuneId = runeMetadataRows
            .GroupBy(rv => rv.RuneId)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var first = g.First();
                    return new RuneSelectionMetadata(first.RunePathId, first.Slot);
                });

        // Map to final DTOs
        var items = participantData.Select(p =>
        {
            var itemList = itemsByParticipant.GetValueOrDefault(p.ParticipantId) ?? new List<int>();
            if (itemList.Count > 7)
                itemList = itemList.Take(7).ToList();
            // Ensure 7 slots (pad with 0s if needed)
            while (itemList.Count < 7) itemList.Add(0);

            var runeSelections = runesByParticipant.GetValueOrDefault(p.ParticipantId) ?? [];
            var runeSummary = BuildRuneSummary(
                runeSelections,
                p.Patch,
                runeMetadataByPatch,
                runeMetadataByRuneId);
            var runeDetail = BuildRuneDetail(
                runeSelections,
                p.Patch,
                runeMetadataByPatch,
                runeMetadataByRuneId);

            return new RecentMatchSummary(
                p.MatchId ?? string.Empty,
                p.MatchDate,
                p.Duration,
                p.QueueId,
                !string.IsNullOrWhiteSpace(p.QueueType) ? p.QueueType : QueueCatalog.ResolveQueueLabel(p.QueueId),
                p.Win,
                p.ChampionId,
                p.TeamPosition,
                p.Kills,
                p.Deaths,
                p.Assists,
                p.VisionScore,
                p.TotalDamageDealtToChampions,
                p.Duration > 0 ? (p.TotalMinionsKilled + p.NeutralMinionsKilled) / (p.Duration / 60.0) : 0.0,
                p.SummonerSpell1Id,
                p.SummonerSpell2Id,
                itemList,
                runeSummary,
                runeDetail,
                performanceByParticipant.GetValueOrDefault(p.ParticipantId) ??
                BuildFallbackPerformance(p)
            );
        }).ToList();

        return new PagedResult<RecentMatchSummary>(items, page, pageSize, total, facets);
    }

    private async Task<IReadOnlyDictionary<Guid, MatchPerformanceSummary>> LoadMatchPerformanceAsync(
        IReadOnlyCollection<Guid> matchIds,
        CancellationToken ct)
    {
        if (matchIds.Count == 0)
            return new Dictionary<Guid, MatchPerformanceSummary>();

        var participants = await db.MatchParticipants
            .AsNoTracking()
            .Where(participant => matchIds.Contains(participant.MatchId))
            .Select(participant => new MatchPerformanceScorer.Input(
                participant.MatchId,
                participant.Id,
                participant.TeamId,
                participant.Win,
                participant.Kills,
                participant.Deaths,
                participant.Assists,
                participant.GoldEarned,
                participant.TotalDamageDealtToChampions,
                participant.VisionScore,
                participant.TotalMinionsKilled + participant.NeutralMinionsKilled,
                participant.Match.Duration))
            .ToListAsync(ct);

        return MatchPerformanceScorer.Score(participants);
    }

    private static MatchPerformanceSummary BuildFallbackPerformance(RecentMatchProjection participant)
    {
        var csPerMin = participant.Duration > 0
            ? (participant.TotalMinionsKilled + participant.NeutralMinionsKilled) /
              (participant.Duration / 60.0)
            : 0;
        return new MatchPerformanceSummary(5.5, 1, 1, null, 0, 0, 0, 0, Math.Round(csPerMin, 2));
    }

    private static async Task<MatchHistoryFacets> LoadMatchHistoryFacetsAsync(
        IQueryable<Data.Models.LoL.Match.MatchParticipant> query,
        CancellationToken ct)
    {
        var queues = await query
            .Select(mp => new { mp.Match.QueueId, mp.Match.QueueType, mp.Match.QueueFamily })
            .Distinct()
            .OrderBy(value => value.QueueId)
            .ThenBy(value => value.QueueType)
            .ToListAsync(ct);
        var championIds = await query
            .Select(mp => mp.ChampionId)
            .Distinct()
            .OrderBy(value => value)
            .ToListAsync(ct);

        return new MatchHistoryFacets(
            queues.Select(value => new MatchHistoryQueueFacet(
                value.QueueId,
                !string.IsNullOrWhiteSpace(value.QueueType)
                    ? value.QueueType
                    : QueueCatalog.ResolveQueueLabel(value.QueueId),
                !string.IsNullOrWhiteSpace(value.QueueFamily)
                    ? value.QueueFamily
                    : QueueCatalog.ResolveQueueFamily(value.QueueId))).ToList(),
            championIds);
    }

    private async Task<List<RecentMatchProjection>> LoadRecentMatchPageConservativelyAsync(
        IQueryable<Data.Models.LoL.Match.MatchParticipant> query,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var pageRows = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(mp => new { mp.Id, mp.MatchId })
            .ToListAsync(ct);

        var projections = new List<RecentMatchProjection>(pageRows.Count);
        var matchCache = new Dictionary<Guid, RecentMatchMatchProjection>();

        foreach (var row in pageRows)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var participant = await db.MatchParticipants
                    .AsNoTracking()
                    .Where(mp => mp.Id == row.Id)
                    .Select(mp => new
                    {
                        mp.Id,
                        mp.MatchId,
                        mp.Win,
                        mp.ChampionId,
                        mp.TeamPosition,
                        mp.Kills,
                        mp.Deaths,
                        mp.Assists,
                        mp.VisionScore,
                        mp.TotalDamageDealtToChampions,
                        mp.TotalMinionsKilled,
                        mp.NeutralMinionsKilled,
                        mp.SummonerSpell1Id,
                        mp.SummonerSpell2Id
                    })
                    .SingleOrDefaultAsync(ct);

                if (participant == null)
                    continue;

                if (!matchCache.TryGetValue(participant.MatchId, out var match))
                {
                    match = await db.Matches
                        .AsNoTracking()
                        .Where(m => m.Id == participant.MatchId)
                        .Select(m => new RecentMatchMatchProjection(
                            m.Id,
                            m.MatchId,
                            m.MatchDate,
                            m.Duration,
                            m.QueueId,
                            m.QueueType,
                            m.Patch))
                        .SingleOrDefaultAsync(ct);

                    if (match == null)
                        continue;

                    matchCache[participant.MatchId] = match;
                }

                projections.Add(new RecentMatchProjection(
                    participant.Id,
                    participant.MatchId,
                    match.MatchId,
                    match.MatchDate,
                    match.Duration,
                    match.QueueId,
                    match.QueueType,
                    match.Patch,
                    participant.Win,
                    participant.ChampionId,
                    participant.TeamPosition,
                    participant.Kills,
                    participant.Deaths,
                    participant.Assists,
                    participant.VisionScore,
                    participant.TotalDamageDealtToChampions,
                    participant.TotalMinionsKilled,
                    participant.NeutralMinionsKilled,
                    participant.SummonerSpell1Id,
                    participant.SummonerSpell2Id));
            }
            catch (Exception ex) when (ShouldUseConservativeRecentMatchRead(ex))
            {
                logger.LogWarning(
                    ex,
                    "Skipping unreadable match participant {ParticipantId} while assembling recent matches.",
                    row.Id);
            }
        }

        return projections;
    }

    public async Task<MatchDetailDto?> GetMatchDetailAsync(string matchId, CancellationToken ct)
    {
        var cacheKey = $"{MatchDetailCacheKeyPrefix}{matchId}";
        return await ExecuteStatsRequestAsync(
            "Failed to load match detail.",
            async token => await cache.GetOrCreateAsync(
                cacheKey,
                async cancel => await ComputeMatchDetailAsync(matchId, cancel),
                MatchDetailCacheOptions,
                cancellationToken: token),
            ct);
    }

    public async Task<MatchTimelineDto?> GetMatchTimelineAsync(string matchId, CancellationToken ct)
    {
        var cacheKey = $"{MatchTimelineCacheKeyPrefix}{matchId}";
        return await ExecuteStatsRequestAsync(
            "Failed to load match timeline.",
            async token =>
            {
                var result = await cache.GetOrCreateAsync(
                    cacheKey,
                    async cancel => await ComputeMatchTimelineAsync(matchId, cancel),
                    MatchDetailCacheOptions,
                    cancellationToken: token);

                // Do NOT persist an empty timeline under the long TTL: a match whose
                // gold/XP snapshots have not been derived yet returns zero frames, and
                // caching that for an hour would mask a freshly-ingested timeline with
                // no invalidation path. Evict so the next request recomputes. Populated
                // results stay cached; on a cache hit the entry is already non-empty
                // (we never store empties), so this only fires on a miss that computed
                // empty frames.
                if (result is { Frames.Count: 0 })
                    await cache.RemoveAsync(cacheKey, token);

                return result;
            },
            ct);
    }

    private async Task<MatchTimelineDto?> ComputeMatchTimelineAsync(string matchId, CancellationToken ct)
    {
        var match = await db.Matches
            .AsNoTracking()
            .Where(m => m.MatchId == matchId)
            .Select(m => new { m.Id, m.Duration })
            .FirstOrDefaultAsync(ct);

        if (match == null)
            return null;

        var rows = await (
            from s in db.MatchParticipantTimelineSnapshots.AsNoTracking()
            where s.MatchId == match.Id && (s.MinuteMark % 2 == 0 || s.MinuteMark == 15)
            join p in db.MatchParticipants.AsNoTracking()
                on new { s.MatchId, s.ParticipantId } equals new { p.MatchId, p.ParticipantId }
            select new { s.MinuteMark, p.TeamId, s.Gold, s.Xp }
        ).ToListAsync(ct);

        var frames = rows
            .GroupBy(r => r.MinuteMark)
            .OrderBy(g => g.Key)
            .Select(g => new TimelineFrameDto(
                g.Key,
                g.Where(x => x.TeamId == 100).Sum(x => x.Gold),
                g.Where(x => x.TeamId == 200).Sum(x => x.Gold),
                g.Where(x => x.TeamId == 100).Sum(x => x.Xp),
                g.Where(x => x.TeamId == 200).Sum(x => x.Xp)))
            .ToList();

        return new MatchTimelineDto(matchId, match.Duration, frames);
    }

    private async Task<MatchDetailDto?> ComputeMatchDetailAsync(string matchId, CancellationToken ct)
    {
        var match = await db.Matches
            .AsNoTracking()
            .AsSplitQuery()
            .Include(m => m.Participants)
                .ThenInclude(p => p.Summoner)
            .Include(m => m.Participants)
                .ThenInclude(p => p.Items)
            .Include(m => m.Participants)
                .ThenInclude(p => p.Runes)
            .Include(m => m.Bans)
            .Include(m => m.TeamObjectives)
            .FirstOrDefaultAsync(m => m.MatchId == matchId, ct);

        if (match == null)
            return null;

        // Get rune metadata for determining primary/sub styles
        var runeIds = match.Participants
            .SelectMany(p => p.Runes.Select(r => r.RuneId))
            .Distinct()
            .ToList();

        var normalizedPatch = NormalizePatchVersion(match.Patch);
        var runeMetadataRows = runeIds.Count > 0
            ? await db.RuneVersions
                .AsNoTracking()
                .Where(rv => runeIds.Contains(rv.RuneId) &&
                             (string.IsNullOrWhiteSpace(normalizedPatch) || rv.PatchVersion == normalizedPatch))
                .Select(rv => new { rv.RuneId, rv.RunePathId, rv.Slot })
                .ToListAsync(ct)
            : [];

        var runeMetadata = runeMetadataRows
            .GroupBy(rv => rv.RuneId)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var first = g.First();
                    return new RuneSelectionMetadata(first.RunePathId, first.Slot);
                });

        var performanceByParticipant = MatchPerformanceScorer.Score(
            match.Participants.Select(participant => new MatchPerformanceScorer.Input(
                participant.MatchId,
                participant.Id,
                participant.TeamId,
                participant.Win,
                participant.Kills,
                participant.Deaths,
                participant.Assists,
                participant.GoldEarned,
                participant.TotalDamageDealtToChampions,
                participant.VisionScore,
                participant.TotalMinionsKilled + participant.NeutralMinionsKilled,
                match.Duration)));
        var participants = match.Participants
            .Select(participant => MapParticipant(
                participant,
                runeMetadata,
                performanceByParticipant.GetValueOrDefault(participant.Id) ??
                new MatchPerformanceSummary(5.5, 1, 1, null, 0, 0, 0, 0, 0)))
            .ToList();

        var bans = match.Bans
            .GroupBy(b => b.TeamId)
            .OrderBy(g => g.Key)
            .Select(g => new TeamBansDto(
                g.Key,
                g.OrderBy(b => b.PickTurn).Select(b => b.ChampionId).ToList()))
            .ToList();

        var objectives = match.TeamObjectives
            .OrderBy(o => o.TeamId)
            .Select(o => new TeamObjectivesDto(
                o.TeamId,
                o.FirstBlood,
                new ObjectiveStatDto(o.BaronKills, o.BaronFirst),
                new ObjectiveStatDto(o.DragonKills, o.DragonFirst),
                new ObjectiveStatDto(o.RiftHeraldKills, o.RiftHeraldFirst),
                new ObjectiveStatDto(o.HordeKills, o.HordeFirst),
                new ObjectiveStatDto(o.TowerKills, o.TowerFirst),
                new ObjectiveStatDto(o.InhibitorKills, o.InhibitorFirst)))
            .ToList();

        return new MatchDetailDto(
            match.MatchId ?? string.Empty,
            match.MatchDate,
            match.Duration,
            match.QueueId,
            !string.IsNullOrWhiteSpace(match.QueueType)
                ? match.QueueType
                : QueueCatalog.ResolveQueueLabel(match.QueueId),
            string.IsNullOrWhiteSpace(match.Patch) ? null : match.Patch,
            participants,
            bans,
            objectives
        );
    }

    private static ParticipantDetailDto MapParticipant(
        Data.Models.LoL.Match.MatchParticipant p,
        Dictionary<int, RuneSelectionMetadata> runeMetadata,
        MatchPerformanceSummary performance)
    {
        var items = p.Items
            .OrderBy(i => i.SlotIndex)
            .Select(i => i.ItemId)
            .ToList();

        // Build runes structure from explicit selection data with metadata fallback for legacy rows.
        var runes = BuildRunesDto(
            p.Runes.Select(r => new StoredRuneSelection(
                r.RuneId,
                r.SelectionTree,
                r.SelectionIndex,
                r.StyleId)).ToList(),
            runeMetadata);

        return new ParticipantDetailDto(
            p.Puuid,
            p.Summoner?.GameName,
            p.Summoner?.TagLine,
            p.TeamId,
            p.ChampionId,
            p.TeamPosition,
            p.Win,
            p.Kills,
            p.Deaths,
            p.Assists,
            p.ChampLevel,
            p.GoldEarned,
            p.TotalDamageDealtToChampions,
            p.PhysicalDamageDealtToChampions,
            p.MagicDamageDealtToChampions,
            p.TrueDamageDealtToChampions,
            p.VisionScore,
            p.TotalMinionsKilled,
            p.NeutralMinionsKilled,
            p.SummonerSpell1Id,
            p.SummonerSpell2Id,
            items,
            runes,
            performance
        );
    }

    private static ParticipantRunesDto BuildRunesDto(
        List<StoredRuneSelection> selections,
        Dictionary<int, RuneSelectionMetadata> runeMetadata)
    {
        var mapped = RuneSelectionMapper.Map(
            selections,
            runeId => runeMetadata.TryGetValue(runeId, out var metadata) ? metadata : null);
        return new ParticipantRunesDto(
            mapped.PrimaryStyleId,
            mapped.SubStyleId,
            mapped.PrimaryRunes,
            mapped.SubRunes,
            mapped.StatShards);
    }

    private static string NormalizePatchVersion(string? patchVersion)
    {
        return string.IsNullOrWhiteSpace(patchVersion) ? string.Empty : patchVersion.Trim();
    }

    private static string BuildSummonerStatsTag(Guid summonerId) => $"{SummonerStatsCacheTagPrefix}{summonerId}";

    private static string NormalizeQueueFamily(string? queueFamily)
    {
        if (string.IsNullOrWhiteSpace(queueFamily))
            return QueueCatalog.QueueFamilyAll;

        var normalized = queueFamily.Trim().ToUpperInvariant();
        var validFamilies = QueueCatalog.GetKnownQueueFamilies().ToHashSet(StringComparer.Ordinal);

        return validFamilies.Contains(normalized) ? normalized : QueueCatalog.QueueFamilyAll;
    }

    private static IReadOnlyList<int> NormalizeQueueIds(IReadOnlyCollection<int>? queueIds)
    {
        if (queueIds == null || queueIds.Count == 0)
            return [];

        return queueIds
            .Where(id => id > 0)
            .Distinct()
            .OrderBy(id => id)
            .ToList();
    }

    private static IQueryable<Data.Models.LoL.Match.MatchParticipant> ApplyRecentMatchFilters(
        IQueryable<Data.Models.LoL.Match.MatchParticipant> query,
        string queueFamily,
        IReadOnlyList<int> queueIds,
        int? championId)
    {
        if (queueIds.Count > 0)
        {
            var queueIdSet = queueIds.ToHashSet();
            var queueTypeSet = queueIds.Select(id => id.ToString()).ToHashSet(StringComparer.Ordinal);
            query = query.Where(mp =>
                queueIdSet.Contains(mp.Match.QueueId) ||
                (mp.Match.QueueId == 0 && mp.Match.QueueType != null && queueTypeSet.Contains(mp.Match.QueueType)));
        }

        if (!string.Equals(queueFamily, QueueCatalog.QueueFamilyAll, StringComparison.Ordinal))
            query = query.Where(mp => mp.Match.QueueFamily == queueFamily);

        if (championId is > 0)
            query = query.Where(mp => mp.ChampionId == championId.Value);

        return query;
    }

    /// <summary>
    /// Builds a rune summary (primary/sub styles + keystone) for match history.
    /// Simpler than BuildRunesDto - just enough for match cards.
    /// </summary>
    private static MatchRuneSummary BuildRuneSummary(
        List<StoredRuneSelection> selections,
        string? patchVersion,
        Dictionary<RunePatchKey, RuneSelectionMetadata> runeMetadata,
        IReadOnlyDictionary<int, RuneSelectionMetadata> runeMetadataByRuneId)
    {
        var detail = BuildRuneDetail(selections, patchVersion, runeMetadata, runeMetadataByRuneId);
        return new MatchRuneSummary(
            detail.PrimaryStyleId,
            detail.SubStyleId,
            detail.PrimarySelections.FirstOrDefault());
    }

    private static MatchRuneDetail BuildRuneDetail(
        List<StoredRuneSelection> selections,
        string? patchVersion,
        Dictionary<RunePatchKey, RuneSelectionMetadata> runeMetadataByPatch,
        IReadOnlyDictionary<int, RuneSelectionMetadata> runeMetadataByRuneId)
    {
        var normalizedPatch = NormalizePatchVersion(patchVersion);
        var mapped = RuneSelectionMapper.Map(
            selections,
            runeId => TryGetRuneMetadata(
                runeId,
                normalizedPatch,
                runeMetadataByPatch,
                runeMetadataByRuneId,
                out var metadata)
                    ? metadata
                    : null);
        return new MatchRuneDetail(
            mapped.PrimaryStyleId,
            mapped.SubStyleId,
            mapped.PrimaryRunes,
            mapped.SubRunes,
            mapped.StatShards);
    }

    private static bool TryGetRuneMetadata(
        int runeId,
        string normalizedPatch,
        IReadOnlyDictionary<RunePatchKey, RuneSelectionMetadata> runeMetadataByPatch,
        IReadOnlyDictionary<int, RuneSelectionMetadata> runeMetadataByRuneId,
        out RuneSelectionMetadata metadata)
    {
        if (runeMetadataByPatch.TryGetValue(new RunePatchKey(runeId, normalizedPatch), out metadata))
            return true;

        return runeMetadataByRuneId.TryGetValue(runeId, out metadata);
    }

    /// <summary>
    /// Internal record for rune metadata lookup.
    /// </summary>
    private readonly record struct RunePatchKey(int RuneId, string PatchVersion);

    private sealed record RecentMatchMatchProjection(
        Guid Id,
        string? MatchId,
        long MatchDate,
        int Duration,
        int QueueId,
        string? QueueType,
        string? Patch);
    private sealed record RecentMatchProjection(
        Guid ParticipantId,
        Guid MatchEntityId,
        string? MatchId,
        long MatchDate,
        int Duration,
        int QueueId,
        string? QueueType,
        string? Patch,
        bool Win,
        int ChampionId,
        string? TeamPosition,
        int Kills,
        int Deaths,
        int Assists,
        int VisionScore,
        int TotalDamageDealtToChampions,
        int TotalMinionsKilled,
        int NeutralMinionsKilled,
        int SummonerSpell1Id,
        int SummonerSpell2Id);

    private static bool ShouldUseConservativeRecentMatchRead(Exception ex)
    {
        for (Exception? current = ex; current != null; current = current.InnerException)
        {
            if (current is ArgumentOutOfRangeException)
                return true;
        }

        return false;
    }

    private static async Task<T> ExecuteStatsRequestAsync<T>(
        string failureMessage,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken ct)
    {
        try
        {
            return await operation(ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (SummonerStatsComputationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new SummonerStatsComputationException(failureMessage, ex);
        }
    }
}
