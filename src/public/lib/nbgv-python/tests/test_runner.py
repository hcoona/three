"""Tests for the CLI runner."""

from __future__ import annotations

import json
import sys
from typing import TYPE_CHECKING

import pytest
from nbgv_python.errors import NbgvCommandError
from nbgv_python.runner import NbgvRunner

if TYPE_CHECKING:
    from pathlib import Path


def _write_stub(tmp_path: Path) -> Path:
    """Create a Python script that mimics the `nbgv` CLI."""
    script = tmp_path / "nbgv_stub.py"
    payload = {
        "SimpleVersion": "1.2.3",
        "GitCommitId": "abcdef1234567890",
    }
    script.write_text(
        "import json\n"
        "import sys\n"
        "from pathlib import Path\n"
        "\n"
        "if sys.argv[1:] and sys.argv[1] == 'get-version':\n"
        "    args = sys.argv[2:]\n"
        "    if args[:2] != ['--format', 'json']:\n"
        "        sys.exit(2)\n"
        "    args = args[2:]\n"
        "    if args and args[0] == '--project':\n"
        "        args = args[2:]\n"
        "    if args:\n"
        "        sys.exit(3)\n"
        "    json.dump(" + json.dumps(payload) + ", sys.stdout)\n"
        "elif sys.argv[1:] and sys.argv[1] == 'touch':\n"
        "    target = Path(sys.argv[2])\n"
        "    target.write_text('ok', encoding='utf-8')\n"
        "    sys.exit(0)\n"
        "elif sys.argv[1:] and sys.argv[1] == 'fail':\n"
        "    sys.stderr.write('boom\\n')\n"
        "    sys.exit(5)\n"
        "else:\n"
        "    sys.exit(1)\n"
    )
    return script


@pytest.fixture(name="runner")
def runner_fixture(tmp_path: Path) -> NbgvRunner:
    """Return an `NbgvRunner` wired to the stub CLI."""
    script = _write_stub(tmp_path)
    return NbgvRunner(command=[sys.executable, str(script)])


def test_get_version_returns_git_version_instance(runner: NbgvRunner) -> None:
    """Ensure `get_version()` parses JSON into `GitVersion`."""
    version = runner.get_version()
    assert version["simple_version"] == "1.2.3"
    assert version["git_commit_id"].startswith("abcdef")


def test_forward_propagates_passthrough_commands(
    runner: NbgvRunner, tmp_path: Path
) -> None:
    """Ensure `forward()` executes commands without capturing output."""
    target_file = tmp_path / "marker.txt"
    # Pass the file path relative to the working directory to verify cwd support
    runner.forward(["touch", str(target_file)])
    assert target_file.read_text(encoding="utf-8") == "ok"


def test_forward_raises_when_cli_fails(runner: NbgvRunner) -> None:
    """Raise `NbgvCommandError` when the CLI exits with a failure code."""
    with pytest.raises(NbgvCommandError) as exc:
        runner.forward(["fail"])
    assert exc.value.returncode == 5  # noqa: PLR2004
