"""Scenario tests for the harness's PDMX and OpenScore Lieder candidates."""

from __future__ import annotations

import csv
import hashlib
import io
import tarfile
import xml.etree.ElementTree as ET
import zipfile
from typing import TYPE_CHECKING

import pytest

from sheet_music_omr.harness import corpora
from sheet_music_omr.harness.confine import Confinement
from sheet_music_omr.harness.corpora import (
    LIEDER_COMMIT,
    LIEDER_PREFIX,
    PDMX_PREFIX,
    CorpusError,
    NoUniquePianoPart,
    extract_members,
    extract_piano,
    lieder_candidates,
    mxl_root,
    ordered,
    pdmx_candidates,
    read_member,
)

if TYPE_CHECKING:
    from pathlib import Path

CC0 = "https://creativecommons.org/publicdomain/zero/1.0/"
PDM = "https://creativecommons.org/publicdomain/mark/1.0/"
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


def by_hash(keys: list[str], prefix: str) -> list[str]:
    """Sort keys by the SHA-256 of the prefix and key, computed here."""
    return sorted(
        keys,
        key=lambda key: hashlib.sha256(f"{prefix}{key}".encode()).hexdigest(),
    )


def test_order_is_the_seeded_sha256() -> None:
    """Order three keys by the hash of the seed prefix and key."""
    keys = ["mxl/b.mxl", "mxl/a.mxl", "mxl/c.mxl"]
    candidates = ordered(keys, PDMX_PREFIX)
    assert [c.key for c in candidates] == by_hash(keys, PDMX_PREFIX)
    assert (
        candidates[0].order
        == hashlib.sha256(
            f"w1-pdmx-v1:{candidates[0].key}".encode(),
        ).hexdigest()
    )
    assert [c.key for c in ordered(keys, LIEDER_PREFIX)] == by_hash(
        keys,
        LIEDER_PREFIX,
    )
    with pytest.raises(CorpusError, match="repeat"):
        ordered(["a", "b", "a"], PDMX_PREFIX)


def row(mxl: str, **changes: str) -> list[str]:
    """Return a ``PDMX.csv`` row that passes unless changed."""
    values = {
        "path": mxl.replace(".mxl", ".json").replace("./mxl/", "./data/"),
        "mxl": mxl,
        "title": 'A "song", with a comma\nand a line break',
        "subset:no_license_conflict": "True",
        "subset:all_valid": "True",
        "license_url": CC0,
        "n_tracks": "1",
        "tracks": "0",
        "has_lyrics": "False",
    }
    values.update(changes)
    return [values[name] for name in HEADER]


def write_csv(path: Path, header: list[str], rows: list[list[str]]) -> Path:
    """Write a CSV file as pandas would."""
    with path.open("w", encoding="utf-8", newline="") as stream:
        writer = csv.writer(stream)
        writer.writerow(header)
        writer.writerows(rows)
    return path


def test_pdmx_prefilter_applies_each_clause(tmp_path: Path) -> None:
    """Pass only the rows that meet every clause, failing closed."""
    passing = ["./mxl/1/a.mxl", "./mxl/2/b.mxl", "./mxl/3/c.mxl"]
    rows = [
        row(passing[0]),
        row(passing[1], license_url=PDM),
        row(passing[2]),
        row("./mxl/4/d.mxl", **{"subset:no_license_conflict": "False"}),
        row("./mxl/5/e.mxl", **{"subset:all_valid": "False"}),
        row("./mxl/6/f.mxl", license_url=CC0.rstrip("/")),
        row("./mxl/7/g.mxl", n_tracks="2"),
        row("./mxl/8/h.mxl", tracks="0-40"),
        row("./mxl/9/i.mxl", has_lyrics="True"),
        row("./mxl/10/j.mxl", **{"subset:all_valid": "yes"}),
        row("./mxl/11/k.mxl", n_tracks="1.0"),
        row("./mxl/12/l.mxl")[:-1],
        row(""),
    ]
    result = pdmx_candidates(write_csv(tmp_path / "PDMX.csv", HEADER, rows))
    assert [c.key for c in result.candidates] == by_hash(passing, PDMX_PREFIX)
    assert result.header == tuple(HEADER)
    assert result.path_column == "mxl"
    counts = (result.rows, result.passed, result.undecidable, result.failed)
    assert counts == (13, 3, 4, 6)

    by_path = pdmx_candidates(tmp_path / "PDMX.csv", path_column="path")
    assert {c.key for c in by_path.candidates} == {
        "./data/1/a.json",
        "./data/2/b.json",
        "./data/3/c.json",
    }


def test_pdmx_header_without_a_column_fails(tmp_path: Path) -> None:
    """Refuse a header that lacks a required column, listing the header."""
    header = [name for name in HEADER if name != "has_lyrics"]
    path = write_csv(tmp_path / "PDMX.csv", header, [])
    with pytest.raises(CorpusError, match=r"has_lyrics.*'subset:all_valid'"):
        pdmx_candidates(path)
    with pytest.raises(CorpusError, match="mxl_path"):
        pdmx_candidates(tmp_path / "PDMX.csv", path_column="mxl_path")


def add(archive: tarfile.TarFile, name: str, data: bytes = b"") -> None:
    """Add a regular-file member."""
    member = tarfile.TarInfo(name)
    member.size = len(data)
    archive.addfile(member, io.BytesIO(data))


@pytest.fixture
def confinement(tmp_path: Path) -> Confinement:
    """Confine writes to a run and a download directory."""
    return Confinement.create(tmp_path / "run", tmp_path / "downloads")


def test_tar_extraction_writes_only_requested_safe_files(
    tmp_path: Path,
    confinement: Confinement,
) -> None:
    """Match keys through ``./``, never writing unsafe or other members."""
    tar_path = tmp_path / "mxl.tar.gz"
    with tarfile.open(tar_path, "w:gz") as archive:
        folder = tarfile.TarInfo("./mxl")
        folder.type = tarfile.DIRTYPE
        archive.addfile(folder)
        add(archive, "./mxl/1/a.mxl", b"first")
        add(archive, "mxl/2/b.mxl", b"second")
        add(archive, "../mxl/1/a.mxl", b"escape")
        add(archive, "/mxl/2/b.mxl", b"absolute")
        link = tarfile.TarInfo("./mxl/3/c.mxl")
        link.type = tarfile.SYMTYPE
        link.linkname = "../../../outside"
        archive.addfile(link)
        add(archive, "./mxl/4/unrequested.mxl", b"other")
    dest = confinement.downloads / "pdmx"
    keys = ["./mxl/1/a.mxl", "./mxl/2/b.mxl", "./mxl/3/c.mxl", "./mxl/9/z.mxl"]

    result = extract_members(tar_path, keys, dest, confinement=confinement)

    assert result.paths == {
        "./mxl/1/a.mxl": dest / "mxl" / "1" / "a.mxl",
        "./mxl/2/b.mxl": dest / "mxl" / "2" / "b.mxl",
    }
    assert result.paths["./mxl/1/a.mxl"].read_bytes() == b"first"
    assert result.paths["./mxl/2/b.mxl"].read_bytes() == b"second"
    assert result.missing == ("./mxl/3/c.mxl", "./mxl/9/z.mxl")
    assert result.unsafe == ("../mxl/1/a.mxl", "/mxl/2/b.mxl")
    assert result.first_members[:3] == ("./mxl", "./mxl/1/a.mxl", "mxl/2/b.mxl")
    assert result.members == 7  # noqa: PLR2004 - the members added above
    written = sorted(
        p.relative_to(tmp_path).as_posix()
        for p in tmp_path.rglob("*")
        if p.is_file()
    )
    assert written == [
        "downloads/pdmx/mxl/1/a.mxl",
        "downloads/pdmx/mxl/2/b.mxl",
        "mxl.tar.gz",
    ]


def test_tar_extraction_refuses_unsafe_or_ambiguous_keys(
    tmp_path: Path,
    confinement: Confinement,
) -> None:
    """Refuse a key outside the destination and repeated members."""
    tar_path = tmp_path / "mxl.tar.gz"
    with tarfile.open(tar_path, "w:gz") as archive:
        add(archive, "./mxl/a.mxl", b"one")
        add(archive, "mxl/a.mxl", b"two")
    dest = confinement.downloads / "pdmx"
    with pytest.raises(CorpusError, match="safe relative path"):
        extract_members(tar_path, ["../a.mxl"], dest, confinement=confinement)
    with pytest.raises(CorpusError, match="only by case"):
        extract_members(
            tar_path,
            ["mxl/a.mxl", "MXL/a.mxl"],
            dest,
            confinement=confinement,
        )
    with pytest.raises(CorpusError, match="repeats the member"):
        extract_members(tar_path, ["mxl/a.mxl"], dest, confinement=confinement)


CONTAINER = (
    '<?xml version="1.0" encoding="UTF-8"?>\n'
    "<container><rootfiles>"
    '<rootfile full-path="{path}"'
    ' media-type="application/vnd.recordare.musicxml+xml"/>'
    '<rootfile full-path="other.musicxml"/>'
    "</rootfiles></container>"
)


def mxl(members: dict[str, bytes]) -> bytes:
    """Return the bytes of a zip archive with the given members."""
    buffer = io.BytesIO()
    with zipfile.ZipFile(buffer, "w", zipfile.ZIP_DEFLATED) as archive:
        for name, data in members.items():
            archive.writestr(name, data)
    return buffer.getvalue()


def test_mxl_root_is_the_first_rootfile() -> None:
    """Return the first rootfile that the container names."""
    data = mxl(
        {
            "META-INF/container.xml": CONTAINER.format(
                path="score.musicxml"
            ).encode(),
            "score.musicxml": b"<score-partwise/>",
            "other.musicxml": b"<other/>",
        },
    )
    assert mxl_root(data) == b"<score-partwise/>"


@pytest.mark.parametrize(
    ("data", "reason"),
    [
        (b"not a zip archive", "unreadable"),
        (mxl({"score.musicxml": b"<score-partwise/>"}), "no member"),
        (
            mxl(
                {
                    "META-INF/container.xml": CONTAINER.format(
                        path="../score.musicxml",
                    ).encode(),
                },
            ),
            "unsafe rootfile",
        ),
        (
            mxl(
                {
                    "META-INF/container.xml": CONTAINER.format(
                        path="absent.musicxml",
                    ).encode(),
                },
            ),
            "no member 'absent.musicxml'",
        ),
        (
            mxl(
                {
                    "META-INF/container.xml": (
                        b'<!DOCTYPE container [<!ENTITY x "y">]><container/>'
                    ),
                },
            ),
            "internal subset",
        ),
        (
            mxl({"META-INF/container.xml": b"<container><rootfiles/>"}),
            "not well-formed",
        ),
        (
            mxl(
                {
                    "META-INF/container.xml": (
                        b"<container><rootfiles/></container>"
                    ),
                },
            ),
            "names no rootfile",
        ),
    ],
)
def test_mxl_root_fails_closed(data: bytes, reason: str) -> None:
    """Refuse an archive whose root document cannot be found safely."""
    with pytest.raises(CorpusError, match=reason):
        mxl_root(data)


def test_mxl_root_bounds_the_document(monkeypatch: pytest.MonkeyPatch) -> None:
    """Refuse a root document larger than the bound."""
    monkeypatch.setattr(corpora, "MAX_DOCUMENT_BYTES", 100)
    data = mxl(
        {
            "META-INF/container.xml": b"<container><rootfiles><rootfile"
            b' full-path="s.xml"/></rootfiles></container>',
            "s.xml": b"<score-partwise>" + b" " * 100 + b"</score-partwise>",
        },
    )
    with pytest.raises(CorpusError, match=r"'s\.xml' is larger than"):
        mxl_root(data)


TOP = "OpenScore-Lieder-f44e962"
SCORES = [
    "Lieder/scores/Composer,_A/Set/1/lc1.mscx",
    "Lieder/scores/Composer,_A/Set/2/lc2.mscx",
    "Lieder/scores/Composer,_B/Song/lc3.mscx",
]


def lieder_zip(
    path: Path, comment: bytes, tops: tuple[str, ...] = (TOP,)
) -> Path:
    """Write a repository archive as GitHub makes them."""
    with zipfile.ZipFile(path, "w") as archive:
        for top in tops:
            archive.writestr(f"{top}/", b"")
            archive.writestr(f"{top}/README.md", b"# Lieder\n")
            for key in SCORES:
                archive.writestr(
                    f"{top}/{key}", f"<museScore>{key}</museScore>"
                )
        archive.comment = comment
    return path


def test_lieder_candidates_strip_the_top_directory(tmp_path: Path) -> None:
    """List the scores by repository path and confirm the commit."""
    path = lieder_zip(tmp_path / "Lieder.zip", LIEDER_COMMIT.encode())
    archive = lieder_candidates(path)
    assert [c.key for c in archive.candidates] == by_hash(SCORES, LIEDER_PREFIX)
    assert (archive.top, archive.comment) == (TOP, LIEDER_COMMIT)
    assert (archive.members, archive.commit_matches) == (5, True)
    assert read_member(path, SCORES[2]) == (
        f"<museScore>{SCORES[2]}</museScore>".encode()
    )
    with pytest.raises(CorpusError, match="no member"):
        read_member(path, "Lieder/scores/absent.mscx")


def test_lieder_commit_is_checked_when_present(tmp_path: Path) -> None:
    """Refuse another commit, and record an absent one as unknown."""
    other = lieder_zip(tmp_path / "other.zip", b"0" * 40)
    with pytest.raises(CorpusError, match="names the commit"):
        lieder_candidates(other)
    absent = lieder_zip(tmp_path / "absent.zip", b"")
    assert lieder_candidates(absent).commit_matches is None
    two = lieder_zip(tmp_path / "two.zip", b"", (TOP, "Other"))
    with pytest.raises(CorpusError, match="single top-level"):
        lieder_candidates(two)


DOCTYPE = (
    '<!DOCTYPE score-partwise PUBLIC "-//Recordare//DTD MusicXML 4.0'
    ' Partwise//EN" "http://www.musicxml.org/dtds/partwise.dtd">'
)


def score_part(identifier: str, name: str, sound: str) -> str:
    """Return a ``score-part`` with one instrument."""
    return (
        f'<score-part id="{identifier}"><part-name>{name}</part-name>'
        f'<score-instrument id="{identifier}-I1">'
        f"<instrument-name>{name}</instrument-name>"
        f"<instrument-sound>{sound}</instrument-sound>"
        "</score-instrument></score-part>"
    )


def part(identifier: str, step: str) -> str:
    """Return a one-measure ``part``."""
    return (
        f'<part id="{identifier}"><measure number="1"><attributes>'
        "<divisions>1</divisions></attributes><note><pitch>"
        f"<step>{step}</step><octave>4</octave></pitch><duration>4</duration>"
        "<type>whole</type></note></measure></part>"
    )


def lied(voice: str, piano: str, violin: str) -> bytes:
    """Return a voice, piano and violin score with a part group."""
    return (
        '<?xml version="1.0" encoding="UTF-8" standalone="no"?>\n'
        f"{DOCTYPE}\n"
        '<score-partwise version="4.0">'
        "<work><work-title>Song</work-title></work>"
        "<movement-title>Song</movement-title>"
        '<identification><creator type="composer">Nobody</creator>'
        "</identification>"
        "<defaults><scaling><millimeters>7</millimeters><tenths>40</tenths>"
        "</scaling></defaults>"
        '<credit page="1"><credit-type>title</credit-type>'
        "<credit-words>Song</credit-words></credit>"
        '<credit page="1"><credit-words>Nobody</credit-words></credit>'
        "<part-list>"
        f"{score_part('P1', 'Voice', voice)}"
        '<part-group type="start" number="1"><group-symbol>brace'
        "</group-symbol></part-group>"
        f"{score_part('P2', 'Piano', piano)}"
        f"{score_part('P3', 'Violin', violin)}"
        '<part-group type="stop" number="1"/>'
        "</part-list>"
        f"{part('P1', 'E')}{part('P2', 'C')}{part('P3', 'G')}"
        "</score-partwise>"
    ).encode()


def test_piano_extraction_keeps_one_part_and_the_header() -> None:
    """Keep the piano part, its entry, the credits and the header."""
    source = lied("voice.soprano", "keyboard.piano.grand", "strings.violin")
    extract = extract_piano(source)
    assert extract.startswith(
        f'<?xml version="1.0" encoding="UTF-8"?>\n{DOCTYPE}\n'.encode(),
    )
    root = ET.fromstring(extract)  # noqa: S314 - self-authored fixture
    original = ET.fromstring(source)  # noqa: S314 - self-authored fixture
    assert [child.tag for child in root] == [
        "work",
        "movement-title",
        "identification",
        "defaults",
        "credit",
        "credit",
        "part-list",
        "part",
    ]
    part_list = root.find("part-list")
    assert part_list is not None
    assert [(e.tag, e.get("id")) for e in part_list] == [("score-part", "P2")]
    kept = {ET.tostring(e) for e in root if e.tag in {"part", "credit"}}
    assert kept == {
        ET.tostring(e)
        for e in original
        if e.tag == "credit" or (e.tag == "part" and e.get("id") == "P2")
    }
    assert extract_piano(source) == extract
    assert extract_piano(extract) == extract


@pytest.mark.parametrize(
    ("voice", "piano", "violin"),
    [
        ("voice.soprano", "keyboard.organ", "strings.violin"),
        ("voice.soprano", "", "strings.violin"),
        ("voice.soprano", "keyboard.piano.grand", "keyboard.piano.upright"),
    ],
)
def test_piano_extraction_needs_exactly_one_piano(
    voice: str,
    piano: str,
    violin: str,
) -> None:
    """Exclude a score with no piano sound, even when named Piano, or two."""
    with pytest.raises(NoUniquePianoPart):
        extract_piano(lied(voice, piano, violin))
