#!/usr/bin/env bash
#
# poll-deploy.sh — deterministic pull-based deploy for the Transcendence prod stack.
#
# WHY THIS EXISTS
#   Prod is a single Docker host on a private LAN (no public SSH, no tunnel), so a
#   cloud GitHub runner cannot push a deploy to it. wud (what's-up-docker) was meant
#   to auto-pull `:main`, but wud 8.2.2 silently fails to resolve the ghcr manifest
#   digest for these packages (reports updateAvailable=false even when the registry
#   digest changed), so pushes never auto-deployed. See task P1.3b.
#
#   This script replaces wud for the three app services with a dead-simple, reliable
#   digest poll: it is OUTBOUND-ONLY (prod -> ghcr), needs no inbound exposure, no CI
#   secret, and no self-hosted runner. Run it every ~60s via the systemd timer
#   (scripts/ops/transcendence-deploy.timer).
#
# WHAT IT DOES
#   For each app service, resolve the current `:main` manifest digest from ghcr
#   (anonymous token — the packages are public) and compare it to the digest the
#   running container was pulled at. If they differ, `compose pull` + `up -d` just
#   that service (--no-deps, so postgres/redis are never touched), then optionally
#   post a Discord notification.
#
# INSTALL (on prod, as root):
#   install -D -m 0755 poll-deploy.sh /root/deploy/poll-deploy.sh
#   install -D -m 0644 transcendence-deploy.service /etc/systemd/system/transcendence-deploy.service
#   install -D -m 0644 transcendence-deploy.timer   /etc/systemd/system/transcendence-deploy.timer
#   systemctl daemon-reload && systemctl enable --now transcendence-deploy.timer
#
set -uo pipefail

REGISTRY_HOST="ghcr.io"
OWNER="luisgon-dev"
COMPOSE_DIR="/var/lib/docker/volumes/portainer_data/_data/compose/2"
COMPOSE_PROJECT="transcendence"
ENV_FILE="${COMPOSE_DIR}/stack.env"
COMPOSE_FILE="${COMPOSE_DIR}/docker-compose.yml"
LOCK_FILE="/run/transcendence-deploy.lock"
STATE_DIR="${POLL_DEPLOY_STATE_DIR:-/var/lib/transcendence-deploy}"
RESOLUTION_ALERT_THRESHOLD="${POLL_DEPLOY_RESOLUTION_ALERT_THRESHOLD:-3}"
HEALTH_TIMEOUT_SECONDS="${POLL_DEPLOY_HEALTH_TIMEOUT_SECONDS:-420}"
HEALTH_POLL_SECONDS="${POLL_DEPLOY_HEALTH_POLL_SECONDS:-5}"

[[ "$RESOLUTION_ALERT_THRESHOLD" =~ ^[1-9][0-9]*$ ]] || RESOLUTION_ALERT_THRESHOLD=3
[[ "$HEALTH_TIMEOUT_SECONDS" =~ ^[1-9][0-9]*$ ]] || HEALTH_TIMEOUT_SECONDS=420
[[ "$HEALTH_POLL_SECONDS" =~ ^[1-9][0-9]*$ ]] || HEALTH_POLL_SECONDS=5

# service (compose) : container name : ghcr image repo
SERVICES=(
  "web:transcendence-web:transcendence-web"
  "webapi:transcendence-webapi:transcendence-webapi"
  "service:transcendence-service:transcendence-service"
)

log() { printf '%s poll-deploy: %s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$*"; }

# Resolve the remote :main manifest digest via an anonymous ghcr pull token.
remote_digest() {
  local repo="$1" tok
  tok="$(curl -fsSL --max-time 20 \
        "https://${REGISTRY_HOST}/token?scope=repository:${OWNER}/${repo}:pull&service=${REGISTRY_HOST}" \
        2>/dev/null | sed -n 's/.*"token":"\([^"]*\)".*/\1/p')"
  [ -z "$tok" ] && return 1
  curl -fsSL -I --max-time 20 \
    -H "Authorization: Bearer ${tok}" \
    -H "Accept: application/vnd.docker.distribution.manifest.v2+json" \
    -H "Accept: application/vnd.oci.image.index.v1+json" \
    -H "Accept: application/vnd.oci.image.manifest.v1+json" \
    "https://${REGISTRY_HOST}/v2/${OWNER}/${repo}/manifests/main" 2>/dev/null \
    | tr -d '\r' | sed -n 's/^[Dd]ocker-[Cc]ontent-[Dd]igest: //p' | head -1
}

# The manifest digest the running container's image was pulled at.
local_digest() {
  local container="$1" image_id
  image_id="$(docker inspect "$container" --format '{{.Image}}' 2>/dev/null)" || return 1
  [ -z "$image_id" ] && return 1
  docker image inspect "$image_id" --format '{{range .RepoDigests}}{{println .}}{{end}}' 2>/dev/null \
    | sed -n "s#^${REGISTRY_HOST}/${OWNER}/[^@]*@##p" | head -1
}

notify() {
  local msg="$1" url
  url="$(sed -n 's/^ALERTS_WEBHOOK_URL=//p' "$ENV_FILE" 2>/dev/null | head -1)"
  [ -z "$url" ] && return 0
  curl -fsS --max-time 15 -H 'Content-Type: application/json' \
    -d "$(printf '{"content":"%s"}' "$msg")" "$url" >/dev/null 2>&1 || true
}

record_resolution_failure() {
  local svc="$1" kind="$2" state_file count=0
  mkdir -p "$STATE_DIR"
  state_file="${STATE_DIR}/${svc}-${kind}.count"
  if [ -r "$state_file" ]; then
    read -r count <"$state_file" || count=0
  fi
  [[ "$count" =~ ^[0-9]+$ ]] || count=0
  count=$((count + 1))
  printf '%s\n' "$count" >"$state_file"

  if [ "$count" -eq "$RESOLUTION_ALERT_THRESHOLD" ]; then
    log "ERROR ${svc}: ${kind} digest resolution failed ${count} consecutive times"
    notify "🚨 deploy poll: ${svc} ${kind} digest resolution failed ${count} consecutive times"
  elif [ "$count" -gt "$RESOLUTION_ALERT_THRESHOLD" ]; then
    log "WARN ${svc}: ${kind} digest resolution still failing (${count} consecutive; alerted at ${RESOLUTION_ALERT_THRESHOLD})"
  else
    log "WARN ${svc}: could not resolve ${kind} digest (${count}/${RESOLUTION_ALERT_THRESHOLD})"
  fi
}

clear_resolution_failure() {
  local svc="$1" kind="$2"
  rm -f "${STATE_DIR}/${svc}-${kind}.count"
}

container_health_status() {
  local container="$1"
  docker inspect "$container" \
    --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}no-healthcheck:{{.State.Status}}{{end}}' \
    2>/dev/null
}

wait_for_healthy() {
  local svc="$1" container="$2" started status detail
  started=$SECONDS

  while (( SECONDS - started < HEALTH_TIMEOUT_SECONDS )); do
    status="$(container_health_status "$container")"
    case "$status" in
      healthy)
        log "HEALTHY ${svc}: container passed its healthcheck"
        return 0
        ;;
      unhealthy|exited|dead|no-healthcheck:exited|no-healthcheck:dead)
        break
        ;;
      no-healthcheck:*)
        log "ERROR ${svc}: deployed image has no container healthcheck (${status#no-healthcheck:})"
        return 1
        ;;
    esac
    sleep "$HEALTH_POLL_SECONDS"
  done

  detail="$(docker inspect "$container" --format '{{if .State.Health}}{{range .State.Health.Log}}{{.Output}}{{end}}{{else}}{{.State.Error}}{{end}}' 2>/dev/null | tail -c 500)"
  log "ERROR ${svc}: health verification failed after ${HEALTH_TIMEOUT_SECONDS}s (status=${status:-missing}${detail:+; detail=${detail}})"
  return 1
}

deploy_one() {
  local svc="$1" container="$2" repo="$3" remote current
  remote="$(remote_digest "$repo")"
  if [ -z "$remote" ]; then record_resolution_failure "$svc" remote; return 1; fi
  clear_resolution_failure "$svc" remote
  current="$(local_digest "$container")"
  if [ -z "$current" ]; then record_resolution_failure "$svc" local; return 1; fi
  clear_resolution_failure "$svc" local
  if [ "$remote" = "$current" ]; then return 0; fi

  log "UPDATE ${svc}: ${current:0:19} -> ${remote:0:19}; deploying"
  if [ "${DRY_RUN:-0}" = "1" ]; then log "DRY_RUN ${svc}: would compose pull + up -d --no-deps"; return 0; fi
  if ! docker compose -p "$COMPOSE_PROJECT" --env-file "$ENV_FILE" -f "$COMPOSE_FILE" pull "$svc" >/dev/null 2>&1; then
    log "ERROR ${svc}: pull failed"; notify "⚠️ deploy: ${svc} pull failed"; return 1
  fi
  if ! docker compose -p "$COMPOSE_PROJECT" --env-file "$ENV_FILE" -f "$COMPOSE_FILE" up -d --no-deps "$svc" >/dev/null 2>&1; then
    log "ERROR ${svc}: up -d failed"; notify "🚨 deploy: ${svc} up -d FAILED"; return 1
  fi
  if ! wait_for_healthy "$svc" "$container"; then
    notify "🚨 deploy: ${svc} failed post-deploy health verification"
    return 1
  fi
  local rev
  rev="$(docker inspect "$container" --format '{{ index .Config.Labels "org.opencontainers.image.revision" }}' 2>/dev/null)"
  log "DEPLOYED ${svc} -> rev ${rev:0:12}"
  notify "✅ deployed ${svc} @ ${rev:0:12}"
}

main() {
  # Single-flight: never let two polls overlap a recreate.
  exec 9>"$LOCK_FILE"
  flock -n 9 || { log "another run in progress; exiting"; exit 0; }

  [ -f "$COMPOSE_FILE" ] || { log "FATAL compose file not found: $COMPOSE_FILE"; exit 1; }
  local rc=0
  for entry in "${SERVICES[@]}"; do
    IFS=':' read -r svc container repo <<<"$entry"
    deploy_one "$svc" "$container" "$repo" || rc=1
  done
  docker image prune -f >/dev/null 2>&1 || true
  exit $rc
}

if [ "${POLL_DEPLOY_LIB_ONLY:-0}" != "1" ]; then
  main "$@"
fi
