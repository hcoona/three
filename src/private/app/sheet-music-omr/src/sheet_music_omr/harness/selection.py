"""Select the PDMX and OpenScore Lieder samples by the structural screen.

Candidates are visited in their protocol order until the sample is full.
Each visited candidate is recorded with its first failing reason, and each
selected ``X`` is written to the run directory. Selection converts nothing
with the converters, and a finished selection is never redone.
"""

from __future__ import annotations

import shutil
from dataclasses import dataclass, field
from typing import TYPE_CHECKING

from sheet_music_omr.harness.corpora import (
    CorpusError,
    NoUniquePianoPart,
    extract_members,
    extract_piano,
    lieder_candidates,
    mxl_root,
    pdmx_candidates,
    read_member,
)
from sheet_music_omr.harness.privacy import private_strings, scan
from sheet_music_omr.harness.records import now, read_object, write_json
from sheet_music_omr.harness.screen import REASON_TEXT, REASONS, screen
from sheet_music_omr.harness.stops import StopConditionError
from sheet_music_omr.harness.worker import STEP_SECONDS

if TYPE_CHECKING:
    from collections.abc import Callable, Mapping, Sequence
    from pathlib import Path

    from sheet_music_omr.harness.confine import Confinement
    from sheet_music_omr.harness.corpora import Candidate, TarExtraction
    from sheet_music_omr.harness.musescore import MuseScore
    from sheet_music_omr.harness.worker import Executor

PDMX_SAMPLE = 200
"""The size of the PDMX sample."""

LIEDER_SAMPLE = 50
"""The size of the Lieder sample."""

LIEDER_EXPORTS = 1356
"""The most selection exports: the scores of the pinned Lieder release."""

PDMX_BATCH = 600
"""The candidates extracted per streaming pass over the PDMX archive."""

PARSE_REASON = "musicdiff_parse"
"""The screen's last reason, which selection decides by parsing."""

LIEDER_REASON_TEXT: Mapping[str, str] = {
    "export_failed": "export failed",
    "no_unique_piano_part": "no unique piano part",
}
"""The Lieder exclusion reasons that precede the screen."""

PREFIX: Mapping[str, str] = {"pdmx": "p", "lieder": "l", "pilot": "t"}
"""The identifier prefix of each corpus."""


class SelectionError(StopConditionError):
    """Selection cannot proceed as the protocol fixes it."""


@dataclass(frozen=True, slots=True)
class Visit:
    """One visited candidate.

    Attributes:
        key: The candidate's PDMX or Lieder path.
        order: Its order hash.
        reason: Its first failing reason, or ``None`` when selected.
        detail: What failed.
        id: Its run identifier when selected.
    """

    key: str
    order: str
    reason: str | None
    detail: str = ""
    id: str | None = None

    def record(self) -> dict[str, object]:
        """Return the visit as plain data."""
        return {
            "key": self.key,
            "order": self.order,
            "reason": self.reason,
            "detail": self.detail,
            "id": self.id,
        }


@dataclass(slots=True)
class _Sampler:
    """Screen candidates and keep the first ones that pass."""

    corpus: str
    sample: int
    confinement: Confinement
    executor: Executor
    parse_check: Callable[..., object]
    timeout: float
    visits: list[Visit] = field(default_factory=list)

    @property
    def selected(self) -> int:
        return sum(1 for v in self.visits if v.reason is None)

    @property
    def full(self) -> bool:
        return self.selected >= self.sample

    def exclude(self, candidate: Candidate, reason: str, detail: str) -> None:
        self.visits.append(
            Visit(candidate.key, candidate.order, reason, detail)
        )

    def offer(self, candidate: Candidate, data: bytes) -> None:
        """Screen one candidate ``X`` and keep it if it passes."""
        result = screen(data)
        if not result.passed:
            self.exclude(candidate, str(result.reason), result.detail)
            return
        directory = self.confinement.temp_dir("screen-")
        try:
            path = self.confinement.write_bytes(directory / "x.musicxml", data)
            outcome = self.executor.call(
                self.parse_check,
                {"x": path},
                context=f"step parse_check, {self.corpus} {candidate.key}",
                timeout=self.timeout,
            )
        finally:
            shutil.rmtree(self.confinement.check(directory))
        value = outcome.value if isinstance(outcome.value, dict) else {}
        if outcome.status == "timeout":
            self.exclude(candidate, PARSE_REASON, "timeout")
        elif not outcome.ok:
            self.exclude(candidate, PARSE_REASON, outcome.error_type)
        elif value.get("status") != "ok":
            self.exclude(candidate, PARSE_REASON, "unparsable")
        else:
            name = f"{PREFIX[self.corpus]}{self.selected + 1:04d}"
            target = inputs_dir(self.confinement, self.corpus) / name
            self.confinement.write_bytes(target / "x.musicxml", data)
            self.visits.append(
                Visit(candidate.key, candidate.order, None, "", name),
            )


def selection_path(confinement: Confinement, corpus: str) -> Path:
    """Return the selection record of a corpus."""
    return confinement.run / "selection" / f"{corpus}.json"


def inputs_dir(confinement: Confinement, corpus: str) -> Path:
    """Return the directory of a corpus's selected inputs."""
    return confinement.run / "inputs" / corpus


def _begin(confinement: Confinement, corpus: str) -> None:
    """Refuse a finished selection; clear an interrupted one.

    Raises:
        SelectionError: If the corpus was already selected.
    """
    if selection_path(confinement, corpus).exists():
        message = f"the {corpus} sample is selected and is never reselected"
        raise SelectionError(message)
    directory = confinement.check(inputs_dir(confinement, corpus))
    if directory.exists():
        shutil.rmtree(directory)


def _finish(
    confinement: Confinement,
    sampler: _Sampler,
    candidates: int,
    extra: Mapping[str, object],
) -> dict[str, object]:
    record: dict[str, object] = {
        "corpus": sampler.corpus,
        "sample": sampler.sample,
        "candidates": candidates,
        "visited": [v.record() for v in sampler.visits],
        "selected": [
            {"id": v.id, "key": v.key}
            for v in sampler.visits
            if v.reason is None
        ],
        "finished": now(),
        **extra,
    }
    write_json(confinement, selection_path(confinement, sampler.corpus), record)
    return record


def select_pdmx(  # noqa: PLR0913
    csv_path: Path,
    tar_path: Path,
    *,
    confinement: Confinement,
    executor: Executor,
    parse_check: Callable[..., object],
    sample: int = PDMX_SAMPLE,
    batch: int = PDMX_BATCH,
    timeout: float = STEP_SECONDS,
) -> dict[str, object]:
    """Select the PDMX sample.

    The prefiltered candidates are extracted in ordered batches; each root
    document is screened and parsed until ``sample`` pass. A member that
    exceeds the size bound or whose container is unreadable fails the first
    check as undecidable.

    Returns:
        The selection record.

    Raises:
        SelectionError: If the corpus was selected, an archive is unreadable
            or a requested member is missing.
    """
    _begin(confinement, "pdmx")
    try:
        prefilter = pdmx_candidates(csv_path)
    except CorpusError as error:
        message = f"the PDMX table cannot be read: {error}"
        raise SelectionError(message) from error
    sampler = _Sampler(
        "pdmx",
        sample,
        confinement,
        executor,
        parse_check,
        timeout,
    )
    candidates = prefilter.candidates
    destination = confinement.downloads / "pdmx-mxl"
    for start in range(0, len(candidates), batch):
        chunk = candidates[start : start + batch]
        try:
            extraction = extract_members(
                tar_path,
                [c.key for c in chunk],
                destination,
                confinement=confinement,
            )
        except CorpusError as error:
            message = f"the PDMX archive cannot be extracted: {error}"
            raise SelectionError(message) from error
        if extraction.missing:
            message = (
                f"{len(extraction.missing)} PDMX candidates are missing from "
                "the archive"
            )
            raise SelectionError(message)
        for candidate in chunk:
            if sampler.full:
                break
            _offer_pdmx(sampler, candidate, extraction)
        if sampler.full:
            break
    return _finish(
        confinement,
        sampler,
        len(candidates),
        {
            "prefilter": {
                "rows": prefilter.rows,
                "passed": prefilter.passed,
                "undecidable": prefilter.undecidable,
            },
        },
    )


def _offer_pdmx(
    sampler: _Sampler,
    candidate: Candidate,
    extraction: TarExtraction,
) -> None:
    if candidate.key in extraction.oversized:
        sampler.exclude(candidate, "parts_staves", "undecidable: oversized")
        return
    try:
        root = mxl_root(extraction.paths[candidate.key].read_bytes())
    except (CorpusError, OSError) as error:
        detail = f"undecidable: {type(error).__name__}"
        sampler.exclude(candidate, "parts_staves", detail)
        return
    sampler.offer(candidate, root)


def select_lieder(  # noqa: PLR0913
    zip_path: Path,
    *,
    confinement: Confinement,
    executor: Executor,
    parse_check: Callable[..., object],
    musescore: MuseScore,
    sample: int = LIEDER_SAMPLE,
    cap: int = LIEDER_EXPORTS,
    timeout: float = STEP_SECONDS,
) -> dict[str, object]:
    """Select the Lieder sample.

    Each candidate is exported by MuseScore in a fresh temporary directory,
    which is deleted after the file; its piano extract is screened and
    parsed until ``sample`` pass.

    Returns:
        The selection record.

    Raises:
        SelectionError: If the corpus was selected, the archive is unreadable
            or not the pinned release, a member cannot be read, or the export
            cap would be exceeded.
    """
    _begin(confinement, "lieder")
    try:
        archive = lieder_candidates(zip_path)
    except CorpusError as error:
        message = f"the Lieder archive cannot be read: {error}"
        raise SelectionError(message) from error
    if archive.commit_matches is False:
        message = "the Lieder archive is not the pinned commit"
        raise SelectionError(message)
    sampler = _Sampler(
        "lieder",
        sample,
        confinement,
        executor,
        parse_check,
        timeout,
    )
    exports = 0
    with musescore.batch("lieder-export"):
        for candidate in archive.candidates:
            if sampler.full:
                break
            if exports >= cap:
                message = f"the Lieder selection would exceed {cap} exports"
                raise SelectionError(message)
            try:
                data = read_member(zip_path, candidate.key)
            except CorpusError as error:
                message = f"a Lieder candidate cannot be read: {error}"
                raise SelectionError(message) from error
            exports += 1
            exported, detail = _export(confinement, musescore, data, timeout)
            if exported is None:
                sampler.exclude(candidate, "export_failed", detail)
                continue
            try:
                extract = extract_piano(exported)
            except NoUniquePianoPart as error:
                sampler.exclude(candidate, "no_unique_piano_part", str(error))
                continue
            except CorpusError as error:
                sampler.exclude(candidate, "export_failed", str(error))
                continue
            sampler.offer(candidate, extract)
    return _finish(
        confinement,
        sampler,
        len(archive.candidates),
        {
            "exports": exports,
            "commit_matches": archive.commit_matches,
            "members": archive.members,
        },
    )


def _export(
    confinement: Confinement,
    musescore: MuseScore,
    data: bytes,
    timeout: float,
) -> tuple[bytes | None, str]:
    """Export one ``.mscx`` to MusicXML; delete the temporary files."""
    directory = confinement.temp_dir("lieder-")
    try:
        source = confinement.write_bytes(directory / "score.mscx", data)
        target = directory / "score.musicxml"
        conversion = musescore.convert(source, target, timeout=timeout)
        if conversion.ok:
            return target.read_bytes(), ""
        if conversion.timed_out:
            return None, "timeout"
        return (
            None,
            f"exit {conversion.exit_code}, written {conversion.written}",
        )
    finally:
        shutil.rmtree(confinement.check(directory))


def load_selection(confinement: Confinement, corpus: str) -> dict[str, object]:
    """Read a finished selection record.

    Raises:
        SelectionError: If the corpus has not been selected.
    """
    path = selection_path(confinement, corpus)
    if not path.exists():
        message = f"the {corpus} sample has not been selected"
        raise SelectionError(message)
    return read_object(path)


def _reason_rank(entry: tuple[str, int]) -> int:
    order = (*LIEDER_REASON_TEXT, *REASONS)
    return order.index(entry[0]) if entry[0] in order else len(order)


def _reason_text(reason: str) -> str:
    return LIEDER_REASON_TEXT.get(reason) or REASON_TEXT.get(reason, reason)


def selection_markdown(records: Sequence[Mapping[str, object]]) -> str:
    """Return the public selection lists and exclusion counts."""
    lines = [
        "# W1 round-trip sample selection",
        "",
        "Every statement below is a runtime observation of the harness.",
        "Exclusion counts cover only the screened prefix of each candidate",
        "order.",
    ]
    titles = {
        "pdmx": "PDMX v9 (CC BY 4.0 deposit, doi:10.5281/zenodo.15571083)",
        "lieder": (
            "OpenScore Lieder v3.0.0 piano-accompaniment extracts "
            "(doi:10.5281/zenodo.15450144)"
        ),
    }
    for record in records:
        corpus = str(record.get("corpus"))
        visited = record.get("visited")
        visits = visited if isinstance(visited, list) else []
        selected = record.get("selected")
        chosen = selected if isinstance(selected, list) else []
        counts: dict[str, int] = {}
        for visit in visits:
            reason = visit.get("reason") if isinstance(visit, dict) else None
            if isinstance(reason, str):
                counts[reason] = counts.get(reason, 0) + 1
        lines += [
            "",
            f"## {titles.get(corpus, corpus)}",
            "",
            f"- Candidates in order: {record.get('candidates')}.",
            f"- Candidates screened: {len(visits)}.",
            f"- Selected: {len(chosen)} of {record.get('sample')}.",
        ]
        if "exports" in record:
            lines.append(f"- Selection exports: {record.get('exports')}.")
        if corpus == "lieder":
            lines.append(
                "- Each file is a piano-accompaniment extract, not an "
                "original piano work.",
            )
        lines += ["", "| Exclusion reason | Count |", "| --- | ---: |"]
        lines += [
            f"| {_reason_text(reason)} | {number} |"
            for reason, number in sorted(counts.items(), key=_reason_rank)
        ]
        lines += ["", "Selected files, in order:", ""]
        lines += [
            f"1. `{entry.get('key')}`"
            for entry in chosen
            if isinstance(entry, dict)
        ]
    return "\n".join(lines) + "\n"


def write_selection_markdown(
    confinement: Confinement,
    records: Sequence[Mapping[str, object]],
) -> Path:
    """Write the public selection lists after the privacy scan.

    Raises:
        PrivacyError: If the text holds private content.
    """
    text = selection_markdown(records)
    scan(text, private_strings(confinement))
    return confinement.write_text(
        confinement.run / "selection" / "selection.md",
        text,
    )
