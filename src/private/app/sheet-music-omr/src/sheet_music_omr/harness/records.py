"""Read and write the run directory's JSON records.

The run record ``run.json`` holds the preflight record, the frozen run
plan and every stop record. Each write replaces a file atomically through
the run's confinement.
"""

from __future__ import annotations

import json
import os
import subprocess
from datetime import UTC, datetime
from pathlib import Path
from typing import TYPE_CHECKING

from sheet_music_omr.harness.stops import StopConditionError

if TYPE_CHECKING:
    from collections.abc import Callable, Sequence

    from sheet_music_omr.harness.confine import Confinement

RUN_RECORD = "run.json"
"""The name of the run record in the run directory."""

PROJECT_DIR = Path(__file__).resolve().parents[3]
"""The ``sheet-music-omr`` project directory."""

type Git = Callable[[Sequence[str]], str]


class RecordError(StopConditionError):
    """A run-directory record is unreadable or inconsistent."""


def now() -> str:
    """Return the current UTC time in ISO 8601, to the second."""
    return datetime.now(UTC).isoformat(timespec="seconds")


def dumps(data: object) -> str:
    """Return the stable JSON spelling of a record, ending with LF."""
    return json.dumps(data, indent=2, sort_keys=True, ensure_ascii=False) + "\n"


def write_json(confinement: Confinement, path: Path, data: object) -> None:
    """Write a JSON record atomically inside the confinement."""
    confinement.write_text(path, dumps(data))


def read_json(path: Path) -> object:
    """Read a JSON record.

    Raises:
        RecordError: If it is unreadable or not JSON.
    """
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeDecodeError, json.JSONDecodeError) as error:
        message = f"cannot read the record {path.name}: {type(error).__name__}"
        raise RecordError(message) from error


def read_object(path: Path) -> dict[str, object]:
    """Read a JSON record that must be an object.

    Raises:
        RecordError: If it is unreadable or not an object.
    """
    data = read_json(path)
    if not isinstance(data, dict):
        message = f"the record {path.name} is not an object"
        raise RecordError(message)
    return data


def load_run_record(confinement: Confinement) -> dict[str, object]:
    """Return the run record, or an empty one when none exists yet."""
    path = confinement.run / RUN_RECORD
    return read_object(path) if path.exists() else {}


def update_run_record(
    confinement: Confinement,
    section: str,
    value: object,
) -> None:
    """Replace one section of the run record."""
    record = load_run_record(confinement)
    record[section] = value
    write_json(confinement, confinement.run / RUN_RECORD, record)


def record_stop(
    confinement: Confinement,
    phase: str,
    error: BaseException,
) -> None:
    """Append a stop record to the run record."""
    record = load_run_record(confinement)
    stops = record.get("stops")
    entries = list(stops) if isinstance(stops, list) else []
    entries.append(
        {
            "phase": phase,
            "type": type(error).__name__,
            "message": str(error),
            "time": now(),
        },
    )
    record["stops"] = entries
    write_json(confinement, confinement.run / RUN_RECORD, record)


def repository_root(start: Path = PROJECT_DIR) -> Path | None:
    """Return the repository containing ``start``, if there is one."""
    for directory in (start, *start.parents):
        if (directory / ".git").exists():
            return directory
    return None


def check_outside_repository(*directories: Path) -> None:
    """Check that the run and download directories are outside the repository.

    Raises:
        StopConditionError: If one of them lies inside it.
    """
    root = repository_root()
    if root is None:
        return
    for directory in directories:
        if directory.resolve().is_relative_to(root.resolve()):
            message = "the run and download directories must be outside the "
            message += "repository"
            raise StopConditionError(message)


def run_git(arguments: Sequence[str]) -> str:
    """Run a read-only git command in the project directory.

    Raises:
        StopConditionError: If git fails.
    """
    try:
        completed = subprocess.run(  # noqa: S603 - fixed read-only commands
            ["git", *arguments],  # noqa: S607 - git from PATH
            cwd=PROJECT_DIR,
            capture_output=True,
            check=True,
            text=True,
            env={**os.environ, "GIT_OPTIONAL_LOCKS": "0"},
        )
    except (OSError, subprocess.CalledProcessError) as error:
        message = f"git {' '.join(arguments)} failed"
        raise StopConditionError(message) from error
    return completed.stdout


def source_state(git: Git = run_git) -> dict[str, object]:
    """Return the converter and harness commit and the project's dirty flag.

    The converters and the harness are in one project, so one commit names
    both.
    """
    commit = git(["rev-parse", "HEAD"]).strip()
    status = git(["status", "--porcelain", "--", str(PROJECT_DIR)])
    return {"commit": commit, "dirty": bool(status.strip())}
