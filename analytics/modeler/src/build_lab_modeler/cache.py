"""On-disk cache for the training draw, keyed by the cohort it was drawn from.

A generation's cohort is frozen by `SourceCutoffUtc`, so a draw taken from it can be reused: the same
cohort parameters always select the same matches. That matters twice over.

In production, a run that fails after the draw no longer repays the full cost of the draw on retry --
which on the live cohort is tens of minutes of query time before any modelling starts.

In development it is the difference between iterating on calibration in seconds and in half-hours.
Anything that only touches the model or the estimate stage can reuse a cached draw instead of asking
Postgres for it again.

The key deliberately covers only what changes *which rows are drawn*. It does not cover the borrowing
weights or the drift exclusion, because those are applied after the draw is loaded, so a change to
them must not silently reuse a stale frame -- see `TrainingCache.slice_path` for why the prepared
frame is what gets stored and what that implies.
"""

from __future__ import annotations

import hashlib
import json
import logging
from dataclasses import dataclass
from pathlib import Path

import pandas as pd

LOG = logging.getLogger("build_lab_modeler.cache")

# Bumped when the stored frame's meaning changes -- new columns, a different preparation step, or a
# change to how a slice is thinned. An old cache is then ignored rather than silently reused.
CACHE_SCHEMA_VERSION = 1


def cohort_key(
    patches: list[str],
    cutoff,
    slice_modulus: int,
    slice_rows: int,
) -> str:
    """Short stable digest of everything that decides which rows a slice contains."""
    payload = json.dumps(
        {
            "schema": CACHE_SCHEMA_VERSION,
            "patches": sorted(str(patch) for patch in patches),
            "cutoff": str(cutoff),
            "sliceModulus": int(slice_modulus),
            "sliceRows": int(slice_rows),
        },
        sort_keys=True,
        separators=(",", ":"),
    )
    return hashlib.sha256(payload.encode()).hexdigest()[:16]


@dataclass
class TrainingCache:
    """Per-slice parquet under a cohort-keyed directory.

    Stored per slice rather than as one frame so a partially completed draw is still worth something:
    an interrupted run resumes from the slice it reached instead of starting over.
    """

    directory: Path
    key: str
    enabled: bool = True

    @classmethod
    def for_cohort(
        cls,
        root: Path,
        patches: list[str],
        cutoff,
        slice_modulus: int,
        slice_rows: int,
        *,
        enabled: bool = True,
    ) -> "TrainingCache":
        key = cohort_key(patches, cutoff, slice_modulus, slice_rows)
        return cls(directory=root / "training-draw" / key, key=key, enabled=enabled)

    def slice_path(self, residue: int) -> Path:
        return self.directory / f"slice-{residue:03d}.parquet"

    def read_slice(self, residue: int) -> pd.DataFrame | None:
        if not self.enabled:
            return None
        path = self.slice_path(residue)
        if not path.is_file():
            return None
        try:
            return pd.read_parquet(path)
        except Exception as exc:  # a truncated or unreadable file must not fail the run
            LOG.warning("Ignoring unreadable cached slice %s: %s", path, exc)
            return None

    def write_slice(self, residue: int, frame: pd.DataFrame) -> None:
        if not self.enabled:
            return
        self.directory.mkdir(parents=True, exist_ok=True)
        path = self.slice_path(residue)
        # Written beside the target and moved into place, so a crash mid-write cannot leave a
        # half-parquet that the next run would read as a complete slice.
        staging = path.with_suffix(".parquet.partial")
        try:
            frame.to_parquet(staging, index=False)
            staging.replace(path)
        except Exception as exc:
            # Deliberately loud. A cache that silently never populates looks like a working cache and
            # quietly costs the draw on every run -- which is exactly what an unserialisable uuid
            # column did before `normalise_uuid_columns` existed.
            LOG.error(
                "Could not cache slice %s, so this draw will be repaid on the next run: %s", path, exc
            )
            staging.unlink(missing_ok=True)

    def clear(self) -> int:
        if not self.directory.is_dir():
            return 0
        removed = 0
        for path in sorted(self.directory.glob("slice-*.parquet*")):
            path.unlink(missing_ok=True)
            removed += 1
        return removed
