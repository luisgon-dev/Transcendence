---
phase: 03-priority-ingestion-throughput
plan: 01
subsystem: api
tags: [ingestion, priority-scoring, hangfire, dotnet, options]
requires:
  - phase: 02-refresh-lock-lifecycle-control
    provides: normalized refresh-lock behavior and low-priority producer safety baselines
provides:
  - shared automatic-ingestion scoring contract and implementation
  - centralized ordering integration for champion analytics ingestion and summoner maintenance
  - configurable Jobs:IngestionPriorityPolicy defaults wired into service startup
  - policy regression coverage for ranking and deterministic tie-break behavior
affects: [03-02, 03-03, champion-analytics-ingestion, summoner-maintenance]
tech-stack:
  added: []
  patterns: [shared policy service for low-priority candidate ranking, options-driven weighted scoring]
key-files:
  created:
    - Transcendence.Service.Core/Services/Jobs/Configuration/IngestionPriorityPolicyOptions.cs
    - Transcendence.Service.Core/Services/Jobs/Priority/IIngestionPriorityScoringPolicy.cs
    - Transcendence.Service.Core/Services/Jobs/Priority/IngestionPriorityScoringPolicy.cs
    - tests/Transcendence.Service.Core.Tests/Jobs/PriorityScoringPolicyTests.cs
  modified:
    - Transcendence.Service.Core/Services/Jobs/ChampionAnalyticsIngestionJob.cs
    - Transcendence.Service.Core/Services/Jobs/SummonerMaintenanceJob.cs
    - Transcendence.Service.Core/Services/Extensions/ServiceCollectionExtensions.cs
    - Transcendence.Service/Program.cs
    - Transcendence.Service/appsettings.json
    - Transcendence.Service/appsettings.Development.json
    - tests/Transcendence.Service.Core.Tests/CancellationPropagationTests.cs
    - tests/Transcendence.Service.Core.Tests/ChampionAnalyticsIngestionJobRampTests.cs
key-decisions:
  - "Automatic ingestion ranking uses weighted patch relevance + staleness + favorite signal with options-driven saturation."
  - "Equivalent-score ordering is deterministic via canonical identity then UpdatedAt, with canonical dedupe after scoring."
  - "Both low-priority producers consume one shared scoring policy contract to prevent diverging heuristics."
patterns-established:
  - "Policy-first candidate ordering: producers build pools, shared service ranks and truncates."
  - "Constructor-level policy injection for low-priority job ordering dependencies."
requirements-completed: [INGT-02]
duration: 9min
completed: 2026-03-05
---

# Phase 3 Plan 01: Shared Automatic Ingestion Priority Scoring Summary

**Automatic-ingestion candidate ordering now uses one configurable patch-first scoring policy with deterministic ranking behavior across both low-priority producers.**

## Performance

- **Duration:** 9 min
- **Started:** 2026-03-05T18:54:45Z
- **Completed:** 2026-03-05T19:03:26Z
- **Tasks:** 3
- **Files modified:** 12

## Accomplishments
- Added `IngestionPriorityPolicyOptions`, `IIngestionPriorityScoringPolicy`, and `IngestionPriorityScoringPolicy` as shared automatic-ingestion ranking infrastructure.
- Replaced ad-hoc `UpdatedAt` ordering in `ChampionAnalyticsIngestionJob` and `SummonerMaintenanceJob` with policy-based ranking while keeping existing favorites + tracked-fallback pool collection.
- Added deterministic regression tests for patch-first ordering, stale boost, favorite bias, and tie-break behavior.

## Task Commits

Each task was committed atomically:

1. **Task 1: Add shared automatic-ingestion scoring contract and options** - `cc2ef90` (feat)
2. **Task 2: Wire scoring policy into service/options registration and defaults** - `54fb8f6` (feat)
3. **Task 3: Add policy-focused unit tests for ranking and tie-break determinism** - `a6c2d1c` (test)

**Plan metadata:** pending (created after state/roadmap updates)

## Files Created/Modified
- `Transcendence.Service.Core/Services/Jobs/Priority/IngestionPriorityScoringPolicy.cs` - shared weighted ranking logic with deterministic tie-break ordering.
- `Transcendence.Service.Core/Services/Jobs/ChampionAnalyticsIngestionJob.cs` - candidate ordering delegated to shared scoring policy.
- `Transcendence.Service.Core/Services/Jobs/SummonerMaintenanceJob.cs` - candidate ordering delegated to shared scoring policy.
- `Transcendence.Service/Program.cs` - options binding for `Jobs:IngestionPriorityPolicy`.
- `Transcendence.Service/appsettings.json` and `Transcendence.Service/appsettings.Development.json` - baseline scoring defaults.
- `tests/Transcendence.Service.Core.Tests/Jobs/PriorityScoringPolicyTests.cs` - ranking contract regression suite.

## Decisions Made
- Used a compact scoring surface (`PatchRelevanceWeight`, `StalenessWeight`, `FavoriteWeight`, `StalenessSaturationMinutes`) to keep behavior configurable without exposing many knobs.
- Implemented deterministic score ties as canonical identity first, then `UpdatedAt`, and applied canonical identity dedupe after ranking.
- Kept candidate-pool semantics intact (favorites + tracked fallback) and centralized only ranking responsibility.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Updated existing tests for new constructor dependency**
- **Found during:** Task 3 (policy-focused unit tests)
- **Issue:** Existing tests instantiating `ChampionAnalyticsIngestionJob` failed to compile after adding scoring policy dependency.
- **Fix:** Injected `IngestionPriorityScoringPolicy` in affected test harness constructors.
- **Files modified:** `tests/Transcendence.Service.Core.Tests/CancellationPropagationTests.cs`, `tests/Transcendence.Service.Core.Tests/ChampionAnalyticsIngestionJobRampTests.cs`
- **Verification:** `dotnet test tests/Transcendence.Service.Core.Tests/Transcendence.Service.Core.Tests.csproj -c Release --filter "FullyQualifiedName~PriorityScoringPolicy" -m:1`
- **Committed in:** `a6c2d1c` (part of task commit)

---

**Total deviations:** 1 auto-fixed (1 blocking)
**Impact on plan:** Required for build/test correctness after policy dependency injection. No architectural scope change.

## Issues Encountered
- Local pre-commit hook attempted OpenAPI regeneration and failed to fetch API spec from `127.0.0.1:5057`; commit flow still succeeded because no API contract files changed in this plan.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness
- Shared scoring infrastructure is ready for adaptive throughput integration in Plans 03-02/03-03.
- Both primary low-priority producers now consume one ranking contract, reducing future drift risk.

## Self-Check: PASSED

- FOUND: `.planning/phases/03-priority-ingestion-throughput/03-01-SUMMARY.md`
- FOUND: `cc2ef90`
- FOUND: `54fb8f6`
- FOUND: `a6c2d1c`

---
*Phase: 03-priority-ingestion-throughput*
*Completed: 2026-03-05*
