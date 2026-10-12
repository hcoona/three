"""Selected Ruby qualification executes native tests and retains evidence."""

from __future__ import annotations

import importlib
import json
import subprocess
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "eng/scripts"))
try:
    gate = importlib.import_module("check_ruby_release_preparation")
finally:
    sys.path.pop(0)


@pytest.mark.skipif(sys.platform != "linux", reason="Linux qualification")
def test_native_ruby_release_preparation():
    """The actual declared native rows own product and original gem checks."""
    assert gate.main() == 0


def _report(path, *, names=gate.TEST_NAMES, outcome="Passed", counters=True):
    """Supply native observations without XML-escaping parameter identities."""
    document = ET.Element("TestRun", xmlns=gate.native.NAMESPACE["t"])
    summary = ET.SubElement(document, "ResultSummary")
    if counters:
        ET.SubElement(
            summary,
            "Counters",
            total=str(len(names)),
            executed=str(len(names) if outcome != "NotExecuted" else 0),
            passed=str(len(names) if outcome == "Passed" else 0),
        )
    results = ET.SubElement(document, "Results")
    for name in names:
        ET.SubElement(results, "UnitTestResult", testName=name, outcome=outcome)
    path.parent.mkdir(parents=True, exist_ok=True)
    ET.ElementTree(document).write(path, encoding="utf-8")


def test_qualification_runs_exact_native_rows_and_retains_evidence(
    tmp_path, monkeypatch
):
    """Finite locked Ruby/.NET tools and original evidence reach the carrier."""
    root, directory = tmp_path / "source", tmp_path / "result"
    calls = []
    monkeypatch.setattr(gate.sys, "platform", "linux")
    monkeypatch.setenv("MISE_DISABLE_TOOLS", "ruby,dotnet")

    def run(_root, _directory, label, *arguments, **options):
        calls.append((label, arguments, options))
        if label == "test":
            reports = directory / "tests"
            _report(reports / "preparation.trx")
            evidence = reports / "ruby-release-preparation-tests1-native/actual"
            evidence.mkdir(parents=True)
            (evidence / "preparation.json").write_bytes(b"original preparation")
            (evidence / "original.gem").write_bytes(b"original gem")
        return {"source": "a" * 40, "tree": "b" * 40}.get(label, "")

    monkeypatch.setattr(gate.capture, "run", run)
    gate.qualify(root, directory)
    assert [call[0] for call in calls] == ["tools", "source", "tree", "test"]
    assert calls[0][1] == ("mise", "install", "--locked", "ruby", "dotnet")
    assert "MISE_DISABLE_TOOLS" not in calls[0][2]["environment"]
    arguments = calls[-1][1]
    assert arguments[:4] == ("dotnet", "test", "--project", gate.PROJECT)
    assert "-property:RestoreLockedMode=true" in arguments
    assert arguments[arguments.index("--minimum-expected-tests") + 1] == "3"
    assert arguments[arguments.index("--filter") + 1] == (
        "FullyQualifiedName=WorkflowDelivery.Tests.Release."
        "RubyPreparationNativeTests." + gate.METHOD
    )
    assert arguments[arguments.index("--results-directory") + 1] == str(
        directory / "tests"
    )
    assert json.loads((directory / "subject.json").read_text()) == {
        "revision": "a" * 40,
        "tree": "b" * 40,
        "method": gate.METHOD,
    }
    assert (directory / "tests/preparation.trx").is_file()
    assert next(directory.rglob("preparation.json")).read_bytes() == (
        b"original preparation"
    )
    assert next(directory.rglob("original.gem")).read_bytes() == b"original gem"


@pytest.mark.parametrize(
    "change",
    [
        {"names": ()},
        {"names": gate.TEST_NAMES[:2]},
        {"names": (gate.TEST_NAMES[0],) * 3},
        {"names": (*gate.TEST_NAMES[:2], "AnotherScenario")},
        {"outcome": "NotExecuted"},
        {"outcome": "Failed"},
        {"counters": False},
    ],
)
def test_native_report_rejects_missing_skipped_failed_or_substituted_rows(
    tmp_path, change
):
    """Process success alone cannot qualify the actual required native rows."""
    report = tmp_path / "preparation.trx"
    _report(report, **change)
    with pytest.raises(
        ValueError, match=r"native Ruby preparation.*did not pass"
    ):
        gate.require_native_pass(report)


def test_missing_native_report_cannot_qualify(tmp_path):
    """A successful native process without its original report fails closed."""
    with pytest.raises(FileNotFoundError):
        gate.require_native_pass(tmp_path / "missing.trx")


@pytest.mark.parametrize("failed", ["tools", "test"])
def test_failed_native_operation_stops_without_retry(
    tmp_path, monkeypatch, failed
):
    """Native failures stop once and preserve any written failure evidence."""
    directory = tmp_path / "result"
    monkeypatch.setattr(gate.sys, "platform", "linux")
    calls = []

    def run(_root, _directory, label, *arguments, **_options):
        calls.append(label)
        if label == failed:
            if label == "test":
                _report(directory / "tests/preparation.trx", outcome="Failed")
            raise subprocess.CalledProcessError(2, arguments)
        return "a" * 40

    monkeypatch.setattr(gate.capture, "run", run)
    with pytest.raises(subprocess.CalledProcessError):
        gate.qualify(tmp_path, directory)
    assert calls == (
        ["tools"] if failed == "tools" else ["tools", "source", "tree", "test"]
    )
    assert (directory / "tests/preparation.trx").exists() is (failed == "test")


def test_non_linux_cannot_create_qualification(tmp_path, monkeypatch):
    """An unsupported host cannot create an empty success carrier."""
    monkeypatch.setattr(gate.sys, "platform", "win32")
    with pytest.raises(ValueError, match="requires Linux"):
        gate.qualify(tmp_path, tmp_path / "result")
    assert not list(tmp_path.iterdir())


@pytest.mark.parametrize("outcome", ["Failed", "NotExecuted"])
def test_matching_summary_cannot_hide_failed_or_skipped_row(tmp_path, outcome):
    """Passing summary counters cannot override an individual native row."""
    report = tmp_path / "preparation.trx"
    _report(report)
    document = ET.parse(report)  # noqa: S314 - Owned test fixture.
    document.getroot().findall(
        "t:Results/t:UnitTestResult", gate.native.NAMESPACE
    )[-1].set("outcome", outcome)
    document.write(report)
    with pytest.raises(ValueError, match="did not pass"):
        gate.require_native_pass(report)


def test_declared_target_retains_native_report_preparation_and_gem_in_ci(
    tmp_path,
):
    """The ordinary selected job retains both actual MTP output locations."""
    import tomllib  # noqa: PLC0415 - Local configuration boundary.

    import yaml  # noqa: PLC0415 - Existing root dev dependency.

    declaration = tomllib.loads((ROOT / "pyproject.toml").read_text())
    target = "tests/eng/test_ruby_release_preparation_gate.py"
    assert (
        declaration["tool"]["pytest"]["ini_options"]["testpaths"].count(target)
        == 1
    )
    workflow = yaml.safe_load((ROOT / ".github/workflows/ci.yml").read_text())
    job = workflow["jobs"]["python-tests"]
    assert job["runs-on"] == "ubuntu-latest"
    step = next(
        item
        for item in job["steps"]
        if item.get("with", {}).get("name") == "ruby-release-preparation"
    )
    assert step["if"] == (
        "always() && steps.plan.outputs.native_ruby_preparation == 'true'"
    )
    assert step["with"]["if-no-files-found"] == "error"
    paths = [
        tmp_path
        / "artifacts/ruby-release-preparation/actual/tests/preparation.trx",
        tmp_path
        / "tests/private/app/workflow-delivery/bin/Debug/net10.0"
        / "ruby-release-preparation-tests1-native/actual/preparation.json",
        tmp_path
        / "tests/private/app/workflow-delivery/bin/Debug/net10.0"
        / "ruby-release-preparation-tests1-native/actual/original.gem",
    ]
    for path in paths:
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(path.name.encode())
    retained = set()
    for native_pattern in step["with"]["path"].splitlines():
        pattern = native_pattern.removeprefix(
            "${{ steps.endpoints.outputs.candidate_directory }}/"
        ).rstrip("/")
        for directory in tmp_path.glob(pattern):
            retained.update(directory.rglob("*"))
    assert set(paths) <= retained
    adapters = next(
        item
        for item in job["steps"]
        if item["name"] == "Setup selected MISE adapters"
    )
    assert (
        "steps.plan.outputs.native_ruby_preparation == 'true'" in adapters["if"]
    )
    assert (
        "native_ruby_preparation == 'true' && 'ruby dotnet'"
        in (adapters["with"]["install_args"])
    )
