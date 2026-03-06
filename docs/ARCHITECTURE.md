# Architecture

Transcendence is a backend + web monorepo:

- A .NET Web API that serves reads and queues refresh jobs
- A .NET background worker that executes Hangfire jobs (refresh, ingestion, analytics, etc.)
- A Next.js web frontend that renders pages (SSR) and proxies to the backend via route handlers (BFF)

## Components

### `Transcendence.WebAPI`
- Public/authenticated REST API
- Enqueues background work (Hangfire) for expensive refresh operations
- Exposes OpenAPI/Swagger (spec is exported and committed under `openapi/`)

### `Transcendence.Service`
- Background host that runs Hangfire server and recurring jobs
- Executes ingestion/refresh/analytics workflows
- In `Development`, the worker narrows recurring schedules to analytics-oriented jobs only (analytics refresh/ingestion, summoner maintenance, timeline backfill)
- In `Production`, startup can bootstrap analytics immediately by running patch detection first, then queuing ingestion + adaptive analytics refresh (controlled by `Jobs:Schedule:RunPatchDetectionOnStartup`)

### `Transcendence.Service.Core`
- Domain/application services (analysis, analytics compute, auth, live game, Riot API integration, jobs)
- Called from WebAPI controllers and the worker host

### `Transcendence.Data`
- EF Core DbContext + entities + repositories
- PostgreSQL is the intended runtime database

### `apps/web` (Next.js)
- App Router pages + route handlers used as a BFF:
  - `/api/session/*` for browser auth/session interactions
  - `/api/trn/*` as proxy endpoints to backend (adds auth headers server-side)
- Tailwind styling, SSR-first pages where possible
- Admin dashboard routes under `/admin/*` for ops controls/reports (JWT `admin` role required)
- Frontend analysis routes:
  - `/tierlist`
  - `/champions/*`
  - `/matchups/*`
  - `/pro-builds/*`
  - `/summoners/[region]/[riotId]` is the unified profile + match history surface
    - Legacy `/summoners/[region]/[riotId]/matches*` routes redirect into this unified view using query state (`page`, `queue`, `expandMatchId`)

### `packages/api-client`
- Generated OpenAPI TypeScript client artifacts built from the committed spec
- Schema generation uses `openapi-typescript` + `openapi-fetch`

## Data Flow: Summoner Refresh

1. Client requests a summoner by Riot ID:
   - If data exists in DB: return immediately
   - If missing: return `202 Accepted` indicating refresh is needed
2. Client triggers refresh:
   - WebAPI acquires a refresh lock (prevents concurrent refreshes)
   - WebAPI enqueues Hangfire job
3. Worker performs refresh:
   - Calls Riot APIs
   - Upserts summoner/rank/match records
   - High-priority refresh sequence:
     - ranked solo/duo head sync first
     - all-mode head sync second
     - non-ranked backfill pagination (bounded by safety caps)
4. Client polls GET endpoint until data is ready (200 OK)

### Summoner Read Failure Semantics

- Summoner profile and stats reads fail closed on backend errors.
- `GET /api/summoners/{region}/{name}/{tag}` and `GET /api/summoners/{summonerId}/stats*` do not return synthetic empty success payloads when compute fails.
- API-wide exception handling maps known summoner stats compute failures to `500` ProblemDetails with a request trace id.
- The web BFF/UI consumes ProblemDetails `title`/`detail` fields so user-visible errors still degrade gracefully.

### Refresh Priority Orchestration

- API-triggered summoner refreshes are implicitly high-priority.
- API refresh requests create an additional lock key with prefix `refresh-priority:api:`.
- While any active `refresh-priority:api:` lock exists:
  - Champion analytics ingestion pauses.
  - Live game polling pauses.
  - Failed-match retry pauses.
- Hangfire queue ordering is configured as:
  - `refresh-high`
  - `default`
  - `refresh-low`
- API refresh jobs run on `refresh-high`; ingestion-driven refresh jobs run on `refresh-low`.
- Refresh locks use DB-backed lease semantics (atomic acquire/renew + explicit lease expiry on release) so concurrent lock races do not require lock-row deletion.

### Refresh Lock Lifecycle Telemetry and Retention

- Lock lifecycle telemetry is emitted as best-effort/non-blocking instrumentation from API controllers, repository lock operations, and the lifecycle cleanup job.
- Shared telemetry dimensions/tags:
  - `lock_class` (for example `summoner-refresh`, `refresh-priority:api`, `refresh-lock-lifecycle`)
  - `platform_region` (for example `NA1`, `EUW1`, `GLOBAL`)
  - `outcome` (for example `acquired`, `contention`, `completed`, `failed`, `snapshot`, `active`, `expired`, `deleted_last_run`)
  - `source` (call site, for example `summoners-controller`, `pro-summoners-controller`, `refresh-lock-lifecycle-job`)
- Primary metric instruments:
  - `transcendence.refresh_lock.lifecycle.events` (counter)
  - `transcendence.refresh_lock.contention.wait_hint_seconds` (histogram)
  - `transcendence.refresh_lock.cleanup.deleted` (counter)
  - `transcendence.refresh_lock.cleanup.duration_ms` (histogram)
  - `transcendence.refresh_lock.growth.active` / `.expired` / `.deleted_last_run` (observable gauges)
- Structured log event names are aligned to the same lifecycle contract:
  - `refresh_lock.lifecycle`
  - `refresh_lock.contention_wait_hint`
  - `refresh_lock.cleanup`
  - `refresh_lock.growth_snapshot`
- Default cleanup retention controls:
  - `Jobs:Schedule:EnableRefreshLockLifecycleCleanup=true`
  - `Jobs:Schedule:RefreshLockLifecycleCleanupCron=*/5 * * * *` (every 5 minutes)
  - `Jobs:Schedule:RefreshLockLifecycleForensicsWindowMinutes=30`
  - `Jobs:Schedule:RefreshLockLifecycleCleanupBatchSize=250` (`100` in development profile)
  - `Jobs:Schedule:RefreshLockLifecycleCleanupMaxBatchesPerRun=8` (`4` in development profile)
- Monitoring guidance (trend + threshold):
  - Alert on sustained contention trend: `outcome=contention` rising against `outcome=acquired` for the same `lock_class` + `platform_region` (for example >20% contention over a 15-minute window).
  - Alert on lock growth pressure: `growth.expired` rising across multiple cleanup runs while `growth.deleted_last_run` stays flat or near zero.
  - Alert on cleanup degradation: repeated `refresh_lock.cleanup` outcomes of `failed`/`canceled`, or `outcome=completed` runs where `stopped_by_batch_cap=true` persists with rising expired backlog.

### Continuous Analytics Ingestion

- Champion analytics ingestion now runs continuously in low-priority mode to keep growing current-patch data.
- Summoner maintenance runs continuously in low-priority mode to refresh stale summoners when no high-priority API refresh demand is active.
- Ingestion scales queued refresh count based on:
  - current patch coverage vs target
  - staleness of recent successful fetches
- Even when patch data is healthy, ingestion can queue a small minimum number of low-priority refreshes per run.
- Early patch mode remains ranked solo/duo-first until coverage targets are satisfied; once healthy, low-priority refresh can widen to all supported history queues.
- Low-priority refresh windows stop early whenever active high-priority API refresh demand is detected.
- Low-priority producer budgets (`ChampionAnalyticsIngestionJob`, `SummonerMaintenanceJob`) are selected by adaptive mode each run:
  - `HighPressure`: queue target `0` while API-priority demand is active.
  - `Balanced`: bounded queue target in normal conditions.
  - `CatchUp`: burst queue target/candidate ceiling when coverage/velocity/backlog signals indicate lag.
- Starvation guardrail is applied after adaptive budgeting:
  - Defer-age breach (`max eligible defer age >= threshold`) starts a forced catch-up window.
  - Catch-up windows are lock-backed (`refresh-priority:guardrail:catchup:*`) and paired with cooldown locks (`refresh-priority:guardrail:cooldown:*`) to prevent oscillation.
  - Forced catch-up can override producer pause and the low-priority executor's API-demand early exit only for guardrail-authorized work, preserving normal preemption for ordinary low-priority refreshes.
- New-patch ramp mode (first `Jobs:*:NewPatchRampHours`) schedules additional high-frequency analytics jobs:
  - `refresh-champion-analytics-ramp`
  - `champion-analytics-ingestion-ramp`
  - `summoner-maintenance-ramp`
- Ramp jobs are gated by active-patch age and no-op automatically after the configured ramp window.

### Ingestion Throughput Telemetry

- Throughput telemetry follows the same best-effort/non-blocking pattern as refresh-lock lifecycle telemetry so instrumentation failures cannot block producer execution.
- Shared throughput tags:
  - `producer` (`championanalyticsingestionjob`, `summonermaintenancejob`)
  - `queue_tier` (`refresh-low`)
  - `mode` (`highpressure`, `balanced`, `catchup`, `guardrail`)
  - `outcome` (for example `budget_applied`, `api_priority_active`, `defer_age_breach`, `started`, `queued_target_met`)
  - `source` (`champion-analytics-ingestion-job`, `summoner-maintenance-job`)
- Metric instruments:
  - `transcendence.ingestion_throughput.decisions` (counter)
  - `transcendence.ingestion_throughput.defer_age_breaches` (counter)
  - `transcendence.ingestion_throughput.catch_up.lifecycle` (counter)
  - `transcendence.ingestion_throughput.queue_target` (histogram)
  - `transcendence.ingestion_throughput.queued_count` (histogram)
  - `transcendence.ingestion_throughput.catch_up.window_minutes` (histogram)
- Structured log event names:
  - `ingestion_throughput.budget_decision`
  - `ingestion_throughput.guardrail_decision`
  - `ingestion_throughput.catch_up_window`
  - `ingestion_throughput.queue_output`
- Operational interpretation:
  - Sustained `mode=highpressure` with `outcome=api_priority_active` indicates API-triggered refresh demand is dominating throughput.
  - `defer_age_breach` and `catch_up_window` starts indicate fairness guardrail activation.
  - Compare queue target vs queued output to identify preemption (`stopped_api_priority_preemption`) or candidate scarcity (`skipped_no_candidates`).

### Analytics Response Semantics

- Analytics APIs intentionally do not fall back to previous patch payloads.
- Responses include sample metadata (`sampleStatus`, `sampleSize`, `minimumRecommendedSampleSize`, `patchAgeHours`, `isEarlyPatchWindow`) so web surfaces can show early-patch low-sample/no-data states explicitly.

### Match Queue Scope and History

- Match rows now persist queue metadata (`queueId`, `queueFamily`, `queueType` label).
- Summoner history API defaults to all stored history and supports queue filtering by family or explicit queue IDs.
- Ranked analytics compute paths explicitly filter to ranked solo/duo queue data, so non-ranked ingestion does not contaminate tier/winrate/build/matchup analytics.
- Non-ranked backfill now advances with per-summoner ingestion cursors (`SummonerIngestionCursors`) so progress remains monotonic and does not skip older windows during preemption/failures.
- Match records now persist team bans (`MatchBans`) to support champion `banRate` surfaces.

### Timeline-Derived @15 Metrics

- Ranked solo/duo matches are eligible for timeline ingestion.
- Timeline ingestion persists:
  - fetch state (`MatchTimelineFetchStates`)
  - per-participant snapshots at minute mark 15 (`MatchParticipantTimelineSnapshots`)
- Matchup `avgGoldDiffAt15` and `avgXpDiffAt15` are computed from timeline snapshots (not end-of-game proxies).
- Matchup responses also expose timeline quality metadata:
  - `timelineCoverageRatio`
  - `timelineSampleSize`
  - `timelineDataFreshnessUtc`

### Pro Roster and Pro Builds

- Tracked pro/high-ELO roster entries are stored in `TrackedProSummoners` with optional pro/team metadata.
- Admin API (`/api/admin/pro-summoners`) allows manual curation and updates.
- Champion pro-build analytics joins tracked roster participants against ranked solo/duo match data for:
  - recent pro matches
  - top players
  - common builds

### Rune Hierarchy Pipeline

- Match ingestion stores rune selections with explicit hierarchy metadata per participant:
  - `SelectionTree`: primary, secondary, stat shards
  - `SelectionIndex`: slot order inside each tree
  - `StyleId`: rune path for primary/secondary trees
- Static rune data ingestion now maps each rune to canonical path/slot metadata using CommunityDragon `perkstyles` + `perks`.
- Analytics build computation and summoner match summaries use explicit selection hierarchy first, then fallback to static metadata only for legacy rows.
- API payloads expose:
  - compact rune summary for list views
  - full rune selections for detailed/expanded views

### Match Item Persistence

- Match participant items are persisted with explicit `SlotIndex` (0-6) plus `ItemId`.
- This preserves final inventory order and allows duplicate item IDs in different slots.
- Champion build analytics post-processes persisted items against static item metadata and only counts completed, in-store, build-impact items (filters out components/trinkets/wards/consumables).
- Build endpoint requests do not trigger static-data network refresh; patch item metadata is refreshed by background jobs.
- If metadata coverage is temporarily incomplete for a patch, analytics uses a legacy exclusion fallback to avoid empty build responses.

## Web Auth Boundary (BFF)

The web app never exposes backend tokens to browser JS:

- User tokens are stored as HttpOnly cookies in Next.js domain
- Next route handlers forward requests to backend with:
  - `Authorization: Bearer ...` (UserOnly) when needed
  - `Authorization: Bearer ...` (AdminOnly) for `/admin` flows when needed
  - `X-API-Key` (AppOnly) when needed
- Backend never receives browser cookies (explicitly stripped in proxy)
- Catch-all proxy routes reject invalid path segments (`.`/`..`) to avoid path normalization escapes.
- AppOnly proxy route `/api/trn/app/*` is explicitly allowlisted for approved paths (not a generic arbitrary AppOnly relay).
- Logout flow revokes refresh tokens server-side via `POST /api/auth/logout` before cookie clear.

## Admin Surface and Security

- Admin APIs are protected with JWT + `admin` role (`AdminOnly` policy).
- Admin bootstrap can grant initial admin role from configured email allowlist (`Auth:AdminBootstrap:Emails`).
- Admin mutating operations are rate-limited (`admin-write`) and audited (`AdminAuditEvents`).
- Auth endpoints have dedicated rate limits for login/register/refresh/logout protection.
- Admin UX uses curated `/api/admin/*` endpoints and `/admin/*` pages, including:
  - pipeline overview with worker/server snapshots and effective concurrency
  - database + analysis metrics with per-region ingestion health
  - queue explorer for `enqueued`, `processing`, `scheduled`, and `failed` jobs
  - recurring producer pause/resume controls
  - generic job detail with state history, inferred region, and delete/retry actions
  - bulk backlog delete for `enqueued`/`scheduled`/`failed` states
  - service operational log viewer (`webapi` and `service`) with source-availability metadata and rotated-log scanning
- Admin queue observability is read from Hangfire monitoring APIs, not directly from worker-memory state, so observed backlog and active servers reflect storage-backed Hangfire truth.
- Processing-job delete is exposed as an advanced operator control only; it transitions Hangfire job state to `Deleted`, but already-started side effects may still complete before the worker observes cancellation.

## Analytics Region UX

- Public analytics now treat region as explicit UI state instead of an implicit backend assumption.
- `GET /api/analytics/regions` exposes the enabled ingestion regions plus `ALL`/global for the web app.
- Tier list, builds, matchup, and winrate queries accept the same platform-region tokens and use `PlatformRegion` filtering so region-scoped pages match the ingestion model.
- The web app persists the last selected analytics region in client storage/cookie and best-effort syncs it to `UserPreferences.PreferredRegion`.

### Operational Logging

- `Transcendence.WebAPI` and `Transcendence.Service` emit structured operational log entries to file via a shared logger provider.
- In compose deployments, both hosts mount a shared `operational_logs` volume so `/api/admin/logs/services` can surface both streams from a single admin API.
- Outside shared-volume deployments, the Web API can resolve per-source reader paths via `AdminLogs:Sources:webapi:DirectoryPath` and `AdminLogs:Sources:service:DirectoryPath`.
- The file logger creates the target log file eagerly and emits a one-time stderr warning when the process cannot write the configured path, so container log streams expose permission/path failures even if the operational file itself is absent.

## Caching

Backend uses a layered approach (see source and README):

- HybridCache (L1 in-memory + L2 Redis) for derived stats/analytics
- Persistent storage (PostgreSQL) for canonical match/summoner data
- Summoner stats cache entries are tagged per summoner (`summoner-stats:{summonerId}`) so refresh jobs can invalidate all related stats keys in one operation

## Frontend Overhaul Follow-Ups

Backend work needed to fully unlock new frontend pages is tracked here:

- `docs/BACKEND_TASKS_FRONTEND_OVERHAUL.md`
