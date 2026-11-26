"""Run locked TLA+ SANY conformance checks and isolated TLC model checks."""

from __future__ import annotations

import argparse
import os
import shutil
import subprocess
import sys
import tempfile
import zipfile
from pathlib import Path
from typing import TYPE_CHECKING

if TYPE_CHECKING:
    from collections.abc import Sequence

ROOT = Path(__file__).resolve().parents[2]
DEFAULT_SANY_DRIVER = ROOT / "eng/linter/TlaSanyCheck.java"
MISE_TLA_TOOL = "github:tlaplus/tlaplus"
JAR_NAME = "tla2tools.jar"
USAGE_ERROR = 2


def _is_jar_archive(candidate: Path) -> bool:
    return candidate.is_file() and zipfile.is_zipfile(candidate)


def _resolve_tla_jar() -> Path | None:
    if "TLA2TOOLS_JAR" in os.environ:
        override = os.environ["TLA2TOOLS_JAR"]
        if not override:
            return None
        candidate = Path(override)
        return candidate if _is_jar_archive(candidate) else None

    for entry in os.environ.get("PATH", "").split(os.pathsep):
        if not entry:
            continue
        candidate = Path(entry) / JAR_NAME
        if _is_jar_archive(candidate):
            return candidate

    if mise := shutil.which("mise"):
        where = subprocess.run(
            (mise, "where", MISE_TLA_TOOL),
            cwd=ROOT,
            check=False,
            capture_output=True,
            text=True,
        )
        if where.returncode == 0 and (install_dir := where.stdout.strip()):
            candidate = Path(install_dir) / JAR_NAME
            if _is_jar_archive(candidate):
                return candidate
    return None


def _require_runtime() -> tuple[Path, Path] | None:
    found_java = shutil.which("java")
    if found_java is None:
        print(
            "java not found on PATH; run 'mise install --locked'.",
            file=sys.stderr,
        )
        return None
    jar = _resolve_tla_jar()
    if jar is None:
        print(
            "tla2tools.jar not found; run 'mise install --locked'.",
            file=sys.stderr,
        )
        return None
    return Path(found_java), jar


def _exit_status(returncode: int) -> int:
    return 128 + abs(returncode) if returncode < 0 else returncode


def _run_sany(driver: Path, files: Sequence[str]) -> int:
    if not files:
        print("tla.py sany requires at least one .tla file.", file=sys.stderr)
        return USAGE_ERROR
    if not driver.is_file():
        print(f"SANY driver not found: {driver}", file=sys.stderr)
        return USAGE_ERROR
    runtime = _require_runtime()
    if runtime is None:
        return USAGE_ERROR
    java, jar = runtime
    with tempfile.TemporaryDirectory(prefix="tla-sany-") as temp_dir:
        result = subprocess.run(
            (
                str(java),
                f"-Djava.io.tmpdir={temp_dir}",
                "-cp",
                str(jar),
                str(driver),
                "--",
                *files,
            ),
            check=False,
        )
    return _exit_status(result.returncode)


def _run_tlc(tlc_args: Sequence[str]) -> int:
    arguments = list(tlc_args)
    if arguments and arguments[0] == "--":
        arguments = arguments[1:]
    if not arguments:
        print(
            "tla.py tlc requires TLC arguments ending with a .tla module.",
            file=sys.stderr,
        )
        return USAGE_ERROR
    runtime = _require_runtime()
    if runtime is None:
        return USAGE_ERROR
    java, jar = runtime
    with tempfile.TemporaryDirectory(prefix="tla-tlc-") as temp_dir:
        temp_root = Path(temp_dir)
        java_tmp = temp_root / "tmp"
        java_tmp.mkdir()
        metadir_args = (
            []
            if "-metadir" in arguments
            else ["-metadir", str(temp_root / "states")]
        )
        result = subprocess.run(
            (
                str(java),
                "-XX:+UseParallelGC",
                f"-Djava.io.tmpdir={java_tmp}",
                "-cp",
                str(jar),
                "tlc2.TLC",
                *metadir_args,
                *arguments,
            ),
            check=False,
        )
    return _exit_status(result.returncode)


def main(argv: Sequence[str] | None = None) -> int:
    """Dispatch SANY parsing or isolated TLC model checking."""
    args = list(sys.argv[1:] if argv is None else argv)
    if not args or args[0] in {"-h", "--help"}:
        print(
            "Usage: python eng/scripts/tla.py {sany|tlc} [options] ...",
            file=sys.stderr if not args else sys.stdout,
        )
        return USAGE_ERROR if not args else 0

    subcommand, rest = args[0], args[1:]
    if subcommand == "sany":
        parser = argparse.ArgumentParser(prog="tla.py sany")
        parser.add_argument(
            "--driver",
            type=Path,
            default=DEFAULT_SANY_DRIVER,
            help="Path to TlaSanyCheck.java.",
        )
        if "--" in rest:
            sep = rest.index("--")
            options = parser.parse_args(rest[:sep])
            files = rest[sep + 1 :]
        else:
            options, files = parser.parse_known_args(rest)
            if any(item.startswith("-") for item in files):
                parser.error(f"unrecognized arguments: {' '.join(files)}")
        return _run_sany(options.driver, files)

    if subcommand == "tlc":
        return _run_tlc(rest)

    print(
        f"Unknown subcommand {subcommand!r}; expected 'sany' or 'tlc'.",
        file=sys.stderr,
    )
    return USAGE_ERROR


if __name__ == "__main__":
    raise SystemExit(main())
