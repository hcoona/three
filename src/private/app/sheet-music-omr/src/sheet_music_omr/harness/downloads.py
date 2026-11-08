"""Download the protocol's corpus files from Zenodo under the W1 limit.

The comparison protocol downloads ``PDMX.csv`` and ``mxl.tar.gz`` of PDMX v9
and the OpenScore Lieder ``v3.0.0`` archive into one download directory that
reruns reuse. A ledger in the run directory starts from the W1 cumulative
download total and records every download with the bytes it received. A
download that would take the total above :data:`LIMIT` is refused before any
byte is fetched, which is a stop condition of the protocol.

Each file is fetched once, with no retry, from the Zenodo record's file
download URL ``https://zenodo.org/records/<record>/files/<key>?download=1``,
and is accepted only when its size and MD5 checksum equal the values that
Zenodo publishes for it.
"""

from __future__ import annotations

import hashlib
import http.client
import json
import re
import urllib.request
from dataclasses import dataclass
from datetime import UTC, datetime
from typing import TYPE_CHECKING, Final, TypeGuard
from urllib.parse import quote

from sheet_music_omr.harness.stops import StopConditionError

if TYPE_CHECKING:
    from collections.abc import Callable, Iterable, Iterator
    from pathlib import Path

    from sheet_music_omr.harness.confine import Confinement

LIMIT: Final = 20_000_000_000
"""The W1 cumulative download limit in bytes."""

CHUNK_BYTES: Final = 1024 * 1024
"""The size of one read of the default fetcher."""

TIMEOUT_SECONDS: Final = 60.0
"""The timeout of each blocking network operation of the default fetcher."""

USER_AGENT: Final = "sheet-music-omr-harness/1 (W1 round-trip measurement)"
"""The ``User-Agent`` header of the default fetcher."""

LEDGER_FORMAT: Final = "w1-download-ledger-v1"
"""The format tag of the ledger file."""

_RECORD = re.compile(r"[0-9]+")
_MD5 = re.compile(r"[0-9a-f]{32}")
_UNSAFE_NAME_CHARACTERS = frozenset('\\:*?"<>|\x00')


class DownloadLimitError(StopConditionError):
    """A download would take the W1 cumulative total above the limit."""


class DownloadError(Exception):
    """A download failed, or its size or checksum is not the published one."""


class LedgerError(Exception):
    """The ledger file is missing, malformed or would be overwritten."""


@dataclass(frozen=True, slots=True)
class ZenodoFile:
    """A file of a Zenodo record with its published size and checksum.

    Attributes:
        record: The Zenodo record identifier.
        key: The file key in the record, which may contain ``/``.
        size: The published size in bytes.
        md5: The published MD5 checksum, in lowercase hexadecimal.
    """

    record: str
    key: str
    size: int
    md5: str

    def __post_init__(self) -> None:
        """Refuse a record, key, size or checksum that is not well formed.

        Raises:
            ValueError: If a field is malformed.
        """
        segments = self.key.split("/")
        if not _RECORD.fullmatch(self.record):
            message = f"malformed Zenodo record identifier: {self.record!r}"
            raise ValueError(message)
        if any(
            segment in {"", ".", ".."}
            or not _UNSAFE_NAME_CHARACTERS.isdisjoint(segment)
            for segment in segments
        ):
            message = f"unsafe Zenodo file key: {self.key!r}"
            raise ValueError(message)
        if isinstance(self.size, bool) or self.size <= 0:
            message = f"malformed published size: {self.size!r}"
            raise ValueError(message)
        if not _MD5.fullmatch(self.md5):
            message = f"malformed published MD5 checksum: {self.md5!r}"
            raise ValueError(message)

    @property
    def name(self) -> str:
        """The local file name, which is the last segment of the key."""
        return self.key.rsplit("/", 1)[-1]

    @property
    def url(self) -> str:
        """The download URL, with each segment of the key quoted."""
        path = "/".join(quote(part, safe="") for part in self.key.split("/"))
        return (
            f"https://zenodo.org/records/{self.record}/files/{path}?download=1"
        )


PDMX_CSV: Final = ZenodoFile(
    "15571083",
    "PDMX.csv",
    225_399_738,
    "30392ccf38bb63ce70e7afae70f9c88c",
)
"""The PDMX v9 metadata table."""

PDMX_MXL: Final = ZenodoFile(
    "15571083",
    "mxl.tar.gz",
    1_894_335_797,
    "49ffd75ecf5489c0be6d41182eb11ff7",
)
"""The PDMX v9 archive of compressed MusicXML files."""

LIEDER_ZIP: Final = ZenodoFile(
    "15450144",
    "OpenScore/Lieder-v3.0.0.zip",
    23_508_917,
    "5c48db6f4ac09e99843a4e24bedbfafe",
)
"""The OpenScore Lieder ``v3.0.0`` repository archive."""


@dataclass(frozen=True, slots=True)
class LedgerEntry:
    """One download attempt, successful or not.

    Attributes:
        key: The Zenodo file key.
        record: The Zenodo record identifier.
        url: The URL that was fetched.
        received: The bytes received, which count toward the total even when
            the download failed.
        md5: The MD5 checksum of the received bytes.
        ok: Whether the file was accepted.
        at: The UTC time at which the attempt ended, in ISO 8601.
    """

    key: str
    record: str
    url: str
    received: int
    md5: str
    ok: bool
    at: str


class Ledger:
    """The download ledger of a run, kept as a JSON file in the run directory.

    Attributes:
        path: The ledger file.
        prior_total: The W1 cumulative download total before this ledger.
        basis: How the prior total was established.
    """

    def __init__(
        self,
        path: Path,
        prior_total: int,
        basis: str,
        entries: Iterable[LedgerEntry] = (),
    ) -> None:
        """Hold a ledger; use :meth:`create` or :meth:`load` to obtain one."""
        self.path = path
        self.prior_total = prior_total
        self.basis = basis
        self._entries = tuple(entries)

    @classmethod
    def create(
        cls,
        path: Path,
        *,
        prior_total: int,
        basis: str,
        confinement: Confinement,
    ) -> Ledger:
        """Start a ledger file in the run directory and return it.

        Args:
            path: The ledger file, which must lie in the run directory and
                must not exist.
            prior_total: The W1 cumulative download total so far, in bytes.
            basis: A statement of how the prior total was established, such
                as whether it is a stated allowance or an inference.
            confinement: The run's write confinement.

        Raises:
            LedgerError: If the file exists, lies outside the run directory or
                the prior total or basis is malformed.
        """
        resolved = confinement.check(path)
        if not resolved.is_relative_to(confinement.run):
            message = f"the ledger must lie in the run directory: {path}"
            raise LedgerError(message)
        if resolved.exists():
            message = f"the ledger already exists: {path}"
            raise LedgerError(message)
        if isinstance(prior_total, bool) or not isinstance(prior_total, int):
            message = f"malformed prior total: {prior_total!r}"
            raise LedgerError(message)
        if prior_total < 0 or not basis.strip():
            message = "the prior total must be non-negative, with a basis"
            raise LedgerError(message)
        _write_ledger(resolved, prior_total, basis, (), confinement)
        return cls(resolved, prior_total, basis)

    @classmethod
    def load(cls, path: Path) -> Ledger:
        """Read a ledger file.

        Raises:
            LedgerError: If the file is missing or malformed.
        """
        try:
            data = json.loads(path.read_bytes())
        except (OSError, ValueError) as error:
            message = f"cannot read the ledger {path}: {error}"
            raise LedgerError(message) from error
        return _ledger_from_json(path.resolve(), data)

    @property
    def entries(self) -> tuple[LedgerEntry, ...]:
        """The recorded download attempts, oldest first."""
        return self._entries

    @property
    def total(self) -> int:
        """The prior total plus the bytes received by every entry."""
        return self.prior_total + sum(entry.received for entry in self._entries)

    def record(self, entry: LedgerEntry, *, confinement: Confinement) -> None:
        """Append an entry and persist the ledger atomically."""
        entries = (*self._entries, entry)
        _write_ledger(
            self.path,
            self.prior_total,
            self.basis,
            entries,
            confinement,
        )
        self._entries = entries


def _write_ledger(
    path: Path,
    prior_total: int,
    basis: str,
    entries: tuple[LedgerEntry, ...],
    confinement: Confinement,
) -> None:
    data = {
        "format": LEDGER_FORMAT,
        "limit": LIMIT,
        "prior_total": prior_total,
        "basis": basis,
        "entries": [
            {
                "key": entry.key,
                "record": entry.record,
                "url": entry.url,
                "received": entry.received,
                "md5": entry.md5,
                "ok": entry.ok,
                "at": entry.at,
            }
            for entry in entries
        ],
        "total": prior_total + sum(entry.received for entry in entries),
    }
    text = json.dumps(data, indent=2, ensure_ascii=False) + "\n"
    confinement.write_text(path, text)


def _ledger_from_json(path: Path, data: object) -> Ledger:
    if (
        not isinstance(data, dict)
        or data.get("format") != LEDGER_FORMAT
        or data.get("limit") != LIMIT
    ):
        message = f"the ledger {path} is not a {LEDGER_FORMAT} ledger"
        raise LedgerError(message)
    prior_total = data.get("prior_total")
    basis = data.get("basis")
    raw_entries = data.get("entries")
    if (
        not _is_count(prior_total)
        or not isinstance(basis, str)
        or not isinstance(raw_entries, list)
    ):
        message = f"the ledger {path} is malformed"
        raise LedgerError(message)
    entries = tuple(_entry_from_json(path, raw) for raw in raw_entries)
    return Ledger(path, prior_total, basis, entries)


def _entry_from_json(path: Path, raw: object) -> LedgerEntry:
    if not isinstance(raw, dict):
        message = f"the ledger {path} has a malformed entry"
        raise LedgerError(message)
    strings = [raw.get(name) for name in ("key", "record", "url", "md5", "at")]
    received = raw.get("received")
    ok = raw.get("ok")
    if (
        not all(isinstance(value, str) for value in strings)
        or not _is_count(received)
        or not isinstance(ok, bool)
    ):
        message = f"the ledger {path} has a malformed entry: {raw!r}"
        raise LedgerError(message)
    key, record, url, md5, at = (str(value) for value in strings)
    return LedgerEntry(key, record, url, received, md5, ok=ok, at=at)


def _is_count(value: object) -> TypeGuard[int]:
    return isinstance(value, int) and not isinstance(value, bool) and value >= 0


def urllib_fetch(url: str) -> Iterator[bytes]:
    """Stream the body of an HTTPS URL in chunks of :data:`CHUNK_BYTES`.

    Raises:
        DownloadError: If the URL is not HTTPS, the server answers with an
            HTTP error or the connection fails or times out.
    """
    if not url.startswith("https://"):
        message = f"refusing a non-HTTPS URL: {url}"
        raise DownloadError(message)
    request = urllib.request.Request(  # noqa: S310 - HTTPS checked above
        url,
        headers={"User-Agent": USER_AGENT},
    )
    try:
        with urllib.request.urlopen(  # noqa: S310 - HTTPS checked above
            request,
            timeout=TIMEOUT_SECONDS,
        ) as response:
            while chunk := response.read(CHUNK_BYTES):
                yield chunk
    except (OSError, http.client.HTTPException) as error:
        message = f"cannot fetch {url}: {error}"
        raise DownloadError(message) from error


def md5_file(path: Path) -> str:
    """Return the lowercase hexadecimal MD5 checksum of a file."""
    with path.open("rb") as stream:
        return hashlib.file_digest(
            stream,
            lambda: hashlib.md5(usedforsecurity=False),
        ).hexdigest()


def sha256_file(path: Path) -> str:
    """Return the lowercase hexadecimal SHA-256 digest of a file."""
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def _is_published(path: Path, file: ZenodoFile) -> bool:
    return (
        path.is_file()
        and path.stat().st_size == file.size
        and md5_file(path) == file.md5
    )


def fetch_file(
    file: ZenodoFile,
    *,
    ledger: Ledger,
    confinement: Confinement,
    fetch: Callable[[str], Iterable[bytes]] = urllib_fetch,
) -> Path:
    """Return the downloaded file, fetching it if it is not already present.

    A file already in the download directory with the published size and
    checksum is reused without a ledger entry. Otherwise the limit is checked
    with the published size before any byte is fetched, the body is streamed
    into a partial file, and the attempt is recorded in the ledger with the
    bytes received whether or not it succeeds. There is no retry.

    Args:
        file: The file to download.
        ledger: The run's download ledger.
        confinement: The run's write confinement.
        fetch: Streams the body of a URL.

    Returns:
        The file in the download directory, named after the key's last
        segment.

    Raises:
        DownloadLimitError: If the download would take the ledger total above
            :data:`LIMIT`. Nothing is fetched.
        DownloadError: If the fetch fails, the body exceeds the published
            size, or the size or checksum differs. No file is left behind.
    """
    target = confinement.check(confinement.downloads / file.name)
    if _is_published(target, file):
        return target
    if ledger.total + file.size > LIMIT:
        message = (
            f"downloading {file.key} ({file.size} bytes) would take the W1"
            f" cumulative total {ledger.total} above {LIMIT} bytes"
        )
        raise DownloadLimitError(message)
    partial = confinement.check(target.with_name(f"{target.name}.partial"))
    digest = hashlib.md5(usedforsecurity=False)
    received = 0
    ok = False
    chunks: Iterator[bytes] | None = None
    try:
        with partial.open("wb") as stream:
            chunks = iter(fetch(file.url))
            for chunk in chunks:
                received += len(chunk)
                digest.update(chunk)
                _check_received(file, received)
                stream.write(chunk)
        _check_complete(file, received, digest.hexdigest())
        partial.replace(target)
        ok = True
    finally:
        # Release the connection of a stream that was abandoned early.
        close = getattr(chunks, "close", None)
        if callable(close):
            close()
        if not ok:
            partial.unlink(missing_ok=True)
        entry = LedgerEntry(
            file.key,
            file.record,
            file.url,
            received,
            digest.hexdigest(),
            ok=ok,
            at=datetime.now(UTC).isoformat(timespec="seconds"),
        )
        ledger.record(entry, confinement=confinement)
    return target


def _check_received(file: ZenodoFile, received: int) -> None:
    if received > file.size:
        message = (
            f"{file.key}: received more than the published {file.size} bytes"
        )
        raise DownloadError(message)


def _check_complete(file: ZenodoFile, received: int, md5: str) -> None:
    if received != file.size:
        message = (
            f"{file.key}: received {received} bytes, not the published"
            f" {file.size}"
        )
        raise DownloadError(message)
    if md5 != file.md5:
        message = f"{file.key}: MD5 {md5} is not the published {file.md5}"
        raise DownloadError(message)
