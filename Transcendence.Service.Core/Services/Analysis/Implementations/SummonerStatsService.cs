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
using RuneSelectionTree = Transcendence.Data.Models.LoL.Match.RuneSelectionTree;

namespace Transcendence.Service.Core.Services.Analysis.Implementations;

public class SummonerStatsService(
    TranscendenceContext db,
    HybridCache cache,
    ILogger<SummonerStatsService> logger)
    : ISummonerStatsService
{
    // Cache key prefixes
    private const string OverviewCacheKeyPrefix = "stats:overview:";
    private const string ChampionsCacheKeyPrefix = "stats:champions:";
    private const string RolesCacheKeyPrefix = "stats:roles:";
    private const string RankHistoryCacheKeyPrefix = "stats:rank-history:";
    private const string ActiveSeasonProfileCacheKeyPrefix = "stats:active-season-profile:";
    private const string PlayedWithCacheKeyPrefix = "stats:played-with:";
    private const string MasteryCacheKeyPrefix = "stats:mastery:";
    private const string RecentMatchesCacheKeyPrefix = "stats:recent:";
    private const string MatchDetailCacheKeyPrefix = "match:detail:";
    private const string MatchTimelineCacheKeyPrefix = "match:timeline:";
    private const string SummonerStatsCacheTagPrefix = "summoner-stats:";

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
        CancellationToken ct)
    {
        var baseQuery = db.MatchParticipants
            .AsNoTracking()
            .Where(mp => mp.SummonerId == summonerId)
            .InRankedSoloQueue()
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
            .SingleOrDefaultAsync(ct);

        var total = aggregate?.Total ?? 0;
        var wins = aggregate?.Wins ?? 0;
        var losses = aggregate?.Losses ?? 0;
        var wr = total > 0 ? (double)wins / total * 100.0 : 0.0;

        var recent = await db.MatchParticipants
            .AsNoTracking()
            .Where(mp => mp.SummonerId == summonerId)
            .InRankedSoloQueue()
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
        CancellationToken ct)
    {
        var games = await db.MatchParticipants
            .AsNoTracking()
            .Where(mp => mp.SummonerId == summonerId)
            .InRankedSoloQueue()
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
                g.Average(x => x.MatchDuration > 0
                    ? x.Cs / (x.MatchDuration / 60.0)
                    : 0.0),
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

    public async Task<SummonerSeasonProfileStats> GetActiveSeasonProfileStatsAsync(
        Guid summonerId,
        int topChampions,
        int recentGamesCount,
        CancellationToken ct)
    {
        if (topChampions <= 0) topChampions = 5;
        if (recentGamesCount <= 0) recentGamesCount = 20;

        var season = await RankedSeasonResolver.GetActiveSeasonAsync(db, DateTime.UtcNow, ct);
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
        long startMatchDateMs,
        long? endMatchDateMs,
        CancellationToken ct)
    {
        var baseQuery = db.MatchParticipants
            .AsNoTracking()
            .Where(mp => mp.SummonerId == summonerId)
            .InRankedSoloQueue()
            .Where(mp => mp.Match.MatchDate >= startMatchDateMs);

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
        long startMatchDateMs,
        long? endMatchDateMs,
        CancellationToken ct)
    {
        var query = db.MatchParticipants
            .AsNoTracking()
            .Where(mp => mp.SummonerId == summonerId)
            .InRankedSoloQueue()
            .Where(mp => mp.Match.MatchDate >= startMatchDateMs);

        if (endMatchDateMs.HasValue)
            query = query.Where(mp => mp.Match.MatchDate < endMatchDateMs.Value);

        var games = await query
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

        return games
            .GroupBy(x => x.ChampionId)
            .Select(g =>
            {
                var count = g.Count();
                var wins = g.Sum(x => x.Win ? 1 : 0);
                var avgKills = g.Average(x => (double)x.Kills);
                var avgDeaths = g.Average(x => (double)x.Deaths);
                var avgAssists = g.Average(x => (double)x.Assists);
                return new ChampionStat(
                    g.Key,
                    count,
                    wins,
                    count - wins,
                    count > 0 ? (double)wins / count * 100.0 : 0.0,
                    avgKills,
                    avgDeaths,
                    avgAssists,
                    CalcKdaRatio(avgKills, avgDeaths, avgAssists),
                    g.Average(x => x.MatchDuration > 0 ? x.Cs / (x.MatchDuration / 60.0) : 0.0),
                    g.Average(x => (double)x.VisionScore),
                    g.Average(x => (double)x.TotalDamageDealtToChampions));
            })
            .OrderByDescending(x => x.Games)
            .Take(top)
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
        var rows = await db.MatchParticipants
            .AsNoTracking()
            .Where(mp => mp.SummonerId == summonerId)
            .InRankedSoloQueue()
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
        var coRows = await db.MatchParticipants
            .AsNoTracking()
            .Where(mp => matchIds.Contains(mp.MatchId) && mp.SummonerId != summonerId)
            .Select(mp => new
            {
                mp.MatchId,
                mp.SummonerId,
                mp.TeamId,
                mp.Summoner.GameName,
                mp.Summoner.TagLine
            })
            .ToListAsync(ct);

        return coRows
            .GroupBy(r => r.SummonerId)
            .Select(g =>
            {
                var first = g.First();
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

                return new PlayedWithEntry(g.Key, first.GameName, first.TagLine, g.Count(), sameTeamGames, sameTeamWins);
            })
            .Where(e => e.GamesTogether >= 2)
            .OrderByDescending(e => e.GamesTogether)
            .ThenByDescending(e => e.SameTeamGames)
            .Take(topCount)
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

    public async Task<PagedResult<RecentMatchSummary>> GetRecentMatchesAsync(
        Guid summonerId,
        int page,
        int pageSize,
        string? queueFamily,
        IReadOnlyCollection<int>? queueIds,
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
            $"{RecentMatchesCacheKeyPrefix}{summonerId}:{page}:{pageSize}:{normalizedFamily}:{queueIdsCacheKey}";
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
                    cancel),
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
        CancellationToken ct)
    {
        var query = ApplyRecentMatchFilters(
                db.MatchParticipants
                    .AsNoTracking()
                    .Where(mp => mp.SummonerId == summonerId),
                queueFamily,
                queueIds)
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
            return new PagedResult<RecentMatchSummary>([], page, pageSize, total);

        // Get items and runes for these participants
        var participantIds = participantData.Select(p => p.ParticipantId).Distinct().ToList();

        var itemsByParticipant = await db.Set<Data.Models.LoL.Match.MatchParticipantItem>()
            .AsNoTracking()
            .Where(i => participantIds.Contains(i.MatchParticipantId))
            .GroupBy(i => i.MatchParticipantId)
            .Select(g => new
            {
                ParticipantId = g.Key,
                Items = g.OrderBy(i => i.SlotIndex).Select(i => i.ItemId).ToList()
            })
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
                runeDetail
            );
        }).ToList();

        return new PagedResult<RecentMatchSummary>(items, page, pageSize, total);
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
            async token => await cache.GetOrCreateAsync(
                cacheKey,
                async cancel => await ComputeMatchTimelineAsync(matchId, cancel),
                MatchDetailCacheOptions,
                cancellationToken: token),
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
            where s.MatchId == match.Id
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
                    return new RuneMetadata(first.RunePathId, first.Slot);
                });

        var participants = match.Participants.Select(p => MapParticipant(p, runeMetadata)).ToList();

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
        Dictionary<int, RuneMetadata> runeMetadata)
    {
        var items = p.Items
            .OrderBy(i => i.SlotIndex)
            .Select(i => i.ItemId)
            .ToList();

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
            p.PhysicalDamageDealtToChampions,
            p.MagicDamageDealtToChampions,
            p.TrueDamageDealtToChampions,
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

    private static string BuildSummonerStatsTag(Guid summonerId) => $"{SummonerStatsCacheTagPrefix}{summonerId}";

    private static string NormalizeTeamPosition(string? teamPosition)
    {
        if (string.IsNullOrWhiteSpace(teamPosition))
            return "UNKNOWN";

        return teamPosition.Trim().ToUpperInvariant();
    }

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
        IReadOnlyList<int> queueIds)
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

        return query;
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
