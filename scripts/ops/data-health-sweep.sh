#!/usr/bin/env bash
# Modest production reads: full streamed response, explicit fallback detection, snapshot freshness.
set -Eeuo pipefail
STATE_DIR="${DATA_HEALTH_STATE_DIR:-/var/lib/transcendence-perf}"
TEXTFILE_DIR="${PERF_TEXTFILE_DIR:-${STATE_DIR}/textfile}"
BASE_URL="${PERF_BASE_URL:-https://transcend.kronic.one}"
[[ "$STATE_DIR" == /* && "$STATE_DIR" != / && "$TEXTFILE_DIR" == /* && "$TEXTFILE_DIR" != / ]] || exit 2
install -d -m 0755 "$STATE_DIR" "$TEXTFILE_DIR"
exec 9>"${STATE_DIR}/data-health.lock"
flock -n 9 || exit 0
scratch=$(mktemp -d "${STATE_DIR}/data-health.XXXXXX")
trap 'rm -rf "$scratch"' EXIT
metrics="${scratch}/data_health.prom"
for route in /lol/leaderboards /lol/champions/103 /lol/summoners/na/Kronic-NA1; do
  failed=0
  timing=$(curl --silent --show-error --fail --max-time 12 -o "${scratch}/body" \
    -w '%{time_starttransfer} %{time_total}' "${BASE_URL}${route}") || failed=1
  # Next can send HTTP 200 and a fast loading shell before the backend fails. Do not count it healthy.
  if [[ ! -s "${scratch}/body" ]] || grep -Eq 'data-backend-error|data-trn-backend-error|Win-rate data is unavailable right now|Build data is unavailable right now' "${scratch}/body"; then failed=1; fi
  read -r first total <<<"$timing"
  printf 'transcendence_data_probe_failure{route="%s"} %s\n' "$route" "$failed" >>"$metrics"
  printf 'transcendence_data_probe_first_byte_seconds{route="%s"} %s\n' "$route" "${first:-0}" >>"$metrics"
  printf 'transcendence_data_probe_complete_seconds{route="%s"} %s\n' "$route" "${total:-12}" >>"$metrics"
done
# Only the small snapshot table is scanned. Enforce read-only sessions and a five-second DB budget.
docker exec -e 'PGOPTIONS=-c default_transaction_read_only=on -c statement_timeout=5000 -c lock_timeout=1000 -c application_name=transcendence-data-health' \
  "${PG_CONTAINER:-transcendence-postgres}" psql -X -U "${PG_USER:-postgres}" -d "${PG_DB:-transcendence}" -At -v ON_ERROR_STOP=1 -c '
  SELECT '\''transcendence_snapshot_oldest_age_seconds{feature="'\'' || f.feature || '\''"} '\'' ||
      COALESCE(EXTRACT(epoch FROM now()-min(s."ComputedAtUtc"))::bigint,-1)
  FROM (VALUES ('\''leaderboard-regional'\''),('\''synergies'\''),('\''profile-warm'\'')) f(feature)
  LEFT JOIN "AnalyticsResponseSnapshots" s ON s."Feature"=f.feature
    AND (s."Patch"='\''*'\'' OR s."Patch"=(SELECT "Version" FROM "Patches" WHERE "IsActive" LIMIT 1))
  GROUP BY f.feature;' >>"$metrics"
if [[ -r /proc/pressure/io ]]; then
  awk '$1=="full" {for(i=2;i<=NF;i++) if($i ~ /^avg60=/) {split($i,a,"=");print "transcendence_io_full_pressure_percent " a[2]}}' /proc/pressure/io >>"$metrics"
fi
printf 'transcendence_data_health_last_success_unixtime_seconds %s\n' "$(date +%s)" >>"$metrics"
install -m 0644 "$metrics" "${TEXTFILE_DIR}/data_health.prom.tmp"
mv -f "${TEXTFILE_DIR}/data_health.prom.tmp" "${TEXTFILE_DIR}/data_health.prom"
