# Ops scripts

## `poll-deploy.sh` — pull-based prod deploy (replaces wud)

Prod is a single Docker host on a private LAN with no public SSH and no tunnel, so a
cloud GitHub runner cannot push a deploy to it. The intended auto-updater, **wud**
(what's-up-docker), silently fails to detect new `:main` digests for these ghcr
packages — wud 8.2.2 (the latest release) reports `updateAvailable=false` even when
the registry digest has changed, so pushes never auto-deployed and each release had to
be recreated by hand (see task **P1.3b**).

`poll-deploy.sh` replaces wud for the three app services (`web`, `webapi`, `service`)
with a deterministic, **outbound-only** digest poll:

1. Resolve the current `:main` manifest digest from ghcr with an anonymous pull token
   (the packages are public).
2. Compare it to the digest the running container was pulled at.
3. If they differ, `docker compose pull` + `up -d --no-deps <svc>` (postgres/redis are
   never touched), then post a Discord notification via `ALERTS_WEBHOOK_URL`.

No inbound exposure, no CI secret, no self-hosted runner. A `flock` guard prevents
overlapping runs. Runs every ~60s via the systemd timer (≈ wud's old cadence).

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
systemctl disable --now transcendence-deploy.timer  # pause auto-deploy (e.g. during an incident)
```

Rollback is unchanged: pin a service to an immutable `:sha-<short>` tag in the compose
file and `compose up -d` it (see `docs/ARCHITECTURE.md` "Deployment & rollback").

> If wud's app-container watching is ever wanted back, note it is unreliable here; this
> poller is the source of truth. wud still works fine for the public Docker Hub
> sidecars (portainer/dozzle/grafana/prometheus).

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

## Postgres memory tuning (declarative)

The 2026-06-21 DB-saturation incident was resolved by retuning Postgres
(`shared_buffers` 128MB→4GB, `work_mem`→24MB, `effective_cache_size`→12GB,
`maintenance_work_mem`→512MB) via `ALTER SYSTEM`, which writes `postgresql.auto.conf`
**inside the `postgres_data` volume** — so it survives a restart but is lost if the volume
is rebuilt, and is not version-controlled (#71).

`compose.yml` now passes these as `postgres -c` flags sourced from env vars that default to
Postgres's out-of-box values (local dev is unaffected — do **not** raise them on a small
Docker VM). To make the tuning durable on **prod** (the 16GB host), set the values in the
Portainer stack's `stack.env` (the canonical values are listed in `.env.example`):

```sh
# in the Portainer stack env (alongside POSTGRES_PASSWORD etc.)
POSTGRES_SHARED_BUFFERS=4GB
POSTGRES_WORK_MEM=24MB
POSTGRES_EFFECTIVE_CACHE_SIZE=12GB
POSTGRES_MAINTENANCE_WORK_MEM=512MB
POSTGRES_SHM_SIZE=1gb
```

Then recreate **only** the postgres container in a quiet window (a restart drops all
connections, ~seconds of downtime):

```bash
COMPOSE_DIR=/var/lib/docker/volumes/portainer_data/_data/compose/2   # poll-deploy.sh COMPOSE_DIR
docker compose -p transcendence --env-file "$COMPOSE_DIR/stack.env" \
  -f "$COMPOSE_DIR/docker-compose.yml" up -d postgres                # picks up the new -c flags + shm_size
# verify:
docker exec transcendence-postgres psql -U postgres -c 'SHOW shared_buffers; SHOW work_mem;'
```

`stack.env` lives in the Portainer compose dir (not the `postgres_data` volume), so the
tuning now survives a DB volume rebuild. Command-line `-c` flags take precedence over
`postgresql.auto.conf`; once verified, drop the old override so there is a single source of
truth: `ALTER SYSTEM RESET shared_buffers; … RESET work_mem; …` then reload. `poll-deploy.sh`
only redeploys the app containers, so it never touches postgres — this is a one-time manual step.
