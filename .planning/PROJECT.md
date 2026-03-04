# Transcendence Platform Evolution

## What This Is

Transcendence is a League of Legends data platform with a .NET API, background ingestion workers, and a Next.js web experience backed by a BFF proxy. It delivers summoner and champion statistics by combining persisted match data, scheduled refresh jobs, and cached analytics. This initiative focuses on improving data relevance during early patch windows while operating under Riot development-key limits.

## Core Value

Users should see relevant, patch-current statistics quickly and reliably, even when upstream API capacity is constrained.

## Requirements

### Validated

- ✓ Summoner and analytics read APIs are operational with refresh-on-demand patterns — existing
- ✓ Background job scheduling and queue-based ingestion pipelines are operational via Hangfire — existing
- ✓ Auth boundaries (app/user/admin) and BFF proxy paths are in place across API and web — existing
- ✓ Contract-first API workflow exists (OpenAPI + generated TS client + CI drift checks) — existing

### Active

- [ ] Improve early-patch data relevance under development-key API limits
- [ ] Increase ingestion throughput for high-value, patch-current datasets
- [ ] Define graceful fallback behavior when fresh data is not yet available
- [ ] Implement improvements that remain valid after production Riot API key approval

### Out of Scope

- Key-specific redesigns that only make sense with production-key throughput — avoid obsolescence risk
- New product surfaces unrelated to data freshness/relevance — not aligned with current milestone outcome
- Large API contract redesigns not required for relevance and reliability goals — preserve integration stability

## Context

- Brownfield monorepo with mapped architecture in `.planning/codebase/*.md`.
- Current user pain: early in a patch, users may not see relevant data.
- Known external constraint: production Riot API key is pending approval; development key is active.
- Desired outcome for this milestone: improve ingestion throughput and relevance now without creating technical choices that must be undone once production key access is available.

## Constraints

- **External Dependency**: Riot production API key approval is pending — throughput ceilings currently constrained by development key limits.
- **Compatibility**: Changes must remain valid after production key activation — avoid temporary architecture that becomes obsolete.
- **Contract Stability**: Existing API and BFF boundaries should remain stable unless a change is clearly required for milestone outcomes.
- **Operational Safety**: Throughput changes must preserve scheduler/job reliability and avoid degraded startup or partial-job states.

## Key Decisions

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| Prioritize ingestion throughput and relevance in early patch windows | Directly addresses stated user-visible pain and milestone success target | — Pending |
| Keep improvements key-agnostic | Prevents rework when production Riot key is approved | — Pending |
| Treat this as a core-improvements milestone, not a net-new feature milestone | Keeps scope tight around user impact and reliability | — Pending |

---
*Last updated: 2026-03-04 after initialization*
