from __future__ import annotations

import hashlib
import hmac
import json
import logging
import math
import os
import re
import shutil
import signal
import socket
import threading
import time
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable, Sequence
from uuid import UUID, uuid4

import boto3
import joblib
import numpy as np
import pandas as pd
import psycopg
from psycopg.rows import dict_row
from sklearn.isotonic import IsotonicRegression
from sklearn.linear_model import LogisticRegression
from sklearn.metrics import brier_score_loss, log_loss
from sklearn.pipeline import make_pipeline
from sklearn.preprocessing import StandardScaler

LOG = logging.getLogger("build_lab_modeler")
logging.basicConfig(level=os.getenv("LOG_LEVEL", "INFO"))

EMERALD_PLUS = ("EMERALD", "DIAMOND", "MASTER", "GRANDMASTER", "CHALLENGER")
# Mirrors MatchTimelineIngestionJob.CurrentTimelineSchemaVersion. v1 rows zero-fill the columns
# this pipeline conditions on, so every source query filters on it explicitly.
TIMELINE_SCHEMA_VERSION = 2
# BuildItemCategory: 0 Legendary, 1 Boots, 2 Starter. Anything else (consumables, wards, trinkets,
# mid-game components) is not build relevant and must never enter the inventory state.
BUILD_ITEM_CATEGORIES = (0, 1, 2)
PREGAME_FAMILIES = ("RUNE_PAGE", "RUNE", "SPELL")
# Families whose published average timing is meaningful. FIRST_ITEM_PATH belongs here: it is the item
# stage the champion page surfaces, so excluding it renders that card's timing permanently empty.
TIMED_FAMILIES = ("STARTER", "BOOTS", "ITEM", "FIRST_ITEM_PATH")
FEATURE_COLUMNS = [
    "minute",
    "gold",
    "current_gold",
    "xp",
    "cs",
    "lane_cs",
    "jungle_cs",
    "level",
    "team_gold_diff",
    "team_xp_diff",
    "team_cs_diff",
    "team_kill_diff",
    "team_tower_diff",
    "team_objective_diff",
    # Rune and spell decisions are pregame, so every in-game fact above is genuinely absent rather
    # than zero. The indicator makes that absence explicit instead of letting the model read the
    # zeros as an observed early-game state.
    "has_predecision_state",
    # A timeline row is stamped schema v2 even when Analytics:BuildLab:Enabled was off during
    # ingestion, in which case no event payloads were written at all. Without this indicator the
    # kill/tower/objective diffs of a payload-less match are indistinguishable from a genuinely even
    # game.
    "has_event_state",
]
# Identity-like facts are encoded into bounded vocabularies rather than one-hot expanded, so the
# design matrix width never grows with the row count.
MAX_CHAMPION_VOCABULARY = 200
MAX_ITEM_VOCABULARY = 192
MAX_ROLE_VOCABULARY = 8
MAX_PATCH_VOCABULARY = 4
MAX_REGION_VOCABULARY = 24
DESIGN_MATRIX_MAX_COLUMNS = (
    len(FEATURE_COLUMNS)
    + 4 * MAX_CHAMPION_VOCABULARY
    + MAX_ITEM_VOCABULARY
    + MAX_ROLE_VOCABULARY
    + MAX_PATCH_VOCABULARY
    + MAX_REGION_VOCABULARY
)
# A column only carries balance information when both arms observe it varying often enough. Without
# this floor a dummy that is set on a handful of rows turns the balance gate into a weight-
# concentration gate that contradicts the sibling overlap gate.
MINIMUM_BALANCE_SUPPORT = 25
POST_OUTCOME_COLUMNS = ("won", "win", "outcome", "raw_win_rate", "match_result")
# Prior spread of an action's true lift before any pooling, in win-probability points.
GLOBAL_PRIOR_VARIANCE = 0.02**2
# A rank observed after the match started can encode the outcome, so those rows are kept but
# discounted rather than allowed to carry full weight.
POST_MATCH_RANK_WEIGHT = 0.25
EMPTY_PATH_HASH = hashlib.sha256(b"").hexdigest()
ID_PATTERN = re.compile(r"\d+")


class ShutdownRequested(RuntimeError):
    """Raised from the SIGTERM/SIGINT handler so an in-flight generation is marked failed."""


class LeaseLost(RuntimeError):
    """Raised when a guarded write matches no row, so this process no longer owns the generation."""


@dataclass(frozen=True)
class Settings:
    database_url: str
    artifact_dir: Path
    poll_seconds: int
    run_once: bool
    s3_endpoint: str | None
    s3_bucket: str | None
    s3_access_key: str | None
    s3_secret_key: str | None
    deidentification_salt: str
    lease_owner: str
    lease_seconds: int
    heartbeat_seconds: int
    max_training_rows: int
    retained_generations: int

    @classmethod
    def from_env(cls) -> "Settings":
        database_url = os.environ.get("BUILD_LAB_DATABASE_URL")
        if not database_url:
            raise RuntimeError("BUILD_LAB_DATABASE_URL is required.")
        salt = os.environ.get("BUILD_LAB_DEIDENTIFICATION_SALT")
        if not salt or len(salt) < 32:
            raise RuntimeError(
                "BUILD_LAB_DEIDENTIFICATION_SALT is required and must be at least 32 characters. "
                "It must never be derivable from anything published beside the export."
            )
        lease_seconds = max(120, int(os.getenv("BUILD_LAB_LEASE_SECONDS", "900")))
        return cls(
            database_url=database_url,
            artifact_dir=Path(os.getenv("BUILD_LAB_ARTIFACT_DIR", "/artifacts")),
            poll_seconds=max(30, int(os.getenv("BUILD_LAB_POLL_SECONDS", "300"))),
            run_once=os.getenv("BUILD_LAB_RUN_ONCE", "false").lower() == "true",
            s3_endpoint=os.getenv("BUILD_LAB_S3_ENDPOINT") or None,
            s3_bucket=os.getenv("BUILD_LAB_S3_BUCKET") or None,
            s3_access_key=os.getenv("BUILD_LAB_S3_ACCESS_KEY") or None,
            s3_secret_key=os.getenv("BUILD_LAB_S3_SECRET_KEY") or None,
            deidentification_salt=salt,
            lease_owner=os.getenv("BUILD_LAB_LEASE_OWNER")
            or f"{socket.gethostname()}:{os.getpid()}",
            lease_seconds=lease_seconds,
            heartbeat_seconds=max(15, min(lease_seconds // 4, int(
                os.getenv("BUILD_LAB_HEARTBEAT_SECONDS", "60")
            ))),
            max_training_rows=max(20_000, int(os.getenv("BUILD_LAB_MAX_TRAINING_ROWS", "250000"))),
            # Mirrors BuildLabModelingOptions.RetainedGenerations and the Math.Max(2, ...) floor the
            # coordinator applies, so artifact retention and row retention keep the same set.
            retained_generations=max(2, int(os.getenv("BUILD_LAB_RETAINED_GENERATIONS", "4"))),
        )


def run() -> None:
    settings = Settings.from_env()
    settings.artifact_dir.mkdir(parents=True, exist_ok=True)
    install_shutdown_handlers()
    while True:
        try:
            processed = process_next(settings)
            if settings.run_once:
                return
            if not processed:
                # The signal handler raises here too, so a container stop during the idle poll exits
                # cleanly instead of unwinding as an unhandled error.
                time.sleep(settings.poll_seconds)
        except ShutdownRequested:
            LOG.info("Shutdown requested; the modeler is stopping.")
            return
        except psycopg.OperationalError as exc:
            # The database is unreachable or restarting. Any lease taken before the drop is left for
            # the .NET reaper to expire, so retry on the normal cadence rather than letting the
            # container crash-loop through its restart policy on every Postgres bounce.
            LOG.warning("Build Lab modeler could not reach the database: %s", exc)
            if settings.run_once:
                raise
            time.sleep(settings.poll_seconds)


def install_shutdown_handlers() -> None:
    def handle(signum, _frame):
        raise ShutdownRequested(f"The modeler received signal {signum} and stopped mid-generation.")

    for received in (signal.SIGTERM, signal.SIGINT):
        signal.signal(received, handle)


def process_next(settings: Settings) -> bool:
    with psycopg.connect(settings.database_url, row_factory=dict_row) as connection:
        generation = lease_generation(connection, settings)
        if generation is None:
            return False
        heartbeat = GenerationHeartbeat(settings, generation["Id"])
        heartbeat.start()
        try:
            model_generation(connection, generation, settings, heartbeat)
        except LeaseLost as exc:
            # The row is no longer ours, so everything this run wrote rolled back with its
            # transaction and only the owner that reclaimed it may set a terminal status.
            LOG.warning("Build Lab generation %s: %s", generation["Id"], exc)
        except ShutdownRequested as exc:
            mark_failed_safely(connection, generation["Id"], str(exc), settings.lease_owner)
            raise
        except Exception as exc:
            LOG.exception("Build Lab generation %s failed.", generation["Id"])
            mark_failed_safely(connection, generation["Id"], str(exc), settings.lease_owner)
        finally:
            heartbeat.stop()
        return True


class GenerationHeartbeat:
    """Renews the coordinator lease so the expired-lease reaper never kills a healthy run.

    The claimer owns the lease deadline: it writes LeaseExpiresAtUtc when it claims the generation and
    moves it forward on every renewal. The reaper's LeaseTimeoutMinutes is the outer bound on a
    heartbeat, so a renewal that matches no row means the lease is provably gone and the run must not
    publish anything.
    """

    def __init__(self, settings: Settings, generation_id) -> None:
        self._settings = settings
        self._generation_id = generation_id
        self._stop = threading.Event()
        self._lease_lost = threading.Event()
        self._thread = threading.Thread(target=self._loop, daemon=True)

    def start(self) -> None:
        self._thread.start()

    def stop(self) -> None:
        self._stop.set()
        self._thread.join(timeout=5)

    @property
    def lease_lost(self) -> bool:
        return self._lease_lost.is_set()

    def raise_if_lease_lost(self) -> None:
        if self._lease_lost.is_set():
            raise LeaseLost(
                "the modeling lease was reclaimed while the generation was still being modeled"
            )

    def _loop(self) -> None:
        while not self._stop.wait(self._settings.heartbeat_seconds):
            if not self._renew():
                return

    def _renew(self) -> bool:
        """False only once the lease is provably gone; a transport error is retried, not assumed."""
        try:
            with psycopg.connect(self._settings.database_url) as connection:
                renewed = connection.execute(
                    """
                    UPDATE "BuildLabGenerations"
                    SET "HeartbeatAtUtc" = NOW(),
                        "LeaseExpiresAtUtc" = NOW() + make_interval(secs => %s)
                    WHERE "Id" = %s AND "Status" = 1 AND "LeaseOwner" = %s
                    """,
                    (
                        self._settings.lease_seconds,
                        self._generation_id,
                        self._settings.lease_owner,
                    ),
                )
                connection.commit()
        except Exception:
            LOG.warning("Heartbeat for generation %s failed.", self._generation_id, exc_info=True)
            return True
        if renewed.rowcount == 0:
            self._lease_lost.set()
            LOG.warning(
                "The modeling lease for generation %s is no longer held by %s, so the run will not "
                "publish. Only the reaper or an operator can move the row out from under it.",
                self._generation_id,
                self._settings.lease_owner,
            )
            return False
        return True


def lease_generation(connection: psycopg.Connection, settings: Settings) -> dict | None:
    with connection.transaction():
        generation = connection.execute(
            """
            SELECT *
            FROM "BuildLabGenerations"
            WHERE "Status" = 0
            ORDER BY "CreatedAtUtc"
            FOR UPDATE SKIP LOCKED
            LIMIT 1
            """
        ).fetchone()
        if generation is None:
            return None
        claimed = connection.execute(
            """
            UPDATE "BuildLabGenerations"
            SET "Status" = 1,
                "FailureReason" = NULL,
                "LeaseOwner" = %s,
                "LeaseAcquiredAtUtc" = NOW(),
                "LeaseExpiresAtUtc" = NOW() + make_interval(secs => %s),
                "HeartbeatAtUtc" = NOW()
            WHERE "Id" = %s AND "Status" = 0
            """,
            (settings.lease_owner, settings.lease_seconds, generation["Id"]),
        )
        if claimed.rowcount == 0:
            # The row moved between the lock and the claim, so it belongs to whoever moved it.
            LOG.warning(
                "Generation %s was no longer pending when the lease was claimed.",
                generation["Id"],
            )
            return None
    return generation


def model_generation(
    connection: psycopg.Connection,
    generation: dict,
    settings: Settings,
    heartbeat: "GenerationHeartbeat | None" = None,
) -> None:
    generation_id = UUID(str(generation["Id"]))
    included_patches = json_value(generation["IncludedPatchesJson"])
    regions = json_value(generation["IncludedRegionsJson"])
    cutoff = generation["SourceCutoffUtc"]
    LOG.info("Loading frozen decision data for %s.", generation_id)

    rank_offset = resolve_rank_offset_column(connection)
    item_events = load_item_events(connection, included_patches, cutoff, rank_offset)
    timeline_state_events = load_timeline_state_events(connection, included_patches, cutoff)
    participant_teams = load_participant_teams(connection, included_patches, cutoff)
    rune_events = load_rune_decisions(connection, included_patches, cutoff, rank_offset)
    spell_events = load_spell_decisions(connection, included_patches, cutoff, rank_offset)
    if item_events.empty:
        raise RuntimeError("No eligible item decisions were available for this generation.")

    item_decisions = build_item_decisions(item_events)
    item_decisions = enrich_with_predecision_event_state(
        item_decisions,
        timeline_state_events,
        participant_teams,
    )
    changed_item_ids = load_materially_changed_items(connection, included_patches)
    item_decisions = exclude_incompatible_prior_item_rows(
        item_decisions,
        generation["Patch"],
        changed_item_ids,
    )
    decisions = pd.concat(
        [
            item_decisions,
            build_rune_decisions(rune_events),
            build_spell_decisions(spell_events),
        ],
        ignore_index=True,
    )
    decisions = apply_row_weights(decisions, generation["Patch"], included_patches, rank_offset)
    decisions = exclude_drifted_prior_actions(decisions, generation["Patch"])
    if decisions.empty or decisions["won"].nunique() < 2:
        raise RuntimeError("The frozen dataset does not contain both match outcomes.")
    event_state_coverage = float(decisions["has_event_state"].mean())
    if event_state_coverage <= 0:
        LOG.warning(
            "No match in the frozen cohort carries timeline event payloads. The rows are schema v2 "
            "but were ingested with Analytics:BuildLab:Enabled off, so kill and objective state is "
            "absent rather than even."
        )

    # Checked before the two most expensive stretches of the run: writing the dataset and, below,
    # publishing. A lost lease makes both pure waste, and the second check keeps the orphan window
    # around the terminal write as small as the transaction itself.
    if heartbeat is not None:
        heartbeat.raise_if_lease_lost()

    prune_stale_artifacts(connection, settings, generation_id)
    artifact_path = settings.artifact_dir / str(generation_id)
    artifact_path.mkdir(parents=True, exist_ok=True)
    export = deidentified_export(decisions, settings.deidentification_salt)
    export.to_parquet(
        artifact_path / "dataset",
        index=False,
        partition_cols=["patch", "region"],
    )

    structural_model, metrics = train_structural_model(decisions, settings.max_training_rows)
    joblib.dump(structural_model, artifact_path / "win_probability.joblib")
    # Every published number is anchored on the calibrated model, so the calibration gates in the
    # .NET promoter actually govern what is served.
    decisions["baseline_win_probability"] = structural_win_probability(structural_model, decisions)

    estimates = build_action_estimates(decisions, generation_id, generation["Patch"])
    path_estimates = build_path_estimates(decisions, generation_id, generation["Patch"])
    if not estimates:
        raise RuntimeError("No adjusted action estimates could be produced.")

    manifest = {
        "generationId": str(generation_id),
        "datasetVersion": generation["DatasetVersion"],
        "modelVersion": "dr-logit-isotonic-v2",
        "patches": included_patches,
        "regions": regions,
        "sourceCutoffUtc": cutoff.isoformat(),
        "rows": len(decisions),
        "actionEstimates": len(estimates),
        "pathEstimates": len(path_estimates),
        "eventStateCoverage": event_state_coverage,
        "validation": metrics,
    }
    manifest_json = json.dumps(manifest, sort_keys=True, separators=(",", ":"))
    manifest_bytes = manifest_json.encode()
    (artifact_path / "manifest.json").write_text(
        json.dumps(manifest, sort_keys=True, indent=2),
        encoding="utf-8",
    )
    checksum = hashlib.sha256(manifest_bytes).hexdigest()
    artifact_uri = upload_artifacts(settings, artifact_path, generation_id)

    if heartbeat is not None:
        heartbeat.raise_if_lease_lost()

    with connection.transaction():
        insert_estimates(connection, estimates)
        insert_path_estimates(connection, path_estimates)
        # The estimate inserts share this transaction with the terminal write, so a lease lost
        # mid-run rolls the whole set back instead of attaching it to a generation the reaper
        # already failed and then reporting success.
        execute_guarded(
            connection,
            """
            UPDATE "BuildLabGenerations"
            SET "Status" = 2,
                "ModelVersion" = %s,
                "ArtifactUri" = %s,
                "ArtifactSha256" = %s,
                "ArtifactManifestJson" = %s,
                "ValidationMetricsJson" = %s::jsonb,
                "CompletedAtUtc" = NOW(),
                "FailureReason" = NULL,
                "LeaseOwner" = NULL,
                "LeaseExpiresAtUtc" = NULL
            WHERE "Id" = %s AND "Status" = 1 AND "LeaseOwner" = %s
            """,
            (
                "dr-logit-isotonic-v2",
                artifact_uri,
                checksum,
                manifest_json,
                json.dumps(metrics),
                generation_id,
                settings.lease_owner,
            ),
            "the modeling lease was lost before the terminal status write, so the estimates were "
            "rolled back instead of published",
        )
    # The read queries above opened the run's implicit transaction, so the block that just closed was
    # a savepoint inside it. Committing here ends that transaction and makes the success log describe
    # a durable state rather than one still pending at connection close.
    connection.commit()
    LOG.info("Generation %s is ready for .NET promotion.", generation_id)


def execute_guarded(
    connection,
    statement: str,
    parameters: tuple,
    lost_lease_message: str,
) -> None:
    """Run a status write guarded on the status it expects and treat a zero rowcount as a lost lease."""
    cursor = connection.execute(statement, parameters)
    if cursor.rowcount == 0:
        raise LeaseLost(lost_lease_message)


def apply_row_weights(
    decisions: pd.DataFrame,
    current_patch: str,
    included_patches: Iterable[str],
    rank_offset_column: str | None,
) -> pd.DataFrame:
    recency = patch_recency_weights(current_patch, included_patches)
    weights = decisions["patch"].map(recency).fillna(0.0).astype(float)
    # Only the signed column can tell a pre-match reading from a post-match one. Rows written under
    # the old unsigned semantics are all non-negative, so discounting on sign there would silently
    # devalue the whole cohort; they are carried at full weight and flagged as unknown instead.
    post_match = pd.Series(0.0, index=decisions.index)
    if rank_offset_column == "ObservationOffsetSeconds" and (
        "rank_observation_offset_seconds" in decisions.columns
    ):
        offset = pd.to_numeric(
            decisions["rank_observation_offset_seconds"], errors="coerce"
        ).fillna(0.0)
        post_match = (offset > 0).astype(float)
        weights = weights * np.where(post_match > 0, POST_MATCH_RANK_WEIGHT, 1.0)
    decisions = decisions.assign(
        patch_weight=weights,
        rank_observation_is_post_match=post_match,
    )
    # A row borrowed from a fourth patch has zero weight: it contributes nothing to any estimate and
    # must not inflate the observed-count gate either.
    return decisions.loc[decisions["patch_weight"] > 0].reset_index(drop=True)


def patch_recency_weights(current_patch: str, included_patches: Iterable[str]) -> dict[str, float]:
    ordered = [current_patch] + [patch for patch in included_patches if patch != current_patch]
    return {
        patch: weight
        for patch, weight in zip(ordered[:3], [1.0, 0.60, 0.35], strict=False)
    }


def deidentified_export(decisions: pd.DataFrame, salt: str) -> pd.DataFrame:
    export = decisions.copy()
    export["match_surrogate"] = surrogate_ids(export["match_id"], salt, "match")
    export["participant_surrogate"] = surrogate_ids(
        export["match_id"].astype(str) + ":" + export["participant_id"].astype(str),
        salt,
        "participant",
    )
    return export.drop(
        columns=[
            column
            for column in ["match_id", "participant_id", "participant_row_id"]
            if column in export.columns
        ]
    )


def surrogate_ids(values: pd.Series, salt: str, namespace: str) -> pd.Series:
    key = salt.encode()
    return values.map(
        lambda value: hmac.new(key, f"{namespace}:{value}".encode(), hashlib.sha256)
        .hexdigest()[:24]
    )


def resolve_rank_offset_column(connection) -> str | None:
    """The rank observation column is being renamed to a signed offset; adapt to whichever exists."""
    row = connection.execute(
        """
        SELECT "column_name"
        FROM information_schema.columns
        WHERE table_name = 'MatchParticipantRankContexts'
          AND column_name IN ('ObservationOffsetSeconds', 'ObservationDistanceSeconds')
        ORDER BY column_name = 'ObservationOffsetSeconds' DESC
        LIMIT 1
        """
    ).fetchone()
    if row is None:
        return None
    return row["column_name"] if isinstance(row, dict) else row[0]


def rank_context_lateral(rank_offset_column: str | None) -> str:
    offset = f'rank_row."{rank_offset_column}"' if rank_offset_column else "NULL::bigint"
    return f"""
        JOIN LATERAL (
            SELECT rank_row."Tier" AS tier, {offset} AS observation_offset_seconds
            FROM "MatchParticipantRankContexts" rank_row
            WHERE rank_row."MatchId" = %(match_id_column)s
              AND rank_row."ParticipantId" = %(participant_id_column)s
              AND rank_row."Tier" = ANY(%%(tiers)s)
            ORDER BY (COALESCE({offset}, 0) > 0), ABS(COALESCE({offset}, 0))
            LIMIT 1
        ) rank ON TRUE
    """


def load_item_events(
    connection: psycopg.Connection,
    patches: list[str],
    cutoff,
    rank_offset_column: str | None,
) -> pd.DataFrame:
    rank_join = rank_context_lateral(rank_offset_column) % {
        "match_id_column": 'm."Id"',
        "participant_id_column": 'p."ParticipantId"',
    }
    return pd.read_sql_query(
        f"""
        WITH eligible AS (
            SELECT
                m."Id" AS match_id,
                m."MatchDate" AS match_date,
                m."Patch" AS patch,
                COALESCE(m."PlatformRegion", 'GLOBAL') AS region,
                p."Id" AS participant_row_id,
                p."ParticipantId" AS participant_id,
                p."TeamId" AS team_id,
                p."ChampionId" AS champion_id,
                UPPER(COALESCE(p."TeamPosition", '')) AS role,
                p."Win" AS won,
                rank.observation_offset_seconds AS rank_observation_offset_seconds
            FROM "Matches" m
            JOIN "MatchParticipants" p ON p."MatchId" = m."Id"
            JOIN "MatchTimelineFetchStates" timeline
              ON timeline."MatchId" = m."Id"
             AND timeline."Status" = 1
             AND timeline."SchemaVersion" >= %(schema_version)s
            {rank_join}
            -- Status/queue/duration/schema mirror BuildLabGenerationCoordinator.EligibleMatches, so
            -- the published MatchCount provenance describes exactly the cohort modeled here.
            WHERE m."Patch" = ANY(%(patches)s)
              AND m."Status" = 1
              AND m."FetchedAt" <= %(cutoff)s
              AND m."QueueId" = 420
              AND m."Duration" >= 300
              AND COALESCE(p."GameEndedInEarlySurrender", FALSE) = FALSE
        )
        SELECT
            e.*,
            item_event."EventIndex" AS event_index,
            item_event."EventType" AS event_type,
            item_event."TimestampMs" AS timestamp_ms,
            item_event."ItemId" AS action_id,
            item_event."BeforeId" AS before_id,
            item_event."AfterId" AS after_id,
            item_event."BuildCategory" AS build_category,
            frame."MinuteMark" AS minute,
            frame."Gold" AS gold,
            frame."CurrentGold" AS current_gold,
            frame."Xp" AS xp,
            frame."Cs" AS cs,
            frame."LaneCs" AS lane_cs,
            frame."JungleCs" AS jungle_cs,
            frame."Level" AS level,
            team_state.team_gold - opponent_state.team_gold AS team_gold_diff,
            team_state.team_xp - opponent_state.team_xp AS team_xp_diff,
            team_state.team_cs - opponent_state.team_cs AS team_cs_diff,
            COALESCE(opponent.champion_id, 0) AS opponent_champion_id,
            (
                SELECT STRING_AGG(teammate."ChampionId"::text, '-' ORDER BY teammate."ChampionId")
                FROM "MatchParticipants" teammate
                WHERE teammate."MatchId" = e.match_id AND teammate."TeamId" = e.team_id
            ) AS team_composition,
            (
                SELECT STRING_AGG(enemy."ChampionId"::text, '-' ORDER BY enemy."ChampionId")
                FROM "MatchParticipants" enemy
                WHERE enemy."MatchId" = e.match_id AND enemy."TeamId" <> e.team_id
            ) AS enemy_composition
        FROM eligible e
        JOIN "MatchParticipantItemEvents" item_event
         ON item_event."MatchId" = e.match_id
         AND item_event."ParticipantId" = e.participant_id
        JOIN LATERAL (
            SELECT s.*
            FROM "MatchParticipantTimelineSnapshots" s
            WHERE s."MatchId" = e.match_id
              AND s."ParticipantId" = e.participant_id
              AND s."FrameTimestampMs" <= item_event."TimestampMs"
            ORDER BY s."FrameTimestampMs" DESC
            LIMIT 1
        ) frame ON TRUE
        JOIN LATERAL (
            SELECT SUM(s."Gold") AS team_gold, SUM(s."Xp") AS team_xp, SUM(s."Cs") AS team_cs
            FROM "MatchParticipantTimelineSnapshots" s
            JOIN "MatchParticipants" teammate
              ON teammate."MatchId" = s."MatchId" AND teammate."ParticipantId" = s."ParticipantId"
            WHERE s."MatchId" = e.match_id
              AND s."MinuteMark" = frame."MinuteMark"
              AND teammate."TeamId" = e.team_id
        ) team_state ON TRUE
        JOIN LATERAL (
            SELECT SUM(s."Gold") AS team_gold, SUM(s."Xp") AS team_xp, SUM(s."Cs") AS team_cs
            FROM "MatchParticipantTimelineSnapshots" s
            JOIN "MatchParticipants" opponent_member
              ON opponent_member."MatchId" = s."MatchId" AND opponent_member."ParticipantId" = s."ParticipantId"
            WHERE s."MatchId" = e.match_id
              AND s."MinuteMark" = frame."MinuteMark"
              AND opponent_member."TeamId" <> e.team_id
        ) opponent_state ON TRUE
        LEFT JOIN LATERAL (
            SELECT lane_opponent."ChampionId" AS champion_id
            FROM "MatchParticipants" lane_opponent
            WHERE lane_opponent."MatchId" = e.match_id
              AND lane_opponent."TeamId" <> e.team_id
              AND UPPER(COALESCE(lane_opponent."TeamPosition", '')) = e.role
            ORDER BY lane_opponent."ParticipantId"
            LIMIT 1
        ) opponent ON TRUE
        WHERE e.role IN ('TOP', 'JUNGLE', 'MIDDLE', 'BOTTOM', 'UTILITY')
        ORDER BY e.match_date, e.match_id, e.participant_id, item_event."TimestampMs", item_event."EventIndex"
        """,
        connection,
        params={
            "patches": patches,
            "cutoff": cutoff,
            "tiers": list(EMERALD_PLUS),
            "schema_version": TIMELINE_SCHEMA_VERSION,
        },
    )


def load_materially_changed_items(
    connection: psycopg.Connection,
    patches: list[str],
) -> set[int]:
    if len(patches) < 2:
        return set()
    rows = connection.execute(
        """
        SELECT "ItemId"
        FROM "ItemVersions"
        WHERE "PatchVersion" = ANY(%s)
        GROUP BY "ItemId"
        HAVING COUNT(DISTINCT MD5(
            COALESCE("Name", '') || '|' ||
            COALESCE("Description", '') || '|' ||
            "PriceTotal"::text || '|' ||
            COALESCE("BuildsFrom"::text, '') || '|' ||
            COALESCE("BuildsInto"::text, '')
        )) > 1
        """,
        (patches,),
    ).fetchall()
    return {int(row["ItemId"]) for row in rows}


def load_timeline_state_events(connection, patches: list[str], cutoff) -> pd.DataFrame:
    return pd.read_sql_query(
        """
        SELECT
            payload."MatchId" AS match_id,
            payload."EventIndex" AS event_index,
            payload."TimestampMs" AS timestamp_ms,
            payload."EventType" AS event_type,
            payload."PayloadJson" AS payload_json
        FROM "MatchTimelineEventPayloads" payload
        JOIN "Matches" m ON m."Id" = payload."MatchId"
        JOIN "MatchTimelineFetchStates" timeline
          ON timeline."MatchId" = m."Id"
         AND timeline."Status" = 1
         AND timeline."SchemaVersion" >= %(schema_version)s
        WHERE m."Patch" = ANY(%(patches)s)
          AND m."Status" = 1
          AND m."FetchedAt" <= %(cutoff)s
          AND m."QueueId" = 420
          AND m."Duration" >= 300
          AND payload."EventType" IN ('CHAMPION_KILL', 'BUILDING_KILL', 'ELITE_MONSTER_KILL')
        ORDER BY payload."MatchId", payload."TimestampMs", payload."EventIndex"
        """,
        connection,
        params={
            "patches": patches,
            "cutoff": cutoff,
            "schema_version": TIMELINE_SCHEMA_VERSION,
        },
    )


def load_participant_teams(connection, patches: list[str], cutoff) -> pd.DataFrame:
    return pd.read_sql_query(
        """
        SELECT
            p."MatchId" AS match_id,
            p."ParticipantId" AS participant_id,
            p."TeamId" AS team_id
        FROM "MatchParticipants" p
        JOIN "Matches" m ON m."Id" = p."MatchId"
        WHERE m."Patch" = ANY(%s)
          AND m."Status" = 1
          AND m."FetchedAt" <= %s
          AND m."QueueId" = 420
          AND m."Duration" >= 300
        """,
        connection,
        params=(patches, cutoff),
    )


def payload_value(payload: dict, *names: str):
    lowered = {str(key).lower(): value for key, value in payload.items()}
    for name in names:
        value = lowered.get(name.lower())
        if value is not None:
            return value
    return None


def enrich_with_predecision_event_state(
    decisions: pd.DataFrame,
    events: pd.DataFrame,
    participant_teams: pd.DataFrame,
) -> pd.DataFrame:
    """Attach only cumulative facts whose event timestamp precedes the purchase."""
    enriched = decisions.copy()
    for column in ("team_kill_diff", "team_tower_diff", "team_objective_diff"):
        enriched[column] = 0.0
    enriched["has_event_state"] = 0.0
    if enriched.empty or events.empty or participant_teams.empty:
        return enriched
    # Coverage is per match: a match ingested while Build Lab was off carries no payload rows, so its
    # zeroed diffs are missing data rather than an even game.
    enriched["has_event_state"] = (
        enriched["match_id"].isin(set(events["match_id"])).astype(float)
    )

    teams = participant_teams.astype({"participant_id": int, "team_id": int})
    scored = attribute_events_to_teams(events, teams)
    if scored.empty:
        return enriched

    cumulative = scored.sort_values(["timestamp_ms", "event_index"], kind="stable")
    for column in ("kills", "towers", "objectives"):
        cumulative[column] = cumulative.groupby(["match_id", "team_id"], sort=False)[column].cumsum()
    cumulative = cumulative[
        ["match_id", "team_id", "timestamp_ms", "kills", "towers", "objectives"]
    ].sort_values("timestamp_ms", kind="stable")

    positioned = (
        enriched.reset_index(names="_row")[["_row", "match_id", "participant_id", "timestamp_ms"]]
        .astype({"participant_id": int})
        .merge(teams, on=["match_id", "participant_id"], how="left")
        .dropna(subset=["team_id"])
    )
    if positioned.empty:
        return enriched
    positioned["team_id"] = positioned["team_id"].astype(int)
    positioned["opponent_team_id"] = np.where(positioned["team_id"] == 100, 200, 100)
    positioned = positioned.sort_values("timestamp_ms", kind="stable")

    own = merge_cumulative_state(positioned, cumulative, "team_id")
    opponent = merge_cumulative_state(positioned, cumulative, "opponent_team_id")
    rows = positioned["_row"].to_numpy()
    enriched.loc[rows, "team_kill_diff"] = own["kills"].to_numpy() - opponent["kills"].to_numpy()
    enriched.loc[rows, "team_tower_diff"] = own["towers"].to_numpy() - opponent["towers"].to_numpy()
    enriched.loc[rows, "team_objective_diff"] = (
        own["objectives"].to_numpy() - opponent["objectives"].to_numpy()
    )
    return enriched


def attribute_events_to_teams(events: pd.DataFrame, teams: pd.DataFrame) -> pd.DataFrame:
    payloads = events["payload_json"].map(
        lambda value: value if isinstance(value, dict) else json.loads(value)
    )
    killers = payloads.map(lambda payload: positive_int(payload_value(payload, "killerId")))
    declared = payloads.map(lambda payload: positive_int(payload_value(payload, "killerTeamId")))
    owners = payloads.map(lambda payload: positive_int(payload_value(payload, "teamId")))
    scored = events[["match_id", "event_index", "timestamp_ms", "event_type"]].copy()
    scored["participant_id"] = pd.array(killers.to_numpy(), dtype="Int64")
    resolved = scored.merge(
        teams.astype({"participant_id": "Int64"}),
        on=["match_id", "participant_id"],
        how="left",
    )
    fallback = pd.to_numeric(pd.Series(declared.to_numpy()), errors="coerce")
    resolved["team_id"] = resolved["team_id"].fillna(fallback.set_axis(resolved.index))
    # A building destroyed by minions carries killerId 0 and only the OWNING team's id, so the credit
    # belongs to the other team. Dropping those rows would understate the tower diff systematically.
    owning = pd.to_numeric(pd.Series(owners.to_numpy()), errors="coerce").set_axis(resolved.index)
    conceded = owning.where(resolved["event_type"] == "BUILDING_KILL").map(
        {100.0: 200.0, 200.0: 100.0}
    )
    resolved["team_id"] = resolved["team_id"].fillna(conceded)
    resolved = resolved.dropna(subset=["team_id"])
    if resolved.empty:
        return resolved
    resolved["team_id"] = resolved["team_id"].astype(int)
    resolved["kills"] = (resolved["event_type"] == "CHAMPION_KILL").astype(int)
    resolved["towers"] = (resolved["event_type"] == "BUILDING_KILL").astype(int)
    resolved["objectives"] = (resolved["event_type"] == "ELITE_MONSTER_KILL").astype(int)
    return resolved


def merge_cumulative_state(
    positioned: pd.DataFrame,
    cumulative: pd.DataFrame,
    team_column: str,
) -> pd.DataFrame:
    left = positioned[["match_id", team_column, "timestamp_ms"]].rename(
        columns={team_column: "team_id"}
    )
    merged = pd.merge_asof(
        left,
        cumulative,
        on="timestamp_ms",
        by=["match_id", "team_id"],
        allow_exact_matches=False,
    )
    return merged[["kills", "towers", "objectives"]].fillna(0.0)


def load_rune_decisions(
    connection,
    patches: list[str],
    cutoff,
    rank_offset_column: str | None,
) -> pd.DataFrame:
    rank_join = rank_context_lateral(rank_offset_column) % {
        "match_id_column": 'm."Id"',
        "participant_id_column": 'p."ParticipantId"',
    }
    return pd.read_sql_query(
        f"""
        SELECT
            m."Id" AS match_id,
            m."MatchDate" AS match_date,
            m."Patch" AS patch,
            COALESCE(m."PlatformRegion", 'GLOBAL') AS region,
            p."Id" AS participant_row_id,
            p."ParticipantId" AS participant_id,
            p."ChampionId" AS champion_id,
            UPPER(COALESCE(p."TeamPosition", '')) AS role,
            p."Win" AS won,
            rank.observation_offset_seconds AS rank_observation_offset_seconds,
            rune."RuneId" AS action_id,
            rune."SelectionTree" AS selection_tree,
            rune."SelectionIndex" AS selection_index,
            COALESCE(opponent.champion_id, 0) AS opponent_champion_id,
            (
                SELECT STRING_AGG(teammate."ChampionId"::text, '-' ORDER BY teammate."ChampionId")
                FROM "MatchParticipants" teammate
                WHERE teammate."MatchId" = m."Id" AND teammate."TeamId" = p."TeamId"
            ) AS team_composition,
            (
                SELECT STRING_AGG(enemy."ChampionId"::text, '-' ORDER BY enemy."ChampionId")
                FROM "MatchParticipants" enemy
                WHERE enemy."MatchId" = m."Id" AND enemy."TeamId" <> p."TeamId"
            ) AS enemy_composition
        FROM "Matches" m
        JOIN "MatchParticipants" p ON p."MatchId" = m."Id"
        JOIN "MatchTimelineFetchStates" timeline
          ON timeline."MatchId" = m."Id"
         AND timeline."Status" = 1
         AND timeline."SchemaVersion" >= %(schema_version)s
        {rank_join}
        JOIN "MatchParticipantRunes" rune ON rune."MatchParticipantId" = p."Id"
        LEFT JOIN LATERAL (
            SELECT lane_opponent."ChampionId" AS champion_id
            FROM "MatchParticipants" lane_opponent
            WHERE lane_opponent."MatchId" = m."Id"
              AND lane_opponent."TeamId" <> p."TeamId"
              AND UPPER(COALESCE(lane_opponent."TeamPosition", '')) =
                  UPPER(COALESCE(p."TeamPosition", ''))
            ORDER BY lane_opponent."ParticipantId"
            LIMIT 1
        ) opponent ON TRUE
        WHERE m."Patch" = ANY(%(patches)s)
          AND m."Status" = 1
          AND m."FetchedAt" <= %(cutoff)s
          AND m."QueueId" = 420
          AND m."Duration" >= 300
          AND UPPER(COALESCE(p."TeamPosition", '')) IN ('TOP', 'JUNGLE', 'MIDDLE', 'BOTTOM', 'UTILITY')
          AND COALESCE(p."GameEndedInEarlySurrender", FALSE) = FALSE
        ORDER BY m."MatchDate", m."Id", p."ParticipantId", rune."SelectionTree", rune."SelectionIndex"
        """,
        connection,
        params={
            "patches": patches,
            "cutoff": cutoff,
            "tiers": list(EMERALD_PLUS),
            "schema_version": TIMELINE_SCHEMA_VERSION,
        },
    )


def load_spell_decisions(
    connection,
    patches: list[str],
    cutoff,
    rank_offset_column: str | None,
) -> pd.DataFrame:
    rank_join = rank_context_lateral(rank_offset_column) % {
        "match_id_column": 'm."Id"',
        "participant_id_column": 'p."ParticipantId"',
    }
    return pd.read_sql_query(
        f"""
        SELECT
            m."Id" AS match_id,
            m."MatchDate" AS match_date,
            m."Patch" AS patch,
            COALESCE(m."PlatformRegion", 'GLOBAL') AS region,
            p."Id" AS participant_row_id,
            p."ParticipantId" AS participant_id,
            p."ChampionId" AS champion_id,
            UPPER(COALESCE(p."TeamPosition", '')) AS role,
            p."Win" AS won,
            rank.observation_offset_seconds AS rank_observation_offset_seconds,
            p."SummonerSpell1Id" AS spell_1,
            p."SummonerSpell2Id" AS spell_2,
            COALESCE(opponent.champion_id, 0) AS opponent_champion_id,
            (
                SELECT STRING_AGG(teammate."ChampionId"::text, '-' ORDER BY teammate."ChampionId")
                FROM "MatchParticipants" teammate
                WHERE teammate."MatchId" = m."Id" AND teammate."TeamId" = p."TeamId"
            ) AS team_composition,
            (
                SELECT STRING_AGG(enemy."ChampionId"::text, '-' ORDER BY enemy."ChampionId")
                FROM "MatchParticipants" enemy
                WHERE enemy."MatchId" = m."Id" AND enemy."TeamId" <> p."TeamId"
            ) AS enemy_composition
        FROM "Matches" m
        JOIN "MatchParticipants" p ON p."MatchId" = m."Id"
        JOIN "MatchTimelineFetchStates" timeline
          ON timeline."MatchId" = m."Id"
         AND timeline."Status" = 1
         AND timeline."SchemaVersion" >= %(schema_version)s
        {rank_join}
        LEFT JOIN LATERAL (
            SELECT lane_opponent."ChampionId" AS champion_id
            FROM "MatchParticipants" lane_opponent
            WHERE lane_opponent."MatchId" = m."Id"
              AND lane_opponent."TeamId" <> p."TeamId"
              AND UPPER(COALESCE(lane_opponent."TeamPosition", '')) =
                  UPPER(COALESCE(p."TeamPosition", ''))
            ORDER BY lane_opponent."ParticipantId"
            LIMIT 1
        ) opponent ON TRUE
        WHERE m."Patch" = ANY(%(patches)s)
          AND m."Status" = 1
          AND m."FetchedAt" <= %(cutoff)s
          AND m."QueueId" = 420
          AND m."Duration" >= 300
          AND UPPER(COALESCE(p."TeamPosition", '')) IN ('TOP', 'JUNGLE', 'MIDDLE', 'BOTTOM', 'UTILITY')
          AND COALESCE(p."GameEndedInEarlySurrender", FALSE) = FALSE
        """,
        connection,
        params={
            "patches": patches,
            "cutoff": cutoff,
            "tiers": list(EMERALD_PLUS),
            "schema_version": TIMELINE_SCHEMA_VERSION,
        },
    )


def build_item_decisions(rows: pd.DataFrame) -> pd.DataFrame:
    if rows.empty:
        return rows
    output: list[dict] = []
    for _, participant in rows.groupby(["match_id", "participant_id"], sort=False):
        participant = participant.sort_values(["timestamp_ms", "event_index"])
        undone = undone_purchase_indexes(participant)
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
                # An undo of a sale restores the sold item. Ingestion classifies the undo row from
                # its after/before id, so a restored consumable stays out of the inventory state.
                restored_category = positive_or_zero_int(item.get("build_category"))
                if after_id and restored_category in BUILD_ITEM_CATEGORIES:
                    inventory.append(after_id)
                continue
            if event_type != 0 or not action_id or event_index in undone:
                continue

            build_category = positive_or_zero_int(item.get("build_category"))
            source = item.to_dict()
            source["inventory_ids"] = "-".join(str(value) for value in sorted(inventory))
            # Only build-relevant acquisitions belong in the inventory state. Consumables, wards,
            # trinkets and mid-game components carry no category and must not enter it.
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
                        decision_record(
                            source,
                            "FIRST_ITEM_PATH",
                            0,
                            [],
                            [*selected, action_id],
                        )
                    )
            output.append(decision_record(source, family, stage, selected, [action_id]))
            selected.append(action_id)
    return pd.DataFrame(output)


def positive_int(value) -> int | None:
    if value is None or pd.isna(value):
        return None
    parsed = int(value)
    return parsed if parsed > 0 else None


def positive_or_zero_int(value) -> int | None:
    if value is None or pd.isna(value):
        return None
    return int(value)


def remove_last(values: list, target) -> tuple | int | None:
    for index in range(len(values) - 1, -1, -1):
        value = values[index]
        item_id = value[0] if isinstance(value, tuple) else value
        if item_id == target:
            return values.pop(index)
    return None


def undone_purchase_indexes(participant: pd.DataFrame) -> set[int]:
    """Identify purchases reversed by ITEM_UNDO while replaying the exact lifecycle."""
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
    return undone


def build_rune_decisions(rows: pd.DataFrame) -> pd.DataFrame:
    if rows.empty:
        return pd.DataFrame()
    output: list[dict] = []
    for _, participant in rows.groupby(["match_id", "participant_id"], sort=False):
        participant = participant.sort_values(["selection_tree", "selection_index"])
        rune_ids = participant["action_id"].astype(int).tolist()
        if rune_ids:
            output.append(
                decision_record(
                    participant.iloc[0].to_dict(),
                    "RUNE_PAGE",
                    0,
                    [],
                    rune_ids,
                )
            )
        selected: list[int] = []
        for stage, (_, rune) in enumerate(
            participant.iterrows(), start=1
        ):
            record = decision_record(rune.to_dict(), "RUNE", stage, selected, [int(rune["action_id"])])
            output.append(record)
            selected.append(int(rune["action_id"]))
    return pd.DataFrame(output)


def build_spell_decisions(rows: pd.DataFrame) -> pd.DataFrame:
    if rows.empty:
        return pd.DataFrame()
    output = []
    for _, row in rows.iterrows():
        spell_ids = sorted([int(row["spell_1"]), int(row["spell_2"])])
        output.append(decision_record(row.to_dict(), "SPELL", 0, [], spell_ids))
    return pd.DataFrame(output)


def exclude_incompatible_prior_item_rows(
    decisions: pd.DataFrame,
    current_patch: str,
    changed_item_ids: set[int],
) -> pd.DataFrame:
    if decisions.empty or not changed_item_ids:
        return decisions
    incompatible = (decisions["patch"].astype(str) != current_patch) & decisions[
        "action_ids"
    ].map(lambda value: bool(changed_item_ids.intersection(json.loads(value))))
    return decisions.loc[~incompatible].copy()


def exclude_drifted_prior_actions(decisions: pd.DataFrame, current_patch: str) -> pd.DataFrame:
    """Drop borrowed rows when an action's current/prior observed outcome differs materially.

    This is deliberately conservative and runs before estimation. It does not label the difference
    causal; it only prevents old-patch observations from supporting a current-patch estimate.
    """
    if decisions.empty or decisions["patch"].astype(str).nunique() < 2:
        return decisions
    key_columns = ["champion_id", "role", "family", "stage", "action_key"]
    summaries = (
        decisions.assign(is_current=decisions["patch"].astype(str) == current_patch)
        .groupby(key_columns + ["is_current"], dropna=False)["won"]
        .agg(["count", "mean"])
        .reset_index()
    )
    current = summaries[summaries["is_current"]].set_index(key_columns)
    prior = summaries[~summaries["is_current"]].set_index(key_columns)
    comparable = current.join(prior, lsuffix="_current", rsuffix="_prior", how="inner")
    drifted = comparable[
        (comparable["count_current"] >= 100)
        & (comparable["count_prior"] >= 100)
        & ((comparable["mean_current"] - comparable["mean_prior"]).abs() > 0.05)
    ].index
    if len(drifted) == 0:
        return decisions
    drifted_keys = set(drifted.tolist())
    is_drifted_prior = decisions.apply(
        lambda row: row["patch"] != current_patch
        and tuple(row[column] for column in key_columns) in drifted_keys,
        axis=1,
    )
    return decisions.loc[~is_drifted_prior].copy()


def decision_record(
    source: dict,
    family: str,
    stage: int,
    prefix: list[int],
    action_ids: list[int],
) -> dict:
    prefix_hash = hash_path(prefix)
    pregame = family in PREGAME_FAMILIES
    return {
        **source,
        "family": family,
        "stage": stage,
        "path_prefix": json.dumps(prefix),
        "path_prefix_hash": prefix_hash,
        "action_ids": json.dumps(action_ids),
        "action_key": "+".join(str(value) for value in action_ids),
        "inventory_ids": "" if pregame else str(source.get("inventory_ids") or ""),
        "has_predecision_state": 0.0 if pregame else 1.0,
        # The conditioning frame is floored to the timeline frame cadence, so a published timing read
        # from "minute" is biased early by up to FrameIntervalMinutes. The event's own timestamp is
        # the real decision time.
        "decision_minute": float("nan") if pregame else event_minute(source.get("timestamp_ms")),
        # Item rows are re-stamped by enrich_with_predecision_event_state once payload coverage for
        # the match is known; pregame rows never have in-game event state.
        "has_event_state": 0.0,
        "minute": float(source.get("minute") or 0),
        "gold": float(source.get("gold") or 0),
        "current_gold": float(source.get("current_gold") or 0),
        "xp": float(source.get("xp") or 0),
        "cs": float(source.get("cs") or 0),
        "lane_cs": float(source.get("lane_cs") or 0),
        "jungle_cs": float(source.get("jungle_cs") or 0),
        "level": float(source.get("level") or 0),
        "team_gold_diff": float(source.get("team_gold_diff") or 0),
        "team_xp_diff": float(source.get("team_xp_diff") or 0),
        "team_cs_diff": float(source.get("team_cs_diff") or 0),
        "team_kill_diff": float(source.get("team_kill_diff") or 0),
        "team_tower_diff": float(source.get("team_tower_diff") or 0),
        "team_objective_diff": float(source.get("team_objective_diff") or 0),
    }


def event_minute(timestamp_ms) -> float:
    if timestamp_ms is None or pd.isna(timestamp_ms):
        return float("nan")
    return float(timestamp_ms) / 60_000.0


def average_timing(rows: pd.DataFrame, family: str) -> float | None:
    if family not in TIMED_FAMILIES or "decision_minute" not in rows.columns:
        return None
    minutes = pd.to_numeric(rows["decision_minute"], errors="coerce").dropna()
    return float(minutes.mean()) if not minutes.empty else None


def train_structural_model(
    decisions: pd.DataFrame,
    max_training_rows: int = 250_000,
) -> tuple[dict, dict]:
    ordered = decisions.sort_values(["match_date", "match_id"]).drop_duplicates(
        ["match_id", "participant_id", "minute"]
    )
    if len(ordered) > max_training_rows:
        # A systematic chronological sample keeps the patch and time mix intact while bounding the
        # dense design matrix that the fit materialises.
        ordered = ordered.iloc[:: math.ceil(len(ordered) / max_training_rows)]
    match_order = (
        ordered[["match_id", "match_date", "patch"]]
        .drop_duplicates("match_id")
        .sort_values(["match_date", "match_id"])
    )
    patches = match_order["patch"].dropna().astype(str).unique()
    held_out_patch = patches[-1] if len(patches) >= 2 else None
    if held_out_patch:
        development_matches = match_order.loc[
            match_order["patch"].astype(str) != held_out_patch, "match_id"
        ].tolist()
        test_matches = match_order.loc[
            match_order["patch"].astype(str) == held_out_patch, "match_id"
        ].tolist()
        calibration_start = max(1, int(len(development_matches) * 0.8))
        train_matches = development_matches[:calibration_start]
        calibration_matches = development_matches[calibration_start:]
    else:
        match_ids = match_order["match_id"].tolist()
        train_end = max(1, int(len(match_ids) * 0.7))
        calibration_end = max(train_end + 1, int(len(match_ids) * 0.85))
        train_matches = match_ids[:train_end]
        calibration_matches = match_ids[train_end:calibration_end]
        test_matches = match_ids[calibration_end:]
    train = ordered[ordered["match_id"].isin(train_matches)]
    calibration = ordered[ordered["match_id"].isin(calibration_matches)]
    test = ordered[ordered["match_id"].isin(test_matches)]
    if min(len(train), len(calibration), len(test)) < 20:
        raise RuntimeError("At least 140 chronological decision frames are required for validation.")

    spec = build_design_spec(ordered)
    design = design_matrix(ordered, spec)
    train_x = design.loc[train.index]
    calibration_x = design.loc[calibration.index]
    test_x = design.loc[test.index]
    model = make_pipeline(StandardScaler(), LogisticRegression(max_iter=500, class_weight="balanced"))
    model.fit(train_x, train["won"].astype(int))
    calibration_raw = model.predict_proba(calibration_x)[:, 1]
    calibrator = IsotonicRegression(out_of_bounds="clip")
    calibrator.fit(calibration_raw, calibration["won"].astype(int))
    test_raw = model.predict_proba(test_x)[:, 1]
    predicted = np.asarray(calibrator.predict(test_raw), dtype=float)
    actual = test["won"].astype(int).to_numpy()
    baseline_probability = float(train["won"].mean())
    baseline = np.full_like(predicted, baseline_probability)
    overall_ece = expected_calibration_error(actual, predicted)
    time_bands = pd.qcut(test["minute"], q=min(5, test["minute"].nunique()), duplicates="drop")
    band_eces = [
        expected_calibration_error(
            actual[np.asarray(time_bands == band)],
            predicted[np.asarray(time_bands == band)],
        )
        for band in time_bands.dropna().unique()
    ]
    beats_baseline = bool(
        brier_score_loss(actual, predicted) < brier_score_loss(actual, baseline)
        and log_loss(actual, np.clip(predicted, 1e-6, 1 - 1e-6))
        < log_loss(actual, np.clip(baseline, 1e-6, 1 - 1e-6))
    )
    leakage = evaluate_leakage(
        {
            "train": train_matches,
            "calibration": calibration_matches,
            "test": test_matches,
        },
        design.columns,
    )
    metrics = {
        "overallEce": overall_ece,
        "maxTimeBandEce": max(band_eces, default=overall_ece),
        "brierScore": float(brier_score_loss(actual, predicted)),
        "baselineBrierScore": float(brier_score_loss(actual, baseline)),
        "logLoss": float(log_loss(actual, np.clip(predicted, 1e-6, 1 - 1e-6))),
        "baselineLogLoss": float(log_loss(actual, np.clip(baseline, 1e-6, 1 - 1e-6))),
        "heldOutPatchPassed": bool(held_out_patch and beats_baseline),
        "leakageCheckPassed": leakage["passed"],
        "leakageDetail": leakage,
        "heldOutPatch": held_out_patch,
        "trainMatchCount": len(set(train_matches)),
        "calibrationMatchCount": len(set(calibration_matches)),
        "testMatchCount": len(set(test_matches)),
        "designColumnCount": len(design.columns),
    }
    return (
        {"model": model, "calibrator": calibrator, "spec": spec, "features": design.columns.tolist()},
        metrics,
    )


def evaluate_leakage(splits: dict[str, Sequence], design_columns: Iterable[str]) -> dict:
    """Measure the leakage claim instead of asserting it, and keep enough detail to debug it."""
    sets = {name: set(values) for name, values in splits.items()}
    overlaps = {}
    names = list(sets)
    for position, left in enumerate(names):
        for right in names[position + 1:]:
            shared = sets[left] & sets[right]
            if shared:
                overlaps[f"{left}|{right}"] = {
                    "count": len(shared),
                    "sample": [str(value) for value in sorted(shared, key=str)[:5]],
                }
    post_outcome = sorted(
        column
        for column in design_columns
        if any(
            str(column) == blocked or str(column).startswith(f"{blocked}_")
            for blocked in POST_OUTCOME_COLUMNS
        )
    )
    empty = sorted(name for name, values in sets.items() if not values)
    return {
        "passed": not overlaps and not post_outcome and not empty,
        "splitMatchOverlaps": overlaps,
        "postOutcomeFeatureColumns": post_outcome,
        "emptySplits": empty,
        "splitMatchCounts": {name: len(values) for name, values in sets.items()},
    }


def structural_win_probability(
    bundle: dict,
    frame: pd.DataFrame,
    chunk_rows: int = 50_000,
) -> np.ndarray:
    """Score the frozen dataset with the calibrated model in bounded-memory chunks."""
    if frame.empty:
        return np.zeros(0)
    values = np.empty(len(frame), dtype=float)
    for start in range(0, len(frame), chunk_rows):
        chunk = frame.iloc[start:start + chunk_rows]
        raw = bundle["model"].predict_proba(design_matrix(chunk, bundle["spec"]))[:, 1]
        values[start:start + len(chunk)] = np.asarray(bundle["calibrator"].predict(raw), dtype=float)
    return np.clip(values, 1e-4, 1 - 1e-4)


def build_action_estimates(
    decisions: pd.DataFrame,
    generation_id: UUID,
    patch: str,
) -> list[tuple]:
    expanded = expand_scopes(decisions)
    grouping = [
        "champion_id",
        "role",
        "scope_opponent_id",
        "scope_region",
        "family",
        "stage",
        "path_prefix_hash",
        "path_prefix",
    ]
    records: list[dict] = []
    for keys, group in expanded.groupby(grouping, sort=False, dropna=False):
        if len(group) < 40 or group["action_key"].nunique() < 2:
            continue
        alternatives = group["action_key"].value_counts()
        # One design matrix per cell, not per candidate action: the encoding depends on the cell, and
        # materializing it once per alternative is what makes a large cell expensive.
        design = design_matrix(group).to_numpy(dtype=float)
        for action_key in alternatives.index:
            result = doubly_robust_binary(group, action_key, design)
            if result is None:
                continue
            (
                champion_id,
                role,
                opponent_id,
                region,
                family,
                stage,
                prefix_hash,
                prefix_json,
            ) = keys
            selected = group["action_key"] == action_key
            records.append(
                {
                    **result,
                    "champion_id": int(champion_id),
                    "role": role,
                    "opponent_id": int(opponent_id),
                    "region": region,
                    "family": family,
                    "stage": int(stage),
                    "prefix_hash": prefix_hash,
                    "prefix_json": prefix_json,
                    "action_key": str(action_key),
                    "action_ids": group.loc[selected, "action_ids"].iloc[0],
                    "average_timing": average_timing(group.loc[selected], family),
                    "baseline_definition": action_baseline_definition(
                        family,
                        int(stage),
                        prefix_json,
                        len(alternatives) - 1,
                    ),
                }
            )
    apply_partial_pooling(
        records,
        [["family", "stage", "action_key"], ["family", "stage", "action_key", "role"]],
    )
    return [
        (
            uuid4(),
            generation_id,
            record["champion_id"],
            record["role"],
            record["opponent_id"],
            patch,
            record["region"],
            record["family"],
            record["stage"],
            record["prefix_hash"],
            record["prefix_json"],
            record["action_key"],
            record["action_ids"],
            record["estimate"],
            record["low"],
            record["high"],
            record["raw_win_rate"],
            record["pick_rate"],
            record["observed_count"],
            record["effective_sample_size"],
            record["average_timing"],
            record["overlap"],
            record["balance"],
            record["stable"],
            False,
            "SHADOW",
            "GLOBAL" if record["region"] == "GLOBAL" else "REGIONAL_OVERRIDE",
            record["baseline_definition"],
            None,
        )
        for record in records
    ]


def action_baseline_definition(
    family: str,
    stage: int,
    prefix_json: str,
    alternative_count: int,
) -> str:
    """Name the comparison set an estimate was actually measured against."""
    prefix = parse_id_list(prefix_json)
    if family in PREGAME_FAMILIES:
        context = "the same champion, role, lane opponent, team and enemy composition, patch and region"
    elif prefix:
        context = (
            f"the same champion, role, lane opponent, patch, region and the identical {len(prefix)}"
            "-action prefix"
        )
    else:
        context = "the same champion, role, lane opponent, patch and region, with no prior selection"
    return (
        f"{alternative_count} alternative {family} choice(s) observed at stage {stage} under "
        f"{context}, weighted to the selected arm's pre-decision state."
    )[:256]


def build_path_estimates(
    decisions: pd.DataFrame,
    generation_id: UUID,
    patch: str,
) -> list[tuple]:
    item_rows = decisions[decisions["family"].isin(["STARTER", "BOOTS", "ITEM"])].copy()
    if item_rows.empty:
        return []
    paths: list[dict] = []
    for _, participant in item_rows.groupby(["match_id", "participant_id"], sort=False):
        participant = participant.sort_values(["minute", "stage"], kind="stable")
        ids: list[int] = []
        for position in range(len(participant)):
            ids.extend(json.loads(participant["action_ids"].iloc[position]))
            if len(ids) < 2:
                continue
            # Covariates come from the frame immediately before the purchase that completes this
            # prefix, not from the starter frame that predates most of the path.
            row = participant.iloc[position].to_dict()
            row["path_ids"] = json.dumps(ids)
            row["path_hash"] = hash_path(ids)
            row["path_action"] = "+".join(str(value) for value in ids)
            paths.append(row)
    if not paths:
        return []
    frame = expand_scopes(pd.DataFrame(paths))
    frame["participant_key"] = (
        frame["match_id"].astype(str) + "|" + frame["participant_id"].astype(str)
    )
    frame["path_length"] = frame["path_action"].str.count(r"\+") + 1
    records: list[dict] = []
    scope_columns = ["champion_id", "role", "scope_opponent_id", "scope_region"]
    for scope_keys, scope in frame.groupby(scope_columns, sort=False, dropna=False):
        for path_keys, group in scope.groupby(["path_hash", "path_ids"], sort=False, dropna=False):
            target = group["path_action"].iloc[0]
            comparator = participant_level_comparator(scope, target)
            if comparator is None:
                continue
            result = doubly_robust_binary(comparator, target)
            if result is None:
                continue
            records.append(
                {
                    **result,
                    "champion_id": int(scope_keys[0]),
                    "role": scope_keys[1],
                    "opponent_id": int(scope_keys[2]),
                    "region": scope_keys[3],
                    "path_hash": path_keys[0],
                    "path_ids": path_keys[1],
                }
            )
    apply_partial_pooling(records, [["path_hash"], ["path_hash", "role"]])
    return [
        (
            uuid4(),
            generation_id,
            record["champion_id"],
            record["role"],
            record["opponent_id"],
            patch,
            record["region"],
            record["path_hash"],
            record["path_ids"],
            float(np.clip(record["baseline_probability"] + record["estimate"], 0, 1)),
            record["estimate"],
            record["low"],
            record["high"],
            record["observed_count"],
            record["effective_sample_size"],
            False,
            None,
        )
        for record in records
    ]


def participant_level_comparator(scope: pd.DataFrame, target: str) -> pd.DataFrame | None:
    """One row per participant so a participant never appears in both arms of the same estimate."""
    treated = scope[scope["path_action"] == target].drop_duplicates("participant_key")
    if treated.empty:
        return None
    others = scope[~scope["participant_key"].isin(set(treated["participant_key"]))]
    control = (
        others.sort_values("path_length", kind="stable")
        .groupby("participant_key", sort=False)
        .tail(1)
    )
    comparator = pd.concat([treated, control], ignore_index=True)
    comparator["action_key"] = comparator["path_action"]
    return comparator


def expand_scopes(frame: pd.DataFrame) -> pd.DataFrame:
    opponents = frame["opponent_champion_id"].fillna(0).astype(int)
    regions = frame["region"].fillna("GLOBAL").astype(str).str.upper()
    scopes = []
    for opponent, region in (
        (0, None),
        (opponents, None),
        (0, regions),
        (opponents, regions),
    ):
        scope = frame.copy()
        scope["scope_opponent_id"] = opponent
        scope["scope_region"] = "GLOBAL" if region is None else region
        scopes.append(scope)
    return pd.concat(scopes, ignore_index=True).drop_duplicates()


def doubly_robust_binary(
    group: pd.DataFrame,
    action_key: str,
    design: np.ndarray | None = None,
) -> dict | None:
    treated = (group["action_key"].astype(str) == str(action_key)).astype(int).to_numpy()
    outcome = group["won"].astype(int).to_numpy()
    clusters = group["match_id"].astype(str).to_numpy()
    unique_clusters = np.unique(clusters)
    if (
        treated.sum() < 10
        or (1 - treated).sum() < 10
        or len(np.unique(outcome)) < 2
        or len(unique_clusters) < 4
    ):
        return None
    x = design_matrix(group).to_numpy(dtype=float) if design is None else design
    if "baseline_win_probability" in group.columns:
        # The calibrated structural score is a pre-decision prognostic summary, so both nuisance
        # models see it and the published baseline inherits the calibration gates.
        prognostic = np.clip(
            group["baseline_win_probability"].astype(float).to_numpy(), 1e-4, 1 - 1e-4
        )
        x = np.column_stack([x, np.log(prognostic / (1 - prognostic))])
    recency_weights = group.get("patch_weight", pd.Series(1.0, index=group.index)).to_numpy(float)
    propensity = np.full(len(group), np.nan)
    mu1 = np.full(len(group), np.nan)
    mu0 = np.full(len(group), np.nan)
    fold_count = min(5, len(unique_clusters))
    folds = chronological_fold_assignment(group, clusters, fold_count)
    for fold in range(fold_count):
        validation_index = np.flatnonzero(folds == fold)
        train_index = np.flatnonzero(folds != fold)
        if len(validation_index) == 0:
            continue
        if (
            len(np.unique(treated[train_index])) < 2
            or len(np.unique(outcome[train_index])) < 2
        ):
            return None
        propensity_model = make_pipeline(StandardScaler(), LogisticRegression(max_iter=400))
        propensity_model.fit(
            x[train_index],
            treated[train_index],
            logisticregression__sample_weight=recency_weights[train_index],
        )
        propensity[validation_index] = propensity_model.predict_proba(x[validation_index])[:, 1]

        outcome_model = make_pipeline(StandardScaler(), LogisticRegression(max_iter=400))
        outcome_model.fit(
            np.column_stack([x[train_index], treated[train_index]]),
            outcome[train_index],
            logisticregression__sample_weight=recency_weights[train_index],
        )
        mu1[validation_index] = outcome_model.predict_proba(
            np.column_stack([x[validation_index], np.ones(len(validation_index))])
        )[:, 1]
        mu0[validation_index] = outcome_model.predict_proba(
            np.column_stack([x[validation_index], np.zeros(len(validation_index))])
        )[:, 1]
    if np.isnan(propensity).any() or np.isnan(mu1).any() or np.isnan(mu0).any():
        return None
    propensity = np.clip(propensity, 0.02, 0.98)
    dr1 = mu1 + treated / propensity * (outcome - mu1)
    dr0 = mu0 + (1 - treated) / (1 - propensity) * (outcome - mu0)
    influence = dr1 - dr0
    estimate = float(np.average(influence, weights=recency_weights))
    standard_error = clustered_standard_error(influence, recency_weights, estimate, clusters)
    if not math.isfinite(standard_error):
        return None
    weights = recency_weights * treated / propensity
    effective_sample = float(weights.sum() ** 2 / max(np.square(weights).sum(), 1e-9))
    overlap = float(np.mean((propensity >= 0.05) & (propensity <= 0.95)))
    balance = maximum_weighted_smd(x, treated, propensity)
    return {
        "estimate": estimate,
        "raw_estimate": estimate,
        "standard_error": standard_error,
        "low": estimate - 1.96 * standard_error,
        "high": estimate + 1.96 * standard_error,
        "raw_win_rate": float(outcome[treated == 1].mean()),
        "pick_rate": float(treated.mean()),
        "observed_count": independent_unit_count(group, treated),
        "effective_sample_size": effective_sample,
        "baseline_probability": float(np.average(mu0, weights=recency_weights)),
        "overlap": overlap,
        "balance": balance,
        "stable": direction_is_stable(influence, folds, clusters, estimate),
    }


def independent_unit_count(group: pd.DataFrame, treated: np.ndarray) -> int:
    """Count independent units, not rows: one participant may contribute several decision rows."""
    if "participant_id" not in group.columns:
        return int(treated.sum())
    selected = group.loc[treated == 1, ["match_id", "participant_id"]]
    return int(len(set(zip(selected["match_id"].astype(str), selected["participant_id"].astype(str)))))


def chronological_fold_assignment(
    group: pd.DataFrame,
    clusters: np.ndarray,
    fold_count: int,
) -> np.ndarray:
    """Contiguous chronological blocks of matches, so every fold is genuinely held out in time."""
    order_columns = [column for column in ("match_date", "match_id") if column in group.columns]
    ordered = group.sort_values(order_columns, kind="stable") if order_columns else group
    sequence = ordered["match_id"].astype(str).drop_duplicates().to_numpy()
    assignment = {}
    for fold, block in enumerate(np.array_split(np.arange(len(sequence)), fold_count)):
        for position in block:
            assignment[sequence[position]] = fold
    return np.array([assignment[value] for value in clusters], dtype=int)


def clustered_standard_error(
    influence: np.ndarray,
    weights: np.ndarray,
    estimate: float,
    clusters: np.ndarray,
) -> float:
    """Cluster-robust variance: rows from one match share an influence value and are not independent."""
    total = weights.sum()
    if total <= 0:
        return float("inf")
    contribution = weights * (influence - estimate) / total
    _, inverse = np.unique(clusters, return_inverse=True)
    cluster_sums = np.bincount(inverse, weights=contribution)
    count = len(cluster_sums)
    if count < 2:
        return float("inf")
    correction = count / (count - 1)
    return float(np.sqrt(correction * np.sum(np.square(cluster_sums))))


def direction_is_stable(
    influence: np.ndarray,
    folds: np.ndarray,
    clusters: np.ndarray,
    estimate: float,
) -> bool:
    """No held-out chronological fold may point the other way by more than its own noise."""
    if abs(estimate) < 1e-9:
        return False
    _, inverse = np.unique(clusters, return_inverse=True)
    counts = np.bincount(inverse)
    cluster_means = np.bincount(inverse, weights=influence) / np.maximum(counts, 1)
    cluster_folds = np.zeros(len(counts), dtype=int)
    cluster_folds[inverse] = folds
    for fold in np.unique(folds):
        means = cluster_means[cluster_folds == fold]
        if len(means) < 2:
            continue
        fold_mean = float(means.mean())
        fold_error = float(means.std(ddof=1) / math.sqrt(len(means)))
        if np.sign(fold_mean) != np.sign(estimate) and abs(fold_mean) > fold_error:
            return False
    return True


def apply_partial_pooling(records: list[dict], levels: list[list[str]]) -> None:
    """Partial pooling toward each parent scope, with the interval taken from the same posterior."""
    if not records:
        return
    raw = np.array([float(record["raw_estimate"]) for record in records])
    variance = np.array([max(float(record["standard_error"]), 1e-6) ** 2 for record in records])
    prior_mean = np.zeros(len(records))
    prior_variance = np.full(len(records), GLOBAL_PRIOR_VARIANCE)
    for keys in levels:
        groups: dict[tuple, list[int]] = {}
        for position, record in enumerate(records):
            groups.setdefault(tuple(record[key] for key in keys), []).append(position)
        next_mean = prior_mean.copy()
        next_variance = prior_variance.copy()
        for members in groups.values():
            index = np.array(members)
            spread = between_group_variance(raw[index], variance[index])
            precision = 1.0 / (variance[index] + spread)
            group_mean = float(np.sum(precision * raw[index]) / np.sum(precision))
            group_variance = float(1.0 / np.sum(precision))
            parent_mean = float(prior_mean[index[0]])
            parent_variance = float(prior_variance[index[0]])
            combined = 1.0 / group_variance + 1.0 / parent_variance
            next_mean[index] = (group_mean / group_variance + parent_mean / parent_variance) / combined
            next_variance[index] = spread + 1.0 / combined
        prior_mean, prior_variance = next_mean, next_variance
    posterior_variance = 1.0 / (1.0 / variance + 1.0 / prior_variance)
    posterior_mean = (raw / variance + prior_mean / prior_variance) * posterior_variance
    posterior_error = np.sqrt(posterior_variance)
    for position, record in enumerate(records):
        record["estimate"] = float(posterior_mean[position])
        record["posterior_standard_error"] = float(posterior_error[position])
        record["low"] = float(posterior_mean[position] - 1.96 * posterior_error[position])
        record["high"] = float(posterior_mean[position] + 1.96 * posterior_error[position])


def between_group_variance(values: np.ndarray, variance: np.ndarray) -> float:
    """Method-of-moments spread of true child effects around their parent."""
    if len(values) < 2:
        return GLOBAL_PRIOR_VARIANCE
    return float(max(values.var(ddof=1) - variance.mean(), 1e-8))


def maximum_weighted_smd(
    x: np.ndarray,
    treated: np.ndarray,
    propensity: np.ndarray,
    minimum_support: int | None = None,
) -> float:
    treated_weights = treated / propensity
    control_weights = (1 - treated) / (1 - propensity)
    if treated_weights.sum() <= 0 or control_weights.sum() <= 0:
        return float("inf")
    treated_rows = x[treated == 1]
    control_rows = x[treated == 0]
    support = (
        minimum_support
        if minimum_support is not None
        else max(MINIMUM_BALANCE_SUPPORT, math.ceil(0.01 * len(x)))
    )
    pooled_sd = np.sqrt((np.var(treated_rows, axis=0) + np.var(control_rows, axis=0)) / 2)
    informative = (
        (np.count_nonzero(treated_rows, axis=0) >= support)
        & (np.count_nonzero(control_rows, axis=0) >= support)
        & (pooled_sd > 1e-9)
    )
    if not informative.any():
        return 0.0
    treated_mean = np.average(x[:, informative], axis=0, weights=treated_weights)
    control_mean = np.average(x[:, informative], axis=0, weights=control_weights)
    smd = np.abs(treated_mean - control_mean) / pooled_sd[informative]
    return float(np.nanmax(smd))


@dataclass(frozen=True)
class DesignSpec:
    champion_ids: tuple[int, ...]
    opponent_ids: tuple[int, ...]
    roster_ids: tuple[int, ...]
    item_ids: tuple[int, ...]
    roles: tuple[str, ...]
    patches: tuple[str, ...]
    regions: tuple[str, ...]
    columns: tuple[str, ...]


def build_design_spec(frame: pd.DataFrame) -> DesignSpec:
    """Fix bounded vocabularies once so the matrix width never scales with the row count."""
    champion_ids = top_scalar_values(frame, "champion_id", MAX_CHAMPION_VOCABULARY)
    opponent_ids = top_scalar_values(frame, "opponent_champion_id", MAX_CHAMPION_VOCABULARY)
    roster_ids = top_token_values(
        frame, ("team_composition", "enemy_composition"), MAX_CHAMPION_VOCABULARY
    )
    item_ids = top_token_values(frame, ("inventory_ids",), MAX_ITEM_VOCABULARY)
    roles = top_label_values(frame, "role", MAX_ROLE_VOCABULARY)
    patches = top_label_values(frame, "patch", MAX_PATCH_VOCABULARY)
    regions = top_label_values(frame, "region", MAX_REGION_VOCABULARY)
    columns = tuple(
        [*FEATURE_COLUMNS]
        + [f"champion_{value}" for value in champion_ids]
        + [f"opponent_{value}" for value in opponent_ids]
        + [f"role_{value}" for value in roles]
        + [f"patch_{value}" for value in patches]
        + [f"region_{value}" for value in regions]
        + [f"team_champion_{value}" for value in roster_ids]
        + [f"enemy_champion_{value}" for value in roster_ids]
        + [f"inventory_item_{value}" for value in item_ids]
    )
    if len(columns) > DESIGN_MATRIX_MAX_COLUMNS:
        raise RuntimeError("The design matrix exceeded its bounded width.")
    return DesignSpec(
        champion_ids=champion_ids,
        opponent_ids=opponent_ids,
        roster_ids=roster_ids,
        item_ids=item_ids,
        roles=roles,
        patches=patches,
        regions=regions,
        columns=columns,
    )


def top_scalar_values(frame: pd.DataFrame, column: str, limit: int) -> tuple[int, ...]:
    if column not in frame.columns:
        return ()
    values = pd.to_numeric(frame[column], errors="coerce").dropna().astype(int)
    return tuple(sorted(values.value_counts().index[:limit].tolist()))


def top_label_values(frame: pd.DataFrame, column: str, limit: int) -> tuple[str, ...]:
    if column not in frame.columns:
        return ()
    values = frame[column].fillna("UNKNOWN").astype(str)
    return tuple(sorted(values.value_counts().index[:limit].tolist()))


def top_token_values(frame: pd.DataFrame, columns: tuple[str, ...], limit: int) -> tuple[int, ...]:
    counts: dict[int, int] = {}
    for column in columns:
        if column not in frame.columns:
            continue
        codes, uniques = pd.factorize(frame[column].fillna("").astype(str))
        occurrences = np.bincount(codes[codes >= 0], minlength=len(uniques))
        for position, raw in enumerate(uniques):
            for token in parse_id_list(raw):
                counts[token] = counts.get(token, 0) + int(occurrences[position])
    ordered = sorted(counts.items(), key=lambda entry: (-entry[1], entry[0]))
    return tuple(sorted(token for token, _ in ordered[:limit]))


def parse_id_list(value) -> list[int]:
    if value is None:
        return []
    if isinstance(value, float) and math.isnan(value):
        return []
    return [int(token) for token in ID_PATTERN.findall(str(value))]


def design_matrix(frame: pd.DataFrame, spec: DesignSpec | None = None) -> pd.DataFrame:
    spec = spec or build_design_spec(frame)
    blocks = [frame.reindex(columns=FEATURE_COLUMNS, fill_value=0).fillna(0).to_numpy(np.float32)]
    blocks.append(one_hot_scalar(frame, "champion_id", spec.champion_ids))
    blocks.append(one_hot_scalar(frame, "opponent_champion_id", spec.opponent_ids))
    blocks.append(one_hot_label(frame, "role", spec.roles))
    blocks.append(one_hot_label(frame, "patch", spec.patches))
    blocks.append(one_hot_label(frame, "region", spec.regions))
    blocks.append(multi_hot_tokens(frame, "team_composition", spec.roster_ids))
    blocks.append(multi_hot_tokens(frame, "enemy_composition", spec.roster_ids))
    blocks.append(multi_hot_tokens(frame, "inventory_ids", spec.item_ids))
    return pd.DataFrame(
        np.hstack([block for block in blocks if block.shape[1] > 0]),
        index=frame.index,
        columns=list(spec.columns),
    )


def one_hot_scalar(frame: pd.DataFrame, column: str, vocabulary: tuple[int, ...]) -> np.ndarray:
    matrix = np.zeros((len(frame), len(vocabulary)), dtype=np.float32)
    if not vocabulary or column not in frame.columns:
        return matrix
    values = pd.to_numeric(frame[column], errors="coerce")
    return scatter_positions(matrix, values.map(vocabulary_positions(vocabulary)))


def one_hot_label(frame: pd.DataFrame, column: str, vocabulary: tuple[str, ...]) -> np.ndarray:
    matrix = np.zeros((len(frame), len(vocabulary)), dtype=np.float32)
    if not vocabulary or column not in frame.columns:
        return matrix
    values = frame[column].fillna("UNKNOWN").astype(str)
    return scatter_positions(matrix, values.map(vocabulary_positions(vocabulary)))


def vocabulary_positions(vocabulary: tuple) -> dict:
    return {value: index for index, value in enumerate(vocabulary)}


def scatter_positions(matrix: np.ndarray, positions: pd.Series) -> np.ndarray:
    known = positions.notna().to_numpy()
    matrix[np.flatnonzero(known), positions[known].astype(int).to_numpy()] = 1.0
    return matrix


def multi_hot_tokens(frame: pd.DataFrame, column: str, vocabulary: tuple[int, ...]) -> np.ndarray:
    matrix = np.zeros((len(frame), len(vocabulary)), dtype=np.float32)
    if not vocabulary or column not in frame.columns:
        return matrix
    positions = vocabulary_positions(vocabulary)
    codes, uniques = pd.factorize(frame[column].fillna("").astype(str))
    lookup = np.zeros((len(uniques), len(vocabulary)), dtype=np.float32)
    for row, raw in enumerate(uniques):
        for token in parse_id_list(raw):
            position = positions.get(token)
            if position is not None:
                lookup[row, position] = 1.0
    known = codes >= 0
    matrix[known] = lookup[codes[known]]
    return matrix


def expected_calibration_error(
    actual: np.ndarray,
    predicted: np.ndarray,
    bins: int = 10,
) -> float:
    if len(actual) == 0:
        return 1.0
    boundaries = np.linspace(0, 1, bins + 1)
    indices = np.clip(np.digitize(predicted, boundaries, right=True) - 1, 0, bins - 1)
    error = 0.0
    for index in range(bins):
        mask = indices == index
        if mask.any():
            error += mask.mean() * abs(float(actual[mask].mean()) - float(predicted[mask].mean()))
    return float(error)


def hash_path(path: Iterable[int]) -> str:
    return hashlib.sha256(",".join(str(value) for value in path).encode()).hexdigest()


def insert_estimates(connection, rows: list[tuple]) -> None:
    connection.executemany(
        """
        INSERT INTO "AdjustedActionEstimates" (
            "Id", "GenerationId", "ChampionId", "Role", "OpponentChampionId", "Patch",
            "RegionScope", "DecisionFamily", "Stage", "PathPrefixHash", "PathPrefixJson",
            "ActionKey", "ActionIdsJson", "AdjustedWpa", "ConfidenceLow", "ConfidenceHigh",
            "RawWinRate", "PickRate", "ObservedCount", "EffectiveSampleSize",
            "AverageTimingMinutes", "PropensityOverlap", "CovariateBalance",
            "StableAcrossFolds", "IsPublishable", "EvidenceQuality", "FallbackScope",
            "BaselineDefinition", "UnavailableReason", "ComputedAtUtc"
        ) VALUES (
            %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s::jsonb, %s, %s::jsonb,
            %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, NOW()
        )
        ON CONFLICT DO NOTHING
        """,
        rows,
    )


def insert_path_estimates(connection, rows: list[tuple]) -> None:
    if not rows:
        return
    connection.executemany(
        """
        INSERT INTO "AdjustedPathEstimates" (
            "Id", "GenerationId", "ChampionId", "Role", "OpponentChampionId", "Patch",
            "RegionScope", "PathHash", "ItemPathJson", "EstimatedWinProbability",
            "AdjustedLift", "ConfidenceLow", "ConfidenceHigh", "ObservedCount",
            "EffectiveSampleSize", "IsPublishable", "UnavailableReason"
        ) VALUES (
            %s, %s, %s, %s, %s, %s, %s, %s, %s::jsonb, %s, %s, %s, %s, %s, %s, %s, %s
        )
        ON CONFLICT DO NOTHING
        """,
        rows,
    )


def upload_artifacts(settings: Settings, artifact_path: Path, generation_id: UUID) -> str:
    if not settings.s3_endpoint or not settings.s3_bucket:
        return artifact_path.as_uri()
    client = boto3.client(
        "s3",
        endpoint_url=settings.s3_endpoint,
        aws_access_key_id=settings.s3_access_key,
        aws_secret_access_key=settings.s3_secret_key,
    )
    for path in artifact_path.rglob("*"):
        if not path.is_file():
            continue
        relative = path.relative_to(artifact_path).as_posix()
        client.upload_file(str(path), settings.s3_bucket, f"build-lab/{generation_id}/{relative}")
    return f"s3://{settings.s3_bucket}/build-lab/{generation_id}/"


def prune_stale_artifacts(connection, settings: Settings, current_generation_id: UUID) -> None:
    """Delete artifact bundles for generations the coordinator no longer retains.

    Nothing else ever removes a bundle and the create job runs daily, so this volume is the one part
    of the pipeline that grows without bound on a single-box deployment. Retention mirrors
    BuildLabGenerationCoordinator.RetireOldGenerationsAsync; anything active, still in flight, or
    still promotable is kept whatever its age.
    """
    if not settings.artifact_dir.is_dir():
        return
    try:
        retained = retained_generation_ids(connection, settings.retained_generations)
    except Exception:
        # Housekeeping may never fail a generation, and a failed query leaves the connection in an
        # aborted transaction that the rest of the run still needs.
        connection.rollback()
        LOG.warning("Artifact retention could not be resolved; nothing was pruned.", exc_info=True)
        return
    retained.add(str(current_generation_id))
    for path in sorted(settings.artifact_dir.iterdir()):
        generation_id = canonical_generation_id(path.name)
        # Only bundles this pipeline wrote are ever removed; anything else on the volume is not ours.
        if generation_id is None or generation_id in retained or not path.is_dir():
            continue
        try:
            shutil.rmtree(path)
            LOG.info("Pruned the artifact bundle of unretained generation %s.", generation_id)
        except OSError:
            LOG.warning("Pruning the artifact bundle at %s failed.", path, exc_info=True)


def retained_generation_ids(connection, retained_generations: int) -> set[str]:
    rows = connection.execute(
        """
        WITH retained AS (
            SELECT "Id"
            FROM "BuildLabGenerations"
            WHERE "Status" IN (3, 5)
            -- PostgreSQL orders DESC as NULLS FIRST, so never-promoted rows would otherwise rank
            -- ahead of real promotions here exactly as they would in the coordinator.
            ORDER BY ("PromotedAtUtc" IS NOT NULL) DESC, "PromotedAtUtc" DESC, "CreatedAtUtc" DESC
            LIMIT %s
        )
        SELECT "Id"::text AS generation_id FROM retained
        UNION
        SELECT "Id"::text AS generation_id
        FROM "BuildLabGenerations"
        WHERE "IsActive" OR "Status" IN (0, 1, 2, 3)
        """,
        (max(2, retained_generations),),
    ).fetchall()
    return {
        canonical_generation_id(row["generation_id"] if isinstance(row, dict) else row[0]) or ""
        for row in rows
    }


def canonical_generation_id(value) -> str | None:
    try:
        return str(UUID(str(value)))
    except (ValueError, TypeError):
        return None


def mark_failed_safely(connection, generation_id, message: str, lease_owner: str) -> None:
    """A database error leaves the connection in a failed transaction; roll back before writing."""
    try:
        connection.rollback()
    except Exception:
        LOG.warning("Rollback before the failure write did not succeed.", exc_info=True)
    try:
        mark_failed(connection, generation_id, message, lease_owner)
    except Exception:
        LOG.exception(
            "Generation %s could not be marked failed and may need manual recovery.",
            generation_id,
        )


def mark_failed(connection, generation_id, message: str, lease_owner: str) -> None:
    # Modeling under this process's own lease is the only state this worker may fail. Without the
    # guard a late failure could stamp Failed onto a promoted generation, leaving an active row the
    # serving layer rejects and RollbackAsync cannot repair.
    failed = connection.execute(
        """
        UPDATE "BuildLabGenerations"
        SET "Status" = 4,
            "FailureReason" = %s,
            "CompletedAtUtc" = NOW(),
            "LeaseOwner" = NULL,
            "LeaseExpiresAtUtc" = NULL
        WHERE "Id" = %s
          AND "Status" = 1
          AND "LeaseOwner" = %s
          AND NOT "IsActive"
        """,
        (message[:1024], generation_id, lease_owner),
    )
    connection.commit()
    if failed.rowcount == 0:
        LOG.warning(
            "Generation %s was not marked failed: it is no longer being modeled under the lease "
            "held by %s.",
            generation_id,
            lease_owner,
        )


def json_value(value) -> list:
    if isinstance(value, list):
        return value
    if isinstance(value, str):
        return json.loads(value)
    return list(value or [])
