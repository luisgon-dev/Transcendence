#!/usr/bin/env python3
"""Build the complete release contract; no tags are resolved by the deployer."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import tarfile

ROOT = Path(__file__).resolve().parents[2]
COMPONENTS = ("service", "webapi", "web")


def build(revision, records, output):
    if not re.fullmatch(r"[0-9a-f]{40}", revision):
        raise ValueError("A full source revision is required")
    images = {}
    for component in COMPONENTS:
        matches = list(records.rglob(f"{component}.json"))
        if len(matches) != 1:
            raise ValueError(f"Exactly one {component} image record is required")
        record = json.loads(matches[0].read_text())
        expected = f"ghcr.io/luisgon-dev/transcendence-{component}@sha256:"
        if record.get("revision") != revision or not re.fullmatch(re.escape(expected) + r"[0-9a-f]{64}", record.get("image", "")):
            raise ValueError(f"Invalid {component} image identity")
        images[component] = record["image"]
    paths = [ROOT / "compose.yml", ROOT / "scripts/e2e/seed-profile.sql"]
    paths += sorted((ROOT / "scripts/ops").glob("release_*.py"))
    paths += sorted((ROOT / "scripts/ops").glob("transcendence-backup*.service"))
    paths += sorted((ROOT / "scripts/ops").glob("transcendence-backup*.timer"))
    paths += [ROOT / "scripts/ops/transcendence-restore-drill.service",
              ROOT / "scripts/ops/transcendence-restore-drill.timer"]
    paths += sorted((ROOT / "config/deploy").glob("*"))
    files = {str(p.relative_to(ROOT)): hashlib.sha256(p.read_bytes()).hexdigest()
             for p in paths if p.is_file()}
    manifest = dict(schema=1, release=f"release-{revision}", revision=revision,
                    images=images, files=files, databaseRollback="application-only")
    output.mkdir(parents=True, exist_ok=True)
    (output / "release.json").write_text(json.dumps(manifest, indent=2) + "\n")
    with tarfile.open(output / "release.tgz", "w:gz") as archive:
        archive.add(output / "release.json", arcname="release.json")
        for name in files:
            archive.add(ROOT / name, arcname=name)
    return manifest


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--revision", required=True)
    parser.add_argument("--records", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    build(args.revision, args.records, args.output)
