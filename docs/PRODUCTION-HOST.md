# Production host configuration

This records the November 15, 2026 production adoption of complete releases. See
[DEPLOYMENT.md](DEPLOYMENT.md) for the release protocol and recovery commands.

## Hosts and routing

| Role | Location |
| --- | --- |
| Application Docker host | Proxmox `.84`, LXC 104, `192.168.0.221` |
| Nginx Proxy Manager | Proxmox `.84`, LXC 100, `192.168.0.229` |
| Unraid NAS | `192.168.0.199`, storage interface `10.10.84.1` |
| Proxmox storage interface | `10.10.84.10` |

The application environment remains `/root/transcendence/.env`, root-owned mode
0600. It explicitly sets the web/API LAN bindings, the application proxy network
`172.19.0.0/16`, and NPM's `192.168.0.229/32`. Retain the database tuning, worker
concurrency and application credentials in that file.

The adoption baseline is private at
`/root/deploy/activation-20261115/baseline/`: original environment, Compose file,
local patch and container inspection. The older checkout has local history and
Compose changes; do not reset it or use it as a second deployment controller.

The release controller uses `/etc/transcendence-release.conf` and
`/var/lib/transcendence-release`. The legacy `transcendence-deploy.timer` is
disabled. Keep any Komodo container recreators disabled for these application
containers too.

NPM's site/API upstreams use the inner edge on `192.168.0.221:3001` and `:8082`.
The Proxmox firewall permits those ports only from its existing NPM IP set. NPM's
saved proxy-host records retain certificates 5 and 6 and use a scoped include that
overwrites client/protocol headers and strips unverified `CF-Connecting-IP`.
The edge's pinned configuration is under `/root/deploy/transcendence-edge`, with
`/etc/transcendence-edge.env` and the atomic route at
`/var/lib/transcendence-release/traffic/active.yml`. NPM's original records and
API rollback helper are retained on the NPM LXC under `/data/backups` and `/data/scripts`.

## Unraid database backups

The encrypted repository is physically on Unraid:
`/mnt/user/transcendence-backups/restic`. The dedicated share uses the array and
does not export SMB. Its NFS export allows only `10.10.84.10`, with requests mapped
to Unraid's `nobody:users` account. The share settings persist in
`/boot/config/shares/transcendence-backups.cfg`.

Proxmox mounts `10.10.84.1:/mnt/user/transcendence-backups` at
`/mnt/transcendence-backups`, using NFSv3, a hard mount, `nosuid,nodev,noexec` and
systemd automount configuration in `/etc/fstab`. LXC 104's `mp0` binds that mount
at the same path. The repository in the application LXC is therefore
`/mnt/transcendence-backups/restic`.

The LXC's private configuration files are `/etc/transcendence-backup.conf` and
`/etc/transcendence-backup.password`. A separate recovery copy of the restic
password is on Unraid at
`/mnt/user/backups/transcendence-recovery/restic.password`, under a root-only
directory outside the NFS export. Preserve it independently of the application
LXC. Application `.env` credentials were not copied to the NAS.

The legacy `/etc/cron.d/trn-archive` ran every thirty minutes with
`APPLY=1 KEEP_PATCHES=3`; its exact cron/script/checkpoint baseline is retained in
the private adoption directory. Managed adoption replaces that scheduler with
the worker's `archive-matches` job and imports its pending 500-match cohort for
patch 16.18. Completed history already occupies approximately 30 GiB on Unraid at
`/mnt/user/backup/transcendence-match-archive`; retain those files and manifests.

The existing Proxmox `nas-backup` mount supplies that exact subdirectory to LXC
104 at `/mnt/transcendence-match-archive`. Its external `transcendence_match_archive`
Docker volume binds the NAS path. `transcendence_maintenance_state` binds
`/var/lib/transcendence-archive`, preserving the original lock inode across host
backup tools and worker containers. Keep
`TRN_BACKUP_ARCHIVE_STATE_DIR=/var/lib/transcendence-archive`. Database checkpoints
own managed progress; the original local state becomes an adoption receipt.
Old-patch exports remain separate from restic database snapshot retention.

Use the repository backup and restore units. Their timers run daily at 03:00 and
Sunday at 05:00 America/Los_Angeles, with up to twenty minutes of jitter. Enable
the timers only after both the full backup and isolated restore verification
have succeeded. Check their textfile success timestamps in
`/var/lib/transcendence-perf/textfile` and query restic snapshots with the private
backup environment loaded.

## Restore workspace

At adoption, the database reported approximately 345 GiB, exceeding the free
space on the production root filesystem. A separate 500 GiB ext4 thin volume,
`hdd:vm-104-disk-1`, is LXC 104's `mp1` at
`/var/lib/transcendence-backup`, excluded from Proxmox backups. It holds temporary
dumps and the isolated restore Docker daemon; production PostgreSQL remains on
its existing volume.

`transcendence-restore-docker.service` reads
`/etc/transcendence-restore-docker.json`, uses
`/run/transcendence-restore-docker.sock`, and stores its data below the dedicated
workspace. It has a separate execution directory, no Docker bridge, no iptables
management, and a root-only socket. The restore unit requires this daemon and
sets `DOCKER_HOST` only for the restore drill. The backup unit continues using
the production Docker daemon to read PostgreSQL.

Both units require their mount points. The restore daemon stops when it is no
longer needed, and the restore service trims the scratch filesystem afterwards
to reclaim unused thin-provisioned blocks. Monitor the underlying Proxmox thin
pool as well as filesystem free space; the workspace is disposable, but the
production database is not.

## Monitoring

`config/monitoring/grafana/provisioning/alerting/deployment.yml` is installed in
the existing Grafana provisioning directory. Its four rules cover degraded
releases, stale release verification, stale NAS backups and stale restore drills.
Node exporter already mounts `/var/lib/transcendence-perf/textfile`.
`storage.yml` provisions four worker archive alerts covering failures, frozen
cohort age, a stale runner and incomplete legacy adoption.

Check actual public readiness and source/slot response headers, the recorded
release state, NAS snapshot existence, and restored application table queries
before declaring an adoption complete. A running timer or healthy container
alone is insufficient.

## Raw timeline JSON retirement

A source and live-catalog audit found no current API, frontend, repository, analytics, view,
materialized view, function or foreign-key consumer of `MatchTimelineEventPayloads`. Its only current
application access was the writer; the kill-scalar backfill served a retired modeler. The table
occupied 148,327,620,608 bytes at audit. The owner explicitly requested deletion of current and past
payloads. Collection and the obsolete backfill are removed, and an EF-generated migration drops the
table. Historical `MatchTimelineEventPayloads.csv.gz` files are removed with their manifest entries;
other archive files, counts, checkpoints and completed markers are preserved. The scoped deletion
receipt records paths, sizes and manifest metadata, without retaining payload contents.

Structured item events, purchases, skills, snapshots, rank contexts, compact facts, Build Lab counts,
and live-game snapshot JSON remain. Timeline schema v2 continues to describe its retained structured
features; no mass re-fetch is needed. The unfinished pre-retirement dump was canceled with explicit
owner approval, and the replacement full NAS backup and restore drill cover the reduced database.
