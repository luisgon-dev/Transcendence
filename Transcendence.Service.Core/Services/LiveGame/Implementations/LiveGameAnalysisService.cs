using Microsoft.EntityFrameworkCore;
using Transcendence.Data;
using Transcendence.Service.Core.Services.Analytics.Interfaces;
using Transcendence.Service.Core.Services.Analytics.Models;
using Transcendence.Service.Core.Services.LiveGame.Interfaces;
using Transcendence.Service.Core.Services.LiveGame.Models;

namespace Transcendence.Service.Core.Services.LiveGame.Implementations;

public class LiveGameAnalysisService(
    TranscendenceContext db,
    IChampionAnalyticsService championAnalyticsService) : ILiveGameAnalysisService
{
    private const double NeutralWinRate = 0.50;
    private const double DefaultRankScore = 3.0;
    private const double MaxTierScore = 10.0;
    private const double RecentWinRateWeight = 0.40;
    private const double ChampionWinRateWeight = 0.40;
    private const double RankWeight = 0.20;

    // Thresholds represent meaningful deviations from neutral values before labeling strengths/weaknesses.
    private const double RecentWinRateStrengthThreshold = 0.52;
    private const double ChampionWinRateStrengthThreshold = 0.51;
    private const double RankStrengthThreshold = 0.55;
    private const double RecentWinRateWeaknessThreshold = 0.48;
    private const double ChampionWinRateWeaknessThreshold = 0.49;
    private const double RankWeaknessThreshold = 0.35;

    private static readonly Dictionary<string, double> TierScores = new(StringComparer.OrdinalIgnoreCase)
    {
        ["IRON"] = 1,
        ["BRONZE"] = 2,
        ["SILVER"] = 3,
        ["GOLD"] = 4,
        ["PLATINUM"] = 5,
        ["EMERALD"] = 6,
        ["DIAMOND"] = 7,
        ["MASTER"] = 8,
        ["GRANDMASTER"] = 9,
        ["CHALLENGER"] = 10
    };

    public async Task<LiveGameAnalysisDto> AnalyzeAsync(
        string platformRegion,
        LiveGameResponseDto liveGame,
        CancellationToken ct = default)
    {
        var championWinRateCache = new Dictionary<int, double?>();
        var participantAnalysis = new List<LiveGameParticipantAnalysisDto>();
        var participantPuuids = liveGame.Participants
            .Select(p => p.Puuid)
            .Where(puuid => !string.IsNullOrWhiteSpace(puuid))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var summonersRaw = await db.Summoners
            .AsNoTracking()
            .Include(x => x.Ranks)
            .Where(x => x.Puuid != null && participantPuuids.Contains(x.Puuid))
            .ToListAsync(ct);

        var summonersByPuuid = summonersRaw
            .GroupBy(s => s.Puuid!, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g
                    .OrderByDescending(s => s.UpdatedAt)
                    .ThenByDescending(s => s.Id)
                    .First(),
                StringComparer.Ordinal);

        var summonerIds = summonersByPuuid.Values
            .Select(s => s.Id)
            .Distinct()
            .ToList();

        var recentOverviewBySummonerId = await db.MatchParticipants
            .AsNoTracking()
            .Where(mp => summonerIds.Contains(mp.SummonerId))
            .GroupBy(mp => mp.SummonerId)
            .Select(g => new
            {
                SummonerId = g.Key,
                Games = g.Count(),
                WinRate = g.Count() > 0 ? (double)g.Sum(x => x.Win ? 1 : 0) / g.Count() * 100.0 : 0.0,
                KdaRatio = (g.Average(x => (double)x.Kills) + g.Average(x => (double)x.Assists))
                           / Math.Max(1.0, g.Average(x => (double)x.Deaths))
            })
            .ToDictionaryAsync(x => x.SummonerId, ct);

        foreach (var participant in liveGame.Participants)
        {
            var summoner = !string.IsNullOrWhiteSpace(participant.Puuid) &&
                           summonersByPuuid.TryGetValue(participant.Puuid, out var resolvedSummoner)
                ? resolvedSummoner
                : null;

            string? tier = null;
            string? division = null;
            int? lp = null;
            double? recentWinRate = null;
            double? recentKda = null;

            if (summoner != null)
            {
                var solo = summoner.Ranks.FirstOrDefault(x => x.QueueType == "RANKED_SOLO_5x5");
                tier = solo?.Tier;
                division = solo?.RankNumber;
                lp = solo?.LeaguePoints;

                if (recentOverviewBySummonerId.TryGetValue(summoner.Id, out var overview) && overview.Games > 0)
                {
                    recentWinRate = overview.WinRate;
                    recentKda = overview.KdaRatio;
                }
            }

            if (!championWinRateCache.TryGetValue(participant.ChampionId, out var championWinRate))
            {
                championWinRate = await GetChampionWeightedWinRateAsync(participant.ChampionId, ct);
                championWinRateCache[participant.ChampionId] = championWinRate;
            }

            participantAnalysis.Add(new LiveGameParticipantAnalysisDto(
                participant.Puuid,
                participant.TeamId,
                participant.ChampionId,
                tier,
                division,
                lp,
                recentWinRate,
                recentKda,
                championWinRate
            ));
        }

        var teams = BuildTeamAnalysis(participantAnalysis);

        if (teams.Count == 2)
        {
            var teamA = teams[0];
            var teamB = teams[1];
            var total = Math.Max(0.0001, teamA.CompositeScore + teamB.CompositeScore);

            teams[0] = teamA with { EstimatedWinProbability = teamA.CompositeScore / total };
            teams[1] = teamB with { EstimatedWinProbability = teamB.CompositeScore / total };
        }

        return new LiveGameAnalysisDto(
            GeneratedAtUtc: DateTime.UtcNow,
            Participants: participantAnalysis,
            Teams: teams
        );
    }

    private async Task<double?> GetChampionWeightedWinRateAsync(int championId, CancellationToken ct)
    {
        var summary = await championAnalyticsService.GetWinRatesAsync(
            championId,
            new ChampionAnalyticsFilter(),
            ct);

        if (summary.ByRoleTier.Count == 0)
            return null;

        var totalGames = summary.ByRoleTier.Sum(x => x.Games);
        if (totalGames == 0)
            return null;

        var weighted = summary.ByRoleTier.Sum(x => x.WinRate * x.Games) / totalGames;
        return weighted;
    }

    private static List<TeamAnalysisDto> BuildTeamAnalysis(List<LiveGameParticipantAnalysisDto> participants)
    {
        return participants
            .GroupBy(x => x.TeamId)
            .Select(g =>
            {
                var avgRecentWinRate = AverageOrDefault(g.Select(x => x.RecentWinRate), NeutralWinRate);
                var avgChampionWinRate = AverageOrDefault(g.Select(x => x.ChampionWinRate), NeutralWinRate);
                var avgRankScore = AverageOrDefault(g.Select(x => (double?)MapTierToScore(x.RankTier)), DefaultRankScore);
                var normalizedRank = Math.Clamp(avgRankScore / MaxTierScore, 0.0, 1.0);

                var score = (avgRecentWinRate * RecentWinRateWeight)
                            + (avgChampionWinRate * ChampionWinRateWeight)
                            + (normalizedRank * RankWeight);

                var strengths = new List<string>();
                var weaknesses = new List<string>();

                if (avgRecentWinRate >= RecentWinRateStrengthThreshold) strengths.Add("Strong recent form");
                if (avgChampionWinRate >= ChampionWinRateStrengthThreshold) strengths.Add("Meta-favored champions");
                if (normalizedRank >= RankStrengthThreshold) strengths.Add("Higher average ladder tier");

                if (avgRecentWinRate <= RecentWinRateWeaknessThreshold) weaknesses.Add("Weak recent form");
                if (avgChampionWinRate <= ChampionWinRateWeaknessThreshold) weaknesses.Add("Lower champion baseline win rates");
                if (normalizedRank <= RankWeaknessThreshold) weaknesses.Add("Lower average ladder tier");

                return new TeamAnalysisDto(
                    TeamId: g.Key,
                    AverageRecentWinRate: avgRecentWinRate,
                    AverageChampionWinRate: avgChampionWinRate,
                    AverageRankScore: avgRankScore,
                    CompositeScore: score,
                    EstimatedWinProbability: NeutralWinRate,
                    Strengths: strengths,
                    Weaknesses: weaknesses
                );
            })
            .OrderBy(x => x.TeamId)
            .ToList();
    }

    private static double AverageOrDefault(IEnumerable<double?> values, double fallback)
    {
        var filtered = values.Where(x => x.HasValue).Select(x => x!.Value).ToList();
        if (filtered.Count == 0) return fallback;
        return filtered.Average();
    }

    private static double MapTierToScore(string? tier)
    {
        if (string.IsNullOrWhiteSpace(tier)) return DefaultRankScore;
        return TierScores.GetValueOrDefault(tier, DefaultRankScore);
    }
}
