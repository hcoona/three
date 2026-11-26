"""Offline blind Copista baseline on the ledger-anchored TRAIN_DEV split."""

from __future__ import annotations

import contextlib
import io
import json
import math
import os
import re
import selectors
import shutil
import signal
import subprocess
import time
import uuid
from collections import defaultdict
from dataclasses import asdict, dataclass, replace
from pathlib import Path
from typing import TYPE_CHECKING, cast

from PIL import Image

from sheet_music_omr.evaluation import copista, stats, w5_detectors, wheel_lock
from sheet_music_omr.evaluation.alignment import (
    atomic_write_text,
    check_no_symlink_under,
    venv_python_path,
)
from sheet_music_omr.evaluation.cli import (
    _evaluate_second_reader_recall,
    _piece_table_from_score,
)
from sheet_music_omr.evaluation.scorer import PredictionScore, score_prediction
from sheet_music_omr.evaluation.xml_merge import (
    SystemCropAssemblyResult,
    concat_system_predictions,
)
from sheet_music_omr.harness.privacy import scrub_paths
from sheet_music_omr.harness.records import run_git, source_state
from sheet_music_omr.harness.stops import StopConditionError
from sheet_music_omr.synthetic.ledgers import WORKER_SCRATCH_HEADROOM_BYTES
from sheet_music_omr.training.asset_locks import TRAINDEV_GOLD_IDS
from sheet_music_omr.training.ledgers import (
    W6_OFFLINE_ENV,
    W6_WORKER_TIMEOUT_SECONDS,
    VerifiedTrainDevRoot,
    _check_w6_disk_cap,
    check_w6_runs_root,
    open_verified_traindev_root,
    reserve_w6_copista_invocation_and_read_anchor,
    settle_w6_run,
    verify_w6_live_assets,
    w6_disk_lock,
)

if TYPE_CHECKING:
    from collections.abc import Mapping, Sequence

    from sheet_music_omr.evaluation.w5_detectors import (
        Box,
        PageMatchReport,
        PredictedBox,
    )

_UNSHARE = ("unshare", "-rn", "--")
_REAP_SECONDS = 5.0
_MAX_HEARTBEAT_LENGTH = 32
_TIMEOUT_RETURN_CODE = 124
_TWO_STAVES = 2
_TRAINDEV_MEASURES = 1601
_TRAINDEV_SYSTEMS = 323
_PAGE_NAME = re.compile(r"pages/(gold-\d{2})-p([0-9]{2})\.png")

_BATCH_WORKER = """
def run_batch(cfg):
    model = read.load_model(cfg["model_path"], device="cpu")
    for index, jobs in enumerate(cfg["pages"]):
        for job in jobs:
            _run_page_with_model(job, model, "cpu")
        os.write(heartbeat_fd, f"{index}\\n".encode("ascii"))

run_batch(json.loads(Path(sys.argv[1]).read_text(encoding="utf-8")))
"""


@dataclass(frozen=True, slots=True)
class WorkerOutcome:
    """Reaped subprocess outcome and completed page prefix."""

    returncode: int
    completed_pages: int


def _kill_worker_group(process: subprocess.Popen[bytes]) -> None:
    if process.poll() is not None:
        return
    with contextlib.suppress(ProcessLookupError, OSError):
        os.killpg(process.pid, signal.SIGKILL)
    with contextlib.suppress(subprocess.TimeoutExpired, OSError):
        process.wait(timeout=_REAP_SECONDS)


def _monitor_worker(
    process: subprocess.Popen[bytes],
    page_count: int,
    page_timeout: float,
) -> WorkerOutcome:
    stdout = process.stdout
    if stdout is None:
        msg = "missing Copista worker heartbeat pipe"
        raise StopConditionError(msg)
    complete = 0
    pending = b""
    deadline = time.monotonic() + page_timeout
    with stdout, selectors.DefaultSelector() as selector:
        selector.register(stdout, selectors.EVENT_READ)
        while complete < page_count:
            wait = deadline - time.monotonic()
            if wait <= 0 or not selector.select(wait):
                _kill_worker_group(process)
                return WorkerOutcome(_TIMEOUT_RETURN_CODE, complete)
            data = os.read(stdout.fileno(), 4096)
            if not data:
                break
            pending += data
            while b"\n" in pending and complete < page_count:
                line, pending = pending.split(b"\n", 1)
                if line not in {
                    f"{complete}".encode(),
                    f"page:{complete}".encode(),
                }:
                    _kill_worker_group(process)
                    msg = "invalid Copista worker heartbeat"
                    raise StopConditionError(msg)
                complete += 1
                deadline = time.monotonic() + page_timeout
            if len(pending) > _MAX_HEARTBEAT_LENGTH:
                _kill_worker_group(process)
                msg = "invalid Copista worker heartbeat length"
                raise StopConditionError(msg)
    try:
        code = process.wait(timeout=_REAP_SECONDS)
    except subprocess.TimeoutExpired:
        _kill_worker_group(process)
        return WorkerOutcome(_TIMEOUT_RETURN_CODE, complete)
    if code < 0 or (code == 0 and complete != page_count):
        msg = "Copista worker exited abnormally before completing pages"
        raise StopConditionError(msg)
    return WorkerOutcome(code, complete)


def run_supervised_worker(  # noqa: PLR0913
    worker_command: Sequence[str],
    *,
    scratch: Path,
    page_count: int,
    page_timeout: float = W6_WORKER_TIMEOUT_SECONDS,
    env: Mapping[str, str] | None = None,
    namespace_prefix: Sequence[str] = _UNSHARE,
    clean_scratch: bool = True,
) -> WorkerOutcome:
    """Run offline worker directly with per-page heartbeat timeout."""
    if page_count < 1 or not 0 < page_timeout <= W6_WORKER_TIMEOUT_SECONDS:
        msg = "Copista requires a positive page count and bounded timeout"
        raise StopConditionError(msg)
    command = [*namespace_prefix, *worker_command]
    try:
        proc = subprocess.Popen(  # noqa: S603
            command,
            cwd=scratch,
            env=dict(env or os.environ) | W6_OFFLINE_ENV,
            close_fds=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.DEVNULL,
            stdin=subprocess.DEVNULL,
            start_new_session=True,
        )
    except OSError as exc:
        msg = "cannot start isolated Copista worker"
        raise StopConditionError(msg) from exc
    try:
        result = _monitor_worker(proc, page_count, page_timeout)
    except BaseException:
        _kill_worker_group(proc)
        raise
    if clean_scratch:
        shutil.rmtree(scratch)
    return result


@dataclass(frozen=True, slots=True)
class OracleSystem:
    """One exact oracle box and its inclusive reference measure partition."""

    box: Box
    first_ordinal: int
    last_ordinal: int

    @property
    def ordinals(self) -> frozenset[int]:
        """Return the inclusive one-based measure range."""
        return frozenset(range(self.first_ordinal, self.last_ordinal + 1))


def partition_oracle_systems(
    blind_by_page: Mapping[int, Sequence[tuple[int, PredictedBox]]],
    oracle_by_page: Mapping[int, Sequence[OracleSystem]],
    prediction: SystemCropAssemblyResult,
) -> tuple[dict[int, frozenset[int]], frozenset[int], list[PageMatchReport]]:
    """Apply W5 exact matching and bind every candidate measure to its crop."""
    surviving = set(prediction.pred_measure_crop_indices)
    allowed_by_crop: dict[int, frozenset[int]] = {}
    forbidden: set[int] = set()
    reports = []
    for page, oracle in oracle_by_page.items():
        blind = blind_by_page.get(page, ())
        counts, pairs = w5_detectors.match_page_boxes_with_pairs(
            [pred for _, pred in blind], [s.box for s in oracle]
        )
        reports.append(counts)
        for i, system in enumerate(oracle):
            if i in pairs:
                crop_id = blind[pairs[i]][0]
                if crop_id in surviving:
                    allowed_by_crop[crop_id] = system.ordinals
                    continue
            forbidden.update(system.ordinals)
    allowed = {
        ordinal: allowed_by_crop.get(crop_id, frozenset())
        for ordinal, crop_id in enumerate(
            prediction.pred_measure_crop_indices, 1
        )
    }
    return allowed, frozenset(forbidden), reports


def score_oracle_partitioned_prediction(
    prediction: SystemCropAssemblyResult,
    reference_xml: str,
    blind_by_page: Mapping[int, Sequence[tuple[int, PredictedBox]]],
    oracle_by_page: Mapping[int, Sequence[OracleSystem]],
) -> PredictionScore:
    """Score only 1:1 crop-to-oracle alignments; penalize every other bar."""
    allowed, forbidden, _ = partition_oracle_systems(
        blind_by_page, oracle_by_page, prediction
    )
    return score_prediction(
        prediction.xml_text,
        reference_xml,
        forbidden_ref_ordinals=forbidden,
        allowed_ref_ordinals_by_cand=allowed,
    )


@dataclass(frozen=True, slots=True)
class _BlindPage:
    gold_id: str
    page_index: int
    boxes: tuple[PredictedBox, ...]
    crops: tuple[Path, ...]


def _freeze_blind_pages(
    root: VerifiedTrainDevRoot,
    scratch: Path,
    runs: Path,
) -> tuple[_BlindPage, ...]:
    pages = []
    for rel in root.page_relpaths:
        match = _PAGE_NAME.fullmatch(rel)
        if match is None:
            msg = "invalid attested TRAIN_DEV page name"
            raise StopConditionError(msg)
        with Image.open(io.BytesIO(root.read_verified_leaf_bytes(rel))) as raw:
            page = raw.convert("L")
        boxes = tuple(w5_detectors.detect_grand_staff_systems_blind(page))
        crops = []
        for i, box in enumerate(boxes, 1):
            x0, y0, x1, y1, staves = box
            if staves != _TWO_STAVES or not (
                0 <= x0 < x1 <= page.width and 0 <= y0 < y1 <= page.height
            ):
                msg = "blind TRAIN_DEV detector returned an invalid crop"
                raise StopConditionError(msg)
            target = scratch / f"p{len(pages):02d}-s{i:02d}.png"
            crop = page.crop(
                (math.floor(x0), math.floor(y0), math.ceil(x1), math.ceil(y1))
            )
            with io.BytesIO() as raw_crop:
                crop.save(raw_crop, format="PNG")
                data = raw_crop.getvalue()
            _check_w6_disk_cap(runs, additional_bytes=len(data))
            target.write_bytes(data)
            crops.append(target)
        pages.append(_BlindPage(match[1], int(match[2]), boxes, tuple(crops)))
    return tuple(pages)


def _prepare_worker(
    downloads: Path,
    scratch: Path,
    pages: Sequence[_BlindPage],
) -> tuple[list[str], dict[str, str]]:
    cache = downloads / "w2-copista-cache"
    root = cache / f"copisteria-{wheel_lock.COPISTERIA_COMMIT}"
    py = venv_python_path(downloads / "w2-copista-venv")
    startup = (
        "import os\n"
        "heartbeat_fd = os.dup(1)\n"
        "with open(os.devnull, 'wb') as sink:\n"
        "    os.dup2(sink.fileno(), 1)\n"
    )
    # Reuse the pinned W2 inference and confidence extraction, not its pool.
    w2_worker = copista._WORKER_SCRIPT  # noqa: SLF001
    shared = w2_worker.split("def _worker_loop(", 1)[0]
    script = scratch / "inference.py"
    script.write_text(startup + shared + _BATCH_WORKER, encoding="utf-8")
    cfg = scratch / "jobs.json"
    cfg.write_text(
        json.dumps(
            {
                "model_path": str(root / "models/evidence-2m.pt"),
                "pages": [
                    [
                        [
                            str(crop),
                            str(
                                scratch
                                / "predictions"
                                / crop.with_suffix(".musicxml").name
                            ),
                            str(
                                scratch
                                / "predictions"
                                / crop.with_suffix(".json").name
                            ),
                            "",
                            "",
                            "",
                            "",
                            "",
                            "0",
                        ]
                        for crop in p.crops
                    ]
                    for p in pages
                ],
            }
        ),
        encoding="utf-8",
    )
    env = (
        os.environ
        | W6_OFFLINE_ENV
        | {
            "OMR_EXPECTED_COPISTERIA_ROOT": str(root),
            "OMP_NUM_THREADS": "1",
            "TMPDIR": str(scratch),
        }
    )
    return [str(py), "-I", "-B", str(script), str(cfg)], env


type BlindBoxes = dict[int, list[tuple[int, PredictedBox]]]


def _read_blind_predictions(
    pages: Sequence[_BlindPage],
    completed: int,
    scratch: Path,
) -> dict[str, tuple[SystemCropAssemblyResult, BlindBoxes]]:
    specs: dict[str, list[tuple[int, int, bytes | None]]] = defaultdict(list)
    boxes: dict[str, BlindBoxes] = defaultdict(dict)
    confs: dict[str, dict[int, list[float]]] = defaultdict(dict)
    for i, page in enumerate(pages):
        gid = page.gold_id
        boxes[gid][page.page_index] = []
        for sys_idx, (box, crop) in enumerate(
            zip(page.boxes, page.crops, strict=True), 1
        ):
            slot = len(specs[gid])
            boxes[gid][page.page_index].append((slot, box))
            xml: bytes | None = None
            pred = scratch / "predictions" / crop.name
            if i < completed:
                try:
                    post = json.loads(pred.with_suffix(".json").read_bytes())
                    scores = [float(m["raw_conf"]) for m in post["measures"]]
                    if scores and all(0 <= score <= 1 for score in scores):
                        xml = pred.with_suffix(".musicxml").read_bytes()
                        confs[gid][slot] = scores
                except OSError, ValueError, KeyError, TypeError:
                    xml = None
            specs[gid].append((page.page_index, sys_idx, xml))
    return {
        gid: (
            concat_system_predictions(specs[gid], crop_raw_confs=confs[gid]),
            boxes[gid],
        )
        for gid in sorted(TRAINDEV_GOLD_IDS)
    }


def _rank_blind_draft(
    gid: str,
    prediction: SystemCropAssemblyResult,
    draft: str,
) -> tuple[PredictionScore, list[stats.BlindDraftMeasure]]:
    score = score_prediction(prediction.xml_text, draft)
    parse_valid = score.parse_valid and not prediction.failed_crops
    return score, [
        stats.BlindDraftMeasure(
            gid,
            measure.measure_ordinal,
            sum(measure.layer2_counts.values()) if parse_valid else 1,
            (
                1.0 - prediction.pred_measure_raw_conf[m - 1]
                if parse_valid
                and (m := measure.aligned_cand_ordinal) is not None
                and m <= len(prediction.pred_measure_raw_conf)
                else 1.0
            ),
        )
        for measure in score.measures
    ]


def _positive_int(value: object) -> int:
    if type(value) is not int or value < 1:
        msg = "invalid TRAIN_DEV manifest ordinal"
        raise StopConditionError(msg)
    return value


def _oracle_partition(
    piece: Mapping[str, object],
    page_indices: set[int],
) -> dict[int, list[OracleSystem]]:
    pages: dict[int, list[OracleSystem]] = defaultdict(list)
    next_measure = 1
    for system in cast("list[dict[str, object]]", piece["systems"]):
        page = _positive_int(system["page_index"])
        if page not in page_indices:
            msg = "oracle references a page outside the attested piece"
            raise StopConditionError(msg)
        lo, hi = map(_positive_int, cast("list[int]", system["ordinal_range"]))
        x0, y0, x1, y1 = map(
            float, cast("list[float]", system["facsimile_box_px"])
        )
        if lo != next_measure or hi < lo or not (0 <= x0 < x1 and 0 <= y0 < y1):
            msg = "invalid TRAIN_DEV oracle system partition"
            raise StopConditionError(msg)
        pages[page].append(OracleSystem((x0, y0, x1, y1), lo, hi))
        next_measure = hi + 1
    if set(pages) != page_indices or next_measure != (
        _positive_int(piece["measure_count"]) + 1
    ):
        msg = "incomplete TRAIN_DEV oracle page/measure coverage"
        raise StopConditionError(msg)
    return dict(pages)


def _read_oracle(
    root: VerifiedTrainDevRoot,
    pages: Sequence[_BlindPage],
) -> dict[str, tuple[dict[str, object], dict[int, list[OracleSystem]]]]:
    try:
        manifest = json.loads(
            root.read_verified_leaf_bytes("traindev-manifest.json")
        )
        pieces: list[dict[str, object]] = manifest["pieces"]
        if (
            len(pieces) != len(TRAINDEV_GOLD_IDS)
            or {piece["gold_id"] for piece in pieces} != TRAINDEV_GOLD_IDS
            or any(
                piece.get("split", "TRAIN_DEV") != "TRAIN_DEV"
                for piece in pieces
            )
        ):
            msg = "oracle manifest is not exclusively the 16 TRAIN_DEV pieces"
            raise StopConditionError(msg)
        result = {}
        systems = measures = 0
        for piece in pieces:
            gid = str(piece["gold_id"])
            page_indices = {
                page.page_index for page in pages if page.gold_id == gid
            }
            partition = _oracle_partition(piece, page_indices)
            systems += sum(map(len, partition.values()))
            measures += _positive_int(piece["measure_count"])
            result[gid] = (piece, partition)
        if (systems, measures) != (_TRAINDEV_SYSTEMS, _TRAINDEV_MEASURES):
            msg = "oracle manifest has drifted TRAIN_DEV totals"
            raise StopConditionError(msg)
    except (KeyError, TypeError, ValueError) as exc:
        msg = "invalid sealed TRAIN_DEV oracle manifest"
        raise StopConditionError(msg) from exc
    return result


def _score_baseline(
    root: VerifiedTrainDevRoot,
    pages: Sequence[_BlindPage],
    predictions: Mapping[str, tuple[SystemCropAssemblyResult, BlindBoxes]],
    outcome: WorkerOutcome,
) -> dict[str, object]:
    drafts = {}
    blind = []
    for gid, (prediction, _) in predictions.items():
        draft = root.read_verified_leaf_bytes(
            f"gold/{gid}/draft.musicxml"
        ).decode()
        score, measures = _rank_blind_draft(gid, prediction, draft)
        drafts[gid] = (draft, score)
        blind.extend(measures)
    flags = stats.rank_second_reader_flags(blind)
    oracle = _read_oracle(root, pages)
    candidate_tables = []
    draft_tables = []
    draft_scores = {}
    draft_counts = {}
    piece_reports = []
    matched_totals = []
    for gid, (prediction, boxes) in predictions.items():
        accepted = root.read_verified_leaf_bytes(
            f"gold/{gid}/accepted.musicxml"
        ).decode()
        meta, systems = oracle[gid]
        meta.setdefault("book_cohort", "TRAIN_DEV")
        allowed, forbidden, reports = partition_oracle_systems(
            boxes, systems, prediction
        )
        candidate = score_prediction(
            prediction.xml_text,
            accepted,
            forbidden_ref_ordinals=forbidden,
            allowed_ref_ordinals_by_cand=allowed,
        )
        draft, blind_score = drafts[gid]
        draft_score = score_prediction(draft, accepted)
        draft_counts[gid] = blind_score.ref_measure_count
        draft_scores[gid] = draft_score
        table = _piece_table_from_score(meta, candidate, l1_converged_only=True)
        # A missing system is an error regardless of the category mask.
        layer1 = frozenset(stats.LAYER1_CATEGORIES)
        candidate_tables.append(
            replace(
                table,
                layer1_errors=tuple(
                    layer1 if i in forbidden else errs
                    for i, errs in enumerate(table.layer1_errors, 1)
                ),
                masked_categories=tuple(
                    frozenset() if i in forbidden else masks
                    for i, masks in enumerate(table.masked_categories, 1)
                ),
            )
        )
        draft_tables.append(
            _piece_table_from_score(meta, draft_score, l1_converged_only=True)
        )
        piece_reports.append(
            {
                "gold_id": gid,
                "parse_valid": candidate.parse_valid,
                "forbidden_ref_measures": len(forbidden),
                "extra_candidate_measures": len(candidate.extra_cand_bars),
                "copista_aligned_d_norm": candidate.d_norm_aligned,
                "copista_whole_d_norm": candidate.d_norm_whole,
                "draft_aligned_d_norm": draft_score.d_norm_aligned,
                "copista_vs_draft_aligned_d_norm": blind_score.d_norm_aligned,
                "failed_crops": len(prediction.failed_crops),
            }
        )
        matched_totals.extend(reports)
    return {
        "schema": "omr-w6-copista-per-system-traindev-v1",
        "split": "TRAIN_DEV",
        "reader": "copista-28m@44464d1",
        "detector": "comb_dp_blind",
        "test_touches": 0,
        "manual_overrides": 0,
        "pieces": len(piece_reports),
        "parsed_pieces": sum(bool(p["parse_valid"]) for p in piece_reports),
        "pages": len(pages),
        "completed_pages": outcome.completed_pages,
        "worker_exit_code": outcome.returncode,
        "worker_timed_out": outcome.returncode == _TIMEOUT_RETURN_CODE,
        "oracle_box_matching": asdict(
            w5_detectors.summarize_page_matches(matched_totals)
        ),
        "blind_second_reader": _evaluate_second_reader_recall(
            gids=list(predictions),
            d_list=draft_tables,
            c_list=candidate_tables,
            draft_scores=draft_scores,
            indep_draft_counts=draft_counts,
            flags=flags,
            survivor_anchors=set(),
            survivor_spans=set(),
        ),
        "copista_a_error": stats.overall_rate(candidate_tables),
        "draft_a_error": stats.overall_rate(draft_tables),
        "per_piece": piece_reports,
    }


def _worktrees() -> tuple[Path, ...]:
    return tuple(
        Path(entry.removeprefix("worktree "))
        for entry in run_git(["worktree", "list", "--porcelain", "-z"]).split(
            "\0"
        )
        if entry.startswith("worktree ")
    )


def _write_baseline_report(
    run: Path,
    runs: Path,
    payload: Mapping[str, object],
    environment: Mapping[str, object],
) -> None:
    report = scrub_paths(json.dumps(payload, indent=2, sort_keys=True))
    markdown = (
        "# Offline per-system Copista TRAIN_DEV baseline\n\n"
        "Blind crops and second-reader flags were frozen before applying the "
        "oracle reference partition. No TEST input was opened.\n\n"
        f"```json\n{report}\n```\n"
    )
    for rel, text in {
        "environment.json": scrub_paths(json.dumps(environment, indent=2))
        + "\n",
        "report/copista-per-system-traindev.json": report + "\n",
        "report/copista-per-system-traindev.md": markdown,
    }.items():
        dest = run / rel
        check_no_symlink_under(dest, run)
        _check_w6_disk_cap(runs, additional_bytes=len(text.encode()))
        atomic_write_text(dest, text)


def _check_write_roots(
    run: Path,
    downloads: Path,
    private: Path,
    worktrees: Sequence[Path],
) -> None:
    if run.is_relative_to(downloads) or downloads.is_relative_to(run):
        msg = "W6 run and download directories must be disjoint"
        raise StopConditionError(msg)
    for path in (run, downloads):
        check_no_symlink_under(path, Path(path.anchor))
        if (
            path.is_relative_to(private)
            or private.is_relative_to(path)
            or any(
                path.is_relative_to(worktree.resolve())
                for worktree in worktrees
            )
        ):
            msg = "W6 write roots must be external and disjoint from TRAIN_DEV"
            raise StopConditionError(msg)


def run_copista_per_system(
    downloads_dir: Path,
    run_dir: Path,
    *,
    traindev_root: Path,
    worktree_roots: Sequence[Path] | None = None,
    formal: bool = True,
) -> dict[str, object]:
    """Reserve before private input and settle every attempted invocation."""
    run, downloads, private = (
        Path(os.path.abspath(p))  # noqa: PTH100 - no private path resolution
        for p in (run_dir, downloads_dir, traindev_root)
    )
    check_w6_runs_root(downloads, run.parent)
    state = source_state()
    if not run.name.startswith("w6-slice-a-") or (formal and state["dirty"]):
        msg = "invalid W6 run directory or dirty formal code tree"
        raise StopConditionError(msg)
    discovered = _worktrees()
    if not discovered:
        msg = "cannot establish git worktree boundaries"
        raise StopConditionError(msg)
    worktrees = tuple(dict.fromkeys((*discovered, *(worktree_roots or ()))))
    _check_write_roots(run, downloads, private, worktrees)
    token, anchor = reserve_w6_copista_invocation_and_read_anchor(
        downloads,
        run_id=run.name,
        git_commit=str(state["commit"]),
        clean_tree=not state["dirty"],
        command="run-copista-per-system",
    )
    status = "failed"
    processed = 0
    scratch = run / f".copista-scratch-{uuid.uuid4().hex}"
    cleanup_safe = True
    try:
        environment = verify_w6_live_assets(downloads) | {
            "git": state,
            "offline_env": W6_OFFLINE_ENV,
            "renderers": {"musescore": "not_used", "verovio": "not_used"},
        }
        with w6_disk_lock(downloads, run.parent) as runs:
            check_no_symlink_under(scratch, run.parent)
            _check_w6_disk_cap(runs)
            with open_verified_traindev_root(
                traindev_root, anchor, worktrees
            ) as root:
                scratch.mkdir(parents=True, mode=0o700)
                pages = _freeze_blind_pages(root, scratch, runs)
                command, env = _prepare_worker(downloads, scratch, pages)
                _check_w6_disk_cap(
                    runs,
                    additional_bytes=WORKER_SCRATCH_HEADROOM_BYTES,
                )
                cleanup_safe = False
                outcome = run_supervised_worker(
                    command,
                    scratch=scratch,
                    page_count=len(pages),
                    env=env,
                    clean_scratch=False,
                )
                cleanup_safe = True
                if outcome.returncode not in {0, _TIMEOUT_RETURN_CODE}:
                    msg = "Copista worker or namespace setup failed"
                    raise StopConditionError(msg)
                processed = outcome.completed_pages
                _check_w6_disk_cap(runs)
                predictions = _read_blind_predictions(pages, processed, scratch)
                payload = _score_baseline(root, pages, predictions, outcome)
            _write_baseline_report(run, runs, payload, environment)
        if outcome.returncode == 0:
            status = "completed"
        return payload
    finally:
        try:
            if cleanup_safe and scratch.exists():
                with w6_disk_lock(downloads, run.parent):
                    shutil.rmtree(scratch)
        finally:
            settle_w6_run(
                downloads, token, status=status, actual_pages=processed
            )
