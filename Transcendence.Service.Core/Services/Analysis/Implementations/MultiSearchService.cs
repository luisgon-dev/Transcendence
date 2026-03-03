using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Core.Services.Analysis.Interfaces;
using Transcendence.Service.Core.Services.Analysis.Models;

namespace Transcendence.Service.Core.Services.Analysis.Implementations;

public class MultiSearchService(
    ISummonerRepository summonerRepository,
    ISummonerStatsService statsService,
    ILogger<MultiSearchService> logger
) : IMultiSearchService
{
    private static readonly string[] AllRoles = ["TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY"];

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

    public async Task<MultiSearchResult> SearchAsync(
        string platformRegion,
        IReadOnlyList<(string GameName, string TagLine)> riotIds,
        CancellationToken ct = default)
    {
        // Batch-load all summoners from DB in a single query.
        var summoners = await summonerRepository.FindByRiotIdsAsync(platformRegion, riotIds, ct);

        // Index by normalized gameName+tagLine for fast lookup.
        var summonerLookup = summoners.ToDictionary(
            s => ($"{s.GameNameNormalized}", $"{s.TagLineNormalized}"),
            s => s,
            EqualityComparer<(string, string)>.Default);

        var results = new List<MultiSearchSummoner>();

        // Process each requested summoner sequentially (scoped DbContext is not thread-safe).
        foreach (var (gameName, tagLine) in riotIds)
        {
            var normalizedName = gameName.Trim().ToUpperInvariant();
            var normalizedTag = tagLine.Trim().ToUpperInvariant();
            var key = (normalizedName, normalizedTag);

            if (!summonerLookup.TryGetValue(key, out var summoner))
            {
                results.Add(new MultiSearchSummoner(gameName, tagLine, Found: false));
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
                    "Failed to compute stats for summoner {GameName}#{TagLine} in multi-search. Returning partial data.",
                    gameName, tagLine);

                // Return found but with minimal data on failure.
                results.Add(BuildMinimalResult(summoner, gameName, tagLine));
            }
        }

        var teamAnalysis = BuildTeamAnalysis(results);
        return new MultiSearchResult(results, teamAnalysis);
    }

    private async Task<MultiSearchSummoner> BuildSummonerResultAsync(
        Data.Models.LoL.Account.Summoner summoner,
        string gameName,
        string tagLine,
        CancellationToken ct)
    {
        // Sequential calls - DbContext is scoped and not thread-safe.
        var overview = await statsService.GetSummonerOverviewAsync(summoner.Id, 20, ct);
        var champions = await statsService.GetChampionStatsAsync(summoner.Id, 3, ct);
        var roles = await statsService.GetRoleBreakdownAsync(summoner.Id, ct);

        var soloRank = summoner.Ranks
            .FirstOrDefault(r => IsSoloRankQueue(r.QueueType));
        var flexRank = summoner.Ranks
            .FirstOrDefault(r => IsFlexRankQueue(r.QueueType));

        var primaryRole = roles.Count > 0 ? roles[0].Role : null;

        return new MultiSearchSummoner(
            GameName: summoner.GameName ?? gameName,
            TagLine: summoner.TagLine ?? tagLine,
            Found: true,
            SummonerId: summoner.Id,
            ProfileIconId: summoner.ProfileIconId,
            SummonerLevel: (int)summoner.SummonerLevel,
            SoloRank: soloRank != null
                ? new MultiSearchRank(soloRank.Tier, soloRank.RankNumber, soloRank.LeaguePoints, soloRank.Wins,
                    soloRank.Losses)
                : null,
            FlexRank: flexRank != null
                ? new MultiSearchRank(flexRank.Tier, flexRank.RankNumber, flexRank.LeaguePoints, flexRank.Wins,
                    flexRank.Losses)
                : null,
            Overview: overview.TotalMatches > 0 ? overview : null,
            TopChampions: champions.Count > 0 ? champions : null,
            Roles: roles.Count > 0 ? roles : null,
            PrimaryRole: primaryRole
        );
    }

    private static MultiSearchSummoner BuildMinimalResult(
        Data.Models.LoL.Account.Summoner summoner,
        string gameName,
        string tagLine)
    {
        var soloRank = summoner.Ranks
            .FirstOrDefault(r => IsSoloRankQueue(r.QueueType));
        var flexRank = summoner.Ranks
            .FirstOrDefault(r => IsFlexRankQueue(r.QueueType));

        return new MultiSearchSummoner(
            GameName: summoner.GameName ?? gameName,
            TagLine: summoner.TagLine ?? tagLine,
            Found: true,
            SummonerId: summoner.Id,
            ProfileIconId: summoner.ProfileIconId,
            SummonerLevel: (int)summoner.SummonerLevel,
            SoloRank: soloRank != null
                ? new MultiSearchRank(soloRank.Tier, soloRank.RankNumber, soloRank.LeaguePoints, soloRank.Wins,
                    soloRank.Losses)
                : null,
            FlexRank: flexRank != null
                ? new MultiSearchRank(flexRank.Tier, flexRank.RankNumber, flexRank.LeaguePoints, flexRank.Wins,
                    flexRank.Losses)
                : null
        );
    }

    private static MultiSearchTeamAnalysis BuildTeamAnalysis(List<MultiSearchSummoner> summoners)
    {
        var foundSummoners = summoners.Where(s => s.Found).ToList();

        // Compute average rank from solo queue ranks.
        var rankScores = foundSummoners
            .Where(s => s.SoloRank != null)
            .Select(s => MapTierToScore(s.SoloRank!.Tier))
            .ToList();

        var avgRankScore = rankScores.Count > 0 ? rankScores.Average() : 0.0;
        var avgRankLabel = ScoreToTierLabel(avgRankScore);

        // Determine role coverage from each player's primary role.
        var coveredRoles = foundSummoners
            .Where(s => !string.IsNullOrWhiteSpace(s.PrimaryRole))
            .Select(s => s.PrimaryRole!.ToUpperInvariant())
            .Where(r => AllRoles.Contains(r))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var missingRoles = AllRoles
            .Where(r => !coveredRoles.Contains(r, StringComparer.OrdinalIgnoreCase))
            .ToList();

        // Detect potential autofills: players whose primary role duplicates another player's,
        // or players not found in the database.
        var autofills = new List<MultiSearchAutofill>();

        // Count how many players claim each role.
        var roleCounts = foundSummoners
            .Where(s => !string.IsNullOrWhiteSpace(s.PrimaryRole))
            .GroupBy(s => s.PrimaryRole!.ToUpperInvariant())
            .Where(g => g.Count() > 1)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var (role, players) in roleCounts)
        {
            // All but the highest-ranked player in a contested role are potential autofills.
            var sorted = players
                .OrderByDescending(p => p.SoloRank != null ? MapTierToScore(p.SoloRank.Tier) : 0)
                .Skip(1)
                .ToList();

            foreach (var player in sorted)
            {
                autofills.Add(new MultiSearchAutofill(
                    player.GameName,
                    player.TagLine,
                    player.PrimaryRole,
                    $"Contested role: {role} (may be autofilled)"
                ));
            }
        }

        // Flag not-found summoners.
        foreach (var notFound in summoners.Where(s => !s.Found))
        {
            autofills.Add(new MultiSearchAutofill(
                notFound.GameName,
                notFound.TagLine,
                null,
                "Not found in database"
            ));
        }

        return new MultiSearchTeamAnalysis(
            AverageRankScore: avgRankScore,
            AverageRankLabel: avgRankLabel,
            RoleCoverage: coveredRoles,
            MissingRoles: missingRoles,
            PotentialAutofills: autofills
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

    private static bool IsSoloRankQueue(string? queueType)
    {
        if (string.IsNullOrWhiteSpace(queueType)) return false;
        return queueType.Equals("RANKED_SOLO_5x5", StringComparison.OrdinalIgnoreCase) ||
               queueType.Equals("RANKED_SOLO_5X5", StringComparison.OrdinalIgnoreCase) ||
               queueType.Equals("RANKED_SOLO_5V5", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFlexRankQueue(string? queueType)
    {
        if (string.IsNullOrWhiteSpace(queueType)) return false;
        return queueType.StartsWith("RANKED_FLEX", StringComparison.OrdinalIgnoreCase);
    }
}
