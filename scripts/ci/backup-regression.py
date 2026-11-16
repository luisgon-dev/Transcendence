#!/usr/bin/env python3
"""Round-trip a disposable database through an encrypted local restic test repository."""
import argparse
import importlib.util
import json
import os
from pathlib import Path
import secrets
import subprocess
import tempfile
import time

ROOT = Path(__file__).resolve().parents[2]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--restic', type=Path, required=True)
    args = parser.parse_args()
    spec = importlib.util.spec_from_file_location('release_backup', ROOT / 'scripts/ops/release_backup.py')
    backup = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(backup)
    image = 'postgres:18.7-alpine'
    container = 'trn-backup-fixture-' + secrets.token_hex(6)
    with tempfile.TemporaryDirectory() as scratch:
        scratch = Path(scratch)
        password = scratch / 'password'
        password.write_text(secrets.token_hex(32))
        password.chmod(0o600)
        application_env = scratch / 'application.env'
        application_env.write_bytes(b'BACKUP_FIXTURE_SECRET=encrypted-recovery-fixture\n')
        application_env.chmod(0o600)
        os.environ.update(RESTIC_REPOSITORY=str(scratch / 'repository'), RESTIC_PASSWORD_FILE=str(password),
                          TRN_BACKUP_APP_ENV_FILE=str(application_env),
                          RESTIC_CACHE_DIR=str(scratch / 'cache'),
                          TRN_RELEASE_TEXTFILE_DIR=str(scratch / 'metrics'),
                          PATH=str(args.restic.resolve().parent) + os.pathsep + os.environ['PATH'])
        backup.run(['restic', 'init'])
        state = scratch / 'state'
        state.mkdir()
        try:
            backup.run(['docker', 'run', '-d', '--name', container, '--network', 'none',
                        '-e', 'POSTGRES_HOST_AUTH_METHOD=trust', '-e', 'POSTGRES_DB=backup_fixture', image])
            for _ in range(60):
                result = subprocess.run(['docker', 'exec', container, 'pg_isready', '-h', '127.0.0.1', '-U', 'postgres'], capture_output=True)
                if result.returncode == 0:
                    break
                time.sleep(1)
            else:
                raise RuntimeError('Backup fixture did not start')
            sql = ('CREATE TABLE "__EFMigrationsHistory" ("MigrationId" text PRIMARY KEY);'
                   'INSERT INTO "__EFMigrationsHistory" VALUES (\'fixture\');'
                   'CREATE TABLE "Summoners" ("Id" integer PRIMARY KEY, "GameName" text);'
                   'INSERT INTO "Summoners" VALUES (1, \'Encrypted restore fixture\');')
            backup.run(['docker', 'exec', '-i', container, 'psql', '-U', 'postgres', '-d', 'backup_fixture',
                        '-v', 'ON_ERROR_STOP=1'], input=sql.encode())
            backup.backup(state, container, 1024**3)
            snapshots = json.loads(backup.run(['restic', 'snapshots', '--json', '--tag', 'transcendence-database']))
            recovered = backup.run(['restic', 'dump', snapshots[-1]['id'], str(application_env)])
            assert recovered == application_env.read_bytes(), 'Encrypted application environment recovery failed'
            backup.restore_drill(state, image, 1024**3)
            assert not (state / 'database.dump').exists()
            assert (scratch / 'metrics/database_backup.prom').is_file()
            assert (scratch / 'metrics/database_restore_drill.prom').is_file()
            print('Encrypted backup, repository check, retention and isolated PostgreSQL restore passed.')
        finally:
            subprocess.run(['docker', 'rm', '-f', '-v', container], capture_output=True, timeout=60)


if __name__ == '__main__':
    main()
