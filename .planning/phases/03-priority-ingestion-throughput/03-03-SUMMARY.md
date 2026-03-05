---
phase: 03-priority-ingestion-throughput
plan: "03"
subsystem: api
tags: [hangfire, ingestion, throughput, fairness, guardrail]
requires:
  - phase: 03-01
    provides: priority-scored low-priority candidate ordering
  - phase: 03-02
    provides: adaptive throughput mode and queue budget outputs
provides:
  - Starvation guardrail policy with defer-age breach detection and forced catch-up windows
  - Shared catch-up/cooldown lock-key contract for low-priority producer coordination
  - Guardrail-aware queue target enforcement in champion ingestion and summoner maintenance
affects: [03-04, operations, ingestion-observability]
tech-stack:
  added: []
  patterns: [lock-backed catch-up windows, shared fairness policy contract]
key-files:
  created:
    - Transcendence.Service.Core/Services/Jobs/Configuration/StarvationGuardrailOptions.cs
    - Transcendence.Service.Core/Services/Jobs/Priority/IStarvationGuardrailPolicy.cs
    - Transcendence.Service.Core/Services/Jobs/Priority/StarvationGuardrailPolicy.cs
    - tests/Transcendence.Service.Core.Tests/Jobs/StarvationGuardrailPolicyTests.cs
  modified:
    - Transcendence.Service.Core/Services/Jobs/RefreshLockKeys.cs
    - Transcendence.Service.Core/Services/Jobs/ChampionAnalyticsIngestionJob.cs
    - Transcendence.Service.Core/Services/Jobs/SummonerMaintenanceJob.cs
    - Transcendence.Service/Program.cs
    - Transcendence.Service/appsettings.json
    - tests/Transcendence.Service.Core.Tests/ChampionAnalyticsIngestionJobRampTests.cs
    - tests/Transcendence.Service.Core.Tests/SummonerMaintenanceJobTests.cs
    - tests/Transcendence.Service.Core.Tests/CancellationPropagationTests.cs
key-decisions:
  - "Catch-up cooldown is lock-backed by acquiring a cooldown key for window+cooldown TTL at activation time."
  - "Forced catch-up is a queue-floor override layered on adaptive budget output, not a replacement policy."
  - "Both low-priority producers use the same guardrail contract and producer-scoped lock keys."
patterns-established:
  - "Producer fairness decisions use policy + lock state + defer-age input, then feed final queue target/max-candidates."
  - "High-priority pause checks remain default behavior but are bypassed only when forced catch-up is active."
requirements-completed: [INGT-03, INGT-04]
duration: 12min
completed: 2026-03-05
---

# Phase 03 Plan 03: Starvation Guardrails Summary

**Low-priority ingestion fairness now enforces defer-age guardrails with forced catch-up windows while preserving adaptive high-priority-first behavior.**

## Performance

- **Duration:** 12 min
- **Started:** 2026-03-05T19:22:27Z
- **Completed:** 2026-03-05T19:34:35Z
- **Tasks:** 3
- **Files modified:** 12

## Accomplishments
- Added a configurable starvation guardrail policy that decides start/continue/cooldown outcomes deterministically.
- Integrated guardrail outcomes into both champion analytics ingestion and summoner maintenance queue enforcement.
- Bound runtime config and added regression tests for guardrail policy transitions plus producer catch-up behavior under pressure.

## Task Commits

1. **Task 1: Implement defer-age guardrail policy and catch-up window controls** - `120aaab` (feat)
2. **Task 2: Apply guardrail outcomes to ingestion and maintenance producers** - `fb1f0aa` (feat)
3. **Task 3: Add guardrail policy/config tests and environment bindings** - `a0185ea` (test)

## Files Created/Modified
- `Transcendence.Service.Core/Services/Jobs/Priority/StarvationGuardrailPolicy.cs` - Evaluates breach/window/cooldown state and produces forced catch-up outputs.
- `Transcendence.Service.Core/Services/Jobs/ChampionAnalyticsIngestionJob.cs` - Applies guardrail outputs to queue target, max candidates, and API-priority pause checks.
- `Transcendence.Service.Core/Services/Jobs/SummonerMaintenanceJob.cs` - Applies the same guardrail contract to maintenance producer behavior.
- `Transcendence.Service/appsettings.json` - Adds `Jobs:StarvationGuardrail` runtime config.
- `tests/Transcendence.Service.Core.Tests/Jobs/StarvationGuardrailPolicyTests.cs` - Covers breach detection, catch-up continuation, cooldown behavior, and reactivation.

## Decisions Made
- Used refresh-lock keys for both catch-up-window and cooldown windows to keep fairness coordination deterministic across runs.
- Kept adaptive throughput as the primary budget signal and treated forced catch-up as a bounded override for starvation cases.
- Scoped defer-age estimation per producer eligibility rules (`all tracked` for champion ingestion, `stale-eligible` for maintenance).

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

- Task 3 test compile initially failed due FluentAssertions method name mismatch; corrected assertion API and re-ran verification.
- Workspace emits pre-existing warnings from historical migration class naming (`init`) and one transient copy-retry warning during build.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Guardrail logic and config are in place for telemetry/operational hardening in later phase work.
- No blockers found for continuing to `03-04`.

## Self-Check: PASSED

- FOUND: `.planning/phases/03-priority-ingestion-throughput/03-03-SUMMARY.md`
- FOUND commits: `120aaab`, `fb1f0aa`, `a0185ea`
