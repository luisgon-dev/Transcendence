import inspect
import multiprocessing
import pathlib
import json
import logging
import re
from datetime import datetime, timedelta, timezone
from pathlib import Path
from uuid import uuid4

import numpy as np
import pandas as pd
import pytest

from build_lab_modeler import pipeline
from build_lab_modeler.pipeline import (
    DESIGN_MATRIX_MAX_COLUMNS,
    FEATURE_COLUMNS,
    GLOBAL_PRIOR_VARIANCE,
    TIMED_FAMILIES,
    UNKNOWN_ARCHETYPE,
    UNVERIFIED_BORROW_WEIGHT,
    MODELING_LOCK_KEY,
    RunOutcome,
    LeaseLost,
    PatchChangeSet,
    Settings,
    ACTION_POOLING_LEVELS,
    PATH_POOLING_LEVELS,
    action_records,
    action_tuples,
    apply_partial_pooling,
    apply_row_weights,
    commensurability_weights,
    release_modeling_lock,
    try_acquire_modeling_lock,
    average_timing,
    build_action_estimates,
    build_item_decisions,
    build_design_spec,
    build_path_estimates,
    build_rune_decisions,
    build_spell_decisions,
    apply_event_state,
    event_state_from_events,
    stream_cohort_event_state,
    clustered_standard_error,
    deidentified_export,
    design_matrix,
    direction_is_stable,
    doubly_robust_binary,
    enrich_with_predecision_event_state,
    evaluate_leakage,
    execute_guarded,
    expand_scopes,
    expected_calibration_error,
    hash_path,
    insert_estimates,
    load_cohort_champions,
    load_cohort_match_count,
    load_decision_frame,
    load_item_events,
    load_participant_teams,
    load_rune_decisions,
    load_spell_decisions,
    load_timeline_state_events,
    timeline_state_events_query,
    mark_failed,
    maximum_weighted_smd,
    model_generation,
    participant_level_comparator,
    patch_recency_weights,
    path_records,
    path_tuples,
    prepare_decisions,
    prune_stale_artifacts,
    rank_context_lateral,
    retained_generation_ids,
    champion_match_cte,
    scope_predicates,
    training_sample_modulus,
    structural_win_probability,
    train_structural_model,
    undone_purchase_indexes,
)


class FakeCursor:
    def __init__(self, rowcount: int, rows: list | None = None) -> None:
        self.rowcount = rowcount
        self._rows = rows or []

    def fetchall(self) -> list:
        return self._rows

    def fetchone(self):
        return self._rows[0] if self._rows else None


class FakeTransaction:
    """psycopg semantics: everything staged inside the block is discarded when the block raises."""

    def __init__(self, connection: "FakeConnection") -> None:
        self._connection = connection
        self._start = 0

    def __enter__(self) -> "FakeTransaction":
        self._connection.transaction_depth += 1
        self._start = len(self._connection.staged)
        return self

    def __exit__(self, exception_type, *_) -> bool:
        connection = self._connection
        connection.transaction_depth -= 1
        staged = connection.staged[self._start:]
        del connection.staged[self._start:]
        if exception_type is None:
            connection.committed.extend(staged)
        else:
            connection.rollbacks += 1
        return False


class EmptyServerCursor:
    """psycopg's server-side cursor shape, holding no rows."""

    description: list = []

    def __enter__(self):
        return self

    def __exit__(self, *_):
        return False

    def execute(self, sql, params=None):
        return self

    def fetchmany(self, size):
        return []


class FakeCursorHandle(EmptyServerCursor):
    """What `connection.cursor()` hands back.

    psycopg puts `executemany` on the cursor and never on the connection. Modelling that faithfully
    is the point: a fake that answers `connection.executemany` lets `insert_estimates` pass every
    test and then fail on the last statement of a two-day production run, which is exactly what
    happened to generation d693a582 after all 173 champions had been swept.
    """

    def __init__(self, connection: "FakeConnection") -> None:
        self._connection = connection

    def executemany(self, statement: str, rows: list) -> "FakeCursor":
        self._connection.statements.append((statement, rows))
        self._connection._stage(statement, rows)
        return FakeCursor(len(rows))


class FakeConnection:
    """Records every statement so the guarded status writes can be asserted without a database."""

    def __init__(
        self,
        cursors: list[FakeCursor] | None = None,
        rowcounts: dict[str, int] | None = None,
        rows: dict[str, list] | None = None,
        fail_on: str | None = None,
    ) -> None:
        self._cursors = list(cursors or [])
        self._rowcounts = dict(rowcounts or {})
        self._rows = dict(rows or {})
        self._fail_on = fail_on
        self.server_cursors: list = []
        self.statements: list[tuple[str, tuple | list | None]] = []
        # Statements that survived their transaction block, as opposed to every statement attempted.
        self.staged: list[tuple[str, tuple | list | None]] = []
        self.committed: list[tuple[str, tuple | list | None]] = []
        self.transaction_depth = 0
        self.commits = 0
        self.rollbacks = 0

    def execute(self, statement: str, parameters=None) -> FakeCursor:
        self.statements.append((statement, parameters))
        if self._fail_on and self._fail_on in statement:
            raise RuntimeError("the statement failed")
        self._stage(statement, parameters)
        return self._cursor_for(statement)

    def cursor(self, name=None, row_factory=None):
        """A server-side cursor over nothing.

        The sweep streams the cohort's event state through one of these. A generation driven against
        this fake has no timeline rows, so an empty stream is the honest answer -- and keeping the real
        `stream_cohort_event_state` in the path means these tests still exercise it rather than
        stubbing the step that broke prod.
        """
        self.server_cursors.append(name)
        return FakeCursorHandle(self)

    def transaction(self) -> FakeTransaction:
        return FakeTransaction(self)

    def commit(self) -> None:
        self.commits += 1

    def rollback(self) -> None:
        self.rollbacks += 1

    def __enter__(self) -> "FakeConnection":
        return self

    def __exit__(self, *_) -> bool:
        return False

    def _stage(self, statement: str, parameters) -> None:
        target = self.staged if self.transaction_depth else self.committed
        target.append((statement, parameters))

    def _cursor_for(self, statement: str) -> FakeCursor:
        if self._cursors:
            return self._cursors.pop(0)
        rowcount = next(
            (count for fragment, count in self._rowcounts.items() if fragment in statement),
            1,
        )
        rows = next((values for fragment, values in self._rows.items() if fragment in statement), [])
        return FakeCursor(rowcount, rows)


def modeler_settings(monkeypatch, **environment) -> Settings:
    monkeypatch.setenv("BUILD_LAB_DATABASE_URL", "postgresql://localhost/test")
    monkeypatch.setenv("BUILD_LAB_DEIDENTIFICATION_SALT", "s" * 32)
    for name, value in environment.items():
        monkeypatch.setenv(name, value)
    return Settings.from_env()


def test_hash_path_is_order_sensitive_and_stable():
    assert hash_path([1, 2, 3]) == hash_path([1, 2, 3])
    assert hash_path([1, 2, 3]) != hash_path([3, 2, 1])


def test_calibration_error_is_zero_for_perfect_bins():
    actual = np.array([0, 0, 1, 1])
    predicted = np.array([0.0, 0.0, 1.0, 1.0])
    assert expected_calibration_error(actual, predicted, bins=2) == 0


def test_patch_recency_weights_cap_borrowing_at_two_prior_patches():
    assert patch_recency_weights("26.14", ["26.12", "26.14", "26.13", "26.11"]) == {
        "26.14": 1.0,
        "26.12": 0.60,
        "26.13": 0.35,
    }


def test_doubly_robust_estimator_recovers_positive_synthetic_effect():
    rng = np.random.default_rng(7)
    size = 4000
    skill = rng.normal(size=size)
    treatment_probability = 1 / (1 + np.exp(-0.8 * skill))
    treated = rng.binomial(1, treatment_probability)
    outcome_probability = 1 / (1 + np.exp(-(-0.2 + 0.6 * skill + 0.55 * treated)))
    won = rng.binomial(1, outcome_probability)
    frame = pd.DataFrame(
        {
            "action_key": np.where(treated == 1, "A", "B"),
            "won": won,
            "minute": 10 + skill,
            "gold": 5000 + 400 * skill,
            "current_gold": 1000 + 100 * skill,
            "xp": 4000 + 300 * skill,
            "cs": 80 + 10 * skill,
            "level": 9 + skill,
            "team_gold_diff": 300 * skill,
            "team_xp_diff": 200 * skill,
            "team_cs_diff": 8 * skill,
            "match_date": np.arange(size),
            "match_id": np.arange(size),
        }
    )
    estimate = doubly_robust_binary(frame, "A")
    assert estimate is not None
    assert estimate["estimate"] > 0.05
    assert estimate["low"] < estimate["estimate"] < estimate["high"]


def test_item_replay_drops_undone_choice_and_preserves_ordered_inventory_state():
    rows = pd.DataFrame(
        [
            {"event_index": 0, "event_type": 0, "timestamp_ms": 100, "action_id": 1038, "before_id": None, "after_id": None, "build_category": None},
            {"event_index": 1, "event_type": 0, "timestamp_ms": 150, "action_id": 2003, "before_id": None, "after_id": None, "build_category": 2},
            {"event_index": 2, "event_type": 0, "timestamp_ms": 200, "action_id": 6672, "before_id": None, "after_id": None, "build_category": 0},
            {"event_index": 3, "event_type": 2, "timestamp_ms": 201, "action_id": None, "before_id": 6672, "after_id": 0, "build_category": 0},
            {"event_index": 4, "event_type": 0, "timestamp_ms": 300, "action_id": 3031, "before_id": None, "after_id": None, "build_category": 0},
        ]
    )
    rows = rows.assign(
        match_id="match",
        participant_id=1,
        patch="26.14",
        region="NA1",
        won=True,
        champion_id=22,
        opponent_champion_id=51,
        role="BOTTOM",
        match_date=1,
    )

    assert undone_purchase_indexes(rows) == {2}
    decisions = build_item_decisions(rows)

    assert decisions["family"].tolist() == ["STARTER", "FIRST_ITEM_PATH", "ITEM"]
    assert decisions["action_key"].tolist() == ["2003", "2003+3031", "3031"]
    # The B.F. Sword component and every consumable are not build relevant, so only the starter
    # reaches the inventory state the model conditions on.
    assert decisions["inventory_ids"].tolist() == ["", "2003", "2003"]


def test_undoing_a_sale_only_restores_build_relevant_inventory():
    rows = pd.DataFrame(
        [
            {"event_index": 0, "event_type": 0, "timestamp_ms": 100, "action_id": 2003, "before_id": None, "after_id": None, "build_category": 2},
            {"event_index": 1, "event_type": 1, "timestamp_ms": 150, "action_id": 2003, "before_id": None, "after_id": None, "build_category": 2},
            # Undo of the starter sale restores it; undo of a consumable sale must not.
            {"event_index": 2, "event_type": 2, "timestamp_ms": 160, "action_id": None, "before_id": 0, "after_id": 2003, "build_category": 2},
            {"event_index": 3, "event_type": 2, "timestamp_ms": 170, "action_id": None, "before_id": 0, "after_id": 2055, "build_category": None},
            {"event_index": 4, "event_type": 0, "timestamp_ms": 300, "action_id": 3031, "before_id": None, "after_id": None, "build_category": 0},
        ]
    )
    rows = rows.assign(
        match_id="match",
        participant_id=1,
        patch="26.14",
        region="NA1",
        won=True,
        champion_id=22,
        opponent_champion_id=51,
        role="BOTTOM",
        match_date=1,
    )

    decisions = build_item_decisions(rows)
    item_row = decisions[decisions["family"] == "ITEM"].iloc[0]

    assert item_row["inventory_ids"] == "2003"


def test_event_state_excludes_events_at_or_after_the_purchase():
    decisions = pd.DataFrame(
        [
            {"match_id": "match", "participant_id": 1, "timestamp_ms": 1000},
            {"match_id": "match", "participant_id": 1, "timestamp_ms": 2000},
        ]
    )
    events = pd.DataFrame(
        [
            {"match_id": "match", "event_index": 0, "timestamp_ms": 999, "event_type": "CHAMPION_KILL", "killer_participant_id": "1", "killer_team_id": None, "owner_team_id": None},
            {"match_id": "match", "event_index": 1, "timestamp_ms": 1000, "event_type": "CHAMPION_KILL", "killer_participant_id": "6", "killer_team_id": None, "owner_team_id": None},
            {"match_id": "match", "event_index": 2, "timestamp_ms": 1500, "event_type": "ELITE_MONSTER_KILL", "killer_participant_id": "6", "killer_team_id": None, "owner_team_id": None},
        ]
    )
    teams = pd.DataFrame(
        [
            {"match_id": "match", "participant_id": 1, "team_id": 100},
            {"match_id": "match", "participant_id": 6, "team_id": 200},
        ]
    )

    enriched = enrich_with_predecision_event_state(decisions, events, teams)

    assert enriched["team_kill_diff"].tolist() == [1, 0]
    assert enriched["team_objective_diff"].tolist() == [0, -1]


def test_minion_destroyed_buildings_are_credited_to_the_conceding_team_s_opponent():
    decisions = pd.DataFrame(
        [{"match_id": "match", "participant_id": 1, "timestamp_ms": 2000}]
    )
    events = pd.DataFrame(
        [
            {
                "match_id": "match",
                "event_index": 0,
                "timestamp_ms": 1000,
                "event_type": "BUILDING_KILL",
                # Riot reports a minion-destroyed turret with killerId 0 and the owning team's id.
                # A minion-destroyed building: killerId 0 and only the OWNING team's id.
                "killer_participant_id": "0",
                "killer_team_id": None,
                "owner_team_id": "200",
            }
        ]
    )
    teams = pd.DataFrame([{"match_id": "match", "participant_id": 1, "team_id": 100}])

    enriched = enrich_with_predecision_event_state(decisions, events, teams)

    assert enriched["team_tower_diff"].tolist() == [1.0]


def test_payload_less_v2_matches_are_flagged_instead_of_reading_as_an_even_game():
    decisions = pd.DataFrame(
        [
            {"match_id": "covered", "participant_id": 1, "timestamp_ms": 2000},
            {"match_id": "payloadless", "participant_id": 1, "timestamp_ms": 2000},
        ]
    )
    events = pd.DataFrame(
        [
            {
                "match_id": "covered",
                "event_index": 0,
                "timestamp_ms": 1000,
                "event_type": "CHAMPION_KILL",
                "killer_participant_id": "1",
                "killer_team_id": None,
                "owner_team_id": None,
            }
        ]
    )
    teams = pd.DataFrame(
        [
            {"match_id": "covered", "participant_id": 1, "team_id": 100},
            {"match_id": "payloadless", "participant_id": 1, "team_id": 100},
        ]
    )

    enriched = enrich_with_predecision_event_state(decisions, events, teams)

    assert enriched["has_event_state"].tolist() == [1.0, 0.0]
    assert enriched["team_kill_diff"].tolist() == [1, 0]
    # With no payload rows at all the indicator, not the zeroed diffs, carries the absence.
    absent = enrich_with_predecision_event_state(decisions, events.iloc[:0], teams)
    assert absent["has_event_state"].tolist() == [0.0, 0.0]
    assert "has_event_state" in FEATURE_COLUMNS


def test_event_state_is_linear_in_matches_and_matches_the_scan_result():
    matches = 40
    decisions = pd.DataFrame(
        [
            {"match_id": f"match-{index}", "participant_id": 1, "timestamp_ms": 2000}
            for index in range(matches)
        ]
    )
    events = pd.DataFrame(
        [
            {
                "match_id": f"match-{index}",
                "event_index": 0,
                "timestamp_ms": 1000,
                "event_type": "BUILDING_KILL",
                "killer_participant_id": "1",
                "killer_team_id": None,
                "owner_team_id": None,
            }
            for index in range(matches)
        ]
    )
    teams = pd.DataFrame(
        [{"match_id": f"match-{index}", "participant_id": 1, "team_id": 100} for index in range(matches)]
    )

    enriched = enrich_with_predecision_event_state(decisions, events, teams)

    assert enriched["team_tower_diff"].tolist() == [1.0] * matches


def composition_frame(rows: int, seed: int = 11) -> pd.DataFrame:
    rng = np.random.default_rng(seed)
    return pd.DataFrame(
        {
            "champion_id": rng.integers(1, 170, rows),
            "opponent_champion_id": rng.integers(1, 170, rows),
            "role": rng.choice(["TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY"], rows),
            "patch": rng.choice(["26.12", "26.13", "26.14"], rows),
            "region": rng.choice(["NA1", "EUW1", "KR"], rows),
            "team_composition": [
                "-".join(str(value) for value in sorted(rng.choice(range(1, 170), 5, replace=False)))
                for _ in range(rows)
            ],
            "enemy_composition": [
                "-".join(str(value) for value in sorted(rng.choice(range(1, 170), 5, replace=False)))
                for _ in range(rows)
            ],
            "inventory_ids": [
                "-".join(str(value) for value in sorted(rng.choice(range(3000, 3200), 3, replace=False)))
                for _ in range(rows)
            ],
        }
    )


def test_design_matrix_width_is_bounded_independently_of_the_row_count():
    small = design_matrix(composition_frame(80))
    large = design_matrix(composition_frame(1600))

    assert small.shape[1] <= DESIGN_MATRIX_MAX_COLUMNS
    assert large.shape[1] <= DESIGN_MATRIX_MAX_COLUMNS
    # Twenty times the rows must not widen the matrix anywhere near proportionally: the old dense
    # one-hot over the composition strings produced roughly two singleton dummies per row.
    assert large.shape[1] < 2 * small.shape[1]


def test_design_matrix_encodes_compositions_as_multi_hot_champion_membership():
    frame = pd.DataFrame(
        [
            {"team_composition": "1-2-3-4-5", "enemy_composition": "6-7-8-9-10", "inventory_ids": "3006"},
            {"team_composition": "1-2-3-4-5", "enemy_composition": "11-12-13-14-15", "inventory_ids": ""},
        ]
    )
    spec = build_design_spec(frame)
    encoded = design_matrix(frame, spec)

    assert encoded["team_champion_1"].tolist() == [1.0, 1.0]
    assert encoded["enemy_champion_6"].tolist() == [1.0, 0.0]
    assert encoded["inventory_item_3006"].tolist() == [1.0, 0.0]


def test_design_matrix_reuses_a_fixed_spec_across_frames():
    spec = build_design_spec(composition_frame(200))
    other = design_matrix(composition_frame(50, seed=99), spec)

    assert list(other.columns) == list(spec.columns)


def test_balance_gate_passes_for_a_correctly_specified_propensity_with_sparse_dummies():
    rng = np.random.default_rng(3)
    size = 4000
    skill = rng.normal(size=size)
    propensity = 1 / (1 + np.exp(-0.8 * skill))
    treated = rng.binomial(1, propensity)
    # Real covariates plus the kind of near-singleton dummy the old dense one-hot produced.
    singletons = np.zeros((size, 200), dtype=float)
    singletons[np.arange(200), np.arange(200)] = 1.0
    covariates = np.column_stack([skill, 0.5 * skill + rng.normal(size=size), singletons])

    balance = maximum_weighted_smd(covariates, treated, propensity)

    assert balance < 0.10


def test_balance_gate_still_flags_a_genuinely_imbalanced_covariate():
    rng = np.random.default_rng(5)
    size = 2000
    skill = rng.normal(size=size)
    treated = rng.binomial(1, 1 / (1 + np.exp(-1.5 * skill)))
    # A flat propensity leaves the real confounder unbalanced and must fail the gate.
    balance = maximum_weighted_smd(skill.reshape(-1, 1), treated, np.full(size, 0.5))

    assert balance > 0.10


def test_clustered_standard_error_recovers_the_sqrt_k_understatement():
    rng = np.random.default_rng(13)
    clusters = 500
    repeats = 5
    values = rng.normal(size=clusters)
    influence = np.repeat(values, repeats)
    cluster_ids = np.repeat(np.arange(clusters).astype(str), repeats)
    independent_ids = np.arange(clusters * repeats).astype(str)
    weights = np.ones(clusters * repeats)
    estimate = float(influence.mean())

    clustered = clustered_standard_error(influence, weights, estimate, cluster_ids)
    naive = clustered_standard_error(influence, weights, estimate, independent_ids)

    assert clustered == pytest.approx(naive * np.sqrt(repeats), rel=0.02)


def test_clustered_estimate_reports_a_wider_interval_than_the_naive_duplicate_pool():
    rng = np.random.default_rng(21)
    participants = 900
    repeats = 4
    skill = rng.normal(size=participants)
    treated = rng.binomial(1, 1 / (1 + np.exp(-0.6 * skill)))
    won = rng.binomial(1, 1 / (1 + np.exp(-(-0.1 + 0.5 * skill + 0.5 * treated))))
    base = pd.DataFrame(
        {
            "action_key": np.where(treated == 1, "A", "B"),
            "won": won,
            "minute": 10 + skill,
            "gold": 5000 + 400 * skill,
            "team_gold_diff": 300 * skill,
            "match_date": np.arange(participants),
            "match_id": np.arange(participants).astype(str),
            "participant_id": 1,
        }
    )
    duplicated = pd.concat([base] * repeats, ignore_index=True)
    duplicated["match_date"] = np.tile(base["match_date"].to_numpy(), repeats)

    clustered = doubly_robust_binary(duplicated, "A")
    assert clustered is not None
    # Every duplicate of an untreated participant carries an identical influence value, so the row
    # count must not buy precision and must not inflate the observed count either.
    assert clustered["observed_count"] == int(treated.sum())

    independent = duplicated.copy()
    independent["match_id"] = np.arange(len(independent)).astype(str)
    independent["participant_id"] = np.arange(len(independent))
    naive = doubly_robust_binary(independent, "A")
    assert naive is not None
    assert clustered["standard_error"] > 1.5 * naive["standard_error"]


def test_path_comparator_never_places_a_participant_in_both_arms():
    scope = pd.DataFrame(
        [
            {"participant_key": "m1|1", "path_action": "1+2", "path_length": 2},
            {"participant_key": "m1|1", "path_action": "1+2+3", "path_length": 3},
            {"participant_key": "m1|2", "path_action": "1+2", "path_length": 2},
            {"participant_key": "m1|2", "path_action": "1+2+9", "path_length": 3},
            {"participant_key": "m2|1", "path_action": "4+5", "path_length": 2},
        ]
    )

    comparator = participant_level_comparator(scope, "1+2+3")

    assert sorted(comparator["participant_key"]) == ["m1|1", "m1|2", "m2|1"]
    assert sorted(comparator["action_key"]) == ["1+2+3", "1+2+9", "4+5"]
    assert comparator["participant_key"].is_unique


def test_partial_pooling_moves_cells_toward_the_parent_and_keeps_the_interval_consistent():
    records = [
        {"raw_estimate": 0.30, "standard_error": 0.10, "family": "ITEM", "stage": 1, "action_key": "A", "role": "TOP"},
        {"raw_estimate": 0.02, "standard_error": 0.01, "family": "ITEM", "stage": 1, "action_key": "A", "role": "TOP"},
        {"raw_estimate": 0.03, "standard_error": 0.01, "family": "ITEM", "stage": 1, "action_key": "A", "role": "TOP"},
        {"raw_estimate": 0.02, "standard_error": 0.01, "family": "ITEM", "stage": 1, "action_key": "A", "role": "MIDDLE"},
    ]

    apply_partial_pooling(records, [["family", "stage", "action_key"], ["family", "stage", "action_key", "role"]])

    noisy = records[0]
    precise = records[1]
    # The noisy cell is pulled toward its siblings, not merely scaled toward zero.
    assert 0.02 < noisy["estimate"] < 0.30
    assert abs(precise["estimate"] - 0.02) < 0.01
    for record in records:
        error = record["posterior_standard_error"]
        assert record["low"] == pytest.approx(record["estimate"] - 1.96 * error)
        assert record["high"] == pytest.approx(record["estimate"] + 1.96 * error)
        assert error <= record["standard_error"]


def test_partial_pooling_shrinks_an_isolated_cell_toward_no_lift():
    records = [{"raw_estimate": 0.40, "standard_error": 0.30, "path_hash": "h", "role": "TOP"}]

    apply_partial_pooling(records, [["path_hash"], ["path_hash", "role"]])

    assert 0 < records[0]["estimate"] < 0.40
    assert records[0]["posterior_standard_error"] < np.sqrt(GLOBAL_PRIOR_VARIANCE) + 0.30


def test_scope_expansion_emits_a_regional_row_without_an_opponent():
    frame = pd.DataFrame(
        [{"opponent_champion_id": 51, "region": "na1", "won": True}]
    )

    # The floor is off here so this stays a test of the expansion shape; the prune has its own.
    expanded = expand_scopes(frame, fine_scope_min_rows=0)
    scopes = set(zip(expanded["scope_opponent_id"], expanded["scope_region"]))

    assert (0, "GLOBAL") in scopes
    assert (51, "GLOBAL") in scopes
    assert (0, "NA1") in scopes
    assert (51, "NA1") in scopes


def test_the_fine_scope_floor_drops_only_scopes_that_could_never_publish():
    """A cell is a subset of its scope, so pruning a scope under the publication floor cannot
    remove a cell that would have cleared it -- and the global scope is never pruned."""
    big = pd.DataFrame(
        [
            {"champion_id": 1, "role": "MIDDLE", "match_id": f"m{n}",
             "opponent_champion_id": 51, "region": "na1", "won": n % 2 == 0}
            for n in range(120)
        ]
    )
    thin = pd.DataFrame(
        [
            {"champion_id": 1, "role": "MIDDLE", "match_id": f"t{n}",
             "opponent_champion_id": 99, "region": "kr", "won": True}
            for n in range(5)
        ]
    )
    frame = pd.concat([big, thin], ignore_index=True)

    expanded = expand_scopes(frame, fine_scope_min_rows=100)
    scopes = set(zip(expanded["scope_opponent_id"], expanded["scope_region"]))

    # The global scope survives regardless of how thin any matchup inside it is.
    assert (0, "GLOBAL") in scopes
    # 120 rows against opponent 51 clears the floor; 5 rows against opponent 99 cannot.
    assert (51, "GLOBAL") in scopes
    assert (99, "GLOBAL") not in scopes
    assert (99, "KR") not in scopes
    # Every row is still represented at the global scope -- pruning drops scopes, never rows.
    assert int((expanded["scope_opponent_id"] == 0).sum()) >= len(frame)


def test_leakage_check_reports_a_measured_failure():
    passing = evaluate_leakage(
        {"train": ["a", "b"], "calibration": ["c"], "test": ["d"]},
        ["minute", "gold", "champion_7"],
    )
    assert passing["passed"] is True

    shared = evaluate_leakage(
        {"train": ["a", "b"], "calibration": ["b"], "test": ["d"]},
        ["minute"],
    )
    assert shared["passed"] is False
    assert shared["splitMatchOverlaps"]["train|calibration"]["count"] == 1

    leaked = evaluate_leakage(
        {"train": ["a"], "calibration": ["c"], "test": ["d"]},
        ["minute", "won"],
    )
    assert leaked["passed"] is False
    assert leaked["postOutcomeFeatureColumns"] == ["won"]


def test_zero_weight_rows_from_a_fourth_patch_are_dropped_before_counting():
    decisions = pd.DataFrame(
        {
            "patch": ["26.14", "26.13", "26.12", "26.11"],
            "won": [True, False, True, False],
        }
    )

    weighted = apply_row_weights(decisions, "26.14", ["26.14", "26.13", "26.12", "26.11"], None)

    assert weighted["patch"].tolist() == ["26.14", "26.13", "26.12"]
    assert weighted["patch_weight"].tolist() == [1.0, 0.60, 0.35]


def synthetic_decisions(
    participants: int = 400,
    seed: int = 31,
    include_first_item_path: bool = False,
) -> pd.DataFrame:
    rng = np.random.default_rng(seed)
    rows = []
    for index in range(participants):
        skill = float(rng.normal())
        picks_core = rng.random() < 1 / (1 + np.exp(-0.5 * skill))
        won = bool(rng.binomial(1, 1 / (1 + np.exp(-(0.4 * skill + 0.5 * picks_core)))))
        patch = ["26.14", "26.13"][index % 2]
        core_id = 6672 if picks_core else 3031
        # (family, stage, action, frame): the first-item-path row shares the frame of the legendary
        # purchase that produced it, exactly as build_item_decisions emits it.
        staged = [("STARTER", 0, 1055, 0), ("ITEM", 1, core_id, 1), ("ITEM", 2, 3036, 2)]
        if include_first_item_path:
            staged.append(("FIRST_ITEM_PATH", 0, core_id, 1))
        for family, stage, action_id, frame in staged:
            rows.append(
                {
                    "match_id": f"match-{index}",
                    "participant_id": 1,
                    # A team in a match is the independent unit the calibration null resamples: every
                    # row of one team shares its single outcome.
                    "team_id": 100 if index % 2 == 0 else 200,
                    "match_date": index,
                    "patch": patch,
                    "region": "NA1",
                    "champion_id": 22,
                    "opponent_champion_id": 51,
                    "role": "BOTTOM",
                    "won": won,
                    "family": family,
                    "stage": stage,
                    "path_prefix": "[]",
                    "path_prefix_hash": hash_path([]),
                    "action_ids": f"[{action_id}]",
                    "action_key": str(action_id),
                    "inventory_ids": "",
                    "team_composition": "22-64-103-201-517",
                    "enemy_composition": "51-59-134-412-875",
                    "has_predecision_state": 1.0,
                    "minute": float(frame * 8),
                    # Deliberately offset from the floored frame minute so a published timing taken
                    # from the frame instead of the purchase event fails.
                    "decision_minute": float(frame * 8) + 0.75,
                    "gold": 1000.0 + 900 * frame + 200 * skill,
                    "current_gold": 100.0,
                    "xp": 500.0 * (frame + 1),
                    "cs": 20.0 * (frame + 1),
                    "lane_cs": 18.0 * (frame + 1),
                    "jungle_cs": 1.0,
                    "level": 3.0 + 3 * frame,
                    "team_gold_diff": 300 * skill,
                    "team_xp_diff": 200 * skill,
                    "team_cs_diff": 8 * skill,
                    "team_kill_diff": float(rng.integers(-4, 5)),
                    "team_tower_diff": 0.0,
                    "team_objective_diff": 0.0,
                }
            )
    return apply_row_weights(pd.DataFrame(rows), "26.14", ["26.14", "26.13"], None)


def test_structural_model_scores_every_row_and_reports_a_measured_leakage_result():
    decisions = synthetic_decisions()

    bundle, metrics = train_structural_model(decisions, max_training_rows=100_000)

    assert metrics["leakageCheckPassed"] is True
    assert metrics["leakageDetail"]["splitMatchOverlaps"] == {}
    assert metrics["designColumnCount"] <= DESIGN_MATRIX_MAX_COLUMNS

    scored = structural_win_probability(bundle, decisions, chunk_rows=137)
    assert len(scored) == len(decisions)
    assert scored.min() > 0 and scored.max() < 1


def test_action_and_path_estimates_publish_intervals_around_the_published_point():
    decisions = synthetic_decisions()
    bundle, _ = train_structural_model(decisions, max_training_rows=100_000)
    decisions["baseline_win_probability"] = structural_win_probability(bundle, decisions)
    generation_id = uuid4()

    estimates = build_action_estimates(decisions, generation_id, "26.14")
    paths = build_path_estimates(decisions, generation_id, "26.14")

    assert estimates
    for row in estimates:
        estimate, low, high, observed = row[13], row[14], row[15], row[18]
        assert low < estimate < high
        assert observed <= decisions["match_id"].nunique()
    assert paths
    for row in paths:
        probability, lift, low, high = row[9], row[10], row[11], row[12]
        assert 0.0 <= probability <= 1.0
        assert low < lift < high


def test_post_match_rank_observations_are_down_weighted_and_recorded():
    decisions = pd.DataFrame(
        {
            "patch": ["26.14", "26.14"],
            "won": [True, False],
            "rank_observation_offset_seconds": [-3600, 3600],
        }
    )

    weighted = apply_row_weights(
        decisions, "26.14", ["26.14"], "ObservationOffsetSeconds"
    )

    assert weighted["patch_weight"].tolist() == [1.0, 0.25]
    assert weighted["rank_observation_is_post_match"].tolist() == [0.0, 1.0]


def test_unsigned_legacy_rank_offsets_keep_the_cohort_at_full_weight():
    decisions = pd.DataFrame(
        {
            "patch": ["26.14", "26.14"],
            "won": [True, False],
            # Rows written under the old unsigned semantics cannot be classified, so nothing may be
            # discounted or dropped on their account until re-ingestion.
            "rank_observation_offset_seconds": [3600, 7200],
        }
    )

    weighted = apply_row_weights(
        decisions, "26.14", ["26.14"], "ObservationDistanceSeconds"
    )

    assert weighted["patch_weight"].tolist() == [1.0, 1.0]
    assert weighted["rank_observation_is_post_match"].tolist() == [0.0, 0.0]


def test_rank_lateral_prefers_the_nearest_pre_match_observation():
    sql = rank_context_lateral("ObservationOffsetSeconds")

    # A post-match reading sorts last (the boolean is the first sort key) and only one row is taken,
    # so a post-match rank is used only when the participant has no pre-match reading at all.
    assert 'ORDER BY (COALESCE(rank_row."ObservationOffsetSeconds", 0) > 0)' in sql
    assert "LIMIT 1" in sql
    # Neither column exists yet: the cohort filter still works, with the offset simply unknown.
    assert '"ObservationOffsetSeconds"' not in rank_context_lateral(None)


def test_every_timeline_dependent_loader_filters_the_schema_version():
    for loader in (
        load_item_events,
        timeline_state_events_query,
        load_rune_decisions,
        load_spell_decisions,
    ):
        source = inspect.getsource(loader)
        assert '"SchemaVersion" >= %(schema_version)s' in source, loader.__name__
        assert 'timeline."Status" = 1' in source, loader.__name__


def test_lane_opponent_join_cannot_duplicate_a_decision_row():
    for loader in (load_item_events, load_rune_decisions, load_spell_decisions):
        source = inspect.getsource(loader)
        opponent_join = source.split("LEFT JOIN LATERAL")[1]
        assert "LIMIT 1" in opponent_join.split(") opponent")[0], loader.__name__


def test_pregame_decisions_carry_no_in_game_state_but_keep_pregame_conditioning():
    rune_rows = pd.DataFrame(
        [
            {
                "match_id": "match",
                "participant_id": 1,
                "match_date": 1,
                "patch": "26.14",
                "region": "NA1",
                "champion_id": 22,
                "opponent_champion_id": 51,
                "role": "BOTTOM",
                "won": True,
                "action_id": 8005 + index,
                "selection_tree": 0,
                "selection_index": index,
                "team_composition": "22-64-103-201-517",
                "enemy_composition": "51-59-134-412-875",
            }
            for index in range(3)
        ]
    )
    spell_rows = rune_rows.iloc[:1].assign(spell_1=4, spell_2=7)

    runes = build_rune_decisions(rune_rows)
    spells = build_spell_decisions(spell_rows)

    for frame in (runes, spells):
        assert (frame["has_predecision_state"] == 0.0).all()
        assert (frame["has_event_state"] == 0.0).all()
        assert (frame["inventory_ids"] == "").all()
        assert (frame["gold"] == 0.0).all()
        # The pregame state that does exist is still carried into the design matrix.
        encoded = design_matrix(frame)
        assert encoded["champion_22"].tolist() == [1.0] * len(frame)
        assert encoded["opponent_51"].tolist() == [1.0] * len(frame)
        assert encoded["enemy_champion_51"].tolist() == [1.0] * len(frame)
        assert encoded["patch_26.14"].tolist() == [1.0] * len(frame)
        assert encoded["region_NA1"].tolist() == [1.0] * len(frame)
    assert runes["family"].tolist() == ["RUNE_PAGE", "RUNE", "RUNE", "RUNE"]
    assert spells["action_key"].tolist() == ["4+7"]


def test_precomputed_design_matches_the_matrix_the_estimator_would_build():
    frame = synthetic_decisions(participants=200, seed=5)
    group = frame[frame["stage"] == 1]
    design = design_matrix(group).to_numpy(dtype=float)

    hoisted = doubly_robust_binary(group, "6672", design)
    internal = doubly_robust_binary(group, "6672")

    assert hoisted is not None and internal is not None
    assert hoisted["estimate"] == pytest.approx(internal["estimate"])
    assert hoisted["standard_error"] == pytest.approx(internal["standard_error"])


def test_action_estimates_describe_the_baseline_they_were_measured_against():
    decisions = synthetic_decisions()
    estimates = build_action_estimates(decisions, uuid4(), "26.14")

    assert estimates
    placeholders = inspect.getsource(insert_estimates).count("%s")
    for row in estimates:
        # One placeholder per tuple element; ComputedAtUtc is the only literal in the statement.
        assert len(row) == placeholders
        baseline = row[27]
        assert isinstance(baseline, str) and 0 < len(baseline) <= 256
        assert "alternative" in baseline
    assert 'BaselineDefinition' in inspect.getsource(insert_estimates)


def test_direction_stability_rejects_a_fold_that_points_the_other_way():
    influence = np.concatenate([np.full(200, 0.4), np.full(200, -0.4)])
    clusters = np.arange(400).astype(str)
    folds = np.concatenate([np.zeros(200, dtype=int), np.ones(200, dtype=int)])

    assert direction_is_stable(np.full(400, 0.4), folds, clusters, 0.4) is True
    assert direction_is_stable(influence, folds, clusters, float(influence.mean())) is False


def test_surrogate_ids_depend_on_the_secret_salt_and_drop_the_real_ids():
    decisions = pd.DataFrame(
        {
            "match_id": ["match-a", "match-b"],
            "participant_id": [1, 2],
            "participant_row_id": [10, 11],
            "won": [True, False],
        }
    )

    export = deidentified_export(decisions, "a" * 32)
    other = deidentified_export(decisions, "b" * 32)

    assert "match_id" not in export.columns
    assert "participant_id" not in export.columns
    assert "participant_row_id" not in export.columns
    assert export["match_surrogate"].is_unique
    # A salt published beside the artifact would make every surrogate re-derivable.
    assert export["match_surrogate"].tolist() != other["match_surrogate"].tolist()


def test_settings_fail_closed_without_a_secret_deidentification_salt(monkeypatch):
    monkeypatch.setenv("BUILD_LAB_DATABASE_URL", "postgresql://localhost/test")
    monkeypatch.delenv("BUILD_LAB_DEIDENTIFICATION_SALT", raising=False)
    with pytest.raises(RuntimeError, match="BUILD_LAB_DEIDENTIFICATION_SALT"):
        Settings.from_env()

    monkeypatch.setenv("BUILD_LAB_DEIDENTIFICATION_SALT", "too-short")
    with pytest.raises(RuntimeError, match="32 characters"):
        Settings.from_env()

    monkeypatch.setenv("BUILD_LAB_DEIDENTIFICATION_SALT", "s" * 32)
    settings = Settings.from_env()
    assert settings.lease_owner


def test_execute_guarded_treats_a_zero_rowcount_as_a_lost_lease():
    held = FakeConnection([FakeCursor(1)])
    execute_guarded(held, 'UPDATE "BuildLabGenerations" SET "Status" = 2', (1,), "lease is gone")

    reclaimed = FakeConnection([FakeCursor(0)])
    with pytest.raises(LeaseLost, match="lease is gone"):
        execute_guarded(
            reclaimed, 'UPDATE "BuildLabGenerations" SET "Status" = 2', (1,), "lease is gone"
        )


def test_every_guarded_status_write_checks_its_rowcount():
    for function in (pipeline.lease_generation, mark_failed):
        assert ".rowcount" in inspect.getsource(function), function.__name__
    # The terminal write delegates its rowcount check, so the check lives in one place.
    assert "execute_guarded(" in inspect.getsource(model_generation)
    assert "rowcount == 0" in inspect.getsource(execute_guarded)


def publishing_generation(monkeypatch, tmp_path) -> tuple[Settings, dict]:
    """A generation whose heavy modeling stages are stubbed so the publish path can be driven."""
    settings = modeler_settings(monkeypatch, BUILD_LAB_ARTIFACT_DIR=str(tmp_path))
    item_events = pd.DataFrame(
        [
            {
                "event_index": 0,
                "event_type": 0,
                "timestamp_ms": 525_000,
                "action_id": 6672,
                "before_id": None,
                "after_id": None,
                "build_category": 0,
                "match_id": f"match-{index}",
                "participant_id": 1,
                "match_date": index,
                "patch": "26.14",
                "region": "NA1",
                "champion_id": 22,
                "opponent_champion_id": 51,
                "role": "BOTTOM",
                "won": bool(index % 2),
                "minute": 8.0,
            }
            for index in range(4)
        ]
    )
    empty = pd.DataFrame()
    monkeypatch.setattr(pipeline, "resolve_rank_offset_column", lambda *_: None)
    monkeypatch.setattr(pipeline, "load_item_events", lambda *_, **__: item_events)
    monkeypatch.setattr(pipeline, "load_timeline_state_events", lambda *_, **__: empty)
    monkeypatch.setattr(pipeline, "load_participant_teams", lambda *_, **__: empty)
    monkeypatch.setattr(pipeline, "load_rune_decisions", lambda *_, **__: empty)
    monkeypatch.setattr(pipeline, "load_spell_decisions", lambda *_, **__: empty)
    monkeypatch.setattr(pipeline, "load_cohort_match_count", lambda *_: 40_000)
    # One worker: the publish path is what these tests drive, and a process pool would need a real
    # database. The parallel path is covered separately against the sequential one.
    monkeypatch.setenv("BUILD_LAB_SWEEP_WORKERS", "1")
    monkeypatch.setattr(pipeline, "load_cohort_champions", lambda *_: [22])
    monkeypatch.setattr(
        pipeline,
        "train_structural_model",
        lambda *_: ({"model": None, "calibrator": None}, {"overallEce": 0.01}),
    )
    monkeypatch.setattr(
        pipeline, "structural_win_probability", lambda bundle, frame: np.full(len(frame), 0.5)
    )
    # Pooling has its own tests; here it would only stand between the stubbed records and the publish
    # path this fixture exists to drive.
    monkeypatch.setattr(pipeline, "apply_partial_pooling", lambda *_: None)
    monkeypatch.setattr(pipeline, "action_records", lambda *_: [{"champion_id": 22}])
    monkeypatch.setattr(pipeline, "path_records", lambda *_: [])
    monkeypatch.setattr(pipeline, "action_tuples", lambda *_: [(uuid4(), "estimate")])
    monkeypatch.setattr(pipeline, "path_tuples", lambda *_: [])
    generation = {
        "Id": uuid4(),
        "Patch": "26.14",
        "DatasetVersion": "build-lab-v1",
        "IncludedPatchesJson": ["26.14"],
        "IncludedRegionsJson": ["NA1"],
        "SourceCutoffUtc": datetime(2026, 7, 1, tzinfo=timezone.utc),
    }
    return settings, generation


def test_a_lost_lease_at_the_terminal_write_rolls_the_estimates_back(
    tmp_path,
    monkeypatch,
    caplog,
):
    settings, generation = publishing_generation(monkeypatch, tmp_path)
    # The reaper already failed this generation, so the guarded terminal write matches no row.
    connection = FakeConnection(rowcounts={'SET "Status" = 2': 0})

    with caplog.at_level(logging.INFO):
        with pytest.raises(LeaseLost):
            model_generation(connection, generation, settings)

    attempted = [row for row in connection.statements if "AdjustedActionEstimates" in row[0]]
    survived = [row for row in connection.committed if "AdjustedActionEstimates" in row[0]]
    assert attempted and survived == []
    assert connection.rollbacks == 1
    assert connection.commits == 0
    assert "ready for .NET promotion" not in caplog.text


def test_a_held_lease_publishes_the_estimates_with_the_terminal_write(tmp_path, monkeypatch):
    settings, generation = publishing_generation(monkeypatch, tmp_path)
    connection = FakeConnection()

    model_generation(connection, generation, settings)

    committed = [statement for statement, _ in connection.committed]
    assert any("AdjustedActionEstimates" in statement for statement in committed)
    terminal = next(
        parameters for statement, parameters in connection.committed if 'SET "Status" = 2' in statement
    )
    # Only the owner named in the guard may publish, and the run's transaction is committed before the
    # success is logged.
    assert terminal[-1] == settings.lease_owner
    assert connection.rollbacks == 0
    assert connection.commits == 1
    assert (tmp_path / str(generation["Id"]) / "manifest.json").is_file()
    # Every run prunes unretained bundles before it writes its own, or the volume grows forever.
    assert any(
        '"IsActive" OR "Status" IN (0, 1, 2, 3)' in statement
        for statement, _ in connection.statements
    )


def test_a_reclaimed_generation_publishes_no_artifacts_and_no_estimates(tmp_path, monkeypatch):
    # The terminal status write is guarded on the status it expects, so a row the coordinator moved
    # out from under this run aborts before anything is published.
    settings, generation = publishing_generation(monkeypatch, tmp_path)
    # The coordinator reclaimed the row, so the guarded terminal write matches nothing.
    connection = FakeConnection(rowcounts={'SET "Status" = 2': 0})

    with pytest.raises(LeaseLost):
        model_generation(connection, generation, settings)

    # Estimates may be staged, but the guarded terminal write aborts the transaction, so nothing
    # a reclaimed run produced is ever committed.
    assert [row for row in connection.committed if "AdjustedActionEstimates" in row[0]] == []
    assert connection.rollbacks > 0


def test_mark_failed_only_fails_a_generation_this_worker_still_owns(caplog):
    owned = FakeConnection([FakeCursor(1)])
    mark_failed(owned, "generation", "boom", "owner-a")

    statement, parameters = owned.statements[-1]
    # Guarded to Modeling under this owner and never to a promoted row: an active generation stamped
    # Failed is a state the serving layer rejects and RollbackAsync cannot repair.
    assert '"Status" = 1' in statement
    assert '"LeaseOwner" = %s' in statement
    assert 'NOT "IsActive"' in statement
    assert parameters == ("boom", "generation", "owner-a")
    assert owned.commits == 1

    reclaimed = FakeConnection([FakeCursor(0)])
    with caplog.at_level(logging.WARNING):
        mark_failed(reclaimed, "generation", "boom", "owner-b")
    assert "was not marked failed" in caplog.text


def test_artifact_retention_mirrors_the_coordinator_retention_window(monkeypatch):
    # BuildLabModelingOptions.RetainedGenerations and the coordinator's Math.Max(2, ...) floor.
    assert modeler_settings(monkeypatch).retained_generations == 4
    assert (
        modeler_settings(monkeypatch, BUILD_LAB_RETAINED_GENERATIONS="1").retained_generations == 2
    )

    connection = FakeConnection()
    retained_generation_ids(connection, 1)
    statement, parameters = connection.statements[-1]

    assert parameters == (2,)
    assert '"Status" IN (3, 5)' in statement
    assert (
        'ORDER BY ("PromotedAtUtc" IS NOT NULL) DESC, "PromotedAtUtc" DESC, "CreatedAtUtc" DESC'
        in statement
    )
    # Active, in-flight and still-promotable generations are retained whatever their age.
    assert '"IsActive" OR "Status" IN (0, 1, 2, 3)' in statement


def test_pruning_removes_only_the_artifacts_of_unretained_generations(tmp_path, monkeypatch):
    settings = modeler_settings(monkeypatch, BUILD_LAB_ARTIFACT_DIR=str(tmp_path))
    current, retained, unretained = uuid4(), uuid4(), uuid4()
    for generation_id in (current, retained, unretained):
        (tmp_path / str(generation_id) / "dataset").mkdir(parents=True)
        (tmp_path / str(generation_id) / "manifest.json").write_text("{}", encoding="utf-8")
    (tmp_path / "lost+found").mkdir()
    (tmp_path / "unrelated.txt").write_text("keep", encoding="utf-8")
    connection = FakeConnection(
        rows={'FROM "BuildLabGenerations"': [{"generation_id": str(retained)}]}
    )

    prune_stale_artifacts(connection, settings, current)

    assert not (tmp_path / str(unretained)).exists()
    assert (tmp_path / str(current) / "manifest.json").is_file()
    assert (tmp_path / str(retained) / "manifest.json").is_file()
    # Nothing this pipeline did not write is ever removed from the volume.
    assert (tmp_path / "lost+found").is_dir()
    assert (tmp_path / "unrelated.txt").is_file()


def test_a_failed_retention_query_never_fails_the_generation(tmp_path, monkeypatch, caplog):
    settings = modeler_settings(monkeypatch, BUILD_LAB_ARTIFACT_DIR=str(tmp_path))
    orphan = uuid4()
    (tmp_path / str(orphan)).mkdir()
    connection = FakeConnection(fail_on='FROM "BuildLabGenerations"')

    with caplog.at_level(logging.WARNING):
        prune_stale_artifacts(connection, settings, uuid4())

    assert (tmp_path / str(orphan)).is_dir()
    # The aborted transaction is rolled back, because the rest of the run still needs the connection.
    assert connection.rollbacks == 1
    assert "nothing was pruned" in caplog.text


def test_published_timing_comes_from_the_purchase_event_not_the_timeline_frame():
    rows = pd.DataFrame(
        [
            {"event_index": 0, "event_type": 0, "timestamp_ms": 15_000, "action_id": 1055, "before_id": None, "after_id": None, "build_category": 2},
            {"event_index": 1, "event_type": 0, "timestamp_ms": 525_000, "action_id": 6672, "before_id": None, "after_id": None, "build_category": 0},
        ]
    )
    # The conditioning frame is floored to the frame cadence; the purchase happened 45s into it.
    rows = rows.assign(
        match_id="match",
        participant_id=1,
        patch="26.14",
        region="NA1",
        won=True,
        champion_id=22,
        opponent_champion_id=51,
        role="BOTTOM",
        match_date=1,
        minute=8.0,
    )

    decisions = build_item_decisions(rows)
    by_family = decisions.set_index("family")["decision_minute"]

    assert by_family["ITEM"] == pytest.approx(8.75)
    assert by_family["STARTER"] == pytest.approx(0.25)
    for family in ("STARTER", "ITEM", "FIRST_ITEM_PATH"):
        selected = decisions[decisions["family"] == family]
        assert average_timing(selected, family) == pytest.approx(
            selected["decision_minute"].iloc[0]
        )
        assert average_timing(selected, family) != selected["minute"].iloc[0]
    # A pregame decision has no timing to publish at all.
    assert average_timing(decisions, "RUNE") is None


def test_first_item_path_estimates_publish_the_timing_the_champion_page_renders():
    decisions = synthetic_decisions(include_first_item_path=True)

    estimates = build_action_estimates(decisions, uuid4(), "26.14")
    timings = {row[7]: row[20] for row in estimates}

    assert "FIRST_ITEM_PATH" in TIMED_FAMILIES
    # Without this family the champion page's first-item card renders "—" forever.
    assert timings["FIRST_ITEM_PATH"] == pytest.approx(8.75)
    assert all(timing is not None for timing in timings.values())


def test_dockerfile_layers_cover_every_declared_dependency():
    root = Path(__file__).resolve().parents[1]
    dockerfile = (root / "Dockerfile").read_text(encoding="utf-8")
    pyproject = (root / "pyproject.toml").read_text(encoding="utf-8")

    declared = re.findall(r'^\s*"([A-Za-z0-9_.-]+)(?:\[[^\]]*\])?[<>=!~]', pyproject, re.MULTILINE)
    installed = "\n".join(
        line for line in dockerfile.splitlines() if line.startswith("RUN pip install")
    )
    # The project is installed with --no-deps, so anything missing from the split layers would be
    # absent at runtime rather than merely mis-layered.
    assert declared, "no dependencies parsed out of pyproject.toml"
    missing = [name for name in declared if name not in installed]
    assert not missing, f"pyproject deps absent from the Dockerfile install layers: {missing}"
    assert "--no-deps" in dockerfile

    # One oversized layer is what broke `docker pull` on the containerd image store: keep the
    # scientific stack spread across separate RUN layers.
    dependency_layers = [
        line for line in dockerfile.splitlines()
        if line.startswith("RUN pip install") and "--no-deps" not in line
    ]
    assert len(dependency_layers) >= 6, dependency_layers
    for package in ("numpy", "pandas", "scikit-learn", "pyarrow", "scipy", "psycopg", "boto3"):
        owning = [line for line in dependency_layers if package in line]
        assert len(owning) == 1, f"{package} must be installed in exactly one layer: {owning}"

    # scipy earns its own layer twice over: it is a direct import rather than merely
    # scikit-learn's transitive dependency, and it was the bulk of what made that layer ~153 MB.
    assert "from scipy" in (root / "src" / "build_lab_modeler" / "pipeline.py").read_text(
        encoding="utf-8"
    ), "scipy is declared and layered on the premise that it is imported directly"
    scipy_layer = next(line for line in dependency_layers if "scipy" in line)
    assert "scikit-learn" not in scipy_layer, "scipy must not share scikit-learn's layer again"


def test_the_image_prepares_the_artifact_volume_for_the_runtime_uid(monkeypatch):
    settings = modeler_settings(monkeypatch)
    dockerfile = (Path(__file__).resolve().parents[1] / "Dockerfile").read_text(encoding="utf-8")

    assert f"mkdir -p {settings.artifact_dir}" in dockerfile
    assert f"chown 65532:65532 {settings.artifact_dir}" in dockerfile
    # Docker seeds a named volume from the image, so the chown must precede the unprivileged USER.
    assert dockerfile.index("chown 65532:65532") < dockerfile.index("USER 65532:65532")

    compose = Path(__file__).resolve().parents[3] / "compose.yml"
    if compose.is_file():
        deployed = compose.read_text(encoding="utf-8")
        assert f"BUILD_LAB_ARTIFACT_DIR: {settings.artifact_dir}" in deployed
        assert f"build_lab_artifacts:{settings.artifact_dir}" in deployed
    assert ":" in settings.lease_owner


def _operational_error(monkeypatch):
    """The real driver's error class, or a stand-in when psycopg is an import-only stub."""
    existing = getattr(pipeline.psycopg, "OperationalError", None)
    if isinstance(existing, type) and issubclass(existing, BaseException):
        return existing

    class OperationalError(Exception):
        pass

    monkeypatch.setattr(pipeline.psycopg, "OperationalError", OperationalError, raising=False)
    return OperationalError


def test_run_retries_an_unreachable_database_instead_of_crash_looping(monkeypatch, tmp_path, caplog):
    settings = modeler_settings(
        monkeypatch, BUILD_LAB_POLL_SECONDS="1", BUILD_LAB_ARTIFACT_DIR=str(tmp_path)
    )
    operational_error = _operational_error(monkeypatch)
    attempts = {"count": 0}

    def fail_then_stop(_settings):
        attempts["count"] += 1
        if attempts["count"] == 1:
            raise operational_error("failed to resolve host 'postgres'")
        raise pipeline.ShutdownRequested("stop")

    sleeps: list[float] = []
    monkeypatch.setattr(type(settings), "from_env", staticmethod(lambda: settings))
    monkeypatch.setattr(pipeline, "process_next", fail_then_stop)
    monkeypatch.setattr(pipeline, "install_shutdown_handlers", lambda: None)
    monkeypatch.setattr(pipeline.time, "sleep", sleeps.append)

    with caplog.at_level(logging.WARNING):
        pipeline.run()

    # A Postgres bounce must cost one polling cycle, not the container.
    assert attempts["count"] == 2
    assert sleeps == [settings.poll_seconds]
    assert any("could not reach the database" in record.message for record in caplog.records)


def test_run_once_still_surfaces_an_unreachable_database(monkeypatch, tmp_path):
    settings = modeler_settings(
        monkeypatch, BUILD_LAB_RUN_ONCE="true", BUILD_LAB_ARTIFACT_DIR=str(tmp_path)
    )
    operational_error = _operational_error(monkeypatch)

    def always_fail(_settings):
        raise operational_error("down")

    monkeypatch.setattr(type(settings), "from_env", staticmethod(lambda: settings))
    monkeypatch.setattr(pipeline, "process_next", always_fail)
    monkeypatch.setattr(pipeline, "install_shutdown_handlers", lambda: None)

    # A one-shot invocation (CI / manual) must fail loudly rather than swallow the outage.
    with pytest.raises(operational_error):
        pipeline.run()


# --- Phase A: adaptive per-cell borrowing -------------------------------------------------


def _borrow_frame(current_win_rate: float, prior_win_rate: float, n: int = 400, champion: int = 103):
    """One cell observed on both the current and the prior patch, with controllable disagreement."""
    rows = []
    for patch, rate in (("16.15", current_win_rate), ("16.14", prior_win_rate)):
        wins = int(round(rate * n))
        for index in range(n):
            rows.append(
                {
                    "patch": patch,
                    "champion_id": champion,
                    "role": "MIDDLE",
                    "family": "ITEM",
                    "stage": 1,
                    "action_key": "item:3157",
                    "action_ids": json.dumps([3157]),
                    "won": 1 if index < wins else 0,
                }
            )
    return pd.DataFrame(rows)


NO_CHANGES = PatchChangeSet(items=frozenset(), runes=frozenset(), champions=frozenset())


def test_borrowing_stays_strong_when_the_prior_patch_agrees():
    frame = _borrow_frame(0.50, 0.50)

    weights = commensurability_weights(frame, "16.15", NO_CHANGES)

    current = weights[frame["patch"] == "16.15"]
    prior = weights[frame["patch"] == "16.14"]
    assert (current == 1.0).all(), "current-patch rows are never discounted"
    assert prior.min() > 0.95, "an agreeing prior patch should borrow at close to full strength"


def test_borrowing_collapses_across_a_meta_break():
    # 50% -> 70% on 400 observations a side is a ~6 sigma split: the cell is not the same cell.
    frame = _borrow_frame(0.50, 0.70)

    weights = commensurability_weights(frame, "16.15", NO_CHANGES)

    assert weights[frame["patch"] == "16.14"].max() < 0.05
    assert (weights[frame["patch"] == "16.15"] == 1.0).all()


def test_borrowing_decays_monotonically_with_disagreement():
    previous = 1.1
    for prior_rate in (0.50, 0.54, 0.58, 0.62, 0.70):
        weight = commensurability_weights(_borrow_frame(0.50, prior_rate), "16.15", NO_CHANGES)
        current = weight[_borrow_frame(0.50, prior_rate)["patch"] == "16.14"].max()
        assert current < previous, f"weight must fall as disagreement grows (at {prior_rate})"
        previous = current


def test_a_thin_prior_cell_is_not_discarded_for_noise():
    # Same 20-point gap, but on 12 observations it is well inside noise, so it must not be treated
    # as a meta break the way the 400-observation version is.
    thin = commensurability_weights(_borrow_frame(0.50, 0.70, n=12), "16.15", NO_CHANGES)
    thick = commensurability_weights(_borrow_frame(0.50, 0.70, n=400), "16.15", NO_CHANGES)

    assert thin[_borrow_frame(0.50, 0.70, n=12)["patch"] == "16.14"].max() > 0.5
    assert thick[_borrow_frame(0.50, 0.70, n=400)["patch"] == "16.14"].max() < 0.05


@pytest.mark.parametrize(
    "changes,reason",
    [
        (PatchChangeSet(frozenset({3157}), frozenset(), frozenset()), "item was rebalanced"),
        (PatchChangeSet(frozenset(), frozenset(), frozenset({103})), "champion was rebalanced"),
        (PatchChangeSet(frozenset(), frozenset({3157}), frozenset()), "rune in the action changed"),
    ],
)
def test_a_static_change_hard_excludes_the_prior_patch(changes, reason):
    # Perfect agreement: only the static change set can be what zeroes these rows.
    frame = _borrow_frame(0.50, 0.50)

    weights = commensurability_weights(frame, "16.15", changes)

    assert (weights[frame["patch"] == "16.14"] == 0.0).all(), reason
    assert (weights[frame["patch"] == "16.15"] == 1.0).all(), "current patch is never excluded"


def test_a_static_change_to_another_champion_does_not_block_this_one():
    frame = _borrow_frame(0.50, 0.50, champion=103)
    changes = PatchChangeSet(frozenset(), frozenset(), frozenset({64}))

    weights = commensurability_weights(frame, "16.15", changes)

    assert weights[frame["patch"] == "16.14"].min() > 0.95


def test_a_prior_cell_with_no_current_counterpart_is_admitted_cautiously():
    frame = _borrow_frame(0.50, 0.50)
    frame = frame.loc[frame["patch"] == "16.14"].reset_index(drop=True)

    weights = commensurability_weights(frame, "16.15", NO_CHANGES)

    assert weights.max() == pytest.approx(UNVERIFIED_BORROW_WEIGHT)


def test_adaptive_weights_flow_into_the_row_weights_and_drop_zeroed_rows():
    frame = _borrow_frame(0.50, 0.50)
    changes = PatchChangeSet(frozenset({3157}), frozenset(), frozenset())

    weighted = apply_row_weights(frame, "16.15", ["16.15", "16.14"], None, changes)

    assert set(weighted["patch"]) == {"16.15"}, "hard-excluded rows carry zero weight and are dropped"
    assert (weighted["patch_weight"] == 1.0).all()


def test_row_weights_are_unchanged_when_no_change_set_is_supplied():
    frame = _borrow_frame(0.50, 0.50)

    weighted = apply_row_weights(frame, "16.15", ["16.15", "16.14"], None)

    prior = weighted.loc[weighted["patch"] == "16.14", "patch_weight"]
    assert prior.nunique() == 1
    assert prior.iloc[0] == pytest.approx(0.60), "falls back to the recency floor"


# --- Phase B: archetype pooling -------------------------------------------------------------


def _pool_records(divergent_estimate: float | None = None):
    """Six champions sharing an action; four mages, two marksmen."""
    records = []
    for index, (champion, archetype, estimate) in enumerate(
        [
            (1, "mage", 0.020),
            (2, "mage", 0.022),
            (3, "mage", 0.018),
            (4, "mage", divergent_estimate if divergent_estimate is not None else 0.021),
            (5, "marksman", -0.015),
            (6, "marksman", -0.017),
        ]
    ):
        records.append(
            {
                "champion_id": champion,
                "role": "MIDDLE",
                "archetype": archetype,
                "family": "ITEM",
                "stage": 1,
                "action_key": "item:3157",
                "raw_estimate": estimate,
                "standard_error": 0.010,
            }
        )
    return records


ARCHETYPE_LEVELS = [
    ["family", "stage", "action_key"],
    ["family", "stage", "action_key", "role"],
    ["family", "stage", "action_key", "role", "archetype"],
]
ROLE_LEVELS = ARCHETYPE_LEVELS[:2]


def test_archetype_pooling_narrows_the_posterior_for_a_typical_champion():
    with_archetype = _pool_records()
    without = _pool_records()
    apply_partial_pooling(with_archetype, ARCHETYPE_LEVELS)
    apply_partial_pooling(without, ROLE_LEVELS)

    tighter = with_archetype[0]["posterior_standard_error"]
    looser = without[0]["posterior_standard_error"]
    assert tighter <= looser, "adding a level must not widen a well-supported cell"


def test_archetype_pooling_does_not_flatten_a_genuinely_divergent_champion():
    # Champion 4 is a mage whose true effect is the opposite sign of its archetype.
    records = _pool_records(divergent_estimate=-0.030)
    apply_partial_pooling(records, ARCHETYPE_LEVELS)

    divergent = next(r for r in records if r["champion_id"] == 4)
    peers = [r["estimate"] for r in records if r["champion_id"] in (1, 2, 3)]

    assert divergent["estimate"] < min(peers), "a divergent champion must not be pulled to its peers"
    assert divergent["estimate"] < 0, "the sign of a strongly-observed divergence must survive"


def test_posterior_interval_matches_the_posterior_it_reports():
    records = _pool_records()
    apply_partial_pooling(records, ARCHETYPE_LEVELS)

    for record in records:
        width = record["high"] - record["low"]
        assert width == pytest.approx(2 * 1.96 * record["posterior_standard_error"], rel=1e-9)
        assert record["low"] < record["estimate"] < record["high"]


def test_unknown_archetype_degrades_to_role_level_pooling():
    records = _pool_records()
    for record in records:
        record["archetype"] = UNKNOWN_ARCHETYPE
    reference = _pool_records()
    apply_partial_pooling(records, ARCHETYPE_LEVELS)
    apply_partial_pooling(reference, ROLE_LEVELS)

    for pooled, expected in zip(records, reference, strict=True):
        assert pooled["estimate"] == pytest.approx(expected["estimate"], rel=1e-6)

def test_the_modeling_lock_is_a_session_advisory_lock_not_a_renewed_lease():
    acquired = FakeConnection(rows={"pg_try_advisory_lock": [{"ok": True}]})

    assert try_acquire_modeling_lock(acquired) is True
    statement, parameters = acquired.statements[-1]
    assert "pg_try_advisory_lock" in statement
    assert parameters == (MODELING_LOCK_KEY,)

    # Liveness is the session, so nothing writes or renews a deadline column.
    source = inspect.getsource(pipeline.process_next) + inspect.getsource(pipeline.lease_generation)
    for abandoned in ("LeaseExpiresAtUtc", "HeartbeatAtUtc", "LeaseAcquiredAtUtc"):
        assert abandoned not in source, abandoned


def test_a_second_modeler_backs_off_instead_of_claiming_the_same_generation():
    held = FakeConnection(rows={"pg_try_advisory_lock": [{"ok": False}]})

    assert try_acquire_modeling_lock(held) is False
    # It must not go on to claim anything while another modeler is mid-run.
    assert not any("BuildLabGenerations" in statement for statement, _ in held.statements)


def test_the_lock_is_released_even_when_the_run_raises():
    connection = FakeConnection(rows={"pg_try_advisory_lock": [{"ok": True}]})
    release_modeling_lock(connection)

    statement, parameters = connection.statements[-1]
    assert "pg_advisory_unlock" in statement
    assert parameters == (MODELING_LOCK_KEY,)
    # process_next releases in a finally, so a raising run cannot strand the lock for the session.
    assert "finally:" in inspect.getsource(pipeline.process_next)
    assert "release_modeling_lock" in inspect.getsource(pipeline.process_next)


def test_the_coordinator_and_the_modeler_agree_on_the_lock_key():
    coordinator = Path(__file__).resolve().parents[3] / (
        "Transcendence.Service.Core/Services/Analytics/Implementations/"
        "BuildLabGenerationCoordinator.cs"
    )
    if not coordinator.is_file():
        pytest.skip("the .NET coordinator is not present in this checkout")
    # Both hash the same string with hashtextextended; a drift here silently disables the reaper.
    assert f'"{MODELING_LOCK_KEY}"' in coordinator.read_text(encoding="utf-8")


def test_the_claim_is_committed_before_the_long_run_starts():
    # psycopg opens a transaction on the lock SELECT. Without an explicit commit the claim becomes a
    # savepoint and `Modeling` stays invisible to every other session for the whole run, so the admin
    # surface shows PendingDataset and the reaper cannot see the row at all.
    connection = FakeConnection(rows={"pg_try_advisory_lock": [{"ok": True}]})

    try_acquire_modeling_lock(connection)

    assert connection.commits >= 1, "the lock SELECT's transaction must be closed"
    assert "connection.commit()" in inspect.getsource(pipeline.lease_generation)


def test_process_next_reports_a_failed_generation_as_failed_not_success():
    # A oneshot is observed by its exit code. A generation that blew up must not look like a healthy
    # tick, or a permanently broken pipeline reports success forever.
    assert RunOutcome.FAILED is not RunOutcome.COMPLETED
    source = inspect.getsource(pipeline.process_next)
    assert "return RunOutcome.FAILED" in source
    assert "return RunOutcome.COMPLETED" in source
    # Nothing pending, or another modeler already running, is a successful no-op.
    assert source.count("return RunOutcome.IDLE") == 2


def test_run_once_returns_the_outcome_for_the_exit_code():
    source = inspect.getsource(pipeline.run)
    assert "return outcome" in source, "run_once must hand the outcome to the caller"

    from build_lab_modeler.__main__ import EXIT_CODES

    assert EXIT_CODES[RunOutcome.IDLE] == 0
    assert EXIT_CODES[RunOutcome.COMPLETED] == 0
    assert EXIT_CODES[RunOutcome.FAILED] != 0
    # Every outcome must map, or a new one would KeyError at the worst possible moment.
    assert set(EXIT_CODES) == set(RunOutcome)


def test_the_daemon_loop_only_sleeps_when_idle():
    # COMPLETED and FAILED both mean work happened, so the loop should come straight back for the next
    # pending generation rather than sitting out a poll interval.
    source = inspect.getsource(pipeline.run)
    assert "if outcome is RunOutcome.IDLE:" in source


def test_the_oneshot_is_scheduled_by_systemd_and_not_by_the_deploy_poller():
    ops = Path(__file__).resolve().parents[3] / "scripts/ops"
    if not ops.is_dir():
        pytest.skip("ops scripts are not present in this checkout")

    unit = (ops / "transcendence-modeler.service").read_text(encoding="utf-8")
    assert "Type=oneshot" in unit
    assert "run --rm" in unit, "a fresh container per run leaves nothing to recreate"
    assert " -T " in unit, "no TTY exists under systemd"
    assert "pull" in unit, "the image must change between runs, never underneath one"
    assert "TimeoutStartSec=0" in unit, "an hours-long run must not be killed by systemd"

    timer = (ops / "transcendence-modeler.timer").read_text(encoding="utf-8")
    assert "transcendence-modeler.service" in timer

    # The regression this design exists to prevent: the poller recreating the modeler mid-generation.
    poller = (ops / "poll-deploy.sh").read_text(encoding="utf-8")
    assert "analytics-modeler:transcendence-analytics-modeler" not in poller


def test_the_compose_service_is_a_oneshot_not_a_daemon():
    compose = Path(__file__).resolve().parents[3] / "compose.yml"
    if not compose.is_file():
        pytest.skip("compose.yml is not present in this checkout")
    text = compose.read_text(encoding="utf-8")
    modeler = text[text.index("  analytics-modeler:"):]
    modeler = modeler[: modeler.index("\n  pgadmin:")] if "\n  pgadmin:" in modeler else modeler

    assert 'restart: "no"' in modeler, "a restart policy would relaunch a finished run"
    assert "container_name: transcendence-analytics-modeler" not in modeler, (
        "run --rm supplies its own container; a fixed name collides across invocations"
    )
    assert "BUILD_LAB_RUN_ONCE:-true" in modeler


def multi_champion_decisions(champions: tuple[int, ...] = (22, 51, 103)) -> pd.DataFrame:
    """A cohort spanning several champions, two patches and two archetypes."""
    frames = []
    for offset, champion_id in enumerate(champions):
        frame = synthetic_decisions(participants=300, seed=31 + offset)
        frames.append(
            frame.assign(
                champion_id=champion_id,
                match_id=frame["match_id"] + f"-{champion_id}",
                archetype="marksman" if champion_id == 22 else "mage",
            )
        )
    decisions = pd.concat(frames, ignore_index=True)
    # A deterministic stand-in for the calibrated model, so what these tests compare is the
    # partitioning rather than the fit.
    decisions["baseline_win_probability"] = 0.35 + 0.3 * ((decisions.index % 7) / 6.0)
    return decisions


def action_record_key(record: dict) -> tuple:
    return (
        record["champion_id"],
        record["role"],
        record["opponent_id"],
        record["region"],
        record["family"],
        record["stage"],
        record["prefix_hash"],
        record["action_key"],
    )


def test_a_champion_sweep_produces_the_same_action_records_as_the_whole_cohort():
    # The reason the modeler can hold one champion at a time: every grouping key in action_records
    # starts with champion_id, so partitioning on it cannot move a row between cells. If this ever
    # diverges, the sweep is quietly estimating something different from what it claims to.
    decisions = multi_champion_decisions()
    cohort = action_records(decisions)
    swept = [
        record
        for champion_id in sorted(decisions["champion_id"].unique())
        for record in action_records(decisions[decisions["champion_id"] == champion_id])
    ]
    assert cohort, "the fixture must produce cells that clear the minimum-support gate"
    assert sorted(map(action_record_key, swept)) == sorted(map(action_record_key, cohort))
    cohort_by_key = {action_record_key(record): record for record in cohort}
    for record in swept:
        reference = cohort_by_key[action_record_key(record)]
        assert record["estimate"] == pytest.approx(reference["estimate"])
        assert record["observed_count"] == reference["observed_count"]
        assert record["effective_sample_size"] == pytest.approx(
            reference["effective_sample_size"]
        )


def test_a_champion_sweep_produces_the_same_path_records_as_the_whole_cohort():
    decisions = multi_champion_decisions()
    cohort = path_records(decisions)
    swept = [
        record
        for champion_id in sorted(decisions["champion_id"].unique())
        for record in path_records(decisions[decisions["champion_id"] == champion_id])
    ]
    assert cohort
    key = lambda record: (  # noqa: E731 - a local projection, not a policy
        record["champion_id"],
        record["role"],
        record["opponent_id"],
        record["region"],
        record["path_hash"],
    )
    assert sorted(map(key, swept)) == sorted(map(key, cohort))
    cohort_by_key = {key(record): record for record in cohort}
    for record in swept:
        assert record["estimate"] == pytest.approx(cohort_by_key[key(record)]["estimate"])


def test_borrowing_weights_and_drift_exclusion_are_champion_local():
    # apply_row_weights and exclude_drifted_prior_actions both compare current against prior patch
    # within a cell keyed on champion, so the sweep must reproduce the cohort-wide weights exactly.
    decisions = multi_champion_decisions()
    changes = PatchChangeSet(items=frozenset({3031}), runes=frozenset(), champions=frozenset())
    archetypes = {22: "marksman", 51: "mage", 103: "mage"}
    arguments = ("26.14", ["26.14", "26.13"], None, changes, archetypes)
    cohort = prepare_decisions(decisions, *arguments, exclude_drift=True)
    swept = pd.concat(
        [
            prepare_decisions(
                decisions[decisions["champion_id"] == champion_id],
                *arguments,
                exclude_drift=True,
            )
            for champion_id in sorted(decisions["champion_id"].unique())
        ],
        ignore_index=True,
    )
    assert not cohort.empty
    key_columns = ["champion_id", "match_id", "family", "stage", "action_key"]
    assert len(swept) == len(cohort)
    left = cohort.set_index(key_columns).sort_index()
    right = swept.set_index(key_columns).sort_index()
    assert left.index.equals(right.index)
    assert left["patch_weight"].to_numpy() == pytest.approx(right["patch_weight"].to_numpy())
    assert (left["archetype"] == right["archetype"]).all()


def test_pooling_still_borrows_strength_across_the_swept_champions():
    # Pooling is the one genuinely cross-champion stage. It runs on the accumulated records, so a
    # sparse champion must still shrink toward the champions swept before and after it.
    decisions = multi_champion_decisions()
    records = [
        record
        for champion_id in sorted(decisions["champion_id"].unique())
        for record in action_records(decisions[decisions["champion_id"] == champion_id])
    ]
    before = [record["estimate"] for record in records]
    apply_partial_pooling(records, ACTION_POOLING_LEVELS)
    after = [record["estimate"] for record in records]
    assert any(
        pooled != pytest.approx(raw) for pooled, raw in zip(before, after, strict=True)
    ), "pooling over the swept records changed nothing, so no strength was borrowed"


def test_every_cohort_loader_scopes_on_the_same_predicates():
    # The sweep is only correct if all five loaders agree on which rows belong to the scope. Item,
    # rune and spell rows are per participant; team composition and kill state are per match.
    participant_scoped = (load_item_events, load_rune_decisions, load_spell_decisions)
    match_scoped = (timeline_state_events_query, load_participant_teams)
    for loader in participant_scoped + match_scoped:
        source = inspect.getsource(loader)
        assert "scope_predicates(" in source, loader.__name__
        assert "match_sample_range" in source, loader.__name__
    for loader in participant_scoped:
        assert "match_scoped=False" in inspect.getsource(loader), loader.__name__
    for loader in match_scoped:
        assert "match_scoped=True" in inspect.getsource(loader), loader.__name__


def test_scope_predicates_pick_the_filter_that_matches_the_grain():
    # Both grains restrict to the champion's matches: that predicate is what lets the plan drive off
    # the small champion_matches set instead of re-reading the cohort once per champion.
    participant = scope_predicates(22, None, match_scoped=False)
    match = scope_predicates(22, None, match_scoped=True)
    for clause in (participant, match):
        assert 'm."Id" IN (SELECT "MatchId" FROM champion_matches)' in clause
    # Only the participant grain narrows to the champion's own rows. A kill or objective diff is a fact
    # about the match, so a match-scoped load must keep all ten participants.
    assert 'p."ChampionId" = %(champion_id)s' in participant
    assert 'p."ChampionId" = %(champion_id)s' not in match
    # An unscoped load must add nothing, and a modulus of 1 selects the whole cohort already.
    assert scope_predicates(None, None, match_scoped=False) == ""
    assert scope_predicates(None, None, match_scoped=True) == ""
    # An id range is an index scan; the hash residue it replaced could not use one, so every slice
    # re-scanned the whole cohort.
    sampled = scope_predicates(None, ("019fb140-0000-7000-8000-000000000000", "019fb141-0000-7000-8000-000000000000"), match_scoped=False)
    assert 'm."Id" >= %(match_sample_from)s' in sampled
    assert 'm."Id" < %(match_sample_until)s' in sampled
    assert "hashtextextended" not in sampled


def test_the_champion_match_set_is_materialized_and_cohort_scoped():
    # Measured on prod: without MATERIALIZED the planner inlines this and re-reads the whole cohort per
    # champion (100s for one champion's item events); with it, the same load takes under six seconds.
    # The cohort predicates belong inside the CTE so it stays the small side of the join.
    leading = champion_match_cte(22, leading=True)
    assert leading.startswith("champion_matches AS MATERIALIZED (")
    assert leading.endswith(",\n"), "a leading CTE must chain onto the query's own WITH"
    standalone = champion_match_cte(22, leading=False)
    assert standalone.startswith("WITH champion_matches AS MATERIALIZED (")
    for clause in ('cm."Patch" = ANY(%(patches)s)', 'cm."Duration" >= 300', 'cm."QueueId" = 420'):
        assert clause in standalone
    # An unscoped load must not emit a CTE that nothing references.
    assert champion_match_cte(None, leading=True) == ""
    assert champion_match_cte(None, leading=False) == ""


def test_every_champion_scoped_loader_defines_the_match_set_it_references():
    # A loader that filters on champion_matches without defining it is a runtime SQL error that only
    # shows up on the scoped path, which is every load the estimate sweep makes.
    for loader in (
        load_item_events,
        load_rune_decisions,
        load_spell_decisions,
        timeline_state_events_query,
        load_participant_teams,
    ):
        source = inspect.getsource(loader)
        assert "champion_match_cte(" in source, loader.__name__


def test_the_training_sample_keeps_the_row_budget_without_loading_the_corpus():
    # The structural fit was always capped at max_training_rows; sampling matches in the query is how
    # that cap stops costing a full corpus load.
    assert training_sample_modulus(40_000, 12_000) == 3
    # A cohort smaller than the target is taken whole rather than thinned.
    assert training_sample_modulus(5_000, 12_000) == 1
    assert training_sample_modulus(0, 12_000) == 1


def test_the_estimate_sweep_scopes_every_load_to_one_champion(monkeypatch, tmp_path):
    monkeypatch.setenv("BUILD_LAB_SWEEP_WORKERS", "1")
    settings, generation = publishing_generation(monkeypatch, tmp_path)
    champions = [22, 51, 103]
    monkeypatch.setattr(pipeline, "load_cohort_champions", lambda *_: champions)
    scopes: list[dict] = []
    real_loader = pipeline.load_decision_frame

    def recording(*args, **kwargs):
        scopes.append(kwargs)
        return real_loader(*args, **kwargs)

    monkeypatch.setattr(pipeline, "load_decision_frame", recording)
    model_generation(FakeConnection(), generation, settings)

    # The sliced training draw first, then exactly one scoped load per champion. An unscoped estimate
    # load is the shape that could not fit in memory.
    # Partitioned by what the load is FOR, not by a fixed count: the number of training ranges depends
    # on how big the cohort is relative to the sample target.
    training = [scope for scope in scopes if scope.get("champion_id") is None]
    sweep = [scope for scope in scopes if scope.get("champion_id") is not None]
    assert training, "the structural fit must draw at least one range"
    assert [scope.get("champion_id") for scope in sweep] == champions
    assert all(scope.get("match_sample_range") is None for scope in sweep)
    # Every slice draws a disjoint residue of the same modulus, so the union is the intended sample
    # rather than the same matches fetched eight times.
    assert all(scope.get("champion_id") is None for scope in training)
    # Each training load reads a distinct id range, and the ranges are disjoint, so the union is the
    # intended sample rather than the same matches fetched repeatedly.
    drawn = [scope["match_sample_range"] for scope in training]
    assert len(set(drawn)) == len(drawn), drawn


def cohort_timeline_fixture():
    """Two matches of events, of which the champion under test played only the first.

    Shaped like the loader's output, including the payload scalars arriving as text.
    """
    events = pd.DataFrame(
        [
            {"match_id": "mine", "event_index": 0, "timestamp_ms": 500, "event_type": "CHAMPION_KILL", "killer_participant_id": "1", "killer_team_id": None, "owner_team_id": None},
            {"match_id": "mine", "event_index": 1, "timestamp_ms": 900, "event_type": "BUILDING_KILL", "killer_participant_id": "0", "killer_team_id": None, "owner_team_id": "200"},
            {"match_id": "mine", "event_index": 2, "timestamp_ms": 1400, "event_type": "CHAMPION_KILL", "killer_participant_id": "6", "killer_team_id": None, "owner_team_id": None},
            {"match_id": "theirs", "event_index": 0, "timestamp_ms": 400, "event_type": "CHAMPION_KILL", "killer_participant_id": "6", "killer_team_id": None, "owner_team_id": None},
            {"match_id": "theirs", "event_index": 1, "timestamp_ms": 700, "event_type": "ELITE_MONSTER_KILL", "killer_participant_id": "6", "killer_team_id": None, "owner_team_id": None},
        ]
    )
    decisions = pd.DataFrame(
        [
            {"match_id": "mine", "participant_id": 1, "timestamp_ms": 1000},
            {"match_id": "mine", "participant_id": 1, "timestamp_ms": 2000},
        ]
    )
    teams = pd.DataFrame(
        [
            {"match_id": "mine", "participant_id": 1, "team_id": 100},
            {"match_id": "mine", "participant_id": 6, "team_id": 200},
            {"match_id": "theirs", "participant_id": 6, "team_id": 200},
        ]
    )
    return events, decisions, teams


class ChunkedTimelineConnection:
    """A connection whose server-side cursor hands the fixture back in chunks.

    Mirrors psycopg closely enough to drive `stream_cohort_event_state`: a named cursor, a
    `description` naming the columns, and `fetchmany` yielding tuples. A deliberately tiny chunk size
    splits a match across chunk boundaries, which is the case the cumulative sums have to survive.
    """

    class Column:
        def __init__(self, name):
            self.name = name

    class Cursor:
        def __init__(self, frame, chunk):
            self._frame = frame
            self._chunk = chunk
            self._offset = 0
            self.description = [ChunkedTimelineConnection.Column(c) for c in frame.columns]
            self.executed = []

        def __enter__(self):
            return self

        def __exit__(self, *_):
            return False

        def execute(self, sql, params=None):
            self.executed.append((sql, params))
            return self

        def fetchmany(self, size):
            window = self._frame.iloc[self._offset:self._offset + size]
            self._offset += len(window)
            return list(window.itertuples(index=False, name=None))

    def __init__(self, frame, chunk=2):
        self._frame = frame
        self._chunk = chunk
        self.cursors = []

    def cursor(self, name=None, row_factory=None):
        cursor = ChunkedTimelineConnection.Cursor(self._frame, self._chunk)
        cursor.name = name
        self.cursors.append(cursor)
        return cursor


def streamed_state(events, teams, chunk=2):
    connection = ChunkedTimelineConnection(events, chunk=chunk)
    state = stream_cohort_event_state(
        connection, ["16.15"], datetime(2026, 8, 10, tzinfo=timezone.utc), teams, chunk_rows=chunk
    )
    return state, connection


def test_streamed_cohort_state_matches_the_buffered_per_scope_path():
    # The whole safety argument for reducing the cohort once instead of querying per champion. If these
    # diverge, the sweep publishes different numbers than the per-scope loads produced.
    events, decisions, teams = cohort_timeline_fixture()
    scoped = events[events["match_id"] == "mine"].reset_index(drop=True)

    buffered = enrich_with_predecision_event_state(decisions, scoped, teams)
    state, _ = streamed_state(events, teams)
    streamed = apply_event_state(decisions, state, teams)

    pd.testing.assert_frame_equal(buffered, streamed)
    # Not a vacuous comparison: the fixture has to actually move the diff columns.
    assert buffered["team_kill_diff"].tolist() == [1.0, 0.0]
    assert buffered["team_tower_diff"].tolist() == [1.0, 1.0]
    assert buffered["has_event_state"].tolist() == [1.0, 1.0]


def test_a_chunk_boundary_inside_a_match_does_not_restart_its_counters():
    # Cumulative sums are deferred to the end for exactly this reason: chunking is a transport detail
    # and must not be observable in the result.
    events, decisions, teams = cohort_timeline_fixture()
    whole, _ = streamed_state(events, teams, chunk=len(events))
    split, connection = streamed_state(events, teams, chunk=1)

    assert len(connection.cursors[0].executed) == 1, "the statement is issued once, then streamed"
    pd.testing.assert_frame_equal(
        apply_event_state(decisions, whole, teams),
        apply_event_state(decisions, split, teams),
    )


def test_an_unrelated_match_cannot_reach_a_published_column():
    # Why cohort-wide state is safe to share across every champion: merge_asof carries the match in
    # `by`, so a match the champion never played is inert. Asserted directly so a future change to the
    # merge keys fails here rather than silently mixing matches together.
    events, decisions, teams = cohort_timeline_fixture()
    scoped = events[events["match_id"] == "mine"].reset_index(drop=True)

    pd.testing.assert_frame_equal(
        apply_event_state(decisions, event_state_from_events(scoped, teams), teams),
        apply_event_state(decisions, event_state_from_events(events, teams), teams),
    )


def test_event_state_is_held_in_a_narrow_numeric_width():
    # The regression that took the modeler down: holding the cohort's raw events cost ~340 bytes a row
    # and blew the 6g container. The reduced form has to stay numeric and must not carry the match id
    # as a string, which is what dominated that footprint.
    events, _, teams = cohort_timeline_fixture()
    state, _ = streamed_state(events, teams)

    assert list(state.cumulative.columns) == [
        "match_code", "team_id", "timestamp_ms", "kills", "towers", "objectives"
    ]
    assert all(str(dtype).startswith("int") for dtype in state.cumulative.dtypes), state.cumulative.dtypes
    per_row = state.cumulative.memory_usage(deep=True).sum() / max(len(state.cumulative), 1)
    assert per_row < 64, per_row


def test_coverage_separates_a_payloadless_match_from_an_unattributable_one():
    # has_event_state rides on covered_matches rather than on the cumulative frame, because a match
    # whose events attribute to no team is still a match that HAD payload rows.
    events = pd.DataFrame(
        [{"match_id": "covered", "event_index": 0, "timestamp_ms": 500, "event_type": "CHAMPION_KILL",
          "killer_participant_id": "0", "killer_team_id": None, "owner_team_id": None}]
    )
    decisions = pd.DataFrame(
        [
            {"match_id": "covered", "participant_id": 1, "timestamp_ms": 2000},
            {"match_id": "payloadless", "participant_id": 1, "timestamp_ms": 2000},
        ]
    )
    teams = pd.DataFrame(
        [
            {"match_id": "covered", "participant_id": 1, "team_id": 100},
            {"match_id": "payloadless", "participant_id": 1, "team_id": 100},
        ]
    )
    state, _ = streamed_state(events, teams)

    enriched = apply_event_state(decisions, state, teams)
    assert state.cumulative.empty, "killerId 0 with no owning team attributes to nobody"
    assert enriched["has_event_state"].tolist() == [1.0, 0.0]


def test_an_empty_cohort_streams_to_empty_state():
    events, decisions, teams = cohort_timeline_fixture()
    state, _ = streamed_state(events.iloc[:0], teams)
    assert state.cumulative.empty
    assert state.covered_matches == frozenset()
    assert apply_event_state(decisions, state, teams)["has_event_state"].tolist() == [0.0, 0.0]


def test_the_sweep_never_reads_the_timeline_once_per_champion(monkeypatch, tmp_path):
    # The amplification this removed: `load_timeline_state_events` is match-scoped, so a champion-scoped
    # call re-read every match that champion played -- ten times over the cohort across a sweep, which
    # is what projected a 173-champion run to 17 days on prod.
    monkeypatch.setenv("BUILD_LAB_SWEEP_WORKERS", "1")
    settings, generation = publishing_generation(monkeypatch, tmp_path)
    champions = [22, 51, 103]
    monkeypatch.setattr(pipeline, "load_cohort_champions", lambda *_: champions)
    calls: list = []
    real_loader = pipeline.load_timeline_state_events

    def recording(connection, patches, cutoff, champion_id=None, match_sample_range=None):
        calls.append({"champion_id": champion_id, "match_sample_range": match_sample_range})
        return real_loader(connection, patches, cutoff, champion_id, match_sample_range)

    monkeypatch.setattr(pipeline, "load_timeline_state_events", recording)
    monkeypatch.setattr(
        pipeline,
        "stream_cohort_event_state",
        lambda *a, **k: pipeline.CohortEventState(pipeline.empty_event_state_rows(), {}, frozenset()),
    )
    model_generation(FakeConnection(), generation, settings)

    assert all(call["champion_id"] is None for call in calls), calls


def test_the_sweep_streams_the_cohort_rather_than_buffering_it(monkeypatch, tmp_path):
    # This failure mode is silent: a buffered cohort read works on a small corpus and only dies once
    # the cohort outgrows the container, which is exactly how it reached prod.
    monkeypatch.setenv("BUILD_LAB_SWEEP_WORKERS", "1")
    settings, generation = publishing_generation(monkeypatch, tmp_path)
    monkeypatch.setattr(pipeline, "load_cohort_champions", lambda *_: [22])
    streamed: list = []

    def recording_stream(connection, patches, cutoff, teams, **kwargs):
        streamed.append(patches)
        return pipeline.CohortEventState(pipeline.empty_event_state_rows(), {}, frozenset())

    monkeypatch.setattr(pipeline, "stream_cohort_event_state", recording_stream)
    model_generation(FakeConnection(), generation, settings)

    assert len(streamed) == 1, "the cohort's event state is streamed exactly once per process"


def test_the_sequential_sweep_caps_blas_threads_like_the_pool_does(monkeypatch, tmp_path):
    # #162 capped BLAS threads for spawned workers and made sequential the default in the same change,
    # so the cap landed on the branch the default never takes. `nproc` reports the host's cores inside
    # the container because a cpu quota does not change it, so OpenBLAS sized its pools for 46 cores
    # against a 3-cpu quota: prod ran 92 threads, spent ~300% cpu with almost no I/O, and did not finish
    # one champion in 80 minutes, with every stack sample inside a logistic fit.
    #
    # Asserted against the wrapper rather than live threadpool_info(), which reports an empty list when
    # no accelerated backend happens to be loaded and would make this pass vacuously.
    monkeypatch.setenv("BUILD_LAB_SWEEP_WORKERS", "1")
    monkeypatch.setenv("BUILD_LAB_SWEEP_BLAS_THREADS", "1")
    settings, generation = publishing_generation(monkeypatch, tmp_path)
    monkeypatch.setattr(pipeline, "load_cohort_champions", lambda *_: [22, 51])
    monkeypatch.setattr(
        pipeline,
        "stream_cohort_event_state",
        lambda *a, **k: pipeline.CohortEventState(pipeline.empty_event_state_rows(), {}, frozenset()),
    )

    events: list = []

    class RecordingLimits:
        def __init__(self, limits=None):
            self.limits = limits

        def __enter__(self):
            events.append(("enter", self.limits))
            return self

        def __exit__(self, *_):
            events.append(("exit", self.limits))
            return False

    real_sweep = pipeline.sweep_champion

    def recording_sweep(champion_id):
        events.append(("champion", champion_id))
        return real_sweep(champion_id)

    monkeypatch.setattr(pipeline, "threadpool_limits", RecordingLimits)
    monkeypatch.setattr(pipeline, "sweep_champion", recording_sweep)
    model_generation(FakeConnection(), generation, settings)

    assert ("enter", 1) in events, events
    champions_seen = [name for name, _ in events].count("champion")
    assert champions_seen == 2, events
    # Every champion has to run INSIDE the limit, not merely after it was set up once.
    opened = events.index(("enter", 1))
    closed = events.index(("exit", 1))
    swept = [i for i, (name, _) in enumerate(events) if name == "champion"]
    assert all(opened < i < closed for i in swept), events


def test_the_blas_thread_cap_is_configurable_and_floored(monkeypatch, tmp_path):
    assert modeler_settings(monkeypatch, BUILD_LAB_ARTIFACT_DIR=str(tmp_path)).sweep_blas_threads == 1
    assert modeler_settings(
        monkeypatch, BUILD_LAB_ARTIFACT_DIR=str(tmp_path), BUILD_LAB_SWEEP_BLAS_THREADS="3"
    ).sweep_blas_threads == 3
    assert modeler_settings(
        monkeypatch, BUILD_LAB_ARTIFACT_DIR=str(tmp_path), BUILD_LAB_SWEEP_BLAS_THREADS="0"
    ).sweep_blas_threads == 1


class DictRowCursor:
    """A cursor shaped like psycopg's, whose row factory decides the row type.

    Mirrors the property that broke prod: a connection opened with `row_factory=dict_row` hands
    `fetchall()` dicts unless a cursor names a different factory.
    """

    class Column:
        def __init__(self, name: str) -> None:
            self.name = name

    def __init__(self, columns: list[str], rows: list[tuple], row_factory) -> None:
        self._columns = columns
        self._rows = rows
        self._row_factory = row_factory
        self.executed: list[tuple[str, dict | None]] = []

    def __enter__(self):
        return self

    def __exit__(self, *_):
        return False

    def execute(self, sql, params=None):
        self.executed.append((sql, params))
        return self

    @property
    def description(self):
        return [self.Column(name) for name in self._columns]

    def fetchall(self):
        if self._row_factory is pipeline.dict_row:
            return [dict(zip(self._columns, row, strict=True)) for row in self._rows]
        return list(self._rows)


class DictRowConnection:
    def __init__(self, columns: list[str], rows: list[tuple]) -> None:
        self._columns = columns
        self._rows = rows
        self.cursors: list[DictRowCursor] = []

    def cursor(self, row_factory=None):
        # Default to the connection's factory, exactly as psycopg does.
        cursor = DictRowCursor(self._columns, self._rows, row_factory or pipeline.dict_row)
        self.cursors.append(cursor)
        return cursor


def test_the_frame_reader_returns_values_not_column_names_on_a_dict_row_connection():
    # The bug this pins cost a production run: pandas' DBAPI2 fallback iterates each dict row, which
    # yields its KEYS, so every cell came back equal to its own column name. The row count and shape
    # were right, dtypes were merely object, and it only surfaced later as int('event_type').
    columns = ["match_id", "event_index", "event_type", "action_id"]
    rows = [("match-a", 0, 0, 6672), ("match-a", 1, 2, 3031)]
    connection = DictRowConnection(columns, rows)

    frame = pipeline.read_sql_frame(connection, "SELECT ...", {"patches": ["16.15"]})

    assert list(frame.columns) == columns
    assert frame["event_type"].tolist() == [0, 2]
    assert frame["match_id"].tolist() == ["match-a", "match-a"]
    # The corruption signature: a cell equal to its own column name.
    for column in columns:
        assert not (frame[column].astype(str) == column).any(), column
    # Numeric columns must stay numeric, or every downstream int() cast is a coin flip.
    assert int(frame["event_type"].iloc[0]) == 0
    # It must ask for tuples rather than inherit the connection's dict factory.
    assert connection.cursors[-1]._row_factory is pipeline.tuple_row


def test_no_loader_uses_the_pandas_dbapi_fallback():
    # pd.read_sql_query on the run's dict_row connection is silently wrong, so no loader may reach for
    # it. Keeping this a source assertion catches a reintroduction that no fake connection would.
    source = inspect.getsource(pipeline)
    assert "pd.read_sql_query" not in source.replace("`pd.read_sql_query` must not", "")
    for loader in (
        load_item_events,
        load_rune_decisions,
        load_spell_decisions,
        load_timeline_state_events,
        load_participant_teams,
    ):
        assert "read_sql_frame(" in inspect.getsource(loader), loader.__name__


def test_no_status_write_references_a_dropped_lease_column():
    # 20260801025434_DropModelingLeaseColumns removed LeaseExpiresAtUtc and HeartbeatAtUtc when the
    # lease became a session advisory lock. A stale reference in the TERMINAL write is invisible until
    # a run actually succeeds, and then it fails the publish it was supposed to record.
    for function in (model_generation, mark_failed):
        source = inspect.getsource(function)
        for dropped in ("LeaseExpiresAtUtc", "HeartbeatAtUtc"):
            assert dropped not in source, f"{function.__name__} still writes {dropped}"


def phase_flipped_calibration_data(seed: int = 7):
    """Raw scores whose bias flips sign between early and late game.

    A single monotone map cannot correct both halves: for any given raw score the two bands disagree
    about the outcome rate in opposite directions, so a global isotonic fit averages them and leaves
    each band wrong. This is the shape the promotion gate's per-band ECE is designed to catch.
    """
    rng = np.random.default_rng(seed)
    raw, minutes, actual = [], [], []
    for minute, shift in ((5.0, -0.25), (25.0, +0.25)):
        scores = rng.uniform(0.15, 0.85, 4000)
        raw.extend(scores)
        minutes.extend([minute + rng.uniform(0, 4.0) for _ in scores])
        actual.extend(rng.binomial(1, np.clip(scores + shift, 0.01, 0.99)))
    return (
        np.asarray(minutes, dtype=float),
        np.asarray(raw, dtype=float),
        np.asarray(actual, dtype=int),
    )


def worst_band_ece(minutes, actual, predicted) -> float:
    early = minutes < 15
    return max(
        expected_calibration_error(actual[early], predicted[early]),
        expected_calibration_error(actual[~early], predicted[~early]),
    )


def test_banded_calibration_beats_a_global_map_on_a_phase_dependent_bias():
    minutes, raw, actual = phase_flipped_calibration_data()

    from sklearn.isotonic import IsotonicRegression

    global_only = IsotonicRegression(out_of_bounds="clip")
    global_only.fit(raw, actual)
    global_worst = worst_band_ece(minutes, actual, np.asarray(global_only.predict(raw), dtype=float))

    banded = pipeline.fit_banded_calibrator(minutes, raw, actual)
    banded_worst = worst_band_ece(
        minutes, actual, pipeline.apply_banded_calibrator(banded, minutes, raw)
    )

    # The global map is what produced a 0.053 worst band against a 0.025 limit on the live cohort.
    assert global_worst > 0.05, f"fixture must exhibit the bias it claims: {global_worst}"
    assert banded_worst < global_worst / 2, f"global={global_worst} banded={banded_worst}"


def test_a_calibration_split_too_small_to_band_falls_back_instead_of_memorising():
    # Bands are quantiles, so they always hold roughly equal counts -- the minimum-rows guard fires on
    # a SMALL calibration split, not on one lopsided band. A sparse cohort must therefore degrade to a
    # single global map rather than fit five isotonic curves on a few dozen rows each.
    rng = np.random.default_rng(3)
    raw = rng.uniform(0.1, 0.9, 300)
    minutes = rng.uniform(0.0, 40.0, 300)
    actual = rng.binomial(1, raw)

    calibrator = pipeline.fit_banded_calibrator(minutes, raw, actual)

    assert calibrator["bands"] == {}, "300 rows over 5 bands must not be fit per band"
    assert calibrator["fallback"] is not None
    values = pipeline.apply_banded_calibrator(calibrator, minutes, raw)
    assert np.isfinite(values).all() and values.shape == raw.shape
    # With no per-band fit, routing must be a no-op rather than silently dropping rows.
    assert np.allclose(values, calibrator["fallback"].predict(raw))


def test_a_skewed_minute_column_no_longer_collapses_the_banding():
    # The whole reason phase edges are fixed: quantiles of a distribution with a point mass collapsed
    # onto that mass and lost bands, so the band count depended on the shape of the draw. Fixed edges
    # are unaffected by skew.
    minutes = np.concatenate([np.full(2000, 5.0), np.full(10, 40.0)])
    edges = pipeline.calibration_band_edges(minutes)
    assert edges.tolist() == [0.5, 8.0, 14.0, 20.0], edges


def test_a_single_outcome_band_gets_no_calibrator_of_its_own():
    rng = np.random.default_rng(11)
    raw = rng.uniform(0.1, 0.9, 2000)
    minutes = np.concatenate([np.full(1000, 5.0), np.full(1000, 30.0)])
    # The late band won every game, so an isotonic fit there would collapse to the constant 1.0.
    actual = np.concatenate([rng.binomial(1, raw[:1000]), np.ones(1000, dtype=int)])

    calibrator = pipeline.fit_banded_calibrator(minutes, raw, actual)
    assignment = np.digitize(minutes, calibrator["edges"])
    late_band = int(assignment[-1])

    assert late_band not in calibrator["bands"]


def test_phase_edges_are_fixed_and_dropped_only_when_they_split_nothing():
    # Boundaries are game-meaningful, not data-derived: plating at 14:00, Herald-to-Baron at 20:00, and
    # pregame separated from minute one because those rows carry no in-game state at all.
    assert pipeline.PHASE_BAND_EDGES == (0.5, 8.0, 14.0, 20.0)

    # Every cohort gets every edge, whatever it contains. Filtering to the observed range would
    # renumber the bands above a dropped edge, so a cohort of only mid-game rows would report them
    # under the "pregame" label. An edge that splits nothing just yields an empty band.
    for minutes in (
        np.asarray([0.0, 1.0, 5.0, 9.0]),        # short games only
        np.asarray([0.0, 3.0, 11.0, 26.0, 44.0]),
        np.full(50, 12.0),                        # every row in one phase
        np.asarray([]),
    ):
        assert pipeline.calibration_band_edges(minutes).tolist() == [0.5, 8.0, 14.0, 20.0]

    # Which means a single-phase cohort is labelled by its actual phase, not by band zero.
    assignment = np.digitize(np.full(5, 12.0), pipeline.calibration_band_edges(np.full(5, 12.0)))
    assert [pipeline.phase_band_label(int(b)) for b in np.unique(assignment)] == ["late-laning"]


def test_pregame_decisions_land_in_their_own_phase_band():
    # Rune pages and summoners are chosen with no game state, so they must not share a calibration map
    # with early item purchases. They all sit at minute 0, which the 0.5 edge separates.
    minutes = np.asarray([0.0, 0.0, 1.0, 7.9, 8.1, 13.9, 14.1, 19.9, 20.1, 35.0])
    assignment = np.digitize(minutes, np.asarray(pipeline.PHASE_BAND_EDGES, dtype=float))
    assert assignment.tolist() == [0, 0, 1, 1, 2, 2, 3, 3, 4, 4]
    assert [pipeline.phase_band_label(b) for b in (0, 1, 2, 3, 4)] == [
        "pregame", "early-laning", "late-laning", "mid-game", "late-game",
    ]


def test_scoring_routes_each_row_through_the_band_it_belongs_to():
    # Published numbers must come off the same map the gate measured, so identical rows that differ
    # only in decision minute must be able to calibrate differently.
    minutes, raw, actual = phase_flipped_calibration_data()
    calibrator = pipeline.fit_banded_calibrator(minutes, raw, actual)
    probe = np.full(4, 0.5)
    early = pipeline.apply_banded_calibrator(calibrator, np.full(4, 5.0), probe)
    late = pipeline.apply_banded_calibrator(calibrator, np.full(4, 27.0), probe)
    assert not np.allclose(early, late), "minute had no effect, so routing is not happening"
    # And the direction matches the injected bias: late games were under-predicted.
    assert late.mean() > early.mean()


def test_the_manifest_separates_an_untestable_patch_holdout_from_a_failed_one():
    # A single-patch cohort cannot be split across a patch boundary. Reporting only `False` makes
    # "not testable" indistinguishable from "tested and failed", which is what blocked promotion.
    decisions = synthetic_decisions()
    single = decisions[decisions["patch"] == "26.14"]
    _, metrics = train_structural_model(single, max_training_rows=100_000)
    assert metrics["heldOutPatch"] is None
    assert metrics["heldOutPatchApplicable"] is False
    assert metrics["heldOutPatchPassed"] is False
    # Two patches: the holdout is testable, so applicability is true whatever the verdict.
    _, both = train_structural_model(decisions, max_training_rows=100_000)
    assert both["heldOutPatch"] is not None
    assert both["heldOutPatchApplicable"] is True
    # Calibration provenance travels with the metrics, since the ECE gate is measured against it.
    assert isinstance(both["calibrationBandEdges"], list)
    # The fixture's calibration split is far too small to band, so zero per-band fits is the correct
    # outcome here; what must hold is that the provenance is reported either way.
    assert both["calibrationBandCount"] == 0


def test_the_cli_still_maps_run_outcomes_onto_exit_codes():
    from build_lab_modeler import __main__ as entrypoint

    # A scheduler sees only the exit code, so a failed generation must not look like a quiet tick.
    assert entrypoint.EXIT_CODES[RunOutcome.IDLE] == 0
    assert entrypoint.EXIT_CODES[RunOutcome.COMPLETED] == 0
    assert entrypoint.EXIT_CODES[RunOutcome.FAILED] == 1


def test_the_cli_gate_limits_match_the_committed_dotnet_options():
    # The CLI reports the promoter's verdict locally. If these drift from BuildLabModelingOptions the
    # local answer stops predicting the deployed one, which is the whole reason the subcommand exists.
    from build_lab_modeler import __main__ as entrypoint

    options = pathlib.Path(__file__).resolve().parents[3] / (
        "Transcendence.Service.Core/Services/Analytics/Models/BuildLabModelingOptions.cs"
    )
    source = options.read_text(encoding="utf-8")
    for field, limit in (
        ("MaximumOverallEce", entrypoint.GATE_LIMITS["maximumOverallEce"]),
        ("MaximumTimeBandEce", entrypoint.GATE_LIMITS["maximumTimeBandEce"]),
    ):
        assert f"public double {field} {{ get; set; }} = {limit};" in source, field


def test_every_subcommand_is_on_demand_and_needs_no_pending_generation():
    from build_lab_modeler import __main__ as entrypoint

    parser = entrypoint.build_parser()
    # An empty argv must keep meaning "production run", or the scheduler's invocation changes meaning.
    assert parser.parse_args([]).command is None
    for command in ("dataset", "train", "champion"):
        parsed = parser.parse_args([command])
        assert parsed.command == command
        # Each must accept an explicit cohort so it can run with no generation row at all.
        assert hasattr(parsed, "patches") and hasattr(parsed, "cutoff")
        assert hasattr(parsed, "no_cache") and hasattr(parsed, "refresh")


def test_the_training_cache_key_tracks_the_ranges_and_not_the_cutoff():
    from build_lab_modeler.cache import cohort_key

    low = ("019fb140-0000-7000-8000-000000000000", "019fb141-0000-7000-8000-000000000000")
    high = ("019fb142-0000-7000-8000-000000000000", "019fb143-0000-7000-8000-000000000000")
    base = cohort_key(["16.15"], [low], 31_250)
    assert base == cohort_key(["16.15"], [low], 31_250)
    # Anything that changes which rows are drawn must change the key.
    assert base != cohort_key(["16.15", "16.14"], [low], 31_250)
    assert base != cohort_key(["16.15"], [high], 31_250)
    assert base != cohort_key(["16.15"], [low, high], 31_250)
    assert base != cohort_key(["16.15"], [low], 10_000)
    # The cutoff deliberately does NOT appear: a range's contents are fixed once ids beyond it exist,
    # so including it made every daily generation redraw a sample it already had. Staleness is bounded
    # separately by is_fresh_for.
    assert "cutoff" not in cohort_key.__doc__.lower().split("deliberately")[0]


def test_a_cached_slice_round_trips_and_a_truncated_one_is_ignored(tmp_path):
    from build_lab_modeler.cache import TrainingCache

    cache = TrainingCache.for_cohort(tmp_path, ["16.15"], [("019fb140-0000-7000-8000-000000000000", "019fb141-0000-7000-8000-000000000000")], 31_250)
    assert cache.read_slice(0) is None, "a cold cache must miss rather than raise"
    frame = pd.DataFrame({"won": [True, False], "minute": [4.0, 21.0]})
    cache.write_slice(0, frame)
    restored = cache.read_slice(0)
    assert restored is not None and len(restored) == 2
    assert restored["won"].tolist() == [True, False]
    # A crash mid-write must not leave something the next run reads as a complete slice.
    cache.slice_path(0).write_bytes(b"not parquet")
    assert cache.read_slice(0) is None
    # Disabling the cache must neither read nor write.
    disabled = TrainingCache(directory=cache.directory, key=cache.key, enabled=False)
    disabled.write_slice(1, frame)
    assert not disabled.slice_path(1).exists()
    assert disabled.read_slice(0) is None


def test_the_draw_reuses_cached_slices_instead_of_querying_again(tmp_path, monkeypatch):
    from build_lab_modeler.cache import TrainingCache

    settings = modeler_settings(monkeypatch, BUILD_LAB_ARTIFACT_DIR=str(tmp_path))
    ranges = [("019fb140-0000-7000-8000-000000000000", "019fb141-0000-7000-8000-000000000000")] * pipeline.TRAINING_SAMPLE_SLICES
    cache = TrainingCache.for_cohort(tmp_path, ["16.15"], ranges, 500)
    for residue in range(pipeline.TRAINING_SAMPLE_SLICES):
        cache.write_slice(residue, pd.DataFrame({"won": [True, False], "minute": [4.0, 21.0]}))
    queried = []
    monkeypatch.setattr(
        pipeline,
        "load_decision_frame",
        lambda *args, **kwargs: queried.append(kwargs) or pd.DataFrame(),
    )
    monkeypatch.setattr(pipeline, "training_draw_shape", lambda *_: (ranges, 500))

    frame = pipeline.build_training_frame(
        FakeConnection(), ["16.15"], "cutoff", None, "16.15", set(), settings,
        lambda frame, exclude_drift=True: frame, cache=cache,
    )

    assert queried == [], "a fully cached draw must not touch the database"
    assert len(frame) == 2 * pipeline.TRAINING_SAMPLE_SLICES


def test_uuid_columns_are_rendered_as_strings_at_the_loader_boundary():
    # psycopg returns `uuid` as uuid.UUID and Arrow cannot infer a type for it, so any frame still
    # holding a match id fails to serialise -- which is every frame the training cache stores.
    from uuid import UUID as Uuid

    match_id = Uuid("019fb1f9-ff6e-77e6-bc6f-298c57097e61")
    columns = ["match_id", "participant_id", "event_type"]
    rows = [(match_id, 1, 0), (match_id, 2, 1)]
    connection = DictRowConnection(columns, rows)

    frame = pipeline.read_sql_frame(connection, "SELECT ...", {})

    assert frame["match_id"].tolist() == ["019fb1f9-ff6e-77e6-bc6f-298c57097e61"] * 2
    assert not any(isinstance(value, Uuid) for value in frame["match_id"])
    # Non-uuid object columns must be left alone.
    assert frame["participant_id"].tolist() == [1, 2]


def test_a_normalised_frame_survives_the_parquet_round_trip(tmp_path):
    # The regression that broke the cache: this write raised ArrowInvalid on a uuid column.
    from uuid import UUID as Uuid
    from build_lab_modeler.cache import TrainingCache

    frame = pipeline.normalise_uuid_columns(
        pd.DataFrame(
            {
                "match_id": [Uuid("019fb1f9-ff6e-77e6-bc6f-298c57097e61"), None],
                "won": [True, False],
                "minute": [4.0, 21.0],
            }
        )
    )
    cache = TrainingCache.for_cohort(tmp_path, ["16.15"], [("019fb140-0000-7000-8000-000000000000", "019fb141-0000-7000-8000-000000000000")], 500)
    cache.write_slice(0, frame)

    restored = cache.read_slice(0)
    assert restored is not None, "the slice must actually be written, not swallowed as a warning"
    assert restored["match_id"].iloc[0] == "019fb1f9-ff6e-77e6-bc6f-298c57097e61"


def test_the_surrogate_export_is_unchanged_by_uuid_normalisation():
    # Normalisation must not move any published number. surrogate_ids interpolates the id into a
    # string and str(UUID) is the canonical hyphenated form, so both spellings must hash the same.
    from uuid import UUID as Uuid

    match_id = Uuid("019fb1f9-ff6e-77e6-bc6f-298c57097e61")
    base = pd.DataFrame({"participant_id": [1, 2], "champion_id": [22, 51]})
    as_uuid = base.assign(match_id=[match_id, match_id])
    as_text = base.assign(match_id=[str(match_id), str(match_id)])

    salt = "s" * 32
    left = deidentified_export(as_uuid, salt)
    right = deidentified_export(as_text, salt)

    assert left["match_surrogate"].tolist() == right["match_surrogate"].tolist()
    assert left["participant_surrogate"].tolist() == right["participant_surrogate"].tolist()


def test_adaptive_ece_bins_still_catch_systematic_bias():
    # The whole safety argument for coarsening bins: a real shift persists inside any bin, so it is
    # still measured, while noise averages out. If this ever fails, the gate has been blinded.
    rng = np.random.default_rng(101)
    for n in (2_256, 5_500, 20_000):
        predicted = np.clip(rng.beta(2.0, 2.0, n), 1e-4, 1 - 1e-4)
        for bias in (0.05, 0.10, 0.25):
            actual = rng.binomial(1, np.clip(predicted + bias, 0.01, 0.99))
            measured = expected_calibration_error(actual, predicted)
            # A shift of `bias` must register as roughly that much error, not be smoothed away.
            assert measured > bias * 0.6, f"n={n} bias={bias} measured={measured}"


def test_adaptive_ece_bins_lower_the_noise_floor_on_a_calibrated_sample():
    # A perfectly calibrated model must not be failed for being observed on a thin band. At 10 fixed
    # bins the live cohort's thinnest band (n=2,256) had a noise floor above the promoter's 0.025.
    rng = np.random.default_rng(202)
    fixed, adaptive = [], []
    for _ in range(200):
        predicted = np.clip(rng.beta(2.0, 2.0, 2_256), 1e-4, 1 - 1e-4)
        actual = rng.binomial(1, predicted)
        fixed.append(expected_calibration_error(actual, predicted, bins=10))
        adaptive.append(expected_calibration_error(actual, predicted))

    assert float(np.median(fixed)) > 0.018, "fixture must reproduce the biased-at-small-n behaviour"
    assert float(np.median(adaptive)) < float(np.median(fixed)) * 0.75
    # And the corrected floor must sit clear of the limit the promoter actually applies.
    assert float(np.quantile(adaptive, 0.95)) < 0.025


def test_ece_bin_count_scales_with_the_sample_and_stays_bounded():
    assert pipeline.ece_bin_count(0) == 2, "never fewer than two bins"
    assert pipeline.ece_bin_count(999) == 2
    assert pipeline.ece_bin_count(2_256) == 4
    assert pipeline.ece_bin_count(5_500) == 10
    # Capped, so a large sample keeps the resolution the metric was designed around.
    assert pipeline.ece_bin_count(10_000_000) == 10


def clustered_probabilities(clusters: int, rows_per_cluster: int, seed: int = 5):
    rng = np.random.default_rng(seed)
    predicted = np.clip(rng.beta(2.0, 2.0, clusters * rows_per_cluster), 1e-4, 1 - 1e-4)
    labels = np.repeat(np.arange(clusters), rows_per_cluster).astype(str)
    return predicted, labels


def test_the_clustered_null_is_higher_than_a_row_level_one():
    # The correction that matters: rows are not independent observations. A team in a match shares one
    # outcome across all its rows, so treating rows as units understates how much error noise alone
    # produces -- measured at 20-50% on the live cohort.
    predicted, clusters = clustered_probabilities(400, 6)
    rows = np.arange(predicted.size).astype(str)  # every row its own cluster == row-level bootstrap

    row_median, _ = pipeline.clustered_ece_null(predicted, rows, 0.99, resamples=150)
    clustered_median, _ = pipeline.clustered_ece_null(predicted, clusters, 0.99, resamples=150)

    assert clustered_median > row_median * 1.15, f"row={row_median} clustered={clustered_median}"


def test_the_null_threshold_sits_above_its_own_median_and_is_reproducible():
    predicted, clusters = clustered_probabilities(300, 5)
    median, threshold = pipeline.clustered_ece_null(predicted, clusters, 0.99, resamples=200)
    assert threshold > median > 0
    # A gate decision has to be reproducible: the same input must reach the same verdict on a re-run.
    again = pipeline.clustered_ece_null(predicted, clusters, 0.99, resamples=200)
    assert (median, threshold) == again
    # An empty band must not raise; it simply has no floor.
    assert pipeline.clustered_ece_null(np.asarray([]), np.asarray([]), 0.99) == (0.0, 0.0)


def test_a_well_calibrated_model_sits_inside_its_own_noise_floor():
    # The property the gate depends on. If this fails, well-calibrated models are being rejected.
    predicted, clusters = clustered_probabilities(500, 6, seed=9)
    rng = np.random.default_rng(9)
    shared = np.repeat(rng.random(500), 6)
    actual = (predicted > shared).astype(int)

    observed = expected_calibration_error(actual, predicted)
    _, threshold = pipeline.clustered_ece_null(predicted, clusters, 0.99, resamples=300)

    assert observed <= threshold, f"observed={observed} threshold={threshold}"


def test_a_materially_miscalibrated_model_breaks_out_of_its_noise_floor():
    # And the other half: the gate must still catch real miscalibration, or it is decoration.
    predicted, clusters = clustered_probabilities(500, 6, seed=11)
    rng = np.random.default_rng(11)
    shared = np.repeat(rng.random(500), 6)
    biased = np.clip(predicted + 0.12, 0.01, 0.99)
    actual = (biased > shared).astype(int)

    observed = expected_calibration_error(actual, predicted)
    _, threshold = pipeline.clustered_ece_null(predicted, clusters, 0.99, resamples=300)

    assert observed > threshold, f"observed={observed} threshold={threshold}"


def test_the_metrics_report_both_halves_of_the_calibration_gate():
    decisions = synthetic_decisions()
    _, metrics = train_structural_model(decisions, max_training_rows=100_000)

    assert "calibrationExceedsNoiseFloor" in metrics
    assert isinstance(metrics["calibrationExceedsNoiseFloor"], bool)
    assert metrics["maxTimeBandEceExcess"] >= 0.0
    # Bonferroni across the phases present, so five phases are not each tested at the family-wise rate.
    assert 0.9 < metrics["calibrationBandQuantile"] < 1.0
    for phase, detail in metrics["timeBandDetail"].items():
        assert detail["rows"] > 0, phase
        assert detail["independentUnits"] <= detail["rows"], phase
        assert detail["noiseFloorThreshold"] >= detail["noiseFloorMedian"], phase
        assert detail["eceExcess"] >= 0.0, phase
        assert isinstance(detail["withinNoiseFloor"], bool), phase


def reference_build_item_decisions(rows: pd.DataFrame) -> pd.DataFrame:
    """The pandas-per-row implementation that `build_item_decisions` replaced, kept verbatim.

    The rewrite is a performance change with no intended behaviour change, so the guarantee that
    matters is byte-identical output. Keeping the original here makes that assertable rather than
    asserted.
    """
    from build_lab_modeler.pipeline import (
        BUILD_ITEM_CATEGORIES,
        decision_record,
        positive_int,
        positive_or_zero_int,
        remove_last,
    )

    if rows.empty:
        return rows
    output: list[dict] = []
    for _, participant in rows.groupby(["match_id", "participant_id"], sort=False):
        participant = participant.sort_values(["timestamp_ms", "event_index"])
        active: list[tuple[int, int | None]] = []
        undone: set[int] = set()
        for _, event in participant.iterrows():
            event_type = int(event["event_type"])
            item_id = positive_int(event.get("action_id"))
            before_id = positive_int(event.get("before_id"))
            after_id = positive_int(event.get("after_id"))
            if event_type == 0 and item_id:
                active.append((item_id, int(event["event_index"])))
            elif event_type in (1, 3) and item_id:
                remove_last(active, item_id)
            elif event_type == 2:
                if before_id:
                    removed = remove_last(active, before_id)
                    if isinstance(removed, tuple) and removed[1] is not None:
                        undone.add(removed[1])
                if after_id:
                    active.append((after_id, None))
        starter_rows = participant[
            (participant["event_type"] == 0)
            & (participant["build_category"] == 2)
            & (~participant["event_index"].isin(undone))
        ]
        starter_ids = starter_rows["action_id"].dropna().astype(int).tolist()
        selected: list[int] = sorted(starter_ids)
        if starter_ids:
            first = starter_rows.iloc[0].to_dict()
            first["inventory_ids"] = ""
            output.append(decision_record(first, "STARTER", 0, [], sorted(starter_ids)))
        legendary_stage = 0
        boots_stage = 0
        inventory: list[int] = []
        for _, item in participant.iterrows():
            event_type = int(item["event_type"])
            action_id = positive_int(item.get("action_id"))
            before_id = positive_int(item.get("before_id"))
            after_id = positive_int(item.get("after_id"))
            event_index = int(item["event_index"])
            if event_type == 1 or event_type == 3:
                if action_id:
                    remove_last(inventory, action_id)
                continue
            if event_type == 2:
                restored_category = positive_or_zero_int(item.get("build_category"))
                if after_id and restored_category in BUILD_ITEM_CATEGORIES:
                    inventory.append(after_id)
                continue
            if event_type != 0 or not action_id or event_index in undone:
                continue
            build_category = positive_or_zero_int(item.get("build_category"))
            source = item.to_dict()
            source["inventory_ids"] = "-".join(str(value) for value in sorted(inventory))
            if build_category in BUILD_ITEM_CATEGORIES:
                inventory.append(action_id)
            if build_category not in (0, 1):
                continue
            if build_category == 1:
                boots_stage += 1
                family, stage = "BOOTS", boots_stage
            else:
                legendary_stage += 1
                family, stage = "ITEM", legendary_stage
                if legendary_stage == 1:
                    output.append(
                        decision_record(source, "FIRST_ITEM_PATH", 0, [], [*selected, action_id])
                    )
            output.append(decision_record(source, family, stage, selected, [action_id]))
            selected.append(action_id)
    return pd.DataFrame(output)


def realistic_item_events(participants: int = 60, seed: int = 17) -> pd.DataFrame:
    """Item events exercising every branch of the replay: sells, undos, boots, consumables, refunds."""
    rng = np.random.default_rng(seed)
    rows = []
    for participant in range(participants):
        match_id = f"match-{participant // 3}"
        participant_id = 1 + participant % 10
        index = 0
        timestamp = 0
        def event(event_type, action_id, category, before=None, after=None):
            nonlocal index, timestamp
            timestamp += int(rng.integers(20_000, 90_000))
            rows.append({
                "match_id": match_id,
                "participant_id": participant_id,
                "team_id": 100 if participant_id <= 5 else 200,
                "match_date": participant,
                "patch": "26.14",
                "region": "NA1",
                "champion_id": 22 + (participant % 4),
                "opponent_champion_id": 51,
                "role": "BOTTOM",
                "won": bool(participant % 2),
                "event_index": index,
                "event_type": event_type,
                "timestamp_ms": timestamp,
                "action_id": action_id,
                "before_id": before,
                "after_id": after,
                "build_category": category,
                "minute": timestamp / 60_000.0,
                "gold": 500.0 + 120 * index,
                "current_gold": 60.0,
                "xp": 300.0 * (index + 1),
                "cs": 12.0 * (index + 1),
                "lane_cs": 10.0 * (index + 1),
                "jungle_cs": 0.0,
                "level": 2.0 + index,
                "team_gold_diff": 40.0 * index,
                "team_xp_diff": 25.0 * index,
                "team_cs_diff": 3.0 * index,
            })
            index += 1

        event(0, 1055, 2)                       # starter
        event(0, 2003, 2)                       # second starter (potion)
        if participant % 5 == 0:
            event(0, 3070, None)                # uncategorised component
        event(0, 6672 if participant % 2 else 3031, 0)   # first legendary
        if participant % 4 == 0:
            event(1, 1055, 2)                   # sell the starter
        if participant % 7 == 0:
            event(2, None, 2, before=2003, after=2003)   # undo
        event(0, 3006, 1)                       # boots
        event(0, 3036, 0)                       # second legendary
        if participant % 6 == 0:
            event(3, 3006, 1)                   # destroy boots
        event(0, 3072, 0)                       # third legendary
    return pd.DataFrame(rows)


def test_the_vectorised_replay_is_byte_identical_to_the_pandas_one():
    events = realistic_item_events()
    fast = build_item_decisions(events)
    reference = reference_build_item_decisions(events)

    assert not fast.empty and not reference.empty
    assert list(fast.columns) == list(reference.columns)
    assert len(fast) == len(reference), f"fast={len(fast)} reference={len(reference)}"
    pd.testing.assert_frame_equal(
        fast.reset_index(drop=True), reference.reset_index(drop=True), check_dtype=True
    )
    # The fixture must actually exercise the branches, or identical output proves little.
    families = set(fast["family"])
    assert {"STARTER", "ITEM", "BOOTS", "FIRST_ITEM_PATH"} <= families, families
    assert (fast["inventory_ids"].astype(str).str.len() > 0).any(), "inventory state never populated"


def test_the_vectorised_replay_is_materially_faster():
    import time

    events = realistic_item_events(participants=900, seed=23)

    start = time.perf_counter()
    reference = reference_build_item_decisions(events)
    slow = time.perf_counter() - start

    start = time.perf_counter()
    fast = build_item_decisions(events)
    quick = time.perf_counter() - start

    pd.testing.assert_frame_equal(
        fast.reset_index(drop=True), reference.reset_index(drop=True), check_dtype=True
    )
    # This stage dominated a production run; a rewrite that is not clearly faster is not worth its risk.
    assert quick < slow / 2.0, f"reference={slow:.3f}s vectorised={quick:.3f}s"
    print(f"\nreplay: reference={slow:.3f}s vectorised={quick:.3f}s speedup={slow/quick:.1f}x")


def test_everything_the_sweep_pool_ships_to_a_worker_is_picklable(monkeypatch, tmp_path):
    # A ProcessPoolExecutor fails at RUNTIME on an unpicklable initarg, i.e. an hour into a run. The
    # settings dataclass and the cohort payload cross that boundary, so pin them here instead.
    import pickle

    settings = modeler_settings(monkeypatch, BUILD_LAB_ARTIFACT_DIR=str(tmp_path))
    changes = PatchChangeSet(items=frozenset({3031}), runes=frozenset(), champions=frozenset({22}))
    cohort = {
        "included_patches": ["16.15"],
        "cutoff": datetime(2026, 8, 4, tzinfo=timezone.utc),
        "rank_offset": "ObservationOffsetSeconds",
        "current_patch": "16.15",
        "changed_items": {3031},
        "changes": changes,
        "archetypes": {22: "marksman"},
        "artifact_path": str(tmp_path),
    }

    assert pickle.loads(pickle.dumps(settings)) == settings
    restored = pickle.loads(pickle.dumps(cohort))
    assert restored["changes"] == changes
    assert restored["changed_items"] == {3031}
    # The task function itself must be importable by name, or the pool cannot dispatch it.
    assert pipeline.sweep_champion.__module__ == "build_lab_modeler.pipeline"
    assert pickle.loads(pickle.dumps(pipeline.sweep_champion)) is pipeline.sweep_champion


def test_the_sequential_sweep_reuses_the_connection_it_was_given(monkeypatch, tmp_path):
    # Opening a second connection for an in-process sweep wastes a slot against a database shared with
    # the live app, and would make the publish path untestable without a real server.
    settings = modeler_settings(monkeypatch, BUILD_LAB_ARTIFACT_DIR=str(tmp_path))
    sentinel = FakeConnection()
    # What the stream draws is another test's business; this one is about the wiring, so it is stubbed
    # rather than serviced by a fake cursor.
    monkeypatch.setattr(
        pipeline, "load_participant_teams", lambda *_, **__: pd.DataFrame()
    )
    monkeypatch.setattr(
        pipeline,
        "stream_cohort_event_state",
        lambda *a, **k: pipeline.CohortEventState(pipeline.empty_event_state_rows(), {}, frozenset()),
    )
    # The cohort carries the patches and cutoff because setup also streams the sweep's one pass over
    # the cohort's event state here, on this same connection -- which is the point of reusing it.
    pipeline._sweep_worker_setup(
        settings,
        {
            "artifact_path": str(tmp_path),
            "included_patches": ["16.15"],
            "cutoff": datetime(2026, 8, 8, 2, 15, tzinfo=timezone.utc),
        },
        "unused.joblib",
        connection=sentinel, bundle={"model": None},
    )
    assert pipeline._WORKER["connection"] is sentinel
    assert pipeline._WORKER["owns_connection"] is False
    assert pipeline._WORKER["bundle"] == {"model": None}
    assert "event_state" in pipeline._WORKER


def test_the_modeler_tunes_only_its_own_session(monkeypatch, tmp_path):
    """These are SET, never ALTER SYSTEM: the server's own settings are sized for the web app and
    must stay that way. A session-scoped budget lets the sweep have a bigger one without every
    other client inheriting it."""
    settings = modeler_settings(
        monkeypatch,
        BUILD_LAB_ARTIFACT_DIR=str(tmp_path),
        BUILD_LAB_SESSION_WORK_MEM="64MB",
        BUILD_LAB_SESSION_PARALLEL_WORKERS="3",
    )
    connection = FakeConnection()

    pipeline.tune_session(connection, settings)

    issued = [statement for statement, _ in connection.statements]
    assert "SET work_mem = '64MB'" in issued
    assert "SET max_parallel_workers_per_gather = 3" in issued
    assert not any("ALTER SYSTEM" in statement for statement in issued)


def _read_state_in_child(path: str) -> tuple:
    """Module-level so `spawn` can import it; a closure would not pickle."""
    from build_lab_modeler.pipeline import read_cohort_event_state
    state = read_cohort_event_state(Path(path))
    return len(state.cumulative), dict(state.match_codes), set(state.covered_matches)


def test_the_shared_state_survives_a_spawned_process(tmp_path):
    """The pool uses `spawn`, so the worker is a fresh interpreter that inherits nothing.

    Everything it needs must therefore either pickle through initargs or be reachable on disk. This
    exercises the real mechanism -- a genuinely separate process reading the file the parent wrote --
    rather than trusting that an in-process call proves it.
    """
    import multiprocessing
    from concurrent.futures import ProcessPoolExecutor

    state = pipeline.CohortEventState(
        pd.DataFrame(
            {
                "match_code": pd.Series([1, 2], dtype="int32"),
                "team_id": pd.Series([100, 200], dtype="int32"),
                "timestamp_ms": pd.Series([10, 20], dtype="int64"),
                "kills": pd.Series([1, 2], dtype="int32"),
                "towers": pd.Series([0, 1], dtype="int32"),
                "objectives": pd.Series([1, 0], dtype="int32"),
            }
        ),
        {"m1": 1, "m2": 2},
        frozenset({"m1", "m2"}),
    )
    path = pipeline.write_cohort_event_state(state, tmp_path / "state")

    with ProcessPoolExecutor(
        max_workers=1, mp_context=multiprocessing.get_context("spawn")
    ) as pool:
        rows, codes, covered = pool.submit(_read_state_in_child, str(path)).result(timeout=120)

    assert rows == 2
    assert codes == {"m1": 1, "m2": 2}
    assert covered == {"m1", "m2"}


def test_the_pool_initargs_are_picklable(monkeypatch, tmp_path):
    """spawn pickles initargs; a Settings field that cannot pickle fails only in production, and
    only after the structural fit has already been paid for."""
    import pickle

    settings = modeler_settings(monkeypatch, BUILD_LAB_ARTIFACT_DIR=str(tmp_path))
    cohort = {
        "included_patches": ["16.15", "16.16"],
        "cutoff": datetime(2026, 8, 14, 2, 15, tzinfo=timezone.utc),
        "rank_offset": None,
        "current_patch": "16.16",
        "changed_items": set(),
        "changes": {},
        "archetypes": {},
        "artifact_path": str(tmp_path),
    }
    restored_settings, restored_cohort = pickle.loads(pickle.dumps((settings, cohort)))
    assert restored_settings.sweep_workers == settings.sweep_workers
    assert restored_settings.session_work_mem == settings.session_work_mem
    assert restored_cohort["included_patches"] == ["16.15", "16.16"]


def test_the_reduced_state_stays_narrow_enough_to_replicate(tmp_path):
    """The reduced state is held by the parent and read again by every sweep worker, so its width
    is multiplied by the worker count. At int64 throughout a 32.4M-row cohort cost 1,166 MB per
    copy, and parent-plus-four-workers OOM-killed a worker three nights running."""
    assert pipeline.EVENT_STATE_DTYPES["match_code"] == "int32"
    assert pipeline.EVENT_STATE_DTYPES["timestamp_ms"] == "int32"
    for counter in ("kills", "towers", "objectives"):
        assert pipeline.EVENT_STATE_DTYPES[counter] == "int16"
    width = sum(
        int(dtype.removeprefix("int")) // 8 for dtype in pipeline.EVENT_STATE_DTYPES.values()
    )
    assert width <= 16, f"the reduced state widened to {width} bytes/row; it is replicated per worker"
    # The declared shape actually uses them.
    assert dict(pipeline.empty_event_state_rows().dtypes.astype(str)) == pipeline.EVENT_STATE_DTYPES


def test_both_sides_of_the_event_state_merge_share_one_width(tmp_path):
    """merge_asof raises MergeError when `on` or `by` differ in width between the two frames, so
    narrowing the state alone silently breaks the join -- which is exactly what happened when this
    was first narrowed. Both sides must derive from EVENT_STATE_DTYPES."""
    source = inspect.getsource(pipeline.apply_event_state)
    for key in ("match_code", "team_id", "timestamp_ms"):
        assert f'EVENT_STATE_DTYPES["{key}"]' in source, (
            f"apply_event_state casts {key} to a literal width instead of the shared declaration"
        )
    assert '.astype("int64")' not in source, "a hardcoded width crept back into the merge"


def test_the_cohort_event_state_round_trips_through_the_shared_file(tmp_path):
    """The pool and the sequential reference must sweep against identical state.

    Workers no longer derive this themselves -- the parent reduces it once and hands over a
    directory -- so anything lost in the round trip is a silent divergence between the two paths
    rather than a crash.
    """
    cumulative = pd.DataFrame(
        {
            "match_code": pd.Series([1, 1, 2], dtype="int32"),
            "team_id": pd.Series([100, 200, 100], dtype="int32"),
            "timestamp_ms": pd.Series([1000, 2000, 3000], dtype="int64"),
            "kills": pd.Series([1, 0, 2], dtype="int32"),
            "towers": pd.Series([0, 1, 0], dtype="int32"),
            "objectives": pd.Series([0, 0, 1], dtype="int32"),
        }
    )
    state = pipeline.CohortEventState(
        cumulative,
        {"aaaaaaaa-0000-4000-8000-000000000001": 1, "aaaaaaaa-0000-4000-8000-000000000002": 2},
        frozenset({"aaaaaaaa-0000-4000-8000-000000000001"}),
    )

    restored = pipeline.read_cohort_event_state(
        pipeline.write_cohort_event_state(state, tmp_path / "state")
    )

    pd.testing.assert_frame_equal(restored.cumulative, state.cumulative)
    assert restored.match_codes == state.match_codes
    assert restored.covered_matches == state.covered_matches
    # Codes are looked up by string id and compared as ints; parquet must not hand back numpy types
    # that break either.
    assert all(isinstance(key, str) for key in restored.match_codes)
    assert all(isinstance(value, int) for value in restored.match_codes.values())


def test_an_empty_cohort_event_state_round_trips_too(tmp_path):
    state = pipeline.CohortEventState(pipeline.empty_event_state_rows(), {}, frozenset())
    restored = pipeline.read_cohort_event_state(
        pipeline.write_cohort_event_state(state, tmp_path / "state")
    )
    assert restored.cumulative.empty
    assert restored.match_codes == {}
    assert restored.covered_matches == frozenset()


def test_a_worker_given_a_state_path_reads_it_instead_of_scanning(monkeypatch, tmp_path):
    """A spawned worker must never re-derive the cohort state: that is one 16M-row scan per worker,
    which is what made the pool slower than sequential."""
    scanned = []
    monkeypatch.setattr(
        pipeline, "stream_cohort_event_state",
        lambda *a, **k: scanned.append(1) or pipeline.CohortEventState(
            pipeline.empty_event_state_rows(), {}, frozenset()
        ),
    )
    monkeypatch.setattr(pipeline, "load_participant_teams", lambda *a, **k: pd.DataFrame())
    state = pipeline.CohortEventState(
        pd.DataFrame({column: pd.Series([1], dtype="int64") for column in pipeline.EVENT_STATE_COLUMNS}),
        {"m1": 1},
        frozenset({"m1"}),
    )
    path = pipeline.write_cohort_event_state(state, tmp_path / "state")

    pipeline._sweep_worker_setup(
        modeler_settings(monkeypatch, BUILD_LAB_ARTIFACT_DIR=str(tmp_path)),
        {"artifact_path": str(tmp_path), "included_patches": ["16.15"],
         "cutoff": datetime(2026, 8, 8, 2, 15, tzinfo=timezone.utc)},
        "unused.joblib",
        connection=object(), bundle={"model": None},
        event_state_path=str(path),
    )

    assert scanned == [], "the worker scanned the cohort instead of reading the shared file"
    assert pipeline._WORKER["event_state"].match_codes == {"m1": 1}


def test_the_sweep_worker_count_is_configurable_and_floored(monkeypatch, tmp_path):
    # Defaults to the container's cpu quota, not to 1 and not to nproc.
    #
    # Sequential used to be the default on measurement -- three workers took 1349s against 432s
    # sequential -- but that was because every worker streamed its own copy of the cohort event
    # state, so N workers meant N concurrent 16M-row scans of a spinning disk. The parent now
    # reduces that state once and shares it as a file, which is the condition the original comment
    # named for revisiting this. It still can never drop below one.
    assert (
        modeler_settings(monkeypatch, BUILD_LAB_ARTIFACT_DIR=str(tmp_path)).sweep_workers
        == pipeline.container_cpu_quota()
    )
    assert modeler_settings(
        monkeypatch, BUILD_LAB_ARTIFACT_DIR=str(tmp_path), BUILD_LAB_SWEEP_WORKERS="8"
    ).sweep_workers == 8
    assert modeler_settings(
        monkeypatch, BUILD_LAB_ARTIFACT_DIR=str(tmp_path), BUILD_LAB_SWEEP_WORKERS="0"
    ).sweep_workers == 1


class BusyConnection(FakeConnection):
    """psycopg's shape when a result is still unconsumed.

    A server-side cursor mid-stream, or an execute interrupted by a signal, leaves the connection
    refusing every further statement -- and `rollback()` does not clear it, because the problem is
    an unconsumed result rather than an aborted transaction.
    """

    def rollback(self) -> None:
        raise psycopg.OperationalError("another command is already in progress")

    def execute(self, statement: str, parameters=None):
        raise psycopg.OperationalError("another command is already in progress")


def test_the_failure_reason_survives_a_connection_that_is_mid_command(monkeypatch, tmp_path):
    """The reaper would otherwise stamp a generic 'exited without finishing' over the real reason,
    which is the one thing worth keeping from a failed run."""
    settings = modeler_settings(monkeypatch, BUILD_LAB_ARTIFACT_DIR=str(tmp_path))
    fresh = FakeConnection()
    opened = []

    class _Ctx:
        def __enter__(self): return fresh
        def __exit__(self, *_): return False

    monkeypatch.setattr(
        pipeline.psycopg, "connect", lambda *a, **k: opened.append(1) or _Ctx()
    )

    pipeline.mark_failed_safely(
        BusyConnection(), "11111111-2222-3333-4444-555555555555",
        "the real reason", "host:1", settings,
    )

    assert opened == [1], "a busy connection must be replaced, not given up on"
    written = [(sql, params) for sql, params in fresh.statements if "BuildLabGenerations" in sql]
    assert written, "the failure was never recorded on the fresh connection"
    sql, params = written[0]
    assert "the real reason" in params
    # The lease guard must survive the retry: a second connection holds no advisory lock, so the
    # WHERE clause is the only thing stopping it closing out someone else's generation.
    assert '"Status" = 1' in sql and '"LeaseOwner" = %s' in sql and 'NOT "IsActive"' in sql


def test_a_healthy_connection_writes_the_failure_without_opening_another(monkeypatch, tmp_path):
    settings = modeler_settings(monkeypatch, BUILD_LAB_ARTIFACT_DIR=str(tmp_path))
    opened = []
    monkeypatch.setattr(pipeline.psycopg, "connect", lambda *a, **k: opened.append(1))
    connection = FakeConnection()

    pipeline.mark_failed_safely(
        connection, "11111111-2222-3333-4444-555555555555", "boom", "host:1", settings
    )

    assert opened == [], "a working connection must not be replaced"
    assert any("BuildLabGenerations" in sql for sql, _ in connection.statements)


def test_an_empty_env_var_falls_back_to_the_default(monkeypatch, tmp_path):
    """Compose renders `${VAR:-}` as an empty string, not as an unset variable.

    `os.getenv(name, default)` therefore returns "" and the default never applies, so every
    int()/float() around it raises. This is how `BUILD_LAB_SWEEP_WORKERS: ${BUILD_LAB_SWEEP_WORKERS:-}`
    -- the intended way to spell "use the computed default" -- crashed the modeler at startup.
    """
    monkeypatch.setenv("BUILD_LAB_SWEEP_WORKERS", "")
    monkeypatch.setenv("BUILD_LAB_SESSION_WORK_MEM", "")
    monkeypatch.setenv("BUILD_LAB_TRAINING_DRAW_MAX_AGE_HOURS", "   ")

    settings = modeler_settings(monkeypatch, BUILD_LAB_ARTIFACT_DIR=str(tmp_path))

    assert settings.sweep_workers == pipeline.container_cpu_quota()
    assert settings.session_work_mem == "128MB"
    assert settings.training_draw_max_age_hours == 36.0
    # An explicit value still wins over the default.
    monkeypatch.setenv("BUILD_LAB_SWEEP_WORKERS", "6")
    assert modeler_settings(monkeypatch, BUILD_LAB_ARTIFACT_DIR=str(tmp_path)).sweep_workers == 6


def test_the_cpu_quota_is_read_from_the_cgroup_not_from_nproc(monkeypatch, tmp_path):
    """nproc reports the HOST's cores inside a container, which is what oversubscribed OpenBLAS in
    #167 and killed the worker pool before that. The quota is the only number that describes what
    this process may actually use."""
    quota_file = tmp_path / "cpu.max"
    quota_file.write_text("300000 100000")  # 3 cpus
    monkeypatch.setattr(pipeline.pathlib, "Path", lambda _: quota_file)
    assert pipeline.container_cpu_quota() == 3

    quota_file.write_text("1600000 100000")  # 16 cpus
    assert pipeline.container_cpu_quota() == 16

    # An unlimited cgroup falls back rather than raising.
    quota_file.write_text("max 100000")
    monkeypatch.undo()
    assert pipeline.container_cpu_quota() >= 1


def test_a_reused_training_draw_is_bounded_by_age_and_never_from_the_future(tmp_path):
    from build_lab_modeler.cache import TrainingCache

    ranges = [("019fb140-0000-7000-8000-000000000000", "019fb141-0000-7000-8000-000000000000")]
    drawn_at = datetime(2026, 8, 4, 6, 0, tzinfo=timezone.utc)
    cache = TrainingCache.for_cohort(
        tmp_path, ["16.15"], ranges, 500, cutoff=drawn_at, max_age_hours=36
    )
    assert cache.drawn_at() is None, "nothing cached yet, so nothing to reuse"
    assert cache.is_fresh_for(drawn_at) is False
    cache.write_slice(0, pd.DataFrame({"won": [True, False], "minute": [4.0, 21.0]}))
    assert cache.drawn_at() == drawn_at

    # Same cutoff, and a later one inside the bound, may reuse it.
    assert cache.is_fresh_for(drawn_at) is True
    assert cache.is_fresh_for(drawn_at + timedelta(hours=30)) is True
    # Beyond the bound the fit would drift arbitrarily far behind the cohort it scores.
    assert cache.is_fresh_for(drawn_at + timedelta(hours=40)) is False
    # And never a draw taken AFTER the cutoff being modelled: it holds matches the generation excludes.
    assert cache.is_fresh_for(drawn_at - timedelta(hours=1)) is False
    # Reuse across cutoffs can be switched off entirely.
    strict = TrainingCache.for_cohort(
        tmp_path, ["16.15"], ranges, 500, cutoff=drawn_at, max_age_hours=0
    )
    assert strict.is_fresh_for(drawn_at + timedelta(hours=1)) is False


def test_the_timeline_loader_extracts_payload_scalars_in_sql():
    # Parsing PayloadJson in pandas cost one json.loads plus three dict walks per event -- roughly
    # 290,000 per champion -- and was the dominant cost of the sweep. PayloadJson is jsonb, so Postgres
    # reads the three scalars directly. Nothing downstream may reach for the raw payload again.
    source = inspect.getsource(timeline_state_events_query)
    for field in ("killer_participant_id", "killer_team_id", "owner_team_id"):
        assert field in source, field
    # Both spellings, matching the case-insensitive lookup this replaced.
    for key in ("'killerId'", "'killerid'", "'killerTeamId'", "'killerteamid'", "'teamId'", "'teamid'"):
        assert f"->> {key}" in source, key
    assert "payload_json" not in source
    # Checks for CALLS, not mentions: the docstring names what it replaced.
    attribution = inspect.getsource(pipeline.attribute_events_to_teams)
    assert "json.loads(" not in attribution
    assert "payload_value(" not in attribution


def test_team_attribution_treats_a_zero_id_as_absent_like_positive_int_did():
    # positive_int's contract: zero and negatives mean "no id", not id zero. The SQL extraction returns
    # text, so that filter has to survive the move or minion-killed buildings get miscredited.
    events = pd.DataFrame([
        {"match_id": "m", "event_index": 0, "timestamp_ms": 10, "event_type": "CHAMPION_KILL",
         "killer_participant_id": "0", "killer_team_id": "0", "owner_team_id": None},
        {"match_id": "m", "event_index": 1, "timestamp_ms": 20, "event_type": "BUILDING_KILL",
         "killer_participant_id": "0", "killer_team_id": None, "owner_team_id": "200"},
        {"match_id": "m", "event_index": 2, "timestamp_ms": 30, "event_type": "CHAMPION_KILL",
         "killer_participant_id": "7", "killer_team_id": None, "owner_team_id": None},
    ])
    teams = pd.DataFrame([{"match_id": "m", "participant_id": 7, "team_id": 200}])

    scored = pipeline.attribute_events_to_teams(events, teams.astype({"participant_id": int, "team_id": int}))

    # Row 0: killer 0 and team 0 are both absent, so nothing to credit -- dropped.
    # Row 1: minions felled team 200's building, so team 100 is credited.
    # Row 2: killer 7 resolves through the roster to team 200.
    assert scored["event_index"].tolist() == [1, 2]
    assert scored.loc[scored["event_index"] == 1, "team_id"].item() == 100
    assert scored.loc[scored["event_index"] == 1, "towers"].item() == 1
    assert scored.loc[scored["event_index"] == 2, "team_id"].item() == 200
    assert scored.loc[scored["event_index"] == 2, "kills"].item() == 1


def _fitted_blas_threads() -> int:
    """Do real BLAS work so the process has live worker threads, as the structural fit leaves it."""
    import numpy as _np

    matrix = _np.random.default_rng(0).random((600, 600))
    _np.linalg.svd(matrix, full_matrices=False)
    return 1


def _pool_probe(value: int) -> int:
    import numpy as _np

    return int(_np.asarray([value]).sum())


def test_the_sweep_pool_does_not_inherit_a_forked_lock_and_deadlock():
    """A pool started after BLAS work must still run.

    The sweep forks straight after the structural fit, which leaves BLAS/OpenMP threads behind. fork()
    inherits mutex STATE, so a lock held by one of those threads arrives already-locked in the child and
    is never released -- on prod that deadlocked all four workers for 45 minutes without a single
    champion completing, and no single-worker test could have seen it. This starts a real pool the same
    way the sweep does and requires it to finish.
    """
    from concurrent.futures import ProcessPoolExecutor

    _fitted_blas_threads()

    with ProcessPoolExecutor(
        max_workers=2, mp_context=multiprocessing.get_context("spawn")
    ) as pool:
        results = list(pool.map(_pool_probe, [1, 2, 3, 4]))

    assert results == [1, 2, 3, 4]


def test_the_sweep_pool_is_configured_for_spawn():
    # Belt and braces around the deadlock above: the source must name the start method, because the
    # platform default on Linux is fork and the failure is silent -- a hang, not an error.
    source = inspect.getsource(pipeline.model_generation)
    assert 'mp_context=multiprocessing.get_context("spawn")' in source
    assert "ProcessPoolExecutor(" in source


def test_the_sweep_caps_worker_thread_pools_before_spawning():
    # nproc inside a container reports the HOST's cores regardless of the cpu quota, so each spawned
    # interpreter would size an OpenBLAS pool at ~46 threads. Four of those against a 3-cpu quota killed
    # the pool outright with std::system_error: Resource temporarily unavailable. Children read these at
    # numpy import, so they must be set before the pool starts, not inside the worker.
    source = inspect.getsource(pipeline.model_generation)
    for variable in ("OMP_NUM_THREADS", "OPENBLAS_NUM_THREADS", "MKL_NUM_THREADS"):
        assert variable in source, variable
    capped = source.index("OMP_NUM_THREADS")
    spawned = source.index("ProcessPoolExecutor(")
    assert capped < spawned, "thread caps must be set before the pool is created"
    # setdefault, so an operator can still override it per deployment.
    assert "os.environ.setdefault(" in source
