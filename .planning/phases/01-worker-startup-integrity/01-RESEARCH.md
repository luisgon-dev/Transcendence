# Phase 1 Worker Startup Integrity Research

## Goals and requirement mapping

| Requirement | Goal for this phase | Primary implementation target |
|---|---|---|
| WORK-01 | Mandatory recurring jobs are verifiably registered before startup is considered healthy. | Add startup registration + verification pipeline with explicit mandatory-job policy and startup state reporting. |
| WORK-02 | Startup fails when mandatory scheduling cannot be registered/verified (no false healthy state). | Replace current fail-open wrappers for mandatory jobs with bounded retry + fail-fast throw. |
| WORK-03 | Long-running refresh/ingestion paths stop safely during deploy/runtime cancellation and resume cleanly. | Propagate cancellation through loops/catches, avoid swallowing `OperationCanceledException`, and keep lock/cursor consistency. |
| WORK-04 | Dev/prod scheduling policy is shared and profile-driven rather than duplicated/divergent. | Extract one scheduling policy source used by both workers, with explicit profile overrides. |

This mapping is directly aligned with the worker reliability requirements in `.planning/REQUIREMENTS.md` (`WORK-01..WORK-04`) and the phase context decisions in `.planning/phases/01-worker-startup-integrity/01-CONTEXT.md`.

## Current-state findings (with file references)

1. Startup currently uses fail-open error wrappers for recurring job registration.
- `ProductionWorker` wraps all registration/removal calls in `Try*` methods that log and continue (`Transcendence.Service/Workers/ProductionWorker.cs:37`, `Transcendence.Service/Workers/ProductionWorker.cs:371`).
- `DevelopmentWorker` has the same fail-open pattern (`Transcendence.Service/Workers/DevelopmentWorker.cs:41`, `Transcendence.Service/Workers/DevelopmentWorker.cs:248`).
- Result: mandatory job setup failures do not currently fail startup (WORK-02 gap).

2. There is no explicit startup verification gate for mandatory jobs.
- Jobs are configured, but there is no post-registration verification that required jobs are present and schedulable (`Transcendence.Service/Workers/ProductionWorker.cs:29`, `Transcendence.Service/Workers/DevelopmentWorker.cs:28`).
- `RemoveInvalidRecurringJobs` exists and can deserialize payloads, but it is cleanup logic, not startup integrity verification (`Transcendence.Service.Core/Services/Extensions/HangfireExtensions.cs:20`).
- Result: operator cannot deterministically verify startup integrity before declaring healthy (WORK-01 gap).

3. Scheduling policy behavior is duplicated and diverges by environment.
- `Program.cs` hard-switches workers by environment (`Transcendence.Service/Program.cs:70`).
- `DevelopmentWorker` intentionally removes non-analytics jobs (`detect-patch`, `retry-failed-matches`, `poll-live-games`) (`Transcendence.Service/Workers/DevelopmentWorker.cs:184`).
- `ProductionWorker` always configures those jobs (`Transcendence.Service/Workers/ProductionWorker.cs:37`, `Transcendence.Service/Workers/ProductionWorker.cs:46`, `Transcendence.Service/Workers/ProductionWorker.cs:195`).
- Result: policy ownership is split across two classes (WORK-04 gap).

4. Option model supports schedules/toggles but not startup integrity policy controls.
- `WorkerJobScheduleOptions` covers cron/toggles and cleanup/run-on-startup flags only (`Transcendence.Service.Core/Services/Jobs/Configuration/WorkerJobScheduleOptions.cs:5`).
- Missing controls: mandatory job list/profile, retry/backoff policy, and startup integrity mode.

5. Cancellation is partially propagated, but cancellation exceptions can be swallowed in long loops.
- Core job APIs accept `CancellationToken` and pass tokens to IO calls (`Transcendence.Service.Core/Services/Jobs/SummonerRefreshJob.cs:35`, `Transcendence.Service.Core/Services/Jobs/ChampionAnalyticsIngestionJob.cs:28`).
- In `SummonerRefreshJob`, broad `catch (Exception)` blocks inside per-match loops can capture cancellation and continue (`Transcendence.Service.Core/Services/Jobs/SummonerRefreshJob.cs:336`, `Transcendence.Service.Core/Services/Jobs/SummonerRefreshJob.cs:480`, `Transcendence.Service.Core/Services/Jobs/SummonerRefreshJob.cs:620`, `Transcendence.Service.Core/Services/Jobs/SummonerRefreshJob.cs:647`).
- Lock release in `finally` currently uses the job cancellation token; if already canceled, lock release can be skipped (`Transcendence.Service.Core/Services/Jobs/SummonerRefreshJob.cs:117`, `Transcendence.Service.Core/Services/Jobs/SummonerRefreshJob.cs:581`).
- Result: deploy-time cancellation behavior is not fully hardened (WORK-03 gap).

6. Existing tests cover ingestion/refresh behavior but not worker startup integrity.
- Coverage exists for ramp queueing and refresh lock behavior (`tests/Transcendence.Service.Core.Tests/ChampionAnalyticsIngestionJobRampTests.cs:23`, `tests/Transcendence.Service.Core.Tests/SummonerRefreshJobTests.cs:27`).
- No tests currently target `ProductionWorker`, `DevelopmentWorker`, startup verification, or fail-fast startup behavior.

## Recommended implementation strategy

### WORK-01 and WORK-02: mandatory startup verification + fail-fast behavior

1. Introduce a shared startup orchestrator used by both workers.
- Example shape: `WorkerStartupOrchestrator` with phases: cleanup -> register -> verify -> publish startup state.
- Keep worker classes thin; orchestrator owns policy and outcomes.

2. Define explicit job descriptors with mandatory metadata.
- Example descriptor fields: `JobId`, `Cron`, `Register()`, `RemoveWhenDisabled`, `IsMandatory`, `Profiles`.
- Mandatory default baseline should include context-defined core jobs: patch detection, retry failed matches, analytics refresh, analytics ingestion, and summoner maintenance.

3. Add post-registration verification against Hangfire storage.
- Verify each mandatory job by ID in storage (`recurring-job:{id}` hash):
  - hash exists,
  - cron value is parseable,
  - job payload deserializes to an invocable job (`InvocationData.DeserializePayload(...).DeserializeJob()`).
- Reuse logic style already present in `RemoveInvalidRecurringJobs` for deserialization checks (`Transcendence.Service.Core/Services/Extensions/HangfireExtensions.cs:74`).

4. Add bounded retry for transient startup dependencies.
- Retry mandatory registration + verification with small bounded backoff (for DB/Hangfire cold-start races).
- If still failing after retries, throw and fail hosted-service startup.

5. Treat non-mandatory failures as degraded, not healthy.
- Continue startup for optional jobs but emit structured warning logs and set startup state to degraded.
- Do not mark startup healthy unless mandatory verification succeeds.

### WORK-04: one shared scheduling policy across environments

1. Extract policy composition into a shared component.
- Replace duplicated scheduling code in `ProductionWorker` and `DevelopmentWorker` with a shared policy builder.

2. Move environment differences to explicit profiles.
- Keep one baseline schedule policy, then apply profile deltas (for example `default`, `development-analytics-only`).
- Default dev profile should match mandatory baseline unless explicitly overridden.

3. Unify registration API for testability.
- Prefer injecting and using `IRecurringJobManager` in both workers instead of mixing static `RecurringJob` APIs.

4. Keep `CleanupOnStartup` opt-in.
- Preserve existing default-off behavior from options (`WorkerJobScheduleOptions.CleanupOnStartup`) and context decision.

### WORK-03: cancellation hardening in ingestion/refresh paths

1. Do not swallow cancellation.
- In long-running loops, add `ct.ThrowIfCancellationRequested()` at loop boundaries and before expensive calls.
- Replace broad exception catches with `catch (OperationCanceledException) { throw; }` before generic catches, or `catch (Exception ex) when (ex is not OperationCanceledException)`.

2. Keep lock hygiene in cancellation paths.
- In `finally`, release locks with a non-cancelable token (or short internal timeout) so canceled jobs do not leak locks.
- Keep release failures logged but non-fatal.

3. Preserve safe progress semantics.
- Keep cursor updates and duplicate-safe persistence behavior already present in `SummonerRefreshJob` (`UpsertCursorAsync`, duplicate handling), but ensure cancellation exits promptly before additional fetch/persist iterations.

4. Keep Hangfire token placeholder usage when enqueueing child jobs.
- Continue using `CancellationToken.None` in expression trees for enqueued jobs (Hangfire runtime injects the execution token into method parameter at execution time).

## Risks and mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Fail-fast startup causes service not to boot during transient DB/Hangfire issues. | Availability hit during deploy/restart. | Bounded startup retries with backoff; clear fatal log with failing job IDs. |
| Mandatory-job policy is misconfigured (too broad/too narrow). | False failures or silent missing critical jobs. | Default mandatory baseline in code + config override with validation at startup. |
| Cancellation hardening changes job completion patterns under load. | More early exits during deploy windows. | Add regression tests for partial progress + lock release; monitor cancellation metrics/logs after rollout. |
| Shared policy refactor introduces behavior drift. | Unexpected schedule changes in one environment. | Golden tests comparing expected descriptors by profile; explicit profile snapshots in tests. |
| Non-cancelable lock release hangs. | Shutdown latency. | Use tight timeout/guarded retry around lock release and log timeout events. |

## Test strategy suggestions

1. Add startup integrity unit tests (new worker test project recommended, e.g. `tests/Transcendence.Service.Tests`).
- Mandatory registration success -> startup state healthy.
- Mandatory registration failure after retries -> startup throws (fail-fast).
- Mandatory verification failure (missing/unresolvable hash) -> startup throws.
- Optional registration failure -> startup continues in degraded state and logs warning.

2. Add shared policy parity tests.
- Assert both env profiles use same mandatory baseline job IDs.
- Assert explicit profile deltas only (for example, dev-specific cron differences) and no hidden removals.

3. Add cancellation propagation tests in existing core test project.
- `SummonerRefreshJob` stops when token is canceled mid-loop.
- `OperationCanceledException` is rethrown (not logged as generic fetch/persist failure).
- Lock release still attempted on cancellation.

4. Extend existing ingestion tests.
- Build on `ChampionAnalyticsIngestionJobRampTests` to assert cancellation-aware early stop and no extra enqueue after cancellation.

5. Add host-level startup smoke test (component test).
- Start an in-memory host with mocked `IRecurringJobManager`/`JobStorage` and verify startup outcome path (healthy/degraded/fail-fast).

## Rollout/operational notes

1. Roll out in two operational steps.
- Step 1: deploy shared policy + verification logging with startup-state visibility.
- Step 2: enable strict fail-fast for mandatory jobs in all environments once false positives are eliminated.

2. Add startup integrity logs with stable event IDs.
- Emit one startup summary record with: mandatory jobs expected, registered, verified, failed, retry count, and final startup state.

3. Publish operator runbook updates.
- Include how to identify missing mandatory jobs, how retries behaved, and what action to take on fail-fast boot failures.

4. Keep cleanup controls explicit.
- Continue requiring explicit opt-in for `CleanupOnStartup`; avoid accidental recurring-job loss during deploy.

5. Verify profile intent in config review.
- Review `appsettings.json` and `appsettings.Development.json` schedule/profile deltas during PR to prevent accidental policy divergence.

## Validation Architecture

### Quick validation commands (developer workstation)

```bash
dotnet test tests/Transcendence.Service.Core.Tests/Transcendence.Service.Core.Tests.csproj --filter "FullyQualifiedName~SummonerRefreshJobTests|FullyQualifiedName~ChampionAnalyticsIngestionJobRampTests"
```

```bash
dotnet test tests/Transcendence.Service.Tests/Transcendence.Service.Tests.csproj --filter "FullyQualifiedName~StartupIntegrity|FullyQualifiedName~WorkerSchedulingPolicy"
```

### Full validation commands (CI / pre-merge gate)

```bash
dotnet test Transcendence.sln -c Release
```

```bash
dotnet build Transcendence.sln -c Release
```

### Concrete verification architecture for this phase

1. Unit layer (fast, deterministic).
- Validate policy composition, mandatory classification, verification logic, retry behavior, and startup-state transitions with mocked Hangfire interfaces.

2. Component layer (host startup behavior).
- Spin up the worker host with test doubles for Hangfire registration/storage.
- Assert strict outcomes:
  - mandatory failure -> host startup fails,
  - optional failure -> host starts but reports degraded startup state,
  - full success -> host reports healthy startup state.

3. Core-job cancellation layer.
- Exercise cancellation during `SummonerRefreshJob` and ingestion queueing loops to confirm immediate stop, safe lock handling, and no duplicate/stale continuation.

4. Operational smoke layer.
- In a deploy-like environment, verify startup summary logs and recurring-job presence for mandatory IDs before marking deployment healthy.
