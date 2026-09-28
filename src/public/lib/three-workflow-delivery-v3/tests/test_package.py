"""Launch contracts for Workflow Delivery v3's installed workspace tools."""

from __future__ import annotations

import os
import subprocess
import sys
import sysconfig
from pathlib import Path


def test_installed_workspace_entry_points_run(tmp_path: Path) -> None:
    """Launch both installed entry points without source-path overrides."""
    scripts = Path(sysconfig.get_path("scripts"))
    suffix = ".exe" if os.name == "nt" else ""
    console = scripts / f"three-workflow-delivery-v3{suffix}"
    environment = os.environ.copy()
    for name in ("PYTHONHOME", "PYTHONPATH"):
        environment.pop(name, None)

    for command in (
        (str(console), "--help"),
        (
            sys.executable,
            "-I",
            "-m",
            "three_workflow_delivery_v3.acceptance",
            "probe",
            "--help",
        ),
    ):
        result = subprocess.run(  # noqa: S603
            command,
            cwd=tmp_path,
            env=environment,
            check=False,
            capture_output=True,
            text=True,
            timeout=15,
        )
        assert result.returncode == 0, (
            f"{command!r}\nstdout:\n{result.stdout}\nstderr:\n{result.stderr}"
        )
        assert result.stdout.startswith("usage:")
