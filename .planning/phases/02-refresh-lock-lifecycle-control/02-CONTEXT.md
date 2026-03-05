# Phase 2: Refresh Lock Lifecycle Control - Context

**Gathered:** 2026-03-04
**Status:** Ready for planning

<domain>
## Phase Boundary

Make refresh lock ownership deterministic across entry points, bound refresh-lock table growth with explicit retention behavior, and expose operator-usable lock lifecycle telemetry.

This phase clarifies how lock lifecycle control behaves. It does not add new product capabilities beyond lock normalization, retention, and observability.

</domain>

<decisions>
## Implementation Decisions

### Lock identity normalization
- Use `platform + normalized riot id (gameName/tagLine)` as the canonical refresh lock identity model.
- Normalization is limited to trim + uppercase for `gameName` and `tagLine`; do not aggressively slugify or collapse punctuation/symbols.
- API, admin, and worker-triggered refresh paths share one canonical lock namespace for deterministic ownership.
- Keep a fixed default lock TTL policy as baseline behavior, with flow-specific overrides only when explicitly required.

### Contention behavior
- Use a uniform accepted/in-progress response style with wait hints wherever feasible when contention occurs.
- Repeated refresh attempts during an active lock are idempotent no-ops (no duplicate queueing).
- If main lock is acquired but API-priority lock is unavailable, proceed with main-lock-owned refresh and skip priority marker.
- Maintain poll-oriented visibility for callers so follow-up reads can convey in-progress state.

### Retention and cleanup policy
- Retention cleanup targets expired lock rows first; active leases are not reclaimed by default cleanup.
- Keep a short forensics window after expiry, then purge.
- Use frequent scheduled cleanup as the default retention cadence to keep growth bounded predictably.
- Cleanup failure is best-effort with strong operator alerting/telemetry; do not block core refresh processing by default.

### Lock lifecycle telemetry
- Baseline observability is structured logs plus metrics.
- Telemetry dimensions should include lock prefix/class, platform/region, and lifecycle outcome (acquire/release/cleanup success/failure/contention).
- Contention monitoring should use trend + threshold-style alerting, not per-event paging.
- Growth telemetry should track active vs expired counts plus cleanup deltas to expose backlog pressure and cleanup effectiveness.

### Claude's Discretion
- Exact metric names, logging field keys, and threshold defaults.
- Concrete cadence value and retention duration for "frequent cleanup" and "short forensics window."
- Final API payload shapes for harmonized contention wait hints where endpoint contracts differ today.

</decisions>

<code_context>
## Existing Code Insights

### Reusable Assets
- `Transcendence.Service.Core/Services/Jobs/RefreshLockKeys.cs`: existing canonical key builders and prefixes.
- `Transcendence.Data/Repositories/Implementations/RefreshLockRepository.cs`: lease-based acquire/release logic (atomic upsert and active-prefix checks).
- `Transcendence.WebAPI/Controllers/SummonersController.cs`: user refresh flow with contention wait hint handling.
- `Transcendence.WebAPI/Controllers/ProSummonersController.cs`: admin refresh flow currently using a simpler contention response.
- `Transcendence.Service.Core/Services/Jobs/SummonerRefreshJob.cs`: bounded lock release safety and priority-lock cooperation behavior.

### Established Patterns
- Lock ownership is lease-style (`LockedUntilUtc`) with unique key constraints and atomic conflict handling in persistence.
- Current lock keys are built from platform + normalized riot id in multiple entry points.
- Priority-demand gating pattern already exists via `AnyActiveByPrefixAsync(RefreshLockKeys.ApiPriorityRefreshPrefix, ...)`.
- Lock release hygiene follows bounded timeout and structured logging patterns.

### Integration Points
- Repository contracts and storage behavior: `Transcendence.Data/Repositories/Interfaces/IRefreshLockRepository.cs`, `Transcendence.Data/Models/Service/RefreshLock.cs`, `Transcendence.Data/TranscendenceContext.cs`.
- Refresh entry points: `Transcendence.WebAPI/Controllers/SummonersController.cs`, `Transcendence.WebAPI/Controllers/ProSummonersController.cs`.
- Background lock consumers: `SummonerRefreshJob`, `ChampionAnalyticsIngestionJob`, `SummonerMaintenanceJob`, `LiveGamePollingJob`, `MatchTimelineBackfillJob`, `RetryFailedMatchesJob`.
- Existing regression surface: `tests/Transcendence.WebAPI.Tests/SummonersControllerTests.cs`, `tests/Transcendence.Service.Core.Tests/SummonerRefreshJobTests.cs`.

</code_context>

<specifics>
## Specific Ideas

- Keep contention behavior operator-explicit and caller-explicit (clear in-progress signals over implicit silence).
- Prioritize deterministic ownership consistency across user/admin/background lock paths over source-specific lock partitions.
- Track lifecycle outcomes in a way that makes lock-table growth and contention trends explainable during patch spikes.

</specifics>

<deferred>
## Deferred Ideas

None - discussion stayed within phase scope.

</deferred>

---

*Phase: 02-refresh-lock-lifecycle-control*
*Context gathered: 2026-03-04*
