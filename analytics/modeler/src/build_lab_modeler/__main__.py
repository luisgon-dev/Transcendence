"""Entry point for the Build Lab modeler.

`run` is the production path a scheduler invokes. The other subcommands exist because the production
path is the worst possible way to answer a question about the model: it leases a generation, spends
tens of minutes drawing its cohort, sweeps every champion, and publishes -- so a question as small as
"did that calibration change help?" used to cost a full run and could only be watched by tailing a
container's log.

Every subcommand below is on-demand, needs no pending generation, writes nothing to the database, and
shares the cohort-keyed training cache with `run`. Once a draw is cached, `train` is seconds.
"""

import argparse
import json
import logging
import os
import sys

import psycopg
from psycopg.rows import dict_row

from . import pipeline
from .cache import TrainingCache
from .pipeline import RunOutcome, Settings, run

# The exit code is the only thing a oneshot scheduler sees, so map the outcome onto it rather than
# always exiting 0. `idle` and `completed` are both successful ticks; a generation that failed its
# gates or blew up must surface as a unit failure, visible without reading the database.
EXIT_CODES = {
    RunOutcome.IDLE: 0,
    RunOutcome.COMPLETED: 0,
    RunOutcome.FAILED: 1,
}

# Mirrors BuildLabModelingOptions so a local run can report the same verdict the promoter will reach.
# The .NET options remain authoritative -- these are for seeing the answer without a deploy, and are
# asserted against the committed defaults in the test suite so they cannot drift silently.
GATE_LIMITS = {"maximumOverallEce": 0.015, "maximumTimeBandEce": 0.025}


def configure_logging() -> None:
    logging.basicConfig(
        level=os.getenv("LOG_LEVEL", "INFO").upper(),
        format="%(asctime)s %(levelname)s %(message)s",
        stream=sys.stderr,
        force=True,
    )


def connect(settings: Settings) -> psycopg.Connection:
    return psycopg.connect(settings.database_url, row_factory=dict_row)


def resolve_cohort(connection, arguments) -> tuple[str, list[str], object]:
    """The cohort to work on: an explicit one, or the newest generation's."""
    if arguments.patches:
        patches = [patch.strip() for patch in arguments.patches.split(",") if patch.strip()]
        return patches[0], patches, arguments.cutoff
    row = connection.execute(
        """
        SELECT "Patch", "IncludedPatchesJson", "SourceCutoffUtc"
        FROM "BuildLabGenerations"
        ORDER BY "CreatedAtUtc" DESC
        LIMIT 1
        """
    ).fetchone()
    if row is None:
        raise SystemExit("No generation exists to take a cohort from; pass --patches and --cutoff.")
    return (
        str(row["Patch"]),
        pipeline.json_value(row["IncludedPatchesJson"]),
        arguments.cutoff or row["SourceCutoffUtc"],
    )


def cohort_context(connection, settings: Settings, arguments):
    """Everything the loaders and `prepare` need, resolved once."""
    current_patch, included_patches, cutoff = resolve_cohort(connection, arguments)
    rank_offset = pipeline.resolve_rank_offset_column(connection)
    changes = pipeline.load_patch_change_set(connection, included_patches)
    archetypes = pipeline.load_champion_archetypes(connection, current_patch)

    def prepare(frame, *, exclude_drift: bool = True):
        return pipeline.prepare_decisions(
            frame,
            current_patch,
            included_patches,
            rank_offset,
            changes,
            archetypes,
            exclude_drift=exclude_drift,
        )

    return {
        "current_patch": current_patch,
        "included_patches": included_patches,
        "cutoff": cutoff,
        "rank_offset": rank_offset,
        "changes": changes,
        "archetypes": archetypes,
        "changed_items": set(changes.items),
        "prepare": prepare,
    }


def training_cache(connection, settings: Settings, context, arguments) -> TrainingCache:
    shape = pipeline.training_draw_shape(
        connection, context["included_patches"], context["cutoff"], settings
    )
    cache = TrainingCache.for_cohort(
        settings.artifact_dir / "_cache",
        context["included_patches"],
        *shape,
        enabled=not arguments.no_cache,
        cutoff=context["cutoff"],
        max_age_hours=settings.training_draw_max_age_hours,
    )
    if getattr(arguments, "refresh", False):
        removed = cache.clear()
        logging.getLogger("build_lab_modeler").info(
            "Cleared %d cached slices for cohort %s.", removed, cache.key
        )
    return cache


def draw(connection, settings: Settings, context, cache):
    return pipeline.build_training_frame(
        connection,
        context["included_patches"],
        context["cutoff"],
        context["rank_offset"],
        context["current_patch"],
        context["changed_items"],
        settings,
        context["prepare"],
        cache=cache,
    )


def report_gates(metrics: dict) -> bool:
    """Print the promoter's verdict for each gate and whether the set would promote."""
    applicable = metrics.get("heldOutPatchApplicable")
    checks = [
        ("overallEce", metrics["overallEce"], GATE_LIMITS["maximumOverallEce"],
         metrics["overallEce"] <= GATE_LIMITS["maximumOverallEce"]),
        ("maxTimeBandEce", metrics["maxTimeBandEce"], GATE_LIMITS["maximumTimeBandEce"],
         metrics["maxTimeBandEce"] <= GATE_LIMITS["maximumTimeBandEce"]),
        ("brier < baseline", metrics["brierScore"], metrics["baselineBrierScore"],
         metrics["brierScore"] < metrics["baselineBrierScore"]),
        ("logLoss < baseline", metrics["logLoss"], metrics["baselineLogLoss"],
         metrics["logLoss"] < metrics["baselineLogLoss"]),
        ("patch holdout", metrics["heldOutPatchPassed"],
         "waived (single-patch cohort)" if applicable is False else True,
         bool(metrics["heldOutPatchPassed"]) or applicable is False),
        ("leakage", metrics["leakageCheckPassed"], True, bool(metrics["leakageCheckPassed"])),
    ]
    print("=== promotion gates ===")
    for name, got, limit, ok in checks:
        print(f"{'PASS' if ok else 'FAIL'}  {name}: got={got} limit={limit}")
    passed = all(ok for *_, ok in checks)
    print(f"=== {'WOULD PROMOTE' if passed else 'WOULD BE REJECTED'} ===")
    return passed


def command_dataset(settings: Settings, arguments) -> int:
    with connect(settings) as connection:
        context = cohort_context(connection, settings, arguments)
        cache = training_cache(connection, settings, context, arguments)
        frame = draw(connection, settings, context, cache)
    print(f"cohort={','.join(context['included_patches'])} cutoff={context['cutoff']}")
    print(f"cache={cache.directory}")
    print(f"rows={len(frame)} outcomes={frame['won'].nunique() if len(frame) else 0}")
    return 0 if len(frame) else 1


def command_train(settings: Settings, arguments) -> int:
    with connect(settings) as connection:
        context = cohort_context(connection, settings, arguments)
        cache = training_cache(connection, settings, context, arguments)
        frame = draw(connection, settings, context, cache)
    if frame.empty:
        print("no rows drawn")
        return 1
    bundle, metrics = pipeline.train_structural_model(
        frame, settings.max_training_rows, arguments.bands or settings.calibration_bands
    )
    print(f"calibration bands requested: {arguments.bands or settings.calibration_bands}")
    print(json.dumps({k: v for k, v in metrics.items() if k != "leakageDetail"}, indent=2))
    passed = report_gates(metrics)
    # Scoring must route through the calibrator the gates were measured on, so exercise it here too.
    scored = pipeline.structural_win_probability(bundle, frame.head(min(len(frame), 20_000)))
    print(f"scored {len(scored)} rows: min={scored.min():.4f} mean={scored.mean():.4f} max={scored.max():.4f}")
    return 0 if passed else 2


def command_champion(settings: Settings, arguments) -> int:
    with connect(settings) as connection:
        context = cohort_context(connection, settings, arguments)
        cache = training_cache(connection, settings, context, arguments)
        frame = draw(connection, settings, context, cache)
        if frame.empty:
            print("no rows drawn")
            return 1
        bundle, _ = pipeline.train_structural_model(
            frame, settings.max_training_rows, settings.calibration_bands
        )
        del frame
        champions = (
            [int(value) for value in arguments.champions.split(",")]
            if arguments.champions
            else pipeline.load_cohort_champions(
                connection, context["included_patches"], context["cutoff"]
            )[: arguments.limit]
        )
        actions, paths, rows = [], [], 0
        for champion_id in champions:
            champion = context["prepare"](
                pipeline.load_decision_frame(
                    connection,
                    context["included_patches"],
                    context["cutoff"],
                    context["rank_offset"],
                    context["current_patch"],
                    context["changed_items"],
                    champion_id=champion_id,
                )
            )
            if champion.empty:
                print(f"champion {champion_id}: no eligible rows")
                continue
            champion["baseline_win_probability"] = pipeline.structural_win_probability(
                bundle, champion
            )
            champion_actions = pipeline.action_records(champion)
            champion_paths = pipeline.path_records(champion)
            actions += champion_actions
            paths += champion_paths
            rows += len(champion)
            print(
                f"champion {champion_id}: rows={len(champion)} "
                f"actions={len(champion_actions)} paths={len(champion_paths)} "
                f"event_state={champion['has_event_state'].mean():.3f}"
            )
            del champion
    pipeline.apply_partial_pooling(actions, pipeline.ACTION_POOLING_LEVELS)
    pipeline.apply_partial_pooling(paths, pipeline.PATH_POOLING_LEVELS)
    print(f"total rows={rows} action records={len(actions)} path records={len(paths)}")
    return 0 if actions else 1


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(prog="build_lab_modeler", description=__doc__)
    subcommands = parser.add_subparsers(dest="command")

    def add_cohort_arguments(target, *, cache: bool = True):
        target.add_argument("--patches", help="Comma-separated cohort, newest first. Default: the newest generation's.")
        target.add_argument("--cutoff", help="Source cutoff timestamp. Default: the newest generation's.")
        if cache:
            target.add_argument("--no-cache", action="store_true", help="Ignore and do not write the training cache.")
            target.add_argument("--refresh", action="store_true", help="Discard the cached draw and redraw it.")

    subcommands.add_parser("run", help="Production path: lease a pending generation and model it.")
    add_cohort_arguments(subcommands.add_parser("dataset", help="Build and cache the training draw."))
    train = subcommands.add_parser("train", help="Fit from the draw and report the promotion gates.")
    add_cohort_arguments(train)
    train.add_argument("--bands", type=int, help="Calibration bands to condition on game phase.")
    champion = subcommands.add_parser("champion", help="Produce estimate records for a few champions.")
    add_cohort_arguments(champion)
    champion.add_argument("--champions", help="Comma-separated champion ids. Default: the first --limit in the cohort.")
    champion.add_argument("--limit", type=int, default=3, help="How many champions to sweep when none are named.")
    return parser


def main(argv: list[str] | None = None) -> int:
    configure_logging()
    parser = build_parser()
    arguments = parser.parse_args(argv)
    command = arguments.command or "run"
    if command == "run":
        return EXIT_CODES[run()]
    settings = Settings.from_env()
    settings.artifact_dir.mkdir(parents=True, exist_ok=True)
    handlers = {
        "dataset": command_dataset,
        "train": command_train,
        "champion": command_champion,
    }
    return handlers[command](settings, arguments)


if __name__ == "__main__":
    sys.exit(main())
