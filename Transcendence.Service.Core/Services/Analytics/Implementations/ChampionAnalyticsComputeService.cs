using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Service.Core.Queries;
using Transcendence.Service.Core.Services.Analytics.Interfaces;
using Transcendence.Service.Core.Services.Analytics.Models;
using Transcendence.Service.Core.Services.RiotApi;

namespace Transcendence.Service.Core.Services.Analytics.Implementations;

/// <summary>
/// Raw computation service for champion analytics using EF Core aggregation.
/// </summary>
public partial class ChampionAnalyticsComputeService : IChampionAnalyticsComputeService
{
    private const int MinMatchupSampleSize = 30;
    private const int MatchupsToShow = 5;
    private readonly TranscendenceContext _context;
    private readonly ChampionAnalyticsComputeOptions _options;
    private readonly ILogger<ChampionAnalyticsComputeService> _logger;

    public ChampionAnalyticsComputeService(
        TranscendenceContext context,
        IOptions<ChampionAnalyticsComputeOptions> options,
        ILogger<ChampionAnalyticsComputeService> logger)
    {
        _context = context;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ChampionProBuildsResponse> ComputeProBuildsAsync(
        int championId,
        string? region,
        string? role,
        string scope,
        string patch,
        CancellationToken ct)
    {
        var normalizedRegion = string.IsNullOrWhiteSpace(region) ? "ALL" : region.Trim().ToUpperInvariant();
        var normalizedRole = string.IsNullOrWhiteSpace(role) ? "ALL" : role.Trim().ToUpperInvariant();
        var normalizedScope = NormalizeProScope(scope);

        var proQuery = _context.TrackedProSummoners
            .AsNoTracking()
            .Where(x => x.IsActive);

        proQuery = normalizedScope switch
        {
            "highelo" => proQuery.Where(x => x.IsHighEloOtp),
            "all" => proQuery.Where(x => x.IsPro || x.IsHighEloOtp),
            _ => proQuery.Where(x => x.IsPro)
        };

        if (!string.Equals(normalizedRegion, "ALL", StringComparison.Ordinal))
        {
            var platforms = AnalyticsScopeMath.ResolvePlatformsForRegion(normalizedRegion);
            proQuery = proQuery.Where(x => platforms.Contains(x.PlatformRegion.ToUpper()));
        }

        var proRoster = await proQuery
            .Select(x => new
            {
                x.Puuid,
                x.PlatformRegion,
                x.GameName,
                x.TagLine,
                x.ProName,
                x.TeamName
            })
            .ToListAsync(ct);

        var trackedPuuids = proRoster
            .Select(x => x.Puuid)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (trackedPuuids.Count == 0)
            return new ChampionProBuildsResponse(championId, patch, normalizedRole, normalizedRegion, normalizedScope, [], [], []);

        var participantQuery = _context.MatchParticipants
            .AsNoTracking()
            .AsSplitQuery()
            .Include(mp => mp.Items)
            .Include(mp => mp.Runes)
            .Include(mp => mp.Summoner)
            .Where(mp => mp.ChampionId == championId)
            .OnPatch(patch)
            .FromSuccessfulMatches()
            .InRankedSoloQueue()
            .Where(mp => mp.Puuid != null && trackedPuuids.Contains(mp.Puuid));

        if (!string.Equals(normalizedRole, "ALL", StringComparison.Ordinal))
            participantQuery = participantQuery.Where(mp => mp.TeamPosition == normalizedRole);

        // Bound the heavy item/rune collection projection to the most-recent N rows so the wide
        // role=ALL + scope=all + region=ALL pool cannot command-timeout (the surface only renders
        // recent matches + aggregate top-players/common-builds, which a recency window represents).
        var maxParticipantRows = Math.Max(100, _options.ProBuildMaxParticipantRows);

        var rows = await participantQuery
            .OrderByDescending(mp => mp.Match.MatchDate)
            .ThenByDescending(mp => mp.Match.MatchId)
            .Take(maxParticipantRows)
            .Select(mp => new
            {
                mp.Match.MatchId,
                MatchGuid = mp.Match.Id,
                mp.Match.MatchDate,
                mp.Win,
                mp.ParticipantId,
                mp.SummonerSpell1Id,
                mp.SummonerSpell2Id,
                mp.Puuid,
                mp.Summoner.GameName,
                mp.Summoner.TagLine,
                Items = mp.Items.Select(i => i.ItemId).ToList(),
                Runes = mp.Runes.Select(r => new ChampionBuildPathBuilder.StoredRuneSelection(
                    r.RuneId,
                    r.SelectionTree,
                    r.SelectionIndex,
                    r.StyleId)).ToList()
            })
            .ToListAsync(ct);

        if (rows.Count == 0)
            return new ChampionProBuildsResponse(championId, patch, normalizedRole, normalizedRegion, normalizedScope, [], [], []);

        var rosterByPuuid = proRoster
            .GroupBy(x => x.Puuid, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var allRuneIds = rows
            .SelectMany(r => r.Runes.Select(x => x.RuneId))
            .Distinct()
            .ToList();

        var runeMetadata = await _context.RuneVersions
            .AsNoTracking()
            .Where(rv => allRuneIds.Contains(rv.RuneId) && rv.PatchVersion == patch)
            .Select(rv => new { rv.RuneId, rv.RunePathId, rv.Slot })
            .ToDictionaryAsync(rv => rv.RuneId, rv => new ChampionBuildPathBuilder.RuneMetadata(rv.RunePathId, rv.Slot), ct);

        // Ordered build path + skill orders for the projected pro matches (timeline-derived).
        var proMatchGuids = rows.Select(r => r.MatchGuid).Distinct().ToList();

        var proPurchasesByParticipant = (await _context.MatchParticipantItemPurchases
                .AsNoTracking()
                .Where(p => proMatchGuids.Contains(p.MatchId))
                .Select(p => new { p.MatchId, p.ParticipantId, p.PurchaseIndex, p.ItemId, p.Category })
                .ToListAsync(ct))
            .GroupBy(p => (p.MatchId, p.ParticipantId))
            .ToDictionary(
                g => g.Key,
                g => g.Where(x => x.Category != BuildItemCategory.Starter)
                    .OrderBy(x => x.PurchaseIndex)
                    .Select(x => x.ItemId)
                    .ToList());

        var proSkillByParticipant = (await _context.MatchParticipantSkillOrders
                .AsNoTracking()
                .Where(s => proMatchGuids.Contains(s.MatchId))
                .Select(s => new { s.MatchId, s.ParticipantId, s.FirstThree, s.MaxOrder })
                .ToListAsync(ct))
            .GroupBy(s => (s.MatchId, s.ParticipantId))
            .ToDictionary(g => g.Key, g => g.First());

        var projectedRows = rows
            .Select(r =>
            {
                var runeInfo = ChampionBuildPathBuilder.BuildRuneInfo(r.Runes, runeMetadata);
                rosterByPuuid.TryGetValue(r.Puuid ?? string.Empty, out var roster);
                var playerName = !string.IsNullOrWhiteSpace(roster?.ProName)
                    ? roster.ProName
                    : (r.GameName != null && r.TagLine != null ? $"{r.GameName}#{r.TagLine}" : r.GameName);

                // Covered rows use the cleaned, purchase-ordered path; uncovered rows fall back to the
                // raw inventory cleaned through the same completed-item filter (legacy exclusions) so
                // both branches yield comparable item sets and don't fragment commonBuilds grouping
                // during the timeline-backfill window.
                var orderedItems =
                    proPurchasesByParticipant.TryGetValue((r.MatchGuid, r.ParticipantId), out var purchasePath) && purchasePath.Count > 0
                        ? purchasePath
                        : ChampionBuildPathBuilder.NormalizeCompletedBuildItems(r.Items, ChampionBuildPathBuilder.EmptyItemMetadata, useLegacyFallback: true);

                proSkillByParticipant.TryGetValue((r.MatchGuid, r.ParticipantId), out var skill);

                return new
                {
                    r.MatchId,
                    r.MatchDate,
                    r.Win,
                    PlayerName = playerName,
                    TeamName = roster?.TeamName,
                    Items = orderedItems,
                    Spell1Id = r.SummonerSpell1Id,
                    Spell2Id = r.SummonerSpell2Id,
                    SkillOrder = skill is not null ? new SkillOrderDto(skill.FirstThree, skill.MaxOrder) : null,
                    RuneInfo = runeInfo
                };
            })
            .ToList();

        var recentMatches = projectedRows
            .OrderByDescending(r => r.MatchDate)
            .ThenByDescending(r => r.MatchId)
            .Take(25)
            .Select(r => new ProMatchBuildDto(
                r.MatchId ?? string.Empty,
                r.PlayerName,
                r.TeamName,
                r.Win,
                r.MatchDate,
                r.Items,
                r.RuneInfo.PrimaryStyleId,
                r.RuneInfo.SubStyleId,
                r.RuneInfo.PrimaryRunes,
                r.RuneInfo.SubRunes,
                r.RuneInfo.StatShards,
                r.Spell1Id,
                r.Spell2Id,
                r.SkillOrder))
            .ToList();

        var topPlayers = projectedRows
            .GroupBy(r => new { r.PlayerName, r.TeamName })
            .Select(g => new ProPlayerSummaryDto(
                g.Key.PlayerName,
                g.Key.TeamName,
                g.Count(),
                g.Count() > 0 ? (double)g.Count(x => x.Win) / g.Count() : 0.0))
            .OrderByDescending(p => p.Games)
            .ThenByDescending(p => p.WinRate)
            .Take(10)
            .ToList();

        // Group by the item set (sorted key) for stable grouping, but display a representative
        // member's purchase-ordered items.
        var commonBuilds = projectedRows
            .GroupBy(r => string.Join(",", r.Items.OrderBy(i => i)))
            .Select(g => new CommonProBuildDto(
                g.First().Items,
                g.Count(),
                g.Count() > 0 ? (double)g.Count(x => x.Win) / g.Count() : 0.0))
            .OrderByDescending(x => x.Games)
            .ThenByDescending(x => x.WinRate)
            .Take(10)
            .ToList();

        return new ChampionProBuildsResponse(
            championId,
            patch,
            normalizedRole,
            normalizedRegion,
            normalizedScope,
            recentMatches,
            topPlayers,
            commonBuilds);
    }

    public async Task<ProChampionPlayrateResponse> ComputeProChampionPlayrateAsync(
        string? region,
        string scope,
        string patch,
        CancellationToken ct)
    {
        var normalizedRegion = string.IsNullOrWhiteSpace(region) ? "ALL" : region.Trim().ToUpperInvariant();
        var normalizedScope = NormalizeProScope(scope);

        var rosterQuery = _context.TrackedProSummoners
            .AsNoTracking()
            .Where(x => x.IsActive);

        rosterQuery = normalizedScope switch
        {
            "highelo" => rosterQuery.Where(x => x.IsHighEloOtp),
            "all" => rosterQuery.Where(x => x.IsPro || x.IsHighEloOtp),
            _ => rosterQuery.Where(x => x.IsPro)
        };

        if (!string.Equals(normalizedRegion, "ALL", StringComparison.Ordinal))
        {
            var platforms = AnalyticsScopeMath.ResolvePlatformsForRegion(normalizedRegion);
            rosterQuery = rosterQuery.Where(x => platforms.Contains(x.PlatformRegion.ToUpper()));
        }

        var rosterPuuids = await rosterQuery
            .Select(x => x.Puuid)
            .ToListAsync(ct);

        var trackedPuuids = rosterPuuids
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (trackedPuuids.Count == 0)
            return new ProChampionPlayrateResponse(patch, normalizedRegion, normalizedScope, []);

        var rows = await _context.MatchParticipants
            .AsNoTracking()
            .OnPatch(patch)
            .FromSuccessfulMatches()
            .InRankedSoloQueue()
            .Where(mp => mp.Puuid != null && trackedPuuids.Contains(mp.Puuid))
            .Select(mp => new { mp.ChampionId, mp.Win, mp.Puuid })
            .ToListAsync(ct);

        if (rows.Count == 0)
            return new ProChampionPlayrateResponse(patch, normalizedRegion, normalizedScope, []);

        var champions = rows
            .GroupBy(r => r.ChampionId)
            .Select(g =>
            {
                var games = g.Count();
                var wins = g.Count(x => x.Win);
                return new ProChampionPlayrateDto(
                    g.Key,
                    games,
                    wins,
                    games > 0 ? (double)wins / games : 0.0,
                    g.Select(x => x.Puuid).Distinct().Count());
            })
            .OrderByDescending(c => c.Games)
            .ThenByDescending(c => c.WinRate)
            .ToList();

        return new ProChampionPlayrateResponse(patch, normalizedRegion, normalizedScope, champions);
    }

    public async Task<List<ProPlayerDto>> ComputeProRosterAsync(
        string? region,
        CancellationToken ct)
    {
        var normalizedRegion = string.IsNullOrWhiteSpace(region) ? "ALL" : region.Trim().ToUpperInvariant();

        var query = _context.TrackedProSummoners
            .AsNoTracking()
            .Where(x => x.IsActive && x.IsPro);

        if (!string.Equals(normalizedRegion, "ALL", StringComparison.Ordinal))
        {
            var platforms = AnalyticsScopeMath.ResolvePlatformsForRegion(normalizedRegion);
            query = query.Where(x => platforms.Contains(x.PlatformRegion.ToUpper()));
        }

        return await query
            .OrderBy(x => x.ProName ?? x.GameName)
            .Select(x => new ProPlayerDto(
                x.ProName,
                x.TeamName,
                x.PlatformRegion,
                x.GameName,
                x.TagLine))
            .ToListAsync(ct);
    }

    internal static string NormalizeProScope(string? scope) =>
        (scope ?? "all").Trim().ToLowerInvariant() switch
        {
            "pro" => "pro",
            "highelo" => "highelo",
            _ => "all"
        };

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
