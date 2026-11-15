#!/usr/bin/env python3
"""Exercise new deployment tooling against fixed signed application fixtures on PRs.

Publication additionally stages the newly built images; fixtures never become a
public release. This test needs direct CDN egress for Next's pinned image requests.
"""
import argparse
import importlib.util
import json
from pathlib import Path
import tarfile

ROOT = Path(__file__).resolve().parents[2]


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--state', type=Path, required=True)
    args = parser.parse_args()
    fixture = json.loads((ROOT / 'config/deploy/staging-fixture.json').read_text())
    records = args.state / 'records'
    records.mkdir(parents=True, exist_ok=True)
    for component, image in fixture['images'].items():
        (records / f'{component}.json').write_text(json.dumps(dict(image=image, revision=fixture['revision'])))
    build = load('release_builder', ROOT / 'scripts/ci/build-release.py')
    build.build(fixture['revision'], records, args.state / 'output')
    bundle = args.state / 'bundle'
    bundle.mkdir(exist_ok=True)
    with tarfile.open(args.state / 'output/release.tgz') as archive:
        archive.extractall(bundle, filter='data')
    deploy = load('release_deploy', bundle / 'scripts/ops/release_deploy.py')
    # Verify fixture supply chain before starting any of its containers.
    env = args.state / 'unused.env'
    env.touch()
    engine = deploy.Engine(bundle, args.state, env)
    engine.verify_images()
    deploy.stage(bundle, args.state / 'stage')
    print('Full isolated staging regression passed, including public BFF and image delivery.')


if __name__ == '__main__':
    main()
