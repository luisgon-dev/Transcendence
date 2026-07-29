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
