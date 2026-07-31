import inspect
import json
import logging
import re
from datetime import datetime, timezone
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
    GenerationHeartbeat,
    LeaseLost,
    PatchChangeSet,
    Settings,
    apply_partial_pooling,
    apply_row_weights,
    commensurability_weights,
    average_timing,
    build_action_estimates,
    build_item_decisions,
    build_design_spec,
    build_path_estimates,
    build_rune_decisions,
    build_spell_decisions,
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
    load_item_events,
    load_rune_decisions,
    load_spell_decisions,
    load_timeline_state_events,
    mark_failed,
    maximum_weighted_smd,
    model_generation,
    participant_level_comparator,
    patch_recency_weights,
    prune_stale_artifacts,
    rank_context_lateral,
    retained_generation_ids,
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

    def executemany(self, statement: str, rows: list) -> FakeCursor:
        self.statements.append((statement, rows))
        self._stage(statement, rows)
        return FakeCursor(len(rows))

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
            {"match_id": "match", "event_index": 0, "timestamp_ms": 999, "event_type": "CHAMPION_KILL", "payload_json": {"KillerId": 1}},
            {"match_id": "match", "event_index": 1, "timestamp_ms": 1000, "event_type": "CHAMPION_KILL", "payload_json": {"KillerId": 6}},
            {"match_id": "match", "event_index": 2, "timestamp_ms": 1500, "event_type": "ELITE_MONSTER_KILL", "payload_json": {"KillerId": 6}},
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
                "payload_json": {"killerId": 0, "teamId": 200},
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
                "payload_json": {"killerId": 1},
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
                "payload_json": {"killerId": 1},
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

    expanded = expand_scopes(frame)
    scopes = set(zip(expanded["scope_opponent_id"], expanded["scope_region"]))

    assert (0, "GLOBAL") in scopes
    assert (51, "GLOBAL") in scopes
    assert (0, "NA1") in scopes
    assert (51, "NA1") in scopes


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
        load_timeline_state_events,
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
    assert settings.heartbeat_seconds <= settings.lease_seconds // 4


def test_execute_guarded_treats_a_zero_rowcount_as_a_lost_lease():
    held = FakeConnection([FakeCursor(1)])
    execute_guarded(held, 'UPDATE "BuildLabGenerations" SET "Status" = 2', (1,), "lease is gone")

    reclaimed = FakeConnection([FakeCursor(0)])
    with pytest.raises(LeaseLost, match="lease is gone"):
        execute_guarded(
            reclaimed, 'UPDATE "BuildLabGenerations" SET "Status" = 2', (1,), "lease is gone"
        )


def test_every_guarded_status_write_checks_its_rowcount():
    for function in (pipeline.lease_generation, mark_failed, GenerationHeartbeat._renew):
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
    monkeypatch.setattr(pipeline, "load_item_events", lambda *_: item_events)
    monkeypatch.setattr(pipeline, "load_timeline_state_events", lambda *_: empty)
    monkeypatch.setattr(pipeline, "load_participant_teams", lambda *_: empty)
    monkeypatch.setattr(pipeline, "load_rune_decisions", lambda *_: empty)
    monkeypatch.setattr(pipeline, "load_spell_decisions", lambda *_: empty)
    monkeypatch.setattr(
        pipeline,
        "train_structural_model",
        lambda *_: ({"model": None, "calibrator": None}, {"overallEce": 0.01}),
    )
    monkeypatch.setattr(
        pipeline, "structural_win_probability", lambda bundle, frame: np.full(len(frame), 0.5)
    )
    monkeypatch.setattr(pipeline, "build_action_estimates", lambda *_: [(uuid4(), "estimate")])
    monkeypatch.setattr(pipeline, "build_path_estimates", lambda *_: [])
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


class LostLeaseHeartbeat:
    """A heartbeat that has already proved the lease is gone."""

    lease_lost = True

    def raise_if_lease_lost(self) -> None:
        raise LeaseLost("the modeling lease was reclaimed")


def test_a_lease_lost_before_publishing_writes_no_artifacts_and_no_estimates(tmp_path, monkeypatch):
    settings, generation = publishing_generation(monkeypatch, tmp_path)
    connection = FakeConnection()

    with pytest.raises(LeaseLost):
        model_generation(connection, generation, settings, LostLeaseHeartbeat())

    assert not (tmp_path / str(generation["Id"])).exists()
    assert [row for row in connection.statements if "AdjustedActionEstimates" in row[0]] == []


def test_the_claimer_owns_the_lease_deadline_and_a_reclaimed_lease_stops_the_heartbeat(monkeypatch):
    settings = modeler_settings(monkeypatch)
    heartbeat = GenerationHeartbeat(settings, uuid4())
    renewed = FakeConnection([FakeCursor(1)])
    monkeypatch.setattr(pipeline.psycopg, "connect", lambda *_, **__: renewed)

    assert heartbeat._renew() is True
    heartbeat.raise_if_lease_lost()
    statement, parameters = renewed.statements[-1]
    # The claimer, not the reaper, owns LeaseExpiresAtUtc: every renewal moves the deadline forward,
    # so the reaper's LeaseTimeoutMinutes only governs a lease that never wrote one.
    assert '"LeaseExpiresAtUtc" = NOW() + make_interval(secs => %s)' in statement
    assert '"Status" = 1' in statement and '"LeaseOwner" = %s' in statement
    assert parameters[0] == settings.lease_seconds
    assert 'NOW() + make_interval(secs => %s)' in inspect.getsource(pipeline.lease_generation)

    reclaimed = FakeConnection([FakeCursor(0)])
    monkeypatch.setattr(pipeline.psycopg, "connect", lambda *_, **__: reclaimed)
    assert heartbeat._renew() is False
    assert heartbeat.lease_lost is True
    with pytest.raises(LeaseLost):
        heartbeat.raise_if_lease_lost()


def test_a_transport_error_is_never_read_as_a_lost_lease(monkeypatch):
    settings = modeler_settings(monkeypatch)
    heartbeat = GenerationHeartbeat(settings, uuid4())

    def unreachable(*_, **__):
        raise OSError("the database is unreachable")

    monkeypatch.setattr(pipeline.psycopg, "connect", unreachable)

    assert heartbeat._renew() is True
    assert heartbeat.lease_lost is False


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
    assert len(dependency_layers) >= 4, dependency_layers
    for package in ("numpy", "pandas", "scikit-learn", "pyarrow"):
        owning = [line for line in dependency_layers if package in line]
        assert len(owning) == 1, f"{package} must be installed in exactly one layer: {owning}"


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
