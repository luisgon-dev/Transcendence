# Architecture

Transcendence is a backend + web monorepo:

- A .NET Web API that serves reads and queues refresh jobs
- A .NET background worker that executes Hangfire jobs (refresh, ingestion, analytics, etc.)
- A Next.js web frontend that renders pages (SSR) and proxies to the backend via route handlers (BFF)
- Frontend and API routes live under the `/lol/*` + `/api/lol/*` namespace

## Components

### `Transcendence.WebAPI`
- Public/authenticated REST API
- Enqueues background work (Hangfire) for expensive refresh operations
- Exposes OpenAPI/Swagger (spec is exported and committed under `openapi/`)
- Does not hold Riot API keys and does not call Camille directly
- Has no project reference to `Transcendence.Data`. Controllers own HTTP concerns only (authorization,
  status/header mapping, route-link construction, and audit actor extraction) and delegate persistence,
  projection, validation, and refresh-lock orchestration to `Transcendence.Service.Core` services.

### `Transcendence.Service`
- Background host that runs Hangfire server and recurring jobs
- Executes ingestion/refresh/analytics workflows
- Owns Riot/Camille integration for LoL
- Recurring jobs are registered through a shared `WorkerRecurringJobPolicy`; the active set is driven by the `Jobs:Schedule` `Enable*` flags and the resolved scheduling profile (`Jobs:Schedule:Profile`, default `stable`). `DevelopmentWorker` and `ProductionWorker` schedule the same job set and differ only in startup behavior, not in which recurring jobs run.
- The default `stable` scheduling profile is coverage-first for LoL analytics:
  - adaptive analytics refresh (self-paced; tightens during the new-patch ramp window)
  - champion analytics ingestion
  - summoner maintenance
  - high-elo roster refresh
  - low-frequency timeline backfill
- On startup patch rollover, the worker refreshes static data synchronously but no longer performs a blanket Hangfire backlog purge. Current-patch catch-up work is preserved across restarts.

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
  - `/api/static/*` (champions, items, runes, spells) serve cached Data Dragon / CommunityDragon static maps to the browser (`public, s-maxage=86400, stale-while-revalidate=86400`)
  - `/api/diagnostics/backend` is a server-side backend connectivity probe (GETs the analytics tier-list endpoint) that always returns HTTP `200` with `{ ok, backend, requestId, durationMs }`
- Tailwind styling, SSR-first pages where possible
- **Auth token refresh runs in `proxy.ts` (Next 16 middleware), not during render.** Auth uses a short-lived access token + a single-use, rotated refresh token (the backend revokes the presented refresh token on every `/api/auth/refresh`, no grace window). Refreshing inside a Server Component render can't persist the rotated cookie (cookie writes are illegal during render), which would burn the refresh token and silently log users out. So `proxy.ts` refreshes a stale access token on page navigations and writes the rotated cookies to **both** the forwarded request (so the SSR render reads the fresh token and doesn't refresh again) and the response (so the browser persists them). It is fail-safe: it only acts when a refresh token exists and the access token is stale, skips prefetch requests (so a speculative prefetch can't burn the single-use token), and on any failure (401/5xx/network/timeout/malformed) passes through **without** clearing cookies. The matcher excludes `/api/*` (route handlers refresh + clear in their own writable context via `getAccessTokenOrRefresh`/`getSessionMe`), `_next`, and static files. Pure helpers live in `lib/proxyAuth.ts`; cookie names + the staleness check in `lib/authCookieShared.ts`.
- Riot Sign On uses two same-origin route handlers: `/api/session/riot/start` creates a 256-bit
  correlation state in an HttpOnly, SameSite=Lax, ten-minute cookie; `/api/session/riot/callback`
  validates it with a constant-time comparison before sending the one-time code to the backend.
  The backend performs Client Secret Basic token exchange and Account-V1 identity verification,
  then discards Riot tokens. Durable state is limited to the one-to-one verified PUUID/Riot ID link.
  RSO is optional and fails closed while its approved production-client credentials are absent.
- `getSessionMe` is React-`cache()` wrapped so server consumers share one `/api/auth/me` lookup per render. The header renders `AccountNav` behind a fixed-size `<Suspense>` fallback, keeping session resolution off the document-shell critical path.
- Admin dashboard routes under `/admin/*` for ops controls/reports (JWT `admin` role required)
- Frontend analysis routes:
  - `/lol/tierlist`
  - `/lol/champions/*` is the unified champion surface: win rates, builds (with the full rune tree), inline matchups summary, a sortable "All Matchups" table (`?sort=winRate|games`, anchored at `#matchups`), and a quick link to pro builds. The page reads `/api/lol/analytics/champions/{championId}/profile` so the role selection, cached build aggregate, and cached matchup aggregate arrive through one backend request instead of a client-side analytics waterfall. The standalone `/lol/matchups` surface was removed; `/lol/matchups` and `/lol/matchups/:championId` now 308-redirect into `/lol/champions/*` (preserving query state) via `next.config.mjs` `redirects()`.
  - `/lol/pro-builds/*` — the index hero is a pro/high-elo champion playrate ranking with a `scope` segmented control (Pro / High-Elo / All) plus a public "Tracked Pros" roster panel. The toolbar, playrate table, roster, and champion search render from the first fetch batch; the fan-out recent-match feed and its item map stream independently behind `<Suspense>` with a stable row skeleton.
  - `/lol/items/*` and `/lol/runes/*` are the Build Atlas: searchable index and detail pages for
    resource pick rate, win rate, sample size, and champion-role fit. The indexes, detail pages,
    header, command palette, landing discovery, and sitemap all expose the new surfaces.
  - `/lol/live` is the first-class live-game scout. It accepts a Riot ID, renders both teams with
    rank/form/streak/champion-pool and loadout context, and re-checks active games every minute.
    Profile sidebars reuse a compact version of the same card.
  - `/lol/summoners/[region]/[riotId]` is the unified LoL profile + match history surface. Its RSC resolves the profile first, then fetches the requested match-history page and rank history through server-to-server calls while the route skeleton streams behind `<Suspense>`. React-cached champion, item, spell, and rune maps are passed into the client island in the same render; browser fetches are reserved for pagination, refresh completion, and best-effort recovery when a server dependency failed.
    - Legacy `/lol/summoners/[region]/[riotId]/matches*` routes redirect into this unified view using query state (`page`, `queue`, `expandMatchId`)
- Public LoL patch badges now read backend analytics patch status instead of raw Data Dragon latest so web patch labels match the active analytics dataset
- LoL analytics pages (tier list, champion, pro-builds) carry a historical patch selector (`AnalyticsPatchFilter`, backed by `lib/lolPatchFilters.ts` + `lib/lolAnalyticsPatches.ts`, surfaced via `FilterBar`) that reads `GET /api/lol/analytics/patches` and drives the `patch` query parameter
- Analytics filter defaults and feedback:
  - The champion detail page (`/lol/champions/[championId]`) defaults to **Emerald+** when the `rankTier` param is absent (`resolveDefaultedRankTier` in `lib/ranks.ts`). An explicit `?rankTier=all` means all-ranks; to keep that selectable, the page's `FilterBar` runs in `explicitAllRank` mode so the rank dropdown/lane tabs emit and preserve a literal `rankTier=all`. The tier list keeps its own `DEFAULT_TIERLIST_RANK_TIER` handling.
  - Lane selectors site-wide use `LANE_ROLES` (`lib/roles.ts`) — the five lanes, no "All" tab. Pages that aggregate across lanes (tier list, pro-builds) do so via an absent `role` param (no lane highlighted), not a selectable tab.
  - Navigation/filter pending feedback uses Next 16 primitives: segment-level `loading.tsx` skeletons (champion, tier list, pro-builds) for instant route fallback + partial prefetch, `useLinkStatus` spinners on lane/role `<Link>` tabs (`components/ui/LinkPendingDot.tsx`), and `useTransition` `isPending` spinners on the rank/region/patch controls.
  - The champion detail page (`/lol/champions/[championId]`) streams: the champion identity shell (icon/name/title, from cached Data Dragon static data) renders immediately, while the profile-dependent regions (tier badge + stats + filters, and the win-rate/builds/matchups sections) stream behind `<Suspense>` so a cold backend profile (3–6s) no longer blocks first paint. A single `cache()`-wrapped loader is shared by the two streamed regions so the profile is fetched once.
  - Its splash backdrop uses responsive `next/image` optimization at quality 55 instead of a full-resolution CSS background. It loads eagerly because browser verification identifies that visual as the page's LCP; the small identity icon remains prioritized as well.
  - Data Dragon static fetchers (`lib/staticData.ts` — champion/item/rune maps, version lookup — and `lib/lolAnalyticsPatches.ts`) are wrapped in React `cache()` so the version lookup + heavy JSON transform run once per render and dedupe across the page and sibling components (they previously re-ran per fetcher and per page).
  - The unified LoL profile hero backdrop uses the most-played champion's splash; a stable per-summoner skin is only used once its splash art loads (some skin `num`s lack art and would blank the backdrop), otherwise the base `_0` splash.

### `packages/api-client`
- Generated OpenAPI TypeScript client artifacts built from the committed spec
- Schema generation uses `openapi-typescript` + `openapi-fetch`

## Data Flow: Summoner Refresh

1. Client requests a summoner by Riot ID:
   - If data exists in DB: return immediately
   - If missing: return `202 Accepted` indicating refresh is needed
2. Client triggers refresh:
   - The refresh endpoint is signed-in only (`UserOnly`); the web app reaches it through `/api/trn/user/*`
   - The shared `ISummonerRefreshCoordinator` acquires the refresh locks (preventing concurrent refreshes)
   - The coordinator enqueues the Hangfire job and releases owned locks if enqueueing fails
3. Worker performs refresh:
   - Calls Riot APIs
   - Upserts summoner/rank/match records
   - High-priority refresh sequence:
     - ranked solo/duo head sync first
     - all-mode head sync second
     - non-ranked backfill pagination (bounded by safety caps)
   - Signed-in manual refreshes then enqueue `FullHistoryBackfillJob` on the reserved `history-backfill` queue
4. Client polls GET endpoint until data is ready (200 OK)

### Full-History Profile Backfill

- A manual signed-in profile refresh is the opt-in trigger for exhaustive profile history. Routine analytics ingestion stays current-patch/ranked-head focused and does not scan an arbitrary player's ancient history.
- `FullHistoryBackfillJob` pages Match-V5 by PUUID with `count=100`, a time cursor (`endTime`), and no queue filter, so it discovers every queue that Riot's account match history can search for that player. The lower bound is configurable as `Jobs:FullHistoryBackfill:MinimumMatchStartEpochSeconds` (default June 16, 2021, the Match-V5 historical floor the app targets).
- The job stores compact `SummonerMatchFacts` for the selected summoner only: match id, queue, patch, season key, participant stats, surrender/remake signals, and versioned ranked-total classification. These facts intentionally have no FK to `Matches`, so they persist when the archival job prunes raw match detail.
- Active-season profile overview/champion stats are materialized into `SummonerSeasonOverviewStats` and `SummonerSeasonChampionStats` for ranked solo/duo. The profile read uses these aggregates when present and falls back to retained raw `MatchParticipants` for players that have not completed a backfill.
- `SummonerSeasonCoverages` compares the stored completed ranked solo/duo fact count to the current Riot ranked total (`Rank.Wins + Rank.Losses`) and records the delta/status. The first classifier version counts queue 420, `GameComplete`, and excludes early-ended matches using Riot's early-surrender signal; the classifier version is persisted so future interpretation changes are auditable.
- Riot's public league API exposes current league entries, not a historical per-player season-rank ledger. Existing `HistoricalRank` data is therefore app-observed snapshots only; season labels like "Season 2025 Diamond IV" can only be produced from snapshots the app captured.
- The profile LP progression preserves that provenance: the web normalizes observed tier/division/LP snapshots onto a monotonic ladder-points scale, reports the observation window and net LP-equivalent change, and explicitly avoids presenting it as Riot per-game history.
- Profile season windows are resolved from `RankedSeasons` when configured, with a calendar-year fallback. This keeps the active-season profile surface independent from the analytics patch selector.

## Riot Identifier Policy

- `Puuid` is the canonical durable Riot identifier in storage.
- User-facing lookup uses normalized Riot ID fields: `gameName` + `tagLine` + `platformRegion`.
- Internal joins and downstream reads pivot from Riot ID to local summoner GUIDs or directly to `Puuid`.
- `RiotSummonerId` and `AccountId` are non-canonical refresh artifacts. They must not be required for search, profile lookup, favorites, live-game resolution, or analytics joins.

Current app usage follows that policy:
- LoL search/autosuggest uses stored normalized Riot ID fields plus match participation presence.
- LoL profile GET resolves by Riot ID first, then uses the local summoner GUID for stats and matches.
- Favorites, pro-roster rows, and live-game snapshots key on `Puuid` or local IDs rather than encrypted Riot identifiers.
- `UserRiotAccount` makes a verified PUUID unique across site accounts. Linked identity personalizes
  the landing-page main-profile shortcut and account dashboard; it does not alter public analytics.

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
- API refresh jobs run on `refresh-high`; ingestion-driven refresh jobs run on `refresh-low`.
- Refresh locks use DB-backed lease semantics (atomic acquire/renew + explicit lease expiry on release) so concurrent lock races do not require lock-row deletion.

### Live-Game Snapshot Boundary

- Only the worker host owns Riot credentials and calls Spectator-V5. The keyless Web API serves the
  latest `LiveGameSnapshot`; browser requests never bypass the ingestion priority policy.
- Each snapshot stores its complete response as PostgreSQL `jsonb` alongside indexed
  state/game/timing columns. This preserves participant spells/runes and computed scouting analysis
  across the worker-to-Web-API boundary. Snapshots written before the payload column remain readable
  through the legacy state/game fallback.
- Participant form is bounded to the latest 20 stored matches. Champion pool entries are the top
  three picks within that window, and streaks are signed (wins positive, losses negative). Response
  age is recomputed when read so clients can label stale observations honestly.

### Refresh Lock Lifecycle Telemetry and Retention

- Lock lifecycle telemetry is emitted as best-effort/non-blocking instrumentation from the shared API refresh coordinator, repository lock operations, and the lifecycle cleanup job.
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
- **Self-pacing (no separate ramp jobs).** New-patch ramp behavior is folded into the base jobs instead of duplicate recurring registrations. Each producer (`champion-analytics-ingestion`, `summoner-maintenance`) and the adaptive `refresh-champion-analytics` runs on one fast "heartbeat" cron and decides internally whether a tick does real work:
  - The producers acquire a self-pacing slot (`producer:pacing:*` lock) whose TTL is the ramp interval within `Jobs:*:NewPatchRampHours` of the active patch release, else the steady interval (`SelfPace{Ramp,Steady}IntervalMinutes`). While the slot is held the dispatcher skips its fan-out, so the effective cadence tightens on a fresh patch and relaxes after — driven by patch age, not a second cron. Ramp-vs-steady *budget* params (`Ramp*` option values) are still auto-selected from patch age inside the per-region run.
  - `RefreshChampionAnalyticsJob.ExecuteAdaptiveAsync` self-paces via its own refresh cooldown (`Ramp/MinimumRefreshIntervalMinutes`), so polling it at the heartbeat cadence honors the shorter ramp cooldown without a separate ramp job.
- **Discovery reserved lane.** The per-region producers and the analytics summoner-refresh consumers they enqueue run on a dedicated `HangfireQueues.Discovery` (`"discovery"`) queue with its own `BackgroundJobServer` worker pool (`Transcendence.Service/Program.cs`), so this heaviest pipeline is never buried behind the broad `refresh-low` maintenance backlog (the failure that previously left a new patch starved of match discovery). Because `RefreshForAnalytics` is enqueued via `ISummonerRefreshJob`, its `[Queue("discovery")]` lives on the **interface** method (Hangfire resolves the queue from the enqueued interface, not the implementation).
- **Queue-depth backpressure.** Before fanning out refresh consumers, each producer reads the discovery queue depth (`IQueueDepthProbe`) and scales its per-run queue target down between `DiscoveryQueueBackpressureSoftCap`/`HardCap` (default 5000/10000), to zero at/above the hard cap. This is a final ceiling that overrides forced catch-up and cold-start: when the discovery workers are the bottleneck, adding more is waste and risks the unbounded regrowth that caused the original ~100k clog. The probe fails open (no backpressure) so a monitoring hiccup never stalls ingestion.

#### Patch detection: data-driven promotion (not Data Dragon timing)

- Riot's **match `gameVersion`** (the deployed game build, the value `NormalizePatch` derives the patch from) **lags the Data Dragon patch label** and rolls out **region-by-region over ~a day**. Promoting the active analytics patch the instant Data Dragon updates makes ingestion target a patch whose games don't exist yet — so it discards every real match (the big regions can be hours/days behind, e.g. NA still producing `16.11` match data while Data Dragon already reports `16.12`).
- `StaticDataService.DetectAndRefreshAsync` therefore **records the new patch + fetches its static assets immediately but gates promotion**: a new patch becomes active only once its `gameVersion` has actually rolled out — `Jobs:PatchPromotion:MinRegionsRolledOut` regions (default 5) each with ≥ `MinMatchesPerRegionToCount` (default 200) Success matches — with a `MaxWaitHoursBeforeForcePromote` (default 72) stuck-guard. Bootstrap (no active patch yet) promotes immediately.
- To break the chicken-and-egg (you must collect the new patch to measure its rollout) **and** keep ingestion productive during the rollover, the analytics consumer (`SummonerRefreshJob.RefreshForAnalytics`) persists matches on the **{active patch} ∪ {most-recently-detected patch}** set (`GetIngestiblePatchesAsync`), not the single active patch, and clamps the match-id fetch window to `Jobs:MatchIngestion:AnalyticsRecentWindowDays` (default 4) so it only scans recent games. The new patch accumulates in the background and StaticDataService promotes it data-driven; analytics **read** queries still scope to the single active patch.
- **Early-stop on not-yet-rolled-out regions.** Riot match ids are newest-first, so the newest ranked match reveals a region's current game build. When a patch filter is in effect and that newest match is **not** on an acceptable patch, the region hasn't rolled out — `SyncMatchWindowAsync` skips the rest of that summoner after a single fetch instead of pulling ~100 old-patch games it would only discard. On a personal-tier Riot key (low rate budget) this is essential: without it, refreshing the laggard regions burns the scarce budget on matches that are thrown away, starving the productive regions and saturating Camille's rate limiter (which manifests as stalled consumers).

#### Riot request-rate gate (`IRiotRateGate`)

- Riot enforces its app rate limit **per routing region** (subdomain): `americas`/`europe`/`asia`/`sea` for match-v5, `na1`/`euw1`/… for platform APIs, each ~20 req/s + ~100 req/2min on a personal/dev-tier key. The system can easily generate match-fetch requests faster than that, and Camille's own limiter responds by **queuing them unboundedly** — which fills the Hangfire worker slots with parked jobs and stalls ingestion (the observed outage).
- `RiotRateGate` is a singleton, dependency-free **per-region token bucket** (`SemaphoreSlim` of tokens refilled by a timer, one bucket per routing value, `Jobs:RiotRateGate`) that paces outbound calls to stay **under** each region's budget, so Camille never has to queue. The high-volume match calls go through it — `MatchService.GetMatch*` (detail), `RiotMatchIdsClient` (ids), `MatchTimelineIngestionJob` (timeline) — each gated by its `RegionalRoute`. Because the buckets are independent, the regions' budgets are used **in parallel** (the aggregate ceiling across regional routes is several × a single region). If a region's budget stays exhausted past `MaxWaitSeconds` the gate **rejects** rather than parking the worker — the caller skips that call and it's retried on a later refresh. This bounds every Riot await, so a saturated region can never deadlock the lane again.
- **Gate rejection is a deferral, never a fetch failure.** Every match-fetch path treats a rejected gate acquisition as a no-op skip (retried on a later sweep) and does **not** increment the match's `RetryCount`. This includes `MatchService.FetchMatchWithRetryAsync`, which previously *threw* on rejection and let its generic `catch` flip merely-throttled matches to the terminal `PermanentlyUnfetchable` status after five throttle-skips — silent, permanent loss of fetchable matches during exactly the new-patch surges when the budget is tightest (the global query filter hides `PermanentlyUnfetchable` rows). Only a genuine Riot 404/410 (Camille returns `null`) is classified `PermanentlyUnfetchable`; all other errors stay `TemporaryFailure` with backoff. `RetryFailedMatchesJob` also drains a bounded batch of rows mislabeled by the old bug (identified by their rate-gate error signature) back to `TemporaryFailure` each run (`Jobs:RetryFailedMatches:RevivePermanentlyUnfetchablePerRun`, default 25; genuine 404s are never touched). It is the **sole** retry driver for `TemporaryFailure` matches — the former inline `BackgroundJob.Schedule` self-schedule that raced the sweep (double-fetching, double-spending budget) was removed.
- **Refresh-lock release is fenced by owner.** `TryAcquireOwnedAsync`/`ReleaseOwnedAsync` stamp a per-acquisition token (`RefreshLocks.OwnerToken`) and release only when it still matches, so a stale holder whose lease already expired (and was re-acquired by someone else) can't free the new owner's lock. On the acquire→enqueue→job-release handoff the token travels inside the lock-key string as an *owned handle* (mirroring the analytics execution-context suffix), so no Hangfire job signature changed and legacy in-flight jobs fall back to release-by-key.
- **Per-summoner backfill is serialized and transactional.** `FullHistoryBackfillJob` takes a per-summoner lease for the whole run (overlapping runs skip instead of double-fetching and colliding on the `SummonerMatchFacts` unique index), isolates per-match failures (a transient Riot error is recorded and skipped rather than escaping and stranding the backfill in `Running` with no resumer), and wraps each season's delete-then-reinsert recompute in a transaction (uncached reads never see empty season stats mid-recompute).
- **Timeline writes are serialized per match.** `MatchTimelineIngestionJob` guards the snapshot/build-path delete-then-insert with a per-match Postgres advisory lock (`pg_advisory_xact_lock`) inside a transaction, so two workers ingesting the same match from different sources can't interleave and collide on the snapshot/purchase/skill keys — while different matches stay fully parallel (the reason this job deliberately avoids a global `[DisableConcurrentExecution]`).

#### Low-priority ingestion is ranked-head-only; defer-age guardrail retired

- **Ranked-head-only.** Low-priority analytics ingestion (both producers, `discovery` lane) fetches only the current-patch **ranked head** with the not-rolled-out early-stop — it does **not** widen into all-modes-head/non-ranked backfill. That widening (`SummonerMaintenanceJob`'s `EnableAllModesWidening`, default **off**) is the "budget bomb": for an uncovered summoner it pulls the player's entire ancient match history through the rate gate (~20+ min, old-patch yield) and saturates the lane. Non-ranked **profile** backfill is handled on demand by the high-priority `RefreshByRiotId` path instead, where a user actually opened the profile.
- **Defer-age starvation guardrail disabled** (`Jobs:StarvationGuardrail:Enabled=false`). Its signal (oldest eligible summoner's `UpdatedAt` age ≥ 6h) is structurally unsatisfiable at this scale on the yield-limited personal key — of ~4.1M summoners only a few thousand are <6h fresh, so the breach predicate was **permanently** true and forced catch-up fired perpetually (bypassing the API-priority pause and, with widening, driving the ancient-history grind). The adaptive throughput budget's coverage/velocity-driven `CatchUp` mode + the per-region cold-start override remain the legitimate bursting mechanisms. Re-enable only with a delta/growth-based defer signal, not absolute age.
- **`Summoner.LastActiveAtUtc`** — the most recent game-creation time across a summoner's ingested matches (maintained in `MatchService` via the EF-tracked match graph, monotonic; null until first seen in an ingested match). This is the durable *activity* signal (distinct from `UpdatedAt`, which is fetch/coverage recency) intended to drive future activity-aware candidate selection — preferring summoners who recently *played* current-patch ranked over merely-stale ones. It populates going forward as matches ingest.

#### Ingestion observability runbook (discovery-lane health)

The discovery lane stalls when all workers park on a few long, non-stoppable jobs (the failure above). Signature + checks (prod is `root@192.168.0.221`, container `transcendence-postgres`, db `transcendence`):

- **Workers stuck:** `SELECT count(*) FROM hangfire.jobqueue WHERE queue='discovery' AND fetchedat IS NOT NULL` equals the lane WorkerCount, with identical `now()-fetchedat` age for 20+ min (none cycling) and `transcendence-service` CPU near-idle (parked, not computing). Healthy = `oldest_run` age stays small and cycles.
- **Budget-bomb / perpetual-catch-up regression:** non-zero counts of `jobqueue` discovery jobs whose `hangfire.job.invocationdata` contains `forced-catch-up` (guardrail mis-fire) or `includeAllModes` true (widening) — both should be **0** in steady state.
- **Yield:** `docker logs transcendence-service --since 10m | grep 'AnalyticsRefresh] Completed' | grep -oE 'rankedHead=[0-9]+' | sort | uniq -c` — the `rankedHead>0` share is the productive-yield headline.
- **Active-patch growth:** `SELECT count(*) FROM "Matches" WHERE "Status"=1 AND "Patch"=(SELECT "Version" FROM "Patches" WHERE "IsActive")` sampled over a window.
- **Recovery:** purge the enqueued backlog (`WITH del AS (DELETE FROM hangfire.jobqueue WHERE queue='discovery' AND fetchedat IS NULL RETURNING jobid::bigint AS id) DELETE FROM hangfire.job j USING del WHERE j.id=del.id;`); producers refill with fresh current-window jobs within minutes.

#### Analytics cache warm-coverage + invalidation

- Champion analytics use HybridCache (L1 in-memory 1h / L2 Redis 24h via `AnalyticsCacheOptions`). Cache keys vary by champion × role × rankTier × region × patch, so the pre-warm set must match what the frontend actually requests or real reads cold-compute (3–6s) on every miss.
- `RefreshChampionAnalyticsJob` pre-warms win-rate/build/matchup (and, bounded, pro-build) aggregates for the top champions per role **at the page-default tier** — `Jobs:RefreshChampionAnalytics:PreWarmRankTier` (default `EMERALD_PLUS`), the same tier the champion page reads — instead of the all-ranks key the page never requests by default. Win rates are warmed with no role filter (the profile endpoint reads the full by-role table to resolve the most-played lane).
- Pro-builds pre-warming is gated by `PreWarmProBuilds` and bounded separately by `ProBuildChampionsPerRoleToPreWarm` (default 8, lower than the standard `ChampionsPerRoleToPreWarm`) because the pro-builds compute is heavier; it warms the role-scoped (most-played-lane) default that the pro-builds page lands on.
- Routine refreshes invalidate **only the current patch's** entries (`InvalidateAnalyticsCacheForPatchAsync`, the `patch:{version}` tag) so re-ingesting current-patch matches does not cold-start every cached entry (other patches, pro roster, pro playrate) at once. The admin `POST /cache/invalidate` still clears the whole `analytics` tag.
- **Champion tier grade (single source of truth).** Tiers are scored by one shared pure scorer (`ChampionTierScorer`) that every path calls — the raw live compute, the precomputed-stats read, and the hourly refresher — so a champion's grade is identical on the tier list, the champions grid, and the champion detail hero, and the raw-vs-stats equivalence holds by construction. Methodology: **per-role-first** (champions compared only to same-role peers; the unified view shows each champion at its primary role), **strength = win-rate delta vs the role baseline** with empirical-Bayes shrinkage (Beta prior fit per role via method-of-moments; tuned by `Analytics:Tiering:*`), **role-volume-adaptive sample gates** (live-calibrated bounds keep broad scopes conservative while thin scopes can resolve real signal), **absolute cutoffs** on the delta (so `S` can be empty on a balanced patch), and pick/ban kept on a separate `contestedScore` popularity axis. The computed grade is **persisted** as `ChampionScopeGradeStat` by `PrecomputedAnalyticsRefresher.RefreshTabularCoreAsync` in the same delete+insert transaction as the raw atoms — for region=ALL and rank scopes `{ALL, EMERALD_PLUS}` only (the scopes every web surface defaults to), per lane role plus a primary-role overview row, with patch-over-patch `movement` resolved against the previous patch (by `Patches.ReleaseDate`). The tier-list read serves this persisted grade directly for those default scopes; a specific region or exact tier recomputes live via the same scorer (no movement), mirroring `ChampionMatchupStat`'s all-region-persisted / region-filtered-live pattern. `TierListResponse.confidence` distinguishes a resolved spread from a flat or insufficient scope so the UI does not present uniform B as confident balance. The champion profile endpoint projects this champion's tier-list entry into a `grade` field so the detail hero renders the list grade (no frontend re-derivation). The tier-list cache key is `analytics:tierlist:v4:` (bumped from `v3` for the calibrated grades and confidence contract).
- **Champion patch trends.** The profile endpoint also projects up to 12 historical `ChampionScopeGradeStat` rows joined to `Patches.ReleaseDate` for the same champion/queue/role/global rank scope. This reuses the already-durable grade history instead of introducing a duplicate time-series table; the web renders the chart only with at least two real patch points.
- **Dedicated hourly default-profile warm** (`WarmDefaultChampionProfilesJob`, cron `Jobs:Schedule:WarmDefaultChampionProfilesCron` = `0 * * * *`, low-priority `refresh-low` queue): keeps **every** champion's default profile page warm and fresh, not just the top-N the adaptive refresh covers. For each champion with ≥ `Jobs:WarmDefaultChampionProfiles:MinimumGamesToWarm` games on the active patch it calls `IChampionAnalyticsService.RefreshDefaultProfileCacheAsync`, which **recomputes** win rates (Emerald+, region=ALL, no role) → resolves the most-played lane exactly as the profile endpoint does → recomputes builds + matchups (and, when `IncludeProBuilds`, the lane-scoped pro-builds) for that lane, then **overwrites** the exact cache keys via `HybridCache.SetAsync`. SetAsync is gap-free refresh-ahead — the old value keeps serving until the fresh one lands, so there is no invalidate-then-cold window. With L2 (Redis) TTL 24h and a 1h refresh, every default profile stays a permanent Redis hit (warm read ≈ tens of ms vs 3–6s cold) with ≤1h-old stats. The job runs on its own DI scope per champion (isolated `DbContext`) with bounded `MaxConcurrency`, so it yields DB to ingestion/API demand. NB: the worker process populates the shared L2 (Redis); the WebAPI process reads its own (cold) L1 then hits that warm Redis entry.
- **Reserved worker pool.** The "keep analytics warm/fresh" jobs (`WarmDefaultChampionProfilesJob` + the adaptive `RefreshChampionAnalyticsJob`) run on a dedicated Hangfire queue, `HangfireQueues.AnalyticsWarm` (`"analytics-warm"`), served by a **second `BackgroundJobServer`** with its own small worker pool (`Transcendence.Service/Program.cs`). The main 24-worker server pulls its queues highest-priority-first and does **not** serve `analytics-warm`, so a saturated `refresh-*`/`default` backlog can never starve these jobs of workers — they always run on schedule. (This isolates worker *slots*; both servers still share the Hangfire/Postgres storage.) Other queue names remain inline literals at the host; only this reserved lane is a shared constant because it is referenced from both the server registration and the jobs' `[Queue]` attributes.

### Deployment & rollback

The stack ships continuously: a push to `main` triggers the `Docker Images` workflow, which builds and pushes the changed component images to GHCR tagged both `:main` (the moving tag) and `:sha-<short>` (immutable, one per commit). On the prod host (`root@192.168.0.221`) a **systemd-timer digest poller** (`scripts/ops/poll-deploy.sh`, every ~60s — see `scripts/ops/README.md`, the authoritative deploy runbook) resolves the current `:main` manifest digest from GHCR and, when it changes, runs `docker compose pull` + `up -d --no-deps` for just that app service (postgres/redis untouched). The poller then gates success on the image's container healthcheck (web `/api/health`, WebAPI `/health/ready`, worker heartbeat) for a bounded window; only a healthy replacement emits the success notification. Pull, recreate, missing-healthcheck, and health-timeout failures make the systemd run fail and send Discord failures. Digest-resolution failures persist per-service counters and alert after three consecutive misses instead of silently freezing deploys. **wud is retired for the app containers** (wud 8.2.2 silently failed to resolve the GHCR digest for these packages); it may still linger in the stack but does not deploy `web`/`webapi`/`service`. On startup the **worker host** applies any pending EF migrations (`Database:AutoMigrate`, enabled in `config/backend.shared.json`) — only the worker can, since it is the migrations assembly; the WebAPI relies on it. So a migration-bearing deploy no longer needs a manual `dotnet ef database update` — which makes the CI **hot-table DDL gate** more important, since a migration that compiles but blocks on a hot table would still auto-apply on the worker. Hot-table index migrations remain the exception and must be applied out-of-band before the deploy (see DEVELOPMENT.md); the CI `migration-apply` job additionally applies the full chain to an ephemeral Postgres on every PR.

**Rollback (break-glass).** Because every build also publishes an immutable `:sha-<short>` tag, rolling back is re-pointing the stack at a known-good commit instead of reverting and waiting for a rebuild:

1. Find the good SHA — `git log --oneline` on `main`, the `:sha-` tags on the GHCR package, or the previously-running image via `docker inspect <container> --format '{{.Image}}'`.
2. In the Portainer stack (project `transcendence`, compose at `/var/lib/docker/volumes/portainer_data/_data/compose/2/docker-compose.yml`) change the affected service's image from `:main` to `:sha-<good>` and redeploy the stack. The poll-deploy digest compare only fires for the `:main` tag, so a pinned `:sha-` tag holds (the poller leaves non-`:main` services alone).
3. Once the fix is back on `main`, flip the image to `:main` again to resume auto-tracking.

A schema-affecting deploy cannot be fully undone by an image rollback alone — an out-of-band migration (see DEVELOPMENT.md) must be reversed deliberately.

### Metrics-based alerting

The WebAPI (`/metrics`, ASP.NET Core exporter) and worker (`:9464`, standalone Prometheus `HttpListener`) are scraped by Prometheus (`config/monitoring/prometheus.yml`), which Grafana (`grafana/grafana:13.1.0`) reads. Prometheus + Grafana are their own **single-source-of-truth stack** at `config/monitoring/` (`compose.yml` + a prod overlay `compose.prod.yml`), deployed identically local and prod (prod at `/root/transcendence-monitoring/`, synced from the repo — it is *not* part of the app's Portainer stack or the `poll-deploy` pipeline; see `config/monitoring/README.md`). Grafana is file-provisioned (`config/monitoring/grafana/provisioning`). Beyond the worker's own in-app **ingestion** health alerter (`IngestionHealthAlertJob` → `WebhookAlertNotifier`, posts to `Alerts:Webhook:Url`), Grafana owns **infrastructure** alerting so that an external process — not the dying worker — pages:

- **Provisioned rules** (`config/monitoring/grafana/provisioning/alerting/rules.yml`, folder `Transcendence`, evaluated every 1m): `WebAPI down` and `Worker down` (`up == 0`, `for 2m`, `noData → Alerting` so a vanished scrape target still pages), `API 5xx error ratio high` (5xx : total request ratio > 5%, `for 5m`), `API p95 latency high` (p95 `http_server_request_duration_seconds` > 1.5s, `for 10m`), and `Host disk space low` (fullest real filesystem < 15% free, `for 15m` — pages *before* a full disk crash-loops Postgres, as it did 2026-07-19). Each is a two-stage query — an *instant* PromQL query (refId A) feeding a Math condition (refId B) — routed via per-rule `notification_settings` so the default notification policy is left untouched.
- **Scrape targets** (`config/monitoring/prometheus.yml`): `transcendence-webapi:8080` + `transcendence-service:9464` (app `/metrics`) and `transcendence-node-exporter:9100` — a `node-exporter` sidecar in the monitoring stack that mounts the host/CT root read-only (`--path.rootfs=/host`) to expose `node_filesystem_*` for the disk alert.
- **Contact point** (`config/monitoring/grafana/provisioning/alerting/contactpoints.yml`): a Grafana-native `discord` receiver whose URL is interpolated from the `DISCORD_ALERT_WEBHOOK_URL` env var on the grafana container, so no secret is committed. Grafana 13 refuses to *start* on an empty contact-point URL, so unset falls back to a no-op placeholder URL (Grafana boots; alerts don't deliver anywhere real). Set it in prod (reuse the same webhook as the worker's `Alerts:Webhook:Url`).
- **Postgres/Redis have no Prometheus target of their own** (only webapi + worker are scraped), so their loss is covered *indirectly*: the worker crash-loops without the DB (→ `Worker down`) and DB/cache faults surface as API 5xx (→ `API 5xx error ratio high`). Add `postgres_exporter`/`redis_exporter` if dedicated DB/Redis-down alerts are ever needed.

This complements the deploy pipeline: poll-deploy catches startup/readiness failures before reporting success, while Grafana catches later regressions and a service that fails after the bounded deploy window.

### Match Detail Retention and Archival

The binding constraint on tier-list sample size is storage, not the Riot API budget, so old-patch match
detail is archived off-box and pruned to keep the database from growing unbounded while ingestion scales up.

- **Policy:** keep full match detail for the newest `KEEP_PATCHES` (default 3 = active + last 2); every older
  patch is archived to the NAS, then pruned from Postgres. Cached analytics aggregates for old patches are
  unaffected (they are recomputed/served from their own stores, not the raw match tables).
- Full-history profile facts (`SummonerMatchFacts` and the season aggregate/coverage tables) are outside the
  `Matches` cascade and are not archived by this patch-retention job. They are the durable store for selected
  players' deeper profile insights.
- **Archival job** (`scripts/ops/archive-old-patches.sh`, weekly cron on the Docker host): for each eligible
  patch it streams `Matches` + all cascade children (`MatchParticipants`, `MatchParticipantItems`,
  `MatchParticipantRunes`, `MatchBans`, `MatchParticipantTimelineSnapshots`,
  `MatchTimelineFetchStates`) out via Postgres `COPY` → `gzip` → `ssh` to the NAS, verifies each archive
  (gzip integrity + exact row count) and only then prunes (one cascading `DELETE` on `Matches`, batched).
  Restore is `zcat <Table>.csv.gz | psql -c "COPY \"<Table>\" FROM STDIN WITH (FORMAT csv, HEADER true)"`
  (parents before children).
- **Consistency under the live writer:** the ingestion worker keeps inserting old-patch matches (lapsed
  players returning, retried failed matches), so each patch's match-ID set is first frozen into a work table
  (T0 snapshot); count/export/verify/delete are all bounded to that frozen set. Rows inserted after T0 are
  excluded (no verify race, no data loss) and archived on the next run (residuals land in a non-clobbering
  `NAS/<patch>/residual-<epoch>/` once a patch is already `_DONE`).
- **Plan/throughput:** export uses an adaptive query plan (large slices sequential-scan; small slices force
  index nested-loops) because the prod DB is HDD-backed; deletes free pages for reuse inside Postgres
  (`VACUUM FULL` is intentionally avoided — it locks the table — so the file does not shrink but the DB stops
  growing as new ingestion reuses the freed pages). A one-time `scripts/ops/archive-remaining-bulk.sh` does the
  same with a single frozen set across all old patches for an initial backlog sweep.
- With retention bounding growth, ingestion is scaled up: more enabled regions and a higher per-patch
  `TargetSuccessfulMatchesForCurrentPatch` (steady-state detail ≈ `KEEP_PATCHES × target` matches).

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
- Public tier-list and champion analytics reads accept a queue family (`solo`, `flex`, `aram`, `arena`). Patch availability, cache keys, samples, bans, grades, and tier-list atoms are isolated by queue so a non-Solo request cannot reuse Solo/Duo data.
- Item/rune analytics aggregate current or selected-patch Ranked Solo/Duo participants into one row
  per participant/resource before grouping, so duplicate item slots cannot inflate pick rate. The
  service joins static patch metadata softly, excludes non-build-impact inventory and stat shards,
  and caches region/patch index and detail payloads in HybridCache for 24h (1h local).

### Match Queue Scope and History

- Match rows now persist queue metadata (`queueId`, `queueFamily`, `queueType` label).
- Summoner history API defaults to all stored history and supports queue filtering by family or explicit queue IDs.
- Analytics compute paths explicitly filter to the requested queue family. The precomputed tabular grain includes `QueueFamily` on role/tier, match-count, ban, and grade atoms; existing rows migrate as `RANKED_SOLO_DUO`.
- Solo/Duo and Flex preserve lane roles. ARAM and Arena collapse to the synthetic role `ALL` and use their own tier baselines/sample populations. Flex rank scoping reads Flex rank; the casual queues use Solo/Duo rank as a skill segment. Lane-pair matchups are intentionally unavailable for roleless queues, while queue-specific builds fall back to the live compute outside the durable Solo/Duo build snapshots.
- Non-ranked backfill now advances with per-summoner ingestion cursors (`SummonerIngestionCursors`) so progress remains monotonic and does not skip older windows during preemption/failures.
- Match records now persist team bans (`MatchBans`) to support champion `banRate` surfaces.

### Timeline-Derived @15 Metrics

- Ranked solo/duo matches are eligible for timeline ingestion.
- Timeline backfill is an enrichment path, not the main coverage path for tier lists or champion win-rate/build data.
- Timeline ingestion persists:
  - fetch state (`MatchTimelineFetchStates`), versioned by `SchemaVersion` (`MatchTimelineIngestionJob.CurrentTimelineSchemaVersion`) so already-`Success` matches are re-ingested **once** when the job begins deriving new per-match data; `MatchTimelineBackfillJob` re-enqueues stale-schema matches.
  - per-participant snapshots at minute mark 15 (`MatchParticipantTimelineSnapshots`)
  - per-participant **ordered, build-relevant item purchases** (`MatchParticipantItemPurchases`) parsed from `ITEM_PURCHASED`/`ITEM_UNDO`/`ITEM_SOLD`/`ITEM_DESTROYED` events — net of undo/sell/destroy, categorized `Starter`/`Boots`/`Legendary` via `ItemVersion` metadata, with components/consumables/trinkets dropped (~5–8 rows per participant).
  - per-participant **skill order** (`MatchParticipantSkillOrders`) parsed from `SKILL_LEVEL_UP` events — the full leveling sequence, opening first-three, and basic-ability max priority (ability evolutions excluded).
- Event parsing is a pure, unit-tested function (`TimelineBuildParser`); completed-item classification is shared with the analytics compute layer through `BuildItemClassifier` so ingestion and aggregation use one definition.
- Champion **builds** fold these ordered purchases + skill orders + the summoner spells already stored on `MatchParticipant` into a sectioned, timing-aware path (top spell pairs, dominant skill order, top starter sets, top boots, the per-position dominant core path with average completion minute, and 4th/5th/6th situational options). The builds/pro-builds `HybridCache` key prefixes were versioned to `v2` so stale pre-overhaul payloads are not served.
- Matchup `avgGoldDiffAt15` and `avgXpDiffAt15` are computed from timeline snapshots (not end-of-game proxies).
- Matchup responses also expose timeline quality metadata:
  - `timelineCoverageRatio`
  - `timelineSampleSize`
  - `timelineDataFreshnessUtc`

### Pro Roster and Pro Builds

- Tracked pro/high-ELO roster entries are stored in `TrackedProSummoners` with optional pro/team metadata.
- The same roster table is also used as a high-value analytics seed source. Automated high-elo refresh writes active roster rows with `IsPro=false`; pro-build analytics can select `pro`, `highelo`, or `all` roster scope.
- Admin API (`/api/admin/pro-summoners`) allows manual curation and updates.
- Champion pro-build analytics joins tracked roster participants against ranked solo/duo match data using the selected roster scope for:
  - recent pro matches
  - top players
  - common builds
- When `role` is omitted the endpoint resolves the champion's most-played lane from the cached win-rate aggregate (mirrors the `/profile` endpoint) and computes lane-scoped pro builds, so the default `/lol/pro-builds/{championId}` landing view is a single lane rather than the heavy cross-role aggregate. The cross-role path (no resolvable lane) bounds its participant scan to the most-recent `Analytics:Compute:ProBuildMaxParticipantRows` rows (default 1500): without a `TeamPosition` filter, `role=ALL` + `scope=all` + `region=ALL` over a multi-thousand-PUUID roster otherwise materializes the heavy item/rune projection unbounded and command-timeouts.
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
- Rune match facts retain `RuneId` + `PatchVersion` as indexed scalar columns but deliberately do not foreign-key to `RuneVersions`. Readers soft-join metadata so an incomplete static-data sync or later static-data reseed cannot reject or delete immutable match history.
- Analytics build computation and summoner match summaries use explicit selection hierarchy first, then fallback to static metadata only for legacy rows.
- API payloads expose:
  - compact rune summary for list views
  - full rune selections for detailed/expanded views

### Match Item Persistence

- Match participant items are persisted with explicit `SlotIndex` (0-6) plus `ItemId`.
- This preserves final inventory order and allows duplicate item IDs in different slots.
- Item match facts retain `ItemId` + `PatchVersion` as indexed scalar columns but deliberately do not foreign-key to `ItemVersions`; static metadata is enrichment, not an ingestion prerequisite or cascade owner for historical facts.
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
- Anonymous public proxy route `/api/trn/public/*` is explicitly allowlisted (`lib/publicProxyAllowlist.ts`): only LoL summoner reads (`lol/summoners/**` GET) are relayed; anything else (admin, user, auth, analytics writes, refresh POSTs, arbitrary paths, `PUT`/`DELETE`) is rejected `404`. It forwards no credentials, so it is a narrow read surface, not a generic anonymous relay. Signed-in profile refreshes go through `/api/trn/user/*`, where the BFF attaches the user's JWT.
- Logout flow revokes refresh tokens server-side via `POST /api/auth/logout` before cookie clear.
- Password recovery stores only a SHA-256 hash of each random one-time token in `UserPasswordResetTokens`; SMTP delivers the raw token in a short-lived `/account/reset-password` link. Issuing a newer token invalidates older links, and completing a reset atomically updates the PBKDF2 password hash, clears account lockout, consumes outstanding reset tokens, and revokes all refresh sessions. Initiation responses do not reveal whether an email is registered.
- **Client-IP forwarding for rate limiting.** The BFF resolves the real client IP (`lib/clientIp.ts`: `cf-connecting-ip` → `x-real-ip` → the rightmost, edge-appended `X-Forwarded-For` entry) and forwards it so the backend's per-IP limiters partition on the true client. The `/api/trn/*` proxy **strips** any client-supplied `X-Forwarded-For` and re-sets that single trusted value, so a forged chain can't spoof the per-IP read caps or masquerade as internal. The auth routes (`login`/`register`/`refresh`) reach the backend via the generated client rather than the proxy, so they forward the same header explicitly — without it, every auth request collapsed onto the BFF's own address (one global window = a trivial site-wide login/refresh DoS and defeated per-attacker brute-force isolation).

## Admin Surface and Security

- Admin APIs are protected with JWT + `admin` role (`AdminOnly` policy).
- Admin bootstrap can grant initial admin role from configured email allowlist (`Auth:AdminBootstrap:Emails`).
- Admin mutating operations are rate-limited (`admin-write`) and audited (`AdminAuditEvents`).
- Auth endpoints have dedicated rate limits for login/register/refresh/logout protection.
- The admin BFF same-origin (CSRF) check compares the request `Origin` against a server-configured canonical origin (`TRN_PUBLIC_ORIGIN`) when set, rather than the client-forwardable `X-Forwarded-Host`. `SameSite=Lax` cookies remain the primary CSRF control; the origin check is defense-in-depth.
- Public read endpoints (`expensive-read`/`search-read`/`multisearch-read`) are rate-limited **per client IP** (not one shared global window), so a single client cannot exhaust everyone's budget. Internal/SSR traffic — the web frontend's server-side fetches, health probes, other backend services — reaches the API directly with a private/loopback source address and is **exempt** (`BuildIpReadPartition`/`IsInternalAddress` in `Program.cs`); it would otherwise all collapse into one partition and starve under a single cap. Browser traffic carries its real public IP via `UseForwardedHeaders` (the same mechanism the auth limiters rely on).
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

## Data Access

The codebase deliberately does **not** route all persistence through a repository layer. EF Core's
`DbContext` is already a unit-of-work + repository, and testability is provided by substituting the
provider (`SqliteCompatibleTranscendenceContext` backs the analytics/service tests against a real
in-memory schema, not repository mocks). The policy is therefore:

1. **Direct EF Core by default.** Services and jobs query `TranscendenceContext` directly. Don't wrap
   reads in a repository for its own sake — a generic `GetById`/`Add` CRUD wrapper adds indirection
   without value.
2. **Reusable read predicates are composable query-objects**, not copy-paste. A `Where(...)` clause used
   in more than one place becomes an `IQueryable<T>` extension under `Transcendence.Service.Core/Queries`
   — e.g. `MatchParticipantQueries`: `participants.InAnalyticsQueue(queueFamily).OnPatch(patch).FromSuccessfulMatches()`.
   Each is a pure `Where` that EF folds into one SQL `WHERE`, so they commute and compose. (This replaced
   the ranked-solo-queue predicate that had been duplicated 20+ times across the analytics/stats services.)
   A genuinely single-use predicate stays inline — query-objects are for *reuse*, not premature abstraction.
3. **Repositories wrap write operations that carry real logic** — e.g. `SummonerRepository`'s raw-SQL
   upsert with unique-violation recovery, `UserAccountRepository`'s refresh-token rotation + family
   revocation, `MatchRepository`. A repository must earn its place with behaviour beyond CRUD.
4. **Testability comes from the substitutable `DbContext`**, so reads do not need a repository seam to be
   testable; the write repositories are tested by mocking their interface where the surrounding logic
   matters (e.g. `UserAuthServiceTests`).

Rule of thumb: *query-object for a reused read predicate · repository for a write with logic · direct
`DbContext` for everything else.*

## Change Tracking

Future implementation work should live in GitHub issues or pull requests rather than long-lived planning docs committed under `docs/`.
