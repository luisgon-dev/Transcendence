# Roadmap: Transcendence Platform Evolution

## Overview

This roadmap delivers early-patch relevance and ingestion reliability under development-key limits by hardening worker foundations first, then enforcing lock and throughput controls, then exposing deterministic freshness semantics, and finally locking quality/documentation parity for safe long-term operation.

## Phases

**Phase Numbering:**
- Integer phases (1, 2, 3): Planned milestone work
- Decimal phases (2.1, 2.2): Urgent insertions (marked with INSERTED)

Decimal phases appear between their surrounding integers in numeric order.

- [ ] **Phase 1: Worker Startup Integrity** - Ensure recurring-job registration, fail-fast startup, and cancellation-safe execution.
- [ ] **Phase 2: Refresh Lock Lifecycle Control** - Normalize lock behavior and bound lock-store growth with telemetry.
- [ ] **Phase 3: Priority Ingestion Throughput** - Prioritize patch-relevant/high-value ingestion without starving long-tail work.
- [ ] **Phase 4: Deterministic Freshness Contract** - Provide consistent fallback semantics and additive freshness metadata.
- [ ] **Phase 5: Quality Gates and Documentation Parity** - Protect behavior with tests and keep API/ops docs in sync.

## Phase Details

### Phase 1: Worker Startup Integrity
**Goal**: Operators and users can rely on worker startup integrity, scheduling policy consistency, and safe cancellation during deploy/runtime transitions.
**Depends on**: Nothing (first phase)
**Requirements**: WORK-01, WORK-02, WORK-03, WORK-04
**Success Criteria** (what must be TRUE):
  1. Operator can verify mandatory recurring jobs are registered before traffic-serving health is reported.
  2. If mandatory scheduling registration fails, startup fails fast with no partial healthy state.
  3. Long-running refresh/ingestion paths honor cancellation so deploys do not create stale or duplicated updates.
  4. Scheduling behavior is consistent across development and production via shared policy logic.
**Plans**: TBD

### Phase 2: Refresh Lock Lifecycle Control
**Goal**: Refresh lock ownership is deterministic, lock storage growth is bounded, and contention/retention behavior is observable.
**Depends on**: Phase 1
**Requirements**: LOCK-01, LOCK-02, LOCK-03
**Success Criteria** (what must be TRUE):
  1. Refresh requests use normalized lock keys across all entry points, reducing collision and ownership inconsistencies.
  2. Expired/stale lock records are cleaned up by retention policy so lock storage does not grow unbounded.
  3. Operators can monitor lock contention and growth trends through lock lifecycle telemetry.
**Plans**: TBD

### Phase 3: Priority Ingestion Throughput
**Goal**: Patch-window ingestion capacity favors user-visible/high-value data while preserving forward progress on long-tail workloads.
**Depends on**: Phase 2
**Requirements**: INGT-01, INGT-02, INGT-03, INGT-04
**Success Criteria** (what must be TRUE):
  1. User-triggered refresh requests are prioritized ahead of non-interactive background ingestion during patch spikes.
  2. Configurable priority scoring ingests patch-relevant champion/stat datasets before lower-value candidates.
  3. Queue-tier throughput budgets keep high-priority freshness responsive under load.
  4. Starvation guardrails ensure low-priority ingestion still receives progress over time.
**Plans**: TBD

### Phase 4: Deterministic Freshness Contract
**Goal**: API/BFF freshness behavior is deterministic and transparent to users while remaining backward compatible for existing clients.
**Depends on**: Phase 3
**Requirements**: FRSH-01, FRSH-02, FRSH-03
**Success Criteria** (what must be TRUE):
  1. When fresh data is unavailable, responses follow consistent stale/fallback states instead of ambiguous behavior.
  2. Returned statistics include freshness metadata (timestamp/state) that clients can read without contract breakage.
  3. Existing clients continue functioning with backward-compatible API/BFF behavior as freshness semantics improve.
**Plans**: TBD

### Phase 5: Quality Gates and Documentation Parity
**Goal**: Freshness/fallback and operator-critical behaviors are protected by automated verification and accurate docs/contracts.
**Depends on**: Phase 4
**Requirements**: QUAL-01, QUAL-02
**Success Criteria** (what must be TRUE):
  1. Automated tests detect regressions in user-facing freshness/fallback behavior and critical worker orchestration paths.
  2. Operator-facing behavior changes are documented in API/development/architecture docs in the same milestone.
  3. OpenAPI and generated client updates stay aligned with any contract-level freshness field changes.
**Plans**: TBD

## Progress

**Execution Order:**
Phases execute in numeric order: 1 -> 2 -> 3 -> 4 -> 5

| Phase | Plans Complete | Status | Completed |
|-------|----------------|--------|-----------|
| 1. Worker Startup Integrity | 2/3 | In Progress | - |
| 2. Refresh Lock Lifecycle Control | 0/TBD | Not started | - |
| 3. Priority Ingestion Throughput | 0/TBD | Not started | - |
| 4. Deterministic Freshness Contract | 0/TBD | Not started | - |
| 5. Quality Gates and Documentation Parity | 0/TBD | Not started | - |
