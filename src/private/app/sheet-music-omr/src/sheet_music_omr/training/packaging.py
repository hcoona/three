"""Composer-disjoint W6 synthetic intake and complete-variant datasets."""

from __future__ import annotations

import contextlib
import hashlib
import io
import json
import math
import multiprocessing
import os
import re
import shutil
import signal
import subprocess
import sys
import threading
import time
import uuid
from collections import Counter, defaultdict
from concurrent.futures import (
    BrokenExecutor,
    ProcessPoolExecutor,
    ThreadPoolExecutor,
)
from concurrent.futures import TimeoutError as FuturesTimeoutError
from dataclasses import dataclass, field, replace
from itertools import batched, islice
from pathlib import Path
from typing import TYPE_CHECKING, cast
from weakref import WeakSet

import numpy as np
from PIL import Image

from sheet_music_omr import model
from sheet_music_omr.assembly import header_state_after
from sheet_music_omr.comparison import CONTENT_CATEGORIES
from sheet_music_omr.evaluation.alignment import (
    atomic_write_text,
    check_no_symlink_under,
    venv_python_path,
)
from sheet_music_omr.harness.downloads import sha256_file
from sheet_music_omr.harness.musescore import (
    preference_values,
    wsl_translate_command,
)
from sheet_music_omr.harness.records import repository_root, source_state
from sheet_music_omr.harness.stops import StopConditionError
from sheet_music_omr.parser import parse_document
from sheet_music_omr.synthetic import degrade, injector, intake, renderers
from sheet_music_omr.synthetic.ledgers import (
    ACCEPTED_MUSESCORE_IDENTITIES,
    RESVG_VERSION,
    RESVG_WHEEL_SHA256,
    VEROVIO_VERSION,
    VEROVIO_WHEEL_SHA256,
    WORKER_SCRATCH_HEADROOM_BYTES,
    verify_w4_w5_live_assets,
)
from sheet_music_omr.timeline import build_timeline
from sheet_music_omr.training.asset_locks import W6_MODEL_METADATA_LOCKS
from sheet_music_omr.training.ledgers import (
    W6_DISK_CAP_BYTES,
    W6_OFFLINE_ENV,
    W6_WORKER_TIMEOUT_SECONDS,
    _check_w6_disk_cap,
    check_w6_runs_root,
    reserve_w6_render_run,
    settle_w6_run,
    verify_w6_training_assets,
    w6_disk_lock,
    w6_scratch_dir_bytes,
)
from sheet_music_omr.writer import _state_lines, write_carry_state

if TYPE_CHECKING:
    from collections.abc import (
        Callable,
        Generator,
        Iterable,
        Iterator,
        Mapping,
        Sequence,
    )
    from concurrent.futures import Future

    from sheet_music_omr.harness.musescore import MuseScore

VEROVIO_DISABLED_CATEGORIES = frozenset(
    {"clef", "cross_staff", "grace", "hairpin", "ottava"}
)
_TRAIN, _VAL = "synth_train", "synth_val"
_SPLITS = (_TRAIN, _VAL)
_SPLIT_SALT = "omr-w6-synth-split-v1:"
_LIEDER_COUNT = 60
_MIN_FORMAL_WORKS = 1500
_FORMAL_VAL_FLOOR = 150
_FIXTURE_VAL_FLOOR = 10
_MILESTONE_MINIMA = (18_000, 2_000, 45_000, 5_000)
_MAX_VARIANT_ROUNDS = 21
_MIN_FORMAL_SOURCES = 2500
_MAX_TOTAL_SOURCES = 5000
_VEROVIO_MAX_DROP_RATE = 0.05
_PLATE = re.compile(r"G[1-7]")
_SAFE_ID = re.compile(r"[A-Za-z0-9][A-Za-z0-9_.-]*")
type Row = dict[str, object]
type _VariantRows = tuple[list[Row], list[Row], dict[Path, bytes]]
type _ProfileResult = tuple[str | None, intake.ProfiledSource | str]
_ORIG_RENDER_VEROVIO = renderers.render_verovio
_ORIG_NORMALIZE_CANDIDATE = intake.normalize_and_profile_candidate
_TERMINATED_POOLS: WeakSet[ProcessPoolExecutor] = WeakSet()


@dataclass(frozen=True, slots=True)
class _WorkerError:
    kind: str
    message: str


def _worker_error(exc: BaseException) -> _WorkerError:
    if isinstance(exc, KeyboardInterrupt | SystemExit):
        raise exc
    try:
        detail = str(exc)
    except Exception:  # noqa: BLE001
        detail = ""
    kind = "value_error" if isinstance(exc, ValueError) else "stop"
    return _WorkerError(kind, detail or type(exc).__name__)


def _to_exception(err: _WorkerError) -> Exception:
    if err.kind == "value_error":
        return ValueError(err.message)
    return StopConditionError(err.message)


def _terminate_pool(
    pool: ProcessPoolExecutor,
    *,
    processes: Sequence[object] | None = None,
) -> bool:
    _TERMINATED_POOLS.add(pool)
    procs = (
        list(processes)
        if processes is not None
        else list((getattr(pool, "_processes", None) or {}).values())
    )
    pool.shutdown(wait=False, cancel_futures=True)
    for proc in procs:
        pid = getattr(proc, "pid", None)
        is_alive = getattr(proc, "is_alive", lambda: False)
        if pid is not None and is_alive():
            with contextlib.suppress(ProcessLookupError, PermissionError):
                os.kill(pid, signal.SIGKILL)
    deadline = time.monotonic() + 5.0
    all_reaped = True
    for proc in procs:
        join = getattr(proc, "join", None)
        if callable(join):
            join(timeout=max(0.0, deadline - time.monotonic()))
        if getattr(proc, "is_alive", lambda: False)():
            all_reaped = False
    return all_reaped


def _shutdown_pool(
    pool: ProcessPoolExecutor, *, clean_idle: bool = False
) -> None:
    if pool in _TERMINATED_POOLS:
        return
    processes = list((getattr(pool, "_processes", None) or {}).values())
    if not clean_idle or sys.exc_info()[0] is not None:
        reaped = _terminate_pool(pool, processes=processes)
        if not reaped and sys.exc_info()[0] is None:
            msg = "W6 worker pool could not be reaped"
            raise StopConditionError(msg)
        return
    pool.shutdown(wait=False, cancel_futures=True)
    deadline = time.monotonic() + 5.0
    for proc in processes:
        proc.join(timeout=max(0.0, deadline - time.monotonic()))
    if any(proc.is_alive() for proc in processes) and not _terminate_pool(
        pool, processes=processes
    ):
        msg = "W6 worker pool could not be reaped"
        raise StopConditionError(msg)


def _await_futures[T](
    pool: ProcessPoolExecutor,
    futures: Sequence[Future[T]],
    *,
    timeout: float = W6_WORKER_TIMEOUT_SECONDS,
    started_at: float | None = None,
) -> list[T]:
    deadline = (
        started_at if started_at is not None else time.monotonic()
    ) + timeout
    results: list[T] = []
    try:
        for future in futures:
            remaining = max(0.0, deadline - time.monotonic())
            results.append(future.result(timeout=remaining))
    except (FuturesTimeoutError, BrokenExecutor) as exc:
        reaped = _terminate_pool(pool)
        suffix = "" if reaped else "; worker could not be reaped"
        msg = f"W6 worker pool exceeded {timeout:g}s deadline or failed{suffix}"
        raise StopConditionError(msg) from exc
    return results


def _sha(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def _field(row: Mapping[str, object], key: str) -> str:
    val = row.get(key)
    if not isinstance(val, str) or not val.strip():
        msg = f"missing Slice A {key}"
        raise StopConditionError(msg)
    return val


def _int(value: object, minimum: int = 0) -> int:
    if type(value) is not int or value < minimum:
        msg = "invalid Slice A integer field"
        raise StopConditionError(msg)
    return value


def _meets_crop_minima(targets: tuple[int, int, int, int]) -> bool:
    return all(
        _int(target, 1) >= minimum
        for target, minimum in zip(targets, _MILESTONE_MINIMA, strict=True)
    )


def validate_w6_packaging_minima(
    *,
    formal: bool,
    max_pdmx: int,
    min_val_works: int,
    crop_targets: tuple[int, int, int, int],
) -> None:
    """Reject lowered requirements before starting a formal Slice A run."""
    if formal and (
        not _meets_crop_minima(crop_targets)
        or _int(min_val_works) < _FORMAL_VAL_FLOOR
        or _int(max_pdmx) + _LIEDER_COUNT < _MIN_FORMAL_SOURCES
    ):
        msg = (
            "formal Slice A milestone minima are 18,000/2,000 prefix crops, "
            "45,000/5,000 full crops, 150 validation works and an intake "
            "limit of at least 2,500 scores"
        )
        raise StopConditionError(msg)


def _work_groups(
    composer: str,
    entries: Sequence[tuple[intake.WorkIdentity, Row]],
) -> list[Row]:
    parents = list(range(len(entries)))

    def find(i: int) -> int:
        while parents[i] != i:
            i = parents[i] = parents[parents[i]]
        return i

    for i, (work, _) in enumerate(entries):
        for j in range(i):
            if not work.matches(entries[j][0], conservative_exclusion=True):
                continue
            ri, rj = find(i), find(j)
            if ri == rj:
                continue
            comp_i = [k for k in range(len(entries)) if find(k) == ri]
            comp_j = [k for k in range(len(entries)) if find(k) == rj]
            if any(
                entries[a][0].conflicts_with(
                    entries[b][0], conservative_exclusion=True
                )
                for a in comp_i
                for b in comp_j
            ):
                continue
            parents[ri] = rj
    groups: dict[int, list[Row]] = defaultdict(list)
    for i, (_, row) in enumerate(entries):
        groups[find(i)].append(row)
    result = []
    for group in groups.values():
        ids = sorted(_field(row, "source_id") for row in group)
        token = _sha(
            json.dumps([composer, ids], separators=(",", ":")).encode()
        )
        result.extend(
            row | {"canonical_composer": composer, "work_group_id": token}
            for row in sorted(group, key=lambda row: _field(row, "source_id"))
        )
    return result


def _split_counts(rows: Sequence[Mapping[str, object]]) -> dict[str, int]:
    return {
        "composers": len({row["canonical_composer"] for row in rows}),
        "works": len({row["work_group_id"] for row in rows}),
        "scores": len(rows),
        "measures": sum(_int(row.get("measure_count", 0)) for row in rows),
    }


def split_admitted_sources_by_composer(
    sources: Sequence[Mapping[str, object]],
    *,
    min_val_works: int = 150,
) -> tuple[list[Row], list[Row], Row]:
    """Keep composers and transitive editions together in a stable split."""
    _int(min_val_works, 1)
    composers: dict[str, list[tuple[intake.WorkIdentity, Row]]] = defaultdict(
        list
    )
    seen: set[str] = set()
    for raw in sources:
        row = dict(raw)
        work = intake.normalize_work_identity(
            _field(row, "composer"), _field(row, "title")
        )
        if work is None:
            msg = "uninformative or contradictory admitted work identity"
            raise StopConditionError(msg)
        source_id = _field(row, "source_id")
        if source_id in seen:
            msg = "duplicate admitted source ID"
            raise StopConditionError(msg)
        seen.add(source_id)
        composers[work.composer_surname].append((work, row))
    buckets = [
        _work_groups(composer, composers[composer])
        for composer in sorted(
            composers,
            key=lambda name: (_sha((_SPLIT_SALT + name).encode()), name),
        )
    ]
    work_counts = [len({row["work_group_id"] for row in b}) for b in buckets]
    total = sum(work_counts)
    if total < _MIN_FORMAL_WORKS and min_val_works < _FIXTURE_VAL_FLOOR:
        msg = "synth_val minimum work quota must be at least 10"
        raise StopConditionError(msg)
    minimum = (
        max(min_val_works, _FORMAL_VAL_FLOOR)
        if (total >= _MIN_FORMAL_WORKS)
        else min_val_works
    )
    quota = max(minimum, math.ceil(total / 10))
    train_only = next(
        (
            i
            for i in reversed(range(len(buckets)))
            if total - work_counts[i] >= quota
        ),
        -1,
    )
    if train_only < 0:
        msg = "composer-disjoint synth_val cannot reach minimum work quota"
        raise StopConditionError(msg)
    train: list[Row] = []
    val: list[Row] = []
    val_works = 0
    for i, (bucket, count) in enumerate(zip(buckets, work_counts, strict=True)):
        if i != train_only and val_works < quota:
            val.extend(row | {"split": _VAL} for row in bucket)
            val_works += count
        else:
            train.extend(row | {"split": _TRAIN} for row in bucket)
    train_counts, val_counts = _split_counts(train), _split_counts(val)
    return (
        train,
        val,
        {
            _TRAIN: train_counts,
            _VAL: val_counts,
            "target_val_works": quota,
            "val_fractions": {
                key: (
                    val_counts[key] / (value + val_counts[key])
                    if value + val_counts[key]
                    else None
                )
                for key, value in train_counts.items()
            },
        },
    )


def choose_engine_for_score(
    score: model.AssembledScore,
    musicxml: str | bytes,
    *,
    preferred_engine: str = "verovio",
    present_categories: Iterable[str] = (),
) -> str:
    """Route native and injected unchecked notation to MuseScore only."""
    category_supervision_mask(preferred_engine)
    xml = musicxml.decode() if isinstance(musicxml, bytes) else musicxml
    root = renderers._parse_xml(xml)  # noqa: SLF001
    checked = renderers._categories(root, xml)  # noqa: SLF001
    if (
        (checked | set(present_categories)) & VEROVIO_DISABLED_CATEGORIES
        or renderers._has_midscore_clef(root)  # noqa: SLF001
        or injector.has_unchecked_verovio_supplementary_facet(score, musicxml)
    ):
        return "musescore"
    return preferred_engine


_ORIG_CHOOSE_ENGINE = choose_engine_for_score


def category_supervision_mask(engine: str) -> dict[str, bool]:
    """Return all 27 W1 category gates for one rendering engine."""
    if engine not in {"verovio", "musescore"}:
        msg = "unknown W6 rendering engine"
        raise StopConditionError(msg)
    return {
        category: (
            engine == "musescore" or category not in VEROVIO_DISABLED_CATEGORIES
        )
        for category in CONTENT_CATEGORIES
    }


def _variant_groups(
    rows: Sequence[Mapping[str, object]],
) -> Iterator[tuple[str, Sequence[Mapping[str, object]]]]:
    seen: set[str] = set()
    offset = 0
    while offset < len(rows):
        first = rows[offset]
        variant = _field(first, "variant_id")
        count = _int(first.get("expected_systems"), 1)
        split = first.get("split")
        batch = rows[offset : offset + count]
        if variant in seen or split not in _SPLITS or len(batch) != count:
            msg = "incomplete or noncontiguous Slice A variant sequence"
            raise StopConditionError(msg)
        for i, row in enumerate(batch):
            if (
                row.get("variant_id") != variant
                or _int(row.get("expected_systems"), 1) != count
                or _int(row.get("system_index")) != i
                or row.get("split") != split
                or row.get("is_final_system") is not (i == count - 1)
                or any(
                    row.get(key) != first.get(key)
                    for key in ("source_id", "work_group_id", "engine", "seed")
                )
            ):
                msg = "incomplete or inconsistent Slice A variant sequence"
                raise StopConditionError(msg)
        yield str(split), batch
        offset += count
        seen.add(variant)


def verify_complete_variant_sequences(
    rows: Sequence[Mapping[str, object]],
) -> None:
    """Reject missing, reordered, duplicated and interleaved variant rows."""
    for _ in _variant_groups(rows):
        pass


def slice_prefix_milestone(
    rows_50k: Sequence[Mapping[str, object]],
    target_train_crops: int = 18_000,
    target_val_crops: int = 2_000,
) -> list[Row]:
    """Take a deterministic prefix per split without truncating variants."""
    targets = {_TRAIN: _int(target_train_crops), _VAL: _int(target_val_crops)}
    counts: Counter[str] = Counter()
    result: list[Row] = []
    for split, batch in _variant_groups(rows_50k):
        if counts[split] < targets[split]:
            counts[split] += len(batch)
            result.extend(dict(row) for row in batch)
    if any(counts[split] < target for split, target in targets.items()):
        msg = "insufficient complete variant sequences for W6 milestone"
        raise StopConditionError(msg)
    return result


@dataclass(frozen=True, slots=True)
class RenderedVariant:
    """A complete renderer-gated variant and its admitted source provenance."""

    variant_id: str
    source: Mapping[str, object]
    seed: int
    plate_group: str
    render: renderers.RenderResult
    prepackaged: _VariantRows | None = None
    _packaging_error: _WorkerError | None = None


@dataclass(frozen=True, slots=True)
class _DegradedPage:
    page_png: bytes
    page_width: int
    page_height: int
    boxes: list[Row]
    crops: tuple[tuple[bytes, int, int], ...]


_INVENTORY_FIELDS = {
    "tuplets": "tuplet",
    "repeats_voltas": "repeat_ending",
    "ties": "tie",
    "slurs": "slur",
    "articulations": "articulation_fermata",
    "fermatas": "articulation_fermata",
    "arpeggios": "arpeggio",
    "ornaments": "ornament",
    "tremolos": "tremolo",
    "cross_staff": "cross_staff",
    "fingering": "fingering",
    "accidentals": "accidental",
    "dynamics": "dynamic",
    "hairpins": "hairpin",
    "words": "text",
    "tempo": "tempo_rehearsal",
    "rehearsal": "tempo_rehearsal",
    "pedal": "pedal",
    "ottavas": "ottava",
    "clef_changes": "clef",
}
_EVENT_CATEGORIES: dict[type, str] = {
    model.ClefChange: "clef",
    model.DashesStart: "text",
    model.DynamicText: "dynamic",
    model.HairpinEnd: "hairpin",
    model.OttavaEnd: "ottava",
    model.TrillEnd: "ornament",
}
_FLAG_CATEGORIES: dict[type, str] = {
    model.Beam: "beam",
    model.Grace: "grace",
    model.SlurRelation: "slur",
    model.TremoloRelation: "tremolo",
}


def _present_categories(doc: model.SystemDocument) -> list[str]:
    counts: Counter[str] = Counter()
    categories = {"voice", "time_signature", "key_signature"}
    if doc.headings:
        categories.add("heading")
    for block in doc.blocks:
        counts.update(intake._measure_counts(block))  # noqa: SLF001
    for placed in build_timeline(doc).events():
        event = placed.event
        intake._event_counts(event, counts)  # noqa: SLF001
        if category := _EVENT_CATEGORIES.get(type(event)):
            categories.add(category)
        if isinstance(event, model.Note | model.Rest | model.MeasureRest):
            categories.add("rhythm_rest")
        if isinstance(event, model.Note | model.Rest):
            categories.update(
                category
                for flag in event.flags
                if (category := _FLAG_CATEGORIES.get(type(flag)))
            )
        if isinstance(event, model.Note):
            categories.add("pitch")
            if len(event.pitches) > 1:
                categories.add("chord_membership")
            if any(pitch.tie_end is not None for pitch in event.pitches):
                categories.add("tie")
    return sorted(
        categories
        | {cat for field, cat in _INVENTORY_FIELDS.items() if counts[field]}
    )


def _png(image: Image.Image) -> bytes:
    with io.BytesIO() as raw:
        image.save(raw, format="PNG")
        return raw.getvalue()


def _boxes(page: degrade.TierRPage) -> list[Row]:
    width, height = page.image.size
    rows: list[Row] = []
    for cls, kind, boxes in (
        (0, "system", page.system_boxes),
        (1, "measure", page.measure_boxes),
    ):
        for x0, y0, x1, y1 in boxes:
            if not (0 <= x0 < x1 <= width and 0 <= y0 < y1 <= height):
                msg = "co-warped W6 box lies outside the page raster"
                raise StopConditionError(msg)
            rows.append(
                {
                    "class_id": cls,
                    "category": kind,
                    "box_px": [x0, y0, x1, y1],
                    "box_normalized": [
                        (x1 + x0) / (2 * width),
                        (y1 + y0) / (2 * height),
                        (x1 - x0) / width,
                        (y1 - y0) / height,
                    ],
                }
            )
    return rows


def _decode_page(page: renderers.RenderedPage) -> Image.Image:
    with Image.open(io.BytesIO(page.png)) as raw:
        return raw.convert("RGB")


def _degrade_page(
    page: renderers.RenderedPage,
    mirror_png: bytes | None,
    seed: int,
    pidx: int,
) -> _DegradedPage:
    geometry = page.geometry
    mirror: Image.Image | None = None
    if mirror_png is not None:
        with Image.open(io.BytesIO(mirror_png)) as raw:
            mirror = raw.convert("RGB")
    tier = degrade.apply_tier_r_degradation(
        _decode_page(page),
        [s.box for s in geometry.systems],
        [m.box for m in geometry.measures],
        mirror_im=mirror,
        seed=_seed(f"{seed}:{pidx}"),
    )
    return _DegradedPage(
        page_png=_png(tier.image),
        page_width=tier.image.width,
        page_height=tier.image.height,
        boxes=_boxes(tier),
        crops=tuple(
            (_png(crop), crop.width, crop.height)
            for crop in degrade.crop_tier_r_systems(tier)
        ),
    )


def _degrade_page_worker(
    page: renderers.RenderedPage,
    mirror_png: bytes | None,
    seed: int,
    pidx: int,
) -> _DegradedPage | _WorkerError:
    try:
        return _degrade_page(page, mirror_png, seed, pidx)
    except BaseException as exc:  # noqa: BLE001
        return _worker_error(exc)


def _variant_rows(
    variant: RenderedVariant,
    *,
    degraded_pages: Sequence[_DegradedPage] | None = None,
) -> _VariantRows:
    render = variant.render
    if (
        render.gate.status != "passed"
        or not render.pages
        or not _SAFE_ID.fullmatch(variant.variant_id)
        or not _PLATE.fullmatch(variant.plate_group)
        or variant.source.get("rights_variant") != "permissive"
        or variant.source.get("split") not in _SPLITS
    ):
        msg = "variant lacks a W6 gate, safe ID, split or permissive rights"
        raise StopConditionError(msg)
    common: Row = {
        key: _field(variant.source, key)
        for key in ("source_id", "work_group_id", "split", "canonical_composer")
    }
    common.update(
        {
            "rights_variant": "permissive",
            "engine": render.engine,
            "seed": variant.seed,
            "variant_id": variant.variant_id,
            "plate_group": variant.plate_group,
        }
    )
    mask = category_supervision_mask(render.engine)
    count = len(render.system_texts)
    if count != sum(len(p.geometry.systems) for p in render.pages):
        msg = "W6 renderer label count differs from rendered systems"
        raise StopConditionError(msg)
    crops: list[Row] = []
    pages: list[Row] = []
    files: dict[Path, bytes] = {}
    prior: model.SystemDocument | None = None
    next_ordinal = 1
    for pidx, page in enumerate(render.pages, 1):
        geometry = page.geometry
        degraded = (
            degraded_pages[pidx - 1]
            if degraded_pages is not None
            else _degrade_page(
                page,
                render.pages[pidx].png if pidx < len(render.pages) else None,
                variant.seed,
                pidx,
            )
        )
        page_id = f"{variant.variant_id}-p{pidx:04d}"
        page_path = Path("assets/pages") / f"{page_id}.png"
        files[page_path] = degraded.page_png
        pages.append(
            common
            | {
                "schema": "omr-w6-page-boxes-v1",
                "page_id": page_id,
                "page_relpath": page_path.as_posix(),
                "page_width_px": degraded.page_width,
                "page_height_px": degraded.page_height,
                "sha256": _sha(files[page_path]),
                "boxes": degraded.boxes,
            }
        )
        for sidx, (system, (crop_png, crop_w, crop_h)) in enumerate(
            zip(geometry.systems, degraded.crops, strict=True),
            1,
        ):
            target = render.system_texts[len(crops)]
            doc = parse_document(target)
            if (
                not isinstance(doc, model.SystemDocument)
                or (doc.page, doc.system) != (pidx, sidx)
                or (system.page, system.system) != (pidx, sidx)
                or system.first_ordinal != next_ordinal
                or system.last_ordinal < next_ordinal
            ):
                msg = "W6 crop/target geometry or measure sequence mismatch"
                raise StopConditionError(msg)
            next_ordinal = system.last_ordinal + 1
            pos = _present_categories(doc)
            if any(not mask[category] for category in pos):
                msg = "renderer emitted a category outside its W6 supervision"
                raise StopConditionError(msg)
            header = ""
            if prior is not None:
                key, meter, clefs = header_state_after(prior)
                if (doc.key, doc.time, doc.clefs) != (key, meter, clefs):
                    msg = "W6 renderer labels disagree at a header boundary"
                    raise StopConditionError(msg)
                header = "\n".join(
                    _state_lines(
                        replace(doc, key=key, time=meter, clefs=clefs)
                    )[:3]
                )
            sample_id = f"{page_id}-s{sidx:03d}"
            crop_path = Path("assets/crops") / f"{sample_id}.png"
            files[crop_path] = crop_png
            crops.append(
                common
                | {
                    "schema": "omr-w6-crop-dataset-v1",
                    "sample_id": sample_id,
                    "system_index": len(crops),
                    "expected_systems": count,
                    "is_final_system": len(crops) == count - 1,
                    "page_id": page_id,
                    "staff_space_px": 14.0,
                    "crop_relpath": crop_path.as_posix(),
                    "crop_width": crop_w,
                    "crop_height": crop_h,
                    "carry_in_text": write_carry_state(doc.carry_in),
                    "incoming_header_state_text": header,
                    "page_system_tuple": (pidx, sidx),
                    "target_text_v2": target,
                    "present_categories": pos,
                    "category_supervision_mask": mask,
                    "source_measure_ordinals": (
                        system.first_ordinal,
                        system.last_ordinal,
                    ),
                    "sha256": _sha(files[crop_path]),
                }
            )
            prior = doc
    if next_ordinal - 1 != _int(variant.source.get("measure_count"), 1):
        msg = "variant does not cover its complete source measure sequence"
        raise StopConditionError(msg)
    return crops, pages, files


def _seed(text: str) -> int:
    return int(_sha(text.encode())[:16], 16)


def _quantiles(values: Iterable[int]) -> dict[str, float] | None:
    samples = list(values)
    if not samples:
        return None
    return dict(
        zip(
            ("p50", "p90", "p99", "max"),
            map(float, np.percentile(samples, [50, 90, 99, 100])),
            strict=True,
        )
    )


_SIZING_PROBE = """
import json
import math
import sys
from tokenizers import Tokenizer

assets = json.loads(sys.argv[1])
inputs = json.load(sys.stdin)
tokens = {}
for key, path in assets["tokenizers"].items():
    tokenizer = Tokenizer.from_file(path)
    tokenizer.no_truncation()
    tokenizer.no_padding()
    tokens[key] = [
        len(enc.ids)
        for i in range(0, len(inputs["texts"]), 256)
        for enc in tokenizer.encode_batch(inputs["texts"][i:i + 256])
    ]

def visual_grids(path, sizes):
    from PIL import Image
    from transformers import AutoImageProcessor

    processor = AutoImageProcessor.from_pretrained(
        path, trust_remote_code=False, local_files_only=True
    )
    merge = getattr(processor, "merge_size", 2)
    result = {}
    fast = getattr(processor, "get_number_of_image_patches", None)
    for w, h in set(map(tuple, sizes)):
        if fast is not None:
            patches = fast(h, w, {})
        else:
            features = processor.preprocess(images=Image.new("RGB", (w, h)))
            patches = math.prod(features["image_grid_" + "t" + "hw"][0])
        patches = int(patches)
        if patches < merge**2 or patches % merge**2:
            raise ValueError("invalid pinned image processor grid")
        result[w, h] = patches // merge**2
    return [result[w, h] for w, h in sizes]

print(json.dumps({
    "tokens": tokens,
    "visual": {
        key: visual_grids(path, inputs["sizes"])
        for key, path in assets["processors"].items()
    },
}))
"""


def _pinned_lengths(
    rows: Sequence[Mapping[str, object]],
    downloads: Path | None,
) -> tuple[dict[str, list[int]], dict[str, list[int]]]:
    if downloads is None:
        return {}, {}
    tokenizers, processors = {}, {}
    for repo in W6_MODEL_METADATA_LOCKS:
        root = downloads / "w6-model-meta" / repo.slug
        token = root / "tokenizer.json"
        preprocessor = root / "preprocessor_config.json"
        load_processor = preprocessor.is_file() and repo.repo_id.startswith(
            ("Qwen/", "Paddle")
        )
        if not (token.is_file() or load_processor):
            continue
        for pinned in repo.files:
            path = root / pinned.filename
            check_no_symlink_under(path, downloads)
            if not path.is_file() or sha256_file(path) != pinned.sha256:
                msg = "cannot measure lengths using drifted W6 metadata"
                raise StopConditionError(msg)
        if token.is_file():
            tokenizers[repo.slug] = str(token)
        if load_processor:
            processors[repo.slug] = str(root)
    if not (tokenizers or processors):
        return {}, {}
    python = venv_python_path(downloads / "w6-train-venv")
    try:
        process = subprocess.run(  # noqa: S603
            [
                str(python),
                "-I",
                "-B",
                "-c",
                _SIZING_PROBE,
                json.dumps(
                    {"tokenizers": tokenizers, "processors": processors}
                ),
            ],
            check=True,
            capture_output=True,
            text=True,
            timeout=W6_WORKER_TIMEOUT_SECONDS,
            cwd=downloads,
            env=os.environ | W6_OFFLINE_ENV,
            input=json.dumps(
                {
                    "texts": [row["target_text_v2"] for row in rows],
                    "sizes": [
                        (r["crop_width"], r["crop_height"]) for r in rows
                    ],
                }
            ),
        )
        lengths = json.loads(process.stdout)
        return lengths["tokens"], lengths["visual"]
    except (OSError, ValueError, subprocess.SubprocessError) as exc:
        msg = "pinned W6 tokenizer/processor length probe failed"
        raise StopConditionError(msg) from exc


def _encoder_grids(row: Mapping[str, object]) -> dict[str, int]:
    h, w = _int(row["crop_height"], 1), _int(row["crop_width"], 1)
    return {
        "convnext_stride8": (h // 8) * (w // 8),
        "convnext_stride16": (h // 16) * (w // 16),
        "monkeyocr_stride14": math.ceil(h / 28) * math.ceil(w / 28) * 4,
    }


def _milestone_stats(
    rows: Sequence[Row],
    source_rows: Sequence[Row],
    token_lengths: Mapping[str, list[int]],
    visual_lengths: Mapping[str, list[int]],
) -> Row:
    selected = {
        _field(row, "sample_id"): idx for idx, row in enumerate(source_rows)
    }
    result: Row = {}
    for split in _SPLITS:
        members = [r for r in rows if r["split"] == split]
        positives: Counter[str] = Counter(dict.fromkeys(CONTENT_CATEGORIES, 0))
        repeat: Counter[tuple[str, int]] = Counter()
        visual: dict[str, list[int]] = defaultdict(list)
        for r in members:
            positives.update(cast("list[str]", r["present_categories"]))
            lo, hi = cast("tuple[int, int]", r["source_measure_ordinals"])
            repeat.update(
                (_field(r, "source_id"), i) for i in range(lo, hi + 1)
            )
            idx = selected[_field(r, "sample_id")]
            grids = _encoder_grids(r) | {
                name: lengths[idx] for name, lengths in visual_lengths.items()
            }
            for model_name, count in grids.items():
                visual[model_name].append(count)
        result[split] = {
            "crops": len(members),
            "source_measures": len(repeat),
            "variants": len({r["variant_id"] for r in members}),
            "scores": len({r["source_id"] for r in members}),
            "works": len({r["work_group_id"] for r in members}),
            "composers": len({r["canonical_composer"] for r in members}),
            "category_positive_counts": dict(positives),
            "source_measure_repetition": _quantiles(repeat.values()),
            "visual_grid_lengths": {
                name: _quantiles(vals) for name, vals in visual.items()
            },
            "target_token_lengths": {
                name: _quantiles(
                    vals[selected[_field(r, "sample_id")]] for r in members
                )
                for name, vals in token_lengths.items()
            },
        }
    return result


def _jsonl(rows: Iterable[Row]) -> str:
    return "".join(json.dumps(row, sort_keys=True) + "\n" for row in rows)


@dataclass(slots=True)
class _DatasetSink:
    root: Path
    usage: int = 0

    def check_headroom(self, additional: int) -> None:
        if self.usage + additional > W6_DISK_CAP_BYTES:
            msg = "Slice A 100 GB generated disk cap would be exceeded"
            raise StopConditionError(msg)

    def write(self, files: Mapping[Path, bytes]) -> None:
        charge = sum(map(len, files.values()))
        self.check_headroom(charge)
        for rel, data in files.items():
            if rel.is_absolute() or ".." in rel.parts:
                msg = "W6 dataset path escapes its staging root"
                raise StopConditionError(msg)
            path = self.root / rel
            check_no_symlink_under(path, self.root.parent)
            path.parent.mkdir(parents=True, exist_ok=True)
            with path.open("xb") as stream:
                stream.write(data)
        self.usage += charge


def package_rendered_variants_to_datasets(  # noqa: PLR0913
    variants: Iterable[RenderedVariant],
    run_dir: Path,
    *,
    downloads_dir: Path | None = None,
    target_train_crops: int = 45_000,
    target_val_crops: int = 5_000,
    prefix_train_crops: int = 18_000,
    prefix_val_crops: int = 2_000,
    validate_before_publish: Callable[[], None] | None = None,
) -> Row:
    """Package verified renderings under fixture milestone names only."""
    return _package_rendered_variants(
        variants,
        run_dir,
        downloads_dir=downloads_dir,
        target_train_crops=target_train_crops,
        target_val_crops=target_val_crops,
        prefix_train_crops=prefix_train_crops,
        prefix_val_crops=prefix_val_crops,
        validate_before_publish=validate_before_publish,
    )


def _package_rendered_variants(  # noqa: PLR0913
    variants: Iterable[RenderedVariant],
    run_dir: Path,
    *,
    downloads_dir: Path | None = None,
    target_train_crops: int = 45_000,
    target_val_crops: int = 5_000,
    prefix_train_crops: int = 18_000,
    prefix_val_crops: int = 2_000,
    _formal_milestones: bool = False,
    validate_before_publish: Callable[[], None] | None = None,
    _meter: _PageMeter | None = None,
) -> Row:
    """Package verified renderings and terminate at complete milestones."""
    root = run_dir.absolute() / "datasets"
    downloads = (downloads_dir or root.parents[2] / "downloads").absolute()
    check_w6_runs_root(downloads, root.parents[1])
    if not root.parent.name.startswith("w6-slice-a-") or root.exists():
        msg = "W6 packaging requires a new dataset under a w6-slice-a-* run"
        raise StopConditionError(msg)
    check_no_symlink_under(root, root.parent.parent)
    counts = _meter.crop_counts if _meter is not None else Counter()
    targets = {
        _TRAIN: _int(target_train_crops, 1),
        _VAL: _int(target_val_crops, 1),
    }
    prefix_targets = {_TRAIN: prefix_train_crops, _VAL: prefix_val_crops}
    sink = _DatasetSink(root.with_name(".datasets-staging"))
    with w6_disk_lock(downloads, root.parents[1]) as runs:
        check_no_symlink_under(sink.root, runs)
        if root.exists() or sink.root.exists():
            msg = "W6 dataset destination is already occupied"
            raise StopConditionError(msg)
        sink.usage = _check_w6_disk_cap(runs)
        if _meter is not None:
            _meter.disk = sink
        result = _package(
            variants,
            sink,
            counts,
            targets,
            prefix_targets=prefix_targets,
            downloads_dir=downloads_dir,
            formal_milestones=_formal_milestones,
            validate_before_publish=validate_before_publish,
        )
        sink.root.rename(root)
        return result


def _package(  # noqa: C901, PLR0913
    variants: Iterable[RenderedVariant],
    sink: _DatasetSink,
    counts: Counter[str],
    targets: Mapping[str, int],
    *,
    prefix_targets: Mapping[str, int],
    downloads_dir: Path | None,
    formal_milestones: bool,
    validate_before_publish: Callable[[], None] | None,
) -> Row:
    rows: list[Row] = []
    page_rows: list[Row] = []
    seen: set[str] = set()
    partitions: dict[tuple[str, str], str] = {}
    for variant in variants:
        split = _field(variant.source, "split")
        if split not in targets or variant.variant_id in seen:
            msg = "invalid split or duplicate W6 variant"
            raise StopConditionError(msg)
        seen.add(variant.variant_id)
        for key in ("canonical_composer", "work_group_id", "source_id"):
            partition = (key, _field(variant.source, key))
            if partitions.setdefault(partition, split) != split:
                msg = "W6 source, work or composer appears in both splits"
                raise StopConditionError(msg)
        if counts[split] >= targets[split]:
            continue
        try:
            if variant._packaging_error is not None:  # noqa: SLF001
                raise _to_exception(variant._packaging_error)  # noqa: SLF001
            crops, pages, files = (
                variant.prepackaged
                if variant.prepackaged is not None
                else _variant_rows(variant)
            )
        except ValueError:
            # Tier R can reject a rendering; do not emit any partial variant.
            continue
        verify_complete_variant_sequences(crops)
        sink.write(files)
        rows.extend(crops)
        page_rows.extend(pages)
        counts[split] += len(crops)
        if all(counts[s] >= targets[s] for s in _SPLITS):
            break
    if validate_before_publish is not None:
        validate_before_publish()
    verify_complete_variant_sequences(rows)
    if any(counts[s] < targets[s] for s in _SPLITS):
        msg = "W6 render budget did not reach both full crop targets"
        raise StopConditionError(msg)
    prefix = slice_prefix_milestone(
        rows, prefix_targets[_TRAIN], prefix_targets[_VAL]
    )
    token_lengths, visual_lengths = _pinned_lengths(rows, downloads_dir)
    result: Row = {}
    names = (
        ("w6-crops-20k", "w6-crops-50k")
        if formal_milestones
        else ("fixture-crops-prefix", "fixture-crops-full")
    )
    for milestone, subset in zip(names, (prefix, rows), strict=True):
        page_ids = {row["page_id"] for row in subset}
        manifests = {}
        for split in _SPLITS:
            directory = Path(milestone) / split
            manifests[directory / "crops.jsonl"] = _jsonl(
                row for row in subset if row["split"] == split
            ).encode()
            manifests[directory / "page-boxes.jsonl"] = _jsonl(
                row
                for row in page_rows
                if row["split"] == split and row["page_id"] in page_ids
            ).encode()
        report = _milestone_stats(subset, rows, token_lengths, visual_lengths)
        result[milestone] = report
        manifests[Path(milestone) / "summary.json"] = (
            json.dumps(report, sort_keys=True, indent=2) + "\n"
        ).encode()
        sink.write(manifests)
    return result


class MetadataOnlyTestExclusion(intake.TestExclusionGate):
    """Load locked hashes and identities without opening any TEST notation."""

    def __init__(self, exclusion_path: Path, status_path: Path) -> None:
        """Bind the frozen digests and non-notation aliases."""
        read_exclusion = intake._verified_exclusion  # noqa: SLF001
        read_status = intake._test_status  # noqa: SLF001
        work_identity = intake._status_identity  # noqa: SLF001
        pairs = read_exclusion(exclusion_path.read_bytes())
        self._digests = frozenset(
            digest for pair in pairs.values() for digest in pair
        )
        identities: set[intake.WorkIdentity] = set()
        for gid, row in read_status(status_path).items():
            number = int(gid.removeprefix("gold-"))
            aliases = {
                ident
                for field in ("source", "workspace")
                if (ident := work_identity(row.get(field), number)) is not None
            }
            if not aliases:
                msg = "missing metadata-only TEST work identity"
                raise StopConditionError(msg)
            identities.update(aliases)
        self._identities = frozenset(identities)

    def verifies_train_dev(
        self,
        gid: str,  # noqa: ARG002
        raw_digest: str,  # noqa: ARG002
    ) -> bool:
        """This intake can only admit permissive sources."""
        return False


def _read_sources(path: Path) -> list[Row]:
    with path.open(encoding="utf-8") as file:
        return [json.loads(line) for line in file]


def _source_score(
    row: Mapping[str, object], root: Path
) -> model.AssembledScore:
    path = root / _field(row, "canonical_v2_path")
    check_no_symlink_under(path, root)
    raw = path.read_bytes()
    if _sha(raw) != _field(row, "canonical_v2_sha256"):
        msg = "admitted W6 source notation digest drift"
        raise StopConditionError(msg)
    score = parse_document(raw.decode())
    if not isinstance(score, model.AssembledScore) or len(score.blocks) != (
        _int(row.get("measure_count"), 1)
    ):
        msg = "invalid admitted W6 source score or measure count"
        raise StopConditionError(msg)
    return score


_ORIG_SOURCE_SCORE = _source_score


def _offline_worker() -> None:
    os.environ.update(W6_OFFLINE_ENV)


def _process_pool(workers: int) -> ProcessPoolExecutor:
    return ProcessPoolExecutor(
        max_workers=workers,
        mp_context=multiprocessing.get_context("forkserver"),
        initializer=_offline_worker,
    )


def _profile_pdmx_worker(
    candidate: intake.SourceCandidate,
    checked: tuple[bytes, dict[str, str], str, str],
) -> _ProfileResult | _WorkerError:
    try:
        return intake._profile_candidate(candidate, checked)  # noqa: SLF001
    except BaseException as exc:  # noqa: BLE001
        return _worker_error(exc)


def _parallel_pdmx_profiles(  # noqa: C901, PLR0912
    downloads: Path,
    gate: MetadataOnlyTestExclusion,
    seen: set[str],
    counts: Counter[str],
    workers: int,
) -> Generator[intake.ProfiledSource | None]:
    candidates = intake.iter_pdmx_candidates(downloads)
    pool = _process_pool(workers)
    in_flight = False
    try:
        while True:
            staged: list[
                str
                | tuple[
                    intake.SourceCandidate,
                    tuple[bytes, dict[str, str], str, str],
                ]
            ] = []
            failure: Exception | None = None
            try:
                for candidate in islice(candidates, workers * 4):
                    try:
                        # Check both digests, rights and aliases before handing
                        # the score to a worker, without sending TEST state.
                        checked = intake._preflight_candidate(  # noqa: SLF001
                            candidate, gate
                        )
                    except intake._CandidateExcludedError as exc:  # noqa: SLF001
                        staged.append(str(exc))
                        continue
                    staged.append(
                        (replace(candidate, raw=b"", decode=None), checked)
                    )
            except Exception as exc:  # noqa: BLE001 - defer speculative archive errors
                failure = exc
            pending = [
                (idx, item)
                for idx, item in enumerate(staged)
                if not isinstance(item, str)
            ]
            resolved: dict[int, _ProfileResult | _WorkerError] = {}
            for wave in batched(pending, workers, strict=False):
                started_at = time.monotonic()
                in_flight = True
                futures = [
                    pool.submit(_profile_pdmx_worker, cand, checked)
                    for _, (cand, checked) in wave
                ]
                wave_out = _await_futures(pool, futures, started_at=started_at)
                in_flight = False
                for (idx, _), out in zip(wave, wave_out, strict=True):
                    resolved[idx] = out
            for idx, item in enumerate(staged):
                counts["eligible_pdmx_candidates_seen"] += 1
                if isinstance(item, str):
                    digest, result = None, item
                else:
                    out = resolved[idx]
                    if isinstance(out, _WorkerError):
                        raise _to_exception(out)
                    digest, result = out
                if digest is not None and digest in seen:
                    counts["duplicate_canonical"] += 1
                elif isinstance(result, str):
                    counts[result] += 1
                else:
                    gate.audit_admitted_manifest((result.manifest_row,))
                    seen.add(_field(result.manifest_row, "canonical_v2_sha256"))
                    counts["admitted"] += 1
                    yield result
                    continue
                yield None
            if failure is not None:
                raise failure
            if len(staged) < workers * 4:
                return
    finally:
        _shutdown_pool(pool, clean_idle=not in_flight)


def _serial_pdmx_profiles(
    downloads: Path,
    gate: MetadataOnlyTestExclusion,
    seen: set[str],
    counts: Counter[str],
) -> Generator[intake.ProfiledSource | None]:
    for candidate in intake.iter_pdmx_candidates(downloads):
        counts["eligible_pdmx_candidates_seen"] += 1
        yield intake.normalize_and_profile_candidate(
            candidate, gate=gate, seen_canonical=seen, counts=counts
        )


def _expanded_intake(  # noqa: PLR0913
    run: Path,
    downloads: Path,
    lieder_manifest: Path,
    gate: MetadataOnlyTestExclusion,
    max_pdmx: int,
    *,
    workers: int = 1,
) -> tuple[list[Row], Row]:
    all_rows = _read_sources(lieder_manifest)
    if any(
        row.get("corpus") not in {"OpenScore Lieder", "PDMX"}
        or row.get("rights_variant") != "permissive"
        for row in all_rows
    ):
        msg = "expected permissive W4a source manifest rows"
        raise StopConditionError(msg)
    rows = [row for row in all_rows if row.get("corpus") == "OpenScore Lieder"]
    if len(rows) != _LIEDER_COUNT or any(
        row.get("license_url") != "CC0-1.0" for row in rows
    ):
        msg = "expected the already-admitted 60 permissive Lieder scores"
        raise StopConditionError(msg)
    gate.audit_admitted_manifest(rows)
    seen = {_field(row, "canonical_v2_sha256") for row in rows}
    if len(seen) != len(rows):
        msg = "duplicate canonical score in reused Lieder manifest"
        raise StopConditionError(msg)
    corpus = run / "synthetic" / "permissive"
    counts: Counter[str] = Counter()
    for row in rows:
        _source_score(row, lieder_manifest.parent)
        row["canonical_root"] = str(lieder_manifest.parent)
    if max_pdmx:
        disk_used = _check_w6_disk_cap(run.parent)
        normalize = intake.normalize_and_profile_candidate
        profiles = (
            _parallel_pdmx_profiles(downloads, gate, seen, counts, workers)
            if workers > 1 and normalize is _ORIG_NORMALIZE_CANDIDATE
            else _serial_pdmx_profiles(downloads, gate, seen, counts)
        )
        try:
            for profile in profiles:
                if profile is None:
                    continue
                disk_used += len(profile.canonical_v2.encode()) + len(
                    profile.musicxml
                )
                if disk_used > W6_DISK_CAP_BYTES:
                    msg = "expanded intake would exceed the Slice A disk cap"
                    raise StopConditionError(msg)
                row = intake._write_source(run, corpus, profile)  # noqa: SLF001
                rows.append(row | {"canonical_root": str(corpus)})
                if counts["admitted"] >= max_pdmx:
                    break
        finally:
            profiles.close()
    gate.audit_admitted_manifest(rows)
    return rows, {
        "reused_lieder": _LIEDER_COUNT,
        "pdmx": dict(counts),
        "pdmx_admission_limit_reached": counts["admitted"] >= max_pdmx,
        "admitted_scores": len(rows),
    }


def _record_intake(
    run: Path,
    runs: Path,
    rows: Sequence[Row],
    funnel: Row,
    *,
    formal: bool,
) -> None:
    if (
        formal
        and len(rows) < _MIN_FORMAL_SOURCES
        and funnel["pdmx_admission_limit_reached"] is True
    ):
        msg = "formal Slice A intake stopped before admitting 2,500 scores"
        raise StopConditionError(msg)
    directory = run / "synthetic" / "permissive"
    outputs = {
        directory / "sources-manifest.jsonl": _jsonl(rows),
        directory / "intake-summary.json": json.dumps(funnel, indent=2) + "\n",
    }
    _check_w6_disk_cap(
        runs,
        additional_bytes=sum(len(s.encode()) for s in outputs.values()),
    )
    for path, content in outputs.items():
        check_no_symlink_under(path, run)
        atomic_write_text(path, content)


@dataclass(slots=True)
class _PageMeter:
    planned: int
    min_verovio_cohort: int = 30
    actual: int = 0
    uncounted_worker: bool = False
    crop_counts: Counter[str] = field(default_factory=Counter)
    disk: _DatasetSink | None = None
    gates: dict[str, renderers.RenderGateLedger] = field(init=False)

    def __post_init__(self) -> None:
        self.gates = {
            "musescore": renderers.RenderGateLedger("musescore"),
            "verovio": renderers.RenderGateLedger(
                "verovio",
                min_cohort_before_disable=self.min_verovio_cohort,
                disabled_categories=set(VEROVIO_DISABLED_CATEGORIES),
            ),
        }

    def add(self, pages: int) -> None:
        self.actual += pages
        if self.actual > self.planned:
            msg = "W6 renderer exceeded its pre-start page reservation"
            raise StopConditionError(msg)

    def check_scratch(self, additional: int) -> _DatasetSink:
        if self.disk is None:
            msg = "W6 renderer started without a disk budget"
            raise StopConditionError(msg)
        self.disk.check_headroom(additional)
        return self.disk

    def gate_summary(self) -> Row:
        return {
            engine: {
                "outcomes": dict(ledger.outcomes),
                "drop_rates": ledger.drop_rates(),
                "disabled_categories": sorted(ledger.disabled_categories),
            }
            for engine, ledger in self.gates.items()
        }

    def validate_verovio_cohort(self, *, formal: bool) -> None:
        gate = self.gates["verovio"]
        evaluated = gate.outcomes["passed"] + gate.outcomes["dropped"]
        if formal and evaluated < gate.min_cohort_before_disable:
            msg = "formal Slice A run did not reach the minimum Verovio cohort"
            raise _VerovioCohortError(msg)
        if not evaluated:
            return
        if any(
            rate > _VEROVIO_MAX_DROP_RATE
            for name, rate in gate.drop_rates().items()
            if name not in VEROVIO_DISABLED_CATEGORIES
        ):
            msg = "unmasked Verovio category exceeded the 5% drop gate"
            raise _VerovioCohortError(msg)


@dataclass(frozen=True, slots=True)
class _VariantPlan:
    row: Row
    seed_text: str
    seed: int
    style: renderers.LayoutStyle
    measures: int


@dataclass(frozen=True, slots=True)
class _PreparedVariant(_VariantPlan):
    engine: str
    xml: str


@dataclass(slots=True)
class _RenderOutcome:
    plan: _VariantPlan
    result: renderers.RenderResult | _WorkerError | Exception | None
    pages: int
    scratch_bytes: int


class _VerovioCohortError(StopConditionError):
    """An unmasked category exceeded the inherited Verovio drop gate."""


def _variant_plan(row: Row, round_index: int) -> _VariantPlan:
    measures = _int(row.get("measure_count"), 1)
    digest = _field(row, "canonical_v2_sha256")
    seed_text = f"omr-w6-variant-v1:{round_index}:{digest}"
    seed = _seed(seed_text)
    return _VariantPlan(
        row,
        seed_text,
        seed,
        renderers.sample_layout_and_style(seed, measures),
        measures,
    )


def _prepare_variant(plan: _VariantPlan, preferred: str) -> _PreparedVariant:
    score = _source_score(plan.row, Path(_field(plan.row, "canonical_root")))
    categories = (
        set(injector._CATEGORIES) - {"N2", "P3"}  # noqa: SLF001
        if preferred == "verovio"
        else None
    )
    injected = injector.inject_annotations(
        score, seed=plan.seed_text, enabled_categories=categories
    )
    engine = choose_engine_for_score(
        injected.score, injected.musicxml, preferred_engine=preferred
    )
    return _PreparedVariant(
        plan.row,
        plan.seed_text,
        plan.seed,
        plan.style,
        plan.measures,
        engine,
        injected.musicxml.decode(),
    )


def _prepare_variant_worker(
    plan: _VariantPlan, preferred: str
) -> _PreparedVariant | _WorkerError:
    try:
        return _prepare_variant(plan, preferred)
    except BaseException as exc:  # noqa: BLE001 - raise in submission order
        return _worker_error(exc)


def _rendered_variant(
    plan: _VariantPlan, result: renderers.RenderResult
) -> RenderedVariant:
    return RenderedVariant(
        _sha(plan.seed_text.encode()),
        plan.row,
        plan.seed,
        plan.style.plate_group,
        result,
    )


def _retained_scratch_bytes(scratch: Path, peak_bytes: int) -> int:
    if not scratch.exists():
        return peak_bytes
    if not any(scratch.iterdir()):
        shutil.rmtree(scratch)
        return peak_bytes
    return max(peak_bytes, w6_scratch_dir_bytes(scratch))


def _execute_prepared_variant(  # noqa: PLR0913
    prepared: _PreparedVariant,
    *,
    run: Path,
    downloads: Path,
    musescore: MuseScore | None,
    calibrated_identity: tuple[str, str] | None,
    musescore_lock: threading.Lock,
    max_pages: int,
    max_scratch_bytes: int | None = None,
) -> _RenderOutcome:
    local_pages = 0
    peak_scratch_bytes = 0
    scratch = run / "tmp" / uuid.uuid4().hex

    def add_pages(count: int) -> None:
        nonlocal local_pages, peak_scratch_bytes
        local_pages += count
        if scratch.exists():
            peak_scratch_bytes = max(
                peak_scratch_bytes, w6_scratch_dir_bytes(scratch)
            )

    ledger = renderers.RenderGateLedger(
        prepared.engine,
        min_cohort_before_disable=10**9,
        disabled_categories=(
            set(VEROVIO_DISABLED_CATEGORIES)
            if prepared.engine == "verovio"
            else set()
        ),
    )
    try:
        if prepared.engine == "musescore":
            if musescore is not None:
                _verify_musescore_output_bound_preconditions(musescore)
            with musescore_lock:
                result = renderers.render_musescore(
                    prepared.xml,
                    prepared.style,
                    cast("MuseScore", musescore),
                    scratch,
                    ledger,
                    rights_variant="permissive",
                    max_pages=max_pages,
                    count_rasters=add_pages,
                    calibrated_identity=calibrated_identity,
                )
        else:
            result = renderers.render_verovio(
                prepared.xml,
                prepared.style,
                downloads,
                scratch,
                ledger,
                max_pages=max_pages,
                count_rasters=add_pages,
                max_scratch_bytes=max_scratch_bytes,
            )
        scratch_bytes = max(
            peak_scratch_bytes,
            w6_scratch_dir_bytes(scratch) if scratch.exists() else 0,
        )
        if scratch.exists():
            shutil.rmtree(scratch)
    except Exception as exc:  # noqa: BLE001 - re-raised after batch accounting
        return _RenderOutcome(
            prepared,
            exc,
            max(local_pages, prepared.measures),
            _retained_scratch_bytes(scratch, peak_scratch_bytes),
        )
    return _RenderOutcome(prepared, result, local_pages, scratch_bytes)


def _stage_verovio_parent_job(
    prepared: _PreparedVariant,
    *,
    run: Path,
    downloads: Path,
    max_pages: int,
    max_scratch_bytes: int,
) -> tuple[
    _VariantPlan,
    renderers.RenderResult | renderers.StagedVerovio | Exception,
    int,
    int,
]:
    local_pages = 0
    peak_scratch_bytes = 0
    scratch = run / "tmp" / uuid.uuid4().hex

    def add_pages(count: int) -> None:
        nonlocal local_pages, peak_scratch_bytes
        local_pages += count
        if scratch.exists():
            peak_scratch_bytes = max(
                peak_scratch_bytes, w6_scratch_dir_bytes(scratch)
            )

    ledger = renderers.RenderGateLedger(
        "verovio",
        min_cohort_before_disable=10**9,
        disabled_categories=set(VEROVIO_DISABLED_CATEGORIES),
    )
    stripped = replace(prepared, xml="")
    try:
        staged = renderers.stage_verovio_subprocess(
            prepared.xml,
            prepared.style,
            downloads,
            scratch,
            ledger,
            max_pages=max_pages,
            count_rasters=add_pages,
            max_scratch_bytes=max_scratch_bytes,
        )
        scratch_bytes = max(
            peak_scratch_bytes,
            w6_scratch_dir_bytes(scratch) if scratch.exists() else 0,
        )
        if scratch.exists():
            shutil.rmtree(scratch)
    except Exception as exc:  # noqa: BLE001 - re-raised after all threads reap
        return (
            stripped,
            exc,
            max(local_pages, prepared.measures),
            _retained_scratch_bytes(scratch, peak_scratch_bytes),
        )
    return stripped, staged, local_pages, scratch_bytes


def _verify_verovio_worker(
    staged: renderers.StagedVerovio,
) -> renderers.RenderResult | _WorkerError:
    ledger = renderers.RenderGateLedger(
        "verovio",
        min_cohort_before_disable=10**9,
        disabled_categories=set(VEROVIO_DISABLED_CATEGORIES),
    )
    try:
        return renderers.verify_verovio_outputs(staged, ledger)
    except BaseException as exc:  # noqa: BLE001
        return _worker_error(exc)


def _prepackage_variants_parallel(
    pool: ProcessPoolExecutor,
    workers: int,
    variants: Sequence[RenderedVariant],
) -> list[RenderedVariant]:
    page_tasks: list[
        tuple[int, int, renderers.RenderedPage, bytes | None, int]
    ] = []
    for v_idx, variant in enumerate(variants):
        pages = variant.render.pages
        for pidx, page in enumerate(pages, 1):
            mirror_png = pages[pidx].png if pidx < len(pages) else None
            page_tasks.append((v_idx, pidx, page, mirror_png, variant.seed))
    degraded_by_variant: dict[int, list[_DegradedPage]] = defaultdict(list)
    errors_by_variant: dict[int, _WorkerError] = {}
    for wave in batched(page_tasks, workers, strict=False):
        started_at = time.monotonic()
        futures = [
            pool.submit(_degrade_page_worker, page, mirror_png, seed, pidx)
            for _, pidx, page, mirror_png, seed in wave
        ]
        for (v_idx, *_), res in zip(
            wave,
            _await_futures(pool, futures, started_at=started_at),
            strict=True,
        ):
            if isinstance(res, _WorkerError):
                errors_by_variant.setdefault(v_idx, res)
            else:
                degraded_by_variant[v_idx].append(res)
    packaged: list[RenderedVariant] = []
    for v_idx, variant in enumerate(variants):
        metadata = replace(variant.render, pages=(), system_texts=())
        if err := errors_by_variant.get(v_idx):
            packaged.append(
                replace(variant, render=metadata, _packaging_error=err)
            )
            continue
        try:
            rows = _variant_rows(
                variant, degraded_pages=degraded_by_variant[v_idx]
            )
        except BaseException as exc:  # noqa: BLE001 - defer in variant order
            packaged.append(
                replace(
                    variant,
                    render=metadata,
                    _packaging_error=_worker_error(exc),
                )
            )
        else:
            packaged.append(replace(variant, render=metadata, prepackaged=rows))
    return packaged


_MAX_MUSESCORE_REPEATS = 10


def _verify_musescore_output_bound_preconditions(musescore: MuseScore) -> None:
    """Require substantiated 4.7.6/4.7.7 binary and disabled jump inference."""
    if (
        musescore.version,
        musescore.executable_sha256,
    ) not in ACCEPTED_MUSESCORE_IDENTITIES:
        msg = "MuseScore identity needs accepted mutable-source recheck"
        raise StopConditionError(msg)
    prefs = preference_values(musescore.baseline_preferences)
    if prefs.get("import/musicXml/importInferTextType", "").lower() != "false":
        msg = "W6 MuseScore output bound requires importInferTextType=false"
        raise StopConditionError(msg)


def _musescore_worst_case_bytes(measures: int, xml: str) -> int:
    """Return proven upper bound for one US-Letter 300-DPI MuseScore batch job.

    1. Canvas (renderers.py:137-138): US Letter 8.5x11.0 in at -r 300 DPI ->
       2550x3300 32-bit RGBA = 33,663,300 filtered scanline bytes -> RFC-1951
       uncompressed stored-block PNG format ceiling < 35 MB < 64 MiB/page.
    2. Pagination (createMultiMeasureRests=0, single 2-staff piano part):
       MuseScore never subdivides one <measure> element across systems and
       places >= 1 system/page, so pages <= max(measures, <measure> count) + 1.
    3. Sidecars (musescore positionswriter.cpp repeatList(expandRepeats=true)):
       rejecting > 10 <repeat tags bounds playback passes to <= 2**R; each
       segment (<element> < 256 B) and unrolled playback chord (<event> < 256 B)
       plus un-unrolled MusicXML re-export fits within the formula below.
    """
    repeat_tags = xml.count("<repeat")
    if repeat_tags > _MAX_MUSESCORE_REPEATS:
        msg = "MuseScore job exceeds bounded repeat-expansion limit"
        raise StopConditionError(msg)
    measure_elems = max(measures, xml.count("<measure"))
    max_job_pages = measure_elems + 1
    segments = (
        xml.count("<note")
        + xml.count("<backup")
        + xml.count("<forward")
        + measure_elems
        + 16
    )
    passes = 1 << repeat_tags
    png_ceiling = WORKER_SCRATCH_HEADROOM_BYTES * max_job_pages
    sidecar_ceiling = 512 * segments * (1 + passes) + 8 * 1024 * 1024
    xml_ceiling = (
        16 * len(xml.encode("utf-8")) + 4096 * segments + 8 * 1024 * 1024
    )
    return png_ceiling + sidecar_ceiling + xml_ceiling


def _verovio_worst_case_bytes(plan: _PreparedVariant) -> int:
    """Return hard-enforced scratch ceiling for one Verovio subprocess job."""
    est_pages = max(
        1,
        math.ceil(len(plan.style.layout.starts) / plan.style.systems_per_page),
    )
    return (
        WORKER_SCRATCH_HEADROOM_BYTES * est_pages
        + 16 * len(plan.xml.encode("utf-8"))
        + 16 * 1024 * 1024
    )


def _item_worst_case_bytes(item: _PreparedVariant) -> int:
    if item.engine == "musescore":
        return _musescore_worst_case_bytes(item.measures, item.xml)
    return _verovio_worst_case_bytes(item)


def _render_variants(  # noqa: C901, PLR0912, PLR0913, PLR0915
    rows: Sequence[Row],
    *,
    run: Path,
    downloads: Path,
    musescore: MuseScore,
    meter: _PageMeter,
    calibrated_identity: tuple[str, str] | None,
    targets: Mapping[str, int],
    verovio_workers: int = 1,
    musescore_page_cap: int | None = None,
) -> Iterator[RenderedVariant]:
    workers = _int(verovio_workers, 1)
    if musescore_page_cap is not None:
        musescore_page_cap = _int(musescore_page_cap, 0)
    musescore_pages = 0
    musescore_lock = threading.Lock()
    batch: list[_PreparedVariant] = []
    reserved_measures = 0
    reserved_scratch_bytes = 0
    pending_ms_measures = 0
    attempted = False
    process_pool = (
        _process_pool(workers)
        if workers > 1
        and renderers.render_verovio is _ORIG_RENDER_VEROVIO
        and choose_engine_for_score is _ORIG_CHOOSE_ENGINE
        and _source_score is _ORIG_SOURCE_SCORE
        else None
    )
    thread_pool = (
        ThreadPoolExecutor(max_workers=workers) if workers > 1 else None
    )
    preparations: dict[
        int,
        tuple[
            str,
            float,
            Future[_PreparedVariant | _WorkerError]
            | _PreparedVariant
            | _WorkerError,
        ],
    ] = {}
    discarded: list[tuple[float, Future[_PreparedVariant | _WorkerError]]] = []

    def preferred_engine(plan: _VariantPlan) -> str:
        return (
            "musescore"
            if plan.seed % 2 == 0 and permits_musescore(plan.measures)
            else "verovio"
        )

    def permits_musescore(measures: int) -> bool:
        return musescore_page_cap is None or (
            musescore_pages + pending_ms_measures + measures
            <= musescore_page_cap
        )

    def discard_preparation(index: int) -> None:
        popped = preparations.pop(index, None)
        if popped is None:
            return
        _, started_at, slot = popped
        if not isinstance(slot, _PreparedVariant | _WorkerError):
            slot.cancel()
            if not slot.done():
                discarded.append((started_at, slot))

    def drain_preparations() -> None:
        if process_pool is None:
            return
        for index, (eng, started_at, entry) in list(preparations.items()):
            if not isinstance(entry, _PreparedVariant | _WorkerError):
                (resolved,) = _await_futures(
                    process_pool, (entry,), started_at=started_at
                )
                preparations[index] = (eng, started_at, resolved)
        for started_at, fut in list(discarded):
            if not fut.done():
                _await_futures(process_pool, (fut,), started_at=started_at)
        discarded.clear()

    def prepare(
        plan: _VariantPlan, idx: int, round_index: int
    ) -> _PreparedVariant:
        preferred = preferred_engine(plan)
        if process_pool is None:
            return _prepare_variant(plan, preferred)
        cached = preparations.get(idx)
        if cached is None or cached[0] != preferred:
            # Keep at most `workers` active lookahead jobs so each starts
            # immediately without queue delay.
            for index in range(idx, min(idx + workers, len(rows))):
                row = rows[index]
                try:
                    split = _field(row, "split")
                    if meter.crop_counts[split] >= targets[split]:
                        continue
                    upcoming = (
                        plan
                        if index == idx
                        else _variant_plan(row, round_index)
                    )
                except KeyError, StopConditionError:
                    if index == idx:
                        raise
                    # Check invalid rows only if the schedule reaches them.
                    break
                engine = preferred_engine(upcoming)
                entry = preparations.get(index)
                if entry is None or entry[0] != engine:
                    preparations[index] = (
                        engine,
                        time.monotonic(),
                        process_pool.submit(
                            _prepare_variant_worker, upcoming, engine
                        ),
                    )
        eng, started_at, slot = preparations[idx]
        if not isinstance(slot, _PreparedVariant | _WorkerError):
            (slot,) = _await_futures(
                process_pool, (slot,), started_at=started_at
            )
            preparations[idx] = (eng, started_at, slot)
        if isinstance(slot, _WorkerError):
            raise _to_exception(slot)
        return slot

    def dispatch(
        items: Sequence[_PreparedVariant],
        max_pages: int,
        *,
        is_musescore: bool,
    ) -> list[_RenderOutcome]:
        if process_pool is not None and not is_musescore:
            # Run unshare Verovio subprocesses on parent threads under the disk
            # lock owner so every renderer is reaped before lock release, then
            # run pure-Python SVG/MEI verification in forkserver workers.
            def stage_one(
                item: _PreparedVariant,
            ) -> tuple[
                _VariantPlan,
                renderers.RenderResult | renderers.StagedVerovio | Exception,
                int,
                int,
            ]:
                return _stage_verovio_parent_job(
                    item,
                    run=run,
                    downloads=downloads,
                    max_pages=min(item.measures, max_pages),
                    max_scratch_bytes=_verovio_worst_case_bytes(item),
                )

            staged_rows = (
                [
                    job.result()
                    for job in [
                        thread_pool.submit(stage_one, it) for it in items
                    ]
                ]
                if thread_pool is not None and len(items) > 1
                else [stage_one(it) for it in items]
            )
            meter.add(sum(pages for _, _, pages, _ in staged_rows))
            meter.uncounted_worker = False
            verify_indices = [
                i
                for i, (_, st, _, _) in enumerate(staged_rows)
                if isinstance(st, renderers.StagedVerovio)
            ]
            verified: dict[int, renderers.RenderResult | _WorkerError] = {}
            if verify_indices:
                started_at = time.monotonic()
                verify_jobs = [
                    process_pool.submit(
                        _verify_verovio_worker,
                        cast("renderers.StagedVerovio", staged_rows[i][1]),
                    )
                    for i in verify_indices
                ]
                for i, res in zip(
                    verify_indices,
                    _await_futures(
                        process_pool, verify_jobs, started_at=started_at
                    ),
                    strict=True,
                ):
                    verified[i] = res
            return [
                _RenderOutcome(
                    plan,
                    verified[i]
                    if i in verified
                    else cast(
                        "renderers.RenderResult | Exception", staged_or_res
                    ),
                    pages,
                    scratch_bytes,
                )
                for i, (plan, staged_or_res, pages, scratch_bytes) in enumerate(
                    staged_rows
                )
            ]

        def execute(item: _PreparedVariant) -> _RenderOutcome:
            return _execute_prepared_variant(
                item,
                run=run,
                downloads=downloads,
                musescore=musescore,
                calibrated_identity=calibrated_identity,
                musescore_lock=musescore_lock,
                max_pages=min(item.measures, max_pages),
                max_scratch_bytes=(
                    None
                    if item.engine == "musescore"
                    else _verovio_worst_case_bytes(item)
                ),
            )

        if thread_pool is not None and len(items) > 1:
            jobs = [thread_pool.submit(execute, item) for item in items]
            outcomes = [job.result() for job in jobs]
        else:
            outcomes = [execute(item) for item in items]
        meter.add(sum(outcome.pages for outcome in outcomes))
        meter.uncounted_worker = False
        return outcomes

    def flush_batch() -> list[RenderedVariant]:  # noqa: C901
        nonlocal musescore_pages, reserved_measures, reserved_scratch_bytes
        nonlocal pending_ms_measures, attempted
        if not batch:
            return []
        drain_preparations()
        items = list(batch)
        batch.clear()
        reserved_measures = 0
        first = items[0]
        is_musescore = first.engine == "musescore"
        required_scratch = sum(_item_worst_case_bytes(it) for it in items)
        scratch_disk = meter.check_scratch(required_scratch)
        reserved_scratch_bytes = 0
        pending_ms_measures = 0
        remaining = W6_DISK_CAP_BYTES - scratch_disk.usage
        max_pages = max(
            1, remaining // (WORKER_SCRATCH_HEADROOM_BYTES * len(items))
        )
        # If dispatch or collection fails, settlement charges the reservation.
        meter.uncounted_worker = True
        outcomes = dispatch(items, max_pages, is_musescore=is_musescore)
        scratch_disk.check_headroom(
            sum(outcome.scratch_bytes for outcome in outcomes)
        )
        scratch_disk.usage += sum(
            outcome.scratch_bytes
            for outcome in outcomes
            if isinstance(outcome.result, _WorkerError | Exception)
        )
        rendered: list[RenderedVariant] = []
        failure: Exception | None = None
        for outcome in outcomes:
            if outcome.result is None:
                continue
            attempted = True
            if isinstance(outcome.result, _WorkerError):
                failure = failure or _to_exception(outcome.result)
                continue
            if isinstance(outcome.result, Exception):
                failure = failure or outcome.result
                continue
            engine = (
                outcome.plan.engine
                if isinstance(outcome.plan, _PreparedVariant)
                else "verovio"
            )
            gate = outcome.result.gate
            ledger = meter.gates[engine]
            ledger.record(gate, set(outcome.result.categories))
            if engine == "musescore":
                musescore_pages += outcome.pages
            else:
                ledger.disabled_categories.update(VEROVIO_DISABLED_CATEGORIES)
            if gate.status == "passed":
                rendered.append(_rendered_variant(outcome.plan, outcome.result))
        if failure is not None:
            raise failure
        if (
            meter.gates["verovio"].disabled_categories
            - VEROVIO_DISABLED_CATEGORIES
        ):
            meter.validate_verovio_cohort(formal=False)
        if process_pool is not None and not is_musescore and rendered:
            return _prepackage_variants_parallel(
                process_pool, workers, rendered
            )
        return rendered

    try:
        for round_index in range(_MAX_VARIANT_ROUNDS):
            drain_preparations()
            preparations.clear()
            attempted = False
            idx = 0
            while idx < len(rows):
                if all(
                    meter.crop_counts[s] >= need for s, need in targets.items()
                ):
                    return
                row = rows[idx]
                split = _field(row, "split")
                if meter.crop_counts[split] >= targets[split]:
                    discard_preparation(idx)
                    idx += 1
                    continue
                measures = _int(row.get("measure_count"), 1)
                if measures + reserved_measures > meter.planned - meter.actual:
                    if batch:
                        yield from flush_batch()
                        continue
                    discard_preparation(idx)
                    idx += 1
                    continue
                plan = _variant_plan(row, round_index)
                allow_musescore = permits_musescore(measures)
                item = prepare(plan, idx, round_index)
                is_musescore = item.engine == "musescore"
                if is_musescore and not allow_musescore:
                    if pending_ms_measures > 0 and batch:
                        yield from flush_batch()
                        continue
                    discard_preparation(idx)
                    idx += 1
                    continue
                if is_musescore and batch:
                    yield from flush_batch()
                    continue
                discard_preparation(idx)
                if (
                    is_musescore
                    and item.xml.count("<repeat") > _MAX_MUSESCORE_REPEATS
                ):
                    idx += 1
                    continue
                item_scratch = _item_worst_case_bytes(item)
                meter.check_scratch(reserved_scratch_bytes + item_scratch)
                batch.append(item)
                reserved_measures += measures
                reserved_scratch_bytes += item_scratch
                if is_musescore:
                    pending_ms_measures += measures
                idx += 1
                if is_musescore or len(batch) >= workers:
                    yield from flush_batch()
            if batch:
                yield from flush_batch()
            if not attempted:
                return
    finally:
        if process_pool is not None:
            has_pending = any(
                not isinstance(slot, _PreparedVariant | _WorkerError)
                and not slot.done()
                for _, _, slot in preparations.values()
            ) or any(not fut.done() for _, fut in discarded)
            _shutdown_pool(process_pool, clean_idle=not has_pending)
        if thread_pool is not None:
            thread_pool.shutdown(wait=True)


def _offline_musescore(
    command: Sequence[str],
    timeout: float,
) -> tuple[int | None, str]:
    try:
        process = subprocess.run(  # noqa: S603
            wsl_translate_command(command),
            env=os.environ | W6_OFFLINE_ENV,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            stdin=subprocess.DEVNULL,
            close_fds=True,
            timeout=min(timeout, W6_WORKER_TIMEOUT_SECONDS),
            check=False,
        )
    except subprocess.TimeoutExpired as exc:
        return None, (exc.output or b"").decode("utf-8", errors="replace")
    return process.returncode, process.stdout.decode("utf-8", errors="replace")


def _write_packaging_report(run: Path, downloads: Path, result: Row) -> None:
    path = run / "report" / "w6-packaging.json"
    with w6_disk_lock(downloads, run.parent) as runs:
        check_no_symlink_under(path, run)
        report = json.dumps(result, indent=2) + "\n"
        _check_w6_disk_cap(runs, additional_bytes=len(report.encode()))
        atomic_write_text(path, report)


def run_expanded_w4a_and_package(  # noqa: PLR0913, PLR0915
    downloads_dir: Path,
    run_dir: Path,
    *,
    lieder_manifest: Path,
    test_exclusion: Path,
    test_status: Path,
    musescore: MuseScore,
    planned_pages: int = 15_000,
    max_pdmx: int = 3000,
    min_val_works: int = 150,
    formal: bool = True,
    calibrated_identity: tuple[str, str] | None = None,
    target_train_crops: int = 45_000,
    target_val_crops: int = 5_000,
    prefix_train_crops: int = 18_000,
    prefix_val_crops: int = 2_000,
    verovio_workers: int = 1,
    musescore_page_cap: int | None = None,
) -> Row:
    """Reuse Lieder, expand staged PDMX, then render and package offline."""
    run, downloads = run_dir.absolute(), downloads_dir.absolute()
    check_w6_runs_root(downloads, run.parent)
    validate_w6_packaging_minima(
        formal=formal,
        max_pdmx=max_pdmx,
        min_val_works=min_val_works,
        crop_targets=(
            prefix_train_crops,
            prefix_val_crops,
            target_train_crops,
            target_val_crops,
        ),
    )
    repo = repository_root()
    if (
        not run.name.startswith("w6-slice-a-")
        or musescore.confinement.run != run
        or musescore.confinement.downloads != downloads
        or repo is None
        or any(p.is_relative_to(repo) for p in (run, downloads))
    ):
        msg = "invalid W6 packaging write roots"
        raise StopConditionError(msg)
    if _int(max_pdmx) + _LIEDER_COUNT > _MAX_TOTAL_SOURCES:
        msg = "expanded intake exceeds the W6 admitted score ceiling"
        raise StopConditionError(msg)
    check_no_symlink_under(run, Path(run.anchor))
    check_no_symlink_under(downloads, Path(downloads.anchor))
    state = source_state()
    if formal and state["dirty"]:
        msg = "formal Slice A run requires a clean code tree"
        raise StopConditionError(msg)
    reserve = reserve_w6_render_run(
        downloads,
        run_id=run.name,
        git_commit=str(state["commit"]),
        clean_tree=not state["dirty"],
        command="run-expanded-w4a-and-package",
        planned_pages=planned_pages,
        runs_parent=run.parent,
    )
    meter = _PageMeter(planned_pages, min_verovio_cohort=250 if formal else 30)
    status = "failed"
    try:
        environment = verify_w6_training_assets(downloads)
        verify_w4_w5_live_assets(downloads, require_s3=True)
        if sha256_file(musescore.executable) != musescore.executable_sha256:
            msg = "host MuseScore binary drifted before W6 rendering"
            raise StopConditionError(msg)
        if musescore_page_cap != 0:
            _verify_musescore_output_bound_preconditions(musescore)
        environment |= {
            "git": state,
            "offline_env": W6_OFFLINE_ENV,
            "musescore_version": musescore.version,
            "musescore_binary_sha256": musescore.executable_sha256,
            "verovio_version": VEROVIO_VERSION,
            "verovio_wheel_sha256": VEROVIO_WHEEL_SHA256,
            "resvg_version": RESVG_VERSION,
            "resvg_wheel_sha256": RESVG_WHEEL_SHA256,
        }
        with w6_disk_lock(downloads, run.parent) as runs:
            env_path = run / "environment.json"
            check_no_symlink_under(env_path, run.parent)
            environment_text = json.dumps(environment) + "\n"
            _check_w6_disk_cap(
                runs,
                additional_bytes=len(environment_text.encode()),
            )
            atomic_write_text(env_path, environment_text)
            gate = MetadataOnlyTestExclusion(test_exclusion, test_status)
            rows, funnel = _expanded_intake(
                run,
                downloads,
                lieder_manifest,
                gate,
                _int(max_pdmx),
                workers=_int(verovio_workers, 1),
            )
            _record_intake(run, runs, rows, funnel, formal=formal)
        train, val, split = split_admitted_sources_by_composer(
            rows, min_val_works=min_val_works
        )
        # Alternate splits so train cannot exhaust the shared page budget.
        ordered = [
            row for pair in zip(train, val, strict=False) for row in pair
        ]
        ordered += train[len(val) :] + val[len(train) :]

        def validate_before_publish() -> None:
            meter.validate_verovio_cohort(formal=formal)
            if sha256_file(musescore.executable) != musescore.executable_sha256:
                msg = "host MuseScore binary changed during W6 rendering"
                raise StopConditionError(msg)

        result = _package_rendered_variants(
            _render_variants(
                ordered,
                run=run,
                downloads=downloads,
                musescore=replace(musescore, launcher=_offline_musescore),
                meter=meter,
                calibrated_identity=calibrated_identity,
                targets={_TRAIN: target_train_crops, _VAL: target_val_crops},
                verovio_workers=verovio_workers,
                musescore_page_cap=musescore_page_cap,
            ),
            run,
            downloads_dir=downloads,
            target_train_crops=target_train_crops,
            target_val_crops=target_val_crops,
            prefix_train_crops=prefix_train_crops,
            prefix_val_crops=prefix_val_crops,
            _formal_milestones=formal,
            validate_before_publish=validate_before_publish,
            _meter=meter,
        )
        result |= {
            "intake": funnel,
            "split": split,
            "rendered_pages": meter.actual,
            "gate_summary": meter.gate_summary(),
        }
        _write_packaging_report(run, downloads, result)
    except _VerovioCohortError:
        _write_packaging_report(
            run,
            downloads,
            {
                "status": "failed",
                "rendered_pages": meter.actual,
                "gate_summary": meter.gate_summary(),
            },
        )
        raise
    else:
        status = "completed"
        return result
    finally:
        settle_w6_run(
            downloads,
            reserve,
            status=status,
            actual_pages=max(planned_pages, meter.actual)
            if meter.uncounted_worker
            else meter.actual,
        )
