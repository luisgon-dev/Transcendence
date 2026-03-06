#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"

mkdir -p "$ROOT/openapi"

if command -v dotnet >/dev/null 2>&1; then
  DOTNET_BIN="dotnet"
elif command -v dotnet.exe >/dev/null 2>&1; then
  DOTNET_BIN="dotnet.exe"
elif [[ -x "/c/Program Files/dotnet/dotnet.exe" ]]; then
  DOTNET_BIN="/c/Program Files/dotnet/dotnet.exe"
else
  echo "dotnet not found in PATH."
  exit 127
fi

if [[ "$DOTNET_BIN" == *.exe ]]; then
  if command -v cygpath >/dev/null 2>&1; then
    ROOT_DOTNET="$(cygpath -w "$ROOT")"
  elif command -v wslpath >/dev/null 2>&1; then
    ROOT_DOTNET="$(wslpath -w "$ROOT")"
  else
    ROOT_DOTNET="$(pwd -W)"
  fi
  WEBAPI_PROJECT="$ROOT_DOTNET\\Transcendence.WebAPI\\Transcendence.WebAPI.csproj"
else
  WEBAPI_PROJECT="$ROOT/Transcendence.WebAPI/Transcendence.WebAPI.csproj"
fi

"$DOTNET_BIN" build -c Release -m:1 "$WEBAPI_PROJECT"

# The WebAPI program requires these values at startup (even for Swagger generation).
export ConnectionStrings__MainDatabase="${ConnectionStrings__MainDatabase:-Host=localhost;Port=5432;Database=transcendence;Username=postgres;Password=postgres}"
export ConnectionStrings__Redis="${ConnectionStrings__Redis:-localhost:6379}"
export ConnectionStrings__RiotApi="${ConnectionStrings__RiotApi:-RGAPI-00000000-0000-0000-0000-000000000000}"
export Auth__Jwt__Key="${Auth__Jwt__Key:-OPENAPI_EXPORT_ONLY_CHANGE_THIS_32_PLUS_CHARS}"
export ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-Production}"
export Swagger__Enable="${Swagger__Enable:-true}"
APP_ARGS=(
  "--ConnectionStrings:MainDatabase=$ConnectionStrings__MainDatabase"
  "--ConnectionStrings:Redis=$ConnectionStrings__Redis"
  "--ConnectionStrings:RiotApi=$ConnectionStrings__RiotApi"
  "--Auth:Jwt:Key=$Auth__Jwt__Key"
  "--Swagger:Enable=$Swagger__Enable"
)

SWAGGER_URL="${SWAGGER_URL:-http://127.0.0.1:5057/swagger/v1/swagger.json}"
SWAGGER_OUT="$ROOT/openapi/transcendence.v1.json"
if LOG_FILE="$(mktemp "${TMPDIR%/}/trn-openapi-XXXXXX.log" 2>/dev/null)"; then
  :
elif LOG_FILE="$(mktemp -t trn-openapi 2>/dev/null)"; then
  :
else
  echo "Failed to create temporary log file for OpenAPI export."
  exit 1
fi

"$DOTNET_BIN" run --no-build --configuration Release \
  --no-launch-profile \
  --project "$WEBAPI_PROJECT" \
  --urls "http://127.0.0.1:5057" \
  -- "${APP_ARGS[@]}" >"$LOG_FILE" 2>&1 &
API_PID=$!

cleanup() {
  if kill -0 "$API_PID" 2>/dev/null; then
    kill "$API_PID" 2>/dev/null || true
    wait "$API_PID" 2>/dev/null || true
  fi
}
trap cleanup EXIT

for _ in $(seq 1 60); do
  if curl --fail --silent --show-error "$SWAGGER_URL" -o "$SWAGGER_OUT"; then
    break
  fi

  if ! kill -0 "$API_PID" 2>/dev/null; then
    echo "WebAPI exited before swagger export completed."
    cat "$LOG_FILE"
    exit 1
  fi

  sleep 1
done

if [[ ! -s "$SWAGGER_OUT" ]]; then
  echo "Failed to download swagger from $SWAGGER_URL."
  cat "$LOG_FILE"
  exit 1
fi

if [[ -n "$(tail -c 1 "$SWAGGER_OUT")" ]]; then
  echo >> "$SWAGGER_OUT"
fi
