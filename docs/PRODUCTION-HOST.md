# Production adoption and validation

This records the November 2026 adoption of complete releases. Use
[DEPLOYMENT.md](DEPLOYMENT.md) for installation and recovery commands.
Keep actual host addresses, guest identifiers, mounted share locations, snapshot identifiers,
credential recovery locations and private acceptance receipts in the server's root-only runbook.
They are intentionally omitted from this public document.

## Release and storage ownership

The signed release controller owns all three application images and the blue-green traffic
route. Retire competing deployment timers or container recreators after preserving their rollback
configuration. The controller and archive/backup/restore actors share the storage maintenance gate;
heavy periodic analytics yield while exclusive maintenance runs. Public readiness and source/slot
checks continue against the retained release while a promotion waits for storage.

The application environment remains private and outside signed release bundles. Preserve original
configuration and local checkout changes during adoption; do not reset an existing checkout to
turn it into another deployment controller.

## NAS backups and application recovery

Use a dedicated, restricted NAS export for the encrypted restic repository. Persist its mount and
require the actual remote filesystem so a missing NAS cannot silently redirect backups to local
storage. Retain the repository password independently of the application host and outside the
application's NAS export.

The full production database backup passed repository integrity, retention, snapshot-content and
actual NAS placement checks. The owner also approved storing the application's private environment
inside the encrypted repository; recovery from the actual NAS matched its original bytes without
displaying credentials. Configure `TRN_BACKUP_APP_ENV_FILE` for every subsequent daily snapshot.
No plaintext application credential copy is required on the NAS export.

The first full isolated restore exposed frequent checkpoints and an automatic vacuum scan during
loading. Native PostgreSQL bulk-load settings were applied to the disposable clone without
abandoning its import. [DEPLOYMENT.md](DEPLOYMENT.md#backups-and-restore-drills) documents the settings
and the two-CPU/2 GiB runtime limit. Production database settings remain independently managed.
Complete restore success, recovered-data checks and cleanup gate timer activation.

## Restore workspace

Give production-size restore drills a dedicated disposable filesystem with sufficient free space;
monitor its underlying thin pool as well as filesystem capacity. Production database volumes must
never be attached to the drill. Require both the NAS and workspace mounts before starting.

The checked-in [runtime configuration](../config/deploy/restore-docker.json),
[daemon unit](../config/deploy/transcendence-restore-docker.service) and
[restore drop-in](../config/deploy/restore-workspace.conf) isolate data roots, sockets, mount and
network namespaces. The daemon starts its own managed containerd in the same private namespaces,
with production's containerd socket hidden. Separate data roots and `bridge=none` alone do not
provide this isolation. Preload the pinned image through the production socket's image save and
the private socket's image load because the private daemon has no network egress.

Acceptance verified namespace ownership, a real PostgreSQL container, and continued production
PostgreSQL/default-bridge operation after private-runtime shutdown. The daemon stops when no longer
needed and the disposable filesystem is trimmed after each drill.

## Managed archive acceptance

The worker's `archive-matches` job replaces legacy cron/scripts and adopts unfinished chunks into
transactional database checkpoints. Retain existing NAS exports, manifests and completed markers;
database snapshot retention never deletes those separate archive files. Match the NAS directory
ownership to the worker image UID and test a real write/fsync/read/delete operation before adoption.
Keep the same maintenance lock files across host tools and worker containers.

The initial adoption completed a 500-match legacy cohort. All twelve exported files passed
independent compressed-file SHA-256 and logical CSV counts. All 500 compact synergy-source records
and the sampled player's 263 history facts remained after raw-match pruning. Managed and legacy
pending tables both reached zero and the completed marker existed. The old scheduler and executable
were retired into the private rollback baseline.

A source and live-database audit found no current application, database view/function or foreign-key
consumer of the raw timeline JSON table. Its only application access was the writer; the old scalar
backfill served a retired modeler. After owner approval, the writer/backfill were removed and an
EF-generated migration dropped approximately 138 GiB of unused live data. Removing 621 historical
payload files and their exact manifest entries reclaimed approximately 14.2 GiB on the NAS. The
private retirement receipt retains metadata only. Structured timeline features, live-game JSON,
compact facts, player history and unrelated archive contents remain.

## Scheduling and monitoring

The versioned timers default to daily database backups at 03:00 and Sunday restore drills at 05:00
America/Los_Angeles, with up to twenty minutes of jitter. Enable them only after a complete manual
backup and production-size isolated restore pass. Heavy work shares the maintenance gate.

Provision the checked-in deployment and storage alerts. Verify actual public readiness/source,
NAS snapshot contents, restored application data, archive checkpoints and success timestamps;
a running timer or healthy container alone does not satisfy adoption acceptance. Record concrete
snapshot identifiers and private evidence locations in the server runbook.
