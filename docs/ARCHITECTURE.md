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

### `Transcendence.WebAdminPortal`
- Private break-glass Hangfire dashboard host (not intended for public exposure)

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
- Generated OpenAPI TypeScript client artifacts
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

### Continuous Analytics Ingestion

- Champion analytics ingestion now runs continuously in low-priority mode to keep growing current-patch data.
- Summoner maintenance runs continuously in low-priority mode to refresh stale summoners when no high-priority API refresh demand is active.
- Ingestion scales queued refresh count based on:
  - current patch coverage vs target
  - staleness of recent successful fetches
- Even when patch data is healthy, ingestion can queue a small minimum number of low-priority refreshes per run.
- Early patch mode remains ranked solo/duo-first until coverage targets are satisfied; once healthy, low-priority refresh can widen to all supported history queues.
- Low-priority refresh windows stop early whenever active high-priority API refresh demand is detected.

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

## Admin Surface and Security

- Admin APIs are protected with JWT + `admin` role (`AdminOnly` policy).
- Admin bootstrap can grant initial admin role from configured email allowlist (`Auth:AdminBootstrap:Emails`).
- Admin mutating operations are rate-limited (`admin-write`) and audited (`AdminAuditEvents`).
- Raw Hangfire dashboard remains a private break-glass surface; public/admin UX should use the curated `/api/admin/*` endpoints and `/admin/*` UI.

## Caching

Backend uses a layered approach (see source and README):

- HybridCache (L1 in-memory + L2 Redis) for derived stats/analytics
- Persistent storage (PostgreSQL) for canonical match/summoner data
- Summoner stats cache entries are tagged per summoner (`summoner-stats:{summonerId}`) so refresh jobs can invalidate all related stats keys in one operation

## Frontend Overhaul Follow-Ups

Backend work needed to fully unlock new frontend pages is tracked here:

- `docs/BACKEND_TASKS_FRONTEND_OVERHAUL.md`
