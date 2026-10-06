#!/usr/bin/env bash
# Bounded archive-then-prune for an HDD: frozen chunks, verified exports, resumable paced deletes.
# A verified chunk is never exported again after any of its rows have been pruned.
set -Eeuo pipefail
KEEP_PATCHES="${KEEP_PATCHES:-3}"
APPLY="${APPLY:-0}"
ONLY_PATCH="${ONLY_PATCH:-}"
NAS_HOST="${NAS_HOST:-192.168.0.199}"
NAS_DIR="${NAS_DIR:-/mnt/user/backup/transcendence-match-archive}"
PG_CONTAINER="${PG_CONTAINER:-transcendence-postgres}"
PG_USER="${PG_USER:-postgres}"
PG_DB="${PG_DB:-transcendence}"
DELETE_BATCH="${DELETE_BATCH:-25}"
FREEZE_MATCHES="${FREEZE_MATCHES:-500}"
MAX_RUN_SECONDS="${MAX_RUN_SECONDS:-1200}"
MAX_WAL_MB="${MAX_WAL_MB:-256}"
DELETE_TIMEOUT_MS="${DELETE_TIMEOUT_MS:-15000}"
EXPORT_TIMEOUT_MS="${EXPORT_TIMEOUT_MS:-120000}"
BATCH_SLEEP_SECONDS="${BATCH_SLEEP_SECONDS:-2}"
MAX_IO_PRESSURE="${MAX_IO_PRESSURE:-35}"
IO_PRESSURE_FILE="${IO_PRESSURE_FILE:-/proc/pressure/io}"
STATE_DIR="${ARCHIVE_STATE_DIR:-/var/lib/transcendence-archive}"
WORK="_patch_archive_pending"
TABLES=(Matches MatchParticipants MatchParticipantItems MatchParticipantRunes MatchBans
        MatchTeamObjectives MatchParticipantTimelineSnapshots MatchTimelineFetchStates
        MatchParticipantItemPurchases MatchParticipantSkillOrders
        MatchParticipantItemEvents MatchParticipantRankContexts MatchTimelineEventPayloads)
SSH_NAS=(ssh -o BatchMode=yes -o StrictHostKeyChecking=yes -o ConnectTimeout=15 "root@${NAS_HOST}")

for option in KEEP_PATCHES DELETE_BATCH FREEZE_MATCHES MAX_RUN_SECONDS MAX_WAL_MB DELETE_TIMEOUT_MS EXPORT_TIMEOUT_MS; do
  [[ "${!option}" =~ ^[1-9][0-9]*$ ]] || { echo "Invalid ${option}" >&2; exit 2; }
done
[[ "$APPLY" =~ ^[01]$ && "$BATCH_SLEEP_SECONDS" =~ ^[0-9]+$ && "$MAX_IO_PRESSURE" =~ ^[0-9]+$ ]] || exit 2
[[ "$STATE_DIR" == /* && "$STATE_DIR" != / && "$NAS_DIR" == /* && "$NAS_DIR" != / ]] || exit 2
[[ "$NAS_DIR" != *"'"* && "$NAS_DIR" != *$'\n'* ]] || exit 2
[[ -z "$ONLY_PATCH" || "$ONLY_PATCH" =~ ^[0-9]+\.[0-9]+$ ]] || exit 2
started=$SECONDS
log() { printf '%s archive: %s\n' "$(date -u +%FT%TZ)" "$*"; }
pgq() {
  local readonly=""
  [[ "$APPLY" == 0 ]] && readonly=" -c default_transaction_read_only=on"
  docker exec -e "PGOPTIONS=-c application_name=transcendence-archive -c statement_timeout=${DELETE_TIMEOUT_MS} -c lock_timeout=2000${readonly}" \
    "$PG_CONTAINER" psql -X -U "$PG_USER" -d "$PG_DB" -v ON_ERROR_STOP=1 "$@"
}
pgval() { pgq -tAc "$1"; }
eligible_sql="SELECT DISTINCT m.\"Patch\" FROM \"Matches\" m
  WHERE m.\"Patch\" IS NOT NULL AND m.\"Patch\" <> ''
    AND m.\"Patch\" NOT IN (SELECT \"Version\" FROM \"Patches\" ORDER BY \"ReleaseDate\" DESC NULLS LAST LIMIT ${KEEP_PATCHES})
    AND m.\"Patch\" <> COALESCE((SELECT \"Version\" FROM \"Patches\" WHERE \"IsActive\" LIMIT 1), '__none__')
  ORDER BY m.\"Patch\""

# Dry-run is actually read-only: no frozen work table, NAS directories, or state files are created.
if [[ "$APPLY" == 0 ]]; then
  pgval "$eligible_sql"
  log "Dry-run: eligible patches above; frozen chunks <=${FREEZE_MATCHES}, deletes <=${DELETE_BATCH}, run <=${MAX_RUN_SECONDS}s."
  exit 0
fi
install -d -m 0750 "$STATE_DIR"
exec 9>"${STATE_DIR}/run.lock"
flock -n 9 || { log "Another archive run is active."; exit 0; }
state="${STATE_DIR}/current"
budget_left() { (( SECONDS - started < MAX_RUN_SECONDS )); }
pressure_ok() {
  [[ ! -r "$IO_PRESSURE_FILE" ]] && return 0
  awk -v cap="$MAX_IO_PRESSURE" '$1=="full" {for(i=2;i<=NF;i++) if($i ~ /^avg60=/) {split($i,a,"="); exit(a[2]>=cap)}}' "$IO_PRESSURE_FILE"
}
wal_position() { pgval "SELECT pg_current_wal_insert_lsn()::text;"; }
wal_start=$(wal_position)
wal_ok() {
  local delta
  delta=$(pgval "SELECT pg_wal_lsn_diff(pg_current_wal_insert_lsn(), '${wal_start}'::pg_lsn)::bigint;") || return 1
  (( delta < MAX_WAL_MB * 1024 * 1024 ))
}
save_state() {
  printf '%s\n' "$patch" "$destination" "$frozen" "$verified" >"${state}.tmp"
  mv -f "${state}.tmp" "$state"
}
select_for() {
  case "$1" in
    Matches) echo "SELECT m.* FROM \"Matches\" m JOIN ${WORK} a ON m.\"Id\"=a.\"Id\"" ;;
    MatchParticipants) echo "SELECT p.* FROM \"MatchParticipants\" p JOIN ${WORK} a ON p.\"MatchId\"=a.\"Id\"" ;;
    MatchParticipantItems|MatchParticipantRunes)
      echo "SELECT c.* FROM \"$1\" c JOIN \"MatchParticipants\" p ON c.\"MatchParticipantId\"=p.\"Id\" JOIN ${WORK} a ON p.\"MatchId\"=a.\"Id\"" ;;
    *) echo "SELECT c.* FROM \"$1\" c JOIN ${WORK} a ON c.\"MatchId\"=a.\"Id\"" ;;
  esac
}
while budget_left; do
  if ! pressure_ok || ! wal_ok; then
    log "Yielding to database I/O pressure/WAL budget; frozen progress retained for the next run."
    exit 0
  fi
  if [[ -f "$state" ]]; then
    mapfile -t saved <"$state"
    [[ "${#saved[@]}" == 4 && "${saved[0]}" =~ ^[0-9]+\.[0-9]+$ && "${saved[2]}" =~ ^[0-9]+$ && "${saved[3]}" =~ ^[01]$ ]] || exit 1
    patch="${saved[0]}"; destination="${saved[1]}"; frozen="${saved[2]}"; verified="${saved[3]}"
    [[ "$destination" == "$NAS_DIR/"* && "$destination" != *"'"* && "$destination" != *$'\n'* ]] || exit 1
    if [[ "$verified" == 1 ]] && "${SSH_NAS[@]}" "test -f '${destination}/_DONE'" </dev/null; then
      if [[ $(pgval "SELECT to_regclass('public.${WORK}') IS NOT NULL;") == f ]]; then
        rm -f "$state" "${STATE_DIR}/manifest.json"
        log "Recovered completed chunk after progress cleanup was interrupted."
        continue
      fi
    fi
    # A patch can be promoted between runs: never prune the active or newly retained patch.
    still_eligible=$(pgval "SELECT count(*) FROM ($eligible_sql) e WHERE e.\"Patch\"='${patch}';")
    [[ "$still_eligible" != 0 || $(pgval "SELECT count(*) FROM ${WORK};") == 0 ]] || { log "${patch} is now protected; retaining frozen state without deletes."; exit 0; }
    log "Resuming ${patch}, originally ${frozen} frozen matches, verified=${verified}."
  else
    # An old-script or interrupted unowned work table is never silently destroyed.
    existing=$(pgval "SELECT to_regclass('public.${WORK}') IS NOT NULL;")
    if [[ "$existing" == t ]]; then
      [[ $(pgval "SELECT count(*) FROM ${WORK};") == 0 ]] || { log "Unowned ${WORK} exists; refusing to replace its frozen set."; exit 1; }
      pgq -c "DROP TABLE ${WORK};" >/dev/null
    fi
    patches=$(pgval "$eligible_sql")
    patch=""
    while IFS= read -r candidate; do
      [[ "$candidate" =~ ^[0-9]+\.[0-9]+$ ]] || continue
      [[ -z "$ONLY_PATCH" || "$candidate" == "$ONLY_PATCH" ]] || continue
      patch="$candidate"; break
    done <<<"$patches"
    [[ -n "$patch" ]] || { log "No eligible patches remain."; exit 0; }
    stamp="$(date -u +%Y%m%dT%H%M%S)-$$"
    destination="${NAS_DIR}/${patch}/chunk-${stamp}"
    frozen=0; verified=0
    # Save ownership first. A crash before CREATE can be resumed safely.
    save_state
    pgq -c "CREATE TABLE ${WORK} AS SELECT \"Id\" FROM \"Matches\" WHERE \"Patch\"='${patch}' ORDER BY \"Id\" LIMIT ${FREEZE_MATCHES}; ALTER TABLE ${WORK} ADD PRIMARY KEY (\"Id\");" >/dev/null
    frozen=$(pgval "SELECT count(*) FROM ${WORK};")
    save_state
    log "Frozen ${frozen} matches for ${patch} -> ${destination}."
  fi

  if [[ "$verified" == 0 ]]; then
    exists=$(pgval "SELECT to_regclass('public.${WORK}') IS NOT NULL;")
    if [[ "$exists" == f && "$frozen" == 0 ]]; then
      pgq -c "CREATE TABLE ${WORK} AS SELECT \"Id\" FROM \"Matches\" WHERE \"Patch\"='${patch}' ORDER BY \"Id\" LIMIT ${FREEZE_MATCHES}; ALTER TABLE ${WORK} ADD PRIMARY KEY (\"Id\");" >/dev/null
    fi
    frozen=$(pgval "SELECT count(*) FROM ${WORK};")
    save_state
    "${SSH_NAS[@]}" "mkdir -p '${destination}'" </dev/null
    manifest="${STATE_DIR}/manifest.json"
    printf '{"patch":"%s","frozenMatches":%s,"rows":{' "$patch" "$frozen" >"$manifest"
    first=1
    for table in "${TABLES[@]}"; do
      budget_left && pressure_ok && wal_ok || { log "Export paused; no deletes permitted."; exit 0; }
      source_sql=$(select_for "$table")
      rows=$(pgval "SET enable_seqscan=off; SELECT count(*) FROM (${source_sql}) src;" | tail -1)
      [[ "$rows" =~ ^[0-9]+$ ]] || exit 1
      log "Exporting ${table}: ${rows} rows."
      docker exec -e "PGOPTIONS=-c application_name=transcendence-archive -c enable_seqscan=off -c statement_timeout=${EXPORT_TIMEOUT_MS} -c lock_timeout=2000" \
        "$PG_CONTAINER" psql -X -U "$PG_USER" -d "$PG_DB" -v ON_ERROR_STOP=1 \
        -c "COPY (${source_sql}) TO STDOUT WITH (FORMAT csv, HEADER true)" \
        | gzip | "${SSH_NAS[@]}" "cat > '${destination}/${table}.csv.gz'"
      "${SSH_NAS[@]}" "gzip -t '${destination}/${table}.csv.gz'" </dev/null
      lines=$("${SSH_NAS[@]}" "zcat '${destination}/${table}.csv.gz' | wc -l" </dev/null)
      [[ "$lines" == "$((rows + 1))" ]] || { log "Verification failed for ${table}; no deletes."; exit 1; }
      checksum=$("${SSH_NAS[@]}" "sha256sum '${destination}/${table}.csv.gz'" </dev/null | awk '{print $1}')
      [[ "$checksum" =~ ^[a-f0-9]{64}$ ]] || exit 1
      [[ "$first" == 1 ]] || printf ',' >>"$manifest"
      first=0
      printf '"%s":{"count":%s,"sha256":"%s"}' "$table" "$rows" "$checksum" >>"$manifest"
    done
    printf '}}\n' >>"$manifest"
    "${SSH_NAS[@]}" "cat > '${destination}/_manifest.json'" <"$manifest"
    verified=1
    save_state
    log "Every child export verified; pruning is now permitted."
  fi

  batch=$DELETE_BATCH
  while budget_left && pressure_ok && wal_ok; do
    remaining=$(pgval "SELECT count(*) FROM ${WORK};")
    [[ "$remaining" != 0 ]] || break
    # Preserve compact synergy facts before removing their raw source. The bounded worker prioritizes
    # this frozen table; old deployments without the new tables retain their existing archive behavior.
    facts_exist=$(pgval "SELECT to_regclass('public.\"ChampionSynergySourceMatches\"') IS NOT NULL;")
    if [[ "$facts_exist" == t ]]; then
      missing=$(pgval "SELECT EXISTS(SELECT 1 FROM \"Matches\" m JOIN ${WORK} a ON a.\"Id\"=m.\"Id\" WHERE m.\"Status\"=1 AND (m.\"QueueFamily\"='RANKED_FLEX' OR m.\"QueueId\" IN (420,440) OR (m.\"QueueId\"=0 AND m.\"QueueType\" IN ('420','440'))) AND NOT EXISTS(SELECT 1 FROM \"ChampionSynergySourceMatches\" s WHERE s.\"MatchId\"=m.\"Id\"));")
      [[ "$missing" == f ]] || { log "Waiting for compact synergy facts for the frozen chunk; verified exports retained."; exit 0; }
    fi
    batch_started=$SECONDS
    if ! pgq -c "WITH batch AS (SELECT \"Id\" FROM ${WORK} ORDER BY \"Id\" LIMIT ${batch}), del_m AS (DELETE FROM \"Matches\" WHERE \"Id\" IN (SELECT \"Id\" FROM batch)) DELETE FROM ${WORK} WHERE \"Id\" IN (SELECT \"Id\" FROM batch);" >/dev/null; then
      if (( batch <= 1 )); then log "Single-match prune failed; frozen verified state retained."; exit 1; fi
      batch=$(( (batch + 1) / 2 ))
      log "Prune rolled back; reducing batch to ${batch}."
    else
      elapsed=$((SECONDS - batch_started))
      log "Pruned <=${batch} matches in ${elapsed}s; previous remaining=${remaining}."
      if (( elapsed > 5 && batch > 1 )); then batch=$(( (batch + 1) / 2 )); fi
    fi
    sleep "$BATCH_SLEEP_SECONDS"
  done
  remaining=$(pgval "SELECT count(*) FROM ${WORK};")
  if [[ "$remaining" != 0 ]]; then log "Run budget reached; ${remaining} frozen matches remain."; exit 0; fi
  # The chunk marker is written before clearing progress; failed marker writes are resumable too.
  "${SSH_NAS[@]}" "date -u +%FT%TZ > '${destination}/_DONE'" </dev/null
  # A completed NAS marker makes an interruption between DROP and local cleanup recoverable.
  pgq -c "DROP TABLE ${WORK};" >/dev/null
  rm -f "$state" "${STATE_DIR}/manifest.json"
  log "Verified chunk archived and pruned for ${patch}."
done
log "Run time budget reached; continuing on the next invocation."
