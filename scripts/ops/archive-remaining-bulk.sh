#!/usr/bin/env bash
# One-time bulk archive of ALL old-patch match detail (everything except the newest KEEP patches)
# to the NAS, then prune. Consistency under a LIVE writer (the ingestion worker keeps inserting
# matches for players returning after a break, i.e. older patches) is guaranteed by freezing the
# exact set of old-patch match IDs into a work table (T0 snapshot); every export/verify/delete is
# strictly bounded to that frozen set. Anything inserted after T0 is excluded — no verify race, no
# data loss — and gets caught by the next (recurring) archive run. DRY-RUN unless APPLY=1.
set -uo pipefail
KEEP="${KEEP_PATCHES:-3}"; APPLY="${APPLY:-0}"; BATCH="${DELETE_BATCH:-20000}"
NAS_HOST="${NAS_HOST:-192.168.0.199}"; NAS_DIR="${NAS_DIR:-/mnt/user/backup/transcendence-match-archive/_bulk}"
PG="${PG_CONTAINER:-transcendence-postgres}"; PGU="${PG_USER:-postgres}"; PGD="${PG_DB:-transcendence}"
WORK="_match_archive_pending"
SSH_NAS=(ssh -o BatchMode=yes -o StrictHostKeyChecking=accept-new -o ConnectTimeout=15 "root@${NAS_HOST}")
pg(){ docker exec -i "$PG" psql -U "$PGU" -d "$PGD" -v ON_ERROR_STOP=1 "$@"; }   # default planner = seq scan (HDD-friendly for bulk)

TABLES=(Matches MatchParticipants MatchParticipantItems MatchParticipantRunes MatchBans MatchSummoner MatchParticipantTimelineSnapshots MatchTimelineFetchStates)
# Row source for table $1 — every table joined to the frozen work table of match IDs.
where_for(){ case "$1" in
  Matches)                           echo "SELECT m.* FROM \"Matches\" m JOIN ${WORK} a ON m.\"Id\"=a.\"Id\"";;
  MatchParticipants)                 echo "SELECT mp.* FROM \"MatchParticipants\" mp JOIN ${WORK} a ON mp.\"MatchId\"=a.\"Id\"";;
  MatchParticipantItems)             echo "SELECT i.* FROM \"MatchParticipantItems\" i JOIN \"MatchParticipants\" mp ON i.\"MatchParticipantId\"=mp.\"Id\" JOIN ${WORK} a ON mp.\"MatchId\"=a.\"Id\"";;
  MatchParticipantRunes)             echo "SELECT r.* FROM \"MatchParticipantRunes\" r JOIN \"MatchParticipants\" mp ON r.\"MatchParticipantId\"=mp.\"Id\" JOIN ${WORK} a ON mp.\"MatchId\"=a.\"Id\"";;
  MatchBans)                         echo "SELECT b.* FROM \"MatchBans\" b JOIN ${WORK} a ON b.\"MatchId\"=a.\"Id\"";;
  MatchSummoner)                     echo "SELECT ms.* FROM \"MatchSummoner\" ms JOIN ${WORK} a ON ms.\"MatchesId\"=a.\"Id\"";;
  MatchParticipantTimelineSnapshots) echo "SELECT ts.* FROM \"MatchParticipantTimelineSnapshots\" ts JOIN ${WORK} a ON ts.\"MatchId\"=a.\"Id\"";;
  MatchTimelineFetchStates)          echo "SELECT fs.* FROM \"MatchTimelineFetchStates\" fs JOIN ${WORK} a ON fs.\"MatchId\"=a.\"Id\"";;
esac; }

echo "Mode: $([ "$APPLY" = 1 ] && echo APPLY || echo DRY-RUN) | keep newest ${KEEP} | NAS root@${NAS_HOST}:${NAS_DIR}"
echo "Kept patches: $(pg -tAc "SELECT string_agg(\"Version\",',') FROM (SELECT \"Version\" FROM \"Patches\" ORDER BY \"ReleaseDate\" DESC NULLS LAST LIMIT ${KEEP}) _k")"

# --- T0 snapshot: freeze the set of old-patch match IDs ---
echo "Freezing old-patch match-ID set into ${WORK} ..."
pg -c "DROP TABLE IF EXISTS ${WORK};
       CREATE TABLE ${WORK} AS
         SELECT \"Id\" FROM \"Matches\"
         WHERE \"Patch\" NOT IN (SELECT \"Version\" FROM \"Patches\" ORDER BY \"ReleaseDate\" DESC NULLS LAST LIMIT ${KEEP});
       ALTER TABLE ${WORK} ADD PRIMARY KEY (\"Id\");" || { echo "FATAL: could not build ${WORK}"; exit 1; }
FROZEN=$(pg -tAc "SELECT count(*) FROM ${WORK}")
echo "Frozen matches to archive+prune: ${FROZEN}"
"${SSH_NAS[@]}" "mkdir -p '${NAS_DIR}'"

declare -A CNT; ok=1
for T in "${TABLES[@]}"; do
  c=$(pg -tAc "SELECT count(*) FROM ($(where_for "$T")) _s") || { echo "count fail $T"; ok=0; break; }
  CNT[$T]=$c
  if [ "$APPLY" != 1 ]; then echo "  [dry-run] $T: $c rows would archive"; continue; fi
  echo "  exporting $T ($c rows)..."
  docker exec -i "$PG" psql -U "$PGU" -d "$PGD" -v ON_ERROR_STOP=1 \
    -c "COPY ($(where_for "$T")) TO STDOUT WITH (FORMAT csv, HEADER true)" 2>/tmp/trn_bulk_err.$$ \
    | gzip | "${SSH_NAS[@]}" "cat > '${NAS_DIR}/${T}.csv.gz'"
  ps=("${PIPESTATUS[@]}")
  { [ "${ps[0]:-1}" = 0 ] && [ "${ps[1]:-1}" = 0 ] && [ "${ps[2]:-1}" = 0 ]; } || { echo "  EXPORT FAIL $T (${ps[*]}): $(head -c200 /tmp/trn_bulk_err.$$)"; ok=0; break; }
  "${SSH_NAS[@]}" "gzip -t '${NAS_DIR}/${T}.csv.gz'" || { echo "  GZIP FAIL $T"; ok=0; break; }
  fl=$("${SSH_NAS[@]}" "zcat '${NAS_DIR}/${T}.csv.gz' | wc -l")
  [ "$fl" = "$((c + 1))" ] || { echo "  VERIFY FAIL $T: archived=$fl expected=$((c+1))"; ok=0; break; }
  echo "    ok $T: $c rows archived + verified"
done
rm -f /tmp/trn_bulk_err.$$
[ "$ok" = 1 ] || { echo "!! archive/verify failed — NO deletes performed. ${WORK} left in place for inspection."; exit 1; }
[ "$APPLY" != 1 ] && { echo "[dry-run] would now batch-DELETE ${FROZEN} frozen matches (cascade)."; pg -c "DROP TABLE IF EXISTS ${WORK};"; exit 0; }

{ printf '{"archivedAtUtc":"%s","keepPatches":%s,"frozenMatches":%s,"rows":{' "$(date -u +%FT%TZ)" "$KEEP" "$FROZEN"
  f=1; for T in "${TABLES[@]}"; do [ "$f" = 1 ]||printf ','; printf '"%s":%s' "$T" "${CNT[$T]}"; f=0; done; printf '}}\n'; } \
  | "${SSH_NAS[@]}" "cat > '${NAS_DIR}/_manifest.json'"

echo "Archive verified. Pruning ${FROZEN} matches in batches of ${BATCH} (cascade)..."
while :; do
  rem=$(pg -tAc "SELECT count(*) FROM ${WORK}") || { echo "  remaining-count failed"; break; }
  [ "${rem:-0}" -eq 0 ] && { echo "  all frozen matches pruned."; break; }
  pg -c "WITH batch AS (SELECT \"Id\" FROM ${WORK} LIMIT ${BATCH}),
              del_m AS (DELETE FROM \"Matches\" WHERE \"Id\" IN (SELECT \"Id\" FROM batch))
         DELETE FROM ${WORK} WHERE \"Id\" IN (SELECT \"Id\" FROM batch);" \
    || { echo "  batch delete failed (remaining=${rem}); ${WORK} retained"; ok=0; break; }
  echo "  pruned batch — remaining matches: $(pg -tAc "SELECT count(*) FROM ${WORK}")"
done
[ "$ok" = 1 ] || { echo "!! prune incomplete — archive intact on NAS; ${WORK} holds the un-pruned remainder."; exit 1; }

pg -c "DROP TABLE IF EXISTS ${WORK};"
"${SSH_NAS[@]}" "date -u +%FT%TZ > '${NAS_DIR}/_DONE'"
echo "BULK DONE. DB logical size: $(pg -tAc "SELECT pg_size_pretty(pg_database_size('${PGD}'))")  (run VACUUM to release pages for reuse)."
