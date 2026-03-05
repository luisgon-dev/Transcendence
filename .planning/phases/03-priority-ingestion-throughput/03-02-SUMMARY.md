---
phase: 03-priority-ingestion-throughput
plan: 02
subsystem: api
tags: [ingestion, throughput, adaptive-budget, hangfire, dotnet]
requires:
  - phase: 03-priority-ingestion-throughput
    provides: shared INGT-02 candidate scoring policy and deterministic ranking contract
provides:
  - adaptive throughput budget policy using live + historical ingestion signals
  - producer integration for champion ingestion and maintenance queue targets
  - startup/config wiring for adaptive budget tuning
  - regression tests for budget mode transitions and producer truncation behavior
affects: [03-03, 03-04, champion-analytics-ingestion, summoner-maintenance]
tech-stack:
  added: []
  patterns: [shared adaptive budget policy with producer-specific hysteresis state, score-first candidate ordering before adaptive truncation]
key-files:
  created:
    - Transcendence.Service.Core/Services/Jobs/Configuration/AdaptiveThroughputBudgetOptions.cs
    - Transcendence.Service.Core/Services/Jobs/Priority/IAdaptiveThroughputBudgetPolicy.cs
    - Transcendence.Service.Core/Services/Jobs/Priority/AdaptiveThroughputBudgetPolicy.cs
    - tests/Transcendence.Service.Core.Tests/SummonerMaintenanceJobTests.cs
    - tests/Transcendence.Service.Core.Tests/Jobs/AdaptiveThroughputBudgetPolicyTests.cs
  modified:
    - Transcendence.Service.Core/Services/Jobs/ChampionAnalyticsIngestionJob.cs
    - Transcendence.Service.Core/Services/Jobs/SummonerMaintenanceJob.cs
    - Transcendence.Service/Program.cs
    - Transcendence.Service/appsettings.json
    - tests/Transcendence.Service.Core.Tests/ChampionAnalyticsIngestionJobRampTests.cs
    - tests/Transcendence.Service.Core.Tests/CancellationPropagationTests.cs
key-decisions:
  - "Adaptive mode selection is centralized in one policy that combines API-priority pressure, patch coverage, backlog age, and recent velocity."
  - "Low-priority producers consume policy output for both max-candidate selection and queue-target truncation while retaining shared INGT-02 ranking order."
  - "Mode hysteresis and cooldown are persisted per producer key to avoid oscillating between high-pressure, balanced, and catch-up decisions."
patterns-established:
  - "Budget-first producer flow: compute signals -> request adaptive decision -> rank candidates -> truncate by adaptive queue target."
  - "Policy transition testing validates cooldown and pressure re-entry semantics independent of job infrastructure."
requirements-completed: [INGT-01, INGT-02, INGT-03]
duration: 12min
completed: 2026-03-05
---

# Phase 3 Plan 02: Adaptive Queue-Tier Throughput Budgeting Summary

**Champion ingestion and maintenance now share an adaptive throughput budget mode (`high_pressure`/`balanced`/`catch_up`) driven by live API-priority pressure and historical patch-ingestion signals before queueing low-tier work.**

## Performance

- **Duration:** 12 min
- **Started:** 2026-03-05T19:07:31Z
- **Completed:** 2026-03-05T19:19:39Z
- **Tasks:** 3
- **Files modified:** 11

## Accomplishments
- Added `AdaptiveThroughputBudgetPolicy` and options/contracts to compute per-run producer budgets using API-priority demand, patch coverage progress, backlog age, velocity, and candidate pressure.
- Integrated adaptive budget decisions into `ChampionAnalyticsIngestionJob` and `SummonerMaintenanceJob` so queue targets and candidate caps are dynamic while shared scoring order remains the pre-truncation contract.
- Added startup/config wiring and regression tests for mode transitions, pressure re-entry, and producer queue truncation behavior.

## Task Commits

Each task was committed atomically:

1. **Task 1: Build adaptive throughput budget policy with live and historical signals** - `155fc42` (feat)
2. **Task 2: Integrate adaptive budgets and INGT-02 scoring order into low-priority producer queue decisions** - `99555dc` (feat)
3. **Task 3: Add config wiring and policy transition tests** - `21390ea` (test)

**Plan metadata:** pending (created after state/roadmap updates)

## Files Created/Modified
- `Transcendence.Service.Core/Services/Jobs/Priority/AdaptiveThroughputBudgetPolicy.cs` - mode resolver with cooldown/hysteresis and bounded queue/candidate outputs.
- `Transcendence.Service.Core/Services/Jobs/ChampionAnalyticsIngestionJob.cs` - adaptive signal collection plus budget-driven queue targets.
- `Transcendence.Service.Core/Services/Jobs/SummonerMaintenanceJob.cs` - shared adaptive budget integration for maintenance queueing.
- `Transcendence.Service/Program.cs` and `Transcendence.Service/appsettings.json` - adaptive options binding and default tunables.
- `tests/Transcendence.Service.Core.Tests/Jobs/AdaptiveThroughputBudgetPolicyTests.cs` - transition and boundary-condition regression coverage.
- `tests/Transcendence.Service.Core.Tests/SummonerMaintenanceJobTests.cs` and `tests/Transcendence.Service.Core.Tests/ChampionAnalyticsIngestionJobRampTests.cs` - producer ordering/truncation integration checks.

## Decisions Made
- Kept all adaptive budgeting behavior centralized behind `IAdaptiveThroughputBudgetPolicy` so producers do not duplicate threshold logic.
- Preserved INGT-02 ordering by running shared score ranking before adaptive queue-target truncation in both low-priority producers.
- Used per-producer mode state with cooldown guards to avoid rapid mode oscillation during pressure drops/re-entry.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Updated existing constructor-based tests for new adaptive policy dependency**
- **Found during:** Task 2
- **Issue:** Existing `ChampionAnalyticsIngestionJob` test construction paths failed after adding `IAdaptiveThroughputBudgetPolicy` dependency.
- **Fix:** Updated test harness construction to provide adaptive policy implementations.
- **Files modified:** `tests/Transcendence.Service.Core.Tests/CancellationPropagationTests.cs`, `tests/Transcendence.Service.Core.Tests/ChampionAnalyticsIngestionJobRampTests.cs`
- **Verification:** `dotnet test tests/Transcendence.Service.Core.Tests/Transcendence.Service.Core.Tests.csproj -c Release --filter "FullyQualifiedName~ChampionAnalyticsIngestionJobRampTests|FullyQualifiedName~SummonerMaintenanceJobTests" -m:1`
- **Committed in:** `99555dc` (part of task commit)

---

**Total deviations:** 1 auto-fixed (1 blocking)
**Impact on plan:** Required for compile/test continuity after policy integration; no architectural scope change.

## Issues Encountered
- Pre-commit OpenAPI export hook attempted local spec fetch from `127.0.0.1:5057` and failed, but commit flow completed with no API-surface changes staged.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness
- Adaptive budget infrastructure is in place for starvation guardrail work in 03-03.
- Producer logs now expose mode and signal snapshots to support operational tuning of catch-up thresholds.

## Self-Check: PASSED

- FOUND: `.planning/phases/03-priority-ingestion-throughput/03-02-SUMMARY.md`
- FOUND: `155fc42`
- FOUND: `99555dc`
- FOUND: `21390ea`

---
*Phase: 03-priority-ingestion-throughput*
*Completed: 2026-03-05*
