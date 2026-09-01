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
# RESUMABILITY
#   Progress is a MatchId watermark, not "WHERE KillerId IS NULL". Null is a legitimate value --
#   BUILDING_KILL carries no killerId -- so a null-based resume would reprocess those rows forever
#   and could never terminate.
#
# Env: PG_CONTAINER(transcendence-postgres) PG_USER(postgres) PG_DB(transcendence)
#      BATCH_MATCHES(2000) STATE(/var/lib/transcendence-deploy/kill-scalar-backfill.watermark)
#      SKIP_VACUUM(0)
set -uo pipefail

PG_CONTAINER="${PG_CONTAINER:-transcendence-postgres}"
PG_USER="${PG_USER:-postgres}"
PG_DB="${PG_DB:-transcendence}"
BATCH_MATCHES="${BATCH_MATCHES:-2000}"
STATE="${STATE:-/var/lib/transcendence-deploy/kill-scalar-backfill.watermark}"
KILLS="'CHAMPION_KILL','BUILDING_KILL','ELITE_MONSTER_KILL'"

psql() { docker exec -i "$PG_CONTAINER" psql -U "$PG_USER" -d "$PG_DB" -v ON_ERROR_STOP=1 "$@"; }
q()    { psql -tAc "$1"; }

mkdir -p "$(dirname "$STATE")"
watermark="$(cat "$STATE" 2>/dev/null || echo '00000000-0000-0000-0000-000000000000')"
echo "$(date -u +%H:%M:%S) resuming from MatchId > $watermark (batch=$BATCH_MATCHES matches)"

total=0
while :; do
  # One batch = the next N match ids in PK order. Driving off Matches keeps each UPDATE bounded by
  # the payload rows of a known, small set rather than by a scan of the payload table.
  # ORDER BY ... DESC LIMIT 1, not max(): Postgres has no max(uuid) aggregate, and the version that
  # used one failed per batch, reported "backfilled 0 rows" and still exited 0 -- a silent no-op
  # that looked exactly like success.
  upper="$(q "SELECT id FROM (SELECT \"Id\" AS id FROM \"Matches\" WHERE \"Id\" > '$watermark' ORDER BY \"Id\" LIMIT $BATCH_MATCHES) s ORDER BY id DESC LIMIT 1;")"
  if [ -z "$upper" ]; then
    remaining="$(q "SELECT count(*) FROM \"Matches\" WHERE \"Id\" > '$watermark';")"
    if [ "${remaining:-0}" != "0" ]; then
      echo "ABORT: $remaining matches remain past the watermark but the batch query returned nothing."
      exit 1
    fi
    echo "$(date -u +%H:%M:%S) no matches left"
    break
  fi

  updated="$(q "WITH b AS (
      UPDATE \"MatchTimelineEventPayloads\" p
      SET \"KillerId\"     = NULLIF(COALESCE(p.\"PayloadJson\"->>'killerId',     p.\"PayloadJson\"->>'killerid'), '')::int,
          \"KillerTeamId\" = NULLIF(COALESCE(p.\"PayloadJson\"->>'killerTeamId', p.\"PayloadJson\"->>'killerteamid'), '')::int,
          \"TeamId\"       = NULLIF(COALESCE(p.\"PayloadJson\"->>'teamId',       p.\"PayloadJson\"->>'teamid'), '')::int
      WHERE p.\"MatchId\" > '$watermark' AND p.\"MatchId\" <= '$upper'
        AND p.\"EventType\" IN ($KILLS)
      RETURNING 1) SELECT count(*) FROM b;")"
  total=$(( total + ${updated:-0} ))
  watermark="$upper"
  printf '%s\n' "$watermark" > "$STATE"
  echo "$(date -u +%H:%M:%S) +${updated:-0} rows (total $total), watermark $watermark"
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
