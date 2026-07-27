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
#   This script replaces wud for the three app services with an OUTBOUND-ONLY
#   release poll (prod -> ghcr): it compares both the manifest digest and signed
#   OCI revision label, needs no inbound exposure, no CI secret, and no self-hosted
#   runner. Run it every ~60s via the systemd timer
#   (scripts/ops/transcendence-deploy.timer).
#
# WHAT IT DOES
#   For each app service, resolve the current `:main` manifest digest and OCI revision
#   label from ghcr and compare both to the running container. The revision comparison
#   catches registries/build pipelines that reuse a manifest digest across rebuilt tags.
#   If either differs, `compose pull` + `up -d` just
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
  "service:transcendence-service:transcendence-service"
  "webapi:transcendence-webapi:transcendence-webapi"
  "web:transcendence-web:transcendence-web"
)

log() { printf '%s poll-deploy: %s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$*"; }

run_logged() {
  local label="$1"
  shift
  local output_file rc
  mkdir -p "$STATE_DIR"
  output_file="${STATE_DIR}/last-command.log"
  : >"$output_file"
  chmod 0600 "$output_file"

  "$@" >"$output_file" 2>&1
  rc=$?
  if [ "$rc" -eq 0 ]; then
    rm -f "$output_file"
    return 0
  fi

  log "ERROR ${label} exited ${rc}; command output follows"
  while IFS= read -r line; do
    log "  ${line}"
  done < <(tail -n 40 "$output_file")
  return "$rc"
}

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

remote_revision() {
  local repo="$1"
  docker buildx imagetools inspect "${REGISTRY_HOST}/${OWNER}/${repo}:main" \
    --format '{{index .Image.Config.Labels "org.opencontainers.image.revision"}}' \
    2>/dev/null
}

# The manifest digest the running container's image was pulled at.
local_digest() {
  local container="$1" image_id
  image_id="$(docker inspect "$container" --format '{{.Image}}' 2>/dev/null)" || return 1
  [ -z "$image_id" ] && return 1
  docker image inspect "$image_id" --format '{{range .RepoDigests}}{{println .}}{{end}}' 2>/dev/null \
    | sed -n "s#^${REGISTRY_HOST}/${OWNER}/[^@]*@##p" | head -1
}

local_revision() {
  local container="$1"
  docker inspect "$container" \
    --format '{{ index .Config.Labels "org.opencontainers.image.revision" }}' \
    2>/dev/null
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

rollback_service() {
  local svc="$1" container="$2" repo="$3" previous_image_id="$4" failed_release="$5"
  local failed_file="${STATE_DIR}/${svc}-failed-digest" previous_container_id="${6:-}"
  local current_container_id previous_name
  mkdir -p "$STATE_DIR"
  printf '%s\n' "$failed_release" >"$failed_file"

  if [ -z "$previous_image_id" ]; then
    log "ERROR ${svc}: rollback unavailable because the previous image ID is missing"
    return 1
  fi

  # Compose can fail after renaming the old container but before the replacement takes ownership of
  # the canonical name. Prefer restoring that exact still-running container; it is the fastest and
  # safest rollback and avoids another recreate during an already-partial operation.
  if [ -n "$previous_container_id" ] && docker inspect "$previous_container_id" >/dev/null 2>&1; then
    current_container_id="$(docker inspect "$container" --format '{{.Id}}' 2>/dev/null || true)"
    if [ -n "$current_container_id" ] && [ "$current_container_id" != "$previous_container_id" ]; then
      log "ROLLBACK ${svc}: removing partial replacement ${current_container_id:0:19}"
      if ! run_logged "${svc} remove partial replacement" docker rm -f "$current_container_id"; then
        return 1
      fi
    fi

    previous_name="$(docker inspect "$previous_container_id" --format '{{.Name}}' 2>/dev/null | sed 's#^/##')"
    if [ "$previous_name" != "$container" ]; then
      log "ROLLBACK ${svc}: restoring canonical container name from ${previous_name:-unknown}"
      if ! run_logged "${svc} restore container name" docker rename "$previous_container_id" "$container"; then
        return 1
      fi
    fi
    if ! run_logged "${svc} restart previous container" docker start "$previous_container_id"; then
      return 1
    fi
    if wait_for_healthy "$svc" "$container"; then
      log "ROLLED BACK ${svc} by restoring the previous container; release is quarantined until :main changes"
      return 0
    fi
    log "WARN ${svc}: exact-container recovery failed health verification; falling back to recreate"
  fi

  log "ROLLBACK ${svc}: recreating previous image ${previous_image_id:0:19}"
  if ! run_logged "${svc} retag previous image" \
      docker image tag "$previous_image_id" "${REGISTRY_HOST}/${OWNER}/${repo}:main"; then
    log "ERROR ${svc}: could not retag previous image for rollback"
    return 1
  fi
  current_container_id="$(docker inspect "$container" --format '{{.Id}}' 2>/dev/null || true)"
  if [ -n "$current_container_id" ] &&
     ! run_logged "${svc} remove failed replacement" docker rm -f "$current_container_id"; then
    return 1
  fi
  if ! run_logged "${svc} rollback recreate" \
      docker compose -p "$COMPOSE_PROJECT" --env-file "$ENV_FILE" -f "$COMPOSE_FILE" \
      up -d --no-deps --pull never "$svc"; then
    log "ERROR ${svc}: rollback recreate failed"
    return 1
  fi
  if ! wait_for_healthy "$svc" "$container"; then
    log "ERROR ${svc}: rollback image failed health verification"
    return 1
  fi

  log "ROLLED BACK ${svc}; release is quarantined until :main changes"
  return 0
}

run_migrations() {
  log "MIGRATE service: applying backward-compatible database migrations before replacement"
  run_logged "service migration" \
    docker compose -p "$COMPOSE_PROJECT" --env-file "$ENV_FILE" -f "$COMPOSE_FILE" \
    run --rm --no-deps --pull never \
    -e Database__AutoMigrate=true \
    -e Database__MigrateOnly=true \
    service
}

deploy_one() {
  local svc="$1" container="$2" repo="$3"
  local remote current remote_rev current_rev release_id previous_image_id previous_container_id failed_file failed_release
  remote="$(remote_digest "$repo")"
  if [ -z "$remote" ]; then record_resolution_failure "$svc" remote; return 1; fi
  clear_resolution_failure "$svc" remote
  remote_rev="$(remote_revision "$repo")"
  if [ -z "$remote_rev" ]; then record_resolution_failure "$svc" remote-revision; return 1; fi
  clear_resolution_failure "$svc" remote-revision
  release_id="${remote}:${remote_rev}"
  failed_file="${STATE_DIR}/${svc}-failed-digest"
  failed_release=""
  if [ -r "$failed_file" ]; then
    read -r failed_release <"$failed_file" || failed_release=""
  fi
  if [ "$release_id" = "$failed_release" ]; then
    log "SKIP ${svc}: remote release ${remote_rev:0:12} previously failed and remains quarantined"
    return 0
  fi
  current="$(local_digest "$container")"
  if [ -z "$current" ]; then record_resolution_failure "$svc" local; return 1; fi
  clear_resolution_failure "$svc" local
  current_rev="$(local_revision "$container")"
  if [ -z "$current_rev" ]; then record_resolution_failure "$svc" local-revision; return 1; fi
  clear_resolution_failure "$svc" local-revision
  if [ "$remote" = "$current" ] && [ "$remote_rev" = "$current_rev" ]; then
    rm -f "$failed_file"
    return 0
  fi
  previous_image_id="$(docker inspect "$container" --format '{{.Image}}' 2>/dev/null)"
  previous_container_id="$(docker inspect "$container" --format '{{.Id}}' 2>/dev/null)"

  log "UPDATE ${svc}: rev ${current_rev:0:12} -> ${remote_rev:0:12}; deploying"
  if [ "${DRY_RUN:-0}" = "1" ]; then
    log "DRY_RUN ${svc}: would compose pull, migrate if needed, recreate, and health-check"
    return 0
  fi
  if ! run_logged "${svc} pull" \
      docker compose -p "$COMPOSE_PROJECT" --env-file "$ENV_FILE" -f "$COMPOSE_FILE" pull "$svc"; then
    log "ERROR ${svc}: pull failed"; notify "⚠️ deploy: ${svc} pull failed"; return 1
  fi
  if [ "$svc" = "service" ] && ! run_migrations; then
    mkdir -p "$STATE_DIR"
    printf '%s\n' "$release_id" >"$failed_file"
    log "ERROR ${svc}: pre-deploy migration failed; existing worker remains active"
    notify "🚨 deploy: ${svc} migration failed; release quarantined"
    return 1
  fi
  if ! run_logged "${svc} up -d" \
      docker compose -p "$COMPOSE_PROJECT" --env-file "$ENV_FILE" -f "$COMPOSE_FILE" \
      up -d --no-deps --pull never "$svc"; then
    log "ERROR ${svc}: up -d failed"
    rollback_service \
      "$svc" "$container" "$repo" "$previous_image_id" "$release_id" "$previous_container_id" || true
    notify "🚨 deploy: ${svc} up -d failed; rollback attempted"
    return 1
  fi
  if ! wait_for_healthy "$svc" "$container"; then
    rollback_service \
      "$svc" "$container" "$repo" "$previous_image_id" "$release_id" "$previous_container_id" || true
    notify "🚨 deploy: ${svc} failed health verification; rollback attempted"
    return 1
  fi
  rm -f "$failed_file"
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
    if ! deploy_one "$svc" "$container" "$repo"; then
      rc=1
      log "ABORT remaining services: ${svc} did not deploy successfully"
      break
    fi
  done
  docker image prune -f >/dev/null 2>&1 || true
  exit $rc
}

if [ "${POLL_DEPLOY_LIB_ONLY:-0}" != "1" ]; then
  main "$@"
fi
