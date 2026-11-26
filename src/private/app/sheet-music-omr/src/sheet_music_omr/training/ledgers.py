"""Durable Slice A effect budgets and the sealed TRAIN_DEV read boundary."""

from __future__ import annotations

import hashlib
import json
import os
import pwd
import re
import stat
import subprocess
import sys
import urllib.parse
import urllib.request
import uuid
from contextlib import contextmanager, suppress
from http import HTTPStatus
from pathlib import Path
from typing import TYPE_CHECKING, Any, NoReturn, Self

from sheet_music_omr.evaluation import copista, wheel_lock
from sheet_music_omr.evaluation.alignment import (
    check_no_symlink_under,
    venv_python_path,
)
from sheet_music_omr.harness import privacy
from sheet_music_omr.harness.gold import gold_id
from sheet_music_omr.harness.records import repository_root
from sheet_music_omr.harness.stops import StopConditionError
from sheet_music_omr.synthetic.ledgers import (
    _exclusive_file_lock,
    _utc_now,
    directory_bytes,
)
from sheet_music_omr.training.asset_locks import (
    EXPECTED_TRAINDEV_PORTABLE_SHA256,
    TRAINDEV_GOLD_IDS,
    W6_MODEL_METADATA_LOCKS,
    compute_w6_metadata_lock_sha256,
    compute_w6_wheel_lock_sha256,
    resolve_host_w6_wheels,
)

if TYPE_CHECKING:
    from collections.abc import Iterator, Mapping, Sequence
    from contextlib import AbstractContextManager

W6_DOWNLOAD_LEDGER_NAME = "w6-slice-a-download-ledger.json"
W6_DOWNLOAD_CAP_BYTES = 15_000_000_000
W6_RUN_LEDGER_NAME = "w6-slice-a-run-ledger.jsonl"
W6_RUN_LOCK_NAME = ".w6-slice-a-run-ledger.lock"
W6_DISK_LOCK_NAME = "w6-slice-a-disk.lock"
W6_DISK_CAP_BYTES = 100_000_000_000
W6_SINGLE_RUN_PAGE_CAP = 15_000
W6_CUMULATIVE_PAGE_CAP = 35_000
W6_PRIVATE_COPISTA_CAP = 10
W6_WORKER_TIMEOUT_SECONDS = 300.0
_CAMPAIGN_ANCHOR_PATH: Path | None = None
PRIOR_CAMPAIGN_LEDGER_SHA256S: dict[str, str] = {
    "download-ledger.json": (
        "b6f071893803a2f2ddea4cf5c454a9b484f5a736d1ba7b30159898c2bf52e47b"
    ),
    "w2-download-ledger.json": (
        "008c786afccbd9048be8a1bf14b55806630550d451da2738dc2d5af6e8f9de13"
    ),
    "w2-touch-log.jsonl": (
        "dd9fdce39d693390e7c1269b98a2cd98e0fef6d6cf5bdda1b76585f1c73d89bf"
    ),
    "w4-w5-download-ledger.json": (
        "0d02ff9024b42106422f3eb1dd50f087a7c5d17a3f96afcfed9d5d7db085e03e"
    ),
    "w4-w5-run-ledger.jsonl": (
        "aef5ab7a5ba47c69a6fab574a83d275c81920f9debef51871e67d521318b8b57"
    ),
}
W6_OFFLINE_ENV = {
    "HF_HUB_OFFLINE": "1",
    "TRANSFORMERS_OFFLINE": "1",
    "PIP_NO_INDEX": "1",
    "UV_OFFLINE": "1",
    "PYTHONDONTWRITEBYTECODE": "1",
}

_WHEEL_CACHES = ("wheel-cache", "w4-wheels", "w6-wheels")
_META_NAMES = frozenset(
    mf.filename for repo in W6_MODEL_METADATA_LOCKS for mf in repo.files
)
_WIRE_OVERHEAD = 64 * 1024
_DIR_FLAGS = os.O_RDONLY | os.O_NOFOLLOW | os.O_DIRECTORY
_FILE_FLAGS = os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK
_MANIFEST = "traindev-manifest.json"
_PAGE_NAME = re.compile(r"pages/(gold-\d{2})-p([0-9]{2})\.png")
_SHA = re.compile(r"[a-f0-9]{64}")
_PAGE_COUNT = 69
_GOLD_COUNT = 23


def _stop(message: str) -> NoReturn:
    raise StopConditionError(message)


def _digest(value: object) -> str:
    if not isinstance(value, str) or not _SHA.fullmatch(value):
        _stop("invalid Slice A SHA-256 lock")
    return value


def _integer(value: object, minimum: int = 0) -> int:
    if not isinstance(value, int) or isinstance(value, bool) or value < minimum:
        _stop("invalid Slice A integer field")
    return value


def _object(value: object) -> dict[str, Any]:
    if not isinstance(value, dict):
        _stop("invalid Slice A JSON object")
    return value


def _read_json(path: Path) -> dict[str, Any]:
    try:
        return _object(json.loads(path.read_bytes()))
    except (OSError, ValueError) as exc:
        msg = "unreadable Slice A JSON record"
        raise StopConditionError(msg) from exc


def verify_prior_campaign_ledgers(downloads_dir: Path) -> dict[str, object]:
    """Require byte-for-byte preservation of retired campaign ledgers."""
    downloads = downloads_dir.absolute()
    check_no_symlink_under(downloads, Path(downloads.anchor))
    for name, expected in PRIOR_CAMPAIGN_LEDGER_SHA256S.items():
        path = downloads / name
        check_no_symlink_under(path, downloads)
        if not path.is_file():
            _stop(f"missing prior campaign ledger: {name}")
        if hashlib.sha256(_ledger_bytes(path)).hexdigest() != expected:
            _stop(f"prior campaign ledger SHA-256 mismatch: {name}")
    return dict(PRIOR_CAMPAIGN_LEDGER_SHA256S)


def _json_bytes(value: object) -> bytes:
    return json.dumps(value, sort_keys=True, separators=(",", ":")).encode()


def _sync_parent(path: Path) -> None:
    fd = os.open(path.parent, _DIR_FLAGS)
    try:
        os.fsync(fd)
    finally:
        os.close(fd)


def _write_json(path: Path, value: object) -> None:
    tmp = path.with_name(f".{path.name}.{uuid.uuid4().hex}")
    try:
        with tmp.open("xb") as stream:
            stream.write(_json_bytes(value) + b"\n")
            stream.flush()
            os.fsync(stream.fileno())
        tmp.replace(path)
        _sync_parent(path)
    finally:
        tmp.unlink(missing_ok=True)


def _verified_file(path: Path, size: int, sha256: str) -> bool:
    if not path.is_file() or path.stat().st_size != size:
        return False
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest() == sha256


def _wheel_name(url: str) -> str:
    return urllib.parse.unquote(Path(urllib.parse.urlsplit(url).path).name)


def _cache_paths(downloads: Path, name: str) -> Iterator[Path]:
    for subdir in _WHEEL_CACHES:
        yield downloads / subdir / name


def _validate_https(url: str) -> None:
    parsed = urllib.parse.urlsplit(url)
    if (
        parsed.scheme != "https"
        or not parsed.hostname
        or parsed.username is not None
        or parsed.password is not None
        or parsed.fragment
    ):
        _stop("refusing non-HTTPS or credentialed Slice A transfer")


def _transfer_locked(
    url: str, dest: Path, size: int, budget: int, wire: list[int]
) -> None:
    # No proxy CONNECT or urllib fallback can bypass the W2 wire meter.
    opener = urllib.request.build_opener(
        urllib.request.ProxyHandler({}),
        copista._MeteredHTTPSHandler(budget, wire, "W6 asset"),  # noqa: SLF001
        copista._NoRedirectHandler(),  # noqa: SLF001
    )
    for _ in range(6):
        _validate_https(url)
        if Path(_wheel_name(url)).suffix.lower() in {
            ".py",
            ".bin",
            ".pt",
            ".pth",
            ".safetensors",
            ".ckpt",
            ".onnx",
        }:
            _stop("refusing remote code or weights in Slice A")
        request = urllib.request.Request(  # noqa: S310
            url, headers={"Accept-Encoding": "identity"}
        )
        with opener.open(request, timeout=180) as response:
            if response.status in {301, 302, 303, 307, 308}:
                location = response.headers.get("Location")
                if not location:
                    _stop("Slice A transfer redirect lacks a location")
                url = urllib.parse.urljoin(url, location)
                continue
            if (
                response.status != HTTPStatus.OK
                or response.headers.get_all("Content-Length") != [str(size)]
                or response.headers.get("Transfer-Encoding") is not None
                or response.headers.get("Content-Encoding", "identity")
                != "identity"
            ):
                _stop("Slice A response is not an exact locked payload")
            with dest.open("xb") as stream:
                remaining = size
                while remaining:
                    chunk = response.read(min(remaining, 1 << 20))
                    if not chunk:
                        _stop("truncated Slice A payload")
                    stream.write(chunk)
                    remaining -= len(chunk)
                stream.flush()
                os.fsync(stream.fileno())
            return
    _stop("too many Slice A redirects")


def _ledger_bytes(path: Path, *, optional: bool = False) -> bytes:
    check_no_symlink_under(path, path.parent)
    if optional and not path.exists():
        return b""
    if not path.is_file():
        _stop(f"missing or invalid Slice A ledger: {path.name}")
    try:
        return path.read_bytes()
    except OSError as exc:
        msg = f"unreadable Slice A ledger: {path.name}"
        raise StopConditionError(msg) from exc


def _read_download_ledger(raw: bytes) -> dict[str, Any]:
    try:
        data = _object(json.loads(raw))
    except ValueError as exc:
        msg = "invalid Slice A download ledger"
        raise StopConditionError(msg) from exc
    if (
        data.get("schema") != "omr-w6-slice-a-download-ledger-v1"
        or not isinstance(data.get("entries"), list)
        or sum(
            _integer(_object(e).get("charged_bytes")) for e in data["entries"]
        )
        != _integer(data.get("total_bytes"))
    ):
        _stop("invalid Slice A download ledger")
    return data


class W6DownloadLedger:
    """Reserve a bounded response (payload plus wire overhead) before GET."""

    def __init__(self, downloads_dir: Path) -> None:
        """Locate the persistent shared download ledger."""
        _read_campaign_anchor()
        self.downloads_dir = downloads_dir.absolute()
        self.path = self.downloads_dir / W6_DOWNLOAD_LEDGER_NAME

    def _write(self, data: dict[str, Any], anchor: dict[str, Any]) -> None:
        encoded = _json_bytes(data) + b"\n"
        new_sha = hashlib.sha256(encoded).hexdigest()
        anchor["pending_download_ledger_bytes"] = len(encoded)
        anchor["pending_download_ledger_sha256"] = new_sha
        _write_json(_campaign_anchor_path(), anchor)
        _write_json(self.path, data)
        anchor.pop("pending_download_ledger_bytes", None)
        anchor.pop("pending_download_ledger_sha256", None)
        anchor["download_ledger_bytes"] = len(encoded)
        anchor["download_ledger_sha256"] = new_sha
        _write_json(_campaign_anchor_path(), anchor)

    def total_bytes(self) -> int:
        """Return settled bytes plus every outstanding reservation."""
        with _campaign_lock():
            _, raw, _ = _bind_campaign_roots(self.downloads_dir)
            return _integer(_read_download_ledger(raw)["total_bytes"])

    def ensure_cached_file(
        self, *, url: str, dest: Path, expected_bytes: int, expected_sha256: str
    ) -> Path:
        """Return a verified cache path, reserving before all network I/O."""
        _validate_https(url)
        name = _wheel_name(url)
        if name != dest.name or not (
            name in _META_NAMES or name.endswith(".whl")
        ):
            _stop("refusing unlocked remote code or weights in Slice A")
        _integer(expected_bytes, 1)
        _digest(expected_sha256)
        dest = dest.absolute()
        check_no_symlink_under(dest, self.downloads_dir)
        budget = expected_bytes + _WIRE_OVERHEAD
        with _campaign_lock():
            anchor, raw, _ = _bind_campaign_roots(self.downloads_dir)
            data = _read_download_ledger(raw)
            candidates = [dest]
            if name.endswith(".whl"):
                candidates.extend(_cache_paths(self.downloads_dir, name))
            for path in dict.fromkeys(candidates):
                check_no_symlink_under(path, self.downloads_dir)
                if _verified_file(path, expected_bytes, expected_sha256):
                    return path
            if dest.exists():
                _stop("cached Slice A asset has drifted")
            if data["total_bytes"] + budget > W6_DOWNLOAD_CAP_BYTES:
                _stop("Slice A 15 GB download cap would be exceeded")
            record = {
                "asset": name,
                "started_utc": _utc_now(),
                "reserved_bytes": budget,
                "charged_bytes": budget,
                "status": "reserved",
            }
            data["entries"].append(record)
            data["total_bytes"] += budget
            self._write(data, anchor)
            self._receive(url, dest, expected_bytes, expected_sha256, record)
            data["total_bytes"] -= budget - _integer(record["charged_bytes"])
            self._write(data, anchor)
        return dest

    @staticmethod
    def _receive(
        url: str,
        dest: Path,
        size: int,
        sha256: str,
        record: dict[str, Any],
    ) -> None:
        dest.parent.mkdir(parents=True, exist_ok=True)
        tmp = dest.with_name(f".{dest.name}.{uuid.uuid4().hex}.part")
        budget = _integer(record["reserved_bytes"], 1)
        wire = [0]
        try:
            _transfer_locked(url, tmp, size, budget, wire)
            if not size <= wire[0] <= budget:
                _stop("unmetered Slice A transfer")
            if not _verified_file(tmp, size, sha256):
                _stop("Slice A payload failed locked size or digest check")
            tmp.replace(dest)
            _sync_parent(dest)
            record.update(
                status="completed",
                charged_bytes=wire[0],
                settled_utc=_utc_now(),
            )
        finally:
            tmp.unlink(missing_ok=True)


def _meta_assets(downloads: Path) -> Iterator[tuple[str, Path, int, str]]:
    for repo in W6_MODEL_METADATA_LOCKS:
        for mf in repo.files:
            path = downloads / "w6-model-meta" / repo.slug / mf.filename
            url = f"https://huggingface.co/{repo.repo_id}/resolve"
            yield (
                f"{url}/{repo.commit_sha}/{mf.filename}",
                path,
                mf.size,
                mf.sha256,
            )


def _venv_versions(
    python: Path, expected: Mapping[str, str], downloads: Path
) -> dict[str, object]:
    probe = (
        "import importlib.metadata as m,json,sys;"
        "print(json.dumps({'python': sys.version.split()[0], "
        "'packages': {p: m.version(p) for p in sys.argv[1:]}}))"
    )
    try:
        result = subprocess.check_output(  # noqa: S603
            [str(python), "-I", "-B", "-c", probe, *expected],
            timeout=30,
            cwd=downloads,
            env=os.environ | W6_OFFLINE_ENV,
        )
        versions = _object(json.loads(result))
        if versions.get("packages") != dict(expected):
            _stop("installed Slice A package versions differ from lock")
    except (OSError, subprocess.SubprocessError, ValueError) as exc:
        msg = "cannot verify Slice A venv versions"
        raise StopConditionError(msg) from exc
    return versions


def setup_w6_slice_a(
    downloads_dir: Path, *, init_campaign: bool = False
) -> dict[str, object]:
    """Fetch only locked wheels and metadata; install exact local files."""
    downloads = downloads_dir.absolute()
    root = repository_root()
    if root is None or downloads.resolve().is_relative_to(root):
        _stop("W6 provisioning directory must be outside the git worktree")
    if init_campaign:
        _init_w6_campaign_anchor(downloads)
    ledger = W6DownloadLedger(downloads)
    ledger.total_bytes()
    wheels = resolve_host_w6_wheels()
    venv = downloads / "w6-train-venv"
    check_no_symlink_under(venv, downloads)
    with _exclusive_file_lock(downloads / ".w6-train-setup.lock"):
        paths = [
            ledger.ensure_cached_file(
                url=wheel.url,
                dest=downloads / "w6-wheels" / _wheel_name(wheel.url),
                expected_bytes=wheel.size,
                expected_sha256=wheel.sha256,
            )
            for wheel in wheels
        ]
        for url, path, size, sha256 in _meta_assets(downloads):
            ledger.ensure_cached_file(
                url=url, dest=path, expected_bytes=size, expected_sha256=sha256
            )
        create = [sys.executable, "-I", "-m", "venv", str(venv)]
        install = [
            str(venv_python_path(venv)),
            "-I",
            "-m",
            "pip",
            "--isolated",
            "install",
            "--no-index",
            "--no-deps",
            "--force-reinstall",
            "--no-cache-dir",
            "--disable-pip-version-check",
            *map(str, paths),
        ]
        env = os.environ | W6_OFFLINE_ENV | {"TMPDIR": str(downloads)}
        for command in (create, install):
            try:
                subprocess.run(  # noqa: S603
                    command,
                    env=env,
                    cwd=downloads,
                    check=True,
                    timeout=W6_WORKER_TIMEOUT_SECONDS,
                )
            except (OSError, subprocess.SubprocessError) as exc:
                msg = "offline W6 venv provisioning failed"
                raise StopConditionError(msg) from exc
        return verify_w6_training_assets(downloads) | {
            "download_bytes": ledger.total_bytes()
        }


def verify_w6_training_assets(downloads_dir: Path) -> dict[str, object]:
    """Verify locked W6 wheels, metadata and venv without W2 or network I/O."""
    downloads = downloads_dir.absolute()
    check_no_symlink_under(downloads, Path(downloads.anchor))
    wheels = resolve_host_w6_wheels()
    for wheel in wheels:
        for path in _cache_paths(downloads, _wheel_name(wheel.url)):
            check_no_symlink_under(path, downloads)
            if _verified_file(path, wheel.size, wheel.sha256):
                break
        else:
            _stop("W6 wheel drift; accepted mutable-source recheck required")
    for _, path, size, sha256 in _meta_assets(downloads):
        check_no_symlink_under(path, downloads)
        if not _verified_file(path, size, sha256):
            _stop("W6 metadata drift; accepted mutable-source recheck required")
    venv = downloads / "w6-train-venv"
    check_no_symlink_under(venv / "pyvenv.cfg", downloads)
    if not (venv / "pyvenv.cfg").is_file():
        _stop("missing locked W6 training venv")
    return {
        "wheel_lock_sha256": compute_w6_wheel_lock_sha256(wheels),
        "metadata_lock_sha256": compute_w6_metadata_lock_sha256(),
        "training_venv": _venv_versions(
            venv_python_path(venv),
            {wheel.name: wheel.version for wheel in wheels},
            downloads,
        ),
    }


def verify_w6_live_assets(downloads_dir: Path) -> dict[str, object]:
    """Verify local W6 assets and the pinned offline W2 reader; never fetch."""
    downloads = downloads_dir.absolute()
    info = verify_w6_training_assets(downloads)
    cache = downloads / "w2-copista-cache"
    venv = downloads / "w2-copista-venv"
    commit = wheel_lock.COPISTERIA_COMMIT
    source = cache / f"copisteria-{commit}"
    archive = cache / f"copisteria-{commit}.tar.gz"
    stamp = venv / ".copista-wheel-lock-sha256"
    for path in (source, archive, stamp, cache / "provenance.json"):
        check_no_symlink_under(path, downloads)
    tree = copista._verify_copisteria_archive_and_unpack(  # noqa: SLF001
        archive, cache, source, allow_extract=False
    )
    prov = _read_json(cache / "provenance.json")
    lock = wheel_lock.compute_wheel_lock_sha256()
    if (
        prov.get("copisteria_commit") != commit
        or prov.get("copisteria_archive_sha256")
        != wheel_lock.COPISTERIA_ARCHIVE_SHA256
        or prov.get("copisteria_src_tree_sha256") != tree
        or prov.get("copista_hf_revision") != wheel_lock.COPISTA_HF_REVISION
        or not stamp.is_file()
        or stamp.read_text(encoding="utf-8").strip() != lock
        or not (venv / "pyvenv.cfg").is_file()
    ):
        _stop("W2 Copista cache provenance drift")
    versions = {}
    for (
        name,
        version,
        url,
        size,
        sha256,
    ) in wheel_lock.resolve_host_wheel_lock():
        path = cache / "wheelhouse" / _wheel_name(url)
        check_no_symlink_under(path, downloads)
        if not _verified_file(path, size, sha256):
            _stop("W2 Copista wheel drift")
        versions[name] = version
    weights = {}
    for rel, size, sha256 in wheel_lock.COPISTA_WEIGHTS:
        if not _verified_file(source / "models" / rel, size, sha256):
            _stop("W2 Copista weight drift")
        weights[rel] = sha256
    return info | {
        "copista_revision": wheel_lock.COPISTA_HF_REVISION,
        "copisteria_archive_sha256": wheel_lock.COPISTERIA_ARCHIVE_SHA256,
        "copisteria_src_tree_sha256": tree,
        "copista_wheel_lock_sha256": lock,
        "copista_weight_sha256": weights,
        "copista_venv": _venv_versions(
            venv_python_path(venv), versions, downloads
        ),
    }


def check_w6_runs_root(
    downloads_dir: Path, runs_parent: Path | None = None
) -> Path:
    """Require a single symlink-free runs directory beside downloads."""
    downloads = downloads_dir.absolute()
    check_no_symlink_under(downloads, Path(downloads.anchor))
    downloads = downloads.resolve()
    runs = downloads.parent / "runs"
    check_no_symlink_under(runs, runs.parent)
    if (
        runs == downloads
        or (runs.exists() and not runs.is_dir())
        or (runs_parent is not None and runs_parent.resolve() != runs.resolve())
    ):
        _stop("W6 run directory must be directly under the canonical runs root")
    return runs


def _default_campaign_anchor_path() -> Path:
    return (
        Path(pwd.getpwuid(os.geteuid()).pw_dir)
        / ".local"
        / "state"
        / "sheet-music-omr"
        / "w6-slice-a-campaign-anchor.json"
    )


def _campaign_anchor_path() -> Path:
    return _CAMPAIGN_ANCHOR_PATH or _default_campaign_anchor_path()


def _read_campaign_anchor() -> dict[str, Any]:
    path = _campaign_anchor_path()
    check_no_symlink_under(path, Path(path.anchor))
    if not path.is_file():
        _stop(
            "missing Slice A campaign anchor (--init-campaign is for first "
            "use only; recover original state or escalate)"
        )
    return _read_json(path)


def _campaign_lock() -> AbstractContextManager[None]:
    path = _campaign_anchor_path()
    check_no_symlink_under(path, Path(path.anchor))
    return _exclusive_file_lock(path.with_suffix(".lock"))


def _disk_lock() -> AbstractContextManager[None]:
    path = _campaign_anchor_path()
    check_no_symlink_under(path, Path(path.anchor))
    return _exclusive_file_lock(path.with_name(W6_DISK_LOCK_NAME))


def _campaign_root_id(path: Path) -> list[int]:
    try:
        info = path.stat()
    except OSError as exc:
        msg = "missing or unreadable Slice A campaign root"
        raise StopConditionError(msg) from exc
    if not stat.S_ISDIR(info.st_mode):
        _stop("invalid Slice A campaign root")
    return list(_identity(info))


def _matches_root_id(recorded: object, observed: list[int]) -> bool:
    return (
        isinstance(recorded, list)
        and all(type(value) is int for value in recorded)
        and recorded == observed
    )


def _campaign_roots(downloads: Path, runs: Path) -> dict[str, object]:
    return {
        "downloads_path": str(downloads.resolve()),
        "downloads_root_id": _campaign_root_id(downloads),
        "runs_path": str(runs),
        "runs_root_id": _campaign_root_id(runs),
    }


def _check_bound_roots(downloads: Path, anchor: Mapping[str, object]) -> Path:
    runs = check_w6_runs_root(downloads)
    if (
        anchor.get("downloads_path") != str(downloads.resolve())
        or anchor.get("runs_path") != str(runs)
        or not _matches_root_id(
            anchor.get("downloads_root_id"), _campaign_root_id(downloads)
        )
        or not _matches_root_id(
            anchor.get("runs_root_id"), _campaign_root_id(runs)
        )
    ):
        _stop("Slice A campaign is anchored to a different root")
    return runs


def _run_ledger_stamp(raw: bytes) -> dict[str, object]:
    return {
        "run_ledger_bytes": len(raw),
        "run_ledger_prefix_sha256": hashlib.sha256(raw).hexdigest(),
    }


def _campaign_ledger_bytes(
    downloads: Path,
    anchor: dict[str, Any],
    *,
    require_run: bool = False,
    allow_recover: bool = True,
) -> tuple[bytes, bytes]:
    download_raw = _ledger_bytes(downloads / W6_DOWNLOAD_LEDGER_NAME)
    observed = (len(download_raw), hashlib.sha256(download_raw).hexdigest())
    committed = (
        _integer(anchor.get("download_ledger_bytes"), 1),
        _digest(anchor.get("download_ledger_sha256")),
    )
    pending_sha = anchor.get("pending_download_ledger_sha256")
    pending = (
        (
            _integer(anchor.get("pending_download_ledger_bytes"), 1),
            _digest(pending_sha),
        )
        if allow_recover and pending_sha is not None
        else None
    )
    if observed == pending:
        anchor.pop("pending_download_ledger_bytes", None)
        anchor.pop("pending_download_ledger_sha256", None)
        anchor["download_ledger_bytes"], anchor["download_ledger_sha256"] = (
            observed
        )
        _write_json(_campaign_anchor_path(), anchor)
    elif observed == committed:
        if allow_recover and pending is not None:
            anchor.pop("pending_download_ledger_bytes", None)
            anchor.pop("pending_download_ledger_sha256", None)
            _write_json(_campaign_anchor_path(), anchor)
    else:
        _stop("Slice A download ledger length or SHA-256 mismatch")
    run_raw = _ledger_bytes(
        downloads / W6_RUN_LEDGER_NAME, optional=not require_run
    )
    count = _integer(anchor.get("run_ledger_bytes"))
    if len(run_raw) < count or hashlib.sha256(
        run_raw[:count]
    ).hexdigest() != _digest(anchor.get("run_ledger_prefix_sha256")):
        _stop("Slice A run ledger prefix SHA-256 mismatch")
    return download_raw, run_raw


def _bind_campaign_roots(
    downloads: Path,
) -> tuple[dict[str, Any], bytes, bytes]:
    """Validate the pinned roots and ledger bytes under the campaign lock."""
    anchor = _read_campaign_anchor()
    _check_no_unreaped_worker()
    _check_bound_roots(downloads, anchor)
    verify_prior_campaign_ledgers(downloads)
    download_raw, run_raw = _campaign_ledger_bytes(downloads, anchor)
    return anchor, download_raw, run_raw


def _init_w6_campaign_anchor(downloads: Path) -> None:
    with _campaign_lock():
        path = _campaign_anchor_path()
        if path.exists():
            _bind_campaign_roots(downloads)
            return
        verify_prior_campaign_ledgers(downloads)
        runs = check_w6_runs_root(downloads)
        _check_w6_disk_cap(runs)
        run_raw = _ledger_bytes(downloads / W6_RUN_LEDGER_NAME, optional=True)
        _run_state(run_raw)
        download_path = downloads / W6_DOWNLOAD_LEDGER_NAME
        check_no_symlink_under(download_path, downloads)
        if not download_path.exists():
            _write_json(
                download_path,
                {
                    "schema": "omr-w6-slice-a-download-ledger-v1",
                    "total_bytes": 0,
                    "entries": [],
                },
            )
        download_raw = _ledger_bytes(download_path)
        _read_download_ledger(download_raw)
        runs.mkdir(exist_ok=True)
        _write_json(
            path,
            _campaign_roots(downloads, runs)
            | _run_ledger_stamp(run_raw)
            | {
                "download_ledger_bytes": len(download_raw),
                "download_ledger_sha256": hashlib.sha256(
                    download_raw
                ).hexdigest(),
            },
        )


def migrate_w6_campaign_roots(
    old_downloads: Path, new_downloads: Path
) -> dict[str, object]:
    """Rebind the campaign after preserving its ledgers and retained runs."""
    old_downloads, new_downloads = (
        old_downloads.absolute(),
        new_downloads.absolute(),
    )
    with _disk_lock(), _campaign_lock():
        anchor = _read_campaign_anchor()
        _check_no_unreaped_worker()
        old_runs = _check_bound_roots(old_downloads, anchor)
        new_runs = check_w6_runs_root(new_downloads)
        verify_prior_campaign_ledgers(new_downloads)
        old_download_raw, old_run_raw = _campaign_ledger_bytes(
            old_downloads, anchor
        )
        need_run = _integer(anchor.get("run_ledger_bytes")) > 0 or bool(
            old_run_raw
        )
        new_download_raw, run_raw = _campaign_ledger_bytes(
            new_downloads,
            anchor,
            require_run=need_run,
            allow_recover=False,
        )
        if new_download_raw != old_download_raw:
            _stop("Slice A migration would discard existing download ledger")
        if not run_raw.startswith(old_run_raw):
            _stop("Slice A migration would discard existing run ledger bytes")
        _run_state(run_raw)
        if old_runs != new_runs and any(old_runs.glob("w6-slice-a-*")):
            _stop("move all retained Slice A run directories before migration")
        _check_w6_disk_cap(new_runs)
        new_runs.mkdir(exist_ok=True)
        _write_json(
            _campaign_anchor_path(),
            anchor
            | _campaign_roots(new_downloads, new_runs)
            | _run_ledger_stamp(run_raw),
        )
        return {"status": "migrated"}


@contextmanager
def w6_disk_lock(
    downloads_dir: Path, runs_parent: Path | None = None
) -> Iterator[Path]:
    """Serialize disk checks and writes against the pinned runs root."""
    downloads = downloads_dir.absolute()
    with _disk_lock():
        with _campaign_lock():
            _bind_campaign_roots(downloads)
            runs = check_w6_runs_root(downloads, runs_parent)
        yield runs


def check_w6_disk_cap(
    downloads_dir: Path,
    runs_parent: Path | None = None,
    *,
    additional_bytes: int = 0,
) -> int:
    """Check all Slice A run directories, including aborted runs."""
    with w6_disk_lock(downloads_dir, runs_parent) as runs:
        return _check_w6_disk_cap(runs, additional_bytes=additional_bytes)


W6_SCRATCH_RESERVATION_FILE = ".w6-scratch-reservation.json"
W6_UNREAPED_WORKER_NAME = ".w6-unreaped-worker.json"


def _pending_native_worker_path() -> Path:
    path = _campaign_anchor_path().with_name(W6_UNREAPED_WORKER_NAME)
    check_no_symlink_under(path, Path(path.anchor))
    return path


def has_pending_w6_native_worker(reservation_id: str | None = None) -> bool:
    """Return whether an unconfirmed native-worker fence is present."""
    marker = _pending_native_worker_path()
    if reservation_id is None:
        return marker.exists()
    try:
        raw = marker.read_bytes()
    except FileNotFoundError:
        return False
    except OSError:
        return True
    try:
        data = _object(json.loads(raw))
    except StopConditionError, ValueError:
        return True
    return data.get("reservation_id") == reservation_id


def _check_no_unreaped_worker() -> None:
    if has_pending_w6_native_worker():
        _stop(
            "unconfirmed W6 native worker exit blocks further campaign launches"
        )


def begin_w6_native_worker(detail: str, *, reservation_id: str = "") -> str:
    """Publish a populated exclusive token-bound fence atomically pre-launch."""
    marker = _pending_native_worker_path()
    marker.parent.mkdir(parents=True, exist_ok=True)
    token = uuid.uuid4().hex
    payload = (
        _json_bytes(
            {
                "token": token,
                "reservation_id": reservation_id,
                "detail": detail,
                "utc": _utc_now(),
            }
        )
        + b"\n"
    )
    tmp = marker.with_name(f"{marker.name}.{token}.part")
    check_no_symlink_under(tmp, marker.parent)
    try:
        with tmp.open("xb") as stream:
            stream.write(payload)
            stream.flush()
            os.fsync(stream.fileno())
        os.link(tmp, marker, follow_symlinks=False)
    except FileExistsError:
        _stop(
            "unconfirmed W6 native worker exit blocks further campaign launches"
        )
    finally:
        tmp.unlink(missing_ok=True)
    _sync_parent(marker)
    return token


def clear_w6_native_worker(token: str) -> None:
    """Clear the pending fence only for the exact launch token that set it."""
    marker = _pending_native_worker_path()
    if not marker.is_file():
        _stop("missing W6 native worker fence on exit clearance")
    data = _read_json(marker)
    if data.get("token") != token:
        _stop("W6 native worker fence token mismatch on exit clearance")
    marker.unlink()
    _sync_parent(marker)


def _unique_file_bytes(
    fp: Path, seen_inodes: set[tuple[int, int]] | None
) -> tuple[int, int]:
    with suppress(FileNotFoundError):
        info = fp.stat(follow_symlinks=False)
        if stat.S_ISREG(info.st_mode):
            ident = _identity(info)
            if seen_inodes is None:
                return info.st_size, info.st_size
            is_new = ident not in seen_inodes
            seen_inodes.add(ident)
            return info.st_size, (info.st_size if is_new else 0)
    return 0, 0


def w6_scratch_dir_bytes(
    directory: Path,
    *,
    seen_inodes: set[tuple[int, int]] | None = None,
) -> int:
    """Return ``max(reserved_bytes, actual_bytes)`` in a single walk."""
    if not directory.exists():
        return 0
    meta = directory / W6_SCRATCH_RESERVATION_FILE
    if meta.is_symlink():
        _stop("invalid Slice A scratch reservation symlink")
    reserved = 0
    if meta.is_file():
        try:
            payload = json.loads(meta.read_text(encoding="utf-8"))
            reserved = _integer(payload.get("reserved_bytes"), minimum=1)
        except OSError, ValueError, AttributeError:
            _stop("malformed Slice A scratch reservation metadata")
    actual = 0
    unique = 0
    for dirpath, _, filenames in os.walk(directory, followlinks=False):
        dp = Path(dirpath)
        for fn in filenames:
            fp = dp / fn
            if fp == meta or fp.is_symlink():
                continue
            raw_sz, uniq_sz = _unique_file_bytes(fp, seen_inodes)
            actual += raw_sz
            unique += uniq_sz
    if seen_inodes is None:
        return max(reserved, actual)
    return unique + max(0, reserved - actual)


def _directory_unique_bytes(
    root: Path, seen_inodes: set[tuple[int, int]]
) -> int:
    if not root.exists():
        return 0
    total = 0
    for dirpath, _, filenames in os.walk(root, followlinks=False):
        dp = Path(dirpath)
        for fn in filenames:
            fp = dp / fn
            if not fp.is_symlink():
                _, uniq_sz = _unique_file_bytes(fp, seen_inodes)
                total += uniq_sz
    return total


def _w6_tmp_bytes(
    tmp: Path, seen_inodes: set[tuple[int, int]] | None = None
) -> int:
    if tmp.is_symlink() or not tmp.is_dir():
        _stop("invalid Slice A scratch directory")
    total = 0
    for child in tmp.iterdir():
        if child.is_symlink():
            _stop("invalid Slice A scratch job directory")
        if child.is_dir():
            total += w6_scratch_dir_bytes(child, seen_inodes=seen_inodes)
        else:
            _, uniq_sz = _unique_file_bytes(child, seen_inodes)
            total += uniq_sz
    return total


def _w6_run_bytes(
    directory: Path, seen_inodes: set[tuple[int, int]] | None = None
) -> int:
    total = 0
    for entry in directory.iterdir():
        if entry.name == "tmp":
            total += _w6_tmp_bytes(entry, seen_inodes)
        elif not entry.is_symlink():
            if entry.is_dir():
                total += (
                    directory_bytes(entry)
                    if seen_inodes is None
                    else _directory_unique_bytes(entry, seen_inodes)
                )
            else:
                _, uniq_sz = _unique_file_bytes(entry, seen_inodes)
                total += uniq_sz
    return total


def _check_w6_disk_cap(runs: Path, *, additional_bytes: int = 0) -> int:
    """Measure physical disk use across all Slice A runs under the disk lock."""
    total = 0
    seen_inodes: set[tuple[int, int]] = set()
    for directory in sorted(runs.glob("w6-slice-a-*")):
        if directory.is_symlink() or not directory.is_dir():
            _stop("invalid Slice A run directory")
        total += _w6_run_bytes(directory, seen_inodes)
    if total + _integer(additional_bytes) > W6_DISK_CAP_BYTES:
        _stop("Slice A 100 GB generated disk cap would be exceeded")
    return total


def _append_run(
    downloads: Path,
    anchor: dict[str, Any],
    raw: bytes,
    record: dict[str, object],
) -> None:
    path = downloads / W6_RUN_LEDGER_NAME
    line = _json_bytes(record | {"utc": _utc_now()}) + b"\n"
    with path.open("ab") as stream:
        stream.write(line)
        stream.flush()
        os.fsync(stream.fileno())
    _sync_parent(path)
    _write_json(_campaign_anchor_path(), anchor | _run_ledger_stamp(raw + line))


@contextmanager
def _locked_run_ledger(
    downloads: Path,
) -> Iterator[tuple[dict[str, Any], bytes]]:
    with _campaign_lock():
        anchor = _read_campaign_anchor()
        _check_bound_roots(downloads, anchor)
        verify_prior_campaign_ledgers(downloads)
        with _exclusive_file_lock(downloads / W6_RUN_LOCK_NAME):
            _, raw = _campaign_ledger_bytes(downloads, anchor)
            yield anchor, raw


def _run_records(raw: bytes) -> Iterator[dict[str, Any]]:
    if raw and not raw.endswith(b"\n"):
        _stop("incomplete Slice A run ledger record")
    try:
        for line in raw.splitlines():
            yield _object(json.loads(line))
    except ValueError as exc:
        msg = "invalid Slice A run ledger"
        raise StopConditionError(msg) from exc


def _run_state(
    raw: bytes,
) -> tuple[dict[str, Any], dict[str, Any], dict[str, Any]]:
    started: dict[str, Any] = {}
    settled: dict[str, Any] = {}
    anchor: dict[str, Any] = {}
    for rec in _run_records(raw):
        event = rec.get("event")
        if event == "traindev-root-anchor" or (
            event is None and rec.get("kind") == "traindev-root-anchor"
        ):
            anchor = rec
            continue
        key = rec.get("reservation_id")
        if not isinstance(key, str) or not key:
            _stop("invalid Slice A reservation ID")
        if event == "started" and key not in started:
            if rec.get("kind") not in {"render", "copista"}:
                _stop("invalid Slice A reservation kind")
            started[key] = rec
        elif event == "settled" and key in started and key not in settled:
            settled[key] = rec
        else:
            _stop("invalid Slice A ledger lifecycle")
    return started, settled, anchor


def _start(  # noqa: PLR0913
    downloads: Path,
    anchor: dict[str, Any],
    raw: bytes,
    *,
    run_id: str,
    git_commit: str,
    clean_tree: bool,
    command: str,
    kind: str,
    pages: int = 0,
) -> str:
    token = uuid.uuid4().hex
    _append_run(
        downloads,
        anchor,
        raw,
        {
            "event": "started",
            "reservation_id": token,
            "kind": kind,
            "run_id": run_id,
            "git_commit": git_commit,
            "clean_tree": clean_tree,
            "command": command,
            "reserved_pages": pages,
        },
    )
    return token


def reserve_w6_render_run(  # noqa: PLR0913
    downloads_dir: Path,
    *,
    run_id: str,
    git_commit: str,
    clean_tree: bool,
    command: str,
    planned_pages: int,
    runs_parent: Path | None = None,
) -> str:
    """Charge a started render even if its process never settles."""
    downloads = downloads_dir.absolute()
    if not 1 <= _integer(planned_pages) <= W6_SINGLE_RUN_PAGE_CAP:
        _stop("Slice A planned pages outside 1..15,000 per-run bound")
    with (
        w6_disk_lock(downloads, runs_parent) as runs,
        _locked_run_ledger(downloads) as (anchor, raw),
    ):
        _check_no_unreaped_worker()
        _check_w6_disk_cap(runs)
        starts, settlements, _ = _run_state(raw)
        charged = sum(
            _integer(settlements[key]["charged_pages"], 1)
            if key in settlements
            else _integer(row["reserved_pages"], 1)
            for key, row in starts.items()
            if row["kind"] == "render"
        )
        if charged + planned_pages > W6_CUMULATIVE_PAGE_CAP:
            _stop("Slice A cumulative render page cap would be exceeded")
        return _start(
            downloads,
            anchor,
            raw,
            run_id=run_id,
            git_commit=git_commit,
            clean_tree=clean_tree,
            command=command,
            kind="render",
            pages=planned_pages,
        )


def _validate_anchor(anchor: Mapping[str, object]) -> None:
    if anchor.get("portable_aggregate_sha256") != (
        EXPECTED_TRAINDEV_PORTABLE_SHA256
    ):
        _stop("missing or drifted portable TRAIN_DEV anchor")
    _digest(anchor.get("staging_attestation_sha256"))
    _integer(anchor.get("root_st_dev"))
    _integer(anchor.get("root_st_ino"), 1)


def reserve_w6_copista_invocation_and_read_anchor(
    downloads_dir: Path,
    *,
    run_id: str,
    git_commit: str,
    clean_tree: bool,
    command: str,
) -> tuple[str, dict[str, object]]:
    """Atomically pin an external anchor and charge one private invocation."""
    downloads = downloads_dir.absolute()
    with _locked_run_ledger(downloads) as (campaign, raw):
        _check_no_unreaped_worker()
        starts, _, anchor = _run_state(raw)
        _validate_anchor(anchor)
        if sum(row["kind"] == "copista" for row in starts.values()) >= (
            W6_PRIVATE_COPISTA_CAP
        ):
            _stop("Slice A private Copista invocation cap would be exceeded")
        token = _start(
            downloads,
            campaign,
            raw,
            run_id=run_id,
            git_commit=git_commit,
            clean_tree=clean_tree,
            command=command,
            kind="copista",
        )
    return token, anchor


def verify_completed_w6_render_run(downloads_dir: Path, run_id: str) -> None:
    """Require a settled completed render entry in the Slice A run ledger."""
    downloads = downloads_dir.absolute()
    with _locked_run_ledger(downloads) as (_, raw):
        starts, settlements, _ = _run_state(raw)
        if not any(
            row.get("kind") == "render"
            and row.get("run_id") == run_id
            and settlements.get(res_id, {}).get("status") == "completed"
            for res_id, row in starts.items()
        ):
            _stop(f"base W6 run {run_id} has no completed Slice A ledger entry")


def settle_w6_run(
    downloads_dir: Path,
    reservation_id: str,
    *,
    actual_pages: int = 0,
    status: str,
) -> None:
    """Settle a reservation exactly once, never refunding a started read."""
    downloads = downloads_dir.absolute()
    _integer(actual_pages)
    if status not in {"completed", "failed", "aborted"}:
        _stop("invalid Slice A settlement status")
    with _locked_run_ledger(downloads) as (anchor, raw):
        starts, settlements, _ = _run_state(raw)
        if reservation_id not in starts or reservation_id in settlements:
            _stop("unknown or already-settled Slice A reservation")
        row = starts[reservation_id]
        is_render = row["kind"] == "render"
        _append_run(
            downloads,
            anchor,
            raw,
            {
                "event": "settled",
                "reservation_id": reservation_id,
                "status": status,
                "actual_pages": actual_pages,
                "charged_pages": max(actual_pages, 1) if is_render else 0,
            },
        )
        if is_render and actual_pages > row["reserved_pages"]:
            _stop("Slice A run exceeded its render reservation")


@contextmanager
def _walk_directory(
    parts: Sequence[str],
    parent_fd: int | None = None,
) -> Iterator[int]:
    fd = (
        os.dup(parent_fd)
        if parent_fd is not None
        else os.open("/", os.O_RDONLY | os.O_DIRECTORY)
    )
    try:
        for part in parts:
            if part in {"", ".", ".."}:
                _stop("invalid sealed TRAIN_DEV path component")
            child = os.open(part, _DIR_FLAGS, dir_fd=fd)
            os.close(fd)
            fd = child
            if parent_fd is not None:
                _check_stat(os.fstat(fd), directory=True)
        yield fd
    finally:
        os.close(fd)


def _check_stat(info: os.stat_result, *, directory: bool = False) -> None:
    check = stat.S_ISDIR if directory else stat.S_ISREG
    if (
        not check(info.st_mode)
        or info.st_uid != os.geteuid()
        or info.st_mode & 0o777 != (0o500 if directory else 0o400)
    ):
        _stop("invalid sealed TRAIN_DEV permissions, owner or file type")


def _identity(info: os.stat_result) -> tuple[int, int]:
    return info.st_dev, info.st_ino


def _leaf_stat(info: os.stat_result) -> tuple[int, int, int, int]:
    return (*_identity(info), info.st_size, info.st_mtime_ns)


def _read_fd(fd: int, limit: int) -> tuple[bytes, str]:
    data = bytearray()
    hasher = hashlib.sha256()
    while len(data) < limit:
        block = os.read(fd, min(1 << 20, limit - len(data)))
        if not block:
            _stop("sealed TRAIN_DEV file truncated during read")
        hasher.update(block)
        data.extend(block)
    return bytes(data), hasher.hexdigest()


def _attested_leaves(  # noqa: C901
    data: dict[str, Any],
) -> dict[str, dict[str, Any]]:
    rows = data.get("leaves")
    if not isinstance(rows, list):
        _stop("invalid TRAIN_DEV attested leaf inventory")
    leaves = {}
    pages: dict[str, list[int]] = {gid: [] for gid in TRAINDEV_GOLD_IDS}
    expected = {_MANIFEST} | {
        f"gold/{gid}/{kind}.musicxml"
        for gid in TRAINDEV_GOLD_IDS
        for kind in ("accepted", "draft")
    }
    portable = []
    for raw in rows:
        row = _object(raw)
        rel = row.get("relpath")
        if not isinstance(rel, str) or rel in leaves:
            _stop("invalid or duplicate TRAIN_DEV attested path")
        gid = row.get("gold_id")
        if rel not in expected:
            match = _PAGE_NAME.fullmatch(rel)
            if not match or match[1] not in pages or gid != match[1]:
                _stop("TRAIN_DEV leaf outside its exact allowlist")
            pages[match[1]].append(int(match[2]))
        elif rel != _MANIFEST and gid != rel.split("/")[1]:
            _stop("TRAIN_DEV leaf Gold identity mismatch")
        elif rel == _MANIFEST and gid not in {None, "", "TRAIN_DEV"}:
            _stop("unexpected Gold identity on TRAIN_DEV manifest")
        sha256 = _digest(row.get("expected_sha256"))
        for number in ("st_dev", "st_ino", "st_size", "st_mtime_ns"):
            _integer(row.get(number))
        leaves[rel] = row
        portable.append((rel, str(gid or ""), sha256))
    if (
        not expected <= leaves.keys()
        or sum(map(len, pages.values())) != _PAGE_COUNT
        or any(
            not indices or sorted(indices) != list(range(1, len(indices) + 1))
            for indices in pages.values()
        )
    ):
        _stop("incomplete TRAIN_DEV attested leaf inventory")
    manifest_sha = _digest(data.get("traindev_manifest_sha256"))
    portable_payload = {
        "schema": "omr-w6-traindev-portable-v1",
        "leaves": [
            {"gold_id": g, "relpath": rel, "expected_sha256": sha}
            for rel, g, sha in sorted(portable)
        ],
        "traindev_manifest_sha256": manifest_sha,
    }
    if (
        leaves[_MANIFEST]["expected_sha256"] != manifest_sha
        or hashlib.sha256(_json_bytes(portable_payload)).hexdigest()
        != EXPECTED_TRAINDEV_PORTABLE_SHA256
    ):
        _stop("TRAIN_DEV portable attestation digest drift")
    return leaves


class VerifiedTrainDevRoot:
    """A pinned directory descriptor allowing only attested payload reads."""

    def __init__(self, fd: int, leaves: dict[str, dict[str, Any]]) -> None:
        """Take ownership of the authenticated directory descriptor."""
        self._fd = fd
        self._leaves = leaves

    @property
    def page_relpaths(self) -> tuple[str, ...]:
        """Return the attested pages in deterministic piece/page order."""
        return tuple(sorted(p for p in self._leaves if _PAGE_NAME.fullmatch(p)))

    def read_verified_leaf_bytes(self, relpath: str) -> bytes:
        """Return bytes only if file metadata and streamed digest both match."""
        row = self._leaves.get(relpath)
        if row is None or self._fd < 0:
            _stop("TRAIN_DEV leaf is not available in the attested allowlist")
        *parents, name = relpath.split("/")
        try:
            with _walk_directory(parents, self._fd) as parent:
                leaf = os.open(name, _FILE_FLAGS, dir_fd=parent)
                try:
                    info = os.fstat(leaf)
                    _check_stat(info)
                    expected = tuple(
                        row[k]
                        for k in ("st_dev", "st_ino", "st_size", "st_mtime_ns")
                    )
                    if _leaf_stat(info) != expected:
                        _stop("sealed TRAIN_DEV leaf changed before read")
                    data, digest = _read_fd(leaf, info.st_size)
                    if (
                        _leaf_stat(os.fstat(leaf)) != expected
                        or digest != row["expected_sha256"]
                    ):
                        _stop("sealed TRAIN_DEV leaf digest drift")
                    return data
                finally:
                    os.close(leaf)
        except OSError as exc:
            msg = "cannot securely open TRAIN_DEV leaf"
            raise StopConditionError(msg) from exc

    def close(self) -> None:
        """Release the pinned root descriptor."""
        if self._fd >= 0:
            os.close(self._fd)
            self._fd = -1

    def __enter__(self) -> Self:
        """Use the verified root as a scoped read capability."""
        return self

    def __exit__(self, *_exc: object) -> None:
        """Release the descriptor at scope exit."""
        self.close()


def _check_worktrees(
    path: Path,
    root_stat: os.stat_result,
    worktree_roots: Sequence[Path],
) -> None:
    # Include identity checks for aliases of worktrees and their ancestors.
    for worktree in worktree_roots:
        wt = worktree.resolve()
        if path.is_relative_to(wt) or wt.is_relative_to(path):
            _stop("TRAIN_DEV root must be outside all git worktrees")
        for ancestor in (wt, *wt.parents):
            if ancestor.exists() and _identity(ancestor.stat()) == (
                _identity(root_stat)
            ):
                _stop("TRAIN_DEV root aliases a git worktree or its ancestor")
        if wt.is_dir():
            wt_identity = _identity(wt.stat())
            for ancestor in path.parents:
                if _identity(ancestor.stat()) == wt_identity:
                    _stop("TRAIN_DEV root is inside a git worktree")


def open_verified_traindev_root(
    traindev_root: Path,
    anchor: Mapping[str, object],
    worktree_roots: Sequence[Path],
) -> VerifiedTrainDevRoot:
    """Authenticate the staging seal before opening any payload leaf.

    The portable seal is canonical compact JSON
    (``omr-w6-traindev-portable-v1``) containing ``leaves`` objects
    (``gold_id``, ``relpath``, ``expected_sha256``; ``TRAIN_DEV`` or empty on
    the manifest) sorted by ``relpath``, alongside ``traindev_manifest_sha256``.
    The external anchor additionally binds the exact attestation bytes and root
    inode.
    """
    _validate_anchor(anchor)
    path = traindev_root.absolute()
    try:
        with _walk_directory(path.parts[1:]) as fd:
            info = os.fstat(fd)
            _check_stat(info, directory=True)
            root_id = (anchor["root_st_dev"], anchor["root_st_ino"])
            if _identity(info) != root_id:
                _stop("TRAIN_DEV directory does not match external anchor")
            _check_worktrees(path, info, worktree_roots)
            attestation = os.open(
                "staging-attestation.json", _FILE_FLAGS, dir_fd=fd
            )
            try:
                att_stat = os.fstat(attestation)
                _check_stat(att_stat)
                data, digest = _read_fd(attestation, att_stat.st_size)
            finally:
                os.close(attestation)
            if digest != anchor["staging_attestation_sha256"]:
                _stop("TRAIN_DEV staging attestation digest drift")
            obj = _object(json.loads(data))
            if (obj.get("root_st_dev"), obj.get("root_st_ino")) != root_id:
                _stop("TRAIN_DEV staging attestation root identity drift")
            leaves = _attested_leaves(obj)
            return VerifiedTrainDevRoot(os.dup(fd), leaves)
    except (OSError, ValueError) as exc:
        msg = "cannot authenticate TRAIN_DEV root"
        raise StopConditionError(msg) from exc


def scan_public_report(report_text: str, mapping_json_path: Path) -> None:
    """Read only the private identity mapping, not private score payloads."""
    mapping = _read_json(mapping_json_path)
    pieces = mapping.get("pieces")
    if not isinstance(pieces, list) or len(pieces) != _GOLD_COUNT:
        _stop("publication gate requires the full 23-piece identity mapping")
    identities: set[str] = set()
    private = [_private_identity(mapping, key) for key in ("status", "cache")]
    for row in pieces:
        piece = _object(row)
        identities.add(_private_identity(piece, "id"))
        private.extend(
            _private_identity(piece, key)
            for key in ("workspace", "build", "path", "sha256")
        )
    if identities != {gold_id(i) for i in range(1, _GOLD_COUNT + 1)}:
        _stop("publication gate Gold identity set is incomplete")
    privacy.scan(report_text, private)


def _private_identity(record: Mapping[str, object], key: str) -> str:
    value = record.get(key)
    if not isinstance(value, str) or not value.strip():
        _stop(f"publication gate lacks a private identity field: {key}")
    return value
