# Architecture

**Analysis Date:** 2026-01-31

## Pattern Overview

**Overall:** Distributed multi-project ASP.NET Core with separate API and background job service using Repository and Service patterns.

**Key Characteristics:**
- Data-driven League of Legends analytics service
- Asynchronous background job processing for Riot API calls
- Separated concerns: WebAPI (sync reads), Service (async refresh jobs), Data (persistence), Core (business logic)
- Event-driven refresh pattern using distributed locks and Hangfire queues
- Entity Framework Core with PostgreSQL

## Layers

**Presentation Layer (API):**
- Purpose: HTTP REST endpoints for client consumption
- Location: `Transcendence.WebAPI/Controllers/`
- Contains: ASP.NET Core controllers for summoner data and statistics
- Depends on: Service.Core (business logic), Data (persistence), Hangfire (background jobs)
- Used by: External clients

**Admin/Monitoring Layer:**
- Purpose: Hangfire dashboard for job monitoring
- Location: `Transcendence.WebAdminPortal/Program.cs`
- Contains: Hangfire dashboard configuration only
- Depends on: Hangfire PostgreSQL storage
- Used by: Operations staff

**Core Business Logic Layer:**
- Purpose: Analytics, API integration, and data transformation
- Location: `Transcendence.Service.Core/Services/`
- Contains: Analysis services, Riot API wrappers, static data management, background job definitions
- Depends on: Data layer (repositories, models), Camille (Riot SDK)
- Used by: WebAPI controllers, Worker service

**Data Access Layer:**
- Purpose: Database operations and repository pattern
- Location: `Transcendence.Data/Repositories/` and `Transcendence.Data/Models/`
- Contains: Entity Framework DbContext, repositories with interfaces, EF models
- Depends on: Microsoft.EntityFrameworkCore, PostgreSQL driver (Npgsql)
- Used by: All other layers

**Background Worker Service:**
- Purpose: Execute long-running jobs (refreshes, static data updates) asynchronously
- Location: `Transcendence.Service/Program.cs` and `Transcendence.Service/Workers/`
- Contains: Hangfire server configuration, worker implementations
- Depends on: Service.Core (jobs), Data (persistence), Hangfire
- Used by: Scheduled tasks, enqueued background jobs

## Data Flow

**Summoner Refresh Flow (On-Demand):**

1. Client calls `POST /api/summoners/{region}/{name}/{tag}/refresh`
2. `SummonersController` acquires a distributed lock via `RefreshLockRepository`
3. Controller enqueues `ISummonerRefreshJob` using Hangfire's `IBackgroundJobClient`
4. Returns 202 Accepted with polling URL and retry-after header
5. Hangfire worker picks up the job from PostgreSQL queue
6. `SummonerRefreshJob.RefreshByRiotId` executes:
   - Calls `ISummonerService.GetSummonerByRiotIdAsync` (Riot API via Camille)
   - Calls `ISummonerRepository.AddOrUpdateSummonerAsync` for persistence
   - Fetches recent ranked solo match IDs via Riot API
   - Deduplicates against existing matches in DB
   - Calls `IMatchService.GetMatchDetailsAsync` for each new match
   - Persists matches and participants via `IMatchRepository`
7. Job releases the refresh lock
8. Client polls the original GET endpoint until summoner is found in DB

**Statistics Retrieval Flow (Cached):**

1. Client calls `GET /api/summoners/{summonerId}/stats/overview`
2. `SummonerStatsController` calls `ISummonerStatsService.GetSummonerOverviewAsync`
3. Service queries `MatchParticipants` table directly using LINQ/EF
4. Aggregates wins, losses, KDA, CS/min, vision score, damage
5. Calculates recent performance from last N matches ordered by date
6. Returns computed statistics

**Static Data Update Flow (Scheduled):**

1. `ProductionWorker` schedules `UpdateStaticDataJob` daily at configured time
2. Job calls `IStaticDataService` to fetch patch/rune/item data from DataDragon and CommunityDragon
3. Service upserts `Patch`, `RuneVersion`, and `ItemVersion` tables
4. Version-based foreign keys ensure historical data consistency

**State Management:**

- **Summoner State:** Stored in `Summoners` table, updated on refresh
- **Match History:** Appended to `Matches` and `MatchParticipants` tables
- **Refresh Lock:** Short-lived distributed lock in `RefreshLocks` table (prevents concurrent refreshes)
- **Rank History:** Archived in `HistoricalRanks` table when rank changes detected
- **Static Game Data:** Versioned by patch in `Patch`, `RuneVersion`, `ItemVersion` tables

## Key Abstractions

**Repositories (Data Access):**
- Purpose: Abstract EF Core access patterns for aggregate roots
- Examples: `SummonerRepository`, `MatchRepository`, `RankRepository`, `RefreshLockRepository`
- Location: `Transcendence.Data/Repositories/Interfaces/` and `Transcendence.Data/Repositories/Implementations/`
- Pattern: Interface-based with async Task methods, optional expression includes for EF eager loading

**Service Pattern (Business Logic):**
- Purpose: Domain-specific operations (API calls, analytics, data transformation)
- Examples: `ISummonerService`, `IMatchService`, `IRankService`, `ISummonerStatsService`, `IChampionLoadoutAnalysisService`
- Location: `Transcendence.Service.Core/Services/`
- Pattern: Injected into controllers or other services; stateless; async Task methods

**Job Pattern (Background Work):**
- Purpose: Long-running asynchronous operations executed by Hangfire
- Examples: `ISummonerRefreshJob`, `UpdateStaticDataJob`
- Location: `Transcendence.Service.Core/Services/Jobs/`
- Pattern: Public interface method acts as Hangfire method to invoke; throws exceptions to signal failure; Hangfire handles retries

## Entry Points

**WebAPI:**
- Location: `Transcendence.WebAPI/Program.cs`
- Triggers: HTTP requests to `/api/*`
- Responsibilities:
  - Configures ASP.NET Core pipeline
  - Registers service dependencies via `AddTranscendenceCore()`
  - Registers repositories via `AddProjectSyndraRepositories()`
  - Configures Hangfire client (job enqueueing only, no server)
  - Maps controller routes

**Service (Worker/Jobs):**
- Location: `Transcendence.Service/Program.cs`
- Triggers: Hangfire scheduler (recurring jobs) and job queue
- Responsibilities:
  - Configures Worker host with Hangfire server (executes jobs)
  - Registers Riot API client via `AddTranscendenceRiot()`
  - Runs either `DevelopmentWorker` or `ProductionWorker` based on environment
  - Registers all core services with Riot API key

**Admin Portal:**
- Location: `Transcendence.WebAdminPortal/Program.cs`
- Triggers: HTTP requests to `/hangfire`
- Responsibilities: Exposes Hangfire dashboard for monitoring

## Error Handling

**Strategy:** Exception propagation with logging and Hangfire retry policies

**Patterns:**

- **Controller Level:** Try-parse for platform region validation; return 400 BadRequest for invalid input; 202 Accepted with retry-after for refresh in progress
- **Job Level:** Try-catch in `SummonerRefreshJob` with logging; finally block guarantees lock release; individual match failures logged but don't halt job
- **Hangfire Retry:** `AutomaticRetryAttribute` with `Attempts = 0` configured globally (no automatic retries; manual job re-enqueueing)
- **DB Operations:** SaveChangesAsync called per operation; individual match inserts wrapped in try-catch to prevent cascade failure

## Cross-Cutting Concerns

**Logging:**
- Approach: `ILogger<T>` injected into services and jobs
- Pattern: Structured logging with context (GameName, TagLine, MatchId, etc.) at job boundaries and error points
- Location: Throughout `Transcendence.Service.Core/Services/` and `Transcendence.WebAPI/Controllers/`

**Validation:**
- Approach: Route-based (platform region mapping), query string defaults (recentGamesCount, topChampions)
- Pattern: `TryParsePlatformRoute` helper in `SummonersController` maps short forms (NA, EUW) to official routes (NA1, EUW1)
- Location: Controller methods; service methods assume valid inputs

**Authentication:**
- Status: Not implemented
- Comment: API is currently open; candidates for future: API key in header, OAuth2 via Riot

**Database Migrations:**
- Tool: Entity Framework Core migrations
- Location: `Transcendence.Service/Migrations/`
- Pattern: MigrationsAssembly set to "Transcendence.Service" (separate project holds migrations)

---

*Architecture analysis: 2026-01-31*
