#!/usr/bin/env bash
#
# archive-old-patches.sh — recurring retention: archive (not delete) old-patch LoL match detail to
# the NAS, then prune it. Runs on the Docker host (192.168.0.221), weekly via /etc/cron.d/trn-archive.
#
# For every patch OLDER than the newest KEEP_PATCHES it streams each match table (Matches + all
# cascade children) out via Postgres COPY -> gzip -> ssh to the NAS, verifies (gzip integrity +
# exact row count), writes a manifest, and only THEN prunes (one cascading DELETE on Matches, in
# bounded batches). Correctness guarantees:
#
#  * Consistency under the LIVE ingestion worker — the worker keeps inserting old-patch matches as
#    lapsed players return / failed matches are retried. So per patch we first FREEZE that patch's
#    match-ID set into a work table (T0 snapshot); every count / export / verify / delete is bounded
#    to that frozen set. Rows inserted after T0 are excluded (no verify race, no data loss) and are
#    caught on the next run.
#  * Adaptive plan for HDD — a big patch (> SEQSCAN_THRESHOLD matches) exports a large slice, so the
#    default sequential scan wins; a small patch forces index nested-loops (enable_seqscan=off) to
#    avoid full scans of the ~100M-row child tables. (Forcing index on a big patch = tens of millions
#    of random HDD fetches = catastrophic; forcing seq-scan on a tiny patch = a needless full scan.)
#  * Residual-safe + idempotent — a patch's first archive lands in NAS/<patch>/ and writes _DONE. If a
#    _DONE patch later accrues a residual (post-T0 inserts), that residual is archived to
#    NAS/<patch>/residual-<epoch>/ before pruning — never clobbering the primary archive.
#
# Restore a table:  zcat <Table>.csv.gz | docker exec -i transcendence-postgres \
#                     psql -U postgres -d transcendence -c "COPY \"<Table>\" FROM STDIN WITH (FORMAT csv, HEADER true)"
#   (restore parents before children: Matches -> MatchParticipants -> Items/Runes, then the rest.)
#
# Safety: DRY-RUN by default (APPLY=1 to archive+prune). Verify-before-delete is mandatory; any table
# that fails verification skips the prune for that patch (archive on the NAS is left intact).
#
# Env: KEEP_PATCHES(3) NAS_HOST(192.168.0.199) NAS_DIR(/mnt/user/backup/transcendence-match-archive)
#      PG_CONTAINER(transcendence-postgres) PG_USER(postgres) PG_DB(transcendence) APPLY(0)
#      ONLY_PATCH('') DELETE_BATCH(20000) SEQSCAN_THRESHOLD(50000)
set -uo pipefail

KEEP_PATCHES="${KEEP_PATCHES:-3}"
NAS_HOST="${NAS_HOST:-192.168.0.199}"
NAS_DIR="${NAS_DIR:-/mnt/user/backup/transcendence-match-archive}"
PG_CONTAINER="${PG_CONTAINER:-transcendence-postgres}"
PG_USER="${PG_USER:-postgres}"
PG_DB="${PG_DB:-transcendence}"
APPLY="${APPLY:-0}"
ONLY_PATCH="${ONLY_PATCH:-}"
BATCH="${DELETE_BATCH:-20000}"
SEQSCAN_THRESHOLD="${SEQSCAN_THRESHOLD:-50000}"
WORK="_patch_archive_pending"

SSH_NAS=(ssh -o BatchMode=yes -o StrictHostKeyChecking=accept-new -o ConnectTimeout=15 "root@${NAS_HOST}")
PGENV=()   # set per-patch for the export (empty = default seq-scan; index-forcing for small patches)
pgq()   { docker exec -i "$PG_CONTAINER" psql -U "$PG_USER" -d "$PG_DB" -v ON_ERROR_STOP=1 "$@"; }                  # default planner
pgx()   { docker exec -i "${PGENV[@]}" "$PG_CONTAINER" psql -U "$PG_USER" -d "$PG_DB" -v ON_ERROR_STOP=1 "$@"; }    # planner per $PGENV
pgval() { pgq -tAc "$1"; }            # planner-neutral scalar reads

TABLES=(Matches MatchParticipants MatchParticipantItems MatchParticipantRunes MatchBans MatchParticipantTimelineSnapshots MatchTimelineFetchStates)
# Row source for table $1 — every table joined to the frozen per-patch work table of match IDs.
sel_for() { case "$1" in
  Matches)                           echo "SELECT m.* FROM \"Matches\" m JOIN ${WORK} a ON m.\"Id\"=a.\"Id\"";;
  MatchParticipants)                 echo "SELECT mp.* FROM \"MatchParticipants\" mp JOIN ${WORK} a ON mp.\"MatchId\"=a.\"Id\"";;
  MatchParticipantItems)             echo "SELECT i.* FROM \"MatchParticipantItems\" i JOIN \"MatchParticipants\" mp ON i.\"MatchParticipantId\"=mp.\"Id\" JOIN ${WORK} a ON mp.\"MatchId\"=a.\"Id\"";;
  MatchParticipantRunes)             echo "SELECT r.* FROM \"MatchParticipantRunes\" r JOIN \"MatchParticipants\" mp ON r.\"MatchParticipantId\"=mp.\"Id\" JOIN ${WORK} a ON mp.\"MatchId\"=a.\"Id\"";;
  MatchBans)                         echo "SELECT b.* FROM \"MatchBans\" b JOIN ${WORK} a ON b.\"MatchId\"=a.\"Id\"";;
  MatchParticipantTimelineSnapshots) echo "SELECT ts.* FROM \"MatchParticipantTimelineSnapshots\" ts JOIN ${WORK} a ON ts.\"MatchId\"=a.\"Id\"";;
  MatchTimelineFetchStates)          echo "SELECT fs.* FROM \"MatchTimelineFetchStates\" fs JOIN ${WORK} a ON fs.\"MatchId\"=a.\"Id\"";;
esac; }

mapfile -t PATCHES < <(pgval "
  SELECT DISTINCT m.\"Patch\" FROM \"Matches\" m
  WHERE m.\"Patch\" IS NOT NULL AND m.\"Patch\" <> ''
    AND m.\"Patch\" NOT IN (SELECT \"Version\" FROM \"Patches\" ORDER BY \"ReleaseDate\" DESC NULLS LAST LIMIT ${KEEP_PATCHES})
    AND m.\"Patch\" <> COALESCE((SELECT \"Version\" FROM \"Patches\" WHERE \"IsActive\" LIMIT 1), '__none__')
  ORDER BY m.\"Patch\";") || { echo "FATAL: could not query eligible patches"; exit 1; }

echo "Mode: $([ "$APPLY" = 1 ] && echo APPLY || echo DRY-RUN) | keep newest ${KEEP_PATCHES} | NAS root@${NAS_HOST}:${NAS_DIR}"
echo "Eligible patches (${#PATCHES[@]}): ${PATCHES[*]:-<none>}"
[ "${#PATCHES[@]}" -eq 0 ] && { echo "Nothing to archive."; exit 0; }

for P in "${PATCHES[@]}"; do
  [ -n "$ONLY_PATCH" ] && [ "$P" != "$ONLY_PATCH" ] && continue
  echo "=== Patch ${P} ==="

  # T0 snapshot: freeze this patch's match-ID set.
  pgq -c "DROP TABLE IF EXISTS ${WORK};
          CREATE TABLE ${WORK} AS SELECT \"Id\" FROM \"Matches\" WHERE \"Patch\"='${P}';
          ALTER TABLE ${WORK} ADD PRIMARY KEY (\"Id\");" \
    || { echo "  could not freeze ${P}; skipping"; continue; }
  frozen=$(pgval "SELECT count(*) FROM ${WORK}")
  if [ "${frozen:-0}" -eq 0 ]; then echo "  0 matches; skipping"; pgq -c "DROP TABLE IF EXISTS ${WORK};" >/dev/null 2>&1; continue; fi

  # Primary archive -> NAS/<P>/ ; residual (patch already _DONE) -> NAS/<P>/residual-<epoch>/
  if "${SSH_NAS[@]}" "test -f '${NAS_DIR}/${P}/_DONE'" 2>/dev/null; then
    is_residual=1; dest="${NAS_DIR}/${P}/residual-$(date +%s)"
    echo "  patch already archived (_DONE) — ${frozen} residual matches -> ${dest}"
  else
    is_residual=0; dest="${NAS_DIR}/${P}"
    echo "  ${frozen} matches -> ${dest}"
  fi

  # Adaptive planner: big slice -> default seq scan; small slice -> force index nested-loops.
  if [ "$frozen" -gt "$SEQSCAN_THRESHOLD" ]; then PGENV=(); else PGENV=(-e "PGOPTIONS=-c enable_seqscan=off"); fi

  unset CNT; declare -A CNT; total=0; cnt_ok=1
  for T in "${TABLES[@]}"; do
    c=$(pgx -tAc "SELECT count(*) FROM ($(sel_for "$T")) _s") || { echo "  count failed ${T}"; cnt_ok=0; break; }
    CNT[$T]=$c; total=$((total + c))
  done
  [ "$cnt_ok" = 1 ] || { echo "  skipping ${P} (count error)"; pgq -c "DROP TABLE IF EXISTS ${WORK};" >/dev/null 2>&1; continue; }
  printf '  rows:'; for T in "${TABLES[@]}"; do printf ' %s=%s' "$T" "${CNT[$T]}"; done; printf ' | total=%s | plan=%s\n' "$total" "$([ "${#PGENV[@]}" -eq 0 ] && echo seqscan || echo index)"

  if [ "$APPLY" != "1" ]; then echo "  [dry-run] would archive -> ${dest} then prune ${frozen} matches"; pgq -c "DROP TABLE IF EXISTS ${WORK};" >/dev/null 2>&1; continue; fi

  "${SSH_NAS[@]}" "mkdir -p '${dest}'" || { echo "  NAS mkdir failed; skipping ${P}"; pgq -c "DROP TABLE IF EXISTS ${WORK};" >/dev/null 2>&1; continue; }
  verified=1
  for T in "${TABLES[@]}"; do
    docker exec -i "${PGENV[@]}" "$PG_CONTAINER" psql -U "$PG_USER" -d "$PG_DB" -v ON_ERROR_STOP=1 \
      -c "COPY ($(sel_for "$T")) TO STDOUT WITH (FORMAT csv, HEADER true)" 2>/tmp/trn_arc_err.$$ \
      | gzip | "${SSH_NAS[@]}" "cat > '${dest}/${T}.csv.gz'"
    ps=("${PIPESTATUS[@]}")
    if [ "${ps[0]:-1}" -ne 0 ] || [ "${ps[1]:-1}" -ne 0 ] || [ "${ps[2]:-1}" -ne 0 ]; then
      echo "  EXPORT FAIL ${T} (pipestatus=${ps[*]}): $(head -c 300 /tmp/trn_arc_err.$$ 2>/dev/null)"; verified=0; break
    fi
    if ! "${SSH_NAS[@]}" "gzip -t '${dest}/${T}.csv.gz'"; then echo "  GZIP CORRUPT ${T}"; verified=0; break; fi
    fl=$("${SSH_NAS[@]}" "zcat '${dest}/${T}.csv.gz' | wc -l") || { echo "  count-back failed ${T}"; verified=0; break; }
    exp=$(( CNT[$T] + 1 ))   # +1 = CSV header line
    if [ "$fl" -ne "$exp" ]; then echo "  VERIFY FAIL ${T}: archived_lines=${fl} expected=${exp}"; verified=0; break; fi
    echo "    ok ${T}: ${CNT[$T]} rows archived + verified"
  done
  rm -f /tmp/trn_arc_err.$$

  if [ "$verified" != "1" ]; then echo "  !! verification failed for ${P} — DB NOT modified."; pgq -c "DROP TABLE IF EXISTS ${WORK};" >/dev/null 2>&1; continue; fi

  { printf '{"patch":"%s","archivedAtUtc":"%s","keepPatches":%s,"residual":%s,"frozenMatches":%s,"rows":{' \
      "$P" "$(date -u +%FT%TZ)" "$KEEP_PATCHES" "$is_residual" "$frozen"
    first=1; for T in "${TABLES[@]}"; do [ "$first" = 1 ] || printf ','; printf '"%s":%s' "$T" "${CNT[$T]}"; first=0; done
    printf '}}\n'; } | "${SSH_NAS[@]}" "cat > '${dest}/_manifest.json'"

  # Bounded cascade prune of exactly the frozen set.
  prune_ok=1
  while :; do
    rem=$(pgval "SELECT count(*) FROM ${WORK}") || { echo "  remaining-count failed"; prune_ok=0; break; }
    [ "${rem:-0}" -eq 0 ] && break
    pgq -c "WITH batch AS (SELECT \"Id\" FROM ${WORK} LIMIT ${BATCH}),
                         del_m AS (DELETE FROM \"Matches\" WHERE \"Id\" IN (SELECT \"Id\" FROM batch))
                    DELETE FROM ${WORK} WHERE \"Id\" IN (SELECT \"Id\" FROM batch);" \
      || { echo "  batch delete failed (remaining=${rem})"; prune_ok=0; break; }
  done
  pgq -c "DROP TABLE IF EXISTS ${WORK};" >/dev/null 2>&1

  if [ "$prune_ok" = 1 ]; then
    [ "$is_residual" = 0 ] && "${SSH_NAS[@]}" "date -u +%FT%TZ > '${NAS_DIR}/${P}/_DONE'"
    echo "  ✓ archived + pruned ${P} (${frozen} matches, ~${total} rows)"
  else
    echo "  prune incomplete for ${P} (archive intact on NAS; remainder left in DB for next run)."
  fi
done

echo "Done. Deleted rows free pages for reuse inside Postgres (DB file does not shrink without VACUUM FULL,"
echo "which is intentionally avoided — it locks the table; autovacuum + plain reuse keep the DB from growing)."
