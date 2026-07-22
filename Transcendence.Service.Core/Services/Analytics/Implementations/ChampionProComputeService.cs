using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Service.Core.Queries;
using Transcendence.Service.Core.Services.Analytics.Interfaces;
using Transcendence.Service.Core.Services.Analytics.Models;
using Transcendence.Service.Core.Services.StaticData.Models;

namespace Transcendence.Service.Core.Services.Analytics.Implementations;

/// <summary>
/// Raw + stats-backed computation for the pro / high-elo surfaces (pro builds, pro champion playrate, and
/// the public pro roster). Extracted from the original analytics compute service (P10.1) so this domain is
/// a focused unit; win rates / tier lists, builds, and matchups (<see cref="ChampionMatchupComputeService"/>)
/// each have their own service. Behavior is identical to the pre-extraction code — the analytics test suite
/// (raw + raw-vs-stats pro-surface equivalence) is the gate.
/// </summary>
public sealed class ChampionProComputeService : IChampionProComputeService
{
    private readonly TranscendenceContext _context;
    private readonly ChampionAnalyticsComputeOptions _options;

    public ChampionProComputeService(
        TranscendenceContext context,
        IOptions<ChampionAnalyticsComputeOptions> options)
    {
        _context = context;
        _options = options.Value;
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
                Runes = mp.Runes.Select(r => new StoredRuneSelection(
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
            .ToDictionaryAsync(rv => rv.RuneId, rv => new RuneSelectionMetadata(rv.RunePathId, rv.Slot), ct);

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
            .Where(r => r.Items.Count > 0)
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
            .GroupBy(mp => mp.ChampionId)
            .Select(g => new
            {
                ChampionId = g.Key,
                Games = g.Count(),
                Wins = g.Sum(mp => mp.Win ? 1 : 0),
                UniquePlayers = g.Select(mp => mp.Puuid).Distinct().Count()
            })
            .ToListAsync(ct);

        if (rows.Count == 0)
            return new ProChampionPlayrateResponse(patch, normalizedRegion, normalizedScope, []);

        var champions = rows
            .Select(row => new ProChampionPlayrateDto(
                row.ChampionId,
                row.Games,
                row.Wins,
                row.Games > 0 ? (double)row.Wins / row.Games : 0.0,
                row.UniquePlayers))
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

    public async Task<ChampionProBuildsResponse> ComputeProBuildsFromStatsAsync(
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

        // Precomputed only at the all-region scope for a specific role; everything else falls back to live.
        if (normalizedRegion == "ALL" && normalizedRole != "ALL")
        {
            var key = $"{championId}:{normalizedRole}:{normalizedScope}";
            var payload = await _context.AnalyticsResponseSnapshots.AsNoTracking()
                .Where(x => x.Feature == AnalyticsSnapshotSerialization.ProBuildsFeature && x.ScopeKey == key && x.Patch == patch)
                .Select(x => x.Payload)
                .FirstOrDefaultAsync(ct);

            if (payload != null)
            {
                var cached = AnalyticsSnapshotSerialization.Deserialize<ChampionProBuildsResponse>(payload);
                if (cached != null)
                    return cached;
            }
        }

        return await ComputeProBuildsAsync(championId, region, role, scope, patch, ct);
    }

    public async Task<ProChampionPlayrateResponse> ComputeProChampionPlayrateFromStatsAsync(
        string? region,
        string scope,
        string patch,
        CancellationToken ct)
    {
        var normalizedRegion = string.IsNullOrWhiteSpace(region) ? "ALL" : region.Trim().ToUpperInvariant();
        var normalizedScope = NormalizeProScope(scope);

        if (normalizedRegion == "ALL")
        {
            var payload = await _context.AnalyticsResponseSnapshots.AsNoTracking()
                .Where(x => x.Feature == AnalyticsSnapshotSerialization.ProPlayrateFeature && x.ScopeKey == normalizedScope && x.Patch == patch)
                .Select(x => x.Payload)
                .FirstOrDefaultAsync(ct);

            if (payload != null)
            {
                var cached = AnalyticsSnapshotSerialization.Deserialize<ProChampionPlayrateResponse>(payload);
                if (cached != null)
                    return cached;
            }
        }

        return await ComputeProChampionPlayrateAsync(region, scope, patch, ct);
    }
}
