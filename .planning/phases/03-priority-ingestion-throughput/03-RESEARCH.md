# Phase 3 Priority Ingestion Throughput Research

## Scope and Inputs

- Phase context reviewed: `.planning/phases/03-priority-ingestion-throughput/03-CONTEXT.md`
- Project requirements reviewed: `.planning/REQUIREMENTS.md`
- Project state/history reviewed: `.planning/STATE.md`
- Prior phase context/research reviewed: Phase 1 and Phase 2 context/research artifacts
- Project-specific instructions check:
  - `CLAUDE.md`: not present in repository root
  - `.claude/skills/`: not present
  - `.agents/skills/`: not present

This research answers: what must be known to plan Phase 3 (`INGT-01..INGT-04`) well, without reopening decisions already locked in `03-CONTEXT.md`.

## Goal and Requirement Mapping

| Requirement | What must be true in implementation | Primary code seams |
|---|---|---|
| INGT-01 | User-triggered refresh work stays ahead of non-interactive ingestion during spikes. | `SummonersController`, `ProSummonersController`, `SummonerRefreshJob`, queue configuration in `Transcendence.Service/Program.cs` |
| INGT-02 | Candidate ordering is configurable and patch-relevant/high-value datasets are ingested first. | `ChampionAnalyticsIngestionJob`, `SummonerMaintenanceJob`, options under `Jobs:ChampionAnalyticsIngestion` and `Jobs:SummonerMaintenance` |
| INGT-03 | Queue-tier budgets adapt under load so high-priority freshness remains responsive. | Hangfire queue config + low-priority producers (`ChampionAnalyticsIngestionJob`, `SummonerMaintenanceJob`) + lock demand signals |
| INGT-04 | Low-priority ingestion cannot be deferred forever; guardrails enforce forward progress. | stale-candidate selection in ingestion/maintenance jobs + new defer-age guardrail + telemetry |

## Locked Decisions from Context (Must Honor)

From `03-CONTEXT.md`, these are fixed for planning:

- Candidate ranking policy is patch-first freshness.
- Candidate pool keeps existing behavior: favorites first, tracked fallback.
- Manual/user refresh stays all-match-types; ranked-only focus applies to automatic ingestion policy.
- Automatic ingestion is ranked-only during ramp and switches to mixed/all-modes once coverage target is reached.
- Throughput budgets are adaptive/auto-budgeted, not static-only knobs.
- During active high-priority demand, high-priority may consume all capacity.
- After pressure drops, low-priority catch-up bursts should run aggressively.
- Adaptive decisions must use both live and historical signals.
- Starvation guardrail is max defer age; breaches trigger forced catch-up windows.
- Guardrail scope for this phase is champion analytics ingestion + summoner maintenance.
- Guardrail behavior must emit structured logs and metrics.

Also explicitly delegated to implementation/planning (no user re-questioning needed):

- Exact scoring formula and tie-breakers.
- Numeric thresholds (defer age, catch-up window size, shift boundaries).
- Metric names/dimensions for budget shifts and guardrail activation.
- Historical signal storage/windowing shape.

## Current-State Findings

### 1) Priority path already exists, but responsiveness is not budgeted end-to-end

- User refresh endpoints acquire normal + API-priority lock and enqueue `RefreshByRiotId` on `refresh-high`:
  - `Transcendence.WebAPI/Controllers/SummonersController.cs`
  - `Transcendence.WebAPI/Controllers/ProSummonersController.cs`
- Worker queue ordering is already `refresh-high`, `default`, `refresh-low`:
  - `Transcendence.Service/Program.cs`
- Low-priority refresh path preempts itself when API-priority demand appears:
  - `SummonerRefreshJob.RefreshForAnalytics(...)`
- Low-priority producers also pause when API-priority demand is active:
  - `ChampionAnalyticsIngestionJob`
  - `SummonerMaintenanceJob`
  - `MatchTimelineBackfillJob`
  - `RetryFailedMatchesJob`
  - `LiveGamePollingJob`

Planning implication:

- INGT-01 is partially satisfied by existing architecture.
- The gap is adaptive throughput budgeting behavior and explicit fairness guardrails, not basic priority plumbing.

### 2) Candidate prioritization is currently heuristic, not configurable scoring

- `ChampionAnalyticsIngestionJob` candidate selection:
  - optional favorites join
  - optional tracked fallback
  - ordered by `UpdatedAt` ascending
  - canonical identity dedupe
- `SummonerMaintenanceJob` candidate selection:
  - stale cutoff + optional favorites + tracked fallback
  - ordered by `UpdatedAt` ascending

Planning implication:

- INGT-02 gap is real: there is no explicit priority score model with configurable weights.
- Existing behavior and options are a good base to extend (do not replace pipeline shape).

### 3) Throughput control exists as static per-run limits, not adaptive queue-tier budgets

- Existing controls are static knobs (`Min/MaxRefreshJobsToQueuePerRun`, ramp variants, page counts).
- Queue-tier budgets are implicit (queue order) and producer pausing, but no adaptive budget allocator combines live + historical signals.

Planning implication:

- INGT-03 needs a budget decision component that producers consult each run.
- Plan should avoid hardcoding new fixed knobs as the primary policy (conflicts with context decision).

### 4) Starvation is possible today under sustained API-priority pressure

- Low producers can fully skip while API priority is active.
- There is no explicit max defer-age threshold or forced catch-up state machine.

Planning implication:

- INGT-04 requires explicit fairness policy state, not just best-effort retries.

### 5) Reusable telemetry pattern already exists and should be reused

- `RefreshLockLifecycleTelemetry` already provides a non-blocking metrics/logging pattern with stable dimensions (`lock_class`, `platform_region`, `outcome`, `source`) and `System.Diagnostics.Metrics` usage.

Planning implication:

- Implement phase telemetry using the same non-blocking pattern and dimension conventions.
- Do not introduce a separate telemetry style for this phase.

### 6) Test baseline is strong for refresh locking and ramp behavior, weaker for throughput/fairness policy

- Existing coverage includes:
  - `SummonerRefreshJobTests`
  - `ChampionAnalyticsIngestionJobRampTests`
  - refresh lock lifecycle tests
- Missing coverage for:
  - adaptive budget decisions
  - starve guardrail activation
  - forced catch-up window behavior
  - cross-job fairness progression over time

Planning implication:

- Test work must be first-class in plan breakdown, not tacked on at the end.

## Recommended Implementation Strategy for Planning

### Workstream A: Configurable priority scoring (INGT-02)

1. Add a policy/options surface dedicated to automatic ingestion scoring (new options object under `Jobs`).
2. Keep current candidate pool creation (favorites + tracked fallback) but replace final ordering with computed score.
3. Score should combine (weights configurable):
   - patch relevance (current-patch deficit / freshness)
   - candidate staleness age
   - user-value signal (favorite)
   - tie-break by oldest `UpdatedAt` for deterministic behavior
4. Keep manual refresh path unchanged (`RefreshByRiotId` stays all-modes).

Suggested implementation seam:

- New internal policy service consumed by both `ChampionAnalyticsIngestionJob` and `SummonerMaintenanceJob` to avoid duplicated scoring logic.

### Workstream B: Adaptive throughput budget policy (INGT-01, INGT-03)

1. Introduce a budget decision service that outputs per-run budgets for low-priority producers.
2. Inputs (live signals):
   - active API-priority lock presence
   - high/low queue pressure indicators (Hangfire monitoring API)
   - current low-tier backlog age snapshot
3. Inputs (historical signals):
   - recent ingestion velocity (e.g., successful current-patch matches over lookback windows)
   - patch coverage progress vs target thresholds
4. Outputs:
   - budget mode (`high_pressure`, `balanced`, `catch_up`)
   - producer queue target caps for this run
   - whether low-tier all-modes work is allowed this run
5. Preserve behavior that high-priority can consume all capacity during spikes.

Planning note:

- Prefer adaptive producer budgets first; avoid planning a queue-infrastructure rewrite.

### Workstream C: Starvation guardrails with forced catch-up windows (INGT-04)

1. Define max defer age threshold for low-priority eligible candidates.
2. When threshold breached, trigger catch-up window state with elevated low-priority budget.
3. Scope guardrail to champion analytics ingestion + summoner maintenance only (per context decision).
4. Emit telemetry/logs for:
   - threshold breach
   - window start/end
   - catch-up outcome (progress/no progress)

Planning note:

- Guardrail should be explicit state, not an implicit side effect of one-off logs.

### Workstream D: Observability and operational controls

1. Add a dedicated throughput telemetry component (same style as lock lifecycle telemetry).
2. Standardize dimensions for policy outcomes (e.g., queue_tier, budget_mode, outcome, source).
3. Add structured summaries in each producer run with selected signal values and applied budgets.
4. Add minimal config surface in `appsettings*.json` for thresholds and lookback windows.

## Recommended Plan Decomposition

1. Plan 03-01: Shared priority scoring + candidate ordering integration.
- Deliver scoring policy abstraction and integrate into ingestion + maintenance jobs.
- Keep existing pool behavior and canonical dedupe.

2. Plan 03-02: Adaptive budget engine + producer integration.
- Implement live/historical signal aggregation.
- Integrate per-run budgets into queue target decisions in both producers.

3. Plan 03-03: Starvation guardrails and catch-up window state.
- Implement max defer-age detection and forced catch-up windows.
- Ensure forward progress behavior is observable and deterministic.

4. Plan 03-04: Telemetry, tests, and docs parity.
- Add policy/guardrail metrics and structured logs.
- Add regression tests for budget mode transitions and fairness behavior.
- Update docs if config surface changes.

## Key Risks and Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Scoring query adds heavy DB load. | Throughput drops during spikes. | Score only bounded candidate pool, keep query set small, add/verify indexes only if needed. |
| Adaptive policy oscillates rapidly. | Unstable throughput and noisy behavior. | Add hysteresis/cooldown between mode transitions and deterministic tie-breaks. |
| Guardrail overrides too aggressive. | User-triggered freshness can regress. | Keep high-priority-first invariant and cap catch-up window intensity. |
| Insufficient historical signal quality. | Bad budget choices. | Start with simple robust signals (coverage + recent velocity + defer age), expand later. |
| Tests only cover happy paths. | Regressions in spike scenarios. | Add mode-transition and prolonged-pressure tests with deterministic fakes/time control. |

## Planning Checklist (Non-Negotiable)

- Do not alter manual refresh semantics (`RefreshByRiotId` all-modes behavior remains).
- Do not remove existing favorites-first + tracked-fallback candidate pool shape.
- Ensure automatic ingestion remains ranked-focused until patch coverage threshold is met.
- Ensure starvation guardrail applies to both ingestion and maintenance producers.
- Ensure telemetry is non-blocking and follows existing dimensions/logging conventions.
- Ensure plan includes concrete tests for INGT-01..INGT-04 acceptance signals.

## Documentation Impact Checklist

Likely docs impact if phase introduces/changes options and operational behavior:

- `docs/DEVELOPMENT.md` and/or `README.md` for new `Jobs:*` tuning settings.
- `docs/ARCHITECTURE.md` for adaptive budget + guardrail flow and telemetry model.
- `docs/API.md` and OpenAPI are likely unchanged unless any API contract changes are introduced (not expected for this phase).

## Validation Architecture

### Priority validations

1. Policy unit tests.
- scoring ranking correctness
- budget mode decisions from live/historical signal combinations
- guardrail trigger and catch-up window transitions

2. Job integration tests (`Service.Core.Tests`).
- `ChampionAnalyticsIngestionJob` and `SummonerMaintenanceJob` consume policy outputs correctly
- high-priority contention preempts low work as expected
- low-priority receives progress after defer-age breach

3. Queue responsiveness tests (targeted).
- under simulated pressure, high-priority refresh is not blocked by low-tier queue growth

4. Telemetry assertions.
- budget mode/guardrail logs and metrics emit expected dimensions and outcomes

### Suggested command set

```bash
dotnet test tests/Transcendence.Service.Core.Tests/Transcendence.Service.Core.Tests.csproj --filter "FullyQualifiedName~ChampionAnalyticsIngestion|FullyQualifiedName~SummonerMaintenance|FullyQualifiedName~SummonerRefresh"
```

```bash
dotnet test tests/Transcendence.WebAPI.Tests/Transcendence.WebAPI.Tests.csproj --filter "FullyQualifiedName~SummonersController|FullyQualifiedName~ProSummonersController"
```

```bash
dotnet build Transcendence.sln -c Release -m:1
```

```bash
dotnet test Transcendence.sln -c Release
```

---

Research complete for planning Phase 3 (`INGT-01`, `INGT-02`, `INGT-03`, `INGT-04`).
