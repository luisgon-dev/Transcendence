from __future__ import annotations

import enum
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
import multiprocessing
from concurrent.futures import ProcessPoolExecutor
import pathlib
from pathlib import Path
from typing import Iterable, Sequence
from uuid import UUID, uuid4

import boto3
import joblib
import numpy as np
from scipy.special import erfc
import pandas as pd
import psycopg
from psycopg.rows import dict_row, tuple_row
from threadpoolctl import threadpool_limits

from .cache import TrainingCache
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
def container_cpu_quota() -> int:
    """CPUs this container may actually use, which is never what `nproc` reports.

    `nproc`, `os.cpu_count()` and OpenBLAS all read the HOST's core count -- a cgroup cpu quota does
    not change it. That single fact has cost this pipeline two separate incidents: OpenBLAS sizing a
    46-thread pool against a 3-cpu quota (#167), and a worker pool sized the same way exhausting
    thread resources outright. Reading the quota is the only honest answer, so every default that
    scales with "how many cores do I have" derives from here.
    """
    try:
        quota, period = pathlib.Path("/sys/fs/cgroup/cpu.max").read_text().split()
        if quota != "max":
            return max(1, int(int(quota) / int(period)))
    except (OSError, ValueError):
        pass
    try:  # cgroup v1
        quota = int(pathlib.Path("/sys/fs/cgroup/cpu/cpu.cfs_quota_us").read_text())
        period = int(pathlib.Path("/sys/fs/cgroup/cpu/cpu.cfs_period_us").read_text())
        if quota > 0:
            return max(1, quota // period)
    except (OSError, ValueError):
        pass
    return max(1, os.cpu_count() or 1)


def tune_session(connection, settings: "Settings") -> None:
    """Per-session planner budget for the modeler's own connections.

    The server is tuned for the web app -- 24MB work_mem and two parallel workers per gather, which
    are sane for many small OLTP queries and badly wrong for a handful of 200k-row analytical joins.
    Setting them on this connection leaves every other client untouched. `max_parallel_workers_per_
    gather` is still capped by the server-wide `max_parallel_workers` pool, so raising it here only
    helps once that pool is raised too.
    """
    connection.execute(f"SET work_mem = '{settings.session_work_mem}'")
    connection.execute(
        f"SET max_parallel_workers_per_gather = {settings.session_parallel_workers}"
    )


# Opponent- and region-scoped cells below this many rows cannot reach BuildLabEvidenceGate's
# MinimumObservedActions (1000), so they are never expanded. 0 restores the pre-prune behaviour.
FINE_SCOPE_MIN_ROWS = max(0, int(os.getenv("BUILD_LAB_FINE_SCOPE_MIN_ROWS", "1000")))

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
# Disagreement, in sigma, at which a borrowed cell keeps ~61% of its weight. Two sigma roughly
# halves it and a real meta break drives it to zero, with no cliff to hand-tune.
COMMENSURABILITY_TOLERANCE_Z = 2.0
# A borrowed cell the current patch has never observed cannot be checked for drift either way, so it
# is admitted at a fraction of its recency weight rather than trusted or discarded outright.
UNVERIFIED_BORROW_WEIGHT = 0.35
# Champions with no published roles pool at the role level, exactly as before archetypes existed.
UNKNOWN_ARCHETYPE = "unknown"
# Lift, in win-probability points, separating "typical" from above/below average. Mirrors
# BuildLabEvidenceGate.BucketThreshold; the two must move together.
BUCKET_THRESHOLD = 0.005
EMPTY_PATH_HASH = hashlib.sha256(b"").hexdigest()
ID_PATTERN = re.compile(r"\d+")


class ShutdownRequested(RuntimeError):
    """Raised from the SIGTERM/SIGINT handler so an in-flight generation is marked failed."""


class RunOutcome(enum.Enum):
    """
    What one invocation actually did, so a run-to-completion process can exit meaningfully.

    A oneshot is scheduled and observed by systemd, which only sees the exit code: a generation that
    failed its gates must not look the same as a tick with nothing to do, or a broken pipeline reports
    success forever.
    """

    IDLE = "idle"
    COMPLETED = "completed"
    FAILED = "failed"


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
    max_training_rows: int
    training_sample_matches: int
    cache_training_draw: bool
    training_draw_max_age_hours: float
    sweep_workers: int
    sweep_blas_threads: int
    session_work_mem: str
    session_parallel_workers: int
    calibration_bands: int
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
            max_training_rows=max(20_000, int(os.getenv("BUILD_LAB_MAX_TRAINING_ROWS", "250000"))),
            # How many whole matches the structural fit draws. Sized so the sample yields well over
            # max_training_rows of decisions while its raw item events stay a small fraction of the
            # corpus, which is what keeps peak memory independent of how long the cohort has been
            # accumulating.
            training_sample_matches=max(
                2_000, int(os.getenv("BUILD_LAB_TRAINING_SAMPLE_MATCHES", "12000"))
            ),
            # Mirrors BuildLabModelingOptions.RetainedGenerations and the Math.Max(2, ...) floor the
            # coordinator applies, so artifact retention and row retention keep the same set.
            # On by default: a generation's cohort is frozen by its cutoff, so a cached draw is
            # always the draw that cohort would produce, and a retry after a mid-run failure no longer
            # repays tens of minutes of query time before modelling starts.
            cache_training_draw=os.getenv("BUILD_LAB_CACHE_TRAINING_DRAW", "true").lower() == "true",
            # How stale a reused training draw may be. Cohorts are nested, so yesterday's draw is a
            # valid sample of today's cohort minus the newest matches -- acceptable for the structural
            # nuisance model, and the cutoff actually used is recorded in the manifest. 0 disables reuse
            # across cutoffs entirely.
            training_draw_max_age_hours=max(
                0.0, float(os.getenv("BUILD_LAB_TRAINING_DRAW_MAX_AGE_HOURS", "36"))
            ),
            # Defaults to sequential, on measurement rather than principle.
            #
            # Champions are independent and the host has 43 idle cores, so fanning out looks obvious.
            # It is not: the sweep is ~90% waiting on Postgres, and prod's database is HDD-backed, so
            # concurrent scans thrash the disk instead of overlapping. Measured on the live cohort, the
            # same three champions took 432s sequentially and 1349s across three workers -- parallelism
            # was 3x SLOWER. The pool is kept because it is correct and the arithmetic changes on SSD or
            # once the timeline amplification is removed, but it must be opted into.
            # Defaults to the container's cpu quota, no longer to 1.
            #
            # The old default was sequential on measurement: three workers took 1349s against 432s
            # sequential. That measurement was correct and is now obsolete, because its cause has
            # been removed. Each worker streamed its OWN copy of the cohort event state -- N
            # concurrent 16M-row scans of an HDD-backed database, which thrashes rather than
            # overlaps. The parent now reduces that state once and hands workers a file, so the
            # per-worker cost of joining the sweep is a local read of a few hundred MB instead of a
            # 74-minute scan, and the arithmetic the old comment anticipated ("changes ... once the
            # timeline amplification is removed") has changed.
            #
            # Sized from the cgroup quota rather than nproc: the container is given far fewer cpus
            # than the host has, and every previous attempt to scale with nproc oversubscribed.
            sweep_workers=max(
                1, int(os.getenv("BUILD_LAB_SWEEP_WORKERS", str(container_cpu_quota())))
            ),
            # BLAS threads during the estimate sweep, and the single biggest lever on its runtime.
            #
            # Measured on the sweep's real fit shape -- 600 rows by DESIGN_MATRIX_MAX_COLUMNS (1,044)
            # wide, 15 fits per (scope, path), under a 3-cpu quota:
            #
            #     cap=1   6 ms/fit      cap=4    65 ms/fit
            #     cap=2   2 ms/fit      cap=8   112 ms/fit
            #     cap=3   3 ms/fit      uncapped (24 threads)  2,744 ms/fit
            #
            # Threading helps up to the quota and collapses past it, and uncapped is 400x worse than
            # any capped setting. Width is what makes it so violent: the same benchmark at 40 columns
            # shows no difference at all, which is how this stayed invisible.
            #
            # Defaults to 1 rather than the measured-best 2 because `nproc` cannot be trusted to
            # describe the quota -- it reports the host's cores -- so 1 is the only value that cannot
            # oversubscribe whatever the container is actually given.
            sweep_blas_threads=max(1, int(os.getenv("BUILD_LAB_SWEEP_BLAS_THREADS", "1"))),
            # How finely calibration is conditioned on game phase. The promotion gate scores ECE
            # within time bands, so this is the dial that moves the gate that is hardest to pass.
            calibration_bands=max(
                1, int(os.getenv("BUILD_LAB_CALIBRATION_BANDS", str(CALIBRATION_BANDS)))
            ),
            # The server's 24MB work_mem is sized for the web app's many small queries; the sweep
            # runs a few large joins per champion and sorts inside them.
            #
            # Deliberately modest, because this number multiplies harder than it looks: work_mem is
            # per sort/hash NODE, every parallel worker inside a query gets its own, and the sweep
            # runs one such query per sweep worker. Worst case is roughly
            #   work_mem x nodes x (1 + session_parallel_workers) x sweep_workers
            # so 128MB across 4 sweep workers with 2 parallel workers each is already several GB.
            # Raise it against measured plans and the box's free RAM, not on principle.
            session_work_mem=os.getenv("BUILD_LAB_SESSION_WORK_MEM", "128MB"),
            session_parallel_workers=max(
                0, int(os.getenv("BUILD_LAB_SESSION_PARALLEL_WORKERS", "2"))
            ),
            retained_generations=max(2, int(os.getenv("BUILD_LAB_RETAINED_GENERATIONS", "4"))),
        )


def run() -> RunOutcome:
    settings = Settings.from_env()
    settings.artifact_dir.mkdir(parents=True, exist_ok=True)
    install_shutdown_handlers()
    while True:
        try:
            outcome = process_next(settings)
            if settings.run_once:
                return outcome
            if outcome is RunOutcome.IDLE:
                # The signal handler raises here too, so a container stop during the idle poll exits
                # cleanly instead of unwinding as an unhandled error.
                time.sleep(settings.poll_seconds)
        except ShutdownRequested:
            LOG.info("Shutdown requested; the modeler is stopping.")
            return RunOutcome.IDLE
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


def process_next(settings: Settings) -> RunOutcome:
    with psycopg.connect(settings.database_url, row_factory=dict_row) as connection:
        tune_session(connection, settings)
        # Exclusivity for the whole run, released by the session itself if this process dies.
        if not try_acquire_modeling_lock(connection):
            LOG.info("Another modeler holds the modeling lock; nothing to do this tick.")
            return RunOutcome.IDLE
        try:
            generation = lease_generation(connection, settings)
            if generation is None:
                return RunOutcome.IDLE
            try:
                model_generation(connection, generation, settings)
            except ShutdownRequested as exc:
                mark_failed_safely(connection, generation["Id"], str(exc), settings.lease_owner)
                raise
            except Exception as exc:
                LOG.exception("Build Lab generation %s failed.", generation["Id"])
                mark_failed_safely(connection, generation["Id"], str(exc), settings.lease_owner)
                return RunOutcome.FAILED
            return RunOutcome.COMPLETED
        finally:
            release_modeling_lock(connection)


MODELING_LOCK_KEY = "build-lab-generation-modeling"


def try_acquire_modeling_lock(connection: psycopg.Connection) -> bool:
    """
    Session-scoped advisory lock held for the whole run.

    This replaces an application-level lease with heartbeats and an expiry column. PostgreSQL ties a
    session lock to the connection, so a crashed, OOM-killed, or `docker kill`ed modeler releases it
    the moment its TCP session drops — with no timer thread to schedule, no deadline to renew, and no
    reaper timeout to keep in sync across two languages. The previous design reaped six consecutive
    healthy generations because the renewal thread could not win the GIL against a pandas load.

    It is also the convention already used for every other long exclusive job here; see
    RefreshBuildResourceAnalyticsJob and MatchTimelineIngestionJob.
    """
    acquired = connection.execute(
        "SELECT pg_try_advisory_lock(hashtextextended(%s, 0))", (MODELING_LOCK_KEY,)
    ).fetchone()
    held = bool(next(iter(acquired.values()))) if acquired else False
    # psycopg opens a transaction on that SELECT, which would make the subsequent claim a savepoint
    # and hide `Modeling` from every other session until the whole run committed. A session advisory
    # lock is not transaction-scoped, so committing here keeps the lock and lets the claim be its own
    # durable, immediately-visible transaction.
    connection.commit()
    return held


def release_modeling_lock(connection: psycopg.Connection) -> None:
    try:
        connection.execute(
            "SELECT pg_advisory_unlock(hashtextextended(%s, 0))", (MODELING_LOCK_KEY,)
        )
    except Exception:  # pragma: no cover - the session is already gone, which releases it anyway
        LOG.warning("Could not release the modeling advisory lock; the session drop will free it.")


def lease_generation(connection: psycopg.Connection, settings: Settings) -> dict | None:
    """Claim the oldest pending generation. The caller must already hold the modeling lock."""
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
                "LeaseOwner" = %s
            WHERE "Id" = %s AND "Status" = 0
            """,
            (settings.lease_owner, generation["Id"]),
        )
        if claimed.rowcount == 0:
            # The row moved between the lock and the claim, so it belongs to whoever moved it.
            LOG.warning(
                "Generation %s was no longer pending when it was claimed.",
                generation["Id"],
            )
            return None
    # Publish the claim before the long run starts, so the admin surface and the reaper both see
    # `Modeling` rather than a row that still looks unclaimed for hours.
    connection.commit()
    return generation



def training_draw_shape(
    connection: psycopg.Connection,
    included_patches: list[str],
    cutoff,
    settings: "Settings",
) -> tuple[list[tuple], int]:
    """The id ranges the draw reads and the row cap each one is thinned to."""
    match_count = load_cohort_match_count(connection, included_patches, cutoff)
    ranges = load_training_sample_ranges(
        connection,
        included_patches,
        cutoff,
        match_count,
        settings.training_sample_matches,
        TRAINING_SAMPLE_SLICES,
    )
    return ranges, max(1, settings.max_training_rows // TRAINING_SAMPLE_SLICES)


def build_training_frame(
    connection: psycopg.Connection,
    included_patches: list[str],
    cutoff,
    rank_offset: str | None,
    current_patch: str,
    changed_items: set[int],
    settings: "Settings",
    prepare,
    *,
    cache: "TrainingCache | None" = None,
) -> pd.DataFrame:
    """The draw the structural fit is trained on, one hash-disjoint slice at a time.

    This model was always fit on at most `max_training_rows`; the shape this replaced reached that by
    loading every decision row in the cohort and then discarding over 99% of them, which is what made
    peak memory scale with the corpus. Sampling whole matches in the query keeps the same row budget --
    whole matches, not rows, so the chronological train/calibration/test split still partitions by
    match and `evaluate_leakage` still sees disjoint splits.

    Each slice is thinned before the next is drawn, so the whole draw is never resident at once, and
    cached on the way past, so a retry or a model-only iteration does not pay for the queries again.
    """
    sample_ranges, slice_rows = training_draw_shape(connection, included_patches, cutoff, settings)
    drawn_at = cache.drawn_at() if cache else None
    if drawn_at is not None and cache is not None and cache.is_fresh_for(cutoff):
        LOG.info("Reusing the training draw taken for cutoff %s.", drawn_at)
    LOG.info(
        "Training draw: %d id ranges, up to %d rows each%s.",
        len(sample_ranges),
        slice_rows,
        f" (cache {cache.key})" if cache and cache.enabled else " (uncached)",
    )
    slices = []
    for residue, sample_range in enumerate(sample_ranges):
        reusable = cache is not None and (cache.is_fresh_for(cutoff) or cache.drawn_at() is None)
        cached = cache.read_slice(residue) if reusable else None
        if cached is not None:
            LOG.info(
                "Training slice %d/%d: %d rows from cache.",
                residue + 1,
                len(sample_ranges),
                len(cached),
            )
            slices.append(cached)
            continue
        drawn = prepare(
            load_decision_frame(
                connection,
                included_patches,
                cutoff,
                rank_offset,
                current_patch,
                changed_items,
                match_sample_range=sample_range,
            ),
            # `exclude_drifted_prior_actions` fires on cells with at least 100 observations in both
            # the current and a prior patch. Those counts are divided by the sampling rate here, so
            # applying it would drop an arbitrary subset of cells rather than a conservative one. It
            # guards *borrowing into a per-cell estimate*, which is not what this model does: the
            # model is P(win | pre-decision state) and is held to the calibration and held-out-patch
            # gates instead.
            exclude_drift=False,
        )
        kept = thin_chronologically(drawn, slice_rows)
        LOG.info(
            "Training slice %d/%d: %d rows drawn, %d kept.",
            residue + 1,
            len(sample_ranges),
            len(drawn),
            len(kept),
        )
        del drawn
        if cache:
            cache.write_slice(residue, kept)
        slices.append(kept)
    return pd.concat(slices, ignore_index=True) if slices else pd.DataFrame()



# One connection and one model bundle per worker process, built on first use. Rebuilding them per
# champion would cost a connect and an unpickle 173 times over.
_WORKER: dict = {}


def _sweep_worker_setup(
    settings: "Settings",
    cohort: dict,
    bundle_path: str,
    connection=None,
    bundle=None,
    event_state_path: str | None = None,
    event_state: "CohortEventState | None" = None,
) -> None:
    """Per-process state for the sweep.

    In a worker process there is no connection or model to inherit, so both are built here -- once per
    process rather than once per champion. Run in-process, the parent hands over what it already holds,
    which avoids a redundant connection and a redundant unpickle of the model.
    """
    _WORKER.clear()
    _WORKER["settings"] = settings
    _WORKER["cohort"] = cohort
    _WORKER["owns_connection"] = connection is None
    _WORKER["connection"] = (
        connection
        if connection is not None
        else psycopg.connect(settings.database_url, row_factory=dict_row)
    )
    if connection is None:  # a connection handed down by the parent is already tuned
        tune_session(_WORKER["connection"], settings)
    _WORKER["bundle"] = bundle if bundle is not None else joblib.load(bundle_path)
    # Reduced once by the parent and shared, never re-derived per worker.
    #
    # This read is the sweep's single largest fixed cost -- a 16M-row scan of the payload table, 74
    # minutes on prod. Deriving it per worker is what made the pool slower than sequential: N
    # workers meant N concurrent scans of the same rows on a spinning disk. The parent now reduces
    # it once and passes either the object (in-process) or a parquet directory (spawned workers),
    # so both paths sweep against a byte-identical state and a worker costs a local read to start.
    if event_state is not None:
        _WORKER["event_state"] = event_state
    elif event_state_path is not None:
        _WORKER["event_state"] = read_cohort_event_state(Path(event_state_path))
    else:
        _WORKER["event_state"] = stream_cohort_event_state(
            _WORKER["connection"],
            cohort["included_patches"],
            cohort["cutoff"],
            load_participant_teams(
                _WORKER["connection"], cohort["included_patches"], cohort["cutoff"]
            ),
        )


def _sweep_pool_setup(
    settings: "Settings", cohort: dict, bundle_path: str, event_state_path: str
) -> None:
    """Pool initializer. `initargs` is a positional tuple with no keyword support, so a wrapper that
    names exactly what a spawned worker needs is safer than padding the general signature with
    placeholder Nones that silently shift if it ever gains an argument."""
    _sweep_worker_setup(settings, cohort, bundle_path, event_state_path=event_state_path)


def sweep_champion(champion_id: int) -> dict:
    """Produce one champion's estimate records. Runs in a worker process.

    Returns records rather than writing estimates: pooling is cross-champion and has to happen once,
    in the parent, after every champion is in.
    """
    settings = _WORKER["settings"]
    cohort = _WORKER["cohort"]
    connection = _WORKER["connection"]
    frame = prepare_decisions(
        load_decision_frame(
            connection,
            cohort["included_patches"],
            cohort["cutoff"],
            cohort["rank_offset"],
            cohort["current_patch"],
            cohort["changed_items"],
            champion_id=champion_id,
            event_state=_WORKER["event_state"],
        ),
        cohort["current_patch"],
        cohort["included_patches"],
        cohort["rank_offset"],
        cohort["changes"],
        cohort["archetypes"],
        exclude_drift=True,
    )
    if frame.empty:
        return {"champion_id": champion_id, "rows": 0, "actions": [], "paths": [], "event_state": 0.0}
    # Every published number is anchored on the calibrated model, so the calibration gates in the .NET
    # promoter actually govern what is served.
    frame["baseline_win_probability"] = structural_win_probability(_WORKER["bundle"], frame)
    actions = action_records(frame)
    paths = path_records(frame)
    # champion_id leads the partitioning so each champion writes into its own directory: workers cannot
    # collide on a file name even though they write concurrently.
    deidentified_export(frame, settings.deidentification_salt).to_parquet(
        Path(cohort["artifact_path"]) / "dataset",
        index=False,
        partition_cols=["champion_id", "patch", "region"],
    )
    return {
        "champion_id": champion_id,
        "rows": len(frame),
        "actions": actions,
        "paths": paths,
        "event_state": float(frame["has_event_state"].sum()),
    }


def model_generation(
    connection: psycopg.Connection,
    generation: dict,
    settings: Settings,
) -> None:
    generation_id = UUID(str(generation["Id"]))
    included_patches = json_value(generation["IncludedPatchesJson"])
    regions = json_value(generation["IncludedRegionsJson"])
    cutoff = generation["SourceCutoffUtc"]
    current_patch = generation["Patch"]
    rank_offset = resolve_rank_offset_column(connection)
    changes = load_patch_change_set(connection, included_patches)
    archetypes = load_champion_archetypes(connection, current_patch)
    LOG.info(
        "Patch change set across %s: %d items, %d runes, %d champions; %d champions carry an archetype.",
        ", ".join(included_patches),
        len(changes.items),
        len(changes.runes),
        len(changes.champions),
        len(archetypes),
    )
    changed_items = set(changes.items)

    def prepare(frame: pd.DataFrame, *, exclude_drift: bool = True) -> pd.DataFrame:
        return prepare_decisions(
            frame,
            current_patch,
            included_patches,
            rank_offset,
            changes,
            archetypes,
            exclude_drift=exclude_drift,
        )

    prune_stale_artifacts(connection, settings, generation_id)
    artifact_path = settings.artifact_dir / str(generation_id)
    artifact_path.mkdir(parents=True, exist_ok=True)

    # Phase 1 - the structural model, fit on a deterministic sample of whole matches.
    training = build_training_frame(
        connection,
        included_patches,
        cutoff,
        rank_offset,
        current_patch,
        changed_items,
        settings,
        prepare,
        cache=TrainingCache.for_cohort(
            settings.artifact_dir / "_cache",
            included_patches,
            *training_draw_shape(connection, included_patches, cutoff, settings),
            enabled=settings.cache_training_draw,
            cutoff=cutoff,
            max_age_hours=settings.training_draw_max_age_hours,
        ),
    )
    if training.empty:
        raise RuntimeError("No eligible item decisions were available for this generation.")
    if training["won"].nunique() < 2:
        raise RuntimeError("The frozen dataset does not contain both match outcomes.")
    structural_model, metrics = train_structural_model(
        training, settings.max_training_rows, settings.calibration_bands
    )
    del training
    joblib.dump(structural_model, artifact_path / "win_probability.joblib")

    # Phase 2 - estimates, one champion at a time.
    #
    # Every grouping key in `action_records`/`path_records` starts with champion_id, and the borrowing
    # weights and drift exclusion are keyed on (champion, role, family, stage, action), so a champion
    # sweep produces the same records as the whole-cohort pass while only one champion's rows are ever
    # resident. `expand_scopes` quadruples its input, which is why the cohort-wide version could not
    # fit regardless of how the loads were shaped.
    champions = load_cohort_champions(connection, included_patches, cutoff)
    workers = max(1, min(settings.sweep_workers, len(champions) or 1))
    LOG.info("Sweeping %d champions across %d worker processes.", len(champions), workers)
    action_pool: list[dict] = []
    path_pool: list[dict] = []
    decision_rows = 0
    event_state_rows = 0.0
    cohort = {
        "included_patches": included_patches,
        "cutoff": cutoff,
        "rank_offset": rank_offset,
        "current_patch": current_patch,
        "changed_items": changed_items,
        "changes": changes,
        "archetypes": archetypes,
        "artifact_path": str(artifact_path),
    }

    def absorb(result: dict, position: int) -> None:
        nonlocal decision_rows, event_state_rows
        if result["rows"] == 0:
            LOG.info(
                "Champion %d/%d (%d) has no eligible rows.",
                position, len(champions), result["champion_id"],
            )
            return
        action_pool.extend(result["actions"])
        path_pool.extend(result["paths"])
        decision_rows += result["rows"]
        event_state_rows += result["event_state"]
        LOG.info(
            "Champion %d/%d (%d): %d rows, %d action records, %d path records.",
            position, len(champions), result["champion_id"],
            result["rows"], len(result["actions"]), len(result["paths"]),
        )

    # Applies to BOTH paths, which is the bug this closes. #162 capped BLAS threads for spawned
    # workers and made sequential the default in the same change, so the cap landed on the branch the
    # default never takes. Left uncapped, `nproc` reports the host's 46 cores (a cpu quota does not
    # change it) and OpenBLAS sizes its pools to match: prod ran 92 threads against a 3-cpu quota,
    # burning the quota on scheduling instead of arithmetic. Observed as ~300% cpu with almost no I/O,
    # one champion incomplete after 80 minutes, and every stack sample inside a logistic fit.
    #
    # threadpoolctl rather than the environment, because the environment cannot fix this process:
    # OpenBLAS reads those variables when it loads, and numpy is imported long before the sweep. This
    # retunes pools that are already running. The environment is still set for the pool, whose
    # children read it at import. Scoped to the sweep so the structural fit above keeps its threads --
    # that one is a single large fit, which does thread well.
    with threadpool_limits(limits=settings.sweep_blas_threads):
        LOG.info(
            "Sweep BLAS threads limited to %d (container quota is the binding constraint, not nproc).",
            settings.sweep_blas_threads,
        )
        # Reduced once, here, for both paths -- so the pool sweeps against the same state the
        # sequential reference does rather than N independently-derived copies of it.
        cohort_state = stream_cohort_event_state(
            connection,
            included_patches,
            cutoff,
            load_participant_teams(connection, included_patches, cutoff),
        )
        if workers == 1:
            # Kept as a real path, not a fallback nobody runs: it is what a constrained host uses, and it
            # is the reference the parallel path is checked against.
            _sweep_worker_setup(
                settings,
                cohort,
                str(artifact_path / "win_probability.joblib"),
                connection=connection,
                bundle=structural_model,
                event_state=cohort_state,
            )
            for position, champion_id in enumerate(champions, start=1):
                absorb(sweep_champion(champion_id), position)
        else:
            # A champion that raises must fail the generation rather than silently drop out: a missing
            # champion is a quietly incomplete estimate set, which is worse than no estimate set.
            # Workers get one BLAS thread each.
            #
            # `nproc` inside the container reports the HOST's cores -- a cpu quota does not change it -- so
            # OpenBLAS sizes its pool at ~46 threads per process. Four interpreters doing that against a
            # 3-cpu quota is not just oversubscription: it exhausted thread resources outright and the pool
            # died with `std::system_error: Resource temporarily unavailable`. Workers only score with an
            # already-fitted model, which is light linear algebra, so one thread each is right on the merits
            # as well. Set before the pool starts so spawned children inherit it ahead of importing numpy,
            # which is the only point at which OpenBLAS reads it. The structural fit above has already run,
            # so its own threading is unaffected.
            for variable in (
                "OMP_NUM_THREADS",
                "OPENBLAS_NUM_THREADS",
                "MKL_NUM_THREADS",
                "NUMEXPR_NUM_THREADS",
            ):
                os.environ.setdefault(variable, "1")

            # "spawn", never the Linux default of "fork".
            #
            # The structural fit immediately above runs BLAS/OpenMP threads. fork() copies only the calling
            # thread but inherits every mutex in whatever state it was in, so a lock held by a BLAS thread at
            # fork time is inherited already-locked and never released -- the child then deadlocks the first
            # time it touches that runtime. Observed exactly that on prod: four workers connected, opened a
            # transaction each, and sat idle-in-transaction for 45 minutes without completing one champion.
            # spawn starts a fresh interpreter with no inherited lock state. It costs a re-import per worker,
            # once, against a sweep measured in tens of minutes.
            state_path = write_cohort_event_state(
                cohort_state, artifact_path / "cohort_event_state"
            )
            LOG.info(
                "Cohort event state shared with %d workers from %s (%d rows).",
                workers, state_path, len(cohort_state.cumulative),
            )
            with ProcessPoolExecutor(
                max_workers=workers,
                mp_context=multiprocessing.get_context("spawn"),
                initializer=_sweep_pool_setup,
                initargs=(
                    settings,
                    cohort,
                    str(artifact_path / "win_probability.joblib"),
                    str(state_path),
                ),
            ) as pool:
                for position, result in enumerate(pool.map(sweep_champion, champions), start=1):
                    absorb(result, position)

    if not action_pool:
        raise RuntimeError("No adjusted action estimates could be produced.")
    event_state_coverage = event_state_rows / decision_rows if decision_rows else 0.0
    if event_state_coverage <= 0:
        LOG.warning(
            "No match in the frozen cohort carries timeline event payloads. The rows are schema v2 "
            "but were ingested with Analytics:BuildLab:Enabled off, so kill and objective state is "
            "absent rather than even."
        )

    # Pooling is the one genuinely cross-champion step, and it borrows strength between champions
    # through the small per-cell records rather than the rows behind them.
    apply_partial_pooling(action_pool, ACTION_POOLING_LEVELS)
    apply_partial_pooling(path_pool, PATH_POOLING_LEVELS)
    estimates = action_tuples(action_pool, generation_id, current_patch)
    path_estimates = path_tuples(path_pool, generation_id, current_patch)

    manifest = {
        "generationId": str(generation_id),
        "datasetVersion": generation["DatasetVersion"],
        "modelVersion": "dr-logit-isotonic-v2",
        "patches": included_patches,
        "regions": regions,
        "sourceCutoffUtc": cutoff.isoformat(),
        "rows": decision_rows,
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
                "LeaseOwner" = NULL
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


def load_decision_frame(
    connection: psycopg.Connection,
    included_patches: list[str],
    cutoff,
    rank_offset: str | None,
    current_patch: str,
    changed_items: set[int],
    *,
    champion_id: int | None = None,
    match_sample_range: tuple | None = None,
    event_state: "CohortEventState | None" = None,
) -> pd.DataFrame:
    """Load one scope of the frozen cohort and turn it into unweighted decision rows.

    The scope is either a champion (the estimate sweep) or a deterministic sample of matches (the
    structural fit). Item events are participant-scoped, while team composition and kill state are
    match-scoped, because a kill diff is a fact about the match rather than about one participant.

    `event_state` is the sweep's cohort-wide event state, streamed and reduced once per process.
    Supplying it replaces this scope's timeline query with a join against counters already computed;
    omitting it queries and reduces per scope, which is what the structural fit does since its match
    sample is drawn once rather than 173 times.
    """
    scope = {"champion_id": champion_id, "match_sample_range": match_sample_range}
    item_events = load_item_events(
        connection, included_patches, cutoff, rank_offset, **scope
    )
    if item_events.empty:
        return pd.DataFrame()
    item_decisions = build_item_decisions(item_events)
    del item_events
    teams = load_participant_teams(connection, included_patches, cutoff, **scope)
    item_decisions = (
        apply_event_state(item_decisions, event_state, teams)
        if event_state is not None
        else enrich_with_predecision_event_state(
            item_decisions,
            load_timeline_state_events(connection, included_patches, cutoff, **scope),
            teams,
        )
    )
    item_decisions = exclude_incompatible_prior_item_rows(
        item_decisions,
        current_patch,
        changed_items,
    )
    return pd.concat(
        [
            item_decisions,
            build_rune_decisions(
                load_rune_decisions(connection, included_patches, cutoff, rank_offset, **scope)
            ),
            build_spell_decisions(
                load_spell_decisions(connection, included_patches, cutoff, rank_offset, **scope)
            ),
        ],
        ignore_index=True,
    )


def thin_chronologically(frame: pd.DataFrame, max_rows: int) -> pd.DataFrame:
    """Systematic chronological subsample, matching how `train_structural_model` bounds its own fit.

    Deduplicated on the frame identity first so `max_rows` is measured in the same unit the fit uses.
    """
    if frame.empty:
        return frame
    ordered = frame.sort_values(["match_date", "match_id"]).drop_duplicates(
        ["match_id", "participant_id", "minute"]
    )
    if len(ordered) <= max_rows:
        return ordered
    return ordered.iloc[:: math.ceil(len(ordered) / max_rows)]


def prepare_decisions(
    frame: pd.DataFrame,
    current_patch: str,
    included_patches: list[str],
    rank_offset: str | None,
    changes: "PatchChangeSet",
    archetypes: dict[int, str],
    *,
    exclude_drift: bool,
) -> pd.DataFrame:
    """Attach archetypes and borrowing weights, and drop rows no estimate may use.

    Every step here is keyed on the champion or on the row itself, so this is safe to apply to one
    champion's rows in isolation.
    """
    if frame.empty:
        return frame
    frame = frame.assign(
        archetype=frame["champion_id"].astype(int).map(archetypes).fillna(UNKNOWN_ARCHETYPE)
    )
    frame = apply_row_weights(frame, current_patch, included_patches, rank_offset, changes)
    if exclude_drift:
        frame = exclude_drifted_prior_actions(frame, current_patch)
    return frame


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
    changes: PatchChangeSet | None = None,
) -> pd.DataFrame:
    recency = patch_recency_weights(current_patch, included_patches)
    weights = decisions["patch"].map(recency).fillna(0.0).astype(float)
    if changes is not None and not decisions.empty:
        # Recency is the ceiling; commensurability decides how much of it a borrowed row keeps.
        # The product flows into the Kish ESS, so over-borrowing cannot manufacture precision — the
        # effective-sample gate absorbs it.
        weights = weights * commensurability_weights(decisions, current_patch, changes)
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
    """Recency floor only. Adaptive borrowing scales these per cell; see `commensurability_weights`."""
    ordered = [current_patch] + [patch for patch in included_patches if patch != current_patch]
    return {
        patch: weight
        for patch, weight in zip(ordered[:3], [1.0, 0.60, 0.35], strict=False)
    }


def commensurability_weights(
    decisions: pd.DataFrame,
    current_patch: str,
    changes: PatchChangeSet,
) -> pd.Series:
    """
    Per-row borrowing multiplier in [0, 1] for rows from a *prior* patch.

    Patches ship fortnightly, so a fixed per-patch weight is the wrong instrument: it discards good
    data from the ~94% of champions a patch does not touch, and keeps data from the ones it does.
    This scales each borrowed row by how commensurable its cell still looks:

      * a static change to the champion, the item, or a rune in the action -> 0 (hard exclude).
        Riot told us the cell changed, so no amount of agreement should rescue it.
      * otherwise a power-prior style discount on the current-vs-prior disagreement in observed
        outcome for that exact cell, so an unchanged cell borrows at near full strength and a cell
        that drifted for reasons the static data cannot see decays smoothly to zero.

    The discount is a z-score, not a raw win-rate delta: a thinly observed prior cell must not be
    thrown away for noise, and must not be trusted merely because it is thin.

    Current-patch rows are always 1.0 and are never touched here.
    """
    if decisions.empty:
        return pd.Series(dtype=float)

    is_prior = decisions["patch"].astype(str) != current_patch
    weights = pd.Series(1.0, index=decisions.index)
    if not is_prior.any():
        return weights

    action_ids = decisions["action_ids"].map(_parse_action_ids)
    blocked = pd.Series(False, index=decisions.index)
    if changes.champions:
        blocked |= decisions["champion_id"].astype(int).isin(changes.champions)
    if changes.items or changes.runes:
        touched = frozenset(changes.items | changes.runes)
        blocked |= action_ids.map(lambda ids: bool(touched.intersection(ids)))

    weights = weights.where(~(is_prior & blocked), 0.0)

    key_columns = ["champion_id", "role", "family", "stage", "action_key"]
    summary = (
        decisions.assign(_is_current=~is_prior)
        .groupby(key_columns + ["_is_current"], dropna=False)["won"]
        .agg(["count", "mean"])
        .reset_index()
    )
    current = summary.loc[summary["_is_current"]].set_index(key_columns)
    prior = summary.loc[~summary["_is_current"]].set_index(key_columns)
    shared = current.index.intersection(prior.index)
    if len(shared) == 0:
        # Nothing to compare against: an unseen cell keeps only the recency floor.
        return weights.where(~is_prior, weights * UNVERIFIED_BORROW_WEIGHT)

    current_n = current.loc[shared, "count"].to_numpy(dtype=float)
    prior_n = prior.loc[shared, "count"].to_numpy(dtype=float)
    current_p = current.loc[shared, "mean"].to_numpy(dtype=float)
    prior_p = prior.loc[shared, "mean"].to_numpy(dtype=float)
    pooled = (current_p * current_n + prior_p * prior_n) / np.maximum(current_n + prior_n, 1.0)
    standard_error = np.sqrt(
        np.maximum(pooled * (1.0 - pooled), 1e-6) * (1.0 / np.maximum(current_n, 1.0) + 1.0 / np.maximum(prior_n, 1.0))
    )
    z = np.abs(current_p - prior_p) / np.maximum(standard_error, 1e-6)
    # Gaussian decay: agreement (z~0) borrows fully, a two-sigma split roughly halves the row, and a
    # genuine meta break drives the cell to zero without a cliff to tune.
    alpha = np.exp(-0.5 * np.square(z / COMMENSURABILITY_TOLERANCE_Z))

    lookup = pd.Series(alpha, index=shared)
    row_keys = pd.MultiIndex.from_frame(decisions[key_columns])
    row_alpha = pd.Series(row_keys.map(lookup), index=decisions.index).astype(float)
    # A prior cell with no current-patch counterpart cannot be verified either way.
    row_alpha = row_alpha.fillna(UNVERIFIED_BORROW_WEIGHT)

    return weights.where(~is_prior, weights * row_alpha)


def _parse_action_ids(value: object) -> frozenset[int]:
    try:
        return frozenset(int(item) for item in json.loads(str(value)))
    except (TypeError, ValueError, json.JSONDecodeError):
        return frozenset()


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



# A generation's raw event volume is the thing that made the modeler unrunnable: 28.8M item-event rows
# at 62k matches, materialised as one pandas row each. Cells never span champions, so the sweep loads
# one champion at a time and keeps only the small per-cell records. These predicates are what scope
# each loader to a champion without changing which rows the union produces.
CHAMPION_EVENT_FILTER = '          AND p."ChampionId" = %(champion_id)s\n'
# Team composition and kill state span the whole match, so every loader also restricts to the matches
# the champion appears in rather than to its own participant rows.
CHAMPION_MATCH_FILTER = (
    '          AND m."Id" IN (SELECT "MatchId" FROM champion_matches)\n'
)
# The champion's cohort matches, computed once and up front.
#
# Without MATERIALIZED the planner inlines the predicate, drives the whole query off a sequential scan
# of the timeline fetch states, and re-filters `ChampionId` per match — so every champion in the sweep
# re-reads the entire cohort. Measured on prod, that shape did not finish a single champion in six
# minutes; forcing the small match set to be built first from the ChampionId index brings one champion
# to under six seconds. GROUP BY rather than DISTINCT because the planner costs the HashAggregate more
# accurately here.
CHAMPION_MATCH_CTE = """champion_matches AS MATERIALIZED (
    SELECT cp."MatchId" AS "MatchId"
    FROM "MatchParticipants" cp
    JOIN "Matches" cm ON cm."Id" = cp."MatchId"
    WHERE cp."ChampionId" = %(champion_id)s
      AND cm."Patch" = ANY(%(patches)s)
      AND cm."Status" = 1
      AND cm."FetchedAt" <= %(cutoff)s
      AND cm."QueueId" = 420
      AND cm."Duration" >= 300
    GROUP BY cp."MatchId"
)"""


def read_sql_frame(connection, sql: str, params: dict) -> pd.DataFrame:
    """Run a query and build a DataFrame from it, with an explicitly tuple-shaped cursor.

    `pd.read_sql_query` must not be used on this connection. The run's connection is opened with
    `row_factory=dict_row` because the generation row is read by column name, and pandas' DBAPI2
    fallback feeds whatever `fetchall()` returns straight into `DataFrame.from_records`. Handed dicts
    it iterates each one -- which yields its KEYS -- so every cell in the frame comes back equal to its
    own column name. That is silent: the row count is right, the dtypes are just `object`, and the
    corruption only surfaces much later as `int('event_type')`. Naming the row factory here removes the
    coupling to the connection's, and skips pandas' "not tested" DBAPI2 path entirely.
    """
    with connection.cursor(row_factory=tuple_row) as cursor:
        cursor.execute(sql, params)
        columns = [column.name for column in cursor.description or []]
        return normalise_uuid_columns(pd.DataFrame.from_records(cursor.fetchall(), columns=columns))


def normalise_uuid_columns(frame: pd.DataFrame) -> pd.DataFrame:
    """Render uuid columns as their canonical strings at the loader boundary.

    psycopg returns `uuid` as `uuid.UUID`, and Arrow cannot infer a type for it, so any frame still
    carrying a match id fails to serialise -- which is every frame the training cache stores. Coercing
    at the boundary keeps one type in play everywhere instead: a cached slice and a freshly drawn one
    must not disagree, or concatenating them yields a column mixing UUID with str whose sort raises.

    This does not change any published value. `surrogate_ids` interpolates the id into a string, and
    `str(UUID)` is the canonical hyphenated form, so the deidentified surrogates hash identically.
    """
    for column in frame.columns:
        if frame[column].dtype != object:
            continue
        present = frame[column].dropna()
        if present.empty or not isinstance(present.iloc[0], UUID):
            continue
        frame[column] = frame[column].map(lambda value: None if value is None else str(value))
    return frame


def champion_match_cte(champion_id: int | None, *, leading: bool) -> str:
    """The champion_matches CTE, or nothing when the load is not champion-scoped.

    `leading=True` is for a query that already opens its own WITH, so this only contributes the
    definition and a comma; `leading=False` supplies the WITH keyword itself.
    """
    if champion_id is None:
        return ""
    if leading:
        return CHAMPION_MATCH_CTE + ",\n"
    return "WITH " + CHAMPION_MATCH_CTE + "\n"
# The structural model was always fit on at most `max_training_rows`; the old code reached that by
# loading every row and discarding >99% of them. Sampling whole matches in the query instead keeps
# the same row budget while bounding the load, and hashing the id makes the draw deterministic, so a
# re-run of the same generation trains on the same matches.
# A half-open id range, not a hash residue.
#
# `mod(abs(hashtextextended(m."Id"::text, 11)), n) = k` cannot use an index, so every slice re-scanned
# the whole cohort join -- eight scans for eight slices. Match ids are UUIDv7, so they sort by insertion
# time, and IX_Matches_AnalyticsEligible is (Patch, Id): a range predicate is an index scan instead.
# Ranges are picked spread across the id space, so the union is still a systematic sample over the
# cohort's whole time span rather than one contiguous block of it.
MATCH_SAMPLE_FILTER = (
    '          AND m."Id" >= %(match_sample_from)s\n'
    '          AND m."Id" < %(match_sample_until)s\n'
)
# The draw is taken in slices so the structural fit's peak is bounded like the champion sweep's is.
# Residues 0..slices-1 of (modulus * slices) select the same fraction of matches as residue 0 of
# modulus, so slicing changes the peak without changing the size of the sample.
TRAINING_SAMPLE_SLICES = 8


def scope_predicates(
    champion_id: int | None,
    match_sample_range: tuple | None,
    *,
    match_scoped: bool,
) -> str:
    """Extra WHERE clauses shared by every cohort loader, so all of them scope identically.

    Champion scoping always restricts to the champion's matches, because that is what makes the plan
    drive off the small `champion_matches` set. A participant-scoped loader additionally keeps only the
    champion's own rows; a match-scoped one must keep all ten participants, since a kill or objective
    diff is a fact about the match.
    """
    clauses = ""
    if champion_id is not None:
        clauses += CHAMPION_MATCH_FILTER
        if not match_scoped:
            clauses += CHAMPION_EVENT_FILTER
    if match_sample_range is not None:
        clauses += MATCH_SAMPLE_FILTER
    return clauses


def load_cohort_match_count(connection: psycopg.Connection, patches: list[str], cutoff) -> int:
    row = connection.execute(
        """
        SELECT count(*) AS matches
        FROM "Matches" m
        WHERE m."Patch" = ANY(%(patches)s)
          AND m."Status" = 1
          AND m."FetchedAt" <= %(cutoff)s
          AND m."QueueId" = 420
          AND m."Duration" >= 300
        """,
        {"patches": patches, "cutoff": cutoff},
    ).fetchone()
    return int(row["matches"]) if row else 0


def load_training_sample_ranges(
    connection: psycopg.Connection,
    patches: list[str],
    cutoff,
    match_count: int,
    target_matches: int,
    slices: int,
) -> list[tuple]:
    """Half-open id ranges whose union is roughly `target_matches` of the cohort.

    The id space is cut into `match_count / target_matches * slices` equal-count blocks and every
    (count/target)'th one is taken, so the chosen blocks are spread across the whole span rather than
    bunched at one end. Match ids are UUIDv7, so equal-count id blocks are equal-count time blocks and
    the union keeps the chronological spread the train/calibration/test split depends on.

    Boundaries come from percentile_disc over the cohort's ids -- one sort of a few tens of thousands of
    values, against eight full-cohort scans, which is what the hash residue predicate cost.
    """
    if slices < 1:
        return []
    stride = max(1, match_count // target_matches) if target_matches > 0 else 1
    if stride == 1:
        # The whole cohort is wanted, so one unbounded range beats slicing it.
        return [(None, None)]
    blocks = stride * slices
    # Fractions 0 and 1 give the lowest and highest id, so no separate aggregate is needed -- Postgres
    # has no min(uuid)/max(uuid) anyway.
    fractions = [index / blocks for index in range(blocks + 1)]
    row = connection.execute(
        """
        SELECT percentile_disc(%(fractions)s::double precision[])
                 WITHIN GROUP (ORDER BY m."Id") AS edges
        FROM "Matches" m
        WHERE m."Patch" = ANY(%(patches)s)
          AND m."Status" = 1
          AND m."FetchedAt" <= %(cutoff)s
          AND m."QueueId" = 420
          AND m."Duration" >= 300
        """,
        {"fractions": fractions, "patches": patches, "cutoff": cutoff},
    ).fetchone()
    edges = list(row["edges"]) if row and row["edges"] else []
    if len(edges) < blocks + 1 or edges[0] is None:
        return [(None, None)]
    # `< upper` is exclusive, so the last block's bound has to sit strictly above the largest id or the
    # newest match in the cohort would be silently dropped.
    edges[-1] = _successor_uuid(edges[-1])
    return [(edges[index * stride], edges[index * stride + 1]) for index in range(slices)]


def _successor_uuid(value) -> str:
    """The smallest id strictly greater than `value`, so a half-open range can include it."""
    digits = str(value).replace("-", "")
    return str(UUID(int=int(digits, 16) + 1))


def training_sample_modulus(match_count: int, target_matches: int) -> int:
    """Keep roughly `target_matches` of the cohort. 1 means "take everything"."""
    if match_count <= target_matches or target_matches <= 0:
        return 1
    return max(1, match_count // target_matches)


def load_cohort_champions(connection: psycopg.Connection, patches: list[str], cutoff) -> list[int]:
    """Champions present in the frozen cohort, in a stable order so a resumed run is reproducible."""
    rows = connection.execute(
        """
        SELECT DISTINCT p."ChampionId" AS champion_id
        FROM "MatchParticipants" p
        JOIN "Matches" m ON m."Id" = p."MatchId"
        WHERE m."Patch" = ANY(%(patches)s)
          AND m."Status" = 1
          AND m."FetchedAt" <= %(cutoff)s
          AND m."QueueId" = 420
          AND m."Duration" >= 300
        ORDER BY 1
        """,
        {"patches": patches, "cutoff": cutoff},
    ).fetchall()
    return [int(row["champion_id"]) for row in rows]


def load_item_events(
    connection: psycopg.Connection,
    patches: list[str],
    cutoff,
    rank_offset_column: str | None,
    champion_id: int | None = None,
    match_sample_range: tuple | None = None,
) -> pd.DataFrame:
    rank_join = rank_context_lateral(rank_offset_column) % {
        "match_id_column": 'm."Id"',
        "participant_id_column": 'p."ParticipantId"',
    }
    champion_scope = scope_predicates(champion_id, match_sample_range, match_scoped=False)
    champion_cte = champion_match_cte(champion_id, leading=True)
    return read_sql_frame(
        connection,
        f"""
        WITH {champion_cte}eligible AS (
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
{champion_scope}        )
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
        params={
            "patches": patches,
            "cutoff": cutoff,
            "tiers": list(EMERALD_PLUS),
            "schema_version": TIMELINE_SCHEMA_VERSION,
            "champion_id": champion_id,
            "match_sample_from": match_sample_range[0] if match_sample_range else None,
            "match_sample_until": match_sample_range[1] if match_sample_range else None,
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
            COALESCE("BuildsInto"::text, '') || '|' ||
            "InStore"::text
        )) > 1
        """,
        (patches,),
    ).fetchall()
    return {int(row["ItemId"]) for row in rows}


def load_materially_changed_runes(
    connection: psycopg.Connection,
    patches: list[str],
) -> set[int]:
    """Runes are a balance lever too, and a rune change invalidates borrowed rune-page rows."""
    if len(patches) < 2:
        return set()
    rows = connection.execute(
        """
        SELECT "RuneId"
        FROM "RuneVersions"
        WHERE "PatchVersion" = ANY(%s)
        GROUP BY "RuneId"
        HAVING COUNT(DISTINCT MD5(
            COALESCE("Name", '') || '|' ||
            COALESCE("Description", '') || '|' ||
            "Slot"::text || '|' ||
            "RunePathId"::text
        )) > 1
        """,
        (patches,),
    ).fetchall()
    return {int(row["RuneId"]) for row in rows}


def load_materially_changed_champions(
    connection: psycopg.Connection,
    patches: list[str],
) -> set[int]:
    """
    Champion rebalances are the most common patch lever and invalidate every borrowed row for that
    champion, whatever the item. `BalanceHash` is a hash of a numeric-only projection of Data Dragon
    (base stats plus each spell's cooldown/cost/range/effect), so cosmetic churn does not register.
    """
    if len(patches) < 2:
        return set()
    rows = connection.execute(
        """
        SELECT "ChampionId"
        FROM "ChampionVersions"
        WHERE "PatchVersion" = ANY(%s)
        GROUP BY "ChampionId"
        HAVING COUNT(DISTINCT "BalanceHash") > 1
        """,
        (patches,),
    ).fetchall()
    return {int(row["ChampionId"]) for row in rows}


@dataclass(frozen=True)
class PatchChangeSet:
    """What a patch actually touched, per entity. Empty sets mean "nothing changed"."""

    items: frozenset[int]
    runes: frozenset[int]
    champions: frozenset[int]

    @property
    def is_empty(self) -> bool:
        return not (self.items or self.runes or self.champions)


def load_patch_change_set(connection: psycopg.Connection, patches: list[str]) -> PatchChangeSet:
    return PatchChangeSet(
        items=frozenset(load_materially_changed_items(connection, patches)),
        runes=frozenset(load_materially_changed_runes(connection, patches)),
        champions=frozenset(load_materially_changed_champions(connection, patches)),
    )


def load_champion_archetypes(connection: psycopg.Connection, patch: str) -> dict[int, str]:
    """
    Champion -> archetype, used as a pooling level so a sparse champion borrows strength from
    champions that play like it rather than from every champion at once. Roles are stored sorted, so
    the joined key is stable; a champion with no roles pools at the role level as before.
    """
    rows = connection.execute(
        """
        SELECT "ChampionId", "Roles"
        FROM "ChampionVersions"
        WHERE "PatchVersion" = %s
        """,
        (patch,),
    ).fetchall()
    archetypes: dict[int, str] = {}
    for row in rows:
        roles = [str(role).strip().lower() for role in (row["Roles"] or []) if str(role).strip()]
        archetypes[int(row["ChampionId"])] = "+".join(sorted(roles)) if roles else UNKNOWN_ARCHETYPE
    return archetypes


def timeline_state_events_query(
    patches: list[str],
    cutoff,
    champion_id: int | None = None,
    match_sample_range: tuple | None = None,
) -> tuple[str, dict]:
    """The timeline-event statement and its parameters.

    Shared verbatim by the buffered loader below and the streaming reducer, so the two cannot drift
    into reading different rows -- which is the only way their results could disagree.
    """
    sql = (
        champion_match_cte(champion_id, leading=False)
        + """
        SELECT
            payload."MatchId" AS match_id,
            payload."EventIndex" AS event_index,
            payload."TimestampMs" AS timestamp_ms,
            payload."EventType" AS event_type,
            -- Extracted here rather than in pandas. PayloadJson is jsonb, and parsing it in Python cost
            -- one json.loads plus three dict walks for EVERY event -- around 290,000 per champion, which
            -- made this the dominant cost of the champion sweep. Postgres reads the three scalars it
            -- actually needs straight out of the stored jsonb.
            --
            -- COALESCE covers the camelCase Riot sends and an all-lowercase variant, matching what the
            -- case-insensitive lookup this replaces would have found.
            COALESCE(
                payload."PayloadJson" ->> 'killerId',
                payload."PayloadJson" ->> 'killerid'
            ) AS killer_participant_id,
            COALESCE(
                payload."PayloadJson" ->> 'killerTeamId',
                payload."PayloadJson" ->> 'killerteamid'
            ) AS killer_team_id,
            COALESCE(
                payload."PayloadJson" ->> 'teamId',
                payload."PayloadJson" ->> 'teamid'
            ) AS owner_team_id
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
"""
        + scope_predicates(champion_id, match_sample_range, match_scoped=True)
        + """
        -- Ordered by the primary key (MatchId, EventIndex) rather than by (MatchId, TimestampMs,
        -- EventIndex), which matches no index and therefore sorts ~16M rows on every generation.
        -- The two orders are identical, not merely similar: ingestion assigns EventIndex as the
        -- position AFTER sorting that match's events by Timestamp (MatchTimelineIngestionJob's
        -- StageTimelineEventPayloadsAsync -- .OrderBy(Timestamp).Select((e, index) => ...)), and
        -- LINQ's OrderBy is stable, so equal timestamps keep a deterministic order. That job is the
        -- only writer of this table, so the invariant holds for every row.
        ORDER BY payload."MatchId", payload."EventIndex"
        """
    )
    return sql, {
        "patches": patches,
        "cutoff": cutoff,
        "schema_version": TIMELINE_SCHEMA_VERSION,
        "champion_id": champion_id,
        "match_sample_from": match_sample_range[0] if match_sample_range else None,
        "match_sample_until": match_sample_range[1] if match_sample_range else None,
    }


def load_timeline_state_events(
    connection,
    patches: list[str],
    cutoff,
    champion_id: int | None = None,
    match_sample_range: tuple | None = None,
) -> pd.DataFrame:
    """Buffered load of one scope's events. Used by the structural fit, whose scope is a match sample.

    The champion sweep must not use this: see `stream_cohort_event_state` for why a cohort-wide scope
    cannot be materialised.
    """
    sql, params = timeline_state_events_query(patches, cutoff, champion_id, match_sample_range)
    return read_sql_frame(connection, sql, params)


# Reading the cohort's timeline once instead of once per champion is right: `load_timeline_state_events`
# is match-scoped, because a kill diff is a fact about the match, so ten participants per match meant the
# sweep re-read the same events ten times over -- ~13.6 hours of index scans and jsonb extraction on
# prod's HDD-backed database, which is what projected a 173-champion run to 17 days.
#
# Materialising that single read is NOT right, and the first attempt at this did exactly that. The
# compose unit pins the contract it broke:
#
#     mem_limit 6g -- "peak is set by the largest single champion's rows plus one training slice, not
#     by the size of the cohort, so this no longer has to be raised as the corpus grows"
#
# Measured on the live 16.15 cohort: the buffered read costs ~340 bytes per row in accumulated tuples
# BEFORE any DataFrame exists, so its 7.14M events reach ~2.7GB, `DataFrame.from_records` then builds
# object arrays beside them, and `normalise_uuid_columns` mints 7.14M fresh strings while the UUIDs are
# still referenced. Runs died on SIGKILL 23 minutes in, before the preload logged a single line.
#
# The resolution is that the sweep does not actually need the events. It needs the running per-(match,
# team) kill/tower/objective counts that `merge_cumulative_state` reads as-of each purchase, and those
# are derivable chunk by chunk and storable in a numeric width -- int32 match codes, teams and counters
# -- of ~32 bytes a row against the ~340 the raw form costs. So rows are reduced as they arrive and the
# raw form is never accumulated: one database read, and a resident footprint that stays proportional to
# the cohort's *event state* rather than its event text.
COHORT_EVENT_CHUNK_ROWS = 250_000

EVENT_STATE_COLUMNS = ["match_code", "team_id", "timestamp_ms", "kills", "towers", "objectives"]


@dataclass
class CohortEventState:
    """Cohort-wide pre-decision event state, reduced to a width the per-champion contract can hold.

    `cumulative` carries one row per attributed event with the running counts for its (match, team),
    keyed by an int32 `match_code` rather than the match id -- 4 bytes against the ~85 a distinct
    36-character `str` costs, which is the whole reason this fits.

    `covered_matches` is tracked separately on purpose. `has_event_state` must distinguish a match that
    produced no payload rows at all from one whose payload rows produced no attributable team, and the
    cumulative frame alone cannot tell those apart -- both are simply absent from it.
    """

    cumulative: pd.DataFrame
    match_codes: dict
    covered_matches: frozenset


def write_cohort_event_state(state: CohortEventState, directory: Path) -> Path:
    """Persist the reduced cohort state so every sweep worker can read it instead of deriving it.

    This is what makes the worker pool viable. Deriving this state costs one 16M-row scan of the
    payload table -- 74 minutes on prod -- and each worker used to pay it, so N workers meant N
    concurrent scans of a spinning disk competing with each other. Written once and read back, a
    worker joins the sweep for the price of a local parquet read.

    Three files rather than one pickle: the frame is all-integer columnar data that parquet stores
    densely, and a pickle of a 578 MB frame would have to be re-read in full by every worker with no
    chance for the page cache to share it.
    """
    directory.mkdir(parents=True, exist_ok=True)
    state.cumulative.to_parquet(directory / "cumulative.parquet", index=False)
    pd.DataFrame(
        {
            "match_id": list(state.match_codes.keys()),
            "match_code": [int(code) for code in state.match_codes.values()],
        }
    ).to_parquet(directory / "match_codes.parquet", index=False)
    pd.DataFrame({"match_id": sorted(state.covered_matches)}).to_parquet(
        directory / "covered_matches.parquet", index=False
    )
    return directory


def read_cohort_event_state(directory: Path) -> CohortEventState:
    """Rebuild what `write_cohort_event_state` stored, exactly."""
    codes = pd.read_parquet(directory / "match_codes.parquet")
    covered = pd.read_parquet(directory / "covered_matches.parquet")
    return CohortEventState(
        pd.read_parquet(directory / "cumulative.parquet"),
        {
            str(match_id): int(code)
            for match_id, code in zip(codes["match_id"], codes["match_code"])
        },
        frozenset(str(match_id) for match_id in covered["match_id"]),
    )


def empty_event_state_rows() -> pd.DataFrame:
    """The reduced shape, as `CohortEventState.cumulative` carries it."""
    return pd.DataFrame({column: pd.Series(dtype="int64") for column in EVENT_STATE_COLUMNS})


def empty_scored_rows() -> pd.DataFrame:
    """A scored chunk that credited nothing.

    Carries `event_index` even though the final state does not: chunks are concatenated and sorted on
    it before the cumulative sums run, so an empty chunk missing the column raises rather than
    contributing nothing -- which is what a match whose events attribute to no team produces.
    """
    return pd.DataFrame(
        {column: pd.Series(dtype="int64") for column in [*EVENT_STATE_COLUMNS, "event_index"]}
    )


def score_event_chunk(chunk: pd.DataFrame, teams: pd.DataFrame, codes: dict) -> pd.DataFrame:
    """Reduce one chunk of raw events to narrow per-event team credits.

    Attribution stays in `attribute_events_to_teams` rather than being reimplemented in SQL: the
    killer/declared-team/conceded-building precedence is subtle, it is covered by tests, and a second
    implementation could only ever diverge from it.
    """
    scored = attribute_events_to_teams(chunk, teams)
    if scored.empty:
        return empty_scored_rows()
    for match_id in scored["match_id"].unique().tolist():
        if match_id not in codes:
            codes[match_id] = len(codes)
    return pd.DataFrame(
        {
            "match_code": scored["match_id"].map(codes).astype("int32"),
            "team_id": scored["team_id"].astype("int32"),
            "timestamp_ms": scored["timestamp_ms"].astype("int64"),
            "event_index": scored["event_index"].astype("int32"),
            "kills": scored["kills"].astype("int32"),
            "towers": scored["towers"].astype("int32"),
            "objectives": scored["objectives"].astype("int32"),
        }
    )


def accumulate_event_state(parts: list, codes: dict, covered: frozenset) -> CohortEventState:
    """Run the cumulative sums once over every scored chunk.

    Deliberately deferred to the end rather than done per chunk: the sort is global and a chunk
    boundary can fall inside a match, so summing per chunk would restart a match's counters partway
    through. Sorting the assembled narrow frame reproduces the buffered path's ordering exactly.
    """
    if not parts:
        return CohortEventState(empty_event_state_rows(), dict(codes), covered)
    cumulative = pd.concat(parts, ignore_index=True)
    parts.clear()
    cumulative = cumulative.sort_values(["timestamp_ms", "event_index"], kind="stable")
    for column in ("kills", "towers", "objectives"):
        cumulative[column] = cumulative.groupby(["match_code", "team_id"], sort=False)[column].cumsum()
    return CohortEventState(
        cumulative[EVENT_STATE_COLUMNS].sort_values("timestamp_ms", kind="stable"),
        dict(codes),
        covered,
    )


def event_state_from_events(events: pd.DataFrame, teams: pd.DataFrame) -> CohortEventState:
    """The buffered equivalent of the stream, for scopes small enough to hold (the structural fit)."""
    if events.empty:
        return CohortEventState(empty_event_state_rows(), {}, frozenset())
    codes: dict = {}
    return accumulate_event_state(
        [score_event_chunk(events, teams, codes)], codes, frozenset(events["match_id"])
    )


def stream_cohort_event_state(
    connection,
    patches: list[str],
    cutoff,
    teams: pd.DataFrame,
    *,
    chunk_rows: int = COHORT_EVENT_CHUNK_ROWS,
) -> CohortEventState:
    """Draw the cohort's events once and reduce them as they arrive, never holding the raw set.

    A server-side cursor is the point: `read_sql_frame` calls `fetchall`, which is precisely the
    behaviour that cannot fit. The statement is the same one `load_timeline_state_events` issues, taken
    from the shared builder so the two cannot read different rows.
    """
    sql, params = timeline_state_events_query(patches, cutoff)
    codes: dict = {}
    covered: set = set()
    parts: list[pd.DataFrame] = []
    rows = 0
    with connection.cursor(name="cohort_event_state", row_factory=tuple_row) as cursor:
        cursor.execute(sql, params)
        columns = [column.name for column in cursor.description or []]
        while True:
            batch = cursor.fetchmany(chunk_rows)
            if not batch:
                break
            rows += len(batch)
            chunk = normalise_uuid_columns(pd.DataFrame.from_records(batch, columns=columns))
            del batch
            covered.update(chunk["match_id"].unique().tolist())
            parts.append(score_event_chunk(chunk, teams, codes))
            del chunk
    state = accumulate_event_state(parts, codes, frozenset(covered))
    LOG.info(
        "Cohort event state: %d events over %d matches reduced to %d rows (%.0f MB resident).",
        rows,
        len(covered),
        len(state.cumulative),
        state.cumulative.memory_usage(deep=True).sum() / 1e6,
    )
    return state


def load_participant_teams(
    connection,
    patches: list[str],
    cutoff,
    champion_id: int | None = None,
    match_sample_range: tuple | None = None,
) -> pd.DataFrame:
    return read_sql_frame(
        connection,
        champion_match_cte(champion_id, leading=False)
        + """
        SELECT
            p."MatchId" AS match_id,
            p."ParticipantId" AS participant_id,
            p."TeamId" AS team_id
        FROM "MatchParticipants" p
        JOIN "Matches" m ON m."Id" = p."MatchId"
        WHERE m."Patch" = ANY(%(patches)s)
          AND m."Status" = 1
          AND m."FetchedAt" <= %(cutoff)s
          AND m."QueueId" = 420
          AND m."Duration" >= 300
"""
        + scope_predicates(champion_id, match_sample_range, match_scoped=True)
        + """
        """,
        params={
            "patches": patches,
            "cutoff": cutoff,
            "champion_id": champion_id,
            "match_sample_from": match_sample_range[0] if match_sample_range else None,
            "match_sample_until": match_sample_range[1] if match_sample_range else None,
        },
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
    """Attach only cumulative facts whose event timestamp precedes the purchase.

    The buffered entry point: reduces `events` and applies the result in one step. The champion sweep
    calls `apply_event_state` directly instead, against state streamed once for the whole cohort.
    """
    if decisions.empty or events.empty or participant_teams.empty:
        return apply_event_state(
            decisions, CohortEventState(empty_event_state_rows(), {}, frozenset()), participant_teams
        )
    teams = participant_teams.astype({"participant_id": int, "team_id": int})
    return apply_event_state(decisions, event_state_from_events(events, teams), teams)


def apply_event_state(
    decisions: pd.DataFrame,
    state: CohortEventState,
    participant_teams: pd.DataFrame,
) -> pd.DataFrame:
    """Join one scope's decisions to already-reduced cohort event state."""
    enriched = decisions.copy()
    for column in ("team_kill_diff", "team_tower_diff", "team_objective_diff"):
        enriched[column] = 0.0
    enriched["has_event_state"] = 0.0
    if enriched.empty or participant_teams.empty or not state.covered_matches:
        return enriched
    # Coverage is per match: a match ingested while Build Lab was off carries no payload rows, so its
    # zeroed diffs are missing data rather than an even game. Tracked on the state rather than derived
    # from `cumulative`, which cannot distinguish "no payload rows" from "no attributable team".
    enriched["has_event_state"] = enriched["match_id"].isin(state.covered_matches).astype(float)
    if state.cumulative.empty:
        return enriched

    teams = participant_teams.astype({"participant_id": int, "team_id": int})
    positioned = (
        enriched.reset_index(names="_row")[["_row", "match_id", "participant_id", "timestamp_ms"]]
        .astype({"participant_id": int})
        .merge(teams, on=["match_id", "participant_id"], how="left")
        .dropna(subset=["team_id"])
    )
    if positioned.empty:
        return enriched
    # Onto the state's own key space. A decision whose match contributed no attributed event has no
    # code, and drops out here rather than merging against an unrelated match's counters.
    positioned["match_code"] = positioned["match_id"].map(state.match_codes)
    positioned = positioned.dropna(subset=["match_code"])
    if positioned.empty:
        return enriched
    positioned["match_code"] = positioned["match_code"].astype("int32")
    positioned["team_id"] = positioned["team_id"].astype("int32")
    positioned["opponent_team_id"] = np.where(
        positioned["team_id"] == 100, 200, 100
    ).astype("int32")
    # merge_asof compares the `on` column directly, so both sides must carry the same width.
    positioned["timestamp_ms"] = positioned["timestamp_ms"].astype("int64")
    positioned = positioned.sort_values("timestamp_ms", kind="stable")

    own = merge_cumulative_state(positioned, state.cumulative, "team_id")
    opponent = merge_cumulative_state(positioned, state.cumulative, "opponent_team_id")
    rows = positioned["_row"].to_numpy()
    enriched.loc[rows, "team_kill_diff"] = own["kills"].to_numpy() - opponent["kills"].to_numpy()
    enriched.loc[rows, "team_tower_diff"] = own["towers"].to_numpy() - opponent["towers"].to_numpy()
    enriched.loc[rows, "team_objective_diff"] = (
        own["objectives"].to_numpy() - opponent["objectives"].to_numpy()
    )
    return enriched


def attribute_events_to_teams(events: pd.DataFrame, teams: pd.DataFrame) -> pd.DataFrame:
    """Credit each kill, tower and objective to a team.

    The three payload scalars arrive already extracted by `load_timeline_state_events`, so nothing here
    parses json. That is the whole point: the previous shape ran `json.loads` and three dict walks per
    event, and the sweep touches hundreds of thousands of events per champion.
    """
    scored = events[["match_id", "event_index", "timestamp_ms", "event_type"]].copy()
    killers = pd.to_numeric(events["killer_participant_id"], errors="coerce")
    # positive_int's contract: zero and negatives are absent, not real ids.
    scored["participant_id"] = pd.array(
        killers.where(killers > 0).to_numpy(), dtype="Int64"
    )
    resolved = scored.merge(
        teams.astype({"participant_id": "Int64"}),
        on=["match_id", "participant_id"],
        how="left",
    )
    declared = pd.to_numeric(events["killer_team_id"], errors="coerce")
    declared = declared.where(declared > 0).set_axis(resolved.index)
    resolved["team_id"] = resolved["team_id"].fillna(declared)
    # A building destroyed by minions carries killerId 0 and only the OWNING team's id, so the credit
    # belongs to the other team. Dropping those rows would understate the tower diff systematically.
    owning = pd.to_numeric(events["owner_team_id"], errors="coerce")
    owning = owning.where(owning > 0).set_axis(resolved.index)
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
    left = positioned[["match_code", team_column, "timestamp_ms"]].rename(
        columns={team_column: "team_id"}
    )
    # `by` carries the match, so counters from a match the decision does not belong to can never be
    # picked up -- which is what makes cohort-wide state safe to share across the whole sweep.
    merged = pd.merge_asof(
        left,
        cumulative,
        on="timestamp_ms",
        by=["match_code", "team_id"],
        allow_exact_matches=False,
    )
    return merged[["kills", "towers", "objectives"]].fillna(0.0)


def load_rune_decisions(
    connection,
    patches: list[str],
    cutoff,
    rank_offset_column: str | None,
    champion_id: int | None = None,
    match_sample_range: tuple | None = None,
) -> pd.DataFrame:
    rank_join = rank_context_lateral(rank_offset_column) % {
        "match_id_column": 'm."Id"',
        "participant_id_column": 'p."ParticipantId"',
    }
    champion_scope = scope_predicates(champion_id, match_sample_range, match_scoped=False)
    champion_cte = champion_match_cte(champion_id, leading=False)
    return read_sql_frame(
        connection,
        f"""
        {champion_cte}SELECT
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
{champion_scope}        ORDER BY m."MatchDate", m."Id", p."ParticipantId", rune."SelectionTree", rune."SelectionIndex"
        """,
        params={
            "patches": patches,
            "cutoff": cutoff,
            "tiers": list(EMERALD_PLUS),
            "champion_id": champion_id,
            "match_sample_from": match_sample_range[0] if match_sample_range else None,
            "match_sample_until": match_sample_range[1] if match_sample_range else None,
            "schema_version": TIMELINE_SCHEMA_VERSION,
        },
    )


def load_spell_decisions(
    connection,
    patches: list[str],
    cutoff,
    rank_offset_column: str | None,
    champion_id: int | None = None,
    match_sample_range: tuple | None = None,
) -> pd.DataFrame:
    rank_join = rank_context_lateral(rank_offset_column) % {
        "match_id_column": 'm."Id"',
        "participant_id_column": 'p."ParticipantId"',
    }
    champion_scope = scope_predicates(champion_id, match_sample_range, match_scoped=False)
    champion_cte = champion_match_cte(champion_id, leading=False)
    return read_sql_frame(
        connection,
        f"""
        {champion_cte}SELECT
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
{champion_scope}        """,
        params={
            "patches": patches,
            "cutoff": cutoff,
            "tiers": list(EMERALD_PLUS),
            "champion_id": champion_id,
            "match_sample_from": match_sample_range[0] if match_sample_range else None,
            "match_sample_until": match_sample_range[1] if match_sample_range else None,
            "schema_version": TIMELINE_SCHEMA_VERSION,
        },
    )


ITEM_REPLAY_COLUMNS = (
    "event_type",
    "event_index",
    "timestamp_ms",
    "action_id",
    "before_id",
    "after_id",
    "build_category",
)


def replay_columns(rows: pd.DataFrame) -> dict[str, np.ndarray]:
    """The columns the purchase replay reads, as numpy arrays.

    A missing column becomes an all-None array so the replay reads it as absent, matching what
    `dict.get` returned when this walked pandas rows.
    """
    return {
        name: (
            rows[name].to_numpy()
            if name in rows.columns
            else np.full(len(rows), None, dtype=object)
        )
        for name in ITEM_REPLAY_COLUMNS
    }


def undone_purchase_indexes(participant: pd.DataFrame) -> set[int]:
    """Identify purchases reversed by ITEM_UNDO while replaying the exact lifecycle."""
    columns = replay_columns(participant)
    return undone_from_arrays(columns, np.arange(len(participant)))


def undone_from_arrays(columns: dict[str, np.ndarray], positions: np.ndarray) -> set[int]:
    """Same lifecycle replay, over positions into pre-extracted arrays."""
    event_types = columns["event_type"]
    event_indexes = columns["event_index"]
    action_ids = columns["action_id"]
    before_ids = columns["before_id"]
    after_ids = columns["after_id"]
    active: list[tuple[int, int | None]] = []
    undone: set[int] = set()
    for position in positions:
        event_type = int(event_types[position])
        item_id = positive_int(action_ids[position])
        before_id = positive_int(before_ids[position])
        after_id = positive_int(after_ids[position])
        if event_type == 0 and item_id:
            active.append((item_id, int(event_indexes[position])))
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


def build_item_decisions(rows: pd.DataFrame) -> pd.DataFrame:
    """Collapse ordered purchase events into the decisions an estimate is measured on.

    The replay is sequential by nature -- inventory state at a purchase depends on every event before
    it -- so this is a loop, not a vectorised expression. What it avoids is pandas' per-row cost: the
    old shape called `groupby`, `sort_values` and `iterrows` per participant and `Series.to_dict` per
    emitted row, which built a pandas object for every one of hundreds of thousands of rows and made
    this the single slowest stage of a run.

    Two passes instead. The first walks numpy arrays and records only *which* row emits *what*; the
    second converts just those rows to dicts in one call. Emitted rows are roughly a third of the
    input, so the dictionaries built drop by the same factor and none are built for rows that only
    move inventory state.
    """
    if rows.empty:
        return rows

    columns = replay_columns(rows)
    event_types = columns["event_type"]
    event_indexes = columns["event_index"]
    action_ids = columns["action_id"]
    before_ids = columns["before_id"]
    after_ids = columns["after_id"]
    build_categories = columns["build_category"]

    # One sort for everything: participants grouped in first-appearance order (factorize codes are
    # assigned by appearance), each group internally ordered by (timestamp_ms, event_index) exactly as
    # the per-group sort_values did. lexsort's last key is the primary one.
    group_codes = pd.factorize(
        pd.MultiIndex.from_frame(rows[["match_id", "participant_id"]]), sort=False
    )[0]
    order = np.lexsort((event_indexes, columns["timestamp_ms"], group_codes))
    ordered_codes = group_codes[order]
    boundaries = np.flatnonzero(np.r_[True, ordered_codes[1:] != ordered_codes[:-1]])
    group_slices = np.r_[boundaries, ordered_codes.size]

    # (row position, family, stage, prefix, action ids, inventory string) per emitted decision.
    emitted: list[tuple[int, str, int, list[int], list[int], str]] = []
    for index in range(boundaries.size):
        positions = order[group_slices[index]:group_slices[index + 1]]
        undone = undone_from_arrays(columns, positions)

        starter_positions = [
            position
            for position in positions
            if int(event_types[position]) == 0
            and positive_or_zero_int(build_categories[position]) == 2
            and int(event_indexes[position]) not in undone
        ]
        starter_ids = [
            value
            for value in (positive_int(action_ids[position]) for position in starter_positions)
            if value is not None
        ]
        selected: list[int] = sorted(starter_ids)
        if starter_ids:
            emitted.append((starter_positions[0], "STARTER", 0, [], sorted(starter_ids), ""))

        legendary_stage = 0
        boots_stage = 0
        inventory: list[int] = []
        for position in positions:
            event_type = int(event_types[position])
            action_id = positive_int(action_ids[position])
            before_id = positive_int(before_ids[position])
            after_id = positive_int(after_ids[position])
            if event_type == 1 or event_type == 3:
                if action_id:
                    remove_last(inventory, action_id)
                continue
            if event_type == 2:
                # An undo of a sale restores the sold item. Ingestion classifies the undo row from
                # its after/before id, so a restored consumable stays out of the inventory state.
                restored_category = positive_or_zero_int(build_categories[position])
                if after_id and restored_category in BUILD_ITEM_CATEGORIES:
                    inventory.append(after_id)
                continue
            if event_type != 0 or not action_id or int(event_indexes[position]) in undone:
                continue

            build_category = positive_or_zero_int(build_categories[position])
            inventory_ids = "-".join(str(value) for value in sorted(inventory))
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
                    emitted.append(
                        (position, "FIRST_ITEM_PATH", 0, [], [*selected, action_id], inventory_ids)
                    )
            emitted.append((position, family, stage, list(selected), [action_id], inventory_ids))
            selected.append(action_id)

    if not emitted:
        return pd.DataFrame()
    # One bulk conversion for the emitted rows only. A position appearing twice (a first legendary
    # emits both FIRST_ITEM_PATH and ITEM) yields two independent dicts, as it must.
    sources = rows.iloc[[entry[0] for entry in emitted]].to_dict("records")
    output = []
    for (_, family, stage, prefix, action_ids_value, inventory_ids), source in zip(
        emitted, sources, strict=True
    ):
        source["inventory_ids"] = inventory_ids
        output.append(decision_record(source, family, stage, prefix, action_ids_value))
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


# Game-phase boundaries in minutes, not quantiles of whatever this run happened to draw.
#
# Quantile bands moved between generations, so two runs were never judged on the same definition, and
# on this data they produced (1,10], (10,15], (15,22], (22,51] -- boundaries with no meaning in League
# and a starved 2,256-row band caused by the point mass of pregame rows at minute 0. Fixed boundaries
# are stable across runs, and each one is a phase a reader can act on:
#
#   0          pregame: rune pages and summoners, chosen with NO in-game state at all
#   (0, 8]     early laning
#   (8, 14]    late laning -- turret plating falls at 14:00
#   (14, 20]   mid game -- Herald gives way to Baron at 20:00
#   (20, +)    late game
#
# Removing the band count also removes the tunable: a sweep over it produced passes at 8, 16 and 32 and
# failures at 12 and 24, and picking the value that passed would have been choosing a lucky number.
PHASE_BAND_EDGES = (0.5, 8.0, 14.0, 20.0)

# Resamples behind each phase's null distribution. The gate reads a high quantile of it, so this has to
# be large enough for that quantile to be stable; 1,000 is a few seconds against an hour-long run.
CALIBRATION_BOOTSTRAP_RESAMPLES = 1_000
# Fixed, because a gate decision has to be reproducible: the same generation must reach the same verdict
# on a re-run, and a bootstrap seeded from the clock would not.
CALIBRATION_BOOTSTRAP_SEED = 20260805
# Family-wise error across the phase bands. Testing five phases at 5% each would fail a well-calibrated
# model about 23% of the time, so the per-phase quantile is tightened to keep the overall rate at 5%.
CALIBRATION_FAMILY_WISE_ERROR = 0.05

CALIBRATION_BANDS = 5
# Below this an isotonic fit on one band memorises its calibration split instead of calibrating it,
# which would show up as a band that looks perfect here and drifts in production.
MINIMUM_BAND_CALIBRATION_ROWS = 200



def clustered_ece_null(
    predicted: np.ndarray,
    clusters: np.ndarray,
    quantile: float,
    *,
    resamples: int = CALIBRATION_BOOTSTRAP_RESAMPLES,
    seed: int = CALIBRATION_BOOTSTRAP_SEED,
) -> tuple[float, float]:
    """What ECE a PERFECTLY calibrated model scores here anyway, from sampling noise alone.

    Returns (median, quantile) of that null distribution.

    ECE is positively biased, so "is 0.02 bad?" has no answer without knowing what perfect looks like
    at this sample size and dependence structure. The comparison is what makes the gate sample-size
    aware: a fixed constant silently becomes unpassable on thin data and toothless on plentiful data.

    Resampling is by CLUSTER, not by row, and that is the whole point. Rows are not independent
    observations: one team in one match shares a single outcome across all of its participants' rows,
    and those participants also share the team gold, kill and objective features by construction. A
    row-level bootstrap treats ~5,000 rows as 5,000 observations when they are closer to 1,700
    independent units, and understates the floor by 20-50% -- measured on the live cohort.

    Known simplification: the two teams within a match are perfectly anti-correlated (one wins, one
    loses) and this treats their clusters as independent. That slightly overstates variance, so the
    floor is a little generous; it is the conservative direction for a false REJECTION, and the
    practical excess bound below is what guards the other side.
    """
    if predicted.size == 0:
        return 0.0, 0.0
    order = np.argsort(clusters, kind="stable")
    ordered_clusters = clusters[order]
    ordered_predicted = predicted[order]
    starts = np.flatnonzero(np.r_[True, ordered_clusters[1:] != ordered_clusters[:-1]])
    lengths = np.diff(np.r_[starts, ordered_clusters.size])
    rng = np.random.default_rng(seed)
    scores = np.empty(resamples, dtype=float)
    for index in range(resamples):
        picked = rng.integers(0, starts.size, starts.size)
        rows = np.concatenate(
            [np.arange(starts[cluster], starts[cluster] + lengths[cluster]) for cluster in picked]
        )
        probabilities = ordered_predicted[rows]
        # One outcome draw per resampled cluster, shared by every row in it. This is what injects the
        # dependence: a team either won or lost, and all its rows agree.
        shared = np.repeat(rng.random(starts.size), lengths[picked])
        scores[index] = expected_calibration_error(
            (probabilities > shared).astype(int), probabilities
        )
    return float(np.median(scores)), float(np.quantile(scores, quantile))


def phase_band_label(band: int) -> str:
    """Human-readable name for a phase band index, for the manifest and the failure reason."""
    labels = ("pregame", "early-laning", "late-laning", "mid-game", "late-game")
    return labels[band] if 0 <= band < len(labels) else f"band-{band}"


def calibration_band_edges(minutes: np.ndarray, bands: int | None = None) -> np.ndarray:
    """The fixed game-phase edges, always all of them.

    Deliberately not filtered to the observed range. Dropping an edge renumbers every band above it, so
    a cohort of only mid-game rows would report them under the `pregame` label. An edge that splits
    nothing simply yields a band with no rows, and empty bands are skipped where bands are consumed --
    which costs nothing and keeps band index and phase meaning the same thing in every cohort.
    """
    return np.asarray(PHASE_BAND_EDGES, dtype=float)


def fit_banded_calibrator(
    minutes: np.ndarray,
    raw: np.ndarray,
    actual: np.ndarray,
    bands: int = CALIBRATION_BANDS,
) -> dict:
    """Isotonic calibration per game-phase band, with a global calibrator as the fallback.

    The promotion gate measures ECE *within* time bands, so calibrating globally is measuring one
    thing and fitting another: a single monotone map cannot correct a bias that changes sign between
    the early and late game, and the worst band carries the gate. Measured on the live 16.15 cohort, a
    global fit gave an overall ECE of 0.0078 (well inside its 0.015 limit) while the worst time band
    sat at 0.0529 against a 0.025 limit, and it barely improved when the training draw was quadrupled
    -- a worst-of-N statistic does not shrink with sample size the way a mean does. Fitting each band
    separately targets the criterion the gate actually applies.

    Bands are quantiles of the *calibration* split, so the edges are fixed at fit time and travel with
    the model rather than being re-derived from whatever is being scored.
    """
    fallback = IsotonicRegression(out_of_bounds="clip")
    fallback.fit(raw, actual)
    edges = calibration_band_edges(minutes, bands)
    assignment = np.digitize(minutes, edges)
    bands: dict[int, IsotonicRegression] = {}
    for band in np.unique(assignment):
        mask = assignment == band
        # A band with one outcome has nothing to calibrate against and would collapse to a constant.
        if int(mask.sum()) < MINIMUM_BAND_CALIBRATION_ROWS or np.unique(actual[mask]).size < 2:
            continue
        band_calibrator = IsotonicRegression(out_of_bounds="clip")
        band_calibrator.fit(raw[mask], actual[mask])
        bands[int(band)] = band_calibrator
    return {"edges": edges, "bands": bands, "fallback": fallback}


def apply_banded_calibrator(calibrator: dict, minutes: np.ndarray, raw: np.ndarray) -> np.ndarray:
    """Route each row to the calibrator for its band, falling back to the global one."""
    values = np.asarray(calibrator["fallback"].predict(raw), dtype=float)
    edges = calibrator["edges"]
    if len(edges) == 0 or not calibrator["bands"]:
        return values
    assignment = np.digitize(minutes, edges)
    for band, band_calibrator in calibrator["bands"].items():
        mask = assignment == band
        if mask.any():
            values[mask] = np.asarray(band_calibrator.predict(raw[mask]), dtype=float)
    return values


def train_structural_model(
    decisions: pd.DataFrame,
    max_training_rows: int = 250_000,
    calibration_bands: int = CALIBRATION_BANDS,
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
    calibrator = fit_banded_calibrator(
        calibration["minute"].to_numpy(dtype=float),
        calibration_raw,
        calibration["won"].astype(int).to_numpy(),
        calibration_bands,
    )
    test_raw = model.predict_proba(test_x)[:, 1]
    predicted = apply_banded_calibrator(
        calibrator, test["minute"].to_numpy(dtype=float), test_raw
    )
    actual = test["won"].astype(int).to_numpy()
    baseline_probability = float(train["won"].mean())
    baseline = np.full_like(predicted, baseline_probability)
    overall_ece = expected_calibration_error(actual, predicted)
    # Evaluated on the SAME fixed phase edges the calibrator was fit with. Quantiles of the test set
    # moved the goalposts between runs and starved one band; a phase is a phase.
    test_minutes = test["minute"].to_numpy(dtype=float)
    band_assignment = np.digitize(test_minutes, np.asarray(PHASE_BAND_EDGES, dtype=float))
    present_bands = np.unique(band_assignment)
    # Bonferroni across the phases actually present, so the family-wise false-rejection rate is the
    # configured one rather than that rate times the number of phases.
    band_quantile = 1.0 - (CALIBRATION_FAMILY_WISE_ERROR / max(1, len(present_bands)))
    # A team in a match is the independent unit: all of its rows share one outcome.
    clusters = (
        test["match_id"].astype(str) + ":" + test["team_id"].astype(str)
    ).to_numpy()
    band_eces = []
    band_excesses = []
    exceeds_noise_floor = False
    band_detail = {}
    for band in present_bands:
        mask = band_assignment == band
        band_ece = expected_calibration_error(actual[mask], predicted[mask])
        floor_median, floor_threshold = clustered_ece_null(
            predicted[mask], clusters[mask], band_quantile
        )
        excess = max(0.0, band_ece - floor_median)
        within = band_ece <= floor_threshold
        exceeds_noise_floor = exceeds_noise_floor or not within
        band_eces.append(band_ece)
        band_excesses.append(excess)
        band_detail[phase_band_label(int(band))] = {
            "rows": int(mask.sum()),
            "independentUnits": int(np.unique(clusters[mask]).size),
            "ece": band_ece,
            "eceBins": ece_bin_count(int(mask.sum())),
            "noiseFloorMedian": floor_median,
            "noiseFloorThreshold": floor_threshold,
            "eceExcess": excess,
            "withinNoiseFloor": bool(within),
        }
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
        # A cohort with a single patch cannot be split across a patch boundary, so there is nothing to
        # pass or fail. Reported separately from the result so the promoter can tell "not testable"
        # apart from "tested and failed" instead of reading False as a verdict.
        "heldOutPatchApplicable": bool(held_out_patch),
        "leakageCheckPassed": leakage["passed"],
        "leakageDetail": leakage,
        "heldOutPatch": held_out_patch,
        "trainMatchCount": len(set(train_matches)),
        "calibrationMatchCount": len(set(calibration_matches)),
        "testMatchCount": len(set(test_matches)),
        "designColumnCount": len(design.columns),
        "calibrationBandCount": len(calibrator["bands"]),
        # Per-phase detail, so a rejection says WHICH phase is miscalibrated and on how many rows
        # rather than only that some anonymous band failed.
        "timeBandDetail": band_detail,
        # True when some phase is worse than a perfectly calibrated model would look here. This is the
        # statistical half of the calibration gate: it asks whether the deviation is DETECTABLE.
        "calibrationExceedsNoiseFloor": exceeds_noise_floor,
        # And this is the practical half: how far the worst phase sits above its own noise floor, so a
        # deviation that is detectable but tiny does not block a publish, and one that is large does
        # even if the sample is too thin to call it significant.
        "maxTimeBandEceExcess": max(band_excesses, default=0.0),
        "calibrationBandQuantile": band_quantile,
        "calibrationBandEdges": [float(edge) for edge in calibrator["edges"]],
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
        # Routed by decision minute through the same band edges the calibrator was fit with, or every
        # published number would be read off a different map than the one the gate measured.
        values[start:start + len(chunk)] = apply_banded_calibrator(
            bundle["calibrator"], chunk["minute"].to_numpy(dtype=float), raw
        )
    return np.clip(values, 1e-4, 1 - 1e-4)


ACTION_POOLING_LEVELS = [
    ["family", "stage", "action_key"],
    ["family", "stage", "action_key", "role"],
    # Archetype sits between role and champion: an item's effect on a burst mage says far more about
    # the same item on another burst mage than the role average does, so a sparse champion shrinks
    # toward champions that play like it. `between_group_variance` is method-of-moments, so if
    # archetype explains no variance this level contributes almost nothing rather than flattening
    # genuine champion-specific effects.
    ["family", "stage", "action_key", "role", "archetype"],
]

PATH_POOLING_LEVELS = [["path_hash"], ["path_hash", "role"]]


def build_action_estimates(
    decisions: pd.DataFrame,
    generation_id: UUID,
    patch: str,
) -> list[tuple]:
    """Whole-cohort convenience wrapper. `model_generation` drives the two halves separately so that
    only one champion's rows are ever resident; pooling is global either way."""
    records = action_records(decisions)
    apply_partial_pooling(records, ACTION_POOLING_LEVELS)
    return action_tuples(records, generation_id, patch)


def action_records(decisions: pd.DataFrame) -> list[dict]:
    """Per-cell doubly-robust records. Every grouping key starts with champion_id, so running this
    per champion and concatenating yields the same records as running it over the whole cohort."""
    if decisions.empty:
        return []
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
                    "archetype": str(group["archetype"].iloc[0])
                    if "archetype" in group.columns
                    else UNKNOWN_ARCHETYPE,
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
    return records


def action_tuples(records: list[dict], generation_id: UUID, patch: str) -> list[tuple]:
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
            record["evidence_bucket"],
            record["bucket_confidence"],
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
    """Whole-cohort convenience wrapper; see `build_action_estimates`."""
    records = path_records(decisions)
    apply_partial_pooling(records, PATH_POOLING_LEVELS)
    return path_tuples(records, generation_id, patch)


def path_records(decisions: pd.DataFrame) -> list[dict]:
    if decisions.empty:
        return []
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
    return records


def path_tuples(records: list[dict], generation_id: UUID, patch: str) -> list[tuple]:
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


def expand_scopes(
    frame: pd.DataFrame,
    fine_scope_min_rows: int = FINE_SCOPE_MIN_ROWS,
) -> pd.DataFrame:
    """Quadruple the frame into the four scope granularities the serving layer can answer at.

    `fine_scope_min_rows` drops opponent- and region-scoped rows whose whole scope is too small to
    reach the publication floor (`MinimumObservedActions`, 1000). This is a pure cost cut, not a
    change of published values: a cell is always a subset of its scope, so a scope carrying fewer
    rows than the floor cannot contain a cell that clears it. Measured on one role of one patch, 90%
    of the cells that survive the 40-row attempt floor sit in these three variants and none of them
    can publish -- they were fitted (a 1,044-column design, 5 folds x 3 nuisance models each) and
    then discarded by the promoter.

    What it does give up is the direction-only Bucketed tier at matchup and region granularity,
    which needs BucketConfidence rather than 1000 observations. Those cells now fall back to
    GLOBAL_FALLBACK, which the serving layer already resolves. The global (champion x role) scope
    keeps the original 40-row floor and is untouched.
    """
    opponents = frame["opponent_champion_id"].fillna(0).astype(int)
    regions = frame["region"].fillna("GLOBAL").astype(str).str.upper()
    scope_keys = ["champion_id", "role", "scope_opponent_id", "scope_region"]
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
        is_global = isinstance(opponent, int) and region is None
        if not is_global and fine_scope_min_rows > 0 and not scope.empty:
            # champion_id and role are always present on the production frames; narrowing to the
            # columns actually there keeps this usable on the smaller frames the tests build.
            keys = [key for key in scope_keys if key in scope.columns]
            scope = scope.assign(_scope_rows=1)
            sizes = scope.groupby(keys, sort=False, dropna=False)["_scope_rows"].transform("size")
            scope = scope[sizes >= fine_scope_min_rows].drop(columns="_scope_rows")
        if not scope.empty:
            scopes.append(scope)
    if not scopes:
        return frame.iloc[0:0].assign(scope_opponent_id=0, scope_region="GLOBAL")
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
    parent_of = [0] * len(records)
    for keys in levels:
        groups: dict[tuple, list[int]] = {}
        for position, record in enumerate(records):
            groups.setdefault(tuple(record[key] for key in keys), []).append(position)
        next_mean = prior_mean.copy()
        next_variance = prior_variance.copy()
        group_of = list(parent_of)
        for group_id, members in enumerate(groups.values()):
            # A level that reproduces its parent's membership refines nothing, so applying the
            # update would shrink the same records toward their own mean a second time. Champions
            # with no published archetype hit this, and must land exactly where role-level pooling
            # left them rather than being quietly pulled tighter.
            if len({parent_of[position] for position in members}) == 1 and len(members) == sum(
                1 for position in range(len(records)) if parent_of[position] == parent_of[members[0]]
            ):
                for position in members:
                    group_of[position] = group_id
                continue
            for position in members:
                group_of[position] = group_id
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
        parent_of = group_of
    posterior_variance = 1.0 / (1.0 / variance + 1.0 / prior_variance)
    posterior_mean = (raw / variance + prior_mean / prior_variance) * posterior_variance
    posterior_error = np.sqrt(posterior_variance)
    bucket, bucket_confidence = posterior_buckets(posterior_mean, posterior_error)
    for position, record in enumerate(records):
        record["estimate"] = float(posterior_mean[position])
        record["posterior_standard_error"] = float(posterior_error[position])
        record["evidence_bucket"] = bucket[position]
        record["bucket_confidence"] = float(bucket_confidence[position])
        record["low"] = float(posterior_mean[position] - 1.96 * posterior_error[position])
        record["high"] = float(posterior_mean[position] + 1.96 * posterior_error[position])


def posterior_buckets(
    mean: np.ndarray,
    error: np.ndarray,
) -> tuple[list[str], np.ndarray]:
    """
    Favoured bucket per estimate and the posterior mass behind it.

    A cell needs far more evidence to pin a <=3pp interval than to say which side of "typical" it
    falls on, so the serving layer publishes a direction when it cannot publish a number. The mass is
    computed here, where the posterior lives, so promotion can grade it with a plain column
    comparison instead of materialising every row to evaluate a normal tail.
    """
    safe_error = np.maximum(error, 1e-9)
    # P(lift > +threshold) and P(lift < -threshold) under the posterior.
    above = 0.5 * erfc((BUCKET_THRESHOLD - mean) / (safe_error * math.sqrt(2)))
    below = 0.5 * erfc((BUCKET_THRESHOLD + mean) / (safe_error * math.sqrt(2)))
    typical = np.clip(1.0 - above - below, 0.0, 1.0)
    stacked = np.vstack([below, typical, above])
    labels = np.array(["BELOW_AVERAGE", "TYPICAL", "ABOVE_AVERAGE"])
    winner = stacked.argmax(axis=0)
    return list(labels[winner]), stacked.max(axis=0)


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


# Rows per probability bin below which a bin's observed rate is mostly sampling noise. Measured on
# the live cohort: at 10 fixed bins a PERFECTLY calibrated model scores a median ECE of 0.0217 and a
# 95th percentile of 0.0316 on the gate's thinnest time band (n=2,256) -- above the 0.025 limit the
# promoter applies, so the gate was rejecting noise. At ~500 rows per bin the same band's noise floor
# falls to 0.0140 / 0.0233.
MINIMUM_ECE_BIN_ROWS = 500
MAXIMUM_ECE_BINS = 10


def ece_bin_count(sample_size: int) -> int:
    """Bins that keep enough rows each for a bin mean to estimate anything."""
    return max(2, min(MAXIMUM_ECE_BINS, sample_size // MINIMUM_ECE_BIN_ROWS))


def expected_calibration_error(
    actual: np.ndarray,
    predicted: np.ndarray,
    bins: int | None = None,
) -> float:
    """Binned calibration error, with the bin count scaled to the sample by default.

    ECE sums |observed - predicted| per bin, so it is positively biased: sampling noise inflates every
    term and never cancels. With a fixed bin count that bias grows as the sample shrinks, which makes a
    *max over bands* mostly a report on whichever band is thinnest rather than on calibration.

    Coarsening the bins trades away only the ability to see miscalibration that oscillates *within* a
    bin. Systematic bias -- the thing the gate exists to catch -- survives any binning, because a shift
    does not average out inside a bin the way noise does.
    """
    if len(actual) == 0:
        return 1.0
    bins = ece_bin_count(len(actual)) if bins is None else bins
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
    # psycopg exposes `executemany` on the cursor only -- `Connection` carries `execute` as a
    # convenience but never this. Going through an explicit cursor is the whole API, not a style
    # choice: the connection-level call raises AttributeError, and because it is the terminal write
    # it does so only after the full 173-champion sweep has already been paid for.
    with connection.cursor() as cursor:
        cursor.executemany(
            """
            INSERT INTO "AdjustedActionEstimates" (
                "Id", "GenerationId", "ChampionId", "Role", "OpponentChampionId", "Patch",
                "RegionScope", "DecisionFamily", "Stage", "PathPrefixHash", "PathPrefixJson",
                "ActionKey", "ActionIdsJson", "AdjustedWpa", "ConfidenceLow", "ConfidenceHigh",
                "RawWinRate", "PickRate", "ObservedCount", "EffectiveSampleSize",
                "AverageTimingMinutes", "PropensityOverlap", "CovariateBalance",
                "StableAcrossFolds", "IsPublishable", "EvidenceQuality", "FallbackScope",
                "BaselineDefinition", "EvidenceBucket", "BucketConfidence",
                "UnavailableReason", "ComputedAtUtc"
            ) VALUES (
                %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s::jsonb, %s, %s::jsonb,
                %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, NOW()
            )
            ON CONFLICT DO NOTHING
            """,
            rows,
        )


def insert_path_estimates(connection, rows: list[tuple]) -> None:
    if not rows:
        return
    with connection.cursor() as cursor:
        cursor.executemany(
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
            "LeaseOwner" = NULL
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
