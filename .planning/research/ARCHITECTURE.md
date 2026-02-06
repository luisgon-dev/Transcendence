# Architecture Patterns: Analytics, Live Game, Auth, and Management Integration

**Domain:** League of Legends Analytics Backend (OP.GG/U.GG-like)
**Researched:** 2026-02-01
**Context:** Adding analytics computation, live game analysis, auth, and management features to existing .NET layered architecture

## Executive Summary

The existing architecture (WebAPI → Service.Core → Data with Hangfire background jobs) provides a solid foundation. New features should be integrated as follows:

1. **Analytics Computation Layer** - Add as specialized services in Service.Core with caching abstraction
2. **Live Game Analysis** - Add as background polling jobs leveraging existing Hangfire infrastructure
3. **Auth System** - Add as cross-cutting middleware using ASP.NET Core's multiple authentication schemes
4. **Management/Monitoring** - Extend existing Hangfire dashboard with custom pages and health checks

**Key Principle:** Maintain separation of concerns by keeping each layer's responsibilities clear while adding horizontal concerns (caching, auth) as cross-cutting infrastructure.

## Recommended Architecture

### Current State (Baseline)

```
┌─────────────────────────────────────────────────────────────┐
│                    Presentation Layer                        │
│  ┌─────────────────┐         ┌──────────────────────┐      │
│  │  WebAPI         │         │  WebAdminPortal      │      │
│  │  (Controllers)  │         │  (Hangfire Dashboard)│      │
│  └────────┬────────┘         └──────────────────────┘      │
└───────────┼──────────────────────────────────────────────────┘
            │
┌───────────┼──────────────────────────────────────────────────┐
│           ▼              Core Business Layer                 │
│  ┌─────────────────────────────────────────────────┐        │
│  │         Service.Core                             │        │
│  │  ┌──────────────┐  ┌─────────────┐             │        │
│  │  │ RiotApi      │  │ Analysis    │             │        │
│  │  │ Services     │  │ Services    │             │        │
│  │  └──────────────┘  └─────────────┘             │        │
│  │  ┌──────────────┐  ┌─────────────┐             │        │
│  │  │ Jobs         │  │ StaticData  │             │        │
│  │  └──────────────┘  └─────────────┘             │        │
│  └─────────────────────────────────────────────────┘        │
└───────────┼──────────────────────────────────────────────────┘
            │
┌───────────┼──────────────────────────────────────────────────┐
│           ▼              Data Layer                          │
│  ┌─────────────────────────────────────────────────┐        │
│  │  Repositories (EF Core)  +  Models               │        │
│  └─────────────────────────────────────────────────┘        │
└───────────┼──────────────────────────────────────────────────┘
            │
┌───────────▼──────────────────────────────────────────────────┐
│                    PostgreSQL Database                        │
│  (Summoners, Matches, Ranks, Hangfire Storage)               │
└──────────────────────────────────────────────────────────────┘
```

### Target State (With New Components)

```
┌─────────────────────────────────────────────────────────────────────┐
│                       Presentation Layer                             │
│  ┌─────────────────┐    ┌──────────────────────────────────┐       │
│  │  WebAPI         │    │  WebAdminPortal                  │       │
│  │  (Controllers)  │    │  ┌────────────────────────────┐  │       │
│  │  + Auth         │    │  │ Hangfire Dashboard         │  │       │
│  │    Middleware   │    │  │ + Management Pages         │  │       │
│  └────────┬────────┘    │  │ + Health Checks            │  │       │
└───────────┼─────────────│  └────────────────────────────┘  │───────┘
            │             └─────────────────────────────────────┘
            │ (Multiple Auth Schemes: API Key + JWT)
┌───────────┼──────────────────────────────────────────────────────────┐
│           ▼              Core Business Layer                          │
│  ┌───────────────────────────────────────────────────────────┐      │
│  │                    Service.Core                            │      │
│  │  ┌──────────────┐  ┌─────────────────────┐               │      │
│  │  │ RiotApi      │  │ Analytics           │               │      │
│  │  │ Services     │  │ ┌─────────────────┐ │ ← NEW         │      │
│  │  │              │  │ │ Aggregation Svc │ │               │      │
│  │  └──────────────┘  │ │ + Cache Layer   │ │               │      │
│  │                    │ └─────────────────┘ │               │      │
│  │  ┌──────────────┐  │ ┌─────────────────┐ │               │      │
│  │  │ Jobs         │  │ │ Stats Services  │ │               │      │
│  │  │ + LiveGame   │  │ │ (existing)      │ │               │      │
│  │  │   Polling    │  │ └─────────────────┘ │               │      │
│  │  │ ← NEW        │  └─────────────────────┘               │      │
│  │  └──────────────┘  ┌─────────────────────┐               │      │
│  │                    │ Auth Services       │ ← NEW         │      │
│  │  ┌──────────────┐  │ ┌─────────────────┐ │               │      │
│  │  │ StaticData   │  │ │ API Key Mgmt    │ │               │      │
│  │  │ Services     │  │ │ User Account    │ │               │      │
│  │  └──────────────┘  │ └─────────────────┘ │               │      │
│  │                    └─────────────────────┘               │      │
│  └───────────────────────────────────────────────────────────┘      │
└───────────┼──────────────────────────────────────────────────────────┘
            │
┌───────────┼──────────────────────────────────────────────────────────┐
│           ▼          Cross-Cutting Infrastructure                     │
│  ┌───────────────────────────────────────────────────────────┐      │
│  │  IDistributedCache (Redis)  ← NEW                         │      │
│  │  - Analytics aggregation results                          │      │
│  │  - Live game data (reduce Spectator API calls)            │      │
│  │  - User session tokens                                    │      │
│  └───────────────────────────────────────────────────────────┘      │
└───────────┼──────────────────────────────────────────────────────────┘
            │
┌───────────┼──────────────────────────────────────────────────────────┐
│           ▼              Data Layer                                   │
│  ┌───────────────────────────────────────────────────────────┐      │
│  │  Repositories + Models                                     │      │
│  │  + ApiKey, UserAccount entities  ← NEW                    │      │
│  └───────────────────────────────────────────────────────────┘      │
└───────────┼──────────────────────────────────────────────────────────┘
            │
┌───────────▼──────────────────────────────────────────────────────────┐
│                       PostgreSQL Database                             │
│  (+ ApiKeys, UserAccounts, LiveGameSnapshots tables)  ← NEW          │
└───────────────────────────────────────────────────────────────────────┘
```

## Component Boundaries and Integration Points

### 1. Analytics Computation Layer

**Location:** `Transcendence.Service.Core/Services/Analytics/`

**Component Boundaries:**

| Component | Responsibility | Communicates With |
|-----------|---------------|-------------------|
| **IAggregationService** | Compute complex aggregations (champion winrates across patches, player performance trends) | IMatchRepository, ICacheService |
| **ICacheService** | Abstract caching interface (wraps IDistributedCache) | Redis via IDistributedCache |
| **ISummonerStatsService** (existing) | Simple stats computations | IMatchRepository, ICacheService (new) |
| **IChampionLoadoutAnalysisService** (existing) | Champion-specific analytics | IMatchRepository, ICacheService (new) |

**Integration Strategy:**

1. **Add ICacheService abstraction** in Service.Core:
   - Interface wraps IDistributedCache with domain-specific methods
   - Example: `Task<T?> GetOrComputeAsync<T>(string key, Func<Task<T>> compute, TimeSpan expiration)`
   - Benefits: Testability, separation from infrastructure, consistent cache key strategy

2. **Inject ICacheService into existing analytics services**:
   - Decorator pattern: check cache first, compute on miss, store result
   - Cache keys follow convention: `{EntityType}:{EntityId}:{MetricType}:{TimeWindow}`
   - Example: `summoner:12345:winrate:last30days`

3. **Add new IAggregationService for heavy computations**:
   - Handles multi-summoner comparisons, patch-level trends, region-wide statistics
   - Uses background jobs for expensive pre-computations
   - Stores results in cache with longer TTL (1-24 hours depending on data volatility)

**Data Flow:**

```
Controller → Analytics Service → ICacheService.GetOrComputeAsync
                                        ↓ (cache miss)
                                  Repository (raw data)
                                        ↓
                                  In-memory aggregation (LINQ/EF)
                                        ↓
                                  ICacheService.Set (store result)
                                        ↓
                                  Return to Controller
```

**Why This Works:**
- Maintains existing service pattern
- Cache abstraction prevents leaking infrastructure concerns
- Services remain unit testable (mock ICacheService)
- EF Core handles aggregation efficiently (2026 benchmarks show competitive performance with Dapper)

### 2. Live Game Analysis

**Location:** `Transcendence.Service.Core/Services/LiveGame/` and `Transcendence.Service.Core/Services/Jobs/`

**Component Boundaries:**

| Component | Responsibility | Communicates With |
|-----------|---------------|-------------------|
| **ILiveGameService** | Fetch live game data via Spectator V4 API | RiotGamesApi (Camille), ICacheService |
| **ILiveGameAnalysisService** | Analyze participants (team comp, counters, player performance) | ILiveGameService, ISummonerStatsService, IMatchRepository |
| **LiveGamePollingJob** | Background job polling for active games | ILiveGameService, ILiveGameSnapshotRepository |
| **ILiveGameSnapshotRepository** | Persist live game state snapshots | EF Core, LiveGameSnapshot entity |

**Integration Strategy:**

1. **Polling Architecture (Spectator API Pattern)**:
   - **Not** event-driven (Riot doesn't provide webhooks)
   - Background job polls Spectator API every 30-60 seconds for tracked summoners
   - Cache live game data in Redis (TTL: 2 minutes) to reduce API calls
   - Persist snapshots to DB for historical "what was predicted vs actual outcome" analysis

2. **Job-Based Polling**:
   - Leverage existing Hangfire infrastructure
   - `RecurringJob.AddOrUpdate<LiveGamePollingJob>("poll-live-games", x => x.PollTrackedSummoners(), "*/1 * * * *")`
   - On game end detection: trigger match refresh job to persist final result

3. **Live Game Data Flow**:
   ```
   Client Request (GET /api/summoners/{id}/live-game)
         ↓
   Controller → ILiveGameService.GetLiveGameAsync(summonerId)
         ↓
   Check Cache (Redis) → HIT: return cached data
         ↓ (MISS)
   Call Spectator V4 API (via Camille)
         ↓
   Store in Cache (TTL: 2 minutes)
         ↓
   ILiveGameAnalysisService.EnrichWithAnalytics(liveGame)
         ↓ (fetch player stats for each participant)
   ISummonerStatsService.GetChampionStats(summonerId, championId)
         ↓
   Return enriched live game data to client
   ```

4. **Snapshot Persistence**:
   - Background job stores periodic snapshots (start of game, mid-game, end detected)
   - Use case: ML training data, accuracy tracking for predictions

**Why Polling Works:**
- Spectator API returns 404 when not in game (cheap health check)
- Redis cache prevents excessive API calls
- Hangfire handles scheduling/retries/failure recovery
- Fits existing background job pattern

**Anti-Pattern to Avoid:**
- Don't poll every summoner in DB (millions). Only poll:
  - Summoners with active refresh requests
  - User-tracked summoners (feature for authenticated users)
  - Featured high-ELO players

### 3. Auth System (API Keys + User Accounts)

**Location:** `Transcendence.WebAPI/` (middleware), `Transcendence.Service.Core/Services/Auth/` (business logic)

**Component Boundaries:**

| Component | Responsibility | Communicates With |
|-----------|---------------|-------------------|
| **ApiKeyAuthenticationHandler** | Validate API keys from header | IApiKeyService |
| **JwtAuthenticationHandler** | Validate JWT tokens for user accounts | IJwtService, IUserAccountService |
| **IApiKeyService** | API key CRUD, validation, rate limiting | IApiKeyRepository |
| **IUserAccountService** | User registration, login, Riot account linking | IUserAccountRepository, Riot OAuth |
| **Multiple Auth Schemes** | ASP.NET Core policy-based auth | Both handlers |

**Integration Strategy:**

1. **Multiple Authentication Schemes Pattern**:
   ```csharp
   builder.Services.AddAuthentication()
       .AddScheme<ApiKeyAuthenticationHandler>("ApiKey", options => {})
       .AddJwtBearer("Bearer", options => { /* JWT config */ });

   builder.Services.AddAuthorization(options =>
   {
       // Default policy: Accept EITHER ApiKey OR Bearer
       var policy = new AuthorizationPolicyBuilder("ApiKey", "Bearer")
           .RequireAuthenticatedUser()
           .Build();
       options.DefaultPolicy = policy;

       // App-only policy (for desktop app endpoints)
       options.AddPolicy("AppOnly", new AuthorizationPolicyBuilder("ApiKey")
           .RequireAuthenticatedUser()
           .Build());

       // User-only policy (for personalized features)
       options.AddPolicy("UserOnly", new AuthorizationPolicyBuilder("Bearer")
           .RequireAuthenticatedUser()
           .Build());
   });
   ```

2. **Controller Authorization Examples**:
   ```csharp
   // Accept API key OR user JWT
   [Authorize]
   [HttpGet("summoners/{region}/{name}/{tag}")]
   public async Task<IActionResult> GetSummoner(...) { }

   // Require API key (desktop app)
   [Authorize(Policy = "AppOnly")]
   [HttpPost("summoners/{id}/refresh")]
   public async Task<IActionResult> RefreshSummoner(...) { }

   // Require user account (personalization)
   [Authorize(Policy = "UserOnly")]
   [HttpPost("users/me/favorites")]
   public async Task<IActionResult> AddFavorite(...) { }
   ```

3. **API Key Handler Implementation**:
   ```csharp
   // Transcendence.WebAPI/Authentication/ApiKeyAuthenticationHandler.cs
   protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
   {
       if (!Request.Headers.TryGetValue("X-API-Key", out var apiKeyHeaderValues))
           return AuthenticateResult.NoResult();

       var apiKey = apiKeyHeaderValues.FirstOrDefault();
       var validationResult = await _apiKeyService.ValidateAsync(apiKey);

       if (!validationResult.IsValid)
           return AuthenticateResult.Fail("Invalid API key");

       var claims = new[] {
           new Claim(ClaimTypes.Name, validationResult.AppName),
           new Claim("ApiKeyId", validationResult.ApiKeyId.ToString())
       };
       var identity = new ClaimsIdentity(claims, Scheme.Name);
       var principal = new ClaimsPrincipal(identity);
       var ticket = new AuthenticationTicket(principal, Scheme.Name);

       return AuthenticateResult.Success(ticket);
   }
   ```

4. **Data Model**:
   ```csharp
   // Transcendence.Data/Models/Auth/ApiKey.cs
   public class ApiKey
   {
       public Guid Id { get; set; }
       public string Key { get; set; } // SHA256 hash
       public string AppName { get; set; }
       public DateTime CreatedAt { get; set; }
       public DateTime? RevokedAt { get; set; }
       public int RateLimitPerMinute { get; set; } // Future: rate limiting
   }

   // Transcendence.Data/Models/Auth/UserAccount.cs
   public class UserAccount
   {
       public Guid Id { get; set; }
       public string Email { get; set; }
       public string PasswordHash { get; set; }
       public string? RiotPuuid { get; set; } // Linked Riot account
       public DateTime CreatedAt { get; set; }
       public List<Summoner> FavoriteSummoners { get; set; } // Many-to-many
   }
   ```

**Build Order Implications:**
- Phase 1: API key auth only (simpler, unblocks desktop app development)
- Phase 2: User accounts with JWT (enables web personalization features)
- Reason: API keys are stateless and easier to test; user accounts require password hashing, email verification, OAuth flows

**Why Multiple Schemes Work:**
- ASP.NET Core built-in support (high confidence)
- Desktop app gets simple API key (paste into settings)
- Web users get full account features
- Middleware-level enforcement (cross-cutting concern, not in controllers)

### 4. Management/Monitoring Layer

**Location:** `Transcendence.WebAdminPortal/` (extended), `Transcendence.Service.Core/Services/Management/`

**Component Boundaries:**

| Component | Responsibility | Communicates With |
|-----------|---------------|-------------------|
| **Hangfire Dashboard** (existing) | Job monitoring, retry, manual triggers | Hangfire PostgreSQL storage |
| **Custom Management Pages** | Data controls (manual refresh, purge old data, static data updates) | Background jobs via IBackgroundJobClient |
| **Health Check Endpoints** | System health, DB connectivity, Redis connectivity, Riot API status | IHealthCheck implementations |
| **IDataMaintenanceService** | Purge old matches, cleanup orphaned data, recompute aggregates | Repositories |

**Integration Strategy:**

1. **Extend Hangfire Dashboard with Custom Pages**:
   - Use Hangfire.Dashboard.Management package for custom actions
   - Add management pages at `/hangfire/management`
   - Example actions:
     - Manual summoner refresh (enqueue job)
     - Force static data update
     - Purge matches older than X days
     - Recompute cached aggregations

2. **Health Checks**:
   ```csharp
   // In WebAPI/Program.cs
   builder.Services.AddHealthChecks()
       .AddNpgSql(connectionString, name: "postgres")
       .AddRedis(redisConnectionString, name: "redis")
       .AddCheck<RiotApiHealthCheck>("riot-api")
       .AddCheck<HangfireHealthCheck>("hangfire-jobs");

   app.MapHealthChecks("/health", new HealthCheckOptions {
       ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
   });
   ```

3. **Custom Management Service**:
   ```csharp
   // Transcendence.Service.Core/Services/Management/IDataMaintenanceService.cs
   public interface IDataMaintenanceService
   {
       Task PurgeMatchesOlderThan(TimeSpan age);
       Task CleanupOrphanedParticipants();
       Task RecomputeAllAggregates();
       Task<SystemHealthReport> GetSystemHealth();
   }
   ```

4. **Dashboard Security**:
   - Current state: Dashboard accessible only from localhost
   - Enhanced: Add authentication filter for production
   ```csharp
   app.UseHangfireDashboard("/hangfire", new DashboardOptions {
       Authorization = new[] { new AdminAuthorizationFilter() }
   });
   ```

**Data Flow:**

```
Admin User → Hangfire Dashboard (/hangfire/management)
                     ↓
         Custom Management Page (button click)
                     ↓
         IBackgroundJobClient.Enqueue<IDataMaintenanceJob>(...)
                     ↓
         Hangfire picks up job
                     ↓
         Job executes → IDataMaintenanceService
                     ↓
         Repositories perform bulk operations
                     ↓
         Job completes → Dashboard shows result
```

**Why This Works:**
- Hangfire dashboard already exists (minimal new infrastructure)
- Custom pages leverage existing job queue (consistent with architecture)
- Health checks are ASP.NET Core standard (high confidence)
- Separates admin concerns from public API

## Patterns to Follow

### Pattern 1: Cache-Aside with Computation

**What:** Check cache before computing expensive aggregations; populate cache on miss.

**When:** Analytics endpoints with heavy LINQ aggregations or multi-table joins.

**Example:**
```csharp
public async Task<WinRateStats> GetChampionWinRateAsync(int championId, string patch)
{
    var cacheKey = $"champion:{championId}:winrate:{patch}";

    return await _cacheService.GetOrComputeAsync(cacheKey, async () =>
    {
        var matches = await _matchRepository.GetMatchesByChampionAndPatch(championId, patch);
        var wins = matches.Count(m => m.Win);
        return new WinRateStats
        {
            TotalGames = matches.Count,
            Wins = wins,
            WinRate = (double)wins / matches.Count
        };
    }, TimeSpan.FromHours(6)); // Low volatility: cache for 6 hours
}
```

**Benefits:**
- Reduces DB load
- Consistent API response times
- Easy to test (mock ICacheService)

### Pattern 2: Background Job for Long-Running Operations

**What:** Offload expensive operations to Hangfire background jobs.

**When:** Live game polling, bulk data refresh, nightly aggregation pre-computation.

**Example:**
```csharp
// In controller (enqueue job)
[HttpPost("summoners/{id}/track-live-games")]
public IActionResult TrackLiveGames(Guid id)
{
    _backgroundJobClient.Enqueue<ILiveGamePollingJob>(
        x => x.StartTracking(id));
    return Accepted();
}

// In job implementation
public async Task StartTracking(Guid summonerId)
{
    // Poll Spectator API every minute
    while (await _liveGameService.IsInGame(summonerId))
    {
        var liveGame = await _liveGameService.GetLiveGameAsync(summonerId);
        await _liveGameSnapshotRepository.AddSnapshot(liveGame);
        await Task.Delay(TimeSpan.FromMinutes(1));
    }
}
```

**Benefits:**
- Fits existing Hangfire infrastructure
- Job persistence (survives restarts)
- Built-in retry and monitoring

### Pattern 3: Multiple Authentication Schemes with Policies

**What:** Support both API key (for apps) and JWT (for users) authentication.

**When:** Public API with both machine clients and human users.

**Example:**
```csharp
// Allow both schemes
[Authorize] // Uses default policy (ApiKey OR Bearer)
[HttpGet("summoners/{id}")]
public async Task<IActionResult> GetSummoner(Guid id) { }

// Require specific scheme
[Authorize(Policy = "UserOnly")] // Must be authenticated user
[HttpPost("users/me/favorites/{summonerId}")]
public async Task<IActionResult> AddFavorite(Guid summonerId) { }
```

**Benefits:**
- Single API serves multiple client types
- Clear authorization rules
- Middleware-level enforcement

### Pattern 4: Repository + Service Layering for Auth

**What:** Auth data (API keys, user accounts) follows same repository pattern as domain data.

**When:** Adding new data entities that need persistence.

**Example:**
```csharp
// Repository
public interface IApiKeyRepository
{
    Task<ApiKey?> GetByKeyAsync(string key);
    Task<ApiKey> CreateAsync(string appName);
    Task RevokeAsync(Guid id);
}

// Service (business logic)
public interface IApiKeyService
{
    Task<ApiKeyValidationResult> ValidateAsync(string key);
    Task<string> GenerateNewKeyAsync(string appName);
}

// Service implementation
public class ApiKeyService : IApiKeyService
{
    public async Task<ApiKeyValidationResult> ValidateAsync(string key)
    {
        var hashedKey = HashKey(key); // Never store plaintext
        var apiKey = await _repository.GetByKeyAsync(hashedKey);

        if (apiKey == null || apiKey.RevokedAt != null)
            return ApiKeyValidationResult.Invalid();

        // Future: Check rate limits
        return ApiKeyValidationResult.Valid(apiKey.Id, apiKey.AppName);
    }
}
```

**Benefits:**
- Consistent with existing patterns
- Testable
- Single responsibility (repository = persistence, service = logic)

## Anti-Patterns to Avoid

### Anti-Pattern 1: Caching in Repositories

**What:** Adding cache logic directly in repository implementations.

**Why bad:** Violates single responsibility; repositories should only handle data access.

**Instead:** Add caching in service layer via ICacheService abstraction.

**Example of WRONG approach:**
```csharp
// DON'T DO THIS
public class SummonerRepository : ISummonerRepository
{
    public async Task<Summoner?> GetByIdAsync(Guid id)
    {
        var cacheKey = $"summoner:{id}";
        var cached = await _cache.GetAsync(cacheKey); // Repository knows about cache!
        if (cached != null) return cached;

        var summoner = await _context.Summoners.FindAsync(id);
        await _cache.SetAsync(cacheKey, summoner);
        return summoner;
    }
}
```

**Example of CORRECT approach:**
```csharp
// Service wraps repository with caching
public class SummonerService : ISummonerService
{
    public async Task<Summoner?> GetByIdAsync(Guid id)
    {
        return await _cacheService.GetOrComputeAsync(
            $"summoner:{id}",
            () => _summonerRepository.GetByIdAsync(id),
            TimeSpan.FromMinutes(30)
        );
    }
}
```

### Anti-Pattern 2: Real-Time Live Game Polling Per Request

**What:** Calling Spectator API on every client request to `/live-game` endpoint.

**Why bad:**
- Exceeds Riot API rate limits
- Slow response times (external API latency)
- Unnecessary load

**Instead:**
- Background job polls periodically (every 1-2 minutes)
- Store in Redis cache (TTL: 2 minutes)
- Controller serves from cache

**Consequences of Wrong Approach:**
- API key suspension from Riot
- Degraded user experience (timeouts)

### Anti-Pattern 3: Mixing Authentication Schemes in Controllers

**What:** Manually parsing API keys AND JWT tokens in controller actions.

**Why bad:**
- Violates separation of concerns
- Duplicated logic across controllers
- Harder to test

**Instead:** Use ASP.NET Core authentication middleware with multiple schemes.

**Example of WRONG approach:**
```csharp
// DON'T DO THIS
[HttpGet("summoners/{id}")]
public async Task<IActionResult> GetSummoner(Guid id)
{
    // Manual auth in controller!
    if (Request.Headers.TryGetValue("X-API-Key", out var apiKey))
    {
        if (!await _apiKeyService.ValidateAsync(apiKey))
            return Unauthorized();
    }
    else if (Request.Headers.TryGetValue("Authorization", out var bearer))
    {
        // Parse JWT manually...
    }
    else
    {
        return Unauthorized();
    }

    // Business logic...
}
```

**Example of CORRECT approach:**
```csharp
// Middleware handles auth
[Authorize] // Accepts either scheme
[HttpGet("summoners/{id}")]
public async Task<IActionResult> GetSummoner(Guid id)
{
    // User is already authenticated by middleware
    // Access claims via User.Identity
}
```

### Anti-Pattern 4: Tight Coupling to IDistributedCache

**What:** Services directly depend on `IDistributedCache` from Microsoft.Extensions.Caching.

**Why bad:**
- Couples domain services to infrastructure interface
- Harder to test (mock byte[] serialization)
- No domain-specific cache key conventions

**Instead:** Create `ICacheService` abstraction with domain methods.

**Example:**
```csharp
// Domain-specific abstraction
public interface ICacheService
{
    Task<T?> GetOrComputeAsync<T>(string key, Func<Task<T>> compute, TimeSpan expiration);
    Task InvalidateAsync(string pattern); // Example: "summoner:*"
}

// Implementation wraps IDistributedCache
public class RedisCacheService : ICacheService
{
    private readonly IDistributedCache _cache;

    public async Task<T?> GetOrComputeAsync<T>(string key, Func<Task<T>> compute, TimeSpan expiration)
    {
        var cached = await _cache.GetStringAsync(key);
        if (cached != null)
            return JsonSerializer.Deserialize<T>(cached);

        var value = await compute();
        await _cache.SetStringAsync(key, JsonSerializer.Serialize(value),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = expiration });

        return value;
    }
}
```

### Anti-Pattern 5: Hangfire Jobs with Heavy Logic

**What:** Putting complex business logic directly in job classes.

**Why bad:**
- Jobs become untestable (Hangfire infrastructure required)
- Violates single responsibility
- Can't reuse logic outside job context

**Instead:** Jobs orchestrate; services contain logic.

**Example of WRONG approach:**
```csharp
public class LiveGamePollingJob
{
    public async Task Poll(Guid summonerId)
    {
        // Heavy logic in job class!
        var spectatorData = await _riotApi.Spectator.GetCurrentGameInfoBySummonerId(...);
        var enrichedData = /* complex analysis logic */;
        await _dbContext.SaveChangesAsync();
    }
}
```

**Example of CORRECT approach:**
```csharp
public class LiveGamePollingJob
{
    public async Task Poll(Guid summonerId)
    {
        // Job orchestrates; service has logic
        var liveGame = await _liveGameService.GetAndAnalyzeCurrentGameAsync(summonerId);
        await _liveGameSnapshotRepository.SaveAsync(liveGame);
    }
}
```

## Scalability Considerations

| Concern | At 100 users | At 10K users | At 1M users |
|---------|--------------|--------------|-------------|
| **Analytics Computation** | In-memory LINQ aggregations, no cache | Add Redis cache (6-hour TTL for winrates) | Pre-compute aggregations nightly via background jobs; cache with 24-hour TTL |
| **Live Game Polling** | Poll on-demand (per request) | Background job polls tracked summoners every 2 minutes | Partition polling across multiple workers by region; cache in Redis |
| **Auth Token Validation** | Validate against DB on every request | Cache valid API keys in Redis (1-hour TTL) | Stateless JWT validation (no DB hit); API key bloom filter for revocation check |
| **Database Connections** | Default EF Core pooling (max 100) | Increase pool size to 500; add read replicas for analytics queries | Separate analytics DB (OLAP) from transactional DB (OLTP); use computed columns |
| **Hangfire Job Queue** | Single worker, 5 concurrent jobs | Multiple workers (horizontal scaling), 20 concurrent jobs | Partition jobs by region across workers; use priority queues |

**Key Insight:** Start simple (in-memory aggregations, direct DB access). Add caching when response times degrade. Add pre-computation when cache misses become expensive.

## Build Order Implications

Based on dependencies between components, recommended build order:

### Phase 1: Foundation (Caching Infrastructure)
**Why First:** Analytics, live game, and auth all benefit from caching.

1. Add ICacheService abstraction to Service.Core
2. Add Redis IDistributedCache implementation
3. Add cache key conventions documentation
4. Update existing analytics services to use cache

**Deliverable:** Analytics endpoints are faster; foundation ready for new features.

### Phase 2: API Key Authentication
**Why Before User Accounts:** Simpler; unblocks desktop app development.

1. Add ApiKey entity and repository
2. Add IApiKeyService for validation
3. Add ApiKeyAuthenticationHandler middleware
4. Add management endpoints for key creation/revocation

**Deliverable:** Desktop app can authenticate with API keys.

### Phase 3: Analytics Computation
**Why Now:** Depends on caching (Phase 1); independent of auth.

1. Add IAggregationService for complex analytics
2. Add background jobs for pre-computation
3. Add new analytics endpoints (champion trends, matchup analysis)
4. Add cache warming jobs (run nightly)

**Deliverable:** Rich analytics features available.

### Phase 4: Live Game Polling
**Why After Analytics:** Reuses analytics services for enrichment; benefits from caching.

1. Add ILiveGameService (Spectator API wrapper)
2. Add LiveGamePollingJob (background polling)
3. Add LiveGameSnapshot entity and repository
4. Add /live-game endpoints
5. Add Redis cache for live game data

**Deliverable:** Live game analysis available to desktop app.

### Phase 5: User Accounts (JWT Auth)
**Why Last:** Most complex; enables web personalization but not required for core features.

1. Add UserAccount entity and repository
2. Add JWT authentication handler
3. Add multiple auth scheme policies
4. Add user registration/login endpoints
5. Add Riot account linking (OAuth)
6. Add favorite summoners feature

**Deliverable:** Web users can create accounts and personalize experience.

### Phase 6: Management/Monitoring
**Why Throughout:** Can be added incrementally alongside other phases.

1. Add health check endpoints (early, for monitoring)
2. Add custom Hangfire dashboard pages (as jobs are added)
3. Add IDataMaintenanceService (when data cleanup needed)
4. Add dashboard authentication (before production)

**Deliverable:** Ops team has visibility and control.

## Data Flow Examples

### Example 1: Analytics Query with Caching

```
Client: GET /api/champions/89/stats?patch=14.3
          ↓
Controller (requires auth: API key or JWT)
          ↓
IChampionAnalysisService.GetStatsAsync(89, "14.3")
          ↓
ICacheService.GetOrComputeAsync("champion:89:stats:14.3")
          ↓ (cache miss)
IMatchRepository.GetMatchesByChampionAndPatch(89, "14.3")
          ↓
EF Core query → PostgreSQL
          ↓
LINQ aggregation (in-memory)
  - Win rate
  - Average KDA
  - Popular items
          ↓
ICacheService.Set("champion:89:stats:14.3", result, TTL: 6 hours)
          ↓
Return to Controller → JSON response
```

### Example 2: Live Game Polling (Background)

```
Hangfire Scheduler (every 2 minutes)
          ↓
LiveGamePollingJob.PollTrackedSummoners()
          ↓
ILiveGameService.GetCurrentGameAsync(summonerId)
          ↓
Check Redis cache (key: "livegame:{summonerId}")
          ↓ (cache miss)
Call Spectator V4 API (via Camille)
  - Returns CurrentGameInfo or 404
          ↓ (200 OK)
Parse game data (participants, bans, game time)
          ↓
Cache in Redis (TTL: 2 minutes)
          ↓
ILiveGameAnalysisService.EnrichWithPlayerStats(gameInfo)
  - For each participant:
    - Fetch ISummonerStatsService.GetRecentPerformance()
    - Fetch champion mastery, recent winrate
          ↓
ILiveGameSnapshotRepository.SaveAsync(snapshot)
          ↓
EF Core insert → PostgreSQL
          ↓
Job complete
```

### Example 3: User Authentication Flow (JWT)

```
Client: POST /api/auth/login
Body: { email, password }
          ↓
AuthController (no [Authorize] attribute)
          ↓
IUserAccountService.AuthenticateAsync(email, password)
          ↓
IUserAccountRepository.GetByEmailAsync(email)
          ↓
Verify password hash (BCrypt)
          ↓ (valid)
IJwtService.GenerateToken(userId, email)
  - Create claims
  - Sign with secret key
  - 7-day expiration
          ↓
Return JWT to client
          ↓
Client stores JWT (localStorage for web, secure storage for desktop)
          ↓
Subsequent requests: Authorization: Bearer {token}
          ↓
JwtAuthenticationHandler validates token
  - Signature verification
  - Expiration check
  - Extract claims
          ↓
User.Identity populated → controller accesses User.FindFirst("sub")
```

### Example 4: API Key Authentication Flow

```
Desktop App sends: GET /api/summoners/NA/Player/NA1
Header: X-API-Key: abc123def456
          ↓
ApiKeyAuthenticationHandler.HandleAuthenticateAsync()
          ↓
Extract "X-API-Key" header
          ↓
IApiKeyService.ValidateAsync("abc123def456")
          ↓
Hash key (SHA256)
          ↓
Check Redis cache (key: "apikey:hash:{hash}")
          ↓ (cache miss)
IApiKeyRepository.GetByKeyAsync(hash)
          ↓
EF Core query → PostgreSQL
          ↓
Check if revoked (RevokedAt == null)
          ↓ (valid)
Cache in Redis (TTL: 1 hour, key: apikey ID)
          ↓
Return validation result (appName, keyId)
          ↓
Handler creates ClaimsPrincipal
  - Claim: Name = appName
  - Claim: ApiKeyId = keyId
          ↓
Controller executes with authenticated user
```

## Technology Decisions

### Caching: Redis via IDistributedCache

**Why Redis:**
- Industry standard for distributed caching (HIGH confidence)
- Built-in ASP.NET Core support via `Microsoft.Extensions.Caching.StackExchangeRedis`
- Supports advanced features (pub/sub for future cache invalidation)
- Proven at scale (OP.GG, U.GG likely use Redis or similar)

**Alternatives Considered:**
- In-memory cache (`IMemoryCache`): Doesn't scale horizontally; lost on restart
- SQL Server as cache: Slower; adds DB load
- Memcached: Less feature-rich than Redis; same deployment complexity

### Authentication: ASP.NET Core Multiple Schemes

**Why Built-In Middleware:**
- Native framework support (HIGH confidence)
- Separation of concerns (cross-cutting infrastructure)
- Extensive documentation and community examples
- Handles edge cases (scheme selection, policy evaluation)

**Alternatives Considered:**
- Custom middleware: Reinventing the wheel; higher bug risk
- Third-party library (e.g., IdentityServer): Over-engineered for simple API key + JWT

### Background Jobs: Hangfire (Existing)

**Why Continue Using Hangfire:**
- Already integrated (sunk cost is minimal)
- Proven for current refresh jobs
- Built-in dashboard (management layer benefit)
- Supports recurring jobs, retries, distributed locks

**No Change Needed:** Live game polling fits existing job pattern.

### Analytics Computation: EF Core LINQ + Caching

**Why Not Move to Dedicated Analytics Engine:**
- 2026 benchmarks show EF Core competitive for aggregations (MEDIUM confidence)
- Adds complexity (new infrastructure, data replication lag)
- Caching solves 90% of performance concerns at <1M users
- Can migrate later if needed (repository pattern abstracts data source)

**When to Reconsider:**
- If aggregations regularly timeout (>5 seconds)
- If DB CPU consistently >70%
- If query complexity exceeds EF capabilities (window functions, recursive CTEs)

**Alternative:** ClickHouse or TimescaleDB (both PostgreSQL-compatible extensions for analytics).

## Sources

### Caching Architecture
- [Caching guidance - Azure Architecture Center](https://learn.microsoft.com/en-us/azure/architecture/best-practices/caching)
- [Caching in .NET - Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/core/extensions/caching)
- [Distributed caching in ASP.NET Core - Microsoft Learn](https://learn.microsoft.com/en-us/aspnet/core/performance/caching/distributed?view=aspnetcore-10.0)
- [Distributed Caching in ASP.NET Core with Redis - Code Maze](https://code-maze.com/aspnetcore-distributed-caching/)

### Authentication Patterns
- [Authorize with a specific scheme in ASP.NET Core - Microsoft Learn](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/limitingidentitybyscheme?view=aspnetcore-10.0)
- [How to Use Multiple Authentication Schemes in .NET - Code Maze](https://code-maze.com/dotnet-multiple-authentication-schemes/)
- [Implement API Key Authentication in ASP.NET Core - Code Maze](https://code-maze.com/aspnetcore-api-key-authentication/)

### Hangfire Management
- [Hangfire – Background Jobs for .NET](https://www.hangfire.io/overview.html)
- [Using Dashboard UI — Hangfire Documentation](https://docs.hangfire.io/en/latest/configuration/using-dashboard.html)
- [GitHub - Hangfire.Dashboard.Management.v2](https://github.com/lcourson/Hangfire.Dashboard.Management.v2)

### Live Game Analytics
- [Using the spectator-v4 API - DarkIntaqt Blog](https://darkintaqt.com/blog/spectator-v4)
- [Riot Games API Deep Dive](https://technology.riotgames.com/news/riot-games-api-deep-dive)
- [Real-Time Analytics with SignalR and InfluxDB](https://developersvoice.com/blog/data-analytics/real-time-analytics-with-signalr-and-influxdb/)

### .NET Architecture Patterns
- [Common web application architectures - .NET - Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/architecture/modern-web-apps-azure/common-web-application-architectures)
- [Layered (N-Tier) Architecture in .NET Core](https://dev.to/dotnetfullstackdev/layered-n-tier-architecture-in-net-core-51ic)
- [Core-Driven Architecture: Structuring Business Logic for Maintainable and Scalable .NET Applications](https://dev.to/adrianbailador/core-driven-architecture-structuring-business-logic-for-maintainable-and-scalable-net-applications-4230)

### EF Core Performance
- [Modeling for Performance - EF Core - Microsoft Learn](https://learn.microsoft.com/en-us/ef/core/performance/modeling-for-performance)
- [Benchmarking EF Core LINQ, Dapper Raw SQL, and Stored Procedures in .NET (2026)](https://dev.to/mina_golzari_dalir/benchmarking-ef-core-linq-dapper-raw-sql-and-stored-procedures-in-net-a-real-world-performance-54e4)

---

**Confidence Level:** HIGH for caching, auth middleware, Hangfire patterns (verified with official docs). MEDIUM for live game polling specifics (based on community patterns, not official OP.GG architecture).

**Last Updated:** 2026-02-01
