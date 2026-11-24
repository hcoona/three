"""Contract checks for the W4/W5 fuses, offline supervisor and CLI."""

from __future__ import annotations

import hashlib
import io
import json
import sys
from pathlib import Path
from types import MappingProxyType, SimpleNamespace

import pytest
from PIL import Image, ImageDraw

from sheet_music_omr.harness import privacy, records
from sheet_music_omr.harness.stops import StopConditionError
from sheet_music_omr.synthetic import cli, ledgers
from sheet_music_omr.synthetic.ledgers import (
    W4W5DownloadLedger,
    W4W5RunLedger,
    check_metered_proof_disk_cap,
    run_offline_isolated_subprocess,
    verify_loopback_only_unshare,
)

_FIXTURES = Path(__file__).parent / "fixtures" / "assembly"
_URL = "https://example.invalid/fixture.whl"
_STOP = 2
_TRAIN_PAGES = 69


class _Response(io.BytesIO):
    headers = MappingProxyType({})


def _read_events(ledger):
    return [
        json.loads(line)
        for line in ledger.ledger_path.read_text(encoding="utf-8").splitlines()
    ]


def _reserve(ledger, run, command, pages):
    return ledger.reserve(
        run_id=run,
        command=command,
        reserved_pages=pages,
        git_commit="recorded-fixture-commit",
        clean_tree=True,
    )


def test_download_reserves_before_transfer_and_reuses_verified_sha(
    tmp_path, monkeypatch
):
    """Make the reservation visible before wire I/O and reuse verified bytes."""
    ledger = W4W5DownloadLedger(tmp_path)
    dest = tmp_path / "fixture.whl"
    payload = b"fixture wheel bytes"
    digest = hashlib.sha256(payload).hexdigest()

    def open_response(*_args, **_kwargs):
        state = json.loads(ledger.ledger_path.read_text("utf-8"))
        (row,) = state["entries"]
        assert row["status"] == "reserved"
        assert state["total_bytes"] == row["reserved_bytes"] > len(payload)
        with pytest.raises(StopConditionError, match="unreconciled reserved"):
            W4W5DownloadLedger(tmp_path).ensure_cached_file(
                url=_URL,
                dest=tmp_path / "competing.whl",
                expected_bytes=1,
            )
        return _Response(payload)

    monkeypatch.setattr(ledgers.urllib.request, "urlopen", open_response)
    kwargs = {
        "url": _URL,
        "dest": dest,
        "expected_bytes": len(payload),
        "expected_sha256": digest,
    }
    assert ledger.ensure_cached_file(**kwargs) == digest
    before = ledger.ledger_path.read_bytes()
    assert dest.read_bytes() == payload
    assert ledger.total_bytes() == len(payload)
    assert ledger.ensure_cached_file(**kwargs) == digest
    assert ledger.ledger_path.read_bytes() == before
    dest.write_bytes(b"X" * len(payload))
    with pytest.raises(StopConditionError, match="SHA-256"):
        ledger.ensure_cached_file(**kwargs)

    # Partial transfer failure retains full reservation rather than 1 byte.
    monkeypatch.setattr(
        ledgers.urllib.request,
        "urlopen",
        lambda *_a, **_kw: _Response(b"short"),
    )
    prior_total = ledger.total_bytes()
    with pytest.raises(StopConditionError, match="size 5 != 50"):
        ledger.ensure_cached_file(
            url=_URL, dest=tmp_path / "partial.whl", expected_bytes=50
        )
    assert ledger.total_bytes() >= prior_total + 50 + 16 * 1024


def test_download_stops_before_15_gb_and_returns_to_owner(tmp_path):
    """Reject the reservation before any transfer or ledger change."""
    ledger = W4W5DownloadLedger(tmp_path)
    with pytest.raises(
        StopConditionError, match=r"15000000000.*return to repository"
    ):
        ledger.ensure_cached_file(
            url=_URL,
            dest=tmp_path / "oversized.whl",
            expected_bytes=15_000_000_000,
        )
    assert not ledger.ledger_path.exists()


def test_unreconciled_download_blocks_even_cached_reuse(tmp_path):
    """Fail closed without altering the unresolved byte charge."""
    ledger = W4W5DownloadLedger(tmp_path)
    cached = tmp_path / "present.whl"
    cached.write_bytes(b"x")
    pending = {
        "schema": "w4-w5-download-ledger-v1",
        "total_bytes": 100,
        "entries": [{"status": "reserved", "reserved_bytes": 100}],
    }
    original = json.dumps(pending)
    ledger.ledger_path.write_text(original, "utf-8")
    with pytest.raises(StopConditionError, match="unreconciled reserved"):
        ledger.ensure_cached_file(url=_URL, dest=cached, expected_bytes=1)
    assert ledger.ledger_path.read_text("utf-8") == original


def test_run_ledger_charges_joint_page_cap_across_commands(tmp_path):
    """Pool pages from both W4 and W5 instead of applying separate caps."""
    ledger = W4W5RunLedger(tmp_path)
    runs = [
        *((f"s3-{index}", "s3-proof", 5_000) for index in range(4)),
        ("s3-last", "s3-proof", 4_839),
        ("train", "w5-traindev-69", 69),
        ("locked", "w5-locked-92", 92),
    ]
    for run, command, pages in runs:
        reservation = _reserve(ledger, run, command, pages)
        ledger.settle(reservation, actual_pages=pages, status="completed")
    with pytest.raises(StopConditionError, match=r"25000 \+ 1.*25000"):
        _reserve(ledger, "overflow", "s3-proof", 1)


def test_crashed_run_retains_full_reservation_and_blocks_next_start(tmp_path):
    """Preserve the complete reservation across independent ledger objects."""
    ledger = W4W5RunLedger(tmp_path)
    _reserve(ledger, "interrupted", "s3-proof", 4_000)
    with pytest.raises(
        StopConditionError, match="unreconciled crashed formal run"
    ):
        _reserve(W4W5RunLedger(tmp_path), "next", "w5-traindev-69", 69)
    (row,) = _read_events(ledger)
    assert (row["status"], row["reserved_pages"]) == ("started", 4_000)
    assert "actual_pages" not in row


@pytest.mark.parametrize(
    ("command", "pages", "attempts", "reason"),
    [
        ("w5-traindev-69", 69, 20, "TRAIN_DEV starts would exceed 20"),
        ("w5-locked-92", 92, 3, "3-attempt circuit breaker"),
    ],
)
def test_w5_failed_starts_consume_attempt_budget(
    tmp_path, command, pages, attempts, reason
):
    """Count failed attempts even when there were no evaluated pages."""
    ledger = W4W5RunLedger(tmp_path)
    for attempt in range(attempts):
        reservation = _reserve(ledger, str(attempt), command, pages)
        ledger.settle(reservation, actual_pages=0, status="failed")
    with pytest.raises(StopConditionError, match=reason):
        _reserve(ledger, "one-too-many", command, pages)
    assert len(_read_events(ledger)) == 2 * attempts


def test_locked92_completion_immediately_blocks_further_starts(tmp_path):
    """Do not treat unused failure-attempt slots as another allowed run."""
    ledger = W4W5RunLedger(tmp_path)
    first = _reserve(ledger, "locked-first", "w5-locked-92", 92)
    ledger.settle(first, actual_pages=92, status="completed")
    with pytest.raises(StopConditionError, match="already completed once"):
        _reserve(ledger, "locked-second", "w5-locked-92", 92)
    assert [row["status"] for row in _read_events(ledger)] == [
        "started",
        "completed",
    ]


def test_disk_fuse_sums_only_metered_proof_directories(tmp_path):
    """Pool nested proof bytes without charging an older unmetered run."""
    metered = 24
    for directory, size in (
        ("w4-w5-one", 11),
        ("w4-w5-two", 13),
        ("earlier", 30),
    ):
        root = tmp_path / directory / "nested"
        root.mkdir(parents=True)
        (root / "artifact").write_bytes(b"x" * size)
    assert check_metered_proof_disk_cap(tmp_path, cap_bytes=25) == metered
    with pytest.raises(StopConditionError, match="24-byte disk cap"):
        check_metered_proof_disk_cap(tmp_path, cap_bytes=24)


@pytest.fixture
def linux_namespaces():
    """Require the host's network/PID namespace contract for worker tests."""
    if sys.platform != "linux":
        pytest.skip("Linux network and PID namespaces are required")
    try:
        verify_loopback_only_unshare()
    except StopConditionError as exc:
        pytest.skip(f"host cannot establish loopback-only namespaces: {exc}")


@pytest.mark.usefixtures("linux_namespaces")
def test_offline_worker_sees_only_loopback(tmp_path):
    """Observe actual worker interfaces rather than only testing its command."""
    result = run_offline_isolated_subprocess(
        [
            sys.executable,
            "-c",
            (
                "from pathlib import Path; print(','.join("
                "line.split(':', 1)[0].strip() for line in "
                "Path('/proc/net/dev').read_text().splitlines()[2:]))"
            ),
        ],
        scratch_root=tmp_path,
    )
    assert result.returncode == 0
    assert result.stdout_sanitized.strip() == "lo"
    assert not list(tmp_path.iterdir())


@pytest.mark.usefixtures("linux_namespaces")
def test_inner_timeout_reaps_detached_grandchild_before_scratch_cleanup(
    tmp_path,
):
    """Observe the detached grandchild's death before scratch is removed."""
    grandchild = """
import time
from pathlib import Path

status = Path('/proc/self/status').read_text().splitlines()
nspid = next(line for line in status if line.startswith('NSpid:'))
Path('grandchild').write_text(nspid.split()[1])
time.sleep(30)
"""
    worker = """
import subprocess
import sys
import time

subprocess.Popen([sys.executable, '-c', sys.argv[1]], start_new_session=True)
time.sleep(30)
"""
    seen = []

    def after_reap(scratch):
        pid = int((scratch / "grandchild").read_text("utf-8"))
        assert not Path(f"/proc/{pid}").exists()
        seen.append(scratch)

    result = run_offline_isolated_subprocess(
        [sys.executable, "-c", worker, grandchild],
        scratch_root=tmp_path,
        deadline_seconds=2,
        on_before_cleanup=after_reap,
    )
    assert result.returncode in {124, 137}
    (scratch,) = seen
    assert not scratch.exists()


@pytest.mark.usefixtures("linux_namespaces")
def test_offline_worker_scrubs_paths_and_title_lines(tmp_path):
    """Keep staged paths and names out of returned worker diagnostics."""
    private = tmp_path / "private-score.png"
    private.write_bytes(b"fixture")
    result = run_offline_isolated_subprocess(
        [
            sys.executable,
            "-c",
            (
                "import sys; print(sys.argv[1]); "
                "print('title: local fixture identity', file=sys.stderr)"
            ),
            "{SCRATCH_PNG}",
        ],
        scratch_root=tmp_path / "workers",
        input_page_png=private,
    )
    assert result.returncode == 0
    assert "<path>" in result.stdout_sanitized
    assert "[title-scrubbed]" in result.stderr_sanitized
    privacy.scan(
        result.stdout_sanitized + result.stderr_sanitized,
        (str(tmp_path), "local fixture identity", private.name),
    )


@pytest.fixture
def cli_paths(tmp_path, monkeypatch):
    """Give the CLI two read-only worktrees and disjoint external roots."""
    repo = tmp_path / "repo"
    repo.mkdir()
    alternate = tmp_path / "linked-worktree"
    alternate.mkdir()
    monkeypatch.setattr(cli, "repository_root", lambda: repo)
    monkeypatch.setattr(records, "worktree_roots", lambda: (repo, alternate))
    return {
        "repo": repo,
        "alternate": alternate,
        "run_dir": tmp_path / "w4-w5-test",
        "downloads": tmp_path / "downloads",
        "w3_run_dir": tmp_path / "w3",
    }


def _arguments(paths, **overrides):
    args = []
    for key in ("run_dir", "downloads", "w3_run_dir"):
        args += [
            "--" + key.replace("_", "-"),
            str(overrides.get(key, paths[key])),
        ]
    return args


@pytest.mark.parametrize(
    "arguments",
    [
        [],
        ["setup-w4-w5"],
        ["validate-assembly", "--expected-systems", "0", "not-read"],
        ["run-w5-detectors", "--mode", "invalid"],
    ],
)
def test_cli_rejects_invalid_arguments(arguments, capsys):
    """Reject invalid or incomplete commands before accessing inputs."""
    with pytest.raises(SystemExit) as exc:
        cli.main(arguments)
    assert exc.value.code == _STOP
    assert "error:" in capsys.readouterr().err


@pytest.mark.parametrize(
    ("key", "worktree"), [("run_dir", "repo"), ("downloads", "alternate")]
)
def test_cli_refuses_write_roots_in_any_worktree(
    cli_paths, capsys, key, worktree
):
    """Apply the same confinement to the primary and linked worktrees."""
    blocked = cli_paths[worktree] / "blocked"
    assert (
        cli.main(["run-s3", *_arguments(cli_paths, **{key: blocked})]) == _STOP
    )
    assert not blocked.exists()
    assert "outside the repository" in capsys.readouterr().err


@pytest.mark.parametrize(
    ("flags", "reason"),
    [
        ([], "requires --formal"),
        (["--formal", "--no-homr"], "cannot use --no-homr"),
        (["--formal"], "requires a clean project code tree"),
    ],
)
def test_locked92_stops_before_inputs_on_missing_formal_or_dirty_tree(
    cli_paths, monkeypatch, capsys, flags, reason
):
    """Leave a sanitized stop and no formal start when preflight rejects."""
    monkeypatch.setattr(
        cli, "source_state", lambda: {"commit": "fixture", "dirty": True}
    )

    def no_reads(*_args, **_kwargs):
        pytest.fail(
            "locked92 touched its inputs before validating authorization"
        )

    monkeypatch.setattr(cli, "verify_w3_snapshot_attestation", no_reads)
    assert (
        cli.main(
            [
                "run-w5-detectors",
                *_arguments(cli_paths),
                "--mode",
                "locked92",
                *flags,
            ]
        )
        == _STOP
    )
    record = (cli_paths["run_dir"] / "run.json").read_text("utf-8")
    assert reason in record
    assert reason in capsys.readouterr().err
    privacy.scan(record, tuple(str(path) for path in cli_paths.values()))
    assert not W4W5RunLedger(cli_paths["downloads"]).ledger_path.exists()


def test_formal_run_rejects_unmetered_directory_name(cli_paths, capsys):
    """Ensure every formal output is charged to the disk meter."""
    unmetered = cli_paths["run_dir"].with_name("unmetered")
    assert (
        cli.main(
            ["run-s3", *_arguments(cli_paths, run_dir=unmetered), "--formal"]
        )
        == _STOP
    )
    assert "must start with w4-w5-" in capsys.readouterr().err


def test_formal_detector_failure_settles_partial_pages_and_scrubs_stop(
    cli_paths, monkeypatch, capsys
):
    """Reserve before input verification and charge work on a sanitized stop."""
    downloads = cli_paths["downloads"]
    ledger = W4W5RunLedger(downloads)
    monkeypatch.setattr(
        cli, "source_state", lambda: {"commit": "fixture", "dirty": False}
    )
    monkeypatch.setattr(cli, "verify_loopback_only_unshare", lambda: None)

    def attest(_w3, *, include_test_pages):
        assert not include_test_pages
        (start,) = _read_events(ledger)
        assert start["status"] == "started"
        assert start["command"] == "w5-traindev-69"
        assert start["reserved_pages"] == _TRAIN_PAGES
        return {"geometry_sha256": "fixture"}

    def fail(_conf, _pages, counter, *, homr_enabled):
        assert not homr_enabled
        counter.add(7)
        msg = f"worker failed at {downloads}/private.png\nTitle: local score"
        raise StopConditionError(msg)

    monkeypatch.setattr(cli, "verify_w3_snapshot_attestation", attest)
    monkeypatch.setattr(
        cli, "verify_copisteria_live_assets", lambda *_: "fixture-prov"
    )
    monkeypatch.setattr(cli, "_detector_pages", lambda *_args, **_kwargs: [])
    monkeypatch.setattr(cli, "_benchmark", fail)
    assert (
        cli.main(
            [
                "run-w5-detectors",
                *_arguments(cli_paths),
                "--formal",
                "--no-homr",
            ]
        )
        == _STOP
    )
    _, settlement = _read_events(ledger)
    assert (settlement["status"], settlement["actual_pages"]) == ("failed", 7)
    run = cli_paths["run_dir"]
    output = (run / "run.json").read_text("utf-8") + capsys.readouterr().err
    assert "worker failed" in output
    privacy.scan(output, (str(downloads), "private.png", "local score"))
    assert not (run / "report" / "w5-detector-report.json").exists()


def test_locked92_report_scores_geometry_without_notation_access(
    cli_paths, monkeypatch, capsys
):
    """Keep the holdout geometry-only and compute the decision on TRAIN_DEV."""
    w3 = cli_paths["w3_run_dir"]
    (w3 / "alignment").mkdir(parents=True)
    (w3 / "pages").mkdir()
    pieces = []
    box = (2, 2, 14, 14)
    for gid, split, group in (
        ("gold-07", "TRAIN_DEV", "G1"),
        ("gold-01", "TEST", "G2"),
    ):
        Image.new("L", (16, 16), 255).save(w3 / "pages" / f"{gid}-p01.png")
        pieces.append(
            {
                "gold_id": gid,
                "split": split,
                "group": group,
                "pages": [{"page_index": 1}],
                "systems": [{"page_index": 1, "facsimile_box_px": box}],
            }
        )
    (w3 / "alignment" / "manifest.json").write_text(
        json.dumps({"pieces": pieces}), "utf-8"
    )
    for name in ("_TRAIN_PAGES", "_TRAIN_SYSTEMS"):
        monkeypatch.setattr(cli, name, 1)
    for name in ("_LOCKED_PAGES", "_LOCKED_SYSTEMS"):
        monkeypatch.setattr(cli, name, 2)
    monkeypatch.setattr(cli, "EXPECTED_PAGE_SIZE", (16, 16))
    monkeypatch.setattr(
        cli, "source_state", lambda: {"commit": "fixture", "dirty": False}
    )
    monkeypatch.setattr(
        cli, "run_git", lambda args: "" if args[0] == "status" else "fixture"
    )
    monkeypatch.setattr(cli, "verify_loopback_only_unshare", lambda: None)

    probed: list[bool] = []

    def attest(_w3, *, include_test_pages):
        assert include_test_pages
        assert probed == [True]
        return {"geometry_sha256": "fixture"}

    def decide(train_dev):
        assert set(train_dev) == {"comb_dp_blind", "copisteria_front"}
        assert {partition.n_gold for partition in train_dev.values()} == {1}
        return _decision(train_dev)

    _decision = cli.evaluate_no_train_decision
    monkeypatch.setattr(cli, "verify_w3_snapshot_attestation", attest)
    monkeypatch.setattr(
        cli, "verify_copisteria_live_assets", lambda *_: "fixture-prov"
    )
    monkeypatch.setattr(
        cli,
        "verify_w4_w5_live_assets",
        lambda *_a, **_kw: {"homr": {"enabled": True, "homr_version": "0.7.1"}},
    )
    monkeypatch.setattr(cli, "render_synthetic_probe_png", lambda *_: b"png")
    monkeypatch.setattr(cli, "evaluate_no_train_decision", decide)
    monkeypatch.setattr(
        cli, "detect_grand_staff_systems_blind", lambda _: [(*box, 2)]
    )
    monkeypatch.setattr(
        cli, "run_copisteria_front_detector", lambda pages, *_: {pages[0]: []}
    )

    def fake_homr(pages, *_):
        if pages[0].name == "probe.png":
            probed.append(True)
        return {
            pages[0]: SimpleNamespace(
                boxes=(),
                unscorable_page_frame=True,
                position_row_count=1,
                returncode=0,
            )
        }

    monkeypatch.setattr(cli, "run_homr_cli_detector", fake_homr)
    assert (
        cli.main(
            [
                "run-w5-detectors",
                *_arguments(cli_paths),
                "--mode",
                "locked92",
                "--formal",
            ]
        )
        == 0
    )
    capsys.readouterr()
    run = cli_paths["run_dir"]
    report = json.loads(
        (run / "report/w5-detector-report.json").read_text("utf-8")
    )
    assert report["assembly"]["status"] == "not_run_geometry_only"
    blind = report["detectors"]["comb_dp_blind"]
    assert blind["TRAIN_DEV"]["counts"]["exact_two_staff_match"] == 1
    assert blind["TEST"]["counts"]["exact_two_staff_match"] == 1
    assert {group for group, row in blind.items() if row["scored_pages"]} == {
        "TRAIN_DEV",
        "TEST",
        "G1",
        "G2",
    }
    _, settlement = _read_events(W4W5RunLedger(cli_paths["downloads"]))
    assert (settlement["status"], settlement["actual_pages"]) == (
        "completed",
        2,
    )


def test_s3_statistics_load_train_dev_crops_only(
    cli_paths, monkeypatch, capsys
):
    """Run the report assembly with one real crop and no readable TEST crop."""
    w3 = cli_paths["w3_run_dir"]
    (w3 / "alignment").mkdir(parents=True)
    crops = w3 / "crops" / "facsimile"
    crops.mkdir(parents=True)
    crop = Image.new("L", (300, 160), 255)
    draw = ImageDraw.Draw(crop)
    for origin in (20, 90):
        for line in range(5):
            y = origin + line * 12
            draw.line((10, y, 290, y), fill=0, width=2)
    crop.save(crops / "gold-07-s01.png")
    (w3 / "alignment/manifest.json").write_text(
        json.dumps(
            {
                "pieces": [
                    {
                        "gold_id": gid,
                        "split": split,
                        "systems": [{"system_index": 1}],
                    }
                    for gid, split in (
                        ("gold-07", "TRAIN_DEV"),
                        ("gold-01", "TEST"),
                    )
                ]
            }
        ),
        "utf-8",
    )
    downloads = cli_paths["downloads"]
    downloads.mkdir()
    (downloads / "w4-w5-setup-provenance.json").write_text(
        json.dumps(
            {
                "verovio_version": ledgers.VEROVIO_VERSION,
                "verovio_wheel_sha256": ledgers.VEROVIO_WHEEL_SHA256,
                "resvg_version": ledgers.RESVG_VERSION,
                "resvg_wheel_sha256": ledgers.RESVG_WHEEL_SHA256,
            }
        ),
        "utf-8",
    )
    monkeypatch.setattr(cli, "_TRAIN_SYSTEMS", 1)
    monkeypatch.setattr(cli, "_OVERLAY_PAGES", 1)
    monkeypatch.setattr(cli, "verify_loopback_only_unshare", lambda: None)
    monkeypatch.setattr(
        cli,
        "verify_w3_snapshot_attestation",
        lambda *_args, **_kwargs: {"geometry_sha256": "fixture"},
    )
    monkeypatch.setattr(
        cli.MuseScore,
        "open",
        lambda *_args, **_kwargs: SimpleNamespace(
            version="4.7.7", executable_sha256="fixture"
        ),
    )
    monkeypatch.setattr(
        cli,
        "verify_w4_w5_live_assets",
        lambda *_a, **_kw: {},
    )
    monkeypatch.setattr(
        cli,
        "_sources",
        lambda *_: [SimpleNamespace(digest="fixture", weight=1.0, measures=1)],
    )

    def proof(conf_p, _args, name_p, _sources, *, musescore, counter, **_kw):
        assert musescore.version == "4.7.7"
        counter.add(1)
        persist = getattr(cli, "_persist_files")  # noqa: B009
        persist(
            conf_p,
            {Path(name_p) / "samples-manifest.jsonl": b"{}\n"},
            base=Path("tmp") / "s3-publish",
        )
        with Image.open(crops / "gold-07-s01.png") as crop_im:
            sample = cli.compute_crop_statistics(crop_im)
        return {"rendered_degraded_pages": 1}, [sample]

    monkeypatch.setattr(cli, "_prove_engine", proof)
    assert (
        cli.main(
            [
                "run-s3",
                *_arguments(cli_paths),
                "--roundtrip-pieces",
                "1",
                "--timing-pages",
                "1",
            ]
        )
        == 0
    )
    capsys.readouterr()
    report_path = cli_paths["run_dir"] / "report/w4-s3-report.json"
    report = json.loads(report_path.read_text("utf-8"))
    assert report["train_dev_crop_count"] == 1
    assert set(report["crop_statistics"]) == {"musescore", "verovio"}
    for engine in report["crop_statistics"].values():
        assert set(engine) == {
            "staff_space_px",
            "staff_line_thickness_px",
            "laplacian_blur_var",
            "bg_noise_sigma",
            "p95_p05_contrast",
            "staff_slope_ptp_px",
        }
        assert all(
            metric["train_dev_median_in_synthetic_interval"]
            for metric in engine.values()
        )


def test_validate_assembly_reports_success_and_order_failure(capsys):
    """Honor ordered multi-page assembly and report content-free failures."""
    files = [str(_FIXTURES / f"study-{index}.txt") for index in (1, 2, 3)]
    command = ["validate-assembly", "--expected-systems", "3"]
    assert cli.main([*command, *files]) == 0
    report = json.loads(capsys.readouterr().out)
    assert report["status"] == "success"
    assert report["observed_sequence"] == [[1, 1], [1, 2], [2, 1]]
    assert cli.main([*command, files[1], files[0], files[2]]) == 1
    failure = json.loads(capsys.readouterr().out)
    assert failure["status"] == "failed"
    assert {
        "code": "system_sequence",
        "system_index": None,
        "line": None,
        "column": None,
    } in failure["diagnostics"]
