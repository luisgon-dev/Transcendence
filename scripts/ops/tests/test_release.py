import contextlib
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import tarfile
import tempfile
import unittest
from unittest.mock import MagicMock, patch

ROOT = Path(__file__).resolve().parents[3]


def module(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


deploy = module('release_deploy', ROOT / 'scripts/ops/release_deploy.py')
bootstrap = module('release_bootstrap', ROOT / 'scripts/ops/release_bootstrap.py')
builder = module('release_builder', ROOT / 'scripts/ci/build-release.py')
backup = module('release_backup', ROOT / 'scripts/ops/release_backup.py')
edge = module('release_edge', ROOT / 'scripts/ops/release_edge.py')
SHA = 'a' * 40


class ReleaseFixture(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.records = self.root / 'records'
        self.records.mkdir()
        for name in deploy.COMPONENTS:
            (self.records / (name + '.json')).write_text(json.dumps(dict(
                image=f'ghcr.io/luisgon-dev/transcendence-{name}@sha256:' + 'b' * 64, revision=SHA)))
        self.output = self.root / 'output'
        builder.build(SHA, self.records, self.output)
        self.bundle = self.root / 'bundle'
        self.bundle.mkdir()
        bootstrap.extract_verified(self.output / 'release.tgz', self.bundle, 'release-' + SHA)
        self.state = self.root / 'state'
        self.env = self.root / '.env'
        self.env.write_text('')
        self.addCleanup(patch.stopall)
        patch.dict(os.environ, {}, clear=False).start()
        os.environ.pop('TRN_RELEASE_TEXTFILE_DIR', None)

    def config(self):
        manifest = deploy.load_manifest(self.bundle)
        config = {'services': {c: {'image': i, 'environment': {}} for c, i in manifest['images'].items()},
                  'networks': {'transcendence-net': {'name': 'production-network'}},
                  'volumes': {'web_image_cache': {'name': 'production-cache'}}}
        config['services']['web']['environment'] = dict(TRN_TRUSTED_PROXY_HEADERS='true',
            TRN_PUBLIC_ORIGIN='https://example.com', TRN_BACKEND_API_KEY='x' * 32)
        config['services']['webapi']['environment'] = {'Security__TrustedProxyNetworks__0': '172.16.0.0/12',
                                                                   'Auth__Jwt__Key': 'x' * 32}
        config['services'].update({'web-cache-init': {'image': manifest['images']['web']},
                                   'operational-logs-init': {'image': manifest['images']['webapi']}})
        return config


class ContractTests(ReleaseFixture):
    def test_snapshot_preserves_literal_dollars_in_runtime_credentials(self):
        engine = deploy.Engine(self.bundle, self.state, self.env)
        container = dict(Image='sha256:fixture', Config=dict(Env=['CREDENTIAL=literal$value']),
                         HostConfig=dict(PortBindings={}), Mounts=[], NetworkSettings=dict(Networks={}))
        with patch.object(engine, 'inspect', return_value=container), \
                patch.object(engine, 'compose', return_value=json.dumps(self.config())), \
                patch.object(engine, 'run', side_effect=lambda argv, *a, **k: json.dumps(self.config()) if 'config' in argv else ''):
            snapshot = engine.snapshot()
        value = json.loads(snapshot.read_text())['services']['web']['environment']['CREDENTIAL']
        self.assertEqual(value, 'literal$$value')

    def test_initial_edge_route_uses_private_ips_and_never_overwrites_live_route(self):
        path = self.root / 'active.yml'
        container = dict(State=dict(Health=dict(Status='healthy')),
                         NetworkSettings=dict(Networks={'transcendence_transcendence-net': dict(IPAddress='172.18.0.5')}))
        with patch('sys.argv', ['release_edge.py', '--traffic-file', str(path)]), \
                patch.object(edge.subprocess, 'check_output', return_value=json.dumps([container]).encode()), \
                contextlib.redirect_stdout(io.StringIO()):
            edge.main()
            original = path.read_bytes()
            with self.assertRaises(FileExistsError):
                edge.main()
        self.assertEqual(path.read_bytes(), original)
        self.assertEqual(json.loads(original)['http']['services']['web']['loadBalancer']['servers'][0]['url'],
                         'http://172.18.0.5:3000')

    def test_cached_tooling_is_revalidated_without_replacing_active_directory(self):
        manifest = deploy.load_manifest(self.bundle)
        original_inode = self.bundle.stat().st_ino
        bootstrap.validate_directory(self.bundle, manifest)
        self.assertEqual(original_inode, self.bundle.stat().st_ino)
        (self.bundle / 'scripts/ops/contextlib.py').write_text('untrusted import shadow')
        with self.assertRaisesRegex(ValueError, 'inventory'):
            bootstrap.validate_directory(self.bundle, manifest)

    def test_first_adoption_rollback_preserves_actual_bindings_and_persistent_volumes(self):
        config = self.config()
        container = dict(HostConfig=dict(PortBindings={'3000/tcp': [{'HostIp': '', 'HostPort': '3000'}]}),
                         Mounts=[dict(Type='volume', Source='/docker/volume', Name='real-cache',
                                      Destination='/app/cache', RW=True)],
                         NetworkSettings=dict(Networks={'actual-network': dict(Aliases=['web'])}))
        deploy.capture_runtime_config(config, 'web', container)
        self.assertEqual(config['services']['web']['ports'][0]['published'], '3000')
        self.assertNotIn('host_ip', config['services']['web']['ports'][0])
        self.assertTrue(config['volumes']['retained-web-0']['external'])
        self.assertEqual(config['volumes']['retained-web-0']['name'], 'real-cache')
        self.assertEqual(config['networks']['retained-web-0']['name'], 'actual-network')

    def test_staging_cannot_inherit_production_credentials_or_infrastructure(self):
        with patch.dict(os.environ, {'REDIS_CONNECTION': 'production:6379', 'POSTGRES_SHARED_BUFFERS': '4GB',
                                     'RIOT_API_KEY_LOL': 'production-secret', 'WEB_IMAGE': 'untrusted:main'}):
            environment = deploy.stage_environment(deploy.load_manifest(self.bundle)['images'])
        self.assertNotIn('REDIS_CONNECTION', environment)
        self.assertNotIn('POSTGRES_SHARED_BUFFERS', environment)
        self.assertNotIn('RIOT_API_KEY_LOL', environment)
        self.assertNotEqual(environment['WEB_IMAGE'], 'untrusted:main')

    def test_complete_bundle_round_trips_and_corruption_is_rejected(self):
        self.assertEqual(deploy.load_manifest(self.bundle)['revision'], SHA)
        (self.bundle / 'compose.yml').write_text('changed configuration')
        with self.assertRaisesRegex(ValueError, 'checksum'):
            deploy.load_manifest(self.bundle)

    def test_incomplete_or_mixed_revision_cannot_become_a_release(self):
        path = self.records / 'web.json'
        path.unlink()
        with self.assertRaisesRegex(ValueError, 'Exactly one'):
            builder.build(SHA, self.records, self.output)
        path.write_text(json.dumps(dict(image='ghcr.io/luisgon-dev/transcendence-web@sha256:' + 'b' * 64,
                                       revision='c' * 40)))
        with self.assertRaisesRegex(ValueError, 'identity'):
            builder.build(SHA, self.records, self.output)

    def test_moving_image_tag_cannot_be_used_as_a_contract(self):
        manifest = deploy.load_manifest(self.bundle)
        manifest['images']['web'] = 'ghcr.io/luisgon-dev/transcendence-web:main'
        (self.bundle / 'release.json').write_text(json.dumps(manifest))
        with self.assertRaisesRegex(ValueError, 'digest'):
            deploy.load_manifest(self.bundle)

    def test_archive_paths_links_and_duplicates_cannot_escape_inventory(self):
        for kind in ('traversal', 'link', 'duplicate'):
            with self.subTest(kind=kind):
                path = self.root / f'{kind}.tgz'
                with tarfile.open(path, 'w:gz') as archive:
                    member = tarfile.TarInfo('../escape' if kind == 'traversal' else 'file')
                    if kind == 'link':
                        member.type = tarfile.SYMTYPE
                        member.linkname = '/etc/passwd'
                    archive.addfile(member)
                    if kind == 'duplicate':
                        archive.addfile(member)
                with self.assertRaises(ValueError):
                    bootstrap.extract_verified(path, self.root / kind, 'release-' + SHA)

    def test_preflight_rejects_proxy_origin_and_credential_drift(self):
        for field, value in [('TRN_TRUSTED_PROXY_HEADERS', 'false'),
                             ('TRN_PUBLIC_ORIGIN', 'https://wrong.example'),
                             ('TRN_BACKEND_API_KEY', 'CHANGE_ME')]:
            config = self.config()
            config['services']['web']['environment'][field] = value
            with self.assertRaises(ValueError):
                deploy.validate_config(config, deploy.load_manifest(self.bundle)['images'], 'https://example.com')

    def test_blue_green_candidate_has_no_shared_service_dns_alias(self):
        engine = deploy.Engine(self.bundle, self.state, self.env)
        with patch.object(deploy.Engine, 'compose', return_value=''):
            candidate = deploy.prepare_candidate(engine, self.config(), 'production-network')
        rendered = json.loads((engine.attempt / 'candidate.json').read_text())
        self.assertEqual(set(rendered['services']), {candidate.project + '-web', candidate.project + '-webapi'})
        self.assertNotIn('webapi', rendered['services'])
        self.assertTrue(rendered['networks']['candidate']['external'])
        self.assertTrue(rendered['volumes']['web_image_cache']['external'])
        web = rendered['services'][candidate.project + '-web']
        self.assertEqual(web['ports'][0]['host_ip'], '127.0.0.1')
        self.assertIn(candidate.project + '-webapi', web['environment']['TRN_BACKEND_BASE_URL'])


class FakeEngine(deploy.Engine):
    def __init__(self, fixture, fail=None):
        super().__init__(fixture.bundle, fixture.state, fixture.env, timeout=0)
        self.events, self.fail = [], fail
        self.config = fixture.config()

    def compose(self, *args, **kwargs):
        if args[0] == 'config':
            return json.dumps(self.config)
        if args[0] == 'up':
            self.events.append('replace:' + args[-1])
            if self.fail == args[-1]:
                raise RuntimeError('injected replacement failure')
        elif 'Database__MigrateOnly=true' in args:
            self.events.append('migrate')
            if self.fail == 'migrate':
                raise RuntimeError('injected migration failure')
        return ''

    def verify_images(self):
        self.events.append('verify')

    def check_disk(self):
        pass

    def stop_migrator(self):
        self.events.append('stop-migrator')

    def snapshot(self):
        self.events.append('snapshot')
        path = self.attempt / 'previous.json'
        deploy.write_json(path, self.config)
        return path

    def rollback(self, previous, components=deploy.COMPONENTS):
        self.events.append('rollback:' + ','.join(components))
        if self.fail == 'rollback':
            raise RuntimeError('injected rollback failure')

    def wait(self, components):
        self.events.append('health:' + ','.join(components))

    def attest_running(self, components=deploy.COMPONENTS):
        self.events.append('identity:' + ','.join(components))

    def diagnostics(self):
        self.events.append('diagnostics')


class DeploymentTests(ReleaseFixture):
    def test_interrupted_rollout_can_recover_without_overwriting_its_baseline(self):
        engine = FakeEngine(self)
        prior = self.root / 'previous.json'
        deploy.write_json(prior, self.config())
        deploy.write_json(engine.attempt / 'transaction.json', dict(release=engine.release, current=None,
            previous=str(prior), pointer=None, mode='replace', base_url='https://example.com',
            api_url=None, profile='/profile', traffic_file=None))
        with contextlib.redirect_stdout(io.StringIO()):
            engine.recover()
        self.assertIn('stop-migrator', engine.events)
        self.assertIn('rollback:service,webapi,web', engine.events)
        self.assertFalse((engine.attempt / 'transaction.json').exists())
        self.assertTrue((engine.attempt / 'quarantined').exists())
        self.assertFalse((self.state / 'current.json').exists())

    def apply(self, engine, **kwargs):
        with patch.object(deploy, 'stage', side_effect=lambda *a: engine.events.append('stage')), \
                patch.object(deploy, 'http_probe') as probe, \
                contextlib.redirect_stdout(io.StringIO()):
            if engine.fail == 'probe':
                probe.side_effect = RuntimeError('wrong release served')
            engine.apply('https://example.com', '/profile', **kwargs)

    def test_success_is_committed_only_after_all_services_and_public_checks(self):
        engine = FakeEngine(self)
        self.apply(engine)
        self.assertLess(engine.events.index('stage'), engine.events.index('migrate'))
        self.assertLess(engine.events.index('migrate'), engine.events.index('replace:service'))
        self.assertEqual(json.loads((self.state / 'status.json').read_text())['phase'], 'healthy')
        self.assertEqual((self.state / 'current').resolve(), self.bundle)
        self.assertEqual((self.state / 'current.json').stat().st_mode & 0o777, 0o600)

    def test_failed_api_rolls_back_whole_app_after_preserving_diagnostics(self):
        engine = FakeEngine(self, 'webapi')
        with self.assertRaises(RuntimeError):
            self.apply(engine)
        self.assertNotIn('replace:web', engine.events)
        self.assertLess(engine.events.index('diagnostics'), engine.events.index('rollback:service,webapi,web'))
        self.assertFalse((self.state / 'current.json').exists())
        self.assertTrue((engine.attempt / 'quarantined').exists())
        self.assertEqual(json.loads((self.state / 'status.json').read_text())['deployed'], 'legacy')
        self.assertIn('transcendence_release_healthy 0', (self.state / 'textfile/release.prom').read_text())

    def test_quarantine_survives_future_polls_and_exits_as_failure(self):
        engine = FakeEngine(self, 'webapi')
        with self.assertRaises(RuntimeError):
            self.apply(engine)
        engine.events.clear()
        with self.assertRaisesRegex(RuntimeError, 'quarantined'):
            self.apply(engine)
        self.assertEqual(engine.events, [])
        self.assertEqual(json.loads((self.state / 'status.json').read_text())['phase'], 'quarantined')

    def test_migration_failure_leaves_running_apps_untouched(self):
        engine = FakeEngine(self, 'migrate')
        with self.assertRaises(RuntimeError):
            self.apply(engine)
        self.assertFalse(any(e.startswith(('replace:', 'rollback:')) for e in engine.events))

    def test_failure_after_state_commit_restores_previous_release_record(self):
        engine = FakeEngine(self)
        prior = dict(release='release-' + 'c' * 40, config=str(self.root / 'prior.json'))
        deploy.write_json(self.state / 'current.json', prior)
        deploy.write_json(Path(prior['config']), self.config())
        original = engine.status
        def status(phase, error=None):
            if phase == 'healthy':
                raise OSError('state commit interruption')
            original(phase, error)
        with patch.object(engine, 'status', side_effect=status):
            with self.assertRaises(OSError):
                self.apply(engine)
        self.assertEqual(json.loads((self.state / 'current.json').read_text()), prior)
        self.assertEqual(json.loads((self.state / 'status.json').read_text())['deployed'], prior['release'])
        self.assertIn('rollback:service,webapi,web', engine.events)

    def test_rollback_failure_is_reported_and_never_commits_success(self):
        engine = FakeEngine(self, 'webapi')
        with patch.object(engine, 'rollback', side_effect=RuntimeError('cannot restore')):
            with self.assertRaises(RuntimeError):
                self.apply(engine)
        status = json.loads((self.state / 'status.json').read_text())
        self.assertEqual(status['phase'], 'rollback_failed')
        self.assertFalse((self.state / 'current.json').exists())

    def test_blue_green_public_failure_restores_route_and_worker_before_cleanup(self):
        engine = FakeEngine(self)
        traffic = self.root / 'active.yml'
        original = b'{"previous":"route"}'
        traffic.write_bytes(original)
        candidate = FakeEngine(self)
        candidate.project = 'trn-slot-' + SHA[:12]
        original_compose = candidate.compose
        candidate.compose = lambda *a, **k: '127.0.0.1:12345' if a[0] == 'port' else original_compose(*a, **k)
        def public(base, *args, **kwargs):
            if base == 'https://example.com':
                raise RuntimeError('new route did not become ready')
        with patch.object(deploy, 'stage'), patch.object(deploy, 'prepare_candidate', return_value=candidate), \
                patch.object(deploy, 'wait_for_public', side_effect=public), contextlib.redirect_stdout(io.StringIO()):
            with self.assertRaises(RuntimeError):
                engine.apply('https://example.com', '/profile', mode='blue-green',
                             traffic_file=traffic, network='production-network')
        self.assertEqual(traffic.read_bytes(), original)
        self.assertIn('rollback:service', engine.events)
        self.assertNotIn('replace:web', engine.events)
        self.assertFalse((self.state / 'current.json').exists())

    def test_staging_failure_never_migrates_or_replaces_production(self):
        engine = FakeEngine(self)
        with patch.object(deploy, 'stage', side_effect=RuntimeError('staging failed')), contextlib.redirect_stdout(io.StringIO()):
            with self.assertRaises(RuntimeError):
                engine.apply('https://example.com', '/profile')
        self.assertEqual(engine.events, ['verify'])

    def test_preflight_mismatch_never_pulls_or_replaces_images(self):
        engine = FakeEngine(self)
        engine.config['services']['web']['environment']['TRN_TRUSTED_PROXY_HEADERS'] = 'false'
        with self.assertRaises(ValueError):
            self.apply(engine)
        self.assertEqual(engine.events, [])

    def test_http200_cannot_mask_backend_diagnostics_failure(self):
        responses = [b'html', json.dumps(dict(status='ready', release=SHA)).encode(), b'{"ok":false}']
        class Response:
            status = 200
            def __enter__(self): return self
            def __exit__(self, *args): pass
            def read(self, *args): return responses.pop(0)
        with patch.object(deploy.urllib.request, 'urlopen', side_effect=lambda *a, **k: Response()):
            with self.assertRaisesRegex(RuntimeError, 'Web-to-API'):
                deploy.http_probe('https://example.com', SHA, '/profile')

    def test_same_source_revision_cannot_mask_a_cutover_that_never_happened(self):
        responses = [b'html', json.dumps(dict(status='ready', release=SHA)).encode()]
        class Response:
            status = 200
            headers = {'x-trn-deployment': 'old-slot'}
            def __enter__(self): return self
            def __exit__(self, *args): pass
            def read(self, *args): return responses.pop(0)
        with patch.object(deploy.urllib.request, 'urlopen', side_effect=lambda *a, **k: Response()):
            with self.assertRaisesRegex(RuntimeError, 'deployment slot'):
                deploy.http_probe('https://example.com', SHA, '/profile', route='new-slot')


class BackupTests(unittest.TestCase):
    def test_cancelled_dump_terminates_only_its_tagged_database_session(self):
        with tempfile.TemporaryDirectory() as tmp:
            process = MagicMock(returncode=None)
            process.poll.return_value = None
            with patch.dict(os.environ, {'TRN_BACKUP_COMMAND_TIMEOUT_SECONDS': '60'}), \
                    patch.object(backup, 'run', return_value=b'370000000000') as commands, \
                    patch.object(backup.subprocess, 'Popen', return_value=process) as launch, \
                    patch.object(backup.subprocess, 'run', return_value=MagicMock(returncode=0)) as cleanup, \
                    patch.object(backup.time, 'monotonic', side_effect=[0, 61]), \
                    patch.object(backup.shutil, 'disk_usage', return_value=type('Disk', (), {'free': 10**12})()):
                with self.assertRaisesRegex(RuntimeError, 'deadline'):
                    backup.backup(Path(tmp), 'production-postgres', 5 * 1024**3)
            application = next(value.split('=', 1)[1] for value in launch.call_args.args[0] if value.startswith('PGAPPNAME='))
            self.assertIn(application.encode(), cleanup.call_args.kwargs['input'])
            self.assertIn(b'pg_terminate_backend', cleanup.call_args.kwargs['input'])
            self.assertNotIn(b"application_name='pg_dump'", cleanup.call_args.kwargs['input'])
            process.wait.assert_called_once_with(timeout=15)
            commands.assert_called_once()
            self.assertFalse((Path(tmp) / 'database.dump').exists())
            self.assertFalse((Path(tmp) / 'textfile/database_backup.prom').exists())

    def test_backup_and_restore_guard_exclude_archiver_and_release_lock_on_failure(self):
        with tempfile.TemporaryDirectory() as tmp, \
                patch.dict(os.environ, {'TRN_BACKUP_ARCHIVE_STATE_DIR': tmp}):
            lock_path = Path(tmp) / 'run.lock'
            with self.assertRaisesRegex(RuntimeError, 'operation failed'):
                with backup.archive_guard() as directory:
                    self.assertEqual(directory, Path(tmp).resolve())
                    for path in (lock_path, Path(tmp) / 'exclusive.lock'):
                        with path.open('a') as other:
                            with self.assertRaises(BlockingIOError):
                                backup.fcntl.flock(other, backup.fcntl.LOCK_EX | backup.fcntl.LOCK_NB)
                    raise RuntimeError('operation failed')
            for path in (lock_path, Path(tmp) / 'exclusive.lock'):
                with path.open('a') as other:
                    backup.fcntl.flock(other, backup.fcntl.LOCK_EX | backup.fcntl.LOCK_NB)

    def test_large_dump_can_exceed_thirty_minutes_within_configured_deadline(self):
        with tempfile.TemporaryDirectory() as tmp:
            process = MagicMock(returncode=0)
            process.poll.side_effect = [None, 0, 0]
            def start_dump(*args, **kwargs):
                kwargs['stdout'].write(b'complete database dump')
                kwargs['stdout'].flush()
                return process
            archive = Path(tmp) / 'archive'
            archive.mkdir()
            (archive / 'current').write_text('frozen archive resume state')
            with patch.dict(os.environ, {'TRN_BACKUP_COMMAND_TIMEOUT_SECONDS': '3600'}), \
                    patch.object(backup, 'run', return_value=b'370000000000') as commands, \
                    patch.object(backup.subprocess, 'Popen', side_effect=start_dump), \
                    patch.object(backup.time, 'monotonic', side_effect=[0, 1801]), \
                    patch.object(backup.time, 'sleep'), \
                    patch.object(backup.shutil, 'disk_usage', return_value=type('Disk', (), {'free': 10**12})()):
                backup.backup(Path(tmp), 'production-postgres', 5 * 1024**3, archive)
            process.kill.assert_not_called()
            restic_command = commands.call_args_list[1].args[0]
            self.assertIn(str(archive), restic_command)
            self.assertIn(str(archive / 'run.lock'), restic_command)
            self.assertEqual((archive / 'current').read_text(), 'frozen archive resume state')
            self.assertTrue((Path(tmp) / 'textfile/database_backup.prom').exists())
            self.assertFalse((Path(tmp) / 'database.dump').exists())

    def test_unsafe_timeout_is_rejected_before_running_commands(self):
        with patch.dict(os.environ, {'TRN_BACKUP_COMMAND_TIMEOUT_SECONDS': '0'}), \
                patch.object(backup.subprocess, 'run') as run:
            with self.assertRaisesRegex(ValueError, 'timeout'):
                backup.run(['restic', 'check'])
            run.assert_not_called()

    def test_missing_nas_mount_cannot_fall_back_to_a_local_directory(self):
        with tempfile.TemporaryDirectory() as tmp:
            with self.assertRaisesRegex(ValueError, 'not mounted'):
                backup.validate_destination(tmp + '/restic', tmp)

    def test_bind_mount_of_local_disk_cannot_masquerade_as_nas(self):
        with tempfile.TemporaryDirectory() as tmp, patch.object(Path, 'is_mount', return_value=True), \
                patch.object(backup, 'run', return_value=b'ext2/ext3\n'):
            with self.assertRaisesRegex(ValueError, 'local fallback'):
                backup.validate_destination(tmp + '/restic', tmp)

    def test_mounted_nfs_repository_and_remote_sftp_are_accepted(self):
        with tempfile.TemporaryDirectory() as tmp, patch.object(Path, 'is_mount', return_value=True), \
                patch.object(backup, 'run', return_value=b'nfs\n'):
            backup.validate_destination(tmp + '/restic', tmp)
        backup.validate_destination('sftp:backup@nas:/restic', None)

    def test_restore_drill_refuses_missing_snapshot_and_preserves_success_timestamp(self):
        with tempfile.TemporaryDirectory() as tmp, patch.object(backup, 'run', return_value=b'[]') as run:
            with self.assertRaisesRegex(RuntimeError, 'No database backup'):
                backup.restore_drill(Path(tmp), 'postgres:18.7-alpine')
            self.assertEqual(run.call_count, 1)
            self.assertFalse((Path(tmp) / 'textfile').exists())

    def test_restore_drill_refuses_to_fill_host_disk(self):
        snapshots = json.dumps([dict(id='snapshot', time='2026-11-14')]).encode()
        with tempfile.TemporaryDirectory() as tmp, patch.object(backup, 'run', side_effect=[snapshots, b'999999999999999']):
            with self.assertRaisesRegex(RuntimeError, 'disk headroom'):
                backup.restore_drill(Path(tmp), 'postgres:18.7-alpine')


if __name__ == '__main__':
    unittest.main()
