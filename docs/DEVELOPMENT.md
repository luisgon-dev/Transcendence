# Development

This repo contains a .NET backend (API + background worker) and a Next.js web frontend.

## Prerequisites

- .NET SDK (see `global.json`)
- Docker Desktop (recommended) or local:
  - PostgreSQL 16+
  - Redis 7+
- Node.js (recommended: Node 22)
- pnpm (repo pins `pnpm@10.22.0` in root `package.json`)

## Quick Start (Recommended)

1. Start infrastructure + backend:

```bash
docker compose up --build
```

2. Install JS dependencies:

```bash
corepack pnpm install
```

3. Install repo Git hooks (recommended once per clone):

```bash
corepack pnpm hooks:install
```

4. Configure the web app:

```bash
cp apps/web/.env.example apps/web/.env.local
```

Set:
- `TRN_BACKEND_BASE_URL=http://localhost:8080`
- `TRN_BACKEND_API_KEY=<api key for AppOnly endpoints>`
  - Note: the web app allowlists AppOnly proxy paths; this key is not exposed as a generic proxy capability.

Optional (admin bootstrap):
- `ADMIN_BOOTSTRAP_EMAIL_0=<your-admin-email>` before `docker compose up`
- Register/login that email in web app, then open `/admin`

Optional:
- `TRN_BACKEND_TIMEOUT_MS=10000` (server-side backend timeout, milliseconds)
- `TRN_ERROR_VERBOSITY=safe|verbose` (controls user-visible error detail from Next route handlers)

5. Run the web app:

```bash
corepack pnpm web:dev
```

Web: `http://localhost:3000`

API health:
- `http://localhost:8080/health/live`
- `http://localhost:8080/health/ready`

## Run Without Docker (Backend)

### Secrets

`Transcendence.WebAPI`:

```bash
dotnet user-secrets set "ConnectionStrings:MainDatabase" "Host=localhost;Port=5432;Database=transcendence;Username=postgres;Password=postgres" --project Transcendence.WebAPI
dotnet user-secrets set "ConnectionStrings:Redis" "localhost:6379" --project Transcendence.WebAPI
dotnet user-secrets set "ConnectionStrings:RiotApi" "RGAPI-your-key" --project Transcendence.WebAPI
dotnet user-secrets set "Auth:Jwt:Key" "CHANGE_THIS_TO_A_REAL_32+_CHAR_SECRET" --project Transcendence.WebAPI
dotnet user-secrets set "Auth:Jwt:RequireKeyInDevelopment" "false" --project Transcendence.WebAPI
dotnet user-secrets set "Auth:AdminBootstrap:Emails:0" "admin@example.com" --project Transcendence.WebAPI
dotnet user-secrets set "Auth:BootstrapApiKey" "trn_bootstrap_dev_key" --project Transcendence.WebAPI
dotnet user-secrets set "Auth:BootstrapApiKeyEnabledInDevelopmentOnly" "true" --project Transcendence.WebAPI
```

Security notes:
- `Auth:Jwt:Key` is required outside `Development`; startup fails if missing or if the known development placeholder is used.
- `Auth:BootstrapApiKeyEnabledInDevelopmentOnly=true` rejects bootstrap API key auth outside `Development`.

`Transcendence.Service`:

```bash
dotnet user-secrets set "ConnectionStrings:MainDatabase" "Host=localhost;Port=5432;Database=transcendence;Username=postgres;Password=postgres" --project Transcendence.Service
dotnet user-secrets set "ConnectionStrings:Redis" "localhost:6379" --project Transcendence.Service
dotnet user-secrets set "ConnectionStrings:RiotApi" "RGAPI-your-key" --project Transcendence.Service
```

### Database migrations

```bash
dotnet ef database update --project Transcendence.Service --startup-project Transcendence.Service
```

Migration policy:
- Do not hand-author or hand-edit EF migration files.
- Generate migrations only via EF CLI (for example: `dotnet ef migrations add <Name> --project Transcendence.Service --startup-project Transcendence.Service`).

### Run services

```bash
dotnet run --project Transcendence.WebAPI
dotnet run --project Transcendence.Service
```

Admin web UI runs in `apps/web` under `/admin` and requires an authenticated user with `admin` role.
Admin diagnostics include `/admin/jobs` (including failed-job detail) and `/admin/logs` (service/webapi operational logs).
`/api/auth/logout` revokes the active refresh token server-side and the web logout flow calls it before clearing cookies.

### Operational Log Files

`Transcendence.WebAPI` and `Transcendence.Service` both write structured operational log lines to files.

- Config section: `OperationalLogs`
- Keys:
  - `OperationalLogs:ServiceName` (`webapi` or `service`)
  - `OperationalLogs:DirectoryPath` (default `logs`)
  - `OperationalLogs:MinLevel` (default `Information`)

In Docker Compose (`docker-compose.yml` and `docker-compose.production.yml`), both services mount a shared `operational_logs` volume at `/var/log/transcendence` so admin APIs can read both log streams.

## Web Commands

From repo root:

```bash
corepack pnpm backend:test
corepack pnpm hooks:install
corepack pnpm precommit:check
corepack pnpm web:dev
corepack pnpm web:test
corepack pnpm web:lint
corepack pnpm web:build
```

## Backend Tests

From repo root:

```bash
dotnet test tests/Transcendence.Service.Core.Tests
dotnet test tests/Transcendence.WebAPI.Tests
```

Current `web:test` scope:
- Utility/unit tests in `apps/web/lib/*.test.ts`
- Runs in Vitest `node` environment (no DOM harness needed for current test suite)

Note:
- `apps/web` package scripts `dev` and `build` prebuild `@transcendence/api-client` automatically, so direct commands such as `pnpm --filter web dev` and `pnpm --filter web build` work without a separate manual client build step.

## OpenAPI + TypeScript Client

Source of truth: `openapi/transcendence.v1.json`

```bash
corepack pnpm api:gen
corepack pnpm api:check
```

If hooks are installed (`corepack pnpm hooks:install`), pre-commit runs path-aware checks automatically before each commit:
- `pnpm precommit:api-sync` runs only when staged files touch API-relevant paths (`Transcendence.WebAPI/`, `Transcendence.Service.Core/`, `Transcendence.Data/`, `scripts/openapi/export.sh`, OpenAPI/client artifacts), then stages regenerated artifacts.
- `pnpm precommit:check` runs `git diff --cached --check` to catch staged whitespace issues.

## Background Job Tuning

Key worker settings live under `Jobs:*` in `Transcendence.Service/appsettings*.json`.

### Development Worker Scope

When `Transcendence.Service` runs in the `Development` environment, the `DevelopmentWorker` schedules only analytics-oriented recurring jobs:

- `refresh-champion-analytics`
- `refresh-champion-analytics-adaptive` (when enabled)
- `champion-analytics-ingestion` (when enabled)
- `summoner-maintenance` (when enabled)
- `match-timeline-backfill` (when enabled)

It explicitly removes non-analytics recurring jobs (`detect-patch`, `retry-failed-matches`, `poll-live-games`) from the scheduler to keep local runs focused on analytics behavior.

### Production Startup Bootstrap

When `Transcendence.Service` runs in non-development environments, the `ProductionWorker` can queue startup bootstrap jobs so analytics is available sooner after deploy:

- `Jobs:Schedule:RunPatchDetectionOnStartup=true` runs patch detection immediately on startup.
- After startup patch detection, the worker queues analytics ingestion (when enabled) and adaptive analytics refresh.

### Champion Analytics Ingestion

`Jobs:ChampionAnalyticsIngestion` now supports:

- `MinimumSuccessfulMatchesForCurrentPatch`
- `TargetSuccessfulMatchesForCurrentPatch`
- `DataStaleAfterMinutes`
- `MaxCandidateSummonersPerRun`
- `MinRefreshJobsToQueuePerRun`
- `MaxRefreshJobsToQueuePerRun`
- `RefreshLockMinutes`
- `PrioritizeFavoriteSummoners`
- `FallbackToTrackedSummoners`
- `PauseWhenApiPriorityRefreshActive`
- `NewPatchRampHours`
- `RampDataStaleAfterMinutes`
- `RampMaxCandidateSummonersPerRun`
- `RampMinRefreshJobsToQueuePerRun`
- `RampMaxRefreshJobsToQueuePerRun`

This job determines when low-priority refresh can widen beyond ranked-only ingestion during early patch windows.

### Match Ingestion Windows

`Jobs:MatchIngestion` supports:

- `MatchIdsPageSize`
- `HighPriorityRankedPages`
- `HighPriorityAllModesHeadPages`
- `HighPriorityNonRankedBackfillMaxPages`
- `LowPriorityRankedPages`
- `LowPriorityAllModesHeadPages`
- `LowPriorityNonRankedBackfillMaxPages`

High-priority user refresh always executes ranked head sync first, then all-mode sync/backfill. Low-priority ingestion preserves ranked-first behavior and can be preempted by active API-priority refresh demand.

Non-ranked backfill ordering is tracked per summoner with `SummonerIngestionCursors` to ensure monotonic progress across repeated low-priority runs.

### Summoner Maintenance

`Jobs:SummonerMaintenance` supports:

- `MaxCandidateSummonersPerRun`
- `MaxRefreshJobsToQueuePerRun`
- `DataStaleAfterMinutes`
- `RefreshLockMinutes`
- `PrioritizeFavoriteSummoners`
- `PauseWhenApiPriorityRefreshActive`
- `NewPatchRampHours`
- `RampMaxCandidateSummonersPerRun`
- `RampMaxRefreshJobsToQueuePerRun`
- `RampDataStaleAfterMinutes`

This recurring job refreshes stale summoners in low-priority mode when no active high-priority API refresh lock exists.

### Refresh Lock Lifecycle Cleanup and Telemetry

`Jobs:Schedule` refresh-lock lifecycle settings:

- `RefreshLockLifecycleCleanupCron` (default `*/5 * * * *`)
- `EnableRefreshLockLifecycleCleanup` (default `true`)
- `RefreshLockLifecycleForensicsWindowMinutes` (default `30`)
- `RefreshLockLifecycleCleanupBatchSize` (default `250`, development profile `100`)
- `RefreshLockLifecycleCleanupMaxBatchesPerRun` (default `8`, development profile `4`)

Telemetry emitted by refresh lock lifecycle instrumentation uses consistent tags:

- `lock_class`
- `platform_region`
- `outcome`
- `source`

Metric names:

- `transcendence.refresh_lock.lifecycle.events`
- `transcendence.refresh_lock.contention.wait_hint_seconds`
- `transcendence.refresh_lock.cleanup.deleted`
- `transcendence.refresh_lock.cleanup.duration_ms`
- `transcendence.refresh_lock.growth.active`
- `transcendence.refresh_lock.growth.expired`
- `transcendence.refresh_lock.growth.deleted_last_run`

Structured log event names:

- `refresh_lock.lifecycle`
- `refresh_lock.contention_wait_hint`
- `refresh_lock.cleanup`
- `refresh_lock.growth_snapshot`

Operational monitoring baseline:

- Track contention trend by lock class/region: compare `outcome=contention` against `outcome=acquired`.
- Track cleanup effectiveness: watch `growth.expired` and `growth.deleted_last_run` together; rising expired with flat deleted indicates retention pressure.
- Track cleanup health: watch `refresh_lock.cleanup` outcomes (`completed`, `canceled`, `failed`) and duration growth over time.

### Timeline Ingestion

`Jobs:TimelineIngestion` supports:

- `Enabled`
- `MinuteMark`
- `MaxRetryAttempts`
- `BackfillBatchSize`
- `BackfillMaxEnqueuesPerRun`
- `BackfillCurrentPatchOnly`
- `PauseWhenApiPriorityRefreshActive`

Timeline ingestion persists ranked @15 snapshots and fetch status for matchup depth analytics.

### Rune Selection Integrity Backfill

`Jobs:Schedule` now supports:

- `RuneSelectionIntegrityBackfillCron`
- `EnableRuneSelectionIntegrityBackfill`
- `SummonerMaintenanceCron`
- `SummonerMaintenanceRampCron`
- `EnableSummonerMaintenance`
- `MatchTimelineBackfillCron`
- `EnableMatchTimelineBackfill`
- `RefreshChampionAnalyticsRampCron`
- `ChampionAnalyticsIngestionRampCron`
- `EnableNewPatchRamp`

`Jobs:RuneSelectionIntegrityBackfill` supports:

- `BatchSize`
- `MaxBatchesPerRun`

### Analytics Compute Thresholds

Analytics sampling thresholds are configurable in both API and worker hosts:

- `Analytics:Compute:MinimumGamesRequired`
- `Analytics:Compute:EarlyPatchMinimumGamesRequired`
- `Analytics:Compute:EarlyPatchWindowHours`

### Analytics Response Sampling

- Analytics APIs now expose sample metadata fields (`sampleStatus`, `sampleSize`, `minimumRecommendedSampleSize`, `patchAgeHours`, `isEarlyPatchWindow`).
- Current behavior is current-patch only (no previous-patch fallback responses).

## Documentation Policy (Contributor Requirement)

If a change affects any of the following, update docs in the same PR:

- Runtime behavior, user flows, or UI routes: update `README.md` and/or `docs/ARCHITECTURE.md`
- API endpoints, payloads, auth, or status codes: update `docs/API.md` and ensure OpenAPI is up to date
- Environment variables, secrets, compose, or run commands: update `docs/DEVELOPMENT.md`
