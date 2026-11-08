"""Scenario tests for the harness's downloads under the W1 limit."""

from __future__ import annotations

import hashlib
import json
from datetime import datetime, timedelta
from typing import TYPE_CHECKING

import pytest

from sheet_music_omr.harness.confine import Confinement
from sheet_music_omr.harness.downloads import (
    LIEDER_ZIP,
    LIMIT,
    DownloadError,
    DownloadLimitError,
    Ledger,
    LedgerError,
    ZenodoFile,
    fetch_file,
)

if TYPE_CHECKING:
    from collections.abc import Iterator
    from pathlib import Path

PAYLOAD = b"synthetic corpus file\n" * 1000
FILE = ZenodoFile(
    "123",
    "corpus/data.bin",
    len(PAYLOAD),
    hashlib.md5(PAYLOAD, usedforsecurity=False).hexdigest(),
)
BASIS = "a stated allowance of the owner; an inference, not a measurement"


@pytest.fixture
def confinement(tmp_path: Path) -> Confinement:
    """Confine writes to a run and a download directory."""
    return Confinement.create(tmp_path / "run", tmp_path / "downloads")


@pytest.fixture
def ledger(confinement: Confinement) -> Ledger:
    """Start a ledger in the run directory."""
    return Ledger.create(
        confinement.run / "downloads.json",
        prior_total=1000,
        basis=BASIS,
        confinement=confinement,
    )


def serve(*chunks: bytes):
    """Return a fetcher that streams the chunks, and the URLs it fetched."""
    fetched: list[str] = []

    def fetch(url: str) -> Iterator[bytes]:
        fetched.append(url)
        yield from chunks

    return fetch, fetched


def refuse(url: str) -> Iterator[bytes]:
    """Fail the test: nothing may be fetched."""
    pytest.fail(f"nothing may be fetched, but {url} was")


def leftovers(confinement: Confinement) -> list[str]:
    """Return the names in the download directory."""
    return sorted(path.name for path in confinement.downloads.iterdir())


def test_download_streams_verifies_and_records(
    confinement: Confinement,
    ledger: Ledger,
) -> None:
    """Fetch the published bytes, keep the file and record the attempt."""
    fetch, fetched = serve(PAYLOAD[:100], PAYLOAD[100:])
    path = fetch_file(FILE, ledger=ledger, confinement=confinement, fetch=fetch)
    assert path == confinement.downloads / "data.bin"
    assert path.read_bytes() == PAYLOAD
    assert leftovers(confinement) == ["data.bin"]
    assert fetched == [
        "https://zenodo.org/records/123/files/corpus/data.bin?download=1"
    ]
    (entry,) = ledger.entries
    assert (entry.key, entry.record, entry.url) == (
        "corpus/data.bin",
        "123",
        fetched[0],
    )
    assert (entry.received, entry.md5, entry.ok) == (
        len(PAYLOAD),
        FILE.md5,
        True,
    )
    at = datetime.fromisoformat(entry.at)
    assert at.utcoffset() == timedelta(0)
    assert ledger.total == 1000 + len(PAYLOAD)
    reloaded = Ledger.load(ledger.path)
    assert reloaded.entries == ledger.entries
    assert (reloaded.prior_total, reloaded.basis) == (1000, BASIS)
    assert reloaded.total == ledger.total


def test_checksum_mismatch_fails_and_counts_the_bytes(
    confinement: Confinement,
    ledger: Ledger,
) -> None:
    """Delete a body of the right size but the wrong checksum."""
    corrupt = b"X" + PAYLOAD[1:]
    fetch, _ = serve(corrupt)
    with pytest.raises(DownloadError, match="MD5"):
        fetch_file(FILE, ledger=ledger, confinement=confinement, fetch=fetch)
    assert leftovers(confinement) == []
    (entry,) = Ledger.load(ledger.path).entries
    assert (entry.received, entry.ok) == (len(PAYLOAD), False)
    assert entry.md5 == hashlib.md5(corrupt, usedforsecurity=False).hexdigest()
    assert ledger.total == 1000 + len(PAYLOAD)


def test_oversize_stream_is_aborted(
    confinement: Confinement,
    ledger: Ledger,
) -> None:
    """Stop reading once the body exceeds the published size."""
    closed: list[bool] = []

    def endless(_url: str) -> Iterator[bytes]:
        try:
            while True:
                yield PAYLOAD
        finally:
            closed.append(True)

    with pytest.raises(DownloadError, match="more than the published"):
        fetch_file(FILE, ledger=ledger, confinement=confinement, fetch=endless)
    assert closed == [True]
    assert leftovers(confinement) == []
    (entry,) = Ledger.load(ledger.path).entries
    assert (entry.received, entry.ok) == (2 * len(PAYLOAD), False)


def test_short_stream_and_fetch_error_fail(
    confinement: Confinement,
    ledger: Ledger,
) -> None:
    """Record a truncated body and a body cut off by a fetch error."""
    short, _ = serve(PAYLOAD[:-1])
    with pytest.raises(DownloadError, match="not the published"):
        fetch_file(FILE, ledger=ledger, confinement=confinement, fetch=short)

    def broken(_url: str) -> Iterator[bytes]:
        yield PAYLOAD[:10]
        message = "connection reset"
        raise DownloadError(message)

    with pytest.raises(DownloadError, match="connection reset"):
        fetch_file(FILE, ledger=ledger, confinement=confinement, fetch=broken)
    assert leftovers(confinement) == []
    received = [(e.received, e.ok) for e in Ledger.load(ledger.path).entries]
    assert received == [(len(PAYLOAD) - 1, False), (10, False)]


def test_limit_is_checked_before_fetching(
    confinement: Confinement,
) -> None:
    """Refuse a download above the limit and accept one exactly at it."""
    over = Ledger.create(
        confinement.run / "over.json",
        prior_total=LIMIT - FILE.size + 1,
        basis=BASIS,
        confinement=confinement,
    )
    with pytest.raises(DownloadLimitError):
        fetch_file(FILE, ledger=over, confinement=confinement, fetch=refuse)
    assert Ledger.load(over.path).entries == ()
    assert leftovers(confinement) == []

    at_limit = Ledger.create(
        confinement.run / "at-limit.json",
        prior_total=LIMIT - FILE.size,
        basis=BASIS,
        confinement=confinement,
    )
    fetch, _ = serve(PAYLOAD)
    fetch_file(FILE, ledger=at_limit, confinement=confinement, fetch=fetch)
    assert Ledger.load(at_limit.path).total == LIMIT


def test_rerun_reuses_a_verified_file(
    confinement: Confinement,
    ledger: Ledger,
) -> None:
    """Reuse a published file with no entry, and replace a wrong one."""
    target = confinement.downloads / "data.bin"
    target.write_bytes(PAYLOAD)
    path = fetch_file(
        FILE, ledger=ledger, confinement=confinement, fetch=refuse
    )
    assert path == target
    assert Ledger.load(ledger.path).entries == ()

    target.write_bytes(b"X" + PAYLOAD[1:])
    fetch, _ = serve(PAYLOAD)
    fetch_file(FILE, ledger=ledger, confinement=confinement, fetch=fetch)
    assert target.read_bytes() == PAYLOAD
    assert [e.ok for e in Ledger.load(ledger.path).entries] == [True]


def test_ledger_stays_in_the_run_directory(
    confinement: Confinement,
    ledger: Ledger,
) -> None:
    """Refuse to overwrite a ledger or to put one in the downloads."""
    with pytest.raises(LedgerError, match="already exists"):
        Ledger.create(
            ledger.path,
            prior_total=0,
            basis=BASIS,
            confinement=confinement,
        )
    with pytest.raises(LedgerError, match="run directory"):
        Ledger.create(
            confinement.downloads / "ledger.json",
            prior_total=0,
            basis=BASIS,
            confinement=confinement,
        )
    data = json.loads(ledger.path.read_text(encoding="utf-8"))
    data["entries"] = [{"key": "x"}]
    ledger.path.write_text(json.dumps(data), encoding="utf-8")
    with pytest.raises(LedgerError, match="malformed entry"):
        Ledger.load(ledger.path)


def test_zenodo_url_quotes_each_key_segment() -> None:
    """Build the record file URL and the local name from the key."""
    assert LIEDER_ZIP.url == (
        "https://zenodo.org/records/15450144/files/OpenScore/"
        "Lieder-v3.0.0.zip?download=1"
    )
    assert LIEDER_ZIP.name == "Lieder-v3.0.0.zip"
    spaced = ZenodoFile("1", "a b/c#d.zip", 1, "0" * 32)
    assert spaced.url == (
        "https://zenodo.org/records/1/files/a%20b/c%23d.zip?download=1"
    )
    with pytest.raises(ValueError, match="unsafe"):
        ZenodoFile("1", "../escape.zip", 1, "0" * 32)
