#!/usr/bin/env bash
set -euo pipefail

POSTGRES_CONTAINER="${POSTGRES_CONTAINER:-transcendence-postgres}"
POSTGRES_USER="${POSTGRES_USER:-postgres}"
POSTGRES_DATABASE="${POSTGRES_DATABASE:-transcendence}"
STATE_DIR="${STATE_DIR:-/var/lib/transcendence-performance}"
MAX_STEADY_BACKLOG="${MAX_STEADY_BACKLOG:-500}"
TOP_STATEMENTS="${TOP_STATEMENTS:-20}"
RETENTION_DAYS="${RETENTION_DAYS:-30}"

if [[ "$STATE_DIR" != /* || "$STATE_DIR" == "/" ]]; then
  echo "STATE_DIR must be an explicit absolute directory other than /." >&2
  exit 2
fi
if ! [[ "$MAX_STEADY_BACKLOG" =~ ^[0-9]+$ && "$TOP_STATEMENTS" =~ ^[1-9][0-9]*$ ]]; then
  echo "MAX_STEADY_BACKLOG and TOP_STATEMENTS must be non-negative integers." >&2
  exit 2
fi

install -d -m 0750 "$STATE_DIR"
stamp="$(date -u +%Y%m%dT%H%M%SZ)"
report="$STATE_DIR/postgres-performance-${stamp}.md"
current_sizes="$STATE_DIR/.table-sizes-${stamp}.tmp"
previous_sizes="$STATE_DIR/latest-table-sizes.tsv"

psql_base=(
  docker exec "$POSTGRES_CONTAINER"
  psql
  -X
  -v ON_ERROR_STOP=1
  -U "$POSTGRES_USER"
  -d "$POSTGRES_DATABASE"
)

pgval() {
  "${psql_base[@]}" -Atqc "$1"
}

append_query() {
  local title="$1"
  local sql="$2"
  {
    printf '\n## %s\n\n```text\n' "$title"
    "${psql_base[@]}" -P pager=off -P border=1 -P format=aligned -c "$sql"
    printf '```\n'
  } >>"$report"
}

if ! docker inspect "$POSTGRES_CONTAINER" >/dev/null 2>&1; then
  echo "PostgreSQL container '$POSTGRES_CONTAINER' was not found." >&2
  exit 1
fi

runnable_backlog="$(
  pgval "
    SELECT CASE
      WHEN to_regclass('hangfire.jobqueue') IS NULL THEN 0
      ELSE (
        SELECT count(*)
        FROM hangfire.jobqueue
        WHERE fetchedat IS NULL
           OR fetchedat < now() - interval '30 minutes'
      )
    END;"
)"

sample_state="steady"
if (( runnable_backlog > MAX_STEADY_BACKLOG )); then
  sample_state="busy"
fi

cat >"$report" <<EOF
# PostgreSQL performance report

- Captured: ${stamp}
- Database: ${POSTGRES_DATABASE}
- Runnable Hangfire backlog: ${runnable_backlog}
- Sample state: **${sample_state}**
- Steady-state threshold: ${MAX_STEADY_BACKLOG}

This report is read-only. It identifies candidates for investigation; it never drops indexes,
changes settings, resets statistics, vacuums tables, or runs query plans automatically.
EOF

if [[ "$sample_state" == "busy" ]]; then
  cat >>"$report" <<EOF

> The ingestion backlog is above the steady-state threshold. Treat latency, CPU, cache, and table
> activity as a busy-system baseline. Defer permanent tuning decisions until a later report is
> captured with the backlog below ${MAX_STEADY_BACKLOG}.
EOF
fi

append_query "Runtime and instrumentation" "
  SELECT version();
  SELECT name, setting, unit, source
  FROM pg_settings
  WHERE name IN (
    'max_connections', 'shared_buffers', 'work_mem', 'effective_cache_size',
    'maintenance_work_mem', 'shared_preload_libraries', 'track_io_timing'
  )
  ORDER BY name;
  SELECT now() - pg_postmaster_start_time() AS database_uptime,
         stats_reset
  FROM pg_stat_database
  WHERE datname = current_database();"

append_query "Queue state and recent progress" "
  SELECT queue,
         count(*) AS total,
         count(*) FILTER (WHERE fetchedat IS NULL) AS ready,
         count(*) FILTER (WHERE fetchedat IS NOT NULL) AS fetched
  FROM hangfire.jobqueue
  GROUP BY queue
  ORDER BY queue;

  SELECT name,
         count(*) AS transitions_last_hour,
         max(createdat) AS latest
  FROM hangfire.state
  WHERE createdat >= now() - interval '1 hour'
  GROUP BY name
  ORDER BY name;"

append_query "Database I/O and temporary work" "
  SELECT datname,
         numbackends,
         round(
           100.0 * blks_hit / NULLIF(blks_hit + blks_read, 0),
           2
         ) AS cache_hit_percent,
         temp_files,
         pg_size_pretty(temp_bytes) AS temp_bytes,
         deadlocks,
         blk_read_time,
         blk_write_time
  FROM pg_stat_database
  WHERE datname = current_database();"

append_query "Connections by application" "
  SELECT coalesce(nullif(application_name, ''), '(unset)') AS application,
         state,
         count(*) AS connections,
         max(CASE WHEN state = 'active' THEN now() - query_start END) AS oldest_active
  FROM pg_stat_activity
  WHERE datname = current_database()
  GROUP BY application_name, state
  ORDER BY connections DESC, application, state;"

append_query "Top statements by total execution time" "
  SELECT calls,
         round(total_exec_time::numeric, 2) AS total_ms,
         round(mean_exec_time::numeric, 2) AS mean_ms,
         rows,
         shared_blks_hit,
         shared_blks_read,
         temp_blks_written,
         pg_size_pretty(wal_bytes::bigint) AS wal,
         left(regexp_replace(query, E'\\\\s+', ' ', 'g'), 260) AS query
  FROM pg_stat_statements
  WHERE dbid = (SELECT oid FROM pg_database WHERE datname = current_database())
    AND query NOT LIKE '%pg_stat_statements%'
  ORDER BY total_exec_time DESC
  LIMIT ${TOP_STATEMENTS};"

append_query "Top statements by mean execution time (minimum 5 calls)" "
  SELECT calls,
         round(mean_exec_time::numeric, 2) AS mean_ms,
         round(total_exec_time::numeric, 2) AS total_ms,
         shared_blks_read,
         temp_blks_written,
         left(regexp_replace(query, E'\\\\s+', ' ', 'g'), 260) AS query
  FROM pg_stat_statements
  WHERE dbid = (SELECT oid FROM pg_database WHERE datname = current_database())
    AND calls >= 5
    AND query NOT LIKE '%pg_stat_statements%'
  ORDER BY mean_exec_time DESC
  LIMIT ${TOP_STATEMENTS};"

append_query "Table size, scan mix, and vacuum health" "
  SELECT relname AS table_name,
         pg_size_pretty(pg_total_relation_size(relid)) AS total_size,
         seq_scan,
         idx_scan,
         n_live_tup,
         n_dead_tup,
         round(100.0 * n_dead_tup / NULLIF(n_live_tup + n_dead_tup, 0), 2) AS dead_percent,
         last_autovacuum,
         last_autoanalyze
  FROM pg_stat_user_tables
  ORDER BY pg_total_relation_size(relid) DESC
  LIMIT 30;"

append_query "Large low-use indexes (review only)" "
  SELECT schemaname,
         relname AS table_name,
         indexrelname AS index_name,
         idx_scan,
         pg_size_pretty(pg_relation_size(indexrelid)) AS index_size,
         pg_get_indexdef(indexrelid) AS definition
  FROM pg_stat_user_indexes
  WHERE pg_relation_size(indexrelid) >= 1024 * 1024
  ORDER BY idx_scan ASC, pg_relation_size(indexrelid) DESC
  LIMIT 30;"

"${psql_base[@]}" -At -F $'\t' -c "
  SELECT relname,
         pg_total_relation_size(relid),
         n_live_tup,
         n_dead_tup
  FROM pg_stat_user_tables
  ORDER BY relname;" >"$current_sizes"

{
  printf '\n## Table growth since previous report\n\n```text\n'
  if [[ -s "$previous_sizes" ]]; then
    awk -F '\t' '
      NR == FNR { previous[$1] = $2; next }
      {
        old = ($1 in previous) ? previous[$1] : 0
        delta = $2 - old
        printf "%-48s current_bytes=%-14s delta_bytes=%s\n", $1, $2, delta
      }
    ' "$previous_sizes" "$current_sizes"
  else
    printf 'No previous snapshot exists. This run establishes the table-size baseline.\n'
  fi
  printf '```\n'
} >>"$report"

mv "$current_sizes" "$previous_sizes"
chmod 0640 "$previous_sizes" "$report"

# Retention is intentionally scoped to this validated state directory and report filename pattern.
find "$STATE_DIR" -maxdepth 1 -type f -name 'postgres-performance-*.md' \
  -mtime "+${RETENTION_DAYS}" -delete

ln -sfn "$(basename "$report")" "$STATE_DIR/latest.md"
printf '%s\n' "$report"
