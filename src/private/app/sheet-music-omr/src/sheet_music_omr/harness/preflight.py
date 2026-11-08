"""Execution step 1: record versions, check MuseScore and run calibration.

The preflight records the run record's source and tool versions, checks or
starts the download ledger, runs one MuseScore conversion of a calibration
fixture with the settings hash checked, and runs the calibration tests. A
failure of any of these is a stop.
"""

from __future__ import annotations

import os
import platform
import shutil
import subprocess
import sys
from importlib import metadata
from pathlib import Path
from typing import TYPE_CHECKING

from sheet_music_omr.harness.downloads import Ledger, LedgerError
from sheet_music_omr.harness.musescore import sanitize_log
from sheet_music_omr.harness.records import (
    PROJECT_DIR,
    now,
    run_git,
    source_state,
    update_run_record,
)
from sheet_music_omr.harness.stops import StopConditionError

if TYPE_CHECKING:
    from collections.abc import Callable, Sequence

    from sheet_music_omr.harness.confine import Confinement
    from sheet_music_omr.harness.musescore import MuseScore
    from sheet_music_omr.harness.records import Git

LEDGER_NAME = "download-ledger.json"
"""The name of the download ledger in the run directory."""

CALIBRATION_TESTS = (
    Path("tests") / "test_comparison.py",
    Path("tests") / "test_harness_screen.py",
)
"""The tests of the calibration list and the screen's allowlist review."""

PREFLIGHT_FIXTURE = (
    PROJECT_DIR / "tests" / "fixtures" / "comparison" / ("base.musicxml")
)
"""The calibration fixture that the preflight converts with MuseScore."""

PACKAGES = ("musicdiff", "music21", "converter21", "numpy")
"""The resolved packages whose versions the run records."""

type Calibrate = Callable[[Sequence[Path], Path], tuple[int, str]]
"""Run the calibration tests with a temporary directory; return the status
and output."""


class PreflightError(StopConditionError):
    """The preflight failed."""


def run_calibration(tests: Sequence[Path], temp: Path) -> tuple[int, str]:
    """Run the calibration tests with pytest in a child process.

    The child's temporary files go under ``temp``; it writes no cache or
    bytecode into the repository.
    """
    environment = {
        **os.environ,
        "TEMP": str(temp),
        "TMP": str(temp),
        "TMPDIR": str(temp),
        "PYTHONDONTWRITEBYTECODE": "1",
    }
    completed = subprocess.run(  # noqa: S603 - this interpreter and pytest
        [
            sys.executable,
            "-m",
            "pytest",
            "-p",
            "no:cacheprovider",
            "-q",
            f"--basetemp={temp / 'pytest'}",
            *(str(PROJECT_DIR / test) for test in tests),
        ],
        cwd=PROJECT_DIR,
        env=environment,
        capture_output=True,
        text=True,
        check=False,
    )
    return completed.returncode, completed.stdout + completed.stderr


def tool_versions() -> dict[str, str]:
    """Return the resolved versions of the instrument's packages.

    Raises:
        PreflightError: If one is not installed.
    """
    versions = {"python": platform.python_version()}
    for name in PACKAGES:
        try:
            versions[name] = metadata.version(name)
        except metadata.PackageNotFoundError as error:
            message = f"the package {name} is not installed"
            raise PreflightError(message) from error
    return versions


def open_ledger(
    confinement: Confinement,
    *,
    prior_total: int | None,
    basis: str | None,
) -> Ledger:
    """Load the run's download ledger, or start it with the prior total.

    Raises:
        PreflightError: If it is unreadable, or absent with no prior total
            and basis given.
    """
    path = confinement.run / LEDGER_NAME
    try:
        if path.exists():
            return Ledger.load(path)
        if prior_total is None or not basis:
            message = (
                "the download ledger is absent; give the W1 prior total and "
                "its basis"
            )
            raise PreflightError(message)
        return Ledger.create(
            path,
            prior_total=prior_total,
            basis=basis,
            confinement=confinement,
        )
    except LedgerError as error:
        message = f"the download ledger is unusable: {error}"
        raise PreflightError(message) from error


def preflight(  # noqa: PLR0913
    confinement: Confinement,
    musescore: MuseScore,
    *,
    prior_total: int | None = None,
    basis: str | None = None,
    calibrate: Calibrate | None = run_calibration,
    git: Git = run_git,
    fixture: Path = PREFLIGHT_FIXTURE,
    require_ledger: bool = True,
) -> dict[str, object]:
    """Run execution step 1 and record it in the run record.

    Args:
        confinement: The run's write confinement.
        musescore: The pinned MuseScore, opened with its checks.
        prior_total: The W1 cumulative download total, for a new ledger.
        basis: How the prior total was established, for a new ledger.
        calibrate: Runs the calibration tests; ``None`` skips them, which
            only a pilot may do, and the record says so.
        git: Runs a read-only git command.
        fixture: The calibration fixture for the MuseScore conversion.
        require_ledger: Whether the download ledger must exist or be
            started; a pilot downloads nothing and may skip it.

    Returns:
        The preflight record.

    Raises:
        PreflightError: If the conversion or calibration fails.
    """
    ledger: dict[str, object] = {"status": "skipped"}
    if require_ledger or (confinement.run / LEDGER_NAME).exists():
        opened = open_ledger(confinement, prior_total=prior_total, basis=basis)
        ledger = {"total": opened.total, "entries": len(opened.entries)}
    with musescore.batch("preflight"):
        directory = confinement.temp_dir("preflight-")
        try:
            conversion = musescore.convert(
                fixture,
                directory / "preflight.musicxml",
            )
        finally:
            shutil.rmtree(confinement.check(directory))
    if not conversion.ok:
        message = "the preflight MuseScore conversion failed"
        raise PreflightError(message)
    settings_hash = musescore.check_settings()
    calibration: dict[str, object] = {"status": "skipped"}
    if calibrate is not None:
        directory = confinement.temp_dir("calibration-")
        try:
            status, output = calibrate(CALIBRATION_TESTS, directory)
        finally:
            shutil.rmtree(confinement.check(directory), ignore_errors=True)
        calibration = {
            "status": "passed" if status == 0 else "failed",
            "tests": [test.as_posix() for test in CALIBRATION_TESTS],
            "log": sanitize_log(output, musescore.private),
        }
        if status != 0:
            update_run_record(
                confinement, "preflight", {"calibration": calibration}
            )
            message = "calibration failed"
            raise PreflightError(message)
    record: dict[str, object] = {
        "time": now(),
        "source": source_state(git),
        "versions": tool_versions(),
        "musescore": {
            "path": str(musescore.executable),
            "version": musescore.version,
            "preferences": musescore.preferences(),
            "settings_sha256": settings_hash,
            "conversion": conversion.record(),
        },
        "ledger": ledger,
        "calibration": calibration,
    }
    update_run_record(confinement, "preflight", record)
    return record
