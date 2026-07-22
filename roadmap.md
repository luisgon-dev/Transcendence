# Transcendence — Remediation Roadmap

> Generated from the adversarial codebase audit of **2026-07-11** (`Transcendence-Audit-2026-07-11.pdf`).
> This roadmap places **all 149 verified findings** into four sequenced phases. Every item carries its severity, a fix, the "why", and the `file:line` it lives at.
>
> **Verdict:** A high-craft, trustworthy Ranked Solo/Duo analytics engine held back by a small cluster of silent-failure correctness/security/deploy risks, tests that never touch the reality they ship on, accessibility regressions on flagship surfaces, and a product surface still too narrow to be a general-purpose daily driver.

## How to use this document

- Phases are **ordered by urgency**, not size. Do **P0** first (it stops real harm), then **P1** (removes false confidence & restores accessibility), then **P2** (product breadth), then **P3** (debt & polish).
- Within each phase, work is grouped by area. Each area opens with the auditor's one-line assessment for context.
- Tags: severity `HIGH`/`MED`/`LOW`/`INFO`, effort `trivial`/`small`/`medium`/`large`, and `✅ verified` where a finding was adversarially re-checked against the source.
- The **★ Priority sequence** block at the top of each phase is the audit's recommended order for the highest-leverage items in that phase.

## Phase overview

| Phase | Focus | Items | High | Med | Low/Info |
|---|---|---|---|---|---|
| **P0** | Stop the Bleeding — Correctness, Security & Deploy Safety | 11 | 3 | 8 | 0 |
| **P1** | Trust & Hardening — Test Reality, Fix Protocols, Restore Accessibility | 55 | 7 | 48 | 0 |
| **P2** | Product to Daily-Driver — Discoverability, Multi-Mode & the Champ-Select Hook | 12 | 4 | 5 | 3 |
| **P3** | Polish, Refactors & Cleanup | 71 | 0 | 12 | 59 |
| | **Total** | **149** | | | |

---

## P0 — Stop the Bleeding — Correctness, Security & Deploy Safety

Findings that cause real, silent harm **today** — permanent data loss, a trivial global auth DoS, an indefinite prod crash-loop reported as success, and reliability corruption. Each is small-to-medium effort relative to its blast radius. **Ship this phase before anything else.**

**11 items** · 3 high · 8 medium · 0 low/info

### ★ Priority sequence (audit recommendation)

_Stop silent data loss, close the DoS/deploy hazards, and de-risk migrations_

1. Treat rate-gate exhaustion as a no-op skip, not a fetch failure: return a distinct outcome without incrementing RetryCount, and only advance PermanentlyUnfetchable on genuine Riot 404/410; add a revival sweep for rows classified during rate pressure (MatchService.cs:398-399,546-569).
1. Forward the real client IP on the auth path (route session login/register/refresh through the XFF-copying proxy, or inject a sanitized client-IP header) and add a per-account attempt counter independent of IP; strip inbound X-Forwarded-For at the BFF and set it from the true peer (trnClient.ts, trnProxy.ts, WebAPI Program.cs:73-102).
1. Add a CI job that spins up ephemeral Postgres and runs the full migration chain (dotnet ef database update) from empty, ideally with representative seed rows, so data-dependent migration failures are caught pre-merge (ci-web-backend.yml).
1. Make poll-deploy verify /health/ready for a bounded window before reporting success (else notify failure / roll back), and alert on N consecutive digest-resolution failures (poll-deploy.sh:82-101).
1. Distinguish gate-exhaustion from end-of-history in match-id listing so a backfill reschedules instead of completing prematurely (RiotMatchIdsClient.cs, FullHistoryBackfillJob.cs:100-118).
1. Serialize FullHistoryBackfillJob per summoner (refresh-lock or dedupe enqueue) and wrap RecomputeSeasonAggregatesAsync delete+insert in a transaction (FullHistoryBackfillJob.cs:33,145-191,502-600).

### Ingestion & Background Jobs

> The ingestion layer is unusually mature for a personal project: a per-region token-bucket rate gate, dedicated Hangfire queue lanes with bounded worker pools, adaptive throughput + starvation-guardrail + discovery-backpressure controls, cursor-based backfills, and idempotent match/timeline persistence with per-row duplicate fallbacks. Cancellation is propagated correctly in most jobs and the rate-gate refill is race-safe. The material risks are concentrated in the failure/retry semantics: transient rate-gate backpressure is conflated with genuine fetch failure (permanently discarding matches), refresh locks are released by key rather than by owner (defeating dedup under queue backlog), and several paths have overlapping enqueue sources with no cross-source concurrency guard. None are guaranteed prod outages, but a few are silent-data-loss / wasted-budget hazards that surface exactly during a new-patch ingestion surge when the rate budget is tightest.

- [x] **Rate-gate backpressure is counted as a fetch failure and can permanently discard a valid match** `HIGH` · `small` · ✅ verified
  - **Fix:** Treat gate exhaustion as a no-op skip, not a failure: return false (or a distinct outcome) without incrementing RetryCount, mirroring GetMatchDetailsAsync. Only advance RetryCount/PermanentlyUnfetchable on genuine Riot 404/410/permanent errors. Consider a periodic revival sweep for PermanentlyUnfetchable rows that were classified during rate pressure.
  - **Why:** During a new-patch ingestion surge (exactly when the token bucket is chronically exhausted), a TemporaryFailure match that is merely being throttled accumulates RetryCount across retry runs and, after 5 throttle skips, is flipped to PermanentlyUnfetchable — a terminal status that is globally filtered out and never retried. That is silent, permanent loss of an otherwise-fetchable match. Blast radius is limited to matches that reach TemporaryFailure (a comparatively small, retry-path-only population), which is why this is high rather than critical.
  - **Where:** `Transcendence.Service.Core/Services/RiotApi/Implementations/MatchService.cs:398-399, 546-569; RetryFailedMatchesJob.cs:40; TranscendenceContext.cs:129`
- [x] **Refresh locks are released by key, not by owner — a stale holder can release a newer holder's lock** `MED` · `medium`
  - **Fix:** Make release owner-scoped: store a per-acquisition token (GUID) on the row, return it from TryAcquireAsync, and have ReleaseAsync only clear the lease when the token still matches (fencing). This is the standard fix for expiry-based distributed locks.
  - **Why:** If the discovery queue backs up so a consumer runs after its lock's TTL elapsed, a later producer run can re-acquire the same summoner lock and enqueue a second refresh; when the original consumer finally completes it releases the *new* holder's lock, letting a third producer enqueue yet another concurrent RefreshForAnalytics for the same summoner. That defeats the dedup lock, double-spends the scarce personal-tier Riot budget, and races the per-summoner ingestion cursor (SummonerIngestionCursor.Version++) between two DbContexts.
  - **Where:** `Transcendence.Data/Repositories/Implementations/RefreshLockRepository.cs:38-53; ChampionAnalyticsIngestionJob.cs:392-404; SummonerRefreshJob.cs:808-833`
- [x] **Two independent retry mechanisms re-fetch the same failed match, double-spending Riot budget** `MED` · `small`
  - **Fix:** Pick one retry driver. Either drop the inline BackgroundJob.Schedule and let the recurring sweep own retries (with backoff via LastAttemptAt), or drop the sweep for TemporaryFailure and rely solely on the scheduled chain. If both must exist, gate FetchMatchWithRetryAsync on a short per-match in-flight lock.
  - **Why:** The same match can be fetched twice near-simultaneously (the scheduled retry plus the hourly sweep), wasting the constrained rate budget and double-incrementing RetryCount, which accelerates the PermanentlyUnfetchable flip described in the first finding. RetryFailedMatchesJob's [DisableConcurrentExecution] only guards against itself, not against the self-scheduled jobs.
  - **Where:** `Transcendence.Service.Core/Services/RiotApi/Implementations/MatchService.cs:557-569; RetryFailedMatchesJob.cs:40-58`
- [x] **FullHistoryBackfillJob has no per-match exception isolation; one transient Riot error strands the backfill in 'Running' forever** `MED` · `small`
  - **Fix:** Wrap the per-match BuildFactAsync call in try/catch, route transient errors to RecordFetchFailureAsync (already used for null results) instead of letting them escape, and/or add a recurring sweep that re-enqueues backfills stuck in Running past a threshold.
  - **Why:** A single transient Riot 5xx/timeout/deserialization error on any match in the page propagates out of ProcessAsync; after the one Hangfire retry (which re-processes the same poison page and likely fails again) the job goes to Failed and the self-continuation chain dies. The backfill row is left in Running with no recurring resumer, so the player's full history silently never completes.
  - **Where:** `Transcendence.Service.Core/Services/Jobs/FullHistoryBackfillJob.cs:145-178, 264-276; Transcendence.Service/Program.cs:37`
- [x] **Concurrent same-match timeline ingestion races on the snapshot/skill primary keys, burning the retry budget** `MED` · `medium`
  - **Fix:** Add a short per-match lock (or Postgres advisory lock on the match id) around IngestMatchTimelineAsync, or make the snapshot/purchase/skill writes upsert (ON CONFLICT) rather than delete-then-insert, so concurrent runs converge instead of colliding.
  - **Why:** Under active ingestion the same match can be in the timeline queue from two sources and picked up by two of the 8 timeline workers at once, producing spurious PK-collision failures. Repeated collisions consume the MaxRetryAttempts=4 budget and can flip a perfectly fetchable match's timeline to PermanentlyFailed. The stated idempotency guarantee holds only for serial re-ingestion, not the concurrent cross-source case.
  - **Where:** `Transcendence.Service.Core/Services/Jobs/MatchTimelineIngestionJob.cs:16-20, 143-150, 168-189; SummonerRefreshJob.cs:788-806; MatchTimelineBackfillJob.cs:63-84; TranscendenceContext.cs:406,449`

### Security & Authentication

> The auth boundary is, on the whole, carefully built: PBKDF2 password hashing with fixed-time compare and rehash-on-login, refresh-token reuse detection with family revocation, fully-validated JWTs, HttpOnly/SameSite/Secure cookies, a strict public-proxy allowlist with path-traversal rejection, uniform [Authorize(AdminOnly)] + audit logging on admin endpoints, no IDOR on user-scoped resources, and no real secrets committed. The most material problem is the rate-limiting boundary: the backend partitions auth (and read) limits on client IP restored from X-Forwarded-For, but the BFF's auth path (login/register/refresh via the generated client) never forwards the client IP, collapsing all auth traffic into one global partition — a trivial global login/refresh DoS and a defeated per-attacker control. Secondary issues are the BFF forwarding client-controlled X-Forwarded-For verbatim (rate-limit / internal-classification bypass) and a spoofable admin same-origin check. None are data-loss/RCE class; several are availability or defense-in-depth weaknesses.

- [x] **Auth rate limiters collapse to one global partition — client IP is never forwarded on the auth path** `HIGH` · `medium` · ✅ verified
  - **Fix:** Forward the real client IP on the auth path: either route session/login|register|refresh through proxyToBackend (which copies X-Forwarded-For) or have getTrnClient/adminBackend inject a sanitized X-Forwarded-For from the inbound request. Alternatively partition the backend auth limiter on a value the BFF actually forwards (a per-request client-IP header the BFF sets itself), and add a defensive per-account attempt counter independent of IP.
  - **Why:** Every user's login/register/refresh shares one fixed-window bucket keyed on the BFF IP: 8 logins/min, 4 registers/min, 20 refreshes/min for the ENTIRE site. (a) A trivial global DoS — an attacker (or even normal concurrent traffic) exhausts the window and everyone gets 429 on login / 503 on refresh; the 20/min refresh cap is especially fragile since getAccessTokenOrRefresh runs on nearly every authenticated navigation. (b) The documented per-attacker brute-force isolation does not hold — attempts against many accounts all count against the same shared bucket.
  - **Where:** `apps/web/lib/trnClient.ts:8; apps/web/app/api/session/login/route.ts:17-18; Transcendence.WebAPI/Program.cs:73-102,387-391`
- [x] **BFF forwards client-controlled X-Forwarded-For verbatim; backend trusts it and treats private IPs as unlimited** `MED` · `small`
  - **Fix:** At the BFF boundary, strip inbound X-Forwarded-For/X-Forwarded-Proto in copyHeaders and set them explicitly from the true immediate peer, or forward a dedicated trusted client-IP header the backend reads instead of raw XFF. Confirm the nginx layer replaces (not appends) client-supplied XFF and align ForwardLimit with the real hop count.
  - **Why:** Depending on the nginx XFF handling in front of the BFF (append vs replace), a public client can inject X-Forwarded-For: 10.0.0.1 (or rotate forged public IPs) through the public read proxy so the backend either sees an 'internal' source and applies NO rate limit at all, or a spoofed per-IP bucket — defeating the public read throttles (expensive-read 120/min, search 600/min, multisearch 60/min) that protect DB-heavy analytics endpoints. Exploitability is contingent on the nginx layer not sanitizing client XFF (unconfirmed).
  - **Where:** `apps/web/lib/trnProxy.ts:9-20; Transcendence.WebAPI/Program.cs:288-294,398-437`
- [x] **Admin BFF same-origin (CSRF) check is spoofable via X-Forwarded-Host** `MED` · `small`
  - **Fix:** Derive the expected host from trusted server config (an env-configured canonical host / Next's own request URL) rather than client-supplied x-forwarded-host, or drop the bespoke check and standardize on a same-origin/verb policy applied consistently to both the admin and user proxies. Keep SameSite=Lax as the primary control.
  - **Why:** The origin check provides false assurance as a CSRF control — it can be trivially satisfied cross-origin. The real backstop is SameSite=Lax cookies (cross-site POST/PUT/DELETE won't carry the session cookie, so getSessionMe returns unauthenticated), which keeps actual exploitation blocked; this is therefore a defeated defense-in-depth layer plus an inconsistency between the admin and user proxies rather than a live account-takeover path.
  - **Where:** `apps/web/app/api/trn/admin/[...path]/route.ts:13-34; apps/web/app/api/trn/user/[...path]/route.ts:13-42`

### Concurrency & Reliability

> The refresh-lock machinery and background-job concurrency are, on the whole, unusually well-engineered for this class of codebase: the DB lock is a genuinely atomic upsert (no acquire TOCTOU), lock ownership is handed off cleanly from producer to consumer with release-on-failure, all concurrent work isolates its EF DbContext behind per-task DI scopes, there is zero blocking-on-async and no async void, and the singleton state (rate-gate buckets, heartbeat, telemetry) uses correct primitives. The real risk is concentrated in the newest code: FullHistoryBackfillJob (PR #117) has no concurrency guard and no duplicate/transaction handling, so overlapping runs for one summoner crash on unique-index collisions and burn scarce Riot budget, and its non-transactional delete-then-insert recompute can leave season stats missing on a crash. The remaining findings are low-impact races/inconsistencies that largely self-heal.

- [x] **FullHistoryBackfillJob has no concurrency guard: overlapping runs for one summoner crash on unique-index collisions and double-spend scarce Riot budget** `MED` · `medium` · ✅ verified
  - **Fix:** Serialize per summoner: either add a refresh-lock acquire keyed on summonerId around ProcessAsync (release-on-failure like the other jobs), or gate the enqueue so a Queued/Running SummonerFullHistoryBackfill row is not re-enqueued. Additionally, catch DbUpdateException on the duplicate-match path (as SummonerRefreshJob does) so a benign race degrades to a skip instead of a job failure.
  - **Why:** Overlapping backfills for an active summoner double-fetch the same matches, wasting the known-scarce personal-tier Riot rate budget; produce error/retry churn on unhandled duplicate-key exceptions; and roll back the whole page batch (cursor not advanced) on collision. Self-heals on retry (facts already exist) but is pure waste plus alarming error logs.
  - **Where:** `Transcendence.Service.Core/Services/Jobs/FullHistoryBackfillJob.cs:33,145-191; enqueued at SummonerRefreshJob.cs:131 and self-enqueued at FullHistoryBackfillJob.cs:211`
- [x] **RecomputeSeasonAggregatesAsync deletes season aggregates and re-inserts them without a transaction** `MED` · `small`
  - **Fix:** Wrap the delete + Add + SaveChanges for each season in a single BeginTransactionAsync (mirroring PrecomputedAnalyticsRefresher), or switch to an in-place upsert instead of delete-then-insert, so readers and crashes never observe a half-recomputed season.
  - **Why:** Transient empty season stats on the summoner profile during every recompute for uncached reads, and a persistent stats gap for that season/queue if the worker is killed mid-recompute — a real (if self-healing) data-availability regression.
  - **Where:** `Transcendence.Service.Core/Services/Jobs/FullHistoryBackfillJob.cs:502-507 (deletes) vs :600 (insert save)`

### Infrastructure, CI/CD & Observability

> Infra hygiene is genuinely strong for a self-hosted single-host stack: all three images run non-root and multi-stage, GH Actions are SHA-pinned, images are cosign-signed and path-filtered per component, and there is a thoughtful liveness/readiness split plus a thread-pool-proof worker watchdog and OpenTelemetry+Grafana metrics. The material gaps are all in deploy safety and alerting: prod auto-migrates on worker startup yet no CI stage ever executes migrations against a real Postgres (tests are SQLite), so a runtime-failing migration crash-loops the worker while poll-deploy reports the deploy as a success; poll-deploy does no post-deploy health verification and its own pipeline failures are silent; there are no metrics-based alert rules (Prometheus/Grafana) so nothing alerts on API errors, DB/Redis down, or the worker being fully down; and several safety-critical docs (auto-migrate, wud) are stale/contradictory. None are data-loss or security-critical, but the migration/deploy-safety cluster is a real production risk.

- [x] **Prod auto-migrates on worker startup, but no CI stage ever runs migrations against a real Postgres — a runtime-failing migration crash-loops the worker while the deploy reports success** `HIGH` · `medium` · ✅ verified
  - **Fix:** Add a CI job that spins up an ephemeral Postgres (services: postgres) and runs `dotnet ef database update` (or MigrateAsync) end-to-end against it, ideally seeded with representative rows, so data-dependent migration failures are caught before merge. Optionally gate the worker's auto-migrate so a failed migration surfaces a distinct alert rather than a silent crash loop.
  - **Why:** A migration that compiles and passes drift-check but fails at runtime throws on worker startup, the process exits non-zero, restart:unless-stopped restarts it, and it fails again — an indefinite crash loop that halts ALL ingestion and leaves the schema un-advanced. Meanwhile poll-deploy has already recreated the new webapi against the un-migrated schema. The failure is a worker outage plus a stuck deploy that needs manual intervention.
  - **Where:** `config/backend.shared.json:7; Transcendence.Service/Program.cs:216-219; Transcendence.Service.Core/Services/Database/DatabaseMigrator.cs:29-43; .github/workflows/ci-web-backend.yml:26-27,88-97`

---

## P1 — Trust & Hardening — Test Reality, Fix Protocols, Restore Accessibility

Close the confidence gaps that let regressions ship green (CI never touches real Postgres; auth crypto is mocked), restore the accessibility baseline the product claims, fix the lock protocol, add alerting, and tighten the API/caching/data-correctness surfaces. These protect every future change.

**55 items** · 7 high · 48 medium · 0 low/info

### ★ Priority sequence (audit recommendation)

_Make CI test reality, fix the lock protocol, restore accessibility, and add alerting_

1. Add unit tests for all auth crypto (HashPassword→Verify round-trip, wrong/malformed-hash rejection, cost-upgrade path, JwtService.ResolveSigningKey branches, ApiKeyAuthenticationHandler) — pure functions, trivial to cover (high-severity testing gap).
1. Introduce a Testcontainers.PostgreSql integration tier running the analytics equivalence + read-path tests and the authz boundary (WebApplicationFactory hitting public/AppOnly/admin with no/wrong/correct creds) against real Postgres.
1. Make refresh-lock release owner-scoped via a per-acquisition fencing token so a stale holder can't free a newer lock, and consolidate the two retry drivers into one (RefreshLockRepository.cs, RetryFailedMatchesJob.cs).
1. Restore visible focus rings: apply the existing FOCUS_RING token to tier-list sort/rows/spine, champion role/matchup/region filters, pro-builds, and profile expanders; give the command palette dialog/combobox semantics (globals.css:233, TierListTable, Confidence, GlobalCommandPalette).
1. Add Prometheus alert rules + alertmanager (or Grafana provisioned alerting) for up==0 per target, API 5xx ratio/latency, DB/Redis down, and worker-down — routed to the existing webhook so an external process, not the dying worker, pages.
1. Enable Swashbuckle NRT/required support, regenerate the OpenAPI spec + client, and standardize the error model on ProblemDetails (including admin endpoints); add a web-container healthcheck.
1. Immediately invalidate the patch's analytics cache tag after the atom refresh commits (so 'updated N ago' matches served data), and stop negatively caching empty timelines/analytics on the long TTL.

### Backend Architecture & Layering

> The 4-project split (Data → Service.Core → WebAPI/Service) is coherent at the assembly level with no circular project references, and DI lifetimes are handled carefully — singletons that need scoped state correctly use IServiceScopeFactory, and no captive DbContext was found. The Hangfire lane topology and non-pooled-vs-pooled DbContext choices are deliberate and well-documented. The main architectural weaknesses are on the presentation boundary: several WebAPI controllers reach past Service.Core straight into EF/DbContext and repositories (query and write logic, including SaveChanges, live in controllers), refresh-lock orchestration is duplicated verbatim across two controllers, and admin DTO ownership is deliberately mislabeled into a WebAPI namespace while physically living in Service.Core. One god-service (SummonerStatsService, 1642 LOC) survived the P10.1 decomposition.

- [x] **WebAPI controllers bypass Service.Core and access EF/DbContext and repositories directly (query + write logic in the presentation layer)** `HIGH` · `large` · ✅ verified
  - **Fix:** Introduce Service.Core services (e.g. IAnalyticsPatchQueryService, ITrackedProSummonerService) that own these queries/mutations and return DTOs; have controllers depend on those instead of TranscendenceContext. Longer term, drop the WebAPI→Transcendence.Data project reference (keep only repositories/DTOs exposed through Service.Core) so the compiler enforces the boundary.
  - **Why:** Query and mutation logic lives in the HTTP layer with no service seam: it cannot be unit-tested without spinning up controllers, cannot be reused by the worker or other endpoints, and mixes transport concerns (IActionResult, rate-limit attrs) with data access. There is no ITrackedProSummonerService at all — an entire admin write surface (create/update/delete of tracked pros) has its invariants enforced only inside the controller. This is the most consequential layering break for the architecture dimension.
  - **Where:** `Transcendence.WebAPI/Controllers/AnalyticsController.cs:22,59-67; Transcendence.WebAPI/Controllers/ProSummonersController.cs:26,89-126; Transcendence.WebAPI/Controllers/SummonersController.cs:25,102-103; Transcendence.WebAPI/Transcendence.WebAPI.csproj (ProjectReference to Transcendence.Data)`

### Data Model & Migrations

> The EF model is generally well-engineered: composite unique indexes double as UPSERT conflict targets and read lookups, global query filters for unfetchable matches are applied consistently across every match-dependent entity, and the covering-index strategy (bare shape in the model, INCLUDE payload in idempotent CONCURRENTLY raw-SQL migrations) is unusually disciplined and thoroughly documented. CI backs this with both a hot-table DDL lint and a has-pending-model-changes drift guard. The real weaknesses are relational-integrity smells rather than outright bugs: a redundant Match↔Summoner join table that is written on every ingest but never read, hard FKs from immutable match facts to mutable versioned static-data tables, nullable columns carrying UNIQUE indexes that Postgres does not enforce across NULLs, and a test harness (EF InMemory for WebAPI, SQLite-EnsureCreated for Core) that never exercises the real Postgres DDL/migrations and only partially enforces constraints. There are no decimal-precision issues because the model has no decimal columns (rates use double, counts use int).

- [x] **Redundant Match↔Summoner many-to-many join table (`MatchSummoner`) is written on every ingest but never read** `MED` · `medium`
  - **Fix:** Remove the Match.Summoners / Summoner.Matches skip navigations and the MatchService.Add calls, then drop the `MatchSummoner` table (out-of-band per the hot-table DDL playbook). All consumers already read Match↔Summoner through MatchParticipants.
  - **Why:** The Match↔Summoner association is stored twice: once here and once in MatchParticipants (which already has a UNIQUE (MatchId, SummonerId) index). On the hottest ingestion path each match writes ~10 extra rows to a table nothing queries, adding write amplification, storage, and a second cascade path to maintain, for zero read benefit.
  - **Where:** `Transcendence.Data/Models/LoL/Match/Match.cs:34, Transcendence.Data/Models/LoL/Account/Summoner.cs:26; Transcendence.Service.Core/Services/RiotApi/Implementations/MatchService.cs:111,299,461`
- [x] **Immutable match facts carry hard required FKs to mutable, per-patch static-data tables (ItemVersion / RuneVersion)** `MED` · `large`
  - **Fix:** Decouple facts from static versions: store ItemId/RuneId (+PatchVersion) as plain columns without an enforced FK to the version tables (or set OnDelete Restrict/NoAction and resolve version metadata as a soft join at read time). This makes ingestion robust to static-data gaps and prevents accidental cascade loss of match history.
  - **Why:** A match that references an item/rune with no ItemVersion/RuneVersion row for its patch (a newly added or removed game item, or an incomplete static-data sync) fails the entire match insert on the FK constraint — historical fact ingestion is coupled to the completeness of mutable static data. Conversely, deleting/reseeding a versioned static row would cascade-delete historical MatchParticipantItem/Rune rows.
  - **Where:** `Transcendence.Data/TranscendenceContext.cs:329-339 (MatchParticipantRune→RuneVersion), 471-477 (MatchParticipantItem→ItemVersion); Transcendence.Service.Core/Services/RiotApi/Implementations/MatchService.cs:762-786,422`
- [x] **Nullable columns carry UNIQUE indexes that Postgres does not enforce across NULLs (Match.MatchId, Summoner.Puuid)** `MED` · `medium`
  - **Fix:** Make Puuid and MatchId non-nullable (they are de-facto required) so the unique constraint actually enforces identity; if a transient-null window is genuinely needed, keep nullable but add HasFilter("...IS NOT NULL") to make the intent explicit and audit every insert path.
  - **Why:** If any insert path ever leaves Puuid/MatchId null (e.g. a stub created before identity resolution), the DB silently allows duplicate identity-less rows that the unique index is supposed to prevent, and dedupe logic keyed on these columns can diverge. In practice both are set at insert (MatchService assigns MatchId; puuid is resolved before Summoner creation), so current risk is low — but it is unenforced.
  - **Where:** `Transcendence.Data/TranscendenceContext.cs:76-98 (unique MatchId, unique Puuid); Migrations/20250511063835_init.cs:51 (MatchId text nullable:true), :101 (Puuid text nullable:true)`
- [x] **Test harness never exercises the real Postgres DDL/migrations and only partially enforces relational constraints** `MED` · `medium`
  - **Fix:** Add a Postgres-backed integration smoke test (Testcontainers) that runs `Database.Migrate()` and exercises the constraint/index-sensitive flows (upserts, unique violations, cascade deletes); at minimum, migrate the InMemory WebAPI tests to the SQLite context so unique/FK constraints are enforced.
  - **Why:** No automated test applies the actual migration DDL, so migration bugs (bad Down, wrong column type, missing constraint) and schema/migration drift versus the model can pass CI. InMemory-backed controller tests can accept data that violates real unique/FK constraints, giving false confidence for constraint-sensitive paths (e.g. upsert conflict handling).
  - **Where:** `tests/Transcendence.Service.Core.Tests/Support/SqliteCompatibleTranscendenceContext.cs:10-21; tests/Transcendence.WebAPI.Tests/AnalyticsControllerTests.cs:122-125 (and ProSummoners/AdminOperations: UseInMemoryDatabase)`

### Query Efficiency & EF Core Usage

> The analytics read surface is, on the whole, thoughtfully engineered: composable query objects fold predicates into single WHEREs, purpose-built covering indexes (with documented prod EXPLAIN wins) back the dominant scans, the precompute-aggregate layer keeps the default read paths off raw match data, AsNoTracking is applied consistently, and match-detail uses AsSplitQuery to avoid cartesian explosion. The gaps are concentrated in (a) the summoner-profile read path, where the hot endpoint tracks and over-fetches an unbounded unused navigation and several aggregates are computed client-side over a summoner's full participant history instead of in SQL, and (b) a raw analytics fallback that materializes an entire scope's distinct-MatchId set into app memory where its sibling paths keep it as a subquery. None are data-loss or security issues; the highest-impact one runs on every profile page load.

- [x] **Profile read tracks the full summoner graph and over-fetches unbounded, unused HistoricalRanks** `MED` · `small`
  - **Fix:** In the controller read path, drop the HistoricalRanks include and request a no-tracking read (e.g. an AsNoTracking overload or `q => q.AsNoTracking().Include(s => s.Ranks)`). Keep the tracking behavior for the write callers (SummonerRefreshJob) via a separate path or an explicit flag.
  - **Why:** On the hottest, uncached endpoint (the summoner lookup itself is not HybridCache-backed, unlike its stats sub-calls), every page load pays a redundant split query over an ever-growing HistoricalRanks table plus change-tracking snapshots for the whole graph. For long-tracked accounts this is hundreds of unused rows materialized and tracked per request.
  - **Where:** `Transcendence.WebAPI/Controllers/SummonersController.cs:102-103; Transcendence.Data/Repositories/Implementations/SummonerRepository.cs:79-132`
- [x] **Summoner champion-stats and role-breakdown aggregate client-side over the full participant history** `MED` · `medium`
  - **Fix:** Push the grouping into SQL (GroupBy(ChampionId)/GroupBy(TeamPosition) with Count/Sum/Average server-side), returning only the grouped rows; do the TeamPosition normalization/merge on the small result set. This mirrors what ComputeOverviewAsync and the season-aggregate path already do.
  - **Why:** For a heavy account (thousands of ranked games) each call materializes thousands of rows across the wire to emit ~5-15 aggregate rows, seeking-then-heap-fetching via the non-covering IX on SummonerId. MultiSearch calls both methods per summoner for up to 5 summoners, so a cold champ-select lookup fans out to ~10 full-history scans (sequential, since the DbContext is shared).
  - **Where:** `Transcendence.Service.Core/Services/Analysis/Implementations/SummonerStatsService.cs:164-211 (champions), :575-602 (roles); amplified by MultiSearchService.cs:86-87`
- [x] **Raw tier-list fallback materializes the whole scope's distinct MatchId set into app memory** `MED` · `small`
  - **Fix:** Keep the scope match-id set as an IQueryable subquery (as BuildScopedMatchIdQuery already does) and derive totalMatchesInScope via a CountAsync on that subquery, so the ban aggregation stays entirely in SQL.
  - **Why:** On a freshly-promoted popular patch (before its first hourly precompute refresh, when HasStatsAsync is false), the first uncached tier-list request round-trips tens of thousands of GUIDs through the app instead of scoring inside Postgres. Bounded (cached 24h after first hit, and only on un-refreshed patches) but an avoidable memory/latency spike relative to the equivalent paths.
  - **Where:** `Transcendence.Service.Core/Services/Analytics/Implementations/ChampionWinRateComputeService.cs:284-300`

### Analytics Correctness & Precompute

> The precompute layer is unusually well-built for correctness: a single pure `ChampionTierScorer` is shared by the raw compute, the stats read, and the refresher, and a comprehensive equivalence/fixture test suite (win-rates, unified + role-filtered tier lists, matchups, build/pro snapshot round-trips, and a hand-computed refresher fixture) gates raw-vs-stats divergence. Per-patch replace is transactional (no half-written-patch visibility on Postgres READ COMMITTED) and grades are persisted inside the atom transaction so a tier-list read never pairs new atoms with a stale grade. The real risks are not in the aggregation math but in (1) the decoupling between atom-refresh and read-cache invalidation, which lets the "updated N ago" freshness label overstate what is actually served, and (2) uncalibrated, aggressive tiering priors/floors that collapse thin scopes to a uniform B grade. Remaining items are low-severity, mostly acknowledged edge cases.

- [x] **Atom refresh and read-cache invalidation are decoupled, so the "updated N ago" freshness label can overstate the data actually served** `MED` · `small`
  - **Fix:** Have RefreshPrecomputedAnalyticsJob invalidate (or SetAsync-overwrite) the patch's analytics cache tag immediately after committing the atoms, or drive the read-cache invalidation off the atom ComputedAtUtc rather than off match-ingestion counters, so the served data and the freshness label advance together.
  - **Why:** After the hourly atom refresh advances ComputedAtUtc, the tier list / win rates a user sees can still be served from a pre-refresh cache entry until the next threshold-gated invalidation fires — up to the 24h TTL on a low-traffic patch where the adaptive threshold is rarely met. The page can display "updated a few minutes ago" while showing numbers computed from atoms that predate the refresh, undermining trust on the product's core surface.
  - **Where:** `/Users/kronic/Projects/Personal/Transcendence/transcendence_backend/Transcendence.Service.Core/Services/Jobs/RefreshPrecomputedAnalyticsJob.cs:41`
- [x] **Uncalibrated tiering defaults (500-game floor + 200-game prior-fit) collapse thin scopes to a uniform B tier list** `MED` · `medium`
  - **Fix:** Run the flagged calibration pass against a live patch and lower/scale the floors per scope volume (e.g. relax GradeMinGamesFloor for aggregated region=ALL scopes vs sparse exact tiers), and surface a low-confidence signal when a whole scope collapses to B so it reads as "insufficient data" rather than "balanced."
  - **Why:** On exact-tier scopes and specific-region reads (the fallback re-scoring path), and on any low-population scope, few champions reach 500 games per patch, so the tier list degenerates to all-B with no S/A/C/D differentiation — the opposite of the tier list's purpose. The high-volume region=ALL default scopes are less affected, but the feature quietly under-delivers on the long tail.
  - **Where:** `/Users/kronic/Projects/Personal/Transcendence/transcendence_backend/Transcendence.Service.Core/Services/Analytics/Models/TieringOptions.cs:32`

### Caching Layer

> The caching layer is fundamentally sound: it uses HybridCache (10.2.0, .NET 10) with a shared Redis L2 across the WebAPI and Worker hosts, consistent logical tag-based invalidation (which the .NET docs confirm works cross-node for multi-server setups), correct write-then-invalidate ordering, versioned key prefixes bumped on payload-shape changes, and no negative caching of thrown errors. The most material issues are staleness/negative-caching bugs rather than collisions or security problems: match timelines cache an empty result with no invalidation path, empty/thin analytics get a 24h TTL bounded only by a 2h–24h refresh cadence, and the analytics warm-writer reconstructs cache keys by hand (drift risk against the readers). No critical (data-loss/security) issues were found in this dimension. All findings are correctness/maintainability-grade.

- [x] **Match timelines negatively cache an empty gold/XP chart for up to 1h with no invalidation path** `MED` · `small`
  - **Fix:** Either skip caching when frames are empty (treat empty as a cache-miss), give the timeline entry a short TTL when empty, or tag it (e.g. `match-timeline:{matchId}`) and have MatchTimelineIngestionJob RemoveByTag on completion — mirroring how SummonerRefreshJob invalidates `summoner-stats:{id}` after writing.
  - **Why:** A user opening a match whose timeline hasn't been ingested yet caches an empty-frames timeline for up to 1h (L2) / 15min (L1). When the timeline job later lands the snapshots, there is no hook to invalidate the cached empty result, so the gold-diff chart stays blank for up to an hour after the data actually exists.
  - **Where:** `Transcendence.Service.Core/Services/Analysis/Implementations/SummonerStatsService.cs:1094-1137`
- [x] **Empty / zero-sample analytics results inherit the 24h TTL, bounded only by a 2h–24h refresh cadence** `MED` · `small`
  - **Fix:** Consider a shorter TTL for empty/low-sample payloads (e.g. cache empty analytics for minutes, not 24h) so freshly-arriving data surfaces without waiting for the next patch-tag invalidation.
  - **Why:** For a champion/role/region scope whose data has just begun arriving (below the 500-match/30-min threshold, patch not stale), a cached empty or thin result is served for up to the 2h steady cooldown, and up to 24h in the pathological quiet-patch case, even though rows now exist. This is the 'newly-ingesting patch shows no data' failure class.
  - **Where:** `Transcendence.Service.Core/Services/Analytics/Implementations/ChampionAnalyticsService.cs:43-47,122-128`
- [x] **Analytics warm-writer reconstructs builds/matchups/pro-builds cache keys by hand — silent drift risk vs the readers** `MED` · `small`
  - **Fix:** Extract a single static key-builder per payload type (BuildBuildsKey/BuildMatchupsKey/BuildProBuildsKey) used by both the reader and RefreshDefaultProfileCacheAsync, as already done for win-rates via BuildCacheKey.
  - **Why:** If the two hand-written key formats ever diverge, the warm job keeps running 'green' while every champion-profile read cold-computes after each invalidation — defeating the entire warm path with no error surfaced. This is the classic warm/read key-drift trap the codebase otherwise avoids by warming through read methods in RefreshChampionAnalyticsJob.
  - **Where:** `Transcendence.Service.Core/Services/Analytics/Implementations/ChampionAnalyticsService.cs:495-516`

### API Design, Contracts & OpenAPI

> The API is resource-oriented, uses purpose-built DTOs/records with no raw EF-entity leakage, and — importantly — is gated by a real CI drift check (`pnpm api:check`) so the committed OpenAPI spec cannot silently diverge from the code. The load-bearing weakness is fidelity, not structure: Swashbuckle is not configured for C# nullable-reference-type / required-property support, so the generated TypeScript client (whose `components` types the frontend imports everywhere) systematically misrepresents nullability in both directions and marks nothing required. Secondary issues are a genuinely inconsistent error model (RFC7807 ProblemDetails everywhere except admin endpoints, which return an undocumented `{message,detail}` shape), many untyped success bodies, a missing validation-error schema, and a shipped placeholder field in the profile contract.

- [x] **OpenAPI does not represent C# nullability; generated client types are inverted and never `required`** `MED` · `medium` · ✅ verified
  - **Fix:** Enable options.SupportNonNullableReferenceTypes() in AddSwaggerGen (and enable NRT-required emission), regenerate the spec, and verify nullable object properties emit `allOf:[{$ref}], nullable:true` (OpenAPI 3.0) so openapi-typescript produces `RankInfo | null`. Re-run api:check and fix the resulting diff.
  - **Why:** The generated client (the frontend imports `components` for typing in 13+ files) is wrong in both directions: fields that are always present are typed possibly-null, and fields that are frequently null (soloRank, flexRank, overviewStats, statsAge, grade) are typed non-null. TypeScript cannot catch null-derefs on the nullable objects, and `x === undefined` absence checks are wrong because the wire sends `null`. This defeats the primary value of a typed client and is a latent correctness trap for any new consumer.
  - **Where:** `Transcendence.WebAPI/Program.cs:106-132; openapi/transcendence.v1.json (SummonerProfileResponse); packages/api-client/src/schema.ts:4916-4926`
- [x] **Inconsistent error model: admin endpoints return an undocumented `{message,detail}` body instead of ProblemDetails** `MED` · `medium`
  - **Fix:** Pick one error contract. Simplest: return ValidationProblemDetails/Problem(detail:...) from admin actions (the filter already handles strings) so all 4xx are ProblemDetails; or, if the `{message,detail}` shape is intentional, define an explicit AdminError schema and annotate those actions with [ProducesResponseType(typeof(AdminError),400)].
  - **Why:** The API emits two different error shapes (RFC7807 ProblemDetails vs `{message,detail}`) and the OpenAPI contract misrepresents the admin shape as ProblemDetails. A client coding against the spec expects `.title`/`.detail` but receives `.message`/`.detail`, and content-type/type discovery breaks for admin errors.
  - **Where:** `Transcendence.WebAPI/Controllers/AdminOperationsController.cs:68,79,95,150; Transcendence.WebAPI/Errors/ProblemDetailsErrorBodyFilter.cs:26-28`
- [x] **Many mutation endpoints return anonymous success bodies, producing untyped 200s in the contract** `MED` · `small`
  - **Fix:** Introduce a small typed result record (e.g. OperationResult { string Message; string? Id }) and annotate with [ProducesResponseType(typeof(OperationResult),200)], or return 204 NoContent for pure side-effect operations.
  - **Why:** The generated client types these success payloads as empty/void, so any consumer reading `.message` is untyped and unchecked. The response shape is effectively undocumented and can drift silently (api:check won't catch it because the anonymous object never appears in the schema).
  - **Where:** `Transcendence.WebAPI/Controllers/AdminOperationsController.cs:58,307; ApiKeysController.cs:52; AnalyticsController.cs:131; ChampionAnalyticsController.cs:251`
- [x] **Validation-error (ValidationProblemDetails) schema is absent from the contract** `MED` · `small`
  - **Fix:** Register the ValidationProblemDetails schema (e.g. annotate validated actions with [ProducesResponseType(typeof(ValidationProblemDetails),400)] or add a document/operation filter) so the `errors` shape is part of the contract.
  - **Why:** The per-field `errors` map returned on validation failures is invisible and untyped in the generated client, so consumers cannot reliably surface field-level validation messages against the contract.
  - **Where:** `openapi/transcendence.v1.json (components.schemas); Transcendence.WebAPI/Models/MultiSearch/MultiSearchDtos.cs:10-19`
- [x] **Profile contract ships a permanent placeholder in `championName`** `MED` · `small`
  - **Fix:** Either resolve the real name server-side via the existing StaticDataService, or drop the ChampionName field from the response DTOs entirely (the client already resolves names from static data), making championId the single source of truth.
  - **Why:** A documented contract field permanently carries placeholder data; if a consumer's static-data lookup fails it renders "Champion 157" to end users. It is misleading dead weight in the response and a long-lived stale TODO.
  - **Where:** `Transcendence.WebAPI/Controllers/SummonersController.cs:476-483; apps/web/components/lol-profile/ProfileSidebar.tsx:207`

### Testing

> The backend suite is sizeable (207 xUnit facts/theories across ~44 files, plus 20 frontend vitest files) and contains several genuinely high-value tests: a rigorous raw-vs-precompute equivalence gate for analytics, incident-driven regression guards, and strong refresh-token-rotation coverage. However, the correctness of the two most safety-critical areas is essentially unverified: (1) all authentication crypto — hand-rolled PBKDF2 password hashing/verification, JWT signing, and the API-key auth handler — is entirely untested and merely mocked away; and (2) the entire data layer is validated only against SQLite (via EnsureCreated) and the EF InMemory provider, never a real PostgreSQL, so Postgres/Npgsql translation, migrations, and the authorization boundary are all unexercised. Assertion quality in the seeded service tests is good; a minority of controller tests are near-tautological forwarding checks.

- [x] **All authentication crypto is untested — hand-rolled PBKDF2 hashing, login verification, JWT signing, and API-key handler are only mocked** `HIGH` · `medium` · ✅ verified
  - **Fix:** Add unit tests: HashPassword→VerifyPassword round-trip, wrong-password rejection, malformed/empty stored-hash rejection, cost-factor-upgrade path (storedIterations < PasswordIterations), LoginAsync with a real IJwtService for unknown user / bad password / success, JwtService.ResolveSigningKey's four branches (dev fallback, dev-require, prod-missing throw, prod-placeholder throw), and ApiKeyAuthenticationHandler (missing header→NoResult, empty→Fail, invalid→Fail, valid→claims incl. bootstrap).
  - **Why:** The most security-critical code paths in the product have no automated proof of correctness. A regression in the stored-hash format parsing, base64 salt handling, timing-safe comparison, cost-factor-upgrade branch, JWT claim/role emission, or the ResolveSigningKey guard that blocks the dev placeholder key in production would ship silently — enabling auth bypass, account lockout, or an insecure signing key in prod. These are pure/near-pure functions that are trivial to unit-test.
  - **Where:** `Transcendence.Service.Core/Services/Auth/Implementations/UserAuthService.cs:50-71,186-221; JwtService.cs:1-103; Transcendence.WebAPI/Security/ApiKeyAuthenticationHandler.cs:18-46`
- [x] **No test runs against real PostgreSQL — data layer is validated only on SQLite and the EF InMemory provider** `HIGH` · `large` · ✅ verified
  - **Fix:** Introduce a small integration tier using Testcontainers.PostgreSql (or a CI Postgres service) that runs at least the analytics equivalence tests and the SummonerStatsService read-path against real Postgres; keep SQLite for fast unit feedback but stop relying on it as the sole correctness backing for query-heavy code.
  - **Why:** Any behavior differing between SQLite/InMemory and PostgreSQL — Npgsql array/JSONB columns, collation/case-sensitivity, NULL ordering in ORDER BY, decimal vs double aggregation, GROUP BY semantics, or EF-to-SQL translation that throws only on Npgsql — is invisible to the suite. A query that passes InMemory can throw or return different rows in prod. The suite's green status materially overstates data-layer confidence.
  - **Where:** `tests/Transcendence.Service.Core.Tests/Support/SqliteCompatibleTranscendenceContext.cs:14-20; ChampionAnalyticsStatsEquivalenceTests.cs:175-179; tests/Transcendence.WebAPI.Tests/AnalyticsControllerTests.cs:120-126`
- [x] **Migrations are never applied in any test — schema is built from the model via EnsureCreated, so the 43 migrations' SQL is unverified** `MED` · `medium` · ✅ verified
  - **Fix:** Add a CI/integration step that applies the full migration chain (dotnet ef database update) against a throwaway Postgres from an empty DB, ideally asserting the resulting schema equals the EnsureCreated model schema.
  - **Why:** A migration with invalid raw SQL, a bad data-backfill step, or an ordering/dependency problem would pass CI (model still matches snapshot) and only fail when manually applied to prod Postgres, where recovery is high-risk. Model/snapshot agreement does not prove the migration that produces that snapshot can actually run.
  - **Where:** `tests/Transcendence.Service.Core.Tests/*.cs (all use db.Database.EnsureCreatedAsync()); .github/workflows/ci-web-backend.yml (migration-safety job); scripts/ci/check-migrations.sh`
- [x] **Authorization boundary is never exercised — controller tests bypass [Authorize], API-key scheme, and admin policies via a hand-built principal** `MED` · `large` · ✅ verified
  - **Fix:** Add end-to-end tests with WebApplicationFactory hitting representative public / AppOnly (X-API-Key) / admin endpoints with no credentials, wrong credentials, and correct credentials, asserting 401/403 vs 200. At minimum, unit-test the ApiKeyAuthenticationHandler and policy definitions directly.
  - **Why:** The access-control boundary — arguably the highest-risk thing to get wrong in an admin/AppOnly API — has no regression protection. Dropping an [Authorize] attribute, misconfiguring a policy, or breaking the X-API-Key scheme would not fail any test.
  - **Where:** `tests/Transcendence.WebAPI.Tests/SummonersControllerTests.cs:243-273; ProSummonersControllerTests.cs:47-54; AdminOperationsControllerTests.cs`
- [x] **Frontend tests cover only lib/ pure functions — zero component, page, or rendering tests despite substantial UI** `MED` · `large`
  - **Fix:** Add React Testing Library component tests for the highest-risk interactive components (command palette, tier-list table, profile/match-history), focused on state transitions and empty/error/loading branches; wire them into `pnpm --filter web test`.
  - **Why:** Regressions in rendering logic, loading/empty/error states, accessibility affordances, and data wiring in the React tree are caught only by the compose e2e script (not in the standard PR test gate) or by manual review. The frontend correctness signal is limited to domain/proxy utilities.
  - **Where:** `apps/web/lib/*.test.ts (20 files) + apps/web/proxy.test.ts; apps/web/package.json:10`

### Frontend Architecture

> The App Router architecture is largely sound and, in places, genuinely sophisticated: a clean BFF trust-boundary design (no-credentials public allowlist proxy, per-namespace credential injection, cookie stripping in both directions), no secrets in the client bundle, a well-reasoned Next 16 `proxy.ts` middleware that refreshes-and-persists tokens before render, and correct Suspense streaming on the champion detail page. The main architectural weaknesses are concentrated in the summoner-profile surface, which abandons the server-streaming pattern used elsewhere and pushes nearly all data fetching (match history, rank history, and four full static-data maps) to the client as a post-hydration waterfall. Secondary issues are missing per-request deduplication of `getSessionMe` (blocking, un-suspended, called twice on admin routes) and a couple of pages/fetch paths that fetch on the client where the server already has the data.

- [x] **Summoner profile abandons server streaming for a full client-side data waterfall** `MED` · `large` · ✅ verified
  - **Fix:** Fetch page-1 match history (and rank history) server-side in the RSC — the profile response already yields summonerId, so a two-step server fetch is a fast server-to-server hop — and stream it behind a Suspense boundary like the champion page does. Keep client fetching only for interaction-driven re-loads (pagination, queue/sort/champion filters, match-detail expand).
  - **Why:** Every profile view shows the shell, then a match-history spinner and layout shift while an avoidable browser round-trip completes; TTI and perceived latency are worse than the champion page, which streams the same class of data server-side behind Suspense. The profile is the most-visited authenticated surface, so the inconsistency is costly.
  - **Where:** `apps/web/components/SummonerProfileUnified.tsx:202-352`
- [x] **Full DDragon static maps fetched on the client on every profile view** `MED` · `medium`
  - **Fix:** Pass the static maps down from the server render as props (they are already React-cached server-side), or at minimum add `cache: "force-cache"`/an explicit browser max-age so repeat navigations don't re-fetch. Consider only shipping the item/champion subset actually referenced by the loaded matches.
  - **Why:** Four extra client round-trips plus parsing of large JSON payloads on each profile load, and no reliable browser caching between visits. The server render already has these maps cached via `fetchChampionMap`/`fetchItemMap`, so this is pure duplicated work shipped to the client.
  - **Where:** `apps/web/components/SummonerProfileUnified.tsx:207-212`
- [x] **getSessionMe is not request-deduplicated and blocks the document shell** `MED` · `small`
  - **Fix:** Wrap getSessionMe in React `cache()` to collapse the duplicate calls within a request, and wrap <AccountNav/> in a Suspense boundary (with a lightweight fallback) so session resolution never blocks the page shell.
  - **Why:** Redundant backend /me round-trips per render for logged-in users, and a global TTFB tax that undercuts the deliberate Suspense streaming elsewhere (the champion page can't stream its shell until AccountNav's /me resolves).
  - **Where:** `apps/web/lib/session.ts:33`

### Frontend Performance

> The frontend is thoughtfully built on server components + ISR with backend precompute, streaming on the champion detail page, paginated match history, and consistently CLS-safe images (explicit width/height, next/font swap). The dominant risk is the Tier List: the entire ~170-row champion ladder renders in a single "use client" component with no virtualization, and each row mounts heavy per-row subcomponents including a self-contained Radix Tooltip.Provider — inflating hydration, TBT, and memory. Secondary risks are a site-wide first-load JS penalty from eagerly bundling framer-motion + cmdk in the root layout, a fully-blocking multi-fetch waterfall on the pro-builds index (no streaming), an unoptimized full-resolution splash JPG on every champion page, and over-eager Link prefetch across the dense tier-list table.

- [x] **Tier List renders the entire ~170-row ladder unvirtualized as one client component** `MED` · `large` · ✅ verified
  - **Fix:** Virtualize the table body (e.g. @tanstack/react-virtual) or cap the initial render with a 'show more' boundary. At minimum add CSS `content-visibility: auto` + `contain-intrinsic-size` on `.tierlist-row`/tier sections so off-screen rows skip layout/paint. Consider keeping the row list as a server component and only hydrating the interactive controls.
  - **Why:** All ~170 rows — each with a next/image icon, DataBar (with CI-whisker math), ConfidenceBadge, LaneIcon and a Tooltip — are serialized into the RSC/HTML payload and fully re-rendered on hydration. This drives up Total Blocking Time, hydration cost, and DOM size on the site's flagship analytics page. Sorting/filtering re-sorts and re-renders the whole array (memoized but still O(n) per interaction). On a role-filtered view the row count can climb further.
  - **Where:** `apps/web/components/TierListTable.tsx:1,579,590`
- [x] **framer-motion + cmdk eagerly bundled into every route via GlobalCommandPalette in the root layout** `MED` · `medium`
  - **Fix:** Lazy-load the palette with `next/dynamic(..., { ssr: false })` triggered on first Cmd+K/open, and add `experimental.optimizePackageImports: ['radix-ui','framer-motion']` to next.config. Prefer CSS transitions over framer-motion for the simple height/opacity match-card expand where feasible.
  - **Why:** Site-wide first-load JavaScript is larger than necessary (framer-motion core alone is tens of KB gzipped), hurting TBT/LCP on first navigation to any route, including light pages like the landing and login.
  - **Where:** `apps/web/app/layout.tsx:5,67 ; apps/web/components/GlobalCommandPalette.tsx:1-4`
- [x] **Pro-builds index blocks first byte on a two-phase, up-to-13-request server fetch with no streaming** `MED` · `medium`
  - **Fix:** Wrap the champion pro-build feed in `<Suspense>` so the toolbar + playrate table stream immediately while the per-champion feed loads. Consider a single backend endpoint that returns the feed rather than fanning out N per-champion requests.
  - **Why:** On an ISR cache miss/revalidation the user waits for two sequential network batches (5 then 8 backend round-trips) before any content paints, delaying TTFB/LCP. Unlike the champion detail page, there is no Suspense shell to stream the header first.
  - **Where:** `apps/web/app/lol/pro-builds/page.tsx:131-197`
- [x] **Unoptimized full-resolution champion splash JPG loaded via CSS background on every champion page** `MED` · `small`
  - **Fix:** Route it through next/image (fill + a small `sizes`) or use the smaller `_0` loading/centered crop, and/or serve it as a low-res/blurred decorative layer. Since it's purely decorative, consider `fetchpriority=low` and lazy behavior.
  - **Why:** Every champion page eagerly downloads a large full-res JPG purely for a 30%-opacity backdrop, wasting bandwidth and competing with LCP on mobile/slow connections for no data value.
  - **Where:** `apps/web/app/lol/champions/[championId]/page.tsx:163,171-175`
- [x] **Over-eager Link prefetch across the dense tier-list table (~340 default-prefetch links)** `MED` · `trivial`
  - **Fix:** Set `prefetch={false}` on the row/analyze links (or prefetch on hover/focus only, as the command palette already does with router.prefetch), keeping prefetch for the small set of likely-clicked links.
  - **Why:** Scrolling the tier list can trigger a large number of RSC prefetch fetches to the champion route, adding network/CPU pressure on the client and origin with low hit value (most rows are never clicked).
  - **Where:** `apps/web/components/TierListTable.tsx:323-333,406-411`

### Infrastructure, CI/CD & Observability

> Infra hygiene is genuinely strong for a self-hosted single-host stack: all three images run non-root and multi-stage, GH Actions are SHA-pinned, images are cosign-signed and path-filtered per component, and there is a thoughtful liveness/readiness split plus a thread-pool-proof worker watchdog and OpenTelemetry+Grafana metrics. The material gaps are all in deploy safety and alerting: prod auto-migrates on worker startup yet no CI stage ever executes migrations against a real Postgres (tests are SQLite), so a runtime-failing migration crash-loops the worker while poll-deploy reports the deploy as a success; poll-deploy does no post-deploy health verification and its own pipeline failures are silent; there are no metrics-based alert rules (Prometheus/Grafana) so nothing alerts on API errors, DB/Redis down, or the worker being fully down; and several safety-critical docs (auto-migrate, wud) are stale/contradictory. None are data-loss or security-critical, but the migration/deploy-safety cluster is a real production risk.

- [x] **poll-deploy performs no post-deploy health verification and its own pipeline failures are silent — a broken deploy is reported as successful** `MED` · `medium`
  - **Fix:** After `up -d`, poll the container health/`/health/ready` for a bounded window and only then report success (else notify failure and optionally roll back to the previous image id). Emit an alert (not just a WARN) when remote/local digest resolution fails N consecutive times.
  - **Why:** No automatic rollback and no truthful deploy signal: operators get a ✅ Discord ping while the service is down, and a broken ghcr-token path silently freezes all deploys (the exact class of failure that motivated replacing wud) without any alert.
  - **Where:** `scripts/ops/poll-deploy.sh:82-101,85,87`
- [x] **No metrics-based alerting: Prometheus has no rules/alertmanager and Grafana has no alerting provisioning; in-app alerts cover only ingestion** `MED` · `medium` — Grafana provisioned alerting added (`config/monitoring/grafana/provisioning/alerting/`): WebAPI/worker down, API 5xx ratio, API p95 latency → Discord contact point via `DISCORD_ALERT_WEBHOOK_URL`. DB/Redis covered indirectly (worker-down + API 5xx); dedicated exporters deferred.
  - **Fix:** Add Prometheus alert rules + alertmanager (or Grafana provisioned alerting) for the high-value signals (up==0 per target, API error ratio, request latency, DB connection saturation) routed to the same webhook, so an external process — not the dying worker — raises the alarm.
  - **Why:** Nothing pages on API 5xx/latency spikes, Postgres or Redis being down, host resource exhaustion, or the worker being fully down. Worse, the ingestion alerter runs INSIDE the worker, so if the worker crash-loops (see other findings) there is nothing left to alert that it is down — the one component whose death you most need to know about is the one that self-reports.
  - **Where:** `config/monitoring/prometheus.yml:1-23; config/monitoring/grafana/provisioning/ (datasources + dashboards only); Transcendence.Service.Core/Services/Diagnostics/WebhookAlertNotifier.cs:11-21`
- [x] **Stale/contradictory docs on safety-critical behaviors: CI claims prod 'never auto-migrates' and ARCHITECTURE still describes wud as the deployer** `MED` · `trivial`
  - **Fix:** Fix the CI comment to reflect that prod auto-migrates (which makes the hot-table gate MORE important), and update ARCHITECTURE.md's Deployment section to describe the systemd poll-deploy pipeline as the source of truth. Consider centralizing the deploy runbook in one place the ops README already owns.
  - **Why:** An engineer relying on the CI comment will mis-reason about the migration-safety gate's purpose, and an on-call operator following ARCHITECTURE.md during an incident will look for wud (gone) instead of the systemd timer / poll-deploy.sh that actually controls deploys — costly confusion at exactly the wrong moment.
  - **Where:** `.github/workflows/ci-web-backend.yml:86-88; docs/ARCHITECTURE.md:234; scripts/ops/README.md:1-13`
- [x] **Web container ships no healthcheck — a hung Next.js process is never detected or restarted** `MED` · `small`
  - **Fix:** Add a lightweight health route (e.g. app/api/health) and a HEALTHCHECK in apps/web/Dockerfile (or the compose web service) so a hung frontend is marked unhealthy and restarted like the other two services.
  - **Why:** If the Next.js server deadlocks or wedges (event loop stall, memory pressure) it stays 'running' with no health signal; Docker won't restart it, poll-deploy's `up -d` won't gate on it, and nothing depends_on its health. The user-facing frontend can be dead while every dashboard shows green.
  - **Where:** `apps/web/Dockerfile:27-44; compose.yml:69-88`

### Documentation & Developer Experience

> Documentation quality is above average and the recent TFT removal was executed cleanly (zero stale TFT references anywhere in docs or the OpenAPI spec). Env-var/compose mappings, referenced files, and the API.md endpoint list are almost entirely accurate, and DEVELOPMENT.md/ARCHITECTURE.md contain genuinely excellent operational runbook content. The most serious problem is a canonical doc (ARCHITECTURE.md) that still describes the old, explicitly-retired prod deploy mechanism (wud) — an ops runbook that would actively misdirect during an incident. Secondary gaps: a fully committed Prometheus/Grafana observability stack is undocumented despite ~60 documented metric names, plus a scatter of smaller inaccuracies (endpoint count, one undocumented endpoint, a wrong host URL).

- [x] **Canonical ARCHITECTURE.md documents the retired 'wud' deploy mechanism as the live prod pipeline** `HIGH` · `small`
  - **Fix:** Rewrite ARCHITECTURE.md's 'Deployment & rollback' section to describe the systemd poll-deploy.sh timer as the live mechanism, note wud is retired for app containers, and link scripts/ops/README.md as the authoritative deploy runbook; consider promoting that file into the canonical docs list.
  - **Why:** ARCHITECTURE.md is on AGENTS.md's 'Canonical Docs (Keep These Correct)' list. An operator or agent debugging a stuck deploy per the canonical doc would investigate wud — dead for the app containers — wasting time during an incident, while the actual poller/timer is undiscoverable from any top-level doc. The repo's own doc-hygiene policy is violated for its most incident-critical runbook.
  - **Where:** `docs/ARCHITECTURE.md:234,239 (vs scripts/ops/README.md:3, scripts/ops/transcendence-deploy.timer:2)`

### UX — Home, Navigation & Tier List

> The home/nav/search surface is genuinely high-craft and answer-first: a search-forward hero with a Cmd+K palette, a live Emerald+ "Top Picks" preview pinned to the same scope its links lead to, principled responsive column-drop on the dense tier list, and a coherent flat "command-deck" system (tier rails, diverging DataBars, Confidence chips, tabular figures). The most serious problems are accessibility regressions on the densest, most-used surfaces: the tier-list table's interactive elements have no visible keyboard-focus indicator even though native outlines are globally suppressed, and the flagship command palette lacks modal/combobox semantics (no dialog role, focus trap, focus return, or aria-activedescendant). Structurally, the "sticky Toolbar/head" design language is not actually realized — the filter toolbar is position:relative and the table's sticky column headers are silently broken by an overflow-x wrapper — so deep in a ~170-row list the user loses both column context and filter access, and below the xl breakpoint the TierSpine jump-nav disappears entirely. Copy and decoration are mostly restrained, except the command palette, which is over-chromed and off-brand relative to the rest of the system.

- [x] **Tier-list interactive elements have no visible keyboard focus indicator (native outline globally removed)** `HIGH` · `small` · ✅ verified
  - **Fix:** Apply the existing FOCUS_RING treatment (focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/35 ring-offset) to the sort header buttons, champion row Links, Analyze links, and TierSpine buttons; consider a shared row-focus utility so the whole clickable row shows a ring.
  - **Why:** Keyboard-only and low-vision users tabbing through the densest, most-interactive table on the site see no focus indicator at all on the sort controls, every champion link, and the tier jump-nav — a WCAG 2.4.7 (Focus Visible) failure directly contradicting the "Inclusive by default" principle, and it is impossible to tell where focus is among 170 rows.
  - **Where:** `apps/web/app/globals.css:233; apps/web/components/TierListTable.tsx:268-273,323-324,406-411; apps/web/components/ui/TierSpine.tsx:49-57`
- [x] **Command palette (the flagship nav control) lacks modal and combobox accessibility semantics** `MED` · `medium`
  - **Fix:** Wrap the palette in Radix Dialog (or add role="dialog" aria-modal + a focus trap and return focus to the launcher on close), switch the field to cmdk `Command.Input` to restore combobox/aria-activedescendant wiring, and replace the native region `<select>` with the shared Select primitive.
  - **Why:** For the product's primary, search-forward navigation, screen-reader users are not told a modal opened, cannot Tab-cycle within a trap (focus can leak to the visually covered page), lose their place on close, and get no announcement of the currently selected result. It degrades the most important interaction for assistive-tech users.
  - **Where:** `apps/web/components/GlobalCommandPalette.tsx:489-503,543,591-599,604-616`
- [x] **Tier-list sticky column headers never actually stick (overflow-x wrapper + header occlusion)** `MED` · `medium`
  - **Fix:** Remove the overflow-x wrapper (columns already fit via progressive column-drop) or replace it with a horizontal-only scroll technique that preserves viewport-relative sticky, and offset the sticky head with `top` equal to the header height (and a z-index above the rows) so it lands below the site header rather than behind it.
  - **Why:** On a ~170-row table, once the user scrolls past the first screen the column labels (Win Rate / Strength / Pick / Ban / Games / Trend) are gone, so numeric columns lose their meaning exactly when the list is longest — the opposite of the CLAUDE.md "compact sticky Toolbar headers" signature intent.
  - **Where:** `apps/web/components/TierListTable.tsx:576-582,587-592; apps/web/app/globals.css:536-540; apps/web/components/SiteHeaderClient.tsx:42`
- [x] **Deep in the ~170-row list there is no persistent way to filter or jump (non-sticky toolbar; xl-only spine)** `MED` · `medium`
  - **Fix:** Make the filter Toolbar (or a condensed version) sticky below the header, or make the tier-pill/search row sticky; consider showing a compact TierSpine or a floating "jump to tier / back to filters" affordance below xl.
  - **Why:** On the common 1024–1279px laptop/tablet width, a user reading tier C/D at the bottom of the board cannot re-filter, re-search, change rank/lane, or jump tiers without scrolling all the way back to the top — and even on xl the spine only jumps tiers, it does not expose the filters. Navigability of the one-page full ladder degrades sharply below xl.
  - **Where:** `apps/web/app/globals.css:375-381; apps/web/app/lol/tierlist/page.tsx:139-175; apps/web/components/TierListTable.tsx:435-465,538-547`
- [x] **Command palette is over-decorated and off-brand relative to the restrained system** `MED` · `small`
  - **Fix:** Cut the status-chip row and the explanatory paragraph (a keyboard hint footer is enough), rename "Meta Pages"/"routes" to plain "Pages"/section names, and reserve the red spotlight/hairline for genuine emphasis to keep the one-accent discipline.
  - **Why:** The primary navigation surface carries the most decoration and the least data-forward copy on the whole site; on short viewports the chrome pushes actual results below the fold and the "routes/Meta Pages" language is more machine-voiced than the clean labels used elsewhere.
  - **Where:** `apps/web/components/GlobalCommandPalette.tsx:557-583,803-806,132-144`
- [x] **Login has no password-recovery or account-recovery path** `MED` · `medium`
  - **Fix:** Add a "Forgot password?" link next to the password field wired to a reset flow (or, if reset is intentionally unsupported, state that explicitly so users understand the account is disposable).
  - **Why:** A user who forgets their password is fully locked out of their saved favorites and account state with no self-service recovery — a dead-end flow. Severity is bounded because the account only manages site state (per the page's own copy), not game data.
  - **Where:** `apps/web/app/account/login/page.tsx:70-111`

### UX — Champion & Pro-Builds Pages

> The champion detail page is genuinely strong: op.gg/u.gg-class density with far higher polish, a well-orchestrated progressive-disclosure Builds card (timing-aware sectioned breakdown + open "Recommended" and collapsed alternatives), diverging DataBars with real 95% CI whiskers, and a plain-English sample banner. The main problems are (1) a systemic keyboard-accessibility gap — the global reset strips focus outlines and the shared tab/pill/button controls on these exact pages add none back; (2) the "which build do I actually use?" answer is undermined by a "Recommended" build showing a lower win rate than a collapsed "Alternative," and by the Pro Builds detail page being a raw data dump rather than a synthesized build; and (3) several comprehensibility rough edges (jumbled win-rate-by-rank order, cryptic Confidence pips, redundant error stacking). Nothing here is data-loss/security; severity tops out at High for the focus-visible failure.

- [x] **Interactive tab/pill/button controls have no visible keyboard focus indicator (WCAG 2.4.7 failure)** `HIGH` · `small` · ✅ verified
  - **Fix:** Add a shared focus-visible ring to `.control-tab` (e.g. `:focus-visible { box-shadow: inset 0 -2px 0 var(--t-primary), 0 0 0 2px color-mix(in oklch, var(--t-primary), transparent 78%); }`) and to the hero pill Links / Search button. Keep the global `outline:none` only where a token ring replaces it.
  - **Why:** A keyboard-only user tabbing through the champion detail role filters, matchup sort, region/scope filters, and pro-builds pages sees no indication of which control is focused — they cannot tell where they are. This is a clear WCAG 2.1 SA 2.4.7 (Focus Visible) failure across the primary interactive surface of both in-scope pages, and directly contradicts CLAUDE.md principle 6 ('keyboard navigability … accessibility is a baseline').
  - **Where:** `apps/web/app/globals.css:233-235 and :604-617; apps/web/components/RoleFilterTabs.tsx:44-52; apps/web/components/MatchupsTable.tsx:54-69; apps/web/app/lol/champions/[championId]/page.tsx:297-308`
- [x] **"Recommended Build" can show a lower win rate than the collapsed "Alternative 1", with no explanation** `MED` · `small`
  - **Fix:** Either sort so the recommended build is defensibly best, or label the axis of recommendation (e.g. 'Most played · 144 games' vs 'Higher win rate, smaller sample') and add a one-line tooltip explaining that Recommended weights sample size/popularity. The existing Confidence pip vocabulary could carry this.
  - **Why:** This directly undercuts the page's core job ('which build do I actually use?'). A casual user sees a build labelled 'Recommended' underperforming a hidden 'Alternative' by ~7pp; a competitive user optimizing win rate is actively misled into distrusting the ranking. The naming asserts a recommendation the visible numbers contradict, with zero rationale.
  - **Where:** `apps/web/app/lol/champions/[championId]/page.tsx:526-554 (BuildRows) — screenshot d-champ-detail.png Builds card`
- [x] **Pro Builds detail page is a raw data dump, not a synthesized build — no answer to "which build do I use?"** `MED` · `medium`
  - **Fix:** Reuse/adapt BuildBreakdown (or at least name the common builds by their differentiator — 'Lethality opener', 'Bruiser') and lead with a single recommended pro build + runes before the per-match feed.
  - **Why:** The page a user reaches specifically to see how the best players build a champion is the weakest build view in the product. A first-time visitor cannot extract 'the pro build' — they get an undifferentiated list. This wastes the strong BuildBreakdown component that already exists and violates CLAUDE.md principle 4 (progressive disclosure / show the answer first).
  - **Where:** `apps/web/app/lol/pro-builds/[championId]/page.tsx:369-397 (Common Builds) and :400-487 (Recent Pro Matches) — screenshot d-probuild-detail.png`
- [x] **Win-rate-by-rank strip is ordered by sample size, not by the rank ladder** `MED` · `trivial`
  - **Fix:** Sort by the canonical rank ordinal (ranks.ts already has the ladder ordering used elsewhere), keep games in the tooltip/whisker. Optionally add tiny rank crests for scannability.
  - **Why:** A casual user scanning 'how does this champ do at MY rank?' must hunt through an arbitrary order; the eye expects a monotonic Iron→Challenger (or reverse) ladder. The diverging bars are excellent but the axis they hang off is unreadable as a progression.
  - **Where:** `apps/web/app/lol/champions/[championId]/page.tsx:464-467 — screenshot d-champ-detail.png ('WIN RATE BY RANK')`
- [x] **Confidence pips are undecodable without hover/focus and are a tiny, repeated tab target** `MED` · `small`
  - **Fix:** Show a short inline label on wider layouts (e.g. 'Stable') or an accessible-by-default caption; enlarge the hit/focus target to ≥24px; consider making it non-focusable when an adjacent text label already conveys the same signal to reduce tab noise.
  - **Why:** On mobile (no hover) a casual user sees three cryptic bars with no way to learn what they mean; on keyboard they collect dozens of tiny tab stops. CLAUDE.md insists 'Confidence is DATA, never decoration' — but here the data is only legible to a mouse user who happens to hover.
  - **Where:** `apps/web/components/ui/Confidence.tsx:41-64 (used throughout BuildBreakdown.tsx:219,247,282,298)`
- [x] **Pro Builds detail error state stacks a "Data Unavailable" card on top of three empty section cards** `MED` · `small`
  - **Fix:** When `!proBuildsRes.ok`, render only the error card (and keep the Filters card) — suppress the empty Top Players/Common Builds/Recent Matches sections instead of showing three redundant empties.
  - **Why:** A single backend failure produces four stacked failure/empty panels saying roughly the same thing, plus a header badge reading '0 matches' and 'Patch Unknown'. It reads as broken and cluttered rather than one clean error, and buries the still-useful filters.
  - **Where:** `apps/web/app/lol/pro-builds/[championId]/page.tsx:324-336 then :338-487`

### UX — Summoner Profile & Auth

> The profile and auth surfaces show high craft and a coherent "Ladder" system: colorblind-safe recent-form pips (fill+shape redundancy), small-n win-rate guards, reduced-motion-gated expand animations, deep-linkable URL state, and layout-matching skeletons. The two biggest UX defects are (1) the mobile DOM order buries the primary content — match history sits below five secondary sidebar cards — and (2) a failed/not-found profile renders a permanent loading skeleton beneath the error banner, which reads as "broken" rather than "not found". Auth is functional but incomplete (no password recovery, no reveal toggle), the Favorite control has no persistent/toggle state, and several ad-hoc low-opacity text tints drop below WCAG AA in the light theme.

- [x] **Mobile: match history is buried below all five sidebar cards (primary content pushed far down)** `HIGH` · `medium` · ✅ verified
  - **Fix:** Interleave with `order` utilities so mobile order is hero → Ranked snapshot → PerformanceCard → MatchHistorySection → (mastery, champion pool, duo, live-game). Split ProfileSidebar into a top slice (ranked) that floats above matches and a bottom slice that drops below on mobile. Keep the desktop xl two-column layout unchanged.
  - **Why:** On the most-used device class, the profile's primary content (match history + recent-form context) is the last thing a user reaches, contradicting the "show the answer first" / progressive-disclosure principle. Casual players (a stated primary audience) come for recent games yet must scroll through mastery/duo/live-game cards first.
  - **Where:** `apps/web/components/SummonerProfileUnified.tsx:446-457`
- [x] **Failed / not-found profile renders a permanent loading skeleton under the error banner** `MED` · `small`
  - **Fix:** When `error && !profile`, replace the skeleton card with a dedicated EmptyState (the primitive exists) — e.g. "We couldn't find <RiotId> in <REGION>" with a Search CTA and a Retry action — instead of an endless Skeleton. Only render the skeleton while `polling`/`busy`.
  - **Why:** A not-found or errored profile looks half-broken/still-loading rather than clearly "we couldn't find this player." Undermines trust on exactly the failure path where clarity matters most, and offers no recovery action (search again, retry).
  - **Where:** `apps/web/components/SummonerProfileUnified.tsx:441-444`
- [x] **Auth flow is incomplete: no password recovery, no reveal toggle, no confirm-password** `MED` · `medium`
  - **Fix:** Add a "Forgot password?" link + reset flow (or explicitly state recovery isn't available if intentionally omitted). Add a password show/hide toggle to both forms and a confirm-password field to register. Keep the email-only stance if desired.
  - **Why:** A user who forgets their password has zero in-product recovery path; lockout forces a second account. No reveal toggle raises entry friction on the enforced 12-char passwords, and no confirm field means a signup typo silently locks the new account. The "No Riot auth" stance is a fine deliberate choice; the missing recovery is the real gap.
  - **Where:** `apps/web/app/account/login/page.tsx:70-111`
- [x] **Add Favorite is add-only: no favorited/toggle state, silent duplicates, non-actionable login prompt** `MED` · `medium`
  - **Fix:** Fetch favorite state on mount and render a real toggle (Added ✓ ↔ Add Favorite) that removes on second press; make the 401 message a Link to /account/login with a return path; disable/replace the button once saved to prevent duplicate POSTs.
  - **Why:** Users can't tell a player is already saved, may create duplicates, and a signed-out user is told to log in with no one-click way to do so. The control reads as a one-shot rather than a stateful toggle.
  - **Where:** `apps/web/components/FavoriteButton.tsx:29-59`
- [x] **Ad-hoc low-opacity foreground text tints fall below WCAG AA in the light theme** `MED` · `small`
  - **Fix:** Replace sub-muted fg tints (text-fg/55, text-fg/64) on small type with the `text-muted` token (or bump to >=text-fg/72, which passes). Audit the profile for `text-fg/5x`/`/6x` on text-xs and standardize on the token so contrast holds in both themes.
  - **Why:** Timestamps, rank indices, and per-champion meta are the scannable scaffolding of the profile; in light mode they are hard to read for low-vision users, breaching the "inclusive by default" baseline. Dark mode is borderline-passing but light fails.
  - **Where:** `apps/web/components/lol-profile/ProfileSidebar.tsx:205`
- [x] **"Expand details" affordance is a faint text-only label with no caret/icon** `MED` · `small`
  - **Fix:** Add a rotating chevron icon (mirroring the by-role <details> caret in PerformanceCard.tsx:156-162) next to the label and raise its contrast (text-muted or fg/80). Consider a subtle full-width bottom affordance so the expand target reads as interactive on touch.
  - **Why:** Discoverability of the richest feature (per-match scoreboard, runes, takeaways, gold curve) hinges on a subtle label many users won't register; the type-overline + fg/65 also compounds the contrast issue above. Progressive disclosure only works if the "there's more here" cue is legible.
  - **Where:** `apps/web/components/lol-profile/MatchHistorySection.tsx:300-302`
- [x] **Live Game check is a buried, manual one-shot with no auto-refresh or loading skeleton** `MED` · `medium`
  - **Fix:** Surface a live indicator higher (e.g. an auto-checked "In game" badge in the hero when detected), add a skeleton/spinner during the fetch, stamp the result with a checked-at time, and offer a light auto-refresh/Re-check while state is IN_PROGRESS. At minimum move the card above duo/mastery on mobile.
  - **Why:** The most time-sensitive profile feature (is this player in a game right now?) is the hardest to find and requires deliberate action; there's no freshness once shown and no visible progress during the request, so most users never engage it.
  - **Where:** `apps/web/components/LiveGameCard.tsx:229-289`

---

## P2 — Product to Daily-Driver — Discoverability, Multi-Mode & the Champ-Select Hook

With the core hardened, invest in reach and habit loops. SEO and multi-mode analytics are the biggest unlocks; the multi-search backend already exists and just needs a UI. This is the phase that turns a trustworthy Solo/Duo tool into a general-purpose LoL site.

**12 items** · 4 high · 5 medium · 3 low/info

### ★ Priority sequence (audit recommendation)

_Discoverability, multi-mode analytics, and the champ-select hook_

1. Add the SEO/discoverability layer: generateMetadata on champion/profile/tierlist pages, app/sitemap.ts + robots.ts, metadataBase, and per-page OpenGraph images (the single biggest growth blocker).
1. Add a queue dimension to the analytics precompute — ARAM first (own tiering, no roles), then Arena/Flex — with a queue selector in the filter bars; the data is already ingested.
1. Ship the /lol/multi-search champ-select page (paste lobby → rank/main-role/form table) on the existing backend endpoint, wired into the command palette; the README already advertises it.
1. Add leaderboards (regional Challenger/GM ladders + per-champion one-trick ladders) on the existing Ranks/summoner data.
1. Persist per-patch champion win-rate history and per-game LP deltas to power real trend charts on champion pages and a true profile LP graph.
1. Fix the profile server-streaming regression: fetch page-1 match history + rank history in the RSC behind Suspense and pass static maps down from the server render instead of the client waterfall (SummonerProfileUnified.tsx:202-352).

### Product Completeness & Opportunities

> Transcendence has a genuinely strong, trustworthy ranked-Solo/Duo analytics core (empirical-Bayes tiers with an explicit confidence layer, fast precomputed reads, rich post-game analysis, timing-aware builds, a Porofessor-lite live-scouting card) and better craft than most of the field. But as a general-purpose daily driver it is narrow: analytics is hard-wired to Ranked Solo/Duo only (no ARAM/Arena/Flex/Normal despite those being ingested and playing on profiles), there is essentially no SEO/discoverability layer, an advertised multi-search champ-select tool has no UI, and whole competitor-standard surfaces are absent (leaderboards/ladder, item & rune stat pages, esports, RSO account linking, trend graphs, team-comp/duo tools). The core is trustworthy and polished; the surface area and growth loop are not yet competitive with op.gg / u.gg / lolalytics.

- [x] **No SEO / discoverability layer — the growth engine every competitor runs on is missing** `HIGH` · `medium` · ✅ verified
  - **Fix:** Add `generateMetadata` to champions/[championId] ('Aatrox Build, Runes & Counters — Patch X.Y'), summoner profiles ('Kronic#NA1 — Rank, Match History'), and tierlist; add app/sitemap.ts (enumerate champions + top summoners) and app/robots.ts; set metadataBase and per-page openGraph images. Next.js 16 App Router makes this first-class.
  - **Why:** op.gg / u.gg / lolalytics get the overwhelming majority of their traffic from Google (users type a summoner name or 'aatrox build' into search). With no per-page titles, no structured data, no sitemap and no OG images, none of these pages are individually indexable or shareable — Discord/Twitter link previews are all identical. This is the single biggest blocker to Transcendence becoming a *daily driver at scale*: users can't discover it, and can't be pulled back by a shared link.
  - **Where:** `apps/web/app/layout.tsx:27 (only static metadata); grep for generateMetadata across apps/web/app → 0 hits; no apps/web/app/sitemap.ts or robots.ts; apps/web/public/ has no robots.txt/sitemap`
- [x] **Analytics is Ranked Solo/Duo ONLY — no ARAM, Arena, Flex or Normal tier lists / champion stats** `HIGH` · `large` · ✅ verified
  - **Fix:** Add a queue dimension to the analytics precompute (at minimum ARAM, then Arena/Flex): a queueFamily filter on tierlist/champion endpoints and a queue selector in TierListFilterBar/FilterBar. ARAM needs its own tiering (no roles, different sample math).
  - **Why:** ARAM is one of the most-played modes in League and Arena has a large recurring audience; lolalytics and u.gg both ship dedicated ARAM and Arena tier lists and champion pages. A 'general-purpose LoL site' that returns nothing for the modes a huge fraction of casual players actually play cannot be their daily driver. The data is already in the corpus, so this is a surfacing gap, not an ingestion one.
  - **Where:** `Transcendence.Service.Core/Services/Analytics/Implementations/ChampionWinRateComputeService.cs:52,151,264,371 (.InRankedSoloQueue()); PrecomputedAnalyticsRefresher.cs:36 (RANKED_SOLO_5x5); AnalyticsController.cs GetPatches filters QueueId==RankedSoloDuoQueueId; QueueCatalog.cs IsRankedAnalyticsQueue → only 420`
- [x] **Multi-search (champ-select scouting) is advertised and built in the backend but has no UI** `HIGH` · `medium` · ✅ verified
  - **Fix:** Build a /lol/multi-search page (paste lobby names → table of rank, main role, autofill risk, recent form), and wire a 'paste multiple summoners' affordance into the command palette / search box. The backend contract already exists.
  - **Why:** Champ-select multi-search is a core daily-use hook on u.gg, Blitz, and Porofessor — players paste the lobby to scout teammates/opponents every game. Shipping the endpoint but not the UI is both a missed retention driver and a credibility problem (the README advertises a feature users can't find).
  - **Where:** `Transcendence.WebAPI/Controllers/SummonersController.cs:401 [HttpPost("multi-search")]; README.md 'multi-search (up to 5 players) for champ-select'; grep 'multi-search|multiSearch' across apps/web → 0 hits`
- [x] **No leaderboards / ladder pages (regional or champion one-trick ladders)** `HIGH` · `medium` · ✅ verified
  - **Fix:** Add a leaderboard endpoint (top ranks by region/queue, and top players per champion+role) backed by the existing Ranks/summoner data, plus a /lol/leaderboard page with region and champion filters.
  - **Why:** Regional Challenger/GM ladders and champion-specific one-trick leaderboards are table stakes on op.gg/u.gg — they drive both discovery (players look themselves/pros up) and retention (climbers track rank). Their absence removes a whole class of return visits.
  - **Where:** `absent — searched routes under apps/web/app/lol/*, all Controllers, and Transcendence.Service.Core for 'leaderboard|ladder' → 0 product hits`
- [x] **Live-game scouting is shallow vs Porofessor and hard to discover (no dedicated route, not in nav)** `MED` · `large` · ✅ verified
  - **Fix:** Give live game a first-class /live entry point and enrich the DTO: per-opponent champion pool + recent streaks, chosen runes/summoners when available, and periodic refresh while in-game.
  - **Why:** Porofessor/GameLenses win the pre-game/in-game moment with deep opponent pools, tilt/streak signals and live objective tracking, and users open them every game. Transcendence's version is a static one-shot team summary buried in a profile sidebar, so it can't own that high-frequency moment.
  - **Where:** `Transcendence.Service.Core/Services/LiveGame/Models/LiveGameAnalysisDtos.cs; LiveGameController.cs:21; mounted only at apps/web/components/lol-profile/ProfileSidebar.tsx:256; SiteHeaderClient.tsx:16-18 nav has no live entry`
- [x] **No trend-over-time views: shallow LP history and no per-patch win-rate graphs** `MED` · `medium` · ✅ verified
  - **Fix:** Persist per-patch champion win-rate history and render a trend chart on champion pages; capture per-game LP deltas (or backfill from ranked snapshots) for a true profile LP graph.
  - **Why:** op.gg shows a full LP-per-game graph; lolalytics/u.gg show 30-day and patch-over-patch win-rate trends that climbers use to judge whether a pick is rising or falling. Without real time series, users can't answer 'is this champ getting stronger?' or 'how has my LP moved?' — both core recurring questions.
  - **Where:** `apps/web/components/lol-profile/ProfileSidebar.tsx:47-56 (snapshot-based LP sparkline, self-hides <2 points); TierListTable.tsx:69 'Trend' column = tier movement/previousTier only, not a time series`
- [x] **No Riot account linking (RSO) — email-only auth, manual favorites, no personalization** `MED` · `large` · ✅ verified
  - **Fix:** Add Riot RSO login to establish a verified main summoner, auto-populate favorites/home, and personalize the landing page around the signed-in player's data.
  - **Why:** Competitors let a user link Riot once and get a personalized home (their profile, their champions, their recent games) on every visit — the strongest retention loop for a daily driver. Without it, onboarding is generic and the site never becomes 'my' dashboard.
  - **Where:** `Transcendence.WebAPI/Controllers/AuthController.cs (register/login/refresh/password-reset only); UserPreferencesController.cs favorites are manual GUID adds; grep 'rso|oauth' across source → 0 hits`
- [x] **No standalone item or rune analytics pages** `MED` · `medium` · ✅ verified
  - **Fix:** Add /lol/items and /lol/runes index + detail pages driven by the existing build corpus (win rate / pick rate per item and per rune, by champion/role).
  - **Why:** u.gg and lolalytics ship item stat pages ('which champs build this, win rate by item') and rune-stats pages. These are secondary but expected exploration surfaces for theorycrafters and add indexable long-tail SEO pages. Their absence narrows the site to champion/profile only.
  - **Where:** `absent — apps/web/app/lol/ contains only champions, pro-builds, summoners, tierlist; apps/web/app/api/static/{items,runes} are Data Dragon passthrough only`
- [x] **No team-composition, synergy, or duo tools — only per-champion counters** `MED` · `medium` · ✅ verified
  - **Fix:** Add champion synergy (co-occurrence win rate for bot-lane and jungle+lane pairs) and a 'best partners' section on champion pages, reusing the existing matchup compute pipeline.
  - **Why:** Mobalytics/u.gg offer synergy and duo/lane-partner data and team-comp tools that competitive duos rely on. Counters alone answer 'is this matchup winnable' but not 'who should I duo/pair with', a common recurring question.
  - **Where:** `Transcendence.WebAPI/Controllers/ChampionAnalyticsController.cs:223 (matchups only); grep 'synerg|duo' across source → 0 hits`
- [x] **No esports / pro-match coverage — 'Pro Builds' is solo-queue builds, not matches** `LOW` · `large` · ✅ verified
  - **Fix:** If pursuing breadth, integrate an esports schedule/results feed (e.g. LoL Esports data) into a /lol/esports hub; otherwise clarify positioning so 'Pro' isn't confused with esports coverage.
  - **Why:** u.gg, Mobalytics and Blitz run esports hubs (schedules, results, pro team comps) that pull fans in daily during splits. This is a differentiator rather than table stakes, but its absence caps engagement outside of ranked players.
  - **Where:** `apps/web/app/lol/pro-builds + ProAnalyticsController.cs:25,42 (champions/players); grep 'esport|lck|lec|worlds|schedule' across source → 0 hits`
- [x] **No retention/growth hooks: no notifications/alerts, no sharing/embeds, no companion/overlay** `LOW` · `medium` · ✅ verified
  - **Fix:** Start cheap: 'favorite is live now' surfacing on the favorites page and shareable OG cards for profiles/champions; consider a lightweight browser or in-client companion later.
  - **Why:** Daily drivers rely on pull mechanics: Blitz/Porofessor use a desktop client, op.gg pushes 'your favorite is in game', and everyone leans on shareable cards. Transcendence is purely pull-to-refresh in a browser tab, so it has no mechanism to bring users back between intentional visits.
  - **Where:** `absent — grep 'notification|webhook|alert|embed' across source → 0 product hits; no in-client/overlay app in repo; only manual favorites (UserPreferencesController)`
- [x] **Casual-user onboarding is thin — landing is search + tier list, no guidance layer** `LOW` · `small` · ✅ verified
  - **Fix:** Add a lightweight guidance strip for query-less visitors (e.g. 'easy champions to climb with' by role, a one-line 'how tiers are computed' explainer) without compromising the data-first center of gravity.
  - **Why:** PRODUCT.md deliberately centers competitive climbers, which is defensible — but the owner's stated goal is a *general-purpose* daily driver, and a first-time casual visitor with no summoner name in mind gets a tier list and little else. Competitors offer 'champions to climb with', role-based starter guidance, and explainer copy.
  - **Where:** `apps/web/app/page.tsx (search launcher + tier list 'top picks' strip); SiteHeaderClient.tsx:16-18 nav = Tier List/Champions/Pro Builds`

---

## P3 — Polish, Refactors & Cleanup

Longer-horizon structural debt (god-file decomposition, the layering break, duplication), performance/secondary-surface polish, documentation drift, and the long tail of low-severity items. Pay these down once the product is trustworthy and competitive.

**71 items** · 0 high · 12 medium · 59 low/info

### ★ Priority sequence (audit recommendation)

_Personalization, secondary surfaces, performance, and refactors_

1. Add Riot RSO account linking to establish a verified main and personalize the home/favorites; add a stateful Favorite toggle and a password-recovery flow.
1. Add item and rune stat pages and champion synergy/duo tools from the existing build/matchup corpus (secondary surfaces + long-tail SEO).
1. Virtualize the tier-list body (or content-visibility), hoist a single Radix Tooltip.Provider, lazy-load the command palette (framer-motion/cmdk out of the root bundle), and stream the pro-builds index behind Suspense.
1. Decompose the remaining god-files: split SummonerStatsService into stats/match-history/RuneSelectionMapper, extract the shared MatchService participant factory, and break SummonerProfileClient into cohesive hooks with a StaticDataContext.
1. Fix mobile profile ordering (match history above sidebar cards), replace the not-found loading skeleton with an EmptyState, make profile filters page-scope-honest or server-side, and fix the contrast tints below AA in light theme.
1. Update canonical docs (ARCHITECTURE deploy section, CI auto-migrate comment, observability stack), enrich live-game scouting into a first-class /live surface, and add named constants/options for the scattered magic numbers and hardcoded tunables.

### Backend Architecture & Layering

> The 4-project split (Data → Service.Core → WebAPI/Service) is coherent at the assembly level with no circular project references, and DI lifetimes are handled carefully — singletons that need scoped state correctly use IServiceScopeFactory, and no captive DbContext was found. The Hangfire lane topology and non-pooled-vs-pooled DbContext choices are deliberate and well-documented. The main architectural weaknesses are on the presentation boundary: several WebAPI controllers reach past Service.Core straight into EF/DbContext and repositories (query and write logic, including SaveChanges, live in controllers), refresh-lock orchestration is duplicated verbatim across two controllers, and admin DTO ownership is deliberately mislabeled into a WebAPI namespace while physically living in Service.Core. One god-service (SummonerStatsService, 1642 LOC) survived the P10.1 decomposition.

- [x] **Refresh-lock orchestration duplicated verbatim across SummonersController and ProSummonersController** `MED` · `medium`
  - **Fix:** Extract the acquire→enqueue→compensate flow into a single Service.Core orchestrator (e.g. ISummonerRefreshOrchestrator.QueueRefreshAsync) returning a small result the controllers map to IActionResult; both controllers call it.
  - **Why:** The lock TTL, priority-key semantics, compensation-on-enqueue-failure, and contention-response shape must be kept in sync by hand across two files; a fix or change to the lock protocol in one controller will silently diverge from the other, risking inconsistent refresh behavior (e.g. leaked locks) for one caller path.
  - **Where:** `Transcendence.WebAPI/Controllers/SummonersController.cs:304-389; Transcendence.WebAPI/Controllers/ProSummonersController.cs:222-299`
- [x] **Admin DTOs physically in Service.Core are declared under `namespace Transcendence.WebAPI.Controllers`, and facades `using Transcendence.WebAPI.Controllers`** `MED` · `medium`
  - **Fix:** Move the DTOs to a Service.Core-owned namespace (e.g. Transcendence.Service.Core.Services.Admin.Models) and, if the generated OpenAPI schema id must stay stable, pin it via Swashbuckle's SchemaId/CustomSchemaIds rather than by mislabeling the namespace.
  - **Why:** Namespace no longer reflects assembly/layer ownership. A reader (or static-analysis/dependency-direction tooling) sees Service.Core types and usings in a WebAPI namespace and cannot tell that the real dependency direction is intact. It also couples the Service.Core contract names to a WebAPI-shaped naming decision (OpenAPI schema stability), making a future namespace correction a breaking OpenAPI/client-regen change.
  - **Where:** `Transcendence.Service.Core/Services/Admin/Models/AdminOperationsContracts.cs:3-8; Transcendence.Service.Core/Services/Admin/Implementations/AdminJobsFacade.cs:9; Transcendence.Service.Core/Services/Admin/Implementations/AdminOverviewFacade.cs:13`
- [ ] **SummonerStatsService is a 1642-LOC god service; its interface bundles unrelated concerns (ISP violation)** `MED` · `large`
  - **Fix:** Split along the seams: keep summoner-scoped aggregates in ISummonerStatsService and extract match rendering (GetMatchDetailAsync/GetMatchTimelineAsync) into an IMatchDetailService, mirroring the prior god-file decomposition.
  - **Why:** Any consumer that only needs one match detail takes a dependency on the entire summoner-stats surface; the class is a change-magnet and hard to test in isolation. The parallel P10.1 effort already split other god-files (e.g. the 4-service decomposition noted in project memory), but this one was left intact, so the codebase is now inconsistent about where the 'god service' line is.
  - **Where:** `Transcendence.Service.Core/Services/Analysis/Interfaces/ISummonerStatsService.cs:6-53; Transcendence.Service.Core/Services/Analysis/Implementations/SummonerStatsService.cs (1642 LOC)`
- [x] **Service-locator anti-pattern for telemetry inside controllers, with all exceptions swallowed** `LOW` · `small`
  - **Fix:** Inject IRefreshLockLifecycleTelemetry via the constructor (it is always registered) or fold telemetry into the proposed refresh orchestrator; drop the service-locator and the empty catch.
  - **Why:** Hides a real dependency from the constructor (harder to see/test), and the blanket catch would silently mask a genuine misconfiguration where the telemetry service is missing. Combined with the duplicated refresh logic, it signals the orchestration was pasted rather than shared.
  - **Where:** `Transcendence.WebAPI/Controllers/SummonersController.cs:550-560,568-578; duplicated in Transcendence.WebAPI/Controllers/ProSummonersController.cs`
- [ ] **Inconsistent Interfaces/Implementations/Models folder convention across Service.Core service areas** `LOW` · `medium`
  - **Fix:** Either document these as intentional exceptions (small/cross-cutting areas stay flat) or normalize the larger areas (Diagnostics, Jobs) to the Interfaces/Implementations/Models layout used elsewhere.
  - **Why:** Discoverability cost: contributors cannot rely on a single rule for where an interface vs implementation lives, and the repo-map's stated convention does not hold uniformly. Low risk, purely maintainability.
  - **Where:** `Transcendence.Service.Core/Services/{Cache,Diagnostics,Database,Jobs} vs {Admin,Analysis,Auth,LiveGame,StaticData,RiotApi}`

### Backend Code Quality & Anti-Patterns

> The largest services split cleanly into two camps. ChampionAnalyticsService is a genuinely well-decomposed facade that delegates all heavy compute to focused sub-services, and read paths are disciplined (AsNoTracking, DTO projection, tag-based cache invalidation, consistent OperationCanceledException rethrow, no empty catch blocks). The other three, however, carry serious duplication debt: MatchService triplicates ~120-line ingestion bodies (author-acknowledged), and rune-selection parsing is re-implemented three times across two files — and the copies have already drifted, which is a latent correctness bug, not just style. SummonerStatsService is a 1642-LOC god class mixing 10 query responsibilities with ~300 lines of rune mapping. Magic numbers (the 5000 stat-mod threshold, retention/retry constants) are scattered as bare literals rather than named/config-bound.

- [x] **MatchService triplicates the entire match-ingestion body across three methods** `MED` · `medium` · ✅ verified
  - **Fix:** Extract a single `MapParticipant(Participant p, Match match, Summoner summoner)` factory and a shared `BuildParticipants(info, resolver, match)` helper parameterized by a summoner-resolution delegate. The three public methods then differ only in how they resolve summoners (full API resolve vs stub vs retry) and in their persistence/error wrapping.
  - **Why:** Any field added to MatchParticipant, or any bug fixed in one ingestion path, must be manually mirrored in two others or the paths silently diverge. This is exactly the class of drift that produces 'lightweight import has stale/missing data' bugs. It also inflates the file and obscures the small real differences (stub-creation vs Riot resolution) between the three flows.
  - **Where:** `Transcendence.Service.Core/Services/RiotApi/Implementations/MatchService.cs:126-160, 313-346, 475-509`
- [x] **Rune-selection parsing is re-implemented three times and the copies have already diverged** `MED` · `medium` · ✅ verified
  - **Fix:** Promote a single `RuneSelectionMapper` (taking StoredRuneSelection[] + a metadata lookup) that returns the structured primary/sub/statShard tuple, and have all three call sites consume it. Delete the duplicate HasStructuredSelections.
  - **Why:** BuildRunesDto can assign a stat-mod path id (5000) or 0 as a primary rune style where the other two implementations reject it -- the same participant's runes can be rendered differently on match-detail vs match-history vs build pages. This is a live correctness inconsistency, and the duplication guarantees future fixes land in only one copy.
  - **Where:** `Transcendence.Service.Core/Services/Analysis/Implementations/SummonerStatsService.cs:1268-1352 & 1454-1551; Transcendence.Service.Core/Services/Analytics/Implementations/ChampionBuildPathBuilder.cs:381-468`
- [ ] **SummonerStatsService is a 1642-LOC god class spanning 10 unrelated query responsibilities plus rune mapping** `MED` · `large`
  - **Fix:** Split along seams: a SummonerStatsService (overview/champions/roles/season), a SummonerMatchHistoryService (recent matches + match detail/timeline), and a shared RuneSelectionMapper. Each becomes independently testable.
  - **Why:** SRP is broken: a change to match-timeline aggregation and a change to rune parsing touch the same 1600-line file, raising merge-conflict and regression surface. The rune-mapping block is the very code duplicated in ChampionBuildPathBuilder, so its being buried here also hides the reuse opportunity.
  - **Where:** `Transcendence.Service.Core/Services/Analysis/Implementations/SummonerStatsService.cs:17-1642`
- [x] **The '5000' stat-mod rune-path threshold is a bare literal repeated in 13+ sites across 4 files** `MED` · `small`
  - **Fix:** Define `public const int StatModPathId = 5000;` (and helpers `IsStatModPath` / `IsRealRunePath`) in one shared static, e.g. alongside the rune metadata types, and reference it everywhere including the StaticDataService assignment.
  - **Why:** The meaning ('5000 == synthetic stat-mod path id') lives nowhere as a name, so a reader must reverse-engineer it, and if Riot's data scheme ever shifts, the value must be found and changed correctly in a dozen places across four files. Classic primitive-obsession / magic-number smell.
  - **Where:** `Transcendence.Service.Core/Services/StaticData/Implementations/StaticDataService.cs:401; SummonerStatsService.cs:1328,1336,1495,1503,1523,1530; ChampionBuildPathBuilder.cs:410,417,440,447; RuneSelectionIntegrityBackfillJob.cs:160,168,178`
- [ ] **SummonerStatsService has two near-identical overview/champion compute methods differing only by a date filter** `MED` · `medium`
  - **Fix:** Parameterize a single private method with optional `(long? startMs, long? endMs)` bounds (null = all-time) feeding a shared aggregate projection, and delete the season-specific copies.
  - **Why:** ~130 lines of duplicated aggregation logic. The two champion-stat copies have already drifted in style -- the first uses a `0, // fill KDA after` placeholder then a second `x with` pass (193, 205-210) while the second computes KDA inline (445) -- showing the copies are maintained independently.
  - **Where:** `Transcendence.Service.Core/Services/Analysis/Implementations/SummonerStatsService.cs:66-144 vs 314-394; and 161-211 vs 396-453`
- [x] **Operational tunables in MatchService are hardcoded literals while sibling policies are options-bound** `MED` · `small`
  - **Fix:** Move these into an options record (e.g. MatchFetchOptions { RetentionDays, MaxRetries, BackoffSeconds[] }) bound from appsettings, mirroring PatchPromotionOptions.
  - **Why:** Changing the Riot retention window (a Riot-API policy, not a code invariant) or tuning retry behavior requires a code change + redeploy, and the '730 == 2 years' relationship is only documented in a comment. Inconsistent with the codebase's own configuration convention.
  - **Where:** `Transcendence.Service.Core/Services/RiotApi/Implementations/MatchService.cs:373,549,560-561`
- [x] **NonCacheablePatchFallbackException is used purely as control flow to abort memoization** `LOW` · `small`
  - **Fix:** Have the fetch helpers return the boolean cacheability they already compute, and pass it to a cache API that supports 'compute-but-don't-store' (or gate the SetAsync on the flag) instead of throwing.
  - **Why:** Exceptions-as-control-flow obscures the happy path, costs stack-unwinding on a routine branch, and couples the two helpers through a private exception type rather than an explicit return contract.
  - **Where:** `Transcendence.Service.Core/Services/StaticData/Implementations/StaticDataService.cs:186-202, 162-184`
- [x] **Cache-key strings are hand-built in multiple places and must match a controller method by convention only** `LOW` · `medium`
  - **Fix:** Centralize each cache-key shape in a single builder (extend the existing BuildCacheKey pattern to builds/matchups/probuilds) and share the most-played-lane resolver so warmer and reader provably agree.
  - **Why:** If a key format or the lane-selection heuristic changes in one location, the cache-warming path silently writes keys the read path never queries (cold cache, wasted compute) with no compile-time or test failure. Fragile coupling.
  - **Where:** `Transcendence.Service.Core/Services/Analytics/Implementations/ChampionAnalyticsService.cs:258,303,408,495,501,512,618-628`

### Data Model & Migrations

> The EF model is generally well-engineered: composite unique indexes double as UPSERT conflict targets and read lookups, global query filters for unfetchable matches are applied consistently across every match-dependent entity, and the covering-index strategy (bare shape in the model, INCLUDE payload in idempotent CONCURRENTLY raw-SQL migrations) is unusually disciplined and thoroughly documented. CI backs this with both a hot-table DDL lint and a has-pending-model-changes drift guard. The real weaknesses are relational-integrity smells rather than outright bugs: a redundant Match↔Summoner join table that is written on every ingest but never read, hard FKs from immutable match facts to mutable versioned static-data tables, nullable columns carrying UNIQUE indexes that Postgres does not enforce across NULLs, and a test harness (EF InMemory for WebAPI, SQLite-EnsureCreated for Core) that never exercises the real Postgres DDL/migrations and only partially enforces constraints. There are no decimal-precision issues because the model has no decimal columns (rates use double, counts use int).

- [ ] **No optimistic-concurrency tokens (rowversion / xmin) on any entity** `LOW` · `medium`
  - **Fix:** For rows with genuine multi-writer contention (Summoner), add an xmin concurrency token (Npgsql .UseXminAsConcurrencyToken()) so conflicting updates fail fast instead of silently overwriting; document that the RefreshLock table is the intended serialization mechanism otherwise.
  - **Why:** Last-writer-wins on concurrently-updated rows (e.g. Summoner profile/UpdatedAt/LastActiveAtUtc) with no lost-update detection. Risk is largely mitigated because the precomputed-analytics writes are idempotent ON CONFLICT upserts and Summoner refresh is serialized via the RefreshLock table, so this is a design gap rather than an active corruption source.
  - **Where:** `Transcendence.Data/Models/** (grep for RowVersion/IsConcurrencyToken/Timestamp/byte[] returns nothing); Transcendence.Data/TranscendenceContext.cs (no concurrency config)`
- [x] **Analytics stat key columns are unbounded `text`, inconsistent with the bounded summoner-stat tables** `LOW` · `small`
  - **Fix:** Add HasMaxLength to the analytics key columns to match the summoner-stat conventions (e.g. Patch 32, PlatformRegion 16, RankScope/Role/Feature 64).
  - **Why:** Functionally harmless on Postgres (text == varchar performance), but these are short enum-like codes participating in composite unique indexes; leaving them unbounded is inconsistent, weakens self-documentation, and removes a cheap guard against accidental oversized values.
  - **Where:** `Transcendence.Data/TranscendenceContext.cs:610-669 (ChampionRoleTierStat/ChampionScopeGradeStat/ChampionBanScopeStat/ChampionMatchupStat/ChampionBuildSnapshot/AnalyticsResponseSnapshot); contrast SummonerMatchFact config at :531-554`
- [x] **`Summoner.PlatformRegion` / `Region` declared `required string?` (required modifier on a nullable type)** `INFO` · `trivial`
  - **Fix:** Drop the nullable `?` (make it `required string`) so the type matches the intent and the DB column is NOT NULL.
  - **Why:** Contradictory intent: the field is meant to be mandatory yet the type allows null, and PlatformRegion drives the region-scoped indexes (IX_Summoners_Region_UpdatedAt, search-prefix) and candidate selection — a null-region row silently falls outside all per-region queries. Low practical risk since ingestion always sets a region.
  - **Where:** `Transcendence.Data/Models/LoL/Account/Summoner.cs:19-20`

### Query Efficiency & EF Core Usage

> The analytics read surface is, on the whole, thoughtfully engineered: composable query objects fold predicates into single WHEREs, purpose-built covering indexes (with documented prod EXPLAIN wins) back the dominant scans, the precompute-aggregate layer keeps the default read paths off raw match data, AsNoTracking is applied consistently, and match-detail uses AsSplitQuery to avoid cartesian explosion. The gaps are concentrated in (a) the summoner-profile read path, where the hot endpoint tracks and over-fetches an unbounded unused navigation and several aggregates are computed client-side over a summoner's full participant history instead of in SQL, and (b) a raw analytics fallback that materializes an entire scope's distinct-MatchId set into app memory where its sibling paths keep it as a subquery. None are data-loss or security issues; the highest-impact one runs on every profile page load.

- [x] **Pro champion-playrate fetches all pro participant rows to group in memory** `LOW` · `small`
  - **Fix:** Aggregate in SQL: GroupBy(ChampionId) selecting Count(), Sum(Win) and a distinct-Puuid count, returning only the per-champion rows.
  - **Why:** Bounded by roster size and precompute-backed for region=ALL, but the region-specific / uncached path scans and ships every tracked-pro participant row for the patch to compute a small ranked list.
  - **Where:** `Transcendence.Service.Core/Services/Analytics/Implementations/ChampionProComputeService.cs:299-326`
- [x] **Active-season resolution runs an uncached DB query on every profile-stats request** `LOW` · `trivial`
  - **Fix:** Wrap the active-season resolution in a short HybridCache entry (minutes) keyed on the current time bucket, mirroring ActivePatchCacheOptions.
  - **Why:** Even when a profile's stats are fully cache-warm, every load still round-trips to RankedSeasons. The table is tiny and indexed (StartUtc/EndUtc index), so cost per call is small, but it defeats part of the caching intent on a hot path and is inconsistent with the patch-lookup treatment.
  - **Where:** `Transcendence.Service.Core/Services/Analysis/Implementations/SummonerStatsService.cs:222; Transcendence.Service.Core/Services/Analysis/RankedSeasonResolver.cs:16-26`
- [x] **Rank-history snapshot check queries per iteration through a navigation instead of the indexed shadow FK** `LOW` · `small`
  - **Fix:** Query HistoricalRanks by the shadow FK (EF.Property<Guid?>(hr, "SummonerId") == summoner.Id) and, if worth it, batch the existence checks for all incoming queue types into one query before the loop.
  - **Why:** A per-row awaited query in a loop on the rank write path. Bounded (a summoner has ~2-3 queue types) so real impact is small, but each iteration pays an avoidable join instead of an indexed shadow-FK seek.
  - **Where:** `Transcendence.Data/Repositories/Implementations/RankRepository.cs:36-46`

### Ingestion & Background Jobs

> The ingestion layer is unusually mature for a personal project: a per-region token-bucket rate gate, dedicated Hangfire queue lanes with bounded worker pools, adaptive throughput + starvation-guardrail + discovery-backpressure controls, cursor-based backfills, and idempotent match/timeline persistence with per-row duplicate fallbacks. Cancellation is propagated correctly in most jobs and the rate-gate refill is race-safe. The material risks are concentrated in the failure/retry semantics: transient rate-gate backpressure is conflated with genuine fetch failure (permanently discarding matches), refresh locks are released by key rather than by owner (defeating dedup under queue backlog), and several paths have overlapping enqueue sources with no cross-source concurrency guard. None are guaranteed prod outages, but a few are silent-data-loss / wasted-budget hazards that surface exactly during a new-patch ingestion surge when the rate budget is tightest.

- [x] **Rate-gate exhaustion when listing match IDs is indistinguishable from end-of-history, silently truncating the window** `LOW` · `small`
  - **Fix:** Distinguish 'gate exhausted' from 'empty result' — e.g. return a nullable/sentinel from GetMatchIdsByPuuidAsync so callers can break-and-reschedule (retry later) on exhaustion rather than treating it as end-of-history and completing the backfill.
  - **Why:** When the region's token bucket is momentarily empty during a match-id list call, the summoner's window sync (or, worse, a full-history backfill) terminates early as if it had reached the end of history. For the head/window sync this self-heals on the next scheduled refresh, but for FullHistoryBackfillJob it can prematurely mark the backfill Completed and stop paging older matches. The conflation is most likely precisely during a ramp when budget is tightest.
  - **Where:** `Transcendence.Service.Core/Services/Jobs/RiotMatchIdsClient.cs:21-24; SummonerRefreshJob.cs:366-381, 553-568; FullHistoryBackfillJob.cs:100-118`
- [x] **No reactive 429 / Retry-After handling in application code; pipeline relies entirely on proactive pacing** `LOW` · `medium`
  - **Fix:** Add a 429-aware catch around Riot calls that reads Retry-After (or Camille's rate-limit exception), briefly drains/pauses the affected region's bucket, and reschedules without counting the attempt as a real failure. At minimum, confirm and document how Camille surfaces 429 so the proactive-only assumption is validated.
  - **Why:** If the bucket is mis-tuned relative to the live key tier, or the key is shared / the limit changes, an actual 429 is surfaced as a generic exception and treated as a normal fetch failure (incrementing RetryCount, contributing to the PermanentlyUnfetchable flip), rather than a backoff signal. The system has no closed-loop reaction to real rate-limit responses.
  - **Where:** `Transcendence.Service.Core/Services/RiotApi/Implementations/MatchService.cs:43-44, 196-197, 400; MatchTimelineIngestionJob.cs:115-116; FullHistoryBackfillJob.cs:276`

### Analytics Correctness & Precompute

> The precompute layer is unusually well-built for correctness: a single pure `ChampionTierScorer` is shared by the raw compute, the stats read, and the refresher, and a comprehensive equivalence/fixture test suite (win-rates, unified + role-filtered tier lists, matchups, build/pro snapshot round-trips, and a hand-computed refresher fixture) gates raw-vs-stats divergence. Per-patch replace is transactional (no half-written-patch visibility on Postgres READ COMMITTED) and grades are persisted inside the atom transaction so a tier-list read never pairs new atoms with a stale grade. The real risks are not in the aggregation math but in (1) the decoupling between atom-refresh and read-cache invalidation, which lets the "updated N ago" freshness label overstate what is actually served, and (2) uncalibrated, aggressive tiering priors/floors that collapse thin scopes to a uniform B grade. Remaining items are low-severity, mostly acknowledged edge cases.

- [x] **Per-region distinct-match denominators double-count matches whose participants span platform regions** `LOW` · `small`
  - **Fix:** Either key region off the match's PlatformRegion (single value) rather than the participant's summoner region, or document this as an accepted approximation; no urgent action given the rarity and raw/stats consistency.
  - **Why:** For the rare cross-platform match (account transfers), per-region ban-rate and match-count denominators are inflated, slightly skewing region-specific ban/pick rates. Region=ALL rows are unaffected (computed by a distinct global re-scan). Impact is small because a ranked match is normally single-platform.
  - **Where:** `/Users/kronic/Projects/Personal/Transcendence/transcendence_backend/Transcendence.Service.Core/Services/Analytics/Implementations/PrecomputedAnalyticsRefresher.cs:399`
- [x] **Role-filtered raw tier list computes contested/presence from role-only games, diverging from the region=ALL persisted grade** `LOW` · `small`
  - **Fix:** For role-filtered requests, compute the presence numerator from the champion's full cross-role scope games (or pass cross-role totals into the scorer) so the contested index is stable across role and region filters; alternatively document that the contested index is role-scoped in the role-filtered view.
  - **Why:** The same champion's ContestedScore (and role-scoped ban denominator) differs between a specific-region role-filtered view (raw path, role-only presence) and the region=ALL view (persisted grade, cross-role presence). Only the secondary "most contested" index and displayed ban rate are affected; tier/strength/win/pick are correct.
  - **Where:** `/Users/kronic/Projects/Personal/Transcendence/transcendence_backend/Transcendence.Service.Core/Services/Analytics/Implementations/ChampionWinRateComputeService.cs:268`
- [ ] **Non-core surfaces refresh in separate transactions, allowing transient cross-surface staleness and an implicit ordering dependency** `LOW` · `medium`
  - **Fix:** Document (or assert) the required phase ordering, and consider a single job-level transaction or a completion marker so partial refreshes are detectable; keep RefreshBuilds after RefreshTabularCore explicitly.
  - **Why:** A crash/cancellation between phases leaves fresh role-tier atoms + grades alongside stale build/matchup/pro snapshots for the same patch until the next successful run. Because these are independent surfaces the user-visible effect is minor and self-heals hourly, but the ordering coupling is implicit and could break under a future refactor that reorders or parallelizes the phases.
  - **Where:** `/Users/kronic/Projects/Personal/Transcendence/transcendence_backend/Transcendence.Service.Core/Services/Analytics/Implementations/PrecomputedAnalyticsRefresher.cs:215`
- [x] **Low-sample protection is one-sided: it caps S/A to B but still allows C/D for thin samples** `INFO` · `trivial`
  - **Fix:** If the intent is to avoid over-punishing thin samples, symmetrically clamp low-sample champions toward B (both directions), or accept the asymmetry explicitly in the doc.
  - **Why:** A champion just under the 500-game floor with an unlucky loss streak can be graded D while an equally-thin sample can never be graded S — an asymmetry. In practice empirical-Bayes shrinkage pulls thin samples toward the baseline, so reaching C/D requires a fitted-small prior plus a persistent negative delta, making the real-world impact small; but the protection is inconsistent.
  - **Where:** `/Users/kronic/Projects/Personal/Transcendence/transcendence_backend/Transcendence.Service.Core/Services/Analytics/ChampionTierScorer.cs:190`

### Caching Layer

> The caching layer is fundamentally sound: it uses HybridCache (10.2.0, .NET 10) with a shared Redis L2 across the WebAPI and Worker hosts, consistent logical tag-based invalidation (which the .NET docs confirm works cross-node for multi-server setups), correct write-then-invalidate ordering, versioned key prefixes bumped on payload-shape changes, and no negative caching of thrown errors. The most material issues are staleness/negative-caching bugs rather than collisions or security problems: match timelines cache an empty result with no invalidation path, empty/thin analytics get a 24h TTL bounded only by a 2h–24h refresh cadence, and the analytics warm-writer reconstructs cache keys by hand (drift risk against the readers). No critical (data-loss/security) issues were found in this dimension. All findings are correctness/maintainability-grade.

- [x] **Patch rollover does not invalidate the cached active-patch pointer** `LOW` · `trivial`
  - **Fix:** Have PromotePatchAsync also invalidate the active-patch key (or the 'analytics' tag) so the new active patch is picked up immediately rather than after the TTL.
  - **Why:** For up to ~5 minutes after a new patch is promoted, every analytics endpoint continues to resolve the OLD patch version from the cached pointer, briefly serving old-patch analytics. Self-heals on the 5-min TTL and is partly benign (a just-promoted patch has little data), so impact is small.
  - **Where:** `Transcendence.Service.Core/Services/Analytics/Implementations/ChampionAnalyticsService.cs:546-559`
- [x] **Two different region-normalization schemes feed analytics cache keys** `LOW` · `small`
  - **Fix:** Standardize on one region representation for all analytics cache keys (either always the code or always the filter form) and document why the win-rate filter differs if it must.
  - **Why:** No collision or drift today because read and warm each use the same scheme consistently per key-type, but the split invites future bugs: a change to one normalizer, or a copy-paste of one key format into another, can silently produce keys that never match. Purely a clarity/robustness smell.
  - **Where:** `Transcendence.Service.Core/Services/Analytics/Implementations/ChampionAnalyticsService.cs:112,168,258,303,408`
- [ ] **No cross-process stampede coordination — post-invalidation both hosts can recompute the same key** `INFO` · `medium`
  - **Fix:** No action needed while compute stays cheap; if a payload's factory ever becomes expensive again, consider a short distributed lock or letting the warm job own (re)population without an overlapping read-through window.
  - **Why:** At most one duplicate compute per key per invalidation across the two hosts. Because reads now serve from precomputed aggregate tables (30–225ms per the analytics-layer memory) rather than the old 7s compute, the duplicate cost is small. Noted for completeness since the dimension calls out thundering-herd protection.
  - **Where:** `Transcendence.WebAPI/Program.cs:195-205`

### API Design, Contracts & OpenAPI

> The API is resource-oriented, uses purpose-built DTOs/records with no raw EF-entity leakage, and — importantly — is gated by a real CI drift check (`pnpm api:check`) so the committed OpenAPI spec cannot silently diverge from the code. The load-bearing weakness is fidelity, not structure: Swashbuckle is not configured for C# nullable-reference-type / required-property support, so the generated TypeScript client (whose `components` types the frontend imports everywhere) systematically misrepresents nullability in both directions and marks nothing required. Secondary issues are a genuinely inconsistent error model (RFC7807 ProblemDetails everywhere except admin endpoints, which return an undocumented `{message,detail}` shape), many untyped success bodies, a missing validation-error schema, and a shipped placeholder field in the profile contract.

- [x] **ProblemDetails content-type in the spec (`application/json`/`text/plain`) does not match runtime `application/problem+json`** `LOW` · `small`
  - **Fix:** Annotate error responses with the `application/problem+json` content type (via a ProducesResponseType/operation filter or [Produces]) so the declared and actual media types align.
  - **Why:** A strict client that content-negotiates or matches on media type will mis-handle error bodies; the contract misrepresents the RFC 7807 media type the server actually returns.
  - **Where:** `Transcendence.WebAPI/Errors/ProblemDetailsErrorBodyFilter.cs:36-40; openapi/transcendence.v1.json`
- [ ] **GET summoner-by-riot-id is dual-typed (200 profile vs 202 accepted), forcing status-based branching and bypassing the typed client** `LOW` · `medium`
  - **Fix:** Consider modeling absence explicitly (e.g. 404 for not-stored, 200 with a `status`/`stale` discriminator field, or a dedicated `?refresh` sub-resource) so the success path is a single typed 200 shape.
  - **Why:** A read endpoint that returns 202 with an alternate body is an unusual contract that most typed HTTP clients model awkwardly; here it pushed the frontend to hand-roll status handling instead of the generated client, eroding the client's value.
  - **Where:** `Transcendence.WebAPI/Controllers/SummonersController.cs:89-96,271-279; apps/web/app/lol/summoners/[region]/[riotId]/page.tsx:113,153`
- [x] **Three redundant analytics cache-invalidate endpoints** `LOW` · `trivial`
  - **Fix:** Consolidate to a single canonical endpoint (admin-scoped) and remove the duplicates, or clearly differentiate scope (all vs champion-only) if that distinction is real.
  - **Why:** Three public routes for one operation (with two different auth policies: AppOnly vs AdminOnly) bloat the contract and blur the intended entry point / authorization story for cache invalidation.
  - **Where:** `Transcendence.WebAPI/Controllers/AdminOperationsController.cs:305; AnalyticsController.cs:130; ChampionAnalyticsController.cs:250`
- [x] **201 Created Location for API-key creation points at the collection with a spurious `?id=` query** `LOW` · `trivial`
  - **Fix:** Add a GET /api/auth/keys/{id} and reference it, or return 200/Created without a misleading Location.
  - **Why:** The 201 Location header violates REST expectations (does not address the new resource); clients that follow Location land on the full list with an ignored query param.
  - **Where:** `Transcendence.WebAPI/Controllers/ApiKeysController.cs:40`
- [x] **PUT pro-summoner lacks the required-field validation the POST enforces** `LOW` · `small`
  - **Fix:** Apply the same required-field validation in Update (or add DataAnnotations to UpsertTrackedProSummonerRequest and rely on [ApiController] model validation for both).
  - **Why:** Inconsistent write contract for the same resource; a full-replacement PUT can degrade a record to a state the API refuses to create, and the difference is invisible in the contract.
  - **Where:** `Transcendence.WebAPI/Controllers/ProSummonersController.cs:159-171 vs 67-69`
- [x] **No API version segment in routes despite an OpenAPI 'v1' document** `INFO` · `medium`
  - **Fix:** If external consumers are ever expected, adopt URL or header versioning (e.g. Asp.Versioning) before the surface stabilizes; otherwise document that the contract is intentionally internal/unversioned.
  - **Why:** Any breaking change to a route or payload is a hard break with no negotiated coexistence path. Impact is bounded because the API is internal and consumed by a single lock-step-generated client, but there is no runway for external/mobile consumers or staged rollouts.
  - **Where:** `openapi/transcendence.v1.json (info.version "v1"); all controller [Route] attributes (e.g. SummonersController.cs:22 "api/lol/summoners")`

### Security & Authentication

> The auth boundary is, on the whole, carefully built: PBKDF2 password hashing with fixed-time compare and rehash-on-login, refresh-token reuse detection with family revocation, fully-validated JWTs, HttpOnly/SameSite/Secure cookies, a strict public-proxy allowlist with path-traversal rejection, uniform [Authorize(AdminOnly)] + audit logging on admin endpoints, no IDOR on user-scoped resources, and no real secrets committed. The most material problem is the rate-limiting boundary: the backend partitions auth (and read) limits on client IP restored from X-Forwarded-For, but the BFF's auth path (login/register/refresh via the generated client) never forwards the client IP, collapsing all auth traffic into one global partition — a trivial global login/refresh DoS and a defeated per-attacker control. Secondary issues are the BFF forwarding client-controlled X-Forwarded-For verbatim (rate-limit / internal-classification bypass) and a spoofable admin same-origin check. None are data-loss/RCE class; several are availability or defense-in-depth weaknesses.

- [x] **User/account enumeration via login timing and register status code** `LOW` · `small`
  - **Fix:** Compute a dummy PBKDF2 hash on the user-not-found path so login latency is constant regardless of account existence; consider returning a uniform response for register (always 200/accepted, notify by email) if enumeration resistance is desired.
  - **Why:** An attacker can enumerate which emails have accounts (timing on login, status code on register), aiding targeted phishing/credential-stuffing. Low impact given rate limits and no direct data exposure.
  - **Where:** `Transcendence.Service.Core/Services/Auth/Implementations/UserAuthService.cs:50-57; Transcendence.WebAPI/Controllers/AuthController.cs:20-35`
- [x] **Bootstrap API key compared with non-constant-time string equality** `LOW` · `trivial`
  - **Fix:** Compare the bootstrap key with CryptographicOperations.FixedTimeEquals over UTF-8 bytes (or hash-then-compare like normal keys), independent of environment.
  - **Why:** If an operator sets Auth:BootstrapApiKeyEnabledInDevelopmentOnly=false and configures a bootstrap key in production, the non-constant-time compare is a (weak, network-noisy) timing oracle toward recovering the app-tier bootstrap key. Low: requires a deliberate insecure config and the bootstrap principal is 'app' role, not admin.
  - **Where:** `Transcendence.Service.Core/Services/Auth/Implementations/ApiKeyService.cs:60-73`
- [x] **Account email (PII) written to application logs across auth flows** `LOW` · `trivial`
  - **Fix:** Log the user Guid instead of the email, or hash/redact the local-part, for these operational messages; keep raw email only in the audit trail where an actor identity is required.
  - **Why:** User email addresses accumulate in service logs and the admin log surface, expanding the PII footprint subject to log retention/exfiltration. No credentials or tokens are logged (verified). Low.
  - **Where:** `Transcendence.Service.Core/Services/Auth/Implementations/UserAuthService.cs:64,145,244; AdminBootstrapService.cs:49`

### Concurrency & Reliability

> The refresh-lock machinery and background-job concurrency are, on the whole, unusually well-engineered for this class of codebase: the DB lock is a genuinely atomic upsert (no acquire TOCTOU), lock ownership is handed off cleanly from producer to consumer with release-on-failure, all concurrent work isolates its EF DbContext behind per-task DI scopes, there is zero blocking-on-async and no async void, and the singleton state (rate-gate buckets, heartbeat, telemetry) uses correct primitives. The real risk is concentrated in the newest code: FullHistoryBackfillJob (PR #117) has no concurrency guard and no duplicate/transaction handling, so overlapping runs for one summoner crash on unique-index collisions and burn scarce Riot budget, and its non-transactional delete-then-insert recompute can leave season stats missing on a crash. The remaining findings are low-impact races/inconsistencies that largely self-heal.

- [ ] **WorkerWatchdog hard-kills the process with Environment.Exit, which can interrupt the non-transactional writes above** `LOW` · `small`
  - **Fix:** Keep the watchdog, but make the operations it can interrupt crash-safe (fix #2 with a transaction). Optionally have the watchdog request a bounded graceful stop before Environment.Exit, and dispose the CTS in a StopAsync/IDisposable.
  - **Why:** The watchdog is the concrete crash vector that turns finding #2 from a transient window into a lasting data gap. Low on its own; the coupling with the non-transactional recompute is the actual risk.
  - **Where:** `Transcendence.Service.Core/Services/Diagnostics/WorkerWatchdog.cs:91,42,102-106`
- [x] **SummonerMaintenanceJob releases the lock on enqueue failure using the possibly-cancelled request token** `LOW` · `trivial`
  - **Fix:** Route this release through the same dedicated-timeout-CTS helper used by ChampionAnalyticsIngestionJob/SummonerRefreshJob so lock release never depends on the caller's cancellation token.
  - **Why:** On a cancellation-triggered enqueue failure the summoner-refresh lock is held until its TTL expires (delaying re-processing of that one summoner). Self-heals via TTL; narrow path (Hangfire enqueue rarely throws).
  - **Where:** `Transcendence.Service.Core/Services/Jobs/SummonerMaintenanceJob.cs:387-391`
- [x] **AdaptiveThroughputBudgetPolicy singleton has a check-then-act race on its shared _modeStates dictionary** `LOW` · `trivial`
  - **Fix:** If you want to remove the latent hazard, replace the GetOrAdd + indexer write with a single _modeStates.AddOrUpdate whose update delegate computes the resolved mode, making the transition atomic.
  - **Why:** If it ever occurred, at most one throttling cycle would pick a slightly-wrong hysteresis mode, self-correcting the next tick. Recorded because it is a genuine shared-state check-then-act; impact is cosmetic pacing jitter.
  - **Where:** `Transcendence.Service.Core/Services/Jobs/Priority/AdaptiveThroughputBudgetPolicy.cs:10,92,122-123`
- [x] **RefreshLockLifecycleTelemetry writes gauge-dimension fields without a memory barrier while the metrics thread reads them** `INFO` · `trivial`
  - **Fix:** Mark the two string fields volatile (or fold class+region+counts into a single immutable record swapped via Volatile.Write/Interlocked.Exchange) so the gauge always reads a consistent snapshot.
  - **Why:** Cosmetic: a metrics data point can carry stale dimension labels for a moment. No functional effect.
  - **Where:** `Transcendence.Service.Core/Services/Diagnostics/RefreshLockLifecycleTelemetry.cs:47-48,195-196,216-228`

### Testing

> The backend suite is sizeable (207 xUnit facts/theories across ~44 files, plus 20 frontend vitest files) and contains several genuinely high-value tests: a rigorous raw-vs-precompute equivalence gate for analytics, incident-driven regression guards, and strong refresh-token-rotation coverage. However, the correctness of the two most safety-critical areas is essentially unverified: (1) all authentication crypto — hand-rolled PBKDF2 password hashing/verification, JWT signing, and the API-key auth handler — is entirely untested and merely mocked away; and (2) the entire data layer is validated only against SQLite (via EnsureCreated) and the EF InMemory provider, never a real PostgreSQL, so Postgres/Npgsql translation, migrations, and the authorization boundary are all unexercised. Assertion quality in the seeded service tests is good; a minority of controller tests are near-tautological forwarding checks.

- [ ] **Several controller tests are near-tautological forwarding checks (over-mocking)** `LOW` · `trivial`
  - **Fix:** Fold the pure pass-through cases into the behavior-rich tests, or replace them with tests that assert a real decision (parameter normalization, defaulting, error mapping). Not harmful, but low ROI.
  - **Why:** Low value-per-test: they pad the count and give a false sense of coverage without testing meaningful branching. (By contrast, the same file's most-played-role resolution and RejectsUnknownRole tests, lines 12-124, are genuinely valuable.)
  - **Where:** `tests/Transcendence.WebAPI.Tests/ChampionAnalyticsControllerTests.cs:126-182`
- [ ] **Analytics equivalence gate excludes ban-rate/contested/movement fields, leaving persisted-path denominators outside the correctness comparison** `INFO` · `small`
  - **Fix:** Add targeted assertions on expected absolute ban-rate/contested/movement values for the seeded dataset (independent of the raw-vs-stats comparison) so those persisted fields have direct coverage.
  - **Why:** A defect specific to the persisted role-independent ban-rate denominator or the movement/previous-tier computation would not be caught by the equivalence tests — those fields are trusted rather than verified here.
  - **Where:** `tests/Transcendence.Service.Core.Tests/ChampionAnalyticsStatsEquivalenceTests.cs:65-93`

### Frontend Architecture

> The App Router architecture is largely sound and, in places, genuinely sophisticated: a clean BFF trust-boundary design (no-credentials public allowlist proxy, per-namespace credential injection, cookie stripping in both directions), no secrets in the client bundle, a well-reasoned Next 16 `proxy.ts` middleware that refreshes-and-persists tokens before render, and correct Suspense streaming on the champion detail page. The main architectural weaknesses are concentrated in the summoner-profile surface, which abandons the server-streaming pattern used elsewhere and pushes nearly all data fetching (match history, rank history, and four full static-data maps) to the client as a post-hydration waterfall. Secondary issues are missing per-request deduplication of `getSessionMe` (blocking, un-suspended, called twice on admin routes) and a couple of pages/fetch paths that fetch on the client where the server already has the data.

- [x] **Favorites page is fully client-rendered when the initial list could be server-fetched** `LOW` · `medium`
  - **Fix:** Render the page as a server component that fetches the initial favorites list (via the session-aware backend call), and keep a small client island only for the interactive remove/add actions.
  - **Why:** Extra client round-trip and a loading flash on an authenticated route whose first paint could be data-complete; minor but inconsistent with the server-first pattern the analytics pages follow.
  - **Where:** `apps/web/app/account/favorites/page.tsx:20-58`
- [x] **Unique per-call x-trn-request-id header defeats Next fetch memoization; some hot fetches lack the compensating cache() wrapper** `LOW` · `small`
  - **Fix:** Either move the request-id header out of the memoization key path (e.g. attach it in the middleware/proxy layer rather than per fetchBackendJson call) or systematically wrap shared read fetchers (status included) in React `cache()`.
  - **Why:** Duplicated backend work where the cache() wrapper is missing (the data cache may absorb the second network hit, but that is not guaranteed and the pattern is fragile — every new call site must remember to add cache()).
  - **Where:** `apps/web/lib/backendCall.ts:44`
- [x] **user BFF proxy lacks the same-origin guard the admin proxy enforces** `INFO` · `trivial`
  - **Fix:** For consistency and belt-and-suspenders CSRF protection, apply the same `isSameOrigin` check to non-safe methods in the user proxy handler.
  - **Why:** Defense-in-depth parity gap rather than an open vulnerability; SameSite=Lax mitigates classic CSRF for the mutating user endpoints today.
  - **Where:** `apps/web/app/api/trn/user/[...path]/route.ts:13-41`

### Frontend Code Quality

> The frontend is, on balance, well-crafted: pure logic is factored into `lib/` with careful scale-normalization, accessibility is genuinely handled (aria-sort, aria-expanded, focus rings, reduced-motion gating), and fetch effects cancel correctly. The quality debt is concentrated in the summoner-profile subtree: `SummonerProfileClient` is a 510-line orchestration god-component (23 `useState`, 7 `useEffect`) that then prop-drills ~29 props into `MatchHistorySection`, and a static-data bundle is threaded five components deep. Secondary issues: unsafe casts of untrusted/undertyped payloads that mask OpenAPI-client type drift, and inline filters that only see the current 20-match page.

- [ ] **Inline queue/champion filters and their option lists only cover the current 20-match page** `MED` · `medium` · ✅ verified
  - **Fix:** Either move queue/champion filtering server-side (pass filter params into the recent-matches request) or make the intended scope explicit in the UI copy ("filtering this page"). Deriving filter options from a single page also makes the dropdown contents flicker as you page.
  - **Why:** A user who filters by e.g. ARAM can see "0 shown" even when ARAM games exist on later pages, and the queue dropdown lists only queues present in the current 20 matches. The filter reads as global but is page-local — a subtle correctness/UX trap.
  - **Where:** `apps/web/components/SummonerProfileUnified.tsx:106-179, 290-331`
- [ ] **SummonerProfileClient is a 510-line orchestration god-component (23 useState, 7 useEffect)** `LOW` · `large` · ☑︎ partly-verified
  - **Fix:** Extract cohesive hooks: useSummonerRefreshPolling, useMatchHistory(page), useRankHistory, useMatchDetails, useStaticData, and useSyncedProfileQuery (URL). The JSX component then composes hooks and shrinks to a layout shell.
  - **Why:** Very hard to test, reason about, or change in isolation; a bug in one concern (e.g. poll backoff) forces re-reading the whole file. High re-render surface — any of 23 state slices re-runs the whole component and its memo graph.
  - **Where:** `apps/web/components/SummonerProfileUnified.tsx:48-510 (state 76-104; effects 185-352)`
- [ ] **Heavy prop drilling: ~29 props into MatchHistorySection and a static-data bundle threaded 5 levels deep** `LOW` · `medium` · ✅ verified
  - **Fix:** Put the static-data bundle (champion/item/spell/rune maps + versions) behind a StaticDataContext provider mounted once in SummonerProfileClient; consumers read via a hook. Groups the remaining callbacks/state into a couple of cohesive objects.
  - **Why:** Signature churn: adding one static map touches every layer. Intermediate components (MatchHistorySection, MatchScoreboard) accept props they never read, only forward — pure passthrough noise that obscures which component actually consumes what.
  - **Where:** `apps/web/components/lol-profile/MatchHistorySection.tsx:48-78 (props type); apps/web/components/SummonerProfileUnified.tsx:465-504`
- [x] **Duplicated constants and formatting that belong in lib/ (regions, k-suffix, win-rate thresholds)** `LOW` · `small`
  - **Fix:** Move REGIONS to a lib/regions module, add a formatCompact()/thousands helper to lib/format, and derive matchupVerdict from a shared threshold constant alongside winRateColorClass.
  - **Why:** Drift risk: a new region or a threshold tweak must be applied in multiple files; the 52/48 verdict boundary and the color boundary can silently diverge.
  - **Where:** `apps/web/components/SearchBar.tsx:11-23 & GlobalCommandPalette.tsx:43-55; ScoreboardTeamTable.tsx:34-36 & PerformanceCard.tsx:34-38 (& matchInsights.ts:142,148); app/lol/champions/[championId]/page.tsx:52-57`
- [x] **Index-based React keys on dynamic lists** `LOW` · `trivial`
  - **Fix:** Key build variants by a stable identity (primaryStyleId+coreItems hash or a server-provided variant id) rather than array index.
  - **Why:** If build order ever changes across a re-render (e.g. re-sort or partial update), React reconciles by position, which can misassociate the uncontrolled `<details open>` state and animations. Low today because these lists are recomputed whole per navigation.
  - **Where:** `apps/web/app/lol/champions/[championId]/page.tsx:526-528; apps/web/app/lol/pro-builds/page.tsx:319,493`
- [x] **GlobalCommandPalette reads window layout during render and recomputes helpers every render** `LOW` · `small`
  - **Fix:** Compute the open-origin geometry once on open (in the open event handler / a ref) instead of every render; prefer deriving activeTierSection during render or clamping it inline over a syncing effect.
  - **Why:** Minor: reading layout during render is fragile (values captured once at open, stale on resize) and adds avoidable work per keystroke while the palette is open. The tier-list derived-state-in-effect causes an extra render pass when visibleTiers changes.
  - **Where:** `apps/web/components/GlobalCommandPalette.tsx:122-184, 476-487`

### Frontend Performance

> The frontend is thoughtfully built on server components + ISR with backend precompute, streaming on the champion detail page, paginated match history, and consistently CLS-safe images (explicit width/height, next/font swap). The dominant risk is the Tier List: the entire ~170-row champion ladder renders in a single "use client" component with no virtualization, and each row mounts heavy per-row subcomponents including a self-contained Radix Tooltip.Provider — inflating hydration, TBT, and memory. Secondary risks are a site-wide first-load JS penalty from eagerly bundling framer-motion + cmdk in the root layout, a fully-blocking multi-fetch waterfall on the pro-builds index (no streaming), an unoptimized full-resolution splash JPG on every champion page, and over-eager Link prefetch across the dense tier-list table.

- [x] **Per-row Radix Tooltip.Provider anti-pattern (one Provider mounted per tier-list row)** `LOW` · `small` · ✅ verified
  - **Fix:** Hoist a single `RadixTooltip.Provider` to the root layout (or the table root) and make the Tooltip wrapper render only Root/Trigger/Content. Correct the misleading comment.
  - **Why:** Hundreds of redundant Radix context Providers per tier-list render inflate the client component tree, hydration work, and memory, compounding the unvirtualized-list problem above. The pattern repeats anywhere Tooltip is used inside a list.
  - **Where:** `apps/web/components/ui/Tooltip.tsx:21-40 (used at TierListTable.tsx:365)`
- [x] **Hundreds of tiny immutable ddragon icons routed through the Next image optimizer with no format/TTL tuning** `LOW` · `small`
  - **Fix:** Consider `unoptimized` for the tiny fixed-size icons (or a lightweight custom loader hitting ddragon directly) and set `images.minimumCacheTTL` to a long value for the ones that stay optimized. Add explicit `formats` if AVIF/WebP is desired.
  - **Why:** Optimizing hundreds of ~24-64px immutable PNGs adds origin CPU + optimizer-cache churn for marginal byte savings (these files are already tiny), and without `minimumCacheTTL` the optimizer honors upstream cache headers only. This is a server-efficiency tradeoff more than a client CWV issue.
  - **Where:** `apps/web/next.config.mjs:4-32 ; apps/web/lib/staticData.ts:179-235`
- [x] **Stacked backdrop-blur sticky layers during tier-list scroll** `LOW` · `trivial`
  - **Fix:** Use a solid/translucent (non-blur) background for the sticky table heads, reserving backdrop-blur for the single floating banner, per the design doc's own 'blur reserved for genuinely floating layers' rule.
  - **Why:** Backdrop-blur is GPU-expensive to repaint during scroll; multiple stacked blur layers on a long scrolling ladder can cause jank on low-end devices. Minor relative to the unvirtualized-list cost.
  - **Where:** `apps/web/app/globals.css:536-540 ; apps/web/app/lol/tierlist/page.tsx:180-184`

### Infrastructure, CI/CD & Observability

> Infra hygiene is genuinely strong for a self-hosted single-host stack: all three images run non-root and multi-stage, GH Actions are SHA-pinned, images are cosign-signed and path-filtered per component, and there is a thoughtful liveness/readiness split plus a thread-pool-proof worker watchdog and OpenTelemetry+Grafana metrics. The material gaps are all in deploy safety and alerting: prod auto-migrates on worker startup yet no CI stage ever executes migrations against a real Postgres (tests are SQLite), so a runtime-failing migration crash-loops the worker while poll-deploy reports the deploy as a success; poll-deploy does no post-deploy health verification and its own pipeline failures are silent; there are no metrics-based alert rules (Prometheus/Grafana) so nothing alerts on API errors, DB/Redis down, or the worker being fully down; and several safety-critical docs (auto-migrate, wud) are stale/contradictory. None are data-loss or security-critical, but the migration/deploy-safety cluster is a real production risk.

- [ ] **Base images use floating tags (not digest-pinned) and SBOM/provenance attestations are disabled — inconsistent, weaker supply-chain than the rest of the pipeline** `LOW` · `small`
  - **Fix:** Pin base images by digest (renovate/dependabot can bump them), and re-evaluate whether provenance/sbom can be re-enabled now that wud is gone — poll-deploy resolves the moving :main digest via explicit Accept headers regardless.
  - **Why:** Builds are not byte-reproducible and a mutated/compromised upstream tag silently changes prod; disabling SBOM/provenance forfeits supply-chain attestation even though the pipeline otherwise pins Actions by SHA and cosign-signs images (rigor applied unevenly).
  - **Where:** `apps/web/Dockerfile:1,27; Transcendence.WebAPI/Dockerfile:1,7; Transcendence.Service/Dockerfile:1,5; .github/workflows/docker-images.yml:163-164`
- [x] **compose.yml (the documented 'safe deploy source') ships TRN_ERROR_VERBOSITY=verbose for the web service, leaking internal exception text to clients** `LOW` · `trivial`
  - **Fix:** Default the deploy compose to `safe` (verbose is a debugging opt-in) or scope verbose to non-prod, so a deploy from this file doesn't leak internals.
  - **Why:** Raw internal error messages (upstream host, connection/DNS detail, stack fragments) are exposed to end users in a compose file explicitly described (compose.yml:9-13) as a safe prod deploy source. Minor information disclosure and inconsistent with a production posture.
  - **Where:** `compose.yml:81; apps/web/lib/env.ts:15-21; apps/web/lib/trnProxy.ts:81-95,122-133`
- [x] **Grafana admin defaults to admin/admin when GRAFANA_ADMIN_PASSWORD is unset, with the port published** `LOW` · `trivial`
  - **Fix:** Drop the `:-admin` password default (fail closed / require the var) and add GRAFANA_ADMIN_PASSWORD to .env.example so it's an explicit, non-default secret.
  - **Why:** If the ops-tools profile is enabled without setting the password (and .env.example doesn't remind you to), the metrics UI — which exposes operational internals — is reachable with default credentials on the published port.
  - **Where:** `compose.yml:191-207`

### Documentation & Developer Experience

> Documentation quality is above average and the recent TFT removal was executed cleanly (zero stale TFT references anywhere in docs or the OpenAPI spec). Env-var/compose mappings, referenced files, and the API.md endpoint list are almost entirely accurate, and DEVELOPMENT.md/ARCHITECTURE.md contain genuinely excellent operational runbook content. The most serious problem is a canonical doc (ARCHITECTURE.md) that still describes the old, explicitly-retired prod deploy mechanism (wud) — an ops runbook that would actively misdirect during an incident. Secondary gaps: a fully committed Prometheus/Grafana observability stack is undocumented despite ~60 documented metric names, plus a scatter of smaller inaccuracies (endpoint count, one undocumented endpoint, a wrong host URL).

- [x] **Committed Prometheus/Grafana observability stack is entirely undocumented despite extensive metric docs** `MED` · `small` · ✅ verified
  - **Fix:** Add an 'Observability' subsection to DEVELOPMENT.md (and the README dev-tooling block) documenting the ops-tools Prometheus/Grafana stack, the 5 dashboards, ports (:9090/:3001), and admin creds; add PROMETHEUS_PORT/GRAFANA_PORT/GRAFANA_ADMIN_* to .env.example.
  - **Why:** The heavily documented metrics have no consumer story: a new operator cannot discover the dashboards exist, how to bring them up, or what URL/credentials to use. The telemetry documentation reads as dead-end reference with no path to actually view it.
  - **Where:** `compose.yml:175-211 & config/monitoring/grafana/dashboards/ (vs README.md:159-169, docs/DEVELOPMENT.md telemetry sections)`
- [x] **README misdescribes what the 'ops-tools' compose profile launches** `MED` · `trivial` · ✅ verified
  - **Fix:** Update the README ops-tools line to list all three services and their ports, or split observability into its own documented command.
  - **Why:** A developer running the documented command gets an unexpected Prometheus + Grafana boot (extra containers, ports 9090/3001) with no explanation, and conversely won't realize those tools are available. Factual inaccuracy in a command table.
  - **Where:** `README.md:166 (vs compose.yml:160,175,191 all profiles:['ops-tools'])`
- [x] **OpenAPI spec exposes a match-timeline endpoint that docs/API.md never lists** `LOW` · `trivial` · ✅ verified
  - **Fix:** Add the matches/{matchId}/timeline endpoint (auth + response shape) to the API.md summoner section.
  - **Why:** API.md declares itself a navigational summary with OpenAPI as source of truth but omits a live public endpoint, so a consumer relying on the human-readable list would miss it. Minor completeness gap, not a contract error.
  - **Where:** `docs/API.md:41-50 (vs openapi/transcendence.v1.json path /api/lol/summoners/{summonerId}/matches/{matchId}/timeline)`
- [x] **README overstates the API surface as '80+ endpoints'** `LOW` · `trivial`
  - **Fix:** Change to '60+ endpoints' (or drop the count) to match the committed spec.
  - **Why:** The headline number in the canonical README is ~27% higher than the actual committed contract, eroding trust in the doc's precision, at odds with the product's stated 'precise' brand.
  - **Where:** `README.md:55`
- [x] **AGENTS.md points frontend debugging at the wrong host (apex kronic.one, not transcend.kronic.one)** `LOW` · `trivial`
  - **Fix:** Fix AGENTS.md:93 to use https://transcend.kronic.one for consistency with the rest of the file.
  - **Why:** An agent following the debugging instructions verbatim opens the wrong (or a redirecting/unrelated) host, then screenshots/asserts against the wrong page.
  - **Where:** `AGENTS.md:93 (vs AGENTS.md:98,105 and README.md:20)`
- [x] **Inconsistent 'bring up the stack' command across canonical docs** `INFO` · `trivial`
  - **Fix:** Pick one canonical command (the pnpm script) and reference it uniformly, or explicitly note the foreground-vs-detached tradeoff once.
  - **Why:** Minor onboarding friction: a new dev sees two different 'correct' ways to start the stack and the foreground variant blocks the terminal.
  - **Where:** `AGENTS.md:24 & docs/DEVELOPMENT.md:20 (vs README.md:131 / package.json:28)`

### UX — Home, Navigation & Tier List

> The home/nav/search surface is genuinely high-craft and answer-first: a search-forward hero with a Cmd+K palette, a live Emerald+ "Top Picks" preview pinned to the same scope its links lead to, principled responsive column-drop on the dense tier list, and a coherent flat "command-deck" system (tier rails, diverging DataBars, Confidence chips, tabular figures). The most serious problems are accessibility regressions on the densest, most-used surfaces: the tier-list table's interactive elements have no visible keyboard-focus indicator even though native outlines are globally suppressed, and the flagship command palette lacks modal/combobox semantics (no dialog role, focus trap, focus return, or aria-activedescendant). Structurally, the "sticky Toolbar/head" design language is not actually realized — the filter toolbar is position:relative and the table's sticky column headers are silently broken by an overflow-x wrapper — so deep in a ~170-row list the user loses both column context and filter access, and below the xl breakpoint the TierSpine jump-nav disappears entirely. Copy and decoration are mostly restrained, except the command palette, which is over-chromed and off-brand relative to the rest of the system.

- [x] **Theme toggle flashes an empty icon and can announce the wrong label before hydration** `LOW` · `trivial`
  - **Fix:** Read the resolved theme synchronously from the `dark` class during render (or from the same source the FOUC head script uses) so the icon and aria-label are correct on first paint.
  - **Why:** A brief empty-icon flash on load, and assistive tech reading the toggle in the pre-hydration window may get an aria-label opposite to the actual theme. Cosmetic/minor, but on a control the design calls out as a signature.
  - **Where:** `apps/web/components/ui/ThemeToggle.tsx:18-49`
- [x] **Hero copy promises "matchup" search that the palette can't fulfill** `LOW` · `trivial`
  - **Fix:** Either add a matchup result/route (matchups live inside champion pages, so a "Champion X matchups" quick route would suffice) or drop "or matchup" from the headline to match what search actually returns.
  - **Why:** The single most prominent promise on the site names a capability the primary search does not surface, so a first-run user who takes the headline literally hits an empty result and mild distrust.
  - **Where:** `apps/web/app/page.tsx:112; apps/web/components/GlobalCommandPalette.tsx:667-834`

### UX — Champion & Pro-Builds Pages

> The champion detail page is genuinely strong: op.gg/u.gg-class density with far higher polish, a well-orchestrated progressive-disclosure Builds card (timing-aware sectioned breakdown + open "Recommended" and collapsed alternatives), diverging DataBars with real 95% CI whiskers, and a plain-English sample banner. The main problems are (1) a systemic keyboard-accessibility gap — the global reset strips focus outlines and the shared tab/pill/button controls on these exact pages add none back; (2) the "which build do I actually use?" answer is undermined by a "Recommended" build showing a lower win rate than a collapsed "Alternative," and by the Pro Builds detail page being a raw data dump rather than a synthesized build; and (3) several comprehensibility rough edges (jumbled win-rate-by-rank order, cryptic Confidence pips, redundant error stacking). Nothing here is data-loss/security; severity tops out at High for the focus-visible failure.

- [x] **Action-red accent used decoratively for navigation emphasis (violates one-accent-with-intent)** `LOW` · `trivial`
  - **Fix:** Make the two hero nav links visually equal (neutral), or justify red only on a genuine primary action. Restyle the 'More Ways to Explore' card as a neutral surface with standard secondary links; drop the `data-active` hack.
  - **Why:** CLAUDE.md principle 3: 'When the action red appears, it should mean act here … nothing decorative wears it.' Here red arbitrarily prioritizes Pro Builds over the equally-actionable Matchups link and paints a whole informational CTA card, diluting the 'act here' signal the rest of the system carefully reserves.
  - **Where:** `apps/web/app/lol/champions/[championId]/page.tsx:297-308; apps/web/app/lol/pro-builds/[championId]/page.tsx:489-510`
- [x] **Pro Builds region filter renders 11+ wrapping tabs while the champion page uses a compact Select** `LOW` · `trivial`
  - **Fix:** Use `variant="select"` for region on the pro-builds pages to match the champion page, or collapse to a Select below a breakpoint.
  - **Why:** Inconsistent IA for the identical control, and on a phone 11 tabs wrap to several rows of chrome above the data — heavy for a filter that is usually left at Global. It also makes the pro-builds Filters block visually noisier than the champion page.
  - **Where:** `apps/web/app/lol/pro-builds/[championId]/page.tsx:288 and app/lol/pro-builds/page.tsx:266 (default variant="tabs") vs apps/web/components/FilterBar.tsx:79 (variant="select")`
- [x] **Matchup table sort lacks a direction indicator and aria-sort; "Sort by Win Rate" quietly means worst-first, and DataBars vanish on mobile** `LOW` · `small`
  - **Fix:** Add a direction caret + `aria-sort` to the sort controls (reuse the accessible Table header pattern) and label the default order ('Toughest first'). Consider a minimal inline bar (or the whisker as a tiny mark) on mobile so the confidence signal survives.
  - **Why:** Screen-reader users get no sort state; sighted users get no direction cue and may misread 'Sort by Win Rate' as best-first. On mobile the 'core data language' (diverging bar) and the only per-row confidence signal (the CI whisker) are gone exactly where the audience is largest.
  - **Where:** `apps/web/components/MatchupsTable.tsx:38-44,74-80; apps/web/components/ui/DataBar.tsx:53-54`

### UX — Summoner Profile & Auth

> The profile and auth surfaces show high craft and a coherent "Ladder" system: colorblind-safe recent-form pips (fill+shape redundancy), small-n win-rate guards, reduced-motion-gated expand animations, deep-linkable URL state, and layout-matching skeletons. The two biggest UX defects are (1) the mobile DOM order buries the primary content — match history sits below five secondary sidebar cards — and (2) a failed/not-found profile renders a permanent loading skeleton beneath the error banner, which reads as "broken" rather than "not found". Auth is functional but incomplete (no password recovery, no reveal toggle), the Favorite control has no persistent/toggle state, and several ad-hoc low-opacity text tints drop below WCAG AA in the light theme.

- [x] **Empty match history always blames the filters, even when the player has zero matches** `LOW` · `trivial`
  - **Fix:** Distinguish the two cases: if queue===ALL and championFilter is empty, show a true empty state ("No ranked matches recorded yet" + Update Now hint); only mention filters when one is actually active.
  - **Why:** Misleading copy for genuinely empty profiles; a new player sees filter-troubleshooting text that doesn't apply and gets no guidance (e.g. "Update Now" to trigger ingestion).
  - **Where:** `apps/web/components/lol-profile/MatchHistorySection.tsx:461-465`
- [x] **Champion filter substring-matches numeric champion IDs, producing surprising results** `LOW` · `trivial`
  - **Fix:** Match names by substring but require an exact/leading numeric match for IDs (or drop ID matching and rely on the name datalist), so digit input behaves predictably.
  - **Why:** Minor: the datalist offers names so most users type names, but numeric input yields opaque, unexpected filtering with no explanation.
  - **Where:** `apps/web/components/SummonerProfileUnified.tsx:161-167`

---

## Appendix — Traceability

All 149 surviving findings from the audit are represented above (1 finding was refuted on verification and excluded). Coverage by area:

| Area | Items | Phases |
|---|---|---|
| Backend Architecture & Layering | 6 | P1, P3 |
| Backend Code Quality & Anti-Patterns | 8 | P3 |
| Data Model & Migrations | 7 | P1, P3 |
| Query Efficiency & EF Core Usage | 6 | P1, P3 |
| Ingestion & Background Jobs | 7 | P0, P3 |
| Analytics Correctness & Precompute | 6 | P1, P3 |
| Caching Layer | 6 | P1, P3 |
| API Design, Contracts & OpenAPI | 11 | P1, P3 |
| Security & Authentication | 6 | P0, P3 |
| Concurrency & Reliability | 6 | P0, P3 |
| Testing | 7 | P1, P3 |
| Frontend Architecture | 6 | P1, P3 |
| Frontend Code Quality | 6 | P3 |
| Frontend Performance | 8 | P1, P3 |
| Infrastructure, CI/CD & Observability | 8 | P0, P1, P3 |
| Documentation & Developer Experience | 7 | P1, P3 |
| Product Completeness & Opportunities | 12 | P2 |
| UX — Home, Navigation & Tier List | 8 | P1, P3 |
| UX — Champion & Pro-Builds Pages | 9 | P1, P3 |
| UX — Summoner Profile & Auth | 9 | P1, P3 |

_Source of truth: the audit PDF's per-area findings and adversarial verifications. Re-bucket items freely — the phase assignment is a starting sequence, not a contract._
