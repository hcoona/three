"""Command-line interface for W6 Slice A packaging, smoke checks and Copista."""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import TYPE_CHECKING

from sheet_music_omr.evaluation.alignment import (
    atomic_write_text,
    check_no_symlink_under,
    venv_python_path,
)
from sheet_music_omr.harness.confine import Confinement
from sheet_music_omr.harness.downloads import sha256_file
from sheet_music_omr.harness.musescore import (
    MuseScore,
    default_executable,
    settings_path,
)
from sheet_music_omr.harness.privacy import scrub_paths
from sheet_music_omr.harness.records import (
    check_outside_repository,
    source_state,
)
from sheet_music_omr.harness.stops import StopConditionError
from sheet_music_omr.synthetic.ledgers import ACCEPTED_MUSESCORE_IDENTITIES
from sheet_music_omr.training.copista_per_system import run_copista_per_system
from sheet_music_omr.training.ledgers import (
    _check_w6_disk_cap,
    check_w6_runs_root,
    migrate_w6_campaign_roots,
    scan_public_report,
    setup_w6_slice_a,
    verify_w6_training_assets,
    w6_disk_lock,
)
from sheet_music_omr.training.models import run_w6_neural_cpu_smoke
from sheet_music_omr.training.packaging import (
    run_expanded_w4a_and_package,
    run_musescore_supplement_and_package,
    validate_w6_packaging_minima,
    validate_w6_supplement_minima,
)

if TYPE_CHECKING:
    from collections.abc import Sequence


def _positive_int(raw: str) -> int:
    try:
        value = int(raw)
        if value >= 1:
            return value
    except ValueError:
        pass
    msg = f"expected a positive integer, got {raw!r}"
    raise argparse.ArgumentTypeError(msg)


def _nonnegative_int(raw: str) -> int:
    try:
        value = int(raw)
        if value >= 0:
            return value
    except ValueError:
        pass
    msg = f"expected a non-negative integer, got {raw!r}"
    raise argparse.ArgumentTypeError(msg)


def _add_common_packaging_args(sub: argparse.ArgumentParser) -> None:
    sub.add_argument("--run-dir", type=Path, required=True)
    sub.add_argument("--downloads", type=Path, required=True)
    sub.add_argument("--test-exclusion", type=Path, required=True)
    sub.add_argument("--test-status", type=Path, required=True)
    sub.add_argument("--musescore", type=Path, default=None)
    sub.add_argument("--settings", type=Path, default=None)
    sub.add_argument(
        "--formal",
        action=argparse.BooleanOptionalAction,
        default=True,
    )


def build_parser() -> argparse.ArgumentParser:
    """Construct the W6 Slice A CLI parser."""
    parser = argparse.ArgumentParser(
        prog="python -m sheet_music_omr.training",
        description=(
            "W6 Slice A metered provisioning, CPU smoke checks, bulk crop "
            "packaging, offline per-system Copista baseline and privacy scan."
        ),
    )
    sub = parser.add_subparsers(dest="command", required=True)

    setup = sub.add_parser(
        "setup-w6-slice-a",
        help="Provision locked CPU wheels and non-code HF metadata.",
    )
    setup.add_argument("--downloads", type=Path, required=True)
    setup.add_argument(
        "--init-campaign",
        action="store_true",
        help="Initialize the host anchor.",
    )

    migrate = sub.add_parser(
        "migrate-w6-downloads",
        help="Rebind W6 after preserving its ledgers and retained runs.",
    )
    migrate.add_argument("--from-downloads", type=Path, required=True)
    migrate.add_argument("--to-downloads", type=Path, required=True)

    smoke = sub.add_parser(
        "smoke-w6-stack",
        help="Run offline tiny-config CPU forward/backward smoke checks.",
    )
    smoke.add_argument("--downloads", type=Path, required=True)
    smoke.add_argument("--work-dir", type=Path, required=True)

    pkg = sub.add_parser(
        "package-w6-crops",
        help="Expand permissive W4a intake and package 20k/50k crop datasets.",
    )
    _add_common_packaging_args(pkg)
    pkg.add_argument("--lieder-manifest", type=Path, required=True)
    pkg.add_argument("--min-val-works", type=_positive_int, default=150)
    pkg.add_argument("--planned-pages", type=_positive_int, default=15_000)
    pkg.add_argument("--max-pdmx", type=_nonnegative_int, default=3_000)
    pkg.add_argument("--target-train-crops", type=_positive_int, default=45_000)
    pkg.add_argument("--target-val-crops", type=_positive_int, default=5_000)
    pkg.add_argument("--prefix-train-crops", type=_positive_int, default=18_000)
    pkg.add_argument("--prefix-val-crops", type=_positive_int, default=2_000)
    pkg.add_argument("--verovio-jobs", type=_positive_int, default=1)
    pkg.add_argument(
        "--musescore-page-cap", type=_nonnegative_int, default=None
    )

    supp = sub.add_parser(
        "package-w6-musescore-supplement",
        help="Augment a verified W6 run's 20k/50k milestones via MuseScore.",
    )
    _add_common_packaging_args(supp)
    supp.add_argument("--base-run-dir", type=Path, required=True)
    supp.add_argument("--planned-pages", type=_positive_int, default=1_500)
    supp.add_argument(
        "--supplement-train-crops", type=_positive_int, default=2_700
    )
    supp.add_argument("--supplement-val-crops", type=_positive_int, default=300)
    supp.add_argument(
        "--target-train-crops", type=_positive_int, default=45_000
    )
    supp.add_argument("--target-val-crops", type=_positive_int, default=5_000)
    supp.add_argument(
        "--prefix-train-crops", type=_positive_int, default=18_000
    )
    supp.add_argument("--prefix-val-crops", type=_positive_int, default=2_000)
    supp.add_argument("--jobs", type=_positive_int, default=1)

    copista = sub.add_parser(
        "run-copista-per-system",
        help="Run offline per-system Copista baseline on sealed TRAIN_DEV.",
    )
    copista.add_argument("--run-dir", type=Path, required=True)
    copista.add_argument("--downloads", type=Path, required=True)
    copista.add_argument("--traindev-root", type=Path, required=True)
    copista.add_argument(
        "--formal",
        action=argparse.BooleanOptionalAction,
        default=True,
    )

    scan = sub.add_parser(
        "scan-public-report",
        help="Verify OMR-REQ-015 23-identity privacy scan on report text.",
    )
    scan.add_argument("--report", type=Path, required=True)
    scan.add_argument("--mapping-json", type=Path, required=True)
    return parser


def _prepare_packaging_context(
    args: argparse.Namespace,
) -> tuple[Path, Path, MuseScore]:
    run = args.run_dir.absolute()
    downloads = args.downloads.absolute()
    check_outside_repository(run, downloads)
    check_w6_runs_root(downloads, run.parent)
    if not run.name.startswith("w6-slice-a-"):
        msg = "W6 packaging run directory must start with w6-slice-a-"
        raise StopConditionError(msg)
    check_no_symlink_under(run, Path(run.anchor))
    run, downloads = run.resolve(), downloads.resolve()
    crop_targets = (
        args.prefix_train_crops,
        args.prefix_val_crops,
        args.target_train_crops,
        args.target_val_crops,
    )
    if args.command == "package-w6-crops":
        validate_w6_packaging_minima(
            formal=args.formal,
            max_pdmx=args.max_pdmx,
            min_val_works=args.min_val_works,
            crop_targets=crop_targets,
        )
    else:
        validate_w6_supplement_minima(
            formal=args.formal,
            supplement_train_crops=args.supplement_train_crops,
            supplement_val_crops=args.supplement_val_crops,
            crop_targets=crop_targets,
        )
    if args.formal and source_state()["dirty"]:
        msg = "formal Slice A run requires a clean code tree"
        raise StopConditionError(msg)
    with w6_disk_lock(downloads, run.parent) as runs:
        # Check the initial headroom guarantee before querying host settings or
        # touching MuseScore so low-cap runs fail closed immediately.
        _check_w6_disk_cap(runs, additional_bytes=1_048_576)
        active_settings = settings_path()
        if (
            args.settings is not None
            and args.settings.resolve() != active_settings.resolve()
        ):
            msg = (
                "W6 packaging requires the active host MuseScore settings path"
            )
            raise StopConditionError(msg)
        exe = default_executable() if args.musescore is None else args.musescore
        if not exe.is_file():
            msg = f"MuseScore is not installed at {exe}"
            raise StopConditionError(msg)
        exe_sha = sha256_file(exe)
        matched = [
            ver
            for ver, sha in sorted(ACCEPTED_MUSESCORE_IDENTITIES)
            if sha == exe_sha
        ]
        if not matched:
            msg = "MuseScore identity needs accepted mutable-source recheck"
            raise StopConditionError(msg)
        settings_bytes = (
            active_settings.stat().st_size if active_settings.is_file() else 0
        )
        _check_w6_disk_cap(
            runs,
            additional_bytes=max(1_048_576, settings_bytes + 65_536),
        )
        conf = Confinement.create(run, downloads)
        ms = MuseScore.open(
            conf,
            executable=exe,
            settings=active_settings,
            version_lookup=lambda: matched[0],
        )
    return downloads, run, ms


def _execute_packaging_command(args: argparse.Namespace) -> dict[str, object]:
    downloads, run, ms = _prepare_packaging_context(args)
    if args.command == "package-w6-crops":
        return run_expanded_w4a_and_package(
            downloads,
            run,
            lieder_manifest=args.lieder_manifest.resolve(),
            test_exclusion=args.test_exclusion.resolve(),
            test_status=args.test_status.resolve(),
            musescore=ms,
            planned_pages=args.planned_pages,
            max_pdmx=args.max_pdmx,
            min_val_works=args.min_val_works,
            formal=args.formal,
            target_train_crops=args.target_train_crops,
            target_val_crops=args.target_val_crops,
            prefix_train_crops=args.prefix_train_crops,
            prefix_val_crops=args.prefix_val_crops,
            verovio_workers=args.verovio_jobs,
            musescore_page_cap=args.musescore_page_cap,
        )
    return run_musescore_supplement_and_package(
        downloads,
        run,
        base_run_dir=args.base_run_dir.resolve(),
        test_exclusion=args.test_exclusion.resolve(),
        test_status=args.test_status.resolve(),
        musescore=ms,
        planned_pages=args.planned_pages,
        formal=args.formal,
        supplement_train_crops=args.supplement_train_crops,
        supplement_val_crops=args.supplement_val_crops,
        target_train_crops=args.target_train_crops,
        target_val_crops=args.target_val_crops,
        prefix_train_crops=args.prefix_train_crops,
        prefix_val_crops=args.prefix_val_crops,
        workers=args.jobs,
    )


def _run_command(args: argparse.Namespace) -> dict[str, object]:
    if args.command == "setup-w6-slice-a":
        downloads = args.downloads.absolute()
        check_outside_repository(downloads)
        return setup_w6_slice_a(downloads, init_campaign=args.init_campaign)
    if args.command == "migrate-w6-downloads":
        from_downloads, to_downloads = (
            args.from_downloads.absolute(),
            args.to_downloads.absolute(),
        )
        check_outside_repository(from_downloads, to_downloads)
        return migrate_w6_campaign_roots(from_downloads, to_downloads)
    if args.command == "smoke-w6-stack":
        downloads = args.downloads.absolute()
        work_dir = args.work_dir.absolute()
        check_outside_repository(downloads, work_dir)
        work_root, download_root = work_dir.resolve(), downloads.resolve()
        if work_root.is_relative_to(
            download_root
        ) or download_root.is_relative_to(work_root):
            msg = (
                "smoke work directory and downloads directory must be disjoint"
            )
            raise StopConditionError(msg)
        check_no_symlink_under(work_dir, Path(work_dir.anchor))
        env = verify_w6_training_assets(downloads)
        payload = run_w6_neural_cpu_smoke(
            venv_python_path(downloads / "w6-train-venv"),
            work_dir,
            meta_root=downloads / "w6-model-meta",
        ) | {"environment": env}
        out = work_dir / "smoke-report.json"
        check_no_symlink_under(out, work_dir)
        atomic_write_text(
            out,
            scrub_paths(json.dumps(payload, indent=2, sort_keys=True)) + "\n",
        )
        return payload
    if args.command in {"package-w6-crops", "package-w6-musescore-supplement"}:
        return _execute_packaging_command(args)
    if args.command == "run-copista-per-system":
        return run_copista_per_system(
            args.downloads,
            args.run_dir,
            traindev_root=args.traindev_root,
            formal=args.formal,
        )
    if args.command == "scan-public-report":
        text = args.report.read_text(encoding="utf-8")
        scan_public_report(text, args.mapping_json)
        return {"status": "clean", "bytes_scanned": len(text.encode("utf-8"))}
    msg = f"unknown W6 command: {args.command}"
    raise StopConditionError(msg)


def main(argv: Sequence[str] | None = None) -> int:
    """Execute one W6 Slice A CLI command, returning 2 on StopConditionError."""
    parser = build_parser()
    args = parser.parse_args(argv)
    try:
        result = _run_command(args)
    except StopConditionError as exc:
        err = scrub_paths(str(exc))
        sys.stderr.write(f"stop: {err}\n")
        return 2
    sys.stdout.write(
        scrub_paths(json.dumps(result, indent=2, sort_keys=True)) + "\n"
    )
    return 0
