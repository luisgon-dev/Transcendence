using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Analytics;
using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Core.Services.Diagnostics;
using Transcendence.Service.Core.Services.Leaderboards.Interfaces;
using Transcendence.Service.Core.Services.Leaderboards.Models;
using Transcendence.Service.Core.Services.RiotApi;

namespace Transcendence.Service.Core.Services.Leaderboards.Implementations;

public sealed class LeaderboardService(
    ILeaderboardRepository repository,
    HybridCache cache,
    LeaderboardTelemetry telemetry,
    TranscendenceContext? snapshots = null) : ILeaderboardService
{
    public const string SnapshotFeature = "leaderboard-regional";
    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromSeconds(30)
    };

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
        var safeLimit = Math.Clamp(limit, 1, 100);
        var normalizedRole = NormalizeRole(role);
        var safeMinimumGames = Math.Clamp(minimumChampionGames, 1, 100);
        var normalizedPlatform = platformRegion.Trim().ToUpperInvariant();
        var kind = championId is null ? "regional" : "champion";
        var cacheKey = championId is null
            ? $"leaderboards:v1:regional:{normalizedPlatform}:{normalizedQueue}:{safeLimit}"
            : $"leaderboards:v1:champion:{normalizedPlatform}:{normalizedQueue}:{championId.Value}:{normalizedRole ?? "ALL"}:{safeMinimumGames}:{safeLimit}";
        var cacheMiss = false;
        var succeeded = false;
        var started = Stopwatch.GetTimestamp();

        try
        {
            var result = await cache.GetOrCreateAsync(
                cacheKey,
                async token =>
                {
                    cacheMiss = true;
                    if (championId is null && snapshots != null)
                    {
                        var scope = $"{normalizedPlatform}:{normalizedQueue}";
                        var oldest = DateTime.UtcNow.AddHours(-24);
                        var snapshot = await snapshots.AnalyticsResponseSnapshots.AsNoTracking()
                            .Where(row => row.Feature == SnapshotFeature && row.ScopeKey == scope && row.Patch == "*" &&
                                          row.ComputedAtUtc >= oldest)
                            .Select(row => row.Payload).FirstOrDefaultAsync(token);
                        if (snapshot != null && JsonSerializer.Deserialize<LeaderboardResponse>(snapshot) is { } board)
                            return board with { Entries = board.Entries.Take(safeLimit).ToList() };
                    }
                    return await ComputeAsync(
                        normalizedPlatform,
                        normalizedQueue,
                        championId,
                        normalizedRole,
                        safeLimit,
                        safeMinimumGames,
                        token);
                },
                CacheOptions,
                tags: ["leaderboards"],
                cancellationToken: ct);
            succeeded = true;
            return result;
        }
        finally
        {
            telemetry.Record(
                kind,
                cacheMiss,
                succeeded,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
    }

    // Refresh before expiry. A failure leaves both the durable snapshot and the previous cache intact.
    public async Task RefreshRegionalAsync(string platformRegion, string queue, CancellationToken ct = default)
    {
        var platform = platformRegion.Trim().ToUpperInvariant();
        var normalizedQueue = NormalizeQueue(queue);
        var board = await ComputeAsync(platform, normalizedQueue, null, null, 100, 5, ct);
        if (snapshots != null)
        {
            var scope = $"{platform}:{normalizedQueue}";
            var snapshot = await snapshots.AnalyticsResponseSnapshots
                .FirstOrDefaultAsync(row => row.Feature == SnapshotFeature && row.ScopeKey == scope && row.Patch == "*", ct);
            if (snapshot == null)
            {
                snapshot = new AnalyticsResponseSnapshot
                {
                    Id = Guid.NewGuid(), Feature = SnapshotFeature, ScopeKey = scope, Patch = "*"
                };
                snapshots.AnalyticsResponseSnapshots.Add(snapshot);
            }
            snapshot.Payload = JsonSerializer.Serialize(board);
            snapshot.ComputedAtUtc = board.GeneratedAtUtc;
            await snapshots.SaveChangesAsync(ct);
        }
        await cache.SetAsync($"leaderboards:v1:regional:{platform}:{normalizedQueue}:100",
            board, CacheOptions, tags: ["leaderboards"], cancellationToken: ct);
    }

    private async Task<LeaderboardResponse> ComputeAsync(
        string platformRegion,
        string normalizedQueue,
        int? championId,
        string? normalizedRole,
        int safeLimit,
        int minimumChampionGames,
        CancellationToken ct)
    {
        var rankedFlex = normalizedQueue == QueueCatalog.QueueFamilyRankedFlex;
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
            minimumChampionGames,
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
