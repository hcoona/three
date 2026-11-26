"""Ledgers, offline namespace execution and environment setup for W4 and W5.

Implements the finite execution bounds and two-phase isolation rules of
``docs/synthetic-protocol.md``:
- ``DOWNLOADS/w4-w5-download-ledger.json`` (``15,000,000,000``-byte cap),
  metering HTTP ``HEAD``/``GET`` metadata probes and reserving payload bytes
  atomically before transfer under an exclusive file lock.
- ``DOWNLOADS/w4-w5-run-ledger.jsonl`` (``25,000`` combined formal Stage S3
  and W5 pages, ``20`` formal ``69``-page ``TRAIN_DEV`` W5 starts, ``3`` starts
  toward ``1`` completed locked ``92``-page W5 report, and ``50 GB`` generated
  proof disk cap across ``RUNS/w4-w5-*``), routing any ceiling-exceedance
  stop back to the repository owner.
- Verified ``unshare -rn --pid --fork --kill-child=SIGKILL`` offline process
  execution with inner ``timeout -s KILL`` PID-1 deadline and ``proc.returncode
  >= 0`` reap confirmation before scratch deletion.
"""

from __future__ import annotations

import contextlib
import fcntl
import hashlib
import json
import os
import re
import shutil
import subprocess
import tempfile
import time
import urllib.error
import urllib.request
import zipfile
from dataclasses import dataclass
from datetime import UTC, datetime
from pathlib import Path
from typing import TYPE_CHECKING, Any

from sheet_music_omr.evaluation.alignment import (
    atomic_write_text,
    check_no_symlink_under,
    sha256_bytes,
    sha256_file,
    venv_python_path,
)
from sheet_music_omr.harness.privacy import scrub_paths
from sheet_music_omr.harness.records import (
    check_outside_repository,
    source_state,
)
from sheet_music_omr.harness.stops import StopConditionError

if TYPE_CHECKING:
    from collections.abc import Callable, Iterator, Mapping, Sequence

type JsonDict = dict[str, Any]

W4_W5_DOWNLOAD_CAP_BYTES = 15_000_000_000
W4_W5_DISK_CAP_BYTES = 50_000_000_000
W4_W5_MAX_PAGES_PER_RUN = 5_000
W4_W5_MAX_FORMAL_PAGES = 25_000
W4_W5_MAX_TRAINDEV_W5_STARTS = 20
W4_W5_MAX_LOCKED92_W5_STARTS = 3
WORKER_TIMEOUT_SECONDS = 300

DOWNLOAD_LEDGER_NAME = "w4-w5-download-ledger.json"
DOWNLOAD_LOCK_NAME = ".w4-w5-downloads.lock"
RUN_LEDGER_NAME = "w4-w5-run-ledger.jsonl"
RUN_LOCK_NAME = ".w4-w5-run-ledger.lock"

# Source-verified pins from docs/synthetic-protocol.md.
VEROVIO_VERSION = "6.4.1"
VEROVIO_WHEEL_URL = (
    "https://files.pythonhosted.org/packages/03/22/"
    "c98513dfe1d31681080ea742d8a9a8e7dc83e68d9528b35913298687824e/"
    "verovio-6.4.1-cp310-abi3-manylinux2014_x86_64.manylinux_2_17_x86_64.whl"
)
VEROVIO_WHEEL_BYTES = 8_614_166
VEROVIO_WHEEL_SHA256 = (
    "c19311ec28b113e179b547792b608b901509bbd065615d85bf38b2ad31146e12"
)

RESVG_VERSION = "0.2.0"
RESVG_WHEEL_URL = (
    "https://files.pythonhosted.org/packages/43/d8/"
    "25385f4919fa1858b39820fe7c34a6b0045b7b35d8f49fc32db6580e84ef/"
    "resvg-0.2.0-cp314-cp314-manylinux_2_17_x86_64.manylinux2014_x86_64.whl"
)
RESVG_WHEEL_BYTES = 1_753_778
RESVG_WHEEL_SHA256 = (
    "26f65c8096af11c4253e9d1c45db87bf2882a84d71deddbb7e3b01ee3d62a7e9"
)

HOMR_VERSION = "0.7.1"
HOMR_WHEEL_URL = (
    "https://files.pythonhosted.org/packages/66/43/"
    "bf958d1b16971cc7b67574cf797b3d2290712d873d632a78c7d6ae46e49a/"
    "homr-0.7.1-py3-none-any.whl"
)
HOMR_WHEEL_BYTES = 125_506
HOMR_WHEEL_SHA256 = (
    "163a36b3b87a17f47c6deb5edc394573cc42b09418d18c7e9195b1e6133afc25"
)

HOMR_CPU_CHECKPOINTS: tuple[tuple[str, str, int], ...] = (
    (
        "segnet",
        (
            "https://github.com/liebharc/homr/releases/download/onnx_checkpoints/"
            "segnet_308-3296ccd40960f90ca6ab9c035cca945675d30a0f.zip"
        ),
        53_214_519,
    ),
    (
        "encoder",
        (
            "https://github.com/liebharc/homr/releases/download/onnx_checkpoints/"
            "encoder_pytorch_model_509-2293bba14bfea47d9d28411339362af15fce4c31.zip"
        ),
        48_281_161,
    ),
    (
        "decoder",
        (
            "https://github.com/liebharc/homr/releases/download/onnx_checkpoints/"
            "decoder_pytorch_model_509-2293bba14bfea47d9d28411339362af15fce4c31.zip"
        ),
        37_824_782,
    ),
)

_METADATA_RESERVE_BYTES = 16 * 1024 * 1024
_TITLE_LINE_RE = re.compile(
    r"(?im)^.*(?:title|composer|work-title|movement-title)\s*[:=].*$"
)


def _owner_return_stop(reason: str) -> StopConditionError:
    return StopConditionError(
        f"{reason}; stop before exceeding bound and return to repository "
        "owner on #1491 / #1497 / #686 under the accepted Delivery Wave"
    )


@contextlib.contextmanager
def _exclusive_file_lock(lock_path: Path) -> Iterator[None]:
    lock_path.parent.mkdir(parents=True, exist_ok=True)
    if lock_path.is_symlink():
        msg = f"refusing symlink lock file {lock_path.name}"
        raise StopConditionError(msg)
    with lock_path.open("a+", encoding="utf-8") as fh:
        fcntl.flock(fh.fileno(), fcntl.LOCK_EX)
        try:
            yield
        finally:
            fcntl.flock(fh.fileno(), fcntl.LOCK_UN)


def _utc_now() -> str:
    return datetime.now(UTC).isoformat()


class W4W5DownloadLedger:
    """Metered HTTP ``HEAD``/``GET`` and payload ledger for W4 and W5."""

    def __init__(
        self,
        downloads_dir: Path,
        *,
        cap_bytes: int = W4_W5_DOWNLOAD_CAP_BYTES,
    ) -> None:
        """Bind ledger to ``downloads_dir`` with ``cap_bytes`` ceiling."""
        self.downloads_dir = downloads_dir
        self.cap_bytes = cap_bytes
        self.ledger_path = downloads_dir / DOWNLOAD_LEDGER_NAME
        self.lock_path = downloads_dir / DOWNLOAD_LOCK_NAME

    def _load_unlocked(self) -> JsonDict:
        if not self.ledger_path.exists():
            return {
                "schema": "w4-w5-download-ledger-v1",
                "cap_bytes": self.cap_bytes,
                "total_bytes": 0,
                "entries": [],
            }
        check_no_symlink_under(self.ledger_path, self.downloads_dir)
        data = json.loads(self.ledger_path.read_text(encoding="utf-8"))
        if (
            not isinstance(data, dict)
            or data.get("schema") != "w4-w5-download-ledger-v1"
            or not isinstance(data.get("entries"), list)
            or not isinstance(data.get("total_bytes"), int)
        ):
            msg = "malformed w4-w5-download-ledger.json"
            raise StopConditionError(msg)
        for entry in data["entries"]:
            if isinstance(entry, dict) and entry.get("status") == "reserved":
                msg = (
                    "unreconciled reserved transfer in "
                    "w4-w5-download-ledger.json"
                )
                raise StopConditionError(msg)
        return data

    def _save_unlocked(self, data: JsonDict) -> None:
        atomic_write_text(
            self.ledger_path,
            json.dumps(data, indent=2, sort_keys=True) + "\n",
        )

    def total_bytes(self) -> int:
        """Return current committed + failed/reserved byte total."""
        with _exclusive_file_lock(self.lock_path):
            return int(self._load_unlocked()["total_bytes"])

    def fetch_json_metadata(self, url: str) -> JsonDict:
        """Fetch JSON metadata (e.g., PyPI release JSON) with byte metering."""
        with _exclusive_file_lock(self.lock_path):
            state = self._load_unlocked()
            current = int(state["total_bytes"])
            if current + _METADATA_RESERVE_BYTES > self.cap_bytes:
                error = _owner_return_stop(
                    f"metadata probe {url} could exceed {self.cap_bytes}-byte "
                    "W4/W5 download cap"
                )
                raise error
            res_entry: JsonDict = {
                "utc_started": _utc_now(),
                "kind": "metadata_get",
                "url": url,
                "reserved_bytes": _METADATA_RESERVE_BYTES,
                "actual_bytes": _METADATA_RESERVE_BYTES,
                "status": "reserved",
            }
            state["entries"].append(res_entry)
            state["total_bytes"] = current + _METADATA_RESERVE_BYTES
            self._save_unlocked(state)

        actual = 0
        raw = b""
        status = "failed"
        try:
            req = urllib.request.Request(  # noqa: S310
                url,
                headers={"User-Agent": "sheet-music-omr-w4-w5/1"},
            )
            with urllib.request.urlopen(req, timeout=60.0) as resp:  # noqa: S310
                header_bytes = sum(
                    len(k) + len(v) + 4 for k, v in resp.headers.items()
                )
                actual = header_bytes
                if actual > _METADATA_RESERVE_BYTES:
                    msg = (
                        f"metadata headers from {url} exceeded "
                        f"{_METADATA_RESERVE_BYTES}-byte reservation"
                    )
                    raise StopConditionError(msg)
                chunks: list[bytes] = []
                body_len = 0
                while True:
                    max_read = min(
                        65536,
                        _METADATA_RESERVE_BYTES - header_bytes - body_len + 1,
                    )
                    chunk = resp.read(max(1, max_read))
                    if not chunk:
                        break
                    body_len += len(chunk)
                    actual = header_bytes + body_len
                    if actual > _METADATA_RESERVE_BYTES:
                        msg = (
                            f"metadata response from {url} exceeded "
                            f"{_METADATA_RESERVE_BYTES}-byte reservation"
                        )
                        raise StopConditionError(msg)
                    chunks.append(chunk)
                raw = b"".join(chunks)
                status = "completed"
        finally:
            with _exclusive_file_lock(self.lock_path):
                data = json.loads(self.ledger_path.read_text(encoding="utf-8"))
                # _load_unlocked allows at most one active reserved entry.
                last = data["entries"][-1]
                settled = (
                    actual
                    if status == "completed"
                    else max(actual, _METADATA_RESERVE_BYTES)
                )
                last["actual_bytes"] = settled
                last["utc_settled"] = _utc_now()
                last["status"] = status
                if status == "completed":
                    last["sha256"] = sha256_bytes(raw)
                data["total_bytes"] = sum(
                    int(e["actual_bytes"]) for e in data["entries"]
                )
                self._save_unlocked(data)
        return json.loads(raw.decode("utf-8"))

    def ensure_cached_file(  # noqa: C901, PLR0912, PLR0915
        self,
        *,
        url: str,
        dest: Path,
        expected_bytes: int,
        expected_sha256: str | None = None,
    ) -> str:
        """Verify cached ``dest`` at ``0`` bytes or download with metering."""
        if expected_bytes <= 0:
            msg = f"expected_bytes must be positive for {url}"
            raise StopConditionError(msg)

        overhead = 16 * 1024
        reserve = expected_bytes + overhead
        with _exclusive_file_lock(self.lock_path):
            state = self._load_unlocked()
            check_no_symlink_under(dest, self.downloads_dir)
            if dest.is_file():
                digest = sha256_file(dest)
                if dest.stat().st_size == expected_bytes and (
                    expected_sha256 is None or digest == expected_sha256
                ):
                    return digest
                msg = (
                    f"cached file {dest.name} failed size/SHA-256 verification"
                )
                raise StopConditionError(msg)
            current = int(state["total_bytes"])
            if current + reserve > self.cap_bytes:
                error = _owner_return_stop(
                    f"fetching {dest.name} ({reserve} bytes) would exceed "
                    f"{self.cap_bytes}-byte W4/W5 download cap"
                )
                raise error
            state["entries"].append(
                {
                    "utc_started": _utc_now(),
                    "kind": "payload_get",
                    "url": url,
                    "dest_name": dest.name,
                    "reserved_bytes": reserve,
                    "actual_bytes": reserve,
                    "status": "reserved",
                }
            )
            state["total_bytes"] = current + reserve
            self._save_unlocked(state)

        dest.parent.mkdir(parents=True, exist_ok=True)
        fd, tmp_s = tempfile.mkstemp(prefix=f".{dest.name}.", dir=dest.parent)
        tmp_p = Path(tmp_s)
        actual = 0
        digest = ""
        status = "failed"
        try:
            hasher = hashlib.sha256()
            req = urllib.request.Request(  # noqa: S310
                url,
                headers={"User-Agent": "sheet-music-omr-w4-w5/1"},
            )
            with (
                os.fdopen(fd, "wb") as out_fh,
                urllib.request.urlopen(req, timeout=120.0) as resp,  # noqa: S310
            ):
                hdr_bytes = sum(
                    len(k) + len(v) + 4 for k, v in resp.headers.items()
                )
                actual = hdr_bytes
                if actual > reserve:
                    msg = f"download headers for {dest.name} exceeded budget"
                    raise StopConditionError(msg)  # noqa: TRY301
                body_bytes = 0
                while True:
                    max_read = min(
                        1024 * 1024,
                        max(
                            1,
                            min(expected_bytes - body_bytes, reserve - actual)
                            + 1,
                        ),
                    )
                    chunk = resp.read(max_read)
                    if not chunk:
                        break
                    body_bytes += len(chunk)
                    actual = hdr_bytes + body_bytes
                    if body_bytes > expected_bytes or actual > reserve:
                        msg = (
                            f"download {dest.name} exceeded published size "
                            f"{expected_bytes}"
                        )
                        raise StopConditionError(msg)  # noqa: TRY301
                    out_fh.write(chunk)
                    hasher.update(chunk)
            if body_bytes != expected_bytes:
                msg = (
                    f"download {dest.name} size {body_bytes} != "
                    f"{expected_bytes}"
                )
                raise StopConditionError(msg)  # noqa: TRY301
            digest = hasher.hexdigest()
            if expected_sha256 is not None and digest != expected_sha256:
                msg = (
                    f"download {dest.name} SHA-256 {digest} != "
                    f"{expected_sha256}"
                )
                raise StopConditionError(msg)  # noqa: TRY301
            tmp_p.replace(dest)
        except BaseException:
            tmp_p.unlink(missing_ok=True)
            raise
        else:
            status = "completed"
            return digest
        finally:
            with _exclusive_file_lock(self.lock_path):
                data = json.loads(self.ledger_path.read_text(encoding="utf-8"))
                # _load_unlocked allows at most one active reserved entry.
                last = data["entries"][-1]
                settled = (
                    actual if status == "completed" else max(actual, reserve)
                )
                last["actual_bytes"] = settled
                last["utc_settled"] = _utc_now()
                last["status"] = status
                if digest:
                    last["sha256"] = digest
                data["total_bytes"] = sum(
                    int(e["actual_bytes"]) for e in data["entries"]
                )
                self._save_unlocked(data)


def directory_bytes(root: Path) -> int:
    """Return total regular-file bytes under ``root``."""
    if not root.exists():
        return 0
    total = 0
    for dirpath, _, filenames in os.walk(root, followlinks=False):
        dp = Path(dirpath)
        for fn in filenames:
            fp = dp / fn
            if not fp.is_symlink() and fp.is_file():
                total += fp.stat().st_size
    return total


DISK_LOCK_NAME = ".w4-w5-disk.lock"
WORKER_SCRATCH_HEADROOM_BYTES = 64 * 1024 * 1024


@contextlib.contextmanager
def proof_disk_lock(runs_root: Path) -> Iterator[None]:
    """Serialize cross-run disk headroom checks and proof output writes."""
    runs_root.mkdir(parents=True, exist_ok=True)
    with _exclusive_file_lock(runs_root / DISK_LOCK_NAME):
        yield


def _metered_proof_bytes_unlocked(
    runs_root: Path, extra_dir: Path | None = None
) -> int:
    used = 0
    if runs_root.exists():
        for child in runs_root.iterdir():
            if child.name.startswith("w4-w5-") and child.is_dir():
                used += directory_bytes(child)
    if (
        extra_dir is not None
        and extra_dir.exists()
        and not extra_dir.name.startswith("w4-w5-")
    ):
        used += directory_bytes(extra_dir)
    return used


def check_metered_proof_disk_cap_unlocked(
    runs_root: Path,
    *,
    cap_bytes: int = W4_W5_DISK_CAP_BYTES,
    additional_bytes: int = 0,
    extra_dir: Path | None = None,
) -> int:
    """Check ``w4-w5-*`` proof disk usage assuming ``proof_disk_lock`` held."""
    used = _metered_proof_bytes_unlocked(runs_root, extra_dir=extra_dir)
    if used >= cap_bytes or used + additional_bytes > cap_bytes:
        error = _owner_return_stop(
            f"generated W4/W5 proof outputs ({used} + {additional_bytes} "
            f"bytes) reached {cap_bytes}-byte disk cap"
        )
        raise error
    return used


def check_metered_proof_disk_cap(
    runs_root: Path,
    *,
    cap_bytes: int = W4_W5_DISK_CAP_BYTES,
    additional_bytes: int = 0,
    extra_dir: Path | None = None,
) -> int:
    """Sum ``w4-w5-*`` proof directories under ``runs_root`` with headroom."""
    if not runs_root.exists() and (extra_dir is None or not extra_dir.exists()):
        return check_metered_proof_disk_cap_unlocked(
            runs_root,
            cap_bytes=cap_bytes,
            additional_bytes=additional_bytes,
            extra_dir=extra_dir,
        )
    with proof_disk_lock(runs_root):
        return check_metered_proof_disk_cap_unlocked(
            runs_root,
            cap_bytes=cap_bytes,
            additional_bytes=additional_bytes,
            extra_dir=extra_dir,
        )


@dataclass(frozen=True, slots=True)
class FormalRunReservation:
    """Active reservation in ``w4-w5-run-ledger.jsonl``."""

    run_id: str
    command: str
    reserved_pages: int


class W4W5RunLedger:
    """Persistent append-only formal Stage S3 and W5 run ledger."""

    def __init__(
        self,
        downloads_dir: Path,
        *,
        max_pages_per_run: int = W4_W5_MAX_PAGES_PER_RUN,
        max_formal_pages: int = W4_W5_MAX_FORMAL_PAGES,
        max_traindev_starts: int = W4_W5_MAX_TRAINDEV_W5_STARTS,
        max_locked92_starts: int = W4_W5_MAX_LOCKED92_W5_STARTS,
    ) -> None:
        """Initialize persistent run ledger under ``downloads_dir``."""
        self.downloads_dir = downloads_dir
        self.ledger_path = downloads_dir / RUN_LEDGER_NAME
        self.lock_path = downloads_dir / RUN_LOCK_NAME
        self.max_pages_per_run = max_pages_per_run
        self.max_formal_pages = max_formal_pages
        self.max_traindev_starts = max_traindev_starts
        self.max_locked92_starts = max_locked92_starts

    def _read_records_unlocked(self) -> list[JsonDict]:
        if not self.ledger_path.exists():
            return []
        check_no_symlink_under(self.ledger_path, self.downloads_dir)
        out: list[JsonDict] = []
        for ln in self.ledger_path.read_text(encoding="utf-8").splitlines():
            if ln.strip():
                rec = json.loads(ln)
                if not isinstance(rec, dict):
                    msg = "malformed record in w4-w5-run-ledger.jsonl"
                    raise StopConditionError(msg)
                out.append(rec)
        return out

    def _append_unlocked(self, rec: JsonDict) -> None:
        self.ledger_path.parent.mkdir(parents=True, exist_ok=True)
        with self.ledger_path.open("a", encoding="utf-8") as fh:
            fh.write(json.dumps(rec, sort_keys=True) + "\n")
            fh.flush()
            os.fsync(fh.fileno())

    def reserve(  # noqa: C901, PLR0912, PLR0915
        self,
        *,
        run_id: str,
        command: str,
        reserved_pages: int,
        git_commit: str,
        clean_tree: bool,
    ) -> FormalRunReservation:
        """Atomically reserve ``reserved_pages`` and start slot before work."""
        if not clean_tree:
            msg = "formal W4/W5 run requires a clean project code tree"
            raise StopConditionError(msg)
        if reserved_pages <= 0 or reserved_pages > self.max_pages_per_run:
            error = _owner_return_stop(
                f"planned pages ({reserved_pages}) out of 1.."
                f"{self.max_pages_per_run} per-run bound"
            )
            raise error
        if command not in {"s3-proof", "w5-traindev-69", "w5-locked-92"}:
            msg = f"unknown formal W4/W5 ledger command: {command}"
            raise StopConditionError(msg)

        with _exclusive_file_lock(self.lock_path):
            records = self._read_records_unlocked()
            starts: dict[str, JsonDict] = {}
            settlements: dict[str, JsonDict] = {}
            for rec in records:
                rid = str(rec.get("run_id", ""))
                ev = rec.get("event")
                if ev == "start":
                    starts[rid] = rec
                elif ev == "settle":
                    settlements[rid] = rec

            unsettled = [rid for rid in starts if rid not in settlements]
            if unsettled:
                msg = (
                    "unreconciled crashed formal run in "
                    f"w4-w5-run-ledger.jsonl: {unsettled[0]}"
                )
                raise StopConditionError(msg)
            if run_id in starts:
                msg = f"duplicate formal run_id in ledger: {run_id}"
                raise StopConditionError(msg)

            cum_pages = 0
            traindev_starts = 0
            locked92_starts = 0
            locked92_completed = False
            for rid, s_rec in starts.items():
                cmd = str(s_rec.get("command", ""))
                res_p = int(s_rec.get("reserved_pages", 0))
                stl = settlements.get(rid)
                charged = (
                    int(stl.get("actual_pages", res_p))
                    if stl is not None
                    else res_p
                )
                cum_pages += max(0, charged)
                if cmd == "w5-traindev-69":
                    traindev_starts += 1
                elif cmd == "w5-locked-92":
                    locked92_starts += 1
                    if stl is not None and stl.get("status") == "completed":
                        locked92_completed = True

            if cum_pages + reserved_pages > self.max_formal_pages:
                error = _owner_return_stop(
                    f"cumulative formal pages ({cum_pages} + {reserved_pages})"
                    f" would exceed {self.max_formal_pages}"
                )
                raise error
            if (
                command == "w5-traindev-69"
                and traindev_starts + 1 > self.max_traindev_starts
            ):
                error = _owner_return_stop(
                    f"formal W5 TRAIN_DEV starts would exceed "
                    f"{self.max_traindev_starts}"
                )
                raise error
            if command == "w5-locked-92":
                if locked92_completed:
                    error = _owner_return_stop(
                        "locked 92-page W5 geometry pass already completed "
                        "once; no further 92-page start is permitted"
                    )
                    raise error
                if locked92_starts + 1 > self.max_locked92_starts:
                    error = _owner_return_stop(
                        "locked 92-page W5 starts reached 3-attempt circuit "
                        "breaker"
                    )
                    raise error

            self._append_unlocked(
                {
                    "event": "start",
                    "utc": _utc_now(),
                    "run_id": run_id,
                    "command": command,
                    "git_commit": git_commit,
                    "clean_tree": clean_tree,
                    "reserved_pages": reserved_pages,
                    "status": "started",
                }
            )
            return FormalRunReservation(
                run_id=run_id, command=command, reserved_pages=reserved_pages
            )

    def settle(
        self,
        reservation: FormalRunReservation,
        *,
        actual_pages: int,
        status: str,
    ) -> None:
        """Append settlement record (`completed` or `failed`) for ``run_id``."""
        if status not in {"completed", "failed"}:
            msg = f"invalid settlement status: {status}"
            raise ValueError(msg)
        with _exclusive_file_lock(self.lock_path):
            self._append_unlocked(
                {
                    "event": "settle",
                    "utc": _utc_now(),
                    "run_id": reservation.run_id,
                    "command": reservation.command,
                    "actual_pages": max(0, actual_pages),
                    "status": status,
                }
            )


def sanitize_subprocess_output(
    text: str, forbidden_paths: Sequence[Path] = ()
) -> str:
    """Scrub filesystem paths and score title lines per OMR-REQ-015."""
    cleaned = scrub_paths(_TITLE_LINE_RE.sub("[title-scrubbed]", text))
    paths = {str(path) for path in forbidden_paths}
    paths.update(path.replace("/", "\\") for path in tuple(paths))
    for path in sorted(paths, key=len, reverse=True):
        pattern = re.escape(path) + r"(?:[/\\][^\r\n\"'`]*)?"
        cleaned = re.sub(pattern, "<path>", cleaned)
    return cleaned[-4000:]


def verify_loopback_only_unshare() -> None:
    """Verify ``unshare -rn --pid --fork --kill-child=SIGKILL`` isolates net."""
    probe_py = (
        "from pathlib import Path; "
        "rows = Path('/proc/net/dev').read_text().splitlines()[2:]; "
        "names = [row.split(':', 1)[0].strip() for row in rows]; "
        "raise SystemExit(0 if names == ['lo'] else 3)"
    )
    cmd = [
        "unshare",
        "-rn",
        "--pid",
        "--fork",
        "--kill-child=SIGKILL",
        "--",
        "timeout",
        "-s",
        "KILL",
        "10s",
        "python3",
        "-c",
        probe_py,
    ]
    try:
        proc = subprocess.run(  # noqa: S603
            cmd,
            capture_output=True,
            text=True,
            timeout=15.0,
            check=False,
            close_fds=True,
        )
    except (OSError, subprocess.SubprocessError) as exc:
        msg = f"process-tree network isolation probe failed: {exc}"
        raise StopConditionError(msg) from exc
    if proc.returncode != 0:
        msg = (
            "unshare -rn loopback-only network isolation probe failed "
            f"(exit {proc.returncode})"
        )
        raise StopConditionError(msg)


@dataclass(frozen=True, slots=True)
class OfflineSubprocessResult:
    """Outcome of a single offline isolated subprocess invocation."""

    returncode: int
    stdout_sanitized: str
    stderr_sanitized: str


def _finalize_offline_scratch(
    scratch_dir: Path,
    proc: subprocess.Popen[str] | None,
    *,
    spawn_started: bool,
    reaped_cleanly: bool,
    deadline_at: float,
) -> None:
    if proc is not None and not reaped_cleanly and proc.poll() is None:
        remaining = deadline_at - time.monotonic()
        if remaining > 0:
            with contextlib.suppress(subprocess.TimeoutExpired):
                proc.wait(timeout=remaining)
    if reaped_cleanly or not spawn_started:
        try:
            shutil.rmtree(scratch_dir)
        except OSError as exc:
            msg = f"failed to remove scratch directory: {exc}"
            raise StopConditionError(msg) from exc


def run_offline_isolated_subprocess(  # noqa: PLR0913
    cmd: Sequence[str],
    *,
    scratch_root: Path,
    input_page_png: Path | None = None,
    scratch_page_name: str = "page.png",
    deadline_seconds: int = WORKER_TIMEOUT_SECONDS,
    extra_env: Mapping[str, str] | None = None,
    forbidden_paths: Sequence[Path] = (),
    on_before_cleanup: Callable[[Path], None] | None = None,
) -> OfflineSubprocessResult:
    """Run ``cmd`` inside ``unshare -rn --pid --fork --kill-child=SIGKILL``.

    ``timeout -s KILL`` runs as PID ``1`` inside the namespace while outer
    ``unshare`` stays alive to reap PID ``1``. Deletes the ephemeral scratch
    directory in ``finally`` **only** after ``unshare`` exits with
    ``proc.returncode >= 0``; retains scratch untouched and raises
    :class:`StopConditionError` if ``unshare`` fails to exit within ``5.0 s``
    past ``deadline_seconds`` or was signal-killed (``returncode < 0``).
    """
    scratch_root.mkdir(parents=True, exist_ok=True)
    scratch_dir = Path(tempfile.mkdtemp(prefix="w4w5-iso-", dir=scratch_root))
    staged_png: Path | None = None
    proc: subprocess.Popen[str] | None = None
    spawn_started = False
    reaped_cleanly = False
    deadline_at = time.monotonic() + float(deadline_seconds) + 5.0
    out_s = ""
    err_s = ""
    try:
        if input_page_png is not None:
            staged_png = scratch_dir / scratch_page_name
            shutil.copyfile(input_page_png, staged_png)
        resolved_cmd = [
            str(staged_png) if tok == "{SCRATCH_PNG}" and staged_png else tok
            for tok in cmd
        ]
        wrapped = [
            "unshare",
            "-rn",
            "--pid",
            "--fork",
            "--kill-child=SIGKILL",
            "--",
            "timeout",
            "-s",
            "KILL",
            f"{deadline_seconds}s",
            *resolved_cmd,
        ]
        env = {
            "PATH": os.environ.get("PATH", "/usr/bin:/bin"),
            "HOME": str(scratch_dir),
            "TMPDIR": str(scratch_dir),
            "LANG": "C.UTF-8",
            "OMP_NUM_THREADS": "1",
            "HF_HUB_OFFLINE": "1",
            "TRANSFORMERS_OFFLINE": "1",
            "PIP_NO_INDEX": "1",
            "UV_OFFLINE": "1",
            **(extra_env or {}),
        }
        spawn_started = True
        deadline_at = time.monotonic() + float(deadline_seconds) + 5.0
        try:
            proc = subprocess.Popen(  # noqa: S603
                wrapped,
                cwd=scratch_dir,
                env=env,
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
                encoding="utf-8",
                errors="replace",
                close_fds=True,
                start_new_session=True,
            )
        except OSError:
            spawn_started = False
            raise
        comm_budget = deadline_at - time.monotonic()
        if comm_budget <= 0:
            msg = (
                "outer unshare wrapper budget expired before communicate; "
                "retaining scratch fail-closed"
            )
            raise StopConditionError(msg)
        try:
            out_s, err_s = proc.communicate(timeout=comm_budget)
        except subprocess.TimeoutExpired as exc:
            msg = (
                "outer unshare wrapper did not exit within 5.0s past inner "
                "PID-1 timeout; retaining scratch fail-closed"
            )
            raise StopConditionError(msg) from exc
        if proc.returncode is None or proc.returncode < 0:
            msg = (
                f"unshare wrapper exited abnormally ({proc.returncode}); "
                "retaining scratch fail-closed"
            )
            raise StopConditionError(msg)
        reaped_cleanly = True
        if on_before_cleanup is not None:
            on_before_cleanup(scratch_dir)
    finally:
        _finalize_offline_scratch(
            scratch_dir,
            proc,
            spawn_started=spawn_started,
            reaped_cleanly=reaped_cleanly,
            deadline_at=deadline_at,
        )

    scrub_targets = [*forbidden_paths, scratch_dir]
    if input_page_png is not None:
        scrub_targets.append(input_page_png)
    return OfflineSubprocessResult(
        returncode=int(proc.returncode),
        stdout_sanitized=sanitize_subprocess_output(out_s, scrub_targets),
        stderr_sanitized=sanitize_subprocess_output(err_s, scrub_targets),
    )


def _select_linux_wheel(
    pypi_json: JsonDict, pkg: str, version: str | None = None
) -> tuple[str, str, int, str]:
    """Select best Linux ``x86_64`` / pure-Python wheel for CPython 3.14."""
    ver = version or str(pypi_json["info"]["version"])
    releases = pypi_json.get("releases", {})
    files = (
        releases.get(ver)
        if isinstance(releases, dict) and ver in releases
        else pypi_json.get("urls", [])
    )
    if not isinstance(files, list) or not files:
        msg = f"no PyPI release files found for {pkg}=={ver}"
        raise StopConditionError(msg)

    candidates: list[tuple[int, str, str, int, str]] = []
    for item in files:
        if item.get("packagetype") != "bdist_wheel" or item.get("yanked"):
            continue
        fn = str(item["filename"])
        lower = fn.lower()
        rank = -1
        if (
            "cp314-cp314" in lower
            and "manylinux" in lower
            and "x86_64" in lower
        ):
            rank = 4
        elif "abi3" in lower and "manylinux" in lower and "x86_64" in lower:
            rank = 3
        elif "py3-none-manylinux" in lower and "x86_64" in lower:
            rank = 2
        elif lower.endswith(("-py3-none-any.whl", "-py2.py3-none-any.whl")):
            rank = 1
        if rank > 0:
            sha = str(item["digests"]["sha256"])
            candidates.append(
                (rank, ver, str(item["url"]), int(item["size"]), sha)
            )
    if not candidates:
        msg = f"no compatible Linux x86_64 CPython 3.14 wheel for {pkg}=={ver}"
        raise StopConditionError(msg)
    candidates.sort(key=lambda t: t[0], reverse=True)
    _, best_ver, url, size, sha = candidates[0]
    return best_ver, url, size, sha


def setup_w4_w5_environments(  # noqa: C901, PLR0912, PLR0915
    *,
    downloads_dir: Path,
    repo_root: Path,
    include_homr: bool = True,
) -> JsonDict:
    """Phase 1 (`setup-w4-w5`): metered zero-private-input tool provisioning."""
    check_outside_repository(downloads_dir)
    if downloads_dir.resolve().is_relative_to(repo_root.resolve()):
        msg = "the download directory must be outside the repository"
        raise StopConditionError(msg)
    ledger = W4W5DownloadLedger(downloads_dir)
    wheelhouse = downloads_dir / "w4-w5-wheelhouse"
    wheelhouse.mkdir(parents=True, exist_ok=True)
    copista_wheels = downloads_dir / "w2-copista-cache" / "wheelhouse"

    # 1. Provision the isolated renderer with cached imaging dependencies.
    vrv_whl = wheelhouse / Path(VEROVIO_WHEEL_URL).name
    ledger.ensure_cached_file(
        url=VEROVIO_WHEEL_URL,
        dest=vrv_whl,
        expected_bytes=VEROVIO_WHEEL_BYTES,
        expected_sha256=VEROVIO_WHEEL_SHA256,
    )
    resvg_whl = wheelhouse / Path(RESVG_WHEEL_URL).name
    ledger.ensure_cached_file(
        url=RESVG_WHEEL_URL,
        dest=resvg_whl,
        expected_bytes=RESVG_WHEEL_BYTES,
        expected_sha256=RESVG_WHEEL_SHA256,
    )

    synth_wheels: list[Path] = [vrv_whl, resvg_whl]
    for pattern in (
        "numpy-*.whl",
        "pillow-*.whl",
        "scipy-*.whl",
        "opencv_*.whl",
        "pypdfium2-*.whl",
    ):
        found = (
            sorted(copista_wheels.glob(pattern))
            if copista_wheels.is_dir()
            else []
        )
        if found:
            synth_wheels.append(found[0])
        else:
            pkg_name = pattern.split("-")[0].replace("_", "-")
            meta = ledger.fetch_json_metadata(
                f"https://pypi.org/pypi/{pkg_name}/json"
            )
            _, url, sz, sha = _select_linux_wheel(meta, pkg_name)
            dest = wheelhouse / Path(url).name
            ledger.ensure_cached_file(
                url=url, dest=dest, expected_bytes=sz, expected_sha256=sha
            )
            synth_wheels.append(dest)

    synth_venv = downloads_dir / "w4-synth-venv"
    if not venv_python_path(synth_venv).is_file():
        subprocess.run(  # noqa: S603
            [  # noqa: S607
                "uv",
                "venv",
                "--offline",
                "--no-python-downloads",
                "--python",
                "3.14",
                str(synth_venv),
            ],
            check=True,
            timeout=120.0,
        )
    subprocess.run(  # noqa: S603
        [  # noqa: S607
            "uv",
            "pip",
            "install",
            "--python",
            str(venv_python_path(synth_venv)),
            "--offline",
            "--no-deps",
            *(str(w) for w in synth_wheels),
        ],
        check=True,
        timeout=180.0,
    )

    homr_info: JsonDict = {"enabled": False}
    if include_homr:
        homr_whl = wheelhouse / Path(HOMR_WHEEL_URL).name
        homr_whl_sha = ledger.ensure_cached_file(
            url=HOMR_WHEEL_URL,
            dest=homr_whl,
            expected_bytes=HOMR_WHEEL_BYTES,
            expected_sha256=HOMR_WHEEL_SHA256,
        )
        homr_pkgs: tuple[tuple[str, str | None], ...] = (
            ("onnxruntime", "1.24.1"),
            ("Pillow", "12.1.1"),
            ("types-Pillow", None),
            ("opencv-python-headless", "4.13.0.92"),
            ("rapidocr", "3.10.0"),
            ("pyclipper", None),
            ("shapely", None),
            ("pyyaml", None),
            ("omegaconf", None),
            ("antlr4-python3-runtime", None),
            ("tqdm", None),
            ("colorlog", None),
            ("six", None),
            ("requests", None),
            ("urllib3", None),
            ("idna", None),
            ("charset-normalizer", None),
            ("certifi", None),
            ("flatbuffers", None),
            ("protobuf", None),
            ("coloredlogs", None),
            ("humanfriendly", None),
        )
        homr_wheels: list[Path] = [homr_whl]
        for pattern in (
            "numpy-*.whl",
            "typing_extensions-*.whl",
            "packaging-*.whl",
            "sympy-*.whl",
            "mpmath-*.whl",
            "pypdfium2-*.whl",
        ):
            found = (
                sorted(copista_wheels.glob(pattern))
                if copista_wheels.is_dir()
                else []
            )
            if found:
                homr_wheels.append(found[0])

        for pkg_name, pinned_ver in homr_pkgs:
            url_q = (
                f"https://pypi.org/pypi/{pkg_name}/{pinned_ver}/json"
                if pinned_ver
                else f"https://pypi.org/pypi/{pkg_name}/json"
            )
            meta = ledger.fetch_json_metadata(url_q)
            _, w_url, w_sz, w_sha = _select_linux_wheel(
                meta, pkg_name, pinned_ver
            )
            dest = wheelhouse / Path(w_url).name
            ledger.ensure_cached_file(
                url=w_url, dest=dest, expected_bytes=w_sz, expected_sha256=w_sha
            )
            homr_wheels.append(dest)

        homr_venv = downloads_dir / "w5-homr-venv"
        if not venv_python_path(homr_venv).is_file():
            subprocess.run(  # noqa: S603
                [  # noqa: S607
                    "uv",
                    "venv",
                    "--offline",
                    "--no-python-downloads",
                    "--python",
                    "3.14",
                    str(homr_venv),
                ],
                check=True,
                timeout=120.0,
            )
        subprocess.run(  # noqa: S603
            [  # noqa: S607
                "uv",
                "pip",
                "install",
                "--python",
                str(venv_python_path(homr_venv)),
                "--offline",
                "--no-deps",
                *(str(w) for w in homr_wheels),
            ],
            check=True,
            timeout=180.0,
        )

        # Locate installed homr package root and unpack the 3 CPU checkpoints.
        homr_pkg_dir = Path(
            subprocess.check_output(  # noqa: S603
                [
                    str(venv_python_path(homr_venv)),
                    "-c",
                    (
                        "from importlib.metadata import distribution; "
                        "print(distribution('homr').locate_file('homr'))"
                    ),
                ],
                text=True,
                timeout=30.0,
            ).strip()
        )
        ckpt_cache = downloads_dir / "w5-homr-checkpoints"
        ckpt_cache.mkdir(parents=True, exist_ok=True)
        ckpt_digests: dict[str, str] = {}
        onnx_digests: dict[str, str] = {}
        for role, c_url, c_bytes in HOMR_CPU_CHECKPOINTS:
            z_dest = ckpt_cache / Path(c_url).name
            z_sha = ledger.ensure_cached_file(
                url=c_url, dest=z_dest, expected_bytes=c_bytes
            )
            ckpt_digests[z_dest.name] = z_sha
            target_sub = (
                homr_pkg_dir / "segmentation"
                if role == "segnet"
                else homr_pkg_dir / "transformer"
            )
            target_sub.mkdir(parents=True, exist_ok=True)
            marker = target_sub / f"{z_dest.stem}.onnx"
            if not marker.exists():
                with zipfile.ZipFile(z_dest) as zf:
                    zf.extractall(target_sub)
            if not marker.is_file():
                msg = f"HOMR checkpoint {marker.name} missing after unpack"
                raise StopConditionError(msg)
            onnx_digests[marker.name] = sha256_file(marker)
        homr_info = {
            "enabled": True,
            "homr_version": HOMR_VERSION,
            "homr_wheel_sha256": homr_whl_sha,
            "checkpoints": ckpt_digests,
            "onnx_models": onnx_digests,
        }

    # Verify offline rasterization and both MEI pedal forms.
    verify_loopback_only_unshare()
    calib_py = """\
import xml.etree.ElementTree as ET
import resvg
import verovio

MIXED_XML = '''<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE score-partwise PUBLIC "-//Recordare//DTD MusicXML 4.0 Partwise//EN"
  "http://www.musicxml.org/dtds/partwise.dtd">
<score-partwise version="4.0">
  <part-list><score-part id="P1">
    <part-name>Piano</part-name>
  </score-part></part-list>
  <part id="P1">
    <measure number="1">
      <attributes>
        <divisions>1</divisions>
        <key><fifths>0</fifths></key>
        <time><beats>4</beats><beat-type>4</beat-type></time>
        <staves>2</staves>
        <clef number="1"><sign>G</sign><line>2</line></clef>
        <clef number="2"><sign>F</sign><line>4</line></clef>
      </attributes>
      <direction placement="below"><direction-type>
        <pedal type="start" line="yes"/></direction-type><staff>2</staff>
      </direction>
      <note><pitch><step>C</step><octave>4</octave></pitch><duration>4</duration>
        <voice>1</voice><type>whole</type><staff>1</staff></note>
      <backup><duration>4</duration></backup>
      <note><pitch><step>C</step><octave>3</octave></pitch><duration>4</duration>
        <voice>5</voice><type>whole</type><staff>2</staff></note>
      <direction placement="below"><direction-type>
        <pedal type="stop" line="yes"/></direction-type><staff>2</staff>
      </direction>
    </measure>
    <measure number="2">
      <print new-system="yes"/>
      <direction placement="below"><direction-type>
        <pedal type="start" line="no" sign="yes"/></direction-type>
        <staff>2</staff>
      </direction>
      <note><pitch><step>G</step><octave>4</octave></pitch><duration>4</duration>
        <voice>1</voice><type>whole</type><staff>1</staff></note>
      <backup><duration>4</duration></backup>
      <note><pitch><step>G</step><octave>2</octave></pitch><duration>4</duration>
        <voice>5</voice><type>whole</type><staff>2</staff></note>
      <direction placement="below"><direction-type>
        <pedal type="stop" line="no" sign="yes"/></direction-type>
        <staff>2</staff>
      </direction>
    </measure>
  </part>
</score-partwise>'''

tk = verovio.toolkit()
tk.setOptions({
    "svgBoundingBoxes": True, "svgViewBox": True,
    "pedalStyle": "auto", "breaks": "encoded",
})
assert tk.loadData(MIXED_XML)
assert tk.getPageCount() >= 1
svg = tk.renderToSVG(1)
opts = resvg.usvg.Options.default()
opts.load_system_fonts()
tree = resvg.usvg.Tree.from_str(svg, opts)
png_bytes = bytes(
    resvg.render(
        tree,
        (1.0, 0.0, 0.0, 0.0, 1.0, 0.0),
        bg_color=(255, 255, 255, 255),
    )
)
assert png_bytes[:8] == b"\\x89PNG\\r\\n\\x1a\\n"
mei = tk.getMEI()
root = ET.fromstring(mei)
ns = {"m": "http://www.music-encoding.org/ns/mei"}
pedals = root.findall(".//m:pedal", ns)
forms = [p.get("form") for p in pedals]
assert "line" in forms, f"expected line pedal in {forms}"
assert any(f in (None, "pedstar", "altpedstar") for f in forms), forms
"""
    scratch_check = downloads_dir / ".w4-w5-scratch"
    res = run_offline_isolated_subprocess(
        [str(venv_python_path(synth_venv)), "-c", calib_py],
        scratch_root=scratch_check,
        deadline_seconds=60,
    )
    if res.returncode != 0:
        msg = (
            "Verovio + resvg mixed-pedal offline calibration failed: "
            f"{res.stderr_sanitized}"
        )
        raise StopConditionError(msg)

    source = source_state()
    prov: JsonDict = {
        "schema": "w4-w5-setup-provenance-v1",
        "utc": _utc_now(),
        "git_commit": source["commit"],
        "dirty": source["dirty"],
        "verovio_version": VEROVIO_VERSION,
        "verovio_wheel_sha256": VEROVIO_WHEEL_SHA256,
        "resvg_version": RESVG_VERSION,
        "resvg_wheel_sha256": RESVG_WHEEL_SHA256,
        "homr": homr_info,
        "download_ledger_total_bytes": ledger.total_bytes(),
    }
    atomic_write_text(
        downloads_dir / "w4-w5-setup-provenance.json",
        json.dumps(prov, indent=2, sort_keys=True) + "\n",
    )
    return prov


def verify_w4_w5_live_assets(  # noqa: C901
    downloads_dir: Path,
    *,
    require_s3: bool = False,
    require_homr: bool = False,
) -> JsonDict:
    """Bind live venv wheels and unpacked models to pinned setup provenance."""
    prov_path = downloads_dir / "w4-w5-setup-provenance.json"
    check_no_symlink_under(prov_path, downloads_dir)
    if not prov_path.is_file():
        msg = "w4-w5-setup-provenance.json missing; run setup-w4-w5 first"
        raise StopConditionError(msg)
    prov = json.loads(prov_path.read_text(encoding="utf-8"))
    if not isinstance(prov, dict):
        msg = "malformed w4-w5-setup-provenance.json"
        raise StopConditionError(msg)
    wheelhouse = downloads_dir / "w4-w5-wheelhouse"

    if require_s3:
        vrv_whl = wheelhouse / Path(VEROVIO_WHEEL_URL).name
        resvg_whl = wheelhouse / Path(RESVG_WHEEL_URL).name
        synth_py = venv_python_path(downloads_dir / "w4-synth-venv")
        if (
            not vrv_whl.is_file()
            or sha256_file(vrv_whl) != VEROVIO_WHEEL_SHA256
            or not resvg_whl.is_file()
            or sha256_file(resvg_whl) != RESVG_WHEEL_SHA256
            or prov.get("verovio_wheel_sha256") != VEROVIO_WHEEL_SHA256
            or prov.get("resvg_wheel_sha256") != RESVG_WHEEL_SHA256
            or not synth_py.is_file()
        ):
            msg = (
                "live Verovio/resvg wheel or venv differs from pinned setup; "
                "mutable-source recheck and disposition required"
            )
            raise StopConditionError(msg)
        live_vers = json.loads(
            subprocess.check_output(  # noqa: S603
                [
                    str(synth_py),
                    "-c",
                    (
                        "import json; from importlib.metadata import version; "
                        "print(json.dumps([version('verovio'), "
                        "version('resvg')]))"
                    ),
                ],
                text=True,
                timeout=30.0,
            ).strip()
        )
        if live_vers != [VEROVIO_VERSION, RESVG_VERSION]:
            msg = (
                f"live w4-synth-venv versions {live_vers} differ from "
                f"pinned {[VEROVIO_VERSION, RESVG_VERSION]}"
            )
            raise StopConditionError(msg)

    if require_homr:
        homr_meta = prov.get("homr")
        homr_whl = wheelhouse / Path(HOMR_WHEEL_URL).name
        homr_py = venv_python_path(downloads_dir / "w5-homr-venv")
        if (
            not isinstance(homr_meta, dict)
            or homr_meta.get("enabled") is not True
            or homr_meta.get("homr_version") != HOMR_VERSION
            or homr_meta.get("homr_wheel_sha256") != HOMR_WHEEL_SHA256
            or not homr_whl.is_file()
            or sha256_file(homr_whl) != HOMR_WHEEL_SHA256
            or not homr_py.is_file()
        ):
            msg = (
                "live HOMR wheel or venv differs from pinned setup; "
                "mutable-source recheck and disposition required"
            )
            raise StopConditionError(msg)
        info_s = subprocess.check_output(  # noqa: S603
            [
                str(homr_py),
                "-c",
                (
                    "import json; "
                    "from importlib.metadata import distribution, version; "
                    "print(json.dumps([version('homr'), "
                    "str(distribution('homr').locate_file('homr'))]))"
                ),
            ],
            text=True,
            timeout=30.0,
        ).strip()
        live_h_ver, homr_pkg_s = json.loads(info_s)
        if live_h_ver != HOMR_VERSION:
            msg = f"live w5-homr-venv homr=={live_h_ver} != {HOMR_VERSION}"
            raise StopConditionError(msg)
        homr_pkg = Path(homr_pkg_s)
        onnx_map = homr_meta.get("onnx_models")
        ckpt_map = homr_meta.get("checkpoints")
        if not isinstance(onnx_map, dict) or not isinstance(ckpt_map, dict):
            msg = "HOMR setup provenance missing checkpoint/ONNX digests"
            raise StopConditionError(msg)
        ckpt_cache = downloads_dir / "w5-homr-checkpoints"
        for role, c_url, c_bytes in HOMR_CPU_CHECKPOINTS:
            z_name = Path(c_url).name
            z_path = ckpt_cache / z_name
            onnx_name = f"{Path(z_name).stem}.onnx"
            onnx_path = (
                homr_pkg
                / ("segmentation" if role == "segnet" else "transformer")
            ) / onnx_name
            if (
                not z_path.is_file()
                or z_path.stat().st_size != c_bytes
                or sha256_file(z_path) != ckpt_map.get(z_name)
                or not onnx_path.is_file()
                or sha256_file(onnx_path) != onnx_map.get(onnx_name)
            ):
                msg = f"live HOMR checkpoint {onnx_name} failed digest check"
                raise StopConditionError(msg)
    return prov
