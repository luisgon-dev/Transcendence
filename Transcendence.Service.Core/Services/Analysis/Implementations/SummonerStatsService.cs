using Microsoft.EntityFrameworkCore;
using Transcendence.Data;
using Transcendence.Service.Core.Analysis.Interfaces;
using Transcendence.Service.Core.Analysis.Models;
namespace Transcendence.Service.Core.Analysis.Implementations;

public class SummonerStatsService(TranscendenceContext db) : ISummonerStatsService
{
    public async Task<SummonerOverviewStats> GetSummonerOverviewAsync(Guid summonerId, int recentGamesCount, CancellationToken ct)
    {
        if (recentGamesCount <= 0) recentGamesCount = 20;

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
                mp.Match.Duration > 0 ? (mp.TotalMinionsKilled + mp.NeutralMinionsKilled) / (mp.Match.Duration / 60.0) : 0.0,
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
                g.Average(x => x.Match.Duration > 0 ? (x.TotalMinionsKilled + x.NeutralMinionsKilled) / (x.Match.Duration / 60.0) : 0.0),
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

    public async Task<PagedResult<RecentMatchSummary>> GetRecentMatchesAsync(Guid summonerId, int page, int pageSize, CancellationToken ct)
    {
        if (page <= 0) page = 1;
        if (pageSize <= 0 || pageSize > 100) pageSize = 20;

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
                mp.Match.Duration > 0 ? (mp.TotalMinionsKilled + mp.NeutralMinionsKilled) / (mp.Match.Duration / 60.0) : 0.0
            ))
            .ToListAsync(ct);

        return new PagedResult<RecentMatchSummary>(items, page, pageSize, total);
    }

    static double CalcKdaRatio(double kills, double deaths, double assists)
    {
        return (kills + assists) / Math.Max(1.0, deaths);
    }
}
