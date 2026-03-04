# Architecture Research: Early-Patch Relevance Under Riot Dev-Key Limits

## Milestone Intent
- Improve data relevance during early patch windows without architecture that must be undone after production Riot key approval.
- Increase ingestion effectiveness under constrained upstream throughput.
- Keep existing API/BFF contract boundaries stable unless change is strictly required for reliability signaling.

## Current Brownfield Baseline
- Runtime topology is already split into API host (`Transcendence.WebAPI`), worker host (`Transcendence.Service`), admin Hangfire host (`Transcendence.WebAdminPortal`), and web BFF (`apps/web`).
- Compile-time layering is host -> `Service.Core` -> `Data`; this should remain intact for this milestone.
- High-volume freshness logic currently concentrates in worker jobs and refresh lock semantics, not in API controller contract redesign.
- Existing concern map shows drift risk in worker scheduling duplication and partial-startup behavior.

## Recommended Component Boundaries
- Keep controllers thin and preserve `SummonersController` as the request-entry boundary for read + enqueue semantics.
- Concentrate milestone logic inside `Transcendence.Service.Core/Services/Jobs/*` and related orchestration services.
- Introduce a single shared scheduling policy component consumed by both `DevelopmentWorker` and `ProductionWorker`.
- Keep environment differences as configuration/toggle inputs, not separate duplicated scheduling code paths.
- Treat refresh lock lifecycle as a dedicated boundary: lock key construction, lease acquire/release, and retention cleanup should be explicit responsibilities.
- Keep analytics compute services separate from ingestion orchestration; ingestion decides what to compute, analytics services decide how to compute/cache.
- Keep BFF unchanged functionally; freshness behavior should be represented by existing backend statuses (`200` vs `202`) and metadata, not new proxy logic.

## Data-Flow Changes (Proposed)
- Prioritize data acquisition by patch-aware and value-aware queues so limited API capacity is spent on most relevant entities first.
- Split ingestion flow into explicit stages: candidate selection -> queue assignment -> Riot fetch -> persistence upsert -> cache invalidation -> completion metrics.
- Add backpressure signaling from ingestion jobs to lower-priority schedulers when API-priority lock pressure is high.
- Unify refresh enqueue semantics so API-triggered refresh and scheduled ingestion share lock/key rules and cancellation behavior.
- Add a retention/cleanup flow for `RefreshLock` rows to avoid unbounded cardinality growth from historical keys.
- Ensure cancellation tokens propagate from Hangfire execution context through Riot fetch and DB write paths where safe.
- Move startup scheduling to fail-fast for mandatory recurring jobs; optional jobs may remain best-effort with clear telemetry tags.
- Keep OpenAPI surface stable for this milestone by default; if additional freshness metadata is needed, prefer additive fields over endpoint redesign.

## Build-Order Implications
- Step 1: Establish shared recurring-job scheduling policy abstraction and wire both workers to it.
- Step 2: Introduce required-job registration contract (mandatory vs optional) and enforce fail-fast startup for mandatory set.
- Step 3: Refactor ingestion orchestration to explicit staged pipeline with queue priority policy hooks.
- Step 4: Standardize cancellation propagation across job entrypoints and Riot API service calls.
- Step 5: Implement refresh-lock retention policy and scheduled cleanup job.
- Step 6: Add observability points for queue depth, lock contention, freshness lag, and skipped/deferred ingestion units.
- Step 7: Validate no API/BFF contract break; only then consider additive response metadata if UAT proves necessary.

## Architectural Risks and Guards
- Risk: shared scheduler abstraction can accidentally erase intentional env differences.
- Guard: require per-environment options object with explicit defaults and tests for dev/prod divergence.
- Risk: fail-fast startup can reduce availability if mandatory-set scope is too broad.
- Guard: define a minimal mandatory core (patch detection, high-priority refresh, lock cleanup) and classify remaining jobs as optional.
- Risk: lock cleanup can remove still-relevant keys.
- Guard: cleanup should be lease-expiration + age-threshold based with conservative retention window and dry-run telemetry before hard delete.
- Risk: ingestion prioritization may starve long-tail champions/summoners.
- Guard: reserve a bounded low-priority budget slice each run to prevent starvation.

## Compatibility After Production Key Approval
- Priority policy should be capacity-parameterized, not key-type-parameterized.
- Queue topology and staged pipeline should scale by tuning worker counts/limits rather than redesigning component boundaries.
- Shared scheduler + lock retention + cancellation propagation remain valid under both development and production key regimes.

## Verification Focus for Architecture Fitness
- Worker startup either schedules mandatory jobs fully or fails process startup deterministically.
- High-priority refresh latency drops during early patch windows without severe starvation of low-priority backlog.
- Refresh lock table size trends remain bounded over time.
- API read path still provides graceful fallback when fresh data is pending, with no breaking contract change.
