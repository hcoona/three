"""Isolated out-of-worktree ``copista-28m`` setup, ledger and page inference.

Follows ``docs/evaluation-protocol.md``:
- Reserves and tracks network bytes in ``DOWNLOADS/w2-download-ledger.json``
  against the ``10,000,000,000``-byte (`10 GB`) cumulative W2/W3 download cap.
- Enforces pinned ``kobimusic/copisteria`` commit
  ``2be4a9d88c15fd6b36fe7c97e0fa816f08804570`` and ``kobimusic/copista-28m``
  revision ``44464d11234a22ba23634ec8eb1e31e3b012021a`` with exact per-file
  byte sizes and LFS ``SHA-256`` digests.
- Runs ``copisteria`` in an isolated ``DOWNLOADS/w2-copista-venv`` worker and
  extracts ``1:1`` column-to-measure posteriors (`raw_conf = min_cls_p`).
"""

from __future__ import annotations

import contextlib
import hashlib
import http.client
import io
import json
import os
import shutil
import subprocess
import sys
import tarfile
import tempfile
import urllib.error
import urllib.parse
import urllib.request
from datetime import UTC, datetime
from pathlib import Path
from typing import TYPE_CHECKING, Any

from sheet_music_omr.evaluation.alignment import (
    atomic_write_text,
    check_no_symlink_under,
    freeze_run_masks,
    venv_python_path,
    verify_approved_overlays,
)
from sheet_music_omr.evaluation.wheel_lock import (
    COPISTA_HF_REVISION,
    COPISTA_WEIGHTS,
    COPISTERIA_ARCHIVE_BYTES,
    COPISTERIA_ARCHIVE_SHA256,
    COPISTERIA_ARCHIVE_URL,
    COPISTERIA_COMMIT,
    compute_wheel_lock_sha256,
    resolve_host_wheel_lock,
)
from sheet_music_omr.evaluation.xml_merge import concat_page_predictions
from sheet_music_omr.harness.records import run_git, source_state
from sheet_music_omr.harness.stops import StopConditionError

if TYPE_CHECKING:
    from collections.abc import Sequence

    from sheet_music_omr.harness.confine import Confinement

W2_DOWNLOAD_CAP_BYTES = 10_000_000_000
S0_TEST_TOUCH_CAP = 6
PAGE_DEADLINE_SECONDS = 300

_WORKER_SCRIPT = """\
from concurrent.futures import ThreadPoolExecutor
import importlib.util
import json
import multiprocessing as mp
import os
import queue
import sys
import time
import traceback
from pathlib import Path

os.environ.setdefault("OMP_NUM_THREADS", "1")
os.environ["COPISTERIA_DETECTOR"] = "28m"
import torch
torch.set_num_threads(1)

from PIL import Image

def _verify_copisteria_origin(expected_src_root: str) -> None:
    if not expected_src_root:
        raise RuntimeError("missing OMR_EXPECTED_COPISTERIA_ROOT")
    spec = importlib.util.find_spec("copisteria")
    if spec is None or spec.origin is None:
        raise RuntimeError("copisteria module spec missing")
    origin = Path(spec.origin).resolve()
    root = Path(expected_src_root).resolve()
    if not origin.is_relative_to(root):
        raise RuntimeError(f"copisteria origin {origin} outside {root}")

_verify_copisteria_origin(os.environ.get("OMR_EXPECTED_COPISTERIA_ROOT", ""))

from copisteria import front, read, write
from copisteria.pipeline import inputs
from copisteria.revise import read_revised

_PAGE_DEADLINE = 300.0

def _column_posteriors(rd) -> list[dict]:
    L = rd.layout
    plans = write._plan_parts(rd)
    if not L.systems or not plans:
        return []
    columns = []
    for s_i, sy in enumerate(L.systems):
        ncol = 1 + max((max(c) for c in sy.columns if c), default=-1)
        for c in range(ncol):
            columns.append((s_i, c))

    # Map (staff_global, bar_idx) -> list of real non-measure symbol tokens
    by_sb = {}
    for si, sym in enumerate(L.syms):
        tok = rd.tok.get(si)
        if tok is None or tok["real"]["p"] < write.REAL:
            continue
        cls_v = tok["cls"]["v"]
        if cls_v.startswith("measure"):
            continue
        by_sb.setdefault((sym.staff, sym.bar), []).append(tok)

    out = []
    for m_idx, (s_i, c) in enumerate(columns, start=1):
        sy = L.systems[s_i]
        toks = []
        for st_pos, st in enumerate(sy.staves):
            cols = sy.columns[st_pos]
            for j, cc in enumerate(cols):
                if cc == c:
                    toks.extend(by_sb.get((st, j), ()))
        if not toks:
            out.append({
                "emitted_measure": m_idx,
                "system_index": s_i + 1,
                "column_index": c + 1,
                "n_symbols": 0,
                "min_real_p": 0.0,
                "mean_real_p": 0.0,
                "min_cls_p": 0.0,
                "mean_cls_p": 0.0,
                "raw_conf": 0.0,
            })
        else:
            real_ps = [float(t["real"]["p"]) for t in toks]
            cls_ps = [float(t["cls"]["p"]) for t in toks]
            min_cp = round(min(cls_ps), 6)
            out.append({
                "emitted_measure": m_idx,
                "system_index": s_i + 1,
                "column_index": c + 1,
                "n_symbols": len(toks),
                "min_real_p": round(min(real_ps), 6),
                "mean_real_p": round(sum(real_ps) / len(real_ps), 6),
                "min_cls_p": min_cp,
                "mean_cls_p": round(sum(cls_ps) / len(cls_ps), 6),
                "raw_conf": min_cp,
            })
    return out

def _run_page_with_model(job: list[str], model, device: str) -> dict:
    (
        png_str,
        xml_str,
        post_str,
        page_sha,
        prov_sha,
        masks_sha,
        commit_sha,
        tree_sha,
        dirty_s,
    ) = job
    png = Path(png_str)
    xml_out = Path(xml_str)
    post_out = Path(post_str)
    xml_out.parent.mkdir(parents=True, exist_ok=True)
    work_png = xml_out.parent / png.name
    work_png.write_bytes(png.read_bytes())
    t0 = time.time()
    try:
        dets, cands, feats = inputs(work_png, model)
        im = Image.open(work_png).convert("L")
        rd = read_revised(
            dets, im.width, im.height, model,
            device=device, attention=False, image=im, cands=cands, feats=feats,
        )
        xml_text = write.write(rd, texts=None, image=im)
        cols = _column_posteriors(rd)
        xml_out.write_text(xml_text, encoding="utf-8")
        post_out.write_text(
            json.dumps({
                "page": png.stem,
                "page_sha256": page_sha,
                "provenance_sha256": prov_sha,
                "open_reading_masks_sha256": masks_sha,
                "producer_git_commit": commit_sha,
                "producer_code_tree_sha256": tree_sha,
                "producer_dirty": dirty_s == "1",
                "staves": len(rd.layout.staves),
                "systems": len(rd.layout.systems),
                "emitted_measures": len(cols),
                "measures": cols,
            }, indent=2) + "\\n",
            encoding="utf-8",
        )
        return {
            "page": png.stem,
            "status": "ok",
            "seconds": round(time.time() - t0, 2),
            "emitted_measures": len(cols),
        }
    except Exception:
        xml_out.unlink(missing_ok=True)
        post_out.unlink(missing_ok=True)
        return {
            "page": png.stem,
            "status": "failed",
            "seconds": round(time.time() - t0, 2),
            "error": traceback.format_exc(limit=5)[-800:],
        }

def _worker_loop(
    conn, model_path: str, device: str, expected_src_root: str
) -> None:
    os.environ.setdefault("OMP_NUM_THREADS", "1")
    torch.set_num_threads(1)
    _verify_copisteria_origin(expected_src_root)
    model = read.load_model(model_path, device=device)
    while True:
        try:
            job = conn.recv()
        except EOFError:
            return
        if job is None:
            return
        conn.send(_run_page_with_model(job, model, device))

class _Slot:
    def __init__(
        self, ctx, model_path: str, device: str, expected_src_root: str
    ) -> None:
        self.conn, child = ctx.Pipe()
        self.proc = ctx.Process(
            target=_worker_loop,
            args=(child, model_path, device, expected_src_root),
            daemon=True,
        )
        self.proc.start()
        child.close()

    def kill(self) -> None:
        self.proc.kill()
        self.proc.join()
        self.conn.close()

    def close(self) -> None:
        try:
            self.conn.send(None)
        except OSError:
            pass
        self.proc.join(timeout=5)
        if self.proc.is_alive():
            self.proc.kill()
            self.proc.join()
        self.conn.close()

def main() -> None:
    cfg = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8"))
    expected_src_root = cfg["expected_src_root"]
    _verify_copisteria_origin(expected_src_root)
    model_path = cfg["model_path"]
    device = cfg.get("device", "cpu")
    jobs = cfg["jobs"]
    n_workers = max(1, int(cfg.get("workers", 4)))
    ctx = mp.get_context("spawn")
    idle = queue.Queue()
    all_slots = [
        _Slot(ctx, model_path, device, expected_src_root)
        for _ in range(n_workers)
    ]
    for sl in all_slots:
        idle.put(sl)

    def run_bounded(job: list[str]) -> dict:
        sl = idle.get()
        t0 = time.monotonic()
        try:
            sl.conn.send(job)
            if sl.conn.poll(_PAGE_DEADLINE):
                res = sl.conn.recv()
                idle.put(sl)
                return res
            sl.kill()
            Path(job[1]).unlink(missing_ok=True)
            Path(job[2]).unlink(missing_ok=True)
            repl = _Slot(ctx, model_path, device, expected_src_root)
            all_slots.append(repl)
            idle.put(repl)
            return {
                "page": Path(job[0]).stem,
                "status": "timeout",
                "seconds": round(time.monotonic() - t0, 2),
                "error": "exceeded 300s per-page deadline (SIGKILL)",
            }
        except Exception as exc:
            sl.kill()
            repl = _Slot(ctx, model_path, device, expected_src_root)
            all_slots.append(repl)
            idle.put(repl)
            return {
                "page": Path(job[0]).stem,
                "status": "failed",
                "seconds": round(time.monotonic() - t0, 2),
                "error": str(exc),
            }

    try:
        with ThreadPoolExecutor(max_workers=n_workers) as ex:
            results = list(ex.map(run_bounded, jobs))
    finally:
        for sl in all_slots:
            if sl.proc.is_alive():
                sl.close()
    Path(cfg["results_out"]).write_text(
        json.dumps(results, indent=2) + "\\n", encoding="utf-8"
    )

if __name__ == "__main__":
    main()
"""


def _sha256_file(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as fh:
        while chunk := fh.read(1 << 20):
            h.update(chunk)
    return h.hexdigest()


def _is_sha256_hex(val: object) -> bool:
    return (
        isinstance(val, str)
        and len(val) == 64  # noqa: PLR2004
        and all(c in "0123456789abcdef" for c in val)
    )


class _MeteredRawIO(io.RawIOBase):
    """Socket-boundary reader enforcing a hard TLS-decrypted byte ceiling."""

    def __init__(
        self,
        raw_io: Any,  # noqa: ANN401
        ceiling: int,
        wire: list[int],
        label: str,
    ) -> None:
        self._raw_io = raw_io
        self._ceiling = ceiling
        self._wire = wire
        self._label = label

    def readable(self) -> bool:
        return True

    def readinto(self, b: Any) -> int | None:  # noqa: ANN401
        rem = self._ceiling - self._wire[0]
        if rem <= 0:
            msg = (
                f"download {self._label} wire ceiling ({self._ceiling}B) "
                "reached before EOF"
            )
            raise StopConditionError(msg)
        view = memoryview(b)[:rem]
        while True:
            try:
                res = self._raw_io.readinto(view)
                n = 0 if res is None else int(res)
                break
            except InterruptedError:
                continue
        self._wire[0] += n
        return n

    def close(self) -> None:
        super().close()
        with contextlib.suppress(OSError):
            self._raw_io.close()


class _MeteredSocket:
    """Socket wrapper whose ``makefile`` retains ``_io_refs`` via raw file."""

    def __init__(
        self,
        sock: Any,  # noqa: ANN401
        ceiling: int,
        wire: list[int],
        label: str,
    ) -> None:
        self._sock = sock
        self._ceiling = ceiling
        self._wire = wire
        self._label = label

    def makefile(
        self, mode: str = "r", buffering: int = -1, **_kw: object
    ) -> Any:  # noqa: ANN401
        if "b" in mode and "r" in mode:
            # Delegate to sock.makefile(buffering=0) so socket._io_refs keeps
            # the underlying fd alive after urllib calls HTTPSConnection.close!
            raw_sock_io = self._sock.makefile("rb", buffering=0)
            metered = _MeteredRawIO(
                raw_sock_io, self._ceiling, self._wire, self._label
            )
            buf_sz = min(8192, max(1, self._ceiling - self._wire[0]))
            return io.BufferedReader(metered, buffer_size=buf_sz)
        return self._sock.makefile(mode, buffering)

    def close(self) -> None:
        self._sock.close()

    def __getattr__(self, name: str) -> Any:  # noqa: ANN401
        return getattr(self._sock, name)


class _MeteredHTTPSConnection(http.client.HTTPSConnection):
    def __init__(
        self,
        host: str,
        *,
        ceiling: int,
        wire: list[int],
        label: str,
        **kwargs: Any,  # noqa: ANN401
    ) -> None:
        super().__init__(host, **kwargs)
        self._ceiling = ceiling
        self._wire = wire
        self._label = label

    def connect(self) -> None:
        super().connect()
        self.sock = _MeteredSocket(
            self.sock, self._ceiling, self._wire, self._label
        )


class _MeteredHTTPSHandler(urllib.request.HTTPSHandler):
    def __init__(self, ceiling: int, wire: list[int], label: str) -> None:
        super().__init__()
        self._ceiling = ceiling
        self._wire = wire
        self._label = label

    def https_open(self, req: urllib.request.Request) -> Any:  # noqa: ANN401
        return self.do_open(
            lambda host, **kw: _MeteredHTTPSConnection(
                host,
                ceiling=self._ceiling,
                wire=self._wire,
                label=self._label,
                **kw,
            ),
            req,
        )


class _NoRedirectHandler(urllib.request.HTTPRedirectHandler):
    """Return 30x responses directly without draining redirect bodies."""

    def http_error_301(
        self,
        req: Any,  # noqa: ANN401, ARG002
        fp: Any,  # noqa: ANN401
        code: int,  # noqa: ARG002
        msg: str,  # noqa: ARG002
        headers: Any,  # noqa: ANN401, ARG002
    ) -> Any:  # noqa: ANN401
        return fp

    http_error_302 = http_error_301
    http_error_303 = http_error_301
    http_error_307 = http_error_301
    http_error_308 = http_error_301


_ORIG_URLOPEN = urllib.request.urlopen


def _open_https_request(
    req: urllib.request.Request,
    ceiling: int,
    wire: list[int],
    label: str,
) -> tuple[Any, bool]:
    if urllib.request.urlopen is not _ORIG_URLOPEN:
        return urllib.request.urlopen(req, timeout=180), False  # noqa: S310
    opener = urllib.request.build_opener(
        _MeteredHTTPSHandler(ceiling, wire, label),
        _NoRedirectHandler(),
    )
    return opener.open(req, timeout=180), True


def _load_w2_ledger(ledger_path: Path) -> dict[str, object]:
    if ledger_path.exists():
        raw = json.loads(ledger_path.read_text(encoding="utf-8"))
        entries = raw.get("entries", [])
        entry_sum = (
            sum(int(e.get("bytes", 0)) for e in entries)
            if isinstance(entries, list)
            else 0
        )
        raw["cumulative_bytes"] = max(
            int(raw.get("cumulative_bytes", 0)), entry_sum
        )
        return raw
    return {
        "schema": "w2-download-ledger-v1",
        "cap_bytes": W2_DOWNLOAD_CAP_BYTES,
        "cumulative_bytes": 0,
        "entries": [],
    }


def _save_w2_ledger(ledger_path: Path, ledger: dict[str, object]) -> None:
    ledger_path.parent.mkdir(parents=True, exist_ok=True)
    tmp_p = ledger_path.with_name(f".{ledger_path.name}.{os.getpid()}.tmp")
    tmp_p.write_text(json.dumps(ledger, indent=2) + "\n", encoding="utf-8")
    tmp_p.replace(ledger_path)


def _is_eof_established(
    resp: object, cl_val: int | None, body_read: int
) -> bool:
    if cl_val is not None and body_read == cl_val:
        return True
    if getattr(resp, "length", None) == 0:
        return True
    return isinstance(resp, io.BytesIO) and resp.tell() >= len(resp.getvalue())


def _stream_https_to_file(  # noqa: C901, PLR0912, PLR0913, PLR0915
    *,
    url: str,
    tmp: Path,
    ceiling: int,
    expected_bytes: int | None,
    label: str,
    received: list[int],
) -> tuple[int, str]:
    cur_url = url
    resp: Any = None
    sock_metered = False
    for _ in range(6):
        if not cur_url.startswith("https://"):
            msg = f"refusing non-HTTPS URL for {label}"
            raise StopConditionError(msg)
        req = urllib.request.Request(  # noqa: S310 - HTTPS checked above
            cur_url, headers={"User-Agent": "sheet-music-omr-w2/1.0"}
        )
        r_cand, sock_metered = _open_https_request(
            req, ceiling, received, label
        )
        hdrs_c = getattr(r_cand, "headers", None)
        if (
            hdrs_c is not None
            and hdrs_c.get("Transfer-Encoding") is not None
            and hdrs_c.get("Content-Length") is not None
        ):
            with contextlib.suppress(Exception):
                r_cand.close()
            msg = (
                f"download {label} rejected ambiguous framing "
                "(both Transfer-Encoding and Content-Length)"
            )
            raise StopConditionError(msg)
        code = getattr(r_cand, "status", None) or getattr(r_cand, "code", 200)
        if code in {301, 302, 303, 307, 308}:
            hdrs_r = getattr(r_cand, "headers", None)
            loc = hdrs_r.get("Location") if hdrs_r is not None else None
            if not sock_metered:
                hdr_cost = (
                    len(str(hdrs_r).encode("latin-1", errors="replace"))
                    if hdrs_r is not None
                    else 256
                )
                received[0] += min(ceiling - received[0], max(1, hdr_cost))
            with contextlib.suppress(Exception):
                r_cand.close()
            if not loc or received[0] >= ceiling:
                msg = f"redirect for {label} invalid or exhausted ceiling"
                raise StopConditionError(msg)
            cur_url = urllib.parse.urljoin(cur_url, str(loc))
            continue
        resp = r_cand
        break
    else:
        msg = f"too many redirects for {label}"
        raise StopConditionError(msg)

    h = hashlib.sha256()
    body_read = 0
    with resp, tmp.open("wb") as out:
        hdrs = getattr(resp, "headers", None)
        te_hdr = hdrs.get("Transfer-Encoding") if hdrs is not None else None
        cl_hdr = hdrs.get("Content-Length") if hdrs is not None else None
        if te_hdr is not None and cl_hdr is not None:
            msg = (
                f"download {label} rejected ambiguous framing "
                "(both Transfer-Encoding and Content-Length)"
            )
            raise StopConditionError(msg)
        is_chunked = te_hdr is not None or bool(getattr(resp, "chunked", False))
        cl_val = (
            int(cl_hdr.strip())
            if (
                not is_chunked
                and isinstance(cl_hdr, str)
                and cl_hdr.strip().isdigit()
            )
            else None
        )
        body_ceiling = (
            expected_bytes
            if expected_bytes is not None
            else (ceiling if sock_metered else (ceiling - received[0]))
        )
        if cl_val is not None and (
            cl_val > body_ceiling
            or (expected_bytes is not None and cl_val != expected_bytes)
        ):
            msg = f"download {label} Content-Length {cl_val} invalid vs ceiling"
            raise StopConditionError(msg)

        reader = resp.read1 if hasattr(resp, "read1") else resp.read
        while True:
            rem = (
                body_ceiling - body_read
                if sock_metered
                else min(body_ceiling - body_read, ceiling - received[0])
            )
            if rem <= 0:
                if _is_eof_established(resp, cl_val, body_read):
                    break
                if sock_metered or ceiling - received[0] >= 1:
                    extra = reader(1)
                    if not extra:
                        break
                    if not sock_metered:
                        received[0] += len(extra)
                    msg = f"download {label} exceeded expected {body_ceiling}B"
                    raise StopConditionError(msg)
                msg = (
                    f"download {label} reached byte ceiling ({ceiling}) "
                    "before EOF"
                )
                raise StopConditionError(msg)
            read_sz = min(1 << 18, rem)
            try:
                chunk = reader(read_sz)
            except BaseException as r_exc:
                part_b = getattr(r_exc, "partial", b"")
                if not sock_metered and isinstance(part_b, (bytes, bytearray)):
                    received[0] += min(rem, len(part_b))
                raise
            if not chunk:
                break
            if len(chunk) > rem:
                if not sock_metered:
                    received[0] += rem
                msg = f"download {label} stream returned > requested {rem}B"
                raise StopConditionError(msg)
            if not sock_metered:
                received[0] += len(chunk)
            body_read += len(chunk)
            h.update(chunk)
            out.write(chunk)
        if cl_val is not None and body_read != cl_val:
            msg = (
                f"download {label} truncated before Content-Length "
                f"({body_read} != {cl_val})"
            )
            raise StopConditionError(msg)
    return body_read, h.hexdigest()


def _fetch_with_ledger(  # noqa: PLR0913, PLR0915
    *,
    url: str,
    dest: Path,
    expected_bytes: int | None,
    expected_sha256: str | None,
    ledger_path: Path,
    label: str,
    max_unknown_bytes: int = 50_000_000,
) -> tuple[int, str]:
    if dest.exists():
        actual_size = dest.stat().st_size
        actual_sha = _sha256_file(dest)
        if (expected_bytes is None or actual_size == expected_bytes) and (
            expected_sha256 is None or actual_sha == expected_sha256
        ):
            return actual_size, actual_sha
        msg = (
            f"cached {dest.name} failed verification "
            f"(size={actual_size}, sha256={actual_sha})"
        )
        raise StopConditionError(msg)

    ledger = _load_w2_ledger(ledger_path)
    cur_total = int(ledger["cumulative_bytes"])  # type: ignore[arg-type]
    rem_cap = W2_DOWNLOAD_CAP_BYTES - cur_total
    if rem_cap <= 0 or (
        expected_bytes is not None and expected_bytes > rem_cap
    ):
        msg = (
            f"download {label} would exceed 10 GB cap "
            f"({cur_total} + {expected_bytes or 1})"
        )
        raise StopConditionError(msg)

    ceiling = (
        min(rem_cap, expected_bytes + 65_536)
        if expected_bytes is not None
        else min(max_unknown_bytes, rem_cap)
    )

    # Atomically persist enforced per-transfer ceiling BEFORE opening socket!
    entries: list[dict[str, object]] = ledger["entries"]  # type: ignore[assignment]
    res_idx = len(entries)
    entries.append(
        {
            "utc": datetime.now(UTC).isoformat(),
            "label": label,
            "url": url,
            "bytes": ceiling,
            "status": "reserved",
        }
    )
    ledger["cumulative_bytes"] = cur_total + ceiling
    _save_w2_ledger(ledger_path, ledger)

    dest.parent.mkdir(parents=True, exist_ok=True)
    tmp = dest.with_suffix(dest.suffix + ".part")
    received = [0]
    try:
        bytes_read, digest = _stream_https_to_file(
            url=url,
            tmp=tmp,
            ceiling=ceiling,
            expected_bytes=expected_bytes,
            label=label,
            received=received,
        )
    except BaseException as exc:
        charged = min(
            ceiling,
            max(received[0], tmp.stat().st_size if tmp.exists() else 0),
        )
        tmp.unlink(missing_ok=True)
        ledger["cumulative_bytes"] = cur_total + charged
        entries[res_idx] = {
            "utc": datetime.now(UTC).isoformat(),
            "label": label,
            "url": url,
            "bytes": charged,
            "status": "failed",
            "error": str(exc) or type(exc).__name__,
        }
        _save_w2_ledger(ledger_path, ledger)
        if isinstance(exc, (StopConditionError, KeyboardInterrupt, SystemExit)):
            raise
        msg_0 = f"download {label} failed: {exc}"
        raise StopConditionError(msg_0) from exc

    charged_ok = min(ceiling, max(received[0], bytes_read))
    if expected_bytes is not None and bytes_read != expected_bytes:
        tmp.unlink(missing_ok=True)
        ledger["cumulative_bytes"] = cur_total + charged_ok
        entries[res_idx] = {
            "utc": datetime.now(UTC).isoformat(),
            "label": label,
            "url": url,
            "bytes": charged_ok,
            "status": "failed_size_mismatch",
        }
        _save_w2_ledger(ledger_path, ledger)
        msg = (
            f"{label} size mismatch: expected {expected_bytes}, "
            f"got {bytes_read}"
        )
        raise StopConditionError(msg)
    if expected_sha256 is not None and digest != expected_sha256:
        tmp.unlink(missing_ok=True)
        ledger["cumulative_bytes"] = cur_total + charged_ok
        entries[res_idx] = {
            "utc": datetime.now(UTC).isoformat(),
            "label": label,
            "url": url,
            "bytes": charged_ok,
            "status": "failed_sha256_mismatch",
        }
        _save_w2_ledger(ledger_path, ledger)
        msg = (
            f"{label} SHA-256 mismatch: expected {expected_sha256}, "
            f"got {digest}"
        )
        raise StopConditionError(msg)

    tmp.replace(dest)
    ledger["cumulative_bytes"] = cur_total + charged_ok
    entries[res_idx] = {
        "utc": datetime.now(UTC).isoformat(),
        "label": label,
        "url": url,
        "bytes": charged_ok,
        "body_bytes": bytes_read,
        "sha256": digest,
        "status": "ok",
    }
    _save_w2_ledger(ledger_path, ledger)
    return bytes_read, digest


def append_test_touches(  # noqa: PLR0913
    *,
    touch_log_path: Path,
    run_id: str,
    git_commit: str,
    code_tree_sha256: str,
    readers: Sequence[tuple[str, str | None, str | None]],
    piece_count: int,
    command: Sequence[str],
    open_reading_masks_sha256: str | None = None,
    purpose: str = "S0-baseline",
    cap: int = S0_TEST_TOUCH_CAP,
) -> list[int]:
    """Atomically check capacity and append one or more ``TEST`` touches."""
    existing = []
    if touch_log_path.exists():
        existing = [
            json.loads(ln)
            for ln in touch_log_path.read_text(encoding="utf-8").splitlines()
            if ln.strip()
        ]
    if open_reading_masks_sha256 is not None:
        for r in existing:
            prior_m = r.get("open_reading_masks_sha256")
            if (
                r.get("purpose") == purpose
                and isinstance(prior_m, str)
                and prior_m != open_reading_masks_sha256
            ):
                msg = (
                    f"open_reading_masks_sha256 changed across {purpose} "
                    "campaign after prior TEST touch"
                )
                raise StopConditionError(msg)
    s0_touches = [
        r
        for r in existing
        if r.get("split") == "TEST" and r.get("purpose") == purpose
    ]
    if len(s0_touches) + len(readers) > cap:
        names = ", ".join(r[0] for r in readers)
        msg = (
            f"appending {len(readers)} TEST touch(es) for [{names}] would "
            f"exceed {cap}-touch {purpose} cap (already {len(s0_touches)})"
        )
        raise StopConditionError(msg)

    utc_now = datetime.now(UTC).isoformat()
    touch_log_path.parent.mkdir(parents=True, exist_ok=True)
    counts: list[int] = []
    with touch_log_path.open("a", encoding="utf-8") as fh:
        for idx, (r_id, r_sha, w_sha) in enumerate(readers, start=1):
            rec = {
                "utc": utc_now,
                "run_id": run_id,
                "git_commit": git_commit,
                "code_tree_sha256": code_tree_sha256,
                "reader_id": r_id,
                "reader_sha256": r_sha,
                "weights_sha256": w_sha,
                "open_reading_masks_sha256": open_reading_masks_sha256,
                "split": "TEST",
                "piece_count": piece_count,
                "command": list(command),
                "purpose": purpose,
            }
            fh.write(json.dumps(rec, separators=(",", ":")) + "\n")
            counts.append(len(s0_touches) + idx)
    return counts


def append_test_touch(  # noqa: PLR0913
    *,
    touch_log_path: Path,
    run_id: str,
    git_commit: str,
    code_tree_sha256: str,
    reader_id: str,
    reader_sha256: str | None,
    weights_sha256: str | None,
    piece_count: int,
    command: Sequence[str],
    open_reading_masks_sha256: str | None = None,
    purpose: str = "S0-baseline",
    cap: int = S0_TEST_TOUCH_CAP,
) -> int:
    """Append a single ``TEST`` touch record."""
    return append_test_touches(
        touch_log_path=touch_log_path,
        run_id=run_id,
        git_commit=git_commit,
        code_tree_sha256=code_tree_sha256,
        readers=[(reader_id, reader_sha256, weights_sha256)],
        piece_count=piece_count,
        command=command,
        open_reading_masks_sha256=open_reading_masks_sha256,
        purpose=purpose,
        cap=cap,
    )[0]


def copista_provenance_sha256(prov: dict[str, object]) -> str:
    """Compute canonical digest of ``copista-28m`` code, weights and worker."""
    return hashlib.sha256(
        json.dumps(
            {
                "commit": prov["copisteria_commit"],
                "archive_sha": prov["copisteria_archive_sha256"],
                "src_tree_sha": prov.get("copisteria_src_tree_sha256", ""),
                "weights_sha": prov["combined_weights_sha256"],
                "versions": prov["venv_versions"],
                "worker_sha": hashlib.sha256(
                    _WORKER_SCRIPT.encode("utf-8")
                ).hexdigest(),
            },
            sort_keys=True,
        ).encode("utf-8")
    ).hexdigest()


def _verify_and_fetch_wheels(
    cache_dir: Path, ledger_path: Path
) -> tuple[list[str], str]:
    """Fetch or verify every pinned wheel and return exact file paths."""
    wheelhouse = cache_dir / "wheelhouse"
    wheelhouse.mkdir(parents=True, exist_ok=True)
    wheel_paths: list[str] = []
    for pkg_name, _ver, url, exp_size, exp_sha in resolve_host_wheel_lock():
        fname = urllib.parse.unquote(url.rsplit("/", 1)[-1])
        dest = wheelhouse / fname
        _ = _fetch_with_ledger(
            url=url,
            dest=dest,
            expected_bytes=exp_size,
            expected_sha256=exp_sha,
            ledger_path=ledger_path,
            label=f"wheel/{pkg_name}",
        )
        wheel_paths.append(str(dest))
    return wheel_paths, compute_wheel_lock_sha256()


def _install_copista_wheels(
    py_bin: Path,
    src_root: Path,
    cache_dir: Path,
    ledger_path: Path,
) -> str:
    """Install exact verified wheel files strictly offline."""
    wheel_paths, lock_sha = _verify_and_fetch_wheels(cache_dir, ledger_path)
    wheelhouse = cache_dir / "wheelhouse"
    offline_env = {
        **os.environ,
        "UV_OFFLINE": "1",
        "UV_NO_PYTHON_DOWNLOADS": "1",
    }
    # Step 1: install exact verified wheel files (--no-deps)
    subprocess.run(  # noqa: S603 - offline install of exact verified wheel files
        [  # noqa: S607
            "uv",
            "pip",
            "install",
            "--no-config",
            "--no-python-downloads",
            "--python",
            str(py_bin),
            "--offline",
            "--no-index",
            "--no-deps",
            *wheel_paths,
        ],
        env=offline_env,
        check=True,
    )
    # Step 2: editable install of copisteria using pre-installed setuptools
    subprocess.run(  # noqa: S603 - offline editable build
        [  # noqa: S607
            "uv",
            "pip",
            "install",
            "--no-config",
            "--no-python-downloads",
            "--python",
            str(py_bin),
            "--offline",
            "--no-index",
            "--find-links",
            str(wheelhouse),
            "--no-build-isolation",
            "--no-deps",
            "-e",
            str(src_root),
        ],
        env=offline_env,
        check=True,
    )
    return lock_sha


def _compute_src_tree_sha256(src_root: Path) -> str:
    """Compute canonical tree digest of ``src_root`` excluding weights/build."""
    weight_rels = {f"models/{rel_w}" for rel_w, _, _ in COPISTA_WEIGHTS}
    entries: list[str] = []
    for p in src_root.rglob("*"):
        if p.is_symlink():
            msg = f"unexpected symlink in copisteria source tree: {p.name}"
            raise StopConditionError(msg)
        if not p.is_file():
            continue
        rel_parts = p.relative_to(src_root).parts
        rel = "/".join(rel_parts)
        if (
            rel in weight_rels
            or any(
                pt.endswith(".egg-info") or pt in {"__pycache__", "build"}
                for pt in rel_parts
            )
            or p.suffix in {".pyc", ".pyo"}
        ):
            continue
        entries.append(f"{rel}:{_sha256_file(p)}")
    return hashlib.sha256(
        "\n".join(sorted(entries)).encode("utf-8")
    ).hexdigest()


def _verify_copisteria_archive_and_unpack(  # noqa: C901, PLR0912, PLR0915
    archive_path: Path,
    cache_dir: Path,
    src_root: Path,
    *,
    allow_extract: bool = True,
) -> str:
    if (
        not archive_path.is_file()
        or archive_path.stat().st_size != COPISTERIA_ARCHIVE_BYTES
        or _sha256_file(archive_path) != COPISTERIA_ARCHIVE_SHA256
    ):
        msg = "copisteria archive missing or failed pinned size/SHA-256 check"
        raise StopConditionError(msg)

    expected_prefix = f"copisteria-{COPISTERIA_COMMIT}/"
    arc_entries: list[str] = []
    try:
        with tarfile.open(archive_path, "r:gz") as tf:
            pax_commit = tf.pax_headers.get("comment")
            if pax_commit != COPISTERIA_COMMIT:
                msg = (
                    f"copisteria archive missing/mismatched pax commit "
                    f"{pax_commit!r} != {COPISTERIA_COMMIT!r}"
                )
                raise StopConditionError(msg)
            members = tf.getmembers()
            if not members:
                msg = "empty copisteria archive"
                raise StopConditionError(msg)
            for m in members:
                if m.name != expected_prefix.rstrip(
                    "/"
                ) and not m.name.startswith(expected_prefix):
                    msg = (
                        f"copisteria archive member {m.name!r} outside "
                        f"{expected_prefix}"
                    )
                    raise StopConditionError(msg)
                if m.isfile():
                    rel = m.name[len(expected_prefix) :]
                    fh = tf.extractfile(m)
                    if fh is None:
                        continue
                    digest = hashlib.sha256(fh.read()).hexdigest()
                    arc_entries.append(f"{rel}:{digest}")
            expected_tree_sha = hashlib.sha256(
                "\n".join(sorted(arc_entries)).encode("utf-8")
            ).hexdigest()

            needs_extract = (
                not (src_root / "pyproject.toml").is_file()
                or _compute_src_tree_sha256(src_root) != expected_tree_sha
            )
            if needs_extract:
                if not allow_extract:
                    msg = "copisteria source tree modified since setup-copista"
                    raise StopConditionError(msg)
                saved_models = cache_dir / f".models-preserve-{os.getpid()}"
                shutil.rmtree(saved_models, ignore_errors=True)
                for rel_w, _, _ in COPISTA_WEIGHTS:
                    old_w = src_root / "models" / rel_w
                    if old_w.is_file():
                        dst_w = saved_models / rel_w
                        dst_w.parent.mkdir(parents=True, exist_ok=True)
                        old_w.replace(dst_w)
                shutil.rmtree(src_root, ignore_errors=True)
                tf.extractall(cache_dir, filter="data")
                if saved_models.is_dir():
                    for rel_w, _, _ in COPISTA_WEIGHTS:
                        kept_w = saved_models / rel_w
                        if kept_w.is_file():
                            fin_w = src_root / "models" / rel_w
                            fin_w.parent.mkdir(parents=True, exist_ok=True)
                            kept_w.replace(fin_w)
                    shutil.rmtree(saved_models, ignore_errors=True)
                if _compute_src_tree_sha256(src_root) != expected_tree_sha:
                    msg = "extracted copisteria source tree digest mismatch"
                    raise StopConditionError(msg)
    except (tarfile.TarError, OSError) as exc:
        msg = f"invalid copisteria archive {archive_path.name}: {exc}"
        raise StopConditionError(msg) from exc
    return expected_tree_sha


def _verify_venv_imports(py_bin: Path, src_root: Path) -> bool:
    if not py_bin.is_file():
        return False
    probe = subprocess.run(  # noqa: S603 - single-interpreter pre+post check
        [
            str(py_bin),
            "-c",
            (
                "import importlib.util, sys\n"
                "from pathlib import Path\n"
                "spec = importlib.util.find_spec('copisteria')\n"
                "root = Path(sys.argv[1]).resolve()\n"
                "if spec is None or spec.origin is None or not "
                "Path(spec.origin).resolve().is_relative_to(root):\n"
                "    raise SystemExit(2)\n"
                "import copisteria, pypdfium2, torch, cv2, scipy\n"
                "if not Path(copisteria.__file__).resolve()"
                ".is_relative_to(root):\n"
                "    raise SystemExit(3)\n"
            ),
            str(src_root),
        ],
        capture_output=True,
        check=False,
    )
    return probe.returncode == 0


def setup_copista_environment(
    confinement: Confinement,
) -> dict[str, object]:
    """Set up ``w2-copista-venv``, ``copisteria`` and pinned weights."""
    downloads_dir = confinement.downloads
    ledger_path = downloads_dir / "w2-download-ledger.json"
    ledger_lock = downloads_dir / ".ledger.lock"
    cache_dir = downloads_dir / "w2-copista-cache"
    venv_dir = downloads_dir / "w2-copista-venv"
    cache_dir.mkdir(parents=True, exist_ok=True)

    with confinement.lock(ledger_lock):
        # 1. Fetch and verify pinned copisteria source archive
        archive_path = cache_dir / f"copisteria-{COPISTERIA_COMMIT}.tar.gz"
        prev_prov_p = cache_dir / "provenance.json"
        if prev_prov_p.is_file():
            prev_prov = json.loads(prev_prov_p.read_text(encoding="utf-8"))
            raw_sha = prev_prov.get("copisteria_archive_sha256")
            if (
                prev_prov.get("copisteria_commit") != COPISTERIA_COMMIT
                or not _is_sha256_hex(raw_sha)
                or raw_sha != COPISTERIA_ARCHIVE_SHA256
            ):
                msg = "existing provenance.json failed pinned archive check"
                raise StopConditionError(msg)
        _, archive_sha = _fetch_with_ledger(
            url=COPISTERIA_ARCHIVE_URL,
            dest=archive_path,
            expected_bytes=COPISTERIA_ARCHIVE_BYTES,
            expected_sha256=COPISTERIA_ARCHIVE_SHA256,
            ledger_path=ledger_path,
            label=f"copisteria@{COPISTERIA_COMMIT}",
        )

        # Verify archive PAX commit & source tree against archive
        src_root = cache_dir / f"copisteria-{COPISTERIA_COMMIT}"
        src_tree_sha = _verify_copisteria_archive_and_unpack(
            archive_path, cache_dir, src_root, allow_extract=True
        )

        # 2. Fetch and verify pinned copista-28m weights into src_root/models/
        models_dir = src_root / "models"
        weight_digests: dict[str, str] = {}
        for rel_path, exp_bytes, exp_sha in COPISTA_WEIGHTS:
            url = (
                f"https://huggingface.co/kobimusic/copista-28m/resolve/"
                f"{COPISTA_HF_REVISION}/{rel_path}"
            )
            dest = models_dir / rel_path
            _, sha = _fetch_with_ledger(
                url=url,
                dest=dest,
                expected_bytes=exp_bytes,
                expected_sha256=exp_sha,
                ledger_path=ledger_path,
                label=f"copista-28m/{rel_path}",
            )
            weight_digests[rel_path] = sha

        # 3. Reuse or rebuild isolated venv offline & install pinned lock
        if venv_dir.is_symlink():
            msg = "refusing symlink w2-copista-venv directory"
            raise StopConditionError(msg)
        py_bin = venv_python_path(venv_dir)
        _, expected_wheel_lock_sha = _verify_and_fetch_wheels(
            cache_dir, ledger_path
        )
        lock_stamp = venv_dir / ".copista-wheel-lock-sha256"
        venv_reusable = (
            (venv_dir / "pyvenv.cfg").is_file()
            and py_bin.is_file()
            and lock_stamp.is_file()
            and lock_stamp.read_text(encoding="utf-8").strip()
            == expected_wheel_lock_sha
            and _verify_venv_imports(py_bin, src_root)
        )
        if not venv_reusable:
            if venv_dir.exists():
                shutil.rmtree(venv_dir)
            offline_env = {
                **os.environ,
                "UV_OFFLINE": "1",
                "UV_NO_PYTHON_DOWNLOADS": "1",
            }
            subprocess.run(  # noqa: S603 - offline fresh venv from host python
                [  # noqa: S607
                    "uv",
                    "venv",
                    "--clear",
                    "--no-project",
                    "--no-config",
                    "--no-managed-python",
                    "--no-python-downloads",
                    "--python",
                    sys.executable,
                    str(venv_dir),
                ],
                env=offline_env,
                check=True,
            )
            installed_sha = _install_copista_wheels(
                py_bin, src_root, cache_dir, ledger_path
            )
            if not _verify_venv_imports(py_bin, src_root):
                msg = "post-install copisteria import/origin check failed"
                raise StopConditionError(msg)
            lock_stamp.write_text(installed_sha + "\n", encoding="utf-8")

    # Collect resolved versions in w2-copista-venv
    ver_probe = subprocess.run(  # noqa: S603 - fixed local version probe
        [
            str(py_bin),
            "-c",
            (
                "import importlib.metadata as im, json, sys; "
                "pkgs = ['copisteria','pypdfium2','torch','torchvision',"
                "'numpy','pillow','scipy','opencv-python']; "
                "out = {'python': sys.version.split()[0]}; "
                "out.update({p: im.version(p) for p in pkgs}); "
                "print(json.dumps(out))"
            ),
        ],
        check=True,
        capture_output=True,
        text=True,
    )
    venv_versions = json.loads(ver_probe.stdout.strip())
    combined_weights_sha = hashlib.sha256(
        "".join(f"{k}:{v}\n" for k, v in sorted(weight_digests.items())).encode(
            "utf-8"
        )
    ).hexdigest()

    ledger = _load_w2_ledger(ledger_path)
    provenance = {
        "copisteria_commit": COPISTERIA_COMMIT,
        "copisteria_archive_sha256": archive_sha,
        "copisteria_src_tree_sha256": src_tree_sha,
        "copista_hf_revision": COPISTA_HF_REVISION,
        "weight_sha256": weight_digests,
        "combined_weights_sha256": combined_weights_sha,
        "venv_versions": venv_versions,
        "w2_cumulative_download_bytes": ledger["cumulative_bytes"],
    }
    (cache_dir / "provenance.json").write_text(
        json.dumps(provenance, indent=2) + "\n", encoding="utf-8"
    )
    return provenance


def run_copista_inference_and_assemble(  # noqa: C901, PLR0912, PLR0915
    *,
    confinement: Confinement,
    workers: int = 4,
    device: str = "cpu",
    formal: bool = False,
) -> dict[str, object]:
    """Run ``copista-28m`` on all 92 approved pages and assemble ``P_piece``."""
    src_st = source_state(run_git)
    cur_commit = str(src_st["commit"])
    cur_tree_sha = str(src_st["tree"])
    cur_dirty = bool(src_st.get("dirty"))
    if formal and cur_dirty:
        msg = "project source tree has uncommitted changes during formal run"
        raise StopConditionError(msg)
    run_dir = confinement.run
    downloads_dir = confinement.downloads
    manifest, _ = verify_approved_overlays(run_dir)
    if formal and (
        manifest.get("producer_dirty") is not False
        or manifest.get("producer_git_commit") != cur_commit
        or manifest.get("producer_code_tree_sha256") != cur_tree_sha
    ):
        msg = "manifest produced on dirty or mismatched commit/tree"
        raise StopConditionError(msg)
    masks_sha = str(manifest["open_reading_masks_sha256"])

    cache_dir = downloads_dir / "w2-copista-cache"
    venv_dir = downloads_dir / "w2-copista-venv"
    prov_path = cache_dir / "provenance.json"
    if not prov_path.is_file():
        msg = "copista provenance.json missing; run setup-copista first"
        raise StopConditionError(msg)

    prov = json.loads(prov_path.read_text(encoding="utf-8"))
    archive_path = cache_dir / f"copisteria-{COPISTERIA_COMMIT}.tar.gz"
    src_root = cache_dir / f"copisteria-{COPISTERIA_COMMIT}"
    venv_py = venv_python_path(venv_dir)
    models_dir = src_root / "models"

    verified_tree_sha = _verify_copisteria_archive_and_unpack(
        archive_path, cache_dir, src_root, allow_extract=False
    )
    lock_stamp = venv_dir / ".copista-wheel-lock-sha256"
    expected_wheel_lock_sha = compute_wheel_lock_sha256()
    if (
        prov.get("copisteria_commit") != COPISTERIA_COMMIT
        or prov.get("copisteria_archive_sha256") != COPISTERIA_ARCHIVE_SHA256
        or prov.get("copisteria_src_tree_sha256") != verified_tree_sha
        or venv_dir.is_symlink()
        or not (venv_dir / "pyvenv.cfg").is_file()
        or not lock_stamp.is_file()
        or lock_stamp.read_text(encoding="utf-8").strip()
        != expected_wheel_lock_sha
        or not _verify_venv_imports(venv_py, src_root)
    ):
        msg = "copisteria archive, wheel lock or venv failed verification"
        raise StopConditionError(msg)
    prov_sha = copista_provenance_sha256(prov)

    # Re-verify weight sizes and SHA-256 digests before running inference!
    for rel_path, exp_bytes, exp_sha in COPISTA_WEIGHTS:
        wp = models_dir / rel_path
        if (
            not wp.is_file()
            or wp.stat().st_size != exp_bytes
            or _sha256_file(wp) != exp_sha
        ):
            msg = f"weight verification failed for {rel_path}"
            raise StopConditionError(msg)

    # Validate EVERY output & temp path BEFORE freezing or touching TEST!
    preds_dir = run_dir / "preds" / "copista-28m"
    tmp_dir = run_dir / "tmp"
    res_json = preds_dir / "page-run-summary.json"
    check_no_symlink_under(preds_dir, run_dir)
    check_no_symlink_under(tmp_dir, run_dir)
    check_no_symlink_under(res_json, run_dir)
    for piece in manifest["pieces"]:
        gid_pf = str(piece["gold_id"])
        check_no_symlink_under(preds_dir / f"{gid_pf}.musicxml", run_dir)
        check_no_symlink_under(preds_dir / f"{gid_pf}.meta.json", run_dir)
        for pg in piece["pages"]:
            stem_pf = f"{gid_pf}-p{int(pg['page_index']):02d}"
            check_no_symlink_under(preds_dir / f"{stem_pf}.musicxml", run_dir)
            check_no_symlink_under(
                preds_dir / f"{stem_pf}.posteriors.json", run_dir
            )

    # Freeze open_reading_masks_sha256 before running (TEST scoring touches are
    # recorded atomically in score-s0 before scoring any TEST piece per
    # evaluation-protocol.md:605-616).
    with confinement.lock(downloads_dir / ".ledger.lock"):
        freeze_run_masks(run_dir, downloads_dir, masks_sha)

    preds_dir.mkdir(parents=True, exist_ok=True)
    tmp_dir.mkdir(parents=True, exist_ok=True)
    pages_dir = run_dir / "pages"
    worker_dir = Path(tempfile.mkdtemp(prefix="copista-worker-", dir=tmp_dir))
    staged_page_dir = worker_dir / "pages"
    staged_page_dir.mkdir(parents=True, exist_ok=True)

    try:
        jobs: list[list[str]] = []
        cached_summaries: list[dict[str, object]] = []
        staged_stems: list[str] = []
        for piece in manifest["pieces"]:
            gid = piece["gold_id"]
            for pg in piece["pages"]:
                p_idx = int(pg["page_index"])
                stem = f"{gid}-p{p_idx:02d}"
                png_p = pages_dir / f"{stem}.png"
                pg_sha = _sha256_file(png_p)
                final_xp = preds_dir / f"{stem}.musicxml"
                final_pp = preds_dir / f"{stem}.posteriors.json"
                check_no_symlink_under(final_xp, run_dir)
                check_no_symlink_under(final_pp, run_dir)
                if final_xp.is_file() and final_pp.is_file():
                    try:
                        pdata_c = json.loads(
                            final_pp.read_text(encoding="utf-8")
                        )
                        if (
                            pdata_c.get("page_sha256") == pg_sha
                            and pdata_c.get("provenance_sha256") == prov_sha
                            and pdata_c.get("open_reading_masks_sha256")
                            == masks_sha
                            and pdata_c.get("producer_git_commit") == cur_commit
                            and pdata_c.get("producer_code_tree_sha256")
                            == cur_tree_sha
                            and pdata_c.get("producer_dirty") is cur_dirty
                        ):
                            cached_summaries.append(
                                {
                                    "page": stem,
                                    "status": "cached",
                                    "seconds": 0.0,
                                }
                            )
                            continue
                    except OSError, ValueError:
                        pass
                staged_stems.append(stem)
                jobs.append(
                    [
                        str(png_p),
                        str(staged_page_dir / f"{stem}.musicxml"),
                        str(staged_page_dir / f"{stem}.posteriors.json"),
                        pg_sha,
                        prov_sha,
                        masks_sha,
                        cur_commit,
                        cur_tree_sha,
                        "1" if cur_dirty else "0",
                    ]
                )

        run_results: list[dict[str, object]] = list(cached_summaries)
        if jobs:
            worker_py = worker_dir / "copista_worker.py"
            cfg_json = worker_dir / "copista_jobs.json"
            staged_res = worker_dir / "page-run-summary.json"
            atomic_write_text(worker_py, _WORKER_SCRIPT)
            atomic_write_text(
                cfg_json,
                json.dumps(
                    {
                        "expected_src_root": str(src_root),
                        "model_path": str(models_dir / "evidence-2m.pt"),
                        "device": device,
                        "workers": workers,
                        "jobs": jobs,
                        "results_out": str(staged_res),
                    },
                    indent=2,
                ),
            )
            batch_timeout = PAGE_DEADLINE_SECONDS * max(1, len(jobs)) + 120
            worker_env = {
                **os.environ,
                "OMR_EXPECTED_COPISTERIA_ROOT": str(src_root),
                "COPISTERIA_DETECTOR": "28m",
            }
            try:
                subprocess.run(  # noqa: S603 - verified venv & worker script
                    [str(venv_py), str(worker_py), str(cfg_json)],
                    env=worker_env,
                    check=True,
                    timeout=batch_timeout,
                    start_new_session=True,
                )
            except subprocess.TimeoutExpired as exc:
                msg = f"copista batch worker exceeded {batch_timeout}s ceiling"
                raise StopConditionError(msg) from exc

            run_results.extend(
                json.loads(staged_res.read_text(encoding="utf-8"))
            )
            for stem in staged_stems:
                st_xp = staged_page_dir / f"{stem}.musicxml"
                st_pp = staged_page_dir / f"{stem}.posteriors.json"
                fin_xp = preds_dir / f"{stem}.musicxml"
                fin_pp = preds_dir / f"{stem}.posteriors.json"
                if st_xp.is_file() and st_pp.is_file():
                    atomic_write_text(fin_xp, st_xp.read_text(encoding="utf-8"))
                    atomic_write_text(fin_pp, st_pp.read_text(encoding="utf-8"))
                else:
                    if fin_xp.is_file():
                        fin_xp.unlink(missing_ok=True)
                    if fin_pp.is_file():
                        fin_pp.unlink(missing_ok=True)

        atomic_write_text(res_json, json.dumps(run_results, indent=2) + "\n")
    finally:
        shutil.rmtree(worker_dir, ignore_errors=True)

    # Assemble whole-piece P_piece for each of the 23 pieces
    piece_summaries: list[dict[str, object]] = []
    for piece in manifest["pieces"]:
        gid = piece["gold_id"]
        p_cnt = int(piece["page_count"])
        page_xmls: list[Path | None] = []
        page_confs: dict[int, Sequence[float]] = {}
        piece_page_shas: list[str] = []
        for p_idx in range(1, p_cnt + 1):
            stem = f"{gid}-p{p_idx:02d}"
            png_p = pages_dir / f"{stem}.png"
            pg_sha = _sha256_file(png_p)
            piece_page_shas.append(f"{stem}:{pg_sha}")
            xp = preds_dir / f"{stem}.musicxml"
            pp = preds_dir / f"{stem}.posteriors.json"
            pdata = (
                json.loads(pp.read_text(encoding="utf-8"))
                if pp.exists()
                else {}
            )
            cache_valid = (
                xp.exists()
                and pdata.get("page_sha256") == pg_sha
                and pdata.get("provenance_sha256") == prov_sha
                and pdata.get("open_reading_masks_sha256") == masks_sha
                and pdata.get("producer_git_commit") == cur_commit
                and pdata.get("producer_code_tree_sha256") == cur_tree_sha
                and pdata.get("producer_dirty") is cur_dirty
            )
            page_xmls.append(xp if cache_valid else None)
            if cache_valid:
                page_confs[p_idx] = [
                    float(m["raw_conf"]) for m in pdata.get("measures", [])
                ]

        piece_pages_sha = hashlib.sha256(
            "\n".join(piece_page_shas).encode("utf-8")
        ).hexdigest()
        assembled = concat_page_predictions(
            page_xmls, page_raw_confs=page_confs
        )
        out_xml = preds_dir / f"{gid}.musicxml"
        xml_sha: str | None = None
        if assembled.xml_text is not None:
            atomic_write_text(out_xml, assembled.xml_text)
            xml_sha = _sha256_file(out_xml)
        elif out_xml.is_symlink() or out_xml.is_file():
            out_xml.unlink(missing_ok=True)
        meta = {
            "gold_id": gid,
            "producer_git_commit": cur_commit,
            "producer_code_tree_sha256": cur_tree_sha,
            "producer_dirty": cur_dirty,
            "piece_pages_sha256": piece_pages_sha,
            "provenance_sha256": prov_sha,
            "open_reading_masks_sha256": manifest["open_reading_masks_sha256"],
            "assembled_xml_sha256": xml_sha,
            "parse_valid": assembled.parse_valid,
            "failed_pages": list(assembled.failed_pages),
            "pred_measure_count": len(assembled.pred_measure_pages),
            "pred_measure_pages": list(assembled.pred_measure_pages),
            "pred_measure_raw_conf": list(assembled.pred_measure_raw_conf),
            "error_reasons": assembled.error_reasons,
        }
        atomic_write_text(
            preds_dir / f"{gid}.meta.json", json.dumps(meta, indent=2) + "\n"
        )
        piece_summaries.append(meta)

    return {
        "pages_total": len(jobs),
        "pieces_assembled": len(piece_summaries),
        "pieces_valid": sum(1 for m in piece_summaries if m["parse_valid"]),
        "failed_pages_total": sum(
            len(list(m["failed_pages"]))  # type: ignore[arg-type]
            for m in piece_summaries
        ),
    }
