using Transcendence.Data.Models.Tft.Account;
using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Core.Services.Tft.Interfaces;
using Transcendence.Service.Core.Services.Tft.Models;

namespace Transcendence.Service.Core.Services.Tft.Implementations;

/// <summary>
/// Service for TFT multi-search (lobby scouting) functionality.
/// </summary>
public class TftMultiSearchService(
    ITftSummonerRepository summonerRepository,
    ITftSummonerStatsService statsService,
    ILogger<TftMultiSearchService> logger) : ITftMultiSearchService
{
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

    public async Task<TftMultiSearchResult> SearchAsync(
        string platformRegion,
        IReadOnlyList<(string GameName, string TagLine)> riotIds,
        CancellationToken ct = default)
    {
        // Batch-load all summoners from DB in a single query
        var summoners = await summonerRepository.FindByRiotIdsAsync(platformRegion, riotIds, ct);

        // Index by normalized gameName+tagLine for fast lookup
        var summonerLookup = summoners.ToDictionary(
            s => ($"{s.GameNameNormalized}", $"{s.TagLineNormalized}"),
            s => s,
            EqualityComparer<(string, string)>.Default);

        var results = new List<TftMultiSearchSummoner>();

        // Process each requested summoner sequentially (scoped DbContext is not thread-safe)
        foreach (var (gameName, tagLine) in riotIds)
        {
            var normalizedName = gameName.Trim().ToUpperInvariant();
            var normalizedTag = tagLine.Trim().ToUpperInvariant();
            var key = (normalizedName, normalizedTag);

            if (!summonerLookup.TryGetValue(key, out var summoner))
            {
                results.Add(new TftMultiSearchSummoner(gameName, tagLine, Found: false));
                continue;
            }

            try
            {
                var result = await BuildSummonerResultAsync(summoner, gameName, tagLine, ct);
                results.Add(result);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Failed to compute stats for TFT summoner {GameName}#{TagLine} in multi-search. Returning partial data.",
                    gameName, tagLine);

                // Return found but with minimal data on failure
                results.Add(BuildMinimalResult(summoner, gameName, tagLine));
            }
        }

        var lobbyAnalysis = BuildLobbyAnalysis(results);
        return new TftMultiSearchResult(results, lobbyAnalysis);
    }

    private async Task<TftMultiSearchSummoner> BuildSummonerResultAsync(
        TftSummoner summoner,
        string gameName,
        string tagLine,
        CancellationToken ct)
    {
        // Sequential calls - DbContext is scoped and not thread-safe
        var overview = await statsService.GetSummonerOverviewAsync(summoner.Id, 20, ct);
        var champions = await statsService.GetChampionStatsAsync(summoner.Id, 5, ct);

        var rank = summoner.Ranks
            .OrderByDescending(r => r.UpdatedAt)
            .FirstOrDefault(r => IsRankedTftQueue(r.QueueType));

        return new TftMultiSearchSummoner(
            GameName: summoner.GameName ?? gameName,
            TagLine: summoner.TagLine ?? tagLine,
            Found: true,
            SummonerId: summoner.Id,
            ProfileIconId: summoner.ProfileIconId,
            SummonerLevel: summoner.SummonerLevel,
            Rank: rank != null
                ? new TftMultiSearchRank(rank.Tier, rank.RankNumber, rank.LeaguePoints, rank.Wins, rank.Losses)
                : null,
            Overview: overview.TotalMatches > 0 ? overview : null,
            TopUnits: champions.Count > 0 ? champions : null
        );
    }

    private static TftMultiSearchSummoner BuildMinimalResult(
        TftSummoner summoner,
        string gameName,
        string tagLine)
    {
        var rank = summoner.Ranks
            .OrderByDescending(r => r.UpdatedAt)
            .FirstOrDefault(r => IsRankedTftQueue(r.QueueType));

        return new TftMultiSearchSummoner(
            GameName: summoner.GameName ?? gameName,
            TagLine: summoner.TagLine ?? tagLine,
            Found: true,
            SummonerId: summoner.Id,
            ProfileIconId: summoner.ProfileIconId,
            SummonerLevel: summoner.SummonerLevel,
            Rank: rank != null
                ? new TftMultiSearchRank(rank.Tier, rank.RankNumber, rank.LeaguePoints, rank.Wins, rank.Losses)
                : null
        );
    }

    private static TftMultiSearchLobbyAnalysis BuildLobbyAnalysis(List<TftMultiSearchSummoner> summoners)
    {
        var foundSummoners = summoners.Where(s => s.Found).ToList();

        // Compute average rank from TFT ranks
        var rankScores = foundSummoners
            .Where(s => s.Rank != null)
            .Select(s => MapTierToScore(s.Rank!.Tier))
            .ToList();

        var avgRankScore = rankScores.Count > 0 ? rankScores.Average() : 0.0;
        var avgRankLabel = ScoreToTierLabel(avgRankScore);

        // Detect potential issues
        var issues = new List<TftMultiSearchAutofill>();

        // Flag not-found summoners
        foreach (var notFound in summoners.Where(s => !s.Found))
        {
            issues.Add(new TftMultiSearchAutofill(
                notFound.GameName,
                notFound.TagLine,
                "Not found in database"
            ));
        }

        return new TftMultiSearchLobbyAnalysis(
            AverageRankScore: avgRankScore,
            AverageRankLabel: avgRankLabel,
            FoundPlayers: foundSummoners.Count,
            TotalPlayers: summoners.Count,
            PotentialIssues: issues
        );
    }

    private static double MapTierToScore(string? tier)
    {
        if (string.IsNullOrWhiteSpace(tier)) return 0;
        return TierScores.GetValueOrDefault(tier, 0);
    }

    private static string ScoreToTierLabel(double score)
    {
        return score switch
        {
            >= 9.5 => "CHALLENGER",
            >= 8.5 => "GRANDMASTER",
            >= 7.5 => "MASTER",
            >= 6.5 => "DIAMOND",
            >= 5.5 => "EMERALD",
            >= 4.5 => "PLATINUM",
            >= 3.5 => "GOLD",
            >= 2.5 => "SILVER",
            >= 1.5 => "BRONZE",
            > 0 => "IRON",
            _ => "UNRANKED"
        };
    }

    private static bool IsRankedTftQueue(string? queueType)
    {
        if (string.IsNullOrWhiteSpace(queueType)) return false;
        return queueType.Equals("RANKED_TFT", StringComparison.OrdinalIgnoreCase) ||
               queueType.Equals("RANKED_TFT_DOUBLE_UP", StringComparison.OrdinalIgnoreCase) ||
               queueType.Equals("RANKED_TFT_TURBO", StringComparison.OrdinalIgnoreCase);
    }
}
