using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Core.Services.Analysis.Interfaces;
using Transcendence.Service.Core.Services.RiotApi.DTOs;
using Transcendence.Service.Core.Services.Summoners.Interfaces;

namespace Transcendence.Service.Core.Services.Summoners.Implementations;

public sealed class SummonerProfileService(
    ISummonerRepository summonerRepository,
    ISummonerStatsService statsService) : ISummonerProfileService
{
    public async Task<IReadOnlyList<SummonerSearchCandidateDto>> SearchByPrefixAsync(
        string platformRegion,
        string gameNamePrefix,
        string? tagLinePrefix,
        int limit,
        CancellationToken ct = default)
    {
        var candidates = await summonerRepository.SearchByPrefixAsync(
            platformRegion,
            gameNamePrefix,
            tagLinePrefix,
            limit,
            ct);
        return candidates
            .Select(candidate => new SummonerSearchCandidateDto(
                candidate.PlatformRegion,
                candidate.GameName,
                candidate.TagLine,
                candidate.ProfileIconId))
            .ToList();
    }

    public async Task<SummonerProfileResponse?> GetProfileByRiotIdAsync(
        string platformRegion,
        string gameName,
        string tagLine,
        CancellationToken ct = default)
    {
        var summoner = await summonerRepository.FindByRiotIdWithRanksAsync(
            platformRegion,
            gameName,
            tagLine,
            ct);
        if (summoner is null)
            return null;

        var soloRank = summoner.Ranks
            .Where(r => IsSoloRankQueue(r.QueueType))
            .OrderByDescending(r => r.UpdatedAt)
            .FirstOrDefault();
        var flexRank = summoner.Ranks
            .Where(r => IsFlexRankQueue(r.QueueType))
            .OrderByDescending(r => r.UpdatedAt)
            .FirstOrDefault();

        // These calls share a scoped DbContext and must remain sequential.
        var activeSeasonStats = await statsService.GetActiveSeasonProfileStatsAsync(summoner.Id, 5, 20, ct);
        var recent = await statsService.GetRecentMatchesAsync(summoner.Id, 1, 10, null, null, ct);
        var playedWith = await statsService.GetPlayedWithAsync(summoner.Id, 100, 6, ct);
        var mastery = await statsService.GetTopMasteryAsync(summoner.Id, 6, ct);
        var overview = activeSeasonStats.Overview;
        var mostRecentMatchDate = recent.Items.Count > 0 ? recent.Items[0].MatchDate : (long?)null;

        return new SummonerProfileResponse
        {
            SummonerId = summoner.Id,
            Puuid = summoner.Puuid ?? string.Empty,
            GameName = summoner.GameName ?? string.Empty,
            TagLine = summoner.TagLine ?? string.Empty,
            SummonerLevel = (int)summoner.SummonerLevel,
            ProfileIconId = summoner.ProfileIconId,
            SoloRank = soloRank is null ? null : new RankInfo
            {
                Tier = soloRank.Tier,
                Division = soloRank.RankNumber,
                LeaguePoints = soloRank.LeaguePoints,
                Wins = soloRank.Wins,
                Losses = soloRank.Losses
            },
            FlexRank = flexRank is null ? null : new RankInfo
            {
                Tier = flexRank.Tier,
                Division = flexRank.RankNumber,
                LeaguePoints = flexRank.LeaguePoints,
                Wins = flexRank.Wins,
                Losses = flexRank.Losses
            },
            OverviewStats = overview.TotalMatches > 0 ? new ProfileOverviewStats
            {
                TotalMatches = overview.TotalMatches,
                Wins = overview.Wins,
                Losses = overview.Losses,
                WinRate = overview.WinRate,
                AvgKills = overview.AvgKills,
                AvgDeaths = overview.AvgDeaths,
                AvgAssists = overview.AvgAssists,
                KdaRatio = overview.KdaRatio,
                AvgCsPerMin = overview.AvgCsPerMin,
                AvgVisionScore = overview.AvgVisionScore,
                AvgDamageToChamps = overview.AvgDamageToChamps
            } : null,
            TopChampions = activeSeasonStats.Champions.Select(c => new ProfileChampionStat
            {
                ChampionId = c.ChampionId,
                Games = c.Games,
                Wins = c.Wins,
                Losses = c.Losses,
                WinRate = c.WinRate,
                KdaRatio = c.KdaRatio
            }).ToList(),
            ActiveSeason = new ProfileSeasonMetadata
            {
                SeasonKey = activeSeasonStats.SeasonKey,
                DisplayName = activeSeasonStats.SeasonDisplayName,
                QueueScope = activeSeasonStats.QueueScope
            },
            FullHistory = activeSeasonStats.FullHistory is null ? null : new ProfileFullHistoryStatus
            {
                Status = activeSeasonStats.FullHistory.Status,
                RequestedAtUtc = activeSeasonStats.FullHistory.RequestedAtUtc,
                StartedAtUtc = activeSeasonStats.FullHistory.StartedAtUtc,
                CompletedAtUtc = activeSeasonStats.FullHistory.CompletedAtUtc,
                UpdatedAtUtc = activeSeasonStats.FullHistory.UpdatedAtUtc,
                PagesScanned = activeSeasonStats.FullHistory.PagesScanned,
                MatchIdsDiscovered = activeSeasonStats.FullHistory.MatchIdsDiscovered,
                FactsPersisted = activeSeasonStats.FullHistory.FactsPersisted,
                DetailFetchFailures = activeSeasonStats.FullHistory.DetailFetchFailures,
                CompletedMatchCount = activeSeasonStats.FullHistory.CompletedMatchCount,
                RiotWins = activeSeasonStats.FullHistory.RiotWins,
                RiotLosses = activeSeasonStats.FullHistory.RiotLosses,
                RiotTotal = activeSeasonStats.FullHistory.RiotTotal,
                RankedCountDelta = activeSeasonStats.FullHistory.RankedCountDelta,
                CoverageStatus = activeSeasonStats.FullHistory.CoverageStatus,
                ClassifierVersion = activeSeasonStats.FullHistory.ClassifierVersion
            },
            FrequentlyPlayedWith = playedWith.Select(p => new FrequentlyPlayedWithStat
            {
                SummonerId = p.SummonerId,
                GameName = p.GameName ?? string.Empty,
                TagLine = p.TagLine ?? string.Empty,
                GamesTogether = p.GamesTogether,
                SameTeamGames = p.SameTeamGames,
                SameTeamWins = p.SameTeamWins
            }).ToList(),
            TopMastery = mastery.Select(m => new ChampionMasteryStat
            {
                ChampionId = m.ChampionId,
                ChampionLevel = m.ChampionLevel,
                ChampionPoints = m.ChampionPoints,
                LastPlayTime = m.LastPlayTime,
                ChestGranted = m.ChestGranted,
                TokensEarned = m.TokensEarned
            }).ToList(),
            ProfileAge = new DataAgeMetadata { FetchedAt = summoner.UpdatedAt },
            RankAge = new DataAgeMetadata
            {
                FetchedAt = soloRank?.UpdatedAt ?? flexRank?.UpdatedAt ?? DateTime.UtcNow
            },
            StatsAge = mostRecentMatchDate.HasValue
                ? new DataAgeMetadata
                {
                    FetchedAt = DateTimeOffset.FromUnixTimeMilliseconds(mostRecentMatchDate.Value).UtcDateTime
                }
                : null
        };
    }

    private static bool IsSoloRankQueue(string? queueType)
    {
        if (string.IsNullOrWhiteSpace(queueType))
            return false;

        return queueType.Equals("RANKED_SOLO_5x5", StringComparison.OrdinalIgnoreCase) ||
               queueType.Equals("RANKED_SOLO_5X5", StringComparison.OrdinalIgnoreCase) ||
               queueType.Equals("RANKED_SOLO_5V5", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFlexRankQueue(string? queueType) =>
        !string.IsNullOrWhiteSpace(queueType) &&
        queueType.StartsWith("RANKED_FLEX", StringComparison.OrdinalIgnoreCase);
}
