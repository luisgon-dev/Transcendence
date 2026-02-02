using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Transcendence.Data;
using Transcendence.Service.Core.Services.Analysis.Interfaces;
using Transcendence.Service.Core.Services.Analysis.Models;
using Transcendence.Service.Core.Services.RiotApi.DTOs;

namespace Transcendence.Service.Core.Services.Analysis.Implementations;

public class SummonerStatsService(TranscendenceContext db, HybridCache cache) : ISummonerStatsService
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

        var cacheKey = $"{OverviewCacheKeyPrefix}{summonerId}:{recentGamesCount}";
        return await cache.GetOrCreateAsync(
            cacheKey,
            async cancel => await ComputeOverviewAsync(summonerId, recentGamesCount, cancel),
            StatsCacheOptions,
            cancellationToken: ct);
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

        var cacheKey = $"{ChampionsCacheKeyPrefix}{summonerId}:{top}";
        return await cache.GetOrCreateAsync(
            cacheKey,
            async cancel => await ComputeChampionStatsAsync(summonerId, top, cancel),
            StatsCacheOptions,
            cancellationToken: ct);
    }

    private async Task<IReadOnlyList<ChampionStat>> ComputeChampionStatsAsync(Guid summonerId, int top,
        CancellationToken ct)
    {
        var list = await db.MatchParticipants
            .AsNoTracking()
            .Where(mp => mp.SummonerId == summonerId)
            .GroupBy(mp => new
            {
                mp.ChampionId
            })
            .Select(g => new ChampionStat(
                g.Key.ChampionId,
                g.Count(),
                g.Sum(x => x.Win ? 1 : 0),
                g.Sum(x => x.Win ? 0 : 1),
                g.Count() > 0 ? (double)g.Sum(x => x.Win ? 1 : 0) / g.Count() * 100.0 : 0.0,
                g.Average(x => (double)x.Kills),
                g.Average(x => (double)x.Deaths),
                g.Average(x => (double)x.Assists),
                0, // fill KDA after
                g.Average(x =>
                    x.Match.Duration > 0
                        ? (x.TotalMinionsKilled + x.NeutralMinionsKilled) / (x.Match.Duration / 60.0)
                        : 0.0),
                g.Average(x => (double)x.VisionScore),
                g.Average(x => (double)x.TotalDamageDealtToChampions)
            ))
            .OrderByDescending(x => x.Games)
            .Take(top)
            .ToListAsync(ct);

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
        var cacheKey = $"{RolesCacheKeyPrefix}{summonerId}";
        return await cache.GetOrCreateAsync(
            cacheKey,
            async cancel => await ComputeRoleBreakdownAsync(summonerId, cancel),
            StatsCacheOptions,
            cancellationToken: ct);
    }

    private async Task<IReadOnlyList<RoleStat>> ComputeRoleBreakdownAsync(Guid summonerId, CancellationToken ct)
    {
        var list = await db.MatchParticipants
            .AsNoTracking()
            .Where(mp => mp.SummonerId == summonerId)
            .GroupBy(mp => string.IsNullOrWhiteSpace(mp.TeamPosition) ? "UNKNOWN" : mp.TeamPosition!)
            .Select(g => new RoleStat(
                g.Key,
                g.Count(),
                g.Sum(x => x.Win ? 1 : 0),
                g.Sum(x => x.Win ? 0 : 1),
                g.Count() > 0 ? (double)g.Sum(x => x.Win ? 1 : 0) / g.Count() * 100.0 : 0.0
            ))
            .OrderByDescending(x => x.Games)
            .ToListAsync(ct);

        return list;
    }

    public async Task<PagedResult<RecentMatchSummary>> GetRecentMatchesAsync(Guid summonerId, int page, int pageSize,
        CancellationToken ct)
    {
        if (page <= 0) page = 1;
        if (pageSize <= 0 || pageSize > 100) pageSize = 20;

        var cacheKey = $"{RecentMatchesCacheKeyPrefix}{summonerId}:{page}:{pageSize}";
        return await cache.GetOrCreateAsync(
            cacheKey,
            async cancel => await ComputeRecentMatchesAsync(summonerId, page, pageSize, cancel),
            StatsCacheOptions,
            cancellationToken: ct);
    }

    private async Task<PagedResult<RecentMatchSummary>> ComputeRecentMatchesAsync(Guid summonerId, int page,
        int pageSize, CancellationToken ct)
    {
        var query = db.MatchParticipants
            .AsNoTracking()
            .Where(mp => mp.SummonerId == summonerId)
            .OrderByDescending(mp => mp.Match.MatchDate);

        var total = await query.CountAsync(ct);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(mp => new RecentMatchSummary(
                mp.Match.MatchId!,
                mp.Match.MatchDate,
                mp.Match.Duration,
                mp.Match.QueueType!,
                mp.Win,
                mp.ChampionId,
                mp.TeamPosition,
                mp.Kills,
                mp.Deaths,
                mp.Assists,
                mp.VisionScore,
                mp.TotalDamageDealtToChampions,
                mp.Match.Duration > 0
                    ? (mp.TotalMinionsKilled + mp.NeutralMinionsKilled) / (mp.Match.Duration / 60.0)
                    : 0.0,
                mp.SummonerSpell1Id,
                mp.SummonerSpell2Id,
                //TODO: Fix the below two lines to get actual rune data and items
                mp.Items.Select(i => i.ItemId).ToList(),
                new MatchRuneSummary
                (
                    1,
                    1,
                    1
                )
            ))
            .ToListAsync(ct);

        return new PagedResult<RecentMatchSummary>(items, page, pageSize, total);
    }

    public async Task<MatchDetailDto?> GetMatchDetailAsync(string matchId, CancellationToken ct)
    {
        var cacheKey = $"{MatchDetailCacheKeyPrefix}{matchId}";
        return await cache.GetOrCreateAsync(
            cacheKey,
            async cancel => await ComputeMatchDetailAsync(matchId, cancel),
            MatchDetailCacheOptions,
            cancellationToken: ct);
    }

    private async Task<MatchDetailDto?> ComputeMatchDetailAsync(string matchId, CancellationToken ct)
    {
        var match = await db.Matches
            .AsNoTracking()
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

        var runeMetadata = await db.RuneVersions
            .AsNoTracking()
            .Where(rv => runeIds.Contains(rv.RuneId) && rv.PatchVersion == match.Patch)
            .ToDictionaryAsync(rv => rv.RuneId, rv => new RuneMetadata(rv.RunePathId, rv.Slot), ct);

        var participants = match.Participants.Select(p => MapParticipant(p, runeMetadata)).ToList();

        return new MatchDetailDto(
            match.MatchId ?? string.Empty,
            match.MatchDate,
            match.Duration,
            match.QueueType ?? string.Empty,
            match.Patch,
            participants
        );
    }

    private static ParticipantDetailDto MapParticipant(
        Data.Models.LoL.Match.MatchParticipant p,
        Dictionary<int, RuneMetadata> runeMetadata)
    {
        var items = p.Items.Select(i => i.ItemId).ToList();

        // Build runes structure from stored rune IDs and metadata
        var runes = BuildRunesDto(p.Runes.Select(r => r.RuneId).ToList(), runeMetadata);

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
        List<int> runeIds,
        Dictionary<int, RuneMetadata> runeMetadata)
    {
        // Group runes by their path (style)
        var runesByPath = new Dictionary<int, List<(int RuneId, int Slot)>>();

        foreach (var runeId in runeIds)
        {
            if (runeMetadata.TryGetValue(runeId, out var meta))
            {
                var pathId = meta.PathId;
                var slot = meta.Slot;

                if (!runesByPath.ContainsKey(pathId))
                    runesByPath[pathId] = [];

                runesByPath[pathId].Add((runeId, slot));
            }
        }

        // Primary style has 4 runes (slots 0-3), sub style has 2 runes
        // Stat shards are in a separate path (typically path 5xxx)
        var primaryStyleId = 0;
        var subStyleId = 0;
        var primarySelections = new List<int>();
        var subSelections = new List<int>();
        var statShards = new List<int>();

        foreach (var (pathId, runes) in runesByPath)
        {
            // Stat shard paths are 5xxx range
            if (pathId >= 5000)
            {
                statShards.AddRange(runes.OrderBy(r => r.Slot).Select(r => r.RuneId));
            }
            else if (runes.Count >= 3) // Primary style has 4 runes (keystone + 3)
            {
                primaryStyleId = pathId;
                primarySelections = runes.OrderBy(r => r.Slot).Select(r => r.RuneId).ToList();
            }
            else // Sub style has 2 runes
            {
                subStyleId = pathId;
                subSelections = runes.OrderBy(r => r.Slot).Select(r => r.RuneId).ToList();
            }
        }

        return new ParticipantRunesDto(
            primaryStyleId,
            subStyleId,
            primarySelections,
            subSelections,
            statShards
        );
    }

    private static double CalcKdaRatio(double kills, double deaths, double assists)
    {
        return (kills + assists) / Math.Max(1.0, deaths);
    }

    /// <summary>
    /// Internal record for rune metadata lookup.
    /// </summary>
    private record RuneMetadata(int PathId, int Slot);
}