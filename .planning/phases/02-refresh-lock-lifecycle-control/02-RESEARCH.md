# Phase 2 Refresh Lock Lifecycle Control Research

## Goal and Requirement Mapping

| Requirement | What must be true in implementation | Primary code seams |
|---|---|---|
| LOCK-01 | Refresh lock ownership is deterministic across API/admin/worker entry points using one canonical key model. | `RefreshLockKeys`, refresh controllers, ingestion/maintenance refresh enqueue paths |
| LOCK-02 | Expired/stale lock rows are cleaned by a retention policy so `RefreshLocks` growth is bounded. | `IRefreshLockRepository` + `RefreshLockRepository`, worker recurring job policy, EF model/migration |
| LOCK-03 | Lock lifecycle contention and growth are observable via structured telemetry with operator-usable dimensions. | lock repository + refresh entry points + new lifecycle job telemetry |

This phase is already constrained by `02-CONTEXT.md`; planning should implement those decisions directly, not reopen them.

## Locked Decisions from Context (Must Honor)

- Canonical refresh lock identity is `platform + normalized riot id (gameName/tagLine)`.
- Normalization is `Trim + ToUpperInvariant` for `gameName` and `tagLine` (no slugification/collapse).
- API, admin, and worker-triggered refresh paths share one lock namespace.
- Keep one default TTL policy baseline; only add explicit overrides where required.
- Contention behavior should stay idempotent, with wait hints and in-progress visibility.
- If main lock is acquired but API-priority lock is unavailable, continue with main-lock refresh.
- Retention cleans expired rows first, keeps a short post-expiry forensics window, and runs frequently.
- Cleanup is best-effort with strong telemetry; cleanup failures must not block refresh processing.
- Telemetry baseline is structured logs plus metrics with dimensions for lock class, platform/region, and outcome.

## Current-State Findings

1. Canonical key builders already exist and are used by primary refresh paths.
- `RefreshLockKeys` centralizes both key prefixes and `Trim + ToUpperInvariant` normalization (`Transcendence.Service.Core/Services/Jobs/RefreshLockKeys.cs:5`).
- `SummonersController` and `ProSummonersController` both use `BuildSummonerRefreshKey` and `BuildApiPriorityKey` (`Transcendence.WebAPI/Controllers/SummonersController.cs:248`, `Transcendence.WebAPI/Controllers/ProSummonersController.cs:206`).
- Worker enqueue paths for analytics/maintenance also use the same summoner refresh key builder (`Transcendence.Service.Core/Services/Jobs/ChampionAnalyticsIngestionJob.cs:153`, `Transcendence.Service.Core/Services/Jobs/SummonerMaintenanceJob.cs:116`).

2. Contention response behavior is not harmonized across entry points.
- User API path returns `SummonerAcceptedResponse` with wait hints (`retryAfterSeconds`) (`Transcendence.WebAPI/Controllers/SummonersController.cs:259`).
- Admin pro refresh path returns an anonymous `{ message }` object with no wait hint or poll target (`Transcendence.WebAPI/Controllers/ProSummonersController.cs:212`).

3. Lock storage is unbounded today.
- Repository supports acquire/release/get/prefix checks only; no retention delete/list/count APIs (`Transcendence.Data/Repositories/Interfaces/IRefreshLockRepository.cs:5`).
- Acquire is atomic upsert by key and release just shortens lease (`Transcendence.Data/Repositories/Implementations/RefreshLockRepository.cs:19`, `Transcendence.Data/Repositories/Implementations/RefreshLockRepository.cs:35`).
- `RefreshLock` model has only `Id`, `Key`, `CreatedAtUtc`, `LockedUntilUtc` and no lifecycle cleanup metadata (`Transcendence.Data/Models/Service/RefreshLock.cs:3`).
- EF config currently only indexes `Key` (unique), not `LockedUntilUtc` (`Transcendence.Data/TranscendenceContext.cs:132`).

4. Scheduling architecture is ready for a retention job, but no lock cleanup job exists.
- Worker recurring job policy is centralized and extensible (`Transcendence.Service.Core/Services/Jobs/Configuration/WorkerRecurringJobPolicy.cs:26`).
- `WorkerJobScheduleOptions` already carries cron/toggle patterns used by all recurring jobs (`Transcendence.Service.Core/Services/Jobs/Configuration/WorkerJobScheduleOptions.cs:3`).
- Worker service startup wires options and recurring policy in one place (`Transcendence.Service/Program.cs:57`).

5. Observability infrastructure exists for logs, but no dedicated lock lifecycle telemetry exists.
- Operational logger writes JSON log lines containing rendered message text and exception, but no separate metric pipeline is configured (`Transcendence.Service.Core/Services/Diagnostics/OperationalFileLogger.cs:131`).
- There is no current meter/OpenTelemetry registration in service/web startup paths (`Transcendence.Service/Program.cs:1`, `Transcendence.WebAPI/Program.cs:1`).

6. Test coverage exists for refresh lock release and API accepted responses, but not for retention lifecycle.
- Refresh job tests verify lock release/cancellation behavior (`tests/Transcendence.Service.Core.Tests/SummonerRefreshJobTests.cs:27`).
- API tests cover accepted response for missing summoner path (`tests/Transcendence.WebAPI.Tests/SummonersControllerTests.cs:77`).
- No tests currently cover lock retention cleanup, growth metrics, or pro refresh contention contract.

## Recommended Implementation Strategy for Planning

### Workstream A: Deterministic lock identity and contention contract (LOCK-01)

1. Keep `RefreshLockKeys` as the canonical namespace owner and forbid ad-hoc lock-key composition for refresh locks.
2. Add a small canonical identity helper in `RefreshLockKeys` (or adjacent type) that returns normalized `{ platform, gameName, tagLine }` once and reuse it for key construction and worker dedupe keys.
3. Replace worker candidate dedupe expressions with that same normalizer to avoid subtle drift from current per-job string interpolation (`ChampionAnalyticsIngestionJob` and `SummonerMaintenanceJob`).
4. Centralize TTL policy (default + explicit override points) instead of scattered literals (`15m` in API/admin and option-driven values in workers).
5. Harmonize contention acceptance payload shape where feasible:
- Keep `SummonerAcceptedResponse` for user path.
- Move admin pro refresh to a stable accepted schema with wait hints when lock is held.
- If admin schema changes, plan must include OpenAPI + docs updates in the same phase.

### Workstream B: Retention lifecycle and bounded growth (LOCK-02)

1. Extend lock repository contract for lifecycle operations.
- Add an expired-lock cleanup method with bounded batch semantics.
- Add snapshot/count method(s) for active vs expired rows used by telemetry.
2. Add DB support for efficient retention queries.
- Add index on `LockedUntilUtc` in EF model.
- Generate migration through EF CLI only (per repo migration policy).
3. Add a recurring lifecycle job.
- Create a new job (for example `RefreshLockLifecycleJob`) that runs frequent cleanup.
- Cleanup policy: delete only where `LockedUntilUtc <= now - forensicsWindow`; never reclaim active leases.
- Use bounded batch loop (`batchSize`, `maxBatchesPerRun`) to avoid long DB transactions.
- Keep cleanup best-effort: failures logged/telemetered and do not block refresh flows.
4. Wire job into worker scheduling and startup integrity policy.
- Add cron/toggle in `WorkerJobScheduleOptions` + appsettings.
- Add descriptor in `WorkerRecurringJobPolicy` and include in `KnownJobIds` cleanup path.
- Decide during planning whether lifecycle cleanup is mandatory baseline (recommended for LOCK-02 reliability).

### Workstream C: Lock lifecycle telemetry (LOCK-03)

1. Add explicit lock-lifecycle event telemetry at acquire/release/cleanup points.
- Outcomes: `acquire_success`, `acquire_contention`, `release_success`, `release_timeout`, `cleanup_success`, `cleanup_failure`.
- Dimensions: `lock_class/prefix`, `platform`, `outcome`.
2. Implement metrics with `System.Diagnostics.Metrics` meter in core/service code.
- Counters for event totals.
- Histogram for wait hint seconds / lease duration / cleanup batch duration.
- Gauges (or periodic measurements) for active and expired row counts.
3. Emit structured logs that mirror metric dimensions for immediate operator visibility via existing admin log tooling.
4. Add periodic summary log/metric from lifecycle job to expose growth trend and cleanup effectiveness (`active`, `expired`, `deleted`, `cleanupLagSeconds`).

## Recommended Plan Decomposition

1. Plan 02-01: Lock key normalization + response contract alignment.
- Canonical identity helper, TTL policy centralization, contention payload parity.
2. Plan 02-02: Retention cleanup implementation + schema/index migration.
- Repository lifecycle methods, recurring cleanup job, scheduling/policy wiring.
3. Plan 02-03: Telemetry and regression coverage.
- Metrics/log dimensions, trend snapshots, API/worker test additions, docs/OpenAPI parity.

## Risks and Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Retention job deletes rows too aggressively. | Lost forensic visibility, harder incident triage. | Enforce explicit `forensicsWindow`, start conservative, add cleanup metrics before tightening. |
| Retention query causes DB load spikes on large tables. | Worker slowdowns and lock contention. | Add `LockedUntilUtc` index, bounded batch cleanup, capped per-run batches. |
| Telemetry is added without usable dimensions. | Operators cannot trend contention/growth. | Standardize event schema and tags in one shared telemetry helper. |
| Contract drift between user/admin refresh responses. | Inconsistent operator/client behavior and docs mismatch. | Align DTOs and update OpenAPI + `docs/API.md` in same PR when shape changes. |
| TTL behavior diverges across entry points over time. | Ownership inconsistency and unpredictable wait hints. | Centralize TTL policy and reference it from all refresh enqueue sites. |

## Open Questions to Resolve During Planning

1. Should refresh-lock cleanup be a mandatory baseline recurring job in startup integrity policy, or optional-but-enabled by default?
2. Should admin pro refresh return the exact `SummonerAcceptedResponse` schema, or a parallel admin-specific accepted DTO with wait hints?
3. What is the initial default forensics window and cleanup cadence (for example 15-30 minutes window, every 5 minutes cleanup)?
4. Is built-in `System.Diagnostics.Metrics` + operational logs sufficient for current operator workflow, or should this phase also wire exporter plumbing (OTLP/Prometheus) now?

## Documentation and Contract Impact (Phase-Planning Checklist)

- If refresh accepted payloads change: update `openapi/transcendence.v1.json` and `docs/API.md`.
- If new lifecycle/retention settings are introduced: update `docs/DEVELOPMENT.md` and `README.md`.
- If lock lifecycle flow/scheduling/telemetry architecture changes: update `docs/ARCHITECTURE.md`.

## Validation Architecture

### Quick validation (targeted)

```bash
dotnet test tests/Transcendence.Service.Core.Tests/Transcendence.Service.Core.Tests.csproj --filter "FullyQualifiedName~SummonerRefreshJobTests|FullyQualifiedName~ChampionAnalyticsIngestionJobRampTests|FullyQualifiedName~RefreshLock"
```

```bash
dotnet test tests/Transcendence.WebAPI.Tests/Transcendence.WebAPI.Tests.csproj --filter "FullyQualifiedName~SummonersControllerTests|FullyQualifiedName~ProSummoners"
```

### Full validation

```bash
dotnet build Transcendence.sln -c Release
```

```bash
dotnet test Transcendence.sln -c Release
```

### Validation layers to plan for

1. Repository lifecycle layer.
- Acquire/release semantics unchanged.
- Cleanup deletes only expired-beyond-window rows.
- Active leases are never deleted.

2. API/admin contract layer.
- Contention responses include deterministic wait hints where expected.
- API/admin behavior remains idempotent under repeated refresh attempts.

3. Worker orchestration layer.
- Lifecycle cleanup recurring job is scheduled according to policy/profile.
- Cleanup failures do not break refresh processing.

4. Telemetry layer.
- Lock lifecycle events emit expected dimensions (`lock_class`, `platform`, `outcome`).
- Growth snapshots expose active/expired/deleted trends.

---

Research complete for planning Phase 2 scope (`LOCK-01`, `LOCK-02`, `LOCK-03`).
