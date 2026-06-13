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
- `/admin/jobs/[jobId]` for per-job detail (state history, inferred region, delete/retry)
- `/admin/logs` for service/webapi operational logs
- `/admin/audit` for the admin audit log
- `/admin/api-keys` for AppOnly API key management (list/rotate/revoke)
- `/admin/pro-summoners` for pro/tracked-roster curation

The pro-summoner CSV importer accepts required `gameName`, `tagLine`, and `platformRegion` columns plus optional `puuid`, `proName`, `teamName`, and `type`. A sourced starter import is available at `docs/seeds/probuild-pro-roster-2026-06-07.csv`.

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

In Docker Compose (`compose.yml`), both services mount a shared `operational_logs` volume at `/var/log/transcendence` so admin APIs can read both log streams.
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

Production defaults in `Transcendence.Service/appsettings.json` are coverage-first for LoL:
- `stable` keeps adaptive refresh (self-paced, ramp-aware), champion analytics ingestion, summoner maintenance, high-elo profile refresh, and low-frequency timeline backfill enabled.
- `high-elo-profile-refresh` keeps the tracked high-value roster populated for analytics ingestion.
- `match-timeline-backfill` is intentionally slower than ingestion because tier lists and core champion stats do not require timeline rows.
- `Jobs:Schedule:PurgeBacklogOnPatchRolloverOnStartup` is disabled and startup rollover logic preserves queued current-patch catch-up work.

### Recurring Job Scheduling (Development and Production)

`Transcendence.Service` hosts one of two background workers depending on environment (`Program.cs`): `DevelopmentWorker` when `ASPNETCORE_ENVIRONMENT=Development`, otherwise `ProductionWorker`. Both register the **same** recurring-job set through the shared `WorkerRecurringJobPolicy` — the two workers differ only in startup behavior, not in which recurring jobs they schedule.

Which recurring jobs are active is determined by:

- the per-job `Enable*` flags under `Jobs:Schedule` (for example `EnableChampionAnalyticsIngestion`, `EnableMatchTimelineBackfill`), and
- the resolved **scheduling profile** (`Jobs:Schedule:Profile`, falling back to `DefaultProfile`, default `stable`), whose `Jobs:SchedulingProfiles:Profiles:<name>:JobOverrides` can flip a job's `Enabled`/`Cron`/`MandatoryBaseline`. Profile overrides win over the descriptor defaults, and `poll-live-games` is disabled by default.

The base `appsettings.json` ships `Jobs:Schedule:Profile = "stable"` (there is no `appsettings.Development.json`), so a local worker resolves the **same `stable` profile as production** unless you override `Jobs:Schedule:Profile` (or individual `Enable*` / `JobOverrides` values) via user-secrets or environment variables. Under `stable` the enabled jobs are the LoL analytics-coverage set (adaptive analytics refresh, champion-analytics ingestion, summoner maintenance — each a single self-pacing job that tightens cadence during the new-patch ramp window — plus match-timeline backfill and high-elo profile refresh), the full TFT set (`tft-static-data-refresh`, `tft-analytics-refresh`, `tft-analytics-ingestion`, `tft-summoner-maintenance`), plus the baseline jobs (`detect-patch`, `retry-failed-matches`, `refresh-lock-lifecycle-cleanup`); `poll-live-games`, `rune-selection-integrity-backfill`, and the daily `refresh-champion-analytics` are disabled. The TFT analytics jobs were enabled in the `stable` profile when the TFT frontend went live; they run on the isolated `tft-*` queues using the separate `RiotApi:Tft:ApiKey`, so TFT demand never competes with LoL refresh throughput. TFT comps and player profiles stay empty until these jobs have ingested data, which requires a valid TFT Riot key and some ramp-up time after deploy.

`DevelopmentWorker`'s only environment-specific startup actions are: removing legacy/invalid recurring jobs (old `cache-warmup*` ids), an optional full Hangfire purge when `Jobs:Schedule:CleanupOnStartup=true` (default `false`), and a startup integrity check that fail-fasts on mandatory-baseline job failures. It does **not** run the production startup bootstrap described below.

### Production Startup Bootstrap

When `Transcendence.Service` runs in non-development environments, the `ProductionWorker` only queues startup bootstrap jobs when startup patch detection confirms patch skew:

- `Jobs:Schedule:RunPatchDetectionOnStartup=true` runs patch detection immediately on startup.
- `Jobs:Schedule:PurgeBacklogOnPatchRolloverOnStartup=false` keeps current-patch catch-up work intact across restarts.
- After startup patch detection confirms a rollover, the worker refreshes static data and queues a bounded analytics ingestion bootstrap without performing a blanket Hangfire purge.

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
- `PrioritizeTrackedHighValueSummoners`
- `PrioritizeRankedHighEloSummoners`
- `FallbackToTrackedSummoners`
- `PauseWhenApiPriorityRefreshActive`
- `NewPatchRampHours`
- `RampDataStaleAfterMinutes`
- `RampMaxCandidateSummonersPerRun`
- `RampMinRefreshJobsToQueuePerRun`
- `RampMaxRefreshJobsToQueuePerRun`
- `HighEloTiers`

This job now prefers tracked high-value roster entries and Emerald+ ranked candidates before it falls back to the broad stale summoner pool. Region coverage targets scale with `Jobs:MultiRegionIngestion:Regions[*]:Weight`.

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

- `Enabled` (default **`false`**)
- `MaxEligibleDeferAgeMinutes` (default `360`)
- `CatchUpWindowMinutes` (default `12`)
- `CatchUpCooldownMinutes` (default `20`)
- `ForcedCatchUpQueueTargetFloor` (default `1`)
- `ForcedCatchUpCandidateBurstMultiplier` (default `1.5`)
- `ForcedCatchUpMaxCandidateHardCap` (default `500`)

**Disabled by default.** The defer-age signal (oldest eligible summoner's `UpdatedAt` age ≥ `MaxEligibleDeferAgeMinutes`) is structurally unsatisfiable on the yield-limited personal key — ~4.1M summoners, only a few thousand <6h fresh — so it fired forced catch-up perpetually (bypassing the API-priority pause and driving the all-modes ancient-history grind). The adaptive throughput budget's coverage/velocity `CatchUp` mode and the cold-start override remain the legitimate bursting mechanisms. Re-enable only with a delta/growth-based defer signal, not absolute age. See `docs/ARCHITECTURE.md` → "Low-priority ingestion is ranked-head-only; defer-age guardrail retired".

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

Match-detail preparation is intentionally sequential inside each refresh job because the match service builds EF entity graphs using the job-scoped `DbContext`.

Non-ranked backfill ordering is tracked per summoner with `SummonerIngestionCursors` to ensure monotonic progress across repeated low-priority runs.

### Summoner Maintenance

`Jobs:SummonerMaintenance` supports:

- `MaxCandidateSummonersPerRun`
- `MaxRefreshJobsToQueuePerRun`
- `DataStaleAfterMinutes`
- `RefreshLockMinutes`
- `PrioritizeFavoriteSummoners`
- `PrioritizeTrackedHighValueSummoners`
- `PrioritizeRankedHighEloSummoners`
- `EnableAllModesWidening` (default **`false`**) — when off, low-priority maintenance refreshes are current-patch ranked-head-only; when on, they may widen into all-modes-head + non-ranked backfill if the adaptive budget reports `IncludeAllModes`. Off by default because the widening grinds an uncovered summoner's ancient history through the rate gate and saturates the discovery lane. `ChampionAnalyticsIngestionJob` is always ranked-head-only.
- `HighEloTiers`
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

All four TFT recurring jobs are enabled under the `stable` profile (see the scheduling-profile section above) now that the TFT web surface is live.

Static-data behavior:
- TFT champion/item/trait/augment catalog endpoints read only the currently active set.
- If a TFT static-data refresh fails while previously stored static data exists, the worker logs the failure and continues serving the stored active-set catalog.

### TFT web surface

The TFT frontend (`/tft/*`) is gated by `TFT_FRONTEND_ENABLED` in `apps/web/lib/featureFlags.ts` (now `true`). With the flag on, the header game switcher, command palette, landing page, and `/tft` routes are all live. Catalog pages (units/items/traits/augments) render from the active-set static data immediately; the comps tier list and player profiles populate only once the TFT analytics/ingestion jobs above have run against a valid TFT Riot key.

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
- `SummonerMaintenanceCron` (heartbeat cadence; the job self-paces — see below)
- `EnableSummonerMaintenance`
- `MatchTimelineBackfillCron`
- `EnableMatchTimelineBackfill`
- `ChampionAnalyticsIngestionCron` / `RefreshChampionAnalyticsAdaptiveCron` (heartbeat cadences)

> The separate `*-ramp` recurring jobs (`refresh-champion-analytics-ramp`, `champion-analytics-ingestion-ramp`, `summoner-maintenance-ramp`) and the `EnableNewPatchRamp` / `*RampCron` schedule keys were removed: new-patch ramp behavior is now folded into the base jobs, which self-pace from patch age (`SelfPaceRampIntervalMinutes` / `SelfPaceSteadyIntervalMinutes` on the producer options) and self-select ramp budget params from the existing `Ramp*` option values. See ARCHITECTURE.md → "Self-pacing (no separate ramp jobs)".

`Jobs:RuneSelectionIntegrityBackfill` supports:

- `BatchSize`
- `MaxBatchesPerRun`

### Analytics Compute Thresholds

Analytics sampling thresholds are configurable in both API and worker hosts:

- `Analytics:Compute:MinimumGamesRequired`
- `Analytics:Compute:MaturingPatchMinimumGamesRequired`
- `Analytics:Compute:EarlyPatchMinimumGamesRequired`
- `Analytics:Compute:BootstrapPatchMinimumGamesRequired`
- `Analytics:Compute:BootstrapWindowHours`
- `Analytics:Compute:ProvisionalWindowHours`
- `Analytics:Compute:MaturingWindowHours`

### Analytics Response Sampling

- Analytics APIs now expose sample metadata fields (`sampleStatus`, `sampleSize`, `minimumRecommendedSampleSize`, `patchAgeHours`, `isEarlyPatchWindow`, `patchPhase`, `isProvisional`).
- Current behavior is current-patch only (no previous-patch fallback responses).

## Documentation Policy (Contributor Requirement)

If a change affects any of the following, update docs in the same PR:

- Runtime behavior, user flows, or UI routes: update `README.md` and/or `docs/ARCHITECTURE.md`
- API endpoints, payloads, auth, or status codes: update `docs/API.md` and ensure OpenAPI is up to date
- Environment variables, secrets, compose, or run commands: update `docs/DEVELOPMENT.md`
