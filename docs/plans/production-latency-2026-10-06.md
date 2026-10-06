# Production page latency: findings and fix plan

Investigation: October 6, 2026, approximately 12:23–12:31 PDT. Production access was read-only through the homelab SSH configuration. No services, settings, jobs, schemas, data, or statistics were changed. The initial findings below are preserved; implementation status is recorded separately.

## Current decision

The user deferred storage upgrades and authorized the software changes below on October 6, 2026.
Operate within the existing HDD budget: reduce concurrency and producer fan-out, bound retention and
analytics writes, persist expensive default reads, and monitor full responses and freshness. The
storage migration described later is a future option, not part of this deployment. Large partition
conversion or a full table rewrite is also deferred: rewriting the current 302 GiB corpus would compete
with production traffic and needs a separate placement/migration design.

## Observed evidence

| Observation | Evidence and implication |
| --- | --- |
| Production database is on a rotating disk | CT 104's rootfs is `hdd:vm-104-disk-0`; the Postgres Docker volume is inside that rootfs. Hypervisor `lsblk` identifies its backing device, `sdb`, as rotational. |
| Disk saturation persists | Every live interval in two `iostat` samples showed approximately 100% utilization. Five-minute rates averaged over the preceding 24 hours were about 99.9%. Observed physical write latency ranged from roughly 42 to 432 ms. |
| Processes wait for storage | Database sessions repeatedly waited on `WALWrite`, `WalSync`, and `DataFileRead`. CT 104's five-minute full I/O pressure was about 54%. Active queries in the initial sample had no reported blocking PIDs. |
| CPU and capacity are available | Most sampled hypervisor CPU time was idle. The hypervisor reported about 37 GiB available memory and CT 104 about 21 GiB available. Its filesystem had about 286 GiB free. Historical swap usage exists, but these samples identify I/O contention rather than current CPU exhaustion or a full filesystem. |
| Database size is 302 GiB | The three largest timeline tables occupy about 248 GiB combined: event payloads 136 GiB, item events 63 GiB, timeline snapshots 49 GiB. Sizes include indexes and allocated space; tuple counts are estimates, not an exact bloat measurement. |
| Archiving caused substantial database work | Since the September 29 statistics reset, 43 cascade-delete statements accumulated about 42.6 execution hours: mean 59.5 minutes, maximum 6.8 hours. They generated about 41 GiB of WAL. This is accumulated statement execution time, not a measured archive job duration. |
| Archiving is not the only current source | No archive process was present during inspection. The archive log ended with successful completion and was last modified October 6 at 07:20 PDT. Ingestion and autovacuum were active afterward. |
| Background analytics are expensive too | Since the same statistics reset, Build Lab upserts averaged 38 seconds across 1,519 calls and generated about 70 GiB of WAL. A raw champion/role/patch/rank baseline count used by synergies averaged 11.44 seconds across 6,231 calls. These statistics span busy and quieter periods and are not current endpoint percentiles. |
| Background work remains backlogged | The first queue sample had approximately 6,310 discovery jobs ready and 892 timeline jobs ready; timeline backlog grew to 1,048 in a later sample. Configured worker pools total 46 slots. Separate queues reserve workers but share the same database and disk. |
| Redis is not under memory pressure | Approximately 7.6 MiB used, zero evictions and zero rejected connections. Aggregate Redis hit/miss statistics include multiple workloads and cannot identify a specific endpoint's cache effectiveness. |
| Existing storage cannot hold a simple full migration | `local-lvm` had about 209 GiB physically available, less than the 302 GiB database. CT 104's full rootfs is larger still. Do not infer capacity from nominal thin volume sizes. |

Representative unauthenticated reads through production NPM:

| Route | First byte | Full HTTP response |
| --- | --- | --- |
| `/` | 23 ms | 58 ms |
| `/lol/champions` | 17 ms | 104 ms |
| `/lol/tierlist` | 18 ms | 188 ms |
| `/lol/champions/103` | 151 ms | 4.16 s |
| `/lol/summoners/na/Kronic-NA1` | 18 ms | 2.65 s |
| `/lol/pro-builds` | 17 ms | 832 ms |
| `/lol/leaderboards` | 19 ms | 10.10 s |
| `/lol/leaderboards`, subsequent read | 25 ms | 167 ms |

Subsequent direct public API reads returned the default NA leaderboard in 21 ms and Ahri's default analytics profile in 192 ms. The leaderboard response contained 100 entries, and the repeated page contained no unavailable fallback. This variability is consistent with slow cold work followed by fast cached reads. The first page read was not captured for content inspection, so its 10-second duration alone does not prove a backend timeout.

These are small LAN samples, not load tests, browser LCP measurements, or WAN measurements. Next.js streams an early loading shell, so first-byte latency does not represent the wait for useful page data. An independently running deployment recreated the worker during inspection; no deployment was initiated by this investigation.

## Phase 1: reduce contention before the storage change

1. Record a baseline under normal traffic: route latency, error/fallback rate, WAL throughput, physical disk latency, I/O pressure, job completion rate, cache coverage, and data freshness. Use existing telemetry and modest synthetic reads; avoid adding a load test to the saturated server.
2. Experiment with reducing discovery and timeline concurrency from eight workers each to two each. These are starting values to validate, not established optimal settings. Reduce producer fan-out at the same time so the queues do not grow faster. Retain the interactive refresh path and a small analytics warming allowance. Evaluate the broad main pool separately rather than blindly lowering every queue.
3. Bound Build Lab work using `Analytics:BuildLab:MatchBatchSize` and `MaxMatchesPerRun`, plus an elapsed-time/WAL budget and pacing between committed batches. It already has a processed-match ledger, transactions, and concurrency protection: preserve those guarantees. Check deployed limits before choosing smaller values. Do not turn off `Analytics:BuildLab:Enabled` as a shortcut: that flag also controls detailed timeline capture and serving.
4. Give cache warming a reserved allowance independent of heavy Build Lab and maintenance work. Both currently use `analytics-warm`; separate worker slots alone will still require a shared database concurrency/I/O budget. Keep the existing gap-free cache refresh behavior.
5. Avoid overlapping another large archive/prune with catch-up ingestion. Retain normal autovacuum. Stop expanding batch size or worker counts based on CPU headroom while the storage device is saturated.

Tradeoff: discovery, historical timelines, and Build Lab updates can become less fresh. Publish explicit freshness bounds and restore throughput gradually once the storage constraint is removed. Revert a concurrency experiment if page latency does not improve and backlog or freshness breaches its agreed bound.

## Phase 2: put PostgreSQL on fast storage

Preferred: provision local SSD/NVMe capacity on PVE `.84` for a dedicated Postgres volume containing data, indexes, and WAL. A 1 TB volume is a reasonable planning floor for the current 302 GiB database; size the final volume from measured growth, operating headroom, and migration workspace. Keep NAS storage for verified archives and backups.

The existing 209 GiB of free SSD space is insufficient for copying the entire database. If additional storage is unavailable, a smaller SSD tablespace for hot tables/indexes and a separate WAL location could be investigated as an interim option. That requires an explicit placement and backup design and leaves timeline writes and retention on the HDD; it is more complicated and provides a partial improvement.

Migration procedure to design and approve before execution:

1. Obtain a current consistent database backup and verify a restore. CT 104 is currently excluded from the live PBS guest job (`vmid 100,102,103,105`). Configuration backups and match-detail archives are not a current full database backup.
2. Choose either a maintenance-window physical copy or a pre-seeded physical replica and controlled cutover, depending on acceptable downtime. Estimate the copy time against the real source disk, not its theoretical bandwidth.
3. Pause producers and drain/stop application writers. For a physical copy, shut down Postgres cleanly before the final copy; this host has a documented history of expensive recovery after a short shutdown timeout.
4. Configure an explicit persistent Postgres volume on the fast device. Preserve the exact PostgreSQL major version, permissions, extension configuration, and existing Docker volume mapping requirements. Avoid moving all of CT 104 just to move the database.
5. Verify recovery/startup, schema, representative data, public reads, authenticated operations, and bounded job writes. Keep the old copy until verification is complete. A rollback after writes begin requires accounting for those new writes; do not treat an old stopped copy as a lossless rollback.
6. Resume warming and interactive work, then ingestion in stages. Compare physical I/O latency, page completion, errors, and freshness against the baseline. Confirm ongoing database backup coverage before considering the migration complete.

PostgreSQL already has a 30-minute checkpoint interval, 8 GiB `max_wal_size`, LZ4 WAL compression, and `checkpoint_completion_target=0.9`. More blind checkpoint tuning is not the first fix. Checkpoints flush changed pages and can impose significant I/O load, as described in the [PostgreSQL WAL documentation](https://www.postgresql.org/docs/18/wal-configuration.html).

## Phase 3: make public reads predictable

- Add refresh-ahead snapshots for default regional leaderboards and serve the previous successful result while rebuilding. The service currently uses a five-minute cache whose miss runs the ranking query on the request. Precompute the regional ranking order or verify an appropriate index/query design for its region/queue/tier/LP sort. Use plain `EXPLAIN` first and bounded execution measurements under controlled conditions.
- Move synergy baseline and pair aggregates to compact incremental facts/snapshots, following the existing matchup generation pattern. Preserve current rank attribution, queue/patch/region scope, minimum-sample rules, and raw-versus-stats semantics. This removes repeated raw participant/rank joins rather than merely increasing their timeouts.
- Verify complete default champion profile coverage after deployments and active-patch changes. Cache warming already exists, and the profile already gives synergies a two-second wait budget; retain these improvements. Cover remaining profile dependencies and exact normalized cache keys instead of adding a duplicate warmer.
- Stream independent optional content and render stable page controls early where still useful, particularly leaderboards. Use section-level fallbacks without allowing essential data failures to masquerade as a successful fast page. Measure useful data completion as well as initial render.
- Ensure server-side timeouts cover response-body consumption and cancellation propagates through request-dependent database work. The common fetch helper currently clears its timeout after response headers arrive. Background cache fills that deliberately outlive a request need their own bounded lifecycle.

Implement as focused changes with relevant integration tests and production-like query plans. Update `docs/ARCHITECTURE.md`; update `docs/API.md` and generated contracts only if payloads or endpoint behavior change. Generate any schema migrations with EF CLI after model changes, per repository policy.

## Phase 4: make retention stop causing multi-hour delete pressure

Short term: preserve frozen match-ID sets, complete child-table coverage, export verification, and fail-closed pruning. Replace unpaced 20,000-match cascade batches with smaller, time-budgeted batches and adaptive pauses. Budget by actual child rows/bytes and observed latency rather than parent matches alone. Add a single-run lock, resumable progress, stage durations, WAL metrics, and load-based pause/resume. `nice` on the shell is insufficient because database I/O runs inside PostgreSQL.

Long term: design patch- or time-partitioned raw match/timeline storage so verified archival can be followed by partition retirement. This needs a separate design for existing foreign keys, unique constraints, late-arriving matches, residual archives, and online migration of the existing tables. It is not a quick migration or a prerequisite to the SSD move. PostgreSQL documents that partition retirement avoids the overhead and subsequent vacuum work of bulk row deletion in its [partitioning guide](https://www.postgresql.org/docs/18/ddl-partitioning.html).

Do not use `VACUUM FULL` as the initial latency fix. Normal vacuum reuses freed space, while `VACUUM FULL` rewrites tables and requires an exclusive lock; see [routine vacuuming](https://www.postgresql.org/docs/18/routine-vacuuming.html).

## Phase 5: verify the result and repair monitoring

The exported frontend lab report's last success was October 2 at 11:08:48 UTC. A performance container was still running roughly nine hours after starting; its log ended with `ECONNREFUSED` from Chromium. The host unit has a 30-minute timeout, but that does not guarantee cleanup of a daemon-owned Docker container. Repair named-container cleanup, explicit resource/time limits, freshness reporting, and staleness alerts. Add actual champion and summoner profiles to the current route set; it mostly tests directories and utility pages.

Proposed acceptance targets, to confirm against the desired product freshness and load:

- Default cached public APIs: p95 below 300 ms under normal traffic and background ingestion.
- Useful page data for champion, leaderboard, and summoner routes: p95 below 2 seconds; no routine waits for the 10-second backend timeout or unavailable fallbacks.
- Test warm reads, naturally cold/new-filter reads, deployment cache turnover, and an active archive window. Test both LAN and an external client.
- Record browser LCP separately; target below 2.5 seconds with a stated device/network profile. A loading skeleton alone does not pass the data-completion target.
- Physical database device no longer continuously saturated; write/read latency and CT I/O pressure fall materially. Cache warming succeeds, job completion keeps up with arrivals, and backlog/freshness remain bounded.
- No losses or duplicate counting in Build Lab or owned operations; archive verification and restore checks continue to pass.

Suggested delivery sequence: (1) background work budgets and measurement, (2) storage migration, (3) leaderboard/cache and raw synergy read fixes, (4) paced retention, (5) partitioning as a separately reviewed project. Monitoring repair should accompany the first change so each subsequent step has trustworthy before/after evidence.

## Authorized implementation, October 6, 2026

- Worker configuration reduced to main=8, warm=1, timeline=2, discovery=2, history=1 before code rollout; existing image preserved. New code adds a separate batch=1 lane. Producers have low queue caps.
- Build Lab uses small atomic batches, elapsed/global-WAL budgets and pacing; expired cleanup is bounded.
- Regional leaderboards and default synergies have durable refresh-ahead snapshots with old-success retention and age limits. Profile warming rotates persistently across bounded runs.
- Compact synergy facts backfill incrementally; PostgreSQL tests compare ninety scoped raw/fact results, resume limits, current-rank behavior, and preservation after archive deletion.
- Archive chunks are independently verified, resumable and pressure-aware; tests cover failed export, timeout rollback, immutable verified resume, protected patch and pressure yield.
- Body deadlines, leaderboard streaming, bounded Lighthouse cleanup, and full-response/freshness probes complete the software plan.

Operational targets and overrides are documented in `docs/DEVELOPMENT.md` and `scripts/ops/README.md`.
Targets require ongoing observation; catch-up may take multiple days on the current disk.
