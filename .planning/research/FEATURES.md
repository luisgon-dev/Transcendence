# Features Research: Early-Patch Relevance Milestone

## Scope Anchor
- Product context: brownfield League data platform with .NET API, Hangfire workers, Redis/Postgres, and Next.js BFF.
- Milestone objective: improve early-patch relevance and ingestion throughput under development-key limits.
- Constraint: solutions must remain valid after production Riot API key approval.
- Exclusions: avoid broad API redesign and net-new product surfaces not tied to freshness/reliability.

## Table Stakes

| Feature | User/Business Value | Complexity | Dependencies | Notes |
|---|---|---|---|---|
| Patch-aware ingestion prioritization | Gets patch-current data visible faster during the first days of a patch. | Medium | `ProductionWorker`, `ChampionAnalyticsIngestionJob`, queue ordering, appsettings tuning | Should use policy/config, not hardcoded patch rules, to survive key-tier change. |
| Deterministic fallback states for stale data | Prevents empty/confusing responses when fresh analytics are unavailable. | Medium | API controllers (`Summoners`, `Analytics`, `ChampionAnalytics`), cache metadata, OpenAPI if response contract changes | Prefer explicit freshness metadata over silent partial responses. |
| Required-job startup integrity checks | Avoids "healthy but partially scheduled" worker state. | Medium-High | `ProductionWorker`, `DevelopmentWorker`, startup error policy, logging/alerts | Addresses concern about continue-on-error startup behavior. |
| Refresh lock lifecycle management | Prevents unbounded growth and lock table entropy over time. | Medium | `RefreshLockRepository`, lock key strategy, maintenance recurring job | Add retention/cleanup + lock-key normalization constraints. |
| Cancellation propagation in ingestion/refresh paths | Improves graceful shutdown and lowers stuck-work risk during deploys. | Medium | Worker scheduling methods, job signatures, Riot service methods | Standardize where cancellation is honored vs intentionally ignored. |
| Throughput-safe rate budget allocation | Maximizes useful ingestion within dev-key ceiling without harming user-triggered refreshes. | Medium-High | Queue partitioning, lock priority logic, Hangfire server queue config, Riot API client behavior | Needs clear budget split for API-priority vs background ingestion queues. |

## Differentiators

| Feature | Why It Differentiates | Complexity | Dependencies | Notes |
|---|---|---|---|---|
| Early-patch confidence scoring | Surfaces confidence/freshness level so users can trust or discount stats during patch rollout. | Medium | Analytics compute pipeline, cache payload shape, API/BFF display support | Can be additive metadata; avoid breaking existing clients. |
| Adaptive ingestion strategy by champion demand signals | Prioritizes high-interest champions/roles instead of uniform crawl. | High | Demand signal source (query frequency/live-game presence), ingestion scheduler policy, telemetry | Biggest ROI if signals are cheap and privacy-safe. |
| Freshness-aware cache invalidation tiers | Reduces stale-hit windows for high-volatility data while preserving cache efficiency elsewhere. | Medium-High | HybridCache tagging, compute service partitioning, patch/version dimensions | Must avoid cache stampede with bounded recompute concurrency. |
| Operator-visible ingestion SLO dashboard and controls | Shortens diagnosis time and supports manual mitigation during patch spikes. | Medium | Hangfire dashboard integrations, admin APIs, audit trail | Keep controls role-gated and auditable (`AdminOnly`). |
| Incremental analytics recompute paths | Cuts recomputation cost by recomputing only affected slices after new matches. | High | `ChampionAnalyticsComputeService` decomposition, SQL projections, job orchestration | Coupled with monolith decomposition concern; high leverage but multi-phase. |

## Anti-Features

| Anti-Feature (Do Not Build) | Why It Is Harmful Now | Safer Alternative |
|---|---|---|
| Key-tier-specific architecture fork (dev-only pipeline rewrite) | Creates throwaway design that must be reversed after key approval. | Build key-agnostic policy controls and scalable queue/budget parameters. |
| Large contract redesign for analytics endpoints | Increases integration risk and slows milestone throughput. | Additive metadata fields and backward-compatible behavior. |
| New unrelated product surfaces (social, new game modes, etc.) | Distracts from patch relevance and reliability objective. | Keep effort on ingestion quality, fallback UX, and operational integrity. |
| Hardcoded per-patch champion lists in code | Causes frequent redeploy churn and brittle operations. | Externalized config/policy with safe defaults and guardrails. |
| Broad synchronous "refresh everything now" endpoints | Amplifies rate-limit pressure and risks queue starvation. | Controlled async enqueues with priority classes and lock awareness. |
| Silent stale-data serving without freshness indicators | Hides data quality state and erodes user trust. | Explicit freshness timestamp/state/confidence metadata. |
| Ignoring cancellation in all heavy jobs | Increases deploy risk and leaves uninterruptible work. | Propagate cancellation except for short critical sections. |
| Expanding insecure dev defaults to speed experiments | Increases security risk and operational footguns. | Keep secure defaults; use explicit local opt-in for insecure modes. |

## Complexity and Dependency Notes
- Highest-complexity track: incremental analytics recompute + compute service decomposition.
- Highest-dependency track: throughput-safe rate budget allocation touching queues, locks, and Riot client call patterns.
- Lowest-risk early win: deterministic fallback states with explicit freshness metadata.
- Reliability prerequisite: required-job startup integrity checks before aggressive throughput tuning.
- Data hygiene prerequisite: refresh lock retention before scaling key cardinality from more ingestion paths.
- Brownfield coupling risk: worker duplication across `DevelopmentWorker`/`ProductionWorker` should be reduced to avoid policy drift.

## Suggested Delivery Order
1. Startup integrity checks + cancellation propagation baseline.
2. Refresh lock lifecycle management.
3. Patch-aware prioritization + throughput-safe rate budget allocation.
4. Deterministic fallback states and freshness metadata.
5. Differentiators (confidence scoring, adaptive demand signals, incremental recompute) in follow-on phases.
