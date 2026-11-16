# Production adoption and validation

Use [DEPLOYMENT.md](DEPLOYMENT.md) for installation and recovery commands.
Keep actual host access details, mounted share locations, snapshot identifiers, acceptance receipts,
production measurements and credential recovery locations in the private operator runbook.

## Ownership and storage

The signed release controller owns all three application images and the blue-green traffic route.
Retire competing deployment schedulers after preserving rollback configuration. Release promotion,
managed archiving and backups share the storage maintenance gate; heavy periodic analytics yield
while exclusive maintenance runs. Foreground application work retains its own execution lanes.

The worker's managed archiver replaces legacy cron/scripts and adopts unfinished chunks into
transactional checkpoints. Preserve existing exports, manifests and completed markers. Database
backup retention must never delete separate historical archives. Validate archive writes and
checkpoint reconciliation before retiring a legacy scheduler.

## Backup policy

Use daily PostgreSQL changed-block backups and a throttled monthly physical baseline in the
existing encrypted NAS repository. Schedule the initial baseline off peak and preserve a verified
fallback until it passes. Keep entire dependency chains during retention; a missing incremental
parent must fail closed rather than trigger an unexpected full scan.

Require the real NAS mount and sufficient disposable staging space. Full recovery tests are manual,
preferably off-host, after significant changes or at quarterly review. Leave the optional recovery
host timer disabled on the application host. Production volumes must never attach to a test clone.

## Validation

Verify public readiness and release identity, encrypted repository integrity, recovered application
data, archive checkpoints and actual success timestamps. A canceled restore is not a completed
recovery test. Record concrete production evidence privately; a running timer or healthy container
alone does not demonstrate recoverability.
