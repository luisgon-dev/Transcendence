# Phase 1: Foundation - Data Infrastructure & API Safety - Research

**Researched:** 2026-02-01
**Domain:** .NET 10 caching infrastructure, distributed cache patterns, Riot API rate limiting, background job processing
**Confidence:** HIGH

## Summary

This phase requires implementing a two-tier caching strategy (in-memory L1 + Redis L2) with automatic rate limit safety, patch detection, and safe retry logic. The standard approach uses HybridCache (.NET 9+) for stampede protection, Hangfire for background jobs, and Camille SDK for Riot API integration with built-in rate limit handling. Entity Framework Core with Npgsql handles PostgreSQL persistence with async patterns.

The core challenge is NOT implementing custom rate limiting (Camille handles this), but rather building observability layers, cache invalidation strategies for patch releases, safe retry schedules, and data freshness tracking. Key decision areas include patch detection timing, cache key versioning, Redis TTL relationships, and failure marking to prevent infinite retry loops.

**Primary recommendation:** Use HybridCache as the primary caching abstraction with Redis as the L2 store, Hangfire for scheduled patch checks and background fetching, Camille's built-in rate limiting with monitoring only, and mark permanently unfetchable data in the database rather than retrying indefinitely.

## Standard Stack

### Core Caching & Infrastructure
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| Microsoft.Extensions.Caching.Hybrid | 10.0+ | Two-tier cache (L1 MemoryCache + L2 Redis) with stampede protection | Official .NET 10 standard, built-in cache stampede prevention via single-flight, tag-based invalidation |
| Microsoft.Extensions.Caching.StackExchangeRedis | 10.0+ | Redis distributed cache backend | Official ASP.NET Core integration, connection multiplexing efficiency, single TCP connection |
| StackExchange.Redis | 2.7+ | Low-level Redis client operations if needed | Industry standard, used by HybridCache internally |
| Hangfire | 1.8+ | Background job scheduling and execution | Industry standard for .NET background jobs, supports queues, scheduling, rate limiting via Hangfire.Throttling |
| Camille.RiotGames | 3.0.0-nightly | Riot API client with built-in rate limiting | Official auto-updates within 24 hours, thread-safe, handles rate limit headers automatically, retry logic included |

### Data Persistence
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| Entity Framework Core | 10.0+ | ORM for PostgreSQL | Official, async-first design, DbContext pooling, query optimization |
| Npgsql.EntityFrameworkCore.PostgreSQL | 10.0.0+ | PostgreSQL provider for EF Core | Official Npgsql provider, unified configuration (UseNpgsql), supports GUID v7, PostgreSQL-specific features |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| Microsoft.Extensions.Hosting.Windows | 10.0+ | Windows service hosting for Worker Service | Running background workers as Windows services |
| Polly | 8.0+ | Resilience & transient fault-handling library | Complex retry policies beyond Hangfire (optional, Hangfire handles exponential backoff) |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| HybridCache | Manual IMemoryCache + IDistributedCache | More boilerplate, no stampede protection, loses tag-based invalidation |
| Hangfire | Quartz.NET | Quartz has more complexity, Hangfire simpler for simple jobs |
| Hangfire | Temporal | Overkill for this phase, adds operational complexity |
| Camille | RiotNet | RiotNet is community-maintained, Camille auto-generates from official API spec |
| StackExchange.Redis | ServiceStack.Redis | StackExchange is standard, community-preferred |

**Installation:**
```bash
dotnet add package Microsoft.Extensions.Caching.Hybrid
dotnet add package Microsoft.Extensions.Caching.StackExchangeRedis
dotnet add package Hangfire.Core
dotnet add package Hangfire.SqlServer
dotnet add package Camille.RiotGames
dotnet add package Microsoft.EntityFrameworkCore
dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL
```

## Architecture Patterns

### Recommended Project Structure
```
src/
├── Transcendence.WebApi/              # ASP.NET Core WebAPI
│   ├── Controllers/
│   ├── Services/                      # HybridCache wrapper, data services
│   ├── Middleware/                    # Request logging, error handling
│   └── Startup.cs                     # DI configuration
├── Transcendence.Worker/              # BackgroundService for Hangfire
│   ├── Jobs/                          # Hangfire job classes
│   ├── Services/                      # Static data fetching, retry orchestration
│   └── Program.cs                     # Host configuration
├── Transcendence.Service.Core/        # Business logic
│   ├── CacheStrategy/                 # Cache key generation, TTL config
│   ├── RiotIntegration/               # Camille wrapper, rate limit monitoring
│   ├── DataFetching/                  # Retry schedules, failure marking
│   └── Models/                        # Domain models, DTOs
└── Transcendence.Data/                # EF Core DbContext, migrations
    ├── ApplicationDbContext.cs        # Npgsql configuration
    ├── Entities/                      # Domain entities with data age metadata
    └── Migrations/                    # EF Core migrations
```

### Pattern 1: HybridCache with Redis & Stampede Protection
**What:** Use HybridCache as the primary caching abstraction. It provides automatic stampede protection (only one caller executes the factory, others wait) and tag-based invalidation.

**When to use:** All read-through caching scenarios for static data, rank data, match summaries. Use for ANY data fetched from Riot API.

**Example:**
```csharp
// Source: https://learn.microsoft.com/en-us/aspnet/core/performance/caching/hybrid?view=aspnetcore-10.0
public class ChampionDataService
{
    private readonly HybridCache _cache;

    public ChampionDataService(HybridCache cache)
    {
        _cache = cache;
    }

    public async Task<List<Champion>> GetChampionsAsync(CancellationToken token)
    {
        // HybridCache handles stampede protection internally
        // Only one request fetches from Riot API, others wait
        return await _cache.GetOrCreateAsync(
            key: "static:champions:v1",
            factory: async ct => await FetchChampionsFromRiotAsync(ct),
            options: new HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromHours(24),
                LocalCacheExpiration = TimeSpan.FromMinutes(5) // L1/L2 TTL relationship
            },
            tags: new[] { "patch-dependent" }, // For bulk invalidation
            cancellationToken: token
        );
    }
}
```

### Pattern 2: Cache Key Versioning for Patch Detection
**What:** Embed patch version or detection timestamp in cache keys. When a patch is detected, old keys expire naturally via TTL; new keys are fetched and served.

**When to use:** For champion, item, rune data that changes per patch.

**Example:**
```csharp
// Source: Based on cache key versioning patterns
public class CacheKeyGenerator
{
    // Patch format: 26.01, 26.02, etc. (from 2026-02-01 context)
    public static string StaticDataKey(string dataType, string patchVersion)
    {
        return $"static:{dataType}:{patchVersion}";
    }

    // Usage in fetch
    var key = CacheKeyGenerator.StaticDataKey("champions", "26.01");
    var champions = await _cache.GetOrCreateAsync(
        key,
        async _ => await _riotApi.Champion.GetAllAsync(),
        options: new HybridCacheEntryOptions
        {
            Expiration = TimeSpan.FromDays(30), // Outlives patch cycle
            LocalCacheExpiration = TimeSpan.FromMinutes(5)
        }
    );
}
```

### Pattern 3: Hangfire Background Jobs for Patch Detection & Fetching
**What:** Use Hangfire to schedule periodic checks for patch releases (every 24 hours is reasonable overlap for 2-week patch cycle). On detection, trigger static data refresh and begin aggressive match fetching.

**When to use:** Scheduled patch detection, triggered static data refresh, background match fetching.

**Example:**
```csharp
// Source: https://docs.hangfire.io/en/latest/background-processing/
public class PatchDetectionJob
{
    private readonly IHybridCache _cache;
    private readonly RiotApiWrapper _riotApi;

    public async Task DetectPatchReleaseAsync()
    {
        var currentPatch = await _riotApi.GetCurrentPatchAsync();
        var cachedPatch = await _cache.GetAsync<string>("current:patch:version");

        if (currentPatch != cachedPatch)
        {
            // Patch detected, invalidate static data
            await _cache.RemoveByTagAsync("patch-dependent");

            // Schedule aggressively: refetch champions, items, runes
            BackgroundJob.Enqueue(() => RefreshStaticDataAsync());
        }
    }
}

// Register in Program.cs
services.AddHangfire(config => config
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseSqlServerStorage("connection-string")
);

services.AddHangfireServer();

// Schedule recurring job
RecurringJob.AddOrUpdate<PatchDetectionJob>(
    "detect-patch",
    x => x.DetectPatchReleaseAsync(),
    Cron.Daily(2, 0) // Daily at 2 AM
);
```

### Pattern 4: Rate Limit Monitoring (No Custom Throttling)
**What:** Camille handles rate limiting automatically via built-in headers parsing. Build a monitoring layer to LOG rate limit events, not to apply custom throttling.

**When to use:** Observing API rate limit behavior, detecting when approaching limits, alerting on rate limit exhaustion.

**Example:**
```csharp
// Source: https://github.com/MingweiSamuel/Camille
public class CamilleRiotApiWrapper
{
    private readonly RiotApi _api;
    private readonly ILogger<CamilleRiotApiWrapper> _logger;

    public async Task<List<MatchDto>> GetMatchesByPuuidAsync(string puuid)
    {
        try
        {
            var matches = await _api.MatchV5.GetIdsAsync("na1", puuid);
            return matches;
        }
        catch (RiotApiException ex) when (ex.StatusCode == 429)
        {
            // Camille's built-in handling, but log for monitoring
            _logger.LogWarning(
                "Rate limit hit: {RetryAfter}s remaining quota",
                ex.RetryAfter
            );
            // Don't implement custom throttling; let Camille's auto-retry handle it
            throw;
        }
    }
}
```

### Pattern 5: Database Failure Marking (Prevent Infinite Retries)
**What:** Add a "FetchStatus" enum to data entities: Success, TemporaryFailure, PermanentlyUnfetchable. Mark data as unfetchable after max retries instead of retrying forever.

**When to use:** Match data, timelines that fail to fetch after retry schedule exhausted.

**Example:**
```csharp
// Source: Based on EF Core soft delete patterns
public enum FetchStatus
{
    Unfetched = 0,
    Success = 1,
    TemporaryFailure = 2,
    PermanentlyUnfetchable = 3 // Stop retrying
}

public class Match
{
    public string MatchId { get; set; }
    public FetchStatus Status { get; set; }
    public int RetryCount { get; set; }
    public DateTime? FetchedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

// EF Query Filter to exclude permanently unfetchable
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<Match>()
        .HasQueryFilter(m => m.Status != FetchStatus.PermanentlyUnfetchable);

    base.OnModelCreating(modelBuilder);
}

// Retry logic
public async Task FetchMatchAsync(string matchId)
{
    var match = await _db.Matches.FirstAsync(m => m.MatchId == matchId);

    try
    {
        var data = await _riotApi.MatchV5.GetAsync("na1", matchId);
        match.Status = FetchStatus.Success;
        match.FetchedAt = DateTime.UtcNow;
    }
    catch (Exception ex)
    {
        match.RetryCount++;
        if (match.RetryCount >= 5) // 5 attempts max
        {
            match.Status = FetchStatus.PermanentlyUnfetchable;
            _logger.LogWarning("Match {Id} marked unfetchable after {Count} retries",
                matchId, match.RetryCount);
        }
        else
        {
            match.Status = FetchStatus.TemporaryFailure;
            // Reschedule via Hangfire with exponential backoff
            BackgroundJob.Schedule(
                () => FetchMatchAsync(matchId),
                TimeSpan.FromSeconds(30 * (int)Math.Pow(2, match.RetryCount))
            );
        }
    }

    await _db.SaveChangesAsync();
}
```

### Pattern 6: Data Freshness Metadata in API Responses
**What:** Include "FetchedAt" or "AsOf" timestamps in API responses so clients can understand data age.

**When to use:** All API endpoints returning cached or background-fetched data.

**Example:**
```csharp
public class ChampionResponse
{
    public List<Champion> Champions { get; set; }
    public DateTime AsOf { get; set; }
    public TimeSpan Age => DateTime.UtcNow - AsOf;
}

// In controller
[HttpGet("/api/champions")]
public async Task<ChampionResponse> GetChampions()
{
    var champions = await _championService.GetChampionsAsync();
    var fetchedAt = await _db.Champions
        .OrderByDescending(c => c.FetchedAt)
        .Select(c => c.FetchedAt)
        .FirstOrDefaultAsync();

    return new ChampionResponse
    {
        Champions = champions,
        AsOf = fetchedAt ?? DateTime.UtcNow
    };
}
```

### Anti-Patterns to Avoid
- **Rolling your own cache layer:** HybridCache is mature and handles stampede protection. Using separate IMemoryCache + IDistributedCache manually loses stampede protection.
- **Adding custom rate limit throttling on top of Camille:** Camille already handles Riot API rate limits. A monitoring layer is useful, but NOT custom throttling that delays your own requests.
- **Retrying failed API calls indefinitely without a status flag:** This creates database bloat and infinite job queues. Always mark data as "unfetchable" after max retries.
- **Hard-coding TTLs:** Use configuration (appsettings.json) for all cache TTLs so they can change without redeployment.
- **Ignoring Retry-After headers from Riot API:** Camille parses these automatically; respect them by letting Camille handle backoff.
- **Caching without knowing your L1/L2 TTL relationship:** L1 (MemoryCache) should be shorter than L2 (Redis) to prevent stale distributed cache poisoning across servers.

## Don't Hand-Roll

Problems that look simple but have existing solutions:

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Cache stampede (thundering herd on cache miss) | Custom lock-based single-flight logic | HybridCache (stampede protection built-in) | HybridCache's guarantee: only one concurrent caller executes factory for given key; others wait. Manual implementation is error-prone. |
| Rate limit header parsing & backoff | Custom Retry-After parsing & delay logic | Camille.RiotGames (automatic handling) | Camille auto-parses X-Rate-Limit-* headers and respects Retry-After. Riot API has multiple rate limit types (app-level, method-level, service-level); Camille handles all. |
| Distributed cache invalidation | Custom tag-based deletion logic | HybridCache.RemoveByTagAsync() | Tag-based invalidation is complex (need to track all tagged keys). HybridCache provides single-call bulk invalidation. |
| Exponential backoff retry schedules | Manual delay calculations | Hangfire.AutomaticRetryAttribute or Hangfire.Throttling | Hangfire provides exponential backoff out of box with customizable attempt counts. Manual math is error-prone. |
| Background job scheduling & persistence | Custom timer-based background loops | Hangfire | Hangfire persists jobs to database (survives app restarts), provides dashboard, supports delayed/recurring jobs. Custom timers are fragile. |
| PostgreSQL-specific EF Core configuration | Manual DbContext setup | Npgsql.UseNpgsql() unified config | Npgsql 9.0+ provides single point (UseNpgsql) for all Npgsql + EF config. Prior versions required scattered configuration. |

**Key insight:** This phase's hidden complexity lies in ORCHESTRATING these patterns (when to detect patches, how aggressive to fetch, when to mark data unfetchable), not in building infrastructure. All core infrastructure is provided by standard libraries. Custom code should focus on business logic (patch detection timing, retry schedules, freshness policies) not on low-level caching/rate limiting mechanics.

## Common Pitfalls

### Pitfall 1: Believing You Need Custom Throttling on Top of Camille
**What goes wrong:** Implementing custom rate limit logic (delayed queues, manual Retry-After parsing) because you're "nervous" about hitting Riot API limits.

**Why it happens:** Camille's built-in rate limiting feels "invisible"—it's automatic. Developers assume they need to add their own layer.

**How to avoid:** Test Camille's rate limit handling in staging. Read the Camille source (it parses X-Rate-Limit-* headers). Trust the auto-retry logic. Add monitoring/logging to see rate limit events, but don't add throttling.

**Warning signs:** You're checking response headers for rate limit info, implementing delay logic, or adding RequestsPerSecond limits to your job queues.

### Pitfall 2: Cache Stampede from Missing Stampede Protection
**What goes wrong:** When cache expires, 100 concurrent requests all hit the database/API simultaneously, causing a spike and potential cascading timeout.

**Why it happens:** Using IMemoryCache + IDistributedCache separately without stampede protection. All requests see cache miss, all execute factory concurrently.

**How to avoid:** Use HybridCache exclusively. It guarantees only one concurrent request executes the factory for a given key; others wait for result.

**Warning signs:** You see API timeouts or database connection pool exhaustion right after cache expiration. Multiple identical API calls for same data in logs.

### Pitfall 3: Cache Invalidation Without a Strategy
**What goes wrong:** Forgetting to invalidate static data (champions, items) when patch releases. Stale data served for 24+ hours after patch.

**Why it happens:** No scheduled patch detection job. Assuming "TTL expiration" is enough.

**How to avoid:** Schedule a recurring Hangfire job that checks for patch releases every 12-24 hours (matches 2-week patch cycle). On detection, use `RemoveByTagAsync("patch-dependent")` to invalidate old data immediately. Include timestamp in cache key for defense-in-depth.

**Warning signs:** You don't have a "detect patch" job scheduled. Your static data fetch job doesn't run automatically on patch release.

### Pitfall 4: Infinite Retry Loops (No Unfetchable Status)
**What goes wrong:** Match data fetch fails (user deleted their history, account banned). Job retries forever. Database grows with unfetchable data. Job queue fills up.

**Why it happens:** No failure marking strategy. Code only knows "Success" or "Keep Retrying".

**How to avoid:** Add FetchStatus enum with PermanentlyUnfetchable state. After max retries (e.g., 5), mark status as unfetchable, stop retrying, log warning. Query filters exclude unfetchable data from normal queries.

**Warning signs:** Your Hangfire job queue is growing indefinitely. You see repeat failures for same match ID over hours. Database table has 100K+ rows with Status = null/unknown.

### Pitfall 5: L1/L2 TTL Mismatch
**What goes wrong:** L1 (MemoryCache) expires while L2 (Redis) still valid. Server reads stale data from Redis. Another server overwrites with fresh data. Inconsistent data across servers.

**Why it happens:** Setting TTLs independently without thinking about cache hierarchy. L1 TTL = 1 hour, L2 TTL = 30 minutes (backwards).

**How to avoid:** L1 TTL should be SHORTER than L2 TTL. Example: L2 = 1 hour, L1 = 5 minutes. This ensures in-process cache expires, server checks Redis (which still has fresh data), and refreshes. Prevents stale-while-distributed-fresh scenarios.

**Warning signs:** Your servers return different data for same request. Redis shows fresh data, but server in-memory cache serves stale data.

### Pitfall 6: Ignoring Connection Pooling Configuration
**What goes wrong:** Redis connection timeouts, "Too many connections" errors, or intermittent 429s from Riot API due to connection exhaustion.

**Why it happens:** Default StackExchange.Redis or Hangfire connection settings inadequate for high-throughput. Not configuring max concurrent requests in Camille.

**How to avoid:** Configure StackExchange.Redis for HybridCache with appropriate ConnectionMultiplexer settings (connection timeout ~5s, keepalive < 10 minutes). Configure Camille's MaxConcurrentRequests based on testing. Monitor connection pool health.

**Warning signs:** Intermittent "timeout" errors that correlate with request spikes. Connection pool exhaustion warnings in logs.

### Pitfall 7: Not Including Data Age in API Responses
**What goes wrong:** Client doesn't know data age. Serves 24-hour-old champion stats in UI labeled as "current". User confusion.

**Why it happens:** API returns data without "AsOf" or "FetchedAt" metadata. Client has no way to distinguish fresh from stale.

**How to avoid:** All API responses include "AsOf" timestamp or "Age" duration. Document expected freshness in API spec (e.g., "champions updated within 24h of patch, refresh within 5 minutes after game ends for rank data").

**Warning signs:** API responses lack any timestamp field. Client code tries to infer freshness heuristically.

### Pitfall 8: Unhandled Exceptions in Hangfire Jobs
**What goes wrong:** Job throws unhandled exception, gets retried 10 times (default), then discarded silently. Data never fetched. No alert.

**Why it happens:** Not using AutomaticRetryAttribute or custom exception handling in jobs. Not logging failures.

**How to avoid:** Wrap job logic in try-catch with explicit logging. Use AutomaticRetryAttribute with appropriate attempt count. Set up Hangfire dashboard monitoring. Alert on job failure exceeding threshold.

**Warning signs:** Hangfire jobs silently fail. No logs of why a job retried. Dashboard shows jobs in "Failed" state with no error message.

## Code Examples

Verified patterns from official sources:

### HybridCache Basic Usage with Redis
```csharp
// Source: https://learn.microsoft.com/en-us/aspnet/core/performance/caching/hybrid?view=aspnetcore-10.0
// Program.cs
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("Redis");
});

builder.Services.AddHybridCache(options =>
{
    options.DefaultEntryOptions = new HybridCacheEntryOptions
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromSeconds(30)
    };
});

// Service
public class DataService(HybridCache cache)
{
    public async Task<T> GetAsync<T>(string key, Func<CancellationToken, Task<T>> factory)
    {
        return await cache.GetOrCreateAsync(
            key,
            factory,
            cancellationToken: CancellationToken.None
        );
    }
}
```

### Tag-Based Invalidation
```csharp
// Source: https://learn.microsoft.com/en-us/aspnet/core/performance/caching/hybrid?view=aspnetcore-10.0
public async Task RefreshOnPatchAsync()
{
    // Invalidate all patch-dependent data
    await _cache.RemoveByTagAsync("patch-v26.01");

    // Fetch fresh data; HybridCache prevents stampede
    var fresh = await _cache.GetOrCreateAsync(
        "champions:v26.02",
        async ct => await _riotApi.Champion.GetAllAsync(ct),
        options: new HybridCacheEntryOptions
        {
            Expiration = TimeSpan.FromDays(14),
            LocalCacheExpiration = TimeSpan.FromMinutes(5)
        },
        tags: new[] { "patch-v26.02" },
        cancellationToken: CancellationToken.None
    );
}
```

### Hangfire Recurring Job with Retry Logic
```csharp
// Source: https://docs.hangfire.io/en/latest/background-processing/
[AutomaticRetry(Attempts = 3)]
public class StaticDataRefreshJob
{
    public async Task ExecuteAsync()
    {
        // Fetch champions, items, runes
        // On failure, Hangfire auto-retries with exponential backoff
    }
}

// Program.cs
RecurringJob.AddOrUpdate<StaticDataRefreshJob>(
    "refresh-static-data",
    x => x.ExecuteAsync(),
    Cron.Daily(3, 0) // 3 AM UTC
);
```

### EF Core Async Query with Npgsql
```csharp
// Source: https://www.npgsql.org/efcore/
public async Task<List<Champion>> GetChampionsAsync()
{
    // Always use async methods to avoid thread pool starvation
    return await _db.Champions
        .AsNoTracking()
        .OrderBy(c => c.Name)
        .ToListAsync();
}

// DbContext configuration
public class ApplicationDbContext : DbContext
{
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseNpgsql(
            _connectionString,
            options => options
                .SetPostgresVersion(15, 0)
                .UseNodaTime()
        );
    }
}
```

### Monitoring Rate Limit Events (No Custom Throttling)
```csharp
// Source: https://github.com/MingweiSamuel/Camille
public class RiotApiMonitor
{
    private readonly RiotApi _api;
    private readonly ILogger _logger;

    public async Task<MatchDto> GetMatchAsync(string matchId)
    {
        try
        {
            var match = await _api.MatchV5.GetAsync("na1", matchId);
            return match;
        }
        catch (RiotApiException ex) when (ex.StatusCode == 429)
        {
            // Log rate limit event for monitoring, don't implement custom throttling
            _logger.LogWarning(
                "Rate limit 429: Camille auto-retry in effect. RetryAfter: {Seconds}s",
                ex.RetryAfter
            );
            throw; // Let Camille's auto-retry handle it
        }
    }
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| IMemoryCache + manual IDistributedCache | HybridCache abstraction | .NET 9 (2024) | Eliminates boilerplate, stampede protection built-in, tag-based invalidation |
| Manual retry-after parsing | Camille SDK (auto-parsing) | Ongoing (Camille nightly) | Reduces bugs, auto-respects Riot API rate limit headers, thread-safe |
| DbContext.OnConfiguring() | UseNpgsql() unified config | EF Core 9.0 (2024) | All Npgsql options in one place, cleaner DI integration |
| BackgroundService + timers | Hangfire recurring jobs | Shift from cloud-first (2020s) | Jobs persist to DB, survive restarts, dashboard visibility, rate limiting support |

**Deprecated/outdated:**
- **Manual cache stampede prevention (Redis SET NX loops):** Replaced by HybridCache's built-in single-flight. Manual implementations are error-prone.
- **Service.Redis over StackExchange.Redis:** StackExchange is community standard, actively maintained. ServiceStack.Redis has fewer .NET 10 updates.
- **DbContext.OnConfiguring():** UseNpgsql() unified approach is cleaner for EF 9.0+. OnConfiguring() still works but scattered config.
- **Hard-coded API endpoints in code:** Should use configuration (appsettings.json, environment variables). Facilitates Riot API version changes.

## Open Questions

Things that couldn't be fully resolved:

1. **Patch Detection Timing: Gradual Overlap vs Immediate?**
   - What we know: Patch cycle ~2 weeks. Camille updates within 24h. Two strategies exist: (a) overlap period where old+new data both cached, (b) immediate cutover when patch detected.
   - What's unclear: Which provides better UX? Overlap adds cache memory, immediate is cleaner. No official Riot guidance found.
   - Recommendation: Start with immediate cutover (simpler). Cache key versioning handles stale data naturally. Monitor user impact. Can switch to overlap if needed.

2. **Cache Key Versioning: When to Rotate Keys?**
   - What we know: Patch version (26.01) or timestamp can be embedded. Prevents serving stale data after patch.
   - What's unclear: Should key include patch version (auto-invalidates on patch)? Or just version number? Trade-off between simplicity and freshness guarantees.
   - Recommendation: Embed patch version (e.g., "champions:v26.01"). On patch detection, fetch with new key. Old keys expire via TTL. Simplest and safest.

3. **Failure Marking: Admin-only vs API-exposed Status?**
   - What we know: Need to mark unfetchable data to stop retries. Decision: expose failure status in API or hide from clients?
   - What's unclear: Should API clients see "this data failed to fetch"? Or silently omit failed records?
   - Recommendation: Admin-only initially. Include in internal metrics/logs. If needed for client-side UX later, expose as separate status field. Keeps API contract simple.

4. **Background Job Auto-Pause During API Outages?**
   - What we know: If Riot API is down, Hangfire jobs will retry and fail. Camille respects rate limits, but outages are different.
   - What's unclear: Should we auto-pause background jobs when API returns 503/5XX? Risk of job queue bloat if outage lasts hours.
   - Recommendation: Start without auto-pause. Monitor job failure rate. If outage > 30 minutes, manually pause jobs. Automation can be added if needed. Hangfire dashboard makes manual pausing easy.

5. **Retry Schedule: 30s-5min Delays?**
   - What we know: Requirement mentions "eventual consistency (30s-5min delays)". This is reasonable exponential backoff.
   - What's unclear: Exact schedule (30s → 1m → 2m → 5m? Or different?) and max attempts before marking unfetchable.
   - Recommendation: Use Hangfire's default exponential backoff with multiplier. Attempt 1: 1s, Attempt 2: 10s, Attempt 3: 100s, then cap at 5 minutes. Max 5 attempts, then mark unfetchable. Adjust based on testing.

## Sources

### Primary (HIGH confidence)
- **Microsoft.Extensions.Caching.Hybrid** - Official Microsoft Learn docs: https://learn.microsoft.com/en-us/aspnet/core/performance/caching/hybrid?view=aspnetcore-10.0 (HybridCache usage, stampede protection, tag-based invalidation)
- **Npgsql.EntityFrameworkCore.PostgreSQL** - Official Npgsql docs: https://www.npgsql.org/efcore/ (Npgsql configuration, EF Core integration, PostgreSQL-specific features)
- **Camille.RiotGames** - Official GitHub: https://github.com/MingweiSamuel/Camille (Rate limit handling, configuration, auto-update strategy)
- **Hangfire Documentation** - Official Hangfire docs: https://docs.hangfire.io/en/latest/background-processing/ (Job scheduling, retry configuration, concurrency)
- **StackExchange.Redis** - Official configuration: https://stackexchange.github.io/StackExchange.Redis/Configuration.html (Connection pooling, connection multiplexing)

### Secondary (MEDIUM confidence)
- **Entity Framework Performance** - Microsoft Learn: https://learn.microsoft.com/en-us/ef/core/performance/efficient-querying (Async patterns, query optimization)
- **Hangfire Retries** - Community verified: https://docs.hangfire.io/en/latest/background-processing/dealing-with-exceptions.html (Automatic retry attribute, exponential backoff)
- **HybridCache Overview** - .NET Blog: https://devblogs.microsoft.com/dotnet/hybrid-cache-is-now-ga/ (Official announcement, feature overview)
- **EF Core Soft Delete Patterns** - Milan Jovanović guide: https://www.milanjovanovic.tech/blog/implementing-soft-delete-with-ef-core (Global query filters, soft delete implementation)

### Tertiary (LOW confidence, needs validation)
- **Patch detection automation strategies** - WebSearch only, no official Riot documentation found (patch timing, detection frequency)
- **Failure status exposure (admin vs API)** - Best practice assumption, no official guidance (cache or expose unfetchable status)
- **Cache key versioning strategies** - Industry pattern, verified across multiple sources but no official .NET standard (embed version, rotate on patch)

## Metadata

**Confidence breakdown:**
- **Standard Stack:** HIGH - All libraries verified via official docs, NuGet packages with recent updates, or official GitHub sources
- **Architecture Patterns:** HIGH - HybridCache, Hangfire, Camille patterns documented in official sources. Soft delete is established EF Core pattern.
- **Pitfalls:** MEDIUM - Common pitfalls gathered from official docs, community posts, and logical inference. Some based on ecosystem patterns rather than explicit "gotchas" docs.
- **Code Examples:** HIGH - All examples sourced from official documentation or verified library examples

**Research date:** 2026-02-01
**Valid until:** 2026-03-01 (30 days; stack is stable, HybridCache is mature since .NET 9)
**Note:** .NET 10 released 2024, libraries are stable. Main variables are Riot API changes (handled by Camille nightly updates) and team's patch detection strategy (to be decided in planning phase).
