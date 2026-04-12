using Microsoft.EntityFrameworkCore;
using Transcendence.Data;
using Transcendence.Service.Core.Services.Tft.Interfaces;
using Transcendence.Service.Core.Services.Tft.Models;

namespace Transcendence.Service.Core.Services.Tft.Implementations;

/// <summary>
/// Service for computing TFT summoner statistics from match data.
/// </summary>
public class TftSummonerStatsService(TranscendenceContext context) : ITftSummonerStatsService
{
    public async Task<TftSummonerOverviewStats> GetSummonerOverviewAsync(
        Guid summonerId, 
        int recentGamesCount, 
        CancellationToken ct)
    {
        var safeCount = Math.Clamp(recentGamesCount, 1, 100);

        // Get recent matches with placement data
        var recentMatches = await context.TftMatchParticipants
            .AsNoTracking()
            .Where(x => x.SummonerId == summonerId)
            .Include(x => x.Match)
            .Include(x => x.Units)
            .Include(x => x.Traits)
            .OrderByDescending(x => x.Match.MatchDate)
            .Take(safeCount)
            .ToListAsync(ct);

        var totalMatches = recentMatches.Count;
        
        if (totalMatches == 0)
        {
            return new TftSummonerOverviewStats(
                summonerId,
                0,
                0,
                0,
                0,
                []);
        }

        // Calculate aggregate statistics
        var placements = recentMatches.Select(m => m.Placement).ToList();
        var averagePlacement = placements.Average();
        var top4Count = placements.Count(p => p <= 4);
        var winCount = placements.Count(p => p == 1);
        var top4Rate = (double)top4Count / totalMatches * 100;
        var winRate = (double)winCount / totalMatches * 100;

        // Map recent performance
        var recentPerformance = recentMatches.Select(m => new TftRecentPerformanceStats(
            m.Match.MatchId,
            m.Match.MatchDate,
            m.Placement,
            m.Level,
            m.Match.SetCoreName,
            m.Match.Patch,
            m.Units.Select(u => new TftUnitPerformanceSummary(
                u.CharacterId,
                u.Name,
                u.Tier,
                u.Items)).ToList(),
            m.Traits.Select(t => new TftTraitPerformanceSummary(
                t.Name,
                t.NumUnits,
                t.TierCurrent)).ToList()
        )).ToList();

        return new TftSummonerOverviewStats(
            summonerId,
            totalMatches,
            Math.Round(averagePlacement, 2),
            Math.Round(top4Rate, 2),
            Math.Round(winRate, 2),
            recentPerformance);
    }

    public async Task<IReadOnlyList<TftChampionStat>> GetChampionStatsAsync(
        Guid summonerId, 
        int top, 
        CancellationToken ct)
    {
        var safeTop = Math.Clamp(top, 1, 50);

        // Get all units played by this summoner with their match placements
        var unitData = await context.TftMatchParticipantUnits
            .AsNoTracking()
            .Where(u => u.Participant.SummonerId == summonerId)
            .Include(u => u.Participant)
            .ThenInclude(p => p.Match)
            .ToListAsync(ct);

        if (unitData.Count == 0)
        {
            return [];
        }

        // Group by character ID and compute stats
        var championStats = unitData
            .GroupBy(u => new { u.CharacterId, u.Name, u.Rarity })
            .Select(g =>
            {
                var games = g.Count();
                var avgPlacement = g.Average(u => u.Participant.Placement);
                var top4Count = g.Count(u => u.Participant.Placement <= 4);
                var winCount = g.Count(u => u.Participant.Placement == 1);

                return new TftChampionStat(
                    g.Key.CharacterId,
                    g.Key.Name,
                    g.Key.Rarity + 1, // Convert rarity (0-based) to cost (1-based)
                    games,
                    Math.Round(avgPlacement, 2),
                    Math.Round((double)top4Count / games * 100, 2),
                    Math.Round((double)winCount / games * 100, 2));
            })
            .OrderByDescending(x => x.GamesPlayed)
            .ThenBy(x => x.AveragePlacement)
            .Take(safeTop)
            .ToList();

        return championStats;
    }
}
