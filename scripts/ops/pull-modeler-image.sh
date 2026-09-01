#!/usr/bin/env bash
#
# pull-modeler-image.sh — pull the modeler image, with retries, and say what we ended up on.
#
# WHY THIS EXISTS
#   `docker compose pull` of this image fails intermittently on the prod host with
#
#       failed commit on ref "layer-sha256:...": commit failed: lease does not exist: not found
#
#   That is containerd's content lease expiring while a large layer is extracted onto spinning
#   disk. pyarrow is a single ~142 MB wheel and cannot be split further (deleting files across
#   layers writes overlayfs whiteouts this host cannot extract), so the layer cannot be made small
#   enough to remove the race -- it has to be retried. A second attempt reuses whatever the first
#   one landed and has always succeeded.
#
#   The failure used to be invisible: the modeler unit calls this from `ExecStartPre=-`, so a failed
#   pull was ignored and the run silently proceeded on a stale image. That happened -- a run
#   executed two-commits-old code for hours while the fix sat unpulled in the registry. This script
#   therefore ALWAYS logs the revision it finished on, so staleness is visible in the journal even
#   when the pull fails and the run is allowed to continue.
set -uo pipefail

COMPOSE_DIR="${POLL_DEPLOY_COMPOSE_DIR:-/root/transcendence}"
COMPOSE_PROJECT="${COMPOSE_PROJECT:-transcendence}"
ENV_FILE="${COMPOSE_DIR}/.env"
COMPOSE_FILE="${COMPOSE_DIR}/compose.yml"
IMAGE="${MODELER_IMAGE:-ghcr.io/luisgon-dev/transcendence-analytics-modeler:main}"
ATTEMPTS="${MODELER_PULL_ATTEMPTS:-3}"
BACKOFF="${MODELER_PULL_BACKOFF_SECONDS:-10}"

revision() {
  docker image inspect "$IMAGE" \
    --format '{{ index .Config.Labels "org.opencontainers.image.revision" }}' 2>/dev/null \
    || echo "<absent>"
}

before="$(revision)"
for attempt in $(seq 1 "$ATTEMPTS"); do
  if docker compose -p "$COMPOSE_PROJECT" --env-file "$ENV_FILE" -f "$COMPOSE_FILE" \
       --profile analytics-modeling pull --quiet analytics-modeler 2>&1; then
    echo "modeler image pull succeeded on attempt ${attempt}/${ATTEMPTS}"
    break
  fi
  echo "modeler image pull attempt ${attempt}/${ATTEMPTS} failed"
  [ "$attempt" -lt "$ATTEMPTS" ] && sleep "$BACKOFF"
done

after="$(revision)"
if [ "$after" = "<absent>" ]; then
  echo "WARNING: no modeler image present after ${ATTEMPTS} pull attempts; the run cannot start"
elif [ "$before" = "$after" ]; then
  echo "modeler image revision ${after} (unchanged)"
else
  echo "modeler image revision ${before} -> ${after}"
fi
# Never blocks the run: an unreachable registry should not stop modelling on the image already
# present. The revision line above is what makes a stale image visible rather than silent.
exit 0
