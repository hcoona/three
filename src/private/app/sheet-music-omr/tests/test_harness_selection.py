"""Scenario tests for the harness's PDMX and Lieder sample selection.

Synthetic archives hold copies of a comparison fixture; the musicdiff parse
check is a controlled substitute that rejects a marked score.
"""

from __future__ import annotations

import csv
import io
import shutil
import tarfile
import zipfile
from pathlib import Path
from typing import TYPE_CHECKING

import pytest

from sheet_music_omr.harness.confine import Confinement
from sheet_music_omr.harness.corpora import (
    LIEDER_COMMIT,
    LIEDER_PREFIX,
    PDMX_PREFIX,
    ordered,
)
from sheet_music_omr.harness.musescore import VERSION, MuseScore
from sheet_music_omr.harness.selection import (
    SelectionError,
    load_selection,
    select_lieder,
    select_pdmx,
    write_selection_markdown,
)
from sheet_music_omr.harness.stops import StopConditionError
from sheet_music_omr.harness.worker import InlineExecutor, StepStopError

if TYPE_CHECKING:
    from collections.abc import Sequence

FIXTURES = Path(__file__).parent / "fixtures" / "comparison"
BASE = (FIXTURES / "base.musicxml").read_bytes()
SINGLE = (FIXTURES / "single-staff.musicxml").read_bytes()
MARK = b"<!-- unparsable -->"
PIANO = BASE.replace(
    b"<part-name>Piano</part-name>",
    b'<part-name>Piano</part-name><score-instrument id="P1-I1">'
    b"<instrument-name>Piano</instrument-name>"
    b"<instrument-sound>keyboard.piano</instrument-sound>"
    b"</score-instrument>",
)
CC0 = "https://creativecommons.org/publicdomain/zero/1.0/"
HEADER = [
    "path",
    "mxl",
    "title",
    "subset:no_license_conflict",
    "subset:all_valid",
    "license_url",
    "n_tracks",
    "tracks",
    "has_lyrics",
]


def parse_check(x: Path) -> dict[str, object]:
    """Reject a marked score as musicdiff would an unparsable one."""
    return {"status": "unparsable" if MARK in x.read_bytes() else "ok"}


def defect(x: Path) -> None:
    """Raise a protocol stop from the parse check."""
    del x
    message = "defect"
    raise StopConditionError(message)


@pytest.fixture
def roots(tmp_path: Path) -> Confinement:
    """Return fresh run and download roots."""
    return Confinement.create(tmp_path / "run", tmp_path / "downloads")


def _mxl(score: bytes) -> bytes:
    buffer = io.BytesIO()
    with zipfile.ZipFile(buffer, "w") as archive:
        archive.writestr(
            "META-INF/container.xml",
            '<container><rootfiles><rootfile full-path="s.musicxml"/>'
            "</rootfiles></container>",
        )
        archive.writestr("s.musicxml", score)
    return buffer.getvalue()


def _pdmx(tmp_path: Path, contents: Sequence[bytes]) -> tuple[Path, list[str]]:
    """Write a table and archive whose ordered candidates hold contents."""
    keys = [f"./mxl/{n}/s{n}.mxl" for n in range(len(contents))]
    order = [c.key for c in ordered(keys, PDMX_PREFIX)]
    table = tmp_path / "PDMX.csv"
    with table.open("w", encoding="utf-8", newline="") as stream:
        writer = csv.writer(stream)
        writer.writerow(HEADER)
        for key in keys:
            writer.writerow(
                [key, key, "t", "True", "True", CC0, "1", "0", "False"],
            )
    archive_path = tmp_path / "mxl.tar.gz"
    with tarfile.open(archive_path, "w:gz") as archive:
        for key, data in zip(order, contents, strict=True):
            member = tarfile.TarInfo(key)
            member.size = len(data)
            archive.addfile(member, io.BytesIO(data))
    return table, order


def test_pdmx_keeps_the_first_passing_candidates_in_order(
    roots: Confinement,
    tmp_path: Path,
) -> None:
    """Each visited candidate has its first failing reason or an id."""
    table, order = _pdmx(
        tmp_path,
        [
            _mxl(SINGLE),
            b"not a zip",
            _mxl(BASE + MARK),
            _mxl(BASE),
            _mxl(BASE),
            _mxl(BASE),
        ],
    )

    record = select_pdmx(
        table,
        tmp_path / "mxl.tar.gz",
        confinement=roots,
        executor=InlineExecutor(),
        parse_check=parse_check,
        sample=2,
        batch=4,
    )

    visited = record["visited"]
    assert isinstance(visited, list)
    assert [(v["key"], v["reason"], v["id"]) for v in visited] == [
        (order[0], "parts_staves", None),
        (order[1], "parts_staves", None),
        (order[2], "musicdiff_parse", None),
        (order[3], None, "p0001"),
        (order[4], None, "p0002"),
    ]
    assert visited[1]["detail"].startswith("undecidable")
    inputs = roots.run / "inputs" / "pdmx"
    assert sorted(p.name for p in inputs.iterdir()) == ["p0001", "p0002"]
    assert (inputs / "p0001" / "x.musicxml").read_bytes() == BASE
    assert not list(roots.temp.iterdir())
    with pytest.raises(SelectionError, match="never reselected"):
        select_pdmx(
            table,
            tmp_path / "mxl.tar.gz",
            confinement=roots,
            executor=InlineExecutor(),
            parse_check=parse_check,
        )

    path = write_selection_markdown(roots, [load_selection(roots, "pdmx")])
    text = path.read_text(encoding="utf-8")
    assert text.index(f"`{order[3]}`") < text.index(f"`{order[4]}`")
    assert "| 2 |" in text


def test_pdmx_parse_check_defect_and_missing_member_stop(
    roots: Confinement,
    tmp_path: Path,
) -> None:
    """A protocol defect in the parse check, or a missing member, stops."""
    table, _ = _pdmx(tmp_path, [_mxl(BASE)])
    with pytest.raises(StepStopError, match="defect"):
        select_pdmx(
            table,
            tmp_path / "mxl.tar.gz",
            confinement=roots,
            executor=InlineExecutor(),
            parse_check=defect,
        )
    with tarfile.open(tmp_path / "mxl.tar.gz", "w:gz"):
        pass
    with pytest.raises(SelectionError, match="missing from the archive"):
        select_pdmx(
            table,
            tmp_path / "mxl.tar.gz",
            confinement=roots,
            executor=InlineExecutor(),
            parse_check=parse_check,
        )


class ExportLauncher:
    """Export an ``.mscx`` by copying it, or fail when it says so."""

    def __init__(self) -> None:
        """Start with no exports."""
        self.exports = 0

    def __call__(
        self,
        command: Sequence[str],
        timeout: float,
    ) -> tuple[int | None, str]:
        """Copy the source to the target unless the source is ``fail``."""
        del timeout
        source, target = Path(command[-1]), Path(command[2])
        if source.suffix == ".mscx":
            self.exports += 1
            if source.read_bytes() == b"fail":
                return 1, "failed"
        shutil.copyfile(source, target)
        return 0, "ok"


def _lieder(
    roots: Confinement,
    tmp_path: Path,
    contents: Sequence[bytes],
) -> tuple[Path, list[str], MuseScore, ExportLauncher]:
    keys = [f"Lieder/scores/C/S/lc{n}.mscx" for n in range(len(contents))]
    order = [c.key for c in ordered(keys, LIEDER_PREFIX)]
    path = tmp_path / "Lieder.zip"
    with zipfile.ZipFile(path, "w") as archive:
        for key, data in zip(order, contents, strict=True):
            archive.writestr(f"OpenScore-Lieder-f44e962/{key}", data)
        archive.comment = LIEDER_COMMIT.encode()
    executable = tmp_path / "MuseScore4.exe"
    executable.write_bytes(b"")
    settings = tmp_path / "MuseScore4.ini"
    settings.write_text("[ui]\n", encoding="utf-8")
    launcher = ExportLauncher()
    musescore = MuseScore.open(
        roots,
        executable=executable,
        settings=settings,
        version_lookup=lambda: VERSION,
        launcher=launcher,
    )
    return path, order, musescore, launcher


def test_lieder_records_reasons_and_never_exceeds_the_cap(
    roots: Confinement,
    tmp_path: Path,
) -> None:
    """Failed exports and non-piano scores are excluded; the cap stops."""
    path, order, musescore, launcher = _lieder(
        roots,
        tmp_path,
        [b"fail", BASE, PIANO, PIANO],
    )

    record = select_lieder(
        path,
        confinement=roots,
        executor=InlineExecutor(),
        parse_check=parse_check,
        musescore=musescore,
        sample=1,
    )

    visited = record["visited"]
    assert isinstance(visited, list)
    assert [(v["key"], v["reason"], v["id"]) for v in visited] == [
        (order[0], "export_failed", None),
        (order[1], "no_unique_piano_part", None),
        (order[2], None, "l0001"),
    ]
    assert (record["exports"], launcher.exports) == (3, 3)
    assert not list(roots.temp.iterdir())

    other = Confinement.create(tmp_path / "run2", tmp_path / "downloads2")
    path, _, musescore, launcher = _lieder(
        other,
        tmp_path,
        [b"fail", BASE, PIANO, PIANO],
    )
    with pytest.raises(SelectionError, match="exceed 2 exports"):
        select_lieder(
            path,
            confinement=other,
            executor=InlineExecutor(),
            parse_check=parse_check,
            musescore=musescore,
            sample=1,
            cap=2,
        )
    assert launcher.exports == 2  # noqa: PLR2004 - the cap
