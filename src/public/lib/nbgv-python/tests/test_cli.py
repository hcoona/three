"""Tests for the CLI entry point."""

from __future__ import annotations

import logging
import sys
from typing import TYPE_CHECKING

import pytest
from nbgv_python import cli
from nbgv_python.command import ENV_COMMAND_OVERRIDE
from nbgv_python.errors import NbgvCommandError

if TYPE_CHECKING:
    from pathlib import Path


EXIT_CODE_OK = 0
EXIT_CODE_COMMAND_FAILED = 3
MOCK_EXIT_CODE_COMMAND_ERROR = 2


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


@pytest.mark.usefixtures("stub_env")
def test_cli_main_returns_zero_on_success(tmp_path: Path) -> None:
    """CLI should forward commands and return the exit code."""
    target = tmp_path / "marker.txt"
    exit_code = cli.main(["touch", str(target)])
    assert exit_code == EXIT_CODE_OK
    assert target.read_text(encoding="utf-8") == "ok"


@pytest.mark.usefixtures("stub_env")
def test_cli_returns_failure_code() -> None:
    """CLI should propagate the underlying exit code on failure."""
    exit_code = cli.main(["fail"])
    assert exit_code == EXIT_CODE_COMMAND_FAILED


@pytest.mark.usefixtures("stub_env")
def test_cli_logs_command_error_when_forward_fails(
    caplog: pytest.LogCaptureFixture,
) -> None:
    """CLI should log a helpful message when the underlying command fails."""
    caplog.set_level(logging.ERROR)
    exit_code = cli.main(["fail"])

    assert exit_code == EXIT_CODE_COMMAND_FAILED
    assert "nbgv command failed" in caplog.text
    assert "exit code=3" in caplog.text


@pytest.mark.usefixtures("stub_env")
def test_cli_logs_stderr_when_available(
    monkeypatch: pytest.MonkeyPatch,
    caplog: pytest.LogCaptureFixture,
) -> None:
    """CLI should include stderr details when available on NbgvCommandError."""

    def _raise_command_error(
        _self: object, _args: object, *, _cwd: object = None
    ) -> int:
        raise NbgvCommandError(
            ("nbgv", "fail"),
            MOCK_EXIT_CODE_COMMAND_ERROR,
            None,
            "boom\n",
        )

    monkeypatch.setattr(
        "nbgv_python.cli.NbgvRunner.forward", _raise_command_error
    )
    caplog.set_level(logging.ERROR)

    exit_code = cli.main(["fail"])
    assert exit_code == MOCK_EXIT_CODE_COMMAND_ERROR
    assert "boom" in caplog.text


def test_cli_handles_missing_command(monkeypatch: pytest.MonkeyPatch) -> None:
    """When nbgv cannot be found, CLI should emit a dedicated code."""
    monkeypatch.delenv(ENV_COMMAND_OVERRIDE, raising=False)
    monkeypatch.setenv("PATH", "")
    monkeypatch.setattr("nbgv_python.command.shutil.which", lambda _: None)
    exit_code = cli.main([])
    assert exit_code == cli.EXIT_CODE_NBGV_NOT_FOUND


def test_cli_logs_missing_command_message(
    monkeypatch: pytest.MonkeyPatch,
    caplog: pytest.LogCaptureFixture,
) -> None:
    """CLI should log a user-facing message when nbgv cannot be discovered."""
    monkeypatch.delenv(ENV_COMMAND_OVERRIDE, raising=False)
    monkeypatch.setenv("PATH", "")
    monkeypatch.setattr("nbgv_python.command.shutil.which", lambda _: None)
    caplog.set_level(logging.ERROR)

    exit_code = cli.main([])
    assert exit_code == cli.EXIT_CODE_NBGV_NOT_FOUND
    assert "Unable to locate the 'nbgv' CLI" in caplog.text
