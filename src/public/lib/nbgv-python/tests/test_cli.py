"""Tests for the CLI entry point."""

from __future__ import annotations

import sys
from typing import TYPE_CHECKING

import pytest
from nbgv_python import cli
from nbgv_python.command import ENV_COMMAND_OVERRIDE

if TYPE_CHECKING:
    from pathlib import Path


def _write_stub(tmp_path: Path) -> Path:
    """Create a stub CLI used by the CLI tests."""
    script = tmp_path / "nbgv_stub.py"
    script.write_text(
        "import json\n"
        "import sys\n"
        "from pathlib import Path\n"
        "\n"
        "if sys.argv[1:] == ['get-version', '--format', 'json']:\n"
        "    json.dump({'SimpleVersion': '1.0.0'}, sys.stdout)\n"
        "elif sys.argv[1:] and sys.argv[1] == 'touch':\n"
        "    Path(sys.argv[2]).write_text('ok', encoding='utf-8')\n"
        "    sys.exit(0)\n"
        "elif sys.argv[1:] and sys.argv[1] == 'fail':\n"
        "    sys.stderr.write('boom\\n')\n"
        "    sys.exit(3)\n"
        "else:\n"
        "    sys.exit(1)\n"
    )
    return script


@pytest.fixture(name="stub_env")
def stub_env_fixture(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    """Configure the environment so CLI calls use the stub."""
    script = _write_stub(tmp_path)
    command = f'"{sys.executable}" "{script}"'
    monkeypatch.setenv(ENV_COMMAND_OVERRIDE, command)


def test_cli_main_returns_zero_on_success(
    stub_env: None,  # noqa: ARG001
    tmp_path: Path,
) -> None:
    """CLI should forward commands and return the exit code."""
    target = tmp_path / "marker.txt"
    exit_code = cli.main(["touch", str(target)])
    assert exit_code == 0
    assert target.read_text(encoding="utf-8") == "ok"


def test_cli_returns_failure_code(stub_env: None) -> None:  # noqa: ARG001
    """CLI should propagate the underlying exit code on failure."""
    exit_code = cli.main(["fail"])
    assert exit_code == 3  # noqa: PLR2004


def test_cli_handles_missing_command(monkeypatch: pytest.MonkeyPatch) -> None:
    """When nbgv cannot be found, CLI should emit a dedicated code."""
    monkeypatch.delenv(ENV_COMMAND_OVERRIDE, raising=False)
    monkeypatch.setenv("PATH", "")
    monkeypatch.setattr("nbgv_python.command.shutil.which", lambda _: None)
    exit_code = cli.main([])
    assert exit_code == 127  # noqa: PLR2004
