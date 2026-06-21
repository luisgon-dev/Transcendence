#!/usr/bin/env bash
#
# check-migrations.sh — hot-table DDL safety lint for EF migrations.
#
# Fails when a migration ADDED by this branch (vs the base ref) performs DDL that
# write-locks or rewrites one of the large, continuously-written tables. Such DDL
# must be applied out-of-band — see docs/DEVELOPMENT.md, "Applying index migrations
# to hot tables". Only newly-added migrations are inspected, so the many existing
# plain-CreateIndex migrations already in history are never flagged.
#
# Env:
#   BASE_REF   git ref to diff against (default: origin/main)
#
set -euo pipefail

BASE_REF="${BASE_REF:-origin/main}"
MIG_DIR='Transcendence.Service/Migrations'
HOT_TABLES='Summoners|Matches|MatchParticipants|MatchParticipantTimelineSnapshots'

if ! git rev-parse --verify -q "${BASE_REF}^{commit}" >/dev/null 2>&1; then
  echo "check-migrations: base ref '${BASE_REF}' not found; skipping hot-table lint."
  exit 0
fi
MERGE_BASE="$(git merge-base "${BASE_REF}" HEAD 2>/dev/null || echo "${BASE_REF}")"

# Newly ADDED migration files only (exclude Designer + ModelSnapshot).
NEW_MIGRATIONS=()
while IFS= read -r line; do
  [ -n "$line" ] && NEW_MIGRATIONS+=("$line")
done < <(git diff --name-only --diff-filter=A "${MERGE_BASE}...HEAD" -- "${MIG_DIR}" \
           | grep -E '\.cs$' | grep -vE '\.Designer\.cs$|ModelSnapshot\.cs$' || true)

if [ "${#NEW_MIGRATIONS[@]}" -eq 0 ]; then
  echo "check-migrations: no new migrations in this change. OK."
  exit 0
fi

violations=0
for f in "${NEW_MIGRATIONS[@]}"; do
  [ -f "$f" ] || continue

  # (1) CreateIndex on a hot table — CREATE INDEX always takes a SHARE lock that
  #     blocks writes for the full build. Split RS on ");" so each migrationBuilder
  #     call is one record and the table is matched to the CreateIndex it belongs to.
  if awk 'BEGIN{RS=");"} /CreateIndex\(/ && /table: *"('"$HOT_TABLES"')"/ {hit=1} END{exit !hit}' "$f"; then
    echo "::error file=$f::Adds an index on a hot table (Summoners/Matches/MatchParticipants) via plain CreateIndex, which write-locks the table. Apply it out-of-band with CREATE INDEX CONCURRENTLY and record it manually — see docs/DEVELOPMENT.md 'Applying index migrations to hot tables'."
    violations=$((violations + 1))
  fi

  # (2) AddColumn with a SQL default on a hot table — a volatile defaultValueSql
  #     rewrites the entire table under lock. (Plain constant defaultValue is a
  #     fast metadata-only default on PostgreSQL 11+, so it is not flagged.)
  if awk 'BEGIN{RS=");"} /AddColumn[<(]/ && /table: *"('"$HOT_TABLES"')"/ && /defaultValueSql:/ {hit=1} END{exit !hit}' "$f"; then
    echo "::error file=$f::Adds a column with a SQL default (defaultValueSql) on a hot table; a volatile default rewrites the whole table under lock. Review and apply out-of-band if needed — see docs/DEVELOPMENT.md."
    violations=$((violations + 1))
  fi
done

if [ "$violations" -gt 0 ]; then
  echo ""
  echo "check-migrations: ${violations} hot-table DDL violation(s) in newly-added migration(s)."
  exit 1
fi

echo "check-migrations: ${#NEW_MIGRATIONS[@]} new migration(s) inspected, no hot-table lock/rewrite DDL. OK."
