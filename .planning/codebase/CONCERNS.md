# Codebase Concerns Map

## Scope
- Focus: technical debt and operational risk areas that can block safe iteration.
- Timebox: repository snapshot analyzed on 2026-03-04.

## High Priority Concerns

### 1) Worker scheduling logic is duplicated and drift-prone
- Risk: `DevelopmentWorker` and `ProductionWorker` contain near-duplicate recurring-job orchestration, so new jobs/flags can diverge between environments.
- Evidence:
  - `Transcendence.Service/Workers/DevelopmentWorker.cs`
  - `Transcendence.Service/Workers/ProductionWorker.cs`
- Planning direction: extract shared scheduling policy/service and keep only environment-specific toggles in each worker.

### 2) Startup/job orchestration favors "continue on error" over fail-fast
- Risk: repeated `catch (Exception)` with "continuing startup" can leave partial scheduling state that looks healthy but is missing critical jobs.
- Evidence:
  - `Transcendence.Service/Workers/ProductionWorker.cs`
  - `Transcendence.Service/Workers/DevelopmentWorker.cs`
  - `Transcendence.Service.Core/Services/Jobs/LiveGamePollingJob.cs`
- Planning direction: classify recoverable vs non-recoverable failures; fail startup for required jobs.

### 3) Cancellation tokens are routinely discarded in background execution
- Risk: many jobs are enqueued/scheduled with `CancellationToken.None`, reducing graceful shutdown behavior and making long operations harder to interrupt.
- Evidence:
  - `Transcendence.Service/Workers/ProductionWorker.cs`
  - `Transcendence.Service/Workers/DevelopmentWorker.cs`
  - `Transcendence.Service.Core/Services/RiotApi/Implementations/MatchService.cs`
  - `Transcendence.Service.Core/Services/Jobs/ChampionAnalyticsIngestionJob.cs`
  - `Transcendence.Service.Core/Services/Jobs/SummonerMaintenanceJob.cs`
- Planning direction: standardize cancellation propagation and define explicit non-cancelable boundaries.

### 4) Core analytics/stat services are monolithic and memory-heavy
- Risk: very large classes and repeated `ToListAsync` + in-memory grouping increase blast radius, review difficulty, and runtime memory pressure as data grows.
- Evidence:
  - `Transcendence.Service.Core/Services/Analytics/Implementations/ChampionAnalyticsComputeService.cs` (1353 lines)
  - `Transcendence.Service.Core/Services/Analysis/Implementations/SummonerStatsService.cs` (911 lines)
  - `Transcendence.WebAPI/Controllers/SummonersController.cs` (446 lines)
- Planning direction: split query/aggregation pipelines by feature and move expensive aggregations to bounded SQL projections.

### 5) Refresh-lock storage can grow unbounded by key cardinality
- Risk: lock keys are derived from user/game identifiers and only lease timestamps are updated; no cleanup path exists for stale lock rows.
- Evidence:
  - `Transcendence.Data/Repositories/Implementations/RefreshLockRepository.cs`
  - `Transcendence.Service.Core/Services/Jobs/RefreshLockKeys.cs`
  - `Transcendence.Data/Models/Service/RefreshLock.cs`
- Planning direction: add cleanup/retention job and tighten key strategy/normalization constraints.

## Medium Priority Concerns

### 6) Security footguns in development defaults
- Risk: repository defaults include development bootstrap/API secrets and permissive dashboard access behavior that can be misused in shared environments.
- Evidence:
  - `Transcendence.Service.Core/Services/Auth/Implementations/JwtService.cs`
  - `Transcendence.WebAPI/appsettings.Development.json`
  - `docker-compose.yml`
  - `Transcendence.WebAdminPortal/Program.cs`
- Planning direction: enforce explicit opt-in for insecure dev defaults and document hard guardrails for remote/shared dev hosts.

### 7) Rate-limit partitioning does not account for proxy forwarding
- Risk: auth limiter partitions only by `RemoteIpAddress`; behind reverse proxies this can collapse many users into one partition.
- Evidence:
  - `Transcendence.WebAPI/Program.cs`
- Planning direction: add forwarded-header middleware and trusted-proxy configuration before partition key computation.

### 8) Test surface is narrower than exposed API/worker behavior
- Risk: only a subset of controllers and core services have direct tests, leaving admin/live-game/API-key/job orchestration paths with lower regression detection.
- Evidence:
  - Controllers: `Transcendence.WebAPI/Controllers/*.cs` (10 controllers)
  - API tests: `tests/Transcendence.WebAPI.Tests/AuthControllerTests.cs`, `tests/Transcendence.WebAPI.Tests/SummonersControllerTests.cs`, `tests/Transcendence.WebAPI.Tests/SummonerStatsControllerTests.cs`, `tests/Transcendence.WebAPI.Tests/ApiExceptionHandlerTests.cs`
  - Service tests: `tests/Transcendence.Service.Core.Tests/*.cs`
- Planning direction: prioritize tests for `AdminOperationsController`, `ApiKeysController`, `LiveGameController`, worker scheduling, and lock semantics.

### 9) Dependency stability risk from nightly package usage
- Risk: Riot API client is pinned to a nightly build, increasing probability of upstream breakage or unexpected behavior changes.
- Evidence:
  - `Transcendence.Service.Core/Transcendence.Service.Core.csproj`
- Planning direction: move to stable release, or lock nightly update cadence with compatibility test gates.

### 10) Migration history is large and unevenly named
- Risk: high migration count and inconsistent naming increase review friction and make schema intent harder to track over time.
- Evidence:
  - `Transcendence.Service/Migrations/` (45 C# migration files)
  - `Transcendence.Service/Migrations/20251001040524_More.cs`
  - `Transcendence.Service/Migrations/20251009194304_Non Null.cs`
  - `Transcendence.Service/Migrations/20251011043309_Rank Cleanup.cs`
- Planning direction: improve migration naming discipline and add periodic schema-baseline checkpoints.

### 11) Password reset endpoint is a placeholder implementation
- Risk: exposed endpoint exists but does not execute a reset flow, which can create product/security expectation gaps.
- Evidence:
  - `Transcendence.Service.Core/Services/Auth/Implementations/UserAuthService.cs`
  - `Transcendence.WebAPI/Controllers/AuthController.cs`
- Planning direction: implement tokenized reset workflow or explicitly hide/deprecate endpoint until implemented.

## Cross-Cutting Planning Notes
- Favor "operational correctness first" phases: worker reliability, lock lifecycle, cancellation propagation.
- Then address maintainability: service/controller decomposition and coverage expansion.
- Keep security-default hardening coupled with docs updates in `README.md` and `docs/DEVELOPMENT.md`.
