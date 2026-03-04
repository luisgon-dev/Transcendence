# STACK Research: Early-Patch Relevance + Ingestion Throughput

## Milestone Lens
- Goal: improve early-patch relevance and ingestion throughput while Riot development-key limits are active.
- Constraint: avoid architecture that must be replaced when production key access is approved.
- Baseline stack is strong: ASP.NET Core + Hangfire + EF Core/Postgres + Redis/HybridCache + Next.js BFF.

## Executive Direction
- `Keep` the current host split (WebAPI, Worker, Admin) and Hangfire-first execution model.
- `Adopt` key-agnostic throughput controls, patch-priority ingestion policies, and freshness-aware cache/API patterns.
- `Avoid` net-new infrastructure (Kafka, separate stream processors, OLAP sidecar) in this milestone.

## Keep (High Confidence)
- Keep ASP.NET Core + Worker Service topology for independent scaling.
  - Confidence: High
  - Why: already aligned to request/read vs ingestion/write separation.
- Keep Hangfire + Postgres storage as primary orchestration substrate.
  - Confidence: High
  - Why: queue priorities already exist (`refresh-high`/`refresh-low`) and can be extended without migration risk.
- Keep EF Core + Npgsql as source-of-truth persistence layer.
  - Confidence: High
  - Why: needed for transactional lock/cursor semantics and deterministic upserts.
- Keep Redis + HybridCache for hot analytics reads.
  - Confidence: High
  - Why: allows stale-safe fallback while ingestion catches up during patch day.
- Keep OpenAPI + generated TS client contract workflow.
  - Confidence: High
  - Why: milestone should not destabilize API/BFF integration contracts.

## Adopt (High Confidence)
- Adopt a centralized ingestion throttle service in `Service.Core` with per-endpoint budgets.
  - Confidence: High
  - Keep/Replace: keep Camille + Riot services, replace ad-hoc rate decisions.
  - Concrete: enforce token-bucket or lease-window counters backed by Redis or Postgres row-level state.
- Adopt queue policy tiers focused on relevance windows.
  - Confidence: High
  - Concrete: `patch-hot`, `patch-warm`, `backfill` queues with strict worker ordering and concurrency caps.
  - Concrete: send live/priority summoner refresh to `patch-hot`; historical catch-up to `backfill`.
- Adopt patch-scoped freshness metadata for all analytics payloads.
  - Confidence: High
  - Concrete: include `patchVersion`, `dataFreshnessMinutes`, `sampleSize`, `lastIngestedAt` in response DTOs.
  - Concrete: preserve existing `202` behavior but add deterministic stale/fresh semantics.
- Adopt ingestion candidate scoring before enqueue.
  - Confidence: High
  - Concrete: prioritize champions/summoners by recency, pick rate, and missing patch coverage.
  - Concrete: avoid FIFO-only ingestion under key constraints.
- Adopt scheduler policy extraction (shared code for dev/prod workers).
  - Confidence: High
  - Concrete: one scheduling policy component with environment toggles; remove duplicated recurring-job logic.

## Adopt (Medium Confidence)
- Adopt SQL-first aggregation for heavy analytics hotspots now done in large in-memory pipelines.
  - Confidence: Medium
  - Concrete: move top-N, grouped winrate, and patch-window stats into bounded SQL projections/materialized tables.
  - Risk: requires careful index tuning and query-plan validation.
- Adopt explicit lock-retention cleanup for refresh locks.
  - Confidence: Medium
  - Concrete: add recurring cleanup job + max-age policy to prevent unbounded lock row growth.
- Adopt stable Riot SDK channel (or controlled nightly gate).
  - Confidence: Medium
  - Concrete: prefer stable package; if nightly retained, pin with compatibility smoke tests in CI.

## Avoid (High Confidence)
- Avoid introducing Kafka/RabbitMQ/event bus in this milestone.
  - Confidence: High
  - Reason: operational overhead is high and does not directly solve dev-key bottleneck first.
- Avoid replacing Hangfire with another orchestrator.
  - Confidence: High
  - Reason: migration cost is large; current concerns are policy/reliability, not tooling ceiling.
- Avoid broad API contract redesigns for frontend.
  - Confidence: High
  - Reason: improvement can be achieved via additive freshness fields and existing `202` patterns.
- Avoid key-specific shortcuts that hardcode development-key assumptions.
  - Confidence: High
  - Reason: would force rollback when production key is approved.

## Production-Key Readiness Rules
- All throughput limits must be configuration-driven, not code-constant.
- Queue taxonomy must stay valid at higher request budgets (only concurrency numbers change).
- Freshness metadata contract must remain stable across both key tiers.
- Backfill workloads must remain preemptible by patch-hot workloads at all times.

## Recommended Stack Moves by Horizon
- `Now (this milestone)`: throttle service, queue tiers, freshness metadata, scheduler deduplication, lock cleanup.
- `Next`: SQL-first aggregation extraction for the largest memory-heavy analytics paths.
- `Later (only if needed)`: evaluate broker/stream infra after proving Hangfire + policy tuning cannot meet SLOs.

## Confidence Summary
- Overall recommendation confidence: High.
- Keep decisions: High confidence across all items.
- Adopt decisions: mostly High; SQL extraction and SDK channel hardening are Medium.
- Avoid decisions: High confidence due milestone scope and brownfield constraints.
