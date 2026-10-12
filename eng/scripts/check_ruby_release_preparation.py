"""Run the declared native Ruby Release preparation scenarios on Linux."""

from __future__ import annotations

import os
import sys
import uuid
from pathlib import Path

import check_python_release_preparation as native
import run_node_ci_group as capture

METHOD = "NativePreparationQualifiesCommittedOriginalAndInstalledOrigin"
# Exact native DataRow identities, verified against original MTP TRX output.
TEST_NAMES = (
    METHOD + ' ("buddy","github-packages-ruby",'
    '"refs/heads/preparation-development",False)',
    METHOD + ' ("official","rubygems","refs/heads/main",False)',
    METHOD + ' ("official","rubygems","refs/tags/preparation-preview",True)',
)
PROJECT = native.PROJECT


def require_native_pass(report: Path) -> None:
    """Require every actual Ruby scenario, including branch and dry run."""
    native.require_native_pass(report, test_names=TEST_NAMES, ecosystem="Ruby")


def qualify(root: Path, directory: Path) -> None:
    """Run native tests and preserve their original evidence."""
    if sys.platform != "linux":
        message = "Native Ruby preparation qualification requires Linux"
        raise ValueError(message)
    directory.mkdir(parents=True, exist_ok=False)
    environment = dict(os.environ)
    environment.pop("MISE_DISABLE_TOOLS", None)
    capture.run(
        root,
        directory,
        "tools",
        "mise",
        "install",
        "--locked",
        "ruby",
        "dotnet",
        timeout=600,
        environment=environment,
    )
    revision = capture.run(
        root, directory, "source", "git", "rev-parse", "HEAD", timeout=30
    )
    tree = capture.run(
        root, directory, "tree", "git", "rev-parse", "HEAD^{tree}", timeout=30
    )
    capture.write_json(
        directory / "subject.json",
        {"revision": revision.strip(), "tree": tree.strip(), "method": METHOD},
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
        "FullyQualifiedName=WorkflowDelivery.Tests.Release."
        "RubyPreparationNativeTests." + METHOD,
        "--minimum-expected-tests",
        str(len(TEST_NAMES)),
        "--report-trx",
        "--report-trx-filename",
        "preparation.trx",
        "--results-directory",
        str(reports),
        timeout=1800,
    )
    require_native_pass(reports / "preparation.trx")


def main() -> int:
    """The declared pytest target admits the native gate to ordinary CI."""
    root = Path(__file__).resolve().parents[2]
    directory = root / "artifacts/ruby-release-preparation" / uuid.uuid4().hex
    qualify(root, directory)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
