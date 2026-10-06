#!/usr/bin/env bash
#
# Nightly frontend lab performance sweep.
#
# Runs the CI lab runner against the live deployment and drops a Prometheus exposition file
# where node-exporter's textfile collector will serve it. Nothing is pushed and nothing is
# exposed: the box measures itself, exactly like transcendence-postgres-performance-report.
#
# Installed to /root/deploy/web-perf-sweep.sh and invoked by transcendence-web-perf.service.
# See scripts/ops/README.md.
set -Eeuo pipefail

IMAGE="${PERF_IMAGE:-ghcr.io/luisgon-dev/transcendence-perf:main}"
BASE_URL="${PERF_BASE_URL:-https://transcend.kronic.one}"
STATE_DIR="${PERF_STATE_DIR:-/var/lib/transcendence-perf}"
TEXTFILE_DIR="${PERF_TEXTFILE_DIR:-${STATE_DIR}/textfile}"
SAMPLES="${PERF_SAMPLES:-3}"
OUT_NAME="web_lab.prom"
CONTAINER_NAME="transcendence-web-perf-runner"
TIMEOUT_SECONDS="${PERF_TIMEOUT_SECONDS:-1500}"
[[ "$TIMEOUT_SECONDS" =~ ^[1-9][0-9]*$ ]] || exit 2

# The Docker daemon owns the runner, so killing this systemd client does not stop Chromium.
install -d -m 0755 "$STATE_DIR"
exec 9>"${STATE_DIR}/sweep.lock"
flock -n 9 || exit 0

log() { printf '%s %s\n' "$(date -Is)" "$*"; }

log "sweep starting: image=${IMAGE} base=${BASE_URL} samples=${SAMPLES}"

install -d -m 0755 "${TEXTFILE_DIR}"

if ! docker pull --quiet "${IMAGE}" >/dev/null; then
  log "WARN: could not refresh the image; falling back to the locally cached copy"
  if ! docker image inspect "${IMAGE}" >/dev/null 2>&1; then
    log "ERROR: no local image either, nothing to run"
    exit 1
  fi
fi

# The container writes into a staging dir we own, then we move the result into place. Two
# reasons: the container runs as a non-root user that will not own the host textfile dir, and
# node-exporter parses whatever it finds, so the file must appear atomically and complete.
#
# Staging lives under the state directory rather than /tmp on purpose. The unit sets
# PrivateTmp=true, so a mktemp path here resolves inside systemd's per-unit /tmp namespace —
# but `docker run` is executed by the daemon *outside* that namespace, where the path does not
# exist, so Docker silently creates a fresh root-owned directory and the non-root container
# gets EACCES writing into it.
SCRATCH="${STATE_DIR}/staging"
rm -rf "${SCRATCH}"
install -d -m 0777 "${SCRATCH}"
cleanup() {
  docker rm -f "$CONTAINER_NAME" >/dev/null 2>&1 || true
  rm -rf "${SCRATCH}"
}
trap cleanup EXIT
trap 'exit 143' TERM
trap 'exit 130' INT
docker rm -f "$CONTAINER_NAME" >/dev/null 2>&1 || true

if timeout --signal=TERM --kill-after=15 "$TIMEOUT_SECONDS" docker run --rm \
  --name "$CONTAINER_NAME" \
  --cpus="${PERF_CPUS:-1.5}" \
  --memory="${PERF_MEMORY_LIMIT:-1536m}" \
  --memory-swap="${PERF_MEMORY_LIMIT:-1536m}" \
  --pids-limit=256 \
  --network host \
  --shm-size=1g \
  --tmpfs /tmp:rw,nosuid,nodev,size=256m,mode=1777 \
  -v "${SCRATCH}:/out" \
  "${IMAGE}" \
  --base-url "${BASE_URL}" \
  --routes scripts/perf/routes.prod.json \
  --samples "${SAMPLES}" \
  --report-dir /out/reports \
  --prom-out /out/${OUT_NAME}
then
  if [[ -s "${SCRATCH}/${OUT_NAME}" ]]; then
    install -m 0644 "${SCRATCH}/${OUT_NAME}" "${TEXTFILE_DIR}/${OUT_NAME}.tmp"
    mv -f "${TEXTFILE_DIR}/${OUT_NAME}.tmp" "${TEXTFILE_DIR}/${OUT_NAME}"
    rm -rf "${STATE_DIR}/reports"
    mv "${SCRATCH}/reports" "${STATE_DIR}/reports"
    log "sweep complete: $(grep -c '^transcendence_web_lab' "${TEXTFILE_DIR}/${OUT_NAME}") samples published"
  else
    log "ERROR: runner exited 0 but produced no exposition file"
    exit 1
  fi
else
  # Deliberately leave the previous file in place rather than deleting it. The staleness alert
  # on transcendence_web_lab_last_success_unixtime_seconds is what surfaces this; wiping the
  # file would instead make the series vanish, which reads as "no data" rather than "broken".
  log "ERROR: sweep failed; retaining the previous exposition for the staleness alert to catch"
  exit 1
fi
