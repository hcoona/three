"""Prepare the PDMX and OpenScore Lieder candidates of the comparison protocol.

The PDMX candidates are the rows of ``PDMX.csv`` that pass the protocol's
prefilter, keyed by their compressed MusicXML path; their files come out of
``mxl.tar.gz`` in one streaming pass, and each source score ``X`` is the root
document that the ``.mxl`` container names. The Lieder candidates are the
``.mscx`` files of the pinned repository archive, keyed by their
repository-relative path; after MuseScore exports one to MusicXML, which is
not done here, :func:`extract_piano` keeps its one piano part.

Both orders sort candidates by the SHA-256 of a seed prefix and the key. The
``PDMX.csv`` header, the layout of ``mxl.tar.gz`` and the Lieder file names
are open checks of the protocol, so the results carry the observations that
settle them for the run record. Every unexpected structure fails closed with
:class:`CorpusError`.
"""

from __future__ import annotations

import csv
import hashlib
import io
import lzma
import re
import tarfile
import xml.etree.ElementTree as ET
import zipfile
import zlib
from collections import Counter
from dataclasses import dataclass
from typing import TYPE_CHECKING, Final
from xml.parsers import expat

if TYPE_CHECKING:
    from collections.abc import Collection, Iterable, Iterator, Mapping
    from pathlib import Path

    from sheet_music_omr.harness.confine import Confinement

PDMX_PREFIX: Final = "w1-pdmx-v1:"
"""The seed prefix of the PDMX order."""

LIEDER_PREFIX: Final = "w1-lieder-v1:"
"""The seed prefix of the Lieder order."""

LIEDER_COMMIT: Final = "f44e962a1ff85ddcc2f6f42bed3b0c529555ee15"
"""The commit of the OpenScore Lieder release ``v3.0.0``."""

PDMX_LICENSES: Final = frozenset(
    {
        "https://creativecommons.org/publicdomain/zero/1.0/",
        "https://creativecommons.org/publicdomain/mark/1.0/",
    },
)
"""The ``license_url`` values that the prefilter accepts."""

PDMX_PATH_COLUMN: Final = "mxl"
"""The default ``PDMX.csv`` column of the compressed MusicXML path."""

PDMX_COLUMNS: Final = (
    "subset:no_license_conflict",
    "subset:all_valid",
    "license_url",
    "n_tracks",
    "tracks",
    "has_lyrics",
)
"""The prefilter columns that ``PDMX.csv`` must have."""

MAX_DOCUMENT_BYTES: Final = 64 * 1024 * 1024
"""The largest archive member that is read into memory."""

PIANO_SOUND_PREFIX: Final = "keyboard.piano"
"""The ``instrument-sound`` prefix that names a piano instrument."""

SAMPLE_MEMBERS: Final = 10
"""How many leading archive member names are kept as an observation."""

_CONTAINER: Final = "META-INF/container.xml"
# Characters that a Windows file name cannot hold.
_FILE_NAME_CHARACTERS: Final = frozenset(':*?"<>|')
_SHA: Final = re.compile(r"(?<![0-9A-Fa-f])[0-9A-Fa-f]{40}(?![0-9A-Fa-f])")
_COUNT: Final = re.compile(r"[0-9]+")
_FLAGS: Final = {"True": True, "False": False}
# The csv module stores the limit in a C long, which is 32 bits on Windows.
_FIELD_LIMIT: Final = 2**31 - 1
_ZIP_ERRORS: Final = (
    zipfile.BadZipFile,
    zipfile.LargeZipFile,
    EOFError,
    OSError,
    RuntimeError,
    NotImplementedError,
    ValueError,
    zlib.error,
    lzma.LZMAError,
)
_TAR_ERRORS: Final = (tarfile.TarError, EOFError, OSError, zlib.error)


class CorpusError(Exception):
    """A corpus file is missing, malformed or not as the protocol expects."""


class NoUniquePianoPart(CorpusError):  # noqa: N818 - the exclusion reason
    """A Lieder export does not have exactly one piano part."""


# Order.


@dataclass(frozen=True, slots=True)
class Candidate:
    """A candidate with its position key in a seeded order.

    Attributes:
        key: The candidate's key, a PDMX or Lieder path.
        order: The lowercase hexadecimal SHA-256 of the UTF-8 bytes of the
            seed prefix followed by the key.
    """

    key: str
    order: str


def order_of(key: str, prefix: str) -> str:
    """Return the order value of a key under a seed prefix."""
    return hashlib.sha256((prefix + key).encode("utf-8")).hexdigest()


def ordered(keys: Iterable[str], prefix: str) -> list[Candidate]:
    """Return the candidates sorted by order value, ties broken by key.

    Raises:
        CorpusError: If a key repeats, so that it names no single candidate.
    """
    candidates = [Candidate(key, order_of(key, prefix)) for key in keys]
    counts = Counter(candidate.key for candidate in candidates)
    repeated = sorted(key for key, count in counts.items() if count > 1)
    if repeated:
        message = f"candidate keys repeat: {repeated[:SAMPLE_MEMBERS]}"
        raise CorpusError(message)
    return sorted(candidates, key=lambda c: (c.order, c.key))


# PDMX.


@dataclass(frozen=True, slots=True)
class PdmxPrefilter:
    """The PDMX candidates that pass the prefilter, with its observations.

    Attributes:
        candidates: The passing rows' keys in PDMX order.
        header: The ``PDMX.csv`` header, an open check of the protocol.
        path_column: The column read as the compressed MusicXML path.
        rows: The data rows read.
        passed: The rows that pass every clause.
        undecidable: The rows with a value that no clause can decide: a
            Boolean other than ``True`` or ``False``, an ``n_tracks`` that is
            not a whole number, a field count that differs from the
            header's, or an empty path in a row that passes otherwise.
    """

    candidates: tuple[Candidate, ...]
    header: tuple[str, ...]
    path_column: str
    rows: int
    passed: int
    undecidable: int

    @property
    def failed(self) -> int:
        """The rows that some clause decidedly rejects."""
        return self.rows - self.passed - self.undecidable


@dataclass(frozen=True, slots=True)
class _Columns:
    no_conflict: int
    all_valid: int
    license: int
    n_tracks: int
    tracks: int
    has_lyrics: int
    path: int
    width: int


def pdmx_candidates(
    csv_path: Path,
    *,
    path_column: str = PDMX_PATH_COLUMN,
) -> PdmxPrefilter:
    """Stream ``PDMX.csv`` and return the prefiltered, ordered candidates.

    A row passes when ``subset:no_license_conflict`` and ``subset:all_valid``
    are ``True``, ``license_url`` is one of :data:`PDMX_LICENSES`,
    ``n_tracks`` is 1, ``tracks`` is exactly ``0`` and ``has_lyrics`` is
    ``False``. The key is the path column's value exactly as recorded.

    Args:
        csv_path: The downloaded ``PDMX.csv``.
        path_column: The column of the compressed MusicXML path.

    Raises:
        CorpusError: If the file is unreadable, a required column is missing
            or repeated, or a passing key repeats.
    """
    previous = csv.field_size_limit(_FIELD_LIMIT)
    try:
        with csv_path.open(encoding="utf-8-sig", newline="") as stream:
            return _prefilter(csv.reader(stream), path_column)
    except (OSError, UnicodeDecodeError, csv.Error) as error:
        message = f"cannot read {csv_path}: {error}"
        raise CorpusError(message) from error
    finally:
        csv.field_size_limit(previous)


def _prefilter(rows: Iterator[list[str]], path_column: str) -> PdmxPrefilter:
    header = tuple(next(rows, []))
    columns = _columns(header, path_column)
    keys: list[str] = []
    count = undecidable = 0
    for row in rows:
        count += 1
        decision = _decide(row, columns)
        if decision is None:
            undecidable += 1
        elif decision:
            keys.append(row[columns.path])
    return PdmxPrefilter(
        tuple(ordered(keys, PDMX_PREFIX)),
        header,
        path_column,
        count,
        len(keys),
        undecidable,
    )


def _columns(header: tuple[str, ...], path_column: str) -> _Columns:
    required = (*PDMX_COLUMNS, path_column)
    missing = [name for name in required if name not in header]
    repeated = [name for name in required if header.count(name) > 1]
    if missing or repeated:
        message = (
            f"PDMX.csv lacks the required columns {missing} or repeats"
            f" {repeated}; its header is {list(header)}"
        )
        raise CorpusError(message)
    indexes = [header.index(name) for name in required]
    return _Columns(*indexes, width=len(header))


def _decide(row: list[str], at: _Columns) -> bool | None:
    if len(row) != at.width:
        return None
    no_conflict = _FLAGS.get(row[at.no_conflict])
    all_valid = _FLAGS.get(row[at.all_valid])
    has_lyrics = _FLAGS.get(row[at.has_lyrics])
    n_tracks = row[at.n_tracks]
    if (
        no_conflict is None
        or all_valid is None
        or has_lyrics is None
        or not _COUNT.fullmatch(n_tracks)
    ):
        return None
    passes = (
        no_conflict
        and all_valid
        and row[at.license] in PDMX_LICENSES
        and int(n_tracks) == 1
        and row[at.tracks] == "0"
        and not has_lyrics
    )
    if passes and not row[at.path]:
        return None
    return passes


@dataclass(frozen=True, slots=True)
class TarExtraction:
    """The members extracted from ``mxl.tar.gz``, with its observations.

    Attributes:
        paths: The written file of each requested key that was found.
        missing: The requested keys with no regular-file member.
        oversized: The requested keys whose member is larger than
            :data:`MAX_DOCUMENT_BYTES`, which are not written.
        unsafe: The regular-file member names that are not safe relative
            paths to write: absolute, with an empty, ``.`` or ``..`` segment,
            or with a character that a Windows file name cannot hold. They
            are never written.
        first_members: The first :data:`SAMPLE_MEMBERS` member names, as the
            archive records them, an observation of its layout.
        members: The number of members in the archive.
    """

    paths: Mapping[str, Path]
    missing: tuple[str, ...]
    oversized: tuple[str, ...]
    unsafe: tuple[str, ...]
    first_members: tuple[str, ...]
    members: int


def extract_members(
    tar_path: Path,
    keys: Collection[str],
    dest: Path,
    *,
    confinement: Confinement,
) -> TarExtraction:
    """Write the requested members of a ``.tar.gz`` in one streaming pass.

    A key and a member name match when they are equal after removing leading
    ``./``. Each match is written to ``dest`` under its normalized path. Only
    regular files are written, and nothing else in the archive is.

    Args:
        tar_path: The downloaded ``mxl.tar.gz``.
        keys: The requested keys, as ``PDMX.csv`` records them.
        dest: The directory to write into, inside the confinement.
        confinement: The run's write confinement.

    Raises:
        CorpusError: If the archive is unreadable, a key is unsafe as a path,
            two keys differ only by case, or a requested member repeats.
    """
    scan = _TarScan(_wanted(keys), confinement.check(dest), confinement)
    try:
        with tarfile.open(tar_path, mode="r|gz") as archive:
            for member in archive:
                scan.visit(archive, member)
    except _TAR_ERRORS as error:
        message = f"cannot extract from {tar_path}: {error}"
        raise CorpusError(message) from error
    paths: dict[str, Path] = {}
    missing: list[str] = []
    oversized: list[str] = []
    for name, originals in scan.wanted.items():
        for key in originals:
            if name in scan.found:
                paths[key] = scan.found[name]
            elif name in scan.oversized:
                oversized.append(key)
            else:
                missing.append(key)
    return TarExtraction(
        paths,
        tuple(sorted(missing)),
        tuple(sorted(oversized)),
        tuple(scan.unsafe),
        tuple(scan.first),
        scan.members,
    )


def _normalize(name: str) -> str:
    while name.startswith("./"):
        name = name[2:]
    return name


def _relative_parts(name: str) -> tuple[str, ...] | None:
    """Return the segments of a relative POSIX path that stays inside."""
    if not name or name.startswith("/") or any(c in name for c in "\\\x00"):
        return None
    parts = tuple(name.split("/"))
    if any(part in {"", ".", ".."} for part in parts):
        return None
    return parts


def _safe_parts(name: str) -> tuple[str, ...] | None:
    """Return the segments of a relative path that is safe to write."""
    if not _FILE_NAME_CHARACTERS.isdisjoint(name):
        return None
    return _relative_parts(name)


def _wanted(keys: Iterable[str]) -> dict[str, list[str]]:
    wanted: dict[str, list[str]] = {}
    folded: dict[str, str] = {}
    for key in keys:
        name = _normalize(key)
        if _safe_parts(name) is None:
            message = f"the key is not a safe relative path: {key!r}"
            raise CorpusError(message)
        # The download directory may be on a case-insensitive file system.
        other = folded.setdefault(name.casefold(), name)
        if other != name:
            message = f"the keys {other!r} and {name!r} differ only by case"
            raise CorpusError(message)
        wanted.setdefault(name, []).append(key)
    return wanted


class _TarScan:
    def __init__(
        self,
        wanted: dict[str, list[str]],
        root: Path,
        confinement: Confinement,
    ) -> None:
        self.wanted = wanted
        self.root = root
        self.confinement = confinement
        self.found: dict[str, Path] = {}
        self.seen: set[str] = set()
        self.oversized: set[str] = set()
        self.unsafe: list[str] = []
        self.first: list[str] = []
        self.members = 0

    def visit(self, archive: tarfile.TarFile, member: tarfile.TarInfo) -> None:
        self.members += 1
        if len(self.first) < SAMPLE_MEMBERS:
            self.first.append(member.name)
        if not member.isfile():
            return
        name = _normalize(member.name)
        parts = _safe_parts(name)
        if parts is None:
            self.unsafe.append(member.name)
            return
        if name not in self.wanted:
            return
        if name in self.seen:
            message = f"the archive repeats the member {member.name!r}"
            raise CorpusError(message)
        self.seen.add(name)
        if member.size > MAX_DOCUMENT_BYTES:
            self.oversized.add(name)
            return
        stream = archive.extractfile(member)
        if stream is None:
            return
        with stream:
            data = stream.read()
        target = self.root.joinpath(*parts)
        self.found[name] = self.confinement.write_bytes(target, data)


def mxl_root(data: bytes) -> bytes:
    """Return the root MusicXML document of a compressed MusicXML file.

    The root document is the first ``rootfile`` of ``META-INF/container.xml``,
    which the MusicXML specification makes the main score.

    Raises:
        CorpusError: If the file is not a readable zip archive, the container
            is missing, malformed or names no safe rootfile, or the rootfile
            is missing or larger than :data:`MAX_DOCUMENT_BYTES`.
    """
    try:
        with zipfile.ZipFile(io.BytesIO(data)) as archive:
            container = _read_zip_member(archive, _CONTAINER)
            path = _rootfile(_parse_xml(container, _CONTAINER)[0])
            return _read_zip_member(archive, path)
    except _ZIP_ERRORS as error:
        message = f"the compressed MusicXML file is unreadable: {error}"
        raise CorpusError(message) from error


def _rootfile(container: ET.Element) -> str:
    rootfile = (
        container.find("rootfiles/rootfile")
        if container.tag == "container"
        else None
    )
    path = None if rootfile is None else rootfile.get("full-path")
    if not path:
        message = f"{_CONTAINER} names no rootfile"
        raise CorpusError(message)
    if _relative_parts(path) is None:
        message = f"{_CONTAINER} names an unsafe rootfile: {path!r}"
        raise CorpusError(message)
    return path


def _read_zip_member(archive: zipfile.ZipFile, name: str) -> bytes:
    try:
        info = archive.getinfo(name)
    except KeyError as error:
        message = f"the archive has no member {name!r}"
        raise CorpusError(message) from error
    if info.file_size > MAX_DOCUMENT_BYTES:
        message = f"the member {name!r} is larger than {MAX_DOCUMENT_BYTES}"
        raise CorpusError(message)
    with archive.open(info) as stream:
        data = stream.read(MAX_DOCUMENT_BYTES + 1)
    if len(data) > MAX_DOCUMENT_BYTES:
        message = f"the member {name!r} is larger than {MAX_DOCUMENT_BYTES}"
        raise CorpusError(message)
    return data


class _DeclarationError(Exception):
    pass


def _parse_xml(raw: bytes, what: str) -> tuple[ET.Element, str | None]:
    """Parse XML that declares no entities and has no internal DTD subset.

    Returns:
        The root element and the document type declaration, rebuilt from its
        name and identifiers, or ``None`` if the document has none.
    """
    doctypes: list[str] = []

    def doctype(
        name: str,
        system: str | None,
        public: str | None,
        internal: bool,  # noqa: FBT001
    ) -> None:
        if internal:
            raise _DeclarationError
        doctypes.append(_doctype(name, system, public))

    def entity(*_args: object) -> None:
        raise _DeclarationError

    scanner = expat.ParserCreate()
    scanner.StartDoctypeDeclHandler = doctype
    scanner.EntityDeclHandler = entity
    try:
        scanner.Parse(raw, True)  # noqa: FBT003
        # The scan has refused every entity declaration, and ElementTree
        # never fetches an external DTD.
        root = ET.fromstring(raw)  # noqa: S314
    except (expat.ExpatError, ET.ParseError) as error:
        message = f"{what} is not well-formed XML: {error}"
        raise CorpusError(message) from error
    except _DeclarationError as error:
        message = f"{what} declares a DTD internal subset or an entity"
        raise CorpusError(message) from error
    return root, (doctypes[0] if doctypes else None)


def _doctype(name: str, system: str | None, public: str | None) -> str:
    words = [name]
    if public is not None:
        # A public identifier cannot contain a double quote.
        words += ["PUBLIC", f'"{public}"']
    elif system is not None:
        words.append("SYSTEM")
    if system is not None:
        quote = "'" if '"' in system else '"'
        words.append(f"{quote}{system}{quote}")
    return f"<!DOCTYPE {' '.join(words)}>"


# OpenScore Lieder.


@dataclass(frozen=True, slots=True)
class LiederArchive:
    """The Lieder candidates of the repository archive, with observations.

    Attributes:
        candidates: The ``.mscx`` files' repository-relative paths in Lieder
            order.
        top: The archive's single top-level directory.
        comment: The archive comment, where GitHub archives store the commit.
        members: The number of members in the archive.
        commit_matches: ``True`` when the comment or the top-level directory
            holds a 40-digit hexadecimal commit, which then equals
            :data:`LIEDER_COMMIT`, and ``None`` when neither holds one.
    """

    candidates: tuple[Candidate, ...]
    top: str
    comment: str
    members: int
    commit_matches: bool | None


def lieder_candidates(zip_path: Path) -> LiederArchive:
    """List the ``.mscx`` files of the Lieder archive in Lieder order.

    Each key is the member's path without the archive's single top-level
    directory, which is its path in the repository.

    Raises:
        CorpusError: If the archive is unreadable, has other than one
            top-level directory, holds an unsafe ``.mscx`` path or names a
            commit other than :data:`LIEDER_COMMIT`.
    """
    try:
        with zipfile.ZipFile(zip_path) as archive:
            names = archive.namelist()
            raw_comment = archive.comment
    except _ZIP_ERRORS as error:
        message = f"cannot read {zip_path}: {error}"
        raise CorpusError(message) from error
    top = _single_top(names)
    keys = [
        name[len(top) + 1 :]
        for name in names
        if name.endswith(".mscx") and not name.endswith("/")
    ]
    unsafe = [key for key in keys if _relative_parts(key) is None]
    if unsafe:
        message = f"the archive holds unsafe .mscx paths: {unsafe[:10]}"
        raise CorpusError(message)
    comment = raw_comment.decode("utf-8", errors="replace")
    commits = {sha.lower() for sha in _SHA.findall(f"{comment}\n{top}")}
    if commits - {LIEDER_COMMIT}:
        message = (
            f"the archive names the commit {sorted(commits)}, not"
            f" {LIEDER_COMMIT}"
        )
        raise CorpusError(message)
    return LiederArchive(
        tuple(ordered(keys, LIEDER_PREFIX)),
        top,
        comment,
        len(names),
        commit_matches=True if commits else None,
    )


def read_member(zip_path: Path, key: str) -> bytes:
    """Return the bytes of a Lieder file by its repository-relative path.

    Raises:
        CorpusError: If the archive is unreadable, the key is unsafe or
            missing, or the file is larger than :data:`MAX_DOCUMENT_BYTES`.
    """
    if _relative_parts(key) is None:
        message = f"the key is not a safe relative path: {key!r}"
        raise CorpusError(message)
    try:
        with zipfile.ZipFile(zip_path) as archive:
            top = _single_top(archive.namelist())
            return _read_zip_member(archive, f"{top}/{key}")
    except _ZIP_ERRORS as error:
        message = f"cannot read {key!r} from {zip_path}: {error}"
        raise CorpusError(message) from error


def _single_top(names: list[str]) -> str:
    tops = {name.split("/", 1)[0] for name in names}
    top = next(iter(tops)) if len(tops) == 1 else ""
    if (
        _relative_parts(top) is None
        or not names
        or any(not name.startswith(f"{top}/") for name in names)
    ):
        message = (
            "the archive does not have a single top-level directory:"
            f" {sorted(tops)[:10]}"
        )
        raise CorpusError(message)
    return top


def extract_piano(data: bytes) -> bytes:
    """Keep the one piano part of a MusicXML export of a Lieder score.

    The piano part is the ``part`` whose ``score-part`` has a
    ``score-instrument/instrument-sound`` that starts with ``keyboard.piano``.
    The result keeps that part and its ``score-part`` and every score-level
    element, including ``work``, ``movement-number``, ``movement-title``,
    ``identification``, ``defaults`` and each ``credit``. It drops every
    other ``part``, and every other ``score-part`` and every ``part-group``
    of the ``part-list``. The result is UTF-8 with an XML declaration and the
    input's document type declaration; comments and processing instructions
    are dropped. The same input always gives the same bytes.

    Raises:
        NoUniquePianoPart: If not exactly one ``score-part`` names a piano, or
            not exactly one ``part`` has its identifier.
        CorpusError: If the input is not a well-formed ``score-partwise``
            document with a ``part-list``.
    """
    root, doctype = _parse_xml(data, "the MusicXML export")
    part_list = root.find("part-list") if root.tag == "score-partwise" else None
    if part_list is None:
        message = "the export is not a score-partwise document with parts"
        raise CorpusError(message)
    pianos = [
        entry
        for entry in part_list.findall("score-part")
        if any(
            (sound.text or "").startswith(PIANO_SOUND_PREFIX)
            for sound in entry.iterfind("score-instrument/instrument-sound")
        )
    ]
    if len(pianos) != 1:
        message = f"{len(pianos)} score-parts name a piano instrument"
        raise NoUniquePianoPart(message)
    piano = pianos[0]
    identifier = piano.get("id")
    parts = [
        part for part in root.findall("part") if part.get("id") == identifier
    ]
    if not identifier or len(parts) != 1:
        message = f"{len(parts)} parts have the piano identifier {identifier!r}"
        raise NoUniquePianoPart(message)
    for child in list(part_list):
        if child is not piano:
            part_list.remove(child)
    for part in root.findall("part"):
        if part is not parts[0]:
            root.remove(part)
    head = '<?xml version="1.0" encoding="UTF-8"?>\n'
    if doctype is not None:
        head += f"{doctype}\n"
    body = ET.tostring(root, encoding="unicode")
    return f"{head}{body}\n".encode()
