# Operations Runbook

This runbook covers common operational failures for Transcendence. It assumes a Compose-style deployment with PostgreSQL, Redis, `Transcendence.WebAPI`, `Transcendence.Service`, Hangfire, and the Next.js web app.

## First Response Checklist

1. Open the admin dashboard (`/admin`) with an account that has the `admin` role.
2. Check API readiness (`/health/ready`) and worker liveness from container logs or the hosting platform.
3. In `/admin/jobs`, note Hangfire server count, active queues, failed jobs, scheduled jobs, and the oldest enqueued job age.
4. In `/admin/logs`, inspect both `webapi` and `service` sources for recent structured events and warnings.
5. Avoid broad backlog deletion unless this runbook explicitly calls for it. Prefer retrying, pausing producers, or clearing narrow stale groups.

## Stuck Refresh Locks

Symptoms:
- Summoner refresh endpoints keep returning `202 Accepted` with no profile progress.
- Analytics ingestion shows repeated `api_priority_active` or high-pressure decisions while no user refresh seems active.
- Refresh lock telemetry has rising `growth.expired` or contention with flat cleanup deletes.

Check:
1. In `/admin/logs`, filter for these event names:
   - `refresh_lock.lifecycle`
   - `refresh_lock.contention_wait_hint`
   - `refresh_lock.cleanup`
   - `refresh_lock.growth_snapshot`
2. Confirm the affected `lock_class` (`summoner-refresh`, `refresh-priority:api`, or `tft:summoner-refresh`) and `platform_region`.
3. In `/admin/jobs`, verify whether matching refresh jobs are processing, failed, or no longer present.
4. Confirm the cleanup schedule is enabled:
   - `Jobs:Schedule:EnableRefreshLockLifecycleCleanup=true`
   - `Jobs:Schedule:RefreshLockLifecycleCleanupCron=*/5 * * * *`

Mitigation:
- If jobs are still processing, do not delete locks; wait for lease expiry or job completion.
- If locks are expired and cleanup is running, wait one cleanup interval and re-check `growth.deleted_last_run`.
- If cleanup is failing, fix the worker/database issue first, then let cleanup remove expired rows.
- If a processing Hangfire job is clearly wedged, delete or retry only the specific job from `/admin/jobs`; be aware already-started side effects may still complete.
- Restarting the worker is safe for lease-based locks, but it is not a substitute for cleanup health.

## Hangfire Backlogs

Symptoms:
- `/admin/jobs` shows large `enqueued`, `scheduled`, or `failed` counts.
- Summoner refreshes are slow even though the API is healthy.
- Low-priority ingestion appears to starve API-triggered refreshes.

Check:
1. Confirm Hangfire servers are present and queues are being consumed.
2. Compare queues in priority order:
   - LoL: `refresh-high`, `default`, `refresh-low`
   - TFT: `tft-refresh-high`, `tft-default`, `tft-refresh-low`
3. Check whether recurring producers are adding work faster than workers process it.
4. Inspect failed job exception messages before retrying in bulk.

Mitigation:
- For API-impacting incidents, pause low-priority recurring producers from `/admin/jobs` before clearing anything.
- Retry a small representative batch of failed jobs first. If the same exception repeats, fix the root cause instead of bulk retrying.
- Use bulk backlog delete only for a narrow known-bad group, state, or queue. Do not perform blanket purge on patch rollover; current-patch catch-up work is intended to survive restarts.
- Scale worker concurrency only after verifying database and Riot API limits can tolerate additional throughput.
- Resume paused producers after high-priority queues drain and failed-job rate stabilizes.

## Low New-Patch Samples

Symptoms:
- Public LoL analytics show low-sample, no-data, provisional, or early-patch messaging.
- Analytics responses include low `sampleSize` relative to `minimumRecommendedSampleSize`.
- Current-patch pages have sparse tier list, champion, build, matchup, or pro-build data.

Check:
1. Query `GET /api/lol/analytics/status` or use public patch badges to verify the active analytics patch.
2. Confirm analytics payload metadata: `sampleStatus`, `sampleSize`, `minimumRecommendedSampleSize`, `patchAgeHours`, `patchPhase`, and `isProvisional`.
3. In `/admin/jobs`, verify ramp jobs are scheduled during the configured new-patch window:
   - `refresh-champion-analytics-ramp`
   - `champion-analytics-ingestion-ramp`
   - `summoner-maintenance-ramp`
4. Check ingestion throughput events for `skipped_no_candidates`, `stopped_api_priority_preemption`, or sustained `highpressure`.

Mitigation:
- Treat early low-sample UI as expected immediately after a patch; do not force previous-patch fallback.
- If candidate scarcity is reported, verify high-value roster and ranked candidate ingestion are healthy by region.
- If API-priority demand is preempting ingestion, let high-priority queues drain or temporarily pause nonessential producers.
- If ramp jobs are disabled or outside the window unexpectedly, review `Jobs:*:NewPatchRampHours` and `Jobs:Schedule:EnableNewPatchRamp`.

## Missing Operational Logs

Symptoms:
- `/admin/logs` has only one source, reports a source unavailable, or shows no recent entries.
- Container stdout/stderr contains file logger warnings.

Check:
1. In Compose, confirm both API and worker mount the shared `operational_logs` volume at `/var/log/transcendence`.
2. Confirm each host's `OperationalLogs:DirectoryPath` points to the mounted directory.
3. For split-host deployments, configure WebAPI reader overrides:
   - `AdminLogs:Sources:webapi:DirectoryPath`
   - `AdminLogs:Sources:service:DirectoryPath`
4. Inspect container stdout/stderr for the one-time warning emitted when the logger cannot create or append the target file.

Mitigation:
- Fix mount paths or filesystem permissions, then restart the affected host.
- If hosts intentionally write to separate disks, set the `AdminLogs:Sources:*:DirectoryPath` overrides so the admin API can read both locations.
- Remember the admin log reader scans the live `*.log` file plus rotated `*.log.N` archives.

## Cache Invalidation

Symptoms:
- A refreshed summoner still shows stale stats.
- Analytics pages continue to show old derived data after successful ingestion.
- Public patch badges do not match backend analytics status.

Check:
1. Confirm the canonical PostgreSQL row changed before assuming a cache bug.
2. For summoner stats, verify refresh jobs completed and invalidated the `summoner-stats:{summonerId}` tag.
3. Confirm Redis is reachable; HybridCache uses in-memory L1 plus Redis L2, so single-host restarts may not clear all cached data.
4. Compare web-facing BFF responses with direct backend API responses to determine whether stale data is in backend cache or frontend rendering.

Mitigation:
- Prefer targeted tag invalidation through the code path that owns the data (for example, summoner refresh completion).
- If no targeted invalidation exists for the affected derived data, restart the impacted backend host(s) only after confirming Redis state and accepting temporary cold-cache latency.
- Do not clear all Redis keys unless the incident scope is broad and understood; Redis also backs Hangfire and other runtime state.

## API / Client Contract Drift

Symptoms:
- Web routes fail with unexpected response shapes, missing fields, or status handling issues.
- TypeScript types disagree with backend responses.
- CI or local checks show OpenAPI/client generation differences.

Check:
1. Confirm whether the backend API contract changed (routes, auth, status codes, request/response payloads).
2. Regenerate and verify the spec/client from repo root:

```bash
corepack pnpm api:gen
corepack pnpm api:check
```

3. Review diffs in `openapi/transcendence.v1.json` and `packages/api-client` generated output as applicable.
4. Confirm BFF route handlers under `apps/web` handle all expected backend statuses, especially `202 Accepted` refresh flows and ProblemDetails errors.

Mitigation:
- Commit OpenAPI spec updates with the API change when the contract intentionally changes.
- Regenerate the TypeScript client locally before web changes that consume new/changed fields.
- Update `docs/API.md` when auth, payloads, or status codes change.
- If drift is accidental, revert the incompatible API or web/client change rather than patching around inconsistent contracts.
