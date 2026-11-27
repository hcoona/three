"""Durable W6 GPU Slice B ledgers, setup, and single-pass TEST touch gating."""

from __future__ import annotations

import hashlib
import json
import math
import os
import platform
import shutil
import subprocess
import sys
import urllib.error
import urllib.parse
import urllib.request
import uuid
from contextlib import contextmanager
from pathlib import Path
from typing import TYPE_CHECKING, Any, Literal, NoReturn, cast

from sheet_music_omr.evaluation import copista
from sheet_music_omr.evaluation.alignment import (
    check_no_symlink_under,
    venv_python_path,
)
from sheet_music_omr.harness.records import (
    check_outside_repository,
    repository_root,
    run_git,
)
from sheet_music_omr.harness.stops import StopConditionError
from sheet_music_omr.synthetic.ledgers import _exclusive_file_lock, _utc_now
from sheet_music_omr.training.asset_locks import (
    EXPECTED_W6_FORMAL_6_MANIFEST_SHA256S,
    W6_MODEL_METADATA_LOCKS,
    W6_MODEL_WEIGHT_LOCKS,
    compute_w6_metadata_lock_sha256,
    compute_w6_weight_lock_sha256,
    compute_w6_wheel_lock_sha256,
    resolve_host_w6_gpu_wheels,
    resolve_model_weight_lock,
)
from sheet_music_omr.training.ledgers import (
    W6_OFFLINE_ENV,
    _cache_paths,
    _digest,
    _integer,
    _json_bytes,
    _ledger_bytes,
    _meta_assets,
    _object,
    _sync_parent,
    _validate_https,
    _verified_file,
    _w6_run_bytes,
    _wheel_name,
    _write_json,
    check_w6_runs_root,
    open_verified_traindev_root,
    verify_all_preserved_campaign_ledgers,
)

if TYPE_CHECKING:
    from collections.abc import Iterator, Mapping, Sequence

W6_SLICE_B_DOWNLOAD_LEDGER_NAME = "w6-slice-b-download-ledger.json"
W6_SLICE_B_DOWNLOAD_LOCK_NAME = ".w6-slice-b-downloads.lock"
W6_SLICE_B_DOWNLOAD_CAP_BYTES = 60_000_000_000
W6_SLICE_B_RUN_LEDGER_NAME = "w6-slice-b-run-ledger.jsonl"
W6_SLICE_B_RUN_LOCK_NAME = ".w6-slice-b-run-ledger.lock"
W6_SLICE_B_DISK_LOCK_NAME = ".w6-slice-b-disk.lock"
W6_SLICE_B_DISK_CAP_BYTES = 150_000_000_000
W6_SLICE_B_GPU_TRAIN_CAP = 40
W6_SLICE_B_TRAIN_TIMEOUT_SECONDS = 28_800.0
W6_SLICE_B_TRAINDEV_EVAL_CAP = 30
W6_SLICE_B_EVAL_PAGE_TIMEOUT_SECONDS = 300.0
W6_SLICE_B_DIAG_RENDER_PAGE_CAP = 5_000
W6_SLICE_B_DIAG_MEASURES_PER_JOB_CAP = 200
W6_SLICE_B_PILOT_FINAL_CAP = 1
W6_SLICE_B_SETUP_TIMEOUT_SECONDS = 900.0
W6_PILOT_FINAL_PURPOSE = "W6-pilot-final"
W6_PREREGISTRATION_SCHEMA = "omr-w6-pilot-final-preregistration-v1"

type SliceBRunKind = Literal[
    "train_gpu",
    "eval_traindev",
    "render_diag",
    "pilot_final_test",
]
_SLICE_B_KINDS: frozenset[str] = frozenset(
    {"train_gpu", "eval_traindev", "render_diag", "pilot_final_test"}
)
_FORBIDDEN_SLICE_B_SUFFIXES: frozenset[str] = frozenset(
    {".py", ".bin", ".pt", ".pth", ".ckpt", ".onnx"}
)
_DEFAULT_PACKAGED_DIGESTS: dict[str, str] = dict(
    EXPECTED_W6_FORMAL_6_MANIFEST_SHA256S
)
_REQUIRED_E0_REPOS: frozenset[str] = frozenset(
    {
        "Qwen/Qwen3-VL-2B-Instruct",
        "Qwen/Qwen3.5-2B",
        "PaddlePaddle/PaddleOCR-VL-1.6",
    }
)


def _stop(message: str) -> NoReturn:
    raise StopConditionError(message)


def _git_worktree_roots() -> list[Path]:
    root = repository_root()
    if root is None:
        return []
    out: list[Path] = [root]
    raw = run_git(["worktree", "list", "--porcelain"])
    for line in raw.splitlines():
        if line.startswith("worktree "):
            out.append(Path(line.removeprefix("worktree ").strip()))
    return out


def _read_slice_b_download_ledger(raw: bytes) -> dict[str, Any]:
    try:
        data = _object(json.loads(raw))
    except ValueError as exc:
        msg = "invalid Slice B download ledger"
        raise StopConditionError(msg) from exc
    if (
        data.get("schema") != "omr-w6-slice-b-download-ledger-v1"
        or not isinstance(data.get("entries"), list)
        or sum(
            _integer(_object(e).get("charged_bytes")) for e in data["entries"]
        )
        != _integer(data.get("total_bytes"))
    ):
        _stop("invalid Slice B download ledger")
    return data


def _transfer_slice_b_payload(
    url: str, dest: Path, size: int, read_counter: list[int]
) -> None:
    _validate_https(url)
    leaf = _wheel_name(url)
    if any(leaf.lower().endswith(sfx) for sfx in _FORBIDDEN_SLICE_B_SUFFIXES):
        _stop("refusing remote code or non-safetensors weights in Slice B")
    request = urllib.request.Request(  # noqa: S310
        url, headers={"User-Agent": "sheet-music-omr-w6-slice-b/1.0"}
    )
    try:
        with copista._open_https_request(request, timeout=600) as response:  # noqa: SLF001
            final_url = getattr(response, "url", url)
            if isinstance(final_url, str):
                _validate_https(final_url)
            cl_hdr = response.headers.get("Content-Length")
            if cl_hdr is not None and cl_hdr.strip() != str(size):
                _stop("Slice B response Content-Length differs from lock")
            with dest.open("xb") as stream:
                remaining = size
                while remaining:
                    chunk = response.read(min(remaining, 1 << 20))
                    if not chunk:
                        _stop("truncated Slice B payload")
                    read_counter[0] += len(chunk)
                    if read_counter[0] > size:
                        _stop("Slice B payload exceeded locked size")
                    stream.write(chunk)
                    remaining -= len(chunk)
                if response.read(1):
                    read_counter[0] += 1
                    _stop("Slice B payload exceeded locked size")
                stream.flush()
                os.fsync(stream.fileno())
    except (OSError, urllib.error.URLError) as exc:
        msg = f"Slice B HTTPS transfer failed for {leaf}"
        raise StopConditionError(msg) from exc


class W6SliceBDownloadLedger:
    """Reserve exact locked artifact payload bytes before HTTPS GET."""

    def __init__(
        self, downloads_dir: Path, *, allow_init: bool = False
    ) -> None:
        """Bind self-contained Slice B download ledger under DOWNLOADS."""
        self.downloads_dir = downloads_dir.absolute()
        check_no_symlink_under(
            self.downloads_dir, Path(self.downloads_dir.anchor)
        )
        self.path = self.downloads_dir / W6_SLICE_B_DOWNLOAD_LEDGER_NAME
        self.lock_path = self.downloads_dir / W6_SLICE_B_DOWNLOAD_LOCK_NAME
        self._allow_init = allow_init

    def _load_or_init_unlocked(self) -> dict[str, Any]:
        verify_all_preserved_campaign_ledgers(self.downloads_dir)
        if not self.path.exists():
            if not self._allow_init:
                _stop(f"missing Slice B download ledger: {self.path.name}")
            data: dict[str, Any] = {
                "schema": "omr-w6-slice-b-download-ledger-v1",
                "total_bytes": 0,
                "entries": [],
            }
            _write_json(self.path, data)
            return data
        return _read_slice_b_download_ledger(_ledger_bytes(self.path))

    def total_bytes(self) -> int:
        """Return settled payload bytes plus any outstanding reservations."""
        with _exclusive_file_lock(self.lock_path):
            return _integer(self._load_or_init_unlocked()["total_bytes"])

    def ensure_cached_file(
        self, *, url: str, dest: Path, expected_bytes: int, expected_sha256: str
    ) -> Path:
        """Verify cache or reserve exact payload bytes before HTTPS transfer."""
        _validate_https(url)
        name = _wheel_name(url)
        if name != dest.name or any(
            name.lower().endswith(sfx) for sfx in _FORBIDDEN_SLICE_B_SUFFIXES
        ):
            _stop("refusing unlocked remote code or weights in Slice B")
        allowed = name.endswith(
            (".whl", ".safetensors", ".safetensors.index.json")
        ) or any(
            name == mf.filename
            for repo in W6_MODEL_METADATA_LOCKS
            for mf in repo.files
        )
        if not allowed:
            _stop(f"unapproved Slice B artifact filename: {name}")
        _integer(expected_bytes, 1)
        _digest(expected_sha256)
        dest = dest.absolute()
        check_no_symlink_under(dest, self.downloads_dir)
        with _exclusive_file_lock(self.lock_path):
            data = self._load_or_init_unlocked()
            candidates = [dest]
            if name.endswith(".whl"):
                candidates.extend(_cache_paths(self.downloads_dir, name))
            for path in dict.fromkeys(candidates):
                check_no_symlink_under(path, self.downloads_dir)
                if _verified_file(path, expected_bytes, expected_sha256):
                    return path
            if dest.exists():
                _stop(f"cached Slice B asset has drifted: {name}")
            if (
                data["total_bytes"] + expected_bytes + 1
                > W6_SLICE_B_DOWNLOAD_CAP_BYTES
            ):
                _stop("Slice B 60 GB download cap would be exceeded")
            record: dict[str, Any] = {
                "asset": name,
                "started_utc": _utc_now(),
                "reserved_bytes": expected_bytes,
                "charged_bytes": expected_bytes,
                "status": "reserved",
            }
            data["entries"].append(record)
            data["total_bytes"] += expected_bytes
            _write_json(self.path, data)
            try:
                self._receive(
                    url, dest, expected_bytes, expected_sha256, record
                )
            finally:
                data["total_bytes"] = sum(
                    _integer(_object(e).get("charged_bytes"))
                    for e in data["entries"]
                )
                _write_json(self.path, data)
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
        read_counter = [0]
        try:
            _transfer_slice_b_payload(url, tmp, size, read_counter)
            if read_counter[0] != size or not _verified_file(tmp, size, sha256):
                _stop("Slice B payload failed locked size or digest check")
            tmp.replace(dest)
            _sync_parent(dest)
            record.update(
                status="completed",
                charged_bytes=size,
                settled_utc=_utc_now(),
            )
        except BaseException:
            record.update(
                status="failed",
                charged_bytes=read_counter[0],
                settled_utc=_utc_now(),
            )
            raise
        finally:
            tmp.unlink(missing_ok=True)


def _verify_manifest_png_assets(pkg: Path, manifest_rel: str) -> int:
    manifest_path = pkg / manifest_rel
    if not manifest_path.is_file():
        return 0
    datasets_root = pkg / "datasets"
    seen: set[tuple[str, str]] = set()
    with manifest_path.open(encoding="utf-8") as stream:
        for line in stream:
            row = _object(json.loads(line))
            rel = row.get("crop_relpath") or row.get("page_relpath")
            sha = row.get("sha256")
            if not isinstance(rel, str) or not isinstance(sha, str):
                _stop(f"invalid asset entry in {manifest_rel}")
            seen.add((rel, sha))
    for rel, exp_sha in sorted(seen):
        asset = (datasets_root / rel).resolve()
        if (
            not asset.is_relative_to(datasets_root.resolve())
            or not asset.is_file()
        ):
            _stop(f"missing packaged PNG asset: {rel}")
        with asset.open("rb") as fp:
            got = hashlib.file_digest(fp, "sha256").hexdigest()
            if hasattr(os, "posix_fadvise") and hasattr(
                os, "POSIX_FADV_DONTNEED"
            ):
                os.posix_fadvise(fp.fileno(), 0, 0, os.POSIX_FADV_DONTNEED)
        if got != exp_sha:
            _stop(f"packaged PNG asset SHA-256 mismatch: {rel}")
    return len(seen)


def verify_packaged_slice_a_datasets(
    package_run_dir: Path,
    *,
    expected_digests: Mapping[str, str] | None = None,
    verify_png_assets: bool = False,
) -> dict[str, str]:
    """Verify carried-forward w6-slice-a-package-formal-6 dataset digests."""
    pkg = package_run_dir.absolute()
    check_outside_repository(pkg)
    check_no_symlink_under(pkg, Path(pkg.anchor))
    digests = dict(
        _DEFAULT_PACKAGED_DIGESTS
        if expected_digests is None
        else expected_digests
    )
    verified: dict[str, str] = {}
    for rel, exp in sorted(digests.items()):
        target = pkg / rel
        check_no_symlink_under(target, pkg)
        if not target.is_file():
            _stop(f"missing packaged Slice A artifact: {rel}")
        got = hashlib.sha256(target.read_bytes()).hexdigest()
        if got != exp:
            _stop(f"packaged Slice A artifact SHA-256 mismatch: {rel}")
        verified[rel] = got
    if verify_png_assets:
        for rel in sorted(digests):
            if rel.endswith(("/crops.jsonl", "/page-boxes.jsonl")):
                _verify_manifest_png_assets(pkg, rel)
    return verified


def _weight_assets(
    downloads: Path, model_repos: Sequence[str] | None
) -> Iterator[tuple[str, str, str, Path, int, str]]:
    selected = (
        W6_MODEL_WEIGHT_LOCKS
        if model_repos is None
        else tuple(resolve_model_weight_lock(r) for r in model_repos)
    )
    for repo in selected:
        for wf in repo.files:
            path = downloads / "w6-model-weights" / repo.slug / wf.filename
            url = (
                f"https://huggingface.co/{repo.repo_id}"
                f"/resolve/{repo.commit_sha}/{wf.filename}"
            )
            yield repo.repo_id, repo.slug, url, path, wf.size, wf.sha256


def _stage_metadata_into_weight_dirs(
    downloads: Path, slugs: Sequence[str]
) -> None:
    meta_by_slug = {r.slug: r for r in W6_MODEL_METADATA_LOCKS}
    for slug in dict.fromkeys(slugs):
        repo = meta_by_slug[slug]
        src_dir = downloads / "w6-model-meta" / slug
        dst_dir = downloads / "w6-model-weights" / slug
        dst_dir.mkdir(parents=True, exist_ok=True)
        for mf in repo.files:
            src = src_dir / mf.filename
            dst = dst_dir / mf.filename
            check_no_symlink_under(src, downloads)
            check_no_symlink_under(dst, downloads)
            if not _verified_file(src, mf.size, mf.sha256):
                _stop(f"missing verified metadata file: {slug}/{mf.filename}")
            if _verified_file(dst, mf.size, mf.sha256):
                continue
            if dst.exists():
                _stop(f"drifted staged metadata in weights dir: {mf.filename}")
            try:
                os.link(src, dst)
            except OSError:
                shutil.copy2(src, dst)


def _gpu_venv_probe(
    python: Path,
    expected: Mapping[str, str],
    downloads: Path,
    *,
    require_cuda: bool,
    require_rtx4090_wsl2: bool = False,
) -> dict[str, object]:
    if require_rtx4090_wsl2 and "wsl2" not in platform.release().lower():
        _stop("formal Slice B execution requires local Windows/WSL2 host")
    probe = (
        "import importlib.metadata as m,json,sys,torch;"
        "cuda_ok=bool(torch.cuda.is_available());"
        "info={'python':sys.version.split()[0],"
        "'packages':{p:m.version(p) for p in sys.argv[1:]},"
        "'cuda_available':cuda_ok,"
        "'torch_cuda_version':torch.version.cuda,"
        "'device_name':torch.cuda.get_device_name(0) if cuda_ok else None,"
        "'vram_bytes':torch.cuda.get_device_properties(0).total_memory"
        " if cuda_ok else 0};"
        "print(json.dumps(info))"
    )
    try:
        raw = subprocess.check_output(  # noqa: S603
            [str(python), "-I", "-B", "-c", probe, *expected],
            timeout=60,
            cwd=downloads,
            env=os.environ | W6_OFFLINE_ENV,
        )
        info = _object(json.loads(raw))
    except (OSError, subprocess.SubprocessError, ValueError) as exc:
        msg = "cannot verify Slice B GPU venv versions"
        raise StopConditionError(msg) from exc
    if info.get("packages") != dict(expected):
        _stop("installed Slice B GPU package versions differ from lock")
    if require_cuda and info.get("cuda_available") is not True:
        _stop("Slice B GPU venv reports torch.cuda.is_available() == False")
    dev_name = str(info.get("device_name") or "")
    if require_rtx4090_wsl2 and "RTX 4090" not in dev_name:
        _stop(f"formal Slice B requires local RTX 4090 GPU, got {dev_name!r}")
    return info | {"wsl_kernel": platform.release()}


def verify_w6_slice_b_gpu_assets(  # noqa: C901
    downloads_dir: Path,
    *,
    model_repos: Sequence[str] | None = None,
    require_cuda: bool = True,
    require_rtx4090_wsl2: bool = False,
) -> dict[str, object]:
    """Verify locked CUDA wheels, metadata, safetensors weights and GPU venv."""
    downloads = downloads_dir.absolute()
    check_no_symlink_under(downloads, Path(downloads.anchor))
    verify_all_preserved_campaign_ledgers(downloads)
    wheels = resolve_host_w6_gpu_wheels()
    for wheel in wheels:
        for path in _cache_paths(downloads, _wheel_name(wheel.url)):
            check_no_symlink_under(path, downloads)
            if _verified_file(path, wheel.size, wheel.sha256):
                break
        else:
            _stop(f"Slice B wheel drift or missing wheel: {wheel.name}")
    for _, path, size, sha256 in _meta_assets(downloads):
        check_no_symlink_under(path, downloads)
        if not _verified_file(path, size, sha256):
            _stop("Slice B metadata drift; mutable-source recheck required")
    verified_weights: dict[str, list[str]] = {}
    slugs: list[str] = []
    for repo_id, slug, _, path, size, sha256 in _weight_assets(
        downloads, model_repos
    ):
        check_no_symlink_under(path, downloads)
        if not _verified_file(path, size, sha256):
            _stop(f"Slice B safetensors drift or missing checkpoint: {repo_id}")
        verified_weights.setdefault(repo_id, []).append(path.name)
        slugs.append(slug)
    meta_by_slug = {r.slug: r for r in W6_MODEL_METADATA_LOCKS}
    for slug in dict.fromkeys(slugs):
        wdir = downloads / "w6-model-weights" / slug
        for mf in meta_by_slug[slug].files:
            if not _verified_file(wdir / mf.filename, mf.size, mf.sha256):
                _stop(f"missing staged metadata in {slug}: {mf.filename}")
    venv = downloads / "w6-gpu-venv"
    check_no_symlink_under(venv / "pyvenv.cfg", downloads)
    if not (venv / "pyvenv.cfg").is_file():
        _stop("missing locked W6 GPU venv")
    return {
        "gpu_wheel_lock_sha256": compute_w6_wheel_lock_sha256(wheels),
        "metadata_lock_sha256": compute_w6_metadata_lock_sha256(),
        "weight_lock_sha256": compute_w6_weight_lock_sha256(),
        "verified_weight_repos": verified_weights,
        "gpu_venv": _gpu_venv_probe(
            venv_python_path(venv),
            {wheel.name: wheel.version for wheel in wheels},
            downloads,
            require_cuda=require_cuda,
            require_rtx4090_wsl2=require_rtx4090_wsl2,
        ),
    }


def _require_local_wsl2_rtx4090_host() -> None:
    if "microsoft" not in platform.release().lower():
        _stop("formal setup-w6-slice-b requires a local WSL2 workstation")
    smi_bin = shutil.which("nvidia-smi")
    if smi_bin is None:
        _stop("formal setup-w6-slice-b requires NVIDIA RTX 4090 via nvidia-smi")
    try:
        smi = subprocess.run(  # noqa: S603
            [smi_bin, "-L"],
            capture_output=True,
            text=True,
            timeout=15,
            check=False,
        )
    except OSError as exc:
        msg = "formal setup-w6-slice-b requires NVIDIA RTX 4090 via nvidia-smi"
        raise StopConditionError(msg) from exc
    if smi.returncode != 0 or "RTX 4090" not in smi.stdout:
        _stop("formal setup-w6-slice-b requires a local NVIDIA RTX 4090 GPU")


def setup_w6_slice_b(  # noqa: PLR0913
    downloads_dir: Path,
    *,
    package_run_dir: Path | None = None,
    traindev_root: Path | None = None,
    model_repos: Sequence[str] | None = None,
    require_cuda: bool = True,
    formal: bool = True,
    allow_init: bool = True,
) -> dict[str, object]:
    """Verify carried-forward state, fetch locked GPU assets and build venv."""
    downloads = downloads_dir.absolute()
    check_outside_repository(downloads)
    if formal and require_cuda:
        _require_local_wsl2_rtx4090_host()
    if formal and (package_run_dir is None or traindev_root is None):
        _stop(
            "formal setup-w6-slice-b requires --package-run-dir and"
            " --traindev-root"
        )
    ledger = W6SliceBDownloadLedger(downloads, allow_init=allow_init)
    ledger.total_bytes()
    packaged_info = (
        None
        if package_run_dir is None
        else verify_packaged_slice_a_datasets(
            package_run_dir, verify_png_assets=formal
        )
    )
    traindev_pages = 0
    if traindev_root is not None:
        with open_verified_traindev_root(
            traindev_root, worktree_roots=_git_worktree_roots()
        ) as td:
            traindev_pages = len(td.page_relpaths)
    wheels = resolve_host_w6_gpu_wheels()
    venv = downloads / "w6-gpu-venv"
    check_no_symlink_under(venv, downloads)
    with _exclusive_file_lock(downloads / ".w6-gpu-setup.lock"):
        wheel_paths = [
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
        slugs: list[str] = []
        for _, slug, url, path, size, sha256 in _weight_assets(
            downloads, model_repos
        ):
            ledger.ensure_cached_file(
                url=url, dest=path, expected_bytes=size, expected_sha256=sha256
            )
            slugs.append(slug)
        _stage_metadata_into_weight_dirs(downloads, slugs)
        env = os.environ | W6_OFFLINE_ENV | {"TMPDIR": str(downloads)}
        uv_bin = shutil.which("uv")
        create = [sys.executable, "-I", "-m", "venv", str(venv)]
        if uv_bin is not None:
            install = [
                uv_bin,
                "pip",
                "install",
                "--python",
                str(venv_python_path(venv)),
                "--offline",
                "--no-index",
                "--no-deps",
                "--reinstall",
                *map(str, wheel_paths),
            ]
        else:
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
                *map(str, wheel_paths),
            ]
        for cmd in (create, install):
            try:
                subprocess.run(  # noqa: S603
                    cmd,
                    env=env,
                    cwd=downloads,
                    check=True,
                    timeout=W6_SLICE_B_SETUP_TIMEOUT_SECONDS,
                )
            except (OSError, subprocess.SubprocessError) as exc:
                msg = "offline Slice B GPU venv provisioning failed"
                raise StopConditionError(msg) from exc
        info = verify_w6_slice_b_gpu_assets(
            downloads,
            model_repos=model_repos,
            require_cuda=require_cuda,
            require_rtx4090_wsl2=formal and require_cuda,
        )
        return info | {
            "slice_b_download_bytes": ledger.total_bytes(),
            "packaged_datasets": packaged_info,
            "verified_traindev_pages": traindev_pages or None,
        }


def slice_b_disk_bytes(runs: Path) -> int:
    """Measure unique-inode bytes across all RUNS/w6-slice-b-* directories."""
    total = 0
    seen_inodes: set[tuple[int, int]] = set()
    if not runs.exists():
        return 0
    for directory in sorted(runs.glob("w6-slice-b-*")):
        if directory.is_symlink() or not directory.is_dir():
            _stop("invalid Slice B run directory")
        total += _w6_run_bytes(directory, seen_inodes)
    return total


@contextmanager
def w6_slice_b_disk_lock(
    downloads_dir: Path, runs_parent: Path | None = None
) -> Iterator[Path]:
    """Serialize Slice B soft-cap checks and writes under DOWNLOADS."""
    downloads = downloads_dir.absolute()
    runs = check_w6_runs_root(downloads, runs_parent)
    verify_all_preserved_campaign_ledgers(downloads)
    with _exclusive_file_lock(downloads / W6_SLICE_B_DISK_LOCK_NAME):
        yield runs


def _slice_b_records(raw: bytes) -> Iterator[dict[str, Any]]:
    if raw and not raw.endswith(b"\n"):
        _stop("incomplete Slice B run ledger record")
    try:
        for line in raw.splitlines():
            if not line.strip():
                _stop("blank line forbidden in Slice B run ledger")
            yield _object(json.loads(line))
    except ValueError as exc:
        msg = "invalid Slice B run ledger"
        raise StopConditionError(msg) from exc


def _slice_b_state(
    raw: bytes,
) -> tuple[dict[str, dict[str, Any]], dict[str, dict[str, Any]]]:
    started: dict[str, dict[str, Any]] = {}
    settled: dict[str, dict[str, Any]] = {}
    seen_prereg_runs: set[str] = set()
    for rec in _slice_b_records(raw):
        event = rec.get("event")
        key = rec.get("reservation_id")
        if not isinstance(key, str) or not key:
            _stop("invalid Slice B reservation ID")
        if event == "started" and key not in started:
            if rec.get("kind") not in _SLICE_B_KINDS:
                _stop("invalid Slice B reservation kind")
            started[key] = rec
        elif event == "settled" and key in started and key not in settled:
            settled[key] = rec
        elif event == "preregistered":
            rid = rec.get("run_id")
            _ = _digest(rec.get("preregistration_sha256"))
            if not isinstance(rid, str) or not rid or rid in seen_prereg_runs:
                _stop("duplicate or invalid Slice B preregistered event")
            seen_prereg_runs.add(rid)
        else:
            _stop("invalid Slice B run ledger lifecycle")
    return started, settled


def _slice_b_preregistrations(raw: bytes) -> dict[str, str]:
    out: dict[str, str] = {}
    for rec in _slice_b_records(raw):
        if rec.get("event") == "preregistered":
            rid = str(rec["run_id"])
            out[rid] = _digest(rec["preregistration_sha256"])
    return out


def _append_slice_b_record(downloads: Path, record: dict[str, object]) -> None:
    path = downloads / W6_SLICE_B_RUN_LEDGER_NAME
    line = _json_bytes(record | {"utc": _utc_now()}) + b"\n"
    with path.open("ab") as stream:
        stream.write(line)
        stream.flush()
        os.fsync(stream.fileno())
    _sync_parent(path)


@contextmanager
def _locked_slice_b_run_ledger(
    downloads: Path, *, allow_create: bool = True
) -> Iterator[bytes]:
    verify_all_preserved_campaign_ledgers(downloads)
    dl_path = downloads / W6_SLICE_B_DOWNLOAD_LEDGER_NAME
    if not dl_path.is_file():
        _stop(f"missing Slice B download ledger: {dl_path.name}")
    _read_slice_b_download_ledger(_ledger_bytes(dl_path))
    with _exclusive_file_lock(downloads / W6_SLICE_B_RUN_LOCK_NAME):
        yield _ledger_bytes(
            downloads / W6_SLICE_B_RUN_LEDGER_NAME, optional=allow_create
        )


def _check_slice_b_budgets_before_start(
    starts: Mapping[str, Mapping[str, Any]],
    settlements: Mapping[str, Mapping[str, Any]],
    *,
    kind: SliceBRunKind,
    disk_bytes: int,
) -> None:
    if any(k not in settlements for k in starts):
        _stop("unsettled Slice B campaign run; settle before next dispatch")
    if disk_bytes >= W6_SLICE_B_DISK_CAP_BYTES:
        _stop("Slice B 150 GB generated disk soft cap exhausted")
    counts = {
        k: sum(row["kind"] == k for row in starts.values())
        for k in _SLICE_B_KINDS
    }
    if kind == "train_gpu" and counts["train_gpu"] >= W6_SLICE_B_GPU_TRAIN_CAP:
        _stop("Slice B 40-run GPU training start cap would be exceeded")
    if (
        kind == "eval_traindev"
        and counts["eval_traindev"] >= W6_SLICE_B_TRAINDEV_EVAL_CAP
    ):
        _stop("Slice B 30-run TRAIN_DEV evaluation start cap would be exceeded")
    if kind == "render_diag":
        diag_pages = sum(
            _integer(settlements[k]["charged_pages"], 1)
            for k, row in starts.items()
            if row["kind"] == "render_diag"
        )
        if diag_pages >= W6_SLICE_B_DIAG_RENDER_PAGE_CAP:
            _stop("Slice B 5,000-page diagnostic render soft cap exhausted")
    if kind == "pilot_final_test" and counts["pilot_final_test"] >= (
        W6_SLICE_B_PILOT_FINAL_CAP
    ):
        _stop("Slice B single-pass W6-pilot-final TEST cap already spent")


def reserve_w6_slice_b_run(  # noqa: PLR0913
    downloads_dir: Path,
    *,
    run_id: str,
    git_commit: str,
    clean_tree: bool,
    command: str,
    kind: Literal["train_gpu", "eval_traindev", "render_diag"],
    runs_parent: Path | None = None,
) -> str:
    """Reserve one non-TEST Slice B campaign run before launch."""
    if kind not in {"train_gpu", "eval_traindev", "render_diag"}:
        _stop(f"use reserve_w6_pilot_final_test_pass for TEST: {kind!r}")
    downloads = downloads_dir.absolute()
    with (
        w6_slice_b_disk_lock(downloads, runs_parent) as runs,
        _locked_slice_b_run_ledger(downloads, allow_create=True) as raw,
    ):
        disk_before = slice_b_disk_bytes(runs)
        starts, settlements = _slice_b_state(raw)
        _check_slice_b_budgets_before_start(
            starts, settlements, kind=kind, disk_bytes=disk_before
        )
        token = uuid.uuid4().hex
        _append_slice_b_record(
            downloads,
            {
                "event": "started",
                "status": "started",
                "reservation_id": token,
                "kind": kind,
                "run_id": run_id,
                "git_commit": git_commit,
                "clean_tree": clean_tree,
                "command": command,
                "disk_bytes_before": disk_before,
            },
        )
        return token


def compute_artifact_sha256s(target: Path) -> dict[str, str]:
    """Hash a single file or all regular files in a checkpoint directory."""
    resolved = target.absolute()
    check_outside_repository(resolved)
    check_no_symlink_under(resolved, Path(resolved.anchor))
    if resolved.is_file():
        return {
            resolved.name: hashlib.sha256(resolved.read_bytes()).hexdigest()
        }
    if not resolved.is_dir():
        _stop(f"missing frozen pre-registration artifact: {resolved.name}")
    digests: dict[str, str] = {}
    for dirpath, dirnames, filenames in os.walk(resolved, followlinks=False):
        dp = Path(dirpath)
        for dn in dirnames:
            if (dp / dn).is_symlink():
                _stop("symlink forbidden inside frozen checkpoint directory")
        for fn in sorted(filenames):
            fp = dp / fn
            if fp.is_symlink() or not fp.is_file():
                _stop("non-regular file inside frozen checkpoint directory")
            rel = fp.relative_to(resolved).as_posix()
            with fp.open("rb") as stream:
                digests[rel] = hashlib.file_digest(stream, "sha256").hexdigest()
    if not digests:
        _stop("empty frozen checkpoint directory")
    return digests


_EXPECTED_REPLAY_BASE_SUBRUNS: frozenset[str] = frozenset(
    {
        *(f"train_synthetic_continuation_f{f}" for f in range(4)),
        *(f"train_real_plus_synth_replay_f{f}" for f in range(4)),
        *(f"train_inner_{a}_{b}" for a in range(4) for b in range(a + 1, 4)),
        *(f"eval_cont_f{f}" for f in range(4)),
        *(f"eval_repl_f{f}" for f in range(4)),
        *(f"eval_inner_{a}_{b}" for a in range(4) for b in range(a + 1, 4)),
        "eval_synth_finalist",
    }
)


def record_w6_pilot_final_preregistration(
    downloads_dir: Path,
    *,
    replay_run_id: str,
    preregistration_sha256: str,
) -> None:
    """Anchor the frozen W6-pilot-final manifest digest write-once in ledger."""
    if not replay_run_id.strip():
        _stop("replay_run_id must be non-empty")
    sha = _digest(preregistration_sha256)
    downloads = downloads_dir.absolute()
    with _locked_slice_b_run_ledger(downloads, allow_create=False) as raw:
        starts, settlements = _slice_b_state(raw)
        if any(k not in settlements for k in starts):
            _stop("unsettled Slice B run before recording pre-registration")
        existing = _slice_b_preregistrations(raw)
        if replay_run_id in existing:
            _stop(f"pre-registration already anchored for {replay_run_id!r}")
        _append_slice_b_record(
            downloads,
            {
                "event": "preregistered",
                "reservation_id": uuid.uuid4().hex,
                "run_id": replay_run_id,
                "preregistration_sha256": sha,
            },
        )


def _read_and_validate_calibrator(
    cal_path: Path, expected_sha256: str
) -> dict[str, Any]:
    resolved = cal_path.absolute()
    check_outside_repository(resolved)
    check_no_symlink_under(resolved, Path(resolved.anchor))
    if not resolved.is_file():
        _stop("production calibrator missing or drifted from freeze")
    cal_bytes = resolved.read_bytes()
    if hashlib.sha256(cal_bytes).hexdigest() != expected_sha256:
        _stop("production calibrator missing or drifted from freeze")
    try:
        cal = _object(json.loads(cal_bytes))
    except ValueError as exc:
        msg = "invalid JSON in frozen production calibrator"
        raise StopConditionError(msg) from exc
    method = cal.get("method")
    status = cal.get("status")
    err_n = _integer(cal.get("error_instances"), 0)
    fit_n = _integer(cal.get("fit_instances"), 0)
    intercept = cal.get("intercept", 0.0)
    slope = cal.get("slope")
    if (
        method not in {"platt", "temperature"}
        or status not in {"fit", "insufficient_support"}
        or err_n > fit_n
        or not isinstance(intercept, (int, float))
        or isinstance(intercept, bool)
        or not math.isfinite(float(intercept))
    ):
        _stop("malformed frozen production calibrator payload")
    if status == "fit" and (
        not isinstance(slope, (int, float))
        or isinstance(slope, bool)
        or not math.isfinite(float(slope))
    ):
        _stop("fitted production calibrator missing finite slope")
    return cal


def _verify_completed_prerequisites(  # noqa: C901
    starts: Mapping[str, Mapping[str, Any]],
    settlements: Mapping[str, Mapping[str, Any]],
    preregistrations: Mapping[str, str],
    doc: Mapping[str, Any],
    prereg_sha: str,
) -> None:
    completed_by_kind: dict[str, set[str]] = {k: set() for k in _SLICE_B_KINDS}
    non_completed_runs: set[str] = set()
    for res_id, row in starts.items():
        rid = str(row.get("run_id"))
        kind = str(row.get("kind"))
        if settlements.get(res_id, {}).get("status") == "completed":
            completed_by_kind[kind].add(rid)
        else:
            non_completed_runs.add(rid)
    e0_ids = [str(x) for x in cast("list[Any]", doc["e0_run_ids"])]
    if len(set(e0_ids)) < len(_REQUIRED_E0_REPOS):
        _stop("W6-pilot-final requires all 3 distinct completed E0 VLM runs")
    for req_eval in (*e0_ids, str(doc["synthetic_selection_run_id"])):
        if (
            req_eval not in completed_by_kind["eval_traindev"]
            or req_eval in non_completed_runs
        ):
            _stop(
                f"prerequisite eval {req_eval!r} lacks completed Slice B record"
            )
    rep_id = str(doc["replay_ablation_run_id"])
    if any(
        r == rep_id or r.startswith(f"{rep_id}:") for r in non_completed_runs
    ):
        _stop(f"replay run {rep_id!r} has failed or unsettled subruns")
    expected_subs = set(_EXPECTED_REPLAY_BASE_SUBRUNS)
    if doc["selected_regime"] == "real_plus_synth_replay":
        expected_subs.add("train_production_replay")
    if doc["selected_detector_mode"] != "comb_dp_blind":
        expected_subs.add("eval_detector_blind")
    all_completed = (
        completed_by_kind["train_gpu"] | completed_by_kind["eval_traindev"]
    )
    missing_subs = sorted(
        sub for sub in expected_subs if f"{rep_id}:{sub}" not in all_completed
    )
    if missing_subs:
        _stop(
            f"replay run {rep_id!r} is incomplete; missing {missing_subs[0]!r}"
        )
    anchored_sha = preregistrations.get(rep_id)
    if anchored_sha != prereg_sha:
        _stop(
            "pre-registration SHA-256 does not match ledger anchor at replay"
            " completion"
        )


def _verify_prerequisite_reports(  # noqa: C901
    runs: Path, doc: Mapping[str, Any], prereg_sha: str
) -> None:
    e0_ids = doc.get("e0_run_ids")
    if not isinstance(e0_ids, list) or len(e0_ids) < len(_REQUIRED_E0_REPOS):
        _stop("W6-pilot-final requires all 3 completed E0 VLM runs")
    seen_e0_slugs: set[str] = set()
    for eid in e0_ids:
        rep_p = runs / str(eid) / "report" / "traindev-eval.json"
        if not rep_p.is_file():
            _stop(f"missing E0 report for {eid!r}")
        rep = _object(json.loads(rep_p.read_bytes()))
        if rep.get("evidence_role") != "zero_shot_baseline":
            _stop(f"run {eid!r} is not an E0 zero_shot_baseline report")
        seen_e0_slugs.add(str(rep.get("reader_id", "")))
    for req_repo in _REQUIRED_E0_REPOS:
        slug_pfx = f"e0-{req_repo.replace('/', '--')}@"
        if not any(s.startswith(slug_pfx) for s in seen_e0_slugs):
            _stop(f"missing completed E0 baseline for {req_repo}")
    sel_id = str(doc["synthetic_selection_run_id"])
    sel_p = runs / sel_id / "report" / "traindev-eval.json"
    if not sel_p.is_file():
        _stop(f"missing synthetic selection report for {sel_id!r}")
    sel_rep = _object(json.loads(sel_p.read_bytes()))
    if sel_rep.get("evidence_role") != "cross_validated_selection":
        _stop(f"run {sel_id!r} is not a cross_validated_selection report")
    rep_id = str(doc["replay_ablation_run_id"])
    ablation_path = runs / rep_id / "report" / "replay-ablation.json"
    if not ablation_path.is_file():
        _stop(f"missing completed replay-ablation.json for {rep_id!r}")
    ablation_rep = _object(json.loads(ablation_path.read_bytes()))
    if ablation_rep.get("schema") != "omr-w6-slice-b-replay-ablation-v1":
        _stop(f"invalid schema in replay-ablation.json for {rep_id!r}")
    frozen_copy = runs / rep_id / "report" / "pilot-final-preregistration.json"
    if (
        not frozen_copy.is_file()
        or hashlib.sha256(frozen_copy.read_bytes()).hexdigest() != prereg_sha
    ):
        _stop("pre-registration manifest does not match frozen replay output")


def verify_pilot_final_preregistration(  # noqa: C901, PLR0913
    path: Path,
    *,
    downloads_dir: Path | None = None,
    runs_dir: Path | None = None,
    starts: Mapping[str, Mapping[str, Any]] | None = None,
    settlements: Mapping[str, Mapping[str, Any]] | None = None,
    preregistrations: Mapping[str, str] | None = None,
) -> tuple[dict[str, Any], str]:
    """Verify frozen pre-registration, artifact hashes and prerequisites."""
    target = path.absolute()
    check_outside_repository(target)
    check_no_symlink_under(target, Path(target.anchor))
    if not target.is_file():
        _stop("missing W6-pilot-final pre-registration manifest")
    raw = target.read_bytes()
    sha = hashlib.sha256(raw).hexdigest()
    try:
        doc = _object(json.loads(raw))
    except ValueError as exc:
        msg = "invalid JSON in W6-pilot-final pre-registration manifest"
        raise StopConditionError(msg) from exc
    e0_ids = doc.get("e0_run_ids")
    if (
        doc.get("schema") != W6_PREREGISTRATION_SCHEMA
        or doc.get("purpose") != W6_PILOT_FINAL_PURPOSE
        or doc.get("test_touches_before_preregistration") != 0
        or doc.get("arm") not in {"arm-a", "arm-b"}
        or doc.get("selected_regime")
        not in {"synthetic_only", "real_plus_synth_replay"}
        or not isinstance(e0_ids, list)
        or len(e0_ids) < len(_REQUIRED_E0_REPOS)
        or any(not isinstance(x, str) or not x.strip() for x in e0_ids)
        or _integer(doc.get("max_new_tokens"), 1) > 1536  # noqa: PLR2004
        or _integer(doc.get("num_beams"), 1) > 3  # noqa: PLR2004
    ):
        _stop("incomplete or invalid W6-pilot-final pre-registration manifest")
    for req_k in (
        "synthetic_selection_run_id",
        "replay_ablation_run_id",
        "selected_recognizer_checkpoint",
        "selected_detector_mode",
        "production_calibrator_path",
    ):
        if not isinstance(doc.get(req_k), str) or not str(doc[req_k]).strip():
            _stop(f"missing {req_k} in W6-pilot-final pre-registration")
    if doc["arm"] == "arm-a":
        resolve_model_weight_lock(str(doc.get("model_repo") or ""))
    rec_ckpt = Path(str(doc["selected_recognizer_checkpoint"]))
    exp_rec_shas = _object(doc.get("recognizer_checkpoint_sha256s"))
    if not exp_rec_shas or compute_artifact_sha256s(rec_ckpt) != exp_rec_shas:
        _stop("selected recognizer checkpoint missing or drifted from freeze")
    cal_p = Path(str(doc["production_calibrator_path"]))
    exp_cal_sha = _digest(doc.get("production_calibrator_sha256"))
    verified_cal = _read_and_validate_calibrator(cal_p, exp_cal_sha)
    doc = dict(doc) | {"_verified_calibrator": verified_cal}
    det_mode = str(doc["selected_detector_mode"])
    if det_mode != "comb_dp_blind":
        det_raw = doc.get("selected_detector_checkpoint")
        exp_det_shas = _object(doc.get("detector_checkpoint_sha256s"))
        if (
            not isinstance(det_raw, str)
            or not det_raw.strip()
            or not exp_det_shas
            or compute_artifact_sha256s(Path(det_raw)) != exp_det_shas
        ):
            _stop("selected detector checkpoint missing or drifted from freeze")
    if (
        starts is None
        and settlements is None
        and downloads_dir is not None
        and (downloads_dir / W6_SLICE_B_RUN_LEDGER_NAME).is_file()
    ):
        ledger_raw = (downloads_dir / W6_SLICE_B_RUN_LEDGER_NAME).read_bytes()
        starts, settlements = _slice_b_state(ledger_raw)
        preregistrations = _slice_b_preregistrations(ledger_raw)
    if (
        starts is not None
        and settlements is not None
        and preregistrations is not None
    ):
        _verify_completed_prerequisites(
            starts, settlements, preregistrations, doc, sha
        )
    if runs_dir is not None:
        _verify_prerequisite_reports(runs_dir, doc, sha)
    return doc, sha


def reserve_w6_pilot_final_test_pass(  # noqa: PLR0913
    downloads_dir: Path,
    *,
    run_id: str,
    git_commit: str,
    clean_tree: bool,
    command: str,
    preregistration_path: Path,
    tiny_fixture: bool = False,
    runs_parent: Path | None = None,
) -> tuple[str, dict[str, Any], str, dict[str, object]]:
    """Atomically reserve and fsync the single W6-pilot-final TEST pass."""
    if not run_id.strip():
        _stop("W6-pilot-final requires a non-empty run_id")
    if not tiny_fixture and not clean_tree:
        _stop("W6-pilot-final TEST pass requires a clean git tree")
    downloads = downloads_dir.absolute()
    # Pre-validate manifest, calibrator, and artifact SHA-256s BEFORE locks
    prelim, _ = verify_pilot_final_preregistration(preregistration_path)
    if tiny_fixture:
        env_info: dict[str, object] = {"tiny_fixture": True}
    else:
        model_repo = prelim.get("model_repo")
        env_info = verify_w6_slice_b_gpu_assets(
            downloads,
            model_repos=[str(model_repo)] if model_repo else None,
            require_cuda=True,
            require_rtx4090_wsl2=True,
        )
    touch_path = downloads / "w2-touch-log.jsonl"
    with (
        w6_slice_b_disk_lock(downloads, runs_parent) as runs,
        _locked_slice_b_run_ledger(downloads, allow_create=True) as raw,
        _exclusive_file_lock(downloads / ".ledger.lock"),
    ):
        disk_before = slice_b_disk_bytes(runs)
        starts, settlements = _slice_b_state(raw)
        preregistrations = _slice_b_preregistrations(raw)
        prereg, prereg_sha = verify_pilot_final_preregistration(
            preregistration_path,
            runs_dir=runs,
            starts=starts,
            settlements=settlements,
            preregistrations=preregistrations,
        )
        _check_slice_b_budgets_before_start(
            starts,
            settlements,
            kind="pilot_final_test",
            disk_bytes=disk_before,
        )
        touch_raw = _ledger_bytes(touch_path)
        pilot_touches = copista.verify_w2_touch_log_bytes(
            touch_raw,
            slice_b_run_ledger_path=downloads / W6_SLICE_B_RUN_LEDGER_NAME,
        )
        if pilot_touches != 0:
            _stop("w2-touch-log.jsonl already records a W6-pilot-final touch")
        token = uuid.uuid4().hex
        _append_slice_b_record(
            downloads,
            {
                "event": "started",
                "status": "started",
                "reservation_id": token,
                "kind": "pilot_final_test",
                "purpose": W6_PILOT_FINAL_PURPOSE,
                "run_id": run_id,
                "git_commit": git_commit,
                "clean_tree": clean_tree,
                "command": command,
                "preregistration_sha256": prereg_sha,
                "disk_bytes_before": disk_before,
            },
        )
        touch_record = {
            "utc": _utc_now(),
            "run_id": run_id,
            "split": "TEST",
            "purpose": W6_PILOT_FINAL_PURPOSE,
            "pieces": 7,
            "pages": 23,
            "systems": 111,
            "measures": 432,
            "pilot_touch_index": 1,
            "pilot_touch_cap": 3,
            "git_commit": git_commit,
            "preregistration_sha256": prereg_sha,
        }
        with touch_path.open("ab") as stream:
            stream.write(_json_bytes(touch_record) + b"\n")
            stream.flush()
            os.fsync(stream.fileno())
        _sync_parent(touch_path)
        verified_touches = copista.verify_w2_touch_log_bytes(
            _ledger_bytes(touch_path),
            slice_b_run_ledger_path=downloads / W6_SLICE_B_RUN_LEDGER_NAME,
        )
        if verified_touches != 1:
            _stop("failed to verify fsynced W6-pilot-final touch record")
    return token, prereg, prereg_sha, env_info


def settle_w6_slice_b_run(
    downloads_dir: Path,
    reservation_id: str,
    *,
    status: Literal["completed", "failed", "aborted"],
    actual_pages: int = 0,
    runs_parent: Path | None = None,
) -> dict[str, object]:
    """Settle a Slice B run reservation and record local soft-cap usage."""
    if status not in {"completed", "failed", "aborted"}:
        _stop(f"invalid Slice B settlement status: {status!r}")
    _integer(actual_pages, 0)
    downloads = downloads_dir.absolute()
    with (
        w6_slice_b_disk_lock(downloads, runs_parent) as runs,
        _locked_slice_b_run_ledger(downloads, allow_create=False) as raw,
    ):
        starts, settlements = _slice_b_state(raw)
        if reservation_id not in starts or reservation_id in settlements:
            _stop("unknown or already-settled Slice B reservation")
        row = starts[reservation_id]
        is_diag = row["kind"] == "render_diag"
        charged_pages = max(actual_pages, 1) if is_diag else actual_pages
        disk_after = slice_b_disk_bytes(runs)
        overshoot = max(0, disk_after - W6_SLICE_B_DISK_CAP_BYTES)
        record: dict[str, object] = {
            "event": "settled",
            "reservation_id": reservation_id,
            "status": status,
            "actual_pages": actual_pages,
            "charged_pages": charged_pages,
            "disk_bytes_after": disk_after,
            "disk_soft_cap_overshoot_bytes": overshoot,
        }
        _append_slice_b_record(downloads, record)
        return record
