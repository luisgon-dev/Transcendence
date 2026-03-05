# Requirements: Transcendence Platform Evolution

**Defined:** 2026-03-04
**Core Value:** Users should see relevant, patch-current statistics quickly and reliably, even when upstream API capacity is constrained.

## v1 Requirements

Requirements for this milestone. Each will map to exactly one roadmap phase.

### Ingestion Prioritization

- [ ] **INGT-01**: User-triggered refresh requests are prioritized over non-interactive background ingestion during patch spikes.
- [ ] **INGT-02**: User sees patch-relevant champion/stat data ingested first via configurable priority scoring.
- [ ] **INGT-03**: User-facing freshness remains responsive because queue-tier throughput budgets prevent background starvation of high-priority work.
- [ ] **INGT-04**: User still receives long-tail updates over time because starvation guardrails prevent permanent deferral of low-priority ingestion.

### Freshness and Fallback Contract

- [ ] **FRSH-01**: User receives deterministic response behavior when fresh data is unavailable (consistent stale/fallback states).
- [ ] **FRSH-02**: User can see freshness metadata (timestamp/state) for returned statistics without breaking existing clients.
- [ ] **FRSH-03**: User-facing API/BFF behavior remains backward compatible while freshness semantics are improved.

### Worker Reliability

- [x] **WORK-01**: Operator can verify mandatory recurring jobs are registered at startup before traffic is served.
- [x] **WORK-02**: Operator receives fail-fast startup behavior when mandatory scheduling fails (no partial healthy state).
- [x] **WORK-03**: User avoids stale or duplicated updates during deploys because cancellation is propagated through long-running ingestion/refresh paths.
- [x] **WORK-04**: Operator gets consistent job behavior across development and production environments through shared scheduling policy logic.

### Refresh Lock Lifecycle

- [x] **LOCK-01**: User refresh requests use normalized lock keys to reduce lock collisions and inconsistent lock ownership.
- [x] **LOCK-02**: Operator can bound lock storage growth via retention cleanup of expired/stale lock entries.
- [x] **LOCK-03**: Operator can monitor lock contention and growth via lock lifecycle telemetry.

### Quality and Safety

- [ ] **QUAL-01**: User-impacting freshness/fallback behavior is protected by automated API/worker tests for regression prevention.
- [ ] **QUAL-02**: Operator-facing changes are documented in API/development/architecture docs and OpenAPI when contract fields change.

## v2 Requirements

Deferred to a follow-on milestone.

### Differentiators

- **CONF-01**: User can view confidence scoring for early-patch analytics quality.
- **ADPT-01**: User-demand signals dynamically influence ingestion priority policy.
- **INCR-01**: User sees faster analytics updates through incremental recompute rather than broad recomputation.
- **OPSD-01**: Operator can use an SLO dashboard with controlled mitigation actions during patch spikes.

## Out of Scope

Explicit exclusions for this milestone.

| Feature | Reason |
|---------|--------|
| Architecture fork specialized to development-key limits | Would create throwaway design that becomes obsolete after production-key approval |
| Broad analytics API redesign | Increases integration risk and slows delivery of core freshness/reliability outcomes |
| New unrelated product surfaces | Not aligned with current milestone objective of relevance and throughput |
| Broker/stream platform migration | Too large for this milestone; current architecture can support targeted improvements first |

## Traceability

Updated during roadmap creation.

| Requirement | Phase | Status |
|-------------|-------|--------|
| INGT-01 | Phase 3 - Priority Ingestion Throughput | Pending |
| INGT-02 | Phase 3 - Priority Ingestion Throughput | Pending |
| INGT-03 | Phase 3 - Priority Ingestion Throughput | Pending |
| INGT-04 | Phase 3 - Priority Ingestion Throughput | Pending |
| FRSH-01 | Phase 4 - Deterministic Freshness Contract | Pending |
| FRSH-02 | Phase 4 - Deterministic Freshness Contract | Pending |
| FRSH-03 | Phase 4 - Deterministic Freshness Contract | Pending |
| WORK-01 | Phase 1 - Worker Startup Integrity | Complete |
| WORK-02 | Phase 1 - Worker Startup Integrity | Complete |
| WORK-03 | Phase 1 - Worker Startup Integrity | Complete |
| WORK-04 | Phase 1 - Worker Startup Integrity | Complete |
| LOCK-01 | Phase 2 - Refresh Lock Lifecycle Control | Complete |
| LOCK-02 | Phase 2 - Refresh Lock Lifecycle Control | Complete |
| LOCK-03 | Phase 2 - Refresh Lock Lifecycle Control | Complete |
| QUAL-01 | Phase 5 - Quality Gates and Documentation Parity | Pending |
| QUAL-02 | Phase 5 - Quality Gates and Documentation Parity | Pending |

**Coverage:**
- v1 requirements: 16 total
- Mapped to phases: 16
- Unmapped: 0 ✓

---
*Requirements defined: 2026-03-04*
*Last updated: 2026-03-04 after roadmap mapping*
