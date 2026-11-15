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

The existing `/etc/cron.d/trn-archive` invokes `/root/archive-old-patches.sh` every
thirty minutes with `APPLY=1 KEEP_PATCHES=3`. It already exports historical match
chunks to Unraid's `/mnt/user/backup/transcendence-match-archive` (approximately
30 GiB at adoption). Its installed script matches the repository version. Retain
its `/var/lib/transcendence-archive/current`, `manifest.json` and frozen database
work table together; a partially exported chunk must not be treated as complete.
Set `TRN_BACKUP_ARCHIVE_STATE_DIR=/var/lib/transcendence-archive` so database
backup/restore work shares the existing archive lock and encrypted snapshots
include the matching resume state. The old-patch retention and historical NAS
exports remain separate from restic's database snapshot retention.

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

Check actual public readiness and source/slot response headers, the recorded
release state, NAS snapshot existence, and restored application table queries
before declaring an adoption complete. A running timer or healthy container
alone is insufficient.
