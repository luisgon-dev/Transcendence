#!/usr/bin/env python3
"""Small installed trust boundary: verify a complete bundle, then run its versioned agent."""
import argparse
import fcntl
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import tarfile
import tempfile
import urllib.request

REPOSITORY = "luisgon-dev/Transcendence"
COSIGN = "ghcr.io/sigstore/cosign/cosign:v2.6.3@sha256:4bedb8de1c5c1abd8dea60de704ba449402d238623fa8bb33d2ccaa9beffcbf5"
IDENTITY = f"https://github.com/{REPOSITORY}/.github/workflows/docker-images.yml@refs/heads/main"


def download(url, path, limit):
    request = urllib.request.Request(url, headers={"User-Agent": "transcendence-release/1"})
    with urllib.request.urlopen(request, timeout=60) as response, path.open("wb") as target:
        size = 0
        while data := response.read(65536):
            size += len(data)
            if size > limit:
                raise ValueError("Release download exceeds its size limit")
            target.write(data)


def extract_verified(archive_path, destination, release):
    # Do not extract links, devices, duplicate entries, absolute paths or traversal.
    with tarfile.open(archive_path) as archive:
        members = archive.getmembers()
        names = set()
        total = 0
        for member in members:
            name = Path(member.name)
            total += member.size
            if (not member.isfile() or name.is_absolute() or ".." in name.parts
                    or member.name in names or total > 64 * 1024 * 1024):
                raise ValueError("Invalid release archive entry")
            names.add(member.name)
        archive.extractall(destination, filter="data")
    manifest = json.loads((destination / "release.json").read_text())
    if manifest.get("schema") != 1 or manifest.get("release") != release:
        raise ValueError("Release identity or protocol mismatch")
    expected = set(manifest["files"]) | {"release.json"}
    if names != expected:
        raise ValueError("Release inventory mismatch")
    validate_directory(destination, manifest)
    return manifest


def validate_directory(directory, manifest):
    files = set(manifest["files"]) | {"release.json"}
    paths = list(directory.rglob("*"))
    if any(p.is_symlink() for p in paths) or {str(p.relative_to(directory)) for p in paths if p.is_file()} != files:
        raise ValueError("Installed release inventory differs from the verified bundle")
    for name, digest in manifest["files"].items():
        if hashlib.sha256((directory / name).read_bytes()).hexdigest() != digest:
            raise ValueError("Release file checksum mismatch")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--state", type=Path, default=Path(os.environ.get("TRN_RELEASE_STATE_DIR", "/var/lib/transcendence-release")))
    parser.add_argument("--env-file", type=Path, default=Path(os.environ.get("TRN_RELEASE_ENV_FILE", "/root/transcendence/.env")))
    parser.add_argument("--release", default=os.environ.get("TRN_RELEASE_PIN"), help="Explicit release tag; otherwise use the latest complete release")
    parser.add_argument("--base-url", default=os.environ.get("TRN_RELEASE_BASE_URL", "https://transcend.kronic.one"))
    parser.add_argument("--api-url", default=os.environ.get("TRN_RELEASE_API_URL", "https://api.kronic.one"))
    parser.add_argument("--profile-path", default=os.environ.get("TRN_RELEASE_PROFILE_PATH", "/lol/summoners/na/Kronic-NA1"))
    parser.add_argument("--mode", choices=("replace", "blue-green"), default=os.environ.get("TRN_RELEASE_MODE", "replace"))
    parser.add_argument("--traffic-file", type=Path, default=os.environ.get("TRN_RELEASE_TRAFFIC_FILE"))
    parser.add_argument("--network", default=os.environ.get("TRN_RELEASE_NETWORK", "transcendence_transcendence-net"))
    parser.add_argument("--retain", type=int, default=int(os.environ.get("TRN_RELEASE_RETAIN", "3")))
    args = parser.parse_args()
    os.umask(0o077)
    args.state.mkdir(parents=True, exist_ok=True)
    with (args.state / "lock").open("w") as lock:
        # Same lock as the old updater; an in-progress old poll cannot race adoption.
        with Path("/run/transcendence-deploy.lock").open("w") as legacy:
            try:
                fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
                fcntl.flock(legacy, fcntl.LOCK_EX | fcntl.LOCK_NB)
            except BlockingIOError:
                return
            if args.release and not re.fullmatch(r"release-[0-9a-f]{40}", args.release):
                raise ValueError("Invalid release tag")
            endpoint = "latest" if not args.release else f"tags/{args.release}"
            with tempfile.TemporaryDirectory(dir=args.state) as scratch:
                scratch = Path(scratch)
                download(f"https://api.github.com/repos/{REPOSITORY}/releases/{endpoint}", scratch / "metadata.json", 1024 * 1024)
                metadata = json.loads((scratch / "metadata.json").read_text())
                release = metadata["tag_name"]
                if metadata.get("draft") or metadata.get("prerelease") or not re.fullmatch(r"release-[0-9a-f]{40}", release):
                    raise ValueError("No complete supported release is available")
                assets = {a["name"]: a["browser_download_url"] for a in metadata["assets"]}
                for name in ("release.tgz", "release.sigstore.json"):
                    url = assets[name]
                    if not url.startswith(f"https://github.com/{REPOSITORY}/releases/download/{release}/"):
                        raise ValueError("Unexpected release asset origin")
                    download(url, scratch / name, 64 * 1024 * 1024)
                subprocess.run(["docker", "run", "--rm", "--user", "0:0", "-v", f"{scratch}:/release:ro", COSIGN,
                                "verify-blob", "--bundle", "/release/release.sigstore.json",
                                "--certificate-identity", IDENTITY,
                                "--certificate-oidc-issuer", "https://token.actions.githubusercontent.com",
                                "/release/release.tgz"], check=True, timeout=180)
                unpacked = scratch / "unpacked"
                unpacked.mkdir()
                manifest = extract_verified(scratch / "release.tgz", unpacked, release)
                releases = args.state / "releases"
                releases.mkdir(exist_ok=True)
                target = releases / release
                if target.exists():
                    # Revalidate against freshly verified evidence. Never remove active tooling
                    # while another service may be starting its versioned backup script.
                    validate_directory(target, manifest)
                    if (target / "release.json").read_bytes() != (unpacked / "release.json").read_bytes():
                        raise ValueError("Installed release contract differs; investigate before retrying")
                else:
                    shutil.move(str(unpacked), target)
                command = ["python3", "-B", str(target / "scripts/ops/release_deploy.py"), "apply",
                           "--bundle", str(target), "--state", str(args.state),
                           "--env-file", str(args.env_file), "--base-url", args.base_url,
                           "--api-url", args.api_url,
                           "--profile-path", args.profile_path, "--mode", args.mode,
                           "--network", args.network, "--retain", str(args.retain)]
                if args.traffic_file:
                    command += ["--traffic-file", str(args.traffic_file)]
                # Retain the shared legacy lock in the agent even if its parent is interrupted.
                subprocess.run(command, check=True, pass_fds=(legacy.fileno(),),
                               env=dict(os.environ, TRN_RELEASE_BOOTSTRAPPED="1"))


if __name__ == "__main__":
    main()
