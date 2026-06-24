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

    private readonly TranscendenceContext _context;
    private readonly ILogger<PrecomputedAnalyticsRefresher> _logger;

    public PrecomputedAnalyticsRefresher(
        TranscendenceContext context,
        ILogger<PrecomputedAnalyticsRefresher> logger)
    {
        _context = context;
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
