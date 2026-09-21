"""Contracts for logical commit 11 legacy Buddy retirement."""

from __future__ import annotations

import base64
import fnmatch
import hashlib
import os
import re
import subprocess
from pathlib import Path
from typing import Any, cast

import pytest
import yaml

REPO_ROOT = Path(__file__).resolve().parents[6]
WORKFLOWS = REPO_ROOT / ".github/workflows"
LEGACY_ENTRY_PATHS = (
    ".github/workflows/buddy.yml",
    ".github/workflows/release-buddy.yml",
)
FORBIDDEN_COMPATIBILITY_BASENAMES = frozenset(
    {"buddy.yml", "release-buddy.yml", "legacy-buddy.yml"}
)
ARCHIVED_LEGACY_BUDDY_DOCS = (
    ".github/workflows/REFACTOR_PLAN.md",
    ".github/workflows/docs/DESIGN.prompt.md",
    ".github/workflows/docs/DESIGN.v2.md",
    ".github/workflows/docs/MEMORY.md",
)
OBSOLETE_PRE_V3_PATHS = (
    ".github/actionlint.yaml",
    ".github/workflows/release-build-dotnet.yml",
    "eng/release",
    "eng/scripts/verify_python_distribution_exactness.py",
    "eng/scripts/workflow_release_acceptance_gate.py",
    "eng/scripts/workflow_release_control.py",
    "src/public/lib/hcoona-release-smoke",
    "src/public/lib/hcoona-release-smoke-dotnet-executable",
    "src/public/lib/hcoona-release-smoke-github-packages/three.release.yml",
    "src/public/lib/hcoona-release-smoke-github-packages/three.quality.yml",
    "src/public/lib/hcoona-release-smoke-github-release",
    "src/public/lib/hcoona-release-smoke-inno",
    "src/public/lib/hcoona-release-smoke-npm-dual",
    "src/public/lib/hcoona-release-smoke-nuget",
    "src/public/lib/hcoona-release-smoke-pypi",
    "src/public/lib/hcoona-release-smoke-rubygems",
    "src/public/lib/hcoona-release-smoke-wxt",
    "src/public/lib/three-workflow-release-authoring",
    "src/public/lib/three-workflow-release-build",
    "src/public/lib/three-workflow-release-contracts",
    "src/public/lib/three-workflow-release-metadata",
    "src/public/lib/three-workflow-release-planner",
    "src/public/lib/three-workflow-release-proof",
    "src/public/lib/three-workflow-release-publish",
    "tests/fixtures/workflow-release-acceptance-matrix.json",
    "tests/fixtures/workflow-release-ci-validation-acceptance-matrix.json",
    "tests/test_workflow_release_control.py",
)


def _workflow_documents(root: Path) -> dict[str, dict[str, Any]]:
    documents: dict[str, dict[str, Any]] = {}
    for path in sorted(root.glob("*.y*ml")):
        document = yaml.safe_load(path.read_text(encoding="utf-8"))
        assert isinstance(document, dict), path.name
        documents[path.name] = document
    return documents


def _triggers(document: dict[str, Any]) -> dict[str, Any]:
    value = document.get("on")
    if value is None:
        value = cast("dict[object, Any]", document).get(True, {})
    return value if isinstance(value, dict) else {}


def _local_workflow_calls(document: dict[str, Any]) -> tuple[str, ...]:
    jobs = document.get("jobs", {})
    assert isinstance(jobs, dict)
    calls = []
    for job in jobs.values():
        if not isinstance(job, dict):
            continue
        uses = job.get("uses")
        if isinstance(uses, str) and uses.startswith("./.github/workflows/"):
            calls.append(Path(uses).name)
    return tuple(calls)


def _is_legacy_buddy_worker(document: dict[str, Any]) -> bool:
    for job in document.get("jobs", {}).values():
        if not isinstance(job, dict):
            continue
        uses = job.get("uses")
        inputs = job.get("with", {})
        if (
            isinstance(uses, str)
            and Path(uses).name
            in {"release-buddy.yml", "release-orchestrate.yml"}
            and isinstance(inputs, dict)
            and inputs.get("channel", "buddy") == "buddy"
        ):
            return True
    return False


def _legacy_buddy_routes(root: Path) -> tuple[tuple[str, ...], ...]:
    documents = _workflow_documents(root)
    routes: set[tuple[str, ...]] = set()
    for name, document in documents.items():
        if name in FORBIDDEN_COMPATIBILITY_BASENAMES:
            routes.add((name,))
        if "workflow_dispatch" not in _triggers(document):
            continue
        pending: list[tuple[str, tuple[str, ...]]] = [(name, (name,))]
        visited: set[str] = set()
        while pending:
            current, route = pending.pop()
            if current in visited or current not in documents:
                continue
            visited.add(current)
            current_document = documents[current]
            if _is_legacy_buddy_worker(current_document):
                routes.add(route)
            pending.extend(
                (called, (*route, called))
                for called in _local_workflow_calls(current_document)
            )
    return tuple(sorted(routes))


def _write_workflow(root: Path, name: str, content: str) -> None:
    path = root / name
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(content, encoding="utf-8")


def _v3_hk_globs() -> tuple[str, ...]:
    content = (REPO_ROOT / "hk.pkl").read_text(encoding="utf-8")
    block = content.split(
        "local workflow_delivery_v3_files =",
        1,
    )[1].split('  ["v3-control-pytest"] {', 1)[0]
    return tuple(re.findall(r'"([^"]+)"', block))


def _hk_selected_paths(
    changes: tuple[tuple[str, str, str | None], ...],
) -> tuple[str, ...]:
    globs = _v3_hk_globs()
    paths = {
        path
        for _kind, new_path, old_path in changes
        for path in (old_path, new_path)
        if path is not None
        and any(fnmatch.fnmatchcase(path, pattern) for pattern in globs)
    }
    return tuple(sorted(paths))


def _codeowners_rules(content: str) -> tuple[tuple[str, tuple[str, ...]], ...]:
    rules = []
    for raw_line in content.splitlines():
        fields = raw_line.split("#", 1)[0].split()
        if fields:
            rules.append((fields[0], tuple(fields[1:])))
    return tuple(rules)


def _codeowners_pattern_matches(pattern: str, path: str) -> bool:
    normalized = pattern.removeprefix("/")
    expression = re.escape(normalized)
    expression = expression.replace(r"\*\*/", "(?:.*/)?")
    expression = expression.replace(r"\*\*", ".*")
    expression = expression.replace(r"\*", "[^/]*")
    expression = expression.replace(r"\?", "[^/]")
    return re.fullmatch(expression, path) is not None


def _final_owners(
    rules: tuple[tuple[str, tuple[str, ...]], ...],
    path: str,
) -> tuple[str, ...]:
    owners: tuple[str, ...] = ()
    for pattern, candidate in rules:
        if _codeowners_pattern_matches(pattern, path):
            owners = candidate
    return owners


def test_legacy_buddy_entry_files_are_exactly_retired() -> None:
    """Require legacy entries and the compatibility alias to be absent."""
    assert all(not (REPO_ROOT / path).exists() for path in LEGACY_ENTRY_PATHS)
    assert not (WORKFLOWS / "legacy-buddy.yml").exists()
    assert (WORKFLOWS / "official.yml").is_file()
    assert (WORKFLOWS / "ci.yml").is_file()


@pytest.mark.parametrize(
    ("documents", "expected"),
    [
        (
            {
                "compatibility.yml": """
on: {workflow_dispatch: {}}
jobs:
  publish:
    uses: ./.github/workflows/release-orchestrate.yml
    with: {channel: buddy}
""",
                "release-orchestrate.yml": (
                    "on: {workflow_call: {}}\njobs: {}\n"
                ),
            },
            (("compatibility.yml",),),
        ),
        (
            {
                "relay.yml": """
on: {workflow_dispatch: {}}
jobs:
  delegate:
    uses: ./.github/workflows/compat-worker.yml
""",
                "compat-worker.yml": """
on: {workflow_call: {}}
jobs:
  publish:
    uses: ./.github/workflows/release-orchestrate.yml
    with: {channel: buddy}
""",
                "release-orchestrate.yml": (
                    "on: {workflow_call: {}}\njobs: {}\n"
                ),
            },
            (("relay.yml", "compat-worker.yml"),),
        ),
        (
            {"legacy-buddy.yml": "on: {workflow_dispatch: {}}\njobs: {}\n"},
            (("legacy-buddy.yml",),),
        ),
    ],
)
def test_renamed_and_indirect_compatibility_routes_are_detected(
    tmp_path: Path,
    documents: dict[str, str],
    expected: tuple[tuple[str, ...], ...],
) -> None:
    """Detect direct, indirect, and renamed compatibility routes."""
    for filename, content in documents.items():
        _write_workflow(tmp_path, filename, content)

    assert _legacy_buddy_routes(tmp_path) == expected


def test_release_orchestrator_rejects_buddy_before_v1_policy() -> None:
    """Fail before unchanged v1 policy logic can admit a Buddy channel."""
    orchestrator = _workflow_documents(WORKFLOWS)["release-orchestrate.yml"]
    steps = orchestrator["jobs"]["policy"]["steps"]
    validation = next(
        step for step in steps if step["name"] == "Validate inputs"
    )
    run = validation["run"]

    assert "normalized_channel" in run
    assert '"${normalized_channel,,}" == "buddy"' in run
    assert "has no compatibility route" in run
    assert run.index("has no compatibility route") < run.index(
        "release_orchestrate_policy_validate_inputs.sh"
    )


@pytest.mark.parametrize(
    ("script_name", "extra_environment"),
    [
        ("publish_node_gpr_idempotent.sh", {"OWNER": "hcoona"}),
        ("publish_node_npmjs_idempotent.sh", {}),
    ],
)
def test_node_publishers_hash_the_selected_tarball(
    tmp_path: Path,
    script_name: str,
    extra_environment: dict[str, str],
) -> None:
    """Pass the exact tarball path to Node before querying the registry."""
    tarball = tmp_path / "package with spaces.tgz"
    content = b"reviewed package content"
    tarball.write_bytes(content)
    expected_integrity = "sha512-" + base64.b64encode(
        hashlib.sha512(content).digest()
    ).decode("ascii")

    bin_directory = tmp_path / "bin"
    bin_directory.mkdir()
    publish_marker = tmp_path / "publish-called"
    npm = bin_directory / "npm"
    npm.write_text(
        """#!/usr/bin/env bash
set -Eeuo pipefail
if [[ "${1:-}" == "view" ]]; then
  printf '%s\\n' "${REMOTE_INTEGRITY}"
  exit 0
fi
: >"${PUBLISH_MARKER}"
exit 99
""",
        encoding="utf-8",
    )
    npm.chmod(0o755)

    environment = {
        **os.environ,
        "PATH": f"{bin_directory}{os.pathsep}{os.environ['PATH']}",
        "PROJECT": "example",
        "VERSION": "1.2.3",
        "DIST_TAG": "review",
        "TARBALL": str(tarball),
        "RUNNER_TEMP": str(tmp_path),
        "REMOTE_INTEGRITY": expected_integrity,
        "PUBLISH_MARKER": str(publish_marker),
        **extra_environment,
    }
    result = subprocess.run(  # noqa: S603
        [
            "/usr/bin/bash",
            str(REPO_ROOT / "eng/scripts" / script_name),
        ],
        check=False,
        capture_output=True,
        text=True,
        env=environment,
    )

    assert result.returncode == 0, result.stderr
    assert "integrity match" in result.stdout
    assert not publish_marker.exists()


def test_release_orchestrate_caller_completeness_is_official_only(
    tmp_path: Path,
) -> None:
    """Validate the helper without requiring either retired Buddy caller."""
    workflows = tmp_path / ".github/workflows"
    workflows.mkdir(parents=True)
    _write_workflow(
        workflows,
        "release-orchestrate.yml",
        """
on:
  workflow_call:
    inputs:
      publish_npm:
        required: true
        type: boolean
jobs: {}
""",
    )
    _write_workflow(
        workflows,
        "official.yml",
        """
on: {workflow_dispatch: {}}
jobs:
  orchestrate:
    uses: ./.github/workflows/release-orchestrate.yml
    with:
      publish_npm: true
""",
    )

    result = subprocess.run(  # noqa: S603
        [
            "/usr/bin/bash",
            str(
                REPO_ROOT
                / "eng/scripts/release_orchestrate_lint_caller_completeness.sh"
            ),
        ],
        cwd=tmp_path,
        check=False,
        capture_output=True,
        text=True,
    )

    assert result.returncode == 0, result.stderr
    assert "official.yml" in result.stdout
    assert "buddy.yml" not in result.stdout


def test_v3_shadow_and_buddy_workflows_remain_dedicated() -> None:
    """Keep v3 shadow and Buddy workflows outside the restored v1 entries."""
    shadow = WORKFLOWS / "workflow-delivery-v3-ci.yml"
    assert shadow.is_file()
    assert shadow.read_bytes() != (WORKFLOWS / "ci.yml").read_bytes()

    buddy = WORKFLOWS / "workflow-delivery-v3-buddy-smoke.yml"
    document = yaml.safe_load(buddy.read_text(encoding="utf-8"))
    assert "workflow_dispatch" in _triggers(document)
    assert _legacy_buddy_routes(WORKFLOWS) == ()


def test_pre_v3_control_plane_and_legacy_descriptors_are_absent() -> None:
    """Keep inherited projects, tests, and descriptors out of the tree."""
    assert all(
        not (REPO_ROOT / relative_path).exists()
        for relative_path in OBSOLETE_PRE_V3_PATHS
    )
    assert not tuple((REPO_ROOT / "src").glob("**/three.release.yml"))


def test_pre_v3_design_docs_cannot_reactivate_legacy_buddy_routes() -> None:
    """Mark restored pre-v3 guidance as historical and non-authoritative."""
    for relative_path in ARCHIVED_LEGACY_BUDDY_DOCS:
        notice = (REPO_ROOT / relative_path).read_text(encoding="utf-8")[:700]
        assert "**Archived and superseded:**" in notice, relative_path
        assert (
            "legacy `buddy.yml` and `release-buddy.yml` routes are\n> retired"
            in notice
        ), relative_path
        assert "Do not use this document to recreate either route" in notice, (
            relative_path
        )


@pytest.mark.parametrize(
    ("kind", "new_path", "old_path"),
    [
        ("delete", ".github/workflows/buddy.yml", None),
        ("delete", ".github/workflows/release-buddy.yml", None),
        (
            "rename",
            ".github/workflows/compatibility.yml",
            ".github/workflows/buddy.yml",
        ),
        ("add", ".github/workflows/new-buddy-compatibility.yml", None),
    ],
)
def test_root_hk_selects_buddy_retirement_and_compatibility_changes(
    kind: str,
    new_path: str,
    old_path: str | None,
) -> None:
    """Trigger v3 validation for deletions and compatibility-route attempts."""
    selected = _hk_selected_paths(((kind, new_path, old_path),))

    assert new_path in selected
    if old_path is not None:
        assert old_path in selected


def test_codeowners_covers_deleted_and_future_buddy_routes() -> None:
    """Protect compatibility paths with final-match ownership."""
    rules = _codeowners_rules(
        (REPO_ROOT / ".github/CODEOWNERS").read_text(encoding="utf-8")
    )
    paths = {
        *LEGACY_ENTRY_PATHS,
        ".github/workflows/legacy-buddy.yml",
        ".github/workflows/compatibility.yml",
        ".github/workflows/new-buddy-compatibility.yml",
    }

    assert {
        path: _final_owners(rules, path) for path in paths
    } == dict.fromkeys(paths, ("@hcoona",))


def test_temporary_acceptance_workflows_are_retired() -> None:
    """Retire every temporary destination-acceptance workflow source."""
    temporary_workflows = tuple(
        sorted(
            (
                *WORKFLOWS.glob(
                    "workflow-delivery-v3-buddy-smoke-acceptance*.yml"
                ),
                *WORKFLOWS.glob(
                    "workflow-delivery-v3-buddy-smoke-acceptance*.yaml"
                ),
            )
        )
    )

    assert temporary_workflows == ()
