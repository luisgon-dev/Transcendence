#!/usr/bin/env python3
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

SCRIPT=Path(__file__).resolve().parents[1]/'web-perf-sweep.sh'
MOCK=r'''#!/usr/bin/env python3
import os,sys
from pathlib import Path
args=sys.argv[1:]
with open(os.environ['MOCK_LOG'],'a') as f: f.write(repr(args)+'\n')
if args[0]=='run':
    if os.environ.get('FAIL_RUN'): sys.exit(124)
    staging=Path(args[args.index('-v')+1].split(':')[0]);(staging/'reports').mkdir()
    (staging/'reports/example.json').write_text('{}')
    (staging/'web_lab.prom').write_text('transcendence_web_lab_last_success_unixtime_seconds 1\n')
'''
class PerfTests(unittest.TestCase):
    def run_case(self,fail):
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp);bin=root/'bin';bin.mkdir()
            docker=bin/'docker';docker.write_text(MOCK);docker.chmod(0o755)
            (root/'textfile').mkdir();previous=root/'textfile/web_lab.prom';previous.write_text('previous\n')
            env=dict(os.environ,PATH=str(bin)+':'+os.environ['PATH'],MOCK_LOG=str(root/'calls'),PERF_STATE_DIR=str(root),PERF_TIMEOUT_SECONDS='1')
            if fail: env['FAIL_RUN']='1'
            result=subprocess.run(['bash',str(SCRIPT)],env=env,capture_output=True,text=True,timeout=10)
            self.assertEqual(result.returncode,1 if fail else 0,result.stderr)
            calls=(root/'calls').read_text()
            self.assertIn("'--name', 'transcendence-web-perf-runner'",calls)
            self.assertIn("'--memory=1536m'",calls)
            self.assertIn("'--cpus=1.5'",calls)
            self.assertGreaterEqual(calls.count("['rm', '-f', 'transcendence-web-perf-runner']"),2)
            self.assertFalse((root/'staging').exists())
            if fail: self.assertEqual(previous.read_text(),'previous\n')
            else: self.assertTrue((root/'reports/example.json').exists())
    def test_success_cleans_browser_and_publishes_reports(self): self.run_case(False)
    def test_timeout_cleans_browser_and_keeps_previous_metrics(self): self.run_case(True)
if __name__=='__main__': unittest.main()
