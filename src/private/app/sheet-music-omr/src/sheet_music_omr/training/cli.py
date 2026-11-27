"""Command-line interface for W6 Slice A packaging, smoke checks and Copista."""

from __future__ import annotations

import argparse
import json
import math
import sys
from pathlib import Path
from typing import TYPE_CHECKING, Any

from sheet_music_omr.evaluation.alignment import (
    atomic_write_text,
    check_no_symlink_under,
    venv_python_path,
)
from sheet_music_omr.harness.confine import Confinement
from sheet_music_omr.harness.musescore import (
    MuseScore,
    active_host_settings_path,
    cli_version,
    default_executable,
)
from sheet_music_omr.harness.privacy import scrub_paths
from sheet_music_omr.harness.records import (
    check_outside_repository,
    source_state,
)
from sheet_music_omr.harness.stops import StopConditionError
from sheet_music_omr.training.copista_per_system import run_copista_per_system
from sheet_music_omr.training.ledgers import (
    _check_w6_disk_cap,
    _is_retired_production_campaign,
    check_w6_runs_root,
    scan_public_report,
    setup_w6_slice_a,
    verify_prior_campaign_ledgers,
    verify_w6_training_assets,
    w6_disk_lock,
)
from sheet_music_omr.training.models import run_w6_neural_cpu_smoke
from sheet_music_omr.training.packaging import (
    _offline_musescore,
    run_expanded_w4a_and_package,
    run_musescore_supplement_and_package,
    validate_w6_packaging_minima,
    validate_w6_supplement_minima,
)
from sheet_music_omr.training.slice_b_ledgers import setup_w6_slice_b
from sheet_music_omr.training.slice_b_runner import (
    run_w6_gpu_train,
    run_w6_pilot_final_test,
    run_w6_traindev_eval,
    run_w6_traindev_replay_and_preregister,
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
        help="Compatibility no-op; ledgers are self-contained in downloads.",
    )

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

    _add_slice_b_subparsers(sub)
    return parser


def _positive_float(raw: str) -> float:
    try:
        value = float(raw)
        if value > 0 and math.isfinite(value):
            return value
    except ValueError:
        pass
    msg = f"expected a finite positive float, got {raw!r}"
    raise argparse.ArgumentTypeError(msg)


def _add_slice_b_subparsers(  # noqa: PLR0915
    sub: Any,  # noqa: ANN401
) -> None:
    sb_setup = sub.add_parser(
        "setup-w6-slice-b",
        help="Verify cross-host ledgers and provision CUDA wheels + weights.",
    )
    sb_setup.add_argument("--downloads", type=Path, required=True)
    sb_setup.add_argument("--package-run-dir", type=Path, default=None)
    sb_setup.add_argument("--traindev-root", type=Path, default=None)
    sb_setup.add_argument("--models", nargs="*", default=None)

    sb_train = sub.add_parser(
        "train-w6-gpu",
        help="Run metered Slice B GPU training on w6-crops-20k or 50k.",
    )
    sb_train.add_argument("--downloads", type=Path, required=True)
    sb_train.add_argument("--run-dir", type=Path, required=True)
    sb_train.add_argument(
        "--arm", choices=("arm-a", "arm-b", "detector"), required=True
    )
    sb_train.add_argument("--model-repo", type=str, required=True)
    sb_train.add_argument("--dataset-dir", type=Path, required=True)
    sb_train.add_argument("--package-run-dir", type=Path, default=None)
    sb_train.add_argument(
        "--fuse-stride8",
        action=argparse.BooleanOptionalAction,
        default=True,
    )
    sb_train.add_argument(
        "--longest-edge", type=int, choices=(1024, 1280), default=1024
    )
    sb_train.add_argument("--include-vision-lora", action="store_true")
    sb_train.add_argument("--lora-rank", type=_positive_int, default=32)
    sb_train.add_argument("--max-steps", type=_positive_int, default=1500)
    sb_train.add_argument("--grad-accum", type=_positive_int, default=8)
    sb_train.add_argument("--lr", type=_positive_float, default=None)
    sb_train.add_argument("--eval-every", type=_positive_int, default=None)
    sb_train.add_argument("--seed", type=int, default=42)
    sb_train.add_argument("--resume-from", type=Path, default=None)

    sb_eval = sub.add_parser(
        "eval-w6-traindev",
        help="Run offline unshare -rn E0 or 4-fold evaluation on TRAIN_DEV.",
    )
    sb_eval.add_argument("--downloads", type=Path, required=True)
    sb_eval.add_argument("--run-dir", type=Path, required=True)
    sb_eval.add_argument("--traindev-root", type=Path, required=True)
    sb_eval.add_argument(
        "--mode",
        choices=("e0", "detector-cv", "synthetic-cv"),
        required=True,
    )
    sb_eval.add_argument("--arm", choices=("arm-a", "arm-b"), default="arm-a")
    sb_eval.add_argument("--model-repo", type=str, default=None)
    sb_eval.add_argument("--recognizer-checkpoint", type=Path, default=None)
    sb_eval.add_argument("--detector-mode", type=str, default="comb_dp_blind")
    sb_eval.add_argument("--detector-checkpoint", type=Path, default=None)
    sb_eval.add_argument("--max-new-tokens", type=_positive_int, default=1536)
    sb_eval.add_argument("--num-beams", type=_positive_int, default=3)

    sb_rep = sub.add_parser(
        "run-w6-replay",
        help="Run 4-fold matched-step TRAIN_DEV replay and pre-register TEST.",
    )
    sb_rep.add_argument("--downloads", type=Path, required=True)
    sb_rep.add_argument("--run-dir", type=Path, required=True)
    sb_rep.add_argument("--traindev-root", type=Path, required=True)
    sb_rep.add_argument("--synth-dataset-dir", type=Path, required=True)
    sb_rep.add_argument("--arm", choices=("arm-a", "arm-b"), required=True)
    sb_rep.add_argument("--model-repo", type=str, default=None)
    sb_rep.add_argument(
        "--selected-recognizer-checkpoint", type=Path, required=True
    )
    sb_rep.add_argument(
        "--selected-detector-mode", type=str, default="comb_dp_blind"
    )
    sb_rep.add_argument(
        "--selected-detector-checkpoint", type=Path, default=None
    )
    sb_rep.add_argument("--e0-run-ids", nargs="+", required=True)
    sb_rep.add_argument("--synthetic-selection-run-id", type=str, required=True)
    sb_rep.add_argument("--matched-steps", type=_positive_int, default=160)
    sb_rep.add_argument("--grad-accum", type=_positive_int, default=4)
    sb_rep.add_argument("--lr", type=_positive_float, default=5e-5)
    sb_rep.add_argument("--max-new-tokens", type=_positive_int, default=1536)
    sb_rep.add_argument("--num-beams", type=_positive_int, default=3)
    sb_rep.add_argument("--seed", type=int, default=42)

    sb_final = sub.add_parser(
        "run-w6-pilot-final",
        help="Execute the single pre-registered W6-pilot-final TEST pass.",
    )
    sb_final.add_argument("--downloads", type=Path, required=True)
    sb_final.add_argument("--run-dir", type=Path, required=True)
    sb_final.add_argument("--preregistration", type=Path, required=True)
    sb_final.add_argument("--w2-s0-run-dir", type=Path, required=True)
    sb_final.add_argument("--immich-dir", type=Path, required=True)
    sb_final.add_argument("--desk-study-dir", type=Path, required=True)
    sb_final.add_argument("--req012-manifest", type=Path, required=True)
    sb_final.add_argument("--gold-cache-dir", type=Path, default=None)


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
    verify_prior_campaign_ledgers(downloads)
    if _is_retired_production_campaign():
        msg = "W6 Slice A packaging campaign (#1766/#1769) is retired"
        raise StopConditionError(msg)
    with w6_disk_lock(downloads, run.parent) as runs:
        _check_w6_disk_cap(runs, additional_bytes=1_048_576)
        active_settings = active_host_settings_path()
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
            allow_missing_settings=args.settings is None,
            version_lookup=lambda: cli_version(exe, _offline_musescore),
            launcher=_offline_musescore,
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
    if args.command in {
        "setup-w6-slice-b",
        "train-w6-gpu",
        "eval-w6-traindev",
        "run-w6-replay",
        "run-w6-pilot-final",
    }:
        return _run_slice_b_command(args)
    msg = f"unknown W6 command: {args.command}"
    raise StopConditionError(msg)


def _run_slice_b_command(args: argparse.Namespace) -> dict[str, object]:
    if args.command == "setup-w6-slice-b":
        return setup_w6_slice_b(
            args.downloads,
            package_run_dir=args.package_run_dir,
            traindev_root=args.traindev_root,
            model_repos=args.models if args.models else None,
            formal=True,
        )
    if args.command == "train-w6-gpu":
        return run_w6_gpu_train(
            args.downloads,
            args.run_dir,
            arm=args.arm,
            model_repo=args.model_repo,
            dataset_dir=args.dataset_dir,
            package_run_dir=args.package_run_dir,
            fuse_stride8=args.fuse_stride8,
            longest_edge=args.longest_edge,
            include_vision_lora=args.include_vision_lora,
            lora_rank=args.lora_rank,
            max_steps=args.max_steps,
            grad_accum=args.grad_accum,
            lr=args.lr,
            eval_every=args.eval_every,
            seed=args.seed,
            resume_from=args.resume_from,
            formal=True,
        )
    if args.command == "eval-w6-traindev":
        return run_w6_traindev_eval(
            args.downloads,
            args.run_dir,
            traindev_root=args.traindev_root,
            mode=args.mode,
            arm=args.arm,
            model_repo=args.model_repo,
            recognizer_checkpoint=args.recognizer_checkpoint,
            detector_mode=args.detector_mode,
            detector_checkpoint=args.detector_checkpoint,
            max_new_tokens=args.max_new_tokens,
            num_beams=args.num_beams,
            formal=True,
        )
    if args.command == "run-w6-replay":
        return run_w6_traindev_replay_and_preregister(
            args.downloads,
            args.run_dir,
            traindev_root=args.traindev_root,
            synth_dataset_dir=args.synth_dataset_dir,
            arm=args.arm,
            model_repo=args.model_repo,
            selected_recognizer_checkpoint=args.selected_recognizer_checkpoint,
            selected_detector_mode=args.selected_detector_mode,
            selected_detector_checkpoint=args.selected_detector_checkpoint,
            e0_run_ids=args.e0_run_ids,
            synthetic_selection_run_id=args.synthetic_selection_run_id,
            matched_steps=args.matched_steps,
            grad_accum=args.grad_accum,
            lr=args.lr,
            max_new_tokens=args.max_new_tokens,
            num_beams=args.num_beams,
            seed=args.seed,
            formal=True,
        )
    return run_w6_pilot_final_test(
        args.downloads,
        args.run_dir,
        preregistration_path=args.preregistration,
        w2_s0_run_dir=args.w2_s0_run_dir,
        immich_dir=args.immich_dir,
        desk_study_dir=args.desk_study_dir,
        req012_manifest_path=args.req012_manifest,
        gold_cache_dir=args.gold_cache_dir,
        formal=True,
    )


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
