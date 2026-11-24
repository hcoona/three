"""Bounded W4 synthetic proof and W5 detector evaluation commands."""

from __future__ import annotations

import argparse
import contextlib
import io
import json
import math
import platform
import random
import re
import resource
import shutil
import subprocess
import sys
import tarfile
import time
import zipfile
from collections import Counter
from dataclasses import asdict, dataclass, field, replace
from importlib.metadata import version
from itertools import chain
from pathlib import Path
from typing import TYPE_CHECKING, Any, cast
from uuid import uuid4

from PIL import Image

from sheet_music_omr.assembly import AssemblyStatus, assemble
from sheet_music_omr.comparison import ScoreParseError
from sheet_music_omr.diagnostics import TextScoreError
from sheet_music_omr.evaluation.alignment import (
    EXPECTED_PAGE_SIZE,
    TEST_GOLD_IDS,
    check_no_symlink_under,
    sha256_bytes,
    sha256_file,
)
from sheet_music_omr.evaluation.w5_detectors import (
    detect_grand_staff_systems_blind,
    evaluate_no_train_decision,
    match_page_boxes,
    run_copisteria_front_detector,
    run_homr_cli_detector,
    summarize_page_matches,
    validate_candidate_system_assembly,
    verify_copisteria_live_assets,
    verify_w3_snapshot_attestation,
)
from sheet_music_omr.evaluation.wheel_lock import COPISTERIA_COMMIT
from sheet_music_omr.harness import privacy
from sheet_music_omr.harness.confine import Confinement
from sheet_music_omr.harness.corpora import CorpusError
from sheet_music_omr.harness.musescore import (
    MuseScore,
    has_source_verified_defaults,
)
from sheet_music_omr.harness.records import (
    check_outside_repository,
    dumps,
    read_object,
    record_stop,
    repository_root,
    run_git,
    source_state,
)
from sheet_music_omr.harness.stops import StopConditionError
from sheet_music_omr.model import (
    AssembledScore,
    CarryMode,
    EndpointRole,
    Note,
    SlurRelation,
    SystemDocument,
)
from sheet_music_omr.musicxml_forward import to_musicxml
from sheet_music_omr.musicxml_reverse import MusicXmlError, from_musicxml
from sheet_music_omr.parser import parse_document
from sheet_music_omr.semantics import analyze, validate_document
from sheet_music_omr.supplementary import compare_file
from sheet_music_omr.synthetic.degrade import (
    apply_tier_r_degradation,
    compare_crop_statistics,
    compute_crop_statistics,
    crop_tier_r_systems,
    render_warped_overlay,
)
from sheet_music_omr.synthetic.injector import (
    has_unchecked_verovio_supplementary_facet,
    inject_annotations,
)
from sheet_music_omr.synthetic.intake import (
    TestExclusionGate,
    _coverage,
    intake_sources,
    iter_lieder_candidates,
    iter_pdmx_candidates,
)
from sheet_music_omr.synthetic.ledgers import (
    ACCEPTED_MUSESCORE_IDENTITIES,
    RESVG_VERSION,
    RESVG_WHEEL_SHA256,
    VEROVIO_VERSION,
    VEROVIO_WHEEL_SHA256,
    W4_W5_MAX_PAGES_PER_RUN,
    WORKER_SCRATCH_HEADROOM_BYTES,
    W4W5RunLedger,
    check_metered_proof_disk_cap,
    check_metered_proof_disk_cap_unlocked,
    proof_disk_lock,
    sanitize_subprocess_output,
    setup_w4_w5_environments,
    verify_loopback_only_unshare,
    verify_w4_w5_live_assets,
)
from sheet_music_omr.synthetic.renderers import (
    RenderGate,
    RenderGateLedger,
    calibrate_musescore_pixel_scale,
    render_musescore,
    render_synthetic_probe_png,
    render_verovio,
    sample_layout_and_style,
)
from sheet_music_omr.writer import write_document

if TYPE_CHECKING:
    from collections.abc import Generator, Iterator, Sequence

    from sheet_music_omr.evaluation.w5_detectors import PageMatchReport
    from sheet_music_omr.synthetic.degrade import CropStatistics
    from sheet_music_omr.synthetic.injector import InjectionResult
    from sheet_music_omr.synthetic.intake import SourceCandidate
    from sheet_music_omr.synthetic.renderers import RenderResult

type Json = dict[str, Any]
type Box = tuple[float, float, float, float]

_TRAIN_PAGES = 69
_LOCKED_PAGES = 92
_TRAIN_SYSTEMS = 323
_LOCKED_SYSTEMS = 434
_MIN_ROUNDTRIP = 50
_MIN_TIMING = 100
_OVERLAY_PAGES = 50
_GROUPS = tuple(f"G{n}" for n in range(1, 8))
_PERMISSIVE = Path("synthetic") / "permissive"
_INTAKE_REPORT = "w4a-intake-report"


def _integer(value: str, minimum: int, maximum: int) -> int:
    try:
        number = int(value)
        if minimum <= number <= maximum:
            return number
    except ValueError:
        pass
    msg = f"expected an integer in {minimum}..{maximum}"
    raise argparse.ArgumentTypeError(msg)


def _parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(prog="python -m sheet_music_omr.synthetic")
    commands = parser.add_subparsers(dest="command", required=True)
    setup = commands.add_parser("setup-w4-w5")
    setup.add_argument("--downloads", type=Path, required=True)
    setup.add_argument("--no-homr", action="store_true")
    intake = commands.add_parser("intake-w4a")
    s3 = commands.add_parser("run-s3")
    w5 = commands.add_parser("run-w5-detectors")
    for command in (intake, s3, w5):
        command.add_argument("--run-dir", type=Path, required=True)
        command.add_argument("--downloads", type=Path, required=True)
        command.add_argument("--w3-run-dir", type=Path, required=True)
        command.add_argument(
            "--gold-status", type=Path, required=command is intake
        )
        command.add_argument(
            "--gold-cache", type=Path, required=command is intake
        )
    for command in (intake, s3):
        command.add_argument("--musescore", type=Path)
        command.add_argument("--settings", type=Path)
    for command in (s3, w5):
        command.add_argument("--formal", action="store_true")
    intake.add_argument(
        "--rights-variant", choices=("permissive",), default="permissive"
    )
    intake.add_argument(
        "--max-pdmx", type=lambda v: _integer(v, 0, sys.maxsize), default=380
    )
    intake.add_argument(
        "--max-lieder", type=lambda v: _integer(v, 0, sys.maxsize), default=80
    )
    s3.add_argument(
        "--roundtrip-pieces",
        type=lambda v: _integer(v, 1, W4_W5_MAX_PAGES_PER_RUN),
        default=55,
    )
    s3.add_argument(
        "--timing-pages",
        type=lambda v: _integer(v, 1, W4_W5_MAX_PAGES_PER_RUN),
        default=105,
    )
    s3.add_argument("--seed", default="w4-s3-v1")
    w5.add_argument(
        "--mode", choices=("traindev", "locked92"), default="traindev"
    )
    w5.add_argument("--no-homr", action="store_true")
    validation = commands.add_parser("validate-assembly")
    validation.add_argument(
        "--expected-systems",
        type=lambda v: _integer(v, 1, sys.maxsize),
        required=True,
    )
    validation.add_argument("files", nargs="+", type=Path, metavar="FILE")
    return parser


def _bounded_path(root: Path, relative: Path) -> Path:
    target = root / relative
    check_no_symlink_under(target, root)
    return target


def _outside(path: Path, repo_root: Path) -> Path:
    if path.is_symlink() or path.resolve().is_relative_to(repo_root):
        msg = (
            "the run and download directories must be outside the repository "
            "and not symlinks"
        )
        raise StopConditionError(msg)
    check_outside_repository(path)
    return path.resolve()


def _private(conf: Confinement, args: argparse.Namespace) -> tuple[str, ...]:
    return privacy.private_strings(
        conf,
        (
            str(path.resolve())
            for name in (
                "w3_run_dir",
                "gold_status",
                "gold_cache",
                "musescore",
                "settings",
            )
            if (path := getattr(args, name, None)) is not None
        ),
    )


def _write_report(
    conf: Confinement,
    name: str,
    report: Json,
    markdown: str,
    private: Sequence[str],
) -> None:
    rendered = dumps(report)
    privacy.scan(rendered, private)
    privacy.scan(markdown, private)
    files = {
        _bounded_path(
            conf.run, Path("report") / f"{name}.{suffix}"
        ): content.encode()
        for suffix, content in (("json", rendered), ("md", markdown))
    }
    size = sum(map(len, files.values()))
    with proof_disk_lock(conf.run.parent):
        _check_disk_unlocked(conf, size)
        for target, content in files.items():
            conf.write_bytes(target, content)


def _record_environment(
    conf: Confinement, environment: Json, private: Sequence[str]
) -> None:
    record = dumps(environment)
    privacy.scan(record, private)
    with proof_disk_lock(conf.run.parent):
        _check_disk_unlocked(conf, len(record.encode()))
        conf.write_text(
            _bounded_path(conf.run, Path("environment.json")), record
        )


def _workspace_versions() -> dict[str, str]:
    return {"python": platform.python_version()} | {
        package: version(package)
        for package in ("music21", "musicdiff", "numpy", "pillow")
    }


def _w5_environment(conf: Confinement, *, homr_enabled: bool) -> Json:
    copista_prov_sha = verify_copisteria_live_assets(conf.downloads)
    record: Json = {
        "workspace": _workspace_versions(),
        "copisteria_commit": COPISTERIA_COMMIT,
        "copisteria_provenance_sha256": copista_prov_sha,
        "homr_enabled": homr_enabled,
    }
    if homr_enabled:
        setup = verify_w4_w5_live_assets(conf.downloads, require_homr=True)
        record["homr"] = setup["homr"]
    return record


def _check_disk_unlocked(conf: Confinement, additional_bytes: int = 0) -> None:
    check_metered_proof_disk_cap_unlocked(
        conf.run.parent,
        additional_bytes=additional_bytes + WORKER_SCRATCH_HEADROOM_BYTES,
        extra_dir=conf.run,
    )


def _check_disk(conf: Confinement, additional_bytes: int = 0) -> None:
    check_metered_proof_disk_cap(
        conf.run.parent,
        additional_bytes=additional_bytes + WORKER_SCRATCH_HEADROOM_BYTES,
        extra_dir=conf.run,
    )


def _rows(path: Path) -> list[Json]:
    rows = [
        json.loads(line)
        for line in path.read_text("utf-8").splitlines()
        if line.strip()
    ]
    if any(not isinstance(row, dict) for row in rows):
        msg = "invalid JSONL manifest row"
        raise StopConditionError(msg)
    return rows


def _w3_pieces(w3: Path) -> list[Json]:
    manifest = read_object(_bounded_path(w3, Path("alignment/manifest.json")))
    pieces = manifest["pieces"]
    if not isinstance(pieces, list) or not all(
        isinstance(p, dict) for p in pieces
    ):
        msg = "invalid W3 manifest piece inventory"
        raise StopConditionError(msg)
    return cast("list[Json]", pieces)


def _train_pieces(w3: Path) -> list[Json]:
    pieces = [
        piece for piece in _w3_pieces(w3) if piece["split"] == "TRAIN_DEV"
    ]
    if any(_gold_id(piece) in TEST_GOLD_IDS for piece in pieces):
        msg = "W3 TRAIN_DEV inventory contains a frozen TEST identifier"
        raise StopConditionError(msg)
    return pieces


def _slice_texts(w3: Path, piece: Json) -> tuple[str, ...]:
    gid = _gold_id(piece)
    return tuple(
        _bounded_path(w3, Path("slices") / gid / f"s{index:02d}.txt").read_text(
            "utf-8"
        )
        for index in range(1, len(piece["systems"]) + 1)
    )


def _gold_id(piece: Json) -> str:
    gid: str = piece["gold_id"]
    if not re.fullmatch(r"gold-\d{2}", gid):
        msg = "invalid W3 manifest piece identifier"
        raise StopConditionError(msg)
    return gid


def _assemble_oracle(texts: Sequence[str]) -> AssembledScore:
    result = assemble([parse_document(text) for text in texts])
    if result.status is not AssemblyStatus.SUCCESS or result.score is None:
        msg = "W3 TRAIN_DEV oracle slices did not assemble"
        raise StopConditionError(msg)
    return result.score


def _reference_rates(w3: Path) -> dict[str, float]:
    totals: Counter[str] = Counter()
    measures = 0
    for piece in _train_pieces(w3):
        score = _assemble_oracle(_slice_texts(w3, piece))
        rates, _, _ = _coverage(score, None)
        measures += len(score.blocks)
        totals.update(
            {name: rate * len(score.blocks) for name, rate in rates.items()}
        )
    if not measures:
        msg = "W3 TRAIN_DEV reference notation is missing"
        raise StopConditionError(msg)
    return {name: count / measures for name, count in totals.items()}


def _exclusion_gate(args: argparse.Namespace) -> TestExclusionGate:
    return TestExclusionGate(
        _bounded_path(args.w3_run_dir, Path("splits/test-exclusion.json")),
        args.gold_status,
        args.gold_cache,
    )


def _run_intake(conf: Confinement, args: argparse.Namespace) -> Json:
    gate = _exclusion_gate(args)
    ms: MuseScore | None = None

    def export(raw: bytes) -> bytes:
        nonlocal ms
        if ms is None:
            ms = MuseScore.open(
                conf, executable=args.musescore, settings=args.settings
            )
        with _owned_scratch(conf, "lieder-") as directory:
            input_path = conf.write_bytes(directory / "source.mscx", raw)
            output_path = directory / "source.musicxml"
            with ms.batch("w4a-lieder"):
                result = ms.convert(input_path, output_path)
            if not result.ok:
                msg = "Lieder MusicXML export failed"
                raise CorpusError(msg)
            return output_path.read_bytes()

    def _capped(
        stream: Iterator[SourceCandidate], corpus_name: str, limit: int
    ) -> Iterator[SourceCandidate]:
        if limit <= 0:
            return
        for cand in stream:
            if admitted_tracker[corpus_name] >= limit:
                return
            yield cand

    admitted_tracker: Counter[str] = Counter()
    with (
        contextlib.closing(
            cast(
                "Generator[SourceCandidate]",
                iter_pdmx_candidates(conf.downloads),
            )
        ) as pdmx,
        contextlib.closing(
            cast(
                "Generator[SourceCandidate]",
                iter_lieder_candidates(conf.downloads, export),
            )
        ) as lieder,
    ):
        result = intake_sources(
            chain(
                _capped(pdmx, "PDMX", args.max_pdmx),
                _capped(lieder, "OpenScore Lieder", args.max_lieder),
            ),
            run_dir=conf.run,
            rights_variant=args.rights_variant,
            gate=gate,
            max_per_corpus={
                "PDMX": args.max_pdmx,
                "OpenScore Lieder": args.max_lieder,
            },
            admitted_tracker=admitted_tracker,
            reference_rates=_reference_rates(args.w3_run_dir),
        )
    gate.audit_admitted_manifest(_rows(result.manifest_path))
    report: Json = read_object(result.summary_path)
    report.update(
        schema="omr-w4a-intake-report-v1",
        admitted_manifest_sha256=sha256_file(result.manifest_path),
        test_exclusion_sha256=sha256_file(
            args.w3_run_dir / "splits/test-exclusion.json"
        ),
        post_admission_test_matches=0,
    )
    if ms is not None:
        report["musescore"] = {
            "version": ms.version,
            "executable_sha256": ms.executable_sha256,
        }
    markdown = "# W4a symbolic intake\n\n" + _json_block(report)
    _write_report(conf, _INTAKE_REPORT, report, markdown, _private(conf, args))
    return {"status": "completed", "counts": result.counts}


@contextlib.contextmanager
def _owned_scratch(conf: Confinement, prefix: str) -> Iterator[Path]:
    from tempfile import TemporaryDirectory  # noqa: PLC0415

    with TemporaryDirectory(prefix=prefix, dir=conf.temp) as directory:
        yield Path(directory)


def _json_block(value: object) -> str:
    return f"```json\n{dumps(value)}```\n"


@dataclass(slots=True)
class _PageCount:
    limit: int
    actual: int = 0

    def add(self, pages: int) -> None:
        self.actual += pages
        if self.actual > self.limit:
            msg = (
                "W4/W5 page budget exhausted; return to the repository owner "
                "(#1491, #1497 or #686)"
            )
            raise StopConditionError(msg)

    @property
    def remaining(self) -> int:
        return self.limit - self.actual


def _formal_source(conf: Confinement, *, locked: bool) -> str:
    if not conf.run.name.startswith("w4-w5-"):
        msg = "formal W4/W5 run directory name must start with w4-w5-"
        raise StopConditionError(msg)
    source = source_state()
    if source.get("dirty") is not False:
        msg = "formal W4/W5 run requires a clean project code tree"
        raise StopConditionError(msg)
    commit = source["commit"]
    if not isinstance(commit, str) or not commit:
        msg = "cannot establish formal W4/W5 source commit"
        raise StopConditionError(msg)
    if locked:
        if run_git(["status", "--porcelain"]).strip():
            msg = "locked92 requires a clean repository worktree"
            raise StopConditionError(msg)
        if run_git(["rev-parse", "origin/main"]).strip() != commit:
            msg = "locked92 requires frozen origin/main"
            raise StopConditionError(msg)
    return commit


@contextlib.contextmanager
def _metered_run(
    conf: Confinement,
    command: str,
    pages: int,
    commit: str | None,
) -> Iterator[_PageCount]:
    counter = _PageCount(pages)
    if commit is None:
        yield counter
        return
    ledger = W4W5RunLedger(conf.downloads)
    reservation = ledger.reserve(
        run_id=uuid4().hex,
        command=command,
        reserved_pages=pages,
        git_commit=commit,
        clean_tree=True,
    )
    status = "failed"
    try:
        yield counter
        status = "completed"
    finally:
        ledger.settle(reservation, actual_pages=counter.actual, status=status)


@dataclass(frozen=True, slots=True)
class _DetectorPage:
    path: Path
    split: str
    group: str
    boxes: tuple[Box, ...]


def _detector_pages(w3: Path, *, locked: bool) -> list[_DetectorPage]:
    pages = []
    for piece in _w3_pieces(w3):
        split = piece["split"]
        if split != "TRAIN_DEV" and (split != "TEST" or not locked):
            continue
        gid = _gold_id(piece)
        if (gid in TEST_GOLD_IDS) != (split == "TEST"):
            msg = "W5 inputs do not match the frozen TEST identifiers"
            raise StopConditionError(msg)
        group: str = piece["group"]
        if group not in _GROUPS:
            msg = "invalid W3 book group"
            raise StopConditionError(msg)
        for page in piece["pages"]:
            index = int(page["page_index"])
            boxes = tuple(
                tuple(system["facsimile_box_px"])
                for system in piece["systems"]
                if system["page_index"] == index
            )
            path = _bounded_path(w3, Path("pages") / f"{gid}-p{index:02d}.png")
            pages.append(_DetectorPage(path, split, group, boxes))
    expected = (
        (_LOCKED_PAGES, _LOCKED_SYSTEMS)
        if locked
        else (_TRAIN_PAGES, _TRAIN_SYSTEMS)
    )
    train = [page for page in pages if page.split == "TRAIN_DEV"]
    if (
        (len(pages), sum(len(page.boxes) for page in pages)) != expected
        or (len(train), sum(len(page.boxes) for page in train))
        != (_TRAIN_PAGES, _TRAIN_SYSTEMS)
        or len({page.path for page in pages}) != len(pages)
    ):
        msg = "W5 inventory does not match the accepted page and system counts"
        raise StopConditionError(msg)
    return pages


@dataclass(slots=True)
class _DetectorTotals:
    rows: dict[str, list[PageMatchReport]] = field(default_factory=dict)
    unscorable: Counter[str] = field(default_factory=Counter)
    worker_failures: Counter[str] = field(default_factory=Counter)

    def add(self, table_keys: tuple[str, str], row: PageMatchReport) -> None:
        for key in table_keys:
            self.rows.setdefault(key, []).append(row)

    def table(self, keys: Sequence[str]) -> Json:
        table: Json = {}
        for key in keys:
            rows = self.rows.get(key, [])
            summary = summarize_page_matches(rows) if rows else None
            table[key] = {
                "scored_pages": len(rows),
                "unscorable_page_frame": self.unscorable[key],
                "worker_failures": self.worker_failures[key],
                "counts": asdict(summary) | {"n_matched": summary.n_matched}
                if summary is not None
                else None,
                "rates": _detector_rates(summary)
                if summary is not None
                else None,
            }
        return table


def _detector_rates(row: PageMatchReport) -> dict[str, float | None]:
    gold, predicted = max(1, row.n_gold), max(1, row.n_pred)
    return {
        "exact_two_staff_match": row.exact_two_staff_match / gold,
        "merged_gold": row.merged_gold / gold,
        "split_or_truncated_gold": row.split_or_truncated_gold / gold,
        "merged_preds": row.merged_preds / predicted,
        "spurious_fp": row.spurious_fp / predicted,
        "ink_clipped_matches": (
            row.ink_clipped_matches / max(1, row.n_matched)
            if row.ink_clipped_matches is not None
            else None
        ),
    }


def _benchmark(
    conf: Confinement,
    pages: Sequence[_DetectorPage],
    counter: _PageCount,
    *,
    homr_enabled: bool,
) -> dict[str, _DetectorTotals]:
    totals = {
        name: _DetectorTotals()
        for name in ("comb_dp_blind", "copisteria_front")
    }
    if homr_enabled:
        totals["homr_cli"] = _DetectorTotals()
    scratch = _bounded_path(conf.run, Path("tmp") / "w5-detectors")
    for page in pages:
        _check_disk(conf)
        with Image.open(page.path) as image:
            if image.size != EXPECTED_PAGE_SIZE:
                msg = "W5 page is outside the accepted image dimension"
                raise StopConditionError(msg)
            counter.add(1)
            keys = (page.split, page.group)
            blind = detect_grand_staff_systems_blind(image)
            copisteria = run_copisteria_front_detector(
                [page.path], conf.downloads, scratch
            )[page.path]
            totals["comb_dp_blind"].add(
                keys, match_page_boxes(blind, page.boxes, image)
            )
            totals["copisteria_front"].add(
                keys, match_page_boxes(copisteria, page.boxes, image)
            )
            if homr_enabled:
                homr = run_homr_cli_detector(
                    [page.path], conf.downloads, scratch
                )[page.path]
                result = totals["homr_cli"]
                if homr.unscorable_page_frame:
                    result.unscorable.update(keys)
                    if homr.returncode:
                        result.worker_failures.update(keys)
                else:
                    result.add(
                        keys, match_page_boxes(homr.boxes, page.boxes, image)
                    )
    return totals


def _unclosed_fault(doc: SystemDocument) -> SystemDocument | None:
    used = analyze(doc).relations.relations
    label = next(n for n in range(1, len(used) + 2) if n not in used)
    for block_index in range(len(doc.blocks) - 1, -1, -1):
        block = doc.blocks[block_index]
        for voice_index, voice in reversed(tuple(enumerate(block.voices))):
            for event_index, event in reversed(tuple(enumerate(voice.events))):
                if not isinstance(event, Note):
                    continue
                fault = replace(
                    event,
                    flags=(
                        *event.flags,
                        SlurRelation(label, EndpointRole.START),
                    ),
                )
                events = (
                    *voice.events[:event_index],
                    fault,
                    *voice.events[event_index + 1 :],
                )
                voices = (
                    *block.voices[:voice_index],
                    replace(voice, events=events),
                    *block.voices[voice_index + 1 :],
                )
                blocks = (
                    *doc.blocks[:block_index],
                    replace(block, voices=voices),
                    *doc.blocks[block_index + 1 :],
                )
                mutated = replace(doc, blocks=blocks)
                if not validate_document(mutated):
                    return mutated
    return None


def _assembly_faults(texts: Sequence[str]) -> dict[str, tuple[str, ...]]:
    first = parse_document(texts[0])
    last = parse_document(texts[-1])
    if not isinstance(first, SystemDocument) or not isinstance(
        last, SystemDocument
    ):
        msg = "W3 oracle fault fixture is not a system document"
        raise StopConditionError(msg)
    faults = {
        "missing_system": tuple(texts[:-1]),
        "wrong_tuple": (
            *texts[:-1],
            write_document(replace(last, page=last.page + 1)),
        ),
        "measure_gap": (
            *texts[:-1],
            write_document(
                replace(
                    last,
                    blocks=(
                        *last.blocks[:-1],
                        replace(
                            last.blocks[-1], number=last.blocks[-1].number + 1
                        ),
                    ),
                )
            ),
        ),
    }
    if len(texts) > 1:
        scope = (
            CarryMode.LETTER
            if last.carry is CarryMode.OCTAVE
            else CarryMode.OCTAVE
        )
        mutation = replace(
            last,
            carry=scope,
            carry_in=replace(last.carry_in, accidental_scope=scope),
        )
        if not validate_document(mutation):
            faults["carry_mismatch"] = (*texts[:-1], write_document(mutation))
    if (opened := _unclosed_fault(last)) is not None:
        faults["unclosed_spanner"] = (*texts[:-1], write_document(opened))
    return faults


def _validate_oracle(w3: Path) -> Json:
    expected_codes = {
        "missing_system": "system_sequence",
        "wrong_tuple": "system_sequence",
        "measure_gap": "semantic/measure_id",
        "carry_mismatch": "assembly/carry_in",
        "unclosed_spanner": "assembly/relation_open",
    }
    pieces = _train_pieces(w3)
    rejects: Counter[str] = Counter()
    systems = 0
    for piece in pieces:
        texts = _slice_texts(w3, piece)
        expected = tuple(
            (sy["page_index"], sy["page_system_index"])
            for sy in piece["systems"]
        )
        result = validate_candidate_system_assembly(texts, expected)
        if result.status is not AssemblyStatus.SUCCESS:
            msg = "W5 candidate assembly failed the TRAIN_DEV oracle"
            raise StopConditionError(msg)
        systems += len(texts)
        rendered = to_musicxml(_assemble_oracle(texts))
        recovered = to_musicxml(from_musicxml(rendered))
        if compare_file(
            rendered.decode("utf-8"), recovered.decode("utf-8")
        ).category_counts():
            msg = "W5 TRAIN_DEV assembly has round-trip content differences"
            raise StopConditionError(msg)
        for name, fault in _assembly_faults(texts).items():
            rejection = validate_candidate_system_assembly(fault, expected)
            if (
                rejection.status is AssemblyStatus.SUCCESS
                or expected_codes[name]
                not in {diagnostic.code for diagnostic in rejection.diagnostics}
                or rejection
                != validate_candidate_system_assembly(fault, expected)
            ):
                msg = f"W5 assembly did not reject {name} deterministically"
                raise StopConditionError(msg)
            rejects[name] += 1
    if systems != _TRAIN_SYSTEMS or set(rejects) != expected_codes.keys():
        msg = "W5 TRAIN_DEV assembly oracle or fault coverage is incomplete"
        raise StopConditionError(msg)
    return {
        "pieces_passed": len(pieces),
        "systems_passed": systems,
        "content_differences": 0,
        "roundtrip_basis": "in_memory_musicxml_from_train_dev_slices",
        "faults_rejected": dict(rejects),
    }


def _w5_markdown(report: Json) -> str:
    metrics = (
        "n_gold",
        "n_pred",
        "missed_gold",
        "merged_gold",
        "split_or_truncated_gold",
        "exact_two_staff_match",
        "exact_wrong_staff_count",
        "spurious_fp",
        "merged_preds",
        "ink_clipped_matches",
    )
    headers = (
        "Detector",
        "Partition",
        "Scored pages",
        "Unscorable pages",
        *metrics,
    )
    lines = [
        "# W5 system-detector benchmark",
        "",
        "| " + " | ".join(headers) + " |",
        "| " + " | ".join("---" for _ in headers) + " |",
    ]
    for name, partitions in report["detectors"].items():
        for partition, row in partitions.items():
            counts = row["counts"] or {}
            cells = (
                name,
                partition,
                row["scored_pages"],
                row["unscorable_page_frame"],
                *(counts.get(metric, "—") for metric in metrics),
            )
            lines.append("| " + " | ".join(map(str, cells)) + " |")
    return (
        "\n".join(lines)
        + "\n\n## Decision and assembly\n\n"
        + _json_block(
            {
                name: report[name]
                for name in ("mode", "decision", "assembly", "w3_attestation")
            }
        )
    )


def _probe_offline_homr_fixture(conf: Confinement) -> None:
    scratch = _bounded_path(conf.run, Path("tmp") / "w5-homr-probe")
    scratch.mkdir(parents=True, exist_ok=True)
    probe_png = scratch / "probe.png"
    try:
        probe_bytes = render_synthetic_probe_png(
            conf.downloads, scratch / "vrv"
        )
        conf.write_bytes(probe_png, probe_bytes)
        res = run_homr_cli_detector([probe_png], conf.downloads, scratch)[
            probe_png
        ]
    finally:
        probe_png.unlink(missing_ok=True)
        for empty_candidate in (scratch / "vrv", scratch):
            with contextlib.suppress(OSError):
                empty_candidate.rmdir()
    if res.returncode != 0 or res.position_row_count < 1:
        msg = (
            f"offline HOMR staff fixture probe failed "
            f"(rc={res.returncode}, rows={res.position_row_count})"
        )
        raise StopConditionError(msg)


def _run_w5(conf: Confinement, args: argparse.Namespace) -> Json:
    locked = args.mode == "locked92"
    if locked and not args.formal:
        msg = "locked92 requires --formal"
        raise StopConditionError(msg)
    if locked and args.no_homr:
        msg = "locked92 cannot use --no-homr; all three detectors are required"
        raise StopConditionError(msg)
    commit = _formal_source(conf, locked=locked) if args.formal else None
    verify_loopback_only_unshare()
    command = "w5-locked-92" if locked else "w5-traindev-69"
    with _metered_run(
        conf, command, _LOCKED_PAGES if locked else _TRAIN_PAGES, commit
    ) as counter:
        environment = _w5_environment(conf, homr_enabled=not args.no_homr)
        if locked:
            _probe_offline_homr_fixture(conf)
        attestation = verify_w3_snapshot_attestation(
            args.w3_run_dir, include_test_pages=locked
        )
        pages = _detector_pages(args.w3_run_dir, locked=locked)
        totals = _benchmark(conf, pages, counter, homr_enabled=not args.no_homr)
        decision = evaluate_no_train_decision(
            {
                name: summarize_page_matches(result.rows["TRAIN_DEV"])
                for name, result in totals.items()
                if result.rows.get("TRAIN_DEV")
            }
        )
        keys = (
            ("TRAIN_DEV", "TEST", *_GROUPS)
            if locked
            else ("TRAIN_DEV", *_GROUPS)
        )
        report: Json = {
            "schema": "omr-w5-detector-report-v1",
            "mode": args.mode,
            "formal": args.formal,
            "git_commit": commit,
            "pages_evaluated": counter.actual,
            "w3_attestation": attestation,
            "homr_enabled": not args.no_homr,
            "detectors": {
                name: result.table(keys) for name, result in totals.items()
            },
            "decision": asdict(decision),
            # The locked pass is geometry-only, including on TRAIN_DEV pages.
            "assembly": {"status": "not_run_geometry_only"}
            if locked
            else _validate_oracle(args.w3_run_dir),
        }
        private = _private(conf, args)
        _record_environment(conf, environment, private)
        _write_report(
            conf,
            "w5-detector-report",
            report,
            _w5_markdown(report),
            private,
        )
    return {"status": "completed", "pages_evaluated": counter.actual}


@dataclass(frozen=True, slots=True)
class _Source:
    source_id: str
    digest: str
    measures: int
    weight: float
    path: Path


def _sources(conf: Confinement, args: argparse.Namespace) -> list[_Source]:
    path = _bounded_path(conf.run, _PERMISSIVE / "sources-manifest.jsonl")
    rows = _rows(path)
    if args.gold_status is not None:
        _exclusion_gate(args).audit_admitted_manifest(rows)
    else:
        intake = read_object(
            _bounded_path(conf.run, Path("report") / f"{_INTAKE_REPORT}.json")
        )
        exclusion = _bounded_path(
            args.w3_run_dir, Path("splits/test-exclusion.json")
        )
        if (
            intake.get("schema") != "omr-w4a-intake-report-v1"
            or intake.get("post_admission_test_matches") != 0
            or intake.get("admitted_manifest_sha256") != sha256_file(path)
            or intake.get("test_exclusion_sha256") != sha256_file(exclusion)
        ):
            msg = (
                "cannot establish the admitted manifest's "
                "W4a TEST-exclusion audit"
            )
            raise StopConditionError(msg)
    sources: list[_Source] = []
    seen: set[str] = set()
    for row in rows:
        digest, weight, measures = (
            row["canonical_v2_sha256"],
            row["sampling_weight"],
            row["measure_count"],
        )
        if (
            row["rights_variant"] != "permissive"
            or row.get("gold_id") is not None
            or not re.fullmatch(r"[a-f0-9]{64}", digest)
            or type(measures) is not int
            or measures < 1
            or not math.isfinite(weight)
            or weight <= 0
            or digest in seen
        ):
            msg = "invalid or duplicate admitted permissive source"
            raise StopConditionError(msg)
        relative = Path("sources") / f"{digest}.v2"
        if row["canonical_v2_path"] != relative.as_posix():
            msg = "admitted score path does not match canonical digest"
            raise StopConditionError(msg)
        sources.append(
            _Source(
                row["source_id"],
                digest,
                measures,
                weight,
                _bounded_path(conf.run, _PERMISSIVE / relative),
            )
        )
        seen.add(digest)
    return sources


def _weighted_sources(sources: Sequence[_Source], seed: str) -> list[_Source]:
    rng = random.Random(seed)  # noqa: S311 - reproducible weighted sampling
    ordered = sorted(sources, key=lambda source: source.digest)
    return sorted(ordered, key=lambda source: rng.expovariate(source.weight))


def _score(source: _Source) -> AssembledScore:
    raw = source.path.read_bytes()
    if sha256_bytes(raw) != source.digest:
        msg = "admitted canonical notation has changed"
        raise StopConditionError(msg)
    score = parse_document(raw.decode())
    if (
        not isinstance(score, AssembledScore)
        or len(score.blocks) != source.measures
    ):
        msg = "admitted notation is not the expected complete score"
        raise StopConditionError(msg)
    return score


def _seed(text: str) -> int:
    return int(sha256_bytes(text.encode())[:16], 16)


def _png(image: Image.Image) -> bytes:
    with io.BytesIO() as raw:
        image.save(raw, "PNG")
        return raw.getvalue()


def _real_statistics(w3: Path) -> list[CropStatistics]:
    statistics = []
    for piece in _train_pieces(w3):
        gid = _gold_id(piece)
        for system in piece["systems"]:
            index = int(system["system_index"])
            path = _bounded_path(
                w3, Path("crops/facsimile") / f"{gid}-s{index:02d}.png"
            )
            with Image.open(path) as crop:
                statistics.append(compute_crop_statistics(crop))
    if len(statistics) != _TRAIN_SYSTEMS:
        msg = "W3 real-crop comparison requires exactly 323 TRAIN_DEV crops"
        raise StopConditionError(msg)
    return statistics


def _persist_files(
    conf: Confinement, files: dict[Path, bytes], *, base: Path = _PERMISSIVE
) -> int:
    size = sum(map(len, files.values()))
    with proof_disk_lock(conf.run.parent):
        _check_disk_unlocked(conf, size)
        for relative, content in files.items():
            target = _bounded_path(conf.run, base / relative)
            conf.write_bytes(target, content)
    return size


@dataclass(slots=True)
class _StagedSample:
    digest: str
    categories: frozenset[str]
    stage_root: Path
    relative_files: tuple[Path, ...]
    manifest_rows: tuple[str, ...]
    statistics: tuple[CropStatistics, ...]
    overlays: tuple[Image.Image, ...]
    pages: int
    group: str
    bytes_written: int


@dataclass(slots=True)
class _ProofEngine:
    ledger: RenderGateLedger
    staged: list[_StagedSample] = field(default_factory=list)
    statistics: list[CropStatistics] = field(default_factory=list)
    manifest: list[str] = field(default_factory=list)
    overlays: list[Image.Image] = field(default_factory=list)
    groups: Counter[str] = field(default_factory=Counter)
    pages: int = 0
    rendered: int = 0
    bytes_written: int = 0
    hooks_verified: bool = False

    def eligible_samples(self) -> list[_StagedSample]:
        blocked = self.ledger.disabled_categories
        return [s for s in self.staged if not (s.categories & blocked)]

    def finalize_eligible(self) -> list[_StagedSample]:
        eligible = self.eligible_samples()
        self.statistics = [st for s in eligible for st in s.statistics]
        self.manifest = [row for s in eligible for row in s.manifest_rows]
        all_ov = [ov for s in eligible for ov in s.overlays]
        self.overlays = all_ov[:_OVERLAY_PAGES]
        self.groups = Counter()
        for s in eligible:
            self.groups[s.group] += s.pages
        self.pages = sum(s.pages for s in eligible)
        self.bytes_written = sum(s.bytes_written for s in eligible)
        return eligible

    def summary(self, elapsed: float) -> Json:
        return {
            "gate_outcomes": {
                status: self.ledger.outcomes[status]
                for status in ("passed", "dropped", "excluded")
            },
            "distinct_roundtrip_scores": self.ledger.outcomes["passed"]
            + self.ledger.outcomes["dropped"],
            "category_attempts": dict(self.ledger.category_attempts),
            "category_drops": dict(self.ledger.category_drops),
            "category_drop_rates": self.ledger.drop_rates(),
            "disabled_categories": sorted(self.ledger.disabled_categories),
            "rendered_pages": self.rendered,
            "rendered_degraded_pages": self.pages,
            "layout_group_pages": dict(self.groups),
            "seconds_per_page": elapsed / self.pages,
            "bytes_per_page": self.bytes_written / self.pages,
            "peak_rss_mib": _peak_rss(),
            "workers": 1,
            "overlay_pages_verified": len(self.overlays),
            "overlay_missed_systems": 0,
            "overlay_merged_systems": 0,
            "clutter_label_invariance_verified": self.hooks_verified,
            "crop_rescale_targets_verified_px": [10.0, 20.0],
        }


def _peak_rss() -> Json:
    divisor = 1024**2 if sys.platform == "darwin" else 1024
    own = resource.getrusage(resource.RUSAGE_SELF).ru_maxrss / divisor
    child = resource.getrusage(resource.RUSAGE_CHILDREN).ru_maxrss / divisor
    return {
        "supervisor": own,
        "children": child,
        "sum_of_high_water_marks": own + child,
    }


def _image(raw: bytes) -> Image.Image:
    with Image.open(io.BytesIO(raw)) as image:
        return image.copy()


def _verify_tier_hooks(  # noqa: PLR0913
    clean: Image.Image,
    mirror: Image.Image | None,
    systems: Sequence[Box],
    measures: Sequence[Box],
    *,
    seed: int,
    yolo: bytes,
) -> None:
    uncluttered = apply_tier_r_degradation(
        clean,
        systems,
        measures,
        seed=seed,
        enable_clutter=False,
        mirror_im=mirror,
    )
    if uncluttered.yolo_text.encode() != yolo:
        msg = "Tier R YOLO bytes changed when clutter was disabled"
        raise StopConditionError(msg)
    small, large = (
        crop_tier_r_systems(uncluttered, target_staff_space_px=target)
        for target in (10.0, 20.0)
    )
    if (
        not small
        or len(small) != len(large)
        or any(
            abs(b - 2 * a) > 1
            for low, high in zip(small, large, strict=True)
            for a, b in zip(low.size, high.size, strict=True)
        )
    ):
        msg = "Tier R 10 px and 20 px crop rescale hooks failed"
        raise StopConditionError(msg)


def _stage_rendered(  # noqa: PLR0913
    conf: Confinement,
    engine: _ProofEngine,
    result: RenderResult,
    source: _Source,
    injected: InjectionResult,
    *,
    seed: str,
    group: str,
) -> None:
    if len(result.system_texts) != sum(
        len(page.geometry.systems) for page in result.pages
    ):
        msg = "renderer emitted the wrong number of system slices"
        raise StopConditionError(msg)
    stage_base = Path("tmp") / "s3-stage" / result.engine / source.digest
    stage_root = _bounded_path(conf.run, stage_base)
    texts = iter(result.system_texts)
    all_files: dict[Path, bytes] = {}
    all_rows: list[str] = []
    all_stats: list[CropStatistics] = []
    all_overlays: list[Image.Image] = []
    for page_index, page in enumerate(result.pages, 1):
        page_key = f"{source.digest}-p{page_index:03d}"
        prefix = Path(result.engine)
        clean = _image(page.png)
        mirror = (
            _image(result.pages[page_index].png)
            if page_index < len(result.pages)
            else None
        )
        systems = tuple(system.box for system in page.geometry.systems)
        measures = tuple(measure.box for measure in page.geometry.measures)
        page_seed = _seed(f"{seed}:{page_index}")
        degraded = apply_tier_r_degradation(
            clean, systems, measures, seed=page_seed, mirror_im=mirror
        )
        yolo = degraded.yolo_text.encode()
        if not engine.hooks_verified:
            _verify_tier_hooks(
                clean, mirror, systems, measures, seed=page_seed, yolo=yolo
            )
            engine.hooks_verified = True
        all_files[prefix / "pages" / f"{page_key}.png"] = _png(degraded.image)
        all_files[prefix / "yolo" / f"{page_key}.txt"] = yolo
        for index, crop in enumerate(crop_tier_r_systems(degraded), 1):
            sample = f"{page_key}-s{index:02d}"
            crop_bytes, label = _png(crop), next(texts).encode()
            crop_path = prefix / "crops" / f"{sample}.png"
            label_path = prefix / "slices" / f"{sample}.v2"
            all_files[crop_path] = crop_bytes
            all_files[label_path] = label
            all_stats.append(compute_crop_statistics(crop))
            all_rows.append(
                json.dumps(
                    {
                        "schema": "omr-synth-manifest-v1",
                        "sample_id": f"{result.engine}-{sample}",
                        "seed": page_seed,
                        "source_id": source.source_id,
                        "rights_variant": "permissive",
                        "engine": result.engine,
                        "degradation_tier": "R",
                        "crop_path": crop_path.as_posix(),
                        "crop_sha256": sha256_bytes(crop_bytes),
                        "label_path": label_path.as_posix(),
                        "label_sha256": sha256_bytes(label),
                    },
                    sort_keys=True,
                )
                + "\n"
            )
        overlay = render_warped_overlay(degraded)
        overlay.thumbnail((210, 272))
        all_overlays.append(overlay)

    log_rel = (
        Path(result.engine)
        / "injections"
        / source.digest
        / "injection-log.json"
    )
    all_files[log_rel] = (
        json.dumps(
            {
                "native_counts": injected.native_counts,
                "injected_counts": injected.injected_counts,
                "injected_by_measure": injected.injected_by_measure,
                "pedal_style_used": injected.pedal_style_used,
            },
            indent=2,
            sort_keys=True,
        )
        + "\n"
    ).encode("utf-8")
    written = _persist_files(conf, all_files, base=stage_base) + sum(
        len(row.encode()) for row in all_rows
    )
    engine.staged.append(
        _StagedSample(
            digest=source.digest,
            categories=result.categories,
            stage_root=stage_root,
            relative_files=tuple(all_files),
            manifest_rows=tuple(all_rows),
            statistics=tuple(all_stats),
            overlays=tuple(all_overlays),
            pages=len(result.pages),
            group=group,
            bytes_written=written,
        )
    )


def _contact_sheet(overlays: Sequence[Image.Image]) -> bytes:
    columns, width, height = 10, 210, 272
    sheet = Image.new(
        "RGB",
        (columns * width, math.ceil(len(overlays) / columns) * height),
        "white",
    )
    for index, overlay in enumerate(overlays):
        sheet.paste(
            overlay, ((index % columns) * width, (index // columns) * height)
        )
    return _png(sheet)


_INJECTOR_CATEGORIES = frozenset(
    {"N6", "N1", "N2", "N3", "N4", "N5", "L2", "L3", "L4", "P3"}
)
_W1_TO_INJECTOR = {
    "fingering": "N6",
    "dynamic": "N1",
    "hairpin": "N2",
    "text": "N3",
    "tempo_rehearsal": "N4",
    "pedal": "N5",
    "slur": "L2",
    "articulation_fermata": "L3",
    "arpeggio": "L4",
    "ottava": "P3",
}


def _stage_engine_bundle(
    conf: Confinement,
    name: str,
    engine: _ProofEngine,
    eligible: list[_StagedSample],
) -> None:
    bundle_base = Path("tmp") / "s3-publish"
    for sample in eligible:
        for rel in sample.relative_files:
            src_file = _bounded_path(sample.stage_root, rel)
            dst_file = _bounded_path(conf.run, bundle_base / rel)
            dst_file.parent.mkdir(parents=True, exist_ok=True)
            src_file.replace(dst_file)
    _persist_files(
        conf,
        {
            Path(name) / "samples-manifest.jsonl": "".join(
                engine.manifest
            ).encode(),
            Path("overlays") / f"{name}-contact-sheet.png": _contact_sheet(
                engine.overlays
            ),
        },
        base=bundle_base,
    )


def _publish_s3_bundles(conf: Confinement) -> None:
    bundle_root = _bounded_path(conf.run, Path("tmp") / "s3-publish")
    if not bundle_root.is_dir():
        return
    dest_root = _bounded_path(conf.run, _PERMISSIVE)
    dest_root.mkdir(parents=True, exist_ok=True)
    with proof_disk_lock(conf.run.parent):
        for sub in ("musescore", "verovio", "overlays"):
            staged_sub = bundle_root / sub
            if staged_sub.is_dir():
                staged_sub.replace(_bounded_path(conf.run, _PERMISSIVE / sub))


def _prove_engine(  # noqa: PLR0913
    conf: Confinement,
    args: argparse.Namespace,
    name: str,
    sources: Sequence[_Source],
    *,
    musescore: MuseScore,
    counter: _PageCount,
    calibrated_identity: tuple[str, str] | None = None,
) -> tuple[Json, list[CropStatistics]]:
    engine = _ProofEngine(
        RenderGateLedger(
            name,
            min_cohort_before_disable=(
                args.roundtrip_pieces if name == "verovio" else 1
            ),
        )
    )
    started = time.monotonic()
    prior = counter.actual
    overlay_target = (
        _OVERLAY_PAGES
        if args.formal
        else min(_OVERLAY_PAGES, args.timing_pages)
    )
    page_goal = max(args.timing_pages, overlay_target)
    for source in sources:
        eligible_pages = sum(s.pages for s in engine.eligible_samples())
        if (
            engine.ledger.outcomes["passed"] + engine.ledger.outcomes["dropped"]
            >= args.roundtrip_pieces
            and eligible_pages >= page_goal
        ):
            break
        if source.measures > counter.remaining:
            continue
        _check_disk(conf)
        seed = f"{args.seed}:{source.digest}"
        enabled = _INJECTOR_CATEGORIES - {
            _W1_TO_INJECTOR[c]
            for c in engine.ledger.disabled_categories
            if c in _W1_TO_INJECTOR
        }
        injected = inject_annotations(
            _score(source), seed=seed, enabled_categories=enabled
        )
        if name == "verovio" and (
            facets := has_unchecked_verovio_supplementary_facet(
                injected.score, injected.musicxml
            )
        ):
            engine.ledger.record(
                RenderGate(
                    "excluded", dict.fromkeys(facets, 1), "unchecked_facets"
                ),
                set(),
            )
            continue
        style = sample_layout_and_style(_seed(seed), source.measures)
        notation = injected.musicxml.decode()
        if name == "musescore":
            with _owned_scratch(conf, "musescore-") as directory:
                result = render_musescore(
                    notation,
                    style,
                    musescore,
                    directory / "job",
                    engine.ledger,
                    rights_variant="permissive",
                    calibrated_identity=calibrated_identity,
                    count_rasters=counter.add,
                )
        else:
            scratch = _bounded_path(conf.run, Path("tmp/verovio"))
            result = render_verovio(
                notation,
                style,
                conf.downloads,
                scratch,
                engine.ledger,
                max_pages=min(source.measures, counter.remaining),
                count_rasters=counter.add,
            )
        if result.gate.status != "passed":
            continue
        _stage_rendered(
            conf,
            engine,
            result,
            source,
            injected,
            seed=seed,
            group=style.plate_group,
        )
    engine.rendered = counter.actual - prior
    eligible = engine.finalize_eligible()
    if (
        engine.ledger.outcomes["passed"] + engine.ledger.outcomes["dropped"]
        < args.roundtrip_pieces
        or engine.pages < page_goal
    ):
        msg = (
            f"{name} did not reach the Stage S3 targets within the "
            "admitted inventory and page budget"
        )
        raise StopConditionError(msg)
    elapsed = time.monotonic() - started
    _stage_engine_bundle(conf, name, engine, eligible)
    return engine.summary(elapsed), engine.statistics


def _prepare_s3_environment(
    conf: Confinement,
    args: argparse.Namespace,
    ms: MuseScore,
    counter: _PageCount,
    private: Sequence[str],
) -> tuple[Json, tuple[str, str] | None]:
    verify_w4_w5_live_assets(conf.downloads, require_s3=True)
    ms_identity = (ms.version, ms.executable_sha256)
    unreviewed_formal_ms = (
        args.formal and ms_identity not in ACCEPTED_MUSESCORE_IDENTITIES
    )
    calibrated_identity: tuple[str, str] | None = None
    if not has_source_verified_defaults(ms.version) or unreviewed_formal_ms:
        with _owned_scratch(conf, "musescore-cal-") as cal_dir:
            calibrated_identity = calibrate_musescore_pixel_scale(
                ms, cal_dir / "job", count_rasters=counter.add
            )
    environment: Json = {
        "workspace": _workspace_versions(),
        "musescore_version": ms.version,
        "musescore_executable_sha256": ms.executable_sha256,
        "musescore_pixel_calibrated": calibrated_identity is not None,
        "verovio_version": VEROVIO_VERSION,
        "verovio_wheel_sha256": VEROVIO_WHEEL_SHA256,
        "resvg_version": RESVG_VERSION,
        "resvg_wheel_sha256": RESVG_WHEEL_SHA256,
    }
    _record_environment(conf, environment, private)
    if unreviewed_formal_ms:
        msg = (
            f"MuseScore {ms.version} ({ms.executable_sha256[:12]}) "
            "passed .mpos/12.0 calibration and was recorded in "
            "environment.json; record independent research-evidence-review "
            "disposition before --formal Stage S3"
        )
        raise StopConditionError(msg)
    return environment, calibrated_identity


def _run_s3(conf: Confinement, args: argparse.Namespace) -> Json:
    commit = _formal_source(conf, locked=False) if args.formal else None
    if args.formal and (
        args.roundtrip_pieces < _MIN_ROUNDTRIP
        or args.timing_pages < _MIN_TIMING
    ):
        msg = (
            "formal S3 targets require at least 50 distinct scores and "
            "100 degraded pages per engine"
        )
        raise StopConditionError(msg)
    for existing in (
        *(_PERMISSIVE / sub for sub in ("musescore", "verovio", "overlays")),
        Path("tmp") / "s3-stage",
        Path("tmp") / "s3-publish",
    ):
        if _bounded_path(conf.run, existing).exists():
            msg = (
                "Stage S3 output or staging subtree already exists in RUN; "
                "use a fresh run directory"
            )
            raise StopConditionError(msg)
    sources = _weighted_sources(_sources(conf, args), args.seed)
    if len(sources) < args.roundtrip_pieces:
        msg = "the source inventory cannot reach the distinct-score gate"
        raise StopConditionError(msg)
    planned_pages = min(
        W4_W5_MAX_PAGES_PER_RUN, 2 * sum(source.measures for source in sources)
    )
    minimum_pages = 2 * max(
        args.roundtrip_pieces, args.timing_pages, _OVERLAY_PAGES
    )
    if planned_pages < minimum_pages:
        msg = (
            "S3 timing target cannot fit the per-run page budget; "
            "return to the repository owner (#1491 or #686)"
        )
        raise StopConditionError(msg)
    verify_loopback_only_unshare()
    stage_root = _bounded_path(conf.run, Path("tmp") / "s3-stage")
    publish_root = _bounded_path(conf.run, Path("tmp") / "s3-publish")
    with _metered_run(conf, "s3-proof", planned_pages, commit) as counter:
        attestation = verify_w3_snapshot_attestation(
            args.w3_run_dir, include_test_pages=False
        )
        ms = MuseScore.open(
            conf, executable=args.musescore, settings=args.settings
        )
        private = _private(conf, args)
        environment, calibrated_identity = _prepare_s3_environment(
            conf, args, ms, counter, private
        )
        real = _real_statistics(args.w3_run_dir)
        engines = {}
        statistics = {}
        try:
            for name in ("musescore", "verovio"):
                engines[name], synthetic = _prove_engine(
                    conf,
                    args,
                    name,
                    sources,
                    musescore=ms,
                    counter=counter,
                    calibrated_identity=calibrated_identity,
                )
                if (
                    name == "musescore"
                    and isinstance(getattr(ms, "executable", None), Path)
                    and sha256_file(ms.executable) != ms.executable_sha256
                ):
                    msg = "host MuseScore binary changed during Stage S3 run"
                    raise StopConditionError(msg)
                statistics[name] = {
                    key: asdict(value)
                    for key, value in compare_crop_statistics(
                        synthetic, real
                    ).items()
                }
            _publish_s3_bundles(conf)
        finally:
            shutil.rmtree(stage_root, ignore_errors=True)
            shutil.rmtree(publish_root, ignore_errors=True)
        report: Json = {
            "schema": "omr-w4-s3-report-v1",
            "formal": args.formal,
            "git_commit": commit,
            "w3_attestation": attestation,
            "pages_rendered": counter.actual,
            "train_dev_crop_count": len(real),
            "engines": engines,
            "crop_statistics": statistics,
            "environment": environment,
        }
        markdown = (
            "# Stage S3 synthetic-data proof\n\n"
            "Round-trip checks establish symbolic preservation, not pixel "
            "legibility or recognition accuracy. Memory measurements are "
            "supervisor and child lifetime high-water marks.\n\n"
            + _json_block(report)
        )
        _write_report(conf, "w4-s3-report", report, markdown, private)
    return {"status": "completed", "pages_rendered": counter.actual}


def _validate_files(args: argparse.Namespace) -> int:
    texts = tuple(path.read_text(encoding="utf-8") for path in args.files)
    page, system = 1, 0
    expected = []
    for text in texts:
        with contextlib.suppress(TextScoreError):
            doc = parse_document(text)
            if (
                isinstance(doc, SystemDocument)
                and doc.page == page + 1
                and system
            ):
                page, system = page + 1, 0
        system += 1
        expected.append((page, system))
    count = args.expected_systems
    if count > len(expected):
        expected.append((page, system + 1))
    else:
        expected = expected[:count]
    result = validate_candidate_system_assembly(texts, expected)
    print(dumps(asdict(result) | {"expected_systems": count}), end="")  # noqa: T201
    return 0 if result.status is AssemblyStatus.SUCCESS else 1


def _prepare_run(args: argparse.Namespace, root: Path) -> Confinement:
    run = _outside(args.run_dir, root)
    downloads = _outside(args.downloads, root)
    w3 = args.w3_run_dir.resolve()
    if any(
        path.is_relative_to(w3) or w3.is_relative_to(path)
        for path in (run, downloads)
    ):
        msg = "W3 inputs must be disjoint from the W4/W5 write roots"
        raise StopConditionError(msg)
    for child in ("tmp", "run.json", ".w4-w5.lock", "report", "synthetic"):
        _bounded_path(run, Path(child))
    args.w3_run_dir = w3
    return Confinement.create(run, downloads)


def _stop_detail(
    conf: Confinement, args: argparse.Namespace, reason: str
) -> str:
    detail = sanitize_subprocess_output(reason, (conf.run, conf.downloads))
    try:
        private = _private(conf, args)
        privacy.scan(detail, private)
        encoded = dumps({"message": detail})
        privacy.scan(encoded, private)
    except StopConditionError, OSError:
        return "W4/W5 execution stopped; private details withheld"
    return detail


def _run_command(conf: Confinement, args: argparse.Namespace) -> Json:
    try:
        _check_disk(conf)
        if args.command == "intake-w4a":
            return _run_intake(conf, args)
        if args.command == "run-s3":
            if (args.gold_status is None) != (args.gold_cache is None):
                msg = "--gold-status and --gold-cache must be supplied together"
                raise StopConditionError(msg)
            return _run_s3(conf, args)
        return _run_w5(conf, args)
    except (
        OSError,
        ValueError,
        TypeError,
        KeyError,
        ScoreParseError,
        tarfile.TarError,
        TextScoreError,
        MusicXmlError,
        zipfile.BadZipFile,
    ) as error:
        msg = f"W4/W5 input/artifact processing failed ({type(error).__name__})"
        raise StopConditionError(msg) from error


def main(argv: Sequence[str] | None = None) -> int:
    """Run a synthetic-data command and return its process exit status."""
    args = _parser().parse_args(argv)
    if args.command == "validate-assembly":
        try:
            return _validate_files(args)
        except OSError, UnicodeError:
            print("stop: cannot read assembly input", file=sys.stderr)  # noqa: T201
            return 2
    root = repository_root()
    if root is None:
        print(  # noqa: T201
            "stop: cannot establish repository write confinement",
            file=sys.stderr,
        )
        return 2
    try:
        if args.command == "setup-w4-w5":
            downloads = _outside(args.downloads, root)
            setup_w4_w5_environments(
                downloads_dir=downloads,
                repo_root=root,
                include_homr=not args.no_homr,
            )
            result = {"status": "completed"}
        else:
            conf = _prepare_run(args, root)
            with conf.lock(conf.run / ".w4-w5.lock", blocking=False):
                try:
                    result = _run_command(conf, args)
                except StopConditionError as error:
                    detail = _stop_detail(conf, args, str(error))
                    with contextlib.suppress(StopConditionError, OSError):
                        record_stop(
                            conf, args.command, StopConditionError(detail)
                        )
                    print(f"stop: {detail}", file=sys.stderr)  # noqa: T201
                    return 2
    except (
        StopConditionError,
        OSError,
        subprocess.SubprocessError,
        ValueError,
    ) as error:
        if isinstance(error, StopConditionError):
            detail = sanitize_subprocess_output(str(error))
        else:
            detail = f"synthetic command failed ({type(error).__name__})"
        print(f"stop: {detail}", file=sys.stderr)  # noqa: T201
        return 2
    print(dumps(result), end="")  # noqa: T201
    return 0
