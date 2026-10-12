"""Run the selected native Python Release preparation scenario on Linux."""

from __future__ import annotations

import os
import sys
import uuid
import xml.etree.ElementTree as ET
from pathlib import Path

import run_node_ci_group as capture

METHOD = "NativeBranchPreparationBuildsCompleteOriginalPairAndGitFreeConsumer"
PROJECT = "tests/private/app/workflow-delivery/WorkflowDelivery.Tests.csproj"
NAMESPACE = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}


def require_native_pass(report: Path) -> None:
    """A successful process with zero tests or a skipped test cannot qualify."""
    document = ET.parse(report).getroot()  # noqa: S314 - Owned native test output.
    counters = document.find("t:ResultSummary/t:Counters", NAMESPACE)
    results = document.findall("t:Results/t:UnitTestResult", NAMESPACE)
    if (
        counters is None
        or any(
            counters.get(key) != "1" for key in ("total", "executed", "passed")
        )
        or len(results) != 1
        or results[0].get("testName") != METHOD
        or results[0].get("outcome") != "Passed"
    ):
        message = "The required native Python preparation test did not pass"
        raise ValueError(message)


def qualify(root: Path, directory: Path) -> None:
    """Use committed tools and retain original native reports."""
    if sys.platform != "linux":
        message = "Native Python preparation qualification requires Linux"
        raise ValueError(message)
    directory.mkdir(parents=True, exist_ok=False)
    environment = dict(os.environ)
    # Prepare only the existing locked Python/UV tools for this obligation.
    environment.pop("MISE_DISABLE_TOOLS", None)
    capture.run(
        root,
        directory,
        "tools",
        "mise",
        "install",
        "--locked",
        "python",
        "uv",
        timeout=300,
        environment=environment,
    )
    source = capture.run(
        root, directory, "source", "git", "rev-parse", "HEAD", timeout=30
    )
    tree = capture.run(
        root, directory, "tree", "git", "rev-parse", "HEAD^{tree}", timeout=30
    )
    capture.write_json(
        directory / "subject.json",
        {"revision": source.strip(), "tree": tree.strip(), "method": METHOD},
    )
    reports = directory / "tests"
    capture.run(
        root,
        directory,
        "test",
        "dotnet",
        "test",
        "--project",
        PROJECT,
        "-property:RestoreLockedMode=true",
        "--filter",
        "FullyQualifiedName=" + "WorkflowDelivery.Tests.Release."
        "PythonPreparationNativeTests." + METHOD,
        "--minimum-expected-tests",
        "1",
        "--report-trx",
        "--report-trx-filename",
        "preparation.trx",
        "--results-directory",
        str(reports),
        timeout=720,
    )
    require_native_pass(reports / "preparation.trx")


def main() -> int:
    """The selected pytest target owns invocation without another roster."""
    root = Path(__file__).resolve().parents[2]
    directory = root / "artifacts/python-release-preparation" / uuid.uuid4().hex
    qualify(root, directory)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
