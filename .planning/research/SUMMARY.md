# Research Synthesis: Early-Patch Relevance and Ingestion Throughput

## Stack Direction
- Keep the current brownfield topology: `Transcendence.WebAPI`, `Transcendence.Service`, `Transcendence.WebAdminPortal`, and `apps/web` BFF.
- Keep Hangfire + Postgres orchestration and EF Core/Postgres as the source of truth.
- Keep Redis/HybridCache for hot reads and stale-safe fallback behavior.
- Keep OpenAPI + generated TS client workflow stable.
- Add a centralized, key-agnostic throughput throttle service in `Service.Core`.
- Add config-driven queue tiers (`patch-hot`, `patch-warm`, `backfill`) instead of key-tier-specific code paths.
- Avoid net-new infra this milestone (Kafka/RabbitMQ/stream sidecars).
- Avoid broad API redesign; use additive freshness metadata only when needed.

## Table Stakes
- Patch-aware ingestion prioritization to improve first-days-of-patch relevance.
- Throughput-safe budget allocation that protects user-triggered refresh from background ingestion.
- Deterministic stale/fresh fallback behavior with explicit metadata.
- Required recurring-job startup integrity checks with fail-fast behavior for mandatory jobs.
- Refresh-lock lifecycle management (retention + cleanup + key normalization).
- End-to-end cancellation propagation in long-running background paths.
- Baseline observability for queue depth, lock contention, freshness lag, and deferred work.

## Differentiators
- Early-patch confidence scoring in analytics payloads.
- Adaptive ingestion driven by demand signals (high-interest champions/roles first).
- Freshness-aware cache invalidation tiers to reduce stale-hit windows on volatile data.
- Operator-facing ingestion SLO dashboard and controlled mitigation actions.
- Incremental analytics recompute to avoid full monolithic recomputation.

## Architecture Direction
- Keep controllers thin and preserve existing read + enqueue boundary semantics.
- Centralize scheduling policy once and consume it in both dev and prod workers.
- Keep environment differences as explicit options, not duplicated scheduling logic.
- Refactor ingestion into staged flow: candidate selection, queue assignment, fetch, upsert, invalidation, metrics.
- Unify lock/key rules across API-triggered and scheduled refresh work.
- Add conservative lock cleanup with expiration + age threshold and telemetry-first rollout.
- Preserve API/BFF contract behavior (`200`/`202`) and extend only with additive freshness fields.
- Plan SQL-first aggregation extraction for heavy compute hotspots as a follow-on milestone.

## Top Risks
- Worker scheduling drift between environments causing non-reproducible behavior.
- Partial startup success masking missing critical recurring jobs.
- Non-propagated cancellation causing deploy-time overlap and stuck work.
- Throughput logic coupled to dev-key limits, creating rework after production key access.
- Unbounded refresh-lock table growth increasing latency and storage pressure.
- Memory-heavy analytics aggregation under patch bursts causing GC and timeout regressions.
- Proxy-aware rate limiting gaps causing over-throttle for shared-network users.
- API/BFF/OpenAPI drift during fallback evolution.
- Test coverage gaps on orchestration/admin paths.
- Documentation lag causing operational mistakes.

## Recommended Build Order
1. Consolidate worker scheduling policy and remove duplicate job-registration logic.
2. Introduce mandatory vs optional recurring jobs and fail-fast startup for mandatory set.
3. Standardize cancellation propagation across job entry points and Riot/data paths.
4. Implement refresh-lock lifecycle controls (normalization, retention, cleanup job, metrics).
5. Add patch-aware candidate scoring and queue-tier throughput budgets.
6. Add deterministic freshness metadata and fallback semantics without breaking contracts.
7. Add observability and guardrails (queue, lock, freshness, starvation budgets).
8. Expand tests for startup integrity, lock semantics, fallback contract behavior, and proxy rate limiting.
9. Update docs/OpenAPI/api-client in lockstep with any API behavior changes.
10. Start follow-on differentiators: confidence scoring and adaptive demand signals.

## What this means for next milestone scope
- Scope should center on reliability + throughput policy, not infrastructure replacement.
- In-scope deliverables:
  - Shared scheduler policy used by both worker hosts.
  - Mandatory-job fail-fast startup contract and health verification.
  - Cancellation propagation standards on critical background pipelines.
  - Lock lifecycle controls with bounded growth guarantees.
  - Patch-priority ingestion policy and queue budgets with starvation guardrails.
  - Additive freshness metadata and deterministic fallback semantics.
  - Focused test expansion on orchestration, contracts, and traffic governance.
  - Required docs parity updates (`README.md`, `docs/DEVELOPMENT.md`, `docs/API.md`, `docs/ARCHITECTURE.md`, OpenAPI as needed).
- Out-of-scope for this milestone:
  - Broker/stream platform migration.
  - Broad API surface redesign.
  - New unrelated product surfaces.
- Exit criteria for roadmap acceptance:
  - Mandatory recurring jobs are deterministically present or startup fails.
  - Patch-window high-priority freshness improves without long-tail starvation.
  - Refresh-lock storage trend is bounded.
  - API/BFF compatibility is maintained with validated contracts.
  - Operational runbooks/docs match implemented behavior.
