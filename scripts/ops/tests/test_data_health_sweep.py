import os
from pathlib import Path
import subprocess
import tempfile
import unittest

SCRIPT = Path(__file__).resolve().parents[1] / 'data-health-sweep.sh'
CURL = '''#!/usr/bin/env python3
import os,sys
from pathlib import Path
args=sys.argv[1:]
Path(args[args.index('-o')+1]).write_text('<main data-backend-error>unavailable</main>' if os.environ.get('PAGE_FAIL') else '<main>data</main>')
print('0.02 0.20')
'''
DOCKER = '''#!/usr/bin/env python3
import os,sys
print('transcendence_snapshot_oldest_age_seconds{feature="synergies"} 60')
sys.exit(1 if os.environ.get('DB_FAIL') else 0)
'''


class DataHealthTests(unittest.TestCase):
    def run_sweep(self, **flags):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            binary = root / 'bin'
            binary.mkdir()
            for name, source in [('curl', CURL), ('docker', DOCKER)]:
                command = binary / name
                command.write_text(source)
                command.chmod(0o755)
            env = dict(os.environ, PATH=str(binary) + ':' + os.environ['PATH'], DATA_HEALTH_STATE_DIR=tmp, **flags)
            result = subprocess.run(['bash', str(SCRIPT)], env=env, capture_output=True, text=True, timeout=10)
            self.assertEqual(result.returncode, 0, result.stderr)
            return (root / 'textfile/data_health.prom').read_text()

    def test_database_failure_still_publishes_current_page_results(self):
        metrics = self.run_sweep(DB_FAIL='1')
        self.assertEqual(metrics.count('transcendence_data_probe_failure{'), 3)
        self.assertIn('transcendence_snapshot_probe_failure 1', metrics)
        self.assertIn('transcendence_data_health_last_success_unixtime_seconds ', metrics)
        self.assertNotIn('transcendence_snapshot_oldest_age_seconds', metrics, 'partial failed SQL output must be discarded')

    def test_http_200_fallback_is_reported_as_failed(self):
        metrics = self.run_sweep(PAGE_FAIL='1')
        self.assertIn('transcendence_data_probe_failure{route="/lol/summoners/na/Kronic-NA1"} 1', metrics)

    def test_success_publishes_pages_and_snapshot_ages(self):
        metrics = self.run_sweep()
        self.assertIn('transcendence_snapshot_probe_failure 0', metrics)
        self.assertIn('transcendence_snapshot_oldest_age_seconds{feature="synergies"} 60', metrics)


if __name__ == '__main__':
    unittest.main()
