#!/usr/bin/env python3
"""Exercise the archive entrypoint with isolated fake PostgreSQL and a local fake NAS."""
import gzip
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

SCRIPT = Path(__file__).resolve().parents[1] / 'archive-old-patches.sh'
MOCK = r'''#!/usr/bin/env python3
import json, os, re, subprocess, sys
from pathlib import Path
state_path=Path(os.environ['MOCK_DB'])
s=json.loads(state_path.read_text())
args=sys.argv[1:]
with open(os.environ['MOCK_LOG'],'a') as f: f.write(Path(sys.argv[0]).name+' '+repr(args)+'\n')
def save(): state_path.write_text(json.dumps(s))
if Path(sys.argv[0]).name=='ssh':
    sys.exit(subprocess.call(['bash','-c',args[-1]]))
sql=args[-1]
if 'COPY (' in sql:
    table=re.search(r'FROM "(\w+)"',sql)[1]
    if table==os.environ.get('FAIL_EXPORT'): sys.exit(1)
    print('Id')
    if table=='Matches':
        for i in range(s['work_count']): print(i)
elif 'pg_wal_lsn_diff' in sql:
    initial=int(re.search(r"'0/([0-9A-F]+)'",sql)[1],16)
    print(s['wal']-initial)
elif 'pg_current_wal_insert_lsn' in sql: print('0/'+format(s['wal'],'X'))
elif 'to_regclass' in sql:
    print('t' if ('_patch_archive_pending' in sql and s['work']) else 'f')
elif 'SELECT count(*) FROM (SELECT DISTINCT' in sql:
    print(0 if os.environ.get('PROTECTED') else 1)
elif sql.startswith('SELECT DISTINCT'): print('16.1' if s['matches'] else '',end='\n')
elif 'CREATE TABLE' in sql:
    s['work']=True; s['work_count']=min(s['matches'],int(re.search(r'LIMIT (\d+)',sql)[1]));save()
elif 'SELECT count(*) FROM _patch_archive_pending' in sql: print(s['work_count'])
elif sql.startswith('SET enable_seqscan'):
    print(s['work_count'] if 'FROM "Matches"' in sql else 0)
elif 'WITH batch AS' in sql:
    if os.environ.get('FAIL_DELETE_ONCE') and not s['failed']:
        s['failed']=True;save();sys.exit(1)
    n=min(s['work_count'],int(re.search(r'LIMIT (\d+)',sql)[1]))
    s['work_count']-=n;s['matches']-=n;s['wal']+=1048576;save()
elif 'DROP TABLE' in sql: s['work']=False;save()
else:
    raise RuntimeError('Unexpected SQL: '+sql)
'''

class ArchiveTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.bin = self.root / 'bin'; self.bin.mkdir()
        for name in ['docker', 'ssh']:
            command = self.bin / name; command.write_text(MOCK); command.chmod(0o755)
        self.db = self.root / 'db.json'
        self.db.write_text(json.dumps(dict(work=False, work_count=0, matches=5, wal=256, failed=False)))
        self.log = self.root / 'calls.log'
        self.env = dict(os.environ, PATH=str(self.bin)+':'+os.environ['PATH'], MOCK_DB=str(self.db), MOCK_LOG=str(self.log),
                        NAS_DIR=str(self.root / 'nas'), ARCHIVE_STATE_DIR=str(self.root / 'progress'),
                        IO_PRESSURE_FILE=str(self.root / 'pressure'), BATCH_SLEEP_SECONDS='0', FREEZE_MATCHES='5',
                        DELETE_BATCH='4', MAX_WAL_MB='1', MAX_RUN_SECONDS='60')
        (self.root / 'pressure').write_text('full avg10=0.00 avg60=0.00 avg300=0.00 total=0\n')
    def tearDown(self): self.temp.cleanup()
    def run_archive(self, **extra):
        return subprocess.run(['bash',str(SCRIPT)], env=dict(self.env, **extra), capture_output=True, text=True, timeout=30)
    def state(self): return json.loads(self.db.read_text())
    def test_dry_run_has_no_writes(self):
        result=self.run_archive(APPLY='0')
        self.assertEqual(result.returncode,0,result.stderr)
        self.assertFalse((self.root/'progress').exists())
        self.assertFalse((self.root/'nas').exists())
        self.assertNotIn('CREATE TABLE',self.log.read_text())
    def test_failed_export_never_deletes(self):
        result=self.run_archive(APPLY='1',FAIL_EXPORT='MatchBans')
        self.assertNotEqual(result.returncode,0)
        self.assertEqual(self.state()['matches'],5)
        self.assertNotIn('WITH batch AS',self.log.read_text())
        self.assertEqual((self.root/'progress/current').read_text().splitlines()[3],'0')
    def test_timeout_reduces_batch_and_verified_resume_never_overwrites_exports(self):
        result=self.run_archive(APPLY='1',FAIL_DELETE_ONCE='1')
        self.assertEqual(result.returncode,0,result.stderr)
        self.assertIn('reducing batch to 2',result.stdout)
        self.assertEqual(self.state()['matches'],3)
        frozen=(self.root/'progress/current').read_text().splitlines()
        self.assertEqual(frozen[3],'1')
        export=Path(frozen[1])/'Matches.csv.gz'
        before=export.read_bytes()
        self.assertEqual(len(gzip.decompress(before).splitlines()),6)
        result=self.run_archive(APPLY='1')
        self.assertEqual(result.returncode,0,result.stderr)
        self.assertEqual(export.read_bytes(),before)
        self.assertEqual(self.state()['matches'],0)
        self.assertTrue((Path(frozen[1])/'_DONE').exists())
        self.assertFalse((self.root/'progress/current').exists())
        self.assertEqual(self.log.read_text().count('COPY ('),13)
    def test_protected_patch_resume_never_deletes(self):
        self.run_archive(APPLY='1',FAIL_EXPORT='MatchBans')
        result=self.run_archive(APPLY='1',PROTECTED='1')
        self.assertEqual(result.returncode,0,result.stderr)
        self.assertIn('now protected',result.stdout)
        self.assertEqual(self.state()['matches'],5)
    def test_pressure_yields_before_work_is_frozen(self):
        (self.root/'pressure').write_text('full avg10=40.0 avg60=40.0 avg300=40.0 total=0\n')
        result=self.run_archive(APPLY='1')
        self.assertEqual(result.returncode,0,result.stderr)
        self.assertNotIn('CREATE TABLE',self.log.read_text())
        self.assertEqual(self.state()['matches'],5)

if __name__=='__main__': unittest.main()
