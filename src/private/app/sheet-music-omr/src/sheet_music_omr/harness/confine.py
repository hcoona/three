"""Confine the harness's writes to its run, temporary and download directories.

A write anywhere else is a stop condition of the comparison protocol, so every
harness write goes through a :class:`Confinement`, which refuses a path outside
its roots before anything is written.
"""

from __future__ import annotations

import os
import tempfile
from dataclasses import dataclass
from pathlib import Path

from sheet_music_omr.harness.stops import StopConditionError

TEMP_NAME = "tmp"
"""The name of the temporary directory inside the run directory."""


class ConfinementError(StopConditionError):
    """A write would leave the run, temporary and download directories."""


@dataclass(frozen=True, slots=True)
class Confinement:
    """The directories that the harness may write to.

    Attributes:
        run: The run directory.
        downloads: The download directory, shared by reruns.
    """

    run: Path
    downloads: Path

    @classmethod
    def create(cls, run: Path, downloads: Path) -> Confinement:
        """Resolve both directories and create them with the temporary one.

        Raises:
            ConfinementError: If one directory lies inside the other.
        """
        run = run.resolve()
        downloads = downloads.resolve()
        if run.is_relative_to(downloads) or downloads.is_relative_to(run):
            message = "the run and download directories must be disjoint"
            raise ConfinementError(message)
        for directory in (run, run / TEMP_NAME, downloads):
            directory.mkdir(parents=True, exist_ok=True)
        return cls(run, downloads)

    @property
    def temp(self) -> Path:
        """The temporary directory inside the run directory."""
        return self.run / TEMP_NAME

    def check(self, target: Path) -> Path:
        """Return ``target`` resolved, if it lies in the run or downloads.

        Raises:
            ConfinementError: If it lies elsewhere.
        """
        resolved = target.resolve()
        if resolved.is_relative_to(self.run) or resolved.is_relative_to(
            self.downloads,
        ):
            return resolved
        message = f"write outside the run and download directories: {target}"
        raise ConfinementError(message)

    def mkdir(self, target: Path) -> Path:
        """Create a directory and its parents inside the roots."""
        resolved = self.check(target)
        resolved.mkdir(parents=True, exist_ok=True)
        return resolved

    def write_bytes(self, target: Path, data: bytes) -> Path:
        """Write a file atomically inside the roots.

        The data goes to a temporary file in the same directory, which then
        replaces the target, so a stopped run leaves no partial file.
        """
        resolved = self.check(target)
        resolved.parent.mkdir(parents=True, exist_ok=True)
        handle, name = tempfile.mkstemp(
            prefix=f".{resolved.name}.",
            dir=resolved.parent,
        )
        try:
            with os.fdopen(handle, "wb") as stream:
                stream.write(data)
            Path(name).replace(resolved)
        except BaseException:
            Path(name).unlink(missing_ok=True)
            raise
        return resolved

    def write_text(self, target: Path, text: str) -> Path:
        """Write UTF-8 text atomically inside the roots."""
        return self.write_bytes(target, text.encode("utf-8"))

    def temp_dir(self, prefix: str) -> Path:
        """Create a fresh directory inside the temporary directory."""
        self.temp.mkdir(parents=True, exist_ok=True)
        return Path(tempfile.mkdtemp(prefix=prefix, dir=self.temp))
