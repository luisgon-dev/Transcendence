#!/usr/bin/env python3
"""Prepare an initial edge route to healthy legacy container IPs; never overwrite a live route."""
import argparse
import ipaddress
import json
from pathlib import Path
import subprocess


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--network', default='transcendence_transcendence-net')
    parser.add_argument('--traffic-file', type=Path, default=Path('/var/lib/transcendence-release/traffic/active.yml'))
    args = parser.parse_args()
    if args.traffic_file.suffix not in ('.yml', '.yaml'):
        raise ValueError('Traefik requires a YAML file suffix')
    template = Path(__file__).resolve().parents[2] / 'config/deploy/traffic.example.yml'
    value = json.loads('\n'.join(line for line in template.read_text().splitlines() if not line.startswith('#')))
    for component, marker in (('web', 'LEGACY_WEB_PRIVATE_IP'), ('webapi', 'LEGACY_API_PRIVATE_IP')):
        data = json.loads(subprocess.check_output(['docker', 'inspect', 'transcendence-' + component]))[0]
        if data['State'].get('Health', {}).get('Status') != 'healthy':
            raise RuntimeError('Verify the current application before preparing edge adoption')
        address = data['NetworkSettings']['Networks'][args.network]['IPAddress']
        if not ipaddress.ip_address(address).is_private:
            raise ValueError('Initial edge route must use the private application network')
        server = value['http']['services'][component]['loadBalancer']['servers'][0]
        server['url'] = server['url'].replace(marker, address)
    args.traffic_file.parent.mkdir(parents=True, exist_ok=True, mode=0o755)
    # Exclusive creation prevents accidentally resetting an already managed release route.
    with args.traffic_file.open('x') as output:
        output.write(json.dumps(value, indent=2) + '\n')
    args.traffic_file.chmod(0o644)
    print('Initial private edge route prepared. Existing application containers were not changed.')


if __name__ == '__main__':
    main()
