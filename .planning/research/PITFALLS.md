# Brownfield Milestone Pitfalls Research

## 1) Divergent Worker Scheduling Between Environments
- Why this is a pitfall: near-duplicate logic in development and production workers can drift, causing jobs to run differently than expected.
- Warning signs:
  - A recurring job exists in `ProductionWorker` but not `DevelopmentWorker` (or vice versa).
  - Environment-specific incidents that cannot be reproduced locally.
  - Job cadence or queue names differ without an explicit reason.
- Prevention:
  - Centralize shared recurring-job registration into one scheduling policy/service.
  - Keep only explicit environment toggles in each worker host class.
  - Add tests that assert required jobs are present per environment profile.
- Suggested phase mapping: Phase "Worker Reliability and Scheduling Consolidation".

## 2) Partial Startup Success Masks Missing Critical Jobs
- Why this is a pitfall: broad exception handling during worker startup can allow boot to continue while required jobs are not scheduled.
- Warning signs:
  - Logs show startup errors followed by "continuing startup" semantics.
  - Hangfire dashboard appears healthy but key recurring jobs are absent.
  - Patch-window freshness degrades after deployment despite successful process start.
- Prevention:
  - Classify startup failures into recoverable vs non-recoverable categories.
  - Fail fast when mandatory job registration fails.
  - Add explicit startup health checks for required recurring jobs.
- Suggested phase mapping: Phase "Operational Correctness and Fail-Fast Startup".

## 3) Cancellation Is Not Propagated Through Background Work
- Why this is a pitfall: `CancellationToken.None` usage in jobs undermines graceful shutdown and increases deployment/runtime instability.
- Warning signs:
  - Long-running jobs continue after host shutdown signals.
  - Frequent overlapping execution after restarts.
  - Increased lock contention or stale lease behavior during deploy windows.
- Prevention:
  - Standardize token propagation from host to service layers.
  - Define explicit non-cancelable boundaries only where truly required.
  - Add cancellation behavior tests for high-cost job paths.
- Suggested phase mapping: Phase "Job Execution Safety and Shutdown Semantics".

## 4) Throughput Tuning That Is Coupled to Development-Key Limits
- Why this is a pitfall: overly key-specific tuning can become technical debt once production Riot key capacity changes.
- Warning signs:
  - Hardcoded assumptions based on current development-key limits.
  - Queue or batching logic that would be invalid at higher API budgets.
  - Required rework immediately after key approval.
- Prevention:
  - Use config-driven rate and concurrency controls with safe envelopes.
  - Separate policy (limits, priorities) from mechanism (job orchestration).
  - Validate tuning under both constrained and expanded capacity profiles.
- Suggested phase mapping: Phase "Key-Agnostic Ingestion Throughput Improvements".

## 5) Unbounded Refresh-Lock Key Growth
- Why this is a pitfall: lock rows can accumulate by identifier cardinality without a cleanup lifecycle.
- Warning signs:
  - Monotonic growth in lock table row count.
  - Increasing DB index/storage pressure for refresh lock structures.
  - Degraded lock acquisition latency over time.
- Prevention:
  - Add retention and cleanup jobs for stale lock entries.
  - Normalize key strategy and constrain lock key cardinality.
  - Instrument lock table growth metrics and alert thresholds.
- Suggested phase mapping: Phase "Lock Lifecycle and Data Hygiene".

## 6) Memory-Heavy Analytics Paths Under Early-Patch Burst
- Why this is a pitfall: monolithic compute services with large in-memory aggregation can fail under high churn and patch freshness demand.
- Warning signs:
  - Spikes in worker memory/GC pressure during analytics refresh windows.
  - Slow or timed-out analytics computations after data ingest surges.
  - Frequent scale-up pressure without proportional throughput gains.
- Prevention:
  - Push more aggregation work to bounded SQL projections.
  - Break monolithic compute paths into narrower, composable steps.
  - Add load tests for early-patch data volumes and concurrency.
- Suggested phase mapping: Phase "Analytics Compute Decomposition and Performance".

## 7) Proxy-Aware Rate Limiting Not Fully Enforced
- Why this is a pitfall: IP-partitioned limits without trusted forwarded-header handling can over-throttle many users behind one proxy edge.
- Warning signs:
  - Bursty 429 responses affecting unrelated users from the same network.
  - Rate-limit behavior changes between direct and proxied deployments.
  - Support complaints from enterprise/shared-network users.
- Prevention:
  - Configure forwarded-header middleware with trusted proxy boundaries.
  - Build partition keys from validated forwarded client identity.
  - Add integration tests for proxy and non-proxy traffic scenarios.
- Suggested phase mapping: Phase "API Reliability and Traffic Governance".

## 8) Test Coverage Gaps Around Orchestration and Admin Paths
- Why this is a pitfall: high-change orchestration surfaces with low coverage create regression risk during throughput/reliability work.
- Warning signs:
  - Changes to worker scheduling and admin operations ship without targeted tests.
  - Regressions found only in staging/production environments.
  - Increased manual verification burden for each patch.
- Prevention:
  - Prioritize tests for worker scheduling, lock semantics, and admin operations.
  - Add contract checks for refresh/fallback behavior in read endpoints.
  - Tie phase exit criteria to coverage on critical orchestration flows.
- Suggested phase mapping: Phase "Reliability Test Expansion".

## 9) API/BFF Contract Drift During Fallback Evolution
- Why this is a pitfall: introducing graceful fallback behavior can break BFF assumptions or generated client expectations if contracts are not synchronized.
- Warning signs:
  - Backend response shape/status changes without OpenAPI update.
  - Frontend or generated client breaks after backend deployment.
  - CI drift checks fail late in the cycle.
- Prevention:
  - Treat contract updates as first-class for any API behavior change.
  - Regenerate and validate `packages/api-client` in the same change set.
  - Add endpoint-level tests for 202/fallback and freshness metadata paths.
- Suggested phase mapping: Phase "Graceful Fallback and Contract Integrity".

## 10) Documentation Lag Causes Incorrect Operational Usage
- Why this is a pitfall: this milestone touches APIs, runtime tuning, and possibly job configuration; stale docs create operational mistakes and onboarding friction.
- Warning signs:
  - New env vars or commands exist in code but not docs.
  - API behavior differs from `docs/API.md` and committed OpenAPI spec.
  - Team members rely on tribal knowledge for worker/job behavior.
- Prevention:
  - Enforce docs parity in each PR touching API/runtime architecture.
  - Update canonical docs and OpenAPI in lockstep with implementation.
  - Add lightweight PR checklist entries aligned with AGENTS.md policy.
- Suggested phase mapping: Phase "Documentation and Release Readiness".
