using Microsoft.EntityFrameworkCore;
using Transcendence.Data;
using Transcendence.Service.Core.Queries;
using Transcendence.Service.Core.Services.Analytics.Interfaces;
using Transcendence.Service.Core.Services.Analytics.Models;

namespace Transcendence.Service.Core.Services.Analytics.Implementations;

/// <summary>
/// Raw computation service for champion matchups using EF Core aggregation. Win rates / tier lists, builds,
/// and the pro surfaces have been extracted to their own services (P10.1); this service owns matchups only.
/// </summary>
public partial class ChampionAnalyticsComputeService : IChampionAnalyticsComputeService
{
    private const int MinMatchupSampleSize = 30;
    private const int MatchupsToShow = 5;
    private readonly TranscendenceContext _context;

    public ChampionAnalyticsComputeService(TranscendenceContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Computes matchup data showing counters (bad matchups) and favorable matchups.
    /// Uses lane-specific self-join: same role, different team.
    /// </summary>
    public async Task<ChampionMatchupsResponse> ComputeMatchupsAsync(
        int championId,
        string role,
        string? rankTier,
        string? region,
        string patch,
        CancellationToken ct)
    {
        var rankTierScope = AnalyticsScopeMath.ParseRankTierScope(rankTier);
        const int minuteMark = 15;
        var normalizedRegion = AnalyticsRegionCatalog.NormalizeOrDefault(region);
        var regionFilter = AnalyticsRegionCatalog.NormalizeToFilter(region);

        var championQuery = _context.MatchParticipants
            .AsNoTracking()
            .Where(mp => mp.ChampionId == championId && mp.TeamPosition == role)
            .OnPatch(patch)
            .FromSuccessfulMatches()
            .InRankedSoloQueue();

        championQuery = championQuery.InPlatformRegion(regionFilter);

        // Apply rank tier filter if specified
        championQuery = AnalyticsScopeMath.ApplyRankTierScopeToParticipants(
            championQuery,
            rankTierScope,
            _context.Ranks.AsNoTracking());

        var lanePairsQuery = championQuery
            .Join(
                _context.MatchParticipants.AsNoTracking(),
                champion => champion.MatchId,
                opponent => opponent.MatchId,
                (champion, opponent) => new { Champion = champion, Opponent = opponent })
            .Where(x => x.Champion.TeamPosition == x.Opponent.TeamPosition && x.Champion.TeamId != x.Opponent.TeamId)
            .Select(x => new
            {
                x.Champion.MatchId,
                x.Champion.Win,
                OpponentChampionId = x.Opponent.ChampionId,
                ChampionParticipantId = x.Champion.ParticipantId,
                OpponentParticipantId = x.Opponent.ParticipantId
            });

        var timelineSnapshotQuery = _context.MatchParticipantTimelineSnapshots
            .AsNoTracking()
            .Where(s => s.MinuteMark == minuteMark);

        var matchupData = await (
                from pair in lanePairsQuery
                join championTimeline in timelineSnapshotQuery
                    on new { pair.MatchId, ParticipantId = pair.ChampionParticipantId }
                    equals new { championTimeline.MatchId, championTimeline.ParticipantId }
                    into championTimelineRows
                from championTimeline in championTimelineRows.DefaultIfEmpty()
                join opponentTimeline in timelineSnapshotQuery
                    on new { pair.MatchId, ParticipantId = pair.OpponentParticipantId }
                    equals new { opponentTimeline.MatchId, opponentTimeline.ParticipantId }
                    into opponentTimelineRows
                from opponentTimeline in opponentTimelineRows.DefaultIfEmpty()
                group new { pair, championTimeline, opponentTimeline } by pair.OpponentChampionId
                into g
                select new
                {
                    OpponentChampionId = g.Key,
                    Games = g.Count(),
                    Wins = g.Sum(x => x.pair.Win ? 1 : 0),
                    Losses = g.Sum(x => x.pair.Win ? 0 : 1),
                    TimelineGames = g.Count(x => x.championTimeline != null && x.opponentTimeline != null),
                    AvgGoldDiffAt15 = g
                        .Where(x => x.championTimeline != null && x.opponentTimeline != null)
                        .Select(x => (double?)(x.championTimeline!.Gold - x.opponentTimeline!.Gold))
                        .Average(),
                    AvgXpDiffAt15 = g
                        .Where(x => x.championTimeline != null && x.opponentTimeline != null)
                        .Select(x => (double?)(x.championTimeline!.Xp - x.opponentTimeline!.Xp))
                        .Average(),
                    LatestTimelineAtUtc = g
                        .Where(x => x.championTimeline != null)
                        .Select(x => (DateTime?)x.championTimeline!.DerivedAtUtc)
                        .Max()
                })
            .ToListAsync(ct);

        var aggregates = matchupData
            .Select(m => new MatchupAggregate(
                m.OpponentChampionId, m.Games, m.Wins, m.Losses, m.TimelineGames,
                m.AvgGoldDiffAt15, m.AvgXpDiffAt15, m.LatestTimelineAtUtc))
            .ToList();

        return BuildMatchupsResponse(championId, role, rankTierScope, normalizedRegion, patch, aggregates);
    }

    /// <summary>Per-opponent matchup aggregate — the shared shape both the raw self-join and the stats roll-up
    /// feed into <see cref="BuildMatchupsResponse"/>. <c>AvgGoldDiffAt15</c>/<c>AvgXpDiffAt15</c> are null when
    /// no both-present timeline pairs contributed.</summary>
    internal readonly record struct MatchupAggregate(
        int OpponentChampionId,
        int Games,
        int Wins,
        int Losses,
        int TimelineGames,
        double? AvgGoldDiffAt15,
        double? AvgXpDiffAt15,
        DateTime? LatestTimelineAtUtc);

    /// <summary>
    /// Shared post-aggregation for matchups (threshold + graceful degradation, counters/favorable selection,
    /// ordering, response assembly). Used by BOTH <see cref="ComputeMatchupsAsync"/> (raw self-join) and
    /// <see cref="ComputeMatchupsFromStatsAsync"/> (precomputed roll-up), so the two produce identical DTOs.
    /// Counters/favorable carry a <c>ThenBy(OpponentChampionId)</c> tie-break so the selected five are
    /// deterministic under equal win rates.
    /// </summary>
    private static ChampionMatchupsResponse BuildMatchupsResponse(
        int championId,
        string role,
        AnalyticsScopeMath.RankTierScope rankTierScope,
        string normalizedRegion,
        string patch,
        List<MatchupAggregate> matchupData)
    {
        var totalMatchupGames = matchupData.Sum(m => m.Games);
        var totalTimelineGames = matchupData.Sum(m => m.TimelineGames);
        var timelineCoverage = totalMatchupGames > 0
            ? (double)totalTimelineGames / totalMatchupGames
            : (double?)null;
        var timelineFreshness = matchupData
            .Where(x => x.LatestTimelineAtUtc.HasValue)
            .Select(x => x.LatestTimelineAtUtc)
            .Max();

        var effectiveMatchupSampleSize = AnalyticsScopeMath.ResolveEffectiveSampleSize(MinMatchupSampleSize, totalMatchupGames, floor: 2);

        static MatchupEntryDto ToEntry(MatchupAggregate m) => new()
        {
            OpponentChampionId = m.OpponentChampionId,
            Games = m.Games,
            Wins = m.Wins,
            Losses = m.Losses,
            WinRate = m.Games > 0 ? (double)m.Wins / m.Games : 0.0,
            AvgGoldDiffAt15 = m.AvgGoldDiffAt15,
            AvgXpDiffAt15 = m.AvgXpDiffAt15
        };

        var matchups = matchupData
            .Where(m => m.Games >= effectiveMatchupSampleSize)
            .Select(ToEntry)
            .ToList();

        if (matchups.Count == 0)
        {
            matchups = matchupData
                .Where(m => m.Games >= 1)
                .Select(ToEntry)
                .ToList();
        }

        var allMatchups = matchups
            .OrderByDescending(m => m.Games)
            .ThenByDescending(m => m.WinRate)
            .ThenBy(m => m.OpponentChampionId)
            .ToList();

        // Separate counters (low win rate) and favorable (high win rate). The ThenBy(OpponentChampionId)
        // tie-break makes the Take(5) deterministic when opponents share a win rate.
        var counters = matchups
            .Where(m => m.WinRate < 0.48)
            .OrderBy(m => m.WinRate)
            .ThenBy(m => m.OpponentChampionId)
            .Take(MatchupsToShow)
            .ToList();

        var favorable = matchups
            .Where(m => m.WinRate > 0.52)
            .OrderByDescending(m => m.WinRate)
            .ThenBy(m => m.OpponentChampionId)
            .Take(MatchupsToShow)
            .ToList();

        return new ChampionMatchupsResponse
        {
            ChampionId = championId,
            Role = role,
            RankTier = rankTierScope.CacheToken,
            Region = normalizedRegion,
            Patch = patch,
            Counters = counters,
            FavorableMatchups = favorable,
            AllMatchups = allMatchups,
            TimelineCoverageRatio = timelineCoverage,
            TimelineSampleSize = totalTimelineGames,
            TimelineDataFreshnessUtc = timelineFreshness
        };
    }
}
