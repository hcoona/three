"""Execute checked-in shell bodies at a bounded process boundary."""

from __future__ import annotations

import os
import re
import subprocess
import sys
from typing import TYPE_CHECKING, Any

if TYPE_CHECKING:
    from pathlib import Path


def executable(path: Path, body: str) -> None:
    """Install a controlled command using the current test interpreter."""
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(f"#!{sys.executable}\n{body}\n", encoding="utf-8")
    path.chmod(0o755)


def resolve(value: str, bindings: dict[str, str]) -> str:
    """Require explicit fixture values for every consumed Actions expression."""
    return re.sub(
        r"\$\{\{\s*(.*?)\s*\}\}",
        lambda match: bindings[match.group(1)],
        value,
    )


def run_step(  # noqa: PLR0913
    step: dict[str, Any],
    *,
    cwd: Path,
    env: dict[str, str],
    bindings: dict[str, str],
    workflow: dict[str, Any] | None = None,
    job: dict[str, Any] | None = None,
) -> subprocess.CompletedProcess[str]:
    """Launch a Bash body with effective Actions environment and flags."""
    process_env = {**os.environ, **env}
    shell = None
    directory = "."
    for scope in (workflow or {}, job or {}, step):
        process_env.update(
            {
                key: resolve(str(value), bindings)
                for key, value in scope.get("env", {}).items()
            }
        )
        defaults = scope.get("defaults", {}).get("run", {})
        shell = defaults.get("shell", shell)
        directory = defaults.get("working-directory", directory)
    shell = step.get("shell", shell)
    directory = step.get("working-directory", directory)
    assert shell in (None, "bash"), f"Unsupported shell: {shell}"
    flags = (
        ["-e"]
        if shell is None
        else ["--noprofile", "--norc", "-eo", "pipefail"]
    )
    return subprocess.run(  # noqa: S603
        ["bash", *flags, "-c", resolve(step["run"], bindings)],  # noqa: S607
        cwd=cwd / resolve(directory, bindings),
        env=process_env,
        capture_output=True,
        text=True,
        check=False,
        timeout=30,
    )
