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

log() { printf '%s %s\n' "$(date -Is)" "$*"; }

log "sweep starting: image=${IMAGE} base=${BASE_URL} samples=${SAMPLES}"

install -d -m 0755 "${TEXTFILE_DIR}"

# `docker pull` cannot extract this image on this host. Layers download fine and then the
# daemon's unpacker dies in its tmpmount — "mount callback failed ... mkdir /usr/share/pipewire"
# on an Alpine base, "lchown /usr/share/menu" on a Debian one — and leaves "lease does not
# exist" behind it. The image is not at fault: it pulls and runs elsewhere, and every smaller
# image in this fleet pulls here. It is Docker 29.2.1's pull path on a large layer.
#
# containerd's own unpacker handles the same image without complaint, and because Docker 29 uses
# the containerd image store, anything ctr pulls into the `moby` namespace is immediately visible
# to docker. So: try docker, fall back to ctr, and only then give up on a cached copy.
pull_image() {
  if docker pull --quiet "${IMAGE}" >/dev/null 2>&1; then
    log "pulled with docker"
    return 0
  fi
  log "WARN: docker pull failed; retrying via containerd (see scripts/ops/README.md)"
  if command -v ctr >/dev/null 2>&1 \
     && ctr -n moby images pull --platform linux/amd64 "${IMAGE}" >/dev/null 2>&1; then
    log "pulled with ctr"
    return 0
  fi
  return 1
}

if ! pull_image; then
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
trap 'rm -rf "${SCRATCH}"' EXIT

if docker run --rm \
  --network host \
  --shm-size=1g \
  -v "${SCRATCH}:/out" \
  "${IMAGE}" \
  --base-url "${BASE_URL}" \
  --routes scripts/perf/routes.prod.json \
  --samples "${SAMPLES}" \
  --prom-out /out/${OUT_NAME}
then
  if [[ -s "${SCRATCH}/${OUT_NAME}" ]]; then
    install -m 0644 "${SCRATCH}/${OUT_NAME}" "${TEXTFILE_DIR}/${OUT_NAME}.tmp"
    mv -f "${TEXTFILE_DIR}/${OUT_NAME}.tmp" "${TEXTFILE_DIR}/${OUT_NAME}"
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
