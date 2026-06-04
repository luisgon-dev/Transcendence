# Architecture

Transcendence is a backend + web monorepo:

- A .NET Web API that serves reads and queues refresh jobs
- A .NET background worker that executes Hangfire jobs (refresh, ingestion, analytics, etc.)
- A Next.js web frontend that renders pages (SSR) and proxies to the backend via route handlers (BFF)
- Game surfaces are modularized by route namespace: `/lol/*` + `/api/lol/*` and `/tft/*` + `/api/tft/*`

## Components

### `Transcendence.WebAPI`
- Public/authenticated REST API
- Enqueues background work (Hangfire) for expensive refresh operations
- Exposes OpenAPI/Swagger (spec is exported and committed under `openapi/`)
- Does not hold Riot API keys and does not call Camille directly

### `Transcendence.Service`
- Background host that runs Hangfire server and recurring jobs
- Executes ingestion/refresh/analytics workflows
- Owns Riot/Camille integration for both LoL and TFT
- Recurring jobs are registered through a shared `WorkerRecurringJobPolicy`; the active set is driven by the `Jobs:Schedule` `Enable*` flags and the resolved scheduling profile (`Jobs:Schedule:Profile`, default `stable`). `DevelopmentWorker` and `ProductionWorker` schedule the same job set and differ only in startup behavior, not in which recurring jobs run.
- The default `stable` scheduling profile is coverage-first for LoL analytics:
  - adaptive analytics refresh
  - new-patch ramp refresh
  - champion analytics ingestion
  - summoner maintenance
  - high-elo roster refresh
  - low-frequency timeline backfill
- On startup patch rollover, the worker refreshes static data synchronously but no longer performs a blanket Hangfire backlog purge. Current-patch catch-up work is preserved across restarts.

### `Transcendence.Service.Core`
- Domain/application services (analysis, analytics compute, auth, live game, Riot API integration, jobs)
- Called from WebAPI controllers and the worker host
- LoL and TFT live in separate bounded contexts with separate entities, repositories, services, caches, and jobs

### `Transcendence.Data`
- EF Core DbContext + entities + repositories
- PostgreSQL is the intended runtime database

### `apps/web` (Next.js)
- App Router pages + route handlers used as a BFF:
  - `/api/session/*` for browser auth/session interactions
  - `/api/trn/*` as proxy endpoints to backend (adds auth headers server-side)
  - `/api/static/*` (champions, items, runes, spells) serve cached Data Dragon / CommunityDragon static maps to the browser (`public, s-maxage=86400, stale-while-revalidate=86400`)
  - `/api/diagnostics/backend` is a server-side backend connectivity probe (GETs the analytics tier-list endpoint) that always returns HTTP `200` with `{ ok, backend, requestId, durationMs }`
- Tailwind styling, SSR-first pages where possible
- Admin dashboard routes under `/admin/*` for ops controls/reports (JWT `admin` role required)
- Frontend analysis routes:
  - `/lol/tierlist`
  - `/lol/champions/*` is the unified champion surface: win rates, builds (with the full rune tree), inline matchups summary, a sortable "All Matchups" table (`?sort=winRate|games`, anchored at `#matchups`), and a quick link to pro builds. The standalone `/lol/matchups` surface was removed; `/lol/matchups` and `/lol/matchups/:championId` now 308-redirect into `/lol/champions/*` (preserving query state) via `next.config.mjs` `redirects()`.
  - `/lol/pro-builds/*` — the index hero is a pro/high-elo champion playrate ranking with a `scope` segmented control (Pro / High-Elo / All) plus a public "Tracked Pros" roster panel, retaining champion search and the recent-pro-matches feed.
  - `/lol/summoners/[region]/[riotId]` is the unified LoL profile + match history surface
    - Legacy `/lol/summoners/[region]/[riotId]/matches*` routes redirect into this unified view using query state (`page`, `queue`, `expandMatchId`)
  - `/tft`
  - `/tft/comps/*`
  - `/tft/champions/*`
  - `/tft/items/*`
  - `/tft/traits/*`
  - `/tft/augments/*`
  - `/tft/summoners/[region]/[riotId]`
- Public LoL patch badges now read backend analytics patch status instead of raw Data Dragon latest so web patch labels match the active analytics dataset
- LoL analytics pages (tier list, champion, pro-builds) carry a historical patch selector (`AnalyticsPatchFilter`, backed by `lib/lolPatchFilters.ts` + `lib/lolAnalyticsPatches.ts`, surfaced via `FilterBar`) that reads `GET /api/lol/analytics/patches` and drives the `patch` query parameter

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

## Riot Identifier Policy

- `Puuid` is the canonical durable Riot identifier in storage for both LoL and TFT.
- User-facing lookup uses normalized Riot ID fields: `gameName` + `tagLine` + `platformRegion`.
- Internal joins and downstream reads pivot from Riot ID to local summoner GUIDs or directly to `Puuid`.
- `RiotSummonerId` and `AccountId` are non-canonical refresh artifacts. They must not be required for search, profile lookup, favorites, live-game resolution, or analytics joins.

Current app usage follows that policy:
- LoL search/autosuggest uses stored normalized Riot ID fields plus match participation presence.
- LoL profile GET resolves by Riot ID first, then uses the local summoner GUID for stats and matches.
- TFT search/profile resolves by Riot ID first, then uses the local summoner GUID for match history.
- Favorites, pro-roster rows, and live-game snapshots key on `Puuid` or local IDs rather than encrypted Riot identifiers.

Operational implication:
- Riot API key swaps should not invalidate stored gameplay data as long as `Puuid` and Riot ID fields remain intact.
- Encrypted identifiers may drift after a key change and should be treated as rehydratable, not as durable foreign keys.

### Summoner Read Failure Semantics

- Summoner profile and stats reads fail closed on backend errors.
- `GET /api/lol/summoners/{region}/{name}/{tag}` and `GET /api/lol/summoners/{summonerId}/stats*` do not return synthetic empty success payloads when compute fails.
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
  - `tft-refresh-high`
  - `tft-default`
  - `tft-refresh-low`
- API refresh jobs run on `refresh-high`; ingestion-driven refresh jobs run on `refresh-low`.
- TFT refresh jobs use their own lock and queue namespace (`tft:summoner-refresh:*`, `tft:refresh-priority:api:*`) so TFT demand does not block LoL refresh throughput.
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
- The production ingestion strategy is region-aware and high-elo-first:
  - enabled regions fan out independently
  - per-region coverage targets scale with configured region weights
  - tracked high-value roster candidates are considered before the generic stale summoner pool
  - ranked Emerald+ candidates are preferred ahead of lower-value fallback rows
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

- Analytics APIs default to the active patch and can query stored historical patches with a `patch` query parameter.
- Analytics APIs intentionally do not fall back to a different patch payload when the selected patch has no data.
- Responses include sample metadata (`sampleStatus`, `sampleSize`, `minimumRecommendedSampleSize`, `patchAgeHours`, `isEarlyPatchWindow`) so web surfaces can show selected-patch low-sample/no-data states explicitly.
- `GET /api/lol/analytics/status` is the lightweight source of truth for the active LoL analytics patch used by public web chrome and landing surfaces.
- `GET /api/lol/analytics/patches` lists active and historical LoL patches available to public analytics filters.

### Match Queue Scope and History

- Match rows now persist queue metadata (`queueId`, `queueFamily`, `queueType` label).
- Summoner history API defaults to all stored history and supports queue filtering by family or explicit queue IDs.
- Ranked analytics compute paths explicitly filter to ranked solo/duo queue data, so non-ranked ingestion does not contaminate tier/winrate/build/matchup analytics.
- Non-ranked backfill now advances with per-summoner ingestion cursors (`SummonerIngestionCursors`) so progress remains monotonic and does not skip older windows during preemption/failures.
- Match records now persist team bans (`MatchBans`) to support champion `banRate` surfaces.

### Timeline-Derived @15 Metrics

- Ranked solo/duo matches are eligible for timeline ingestion.
- Timeline backfill is an enrichment path, not the main coverage path for tier lists or champion win-rate/build data.
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
- The same roster table is also used as a high-value analytics seed source. Automated high-elo refresh writes active roster rows with `IsPro=false`; pro-build analytics explicitly filter to `IsPro=true`.
- Admin API (`/api/admin/pro-summoners`) allows manual curation and updates.
- Champion pro-build analytics joins tracked roster participants against ranked solo/duo match data for:
  - recent pro matches
  - top players
  - common builds
- Cross-champion pro analytics live on a dedicated `ProAnalyticsController` (`/api/lol/analytics/pro/*`, separate from the `{championId}` champion routes to avoid route ambiguity):
  - `GET /pro/champions` ranks champions by tracked-player pick frequency. A `scope` parameter selects the roster predicate — `pro` (`IsPro`), `highelo` (`IsHighEloOtp`), or `all` (`IsPro || IsHighEloOtp`, the default) — so the surface stays populated from the continuously-ingested high-elo roster even when the manually-curated `IsPro` set is sparse. Aggregation mirrors the pro-build compute: materialize `{ ChampionId, Win, Puuid }` scalar rows (ranked solo/duo, patch, region→platform), then group in memory (games / wins / win rate / distinct players). Cached 24h (`proplayrate` tag).
  - `GET /pro/players` exposes a public, slimmed projection of the `IsActive && IsPro` roster (no internal identifiers) for the Tracked Pros panel. Cached 24h (`proroster` tag).
  - Both tags compose under the shared `analytics` cache tag, so the existing analytics cache invalidation clears them.

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
  - API key management page (`/admin/api-keys`) for listing, rotating, and revoking AppOnly API keys
  - pro/tracked-roster curation page (`/admin/pro-summoners`)
- Admin queue observability is read from Hangfire monitoring APIs, not directly from worker-memory state, so observed backlog and active servers reflect storage-backed Hangfire truth.
- Processing-job delete is exposed as an advanced operator control only; it transitions Hangfire job state to `Deleted`, but already-started side effects may still complete before the worker observes cancellation.

## Analytics Region UX

- Public analytics now treat region as explicit UI state instead of an implicit backend assumption.
- `GET /api/lol/analytics/regions` exposes the enabled ingestion regions plus `ALL`/global for the web app.
- Tier list, builds, matchup, and winrate queries accept the same platform-region tokens and use `PlatformRegion` filtering so region-scoped pages match the ingestion model.
- The web app persists the last selected analytics region in client storage/cookie and best-effort syncs it to `UserPreferences.PreferredRegion`.

## TFT Architecture

- TFT persistence is parallel to LoL, not shared with it:
  - `TftSummoners`, `TftRanks`, `TftHistoricalRanks`
  - `TftMatches` plus participant/unit/trait/augment child rows
  - TFT static-data tables for sets, patches, units, items, augments, and traits
- TFT summoner refresh flow mirrors LoL semantics but remains isolated:
  - WebAPI checks the store and returns `200` or `202`
  - refresh requests enqueue `ITftSummonerRefreshJob`
  - worker resolves Riot account + TFT summoner + TFT ranks + TFT matches, then persists them
- TFT static data is refreshed independently from LoL patch/static-data flows through `UpdateTftStaticDataJob`.
- TFT static-data rows remain set-versioned in storage, but read endpoints project only the active set so set transitions do not surface duplicate units/items/traits/augments.
- TFT analytics cache and comp aggregation are isolated behind `ITftAnalyticsService` and `/api/tft/analytics/*`.

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

## Change Tracking

Future implementation work should live in GitHub issues or pull requests rather than long-lived planning docs committed under `docs/`.
