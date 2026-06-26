using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Account;
using Transcendence.Data.Models.LoL.Analytics;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Service.Core.Queries;
using Transcendence.Service.Core.Services.Analytics.Interfaces;

namespace Transcendence.Service.Core.Services.Analytics.Implementations;

/// <summary>
/// Rebuilds the tabular-core precomputed analytics aggregates from raw match data. See
/// <see cref="IPrecomputedAnalyticsRefresher"/>. Every aggregation mirrors the filters of the live
/// compute (<c>ChampionAnalyticsComputeService</c>) so the read path can roll these atoms up to the exact
/// same numbers:
/// <list type="bullet">
/// <item><c>ChampionRoleTierStat</c>: a LEFT JOIN to the current solo rank gives each participant a tier
/// ("UNRANKED" when absent); grouped by (region, tier, champion, role) → additive Games/Wins.</item>
/// <item><c>ScopeMatchCountStat</c> / <c>ChampionBanScopeStat</c>: distinct-match denominators/numerators
/// per rank-scope token. These are NOT additive over tier or region, so an explicit synthetic
/// PlatformRegion="ALL" row is materialized (global, no region filter) for the region=ALL read, alongside
/// per-platform rows; the read point-looks-up, never sums. Scope membership uses the same EXISTS form as
/// the live <c>ApplyRankTierScopeToParticipants</c>.</item>
/// </list>
/// Region "ALL" is a reserved synthetic token; <see cref="AllRegion"/>. A null Summoner.PlatformRegion is
/// coalesced to "" (a bucket only the region=ALL roll-up ever includes).
/// </summary>
public class PrecomputedAnalyticsRefresher : IPrecomputedAnalyticsRefresher
{
    /// <summary>Synthetic PlatformRegion value for the global (region-unfiltered) distinct-match rows.</summary>
    public const string AllRegion = "ALL";

    private const string RankedSoloQueueType = "RANKED_SOLO_5x5";

    /// <summary>Minimum (champion, role) games on a patch before a build snapshot is computed (mirrors the build sample floor).</summary>
    private const int MinBuildGames = 30;

    /// <summary>The rank scopes precomputed for builds: the page default + all-ranks. Specific tiers fall back to raw.</summary>
    private static readonly (string Scope, string? RankTier)[] BuildScopes =
        [(RankTierCatalog.EmeraldPlusScope, RankTierCatalog.EmeraldPlusScope), (RankTierCatalog.AllScope, null)];

    private readonly TranscendenceContext _context;
    private readonly IChampionAnalyticsComputeService _computeService;
    private readonly ILogger<PrecomputedAnalyticsRefresher> _logger;

    public PrecomputedAnalyticsRefresher(
        TranscendenceContext context,
        IChampionAnalyticsComputeService computeService,
        ILogger<PrecomputedAnalyticsRefresher> logger)
    {
        _context = context;
        _computeService = computeService;
        _logger = logger;
    }

    public async Task<PrecomputedAnalyticsRefreshResult> RefreshTabularCoreAsync(string patch, CancellationToken ct)
    {
        var computedAt = DateTime.UtcNow;

        var roleTierRows = await BuildRoleTierStatsAsync(patch, computedAt, ct);
        var (scopeMatchRows, banRows) = await BuildScopeStatsAsync(patch, computedAt, ct);

        await using var tx = await _context.Database.BeginTransactionAsync(ct);

        // Replace this patch's rows transactionally: a reader sees either the whole previous snapshot or the
        // whole new one, never a half-written patch.
        await _context.ChampionRoleTierStats.Where(x => x.Patch == patch).ExecuteDeleteAsync(ct);
        await _context.ScopeMatchCountStats.Where(x => x.Patch == patch).ExecuteDeleteAsync(ct);
        await _context.ChampionBanScopeStats.Where(x => x.Patch == patch).ExecuteDeleteAsync(ct);

        _context.ChampionRoleTierStats.AddRange(roleTierRows);
        _context.ScopeMatchCountStats.AddRange(scopeMatchRows);
        _context.ChampionBanScopeStats.AddRange(banRows);
        await _context.SaveChangesAsync(ct);

        await tx.CommitAsync(ct);

        _logger.LogInformation(
            "Precompute refresh (tabular core) patch {Patch}: {RoleTier} role-tier, {ScopeMatch} scope-match, {Ban} ban rows",
            patch, roleTierRows.Count, scopeMatchRows.Count, banRows.Count);

        return new PrecomputedAnalyticsRefreshResult(roleTierRows.Count, scopeMatchRows.Count, banRows.Count);
    }

    // ---- ChampionMatchupStat: all-region lane-pair aggregates per (champion-tier, champion, role, opponent) ----

    public async Task<int> RefreshMatchupsAsync(string patch, CancellationToken ct)
    {
        const int minuteMark = 15;
        var computedAt = DateTime.UtcNow;

        // Champion side: every ranked-solo participant with an assigned role, tagged with its current solo
        // tier (LEFT JOIN -> "UNRANKED"). Mirrors ComputeMatchupsAsync's champion side, aggregated over all
        // champions/roles at once. EF global query filters (Match/MatchParticipant/TimelineSnapshot status)
        // apply via the DbSets, matching the live read.
        var championSide =
            from mp in BaseParticipants(patch)
            join rank in _context.Ranks.AsNoTracking().Where(r => r.QueueType == RankedSoloQueueType)
                on mp.SummonerId equals rank.SummonerId into rankGroup
            from soloRank in rankGroup.DefaultIfEmpty()
            select new
            {
                mp.MatchId,
                mp.Win,
                mp.ChampionId,
                Role = mp.TeamPosition!,
                mp.TeamId,
                mp.ParticipantId,
                Tier = soloRank != null ? soloRank.Tier : RankTierCatalog.Unranked
            };

        // Lane pair: same lane (TeamPosition), opposite team. Opponent side is unfiltered (it inherits
        // patch/status/queue transitively via the shared MatchId).
        var lanePairs =
            from champion in championSide
            join opponent in _context.MatchParticipants.AsNoTracking()
                on champion.MatchId equals opponent.MatchId
            where champion.Role == opponent.TeamPosition && champion.TeamId != opponent.TeamId
            select new
            {
                champion.MatchId,
                champion.Win,
                champion.ChampionId,
                champion.Role,
                champion.Tier,
                ChampionParticipantId = champion.ParticipantId,
                OpponentChampionId = opponent.ChampionId,
                OpponentParticipantId = opponent.ParticipantId
            };

        var timeline = _context.MatchParticipantTimelineSnapshots.AsNoTracking()
            .Where(s => s.MinuteMark == minuteMark);

        var grouped = await (
            from pair in lanePairs
            join championTimelineRow in timeline
                on new { pair.MatchId, ParticipantId = pair.ChampionParticipantId }
                equals new { championTimelineRow.MatchId, championTimelineRow.ParticipantId }
                into championTimelineRows
            from championTimeline in championTimelineRows.DefaultIfEmpty()
            join opponentTimelineRow in timeline
                on new { pair.MatchId, ParticipantId = pair.OpponentParticipantId }
                equals new { opponentTimelineRow.MatchId, opponentTimelineRow.ParticipantId }
                into opponentTimelineRows
            from opponentTimeline in opponentTimelineRows.DefaultIfEmpty()
            group new { pair, championTimeline, opponentTimeline } by new
            {
                pair.Tier,
                pair.ChampionId,
                pair.Role,
                pair.OpponentChampionId
            }
            into g
            select new
            {
                g.Key.Tier,
                g.Key.ChampionId,
                g.Key.Role,
                g.Key.OpponentChampionId,
                Games = g.Count(),
                Wins = g.Sum(x => x.pair.Win ? 1 : 0),
                TimelineGames = g.Count(x => x.championTimeline != null && x.opponentTimeline != null),
                SumGoldDiffAt15 = g
                    .Where(x => x.championTimeline != null && x.opponentTimeline != null)
                    .Select(x => (long)(x.championTimeline!.Gold - x.opponentTimeline!.Gold))
                    .Sum(),
                SumXpDiffAt15 = g
                    .Where(x => x.championTimeline != null && x.opponentTimeline != null)
                    .Select(x => (long)(x.championTimeline!.Xp - x.opponentTimeline!.Xp))
                    .Sum(),
                LatestTimelineAtUtc = g
                    .Where(x => x.championTimeline != null)
                    .Select(x => (DateTime?)x.championTimeline!.DerivedAtUtc)
                    .Max()
            })
            .ToListAsync(ct);

        var rows = grouped.Select(g => new ChampionMatchupStat
        {
            Patch = patch,
            RankTier = g.Tier,
            ChampionId = g.ChampionId,
            Role = g.Role,
            OpponentChampionId = g.OpponentChampionId,
            Games = g.Games,
            Wins = g.Wins,
            TimelineGames = g.TimelineGames,
            SumGoldDiffAt15 = g.SumGoldDiffAt15,
            SumXpDiffAt15 = g.SumXpDiffAt15,
            LatestTimelineAtUtc = g.LatestTimelineAtUtc,
            ComputedAtUtc = computedAt
        }).ToList();

        await using var tx = await _context.Database.BeginTransactionAsync(ct);
        await _context.ChampionMatchupStats.Where(x => x.Patch == patch).ExecuteDeleteAsync(ct);
        _context.ChampionMatchupStats.AddRange(rows);
        await _context.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        _logger.LogInformation("Precompute refresh (matchups) patch {Patch}: {Rows} rows", patch, rows.Count);
        return rows.Count;
    }

    // ---- ChampionBuildSnapshot: durable per-(champion, role, scope) build response (all-region) ----

    public async Task<int> RefreshBuildsAsync(string patch, CancellationToken ct)
    {
        var computedAt = DateTime.UtcNow;

        // Played (champion, role) pairs with enough games to produce a build (mirrors the build sample floor).
        var pairs = await _context.ChampionRoleTierStats
            .AsNoTracking()
            .Where(x => x.Patch == patch)
            .GroupBy(x => new { x.ChampionId, x.Role })
            .Select(g => new { g.Key.ChampionId, g.Key.Role, Games = g.Sum(x => x.Games) })
            .Where(x => x.Games >= MinBuildGames)
            .ToListAsync(ct);

        // Compute every (pair, scope) response first (reads), then replace the patch's rows transactionally.
        var rows = new List<ChampionBuildSnapshot>(pairs.Count * BuildScopes.Length);
        foreach (var pair in pairs)
        {
            foreach (var (scope, rankTier) in BuildScopes)
            {
                var response = await _computeService.ComputeBuildsAsync(
                    pair.ChampionId, pair.Role, rankTier, region: null, patch, ct);

                rows.Add(new ChampionBuildSnapshot
                {
                    Patch = patch,
                    ChampionId = pair.ChampionId,
                    Role = pair.Role,
                    RankScope = scope,
                    Payload = BuildSnapshotSerialization.Serialize(response),
                    ComputedAtUtc = computedAt
                });
            }
        }

        await using var tx = await _context.Database.BeginTransactionAsync(ct);
        await _context.ChampionBuildSnapshots.Where(x => x.Patch == patch).ExecuteDeleteAsync(ct);
        _context.ChampionBuildSnapshots.AddRange(rows);
        await _context.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        _logger.LogInformation("Precompute refresh (builds) patch {Patch}: {Rows} snapshots ({Pairs} champion-roles)",
            patch, rows.Count, pairs.Count);
        return rows.Count;
    }

    // ---- AnalyticsResponseSnapshot: durable pro-builds + pro-playrate responses (all-region) ----

    public async Task<int> RefreshProSurfacesAsync(string patch, CancellationToken ct)
    {
        var computedAt = DateTime.UtcNow;
        var rows = new List<AnalyticsResponseSnapshot>();

        // Pro-playrate: one response per roster scope, all-region.
        foreach (var scope in AnalyticsSnapshotSerialization.ProScopes)
        {
            var response = await _computeService.ComputeProChampionPlayrateAsync(region: null, scope, patch, ct);
            rows.Add(new AnalyticsResponseSnapshot
            {
                Feature = AnalyticsSnapshotSerialization.ProPlayrateFeature,
                ScopeKey = scope,
                Patch = patch,
                Payload = AnalyticsSnapshotSerialization.Serialize(response),
                ComputedAtUtc = computedAt
            });
        }

        // Pro-builds: per pro-played (champion, role) x roster scope, all-region. Enumerate the (champion,
        // role) pairs the active roster (pro OR high-elo) actually plays — much smaller than the general
        // population — and precompute each scope's response (pro/highelo subsets may be empty; that's fine).
        var rosterPuuids = await _context.TrackedProSummoners
            .AsNoTracking()
            .Where(x => x.IsActive && (x.IsPro || x.IsHighEloOtp))
            .Select(x => x.Puuid)
            .Where(p => p != null && p != "")
            .Distinct()
            .ToListAsync(ct);

        var pairs = rosterPuuids.Count == 0
            ? []
            : await BaseParticipants(patch)
                .Where(mp => mp.Puuid != null && rosterPuuids.Contains(mp.Puuid))
                .Select(mp => new { mp.ChampionId, Role = mp.TeamPosition! })
                .Distinct()
                .ToListAsync(ct);

        foreach (var pair in pairs)
        {
            foreach (var scope in AnalyticsSnapshotSerialization.ProScopes)
            {
                var response = await _computeService.ComputeProBuildsAsync(
                    pair.ChampionId, region: null, pair.Role, scope, patch, ct);
                rows.Add(new AnalyticsResponseSnapshot
                {
                    Feature = AnalyticsSnapshotSerialization.ProBuildsFeature,
                    ScopeKey = $"{pair.ChampionId}:{pair.Role}:{scope}",
                    Patch = patch,
                    Payload = AnalyticsSnapshotSerialization.Serialize(response),
                    ComputedAtUtc = computedAt
                });
            }
        }

        await using var tx = await _context.Database.BeginTransactionAsync(ct);
        await _context.AnalyticsResponseSnapshots.Where(x => x.Patch == patch).ExecuteDeleteAsync(ct);
        _context.AnalyticsResponseSnapshots.AddRange(rows);
        await _context.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        _logger.LogInformation("Precompute refresh (pro surfaces) patch {Patch}: {Rows} snapshots ({Pairs} pro champion-roles)",
            patch, rows.Count, pairs.Count);
        return rows.Count;
    }

    // ---- ChampionRoleTierStat: per (region, current-tier, champion, role) Games/Wins (additive) ----

    private async Task<List<ChampionRoleTierStat>> BuildRoleTierStatsAsync(
        string patch, DateTime computedAt, CancellationToken ct)
    {
        var participants = BaseParticipants(patch);

        var grouped = await (
            from mp in participants
            join rank in _context.Ranks.AsNoTracking().Where(r => r.QueueType == RankedSoloQueueType)
                on mp.SummonerId equals rank.SummonerId into rankGroup
            from soloRank in rankGroup.DefaultIfEmpty()
            select new
            {
                Region = mp.Summoner.PlatformRegion,
                Tier = soloRank != null ? soloRank.Tier : RankTierCatalog.Unranked,
                mp.ChampionId,
                Role = mp.TeamPosition!,
                mp.Win
            })
            .GroupBy(x => new { x.Region, x.Tier, x.ChampionId, x.Role })
            .Select(g => new
            {
                g.Key.Region,
                g.Key.Tier,
                g.Key.ChampionId,
                g.Key.Role,
                Games = g.Count(),
                Wins = g.Sum(x => x.Win ? 1 : 0)
            })
            .ToListAsync(ct);

        return grouped
            .Select(g => new ChampionRoleTierStat
            {
                Patch = patch,
                PlatformRegion = g.Region ?? "",
                RankTier = g.Tier,
                ChampionId = g.ChampionId,
                Role = g.Role,
                Games = g.Games,
                Wins = g.Wins,
                ComputedAtUtc = computedAt
            })
            .ToList();
    }

    // ---- ScopeMatchCountStat + ChampionBanScopeStat: distinct-match denominators/numerators per scope ----

    private async Task<(List<ScopeMatchCountStat> ScopeMatches, List<ChampionBanScopeStat> Bans)> BuildScopeStatsAsync(
        string patch, DateTime computedAt, CancellationToken ct)
    {
        var scopeMatchRows = new List<ScopeMatchCountStat>();
        var banRows = new List<ChampionBanScopeStat>();

        foreach (var scope in RankTierCatalog.RankScopeTokens)
        {
            var scoped = ApplyScope(BaseParticipants(patch), scope);

            // (region, matchId) distinct pairs in scope — region from the participant's summoner.
            var regionMatches = scoped
                .Select(mp => new { Region = mp.Summoner.PlatformRegion, mp.MatchId })
                .Distinct();

            // Per-platform distinct-match counts.
            var perRegion = await regionMatches
                .GroupBy(x => x.Region)
                .Select(g => new { Region = g.Key, Total = g.Count() })
                .ToListAsync(ct);

            foreach (var r in perRegion)
            {
                scopeMatchRows.Add(new ScopeMatchCountStat
                {
                    Patch = patch,
                    PlatformRegion = r.Region ?? "",
                    RankScope = scope,
                    TotalMatches = r.Total,
                    ComputedAtUtc = computedAt
                });
            }

            // Global (region=ALL): distinct over the scope ignoring region — NOT the per-region sum, since a
            // match whose participants span regions would be counted once globally but once per region.
            var allTotal = await scoped.Select(mp => mp.MatchId).Distinct().CountAsync(ct);
            if (allTotal > 0)
            {
                scopeMatchRows.Add(new ScopeMatchCountStat
                {
                    Patch = patch,
                    PlatformRegion = AllRegion,
                    RankScope = scope,
                    TotalMatches = allTotal,
                    ComputedAtUtc = computedAt
                });
            }

            // Ban numerator per (region, champion): distinct banned matches among the scope's matches.
            var bansPerRegion = await (
                from rm in regionMatches
                join b in _context.MatchBans.AsNoTracking() on rm.MatchId equals b.MatchId
                group rm by new { rm.Region, b.ChampionId } into g
                select new
                {
                    g.Key.Region,
                    g.Key.ChampionId,
                    Banned = g.Select(x => x.MatchId).Distinct().Count()
                })
                .ToListAsync(ct);

            foreach (var b in bansPerRegion)
            {
                banRows.Add(new ChampionBanScopeStat
                {
                    Patch = patch,
                    PlatformRegion = b.Region ?? "",
                    RankScope = scope,
                    ChampionId = b.ChampionId,
                    BannedMatches = b.Banned,
                    ComputedAtUtc = computedAt
                });
            }

            // Global (region=ALL) ban numerator: distinct banned matches over the scope ignoring region.
            var scopedMatchIds = scoped.Select(mp => mp.MatchId).Distinct();
            var bansAll = await _context.MatchBans.AsNoTracking()
                .Where(b => scopedMatchIds.Contains(b.MatchId))
                .GroupBy(b => b.ChampionId)
                .Select(g => new { ChampionId = g.Key, Banned = g.Select(x => x.MatchId).Distinct().Count() })
                .ToListAsync(ct);

            foreach (var b in bansAll)
            {
                banRows.Add(new ChampionBanScopeStat
                {
                    Patch = patch,
                    PlatformRegion = AllRegion,
                    RankScope = scope,
                    ChampionId = b.ChampionId,
                    BannedMatches = b.Banned,
                    ComputedAtUtc = computedAt
                });
            }
        }

        return (scopeMatchRows, banRows);
    }

    private IQueryable<MatchParticipant> BaseParticipants(string patch) =>
        _context.MatchParticipants
            .AsNoTracking()
            .OnPatch(patch)
            .FromSuccessfulMatches()
            .InRankedSoloQueue()
            .WithAssignedRole();

    /// <summary>
    /// Restricts participants to those whose <i>own</i> current solo rank is in the scope, mirroring the
    /// live <c>ApplyRankTierScopeToParticipants</c> EXISTS form. "ALL" applies no filter (includes unranked).
    /// </summary>
    private IQueryable<MatchParticipant> ApplyScope(IQueryable<MatchParticipant> query, string scope)
    {
        if (scope == RankTierCatalog.AllScope)
            return query;

        var ranks = _context.Ranks.AsNoTracking();

        if (scope == RankTierCatalog.EmeraldPlusScope)
        {
            return query.Where(mp => ranks.Any(r =>
                r.QueueType == RankedSoloQueueType &&
                r.SummonerId == mp.SummonerId &&
                RankTierCatalog.EmeraldPlusTiers.Contains(r.Tier)));
        }

        // Exact tier.
        return query.Where(mp => ranks.Any(r =>
            r.QueueType == RankedSoloQueueType &&
            r.SummonerId == mp.SummonerId &&
            r.Tier == scope));
    }
}
