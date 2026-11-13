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

public class SummonerStatsService(
    TranscendenceContext db,
    HybridCache cache)
    : ISummonerStatsService
{
    // Cache key prefixes
    private const string OverviewCacheKeyPrefix = "stats:overview:";
    private const string ChampionsCacheKeyPrefix = "stats:champions:";
    private const string RolesCacheKeyPrefix = "stats:roles:";
    private const string RankHistoryCacheKeyPrefix = "stats:rank-history:";
    private const string ActiveSeasonCacheKeyPrefix = "stats:active-season:";
    private const string ActiveSeasonProfileCacheKeyPrefix = "stats:active-season-profile:";
    private const string PlayedWithCacheKeyPrefix = "stats:played-with:";
    private const string MasteryCacheKeyPrefix = "stats:mastery:";
    private const string SummonerStatsCacheTagPrefix = "summoner-stats:";

    // Stats cache options: 5min total, 2min L1 (stats change on refresh)
    private static readonly HybridCacheEntryOptions StatsCacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(2)
    };

    // Season configuration changes rarely. A time-bucketed key removes the otherwise unconditional
    // RankedSeasons query while naturally rolling over at season boundaries or after config edits.
    private static readonly TimeSpan ActiveSeasonCacheBucket = TimeSpan.FromMinutes(5);
    private static readonly HybridCacheEntryOptions ActiveSeasonCacheOptions = new()
    {
        Expiration = ActiveSeasonCacheBucket,
        LocalCacheExpiration = TimeSpan.FromMinutes(2)
    };

    public async Task<SummonerOverviewStats> GetSummonerOverviewAsync(Guid summonerId, int recentGamesCount,
        CancellationToken ct)
    {
        if (recentGamesCount <= 0) recentGamesCount = 20;
        var cacheKey = $"{OverviewCacheKeyPrefix}{summonerId}:{recentGamesCount}";
        return await ExecuteStatsRequestAsync(
            "Failed to compute overview stats.",
            async token => await cache.GetOrCreateAsync(
                cacheKey,
                async cancel => await ComputeOverviewAsync(summonerId, recentGamesCount, cancel),
                StatsCacheOptions,
                tags: new[] { BuildSummonerStatsTag(summonerId) },
                cancellationToken: token),
            ct);
    }

    private async Task<SummonerOverviewStats> ComputeOverviewAsync(Guid summonerId, int recentGamesCount,
        CancellationToken ct) =>
        await ComputeOverviewFromParticipantsAsync(
            summonerId,
            recentGamesCount,
            startMatchDateMs: null,
            endMatchDateMs: null,
            ct);

    public async Task<IReadOnlyList<ChampionStat>> GetChampionStatsAsync(Guid summonerId, int top, CancellationToken ct)
    {
        if (top <= 0) top = 10;
        var cacheKey = $"{ChampionsCacheKeyPrefix}{summonerId}:{top}";
        return await ExecuteStatsRequestAsync(
            "Failed to compute champion stats.",
            async token => await cache.GetOrCreateAsync(
                cacheKey,
                async cancel => await ComputeChampionStatsAsync(summonerId, top, cancel),
                StatsCacheOptions,
                tags: new[] { BuildSummonerStatsTag(summonerId) },
                cancellationToken: token),
            ct);
    }

    private async Task<IReadOnlyList<ChampionStat>> ComputeChampionStatsAsync(Guid summonerId, int top,
        CancellationToken ct) =>
        await ComputeChampionStatsFromParticipantsAsync(
            summonerId,
            top,
            startMatchDateMs: null,
            endMatchDateMs: null,
            ct);

    public async Task<SummonerSeasonProfileStats> GetActiveSeasonProfileStatsAsync(
        Guid summonerId,
        int topChampions,
        int recentGamesCount,
        CancellationToken ct)
    {
        if (topChampions <= 0) topChampions = 5;
        if (recentGamesCount <= 0) recentGamesCount = 20;

        var season = await ResolveActiveSeasonAsync(DateTime.UtcNow, ct);
        var cacheKey = $"{ActiveSeasonProfileCacheKeyPrefix}{summonerId}:{season.SeasonKey}:{topChampions}:{recentGamesCount}";
        return await ExecuteStatsRequestAsync(
            "Failed to compute active-season profile stats.",
            async token => await cache.GetOrCreateAsync(
                cacheKey,
                async cancel => await ComputeActiveSeasonProfileStatsAsync(
                    summonerId,
                    season,
                    topChampions,
                    recentGamesCount,
                    cancel),
                StatsCacheOptions,
                tags: new[] { BuildSummonerStatsTag(summonerId) },
                cancellationToken: token),
            ct);
    }

    internal async Task<RankedSeasonWindow> ResolveActiveSeasonAsync(DateTime nowUtc, CancellationToken ct)
    {
        var bucket = nowUtc.Ticks / ActiveSeasonCacheBucket.Ticks;
        return await cache.GetOrCreateAsync(
            $"{ActiveSeasonCacheKeyPrefix}{bucket}",
            async cancel => await RankedSeasonResolver.GetActiveSeasonAsync(db, nowUtc, cancel),
            ActiveSeasonCacheOptions,
            cancellationToken: ct);
    }

    private async Task<SummonerSeasonProfileStats> ComputeActiveSeasonProfileStatsAsync(
        Guid summonerId,
        RankedSeasonWindow season,
        int topChampions,
        int recentGamesCount,
        CancellationToken ct)
    {
        const string queueScope = QueueCatalog.QueueFamilyRankedSoloDuo;
        var overviewRow = await db.SummonerSeasonOverviewStats
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.SummonerId == summonerId &&
                                      x.SeasonKey == season.SeasonKey &&
                                      x.QueueScope == queueScope, ct);

        SummonerOverviewStats overview;
        IReadOnlyList<ChampionStat> champions;
        if (overviewRow != null)
        {
            var recent = await db.SummonerMatchFacts
                .AsNoTracking()
                .Where(f => f.SummonerId == summonerId &&
                            f.SeasonKey == season.SeasonKey &&
                            f.QueueId == QueueCatalog.RankedSoloDuoQueueId &&
                            f.CountsTowardRankedTotal)
                .OrderByDescending(f => f.MatchDate)
                .ThenByDescending(f => f.MatchId)
                .Select(f => new RecentPerformancePoint(
                    f.MatchId,
                    f.Win,
                    f.Kills,
                    f.Deaths,
                    f.Assists,
                    f.DurationSeconds > 0
                        ? (f.TotalMinionsKilled + f.NeutralMinionsKilled) / (f.DurationSeconds / 60.0)
                        : 0.0,
                    f.VisionScore,
                    f.TotalDamageDealtToChampions))
                .Take(recentGamesCount)
                .ToListAsync(ct);

            overview = BuildOverviewFromAggregate(summonerId, overviewRow, recent);
            champions = await LoadChampionStatsFromSeasonAggregatesAsync(
                summonerId,
                season.SeasonKey,
                queueScope,
                topChampions,
                ct);
        }
        else
        {
            var startMs = new DateTimeOffset(season.StartUtc).ToUnixTimeMilliseconds();
            var endMs = season.EndUtc.HasValue
                ? new DateTimeOffset(season.EndUtc.Value).ToUnixTimeMilliseconds()
                : (long?)null;

            overview = await ComputeOverviewFromParticipantsAsync(summonerId, recentGamesCount, startMs, endMs, ct);
            champions = await ComputeChampionStatsFromParticipantsAsync(summonerId, topChampions, startMs, endMs, ct);
        }

        var historyStatus = await LoadFullHistoryProfileStatusAsync(
            summonerId,
            season.SeasonKey,
            queueScope,
            ct);

        return new SummonerSeasonProfileStats(
            season.SeasonKey,
            season.DisplayName,
            queueScope,
            overview,
            champions,
            historyStatus);
    }

    private async Task<SummonerOverviewStats> ComputeOverviewFromParticipantsAsync(
        Guid summonerId,
        int recentGamesCount,
        long? startMatchDateMs,
        long? endMatchDateMs,
        CancellationToken ct)
    {
        var baseQuery = db.MatchParticipants
            .AsNoTracking()
            .Where(mp => mp.SummonerId == summonerId)
            .InRankedSoloQueue();

        if (startMatchDateMs.HasValue)
            baseQuery = baseQuery.Where(mp => mp.Match.MatchDate >= startMatchDateMs.Value);
        if (endMatchDateMs.HasValue)
            baseQuery = baseQuery.Where(mp => mp.Match.MatchDate < endMatchDateMs.Value);

        var aggregate = await baseQuery
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
            })
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
            .SingleOrDefaultAsync(ct);

        var recent = await baseQuery
            .OrderByDescending(mp => mp.Match.MatchDate)
            .ThenByDescending(mp => mp.Match.MatchId)
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
                mp.TotalDamageDealtToChampions))
            .Take(recentGamesCount)
            .ToListAsync(ct);

        var total = aggregate?.Total ?? 0;
        var wins = aggregate?.Wins ?? 0;
        var losses = aggregate?.Losses ?? 0;

        return new SummonerOverviewStats(
            summonerId,
            total,
            wins,
            losses,
            total > 0 ? (double)wins / total * 100.0 : 0.0,
            aggregate?.AvgKills ?? 0,
            aggregate?.AvgDeaths ?? 0,
            aggregate?.AvgAssists ?? 0,
            CalcKdaRatio(aggregate?.AvgKills ?? 0, aggregate?.AvgDeaths ?? 0, aggregate?.AvgAssists ?? 0),
            aggregate?.AvgCsPerMin ?? 0,
            aggregate?.AvgVision ?? 0,
            aggregate?.AvgDamage ?? 0,
            aggregate?.AvgDurationMin ?? 0,
            recent);
    }

    private async Task<IReadOnlyList<ChampionStat>> ComputeChampionStatsFromParticipantsAsync(
        Guid summonerId,
        int top,
        long? startMatchDateMs,
        long? endMatchDateMs,
        CancellationToken ct)
    {
        var query = db.MatchParticipants
            .AsNoTracking()
            .Where(mp => mp.SummonerId == summonerId)
            .InRankedSoloQueue();

        if (startMatchDateMs.HasValue)
            query = query.Where(mp => mp.Match.MatchDate >= startMatchDateMs.Value);
        if (endMatchDateMs.HasValue)
            query = query.Where(mp => mp.Match.MatchDate < endMatchDateMs.Value);

        var rows = await query
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
            .GroupBy(x => x.ChampionId)
            .Select(g => new
            {
                ChampionId = g.Key,
                Games = g.Count(),
                Wins = g.Sum(x => x.Win ? 1 : 0),
                AvgKills = g.Average(x => (double)x.Kills),
                AvgDeaths = g.Average(x => (double)x.Deaths),
                AvgAssists = g.Average(x => (double)x.Assists),
                AvgCsPerMin = g.Average(x => x.MatchDuration > 0
                    ? x.Cs / (x.MatchDuration / 60.0)
                    : 0.0),
                AvgVision = g.Average(x => (double)x.VisionScore),
                AvgDamage = g.Average(x => (double)x.TotalDamageDealtToChampions)
            })
            .OrderByDescending(x => x.Games)
            .Take(top)
            .ToListAsync(ct);

        return rows
            .Select(x => new ChampionStat(
                x.ChampionId,
                x.Games,
                x.Wins,
                x.Games - x.Wins,
                x.Games > 0 ? (double)x.Wins / x.Games * 100.0 : 0.0,
                x.AvgKills,
                x.AvgDeaths,
                x.AvgAssists,
                CalcKdaRatio(x.AvgKills, x.AvgDeaths, x.AvgAssists),
                x.AvgCsPerMin,
                x.AvgVision,
                x.AvgDamage))
            .ToList();
    }

    private static SummonerOverviewStats BuildOverviewFromAggregate(
        Guid summonerId,
        SummonerSeasonOverviewStat row,
        IReadOnlyList<RecentPerformancePoint> recent)
    {
        var total = Math.Max(0, row.TotalMatches);
        var avgKills = Average(row.TotalKills, total);
        var avgDeaths = Average(row.TotalDeaths, total);
        var avgAssists = Average(row.TotalAssists, total);

        return new SummonerOverviewStats(
            summonerId,
            total,
            row.Wins,
            row.Losses,
            total > 0 ? (double)row.Wins / total * 100.0 : 0.0,
            avgKills,
            avgDeaths,
            avgAssists,
            CalcKdaRatio(avgKills, avgDeaths, avgAssists),
            row.TotalDurationSeconds > 0 ? row.TotalCs / (row.TotalDurationSeconds / 60.0) : 0.0,
            Average(row.TotalVisionScore, total),
            Average(row.TotalDamageToChamps, total),
            total > 0 ? row.TotalDurationSeconds / 60.0 / total : 0.0,
            recent);
    }

    private async Task<IReadOnlyList<ChampionStat>> LoadChampionStatsFromSeasonAggregatesAsync(
        Guid summonerId,
        string seasonKey,
        string queueScope,
        int top,
        CancellationToken ct)
    {
        var rows = await db.SummonerSeasonChampionStats
            .AsNoTracking()
            .Where(x => x.SummonerId == summonerId && x.SeasonKey == seasonKey && x.QueueScope == queueScope)
            .OrderByDescending(x => x.Games)
            .ThenByDescending(x => x.Wins)
            .Take(top)
            .ToListAsync(ct);

        return rows.Select(row =>
        {
            var avgKills = Average(row.TotalKills, row.Games);
            var avgDeaths = Average(row.TotalDeaths, row.Games);
            var avgAssists = Average(row.TotalAssists, row.Games);
            return new ChampionStat(
                row.ChampionId,
                row.Games,
                row.Wins,
                row.Losses,
                row.Games > 0 ? (double)row.Wins / row.Games * 100.0 : 0.0,
                avgKills,
                avgDeaths,
                avgAssists,
                CalcKdaRatio(avgKills, avgDeaths, avgAssists),
                row.TotalDurationSeconds > 0 ? row.TotalCs / (row.TotalDurationSeconds / 60.0) : 0.0,
                Average(row.TotalVisionScore, row.Games),
                Average(row.TotalDamageToChamps, row.Games));
        }).ToList();
    }

    private async Task<SummonerFullHistoryProfileStatus?> LoadFullHistoryProfileStatusAsync(
        Guid summonerId,
        string seasonKey,
        string queueScope,
        CancellationToken ct)
    {
        var backfill = await db.SummonerFullHistoryBackfills
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.SummonerId == summonerId &&
                                      x.Scope == SummonerFullHistoryScopes.FullHistory, ct);
        var coverage = await db.SummonerSeasonCoverages
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.SummonerId == summonerId &&
                                      x.SeasonKey == seasonKey &&
                                      x.QueueScope == queueScope, ct);

        if (backfill == null && coverage == null)
            return null;

        return new SummonerFullHistoryProfileStatus(
            coverage?.BackfillStatus ?? backfill?.Status ?? SummonerFullHistoryBackfillStatuses.Queued,
            backfill?.RequestedAtUtc ?? DateTime.MinValue,
            backfill?.StartedAtUtc,
            backfill?.CompletedAtUtc,
            coverage?.UpdatedAtUtc ?? backfill?.UpdatedAtUtc ?? DateTime.MinValue,
            backfill?.PagesScanned ?? 0,
            backfill?.MatchIdsDiscovered ?? 0,
            backfill?.FactsPersisted ?? 0,
            backfill?.DetailFetchFailures ?? 0,
            coverage?.CompletedMatchCount ?? 0,
            coverage?.RiotWins,
            coverage?.RiotLosses,
            coverage?.RiotTotal,
            coverage?.RankedCountDelta,
            coverage?.CoverageStatus,
            coverage?.ClassifierVersion ?? RankedMatchCountClassifier.Version);
    }

    private static double Average(long total, int count)
    {
        return count > 0 ? (double)total / count : 0.0;
    }

    public async Task<IReadOnlyList<RoleStat>> GetRoleBreakdownAsync(Guid summonerId, CancellationToken ct)
    {
        var cacheKey = $"{RolesCacheKeyPrefix}{summonerId}";
        return await ExecuteStatsRequestAsync(
            "Failed to compute role breakdown.",
            async token => await cache.GetOrCreateAsync(
                cacheKey,
                async cancel => await ComputeRoleBreakdownAsync(summonerId, cancel),
                StatsCacheOptions,
                tags: new[] { BuildSummonerStatsTag(summonerId) },
                cancellationToken: token),
            ct);
    }

    private async Task<IReadOnlyList<RoleStat>> ComputeRoleBreakdownAsync(Guid summonerId, CancellationToken ct)
    {
        // Aggregate raw TeamPosition server-side (GROUP BY TeamPosition); normalization/merge
        // ("top" → "TOP", null → "UNKNOWN") happens in C# on the small grouped result set.
        var rawRows = await db.MatchParticipants
            .AsNoTracking()
            .Where(mp => mp.SummonerId == summonerId)
            .InRankedSoloQueue()
            .GroupBy(mp => mp.TeamPosition)
            .Select(g => new
            {
                TeamPosition = g.Key,
                Games = g.Count(),
                Wins = g.Sum(x => x.Win ? 1 : 0)
            })
            .ToListAsync(ct);

        var list = rawRows
            .GroupBy(row => NormalizeTeamPosition(row.TeamPosition))
            .Select(g =>
            {
                var games = g.Sum(x => x.Games);
                var wins = g.Sum(x => x.Wins);
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

        return list;
    }

    public async Task<IReadOnlyList<RankHistoryEntry>> GetRankHistoryAsync(Guid summonerId, string? queueType,
        CancellationToken ct)
    {
        var normalizedQueue = string.IsNullOrWhiteSpace(queueType) ? null : queueType.Trim();
        var cacheKey = $"{RankHistoryCacheKeyPrefix}{summonerId}:{normalizedQueue ?? "-"}";
        return await ExecuteStatsRequestAsync(
            "Failed to load rank history.",
            async token => await cache.GetOrCreateAsync(
                cacheKey,
                async cancel => await ComputeRankHistoryAsync(summonerId, normalizedQueue, cancel),
                StatsCacheOptions,
                tags: new[] { BuildSummonerStatsTag(summonerId) },
                cancellationToken: token),
            ct);
    }

    private async Task<IReadOnlyList<RankHistoryEntry>> ComputeRankHistoryAsync(Guid summonerId, string? queueType,
        CancellationToken ct)
    {
        // HistoricalRank uses a shadow FK "SummonerId" (indexed) — query it directly to avoid a Summoner join.
        var query = db.HistoricalRanks
            .AsNoTracking()
            .Where(hr => EF.Property<Guid?>(hr, "SummonerId") == summonerId);

        if (queueType != null)
            query = query.Where(hr => hr.QueueType == queueType);

        return await query
            .OrderBy(hr => hr.DateRecorded)
            .ThenBy(hr => hr.Id)
            .Select(hr => new RankHistoryEntry(
                hr.QueueType,
                hr.Tier,
                hr.RankNumber,
                hr.LeaguePoints,
                hr.Wins,
                hr.Losses,
                hr.DateRecorded))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<PlayedWithEntry>> GetPlayedWithAsync(Guid summonerId, int recentMatches,
        int topCount, CancellationToken ct)
    {
        recentMatches = Math.Clamp(recentMatches <= 0 ? 100 : recentMatches, 1, 100);
        topCount = Math.Clamp(topCount <= 0 ? 6 : topCount, 1, 10);
        var cacheKey = $"{PlayedWithCacheKeyPrefix}{summonerId}:{recentMatches}:{topCount}";
        return await ExecuteStatsRequestAsync(
            "Failed to compute recently-played-with.",
            async token => await cache.GetOrCreateAsync(
                cacheKey,
                async cancel => await ComputePlayedWithAsync(summonerId, recentMatches, topCount, cancel),
                StatsCacheOptions,
                tags: new[] { BuildSummonerStatsTag(summonerId) },
                cancellationToken: token),
            ct);
    }

    private async Task<IReadOnlyList<PlayedWithEntry>> ComputePlayedWithAsync(Guid summonerId, int recentMatches,
        int topCount, CancellationToken ct)
    {
        // Step 1: anchor on the summoner's own participation in their most recent matches
        // (carries my team + my result per match). Bounded by recentMatches.
        var anchor = await db.MatchParticipants
            .AsNoTracking()
            .Where(mp => mp.SummonerId == summonerId)
            .OrderByDescending(mp => mp.Match.MatchDate)
            .Select(mp => new { mp.MatchId, mp.TeamId, mp.Win })
            .Take(recentMatches)
            .ToListAsync(ct);

        if (anchor.Count == 0)
            return [];

        var anchorByMatch = anchor
            .GroupBy(a => a.MatchId)
            .ToDictionary(g => g.Key, g => g.First());
        var matchIds = anchorByMatch.Keys.ToList();

        // Step 2: co-participants in those matches (excluding self), via the indexed MatchId IN (...).
        // Their rows share heap pages with the anchor rows just read. Names are deliberately not
        // joined here: up to 900 co-participants each cost a Summoners index + heap read, which
        // was most of this method's disk reads on prod, while only the top few are shown.
        var coRows = await db.MatchParticipants
            .AsNoTracking()
            .Where(mp => matchIds.Contains(mp.MatchId) && mp.SummonerId != summonerId)
            .Select(mp => new { mp.MatchId, mp.SummonerId, mp.TeamId })
            .ToListAsync(ct);

        var top = coRows
            .GroupBy(r => r.SummonerId)
            .Select(g =>
            {
                var sameTeamGames = 0;
                var sameTeamWins = 0;
                foreach (var row in g)
                {
                    if (!anchorByMatch.TryGetValue(row.MatchId, out var me))
                        continue;
                    if (row.TeamId == me.TeamId)
                    {
                        sameTeamGames++;
                        if (me.Win) sameTeamWins++;
                    }
                }

                return (SummonerId: g.Key, GamesTogether: g.Count(), SameTeamGames: sameTeamGames,
                    SameTeamWins: sameTeamWins);
            })
            .Where(e => e.GamesTogether >= 2)
            .OrderByDescending(e => e.GamesTogether)
            .ThenByDescending(e => e.SameTeamGames)
            .Take(topCount)
            .ToList();

        if (top.Count == 0)
            return [];

        // Step 3: names for the shown partners only.
        var topIds = top.Select(e => e.SummonerId).ToList();
        var names = await db.Summoners
            .AsNoTracking()
            .Where(s => topIds.Contains(s.Id))
            .Select(s => new { s.Id, s.GameName, s.TagLine })
            .ToDictionaryAsync(s => s.Id, ct);

        return top
            .Select(e =>
            {
                var name = names.GetValueOrDefault(e.SummonerId);
                return new PlayedWithEntry(e.SummonerId, name?.GameName, name?.TagLine, e.GamesTogether,
                    e.SameTeamGames, e.SameTeamWins);
            })
            .ToList();
    }

    public async Task<IReadOnlyList<ChampionMasteryEntry>> GetTopMasteryAsync(Guid summonerId, int top,
        CancellationToken ct)
    {
        top = Math.Clamp(top <= 0 ? 6 : top, 1, 20);
        var cacheKey = $"{MasteryCacheKeyPrefix}{summonerId}:{top}";
        return await ExecuteStatsRequestAsync(
            "Failed to load champion mastery.",
            async token => await cache.GetOrCreateAsync(
                cacheKey,
                async cancel => await ComputeTopMasteryAsync(summonerId, top, cancel),
                StatsCacheOptions,
                tags: new[] { BuildSummonerStatsTag(summonerId) },
                cancellationToken: token),
            ct);
    }

    private async Task<IReadOnlyList<ChampionMasteryEntry>> ComputeTopMasteryAsync(Guid summonerId, int top,
        CancellationToken ct)
    {
        return await db.ChampionMasteries
            .AsNoTracking()
            .Where(cm => cm.SummonerId == summonerId)
            .OrderByDescending(cm => cm.ChampionPoints)
            .Take(top)
            .Select(cm => new ChampionMasteryEntry(
                cm.ChampionId,
                cm.ChampionLevel,
                cm.ChampionPoints,
                cm.LastPlayTime,
                cm.ChestGranted,
                cm.TokensEarned))
            .ToListAsync(ct);
    }

    private static double CalcKdaRatio(double kills, double deaths, double assists) =>
        (kills + assists) / Math.Max(1.0, deaths);

    private static string BuildSummonerStatsTag(Guid summonerId) => $"{SummonerStatsCacheTagPrefix}{summonerId}";

    private static string NormalizeTeamPosition(string? teamPosition)
    {
        if (string.IsNullOrWhiteSpace(teamPosition))
            return "UNKNOWN";

        return teamPosition.Trim().ToUpperInvariant();
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
