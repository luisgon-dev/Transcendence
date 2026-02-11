using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Transcendence.Data;
using Transcendence.Service.Core.Services.Analysis.Interfaces;
using Transcendence.Service.Core.Services.Analysis.Models;
using Transcendence.Service.Core.Services.RiotApi.DTOs;
using RuneSelectionTree = Transcendence.Data.Models.LoL.Match.RuneSelectionTree;

namespace Transcendence.Service.Core.Services.Analysis.Implementations;

public class SummonerStatsService(TranscendenceContext db, HybridCache cache, ILogger<SummonerStatsService> logger)
    : ISummonerStatsService
{
    // Cache key prefixes
    private const string OverviewCacheKeyPrefix = "stats:overview:";
    private const string ChampionsCacheKeyPrefix = "stats:champions:";
    private const string RolesCacheKeyPrefix = "stats:roles:";
    private const string RecentMatchesCacheKeyPrefix = "stats:recent:";
    private const string MatchDetailCacheKeyPrefix = "match:detail:";

    // Stats cache options: 5min total, 2min L1 (stats change on refresh)
    private static readonly HybridCacheEntryOptions StatsCacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(2)
    };

    // Match detail cache options: 1hr total, 15min L1 (match data is immutable)
    private static readonly HybridCacheEntryOptions MatchDetailCacheOptions = new()
    {
        Expiration = TimeSpan.FromHours(1),
        LocalCacheExpiration = TimeSpan.FromMinutes(15)
    };

    public async Task<SummonerOverviewStats> GetSummonerOverviewAsync(Guid summonerId, int recentGamesCount,
        CancellationToken ct)
    {
        if (recentGamesCount <= 0) recentGamesCount = 20;
        try
        {
            var cacheKey = $"{OverviewCacheKeyPrefix}{summonerId}:{recentGamesCount}";
            return await cache.GetOrCreateAsync(
                cacheKey,
                async cancel => await ComputeOverviewAsync(summonerId, recentGamesCount, cancel),
                StatsCacheOptions,
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to compute overview stats for summoner {SummonerId}. Returning an empty payload.",
                summonerId);
            return EmptyOverview(summonerId);
        }
    }

    private async Task<SummonerOverviewStats> ComputeOverviewAsync(Guid summonerId, int recentGamesCount,
        CancellationToken ct)
    {
        var baseQuery = db.MatchParticipants
            .AsNoTracking()
            .Where(mp => mp.SummonerId == summonerId)
            .Select(mp => new
            {
                mp.Win,
                mp.Kills,
                mp.Deaths,
                mp.Assists,
                mp.VisionScore,
                mp.TotalDamageDealtToChampions,
                Cs = mp.TotalMinionsKilled + mp.NeutralMinionsKilled,
                DurationSeconds = mp.Match.Duration
            });

        var aggregate = await baseQuery
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Wins = g.Sum(x => x.Win ? 1 : 0),
                Losses = g.Sum(x => x.Win ? 0 : 1),
                AvgKills = g.Average(x => (double)x.Kills),
                AvgDeaths = g.Average(x => (double)x.Deaths),
                AvgAssists = g.Average(x => (double)x.Assists),
                AvgVision = g.Average(x => (double)x.VisionScore),
                AvgDamage = g.Average(x => (double)x.TotalDamageDealtToChampions),
                AvgCsPerMin = g.Average(x => x.DurationSeconds > 0 ? x.Cs / (x.DurationSeconds / 60d) : 0d),
                AvgDurationMin = g.Average(x => x.DurationSeconds / 60.0)
            })
            .FirstOrDefaultAsync(ct);

        var total = aggregate?.Total ?? 0;
        var wins = aggregate?.Wins ?? 0;
        var losses = aggregate?.Losses ?? 0;
        var wr = total > 0 ? (double)wins / total * 100.0 : 0.0;

        var recent = await db.MatchParticipants
            .AsNoTracking()
            .Where(mp => mp.SummonerId == summonerId)
            .OrderByDescending(mp => mp.Match.MatchDate)
            .Select(mp => new RecentPerformancePoint(
                mp.Match.MatchId!,
                mp.Win,
                mp.Kills,
                mp.Deaths,
                mp.Assists,
                mp.Match.Duration > 0
                    ? (mp.TotalMinionsKilled + mp.NeutralMinionsKilled) / (mp.Match.Duration / 60.0)
                    : 0.0,
                mp.VisionScore,
                mp.TotalDamageDealtToChampions
            ))
            .Take(recentGamesCount)
            .ToListAsync(ct);

        return new SummonerOverviewStats(
            summonerId,
            total,
            wins,
            losses,
            wr,
            aggregate?.AvgKills ?? 0,
            aggregate?.AvgDeaths ?? 0,
            aggregate?.AvgAssists ?? 0,
            CalcKdaRatio(aggregate?.AvgKills ?? 0, aggregate?.AvgDeaths ?? 0, aggregate?.AvgAssists ?? 0),
            aggregate?.AvgCsPerMin ?? 0,
            aggregate?.AvgVision ?? 0,
            aggregate?.AvgDamage ?? 0,
            aggregate?.AvgDurationMin ?? 0,
            recent
        );
    }

    public async Task<IReadOnlyList<ChampionStat>> GetChampionStatsAsync(Guid summonerId, int top, CancellationToken ct)
    {
        if (top <= 0) top = 10;
        try
        {
            var cacheKey = $"{ChampionsCacheKeyPrefix}{summonerId}:{top}";
            return await cache.GetOrCreateAsync(
                cacheKey,
                async cancel => await ComputeChampionStatsAsync(summonerId, top, cancel),
                StatsCacheOptions,
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to compute champion stats for summoner {SummonerId}. Returning an empty payload.",
                summonerId);
            return [];
        }
    }

    private async Task<IReadOnlyList<ChampionStat>> ComputeChampionStatsAsync(Guid summonerId, int top,
        CancellationToken ct)
    {
        var games = await db.MatchParticipants
            .AsNoTracking()
            .Where(mp => mp.SummonerId == summonerId)
            .Select(mp => new
            {
                mp.ChampionId,
                mp.Win,
                mp.Kills,
                mp.Deaths,
                mp.Assists,
                mp.VisionScore,
                mp.TotalDamageDealtToChampions,
                Cs = mp.TotalMinionsKilled + mp.NeutralMinionsKilled,
                MatchDuration = mp.Match.Duration
            })
            .ToListAsync(ct);

        var list = games
            .GroupBy(x => x.ChampionId)
            .Select(g => new ChampionStat(
                g.Key,
                g.Count(),
                g.Sum(x => x.Win ? 1 : 0),
                g.Sum(x => x.Win ? 0 : 1),
                g.Count() > 0 ? (double)g.Sum(x => x.Win ? 1 : 0) / g.Count() * 100.0 : 0.0,
                g.Average(x => (double)x.Kills),
                g.Average(x => (double)x.Deaths),
                g.Average(x => (double)x.Assists),
                0, // fill KDA after
                g.Average(x => x.MatchDuration > 0 ? x.Cs / (x.MatchDuration / 60.0) : 0.0),
                g.Average(x => (double)x.VisionScore),
                g.Average(x => (double)x.TotalDamageDealtToChampions)
            ))
            .OrderByDescending(x => x.Games)
            .Take(top)
            .ToList();

        // Compute KDA for each (post-projection)
        return list
            .Select(x => x with
            {
                KdaRatio = CalcKdaRatio(x.AvgKills, x.AvgDeaths, x.AvgAssists)
            })
            .ToList();
    }

    public async Task<IReadOnlyList<RoleStat>> GetRoleBreakdownAsync(Guid summonerId, CancellationToken ct)
    {
        try
        {
            var cacheKey = $"{RolesCacheKeyPrefix}{summonerId}";
            return await cache.GetOrCreateAsync(
                cacheKey,
                async cancel => await ComputeRoleBreakdownAsync(summonerId, cancel),
                StatsCacheOptions,
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to compute role breakdown for summoner {SummonerId}. Returning an empty payload.",
                summonerId);
            return [];
        }
    }

    private async Task<IReadOnlyList<RoleStat>> ComputeRoleBreakdownAsync(Guid summonerId, CancellationToken ct)
    {
        var rows = await db.MatchParticipants
            .AsNoTracking()
            .Where(mp => mp.SummonerId == summonerId)
            .Select(mp => new { mp.TeamPosition, mp.Win })
            .ToListAsync(ct);

        var list = rows
            .GroupBy(row => NormalizeTeamPosition(row.TeamPosition))
            .Select(g =>
            {
                var games = g.Count();
                var wins = g.Sum(x => x.Win ? 1 : 0);
                return new RoleStat(
                    g.Key,
                    games,
                    wins,
                    games - wins,
                    games > 0 ? (double)wins / games * 100.0 : 0.0
                );
            })
            .OrderByDescending(x => x.Games)
            .ToList();

        if (rows.Count > 0 && list.Count == 0)
        {
            logger.LogWarning(
                "Role breakdown produced no buckets for summoner {SummonerId} despite {MatchCount} matches.",
                summonerId,
                rows.Count);
        }

        return list;
    }

    public async Task<PagedResult<RecentMatchSummary>> GetRecentMatchesAsync(Guid summonerId, int page, int pageSize,
        CancellationToken ct)
    {
        if (page <= 0) page = 1;
        if (pageSize <= 0 || pageSize > 100) pageSize = 20;
        try
        {
            var cacheKey = $"{RecentMatchesCacheKeyPrefix}{summonerId}:{page}:{pageSize}";
            return await cache.GetOrCreateAsync(
                cacheKey,
                async cancel => await ComputeRecentMatchesAsync(summonerId, page, pageSize, cancel),
                StatsCacheOptions,
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to compute recent matches for summoner {SummonerId} page {Page} size {PageSize}. Returning an empty payload.",
                summonerId,
                page,
                pageSize);
            return new PagedResult<RecentMatchSummary>([], page, pageSize, 0);
        }
    }

    private async Task<PagedResult<RecentMatchSummary>> ComputeRecentMatchesAsync(Guid summonerId, int page,
        int pageSize, CancellationToken ct)
    {
        var query = db.MatchParticipants
            .AsNoTracking()
            .Where(mp => mp.SummonerId == summonerId)
            .OrderByDescending(mp => mp.Match.MatchDate);

        var total = await query.CountAsync(ct);

        // First, get participant IDs with match data
        var participantData = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(mp => new
            {
                mp.Id,
                mp.Match.MatchId,
                mp.Match.MatchDate,
                mp.Match.Duration,
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
                mp.SummonerSpell2Id
            })
            .ToListAsync(ct);

        if (participantData.Count == 0)
            return new PagedResult<RecentMatchSummary>([], page, pageSize, total);

        // Get items and runes for these participants
        var participantIds = participantData.Select(p => p.Id).Distinct().ToList();

        var itemsByParticipant = await db.Set<Data.Models.LoL.Match.MatchParticipantItem>()
            .AsNoTracking()
            .Where(i => participantIds.Contains(i.MatchParticipantId))
            .GroupBy(i => i.MatchParticipantId)
            .Select(g => new { ParticipantId = g.Key, Items = g.Select(i => i.ItemId).ToList() })
            .ToDictionaryAsync(x => x.ParticipantId, x => x.Items, ct);

        // Get runes with explicit selection hierarchy (plus metadata fallback fields)
        var runeRows = await db.Set<Data.Models.LoL.Match.MatchParticipantRune>()
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
                        r.PatchVersion,
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
                    return new RuneMetadata(first.RunePathId, first.Slot);
                });

        var runeMetadataByRuneId = runeMetadataRows
            .GroupBy(rv => rv.RuneId)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var first = g.First();
                    return new RuneMetadata(first.RunePathId, first.Slot);
                });

        // Map to final DTOs
        var items = participantData.Select(p =>
        {
            var itemList = itemsByParticipant.GetValueOrDefault(p.Id) ?? new List<int>();
            if (itemList.Count > 7)
                itemList = itemList.Take(7).ToList();
            // Ensure 7 slots (pad with 0s if needed)
            while (itemList.Count < 7) itemList.Add(0);

            var runeSelections = runesByParticipant.GetValueOrDefault(p.Id) ?? [];
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
                p.QueueType ?? "UNKNOWN",
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
                runeDetail
            );
        }).ToList();

        return new PagedResult<RecentMatchSummary>(items, page, pageSize, total);
    }

    public async Task<MatchDetailDto?> GetMatchDetailAsync(string matchId, CancellationToken ct)
    {
        try
        {
            var cacheKey = $"{MatchDetailCacheKeyPrefix}{matchId}";
            return await cache.GetOrCreateAsync(
                cacheKey,
                async cancel => await ComputeMatchDetailAsync(matchId, cancel),
                MatchDetailCacheOptions,
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to compute match detail for match {MatchId}. Returning null.",
                matchId);
            return null;
        }
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
                    return new RuneMetadata(first.RunePathId, first.Slot);
                });

        var participants = match.Participants.Select(p => MapParticipant(p, runeMetadata)).ToList();

        return new MatchDetailDto(
            match.MatchId ?? string.Empty,
            match.MatchDate,
            match.Duration,
            match.QueueType ?? "UNKNOWN",
            string.IsNullOrWhiteSpace(match.Patch) ? null : match.Patch,
            participants
        );
    }

    private static ParticipantDetailDto MapParticipant(
        Data.Models.LoL.Match.MatchParticipant p,
        Dictionary<int, RuneMetadata> runeMetadata)
    {
        var items = p.Items.Select(i => i.ItemId).ToList();

        // Build runes structure from explicit selection data with metadata fallback for legacy rows.
        var runes = BuildRunesDto(
            p.Runes.Select(r => new StoredRuneSelection(
                r.RuneId,
                r.PatchVersion,
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
            p.VisionScore,
            p.TotalMinionsKilled,
            p.NeutralMinionsKilled,
            p.SummonerSpell1Id,
            p.SummonerSpell2Id,
            items,
            runes
        );
    }

    private static ParticipantRunesDto BuildRunesDto(
        List<StoredRuneSelection> selections,
        Dictionary<int, RuneMetadata> runeMetadata)
    {
        if (selections.Count == 0)
            return new ParticipantRunesDto(0, 0, [], [], []);

        if (HasStructuredSelections(selections))
        {
            var primarySelections = selections
                .Where(s => s.SelectionTree == RuneSelectionTree.Primary)
                .OrderBy(s => s.SelectionIndex)
                .Select(s => s.RuneId)
                .ToList();
            var subSelections = selections
                .Where(s => s.SelectionTree == RuneSelectionTree.Secondary)
                .OrderBy(s => s.SelectionIndex)
                .Select(s => s.RuneId)
                .ToList();
            var statShards = selections
                .Where(s => s.SelectionTree == RuneSelectionTree.StatShards)
                .OrderBy(s => s.SelectionIndex)
                .Select(s => s.RuneId)
                .ToList();

            var primaryStyleId = selections
                .Where(s => s.SelectionTree == RuneSelectionTree.Primary && s.StyleId > 0)
                .Select(s => s.StyleId)
                .FirstOrDefault();
            var subStyleId = selections
                .Where(s => s.SelectionTree == RuneSelectionTree.Secondary && s.StyleId > 0)
                .Select(s => s.StyleId)
                .FirstOrDefault();

            if (primaryStyleId == 0 && primarySelections.Count > 0 &&
                runeMetadata.TryGetValue(primarySelections[0], out var primaryMeta))
                primaryStyleId = primaryMeta.PathId;

            if (subStyleId == 0 && subSelections.Count > 0 &&
                runeMetadata.TryGetValue(subSelections[0], out var subMeta))
                subStyleId = subMeta.PathId;

            return new ParticipantRunesDto(
                primaryStyleId,
                subStyleId,
                primarySelections,
                subSelections,
                statShards
            );
        }

        // Legacy fallback: infer trees by path/slot metadata.
        var runesByPath = selections
            .Select(s => runeMetadata.TryGetValue(s.RuneId, out var meta)
                ? (RuneId: s.RuneId, PathId: meta.PathId, Slot: meta.Slot)
                : (RuneId: s.RuneId, PathId: 0, Slot: s.SelectionIndex))
            .GroupBy(x => x.PathId)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Slot).ToList());

        var statPath = runesByPath
            .Where(kvp => kvp.Key >= 5000)
            .SelectMany(kvp => kvp.Value)
            .OrderBy(x => x.Slot)
            .Select(x => x.RuneId)
            .Take(3)
            .ToList();

        var nonStatPaths = runesByPath
            .Where(kvp => kvp.Key > 0 && kvp.Key < 5000)
            .Select(kvp => new { PathId = kvp.Key, Runes = kvp.Value })
            .OrderByDescending(x => x.Runes.Count)
            .ThenBy(x => x.PathId)
            .ToList();

        var primaryPath = nonStatPaths.FirstOrDefault();
        var secondaryPath = nonStatPaths.Skip(1).FirstOrDefault();

        return new ParticipantRunesDto(
            primaryPath?.PathId ?? 0,
            secondaryPath?.PathId ?? 0,
            primaryPath?.Runes.Select(r => r.RuneId).ToList() ?? [],
            secondaryPath?.Runes.Select(r => r.RuneId).ToList() ?? [],
            statPath
        );
    }

    private static bool HasStructuredSelections(List<StoredRuneSelection> selections)
    {
        if (selections.Count == 0)
            return false;

        var hasNonDefaultHierarchy = selections.Any(s =>
            s.SelectionTree != RuneSelectionTree.Primary ||
            s.StyleId != 0);

        if (!hasNonDefaultHierarchy)
            return false;

        var uniqueSlots = selections
            .Select(s => (s.SelectionTree, s.SelectionIndex))
            .Distinct()
            .Count();

        return uniqueSlots == selections.Count;
    }

    private static double CalcKdaRatio(double kills, double deaths, double assists)
    {
        return (kills + assists) / Math.Max(1.0, deaths);
    }

    private static string NormalizePatchVersion(string? patchVersion)
    {
        return string.IsNullOrWhiteSpace(patchVersion) ? string.Empty : patchVersion.Trim();
    }

    private static string NormalizeTeamPosition(string? teamPosition)
    {
        if (string.IsNullOrWhiteSpace(teamPosition))
            return "UNKNOWN";

        return teamPosition.Trim().ToUpperInvariant();
    }

    private static SummonerOverviewStats EmptyOverview(Guid summonerId)
    {
        return new SummonerOverviewStats(
            summonerId,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            []);
    }

    /// <summary>
    /// Builds a rune summary (primary/sub styles + keystone) for match history.
    /// Simpler than BuildRunesDto - just enough for match cards.
    /// </summary>
    private static MatchRuneSummary BuildRuneSummary(
        List<StoredRuneSelection> selections,
        string? patchVersion,
        Dictionary<RunePatchKey, RuneMetadata> runeMetadata,
        IReadOnlyDictionary<int, RuneMetadata> runeMetadataByRuneId)
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
        Dictionary<RunePatchKey, RuneMetadata> runeMetadataByPatch,
        IReadOnlyDictionary<int, RuneMetadata> runeMetadataByRuneId)
    {
        if (selections.Count == 0)
            return new MatchRuneDetail(0, 0, [], [], []);

        var normalizedPatch = NormalizePatchVersion(patchVersion);

        if (HasStructuredSelections(selections))
        {
            var primarySelections = selections
                .Where(s => s.SelectionTree == RuneSelectionTree.Primary)
                .OrderBy(s => s.SelectionIndex)
                .Select(s => s.RuneId)
                .ToList();
            var subSelections = selections
                .Where(s => s.SelectionTree == RuneSelectionTree.Secondary)
                .OrderBy(s => s.SelectionIndex)
                .Select(s => s.RuneId)
                .ToList();
            var statShards = selections
                .Where(s => s.SelectionTree == RuneSelectionTree.StatShards)
                .OrderBy(s => s.SelectionIndex)
                .Select(s => s.RuneId)
                .ToList();

            var primaryStyleId = selections
                .Where(s => s.SelectionTree == RuneSelectionTree.Primary && s.StyleId > 0)
                .Select(s => s.StyleId)
                .FirstOrDefault();
            var subStyleId = selections
                .Where(s => s.SelectionTree == RuneSelectionTree.Secondary && s.StyleId > 0)
                .Select(s => s.StyleId)
                .FirstOrDefault();

            if (primaryStyleId == 0 && primarySelections.Count > 0 &&
                TryGetRuneMetadata(primarySelections[0], normalizedPatch, runeMetadataByPatch, runeMetadataByRuneId,
                    out var primaryMeta) &&
                primaryMeta.PathId is > 0 and < 5000)
            {
                primaryStyleId = primaryMeta.PathId;
            }

            if (subStyleId == 0 && subSelections.Count > 0 &&
                TryGetRuneMetadata(subSelections[0], normalizedPatch, runeMetadataByPatch, runeMetadataByRuneId,
                    out var subMeta) &&
                subMeta.PathId is > 0 and < 5000)
            {
                subStyleId = subMeta.PathId;
            }

            return new MatchRuneDetail(primaryStyleId, subStyleId, primarySelections, subSelections, statShards);
        }

        // Legacy fallback: infer trees/styles by path metadata.
        var resolvedRunes = selections
            .Select(s =>
            {
                return TryGetRuneMetadata(s.RuneId, normalizedPatch, runeMetadataByPatch, runeMetadataByRuneId,
                    out var meta)
                    ? (RuneId: s.RuneId, PathId: meta.PathId, Slot: meta.Slot)
                    : (RuneId: s.RuneId, PathId: 0, Slot: s.SelectionIndex);
            })
            .ToList();

        var statShardsFallback = resolvedRunes
            .Where(r => r.PathId >= 5000)
            .OrderBy(r => r.Slot)
            .Select(r => r.RuneId)
            .Take(3)
            .ToList();

        var nonStatPaths = resolvedRunes
            .Where(r => r.PathId > 0 && r.PathId < 5000)
            .GroupBy(r => r.PathId)
            .Select(g => new
            {
                PathId = g.Key,
                Runes = g.OrderBy(x => x.Slot).ToList()
            })
            .OrderByDescending(x => x.Runes.Count)
            .ThenBy(x => x.PathId)
            .ToList();

        var primaryPath = nonStatPaths.FirstOrDefault();
        var secondaryPath = nonStatPaths.Skip(1).FirstOrDefault();

        return new MatchRuneDetail(
            primaryPath?.PathId ?? 0,
            secondaryPath?.PathId ?? 0,
            primaryPath?.Runes.Select(r => r.RuneId).ToList() ?? [],
            secondaryPath?.Runes.Select(r => r.RuneId).ToList() ?? [],
            statShardsFallback
        );
    }

    private static bool TryGetRuneMetadata(
        int runeId,
        string normalizedPatch,
        IReadOnlyDictionary<RunePatchKey, RuneMetadata> runeMetadataByPatch,
        IReadOnlyDictionary<int, RuneMetadata> runeMetadataByRuneId,
        out RuneMetadata metadata)
    {
        if (runeMetadataByPatch.TryGetValue(new RunePatchKey(runeId, normalizedPatch), out metadata))
            return true;

        return runeMetadataByRuneId.TryGetValue(runeId, out metadata);
    }

    /// <summary>
    /// Internal record for rune metadata lookup.
    /// </summary>
    private readonly record struct RunePatchKey(int RuneId, string PatchVersion);

    private readonly record struct StoredRuneSelection(
        int RuneId,
        string? PatchVersion,
        RuneSelectionTree SelectionTree,
        int SelectionIndex,
        int StyleId);

    private readonly record struct RuneMetadata(int PathId, int Slot);
}
