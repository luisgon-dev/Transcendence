#!/usr/bin/env python3
"""Encrypted off-host PostgreSQL backups and isolated restore drills using restic."""
import argparse
import contextlib
import fcntl
import os
import json
from pathlib import Path
import secrets
import signal
import shutil
import socket
import subprocess
import tempfile
import time


def command_timeout():
    seconds = int(os.environ.get("TRN_BACKUP_COMMAND_TIMEOUT_SECONDS", "21600"))
    if not 60 <= seconds <= 86400:
        raise ValueError("Backup command timeout must be between 60 and 86400 seconds")
    return seconds


@contextlib.contextmanager
def archive_guard():
    configured = os.environ.get("TRN_BACKUP_ARCHIVE_STATE_DIR")
    if not configured:
        yield None
        return
    directory = Path(configured)
    if not directory.is_absolute() or directory == Path("/") or not directory.is_dir():
        raise ValueError("Configure an existing absolute archive state directory")
    directory = directory.resolve()
    if directory == Path("/"):
        raise ValueError("Archive state cannot resolve to the filesystem root")
    # Match archive-old-patches.sh's lock so cron yields without changing frozen
    # IDs, resume state or chunk exports while a backup/restore is running.
    # Announce exclusive work before waiting: new analytics readers must yield while
    # existing readers drain, rather than starving a backup under continuous load.
    with contextlib.ExitStack() as locks:
        deadline = time.monotonic() + command_timeout()
        for name in ("exclusive.lock", "run.lock"):
            lock = locks.enter_context((directory / name).open("a"))
            while True:
                try:
                    fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
                    break
                except BlockingIOError:
                    if time.monotonic() >= deadline:
                        raise RuntimeError("Timed out waiting for the active archive window")
                    time.sleep(1)
        yield directory


def run(command, diagnostics=None, **kwargs):
    result = subprocess.run(command, capture_output=True, timeout=command_timeout(), **kwargs)
    if result.returncode:
        if diagnostics is not None:
            diagnostics.write_bytes((result.stdout + result.stderr)[-65536:])
            diagnostics.chmod(0o600)
        raise RuntimeError(f"{Path(command[0]).name} failed (exit {result.returncode}); inspect private backup diagnostics")
    return result.stdout


def metric(state, name):
    directory = Path(os.environ.get("TRN_RELEASE_TEXTFILE_DIR", state / "textfile"))
    directory.mkdir(parents=True, exist_ok=True)
    directory.chmod(0o755)
    path = directory / f"{name}.prom"
    temporary = path.with_suffix(".tmp")
    temporary.write_text(f"transcendence_{name}_last_success_timestamp_seconds {time.time()}\n")
    temporary.chmod(0o644)
    temporary.replace(path)


def validate_destination(repository, mount):
    if repository.startswith("/") or repository.startswith("local:"):
        if not mount:
            raise ValueError("A NAS filesystem repository requires TRN_BACKUP_MOUNT_POINT")
        mount = Path(mount).resolve()
        repository_path = Path(repository.removeprefix("local:")).resolve()
        if mount == Path("/") or not mount.is_mount() or not repository_path.is_relative_to(mount):
            raise ValueError("NAS share is not mounted or repository is outside the required mount")
        filesystem = run(["stat", "-f", "-c", "%T", str(mount)]).decode().strip()
        if filesystem not in ("nfs", "nfs4", "cifs", "smb2"):
            raise ValueError("Backup mount is not an NFS/SMB filesystem; refusing local fallback")
    elif ":" not in repository:
        raise ValueError("Use a remote restic backend or an explicitly guarded NAS mount")


def backup(state, container, reserve_bytes, archive_state=None):
    dump = state / "database.dump"
    metadata = state / "database-size.txt"
    def call(command):
        return run(command, diagnostics=state / "last-command.log")
    try:
        size = call(["docker", "exec", container, "sh", "-c",
                    'exec psql -U "${POSTGRES_USER:-postgres}" -d "${POSTGRES_DB:-${POSTGRES_USER:-postgres}}" -Atc "SELECT pg_database_size(current_database());"'])
        metadata.write_bytes(size)
        with dump.open("wb") as output, tempfile.TemporaryFile() as errors:
            application = "transcendence-backup-" + secrets.token_hex(12)
            command = ["docker", "exec", "-e", "PGAPPNAME=" + application, container, "sh", "-c",
                       'exec pg_dump -U "${POSTGRES_USER:-postgres}" -d "${POSTGRES_DB:-${POSTGRES_USER:-postgres}}" --format=custom --no-owner --no-acl']
            process = subprocess.Popen(command, stdout=output, stderr=errors)
            deadline = time.monotonic() + command_timeout()
            try:
                while process.poll() is None:
                    if time.monotonic() >= deadline or shutil.disk_usage(state).free < reserve_bytes:
                        raise RuntimeError("Backup stopped to preserve disk headroom or meet its deadline")
                    time.sleep(1)
                if process.returncode or dump.stat().st_size == 0:
                    raise RuntimeError("PostgreSQL dump failed")
            finally:
                if process.poll() is None:
                    # Killing the Docker CLI does not terminate pg_dump inside its container.
                    # Close only this run's database session before stopping its CLI transport.
                    cleanup = ["docker", "exec", "-i", container, "sh", "-c",
                               'exec psql -U "${POSTGRES_USER:-postgres}" -d "${POSTGRES_DB:-${POSTGRES_USER:-postgres}}" -At']
                    try:
                        subprocess.run(cleanup, input=("SELECT pg_terminate_backend(pid) FROM pg_stat_activity "
                                       f"WHERE application_name='{application}' AND pid<>pg_backend_pid();\n").encode(),
                                       capture_output=True, timeout=30, check=True)
                        process.wait(timeout=15)
                    except (subprocess.SubprocessError, OSError):
                        process.kill()
                        process.wait()
        # The operator initializes and supplies an encrypted off-host restic repository.
        command = ["restic", "backup", "--tag", "transcendence-database", str(dump), str(metadata)]
        if archive_state is not None:
            command += ["--exclude", str(archive_state / "run.lock"),
                        "--exclude", str(archive_state / "exclusive.lock"), str(archive_state)]
        call(command)
        call(["restic", "check"])
        call(["restic", "forget", "--tag", "transcendence-database", "--group-by", "host,paths",
             "--keep-daily", "7", "--keep-weekly", "4", "--keep-monthly", "6", "--prune"])
        metric(state, "database_backup")
    finally:
        dump.unlink(missing_ok=True)
        metadata.unlink(missing_ok=True)


def restore_drill(state, image, reserve_bytes=5 * 1024**3):
    container = "trn-restore-drill-" + secrets.token_hex(6)
    def call(command):
        return run(command, diagnostics=state / "last-command.log")
    snapshots = json.loads(call(["restic", "snapshots", "--json", "--tag", "transcendence-database", "--host", socket.gethostname()]))
    if not snapshots:
        raise RuntimeError("No database backup exists for this host")
    snapshot = max(snapshots, key=lambda s: s["time"])["id"]
    size = int(call(["restic", "dump", snapshot, str(state / "database-size.txt")]))
    if shutil.disk_usage(state).free < size + reserve_bytes:
        raise RuntimeError("Insufficient disk headroom for an isolated restore drill")
    with tempfile.TemporaryDirectory(dir=state) as scratch:
        dump = Path(scratch) / "database.dump"
        with dump.open("wb") as output:
            result = subprocess.run(["restic", "dump", snapshot,
                                     str(state / "database.dump")], stdout=output, stderr=subprocess.DEVNULL, timeout=command_timeout())
            if result.returncode or not dump.stat().st_size:
                raise RuntimeError("Could not retrieve the latest database backup")
        try:
            # Private network namespace, no published ports, no production volume or ingestion worker.
            # This clone is discarded after verification. Avoid vacuum scans and
            # frequent checkpoints competing with its bulk load/index rebuilds.
            run(["docker", "run", "-d", "--name", container, "--network", "none", "--memory", "2g",
                 "--cpus", "2", "-e", "POSTGRES_HOST_AUTH_METHOD=trust", "-e", "POSTGRES_DB=restore_drill", image,
                 "-c", "autovacuum=off", "-c", "max_wal_size=8GB", "-c", "checkpoint_timeout=15min",
                 "-c", "maintenance_work_mem=512MB"])
            for _ in range(60):
                result = subprocess.run(["docker", "exec", container, "pg_isready", "-h", "127.0.0.1", "-U", "postgres"], capture_output=True)
                if result.returncode == 0:
                    break
                time.sleep(1)
            else:
                raise RuntimeError("Restore database did not start")
            # Measure the actual Docker volume filesystem, including remote/rootless daemons.
            blocks, block_size = map(int, call(["docker", "exec", container, "stat", "-f", "-c", "%a %S", "/var/lib/postgresql"]).split())
            if blocks * block_size < size + reserve_bytes:
                raise RuntimeError("Insufficient Docker disk headroom for an isolated restore drill")
            with dump.open("rb") as source:
                result = subprocess.run(["docker", "exec", "-i", container, "pg_restore", "-U", "postgres",
                                         "-d", "restore_drill", "--exit-on-error", "--no-owner", "--no-acl"],
                                        stdin=source, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=command_timeout())
                if result.returncode:
                    raise RuntimeError("Database restore failed")
            # Check actual migrated application tables, not just archive readability.
            output = run(["docker", "exec", container, "psql", "-U", "postgres", "-d", "restore_drill", "-Atc",
                          'SELECT COUNT(*) FROM "__EFMigrationsHistory"; SELECT COUNT(*) FROM "Summoners";'])
            counts = output.decode().strip().splitlines()
            if len(counts) != 2 or int(counts[0]) == 0:
                raise RuntimeError("Restored application schema verification failed")
            metric(state, "database_restore_drill")
        finally:
            subprocess.run(["docker", "rm", "-f", "-v", container], capture_output=True, timeout=60)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=("backup", "restore-drill", "check-destination"))
    parser.add_argument("--state", type=Path, default=Path("/var/lib/transcendence-backup"))
    parser.add_argument("--container", default="transcendence-postgres")
    parser.add_argument("--postgres-image", default="postgres:18.7-alpine")
    parser.add_argument("--reserve-gib", type=int, default=5)
    args = parser.parse_args()
    def interrupted(signum, frame):
        signal.signal(signal.SIGTERM, signal.SIG_IGN)
        signal.signal(signal.SIGINT, signal.SIG_IGN)
        raise RuntimeError("Backup interrupted; cleaning up its temporary files and restore container")
    signal.signal(signal.SIGTERM, interrupted)
    signal.signal(signal.SIGINT, interrupted)
    args.state = args.state.resolve()
    os.umask(0o077)
    if not os.environ.get("RESTIC_REPOSITORY") or not os.environ.get("RESTIC_PASSWORD_FILE"):
        raise ValueError("Configure an initialized off-host restic repository and password file")
    validate_destination(os.environ["RESTIC_REPOSITORY"], os.environ.get("TRN_BACKUP_MOUNT_POINT"))
    if args.action == "check-destination":
        print("Backup destination is remote or a verified mounted NAS filesystem")
        return
    if args.reserve_gib < 1:
        raise ValueError("Keep at least 1 GiB free")
    args.state.mkdir(parents=True, exist_ok=True)
    with (args.state / "lock").open("w") as lock:
        fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        with archive_guard() as archive_state:
            if args.action == "backup":
                backup(args.state, args.container, args.reserve_gib * 1024**3, archive_state)
            else:
                restore_drill(args.state, args.postgres_image, args.reserve_gib * 1024**3)
    print("Database " + args.action + " verified", flush=True)


if __name__ == "__main__":
    main()
