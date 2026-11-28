"""Fixture-only W6 checks: no network, staged corpus or private inputs."""

from __future__ import annotations

import hashlib
import inspect
import io
import json
import multiprocessing
import os
import shutil
import sys
import time
from concurrent.futures import ProcessPoolExecutor
from dataclasses import replace
from pathlib import Path
from types import SimpleNamespace
from typing import cast

import pytest
from PIL import Image, ImageDraw
from tokenizers import Tokenizer, models, pre_tokenizers

from sheet_music_omr.evaluation.xml_merge import concat_system_predictions
from sheet_music_omr.harness.confine import Confinement
from sheet_music_omr.harness.musescore import MuseScore
from sheet_music_omr.harness.stops import StopConditionError
from sheet_music_omr.harness.systems import (
    SystemLayout,
    SystemStart,
    split_score,
)
from sheet_music_omr.model import AssembledScore
from sheet_music_omr.musicxml_forward import to_musicxml_text
from sheet_music_omr.parser import parse_document
from sheet_music_omr.synthetic import intake, renderers
from sheet_music_omr.training import asset_locks, ledgers, packaging
from sheet_music_omr.training import copista_per_system as baseline
from sheet_music_omr.training import models as training_models
from sheet_music_omr.training.asset_locks import (
    LockedMetaFile,
    LockedModelRepo,
    LockedWheel,
)
from sheet_music_omr.writer import write_document

_WHEEL = "fixture-0.1-py3-none-any.whl"
_URL = f"https://example.invalid/{_WHEEL}"
_BUDGET = 15_000_000_000
_INVOCATION = {"run_id": "first", "git_commit": "fixture", "clean_tree": False}
_MANIFEST = "traindev-manifest.json"
_HEADER = (
    "format: text-score/2\nkey: 0\ntime: 4/4\nclefs: G2 F4\ncarry: octave\n"
)
_UNUSED_MUSESCORE = cast("MuseScore", None)


def _sha(data):
    return hashlib.sha256(data).hexdigest()


def _records(downloads):
    return [
        json.loads(line)
        for line in (downloads / ledgers.W6_RUN_LEDGER_NAME)
        .read_text()
        .splitlines()
    ]


_PRIOR_SETTLEMENTS = [
    {
        "event": "settle",
        "command": command,
        "status": status,
        "actual_pages": pages,
    }
    for command, status, pages in (
        ("s3-proof", "failed", 215),
        ("s3-proof", "failed", 295),
        ("s3-proof", "completed", 544),
        ("w5-traindev-69", "completed", 69),
        ("w5-locked-92", "completed", 92),
    )
]


def _seed_prior_ledgers(downloads, monkeypatch, *, init_campaign=True):
    downloads.mkdir(parents=True, exist_ok=True)
    (downloads.parent / "runs").mkdir(parents=True, exist_ok=True)
    for name, field_name, total in (
        ("download-ledger.json", "total", 2_350_276_855),
        ("w2-download-ledger.json", "cumulative_bytes", 480_830_451),
        ("w4-w5-download-ledger.json", "total_bytes", 341_067_483),
    ):
        (downloads / name).write_text(json.dumps({field_name: total}))
    (downloads / "w2-touch-log.jsonl").write_text("{}\n" * 6)
    (downloads / "w4-w5-run-ledger.jsonl").write_text(
        "".join(json.dumps(row) + "\n" for row in _PRIOR_SETTLEMENTS)
    )
    monkeypatch.setattr(
        ledgers,
        "PRIOR_CAMPAIGN_LEDGER_SHA256S",
        {
            name: _sha((downloads / name).read_bytes())
            for name in ledgers.PRIOR_CAMPAIGN_LEDGER_SHA256S
        },
    )
    dl_ledger = downloads / ledgers.W6_DOWNLOAD_LEDGER_NAME
    if init_campaign and not dl_ledger.exists():
        ledgers.W6DownloadLedger(downloads, allow_init=True).total_bytes()


@pytest.mark.parametrize(
    ("name", "replacement"),
    [
        ("download-ledger.json", '{"total":2350276854}'),
        ("download-ledger.json", '{"total":2350276855}\n'),
        ("w2-download-ledger.json", '{"cumulative_bytes":480830450}'),
        ("w4-w5-download-ledger.json", '{"total_bytes":341067482}'),
        ("w2-touch-log.jsonl", "{}\n" * 5 + "bad\n"),
        ("w2-touch-log.jsonl", "{}\n" * 5),
        ("w4-w5-run-ledger.jsonl", '{"event":"start"}\n'),
        (
            "w4-w5-run-ledger.jsonl",
            "\n".join(map(json.dumps, _PRIOR_SETTLEMENTS[1:])),
        ),
        (
            "w4-w5-run-ledger.jsonl",
            "\n".join(
                json.dumps(row | {"status": "failed"})
                for row in _PRIOR_SETTLEMENTS
            ),
        ),
    ],
)
def test_prior_campaign_ledgers_reject_fresh_and_truncated_roots(
    tmp_path, monkeypatch, name, replacement
):
    """Starts require intact history from every preceding campaign."""
    downloads = tmp_path / "downloads"
    downloads.mkdir()
    with pytest.raises(StopConditionError, match="missing prior campaign"):
        ledgers.verify_prior_campaign_ledgers(downloads)
    _seed_prior_ledgers(downloads, monkeypatch, init_campaign=False)
    ledgers.verify_prior_campaign_ledgers(downloads)
    (downloads / name).write_text(replacement)
    with pytest.raises(StopConditionError, match=name):
        ledgers.verify_prior_campaign_ledgers(downloads)


def test_relocated_downloads_preserves_ledgers_portably(tmp_path, monkeypatch):
    """Self-contained downloads and runs relocate across hosts cleanly."""
    downloads = tmp_path / "downloads"
    _seed_prior_ledgers(downloads, monkeypatch)
    entry = {
        "schema": "omr-w6-slice-a-download-ledger-v1",
        "total_bytes": 17,
        "entries": [{"charged_bytes": 17}],
    }
    (downloads / ledgers.W6_DOWNLOAD_LEDGER_NAME).write_text(json.dumps(entry))
    monkeypatch.setattr(ledgers, "W6_CUMULATIVE_PAGE_CAP", 2)
    ledgers.reserve_w6_render_run(
        downloads, **_INVOCATION, command="render", planned_pages=2
    )
    relocated = tmp_path / "rtx4090" / "downloads"
    shutil.copytree(downloads, relocated)
    (relocated.parent / "runs").mkdir()
    assert ledgers.W6DownloadLedger(relocated).total_bytes() == 17  # noqa: PLR2004
    with pytest.raises(StopConditionError, match="cumulative"):
        ledgers.reserve_w6_render_run(
            relocated, **_INVOCATION, command="render", planned_pages=1
        )


def test_download_reserves_before_transfer_and_reuses_verified_wheels(
    tmp_path,
    monkeypatch,
):
    """Reserve durable wire headroom; reconcile only after verification."""
    data = b"fixture wheel"
    downloads = tmp_path / "downloads"
    _seed_prior_ledgers(downloads, monkeypatch)
    ledger = ledgers.W6DownloadLedger(downloads)
    dest = downloads / "w6-wheels" / _WHEEL
    before = []

    def transfer(url, target, size, budget, wire):
        state = json.loads(ledger.path.read_bytes())
        before.append(state["entries"][0])
        assert state["total_bytes"] == budget > size == len(data)
        assert url == _URL
        target.write_bytes(data)
        wire[0] = len(data) + 71

    monkeypatch.setattr(ledgers, "_transfer_locked", transfer)
    args = {
        "url": _URL,
        "dest": dest,
        "expected_bytes": len(data),
        "expected_sha256": _sha(data),
    }
    assert ledger.ensure_cached_file(**args) == dest
    assert before[0]["status"] == "reserved"
    assert ledger.total_bytes() == len(data) + 71
    prior = downloads / "w4-wheels" / _WHEEL
    prior.parent.mkdir()
    dest.rename(prior)
    assert ledger.ensure_cached_file(**args) == prior
    assert ledger.total_bytes() == len(data) + 71
    assert len(before) == 1


@pytest.mark.parametrize("failure", ["unmetered", "truncated"])
def test_download_retains_failed_reservation_and_enforces_cap(
    tmp_path,
    monkeypatch,
    failure,
):
    """Failed or uncertain transfers stay charged and cannot spend twice."""
    downloads = tmp_path / "downloads"
    _seed_prior_ledgers(downloads, monkeypatch)
    ledger = ledgers.W6DownloadLedger(downloads)
    dest = downloads / "w6-wheels" / _WHEEL
    data = b"fixture wheel"
    called = []

    def transfer(_url, target, _size, budget, wire):
        called.append(budget)
        target.write_bytes(data if failure == "unmetered" else data[:2])
        if failure == "truncated":
            wire[0] = len(data) + 50

    monkeypatch.setattr(ledgers, "_transfer_locked", transfer)
    with pytest.raises(StopConditionError, match=r"unmetered|size or digest"):
        ledger.ensure_cached_file(
            url=_URL,
            dest=dest,
            expected_bytes=len(data),
            expected_sha256=_sha(data),
        )
    assert not dest.exists()
    retained = ledgers.W6DownloadLedger(downloads).total_bytes()
    assert retained == called[0]
    with pytest.raises(StopConditionError, match="15 GB"):
        ledger.ensure_cached_file(
            url=_URL,
            dest=dest,
            expected_bytes=_BUDGET - retained,
            expected_sha256=_sha(data),
        )
    assert called == [retained]


@pytest.mark.parametrize("name", ["custom.py", "encoder.bin", "a.safetensors"])
def test_download_rejects_code_and_weights_before_reserving(
    tmp_path,
    monkeypatch,
    name,
):
    """A forbidden hostname payload cannot reach transport or the ledger."""
    monkeypatch.setattr(
        ledgers,
        "_transfer_locked",
        lambda *_: pytest.fail("forbidden request reached transport"),
    )
    downloads = tmp_path / "downloads"
    _seed_prior_ledgers(downloads, monkeypatch)
    ledger = ledgers.W6DownloadLedger(downloads)
    with pytest.raises(StopConditionError, match="remote code or weights"):
        ledger.ensure_cached_file(
            url=f"https://example.invalid/{name}",
            dest=downloads / name,
            expected_bytes=1,
            expected_sha256=_sha(b"x"),
        )
    assert ledger.total_bytes() == 0


def test_setup_uses_verified_cache_and_installs_locked_wheels_offline(
    tmp_path,
    monkeypatch,
):
    """Provision exact local wheels without dependency resolution."""
    downloads = tmp_path / "downloads"
    _seed_prior_ledgers(downloads, monkeypatch, init_campaign=False)
    cached = downloads / "wheel-cache" / _WHEEL
    cached.parent.mkdir(parents=True)
    cached.write_bytes(b"fixture")
    wheel = LockedWheel("fixture", "0.1", _URL, 7, _sha(b"fixture"), "MIT")
    monkeypatch.setattr(ledgers, "resolve_host_w6_wheels", lambda: (wheel,))
    monkeypatch.setattr(ledgers, "W6_MODEL_METADATA_LOCKS", ())
    monkeypatch.setattr(ledgers, "repository_root", lambda: tmp_path / "code")
    monkeypatch.setattr(
        ledgers,
        "_transfer_locked",
        lambda *_: pytest.fail("verified cache unexpectedly reached network"),
    )
    commands = []

    def launch(command, *, env, **_kwargs):
        assert env["HF_HUB_OFFLINE"] == env["PIP_NO_INDEX"] == "1"
        commands.append(command)
        if "venv" in command:
            cfg = downloads / "w6-train-venv" / "pyvenv.cfg"
            cfg.parent.mkdir()
            cfg.touch()

    monkeypatch.setattr(ledgers.subprocess, "run", launch)
    monkeypatch.setattr(ledgers, "_venv_versions", lambda *_: {})
    with pytest.raises(StopConditionError, match="missing Slice A download"):
        ledgers.setup_w6_slice_a(downloads)
    assert (
        ledgers.setup_w6_slice_a(downloads, init_campaign=True)[
            "download_bytes"
        ]
        == 0
    )
    create, install = commands
    assert create[-2:] == ["venv", str(downloads / "w6-train-venv")]
    assert {"--no-index", "--no-deps", str(cached)} <= set(install)


def test_render_crash_and_aborted_start_consume_page_budget(
    tmp_path, monkeypatch
):
    """A nonformal run is charged before start and owes one page on abort."""
    downloads = tmp_path / "downloads"
    _seed_prior_ledgers(downloads, monkeypatch)

    def reserve(pages):
        return ledgers.reserve_w6_render_run(
            downloads,
            **_INVOCATION,
            command="render-fixture",
            planned_pages=pages,
        )

    with pytest.raises(StopConditionError, match="per-run"):
        reserve(0)
    with pytest.raises(StopConditionError, match="per-run"):
        reserve(15_001)
    reserve(15_000)
    reserve(15_000)
    aborted = reserve(5_000)
    assert _records(downloads)[0]["event"] == "started"
    with pytest.raises(StopConditionError, match="cumulative"):
        reserve(1)
    ledgers.settle_w6_run(
        downloads,
        aborted,
        status="aborted",
        actual_pages=0,
    )
    assert _records(downloads)[-1]["charged_pages"] == 1
    reserve(4_999)
    with pytest.raises(StopConditionError, match="cumulative"):
        reserve(1)


def test_disk_cap_covers_failed_and_current_runs(tmp_path, monkeypatch):
    """Disk accounting includes prior runs but not other campaigns."""
    monkeypatch.setattr(ledgers, "W6_DISK_CAP_BYTES", 10)
    downloads = tmp_path / "downloads"
    _seed_prior_ledgers(downloads, monkeypatch)
    runs = tmp_path / "runs"
    for label, amount in (
        ("w6-slice-a-old", 4),
        ("w6-slice-a-current", 6),
        ("w5-old", 15),
    ):
        folder = runs / label
        folder.mkdir()
        (folder / "results").write_bytes(b"x" * amount)
    assert ledgers.check_w6_disk_cap(downloads, runs) == 10  # noqa: PLR2004
    (runs / "w6-slice-a-current" / "results").write_bytes(b"x" * 7)
    with pytest.raises(StopConditionError, match="generated disk cap"):
        ledgers.reserve_w6_render_run(
            downloads,
            **_INVOCATION,
            command="render",
            planned_pages=1,
            runs_parent=runs,
        )


def test_packaging_rejects_noncanonical_runs_root(tmp_path, monkeypatch):
    """A caller cannot point disk accounting at a different directory."""
    downloads = tmp_path / "downloads"
    _seed_prior_ledgers(downloads, monkeypatch)
    alternate = tmp_path / "other-runs"
    run = alternate / "w6-slice-a-fixture"
    with pytest.raises(StopConditionError, match="canonical runs root"):
        ledgers.check_w6_disk_cap(downloads, alternate)
    with pytest.raises(StopConditionError, match="canonical runs root"):
        ledgers.reserve_w6_render_run(
            downloads,
            **_INVOCATION,
            command="render",
            planned_pages=1,
            runs_parent=alternate,
        )
    with pytest.raises(StopConditionError, match="canonical runs root"):
        packaging.run_expanded_w4a_and_package(
            downloads,
            run,
            lieder_manifest=tmp_path / "lieder.jsonl",
            test_exclusion=tmp_path / "exclusion.json",
            test_status=tmp_path / "status.json",
            musescore=_UNUSED_MUSESCORE,
        )
    assert not run.exists()
    assert not (downloads / ledgers.W6_RUN_LEDGER_NAME).exists()
    alternate.mkdir()
    (tmp_path / "runs").rmdir()
    (tmp_path / "runs").symlink_to(alternate, target_is_directory=True)
    with pytest.raises(StopConditionError, match="symlink"):
        ledgers.check_w6_disk_cap(downloads)


def _append_anchor(downloads, anchor):
    path = downloads / ledgers.W6_RUN_LEDGER_NAME
    downloads.mkdir(exist_ok=True)
    with path.open("a", encoding="utf-8") as file:
        file.write(
            json.dumps({"event": "traindev-root-anchor"} | anchor) + "\n"
        )


def test_copista_charges_every_private_invocation_using_latest_anchor(
    tmp_path, monkeypatch
):
    """The portable digest check and 10-call cap apply to all runs."""
    reserve = ledgers.reserve_w6_copista_invocation_and_read_anchor
    downloads = tmp_path / "downloads"
    _seed_prior_ledgers(downloads, monkeypatch)
    _append_anchor(downloads, {"portable_aggregate_sha256": _sha(b"old")})
    with pytest.raises(StopConditionError, match="anchor"):
        reserve(downloads, **_INVOCATION, command="debug")
    expected_sha = ledgers.EXPECTED_TRAINDEV_PORTABLE_SHA256
    _append_anchor(
        downloads,
        {"portable_aggregate_sha256": expected_sha},
    )
    for i in range(10):
        token, read_anchor = reserve(
            downloads, **_INVOCATION, command=f"debug-{i}"
        )
        assert read_anchor == {"portable_aggregate_sha256": expected_sha}
        if i % 2:
            ledgers.settle_w6_run(downloads, token, status="failed")
    with pytest.raises(StopConditionError, match="invocation cap"):
        reserve(downloads, **_INVOCATION, command="formal")
    started = sum(r["event"] == "started" for r in _records(downloads))
    assert started == 10  # noqa: PLR2004


def test_copista_reads_legacy_kind_anchor(tmp_path, monkeypatch):
    """A carried-forward kind-only anchor can still reserve an invocation."""
    downloads = tmp_path / "downloads"
    _seed_prior_ledgers(downloads, monkeypatch)
    anchor = {
        "kind": "traindev-root-anchor",
        "portable_aggregate_sha256": ledgers.EXPECTED_TRAINDEV_PORTABLE_SHA256,
    }
    (downloads / ledgers.W6_RUN_LEDGER_NAME).write_text(
        json.dumps(anchor) + "\n"
    )
    _, observed = ledgers.reserve_w6_copista_invocation_and_read_anchor(
        downloads, **_INVOCATION, command="debug"
    )
    assert observed == {
        "portable_aggregate_sha256": ledgers.EXPECTED_TRAINDEV_PORTABLE_SHA256
    }


@pytest.fixture
def sealed(tmp_path, monkeypatch):
    """Stage a portable 102-leaf TRAIN_DEV fixture with normal permissions."""
    worktree = tmp_path / "worktree"
    worktree.mkdir()
    root = tmp_path / "outside" / "sealed"
    root.mkdir(parents=True)
    rels: list[tuple[str, str]] = [("TRAIN_DEV", _MANIFEST)]
    for gid in sorted(ledgers.TRAINDEV_GOLD_IDS):
        rels.extend(
            (gid, f"pages/{gid}-p{page:02d}.png")
            for page in range(1, asset_locks.TRAINDEV_PAGE_COUNTS[gid] + 1)
        )
        rels.extend(
            (gid, f"gold/{gid}/{k}.musicxml") for k in ("accepted", "draft")
        )
    manifest_bytes = json.dumps(
        {
            "pieces": [
                {"gold_id": gid, "split": "TRAIN_DEV"}
                for gid in sorted(ledgers.TRAINDEV_GOLD_IDS)
            ]
        }
    ).encode()
    portable = []
    expected_rel = "pages/gold-07-p01.png"
    for gid, rel in rels:
        leaf = root / rel
        leaf.parent.mkdir(parents=True, exist_ok=True)
        data = manifest_bytes if rel == _MANIFEST else b"fixture payload"
        leaf.write_bytes(data)
        portable.append((rel, gid, _sha(data)))
    packed = json.dumps(
        {
            "schema": "omr-w6-traindev-portable-v1",
            "leaves": [
                {"gold_id": g, "relpath": rel, "expected_sha256": sha}
                for rel, g, sha in sorted(portable)
            ],
            "traindev_manifest_sha256": _sha(manifest_bytes),
        },
        sort_keys=True,
        separators=(",", ":"),
    ).encode()
    digest = _sha(packed)
    monkeypatch.setattr(ledgers, "EXPECTED_TRAINDEV_PORTABLE_SHA256", digest)
    anchor = {"portable_aggregate_sha256": digest}
    return root, anchor, worktree, expected_rel


def test_portable_traindev_root_relocates_and_rejects_symlinks_or_drift(
    tmp_path,
    sealed,
):
    """Portable TRAIN_DEV copies work across paths while blocking tampering."""
    root, anchor, worktree, rel = sealed
    alias = tmp_path / "symlink"
    alias.symlink_to(root.parent, target_is_directory=True)
    with pytest.raises(StopConditionError, match="symlink"):
        ledgers.open_verified_traindev_root(
            alias / root.name, anchor, [worktree]
        )
    copied = tmp_path / "rtx4090-copy" / "traindev-root"
    shutil.copytree(root, copied)
    with ledgers.open_verified_traindev_root(
        copied, anchor, [worktree]
    ) as verified:
        assert verified.read_verified_leaf_bytes(rel) == b"fixture payload"
        with pytest.raises(StopConditionError, match="allowlist"):
            verified.read_verified_leaf_bytes("pages/gold-01-p01.png")
    (copied / rel).write_bytes(b"tampered payload")
    with pytest.raises(StopConditionError, match="digest drift"):
        ledgers.open_verified_traindev_root(copied, anchor, [worktree])

    # Wrong 69-page distribution across 16 IDs is rejected before file reads.
    wrong_dist = tmp_path / "wrong-dist" / "traindev-root"
    shutil.copytree(root, wrong_dist)
    (wrong_dist / "pages/gold-07-p05.png").rename(
        wrong_dist / "pages/gold-08-p03.png"
    )
    with pytest.raises(StopConditionError, match="exact allowlist"):
        ledgers.open_verified_traindev_root(wrong_dist, anchor, [worktree])


def _public_mapping():
    return {
        "status": "/secret/status.json",
        "cache": "/secret/cache",
        "pieces": [
            {
                "id": f"gold-{i:02}",
                "workspace": f"Invented identity {i:02}",
                "build": f"Private Build {i:02}",
                "path": f"/secret/gold/piece-{i:02}.mxl",
                "sha256": _sha(f"private-{i}".encode()),
            }
            for i in range(1, 24)
        ],
    }


def test_public_report_scan_uses_all_private_identities(tmp_path):
    """The publication scanner sees identity records but never opens scores."""
    path = tmp_path / "mapping.json"
    mapping = _public_mapping()
    path.write_text(json.dumps(mapping))
    ledgers.scan_public_report("Aggregate score: 0.5.", path)
    for token in ("invented identity 22", mapping["pieces"][-1]["sha256"]):
        with pytest.raises(StopConditionError, match="private"):
            ledgers.scan_public_report(token, path)
    mapping["pieces"].pop()
    path.write_text(json.dumps(mapping))
    with pytest.raises(StopConditionError, match="full 23-piece"):
        ledgers.scan_public_report("Aggregate score: 0.5.", path)


@pytest.mark.parametrize("key", ["status", "path", "cache", "sha256"])
def test_public_report_scan_rejects_missing_or_blank_identity(tmp_path, key):
    """A missing field cannot silently shrink the publication scan."""
    path = tmp_path / "mapping.json"
    mapping = _public_mapping()
    record = mapping if key in {"status", "cache"} else mapping["pieces"][-1]
    del record[key]
    for value in (None, " "):
        if value is not None:
            record[key] = value
        path.write_text(json.dumps(mapping))
        with pytest.raises(StopConditionError, match=key):
            ledgers.scan_public_report("Aggregate score: 0.5.", path)


def test_baseline_charges_slot_before_root_access(
    tmp_path,
    monkeypatch,
):
    """Orchestration must charge a slot before consulting the private root."""
    downloads = tmp_path / "downloads"
    _seed_prior_ledgers(downloads, monkeypatch)
    run = tmp_path / "runs" / "w6-slice-a-fixture"
    private = tmp_path / "private"
    monkeypatch.setattr(baseline, "_worktrees", lambda: (tmp_path / "code",))
    monkeypatch.setattr(baseline, "verify_w6_live_assets", lambda _: {})
    opened = []

    def open_private(root, _anchor, _worktrees):
        opened.append(root)
        assert _records(downloads)[-1]["event"] == "started"
        msg = "synthetic root mismatch"
        raise StopConditionError(msg)

    monkeypatch.setattr(baseline, "open_verified_traindev_root", open_private)
    with pytest.raises(StopConditionError, match="synthetic root mismatch"):
        baseline.run_copista_per_system(
            downloads,
            run,
            traindev_root=private,
            formal=False,
        )
    assert opened == [private]
    assert _records(downloads)[-1]["status"] == "failed"


def _source(source_id, composer, title):
    return {
        "source_id": source_id,
        "composer": composer,
        "title": title,
        "measure_count": 8,
        "canonical_v2_sha256": _sha(source_id.encode()),
    }


def _metadata_gate(tmp_path, monkeypatch, titles=()):
    status_path = tmp_path / "batch-status.json"
    exclusion_path = tmp_path / "test-exclusion.json"
    pieces, status = [], []
    for idx, gid in enumerate(sorted(intake.TEST_GOLD_IDS)):
        i = int(gid.removeprefix("gold-"))
        pieces.append(
            {
                "gold_id": gid,
                "accepted_xml_sha256": _sha(f"xml-{gid}".encode()),
                "facsimile_pdf_sha256": _sha(f"pdf-{gid}".encode()),
            }
        )
        name = (
            titles[idx]
            if idx < len(titles)
            else f"Tchaikovsky - Album Op. {38 + i}"
        )
        status.append(
            {
                "number": i,
                "accepted": True,
                "workspace": f"{i:02} - {name}",
            }
        )
    status_path.write_text(json.dumps({"batches": status}))
    exclusion_path.write_text(
        json.dumps(
            {
                "schema": "omr-test-exclusion-v1",
                "test_piece_count": len(pieces),
                "pieces": pieces,
            }
        )
    )
    monkeypatch.setattr(
        intake,
        "EXPECTED_TEST_EXCLUSION_SHA256",
        _sha(exclusion_path.read_bytes()),
    )
    return packaging.MetadataOnlyTestExclusion(
        exclusion_path, status_path
    ), pieces


def _split_controls():
    return [
        _source(f"control-{composer}", composer, "Rondo")
        for composer in (
            "Bach",
            "Brahms",
            "Debussy",
            "Grieg",
            "Liszt",
            "Ravel",
            "Schubert",
            "Schumann",
            "Scriabin",
            "Vivaldi",
        )
    ]


def test_transliteration_is_shared_by_test_exclusion_and_split(
    tmp_path, monkeypatch
):
    """A distinct encoding of an aliased TEST work cannot enter intake."""
    gate, pieces = _metadata_gate(tmp_path, monkeypatch)
    alias = _source("alias-copy", "Tschaikowsky", "Children's Album Op. 39")
    frozen_hash = pieces[0]["accepted_xml_sha256"]
    assert alias["canonical_v2_sha256"] != frozen_hash
    assert (
        gate.exclusion_reason(
            alias["composer"],
            alias["title"],
            digests=(alias["canonical_v2_sha256"],),
        )
        == "excluded_test_match"
    )
    with pytest.raises(StopConditionError, match="excluded_test_match"):
        gate.audit_admitted_manifest([alias])
    controls = [
        _source("original", "Tchaikovsky", "Winter Visions Op. 52"),
        _source("edition", "Tschaikowsky", "Snowy Dreams Op. 52"),
        _source("fixture", "Chopin", "Sonata Op. 2"),
        *_split_controls(),
    ]
    train, val, _ = packaging.split_admitted_sources_by_composer(
        controls,
        min_val_works=10,
    )
    by_id = {r["source_id"]: r for r in train + val}
    for key in ("split", "work_group_id"):
        assert by_id["original"][key] == by_id["edition"][key]
    assert by_id["edition"]["canonical_composer"] == "tchaikovsky"


def test_catalogue_format_matches_test_edition_but_preserves_hoboken_series(
    tmp_path, monkeypatch
):
    """Catalogue formatting changes cannot evade the metadata-only gate."""
    moonlight = "Moonlight Sonata, Op. 27 No. 2"
    translated = "Mondscheinsonate, Op. 27/2"
    sonata = "Sonata, Hob. XVI:52"
    trio = "Trio, Hob. XV:52"
    gate, pieces = _metadata_gate(
        tmp_path, monkeypatch, (f"Beethoven - {moonlight}", f"Haydn - {sonata}")
    )
    original = intake.normalize_work_identity("Beethoven", moonlight)
    edition = intake.normalize_work_identity("Beethoven", translated)
    assert original is not None
    assert edition is not None
    assert original.matches(edition)
    digest = _sha(b"distinct edition")
    assert digest != pieces[0]["accepted_xml_sha256"]
    assert not gate.matches_digest(digests=(digest,))
    assert gate.exclusion_reason(
        "Beethoven", translated, digests=(digest,)
    ) == ("excluded_test_match")
    first = intake.normalize_work_identity("Haydn", sonata)
    second = intake.normalize_work_identity("Haydn", trio)
    assert first is not None
    assert second is not None
    assert not first.matches(second)
    assert gate.exclusion_reason("Haydn", sonata) == "excluded_test_match"
    assert gate.exclusion_reason("Haydn", trio, digests=(digest,)) is None


@pytest.mark.parametrize(
    ("composer", "first", "second", "same_work"),
    [
        ("Haydn", "Sonata, Hob. XV:52", "Sonata, Hob. XVI:52", False),
        ("Beethoven", "Sonata, Op. 27", "Mondscheinsonate, Op. 27/2", True),
        ("Beethoven", "Sonata, Op. 27 No. 1", "Sonata, Op. 27 No. 2", False),
        ("Beethoven", "Piano Sonata No. 8", "Piano Sonata No. 9", False),
    ],
)
def test_catalogue_conflicts_override_titles_but_missing_subnumbers_match(
    composer, first, second, same_work
):
    """Prevent same-title collisions without weakening TEST exclusion."""
    left = intake.normalize_work_identity(composer, first)
    right = intake.normalize_work_identity(composer, second)
    assert left is not None
    assert right is not None
    assert left.matches(right) is same_work
    assert right.matches(left) is same_work


def test_split_keeps_composer_aliases_together_without_catalogue_collision():
    """Composer groups survive translation and avoid cross-composer matches."""
    scores = [
        _source("moon", "Beethoven", "Moonlight Sonata"),
        _source("mond", "Beethoven", "Mondscheinsonate"),
        _source("bee2", "Beethoven", "Sonata Op. 2"),
        _source("cho2", "Chopin", "Rondo Op. 2"),
        _source("skat", "Scarlatti", "Sonata K. 1"),
        _source("slon", "Scarlatti", "Sonata L. 366"),
        _source("mozk", "Mozart", "Minuet K. 1"),
        *_split_controls(),
    ]
    partition = packaging.split_admitted_sources_by_composer(
        scores, min_val_works=10
    )
    assert (
        packaging.split_admitted_sources_by_composer(
            list(reversed(scores)), min_val_works=10
        )
        == partition
    )
    train, val, _ = partition
    by_id = {r["source_id"]: r for r in train + val}
    assert by_id["moon"]["split"] == by_id["mond"]["split"]
    assert by_id["skat"]["split"] == by_id["slon"]["split"]
    assert by_id["skat"]["canonical_composer"] == "scarlatti"
    assert by_id["skat"]["work_group_id"] != by_id["mozk"]["work_group_id"]
    assert by_id["bee2"]["work_group_id"] != by_id["cho2"]["work_group_id"]


@pytest.mark.parametrize(
    ("composer", "title"),
    [
        ("Beethoven", "Hob. XVI:52"),
        ("Anonymous", "Sonata Op. 2"),
    ],
)
def test_split_rejects_misattributed_or_uninformative_rows(composer, title):
    """Do not put contradictory or anonymous metadata in either partition."""
    scores = [
        _source("bad", composer, title),
        _source("good", "Mozart", "Minuet K. 1"),
    ]
    with pytest.raises(
        StopConditionError, match="uninformative or contradictory"
    ):
        packaging.split_admitted_sources_by_composer(scores, min_val_works=10)


def test_split_minimum_fails_closed_even_with_many_editions():
    """A large edition count cannot satisfy a distinct-work floor."""
    scores = [
        *[_source(f"edition-{i}", "Chopin", "Sonata Op. 2") for i in range(8)],
        _source("second", "Mozart", "Minuet K. 1"),
    ]
    with pytest.raises(StopConditionError, match="at least 10"):
        packaging.split_admitted_sources_by_composer(scores, min_val_works=9)
    with pytest.raises(StopConditionError, match="cannot reach"):
        packaging.split_admitted_sources_by_composer(scores, min_val_works=10)


def test_wheel_lock_rejects_license_outside_allowlist(monkeypatch):
    """A non-denied license must still be explicitly permitted."""
    wheel = replace(
        asset_locks.W6_LINUX_X86_64_WHEELS[0], license_id="CC-BY-4.0"
    )
    monkeypatch.setattr(asset_locks, "W6_LINUX_X86_64_WHEELS", (wheel,))
    with pytest.raises(StopConditionError, match="unpermitted wheel license"):
        asset_locks.verify_lock_licenses()


def _score(measures=2, *, changed_meter=False):
    text = _HEADER
    for i in range(1, measures + 1):
        if changed_meter and i == 2:  # noqa: PLR2004
            text += f"m {i} time=3/4\n"
        else:
            text += f"m {i}\n"
        duration = "2." if changed_meter and i >= 2 else "1"  # noqa: PLR2004
        text += f"1.1: C5/{duration}\n2.1: C3/{duration}\n"
    result = parse_document(text)
    assert isinstance(result, AssembledScore)
    return result


def test_verovio_routing_and_mask_keep_header_clefs_enabled():
    """Only initial clefs are safe for Verovio; gate other disabled tokens."""
    score = _score()
    xml = to_musicxml_text(score)
    assert packaging.choose_engine_for_score(score, xml) == "verovio"
    assert (
        packaging.choose_engine_for_score(
            score, xml, preferred_engine="musescore"
        )
        == "musescore"
    )
    changed = xml.replace(
        '<measure number="2">',
        '<measure number="2"><attributes><clef number="1">'
        "<sign>F</sign><line>4</line></clef></attributes>",
    )
    assert changed != xml
    assert packaging.choose_engine_for_score(score, changed) == "musescore"
    tremolo = parse_document(
        _HEADER + "m 1\n1.1: C5/1[tremolo=single:3]\n2.1: C3/1\n"
    )
    assert isinstance(tremolo, AssembledScore)
    assert (
        packaging.choose_engine_for_score(tremolo, to_musicxml_text(tremolo))
        == "musescore"
    )
    for category in ("cross_staff", "grace", "hairpin", "ottava"):
        assert (
            packaging.choose_engine_for_score(
                score, xml, present_categories=[category]
            )
            == "musescore"
        )
    verovio = packaging.category_supervision_mask("verovio")
    musescore = packaging.category_supervision_mask("musescore")
    assert len(verovio) == len(musescore) == 27  # noqa: PLR2004
    assert {key for key, enabled in verovio.items() if not enabled} == {
        "clef",
        "cross_staff",
        "grace",
        "hairpin",
        "ottava",
    }
    assert all(musescore.values())


def _sequence(variant, split, count):
    return [
        {
            "variant_id": variant,
            "split": split,
            "expected_systems": count,
            "system_index": i,
            "is_final_system": i == count - 1,
        }
        for i in range(count)
    ]


def test_prefix_takes_whole_variants_in_both_splits_and_checks_full_input():
    """The first quota-crossing variant is never sliced mid-piece."""
    train = _sequence("taa", "synth_train", 3)
    val = _sequence("vaa", "synth_val", 2)
    extra = _sequence("tab", "synth_train", 2)
    result = packaging.slice_prefix_milestone([*train, *val, *extra], 2, 1)
    assert result == [*train, *val]
    packaging.verify_complete_variant_sequences(result)
    for incomplete in (train[1:], [*train, *val[:1]], [*train, *val, extra[0]]):
        with pytest.raises(StopConditionError, match="incomplete"):
            packaging.verify_complete_variant_sequences(incomplete)
        with pytest.raises(StopConditionError, match="incomplete"):
            packaging.slice_prefix_milestone(incomplete, 2, 1)


def _rendered_fixture(variant_id, split):
    score = _score(3, changed_meter=True)
    starts = (SystemStart(0, 1, 1), SystemStart(2, 1, 2))
    docs = split_score(score, starts)
    targets = tuple(map(write_document, docs))
    boxes = [(12, 12, 212, 125), (12, 148, 212, 261)]
    image = Image.new("RGB", (240, 280), "white")
    draw = ImageDraw.Draw(image)
    for _, top, _, _ in boxes:
        for staff in (top + 8, top + 64):
            for line in range(5):
                y = staff + line * 8
                draw.line((28, y, 195, y), fill="black", width=1)
    raw = io.BytesIO()
    image.save(raw, "PNG")
    systems = (
        renderers.RenderedSystem(1, 1, 1, 2, boxes[0], 8.0),
        renderers.RenderedSystem(1, 2, 3, 3, boxes[1], 8.0),
    )
    measures = (
        renderers.RenderedMeasure(1, (12, 12, 112, 125)),
        renderers.RenderedMeasure(2, (112, 12, 212, 125)),
        renderers.RenderedMeasure(3, boxes[1]),
    )
    page = renderers.RenderedPage(
        raw.getvalue(),
        renderers.PageGeometry(240, 280, systems, measures),
    )
    return packaging.RenderedVariant(
        variant_id,
        {
            "source_id": f"example-{variant_id}",
            "work_group_id": variant_id,
            "rights_variant": "permissive",
            "split": split,
            "measure_count": 3,
            "canonical_composer": f"composer-{variant_id}",
        },
        42,
        "G1",
        renderers.RenderResult(
            "musescore",
            renderers.RenderGate("passed"),
            (page,),
            targets,
        ),
    )


def _stage_verovio_fixture(
    xml, _style, _downloads, _scratch, _ledger, *, count_rasters, **_options
):
    assert xml.count("<measure ") == 3  # noqa: PLR2004
    count_rasters(1)
    return renderers.StagedVerovio(
        xml, ("m1", "m2", "m3"), frozenset(), "<mei/>", ()
    )


def _verify_verovio_fixture(_staged, _ledger):
    return replace(
        _rendered_fixture("forkserver", "synth_train").render, engine="verovio"
    )


def _render_verovio_fixture(*args, count_rasters, **options):
    staged = _stage_verovio_fixture(
        *args, count_rasters=count_rasters, **options
    )
    return _verify_verovio_fixture(staged, args[4])


def _init_forkserver_verovio_fixture():
    packaging._offline_worker()  # noqa: SLF001
    assert multiprocessing.get_start_method() == "forkserver"
    assert all(
        os.environ[key] == value
        for key, value in ledgers.W6_OFFLINE_ENV.items()
    )
    pytest.MonkeyPatch().setattr(
        renderers, "verify_verovio_outputs", _verify_verovio_fixture
    )


@pytest.mark.parametrize("musescore_page_cap", [0, None])
def test_forkserver_render_variants_match_serial_artifacts(
    tmp_path, monkeypatch, request, musescore_page_cap
):
    """Round-trip variant plans and packaged crops across forkserver IPC."""
    # Pytest's importlib mode does not put its root on the child import path.
    monkeypatch.syspath_prepend(str(request.config.rootpath))
    tremolo = parse_document(
        _HEADER + "m 1\n1.1: C5/1[tremolo=single:3]\n2.1: C3/1\n"
    )
    assert isinstance(tremolo, AssembledScore)
    rows = []
    for source_id, score in (
        ("musescore-only", tremolo),
        ("verovio-safe", _score(3, changed_meter=True)),
    ):
        path = tmp_path / f"{source_id}.txt"
        raw = write_document(score).encode()
        path.write_bytes(raw)
        rows.append(
            {
                "source_id": source_id,
                "work_group_id": source_id,
                "canonical_composer": "fixture",
                "canonical_root": str(tmp_path),
                "canonical_v2_path": path.name,
                "canonical_v2_sha256": _sha(raw),
                "measure_count": len(score.blocks),
                "rights_variant": "permissive",
                "split": "synth_train",
            }
        )

    def drop_musescore(*_args, count_rasters, **_options):
        count_rasters(1)
        return renderers.RenderResult(
            "musescore", renderers.RenderGate("dropped")
        )

    monkeypatch.setattr(renderers, "render_musescore", drop_musescore)

    def render(workers):
        run = tmp_path / f"workers-{workers}"
        meter = packaging._PageMeter(16)  # noqa: SLF001
        meter.disk = packaging._DatasetSink(run)  # noqa: SLF001
        variants = []
        for variant in packaging._render_variants(  # noqa: SLF001
            rows,
            run=run,
            downloads=tmp_path / "downloads",
            musescore=_UNUSED_MUSESCORE,
            meter=meter,
            calibrated_identity=None,
            targets={"synth_train": 2},
            verovio_workers=workers,
            musescore_page_cap=musescore_page_cap,
        ):
            variants.append(variant)
            # The dataset consumer advances the crop quota after each yield.
            meter.crop_counts["synth_train"] += 2
        (variant,) = variants
        assert variant.source["source_id"] == "verovio-safe"
        assert meter.actual == (1 if musescore_page_cap == 0 else 2)
        assert meter.gates["verovio"].outcomes == {"passed": 1}
        assert meter.gates["musescore"].outcomes == (
            {} if musescore_page_cap == 0 else {"dropped": 1}
        )
        return variant

    with monkeypatch.context() as serial:
        serial.setattr(renderers, "render_verovio", _render_verovio_fixture)
        expected = packaging._variant_rows(render(1))  # noqa: SLF001

    def forbid_native(*_args, **_options):
        pytest.fail("the fixture must not run native Verovio")

    # Keep parent render_verovio identity intact so production selects its pool.
    monkeypatch.setattr(renderers, "_run_verovio", forbid_native)
    monkeypatch.setattr(
        renderers, "stage_verovio_subprocess", _stage_verovio_fixture
    )

    def fixture_pool(workers):
        return ProcessPoolExecutor(
            max_workers=workers,
            mp_context=multiprocessing.get_context("forkserver"),
            initializer=_init_forkserver_verovio_fixture,
        )

    monkeypatch.setattr(packaging, "_process_pool", fixture_pool)
    variant = render(2)
    assert variant.render.pages == ()
    assert variant.render.system_texts == ()
    assert variant.prepackaged is not None
    crops, pages, pngs = variant.prepackaged
    serial_crops, serial_pages, serial_pngs = expected
    assert [row["source_measure_ordinals"] for row in crops] == [(1, 2), (3, 3)]
    assert len(pages) == 1
    assert len(pngs) == 3  # noqa: PLR2004
    assert pngs == serial_pngs
    jsonl = packaging._jsonl  # noqa: SLF001
    assert jsonl(crops) == jsonl(serial_crops)
    assert jsonl(pages) == jsonl(serial_pages)


def test_forkserver_intake_stops_at_same_archive_entry_as_serial(
    tmp_path, monkeypatch
):
    """Archive-order exclusions and dedup do not count prefetched entries."""
    gate, _ = _metadata_gate(tmp_path, monkeypatch)
    run = tmp_path / "w6-slice-a-intake"
    manifest = tmp_path / "lieder.jsonl"
    manifest.write_text("")
    monkeypatch.setattr(packaging, "_LIEDER_COUNT", 0)
    checked_after_cutoff = []

    def candidate(name, raw, *, decode=None):
        return intake.SourceCandidate(
            f"{name}.mxl",
            "PDMX",
            "permissive",
            "CC0-1.0",
            raw,
            composer="Fixture Composer",
            title=f"Fixture {name}",
            decode=decode,
        )

    def delayed(raw):
        checked_after_cutoff.append(True)
        return raw

    def forbidden(_raw):
        pytest.fail("the raw TEST digest must be rejected before decoding")

    first = to_musicxml_text(_score(8)).encode()
    second = to_musicxml_text(_score(9)).encode()
    candidates = [
        candidate("excluded", b"xml-gold-01", decode=forbidden),
        candidate("first", first),
        candidate("duplicate", first + b"\n"),
        candidate("unreadable", b"not XML"),
        candidate("second", second),
        candidate("cutoff", second, decode=delayed),
    ]
    monkeypatch.setattr(
        intake, "iter_pdmx_candidates", lambda _downloads: iter(candidates)
    )

    def artifacts(workers):
        rows, summary = packaging._expanded_intake(  # noqa: SLF001
            run, tmp_path, manifest, gate, 2, workers=workers
        )
        packaging._record_intake(  # noqa: SLF001
            run, tmp_path, rows, summary, formal=False
        )
        return {
            path.relative_to(run): path.read_bytes()
            for path in run.rglob("*")
            if path.is_file()
        }

    serial = artifacts(1)
    assert not checked_after_cutoff
    assert artifacts(2) == serial
    assert checked_after_cutoff == [True]
    summary = json.loads(
        serial[Path("synthetic/permissive/intake-summary.json")]
    )
    assert summary["pdmx"] == {
        "eligible_pdmx_candidates_seen": 5,
        "excluded_test_match": 1,
        "admitted": 2,
        "duplicate_canonical": 1,
        "unreadable_xml": 1,
    }


def test_packaging_writes_crops_and_cowarped_boxes_with_prior_header(
    tmp_path, monkeypatch
):
    """Verify whole shards, gap-free header state and geometry against PNGs."""
    _seed_prior_ledgers(tmp_path / "downloads", monkeypatch)
    run = tmp_path / "runs" / "w6-slice-a-fixture"
    train_variant = _rendered_fixture("tr", "synth_train")
    layout = SystemLayout(
        measures=3, starts=(SystemStart(0, 1, 1), SystemStart(2, 1, 2))
    )
    labels = renderers._system_texts(  # noqa: SLF001 - real label producer, no rasterizer
        to_musicxml_text(_score(3, changed_meter=True)), layout
    )
    packaging.package_rendered_variants_to_datasets(
        [
            replace(
                train_variant,
                render=replace(train_variant.render, system_texts=labels),
            ),
            _rendered_fixture("va", "synth_val"),
        ],
        run,
        target_train_crops=1,
        target_val_crops=1,
        prefix_train_crops=1,
        prefix_val_crops=1,
    )
    root = run / "datasets"
    assert not (root / "w6-crops-20k").exists()
    assert not (root / "w6-crops-50k").exists()
    train = root / "fixture-crops-prefix/synth_train"
    first, second = map(
        json.loads, (train / "crops.jsonl").read_text().splitlines()
    )
    tokenizer = training_models.TextScore2DomainTokenizer()
    for row, expected in zip(
        (first, second), train_variant.render.system_texts, strict=True
    ):
        assert row["target_text_v2"] == expected
        ids, prompt_length = training_models._arm_b_train_sequence(  # noqa: SLF001 - before GPU work
            tokenizer, row
        )
        assert ids[-1] == tokenizer.eos_token_id
        assert tokenizer.decode(ids[prompt_length:-1]) == expected
    assert first["schema"] == "omr-w6-crop-dataset-v1"
    assert first["incoming_header_state_text"] == ""
    assert (
        second["incoming_header_state_text"]
        == "key: 0\ntime: 3/4\nclefs: G2 F4"
    )
    assert json.loads(second["carry_in_text"])["measure"] == 3  # noqa: PLR2004
    assert second["page_system_tuple"] == [1, 2]
    assert second["is_final_system"]
    crop = root / second["crop_relpath"]
    assert second["sha256"] == _sha(crop.read_bytes())
    with Image.open(crop) as img:
        assert img.size == (second["crop_width"], second["crop_height"])
    page = json.loads((train / "page-boxes.jsonl").read_bytes())
    assert page["schema"] == "omr-w6-page-boxes-v1"
    assert (page["page_width_px"], page["page_height_px"]) == (240, 280)
    assert [row["class_id"] for row in page["boxes"]] == [0, 0, 1, 1, 1]
    x0, y0, x1, y1 = page["boxes"][0]["box_px"]
    assert page["boxes"][0]["box_normalized"] == pytest.approx(
        [(x1 + x0) / 480, (y1 + y0) / 560, (x1 - x0) / 240, (y1 - y0) / 280]
    )
    summary = json.loads(
        (root / "fixture-crops-full/summary.json").read_bytes()
    )
    assert summary["synth_train"]["source_measure_repetition"] == {
        "p50": 1.0,
        "p90": 1.0,
        "p99": 1.0,
        "max": 1.0,
    }


def test_standalone_packaging_only_publishes_fixture_names(
    tmp_path, monkeypatch
):
    """Even a quota-sized fixture cannot publish under formal names."""
    _seed_prior_ledgers(tmp_path / "downloads", monkeypatch)
    monkeypatch.setattr(packaging, "_MILESTONE_MINIMA", (1, 1, 2, 2))
    run = tmp_path / "runs" / "w6-slice-a-names"
    expected = {"fixture-crops-prefix", "fixture-crops-full"}
    assert (
        "formal"
        not in inspect.signature(
            packaging.package_rendered_variants_to_datasets
        ).parameters
    )
    result = packaging.package_rendered_variants_to_datasets(
        [
            _rendered_fixture("tr", "synth_train"),
            _rendered_fixture("va", "synth_val"),
        ],
        run,
        target_train_crops=2,
        target_val_crops=2,
        prefix_train_crops=1,
        prefix_val_crops=1,
    )
    assert result.keys() == expected
    assert {
        summary.parent.name
        for summary in (run / "datasets").glob("*/summary.json")
    } == expected


@pytest.mark.parametrize(
    "reduced",
    [
        {"min_val_works": 149},
        {"target_train_crops": 44_999},
        {"target_val_crops": 4_999},
        {"prefix_train_crops": 17_999},
        {"prefix_val_crops": 1_999},
        {"max_pdmx": 2_439},
    ],
)
def test_formal_packaging_rejects_reduced_milestones(
    tmp_path, monkeypatch, reduced
):
    """Formal minima are checked before any reservation or renderer work."""
    downloads = tmp_path / "downloads"
    _seed_prior_ledgers(downloads, monkeypatch)
    run = tmp_path / "runs" / "w6-slice-a-fixture"
    with pytest.raises(
        StopConditionError, match="formal Slice A milestone minima"
    ):
        packaging.run_expanded_w4a_and_package(
            downloads,
            run,
            lieder_manifest=tmp_path / "lieder.jsonl",
            test_exclusion=tmp_path / "exclusion.json",
            test_status=tmp_path / "status.json",
            musescore=_UNUSED_MUSESCORE,
            **reduced,
        )
    assert not run.exists()
    assert not (downloads / ledgers.W6_RUN_LEDGER_NAME).exists()


def _render_inputs(tmp_path, monkeypatch, engine, count):
    score = _score()
    monkeypatch.setattr(packaging, "_source_score", lambda *_: score)
    monkeypatch.setattr(
        packaging, "choose_engine_for_score", lambda *_a, **_k: engine
    )
    monkeypatch.setattr(
        packaging.injector,
        "inject_annotations",
        lambda *_a, seed, **_k: SimpleNamespace(
            score=score, musicxml=seed.encode()
        ),
    )
    return [
        _source(f"fixture-{i}", "Beethoven", "Sonata")
        | {"canonical_root": str(tmp_path), "split": "synth_train"}
        for i in range(count)
    ]


@pytest.fixture
def packaging_run(tmp_path, monkeypatch):
    """Run real reservations and packaging without live assets or corpus."""
    downloads = tmp_path / "downloads"
    _seed_prior_ledgers(downloads, monkeypatch)
    run = tmp_path / "runs" / "w6-slice-a-fixture"
    executable = tmp_path / "fake-musescore"
    executable.write_bytes(b"fixture")
    musescore = MuseScore(
        executable,
        "4.7.7",
        _sha(b"fixture"),
        tmp_path / "settings",
        Confinement(run, downloads),
        None,
        {},
    )
    monkeypatch.setattr(packaging, "repository_root", lambda: tmp_path / "code")
    monkeypatch.setattr(
        packaging, "source_state", lambda: {"commit": "fixture", "dirty": False}
    )
    monkeypatch.setattr(
        renderers,
        "SOURCE_VERIFIED_MUSESCORE_BUILDS",
        {(musescore.version, musescore.executable_sha256)},
    )
    monkeypatch.setattr(packaging, "verify_w6_training_assets", lambda _: {})
    monkeypatch.setattr(
        packaging, "verify_w4_w5_live_assets", lambda *_a, **_k: {}
    )
    monkeypatch.setattr(packaging, "MetadataOnlyTestExclusion", lambda *_: None)
    monkeypatch.setattr(packaging, "_pinned_lengths", lambda *_: ({}, {}))

    def execute(
        rows,
        *,
        formal=False,
        admission_limit_reached=False,
        crop_targets=(1, 1, 2, 2),
    ):
        prefix_train, prefix_val, target_train, target_val = crop_targets
        monkeypatch.setattr(
            packaging,
            "_expanded_intake",
            lambda *_, **_options: (
                rows,
                {"pdmx_admission_limit_reached": admission_limit_reached},
            ),
        )
        monkeypatch.setattr(
            packaging,
            "split_admitted_sources_by_composer",
            lambda source_rows, **_: (
                [row for row in source_rows if row["split"] == "synth_train"],
                [row for row in source_rows if row["split"] == "synth_val"],
                {},
            ),
        )
        return packaging.run_expanded_w4a_and_package(
            downloads,
            run,
            lieder_manifest=tmp_path / "lieder.jsonl",
            test_exclusion=tmp_path / "exclusion.json",
            test_status=tmp_path / "status.json",
            musescore=musescore,
            formal=formal,
            planned_pages=40,
            verovio_workers=2,
            prefix_train_crops=prefix_train,
            prefix_val_crops=prefix_val,
            target_train_crops=target_train,
            target_val_crops=target_val,
        )

    return downloads, run, execute


def _packaging_rows(tmp_path, monkeypatch, splits):
    inputs = _render_inputs(tmp_path, monkeypatch, "verovio", len(splits))
    rendered = {}
    for row, split in zip(inputs, splits, strict=True):
        fixture = _rendered_fixture(row["source_id"], split)
        row.update(fixture.source)
        rendered[row["canonical_v2_sha256"]] = fixture.render
    return inputs, rendered


def test_batch_two_interruption_charges_full_render_reservation(
    tmp_path, monkeypatch, packaging_run
):
    """Uncollected second-batch pages cannot be refunded after batch one."""
    downloads, run, execute = packaging_run
    rows, rendered = _packaging_rows(
        tmp_path, monkeypatch, ("synth_train", "synth_val") * 2
    )
    interrupted = rows[2]["canonical_v2_sha256"]

    def render(
        xml, _style, _downloads, _scratch, _ledger, *, count_rasters, **_
    ):
        digest = xml.rsplit(":", 1)[-1]
        count_rasters(1)
        if digest == interrupted:
            assert next(run.rglob("*.png"), None) is not None
            raise KeyboardInterrupt
        return replace(rendered[digest], engine="verovio")

    monkeypatch.setattr(renderers, "render_verovio", render)
    with pytest.raises(KeyboardInterrupt):
        execute(rows, crop_targets=(1, 1, 4, 4))
    start, settlement = _records(downloads)
    assert settlement["status"] == "failed"
    assert settlement["charged_pages"] == start["reserved_pages"]
    assert not (run / "datasets").exists()


def test_final_small_verovio_cohort_blocks_dataset_publication(
    tmp_path, monkeypatch, packaging_run
):
    """The last drop counts even when the preceding outcome reaches quota."""
    downloads, run, execute = packaging_run
    rows, rendered = _packaging_rows(
        tmp_path,
        monkeypatch,
        ("synth_train", "synth_val", "synth_val", "synth_val"),
    )
    train, first_val, _, last_val = [row["canonical_v2_sha256"] for row in rows]

    def engine(_score, xml, **_):
        return (
            "musescore"
            if xml.decode().rsplit(":", 1)[-1] in {train, first_val}
            else "verovio"
        )

    def render(xml, _style, _context, _scratch, _ledger, *, count_rasters, **_):
        digest = xml.rsplit(":", 1)[-1]
        count_rasters(1)
        if digest == first_val:
            return renderers.RenderResult(
                "musescore", renderers.RenderGate("dropped")
            )
        if digest == last_val:
            return renderers.RenderResult(
                "verovio", renderers.RenderGate("dropped", {"pitch": 1})
            )
        return replace(
            rendered[digest],
            engine="musescore" if digest == train else "verovio",
            categories=frozenset({"pitch"}),
        )

    monkeypatch.setattr(packaging, "choose_engine_for_score", engine)
    monkeypatch.setattr(renderers, "render_musescore", render)
    monkeypatch.setattr(renderers, "render_verovio", render)
    with pytest.raises(StopConditionError, match="5% drop gate"):
        execute(rows)
    assert not (run / "datasets").exists()
    report = json.loads((run / "report/w6-packaging.json").read_bytes())
    assert report["status"] == "failed"
    assert report["gate_summary"]["verovio"]["outcomes"] == {
        "passed": 1,
        "dropped": 1,
    }
    assert report["gate_summary"]["verovio"]["drop_rates"] == {"pitch": 0.5}
    assert _records(downloads)[-1]["charged_pages"] == report["rendered_pages"]


@pytest.mark.parametrize(
    ("admission_limit_reached", "reason"),
    [(True, "intake stopped before"), (False, "minimum Verovio cohort")],
)
def test_formal_packaging_distinguishes_early_stop_from_exhaustion(
    tmp_path, monkeypatch, packaging_run, admission_limit_reached, reason
):
    """An exhausted archive may proceed, but a short final cohort cannot."""
    _, run, execute = packaging_run
    monkeypatch.setattr(packaging, "_MAX_VARIANT_ROUNDS", 1)
    rows, rendered = _packaging_rows(
        tmp_path, monkeypatch, ("synth_train", "synth_val")
    )

    def render(xml, _style, _context, _scratch, _ledger, *, count_rasters, **_):
        count_rasters(1)
        return replace(rendered[xml.rsplit(":", 1)[-1]], engine="verovio")

    monkeypatch.setattr(renderers, "render_verovio", render)
    with pytest.raises(StopConditionError, match=reason):
        execute(
            rows,
            formal=True,
            admission_limit_reached=admission_limit_reached,
            crop_targets=(18_000, 2_000, 45_000, 5_000),
        )
    assert not (run / "datasets").exists()


@pytest.mark.parametrize("engine", ["verovio", "musescore"])
def test_renderer_exception_retains_scratch_and_charges_entire_batch(
    tmp_path, monkeypatch, engine
):
    """A failed worker cannot erase scratch or refund a sibling's pages."""
    rows = _render_inputs(tmp_path, monkeypatch, engine, 2)
    meter = packaging._PageMeter(16)  # noqa: SLF001
    meter.disk = packaging._DatasetSink(tmp_path)  # noqa: SLF001
    marker = b"retained scratch"

    def render(xml, _style, _context, scratch, _ledger, *, count_rasters, **_):
        count_rasters(1)
        if xml.endswith(rows[0]["canonical_v2_sha256"]):
            scratch.mkdir(parents=True)
            (scratch / "active").write_bytes(marker)
            msg = "worker could not be reaped"
            raise StopConditionError(msg)
        return renderers.RenderResult(engine, renderers.RenderGate("dropped"))

    monkeypatch.setattr(renderers, f"render_{engine}", render)
    with pytest.raises(StopConditionError, match="could not be reaped"):
        list(
            packaging._render_variants(  # noqa: SLF001
                rows,
                run=tmp_path,
                downloads=tmp_path / "downloads",
                musescore=_UNUSED_MUSESCORE,
                meter=meter,
                calibrated_identity=None,
                targets={"synth_train": 2},
                verovio_workers=2,
            )
        )
    (retained,) = (tmp_path / "tmp").glob("*/active")
    assert retained.read_bytes() == marker
    assert meter.disk.usage == len(marker)
    assert meter.actual == 9  # noqa: PLR2004
    assert not meter.uncounted_worker


@pytest.mark.parametrize("engine", ["verovio", "musescore"])
def test_renderer_checks_successful_scratch_before_removal(
    tmp_path, monkeypatch, engine
):
    """A normal renderer return still counts its scratch against the cap."""
    rows = _render_inputs(tmp_path, monkeypatch, engine, 1)
    headroom = packaging.WORKER_SCRATCH_HEADROOM_BYTES
    cap = headroom * 2
    monkeypatch.setattr(packaging, "W6_DISK_CAP_BYTES", cap)
    meter = packaging._PageMeter(16)  # noqa: SLF001
    meter.disk = packaging._DatasetSink(tmp_path)  # noqa: SLF001
    calls = []

    def render(
        _xml, _style, _context, scratch, _ledger, *, count_rasters, **kw
    ):
        calls.append(kw.get("max_pages"))
        count_rasters(1)
        scratch.mkdir(parents=True)
        with (scratch / "raster").open("xb") as raster:
            raster.truncate(cap + 1)
        return renderers.RenderResult(engine, renderers.RenderGate("dropped"))

    monkeypatch.setattr(renderers, f"render_{engine}", render)

    def attempt():
        return list(
            packaging._render_variants(  # noqa: SLF001
                rows,
                run=tmp_path,
                downloads=tmp_path / "downloads",
                musescore=_UNUSED_MUSESCORE,
                meter=meter,
                calibrated_identity=None,
                targets={"synth_train": 2},
            )
        )

    meter.disk.usage = cap - headroom // 2
    with pytest.raises(StopConditionError, match="generated disk cap"):
        attempt()
    assert not calls
    meter.disk.usage = 0
    with pytest.raises(StopConditionError, match="generated disk cap"):
        attempt()
    expected_pages = 9 if engine == "musescore" else 8
    assert calls == [min(expected_pages, cap // headroom)]
    assert not list((tmp_path / "tmp").iterdir())


def test_verovio_cohort_stops_before_emitting_the_offending_batch(
    tmp_path, monkeypatch
):
    """Two pitch drops in 30 variants stop the unmasked category cohort."""
    rows = _render_inputs(tmp_path, monkeypatch, "verovio", 31)
    failing = {row["canonical_v2_sha256"] for row in rows[:2]}
    meter = packaging._PageMeter(300)  # noqa: SLF001
    meter.disk = packaging._DatasetSink(tmp_path)  # noqa: SLF001

    def render(
        xml, _style, _downloads, _scratch, _ledger, *, count_rasters, **_
    ):
        count_rasters(1)
        gate = (
            renderers.RenderGate("dropped", {"pitch": 1})
            if xml.rsplit(":", 1)[-1] in failing
            else renderers.RenderGate("passed")
        )
        return renderers.RenderResult(
            "verovio", gate, categories=frozenset({"pitch"})
        )

    monkeypatch.setattr(renderers, "render_verovio", render)
    variants = []
    with pytest.raises(StopConditionError, match="5% drop gate"):
        variants.extend(
            packaging._render_variants(  # noqa: SLF001
                rows,
                run=tmp_path,
                downloads=tmp_path / "downloads",
                musescore=_UNUSED_MUSESCORE,
                meter=meter,
                calibrated_identity=None,
                targets={"synth_train": 31},
                verovio_workers=4,
            )
        )
    assert len(variants) == 26  # noqa: PLR2004
    assert meter.actual == len(rows)
    assert meter.gate_summary()["verovio"] == {
        "outcomes": {"dropped": 2, "passed": 29},
        "drop_rates": {"pitch": pytest.approx(2 / 31)},
        "disabled_categories": sorted(
            packaging.VEROVIO_DISABLED_CATEGORIES | {"pitch"}
        ),
    }


def test_packaging_measures_lengths_using_pinned_tokenizer(
    tmp_path, monkeypatch
):
    """Load exact local tokenizer metadata without truncation or padding."""
    downloads = tmp_path / "downloads"
    monkeypatch.setattr(
        packaging, "venv_python_path", lambda _: Path(sys.executable)
    )
    unknown = "[UNK]"
    tokenizer = Tokenizer(
        models.WordLevel({unknown: 0, "bar": 1}, unk_token=unknown)
    )
    tokenizer.pre_tokenizer = pre_tokenizers.WhitespaceSplit()
    tokenizer.enable_truncation(1)
    tmp_lock = LockedModelRepo("Fixture/model", "b" * 40, "MIT", ())
    pinned_file = downloads / "w6-model-meta" / tmp_lock.slug / "tokenizer.json"
    pinned_file.parent.mkdir(parents=True)
    tokenizer.save(str(pinned_file))
    payload = pinned_file.read_bytes()
    lock = LockedModelRepo(
        tmp_lock.repo_id,
        tmp_lock.commit_sha,
        tmp_lock.license_id,
        (
            LockedMetaFile(
                pinned_file.name, len(payload), "a" * 40, _sha(payload)
            ),
        ),
    )
    monkeypatch.setattr(packaging, "W6_MODEL_METADATA_LOCKS", (lock,))
    rows = [
        {"target_text_v2": "bar bar bar", "crop_width": 224, "crop_height": 112}
    ]
    tokens, visual = packaging._pinned_lengths(rows, downloads)  # noqa: SLF001
    assert tokens == {lock.slug: [3]}
    assert not visual
    pinned_file.write_bytes(payload + b"\n")
    with pytest.raises(StopConditionError, match="drifted W6 metadata"):
        packaging._pinned_lengths(rows, downloads)  # noqa: SLF001


def test_watchdog_reaps_stalled_worker_without_respawn_and_cleans_scratch(
    tmp_path,
):
    """The production watchdog returns 124 after reaping a sleeping child."""
    scratch = tmp_path / "scratch"
    scratch.mkdir()
    marker = tmp_path / "starts"
    script = (
        "import os, sys, time\n"
        "with open(sys.argv[1], 'a') as marker:\n"
        "    marker.write(f'{os.getpid()}\\n')\n"
        "print('0', flush=True)\n"
        "time.sleep(30)\n"
    )
    outcome = baseline.run_supervised_worker(
        [sys.executable, "-c", script, str(marker)],
        scratch=scratch,
        page_count=3,
        page_timeout=0.3,
        namespace_prefix=(),
    )
    assert outcome.returncode == 124  # noqa: PLR2004
    assert outcome.completed_pages == 1
    assert not scratch.exists()
    (pid,) = map(int, marker.read_text().splitlines())
    with pytest.raises(ProcessLookupError):
        os.kill(pid, 0)


def _system(lo, y):
    return baseline.OracleSystem((10, y, 210, y + 100), lo, lo)


def test_oracle_partition_forbids_repeated_neighbors_from_stealing_gap():
    """Identical candidate bars remain constrained to their matched systems."""
    bar = to_musicxml_text(_score(1))
    reference = to_musicxml_text(_score(3))
    oracle = [_system(1, 10), _system(2, 130), _system(3, 250)]
    blind = [(0, (*oracle[0].box, 2)), (1, (*oracle[2].box, 2))]
    prediction = concat_system_predictions([(1, 1, bar), (1, 2, bar)])
    allowed, forbidden, (report,) = baseline.partition_oracle_systems(
        {1: blind}, {1: oracle}, prediction
    )
    assert allowed == {1: frozenset({1}), 2: frozenset({3})}
    assert forbidden == {2}
    assert report.missed_gold == len(forbidden)
    result = baseline.score_oracle_partitioned_prediction(
        prediction, reference, {1: blind}, {1: oracle}
    )
    assert result.parse_valid
    assert [m.aligned_cand_ordinal for m in result.measures] == [1, None, 2]
    assert result.measures[1].layer1_counts["repeat_ending"] == 1
    assert not result.extra_cand_bars


def test_oracle_partition_keeps_spurious_repeated_crop_unmatched():
    """An extra repeated bar still incurs W2's candidate-side penalty."""
    bar = to_musicxml_text(_score(1))
    reference = to_musicxml_text(_score(2))
    oracle = [_system(1, 10), _system(2, 250)]
    blind = [
        (0, (*oracle[0].box, 2)),
        (1, (10, 130, 210, 230, 2)),
        (2, (*oracle[1].box, 2)),
    ]
    prediction = concat_system_predictions(
        [(1, 1, bar), (1, 2, bar), (1, 3, bar)]
    )
    allowed, forbidden, (report,) = baseline.partition_oracle_systems(
        {1: blind}, {1: oracle}, prediction
    )
    assert allowed[2] == frozenset()
    assert not forbidden
    assert report.spurious_fp == 1
    result = baseline.score_oracle_partitioned_prediction(
        prediction, reference, {1: blind}, {1: oracle}
    )
    assert result.parse_valid
    assert [m.aligned_cand_ordinal for m in result.measures] == [1, 3]
    (extra,) = result.extra_cand_bars
    assert extra.cand_ordinal == 2  # noqa: PLR2004
    assert extra.layer1_counts["repeat_ending"] == 1


def test_training_cli_privacy_scan_and_dirty_formal_guard(
    tmp_path, monkeypatch, capsys
):
    """CLI exits 0 on clean report and 2 without creating run_dir on stops."""
    from sheet_music_omr.training import cli as w6_cli  # noqa: PLC0415

    mapping = tmp_path / "mapping.json"
    mapping.write_text(json.dumps(_public_mapping()), encoding="utf-8")
    clean_report = tmp_path / "clean.md"
    clean_report.write_text("Aggregate count: 16 pieces.\n", encoding="utf-8")
    assert (
        w6_cli.main(
            [
                "scan-public-report",
                "--report",
                str(clean_report),
                "--mapping-json",
                str(mapping),
            ]
        )
        == 0
    )
    capsys.readouterr()

    leaky_report = tmp_path / "leaky.md"
    leaky_report.write_text("Leaked Invented identity 01\n", encoding="utf-8")
    assert (
        w6_cli.main(
            [
                "scan-public-report",
                "--report",
                str(leaky_report),
                "--mapping-json",
                str(mapping),
            ]
        )
        == 2  # noqa: PLR2004
    )
    capsys.readouterr()

    monkeypatch.setattr(
        w6_cli, "source_state", lambda: {"commit": "a" * 40, "dirty": True}
    )
    monkeypatch.setattr(w6_cli, "check_outside_repository", lambda *_: None)
    run_dir = tmp_path / "runs" / "w6-slice-a-dirty"
    downloads = tmp_path / "downloads"
    downloads.mkdir()
    package_args = [
        "package-w6-crops",
        "--run-dir",
        str(run_dir),
        "--downloads",
        str(downloads),
        "--lieder-manifest",
        str(tmp_path / "lieder.jsonl"),
        "--test-exclusion",
        str(tmp_path / "excl.json"),
        "--test-status",
        str(tmp_path / "status.json"),
    ]
    for opts, reason in (
        (["--formal"], "clean code tree"),
        (["--target-train-crops", "44999"], "milestone minima"),
        (["--max-pdmx", "2439"], "intake limit"),
        (["--no-formal"], "missing prior campaign ledger"),
    ):
        assert w6_cli.main([*package_args, *opts]) == 2  # noqa: PLR2004
        assert reason in capsys.readouterr().err
    _seed_prior_ledgers(downloads, monkeypatch)
    monkeypatch.setattr(ledgers, "W6_DISK_CAP_BYTES", 1_048_575)
    assert w6_cli.main([*package_args, "--no-formal"]) == 2  # noqa: PLR2004
    assert "generated disk cap" in capsys.readouterr().err
    assert not run_dir.exists()


def test_smoke_cli_uses_only_training_assets_and_reports_environment(
    tmp_path, monkeypatch, capsys
):
    """Neural smoke records the locked environment without requiring Copista."""
    from sheet_music_omr.training import cli as w6_cli  # noqa: PLC0415

    downloads = tmp_path / "downloads"
    work = tmp_path / "work"
    environment = {"wheel_lock_sha256": _sha(b"training")}

    def verify(path):
        assert path == downloads
        return environment

    def smoke(python, path, *, meta_root):
        assert python == downloads / "w6-train-venv" / "bin" / "python"
        assert path == work
        assert meta_root == downloads / "w6-model-meta"
        return {"status": "passed"}

    monkeypatch.setattr(w6_cli, "verify_w6_training_assets", verify)
    monkeypatch.setattr(w6_cli, "run_w6_neural_cpu_smoke", smoke)
    monkeypatch.setattr(w6_cli, "check_outside_repository", lambda *_: None)
    assert (
        w6_cli.main(
            [
                "smoke-w6-stack",
                "--downloads",
                str(downloads),
                "--work-dir",
                str(work),
            ]
        )
        == 0
    )
    expected = {"status": "passed", "environment": environment}
    assert json.loads(capsys.readouterr().out) == expected
    assert json.loads((work / "smoke-report.json").read_bytes()) == expected


def test_standalone_number_does_not_conflate_distinct_genres():
    """Standalone No. distinguishes No. 8 vs No. 9 without merging genres."""
    sonata_3 = intake.normalize_work_identity("Beethoven", "Piano Sonata No. 3")
    conc_3 = intake.normalize_work_identity("Beethoven", "Piano Concerto No. 3")
    sonata_8 = intake.normalize_work_identity("Beethoven", "Piano Sonata No. 8")
    sonata_9 = intake.normalize_work_identity("Beethoven", "Piano Sonata No. 9")
    assert sonata_3 is not None
    assert conc_3 is not None
    assert sonata_8 is not None
    assert sonata_9 is not None
    assert not sonata_3.matches(conc_3)
    assert not sonata_8.matches(sonata_9)
    op2_no1 = intake.normalize_work_identity("Beethoven", "Sonata No. 1, Op. 2")
    op2_no3 = intake.normalize_work_identity("Beethoven", "Sonata No. 3, Op. 2")
    assert op2_no1 is not None
    assert op2_no3 is not None
    assert not op2_no1.matches(op2_no3)
    etude_3 = intake.normalize_work_identity("Chopin", "Étude No. 3, Op. 10")
    etude_5 = intake.normalize_work_identity("Chopin", "Étude Op. 10 No. 5")
    assert etude_3 is not None
    assert etude_5 is not None
    assert not etude_3.matches(etude_5)
    assert not etude_5.matches(etude_3)
    op25_13 = intake.normalize_work_identity("Chopin", "Étude No. 13, Op. 25")
    op25_1 = intake.normalize_work_identity("Chopin", "Étude Op. 25 No. 1")
    op25_1_reordered = intake.normalize_work_identity(
        "Chopin", "Étude No. 1, Op. 25"
    )
    assert op25_13 is not None
    assert op25_1 is not None
    assert op25_1_reordered is not None
    assert op25_13.matches(op25_1, conservative_exclusion=True)
    assert op25_1.matches(op25_13, conservative_exclusion=True)
    assert op25_13.matches(op25_1_reordered, conservative_exclusion=True)
    assert op25_1_reordered.matches(op25_13, conservative_exclusion=True)
    assert not op25_13.matches(op25_1_reordered)
    op10_3 = intake.normalize_work_identity("Chopin", "Étude Op. 10 No. 3")
    assert op10_3 is not None
    op10_3_with_set = intake.normalize_work_identity(
        "Chopin", "Étude Op. 10 No. 3 (from 12 Études, Op. 10)"
    )
    assert op10_3_with_set is not None
    assert not op10_3_with_set.matches(etude_5, conservative_exclusion=True)
    assert not etude_5.matches(op10_3_with_set, conservative_exclusion=True)
    bare_sonata = intake.normalize_work_identity("Beethoven", "Piano Sonata")
    assert bare_sonata is not None
    grouped = packaging._work_groups(  # noqa: SLF001
        "beethoven",
        [
            (sonata_8, {"source_id": "s8"}),
            (bare_sonata, {"source_id": "s_bare"}),
            (sonata_9, {"source_id": "s9"}),
        ],
    )
    by_id = {r["source_id"]: r["work_group_id"] for r in grouped}
    assert by_id["s8"] != by_id["s9"]


def test_musescore_preference_guard_and_cached_calibration(
    tmp_path, monkeypatch
):
    """Enabled jump inference stops; new 4.x builds cache calibration."""
    from sheet_music_omr.harness.confine import Confinement  # noqa: PLC0415
    from sheet_music_omr.harness.musescore import MuseScore  # noqa: PLC0415

    ms = MuseScore(
        executable=tmp_path / "MuseScore4",
        version="4.8.0",
        executable_sha256="1" * 64,
        settings=tmp_path / "MuseScore4.ini",
        confinement=Confinement(tmp_path / "run", tmp_path / "dl"),
        baseline=None,
        baseline_preferences={
            "import/musicXml/importInferTextType": {"value": "true"}
        },
    )
    rows = _render_inputs(tmp_path, monkeypatch, "musescore", 1)
    meter = packaging._PageMeter(16)  # noqa: SLF001
    meter.disk = packaging._DatasetSink(tmp_path)  # noqa: SLF001
    called = False

    def forbidden(*_a, **_kw):
        nonlocal called
        called = True
        msg = "renderer should not have been called"
        raise AssertionError(msg)

    monkeypatch.setattr(renderers, "render_musescore", forbidden)
    with pytest.raises(StopConditionError, match="preferences set explicitly"):
        list(
            packaging._render_variants(  # noqa: SLF001
                rows,
                run=tmp_path,
                downloads=tmp_path / "dl",
                musescore=ms,
                meter=meter,
                calibrated_identity=None,
                targets={"synth_train": 1},
            )
        )
    meter2 = packaging._PageMeter(16)  # noqa: SLF001
    meter2.disk = packaging._DatasetSink(tmp_path)  # noqa: SLF001
    with pytest.raises(StopConditionError, match="importInferTextType=false"):
        list(
            packaging._render_variants(  # noqa: SLF001
                rows,
                run=tmp_path,
                downloads=tmp_path / "dl",
                musescore=replace(ms, version="4.7.7"),
                meter=meter2,
                calibrated_identity=None,
                targets={"synth_train": 1},
            )
        )
    assert not called

    # New 4.8.0 build calibrates once and caches in .musescore-calibrated.json.
    cal_calls = 0

    def fake_cal(m, _dir, *, count_rasters=None):
        nonlocal cal_calls
        cal_calls += 1
        if count_rasters is not None:
            count_rasters(1)
        return (m.version, m.executable_sha256)

    monkeypatch.setattr(renderers, "calibrate_musescore_pixel_scale", fake_cal)
    cache_file = tmp_path / "dl" / renderers.MUSESCORE_CALIBRATION_CACHE_NAME
    assert renderers.ensure_musescore_calibrated(
        ms, tmp_path / "cal1", cache_path=cache_file
    ) == ("4.8.0", "1" * 64)
    assert renderers.ensure_musescore_calibrated(
        ms, tmp_path / "cal2", cache_path=cache_file
    ) == ("4.8.0", "1" * 64)
    assert cal_calls == 1


def test_volta_ending_and_restated_midscore_clef_route_to_musescore():
    """Volta <ending> and restated mid-score <clef> route to MuseScore."""
    score = _score(2)
    base_xml = to_musicxml_text(score)
    volta_xml = base_xml.replace(
        '<measure number="2">',
        '<measure number="2"><barline location="left">'
        '<ending number="1" type="start"/></barline>',
    )
    assert (
        packaging.choose_engine_for_score(
            score, volta_xml, preferred_engine="verovio"
        )
        == "musescore"
    )
    restated_clef_xml = base_xml.replace(
        '<measure number="2">',
        '<measure number="2"><attributes><clef number="2">'
        "<sign>F</sign><line>4</line></clef></attributes>",
    )
    assert (
        packaging.choose_engine_for_score(
            score, restated_clef_xml, preferred_engine="verovio"
        )
        == "musescore"
    )
    empty_meter = packaging._PageMeter(100, min_verovio_cohort=250)  # noqa: SLF001
    empty_meter.validate_verovio_cohort(formal=False)
    with pytest.raises(StopConditionError, match="minimum Verovio cohort"):
        empty_meter.validate_verovio_cohort(formal=True)


class _BrokenUnpicklableError(Exception):
    def __init__(self, a: int, b: int) -> None:
        super().__init__(a, b)
        self.pair = (a, b)

    def __reduce__(self):
        msg = "cannot pickle"
        raise TypeError(msg)

    def __str__(self) -> str:
        msg = "broken __str__"
        raise RuntimeError(msg)


def _stalled_forkserver_job(pid_file: str) -> int:
    Path(pid_file).write_text(f"{os.getpid()}\n", encoding="utf-8")
    time.sleep(30)
    return 1


def _prep_fail_first_stall_second(plan, _preferred):
    marker = Path(str(plan.row["canonical_root"])) / "sibling.pid"
    if plan.row["source_id"] == "fail-first":
        deadline = time.monotonic() + 5.0
        while not marker.is_file() and time.monotonic() < deadline:
            time.sleep(0.02)
        return packaging._WorkerError("stop", "first prep failed")  # noqa: SLF001
    marker.write_text(f"{os.getpid()}\n", encoding="utf-8")
    time.sleep(30)
    return packaging._WorkerError("stop", "unreachable")  # noqa: SLF001


def _init_prep_abort_fixture():
    packaging._offline_worker()  # noqa: SLF001
    pytest.MonkeyPatch().setattr(
        packaging, "_prepare_variant_worker", _prep_fail_first_stall_second
    )


def test_worker_ipc_errors_timeout_reaping_and_verovio_scratch_bound(
    tmp_path, monkeypatch, request
):
    """IPC errors stay primitive, stalled pools reap, and scratch is bounded."""
    monkeypatch.syspath_prepend(str(request.config.rootpath))
    err = packaging._worker_error(_BrokenUnpicklableError(1, 2))  # noqa: SLF001
    assert err == packaging._WorkerError("stop", "_BrokenUnpicklableError")  # noqa: SLF001
    assert isinstance(packaging._to_exception(err), StopConditionError)  # noqa: SLF001
    val_err = packaging._worker_error(ValueError("bad overlay"))  # noqa: SLF001
    assert isinstance(packaging._to_exception(val_err), ValueError)  # noqa: SLF001

    pid_file = tmp_path / "worker.pid"
    pool = packaging._process_pool(1)  # noqa: SLF001
    fut = pool.submit(_stalled_forkserver_job, str(pid_file))
    deadline = time.monotonic() + 5.0
    while not pid_file.is_file() and time.monotonic() < deadline:
        time.sleep(0.02)
    worker_pid = int(pid_file.read_text(encoding="utf-8").strip())
    with pytest.raises(StopConditionError, match="deadline"):
        packaging._await_futures(pool, (fut,), timeout=0.15)  # noqa: SLF001
    packaging._shutdown_pool(pool)  # noqa: SLF001
    with pytest.raises(ProcessLookupError):
        os.kill(worker_pid, 0)

    # Preparation failure aborts and reaps an already-running lookahead sibling.
    prep_rows = [
        _source(sid, "Beethoven", "Sonata")
        | {"canonical_root": str(tmp_path), "split": "synth_train"}
        for sid in ("fail-first", "stall-second")
    ]
    monkeypatch.setattr(
        packaging,
        "_process_pool",
        lambda n: ProcessPoolExecutor(
            max_workers=n,
            mp_context=multiprocessing.get_context("forkserver"),
            initializer=_init_prep_abort_fixture,
        ),
    )
    meter = packaging._PageMeter(16)  # noqa: SLF001
    meter.disk = packaging._DatasetSink(tmp_path / "prep-abort")  # noqa: SLF001
    t0 = time.monotonic()
    with pytest.raises(StopConditionError, match="first prep failed"):
        list(
            packaging._render_variants(  # noqa: SLF001
                prep_rows,
                run=tmp_path / "prep-abort",
                downloads=tmp_path / "dl",
                musescore=_UNUSED_MUSESCORE,
                meter=meter,
                calibrated_identity=None,
                targets={"synth_train": 2},
                verovio_workers=2,
                musescore_page_cap=0,
            )
        )
    assert time.monotonic() - t0 < 6.0  # noqa: PLR2004
    sibling_pid = int((tmp_path / "sibling.pid").read_text(encoding="utf-8"))
    with pytest.raises(ProcessLookupError):
        os.kill(sibling_pid, 0)


def _verify_fail_fixture(_staged, _ledger):
    msg = "verifier exploded after staging"
    raise StopConditionError(msg)


def _init_verify_fail_fixture():
    packaging._offline_worker()  # noqa: SLF001
    pytest.MonkeyPatch().setattr(
        renderers, "verify_verovio_outputs", _verify_fail_fixture
    )


def test_idle_pool_fallback_and_post_stage_verifier_meter(
    tmp_path, monkeypatch, request
):
    """Idle fallback kills saved handles and verifier keeps counted pages."""
    monkeypatch.syspath_prepend(str(request.config.rootpath))

    # Idle shutdown fallback still SIGKILLs a live worker after shutdown clears
    # pool._processes in Python 3.14.
    idle_pid_file = tmp_path / "idle_worker.pid"
    idle_pool = packaging._process_pool(1)  # noqa: SLF001
    _ = idle_pool.submit(_stalled_forkserver_job, str(idle_pid_file))
    deadline = time.monotonic() + 5.0
    while not idle_pid_file.is_file() and time.monotonic() < deadline:
        time.sleep(0.02)
    idle_pid = int(idle_pid_file.read_text(encoding="utf-8").strip())
    packaging._shutdown_pool(idle_pool, clean_idle=True)  # noqa: SLF001
    with pytest.raises(ProcessLookupError):
        os.kill(idle_pid, 0)

    # Verifier failure after staging records actual rendered pages (not full
    # planned reservation).
    score = _score(3)
    v_path = tmp_path / "v.txt"
    v_raw = write_document(score).encode()
    v_path.write_bytes(v_raw)
    v_rows = [
        {
            "source_id": "v1",
            "work_group_id": "v1",
            "canonical_composer": "fixture",
            "canonical_root": str(tmp_path),
            "canonical_v2_path": v_path.name,
            "canonical_v2_sha256": _sha(v_raw),
            "measure_count": 3,
            "rights_variant": "permissive",
            "split": "synth_train",
        }
    ]
    monkeypatch.setattr(
        renderers, "stage_verovio_subprocess", _stage_verovio_fixture
    )
    monkeypatch.setattr(
        packaging,
        "_process_pool",
        lambda n: ProcessPoolExecutor(
            max_workers=n,
            mp_context=multiprocessing.get_context("forkserver"),
            initializer=_init_verify_fail_fixture,
        ),
    )
    meter = packaging._PageMeter(500)  # noqa: SLF001
    meter.disk = packaging._DatasetSink(tmp_path / "verify-fail")  # noqa: SLF001
    with pytest.raises(StopConditionError, match="verifier exploded"):
        list(
            packaging._render_variants(  # noqa: SLF001
                v_rows,
                run=tmp_path / "verify-fail",
                downloads=tmp_path / "dl",
                musescore=_UNUSED_MUSESCORE,
                meter=meter,
                calibrated_identity=None,
                targets={"synth_train": 2},
                verovio_workers=2,
                musescore_page_cap=0,
            )
        )
    assert meter.actual == 1
    assert meter.uncounted_worker is False


def _write_supplement_fixture_base_run(
    base_run: Path, downloads: Path
) -> tuple[Path, list[dict[str, object]]]:
    perm_dir = base_run / "synthetic" / "permissive"
    src_dir = perm_dir / "sources"
    src_dir.mkdir(parents=True)
    bucket_keys = ("tremolos", "cross_staff", "ornaments", "hairpins")
    surnames = (
        "Albeniz",
        "Brahms",
        "Couperin",
        "Debussy",
        "Elgar",
        "Faure",
        "Grieg",
        "Handle",
        "Ibert",
        "Janacek",
        "Kodaly",
        "Liszt",
    )
    manifest_rows: list[dict[str, object]] = []
    for i, comp in enumerate(surnames):
        fname = f"s{i:02d}.v2"
        pitch = f"{'CDEFGAB'[i % 7]}{4 + (i // 7)}"
        unique_score = parse_document(
            _HEADER
            + "".join(
                f"m {m}\n1.1: {pitch}/1\n2.1: C3/1\n" for m in range(1, 4)
            )
        )
        assert isinstance(unique_score, AssembledScore)
        raw_i = write_document(unique_score).encode()
        (src_dir / fname).write_bytes(raw_i)
        manifest_rows.append(
            {
                "source_id": f"pdmx-{i:02d}",
                "corpus": "PDMX",
                "rights_variant": "permissive",
                "license_url": "CC0-1.0",
                "composer": comp,
                "title": f"Sonata No. {i + 1}",
                "canonical_root": str(perm_dir),
                "canonical_v2_path": f"sources/{fname}",
                "canonical_v2_sha256": _sha(raw_i),
                "raw_sha256": _sha(raw_i),
                "measure_count": 3,
                "category_densities": {bucket_keys[i % 4]: 0.5},
            }
        )
    manifest_path = perm_dir / "sources-manifest.jsonl"
    manifest_path.write_text(
        packaging._jsonl(manifest_rows),  # noqa: SLF001
        encoding="utf-8",
    )
    train, val, split = packaging.split_admitted_sources_by_composer(
        manifest_rows, min_val_works=10
    )
    tr_var = replace(
        _rendered_fixture("tr-base", "synth_train"), source=train[0]
    )
    va_var = replace(_rendered_fixture("va-base", "synth_val"), source=val[0])
    tr_var2 = replace(
        _rendered_fixture("tr-base-2", "synth_train"), source=train[1]
    )
    va_var2 = replace(
        _rendered_fixture("va-base-2", "synth_val"), source=val[1]
    )
    pkg_report = packaging.package_rendered_variants_to_datasets(
        [tr_var, va_var, tr_var2, va_var2],
        base_run,
        downloads_dir=downloads,
        target_train_crops=4,
        target_val_crops=4,
        prefix_train_crops=2,
        prefix_val_crops=2,
    )
    report_path = base_run / "report" / "w6-packaging.json"
    report_path.parent.mkdir(parents=True, exist_ok=True)
    report_path.write_text(
        json.dumps(pkg_report | {"split": split}) + "\n",
        encoding="utf-8",
    )
    return manifest_path, manifest_rows


def test_musescore_supplement_audits_test_and_minima_before_read(
    tmp_path, monkeypatch
):
    """Supplement checks minima/digests and audits TEST before reads."""
    with pytest.raises(StopConditionError, match="requires >=2,700/300"):
        packaging.validate_w6_supplement_minima(
            formal=True,
            supplement_train_crops=2699,
            supplement_val_crops=300,
        )
    with pytest.raises(StopConditionError, match="exact 18,000/2,000"):
        packaging.validate_w6_supplement_minima(
            formal=True,
            supplement_train_crops=2700,
            supplement_val_crops=300,
            crop_targets=(18_000, 2_000, 45_001, 5_000),
        )
    with pytest.raises(StopConditionError, match="lacks positive coverage"):
        packaging._verify_supplement_category_positives(  # noqa: SLF001
            [
                {"split": "synth_train", "present_categories": ["tremolo"]},
                {"split": "synth_val", "present_categories": ["tremolo"]},
            ]
        )
    runs_root = tmp_path / "runs"
    base_dir = runs_root / "w6-slice-a-package-formal-4"
    perm_dir = base_dir / "synthetic" / "permissive"
    perm_dir.mkdir(parents=True)
    for rel in (
        "report/w6-packaging.json",
        "synthetic/permissive/sources-manifest.jsonl",
        "datasets/w6-crops-50k/summary.json",
        "datasets/w6-crops-50k/synth_train/crops.jsonl",
        "datasets/w6-crops-50k/synth_val/crops.jsonl",
        "datasets/w6-crops-50k/synth_train/page-boxes.jsonl",
        "datasets/w6-crops-50k/synth_val/page-boxes.jsonl",
    ):
        f = base_dir / rel
        f.parent.mkdir(parents=True, exist_ok=True)
        f.write_text("tampered\n", encoding="utf-8")
    gate, _ = _metadata_gate(tmp_path, monkeypatch)
    test_gid = sorted(intake.TEST_GOLD_IDS)[0]
    poisoned = tmp_path / "poisoned.jsonl"
    poisoned.write_text(
        packaging._jsonl(  # noqa: SLF001
            [
                {
                    "source_id": "pdmx-00",
                    "corpus": "PDMX",
                    "rights_variant": "permissive",
                    "license_url": "CC0-1.0",
                    "composer": "Brahms",
                    "title": "Sonata No. 1",
                    "canonical_root": str(perm_dir),
                    "canonical_v2_path": "sources/s00.v2",
                    "canonical_v2_sha256": "a" * 64,
                    "raw_sha256": _sha(f"xml-{test_gid}".encode()),
                    "measure_count": 3,
                }
            ]
        ),
        encoding="utf-8",
    )
    score_reads = 0

    def forbid_score(*_a, **_kw):
        nonlocal score_reads
        score_reads += 1
        msg = "score must not be opened before verification"
        raise AssertionError(msg)

    monkeypatch.setattr(packaging, "_source_score", forbid_score)
    with pytest.raises(StopConditionError, match="digest provenance"):
        packaging._read_verified_base_blobs(  # noqa: SLF001
            base_dir, "w6-crops-50k", formal=True
        )
    with pytest.raises(StopConditionError, match="TEST"):
        packaging._verify_reused_manifest(  # noqa: SLF001
            poisoned, runs_root, gate, formal=False
        )
    assert score_reads == 0


def test_musescore_supplement_augments_milestones_cleanly(
    tmp_path, monkeypatch
):
    """Supplement augments 20k/50k milestones cleanly on identical splits."""
    from sheet_music_omr.training import models as w6_models  # noqa: PLC0415

    downloads = tmp_path / "downloads"
    _seed_prior_ledgers(downloads, monkeypatch)
    runs_root = tmp_path / "runs"
    base_run = runs_root / "w6-slice-a-base"
    supp_run = runs_root / "w6-slice-a-supp"
    monkeypatch.setattr(packaging, "_pinned_lengths", lambda *_: ({}, {}))
    manifest_path, _ = _write_supplement_fixture_base_run(base_run, downloads)
    _metadata_gate(tmp_path, monkeypatch)
    excl_file = tmp_path / "test-exclusion.json"
    status_file = tmp_path / "batch-status.json"
    assert manifest_path.is_file()

    exe = tmp_path / "fake-ms"
    exe.write_bytes(b"ms")
    ms = MuseScore(
        exe,
        "4.7.7",
        _sha(b"ms"),
        tmp_path / "active-ms.ini",
        Confinement(supp_run, downloads),
        None,
        {"import/musicXml/importInferTextType": {"value": "false"}},
    )
    monkeypatch.setattr(packaging, "repository_root", lambda: tmp_path / "repo")
    monkeypatch.setattr(
        packaging, "source_state", lambda: {"commit": "c" * 40, "dirty": False}
    )
    monkeypatch.setattr(
        renderers,
        "SOURCE_VERIFIED_MUSESCORE_BUILDS",
        {(ms.version, ms.executable_sha256)},
    )
    monkeypatch.setattr(packaging, "verify_w6_training_assets", lambda _: {})
    seen_enabled: list[set[str] | None] = []
    orig_inject = packaging.injector.inject_annotations

    def spy_inject(sc, *, seed, enabled_categories=None):
        seen_enabled.append(
            None if enabled_categories is None else set(enabled_categories)
        )
        return orig_inject(sc, seed=seed, enabled_categories=enabled_categories)

    monkeypatch.setattr(packaging.injector, "inject_annotations", spy_inject)

    def fake_render_ms(*_a, count_rasters, **_kw):
        count_rasters(1)
        return _rendered_fixture("ms-supp", "synth_train").render

    monkeypatch.setattr(renderers, "render_musescore", fake_render_ms)
    bytes_before = ledgers._check_w6_disk_cap(runs_root)  # noqa: SLF001
    out = packaging.run_musescore_supplement_and_package(
        downloads,
        supp_run,
        base_run_dir=base_run,
        test_exclusion=excl_file,
        test_status=status_file,
        musescore=ms,
        planned_pages=30,
        formal=False,
        supplement_train_crops=2,
        supplement_val_crops=2,
        target_train_crops=4,
        target_val_crops=4,
        prefix_train_crops=3,
        prefix_val_crops=3,
    )
    assert seen_enabled
    assert all("N5" not in s for s in seen_enabled if s)
    assert all({"N2", "P3"} <= s for s in seen_enabled if s)
    full_out = cast("dict[str, dict[str, object]]", out["fixture-crops-full"])
    pref_out = cast("dict[str, dict[str, object]]", out["fixture-crops-prefix"])
    assert full_out["synth_train"]["crops"] == 4  # noqa: PLR2004
    assert full_out["synth_val"]["crops"] == 4  # noqa: PLR2004
    assert cast("int", pref_out["synth_train"]["crops"]) >= 3  # noqa: PLR2004
    tr_full = w6_models.load_packaged_crop_manifest(
        supp_run / "datasets" / "fixture-crops-full" / "synth_train"
    )
    va_full = w6_models.load_packaged_crop_manifest(
        supp_run / "datasets" / "fixture-crops-full" / "synth_val"
    )
    assert not (
        {r["canonical_composer"] for r in tr_full}
        & {r["canonical_composer"] for r in va_full}
    )
    raw_sum = sum(
        ledgers._w6_run_bytes(d)  # noqa: SLF001
        for d in runs_root.glob("w6-slice-a-*")
    )
    assert bytes_before < ledgers._check_w6_disk_cap(runs_root) < raw_sum  # noqa: SLF001

    # Corrupting a base page PNG is rejected before linking.
    page_png = next((base_run / "datasets" / "assets" / "pages").glob("*.png"))
    page_png.write_bytes(b"corrupt")
    with pytest.raises(StopConditionError, match="corrupted base W6 page"):
        packaging._load_verified_base_run(  # noqa: SLF001
            base_run,
            run=runs_root / "w6-slice-a-supp2",
            downloads=downloads,
            runs_root=runs_root,
            gate=packaging.MetadataOnlyTestExclusion(excl_file, status_file),
            formal=False,
        )


def test_musescore_timeout_and_batch_jobs(tmp_path):
    """Real _musescore_job drops nonzero exits and stops on timeout."""
    downloads = tmp_path / "downloads"
    runs_root = tmp_path / "runs"
    timeout_run = Confinement.create(
        runs_root / "w6-slice-a-ms-timeout", downloads
    ).run
    exe = tmp_path / "fake-ms"
    exe.write_bytes(b"ms")
    active_ini = tmp_path / "active-ms.ini"
    active_ini.write_text(
        "[General]\nimport\\musicXml\\importInferTextType=false\n",
        encoding="utf-8",
    )
    ms = MuseScore.open(
        Confinement(timeout_run, downloads),
        executable=exe,
        settings=active_ini,
        version_lookup=lambda: "4.7.7",
    )
    ms_xml = to_musicxml_text(_score(3))
    ms_prep = packaging._PreparedVariant(  # noqa: SLF001
        {"source_id": "s1"},
        "seed",
        1,
        renderers.sample_layout_and_style(1, 3),
        3,
        "musescore",
        ms_xml,
    )
    assert packaging._item_max_pages(ms_prep) == 4  # noqa: SLF001, PLR2004

    clean_outcome = packaging._execute_prepared_variant(  # noqa: SLF001
        ms_prep,
        run=timeout_run,
        downloads=downloads,
        musescore=replace(ms, launcher=lambda _c, _t: (1, "failed")),
        calibrated_identity=None,
        musescore_lock=packaging.threading.Lock(),
        max_pages=4,
    )
    assert isinstance(clean_outcome.result, renderers.RenderResult)
    assert clean_outcome.result.gate.status == "dropped"
    assert not list((timeout_run / "tmp").iterdir())

    # Partial PNGs emitted before nonzero MuseScore exit are counted, and an
    # unreaped SIGKILL blocks shared-profile reuse.
    def partial_fail_launcher(_cmd, _t):
        for target in (timeout_run / "tmp").iterdir():
            if target.is_dir():
                (target / "score-1.png").write_bytes(b"png")
                (target / "score-2.png").write_bytes(b"png")
        return (1, "partial failure")

    partial_outcome = packaging._execute_prepared_variant(  # noqa: SLF001
        ms_prep,
        run=timeout_run,
        downloads=downloads,
        musescore=replace(ms, launcher=partial_fail_launcher),
        calibrated_identity=None,
        musescore_lock=packaging.threading.Lock(),
        max_pages=4,
    )
    assert partial_outcome.pages == 2  # noqa: PLR2004

    from sheet_music_omr.harness.musescore import MuseScoreUnreapedError  # noqa: PLC0415

    def unreaped_launcher(_cmd, _t):
        msg = "not reaped"
        raise MuseScoreUnreapedError(msg)

    unreaped = packaging._execute_prepared_variant(  # noqa: SLF001
        ms_prep,
        run=timeout_run,
        downloads=downloads,
        musescore=replace(ms, launcher=unreaped_launcher),
        calibrated_identity=None,
        musescore_lock=packaging.threading.Lock(),
        max_pages=4,
    )
    assert isinstance(unreaped.result, MuseScoreUnreapedError)
    blocked = packaging._execute_prepared_variant(  # noqa: SLF001
        ms_prep,
        run=timeout_run,
        downloads=downloads,
        musescore=replace(ms, launcher=lambda _c, _t: (0, "")),
        calibrated_identity=None,
        musescore_lock=packaging.threading.Lock(),
        max_pages=4,
    )
    assert isinstance(blocked.result, StopConditionError)
    assert "shared-profile reuse" in str(blocked.result)


def test_spacer_only_measure_excluded_from_musescore_supplement(tmp_path):
    """Whole-measure spacers stay in base splits but skip MuseScore plans."""
    from sheet_music_omr.comparison import annotate, install_coupling_fix  # noqa: PLC0415
    from sheet_music_omr.musicxml_events import ProtocolDefectError  # noqa: PLC0415
    from sheet_music_omr.musicxml_forward import to_musicxml  # noqa: PLC0415
    from sheet_music_omr.parser import parse_document  # noqa: PLC0415

    install_coupling_fix()
    spacer_measure_v2 = (
        "format: text-score/2\n"
        "key: 0\n"
        "time: 4/4\n"
        "clefs: F4 F4\n"
        "carry: octave\n"
        "m 1\n"
        "1.1: C4/1\n"
        "2.1: C3/1\n"
        "m 2\n"
        "1.1: x/1\n"
        "2.1: x/1\n"
        "m 3\n"
        "1.1: <clef G2> G4/1\n"
        "2.1: G2/1\n"
        "m 4\n"
        "1.1: C4/1\n"
        "2.1: C3/1\n"
    )
    spacer_doc = cast("AssembledScore", parse_document(spacer_measure_v2))
    with pytest.raises(ProtocolDefectError):
        annotate(to_musicxml(spacer_doc).decode("utf-8"))

    one_staff_spacer_v2 = spacer_measure_v2.replace(
        "m 2\n1.1: x/1\n2.1: x/1\n",
        "m 2\n1.1: x/1\n2.1: C3/1\n",
    )
    ok_doc = cast("AssembledScore", parse_document(one_staff_spacer_v2))
    annotate(to_musicxml(ok_doc).decode("utf-8"))

    sources_dir = tmp_path / "synthetic" / "permissive" / "sources"
    sources_dir.mkdir(parents=True)
    (sources_dir / "bad.v2").write_text(spacer_measure_v2, encoding="utf-8")
    (sources_dir / "ok.v2").write_text(one_staff_spacer_v2, encoding="utf-8")
    surnames = (
        "Albeniz",
        "Brahms",
        "Couperin",
        "Debussy",
        "Elgar",
        "Faure",
        "Grieg",
        "Handle",
        "Ibert",
        "Janacek",
        "Kodaly",
        "Liszt",
    )
    rows = [
        {
            "source_id": f"pdmx-{idx:02d}",
            "composer": name,
            "title": f"Sonata No. {idx + 1}",
            "canonical_root": str(sources_dir.parent),
            "canonical_v2_path": (
                "sources/bad.v2" if idx == 0 else "sources/ok.v2"
            ),
            "canonical_v2_sha256": _sha(
                (spacer_measure_v2 if idx == 0 else one_staff_spacer_v2).encode(
                    "utf-8"
                )
            ),
            "measure_count": 4,
            "category_densities": {"clef_changes": 0.25},
        }
        for idx, name in enumerate(surnames)
    ]
    train, val, _ = packaging.split_admitted_sources_by_composer(
        rows, min_val_works=10
    )
    assert "pdmx-00" in {r["source_id"] for r in [*train, *val]}
    plans, counts = packaging._interleave_supplement_plans([*train, *val])  # noqa: SLF001
    assert counts["clef"] == len(surnames) - 1
    assert "pdmx-00" not in {p.row["source_id"] for p in plans}


_DUPLEX_HEADER = _HEADER.replace("4/4", "2/4")


def _duplex_xml(body):
    score = parse_document(_DUPLEX_HEADER + "m 1\n1.1: D4/2[up]\n" + body)
    assert isinstance(score, AssembledScore)
    return to_musicxml_text(score)


def _packaged_supervision(xml):
    from sheet_music_omr.harness.systems import SystemLayout  # noqa: PLC0415
    from sheet_music_omr.training import models as training  # noqa: PLC0415

    variant = _rendered_fixture("duplex", "synth_train")
    layout = SystemLayout(3, (SystemStart(0, 1, 1), SystemStart(2, 1, 2)))
    labels = renderers._system_texts(xml, layout)  # noqa: SLF001
    render = replace(variant.render, system_texts=labels)
    crops, _, _ = packaging._variant_rows(replace(variant, render=render))  # noqa: SLF001
    tokenizer = training.TextScore2DomainTokenizer()
    targets = []
    for row, label in zip(crops, labels, strict=True):
        ids, prompt_len = training._arm_b_train_sequence(tokenizer, row)  # noqa: SLF001
        target = ids[prompt_len:]
        assert target[-1] == tokenizer.eos_token_id
        assert tokenizer.decode(target[:-1]) == row["target_text_v2"] == label
        targets.append(target)
    return labels, targets


def test_local_beams_ignore_measure_voice_ids():
    """Local beams must not make source IDs authoritative for visible voices."""
    import xml.etree.ElementTree as ET  # noqa: PLC0415

    upper = (
        "1.1: A5/8[up,beam1=start] B5/8[up,beam1=continue] "
        "C6/8[up,beam1=continue] D6/8[up,beam1=end]"
    )
    lower = (
        "1.2: F4/8[down,beam1=start] E4/8[down,beam1=continue] "
        "D4/8[down,beam1=continue] C4/8[down,beam1=end]"
    )
    xml = _duplex_xml(
        f"m 2\n{upper}\n{lower}\nm 3 right=final\n1.1: D4/2[up]\n"
    )
    root = ET.fromstring(xml)  # noqa: S314 - generated fixture
    labels, targets = _packaged_supervision(
        ET.tostring(root, encoding="unicode")
    )
    swap = {"1": "2", "2": "1"}
    voices = root.findall(".//measure[@number='2']/note/voice")
    assert {voice.text for voice in voices} == swap.keys()
    for voice in voices:
        assert voice.text is not None
        voice.text = swap[voice.text]
    changed = ET.tostring(root, encoding="unicode")
    relabeled, retokenized = _packaged_supervision(changed)
    assert f"m 2\n{upper}\n{lower}\n" in labels[0]
    assert relabeled == labels
    assert retokenized == targets


def test_unison_ties_preserve_cross_system_addresses():
    """Two ties on C4 encode different graphs despite identical pitches."""
    from sheet_music_omr import assemble  # noqa: PLC0415

    starts = (
        "m 2\n1.1: D4/4[up] C4{tie@r2:start}/4[up]\n"
        "1.2: C4{tie@r1:start}/2[down]\n"
    )
    targets = []
    for upper, lower in ((2, 1), (1, 2)):
        ends = (
            f"m 3 right=final\n1.1: C4{{tie@r{upper}:end}}/4[up] E4/4[up]\n"
            f"1.2: C4{{tie@r{lower}:end}}/2[down]\n"
        )
        labels, tokens = _packaged_supervision(_duplex_xml(starts + ends))
        targets.append(tokens[1])
        result = assemble([parse_document(label) for label in labels])
        assert result.status.value == "success"
        assert result.text is not None
        assert starts + ends in result.text
    assert targets[0] != targets[1]


@pytest.mark.parametrize("line", ["?(1/2)", '<?>["p","pp"] D4/2[up]'])
def test_review_markers_rejected_at_variant_preparation(tmp_path, line):
    """A valid but unresolved score cannot become a prepared variant."""
    from sheet_music_omr import (  # noqa: PLC0415
        ReviewRequiredError,
        review_markers,
        validate_document,
    )

    source = _DUPLEX_HEADER + f"m 1 right=final\n1.1: {line}\n"
    score = parse_document(source)
    assert not validate_document(score)
    path = tmp_path / "review.v2"
    path.write_bytes(source.encode())
    plan = packaging._variant_plan(  # noqa: SLF001
        {
            "canonical_root": str(tmp_path),
            "canonical_v2_path": path.name,
            "canonical_v2_sha256": _sha(source.encode()),
            "measure_count": 1,
        },
        0,
    )
    with pytest.raises(ReviewRequiredError) as error:
        packaging._prepare_variant(plan, "verovio")  # noqa: SLF001
    assert error.value.markers == review_markers(score)
