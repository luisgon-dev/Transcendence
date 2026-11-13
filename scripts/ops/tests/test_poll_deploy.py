import os
from pathlib import Path
import subprocess
import tempfile
import unittest

SCRIPT = Path(__file__).resolve().parents[1] / 'poll-deploy.sh'


class CompatibilityTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.git('init', '-q')
        self.git('config', 'user.name', 'Deployment regression')
        self.git('config', 'user.email', 'deployment-test@example.invalid')
        self.base = self.commit('initial.txt')

    def git(self, *args):
        return subprocess.check_output(['git', '-C', str(self.root), *args], text=True).strip()

    def commit(self, path):
        target = self.root / path
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(path)
        self.git('add', '.')
        self.git('commit', '-qm', path)
        return self.git('rev-parse', 'HEAD')

    def probe(self, worker, target, extra=''):
        script = f'''source "{SCRIPT}"
COMPOSE_DIR="{self.root}"
local_revision() {{ printf '%s\\n' '{worker}'; }}
{extra}
migration_compatibility '{target}'
'''
        return subprocess.run(['bash', '-c', script], env=dict(os.environ, POLL_DEPLOY_LIB_ONLY='1'),
                              capture_output=True, text=True, timeout=10).returncode

    def test_same_or_already_applied_revision_is_safe(self):
        current = self.commit('api.txt')
        self.assertEqual(self.probe(self.base, self.base), 0)
        self.assertEqual(self.probe(current, self.base), 0)

    def test_migration_requires_worker_and_code_only_change_is_safe(self):
        code = self.commit('api.txt')
        migration = self.commit('Transcendence.Service/Migrations/new.cs')
        self.assertEqual(self.probe(self.base, code), 0)
        self.assertEqual(self.probe(self.base, migration), 1)

    def test_unknown_object_or_failed_lookup_is_not_safe(self):
        self.assertEqual(self.probe(self.base, 'a' * 40), 2)
        self.assertEqual(self.probe(self.base, 'b' * 40, 'git() { return 1; }'), 2)
        self.assertEqual(self.probe('', self.base), 2)

    def test_divergent_history_is_unknown(self):
        worker = self.commit('worker.txt')
        self.git('checkout', '-q', self.base)
        target = self.commit('other.txt')
        self.assertEqual(self.probe(worker, target), 2)


class RolloutTests(unittest.TestCase):
    def run_case(self, component='webapi', compatibility=0, fail='', moved=False):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            calls = root / 'calls'
            calls.touch()
            body = f'''source "{SCRIPT}"
STATE_DIR="{root}"
VERIFY_SIGNATURES={'1' if moved else '0'}
remote_digest() {{ echo sha256:release; }}
remote_revision() {{ printf '%040d\\n' 2; }}
local_digest() {{ echo sha256:previous; }}
local_revision() {{ printf '%040d\\n' 1; }}
docker() {{ echo previous-image; }}
verify_signature() {{ return 0; }}
pulled_digest_is() {{ return {'1' if moved else '0'}; }}
migration_compatibility() {{ return {compatibility}; }}
notify() {{ :; }}
run_logged() {{ echo "$1" >> "{calls}"; [ "$1" != "{fail}" ]; }}
run_migrations() {{ echo migration >> "{calls}"; [ "migration" != "{fail}" ]; }}
wait_for_healthy() {{ echo health >> "{calls}"; [ "health" != "{fail}" ]; }}
wait_for_web_assets() {{ echo assets >> "{calls}"; [ "assets" != "{fail}" ]; }}
rollback_service() {{ echo rollback >> "{calls}"; }}
deploy_one '{component}' 'test-container' 'test-repo'
'''
            result = subprocess.run(['bash', '-c', body], env=dict(os.environ, POLL_DEPLOY_LIB_ONLY='1'),
                                    capture_output=True, text=True, timeout=10)
            quarantined = (root / f'{component}-failed-digest').exists()
            return result.returncode, calls.read_text(), quarantined

    def test_unknown_compatibility_and_pending_migrations_never_pull(self):
        for compatibility in [1, 2]:
            rc, calls, _ = self.run_case(compatibility=compatibility)
            self.assertEqual(rc, 0 if compatibility == 1 else 1)
            self.assertNotIn('pull', calls)

    def test_moving_tag_never_replaces_container(self):
        rc, calls, _ = self.run_case(moved=True)
        self.assertEqual(rc, 1)
        self.assertIn('pull', calls)
        self.assertNotIn('up -d', calls)

    def test_failed_migration_quarantines_release_without_replacement(self):
        rc, calls, quarantined = self.run_case(component='service', fail='migration')
        self.assertEqual(rc, 1)
        self.assertTrue(quarantined)
        self.assertNotIn('up -d', calls)

    def test_health_or_replacement_failure_rolls_back(self):
        for fail in ['health', 'webapi up -d']:
            rc, calls, _ = self.run_case(fail=fail)
            self.assertEqual(rc, 1)
            self.assertIn('rollback', calls)

    def test_web_requires_assets_and_rolls_back_a_failed_probe(self):
        rc, calls, _ = self.run_case(component='web', fail='assets')
        self.assertEqual(rc, 1)
        self.assertIn('web cache preparation', calls)
        self.assertIn('assets', calls)
        self.assertIn('rollback', calls)

    def test_success_requires_health(self):
        rc, calls, _ = self.run_case()
        self.assertEqual(rc, 0)
        self.assertIn('health', calls)


if __name__ == '__main__':
    unittest.main()
