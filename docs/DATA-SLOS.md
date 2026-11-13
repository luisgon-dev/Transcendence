# Data quality and freshness targets

These are initial operating targets, not a claim about measured production attainment.
Review them after seven days of Prometheus data and adjust ingestion budgets before
changing query or index policy. Never label a stored observation as a fresh probe.

| Signal | Target | Evidence and response |
| --- | --- | --- |
| Durable dispatch | 99% under 30 seconds; oldest pending under 60 seconds | `operation_dispatch_lag_seconds` histogram, `operation_pending_oldest_age_seconds`; alert after two minutes over 60 seconds |
| Dispatch observer | Successful scan within 120 seconds | `operation_scan_last_success_seconds`; alert independently of queue depth |
| Image delivery | All four representative optimized assets available after startup | `/api/readiness`, `transcendence_image_probe_ready`; rollout waits, two-minute failure/staleness alerts |
| Analytics snapshots | Promoted matchup generation within two hours during normal ingestion | Existing `transcendence_analytics_matchup_last_success_unixtime_seconds` and generation-failure alerts; keep the previous complete generation on failure |
| Patch coverage | Selected-patch sample status reaches its configured sufficient threshold within 24 hours of regional rollout | Dataset/sample endpoints report games, target coverage and patch phase; report regions still on the previous patch rather than combining them silently |
| Recent profile import | 95% of accepted requests reach a terminal outcome within two minutes outside a Riot rate-limit incident | Owned operation status timestamps and phase/result fields; partial/failed outcomes remain explicit |
| Exhaustive history | Eventual terminal outcome; separate from recent-profile success | Child operation and season coverage delta; no fixed completion SLA while Riot throttles historical work |

## Provenance and equivalence

Rank segmentation uses the **latest stored rank**, not rank at match time. Flex
uses current Flex rank; ARAM/Arena use Solo/Duo as a skill segment. Rank refreshes
can therefore move old games between cohorts. These are observed tracked-game
rates, not predicted win probabilities or a historical Riot rank ledger.

The PostgreSQL equivalence suite compares raw and materialized reads across rank,
region, role and queue, verifies nonempty aggregates, and exercises concurrent
same-patch refresh, patch isolation and raw-fact removal followed by recomputation.
Tabular refresh takes a PostgreSQL transaction advisory lock per patch, including
direct/operator callers. Ingestion may continue during refresh; a snapshot's
computed time describes publication, not the last match observation.

The pending outbox index is checked with `EXPLAIN (ANALYZE, BUFFERS)` against
20,000 terminal rows. Keep production plan/latency evidence before adding further
indexes. Terminal operations default to seven days, expired auth tokens to one
additional day. Cleanup is leaf-first and batch-limited; active operations are
never expired to hide stuck work. Increase cleanup throughput if creation exceeds
the default 500 rows/table/hour; `operation_retention_deleted_total` records work.

## Product measurement

`/api/telemetry/journey` accepts only seven fixed event names and at most 128 bytes.
It rejects extra fields, arbitrary labels and cross-origin browser requests. No
Riot ID, URL, search text, IP, user/session identifier or event record is retained
by this store. Prometheus receives aggregate process counters. A browser stores
only a visited flag and a session-counted flag to distinguish first/return sessions;
blocked storage or requests simply omit optional metrics. Requests omit cookies and referrers. Synthetic browsers are
excluded. These approximate counts are not unique-user analytics.

Use lookup success/failure, multi-search and live-probe completion alongside return
sessions to select future product work. Publish an observation window and sample
size with any conclusion. Do not invent a usage-based priority before collecting it.
