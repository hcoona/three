"""Execution step 1: record versions, check MuseScore and run calibration.

The preflight records the run record's source and tool versions, checks or
starts the download ledger, runs one MuseScore conversion of a calibration
fixture while checking the eight MusicXML preferences (recording the settings
SHA-256 when present), and runs the calibration tests. A failure of any of
these is a stop.
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

from sheet_music_omr.harness.downloads import (
    DOWNLOAD_LOCK_NAME,
    Ledger,
    LedgerError,
)
from sheet_music_omr.harness.musescore import (
    has_source_verified_defaults,
    sanitize_log,
)
from sheet_music_omr.harness.privacy import GOLD_MAPPING
from sheet_music_omr.harness.records import (
    PROJECT_DIR,
    load_run_record,
    now,
    run_git,
    source_state,
    update_run_record,
)
from sheet_music_omr.harness.stops import StopConditionError

if TYPE_CHECKING:
    from collections.abc import Callable, Mapping, Sequence

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
    """Load the shared download ledger (mirrored to the run), or start it.

    A shared ``download-ledger.json`` in ``confinement.downloads`` carries
    cumulative W1 download totals across runs automatically and is mirrored
    into ``confinement.run``. If an older run directory already has a local
    ledger before the shared one exists, it seeds the shared ledger.

    Raises:
        PreflightError: If the ledger is unreadable, conflicts with an explicit
            ``prior_total``, or is absent with no prior total and basis given.
    """
    shared = confinement.downloads / LEDGER_NAME
    local = confinement.run / LEDGER_NAME
    try:
        with confinement.lock(confinement.downloads / DOWNLOAD_LOCK_NAME):
            if shared.exists():
                ledger = Ledger.load(shared, mirror=local)
                if prior_total is not None and prior_total not in {
                    ledger.prior_total,
                    ledger.total,
                }:
                    message = (
                        "the shared download ledger already records total "
                        f"{ledger.total} (prior_total {ledger.prior_total}); "
                        "omit --prior-total or pass a matching value"
                    )
                    raise PreflightError(message)
                ledger.sync_mirror(confinement)
                return ledger
            if local.exists():
                existing = Ledger.load(local)
                ledger = Ledger.create(
                    shared,
                    prior_total=existing.prior_total,
                    basis=existing.basis,
                    entries=existing.entries,
                    confinement=confinement,
                )
                ledger.mirror = local.resolve()
                return ledger
            if prior_total is None or not basis:
                message = (
                    "the download ledger is absent; give the W1 prior total "
                    "and its basis"
                )
                raise PreflightError(message)
            return Ledger.create(
                shared,
                prior_total=prior_total,
                basis=basis,
                confinement=confinement,
                mirror=local,
            )
    except LedgerError as error:
        message = f"the download ledger is unusable: {error}"
        raise PreflightError(message) from error


def _check_existing_preflight(
    confinement: Confinement,
    musescore: MuseScore,
    current_source: Mapping[str, object],
    current_versions: Mapping[str, str],
) -> dict[str, object]:
    record = load_run_record(confinement)
    prior = record.get("preflight")
    has_artifacts = "run" in record or any(
        (confinement.run / sub).exists()
        for sub in ("selection", "inputs", "files", "report", GOLD_MAPPING)
    )
    prior_dict = prior if isinstance(prior, dict) else {}
    prior_ms = prior_dict.get("musescore")
    prior_src = prior_dict.get("source")
    prior_ver = prior_dict.get("versions")
    if has_artifacts and not (
        isinstance(prior_ms, dict)
        and isinstance(prior_src, dict)
        and isinstance(prior_ver, dict)
    ):
        message = (
            "existing selection or execution artifacts lack preflight "
            "provenance; start a fresh --run-dir"
        )
        raise PreflightError(message)
    if isinstance(prior_ms, dict) and (
        prior_ms.get("version") != musescore.version
        or prior_ms.get("executable_sha256") != musescore.executable_sha256
    ):
        message = (
            "MuseScore changed since preflight recorded this run; "
            "start a fresh --run-dir"
        )
        raise PreflightError(message)
    if isinstance(prior_src, dict) and (
        prior_src.get("tree") != current_source.get("tree")
        or bool(prior_src.get("dirty"))
        or bool(current_source.get("dirty"))
    ):
        message = (
            "the converter/harness code tree changed since preflight "
            "recorded this run; start a fresh --run-dir"
        )
        raise PreflightError(message)
    if isinstance(prior_ver, dict) and prior_ver != dict(current_versions):
        message = (
            "resolved package versions changed since preflight recorded "
            "this run; start a fresh --run-dir"
        )
        raise PreflightError(message)
    return dict(prior_dict)


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
    current_source = source_state(git)
    current_versions = tool_versions()
    prior_preflight = _check_existing_preflight(
        confinement, musescore, current_source, current_versions
    )
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
                confinement,
                "preflight",
                {**prior_preflight, "calibration": calibration},
            )
            message = "calibration failed"
            raise PreflightError(message)
    record: dict[str, object] = {
        "time": now(),
        "source": current_source,
        "versions": current_versions,
        "musescore": {
            "path": str(musescore.executable),
            "version": musescore.version,
            "executable_sha256": musescore.executable_sha256,
            "source_verified_defaults": has_source_verified_defaults(
                musescore.version
            ),
            "preferences": musescore.preferences(),
            "settings_sha256": settings_hash,
            "conversion": conversion.record(),
        },
        "ledger": ledger,
        "calibration": calibration,
    }
    update_run_record(confinement, "preflight", record)
    return record
