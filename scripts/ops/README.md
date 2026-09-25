# Ops scripts

## `poll-deploy.sh` — pull-based prod deploy (replaces wud)

Prod is a single Docker host on a private LAN with no public SSH and no tunnel, so a
cloud GitHub runner cannot push a deploy to it. The intended auto-updater, **wud**
(what's-up-docker), silently fails to detect new `:main` digests for these ghcr
packages — wud 8.2.2 (the latest release) reports `updateAvailable=false` even when
the registry digest has changed, so pushes never auto-deployed and each release had to
be recreated by hand (see task **P1.3b**).

`poll-deploy.sh` replaces wud for the three app services (`web`, `webapi`, `service`)
with a deterministic, **outbound-only** release poll:

1. Resolve the current `:main` manifest digest from GHCR and its
   `org.opencontainers.image.revision` label (the packages are public).
2. Compare both to the running container. The revision comparison is required because GHCR/Buildx
   can publish a rebuilt tag whose reported manifest digest is unchanged; digest-only comparison
   would silently miss that release.
3. If they differ, deploy in dependency order: `service` → `webapi` → `web`. Before replacing the
   worker, run the newly pulled image once with `Database__MigrateOnly=true`; only a successful
   migration continues the release.
4. Recreate one service at a time with `--no-deps` (PostgreSQL/Redis are never touched), wait for its
   healthcheck, then continue. A component failure aborts all later components for that poll.
5. On recreate/health failure, restore the exact prior container if Compose left it partially
   renamed. If that container is gone, retag and recreate the prior local image with `--pull never`.
   The failed digest is quarantined until `:main` changes, preventing a minute-by-minute rollback loop.
6. `docker image prune -f`, **only if a service was deployed** in this poll. Never prune on an idle
   poll: under the containerd image store a prune deletes the content of any pull still in flight,
   so a prune every ~60s made every pull longer than one interval (the large images) fail with
   `lease does not exist`.

No inbound exposure, no CI secret, no self-hosted runner. A `flock` guard prevents
overlapping runs. Runs every ~60s via the systemd timer (≈ wud's old cadence). Remote and
local digest/revision-resolution failures are counted per service under `/var/lib/transcendence-deploy`;
the third consecutive failure sends one Discord alert, and a successful resolution resets the
counter. The bounded health wait defaults to 420 seconds so the worker's four-minute startup
grace can complete. Failed Compose command output is stored mode `0600` while needed and copied into
the systemd journal (last 40 lines), so pull/migration/recreate failures are diagnosable instead of
being hidden by output redirection.

Optional overrides are `POLL_DEPLOY_RESOLUTION_ALERT_THRESHOLD`,
`POLL_DEPLOY_HEALTH_TIMEOUT_SECONDS`, `POLL_DEPLOY_HEALTH_POLL_SECONDS`, and
`POLL_DEPLOY_STATE_DIR`.

### Install (on prod, as root)

```bash
# from a checkout / scp of scripts/ops/
install -D -m 0755 poll-deploy.sh /root/deploy/poll-deploy.sh
install -D -m 0644 transcendence-deploy.service /etc/systemd/system/transcendence-deploy.service
install -D -m 0644 transcendence-deploy.timer   /etc/systemd/system/transcendence-deploy.timer
systemctl daemon-reload
systemctl enable --now transcendence-deploy.timer
```

### Operate

```bash
systemctl list-timers transcendence-deploy.timer   # next/last run
journalctl -u transcendence-deploy.service -n 50   # deploy history
/root/deploy/poll-deploy.sh                         # force a poll now
docker inspect transcendence-web --format '{{.State.Health.Status}}' # current gate state
systemctl disable --now transcendence-deploy.timer  # pause auto-deploy (e.g. during an incident)
```

The automatic rollback protects single-service replacement failures. Break-glass rollback remains:
pin a service to an immutable `:sha-<short>` tag in the compose file and `compose up -d` it (see
`docs/ARCHITECTURE.md` "Deployment & rollback").

> The app Compose services explicitly set `wud.watch=false`; keep that exclusion in place because
> this poller is the app release source of truth and two independent recreators can race each other.
> wud may continue watching public Docker Hub sidecars (portainer/dozzle/grafana/prometheus).

### Build Lab stats refresh

Build Lab has no image, timer or unit of its own. It is the worker's `refresh-build-lab-stats` Hangfire
job (`*/15 * * * *`), which adds each newly eligible match's build decisions to the additive
`BuildLabOptionStats` table and records the match in the `BuildLabProcessedMatches` ledger, one
transaction per 500-match batch. A crash mid-run therefore loses at most the batch in flight and never
double counts; the next run resumes from the ledger. A run is capped at 20,000 matches
(`Analytics:BuildLab:MaxMatchesPerRun`), so a fresh patch backfills over several runs — about 45
minutes of disk IO per patch on this box. The job no-ops unless `BUILD_LAB_ENABLED=true`, the same flag
that turns on the detailed timeline capture it reads.

Is it keeping up?

```bash
docker logs transcendence-service 2>&1 | grep 'Build Lab refresh' | tail -5   # counted / backlog per run
```

```sql
SELECT "Patch", count(*) AS matches, max("ProcessedAtUtc") AS last_counted
FROM "BuildLabProcessedMatches" GROUP BY 1 ORDER BY 1 DESC;
SELECT "Patch", count(*) AS stat_rows FROM "BuildLabOptionStats" GROUP BY 1 ORDER BY 1 DESC;
```

The worker's `transcendence_buildlab_backlog_matches` gauge is the number of eligible matches still to
count; it should fall every run and sit near zero between patches. `trn-buildlab-refresh-stale` pages
when no run has completed for two hours.

Only the four newest patches are kept (`PatchesToRetain`); older patches' rows are deleted by the next
run. To recount a patch from scratch — after changing how decisions are counted, say — delete its
ledger and stats rows and let the job refill them:

```sql
DELETE FROM "BuildLabOptionStats" WHERE "Patch" = '16.19';
DELETE FROM "BuildLabProcessedMatches" WHERE "Patch" = '16.19';
```

## `install-matchup-performance-db.sql` — online matchup source preparation

Run once before deploying the resumable matchup migration:

```bash
psql "$DATABASE_URL" -f install-matchup-performance-db.sql
```

It creates the successful-ranked-match and minute-15 timeline indexes with
`CREATE INDEX CONCURRENTLY`, then applies persistent table-local autovacuum/analyze thresholds and
runs `ANALYZE`. The script is idempotent, must run outside a transaction, and does not replace the EF
migration that creates the new narrow fact/generation tables.

## `archive-old-patches.sh` — match-detail retention (archive-then-prune)

Keeps the DB bounded by archiving **old-patch** LoL match detail to the NAS and then
pruning it, keeping only the newest `KEEP_PATCHES` (default 3) patches plus the active
patch. For each eligible patch it freezes that patch's match-ID set into a work table
(T0 snapshot — consistent under the live ingestion worker, which keeps inserting
old-patch rows as lapsed players return), streams every match table (`Matches` + all
cascade children) out via Postgres `COPY` → `gzip` → `ssh` to the NAS, **verifies**
(gzip integrity + exact row count), writes a manifest, and only then prunes — one
cascading `DELETE` on `Matches` in bounded batches (`DELETE_BATCH=20000`). It is a
**bounded-batch job**: dry-run by default (`APPLY=1` to act), verify-before-delete is
mandatory (a failed verify skips the prune and leaves the NAS archive intact),
residual-safe/idempotent (post-T0 inserts to an already-`_DONE` patch archive to
`residual-<epoch>/`), and HDD-adaptive (big slice → seq scan; small slice → forced
index nested-loops, since forcing index on a big slice = catastrophic random HDD reads).

**Why a host cron, not a Hangfire job:** it needs host-level `docker exec` (psql `COPY`)
and NAS `ssh` that the worker container neither has nor should have. The script is the
version-controlled source of truth; prod runs a synced copy (`sha256` must match).

### Install / operate (on prod, as root)

```bash
install -m 0755 archive-old-patches.sh /root/archive-old-patches.sh   # keep in sync with repo
# /etc/cron.d/trn-archive runs it weekly:
#   0 5 * * 0 root APPLY=1 KEEP_PATCHES=3 bash /root/archive-old-patches.sh >> /root/trn-archive.log 2>&1
APPLY=0 bash /root/archive-old-patches.sh                    # dry-run (preview eligible patches + counts)
ONLY_PATCH=16.9 APPLY=1 bash /root/archive-old-patches.sh    # archive+prune one patch
tail -f /root/trn-archive.log                                # run history
```

`archive-remaining-bulk.sh` is the one-time bulk sweep used to clear the initial backlog
(same archive-then-prune guarantees, looped). **Do not run either during a DB-load
incident** — the `COPY` of millions of rows saturates the HDD. Restore instructions are
in each script's header. Deleted rows free pages for reuse inside Postgres; the file does
not shrink without `VACUUM FULL` (intentionally avoided — it locks the table).

#### The `TABLES` list is exhaustive by contract

The prune is a single cascading `DELETE` on `Matches`, so **a child table missing from a script's
`TABLES` array is destroyed by the cascade without ever being archived** — silently, with a successful
exit code. Adding a `Match` child in `TranscendenceContext` therefore requires adding it to
`archive-old-patches.sh` *and* `archive-remaining-bulk.sh` in the same change. Cross-check against
every `HasForeignKey(x => x.MatchId)` with `OnDelete(DeleteBehavior.Cascade)` plus the two
participant-level children (`MatchParticipantItems`, `MatchParticipantRunes`). The current full set is:

```
Matches MatchParticipants MatchParticipantItems MatchParticipantRunes MatchBans
MatchTeamObjectives MatchParticipantTimelineSnapshots MatchTimelineFetchStates
MatchParticipantItemPurchases MatchParticipantSkillOrders
MatchParticipantItemEvents MatchParticipantRankContexts MatchTimelineEventPayloads
```

A listed table that does not exist on the host yet (migration not applied) fails its pre-export count,
which skips the entire patch — the failure mode is fail-closed, never prune-without-archive.

> **Known gap in existing archives.** `MatchTeamObjectives`, `MatchParticipantItemPurchases`, and
> `MatchParticipantSkillOrders` were absent from both scripts' lists before this change, so any patch
> already archived and pruned has **no NAS copy of those three tables**. They are unrecoverable for
> those patches; only the retained (unpruned) patches still hold them. Restores from older archives
> will produce `Matches` with no objectives/purchase-order/skill-order detail, which is expected, not
> corruption. `archive-remaining-bulk.sh` still carries the old seven-table list and must be updated
> before it is ever run again.

## Postgres memory tuning (declarative)

The 2026-06-21 DB-saturation incident was resolved by retuning Postgres
(`shared_buffers` 128MB→4GB, `work_mem`→24MB, `effective_cache_size`→12GB,
`maintenance_work_mem`→512MB) via `ALTER SYSTEM`, which writes `postgresql.auto.conf`
**inside the `postgres_data` volume** — so it survives a restart but is lost if the volume
is rebuilt, and is not version-controlled (#71).

The repo `compose.yml` passes these as `postgres -c` flags sourced from env vars that default
to Postgres's out-of-box values (local dev is unaffected — do **not** raise them on a small
Docker VM). The values live in the compose file / `.env`, which sit outside the
`postgres_data` volume, so the tuning survives a DB volume rebuild.

**Current prod state (verified 2026-07-24).** The **Portainer** compose's `postgres` service hardcodes
the memory values plus query/I/O instrumentation in its command:

```yaml
# in <COMPOSE_DIR>/compose.yml, postgres service
command:
  - postgres
  - -c
  - shared_buffers=4GB
  - -c
  - work_mem=24MB
  - -c
  - effective_cache_size=12GB
  - -c
  - maintenance_work_mem=512MB
  - -c
  - shared_preload_libraries=pg_stat_statements
  - -c
  - track_io_timing=on
  - -c
  - log_temp_files=65536
```

The database was deliberately restarted on 2026-07-24 and `pg_stat_statements` was created. General,
query, I/O, and temp-spill metrics are scraped by the dedicated PostgreSQL exporter. Connection pools
are budgeted at WebAPI 20 + worker 35 under `max_connections=100`; `Application Name` makes the two
callers distinguishable in `pg_stat_activity`.

The old memory values may also remain in `postgresql.auto.conf`. The command-line values are
authoritative; they can be reset from auto.conf after verifying the container flags:

```bash
docker exec transcendence-postgres psql -U postgres -d transcendence \
  -c "ALTER SYSTEM RESET shared_buffers;  ALTER SYSTEM RESET work_mem;" \
  -c "ALTER SYSTEM RESET effective_cache_size; ALTER SYSTEM RESET maintenance_work_mem;"
docker exec transcendence-postgres psql -U postgres -c 'SHOW shared_buffers; SHOW work_mem;'  # verify
```

To recreate postgres deliberately (a restart drops connections, ~seconds of downtime):

```bash
COMPOSE_DIR=/root/transcendence                                      # poll-deploy.sh COMPOSE_DIR
docker compose -p transcendence --env-file "$COMPOSE_DIR/.env" \
  -f "$COMPOSE_DIR/compose.yml" up -d postgres
```

`poll-deploy.sh` only redeploys the app containers, so it never touches PostgreSQL. **Note:** prod
PostgreSQL is `pgautoupgrade/pgautoupgrade:18.3-alpine` (PG 18),
data volume mounted at `/var/lib/postgresql` (PGDATA under `/var/lib/postgresql/18/docker`); the
repo `compose.yml` mirrors this so it's a safe deploy source.

## `postgres-performance-report.sh` — steady-state database review

This read-only report turns PostgreSQL's cumulative statistics into a repeatable tuning record. It
captures the runnable Hangfire backlog, recent completion progress, I/O and temp work, connections by
application, top statements by total and mean time, table size/growth, scan mix, vacuum health, and
large low-use indexes. It never resets statistics, runs `EXPLAIN`, drops indexes, vacuums, or changes
settings.

Reports are stored under `/var/lib/transcendence-performance` for 30 days. Each run compares table
sizes with the prior snapshot. When runnable backlog exceeds `MAX_STEADY_BACKLOG` (default 500), the
report is marked **busy** so load from ingestion/backfill is not mistaken for normal production.

Install the script and low-priority daily timer on the Docker host:

```bash
install -D -m 0755 postgres-performance-report.sh \
  /root/deploy/postgres-performance-report.sh
install -D -m 0644 transcendence-postgres-performance-report.service \
  /etc/systemd/system/transcendence-postgres-performance-report.service
install -D -m 0644 transcendence-postgres-performance-report.timer \
  /etc/systemd/system/transcendence-postgres-performance-report.timer
install -d -m 0750 /var/lib/transcendence-performance
systemctl daemon-reload
systemctl enable --now transcendence-postgres-performance-report.timer
```

Operate and review:

```bash
systemctl start transcendence-postgres-performance-report.service
systemctl status transcendence-postgres-performance-report.service
systemctl list-timers transcendence-postgres-performance-report.timer
less /var/lib/transcendence-performance/latest.md
```

Use `MAX_STEADY_BACKLOG=2000` only when deliberately redefining the busy threshold. A low `idx_scan`
count is a review signal, not permission to drop an index; compare multiple steady reports and inspect
constraints/query plans first.

## `web-perf-sweep.sh` — nightly frontend lab performance sweep

Runs the same Lighthouse runner the CI gate uses (`scripts/perf/web-lab.mjs`), but against the live
site instead of a seeded stack, and publishes Prometheus exposition instead of a pass/fail. The box
measures itself: **CI never writes to Prometheus**, so there is no receiver to expose, no
pushgateway, and no credentials in the pipeline.

The sweep writes `web_lab.prom` into a host directory that node-exporter mounts read-only as its
textfile collector directory. Metrics arrive on node-exporter's existing scrape under `job="node"`.

Why both this and the CI gate exist: the gate runs on seeded, fixed data, so a change in a number
is attributable to a commit. This runs on production data, so it tells the truth about what users
get — but its numbers move as the database grows even when nothing shipped. Neither replaces the
other.

Install on the Docker host:

```bash
install -D -m 0755 web-perf-sweep.sh /root/deploy/web-perf-sweep.sh
install -D -m 0644 transcendence-web-perf.service \
  /etc/systemd/system/transcendence-web-perf.service
install -D -m 0644 transcendence-web-perf.timer \
  /etc/systemd/system/transcendence-web-perf.timer
install -d -m 0755 /var/lib/transcendence-perf/textfile
systemctl daemon-reload
systemctl enable --now transcendence-web-perf.timer
```

The monitoring stack must also be recreated once so node-exporter picks up the textfile mount and
Prometheus the longer retention:

```bash
cd /root/transcendence-monitoring
docker compose -f compose.yml -f compose.prod.yml up -d node-exporter prometheus
```

Operate:

```bash
systemctl start transcendence-web-perf.service     # run now, ~5 min for 15 routes
journalctl -u transcendence-web-perf.service -n 50
systemctl list-timers transcendence-web-perf.timer
cat /var/lib/transcendence-perf/textfile/web_lab.prom
curl -s localhost:9100/metrics | grep transcendence_web_lab   # via node-exporter
```

Tunables are environment variables on the unit: `PERF_IMAGE`, `PERF_BASE_URL`, `PERF_SAMPLES`
(default 3), `PERF_TEXTFILE_DIR`.

**A failed sweep does not delete the previous file.** node-exporter would otherwise keep serving
stale numbers with no indication anything is wrong, so the `trn-web-lab-sweep-stale` Grafana alert
watches `transcendence_web_lab_last_success_unixtime_seconds` and fires after 48 hours. If that
alert is disabled, a dead sweep is invisible.

Routes live in `scripts/perf/routes.prod.json` and are static-only by design. Adding a dynamic
route (`/lol/champions/[championId]` and friends) requires picking an ID from the database, not by
probing URLs — under partial prerendering a missing entity still returns HTTP 200 with a
near-identical shell and echoes the identifier back into the page, so a bad ID would be measured
as a fast page and silently pass.

### Lab scores are host-specific

Lighthouse calibrates its simulated throttling to the CPU it runs on, so the numbers from this
sweep are **only comparable to other runs of this sweep**. `/lol/tierlist` measured 0.61 here and
0.98 from a developer laptop on the same commit against the same URL, with the box idle. Do not
compare these against CI gate numbers or treat them as an absolute quality score. The regression
alert, which compares each route against its own 7-day baseline on this host, is the instrument
that actually works.
