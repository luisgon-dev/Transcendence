---
phase: 01-worker-startup-integrity
plan: "03"
subsystem: infra
tags: [hangfire, worker-startup, retry-policy, scheduling-policy, xunit]
requires:
  - phase: 01-01
    provides: shared recurring-job policy descriptors for both workers
  - phase: 01-02
    provides: cancellation-safe worker execution semantics used by startup/steady-state flows
provides:
  - "Startup integrity orchestration with bounded retries and mandatory recurring-job verification"
  - "Fail-fast worker startup gating when mandatory scheduling integrity is unresolved"
  - "Startup integrity and scheduling parity regression tests for WORK-01/WORK-02"
affects: [worker-startup-integrity, operational-readiness, worker-reliability]
tech-stack:
  added: []
  patterns: [startup-integrity-orchestration, mandatory-job-verification, fail-fast-startup-gate]
key-files:
  created:
    - Transcendence.Service/Workers/Startup/WorkerStartupIntegrityService.cs
    - Transcendence.Service/Workers/Startup/WorkerStartupIntegrityState.cs
    - tests/Transcendence.Service.Core.Tests/WorkerStartupIntegrityTests.cs
    - tests/Transcendence.Service.Core.Tests/WorkerSchedulingPolicyTests.cs
  modified:
    - Transcendence.Service.Core/Services/Jobs/Configuration/WorkerJobScheduleOptions.cs
    - Transcendence.Service/Workers/ProductionWorker.cs
    - Transcendence.Service/Workers/DevelopmentWorker.cs
    - Transcendence.Service/Program.cs
    - tests/Transcendence.Service.Core.Tests/Transcendence.Service.Core.Tests.csproj
    - scripts/openapi/export.sh
key-decisions:
  - "Mandatory startup verification is enforced by reading Hangfire recurring-job hashes and deserializing invocation payload metadata before startup is considered successful."
  - "Workers now fail startup explicitly on mandatory integrity failure after bounded retry, while optional failures produce degraded startup warnings."
  - "Startup integrity tests include worker-level thrown-exception semantics by referencing the service worker project from the core test suite."
patterns-established:
  - "Startup sequencing: cleanup -> integrity evaluation -> status log -> steady-state execution."
  - "Policy confidence: shared scheduling policy parity is enforced with profile-focused descriptor tests."
requirements-completed: [WORK-01, WORK-02]
duration: 11min
completed: 2026-03-04
---

# Phase 01 Plan 03: Worker Startup Integrity Orchestration Summary

**Worker startup now verifies mandatory recurring Hangfire jobs with bounded retries and fails fast when integrity cannot be established, with explicit degraded-mode handling for optional failures.**

## Performance

- **Duration:** 11 min
- **Started:** 2026-03-04T22:27:22Z
- **Completed:** 2026-03-04T22:37:57Z
- **Tasks:** 3
- **Files modified:** 10

## Accomplishments
- Added `WorkerStartupIntegrityService` and `WorkerStartupIntegrityState` to centralize recurring-job registration, mandatory verification, and startup outcome capture.
- Integrated startup integrity gating into both workers so unresolved mandatory failures throw and prevent healthy startup progression.
- Added regression tests for healthy/degraded/fail-fast startup integrity outcomes and mandatory baseline parity between scheduling profiles.

## Task Commits

Each task was committed atomically:

1. **Task 1: Build startup integrity orchestration with bounded retry and mandatory verification** - `bc0788e` (feat)
2. **Task 2: Integrate integrity gate into worker startup and host wiring** - `fd28fe2` (feat)
3. **Task 3: Add startup integrity and policy parity tests aligned with validation map** - `c36a3be` (test)

**Plan metadata:** pending final docs commit

## Files Created/Modified
- `Transcendence.Service/Workers/Startup/WorkerStartupIntegrityService.cs` - startup integrity orchestration with retry budget, mandatory verification, and structured outcomes.
- `Transcendence.Service/Workers/Startup/WorkerStartupIntegrityState.cs` - startup integrity status/result model for host-level consumption.
- `Transcendence.Service.Core/Services/Jobs/Configuration/WorkerJobScheduleOptions.cs` - startup integrity retry/backoff options.
- `Transcendence.Service/Workers/ProductionWorker.cs` - fail-fast startup gating and deterministic startup integrity summary logging.
- `Transcendence.Service/Workers/DevelopmentWorker.cs` - startup integrity gating with mandatory-failure throw semantics.
- `Transcendence.Service/Program.cs` - DI registration for startup integrity service/state.
- `tests/Transcendence.Service.Core.Tests/WorkerStartupIntegrityTests.cs` - mandatory success/degraded/fail-fast integrity and throw-on-fail startup tests.
- `tests/Transcendence.Service.Core.Tests/WorkerSchedulingPolicyTests.cs` - shared policy mandatory baseline parity assertions.
- `tests/Transcendence.Service.Core.Tests/Transcendence.Service.Core.Tests.csproj` - added reference to worker project for startup behavior tests.
- `scripts/openapi/export.sh` - serialized build for stable pre-commit OpenAPI generation in this workspace.

## Decisions Made
- Mandatory startup verification validates both Hangfire recurring hash metadata (`Cron`) and deserializable invocation payloads (`Job`) before healthy startup.
- Optional scheduling failures are retained as degraded signals, while mandatory failures are escalated to startup failure after configured retry budget exhaustion.
- Worker startup now emits deterministic integrity summaries (status, attempts, verified mandatory count, failure counts) prior to steady-state execution.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Stabilized OpenAPI pre-commit export build**
- **Found during:** Task 1 (task commit)
- **Issue:** Pre-commit `api:spec` frequently failed in this workspace because parallel `dotnet build` returned nondeterministic no-diagnostic failures.
- **Fix:** Updated `scripts/openapi/export.sh` to run `dotnet build -m:1` for serialized build execution.
- **Files modified:** `scripts/openapi/export.sh`
- **Verification:** Pre-commit OpenAPI export/build completed successfully during later task commits.
- **Committed in:** `bc0788e`

---

**Total deviations:** 1 auto-fixed (1 blocking)
**Impact on plan:** Deviation was required to keep task-level atomic commits executable in this environment; no feature scope change.

## Auth Gates

None.

## Issues Encountered
- `dotnet test` and API-export pre-commit hooks required local socket binding unavailable in sandbox mode; verification and commit hooks were executed with approved escalation.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness
- Startup integrity contracts for WORK-01 and WORK-02 are now enforced by code and regression tests.
- Phase 01 plan set is complete and ready for milestone/phase progression workflows.

---
*Phase: 01-worker-startup-integrity*
*Completed: 2026-03-04*

## Self-Check: PASSED

- Verified summary and required implementation/test files exist on disk.
- Verified task commits `bc0788e`, `fd28fe2`, and `c36a3be` exist in git history.
