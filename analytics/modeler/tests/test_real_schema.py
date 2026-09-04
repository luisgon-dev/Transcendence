"""Every query the modeler issues, executed against a real migrated PostgreSQL schema.

The unit suite drives the pipeline through fakes. Fakes are fast and they cover branching, but a
fake only rejects what someone taught it to reject, so two production outages in a row came from SQL
that no fake could have failed on:

  * `Connection.executemany` -- a fake answered a method psycopg only exposes on the cursor. The real
    driver raised `AttributeError` at the terminal write, after the entire 173-champion sweep.
  * `incomplete placeholder: '%'` -- a SQL comment reading "~16% are kill events" made psycopg's
    client-side parser reject the statement. Killed the generation hours in, after the training draw.

Both are now caught by the fakes (they parse placeholders the way psycopg does), but the underlying
lesson is that a transcription of a query is not the query. This module removes the transcription: it
runs the real builders through a real connection against the schema the real migrations produce, so
a wrong column name, a wrong cast, a dropped index or a bad placeholder fails here instead of in
prod. The tables are empty -- these assert the statements are VALID, not that they return rows, and
an empty cohort keeps them fast.

Skipped unless MODELER_TEST_DSN points at a migrated database. CI supplies one; locally:

    docker exec <pg> psql -U postgres -c 'CREATE DATABASE modeler_ci'
    docker exec <pg> bash -c 'pg_dump -U postgres --schema-only transcendence | psql -U postgres -d modeler_ci'
    # --schema-only leaves __EFMigrationsHistory empty, which the schema guard below rejects:
    docker exec <pg> bash -c 'pg_dump -U postgres --data-only -t \'"__EFMigrationsHistory"\' transcendence | psql -U postgres -d modeler_ci'
    MODELER_TEST_DSN=postgresql://postgres:postgres@localhost:55432/modeler_ci .venv/bin/python -m pytest tests/test_real_schema.py
"""

import os
import uuid
from datetime import datetime, timezone

import pytest

psycopg = pytest.importorskip("psycopg")
from psycopg.rows import dict_row  # noqa: E402

from build_lab_modeler import pipeline  # noqa: E402

DSN = os.environ.get("MODELER_TEST_DSN")

# A suite that skips itself is indistinguishable from a suite that passes, and that is exactly how
# this gap stayed open. CI sets MODELER_REQUIRE_DB, which turns a missing DSN from a quiet skip into
# a hard failure -- so a broken service container can never read as green.
if not DSN and os.environ.get("MODELER_REQUIRE_DB"):
    raise RuntimeError(
        "MODELER_REQUIRE_DB is set but MODELER_TEST_DSN is not: the database these tests exist to "
        "exercise is unreachable, and skipping them here would report a false pass."
    )

pytestmark = pytest.mark.skipif(
    not DSN, reason="MODELER_TEST_DSN is unset; needs a migrated PostgreSQL schema"
)

PATCHES = ["16.17"]
CUTOFF = datetime(2026, 9, 1, tzinfo=timezone.utc)
# A sample range is a pair of match ids; the loaders slice on `>= from AND < until`.
SAMPLE_RANGE = (uuid.UUID(int=0), uuid.UUID(int=2**128 - 1))


@pytest.fixture
def connection():
    """A real connection, rolled back after every test so the schema is never dirtied."""
    with psycopg.connect(DSN, row_factory=dict_row) as conn:
        yield conn
        conn.rollback()


def test_the_schema_under_test_is_the_migrated_one(connection):
    """A guard on the guard: an empty or hand-rolled database would make every test below vacuous."""
    with connection.cursor() as cursor:
        applied = cursor.execute('SELECT count(*) AS n FROM "__EFMigrationsHistory"').fetchone()["n"]
        columns = cursor.execute(
            """
            SELECT count(*) AS n FROM information_schema.columns
            WHERE table_name = 'MatchTimelineEventPayloads'
              AND column_name IN ('KillerId', 'KillerTeamId', 'TeamId')
            """
        ).fetchone()["n"]
    assert applied > 0, "no migrations applied -- this is not a migrated schema"
    assert columns == 3, (
        "the timeline kill scalars are missing; the loader reads them as columns, so this database "
        "predates AddTimelineKillEventScalars and every query below would be testing the wrong shape"
    )


# Each entry is (name, callable taking a connection). Both branches of the optional arguments are
# covered, because champion_id and match_sample_range each change the SQL that gets built.
def loader_cases():
    def rank_column(connection):
        return pipeline.resolve_rank_offset_column(connection)

    return [
        ("resolve_rank_offset_column", lambda c: pipeline.resolve_rank_offset_column(c)),
        ("load_cohort_match_count", lambda c: pipeline.load_cohort_match_count(c, PATCHES, CUTOFF)),
        ("load_cohort_champions", lambda c: pipeline.load_cohort_champions(c, PATCHES, CUTOFF)),
        (
            "load_training_sample_ranges",
            lambda c: pipeline.load_training_sample_ranges(c, PATCHES, CUTOFF, 10_000, 5_000, 2),
        ),
        ("load_materially_changed_items", lambda c: pipeline.load_materially_changed_items(c, PATCHES)),
        ("load_materially_changed_runes", lambda c: pipeline.load_materially_changed_runes(c, PATCHES)),
        (
            "load_materially_changed_champions",
            lambda c: pipeline.load_materially_changed_champions(c, PATCHES),
        ),
        ("load_champion_archetypes", lambda c: pipeline.load_champion_archetypes(c, PATCHES[0])),
        ("load_patch_change_set", lambda c: pipeline.load_patch_change_set(c, PATCHES)),
        (
            "load_item_events[cohort]",
            lambda c: pipeline.load_item_events(c, PATCHES, CUTOFF, rank_column(c), None, None),
        ),
        (
            "load_item_events[champion+sample]",
            lambda c: pipeline.load_item_events(c, PATCHES, CUTOFF, rank_column(c), 1, SAMPLE_RANGE),
        ),
        (
            "load_rune_decisions[cohort]",
            lambda c: pipeline.load_rune_decisions(c, PATCHES, CUTOFF, rank_column(c), None, None),
        ),
        (
            "load_rune_decisions[champion+sample]",
            lambda c: pipeline.load_rune_decisions(c, PATCHES, CUTOFF, rank_column(c), 1, SAMPLE_RANGE),
        ),
        (
            "load_spell_decisions[cohort]",
            lambda c: pipeline.load_spell_decisions(c, PATCHES, CUTOFF, rank_column(c), None, None),
        ),
        (
            "load_spell_decisions[champion+sample]",
            lambda c: pipeline.load_spell_decisions(c, PATCHES, CUTOFF, rank_column(c), 1, SAMPLE_RANGE),
        ),
        (
            "load_participant_teams[cohort]",
            lambda c: pipeline.load_participant_teams(c, PATCHES, CUTOFF, None, None),
        ),
        (
            "load_participant_teams[champion+sample]",
            lambda c: pipeline.load_participant_teams(c, PATCHES, CUTOFF, 1, SAMPLE_RANGE),
        ),
    ]


@pytest.mark.parametrize("name,run", loader_cases(), ids=[case[0] for case in loader_cases()])
def test_the_loader_executes_against_the_real_schema(connection, name, run):
    """The loader's own SQL, sent to a real server. Empty tables still parse, plan and type-check it."""
    run(connection)


@pytest.mark.parametrize(
    "champion_id,sample_range",
    [(None, None), (1, None), (None, SAMPLE_RANGE), (1, SAMPLE_RANGE)],
    ids=["cohort", "champion", "sample", "champion+sample"],
)
def test_the_timeline_query_executes_in_every_shape(connection, champion_id, sample_range):
    """The query the ~16%-comment broke, in all four shapes its optional arguments produce."""
    sql, params = pipeline.timeline_state_events_query(
        PATCHES, CUTOFF, champion_id=champion_id, match_sample_range=sample_range
    )
    with connection.cursor() as cursor:
        cursor.execute(sql, params).fetchall()


def test_the_timeline_query_still_uses_the_covering_index(connection):
    """The kill-event index is the difference between a 7.6 GB index-only scan and a 77 GB seq scan.

    A dropped INCLUDE column or a changed WHERE clause would not fail any other test -- the query
    would still return the right rows, just after reading the whole table, which is what made a
    generation take days. Asserted on the plan rather than on runtime so it holds on an empty table.
    """
    sql, params = pipeline.timeline_state_events_query(PATCHES, CUTOFF, champion_id=1)
    with connection.cursor() as cursor:
        # enable_seqscan=off: on an EMPTY table a seq scan is always cheapest, so the planner would
        # pick one no matter how good the index is. This asks whether the index CAN serve the query.
        cursor.execute("SET LOCAL enable_seqscan = off")
        plan = "\n".join(
            row["QUERY PLAN"] for row in cursor.execute("EXPLAIN " + sql, params).fetchall()
        )
    assert "IX_MatchTimelineEventPayloads_KillEvents" in plan, (
        "the timeline query no longer reaches the kill-event covering index:\n" + plan
    )


def test_the_estimate_writes_execute_against_the_real_tables(connection):
    """`executemany` through a cursor, into the real tables, with a real foreign key.

    This is the write that `Connection.executemany` died on -- the last statement of a multi-day run.
    Rolled back, so it costs nothing and leaves nothing behind.
    """
    generation_id = uuid.uuid4()
    with connection.cursor() as cursor:
        cursor.execute(
            """
            INSERT INTO "BuildLabGenerations" (
                "Id", "Status", "IsActive", "Patch", "RankScope", "DatasetVersion",
                "StaticDataVersion", "ModelVersion", "CodeRevision", "IncludedPatchesJson",
                "IncludedRegionsJson", "SourceCutoffUtc", "MatchCount", "ArtifactManifestJson",
                "ValidationMetricsJson", "PromotionHistoryJson", "CreatedAtUtc"
            ) VALUES (
                %s, 0, false, %s, 'all', 'v1', 'v1', 'v1', 'test',
                %s::jsonb, '[]'::jsonb, %s, 0, '{}', '{}'::jsonb, '[]'::jsonb, NOW()
            )
            """,
            (generation_id, PATCHES[0], '["16.17"]', CUTOFF),
        )

    action_row = (
        uuid.uuid4(), generation_id, 1, "MIDDLE", 0, PATCHES[0],
        "all", "item", 1, "hash", "[]",
        "3153", "[3153]", 0.01, -0.01, 0.03,
        0.51, 0.10, 100, 90.0,
        12.5, 0.8, 0.02,
        True, True, "high", "champion",
        "baseline", "solid", 0.9,
        None,
    )
    path_row = (
        uuid.uuid4(), generation_id, 1, "MIDDLE", 0, PATCHES[0],
        "all", "pathhash", "[3153]", 0.52,
        0.01, -0.01, 0.03, 100, 90.0, True, None,
    )
    pipeline.insert_estimates(connection, [action_row])
    pipeline.insert_path_estimates(connection, [path_row])

    with connection.cursor() as cursor:
        written = cursor.execute(
            'SELECT count(*) AS n FROM "AdjustedActionEstimates" WHERE "GenerationId" = %s',
            (generation_id,),
        ).fetchone()["n"]
    assert written == 1
    connection.rollback()
