#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"

mkdir -p "$ROOT/openapi"

dotnet tool restore
dotnet build -c Release "$ROOT/Transcendence.WebAPI/Transcendence.WebAPI.csproj"

# The WebAPI program requires these values at startup (even for Swagger generation).
export ConnectionStrings__MainDatabase="${ConnectionStrings__MainDatabase:-Host=localhost;Port=5432;Database=transcendence;Username=postgres;Password=postgres}"
export ConnectionStrings__Redis="${ConnectionStrings__Redis:-localhost:6379}"
export ConnectionStrings__RiotApi="${ConnectionStrings__RiotApi:-RGAPI-00000000-0000-0000-0000-000000000000}"

dotnet swagger tofile \
  --output "$ROOT/openapi/transcendence.v1.json" \
  "$ROOT/Transcendence.WebAPI/bin/Release/net10.0/Transcendence.WebAPI.dll" \
  v1

