"""Scenario tests for the harness's worker pool and inline executor.

Spawned workers import each step by name, so the steps are library
functions: ``os.getenv`` reports the worker's environment, ``json.loads``
fails, ``load_gold`` on an absent file stops and a partial ``time.sleep``
outlives the time bound.
"""

from __future__ import annotations

import functools
import json
import os
import time
from typing import TYPE_CHECKING

import pytest

from sheet_music_omr.harness.gold import load_gold
from sheet_music_omr.harness.worker import (
    InlineExecutor,
    StepStopError,
    WorkerPool,
)

if TYPE_CHECKING:
    from pathlib import Path


def test_pool_bounds_classifies_and_replaces_workers(tmp_path: Path) -> None:
    """A slow step times out, its worker is replaced and the pool goes on."""
    absent = {"status": tmp_path / "absent.json", "cache": tmp_path}
    pool = WorkerPool(tmp_path, jobs=1)
    try:
        ok = pool.call(os.getenv, {"key": "TEMP"}, context="getenv")
        assert (ok.ok, ok.value) == (True, str(tmp_path))

        error = pool.call(json.loads, {"s": "{"}, context="loads")
        assert error.status == "error"
        assert error.error_type == "json.decoder.JSONDecodeError"

        with pytest.raises(StepStopError, match=r"gold: .*GoldError"):
            pool.call(load_gold, absent, context="gold")

        sleep = functools.partial(time.sleep, 30)
        slow = pool.call(sleep, {}, context="sleep", timeout=1)
        assert slow.status == "timeout"
        assert slow.seconds < 10  # noqa: PLR2004 - far below the sleep

        again = pool.call(os.getenv, {"key": "TMP"}, context="getenv")
        assert again.value == str(tmp_path)
    finally:
        pool.close()
    with pytest.raises(RuntimeError, match="closed"):
        pool.call(os.getenv, {"key": "TEMP"}, context="getenv")


def test_inline_executor_classifies_as_the_pool(tmp_path: Path) -> None:
    """The inline executor reports errors and raises stops alike."""
    executor = InlineExecutor()
    assert executor.call(json.loads, {"s": "{"}, context="c").status == "error"
    with pytest.raises(StepStopError):
        executor.call(
            load_gold,
            {"status": tmp_path / "absent.json", "cache": tmp_path},
            context="c",
        )
