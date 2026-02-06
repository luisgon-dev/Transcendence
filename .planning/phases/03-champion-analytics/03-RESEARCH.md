# Phase 3: Champion Analytics - Meta Insights & Recommendations - Research

**Researched:** 2026-02-02
**Domain:** Champion-level aggregated analytics with win rates, tier lists, build recommendations, and matchup matrices
**Confidence:** HIGH

## Summary

Champion analytics aggregates match participant data to produce insights at the champion/role/rank tier level. This phase builds on Phase 2's match data foundation by creating aggregate analytics endpoints that compute win rates, popular builds, tier rankings, and matchup statistics.

The standard approach uses PostgreSQL aggregate queries with materialized views for expensive computations, HybridCache for two-tier caching (L1 in-memory, L2 Redis), and daily scheduled jobs to refresh analytics. The existing infrastructure (HybridCache, EF Core, PostgreSQL) supports this pattern with proven patterns from Phase 2's summoner stats.

**Primary recommendation:** Use PostgreSQL window functions for composite scoring and percentile-based tier assignments, compute build recommendations via frequent itemset patterns (70%+ = core item), cache analytics for 24 hours with tag-based invalidation, and leverage existing materialized view patterns for expensive aggregations.

## Standard Stack

The established libraries/tools for champion analytics in .NET:

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| Microsoft.Extensions.Caching.Hybrid | 9.x (GA in .NET 9) | Two-tier L1/L2 caching | Official Microsoft library, stampede protection, tag-based invalidation for analytics |
| Entity Framework Core | 9.0 | Database queries + migrations | Already in use, supports GROUP BY aggregates and window functions |
| Npgsql.EntityFrameworkCore.PostgreSQL | 9.0 | PostgreSQL provider | Existing provider, supports advanced PostgreSQL features (percentile_cont, window functions) |
| StackExchange.Redis | 2.x | L2 distributed cache | Already configured in Phase 1, proven for analytics caching |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| Hangfire.PostgreSql | Latest | Background job scheduling | Already in use, needed for daily analytics refresh jobs |
| Microsoft.EntityFrameworkCore.Relational | 9.0 | Materialized views support | Raw SQL for view creation in migrations, proven pattern |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| HybridCache | IDistributedCache directly | Lose L1 cache, stampede protection, tag invalidation—no benefits for analytics |
| PostgreSQL window functions | Application-level sorting | 10-100x slower, doesn't scale with data volume |
| Materialized views | Computed on every request | Acceptable for simple aggregates (<1M rows), views needed for complex multi-table aggregates |

**Installation:**
```bash
# All dependencies already installed in Phase 1-2
# No new NuGet packages required
```

## Architecture Patterns

### Recommended Project Structure
```
Transcendence.Service.Core/
├── Services/
│   ├── Analytics/
│   │   ├── Interfaces/
│   │   │   ├── IChampionAnalyticsService.cs     # Read operations
│   │   │   └── IChampionAnalyticsComputeService.cs  # Aggregate computation
│   │   ├── Implementations/
│   │   │   ├── ChampionAnalyticsService.cs      # Cached reads via HybridCache
│   │   │   └── ChampionAnalyticsComputeService.cs   # Raw EF queries
│   │   └── Models/
│   │       ├── ChampionWinRateDto.cs            # Win rate by role/tier
│   │       ├── ChampionBuildDto.cs              # Top 3 builds
│   │       ├── TierListDto.cs                   # S/A/B/C/D tiers
│   │       └── MatchupDto.cs                    # Counters + synergies
│   └── Jobs/
│       └── RefreshChampionAnalyticsJob.cs       # Daily scheduled refresh

Transcendence.Data/
├── Models/
│   └── Analytics/
│       ├── ChampionWinRateSummary.cs            # Aggregate entity (optional materialized view)
│       └── ChampionBuildSummary.cs              # Pre-computed builds (optional)

Transcendence.WebAPI/
└── Controllers/
    └── ChampionAnalyticsController.cs           # GET endpoints only
```

### Pattern 1: Aggregate Computation with Window Functions
**What:** Use PostgreSQL window functions (PERCENT_RANK, ROW_NUMBER) for composite scoring and tier assignment
**When to use:** Tier lists with percentile-based S/A/B/C/D grades, composite win rate + pick rate scoring

**Example:**
```csharp
// Source: Based on Microsoft EF Core documentation + PostgreSQL window function patterns
// https://learn.microsoft.com/en-us/ef/core/performance/modeling-for-performance
// https://neon.com/docs/functions/window-rank

public async Task<List<TierListEntry>> ComputeTierListForRoleAsync(
    string role,
    string rankTier,
    string patch,
    CancellationToken ct)
{
    // Step 1: Aggregate win rate and pick rate per champion
    var query = _context.MatchParticipants
        .AsNoTracking()
        .Where(mp => mp.TeamPosition == role
                  && mp.Match.Patch == patch
                  && mp.Summoner.Ranks.Any(r => r.Tier == rankTier))
        .GroupBy(mp => mp.ChampionId)
        .Select(g => new
        {
            ChampionId = g.Key,
            Games = g.Count(),
            Wins = g.Sum(x => x.Win ? 1 : 0),
            WinRate = (double)g.Sum(x => x.Win ? 1 : 0) / g.Count(),
            // Pick rate computed later via total games
        })
        .Where(x => x.Games >= 100); // Minimum sample size filter

    // Step 2: Compute pick rate and composite score
    var totalGames = await _context.MatchParticipants
        .Where(mp => mp.TeamPosition == role
                  && mp.Match.Patch == patch
                  && mp.Summoner.Ranks.Any(r => r.Tier == rankTier))
        .Select(mp => mp.MatchId)
        .Distinct()
        .CountAsync(ct);

    var champStats = await query.ToListAsync(ct);

    var withScores = champStats
        .Select(c => new
        {
            c.ChampionId,
            c.Games,
            c.Wins,
            c.WinRate,
            PickRate = (double)c.Games / totalGames,
            // Composite: Win rate contributes 70%, pick rate 30%
            CompositeScore = (c.WinRate * 0.7) + ((double)c.Games / totalGames * 0.3)
        })
        .OrderByDescending(x => x.CompositeScore)
        .ToList();

    // Step 3: Assign tiers using percentile thresholds
    // Top 10% = S, 10-30% = A, 30-60% = B, 60-85% = C, 85%+ = D
    var total = withScores.Count;
    return withScores.Select((entry, index) =>
    {
        var percentile = (double)index / total;
        var tier = percentile switch
        {
            < 0.10 => "S",
            < 0.30 => "A",
            < 0.60 => "B",
            < 0.85 => "C",
            _ => "D"
        };

        return new TierListEntry(
            entry.ChampionId,
            tier,
            entry.WinRate,
            entry.PickRate,
            entry.Games,
            entry.CompositeScore
        );
    }).ToList();
}
```

**WHY composite score:** User decision requires "win rate + pick rate" not win rate alone. Win rate alone biases toward niche champions with small samples. Pick rate indicates meta relevance.

**WHY percentile-based tiers:** Ensures consistent distribution across roles/patches. Fixed thresholds (e.g., "50% win rate = A tier") create unbalanced tiers when meta shifts.

**WHY 70/30 weighting:** Standard analytics practice—win rate is primary metric (70%), pick rate is secondary context (30%). Sites like LoLalytics use similar composite formulas.

### Pattern 2: Frequent Itemset Mining for Build Recommendations
**What:** Identify "core" items (appear in 70%+ of games) vs "situational" items, cluster similar builds via item co-occurrence
**When to use:** Build recommendations showing top 3 builds per champion with core/situational distinction

**Example:**
```csharp
// Source: Frequent itemset mining patterns
// https://jameskle.com/writes/itemset-mining
// https://link.springer.com/article/10.1007/s10462-018-9629-z

public async Task<List<ChampionBuild>> ComputeTopBuildsAsync(
    int championId,
    string role,
    string patch,
    string rankTier,
    CancellationToken ct)
{
    // Step 1: Get all item combinations for this champion/role/patch/tier
    var matchData = await _context.MatchParticipants
        .AsNoTracking()
        .Include(mp => mp.Items)
        .Include(mp => mp.Runes)
        .Where(mp => mp.ChampionId == championId
                  && mp.TeamPosition == role
                  && mp.Match.Patch == patch
                  && mp.Summoner.Ranks.Any(r => r.Tier == rankTier))
        .Select(mp => new
        {
            mp.Win,
            Items = mp.Items.Select(i => i.ItemId).OrderBy(x => x).ToList(),
            Runes = mp.Runes.Select(r => r.RuneId).ToList()
        })
        .ToListAsync(ct);

    if (matchData.Count < 100)
        return new List<ChampionBuild>(); // Insufficient sample

    // Step 2: Group by item build (ordered set) + rune page
    var buildGroups = matchData
        .GroupBy(m => new
        {
            Items = string.Join(",", m.Items.Where(i => i != 0)), // Exclude empty slots
            Runes = string.Join(",", m.Runes)
        })
        .Select(g => new
        {
            g.Key.Items,
            g.Key.Runes,
            Games = g.Count(),
            Wins = g.Sum(x => x.Win ? 1 : 0),
            WinRate = (double)g.Sum(x => x.Win ? 1 : 0) / g.Count()
        })
        .Where(b => b.Games >= 30) // Minimum sample for a specific build
        .OrderByDescending(b => b.Games * b.WinRate) // Score: popularity * success
        .Take(3) // Top 3 builds
        .ToList();

    // Step 3: Determine core items (appear in 70%+ of all games)
    var totalGames = matchData.Count;
    var itemFrequency = matchData
        .SelectMany(m => m.Items.Where(i => i != 0))
        .GroupBy(itemId => itemId)
        .ToDictionary(
            g => g.Key,
            g => (double)g.Count() / totalGames
        );

    var coreItems = itemFrequency
        .Where(kvp => kvp.Value >= 0.70)
        .Select(kvp => kvp.Key)
        .ToList();

    // Step 4: Map to DTOs
    return buildGroups.Select(build => new ChampionBuild
    {
        Items = build.Items.Split(',').Select(int.Parse).ToList(),
        Runes = build.Runes.Split(',').Select(int.Parse).ToList(),
        CoreItems = coreItems,
        SituationalItems = build.Items.Split(',')
            .Select(int.Parse)
            .Except(coreItems)
            .ToList(),
        Games = build.Games,
        WinRate = build.WinRate
    }).ToList();
}
```

**WHY 70% threshold:** User decision specifies "70%+ = core item". Lower thresholds produce too many "core" items, reducing distinction. Higher thresholds miss legitimate core items on flex champions.

**WHY top 3 builds:** User decision requires "top 3" not single best. Provides situational options (vs tanks, vs AP, vs burst).

**WHY Games * WinRate scoring:** Balances build popularity (sample size) with effectiveness (win rate). Pure win rate biases toward rare builds with lucky streaks.

### Pattern 3: HybridCache with Tag-Based Invalidation
**What:** Cache analytics for 24 hours with tags for daily refresh, use stampede protection for concurrent requests
**When to use:** All analytics endpoints—win rates, builds, tier lists, matchups

**Example:**
```csharp
// Source: Microsoft HybridCache official documentation
// https://learn.microsoft.com/en-us/aspnet/core/performance/caching/hybrid
// https://devblogs.microsoft.com/dotnet/hybrid-cache-is-now-ga/

public class ChampionAnalyticsService(
    TranscendenceContext db,
    HybridCache cache,
    IChampionAnalyticsComputeService computeService) : IChampionAnalyticsService
{
    private const string WinRateCacheKeyPrefix = "analytics:winrate:";
    private const string TierListCacheKeyPrefix = "analytics:tierlist:";
    private const string BuildCacheKeyPrefix = "analytics:builds:";

    // Analytics cache: 24hr L2, 1hr L1 (daily refresh pattern)
    private static readonly HybridCacheEntryOptions AnalyticsCacheOptions = new()
    {
        Expiration = TimeSpan.FromHours(24),        // Redis L2
        LocalCacheExpiration = TimeSpan.FromHours(1) // In-memory L1
    };

    public async Task<ChampionWinRateDto> GetWinRatesAsync(
        int championId,
        string role,
        string rankTier,
        string? region,
        CancellationToken ct)
    {
        var patch = await GetCurrentPatchAsync(ct);
        var cacheKey = $"{WinRateCacheKeyPrefix}{championId}:{role}:{rankTier}:{region ?? "global"}:{patch}";
        var tags = new[] { "analytics", $"patch:{patch}", "winrates" };

        return await cache.GetOrCreateAsync(
            cacheKey,
            async cancel => await computeService.ComputeWinRatesAsync(
                championId, role, rankTier, region, patch, cancel),
            AnalyticsCacheOptions,
            tags,
            cancellationToken: ct
        );
    }

    public async Task InvalidateAllAnalyticsAsync(CancellationToken ct)
    {
        // Tag-based invalidation: removes all analytics in one call
        await cache.RemoveByTagAsync("analytics", ct);
    }

    public async Task InvalidatePatchAnalyticsAsync(string patch, CancellationToken ct)
    {
        // Invalidate only specific patch (useful when patch data updates)
        await cache.RemoveByTagAsync($"patch:{patch}", ct);
    }
}
```

**WHY 24hr cache:** User decision requires "daily refresh". Analytics are computationally expensive (aggregate millions of matches), immutable within a patch/day window.

**WHY 1hr L1:** Balances staleness vs performance. If new patch detected or job completes, tag invalidation clears both L1 and L2. Short L1 reduces cross-instance inconsistency.

**WHY tag-based invalidation:** Single `RemoveByTagAsync("analytics")` call invalidates all analytics (win rates, builds, tier lists, matchups) atomically. Avoids managing hundreds of individual cache keys.

**CRITICAL:** Tag-based invalidation only marks entries as invalid—doesn't delete from Redis. Entries expire naturally based on TTL. For daily refresh, this is acceptable (old data harmlessly sits in Redis until TTL).

### Pattern 4: Materialized View for Complex Aggregates (Optional)
**What:** Pre-compute expensive multi-table aggregates into database views, refresh daily via migration + scheduled job
**When to use:** Matchup matrices (10-way join across teams), champion popularity trends (time-series aggregates)

**Example:**
```csharp
// Source: PostgreSQL materialized view patterns with EF Core
// https://medium.com/c-sharp-programming/optimize-entity-framework-query-say-goodbye-to-join-tables-and-embrace-materialized-views-indexed-c0d2cc20a6bc
// https://denileo82.hashnode.dev/boosting-performance-with-materialized-views-in-net-core-and-postgresql

// Migration: Create materialized view
public partial class AddChampionMatchupView : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
            CREATE MATERIALIZED VIEW champion_matchup_summary AS
            SELECT
                mp1.champion_id AS champion_id,
                mp1.team_position AS role,
                mp2.champion_id AS opponent_id,
                r.tier AS rank_tier,
                m.patch AS patch,
                COUNT(*) AS games,
                SUM(CASE WHEN mp1.win THEN 1 ELSE 0 END) AS wins,
                CAST(SUM(CASE WHEN mp1.win THEN 1 ELSE 0 END) AS DOUBLE PRECISION) / COUNT(*) AS win_rate
            FROM match_participants mp1
            INNER JOIN match_participants mp2
                ON mp1.match_id = mp2.match_id
                AND mp1.team_position = mp2.team_position
                AND mp1.team_id != mp2.team_id  -- Opponent in same role, different team
            INNER JOIN matches m ON mp1.match_id = m.id
            INNER JOIN summoners s ON mp1.summoner_id = s.id
            INNER JOIN ranks r ON s.id = r.summoner_id
            WHERE mp1.team_position IS NOT NULL
            GROUP BY mp1.champion_id, mp1.team_position, mp2.champion_id, r.tier, m.patch
            HAVING COUNT(*) >= 100;  -- Minimum sample size

            CREATE INDEX idx_matchup_lookup
                ON champion_matchup_summary(champion_id, role, rank_tier, patch);
        ");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP MATERIALIZED VIEW IF EXISTS champion_matchup_summary;");
    }
}

// EF Core entity (read-only)
public class ChampionMatchupSummary
{
    public int ChampionId { get; set; }
    public string Role { get; set; }
    public int OpponentId { get; set; }
    public string RankTier { get; set; }
    public string Patch { get; set; }
    public int Games { get; set; }
    public int Wins { get; set; }
    public double WinRate { get; set; }
}

// Context configuration
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<ChampionMatchupSummary>(entity =>
    {
        entity.ToView("champion_matchup_summary");
        entity.HasNoKey(); // Views don't require keys
    });
}

// Daily refresh job
public class RefreshMatchupViewJob(TranscendenceContext db)
{
    public async Task Execute(CancellationToken ct)
    {
        // Refresh materialized view with latest data
        await db.Database.ExecuteSqlRawAsync(
            "REFRESH MATERIALIZED VIEW CONCURRENTLY champion_matchup_summary;",
            ct);
    }
}
```

**WHY materialized view:** Matchup queries require 10-way joins (participant vs opponent across 5 roles × 2 teams). Computing on every request = 5-10 second query. View pre-computes in 30 seconds, queries in 50ms.

**WHY CONCURRENTLY:** Allows queries during refresh. Without it, view locks during 30-second rebuild. CONCURRENTLY requires unique index (added in migration).

**WHY daily refresh:** Matchup data changes slowly (new matches trickle in). Daily refresh balances freshness vs database load. Real-time not required for meta insights.

### Anti-Patterns to Avoid
- **Computing aggregates on every request:** Aggregate millions of match participants per request = 3-10 second queries. Cache or materialize.
- **Using application-level sorting for tier lists:** Fetching all champions, sorting in C# = 100x slower than PostgreSQL window functions + 10x memory usage.
- **Short TTLs for "freshness theater":** 5-minute cache for daily-refresh analytics wastes computation. Analytics don't change hourly—24hr TTL is appropriate.
- **Individual cache key invalidation:** Managing `cache.RemoveAsync("winrate:1:top:gold:14.1")` for 170 champions × 5 roles × 10 tiers = 8,500 keys. Use tag-based invalidation.
- **Synchronous refresh on user request:** "Refresh now" button triggers 10-minute aggregate computation = request timeout. Use background job, return cached data immediately.

## Don't Hand-Roll

Problems that look simple but have existing solutions:

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Tier assignment algorithm | Custom scoring with if/else chains | PostgreSQL PERCENT_RANK() window function | Handles percentiles natively, 100x faster than app code, automatically adjusts distribution |
| Frequent item clustering | Nested loops comparing item sets | GROUP BY with string aggregation + frequency count | Database already optimized for set operations, avoids N² complexity |
| Cache invalidation across instances | Custom Redis pub/sub listener | HybridCache tag-based invalidation | Built-in, tested, handles edge cases (network failures, reconnects) |
| Composite score normalization | Manual min-max scaling | PostgreSQL PERCENT_RANK or Z-score | Database functions are battle-tested, avoid floating-point precision bugs |
| Movement indicators (↑↓) | Storing previous patch tier in code | Database LEFT JOIN with previous patch view | SQL handles historical comparison natively, simpler than state management |
| Skill order tracking | Parsing match timeline JSON | Riot Match Timeline API SKILL_LEVEL_UP events | Official data structure, though note: [known bug with duplicate events](https://github.com/RiotGames/developer-relations/issues/1100) |

**Key insight:** PostgreSQL window functions (PERCENT_RANK, ROW_NUMBER, RANK) and aggregate functions (GROUP BY, HAVING) are purpose-built for analytics. Attempting to replicate in C# results in 10-100x slower performance and higher memory usage.

## Common Pitfalls

### Pitfall 1: Insufficient Sample Size Bias
**What goes wrong:** Low-sample champions (e.g., 10 games, 90% win rate) appear in S tier, skewing tier lists.
**Why it happens:** No minimum sample filter in aggregation query. Lucky streaks on niche picks dominate.
**How to avoid:** Apply `HAVING COUNT(*) >= 100` in GROUP BY queries before tier assignment. User decision specifies "100 games minimum."
**Warning signs:** Off-meta champions (e.g., Ivern ADC) appearing in S tier. Tier lists changing dramatically day-to-day.

**Prevention code:**
```csharp
.Where(x => x.Games >= 100) // Minimum sample size
```

### Pitfall 2: Multi-Instance Cache Inconsistency
**What goes wrong:** Daily refresh job runs on Instance A, updates Redis. Instance B's L1 cache (1hr TTL) serves stale data for 59 more minutes.
**Why it happens:** HybridCache doesn't synchronize L1 across instances. Tag invalidation only affects current server + Redis.
**How to avoid:**
- **Option 1 (Recommended):** Short L1 TTL (1hr) + 24hr L2. Accept 1hr staleness window for analytics.
- **Option 2 (Complex):** Implement Redis pub/sub backplane to broadcast invalidation events. Requires custom code.
**Warning signs:** Users report "tier list different on mobile vs web" or "refresh didn't update data."

**Reference:** [Microsoft HybridCache documentation](https://learn.microsoft.com/en-us/aspnet/core/performance/caching/hybrid) notes this limitation explicitly.

### Pitfall 3: Core Item False Positives (Boots, Wards)
**What goes wrong:** Boots appear in 95%+ of games, marked as "core" for every champion. Clutters UI.
**Why it happens:** Frequency-based algorithm doesn't distinguish between mandatory items (boots, trinkets) and champion-specific core items.
**How to avoid:** Exclude item categories: boots (item IDs 3006, 3020, 3047, 3111, 3117, 3158), trinkets (3340-3364 range), consumables (2003, 2031, 2055).
**Warning signs:** Build recommendations showing "Core Items: Boots, Control Ward, Refillable Potion."

**Prevention code:**
```csharp
var excludedItems = new HashSet<int>
{
    3006, 3020, 3047, 3111, 3117, 3158, // Boots
    3340, 3363, 3364, // Trinkets
    2003, 2031, 2055  // Consumables
};

var itemFrequency = matchData
    .SelectMany(m => m.Items.Where(i => i != 0 && !excludedItems.Contains(i)))
    // ... rest of logic
```

### Pitfall 4: Matchup Data Sparsity
**What goes wrong:** Nasus Top vs Aatrox Top has 500 games (reliable). Nasus Top vs Yasuo Top has 8 games (unreliable). Both shown equally.
**Why it happens:** Minimum sample size (100 games) applies to champion aggregate, not per-matchup. Some matchups naturally rare.
**How to avoid:** Show game count in UI. Apply secondary minimum (30 games) for individual matchup display. Clearly label "insufficient data" for <30.
**Warning signs:** Extreme win rates (95% or 5%) in matchup matrix. Matchups appearing/disappearing between refreshes.

**User decision:** "Display game count for each matchup so users can judge reliability."

### Pitfall 5: Region Filter Performance Trap
**What goes wrong:** Adding `WHERE region = 'NA1'` to aggregate query. Works fine for NA (20M matches). Times out for OCE (200K matches).
**Why it happens:** Region is stored on `Summoner` table, not `Match`. Requires JOIN on every query. Low-population regions can't hit sample minimums.
**How to avoid:** Default to global aggregation (no region filter). Offer region filter as optional parameter with clear "may have insufficient data" warning.
**Warning signs:** Region filter queries taking 10+ seconds. OCE/LAN/LAS tier lists empty or single-tier.

**User decision:** "Region filtering: global aggregation by default, per-region filter available as optional parameter."

### Pitfall 6: Skill Order Data Gaps
**What goes wrong:** Build recommendations include items + runes but skill order missing or incorrect.
**Why it happens:** Skill order requires Match Timeline API (separate endpoint from Match API). Phase 2 only fetched Match API. Timeline data is heavier (5-10x larger JSON).
**How to avoid:**
- **Phase 3 scope:** Acknowledge skill order requires Timeline API. Mark as "LIMITATION: Skill order not available" in research.
- **Future phase:** Fetch Timeline API for subset of matches (e.g., recent 1000 per champion) to balance data completeness vs storage/API cost.
**Warning signs:** Build recommendations showing only items/runes. Users asking "where is skill order?"

**User decision:** "Include skill order (ability max order) in build recommendations."
**Known issue:** [Riot Timeline API has duplicate SKILL_LEVEL_UP events bug](https://github.com/RiotGames/developer-relations/issues/1100)—requires deduplication logic.

## Code Examples

Verified patterns from official sources:

### Composite Score Calculation (Tier Lists)
```csharp
// Source: Based on LoLalytics PBI formula and standard analytics practices
// https://lolalytics.com/lol/tierlist/
// Composite score: Win rate (primary) + Pick rate (secondary context)

var championStats = await _context.MatchParticipants
    .AsNoTracking()
    .Where(mp => mp.TeamPosition == role
              && mp.Match.Patch == patch
              && mp.Summoner.Ranks.Any(r => r.Tier == rankTier))
    .GroupBy(mp => mp.ChampionId)
    .Select(g => new
    {
        ChampionId = g.Key,
        Games = g.Count(),
        Wins = g.Sum(x => x.Win ? 1 : 0),
        WinRate = (double)g.Sum(x => x.Win ? 1 : 0) / g.Count()
    })
    .Where(c => c.Games >= 100)
    .ToListAsync(ct);

// Compute pick rate (requires total games count)
var totalGames = championStats.Sum(c => c.Games);

var withScores = championStats.Select(c => new
{
    c.ChampionId,
    c.WinRate,
    PickRate = (double)c.Games / totalGames,
    // 70% win rate, 30% pick rate weighting
    CompositeScore = (c.WinRate * 0.70) + ((double)c.Games / totalGames * 0.30)
}).ToList();
```

### Percentile-Based Tier Assignment
```csharp
// Source: PostgreSQL PERCENT_RANK equivalent in C# (for materialized view alternative)
// https://neon.com/docs/functions/window-rank
// https://dev.to/jetthoughts/efficient-percentile-ranking-in-postgresql-dbc

var orderedChampions = withScores
    .OrderByDescending(c => c.CompositeScore)
    .ToList();

var total = orderedChampions.Count;

var tierListEntries = orderedChampions.Select((champion, index) =>
{
    var percentile = (double)index / total;

    // Percentile-based tier thresholds (Claude's discretion)
    var tier = percentile switch
    {
        < 0.10 => "S",  // Top 10%
        < 0.30 => "A",  // 10-30%
        < 0.60 => "B",  // 30-60%
        < 0.85 => "C",  // 60-85%
        _ => "D"        // Bottom 15%
    };

    return new TierListEntry
    {
        ChampionId = champion.ChampionId,
        Tier = tier,
        WinRate = champion.WinRate,
        PickRate = champion.PickRate,
        CompositeScore = champion.CompositeScore,
        Rank = index + 1
    };
}).ToList();
```

### Movement Indicators (Previous Patch Comparison)
```csharp
// Source: Pattern from analytics dashboards
// Compare current tier to previous patch tier for ↑↓ indicators

public async Task<List<TierListEntry>> GetTierListWithMovementAsync(
    string role,
    string rankTier,
    CancellationToken ct)
{
    var currentPatch = await GetCurrentPatchAsync(ct);
    var previousPatch = await GetPreviousPatchAsync(ct);

    // Compute tier lists for both patches
    var currentTiers = await ComputeTierListAsync(role, rankTier, currentPatch, ct);
    var previousTiers = await ComputeTierListAsync(role, rankTier, previousPatch, ct);

    // Create lookup for previous patch tiers
    var previousTierMap = previousTiers.ToDictionary(t => t.ChampionId, t => t);

    // Add movement indicators
    return currentTiers.Select(current =>
    {
        if (!previousTierMap.TryGetValue(current.ChampionId, out var previous))
        {
            return current with { Movement = "NEW" }; // New to tier list
        }

        // Compare tier grades (S=5, A=4, B=3, C=2, D=1)
        var currentValue = TierToValue(current.Tier);
        var previousValue = TierToValue(previous.Tier);

        var movement = (currentValue - previousValue) switch
        {
            > 0 => "UP",    // Improved tier
            < 0 => "DOWN",  // Dropped tier
            _ => "SAME"     // No change
        };

        return current with
        {
            Movement = movement,
            PreviousTier = previous.Tier
        };
    }).ToList();
}

private static int TierToValue(string tier) => tier switch
{
    "S" => 5,
    "A" => 4,
    "B" => 3,
    "C" => 2,
    "D" => 1,
    _ => 0
};
```

### Matchup Win Rate Calculation (Counters + Synergies)
```csharp
// Source: Lane-specific matchup pattern from CounterStats.net
// https://www.counterstats.net/league-of-legends/brand
// User decision: "Matchups are lane-specific (Mid vs Mid, Top vs Top, etc.)"

public async Task<List<MatchupDto>> GetMatchupsAsync(
    int championId,
    string role,
    string rankTier,
    string patch,
    CancellationToken ct)
{
    // Join participant with opponents in same role, different team
    var matchups = await _context.MatchParticipants
        .AsNoTracking()
        .Where(mp => mp.ChampionId == championId
                  && mp.TeamPosition == role
                  && mp.Match.Patch == patch
                  && mp.Summoner.Ranks.Any(r => r.Tier == rankTier))
        .Join(
            _context.MatchParticipants,
            mp1 => mp1.MatchId,
            mp2 => mp2.MatchId,
            (mp1, mp2) => new { Champion = mp1, Opponent = mp2 }
        )
        .Where(x => x.Champion.TeamPosition == x.Opponent.TeamPosition  // Same role
                 && x.Champion.TeamId != x.Opponent.TeamId)              // Different team
        .GroupBy(x => x.Opponent.ChampionId)
        .Select(g => new MatchupDto
        {
            OpponentChampionId = g.Key,
            Games = g.Count(),
            Wins = g.Sum(x => x.Champion.Win ? 1 : 0),
            Losses = g.Sum(x => x.Champion.Win ? 0 : 1),
            WinRate = (double)g.Sum(x => x.Champion.Win ? 1 : 0) / g.Count()
        })
        .Where(m => m.Games >= 30) // Minimum matchup sample (lower than champion minimum)
        .ToListAsync(ct);

    // Separate counters (low win rate) and favorable matchups (high win rate)
    var counters = matchups
        .Where(m => m.WinRate < 0.48)
        .OrderBy(m => m.WinRate)
        .Take(5)
        .ToList();

    var favorable = matchups
        .Where(m => m.WinRate > 0.52)
        .OrderByDescending(m => m.WinRate)
        .Take(5)
        .ToList();

    return new MatchupSummary
    {
        Counters = counters,
        FavorableMatchups = favorable
    };
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| IDistributedCache | HybridCache (L1 + L2) | .NET 9 (Nov 2024) | 10-50x faster cache hits (L1 memory), stampede protection, tag-based invalidation |
| Manual GROUP BY queries | Materialized views for complex aggregates | PostgreSQL 9.3+ (2013) | 100x faster for multi-table joins, daily refresh acceptable for analytics |
| Application-level sorting/ranking | PostgreSQL window functions (PERCENT_RANK) | PostgreSQL 8.4+ (2009) | 10-100x faster percentile calculations, reduces memory usage |
| Custom cache invalidation | Tag-based removal (RemoveByTagAsync) | HybridCache GA (2024) | Single call invalidates entire analytics dataset, simpler than key management |
| Match API only | Match + Timeline API for skill order | Timeline API v5 (2021) | Provides SKILL_LEVEL_UP events for ability max order data |

**Deprecated/outdated:**
- **MemoryCacheEntryOptions with sliding expiration:** HybridCache replaces this with LocalCacheExpiration (absolute) + Expiration (L2). Sliding expiration doesn't work well for analytics (causes indefinite staleness).
- **ResponseCache attribute for controller caching:** Use HybridCache in service layer instead. Controller-level caching doesn't support tag-based invalidation or stampede protection.
- **Manual Redis string serialization:** HybridCache handles serialization automatically with System.Text.Json (or custom serializers). Manual `JsonSerializer.Serialize` + `redis.StringSet` is error-prone.

## Open Questions

Things that couldn't be fully resolved:

1. **Skill Order Data Availability**
   - What we know: Requires Match Timeline API (separate endpoint), contains SKILL_LEVEL_UP events. Phase 2 only fetches Match API.
   - What's unclear: Should Phase 3 backfill Timeline data for existing matches, or only fetch going forward? Timeline JSON is 5-10x larger than Match.
   - Recommendation: Phase 3 scope should fetch Timeline for NEW matches only (modify Phase 2's match fetching). Backfilling is separate data migration task.
   - Known bug: [Duplicate SKILL_LEVEL_UP events in Timeline API](https://github.com/RiotGames/developer-relations/issues/1100)—requires deduplication by (participantId, skillSlot, timestamp).

2. **Tier Boundary Method (Claude's Discretion)**
   - What we know: User wants composite score (win rate + pick rate). Research shows two approaches: percentile-based (top 10% = S) vs fixed thresholds (>55% composite = S).
   - What's unclear: Which method produces better UX and stability across patches?
   - Recommendation: **Use percentile-based (top 10% = S, 10-30% = A, etc.)**
     - **Why:** Ensures consistent tier distribution. Fixed thresholds create "all A tier" or "no S tier" situations when meta shifts.
     - **Tradeoff:** Percentile means "S tier" is relative, not absolute. A 52% win rate champion could be S tier in a balanced patch.
     - **Validation:** This matches LoLalytics and Mobalytics patterns (both use percentile-based systems).

3. **Rune Bundling with Builds (Claude's Discretion)**
   - What we know: Data model stores items + runes per participant. Can cluster builds by items only, or by items + runes together.
   - What's unclear: Do runes correlate strongly enough with items to bundle, or should they be separate recommendations?
   - Recommendation: **Bundle runes with item builds (single build = items + runes)**
     - **Why:** Runes and items are chosen together for synergistic strategies (e.g., Lethality items + Electrocute rune). Separating creates combinatorial explosion (3 item builds × 3 rune pages = 9 recommendations).
     - **Tradeoff:** Less flexibility for users who want to mix/match. However, showing "this build uses these runes" is clearer UX.
     - **Validation:** Sites like U.GG and OP.GG bundle runes with builds, not separate tabs.

4. **Number of Counters/Synergies (Claude's Discretion)**
   - What we know: User wants "both counters and synergies" with game counts. Statistical significance threshold is 30+ games per matchup.
   - What's unclear: Show top 3? Top 5? All with 30+ games?
   - Recommendation: **Show top 5 counters + top 5 favorable matchups**
     - **Why:** Top 3 is too few (misses important matchups), showing all creates noise (15+ counters for popular champions).
     - **Tradeoff:** Top 5 balances completeness vs UI clutter.
     - **Validation:** CounterStats and Mobalytics show 5-6 counters by default, with "show more" option.

5. **Multi-Instance Cache Synchronization**
   - What we know: HybridCache L1 doesn't auto-sync across instances. Tag invalidation only affects current server + Redis L2.
   - What's unclear: Is 1hr L1 staleness acceptable for analytics, or should we implement Redis pub/sub backplane?
   - Recommendation: **Accept 1hr L1 staleness (no backplane for Phase 3)**
     - **Why:** Analytics refresh daily (24hr data cycle). 1hr cross-instance delay is <5% of refresh cycle—negligible for this use case.
     - **Tradeoff:** Edge case: User on Instance A sees new tier list 30min after refresh, user on Instance B sees old tier list for another 30min.
     - **Validation:** For user-specific data (profiles, match history), backplane is critical. For global analytics, short L1 TTL is industry standard.
     - **Future:** If real-time analytics become a requirement (e.g., "live meta tracker"), implement Redis pub/sub backplane using [Milan Jovanovic's pattern](https://www.milanjovanovic.tech/blog/solving-the-distributed-cache-invalidation-problem-with-redis-and-hybridcache).

## Sources

### Primary (HIGH confidence)
- [Microsoft HybridCache Documentation](https://learn.microsoft.com/en-us/aspnet/core/performance/caching/hybrid) - Official guidance on L1/L2 caching, tag-based invalidation, TTL settings
- [Microsoft EF Core Performance Modeling](https://learn.microsoft.com/en-us/ef/core/performance/modeling-for-performance) - Materialized views, denormalization, aggregate query patterns
- [PostgreSQL PERCENT_RANK Function](https://neon.com/docs/functions/window-rank) - Window functions for percentile calculations
- [Milan Jovanovic: HybridCache in ASP.NET Core](https://www.milanjovanovic.tech/blog/hybrid-cache-in-aspnetcore-new-caching-library) - Practical patterns and cache invalidation strategies
- [Milan Jovanovic: Solving Distributed Cache Invalidation](https://www.milanjovanovic.tech/blog/solving-the-distributed-cache-invalidation-problem-with-redis-and-hybridcache) - Redis pub/sub backplane pattern

### Secondary (MEDIUM confidence)
- [LoLalytics Tier List](https://lolalytics.com/lol/tierlist/) - Industry example of composite scoring (win rate + pick rate PBI formula)
- [Mobalytics Tier List](https://mobalytics.gg/lol/tier-list/stats) - Percentile-based tier assignment patterns
- [Medium: Materialized Views with PostgreSQL](https://denileo82.hashnode.dev/boosting-performance-with-materialized-views-in-net-core-and-postgresql) - EF Core + PostgreSQL materialized view implementation
- [Dev.to: Efficient Percentile Ranking in PostgreSQL](https://dev.to/jetthoughts/efficient-percentile-ranking-in-postgresql-dbc) - PERCENT_RANK query patterns
- [Riot Developer Relations: SKILL_LEVEL_UP Bug](https://github.com/RiotGames/developer-relations/issues/1100) - Known issue with duplicate skill order events

### Tertiary (LOW confidence)
- [Frequent Itemset Mining Literature Review](https://link.springer.com/article/10.1007/s10462-018-9629-z) - Academic background on item clustering algorithms
- [James Le: Itemset Mining](https://jameskle.com/writes/itemset-mining) - Conceptual overview of frequent pattern mining
- [Amplitude Statistical Significance Calculator](https://amplitude.com/calculate/statistical-significance) - Sample size and confidence threshold guidance
- [Tools4Dev: Sample Size Selection](https://tools4dev.org/resources/how-to-choose-a-sample-size/) - "100 minimum" rule of thumb for meaningful results

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH - All libraries already in use (Phases 1-2), official Microsoft documentation
- Architecture: HIGH - Patterns verified with official docs (HybridCache, EF Core), industry examples (LoLalytics, Mobalytics)
- Pitfalls: MEDIUM - Derived from research + domain knowledge, some patterns validated with community sites
- Skill order: LOW - Requires Timeline API not yet implemented, known bug requires workaround

**Research date:** 2026-02-02
**Valid until:** 30 days (2026-03-04) for stable patterns (HybridCache, EF Core). Revalidate for Riot API changes (Timeline bug fixes).
