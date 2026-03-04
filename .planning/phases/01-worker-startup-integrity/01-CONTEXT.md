# Phase 1: Worker Startup Integrity - Context

**Gathered:** 2026-03-04
**Status:** Ready for planning

<domain>
## Phase Boundary

Harden worker startup integrity so required recurring jobs are verified before healthy operation, startup does not silently continue in broken states, cancellation behavior is safe during deploy/runtime transitions, and scheduling policy is consistent across environments.

</domain>

<decisions>
## Implementation Decisions

### Startup strictness and health gating
- Startup must fail fast when required recurring job registration/verification cannot be established after bounded retries.
- Health must only be reported green after required jobs are verified.
- On transient startup dependency failures (Hangfire/DB), use bounded retries before declaring mandatory startup failure.
- Startup integrity failures must be surfaced through health checks and structured logs.

### Mandatory job policy
- Mandatory baseline includes core freshness jobs: patch detection, retry failed matches, analytics refresh/ingestion, and summoner maintenance.
- Non-mandatory job registration failures should continue startup in degraded mode with explicit warnings.
- Mandatory-job baseline should be the same across development and production by default.
- Verification should confirm jobs are registered and schedulable, not only present in storage records.

### Environment parity and policy ownership
- Development and production should be mostly aligned via one shared scheduling policy, with explicit profile-driven differences.
- Scheduling decisions should be owned by shared policy logic plus configuration, not duplicated per worker class.
- Environment-specific behavior should use named profiles.
- `CleanupOnStartup` should remain off by default and require explicit opt-in.

### Cancellation and recovery behavior
- Use graceful shutdown windows, then enforce cancellation when work exceeds the window.
- Allow tightly scoped non-cancelable critical sections for consistency and lock hygiene.
- On cancellation, commit safe validated progress and resume remaining work later.
- Unfinished work should auto-requeue/recover on next cycle rather than require manual operator intervention.

### Claude's Discretion
- Exact degraded-state signaling mechanism and where to surface it inside current hosting stack.
- Exact bounded-retry timings and retry backoff strategy.
- Concrete implementation pattern for profile selection and shared scheduler composition.
- Exact cancellation timeout values per job type.

</decisions>

<specifics>
## Specific Ideas

- Keep decisions key-agnostic so this phase remains valid when production Riot API key access is granted.
- Prefer operationally explicit behavior (health + logs) over silent continuation.

</specifics>

<code_context>
## Existing Code Insights

### Reusable Assets
- `Transcendence.Service/Workers/ProductionWorker.cs`: current production scheduling, startup enqueue flow, and error-wrapped registration helpers.
- `Transcendence.Service/Workers/DevelopmentWorker.cs`: current development scheduling profile with analytics-focused behavior.
- `Transcendence.Service.Core/Services/Jobs/Configuration/WorkerJobScheduleOptions.cs`: central schedule/toggle options contract already used by both workers.
- `Transcendence.Service.Core/Services/Extensions/HangfireExtensions.cs`: reusable recurring-job cleanup/validation helpers (`RemoveInvalidRecurringJobs`, `PurgeJobs`).
- `Transcendence.Service.Core/Services/Jobs/SummonerRefreshJob.cs` and `ChampionAnalyticsIngestionJob.cs`: existing cancellation-aware method signatures and queue semantics.

### Established Patterns
- Scheduling is currently wrapper-driven (`TryConfigureRecurringJob`, `TryRemoveRecurringJob`) with continue-on-error logging.
- Environment split exists at host startup (`Transcendence.Service/Program.cs`) by choosing `DevelopmentWorker` vs `ProductionWorker`.
- Job schedules and feature toggles are configuration-driven in `Transcendence.Service/appsettings*.json` under `Jobs:Schedule`.
- Hangfire queue priority pattern is already established (`refresh-high`, `default`, `refresh-low`).

### Integration Points
- `Transcendence.Service/Program.cs`: hook for shared scheduler/policy registration and hosted-service wiring.
- `Transcendence.Service/Workers/*.cs`: primary startup integrity, verification, and degraded/fail behavior surface.
- `Transcendence.Service/appsettings.json` and `Transcendence.Service/appsettings.Development.json`: profile and mandatory/non-mandatory configuration surfaces.
- `tests/Transcendence.Service.Core.Tests/*` and `tests/Transcendence.WebAPI.Tests/*`: existing test suites to extend for startup integrity and cancellation regression coverage.

</code_context>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope.

</deferred>

---

*Phase: 01-worker-startup-integrity*
*Context gathered: 2026-03-04*
