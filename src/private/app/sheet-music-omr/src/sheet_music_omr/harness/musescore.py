"""Run MuseScore Studio 4 conversions under the protocol's isolation.

The harness uses the existing installation and user settings on Windows or
from WSL2 on the Windows host. It never passes ``-F``, ``-R`` or ``--force``,
converts one file per invocation with ``-o`` under a cross-process lock, keeps
a copy of the settings file in the run directory when one exists, never
restores it, records the MuseScore version and binary hash as provenance, and
stops if any of the eight MusicXML preferences changes from the run's baseline.
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
from functools import lru_cache
from pathlib import Path
from typing import TYPE_CHECKING

from sheet_music_omr.harness.privacy import (
    identity_strings_from_path,
    scrub_paths,
)
from sheet_music_omr.harness.stops import StopConditionError

if TYPE_CHECKING:
    from collections.abc import Callable, Generator, Mapping, Sequence

    from sheet_music_omr.harness.confine import Confinement

DEFAULT_EXECUTABLE = Path(r"C:\Program Files\MuseScore 4\bin\MuseScore4.exe")
"""The default installation path of MuseScore Studio 4 on Windows."""

WSL_DEFAULT_EXECUTABLE = Path(
    "/mnt/c/Program Files/MuseScore 4/bin/MuseScore4.exe"
)
"""The default Windows host installation path visible from WSL2."""

DISPLAY_NAME = "MuseScore Studio 4"
"""The installer's display name in the Windows uninstall registry."""

VERSION = "4.7.6.261022093"
"""The reference MuseScore Studio 4.7.6 build cited in the protocol sources."""

SOURCE_VERIFIED_SERIES = ("4.7.6", "4.7.7")
"""MuseScore Studio 4 release series with source-verified MusicXML defaults."""

STEP_SECONDS = 300.0
"""The time bound of one conversion step."""

SETTINGS_COPY = "MuseScore4.ini.copy"
"""The name of the settings copy in the run directory."""

HASH_LOG = "settings-hashes.jsonl"
"""The name of the settings-hash log in the run directory."""

LOCK_NAME = ".musescore.lock"
"""The lock file in the shared download directory serializing conversions."""

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
_VERSION_4 = re.compile(r"^4\.[0-9]+(?:\.[0-9]+)*$")
_LONG_VERSION = re.compile(
    r"\bVersion\s+(?<![0-9.])([0-9]+(?:\.[0-9]+)+)(?!\.[0-9])"
)
_SHORT_VERSION = re.compile(
    r"(?m)^MuseScore[^\r\n]*?\s+v?(?<![0-9.])([0-9]+(?:\.[0-9]+)+)\s*$"
)
_WINDOWS_APPDATA = re.compile(r"^[A-Za-z]:[\\/]|^\\\\")
_WSL_CMD = Path("/mnt/c/Windows/System32/cmd.exe")
_WSL_REG = Path("/mnt/c/Windows/System32/reg.exe")

type Launcher = Callable[[Sequence[str], float], tuple[int | None, str]]
"""Run a command with a time bound; return its exit status and output.

The status is ``None`` when the command was stopped at the time bound.
"""


def default_executable() -> Path:
    """Return the platform's default MuseScore Studio 4 executable path."""
    if sys.platform != "win32" and (
        WSL_DEFAULT_EXECUTABLE.is_file() or Path("/mnt/c").is_dir()
    ):
        return WSL_DEFAULT_EXECUTABLE
    return DEFAULT_EXECUTABLE


def _wslpath(flag: str, value: str) -> str | None:
    try:
        completed = subprocess.run(  # noqa: S603 - fixed WSL path utility
            ["wslpath", flag, value],  # noqa: S607
            capture_output=True,
            check=True,
            text=True,
            timeout=10.0,
        )
    except OSError, subprocess.SubprocessError:
        return None
    resolved = completed.stdout.strip()
    return resolved or None


def _windows_path_for_wsl(path: Path) -> str:
    """Translate a WSL path to a Windows path for ``MuseScore4.exe``."""
    resolved = path.resolve()
    if resolved.exists():
        direct = _wslpath("-w", str(resolved))
        if direct is not None:
            return direct
    parent = _wslpath("-w", str(resolved.parent))
    if parent is not None:
        return f"{parent.rstrip('\\')}\\{resolved.name}"
    return str(path)


@lru_cache(maxsize=1)
def wsl_host_appdata() -> str | None:
    """Query Windows ``%APPDATA%`` from WSL2 in UTF-16LE when available."""
    if sys.platform != "win32" and _WSL_CMD.is_file():
        try:
            completed = subprocess.run(  # noqa: S603 - fixed host query
                [str(_WSL_CMD), "/u", "/d", "/c", "echo %APPDATA%"],
                cwd="/mnt/c",
                capture_output=True,
                check=True,
                timeout=10.0,
            )
            decoded = completed.stdout.decode("utf-16-le").strip()
        except OSError, subprocess.SubprocessError, UnicodeDecodeError:
            return None
        if decoded and decoded != "%APPDATA%" and "\ufffd" not in decoded:
            return decoded
    return None


def _resolve_appdata_settings(
    raw: str,
    *,
    to_posix: Callable[[str], str | None] | None = None,
    require_existing_dir: bool = False,
) -> Path:
    cleaned = raw.strip()
    if not cleaned or cleaned == "%APPDATA%" or "\ufffd" in cleaned:
        message = "cannot resolve %APPDATA% for MuseScore settings"
        raise StopConditionError(message)
    if sys.platform != "win32" and _WINDOWS_APPDATA.match(cleaned):
        translate = (
            (lambda value: _wslpath("-u", value))
            if to_posix is None
            else to_posix
        )
        posix = translate(cleaned)
        if not posix or "\ufffd" in posix:
            message = "cannot translate Windows %APPDATA% path in WSL"
            raise StopConditionError(message)
        cleaned = posix
    candidate = Path(cleaned)
    if not candidate.is_absolute() and not _WINDOWS_APPDATA.match(cleaned):
        message = f"resolved %APPDATA% is not an absolute path: {cleaned!r}"
        raise StopConditionError(message)
    if require_existing_dir and not candidate.is_dir():
        message = f"resolved %APPDATA% directory does not exist: {candidate}"
        raise StopConditionError(message)
    return candidate / "MuseScore" / "MuseScore4.ini"


def settings_path(
    environ: Mapping[str, str] | None = None,
    *,
    to_posix: Callable[[str], str | None] | None = None,
) -> Path:
    """Return the user settings file of MuseScore 4 on Windows or WSL2.

    Raises:
        StopConditionError: If ``%APPDATA%`` cannot be resolved to an existing
            host directory. An absent ``MuseScore4.ini`` under that directory
            remains valid on a fresh installation before first GUI launch.
    """
    if environ is not None:
        return _resolve_appdata_settings(
            environ.get("APPDATA", ""), to_posix=to_posix
        )
    env_appdata = os.environ.get("APPDATA", "").strip()
    if sys.platform == "win32" or env_appdata:
        return _resolve_appdata_settings(
            env_appdata,
            to_posix=to_posix,
            require_existing_dir=True,
        )
    host_appdata = wsl_host_appdata()
    if host_appdata is not None:
        return _resolve_appdata_settings(
            host_appdata,
            to_posix=to_posix,
            require_existing_dir=True,
        )
    message = "cannot resolve %APPDATA% for MuseScore settings"
    raise StopConditionError(message)


def sha256_file(path: Path) -> str:
    """Return the lowercase hexadecimal SHA-256 of a file."""
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1 << 20), b""):
            digest.update(chunk)
    return digest.hexdigest()


def has_source_verified_defaults(version: str | None) -> bool:
    """Return whether ``version`` matches an inspected MuseScore release."""
    if version is None:
        return False
    cleaned = version.strip()
    return any(
        cleaned == series or cleaned.startswith(f"{series}.")
        for series in SOURCE_VERIFIED_SERIES
    )


def read_preferences(
    text: str,
    *,
    version: str | None = VERSION,
) -> dict[str, dict[str, str]]:
    r"""Read the MusicXML preferences from the text of the settings file.

    The file is Qt's INI format: a key ``a/b/c`` is key ``b\c`` of section
    ``[a]``. A preference that the file does not set uses its inspected 4.7.6
    source default, tagged ``default`` on source-verified releases or
    ``assumed_v4_7_6_default`` on newer unverified 4.x releases.

    Returns:
        By settings key: its label, value and source.
    """
    default_source = (
        "default"
        if has_source_verified_defaults(version)
        else "assumed_v4_7_6_default"
    )
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
            "source": "file" if key in values else default_source,
        }
        for key, (label, default) in PREFERENCES.items()
    }


def preference_values(
    preferences: Mapping[str, Mapping[str, str]],
) -> dict[str, str]:
    """Extract the eight effective MusicXML preference values by key."""
    return {
        key: str(entry.get("value", default))
        for key, (_, default) in PREFERENCES.items()
        for entry in (preferences.get(key, {}),)
    }


def is_supported_version(version: str | None) -> bool:
    """Return whether ``version`` names a MuseScore Studio 4 release."""
    return version is not None and bool(_VERSION_4.fullmatch(version.strip()))


def verify_rendering_preferences(musescore: MuseScore) -> None:
    """Require MuseScore 4.x and all eight expected MusicXML preferences."""
    if not is_supported_version(musescore.version):
        msg = f"unsupported MuseScore version {musescore.version!r}"
        raise StopConditionError(msg)
    effective = preference_values(musescore.baseline_preferences)
    for key, (_, expected) in PREFERENCES.items():
        if effective.get(key, "").lower() != expected.lower():
            msg = (
                f"MuseScore preference {key} must be {expected!r} "
                f"(importInferTextType=false required), got "
                f"{effective.get(key)!r}"
            )
            raise StopConditionError(msg)


def _parse_reg_query(output: str, display_name: str) -> str | None:
    """Extract ``DisplayVersion`` for ``display_name`` from ``reg query /s``."""
    for block in re.split(r"\r?\n\s*\r?\n", output):
        values: dict[str, str] = {}
        for raw_line in block.splitlines():
            parts = re.split(r"\s{2,}", raw_line.strip(), maxsplit=2)
            if len(parts) == 3:  # noqa: PLR2004
                values[parts[0]] = parts[2]
        if (
            values.get("DisplayName") == display_name
            and "DisplayVersion" in values
        ):
            return values["DisplayVersion"]
    return None


def _wsl_registry_version(display_name: str = DISPLAY_NAME) -> str | None:
    """Query the Windows uninstall registry from WSL2 via ``reg.exe``."""
    if not _WSL_REG.is_file():
        return None
    roots = (
        r"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        r"HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
        r"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
    )
    for root in roots:
        try:
            completed = subprocess.run(  # noqa: S603 - fixed read-only query
                [str(_WSL_REG), "query", root, "/s"],
                cwd="/mnt/c",
                capture_output=True,
                check=True,
                text=True,
                errors="replace",
                timeout=10.0,
            )
        except OSError, subprocess.SubprocessError:
            continue
        version = _parse_reg_query(completed.stdout, display_name)
        if version is not None:
            return version
    return None


def registry_version(display_name: str = DISPLAY_NAME) -> str | None:
    """Return the installed version from the Windows uninstall registry."""
    if sys.platform == "win32":
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
                                name = winreg.QueryValueEx(
                                    entry, "DisplayName"
                                )[0]
                                if name != display_name:
                                    continue
                                return str(
                                    winreg.QueryValueEx(
                                        entry, "DisplayVersion"
                                    )[0],
                                )
                            except OSError:
                                continue
            except OSError:
                continue
        return None
    return _wsl_registry_version(display_name)


def wsl_translate_command(
    command: Sequence[str],
    *,
    in_wsl: bool | None = None,
    to_windows: Callable[[str], str] | None = None,
) -> list[str]:
    """Translate ``-o TARGET SOURCE`` paths when WSL invokes a ``.exe``."""
    items = list(command)
    active = (sys.platform != "win32") if in_wsl is None else in_wsl
    mapper = (
        (lambda raw: _windows_path_for_wsl(Path(raw)))
        if to_windows is None
        else to_windows
    )
    if (
        active
        and len(items) >= 4  # noqa: PLR2004
        and Path(items[0]).suffix.lower() == ".exe"
        and items[1] == "-o"
    ):
        items[2] = mapper(items[2])
        items[3] = mapper(items[3])
    return items


def run_command(
    command: Sequence[str], timeout: float
) -> tuple[int | None, str]:
    """Run a command, stopping it at the time bound.

    Returns:
        The exit status, or ``None`` when it was stopped, and its combined
        standard output and error.
    """
    translated = wsl_translate_command(command)
    with subprocess.Popen(  # noqa: S603 - a fixed executable and arguments
        translated,
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


def cli_version(
    executable: Path,
    launcher: Launcher = run_command,
) -> str | None:
    """Probe ``executable`` directly via ``--long-version`` or ``--version``."""
    for flag, pattern in (
        ("--long-version", _LONG_VERSION),
        ("--version", _SHORT_VERSION),
    ):
        try:
            status, output = launcher([str(executable), flag], 30.0)
        except OSError:
            return None
        if status == 0:
            match = pattern.search(output)
            if match is not None:
                return match.group(1)
    return None


def sanitize_log(output: str, private: Sequence[str]) -> str:
    """Keep the tail of a log with private paths and names replaced.

    Args:
        output: The log text.
        private: Strings that must not appear, such as paths and the user
            name. Each is replaced with ``<private>``.

    Returns:
        At most the last 20 non-empty lines and 2,000 characters.
    """
    text = scrub_paths(output)
    for value in sorted({p for p in private if p}, key=len, reverse=True):
        variants = {value, value.replace("\\", "/"), value.replace("/", "\\")}
        for variant in variants:
            text = re.sub(re.escape(variant), "<private>", text, flags=re.I)
    lines = [line.rstrip() for line in text.splitlines() if line.strip()]
    return "\n".join(lines[-_LOG_LINES:])[-_LOG_CHARACTERS:]


def _settings_identity_strings(settings: Path) -> list[str]:
    return [
        *identity_strings_from_path(str(settings)),
        str(settings.parent),
    ]


def _path_variants(
    paths: Sequence[Path],
    *,
    translate_wsl: bool,
) -> list[str]:
    values: list[str] = []
    for path in paths:
        values.append(str(path))
        if translate_wsl:
            values.append(_windows_path_for_wsl(path))
    return values


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


def _resolve_executable_version(
    executable: Path | None,
    version_lookup: Callable[[], str | None],
    launcher: Launcher,
) -> tuple[Path, str]:
    default_exe = default_executable()
    raw_exe = default_exe if executable is None else executable
    if not raw_exe.is_file():
        message = f"MuseScore is not installed at {raw_exe}"
        raise StopConditionError(message)
    resolved = raw_exe.resolve()
    is_override = executable is not None and (
        not default_exe.exists() or resolved != default_exe.resolve()
    )
    if version_lookup is not registry_version:
        version = version_lookup()
    elif is_override:
        version = cli_version(resolved, launcher)
    else:
        version = version_lookup()
        if version is None and launcher is run_command:
            version = cli_version(resolved, launcher)
    if not is_supported_version(version):
        message = f"MuseScore version must be 4.x, got {version!r}"
        raise StopConditionError(message)
    assert version is not None  # noqa: S101 - checked above
    return resolved, version


@dataclass(slots=True)
class MuseScore:
    """The recorded MuseScore Studio 4 installation, guarded by preferences.

    Attributes:
        executable: The executable path.
        version: The installed version string (`4.x`).
        executable_sha256: The SHA-256 of the MuseScore executable binary.
        settings: The user settings file path (may be absent on fresh install).
        confinement: The harness's write roots.
        baseline: The initial settings file SHA-256, or ``None`` when absent.
        baseline_preferences: The baseline eight MusicXML preferences.
        launcher: Runs one command with a time bound.
    """

    executable: Path
    version: str
    executable_sha256: str
    settings: Path
    confinement: Confinement
    baseline: str | None
    baseline_preferences: dict[str, dict[str, str]]
    launcher: Launcher = run_command
    private: tuple[str, ...] = field(default=())

    @classmethod
    def open(  # noqa: PLR0913
        cls,
        confinement: Confinement,
        *,
        executable: Path | None = None,
        settings: Path | None = None,
        allow_missing_settings: bool = False,
        version_lookup: Callable[[], str | None] = registry_version,
        launcher: Launcher = run_command,
    ) -> MuseScore:
        """Check the MuseScore 4 installation and MusicXML preferences.

        When the settings file exists and no copy has been written in the run
        directory yet, the settings file is copied once and never restored.
        The eight effective MusicXML preferences from that baseline must remain
        unchanged across the run.

        Raises:
            StopConditionError: If MuseScore 4 is not installed at the path,
                its major version is not 4, an explicit settings override does
                not exist, or any of the eight MusicXML preferences differs
                from the run's baseline.
        """
        executable, version = _resolve_executable_version(
            executable, version_lookup, launcher
        )
        executable_sha256 = sha256_file(executable)
        if settings is None:
            settings = settings_path()
        elif not settings.is_file() and not allow_missing_settings:
            message = f"MuseScore settings file does not exist at {settings}"
            raise StopConditionError(message)
        copy = confinement.run / SETTINGS_COPY
        hash_log = confinement.run / HASH_LOG
        if not copy.exists() and not hash_log.exists() and settings.is_file():
            confinement.write_bytes(copy, settings.read_bytes())
        if copy.is_file():
            baseline: str | None = sha256_file(copy)
            baseline_text = copy.read_text(encoding="utf-8", errors="replace")
        elif not hash_log.exists() and settings.is_file():
            baseline = sha256_file(settings)
            baseline_text = settings.read_text(
                encoding="utf-8", errors="replace"
            )
        else:
            baseline = None
            baseline_text = ""
        baseline_preferences = read_preferences(baseline_text, version=version)
        translate_wsl = (
            sys.platform != "win32" and executable.suffix.lower() == ".exe"
        )
        private = tuple(
            dict.fromkeys(
                value
                for value in (
                    *_path_variants(
                        (
                            confinement.run,
                            confinement.downloads,
                            Path.home(),
                            settings,
                            settings.parent,
                        ),
                        translate_wsl=translate_wsl,
                    ),
                    *_settings_identity_strings(settings),
                    os.environ.get("USERNAME", ""),
                    os.environ.get("USER", ""),
                )
                if value.strip()
            )
        )
        instance = cls(
            executable,
            version,
            executable_sha256,
            settings,
            confinement,
            baseline,
            baseline_preferences,
            launcher,
            private,
        )
        instance.check_settings()
        return instance

    def preferences(self) -> dict[str, dict[str, str]]:
        """Read the MusicXML preferences in effect, read-only."""
        text = (
            self.settings.read_text(encoding="utf-8", errors="replace")
            if self.settings.is_file()
            else ""
        )
        return read_preferences(text, version=self.version)

    def check_settings(self) -> str | None:
        """Verify the eight MusicXML preferences match the run's baseline.

        Returns:
            The current settings file SHA-256, or ``None`` when absent.

        Raises:
            StopConditionError: If any of the eight MusicXML preferences
                differs from the run's baseline.
        """
        current = (
            sha256_file(self.settings) if self.settings.is_file() else None
        )
        if preference_values(self.preferences()) != preference_values(
            self.baseline_preferences
        ):
            message = (
                "the MuseScore MusicXML preferences changed from the run's "
                "recorded settings"
            )
            raise StopConditionError(message)
        return current

    @contextmanager
    def batch(self, name: str) -> Generator[None]:
        """Log the settings hash and verify MusicXML preferences around a batch.

        Raises:
            StopConditionError: If any MusicXML preference differs before or
                after the batch.
        """
        self._log(name, "before")
        yield
        self._log(name, "after")

    def _log(self, name: str, when: str) -> None:
        current = (
            sha256_file(self.settings) if self.settings.is_file() else None
        )
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
        """Convert one file with ``-o``, serialized by the download lock.

        Args:
            source: The input file.
            output: The output file, which must not exist yet.
            timeout: The time bound in seconds.

        Returns:
            The exit status, whether that invocation wrote the output, and
            the sanitized log excerpt.

        Raises:
            StopConditionError: If the output lies outside the write roots or
                already exists, or a MusicXML preference changed.
        """
        target = self.confinement.check(output)
        if target.exists():
            message = "a MuseScore output path already exists"
            raise StopConditionError(message)
        target.parent.mkdir(parents=True, exist_ok=True)
        lock_path = self.confinement.downloads / LOCK_NAME
        with self.confinement.lock(lock_path):
            started = time.time()
            exit_code, log = self.launcher(
                [str(self.executable), "-o", str(target), str(source)],
                timeout,
            )
            elapsed = time.time() - started
            written = target.is_file() and target.stat().st_mtime >= started - 2
            self.check_settings()
        translate_wsl = (
            sys.platform != "win32" and self.executable.suffix.lower() == ".exe"
        )
        private = (
            *self.private,
            *_path_variants(
                (source, source.parent, target, target.parent),
                translate_wsl=translate_wsl,
            ),
        )
        return Conversion(
            exit_code,
            written,
            elapsed,
            sanitize_log(log, private),
        )
