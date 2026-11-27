"""Scenario and core-invariant tests for W6 GPU Slice B locks and ledgers."""
# ruff: noqa: PLR2004, SLF001

from __future__ import annotations

import hashlib
import json
import os
import subprocess
import venv
from dataclasses import replace
from pathlib import Path
from typing import Any, cast

import numpy as np
import pytest
from PIL import Image

from sheet_music_omr.evaluation import copista
from sheet_music_omr.harness.stops import StopConditionError
from sheet_music_omr.model import SystemDocument
from sheet_music_omr.parser import parse_document
from sheet_music_omr.training import (
    asset_locks,
    cli,
    ledgers,
    slice_b_ledgers,
    slice_b_runner,
    slice_b_worker,
)
from sheet_music_omr.training import (
    models as training_models,
)
from sheet_music_omr.training.decoding import (
    BeamCandidate,
    SystemDecodeResult,
    SystemPromptState,
)
from sheet_music_omr.training.models import (
    TextScore2DomainTokenizer,
    apply_position_scoped_bpe_verovio_mask,
    masked_measure_token_flags,
)

_INITIAL_CARRY = (
    '{"measure":1,"offset":"0","event_counts":{},"accidental_scope":"octave",'
    '"accidentals":[],"open":[]}'
)


def _sha256(raw: bytes) -> str:
    return hashlib.sha256(raw).hexdigest()


def _seed_seven_preserved_ledgers(
    downloads: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    downloads.mkdir(parents=True, exist_ok=True)
    (downloads.parent / "runs").mkdir(parents=True, exist_ok=True)
    s0_lines = "".join(
        json.dumps({"split": "TEST", "purpose": "S0-baseline", "idx": i}) + "\n"
        for i in range(6)
    ).encode("utf-8")
    s0_sha = _sha256(s0_lines)
    monkeypatch.setattr(copista, "PRESERVED_W2_S0_TOUCH_LOG_SHA256", s0_sha)
    files = {
        "download-ledger.json": b'{"total":2350276855}',
        "w2-download-ledger.json": b'{"cumulative_bytes":480830451}',
        "w2-touch-log.jsonl": s0_lines,
        "w4-w5-download-ledger.json": b'{"total_bytes":341067483}',
        "w4-w5-run-ledger.jsonl": b'{"event":"settle"}\n',
        "w6-slice-a-download-ledger.json": (
            b'{"entries":[{"charged_bytes":10}],"schema":'
            b'"omr-w6-slice-a-download-ledger-v1","total_bytes":10}\n'
        ),
        "w6-slice-a-run-ledger.jsonl": b"",
    }
    expected = {}
    for name, payload in files.items():
        (downloads / name).write_bytes(payload)
        expected[name] = _sha256(payload)
    monkeypatch.setattr(
        ledgers, "ALL_PRESERVED_CAMPAIGN_LEDGER_SHA256S", expected
    )


def test_slice_b_cuda_wheel_and_weight_locks_invariants(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Verify CUDA wheel closure, safetensors-only weights and license rules."""
    asset_locks.verify_lock_licenses()
    gpu_wheels = asset_locks.resolve_host_w6_gpu_wheels()
    assert len(gpu_wheels) == 73
    by_name = {w.name: w for w in gpu_wheels}
    assert by_name["torch"].version == "2.15.0+cu132"
    assert by_name["torchvision"].version == "0.29.2+cu132"
    assert by_name["triton"].license_id == "MIT"
    for cpu_w in asset_locks.W6_LINUX_X86_64_WHEELS:
        if cpu_w.name not in {"torch", "torchvision"}:
            assert by_name[cpu_w.name] == cpu_w

    assert len(asset_locks.W6_MODEL_WEIGHT_LOCKS) == 8
    for repo in asset_locks.W6_MODEL_WEIGHT_LOCKS:
        assert repo.files
        for wf in repo.files:
            assert wf.filename.endswith(
                (".safetensors", ".safetensors.index.json")
            )
            assert wf.size > 0
            assert len(wf.sha256) == 64

    # Non-NVIDIA wheel cannot claim LicenseRef-NVIDIA-Proprietary
    bad_wheel = replace(
        by_name["triton"], license_id="LicenseRef-NVIDIA-Proprietary"
    )
    monkeypatch.setattr(asset_locks, "W6_GPU_LINUX_X86_64_WHEELS", (bad_wheel,))
    with pytest.raises(StopConditionError, match="unpermitted wheel license"):
        asset_locks.verify_lock_licenses()

    # Weight lock rejects pickle .bin or remote .py files
    monkeypatch.undo()
    bad_repo = replace(
        asset_locks.W6_MODEL_WEIGHT_LOCKS[0],
        files=(
            asset_locks.LockedMetaFile(
                filename="pytorch_model.bin",
                size=10,
                git_oid="0" * 40,
                sha256="a" * 64,
            ),
        ),
    )
    monkeypatch.setattr(
        asset_locks,
        "W6_MODEL_WEIGHT_LOCKS",
        (bad_repo, *asset_locks.W6_MODEL_WEIGHT_LOCKS[1:]),
    )
    with pytest.raises(StopConditionError, match="non-safetensors"):
        asset_locks.verify_lock_licenses()


def test_slice_b_download_ledger_reserves_reuses_and_charges_partial_failure(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    """Reuse cached wheels at 0 bytes and charge partial failed payloads."""
    downloads = tmp_path / "downloads"
    _seed_seven_preserved_ledgers(downloads, monkeypatch)
    ledger = slice_b_ledgers.W6SliceBDownloadLedger(downloads, allow_init=True)
    assert ledger.total_bytes() == 0

    # Cached wheel in w6-wheels is reused with zero ledger charge
    cached_whl = downloads / "w6-wheels" / "cached-1.0-py3-none-any.whl"
    cached_whl.parent.mkdir(parents=True)
    whl_bytes = b"wheel-payload"
    cached_whl.write_bytes(whl_bytes)
    got_path = ledger.ensure_cached_file(
        url="https://example.invalid/cached-1.0-py3-none-any.whl",
        dest=cached_whl,
        expected_bytes=len(whl_bytes),
        expected_sha256=_sha256(whl_bytes),
    )
    assert got_path == cached_whl
    assert ledger.total_bytes() == 0

    # Reject .py and .bin before reserving
    for bad_name in ("modeling_custom.py", "weights.bin"):
        with pytest.raises(StopConditionError, match="refusing unlocked"):
            ledger.ensure_cached_file(
                url=f"https://example.invalid/{bad_name}",
                dest=downloads / "w6-model-weights" / bad_name,
                expected_bytes=16,
                expected_sha256="b" * 64,
            )
    assert ledger.total_bytes() == 0

    # Partial failed safetensors transfer charges actual bytes read
    st_dest = downloads / "w6-model-weights" / "slug" / "model.safetensors"
    full_st = b"safetensors-full-content"

    def _partial_fail(
        _url: str, dest: Path, size: int, read_counter: list[int]
    ) -> None:
        on_disk = json.loads(ledger.path.read_bytes())
        assert on_disk["entries"][0]["status"] == "reserved"
        assert on_disk["total_bytes"] == size
        dest.write_bytes(full_st[:7])
        read_counter[0] = 7
        msg = "simulated network drop"
        raise StopConditionError(msg)

    monkeypatch.setattr(
        slice_b_ledgers, "_transfer_slice_b_payload", _partial_fail
    )
    with pytest.raises(StopConditionError, match="simulated network drop"):
        ledger.ensure_cached_file(
            url="https://example.invalid/model.safetensors",
            dest=st_dest,
            expected_bytes=len(full_st),
            expected_sha256=_sha256(full_st),
        )
    assert not st_dest.exists()
    assert ledger.total_bytes() == 7

    # Subsequent complete transfer charges full payload and verifies SHA-256
    def _ok_transfer(
        _url: str, dest: Path, _size: int, read_counter: list[int]
    ) -> None:
        dest.write_bytes(full_st)
        read_counter[0] = len(full_st)

    monkeypatch.setattr(
        slice_b_ledgers, "_transfer_slice_b_payload", _ok_transfer
    )
    assert (
        ledger.ensure_cached_file(
            url="https://example.invalid/model.safetensors",
            dest=st_dest,
            expected_bytes=len(full_st),
            expected_sha256=_sha256(full_st),
        )
        == st_dest
    )
    assert ledger.total_bytes() == 7 + len(full_st)


def _seed_completed_preregistration_prerequisites(
    downloads: Path, runs: Path, *, settle_last_replay_sub: bool = True
) -> tuple[list[str], str]:
    e0_repos = (
        "Qwen/Qwen3-VL-2B-Instruct",
        "Qwen/Qwen3.5-2B",
        "PaddlePaddle/PaddleOCR-VL-1.6",
    )
    e0_ids = [f"w6-slice-b-e0-{i}" for i in range(3)]
    for eid, erepo in zip(e0_ids, e0_repos, strict=True):
        etok = slice_b_ledgers.reserve_w6_slice_b_run(
            downloads,
            run_id=eid,
            git_commit="abc1234",
            clean_tree=True,
            command="eval-w6-traindev:e0",
            kind="eval_traindev",
            runs_parent=runs,
        )
        er_dir = runs / eid / "report"
        er_dir.mkdir(parents=True)
        eslug = asset_locks.resolve_model_weight_lock(erepo).slug
        (er_dir / "traindev-eval.json").write_text(
            json.dumps(
                {
                    "evidence_role": "zero_shot_baseline",
                    "reader_id": f"e0-{eslug}",
                }
            ),
            encoding="utf-8",
        )
        slice_b_ledgers.settle_w6_slice_b_run(
            downloads,
            etok,
            status="completed",
            actual_pages=69,
            runs_parent=runs,
        )
    sel_tok = slice_b_ledgers.reserve_w6_slice_b_run(
        downloads,
        run_id="w6-slice-b-sel-cv",
        git_commit="abc1234",
        clean_tree=True,
        command="eval-w6-traindev:synthetic-cv",
        kind="eval_traindev",
        runs_parent=runs,
    )
    sel_dir = runs / "w6-slice-b-sel-cv" / "report"
    sel_dir.mkdir(parents=True)
    (sel_dir / "traindev-eval.json").write_text(
        '{"evidence_role":"cross_validated_selection"}', encoding="utf-8"
    )
    slice_b_ledgers.settle_w6_slice_b_run(
        downloads,
        sel_tok,
        status="completed",
        actual_pages=69,
        runs_parent=runs,
    )
    subs = sorted(slice_b_ledgers._EXPECTED_REPLAY_BASE_SUBRUNS)
    last_tok = ""
    for sub in subs:
        kind: Any = "train_gpu" if sub.startswith("train_") else "eval_traindev"
        rtok = slice_b_ledgers.reserve_w6_slice_b_run(
            downloads,
            run_id=f"w6-slice-b-replay:{sub}",
            git_commit="abc1234",
            clean_tree=True,
            command=f"run-w6-replay:{sub}",
            kind=kind,
            runs_parent=runs,
        )
        if sub == subs[-1] and not settle_last_replay_sub:
            last_tok = rtok
            break
        slice_b_ledgers.settle_w6_slice_b_run(
            downloads,
            rtok,
            status="completed",
            actual_pages=69 if kind == "eval_traindev" else 0,
            runs_parent=runs,
        )
    rep_dir = runs / "w6-slice-b-replay" / "report"
    rep_dir.mkdir(parents=True, exist_ok=True)
    (rep_dir / "replay-ablation.json").write_text(
        '{"schema":"omr-w6-slice-b-replay-ablation-v1"}\n', encoding="utf-8"
    )
    return e0_ids, last_tok


def test_slice_b_run_ledger_serial_soft_caps_and_single_test_touch(  # noqa: PLR0915
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    """Enforce serial settlement, soft local disk cap and 1-shot TEST touch."""
    downloads = tmp_path / "downloads"
    _seed_seven_preserved_ledgers(downloads, monkeypatch)
    slice_b_ledgers.W6SliceBDownloadLedger(
        downloads, allow_init=True
    ).total_bytes()
    runs = downloads.parent / "runs"

    tok1 = slice_b_ledgers.reserve_w6_slice_b_run(
        downloads,
        run_id="w6-slice-b-train-1",
        git_commit="abc1234",
        clean_tree=True,
        command="train-w6-gpu",
        kind="train_gpu",
        runs_parent=runs,
    )
    # Concurrent start while tok1 is unsettled must fail closed
    with pytest.raises(StopConditionError, match="unsettled Slice B"):
        slice_b_ledgers.reserve_w6_slice_b_run(
            downloads,
            run_id="w6-slice-b-train-2",
            git_commit="abc1234",
            clean_tree=True,
            command="train-w6-gpu",
            kind="train_gpu",
            runs_parent=runs,
        )

    # Simulate single-run completion overshoot past soft disk cap: settlement
    # succeeds and records overshoot, then blocks any subsequent dispatch!
    monkeypatch.setattr(slice_b_ledgers, "W6_SLICE_B_DISK_CAP_BYTES", 50)
    rdir = runs / "w6-slice-b-train-1"
    rdir.mkdir()
    (rdir / "ckpt.safetensors").write_bytes(b"x" * 80)
    settled = slice_b_ledgers.settle_w6_slice_b_run(
        downloads, tok1, status="completed", runs_parent=runs
    )
    assert settled["disk_soft_cap_overshoot_bytes"] == 30
    with pytest.raises(StopConditionError, match="150 GB generated disk"):
        slice_b_ledgers.reserve_w6_slice_b_run(
            downloads,
            run_id="w6-slice-b-train-2",
            git_commit="abc1234",
            clean_tree=True,
            command="train-w6-gpu",
            kind="train_gpu",
            runs_parent=runs,
        )

    # Raise disk cap and verify single-pass W6-pilot-final TEST reservation
    monkeypatch.setattr(
        slice_b_ledgers, "W6_SLICE_B_DISK_CAP_BYTES", 150_000_000_000
    )
    e0_ids, pending_sub = _seed_completed_preregistration_prerequisites(
        downloads, runs, settle_last_replay_sub=False
    )
    ckpt_dir = runs / "w6-slice-b-train-1" / "best"
    ckpt_dir.mkdir(parents=True)
    (ckpt_dir / "model.safetensors").write_bytes(b"weights")
    (ckpt_dir / "arm_b_config.json").write_text(
        '{"arm_b_arch":"monkeyocr_v2_s"}'
    )
    replay_report_dir = runs / "w6-slice-b-replay" / "report"
    cal_path = replay_report_dir / "production-calibrator.json"
    cal_bytes = (
        b'{"error_instances":0,"fit_instances":5,"intercept":0.0,'
        b'"method":"platt","slope":null,"status":"insufficient_support"}\n'
    )
    cal_path.write_bytes(cal_bytes)
    prereg_path = replay_report_dir / "pilot-final-preregistration.json"
    prereg_payload = {
        "schema": slice_b_ledgers.W6_PREREGISTRATION_SCHEMA,
        "purpose": "W6-pilot-final",
        "test_touches_before_preregistration": 0,
        "e0_run_ids": e0_ids,
        "synthetic_selection_run_id": "w6-slice-b-sel-cv",
        "replay_ablation_run_id": "w6-slice-b-replay",
        "arm": "arm-b",
        "model_repo": "zenosai/MonkeyOCRv2-S",
        "selected_recognizer_checkpoint": str(ckpt_dir),
        "recognizer_checkpoint_sha256s": (
            slice_b_ledgers.compute_artifact_sha256s(ckpt_dir)
        ),
        "selected_regime": "synthetic_only",
        "selected_detector_mode": "comb_dp_blind",
        "selected_detector_checkpoint": None,
        "detector_checkpoint_sha256s": None,
        "production_calibrator_path": str(cal_path),
        "production_calibrator_sha256": _sha256(cal_bytes),
        "max_new_tokens": 1536,
        "num_beams": 3,
    }
    prereg_bytes = (
        json.dumps(prereg_payload, indent=2, sort_keys=True) + "\n"
    ).encode("utf-8")
    prereg_path.write_bytes(prereg_bytes)

    # Partial/unsettled replay fails closed BEFORE touching w2-touch-log.jsonl
    with pytest.raises(StopConditionError, match="unsettled subruns"):
        slice_b_ledgers.reserve_w6_pilot_final_test_pass(
            downloads,
            run_id="w6-slice-b-pilot-final",
            git_commit="abc1234",
            clean_tree=True,
            command="run-w6-pilot-final",
            preregistration_path=prereg_path,
            tiny_fixture=True,
            runs_parent=runs,
        )
    slice_b_ledgers.settle_w6_slice_b_run(
        downloads, pending_sub, status="completed", runs_parent=runs
    )
    slice_b_ledgers.record_w6_pilot_final_preregistration(
        downloads,
        replay_run_id="w6-slice-b-replay",
        preregistration_sha256=_sha256(prereg_bytes),
    )

    # Replacing BOTH checkpoint AND updating manifest digest is caught by
    # write-once ledger-anchored preregistration_sha256!
    (ckpt_dir / "model.safetensors").write_bytes(b"replaced-weights")
    forged_payload = dict(prereg_payload) | {
        "recognizer_checkpoint_sha256s": (
            slice_b_ledgers.compute_artifact_sha256s(ckpt_dir)
        )
    }
    prereg_path.write_text(
        json.dumps(forged_payload, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
    )
    with pytest.raises(StopConditionError, match="does not match ledger"):
        slice_b_ledgers.reserve_w6_pilot_final_test_pass(
            downloads,
            run_id="w6-slice-b-pilot-final",
            git_commit="abc1234",
            clean_tree=True,
            command="run-w6-pilot-final",
            preregistration_path=prereg_path,
            tiny_fixture=True,
            runs_parent=runs,
        )
    (ckpt_dir / "model.safetensors").write_bytes(b"weights")
    prereg_path.write_bytes(prereg_bytes)

    # Incomplete E0 model coverage (duplicate E0 model) fails closed!
    e2_rep = runs / e0_ids[2] / "report" / "traindev-eval.json"
    orig_e2 = e2_rep.read_bytes()
    e2_rep.write_bytes(
        (runs / e0_ids[0] / "report" / "traindev-eval.json").read_bytes()
    )
    with pytest.raises(StopConditionError, match="missing completed E0"):
        slice_b_ledgers.reserve_w6_pilot_final_test_pass(
            downloads,
            run_id="w6-slice-b-pilot-final",
            git_commit="abc1234",
            clean_tree=True,
            command="run-w6-pilot-final",
            preregistration_path=prereg_path,
            tiny_fixture=True,
            runs_parent=runs,
        )
    e2_rep.write_bytes(orig_e2)

    # Non-fixture TEST pass rejects dirty tree BEFORE GPU check or touch write
    with pytest.raises(StopConditionError, match="clean git tree"):
        slice_b_ledgers.reserve_w6_pilot_final_test_pass(
            downloads,
            run_id="w6-slice-b-pilot-final",
            git_commit="abc1234",
            clean_tree=False,
            command="run-w6-pilot-final",
            preregistration_path=prereg_path,
            tiny_fixture=False,
            runs_parent=runs,
        )
    assert (
        len((downloads / "w2-touch-log.jsonl").read_bytes().splitlines()) == 6
    )

    test_tok, _, _, _ = slice_b_ledgers.reserve_w6_pilot_final_test_pass(
        downloads,
        run_id="w6-slice-b-pilot-final",
        git_commit="abc1234",
        clean_tree=True,
        command="run-w6-pilot-final",
        preregistration_path=prereg_path,
        tiny_fixture=True,
        runs_parent=runs,
    )
    assert (
        copista.verify_w2_touch_log_bytes(
            (downloads / "w2-touch-log.jsonl").read_bytes(),
            slice_b_run_ledger_path=(
                downloads / slice_b_ledgers.W6_SLICE_B_RUN_LEDGER_NAME
            ),
        )
        == 1
    )
    slice_b_ledgers.settle_w6_slice_b_run(
        downloads,
        test_tok,
        status="completed",
        actual_pages=23,
        runs_parent=runs,
    )
    # Second W6-pilot-final attempt fails closed permanently
    with pytest.raises(StopConditionError, match="already"):
        slice_b_ledgers.reserve_w6_pilot_final_test_pass(
            downloads,
            run_id="w6-slice-b-pilot-final-2",
            git_commit="abc1234",
            clean_tree=True,
            command="run-w6-pilot-final",
            preregistration_path=prereg_path,
            tiny_fixture=True,
            runs_parent=runs,
        )


def test_position_scoped_bpe_verovio_mask_and_open_reading_measure_mask() -> (
    None
):
    """Mask disabled Verovio continuations without masking header or quotes."""
    vocab = {
        "<pad>": 0,
        "format: text-score/2\n": 1,
        "clefs: G2 F4\n": 2,
        "\nm 1\n1.1: ": 3,
        "<": 4,
        "clef": 5,
        "cresc": 6,
        "p": 7,
        "> ": 8,
        '<tempo "Allegro ': 9,
        "dim": 10,
        '"> ': 11,
        "xs": 12,
        "C4/1\n": 13,
        "[x": 14,
        "s": 15,
        "2": 16,
        "[": 17,
        "[g": 18,
        "r": 19,
        "staccato]": 20,
        "\nm 1\n1.1: [grace]": 21,
        '"> [grace]': 22,
        '<text "say \\"': 23,
        'hi"> ': 24,
        "[grace]": 25,
        'hi"> [grace]': 26,
    }

    class _WrapTok:
        def __len__(self) -> int:
            return len(vocab)

        def decode(
            self, ids: list[int], *, clean_up_tokenization_spaces: bool = False
        ) -> str:
            del clean_up_tokenization_spaces
            inv = {v: k for k, v in vocab.items()}
            return "".join(inv[i] for i in ids)

    # Valid Verovio ground truth uses "[staccato]" (17, 20) and escaped-quote
    # text (23, 10, 24); also probe "[x" -> "s" and "[g" -> "r"
    asst_ids = [
        1,
        2,
        3,
        4,
        7,
        8,
        9,
        10,
        11,
        23,
        10,
        24,
        17,
        20,
        14,
        13,
        18,
        13,
    ]
    logits = np.zeros((1, len(asst_ids), len(vocab)), dtype=np.float32)
    labels = np.asarray([asst_ids], dtype=np.int64)
    mask = {
        "clef": False,
        "cross_staff": False,
        "grace": False,
        "hairpin": False,
        "ottava": False,
        "pitch": True,
    }
    masked = apply_position_scoped_bpe_verovio_mask(
        logits, asst_ids, labels, mask, _WrapTok()
    )
    # Step 1 predicts "clefs: G2 F4\n" in header -> finite, whereas syntax-
    # crossing "\nm 1\n1.1: [grace]" (21) entering body is masked to -inf!
    assert np.isfinite(masked[0, 1, 2])
    assert masked[0, 1, 21] == -np.inf
    # Step 4 follows "<" in body -> "clef" (5) & "cresc" (6) -inf, "p" finite
    assert masked[0, 4, 5] == -np.inf
    assert masked[0, 4, 6] == -np.inf
    assert np.isfinite(masked[0, 4, 7])
    # Step 7 is inside quoted "<tempo \"Allegro " -> "dim" (10) stays finite,
    # while '"> [grace]' (22) closing quote & completing grace is -inf!
    assert np.isfinite(masked[0, 7, 10])
    assert masked[0, 7, 22] == -np.inf
    # Step 10 follows '<text "say \"' (escaped quote, STILL inside quotes!):
    # "[grace]" (25) stays finite inside quotes, while 'hi"> [grace]' (26)
    # closes the real quote and completes grace -> -inf!
    assert np.isfinite(masked[0, 10, 25])
    assert masked[0, 10, 26] == -np.inf
    # Step 12 follows 'hi"> ' (quote now closed!) -> "[" (17) & "[staccato]" ok
    assert np.isfinite(masked[0, 12, 17])
    assert np.isfinite(masked[0, 13, 20])
    # Step 15 follows "[x" (14) -> "s" (15) completes "xs" -> -inf!
    assert masked[0, 15, 15] == -np.inf
    # Step 17 follows "[g" (18) -> "r" (19) completes "gr" -> -inf!
    assert masked[0, 17, 19] == -np.inf

    pieces = [
        "header\n",
        "m 1\n",
        "1.1: C4/1\n",
        "m 2\n",
        "1.1: D4/1\n",
        "m 3\n",
        "1.1: E4/1\n",
    ]
    flags = masked_measure_token_flags(pieces, [2])
    assert flags == [False, False, False, True, True, False, False]


def test_system_results_to_crop_assembly_prefix_and_abstention_fallback() -> (
    None
):
    """Assemble split systems and retain valid closed prefix on abstention."""
    s1_txt = (
        f"format: text-score/2\nkey: 0\ntime: 2/4\nclefs: G2 F4\n"
        f"carry: octave\npage: 1\nsystem: 1\ncarry-in: {_INITIAL_CARRY}\n"
        "m 1\n1.1: C4/2\n2.1: C3/2\n"
    )
    s2_txt = (
        "format: text-score/2\nkey: 0\ntime: 2/4\nclefs: G2 F4\n"
        "carry: octave\npage: 1\nsystem: 2\n"
        'carry-in: {"measure":2,"offset":"0","event_counts":{},'
        '"accidental_scope":"octave","accidentals":[],"open":[]}\n'
        "m 2\n1.1: D4/2\n2.1: D3/2\n"
    )
    d1 = parse_document(s1_txt)
    d2 = parse_document(s2_txt)
    assert isinstance(d1, SystemDocument)
    assert isinstance(d2, SystemDocument)
    r1 = SystemDecodeResult(
        valid=True, document=d1, text=s1_txt, raw_logprob=-2.0
    )
    r2 = SystemDecodeResult(
        valid=True, document=d2, text=s2_txt, raw_logprob=-3.0
    )
    full_asm, scores = slice_b_runner._system_results_to_crop_assembly(
        [(1, 1), (1, 2)], [r1, r2]
    )
    assert full_asm.parse_valid
    assert full_asm.failed_crops == ()
    assert full_asm.pred_measure_crop_indices == (0, 1)
    assert len(scores) == 2
    assert np.all(np.isfinite(scores))

    # Abstention on system 2 retains valid closed prefix from system 1
    r2_abs = SystemDecodeResult(
        valid=False, abstention_reason="upstream_carry_gap"
    )
    part_asm, _ = slice_b_runner._system_results_to_crop_assembly(
        [(1, 1), (1, 2)], [r1, r2_abs]
    )
    assert part_asm.parse_valid
    assert part_asm.failed_crops == (1,)
    assert part_asm.pred_measure_crop_indices == (0,)


def test_slice_b_cli_subcommands_and_packaged_verification(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    """Parse Slice B CLI commands and validate packaged dataset digests."""
    parser = cli.build_parser()
    parsed = parser.parse_args(
        [
            "train-w6-gpu",
            "--downloads",
            str(tmp_path / "dl"),
            "--run-dir",
            str(tmp_path / "runs" / "w6-slice-b-t1"),
            "--arm",
            "arm-b",
            "--model-repo",
            "zenosai/MonkeyOCRv2-S",
            "--dataset-dir",
            str(tmp_path / "ds"),
            "--max-steps",
            "100",
        ]
    )
    assert parsed.command == "train-w6-gpu"
    assert parsed.arm == "arm-b"
    assert parsed.max_steps == 100
    # --no-formal is not accepted on Slice B CLI commands
    with pytest.raises(SystemExit):
        parser.parse_args(
            [
                "run-w6-pilot-final",
                "--downloads",
                str(tmp_path / "dl"),
                "--run-dir",
                str(tmp_path / "runs" / "w6-slice-b-pf"),
                "--preregistration",
                str(tmp_path / "prereg.json"),
                "--w2-s0-run-dir",
                str(tmp_path / "s0"),
                "--immich-dir",
                str(tmp_path / "imm"),
                "--desk-study-dir",
                str(tmp_path / "ds"),
                "--req012-manifest",
                str(tmp_path / "r12.json"),
                "--no-formal",
            ]
        )
    # E0 mode rejects fine-tuned checkpoint overrides before reserving
    with pytest.raises(StopConditionError, match="E0 zero-shot baseline"):
        slice_b_runner.run_w6_traindev_eval(
            tmp_path / "dl",
            tmp_path / "runs" / "w6-slice-b-e0-bad",
            traindev_root=tmp_path / "td",
            mode="e0",
            model_repo="Qwen/Qwen3-VL-2B-Instruct",
            recognizer_checkpoint=tmp_path / "ckpt",
            formal=False,
            tiny_fixture=True,
        )

    pkg = tmp_path / "w6-slice-a-package-formal-6"
    (pkg / "report").mkdir(parents=True)
    (pkg / "datasets" / "w6-crops-20k" / "synth_train").mkdir(parents=True)
    (pkg / "datasets" / "w6-crops-50k").mkdir(parents=True)
    crop_png = pkg / "datasets" / "crops" / "c0.png"
    crop_png.parent.mkdir(parents=True)
    Image.new("RGB", (32, 32), (255, 255, 255)).save(crop_png, format="PNG")
    crop_sha = _sha256(crop_png.read_bytes())
    manifest_row = (
        json.dumps({"crop_relpath": "crops/c0.png", "sha256": crop_sha}).encode(
            "utf-8"
        )
        + b"\n"
    )
    files = {
        "report/w6-packaging.json": b'{"ok":true}\n',
        "datasets/w6-crops-20k/summary.json": b'{"crops":20005}\n',
        "datasets/w6-crops-20k/synth_train/crops.jsonl": manifest_row,
        "datasets/w6-crops-50k/summary.json": b'{"crops":50005}\n',
    }
    exp = {}
    for rel, raw in files.items():
        (pkg / rel).write_bytes(raw)
        exp[rel] = _sha256(raw)
    assert (
        slice_b_ledgers.verify_packaged_slice_a_datasets(
            pkg, expected_digests=exp, verify_png_assets=True
        )
        == exp
    )
    # Corrupted PNG leaf is caught when verify_png_assets=True
    crop_png.write_bytes(b"corrupted-png")
    with pytest.raises(
        StopConditionError, match="packaged PNG asset SHA-256 mismatch"
    ):
        slice_b_ledgers.verify_packaged_slice_a_datasets(
            pkg, expected_digests=exp, verify_png_assets=True
        )
    # Dataset path outside <pkg>/datasets/w6-crops-{20k,50k} is rejected
    with pytest.raises(StopConditionError, match="Slice B dataset directory"):
        slice_b_runner._verify_dataset_belongs_to_packaged_slice_a(
            tmp_path / "unchecked_ds"
        )
    # Non-WSL2 Linux host is rejected at setup_w6_slice_b entry before imports
    monkeypatch.setattr(
        slice_b_ledgers.platform, "release", lambda: "6.8.0-generic"
    )
    with pytest.raises(StopConditionError, match="WSL2 workstation"):
        slice_b_ledgers.setup_w6_slice_b(
            tmp_path / "dl_nowsl",
            package_run_dir=pkg,
            traindev_root=tmp_path / "missing_td",
            formal=True,
        )


def test_slice_b_bare_venv_isolated_worker_and_zero_crop_page_heartbeat(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    """Launch python -I worker from a bare venv without sheet_music_omr."""
    bare_venv = tmp_path / "bare-venv"
    venv.EnvBuilder(
        system_site_packages=False, symlinks=True, with_pip=False
    ).create(bare_venv)
    bare_py = bare_venv / "bin" / "python"
    site_pkgs = next((bare_venv / "lib").glob("python*/site-packages"))
    host_site_pkgs = Path(str(np.__file__)).resolve().parents[1]
    for entry in host_site_pkgs.iterdir():
        if "sheet_music_omr" in entry.name or "sheet-music-omr" in entry.name:
            continue
        dst = site_pkgs / entry.name
        if not dst.exists():
            dst.symlink_to(entry)
    # Confirm python -I in bare_venv CANNOT import sheet_music_omr on its own
    probe = subprocess.run(  # noqa: S603
        [str(bare_py), "-I", "-c", "import sheet_music_omr"],
        env={"PYTHONPATH": slice_b_runner._omr_src_root()},
        capture_output=True,
        check=False,
    )
    assert probe.returncode != 0
    # Confirm _worker_python_cmd imports slice_b_worker cleanly under python -I
    cmd = slice_b_runner._worker_python_cmd(
        bare_py, tmp_path / "missing.json", ("extra1", "extra2")
    )
    launched = subprocess.run(  # noqa: S603
        cmd,
        capture_output=True,
        check=False,
    )
    assert launched.returncode == 2
    assert "usage: slice_b_worker" in launched.stderr.decode("utf-8")

    # Verify in-process zero-crop page heartbeat + split-measure token pieces:
    # 2-page piece where page 1 has 0 crops and page 2 has 1 crop!
    r_fd, w_fd = os.pipe()
    crop_p2 = tmp_path / "p2_s1.png"
    Image.new("RGB", (56, 28), (255, 255, 255)).save(crop_p2, format="PNG")
    out_json = tmp_path / "worker_out.json"
    tok = TextScore2DomainTokenizer()

    class _FakeCuda:
        @staticmethod
        def is_available() -> bool:
            return False

    class _FakeTorch:
        cuda = _FakeCuda

    monkeypatch.setattr(
        slice_b_worker,
        "import_module",
        lambda name: _FakeTorch if name == "torch" else __import__(name),
    )
    monkeypatch.setattr(
        slice_b_worker,
        "_load_arm_a_for_worker",
        lambda *_a, **_kw: (object(), object(), "qwen3_vl"),
    )
    valid_sys = (
        f"format: text-score/2\nkey: 0\ntime: 2/4\nclefs: G2 F4\n"
        f"carry: octave\npage: 2\nsystem: 1\ncarry-in: {_INITIAL_CARRY}\n"
        "m 1\n1.1: C4/2\n2.1: C3/2\n"
    )
    # Emit multi-token tuple (not collapsed 1-tuple!) to exercise token budget
    token_ids = tok.encode(valid_sys)
    assert len(token_ids) > 10
    monkeypatch.setattr(
        slice_b_worker,
        "_generate_arm_a_beams",
        lambda *_a, **_kw: [
            BeamCandidate(
                raw_logprob=-1.5,
                tokens=tuple(tok.pieces(token_ids)),
                length_capped=False,
            )
        ],
    )
    job_cfg = {
        "task": "infer_recognizer",
        "arm": "arm-a",
        "zero_shot_e0": False,
        "max_new_tokens": 256,
        "num_beams": 1,
        "output_json": str(out_json),
        "pieces": [
            {
                "gold_id": "gold-07",
                "expected_pages": [1, 2],
                "crops": [
                    {
                        "page_index": 2,
                        "page_system_index": 1,
                        "crop_path": str(crop_p2),
                    }
                ],
            }
        ],
    }
    try:
        res = slice_b_worker._run_infer_recognizer(job_cfg, heartbeat_fd=w_fd)
    finally:
        os.close(w_fd)
    hb_bytes = os.read(r_fd, 1024)
    os.close(r_fd)
    assert hb_bytes.decode("ascii").splitlines() == ["0", "1"]
    pieces_dict = cast("dict[str, Any]", res["pieces"])
    sys_out = pieces_dict["gold-07"]["systems"]
    assert len(sys_out) == 1
    assert sys_out[0]["valid"] is True


def test_slice_b_monkeyocrv2_arm_b_beam_generation() -> None:
    """Exercise real MonkeyOCRv2EncoderDecoder beam generation with torch."""
    st_mod = pytest.importorskip("safetensors.torch")
    torch_mod = pytest.importorskip("torch")
    del st_mod
    tok = TextScore2DomainTokenizer()
    torch_mod.manual_seed(0)
    vision = training_models.MonkeyOCRv2VisionTrunk(
        embed_dim=32,
        num_hidden_layers=1,
        num_attention_heads=4,
        intermediate_size=64,
    )
    dec_cfg = training_models.ArmBDecoderConfig(
        vocab_size=tok.vocab_size,
        layers=1,
        d_model=32,
        nhead=4,
        d_ff=64,
    )
    m_v2 = training_models.MonkeyOCRv2EncoderDecoder(
        vision=vision, decoder=dec_cfg
    )
    # Bias output head gently toward two ASCII byte tokens then EOS so beam
    # search completes a multi-token sequence!
    t_a = tok.encode("m")[0]
    t_b = tok.encode(" ")[0]
    step_calls = 0
    orig_forward = m_v2.decoder.forward

    def _scripted_forward(seq_t: Any, mem_t: Any) -> Any:
        nonlocal step_calls
        out = orig_forward(seq_t, mem_t).clone()
        chosen = (
            t_a
            if step_calls == 0
            else (t_b if step_calls == 1 else tok.eos_token_id)
        )
        out[:, -1, chosen] += 50.0
        step_calls += 1
        return out

    m_v2.decoder.forward = _scripted_forward  # type: ignore[method-assign]
    cands = slice_b_worker._generate_arm_b_beams(
        m_v2,
        tok,
        Image.new("RGB", (56, 28), (255, 255, 255)),
        SystemPromptState(
            page_system_tuple=(1, 1),
            incoming_header_state_text="",
            carry_in_text=_INITIAL_CARRY,
        ),
        pad_multiple=28,
        carry_measure_tokens=0,
        max_new_tokens=4,
        beam_width=1,
    )
    assert cands
    assert cands[0].tokens == (t_a, t_b)
