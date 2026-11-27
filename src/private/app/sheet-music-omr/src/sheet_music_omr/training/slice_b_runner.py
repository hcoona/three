"""Parent-workspace orchestrator for W6 GPU Slice B training and evaluation."""

from __future__ import annotations

import hashlib
import io
import json
import math
import os
import shutil
import subprocess
from collections import Counter, defaultdict
from dataclasses import asdict, replace
from pathlib import Path
from typing import TYPE_CHECKING, Any, Literal, cast

import numpy as np
from PIL import Image

from sheet_music_omr.assembly import AssemblyStatus, assemble
from sheet_music_omr.comparison import CONTENT_CATEGORIES
from sheet_music_omr.diagnostics import TextScoreError
from sheet_music_omr.evaluation import stats, w5_detectors
from sheet_music_omr.evaluation.alignment import (
    atomic_write_text,
    check_no_symlink_under,
    extract_t01_archive_join,
    sha256_file,
    venv_python_path,
    verify_approved_overlays,
)
from sheet_music_omr.evaluation.cli import (
    DISCREPANCY_SCHEMA,
    _build_discrepancy_log_rows,
    _evaluate_second_reader_recall,
    _inspect_draft_independently,
    _load_and_verify_req012_manifest,
    _piece_table_from_score,
    _summarize_split,
    _validate_discrepancy_row,
)
from sheet_music_omr.evaluation.scorer import PredictionScore, score_prediction
from sheet_music_omr.evaluation.xml_merge import (
    SystemCropAssemblyResult,
    concat_system_predictions,
    validate_candidate_score,
)
from sheet_music_omr.harness.privacy import scan, scrub_paths
from sheet_music_omr.harness.records import (
    check_outside_repository,
    run_git,
    source_state,
)
from sheet_music_omr.harness.stops import StopConditionError
from sheet_music_omr.harness.systems import SystemStart, split_score
from sheet_music_omr.model import AssembledScore, SystemDocument
from sheet_music_omr.musicxml_forward import (
    ReviewRequiredError,
    to_musicxml_text,
)
from sheet_music_omr.musicxml_reverse import from_musicxml_text
from sheet_music_omr.parser import parse_document
from sheet_music_omr.training.asset_locks import (
    TRAINDEV_GOLD_IDS,
    resolve_model_weight_lock,
)
from sheet_music_omr.training.copista_per_system import (
    OracleSystem,
    _kill_worker_group,
    _oracle_partition,
    _rank_blind_draft,
    partition_oracle_systems,
    run_supervised_worker,
)
from sheet_music_omr.training.decoding import (
    TRAINDEV_FOLDS_4,
    AbstentionReason,
    CalibrationPrediction,
    Calibrator,
    InnerValidationPredictions,
    SidecarConfidence,
    SystemDecodeResult,
    build_system_prompt_state,
    build_w7_sidecar_rows,
    emit_aligned_discrepancy_rows,
    fit_nested_calibrators,
    validate_w7_sidecar_row,
)
from sheet_music_omr.training.ledgers import (
    W6_OFFLINE_ENV,
    VerifiedTrainDevRoot,
    check_w6_runs_root,
    open_verified_traindev_root,
)
from sheet_music_omr.training.models import (
    ArmAFamily,
    DetectorFamily,
    DetectorPrediction,
    DetectorTarget,
    compute_class_ap75,
    evaluate_operating_point,
)
from sheet_music_omr.training.slice_b_ledgers import (
    W6_PILOT_FINAL_PURPOSE,
    W6_PREREGISTRATION_SCHEMA,
    W6_SLICE_B_EVAL_PAGE_TIMEOUT_SECONDS,
    W6_SLICE_B_TRAIN_TIMEOUT_SECONDS,
    _git_worktree_roots,
    compute_artifact_sha256s,
    record_w6_pilot_final_preregistration,
    reserve_w6_pilot_final_test_pass,
    reserve_w6_slice_b_run,
    settle_w6_slice_b_run,
    verify_packaged_slice_a_datasets,
    verify_pilot_final_preregistration,
    verify_w6_slice_b_gpu_assets,
)
from sheet_music_omr.writer import write_document

if TYPE_CHECKING:
    from collections.abc import Mapping, Sequence

    from sheet_music_omr.evaluation.w5_detectors import PredictedBox

_TARGET_STAFF_SPACE_PX = 14.0
_REPO_TO_ARM_A_FAMILY: dict[str, ArmAFamily] = {
    "Qwen/Qwen3-VL-2B-Instruct": "qwen3_vl",
    "Qwen/Qwen3-VL-4B-Instruct": "qwen3_vl",
    "Qwen/Qwen3.5-2B": "qwen3_5",
    "PaddlePaddle/PaddleOCR-VL-1.6": "paddleocr_vl",
}
_REPO_TO_ARM_B_ARCH: dict[str, str] = {
    "timm/convnext_tiny.fb_in22k_ft_in1k": "convnext_tiny",
    "zenosai/MonkeyOCRv2-S": "monkeyocr_v2",
}
_REPO_TO_DETECTOR_FAMILY: dict[str, DetectorFamily] = {
    "ustc-community/dfine-small-coco": "d_fine",
    "PekingU/rtdetr_v2_r18vd": "rt_detr_v2",
}
_FOLD_BY_GID: dict[str, int] = {
    gid: f.fold for f in TRAINDEV_FOLDS_4 for gid in f.piece_ids
}
_TEST_GOLD_IDS: frozenset[str] = frozenset(
    {
        "gold-01",
        "gold-02",
        "gold-03",
        "gold-04",
        "gold-05",
        "gold-06",
        "gold-18",
    }
)


def _omr_src_root() -> str:
    return str(Path(__file__).resolve().parents[2])


def _worker_python_cmd(
    gpu_python: Path,
    job_path: Path,
    extra_args: Sequence[str] = (),
) -> list[str]:
    startup = (
        "import sys; sys.path.insert(0, sys.argv[1]); "
        "from sheet_music_omr.training.slice_b_worker import main; "
        "raise SystemExit(main(sys.argv[2:]))"
    )
    return [
        str(gpu_python),
        "-I",
        "-B",
        "-c",
        startup,
        _omr_src_root(),
        str(job_path),
        *extra_args,
    ]


def _validate_slice_b_run_dir(downloads: Path, run_dir: Path) -> Path:
    run = run_dir.absolute()
    check_outside_repository(downloads, run)
    check_w6_runs_root(downloads, run.parent)
    if not run.name.startswith("w6-slice-b-"):
        msg = "Slice B run directory name must start with w6-slice-b-"
        raise StopConditionError(msg)
    check_no_symlink_under(run, Path(run.anchor))
    if run.exists() and any(run.iterdir()):
        msg = (
            f"Slice B run directory already exists and is non-empty: {run.name}"
        )
        raise StopConditionError(msg)
    run.mkdir(parents=True, exist_ok=True)
    return run


def _run_gpu_worker_bounded(
    gpu_python: Path,
    job_cfg: Mapping[str, object],
    *,
    scratch: Path,
    timeout_seconds: float,
    use_unshare: bool,
) -> None:
    scratch.mkdir(parents=True, exist_ok=True)
    job_path = scratch / "worker_job.json"
    job_path.write_text(json.dumps(job_cfg, indent=2) + "\n", encoding="utf-8")
    cmd = [
        *(("unshare", "-rn", "--") if use_unshare else ()),
        *_worker_python_cmd(gpu_python, job_path),
    ]
    env = (
        os.environ
        | W6_OFFLINE_ENV
        | {
            "TMPDIR": str(scratch),
            "OMP_NUM_THREADS": "2",
        }
    )
    err_log = scratch / "worker_stderr.log"
    with err_log.open("wb") as err_fp:
        try:
            proc = subprocess.Popen(  # noqa: S603
                cmd,
                cwd=scratch,
                env=env,
                close_fds=True,
                stdin=subprocess.DEVNULL,
                stdout=err_fp,
                stderr=err_fp,
                start_new_session=True,
            )
        except OSError as exc:
            msg = "cannot launch Slice B GPU worker"
            raise StopConditionError(msg) from exc
        try:
            rc = proc.wait(timeout=timeout_seconds)
        except subprocess.TimeoutExpired as exc:
            _kill_worker_group(proc)
            msg = f"Slice B GPU worker exceeded {timeout_seconds}s deadline"
            raise StopConditionError(msg) from exc
        except BaseException:
            _kill_worker_group(proc)
            raise
    if rc != 0:
        tail = scrub_paths(
            err_log.read_text(encoding="utf-8", errors="replace")[-1200:]
        )
        msg = f"Slice B GPU worker exited with status {rc}: {tail}"
        raise StopConditionError(msg)


def _run_gpu_worker_heartbeat(  # noqa: PLR0913
    gpu_python: Path,
    job_cfg: Mapping[str, object],
    *,
    scratch: Path,
    page_count: int,
    page_timeout: float = W6_SLICE_B_EVAL_PAGE_TIMEOUT_SECONDS,
    namespace_prefix: Sequence[str] = ("unshare", "-rn", "--"),
) -> int:
    scratch.mkdir(parents=True, exist_ok=True)
    job_path = scratch / "heartbeat_job.json"
    job_path.write_text(json.dumps(job_cfg, indent=2) + "\n", encoding="utf-8")
    cmd = _worker_python_cmd(gpu_python, job_path, ("--heartbeat-stdout",))
    env = (
        os.environ
        | W6_OFFLINE_ENV
        | {
            "TMPDIR": str(scratch),
            "OMP_NUM_THREADS": "1",
        }
    )
    outcome = run_supervised_worker(
        cmd,
        scratch=scratch,
        page_count=page_count,
        page_timeout=page_timeout,
        env=env,
        namespace_prefix=namespace_prefix,
        clean_scratch=False,
    )
    if outcome.returncode != 0 or outcome.completed_pages != page_count:
        msg = (
            "Slice B isolated inference worker failed or timed out "
            f"(rc={outcome.returncode}, "
            f"pages={outcome.completed_pages}/{page_count})"
        )
        raise StopConditionError(msg)
    return outcome.completed_pages


def _estimate_page_staff_space_blind(gray_page: Image.Image) -> float:
    arr = np.asarray(gray_page, dtype=np.float32)
    thin = w5_detectors._thin_ink(arr)  # noqa: SLF001
    spacing, _ = w5_detectors._staff_peaks(thin)  # noqa: SLF001
    return float(min(30.0, max(10.0, spacing)))


def _count_blind_staves_in_box(
    gray_page: Image.Image, box: tuple[float, float, float, float]
) -> int:
    x0, y0, x1, y1 = (
        max(0, math.floor(box[0])),
        max(0, math.floor(box[1])),
        min(gray_page.width, math.ceil(box[2])),
        min(gray_page.height, math.ceil(box[3])),
    )
    if x1 - x0 < 32 or y1 - y0 < 32:  # noqa: PLR2004
        return 0
    sub = np.asarray(gray_page.crop((x0, y0, x1, y1)), dtype=np.float32)
    thin = w5_detectors._thin_ink(sub)  # noqa: SLF001
    _, peaks = w5_detectors._staff_peaks(thin)  # noqa: SLF001
    return len(peaks)


def _resample_blind_crop_14px(
    rgb_page: Image.Image,
    box: tuple[float, float, float, float],
    est_staff_space_px: float,
) -> Image.Image:
    x0 = max(0, min(rgb_page.width - 1, math.floor(box[0])))
    y0 = max(0, min(rgb_page.height - 1, math.floor(box[1])))
    x1 = max(x0 + 1, min(rgb_page.width, math.ceil(box[2])))
    y1 = max(y0 + 1, min(rgb_page.height, math.ceil(box[3])))
    crop = rgb_page.crop((x0, y0, x1, y1))
    scale = _TARGET_STAFF_SPACE_PX / max(1.0, est_staff_space_px)
    if abs(scale - 1.0) < 0.02:  # noqa: PLR2004
        return crop
    nw = max(28, round(crop.width * scale))
    nh = max(28, round(crop.height * scale))
    return crop.resize((nw, nh), Image.Resampling.BICUBIC)


def run_w6_gpu_train(  # noqa: C901, PLR0912, PLR0913, PLR0915
    downloads_dir: Path,
    run_dir: Path,
    *,
    arm: Literal["arm-a", "arm-b", "detector"],
    model_repo: str,
    dataset_dir: Path,
    package_run_dir: Path | None = None,
    fuse_stride8: bool = True,
    longest_edge: int = 1024,
    include_vision_lora: bool = False,
    lora_rank: int = 32,
    max_steps: int = 1500,
    grad_accum: int = 8,
    lr: float | None = None,
    eval_every: int | None = None,
    seed: int = 42,
    resume_from: Path | None = None,
    formal: bool = True,
    tiny_fixture: bool = False,
    python_override: Path | None = None,
) -> dict[str, object]:
    """Execute one metered Slice B GPU training run on synthetic data."""
    downloads = downloads_dir.absolute()
    run = _validate_slice_b_run_dir(downloads, run_dir)
    ds_dir = dataset_dir.absolute()
    check_outside_repository(ds_dir)
    check_no_symlink_under(ds_dir, Path(ds_dir.anchor))
    src = source_state(run_git)
    if formal and src["dirty"]:
        msg = "formal Slice B GPU training requires a clean git tree"
        raise StopConditionError(msg)
    if package_run_dir is not None and not tiny_fixture:
        verify_packaged_slice_a_datasets(package_run_dir)
    lock = resolve_model_weight_lock(model_repo)
    env_info = (
        {"tiny_fixture": True}
        if tiny_fixture
        else verify_w6_slice_b_gpu_assets(
            downloads,
            model_repos=[lock.repo_id],
            require_cuda=True,
            require_rtx4090_wsl2=formal,
        )
    )
    gpu_py = (
        python_override
        if python_override is not None
        else venv_python_path(downloads / "w6-gpu-venv")
    )
    weights_dir = downloads / "w6-model-weights" / lock.slug
    job_cfg: dict[str, object] = {
        "model_repo": lock.repo_id,
        "dataset_dir": str(ds_dir),
        "output_dir": str(run / "checkpoints"),
        "weights_dir": str(weights_dir) if weights_dir.is_dir() else None,
        "checkpoint_dir": str(resume_from.resolve()) if resume_from else None,
        "max_steps": max_steps,
        "grad_accum": grad_accum,
        "seed": seed,
        "tiny_fixture": tiny_fixture,
    }
    if eval_every is not None:
        job_cfg["eval_every"] = eval_every
    if arm == "arm-a":
        if lock.repo_id not in _REPO_TO_ARM_A_FAMILY:
            msg = f"{lock.repo_id} is not an approved Arm A model"
            raise StopConditionError(msg)
        job_cfg.update(
            task="train_arm_a",
            arm_a_family=_REPO_TO_ARM_A_FAMILY[lock.repo_id],
            lora=True,
            lora_rank=lora_rank,
            include_vision_lora=include_vision_lora,
            lr=2e-4 if lr is None else lr,
        )
    elif arm == "arm-b":
        if lock.repo_id not in _REPO_TO_ARM_B_ARCH:
            msg = f"{lock.repo_id} is not an approved Arm B encoder"
            raise StopConditionError(msg)
        job_cfg.update(
            task="train_arm_b",
            arm_b_arch=_REPO_TO_ARM_B_ARCH[lock.repo_id],
            fuse_stride8=fuse_stride8,
            lr=3e-4 if lr is None else lr,
        )
    else:
        if lock.repo_id not in _REPO_TO_DETECTOR_FAMILY:
            msg = f"{lock.repo_id} is not an approved page detector"
            raise StopConditionError(msg)
        if longest_edge not in {1024, 1280}:
            msg = "page detector longest_edge must be 1024 or 1280"
            raise StopConditionError(msg)
        job_cfg.update(
            task="train_detector",
            detector_family=_REPO_TO_DETECTOR_FAMILY[lock.repo_id],
            longest_edge=longest_edge,
            lr=1e-4 if lr is None else lr,
        )

    res_id = reserve_w6_slice_b_run(
        downloads,
        run_id=run.name,
        git_commit=str(src["commit"]),
        clean_tree=not bool(src["dirty"]),
        command=f"train-w6-gpu:{arm}:{lock.repo_id}",
        kind="train_gpu",
        runs_parent=run.parent,
    )
    status: Literal["completed", "failed", "aborted"] = "failed"
    scratch = run / "scratch"
    try:
        _run_gpu_worker_bounded(
            gpu_py,
            job_cfg,
            scratch=scratch,
            timeout_seconds=W6_SLICE_B_TRAIN_TIMEOUT_SECONDS,
            use_unshare=False,
        )
        train_summary = json.loads(
            (run / "checkpoints" / "train_summary.json").read_text(
                encoding="utf-8"
            )
        )
        env_out = {
            "run_id": run.name,
            "git_commit": src["commit"],
            "clean_tree": not bool(src["dirty"]),
            "environment": env_info,
        }
        atomic_write_text(
            run / "environment.json",
            scrub_paths(json.dumps(env_out, indent=2, sort_keys=True)) + "\n",
        )
        report_dir = run / "report"
        report_dir.mkdir(parents=True, exist_ok=True)
        report_payload = {
            "schema": "omr-w6-slice-b-train-report-v1",
            "run_id": run.name,
            "git_commit": src["commit"],
            "clean_tree": not bool(src["dirty"]),
            "training": train_summary,
        }
        atomic_write_text(
            report_dir / "train-report.json",
            scrub_paths(json.dumps(report_payload, indent=2, sort_keys=True))
            + "\n",
        )
        status = "completed"
    except KeyboardInterrupt:
        status = "aborted"
        raise
    else:
        return report_payload
    finally:
        shutil.rmtree(scratch, ignore_errors=True)
        settle_w6_slice_b_run(
            downloads, res_id, status=status, runs_parent=run.parent
        )


def _reconstruct_system_results(
    raw_systems: Sequence[Mapping[str, object]],
) -> list[SystemDecodeResult]:
    out: list[SystemDecodeResult] = []
    for item in raw_systems:
        valid = bool(item.get("valid"))
        txt = cast("str | None", item.get("text"))
        doc: SystemDocument | None = None
        if valid and isinstance(txt, str):
            parsed = parse_document(txt)
            if isinstance(parsed, SystemDocument):
                doc = parsed
            else:
                valid = False
        lp_raw = item.get("raw_logprob")
        lp = float(cast("float", lp_raw)) if lp_raw is not None else None
        reason = cast("AbstentionReason | None", item.get("abstention_reason"))
        if not valid and reason is None:
            reason = "parse_invalid"
        out.append(
            SystemDecodeResult(
                valid=valid and doc is not None,
                document=doc,
                text=txt if (valid and doc is not None) else None,
                raw_logprob=lp,
                abstention_reason=None
                if (valid and doc is not None)
                else reason,
                needs_person=bool(item.get("needs_person", False)),
                carry_out_text=cast("str | None", item.get("carry_out_text")),
            )
        )
    return out


def _system_results_to_crop_assembly(  # noqa: C901, PLR0912, PLR0915
    coords: Sequence[tuple[int, int]],
    systems: Sequence[SystemDecodeResult],
) -> tuple[SystemCropAssemblyResult, list[float]]:
    """Convert decoded Text Score 2 systems into a MusicXML assembly result."""
    if not coords:
        return (
            SystemCropAssemblyResult(
                xml_text=None,
                parse_valid=False,
                failed_crops=(),
                pred_measure_crop_indices=(),
                pred_measure_pages=(),
                pred_measure_systems=(),
                pred_measure_raw_conf=(),
                error_reasons={0: "zero predicted system crops"},
            ),
            [],
        )
    valid_prefix: list[SystemDocument] = []
    failed_crops: list[int] = []
    error_reasons: dict[int, str] = {}
    for idx, res in enumerate(systems):
        if res.valid and res.document is not None and not res.needs_person:
            valid_prefix.append(res.document)
        else:
            failed_crops.append(idx)
            error_reasons[idx] = str(
                "review_marker_needs_person"
                if res.needs_person
                else (res.abstention_reason or "parse_invalid")
            )

    # Find longest prefix of valid_prefix that strictly closes and converts
    best_xml: str | None = None
    best_k = 0
    for k in range(len(valid_prefix), 0, -1):
        joined = assemble(valid_prefix[:k])
        if joined.status != AssemblyStatus.SUCCESS or joined.score is None:
            continue
        try:
            cand_xml = to_musicxml_text(joined.score)
            _ = validate_candidate_score(cand_xml)
            best_xml = cand_xml
            best_k = k
            break
        except (
            TextScoreError,
            ReviewRequiredError,
            ValueError,
        ):
            continue

    if best_xml is not None and best_k > 0:
        for extra_fail in range(best_k, len(systems)):
            if extra_fail not in failed_crops:
                failed_crops.append(extra_fail)
                error_reasons[extra_fail] = "trailing_unclosed_prefix"
        crop_indices: list[int] = []
        pages_out: list[int] = []
        sys_out: list[int] = []
        raw_confs: list[float] = []
        raw_scores: list[float] = []
        seen_measures: set[int] = set()
        for c_idx in range(best_k):
            doc = valid_prefix[c_idx]
            sys_lp = systems[c_idx].raw_logprob
            n_blocks = max(1, len(doc.blocks))
            per_block_lp = (
                float(sys_lp) / n_blocks if sys_lp is not None else -10.0
            )
            conf_prob = float(
                math.exp(max(-20.0, min(0.0, per_block_lp / 16.0)))
            )
            for blk in doc.blocks:
                if blk.number in seen_measures:
                    continue
                seen_measures.add(blk.number)
                crop_indices.append(c_idx)
                pages_out.append(coords[c_idx][0])
                sys_out.append(coords[c_idx][1])
                raw_confs.append(round(conf_prob, 6))
                raw_scores.append(round(per_block_lp, 6))
        return (
            SystemCropAssemblyResult(
                xml_text=best_xml,
                parse_valid=True,
                failed_crops=tuple(sorted(set(failed_crops))),
                pred_measure_crop_indices=tuple(crop_indices),
                pred_measure_pages=tuple(pages_out),
                pred_measure_systems=tuple(sys_out),
                pred_measure_raw_conf=tuple(raw_confs),
                error_reasons=error_reasons,
            ),
            raw_scores,
        )

    # Fallback: per-system closed conversion via concat_system_predictions
    specs: list[tuple[int, int, bytes | None]] = []
    confs_map: dict[int, list[float]] = {}
    fallback_scores: list[float] = []
    for c_idx, (pg, sy) in enumerate(coords):
        res = systems[c_idx]
        xml_bytes: bytes | None = None
        if res.valid and res.document is not None and not res.needs_person:
            joined_one = assemble([res.document])
            if (
                joined_one.status == AssemblyStatus.SUCCESS
                and joined_one.score is not None
            ):
                try:
                    s_xml = to_musicxml_text(joined_one.score)
                    _ = validate_candidate_score(s_xml)
                    xml_bytes = s_xml.encode("utf-8")
                    n_m = len({b.number for b in res.document.blocks})
                    sys_lp = res.raw_logprob
                    m_lp = float(sys_lp) / max(1, n_m) if sys_lp else -10.0
                    c_prob = float(math.exp(max(-20.0, min(0.0, m_lp / 16.0))))
                    confs_map[c_idx] = [round(c_prob, 6)] * n_m
                    fallback_scores.extend([round(m_lp, 6)] * n_m)
                except TextScoreError, ReviewRequiredError, ValueError:
                    xml_bytes = None
        specs.append((pg, sy, xml_bytes))
    assembled = concat_system_predictions(specs, crop_raw_confs=confs_map)
    return assembled, fallback_scores


def _extract_blind_pages_from_bytes(  # noqa: PLR0913, PLR0915
    page_items: Sequence[tuple[str, int, bytes]],
    *,
    scratch: Path,
    detector_mode: str,
    detector_checkpoint: Path | None,
    gpu_python: Path,
    namespace_prefix: Sequence[str],
) -> tuple[
    dict[str, dict[int, list[tuple[int, PredictedBox]]]],
    list[dict[str, object]],
    dict[str, list[DetectorPrediction]],
]:
    """Freeze blind system boxes and 14.0 px crops before any oracle read."""
    crops_dir = scratch / "blind_crops"
    pages_dir = scratch / "raw_pages"
    crops_dir.mkdir(parents=True, exist_ok=True)
    pages_dir.mkdir(parents=True, exist_ok=True)
    page_meta: list[tuple[str, int, Path, float]] = []
    for gid, pg_idx, raw_png in page_items:
        p_file = pages_dir / f"{gid}-p{pg_idx:02d}.png"
        p_file.write_bytes(raw_png)
        with Image.open(io.BytesIO(raw_png)) as im:
            est_sp = _estimate_page_staff_space_blind(im.convert("L"))
        page_meta.append((gid, pg_idx, p_file, est_sp))

    unthresholded_by_page: dict[str, list[DetectorPrediction]] = {}
    det_boxes_by_stem: dict[str, list[tuple[float, float, float, float]]] = {}
    if detector_mode != "comb_dp_blind":
        if detector_checkpoint is None:
            msg = (
                f"detector_mode {detector_mode!r} "
                "requires --detector-checkpoint"
            )
            raise StopConditionError(msg)
        op_file = detector_checkpoint / "detector_operating_point.json"
        op_meta = (
            json.loads(op_file.read_text(encoding="utf-8"))
            if op_file.is_file()
            else {}
        )
        det_out = scratch / "detector_preds.json"
        det_job = {
            "task": "infer_detector",
            "checkpoint_dir": str(detector_checkpoint.resolve()),
            "longest_edge": int(op_meta.get("longest_edge", 1024)),
            "tau_system": float(op_meta.get("tau_system", 0.5)),
            "tau_measure": float(op_meta.get("tau_measure", 0.5)),
            "pages": [
                {"page_id": f"{gid}-p{pg_idx:02d}", "png_path": str(p_file)}
                for gid, pg_idx, p_file, _ in page_meta
            ],
            "output_json": str(det_out),
        }
        _run_gpu_worker_heartbeat(
            gpu_python,
            det_job,
            scratch=scratch / "det_work",
            page_count=len(page_meta),
            namespace_prefix=namespace_prefix,
        )
        det_res = json.loads(det_out.read_text(encoding="utf-8"))
        tau_sys = float(det_res["tau_system"])
        for pg_row in det_res["pages"]:
            pid = str(pg_row["page_id"])
            preds = [
                DetectorPrediction(
                    page_id=pid,
                    class_id=int(p["class_id"]),
                    query_idx=int(p["query_idx"]),
                    probability=float(p["probability"]),
                    bbox=cast(
                        "tuple[float, float, float, float]", tuple(p["bbox"])
                    ),
                )
                for p in pg_row["predictions"]
            ]
            unthresholded_by_page[pid] = preds
            sys_boxes = [
                p.bbox
                for p in preds
                if p.class_id == 0 and p.probability >= tau_sys
            ]
            # Sort detected system boxes strictly top-to-bottom in reading order
            sys_boxes.sort(key=lambda b: ((b[1] + b[3]) / 2.0, b[0]))
            det_boxes_by_stem[pid] = sys_boxes

    blind_by_gid: dict[str, dict[int, list[tuple[int, PredictedBox]]]] = (
        defaultdict(dict)
    )
    pieces_worker_cfg: dict[str, list[dict[str, object]]] = defaultdict(list)
    slot_by_gid: dict[str, int] = defaultdict(int)
    for gid, pg_idx, p_file, est_sp in page_meta:
        stem = f"{gid}-p{pg_idx:02d}"
        with Image.open(p_file) as raw_im:
            rgb = raw_im.convert("RGB")
            gray = raw_im.convert("L")
        if detector_mode == "comb_dp_blind":
            p_boxes = list(w5_detectors.detect_grand_staff_systems_blind(gray))
        else:
            p_boxes = [
                (
                    b[0],
                    b[1],
                    b[2],
                    b[3],
                    _count_blind_staves_in_box(gray, b),
                )
                for b in det_boxes_by_stem.get(stem, [])
            ]
        blind_by_gid[gid][pg_idx] = []
        for sys_on_pg, pbox in enumerate(p_boxes, 1):
            slot = slot_by_gid[gid]
            slot_by_gid[gid] += 1
            blind_by_gid[gid][pg_idx].append((slot, pbox))
            norm_crop = _resample_blind_crop_14px(
                rgb, (pbox[0], pbox[1], pbox[2], pbox[3]), est_sp
            )
            c_path = crops_dir / f"{stem}-s{sys_on_pg:02d}.png"
            norm_crop.save(c_path, format="PNG")
            pieces_worker_cfg[gid].append(
                {
                    "slot": slot,
                    "page_index": pg_idx,
                    "page_system_index": sys_on_pg,
                    "bbox_px": [pbox[0], pbox[1], pbox[2], pbox[3]],
                    "crop_path": str(c_path),
                }
            )

    pages_by_gid: dict[str, set[int]] = defaultdict(set)
    for gid, pg_idx, _ in page_items:
        pages_by_gid[gid].add(pg_idx)
    all_gids = sorted(pages_by_gid)
    worker_pieces: list[dict[str, object]] = [
        {
            "gold_id": gid,
            "expected_pages": sorted(pages_by_gid[gid]),
            "crops": pieces_worker_cfg.get(gid, []),
        }
        for gid in all_gids
    ]
    return dict(blind_by_gid), worker_pieces, unthresholded_by_page


def _evaluate_detector_cv_traindev(
    root: VerifiedTrainDevRoot,
    *,
    scratch: Path,
    detector_checkpoint: Path,
    gpu_python: Path,
    namespace_prefix: Sequence[str],
) -> dict[str, object]:
    """Compare trained 2-class detector vs comb_dp_blind across 4 folds."""
    page_items: list[tuple[str, int, bytes]] = []
    for rel in root.page_relpaths:
        stem = Path(rel).stem
        gid, p_str = stem.split("-p", 1)
        page_items.append((gid, int(p_str), root.read_verified_leaf_bytes(rel)))
    blind_det, _, unthresh = _extract_blind_pages_from_bytes(
        page_items,
        scratch=scratch,
        detector_mode="neural_detector",
        detector_checkpoint=detector_checkpoint,
        gpu_python=gpu_python,
        namespace_prefix=namespace_prefix,
    )
    manifest = json.loads(
        root.read_verified_leaf_bytes("traindev-manifest.json")
    )
    oracle_by_gid: dict[str, dict[int, list[OracleSystem]]] = {}
    gt_targets_by_fold: dict[int, list[DetectorTarget]] = defaultdict(list)
    preds_sys_by_fold: dict[int, list[DetectorPrediction]] = defaultdict(list)
    comb_matches_by_fold: dict[int, list[w5_detectors.PageMatchReport]] = (
        defaultdict(list)
    )
    neural_geom_by_fold: dict[int, list[w5_detectors.PageMatchReport]] = (
        defaultdict(list)
    )
    neural_2staff_by_fold: dict[int, list[w5_detectors.PageMatchReport]] = (
        defaultdict(list)
    )
    for piece in manifest["pieces"]:
        gid = str(piece["gold_id"])
        fold = _FOLD_BY_GID[gid]
        pg_indices = {pg for g, pg, _ in page_items if g == gid}
        oracle_pages = _oracle_partition(piece, pg_indices)
        oracle_by_gid[gid] = oracle_pages
        for pg_idx, o_systems in oracle_pages.items():
            pid = f"{gid}-p{pg_idx:02d}"
            for osys in o_systems:
                gt_targets_by_fold[fold].append(DetectorTarget(pid, osys.box))
            for pred in unthresh.get(pid, []):
                if pred.class_id == 0:
                    preds_sys_by_fold[fold].append(pred)
            raw_bytes = root.read_verified_leaf_bytes(
                f"pages/{gid}-p{pg_idx:02d}.png"
            )
            with Image.open(io.BytesIO(raw_bytes)) as im:
                comb_boxes = list(
                    w5_detectors.detect_grand_staff_systems_blind(
                        im.convert("L")
                    )
                )
            o_boxes = [s.box for s in o_systems]
            c_rep, _ = w5_detectors.match_page_boxes_with_pairs(
                comb_boxes, o_boxes
            )
            comb_matches_by_fold[fold].append(c_rep)
            n_boxes_raw = [
                pb for _, pb in blind_det.get(gid, {}).get(pg_idx, [])
            ]
            # Pure 2D geometry match vs optional W5 2-staff predicate
            n_boxes_geom = [(b[0], b[1], b[2], b[3], 2) for b in n_boxes_raw]
            ng_rep, _ = w5_detectors.match_page_boxes_with_pairs(
                n_boxes_geom, o_boxes
            )
            n2_rep, _ = w5_detectors.match_page_boxes_with_pairs(
                n_boxes_raw, o_boxes
            )
            neural_geom_by_fold[fold].append(ng_rep)
            neural_2staff_by_fold[fold].append(n2_rep)

    op_file = detector_checkpoint / "detector_operating_point.json"
    tau_sys = (
        float(json.loads(op_file.read_text("utf-8")).get("tau_system", 0.5))
        if op_file.is_file()
        else 0.5
    )
    fold_summaries: dict[str, object] = {}
    all_preds_sys: list[DetectorPrediction] = []
    all_gt_sys: list[DetectorTarget] = []
    for f in range(4):
        all_preds_sys.extend(preds_sys_by_fold[f])
        all_gt_sys.extend(gt_targets_by_fold[f])
        ap = compute_class_ap75(preds_sys_by_fold[f], gt_targets_by_fold[f])
        op85 = evaluate_operating_point(
            preds_sys_by_fold[f],
            gt_targets_by_fold[f],
            threshold=tau_sys,
            min_iou=0.85,
        )
        fold_summaries[str(f)] = {
            "system_ap75": round(ap, 6) if ap is not None else None,
            "iou85_precision": round(op85.precision, 6),
            "iou85_recall": round(op85.recall, 6),
            "neural_geometry_matching": asdict(
                w5_detectors.summarize_page_matches(neural_geom_by_fold[f])
            ),
            "neural_w5_two_staff_matching": asdict(
                w5_detectors.summarize_page_matches(neural_2staff_by_fold[f])
            ),
            "comb_dp_blind_matching": asdict(
                w5_detectors.summarize_page_matches(comb_matches_by_fold[f])
            ),
        }
    pooled_ap = compute_class_ap75(all_preds_sys, all_gt_sys)
    pooled_op85 = evaluate_operating_point(
        all_preds_sys, all_gt_sys, threshold=tau_sys, min_iou=0.85
    )
    return {
        "schema": "omr-w6-slice-b-detector-traindev-cv-v1",
        "split": "TRAIN_DEV",
        "test_touches": 0,
        "tau_system": tau_sys,
        "pooled": {
            "system_ap75": (
                round(pooled_ap, 6) if pooled_ap is not None else None
            ),
            "iou85_precision": round(pooled_op85.precision, 6),
            "iou85_recall": round(pooled_op85.recall, 6),
            "neural_geometry_matching": asdict(
                w5_detectors.summarize_page_matches(
                    [r for f in range(4) for r in neural_geom_by_fold[f]]
                )
            ),
            "neural_w5_two_staff_matching": asdict(
                w5_detectors.summarize_page_matches(
                    [r for f in range(4) for r in neural_2staff_by_fold[f]]
                )
            ),
            "comb_dp_blind_matching": asdict(
                w5_detectors.summarize_page_matches(
                    [r for f in range(4) for r in comb_matches_by_fold[f]]
                )
            ),
        },
        "by_fold": fold_summaries,
    }


def _score_recognizer_on_traindev(  # noqa: C901, PLR0912, PLR0913, PLR0915
    root: VerifiedTrainDevRoot,
    *,
    run_id: str,
    reader_id: str,
    evidence_role: str,
    blind_by_gid: Mapping[str, dict[int, list[tuple[int, PredictedBox]]]],
    worker_pieces: Sequence[Mapping[str, object]],
    worker_out: Mapping[str, Any],
    logs_dir: Path,
    replay_inner_predictions: (
        Mapping[tuple[int, int], InnerValidationPredictions] | None
    ) = None,
) -> dict[str, object]:
    """Score blind recognizer on TRAIN_DEV, calibrate 4-fold, and emit logs."""
    manifest = json.loads(
        root.read_verified_leaf_bytes("traindev-manifest.json")
    )
    pm_by_gid = {str(p["gold_id"]): dict(p) for p in manifest["pieces"]}
    oracle_by_gid: dict[str, dict[int, list[OracleSystem]]] = {}
    for gid, pm in pm_by_gid.items():
        pm.setdefault("split", "TRAIN_DEV")
        pm.setdefault("book_cohort", "book1")
        pg_set = {int(s["page_index"]) for s in pm["systems"]}
        oracle_by_gid[gid] = _oracle_partition(pm, pg_set)

    # Phase 1: Assemble blind system crops and read initial drafts
    assemblies: dict[str, SystemCropAssemblyResult] = {}
    sys_results_by_gid: dict[str, list[SystemDecodeResult]] = {}
    coords_by_gid: dict[str, list[tuple[int, int]]] = {}
    crop_boxes_by_gid: dict[str, list[tuple[float, float, float, float]]] = {}
    draft_xmls: dict[str, str] = {}

    pieces_decoded = cast("dict[str, dict[str, object]]", worker_out["pieces"])
    for p_cfg in worker_pieces:
        gid = str(p_cfg["gold_id"])
        crops_cfg = cast("list[dict[str, Any]]", p_cfg["crops"])
        coords = [
            (int(c["page_index"]), int(c["page_system_index"]))
            for c in crops_cfg
        ]
        c_boxes = [
            cast("tuple[float, float, float, float]", tuple(c["bbox_px"]))
            for c in crops_cfg
        ]
        raw_sys = cast(
            "list[dict[str, object]]", pieces_decoded[gid]["systems"]
        )
        sys_res = _reconstruct_system_results(raw_sys)
        asm, _ = _system_results_to_crop_assembly(coords, sys_res)
        assemblies[gid] = asm
        sys_results_by_gid[gid] = sys_res
        coords_by_gid[gid] = coords
        crop_boxes_by_gid[gid] = c_boxes
        draft_xmls[gid] = root.read_verified_leaf_bytes(
            f"gold/{gid}/draft.musicxml"
        ).decode("utf-8")

    # Phase 2: Oracle-partitioned scoring & 4-fold calibration predictions
    cand_scores: dict[str, PredictionScore] = {}
    draft_scores: dict[str, PredictionScore] = {}
    cand_tables: dict[str, stats.PieceMeasureTable] = {}
    draft_tables: dict[str, stats.PieceMeasureTable] = {}
    cand_l1conv: dict[str, stats.PieceMeasureTable] = {}
    draft_l1conv: dict[str, stats.PieceMeasureTable] = {}
    raw_conf_by_gm: dict[tuple[str, int], float] = {}
    l2_err_by_gm: dict[tuple[str, int], bool] = {}
    cal_preds_by_fold: dict[int, list[CalibrationPrediction]] = {
        f: [] for f in range(4)
    }
    gm_order_by_fold: dict[int, list[tuple[str, int]]] = {
        f: [] for f in range(4)
    }
    matched_reports: list[w5_detectors.PageMatchReport] = []
    l2_set = frozenset(stats.LAYER2_CATEGORIES)
    layer1_all = frozenset(stats.LAYER1_CATEGORIES)

    for gid in sorted(TRAINDEV_GOLD_IDS):
        accepted_txt = root.read_verified_leaf_bytes(
            f"gold/{gid}/accepted.musicxml"
        ).decode("utf-8")
        asm = assemblies[gid]
        boxes_raw = blind_by_gid.get(gid, {})
        # For neural detector crops, score geometry matching cleanly
        boxes_for_oracle = {
            pg: [(slot, (b[0], b[1], b[2], b[3], 2)) for slot, b in pg_list]
            for pg, pg_list in boxes_raw.items()
        }
        allowed, forbidden, page_reps = partition_oracle_systems(
            boxes_for_oracle, oracle_by_gid[gid], asm
        )
        matched_reports.extend(page_reps)
        c_sc = score_prediction(
            asm.xml_text,
            accepted_txt,
            forbidden_ref_ordinals=forbidden,
            allowed_ref_ordinals_by_cand=allowed,
        )
        d_sc = score_prediction(draft_xmls[gid], accepted_txt)
        cand_scores[gid] = c_sc
        draft_scores[gid] = d_sc

        pm = pm_by_gid[gid]
        c_tb_raw = _piece_table_from_score(pm, c_sc)
        c_tb = replace(
            c_tb_raw,
            layer1_errors=tuple(
                layer1_all if i in forbidden else errs
                for i, errs in enumerate(c_tb_raw.layer1_errors, 1)
            ),
            masked_categories=tuple(
                frozenset() if i in forbidden else masks
                for i, masks in enumerate(c_tb_raw.masked_categories, 1)
            ),
        )
        cand_tables[gid] = c_tb
        draft_tables[gid] = _piece_table_from_score(pm, d_sc)

        c_l1_raw = _piece_table_from_score(pm, c_sc, l1_converged_only=True)
        cand_l1conv[gid] = replace(
            c_l1_raw,
            layer1_errors=tuple(
                layer1_all if i in forbidden else errs
                for i, errs in enumerate(c_l1_raw.layer1_errors, 1)
            ),
            masked_categories=tuple(
                frozenset() if i in forbidden else masks
                for i, masks in enumerate(c_l1_raw.masked_categories, 1)
            ),
        )
        draft_l1conv[gid] = _piece_table_from_score(
            pm, d_sc, l1_converged_only=True
        )

        mask_map: dict[int, set[str]] = {
            m: set() for m in range(1, int(pm["measure_count"]) + 1)
        }
        for entry in pm.get("open_reading_masks", []):
            mask_map[int(entry["measure_ordinal"])].update(
                str(c) for c in entry["categories"]
            )
        fold = _FOLD_BY_GID[gid]
        for ms in c_sc.measures:
            m_ord = ms.measure_ordinal
            c_ord = ms.aligned_cand_ordinal
            rc = (
                float(asm.pred_measure_raw_conf[c_ord - 1])
                if (
                    c_sc.parse_valid
                    and c_ord is not None
                    and 1 <= c_ord <= len(asm.pred_measure_raw_conf)
                )
                else 0.0
            )
            raw_conf_by_gm[(gid, m_ord)] = rc
            unmasked_l2 = l2_set - mask_map[m_ord]
            has_err = bool(
                m_ord in forbidden
                or (set(ms.layer2_counts.keys()) & unmasked_l2)
            )
            l2_err_by_gm[(gid, m_ord)] = has_err
            logit_score = math.log(
                max(1e-6, min(1.0 - 1e-6, rc))
                / max(1e-6, 1.0 - min(1.0 - 1e-6, rc))
            )
            cal_preds_by_fold[fold].append(
                CalibrationPrediction(
                    score=logit_score,
                    has_error=has_err,
                    category="overall",
                    supervised=bool(unmasked_l2 or m_ord in forbidden),
                )
            )
            gm_order_by_fold[fold].append((gid, m_ord))

    nested_platt = fit_nested_calibrators(
        cal_preds_by_fold,
        method="platt",
        replay_predictions=replay_inner_predictions,
    )
    nested_temp = fit_nested_calibrators(
        cal_preds_by_fold,
        method="temperature",
        replay_predictions=replay_inner_predictions,
    )
    cal_conf_by_gm: dict[tuple[str, int], float] = {}
    for f in range(4):
        oof_probs = nested_platt.out_of_fold[f]
        for (gid, m_ord), prob in zip(
            gm_order_by_fold[f], oof_probs, strict=True
        ):
            cal_conf_by_gm[(gid, m_ord)] = (
                round(float(prob), 6) if prob is not None else 0.5
            )

    # Phase 3: Rank blind second-reader flags using out-of-fold calibrators!
    blind_measures: list[stats.BlindDraftMeasure] = []
    blind_lookup: dict[tuple[str, int], stats.BlindDraftMeasure] = {}
    blind_scores: dict[str, PredictionScore] = {}
    indep_draft_counts: dict[str, int] = {}
    for gid in sorted(TRAINDEV_GOLD_IDS):
        asm = assemblies[gid]
        fold_cal = nested_platt.by_fold[_FOLD_BY_GID[gid]].get("overall")
        cal_pred_confs: list[float] = []
        for rc_val in asm.pred_measure_raw_conf:
            lg_val = math.log(
                max(1e-6, min(1.0 - 1e-6, rc_val))
                / max(1e-6, 1.0 - min(1.0 - 1e-6, rc_val))
            )
            pred_c = fold_cal.predict(lg_val) if fold_cal is not None else None
            cal_pred_confs.append(
                round(float(pred_c), 6) if pred_c is not None else rc_val
            )
        asm_for_blind = replace(
            asm, pred_measure_raw_conf=tuple(cal_pred_confs)
        )
        b_sc, b_ms = _rank_blind_draft(gid, asm_for_blind, draft_xmls[gid])
        blind_scores[gid] = b_sc
        indep_draft_counts[gid] = b_sc.ref_measure_count
        blind_measures.extend(b_ms)
        for bm in b_ms:
            blind_lookup[(gid, bm.draft_ordinal)] = bm

    flags = stats.rank_second_reader_flags(blind_measures)
    f20 = flags["at_20pct"]

    # Emit omr-w7-sidecar-v1 and omr-discrepancy-log-v1
    logs_dir.mkdir(parents=True, exist_ok=True)
    sidecar_rows: list[dict[str, object]] = []
    for gid in sorted(TRAINDEV_GOLD_IDS):
        b_sc = blind_scores[gid]
        c_sc = cand_scores[gid]
        cand_to_draft: dict[int, int] = {}
        for d_ms in b_sc.measures:
            if d_ms.aligned_cand_ordinal is not None:
                cand_to_draft[d_ms.aligned_cand_ordinal] = d_ms.measure_ordinal
        cand_to_gold: dict[int, int] = {}
        for g_ms in c_sc.measures:
            if g_ms.aligned_cand_ordinal is not None:
                cand_to_gold[g_ms.aligned_cand_ordinal] = g_ms.measure_ordinal
        budget_map = {
            (gid, d_ord): ((gid, d_ord) in f20)
            for d_ord in range(1, indep_draft_counts[gid] + 1)
        }
        running_cand_ord = 0
        for s_idx, (coords_pair, sys_res, sys_box) in enumerate(
            zip(
                coords_by_gid[gid],
                sys_results_by_gid[gid],
                crop_boxes_by_gid[gid],
                strict=True,
            )
        ):
            local_to_draft: dict[int, int] = {}
            local_to_gold: dict[int, int] = {}
            conf_by_ms: dict[tuple[int, int], SidecarConfidence] = {}
            if sys_res.valid and sys_res.document is not None:
                for local_m in range(1, len(sys_res.document.blocks) + 1):
                    global_c = running_cand_ord + local_m
                    if global_c in cand_to_draft:
                        local_to_draft[local_m] = cand_to_draft[global_c]
                    if global_c in cand_to_gold:
                        g_ord = cand_to_gold[global_c]
                        local_to_gold[local_m] = g_ord
                        cc = cal_conf_by_gm.get((gid, g_ord))
                    else:
                        cc = None
                    for st in (1, 2):
                        conf_by_ms[(local_m, st)] = SidecarConfidence(
                            calibrated_confidence=cc
                        )
                running_cand_ord += len(sys_res.document.blocks)
            s_rows = build_w7_sidecar_rows(
                sys_res,
                run_id=run_id,
                sample_or_piece_id=gid,
                split="TRAIN_DEV",
                page_index=coords_pair[0],
                system_index=s_idx,
                page_system_bbox_px=sys_box,
                confidences=conf_by_ms,
                pred_to_draft_ordinal=local_to_draft,
                pred_to_gold_ordinal=local_to_gold,
                budgeted_draft_flags=budget_map,
            )
            sidecar_rows.extend(s_rows)

    for s_row in sidecar_rows:
        validate_w7_sidecar_row(s_row)
    atomic_write_text(
        logs_dir / "w7-sidecar.jsonl",
        "".join(
            json.dumps(r, separators=(",", ":")) + "\n" for r in sidecar_rows
        ),
    )

    disc_rows = _build_discrepancy_log_rows(
        run_id=run_id,
        copista_reader_id=reader_id,
        pieces_meta=[pm_by_gid[g] for g in sorted(TRAINDEV_GOLD_IDS)],
        draft_scores=draft_scores,
        copista_scores=cand_scores,
        raw_conf_by_gm=raw_conf_by_gm,
        cal_conf_by_gm=cal_conf_by_gm,
        blind_lookup=blind_lookup,
        flags_by_split={"TRAIN_DEV": flags},
        t01_join={"per_measure_rounds": {}, "unlocated_rounds": {}},
        req012_post_by_m={},
        candidate_excluded_categories=(),
    )
    for gid in sorted(TRAINDEV_GOLD_IDS):
        gid_rows = [r for r in disc_rows if r["gold_id"] == gid]
        emit_aligned_discrepancy_rows(
            gid_rows, split="TRAIN_DEV", sample_or_piece_id=gid
        )
    atomic_write_text(
        logs_dir / "omr-discrepancy-log-v1.schema.json",
        json.dumps(DISCREPANCY_SCHEMA, indent=2) + "\n",
    )
    atomic_write_text(
        logs_dir / "discrepancy-log.jsonl",
        "".join(json.dumps(r, separators=(",", ":")) + "\n" for r in disc_rows),
    )

    all_gids = sorted(TRAINDEV_GOLD_IDS)
    by_fold: dict[str, object] = {}
    for fold_spec in TRAINDEV_FOLDS_4:
        f_gids = list(fold_spec.piece_ids)
        f_cand = [cand_tables[g] for g in f_gids]
        f_draft = [draft_tables[g] for g in f_gids]
        by_fold[str(fold_spec.fold)] = {
            "pieces": f_gids,
            "plate_groups": list(fold_spec.plate_groups),
            "candidate_layer1": stats.overall_rate(f_cand),
            "candidate_layer1_converged": stats.overall_rate(
                [cand_l1conv[g] for g in f_gids]
            ),
            "draft_layer1": stats.overall_rate(f_draft),
        }

    total_systems = sum(len(sys_results_by_gid[g]) for g in all_gids)
    valid_systems = sum(
        sum(1 for s in sys_results_by_gid[g] if s.valid) for g in all_gids
    )
    abstention_counts = Counter(
        s.abstention_reason
        for g in all_gids
        for s in sys_results_by_gid[g]
        if not s.valid and s.abstention_reason is not None
    )
    return {
        "schema": "omr-w6-slice-b-traindev-eval-v1",
        "run_id": run_id,
        "reader_id": reader_id,
        "evidence_role": evidence_role,
        "split": "TRAIN_DEV",
        "test_touches": 0,
        "pieces": len(all_gids),
        "parsed_pieces": sum(1 for g in all_gids if cand_scores[g].parse_valid),
        "emitted_systems": total_systems,
        "valid_systems": valid_systems,
        "abstention_counts": dict(abstention_counts),
        "oracle_box_matching": asdict(
            w5_detectors.summarize_page_matches(matched_reports)
        ),
        "pooled_layer1_all": stats.overall_rate(
            [cand_tables[g] for g in all_gids]
        ),
        "pooled_layer1_converged": stats.overall_rate(
            [cand_l1conv[g] for g in all_gids]
        ),
        "draft_layer1_all": stats.overall_rate(
            [draft_tables[g] for g in all_gids]
        ),
        "draft_layer1_converged": stats.overall_rate(
            [draft_l1conv[g] for g in all_gids]
        ),
        "blind_second_reader": _evaluate_second_reader_recall(
            gids=all_gids,
            d_list=[draft_l1conv[g] for g in all_gids],
            c_list=[cand_l1conv[g] for g in all_gids],
            draft_scores=draft_scores,
            indep_draft_counts=indep_draft_counts,
            flags=flags,
            survivor_anchors=set(),
            survivor_spans=set(),
        ),
        "nested_calibration": {
            "platt": dict(nested_platt.metrics_by_category.get("overall", {})),
            "temperature": dict(
                nested_temp.metrics_by_category.get("overall", {})
            ),
            "mode": nested_platt.mode,
            "selection_conditional": True,
        },
        "sidecar_rows": len(sidecar_rows),
        "discrepancy_rows": len(disc_rows),
        "by_fold": by_fold,
    }


def run_w6_traindev_eval(  # noqa: PLR0913, PLR0915
    downloads_dir: Path,
    run_dir: Path,
    *,
    traindev_root: Path,
    mode: Literal["e0", "detector-cv", "synthetic-cv"],
    arm: Literal["arm-a", "arm-b"] = "arm-a",
    model_repo: str | None = None,
    recognizer_checkpoint: Path | None = None,
    detector_mode: str = "comb_dp_blind",
    detector_checkpoint: Path | None = None,
    max_new_tokens: int = 1536,
    num_beams: int = 3,
    formal: bool = True,
    tiny_fixture: bool = False,
    python_override: Path | None = None,
    namespace_prefix: Sequence[str] = ("unshare", "-rn", "--"),
) -> dict[str, object]:
    """Run offline E0, detector CV, or recognizer CV on sealed TRAIN_DEV."""
    downloads = downloads_dir.absolute()
    run = _validate_slice_b_run_dir(downloads, run_dir)
    src = source_state(run_git)
    if formal and src["dirty"]:
        msg = "formal Slice B TRAIN_DEV evaluation requires a clean git tree"
        raise StopConditionError(msg)
    if mode == "e0" and (
        arm != "arm-a"
        or recognizer_checkpoint is not None
        or detector_mode != "comb_dp_blind"
        or detector_checkpoint is not None
    ):
        msg = (
            "E0 zero-shot baseline forbids fine-tuned checkpoints or neural"
            " detector overrides"
        )
        raise StopConditionError(msg)
    repos_to_check = [model_repo] if model_repo is not None else None
    env_info = (
        {"tiny_fixture": True}
        if tiny_fixture
        else verify_w6_slice_b_gpu_assets(
            downloads,
            model_repos=repos_to_check,
            require_cuda=True,
            require_rtx4090_wsl2=formal,
        )
    )
    gpu_py = (
        python_override
        if python_override is not None
        else venv_python_path(downloads / "w6-gpu-venv")
    )
    # Reserve BEFORE opening traindev_root
    res_id = reserve_w6_slice_b_run(
        downloads,
        run_id=run.name,
        git_commit=str(src["commit"]),
        clean_tree=not bool(src["dirty"]),
        command=f"eval-w6-traindev:{mode}",
        kind="eval_traindev",
        runs_parent=run.parent,
    )
    status: Literal["completed", "failed", "aborted"] = "failed"
    scratch = run / "scratch"
    try:
        with open_verified_traindev_root(
            traindev_root, worktree_roots=_git_worktree_roots()
        ) as td_root:
            if mode == "detector-cv":
                if detector_checkpoint is None:
                    msg = "detector-cv requires --detector-checkpoint"
                    raise StopConditionError(msg)
                payload = _evaluate_detector_cv_traindev(
                    td_root,
                    scratch=scratch,
                    detector_checkpoint=detector_checkpoint.resolve(),
                    gpu_python=gpu_py,
                    namespace_prefix=namespace_prefix,
                )
            else:
                page_items = [
                    (
                        Path(rel).stem.split("-p", 1)[0],
                        int(Path(rel).stem.split("-p", 1)[1]),
                        td_root.read_verified_leaf_bytes(rel),
                    )
                    for rel in td_root.page_relpaths
                ]
                blind_by_gid, worker_pieces, _ = (
                    _extract_blind_pages_from_bytes(
                        page_items,
                        scratch=scratch,
                        detector_mode=detector_mode,
                        detector_checkpoint=(
                            detector_checkpoint.resolve()
                            if detector_checkpoint is not None
                            else None
                        ),
                        gpu_python=gpu_py,
                        namespace_prefix=namespace_prefix,
                    )
                )
                rec_out = scratch / "recognizer_preds.json"
                w_cfg: dict[str, object] = {
                    "task": "infer_recognizer",
                    "arm": arm,
                    "zero_shot_e0": mode == "e0",
                    "max_new_tokens": max_new_tokens,
                    "num_beams": num_beams,
                    "pieces": worker_pieces,
                    "output_json": str(rec_out),
                    "tiny_fixture": tiny_fixture,
                }
                if arm == "arm-a":
                    if model_repo is None:
                        msg = "Arm A evaluation requires --model-repo"
                        raise StopConditionError(msg)
                    lock = resolve_model_weight_lock(model_repo)
                    w_dir = downloads / "w6-model-weights" / lock.slug
                    w_cfg.update(
                        arm_a_family=_REPO_TO_ARM_A_FAMILY[lock.repo_id],
                        weights_dir=str(w_dir) if w_dir.is_dir() else None,
                        checkpoint_dir=(
                            str(recognizer_checkpoint.resolve())
                            if recognizer_checkpoint is not None
                            else None
                        ),
                        lora=recognizer_checkpoint is not None,
                    )
                    reader_id = (
                        f"e0-{lock.slug}" if mode == "e0" else f"w6-{lock.slug}"
                    )
                else:
                    if recognizer_checkpoint is None:
                        msg = (
                            "Arm B evaluation requires --recognizer-checkpoint"
                        )
                        raise StopConditionError(msg)
                    w_cfg["checkpoint_dir"] = str(
                        recognizer_checkpoint.resolve()
                    )
                    reader_id = f"w6-arm-b-{recognizer_checkpoint.parent.name}"
                _run_gpu_worker_heartbeat(
                    gpu_py,
                    w_cfg,
                    scratch=scratch / "rec_work",
                    page_count=len(page_items),
                    namespace_prefix=namespace_prefix,
                )
                worker_decoded = json.loads(rec_out.read_text(encoding="utf-8"))
                payload = _score_recognizer_on_traindev(
                    td_root,
                    run_id=run.name,
                    reader_id=reader_id,
                    evidence_role=(
                        "zero_shot_baseline"
                        if mode == "e0"
                        else "cross_validated_selection"
                    ),
                    blind_by_gid=blind_by_gid,
                    worker_pieces=worker_pieces,
                    worker_out=worker_decoded,
                    logs_dir=run / "logs",
                )
        atomic_write_text(
            run / "environment.json",
            scrub_paths(
                json.dumps(
                    {
                        "run_id": run.name,
                        "git_commit": src["commit"],
                        "clean_tree": not bool(src["dirty"]),
                        "environment": env_info,
                    },
                    indent=2,
                    sort_keys=True,
                )
            )
            + "\n",
        )
        report_dir = run / "report"
        report_dir.mkdir(parents=True, exist_ok=True)
        atomic_write_text(
            report_dir / "traindev-eval.json",
            scrub_paths(json.dumps(payload, indent=2, sort_keys=True)) + "\n",
        )
        status = "completed"
    except KeyboardInterrupt:
        status = "aborted"
        raise
    else:
        return payload
    finally:
        shutil.rmtree(scratch, ignore_errors=True)
        settle_w6_slice_b_run(
            downloads,
            res_id,
            status=status,
            actual_pages=69,
            runs_parent=run.parent,
        )


def _build_real_train_rows(
    td_root: VerifiedTrainDevRoot,
    crops_out_dir: Path,
) -> dict[int, list[dict[str, object]]]:
    """Prepare 14.0 px oracle crops and Text Score 2 slices per fold."""
    crops_out_dir.mkdir(parents=True, exist_ok=True)
    manifest = json.loads(
        td_root.read_verified_leaf_bytes("traindev-manifest.json")
    )
    rows_by_fold: dict[int, list[dict[str, object]]] = {f: [] for f in range(4)}
    full_mask = dict.fromkeys(CONTENT_CATEGORIES, True)
    for pm in manifest["pieces"]:
        gid = str(pm["gold_id"])
        fold = _FOLD_BY_GID[gid]
        xml_txt = td_root.read_verified_leaf_bytes(
            f"gold/{gid}/accepted.musicxml"
        ).decode("utf-8")
        v2_full = from_musicxml_text(xml_txt)
        parsed_full = parse_document(v2_full)
        if not isinstance(parsed_full, AssembledScore):
            msg = f"TRAIN_DEV {gid} did not reverse to AssembledScore"
            raise StopConditionError(msg)
        systems_meta = cast("list[dict[str, Any]]", pm["systems"])
        starts = [
            SystemStart(
                ordinal=int(cast("list[int]", s["ordinal_range"])[0]) - 1,
                page=int(s["page_index"]),
                system=int(s["page_system_index"]),
            )
            for s in systems_meta
        ]
        sys_docs = split_score(parsed_full, starts)
        masked_ords = sorted(
            {
                int(entry["measure_ordinal"])
                for entry in cast(
                    "list[dict[str, Any]]",
                    pm.get("open_reading_masks", []),
                )
            }
        )
        prev_doc: SystemDocument | None = None
        for idx, (s_meta, s_doc) in enumerate(
            zip(systems_meta, sys_docs, strict=True)
        ):
            pg_i = int(s_meta["page_index"])
            pg_sys_i = int(s_meta["page_system_index"])
            box = cast(
                "tuple[float, float, float, float]",
                tuple(
                    map(float, cast("list[float]", s_meta["facsimile_box_px"]))
                ),
            )
            sp_px = float(s_meta.get("staff_space_px", 20.0))
            raw_pg = td_root.read_verified_leaf_bytes(
                f"pages/{gid}-p{pg_i:02d}.png"
            )
            with Image.open(io.BytesIO(raw_pg)) as im:
                crop_im = _resample_blind_crop_14px(
                    im.convert("RGB"), box, sp_px
                )
            rel_crop = f"real_crops/{gid}-s{idx + 1:02d}.png"
            dst_crop = crops_out_dir / rel_crop
            dst_crop.parent.mkdir(parents=True, exist_ok=True)
            crop_im.save(dst_crop, format="PNG")
            prompt = build_system_prompt_state((pg_i, pg_sys_i), prev_doc)
            prev_doc = s_doc
            rows_by_fold[fold].append(
                {
                    "sample_id": f"{gid}-s{idx + 1:02d}",
                    "variant_id": gid,
                    "system_index": idx,
                    "expected_systems": len(sys_docs),
                    "is_final_system": idx + 1 == len(sys_docs),
                    "crop_relpath": rel_crop,
                    "crop_width": crop_im.width,
                    "crop_height": crop_im.height,
                    "page_system_tuple": [pg_i, pg_sys_i],
                    "carry_in_text": prompt.carry_in_text,
                    "incoming_header_state_text": (
                        prompt.incoming_header_state_text
                    ),
                    "target_text_v2": write_document(s_doc),
                    "category_supervision_mask": full_mask,
                    "masked_measure_ordinals": masked_ords,
                }
            )
    return rows_by_fold


def run_w6_traindev_replay_and_preregister(  # noqa: C901, PLR0912, PLR0913, PLR0915
    downloads_dir: Path,
    run_dir: Path,
    *,
    traindev_root: Path,
    synth_dataset_dir: Path,
    arm: Literal["arm-a", "arm-b"],
    model_repo: str | None,
    selected_recognizer_checkpoint: Path,
    selected_detector_mode: str,
    selected_detector_checkpoint: Path | None,
    e0_run_ids: Sequence[str],
    synthetic_selection_run_id: str,
    matched_steps: int = 160,
    grad_accum: int = 4,
    lr: float = 5e-5,
    max_new_tokens: int = 1536,
    num_beams: int = 3,
    seed: int = 42,
    formal: bool = True,
    tiny_fixture: bool = False,
    python_override: Path | None = None,
    namespace_prefix: Sequence[str] = ("unshare", "-rn", "--"),
) -> dict[str, object]:
    """Execute 4-fold matched-step replay, calibration, and pre-register."""
    downloads = downloads_dir.absolute()
    run = _validate_slice_b_run_dir(downloads, run_dir)
    ds_dir = synth_dataset_dir.absolute()
    check_outside_repository(ds_dir)
    src = source_state(run_git)
    if formal and src["dirty"]:
        msg = "formal Slice B replay and pre-registration requires a clean tree"
        raise StopConditionError(msg)
    repos_to_check = [model_repo] if model_repo is not None else None
    if not tiny_fixture:
        verify_w6_slice_b_gpu_assets(
            downloads,
            model_repos=repos_to_check,
            require_cuda=True,
            require_rtx4090_wsl2=formal,
        )
    gpu_py = (
        python_override
        if python_override is not None
        else venv_python_path(downloads / "w6-gpu-venv")
    )
    lock = resolve_model_weight_lock(model_repo) if model_repo else None
    w_dir = (
        downloads / "w6-model-weights" / lock.slug if lock is not None else None
    )

    def _metered_train_worker(
        sub_id: str,
        b_cfg: Mapping[str, object],
        work_scratch: Path,
    ) -> None:
        res = reserve_w6_slice_b_run(
            downloads,
            run_id=f"{run.name}:{sub_id}",
            git_commit=str(src["commit"]),
            clean_tree=not bool(src["dirty"]),
            command=f"run-w6-replay:{sub_id}",
            kind="train_gpu",
            runs_parent=run.parent,
        )
        st: Literal["completed", "failed", "aborted"] = "failed"
        try:
            _run_gpu_worker_bounded(
                gpu_py,
                b_cfg,
                scratch=work_scratch,
                timeout_seconds=W6_SLICE_B_TRAIN_TIMEOUT_SECONDS,
                use_unshare=bool(namespace_prefix),
            )
            st = "completed"
        except KeyboardInterrupt:
            st = "aborted"
            raise
        finally:
            settle_w6_slice_b_run(
                downloads, res, status=st, runs_parent=run.parent
            )

    scratch = run / "scratch"
    try:
        with open_verified_traindev_root(
            traindev_root, worktree_roots=_git_worktree_roots()
        ) as td_root:
            real_by_fold = _build_real_train_rows(
                td_root, scratch / "real_data"
            )
            # Train 4 outer continuation + 4 outer replay + 6 inner models,
            # reserving & checking disk/start caps before every worker start!
            outer_ckpts: dict[tuple[str, int], Path] = {}
            for f in range(4):
                real_train = [
                    r for g in range(4) if g != f for r in real_by_fold[g]
                ]
                for branch in (
                    "synthetic_continuation",
                    "real_plus_synth_replay",
                ):
                    b_out = run / "branches" / f"{branch}_f{f}"
                    b_json = scratch / f"{branch}_f{f}.json"
                    b_cfg: dict[str, object] = {
                        "task": "train_replay_branch",
                        "arm": arm,
                        "branch": branch,
                        "matched_steps": matched_steps,
                        "grad_accum": grad_accum,
                        "lr": lr,
                        "seed": seed + f,
                        "synth_dataset_dir": str(ds_dir),
                        "real_train_rows": real_train,
                        "real_crops_root": str(scratch / "real_data"),
                        "checkpoint_dir": str(
                            selected_recognizer_checkpoint.resolve()
                        ),
                        "output_dir": str(b_out),
                        "output_json": str(b_json),
                        "tiny_fixture": tiny_fixture,
                    }
                    if arm == "arm-a" and lock is not None:
                        b_cfg.update(
                            arm_a_family=_REPO_TO_ARM_A_FAMILY[lock.repo_id],
                            weights_dir=(
                                str(w_dir)
                                if (w_dir is not None and w_dir.is_dir())
                                else None
                            ),
                        )
                    _metered_train_worker(
                        f"train_{branch}_f{f}",
                        b_cfg,
                        scratch / f"work_{branch}_{f}",
                    )
                    outer_ckpts[(branch, f)] = b_out / "checkpoint"

            inner_ckpts: dict[frozenset[int], Path] = {}
            pairs = [(a, b) for a in range(4) for b in range(a + 1, 4)]
            for a, b in pairs:
                trained_set = frozenset({0, 1, 2, 3} - {a, b})
                real_inner = [
                    r for k in sorted(trained_set) for r in real_by_fold[k]
                ]
                i_out = run / "branches" / f"inner_hold_{a}_{b}"
                i_json = scratch / f"inner_{a}_{b}.json"
                i_cfg: dict[str, object] = {
                    "task": "train_replay_branch",
                    "arm": arm,
                    "branch": "real_plus_synth_replay",
                    "matched_steps": matched_steps,
                    "grad_accum": grad_accum,
                    "lr": lr,
                    "seed": seed + 100 + a * 4 + b,
                    "synth_dataset_dir": str(ds_dir),
                    "real_train_rows": real_inner,
                    "real_crops_root": str(scratch / "real_data"),
                    "checkpoint_dir": str(
                        selected_recognizer_checkpoint.resolve()
                    ),
                    "output_dir": str(i_out),
                    "output_json": str(i_json),
                    "tiny_fixture": tiny_fixture,
                }
                if arm == "arm-a" and lock is not None:
                    i_cfg.update(
                        arm_a_family=_REPO_TO_ARM_A_FAMILY[lock.repo_id],
                        weights_dir=(
                            str(w_dir)
                            if (w_dir is not None and w_dir.is_dir())
                            else None
                        ),
                    )
                _metered_train_worker(
                    f"train_inner_{a}_{b}",
                    i_cfg,
                    scratch / f"work_inner_{a}_{b}",
                )
                inner_ckpts[trained_set] = i_out / "checkpoint"

            page_items = [
                (
                    Path(rel).stem.split("-p", 1)[0],
                    int(Path(rel).stem.split("-p", 1)[1]),
                    td_root.read_verified_leaf_bytes(rel),
                )
                for rel in td_root.page_relpaths
            ]
            det_ckpt_resolved = (
                selected_detector_checkpoint.resolve()
                if selected_detector_checkpoint is not None
                else None
            )
            if selected_detector_mode != "comb_dp_blind":
                det_res = reserve_w6_slice_b_run(
                    downloads,
                    run_id=f"{run.name}:eval_detector_blind",
                    git_commit=str(src["commit"]),
                    clean_tree=not bool(src["dirty"]),
                    command="run-w6-replay:eval_detector_blind",
                    kind="eval_traindev",
                    runs_parent=run.parent,
                )
                det_st: Literal["completed", "failed", "aborted"] = "failed"
                try:
                    blind_by_gid, worker_pieces, _ = (
                        _extract_blind_pages_from_bytes(
                            page_items,
                            scratch=scratch / "eval_blind",
                            detector_mode=selected_detector_mode,
                            detector_checkpoint=det_ckpt_resolved,
                            gpu_python=gpu_py,
                            namespace_prefix=namespace_prefix,
                        )
                    )
                    det_st = "completed"
                except KeyboardInterrupt:
                    det_st = "aborted"
                    raise
                finally:
                    settle_w6_slice_b_run(
                        downloads,
                        det_res,
                        status=det_st,
                        actual_pages=len(page_items),
                        runs_parent=run.parent,
                    )
            else:
                blind_by_gid, worker_pieces, _ = (
                    _extract_blind_pages_from_bytes(
                        page_items,
                        scratch=scratch / "eval_blind",
                        detector_mode=selected_detector_mode,
                        detector_checkpoint=det_ckpt_resolved,
                        gpu_python=gpu_py,
                        namespace_prefix=namespace_prefix,
                    )
                )
            wp_by_gid = {str(p["gold_id"]): p for p in worker_pieces}

            def _infer_subset(
                ckpt: Path, gids: Sequence[str], tag: str
            ) -> dict[str, Any]:
                padded_pieces = [wp_by_gid[g] for g in gids]
                out_j = scratch / f"pred_{tag}.json"
                j_cfg: dict[str, object] = {
                    "task": "infer_recognizer",
                    "arm": arm,
                    "zero_shot_e0": False,
                    "max_new_tokens": max_new_tokens,
                    "num_beams": num_beams,
                    "pieces": padded_pieces,
                    "output_json": str(out_j),
                    "checkpoint_dir": str(ckpt),
                    "tiny_fixture": tiny_fixture,
                }
                if arm == "arm-a" and lock is not None:
                    j_cfg.update(
                        arm_a_family=_REPO_TO_ARM_A_FAMILY[lock.repo_id],
                        weights_dir=(
                            str(w_dir)
                            if (w_dir is not None and w_dir.is_dir())
                            else None
                        ),
                        lora=True,
                    )
                hb_pages = sum(
                    len(cast("list[int]", wp_by_gid[g]["expected_pages"]))
                    for g in gids
                )
                ev_res = reserve_w6_slice_b_run(
                    downloads,
                    run_id=f"{run.name}:eval_{tag}",
                    git_commit=str(src["commit"]),
                    clean_tree=not bool(src["dirty"]),
                    command=f"run-w6-replay:eval_{tag}",
                    kind="eval_traindev",
                    runs_parent=run.parent,
                )
                ev_st: Literal["completed", "failed", "aborted"] = "failed"
                try:
                    _run_gpu_worker_heartbeat(
                        gpu_py,
                        j_cfg,
                        scratch=scratch / f"hb_{tag}",
                        page_count=hb_pages,
                        namespace_prefix=namespace_prefix,
                    )
                    ev_st = "completed"
                except KeyboardInterrupt:
                    ev_st = "aborted"
                    raise
                finally:
                    settle_w6_slice_b_run(
                        downloads,
                        ev_res,
                        status=ev_st,
                        actual_pages=hb_pages,
                        runs_parent=run.parent,
                    )
                return cast(
                    "dict[str, Any]", json.loads(out_j.read_text("utf-8"))
                )

            # Assemble pooled held-out predictions for continuation vs replay
            cont_pieces_out: dict[str, object] = {}
            repl_pieces_out: dict[str, object] = {}
            for fold_spec in TRAINDEV_FOLDS_4:
                f = fold_spec.fold
                f_gids = list(fold_spec.piece_ids)
                c_res = _infer_subset(
                    outer_ckpts[("synthetic_continuation", f)],
                    f_gids,
                    f"cont_f{f}",
                )
                r_res = _infer_subset(
                    outer_ckpts[("real_plus_synth_replay", f)],
                    f_gids,
                    f"repl_f{f}",
                )
                cont_pieces_out.update(c_res["pieces"])
                repl_pieces_out.update(r_res["pieces"])

            # Inner two-fold-held-out {0,1,2,3}\{f,g} calibration BEFORE scoring
            # repl_eval so calibration metrics, sidecar, and discrepancy log are
            # all computed leak-free from two-fold-held-out models!
            manifest = json.loads(
                td_root.read_verified_leaf_bytes("traindev-manifest.json")
            )
            pm_by_gid = {str(p["gold_id"]): dict(p) for p in manifest["pieces"]}
            oracle_by_gid = {
                gid: _oracle_partition(
                    pm, {int(s["page_index"]) for s in pm["systems"]}
                )
                for gid, pm in pm_by_gid.items()
            }
            l2_set = frozenset(stats.LAYER2_CATEGORIES)

            def _fold_cal_preds(
                decoded_map: Mapping[str, Any], f_idx: int
            ) -> list[CalibrationPrediction]:
                out_p: list[CalibrationPrediction] = []
                for gid in TRAINDEV_FOLDS_4[f_idx].piece_ids:
                    crops_c = cast(
                        "list[dict[str, Any]]", wp_by_gid[gid]["crops"]
                    )
                    coords = [
                        (int(c["page_index"]), int(c["page_system_index"]))
                        for c in crops_c
                    ]
                    sys_r = _reconstruct_system_results(
                        cast(
                            "list[dict[str, object]]",
                            decoded_map[gid]["systems"],
                        )
                    )
                    asm, _ = _system_results_to_crop_assembly(coords, sys_r)
                    boxes_g = {
                        pg: [
                            (sl, (b[0], b[1], b[2], b[3], 2)) for sl, b in plist
                        ]
                        for pg, plist in blind_by_gid.get(gid, {}).items()
                    }
                    allowed, forbidden, _ = partition_oracle_systems(
                        boxes_g, oracle_by_gid[gid], asm
                    )
                    acc_xml = td_root.read_verified_leaf_bytes(
                        f"gold/{gid}/accepted.musicxml"
                    ).decode("utf-8")
                    sc = score_prediction(
                        asm.xml_text,
                        acc_xml,
                        forbidden_ref_ordinals=forbidden,
                        allowed_ref_ordinals_by_cand=allowed,
                    )
                    pm = pm_by_gid[gid]
                    m_masks: dict[int, set[str]] = {
                        m: set() for m in range(1, int(pm["measure_count"]) + 1)
                    }
                    for ent in pm.get("open_reading_masks", []):
                        m_masks[int(ent["measure_ordinal"])].update(
                            str(c) for c in ent["categories"]
                        )
                    for ms in sc.measures:
                        m_o = ms.measure_ordinal
                        c_o = ms.aligned_cand_ordinal
                        rc = (
                            float(asm.pred_measure_raw_conf[c_o - 1])
                            if (
                                sc.parse_valid
                                and c_o is not None
                                and 1 <= c_o <= len(asm.pred_measure_raw_conf)
                            )
                            else 0.0
                        )
                        unm = l2_set - m_masks[m_o]
                        err = bool(
                            m_o in forbidden
                            or (set(ms.layer2_counts.keys()) & unm)
                        )
                        lg = math.log(
                            max(1e-6, min(1.0 - 1e-6, rc))
                            / max(1e-6, 1.0 - min(1.0 - 1e-6, rc))
                        )
                        out_p.append(
                            CalibrationPrediction(
                                score=lg,
                                has_error=err,
                                category="overall",
                                supervised=bool(unm or m_o in forbidden),
                            )
                        )
                return out_p

            inner_preds_map: dict[
                tuple[int, int], InnerValidationPredictions
            ] = {}
            for a in range(4):
                for b in range(a + 1, 4):
                    trained_set = frozenset({0, 1, 2, 3} - {a, b})
                    both_gids = [
                        *TRAINDEV_FOLDS_4[a].piece_ids,
                        *TRAINDEV_FOLDS_4[b].piece_ids,
                    ]
                    dec_ab = _infer_subset(
                        inner_ckpts[trained_set], both_gids, f"inner_{a}_{b}"
                    )["pieces"]
                    # Outer=a uses inner fold b; Outer=b uses inner fold a
                    inner_preds_map[(a, b)] = InnerValidationPredictions(
                        trained_folds=trained_set,
                        predictions=_fold_cal_preds(dec_ab, b),
                    )
                    inner_preds_map[(b, a)] = InnerValidationPredictions(
                        trained_folds=trained_set,
                        predictions=_fold_cal_preds(dec_ab, a),
                    )

            cont_eval = _score_recognizer_on_traindev(
                td_root,
                run_id=f"{run.name}:continuation",
                reader_id="w6-synthetic-continuation",
                evidence_role="conditional_replay_control",
                blind_by_gid=blind_by_gid,
                worker_pieces=worker_pieces,
                worker_out={"pieces": cont_pieces_out},
                logs_dir=run / "logs" / "continuation",
            )
            repl_eval = _score_recognizer_on_traindev(
                td_root,
                run_id=f"{run.name}:replay",
                reader_id="w6-real-plus-synth-replay",
                evidence_role="conditional_replay_treatment",
                blind_by_gid=blind_by_gid,
                worker_pieces=worker_pieces,
                worker_out={"pieces": repl_pieces_out},
                logs_dir=run / "logs" / "replay",
                replay_inner_predictions=inner_preds_map,
            )

            outer_repl_preds = {
                f: _fold_cal_preds(repl_pieces_out, f) for f in range(4)
            }
            # Evaluate selected synthetic-only checkpoint on all 4 folds to fit
            # frozen out-of-fold production calibrator & decide finalist!
            synth_dec = _infer_subset(
                selected_recognizer_checkpoint.resolve(),
                sorted(TRAINDEV_GOLD_IDS),
                "synth_finalist",
            )["pieces"]
            synth_preds_by_fold = {
                f: _fold_cal_preds(synth_dec, f) for f in range(4)
            }
            cont_rate = cast(
                "tuple[float | None, int, int]",
                cont_eval["pooled_layer1_converged"],
            )[0]
            repl_rate = cast(
                "tuple[float | None, int, int]",
                repl_eval["pooled_layer1_converged"],
            )[0]
            replay_improves = (
                repl_rate is not None
                and cont_rate is not None
                and repl_rate + 0.005 < cont_rate
            )
            # If replay improves, train 1 full-TRAIN_DEV production replay
            # checkpoint on all 4 TRAIN_DEV folds + synthetic replay!
            if replay_improves:
                prod_out = run / "branches" / "production_replay"
                prod_json = scratch / "production_replay.json"
                all_real = [r for g in range(4) for r in real_by_fold[g]]
                prod_steps = max(
                    matched_steps,
                    math.ceil(2 * len(all_real) / max(1, grad_accum)),
                )
                prod_cfg: dict[str, object] = {
                    "task": "train_replay_branch",
                    "arm": arm,
                    "branch": "real_plus_synth_replay",
                    "matched_steps": prod_steps,
                    "grad_accum": grad_accum,
                    "lr": lr,
                    "seed": seed + 500,
                    "synth_dataset_dir": str(ds_dir),
                    "real_train_rows": all_real,
                    "real_crops_root": str(scratch / "real_data"),
                    "checkpoint_dir": str(
                        selected_recognizer_checkpoint.resolve()
                    ),
                    "output_dir": str(prod_out),
                    "output_json": str(prod_json),
                    "tiny_fixture": tiny_fixture,
                }
                if arm == "arm-a" and lock is not None:
                    prod_cfg.update(
                        arm_a_family=_REPO_TO_ARM_A_FAMILY[lock.repo_id],
                        weights_dir=(
                            str(w_dir)
                            if (w_dir is not None and w_dir.is_dir())
                            else None
                        ),
                    )
                _metered_train_worker(
                    "train_production_replay",
                    prod_cfg,
                    scratch / "work_production_replay",
                )
                chosen_ckpt = prod_out / "checkpoint"
            else:
                chosen_ckpt = selected_recognizer_checkpoint.resolve()

            # Fit frozen production Platt calibrator on pooled out-of-fold sets
            chosen_fold_preds = (
                outer_repl_preds if replay_improves else synth_preds_by_fold
            )
            all_sup = [
                p
                for f in range(4)
                for p in chosen_fold_preds[f]
                if p.supervised
            ]
            min_cal_support = 10
            prod_cal: Calibrator
            if len(all_sup) >= min_cal_support and min_cal_support <= sum(
                p.has_error for p in all_sup
            ) < len(all_sup):
                platt_ab = stats.fit_monotone_platt(
                    [p.score for p in all_sup],
                    [not p.has_error for p in all_sup],
                )
                prod_cal = Calibrator(
                    method="platt",
                    status="fit",
                    error_instances=sum(p.has_error for p in all_sup),
                    fit_instances=len(all_sup),
                    slope=float(platt_ab[0]),
                    intercept=float(platt_ab[1]),
                )
            else:
                prod_cal = Calibrator(
                    method="platt",
                    status="insufficient_support",
                    error_instances=sum(p.has_error for p in all_sup),
                    fit_instances=len(all_sup),
                )

            report_dir = run / "report"
            report_dir.mkdir(parents=True, exist_ok=True)
            cal_path = report_dir / "production-calibrator.json"
            atomic_write_text(
                cal_path,
                json.dumps(asdict(prod_cal), indent=2, sort_keys=True) + "\n",
            )
            prereg: dict[str, object] = {
                "schema": W6_PREREGISTRATION_SCHEMA,
                "purpose": W6_PILOT_FINAL_PURPOSE,
                "test_touches_before_preregistration": 0,
                "e0_run_ids": list(e0_run_ids),
                "synthetic_selection_run_id": synthetic_selection_run_id,
                "replay_ablation_run_id": run.name,
                "arm": arm,
                "model_repo": lock.repo_id if lock is not None else None,
                "selected_recognizer_checkpoint": str(chosen_ckpt),
                "recognizer_checkpoint_sha256s": compute_artifact_sha256s(
                    chosen_ckpt
                ),
                "selected_regime": (
                    "real_plus_synth_replay"
                    if replay_improves
                    else "synthetic_only"
                ),
                "selected_detector_mode": selected_detector_mode,
                "selected_detector_checkpoint": (
                    str(det_ckpt_resolved)
                    if det_ckpt_resolved is not None
                    else None
                ),
                "detector_checkpoint_sha256s": (
                    compute_artifact_sha256s(det_ckpt_resolved)
                    if det_ckpt_resolved is not None
                    else None
                ),
                "production_calibrator_path": str(cal_path),
                "production_calibrator_sha256": sha256_file(cal_path),
                "max_new_tokens": max_new_tokens,
                "num_beams": num_beams,
            }
            prereg_path = report_dir / "pilot-final-preregistration.json"
            atomic_write_text(
                prereg_path,
                json.dumps(prereg, indent=2, sort_keys=True) + "\n",
            )
            ablation_report: dict[str, object] = {
                "schema": "omr-w6-slice-b-replay-ablation-v1",
                "run_id": run.name,
                "evidence_role": (
                    "conditional_on_upstream_synthetic_finalist_selection"
                ),
                "test_touches": 0,
                "matched_steps": matched_steps,
                "synthetic_continuation": cont_eval,
                "real_plus_synth_replay": repl_eval,
                "replay_two_fold_nested_calibration": cast(
                    "dict[str, object]", repl_eval["nested_calibration"]
                ),
                "replay_selected_over_synthetic": replay_improves,
                "preregistration_path": str(prereg_path),
            }
            atomic_write_text(
                report_dir / "replay-ablation.json",
                scrub_paths(
                    json.dumps(ablation_report, indent=2, sort_keys=True)
                )
                + "\n",
            )
            record_w6_pilot_final_preregistration(
                downloads,
                replay_run_id=run.name,
                preregistration_sha256=sha256_file(prereg_path),
            )
            # Validate pre-registration digest and prerequisite bindings
            _ = verify_pilot_final_preregistration(
                prereg_path,
                downloads_dir=downloads,
                runs_dir=run.parent,
            )
            return ablation_report
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def run_w6_pilot_final_test(  # noqa: C901, PLR0912, PLR0913, PLR0915
    downloads_dir: Path,
    run_dir: Path,
    *,
    preregistration_path: Path,
    w2_s0_run_dir: Path,
    immich_dir: Path,
    desk_study_dir: Path,
    req012_manifest_path: Path,
    gold_cache_dir: Path | None = None,
    formal: bool = True,
    tiny_fixture: bool = False,
    python_override: Path | None = None,
    namespace_prefix: Sequence[str] = ("unshare", "-rn", "--"),
) -> dict[str, object]:
    """Execute at most 1 pre-registered W6-pilot-final evaluation on TEST."""
    downloads = downloads_dir.absolute()
    run = _validate_slice_b_run_dir(downloads, run_dir)
    s0_dir = w2_s0_run_dir.absolute()
    check_outside_repository(
        s0_dir, immich_dir, desk_study_dir, req012_manifest_path
    )
    src = source_state(run_git)
    if formal and src["dirty"]:
        msg = "formal W6-pilot-final TEST pass requires a clean git tree"
        raise StopConditionError(msg)

    # Stat-only existence checks BEFORE reserving or touching TEST!
    t01_reports = desk_study_dir / "omr-finetune-research" / "reports"
    s0_pub_path = s0_dir / "report" / "s0-public.json"
    for req_p in (
        s0_dir / "alignment" / "manifest.json",
        s0_dir / "alignment" / "overlay-qa.json",
        s0_pub_path,
        immich_dir / "batch-status.json",
        req012_manifest_path,
    ):
        if not req_p.is_file():
            msg = f"missing prerequisite before TEST reservation: {req_p.name}"
            raise StopConditionError(msg)
    if not t01_reports.is_dir():
        msg = "missing desk-study T01 reports directory before TEST reservation"
        raise StopConditionError(msg)
    s0_pub_pre = json.loads(s0_pub_path.read_text("utf-8"))
    s0_second_reader = (
        s0_pub_pre.get("splits", {}).get("TEST", {}).get("second_reader")
    )
    if not isinstance(s0_second_reader, dict):
        msg = "Stage S0 public report missing TEST second_reader summary"
        raise StopConditionError(msg)

    # Atomically reserve and fsync Slice B ledger + w2-touch-log.jsonl
    # BEFORE opening any TEST page or MusicXML!
    res_id, prereg, prereg_sha, env_info = reserve_w6_pilot_final_test_pass(
        downloads,
        run_id=run.name,
        git_commit=str(src["commit"]),
        clean_tree=not bool(src["dirty"]),
        command="run-w6-pilot-final",
        preregistration_path=preregistration_path,
        tiny_fixture=tiny_fixture,
        runs_parent=run.parent,
    )
    status: Literal["completed", "failed", "aborted"] = "failed"
    scratch = run / "scratch"
    try:
        arm = cast("Literal['arm-a', 'arm-b']", str(prereg["arm"]))
        model_repo = cast("str | None", prereg.get("model_repo"))
        rec_ckpt = Path(str(prereg["selected_recognizer_checkpoint"]))
        det_mode = str(prereg["selected_detector_mode"])
        det_ckpt_raw = prereg.get("selected_detector_checkpoint")
        det_ckpt = Path(str(det_ckpt_raw)) if det_ckpt_raw else None
        cal_json = cast("dict[str, Any]", prereg["_verified_calibrator"])
        prod_cal = Calibrator(
            method=cast("Literal['platt', 'temperature']", cal_json["method"]),
            status=cast(
                "Literal['fit', 'insufficient_support']", cal_json["status"]
            ),
            error_instances=int(cal_json["error_instances"]),
            fit_instances=int(cal_json["fit_instances"]),
            slope=cal_json.get("slope"),
            intercept=float(cal_json.get("intercept", 0.0)),
        )
        atomic_write_text(
            run / "environment.json",
            scrub_paths(
                json.dumps(
                    {
                        "run_id": run.name,
                        "git_commit": src["commit"],
                        "clean_tree": not bool(src["dirty"]),
                        "environment": env_info,
                    },
                    indent=2,
                    sort_keys=True,
                )
            )
            + "\n",
        )
        gpu_py = (
            python_override
            if python_override is not None
            else venv_python_path(downloads / "w6-gpu-venv")
        )

        manifest, _ = verify_approved_overlays(s0_dir)
        all_pieces: list[dict[str, Any]] = manifest["pieces"]
        test_pieces = [p for p in all_pieces if p["split"] == "TEST"]
        if {str(p["gold_id"]) for p in test_pieces} != _TEST_GOLD_IDS:
            msg = "TEST piece set in manifest differs from frozen 7-piece TEST"
            raise StopConditionError(msg)

        ws_map = {
            int(p["number"]): str(p["workspace"])
            for p in json.loads(
                (immich_dir / "batch-status.json").read_text("utf-8")
            )["pieces"]
        }

        # Resolve relocated accepted_xml_path / initial_draft_path strictly per
        # piece without scanning unrelated private XML files!
        def _resolve_private_xml(
            recorded: str, gid: str, kind: str, target_sha: str
        ) -> Path:
            candidates = [Path(recorded)]
            if gold_cache_dir is not None:
                candidates.append(gold_cache_dir / gid / f"{kind}.musicxml")
            base = downloads.parent
            if kind == "accepted":
                candidates.extend(
                    (
                        base / "gold-cache" / f"{gid}.xml",
                        base / "gold-cache" / f"{gid}.musicxml",
                        base / "gold-cache" / gid / "accepted.musicxml",
                    )
                )
            ws_rel = ws_map.get(int(gid.split("-", 1)[1]), "")
            if ws_rel:
                sub_rels = (
                    ("accepted.musicxml",)
                    if kind == "accepted"
                    else ("build-01/score.musicxml", "draft.musicxml")
                )
                for imm_sub in (
                    immich_dir / "score-transcription-immich-20261031" / ws_rel,
                    immich_dir / ws_rel,
                ):
                    for sub_rel in sub_rels:
                        candidates.append(imm_sub / sub_rel)
            for p_try in candidates:
                if p_try.is_file() and sha256_file(p_try) == target_sha:
                    return p_try
            msg = f"cannot locate SHA-256-verified {kind} XML for {gid}"
            raise StopConditionError(msg)

        accepted_paths: dict[str, Path] = {}
        draft_paths: dict[str, Path] = {}
        page_items: list[tuple[str, int, bytes]] = []
        for pm in sorted(test_pieces, key=lambda x: str(x["gold_id"])):
            gid = str(pm["gold_id"])
            acc_p = _resolve_private_xml(
                str(pm["accepted_xml_path"]),
                gid,
                "accepted",
                str(pm["accepted_xml_sha256"]),
            )
            drf_p = _resolve_private_xml(
                str(pm["initial_draft_path"]),
                gid,
                "draft",
                str(pm["initial_draft_sha256"]),
            )
            if sha256_file(acc_p) != pm["accepted_xml_sha256"]:
                msg = f"{gid} accepted XML SHA-256 mismatch before TEST run"
                raise StopConditionError(msg)
            if sha256_file(drf_p) != pm["initial_draft_sha256"]:
                msg = f"{gid} initial draft SHA-256 mismatch before TEST run"
                raise StopConditionError(msg)
            accepted_paths[gid] = acc_p
            draft_paths[gid] = drf_p
            for pg_rec in pm["pages"]:
                pg_i = int(pg_rec["page_index"])
                pg_file = s0_dir / "pages" / f"{gid}-p{pg_i:02d}.png"
                raw_pg = pg_file.read_bytes()
                if (
                    hashlib.sha256(raw_pg).hexdigest()
                    != pg_rec["page_png_sha256"]
                ):
                    msg = f"{gid}-p{pg_i:02d}.png SHA-256 mismatch"
                    raise StopConditionError(msg)
                page_items.append((gid, pg_i, raw_pg))

        blind_by_gid, worker_pieces, _ = _extract_blind_pages_from_bytes(
            page_items,
            scratch=scratch,
            detector_mode=det_mode,
            detector_checkpoint=det_ckpt,
            gpu_python=gpu_py,
            namespace_prefix=namespace_prefix,
        )
        rec_out = scratch / "pilot_final_preds.json"
        w_cfg: dict[str, object] = {
            "task": "infer_recognizer",
            "arm": arm,
            "zero_shot_e0": False,
            "max_new_tokens": int(prereg["max_new_tokens"]),
            "num_beams": int(prereg["num_beams"]),
            "pieces": worker_pieces,
            "output_json": str(rec_out),
            "checkpoint_dir": str(rec_ckpt),
            "tiny_fixture": tiny_fixture,
        }
        if arm == "arm-a" and model_repo is not None:
            lock = resolve_model_weight_lock(model_repo)
            w_dir = downloads / "w6-model-weights" / lock.slug
            w_cfg.update(
                arm_a_family=_REPO_TO_ARM_A_FAMILY[lock.repo_id],
                weights_dir=str(w_dir) if w_dir.is_dir() else None,
                lora=True,
            )
            reader_id = f"w6-pilot-final-{lock.slug}"
        else:
            reader_id = f"w6-pilot-final-{arm}"

        _run_gpu_worker_heartbeat(
            gpu_py,
            w_cfg,
            scratch=scratch / "rec_work",
            page_count=len(page_items),
            namespace_prefix=namespace_prefix,
        )
        decoded_out = json.loads(rec_out.read_text("utf-8"))["pieces"]

        # Score blind second-reader BEFORE accepted XML comparison
        assemblies: dict[str, SystemCropAssemblyResult] = {}
        sys_results_by_gid: dict[str, list[SystemDecodeResult]] = {}
        coords_by_gid: dict[str, list[tuple[int, int]]] = {}
        crop_boxes_by_gid: dict[
            str, list[tuple[float, float, float, float]]
        ] = {}
        blind_measures: list[stats.BlindDraftMeasure] = []
        blind_lookup: dict[tuple[str, int], stats.BlindDraftMeasure] = {}
        blind_scores: dict[str, PredictionScore] = {}
        indep_draft_counts: dict[str, int] = {}

        for p_cfg in worker_pieces:
            gid = str(p_cfg["gold_id"])
            crops_cfg = cast("list[dict[str, Any]]", p_cfg["crops"])
            coords = [
                (int(c["page_index"]), int(c["page_system_index"]))
                for c in crops_cfg
            ]
            c_boxes = [
                cast("tuple[float, float, float, float]", tuple(c["bbox_px"]))
                for c in crops_cfg
            ]
            sys_r = _reconstruct_system_results(
                cast("list[dict[str, object]]", decoded_out[gid]["systems"])
            )
            asm, _ = _system_results_to_crop_assembly(coords, sys_r)
            assemblies[gid] = asm
            sys_results_by_gid[gid] = sys_r
            coords_by_gid[gid] = coords
            crop_boxes_by_gid[gid] = c_boxes
            # Apply frozen production calibrator BEFORE blind second-reader
            # ranking (zero accepted-XML consultation!)
            cal_pred_confs: list[float] = []
            for rc_val in asm.pred_measure_raw_conf:
                lg_val = math.log(
                    max(1e-6, min(1.0 - 1e-6, rc_val))
                    / max(1e-6, 1.0 - min(1.0 - 1e-6, rc_val))
                )
                pred_c = prod_cal.predict(lg_val)
                cal_pred_confs.append(
                    round(float(pred_c), 6) if pred_c is not None else rc_val
                )
            asm_for_blind = replace(
                asm, pred_measure_raw_conf=tuple(cal_pred_confs)
            )
            d_valid, d_cnt = _inspect_draft_independently(draft_paths[gid])
            indep_draft_counts[gid] = d_cnt
            drf_txt = draft_paths[gid].read_text("utf-8")
            if d_valid:
                b_sc, b_ms = _rank_blind_draft(gid, asm_for_blind, drf_txt)
            else:
                b_sc = score_prediction(None, drf_txt)
                b_ms = [
                    stats.BlindDraftMeasure(gid, m_d, 1, 1.0)
                    for m_d in range(1, d_cnt + 1)
                ]
            blind_scores[gid] = b_sc
            blind_measures.extend(b_ms)
            for bm in b_ms:
                blind_lookup[(gid, bm.draft_ordinal)] = bm

        flags = stats.rank_second_reader_flags(blind_measures)
        f20 = flags["at_20pct"]

        # Oracle-partitioned scoring on TEST
        pm_by_gid = {str(p["gold_id"]): p for p in test_pieces}
        oracle_by_gid = {
            gid: _oracle_partition(
                pm, {int(s["page_index"]) for s in pm["systems"]}
            )
            for gid, pm in pm_by_gid.items()
        }
        cand_scores: dict[str, PredictionScore] = {}
        draft_scores: dict[str, PredictionScore] = {}
        s0_copista_scores: dict[str, PredictionScore] = {}
        cand_tables: dict[str, stats.PieceMeasureTable] = {}
        draft_tables: dict[str, stats.PieceMeasureTable] = {}
        s0_copista_tables: dict[str, stats.PieceMeasureTable] = {}
        raw_conf_by_gm: dict[tuple[str, int], float] = {}
        cal_conf_by_gm: dict[tuple[str, int], float] = {}
        l2_err_by_gm: dict[tuple[str, int], bool] = {}
        matched_reports: list[w5_detectors.PageMatchReport] = []
        l2_set = frozenset(stats.LAYER2_CATEGORIES)
        layer1_all = frozenset(stats.LAYER1_CATEGORIES)

        for gid in sorted(_TEST_GOLD_IDS):
            acc_txt = accepted_paths[gid].read_text("utf-8")
            drf_txt = draft_paths[gid].read_text("utf-8")
            asm = assemblies[gid]
            boxes_g = {
                pg: [(sl, (b[0], b[1], b[2], b[3], 2)) for sl, b in plist]
                for pg, plist in blind_by_gid.get(gid, {}).items()
            }
            allowed, forbidden, p_reps = partition_oracle_systems(
                boxes_g, oracle_by_gid[gid], asm
            )
            matched_reports.extend(p_reps)
            c_sc = score_prediction(
                asm.xml_text,
                acc_txt,
                forbidden_ref_ordinals=forbidden,
                allowed_ref_ordinals_by_cand=allowed,
            )
            d_sc = score_prediction(drf_txt, acc_txt)
            cand_scores[gid] = c_sc
            draft_scores[gid] = d_sc

            # Score frozen Stage S0 copista-28m prediction from w2-s0-formal-3
            s0_cop_meta_p = (
                s0_dir / "preds" / "copista-28m" / f"{gid}.meta.json"
            )
            s0_cop_xml_p = s0_dir / "preds" / "copista-28m" / f"{gid}.musicxml"
            if s0_cop_meta_p.is_file():
                s0_meta = json.loads(s0_cop_meta_p.read_text("utf-8"))
                failed_pgs = {int(x) for x in s0_meta.get("failed_pages", [])}
                s0_forb: set[int] = set()
                for pg_rec in pm_by_gid[gid]["pages"]:
                    if int(pg_rec["page_index"]) in failed_pgs:
                        fo, lo = pg_rec["ordinal_range"]
                        s0_forb.update(range(int(fo), int(lo) + 1))
                s0_xml = (
                    s0_cop_xml_p.read_text("utf-8")
                    if (s0_meta.get("parse_valid") and s0_cop_xml_p.is_file())
                    else None
                )
                s0_copista_scores[gid] = score_prediction(
                    s0_xml, acc_txt, forbidden_ref_ordinals=frozenset(s0_forb)
                )
            else:
                s0_copista_scores[gid] = score_prediction(None, acc_txt)

            pm = pm_by_gid[gid]
            c_tb_raw = _piece_table_from_score(pm, c_sc)
            cand_tables[gid] = replace(
                c_tb_raw,
                layer1_errors=tuple(
                    layer1_all if i in forbidden else errs
                    for i, errs in enumerate(c_tb_raw.layer1_errors, 1)
                ),
                masked_categories=tuple(
                    frozenset() if i in forbidden else masks
                    for i, masks in enumerate(c_tb_raw.masked_categories, 1)
                ),
            )
            draft_tables[gid] = _piece_table_from_score(pm, d_sc)
            s0_copista_tables[gid] = _piece_table_from_score(
                pm, s0_copista_scores[gid]
            )

            m_masks: dict[int, set[str]] = {
                m: set() for m in range(1, int(pm["measure_count"]) + 1)
            }
            for ent in pm.get("open_reading_masks", []):
                m_masks[int(ent["measure_ordinal"])].update(
                    str(c) for c in ent["categories"]
                )
            for ms in c_sc.measures:
                m_o = ms.measure_ordinal
                c_o = ms.aligned_cand_ordinal
                rc = (
                    float(asm.pred_measure_raw_conf[c_o - 1])
                    if (
                        c_sc.parse_valid
                        and c_o is not None
                        and 1 <= c_o <= len(asm.pred_measure_raw_conf)
                    )
                    else 0.0
                )
                raw_conf_by_gm[(gid, m_o)] = rc
                unm = l2_set - m_masks[m_o]
                l2_err_by_gm[(gid, m_o)] = bool(
                    m_o in forbidden or (set(ms.layer2_counts.keys()) & unm)
                )
                lg = math.log(
                    max(1e-6, min(1.0 - 1e-6, rc))
                    / max(1e-6, 1.0 - min(1.0 - 1e-6, rc))
                )
                pred_p = prod_cal.predict(lg)
                cal_conf_by_gm[(gid, m_o)] = (
                    round(float(pred_p), 6)
                    if pred_p is not None
                    else round(rc, 6)
                )

        # Load T01 & REQ-012 archive join for complete omr-discrepancy-log-v1
        measure_counts_all = {
            str(p["gold_id"]): int(p["measure_count"]) for p in all_pieces
        }
        _, req012_post_by_m = _load_and_verify_req012_manifest(
            req012_manifest_path, measure_counts_all, formal=formal
        )
        ws_by_num = {
            int(p["number"]): str(p["workspace"])
            for p in json.loads(
                (immich_dir / "batch-status.json").read_text("utf-8")
            )["pieces"]
        }
        imm_sub = immich_dir / "score-transcription-immich-20261031"
        eff_immich = imm_sub if imm_sub.is_dir() else immich_dir
        t01_join = extract_t01_archive_join(
            t01_reports, eff_immich, ws_by_num, measure_counts_all
        )

        logs_dir = run / "logs"
        logs_dir.mkdir(parents=True, exist_ok=True)
        sidecar_rows: list[dict[str, object]] = []
        for gid in sorted(_TEST_GOLD_IDS):
            b_sc = blind_scores[gid]
            c_sc = cand_scores[gid]
            cand_to_draft = {
                d_ms.aligned_cand_ordinal: d_ms.measure_ordinal
                for d_ms in b_sc.measures
                if d_ms.aligned_cand_ordinal is not None
            }
            cand_to_gold = {
                g_ms.aligned_cand_ordinal: g_ms.measure_ordinal
                for g_ms in c_sc.measures
                if g_ms.aligned_cand_ordinal is not None
            }
            budget_map = {
                (gid, d_o): ((gid, d_o) in f20)
                for d_o in range(1, indep_draft_counts[gid] + 1)
            }
            running_c = 0
            for s_idx, (coords_pair, sys_res, sys_box) in enumerate(
                zip(
                    coords_by_gid[gid],
                    sys_results_by_gid[gid],
                    crop_boxes_by_gid[gid],
                    strict=True,
                )
            ):
                l_to_d: dict[int, int] = {}
                l_to_g: dict[int, int] = {}
                conf_ms: dict[tuple[int, int], SidecarConfidence] = {}
                if sys_res.valid and sys_res.document is not None:
                    for loc_m in range(1, len(sys_res.document.blocks) + 1):
                        gc = running_c + loc_m
                        if gc in cand_to_draft:
                            l_to_d[loc_m] = cand_to_draft[gc]
                        if gc in cand_to_gold:
                            go = cand_to_gold[gc]
                            l_to_g[loc_m] = go
                            cc = cal_conf_by_gm.get((gid, go))
                        else:
                            cc = None
                        for st in (1, 2):
                            conf_ms[(loc_m, st)] = SidecarConfidence(
                                calibrated_confidence=cc
                            )
                    running_c += len(sys_res.document.blocks)
                sidecar_rows.extend(
                    build_w7_sidecar_rows(
                        sys_res,
                        run_id=run.name,
                        sample_or_piece_id=gid,
                        split="TEST",
                        page_index=coords_pair[0],
                        system_index=s_idx,
                        page_system_bbox_px=sys_box,
                        confidences=conf_ms,
                        pred_to_draft_ordinal=l_to_d,
                        pred_to_gold_ordinal=l_to_g,
                        budgeted_draft_flags=budget_map,
                    )
                )
        for s_row in sidecar_rows:
            validate_w7_sidecar_row(s_row)
        atomic_write_text(
            logs_dir / "w7-sidecar.jsonl",
            "".join(
                json.dumps(r, separators=(",", ":")) + "\n"
                for r in sidecar_rows
            ),
        )

        disc_rows = _build_discrepancy_log_rows(
            run_id=run.name,
            copista_reader_id=reader_id,
            pieces_meta=[pm_by_gid[g] for g in sorted(_TEST_GOLD_IDS)],
            draft_scores=draft_scores,
            copista_scores=cand_scores,
            raw_conf_by_gm=raw_conf_by_gm,
            cal_conf_by_gm=cal_conf_by_gm,
            blind_lookup=blind_lookup,
            flags_by_split={"TEST": flags},
            t01_join=t01_join,
            req012_post_by_m=req012_post_by_m,
            candidate_excluded_categories=(),
        )
        for d_row in disc_rows:
            _validate_discrepancy_row(d_row)
        for gid in sorted(_TEST_GOLD_IDS):
            emit_aligned_discrepancy_rows(
                [r for r in disc_rows if r["gold_id"] == gid],
                split="TEST",
                sample_or_piece_id=gid,
            )
        atomic_write_text(
            logs_dir / "omr-discrepancy-log-v1.schema.json",
            json.dumps(DISCREPANCY_SCHEMA, indent=2) + "\n",
        )
        atomic_write_text(
            logs_dir / "discrepancy-log.jsonl",
            "".join(
                json.dumps(r, separators=(",", ":")) + "\n" for r in disc_rows
            ),
        )

        test_gids = sorted(_TEST_GOLD_IDS)
        vs_draft_summary = _summarize_split(
            gids=test_gids,
            pm_by_gid=pm_by_gid,
            d_list=[draft_tables[g] for g in test_gids],
            c_list=[cand_tables[g] for g in test_gids],
            draft_scores=draft_scores,
            copista_scores=cand_scores,
            indep_draft_counts=indep_draft_counts,
            raw_conf_by_gm=raw_conf_by_gm,
            cal_conf_by_gm=cal_conf_by_gm,
            l2_err_by_gm=l2_err_by_gm,
            flags=flags,
            survivor_anchors={
                tuple(x)
                for x in cast(
                    "list[list[Any]]",
                    t01_join.get("test_survivor_anchor_measures", []),
                )
            },
            survivor_spans={
                tuple(x)
                for x in cast(
                    "list[list[Any]]",
                    t01_join.get("test_survivor_span_measures", []),
                )
            },
        )
        vs_copista_boot = stats.cluster_bootstrap_paired(
            [cand_tables[g] for g in test_gids],
            [s0_copista_tables[g] for g in test_gids],
            stats.overall_rate,
        )

        # Verify private XML files were not mutated
        for gid in test_gids:
            if (
                sha256_file(accepted_paths[gid])
                != pm_by_gid[gid]["accepted_xml_sha256"]
            ):
                msg = f"{gid} accepted XML mutated during TEST pass"
                raise StopConditionError(msg)
            if (
                sha256_file(draft_paths[gid])
                != pm_by_gid[gid]["initial_draft_sha256"]
            ):
                msg = f"{gid} draft XML mutated during TEST pass"
                raise StopConditionError(msg)

        # Strip per_piece before emitting public TEST report (OMR-REQ-015)
        vs_draft_public = {
            k: v for k, v in vs_draft_summary.items() if k != "per_piece"
        }

        public_payload = {
            "schema": "omr-w6-pilot-final-test-report-v1",
            "purpose": W6_PILOT_FINAL_PURPOSE,
            "run_id": run.name,
            "git_commit": src["commit"],
            "clean_tree": not bool(src["dirty"]),
            "preregistration_sha256": prereg_sha,
            "reader_id": reader_id,
            "selected_regime": prereg["selected_regime"],
            "selected_detector_mode": det_mode,
            "cumulative_pilot_test_touches": 1,
            "pilot_test_touch_cap": 3,
            "oracle_box_matching": asdict(
                w5_detectors.summarize_page_matches(matched_reports)
            ),
            "test_vs_stage_s0_draft": vs_draft_public,
            "test_vs_stage_s0_copista_overall_layer1": vs_copista_boot,
            "stage_s0_copista_second_reader": s0_second_reader,
            "sidecar_rows": len(sidecar_rows),
            "discrepancy_rows": len(disc_rows),
        }
        sanitized_json = (
            scrub_paths(json.dumps(public_payload, indent=2, sort_keys=True))
            + "\n"
        )
        dyn_forbidden = sorted(
            {
                *(str(ws) for ws in ws_by_num.values() if ws),
                str(Path.home()),
                str(immich_dir),
                str(desk_study_dir),
                str(req012_manifest_path),
            }
        )
        scan(sanitized_json, dyn_forbidden)
        report_dir = run / "report"
        report_dir.mkdir(parents=True, exist_ok=True)
        atomic_write_text(report_dir / "w6-pilot-final.json", sanitized_json)
        status = "completed"
        return json.loads(sanitized_json)
    except KeyboardInterrupt:
        status = "aborted"
        raise
    finally:
        shutil.rmtree(scratch, ignore_errors=True)
        settle_w6_slice_b_run(
            downloads,
            res_id,
            status=status,
            actual_pages=23,
            runs_parent=run.parent,
        )
