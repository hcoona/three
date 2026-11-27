"""W6 lexical supervision, detector geometry, and optional neural models."""

from __future__ import annotations

import codecs
import hashlib
import io
import json
import math
import os
import re
import subprocess
import sys
from collections.abc import Mapping
from contextlib import contextmanager, redirect_stdout, suppress
from dataclasses import asdict, dataclass, replace
from functools import cache
from importlib import import_module
from pathlib import Path
from sysconfig import get_path
from threading import Timer
from types import MappingProxyType
from typing import TYPE_CHECKING, Any, Literal, cast, overload

import numpy as np

if TYPE_CHECKING:
    from collections.abc import Callable, Iterable, Iterator, Sequence

    from numpy.typing import ArrayLike, NDArray
    from PIL.Image import Image

    from sheet_music_omr.synthetic.renderers import RenderedPage
    from sheet_music_omr.training.decoding import (
        BeamCandidate,
        SystemPromptState,
    )
    from sheet_music_omr.training.packaging import RenderedVariant

_EXCLUSIVE_SPELLINGS = {
    "clef": ("<clef ",),
    "cross_staff": ("xs1", "xs2"),
    "grace": ("grace/", "grace"),
    "hairpin": (
        "<cresc(>",
        "<dim(>",
        "<hairpin)>",
        "rel=hairpin@",
        '"kind":"hairpin"',
    ),
    "ottava": (
        "<8va(>",
        "<8va)>",
        "<8vb(>",
        "<8vb)>",
        "<15ma(>",
        "<15ma)>",
        "<15mb(>",
        "<15mb)>",
        "rel=ottava@",
        '"kind":"ottava"',
        "8va",
        "8vb",
        "15ma",
        "15mb",
    ),
}
_LEXEMES = (
    "format: text-score/2",
    "key: ",
    "time: ",
    "clefs: ",
    "carry: ",
    "page: ",
    "system: ",
    "carry-in: ",
    "heading: ",
    "title: ",
    "composer: ",
    "m ",
    "len=",
    "key=",
    "time=",
    "symbol=",
    "from=",
    "to=",
    "right=",
    "left=repeat",
    "ending=",
    "single",
    "repeat",
    "final",
    "double",
    "start",
    "end",
    "continue",
    "common",
    "numeric",
    "cut",
    '"measure":',
    '"offset":',
    '"event_counts":',
    '"accidental_scope":',
    '"accidentals":',
    '"open":',
    '"id":',
    '"kind":',
    '"start":',
    '"pitch":',
    '"variant":',
    '"written":',
    '"scale":',
    '"staff":',
    '"voice":',
    '"event":',
    '"step":',
    '"alter":',
    '"octave":',
    '"onset":',
    '"at":',
    '"direction":',
    '"octave"',
    '"letter"',
    "tie-in",
    "tie@r",
    "arc@r",
    "rel=slur@r",
    "rel=tremolo@r",
    "tremolo=single:",
    "two-start",
    "two-end",
    "rel=arpeggio@r",
    "rel=ornament@r",
    "extended-trill",
    "member",
    "beam1=",
    "beam2=",
    "beam3=",
    "beam4=",
    "beam5=",
    "hook-f",
    "hook-b",
    "stacciss",
    "stacc",
    "acc",
    "ten",
    "marc",
    "port",
    "ferm_",
    "ferm",
    "orn=",
    "mordent",
    "inverted-mordent",
    "turn",
    "inverted-turn",
    "trill",
    "up",
    "down",
    "arp",
    "##",
    "bb",
    "R!",
    "r/",
    "_/",
    "<?",
    "<tempo ",
    "<rehearsal ",
    "<pedal-sign ",
    "<dyn-text ",
    "<^dyn-text ",
    "<ped(>",
    "<ped)>",
    "<ped-open)>",
    "<dashes)>",
    "<line(>",
    "<line)>",
    "<trill)>",
    "rel=pedal@r",
    "rel=line@r",
    "rel=trill@r",
    *(
        f"<{dynamic}>"
        for dynamic in (
            "pppp",
            "ppp",
            "pp",
            "p",
            "mp",
            "mf",
            "f",
            "ff",
            "fff",
            "ffff",
            "sf",
            "sfz",
            "sfp",
            "fp",
            "rfz",
        )
    ),
    *(f"{staff}.{voice}: " for staff in (1, 2) for voice in range(1, 5)),
    *(f"/{n}" for n in (1, 2, 4, 8, 16, 32, 64, 128)),
    *(f"f{digit}" for digit in range(1, 6)),
    *(f"{step}{octave}" for step in "ABCDEFG" for octave in range(10)),
)
_SPECIAL_COUNT = 3
_BYTE_COUNT = 256
_LEXEME_BASE = _SPECIAL_COUNT + _BYTE_COUNT
_VOCAB = tuple(
    dict.fromkeys(
        (
            *_LEXEMES,
            *(
                spelling
                for spellings in _EXCLUSIVE_SPELLINGS.values()
                for spelling in spellings
            ),
        )
    )
)
_LEXEME_IDS = {word: i for i, word in enumerate(_VOCAB, _LEXEME_BASE)}
CATEGORY_EXCLUSIVE_TOKEN_IDS = MappingProxyType(
    {
        category: frozenset(_LEXEME_IDS[word] for word in spellings)
        for category, spellings in _EXCLUSIVE_SPELLINGS.items()
    }
)
_SCAN = re.compile(
    "|".join(re.escape(word) for word in sorted(_VOCAB, key=len, reverse=True))
    + r'|"(?:[^"\\\n]|\\.)*"|//[^\n]*|[\s\S]'
)


class TextScore2DomainTokenizer:
    """Lossless lexical tokenizer with dedicated supervision and UTF-8 bytes."""

    pad_token_id = 0
    bos_token_id = 1
    eos_token_id = 2
    prompt_sep_token_id = _LEXEME_BASE + len(_VOCAB)
    vocab_size = prompt_sep_token_id + 1

    def __len__(self) -> int:
        """Return the size of the deterministic ID vocabulary."""
        return self.vocab_size

    def encode(
        self, text: str, *, add_special_tokens: bool = False
    ) -> list[int]:
        """Tokenize notation; encode literal text and unknown forms as bytes."""
        ids = [self.bos_token_id] if add_special_tokens else []
        for match in _SCAN.finditer(text):
            spelling = match[0]
            token = _LEXEME_IDS.get(spelling)
            if token is None:
                ids.extend(byte + _SPECIAL_COUNT for byte in spelling.encode())
            else:
                ids.append(token)
        if add_special_tokens:
            ids.append(self.eos_token_id)
        return ids

    def token_bytes(self, token: int) -> bytes:
        """Return the raw text bytes for one ID, excluding control tokens."""
        if not 0 <= token < self.vocab_size:
            msg = f"invalid Text Score 2 token ID: {token}"
            raise ValueError(msg)
        if token < _SPECIAL_COUNT or token == self.prompt_sep_token_id:
            return b""
        if token < _LEXEME_BASE:
            return bytes((token - _SPECIAL_COUNT,))
        return _VOCAB[token - _LEXEME_BASE].encode()

    def decode(self, tokens: Iterable[int]) -> str:
        """Decode notation losslessly, rejecting malformed UTF-8."""
        return b"".join(map(self.token_bytes, tokens)).decode()

    def pieces(self, tokens: Iterable[int]) -> Iterator[str]:
        """Yield one text piece per token, including partial UTF-8 and EOS."""
        decoder = codecs.getincrementaldecoder("utf-8")()
        for token in tokens:
            yield decoder.decode(self.token_bytes(token))
        decoder.decode(b"", final=True)


def _disabled_categories(mask: Mapping[str, bool]) -> set[str]:
    # Crop labels use "clef"; its required header tokens remain unmasked.
    clef_aliases = {"mid_score_clef_change", "mid-score clef change"}
    return {
        "clef" if cat in clef_aliases else cat
        for cat, enabled in mask.items()
        if not enabled
    }


def verovio_row_eligible_for_bpe_loss(
    category_supervision_mask: Mapping[str, bool],
    exclusive_token_ids: Mapping[str, Iterable[int]] | None = None,
    *,
    shared_token_ids: Iterable[int] = (),
) -> bool:
    """Require a complete, isolated assignment for every disabled category.

    A caller may supply audited exclusive assignments for a particular BPE
    vocabulary. Without that proof, disabled rows are detector-only.
    """
    disabled = _disabled_categories(category_supervision_mask)
    if not disabled:
        return True
    assignments = {
        cat: set(ids) for cat, ids in (exclusive_token_ids or {}).items()
    }
    shared = set(shared_token_ids)
    return all(
        (ids := assignments.get(cat))
        and not ids.intersection(shared)
        and all(
            not ids.intersection(other_ids)
            for other, other_ids in assignments.items()
            if other != cat
        )
        for cat in disabled
    )


@overload
def apply_category_supervision_logit_mask(
    logits: NDArray[np.floating],
    category_supervision_mask: Mapping[str, bool],
    *,
    exclusive_token_ids: Mapping[str, Iterable[int]] = ...,
) -> NDArray[np.floating]: ...


@overload
def apply_category_supervision_logit_mask(
    logits: Any,  # noqa: ANN401
    category_supervision_mask: Mapping[str, bool],
    *,
    exclusive_token_ids: Mapping[str, Iterable[int]] = ...,
) -> Any: ...  # noqa: ANN401


def apply_category_supervision_logit_mask(
    logits: Any,
    category_supervision_mask: Mapping[str, bool],
    *,
    exclusive_token_ids: Mapping[str, Iterable[int]] = (
        CATEGORY_EXCLUSIVE_TOKEN_IDS
    ),
) -> Any:
    """Return NumPy or Torch logits with disabled continuations set to -inf."""
    disabled = _disabled_categories(category_supervision_mask)
    if not disabled:
        return logits
    assignments = {cat: set(ids) for cat, ids in exclusive_token_ids.items()}
    if not verovio_row_eligible_for_bpe_loss(
        category_supervision_mask, assignments
    ):
        msg = "disabled categories require isolated vocabulary IDs"
        raise ValueError(msg)
    ids = sorted(set().union(*(assignments[cat] for cat in disabled)))
    if ids[0] < 0 or ids[-1] >= logits.shape[-1]:
        msg = "category token IDs are outside the logit vocabulary"
        raise ValueError(msg)
    if isinstance(logits, np.ndarray):
        masked = logits.copy()
        masked[..., ids] = -np.inf
        return masked
    torch = import_module("torch")
    indices = torch.tensor(ids, dtype=torch.long, device=logits.device)
    return logits.index_fill(-1, indices, -float("inf"))


def compute_stride32_resize(
    width: int, height: int, longest_edge: int = 1024
) -> tuple[int, int]:
    """Compute unpadded detector dimensions, in width/height order."""
    if min(width, height, longest_edge) <= 0:
        msg = "image dimensions and longest edge must be positive"
        raise ValueError(msg)
    scale = longest_edge / max(width, height)
    return (
        max(32, round(width * scale / 32) * 32),
        max(32, round(height * scale / 32) * 32),
    )


type Box = tuple[float, float, float, float]
DETECTOR_CLASSES = ("system", "measure")
_MATRIX_DIMS = 2
_SPATIAL_MERGE = 2
_IMAGE_TOKEN = 3


@dataclass(frozen=True, slots=True)
class DetectorPrediction:
    """A valid original-pixel box from a class-specific query ranking."""

    page_id: str
    class_id: int
    query_idx: int
    probability: float
    bbox: Box

    @property
    def class_name(self) -> str:
        """Return the page annotation class."""
        return DETECTOR_CLASSES[self.class_id]


@dataclass(frozen=True, slots=True)
class DetectorTarget:
    """An original-pixel target for single-class matching."""

    page_id: str
    bbox: Box


_CROP_SCHEMA = "omr-w6-crop-dataset-v1"
_PAGE_BOX_SCHEMA = "omr-w6-page-boxes-v1"
_BOX_COORDINATES = 4


def _read_manifest(path: Path, schema: str) -> Iterator[dict[str, object]]:
    with path.open(encoding="utf-8") as stream:
        for line_num, line in enumerate(stream, 1):
            row = json.loads(line)
            if not isinstance(row, dict) or row.get("schema") != schema:
                msg = f"{path.name}:{line_num}: expected {schema} row"
                raise ValueError(msg)
            yield row


def _manifest_dimension(row: Mapping[str, object], key: str) -> int:
    value = row.get(key)
    if type(value) is not int or value <= 0:
        msg = f"invalid packaged image dimension: {key}"
        raise ValueError(msg)
    return value


def _drop_file_page_cache(fd: int) -> None:
    if hasattr(os, "posix_fadvise") and hasattr(os, "POSIX_FADV_DONTNEED"):
        with suppress(OSError):
            os.posix_fadvise(fd, 0, 0, os.POSIX_FADV_DONTNEED)


def _packaged_png(
    root: Path, relative: object, sha256: object, size: tuple[int, int]
) -> Path:
    if not isinstance(relative, str) or Path(relative).is_absolute():
        msg = "packaged PNG requires a dataset-relative path"
        raise ValueError(msg)
    root = root.resolve()
    path = (root / relative).resolve()
    if not path.is_relative_to(root) or not path.is_file():
        msg = "packaged PNG is missing or outside the dataset root"
        raise ValueError(msg)
    image = import_module("PIL.Image")
    with path.open("rb") as stream:
        if hashlib.file_digest(stream, "sha256").hexdigest() != sha256:
            msg = f"packaged PNG SHA-256 mismatch: {relative}"
            raise ValueError(msg)
        stream.seek(0)
        with image.open(stream) as png:
            if png.format != "PNG" or png.size != size:
                msg = f"packaged PNG format or dimensions mismatch: {relative}"
                raise ValueError(msg)
            png.verify()
        _drop_file_page_cache(stream.fileno())
    return path


def load_packaged_crop_manifest(split_dir: Path) -> list[dict[str, object]]:
    """Load complete ordered variants and verify their dataset-relative PNGs."""
    rows = []
    sequences: dict[str, tuple[int, int]] = {}
    for row in _read_manifest(split_dir / "crops.jsonl", _CROP_SCHEMA):
        variant, index = row.get("variant_id"), row.get("system_index")
        count = row.get("expected_systems")
        if not isinstance(variant, str) or not variant:
            msg = "packaged crop requires a variant_id"
            raise ValueError(msg)
        if (
            type(count) is not int
            or type(index) is not int
            or not 0 <= index < count
        ):
            msg = f"invalid packaged crop sequence for {variant}"
            raise ValueError(msg)
        if sequences.get(variant, (0, count)) != (index, count):
            msg = f"out of order or inconsistent crop sequence for {variant}"
            raise ValueError(msg)
        sequences[variant] = index + 1, count
        _packaged_png(
            split_dir.parent.parent,
            row.get("crop_relpath"),
            row.get("sha256"),
            (
                _manifest_dimension(row, "crop_width"),
                _manifest_dimension(row, "crop_height"),
            ),
        )
        rows.append(row)
    if not rows or any(seen != count for seen, count in sequences.values()):
        msg = "empty manifest or incomplete packaged crop sequence"
        raise ValueError(msg)
    return rows


def _manifest_box(value: object, width: int, height: int) -> Box:
    if (
        not isinstance(value, list)
        or len(value) != _BOX_COORDINATES
        or not all(type(coord) in (int, float) for coord in value)
    ):
        msg = "packaged page box requires four pixel coordinates"
        raise ValueError(msg)
    x0, y0, x1, y1 = map(float, value)
    if not (0 <= x0 < x1 <= width and 0 <= y0 < y1 <= height):
        msg = "packaged page box lies outside the page raster"
        raise ValueError(msg)
    return x0, y0, x1, y1


def load_packaged_page_boxes_manifest(
    split_dir: Path,
) -> list[dict[str, object]]:
    """Load pages with class-indexed targets and COCO detection annotations."""
    rows = []
    path = split_dir / "page-boxes.jsonl"
    for image_id, row in enumerate(_read_manifest(path, _PAGE_BOX_SCHEMA)):
        page_id, boxes = row.get("page_id"), row.get("boxes")
        if (
            not isinstance(page_id, str)
            or not page_id
            or not isinstance(boxes, list)
        ):
            msg = "packaged page requires a page_id and a list of boxes"
            raise ValueError(msg)
        width = _manifest_dimension(row, "page_width_px")
        height = _manifest_dimension(row, "page_height_px")
        targets: dict[int, list[DetectorTarget]] = {0: [], 1: []}
        annotations = []
        for box in boxes:
            cls = box.get("class_id") if isinstance(box, dict) else None
            if type(cls) is not int or cls not in targets:
                msg = "page box class_id must be 0 (system) or 1 (measure)"
                raise ValueError(msg)
            bbox = _manifest_box(box.get("box_px"), width, height)
            targets[cls].append(DetectorTarget(page_id, bbox))
            x0, y0, x1, y1 = bbox
            w, h = x1 - x0, y1 - y0
            annotations.append(
                {
                    "bbox": [x0, y0, w, h],
                    "category_id": cls,
                    "area": w * h,
                    "iscrowd": 0,
                }
            )
        rows.append(
            row
            | {
                "targets": targets,
                "annotations": {
                    "image_id": image_id,
                    "annotations": annotations,
                },
            }
        )
    return rows


def _original_box(box: NDArray[np.float64], width: int, height: int) -> Box:
    cx, cy, w, h = map(float, box)
    return (
        min(width, max(0.0, (cx - w / 2) * width)),
        min(height, max(0.0, (cy - h / 2) * height)),
        min(width, max(0.0, (cx + w / 2) * width)),
        min(height, max(0.0, (cy + h / 2) * height)),
    )


def postprocess_detector_raw_outputs(  # noqa: PLR0913
    logits: ArrayLike,
    pred_boxes: ArrayLike,
    orig_width: int,
    orig_height: int,
    *,
    k_system: int = 32,
    k_measure: int = 128,
    page_id: str = "",
) -> list[DetectorPrediction]:
    """Rank raw queries independently per class, then clamp and filter."""
    scores = np.asarray(logits, dtype=np.float64)
    boxes = np.asarray(pred_boxes, dtype=np.float64)
    if (
        scores.ndim != _MATRIX_DIMS
        or scores.shape[1] != len(DETECTOR_CLASSES)
        or boxes.shape != (len(scores), 4)
        or np.isnan(scores).any()
        or not np.isfinite(boxes).all()
        or min(orig_width, orig_height) <= 0
        or min(k_system, k_measure) < 0
    ):
        msg = "invalid detector outputs, original dimensions, or top-K"
        raise ValueError(msg)
    probabilities = np.exp(-np.logaddexp(0.0, -scores))
    result = []
    for cls, limit in enumerate((k_system, k_measure)):
        # Stable sorting preserves query order at tied probabilities.
        order = np.argsort(-probabilities[:, cls], kind="stable")[:limit]
        for j in order:
            bbox = _original_box(boxes[j], orig_width, orig_height)
            if bbox[2] > bbox[0] and bbox[3] > bbox[1]:
                result.append(
                    DetectorPrediction(
                        page_id, cls, int(j), float(probabilities[j, cls]), bbox
                    )
                )
    return result


def _area(box: Box) -> float:
    x0, y0, x1, y1 = box
    return max(0, x1 - x0) * max(0, y1 - y0)


def _iou(a: Box, b: Box) -> float:
    overlap = _area(
        (max(a[0], b[0]), max(a[1], b[1]), min(a[2], b[2]), min(a[3], b[3]))
    )
    return overlap / (_area(a) + _area(b) - overlap) if overlap else 0.0


def _match_ranked(
    preds: Iterable[DetectorPrediction],
    targets: Sequence[DetectorTarget | Box],
    min_iou: float,
) -> list[bool]:
    targets_by_page: dict[str, list[Box]] = {}
    for target in targets:
        page, box = (
            (target.page_id, target.bbox)
            if isinstance(target, DetectorTarget)
            else ("", target)
        )
        targets_by_page.setdefault(page, []).append(box)
    ranked = sorted(
        preds, key=lambda p: (-p.probability, p.page_id, p.query_idx)
    )
    if len({p.class_id for p in ranked}) > 1:
        msg = "detector metrics require one class at a time"
        raise ValueError(msg)
    matched = []
    for prediction in ranked:
        available = targets_by_page.get(prediction.page_id, [])
        overlaps = [_iou(prediction.bbox, box) for box in available]
        best = max(range(len(overlaps)), key=overlaps.__getitem__, default=None)
        found = best is not None and overlaps[best] >= min_iou
        if found and best is not None:
            del available[best]
        matched.append(found)
    return matched


def compute_class_ap75(
    unthresholded_preds: Iterable[DetectorPrediction],
    gt_boxes: Sequence[DetectorTarget | Box],
) -> float | None:
    """Compute 101-point interpolated AP at .75 IoU using every valid query."""
    if not gt_boxes:
        return None
    matches = _match_ranked(unthresholded_preds, gt_boxes, 0.75)
    if not matches:
        return 0.0
    tp = np.cumsum(matches)
    recall = tp / len(gt_boxes)
    precision = tp / np.arange(1, len(tp) + 1)
    envelope = np.maximum.accumulate(precision[::-1])[::-1]
    return float(
        np.mean(
            np.append(envelope, 0.0)[
                np.searchsorted(recall, np.linspace(0, 1, 101))
            ]
        )
    )


@dataclass(frozen=True, slots=True)
class DetectorOperatingPoint:
    """One-to-one matches at one class-specific score/IoU threshold."""

    true_positives: int
    false_positives: int
    false_negatives: int

    @property
    def precision(self) -> float:
        """Return the matched fraction of selected predictions."""
        total = self.true_positives + self.false_positives
        return self.true_positives / total if total else 0.0

    @property
    def recall(self) -> float:
        """Return the matched fraction of labeled boxes."""
        total = self.true_positives + self.false_negatives
        return self.true_positives / total if total else 0.0


def evaluate_operating_point(
    unthresholded_preds: Iterable[DetectorPrediction],
    gt_boxes: Sequence[DetectorTarget | Box],
    *,
    threshold: float,
    min_iou: float = 0.85,
) -> DetectorOperatingPoint:
    """Evaluate thresholded predictions separately from unthresholded AP75."""
    if not 0 <= threshold <= 1 or not 0 < min_iou <= 1:
        msg = "invalid detector operating threshold or IoU"
        raise ValueError(msg)
    matches = _match_ranked(
        (p for p in unthresholded_preds if p.probability >= threshold),
        gt_boxes,
        min_iou,
    )
    tp = sum(matches)
    return DetectorOperatingPoint(tp, len(matches) - tp, len(gt_boxes) - tp)


def unpermute_spatial_merge_2x2(
    tokens: Any,  # noqa: ANN401
    grid_h: int,
    grid_w: int,
) -> Any:  # noqa: ANN401
    """Restore a NumPy or Torch block-major sequence to raster [H, W, D]."""
    stride = _SPATIAL_MERGE
    if (
        min(grid_h, grid_w) <= 0
        or grid_h % stride
        or grid_w % stride
        or tokens.ndim < _MATRIX_DIMS
        or tokens.shape[-2] != grid_h * grid_w
    ):
        msg = "spatial merge requires an even grid and one state per patch"
        raise ValueError(msg)
    leading, dim = tokens.shape[:-2], tokens.shape[-1]
    n = len(leading)
    blocked = tokens.reshape(*leading, grid_h // 2, grid_w // 2, 2, 2, dim)
    raster = blocked.swapaxes(n + 1, n + 2)
    return raster.reshape(*leading, grid_h, grid_w, dim)


if TYPE_CHECKING:
    MonkeyOCRv2VisionTrunk: Any
    MonkeyOCRv2EncoderDecoder: Any
    ConvNeXtStride8And16EncoderDecoder: Any


def _sinusoid_1d(
    length: int,
    dim: int,
    like: Any,  # noqa: ANN401
) -> Any:  # noqa: ANN401
    torch = import_module("torch")
    i = torch.arange(0, dim, 2, device=like.device, dtype=torch.float32)
    inverse = torch.exp(-math.log(10000) * i / dim)
    positions = torch.arange(length, device=like.device, dtype=torch.float32)
    phase = positions[:, None] * inverse[None, :]
    return torch.stack((phase.sin(), phase.cos()), dim=-1).flatten(1).to(like)


def _raster_position(
    height: int,
    width: int,
    dim: int,
    like: Any,  # noqa: ANN401
) -> Any:  # noqa: ANN401
    torch = import_module("torch")
    if dim % 4:
        msg = "2D sinusoidal positions require a width divisible by four"
        raise ValueError(msg)
    rows = _sinusoid_1d(height, dim // 2, like)[:, None, :].expand(
        -1, width, -1
    )
    cols = _sinusoid_1d(width, dim // 2, like)[None, :, :].expand(
        height, -1, -1
    )
    return torch.cat((rows, cols), dim=-1)


@cache
def _neural_arm_b_classes() -> dict[str, Any]:  # noqa: C901
    torch = import_module("torch")
    nn = torch.nn
    fn = torch.nn.functional

    class _MonkeyRMSNorm(nn.Module):
        def __init__(self, dim: int, eps: float = 1e-5) -> None:
            super().__init__()
            self.weight = nn.Parameter(torch.ones(dim))
            self.eps = eps

        def forward(self, x: Any) -> Any:  # noqa: ANN401
            xf = x.float()
            normed = xf * torch.rsqrt(
                xf.pow(2).mean(-1, keepdim=True) + self.eps
            )
            return normed.type_as(x) * self.weight

    class _MonkeyRotaryEmbedding(nn.Module):
        def __init__(self, dim: int, theta: float = 10000.0) -> None:
            super().__init__()
            exponents = torch.arange(0, dim, 2, dtype=torch.float32) / dim
            self.register_buffer(
                "inv_freq", 1.0 / (theta**exponents), persistent=False
            )

        def forward(self, seqlen: int) -> Any:  # noqa: ANN401
            inv = cast("Any", self.inv_freq)
            seq = torch.arange(seqlen, device=inv.device, dtype=inv.dtype)
            return torch.outer(seq, inv)

    def _apply_vision_rope(tensor: Any, freqs: Any) -> Any:  # noqa: ANN401
        orig_dtype = tensor.dtype
        tf = tensor.float()
        half = tf.shape[-1] // 2
        rotated = torch.cat((-tf[..., half:], tf[..., :half]), dim=-1)
        cos = freqs.cos().repeat(1, 2)[None, None, :, :].float()
        sin = freqs.sin().repeat(1, 2)[None, None, :, :].float()
        return (tf * cos + rotated * sin).to(orig_dtype)

    class _MonkeySwiGLU(nn.Module):
        def __init__(
            self, dim: int, intermediate_size: int, *, bias: bool = False
        ) -> None:
            super().__init__()
            self.fc1 = nn.Linear(dim, intermediate_size, bias=bias)
            self.fc2 = nn.Linear(intermediate_size, dim, bias=bias)
            self.fc3 = nn.Linear(dim, intermediate_size, bias=bias)

        def forward(self, x: Any) -> Any:  # noqa: ANN401
            return self.fc2(fn.silu(self.fc1(x)) * self.fc3(x))

    class _MonkeyPatchifier(nn.Module):
        def __init__(
            self, dim: int, patch_size: int, merge_size: int, eps: float
        ) -> None:
            super().__init__()
            self.merge_size = merge_size
            self.proj = nn.Conv2d(
                3, dim, kernel_size=patch_size, stride=patch_size
            )
            self.norm = _MonkeyRMSNorm(dim, eps=eps)

        def forward(self, pixels: Any) -> tuple[Any, int, int]:  # noqa: ANN401
            feat = self.proj(pixels)
            batch, channels, gh, gw = feat.shape
            ms = self.merge_size
            merged = (
                feat.view(batch, channels, gh // ms, ms, gw // ms, ms)
                .permute(0, 2, 4, 3, 5, 1)
                .contiguous()
                .view(batch, gh * gw, channels)
            )
            return self.norm(merged), gh, gw

    class _MonkeyViTPreprocessor(nn.Module):
        def __init__(
            self, dim: int, patch_size: int, merge_size: int, eps: float
        ) -> None:
            super().__init__()
            self.patchifier = _MonkeyPatchifier(
                dim, patch_size, merge_size, eps
            )

        def forward(self, pixels: Any) -> tuple[Any, int, int]:  # noqa: ANN401
            return self.patchifier(pixels)

    class _MonkeyAttention(nn.Module):
        def __init__(
            self, dim: int, num_heads: int, *, bias: bool = False
        ) -> None:
            super().__init__()
            self.num_heads = num_heads
            self.head_dim = dim // num_heads
            self.qkv = nn.Linear(dim, dim * 3, bias=bias)
            self.proj = nn.Linear(dim, dim, bias=bias)

        def forward(self, x: Any, rope_freqs: Any) -> Any:  # noqa: ANN401
            batch, seq_len, dim = x.shape
            q, k, v = (
                self.qkv(x)
                .view(batch, seq_len, 3, self.num_heads, self.head_dim)
                .permute(2, 0, 3, 1, 4)
                .unbind(0)
            )
            q = _apply_vision_rope(q, rope_freqs)
            k = _apply_vision_rope(k, rope_freqs)
            attn = fn.scaled_dot_product_attention(q, k, v, dropout_p=0.0)
            return self.proj(attn.transpose(1, 2).reshape(batch, seq_len, dim))

    class _MonkeyBlock(nn.Module):
        def __init__(
            self,
            dim: int,
            num_heads: int,
            intermediate_size: int,
            eps: float,
            *,
            bias: bool = False,
        ) -> None:
            super().__init__()
            self.attn = _MonkeyAttention(dim, num_heads, bias=bias)
            self.norm1 = _MonkeyRMSNorm(dim, eps=eps)
            self.mlp = _MonkeySwiGLU(dim, intermediate_size, bias=bias)
            self.norm2 = _MonkeyRMSNorm(dim, eps=eps)

        def forward(self, x: Any, rope_freqs: Any) -> Any:  # noqa: ANN401
            x = x + self.attn(self.norm1(x), rope_freqs)
            return x + self.mlp(self.norm2(x))

    class MonkeyOCRv2VisionTrunk(nn.Module):
        """Patch-14 ViT trunk matching zenosai/MonkeyOCRv2-S@8efa4f856a.

        Architecture and parameter layout follow upstream Apache-2.0 source
        ``Yuliang-Liu/MonkeyOCRv2@da96afede9`` (vision encoder module)
        with native PyTorch SDPA and ``trust_remote_code=False``.
        """

        def __init__(  # noqa: PLR0913
            self,
            *,
            embed_dim: int = 384,
            num_hidden_layers: int = 12,
            num_attention_heads: int = 6,
            intermediate_size: int = 1536,
            patch_size: int = 14,
            spatial_merge_size: int = 2,
            rms_norm_eps: float = 1e-5,
            use_bias: bool = False,
            normalize_clip: bool = False,
            weights_path: Path | None = None,
        ) -> None:
            super().__init__()
            head_dim = embed_dim // max(1, num_attention_heads)
            if (
                spatial_merge_size != _SPATIAL_MERGE
                or patch_size <= 0
                or embed_dim % num_attention_heads != 0
                or head_dim % 4 != 0
            ):
                msg = (
                    "MonkeyOCRv2 requires positive patches, 2x2 merge,"
                    " and head_dim divisible by 4"
                )
                raise ValueError(msg)
            self.patch_size = patch_size
            self.spatial_merge_size = spatial_merge_size
            self.embed_dim = embed_dim
            self.normalize_clip = normalize_clip or (weights_path is not None)
            self.patch_embed = _MonkeyViTPreprocessor(
                embed_dim, patch_size, spatial_merge_size, rms_norm_eps
            )
            self.rotary_pos_emb = _MonkeyRotaryEmbedding(head_dim // 2)
            self.blocks = nn.ModuleList(
                [
                    _MonkeyBlock(
                        embed_dim,
                        num_attention_heads,
                        intermediate_size,
                        rms_norm_eps,
                        bias=use_bias,
                    )
                    for _ in range(num_hidden_layers)
                ]
            )
            self.post_trunk_norm = _MonkeyRMSNorm(embed_dim, eps=rms_norm_eps)
            if weights_path is not None:
                self.load_safetensors(weights_path)

        def load_safetensors(self, weights_path: Path) -> None:
            """Load pinned MonkeyOCRv2-S safetensors checkpoint strictly."""
            st_torch = import_module("safetensors.torch")
            target = (
                weights_path / "model.safetensors"
                if weights_path.is_dir()
                else weights_path
            )
            state_dict = st_torch.load_file(str(target))
            self.load_state_dict(state_dict, strict=True)

        def _grid_rope(
            self,
            gh: int,
            gw: int,
            device: Any,  # noqa: ANN401
        ) -> Any:  # noqa: ANN401
            ms = self.spatial_merge_size
            hp = (
                torch.arange(gh, device=device)
                .unsqueeze(1)
                .expand(-1, gw)
                .reshape(gh // ms, ms, gw // ms, ms)
                .permute(0, 2, 1, 3)
                .flatten()
            )
            wp = (
                torch.arange(gw, device=device)
                .unsqueeze(0)
                .expand(gh, -1)
                .reshape(gh // ms, ms, gw // ms, ms)
                .permute(0, 2, 1, 3)
                .flatten()
            )
            pos_ids = torch.stack((hp, wp), dim=-1)
            table = self.rotary_pos_emb(max(gh, gw))
            emb = table[pos_ids]
            return torch.stack((emb[:, 0], emb[:, 1]), dim=2).reshape(
                gh * gw, -1
            )

        def forward(self, pixels: Any) -> Any:  # noqa: ANN401
            """Encode BCHW pixels as [batch, grid_h, grid_w, embed_dim]."""
            if any(
                d == 0 or d % (2 * self.patch_size) for d in pixels.shape[-2:]
            ):
                msg = "MonkeyOCRv2 image dimensions must cover whole 2x2 blocks"
                raise ValueError(msg)
            x = pixels
            if self.normalize_clip:
                mean = x.new_tensor((0.48145466, 0.4578275, 0.40821073))[
                    None, :, None, None
                ]
                std = x.new_tensor((0.26862954, 0.26130258, 0.27577711))[
                    None, :, None, None
                ]
                x = (x - mean) / std
            param_dtype = self.patch_embed.patchifier.proj.weight.dtype
            hidden, gh, gw = self.patch_embed(x.to(dtype=param_dtype))
            rope = self._grid_rope(gh, gw, hidden.device)
            for block in self.blocks:
                hidden = block(hidden, rope)
            hidden = self.post_trunk_norm(hidden)
            return unpermute_spatial_merge_2x2(hidden, gh, gw)

    class _CausalDecoder(nn.Module):
        def __init__(self, spec: ArmBDecoderConfig) -> None:
            super().__init__()
            self.dim = spec.d_model
            self.embedding = nn.Embedding(
                spec.vocab_size,
                spec.d_model,
                padding_idx=TextScore2DomainTokenizer.pad_token_id,
            )
            layer = nn.TransformerDecoderLayer(
                spec.d_model,
                spec.nhead,
                spec.d_ff,
                batch_first=True,
                activation="gelu",
                norm_first=True,
                dropout=0.0,
            )
            self.layers = nn.TransformerDecoder(
                layer, spec.layers, norm=nn.LayerNorm(spec.d_model)
            )
            self.output = nn.Linear(spec.d_model, spec.vocab_size)

        def forward(self, tokens: Any, grid: Any) -> Any:  # noqa: ANN401
            """Attend to visual memory and only previous decoder tokens."""
            _, h, w, dim = grid.shape
            memory = (grid + _raster_position(h, w, dim, grid)).flatten(1, 2)
            length = tokens.shape[-1]
            embedded = self.embedding(tokens)
            embedded = embedded + _sinusoid_1d(length, self.dim, embedded)
            causal = torch.ones(
                (length, length), dtype=torch.bool, device=tokens.device
            ).triu(1)
            hidden = self.layers(
                embedded, memory, tgt_mask=causal, tgt_is_causal=True
            )
            return self.output(hidden)

    class ConvNeXtStride8And16EncoderDecoder(nn.Module):
        """ConvNeXt visual memory with an optional stride-8 lateral path."""

        def __init__(
            self,
            *,
            decoder: ArmBDecoderConfig = ArmBDecoderConfig(),
            fuse_stride8: bool = True,
            weights_path: Path | None = None,
            normalize_imagenet: bool | None = None,
        ) -> None:
            super().__init__()
            timm = import_module("timm")
            target_file = None
            if weights_path is not None:
                target_file = str(
                    weights_path / "model.safetensors"
                    if weights_path.is_dir()
                    else weights_path
                )
            self.normalize_imagenet = (
                target_file is not None
                if normalize_imagenet is None
                else normalize_imagenet
            )
            self.encoder = timm.create_model(
                "convnext_tiny",
                pretrained=target_file is not None,
                features_only=True,
                out_indices=(1, 2),
                pretrained_cfg_overlay=(
                    {"file": target_file} if target_file is not None else None
                ),
            )
            stride8, stride16 = self.encoder.feature_info.channels()
            self.project = nn.Conv2d(stride16, decoder.d_model, kernel_size=1)
            self.lateral = (
                nn.Conv2d(stride8, decoder.d_model, kernel_size=2, stride=2)
                if fuse_stride8
                else None
            )
            self.decoder = _CausalDecoder(decoder)

        def encode_visual_memory(self, pixels: Any) -> Any:  # noqa: ANN401
            """Encode BCHW crop once into [batch, h, w, d_model] memory."""
            x = pixels
            if self.normalize_imagenet:
                mean = x.new_tensor((0.485, 0.456, 0.406))[None, :, None, None]
                std = x.new_tensor((0.229, 0.224, 0.225))[None, :, None, None]
                x = (x - mean) / std
            param_dtype = next(self.encoder.parameters()).dtype
            stride8, stride16 = self.encoder(x.to(dtype=param_dtype))
            memory = self.project(stride16)
            if self.lateral is not None:
                memory = memory + self.lateral(stride8)
            return memory.permute(0, 2, 3, 1)

        def forward(self, pixels: Any, tokens: Any) -> Any:  # noqa: ANN401
            """Return [batch, tokens, vocabulary] logits."""
            return self.decoder(tokens, self.encode_visual_memory(pixels))

    class MonkeyOCRv2EncoderDecoder(nn.Module):
        """MonkeyOCRv2 raster memory projected to the Arm B decoder width."""

        def __init__(
            self,
            *,
            vision: Any = None,  # noqa: ANN401
            decoder: ArmBDecoderConfig = ArmBDecoderConfig(),
        ) -> None:
            super().__init__()
            self.encoder = (
                MonkeyOCRv2VisionTrunk() if vision is None else vision
            )
            self.project = nn.Linear(self.encoder.embed_dim, decoder.d_model)
            self.decoder = _CausalDecoder(decoder)

        def encode_visual_memory(self, pixels: Any) -> Any:  # noqa: ANN401
            """Encode BCHW crop once into [batch, h, w, d_model] memory."""
            return self.project(self.encoder(pixels))

        def forward(self, pixels: Any, tokens: Any) -> Any:  # noqa: ANN401
            """Return [batch, tokens, vocabulary] logits."""
            return self.decoder(tokens, self.encode_visual_memory(pixels))

    return {
        cls.__name__: cls
        for cls in (
            MonkeyOCRv2VisionTrunk,
            MonkeyOCRv2EncoderDecoder,
            ConvNeXtStride8And16EncoderDecoder,
        )
    }


@dataclass(frozen=True, slots=True)
class ArmBDecoderConfig:
    """Causal Text Score 2 decoder shape; W6 defaults to six layers."""

    vocab_size: int = TextScore2DomainTokenizer.vocab_size
    layers: int = 6
    d_model: int = 512
    nhead: int = 8
    d_ff: int = 2048


_ARM_B_NAMES = frozenset(
    {
        "MonkeyOCRv2VisionTrunk",
        "MonkeyOCRv2EncoderDecoder",
        "ConvNeXtStride8And16EncoderDecoder",
    }
)


def __getattr__(name: str) -> Any:  # noqa: ANN401
    if name in _ARM_B_NAMES:
        return _neural_arm_b_classes()[name]
    msg = f"{__name__!r} has no attribute {name!r}"
    raise AttributeError(msg)


type ArmAFamily = Literal["qwen3_vl", "qwen3_5", "paddleocr_vl"]
type DetectorFamily = Literal["d_fine", "rt_detr_v2"]
_ARM_A_NAMES: dict[ArmAFamily, str] = {
    "qwen3_vl": "Qwen3VL",
    "qwen3_5": "Qwen3_5",
    "paddleocr_vl": "PaddleOCRVL",
}
_ARM_A_REPOS: dict[ArmAFamily, str] = {
    "qwen3_vl": "Qwen/Qwen3-VL-2B-Instruct",
    "qwen3_5": "Qwen/Qwen3.5-2B",
    "paddleocr_vl": "PaddlePaddle/PaddleOCR-VL-1.6",
}
_DETECTOR_NAMES: dict[DetectorFamily, str] = {
    "d_fine": "DFine",
    "rt_detr_v2": "RTDetrV2",
}
ARM_A_SEQUENCE_PACKING = False


def load_arm_a_processor(local_metadata_dir: Path) -> Any:  # noqa: ANN401
    """Load an already verified local processor without any remote code."""
    tr = import_module("transformers")
    if not local_metadata_dir.is_dir():
        msg = "Arm A processors require a staged local metadata directory"
        raise ValueError(msg)
    return tr.AutoProcessor.from_pretrained(
        local_metadata_dir, local_files_only=True, trust_remote_code=False
    )


def _strip_auto_map(raw: Mapping[str, Any]) -> dict[str, Any]:
    out: dict[str, Any] = {}
    for k, v in raw.items():
        if k == "auto_map":
            continue
        out[k] = _strip_auto_map(v) if isinstance(v, Mapping) else v
    return out


def load_pretrained_arm_a_and_processor(  # noqa: PLR0913
    family: ArmAFamily,
    weights_dir: Path,
    *,
    rank: int = 32,
    include_vision: bool = False,
    lora: bool = True,
    dtype: Any = None,  # noqa: ANN401
    gradient_checkpointing: bool = True,
) -> tuple[Any, Any]:
    """Load a pinned local VLM checkpoint and processor without remote code."""
    tr = import_module("transformers")
    torch = import_module("torch")
    if not weights_dir.is_dir():
        msg = f"missing staged Arm A weights directory: {weights_dir}"
        raise ValueError(msg)
    stem = _ARM_A_NAMES[family]
    cfg_raw = _strip_auto_map(
        json.loads((weights_dir / "config.json").read_text(encoding="utf-8"))
    )
    cfg = getattr(tr, stem + "Config").from_dict(cfg_raw)
    target_dtype = torch.bfloat16 if dtype is None else dtype
    load_kwargs: dict[str, Any] = {
        "config": cfg,
        "dtype": target_dtype,
        "low_cpu_mem_usage": True,
        "local_files_only": True,
        "trust_remote_code": False,
    }
    if torch.cuda.is_available():
        load_kwargs["device_map"] = {"": 0}
    model = getattr(tr, stem + "ForConditionalGeneration").from_pretrained(
        weights_dir,
        **load_kwargs,
    )
    try:
        processor = load_arm_a_processor(weights_dir)
    except ValueError:
        preproc_raw = _strip_auto_map(
            json.loads(
                (weights_dir / "preprocessor_config.json").read_text(
                    encoding="utf-8"
                )
            )
        )
        img_proc = getattr(tr, stem + "ImageProcessor").from_dict(preproc_raw)
        tok = tr.AutoTokenizer.from_pretrained(
            weights_dir, local_files_only=True, trust_remote_code=False
        )
        chat_tpl = None
        for tpl_name in ("chat_template.jinja", "chat_template.json"):
            tpl_p = weights_dir / tpl_name
            if tpl_p.is_file():
                raw_tpl = tpl_p.read_text(encoding="utf-8")
                chat_tpl = (
                    json.loads(raw_tpl).get("chat_template")
                    if tpl_name.endswith(".json")
                    else raw_tpl
                )
                break
        processor = getattr(tr, stem + "Processor")(
            image_processor=img_proc,
            tokenizer=tok,
            chat_template=chat_tpl,
        )
    if lora:
        peft = import_module("peft")
        if gradient_checkpointing and hasattr(
            model, "gradient_checkpointing_enable"
        ):
            model.gradient_checkpointing_enable()
        if hasattr(model, "enable_input_require_grads"):
            model.enable_input_require_grads()
        scope = (
            r".*(?:language_model|visual)"
            if include_vision
            else r".*language_model"
        )
        adapter = peft.LoraConfig(
            r=rank,
            lora_alpha=2 * rank,
            task_type=peft.TaskType.CAUSAL_LM,
            target_modules=scope + r".*\.(?:q_proj|k_proj|v_proj|o_proj)",
        )
        model = peft.get_peft_model(model, adapter)
    return model, processor


def load_pretrained_page_detector(
    family: DetectorFamily,
    weights_dir: Path,
) -> Any:  # noqa: ANN401
    """Load a pinned COCO detector resized cleanly to 2-class system/measure."""
    tr = import_module("transformers")
    if not weights_dir.is_dir():
        msg = f"missing staged detector weights directory: {weights_dir}"
        raise ValueError(msg)
    stem = _DETECTOR_NAMES[family]
    return getattr(tr, stem + "ForObjectDetection").from_pretrained(
        weights_dir,
        num_labels=len(DETECTOR_CLASSES),
        id2label=dict(enumerate(DETECTOR_CLASSES)),
        label2id={name: i for i, name in enumerate(DETECTOR_CLASSES)},
        ignore_mismatched_sizes=True,
        local_files_only=True,
        trust_remote_code=False,
    )


def format_arm_a_chat(
    family: ArmAFamily,
    processor: Any,  # noqa: ANN401
    messages: Sequence[Mapping[str, object]],
    *,
    add_generation_prompt: bool = True,
) -> str:
    """Render a prompt without packing or Qwen3.5 thinking tokens."""
    opts = {"enable_thinking": False} if family == "qwen3_5" else {}
    return str(
        processor.apply_chat_template(
            list(messages),
            tokenize=False,
            add_generation_prompt=add_generation_prompt,
            **opts,
        )
    )


def _tiny_vlm_config(
    family: ArmAFamily,
    config_type: Any,  # noqa: ANN401
    *,
    pinned: Mapping[str, Any] | None = None,
    tokenizer: Any = None,  # noqa: ANN401
) -> Any:  # noqa: ANN401
    text: dict[str, object] = {
        "hidden_size": 64,
        "intermediate_size": 128,
        "num_hidden_layers": 2,
        "num_attention_heads": 4,
        "num_key_value_heads": 2,
        "head_dim": 16,
        "vocab_size": 128,
        "max_position_embeddings": 256,
        "use_cache": False,
        "pad_token_id": 0,
        "bos_token_id": 1,
        "eos_token_id": 2,
        "rope_parameters": {
            "rope_type": "default",
            "rope_theta": 10000.0,
            "mrope_section": [2, 3, 3],
        },
    }
    vision: dict[str, object] = {
        "hidden_size": 64,
        "intermediate_size": 128,
        "spatial_merge_size": 2,
    }
    if family == "paddleocr_vl":
        vision.update(
            patch_size=14,
            image_size=28,
            num_hidden_layers=2,
            num_attention_heads=4,
        )
    else:
        vision.update(
            depth=2,
            num_heads=4,
            patch_size=16,
            temporal_patch_size=2,
            out_hidden_size=64,
            num_position_embeddings=16,
        )
    if family == "qwen3_vl":
        vision["deepstack_visual_indexes"] = [0, 1]
    elif family == "qwen3_5":
        text.update(
            layer_types=["linear_attention", "full_attention"],
            linear_key_head_dim=16,
            linear_value_head_dim=16,
            linear_num_key_heads=2,
            linear_num_value_heads=4,
            rope_parameters={
                "rope_type": "default",
                "rope_theta": 10000.0,
                "partial_rotary_factor": 1.0,
                "mrope_section": [2, 3, 3],
            },
        )
    specials = {
        "image_token_id": _IMAGE_TOKEN,
        "video_token_id": 4,
        "vision_start_token_id": 5,
        "vision_end_token_id": 6,
    }
    if pinned is not None:
        pinned_text = pinned.get("text_config", pinned)
        text.update(
            vocab_size=max(pinned_text["vocab_size"], len(tokenizer)),
            pad_token_id=tokenizer.pad_token_id,
            bos_token_id=tokenizer.bos_token_id,
            eos_token_id=tokenizer.eos_token_id,
            max_position_embeddings=pinned_text["max_position_embeddings"],
        )
        for key in ("patch_size", "spatial_merge_size", "temporal_patch_size"):
            vision[key] = pinned["vision_config"][key]
        specials.update({key: pinned[key] for key in specials})
    return config_type(text_config=text, vision_config=vision, **specials)


def _arm_a_base(family: ArmAFamily, config: Any = None) -> Any:  # noqa: ANN401
    tr = import_module("transformers")
    stem = _ARM_A_NAMES[family]
    if config is None:
        config = _tiny_vlm_config(family, getattr(tr, stem + "Config"))
    # Native classes accept config instances without executing any Hub code.
    return getattr(tr, stem + "ForConditionalGeneration")(config)


def build_arm_a_lora_model(
    family: ArmAFamily,
    *,
    config: Any = None,  # noqa: ANN401
    rank: int = 32,
    include_vision: bool = False,
) -> Any:  # noqa: ANN401
    """Make a native VLM with LoRA; no checkpoint or remote code is fetched."""
    peft = import_module("peft")
    scope = (
        r".*(?:language_model|visual)"
        if include_vision
        else r".*language_model"
    )
    adapter = peft.LoraConfig(
        r=rank,
        lora_alpha=2 * rank,
        task_type=peft.TaskType.CAUSAL_LM,
        target_modules=scope + r".*\.(?:q_proj|k_proj|v_proj|o_proj)",
    )
    return peft.get_peft_model(_arm_a_base(family, config), adapter)


def build_page_detector(
    family: DetectorFamily,
    *,
    config: Any = None,  # noqa: ANN401
) -> Any:  # noqa: ANN401
    """Build a native two-class detector, defaulting to a tiny CPU fixture."""
    tr = import_module("transformers")
    stem = _DETECTOR_NAMES[family]
    if config is None:
        config = getattr(tr, stem + "Config")(
            backbone_config=_tiny_detector_backbone(family),
            encoder_hidden_dim=32,
            encoder_in_channels=[16, 24, 32],
            encoder_ffn_dim=64,
            encoder_attention_heads=4,
            d_model=32,
            num_queries=12,
            decoder_layers=2,
            decoder_in_channels=[32, 32, 32],
            decoder_ffn_dim=64,
            decoder_attention_heads=4,
            num_denoising=100,
            num_labels=len(DETECTOR_CLASSES),
            id2label=dict(enumerate(DETECTOR_CLASSES)),
            label2id={name: i for i, name in enumerate(DETECTOR_CLASSES)},
        )
    if config.num_labels != len(DETECTOR_CLASSES):
        msg = "W6 page detectors require exactly system and measure classes"
        raise ValueError(msg)
    return getattr(tr, stem + "ForObjectDetection")(config)


def _tiny_detector_backbone(family: DetectorFamily) -> dict[str, object]:
    common = {
        "embedding_size": 8,
        "hidden_sizes": [8, 16, 24, 32],
        "depths": [1, 1, 1, 1],
        "out_indices": [2, 3, 4],
    }
    if family == "d_fine":
        return {
            **common,
            "model_type": "hgnet_v2",
            "stem_channels": [3, 8, 8],
            "stage_in_channels": [8, 8, 16, 24],
            "stage_mid_channels": [4, 8, 12, 16],
            "stage_out_channels": [8, 16, 24, 32],
            "stage_num_blocks": [1, 1, 1, 1],
            "stage_numb_of_layers": [1, 1, 1, 1],
        }
    return {**common, "model_type": "rt_detr_resnet", "layer_type": "basic"}


def _backward(
    loss: Any,  # noqa: ANN401
    model: Any,  # noqa: ANN401
    *,
    strict_grad_check: bool = True,
) -> None:
    torch = import_module("torch")
    if not torch.isfinite(loss).item():
        msg = "non-finite training loss"
        raise RuntimeError(msg)
    loss.backward()
    if not strict_grad_check:
        return
    grads = [p.grad for p in model.parameters() if p.grad is not None]
    if not grads or not all(torch.isfinite(g).all().item() for g in grads):
        msg = "neural CPU smoke produced missing or non-finite gradients"
        raise RuntimeError(msg)
    if not any(torch.count_nonzero(g).item() for g in grads):
        msg = "neural CPU smoke produced only zero gradients"
        raise RuntimeError(msg)


def _read_crop(row: Mapping[str, object], dataset_root: Path) -> Image:
    image = import_module("PIL.Image")
    target = dataset_root / str(row["crop_relpath"])
    with target.open("rb") as stream:
        with image.open(stream) as crop:
            rgb = crop.convert("RGB")
        _drop_file_page_cache(stream.fileno())
    return rgb


def _crop_tensor(
    crop: Image,
    resize_hw: tuple[int, int] | None = None,
    *,
    pad_to_multiple: int | None = None,
) -> Any:  # noqa: ANN401
    torch = import_module("torch")
    if resize_hw is not None:
        height, width = resize_hw
        crop = crop.resize((width, height))
    if pad_to_multiple is not None and pad_to_multiple > 1:
        w, h = crop.size
        pw = (pad_to_multiple - (w % pad_to_multiple)) % pad_to_multiple
        ph = (pad_to_multiple - (h % pad_to_multiple)) % pad_to_multiple
        if pw or ph:
            image_ops = import_module("PIL.ImageOps")
            crop = image_ops.expand(
                crop, border=(0, 0, pw, ph), fill=(255, 255, 255)
            )
    return torch.from_numpy(np.array(crop)).permute(2, 0, 1).float().div(255)


def _row_coordinates(row: Mapping[str, object]) -> tuple[int, int]:
    page, system = cast("Sequence[int]", row["page_system_tuple"])
    return page, system


def _manifest_prompt(row: Mapping[str, object]) -> SystemPromptState:
    from sheet_music_omr.training.decoding import (  # noqa: PLC0415
        build_system_prompt_state,
    )

    prompt = build_system_prompt_state(_row_coordinates(row))
    if row.get("system_index") == 0:
        return prompt
    return replace(
        prompt,
        carry_in_text=str(row["carry_in_text"]),
        incoming_header_state_text=str(row["incoming_header_state_text"]),
    )


def format_arm_b_prompt_prefix(prompt_state: SystemPromptState) -> str:
    """Serialize the caller layout and both incoming states losslessly."""
    page, system = prompt_state.page_system_tuple
    return (
        f"page: {page}\nsystem: {system}\n"
        f"carry-in: {prompt_state.carry_in_text}\n"
        f"{prompt_state.incoming_header_state_text}\n"
    )


def _arm_b_train_sequence(
    tokenizer: TextScore2DomainTokenizer, row: Mapping[str, object]
) -> tuple[list[int], int]:
    prompt = [
        tokenizer.bos_token_id,
        *tokenizer.encode(format_arm_b_prompt_prefix(_manifest_prompt(row))),
        tokenizer.prompt_sep_token_id,
    ]
    return (
        [
            *prompt,
            *tokenizer.encode(str(row["target_text_v2"])),
            tokenizer.eos_token_id,
        ],
        len(prompt),
    )


def _arm_a_messages(prompt: SystemPromptState) -> list[Mapping[str, object]]:
    page, system = prompt.page_system_tuple
    text = (
        f"Transcribe page {page}, system {system} into Text Score 2.\n"
        f"Carry in: {prompt.carry_in_text}\n"
        f"Incoming header: {prompt.incoming_header_state_text}"
    )
    return [
        {
            "role": "user",
            "content": [{"type": "image"}, {"type": "text", "text": text}],
        }
    ]


def _arm_a_batch(
    processor: Any,  # noqa: ANN401
    crop: Image,
    text: str,
    model: Any,  # noqa: ANN401
) -> dict[str, Any]:
    batch = processor(images=[crop], text=[text], return_tensors="pt")
    device = next(model.parameters()).device
    return {name: tensor.to(device) for name, tensor in batch.items()}


def _disabled_ids(
    mask: Mapping[str, bool], assignments: Mapping[str, Iterable[int]]
) -> list[int]:
    return sorted(
        {
            token
            for cat in _disabled_categories(mask)
            for token in assignments[cat]
        }
    )


_MEASURE_HEADER_RE = re.compile(r"^m (\d+)\b")
_IGNORE_LABEL = -100
_BODY_FORBIDDEN_BY_CAT: dict[str, tuple[str, ...]] = {
    "clef": ("<cle",),
    "cross_staff": ("xs",),
    "grace": ("gr",),
    "hairpin": ("<cresc", "<dim(", "<hair", "rel=hair"),
    "ottava": ("<8v", "<15m", "rel=ott"),
}
_ANY_FORBIDDEN_BY_CAT: dict[str, tuple[str, ...]] = {
    "hairpin": ('"kind":"hair',),
    "ottava": ('"kind":"ott',),
}
_BPE_AUTOMATON_CACHE: dict[
    tuple[int, frozenset[str]],
    tuple[
        tuple[str, ...],
        tuple[str, ...],
        dict[str, tuple[int, ...]],
        tuple[str, ...],
        tuple[str, ...],
    ],
] = {}


def masked_measure_token_flags(
    pieces: Sequence[str],
    masked_measure_ordinals: Iterable[int],
) -> list[bool]:
    """Flag assistant token steps belonging to open_reading_masks measures."""
    masked = frozenset(masked_measure_ordinals)
    if not masked:
        return [False] * len(pieces)
    text = "".join(pieces)
    line_masked: list[bool] = []
    active = False
    for line in text.splitlines(keepends=True):
        if m := _MEASURE_HEADER_RE.match(line):
            active = int(m[1]) in masked
        line_masked.extend([active] * len(line))
    flags: list[bool] = []
    pos = 0
    for piece in pieces:
        end = pos + len(piece)
        span = line_masked[pos:end]
        flags.append(bool(span and any(span)))
        pos = end
    return flags


def _build_substring_prefix_map(
    pieces: Sequence[str],
    patterns: tuple[str, ...],
) -> tuple[tuple[str, ...], dict[str, tuple[int, ...]]]:
    if not patterns:
        return (), {}
    prefixes = sorted(
        {pat[:k] for pat in patterns for k in range(len(pat))},
        key=len,
        reverse=True,
    )
    mapping: dict[str, list[int]] = {pfx: [] for pfx in prefixes}
    for tid, piece in enumerate(pieces):
        if not piece:
            continue
        for pfx in prefixes:
            cand = pfx + piece
            if any(pat in cand for pat in patterns):
                mapping[pfx].append(tid)
    return tuple(prefixes), {k: tuple(v) for k, v in mapping.items() if v}


def _bpe_disabled_automaton(
    tokenizer: Any,  # noqa: ANN401
    disabled: frozenset[str],
) -> tuple[
    tuple[str, ...],
    tuple[str, ...],
    dict[str, tuple[int, ...]],
    tuple[str, ...],
    tuple[str, ...],
]:
    key = (id(tokenizer), disabled)
    if key in _BPE_AUTOMATON_CACHE:
        return _BPE_AUTOMATON_CACHE[key]
    pieces = tuple(
        str(tokenizer.decode([tid], clean_up_tokenization_spaces=False))
        for tid in range(len(tokenizer))
    )
    body_pats = tuple(
        pat
        for cat in sorted(disabled)
        for pat in _BODY_FORBIDDEN_BY_CAT.get(cat, ())
    )
    any_pats = tuple(
        pat
        for cat in sorted(disabled)
        for pat in _ANY_FORBIDDEN_BY_CAT.get(cat, ())
    )
    all_pfxs, all_map = _build_substring_prefix_map(
        pieces, (*body_pats, *any_pats)
    )
    res = (pieces, all_pfxs, all_map, body_pats, any_pats)
    _BPE_AUTOMATON_CACHE[key] = res
    return res


def _matching_suffix_ids(
    tail: str,
    prefixes_desc: tuple[str, ...],
    table: Mapping[str, tuple[int, ...]],
) -> set[int]:
    out: set[int] = set()
    for pfx in prefixes_desc:
        if tail.endswith(pfx) and pfx in table:
            out.update(table[pfx])
    return out


def _in_unescaped_quotes(line: str) -> bool:
    in_quotes = False
    escaped = False
    for ch in line:
        if escaped:
            escaped = False
        elif ch == "\\":
            escaped = True
        elif ch == '"':
            in_quotes = not in_quotes
    return in_quotes


def _completes_disabled_in_context(
    acc: str,
    piece: str,
    *,
    acc_has_body: bool,
    body_pats: tuple[str, ...],
    any_pats: tuple[str, ...],
) -> bool:
    tail = acc[-32:]
    window = tail + piece
    cut = len(tail)
    for pat in any_pats:
        start = max(0, cut - len(pat) + 1)
        if window.find(pat, start) != -1:
            return True
    if not body_pats:
        return False
    full = acc + piece
    base = len(acc) - cut
    for pat in body_pats:
        pos = max(0, cut - len(pat) + 1)
        while (idx := window.find(pat, pos)) != -1:
            abs_idx = base + idx
            before = full[:abs_idx]
            in_body_here = acc_has_body or ("\nm " in before)
            in_quotes_here = _in_unescaped_quotes(before.rsplit("\n", 1)[-1])
            if in_body_here and not in_quotes_here:
                return True
            pos = idx + 1
    return False


def apply_position_scoped_bpe_verovio_mask(
    logits: Any,  # noqa: ANN401
    assistant_ids: Sequence[int],
    labels_shifted: Any,  # noqa: ANN401
    mask: Mapping[str, bool],
    tokenizer: Any,  # noqa: ANN401
) -> Any:  # noqa: ANN401
    """Mask disabled-category BPE continuations across arbitrary splits."""
    disabled = frozenset(_disabled_categories(mask))
    if not disabled:
        return logits
    pieces, all_pfxs, all_map, body_pats, any_pats = _bpe_disabled_automaton(
        tokenizer, disabled
    )
    is_np = isinstance(logits, np.ndarray)
    out = logits.copy() if is_np else logits.clone()
    torch_mod = None if is_np else import_module("torch")
    limit = out.shape[-1]
    acc = ""
    in_body = False
    for step in range(out.shape[1]):
        if step > 0 and step - 1 < len(assistant_ids):
            acc += pieces[assistant_ids[step - 1]]
            if not in_body and "\nm " in acc:
                in_body = True
        cands = _matching_suffix_ids(acc[-32:], all_pfxs, all_map)
        if cands:
            tgt = (
                int(labels_shifted[0, step])
                if is_np
                else int(labels_shifted[0, step].item())
            )
            valid = sorted(
                t
                for t in cands
                if 0 <= t < limit
                and t != tgt
                and _completes_disabled_in_context(
                    acc,
                    pieces[t],
                    acc_has_body=in_body,
                    body_pats=body_pats,
                    any_pats=any_pats,
                )
            )
            if valid:
                if is_np:
                    out[:, step, valid] = -np.inf
                elif torch_mod is not None:
                    idx = torch_mod.tensor(
                        valid, dtype=torch_mod.long, device=out.device
                    )
                    out[:, step, idx] = -float("inf")
    return out


def _mask_category_targets(
    labels: Any,  # noqa: ANN401
    disabled: Sequence[int],
) -> Any:  # noqa: ANN401
    torch = import_module("torch")
    return labels.masked_fill(
        torch.isin(labels, labels.new_tensor(disabled)), -100
    )


def run_arm_a_manifest_train_step(  # noqa: PLR0913
    model: Any,  # noqa: ANN401
    processor: Any,  # noqa: ANN401
    family: ArmAFamily,
    row: Mapping[str, object],
    dataset_root: Path,
    *,
    exclusive_token_ids: Mapping[str, Iterable[int]] | None = None,
    shared_token_ids: Iterable[int] = (),
    allow_position_bpe_mask: bool = False,
    masked_measure_ordinals: Iterable[int] = (),
    backward: bool = True,
    grad_scale: float = 1.0,
    strict_grad_check: bool = True,
) -> dict[str, object]:
    """Backpropagate assistant-only loss, skipping unsafe BPE supervision."""
    mask = cast("Mapping[str, bool]", row["category_supervision_mask"])
    assignments = {
        cat: tuple(ids) for cat, ids in (exclusive_token_ids or {}).items()
    }
    eligible_static = verovio_row_eligible_for_bpe_loss(
        mask, assignments, shared_token_ids=shared_token_ids
    )
    if not eligible_static and not allow_position_bpe_mask:
        return {"loss": None, "skipped": True, "reason": "unisolated_bpe"}

    torch = import_module("torch")
    crop = _read_crop(row, dataset_root)
    messages = _arm_a_messages(_manifest_prompt(row))
    prompt = _arm_a_batch(
        processor, crop, format_arm_a_chat(family, processor, messages), model
    )
    messages.append(
        {
            "role": "assistant",
            "content": [{"type": "text", "text": str(row["target_text_v2"])}],
        }
    )
    full_text = format_arm_a_chat(
        family, processor, messages, add_generation_prompt=False
    )
    batch = _arm_a_batch(processor, crop, full_text, model)
    ids = batch["input_ids"]
    prefix_len = prompt["input_ids"].shape[-1]
    if ids.shape[-1] <= prefix_len or not torch.equal(
        ids[:, :prefix_len], prompt["input_ids"]
    ):
        msg = "Arm A chat tokens do not preserve the assistant boundary"
        raise ValueError(msg)
    labels = ids.masked_fill(batch["attention_mask"] == 0, -100)
    labels[:, :prefix_len] = -100
    asst_ids = [int(t) for t in ids[0, prefix_len:].tolist()]
    tok = getattr(processor, "tokenizer", None)
    if masked_ordinals := tuple(masked_measure_ordinals):
        pieces = [
            str(tok.decode([t], clean_up_tokenization_spaces=False))
            if tok is not None
            else chr(t)
            for t in asst_ids
        ]
        for idx, is_masked in enumerate(
            masked_measure_token_flags(pieces, masked_ordinals)
        ):
            if is_masked:
                labels[:, prefix_len + idx] = -100
    model.train()
    if disabled := _disabled_ids(mask, assignments):
        labels = _mask_category_targets(labels, disabled)
        logits = model(**batch, use_cache=False, return_dict=True).logits
        masked = apply_category_supervision_logit_mask(
            logits[:, :-1], mask, exclusive_token_ids=assignments
        )
        loss = torch.nn.functional.cross_entropy(
            masked.transpose(1, 2), labels[:, 1:]
        )
    elif not eligible_static and allow_position_bpe_mask and tok is not None:
        logits = model(**batch, use_cache=False, return_dict=True).logits
        start_step = prefix_len - 1
        sub_logits = apply_position_scoped_bpe_verovio_mask(
            logits[:, start_step:-1],
            asst_ids,
            labels[:, prefix_len:],
            mask,
            tok,
        )
        loss = torch.nn.functional.cross_entropy(
            sub_logits.transpose(1, 2), labels[:, prefix_len:]
        )
    else:
        loss = model(
            **batch, labels=labels, use_cache=False, return_dict=True
        ).loss
    if backward:
        if not (labels != _IGNORE_LABEL).any().item():
            return {
                "loss": None,
                "skipped": True,
                "reason": "all_tokens_masked",
            }
        _backward(loss * grad_scale, model, strict_grad_check=strict_grad_check)
    return {
        "loss": float(loss.detach()),
        "skipped": False,
        "input_ids_shape": list(ids.shape),
        "pixel_values_shape": list(batch["pixel_values"].shape),
    }


def run_arm_b_manifest_train_step(  # noqa: PLR0913
    model: Any,  # noqa: ANN401
    tokenizer: TextScore2DomainTokenizer,
    row: Mapping[str, object],
    dataset_root: Path,
    *,
    resize_hw: tuple[int, int] | None = None,
    pad_to_multiple: int | None = None,
    masked_measure_ordinals: Iterable[int] = (),
    backward: bool = True,
    grad_scale: float = 1.0,
    strict_grad_check: bool = True,
) -> dict[str, object]:
    """Backpropagate masked causal loss from one packaged RGB crop."""
    torch = import_module("torch")
    device = next(model.parameters()).device
    pixels = (
        _crop_tensor(
            _read_crop(row, dataset_root),
            resize_hw,
            pad_to_multiple=pad_to_multiple,
        )
        .unsqueeze(0)
        .to(device)
    )
    ids, prefix_len = _arm_b_train_sequence(tokenizer, row)
    sequence = torch.tensor([ids], device=device)
    labels = sequence[:, 1:].clone()
    labels[:, : prefix_len - 1] = -100
    if masked_ordinals := tuple(masked_measure_ordinals):
        target_ids = ids[prefix_len:-1]
        pieces = list(tokenizer.pieces(target_ids))
        for idx, is_masked in enumerate(
            masked_measure_token_flags(pieces, masked_ordinals)
        ):
            if is_masked:
                labels[:, prefix_len - 1 + idx] = -100
    model.train()
    logits = model(pixels, sequence[:, :-1])
    mask = cast("Mapping[str, bool]", row["category_supervision_mask"])
    masked = apply_category_supervision_logit_mask(logits, mask)
    disabled = _disabled_ids(mask, CATEGORY_EXCLUSIVE_TOKEN_IDS)
    check_disabled_grad = bool(disabled and backward and strict_grad_check)
    if check_disabled_grad:
        logits.retain_grad()
    if disabled:
        labels = _mask_category_targets(labels, disabled)
    loss = torch.nn.functional.cross_entropy(masked.transpose(1, 2), labels)
    if backward:
        if not (labels != _IGNORE_LABEL).any().item():
            return {
                "loss": None,
                "skipped": True,
                "reason": "all_tokens_masked",
            }
        _backward(loss * grad_scale, model, strict_grad_check=strict_grad_check)
        if (
            check_disabled_grad
            and torch.count_nonzero(logits.grad[..., disabled]).item()
        ):
            msg = "disabled Arm B category logits received a nonzero gradient"
            raise RuntimeError(msg)
    return {
        "loss": float(loss.detach()),
        "disabled_category_gradients_zero": (
            True if check_disabled_grad else None
        ),
    }


# Match the constrained decoder's protocol limits. Importing decoding here at
# module load would cycle through its dependency on TextScore2DomainTokenizer.
MAX_MEASURE_TOKENS = 192
MAX_SYSTEM_TOKENS = 1536


@dataclass(slots=True)
class _MeasureBudget:
    seed: int
    count: int | None = None
    line_start: bool = True
    pending: tuple[int, ...] = ()
    saw_space: bool = False
    last_charged: int = 0

    def _charge(self, step: int) -> None:
        if self.count is not None and step != self.last_charged:
            self.count += 1
            self.last_charged = step

    def advance(self, piece: str, step: int, limit: int) -> bool:
        """Track a measure opener even when 'm' and its number are split."""
        if not piece:
            if self.pending:
                self.pending += (step,)
            else:
                self._charge(step)
        for char in piece:
            if self.pending:
                if not self.saw_space and char == " ":
                    self.pending += (step,)
                    self.saw_space = True
                    continue
                if self.saw_space and char in "123456789":
                    self.count = self.seed if self.count is None else 0
                    self.last_charged = 0
                for marker in self.pending:
                    self._charge(marker)
                self.pending = ()
                self.saw_space = False
            elif self.line_start and char == "m":
                self.pending = (step,)
                self.line_start = False
                continue
            self._charge(step)
            if self.count is not None and self.count > limit:
                return False
            self.line_start = char == "\n"
        return self.count is None or self.count <= limit

    def can_end(self, limit: int) -> bool:
        return self.count is None or (
            self.count + len(set(self.pending).difference({self.last_charged}))
            <= limit
        )


@dataclass(frozen=True, slots=True)
class _GenerationBeam:
    ids: tuple[int, ...]
    pieces: tuple[str, ...]
    score: float
    measure: _MeasureBudget


def _ranked_next_tokens(
    scores: Any,  # noqa: ANN401
) -> Iterator[tuple[int, float]]:
    if isinstance(scores, Mapping):
        yield from (
            (token, score)
            for token, score in sorted(
                scores.items(), key=lambda item: (-item[1], item[0])
            )
            if math.isfinite(score)
        )
        return
    if hasattr(scores, "detach"):
        scores = scores.detach().float().cpu().numpy()
    values = np.asarray(scores)
    if values.ndim != 1:
        msg = "expected a vector of next-token log probabilities"
        raise ValueError(msg)
    for token in np.argsort(-values, kind="stable"):
        if math.isfinite(score := float(values[token])):
            yield int(token), score


def generate_autoregressive_candidates(  # noqa: C901, PLR0912, PLR0913
    step_logprobs_fn: Callable[[Sequence[int]], Any],
    decode_token_fn: Callable[[int], str],
    *,
    bos_token_id: int,
    eos_token_id: int,
    prompt_ids: Sequence[int] = (),
    prompt_sep_token_id: int | None = None,
    max_measure_tokens: int = MAX_MEASURE_TOKENS,
    max_system_tokens: int = MAX_SYSTEM_TOKENS,
    carry_measure_tokens: int = 0,
    beam_width: int = 2,
) -> list[BeamCandidate]:
    """Score output after BOS and the optional prompt boundary."""
    from sheet_music_omr.training.decoding import BeamCandidate  # noqa: PLC0415

    if (
        min(max_measure_tokens, max_system_tokens, beam_width) <= 0
        or not 0 <= carry_measure_tokens <= max_measure_tokens
    ):
        msg = "invalid autoregressive token budget or beam width"
        raise ValueError(msg)
    if prompt_sep_token_id in (bos_token_id, eos_token_id) or (
        prompt_ids and prompt_sep_token_id is None
    ):
        msg = "prompt tokens require a distinct separator ID"
        raise ValueError(msg)
    prefix = (bos_token_id,)
    if prompt_sep_token_id is not None:
        prefix += (*prompt_ids, prompt_sep_token_id)

    def advance(
        beam: _GenerationBeam, token: int, score: float
    ) -> _GenerationBeam | BeamCandidate:
        raw_logprob = beam.score + score
        if token == eos_token_id:
            return BeamCandidate(
                raw_logprob,
                beam.pieces,
                length_capped=not beam.measure.can_end(max_measure_tokens),
            )
        piece = decode_token_fn(token)
        pieces = (*beam.pieces, piece)
        measure = replace(beam.measure)
        step = len(pieces)
        if step > max_system_tokens or not measure.advance(
            piece, step, max_measure_tokens
        ):
            return BeamCandidate(raw_logprob, pieces, length_capped=True)
        return _GenerationBeam((*beam.ids, token), pieces, raw_logprob, measure)

    active = [
        _GenerationBeam(prefix, (), 0.0, _MeasureBudget(carry_measure_tokens))
    ]
    completed: list[BeamCandidate] = []
    cap_pruned: list[BeamCandidate] = []
    while active:
        successors = []
        for beam in active:
            kept = 0
            best_capped = None
            near_cap = (
                len(beam.pieces) + 8 >= max_system_tokens
                or (beam.measure.count or 0) + 8 >= max_measure_tokens
            )
            floor = cap_pruned[0].raw_logprob if cap_pruned else -math.inf
            for token, score in _ranked_next_tokens(step_logprobs_fn(beam.ids)):
                if token in (bos_token_id, prompt_sep_token_id):
                    continue
                if kept >= beam_width and (
                    not near_cap
                    or best_capped is not None
                    or beam.score + score <= floor
                ):
                    break
                branch = advance(beam, token, score)
                if isinstance(branch, _GenerationBeam):
                    if kept < beam_width:
                        successors.append(branch)
                        kept += 1
                elif branch.length_capped:
                    best_capped = best_capped or branch
                elif kept < beam_width:
                    completed.append(branch)
                    kept += 1
            if best_capped is not None:
                cap_pruned.append(best_capped)
        active = sorted(successors, key=lambda beam: -beam.score)[:beam_width]
        completed.sort(key=lambda beam: -beam.raw_logprob)
        del completed[beam_width:]
        cap_pruned.sort(key=lambda beam: -beam.raw_logprob)
        del cap_pruned[beam_width:]
    return sorted(
        [*completed, *cap_pruned[:1]], key=lambda beam: -beam.raw_logprob
    )[: max(beam_width, 3)]


class _TinyArmAProcessor:
    """Synthetic processor only for smoke runs without locked metadata."""

    eos_token_id = 2

    def __init__(self, family: ArmAFamily) -> None:
        self.family = family

    def apply_chat_template(
        self,
        messages: list[dict[str, Any]],
        *,
        tokenize: bool,  # noqa: ARG002
        add_generation_prompt: bool,
        enable_thinking: bool = False,  # noqa: ARG002
    ) -> str:
        prompt = messages[0]["content"][1]["text"] + "\nAssistant:\n"
        if add_generation_prompt:
            return prompt
        return (
            prompt + messages[1]["content"][0]["text"] + chr(self.eos_token_id)
        )

    def __call__(
        self,
        *,
        images: list[Image],
        text: list[str],
        return_tensors: str,  # noqa: ARG002
    ) -> dict[str, Any]:
        torch = import_module("torch")
        paddle = self.family == "paddleocr_vl"
        patch = 14 if paddle else 16
        pixels = _crop_tensor(images[0], (2 * patch, 2 * patch))
        patches = pixels.reshape(3, 2, patch, 2, patch).permute(1, 3, 0, 2, 4)
        patches = patches.reshape(4, 3, patch, patch)
        if not paddle:
            patches = patches.unsqueeze(2).expand(-1, -1, 2, -1, -1).flatten(1)
        tokens = torch.tensor(
            [[1, 5, _IMAGE_TOKEN, 6, *text[0].encode("ascii")]]
        )
        return {
            "input_ids": tokens,
            "attention_mask": torch.ones_like(tokens),
            "mm_token_type_ids": (tokens == _IMAGE_TOKEN).long(),
            ("image_grid_" + "t" + "hw"): torch.tensor([[1, 2, 2]]),
            "pixel_values": patches,
        }


def _smoke_notation_prefix(
    prompt: SystemPromptState, *, printed_meter: str | None = None
) -> str:
    page, system = prompt.page_system_tuple
    meter = printed_meter or ("3/4" if system == 1 else "2/4")
    header = (
        prompt.incoming_header_state_text
        or f"key: 0\ntime: {meter}\nclefs: G2 F4"
    )
    carry = prompt.carry_in_text or (
        '{"measure":1,"offset":"0","event_counts":{},"accidental_scope":"octave",'
        '"accidentals":[],"open":[]}'
    )
    return (
        f"format: text-score/2\n{header}\ncarry: octave\npage: {page}\n"
        f"system: {system}\ncarry-in: {carry}\nm {system}\n1.1: "
    )


def _smoke_generate(  # noqa: PLR0913
    prompt: SystemPromptState,
    encode: Callable[[str], Sequence[int]],
    decode: Callable[[int], str],
    next_logits: Callable[[Sequence[int]], Any],
    *,
    special_ids: tuple[int, int],
    carry_measure_tokens: int,
    prompt_ids: Sequence[int] = (),
    prompt_sep_token_id: int | None = None,
) -> _SmokeCandidates:
    # Tiny weights have no notation prior. Restrict the available continuations
    # to two fixture paths and score every token with the reloaded model.
    prefix = _smoke_notation_prefix(prompt)
    continuations: dict[tuple[int, ...], set[int]] = {}
    bos, eos = special_ids
    duration = "2." if "\ntime: 3/4\n" in prefix else "2"
    bodies = (f"C4/{duration}\n", f"?/{duration}\n")
    seed = (bos,)
    if prompt_sep_token_id is not None:
        seed += (*prompt_ids, prompt_sep_token_id)
    for body in bodies:
        ids = (*seed, *encode(prefix + body), eos)
        for i in range(len(seed), len(ids)):
            continuations.setdefault(ids[:i], set()).add(ids[i])

    def step(ids: Sequence[int]) -> dict[int, float]:
        available = sorted(continuations[tuple(ids)])
        scores = next_logits(ids).log_softmax(-1)[available].tolist()
        return dict(zip(available, scores, strict=True))

    candidates = generate_autoregressive_candidates(
        step,
        decode,
        bos_token_id=bos,
        eos_token_id=eos,
        prompt_ids=prompt_ids,
        prompt_sep_token_id=prompt_sep_token_id,
        carry_measure_tokens=carry_measure_tokens,
    )
    if len(candidates) != len(bodies) or any(
        c.length_capped for c in candidates
    ):
        msg = "autoregressive smoke did not produce both fixture candidates"
        raise RuntimeError(msg)
    return [
        (candidate.raw_logprob, list(cast("Iterable[str]", candidate.tokens)))
        for candidate in candidates
    ]


type _SmokeCandidates = list[tuple[float, list[str]]]
type _SmokeGenerator = Callable[[SystemPromptState, int], _SmokeCandidates]
type _SmokeRequest = Callable[[str, SystemPromptState, int], _SmokeCandidates]


def _smoke_arm_a(
    family: ArmAFamily,
    checkpoint: Path,
    crops: Mapping[tuple[int, int], Mapping[str, object]],
    root: Path,
    metadata_dir: Path | None,
) -> tuple[dict[str, object], _SmokeGenerator]:
    torch = import_module("torch")
    peft = import_module("peft")
    tr = import_module("transformers")
    processor: Any = _TinyArmAProcessor(family)
    config = None
    if metadata_dir is not None:
        processor = load_arm_a_processor(metadata_dir)
        config = _tiny_vlm_config(
            family,
            getattr(tr, _ARM_A_NAMES[family] + "Config"),
            pinned=json.loads(
                (metadata_dir / "config.json").read_text(encoding="utf-8")
            ),
            tokenizer=processor.tokenizer,
        )
    row = next(iter(crops.values()))
    seed = 42
    torch.manual_seed(seed)
    model = build_arm_a_lora_model(family, config=config, rank=4)
    optimizer = torch.optim.AdamW(
        [p for p in model.parameters() if p.requires_grad], lr=0.01
    )
    report = run_arm_a_manifest_train_step(model, processor, family, row, root)
    optimizer.step()
    model.eval()
    model.save_pretrained(
        checkpoint, safe_serialization=True, save_embedding_layers=False
    )
    torch.manual_seed(seed)
    reloaded = peft.PeftModel.from_pretrained(
        _arm_a_base(family, config), checkpoint, local_files_only=True
    ).eval()
    inputs = _arm_a_smoke_prompt(
        family,
        processor,
        _manifest_prompt(row),
        _read_crop(row, root),
        reloaded,
    )
    with torch.no_grad():
        torch.testing.assert_close(
            model(**inputs, logits_to_keep=1).logits,
            reloaded(**inputs, logits_to_keep=1).logits,
        )

    def generate(
        prompt: SystemPromptState, carry_measure_tokens: int
    ) -> _SmokeCandidates:
        inputs = _arm_a_smoke_prompt(
            family,
            processor,
            prompt,
            _read_crop(crops[prompt.page_system_tuple], root),
            reloaded,
        )

        def next_logits(ids: Sequence[int]) -> Any:  # noqa: ANN401
            batch = _extend_arm_a_prompt(inputs, ids[1:])
            return reloaded(**batch, logits_to_keep=1, use_cache=False).logits[
                0, -1
            ]

        with torch.no_grad():
            if metadata_dir is None:
                return _smoke_generate(
                    prompt,
                    lambda text: list(text.encode("ascii")),
                    chr,
                    next_logits,
                    special_ids=(1, 2),
                    carry_measure_tokens=carry_measure_tokens,
                )
            tokenizer = processor.tokenizer
            return _smoke_generate(
                prompt,
                lambda text: tokenizer.encode(text, add_special_tokens=False),
                lambda token: tokenizer.decode(
                    [token], clean_up_tokenization_spaces=False
                ),
                next_logits,
                special_ids=(
                    tokenizer.bos_token_id
                    if tokenizer.bos_token_id is not None
                    else tokenizer.pad_token_id,
                    tokenizer.eos_token_id,
                ),
                carry_measure_tokens=carry_measure_tokens,
            )

    return report | {
        "checkpoint_reloaded": True,
        "rank": 4,
        "sequence_packing": ARM_A_SEQUENCE_PACKING,
        "enable_thinking": False if family == "qwen3_5" else None,
    }, generate


def _arm_a_smoke_prompt(
    family: ArmAFamily,
    processor: Any,  # noqa: ANN401
    prompt: SystemPromptState,
    crop: Image,
    model: Any,  # noqa: ANN401
) -> dict[str, Any]:
    text = format_arm_a_chat(family, processor, _arm_a_messages(prompt))
    return _arm_a_batch(processor, crop, text, model)


def _extend_arm_a_prompt(
    batch: dict[str, Any], ids: Sequence[int]
) -> dict[str, Any]:
    torch = import_module("torch")
    return batch | {
        name: torch.cat((batch[name], batch[name].new_tensor([values])), dim=-1)
        for name, values in (
            ("input_ids", ids),
            ("attention_mask", [1] * len(ids)),
            ("mm_token_type_ids", [0] * len(ids)),
        )
        if name in batch
    }


def _smoke_arm_b(  # noqa: PLR0913
    factory: Callable[[], Any],
    checkpoint: Path,
    size: tuple[int, int],
    crops: Mapping[tuple[int, int], Mapping[str, object]],
    root: Path,
    *,
    verovio_row: Mapping[str, object],
) -> tuple[dict[str, object], _SmokeGenerator]:
    torch = import_module("torch")
    tokenizer = TextScore2DomainTokenizer()
    model = factory()
    optimizer = torch.optim.SGD(model.parameters(), lr=0.01)
    report = run_arm_b_manifest_train_step(
        model, tokenizer, verovio_row, root, resize_hw=size
    )
    optimizer.step()
    optimizer.zero_grad(set_to_none=True)
    row = crops[(1, 2)]
    continuation = run_arm_b_manifest_train_step(
        model, tokenizer, row, root, resize_hw=size
    )
    pixels = _crop_tensor(_read_crop(row, root), size).unsqueeze(0)
    sequence, _ = _arm_b_train_sequence(tokenizer, row)
    tokens = torch.tensor([sequence[:-1]])
    optimizer.step()
    torch.save(model.state_dict(), checkpoint)
    clone = factory()
    clone.load_state_dict(
        torch.load(checkpoint, map_location="cpu", weights_only=True)
    )
    model.eval()
    clone.eval()
    with torch.no_grad():
        torch.testing.assert_close(model(pixels, tokens), clone(pixels, tokens))

    def generate(
        prompt: SystemPromptState, carry_measure_tokens: int
    ) -> _SmokeCandidates:
        image = _crop_tensor(
            _read_crop(crops[prompt.page_system_tuple], root), size
        ).unsqueeze(0)

        def next_logits(ids: Sequence[int]) -> Any:  # noqa: ANN401
            tokens = torch.tensor([list(ids)])
            return clone(image, tokens)[0, -1]

        with torch.no_grad():
            return _smoke_generate(
                prompt,
                tokenizer.encode,
                lambda token: tokenizer.decode([token]),
                next_logits,
                special_ids=(tokenizer.bos_token_id, tokenizer.eos_token_id),
                carry_measure_tokens=carry_measure_tokens,
                prompt_ids=tokenizer.encode(format_arm_b_prompt_prefix(prompt)),
                prompt_sep_token_id=tokenizer.prompt_sep_token_id,
            )

    return report | {
        "checkpoint_reloaded": True,
        "continuation_loss": continuation["loss"],
    }, generate


def build_page_detector_processor(
    width: int, height: int, *, longest_edge: int = 1024
) -> Any:  # noqa: ANN401
    """Make the common native, unpadded detector image processor for a page."""
    tr = import_module("transformers")
    w32, h32 = compute_stride32_resize(width, height, longest_edge)
    return tr.RTDetrImageProcessor(
        do_resize=True, size={"height": h32, "width": w32}, do_pad=False
    )


def _write_jsonl(path: Path, rows: Iterable[Mapping[str, object]]) -> None:
    path.write_text(
        "".join(json.dumps(row) + "\n" for row in rows), encoding="utf-8"
    )


def _smoke_rendered_page(
    size: tuple[int, int], count: int, meter: str
) -> RenderedPage:
    from sheet_music_omr.synthetic import renderers  # noqa: PLC0415

    image = import_module("PIL.Image").new("RGB", size, "white")
    draw = import_module("PIL.ImageDraw").Draw(image)
    width, height = size
    systems = []
    measures = []
    for index in range(1, count + 1):
        x0, x1 = width // 8, width * 7 // 8
        top = height * index // (count + 1) - 54
        box = (x0, top, x1, top + 113)
        for staff in (top + 8, top + 64):
            for line in range(5):
                y = staff + line * 8
                draw.line((x0 + 8, y, x1 - 8, y), fill="black", width=2)
        if index == 1:
            draw.text((x0 + 10, top + 42), meter, fill="black")
        systems.append(renderers.RenderedSystem(1, index, index, index, box, 8))
        measures.append(renderers.RenderedMeasure(index, box))
    with io.BytesIO() as raw:
        image.save(raw, "PNG")
        return renderers.RenderedPage(
            raw.getvalue(),
            renderers.PageGeometry(
                width, height, tuple(systems), tuple(measures)
            ),
        )


def _smoke_rendered_variant(
    engine: str, split: str, size: tuple[int, int], count: int, meter: str
) -> RenderedVariant:
    from sheet_music_omr.model import SystemDocument  # noqa: PLC0415
    from sheet_music_omr.parser import parse_document  # noqa: PLC0415
    from sheet_music_omr.synthetic import renderers  # noqa: PLC0415
    from sheet_music_omr.training import decoding  # noqa: PLC0415
    from sheet_music_omr.training.packaging import RenderedVariant  # noqa: PLC0415

    targets = []
    previous = None
    duration = "2." if meter == "3/4" else "2"
    for index in range(1, count + 1):
        prompt = decoding.build_system_prompt_state((1, index), previous)
        target = (
            _smoke_notation_prefix(prompt, printed_meter=meter)
            + f"E4/{duration}\n"
        )
        doc = parse_document(target)
        if not isinstance(doc, SystemDocument):
            msg = "smoke target did not parse as a system"
            raise TypeError(msg)
        previous = doc
        targets.append(target)
    variant_id = f"smoke-{engine}"
    return RenderedVariant(
        variant_id,
        {
            "source_id": variant_id,
            "work_group_id": variant_id,
            "canonical_composer": variant_id,
            "split": split,
            "rights_variant": "permissive",
            "measure_count": count,
        },
        42,
        "G1",
        renderers.RenderResult(
            engine,
            renderers.RenderGate("passed"),
            (_smoke_rendered_page(size, count, meter),),
            tuple(targets),
        ),
    )


def _package_smoke_dataset(run_dir: Path) -> None:
    from unittest.mock import patch  # noqa: PLC0415

    from sheet_music_omr.training import ledgers, packaging  # noqa: PLC0415

    variants = (
        _smoke_rendered_variant("musescore", "synth_val", (460, 637), 2, "3/4"),
        _smoke_rendered_variant("verovio", "synth_train", (300, 512), 1, "2/4"),
    )

    # This dedicated fixture process must not open or bind the host campaign.
    # The packager still checks paths, disk usage, and all dataset contracts.
    @contextmanager
    def fixture_disk_lock(_downloads: Path, runs: Path) -> Iterator[Path]:
        with ledgers._exclusive_file_lock(runs / ".smoke-disk.lock"):  # noqa: SLF001
            yield runs

    with patch.object(packaging, "w6_disk_lock", fixture_disk_lock):
        packaging.package_rendered_variants_to_datasets(
            variants,
            run_dir,
            target_train_crops=1,
            target_val_crops=2,
            prefix_train_crops=1,
            prefix_val_crops=2,
        )


def _write_smoke_dataset(work_dir: Path) -> None:
    runner = (
        "import sys; from pathlib import Path; "
        "from sheet_music_omr.training.models import _package_smoke_dataset; "
        "_package_smoke_dataset(Path(sys.argv[1]))"
    )
    subprocess.run(  # noqa: S603
        [sys.executable, "-c", runner, str(work_dir / _SMOKE_RUN)],
        check=True,
        timeout=120,
    )


type _DetectorBatch = tuple[str, Any, int, int]


def _detector_batch(row: Mapping[str, object], root: Path) -> _DetectorBatch:
    image = import_module("PIL.Image")
    width = _manifest_dimension(row, "page_width_px")
    height = _manifest_dimension(row, "page_height_px")
    path = _packaged_png(
        root, row["page_relpath"], row["sha256"], (width, height)
    )
    with image.open(path) as png:
        batch = build_page_detector_processor(width, height)(
            images=png.convert("RGB"),
            annotations=row["annotations"],
            return_tensors="pt",
        )
    return str(row["page_id"]), batch, width, height


def _smoke_detector(
    family: DetectorFamily,
    checkpoint_path: Path,
    pages: Sequence[_DetectorBatch],
) -> list[dict[str, object]]:
    torch = import_module("torch")
    model = build_page_detector(family).train()
    optimizer = torch.optim.AdamW(model.parameters(), lr=0.001)
    keys = ["loss_vfl_dn_0", "loss_bbox_dn_0", "loss_giou_dn_0"]
    if family == "d_fine":
        keys.append("loss_fgl_dn_0")
    result: list[dict[str, object]] = []
    for _, batch, width, height in pages:
        w32, h32 = compute_stride32_resize(width, height)
        boxes = batch.labels[0]["boxes"]
        if (
            batch.pixel_values.shape[-2:] != (h32, w32)
            or not len(boxes)
            or not ((boxes >= 0) & (boxes <= 1)).all().item()
        ):
            msg = "RTDetrImageProcessor changed the unpadded target frame"
            raise RuntimeError(msg)
        optimizer.zero_grad(set_to_none=True)
        output = model(
            pixel_values=batch.pixel_values,
            labels=batch.labels,
            return_dict=True,
        )
        loss_dict = output.loss_dict
        if not isinstance(loss_dict, dict) or not all(
            k in loss_dict and torch.isfinite(loss_dict[k]).item() for k in keys
        ):
            msg = f"{family}: missing or non-finite denoising losses {keys}"
            raise RuntimeError(msg)
        _backward(output.loss, model)
        optimizer.step()
        result.append(
            {
                "input_size": [width, height],
                "resized_size": [w32, h32],
                "loss": float(output.loss.detach()),
                "denoising_losses": {
                    k: float(loss_dict[k].detach()) for k in keys
                },
            }
        )
    torch.save(model.state_dict(), checkpoint_path)
    clone = build_page_detector(family)
    clone.load_state_dict(
        torch.load(checkpoint_path, map_location="cpu", weights_only=True)
    )
    model.eval()
    clone.eval()
    with torch.no_grad():
        for report, (page_id, batch, width, height) in zip(
            result, pages, strict=True
        ):
            output = model(pixel_values=batch.pixel_values, return_dict=True)
            reloaded = clone(pixel_values=batch.pixel_values, return_dict=True)
            torch.testing.assert_close(output.logits, reloaded.logits)
            torch.testing.assert_close(output.pred_boxes, reloaded.pred_boxes)
            predictions = postprocess_detector_raw_outputs(
                reloaded.logits[0].numpy(),
                reloaded.pred_boxes[0].numpy(),
                width,
                height,
                page_id=page_id,
            )
            if {p.class_id for p in predictions} != {0, 1}:
                msg = f"{family}: raw postprocessing lost a detector class"
                raise RuntimeError(msg)
            report.update(
                checkpoint_reloaded=True,
                eval_inference_verified=True,
                prediction_counts={
                    name: sum(p.class_name == name for p in predictions)
                    for name in DETECTOR_CLASSES
                },
            )
    return result


def _smoke_sidecars(
    run_id: str,
    generate: Callable[[int, SystemPromptState, int], list[BeamCandidate]],
    system_boxes: Mapping[tuple[int, int], Box],
) -> list[dict[str, object]]:
    from sheet_music_omr.training import decoding  # noqa: PLC0415

    piece = decoding.decode_piece_systems_blind(
        list(system_boxes), generate, expected_systems=len(system_boxes)
    )
    scores = [system.raw_logprob for system in piece.systems if system.valid]
    if not piece.gate_eligible or len(scores) != len(system_boxes):
        msg = f"{run_id}: constrained decoding did not complete the smoke piece"
        raise RuntimeError(msg)
    calibration = decoding.fit_nested_calibrators(
        {
            fold.fold: [
                decoding.CalibrationPrediction(score - delta, has_error)
                for score in (-1600.0, -1200.0, -800.0, -400.0)
                for delta, has_error in ((400, True), (0, False))
            ]
            for fold in decoding.TRAINDEV_FOLDS_4
        }
    )
    calibrator = calibration.by_fold[0]["overall"]
    rows = []
    for (page, system), bbox, result, score in zip(
        system_boxes, system_boxes.values(), piece.systems, scores, strict=True
    ):
        confidence = calibrator.predict(score) if score is not None else None
        if confidence is None:
            msg = f"{run_id}: missing calibrated smoke confidence"
            raise RuntimeError(msg)
        rows.extend(
            decoding.build_w7_sidecar_rows(
                result,
                run_id=run_id,
                sample_or_piece_id="smoke",
                split="synth_val",
                page_index=page,
                system_index=system,
                page_system_bbox_px=bbox,
                confidences={(1, 1): decoding.SidecarConfidence(confidence)},
            )
        )
    for row in rows:
        decoding.validate_w7_sidecar_row(row)
    return rows


def _smoke_aligned_discrepancies() -> list[dict[str, object]]:
    from sheet_music_omr.training.decoding import (  # noqa: PLC0415
        emit_aligned_discrepancy_rows,
    )

    # This independent scorer-row fixture uses a real-split key, not real data.
    row: dict[str, object] = {
        "schema": "omr-discrepancy-log-v1",
        "run_id": "w6-smoke-alignment-fixture",
        "reader_id": "w6",
        "split": "TRAIN_DEV",
        "gold_id": "gold-07",
        "group": "G5",
        "page_index": 1,
        "system_index": 1,
        "measure_ordinal": 1,
        "draft_measure_ordinal": 1,
        "insbar_anchor_draft_ordinal": None,
        "delbar_attributed_gold_ordinal": None,
        "parse_valid": True,
        "capability_excluded_categories": [],
        "open_reading_masked_categories": [],
        "converged_aspects": [],
        "layer0_categories": {},
        "layer1_a_categories": {},
        "layer2_invocab_categories": {},
        "layout_c_count": 0,
        "raw_confidence": None,
        "calibrated_confidence": None,
        "second_reader_disagreement_count": None,
        "second_reader_score": None,
        "flagged_at_20pct": False,
        "archive_round_counts": [],
    }
    return emit_aligned_discrepancy_rows(
        [row], split="TRAIN_DEV", sample_or_piece_id="gold-07"
    )


_SMOKE_RUN = Path("runs/w6-slice-a-smoke")
_SMOKE_DATASET = _SMOKE_RUN / "datasets" / "fixture-crops-full"


def _smoke_models(
    work_dir: Path, meta_root: Path | None
) -> tuple[dict[str, object], dict[str, _SmokeGenerator]]:
    from sheet_music_omr.training.asset_locks import (  # noqa: PLC0415
        W6_MODEL_METADATA_LOCKS,
    )

    train_dir = work_dir / _SMOKE_DATASET / "synth_train"
    val_dir = work_dir / _SMOKE_DATASET / "synth_val"
    root = val_dir.parent.parent
    crop_rows = load_packaged_crop_manifest(val_dir)
    page_rows = [
        *load_packaged_page_boxes_manifest(val_dir),
        *load_packaged_page_boxes_manifest(train_dir),
    ]
    crops = {_row_coordinates(row): row for row in crop_rows}
    slugs = {lock.repo_id: lock.slug for lock in W6_MODEL_METADATA_LOCKS}
    arm_a = {}
    processors = {}
    generators: dict[str, _SmokeGenerator] = {}
    for family, repo in _ARM_A_REPOS.items():
        metadata_dir = None if meta_root is None else meta_root / slugs[repo]
        arm_a[family], generators[family] = _smoke_arm_a(
            family, work_dir / family, crops, root, metadata_dir
        )
        if metadata_dir is not None:
            processors[family] = {
                key: arm_a[family][key]
                for key in ("input_ids_shape", "pixel_values_shape")
            }
    (verovio_row,) = load_packaged_crop_manifest(train_dir)
    arm_b = _smoke_arm_b_models(work_dir, crops, verovio_row, generators, root)
    pages = [_detector_batch(row, root) for row in page_rows]
    detectors = {
        family: _smoke_detector(family, work_dir / f"{family}.pt", pages)
        for family in _DETECTOR_NAMES
    }
    return {
        "arm_a": arm_a,
        "arm_b": arm_b,
        "detectors": detectors,
        "processors": processors,
        "processor_verified": meta_root is not None,
    }, generators


def _smoke_arm_b_models(
    work_dir: Path,
    crops: Mapping[tuple[int, int], Mapping[str, object]],
    verovio_row: Mapping[str, object],
    generators: dict[str, _SmokeGenerator],
    root: Path,
) -> dict[str, object]:
    classes = _neural_arm_b_classes()
    spec = ArmBDecoderConfig(layers=2, d_model=64, nhead=4, d_ff=128)
    families = (
        (
            "convnext_tiny",
            "convnext-tiny.pt",
            lambda: classes["ConvNeXtStride8And16EncoderDecoder"](decoder=spec),
            (64, 96),
        ),
        (
            "monkeyocr_v2",
            "monkeyocr-v2.pt",
            lambda: classes["MonkeyOCRv2EncoderDecoder"](
                vision=classes["MonkeyOCRv2VisionTrunk"](
                    embed_dim=64,
                    num_hidden_layers=2,
                    num_attention_heads=4,
                    intermediate_size=128,
                ),
                decoder=spec,
            ),
            (56, 84),
        ),
    )
    reports: dict[str, object] = {}
    for family, checkpoint, factory, size in families:
        reports[family], generators[family] = _smoke_arm_b(
            factory,
            work_dir / checkpoint,
            size,
            crops,
            root,
            verovio_row=verovio_row,
        )
    return reports


@contextmanager
def _native_smoke_models(
    work_dir: Path, meta_root: Path | None
) -> Iterator[tuple[dict[str, object], _SmokeRequest]]:
    torch = import_module("torch")
    threads = torch.get_num_threads()
    torch.set_num_threads(2)
    try:
        with torch.random.fork_rng(devices=[]):
            torch.manual_seed(42)
            report, generators = _smoke_models(work_dir, meta_root)

            def request(
                run_id: str,
                prompt: SystemPromptState,
                carry_measure_tokens: int,
            ) -> _SmokeCandidates:
                return generators[run_id](prompt, carry_measure_tokens)

            yield report, request
    finally:
        torch.set_num_threads(threads)


def _run_neural_smoke_payload(
    work_dir: Path,
    meta_root: Path | None = None,
    *,
    venv_python: Path | None = None,
) -> dict[str, object]:
    """Run blind decoding over native CPU model candidates and emit W7 rows."""
    from sheet_music_omr.training import decoding  # noqa: PLC0415

    _write_smoke_dataset(work_dir)
    split_dir = work_dir / _SMOKE_DATASET / "synth_val"
    targets = {
        row["page_id"]: cast("dict[int, list[DetectorTarget]]", row["targets"])
        for row in load_packaged_page_boxes_manifest(split_dir)
    }
    system_boxes: dict[tuple[int, int], Box] = {}
    for row in load_packaged_crop_manifest(split_dir):
        page, system = cast("list[int]", row["page_system_tuple"])
        system_boxes[page, system] = targets[row["page_id"]][0][system - 1].bbox
    models = (
        _native_smoke_models(work_dir, meta_root)
        if venv_python is None
        else _isolated_smoke_models(venv_python, work_dir, meta_root)
    )
    rows = []
    with models as (report, request):
        run_ids = [
            run_id
            for arm in ("arm_a", "arm_b")
            for run_id in cast("dict[str, object]", report[arm])
        ]
        for run_id in run_ids:

            def generate(
                _index: int,
                prompt: SystemPromptState,
                carry_measure_tokens: int,
                *,
                family: str = run_id,
            ) -> list[BeamCandidate]:
                return [
                    decoding.BeamCandidate(score, tokens)
                    for score, tokens in request(
                        family, prompt, carry_measure_tokens
                    )
                ]

            rows.extend(_smoke_sidecars(run_id, generate, system_boxes))
    _write_jsonl(work_dir / "w7-sidecar.jsonl", rows)
    _write_jsonl(
        work_dir / "w7-discrepancies.jsonl", _smoke_aligned_discrepancies()
    )
    return report | {
        "status": "success",
        "schema": "omr-w6-neural-cpu-smoke-v1",
        "manifest_loader_verified": True,
        "constrained_decode_verified": True,
        "w7_sidecar_verified": True,
    }


def _smoke_worker(work_dir: Path, meta_root: Path | None) -> None:
    from sheet_music_omr.training.decoding import SystemPromptState  # noqa: PLC0415

    protocol = sys.stdout
    with (
        redirect_stdout(sys.stderr),
        _native_smoke_models(work_dir, meta_root) as (
            report,
            generate,
        ),
    ):
        protocol.write(json.dumps(report) + "\n")
        protocol.flush()
        for line in sys.stdin:
            family, state, carry_measure_tokens = json.loads(line)
            state["page_system_tuple"] = tuple(state["page_system_tuple"])
            protocol.write(
                json.dumps(
                    generate(
                        family, SystemPromptState(**state), carry_measure_tokens
                    )
                )
                + "\n"
            )
            protocol.flush()


@contextmanager
def _isolated_smoke_models(
    venv_python: Path, work_dir: Path, meta_root: Path | None
) -> Iterator[tuple[dict[str, object], _SmokeRequest]]:
    # Prefer the locked neural dependencies. The repository environment is
    # only a fallback for imports from the shared decoding module.
    package_src = Path(__file__).absolute().parents[2]
    runner = (
        "import sys; from pathlib import Path; "
        "from sheet_music_omr.training.models import _smoke_worker; "
        "sys.path.append(sys.argv[2]); "
        "meta_root = Path(sys.argv[3]) if len(sys.argv) > 3 else None; "
        "_smoke_worker(Path(sys.argv[1]), meta_root)"
    )
    env = {
        **os.environ,
        "PYTHONPATH": str(package_src),
        "PYTHONNOUSERSITE": "1",
        "PYTHONDONTWRITEBYTECODE": "1",
        "HF_HUB_OFFLINE": "1",
        "TRANSFORMERS_OFFLINE": "1",
        "PIP_NO_INDEX": "1",
        "UV_OFFLINE": "1",
        "CUDA_VISIBLE_DEVICES": "",
    }
    args = [
        str(venv_python.absolute()),
        "-c",
        runner,
        str(work_dir.absolute()),
        get_path("purelib"),
    ]
    if meta_root is not None:
        args.append(str(meta_root.absolute()))
    with subprocess.Popen(  # noqa: S603
        args,
        stdin=subprocess.PIPE,
        stdout=subprocess.PIPE,
        text=True,
        env=env,
    ) as proc:
        stdin, stdout = proc.stdin, proc.stdout
        if stdin is None or stdout is None:
            msg = "missing neural CPU smoke worker pipes"
            raise RuntimeError(msg)

        def receive() -> Any:  # noqa: ANN401
            line = stdout.readline()
            if not line:
                msg = (
                    "neural CPU smoke worker exited or timed out "
                    f"({proc.wait()})"
                )
                raise RuntimeError(msg)
            return json.loads(line)

        def request(
            family: str, prompt: SystemPromptState, carry_measure_tokens: int
        ) -> _SmokeCandidates:
            stdin.write(
                json.dumps((family, asdict(prompt), carry_measure_tokens))
                + "\n"
            )
            stdin.flush()
            return cast("_SmokeCandidates", receive())

        deadline = Timer(300, proc.kill)
        deadline.start()
        try:
            yield cast("dict[str, object]", receive()), request
            stdin.close()
            if proc.wait():
                msg = f"neural CPU smoke worker failed ({proc.returncode})"
                raise RuntimeError(msg)
        finally:
            deadline.cancel()
            proc.kill()


def run_w6_neural_cpu_smoke_in_venv(
    venv_python: Path, work_dir: Path, meta_root: Path | None = None
) -> dict[str, object]:
    """Run offline synthetic fixtures in the isolated neural interpreter."""
    return _run_neural_smoke_payload(
        work_dir, meta_root, venv_python=venv_python
    )


def _verify_smoke_metadata(meta_root: Path) -> None:
    from sheet_music_omr.training.asset_locks import (  # noqa: PLC0415
        W6_MODEL_METADATA_LOCKS,
        verify_lock_licenses,
    )

    verify_lock_licenses()
    for model in W6_MODEL_METADATA_LOCKS:
        for asset in model.files:
            path = meta_root / model.slug / asset.filename
            if not path.is_file() or path.stat().st_size != asset.size:
                msg = (
                    "missing or drifted W6 metadata: "
                    f"{model.slug}/{asset.filename}"
                )
                raise ValueError(msg)
            with path.open("rb") as stream:
                sha256 = hashlib.file_digest(stream, "sha256").hexdigest()
            if sha256 != asset.sha256:
                msg = f"drifted W6 metadata: {model.slug}/{asset.filename}"
                raise ValueError(msg)


def run_w6_neural_cpu_smoke(
    venv_python: Path | None, work_dir: Path, meta_root: Path | None = None
) -> dict[str, object]:
    """Check optional locked metadata and run the complete CPU smoke suite."""
    if meta_root is not None:
        _verify_smoke_metadata(meta_root)
    if venv_python is not None:
        return run_w6_neural_cpu_smoke_in_venv(venv_python, work_dir, meta_root)
    return _run_neural_smoke_payload(work_dir, meta_root)
