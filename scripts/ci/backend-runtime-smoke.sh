#!/usr/bin/env bash
# Exercise the published applications in their real runtime images, not the SDK image.
set -euo pipefail

component="${1:?Usage: backend-runtime-smoke.sh <webapi|service> <image>}"
image="${2:?An image built and loaded into Docker is required}"
case "$component" in webapi|service) ;; *) echo "Unsupported component: $component" >&2; exit 2 ;; esac

prefix="trn-runtime-${component}-${RANDOM}-$$"
network="${prefix}-net"
database="${prefix}-pg"
redis="${prefix}-redis"
application="${prefix}-app"

cleanup() {
  result=$?
  if (( result != 0 )); then
    docker logs --tail 100 "$application" 2>&1 || true
    docker logs --tail 30 "$database" 2>&1 || true
  fi
  docker rm -f "$application" "$database" "$redis" >/dev/null 2>&1 || true
  docker network rm "$network" >/dev/null 2>&1 || true
  exit "$result"
}
trap cleanup EXIT

docker network create "$network" >/dev/null
docker run -d --name "$database" --network "$network" --network-alias postgres \
  --tmpfs /var/lib/postgresql \
  -e POSTGRES_PASSWORD=runtime-smoke-only -e POSTGRES_DB=runtime_smoke \
  postgres:18-alpine >/dev/null
docker run -d --name "$redis" --network "$network" --network-alias redis \
  --tmpfs /data redis:8-alpine >/dev/null

dependencies_ready=false
for (( attempt=0; attempt<60; attempt++ )); do
  if docker exec "$database" pg_isready -U postgres -d runtime_smoke >/dev/null 2>&1 \
    && [[ "$(docker exec "$redis" redis-cli ping 2>/dev/null)" == PONG ]]; then
    dependencies_ready=true
    break
  fi
  sleep 1
done
[[ "$dependencies_ready" == true ]] || { echo "Runtime smoke dependencies did not become ready" >&2; exit 1; }

# Disposable private network, no host ports, persisted volumes, or real service credentials.
environment=(
  -e "ConnectionStrings__MainDatabase=Host=postgres;Database=runtime_smoke;Username=postgres;Password=runtime-smoke-only"
  -e "ConnectionStrings__Redis=redis:6379,abortConnect=false"
  -e "Auth__Jwt__Key=RuntimeSmokeOnly-NotAProductionSecret-0123456789"
  -e "OperationalLogs__DirectoryPath=/tmp/transcendence-logs"
  -e "Telemetry__Enabled=false"
)

if [[ "$component" == service ]]; then
  docker create --name "$application" --network "$network" "${environment[@]}" \
    -e "RiotApi__League__ApiKey=RGAPI-runtime-smoke-unused" \
    -e "Database__AutoMigrate=true" -e "Database__MigrateOnly=true" "$image" >/dev/null
  timeout 120s docker start --attach "$application"
  [[ "$(docker inspect -f '{{.State.ExitCode}}' "$application")" == 0 ]]
  migration_count="$(docker exec "$database" psql -U postgres -d runtime_smoke -Atc \
    'SELECT COUNT(*) FROM "__EFMigrationsHistory";')"
  (( migration_count > 0 ))
  echo "Worker runtime started and applied ${migration_count} migrations."
else
  docker run -d --name "$application" --network "$network" "${environment[@]}" \
    -e "ASPNETCORE_ENVIRONMENT=Production" -e "ASPNETCORE_HTTP_PORTS=8080" \
    -e "Security__TrustedProxyNetworks__0=172.16.0.0/12" \
    -e "Database__AutoMigrate=false" "$image" >/dev/null
  for (( attempt=0; attempt<60; attempt++ )); do
    if docker exec "$application" curl -fsS http://localhost:8080/health/ready >/dev/null 2>&1; then
      echo "API runtime started and passed PostgreSQL/Redis readiness."
      exit 0
    fi
    [[ "$(docker inspect -f '{{.State.Running}}' "$application")" == true ]] || exit 1
    sleep 1
  done
  echo "API runtime did not become ready" >&2
  exit 1
fi
