#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"

BASE_URL="${BASE_URL:-http://localhost:3000}"
API_READY_URL="${API_READY_URL:-http://localhost:8080/health/ready}"
WEB_READY_URL="${WEB_READY_URL:-$BASE_URL}"
WAIT_SECONDS="${WAIT_SECONDS:-120}"

wait_for_url() {
  local url="$1"
  local name="$2"
  local deadline=$((SECONDS + WAIT_SECONDS))

  until curl --fail --silent --show-error "$url" >/dev/null; do
    if (( SECONDS >= deadline )); then
      echo "Timed out waiting for $name at $url after ${WAIT_SECONDS}s."
      docker compose ps || true
      exit 1
    fi

    sleep 2
  done
}

if [[ ! -f ".env" && -f ".env.example" ]]; then
  cp ".env.example" ".env"
  echo "Created .env from .env.example."
fi

docker compose up --build -d

wait_for_url "$API_READY_URL" "WebAPI readiness"
wait_for_url "$WEB_READY_URL" "web frontend"

BASE_URL="$BASE_URL" npx playwright test "$@"
