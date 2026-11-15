#!/usr/bin/env python3
"""Release-wide Compose deployment. This file travels inside the signed release."""
import argparse
import contextlib
import fcntl
from copy import deepcopy
import hashlib
import json
import os
from pathlib import Path
import re
import secrets
import signal
import shutil
import subprocess
import tempfile
import time
import urllib.request

COMPONENTS = ("service", "webapi", "web")
IMAGE_ENV = {"service": "SERVICE_IMAGE", "webapi": "WEBAPI_IMAGE", "web": "WEB_IMAGE"}
COSIGN = "ghcr.io/sigstore/cosign/cosign:v2.6.3@sha256:4bedb8de1c5c1abd8dea60de704ba449402d238623fa8bb33d2ccaa9beffcbf5"
IDENTITY = "https://github.com/luisgon-dev/Transcendence/.github/workflows/docker-images.yml@refs/heads/main"


def write_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(json.dumps(value, indent=2) + "\n")
    temporary.chmod(0o600)
    temporary.replace(path)


def load_manifest(bundle):
    manifest = json.loads((bundle / "release.json").read_text())
    revision = manifest.get("revision", "")
    if (manifest.get("schema") != 1 or not re.fullmatch(r"[0-9a-f]{40}", revision)
            or manifest.get("release") != "release-" + revision
            or manifest.get("databaseRollback") != "application-only"
            or set(manifest.get("images", {})) != set(COMPONENTS)):
        raise ValueError("Unsupported release contract")
    for component, image in manifest["images"].items():
        if not re.fullmatch(f"ghcr.io/luisgon-dev/transcendence-{component}@sha256:[0-9a-f]{{64}}", image):
            raise ValueError("Release must pin every application image by digest")
    for name, digest in manifest["files"].items():
        relative = Path(name)
        if relative.is_absolute() or ".." in relative.parts or not re.fullmatch(r"[0-9a-f]{64}", digest):
            raise ValueError("Invalid release file")
        if hashlib.sha256((bundle / relative).read_bytes()).hexdigest() != digest:
            raise ValueError("Release configuration/tooling checksum mismatch")
    return manifest


def validate_config(config, images, public_url):
    services = config["services"]
    for component in COMPONENTS:
        if services[component]["image"] != images[component]:
            raise ValueError("Compose does not match the release image contract")
    web = services["web"]["environment"]
    api = services["webapi"]["environment"]
    if web.get("TRN_TRUSTED_PROXY_HEADERS") != "true" or not api.get("Security__TrustedProxyNetworks__0"):
        raise ValueError("Required trusted proxy settings are missing")
    if web.get("TRN_PUBLIC_ORIGIN", "").rstrip("/") != public_url.rstrip("/"):
        raise ValueError("Public origin does not match the deployment probe origin")
    for value, minimum in ((api.get("Auth__Jwt__Key", ""), 32), (web.get("TRN_BACKEND_API_KEY", ""), 16)):
        if len(value) < minimum or any(p in value.upper() for p in ("CHANGE_ME", "CHANGME")):
            raise ValueError("Missing or placeholder application credentials")
    if not services.get("web-cache-init") or not services.get("operational-logs-init"):
        raise ValueError("Required persistent storage initializers are missing")


def http_probe(base, revision, profile, route=None, api_url=None):
    """Exercise the serving path and optimized assets, not just process health."""
    def get(path, is_json=False):
        request = urllib.request.Request(base.rstrip("/") + path, headers={"Cache-Control": "no-cache"})
        with urllib.request.urlopen(request, timeout=20) as response:
            if path == "/api/readiness" and route and response.headers.get("x-trn-deployment") != route:
                raise RuntimeError("Traffic has not reached the expected deployment slot")
            body = response.read(2 * 1024 * 1024)
            if response.status != 200 or not body:
                raise RuntimeError("Public smoke response was unsuccessful")
            return json.loads(body) if is_json else body
    get("/")
    assets = get("/api/readiness", True)
    if assets.get("status") != "ready" or assets.get("release") != revision:
        raise RuntimeError("Traffic has not reached the expected ready web release")
    diagnostics = get("/api/diagnostics/backend", True)
    if diagnostics.get("ok") is not True:
        raise RuntimeError("Web-to-API smoke check failed")
    get(profile)
    # Exercise an actual anonymous BFF proxy route and its database query as well.
    search = get("/api/trn/public/lol/summoners/search?region=NA1&q=Kr&limit=1", True)
    if not isinstance(search.get("items"), list):
        raise RuntimeError("Public BFF smoke response was invalid")
    if api_url:
        request = urllib.request.Request(api_url.rstrip("/") + "/health/ready", headers={"Cache-Control": "no-cache"})
        with urllib.request.urlopen(request, timeout=20) as response:
            if response.status != 200 or response.headers.get("x-trn-release") != revision:
                raise RuntimeError("Public API has not reached the expected ready release")
            if route and response.headers.get("x-trn-deployment") != route:
                raise RuntimeError("Public API has not reached the expected deployment slot")


def wait_for_public(base, revision, profile, timeout=90, route=None, api_url=None):
    deadline = time.monotonic() + timeout
    while True:
        try:
            http_probe(base, revision, profile, route, api_url)
            return
        except Exception:
            if time.monotonic() >= deadline:
                raise
            time.sleep(3)


class Engine:
    def __init__(self, bundle, state, env_file, project="transcendence", timeout=420):
        self.bundle, self.state, self.env_file = bundle.resolve(), state.resolve(), env_file.resolve()
        self.manifest = load_manifest(self.bundle)
        self.release = self.manifest["release"]
        self.project, self.timeout = project, timeout
        self.attempt = self.state / "attempts" / self.release
        self.attempt.mkdir(parents=True, exist_ok=True)
        self.environment = dict(os.environ)
        self.environment.update({IMAGE_ENV[c]: i for c, i in self.manifest["images"].items()})
        self.override = self.attempt / "override.json"
        write_json(self.override, {"services": {
            "service": {"environment": {"Database__AutoMigrate": "false"}},
            "webapi": {"environment": {"Database__AutoMigrate": "false"}}}})
        self.command = ["docker", "compose", "--project-directory", str(self.bundle), "-p", project,
                        "--env-file", str(self.env_file), "-f", str(self.bundle / "compose.yml"),
                        "-f", str(self.override)]

    def run(self, argv, timeout=300, input=None, combined=False):
        result = subprocess.run(argv, env=self.environment, input=input, capture_output=True, text=True, timeout=timeout)
        if result.returncode:
            # Rendered Compose and inspection output can contain secrets: private file only.
            evidence = (result.stdout + result.stderr)[-65536:]
            (self.attempt / "last-command.log").write_text(evidence)
            (self.attempt / "last-command.log").chmod(0o600)
            raise RuntimeError("Deployment command failed; see private last-command.log")
        return result.stdout + result.stderr if combined else result.stdout

    def compose(self, *args, timeout=300, input=None):
        return self.run(self.command + list(args), timeout, input)

    def cid(self, component):
        return self.compose("ps", "-a", "-q", component).strip()

    def inspect(self, component):
        cid = self.cid(component)
        if not cid:
            raise RuntimeError(f"Missing {component} container")
        return json.loads(self.run(["docker", "inspect", cid]))[0]

    def wait(self, components):
        deadline = time.monotonic() + self.timeout
        pending = set(components)
        while pending and time.monotonic() < deadline:
            for component in tuple(pending):
                data = self.inspect(component)
                state = data["State"]
                if state.get("Health", {}).get("Status") == "healthy":
                    pending.remove(component)
                elif not state.get("Running") or state.get("Health", {}).get("Status") == "unhealthy":
                    raise RuntimeError(f"{component} failed readiness")
            if pending:
                time.sleep(2)
        if pending:
            raise RuntimeError("Application readiness timed out")

    def verify_images(self):
        for component, image in self.manifest["images"].items():
            self.run(["docker", "run", "--rm", COSIGN, "verify", "--certificate-identity", IDENTITY,
                      "--certificate-oidc-issuer", "https://token.actions.githubusercontent.com", image], 180)
            self.run(["docker", "pull", image], 600)
            revision = self.run(["docker", "image", "inspect", image, "--format",
                                 '{{index .Config.Labels "org.opencontainers.image.revision"}}']).strip()
            if revision != self.manifest["revision"]:
                raise ValueError(f"{component} source revision differs from the release contract")

    def check_disk(self):
        reserve = int(os.environ.get("TRN_RELEASE_MIN_FREE_GIB", "5")) * 1024**3
        docker_root = Path(self.run(["docker", "info", "--format", "{{.DockerRootDir}}" ]).strip())
        if reserve < 1024**3 or any(shutil.disk_usage(p).free < reserve for p in (self.state, docker_root)):
            raise RuntimeError("Insufficient disk headroom for release staging and rollback")

    def attest_running(self, components=COMPONENTS):
        for component in components:
            expected = self.run(["docker", "image", "inspect", self.manifest["images"][component],
                                 "--format", "{{.Id}}"]).strip()
            if self.inspect(component)["Image"] != expected:
                raise RuntimeError(f"{component} is running an unexpected image")

    def diagnostics(self):
        command_log = self.attempt / "last-command.log"
        original_failure = command_log.read_bytes() if command_log.exists() else None
        for component in COMPONENTS:
            try:
                container = self.inspect(component)
                # Config.Env is deliberately excluded from failure evidence.
                write_json(self.attempt / f"{self.project}-{component}-state.json", {
                    "image": container["Image"], "state": container["State"]})
                logs = self.run(["docker", "logs", "--timestamps", "--tail", "100", container["Id"]], combined=True)
                (self.attempt / f"{self.project}-{component}.log").write_text(logs[-65536:])
            except (RuntimeError, subprocess.SubprocessError):
                pass
        if original_failure is not None:
            command_log.write_bytes(original_failure)

    def snapshot(self):
        # Capture the installed configuration on first adoption, then the last successful release.
        current = self.state / "current.json"
        if current.exists():
            source = json.loads(current.read_text())
            config = json.loads(Path(source["config"]).read_text())
        else:
            baseline = self.env_file.parent / "compose.yml"
            config = json.loads(self.run(["docker", "compose", "-p", self.project, "--env-file", str(self.env_file),
                                          "-f", str(baseline), "config", "--format", "json"]))
        for component in COMPONENTS:
            if current.exists() and source.get("candidate") and component != "service":
                container = json.loads(self.run(["docker", "inspect", source["candidate"] + "-" + component]))[0]
            else:
                container = self.inspect(component)
            tag = f"transcendence-retained/{component}:{self.release.removeprefix('release-')}"
            self.run(["docker", "tag", container["Image"], tag])
            config["services"][component]["image"] = tag
            config["services"][component]["environment"] = dict(
                (key, value.replace("$", "$$")) for key, value in
                (item.split("=", 1) for item in container["Config"]["Env"] if "=" in item))
            config["services"][component]["environment"]["Database__AutoMigrate"] = "false"
            config["services"][component].pop("build", None)
            capture_runtime_config(config, component, container)
        # Initializers must use retained images too; never resolve their former moving tags.
        desired = json.loads(self.compose("config", "--format", "json"))
        for initializer, component in (("operational-logs-init", "webapi"), ("web-cache-init", "web")):
            if initializer not in config["services"]:
                config["services"][initializer] = desired["services"][initializer]
                config.setdefault("volumes", {}).update(desired.get("volumes", {}))
            config["services"][initializer]["image"] = config["services"][component]["image"]
        path = self.attempt / "previous.json"
        write_json(path, config)
        return path

    def rollback(self, previous, components=COMPONENTS):
        command = self.command
        self.command = ["docker", "compose", "-p", self.project, "-f", str(previous)]
        try:
            self.compose("run", "--rm", "--no-deps", "--pull", "never", "operational-logs-init")
            self.compose("run", "--rm", "--no-deps", "--pull", "never", "web-cache-init")
            for component in components:
                self.compose("up", "-d", "--no-deps", "--no-build", "--pull", "never", component)
                self.wait([component])
        finally:
            self.command = command

    def status(self, phase, error=None):
        value = dict(desired=self.release, phase=phase, checkedAt=time.time(), error=error)
        current = self.state / "current.json"
        value["deployed"] = json.loads(current.read_text())["release"] if current.exists() else "legacy"
        write_json(self.state / "status.json", value)
        try:
            self.export_metrics(phase, value["checkedAt"])
        except OSError:
            print("Release status saved; metrics exporter directory needs attention", flush=True)
        print(json.dumps(value), flush=True)

    def export_metrics(self, phase, checked_at):
        directory = Path(os.environ.get("TRN_RELEASE_TEXTFILE_DIR", self.state / "textfile"))
        directory.mkdir(parents=True, exist_ok=True)
        directory.chmod(0o755)
        temporary = directory / "release.prom.tmp"
        temporary.write_text(
            f'transcendence_release_healthy {int(phase == "healthy")}\n'
            f'transcendence_release_checked_timestamp_seconds {checked_at}\n'
            f'transcendence_release_quarantined {int(phase in ("quarantined", "failed", "rollback_failed"))}\n')
        temporary.chmod(0o644)
        temporary.replace(directory / "release.prom")

    def apply(self, base_url, profile, retain=3, mode="replace", traffic_file=None, network=None, api_url=None):
        if not 2 <= retain <= 10:
            raise ValueError("Retain between 2 and 10 releases")
        failed = self.attempt / "quarantined"
        if failed.exists():
            self.status("quarantined")
            raise RuntimeError("Release is quarantined; inspect diagnostics before explicit retry")
        current_path = self.state / "current.json"
        current = json.loads(current_path.read_text()) if current_path.exists() else None
        prior_pointer = (self.state / "current").readlink() if (self.state / "current").is_symlink() else None
        prior_config = Path(current["config"]).read_bytes() if current and Path(current["config"]).exists() else None
        if current and current.get("candidate") and mode != "blue-green":
            raise ValueError("Route the edge back to standard containers before leaving blue-green mode")
        if current and current["release"] == self.release and current.get("mode", "replace") == mode:
            # A stopped/unhealthy or manually replaced service is still a degraded deployment.
            self.attest_running(["service"] if mode == "blue-green" else COMPONENTS)
            if current.get("candidate"):
                for component in ("webapi", "web"):
                    actual = json.loads(self.run(["docker", "inspect", current["candidate"] + "-" + component]))[0]
                    expected = self.run(["docker", "image", "inspect", self.manifest["images"][component], "--format", "{{.Id}}"]).strip()
                    if actual["Image"] != expected or actual["State"].get("Health", {}).get("Status") != "healthy":
                        raise RuntimeError("Active blue-green slot has drifted or is unhealthy")
            else:
                self.wait(COMPONENTS)
            http_probe(base_url, self.manifest["revision"], profile, current.get("candidate"), api_url)
            self.status("healthy")
            return
        self.status("preflight")
        config = json.loads(self.compose("config", "--format", "json"))
        validate_config(config, self.manifest["images"], base_url)
        if mode == "blue-green" and (traffic_file is None or not traffic_file.is_file()
                                     or traffic_file.suffix not in (".yml", ".yaml") or not network):
            raise ValueError("Blue-green requires an existing Traefik traffic file and application network")
        self.check_disk()
        self.verify_images()
        self.check_disk()
        # A previous CI/staging report is evidence only for the same complete bundle.
        stage(self.bundle, self.attempt / "stage", self.timeout)
        self.status("staged")
        previous = self.snapshot()
        transaction = self.attempt / "transaction.json"
        config_backup = self.attempt / "prior-config.json"
        if prior_config is not None:
            config_backup.write_bytes(prior_config)
            config_backup.chmod(0o600)
        traffic_backup = self.attempt / "prior-traffic.yml"
        if mode == "blue-green":
            traffic_backup.write_bytes(traffic_file.read_bytes())
        write_json(transaction, dict(release=self.release, current=current, previous=str(previous),
            pointer=str(prior_pointer) if prior_pointer else None, mode=mode, base_url=base_url,
            api_url=api_url, profile=profile, traffic_file=str(traffic_file) if traffic_file else None))
        changed = False
        traffic_previous = None
        candidate = None
        try:
            self.compose("run", "--rm", "--no-deps", "--pull", "never", "operational-logs-init")
            self.compose("run", "--rm", "--no-deps", "--pull", "never", "web-cache-init")
            self.compose("run", "--rm", "--no-deps", "--pull", "never", "-e", "Database__AutoMigrate=true",
                         "-e", "Database__MigrateOnly=true", "--name", self.migration_name, "service", timeout=900)
            self.status("deploying")
            changed = True
            if mode == "blue-green":
                candidate = prepare_candidate(self, config, network)
                candidate.compose("up", "-d", "--no-deps", "--no-build", "--pull", "never", "webapi", "web")
                candidate.wait(["webapi", "web"])
                candidate.attest_running(["webapi", "web"])
                local_port = candidate.compose("port", "web", "3000").strip().rsplit(":", 1)[1]
                wait_for_public(f"http://127.0.0.1:{local_port}", self.manifest["revision"], profile, self.timeout)
                traffic_previous = traffic_file.read_bytes()
            else:
                for component in COMPONENTS:
                    self.compose("up", "-d", "--no-deps", "--no-build", "--pull", "never", component)
                    self.wait([component])
            if candidate:
                self.compose("up", "-d", "--no-deps", "--no-build", "--pull", "never", "service")
                self.wait(["service"])
                switch_traffic(traffic_file, candidate.project)
            self.attest_running(["service"] if candidate else COMPONENTS)
            wait_for_public(base_url, self.manifest["revision"], profile,
                            route=candidate.project if candidate else None, api_url=api_url)
            rendered = self.attempt / "deployed.json"
            write_json(rendered, config)
            write_json(current_path, dict(release=self.release, config=str(rendered),
                       candidate=candidate.project if candidate else None, mode=mode, completedAt=time.time()))
            pointer = self.state / ".current.tmp"
            pointer.unlink(missing_ok=True)
            pointer.symlink_to(self.bundle, target_is_directory=True)
            pointer.replace(self.state / "current")
            self.status("healthy")
        except Exception as failure:
            if candidate:
                candidate.diagnostics()
            self.diagnostics()
            failed.write_text(str(failure) + "\n")
            try:
                self.stop_migrator()
                if traffic_previous is not None:
                    temporary = traffic_file.with_suffix(".rollback.tmp")
                    temporary.write_bytes(traffic_previous)
                    temporary.chmod(0o644)
                    temporary.replace(traffic_file)
                if changed:
                    self.rollback(previous, ["service"] if mode == "blue-green" else COMPONENTS)
                if current:
                    wait_for_public(base_url, current["release"].removeprefix("release-"), profile,
                                    route=current.get("candidate"), api_url=api_url)
                    write_json(current_path, current)
                    if prior_config is not None:
                        Path(current["config"]).write_bytes(prior_config)
                else:
                    current_path.unlink(missing_ok=True)
                pointer = self.state / "current"
                pointer.unlink(missing_ok=True)
                if prior_pointer is not None:
                    pointer.symlink_to(prior_pointer, target_is_directory=True)
                rendered = self.attempt / "deployed.json"
                if rendered.exists() and (not current or Path(current["config"]) != rendered):
                    rendered.replace(self.attempt / "unaccepted.json")
                self.status("failed", "Application rollback completed; database migrations were retained")
                transaction.unlink(missing_ok=True)
                if candidate:
                    try:
                        candidate.compose("down", "--remove-orphans")
                    except (RuntimeError, subprocess.SubprocessError):
                        print("Failed candidate retained for manual cleanup", flush=True)
            except Exception:
                self.status("rollback_failed", "Manual intervention required; private diagnostics retained")
            raise
        transaction.unlink(missing_ok=True)
        # Cleanup is after the success commit. A cleanup error must never roll back a healthy release.
        try:
            if candidate:
                if current and current.get("candidate"):
                    old = current["candidate"]
                    self.run(["docker", "compose", "-p", old, "-f", str(Path(current["config"]).parent / "candidate.json"),
                              "stop", old + "-web", old + "-webapi"])
                else:
                    self.compose("stop", "web", "webapi")
                    self.compose("rm", "-f", "web", "webapi")
            retain_releases(self.state, retain)
        except (RuntimeError, subprocess.SubprocessError):
            print("Healthy release retained; housekeeping needs attention", flush=True)

    @property
    def migration_name(self):
        return "trn-migrate-" + self.manifest["revision"][:12]

    def stop_migrator(self):
        result = subprocess.run(["docker", "inspect", self.migration_name], capture_output=True, text=True, timeout=30)
        if result.returncode:
            return
        data = json.loads(result.stdout)[0]
        write_json(self.attempt / "migration-state.json", data["State"])
        logs = subprocess.run(["docker", "logs", "--tail", "100", self.migration_name], capture_output=True, timeout=30)
        (self.attempt / "migration.log").write_bytes((logs.stdout + logs.stderr)[-65536:])
        if data["State"].get("Running"):
            self.run(["docker", "stop", "--time", "30", self.migration_name], timeout=60)

    def recover(self):
        transaction = self.attempt / "transaction.json"
        record = json.loads(transaction.read_text())
        if record["release"] != self.release:
            raise ValueError("Recovery record does not match the verified release")
        self.diagnostics()
        self.stop_migrator()
        current_path = self.state / "current.json"
        current = record["current"]
        try:
            if record["traffic_file"]:
                path = Path(record["traffic_file"])
                temporary = path.with_suffix(".recover.tmp")
                temporary.write_bytes((self.attempt / "prior-traffic.yml").read_bytes())
                temporary.chmod(0o644)
                temporary.replace(path)
            self.rollback(Path(record["previous"]), ["service"] if record["mode"] == "blue-green" else COMPONENTS)
            if current:
                wait_for_public(record["base_url"], current["release"].removeprefix("release-"),
                                record["profile"], route=current.get("candidate"), api_url=record["api_url"])
                write_json(current_path, current)
                backup = self.attempt / "prior-config.json"
                if backup.exists():
                    Path(current["config"]).write_bytes(backup.read_bytes())
            else:
                current_path.unlink(missing_ok=True)
            pointer = self.state / "current"
            pointer.unlink(missing_ok=True)
            if record["pointer"]:
                pointer.symlink_to(record["pointer"], target_is_directory=True)
            (self.attempt / "quarantined").write_text("Interrupted rollout recovered; review before explicit retry.\n")
            self.status("quarantined", "Interrupted rollout restored to its captured application baseline")
            transaction.unlink()
        except Exception:
            self.status("rollback_failed", "Interrupted rollout recovery needs manual intervention")
            raise


def stage(bundle, directory, timeout=420):
    """Disposable production-mode stack. A migration-only worker can never ingest Riot data."""
    directory.mkdir(parents=True, exist_ok=True)
    parent = directory
    directory = Path(tempfile.mkdtemp(prefix="run-", dir=parent))
    env = directory / "stage.env"
    token = secrets.token_hex(32)
    env.write_text("\n".join(("POSTGRES_DB=release_stage", "POSTGRES_USER=postgres",
        f"POSTGRES_PASSWORD={token}", "REDIS_CONNECTION=redis:6379", "RIOT_API_KEY_LOL=RGAPI-unused",
        "JWT_ISSUER=release-stage", "JWT_AUDIENCE=release-stage", f"JWT_SIGNING_KEY={token}",
        "JWT_ACCESS_TOKEN_MINUTES=15", "AUTH_BOOTSTRAP_API_KEY=", f"WEB_TRN_BACKEND_API_KEY={token}",
        "TRN_PUBLIC_ORIGIN=http://127.0.0.1", "PGADMIN_EMAIL=", "PGADMIN_PASSWORD=")) + "\n")
    env.chmod(0o600)
    project = "trn-stage-" + secrets.token_hex(6)
    engine = Engine(bundle, directory, env, project, timeout)
    # Shell/systemd variables outrank --env-file in Compose. Never inherit production
    # cache addresses, credentials, feature flags or PostgreSQL tuning into a drill.
    engine.environment = stage_environment(engine.manifest["images"])
    config = json.loads(engine.compose("config", "--format", "json"))
    config["services"] = {k: v for k, v in config["services"].items()
                          if k in (*COMPONENTS, "postgres", "redis", "web-cache-init", "operational-logs-init")}
    for name, service in config["services"].items():
        service.pop("container_name", None)
        service.pop("build", None)
        service.pop("ports", None)
        service["restart"] = "no"
        service["cpus"] = 1.0
        service["mem_limit"] = "256m" if name == "redis" else "1g"
        service["pids_limit"] = 256
        if name == "service":
            service["environment"]["Database__AutoMigrate"] = "false"
        if name in ("web", "webapi"):
            service["ports"] = [{"target": 3000 if name == "web" else 8080, "published": "0", "host_ip": "127.0.0.1"}]
    for name, network in config.get("networks", {}).items():
        network["name"] = project + "-net-" + name
        network.pop("external", None)
    for name, volume in config.get("volumes", {}).items():
        volume["name"] = project + "-" + name
        volume.pop("external", None)
    rendered = directory / "stage.json"
    write_json(rendered, config)
    engine.command = ["docker", "compose", "-p", project, "-f", str(rendered)]
    try:
        engine.compose("up", "-d", "--no-build", "postgres", "redis", "operational-logs-init", "web-cache-init")
        engine.wait(["postgres", "redis"])
        engine.compose("run", "--rm", "--no-deps", "--pull", "never", "-e", "Database__AutoMigrate=true",
                       "-e", "Database__MigrateOnly=true", "service", timeout=900)
        engine.compose("exec", "-T", "postgres", "psql", "-U", "postgres", "-d", "release_stage",
                       input=(bundle / "scripts/e2e/seed-profile.sql").read_text())
        engine.compose("up", "-d", "--no-deps", "--no-build", "webapi", "web")
        engine.wait(["webapi", "web"])
        port = engine.compose("port", "web", "3000").strip().rsplit(":", 1)[1]
        wait_for_public(f"http://127.0.0.1:{port}", engine.manifest["revision"], "/lol/summoners/na/faker-NA1", timeout)
        engine.attest_running(["webapi", "web"])
    except Exception:
        engine.diagnostics()
        raise
    finally:
        engine.compose("down", "--volumes", "--remove-orphans")
    write_json(parent / "passed.json", {"release": engine.release, "images": engine.manifest["images"], "checkedAt": time.time()})


def stage_environment(images):
    allowed = {"PATH", "HOME", "DOCKER_HOST", "DOCKER_CONFIG", "DOCKER_CONTEXT", "XDG_RUNTIME_DIR",
               "TMPDIR", "HTTP_PROXY", "HTTPS_PROXY", "NO_PROXY", "SSL_CERT_FILE", "SSL_CERT_DIR"}
    environment = {key: value for key, value in os.environ.items() if key in allowed}
    environment.update({IMAGE_ENV[c]: image for c, image in images.items()})
    return environment


def capture_runtime_config(config, component, container):
    """First adoption can encounter host checkout drift: retain actual ports, mounts and networks."""
    service = config["services"][component]
    ports = []
    for target, bindings in (container.get("HostConfig", {}).get("PortBindings") or {}).items():
        number, protocol = target.split("/")
        for binding in bindings or []:
            port = dict(target=int(number), published=binding["HostPort"], protocol=protocol)
            if binding.get("HostIp"):
                port["host_ip"] = binding["HostIp"]
            ports.append(port)
    service["ports"] = ports
    service["volumes"] = []
    for index, mount in enumerate(container.get("Mounts", [])):
        if mount["Type"] not in ("volume", "bind"):
            raise ValueError("Unsupported mount type in rollback snapshot; adopt explicitly first")
        source = mount["Source"]
        if mount["Type"] == "volume":
            source = f"retained-{component}-{index}"
            config.setdefault("volumes", {})[source] = dict(name=mount["Name"], external=True)
        service["volumes"].append(dict(type=mount["Type"], source=source, target=mount["Destination"],
                                       read_only=not mount["RW"]))
    service["networks"] = {}
    for index, (name, details) in enumerate(container.get("NetworkSettings", {}).get("Networks", {}).items()):
        key = f"retained-{component}-{index}"
        config.setdefault("networks", {})[key] = dict(name=name, external=True)
        service["networks"][key] = dict(aliases=details.get("Aliases") or [])


def prepare_candidate(engine, config, network):
    project = "trn-slot-" + engine.manifest["revision"][:12]
    candidate = Engine(engine.bundle, engine.state, engine.env_file, project, engine.timeout)
    config = deepcopy(config)
    config["services"] = {k: v for k, v in config["services"].items() if k in ("webapi", "web")}
    for name, service in config["services"].items():
        service.pop("depends_on", None)
        service.pop("build", None)
        service["container_name"] = project + "-" + name
        service["networks"] = {"candidate": {"aliases": [project + "-" + name]}}
        # Service names must also be unique: Docker Compose adds them as DNS aliases.
        if name == "web":
            service["environment"]["TRN_BACKEND_BASE_URL"] = f"http://{project}-webapi:8080"
            service["ports"] = [{"target": 3000, "published": "0", "host_ip": "127.0.0.1"}]
        else:
            service.pop("ports", None)
            service["environment"]["Security__ForwardedHeaderLimit"] = "2"
    config["services"] = {project + "-" + name: service for name, service in config["services"].items()}
    config["networks"] = {"candidate": {"name": network, "external": True}}
    for volume in config.get("volumes", {}).values():
        volume["external"] = True
    path = engine.attempt / "candidate.json"
    write_json(path, config)
    candidate.command = ["docker", "compose", "-p", project, "-f", str(path)]
    # Map the logical roles used by health/identity checks to unique Compose service keys.
    original = candidate.compose
    def compose(*args, **kwargs):
        return original(*(project + "-" + a if a in ("web", "webapi") else a for a in args), **kwargs)
    candidate.compose = compose
    return candidate


def switch_traffic(path, project):
    if path.suffix not in (".yml", ".yaml"):
        raise ValueError("Traefik traffic files require a YAML suffix")
    value = {"http": {"routers": {
        "web": {"entryPoints": ["web"], "rule": "PathPrefix(`/`)", "service": "web", "middlewares": ["deployment"]},
        "webapi": {"entryPoints": ["webapi"], "rule": "PathPrefix(`/`)", "service": "webapi", "middlewares": ["deployment"]}},
        "middlewares": {"deployment": {"headers": {"customResponseHeaders": {"X-TRN-Deployment": project}}}},
        "services": {name: {"loadBalancer": {"servers": [{"url": f"http://{project}-{name}:{port}"}]}}
                     for name, port in (("web", 3000), ("webapi", 8080))}}}
    write_json(path, value)
    path.chmod(0o644)


def retain_releases(state, retain):
    # Retain tagged images and private rollback configurations. Never run global image prune.
    attempts = sorted((state / "attempts").glob("release-*"), key=lambda p: p.stat().st_mtime, reverse=True)
    protected = {p.name for p in attempts if (p / "deployed.json").exists()}
    protected = set(p.name for p in [p for p in attempts if p.name in protected][:retain])
    protected.update(p.name for p in [p for p in attempts if not (p / "deployed.json").exists()][:retain])
    if (state / "current.json").exists():
        protected.add(json.loads((state / "current.json").read_text())["release"])
    protected_images = set()
    for release in protected:
        manifest = state / "releases" / release / "release.json"
        if manifest.exists():
            protected_images.update(json.loads(manifest.read_text())["images"].values())
    for attempt in attempts:
        if attempt.name in protected:
            continue
        candidate = attempt / "candidate.json"
        if candidate.exists():
            project = "trn-slot-" + attempt.name.removeprefix("release-")[:12]
            subprocess.run(["docker", "compose", "-p", project, "-f", str(candidate), "down", "--remove-orphans"],
                           capture_output=True, timeout=60, check=True)
        # Only our local retention tags are eligible; no volumes or shared registry tags are deleted.
        for component in COMPONENTS:
            subprocess.run(["docker", "image", "rm", f"transcendence-retained/{component}:{attempt.name.removeprefix('release-')}"],
                           capture_output=True, timeout=30)
        bundle = state / "releases" / attempt.name
        if bundle.exists():
            images = json.loads((bundle / "release.json").read_text())["images"].values()
            for image in images:
                if image not in protected_images:
                    # Unforced removal cannot delete images still used by any running/stopped container.
                    subprocess.run(["docker", "image", "rm", image], capture_output=True, timeout=30)
            shutil.rmtree(bundle)
        shutil.rmtree(attempt)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=("apply", "stage", "status", "retry", "recover"))
    parser.add_argument("--bundle", type=Path, required=True)
    parser.add_argument("--state", type=Path, default=Path("/var/lib/transcendence-release"))
    parser.add_argument("--env-file", type=Path, default=Path("/root/transcendence/.env"))
    parser.add_argument("--base-url", default="https://transcend.kronic.one")
    parser.add_argument("--api-url", default="https://api.kronic.one")
    parser.add_argument("--profile-path", default="/lol/summoners/na/Kronic-NA1")
    parser.add_argument("--retain", type=int, default=3)
    parser.add_argument("--mode", choices=("replace", "blue-green"), default="replace")
    parser.add_argument("--traffic-file", type=Path)
    parser.add_argument("--network", default="transcendence_transcendence-net")
    args = parser.parse_args()
    def interrupted(signum, frame):
        signal.signal(signal.SIGTERM, signal.SIG_IGN)
        signal.signal(signal.SIGINT, signal.SIG_IGN)
        raise RuntimeError("Deployment interrupted; restoring the captured application baseline")
    signal.signal(signal.SIGTERM, interrupted)
    signal.signal(signal.SIGINT, interrupted)
    os.umask(0o077)
    args.state.mkdir(parents=True, exist_ok=True)
    if args.action == "status":
        print((args.state / "status.json").read_text())
    elif args.action == "stage":
        stage(args.bundle, args.state / "staging")
    else:
        # Direct operator retries serialize with scheduled deployment and the legacy updater too.
        with contextlib.ExitStack() as locks:
            for path in (args.state / "deploy.lock", *(() if os.environ.get("TRN_RELEASE_BOOTSTRAPPED") == "1"
                         else (Path("/run/transcendence-deploy.lock"),))):
                descriptor = locks.enter_context(path.open("w"))
                fcntl.flock(descriptor, fcntl.LOCK_EX)
            apply_locked(args)


def apply_locked(args):
    engine = Engine(args.bundle, args.state, args.env_file)
    if args.action == "recover":
        engine.recover()
        return
    interrupted = list((args.state / "attempts").glob("release-*/transaction.json"))
    if interrupted:
        engine.status("failed", "Interrupted rollout requires explicit recover with its retained bundle")
        raise RuntimeError("Recover the interrupted release before starting another deployment: " + interrupted[0].parent.name)
    if args.action == "retry":
        (engine.attempt / "quarantined").unlink(missing_ok=True)
    try:
        engine.apply(args.base_url, args.profile_path, args.retain, args.mode, args.traffic_file, args.network, args.api_url)
    except Exception as error:
        if not (engine.attempt / "quarantined").exists():
            engine.status("failed", type(error).__name__)
        raise


if __name__ == "__main__":
    main()
