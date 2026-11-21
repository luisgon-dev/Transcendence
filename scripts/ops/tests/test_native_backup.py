import importlib.util
import io
import json
import os
from pathlib import Path
import tarfile
import tempfile
import unittest
from unittest.mock import Mock, patch

spec = importlib.util.spec_from_file_location('native_backup', Path(__file__).parents[1] / 'release_pg_backup.py')
native = importlib.util.module_from_spec(spec)
spec.loader.exec_module(native)


def entry(number, parent=None, time=None):
    return dict(id=f'{number:064x}', time=time or f'2026-{number:02d}-01T00:00:00Z', tags=[
        native.TAG, 'trn-verified', 'trn-system:fixture',
        'trn-kind:' + ('full' if parent is None else 'incremental'),
        'trn-parent:' + (f'{parent:064x}' if parent is not None else 'none')])


class NativeImageTests(unittest.TestCase):
    index = native.CURRENT_INDEX
    child = native.LEGACY_CHILDREN[index, native.PLATFORM]

    def fixture(self):
        info = dict(image="postgres@" + self.index, platform=native.PLATFORM)
        official = native.OFFICIAL + "@" + self.index
        root = dict(Descriptor=dict(digest=self.index, mediaType=native.OCI_INDEX),
                    RepoDigests=["my.registry/postgres@" + self.index, info["image"]])
        child = dict(Descriptor=dict(digest=self.child, mediaType=native.OCI_MANIFEST, annotations={
            "org.opencontainers.image.url": "https://hub.docker.com/_/postgres",
            "org.opencontainers.image.source": "https://github.com/docker-library/postgres.git#rev:path",
        }), Os="linux", Architecture="amd64")
        return info, official, root, child

    def test_exact_cached_index_and_platform_are_used_not_first_repo_digest(self):
        info, official, root, child = self.fixture()
        with tempfile.TemporaryDirectory() as tmp:
            engine = native.NativeBackups(Mock(), tmp)
            with patch.object(engine, "call", side_effect=[json.dumps(root), json.dumps(child)]) as call:
                self.assertEqual(engine.cached_image(info), official)
                self.assertEqual(call.call_args_list[0].args[0][:4], ["docker", "image", "inspect", official])
                self.assertEqual(call.call_args_list[1].args[0][:6],
                                 ["docker", "image", "inspect", "--platform", native.PLATFORM, official])
            with patch.object(native.os, "getuid", create=True, return_value=0), \
                 patch.object(native.os, "getgid", create=True, return_value=0):
                self.assertIn("--pull=never", engine.tool(official, tmp, ["pg_verifybackup"]))

    def test_rejects_untrusted_references_and_unknown_legacy_indexes_without_docker(self):
        info, _, _, _ = self.fixture()
        for invalid in (dict(info, image="postgres:18.7-alpine"),
                        dict(info, image="mirror.example/postgres@" + self.index),
                        dict(info, image="postgres@sha256:" + "1" * 64),
                        dict(info, image_manifest="sha256:" + "1" * 64),
                        dict(info, platform="linux/arm64")):
            with self.subTest(invalid=invalid), self.assertRaises(ValueError):
                native.image_identity(invalid)

    def test_rejects_wrong_repository_index_and_child(self):
        for fault in ("repository", "index", "child", "architecture", "annotation", "unavailable"):
            info, _, root, child = self.fixture()
            if fault == "repository":
                root["RepoDigests"].pop()
            if fault == "index":
                root["Descriptor"]["digest"] = self.child
            if fault == "child":
                child["Descriptor"]["digest"] = self.index
            if fault == "architecture":
                child["Architecture"] = "arm64"
            if fault == "annotation":
                child["Descriptor"]["annotations"].clear()
            with self.subTest(fault=fault), tempfile.TemporaryDirectory() as tmp:
                engine = native.NativeBackups(Mock(), tmp)
                with patch.object(engine, "call", side_effect=[
                    json.dumps(root), RuntimeError("missing platform") if fault == "unavailable" else json.dumps(child)
                ]), self.assertRaises((ValueError, RuntimeError)):
                    engine.cached_image(info)


class NativeRetentionTests(unittest.TestCase):
    def test_expiration_keeps_every_incremental_dependency_in_recent_full_chains(self):
        catalog = [entry(1), entry(2, 1), entry(3, 2), entry(4), entry(5, 4), entry(6), entry(7, 6)]
        self.assertEqual(native.retention(catalog, 2), [f'{n:064x}' for n in (1, 2, 3)])

    def test_latest_recovery_point_preserves_its_base_even_after_another_full(self):
        catalog = [entry(1), entry(2), entry(3, 1)]
        self.assertEqual(native.retention(catalog, 1), [])

    def test_missing_parent_fails_closed(self):
        with self.assertRaisesRegex(ValueError, 'missing'):
            native.retention([entry(2, 1)])

    def test_cycle_fails_closed(self):
        with self.assertRaisesRegex(ValueError, 'cycle'):
            native.retention([entry(1, 2), entry(2, 1)])

    def test_unknown_or_ambiguous_catalog_tags_cannot_expire_data(self):
        for tags in ([], ['trn-kind:full', 'trn-kind:incremental', 'trn-parent:none']):
            with self.assertRaises(ValueError):
                native.retention([dict(id='a' * 64, time='2026-01-01', tags=tags)])


class NativeExtractionTests(unittest.TestCase):
    def test_archive_traversal_and_links_are_rejected_before_any_extraction(self):
        for name, kind in (('../escape', tarfile.REGTYPE), ('/escape', tarfile.REGTYPE),
                           ('link', tarfile.SYMTYPE), ('link', tarfile.LNKTYPE), ('device', tarfile.CHRTYPE)):
            with self.subTest(name=name, kind=kind), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                archive = root / 'data.tar'
                with tarfile.open(archive, 'w') as output:
                    good = tarfile.TarInfo('good')
                    good.size = 1
                    output.addfile(good, io.BytesIO(b'x'))
                    bad = tarfile.TarInfo(name)
                    bad.type = kind
                    bad.linkname = '../escape'
                    output.addfile(bad)
                target = root / 'output'
                target.mkdir()
                with self.assertRaises(ValueError):
                    native.safe_extract(archive, target)
                self.assertEqual(list(target.iterdir()), [])

    def test_regular_recovery_files_are_private(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            archive = root / 'data.tar.gz'
            with tarfile.open(archive, 'w:gz') as output:
                member = tarfile.TarInfo('PG_VERSION')
                member.size = 2
                member.mode = 0o4777
                output.addfile(member, io.BytesIO(b'18'))
            target = root / 'output'
            target.mkdir()
            native.safe_extract(archive, target)
            self.assertEqual((target / 'PG_VERSION').read_bytes(), b'18')
            self.assertEqual((target / 'PG_VERSION').stat().st_mode & 0o7777, 0o600)

    def test_public_secrets_fail_before_contacting_postgres(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            secret = root / 'application.env'
            secret.write_text('fixture')
            secret.chmod(0o644)
            engine = native.NativeBackups(None, root)
            with patch.dict(os.environ, {'TRN_BACKUP_APP_ENV_FILE': str(secret)}), patch.object(engine, 'source') as source:
                with self.assertRaisesRegex(ValueError, 'private file'):
                    engine.backup(full=True)
                source.assert_not_called()


class NativeBackupGuardTests(unittest.TestCase):
    def test_daily_without_a_verified_parent_never_starts_a_full_scan(self):
        with tempfile.TemporaryDirectory() as temporary:
            engine = native.NativeBackups(Mock(), Path(temporary))
            with patch.dict(os.environ, {'TRN_BACKUP_APP_ENV_FILE': ''}), \
                 patch.object(engine, 'source', return_value={'system': 'fixture'}), \
                 patch.object(engine, 'catalog', return_value=[]), \
                 patch.object(native.subprocess, 'Popen') as process:
                with self.assertRaisesRegex(ValueError, 'refusing an unexpected full'):
                    engine.backup()
                process.assert_not_called()
                engine.api.metric.assert_not_called()

    def test_pending_snapshots_are_never_incremental_parents(self):
        api = Mock()
        verified = entry(1)
        pending = entry(2, 1)
        pending['tags'].remove('trn-verified')
        pending['tags'].append('trn-pending')
        api.run.return_value = json.dumps([verified, pending]).encode()
        with tempfile.TemporaryDirectory() as temporary:
            engine = native.NativeBackups(api, Path(temporary))
            self.assertEqual(engine.catalog('fixture'), [verified])


if __name__ == '__main__':
    unittest.main()
