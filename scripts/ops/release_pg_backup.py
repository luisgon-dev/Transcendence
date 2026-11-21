"""Native PostgreSQL changed-block backups, encrypted by the existing restic repository.

pg_basebackup tracks changed pages through PostgreSQL WAL summaries. Restic stores
the resulting artifacts; it is not used to infer which live database pages changed.
"""
import hashlib
import json
import os
from pathlib import Path
import re
import secrets
import shutil
import socket
import subprocess
import tarfile
import tempfile
import time

TAG = "transcendence-postgres-native"
METADATA = "native-backup.json"
SNAPSHOT_ID = re.compile(r"[0-9a-f]{64}")
LSN = re.compile(r"[0-9A-F]+/[0-9A-F]+")
DIGEST = re.compile(r"sha256:[0-9a-f]{64}")
PLATFORM = "linux/amd64"
OFFICIAL = "docker.io/library/postgres"
OCI_INDEX = "application/vnd.oci.image.index.v1+json"
OCI_MANIFEST = "application/vnd.oci.image.manifest.v1+json"
CURRENT_INDEX = "sha256:75dcac3dfb1cd01ca3536910bbc84128759362f8b118bf8c55b38a94f68f3b73"
LEGACY_CHILDREN = {
    (CURRENT_INDEX, PLATFORM):
        "sha256:fa1b61f5a4844f4d4b73c9b15716cfc75fe70c931f2d32723630f3b900056366",
}


def image_identity(info):
    repo, sep, index = info["image"].partition("@")
    platform = info["platform"]
    if not sep or repo not in ("postgres", OFFICIAL) or not DIGEST.fullmatch(index):
        raise ValueError("Expected an immutable Docker Official PostgreSQL repository digest")
    child = info.get("image_manifest", LEGACY_CHILDREN.get((index, platform)))
    if platform != PLATFORM or not isinstance(child, str) or not DIGEST.fullmatch(child):
        raise ValueError("Expected a known linux/amd64 PostgreSQL child manifest")
    if (index, platform) in LEGACY_CHILDREN and child != LEGACY_CHILDREN[index, platform]:
        raise ValueError("Unexpected child manifest for the known PostgreSQL index")
    return index, platform, child


def setting(name, default, minimum, maximum):
    value = int(os.environ.get(name, default))
    if not minimum <= value <= maximum:
        raise ValueError(f"{name} must be between {minimum} and {maximum}")
    return value


def snapshot_tag(snapshot, prefix):
    values = [tag[len(prefix):] for tag in snapshot.get("tags", []) if tag.startswith(prefix)]
    if len(values) != 1:
        raise ValueError("Native backup catalog contains missing or ambiguous dependency tags")
    return values[0]


def retention(catalog, keep_full=2):
    """Retain whole recent chains, including every dependency; fail closed on damage."""
    if keep_full < 1:
        raise ValueError("Keep at least one complete full backup chain")
    records = {item["id"]: item for item in catalog}
    if len(records) != len(catalog):
        raise ValueError("Duplicate backup snapshot identifiers")
    for identifier, item in records.items():
        if not SNAPSHOT_ID.fullmatch(identifier):
            raise ValueError("Invalid native snapshot identifier")
        kind = snapshot_tag(item, "trn-kind:")
        parent = snapshot_tag(item, "trn-parent:")
        if kind not in ("full", "incremental") or (kind == "full") != (parent == "none"):
            raise ValueError("Invalid native backup dependency")
        if parent != "none" and parent not in records:
            raise ValueError("Native backup dependency is missing; refusing expiration")
    def root(identifier):
        visited = set()
        while True:
            if identifier in visited:
                raise ValueError("Native backup dependency cycle")
            visited.add(identifier)
            parent = snapshot_tag(records[identifier], "trn-parent:")
            if parent == "none":
                return identifier
            identifier = parent
    roots = {identifier: root(identifier) for identifier in records}
    full = sorted((r for r in catalog if snapshot_tag(r, "trn-kind:") == "full"),
                  key=lambda r: r["time"], reverse=True)
    preserved = {item["id"] for item in full[:keep_full]}
    # A new full must not immediately discard the most recent daily recovery point.
    if catalog:
        preserved.add(roots[max(catalog, key=lambda r: r["time"])["id"]])
    return sorted(identifier for identifier in records if roots[identifier] not in preserved)


def safe_extract(archive, destination):
    """Extract only regular files/directories; this installation has no tablespaces."""
    with tarfile.open(archive, "r:*") as source:
        members = source.getmembers()
        for member in members:
            path = Path(member.name)
            if path.is_absolute() or ".." in path.parts or not (member.isfile() or member.isdir()):
                raise ValueError("Unsafe or unsupported member in database backup archive")
            member.mode = 0o700 if member.isdir() else 0o600
            member.uid, member.gid = os.getuid(), os.getgid()
            member.uname = member.gname = ""
        source.extractall(destination, members=members)


class NativeBackups:
    def __init__(self, api, state, container="transcendence-postgres", reserve_bytes=5 * 1024**3):
        self.api = api
        self.state = Path(state).resolve()
        self.container = container
        self.reserve = reserve_bytes
        self.state.mkdir(parents=True, exist_ok=True)
        self.metadata_path = self.state / METADATA

    def call(self, args, **kwargs):
        # Restic need not use every host CPU to process already-compressed archives.
        environment = dict(os.environ, GOMAXPROCS="1")
        return self.api.run(args, diagnostics=self.state / "last-command.log", env=environment, **kwargs)

    def sql(self, query):
        client = ('exec psql -U "${POSTGRES_USER:-postgres}" '
                  '-d "${POSTGRES_DB:-${POSTGRES_USER:-postgres}}" -qAt -v ON_ERROR_STOP=1')
        return self.call(["docker", "exec", "-i", self.container, "sh", "-c", client],
                         input=("SET statement_timeout='10s';\n" + query + "\n").encode()).decode().strip()

    def prepare_source(self):
        # Explicit full-baseline setup only. A daily incremental must never hide
        # a gap in WAL summaries by starting an unexpected full scan.
        self.sql("ALTER SYSTEM SET summarize_wal = on;\n"
                 "ALTER SYSTEM SET wal_summary_keep_time = '14d';\nSELECT pg_reload_conf();")
        for _ in range(10):
            if self.sql("SHOW summarize_wal;") == "on":
                return
            time.sleep(1)
        raise RuntimeError("WAL summarization did not become active after reload")

    def source(self):
        query = '''SELECT json_build_object(
            'system', (SELECT system_identifier::text FROM pg_control_system()),
            'version', current_setting('server_version_num')::int,
            'checksums', current_setting('data_checksums'),
            'summarize', current_setting('summarize_wal'),
            'user', current_user, 'database', current_database(),
            'bytes', (SELECT sum(pg_database_size(oid)) FROM pg_database),
            'tablespaces', (SELECT count(*) FROM pg_tablespace WHERE oid NOT IN (1663,1664)));
        '''
        info = json.loads(self.sql(query))
        if info["version"] < 180000 or info["summarize"] != "on":
            raise ValueError("Native backups require PostgreSQL 18+ with summarize_wal enabled before the full baseline")
        if info["tablespaces"] != 0:
            raise ValueError("Native backup tooling currently supports the default PostgreSQL tablespaces only")
        running, = json.loads(self.call(["docker", "container", "inspect", "--format",
            '[{"Image":{{json .Image}},"ImageManifestDescriptor":{{json .ImageManifestDescriptor}}}]',
            self.container]))
        child = running.get("ImageManifestDescriptor") or {}
        platform = child.get("platform") or {}
        info.update(image="postgres@" + running["Image"], image_manifest=child.get("digest"),
                    platform=f"{platform.get('os')}/{platform.get('architecture')}")
        self.cached_image(info)
        return info

    def cached_image(self, info):
        index, platform, child = image_identity(info)
        reference = OFFICIAL + "@" + index

        def inspect(*args):
            return json.loads(self.call(["docker", "image", "inspect", *args, reference,
                                         "--format", "{{json .}}"]))

        try:
            root = inspect()
            selected = inspect("--platform", platform)
        except (RuntimeError, subprocess.CalledProcessError) as error:
            raise RuntimeError("Exact PostgreSQL image is unavailable locally; pre-stage the official index and platform") from error
        descriptor = root.get("Descriptor") or {}
        if (descriptor.get("digest") != index or descriptor.get("mediaType") != OCI_INDEX
                or not {"postgres@" + index, reference}.intersection(root.get("RepoDigests") or [])):
            raise ValueError("Cached PostgreSQL image does not match the official OCI index")
        manifest = selected.get("Descriptor") or {}
        annotations = manifest.get("annotations") or {}
        if (manifest.get("digest") != child or manifest.get("mediaType") != OCI_MANIFEST
                or (selected.get("Os"), selected.get("Architecture")) != ("linux", "amd64")
                or annotations.get("org.opencontainers.image.url") != "https://hub.docker.com/_/postgres"
                or not annotations.get("org.opencontainers.image.source", "").startswith(
                    "https://github.com/docker-library/postgres.git#")):
            raise ValueError("Cached PostgreSQL image does not match the recorded official platform manifest")
        return reference

    def catalog(self, system=None, host=None):
        args = ["restic", "snapshots", "--json", "--tag", TAG,
                "--host", host or socket.gethostname()]
        if system is not None:
            args += ["--tag", TAG + ",trn-system:" + str(system)]
        snapshots = json.loads(self.call(args))
        # --tag expressions are ORed by restic; enforce the complete scope locally.
        return [item for item in snapshots if TAG in item.get("tags", []) and "trn-verified" in item.get("tags", []) and
                (system is None or "trn-system:" + str(system) in item.get("tags", []))]

    def read_metadata(self, snapshot):
        matches = [path for path in snapshot["paths"] if Path(path).name == METADATA]
        if len(matches) != 1:
            raise ValueError("Native snapshot is missing its recovery metadata")
        info = json.loads(self.call(["restic", "dump", snapshot["id"], matches[0]]))
        if info.get("format") != 1 or info.get("system") != snapshot_tag(snapshot, "trn-system:"):
            raise ValueError("Native backup metadata does not match its catalog entry")
        if info.get("kind") != snapshot_tag(snapshot, "trn-kind:") or (info.get("parent") or "none") != snapshot_tag(snapshot, "trn-parent:"):
            raise ValueError("Native snapshot metadata has different dependencies from its catalog")
        payload = Path(info["payload"])
        if not payload.is_absolute() or payload == Path("/") or ".." in payload.parts:
            raise ValueError("Invalid native payload path")
        return info

    def tool(self, image, directory, arguments, network="none", name=None):
        command = ["docker", "run", "--pull=never", "--platform", PLATFORM, "--rm", "--read-only", "--cap-drop", "ALL",
                   "--security-opt", "no-new-privileges", "--pids-limit", "64",
                   "--cpus", "1", "--memory", "512m", "--user", f"{os.getuid()}:{os.getgid()}",
                   "--network", network, "--tmpfs", "/tmp:rw,noexec,nosuid,size=16m",
                   "--mount", f"type=bind,src={directory},dst={directory}"]
        if name:
            command += ["--name", name, "--env", "PGAPPNAME=" + name]
        return command + [image, "ionice", "-c", "3"] + arguments

    def idle_backends(self, application, seen):
        if not re.fullmatch(r"trn-native-backup-[0-9a-f]{12}", application):
            raise ValueError("Invalid native backup application identity")
        output = self.sql("SELECT pid FROM pg_stat_activity WHERE backend_type='walsender' "
                          f"AND application_name='{application}';")
        for line in output.splitlines():
            if not line.isdecimal():
                continue
            pid = int(line)
            if pid not in seen:
                self.call(["docker", "exec", "--user", "postgres", self.container,
                           "ionice", "-c", "3", "-p", str(pid)])
                seen.add(pid)

    def verify(self, image, payload):
        self.call(self.tool(image, payload.parent,
                           ["pg_verifybackup", "--exit-on-error", "--no-parse-wal", str(payload)]))
        # pg_verifybackup cannot parse WAL inside tar backups. Verify that part
        # separately rather than treating --no-parse-wal as complete validation.
        manifest = json.loads((payload / "backup_manifest").read_text())
        with tempfile.TemporaryDirectory(dir=payload.parent, prefix="wal-verify-") as temporary:
            wal = Path(temporary)
            safe_extract(payload / "pg_wal.tar.gz", wal)
            for span in manifest["WAL-Ranges"]:
                if not isinstance(span["Timeline"], int) or not all(LSN.fullmatch(span[key]) for key in ("Start-LSN", "End-LSN")):
                    raise ValueError("Invalid WAL range in backup manifest")
                self.call(self.tool(image, payload.parent, ["pg_waldump", "--quiet", "--path", str(wal),
                    "--timeline", str(span["Timeline"]), "--start", span["Start-LSN"], "--end", span["End-LSN"]]))

    def backup(self, full=False, archive_state=None):
        recovery = os.environ.get("TRN_BACKUP_APP_ENV_FILE")
        if recovery:
            recovery = Path(recovery)
            if not recovery.is_absolute() or not recovery.is_file() or recovery.stat().st_mode & 0o077:
                raise ValueError("Application recovery environment must be an existing absolute private file")
            recovery = recovery.resolve()
        source = self.source()
        catalog = self.catalog(source["system"])
        retention(catalog)  # Validate all dependencies before starting expensive work.
        parent = max(catalog, key=lambda r: r["time"]) if catalog else None
        if not full and parent is None:
            raise ValueError("A native full baseline is required; refusing an unexpected full database scan")
        recent = setting("TRN_BACKUP_SKIP_RECENT_FULL_SECONDS", "21600", 0, 86400)
        if not full and snapshot_tag(parent, "trn-kind:") == "full" and time.time() - self.timestamp(parent) < recent:
            print("A recent full baseline already protects this window; daily incremental deferred", flush=True)
            return None
        if shutil.disk_usage(self.state).free < int(source["bytes"]) + self.reserve:
            raise RuntimeError("Insufficient staging headroom for native backup worst case")
        application = "trn-native-backup-" + secrets.token_hex(6)
        rate = setting("TRN_BACKUP_FULL_RATE_KIB" if full else "TRN_BACKUP_INCREMENTAL_RATE_KIB",
                       "16384" if full else "4096", 32, 65536)
        result_snapshot = None
        try:
            with tempfile.TemporaryDirectory(dir=self.state, prefix="native-") as directory:
                directory = Path(directory)
                payload = directory / "payload"
                args = ["pg_basebackup", "--host", "127.0.0.1", "--username", source["user"],
                        "--pgdata", str(payload), "--format", "tar", "--compress", "client-gzip:1",
                        "--wal-method", "stream", "--checkpoint", "spread", "--max-rate", str(rate),
                        "--manifest-checksums", "SHA256", "--label", application, "--no-password"]
                if not full:
                    previous = self.read_metadata(parent)
                    if (image_identity(previous) != image_identity(source) or previous["checksums"] != source["checksums"]
                            or previous["version"] // 10000 != source["version"] // 10000):
                        raise ValueError("PostgreSQL image/version/checksums changed; take an explicit new full baseline")
                    reference = directory / "reference-manifest.json"
                    reference.write_bytes(self.call(["restic", "dump", parent["id"], previous["payload"] + "/backup_manifest"]))
                    if hashlib.sha256(reference.read_bytes()).hexdigest() != previous["manifest_sha256"]:
                        raise RuntimeError("The reference manifest failed encrypted repository readback")
                    args += ["--incremental", str(reference)]
                command = self.tool(source["image"], directory, args,
                                    network="container:" + self.container, name=application)
                with (directory / "client.log").open("wb") as log:
                    process = subprocess.Popen(command, stdout=log, stderr=log)
                    deadline = time.monotonic() + setting(
                        "TRN_BACKUP_FULL_TIMEOUT_SECONDS" if full else "TRN_BACKUP_INCREMENTAL_TIMEOUT_SECONDS",
                        "43200" if full else "7200", 60, 86400)
                    seen = set()
                    try:
                        while process.poll() is None:
                            if time.monotonic() >= deadline or shutil.disk_usage(self.state).free < self.reserve:
                                raise RuntimeError("Native backup stopped to preserve headroom or meet its deadline")
                            self.idle_backends(application, seen)
                            time.sleep(2)
                        if process.returncode:
                            (self.state / "last-command.log").write_bytes((directory / "client.log").read_bytes()[-65536:])
                            raise RuntimeError("Native PostgreSQL backup failed; inspect private diagnostics")
                    finally:
                        try:
                            if process.poll() is None:
                                try:
                                    subprocess.run(["docker", "rm", "--force", application], capture_output=True, timeout=60)
                                finally:
                                    process.kill()
                                    process.wait()
                        finally:
                            # Close only this operation's replication sessions even if Docker cleanup failed.
                            self.sql("SELECT pg_terminate_backend(pid) FROM pg_stat_activity "
                                     f"WHERE application_name='{application}' AND backend_type='walsender';")
                self.verify(source["image"], payload)
                manifest = payload / "backup_manifest"
                info = dict(source, format=1, kind="full" if full else "incremental",
                            parent=parent["id"] if not full else None, payload=str(payload),
                            manifest_sha256=hashlib.sha256(manifest.read_bytes()).hexdigest(),
                            rate_kib=rate, artifact_bytes=sum(p.stat().st_size for p in payload.iterdir()))
                self.metadata_path.write_text(json.dumps(info, indent=2) + "\n")
                self.metadata_path.chmod(0o600)
                command = ["restic", "backup", "--json", "--compression", "off", "--read-concurrency", "1",
                           "--tag", TAG, "--tag", "trn-system:" + source["system"],
                           "--tag", "trn-pending", "--tag", "trn-run:" + application,
                           "--tag", "trn-kind:" + info["kind"], "--tag", "trn-parent:" + (info["parent"] or "none"),
                           str(payload), str(self.metadata_path)]
                if recovery:
                    command.append(str(recovery))
                if archive_state:
                    command += ["--exclude", str(archive_state / "run.lock"),
                                "--exclude", str(archive_state / "exclusive.lock"), str(archive_state)]
                output = self.call(command).decode().splitlines()
                summary = [json.loads(line) for line in output if line.startswith("{")]
                result_snapshot = next(record["snapshot_id"] for record in reversed(summary)
                                       if record.get("message_type") == "summary")
                self.call(["restic", "check"])
                stored = self.call(["restic", "dump", result_snapshot, str(manifest)])
                if hashlib.sha256(stored).hexdigest() != info["manifest_sha256"]:
                    raise RuntimeError("Encrypted NAS manifest readback failed")
                self.call(["restic", "tag", "--remove", "trn-pending", "--add", "trn-verified", result_snapshot])
                current = self.catalog(source["system"])
                verified = [item for item in current if "trn-run:" + application in item["tags"]]
                if len(verified) != 1:
                    raise RuntimeError("Could not identify the verified native snapshot")
                result_snapshot = verified[0]["id"]
                expired = retention(current, setting("TRN_BACKUP_KEEP_FULL_CHAINS", "2", 1, 12))
                if expired:
                    print(f"Retention reports {len(expired)} eligible snapshots; no snapshots removed", flush=True)
                # Persist useful sizes, never database contents or credentials.
                receipt = dict(info, snapshot=result_snapshot)
                (self.state / "last-native-backup.json").write_text(json.dumps(receipt, indent=2) + "\n")
        finally:
            self.metadata_path.unlink(missing_ok=True)
        # Success is recorded after client, verification and temporary-file cleanup.
        self.api.metric(self.state, "database_backup")
        return result_snapshot

    @staticmethod
    def timestamp(snapshot):
        from datetime import datetime
        # restic may include nanoseconds; datetime accepts and truncates them.
        return datetime.fromisoformat(snapshot["time"].replace("Z", "+00:00")).timestamp()

    def restore(self, source_host, snapshot_id=None):
        """Manual/off-host recovery test. Physical indexes are restored, not rebuilt."""
        catalog = self.catalog(host=source_host)
        if not catalog:
            raise ValueError("No verified native backup exists for the requested source host")
        selected = (next((item for item in catalog if item["id"] == snapshot_id), None)
                    if snapshot_id else max(catalog, key=lambda item: item["time"]))
        if selected is None:
            raise ValueError("Requested native snapshot is not in the verified catalog")
        system = snapshot_tag(selected, "trn-system:")
        catalog = [item for item in catalog if snapshot_tag(item, "trn-system:") == system]
        retention(catalog)
        records = {item["id"]: item for item in catalog}
        chain = []
        item = selected
        while True:
            metadata = self.read_metadata(item)
            chain.append((item, metadata))
            parent = snapshot_tag(item, "trn-parent:")
            if parent == "none":
                break
            item = records[parent]
        chain.reverse()
        final = chain[-1][1]
        if any(image_identity(info) != image_identity(final)
               or info["checksums"] != final["checksums"] for _, info in chain):
            raise ValueError("Native backup chain crosses PostgreSQL image or architecture boundaries")
        image = self.cached_image(final)
        manifests = []
        required = int(final["bytes"]) + self.reserve
        for item, info in chain:
            raw = self.call(["restic", "dump", item["id"], info["payload"] + "/backup_manifest"])
            if hashlib.sha256(raw).hexdigest() != info["manifest_sha256"]:
                raise RuntimeError("Native recovery manifest failed integrity verification")
            manifest = json.loads(raw)
            required += sum(entry["Size"] for entry in manifest["Files"])
            # Conservatively include compressed staging and WAL as well as the
            # combined database, instead of filling a small application volume.
            required += 2 * int(info["artifact_bytes"])
            manifests.append(raw)
        if shutil.disk_usage(self.state).free < required:
            raise RuntimeError("Insufficient recovery workspace; use an off-host filesystem with more headroom")
        container = "trn-native-restore-" + secrets.token_hex(6)
        observation = None
        with tempfile.TemporaryDirectory(dir=self.state, prefix="native-restore-") as working:
            working = Path(working)
            inputs = []
            for index, ((item, info), manifest) in enumerate(zip(chain, manifests)):
                download = working / f"download-{index}"
                download.mkdir()
                for filename in ("base.tar.gz", "pg_wal.tar.gz", "backup_manifest"):
                    with (download / filename).open("wb") as output:
                        result = subprocess.run(["restic", "dump", item["id"], info["payload"] + "/" + filename],
                                                stdout=output, stderr=subprocess.DEVNULL,
                                                timeout=self.api.command_timeout(), env=dict(os.environ, GOMAXPROCS="1"))
                    if result.returncode:
                        raise RuntimeError("Could not retrieve native backup artifacts")
                if (download / "backup_manifest").read_bytes() != manifest:
                    raise RuntimeError("Recovery manifest changed during retrieval")
                self.verify(image, download)
                data = working / f"input-{index}"
                data.mkdir()
                safe_extract(download / "base.tar.gz", data)
                wal = data / "pg_wal"
                wal.mkdir(exist_ok=True)
                safe_extract(download / "pg_wal.tar.gz", wal)
                shutil.copyfile(download / "backup_manifest", data / "backup_manifest")
                shutil.rmtree(download)
                inputs.append(data)
            output = working / "combined"
            self.call(self.tool(image, working,
                                ["pg_combinebackup", "--output", str(output)] + [str(path) for path in inputs]))
            self.call(self.tool(image, working, ["pg_verifybackup", "--exit-on-error", str(output)]))
            hba = working / "test-hba.conf"
            hba.write_text("local all all trust\n")
            hba.chmod(0o444)
            try:
                self.call(["docker", "run", "--pull=never", "-d", "--name", container, "--platform", final["platform"],
                           "--network", "none", "--cpus", "1", "--memory", "2g", "--pids-limit", "128",
                           "--security-opt", "no-new-privileges", "--env", "PGDATA=/pgdata",
                           "--mount", f"type=bind,src={output},dst=/pgdata",
                           "--mount", f"type=bind,src={hba},dst=/run/test-hba.conf,readonly",
                           image, "postgres", "-c", "listen_addresses=", "-c", "unix_socket_directories=/tmp",
                           "-c", "hba_file=/run/test-hba.conf", "-c", "archive_mode=off",
                           "-c", "summarize_wal=off", "-c", "autovacuum=off"])
                for _ in range(300):
                    ready = subprocess.run(["docker", "exec", container, "pg_isready", "-h", "/tmp",
                                            "-U", final["user"]], capture_output=True, timeout=15)
                    if ready.returncode == 0:
                        break
                    time.sleep(1)
                else:
                    raise RuntimeError("Recovered native database did not become ready")
                query = ('SET statement_timeout=30000; SET default_transaction_read_only=on; '
                         'SELECT json_build_object(\'migrationCount\', (SELECT count(*) FROM "__EFMigrationsHistory"), '
                         '\'summoners\', (SELECT count(*) FROM "Summoners"), '
                         "'system', (SELECT system_identifier::text FROM pg_control_system()));")
                raw = self.call(["docker", "exec", container, "psql", "-h", "/tmp", "-U", final["user"],
                                 "-d", final["database"], "-qAt", "-v", "ON_ERROR_STOP=1", "-c", query])
                observation = json.loads(raw)
                if observation["system"] != system or observation["migrationCount"] < 1:
                    raise RuntimeError("Native restored database failed application/cluster identity checks")
                observation.update(snapshot=selected["id"], chain_length=len(chain))
                # An optional fixture/operator query runs only against this isolated
                # database. It cannot connect to a serving database or to a network.
                acceptance = os.environ.get("TRN_RESTORE_ACCEPTANCE_SQL_FILE")
                if acceptance:
                    acceptance = Path(acceptance)
                    if not acceptance.is_absolute() or not acceptance.is_file():
                        raise ValueError("Recovery acceptance query must be an existing absolute file")
                    self.call(["docker", "exec", "-i", container, "psql", "-h", "/tmp", "-U", final["user"],
                               "-d", final["database"], "-qAt", "-v", "ON_ERROR_STOP=1"],
                              input=("SET default_transaction_read_only=on; SET statement_timeout=30000;\n" + acceptance.read_text()).encode())
            finally:
                removed = subprocess.run(["docker", "rm", "--force", "--volumes", container],
                                         capture_output=True, timeout=90)
                if removed.returncode:
                    raise RuntimeError("Native recovery container cleanup failed; inspect the private runtime")
                if os.getuid() != 0:
                    # The official image gives PGDATA to its postgres UID. Return
                    # only this disposable bind directory to a non-root operator.
                    self.call(["docker", "run", "--pull=never", "--platform", PLATFORM, "--rm", "--network", "none", "--read-only", "--user", "0:0",
                               "--cap-drop", "ALL", "--cap-add", "CHOWN", "--cap-add", "DAC_OVERRIDE",
                               "--cpus", "1", "--memory", "256m", "--mount", f"type=bind,src={working},dst={working}",
                               image, "chown", "-R", f"{os.getuid()}:{os.getgid()}", str(working)])
        receipt = self.state / "last-native-restore.json"
        receipt.write_text(json.dumps(observation, indent=2) + "\n")
        receipt.chmod(0o600)
        self.api.metric(self.state, "database_restore_drill")
        return observation
