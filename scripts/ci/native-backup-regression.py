#!/usr/bin/env python3
"""Real full + changed-block + changed-block + encrypted off-host-style recovery."""
import argparse
from contextlib import contextmanager
import grp
import importlib.util
import json
import os
from pathlib import Path
import secrets
import shutil
import socket
import subprocess
import tempfile
import time

ROOT = Path(__file__).resolve().parents[2]


def module(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result



@contextmanager
def cold_restore_daemon(root, docker):
    """Start the checked-in restore configuration in a separate network namespace."""
    runtime = root / "restore-daemon"
    runtime.mkdir()
    config = json.loads((ROOT / "config/deploy/restore-docker.json").read_text())
    endpoint = "unix://" + str(runtime / "daemon.sock")
    pidfile = runtime / "daemon.pid"
    config.update({"data-root": str(runtime / "data"), "exec-root": str(runtime / "exec"),
                   "pidfile": str(pidfile), "hosts": [endpoint], "group": grp.getgrgid(os.getgid()).gr_name})
    fixture_config = runtime / "daemon.json"
    fixture_config.write_text(json.dumps(config))
    dockerd = shutil.which("dockerd")
    if not dockerd:
        raise RuntimeError("Fixture requires the isolated CI Docker Engine binary")
    # sudo's restricted PATH would otherwise pick the runner's unrelated containerd/runc.
    runtime_path = f"{Path(dockerd).parent}:/usr/sbin:/usr/bin:/sbin:/bin"
    with (runtime / "daemon.log").open("wb") as log:
        daemon = subprocess.Popen(["sudo", "-n", "env", "PATH=" + runtime_path, "unshare", "--net",
                                   dockerd, "--config-file", str(fixture_config)],
                                  stdout=log, stderr=subprocess.STDOUT)
        try:
            for _ in range(45):
                if daemon.poll() is not None:
                    raise RuntimeError("Isolated Docker daemon exited")
                if subprocess.run([docker, "--host", endpoint, "info"], capture_output=True, timeout=10).returncode == 0:
                    break
                time.sleep(1)
            else:
                raise RuntimeError("Isolated Docker daemon failed to become ready")
            yield endpoint
        except Exception:
            print((runtime / "daemon.log").read_text(errors="replace")[-4096:], flush=True)
            raise
        finally:
            if daemon.poll() is None and pidfile.is_file():
                subprocess.run(["sudo", "-n", "kill", "-TERM", pidfile.read_text().strip()], check=True, timeout=15)
            try:
                daemon.wait(timeout=60)
            finally:
                # Only the new fixture daemon directory, never existing Docker state.
                subprocess.run(["sudo", "-n", "chown", "-R", f"{os.getuid()}:{os.getgid()}", str(runtime)],
                               check=True, timeout=60)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--restic', type=Path, required=True)
    parser.add_argument('--workspace', type=Path)
    args = parser.parse_args()
    common = module('common_backup', ROOT / 'scripts/ops/release_backup.py')
    native = module('native_backup', ROOT / 'scripts/ops/release_pg_backup.py')
    container = 'trn-native-source-' + secrets.token_hex(6)
    image = native.OFFICIAL + '@' + native.CURRENT_INDEX
    with tempfile.TemporaryDirectory(dir=args.workspace) as temporary:
        root = Path(temporary)
        password = root / 'password'
        password.write_text(secrets.token_hex(32))
        password.chmod(0o600)
        environment = root / 'application.env'
        environment.write_text('FIXTURE_RECOVERY_SECRET=native-encrypted-roundtrip\n')
        environment.chmod(0o600)
        os.environ.update(RESTIC_REPOSITORY=str(root / 'repository'), RESTIC_PASSWORD_FILE=str(password),
                          RESTIC_CACHE_DIR=str(root / 'cache'), TRN_RELEASE_TEXTFILE_DIR=str(root / 'metrics'),
                          TRN_BACKUP_APP_ENV_FILE=str(environment), TRN_BACKUP_SKIP_RECENT_FULL_SECONDS='0',
                          PATH=str(args.restic.resolve().parent) + os.pathsep + os.environ['PATH'])
        common.run(['restic', 'init'])
        # The fixture acquisition is explicit and separate from offline backup and restore.
        common.run(['docker', 'pull', '--platform', native.PLATFORM, image])
        retention_tag = native.recovery_tag(native.CURRENT_INDEX)
        common.run(['docker', 'image', 'tag', image, retention_tag])
        archive = root / 'postgres-recovery.tar'
        common.run(['docker', 'image', 'save', '--output', str(archive), retention_tag])
        docker = shutil.which('docker')
        guard = root / 'docker'
        guard.write_text(
            '#!/bin/sh\n'
            'if [ "$1" = pull ]; then echo "Unexpected upstream pull" >&2; exit 91; fi\n'
            'if [ "$1" = run ] || [ "$1" = create ]; then\n'
            '  found=; for arg; do [ "$arg" != "--pull=never" ] || found=1; done\n'
            '  [ "$found" = 1 ] || { echo "Implicit pull not allowed" >&2; exit 92; }\n'
            'fi\n'
            'exec "$TRN_FIXTURE_DOCKER" "$@"\n')
        guard.chmod(0o700)
        os.environ['TRN_FIXTURE_DOCKER'] = docker
        os.environ['PATH'] = str(root) + os.pathsep + os.environ['PATH']
        try:
            common.run(['docker', 'run', '--pull=never', '--platform', native.PLATFORM, '-d', '--name', container, '--network', 'none', '--memory', '512m', '--cpus', '1',
                        '-e', 'POSTGRES_HOST_AUTH_METHOD=trust', '-e', 'POSTGRES_DB=backup_fixture', image,
                        '-c', 'summarize_wal=on', '-c', 'checkpoint_timeout=30s', '-c', 'checkpoint_completion_target=0.1'])
            for _ in range(60):
                ready = subprocess.run(['docker', 'exec', container, 'pg_isready', '-h', '127.0.0.1',
                                        '-U', 'postgres', '-d', 'backup_fixture'], capture_output=True)
                if ready.returncode == 0:
                    break
                time.sleep(1)
            else:
                raise RuntimeError('Native fixture database did not start')
            state = root / 'state'
            engine = native.NativeBackups(common, state, container, 256 * 1024**2)
            engine.sql('''CREATE TABLE "__EFMigrationsHistory" ("MigrationId" text PRIMARY KEY);
                INSERT INTO "__EFMigrationsHistory" VALUES ('native-fixture');
                CREATE TABLE "Summoners" ("Id" integer PRIMARY KEY, "GameName" text);
                INSERT INTO "Summoners" VALUES (1,'before');
                CREATE TABLE payload (id integer PRIMARY KEY, data text);
                INSERT INTO payload SELECT i, repeat(md5(i::text),20) FROM generate_series(1,30000) i;''')
            full = engine.backup(full=True)
            full_bytes = json.loads((state / 'last-native-backup.json').read_text())['artifact_bytes']
            engine.sql('''UPDATE "Summoners" SET "GameName"='after-incremental';
                UPDATE payload SET data='updated' WHERE id=1;
                INSERT INTO payload VALUES (30001,'new');''')
            first = engine.backup()
            engine.sql('DELETE FROM payload WHERE id=2;')
            second = engine.backup()
            latest = json.loads((state / 'last-native-backup.json').read_text())
            assert latest['parent'] == first and latest['artifact_bytes'] < full_bytes
            acceptance = root / 'acceptance.sql'
            acceptance.write_text('''DO $$ BEGIN
                IF (SELECT "GameName" FROM "Summoners" WHERE "Id"=1) <> 'after-incremental'
                    OR (SELECT data FROM payload WHERE id=1) <> 'updated'
                    OR NOT EXISTS (SELECT 1 FROM payload WHERE id=30001)
                    OR EXISTS (SELECT 1 FROM payload WHERE id=2)
                THEN RAISE EXCEPTION 'Native incremental data did not recover'; END IF;
                END $$;''')
            os.environ['TRN_RESTORE_ACCEPTANCE_SQL_FILE'] = str(acceptance)
            source_host = os.environ.get('DOCKER_HOST')
            source_context = os.environ.pop('DOCKER_CONTEXT', None)
            try:
                with cold_restore_daemon(root, docker) as endpoint:
                    os.environ['DOCKER_HOST'] = endpoint
                    # The cold daemon must refuse a restore until the exact artifact is imported.
                    try:
                        engine.restore(socket.gethostname(), second)
                    except RuntimeError as error:
                        assert 'Exact PostgreSQL image is unavailable locally' in str(error), error
                    else:
                        raise AssertionError('Cold restore unexpectedly succeeded with no image')
                    assert not (state / 'last-native-restore.json').exists()
                    assert not list(state.glob('native-restore-*'))
                    common.run(['docker', 'image', 'load', '--input', str(archive)])
                    observation = engine.restore(socket.gethostname(), second)
            finally:
                if source_host is None:
                    os.environ.pop('DOCKER_HOST', None)
                else:
                    os.environ['DOCKER_HOST'] = source_host
                if source_context is not None:
                    os.environ['DOCKER_CONTEXT'] = source_context
            assert observation['chain_length'] == 3 and observation['summoners'] == 1
            recovered = common.run(['restic', 'dump', second, str(environment)])
            assert recovered == environment.read_bytes()
            assert not list(state.glob('native-*/*'))
            assert (root / 'metrics/database_backup.prom').is_file()
            assert (root / 'metrics/database_restore_drill.prom').is_file()
            print(json.dumps(dict(full=full, first_incremental=first, second_incremental=second,
                                  full_artifact_bytes=full_bytes, incremental_artifact_bytes=latest['artifact_bytes'],
                                  restored=observation, environment_exact=True,
                                  cold_cache_refused=True, restored_from_offline_archive=True)))
        except Exception:
            # This repository/database contains only synthetic fixture data.
            diagnostic = root / 'state/last-command.log'
            if diagnostic.exists():
                print(diagnostic.read_text(errors='replace')[-4096:], flush=True)
            raise
        finally:
            subprocess.run(['docker', 'rm', '-f', '-v', container], capture_output=True, timeout=60)
            os.environ.pop('TRN_RESTORE_ACCEPTANCE_SQL_FILE', None)


if __name__ == '__main__':
    main()
