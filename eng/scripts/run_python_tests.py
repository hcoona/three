"""Prepare the configured Python test consumers and run their complete suite."""

from __future__ import annotations

import shutil
import subprocess
import sys

from ci_scope import ROOT, select


def main() -> int:
    """Exact-sync test owners without installing unrelated workspace tools."""
    selection = select(ROOT, (), base="", full=True)
    uv = shutil.which("uv")
    if uv is None:
        message = "The configured UV executable is unavailable"
        raise FileNotFoundError(message)
    prepared = subprocess.run(
        [
            uv,
            "sync",
            "--frozen",
            *[
                argument
                for package in selection["python_packages"]
                for argument in ("--package", package)
            ],
        ],
        cwd=ROOT,
        check=False,
    )
    if prepared.returncode:
        return prepared.returncode
    return subprocess.run(
        [
            uv,
            "run",
            "--no-sync",
            "python",
            "-m",
            "pytest",
            *selection["python_roots"],
            *sys.argv[1:],
        ],
        cwd=ROOT,
        check=False,
    ).returncode


if __name__ == "__main__":
    raise SystemExit(main())
