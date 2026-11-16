#!/usr/bin/env python3
"""Real full + changed-block + changed-block + encrypted off-host-style recovery."""
import argparse
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


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--restic', type=Path, required=True)
    parser.add_argument('--workspace', type=Path)
    args = parser.parse_args()
    common = module('common_backup', ROOT / 'scripts/ops/release_backup.py')
    native = module('native_backup', ROOT / 'scripts/ops/release_pg_backup.py')
    container = 'trn-native-source-' + secrets.token_hex(6)
    image = 'postgres:18.7-alpine'
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
        try:
            common.run(['docker', 'run', '-d', '--name', container, '--network', 'none', '--memory', '512m', '--cpus', '1',
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
            observation = engine.restore(socket.gethostname(), second)
            assert observation['chain_length'] == 3 and observation['summoners'] == 1
            recovered = common.run(['restic', 'dump', second, str(environment)])
            assert recovered == environment.read_bytes()
            assert not list(state.glob('native-*/*'))
            assert (root / 'metrics/database_backup.prom').is_file()
            assert (root / 'metrics/database_restore_drill.prom').is_file()
            print(json.dumps(dict(full=full, first_incremental=first, second_incremental=second,
                                  full_artifact_bytes=full_bytes, incremental_artifact_bytes=latest['artifact_bytes'],
                                  restored=observation, environment_exact=True)))
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
