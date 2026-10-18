"""Run the pinned pytest through the configuration query on a fixture."""

from __future__ import annotations

import json
import os
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[6]
QUERY = (
    ROOT
    / "src/private/app/workflow-delivery/Native/Python/pytest_configuration.py"
)


def _write(path: Path, text: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding="utf-8")


def _query(
    directory: Path, **environment: str
) -> subprocess.CompletedProcess[str]:
    child = {
        key: value
        for key, value in os.environ.items()
        if not key.startswith("PYTEST_")
    }
    child.update(environment)
    return subprocess.run(  # noqa: S603 -- the pinned interpreter and helper.
        [sys.executable, str(QUERY), str(directory)],
        cwd=directory,
        env=child,
        capture_output=True,
        text=True,
        check=False,
        timeout=120,
    )


def test_pinned_pytest_answers_configuration_before_any_collection(tmp_path):
    """The early hook returns the consumed fields without project code."""
    sentinel = tmp_path / "collected"
    _write(
        tmp_path / "pyproject.toml",
        "[tool.pytest.ini_options]\n"
        'testpaths = ["tests one", "src/member/tests"]\n'
        'addopts = "--import-mode=importlib"\n',
    )
    _write(
        tmp_path / "tests one/test_probe.py",
        "from pathlib import Path\n"
        f"Path({str(sentinel)!r}).write_text('collected')\n"
        "def test_probe():\n    pass\n",
    )
    _write(
        tmp_path / "src/member/tests/test_other.py",
        "def test_other():\n    pass\n",
    )
    _write(
        tmp_path / "conftest.py",
        "from pathlib import Path\n"
        f"Path({str(sentinel)!r}).write_text('conftest')\n",
    )

    # Ambient pytest configuration must not reach the query.
    result = _query(
        tmp_path,
        PYTEST_ADDOPTS="--definitely-not-an-option",
        PYTEST_PLUGINS="definitely_missing_plugin",
    )

    assert result.returncode == 0, result.stderr
    assert json.loads(result.stdout) == {
        "directory": str(tmp_path),
        "root": str(tmp_path),
        "configuration_file": str(tmp_path / "pyproject.toml"),
        "testpaths": ["tests one", "src/member/tests"],
        "addopts": ["--import-mode=importlib"],
        "pythonpath": [],
        "required_plugins": [],
    }
    assert not sentinel.exists()


def test_pinned_pytest_reports_python_path_for_rejection(tmp_path):
    """The reader rejects a pythonpath, so its native shape must show."""
    _write(
        tmp_path / "pytest.ini",
        "[pytest]\ntestpaths = t\npythonpath = src lib\n",
    )

    result = _query(tmp_path)

    assert result.returncode == 0, result.stderr
    facts = json.loads(result.stdout)
    assert facts["configuration_file"] == str(tmp_path / "pytest.ini")
    assert facts["testpaths"] == ["t"]
    assert facts["addopts"] == []
    assert facts["pythonpath"] == [str(tmp_path / "src"), str(tmp_path / "lib")]
    assert facts["required_plugins"] == []


def test_pinned_pytest_fails_the_query_for_a_missing_required_plugin(tmp_path):
    """Pytest validates required plugins first, so the query fails closed."""
    _write(
        tmp_path / "pytest.ini",
        "[pytest]\ntestpaths = t\n"
        "required_plugins = pytest-absent-probe>=1.0\n",
    )

    result = _query(tmp_path)

    assert result.returncode == 1
    assert result.stdout == ""
