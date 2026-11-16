# Complete application releases

The recommended production path is GitHub Actions → signed release bundle → host
pull → isolated staging → migration → application rollout → public verification.
Docker Compose remains the runtime. PostgreSQL and Redis are never recreated by
an application deployment. The existing per-image poller remains available during
host migration; install exactly one automatic deployment controller.

## Release contract

Every main build produces all three application images. CI records their exact
OCI digests and source revision, assembles `release.tgz`, then starts those same
artifacts in a disposable production-mode Compose stack. It migrates a fresh
PostgreSQL database, seeds a stored profile, starts Redis/API/web, and exercises
the home page, content readiness, backend diagnostics, profile page and public
BFF database search. The worker runs only with `Database:MigrateOnly=true`, so
staging never starts ingestion or recurring jobs.
Staging explicitly pulls every pinned application image before migration, even
on a fresh publication runner. PostgreSQL readiness checks its final TCP listener,
not the temporary initialization server. CI preserves bounded logs/state on staging
failure without uploading resolved environment files or credentials.

Only successful staging can publish a complete GitHub Release named
`release-<full source revision>`. Its assets are:

- `release.json`: schema version, source revision, all three image digests, and
  checksums of the Compose configuration and deployment tooling.
- `release.tgz`: that manifest, configuration, agent, backup tooling and seed.
- `release.sigstore.json`: keyless cosign bundle covering the entire archive.

The release remains a draft until all assets are uploaded. Legacy `:main` aliases
advance afterwards for hosts still migrating. Coordinated deployments never
resolve those aliases. Existing completed releases must not be edited or reused
for different image digests; publish another commit instead.

The installed bootstrap verifies the archive before extraction or code execution,
requiring the repository's `docker-images.yml` workflow on `refs/heads/main` and
GitHub's OIDC issuer. It rejects path traversal, links, duplicate archive entries,
unexpected files, checksum drift and unsupported protocols. The versioned agent
also verifies each application image's signature and source identity.

## Adopt on the production host

Prerequisites: Linux, Python 3.12+, Docker Engine/Compose v2, outbound GitHub/GHCR,
Sigstore and application CDN access, and sufficient Docker disk headroom. Keep
the existing `/root/transcendence/.env` root-owned mode 0600. This file stays
outside release bundles. Preserve its actual port bindings, database tuning,
proxy settings and credentials when reviewing the new configuration.

Wait for the first complete release to appear in GitHub before installation.
Set `WEB_BIND_ADDRESS` and `API_BIND_ADDRESS` explicitly in the application `.env`
before adoption. With Nginx Proxy Manager in another LXC, use the Docker LXC's
private LAN address; loopback is reachable only inside the Docker LXC. See
`config/deploy/npm.env.example`. Also configure `TRUSTED_PROXY_NETWORK_1` as the
NPM LXC's exact IP/CIDR, while network 0 covers the BFF/application Docker subnet.
From an updated repository checkout:

```bash
git pull --ff-only
install -m 0600 config/deploy/release.env.example /etc/transcendence-release.conf
# Review /etc/transcendence-release.conf and the existing application .env.
bash scripts/ops/install-release-deployer.sh
systemctl start transcendence-release.service
journalctl -u transcendence-release.service -n 100 --no-pager
cat /var/lib/transcendence-release/status.json
```

The installer disables future legacy timer polls; an in-progress legacy run can
finish under the shared lock. The new timer checks every five minutes after the
previous run completes. `flock` serializes scheduled deployment, direct retries
and the old updater. Keep Komodo/other automatic recreators disabled for these
same containers. Komodo can invoke this bootstrap as the sole controller instead
of enabling the timer; it must not independently update the three images.

The bootstrap is intentionally small and stable. Bundle changes automatically
update the versioned deployment agent; a future bootstrap protocol change needs
an explicit reinstall. No inbound SSH or GitHub runner on the Docker host is
required.

## Deployment behavior

1. Validate the complete image/configuration contract, trusted proxy settings,
   public origin and credentials; check free space on both state and Docker disks
   (default reserve 5 GiB, `TRN_RELEASE_MIN_FREE_GIB`, minimum 1 GiB).
2. Verify and pull all three digest-pinned images, then repeat the capacity check.
3. Test the bundle again in a separate, resource-limited staging project. Each
   network/volume is unique; production environment variables cannot override
   the staging secrets, Redis address, feature flags or PostgreSQL tuning.
4. Capture a private rollback configuration, actual running image IDs, ports,
   mounts, environment and networks. Retention tags keep previous images local.
5. Prepare log/cache mount ownership and run the migration-only worker once.
   Normal API/worker containers have automatic migration disabled.
6. Replace worker → API → web, health-check each, verify their actual image IDs,
   then probe the public site and expected web revision through the real proxy.
   The public API readiness route must also return the expected `X-TRN-Release`.
7. Commit `current.json` and the `current` tooling symlink only after validation.

The public probe retries transient asset warmup within a bounded deadline.
Readiness includes actual optimized champion/item/rune/splash output. A 200 from
backend diagnostics is insufficient unless its JSON also says `ok: true`.

Application failures preserve bounded private diagnostics before rolling back the
whole application to its captured configuration. Database changes are retained.
**Migrations must support both the previous and new application versions**:
expand first, migrate data separately, remove obsolete schema in a later release
after rollback support expires. Destructive/hot-table migrations require a
separate reviewed maintenance plan; image rollback never runs EF `Down`.

A failed release stays quarantined across polls and reports a nonzero exit and
degraded metrics. A newer verified release can proceed. Inspect the private
`attempts/<release>/` directory before an explicit retry:

```bash
systemctl stop transcendence-release.timer
release=release-REPLACE_WITH_FULL_REVISION
bundle=/var/lib/transcendence-release/releases/$release
python3 "$bundle/scripts/ops/release_deploy.py" retry --bundle "$bundle"
systemctl start transcendence-release.timer
```

Each rollout also writes a durable `transaction.json` before production changes.
Graceful interruption stops its named migration container and restores the captured
application baseline. If the process/host is killed outright, future polls stop
and preserve the original snapshot instead of treating a mixed stack as the new
baseline. Use the retained bundle's recovery command before retrying or deploying
another release:

```bash
python3 "$bundle/scripts/ops/release_deploy.py" recover --bundle "$bundle"
```

Recovery uses the transaction's saved routes, public origins and configuration,
and leaves the interrupted release quarantined for review. Stop operations allow
time for rollback; a hard kill requires this explicit recovery step. PostgreSQL
and Redis are never stopped by recovery.

Pass the matching `--mode`, `--traffic-file`, `--base-url`, `--api-url`, `--env-file` and
`--network` when using nondefault deployment settings. Direct retries acquire
the deployment locks too. Never delete quarantine automatically on a timer.

For an application rollback after a release was accepted, set `TRN_RELEASE_PIN`
in `/etc/transcendence-release.conf` to a retained complete release, then start the
service. The pin persists across polls until an operator removes it. Confirm
schema backward compatibility first. Recovery of database contents is a distinct
operation using backups.

Successful rollouts retain three successful configurations and a bounded recent
failure history by default (`TRN_RELEASE_RETAIN`, 2–10). State and resolved
configuration contain credentials and remain mode 0600 under a private directory.
Only owned retention tags/old slot containers are eligible for cleanup; there is
no global image/volume prune.

## Optional blue-green web/API routing

Replacement deployment is the default. Blue-green requires explicit adoption of
the inner Traefik edge because the existing TLS proxy's configuration is not
owned by this repository. Install and validate this route before enabling it:

```bash
systemctl stop transcendence-release.timer
install -d -m 0755 /var/lib/transcendence-release/traffic
python3 scripts/ops/release_edge.py
# Set EDGE_TRUSTED_PROXY_CIDRS to the actual TLS proxy source CIDRs.
docker compose -p transcendence-edge -f config/deploy/compose.yml up -d
```

The preparation helper targets the current containers' private IPs and refuses to
overwrite an existing route. Keep the release timer paused while preparing and
testing this edge so a replacement cannot invalidate those initial IPs. After
testing NPM's new upstream and enabling blue-green mode, restart the release timer.
Subsequent releases use unique slot DNS names automatically.

The inner edge uses private host ports 3001/8082 by default. Test it, then point
the existing TLS proxy to those ports or its internal Docker service. Retain TLS,
trusted-header rewriting and the original public origins. Set in the release
configuration:

```dotenv
TRN_RELEASE_MODE=blue-green
TRN_RELEASE_TRAFFIC_FILE=/var/lib/transcendence-release/traffic/active.yml
TRN_RELEASE_NETWORK=transcendence_transcendence-net
```

For NPM in a separate LXC, configure `RELEASE_EDGE_BIND_ADDRESS` as the Docker
LXC's private LAN IP and `EDGE_TRUSTED_PROXY_CIDRS` as the NPM LXC's IP/32.
Keep the existing certificate/SSL settings in NPM. Its website proxy host forwards
HTTP to `Docker-LXC-IP:3001`; the separate API proxy host forwards to `:8082`.
Permit those ports only from the NPM LXC. The browser continues using the same
HTTPS origins. No NPM administrator token is needed for later release switches.

The candidate API trusts at most two configured forwarding hops for NPM → inner
edge. Normal replacement deployment defaults to one (`FORWARDED_HEADER_LIMIT`);
only known proxy networks may advance either hop. Keep network CIDRs narrow.
NPM must overwrite client IP/protocol headers; remove an incoming
`CF-Connecting-IP` unless the edge has verified a Cloudflare sender. The BFF
continues forwarding one validated client address to the API.

Candidates use unique service names and DNS aliases on the existing application
network; warming a candidate cannot redirect the active web process's API calls.
Cache/log volumes stay shared and persistent. The candidate is verified on an
ephemeral loopback port before worker replacement and atomic traffic-file
switching. Public verification confirms the expected source revision and the
slot's `X-TRN-Deployment` response header, including adoption at the same source
revision. NPM must preserve that response header.
Failure restores the previous traffic file and worker; a successful cutover stops
the old web/API slot. The edge supplies stable monitoring aliases after legacy
containers stop. The file must have a `.yml` extension for Traefik's watcher.

To return to replacement mode, first restore the normal containers and explicitly
route the edge to them during a maintenance window; remove/re-adopt the managed
release state only after verifying that route. The agent rejects an implicit
mode change that would leave traffic attached to an unmanaged slot. Blue-green
does not provide host or database failover; that requires additional hosts.

## Backups and restore drills

Install `restic` on the host and configure an **off-host** encrypted repository.
Initialize it explicitly, store its password independently of the Docker host,
and install `config/deploy/backup.env.example` as the root-only
`/etc/transcendence-backup.conf` with your provider credentials and password-file
path. After approving credential storage in the encrypted repository, set
`TRN_BACKUP_APP_ENV_FILE=/root/transcendence/.env` to include the private application environment
in every database snapshot. The file must exist at an absolute path with no group/other permissions;
a missing or public file fails the backup before dumping. Recover it with
`restic dump SNAPSHOT_ID /root/transcendence/.env` into a mode-0600 file on the replacement host.
Keep the repository password in an independent recovery location. Also preserve cluster roles;
these database backups contain application data/schema without
ownership/ACL restoration.

For the Unraid NAS, a dedicated NFS/SMB backup share is supported without granting
the application LXC NAS-administrator SSH access. Export a dedicated share with
access restricted to the mounting client's private IP. Mount it persistently at
`/mnt/transcendence-backups`; if the Docker LXC cannot mount NFS itself, mount on
the LXC host and bind that mounted share into the container. Ensure the LXC's
mapped user can write it. `backup.env.example` uses a restic subdirectory there.
The backup code requires both a real mount point and an NFS/SMB filesystem, so an
unmounted share or a bind of its empty local directory cannot fill the Docker disk.

After installing the backup configuration/password file, initialize the repository
in an operator shell before enabling the services:

```bash
set -a
. /etc/transcendence-backup.conf
set +a
python3 /var/lib/transcendence-release/current/scripts/ops/release_backup.py check-destination
restic init
```

Keep the repository password in an independent recovery location. A dedicated
restricted SFTP account or another remote restic backend is also supported; use
the NAS mount guard only for filesystem repositories. NAS-side snapshots, when
available, can protect retained backups independently of the application host.

After one successful managed release, install the units:

```bash
for unit in transcendence-backup.service transcendence-backup.timer transcendence-restore-drill.service transcendence-restore-drill.timer; do
  install -m 0644 "scripts/ops/$unit" "/etc/systemd/system/$unit"
done
systemctl daemon-reload
systemctl start transcendence-backup.service
systemctl start transcendence-restore-drill.service
# Enable schedules after both manual checks succeed.
systemctl enable --now transcendence-backup.timer transcendence-restore-drill.timer
```

The daily dump streams to a private temporary file with a deadline and disk-space
guard, uploads with restic, checks repository integrity and applies tag-scoped
7-daily/4-weekly/6-monthly retention. Local dumps are removed after every outcome.
The weekly drill selects one snapshot for this host, retrieves that exact dump,
restores a fresh PostgreSQL 18 container with no network or published ports, then
queries real migration and summoner tables. It checks disk headroom first and
removes only its own disposable container/volume afterwards. Successful backup
and restore timestamps advance only after the entire operation succeeds.
Defaults are daily at 3 AM Pacific and Sunday at 5 AM Pacific, with up to twenty
minutes of jitter. Customize them with systemd timer overrides.

Large production databases need budgets based on their measured dump and index
rebuild times. `TRN_BACKUP_COMMAND_TIMEOUT_SECONDS` bounds each backup/restic/restore
command (default six hours, accepted range 60–86400 seconds). The units separately
bound the complete backup to twelve hours and restore drill to eighteen hours;
adjust those with systemd overrides when changing command budgets. A restore drill
waits for an already-running scheduled backup before starting. Disk headroom checks,
cleanup, repository validation and successful restored table queries remain required
regardless of the timeout. Enable schedules only after a complete production-size
backup and restore drill pass.

The disposable restore PostgreSQL instance keeps its two-CPU/2 GiB limit and uses native
bulk-load settings: `autovacuum=off`, `max_wal_size=8GB`, `checkpoint_timeout=15min`, and
`maintenance_work_mem=512MB`. These avoid redundant scans and frequent checkpoints while loading
data and rebuilding indexes, following [PostgreSQL's bulk-load guidance](https://www.postgresql.org/docs/18/populate.html).
They apply only to the discarded drill container; production PostgreSQL retains its own settings.
Durability settings and all dump/restore error checks remain enabled.

For a large restore drill on the application machine, use the checked-in private-runtime templates:
`config/deploy/restore-docker.json`, `config/deploy/transcendence-restore-docker.service`, and
`config/deploy/restore-workspace.conf`. Adapt mount paths, install the JSON as
`/etc/transcendence-restore-docker.json`, the service under `/etc/systemd/system`, and the drop-in as
`/etc/systemd/system/transcendence-restore-drill.service.d/workspace.conf`; reload systemd afterwards.
The private network namespace and private writable `/run/containerd` are required alongside the
separate data/exec roots. Let that Docker daemon manage its own containerd in the same namespaces.
The preload commands deliberately use the production Unix socket to pull/save the pinned PostgreSQL
image, then load it through the private Unix socket; the restore daemon has no network egress.
Validate the configured units, execute a real disposable PostgreSQL container, and confirm the
production bridge/containers remain intact. See [the host runbook](PRODUCTION-HOST.md#restore-workspace).

On an adopted host, also set `TRN_RELEASE_MAINTENANCE_DIR` in the release-controller configuration
to that same initialized local state directory. The controller holds the exclusive storage gate
across image preparation, isolated staging, migrations and rollout. A busy backup/restore/archive
defers promotion without quarantining the release; the next timer poll retries. It independently
attests and probes the retained running release, so `maintenance` status still reports a healthy
current deployment when that verification succeeds. An unchanged-release health check never waits
for the storage gate. `TRN_RELEASE_MAINTENANCE_WAIT_SECONDS` bounds waiting for existing shared
analytics to drain (default 30, range 0–1200 seconds); new heavy work yields during that wait.
Enable this only after both `exclusive.lock` and `run.lock` exist, including on a fresh host.

When the old-patch archive job is installed, set
`TRN_BACKUP_ARCHIVE_STATE_DIR=/var/lib/transcendence-archive` to its actual state
directory. Backups and restore drills announce exclusive work through `exclusive.lock`, then
take the existing `run.lock`. New heavy analytics work yields while existing work drains; the
managed archive actor uses the same protocol. Legacy cron also yields on `run.lock`. The
backup captures the resume state alongside the database in the same encrypted
snapshot while that lock is held. Restore both the database and the saved archive
state together before resuming pruning; never drop a nonempty frozen work table
or invent a replacement state file.

Archived match chunks already live separately on Unraid under
`/mnt/user/backup/transcendence-match-archive`. Database dumps contain only data
still in PostgreSQL; they do not recreate records already pruned into archives.
Preserve every completed chunk, its manifest and any older flat patch archive.
The new backup retention applies only to the encrypted restic database snapshots
and does not remove these historical match exports.

## Managed match archival

Use the application worker's `archive-matches` job for new/adopted installations. Its database
checkpoint follows deployments and database backups, and its dedicated queue appears alongside the
other jobs in existing admin operations. The legacy host cron is not a second scheduler.

1. Pause the old archive cron and preserve its scripts, `current`, `manifest.json` and frozen work
   table. Never discard a partially pruned chunk. Preserve every existing NAS archive directory.
2. Mount the existing NAS match archive into the application host at
   `/mnt/transcendence-match-archive`. For an LXC, bind the already-mounted Proxmox NFS subdirectory
   into the LXC. Grant the worker's actual `APP_UID` native directory ownership or effective write
   permissions on archive parent directories and its pending chunk. Test a real write/fsync/read
   through NFS as that UID: NAS-side ACL display alone is insufficient. The worker needs filesystem access, not a NAS administrator SSH key.
3. Before deploying the new complete release, create these external volumes on the Docker host:

   ```bash
   docker volume create --driver local --opt type=none --opt o=bind \
     --opt device=/mnt/transcendence-match-archive transcendence_match_archive
   docker volume create --driver local --opt type=none --opt o=bind \
     --opt device=/var/lib/transcendence-archive transcendence_maintenance_state
   ```

   Set `MATCH_ARCHIVE_VOLUME_EXTERNAL=true`, `MAINTENANCE_STATE_VOLUME_EXTERNAL=true`,
   `MATCH_ARCHIVE_ENABLED=true` and the desired three-patch policy in the private application `.env`.
   Keep `TRN_BACKUP_ARCHIVE_STATE_DIR=/var/lib/transcendence-archive` in the backup configuration.
   The maintenance volume binds that same directory/inode, so an already-running backup continues
   excluding archive and heavy periodic analytics work. The initializer prepares its file ownership.
4. Deploy through the signed complete-release controller. The additive EF migration creates
   `MatchArchiveChunks`/`MatchArchiveEntries`. A legacy frozen cohort makes the job wait for explicit
   adoption. NAS mount validation, source retention and normal serving stay in effect while waiting.
   This release also retires the unused raw JSON event table: stop the old worker first, then apply
   the generated retirement migration. Retain a release that does not write that table as the
   rollback floor; older writer images require an explicit compatibility repair. The WebAPI has no
   reader of this table and can continue serving during the worker transition.
5. After the backup/restore gate is free, invoke the installed release's service configuration once:

   ```bash
   config=$(python3 -c 'import json; print(json.load(open("/var/lib/transcendence-release/current.json"))["config"])')
   docker compose -p transcendence -f "$config" run --rm --no-deps --pull never \
     -e ArchiveRetention__AdoptLegacyOnly=true service
   ```

   This imports the exact legacy frozen IDs and NAS path, validates any already-verified manifest,
   and preserves partial exports. It renames `current` to an adoption receipt only after the durable
   database checkpoint commits. Repeating the command does not create another cohort. The old
   `_patch_archive_pending` table is drained transactionally with imported IDs; do not drop it manually.
6. Verify one real managed export/prune window, complete manifest hashes/counts, surviving compact
   facts, and the queued job/metrics. Keep legacy cron disabled. Restore a database checkpoint together
   with its matching archive state and immutable NAS files. A backup taken before this additive
   migration can be restored with its retained prior release, then migrated/adopted normally.
   For a bounded acceptance run, use the same Compose command with
   `-e ArchiveRetention__RunOnceOnly=true` in place of `AdoptLegacyOnly`. It takes the normal gate,
   database ownership lock and budgets, then exits without starting another Hangfire server.

The archive is direct `COPY` → gzip on the NAS. It counts CSV records while streaming, including
quoted newlines; it does not run a separate large row-count query before each export. Completed files
are verified on readback and checkpointed per table, so a timed-out later export can resume. Pruning
is bounded and waits for compact synergy coverage. Promotion of a patch back into retention prevents
later prune batches. Restore CSV into the matching release's schema in parent-before-child order,
using the CSV header's explicit column list and manifests. Reconcile primary keys before importing
overlapping historical captures. The PostgreSQL integration test reconstructs a deleted cohort from
these files and checks that the independent compact facts survive.

Row pruning plus autovacuum reuses database pages; it does not promise a smaller disk file. A future
partition migration must account for foreign keys, query pruning, backfill disk space and rollback.
It is not performed by these maintenance jobs. Never substitute `VACUUM FULL` for the bounded policy
during normal serving.

## Monitoring and network requirements

The release and backup services write textfile metrics into the existing
`/var/lib/transcendence-perf/textfile` collector directory. Override with
`TRN_RELEASE_TEXTFILE_DIR` only if node-exporter mounts that same directory.
Sync `config/monitoring/grafana/provisioning/alerting/deployment.yml` using the
existing monitoring runbook. It alerts on a rollout degraded for ten minutes,
verification stale for twenty minutes, backup older than 36 hours, or restore
drill older than eight days. Optional services do not alert before their first
successful metric; verify that first run manually during installation.

`status.json` records desired/deployed release, phase and last verification time.
Quarantine and later idle polls cannot turn a failed rollout green. Missing or
manually changed container images and a failed public probe remain degraded.

Staging requires normal application CDN connectivity from its containers. Next's
image optimizer pins outbound destinations for SSRF protection; generic Node proxy
settings do not cover every optimizer request. Configure the network's approved
egress route when required. Keep TLS verification and image-host/IP validation
enabled. A CDN failure must fail content readiness, not silently skip the gate.

The old poller and its break-glass runbook remain in `scripts/ops/README.md` for
hosts that have not migrated. A merge alone neither installs host services nor
switches the user's existing proxy or backup destination.

The [production host configuration](PRODUCTION-HOST.md) records the adopted LXC
addresses, dedicated Unraid repository, recovery password location and isolated
restore workspace. Verify live state before reusing that dated host inventory.
