#!/usr/bin/env bash
#
# archive-old-patches.sh — archive (not delete) old-patch LoL match detail to the NAS, then prune it.
#
# Runs on the Docker host (192.168.0.221). For every patch OLDER than the newest KEEP_PATCHES,
# it streams each match table (Matches + all cascade children) for that patch out via Postgres
# COPY -> gzip -> ssh to the NAS, verifies the archive (gzip integrity + exact row count), writes
# a manifest, and only THEN deletes the patch from the DB (one cascading DELETE on Matches).
#
# Restore a table:  zcat <Table>.csv.gz | docker exec -i transcendence-postgres \
#                     psql -U postgres -d transcendence -c "COPY \"<Table>\" FROM STDIN WITH (FORMAT csv, HEADER true)"
#   (restore parents before children: Matches -> MatchParticipants -> Items/Runes, then the rest.)
#
# Safety: DRY-RUN by default (set APPLY=1 to archive+prune). Verify-before-delete is mandatory;
# any table that fails verification skips the DELETE for that patch. Idempotent via a _DONE marker.
#
# Env overrides: KEEP_PATCHES(3) NAS_HOST(192.168.0.199) NAS_DIR(/mnt/user/backup/transcendence-match-archive)
#                PG_CONTAINER(transcendence-postgres) PG_USER(postgres) PG_DB(transcendence) APPLY(0) ONLY_PATCH('')
set -uo pipefail

KEEP_PATCHES="${KEEP_PATCHES:-3}"
NAS_HOST="${NAS_HOST:-192.168.0.199}"
NAS_DIR="${NAS_DIR:-/mnt/user/backup/transcendence-match-archive}"
PG_CONTAINER="${PG_CONTAINER:-transcendence-postgres}"
PG_USER="${PG_USER:-postgres}"
PG_DB="${PG_DB:-transcendence}"
APPLY="${APPLY:-0}"
ONLY_PATCH="${ONLY_PATCH:-}"   # if set, restrict to this single patch (still must be eligible)

SSH_NAS=(ssh -o BatchMode=yes -o StrictHostKeyChecking=accept-new -o ConnectTimeout=15 "root@${NAS_HOST}")
pg()    { docker exec -i "$PG_CONTAINER" psql -U "$PG_USER" -d "$PG_DB" -v ON_ERROR_STOP=1 "$@"; }
pgval() { pg -tAc "$1"; }

TABLES=(Matches MatchParticipants MatchParticipantItems MatchParticipantRunes MatchBans MatchSummoner MatchParticipantTimelineSnapshots MatchTimelineFetchStates)
# Row source for table $1, patch $2 (already single-quoted-safe: version strings only).
sel_for() {
  local p="'$2'"
  case "$1" in
    Matches)                            echo "SELECT * FROM \"Matches\" m WHERE m.\"Patch\"=$p";;
    MatchParticipants)                  echo "SELECT mp.* FROM \"MatchParticipants\" mp JOIN \"Matches\" m ON mp.\"MatchId\"=m.\"Id\" WHERE m.\"Patch\"=$p";;
    MatchParticipantItems)              echo "SELECT i.* FROM \"MatchParticipantItems\" i JOIN \"MatchParticipants\" mp ON i.\"MatchParticipantId\"=mp.\"Id\" JOIN \"Matches\" m ON mp.\"MatchId\"=m.\"Id\" WHERE m.\"Patch\"=$p";;
    MatchParticipantRunes)              echo "SELECT r.* FROM \"MatchParticipantRunes\" r JOIN \"MatchParticipants\" mp ON r.\"MatchParticipantId\"=mp.\"Id\" JOIN \"Matches\" m ON mp.\"MatchId\"=m.\"Id\" WHERE m.\"Patch\"=$p";;
    MatchBans)                          echo "SELECT b.* FROM \"MatchBans\" b JOIN \"Matches\" m ON b.\"MatchId\"=m.\"Id\" WHERE m.\"Patch\"=$p";;
    MatchSummoner)                      echo "SELECT ms.* FROM \"MatchSummoner\" ms JOIN \"Matches\" m ON ms.\"MatchesId\"=m.\"Id\" WHERE m.\"Patch\"=$p";;
    MatchParticipantTimelineSnapshots)  echo "SELECT ts.* FROM \"MatchParticipantTimelineSnapshots\" ts JOIN \"Matches\" m ON ts.\"MatchId\"=m.\"Id\" WHERE m.\"Patch\"=$p";;
    MatchTimelineFetchStates)           echo "SELECT fs.* FROM \"MatchTimelineFetchStates\" fs JOIN \"Matches\" m ON fs.\"MatchId\"=m.\"Id\" WHERE m.\"Patch\"=$p";;
  esac
}

mapfile -t PATCHES < <(pgval "
  SELECT DISTINCT m.\"Patch\" FROM \"Matches\" m
  WHERE m.\"Patch\" IS NOT NULL AND m.\"Patch\" <> ''
    AND m.\"Patch\" NOT IN (SELECT \"Version\" FROM \"Patches\" ORDER BY \"ReleaseDate\" DESC NULLS LAST LIMIT ${KEEP_PATCHES})
    AND m.\"Patch\" <> COALESCE((SELECT \"Version\" FROM \"Patches\" WHERE \"IsActive\" LIMIT 1), '__none__')
  ORDER BY m.\"Patch\";") || { echo "FATAL: could not query eligible patches"; exit 1; }

echo "Mode: $([ "$APPLY" = 1 ] && echo APPLY || echo DRY-RUN) | keep newest ${KEEP_PATCHES} patches | NAS root@${NAS_HOST}:${NAS_DIR}"
echo "Eligible patches (${#PATCHES[@]}): ${PATCHES[*]:-<none>}"
[ "${#PATCHES[@]}" -eq 0 ] && { echo "Nothing to archive."; exit 0; }

reclaim_before=$(pgval "SELECT pg_database_size('${PG_DB}');")

for P in "${PATCHES[@]}"; do
  [ -n "$ONLY_PATCH" ] && [ "$P" != "$ONLY_PATCH" ] && continue
  echo "=== Patch ${P} ==="
  if "${SSH_NAS[@]}" "test -f '${NAS_DIR}/${P}/_DONE'" 2>/dev/null; then
    echo "  _DONE present on NAS — already archived; skipping."; continue
  fi

  unset CNT; declare -A CNT; total=0; cnt_ok=1
  for T in "${TABLES[@]}"; do
    c=$(pgval "SELECT count(*) FROM ($(sel_for "$T" "$P")) _s") || { echo "  count failed for ${T}"; cnt_ok=0; break; }
    CNT[$T]=$c; total=$((total + c))
  done
  [ "$cnt_ok" = 1 ] || { echo "  skipping ${P} (count error)"; continue; }
  printf '  rows:'; for T in "${TABLES[@]}"; do printf ' %s=%s' "$T" "${CNT[$T]}"; done; printf ' | total=%s\n' "$total"

  if [ "$APPLY" != "1" ]; then echo "  [dry-run] would archive then DELETE FROM Matches WHERE Patch=${P}"; continue; fi

  "${SSH_NAS[@]}" "mkdir -p '${NAS_DIR}/${P}'" || { echo "  NAS mkdir failed; skipping ${P}"; continue; }
  verified=1
  for T in "${TABLES[@]}"; do
    docker exec -i "$PG_CONTAINER" psql -U "$PG_USER" -d "$PG_DB" -v ON_ERROR_STOP=1 \
      -c "COPY ($(sel_for "$T" "$P")) TO STDOUT WITH (FORMAT csv, HEADER true)" 2>/tmp/trn_arc_err.$$ \
      | gzip | "${SSH_NAS[@]}" "cat > '${NAS_DIR}/${P}/${T}.csv.gz'"
    ps=("${PIPESTATUS[@]}")
    if [ "${ps[0]:-1}" -ne 0 ] || [ "${ps[1]:-1}" -ne 0 ] || [ "${ps[2]:-1}" -ne 0 ]; then
      echo "  EXPORT FAIL ${T} (pipestatus=${ps[*]}): $(head -c 300 /tmp/trn_arc_err.$$ 2>/dev/null)"; verified=0; break
    fi
    if ! "${SSH_NAS[@]}" "gzip -t '${NAS_DIR}/${P}/${T}.csv.gz'"; then echo "  GZIP CORRUPT ${T}"; verified=0; break; fi
    fl=$("${SSH_NAS[@]}" "zcat '${NAS_DIR}/${P}/${T}.csv.gz' | wc -l") || { echo "  count-back failed ${T}"; verified=0; break; }
    exp=$(( CNT[$T] + 1 ))   # +1 = CSV header line
    if [ "$fl" -ne "$exp" ]; then echo "  VERIFY FAIL ${T}: archived_data_lines=${fl} expected=${exp}"; verified=0; break; fi
    echo "    ok ${T}: ${CNT[$T]} rows archived + verified"
  done
  rm -f /tmp/trn_arc_err.$$

  if [ "$verified" != "1" ]; then echo "  !! verification failed for ${P} — DB NOT modified."; continue; fi

  { printf '{"patch":"%s","archivedAtUtc":"%s","keepPatches":%s,"rows":{' "$P" "$(date -u +%FT%TZ)" "$KEEP_PATCHES"
    first=1; for T in "${TABLES[@]}"; do [ "$first" = 1 ] || printf ','; printf '"%s":%s' "$T" "${CNT[$T]}"; first=0; done
    printf '}}\n'; } | "${SSH_NAS[@]}" "cat > '${NAS_DIR}/${P}/_manifest.json'"

  if pg -c "DELETE FROM \"Matches\" WHERE \"Patch\"='${P}';"; then
    "${SSH_NAS[@]}" "date -u +%FT%TZ > '${NAS_DIR}/${P}/_DONE'"
    echo "  ✓ archived + pruned ${P} (~${total} rows)"
  else
    echo "  DELETE failed for ${P} (archive is intact on NAS; left in DB)."
  fi
done

if [ "$APPLY" = 1 ]; then
  after=$(pgval "SELECT pg_database_size('${PG_DB}');")
  echo "DB logical size: $(numfmt --to=iec "$reclaim_before" 2>/dev/null || echo "$reclaim_before") -> $(numfmt --to=iec "$after" 2>/dev/null || echo "$after") (rows deleted; run VACUUM (FULL not required) to release pages to OS)."
fi
