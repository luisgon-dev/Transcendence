#!/usr/bin/env python3
"""Verify real file-provider cutover and rollback, including same-revision route identity."""
import importlib.util
import json
import os
from pathlib import Path
import secrets
import subprocess
import tempfile
import time
import urllib.request

ROOT = Path(__file__).resolve().parents[2]


def run(*args, **kwargs):
    return subprocess.check_output(list(args), text=True, **kwargs).strip()


def main():
    fixture = json.loads((ROOT / 'config/deploy/staging-fixture.json').read_text())
    config = json.loads(run('docker', 'compose', '-f', str(ROOT / 'config/deploy/compose.yml'), 'config', '--format', 'json',
                            env=dict(os.environ, EDGE_TRUSTED_PROXY_CIDRS='172.16.0.0/12')))
    image = config['services']['release-edge']['image']
    spec = importlib.util.spec_from_file_location('release_deploy', ROOT / 'scripts/ops/release_deploy.py')
    deploy = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(deploy)
    prefix = 'trn-edge-test-' + secrets.token_hex(6)
    network = prefix + '-net'
    names = [prefix + '-old', prefix + '-new', prefix + '-edge']
    run('docker', 'network', 'create', network)
    try:
        with tempfile.TemporaryDirectory() as scratch:
            directory = Path(scratch)
            directory.chmod(0o755)
            traffic = directory / 'active.yml'
            for phase, name in zip(('old', 'new'), names):
                code = ('const http=require("node:http"); for(const port of [3000,8080]) '
                        'http.createServer((req,res)=>{res.setHeader("content-type","application/json");'
                        'res.end(JSON.stringify({phase:' + json.dumps(phase) + ',headers:req.headers}))}).listen(port,"0.0.0.0");')
                run('docker', 'run', '-d', '--name', name, '--network', network,
                    '--network-alias', name + '-web', '--network-alias', name + '-webapi',
                    '--entrypoint', 'node', fixture['images']['web'], '-e', code)
            deploy.switch_traffic(traffic, names[0])
            original = traffic.read_bytes()
            excluded = os.environ.get('NO_PROXY', '') + ',' + ','.join(
                name + '-' + role for name in names[:2] for role in ('web', 'webapi'))
            run('docker', 'run', '-d', '--name', names[2], '--network', network,
                '-p', '127.0.0.1::3000', '-p', '127.0.0.1::8080',
                '-e', 'NO_PROXY=' + excluded, '-e', 'no_proxy=' + excluded,
                '-v', f'{directory}:/dynamic:ro', '--read-only', '--cap-drop', 'ALL',
                image, '--entrypoints.web.address=:3000', '--entrypoints.webapi.address=:8080',
                '--entrypoints.web.forwardedheaders.trustedips=172.16.0.0/12',
                '--providers.file.directory=/dynamic', '--providers.file.watch=true')
            ports = [run('docker', 'port', names[2], f'{p}/tcp').rsplit(':', 1)[1] for p in (3000, 8080)]
            def expect(phase, route):
                deadline = time.monotonic() + 60
                while True:
                    try:
                        for port in ports:
                            request = urllib.request.Request(f'http://127.0.0.1:{port}/api/readiness',
                                headers={'X-Real-IP': '203.0.113.10', 'X-Forwarded-For': '203.0.113.10'})
                            with urllib.request.urlopen(request, timeout=5) as response:
                                body = json.loads(response.read())
                                assert body['phase'] == phase
                                assert response.headers.get('x-trn-deployment') == route
                                if port == ports[0]:
                                    assert body['headers']['x-real-ip'] == '203.0.113.10'
                        return
                    except Exception:
                        if time.monotonic() >= deadline:
                            raise
                        time.sleep(1)
            expect('old', names[0])
            deploy.switch_traffic(traffic, names[1])
            expect('new', names[1])
            temporary = directory / 'rollback.tmp'
            temporary.write_bytes(original)
            temporary.chmod(0o644)
            temporary.replace(traffic)
            expect('old', names[0])
            print('Real blue-green traffic cutover, route identity, trusted client header and rollback passed.')
    finally:
        subprocess.run(['docker', 'rm', '-f', *names], capture_output=True)
        subprocess.run(['docker', 'network', 'rm', network], capture_output=True)


if __name__ == '__main__':
    main()
