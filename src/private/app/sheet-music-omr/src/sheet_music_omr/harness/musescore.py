"""Run MuseScore Studio 4.7.6 conversions under the protocol's isolation.

The harness uses the existing installation and user settings. It never passes
``-F``, ``-R`` or ``--force``, converts one file per invocation with ``-o``,
keeps a copy of the settings file in the run directory, never restores it,
and stops when the settings file's hash changes.
"""

from __future__ import annotations

import hashlib
import json
import os
import re
import subprocess
import sys
import time
from contextlib import contextmanager
from dataclasses import dataclass, field
from datetime import UTC, datetime
from pathlib import Path
from typing import TYPE_CHECKING

from sheet_music_omr.harness.stops import StopConditionError

if TYPE_CHECKING:
    from collections.abc import Callable, Generator, Mapping, Sequence

    from sheet_music_omr.harness.confine import Confinement

DEFAULT_EXECUTABLE = Path(r"C:\Program Files\MuseScore 4\bin\MuseScore4.exe")
"""The default installation path of MuseScore Studio 4 on Windows."""

DISPLAY_NAME = "MuseScore Studio 4"
"""The installer's display name in the Windows uninstall registry."""

VERSION = "4.7.6.261022093"
"""The pinned version string, MuseScore Studio 4.7.6."""

STEP_SECONDS = 300.0
"""The time bound of one conversion step."""

SETTINGS_COPY = "MuseScore4.ini.copy"
"""The name of the settings copy in the run directory."""

HASH_LOG = "settings-hashes.jsonl"
"""The name of the settings-hash log in the run directory."""

PREFERENCES: Mapping[str, tuple[str, str]] = {
    "import/musicXml/importBreaks": ("import breaks", "true"),
    "import/musicXml/importLayout": ("import layout", "true"),
    "export/musicXml/exportLayout": ("export layout", "true"),
    "export/musicXml/exportMu3Compat": (
        "export compatibility with MuseScore 3",
        "false",
    ),
    "export/musicXml/exportBreaks": ("export breaks", "All"),
    "export/musicXml/exportInvisibleElements": (
        "export invisible elements",
        "false",
    ),
    "import/compatibility/apply_edwin_for_xml": ("apply default font", "false"),
    "import/musicXml/importInferTextType": ("infer text type", "false"),
}
"""Each recorded MusicXML preference: its label and 4.7.6 source default."""

_LOG_LINES = 20
_LOG_CHARACTERS = 2000
_SECTION = re.compile(r"^\[(?P<name>[^\]]*)\]\s*$")
_GENERAL = "General"

type Launcher = Callable[[Sequence[str], float], tuple[int | None, str]]
"""Run a command with a time bound; return its exit status and output.

The status is ``None`` when the command was stopped at the time bound.
"""


def settings_path(environ: Mapping[str, str] | None = None) -> Path:
    """Return the user settings file of MuseScore 4 on Windows."""
    environ = os.environ if environ is None else environ
    return Path(environ.get("APPDATA", "")) / "MuseScore" / "MuseScore4.ini"


def sha256_file(path: Path) -> str:
    """Return the lowercase hexadecimal SHA-256 of a file."""
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1 << 20), b""):
            digest.update(chunk)
    return digest.hexdigest()


def read_preferences(text: str) -> dict[str, dict[str, str]]:
    r"""Read the MusicXML preferences from the text of the settings file.

    The file is Qt's INI format: a key ``a/b/c`` is key ``b\c`` of section
    ``[a]``. A preference that the file does not set has its source default.

    Returns:
        By settings key: its label, value and source, ``file`` or
        ``default``.
    """
    values: dict[str, str] = {}
    section = ""
    for line in text.splitlines():
        stripped = line.strip()
        if not stripped or stripped.startswith(";"):
            continue
        header = _SECTION.match(stripped)
        if header:
            section = header.group("name")
            continue
        key, separator, value = stripped.partition("=")
        if not separator:
            continue
        name = key.strip().replace("\\", "/")
        if section and section != _GENERAL:
            name = f"{section}/{name}"
        values[name] = value.strip().strip('"')
    return {
        key: {
            "label": label,
            "value": values.get(key, default),
            "source": "file" if key in values else "default",
        }
        for key, (label, default) in PREFERENCES.items()
    }


def registry_version(display_name: str = DISPLAY_NAME) -> str | None:
    """Return the installed version from the Windows uninstall registry."""
    if sys.platform != "win32":
        return None
    import winreg  # noqa: PLC0415 - Windows only

    roots = (
        (
            winreg.HKEY_LOCAL_MACHINE,
            r"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        ),
        (
            winreg.HKEY_LOCAL_MACHINE,
            r"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
        ),
        (
            winreg.HKEY_CURRENT_USER,
            r"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        ),
    )
    for root, path in roots:
        try:
            with winreg.OpenKey(root, path) as uninstall:
                count = winreg.QueryInfoKey(uninstall)[0]
                for index in range(count):
                    with winreg.OpenKey(
                        uninstall,
                        winreg.EnumKey(uninstall, index),
                    ) as entry:
                        try:
                            name = winreg.QueryValueEx(entry, "DisplayName")[0]
                            if name != display_name:
                                continue
                            return str(
                                winreg.QueryValueEx(entry, "DisplayVersion")[0],
                            )
                        except OSError:
                            continue
        except OSError:
            continue
    return None


def run_command(
    command: Sequence[str], timeout: float
) -> tuple[int | None, str]:
    """Run a command, stopping it at the time bound.

    Returns:
        The exit status, or ``None`` when it was stopped, and its combined
        standard output and error.
    """
    with subprocess.Popen(  # noqa: S603 - a fixed executable and arguments
        list(command),
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        stdin=subprocess.DEVNULL,
    ) as process:
        try:
            output, _ = process.communicate(timeout=timeout)
        except subprocess.TimeoutExpired:
            process.kill()
            output, _ = process.communicate()
            return None, output.decode("utf-8", errors="replace")
        return process.returncode, output.decode("utf-8", errors="replace")


def sanitize_log(output: str, private: Sequence[str]) -> str:
    """Keep the tail of a log with private paths and names replaced.

    Args:
        output: The log text.
        private: Strings that must not appear, such as paths and the user
            name. Each is replaced with ``<private>``.

    Returns:
        At most the last 20 non-empty lines and 2,000 characters.
    """
    text = output
    for value in sorted({p for p in private if p}, key=len, reverse=True):
        variants = {value, value.replace("\\", "/"), value.replace("/", "\\")}
        for variant in variants:
            text = re.sub(re.escape(variant), "<private>", text, flags=re.I)
    text = re.sub(r"[A-Za-z]:[\\/][^\s\"']*", "<path>", text)
    lines = [line.rstrip() for line in text.splitlines() if line.strip()]
    return "\n".join(lines[-_LOG_LINES:])[-_LOG_CHARACTERS:]


@dataclass(frozen=True, slots=True)
class Conversion:
    """The outcome of one MuseScore conversion.

    Attributes:
        exit_code: The exit status, or ``None`` when the time bound stopped
            the conversion.
        written: Whether the output exists and that invocation wrote it.
        seconds: The elapsed time.
        log: The sanitized log excerpt.
    """

    exit_code: int | None
    written: bool
    seconds: float
    log: str

    @property
    def timed_out(self) -> bool:
        """Whether the time bound stopped the conversion."""
        return self.exit_code is None

    @property
    def ok(self) -> bool:
        """Whether the conversion exited with 0 and wrote its output."""
        return self.exit_code == 0 and self.written

    def record(self) -> dict[str, object]:
        """Return the conversion as plain data for the run directory."""
        return {
            "exit_code": self.exit_code,
            "timed_out": self.timed_out,
            "written": self.written,
            "seconds": round(self.seconds, 3),
            "log": self.log,
        }


@dataclass(slots=True)
class MuseScore:
    """The pinned MuseScore installation, guarded by the settings hash.

    Attributes:
        executable: The executable path.
        version: The installed version string.
        settings: The user settings file.
        confinement: The harness's write roots.
        baseline: The settings hash when the run started.
        launcher: Runs one command with a time bound.
    """

    executable: Path
    version: str
    settings: Path
    confinement: Confinement
    baseline: str
    launcher: Launcher = run_command
    private: tuple[str, ...] = field(default=())

    @classmethod
    def open(
        cls,
        confinement: Confinement,
        *,
        executable: Path = DEFAULT_EXECUTABLE,
        settings: Path | None = None,
        version_lookup: Callable[[], str | None] = registry_version,
        launcher: Launcher = run_command,
    ) -> MuseScore:
        """Check the installation and settings file and copy the settings.

        The settings file is copied into the run directory once, when no copy
        exists there yet, and is never restored. The copy's hash is the
        baseline of every later invocation in the run directory.

        Raises:
            StopConditionError: If MuseScore is not installed at the path, its
                version is not the pinned one, the settings file is absent or
                its hash differs from the run directory's copy.
        """
        if not executable.is_file():
            message = f"MuseScore is not installed at {executable}"
            raise StopConditionError(message)
        version = version_lookup()
        if version != VERSION:
            message = f"MuseScore version is {version}, not {VERSION}"
            raise StopConditionError(message)
        settings = settings_path() if settings is None else settings
        if not settings.is_file():
            message = "the MuseScore settings file is absent"
            raise StopConditionError(message)
        copy = confinement.run / SETTINGS_COPY
        if not copy.exists():
            confinement.write_bytes(copy, settings.read_bytes())
        baseline = sha256_file(copy)
        if sha256_file(settings) != baseline:
            message = (
                "the MuseScore settings file hash differs from the run's "
                "recorded settings"
            )
            raise StopConditionError(message)
        private = (
            str(confinement.run),
            str(confinement.downloads),
            str(Path.home()),
            os.environ.get("USERNAME", ""),
        )
        return cls(
            executable,
            version,
            settings,
            confinement,
            baseline,
            launcher,
            private,
        )

    def preferences(self) -> dict[str, dict[str, str]]:
        """Read the MusicXML preferences in effect, read-only."""
        return read_preferences(
            self.settings.read_text(encoding="utf-8", errors="replace"),
        )

    def check_settings(self) -> str:
        """Return the settings hash, which must equal the baseline.

        Raises:
            StopConditionError: If the file is absent or its hash changed.
        """
        if not self.settings.is_file():
            message = "the MuseScore settings file is absent"
            raise StopConditionError(message)
        current = sha256_file(self.settings)
        if current != self.baseline:
            message = "the MuseScore settings file hash changed"
            raise StopConditionError(message)
        return current

    @contextmanager
    def batch(self, name: str) -> Generator[None]:
        """Log the settings hash before and after a batch of conversions.

        Raises:
            StopConditionError: If the hash differs from the baseline before
                or after the batch.
        """
        self._log(name, "before")
        yield
        self._log(name, "after")

    def _log(self, name: str, when: str) -> None:
        exists = self.settings.is_file()
        current = sha256_file(self.settings) if exists else None
        log = self.confinement.run / HASH_LOG
        line = json.dumps(
            {
                "batch": name,
                "when": when,
                "sha256": current,
                "time": datetime.now(UTC).isoformat(timespec="seconds"),
            },
        )
        previous = log.read_text(encoding="utf-8") if log.exists() else ""
        self.confinement.write_text(log, previous + line + "\n")
        self.check_settings()

    def convert(
        self,
        source: Path,
        output: Path,
        *,
        timeout: float = STEP_SECONDS,
    ) -> Conversion:
        """Convert one file with ``-o``, its format chosen by the extension.

        Args:
            source: The input file.
            output: The output file, which must not exist yet.
            timeout: The time bound in seconds.

        Returns:
            The exit status, whether that invocation wrote the output, and
            the sanitized log excerpt.

        Raises:
            StopConditionError: If the output lies outside the write roots or
                already exists, or the settings hash changed.
        """
        target = self.confinement.check(output)
        if target.exists():
            message = "a MuseScore output path already exists"
            raise StopConditionError(message)
        target.parent.mkdir(parents=True, exist_ok=True)
        started = time.time()
        exit_code, log = self.launcher(
            [str(self.executable), "-o", str(target), str(source)],
            timeout,
        )
        elapsed = time.time() - started
        written = target.is_file() and target.stat().st_mtime >= started - 2
        self.check_settings()
        private = (*self.private, str(source), str(target))
        return Conversion(
            exit_code,
            written,
            elapsed,
            sanitize_log(log, private),
        )
