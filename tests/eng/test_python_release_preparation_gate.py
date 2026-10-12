"""A selected hosted gate must execute its native preparation scenario."""

from __future__ import annotations

import importlib
import json
import subprocess
import sys
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "eng/scripts"))
try:
    gate = importlib.import_module("check_python_release_preparation")
finally:
    sys.path.pop(0)


@pytest.mark.skipif(sys.platform != "linux", reason="Linux qualification")
def test_native_python_release_preparation():
    """The selected project test owns the native scenario and evidence."""
    assert gate.main() == 0


def _report(  # noqa: PLR0913 - Controlled native report fields.
    path,
    *,
    total="1",
    executed="1",
    passed="1",
    outcome="Passed",
    name=gate.METHOD,
    count=1,
    counters=True,
):
    """Supply distinct native summary and test-result observations."""
    summary = (
        f'<Counters total="{total}" executed="{executed}" passed="{passed}" />'
        if counters
        else ""
    )
    results = (
        f'<UnitTestResult testName="{name}" outcome="{outcome}" />' * count
    )
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(
        '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">'
        f"<ResultSummary>{summary}</ResultSummary><Results>{results}</Results>"
        "</TestRun>"
    )


def test_qualification_retains_one_native_test_and_committed_tool_selection(
    tmp_path, monkeypatch
):
    """Use native locked tools and retain one successful selected result."""
    root, directory = tmp_path / "source", tmp_path / "result"
    calls = []
    monkeypatch.setattr(gate.sys, "platform", "linux")
    monkeypatch.setenv("MISE_DISABLE_TOOLS", "python,dotnet")

    def run(_root, _directory, label, *arguments, **options):
        calls.append((label, arguments, options))
        if label == "test":
            _report(directory / "tests/preparation.trx")
        return {"source": "a" * 40, "tree": "b" * 40}.get(label, "")

    monkeypatch.setattr(gate.capture, "run", run)
    gate.qualify(root, directory)
    assert [call[0] for call in calls] == ["tools", "source", "tree", "test"]
    assert calls[0][1] == ("mise", "install", "--locked", "python", "uv")
    assert "MISE_DISABLE_TOOLS" not in calls[0][2]["environment"]
    arguments = calls[-1][1]
    assert arguments[:4] == ("dotnet", "test", "--project", gate.PROJECT)
    assert "-property:RestoreLockedMode=true" in arguments
    assert arguments[arguments.index("--minimum-expected-tests") + 1] == "1"
    assert arguments[arguments.index("--filter") + 1].endswith(gate.METHOD)
    assert json.loads((directory / "subject.json").read_text()) == {
        "revision": "a" * 40,
        "tree": "b" * 40,
        "method": gate.METHOD,
    }
    assert (directory / "tests/preparation.trx").is_file()


@pytest.mark.parametrize(
    "change",
    [
        {"total": "0", "executed": "0", "passed": "0", "count": 0},
        {"executed": "0", "passed": "0", "outcome": "NotExecuted"},
        {"passed": "0", "outcome": "Failed"},
        {"name": "AnotherScenario"},
        {"count": 2},
        {"counters": False},
    ],
)
def test_native_report_rejects_zero_skip_or_another_scenario(tmp_path, change):
    """A process success cannot hide a missing or different required test."""
    path = tmp_path / "native.trx"
    _report(path, **change)
    with pytest.raises(ValueError, match="did not pass"):
        gate.require_native_pass(path)


@pytest.mark.parametrize("failed", ["tools", "test"])
def test_failed_native_operation_never_retries_or_qualifies(
    tmp_path, monkeypatch, failed
):
    """Native failures stop without replay or fabricated reports."""
    monkeypatch.setattr(gate.sys, "platform", "linux")
    calls = []

    def run(_root, _directory, label, *arguments, **_options):
        calls.append(label)
        if label == failed:
            raise subprocess.CalledProcessError(1, arguments)
        return "a" * 40

    monkeypatch.setattr(gate.capture, "run", run)
    with pytest.raises(subprocess.CalledProcessError):
        gate.qualify(tmp_path, tmp_path / "result")
    assert calls == (
        ["tools"] if failed == "tools" else ["tools", "source", "tree", "test"]
    )
    assert not (tmp_path / "result/tests/preparation.trx").exists()


def test_non_linux_cannot_create_qualification(tmp_path, monkeypatch):
    """An unsupported host cannot produce an empty success carrier."""
    monkeypatch.setattr(gate.sys, "platform", "win32")
    with pytest.raises(ValueError, match="requires Linux"):
        gate.qualify(tmp_path, tmp_path / "result")
    assert not list(tmp_path.iterdir())
