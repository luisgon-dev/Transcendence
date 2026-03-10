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
cp .env.example .env
docker compose up --build
```

Compose reads local backend credentials from the repo-root [`.env.example`](../.env.example). Copy it to an untracked `.env` before first run. The current Riot key variables are:

- `RIOT_API_KEY_LOL`
- `RIOT_API_KEY_TFT`

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

## Local E2E Workflows

Use full Compose when you want the simplest end-to-end path and you need the worker running for LoL/TFT refresh flows:

```bash
cp .env.example .env
corepack pnpm e2e:stack
```

That script:
- creates `.env` from `.env.example` if needed
- starts the full Docker Compose stack in detached mode
- waits for `http://localhost:8080/health/ready`
- waits for `http://localhost:3000`
- runs Playwright against `http://localhost:3000`

For a faster frontend loop, keep backend services in Compose and run Next locally:

```bash
docker compose up --build -d postgres redis webapi service
cp apps/web/.env.example apps/web/.env.local
corepack pnpm web:dev
corepack pnpm e2e:local
```

Rule of thumb:
- Use `corepack pnpm e2e:stack` for true local E2E and TFT/worker verification.
- Use the hybrid mode for day-to-day UI changes when you want faster frontend rebuilds.

## Local Data Slice and Riot Identifier Tools

For local gameplay testing, prefer importing a disposable game-only slice over trying to hand-seed a tiny database.

Package scripts:

```bash
corepack pnpm data:slice:sync -- --help
corepack pnpm data:validate-identifiers -- --help
corepack pnpm data:rehydrate-riot-ids -- --help
```

Recommended shell env:

```bash
export TRN_SOURCE_DB='Host=192.168.0.221;Port=5432;Database=transcendence;Username=postgres;Password=testpassword123!'
export TRN_TARGET_DB='Host=localhost;Port=5432;Database=transcendence_slice;Username=postgres;Password=changme'
export TRN_RIOT_API_KEY_LOL='RGAPI-your-lol-key'
export TRN_RIOT_API_KEY_TFT='RGAPI-your-tft-key'
```

Use a disposable target database. The slice sync truncates and reloads slice-owned tables before import.

Example import with explicit sizing:

```bash
corepack pnpm data:slice:sync -- \
  --regions NA1,EUW1,KR \
  --patch-depth 2 \
  --lol-max-matches-per-region 2500 \
  --lol-sample-percent 25 \
  --tft-max-matches-per-region 1500 \
  --tft-sample-percent 20
```

Sizing controls:
- `--regions <csv>` limits source platform regions.
- `--patch-depth <n>` limits the patch/set windows considered.
- `--lol-max-matches-per-region <n>` and `--tft-max-matches-per-region <n>` hard-cap imported matches per region.
- `--lol-sample-percent <0-100>` and `--tft-sample-percent <0-100>` sample a percentage of the eligible match pool before the hard cap is applied.
- `--skip-lol` and `--skip-tft` let you import a single game surface.

What gets copied:
- LoL/TFT summoners, ranks, match data, match dependents, ingestion cursors, live/pro rows, and static data needed for analytics pages.
- Auth/admin/API-key tables are intentionally excluded.

Safety checks:
- Source and target must have the same latest EF migration.
- The tool prints per-table row counts when the import finishes.

Post-import validation:

```bash
corepack pnpm data:validate-identifiers -- --sample-size 10
```

This reports:
- the app’s identifier policy
- DB counts for canonical vs non-canonical Riot identifiers
- optional live Riot checks showing whether `Puuid` + Riot ID are still canonical and whether encrypted IDs drifted under the current key

Key rotation / key swap workflow:

```bash
corepack pnpm data:validate-identifiers -- --sample-size 10
corepack pnpm data:rehydrate-riot-ids -- --games all --limit 250 --only-missing
```

`data:rehydrate-riot-ids` refreshes non-canonical `RiotSummonerId` / `AccountId` plus Riot ID display fields from stored `Puuid` rows using the current Riot API keys. It is safe to rerun and supports:
- `--games all|lol|tft`
- `--limit <n>`
- `--delay-ms <n>`
- `--only-missing`

## Run Without Docker (Backend)

### Secrets

`Transcendence.WebAPI`:

The WebAPI host is keyless. It does not need Riot API keys to start, serve reads, or export Swagger.

```bash
dotnet user-secrets set "ConnectionStrings:MainDatabase" "Host=localhost;Port=5432;Database=transcendence;Username=postgres;Password=changme" --project Transcendence.WebAPI
dotnet user-secrets set "ConnectionStrings:Redis" "localhost:6379" --project Transcendence.WebAPI
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
dotnet user-secrets set "ConnectionStrings:MainDatabase" "Host=localhost;Port=5432;Database=transcendence;Username=postgres;Password=changme" --project Transcendence.Service
dotnet user-secrets set "ConnectionStrings:Redis" "localhost:6379" --project Transcendence.Service
dotnet user-secrets set "RiotApi:League:ApiKey" "RGAPI-your-lol-key" --project Transcendence.Service
dotnet user-secrets set "RiotApi:Tft:ApiKey" "RGAPI-your-tft-key" --project Transcendence.Service
```

Local defaults:
- Shared backend defaults live in [`config/backend.shared.json`](../config/backend.shared.json) and use PostgreSQL/Npgsql on `localhost:5432` with `postgres/changme`, plus Redis on `localhost:6379`.
- `Transcendence.WebAPI/appsettings.json` and `Transcendence.Service/appsettings.json` contain host-only settings layered on top of the shared config.
- User-secrets remain the recommended override for local credentials and Riot keys.

Riot API key model:
- Only `Transcendence.Service` resolves Riot keys from the canonical nested settings:
  - `RiotApi:League:ApiKey`
  - `RiotApi:Tft:ApiKey`
- The legacy `ConnectionStrings:RiotApi` setting is no longer used.

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

### TFT Local Smoke Test

After the migration is applied and both hosts are running:

1. Verify static-data warmup:

```bash
curl http://localhost:8080/api/tft/analytics/champions
```

2. Queue a TFT refresh for a known Riot ID:

```bash
curl -X POST http://localhost:8080/api/tft/summoners/na1/<gameName>/<tagLine>/refresh
```

3. Poll until the stored profile becomes available:

```bash
curl http://localhost:8080/api/tft/summoners/na1/<gameName>/<tagLine>
```

4. Verify the web surfaces:
- `/tft`
- `/tft/comps`
- `/tft/summoners/na/<gameName>-<tagLine>`

Notes:
- TFT analytics catalog/detail endpoints serve the active set only.
- The worker must have a valid `RiotApi:Tft:ApiKey` for TFT refresh/bootstrap to succeed.
- Static-data refresh pulls from CommunityDragon, so the local machine must have outbound access to `raw.communitydragon.org`.

Admin web UI runs in `apps/web` under `/admin` and requires an authenticated user with `admin` role.
Admin diagnostics include:
- `/admin` for worker/server status, database + analysis metrics, and top backlog groups
- `/admin/jobs` for queue-state exploration, recurring-producer pause/resume, and backlog clearing
- `/admin/logs` for service/webapi operational logs
`/api/auth/logout` revokes the active refresh token server-side and the web logout flow calls it before clearing cookies.
WebAPI now defaults `Microsoft.EntityFrameworkCore.Database.Command` to `Warning` so operational logs are not dominated by insert/update chatter.

### Operational Log Files

`Transcendence.WebAPI` and `Transcendence.Service` both write structured operational log lines to files.

- Config section: `OperationalLogs`
- Keys:
  - `OperationalLogs:ServiceName` (`webapi` or `service`)
  - `OperationalLogs:DirectoryPath` (default `logs`)
  - `OperationalLogs:MinLevel` (default `Information`)
- Admin-reader overrides in `Transcendence.WebAPI`:
  - `AdminLogs:Sources:webapi:DirectoryPath` (optional explicit path for `webapi.log`)
  - `AdminLogs:Sources:service:DirectoryPath` (optional explicit path for `service.log`)

In Docker Compose (`docker-compose.yml` and `docker-compose.production.yml`), both services mount a shared `operational_logs` volume at `/var/log/transcendence` so admin APIs can read both log streams.
The admin logs API scans the live file plus rotated `*.log.N` archives and reports whether the selected source is currently available. In non-compose or split-host setups, configure the `AdminLogs:Sources:*:DirectoryPath` overrides in the Web API so `/api/admin/logs/services` can find worker logs outside the Web API's own content root.
The logger provider now pre-creates the target `*.log` file and writes a one-time stderr warning if the process cannot create or append the file. In container deployments, that warning appears in the container's stdout/stderr stream and is the first place to check when `service.log` is missing.

Compose env contract:
- [`compose.yml`](../compose.yml) injects Riot keys with `RiotApi__League__ApiKey` and `RiotApi__Tft__ApiKey`.
- The repo-root [`.env.example`](../.env.example) uses matching variables:
  - `RIOT_API_KEY_LOL`
  - `RIOT_API_KEY_TFT`

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
- `apps/web` package scripts `dev`, `build`, `lint`, and `test` prebuild `@transcendence/api-client` automatically, so direct commands such as `pnpm --filter web build` and `pnpm --filter web test` work without a separate manual client build step.

## OpenAPI + TypeScript Client

Source of truth: `openapi/transcendence.v1.json` (committed). The generated client schema is rebuilt locally from that spec and is not committed.

```bash
corepack pnpm api:gen
corepack pnpm api:check
```

If hooks are installed (`corepack pnpm hooks:install`), pre-commit runs path-aware checks automatically before each commit:
- `pnpm precommit:api-sync` runs only when staged files touch API-relevant paths (`Transcendence.WebAPI/`, `Transcendence.Service.Core/`, `Transcendence.Data/`, `scripts/openapi/export.sh`, committed OpenAPI spec), regenerates the client locally, and stages the refreshed spec.
- `pnpm precommit:check` runs `git diff --cached --check` to catch staged whitespace issues.

## Background Job Tuning

Key worker settings live under `Jobs:*` in `Transcendence.Service/appsettings.json`.
The public Web API also consumes `Jobs:MultiRegionIngestion` from `Transcendence.WebAPI/appsettings.json` so `/api/analytics/regions` and region-filter normalization stay aligned with the ingestion regions exposed to the frontend.

### Development Worker Scope

When `Transcendence.Service` runs in the `Development` environment, the `DevelopmentWorker` schedules only analytics-oriented recurring jobs:

- `refresh-champion-analytics`
- `refresh-champion-analytics-adaptive` (when enabled)
- `champion-analytics-ingestion` (when enabled)
- `summoner-maintenance` (when enabled)
- `tft-static-data-refresh` (when enabled)
- `tft-analytics-refresh` (when enabled)
- `tft-analytics-ingestion` (when enabled)
- `tft-summoner-maintenance` (when enabled)
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

### Adaptive Throughput Budget Policy

`Jobs:AdaptiveThroughputBudget` supports:

- `VelocityLookbackMinutes` (default `30`)
- `HighPressureCooldownMinutes` (default `8`)
- `CatchUpHoldMinutes` (default `12`)
- `ModeSwitchCooldownMinutes` (default `4`)
- `CatchUpCoverageThreshold` (default `0.85`)
- `CatchUpBacklogAgeMinutes` (default `45`)
- `CatchUpCandidatePressureThreshold` (default `1.1`)
- `MinimumRecentVelocityPerHour` (default `12.0`)
- `CatchUpQueueBurstMultiplier` (default `1.6`)
- `CatchUpCandidateBurstMultiplier` (default `1.8`)
- `HighPressureCandidateMultiplier` (default `0.25`)
- `MaxQueueTargetHardCap` (default `40`)
- `MaxCandidateHardCap` (default `500`)

These settings determine per-run producer mode (`HighPressure`, `Balanced`, `CatchUp`), queue target, and candidate ceiling for low-priority ingestion producers.

### Starvation Guardrail Policy

`Jobs:StarvationGuardrail` supports:

- `Enabled` (default `true`)
- `MaxEligibleDeferAgeMinutes` (default `360`)
- `CatchUpWindowMinutes` (default `12`)
- `CatchUpCooldownMinutes` (default `20`)
- `ForcedCatchUpQueueTargetFloor` (default `1`)
- `ForcedCatchUpCandidateBurstMultiplier` (default `1.5`)
- `ForcedCatchUpMaxCandidateHardCap` (default `500`)

When defer age breaches the threshold, producers open a forced catch-up window and can continue low-priority progress even when API-priority demand is present.

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

### TFT Worker Jobs

`Jobs:Schedule` now also supports dedicated TFT recurring jobs:

- `TftStaticDataCron`
- `TftAnalyticsRefreshCron`
- `TftAnalyticsIngestionCron`
- `TftSummonerMaintenanceCron`
- `EnableTftStaticDataRefresh`
- `EnableTftAnalyticsRefresh`
- `EnableTftAnalyticsIngestion`
- `EnableTftSummonerMaintenance`

Queue model:
- TFT profile refresh: `tft-refresh-high`
- TFT analytics/static-data work: `tft-default`
- TFT maintenance/bootstrap work: `tft-refresh-low`

Static-data behavior:
- TFT champion/item/trait/augment catalog endpoints read only the currently active set.
- If a TFT static-data refresh fails while previously stored static data exists, the worker logs the failure and continues serving the stored active-set catalog.

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

### Ingestion Throughput Telemetry

Throughput telemetry emitted by low-priority producers uses these tags:

- `producer`
- `queue_tier`
- `mode`
- `outcome`
- `source`

Metric names:

- `transcendence.ingestion_throughput.decisions`
- `transcendence.ingestion_throughput.defer_age_breaches`
- `transcendence.ingestion_throughput.catch_up.lifecycle`
- `transcendence.ingestion_throughput.queue_target`
- `transcendence.ingestion_throughput.queued_count`
- `transcendence.ingestion_throughput.catch_up.window_minutes`

Structured log event names:

- `ingestion_throughput.budget_decision`
- `ingestion_throughput.guardrail_decision`
- `ingestion_throughput.catch_up_window`
- `ingestion_throughput.queue_output`

Operational monitoring baseline:

- Rising `outcome=api_priority_active` with `mode=highpressure` confirms high-priority demand is preempting low-priority throughput.
- Track `defer_age_breach` and catch-up lifecycle starts to verify starvation guardrails are triggering when expected.
- Compare `queue_target` against `queued_count` and queue-output outcomes to identify throttling (`stopped_api_priority_preemption`) vs candidate scarcity (`skipped_no_candidates`).

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
