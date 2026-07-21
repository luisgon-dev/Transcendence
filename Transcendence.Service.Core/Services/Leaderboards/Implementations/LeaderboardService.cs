using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Core.Services.Leaderboards.Interfaces;
using Transcendence.Service.Core.Services.Leaderboards.Models;
using Transcendence.Service.Core.Services.RiotApi;

namespace Transcendence.Service.Core.Services.Leaderboards.Implementations;

public sealed class LeaderboardService(ILeaderboardRepository repository) : ILeaderboardService
{
    private static readonly IReadOnlyDictionary<string, int> TierOrder =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
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

    public async Task<LeaderboardResponse> GetAsync(
        string platformRegion,
        string queue,
        int? championId,
        string? role,
        int limit,
        int minimumChampionGames,
        CancellationToken ct = default)
    {
        var normalizedQueue = NormalizeQueue(queue);
        var rankedFlex = normalizedQueue == QueueCatalog.QueueFamilyRankedFlex;
        var safeLimit = Math.Clamp(limit, 1, 100);
        var normalizedRole = NormalizeRole(role);

        if (championId is null)
        {
            var rows = await repository.GetRegionalAsync(platformRegion, rankedFlex, safeLimit, ct);
            var entries = rows.Select((row, index) => new LeaderboardEntry(
                index + 1,
                row.SummonerId,
                row.GameName,
                row.TagLine,
                row.ProfileIconId,
                row.Tier,
                row.Division,
                row.LeaguePoints,
                row.Wins,
                row.Losses,
                UpdatedAtUtc: row.RankUpdatedAtUtc)).ToList();
            return new LeaderboardResponse(
                platformRegion,
                normalizedQueue,
                null,
                null,
                DateTime.UtcNow,
                entries);
        }

        var queueId = rankedFlex ? QueueCatalog.RankedFlexQueueId : QueueCatalog.RankedSoloDuoQueueId;
        var championRows = await repository.GetChampionAsync(
            platformRegion,
            queueId,
            championId.Value,
            normalizedRole,
            Math.Clamp(minimumChampionGames, 1, 100),
            safeLimit,
            ct);
        var sorted = championRows
            .OrderByDescending(row => row.ChampionGames)
            .ThenByDescending(row => TierOrder.GetValueOrDefault(row.Tier ?? string.Empty))
            .ThenByDescending(row => row.LeaguePoints ?? 0)
            .ThenByDescending(row => row.ChampionGames > 0 ? (double)row.ChampionWins / row.ChampionGames : 0)
            .Take(safeLimit)
            .Select((row, index) => new LeaderboardEntry(
                index + 1,
                row.SummonerId,
                row.GameName,
                row.TagLine,
                row.ProfileIconId,
                row.Tier,
                row.Division,
                row.LeaguePoints,
                row.RankedWins,
                row.RankedLosses,
                row.ChampionGames,
                row.ChampionWins,
                row.ChampionGames > 0 ? (double)row.ChampionWins / row.ChampionGames * 100.0 : 0,
                (double)(row.TotalKills + row.TotalAssists) / Math.Max(1, row.TotalDeaths),
                row.UpdatedAtUtc))
            .ToList();
        return new LeaderboardResponse(
            platformRegion,
            normalizedQueue,
            championId,
            normalizedRole,
            DateTime.UtcNow,
            sorted);
    }

    public static string NormalizeQueue(string? queue) =>
        queue?.Trim().ToUpperInvariant() switch
        {
            "FLEX" or "RANKED_FLEX" or "RANKED_FLEX_SR" => QueueCatalog.QueueFamilyRankedFlex,
            _ => QueueCatalog.QueueFamilyRankedSoloDuo
        };

    public static string? NormalizeRole(string? role) =>
        role?.Trim().ToUpperInvariant() switch
        {
            "TOP" => "TOP",
            "JUNGLE" => "JUNGLE",
            "MID" or "MIDDLE" => "MIDDLE",
            "ADC" or "BOTTOM" => "BOTTOM",
            "SUPPORT" or "UTILITY" => "UTILITY",
            _ => null
        };
}
