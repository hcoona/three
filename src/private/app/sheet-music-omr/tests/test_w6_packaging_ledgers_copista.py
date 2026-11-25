"""Fixture-only W6 checks: no network, staged corpus or private inputs."""

from __future__ import annotations

import hashlib
import inspect
import io
import json
import os
import shutil
import sys
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
from sheet_music_omr.harness.systems import SystemStart, split_score
from sheet_music_omr.model import AssembledScore
from sheet_music_omr.musicxml_forward import to_musicxml_text
from sheet_music_omr.parser import parse_document
from sheet_music_omr.synthetic import intake, renderers
from sheet_music_omr.training import asset_locks, ledgers, packaging
from sheet_music_omr.training import copista_per_system as baseline
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


@pytest.fixture(autouse=True)
def _isolated_campaign_anchor(tmp_path, monkeypatch):
    monkeypatch.setattr(
        ledgers, "_CAMPAIGN_ANCHOR_PATH", tmp_path / "host" / "campaign.json"
    )


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
    if init_campaign:
        ledgers._init_w6_campaign_anchor(downloads)  # noqa: SLF001


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


def test_campaign_pins_both_ledgers_and_rejects_replaced_root(
    tmp_path, monkeypatch
):
    """Keep existing ledger bytes and pin both roots on explicit setup."""
    downloads = tmp_path / "downloads"
    _seed_prior_ledgers(downloads, monkeypatch, init_campaign=False)
    download_path = downloads / ledgers.W6_DOWNLOAD_LEDGER_NAME
    entry = {
        "schema": "omr-w6-slice-a-download-ledger-v1",
        "total_bytes": 17,
        "entries": [{"charged_bytes": 17}],
    }
    download_path.write_text(json.dumps(entry))
    _append_anchor(downloads, {})
    ledgers._init_w6_campaign_anchor(downloads)  # noqa: SLF001
    ledger = ledgers.W6DownloadLedger(downloads)
    assert ledger.total_bytes() == entry["total_bytes"]
    root = downloads.stat()
    runs = tmp_path / "runs"
    run_raw = (downloads / ledgers.W6_RUN_LEDGER_NAME).read_bytes()
    anchor = json.loads((tmp_path / "host/campaign.json").read_bytes())
    assert anchor == {
        "downloads_path": str(downloads),
        "downloads_root_id": [root.st_dev, root.st_ino],
        "runs_path": str(runs),
        "runs_root_id": [runs.stat().st_dev, runs.stat().st_ino],
        "download_ledger_bytes": len(download_path.read_bytes()),
        "download_ledger_sha256": _sha(json.dumps(entry).encode()),
        "run_ledger_bytes": len(run_raw),
        "run_ledger_prefix_sha256": _sha(run_raw),
    }
    downloads.rename(tmp_path / "old-downloads")
    _seed_prior_ledgers(downloads, monkeypatch, init_campaign=False)
    ledger.path.write_text(json.dumps(entry))
    with pytest.raises(StopConditionError, match="different root"):
        ledger.total_bytes()


def test_campaign_rejects_relocated_downloads_with_copied_prior_ledgers(
    tmp_path, monkeypatch
):
    """Five historical ledgers cannot authorize a second spending root."""
    downloads = tmp_path / "downloads"
    _seed_prior_ledgers(downloads, monkeypatch)
    ledgers.check_w6_disk_cap(downloads)
    alternate = tmp_path / "other" / "downloads"
    shutil.copytree(
        downloads, alternate, ignore=shutil.ignore_patterns("w6-slice-a-*")
    )
    ledgers.verify_prior_campaign_ledgers(alternate)
    with pytest.raises(StopConditionError, match="different root"):
        ledgers.W6DownloadLedger(alternate).total_bytes()
    with pytest.raises(StopConditionError, match="different root"):
        ledgers.reserve_w6_render_run(
            alternate, **_INVOCATION, command="render", planned_pages=1
        )
    assert not (alternate.parent / "runs").exists()
    assert not (alternate / ledgers.W6_DOWNLOAD_LEDGER_NAME).exists()


def test_campaign_operations_cannot_bootstrap_missing_anchor(
    tmp_path, monkeypatch
):
    """Deleting the registry does not reset any Slice A budget."""
    downloads = tmp_path / "downloads"
    _seed_prior_ledgers(downloads, monkeypatch, init_campaign=False)
    for operation in (
        lambda: ledgers.W6DownloadLedger(downloads),
        lambda: ledgers.check_w6_disk_cap(downloads),
        lambda: ledgers.reserve_w6_render_run(
            downloads, **_INVOCATION, command="render", planned_pages=1
        ),
        lambda: ledgers.reserve_w6_copista_invocation_and_read_anchor(
            downloads, **_INVOCATION, command="debug"
        ),
        lambda: ledgers.settle_w6_run(downloads, "unknown", status="failed"),
    ):
        with pytest.raises(
            StopConditionError, match="missing Slice A campaign anchor"
        ):
            operation()
    assert not (tmp_path / "host/campaign.json").exists()
    assert not (tmp_path / "runs").exists()


def test_campaign_registry_ignores_home(tmp_path, monkeypatch):
    """An environment override cannot create another campaign registry."""
    monkeypatch.setattr(ledgers, "_CAMPAIGN_ANCHOR_PATH", None)
    account_home = tmp_path / "account"
    monkeypatch.setattr(
        ledgers.pwd, "getpwuid", lambda _: SimpleNamespace(pw_dir=account_home)
    )
    downloads = tmp_path / "downloads"
    _seed_prior_ledgers(downloads, monkeypatch)
    monkeypatch.setenv("HOME", str(tmp_path / "override"))
    assert ledgers.check_w6_disk_cap(downloads) == 0
    alternate = tmp_path / "other" / "downloads"
    shutil.copytree(downloads, alternate)
    with pytest.raises(StopConditionError, match="different root"):
        ledgers._init_w6_campaign_anchor(alternate)  # noqa: SLF001
    assert not (tmp_path / "override").exists()


def test_migrate_w6_campaign_roots_preserves_charges_and_retained_runs(
    tmp_path, monkeypatch
):
    """Only an intact destination without leftover runs can become active."""
    old_downloads = tmp_path / "downloads"
    _seed_prior_ledgers(old_downloads, monkeypatch)
    monkeypatch.setattr(ledgers, "W6_CUMULATIVE_PAGE_CAP", 2)
    ledgers.reserve_w6_render_run(
        old_downloads, **_INVOCATION, command="render", planned_pages=2
    )
    old_run = tmp_path / "runs" / "w6-slice-a-fixture"
    old_run.mkdir()
    (old_run / "artifact").write_bytes(b"fixture")
    new_downloads = tmp_path / "other" / "downloads"
    shutil.copytree(old_downloads, new_downloads)
    migrate = ledgers.migrate_w6_campaign_roots
    migrate_args = old_downloads, new_downloads
    for name in (
        "w2-touch-log.jsonl",
        ledgers.W6_DOWNLOAD_LEDGER_NAME,
        ledgers.W6_RUN_LEDGER_NAME,
    ):
        ledger = new_downloads / name
        raw = ledger.read_bytes()
        ledger.write_bytes(b" " + raw[1:])
        with pytest.raises(StopConditionError, match="SHA-256"):
            migrate(*migrate_args)
        ledger.write_bytes(raw)
    with pytest.raises(StopConditionError, match="move all retained"):
        migrate(*migrate_args)
    new_runs = new_downloads.parent / "runs"
    new_runs.mkdir()
    old_run.rename(new_runs / old_run.name)
    with monkeypatch.context() as cap:
        cap.setattr(ledgers, "W6_DISK_CAP_BYTES", 1)
        with pytest.raises(StopConditionError, match="generated disk cap"):
            migrate(*migrate_args)
    assert ledgers.check_w6_disk_cap(old_downloads) == 0
    assert migrate(*migrate_args)["status"] == "migrated"
    assert ledgers.check_w6_disk_cap(new_downloads) == len(b"fixture")
    with pytest.raises(StopConditionError, match="different root"):
        ledgers.check_w6_disk_cap(old_downloads)
    with pytest.raises(StopConditionError, match="cumulative"):
        ledgers.reserve_w6_render_run(
            new_downloads, **_INVOCATION, command="render", planned_pages=1
        )


@pytest.mark.parametrize(
    "damage",
    [
        "download-missing",
        "download-bytes",
        "download-same-total",
        "run-missing",
        "run-lines",
        "run-same-size",
        "run-kind",
    ],
)
def test_campaign_rejects_ledger_rollback_after_commits(
    tmp_path, monkeypatch, damage
):
    """Even a different operation fails closed on either ledger's rollback."""
    downloads = tmp_path / "downloads"
    _seed_prior_ledgers(downloads, monkeypatch)
    data = b"fixture wheel"

    def transfer(_url, target, _size, _budget, wire):
        target.write_bytes(data)
        wire[0] = len(data)

    monkeypatch.setattr(ledgers, "_transfer_locked", transfer)
    ledger = ledgers.W6DownloadLedger(downloads)
    ledger.ensure_cached_file(
        url=_URL,
        dest=downloads / _WHEEL,
        expected_bytes=len(data),
        expected_sha256=_sha(data),
    )
    token = ledgers.reserve_w6_render_run(
        downloads, **_INVOCATION, command="render", planned_pages=2
    )
    ledgers.settle_w6_run(downloads, token, status="completed", actual_pages=2)
    run_ledger = downloads / ledgers.W6_RUN_LEDGER_NAME
    if damage == "download-missing":
        ledger.path.unlink()
    elif damage == "download-same-total":
        ledger.path.write_bytes(
            ledger.path.read_bytes().replace(b"completed", b"alternate")
        )
    elif damage == "download-bytes":
        short = json.loads(ledger.path.read_bytes())
        short["entries"][0]["charged_bytes"] -= 1
        short["total_bytes"] -= 1
        ledger.path.write_text(json.dumps(short))
    elif damage == "run-missing":
        run_ledger.unlink()
    elif damage == "run-same-size":
        run_ledger.write_bytes(
            run_ledger.read_bytes().replace(
                b'"charged_pages":2', b'"charged_pages":1'
            )
        )
    elif damage == "run-kind":
        run_ledger.write_bytes(
            run_ledger.read_bytes().replace(
                b'"kind":"render"', b'"kind":"copista"'
            )
        )
    else:
        run_ledger.write_text(run_ledger.read_text().splitlines()[0] + "\n")
    with pytest.raises(StopConditionError, match=r"missing|SHA-256"):
        ledgers.check_w6_disk_cap(downloads)


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
    with pytest.raises(
        StopConditionError, match="missing Slice A campaign anchor"
    ):
        ledgers.setup_w6_slice_a(downloads)
    assert not commands
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
    runs.rename(tmp_path / "old-runs")
    runs.mkdir()
    with pytest.raises(StopConditionError, match="different root"):
        ledgers.check_w6_disk_cap(downloads, runs)


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
    """The latest anchor and cap apply to debug as well as formal runs."""
    reserve = ledgers.reserve_w6_copista_invocation_and_read_anchor
    downloads = tmp_path / "downloads"
    _seed_prior_ledgers(downloads, monkeypatch)
    with pytest.raises(StopConditionError, match="anchor"):
        reserve(downloads, **_INVOCATION, command="debug")
    anchor = {
        "root_st_dev": 1,
        "root_st_ino": 10,
        "staging_attestation_sha256": _sha(b"attestation"),
        "portable_aggregate_sha256": ledgers.EXPECTED_TRAINDEV_PORTABLE_SHA256,
    }
    _append_anchor(
        downloads, anchor | {"portable_aggregate_sha256": _sha(b"old")}
    )
    with pytest.raises(StopConditionError, match="anchor"):
        reserve(downloads, **_INVOCATION, command="debug")
    _append_anchor(downloads, anchor)
    for i in range(10):
        token, read_anchor = reserve(
            downloads, **_INVOCATION, command=f"debug-{i}"
        )
        assert read_anchor == {"event": "traindev-root-anchor"} | anchor
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
        "root_st_dev": 1,
        "root_st_ino": 10,
        "staging_attestation_sha256": _sha(b"attestation"),
        "portable_aggregate_sha256": ledgers.EXPECTED_TRAINDEV_PORTABLE_SHA256,
    }
    (downloads / ledgers.W6_RUN_LEDGER_NAME).write_text(
        json.dumps(anchor) + "\n"
    )
    _, observed = ledgers.reserve_w6_copista_invocation_and_read_anchor(
        downloads, **_INVOCATION, command="debug"
    )
    assert observed == anchor


@pytest.fixture
def sealed(tmp_path, monkeypatch):
    """Stage an invented leaf namespace with portable and inode attestations."""
    worktree = tmp_path / "worktree"
    worktree.mkdir()
    root = tmp_path / "outside" / "sealed"
    root.mkdir(parents=True)
    rels: list[tuple[str | None, str]] = [(None, _MANIFEST)]
    for i, gid in enumerate(sorted(ledgers.TRAINDEV_GOLD_IDS)):
        rels.extend(
            (gid, f"pages/{gid}-p{page:02d}.png")
            for page in range(1, 6 if i < 5 else 5)  # noqa: PLR2004
        )
        rels.extend(
            (gid, f"gold/{gid}/{k}.musicxml") for k in ("accepted", "draft")
        )
    leaves = []
    portable = []
    expected_rel = "pages/gold-07-p01.png"
    for gid, rel in rels:
        leaf = root / rel
        leaf.parent.mkdir(parents=True, exist_ok=True)
        data = b"fixture payload"
        leaf.write_bytes(data)
        leaf.chmod(0o400)
        observed = leaf.stat()
        portable.append((gid or "", rel, _sha(data)))
        leaves.append(
            {
                "gold_id": gid,
                "relpath": rel,
                "expected_sha256": _sha(data),
                "st_dev": observed.st_dev,
                "st_ino": observed.st_ino,
                "st_size": observed.st_size,
                "st_mtime_ns": observed.st_mtime_ns,
            }
        )
    stat_root = root.stat()
    packed = json.dumps(
        {
            "leaves": sorted(portable),
            "traindev_manifest_sha256": _sha(data),
        },
        sort_keys=True,
        separators=(",", ":"),
    ).encode()
    attestation = json.dumps(
        {
            "leaves": leaves,
            "traindev_manifest_sha256": _sha(data),
            "root_st_dev": stat_root.st_dev,
            "root_st_ino": stat_root.st_ino,
        }
    ).encode()
    (root / "staging-attestation.json").write_bytes(attestation)
    (root / "staging-attestation.json").chmod(0o400)
    folders = [root, *(p for p in root.rglob("*") if p.is_dir())]
    for folder in folders:
        folder.chmod(0o500)
    digest = _sha(packed)
    monkeypatch.setattr(ledgers, "EXPECTED_TRAINDEV_PORTABLE_SHA256", digest)
    anchor = {
        "root_st_dev": stat_root.st_dev,
        "root_st_ino": stat_root.st_ino,
        "staging_attestation_sha256": _sha(attestation),
        "portable_aggregate_sha256": digest,
    }
    yield root, anchor, worktree, expected_rel
    for folder in folders:
        if folder.is_dir():
            folder.chmod(0o700)


def test_sealed_root_rejects_symlinked_ancestor_and_test_leaf(
    tmp_path,
    monkeypatch,
    sealed,
):
    """Even a correct anchored root cannot be reached through a symlink."""
    root, anchor, worktree, rel = sealed
    alias = tmp_path / "symlink"
    alias.symlink_to(root.parent, target_is_directory=True)
    with pytest.raises(StopConditionError, match="authenticate"):
        ledgers.open_verified_traindev_root(
            alias / root.name, anchor, [worktree]
        )
    with ledgers.open_verified_traindev_root(
        root, anchor, [worktree]
    ) as verified:
        assert verified.read_verified_leaf_bytes(rel) == b"fixture payload"
        monkeypatch.setattr(
            os,
            "open",
            lambda *_a, **_k: pytest.fail("opened nonallowlisted leaf"),
        )
        with pytest.raises(StopConditionError, match="allowlist"):
            verified.read_verified_leaf_bytes("pages/gold-01-p01.png")


def test_sealed_root_rejects_replaced_root_and_attestation_before_read(
    monkeypatch,
    sealed,
):
    """A substituted root inode cannot authenticate even identical metadata."""
    root, anchor, worktree, _ = sealed
    old = root.with_name("old")
    root.rename(old)
    root.mkdir()
    attestation = root / "staging-attestation.json"
    attestation.write_bytes((old / attestation.name).read_bytes())
    attestation.chmod(0o400)
    root.chmod(0o500)
    try:
        monkeypatch.setattr(
            os, "read", lambda *_: pytest.fail("read replaced root bytes")
        )
        with pytest.raises(StopConditionError, match="external anchor"):
            ledgers.open_verified_traindev_root(root, anchor, [worktree])
    finally:
        root.chmod(0o700)
        attestation.unlink()
        root.rmdir()
        old.rename(root)


def test_sealed_leaf_rejects_same_size_substitution_before_payload_read(
    monkeypatch,
    sealed,
):
    """Spy on the substituted inode; no read(2) may target that payload."""
    root, anchor, worktree, rel = sealed
    leaf = root / rel
    observed = leaf.stat()
    outside = root.parent / "replacement"
    outside.write_bytes(b"fixture altered")
    outside.chmod(0o400)
    os.utime(outside, ns=(observed.st_atime_ns, observed.st_mtime_ns))
    changed = outside.stat()
    assert changed.st_size == observed.st_size
    assert changed.st_mtime_ns == observed.st_mtime_ns
    leaf.parent.chmod(0o700)
    outside.replace(leaf)
    leaf.parent.chmod(0o500)
    original = os.read
    payload_reads = []

    def watch(fd, count):
        if os.fstat(fd).st_ino == changed.st_ino:
            payload_reads.append(count)
        return original(fd, count)

    monkeypatch.setattr(os, "read", watch)
    with (
        ledgers.open_verified_traindev_root(
            root, anchor, [worktree]
        ) as verified,
        pytest.raises(StopConditionError, match="changed before read"),
    ):
        verified.read_verified_leaf_bytes(rel)
    assert not payload_reads


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


def test_baseline_requires_prestart_anchor_before_root_access(
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
    with pytest.raises(StopConditionError, match="anchor"):
        baseline.run_copista_per_system(
            downloads,
            run,
            traindev_root=private,
            formal=False,
        )
    assert not opened
    portable_sha256 = ledgers.EXPECTED_TRAINDEV_PORTABLE_SHA256
    _append_anchor(
        downloads,
        {
            "root_st_dev": 1,
            "root_st_ino": 10,
            "staging_attestation_sha256": _sha(b"attestation"),
            "portable_aggregate_sha256": portable_sha256,
        },
    )
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


def test_packaging_writes_crops_and_cowarped_boxes_with_prior_header(
    tmp_path, monkeypatch
):
    """Verify whole shards, gap-free header state and geometry against PNGs."""
    _seed_prior_ledgers(tmp_path / "downloads", monkeypatch)
    run = tmp_path / "runs" / "w6-slice-a-fixture"
    packaging.package_rendered_variants_to_datasets(
        [
            _rendered_fixture("tr", "synth_train"),
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
        "4.0",
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
        packaging,
        "ACCEPTED_MUSESCORE_IDENTITIES",
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
            lambda *_: (
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
    assert meter.actual == (9 if engine == "verovio" else 8)
    assert not meter.uncounted_worker


@pytest.mark.parametrize("engine", ["verovio", "musescore"])
def test_renderer_checks_successful_scratch_before_removal(
    tmp_path, monkeypatch, engine
):
    """A normal renderer return still counts its scratch against the cap."""
    rows = _render_inputs(tmp_path, monkeypatch, engine, 1)
    headroom = packaging.WORKER_SCRATCH_HEADROOM_BYTES
    required = (
        packaging._musescore_worst_case_bytes(  # noqa: SLF001
            8, f"xml:{rows[0]['canonical_v2_sha256']}"
        )
        if engine == "musescore"
        else headroom
    )
    cap = required + headroom
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

    meter.disk.usage = cap - required + headroom // 2
    with pytest.raises(StopConditionError, match="generated disk cap"):
        attempt()
    assert not calls
    meter.disk.usage = 0
    with pytest.raises(StopConditionError, match="generated disk cap"):
        attempt()
    assert calls == [min(8, cap // headroom)]
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
        (["--no-formal"], "missing Slice A campaign anchor"),
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
    assert op25_13 is not None
    assert op25_1 is not None
    assert op25_13.matches(op25_1, conservative_exclusion=True)
    assert op25_1.matches(op25_13, conservative_exclusion=True)
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


def test_download_ledger_recovers_interrupted_two_phase_anchor_write(
    tmp_path, monkeypatch
):
    """Restart finalizes an interrupted pending_download_ledger_sha256 write."""
    dl = tmp_path / "downloads"
    _seed_prior_ledgers(dl, monkeypatch)
    ledger = ledgers.W6DownloadLedger(dl)
    data = {
        "schema": "omr-w6-slice-a-download-ledger-v1",
        "total_bytes": 42,
        "entries": [{"charged_bytes": 42}],
    }
    raw = (
        json.dumps(data, sort_keys=True, separators=(",", ":")).encode() + b"\n"
    )
    anchor_path = tmp_path / "host" / "campaign.json"
    anchor = json.loads(anchor_path.read_bytes())
    anchor["pending_download_ledger_bytes"] = len(raw)
    anchor["pending_download_ledger_sha256"] = _sha(raw)
    anchor_path.write_bytes(
        json.dumps(anchor, sort_keys=True, separators=(",", ":")).encode()
        + b"\n"
    )
    (dl / ledgers.W6_DOWNLOAD_LEDGER_NAME).write_bytes(raw)
    stale_dest = tmp_path / "dest" / "downloads"
    _seed_prior_ledgers(stale_dest, monkeypatch, init_campaign=False)
    (stale_dest / ledgers.W6_DOWNLOAD_LEDGER_NAME).write_text(
        json.dumps(
            {
                "schema": "omr-w6-slice-a-download-ledger-v1",
                "total_bytes": 0,
                "entries": [],
            },
            sort_keys=True,
            separators=(",", ":"),
        )
        + "\n"
    )
    with pytest.raises(StopConditionError, match="download ledger"):
        ledgers.migrate_w6_campaign_roots(dl, stale_dest)
    assert ledger.total_bytes() == 42  # noqa: PLR2004

    # Reverse crash state: source still has committed bytes while destination
    # has pending bytes — rejected migration must NOT poison source's anchor!
    committed_raw = (
        json.dumps(
            {
                "schema": "omr-w6-slice-a-download-ledger-v1",
                "total_bytes": 42,
                "entries": [{"charged_bytes": 42}],
            },
            sort_keys=True,
            separators=(",", ":"),
        ).encode()
        + b"\n"
    )
    pending_raw = (
        json.dumps(
            {
                "schema": "omr-w6-slice-a-download-ledger-v1",
                "total_bytes": 99,
                "entries": [{"charged_bytes": 99}],
            },
            sort_keys=True,
            separators=(",", ":"),
        ).encode()
        + b"\n"
    )
    (dl / ledgers.W6_DOWNLOAD_LEDGER_NAME).write_bytes(committed_raw)
    (stale_dest / ledgers.W6_DOWNLOAD_LEDGER_NAME).write_bytes(pending_raw)
    anchor2 = json.loads(anchor_path.read_bytes())
    anchor2["pending_download_ledger_bytes"] = len(pending_raw)
    anchor2["pending_download_ledger_sha256"] = _sha(pending_raw)
    anchor_path.write_bytes(
        json.dumps(anchor2, sort_keys=True, separators=(",", ":")).encode()
        + b"\n"
    )
    with pytest.raises(StopConditionError, match="download ledger"):
        ledgers.migrate_w6_campaign_roots(dl, stale_dest)
    finalized = json.loads(anchor_path.read_bytes())
    assert "pending_download_ledger_sha256" not in finalized
    assert finalized["download_ledger_sha256"] == _sha(committed_raw)
    assert ledger.total_bytes() == 42  # noqa: PLR2004


def test_migrate_w6_campaign_roots_before_any_run_ledger(tmp_path, monkeypatch):
    """Setup-only campaigns without a run ledger can still relocate cleanly."""
    old_dl = tmp_path / "old" / "downloads"
    new_dl = tmp_path / "new" / "downloads"
    _seed_prior_ledgers(old_dl, monkeypatch, init_campaign=False)
    ledgers._init_w6_campaign_anchor(old_dl)  # noqa: SLF001
    _seed_prior_ledgers(new_dl, monkeypatch, init_campaign=False)
    (new_dl / ledgers.W6_DOWNLOAD_LEDGER_NAME).write_bytes(
        (old_dl / ledgers.W6_DOWNLOAD_LEDGER_NAME).read_bytes()
    )
    assert not (old_dl / ledgers.W6_RUN_LEDGER_NAME).exists()
    out = ledgers.migrate_w6_campaign_roots(old_dl, new_dl)
    assert out == {"status": "migrated"}
    assert ledgers.W6DownloadLedger(new_dl).total_bytes() == 0


@pytest.mark.parametrize(
    ("accepted_sha", "infer_text", "expected"),
    [
        ("f" * 64, "false", "mutable-source recheck"),
        ("0" * 64, "true", "importInferTextType=false"),
    ],
)
def test_musescore_output_bound_preconditions_block_dispatch(
    tmp_path, monkeypatch, accepted_sha, infer_text, expected
):
    """Unaccepted binaries or enabled jump inference stop before rendering."""
    from sheet_music_omr.harness.confine import Confinement  # noqa: PLC0415
    from sheet_music_omr.harness.musescore import MuseScore  # noqa: PLC0415

    ms = MuseScore(
        executable=tmp_path / "MuseScore4",
        version="4.7.7",
        executable_sha256="0" * 64,
        settings=tmp_path / "MuseScore4.ini",
        confinement=Confinement(tmp_path / "run", tmp_path / "dl"),
        baseline=None,
        baseline_preferences={
            "import/musicXml/importInferTextType": {"value": infer_text}
        },
    )
    monkeypatch.setattr(
        packaging, "ACCEPTED_MUSESCORE_IDENTITIES", {("4.7.7", accepted_sha)}
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
    with pytest.raises(StopConditionError, match=expected):
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
    assert not called
