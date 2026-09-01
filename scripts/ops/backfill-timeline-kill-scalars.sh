#!/usr/bin/env bash
#
# backfill-timeline-kill-scalars.sh — populate KillerId/KillerTeamId/TeamId on kill-type rows of
# MatchTimelineEventPayloads from the PayloadJson they were previously read out of.
#
# WHY
#   The modeler's cohort scan reads exactly these three scalars. Extracting them from jsonb meant
#   Postgres could not use an index (it cannot satisfy a jsonb expression from one), so the query
#   was a Parallel Seq Scan of the whole 165M-row / 77 GB table to keep the ~16% that are kill
#   events -- the single largest cost in a generation on this host's spinning disk. As columns,
#   IX_MatchTimelineEventPayloads_KillEvents covers the query and it becomes an Index Only Scan.
#
# WHY A VACUUM AT THE END, NOT AS HOUSEKEEPING
#   An index-only scan requires the visibility map, and a bulk UPDATE leaves it unset. Measured on a
#   1M-row fixture: before VACUUM the planner produced a Bitmap Heap Scan even with enable_seqscan
#   off; after it, Index Only Scan with Heap Fetches: 0 and buffers 21,439 -> 4,457. Skip the vacuum
#   and this backfill buys nothing at all.
#
# WHY ctid PAGE RANGES, NOT MatchId RANGES
#   The obvious batching -- pages of match ids -- produced an Index Scan over ~177k rows per batch,
#   which on this host means 177k RANDOM heap fetches on a spinning disk: measured at ~8 minutes a
#   batch, so ~30 hours for the table. Batching by physical page range instead gives a Tid Range
#   Scan, i.e. the same 65 GB read SEQUENTIALLY, which the same disk does two orders of magnitude
#   faster. The work is identical; only the access pattern changes.
#
# RESUMABILITY
#   Progress is the last completed page number. Deliberately not "WHERE KillerId IS NULL": null is a
#   legitimate value -- BUILDING_KILL carries no killerId -- so a null-based resume would reprocess
#   those rows forever and never terminate.
#
# Env: PG_CONTAINER(transcendence-postgres) PG_USER(postgres) PG_DB(transcendence)
#      PAGES_PER_BATCH(200000) STATE(/var/lib/transcendence-deploy/kill-scalar-backfill.watermark)
#      SKIP_VACUUM(0)
set -uo pipefail

PG_CONTAINER="${PG_CONTAINER:-transcendence-postgres}"
PG_USER="${PG_USER:-postgres}"
PG_DB="${PG_DB:-transcendence}"
PAGES_PER_BATCH="${PAGES_PER_BATCH:-200000}"
STATE="${STATE:-/var/lib/transcendence-deploy/kill-scalar-backfill.watermark}"
KILLS="'CHAMPION_KILL','BUILDING_KILL','ELITE_MONSTER_KILL'"

psql() { docker exec -i "$PG_CONTAINER" psql -U "$PG_USER" -d "$PG_DB" -v ON_ERROR_STOP=1 "$@"; }
q()    { psql -tAc "$1"; }

mkdir -p "$(dirname "$STATE")"
page="$(cat "$STATE" 2>/dev/null || echo 0)"
# relpages is an estimate, so overshoot the end: a range past the last page is simply empty.
pages="$(q "SELECT (relpages * 1.1)::bigint FROM pg_class WHERE relname = 'MatchTimelineEventPayloads';")"
echo "$(date -u +%H:%M:%S) resuming at page $page of ~$pages ($PAGES_PER_BATCH pages/batch)"

total=0
while [ "$page" -lt "$pages" ]; do
  upper=$(( page + PAGES_PER_BATCH ))
  updated="$(q "WITH b AS (
      UPDATE \"MatchTimelineEventPayloads\" p
      SET \"KillerId\"     = NULLIF(COALESCE(p.\"PayloadJson\"->>'killerId',     p.\"PayloadJson\"->>'killerid'), '')::int,
          \"KillerTeamId\" = NULLIF(COALESCE(p.\"PayloadJson\"->>'killerTeamId', p.\"PayloadJson\"->>'killerteamid'), '')::int,
          \"TeamId\"       = NULLIF(COALESCE(p.\"PayloadJson\"->>'teamId',       p.\"PayloadJson\"->>'teamid'), '')::int
      WHERE p.ctid >= '($page,0)'::tid AND p.ctid < '($upper,0)'::tid
        AND p.\"EventType\" IN ($KILLS)
      RETURNING 1) SELECT count(*) FROM b;")"
  if [ -z "$updated" ]; then
    echo "ABORT: batch at page $page returned nothing at all (query error, not an empty range)."
    exit 1
  fi
  total=$(( total + updated ))
  page="$upper"
  printf '%s\n' "$page" > "$STATE"
  echo "$(date -u +%H:%M:%S) pages $page/$pages  +$updated rows (total $total)"
done

echo "$(date -u +%H:%M:%S) backfilled $total rows"

# A run that touches nothing is only legitimate when there is genuinely nothing to do. Anything
# else -- a broken batch query, a bad watermark -- must fail loudly rather than look like success.
if [ "$total" -eq 0 ]; then
  outstanding="$(q "SELECT count(*) FROM \"MatchTimelineEventPayloads\" p
                    WHERE p.\"EventType\" IN ($KILLS)
                      AND p.\"KillerId\" IS NULL AND p.\"KillerTeamId\" IS NULL AND p.\"TeamId\" IS NULL
                      AND (p.\"PayloadJson\" ? 'killerId' OR p.\"PayloadJson\" ? 'teamId');")"
  if [ "${outstanding:-0}" != "0" ]; then
    echo "ABORT: 0 rows updated but $outstanding kill rows still carry scalars only in jsonb."
    exit 1
  fi
  echo "nothing to do: every kill row already carries its scalars"
fi

if [ "${SKIP_VACUUM:-0}" = "1" ]; then
  echo "SKIP_VACUUM=1: the index-only scan will NOT engage until this table is vacuumed."
  exit 0
fi
echo "$(date -u +%H:%M:%S) VACUUM (ANALYZE) -- required for the index-only scan; this is the slow part"
psql -c "VACUUM (ANALYZE) \"MatchTimelineEventPayloads\";" >/dev/null
echo "$(date -u +%H:%M:%S) done"
