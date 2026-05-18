# ruff: noqa: SLF001
"""Tests for workflow-release control-plane helper script."""

from __future__ import annotations

import argparse
import hashlib
import importlib.util
import io
import json
import shutil
import subprocess
import sys
import tarfile
from copy import deepcopy
from itertools import pairwise
from pathlib import Path
from typing import TYPE_CHECKING, cast

import pytest
import yaml
from three_workflow_release_authoring import validate_authoring
from three_workflow_release_build import execute_build
from three_workflow_release_contracts import (
    ArtifactNameInputs,
    CiValidationObservedReceiptInput,
    artifact_name,
    artifact_physical_name,
    ci_validation_plan_digest,
    ci_validation_writer_id,
    freeze_ci_validation_receipt,
    validate_ci_validation_aggregate,
    validate_ci_validation_receipt,
    validate_contract,
)
from three_workflow_release_planner import (
    CiValidationPlannerInputs,
    PlannerInputs,
    plan_ci_validation_from_repo,
    plan_release,
)
from three_workflow_release_proof import (
    ProofError,
    classify_immutable_observations,
)

if TYPE_CHECKING:
    from collections.abc import Callable, Mapping, Sequence

    from three_workflow_release_contracts import ReceiptOutcome

REPO_ROOT = Path(__file__).parents[1]
SCRIPT = REPO_ROOT / "eng/scripts/workflow_release_control.py"
FIXTURES = (
    REPO_ROOT
    / "src/public/lib/three-workflow-release-contracts/tests/fixtures/valid"
)
ACCEPTANCE_MATRIX = (
    REPO_ROOT / "tests/fixtures/workflow-release-acceptance-matrix.json"
)
CI_ACCEPTANCE_MATRIX = (
    REPO_ROOT
    / "tests/fixtures/workflow-release-ci-validation-acceptance-matrix.json"
)
LOW_LEVEL_DESIGN = (
    REPO_ROOT / "docs/wiki/analyses/workflow-release-low-level-design.md"
)
CI_LOW_LEVEL_DESIGN = REPO_ROOT / (
    "docs/wiki/analyses/"
    "workflow-release-ci-affected-validation-low-level-design.md"
)
ACCEPTANCE_GATE = REPO_ROOT / "eng/scripts/workflow_release_acceptance_gate.py"
SCRATCH = REPO_ROOT / ".pytest-workflow-release-control"
GIT = shutil.which("git")
SHA_B = "b" * 64
SHA_C = "c" * 64
SIGNER_WORKFLOW = "hcoona/three/.github/workflows/release-publish-node.yml"
FORBIDDEN_FAIL_CLOSED_OUTPUTS = {
    "release-plan.json",
    "execution-sets.json",
    "build-result.json",
    "tag-result.json",
    "publish-request.json",
    "publish-result.json",
    "skip-result.json",
    "immutable-proof.json",
    "github-release-asset-proof.json",
}
PRE_PLAN_FAIL_CLOSED_IDS = {
    "descriptor-discovery-and-invalid-descriptor-fail-closed",
    "target-catalog-validation-fails-closed",
    "unknown-requested-project-id-fails-closed",
    "package-registry-profile-coexistence-fail-closed-rule",
    "buddy-force-rejected-after-official-freeze",
    "external-oidc-topology-blocked",
    "external-target-disabled",
    "invalid-external-oidc-live-enable-allowlist-fails-closed",
    "entry-actor-authorization-fails-closed",
    "invalid-entry-input-rejection",
    "trusted-workflow-ref-gate",
}
FAIL_CLOSED_ARTIFACT_CONTRACTS = {
    row_id: {
        "allowed": {"planner-diagnostics.json", "release-report.json"},
        "absent": FORBIDDEN_FAIL_CLOSED_OUTPUTS,
    }
    for row_id in PRE_PLAN_FAIL_CLOSED_IDS
} | {
    "package-metadata-mismatch-fails-closed": {
        "allowed": {
            "release-plan.json",
            "execution-sets.json",
            "build-result.json",
            "publish-request.json",
            "release-report.json",
        },
        "absent": {
            "tag-result.json",
            "publish-result.json",
            "skip-result.json",
            "immutable-proof.json",
            "github-release-asset-proof.json",
        },
    },
    "immutable-partial-replay": {
        "allowed": {
            "release-plan.json",
            "execution-sets.json",
            "planner-diagnostics.json",
            "release-report.json",
        },
        "absent": {
            "build-result.json",
            "tag-result.json",
            "publish-request.json",
            "publish-result.json",
            "skip-result.json",
            "immutable-proof.json",
            "github-release-asset-proof.json",
        },
    },
    "external-trusted-publisher-misconfiguration-fails-closed": {
        "allowed": {
            "release-plan.json",
            "execution-sets.json",
            "build-result.json",
            "publish-request.json",
            "release-report.json",
        },
        "absent": {
            "tag-result.json",
            "publish-result.json",
            "skip-result.json",
            "immutable-proof.json",
            "github-release-asset-proof.json",
        },
    },
}
NO_SIDE_EFFECT_ARTIFACT_CONTRACTS = {
    "dry-run-with-default-no-build": {
        "allowed": {
            "release-plan.json",
            "execution-sets.json",
            "release-report.json",
        },
        "absent": {
            "build-result.json",
            "tag-result.json",
            "publish-request.json",
            "publish-result.json",
            "skip-result.json",
            "immutable-proof.json",
            "github-release-asset-proof.json",
        },
    },
    "zero-target-or-all-skip-no-side-effect-live-run": {
        "allowed": {
            "release-plan.json",
            "execution-sets.json",
            "skip-result.json",
            "release-report.json",
        },
        "absent": {
            "build-result.json",
            "tag-result.json",
            "publish-request.json",
            "publish-result.json",
            "immutable-proof.json",
            "github-release-asset-proof.json",
        },
    },
    "validation-build-receipts-are-not-immutable-proof": {
        "allowed": {
            "release-plan.json",
            "execution-sets.json",
            "build-result.json",
            "release-report.json",
        },
        "absent": {
            "tag-result.json",
            "publish-request.json",
            "publish-result.json",
            "skip-result.json",
            "immutable-proof.json",
            "github-release-asset-proof.json",
        },
    },
    "github-release-tag-atomicity": {
        "allowed": {
            "release-plan.json",
            "execution-sets.json",
            "build-result.json",
            "release-report.json",
        },
        "absent": {
            "tag-result.json",
            "publish-request.json",
            "publish-result.json",
            "skip-result.json",
            "immutable-proof.json",
            "github-release-asset-proof.json",
        },
    },
}

spec = importlib.util.spec_from_file_location(
    "workflow_release_control", SCRIPT
)
assert spec is not None
control = importlib.util.module_from_spec(spec)
assert spec.loader is not None
spec.loader.exec_module(control)

gate_spec = importlib.util.spec_from_file_location(
    "workflow_release_acceptance_gate", ACCEPTANCE_GATE
)
assert gate_spec is not None
acceptance_gate = importlib.util.module_from_spec(gate_spec)
assert gate_spec.loader is not None
gate_spec.loader.exec_module(acceptance_gate)


def _load(name: str) -> dict[str, object]:
    """Load one valid workflow-release contract fixture."""
    return json.loads((FIXTURES / name).read_text(encoding="utf-8"))


def _workflow(name: str) -> str:
    """Read one workflow file as text for structural assertions."""
    return (REPO_ROOT / ".github/workflows" / name).read_text(encoding="utf-8")


def _write_npm_tarball(path: Path, entries: dict[str, bytes]) -> None:
    """Write a minimal npm package tarball for build executor tests."""
    path.parent.mkdir(parents=True, exist_ok=True)
    with tarfile.open(path, "w:gz") as archive:
        for name, data in entries.items():
            info = tarfile.TarInfo(name)
            info.size = len(data)
            archive.addfile(info, io.BytesIO(data))


def _release_workflow_paths() -> list[Path]:
    """Return release workflow paths in deterministic order."""
    return sorted((REPO_ROOT / ".github/workflows").glob("release-*.yml"))


def _acceptance_matrix() -> dict[str, object]:
    """Load the workflow-release acceptance matrix fixture."""
    return json.loads(ACCEPTANCE_MATRIX.read_text(encoding="utf-8"))


def _ci_acceptance_matrix() -> dict[str, object]:
    """Load the CI affected-validation acceptance matrix fixture."""
    return json.loads(CI_ACCEPTANCE_MATRIX.read_text(encoding="utf-8"))


def _design_acceptance_scenarios() -> list[str]:
    """Extract Section 10 scenario names from the low-level design table."""
    lines = LOW_LEVEL_DESIGN.read_text(encoding="utf-8").splitlines()
    scenarios: list[str] = []
    in_table = False
    for line in lines:
        if line.startswith("| Scenario "):
            in_table = True
            continue
        if not in_table:
            continue
        if not line.startswith("| "):
            if scenarios:
                break
            continue
        if line.startswith("| ---"):
            continue
        scenarios.append(line.split("|", maxsplit=2)[1].strip())
    return scenarios


def _ci_design_acceptance_scenarios() -> list[str]:
    """Extract Section 17 CI acceptance scenario names from the LLD table."""
    lines = CI_LOW_LEVEL_DESIGN.read_text(encoding="utf-8").splitlines()
    scenarios: list[str] = []
    in_section = False
    in_table = False
    for line in lines:
        if line == "## 17. Acceptance Traceability":
            in_section = True
            continue
        if in_section and line.startswith("## 18. "):
            break
        if not in_section:
            continue
        if line.startswith("| Scenario "):
            in_table = True
            continue
        if not in_table:
            continue
        if not line.startswith("| "):
            if scenarios:
                break
            continue
        if line.startswith("| ---"):
            continue
        scenarios.append(line.split("|", maxsplit=2)[1].strip())
    return scenarios


def _all_test_nodeids() -> set[str]:
    """Return all pytest function nodeids in the repository."""
    nodeids: set[str] = set()
    for test_file in sorted(REPO_ROOT.rglob("test*.py")):
        if any(
            part
            in {
                ".git",
                ".pytest-workflow-release-control",
                ".three-workflow-release-planner",
                ".venv",
                "node_modules",
            }
            for part in test_file.parts
        ):
            continue
        relative = test_file.relative_to(REPO_ROOT).as_posix()
        text = test_file.read_text(encoding="utf-8")
        for line in text.splitlines():
            if line.startswith("def test_"):
                name = line.split("(", maxsplit=1)[0].removeprefix("def ")
                nodeids.add(f"{relative}::{name}")
    return nodeids


def _matrix_test_nodeids(matrix: dict[str, object]) -> set[str]:
    """Return pytest nodeids referenced by acceptance matrix evidence."""
    nodeids: set[str] = set()
    for row in matrix["rows"]:
        for references in row["evidence"].values():
            for reference in references:
                if reference["type"] == "test":
                    nodeids.add(reference["value"])
    return nodeids


def _confirmed_scope_descriptor_paths() -> list[str]:
    """Return current confirmed-scope descriptor files."""
    public = sorted(
        path.relative_to(REPO_ROOT).as_posix()
        for path in (REPO_ROOT / "src/public").rglob("three.release.yml")
    )
    private = [
        "src/private/app/qidian-novel-downloader/three.release.yml",
        "src/private/app/vscode-copilot-telegram-hook/three.release.yml",
    ]
    return sorted(public + private)


def _confirmed_scope_project_ids() -> list[str]:
    """Return project ids declared by confirmed-scope descriptors."""
    project_ids: list[str] = []
    for descriptor in _confirmed_scope_descriptor_paths():
        document = yaml.safe_load(
            (REPO_ROOT / descriptor).read_text(encoding="utf-8")
        )
        project_ids.append(document["project"]["id"])
    return sorted(project_ids)


def _dotnet_metadata_for_planner_input(
    metadata_input: dict[str, object],
) -> dict[str, object]:
    """Return deterministic .NET planner metadata for acceptance planning."""
    projects = metadata_input["projects"]
    assert isinstance(projects, dict)
    metadata_projects: dict[str, object] = {}
    for project_id, project in projects.items():
        assert isinstance(project, dict)
        entry = {
            "descriptor-path": project["descriptor-path"],
            "primary-manifest-path": project["primary-manifest-path"],
            "resolved-version": "1.2.3",
        }
        if project.get("requires-package-id") is True:
            entry["package-id"] = str(project_id).replace("-", ".").title()
        metadata_projects[str(project_id)] = entry
    return {
        "api-version": "three.release.dotnet-planner-metadata/v1alpha1",
        "kind": "dotnet-planner-metadata",
        "commit-sha": metadata_input["commit-sha"],
        "projects": metadata_projects,
    }


def _copy_authoring_repo(destination: Path) -> None:
    """Copy authoring candidate inputs into an isolated git worktree."""
    assert GIT is not None
    paths = subprocess.run(  # noqa: S603
        [
            GIT,
            "ls-files",
            "--cached",
            "--others",
            "--exclude-standard",
            "-z",
            "src/public",
            "src/private/app/qidian-novel-downloader",
            "src/private/app/vscode-copilot-telegram-hook",
            "eng/release",
        ],
        cwd=REPO_ROOT,
        check=True,
        stdout=subprocess.PIPE,
    ).stdout.decode("utf-8")
    for relative in (item for item in paths.split("\0") if item):
        source = REPO_ROOT / relative
        target = destination / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, target)
    subprocess.run(  # noqa: S603
        [GIT, "init", "--quiet"],
        cwd=destination,
        check=True,
    )
    subprocess.run(  # noqa: S603
        [GIT, "add", "src", "eng/release"],
        cwd=destination,
        check=True,
    )


def _assert_forbidden_outputs_absent(root: Path) -> None:
    """Assert fail-closed flows did not create downstream release outputs."""
    present = sorted(
        path.relative_to(root).as_posix()
        for path in root.rglob("*")
        if path.is_file() and path.name in FORBIDDEN_FAIL_CLOSED_OUTPUTS
    )
    assert present == []


def _diagnostic_codes(path: Path) -> list[str]:
    """Read diagnostic codes from a planner diagnostics document."""
    document = json.loads(path.read_text(encoding="utf-8"))
    validate_contract(document)
    return [diagnostic["code"] for diagnostic in document["diagnostics"]]


def _run_authoring_validate(
    repo_root: Path, diagnostics_out: Path
) -> subprocess.CompletedProcess[object]:
    """Run the authoring validator against an isolated repository."""
    return subprocess.run(  # noqa: S603
        [
            sys.executable,
            "-m",
            "three_workflow_release_authoring.cli",
            "validate",
            "--repo-root",
            str(repo_root),
            "--diagnostics-out",
            str(diagnostics_out),
        ],
        cwd=REPO_ROOT,
        check=False,
    )


def _validate_mutated_authoring_repo(
    scratch: Path,
    case_name: str,
    mutate: Callable[[Path], None],
    expected_code: str,
) -> None:
    """Mutate an isolated repo and assert authoring fails before outputs."""
    repo_root = scratch / f"{case_name}-repo"
    _copy_authoring_repo(repo_root)
    mutate(repo_root)
    diagnostics = scratch / case_name / "planner-diagnostics.json"
    diagnostics.parent.mkdir()

    result = _run_authoring_validate(repo_root, diagnostics)

    assert result.returncode == 1
    assert diagnostics.is_file()
    assert expected_code in _diagnostic_codes(diagnostics)
    _assert_forbidden_outputs_absent(diagnostics.parent)


def _mutate_descriptor_extra_field(repo_root: Path) -> None:
    """Add an unsupported descriptor field."""
    descriptor = repo_root / "src/public/lib/nbgv-python/three.release.yml"
    document = yaml.safe_load(descriptor.read_text(encoding="utf-8"))
    document["unexpected-field"] = "must fail closed"
    descriptor.write_text(
        yaml.safe_dump(document, sort_keys=False),
        encoding="utf-8",
    )


def _mutate_catalog_schema(repo_root: Path) -> None:
    """Break target catalog schema shape."""
    catalog = repo_root / "eng/release/target-instances.yml"
    document = yaml.safe_load(catalog.read_text(encoding="utf-8"))
    document["families"] = []
    catalog.write_text(
        yaml.safe_dump(document, sort_keys=False),
        encoding="utf-8",
    )


def _mutate_missing_catalog_ref(repo_root: Path) -> None:
    """Point a descriptor target to a missing catalog entry."""
    descriptor = (
        repo_root / "src/public/lib/hcoona-release-smoke-pypi/three.release.yml"
    )
    document = yaml.safe_load(descriptor.read_text(encoding="utf-8"))
    document["profiles"]["official"]["targets"][1]["uses"] = (
        "pypi/does-not-exist"
    )
    descriptor.write_text(
        yaml.safe_dump(document, sort_keys=False),
        encoding="utf-8",
    )


def _mutate_profile_coexistence_conflict(repo_root: Path) -> None:
    """Make buddy and official resolve to the same package-registry target."""
    descriptor = (
        repo_root / "src/public/lib/hcoona-release-smoke-pypi/three.release.yml"
    )
    document = yaml.safe_load(descriptor.read_text(encoding="utf-8"))
    document["profiles"]["buddy"]["targets"].append(
        {"uses": "pypi/pypi", "artifacts": ["wheel", "sdist"]}
    )
    descriptor.write_text(
        yaml.safe_dump(document, sort_keys=False),
        encoding="utf-8",
    )


def _external_oidc_plan_and_sets(
    topology: str = "external-oidc-entry-workflow",
) -> tuple[dict[str, object], dict[str, object]]:
    """Return a valid plan/execution-set pair with an active OIDC target."""
    plan = deepcopy(_load("release-plan.json"))
    execution_sets = deepcopy(_load("execution-sets.json"))
    plan["envelope"]["profile"] = "official"
    execution_sets["active-publish-node-ids"] = ["publish-node/nuget"]
    execution_sets["publish-intent-node-ids"] = ["publish-node/nuget"]
    execution_sets["selected-github-release-publish-node-ids"] = []
    execution_sets["active-github-release-publish-node-ids"] = []
    execution_sets["skip-satisfied-publish-node-ids"] = []
    for selector in execution_sets["active-publish-selectors"].values():
        selector.clear()
    execution_sets["active-publish-selectors"][topology] = [
        "publish-node/nuget"
    ]
    snapshot = deepcopy(
        plan["graph"]["target-instance-snapshots"]["nuget/github-packages"]
    )
    snapshot["catalog-ref"] = "pypi/pypi"
    snapshot["contract"]["id"] = "pypi-publish"
    snapshot["destination"] = {"host": "pypi.org"}
    snapshot["family"] = "pypi"
    snapshot["instance-id"] = "pypi"
    snapshot["capabilities"]["credential-posture"] = "oidc"
    snapshot["capabilities"]["name-uniqueness-scope"] = "package-name"
    snapshot["capabilities"]["profile-coexistence-rule"] = (
        "requires-distinct-name"
    )
    snapshot["capabilities"]["publish-topology"] = topology
    snapshot["contract"] = deepcopy(
        plan["graph"]["target-instance-snapshots"]["github-release/public"][
            "contract"
        ]
    )
    snapshot["contract"]["id"] = "pypi-publish"
    snapshot["contract"]["allowed-artifact-tuples"] = [
        {
            "role": "primary-package",
            "kind-family": "package",
            "concrete-kind": "wheel",
        },
        {
            "role": "primary-package",
            "kind-family": "package",
            "concrete-kind": "sdist",
        },
    ]
    snapshot["contract"]["aggregate-rules"] = {
        "min-artifact-count": 1,
        "max-artifact-count": 2,
        "cross-variant-policy": "forbid",
        "tuple-rules": [
            {
                "role": "primary-package",
                "kind-family": "package",
                "concrete-kind": "wheel",
                "min-count": 1,
                "max-count": 1,
            },
            {
                "role": "primary-package",
                "kind-family": "package",
                "concrete-kind": "sdist",
                "min-count": 0,
                "max-count": 1,
            },
        ],
    }
    plan["graph"]["target-instance-snapshots"]["pypi/pypi"] = snapshot
    plan["graph"]["artifacts"]["artifact/package"]["concrete-kind"] = "wheel"
    node = plan["graph"]["publish-nodes"]["publish-node/nuget"]
    node["publish-disposition"] = "publish"
    node["publish-mode"] = "create-only"
    node["target-instance-snapshot-id"] = "pypi/pypi"
    return plan, execution_sets


def _pypi_only_observation_plan() -> dict[str, object]:
    """Return a plan containing only the PyPI publish node for observation."""
    plan, _ = _external_oidc_plan_and_sets()
    pypi_node = plan["graph"]["publish-nodes"]["publish-node/nuget"]
    pypi_snapshot = plan["graph"]["target-instance-snapshots"]["pypi/pypi"]
    plan["graph"]["publish-nodes"] = {"publish-node/nuget": pypi_node}
    plan["graph"]["target-instance-snapshots"] = {"pypi/pypi": pypi_snapshot}
    return plan


def _unsupported_rubygems_oidc_plan_and_sets() -> tuple[
    dict[str, object], dict[str, object]
]:
    """Return a valid active external OIDC plan for an unsupported registry."""
    plan, execution_sets = _external_oidc_plan_and_sets()
    snapshot = plan["graph"]["target-instance-snapshots"].pop("pypi/pypi")
    snapshot["catalog-ref"] = "rubygems/rubygems-org"
    snapshot["contract"]["id"] = "rubygems-publish"
    snapshot["destination"] = {"host": "rubygems.example"}
    snapshot["family"] = "rubygems"
    snapshot["instance-id"] = "rubygems-org"
    snapshot["capabilities"]["publish-topology"] = (
        "external-oidc-reusable-workflow"
    )
    snapshot["contract"]["allowed-artifact-tuples"] = [
        {
            "role": "primary-package",
            "kind-family": "package",
            "concrete-kind": "rubygem",
        }
    ]
    snapshot["contract"]["aggregate-rules"] = {
        "min-artifact-count": 1,
        "max-artifact-count": 1,
        "cross-variant-policy": "forbid",
        "tuple-rules": [
            {
                "role": "primary-package",
                "kind-family": "package",
                "concrete-kind": "rubygem",
                "min-count": 1,
                "max-count": 1,
            }
        ],
    }
    plan["graph"]["target-instance-snapshots"]["rubygems/rubygems-org"] = (
        snapshot
    )
    plan["graph"]["artifacts"]["artifact/package"]["concrete-kind"] = "rubygem"
    node = plan["graph"]["publish-nodes"]["publish-node/nuget"]
    node["target-instance-snapshot-id"] = "rubygems/rubygems-org"
    execution_sets["active-publish-selectors"][
        "external-oidc-entry-workflow"
    ] = []
    execution_sets["active-publish-selectors"][
        "external-oidc-reusable-workflow"
    ] = ["publish-node/nuget"]
    return plan, execution_sets


def _public_registry_observation_plan(family: str) -> dict[str, object]:
    """Return a plan containing only one public registry publish node."""
    plan = _pypi_only_observation_plan()
    if family == "pypi":
        return plan
    snapshot = plan["graph"]["target-instance-snapshots"].pop("pypi/pypi")
    if family == "nuget":
        snapshot["catalog-ref"] = "nuget/nuget-org"
        snapshot["destination"] = {"host": "nuget.org"}
        snapshot["family"] = "nuget"
        snapshot["instance-id"] = "nuget-org"
        snapshot["contract"]["id"] = "nuget-publish"
    elif family == "npm":
        snapshot["catalog-ref"] = "npm/npmjs"
        snapshot["destination"] = {"host": "registry.npmjs.org"}
        snapshot["family"] = "npm"
        snapshot["instance-id"] = "npmjs"
        snapshot["contract"]["id"] = "npm-publish"
    elif family == "rubygems":
        snapshot["catalog-ref"] = "rubygems/rubygems-org"
        snapshot["destination"] = {"host": "rubygems.org"}
        snapshot["family"] = "rubygems"
        snapshot["instance-id"] = "rubygems-org"
        snapshot["contract"]["id"] = "rubygems-publish"
    else:
        msg = f"unknown registry family {family!r}"
        raise AssertionError(msg)
    node = plan["graph"]["publish-nodes"]["publish-node/nuget"]
    node["target-instance-snapshot-id"] = snapshot["catalog-ref"]
    plan["graph"]["target-instance-snapshots"] = {
        snapshot["catalog-ref"]: snapshot
    }
    return plan


def _github_packages_nuget_observation_plan() -> dict[str, object]:
    """Return a plan containing only one GitHub Packages NuGet publish node."""
    plan = _public_registry_observation_plan("nuget")
    snapshot = plan["graph"]["target-instance-snapshots"].pop("nuget/nuget-org")
    snapshot["catalog-ref"] = "nuget/github-packages"
    snapshot["destination"] = {
        "host": "nuget.pkg.github.com",
        "owner": "hcoona",
    }
    snapshot["instance-id"] = "github-packages"
    snapshot["capabilities"]["credential-posture"] = "github-token"
    snapshot["capabilities"]["name-uniqueness-scope"] = (
        "package-name-with-owner"
    )
    snapshot["capabilities"]["profile-coexistence-rule"] = "same-name-allowed"
    snapshot["capabilities"]["publish-topology"] = "github-token"
    node = plan["graph"]["publish-nodes"]["publish-node/nuget"]
    node["target-instance-snapshot-id"] = "nuget/github-packages"
    plan["graph"]["target-instance-snapshots"] = {
        "nuget/github-packages": snapshot
    }
    return plan


def _single_active_oidc_execution_sets(
    topology: str = "external-oidc-entry-workflow",
) -> dict[str, object]:
    """Return sets with only the fixture package publish node active."""
    execution_sets = deepcopy(_load("execution-sets.json"))
    execution_sets["active-publish-node-ids"] = ["publish-node/nuget"]
    execution_sets["publish-intent-node-ids"] = ["publish-node/nuget"]
    execution_sets["selected-github-release-publish-node-ids"] = []
    execution_sets["active-github-release-publish-node-ids"] = []
    execution_sets["skip-satisfied-publish-node-ids"] = []
    for selector in execution_sets["active-publish-selectors"].values():
        selector.clear()
    execution_sets["active-publish-selectors"][topology] = [
        "publish-node/nuget"
    ]
    return execution_sets


def _run_plan_gate_case(
    case_dir: Path,
    plan: dict[str, object],
    execution_sets: dict[str, object],
    enablement: str,
    expected_code: str,
) -> None:
    """Run the plan gate and assert it emits diagnostics only."""
    case_dir.mkdir(parents=True)
    plan_path = case_dir / "plan-input.json"
    sets_path = case_dir / "sets-input.json"
    diagnostics_path = case_dir / "planner-diagnostics.json"
    plan_path.write_text(json.dumps(plan), encoding="utf-8")
    sets_path.write_text(json.dumps(execution_sets), encoding="utf-8")

    result = control._cmd_plan_gate(
        control.argparse.Namespace(
            plan=str(plan_path),
            execution_sets=str(sets_path),
            enabled_external_oidc_targets=enablement,
            diagnostics_out=str(diagnostics_path),
        )
    )

    assert result == 1
    assert diagnostics_path.is_file()
    assert expected_code in _diagnostic_codes(diagnostics_path)
    _assert_forbidden_outputs_absent(case_dir)


def _run_normalize_entry_case(
    case_dir: Path,
    expected_codes: set[str],
    *,
    overrides: dict[str, str] | None = None,
) -> None:
    """Run entry normalization and assert it emits diagnostics only."""
    case_dir.mkdir(parents=True)
    metadata = case_dir / "entry-metadata.json"
    diagnostics = case_dir / "planner-diagnostics.json"
    inputs = {
        "profile": "official",
        "dry_run": "true",
        "validation_build": "false",
        "force": "false",
    } | (overrides or {})

    result = control._cmd_normalize_entry(
        control.argparse.Namespace(
            profile=inputs["profile"],
            repository="hcoona/three",
            actor="candidate",
            ref="refs/heads/main",
            ref_name="main",
            ref_type="branch",
            pinned_sha=SHA_B,
            requested_project_ids="",
            dry_run=inputs["dry_run"],
            validation_build=inputs["validation_build"],
            force=inputs["force"],
            canary_override_non_public_ref=inputs.get(
                "canary_override_non_public_ref", "false"
            ),
            metadata_out=str(metadata),
            diagnostics_out=str(diagnostics),
            github_output=str(case_dir / "github-output.txt"),
        )
    )

    assert result == 1
    assert not metadata.exists()
    assert expected_codes <= set(_diagnostic_codes(diagnostics))
    _assert_forbidden_outputs_absent(case_dir)


def _step_block(workflow: str, step_name: str) -> str:
    """Return the YAML block for one named workflow step."""
    start = workflow.index(f"      - name: {step_name}\n")
    end = workflow.find("\n      - ", start + 1)
    if end == -1:
        end = len(workflow)
    return workflow[start:end]


def test_acceptance_matrix_fixture_tracks_design_scenarios() -> None:
    """Acceptance fixture must cover every required design matrix row."""
    matrix = _acceptance_matrix()
    design_scenarios = _design_acceptance_scenarios()
    rows = matrix["rows"]
    assert isinstance(rows, list)

    assert matrix["api-version"] == ("three.release.acceptance-matrix/v1alpha1")
    assert matrix["kind"] == "workflow-release-acceptance-matrix"
    assert [row["scenario"] for row in rows] == design_scenarios
    assert len({row["id"] for row in rows}) == len(rows)


def test_acceptance_matrix_rows_are_ci_actionable() -> None:
    """Every acceptance row links to concrete executable evidence."""
    matrix = _acceptance_matrix()
    columns = matrix["evidence-columns"]
    assert columns == [
        "descriptor-or-catalog",
        "plan",
        "execution-set-selectors",
        "request-result-or-receipt",
        "registry-or-readiness",
        "workflow-conclusion",
    ]
    allowed_modes = {
        "ci-fixture",
        "ci-structure",
        "ci-mocked",
        "ci-and-manual-live",
        "manual-live",
        "manual-live-gated",
    }
    valid_artifacts = {path.name for path in (FIXTURES).glob("*.json")} | {
        "github-release-asset-proof.json",
        "immutable-proof.json",
        "execution-sets.json",
    }
    test_nodeids = _all_test_nodeids()
    live_gates = matrix["live-gates"]
    assert isinstance(live_gates, dict)

    for row in matrix["rows"]:
        assert row["validation-mode"] in allowed_modes
        assert row["fixture-anchor"]
        evidence = row["evidence"]
        assert set(evidence) == set(columns)
        for column in columns:
            references = evidence[column]
            assert isinstance(references, list)
            assert references
            for reference in references:
                assert isinstance(reference, dict)
                assert set(reference) == {"type", "value"}
                ref_type = reference["type"]
                value = reference["value"]
                assert isinstance(value, str)
                assert value != "required"
                if ref_type == "path":
                    assert (REPO_ROOT / value).exists(), (row["id"], column)
                elif ref_type == "test":
                    assert value in test_nodeids, (row["id"], column, value)
                elif ref_type in {"artifact", "absent-artifact"}:
                    assert value in valid_artifacts, (row["id"], column)
                elif ref_type == "workflow":
                    assert (REPO_ROOT / value).is_file(), (row["id"], column)
                elif ref_type == "live-gate":
                    assert value in live_gates, (row["id"], column, value)
                    assert live_gates[value]["owner"]
                    assert live_gates[value]["evidence"]
                else:
                    raise AssertionError((row["id"], column, ref_type))


def test_ci_acceptance_matrix_fixture_tracks_lld_scenarios() -> None:
    """CI acceptance fixture must cover every LLD acceptance row."""
    matrix = _ci_acceptance_matrix()
    design_scenarios = _ci_design_acceptance_scenarios()
    rows = matrix["rows"]
    assert isinstance(rows, list)

    assert matrix["api-version"] == (
        "three.release.ci-validation-acceptance-matrix/v1alpha1"
    )
    assert matrix["kind"] == "workflow-release-ci-validation-acceptance-matrix"
    assert matrix["source"] == {
        "design": (
            "docs/wiki/analyses/"
            "workflow-release-ci-affected-validation-low-level-design.md"
        ),
        "section": "17. Acceptance Traceability",
    }
    assert [row["scenario"] for row in rows] == design_scenarios
    assert len({row["id"] for row in rows}) == len(rows)


def test_ci_acceptance_matrix_rows_are_actionable() -> None:
    """Every CI acceptance row maps design text to executable evidence."""
    matrix = _ci_acceptance_matrix()
    columns = matrix["evidence-columns"]
    assert columns == [
        "design-contract",
        "planning-evidence",
        "execution-evidence",
        "aggregate-or-verdict",
        "no-publication-boundary",
    ]
    test_nodeids = _all_test_nodeids()

    for row in matrix["rows"]:
        assert row["validation-mode"] == "ci-acceptance"
        assert row["fixture-anchor"] == (
            "workflow-release-ci-affected-validation-low-level-design#17"
        )
        evidence = row["evidence"]
        assert set(evidence) == set(columns)
        row_test_refs: list[str] = []
        for column in columns:
            references = evidence[column]
            assert isinstance(references, list)
            assert references
            for reference in references:
                assert set(reference) == {"type", "value"}
                ref_type = reference["type"]
                value = reference["value"]
                assert isinstance(value, str)
                if ref_type == "path":
                    assert (REPO_ROOT / value).is_file(), (row["id"], column)
                elif ref_type == "test":
                    assert value in test_nodeids, (row["id"], column, value)
                    row_test_refs.append(value)
                elif ref_type == "workflow":
                    assert (REPO_ROOT / value).is_file(), (row["id"], column)
                else:
                    raise AssertionError((row["id"], column, ref_type))
        assert row_test_refs, row["id"]


def test_ci_acceptance_matrix_preserves_no_publish_boundaries() -> None:
    """CI acceptance rows validate no-publish boundaries, not release probes."""
    matrix = _ci_acceptance_matrix()
    forbidden_nodeid_terms = {
        "observe_remote",
        "publish_request",
        "entry_publish",
        "ensure_tag",
        "official_entry_publish",
    }
    required_boundary_tests = {
        "tests/test_workflow_release_control.py::"
        "test_ci_validation_workflow_executes_mapped_commands_before_receipts",
        "tests/test_workflow_release_control.py::"
        "test_ci_validation_command_mapping_uses_required_no_publish_checks",
    }

    for row in matrix["rows"]:
        boundary_refs = row["evidence"]["no-publication-boundary"]
        assert {
            reference["value"]
            for reference in boundary_refs
            if reference["type"] == "workflow"
        } == {".github/workflows/ci-validate.yml"}
        assert required_boundary_tests <= {
            reference["value"]
            for reference in boundary_refs
            if reference["type"] == "test"
        }
        all_tests = {
            reference["value"]
            for references in row["evidence"].values()
            for reference in references
            if reference["type"] == "test"
        }
        assert not any(
            term in nodeid
            for nodeid in all_tests
            for term in forbidden_nodeid_terms
        ), row["id"]


def test_acceptance_matrix_test_nodeids_are_collected_by_gate() -> None:
    """HK acceptance gate must execute every matrix test evidence nodeid."""
    matrix = _acceptance_matrix()
    ci_matrix = _ci_acceptance_matrix()
    gate_nodeids = set(acceptance_gate._collect_test_nodeids(matrix))
    ci_gate_nodeids = set(acceptance_gate._collect_test_nodeids(ci_matrix))
    mandatory_nodeids = {
        "tests/test_workflow_release_control.py::"
        "test_acceptance_gate_rejects_option_like_nodeids_and_uses_separator",
        "tests/test_workflow_release_control.py::"
        "test_ci_acceptance_matrix_fixture_tracks_lld_scenarios",
        "tests/test_workflow_release_control.py::"
        "test_ci_acceptance_matrix_rows_are_actionable",
        "tests/test_workflow_release_control.py::"
        "test_ci_acceptance_matrix_preserves_no_publish_boundaries",
        "tests/test_workflow_release_control.py::"
        "test_hk_runs_focused_workflow_release_validation",
        "tests/test_workflow_release_control.py::"
        "test_official_entry_publish_sets_up_npm_trusted_runtime",
    }

    assert _matrix_test_nodeids(matrix)
    for nodeid in _matrix_test_nodeids(matrix):
        assert nodeid in _all_test_nodeids()
        assert nodeid in gate_nodeids
    assert _matrix_test_nodeids(ci_matrix)
    for nodeid in _matrix_test_nodeids(ci_matrix):
        assert nodeid in _all_test_nodeids()
        assert nodeid in ci_gate_nodeids
    assert mandatory_nodeids <= gate_nodeids


def test_acceptance_gate_rejects_option_like_nodeids_and_uses_separator() -> (
    None
):
    """Acceptance gate treats matrix test evidence as positional nodeids."""
    malicious = {
        "rows": [
            {
                "evidence": {
                    "registry-or-readiness": [
                        {"type": "test", "value": "--collect-only"}
                    ]
                }
            }
        ]
    }

    with pytest.raises(ValueError, match="option-like"):
        acceptance_gate._collect_test_nodeids(malicious)

    command = acceptance_gate._pytest_command(
        [
            "tests/test_workflow_release_control.py::test_normalize_project_ids_trims_splits_deduplicates_and_sorts"
        ]
    )
    separator_index = command.index("--")
    assert command[separator_index - 1] == "--import-mode=importlib"
    assert command[separator_index + 1].startswith("tests/")


def test_confirmed_scope_descriptor_matrix_matches_current_descriptors() -> (
    None
):
    """Confirmed-scope acceptance row enumerates every descriptor root."""
    matrix = _acceptance_matrix()
    row = next(
        item
        for item in matrix["rows"]
        if item["id"] == "confirmed-scope-descriptor-coverage"
    )
    descriptor_paths = sorted(
        reference["value"]
        for reference in row["evidence"]["descriptor-or-catalog"]
        if reference["type"] == "path"
        and reference["value"].endswith("/three.release.yml")
    )

    assert descriptor_paths == _confirmed_scope_descriptor_paths()


def test_unfiltered_first_delivery_plan_includes_confirmed_scope_projects(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Unfiltered planning selects every confirmed first-delivery project."""
    commit_sha = "a" * 40
    snapshot = validate_authoring(REPO_ROOT)
    expected_project_ids = _confirmed_scope_project_ids()
    metadata_input = snapshot.dotnet_metadata_input(commit_sha)
    dotnet_metadata = _dotnet_metadata_for_planner_input(metadata_input)

    assert expected_project_ids == sorted(snapshot.projects)

    def fake_run(
        args: list[str],
        **_kwargs: object,
    ) -> subprocess.CompletedProcess[str]:
        command = Path(str(args[0])).name
        if len(args) > 2 and args[1] == "show":
            relative = str(args[2]).split(":", maxsplit=1)[1]
            return subprocess.CompletedProcess(
                args,
                0,
                (REPO_ROOT / relative).read_text(encoding="utf-8"),
                "",
            )
        if "worktree" in args:
            if "add" in args:
                Path(str(args[-2])).mkdir(parents=True, exist_ok=True)
            return subprocess.CompletedProcess(args, 0, "", "")
        if command == "dotnet" and "nbgv" in args:
            return subprocess.CompletedProcess(
                args, 0, json.dumps({"SemVer2": "1.2.3"}), ""
            )
        if command == "uv" and "build" in args:
            out_dir = Path(str(args[args.index("--out-dir") + 1]))
            out_dir.mkdir(parents=True, exist_ok=True)
            release_root = str(args[-1])
            package, version = {
                "src/public/lib/hcoona-release-smoke-pypi": (
                    "hcoona_release_smoke_pypi",
                    "1.2.3",
                ),
                "src/public/lib/nbgv-python": (
                    "nbgv_python",
                    "2.1.0.dev1",
                ),
            }[release_root]
            (out_dir / f"{package}-{version}-py3-none-any.whl").write_text(
                "",
                encoding="utf-8",
            )
            (out_dir / f"{package}-{version}.tar.gz").write_text(
                "",
                encoding="utf-8",
            )
            return subprocess.CompletedProcess(args, 0, "", "")
        if command == "ruby":
            metadata = {
                "name": "asciidoctor-latexmath",
                "version": "1.2.3",
                "file_name": "asciidoctor-latexmath-1.2.3.gem",
            }
            return subprocess.CompletedProcess(
                args, 0, json.dumps(metadata), ""
            )
        message = f"unexpected planner subprocess: {args}"
        raise AssertionError(message)

    monkeypatch.setattr(
        "three_workflow_release_planner.planner.subprocess.run",
        fake_run,
    )
    try:
        result = plan_release(
            snapshot,
            PlannerInputs(
                request={
                    "api-version": "three.release.planner-request/v1alpha1",
                    "kind": "planner-request",
                    "profile": "buddy",
                    "commit-sha": commit_sha,
                    "requested-project-ids": [],
                    "request-flags": {"force": False},
                },
                repo_root=REPO_ROOT,
                dry_run=True,
                dotnet_metadata=dotnet_metadata,
            ),
        )
    finally:
        shutil.rmtree(
            REPO_ROOT / ".three-workflow-release-planner",
            ignore_errors=True,
        )

    envelope = result.plan["envelope"]
    assert isinstance(envelope, dict)
    projects = envelope["projects"]
    assert isinstance(projects, dict)
    assert envelope["requested-project-ids"] == []
    assert envelope["selected-project-ids"] == expected_project_ids
    assert sorted(projects) == expected_project_ids


def test_fail_closed_acceptance_rows_match_phase_artifact_contracts() -> None:
    """Fail-closed acceptance rows declare only phase-allowed artifacts."""
    matrix = _acceptance_matrix()
    fail_closed_ids = {
        row["id"]
        for row in matrix["rows"]
        if any(
            marker in row["scenario"].lower()
            for marker in (
                "fail closed",
                "fail-closed",
                "fails closed",
                "blocked",
                "disabled",
                "rejected",
                "trusted workflow-ref",
                "invalid entry",
                "authorization fails closed",
                "metadata mismatch",
                "partial replay",
            )
        )
    }

    assert fail_closed_ids == set(FAIL_CLOSED_ARTIFACT_CONTRACTS)
    for row in matrix["rows"]:
        contract = FAIL_CLOSED_ARTIFACT_CONTRACTS.get(row["id"])
        if contract is None:
            continue
        references = [
            reference
            for column in row["evidence"].values()
            for reference in column
        ]
        positive_artifacts = {
            reference["value"]
            for reference in references
            if reference["type"] == "artifact"
        }
        absent_artifacts = {
            reference["value"]
            for reference in references
            if reference["type"] == "absent-artifact"
        }
        positive_paths = {
            Path(reference["value"]).name
            for reference in references
            if reference["type"] == "path"
        }

        assert positive_artifacts <= contract["allowed"]
        assert absent_artifacts >= contract["absent"]
        assert positive_artifacts.isdisjoint(contract["absent"])
        assert positive_paths.isdisjoint(contract["absent"])
        if row["id"] in PRE_PLAN_FAIL_CLOSED_IDS:
            assert "planner-diagnostics.json" in positive_artifacts
            assert "release-report.json" in positive_artifacts
            assert "release-report.json" not in absent_artifacts


def test_no_side_effect_acceptance_rows_match_artifact_contracts() -> None:
    """No-side-effect rows must not cite downstream live-output evidence."""
    matrix = _acceptance_matrix()
    for row in matrix["rows"]:
        contract = NO_SIDE_EFFECT_ARTIFACT_CONTRACTS.get(row["id"])
        if contract is None:
            continue
        references = [
            reference
            for column in row["evidence"].values()
            for reference in column
        ]
        positive_artifacts = {
            reference["value"]
            for reference in references
            if reference["type"] == "artifact"
        }
        absent_artifacts = {
            reference["value"]
            for reference in references
            if reference["type"] == "absent-artifact"
        }
        positive_paths = {
            Path(reference["value"]).name
            for reference in references
            if reference["type"] == "path"
        }

        assert positive_artifacts <= contract["allowed"]
        assert absent_artifacts >= contract["absent"]
        assert positive_artifacts.isdisjoint(contract["absent"])
        assert positive_paths.isdisjoint(contract["absent"])


def test_fail_closed_acceptance_flows_emit_diagnostics_without_outputs() -> (
    None
):
    """Real invalid commands fail before downstream outputs."""
    scratch = SCRATCH / "fail-closed-flows"
    if scratch.exists():
        shutil.rmtree(scratch)
    scratch.mkdir(parents=True)

    _validate_mutated_authoring_repo(
        scratch,
        "invalid-descriptor",
        _mutate_descriptor_extra_field,
        "DESC_SCHEMA_INVALID",
    )
    _validate_mutated_authoring_repo(
        scratch,
        "invalid-catalog",
        _mutate_catalog_schema,
        "CATALOG_SCHEMA_INVALID",
    )
    _validate_mutated_authoring_repo(
        scratch,
        "missing-catalog-ref",
        _mutate_missing_catalog_ref,
        "CATALOG_REF_NOT_FOUND",
    )
    _validate_mutated_authoring_repo(
        scratch,
        "profile-coexistence-conflict",
        _mutate_profile_coexistence_conflict,
        "DESC_STATIC_INVALID",
    )

    unknown_project = scratch / "unknown-project"
    unknown_project.mkdir()
    request = unknown_project / "planner-request.json"
    request.write_text(
        json.dumps(
            {
                "api-version": "three.release.planner-request/v1alpha1",
                "kind": "planner-request",
                "profile": "buddy",
                "commit-sha": SHA_B,
                "requested-project-ids": ["nbgv-python", "missing-project"],
                "request-flags": {"force": False},
            },
            indent=2,
            sort_keys=True,
        )
        + "\n",
        encoding="utf-8",
    )
    unknown_project_diag = unknown_project / "planner-diagnostics.json"
    unknown_project_result = subprocess.run(  # noqa: S603
        [
            sys.executable,
            "-m",
            "three_workflow_release_planner.cli",
            "plan",
            "--repo-root",
            str(REPO_ROOT),
            "--request",
            str(request),
            "--plan-out",
            str(unknown_project / "release-plan.json"),
            "--execution-sets-out",
            str(unknown_project / "execution-sets.json"),
            "--diagnostics-out",
            str(unknown_project_diag),
        ],
        cwd=REPO_ROOT,
        check=False,
    )
    assert unknown_project_result.returncode == 1
    assert unknown_project_diag.is_file()
    assert "REQ_PROJECT_NOT_FOUND" in _diagnostic_codes(unknown_project_diag)
    _assert_forbidden_outputs_absent(unknown_project)


def test_pre_plan_fail_closed_acceptance_allows_final_report_only() -> None:
    """Pre-plan failures still render only report and diagnostics artifacts."""
    scratch = SCRATCH / "pre-plan-report"
    report_path = scratch / "release-report.json"
    diagnostics_path = scratch / "planner-diagnostics.json"
    shutil.rmtree(scratch, ignore_errors=True)
    scratch.mkdir(parents=True)
    try:
        diagnostics_path.write_text(
            json.dumps(
                {
                    "api-version": "three.release.planner-diagnostics/v1alpha1",
                    "kind": "planner-diagnostics",
                    "diagnostics": [
                        {
                            "api-version": (
                                "three.release.planner-diagnostic/v1alpha1"
                            ),
                            "kind": "planner-diagnostic",
                            "code": "REQ_PROJECT_NOT_FOUND",
                            "message": "requested project is not releasable",
                            "phase": "validation",
                            "scope-kind": "request",
                            "blocking": True,
                            "details": {"project-id": "missing-project"},
                        }
                    ],
                }
            ),
            encoding="utf-8",
        )

        assert (
            control._cmd_report(
                control.argparse.Namespace(
                    repository="hcoona/three",
                    workflow="Release Buddy",
                    run_id=123,
                    attempt=4,
                    head_sha=SHA_B,
                    profile="buddy",
                    dry_run="false",
                    validation_build="false",
                    canary_override_non_public_ref="false",
                    out=str(report_path),
                    plan="",
                    execution_sets="",
                    diagnostics=str(diagnostics_path),
                    artifacts_root="",
                    authorize_conclusion="success",
                    validate_conclusion="success",
                    metadata_conclusion="skipped",
                    plan_conclusion="failure",
                    build_conclusion="skipped",
                    tag_conclusion="skipped",
                    publish_conclusion="skipped",
                )
            )
            == 0
        )
        report = json.loads(report_path.read_text(encoding="utf-8"))
        validate_contract(report)
        assert report["run"]["conclusion"] == "failure"
        assert report["plan"]["plan-id"] is None
        assert report["plan"]["selected-project-ids"] is None
        _assert_forbidden_outputs_absent(scratch)
        assert report_path.is_file()
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_external_oidc_fail_closed_gates_emit_diagnostics_without_outputs(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """External OIDC gates fail before publishing downstream artifacts."""
    scratch = SCRATCH / "external-oidc-fail-closed"
    shutil.rmtree(scratch, ignore_errors=True)
    try:
        plan, execution_sets = _external_oidc_plan_and_sets()
        _run_plan_gate_case(
            scratch / "disabled",
            plan,
            execution_sets,
            "",
            "REQ_EXTERNAL_TARGET_DISABLED",
        )

        plan, execution_sets = _external_oidc_plan_and_sets()
        _run_plan_gate_case(
            scratch / "invalid-allowlist",
            plan,
            execution_sets,
            "not-a-valid-token",
            "REQ_INVALID_INPUT",
        )

        plan, execution_sets = _external_oidc_plan_and_sets()
        _run_plan_gate_case(
            scratch / "missing-observation",
            plan,
            execution_sets,
            "pypi/pypi#example#Example",
            "REMOTE_CLASSIFICATION_FAILED",
        )

        plan, execution_sets = _external_oidc_plan_and_sets()
        monkeypatch.setattr(
            control,
            "_TOPOLOGIES",
            (
                "github-token",
                "external-oidc-caller-workflow",
                "external-oidc-reusable-workflow",
            ),
        )
        _run_plan_gate_case(
            scratch / "blocked-topology",
            plan,
            execution_sets,
            "",
            "REQ_EXTERNAL_TOPOLOGY_BLOCKED",
        )
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_entry_fail_closed_gates_emit_diagnostics_without_outputs(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Entry authorization, input, and ref gates fail before planning."""
    scratch = SCRATCH / "entry-fail-closed"
    shutil.rmtree(scratch, ignore_errors=True)
    try:
        monkeypatch.setattr(control, "_actor_permission", lambda *_: "read")
        monkeypatch.setattr(
            control,
            "_resolve_ref",
            lambda *_: (SHA_B, {"object-type": "commit"}),
        )
        monkeypatch.setattr(control, "_trusted_ref", lambda *_: True)
        _run_normalize_entry_case(
            scratch / "unauthorized",
            {"REQ_ACTOR_UNAUTHORIZED"},
        )

        monkeypatch.setattr(control, "_actor_permission", lambda *_: "maintain")
        _run_normalize_entry_case(
            scratch / "invalid-input",
            {"REQ_INVALID_INPUT", "REQ_FORCE_FOR_OFFICIAL"},
            overrides={
                "dry_run": "false",
                "validation_build": "true",
                "force": "true",
            },
        )

        monkeypatch.setattr(control, "_trusted_ref", lambda *_: False)
        _run_normalize_entry_case(
            scratch / "untrusted-ref",
            {"REQ_UNTRUSTED_WORKFLOW_REF"},
        )
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_normalize_project_ids_trims_splits_deduplicates_and_sorts() -> None:
    """Normalize UI project input into a stable planner filter list."""
    assert control._normalize_project_ids(" beta,alpha\n beta ,,gamma ") == [
        "alpha",
        "beta",
        "gamma",
    ]


def _authorize_entry(
    monkeypatch: pytest.MonkeyPatch,
    inputs: dict[str, str],
) -> tuple[int, dict[str, object] | None, dict[str, object] | None]:
    """Run entry authorization with GitHub API calls mocked as successful."""
    output = SCRATCH / "entry-output.txt"
    metadata = SCRATCH / "entry-metadata.json"
    diagnostics = SCRATCH / "planner-diagnostics.json"
    shutil.rmtree(SCRATCH, ignore_errors=True)
    SCRATCH.mkdir()
    monkeypatch.setattr(control, "_actor_permission", lambda *_: "maintain")
    monkeypatch.setattr(
        control,
        "_resolve_ref",
        lambda *_: (SHA_B, {"object-type": "commit"}),
    )
    monkeypatch.setattr(control, "_trusted_ref", lambda *_: True)
    result = control._cmd_normalize_entry(
        control.argparse.Namespace(
            profile=inputs["profile"],
            repository="hcoona/three",
            actor="maintainer",
            ref=inputs["ref"],
            ref_name=inputs["ref_name"],
            ref_type=inputs["ref_type"],
            pinned_sha=SHA_B,
            requested_project_ids=inputs["requested_project_ids"],
            dry_run="false",
            validation_build="false",
            force="false",
            canary_override_non_public_ref=inputs[
                "canary_override_non_public_ref"
            ],
            metadata_out=str(metadata),
            diagnostics_out=str(diagnostics),
            github_output=str(output),
        )
    )
    metadata_doc = (
        json.loads(metadata.read_text(encoding="utf-8"))
        if metadata.exists()
        else None
    )
    diagnostics_doc = (
        json.loads(diagnostics.read_text(encoding="utf-8"))
        if diagnostics.exists()
        else None
    )
    return result, metadata_doc, diagnostics_doc


def test_official_default_rejects_non_public_release_ref(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Official releases fail closed outside publicReleaseRefSpec."""
    try:
        result, metadata, diagnostics = _authorize_entry(
            monkeypatch,
            {
                "profile": "official",
                "ref": "refs/heads/dev/workflow-canary",
                "ref_name": "dev/workflow-canary",
                "ref_type": "branch",
                "requested_project_ids": "hcoona-release-smoke-pypi",
                "canary_override_non_public_ref": "false",
            },
        )

        assert result == 1
        assert metadata is None
        assert diagnostics is not None
        assert _diagnostic_codes(SCRATCH / "planner-diagnostics.json") == [
            "REQ_UNTRUSTED_WORKFLOW_REF"
        ]
        diagnostic = diagnostics["diagnostics"][0]
        assert diagnostic["project-id"] == "hcoona-release-smoke-pypi"
        assert diagnostic["details"]["canary-override-non-public-ref"] is False
    finally:
        shutil.rmtree(SCRATCH, ignore_errors=True)


def test_official_canary_override_allows_allowlisted_project(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Break-glass official canary override is limited but permits smoke."""
    try:
        result, metadata, diagnostics = _authorize_entry(
            monkeypatch,
            {
                "profile": "official",
                "ref": "refs/heads/dev/workflow-canary",
                "ref_name": "dev/workflow-canary",
                "ref_type": "branch",
                "requested_project_ids": "hcoona-release-smoke-pypi",
                "canary_override_non_public_ref": "true",
            },
        )

        assert result == 0
        assert diagnostics is None
        assert metadata is not None
        assert metadata["requested-project-ids"] == [
            "hcoona-release-smoke-pypi"
        ]
        assert metadata["canary-override-non-public-ref"] is True
    finally:
        shutil.rmtree(SCRATCH, ignore_errors=True)


def test_official_canary_override_rejects_non_allowlisted_project(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Break-glass official canary override is not a general release bypass."""
    try:
        result, metadata, diagnostics = _authorize_entry(
            monkeypatch,
            {
                "profile": "official",
                "ref": "refs/heads/dev/workflow-canary",
                "ref_name": "dev/workflow-canary",
                "ref_type": "branch",
                "requested_project_ids": "nbgv-python",
                "canary_override_non_public_ref": "true",
            },
        )

        assert result == 1
        assert metadata is None
        assert diagnostics is not None
        assert _diagnostic_codes(SCRATCH / "planner-diagnostics.json") == [
            "REQ_INVALID_INPUT"
        ]
        diagnostic = diagnostics["diagnostics"][0]
        assert diagnostic["details"]["canary-override-non-public-ref"] is True
        assert diagnostic["details"]["allowed-project-ids"] == [
            "hcoona-release-smoke-dotnet-executable",
            "hcoona-release-smoke-github-packages",
            "hcoona-release-smoke-github-release",
            "hcoona-release-smoke-inno",
            "hcoona-release-smoke-npm",
            "hcoona-release-smoke-npm-dual",
            "hcoona-release-smoke-nuget",
            "hcoona-release-smoke-pypi",
            "hcoona-release-smoke-rubygems",
            "hcoona-release-smoke-wxt",
        ]
    finally:
        shutil.rmtree(SCRATCH, ignore_errors=True)


def test_buddy_entry_is_not_restricted_by_public_release_ref(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Buddy dispatch remains allowed from trusted non-public release refs."""
    try:
        result, metadata, diagnostics = _authorize_entry(
            monkeypatch,
            {
                "profile": "buddy",
                "ref": "refs/heads/dev/workflow-canary",
                "ref_name": "dev/workflow-canary",
                "ref_type": "branch",
                "requested_project_ids": "hcoona-release-smoke-pypi",
                "canary_override_non_public_ref": "false",
            },
        )

        assert result == 0
        assert diagnostics is None
        assert metadata is not None
        assert metadata["profile"] == "buddy"
    finally:
        shutil.rmtree(SCRATCH, ignore_errors=True)


def test_normalize_entry_uses_dispatch_pinned_sha(monkeypatch) -> None:
    """Entry metadata uses github.sha, not a later ref resolution."""
    pinned_sha = "1" * 40
    later_sha = "2" * 40
    output = SCRATCH / "entry-output.txt"
    metadata = SCRATCH / "entry-metadata.json"
    diagnostics = SCRATCH / "planner-diagnostics.json"
    shutil.rmtree(SCRATCH, ignore_errors=True)
    SCRATCH.mkdir()
    monkeypatch.setattr(control, "_actor_permission", lambda *_: "maintain")
    monkeypatch.setattr(
        control,
        "_resolve_ref",
        lambda *_: (later_sha, {"object-type": "commit"}),
    )
    monkeypatch.setattr(control, "_trusted_ref", lambda *_: True)
    try:
        result = control._cmd_normalize_entry(
            control.argparse.Namespace(
                profile="official",
                repository="hcoona/three",
                actor="maintainer",
                ref="refs/heads/main",
                ref_name="main",
                ref_type="branch",
                pinned_sha=pinned_sha,
                requested_project_ids="",
                dry_run="true",
                validation_build="false",
                force="false",
                canary_override_non_public_ref="false",
                metadata_out=str(metadata),
                diagnostics_out=str(diagnostics),
                github_output=str(output),
            )
        )

        values = dict(
            line.split("=", 1)
            for line in output.read_text(encoding="utf-8").splitlines()
        )
        document = json.loads(metadata.read_text(encoding="utf-8"))
        assert result == 0
        assert values["commit_sha"] == pinned_sha
        assert document["commit-sha"] == pinned_sha
        assert document["commit-sha"] != later_sha
    finally:
        shutil.rmtree(SCRATCH, ignore_errors=True)


def test_matrix_outputs_emit_artifact_names_and_publish_sets() -> None:
    """Derive reusable workflow matrices from closed planner outputs."""
    plan = _load("release-plan.json")
    execution_sets = _load("execution-sets.json")
    output = SCRATCH / "matrix-output.txt"
    shutil.rmtree(SCRATCH, ignore_errors=True)
    SCRATCH.mkdir()
    try:
        args = control.argparse.Namespace(
            plan=str(SCRATCH / "plan.json"),
            execution_sets=str(SCRATCH / "execution-sets.json"),
            run_id=123,
            attempt=4,
            github_output=str(output),
        )
        (SCRATCH / "plan.json").write_text(json.dumps(plan), encoding="utf-8")
        (SCRATCH / "execution-sets.json").write_text(
            json.dumps(execution_sets), encoding="utf-8"
        )

        assert control._cmd_matrix_outputs(args) == 0

        values = dict(
            line.split("=", 1)
            for line in output.read_text(encoding="utf-8").splitlines()
        )
        assert values["plan_id"] == "plan/abc123"
        assert values["variant_ids"] == '["variant/v1"]'
        assert values["variant_matrix"] == (
            '[{"variant-id":"variant/v1","runner":"windows-latest"}]'
        )
        assert values["reusable_publish_node_ids"] == '["publish-node/gh"]'
        assert (
            values["reusable_github_release_publish_node_ids"]
            == '["publish-node/gh"]'
        )
        assert values["reusable_github_packages_publish_node_ids"] == "[]"
        assert values["reusable_external_oidc_publish_node_ids"] == "[]"
        assert values["has_reusable_github_release_publish"] == "true"
        assert values["has_reusable_github_packages_publish"] == "false"
        assert values["has_reusable_external_oidc_publish"] == "false"
        assert values["skip_publish_node_ids"] == '["publish-node/nuget"]'
        assert values["has_entry_proofs"] == "false"
        assert values["entry_proof_matrix"] == "[]"
        assert values["plan_artifact_name"] == artifact_name(
            "plan",
            ArtifactNameInputs(123, 4, plan_id="plan/abc123"),
        )
    finally:
        shutil.rmtree(SCRATCH, ignore_errors=True)


def test_matrix_outputs_partition_reusable_publish_permission_classes() -> None:
    """Reusable publish fan-out is split by required credential capability."""
    plan = deepcopy(_load("release-plan.json"))
    execution_sets = deepcopy(_load("execution-sets.json"))
    output = SCRATCH / "matrix-output.txt"
    execution_sets["active-publish-selectors"]["github-token"] = [
        "publish-node/gh",
        "publish-node/nuget",
    ]
    execution_sets["skip-satisfied-publish-node-ids"] = []
    execution_sets["active-publish-node-ids"] = [
        "publish-node/gh",
        "publish-node/nuget",
    ]
    plan["graph"]["publish-nodes"]["publish-node/nuget"][
        "publish-disposition"
    ] = "publish"
    shutil.rmtree(SCRATCH, ignore_errors=True)
    SCRATCH.mkdir()
    try:
        args = control.argparse.Namespace(
            plan=str(SCRATCH / "plan.json"),
            execution_sets=str(SCRATCH / "execution-sets.json"),
            run_id=123,
            attempt=4,
            github_output=str(output),
        )
        (SCRATCH / "plan.json").write_text(json.dumps(plan), encoding="utf-8")
        (SCRATCH / "execution-sets.json").write_text(
            json.dumps(execution_sets), encoding="utf-8"
        )

        assert control._cmd_matrix_outputs(args) == 0

        values = dict(
            line.split("=", 1)
            for line in output.read_text(encoding="utf-8").splitlines()
        )
        assert (
            values["reusable_publish_node_ids"]
            == '["publish-node/gh","publish-node/nuget"]'
        )
        assert (
            values["reusable_github_release_publish_node_ids"]
            == '["publish-node/gh"]'
        )
        assert (
            values["reusable_github_packages_publish_node_ids"]
            == '["publish-node/nuget"]'
        )
        assert values["reusable_external_oidc_publish_node_ids"] == "[]"
        assert values["has_reusable_github_release_publish"] == "true"
        assert values["has_reusable_github_packages_publish"] == "true"
        assert values["has_reusable_external_oidc_publish"] == "false"
    finally:
        shutil.rmtree(SCRATCH, ignore_errors=True)


def test_matrix_outputs_route_non_dotnet_variants_to_ubuntu() -> None:
    """Only .NET release variants require Windows builders."""
    plan = deepcopy(_load("release-plan.json"))
    plan["envelope"]["projects"]["example"]["ecosystem"] = "python"

    assert control._variant_runner(plan, "variant/v1") == "ubuntu-latest"


def test_dotnet_executable_linux_variant_uses_ubuntu_runner() -> None:
    """NativeAOT Linux executable variants must build on Linux."""
    plan = _dotnet_executable_runner_plan({"rid": "linux-x64"})

    assert control._variant_runner(plan, "variant/v1") == "ubuntu-latest"


def test_dotnet_executable_windows_variant_uses_windows_runner() -> None:
    """NativeAOT Windows executable variants continue to build on Windows."""
    plan = _dotnet_executable_runner_plan({"rid": "win-x64"})

    assert control._variant_runner(plan, "variant/v1") == "windows-latest"


def _dotnet_executable_runner_plan(
    dimensions: dict[str, str],
) -> dict[str, object]:
    plan = deepcopy(_load("release-plan.json"))
    plan["graph"]["variants"]["variant/v1"]["dimensions"] = dimensions
    plan["graph"]["variants"]["variant/v1"]["artifact-ids"] = ["artifact/exe"]
    plan["graph"]["artifacts"] = {
        "artifact/exe": {
            "concrete-kind": "executable",
            "descriptor-handle": "app-binary",
            "kind-family": "binary",
            "produced-from-artifact-ids": [],
            "project-id": "example",
            "role": "primary-binary",
            "variant-id": "variant/v1",
        }
    }
    return plan


def test_dry_run_acceptance_control_plane_has_no_side_effect_matrix() -> None:
    """Dry-run control plane emits closed no-side-effect CI evidence."""
    plan = _load("release-plan.json")
    execution_sets = _load("execution-sets-empty.json")
    output = SCRATCH / "dry-run-output.txt"
    handoff_path = SCRATCH / "entry-publish-handoff.json"
    artifacts_root = SCRATCH / "artifacts"
    report_path = SCRATCH / "release-report.json"
    shutil.rmtree(SCRATCH, ignore_errors=True)
    SCRATCH.mkdir()
    try:
        plan_path = SCRATCH / "release-plan.json"
        sets_path = SCRATCH / "execution-sets.json"
        plan_path.write_text(json.dumps(plan), encoding="utf-8")
        sets_path.write_text(json.dumps(execution_sets), encoding="utf-8")

        assert (
            control._cmd_matrix_outputs(
                control.argparse.Namespace(
                    plan=str(plan_path),
                    execution_sets=str(sets_path),
                    run_id=123,
                    attempt=4,
                    github_output=str(output),
                )
            )
            == 0
        )
        values = dict(
            line.split("=", 1)
            for line in output.read_text(encoding="utf-8").splitlines()
        )
        assert values["has_variants"] == "false"
        assert values["has_reusable_publish"] == "false"
        assert values["has_entry_publish"] == "false"
        assert values["has_active_github_release"] == "false"
        assert values["has_live_side_effects"] == "false"
        assert values["variant_matrix"] == "[]"

        assert (
            control._cmd_entry_publish_handoff(
                control.argparse.Namespace(
                    plan=str(plan_path),
                    execution_sets=str(sets_path),
                    run_id=123,
                    attempt=4,
                    out=str(handoff_path),
                )
            )
            == 0
        )
        handoff = json.loads(handoff_path.read_text(encoding="utf-8"))
        validate_contract(handoff)
        assert handoff["entry-publish-node-ids"] == []

        for artifact_value in (
            "plan_artifact_name",
            "execution_sets_artifact_name",
            "entry_publish_handoff_artifact_name",
        ):
            (artifacts_root / values[artifact_value]).mkdir(parents=True)
        assert (
            control._cmd_report(
                control.argparse.Namespace(
                    repository="hcoona/three",
                    workflow="Release Buddy",
                    run_id=123,
                    attempt=4,
                    head_sha=SHA_B,
                    profile="buddy",
                    dry_run="true",
                    validation_build="false",
                    canary_override_non_public_ref="false",
                    out=str(report_path),
                    plan=str(plan_path),
                    execution_sets=str(sets_path),
                    diagnostics="",
                    artifacts_root=str(artifacts_root),
                    authorize_conclusion="success",
                    validate_conclusion="success",
                    metadata_conclusion="skipped",
                    plan_conclusion="success",
                    build_conclusion="skipped",
                    tag_conclusion="skipped",
                    publish_conclusion="skipped",
                )
            )
            == 0
        )
        report = json.loads(report_path.read_text(encoding="utf-8"))
        validate_contract(report)
        assert report["run"]["conclusion"] == "success"
        assert report["counts"]["active-variants"] == 0
        assert report["counts"]["active-publish-nodes"] == 0
    finally:
        shutil.rmtree(SCRATCH, ignore_errors=True)


def test_live_zero_target_acceptance_has_no_side_effect_gates() -> None:
    """Live zero-target plans skip every active side-effect gate."""
    plan = _load("release-plan.json")
    execution_sets = deepcopy(_load("execution-sets-empty.json"))
    execution_sets["dry-run"] = False
    output = SCRATCH / "live-zero-target-output.txt"
    report_path = SCRATCH / "live-zero-target-report.json"
    artifacts_root = SCRATCH / "live-zero-target-artifacts"
    workflow = _workflow("release-orchestrate.yml")
    shutil.rmtree(SCRATCH, ignore_errors=True)
    SCRATCH.mkdir()
    try:
        plan_path = SCRATCH / "release-plan.json"
        sets_path = SCRATCH / "execution-sets.json"
        plan_path.write_text(json.dumps(plan), encoding="utf-8")
        sets_path.write_text(json.dumps(execution_sets), encoding="utf-8")

        assert (
            control._cmd_matrix_outputs(
                control.argparse.Namespace(
                    plan=str(plan_path),
                    execution_sets=str(sets_path),
                    run_id=123,
                    attempt=4,
                    github_output=str(output),
                )
            )
            == 0
        )
        values = dict(
            line.split("=", 1)
            for line in output.read_text(encoding="utf-8").splitlines()
        )
        assert values["has_variants"] == "false"
        assert values["has_reusable_publish"] == "false"
        assert values["has_entry_publish"] == "false"
        assert values["has_active_github_release"] == "false"
        assert values["has_skip_results"] == "false"
        assert values["has_live_side_effects"] == "false"
        assert values["variant_matrix"] == "[]"

        environment_block_start = workflow.index(
            "  ensure-tag-with-environment:\n"
        )
        environment_block_end = workflow.index(
            "\n  ensure-tag-without-environment:\n",
            environment_block_start,
        )
        environment_block = workflow[
            environment_block_start:environment_block_end
        ]
        assert "has-active-github-release == 'true'" in environment_block

        for artifact_value in (
            "plan_artifact_name",
            "execution_sets_artifact_name",
            "entry_publish_handoff_artifact_name",
        ):
            (artifacts_root / values[artifact_value]).mkdir(parents=True)
        assert (
            control._cmd_report(
                control.argparse.Namespace(
                    repository="hcoona/three",
                    workflow="Release Official",
                    run_id=123,
                    attempt=4,
                    head_sha=SHA_B,
                    profile="official",
                    dry_run="false",
                    validation_build="false",
                    canary_override_non_public_ref="false",
                    out=str(report_path),
                    plan=str(plan_path),
                    execution_sets=str(sets_path),
                    diagnostics="",
                    artifacts_root=str(artifacts_root),
                    authorize_conclusion="success",
                    validate_conclusion="success",
                    metadata_conclusion="success",
                    plan_conclusion="success",
                    build_conclusion="skipped",
                    tag_conclusion="skipped",
                    publish_conclusion="skipped",
                )
            )
            == 0
        )
        report = json.loads(report_path.read_text(encoding="utf-8"))
        validate_contract(report)
        assert report["counts"]["active-variants"] == 0
        assert report["counts"]["active-publish-nodes"] == 0
        assert report["artifacts"]["build-result-artifact-names"] == []
        assert report["artifacts"]["tag-result-artifact-name"] is None
        assert report["artifacts"]["publish-result-artifact-names"] == []
        assert report["artifacts"]["skip-result-artifact-names"] == []
    finally:
        shutil.rmtree(SCRATCH, ignore_errors=True)


def test_live_all_skip_acceptance_emits_only_skip_receipts() -> None:
    """Live all-skip plans may emit skip receipts but no side-effect outputs."""
    plan = deepcopy(_load("release-plan.json"))
    execution_sets = deepcopy(_load("execution-sets.json"))
    plan["graph"]["publish-nodes"]["publish-node/gh"]["publish-disposition"] = (
        "skip-satisfied"
    )
    execution_sets["active-variant-ids"] = []
    execution_sets["active-publish-node-ids"] = []
    execution_sets["active-github-release-publish-node-ids"] = []
    execution_sets["publish-intent-node-ids"] = []
    execution_sets["skip-satisfied-publish-node-ids"] = [
        "publish-node/gh",
        "publish-node/nuget",
    ]
    for selector in execution_sets["active-publish-selectors"].values():
        selector.clear()
    output = SCRATCH / "live-all-skip-output.txt"
    manifest = SCRATCH / "skip-manifest.json"
    artifacts_root = SCRATCH / "live-all-skip-artifacts"
    report_path = SCRATCH / "live-all-skip-report.json"
    shutil.rmtree(SCRATCH, ignore_errors=True)
    SCRATCH.mkdir()
    try:
        plan_path = SCRATCH / "release-plan.json"
        sets_path = SCRATCH / "execution-sets.json"
        plan_path.write_text(json.dumps(plan), encoding="utf-8")
        sets_path.write_text(json.dumps(execution_sets), encoding="utf-8")

        assert (
            control._cmd_matrix_outputs(
                control.argparse.Namespace(
                    plan=str(plan_path),
                    execution_sets=str(sets_path),
                    run_id=123,
                    attempt=4,
                    github_output=str(output),
                )
            )
            == 0
        )
        values = dict(
            line.split("=", 1)
            for line in output.read_text(encoding="utf-8").splitlines()
        )
        assert values["has_variants"] == "false"
        assert values["has_reusable_publish"] == "false"
        assert values["has_entry_publish"] == "false"
        assert values["has_active_github_release"] == "false"
        assert values["has_skip_results"] == "true"
        assert values["has_live_side_effects"] == "false"

        assert (
            control._cmd_skip_results(
                control.argparse.Namespace(
                    plan=str(plan_path),
                    execution_sets=str(sets_path),
                    run_id=123,
                    attempt=4,
                    out_dir=str(artifacts_root),
                    manifest_out=str(manifest),
                )
            )
            == 0
        )
        skip_names = json.loads(manifest.read_text(encoding="utf-8"))[
            "skip-result-artifact-names"
        ]
        assert len(skip_names) == 2
        for name in skip_names:
            skip_result = json.loads(
                (artifacts_root / name / "skip-result.json").read_text(
                    encoding="utf-8"
                )
            )
            validate_contract(skip_result)
            assert skip_result["outcome"] == "skip-satisfied"

        for artifact_value in (
            "plan_artifact_name",
            "execution_sets_artifact_name",
            "entry_publish_handoff_artifact_name",
        ):
            (artifacts_root / values[artifact_value]).mkdir(parents=True)
        assert (
            control._cmd_report(
                control.argparse.Namespace(
                    repository="hcoona/three",
                    workflow="Release Official",
                    run_id=123,
                    attempt=4,
                    head_sha=SHA_B,
                    profile="official",
                    dry_run="false",
                    validation_build="false",
                    canary_override_non_public_ref="true",
                    out=str(report_path),
                    plan=str(plan_path),
                    execution_sets=str(sets_path),
                    diagnostics="",
                    artifacts_root=str(artifacts_root),
                    authorize_conclusion="success",
                    validate_conclusion="success",
                    metadata_conclusion="success",
                    plan_conclusion="success",
                    build_conclusion="skipped",
                    tag_conclusion="skipped",
                    publish_conclusion="skipped",
                )
            )
            == 0
        )
        report = json.loads(report_path.read_text(encoding="utf-8"))
        validate_contract(report)
        assert report["run"]["canary-override-non-public-ref"] is True
        assert report["counts"]["active-variants"] == 0
        assert report["counts"]["active-publish-nodes"] == 0
        assert sorted(report["artifacts"]["skip-result-artifact-names"]) == (
            sorted(skip_names)
        )
        assert report["artifacts"]["build-result-artifact-names"] == []
        assert report["artifacts"]["tag-result-artifact-name"] is None
        assert report["artifacts"]["publish-result-artifact-names"] == []
        for output_name in (
            "build-result.json",
            "tag-result.json",
            "publish-request.json",
            "publish-result.json",
            "immutable-proof.json",
            "github-release-asset-proof.json",
        ):
            assert not (SCRATCH / output_name).exists()
    finally:
        shutil.rmtree(SCRATCH, ignore_errors=True)


def test_validation_build_acceptance_records_build_without_publish_proof() -> (
    None
):
    """Validation build evidence stops at build receipts, never proofs."""
    plan = _load("release-plan.json")
    execution_sets = _load("execution-sets.json")
    artifacts_root = SCRATCH / "validation-build-artifacts"
    matrix_output = SCRATCH / "validation-build-matrix-output.txt"
    report_path = SCRATCH / "validation-build-report.json"
    shutil.rmtree(SCRATCH, ignore_errors=True)
    SCRATCH.mkdir()
    try:
        plan_path = SCRATCH / "release-plan.json"
        sets_path = SCRATCH / "execution-sets.json"
        plan_path.write_text(json.dumps(plan), encoding="utf-8")
        sets_path.write_text(json.dumps(execution_sets), encoding="utf-8")
        assert (
            control._cmd_matrix_outputs(
                control.argparse.Namespace(
                    plan=str(plan_path),
                    execution_sets=str(sets_path),
                    run_id=123,
                    attempt=4,
                    github_output=str(matrix_output),
                )
            )
            == 0
        )
        matrix_values = dict(
            line.split("=", 1)
            for line in matrix_output.read_text(encoding="utf-8").splitlines()
        )
        for artifact_value in (
            "plan_artifact_name",
            "execution_sets_artifact_name",
            "entry_publish_handoff_artifact_name",
        ):
            (artifacts_root / matrix_values[artifact_value]).mkdir(parents=True)

        build_result_name = artifact_name(
            "build-result",
            ArtifactNameInputs(
                123,
                4,
                plan_id=plan["envelope"]["plan-id"],
                variant_id=execution_sets["active-variant-ids"][0],
            ),
        )
        build_result_dir = artifacts_root / build_result_name
        build_result_dir.mkdir(parents=True)
        shutil.copy2(FIXTURES / "build-result.json", build_result_dir)

        assert (
            control._cmd_report(
                control.argparse.Namespace(
                    repository="hcoona/three",
                    workflow="Release Official",
                    run_id=123,
                    attempt=4,
                    head_sha=SHA_B,
                    profile="official",
                    dry_run="true",
                    validation_build="true",
                    canary_override_non_public_ref="false",
                    out=str(report_path),
                    plan=str(plan_path),
                    execution_sets=str(sets_path),
                    diagnostics="",
                    artifacts_root=str(artifacts_root),
                    authorize_conclusion="success",
                    validate_conclusion="success",
                    metadata_conclusion="success",
                    plan_conclusion="success",
                    build_conclusion="success",
                    tag_conclusion="skipped",
                    publish_conclusion="skipped",
                )
            )
            == 0
        )
        report = json.loads(report_path.read_text(encoding="utf-8"))
        validate_contract(report)
        assert report["run"]["validation-build"] is True
        assert report["artifacts"]["build-result-artifact-names"] == [
            build_result_name
        ]
        assert report["artifacts"]["tag-result-artifact-name"] is None
        assert report["artifacts"]["publish-result-artifact-names"] == []
        assert report["artifacts"]["skip-result-artifact-names"] == []
        for output_name in (
            "publish-request.json",
            "publish-result.json",
            "immutable-proof.json",
            "github-release-asset-proof.json",
        ):
            assert not (SCRATCH / output_name).exists()
    finally:
        shutil.rmtree(SCRATCH, ignore_errors=True)


def test_validation_only_immutable_proof_rejection_emits_no_outputs() -> None:
    """Validation-only proof wrappers are rejected before reuse outputs."""
    scratch = SCRATCH / "validation-only-proof-rejection"
    plan = _load("release-plan.json")
    proof = deepcopy(_load("immutable-proof.json"))
    proof["run"]["validation-only"] = True
    remote_members = {
        "publish-node/nuget": [
            {
                "filename": "Example.1.2.3.nupkg",
                "sha256": (
                    "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"
                    "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"
                ),
            }
        ]
    }
    build_result_receipts = [
        {
            "build-result-artifact-name": ("release-build-result-v1-123-1-abc"),
            "build-result-artifact-id": 123,
            "build-result": _load("build-result.json"),
        }
    ]
    shutil.rmtree(scratch, ignore_errors=True)
    scratch.mkdir(parents=True)
    try:
        with pytest.raises(ProofError) as error:
            classify_immutable_observations(
                plan=plan,
                remote_members=remote_members,
                proofs=[proof],
                build_result_receipts=build_result_receipts,
            )

        assert error.value.code == "IMMUTABLE_PROOF_UNAVAILABLE"
        _assert_forbidden_outputs_absent(scratch)
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_live_external_oidc_enablement_routes_entry_publish() -> None:
    """Enabled official external OIDC targets reach the entry workflow path."""
    plan = deepcopy(_load("release-plan.json"))
    execution_sets = deepcopy(_load("execution-sets.json"))
    plan["envelope"]["profile"] = "official"
    execution_sets["active-publish-node-ids"] = ["publish-node/nuget"]
    execution_sets["publish-intent-node-ids"] = ["publish-node/nuget"]
    execution_sets["selected-github-release-publish-node-ids"] = []
    execution_sets["active-github-release-publish-node-ids"] = []
    execution_sets["skip-satisfied-publish-node-ids"] = []
    execution_sets["active-publish-selectors"]["github-token"] = []
    execution_sets["active-publish-selectors"][
        "external-oidc-entry-workflow"
    ] = ["publish-node/nuget"]
    snapshot = deepcopy(
        plan["graph"]["target-instance-snapshots"]["nuget/github-packages"]
    )
    snapshot["catalog-ref"] = "pypi/pypi"
    snapshot["contract"]["id"] = "pypi-publish"
    snapshot["destination"] = {"host": "pypi.org"}
    snapshot["family"] = "pypi"
    snapshot["instance-id"] = "pypi"
    snapshot["capabilities"]["credential-posture"] = "oidc"
    snapshot["capabilities"]["name-uniqueness-scope"] = "package-name"
    snapshot["capabilities"]["profile-coexistence-rule"] = (
        "requires-distinct-name"
    )
    snapshot["capabilities"]["publish-topology"] = (
        "external-oidc-entry-workflow"
    )
    snapshot["contract"] = deepcopy(
        plan["graph"]["target-instance-snapshots"]["github-release/public"][
            "contract"
        ]
    )
    snapshot["contract"]["id"] = "pypi-publish"
    snapshot["contract"]["allowed-artifact-tuples"] = [
        {
            "role": "primary-package",
            "kind-family": "package",
            "concrete-kind": "wheel",
        },
        {
            "role": "primary-package",
            "kind-family": "package",
            "concrete-kind": "sdist",
        },
    ]
    snapshot["contract"]["aggregate-rules"] = {
        "min-artifact-count": 1,
        "max-artifact-count": 2,
        "cross-variant-policy": "forbid",
        "tuple-rules": [
            {
                "role": "primary-package",
                "kind-family": "package",
                "concrete-kind": "wheel",
                "min-count": 1,
                "max-count": 1,
            },
            {
                "role": "primary-package",
                "kind-family": "package",
                "concrete-kind": "sdist",
                "min-count": 0,
                "max-count": 1,
            },
        ],
    }
    plan["graph"]["target-instance-snapshots"]["pypi/pypi"] = snapshot
    plan["graph"]["artifacts"]["artifact/package"]["concrete-kind"] = "wheel"
    node = plan["graph"]["publish-nodes"]["publish-node/nuget"]
    node["publish-disposition"] = "publish"
    node["publish-mode"] = "create-only"
    node["target-instance-snapshot-id"] = "pypi/pypi"
    output = SCRATCH / "entry-matrix-output.txt"
    shutil.rmtree(SCRATCH, ignore_errors=True)
    SCRATCH.mkdir()
    try:
        plan_path = SCRATCH / "release-plan.json"
        sets_path = SCRATCH / "execution-sets.json"
        plan_path.write_text(json.dumps(plan), encoding="utf-8")
        sets_path.write_text(json.dumps(execution_sets), encoding="utf-8")

        assert (
            control._cmd_matrix_outputs(
                control.argparse.Namespace(
                    plan=str(plan_path),
                    execution_sets=str(sets_path),
                    run_id=123,
                    attempt=4,
                    github_output=str(output),
                )
            )
            == 0
        )
        values = dict(
            line.split("=", 1)
            for line in output.read_text(encoding="utf-8").splitlines()
        )
        assert values["entry_publish_node_ids"] == '["publish-node/nuget"]'
        assert values["has_entry_publish"] == "true"
        assert values["reusable_publish_node_ids"] == "[]"
        assert values["has_reusable_publish"] == "false"
    finally:
        shutil.rmtree(SCRATCH, ignore_errors=True)


def test_download_publish_inputs_uses_handoff_declared_artifact_names(
    monkeypatch,
) -> None:
    """Entry-hosted publish downloads only the exact handoff build inputs."""
    plan = _load("release-plan.json")
    execution_sets = deepcopy(_load("execution-sets.json"))
    execution_sets["active-publish-selectors"]["github-token"] = []
    execution_sets["active-publish-selectors"][
        "external-oidc-entry-workflow"
    ] = ["publish-node/gh"]
    handoff = control._entry_publish_handoff(plan, execution_sets, 123, 4)
    calls = []
    monkeypatch.setattr(
        control,
        "_download_artifact",
        lambda repository, run_id, name, destination: calls.append(
            (repository, run_id, name, destination.as_posix())
        ),
    )
    shutil.rmtree(SCRATCH, ignore_errors=True)
    SCRATCH.mkdir()
    try:
        plan_path = SCRATCH / "release-plan.json"
        handoff_path = SCRATCH / "entry-publish-handoff.json"
        plan_path.write_text(json.dumps(plan), encoding="utf-8")
        handoff_path.write_text(json.dumps(handoff), encoding="utf-8")

        assert (
            control._cmd_download_publish_inputs(
                control.argparse.Namespace(
                    plan=str(plan_path),
                    handoff=str(handoff_path),
                    publish_node_id="publish-node/gh",
                    run_id=123,
                    attempt=4,
                    repository="hcoona/three",
                    build_results_dir=str(SCRATCH / "build-results"),
                    bundles_dir=str(SCRATCH / "bundles"),
                )
            )
            == 0
        )

        expected = handoff["publish-inputs-by-node-id"]["publish-node/gh"]
        assert [call[2] for call in calls] == (
            expected["build-result-artifact-names"]
            + expected["build-bundle-artifact-names"]
        )
        assert all(call[0] == "hcoona/three" for call in calls)
        assert all(call[1] == 123 for call in calls)
    finally:
        shutil.rmtree(SCRATCH, ignore_errors=True)


def test_ensure_tags_tolerates_missing_dry_run_publish_tags(
    monkeypatch,
) -> None:
    """Dry-run GitHub Release validation tolerates new publish tags."""
    plan = deepcopy(_load("release-plan.json"))
    execution_sets = deepcopy(_load("execution-sets.json"))
    execution_sets["dry-run"] = True
    execution_sets["active-github-release-publish-node-ids"] = []
    execution_sets["active-publish-node-ids"] = []
    execution_sets["active-publish-selectors"]["github-token"] = []
    out = SCRATCH / "tag-result.json"
    shutil.rmtree(SCRATCH, ignore_errors=True)
    SCRATCH.mkdir()
    monkeypatch.setattr(control, "_remote_tag_commit", lambda *_: None)
    gh_calls = []
    monkeypatch.setattr(
        control,
        "_gh_api",
        lambda *args, **kwargs: gh_calls.append((args, kwargs)),
    )
    try:
        (SCRATCH / "plan.json").write_text(json.dumps(plan), encoding="utf-8")
        (SCRATCH / "execution-sets.json").write_text(
            json.dumps(execution_sets), encoding="utf-8"
        )

        assert (
            control._cmd_ensure_tags(
                control.argparse.Namespace(
                    plan=str(SCRATCH / "plan.json"),
                    execution_sets=str(SCRATCH / "execution-sets.json"),
                    repository="hcoona/three",
                    out=str(out),
                )
            )
            == 0
        )

        result = json.loads(out.read_text(encoding="utf-8"))
        validate_contract(result)
        assert result["tags"] == []
        assert gh_calls == []
    finally:
        shutil.rmtree(SCRATCH, ignore_errors=True)


def test_observe_remote_publications_classifies_missing_github_release_absent(
    monkeypatch,
) -> None:
    """Missing GitHub Release publication writes explicit absent observation."""
    plan = deepcopy(_load("release-plan.json"))
    execution_sets = deepcopy(_load("execution-sets.json"))
    execution_sets["active-publish-node-ids"] = ["publish-node/gh"]
    execution_sets["publish-intent-node-ids"] = ["publish-node/gh"]
    out = SCRATCH / "remote-observations.json"
    diagnostics = SCRATCH / "planner-diagnostics.json"
    shutil.rmtree(SCRATCH, ignore_errors=True)
    SCRATCH.mkdir()
    monkeypatch.setattr(control, "_remote_tag_commit", lambda *_: None)
    monkeypatch.setattr(control, "_github_release_by_tag", lambda *_: None)
    try:
        (SCRATCH / "plan.json").write_text(json.dumps(plan), encoding="utf-8")
        (SCRATCH / "execution-sets.json").write_text(
            json.dumps(execution_sets), encoding="utf-8"
        )

        assert (
            control._cmd_observe_remote_publications(
                control.argparse.Namespace(
                    plan=str(SCRATCH / "plan.json"),
                    execution_sets=str(SCRATCH / "execution-sets.json"),
                    enabled_external_oidc_targets="",
                    repository="hcoona/three",
                    out=str(out),
                    diagnostics_out=str(diagnostics),
                )
            )
            == 0
        )

        observations = json.loads(out.read_text(encoding="utf-8"))
        assert observations == {"publish-node/gh": "absent"}
        assert not diagnostics.exists()
    finally:
        shutil.rmtree(SCRATCH, ignore_errors=True)


def test_observe_remote_publications_fails_closed_on_lookup_errors(
    monkeypatch,
) -> None:
    """Remote lookup errors become planner diagnostics instead of absent."""
    plan = deepcopy(_load("release-plan.json"))
    execution_sets = deepcopy(_load("execution-sets.json"))
    execution_sets["active-publish-node-ids"] = ["publish-node/gh"]
    execution_sets["publish-intent-node-ids"] = ["publish-node/gh"]
    out = SCRATCH / "remote-observations.json"
    diagnostics = SCRATCH / "planner-diagnostics.json"
    shutil.rmtree(SCRATCH, ignore_errors=True)
    SCRATCH.mkdir()
    monkeypatch.setattr(
        control,
        "_remote_tag_commit",
        lambda *_: (_ for _ in ()).throw(RuntimeError("HTTP 503")),
    )
    try:
        (SCRATCH / "plan.json").write_text(json.dumps(plan), encoding="utf-8")
        (SCRATCH / "execution-sets.json").write_text(
            json.dumps(execution_sets), encoding="utf-8"
        )

        assert (
            control._cmd_observe_remote_publications(
                control.argparse.Namespace(
                    plan=str(SCRATCH / "plan.json"),
                    execution_sets=str(SCRATCH / "execution-sets.json"),
                    enabled_external_oidc_targets="",
                    repository="hcoona/three",
                    out=str(out),
                    diagnostics_out=str(diagnostics),
                )
            )
            == 1
        )

        assert not out.exists()
        assert _diagnostic_codes(diagnostics) == [
            "REMOTE_CLASSIFICATION_FAILED"
        ]
    finally:
        shutil.rmtree(SCRATCH, ignore_errors=True)


def test_pypi_missing_package_observation_plans_first_publish(
    monkeypatch,
) -> None:
    """Missing PyPI project/version emits absent and passes live OIDC gate."""
    plan = _pypi_only_observation_plan()
    full_plan, execution_sets = _external_oidc_plan_and_sets()
    out = SCRATCH / "remote-observations.json"
    diagnostics = SCRATCH / "planner-diagnostics.json"
    shutil.rmtree(SCRATCH, ignore_errors=True)
    SCRATCH.mkdir()
    monkeypatch.setattr(control, "_pypi_project_json", lambda _: None)
    try:
        (SCRATCH / "plan.json").write_text(json.dumps(plan), encoding="utf-8")

        assert (
            control._cmd_observe_remote_publications(
                control.argparse.Namespace(
                    plan=str(SCRATCH / "plan.json"),
                    repository="hcoona/three",
                    out=str(out),
                    diagnostics_out=str(diagnostics),
                )
            )
            == 0
        )

        assert json.loads(out.read_text(encoding="utf-8")) == {
            "publish-node/nuget": "absent"
        }
        assert not diagnostics.exists()

        plan_path = SCRATCH / "full-plan.json"
        sets_path = SCRATCH / "execution-sets.json"
        gate_diagnostics = SCRATCH / "gate-diagnostics.json"
        plan_path.write_text(json.dumps(full_plan), encoding="utf-8")
        sets_path.write_text(json.dumps(execution_sets), encoding="utf-8")
        assert (
            control._cmd_plan_gate(
                control.argparse.Namespace(
                    plan=str(plan_path),
                    execution_sets=str(sets_path),
                    remote_observations=str(out),
                    enabled_external_oidc_targets="pypi/pypi#example#Example",
                    diagnostics_out=str(gate_diagnostics),
                )
            )
            == 0
        )
        assert not gate_diagnostics.exists()
    finally:
        shutil.rmtree(SCRATCH, ignore_errors=True)


def test_disabled_pypi_observation_skips_lookup_failure(monkeypatch) -> None:
    """Disabled PyPI OIDC targets are not queried during bootstrap.

    Observation failures for disabled nodes must not fail planning.
    """
    plan, execution_sets = _external_oidc_plan_and_sets()
    out = SCRATCH / "remote-observations.json"
    diagnostics = SCRATCH / "planner-diagnostics.json"
    shutil.rmtree(SCRATCH, ignore_errors=True)
    SCRATCH.mkdir()

    def fail_pypi(_: str) -> dict[str, object]:
        pytest.fail("disabled target must not be queried")

    monkeypatch.setattr(control, "_pypi_project_json", fail_pypi)
    try:
        (SCRATCH / "plan.json").write_text(json.dumps(plan), encoding="utf-8")
        (SCRATCH / "execution-sets.json").write_text(
            json.dumps(execution_sets), encoding="utf-8"
        )

        assert (
            control._cmd_observe_remote_publications(
                control.argparse.Namespace(
                    plan=str(SCRATCH / "plan.json"),
                    execution_sets=str(SCRATCH / "execution-sets.json"),
                    enabled_external_oidc_targets="",
                    repository="hcoona/three",
                    out=str(out),
                    diagnostics_out=str(diagnostics),
                )
            )
            == 0
        )

        observations = json.loads(out.read_text(encoding="utf-8"))
        assert observations == {"publish-node/gh": "absent"}
        assert "publish-node/nuget" not in observations
        assert not diagnostics.exists()
    finally:
        shutil.rmtree(SCRATCH, ignore_errors=True)


def test_inactive_pypi_observation_skips_lookup_failure(monkeypatch) -> None:
    """Inactive PyPI OIDC targets are not queried during bootstrap.

    Observation failures for inactive nodes must not fail planning.
    """
    plan, execution_sets = _external_oidc_plan_and_sets()
    execution_sets["publish-intent-node-ids"] = []
    execution_sets["active-publish-node-ids"] = []
    out = SCRATCH / "remote-observations.json"
    diagnostics = SCRATCH / "planner-diagnostics.json"
    shutil.rmtree(SCRATCH, ignore_errors=True)
    SCRATCH.mkdir()

    def fail_pypi(_: str) -> dict[str, object]:
        pytest.fail("inactive target must not be queried")

    monkeypatch.setattr(control, "_pypi_project_json", fail_pypi)
    try:
        (SCRATCH / "plan.json").write_text(json.dumps(plan), encoding="utf-8")
        (SCRATCH / "execution-sets.json").write_text(
            json.dumps(execution_sets), encoding="utf-8"
        )

        assert (
            control._cmd_observe_remote_publications(
                control.argparse.Namespace(
                    plan=str(SCRATCH / "plan.json"),
                    execution_sets=str(SCRATCH / "execution-sets.json"),
                    enabled_external_oidc_targets=("pypi/pypi#example#Example"),
                    repository="hcoona/three",
                    out=str(out),
                    diagnostics_out=str(diagnostics),
                )
            )
            == 0
        )

        observations = json.loads(out.read_text(encoding="utf-8"))
        assert observations == {"publish-node/gh": "absent"}
        assert "publish-node/nuget" not in observations
        assert not diagnostics.exists()
    finally:
        shutil.rmtree(SCRATCH, ignore_errors=True)


def test_enabled_pypi_gate_requires_observation() -> None:
    """Enabled PyPI OIDC targets require explicit observation evidence."""
    plan, execution_sets = _external_oidc_plan_and_sets()
    shutil.rmtree(SCRATCH, ignore_errors=True)
    SCRATCH.mkdir()
    try:
        (SCRATCH / "plan.json").write_text(json.dumps(plan), encoding="utf-8")
        (SCRATCH / "execution-sets.json").write_text(
            json.dumps(execution_sets), encoding="utf-8"
        )

        assert (
            control._cmd_plan_gate(
                control.argparse.Namespace(
                    plan=str(SCRATCH / "plan.json"),
                    execution_sets=str(SCRATCH / "execution-sets.json"),
                    remote_observations="",
                    enabled_external_oidc_targets=("pypi/pypi#example#Example"),
                    diagnostics_out=str(diagnostics := SCRATCH / "d.json"),
                )
            )
            == 1
        )
        assert _diagnostic_codes(diagnostics) == [
            "REMOTE_CLASSIFICATION_FAILED"
        ]
    finally:
        shutil.rmtree(SCRATCH, ignore_errors=True)


def test_pypi_existing_exact_version_observation_is_skip_satisfied(
    monkeypatch,
) -> None:
    """Existing PyPI package/version maps to exact-satisfied replay."""
    plan = _pypi_only_observation_plan()
    node = plan["graph"]["publish-nodes"]["publish-node/nuget"]
    monkeypatch.setattr(
        control,
        "_pypi_project_json",
        lambda _: {"releases": {"1.2.3": [{"filename": "example-1.2.3.whl"}]}},
    )

    assert control._observe_pypi_publication(node) == "exact-satisfied"


def test_pypi_missing_version_observation_is_absent(monkeypatch) -> None:
    """Existing PyPI project without requested version remains publishable."""
    plan = _pypi_only_observation_plan()
    node = plan["graph"]["publish-nodes"]["publish-node/nuget"]
    monkeypatch.setattr(
        control,
        "_pypi_project_json",
        lambda _: {"releases": {"9.9.9": [{"filename": "example-9.9.9.whl"}]}},
    )

    assert control._observe_pypi_publication(node) == "absent"


@pytest.mark.parametrize(
    ("family", "payload_attr", "missing_payload", "existing_payload"),
    [
        (
            "nuget",
            "_nuget_versions_json",
            None,
            {"versions": ["1.2.3"]},
        ),
        (
            "npm",
            "_npm_package_json",
            None,
            {"versions": {"1.2.3": {"name": "Example"}}},
        ),
        (
            "rubygems",
            "_rubygems_versions_json",
            None,
            [{"number": "1.2.3", "platform": "ruby"}],
        ),
    ],
)
def test_public_registry_missing_and_existing_observations(
    monkeypatch,
    family,
    payload_attr,
    missing_payload,
    existing_payload,
) -> None:
    """NuGet, npm, and RubyGems map absent/exact versions deterministically."""
    plan = _public_registry_observation_plan(family)
    node = plan["graph"]["publish-nodes"]["publish-node/nuget"]
    observe = getattr(control, f"_observe_{family}_publication")

    monkeypatch.setattr(control, payload_attr, lambda _: missing_payload)
    assert observe(node) == "absent"

    monkeypatch.setattr(control, payload_attr, lambda _: existing_payload)
    assert observe(node) == "exact-satisfied"


def test_github_packages_nuget_missing_version_and_existing_observations(
    monkeypatch,
) -> None:
    """Visible GitHub Packages NuGet versions map to absent/exact states."""
    plan = _github_packages_nuget_observation_plan()
    node = plan["graph"]["publish-nodes"]["publish-node/nuget"]
    observed_endpoints: list[str] = []

    def fake_gh_api(*args, **_kwargs):
        endpoint = args[1]
        observed_endpoints.append(endpoint)
        if endpoint == "repos/hcoona/three":
            return {"owner": {"login": "hcoona", "type": "Organization"}}
        if endpoint == "orgs/hcoona/packages/nuget/Example":
            return {"name": "Example"}
        raise AssertionError(endpoint)

    def missing_version_paginated(*args, **_kwargs):
        observed_endpoints.append(args[1])
        return [{"name": "9.9.9"}]

    monkeypatch.setattr(control, "_gh_api", fake_gh_api)
    monkeypatch.setattr(control, "_gh_api_paginated", missing_version_paginated)

    assert (
        control._observe_github_packages_nuget_publication(
            "hcoona/three",
            node,
            plan["graph"]["target-instance-snapshots"]["nuget/github-packages"],
        )
        == "absent"
    )
    assert observed_endpoints == [
        "repos/hcoona/three",
        "orgs/hcoona/packages/nuget/Example",
        "repos/hcoona/three",
        "orgs/hcoona/packages/nuget/Example/versions?per_page=100",
    ]

    def existing_paginated(*args, **_kwargs):
        endpoint = args[1]
        if (
            endpoint
            == "orgs/hcoona/packages/nuget/Example/versions?per_page=100"
        ):
            return [{"name": "1.2.3"}]
        raise AssertionError(endpoint)

    monkeypatch.setattr(control, "_gh_api_paginated", existing_paginated)

    assert (
        control._observe_github_packages_nuget_publication(
            "hcoona/three",
            node,
            plan["graph"]["target-instance-snapshots"]["nuget/github-packages"],
        )
        == "exact-satisfied"
    )


def test_github_packages_nuget_package_404_is_absent(
    monkeypatch,
    capsys,
) -> None:
    """GitHub Packages package 404 is classified as absent."""
    plan = _github_packages_nuget_observation_plan()
    out = SCRATCH / "remote-observations.json"
    diagnostics = SCRATCH / "planner-diagnostics.json"
    shutil.rmtree(SCRATCH, ignore_errors=True)
    SCRATCH.mkdir()

    def fake_gh_api(*args, **_kwargs):
        endpoint = args[1]
        if endpoint == "repos/hcoona/three":
            return {"owner": {"login": "hcoona", "type": "Organization"}}
        if endpoint == "orgs/hcoona/packages/nuget/Example":
            message = f"gh api failed for {endpoint}: HTTP 404"
            raise RuntimeError(message)
        raise AssertionError(endpoint)

    def fail_paginated(*args, **_kwargs):
        message = f"owner package listing must not be used: {args[1]}"
        raise AssertionError(message)

    monkeypatch.setattr(control, "_gh_api", fake_gh_api)
    monkeypatch.setattr(control, "_gh_api_paginated", fail_paginated)
    try:
        (SCRATCH / "plan.json").write_text(json.dumps(plan), encoding="utf-8")

        assert (
            control._cmd_observe_remote_publications(
                control.argparse.Namespace(
                    plan=str(SCRATCH / "plan.json"),
                    repository="hcoona/three",
                    out=str(out),
                    diagnostics_out=str(diagnostics),
                )
            )
            == 0
        )

        assert json.loads(out.read_text(encoding="utf-8")) == {
            "publish-node/nuget": "absent"
        }
        assert not diagnostics.exists()
        stderr = capsys.readouterr().err
        assert "GitHub Packages 404 treated as absent" in stderr
        assert "publish remains the authority" in stderr
    finally:
        shutil.rmtree(SCRATCH, ignore_errors=True)


@pytest.mark.parametrize(
    ("family", "host", "catalog_ref", "project_id", "package_name", "endpoint"),
    [
        (
            "npm",
            "npm.pkg.github.com",
            "npm/github-packages",
            "hexo-renderer-asciidoc",
            "@hcoona/hexo-renderer-asciidoc",
            "orgs/hcoona/packages/npm/%40hcoona%2Fhexo-renderer-asciidoc",
        ),
        (
            "rubygems",
            "rubygems.pkg.github.com",
            "rubygems/github-packages",
            "hcoona-release-smoke-rubygems",
            "hcoona-release-smoke-rubygems",
            "orgs/hcoona/packages/rubygems/hcoona-release-smoke-rubygems",
        ),
    ],
)
def test_github_packages_package_404_is_absent_for_supported_families(  # noqa: PLR0913
    monkeypatch,
    capsys,
    family,
    host,
    catalog_ref,
    project_id,
    package_name,
    endpoint,
) -> None:
    """GitHub Packages 404-as-absent is not limited to smoke packages."""
    plan = _github_packages_nuget_observation_plan()
    plan["envelope"]["profile"] = "buddy"
    snapshot = plan["graph"]["target-instance-snapshots"].pop(
        "nuget/github-packages"
    )
    snapshot["catalog-ref"] = catalog_ref
    snapshot["destination"]["host"] = host
    snapshot["family"] = family
    plan["graph"]["target-instance-snapshots"][catalog_ref] = snapshot
    node = plan["graph"]["publish-nodes"]["publish-node/nuget"]
    node["project-id"] = project_id
    node["target-instance-snapshot-id"] = catalog_ref
    node["resolved-publish-identity"]["package-name"] = package_name
    execution_sets = {
        "dry-run": False,
        "active-publish-node-ids": ["publish-node/nuget"],
        "publish-intent-node-ids": ["publish-node/nuget"],
    }
    out = SCRATCH / "remote-observations.json"
    diagnostics = SCRATCH / "planner-diagnostics.json"
    shutil.rmtree(SCRATCH, ignore_errors=True)
    SCRATCH.mkdir()

    def fake_gh_api(*args, **_kwargs):
        actual_endpoint = args[1]
        if actual_endpoint == "repos/hcoona/three":
            return {"owner": {"login": "hcoona", "type": "Organization"}}
        if actual_endpoint == endpoint:
            message = f"gh api failed for {actual_endpoint}: HTTP 404"
            raise RuntimeError(message)
        raise AssertionError(actual_endpoint)

    monkeypatch.setattr(control, "_gh_api", fake_gh_api)
    try:
        (SCRATCH / "plan.json").write_text(json.dumps(plan), encoding="utf-8")
        (SCRATCH / "execution-sets.json").write_text(
            json.dumps(execution_sets), encoding="utf-8"
        )

        assert (
            control._cmd_observe_remote_publications(
                control.argparse.Namespace(
                    plan=str(SCRATCH / "plan.json"),
                    execution_sets=str(SCRATCH / "execution-sets.json"),
                    enabled_external_oidc_targets="",
                    canary_override_non_public_ref="false",
                    release_environment="",
                    repository="hcoona/three",
                    out=str(out),
                    diagnostics_out=str(diagnostics),
                )
            )
            == 0
        )

        assert json.loads(out.read_text(encoding="utf-8")) == {
            "publish-node/nuget": "absent"
        }
        assert not diagnostics.exists()
        assert (
            "GitHub Packages 404 treated as absent" in capsys.readouterr().err
        )
    finally:
        shutil.rmtree(SCRATCH, ignore_errors=True)


@pytest.mark.parametrize("status", [401, 403])
def test_github_packages_nuget_observation_keeps_auth_errors_fail_closed(
    monkeypatch,
    status,
) -> None:
    """GitHub Packages observation never downgrades non-404 errors."""
    plan = _github_packages_nuget_observation_plan()
    plan["envelope"]["profile"] = "official"
    node = plan["graph"]["publish-nodes"]["publish-node/nuget"]
    node["project-id"] = "hcoona-release-smoke-github-packages"
    node["resolved-publish-identity"]["package-name"] = (
        "Hcoona.ReleaseSmoke.GithubPackages"
    )
    execution_sets = {
        "dry-run": False,
        "active-publish-node-ids": ["publish-node/nuget"],
        "publish-intent-node-ids": ["publish-node/nuget"],
    }
    out = SCRATCH / "remote-observations.json"
    diagnostics = SCRATCH / "planner-diagnostics.json"
    shutil.rmtree(SCRATCH, ignore_errors=True)
    SCRATCH.mkdir()

    def fail_gh_api(*args, **_kwargs):
        endpoint = args[1]
        if endpoint == "repos/hcoona/three":
            return {"owner": {"login": "hcoona", "type": "Organization"}}
        message = f"gh api failed for {endpoint}: HTTP {status}"
        raise RuntimeError(message)

    monkeypatch.setattr(control, "_gh_api", fail_gh_api)
    try:
        (SCRATCH / "plan.json").write_text(json.dumps(plan), encoding="utf-8")
        (SCRATCH / "execution-sets.json").write_text(
            json.dumps(execution_sets), encoding="utf-8"
        )

        assert (
            control._cmd_observe_remote_publications(
                control.argparse.Namespace(
                    plan=str(SCRATCH / "plan.json"),
                    execution_sets=str(SCRATCH / "execution-sets.json"),
                    enabled_external_oidc_targets="",
                    canary_override_non_public_ref="true",
                    release_environment="release",
                    repository="hcoona/three",
                    out=str(out),
                    diagnostics_out=str(diagnostics),
                )
            )
            == 1
        )

        assert not out.exists()
        document = json.loads(diagnostics.read_text(encoding="utf-8"))
        assert [item["code"] for item in document["diagnostics"]] == [
            "REMOTE_CLASSIFICATION_FAILED"
        ]
        assert (
            f"HTTP {status}" in document["diagnostics"][0]["details"]["error"]
        )
    finally:
        shutil.rmtree(SCRATCH, ignore_errors=True)


def test_github_packages_nuget_api_failure_observation_fails_closed(
    monkeypatch,
) -> None:
    """GitHub Packages API failures become clear fail-closed diagnostics."""
    plan = _github_packages_nuget_observation_plan()
    out = SCRATCH / "remote-observations.json"
    diagnostics = SCRATCH / "planner-diagnostics.json"
    shutil.rmtree(SCRATCH, ignore_errors=True)
    SCRATCH.mkdir()

    def fail_gh_api(*_args, **_kwargs):
        message = "gh api failed for repos/hcoona/three: HTTP 403"
        raise RuntimeError(message)

    monkeypatch.setattr(control, "_gh_api", fail_gh_api)
    try:
        (SCRATCH / "plan.json").write_text(json.dumps(plan), encoding="utf-8")

        assert (
            control._cmd_observe_remote_publications(
                control.argparse.Namespace(
                    plan=str(SCRATCH / "plan.json"),
                    repository="hcoona/three",
                    out=str(out),
                    diagnostics_out=str(diagnostics),
                )
            )
            == 1
        )

        assert not out.exists()
        document = json.loads(diagnostics.read_text(encoding="utf-8"))
        assert [item["code"] for item in document["diagnostics"]] == [
            "REMOTE_CLASSIFICATION_FAILED"
        ]
        assert "gh api failed" in document["diagnostics"][0]["details"]["error"]
    finally:
        shutil.rmtree(SCRATCH, ignore_errors=True)


def test_inactive_github_packages_nuget_observation_skips_lookup_failure(
    monkeypatch,
) -> None:
    """Inactive GitHub Packages NuGet nodes are not queried."""
    plan = _github_packages_nuget_observation_plan()
    _, execution_sets = _external_oidc_plan_and_sets()
    execution_sets["active-publish-node-ids"] = []
    execution_sets["publish-intent-node-ids"] = []
    out = SCRATCH / "remote-observations.json"
    diagnostics = SCRATCH / "planner-diagnostics.json"
    shutil.rmtree(SCRATCH, ignore_errors=True)
    SCRATCH.mkdir()

    def fail_observer(*_) -> str:
        pytest.fail("inactive GitHub Packages target must not be queried")

    monkeypatch.setattr(
        control, "_observe_github_packages_nuget_publication", fail_observer
    )
    try:
        (SCRATCH / "plan.json").write_text(json.dumps(plan), encoding="utf-8")
        (SCRATCH / "execution-sets.json").write_text(
            json.dumps(execution_sets), encoding="utf-8"
        )

        assert (
            control._cmd_observe_remote_publications(
                control.argparse.Namespace(
                    plan=str(SCRATCH / "plan.json"),
                    execution_sets=str(SCRATCH / "execution-sets.json"),
                    enabled_external_oidc_targets="",
                    repository="hcoona/three",
                    out=str(out),
                    diagnostics_out=str(diagnostics),
                )
            )
            == 0
        )

        assert json.loads(out.read_text(encoding="utf-8")) == {}
        assert not diagnostics.exists()
    finally:
        shutil.rmtree(SCRATCH, ignore_errors=True)


@pytest.mark.parametrize(
    ("planned", "remote_versions"),
    [
        ("1", ["1.0.0"]),
        ("1.0", ["1.0.0"]),
        ("1.0.0.0", ["1.0.0"]),
        ("01.002.0003", ["1.2.3"]),
        ("1.0.7+r3456", ["1.0.7"]),
        ("1.0.7-alpha.BETA", ["1.0.7-ALPHA.beta"]),
    ],
)
def test_nuget_observation_uses_normalized_version_identity(
    monkeypatch,
    planned,
    remote_versions,
) -> None:
    """NuGet flat-container observation compares normalized identities."""
    plan = _public_registry_observation_plan("nuget")
    node = plan["graph"]["publish-nodes"]["publish-node/nuget"]
    node["resolved-publish-identity"]["version"] = planned
    monkeypatch.setattr(
        control,
        "_nuget_versions_json",
        lambda _: {"versions": remote_versions},
    )

    assert control._observe_nuget_publication(node) == "exact-satisfied"


@pytest.mark.parametrize(
    ("planned", "remote_versions"),
    [
        ("1.0.0-alpha.01", ["1.0.0-alpha.1"]),
        ("1.0.0+abc", ["1.0.1"]),
    ],
)
def test_nuget_observation_preserves_non_identity_differences(
    monkeypatch,
    planned,
    remote_versions,
) -> None:
    """NuGet observation preserves prerelease identifiers and releases."""
    plan = _public_registry_observation_plan("nuget")
    node = plan["graph"]["publish-nodes"]["publish-node/nuget"]
    node["resolved-publish-identity"]["version"] = planned
    monkeypatch.setattr(
        control,
        "_nuget_versions_json",
        lambda _: {"versions": remote_versions},
    )

    assert control._observe_nuget_publication(node) == "absent"


@pytest.mark.parametrize(
    ("planned", "remote_versions"),
    [
        ("1.0..0", ["1.0.0"]),
        ("1.0.0+", ["1.0.0"]),
        ("1.0.0-", ["1.0.0"]),
        ("1.0.0", ["1.0..0"]),
    ],
)
def test_nuget_invalid_version_observation_fails_closed(
    monkeypatch,
    planned,
    remote_versions,
) -> None:
    """Invalid or ambiguous NuGet versions fail closed instead of absent."""
    plan = _public_registry_observation_plan("nuget")
    node = plan["graph"]["publish-nodes"]["publish-node/nuget"]
    node["resolved-publish-identity"]["version"] = planned
    monkeypatch.setattr(
        control,
        "_nuget_versions_json",
        lambda _: {"versions": remote_versions},
    )

    with pytest.raises(ValueError, match="NuGet version"):
        control._observe_nuget_publication(node)


def test_nuget_invalid_planned_version_fails_closed_before_absent_remote(
    monkeypatch,
) -> None:
    """Invalid planned NuGet versions fail closed even when remote is absent."""
    plan = _public_registry_observation_plan("nuget")
    node = plan["graph"]["publish-nodes"]["publish-node/nuget"]
    node["resolved-publish-identity"]["version"] = "1.0..0"
    monkeypatch.setattr(control, "_nuget_versions_json", lambda _: None)

    with pytest.raises(ValueError, match="NuGet version"):
        control._observe_nuget_publication(node)


@pytest.mark.parametrize(
    ("family", "payload_attr", "existing_payload"),
    [
        ("nuget", "_nuget_versions_json", {"versions": ["9.9.9"]}),
        ("npm", "_npm_package_json", {"versions": {"9.9.9": {}}}),
        (
            "rubygems",
            "_rubygems_versions_json",
            [{"number": "9.9.9", "platform": "ruby"}],
        ),
    ],
)
def test_public_registry_missing_version_observation_is_absent(
    monkeypatch,
    family,
    payload_attr,
    existing_payload,
) -> None:
    """Existing public registry package without requested version is absent."""
    plan = _public_registry_observation_plan(family)
    node = plan["graph"]["publish-nodes"]["publish-node/nuget"]
    observe = getattr(control, f"_observe_{family}_publication")

    monkeypatch.setattr(control, payload_attr, lambda _: existing_payload)

    assert observe(node) == "absent"


@pytest.mark.parametrize(
    ("family", "payload_attr", "malformed_payload"),
    [
        ("nuget", "_nuget_versions_json", {"versions": [1]}),
        ("nuget", "_nuget_versions_json", {"versions": ["1.0..0"]}),
        ("npm", "_npm_package_json", {"versions": []}),
        ("rubygems", "_rubygems_versions_json", [{"created_at": "today"}]),
    ],
)
def test_public_registry_malformed_observation_fails_closed(
    monkeypatch,
    family,
    payload_attr,
    malformed_payload,
) -> None:
    """Partial or ambiguous public registry payloads fail closed."""
    plan = _public_registry_observation_plan(family)
    out = SCRATCH / "remote-observations.json"
    diagnostics = SCRATCH / "planner-diagnostics.json"
    shutil.rmtree(SCRATCH, ignore_errors=True)
    SCRATCH.mkdir()
    monkeypatch.setattr(control, payload_attr, lambda _: malformed_payload)
    try:
        (SCRATCH / "plan.json").write_text(json.dumps(plan), encoding="utf-8")

        assert (
            control._cmd_observe_remote_publications(
                control.argparse.Namespace(
                    plan=str(SCRATCH / "plan.json"),
                    repository="hcoona/three",
                    out=str(out),
                    diagnostics_out=str(diagnostics),
                )
            )
            == 1
        )

        assert not out.exists()
        document = json.loads(diagnostics.read_text(encoding="utf-8"))
        assert [item["code"] for item in document["diagnostics"]] == [
            "REMOTE_CLASSIFICATION_FAILED"
        ]
    finally:
        shutil.rmtree(SCRATCH, ignore_errors=True)


@pytest.mark.parametrize(
    ("family", "enablement"),
    [
        ("nuget", "nuget/nuget-org#example#Example"),
        ("npm", "npm/npmjs#example#Example"),
        ("rubygems", "rubygems/rubygems-org#example#Example"),
    ],
)
def test_disabled_and_inactive_public_registry_observation_skips_lookup_failure(
    monkeypatch,
    family,
    enablement,
) -> None:
    """Disabled or inactive public registry OIDC targets are not queried."""
    plan = _public_registry_observation_plan(family)
    _, execution_sets = _external_oidc_plan_and_sets()
    out = SCRATCH / "remote-observations.json"
    diagnostics = SCRATCH / "planner-diagnostics.json"
    shutil.rmtree(SCRATCH, ignore_errors=True)
    SCRATCH.mkdir()

    def fail_observer(*_) -> str:
        pytest.fail("disabled or inactive target must not be queried")

    monkeypatch.setattr(
        control, "_observe_public_registry_publication", fail_observer
    )
    try:
        plan_path = SCRATCH / "plan.json"
        sets_path = SCRATCH / "execution-sets.json"
        plan_path.write_text(json.dumps(plan), encoding="utf-8")
        sets_path.write_text(json.dumps(execution_sets), encoding="utf-8")

        assert (
            control._cmd_observe_remote_publications(
                control.argparse.Namespace(
                    plan=str(plan_path),
                    execution_sets=str(sets_path),
                    enabled_external_oidc_targets="",
                    repository="hcoona/three",
                    out=str(out),
                    diagnostics_out=str(diagnostics),
                )
            )
            == 0
        )
        assert json.loads(out.read_text(encoding="utf-8")) == {}

        out.unlink()
        execution_sets["active-publish-node-ids"] = []
        execution_sets["publish-intent-node-ids"] = []
        sets_path.write_text(json.dumps(execution_sets), encoding="utf-8")
        assert (
            control._cmd_observe_remote_publications(
                control.argparse.Namespace(
                    plan=str(plan_path),
                    execution_sets=str(sets_path),
                    enabled_external_oidc_targets=enablement,
                    repository="hcoona/three",
                    out=str(out),
                    diagnostics_out=str(diagnostics),
                )
            )
            == 0
        )
        assert json.loads(out.read_text(encoding="utf-8")) == {}
        assert not diagnostics.exists()
    finally:
        shutil.rmtree(SCRATCH, ignore_errors=True)


def test_pypi_api_failure_observation_fails_closed(monkeypatch) -> None:
    """PyPI API failures become clear fail-closed diagnostics."""
    plan = _pypi_only_observation_plan()
    out = SCRATCH / "remote-observations.json"
    diagnostics = SCRATCH / "planner-diagnostics.json"
    shutil.rmtree(SCRATCH, ignore_errors=True)
    SCRATCH.mkdir()

    def fail_pypi(_: str) -> dict[str, object]:
        message = "PyPI JSON API request failed for package 'example': HTTP 503"
        raise RuntimeError(message)

    monkeypatch.setattr(control, "_pypi_project_json", fail_pypi)
    try:
        (SCRATCH / "plan.json").write_text(json.dumps(plan), encoding="utf-8")

        assert (
            control._cmd_observe_remote_publications(
                control.argparse.Namespace(
                    plan=str(SCRATCH / "plan.json"),
                    repository="hcoona/three",
                    out=str(out),
                    diagnostics_out=str(diagnostics),
                )
            )
            == 1
        )

        assert not out.exists()
        document = json.loads(diagnostics.read_text(encoding="utf-8"))
        assert [item["code"] for item in document["diagnostics"]] == [
            "REMOTE_CLASSIFICATION_FAILED"
        ]
        assert (
            "PyPI JSON API request failed"
            in document["diagnostics"][0]["details"]["error"]
        )
    finally:
        shutil.rmtree(SCRATCH, ignore_errors=True)


@pytest.mark.parametrize(
    ("urlopen_result", "expected_error"),
    [
        (TimeoutError("timed out"), "timed out"),
        (OSError("socket closed"), "socket closed"),
    ],
)
def test_pypi_network_errors_observation_fails_closed(
    monkeypatch,
    urlopen_result,
    expected_error,
) -> None:
    """PyPI socket failures become planner diagnostics instead of tracebacks."""
    plan = _pypi_only_observation_plan()
    out = SCRATCH / "remote-observations.json"
    diagnostics = SCRATCH / "planner-diagnostics.json"
    shutil.rmtree(SCRATCH, ignore_errors=True)
    SCRATCH.mkdir()

    def fail_urlopen(*_, **__):
        raise urlopen_result

    monkeypatch.setattr(control.urllib.request, "urlopen", fail_urlopen)
    try:
        (SCRATCH / "plan.json").write_text(json.dumps(plan), encoding="utf-8")

        assert (
            control._cmd_observe_remote_publications(
                control.argparse.Namespace(
                    plan=str(SCRATCH / "plan.json"),
                    repository="hcoona/three",
                    out=str(out),
                    diagnostics_out=str(diagnostics),
                )
            )
            == 1
        )

        assert not out.exists()
        document = json.loads(diagnostics.read_text(encoding="utf-8"))
        assert [item["code"] for item in document["diagnostics"]] == [
            "REMOTE_CLASSIFICATION_FAILED"
        ]
        assert (
            "PyPI JSON API request failed"
            in document["diagnostics"][0]["details"]["error"]
        )
        assert expected_error in document["diagnostics"][0]["details"]["error"]
    finally:
        shutil.rmtree(SCRATCH, ignore_errors=True)


def test_pypi_response_read_error_observation_fails_closed(
    monkeypatch,
) -> None:
    """PyPI protocol read failures use the planner diagnostic path."""
    plan = _pypi_only_observation_plan()
    out = SCRATCH / "remote-observations.json"
    diagnostics = SCRATCH / "planner-diagnostics.json"
    shutil.rmtree(SCRATCH, ignore_errors=True)
    SCRATCH.mkdir()

    class FailingReadResponse:
        def __enter__(self):
            return self

        def __exit__(self, *_):
            return False

        def getcode(self):
            return 200

        def read(self):
            partial = b"partial"
            raise control.http.client.IncompleteRead(partial)

    monkeypatch.setattr(
        control.urllib.request,
        "urlopen",
        lambda *_, **__: FailingReadResponse(),
    )
    try:
        (SCRATCH / "plan.json").write_text(json.dumps(plan), encoding="utf-8")

        assert (
            control._cmd_observe_remote_publications(
                control.argparse.Namespace(
                    plan=str(SCRATCH / "plan.json"),
                    repository="hcoona/three",
                    out=str(out),
                    diagnostics_out=str(diagnostics),
                )
            )
            == 1
        )

        assert not out.exists()
        document = json.loads(diagnostics.read_text(encoding="utf-8"))
        assert [item["code"] for item in document["diagnostics"]] == [
            "REMOTE_CLASSIFICATION_FAILED"
        ]
        assert (
            "PyPI JSON API request failed"
            in document["diagnostics"][0]["details"]["error"]
        )
        assert (
            "IncompleteRead" in document["diagnostics"][0]["details"]["error"]
        )
    finally:
        shutil.rmtree(SCRATCH, ignore_errors=True)


def test_github_release_observation_preserves_partial_and_conflicting_states(
    monkeypatch,
) -> None:
    """Existing partial/conflicting GitHub Release states are not absent."""
    plan = deepcopy(_load("release-plan.json"))
    node = plan["graph"]["publish-nodes"]["publish-node/gh"]
    monkeypatch.setattr(control, "_remote_tag_commit", lambda *_: SHA_B)

    assert (
        control._observe_github_release_publication("hcoona/three", SHA_C, node)
        == "conflicting"
    )

    monkeypatch.setattr(control, "_remote_tag_commit", lambda *_: SHA_C)
    monkeypatch.setattr(
        control,
        "_github_release_by_tag",
        lambda *_: {
            "prerelease": True,
            "assets": [{"name": "Example.1.2.3.nupkg"}],
        },
    )

    assert (
        control._observe_github_release_publication("hcoona/three", SHA_C, node)
        == "partial"
    )


def test_ensure_tags_fails_missing_skip_satisfied_tags(monkeypatch) -> None:
    """Skip-satisfied GitHub Release nodes still require an existing tag."""
    plan = deepcopy(_load("release-plan.json"))
    execution_sets = deepcopy(_load("execution-sets.json"))
    plan["graph"]["publish-nodes"]["publish-node/gh"]["publish-disposition"] = (
        "skip-satisfied"
    )
    execution_sets["dry-run"] = True
    execution_sets["active-github-release-publish-node-ids"] = []
    execution_sets["active-publish-node-ids"] = []
    execution_sets["active-publish-selectors"]["github-token"] = []
    execution_sets["skip-satisfied-publish-node-ids"] = ["publish-node/gh"]
    shutil.rmtree(SCRATCH, ignore_errors=True)
    SCRATCH.mkdir()
    monkeypatch.setattr(control, "_remote_tag_commit", lambda *_: None)
    try:
        (SCRATCH / "plan.json").write_text(json.dumps(plan), encoding="utf-8")
        (SCRATCH / "execution-sets.json").write_text(
            json.dumps(execution_sets), encoding="utf-8"
        )

        with pytest.raises(RuntimeError, match="missing"):
            control._cmd_ensure_tags(
                control.argparse.Namespace(
                    plan=str(SCRATCH / "plan.json"),
                    execution_sets=str(SCRATCH / "execution-sets.json"),
                    repository="hcoona/three",
                    out=str(SCRATCH / "tag-result.json"),
                )
            )
    finally:
        shutil.rmtree(SCRATCH, ignore_errors=True)


def test_ensure_tags_fails_mixed_missing_skip_satisfied_tag(
    monkeypatch,
) -> None:
    """Skip-satisfied same-tag nodes block active tag creation when missing."""
    plan = deepcopy(_load("release-plan.json"))
    execution_sets = deepcopy(_load("execution-sets.json"))
    skip_node_id = "publish-node/gh-skip"
    plan["graph"]["publish-nodes"][skip_node_id] = deepcopy(
        plan["graph"]["publish-nodes"]["publish-node/gh"]
    )
    plan["graph"]["publish-nodes"][skip_node_id]["publish-node-id"] = (
        skip_node_id
    )
    plan["graph"]["publish-nodes"][skip_node_id]["publish-disposition"] = (
        "skip-satisfied"
    )
    execution_sets["selected-github-release-publish-node-ids"].append(
        skip_node_id
    )
    execution_sets["skip-satisfied-publish-node-ids"] = [skip_node_id]
    gh_calls = []
    shutil.rmtree(SCRATCH, ignore_errors=True)
    SCRATCH.mkdir()
    monkeypatch.setattr(control, "_remote_tag_commit", lambda *_: None)
    monkeypatch.setattr(
        control,
        "_gh_api",
        lambda *args, **kwargs: gh_calls.append((args, kwargs)),
    )
    try:
        (SCRATCH / "plan.json").write_text(json.dumps(plan), encoding="utf-8")
        (SCRATCH / "execution-sets.json").write_text(
            json.dumps(execution_sets), encoding="utf-8"
        )

        with pytest.raises(RuntimeError, match="skip-satisfied"):
            control._cmd_ensure_tags(
                control.argparse.Namespace(
                    plan=str(SCRATCH / "plan.json"),
                    execution_sets=str(SCRATCH / "execution-sets.json"),
                    repository="hcoona/three",
                    out=str(SCRATCH / "tag-result.json"),
                )
            )
        assert gh_calls == []
    finally:
        shutil.rmtree(SCRATCH, ignore_errors=True)


def test_ensure_tags_fails_non_missing_tag_lookup_errors(monkeypatch) -> None:
    """Non-404 tag lookup failures are not treated as missing tags."""
    plan = deepcopy(_load("release-plan.json"))
    execution_sets = deepcopy(_load("execution-sets.json"))
    gh_calls = []
    shutil.rmtree(SCRATCH, ignore_errors=True)
    SCRATCH.mkdir()
    monkeypatch.setattr(
        control,
        "_remote_tag_commit",
        lambda *_: (_ for _ in ()).throw(RuntimeError("HTTP 409")),
    )
    monkeypatch.setattr(
        control,
        "_gh_api",
        lambda *args, **kwargs: gh_calls.append((args, kwargs)),
    )
    try:
        (SCRATCH / "plan.json").write_text(json.dumps(plan), encoding="utf-8")
        (SCRATCH / "execution-sets.json").write_text(
            json.dumps(execution_sets), encoding="utf-8"
        )

        with pytest.raises(RuntimeError, match="409"):
            control._cmd_ensure_tags(
                control.argparse.Namespace(
                    plan=str(SCRATCH / "plan.json"),
                    execution_sets=str(SCRATCH / "execution-sets.json"),
                    repository="hcoona/three",
                    out=str(SCRATCH / "tag-result.json"),
                )
            )
        assert gh_calls == []
    finally:
        shutil.rmtree(SCRATCH, ignore_errors=True)


def test_remote_tag_commit_only_treats_404_as_missing(monkeypatch) -> None:
    """Only GitHub get-ref 404s are converted to missing tags."""
    monkeypatch.setattr(
        control,
        "_gh_api",
        lambda *_: (_ for _ in ()).throw(RuntimeError("HTTP 404")),
    )

    assert control._remote_tag_commit("hcoona/three", "missing") is None

    monkeypatch.setattr(
        control,
        "_gh_api",
        lambda *_: (_ for _ in ()).throw(RuntimeError("HTTP 409")),
    )
    with pytest.raises(RuntimeError, match="409"):
        control._remote_tag_commit("hcoona/three", "conflict")


def test_remote_tag_commit_fails_unpeelable_existing_refs(monkeypatch) -> None:
    """Existing non-commit refs must not be treated as missing tags."""

    def fake_gh_api(_: str, endpoint: str) -> dict[str, object]:
        if "/git/ref/tags/tree-tag" in endpoint:
            return {"object": {"type": "tree", "sha": SHA_B}}
        if "/git/ref/tags/annotated-tree" in endpoint:
            return {"object": {"type": "tag", "sha": "tag-sha"}}
        if "/git/tags/tag-sha" in endpoint:
            return {"object": {"type": "tree", "sha": SHA_C}}
        raise AssertionError(endpoint)

    monkeypatch.setattr(control, "_gh_api", fake_gh_api)

    with pytest.raises(RuntimeError, match="unsupported object type"):
        control._remote_tag_commit("hcoona/three", "tree-tag")
    with pytest.raises(RuntimeError, match="cannot be peeled"):
        control._remote_tag_commit("hcoona/three", "annotated-tree")


def test_ensure_tags_fails_existing_tag_conflicts_in_dry_run(
    monkeypatch,
) -> None:
    """Dry-run GitHub Release validation still rejects retargeting conflicts."""
    plan = deepcopy(_load("release-plan.json"))
    execution_sets = deepcopy(_load("execution-sets.json"))
    execution_sets["dry-run"] = True
    execution_sets["active-github-release-publish-node-ids"] = []
    execution_sets["active-publish-node-ids"] = []
    execution_sets["active-publish-selectors"]["github-token"] = []
    shutil.rmtree(SCRATCH, ignore_errors=True)
    SCRATCH.mkdir()
    monkeypatch.setattr(control, "_remote_tag_commit", lambda *_: SHA_B)
    try:
        (SCRATCH / "plan.json").write_text(json.dumps(plan), encoding="utf-8")
        (SCRATCH / "execution-sets.json").write_text(
            json.dumps(execution_sets), encoding="utf-8"
        )

        with pytest.raises(RuntimeError, match="points to"):
            control._cmd_ensure_tags(
                control.argparse.Namespace(
                    plan=str(SCRATCH / "plan.json"),
                    execution_sets=str(SCRATCH / "execution-sets.json"),
                    repository="hcoona/three",
                    out=str(SCRATCH / "tag-result.json"),
                )
            )
        for output_name in (
            "tag-result.json",
            "publish-request.json",
            "publish-result.json",
            "skip-result.json",
            "immutable-proof.json",
            "github-release-asset-proof.json",
        ):
            assert not (SCRATCH / output_name).exists()
    finally:
        shutil.rmtree(SCRATCH, ignore_errors=True)


def test_ensure_tags_is_atomic_for_mixed_missing_and_conflicting_tags(
    monkeypatch,
) -> None:
    """A later conflict prevents earlier active tag creation."""
    plan = deepcopy(_load("release-plan.json"))
    execution_sets = deepcopy(_load("execution-sets.json"))
    missing_node_id = "publish-node/gh-missing"
    conflict_node_id = "publish-node/gh-conflict"
    plan["graph"]["publish-nodes"][missing_node_id] = deepcopy(
        plan["graph"]["publish-nodes"]["publish-node/gh"]
    )
    plan["graph"]["publish-nodes"][missing_node_id]["publish-node-id"] = (
        missing_node_id
    )
    plan["graph"]["publish-nodes"][missing_node_id][
        "resolved-publish-identity"
    ] = {"release-tag": "release/example/aaa-missing"}
    plan["graph"]["publish-nodes"][conflict_node_id] = deepcopy(
        plan["graph"]["publish-nodes"]["publish-node/gh"]
    )
    plan["graph"]["publish-nodes"][conflict_node_id]["publish-node-id"] = (
        conflict_node_id
    )
    plan["graph"]["publish-nodes"][conflict_node_id][
        "resolved-publish-identity"
    ] = {"release-tag": "release/example/zzz-conflict"}
    execution_sets["selected-github-release-publish-node-ids"] = [
        missing_node_id,
        conflict_node_id,
    ]
    execution_sets["active-github-release-publish-node-ids"] = [
        missing_node_id,
        conflict_node_id,
    ]
    execution_sets["active-publish-node-ids"] = [
        missing_node_id,
        conflict_node_id,
    ]
    execution_sets["publish-intent-node-ids"] = [
        missing_node_id,
        conflict_node_id,
    ]
    execution_sets["active-publish-selectors"]["github-token"] = [
        missing_node_id,
        conflict_node_id,
    ]
    execution_sets["skip-satisfied-publish-node-ids"] = []
    gh_calls = []

    def fake_remote_tag_commit(_repository: str, tag: str) -> str | None:
        if tag.endswith("-missing"):
            return None
        if tag.endswith("-conflict"):
            return SHA_C
        raise AssertionError(tag)

    shutil.rmtree(SCRATCH, ignore_errors=True)
    SCRATCH.mkdir()
    monkeypatch.setattr(control, "_remote_tag_commit", fake_remote_tag_commit)
    monkeypatch.setattr(
        control,
        "_gh_api",
        lambda *args, **kwargs: gh_calls.append((args, kwargs)),
    )
    try:
        (SCRATCH / "plan.json").write_text(json.dumps(plan), encoding="utf-8")
        (SCRATCH / "execution-sets.json").write_text(
            json.dumps(execution_sets),
            encoding="utf-8",
        )

        with pytest.raises(RuntimeError, match="points to"):
            control._cmd_ensure_tags(
                control.argparse.Namespace(
                    plan=str(SCRATCH / "plan.json"),
                    execution_sets=str(SCRATCH / "execution-sets.json"),
                    repository="hcoona/three",
                    out=str(SCRATCH / "tag-result.json"),
                )
            )
        assert gh_calls == []
        for output_name in (
            "tag-result.json",
            "publish-request.json",
            "publish-result.json",
            "skip-result.json",
            "immutable-proof.json",
            "github-release-asset-proof.json",
        ):
            assert not (SCRATCH / output_name).exists()
    finally:
        shutil.rmtree(SCRATCH, ignore_errors=True)


def test_entry_publish_handoff_is_closed_for_empty_selectors() -> None:
    """Entry publish handoff is present even when no entry selectors exist."""
    plan = _load("release-plan.json")
    execution_sets = _load("execution-sets.json")

    handoff = control._entry_publish_handoff(plan, execution_sets, 123, 4)

    validate_contract(handoff)
    assert handoff["entry-publish-node-ids"] == []
    assert handoff["publish-inputs-by-node-id"] == {}


def test_entry_publish_handoff_is_closed_and_names_exact_build_inputs() -> None:
    """Entry-hosted publish receives a validated exact artifact handoff."""
    plan = _load("release-plan.json")
    execution_sets = deepcopy(_load("execution-sets.json"))
    execution_sets["active-publish-selectors"]["github-token"] = []
    execution_sets["active-publish-selectors"][
        "external-oidc-entry-workflow"
    ] = ["publish-node/gh"]

    handoff = control._entry_publish_handoff(plan, execution_sets, 123, 4)

    validate_contract(handoff)
    assert handoff["entry-publish-node-ids"] == ["publish-node/gh"]
    publish_inputs = handoff["publish-inputs-by-node-id"]["publish-node/gh"]
    assert publish_inputs["build-result-artifact-names"] == [
        artifact_name(
            "build-result",
            ArtifactNameInputs(
                123,
                4,
                plan_id="plan/abc123",
                variant_id="variant/v1",
            ),
        )
    ]
    assert publish_inputs["build-bundle-artifact-names"] == [
        artifact_name(
            "variant-bundle",
            ArtifactNameInputs(
                123,
                4,
                plan_id="plan/abc123",
                variant_id="variant/v1",
            ),
        )
    ]
    control._validate_handoff_inputs(handoff, "publish-node/gh", publish_inputs)


def test_entry_proof_upload_matrix_precomputes_final_artifact_uploads() -> None:
    """Entry-hosted proof staging fans back into deterministic proof uploads."""
    plan = _load("release-plan.json")

    matrix = control._entry_proof_upload_matrix(
        plan, ["publish-node/gh"], 123, 4
    )

    publish_result_name = artifact_name(
        "publish-result",
        ArtifactNameInputs(
            123,
            4,
            plan_id="plan/abc123",
            publish_node_id="publish-node/gh",
        ),
    )
    assert len(matrix) == 2
    assert {entry["staging-artifact-name"] for entry in matrix} == {
        f"proof-staging-{publish_result_name}"
    }
    assert all(
        entry["name"].startswith("release-github-release-asset-proof-v1-")
        for entry in matrix
    )
    assert all(entry["file"] == f"{entry['name']}.json" for entry in matrix)


def test_dual_artifact_npm_projection_flows_from_plan_to_pack(  # noqa: PLR0915
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Artifact-level npm projection drives distinct tarball identities."""
    scratch = SCRATCH / "dual-npm-projection"
    shutil.rmtree(SCRATCH, ignore_errors=True)
    snapshot = validate_authoring(REPO_ROOT)
    commit_sha = "b" * 40

    def fake_plan_run(
        args: list[str],
        **_kwargs: object,
    ) -> subprocess.CompletedProcess[str]:
        return subprocess.CompletedProcess(
            args,
            0,
            '{"SemVer2": "1.2.3"}',
            "",
        )

    monkeypatch.setattr(
        "three_workflow_release_planner.planner.subprocess.run",
        fake_plan_run,
    )
    try:
        result = plan_release(
            snapshot,
            PlannerInputs(
                request={
                    "api-version": "three.release.planner-request/v1alpha1",
                    "kind": "planner-request",
                    "profile": "official",
                    "commit-sha": commit_sha,
                    "requested-project-ids": ["hcoona-release-smoke-npm-dual"],
                    "request-flags": {"force": False},
                },
                repo_root=REPO_ROOT,
                dry_run=True,
            ),
        )
        plan = result.plan
        project = plan["envelope"]["projects"]["hcoona-release-smoke-npm-dual"]
        variant_id = project["variant-ids"][0]
        plan_path = scratch / "release-plan.json"
        request_path = scratch / "build-request.json"
        plan_path.parent.mkdir(parents=True)
        plan_path.write_text(json.dumps(plan), encoding="utf-8")

        assert (
            control._cmd_build_request(
                control.argparse.Namespace(
                    plan=str(plan_path),
                    variant_id=variant_id,
                    out=str(request_path),
                )
            )
            == 0
        )
        request = json.loads(request_path.read_text(encoding="utf-8"))
        artifact_ids = request["variant"]["artifact-ids"]
        projections = {
            artifact_id: request["artifacts"][artifact_id]["projection"]
            for artifact_id in artifact_ids
        }
        assert projections == {
            artifact_ids[0]: {"package-name": "hcoona-release-smoke-npm-dual"},
            artifact_ids[1]: {
                "package-name": "@hcoona/hcoona-release-smoke-npm-dual"
            },
        }

        repo_root = scratch / "repo"
        release_root = repo_root / request["project"]["release-root"]
        shutil.copytree(
            REPO_ROOT / request["project"]["release-root"],
            release_root,
        )
        manifest = release_root / "package.json"
        manifest_json = json.loads(manifest.read_text(encoding="utf-8"))
        manifest_json["name"] = "hcoona-release-smoke-npm-dual"
        manifest.write_text(json.dumps(manifest_json), encoding="utf-8")

        def fake_build_run(
            args: Sequence[str],
            cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            if "run" in args and "build" in args:
                return subprocess.CompletedProcess(args, 0, "", "")
            if "pack" in args:
                current = json.loads(manifest.read_text(encoding="utf-8"))
                package_name = current["name"]
                filename = (
                    f"{package_name.removeprefix('@').replace('/', '-')}"
                    "-1.2.3.tgz"
                )
                out_dir = Path(args[args.index("--pack-destination") + 1])
                _write_npm_tarball(
                    out_dir / filename,
                    {
                        "package/package.json": json.dumps(
                            {
                                "name": package_name,
                                "version": "1.2.3",
                                "main": "./dist/index.js",
                                "files": ["dist", "README.md"],
                            }
                        ).encode(),
                        "package/dist/index.js": b"export {};\n",
                        "package/README.md": b"# Smoke\n",
                    },
                )
                return subprocess.CompletedProcess(
                    args,
                    0,
                    json.dumps(
                        [
                            {
                                "filename": filename,
                                "name": package_name,
                                "version": "1.2.3",
                            }
                        ]
                    ),
                    "",
                )
            msg = f"unexpected command in {cwd}: {args}"
            raise AssertionError(msg)

        build_result = execute_build(
            request,
            repo_root,
            scratch / "bundle",
            runner=fake_build_run,
            check_commit=False,
        )

        packaged_names = []
        for artifact_id in artifact_ids:
            receipt = build_result["artifacts"][artifact_id]
            tarball = scratch / "bundle" / receipt["bundle-relative-path"]
            with tarfile.open(tarball) as archive:
                packaged = archive.extractfile("package/package.json")
                assert packaged is not None
                metadata = json.loads(packaged.read().decode("utf-8"))
            assert metadata["version"] == "1.2.3"
            packaged_names.append(metadata["name"])
        assert packaged_names == [
            "hcoona-release-smoke-npm-dual",
            "@hcoona/hcoona-release-smoke-npm-dual",
        ]
        assert json.loads(manifest.read_text(encoding="utf-8"))["name"] == (
            "hcoona-release-smoke-npm-dual"
        )
    finally:
        shutil.rmtree(SCRATCH, ignore_errors=True)


def test_build_request_rejects_conflicting_target_npm_projections(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Shared npm artifacts cannot inherit different target projections."""
    scratch = SCRATCH / "target-npm-conflict"
    shutil.rmtree(SCRATCH, ignore_errors=True)
    snapshot = validate_authoring(REPO_ROOT)
    commit_sha = "b" * 40

    def fake_plan_run(
        args: list[str],
        **_kwargs: object,
    ) -> subprocess.CompletedProcess[str]:
        return subprocess.CompletedProcess(
            args,
            0,
            '{"SemVer2": "1.2.3"}',
            "",
        )

    monkeypatch.setattr(
        "three_workflow_release_planner.planner.subprocess.run",
        fake_plan_run,
    )
    try:
        result = plan_release(
            snapshot,
            PlannerInputs(
                request={
                    "api-version": "three.release.planner-request/v1alpha1",
                    "kind": "planner-request",
                    "profile": "official",
                    "commit-sha": commit_sha,
                    "requested-project-ids": ["hcoona-release-smoke-npm"],
                    "request-flags": {"force": False},
                },
                repo_root=REPO_ROOT,
                dry_run=True,
            ),
        )
        plan = deepcopy(result.plan)
        project = plan["envelope"]["projects"]["hcoona-release-smoke-npm"]
        variant_id = project["variant-ids"][0]
        npm_node_id = next(
            node_id
            for node_id, node in plan["graph"]["publish-nodes"].items()
            if plan["graph"]["target-instance-snapshots"][
                node["target-instance-snapshot-id"]
            ]["family"]
            == "npm"
        )
        conflicting = deepcopy(plan["graph"]["publish-nodes"][npm_node_id])
        conflicting["projection"]["package-name"] = "@hcoona/other-smoke"
        plan["graph"]["publish-nodes"]["publish-node/conflicting-npm"] = (
            conflicting
        )
        plan_path = scratch / "release-plan.json"
        request_path = scratch / "build-request.json"
        plan_path.parent.mkdir(parents=True)
        plan_path.write_text(json.dumps(plan), encoding="utf-8")

        with pytest.raises(ValueError, match="conflicting npm package-name"):
            control._cmd_build_request(
                control.argparse.Namespace(
                    plan=str(plan_path),
                    variant_id=variant_id,
                    out=str(request_path),
                )
            )
    finally:
        shutil.rmtree(SCRATCH, ignore_errors=True)


def test_publish_request_materializes_build_receipts() -> None:
    """Construct a publish request from build-result artifacts."""
    plan = _load("release-plan.json")
    build_result = _load("build-result.json")
    scratch = SCRATCH.relative_to(REPO_ROOT)
    build_dir = scratch / "build-results"
    bundle_dir = scratch / "bundles"
    shutil.rmtree(SCRATCH, ignore_errors=True)
    try:
        build_name = artifact_name(
            "build-result",
            ArtifactNameInputs(
                123,
                4,
                plan_id="plan/abc123",
                variant_id="variant/v1",
            ),
        )
        (build_dir / build_name).mkdir(parents=True)
        (build_dir / build_name / "build-result.json").write_text(
            json.dumps(build_result), encoding="utf-8"
        )

        request = control._publish_request(
            plan,
            "publish-node/gh",
            123,
            4,
            build_dir,
            bundle_dir,
        )

        assert request["kind"] == "publish-request"
        assert request["publish-node-id"] == "publish-node/gh"
        assert "github-release-asset-attestations" not in request
        validate_contract(request)
        package = request["artifacts"]["artifact/package"]
        assert package["bundle-relative-path"] == "dist/Example.1.2.3.nupkg"
        bundle_name = artifact_name(
            "variant-bundle",
            ArtifactNameInputs(
                123,
                4,
                plan_id="plan/abc123",
                variant_id="variant/v1",
            ),
        )
        assert package["input-path"].endswith(
            f"{bundle_name}/dist/Example.1.2.3.nupkg"
        )
    finally:
        shutil.rmtree(SCRATCH, ignore_errors=True)


def test_report_derives_failed_build_and_publish_ids_from_receipts() -> None:
    """Failed stages report expected active IDs missing valid receipts."""
    plan = _load("release-plan.json")
    execution_sets = _load("execution-sets.json")
    execution_sets["active-variant-ids"] = ["variant/missing", "variant/v1"]
    execution_sets["active-publish-node-ids"] = [
        "publish-node/gh",
        "publish-node/missing",
    ]
    artifacts_root = SCRATCH / "artifacts"
    report_path = SCRATCH / "report.json"
    shutil.rmtree(SCRATCH, ignore_errors=True)
    try:
        SCRATCH.mkdir()
        (SCRATCH / "plan.json").write_text(json.dumps(plan), encoding="utf-8")
        (SCRATCH / "execution-sets.json").write_text(
            json.dumps(execution_sets),
            encoding="utf-8",
        )
        build_name = artifact_name(
            "build-result",
            ArtifactNameInputs(
                123,
                4,
                plan_id="plan/abc123",
                variant_id="variant/v1",
            ),
        )
        publish_name = artifact_name(
            "publish-result",
            ArtifactNameInputs(
                123,
                4,
                plan_id="plan/abc123",
                publish_node_id="publish-node/gh",
            ),
        )
        required_names = [
            artifact_name(
                "plan",
                ArtifactNameInputs(123, 4, plan_id="plan/abc123"),
            ),
            artifact_name(
                "execution-sets",
                ArtifactNameInputs(123, 4, plan_id="plan/abc123"),
            ),
            artifact_name(
                "entry-publish-handoff",
                ArtifactNameInputs(123, 4, plan_id="plan/abc123"),
            ),
            build_name,
            publish_name,
        ]
        for required_name in required_names:
            (artifacts_root / required_name).mkdir(parents=True)
        (artifacts_root / build_name / "build-result.json").write_text(
            json.dumps(_load("build-result.json")),
            encoding="utf-8",
        )
        (artifacts_root / publish_name / "publish-result.json").write_text(
            json.dumps(_load("publish-result.json")),
            encoding="utf-8",
        )

        result = control._cmd_report(
            control.argparse.Namespace(
                repository="hcoona/three",
                workflow="Release Buddy",
                run_id=123,
                attempt=4,
                head_sha=SHA_B,
                profile="buddy",
                dry_run="false",
                validation_build="false",
                canary_override_non_public_ref="false",
                out=str(report_path),
                plan=str(SCRATCH / "plan.json"),
                execution_sets=str(SCRATCH / "execution-sets.json"),
                diagnostics="",
                artifacts_root=str(artifacts_root),
                authorize_conclusion="success",
                validate_conclusion="success",
                metadata_conclusion="skipped",
                plan_conclusion="success",
                build_conclusion="failure",
                tag_conclusion="success",
                publish_conclusion="failure",
            )
        )

        report = json.loads(report_path.read_text(encoding="utf-8"))
        assert result == 0
        assert report["jobs"]["build"]["failed-variant-ids"] == [
            "variant/missing",
        ]
        assert report["jobs"]["publish"]["failed-publish-node-ids"] == [
            "publish-node/missing",
        ]
    finally:
        shutil.rmtree(SCRATCH, ignore_errors=True)


def test_report_leaves_failed_id_lists_empty_without_failed_stage() -> None:
    """Successful, skipped, and cancelled stages do not invent failed IDs."""
    plan = _load("release-plan.json")
    execution_sets = _load("execution-sets.json")
    execution_sets["active-variant-ids"] = ["variant/v1"]
    execution_sets["active-publish-node-ids"] = ["publish-node/gh"]

    assert (
        control._failed_variant_ids(
            "success",
            plan,
            execution_sets,
            None,
            {"build-result-artifact-names": []},
        )
        == []
    )
    assert (
        control._failed_publish_node_ids(
            "cancelled",
            plan,
            execution_sets,
            None,
            {"publish-result-artifact-names": []},
        )
        == []
    )


def test_prepare_attestation_uses_planned_asset_names() -> None:
    """Checksum subjects use public asset names, not bundle paths."""
    plan = _load("release-plan.json")
    build_result = _load("build-result.json")
    build_dir = SCRATCH / "build-results"
    bundle_dir = SCRATCH / "bundles"
    shutil.rmtree(SCRATCH, ignore_errors=True)
    try:
        build_name = artifact_name(
            "build-result",
            ArtifactNameInputs(
                123,
                4,
                plan_id="plan/abc123",
                variant_id="variant/v1",
            ),
        )
        (build_dir / build_name).mkdir(parents=True)
        (build_dir / build_name / "build-result.json").write_text(
            json.dumps(build_result), encoding="utf-8"
        )
        request = control._publish_request(
            plan,
            "publish-node/gh",
            123,
            4,
            build_dir,
            bundle_dir,
        )
        request_path = SCRATCH / "publish-request.json"
        checksums_path = SCRATCH / "checksums.txt"
        artifact_ids_path = SCRATCH / "artifact-ids.json"
        output_path = SCRATCH / "outputs.txt"
        request_path.write_text(json.dumps(request), encoding="utf-8")

        args = control.argparse.Namespace(
            publish_request=str(request_path),
            checksums_out=str(checksums_path),
            artifact_ids_out=str(artifact_ids_path),
            github_output=str(output_path),
        )
        assert control._cmd_prepare_attestation(args) == 0

        assert checksums_path.read_text(encoding="utf-8").splitlines() == [
            f"{SHA_B}  Example.1.2.3.nupkg",
            f"{SHA_C}  Example.1.2.3.snupkg",
        ]
    finally:
        shutil.rmtree(SCRATCH, ignore_errors=True)


def test_proof_documents_generate_named_github_release_asset_proofs() -> None:
    """Publish proof generation produces deterministic upload artifact names."""
    plan = _load("release-plan.json")
    build_result = _load("build-result.json")
    build_dir = SCRATCH / "build-results"
    bundle_dir = SCRATCH / "bundles"
    shutil.rmtree(SCRATCH, ignore_errors=True)
    try:
        build_name = artifact_name(
            "build-result",
            ArtifactNameInputs(
                123,
                4,
                plan_id="plan/abc123",
                variant_id="variant/v1",
            ),
        )
        (build_dir / build_name).mkdir(parents=True)
        (build_dir / build_name / "build-result.json").write_text(
            json.dumps(build_result), encoding="utf-8"
        )
        request = control._publish_request(
            plan,
            "publish-node/gh",
            123,
            4,
            build_dir,
            bundle_dir,
        )
        for entry in request["artifacts"].values():
            entry["input-path"] = entry["bundle-relative-path"]
        request["github-release-asset-attestations"] = {
            "artifact/package": {
                "attestation-id": "1",
                "attestation-url": "https://github.com/hcoona/three/attestations/1",
                "bundle-path": "attestation.json",
            },
            "artifact/symbols": {
                "attestation-id": "1",
                "attestation-url": "https://github.com/hcoona/three/attestations/1",
                "bundle-path": "attestation.json",
            },
        }
        result = deepcopy(_load("publish-result.json"))
        result["evidence"] = {
            "asset-attestations": {
                "artifact/package": {
                    "asset-name": "Example.1.2.3.nupkg",
                    "sha256": SHA_B,
                    "predicate-type": "https://slsa.dev/provenance/v1",
                    "signer-workflow": SIGNER_WORKFLOW,
                    "source-repository": "hcoona/three",
                    "source-digest": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                    "attestation-id": "1",
                    "attestation-url": "https://github.com/hcoona/three/attestations/1",
                    "bundle-path": "attestation.json",
                },
                "artifact/symbols": {
                    "asset-name": "Example.1.2.3.snupkg",
                    "sha256": SHA_C,
                    "predicate-type": "https://slsa.dev/provenance/v1",
                    "signer-workflow": SIGNER_WORKFLOW,
                    "source-repository": "hcoona/three",
                    "source-digest": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                    "attestation-id": "1",
                    "attestation-url": "https://github.com/hcoona/three/attestations/1",
                    "bundle-path": "attestation.json",
                },
            }
        }

        proofs = control._proof_documents(
            plan,
            request,
            result,
            "publish-node/gh",
            {
                "repository": "hcoona/three",
                "workflow": "release-publish-node.yml",
                "run-id": 123,
                "run-attempt": 4,
                "head-sha": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                "live": True,
                "dry-run": False,
                "validation-only": False,
            },
            {},
            build_dir,
            123,
            4,
        )

        assert len(proofs) == 2
        assert all(
            name.startswith("release-github-release-asset-proof-v1-")
            for name, _ in proofs
        )
        for _, proof in proofs:
            validate_contract(proof)
    finally:
        shutil.rmtree(SCRATCH, ignore_errors=True)


def test_proof_documents_generate_named_immutable_proofs() -> None:
    """Immutable registry proof generation records Actions artifact identity."""
    plan = _load("release-plan.json")
    build_result = _load("build-result.json")
    build_dir = SCRATCH / "build-results"
    bundle_dir = SCRATCH / "bundles"
    shutil.rmtree(SCRATCH, ignore_errors=True)
    try:
        build_name = artifact_name(
            "build-result",
            ArtifactNameInputs(
                123,
                4,
                plan_id="plan/abc123",
                variant_id="variant/v1",
            ),
        )
        (build_dir / build_name).mkdir(parents=True)
        (build_dir / build_name / "build-result.json").write_text(
            json.dumps(build_result), encoding="utf-8"
        )
        request = control._publish_request(
            plan,
            "publish-node/nuget",
            123,
            4,
            build_dir,
            bundle_dir,
        )

        proofs = control._proof_documents(
            plan,
            request,
            _load("publish-result.json"),
            "publish-node/nuget",
            {
                "repository": "hcoona/three",
                "workflow": "release-publish-node.yml",
                "run-id": 123,
                "run-attempt": 4,
                "head-sha": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                "live": True,
                "dry-run": False,
                "validation-only": False,
            },
            {build_name: 777},
            build_dir,
            123,
            4,
        )

        assert len(proofs) == 1
        name, proof = proofs[0]
        assert name.startswith("release-immutable-proof-v1-")
        assert proof["build-result-artifact-id"] == 777
        validate_contract(proof)
    finally:
        shutil.rmtree(SCRATCH, ignore_errors=True)


def test_external_oidc_gate_requires_live_enablement_token() -> None:
    """Block official live external OIDC targets unless explicitly enabled."""
    plan = deepcopy(_load("release-plan.json"))
    execution_sets = deepcopy(_load("execution-sets.json"))
    plan["envelope"]["profile"] = "official"
    execution_sets["active-publish-node-ids"] = ["publish-node/gh"]
    snapshot = plan["graph"]["target-instance-snapshots"][
        "github-release/public"
    ]
    snapshot["capabilities"]["credential-posture"] = "oidc"
    snapshot["capabilities"]["publish-topology"] = (
        "external-oidc-reusable-workflow"
    )
    node = plan["graph"]["publish-nodes"]["publish-node/gh"]
    node["resolved-publish-identity"]["package-name"] = "Example"

    diagnostics = control._external_oidc_diagnostics(plan, execution_sets, "")

    assert [diagnostic["code"] for diagnostic in diagnostics] == [
        "REQ_EXTERNAL_TARGET_DISABLED"
    ]
    assert diagnostics[0]["details"]["required-enable-token"] == (
        "github-release/public#example#Example"
    )


def test_external_oidc_gate_blocks_unsupported_remote_observation() -> None:
    """Enabled external OIDC targets fail closed without observation support."""
    plan, execution_sets = _unsupported_rubygems_oidc_plan_and_sets()

    diagnostics = control._external_oidc_diagnostics(
        plan, execution_sets, "rubygems/rubygems-org#example#Example"
    )

    assert [diagnostic["code"] for diagnostic in diagnostics] == [
        "REMOTE_CLASSIFICATION_FAILED"
    ]
    assert diagnostics[0]["message"] == (
        "authoritative remote observation is unsupported for selected official "
        "external OIDC target"
    )
    assert diagnostics[0]["details"]["remote-observation"] == "unsupported"
    assert diagnostics[0]["details"]["target-family"] == "rubygems"


def test_single_selected_oidc_plan_ignores_extra_known_npm_token() -> None:
    """Selected RubyGems-only plans allow unrelated known OIDC npm tokens."""
    plan = _public_registry_observation_plan("rubygems")
    plan["envelope"]["profile"] = "official"
    execution_sets = _single_active_oidc_execution_sets()

    diagnostics = control._external_oidc_diagnostics(
        plan,
        execution_sets,
        (
            "rubygems/rubygems-org#example#Example,"
            "npm/npmjs#hcoona-release-smoke-npm#"
            "@hcoona/hcoona-release-smoke-npm"
        ),
        {"publish-node/nuget": "absent"},
    )

    assert diagnostics == []


def test_oidc_allowlist_unknown_target_ref_fails_closed() -> None:
    """Unknown target refs are rejected even when token shape is valid."""
    plan = _public_registry_observation_plan("rubygems")

    with pytest.raises(RuntimeError) as exc_info:
        control._normalize_enablement("unknown/target#example#Example", plan)

    diagnostics = json.loads(str(exc_info.value))["diagnostics"]
    assert diagnostics[0]["code"] == "REQ_INVALID_INPUT"
    assert diagnostics[0]["details"] == {
        "token": "unknown/target#example#Example"
    }


def test_oidc_allowlist_known_non_oidc_target_ref_fails_closed() -> None:
    """Known target refs using non-OIDC credentials are rejected."""
    plan = _public_registry_observation_plan("rubygems")

    with pytest.raises(RuntimeError) as exc_info:
        control._normalize_enablement(
            "nuget/github-packages#example#Example", plan
        )

    diagnostics = json.loads(str(exc_info.value))["diagnostics"]
    assert diagnostics[0]["code"] == "REQ_INVALID_INPUT"
    assert diagnostics[0]["details"] == {
        "token": "nuget/github-packages#example#Example"
    }


def test_oidc_allowlist_preserves_scoped_npm_package_tokens() -> None:
    """Scoped npm package names keep their slash during token matching."""
    plan = _public_registry_observation_plan("npm")
    plan["envelope"]["profile"] = "official"
    node = plan["graph"]["publish-nodes"]["publish-node/nuget"]
    node["resolved-publish-identity"]["package-name"] = (
        "@hcoona/hcoona-release-smoke-npm"
    )
    execution_sets = _single_active_oidc_execution_sets()

    diagnostics = control._external_oidc_diagnostics(
        plan,
        execution_sets,
        "npm/npmjs#example#@hcoona/hcoona-release-smoke-npm",
        {"publish-node/nuget": "absent"},
    )

    assert diagnostics == []


def test_plan_gate_writes_invalid_oidc_allowlist_diagnostics() -> None:
    """Invalid live-enable allowlists populate planner diagnostics artifact."""
    plan = deepcopy(_load("release-plan.json"))
    execution_sets = deepcopy(_load("execution-sets.json"))
    plan["envelope"]["profile"] = "official"
    execution_sets["active-publish-node-ids"] = ["publish-node/gh"]
    out_dir = SCRATCH / "invalid-oidc"
    shutil.rmtree(SCRATCH, ignore_errors=True)
    try:
        out_dir.mkdir(parents=True)
        plan_path = out_dir / "release-plan.json"
        sets_path = out_dir / "execution-sets.json"
        diagnostics_path = out_dir / "planner-diagnostics.json"
        plan_path.write_text(json.dumps(plan), encoding="utf-8")
        sets_path.write_text(json.dumps(execution_sets), encoding="utf-8")

        result = control._cmd_plan_gate(
            control.argparse.Namespace(
                plan=str(plan_path),
                execution_sets=str(sets_path),
                enabled_external_oidc_targets="not-a-valid-token",
                diagnostics_out=str(diagnostics_path),
            )
        )

        assert result == 1
        diagnostics = json.loads(diagnostics_path.read_text(encoding="utf-8"))
        validate_contract(diagnostics)
        assert diagnostics["diagnostics"][0]["code"] == "REQ_INVALID_INPUT"
        assert diagnostics["diagnostics"][0]["details"] == {
            "token": "not-a-valid-token"
        }
    finally:
        shutil.rmtree(SCRATCH, ignore_errors=True)


def test_windows_build_variant_steps_pin_bash_shell() -> None:
    """Windows-routed reusable builds must not run Bash syntax in PowerShell."""
    workflow = _workflow("release-build-variant.yml")

    for step_name in (
        "Compute artifact names",
        "Materialize build request",
        "Restore local .NET tools",
        "Execute build unit",
    ):
        block = _step_block(workflow, step_name)
        assert "        shell: bash\n" in block
        shell_index = block.index("        shell: bash\n")
        run_index = block.index("        run: |")
        assert shell_index < run_index


def test_build_variant_restores_tools_and_uploads_failure_diagnostics() -> None:
    """Build variants retain diagnostics and restore local tools."""
    workflow = _workflow("release-build-variant.yml")

    restore_block = _step_block(workflow, "Restore local .NET tools")
    assert "dotnet tool restore" in restore_block
    assert "[ -f .config/dotnet-tools.json ]" in restore_block

    diagnostics_block = _step_block(workflow, "Upload build diagnostics")
    assert "        if: failure()\n" in diagnostics_block
    assert "actions/upload-artifact@v4" in diagnostics_block
    assert "build-diagnostics.json" in diagnostics_block
    assert "if-no-files-found: ignore" in diagnostics_block


def test_build_variant_sets_up_node_24_for_npm_smoke_builds() -> None:
    """Reusable validation builds must not rely on runner-default Node.js."""
    workflow = yaml.safe_load(_workflow("release-build-variant.yml"))
    steps = workflow["jobs"]["build"]["steps"]

    setup_index, setup_step = next(
        (index, step)
        for index, step in enumerate(steps)
        if step.get("name") == "Install Node.js for npm builds"
    )
    build_index = next(
        index
        for index, step in enumerate(steps)
        if step.get("name") == "Execute build unit"
    )

    assert setup_index < build_index
    assert setup_step["uses"] == "actions/setup-node@v4"
    assert setup_step["with"]["node-version"] == "24"


def test_workflow_helper_invocations_use_uv_workspace_python() -> None:
    """Release workflows invoke helper with workspace packages available."""
    workflows = REPO_ROOT / ".github/workflows"
    plain_helper = "\n          python eng/scripts/workflow_release_control.py"
    uv_helper = "uv run python eng/scripts/workflow_release_control.py"

    for workflow_path in workflows.glob("release-*.yml"):
        workflow = workflow_path.read_text(encoding="utf-8")
        assert plain_helper not in workflow
        for line in workflow.splitlines():
            if "workflow_release_control.py" in line:
                assert line.strip().startswith(uv_helper)


def test_ci_validation_workflow_exposes_control_plane_boundaries() -> None:
    """CI validation workflow preserves planned control-plane boundaries."""
    workflow = yaml.safe_load(_workflow("ci-validate.yml"))
    jobs = workflow["jobs"]

    assert workflow["name"] == "CI Validation"
    assert "workflow_dispatch" not in workflow[True]
    assert set(workflow[True]) == {"pull_request", "push", "schedule"}
    assert (
        jobs["normalize-input"]["outputs"]["planner-diagnostics-artifact-name"]
        == "${{ steps.refs.outputs.planner_diagnostics_artifact_name }}"
    )
    assert set(jobs) >= {
        "normalize-input",
        "plan",
        "materialize-work-groups",
        "validation-work-groups-layer-0",
        "validation-work-groups-layer-1",
        "validation-work-groups-layer-2",
        "aggregate-evidence",
    }
    assert jobs["plan"]["needs"] == "normalize-input"
    assert set(jobs["materialize-work-groups"]["needs"]) == {
        "normalize-input",
        "plan",
    }
    assert set(jobs["aggregate-evidence"]["needs"]) >= {
        "validation-work-groups-layer-0",
        "validation-work-groups-layer-1",
        "validation-work-groups-layer-2",
    }
    assert (
        jobs["validation-work-groups-layer-1"]["needs"][-1]
        == "validation-work-groups-layer-0"
    )
    assert (
        "work-group-layer-1-matrix"
        in jobs["materialize-work-groups"]["outputs"]
    )
    assert jobs["aggregate-evidence"]["name"] == "aggregate-evidence"
    assert "id-token" not in workflow["permissions"]
    plan_steps = jobs["plan"]["steps"]
    diagnostics_upload = next(
        step
        for step in plan_steps
        if step.get("name") == "Upload planner diagnostics"
    )
    assert diagnostics_upload["with"]["name"] == (
        "${{ needs.normalize-input.outputs.planner-diagnostics-artifact-name }}"
    )


def test_ci_validation_workflow_executes_mapped_commands_before_receipts() -> (
    None
):
    """Validation fan-out runs mapped no-publish commands before receipts."""
    workflow = _workflow("ci-validate.yml")
    dependency_gate = _step_block(
        workflow,
        "Check prerequisite validation receipts",
    )
    validation = _step_block(workflow, "Run mapped validation commands")
    receipt = _step_block(
        workflow,
        "Write validation receipt",
    )
    observation = _step_block(workflow, "Write receipt writer observation")
    upload = _step_block(workflow, "Upload validation receipt")
    aggregate = _step_block(workflow, "Aggregate validation evidence")

    assert "check-ci-validation-dependencies" in dependency_gate
    assert (
        "steps.dependency-gate.outputs.dependency_blocked != 'true'"
        in validation
    )
    assert "run-ci-validation-commands" in validation
    assert "--plan .three-ci-validation/plan/validation-plan.json" in validation
    assert "validation-result.json" in validation
    assert "matrix.work-group.runner-family == 'windows'" in workflow
    assert "write-ci-validation-receipt" in receipt
    assert (
        "--validation-result .three-ci-validation/work/validation-result.json"
        in receipt
    )
    assert (
        'validation_outcome="${VALIDATION_OUTCOME:-blocking-failure}"'
        in receipt
    )
    assert "MATRIX_WORK_GROUP_JSON: ${{ toJson(matrix.work-group) }}" in receipt
    assert "WRITER_JOB: ${{ matrix.work-group.writer-job }}" in receipt
    assert (
        "VALIDATION_OUTCOME: ${{ steps.validation.outputs.validation_outcome }}"
        in receipt
    )
    assert '--validation-outcome "$validation_outcome"' in receipt
    assert (
        "--observed-artifacts-dir .three-ci-validation/observed-artifacts"
        in receipt
    )
    assert "steps.receipt.outputs.receipt_artifact_name" in upload
    assert ".three-ci-validation/work/validation-result.json" in upload
    assert "steps.upload-receipt.outputs.artifact-id" in observation
    assert "write-ci-validation-writer-observation" in observation
    assert "aggregate-ci-evidence" in aggregate
    assert "workflow_release_acceptance_gate.py" not in workflow
    assert (
        "--observed-artifacts-dir .three-ci-validation/observed-artifacts"
        in aggregate
    )
    assert "observed_receipts=[]" not in workflow


def test_ci_validation_artifact_refs_include_planner_diagnostics() -> None:
    """Control-plane artifact refs expose the planner diagnostics artifact."""
    output = SCRATCH / "ci-validation-artifact-refs.txt"
    shutil.rmtree(SCRATCH, ignore_errors=True)
    SCRATCH.mkdir()
    try:
        assert (
            control._cmd_ci_validation_artifact_refs(
                argparse.Namespace(
                    run_id="25887422010",
                    run_attempt="1",
                    github_output=str(output),
                )
            )
            == 0
        )
        outputs = _github_outputs(output)
        assert "planner_diagnostics_artifact_name" in outputs
        assert outputs["planner_diagnostics_artifact_name"].startswith(
            "three-ci-validation-",
        )
        assert outputs["planner_diagnostics_artifact_name"] != (
            "ci-validation-planner-diagnostics-25887422010-1"
        )
    finally:
        shutil.rmtree(SCRATCH, ignore_errors=True)


def test_release_workflow_uv_setup_precedes_uv_run() -> None:
    """Every release job installs pinned uv before invoking uv commands."""
    workflows = REPO_ROOT / ".github/workflows"
    setup_action = (
        "uses: astral-sh/setup-uv@"
        "08807647e7069bb48b6ef5acd8ec9567f424441b # v8.1.0"
    )

    for workflow_path in workflows.glob("release-*.yml"):
        workflow = workflow_path.read_text(encoding="utf-8")
        job_starts = [
            index
            for index, line in enumerate(workflow.splitlines(keepends=True))
            if line.startswith("  ") and line[2:3] not in (" ", "")
        ]
        lines = workflow.splitlines(keepends=True)
        job_starts.append(len(lines))
        for start, end in pairwise(job_starts):
            block = "".join(lines[start:end])
            if "uv run" not in block:
                continue
            assert setup_action in block, workflow_path.name
            assert "          version: '0.10.9'\n" in block, workflow_path.name
            setup_index = block.index(setup_action)
            uv_index = block.index("uv run")
            assert setup_index < uv_index, workflow_path.name


def test_entry_workflows_pass_dispatch_pinned_sha() -> None:
    """Manual entry helpers receive github.sha for immutable run pinning."""
    for workflow_name in ("release-official.yml", "release-buddy.yml"):
        workflow = _workflow(workflow_name)
        normalize_block = _step_block(
            workflow, "Authorize and pin dispatch ref"
        )
        assert "RELEASE_PINNED_SHA: ${{ github.sha }}" in normalize_block
        assert '--pinned-sha "$RELEASE_PINNED_SHA" \\' in normalize_block


def test_entry_authorization_uses_env_for_context_and_dispatch_values() -> None:
    """Pre-authorization shell scripts must not interpolate expressions."""
    for workflow_name in ("release-official.yml", "release-buddy.yml"):
        workflow = _workflow(workflow_name)
        normalize_block = _step_block(
            workflow, "Authorize and pin dispatch ref"
        )
        run_script = normalize_block.split("        run: |\n", 1)[1]

        assert "${{ inputs." not in run_script
        assert "${{ github." not in run_script
        for env_line in (
            "RELEASE_ACTOR: ${{ github.triggering_actor }}",
            "RELEASE_DRY_RUN: ${{ inputs.dry-run }}",
            "RELEASE_PINNED_SHA: ${{ github.sha }}",
            "RELEASE_REF: ${{ github.ref }}",
            "RELEASE_REF_NAME: ${{ github.ref_name }}",
            "RELEASE_REF_TYPE: ${{ github.ref_type }}",
            "RELEASE_REPOSITORY: ${{ github.repository }}",
            (
                "RELEASE_REQUESTED_PROJECT_IDS: "
                "${{ inputs.requested-project-ids }}"
            ),
            "RELEASE_VALIDATION_BUILD: ${{ inputs.validation-build }}",
        ):
            assert env_line in normalize_block
        if workflow_name == "release-buddy.yml":
            assert "RELEASE_FORCE: ${{ inputs.force }}" in normalize_block
            assert '--force "$RELEASE_FORCE" \\' in run_script
            assert (
                "RELEASE_CANARY_OVERRIDE_NON_PUBLIC_REF" not in normalize_block
            )
        else:
            assert (
                "RELEASE_CANARY_OVERRIDE_NON_PUBLIC_REF: "
                "${{ inputs.canary-override-non-public-ref }}"
            ) in normalize_block
            assert (
                "--canary-override-non-public-ref "
                '"$RELEASE_CANARY_OVERRIDE_NON_PUBLIC_REF" \\'
            ) in run_script
        assert (
            '--requested-project-ids "$RELEASE_REQUESTED_PROJECT_IDS" \\'
            in run_script
        )
        assert '--dry-run "$RELEASE_DRY_RUN" \\' in run_script
        assert '--validation-build "$RELEASE_VALIDATION_BUILD" \\' in run_script
        assert '--repository "$RELEASE_REPOSITORY" \\' in run_script
        assert '--actor "$RELEASE_ACTOR" \\' in run_script
        assert '--ref "$RELEASE_REF" \\' in run_script


def test_official_canary_override_is_visible_and_environment_gated() -> None:
    """Official canary override is explicit and does not bypass release env."""
    workflow = yaml.safe_load(_workflow("release-official.yml"))
    raw = _workflow("release-official.yml")
    report_block = _step_block(raw, "Render report")

    dispatch = workflow[True]["workflow_dispatch"]["inputs"]
    override = dispatch["canary-override-non-public-ref"]
    assert override["default"] is False
    assert "hcoona-release-smoke-*" in override["description"]
    assert "RELEASE_CANARY_OVERRIDE_NON_PUBLIC_REF" in report_block
    assert (
        "--canary-override-non-public-ref "
        '"$RELEASE_CANARY_OVERRIDE_NON_PUBLIC_REF" \\'
    ) in report_block
    assert workflow["jobs"]["publish-entry"]["environment"] == "release"
    assert (
        workflow["jobs"]["orchestrate"]["with"]["release-environment"]
        == "${{ inputs.dry-run == false && 'release' || '' }}"
    )
    assert workflow["jobs"]["orchestrate"]["with"][
        "canary-override-non-public-ref"
    ] == (
        "${{ needs.authorize-entry.outputs."
        "canary-override-non-public-ref == 'true' }}"
    )


def test_release_shell_steps_use_env_for_workflow_inputs_and_vars() -> None:
    """Inline release shell scripts receive workflow inputs through env."""
    for workflow_path in _release_workflow_paths():
        workflow = yaml.safe_load(workflow_path.read_text(encoding="utf-8"))
        for job_id, job in workflow["jobs"].items():
            for step_index, step in enumerate(job.get("steps", ())):
                run_script = step.get("run") if isinstance(step, dict) else None
                if run_script is None:
                    continue
                assert "${{ inputs." not in run_script, (
                    workflow_path.name,
                    job_id,
                    step_index,
                    step.get("name"),
                )
                assert "${{ vars." not in run_script, (
                    workflow_path.name,
                    job_id,
                    step_index,
                    step.get("name"),
                )


def test_orchestrator_passes_user_controlled_inputs_through_env() -> None:
    """Planner request and OIDC gate inputs avoid shell interpolation."""
    workflow = _workflow("release-orchestrate.yml")

    write_block = _step_block(workflow, "Write planner request")
    assert (
        "REQUESTED_PROJECT_IDS_JSON: ${{ inputs.requested-project-ids-json }}"
    ) in write_block
    assert "RELEASE_PROFILE: ${{ inputs.profile }}" in write_block
    assert "RELEASE_COMMIT_SHA: ${{ inputs.commit-sha }}" in write_block
    assert "RELEASE_FORCE: ${{ inputs.force }}" in write_block
    assert (
        '--requested-project-ids-json "$REQUESTED_PROJECT_IDS_JSON" \\'
        in write_block
    )
    assert '--profile "$RELEASE_PROFILE" \\' in write_block
    assert '--commit-sha "$RELEASE_COMMIT_SHA" \\' in write_block
    assert '--force "$RELEASE_FORCE" \\' in write_block

    planner_block = _step_block(workflow, "Run planner")
    assert "RELEASE_DRY_RUN: ${{ inputs.dry-run }}" in planner_block
    assert (
        "RELEASE_VALIDATION_BUILD: ${{ inputs.validation-build }}"
        in planner_block
    )
    assert "if [ \"$RELEASE_DRY_RUN\" = 'true' ]; then" in planner_block
    assert (
        "if [ \"$RELEASE_VALIDATION_BUILD\" = 'true' ]; then" in planner_block
    )

    gate_block = _step_block(workflow, "Apply live external OIDC gate")
    assert (
        "ENABLED_EXTERNAL_OIDC_TARGETS: "
        "${{ inputs.enabled-external-oidc-targets }}"
    ) in gate_block
    assert (
        '--enabled-external-oidc-targets "$ENABLED_EXTERNAL_OIDC_TARGETS" \\'
        in gate_block
    )


def test_orchestrator_always_uploads_entry_handoff() -> None:
    """Report inputs include handoff artifact even for empty entry selectors."""
    workflow = _workflow("release-orchestrate.yml")
    write_block = _step_block(workflow, "Write entry publish handoff")
    upload_block = _step_block(workflow, "Upload entry publish handoff")

    assert "if:" not in write_block
    assert "if:" not in upload_block
    assert "entry-publish-handoff.json" in write_block
    assert "entry-publish-handoff.json" in upload_block


def _ci_validation_push_request(
    changed_files: list[str],
) -> dict[str, object]:
    """Return a CI validation push request for control-plane tests."""
    from three_workflow_release_contracts import (  # noqa: PLC0415
        API_VERSIONS_BY_KIND,
        CiValidationKind,
        canonical_json_digest,
        ci_validation_request_artifact_ref,
        ci_validation_request_projection,
    )

    run_id = "25887422010"
    run_attempt = "1"
    request: dict[str, object] = {
        "api-version": API_VERSIONS_BY_KIND[CiValidationKind.REQUEST.value],
        "kind": CiValidationKind.REQUEST.value,
        "created-at": "2026-05-14T21:09:21Z",
        "repository": {"owner": "hcoona", "name": "three"},
        "run": {
            "workflow": "CI Validation",
            "run-id": run_id,
            "run-attempt": run_attempt,
        },
        "schema-diagnostics": [],
        "artifact-ref": ci_validation_request_artifact_ref(
            run_id=run_id,
            run_attempt=run_attempt,
        ),
        "request-digest": "0" * 64,
        "mode": "push",
        "validation-tree": {
            "commit-sha": "b" * 40,
            "ref": "refs/heads/main",
        },
        "event": {
            "name": "push",
            "number": None,
            "actor": "octocat",
            "run-id": run_id,
            "run-attempt": run_attempt,
        },
        "affected-range": {
            "status": "available",
            "base-sha": "a" * 40,
            "base-tip-sha": None,
            "head-sha": "b" * 40,
            "changed-files": sorted(changed_files),
            "source": "push",
            "diagnostic": None,
            "diagnostic-detail": None,
        },
    }
    request["request-digest"] = canonical_json_digest(
        ci_validation_request_projection(request),
    )
    return request


def _github_outputs(path: Path) -> dict[str, str]:
    """Read simple one-line GitHub output records."""
    return dict(
        line.rstrip("\n").split("=", 1)
        for line in path.read_text(encoding="utf-8").splitlines()
        if "=" in line
    )


def test_ci_validation_control_plane_materializes_empty_plan() -> None:
    """CI control helpers write request, assignments, and aggregate evidence."""
    scratch = SCRATCH / "ci-validation-control"
    shutil.rmtree(scratch, ignore_errors=True)
    scratch.mkdir(parents=True)
    try:
        request_path = scratch / "ci-validation-request.json"
        assignments_path = scratch / "selector-assignments.json"
        changed_files_path = scratch / "changed-files.json"
        fact_snapshot_path = scratch / "fact-snapshot.json"
        manifest_path = scratch / "receipt-manifest.json"
        aggregate_path = scratch / "aggregate.json"
        output_path = scratch / "outputs.txt"
        request_args = argparse.Namespace(
            mode="push",
            repository="hcoona/three",
            workflow="CI Validation",
            run_id="25887422010",
            run_attempt="1",
            event_name="push",
            event_number="",
            actor="octocat",
            validation_commit_sha="b" * 40,
            validation_ref="refs/heads/main",
            base_sha="a" * 40,
            base_tip_sha="",
            head_sha="b" * 40,
            changed_files_json="[]",
            range_status="available",
            range_diagnostic_detail="missing",
            created_at="2026-05-14T21:09:21Z",
            out=str(request_path),
            github_output=None,
        )
        assert control._cmd_write_ci_validation_request(request_args) == 0
        request = json.loads(request_path.read_text(encoding="utf-8"))
        plan_snapshot = plan_ci_validation_from_repo(
            CiValidationPlannerInputs(
                request=request,
                repo_root=REPO_ROOT,
                expected_run_id="25887422010",
                expected_run_attempt="1",
                created_at="2026-05-14T21:09:21Z",
            )
        )
        plan_path = scratch / "validation-plan.json"
        plan_path.write_text(
            json.dumps(plan_snapshot.plan),
            encoding="utf-8",
        )
        changed_files_path.write_text(
            json.dumps(plan_snapshot.changed_files_snapshot),
            encoding="utf-8",
        )
        fact_snapshot_path.write_text(
            json.dumps(plan_snapshot.fact_snapshot),
            encoding="utf-8",
        )
        materialize_args = argparse.Namespace(
            plan=str(plan_path),
            changed_files_snapshot=str(changed_files_path),
            fact_snapshot=str(fact_snapshot_path),
            workflow="CI Validation",
            writer_job="validation-work-groups",
            created_at="2026-05-14T21:09:21Z",
            assignments_out=str(assignments_path),
            github_output=str(output_path),
        )
        assert control._cmd_materialize_ci_work_groups(materialize_args) == 0
        assignments = json.loads(assignments_path.read_text(encoding="utf-8"))
        assert assignments["assignments"] == []
        aggregate_args = argparse.Namespace(
            repository="hcoona/three",
            workflow="CI Validation",
            run_id="25887422010",
            run_attempt="1",
            plan=str(plan_path),
            changed_files_snapshot=str(changed_files_path),
            fact_snapshot=str(fact_snapshot_path),
            assignments=str(assignments_path),
            created_at="2026-05-14T21:09:21Z",
            receipt_manifest_out=str(manifest_path),
            aggregate_out=str(aggregate_path),
            github_output=str(output_path),
        )
        assert control._cmd_aggregate_ci_evidence(aggregate_args) == 0
        aggregate = json.loads(aggregate_path.read_text(encoding="utf-8"))
        receipt_manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        changed_files = json.loads(
            changed_files_path.read_text(encoding="utf-8"),
        )
        fact_snapshot = json.loads(
            fact_snapshot_path.read_text(encoding="utf-8"),
        )
        validate_ci_validation_aggregate(
            aggregate,
            plan=plan_snapshot.plan,
            receipt_manifest=receipt_manifest,
            selector_assignments_manifest=assignments,
            changed_files_snapshot=changed_files,
            fact_snapshot=fact_snapshot,
        )
        assert aggregate["verdict"] == "passed"
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_validation_materializer_uses_workflow_matrix_writer_ids() -> None:
    """Trusted writer IDs match the actual workflow matrix object shape."""
    scratch = SCRATCH / "ci-validation-writer-ids"
    shutil.rmtree(scratch, ignore_errors=True)
    scratch.mkdir(parents=True)
    try:
        request = _ci_validation_push_request(
            [
                (
                    "src/public/lib/three-workflow-release-planner/src/"
                    "three_workflow_release_planner/ci_validation_planner.py"
                )
            ],
        )
        plan_snapshot = plan_ci_validation_from_repo(
            CiValidationPlannerInputs(
                request=request,
                repo_root=REPO_ROOT,
                expected_run_id="25887422010",
                expected_run_attempt="1",
                created_at="2026-05-14T21:09:21Z",
            )
        )
        plan_path = scratch / "validation-plan.json"
        changed_files_path = scratch / "changed-files.json"
        fact_snapshot_path = scratch / "fact-snapshot.json"
        assignments_path = scratch / "selector-assignments.json"
        output_path = scratch / "outputs.txt"
        plan_path.write_text(json.dumps(plan_snapshot.plan), encoding="utf-8")
        changed_files_path.write_text(
            json.dumps(plan_snapshot.changed_files_snapshot),
            encoding="utf-8",
        )
        fact_snapshot_path.write_text(
            json.dumps(plan_snapshot.fact_snapshot),
            encoding="utf-8",
        )

        assert (
            control._cmd_materialize_ci_work_groups(
                argparse.Namespace(
                    plan=str(plan_path),
                    changed_files_snapshot=str(changed_files_path),
                    fact_snapshot=str(fact_snapshot_path),
                    workflow="CI Validation",
                    writer_job="validation-work-groups",
                    created_at="2026-05-14T21:09:21Z",
                    assignments_out=str(assignments_path),
                    github_output=str(output_path),
                )
            )
            == 0
        )

        assignments = json.loads(assignments_path.read_text(encoding="utf-8"))
        outputs = _github_outputs(output_path)
        layers = json.loads(outputs["work_group_layers"])
        assert layers
        assert outputs["has_work_group_layer_0"] == "true"
        matrix = {
            item["work-group-id"]: item
            for item in json.loads(outputs["work_group_matrix"])
        }
        assert assignments["assignments"]
        for item in matrix.values():
            assert isinstance(item["depends-on"], list)
            assert isinstance(item["dependency-layer"], int)
            assert item["runner-family"] in {"ubuntu", "windows"}
            assert item["runner"] == item["runner-family"]
            assert item["no-publish"] is True
            if item["kind"] in {"ecosystem-gate", "descriptor-validation"}:
                assert item["validation-commands"]
            if item["kind"] in {
                "lightweight-preflight",
                "release-shaped-artifact",
                "workflow-release-tooling",
            }:
                assert item["validation-commands"]
        for assignment in assignments["assignments"]:
            work_group_id = assignment["work-group-id"]
            assert assignment["trusted-writer-id"] == ci_validation_writer_id(
                workflow="CI Validation",
                job=matrix[work_group_id]["writer-job"],
                matrix={"work-group": matrix[work_group_id]},
            )
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_validation_command_runner_maps_exit_codes_to_outcome() -> None:
    """Mapped validation commands record no-publish command results."""
    scratch = SCRATCH / "ci-validation-command-runner"
    shutil.rmtree(scratch, ignore_errors=True)
    scratch.mkdir(parents=True)
    try:
        result_path = scratch / "validation-result.json"
        output_path = scratch / "outputs.txt"
        matrix = {
            "work-group-id": "wg-python",
            "kind": "ecosystem-gate",
            "runner-family": "ubuntu",
            "validation-commands": [
                {
                    "label": "python ok",
                    "capability": "test",
                    "argv": [
                        sys.executable,
                        "-c",
                        "raise SystemExit(0)",
                    ],
                }
            ],
        }

        assert (
            control._cmd_run_ci_validation_commands(
                argparse.Namespace(
                    matrix_work_group_json=json.dumps(matrix),
                    plan="",
                    repo_root=str(REPO_ROOT),
                    result_out=str(result_path),
                    github_output=str(output_path),
                )
            )
            == 0
        )

        result = json.loads(result_path.read_text(encoding="utf-8"))
        assert result["outcome"] == "success"
        assert result["commands"][0]["argv"][0] == sys.executable
        assert result["commands"][0]["capability"] == "test"
        assert _github_outputs(output_path)["validation_outcome"] == "success"
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_validation_lightweight_policy_uses_frozen_scope() -> None:
    """Lightweight validation consumes the frozen planner coverage target."""
    scratch = SCRATCH / "ci-validation-lightweight-policy"
    shutil.rmtree(scratch, ignore_errors=True)
    scratch.mkdir(parents=True)
    try:
        plan_path = scratch / "validation-plan.json"
        assignments_path = scratch / "selector-assignments.json"
        work_group = {
            "work-group-id": "wg-lightweight",
            "kind": "lightweight-preflight",
            "runner-family": "ubuntu",
            "coverage-target": {
                "type": "lightweight-policy",
                "id": "known-non-impacting",
            },
        }
        matrix_work_group = {**work_group, "no-publish": True}
        plan = {
            "work-groups": [work_group],
            "evidence-expectations": [
                {
                    "work-group-id": "wg-lightweight",
                    "detail-profile": "lightweight-profile",
                }
            ],
            "detail-profiles": [
                {
                    "detail-profile-id": "lightweight-profile",
                    "required-subchecks": [
                        {"subcheck-id": "known-non-impacting-policy"}
                    ],
                }
            ],
        }
        assignments = {
            "assignments": [
                {
                    "assignment-id": "assign-wg-lightweight",
                    "work-group-id": "wg-lightweight",
                    "receipt-artifact-ref": (
                        "ci-validation-receipt-wg-lightweight"
                    ),
                    "writer-observation-ref": (
                        "ci-validation-writer-observation-wg-lightweight"
                    ),
                    "trusted-writer-id": "trusted-writer",
                }
            ]
        }
        plan_path.write_text(json.dumps(plan), encoding="utf-8")
        assignments_path.write_text(json.dumps(assignments), encoding="utf-8")

        assert (
            control._cmd_validate_ci_validation_lightweight_policy(
                argparse.Namespace(
                    plan=str(plan_path),
                    assignments=str(assignments_path),
                    work_group_id="wg-lightweight",
                    matrix_work_group_json=json.dumps(matrix_work_group),
                )
            )
            == 0
        )

        stale_matrix = {
            **matrix_work_group,
            "coverage-target": {"type": "lightweight-policy", "id": "stale"},
        }
        assert (
            control._cmd_validate_ci_validation_lightweight_policy(
                argparse.Namespace(
                    plan=str(plan_path),
                    assignments=str(assignments_path),
                    work_group_id="wg-lightweight",
                    matrix_work_group_json=json.dumps(stale_matrix),
                )
            )
            == 1
        )
        publishing_matrix = {**matrix_work_group, "no-publish": False}
        assert (
            control._cmd_validate_ci_validation_lightweight_policy(
                argparse.Namespace(
                    plan=str(plan_path),
                    assignments=str(assignments_path),
                    work_group_id="wg-lightweight",
                    matrix_work_group_json=json.dumps(publishing_matrix),
                )
            )
            == 1
        )
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_validation_descriptor_mapping_uses_scoped_planned_obligations() -> (
    None
):
    """Descriptor validation does not rediscover unrelated descriptors."""
    scratch = SCRATCH / "ci-validation-descriptor-scope"
    shutil.rmtree(scratch, ignore_errors=True)
    scratch.mkdir(parents=True)
    try:
        scoped_root = Path("src/public/lib/hcoona-release-smoke-pypi")
        source_root = REPO_ROOT / scoped_root
        destination_root = scratch / scoped_root
        shutil.copytree(source_root, destination_root)
        catalog_path = scratch / "eng/release/target-instances.yml"
        catalog_path.parent.mkdir(parents=True)
        shutil.copyfile(
            REPO_ROOT / catalog_path.relative_to(scratch), catalog_path
        )
        unrelated = scratch / "src/public/lib/unrelated-invalid"
        unrelated.mkdir(parents=True)
        (unrelated / "pyproject.toml").write_text(
            "[project]\nname = 'unrelated-invalid'\nversion = '0.0.0'\n",
            encoding="utf-8",
        )
        (unrelated / "three.release.yml").write_text(
            "api-version: three.release/v1alpha1\nkind: invalid-project\n",
            encoding="utf-8",
        )
        descriptor_path = (scoped_root / "three.release.yml").as_posix()
        plan = {
            "work-groups": [
                {
                    "work-group-id": "wg-descriptor",
                    "kind": "descriptor-validation",
                    "coverage-target": {
                        "type": "descriptor",
                        "id": descriptor_path,
                    },
                }
            ],
            "descriptor-obligations": [
                {
                    "descriptor-obligation-id": (
                        "desc-hcoona-release-smoke-pypi"
                    ),
                    "work-group-id": "wg-descriptor",
                    "coverage-target": {
                        "type": "descriptor",
                        "id": descriptor_path,
                    },
                    "descriptor-path": descriptor_path,
                }
            ],
        }
        plan_path = scratch / "validation-plan.json"
        plan_path.write_text(json.dumps(plan), encoding="utf-8")

        commands = control._ci_validation_commands(
            plan,
            cast("Mapping[str, object]", plan["work-groups"][0]),
        )
        assert commands[0]["argv"][4] == "validate-ci-validation-descriptors"
        assert "three_workflow_release_authoring.cli" not in commands[0]["argv"]
        assert (
            control._cmd_validate_ci_validation_descriptors(
                argparse.Namespace(
                    plan=str(plan_path),
                    work_group_id="wg-descriptor",
                    repo_root=str(scratch),
                )
            )
            == 0
        )

        descriptor = destination_root / "three.release.yml"
        descriptor.write_text(
            descriptor.read_text(encoding="utf-8")
            + "\nunexpected-field: true\n",
            encoding="utf-8",
        )
        assert (
            control._cmd_validate_ci_validation_descriptors(
                argparse.Namespace(
                    plan=str(plan_path),
                    work_group_id="wg-descriptor",
                    repo_root=str(scratch),
                )
            )
            == 1
        )
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_validation_command_mapping_uses_required_no_publish_checks() -> (
    None
):
    """Execution mapping uses required non-mutating commands per capability."""
    plan: Mapping[str, object] = {
        "subjects": [
            {
                "subject-id": "dotnet-subject",
                "root": "src/public/lib/CircularList",
            },
            {
                "subject-id": "js-subject",
                "root": "src/public/lib/hcoona-release-smoke-npm",
            },
        ]
    }
    dotnet_group = {
        "work-group-id": "wg-dotnet",
        "kind": "ecosystem-gate",
        "coverage-target": {"type": "subject", "id": "dotnet-subject"},
        "ecosystem": "dotnet",
        "runner-family": "windows",
        "depends-on": [],
        "expected-evidence": {
            "planned-capabilities": ["build", "test", "type-check", "format"],
        },
    }
    js_group = {
        "work-group-id": "wg-js",
        "kind": "ecosystem-gate",
        "coverage-target": {"type": "subject", "id": "js-subject"},
        "ecosystem": "typescript",
        "runner-family": "ubuntu",
        "depends-on": [],
        "expected-evidence": {
            "planned-capabilities": ["format", "type-check"],
        },
    }
    release_group = {
        "work-group-id": "wg-release",
        "kind": "release-shaped-artifact",
        "coverage-target": {"type": "subject", "id": "js-subject"},
        "runner-family": "ubuntu",
        "depends-on": ["wg-js"],
    }

    dotnet_commands = control._ci_validation_commands(plan, dotnet_group)
    fallback_commands = control._ci_validation_commands({}, dotnet_group)
    js_commands = control._ci_validation_commands(plan, js_group)
    release_commands = control._ci_validation_commands(plan, release_group)

    assert ["dotnet", "build", "src/public/lib/CircularList"] in [
        command["argv"] for command in dotnet_commands
    ]
    assert [
        "dotnet",
        "test",
        "src/public/lib/CircularList",
        "--no-restore",
        "--no-build",
    ] in [command["argv"] for command in dotnet_commands]
    test_only_group = {
        **dotnet_group,
        "work-group-id": "wg-dotnet-test-only",
        "expected-evidence": {"planned-capabilities": ["test"]},
    }
    assert [
        "dotnet",
        "test",
        "src/public/lib/CircularList",
    ] in [
        command["argv"]
        for command in control._ci_validation_commands(plan, test_only_group)
    ]
    assert all(
        "--no-restore" not in command["argv"]
        and "--no-build" not in command["argv"]
        for command in control._ci_validation_commands(plan, test_only_group)
    )
    assert any(
        command["capability"] == "type-check"
        and command["argv"]
        == [
            "dotnet",
            "build",
            "src/public/lib/CircularList",
        ]
        for command in dotnet_commands
    )
    assert [
        "dotnet",
        "format",
        "src/public/lib/CircularList",
        "--verify-no-changes",
    ] in [command["argv"] for command in dotnet_commands]
    assert ["dotnet", "build", "dirs.proj"] not in [
        command["argv"] for command in dotnet_commands
    ]
    assert ["dotnet", "build", "dirs.proj"] in [
        command["argv"] for command in fallback_commands
    ]
    assert [
        "pnpm",
        "--dir",
        "src/public/lib/hcoona-release-smoke-npm",
        "run",
        "typecheck",
    ] in [command["argv"] for command in js_commands]
    assert [
        "pnpm",
        "--dir",
        "src/public/lib/hcoona-release-smoke-npm",
        "exec",
        "biome",
        "format",
        "--check",
        ".",
    ] in [command["argv"] for command in js_commands]
    assert all("--if-present" not in command["argv"] for command in js_commands)
    assert all(command["argv"][-1] != "format" for command in js_commands)
    assert release_commands == [
        {
            "label": "validate release-shaped artifact obligations",
            "argv": [],
            "capability": None,
            "builtin": "release-shaped-artifact",
        }
    ]


def test_ci_validation_evidence_preserves_per_capability_outcomes() -> None:
    """Receipt evidence keeps command outcomes per capability."""
    plan: Mapping[str, object] = {
        "work-groups": [
            {
                "work-group-id": "wg-python",
                "kind": "ecosystem-gate",
                "coverage-target": {"type": "subject", "id": "python"},
            }
        ],
        "evidence-expectations": [
            {
                "work-group-id": "wg-python",
                "category": "ecosystem-gate",
                "planned-capabilities": ["build", "test"],
            }
        ],
    }
    validation_result = {
        "outcome": "blocking-failure",
        "commands": [
            {"capability": "build", "outcome": "success"},
            {"capability": "test", "outcome": "blocking-failure"},
        ],
    }
    diagnostics = control._ci_validation_diagnostics(
        plan,
        "wg-python",
        outcome="blocking-failure",
    )

    evidence = control._ci_validation_evidence(
        plan,
        "wg-python",
        outcome="blocking-failure",
        diagnostics=diagnostics,
        validation_result=validation_result,
    )

    results = {
        item["capability"]: item
        for item in cast(
            "Sequence[Mapping[str, object]]",
            evidence["capability-results"],
        )
    }
    assert results["build"]["outcome"] == "success"
    assert results["build"]["diagnostics"] == []
    assert results["test"]["outcome"] == "blocking-failure"
    test_diagnostic = cast(
        "Sequence[Mapping[str, object]]",
        results["test"]["diagnostics"],
    )[0]
    assert test_diagnostic["detail"] == "test"


def test_ci_validation_success_requires_result_identity_match() -> None:
    """Validation-result success is bound to the planned work group identity."""
    plan: Mapping[str, object] = {
        "work-groups": [
            {
                "work-group-id": "wg-python",
                "kind": "ecosystem-gate",
                "runner-family": "ubuntu",
                "coverage-target": {"type": "subject", "id": "python"},
            }
        ],
        "evidence-expectations": [
            {
                "work-group-id": "wg-python",
                "category": "ecosystem-gate",
                "planned-capabilities": ["build"],
            }
        ],
    }
    validation_result: dict[str, object] = {
        "work-group-id": "wg-python",
        "kind": "ecosystem-gate",
        "runner-family": "ubuntu",
        "outcome": "success",
        "commands": [{"capability": "build", "outcome": "success"}],
    }

    assert (
        control._ci_validation_outcome(
            plan,
            "wg-python",
            dependency_blocked=False,
            validation_result=validation_result,
        )
        == "success"
    )
    for field, value in (
        ("work-group-id", "wg-other"),
        ("kind", "descriptor-validation"),
        ("runner-family", "windows"),
    ):
        stale_result = {**validation_result, field: value}
        assert (
            control._ci_validation_outcome(
                plan,
                "wg-python",
                dependency_blocked=False,
                validation_result=stale_result,
            )
            == "blocking-failure"
        )


def test_ci_validation_release_shaped_artifact_does_not_fabricate_success() -> (
    None
):
    """Release-shaped validation fails closed without source evidence."""
    scratch = SCRATCH / "ci-validation-release-shaped-success"
    shutil.rmtree(scratch, ignore_errors=True)
    scratch.mkdir(parents=True)
    try:
        artifact_ref = (
            "ci-validation/artifacts/python/example/"
            "example-1.0.0-py3-none-any.whl"
        )
        work_group = {
            "work-group-id": "wg-release",
            "kind": "release-shaped-artifact",
            "runner-family": "ubuntu",
            "coverage-target": {"type": "subject", "id": "python.example"},
            "depends-on": ["wg-python", "wg-descriptor"],
        }
        obligation = {
            "artifact-obligation-id": "artifact-python-example-wheel",
            "work-group-id": "wg-release",
            "subject-id": "python.example",
            "descriptor-path": "src/public/lib/example/three.release.yml",
            "profile-coverage": ["wheel"],
            "artifact": {
                "kind-family": "python",
                "concrete-kind": "wheel",
                "logical-artifact-role": "package",
                "variant-dimensions": {},
                "expected-artifact-refs": [artifact_ref],
            },
            "release-receipt": {
                "expected-family": "python",
                "logical-receipt-role": "build",
                "variant-dimensions": {},
            },
        }
        plan = {
            "validation-tree": {"commit-sha": "b" * 40},
            "work-groups": [work_group],
            "artifact-obligations": [obligation],
            "evidence-expectations": [
                {
                    "work-group-id": "wg-release",
                    "category": "release-shaped-artifact",
                    "planned-capabilities": None,
                }
            ],
        }
        matrix = {**work_group, "validation-commands": [], "no-publish": True}
        matrix["validation-commands"] = control._ci_validation_commands(
            plan,
            work_group,
        )
        plan_path = scratch / "validation-plan.json"
        result_path = scratch / "validation-result.json"
        outputs_path = scratch / "outputs.txt"
        plan_path.write_text(json.dumps(plan), encoding="utf-8")

        assert (
            control._cmd_run_ci_validation_commands(
                argparse.Namespace(
                    matrix_work_group_json=json.dumps(matrix),
                    plan=str(plan_path),
                    changed_files_snapshot="",
                    fact_snapshot="",
                    assignments="",
                    observed_artifacts_dir="",
                    observed_commit_sha="b" * 40,
                    repo_root=str(REPO_ROOT),
                    result_out=str(result_path),
                    github_output=str(outputs_path),
                )
            )
            == 0
        )

        validation_result = json.loads(result_path.read_text(encoding="utf-8"))
        assert validation_result["outcome"] == "blocking-failure"
        command = validation_result["commands"][0]
        assert command["builtin"] == "release-shaped-artifact"
        assert "source proof is unavailable" in command["error"]
        assert (
            control._ci_validation_outcome(
                plan,
                "wg-release",
                dependency_blocked=False,
                validation_result=validation_result,
            )
            == "blocking-failure"
        )
        evidence = control._ci_validation_evidence(
            plan,
            "wg-release",
            outcome="blocking-failure",
            diagnostics=control._ci_validation_diagnostics(
                plan,
                "wg-release",
                outcome="blocking-failure",
            ),
            validation_result=validation_result,
        )
        assert evidence["artifact-refs"] == []

        stale_matrix = {
            **matrix,
            "coverage-target": {"type": "subject", "id": "python.stale"},
        }
        stale_result_path = scratch / "stale-validation-result.json"
        assert (
            control._cmd_run_ci_validation_commands(
                argparse.Namespace(
                    matrix_work_group_json=json.dumps(stale_matrix),
                    plan=str(plan_path),
                    changed_files_snapshot="",
                    fact_snapshot="",
                    assignments="",
                    observed_artifacts_dir="",
                    observed_commit_sha="b" * 40,
                    repo_root=str(REPO_ROOT),
                    result_out=str(stale_result_path),
                    github_output=None,
                )
            )
            == 0
        )
        stale_result = json.loads(stale_result_path.read_text(encoding="utf-8"))
        assert stale_result["outcome"] == "blocking-failure"
        assert (
            "does not match frozen plan" in stale_result["commands"][0]["error"]
        )
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_validation_release_shaped_success_requires_valid_evidence() -> None:
    """Release-shaped success requires matching artifact evidence."""
    artifact_ref = "ci-validation/artifacts/python/example/wheel.whl"
    plan: Mapping[str, object] = {
        "validation-tree": {"commit-sha": "b" * 40},
        "work-groups": [
            {
                "work-group-id": "wg-release",
                "kind": "release-shaped-artifact",
                "runner-family": "ubuntu",
                "coverage-target": {"type": "subject", "id": "python.example"},
            }
        ],
        "artifact-obligations": [
            {
                "artifact-obligation-id": "artifact-python-example-wheel",
                "work-group-id": "wg-release",
                "subject-id": "python.example",
                "descriptor-path": "src/public/lib/example/three.release.yml",
                "profile-coverage": ["wheel"],
                "artifact": {
                    "kind-family": "python",
                    "concrete-kind": "wheel",
                    "logical-artifact-role": "package",
                    "variant-dimensions": {},
                    "expected-artifact-refs": [artifact_ref],
                },
                "release-receipt": {
                    "expected-family": "python",
                    "logical-receipt-role": "build",
                    "variant-dimensions": {},
                },
            }
        ],
        "evidence-expectations": [
            {
                "work-group-id": "wg-release",
                "category": "release-shaped-artifact",
                "planned-capabilities": None,
            }
        ],
    }
    result = control._ci_artifact_obligation_success_result(
        cast(
            "Mapping[str, object]",
            cast("Sequence[object]", plan["artifact-obligations"])[0],
        )
    )
    cast(
        "dict[str, object]",
        cast("dict[str, object]", result["artifact"])["observed"],
    )["digests"] = [
        {
            "artifact-ref": artifact_ref,
            "algorithm": "sha256",
            "digest": "f" * 64,
            "digest-available": True,
            "diagnostics": [],
        }
    ]
    validation_result: dict[str, object] = {
        "work-group-id": "wg-release",
        "kind": "release-shaped-artifact",
        "runner-family": "ubuntu",
        "coverage-target": {"type": "subject", "id": "python.example"},
        "observed-commit-sha": "b" * 40,
        "outcome": "success",
        "commands": [
            {
                "builtin": "release-shaped-artifact",
                "evidence-source": "no-publish-validation",
                "outcome": "success",
                "artifact-obligation-results": [result],
            }
        ],
    }

    assert (
        control._ci_validation_outcome(
            plan,
            "wg-release",
            dependency_blocked=False,
            validation_result=validation_result,
        )
        == "blocking-failure"
    )
    reused_validation_result = deepcopy(validation_result)
    reused_command = cast(
        "dict[str, object]",
        cast("list[object]", reused_validation_result["commands"])[0],
    )
    reused_command["evidence-source"] = "reused-validation-receipt"
    reused_command["reused-receipt"] = {
        "artifact-ref": (
            "ci-validation/receipts/25887422010/1/wg-release/receipt.json"
        ),
        "receipt-id": "wg-release",
        "receipt-content-digest": "a" * 64,
        "observed-commit-sha": "b" * 40,
    }
    assert (
        control._ci_validation_outcome(
            plan,
            "wg-release",
            dependency_blocked=False,
            validation_result=reused_validation_result,
        )
        == "blocking-failure"
    )
    stale_reused_result = deepcopy(reused_validation_result)
    stale_reused_command = cast(
        "dict[str, object]",
        cast("list[object]", stale_reused_result["commands"])[0],
    )
    cast("dict[str, object]", stale_reused_command["reused-receipt"])[
        "observed-commit-sha"
    ] = "c" * 40
    assert (
        control._ci_validation_outcome(
            plan,
            "wg-release",
            dependency_blocked=False,
            validation_result=stale_reused_result,
        )
        == "blocking-failure"
    )

    missing_evidence = {
        **validation_result,
        "commands": [
            {
                "builtin": "release-shaped-artifact",
                "evidence-source": "no-publish-validation",
                "outcome": "success",
            }
        ],
    }
    assert (
        control._ci_validation_outcome(
            plan,
            "wg-release",
            dependency_blocked=False,
            validation_result=missing_evidence,
        )
        == "blocking-failure"
    )
    unchecked_result = deepcopy(result)
    cast("dict[str, object]", unchecked_result["release-receipt"])[
        "schema-checked"
    ] = False
    invalid_evidence = {
        **validation_result,
        "commands": [
            {
                "builtin": "release-shaped-artifact",
                "evidence-source": "no-publish-validation",
                "outcome": "success",
                "artifact-obligation-results": [unchecked_result],
            }
        ],
    }
    assert (
        control._ci_validation_outcome(
            plan,
            "wg-release",
            dependency_blocked=False,
            validation_result=invalid_evidence,
        )
        == "blocking-failure"
    )

    for field, value in (
        ("coverage-target", {"type": "subject", "id": "python.other"}),
        ("observed-commit-sha", "c" * 40),
    ):
        mismatched_identity = {**validation_result, field: value}
        assert (
            control._ci_validation_outcome(
                plan,
                "wg-release",
                dependency_blocked=False,
                validation_result=mismatched_identity,
            )
            == "blocking-failure"
        )

    fabricated_result = control._ci_artifact_obligation_success_result(
        cast(
            "Mapping[str, object]",
            cast("Sequence[object]", plan["artifact-obligations"])[0],
        )
    )
    fabricated_evidence = {
        **validation_result,
        "commands": [
            {
                "builtin": "release-shaped-artifact",
                "evidence-source": "no-publish-validation",
                "outcome": "success",
                "artifact-obligation-results": [fabricated_result],
            }
        ],
    }
    assert (
        control._ci_validation_outcome(
            plan,
            "wg-release",
            dependency_blocked=False,
            validation_result=fabricated_evidence,
        )
        == "blocking-failure"
    )


@pytest.mark.parametrize(
    "extra_command",
    [
        {
            "outcome": "blocking-failure",
            "evidence-source": "no-publish-validation",
            "artifact-obligation-results": [],
        },
        {
            "outcome": "success",
            "evidence-source": "unsupported-sidecar-command",
            "artifact-obligation-results": [],
        },
        {"outcome": "success"},
        "malformed-command",
    ],
)
def test_ci_validation_release_shaped_sidecar_helpers_reject_extra_commands(
    extra_command: object,
) -> None:
    """Script-side no-publish source helpers fail closed on extra commands."""
    source_command = {
        "outcome": "success",
        "evidence-source": "no-publish-validation",
        "source-proof": {
            "kind": "no-publish-validation-result",
            "work-group-id": "wg-release",
        },
        "artifact-obligation-results": [],
    }
    validation_result = {"commands": [source_command, extra_command]}

    assert (
        control._ci_release_shaped_source_proof_from_validation_result(
            validation_result,
        )
        is None
    )
    assert (
        control._ci_no_publish_source_command_from_validation_result(
            validation_result,
        )
        is None
    )


def test_ci_validation_release_receipt_write_accepts_first_source_receipt() -> (
    None
):
    """Release-shaped validation can emit the first source-backed receipt."""
    scratch = SCRATCH / "ci-validation-release-shaped-first-source"
    shutil.rmtree(scratch, ignore_errors=True)
    scratch.mkdir(parents=True)
    try:
        plan_snapshot = plan_ci_validation_from_repo(
            CiValidationPlannerInputs(
                request=_ci_validation_push_request(
                    ["src/public/lib/nbgv-python/pyproject.toml"],
                ),
                repo_root=REPO_ROOT,
                expected_run_id="25887422010",
                expected_run_attempt="1",
                created_at="2026-05-14T21:09:21Z",
            )
        )
        plan_path = scratch / "validation-plan.json"
        changed_files_path = scratch / "changed-files.json"
        fact_snapshot_path = scratch / "fact-snapshot.json"
        assignments_path = scratch / "selector-assignments.json"
        materialize_outputs_path = scratch / "materialize-outputs.txt"
        validation_result_path = scratch / "validation-result.json"
        receipt_path = scratch / "receipt.json"
        observed_root = scratch / "observed-artifacts"
        plan_path.write_text(json.dumps(plan_snapshot.plan), encoding="utf-8")
        changed_files_path.write_text(
            json.dumps(plan_snapshot.changed_files_snapshot),
            encoding="utf-8",
        )
        fact_snapshot_path.write_text(
            json.dumps(plan_snapshot.fact_snapshot),
            encoding="utf-8",
        )
        assert (
            control._cmd_materialize_ci_work_groups(
                argparse.Namespace(
                    plan=str(plan_path),
                    changed_files_snapshot=str(changed_files_path),
                    fact_snapshot=str(fact_snapshot_path),
                    workflow="CI Validation",
                    writer_job="validation-work-groups",
                    created_at="2026-05-14T21:09:21Z",
                    assignments_out=str(assignments_path),
                    github_output=str(materialize_outputs_path),
                )
            )
            == 0
        )
        assignments = json.loads(assignments_path.read_text(encoding="utf-8"))
        matrix = {
            item["work-group-id"]: item
            for item in json.loads(
                _github_outputs(materialize_outputs_path)["work_group_matrix"]
            )
        }
        release_group = next(
            group
            for group in cast(
                "Sequence[Mapping[str, object]]",
                plan_snapshot.plan["work-groups"],
            )
            if group["kind"] == "release-shaped-artifact"
        )
        release_work_group_id = str(release_group["work-group-id"])
        for dependency in cast("Sequence[str]", release_group["depends-on"]):
            _stage_ci_observed_receipt(
                scratch=scratch,
                observed_root=observed_root,
                plan=plan_snapshot.plan,
                assignments=assignments,
                matrix=matrix,
                work_group_id=dependency,
                outcome="success",
                changed_files_snapshot=cast(
                    "Mapping[str, object]",
                    plan_snapshot.changed_files_snapshot,
                ),
                fact_snapshot=cast(
                    "Mapping[str, object]",
                    plan_snapshot.fact_snapshot,
                ),
            )

        assert (
            control._cmd_run_ci_validation_commands(
                argparse.Namespace(
                    matrix_work_group_json=json.dumps(
                        matrix[release_work_group_id],
                        separators=(",", ":"),
                    ),
                    plan=str(plan_path),
                    changed_files_snapshot=str(changed_files_path),
                    fact_snapshot=str(fact_snapshot_path),
                    assignments=str(assignments_path),
                    observed_artifacts_dir=str(observed_root),
                    observed_commit_sha="b" * 40,
                    repo_root=str(REPO_ROOT),
                    result_out=str(validation_result_path),
                    github_output=None,
                )
            )
            == 0
        )
        validation_result = json.loads(
            validation_result_path.read_text(encoding="utf-8")
        )
        command = validation_result["commands"][0]
        assert validation_result["outcome"] == "success"
        assert command["evidence-source"] == "no-publish-validation"
        assert command["source-proof"]["kind"] == "no-publish-validation-result"

        assert (
            control._cmd_write_ci_validation_receipt(
                argparse.Namespace(
                    plan=str(plan_path),
                    changed_files_snapshot=str(changed_files_path),
                    fact_snapshot=str(fact_snapshot_path),
                    assignments=str(assignments_path),
                    work_group_id=release_work_group_id,
                    matrix_work_group_json=json.dumps(
                        matrix[release_work_group_id],
                        separators=(",", ":"),
                    ),
                    workflow="CI Validation",
                    job=matrix[release_work_group_id]["writer-job"],
                    observed_artifacts_dir=str(observed_root),
                    observed_commit_sha="b" * 40,
                    validation_result=str(validation_result_path),
                    validation_outcome="success",
                    created_at="2026-05-14T21:09:24Z",
                    receipt_out=str(receipt_path),
                    github_output=None,
                )
            )
            == 0
        )
        receipt = json.loads(receipt_path.read_text(encoding="utf-8"))
        assignment = control._ci_assignment_for_work_group(
            assignments,
            release_work_group_id,
        )
        validate_ci_validation_receipt(
            receipt,
            plan=plan_snapshot.plan,
            selector_assignments_manifest=assignments,
            assignment=assignment,
            changed_files_snapshot=plan_snapshot.changed_files_snapshot,
            fact_snapshot=plan_snapshot.fact_snapshot,
        )
        detail = receipt["evidence"]["category-result"]["detail"]
        assert receipt["outcome"] == "success"
        assert detail["evidence-source"] == "no-publish-validation"
        assert detail["source-proof"] == command["source-proof"]
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_validation_release_receipt_write_accepts_observed_reuse() -> (  # noqa: PLR0915
    None
):
    """Release-shaped success is backed by an observed reusable receipt."""
    scratch = SCRATCH / "ci-validation-release-shaped-observed-reuse"
    shutil.rmtree(scratch, ignore_errors=True)
    scratch.mkdir(parents=True)
    try:
        request = _ci_validation_push_request(
            ["src/public/lib/nbgv-python/pyproject.toml"],
        )
        plan_snapshot = plan_ci_validation_from_repo(
            CiValidationPlannerInputs(
                request=request,
                repo_root=REPO_ROOT,
                expected_run_id="25887422010",
                expected_run_attempt="1",
                created_at="2026-05-14T21:09:21Z",
            )
        )
        plan_path = scratch / "validation-plan.json"
        changed_files_path = scratch / "changed-files.json"
        fact_snapshot_path = scratch / "fact-snapshot.json"
        assignments_path = scratch / "selector-assignments.json"
        materialize_outputs_path = scratch / "materialize-outputs.txt"
        validation_result_path = scratch / "validation-result.json"
        receipt_path = scratch / "receipt.json"
        receipt_outputs_path = scratch / "receipt-outputs.txt"
        observed_root = scratch / "observed-artifacts"
        plan_path.write_text(json.dumps(plan_snapshot.plan), encoding="utf-8")
        changed_files_path.write_text(
            json.dumps(plan_snapshot.changed_files_snapshot),
            encoding="utf-8",
        )
        fact_snapshot_path.write_text(
            json.dumps(plan_snapshot.fact_snapshot),
            encoding="utf-8",
        )
        assert (
            control._cmd_materialize_ci_work_groups(
                argparse.Namespace(
                    plan=str(plan_path),
                    changed_files_snapshot=str(changed_files_path),
                    fact_snapshot=str(fact_snapshot_path),
                    workflow="CI Validation",
                    writer_job="validation-work-groups",
                    created_at="2026-05-14T21:09:21Z",
                    assignments_out=str(assignments_path),
                    github_output=str(materialize_outputs_path),
                )
            )
            == 0
        )
        assignments = json.loads(assignments_path.read_text(encoding="utf-8"))
        matrix = {
            item["work-group-id"]: item
            for item in json.loads(
                _github_outputs(materialize_outputs_path)["work_group_matrix"]
            )
        }
        groups = cast(
            "Sequence[Mapping[str, object]]",
            plan_snapshot.plan["work-groups"],
        )
        release_group = next(
            group
            for group in groups
            if group["kind"] == "release-shaped-artifact"
        )
        release_work_group_id = str(release_group["work-group-id"])
        for dependency in cast("Sequence[str]", release_group["depends-on"]):
            _stage_ci_observed_receipt(
                scratch=scratch,
                observed_root=observed_root,
                plan=plan_snapshot.plan,
                assignments=assignments,
                matrix=matrix,
                work_group_id=dependency,
                outcome="success",
                changed_files_snapshot=cast(
                    "Mapping[str, object]",
                    plan_snapshot.changed_files_snapshot,
                ),
                fact_snapshot=cast(
                    "Mapping[str, object]",
                    plan_snapshot.fact_snapshot,
                ),
            )
        _stage_ci_release_shaped_observed_receipt(
            scratch=scratch,
            observed_root=observed_root,
            plan=plan_snapshot.plan,
            assignments=assignments,
            matrix=matrix,
            work_group_id=release_work_group_id,
            changed_files_snapshot=cast(
                "Mapping[str, object]",
                plan_snapshot.changed_files_snapshot,
            ),
            fact_snapshot=cast(
                "Mapping[str, object]",
                plan_snapshot.fact_snapshot,
            ),
        )
        release_assignment = control._ci_assignment_for_work_group(
            assignments,
            release_work_group_id,
        )
        release_receipt_path = (
            observed_root
            / artifact_physical_name(
                str(release_assignment["receipt-artifact-ref"])
            )
            / "receipt.json"
        )
        genuine_release_receipt = json.loads(
            release_receipt_path.read_text(encoding="utf-8")
        )
        synthetic_release_receipt = deepcopy(genuine_release_receipt)
        synthetic_detail = cast(
            "dict[str, object]",
            synthetic_release_receipt["evidence"]["category-result"]["detail"],
        )
        synthetic_detail.pop("evidence-source", None)
        synthetic_detail.pop("source-proof", None)
        release_receipt_path.write_text(
            json.dumps(synthetic_release_receipt),
            encoding="utf-8",
        )
        synthetic_result_path = scratch / "synthetic-validation-result.json"
        assert (
            control._cmd_run_ci_validation_commands(
                argparse.Namespace(
                    matrix_work_group_json=json.dumps(
                        matrix[release_work_group_id],
                        separators=(",", ":"),
                    ),
                    plan=str(plan_path),
                    changed_files_snapshot=str(changed_files_path),
                    fact_snapshot=str(fact_snapshot_path),
                    assignments=str(assignments_path),
                    observed_artifacts_dir=str(observed_root),
                    observed_commit_sha="b" * 40,
                    repo_root=str(REPO_ROOT),
                    result_out=str(synthetic_result_path),
                    github_output=None,
                )
            )
            == 0
        )
        synthetic_result = json.loads(
            synthetic_result_path.read_text(encoding="utf-8")
        )
        assert synthetic_result["outcome"] == "blocking-failure"
        release_receipt_path.write_text(
            json.dumps(genuine_release_receipt),
            encoding="utf-8",
        )
        release_validation_result_path = (
            release_receipt_path.parent / "validation-result.json"
        )
        release_validation_result_json = (
            release_validation_result_path.read_text(
                encoding="utf-8",
            )
        )
        release_validation_result_path.unlink()
        missing_source_result_path = (
            scratch / "missing-source-validation-result.json"
        )
        assert (
            control._cmd_run_ci_validation_commands(
                argparse.Namespace(
                    matrix_work_group_json=json.dumps(
                        matrix[release_work_group_id],
                        separators=(",", ":"),
                    ),
                    plan=str(plan_path),
                    changed_files_snapshot=str(changed_files_path),
                    fact_snapshot=str(fact_snapshot_path),
                    assignments=str(assignments_path),
                    observed_artifacts_dir=str(observed_root),
                    observed_commit_sha="b" * 40,
                    repo_root=str(REPO_ROOT),
                    result_out=str(missing_source_result_path),
                    github_output=None,
                )
            )
            == 0
        )
        missing_source_result = json.loads(
            missing_source_result_path.read_text(encoding="utf-8")
        )
        assert missing_source_result["outcome"] == "blocking-failure"
        release_validation_result_path.write_text(
            release_validation_result_json,
            encoding="utf-8",
        )
        mismatched_source_result_path = (
            scratch / "mismatched-source-result.json"
        )
        mismatched_validation_result = json.loads(
            release_validation_result_json
        )
        mismatched_command = mismatched_validation_result["commands"][0]
        mismatched_proof_digest = mismatched_command["source-proof"][
            "artifact-digests"
        ][0]
        mismatched_proof_digest["digest"] = "e" * 64
        release_validation_result_path.write_text(
            json.dumps(mismatched_validation_result),
            encoding="utf-8",
        )
        assert (
            control._cmd_run_ci_validation_commands(
                argparse.Namespace(
                    matrix_work_group_json=json.dumps(
                        matrix[release_work_group_id],
                        separators=(",", ":"),
                    ),
                    plan=str(plan_path),
                    changed_files_snapshot=str(changed_files_path),
                    fact_snapshot=str(fact_snapshot_path),
                    assignments=str(assignments_path),
                    observed_artifacts_dir=str(observed_root),
                    observed_commit_sha="b" * 40,
                    repo_root=str(REPO_ROOT),
                    result_out=str(mismatched_source_result_path),
                    github_output=None,
                )
            )
            == 0
        )
        mismatched_source_result = json.loads(
            mismatched_source_result_path.read_text(encoding="utf-8")
        )
        assert mismatched_source_result["outcome"] == "blocking-failure"
        release_validation_result_path.write_text(
            release_validation_result_json,
            encoding="utf-8",
        )
        assert (
            control._cmd_run_ci_validation_commands(
                argparse.Namespace(
                    matrix_work_group_json=json.dumps(
                        matrix[release_work_group_id],
                        separators=(",", ":"),
                    ),
                    plan=str(plan_path),
                    changed_files_snapshot=str(changed_files_path),
                    fact_snapshot=str(fact_snapshot_path),
                    assignments=str(assignments_path),
                    observed_artifacts_dir=str(observed_root),
                    observed_commit_sha="b" * 40,
                    repo_root=str(REPO_ROOT),
                    result_out=str(validation_result_path),
                    github_output=None,
                )
            )
            == 0
        )
        validation_result = json.loads(
            validation_result_path.read_text(encoding="utf-8")
        )
        command = validation_result["commands"][0]
        assert validation_result["outcome"] == "success"
        assert command["evidence-source"] == "reused-validation-receipt"
        assert (
            control._cmd_write_ci_validation_receipt(
                argparse.Namespace(
                    plan=str(plan_path),
                    changed_files_snapshot=str(changed_files_path),
                    fact_snapshot=str(fact_snapshot_path),
                    assignments=str(assignments_path),
                    work_group_id=release_work_group_id,
                    matrix_work_group_json=json.dumps(
                        matrix[release_work_group_id],
                        separators=(",", ":"),
                    ),
                    workflow="CI Validation",
                    job=matrix[release_work_group_id]["writer-job"],
                    observed_artifacts_dir=str(observed_root),
                    observed_commit_sha="b" * 40,
                    validation_result=str(validation_result_path),
                    validation_outcome="success",
                    created_at="2026-05-14T21:09:24Z",
                    receipt_out=str(receipt_path),
                    github_output=str(receipt_outputs_path),
                )
            )
            == 0
        )
        receipt = json.loads(receipt_path.read_text(encoding="utf-8"))
        assignment = control._ci_assignment_for_work_group(
            assignments,
            release_work_group_id,
        )
        validate_ci_validation_receipt(
            receipt,
            plan=plan_snapshot.plan,
            selector_assignments_manifest=assignments,
            assignment=assignment,
            changed_files_snapshot=plan_snapshot.changed_files_snapshot,
            fact_snapshot=plan_snapshot.fact_snapshot,
        )
        assert receipt["outcome"] == "success"
        assert receipt["diagnostics"] == []
        receipt_detail = receipt["evidence"]["category-result"]["detail"]
        assert receipt_detail["evidence-source"] == "reused-validation-receipt"
        assert receipt_detail["reused-receipt"] == command["reused-receipt"]
        observed_receipts = control._ci_observed_receipt_inputs(
            plan=plan_snapshot.plan,
            assignments=assignments,
            observed_artifacts_dir=str(observed_root),
            changed_files_snapshot=cast(
                "Mapping[str, object]",
                plan_snapshot.changed_files_snapshot,
            ),
            fact_snapshot=cast(
                "Mapping[str, object]",
                plan_snapshot.fact_snapshot,
            ),
        )
        assert control._ci_receipt_reusable_for_release_shape(
            receipt,
            plan_snapshot.plan,
            assignments,
            release_work_group_id,
            "b" * 40,
            observed_receipts=observed_receipts,
            changed_files_snapshot=cast(
                "Mapping[str, object]",
                plan_snapshot.changed_files_snapshot,
            ),
            fact_snapshot=cast(
                "Mapping[str, object]",
                plan_snapshot.fact_snapshot,
            ),
        )
        mutated_receipt = deepcopy(receipt)
        mutated_results = cast(
            "list[dict[str, object]]",
            mutated_receipt["evidence"]["category-result"]["detail"][
                "artifact-obligation-results"
            ],
        )
        mutated_digest = cast(
            "list[dict[str, object]]",
            cast(
                "dict[str, object]",
                cast("dict[str, object]", mutated_results[0]["artifact"])[
                    "observed"
                ],
            )["digests"],
        )[0]
        mutated_digest["digest"] = "e" * 64
        assert not control._ci_receipt_reusable_for_release_shape(
            mutated_receipt,
            plan_snapshot.plan,
            assignments,
            release_work_group_id,
            "b" * 40,
            observed_receipts=observed_receipts,
            changed_files_snapshot=cast(
                "Mapping[str, object]",
                plan_snapshot.changed_files_snapshot,
            ),
            fact_snapshot=cast(
                "Mapping[str, object]",
                plan_snapshot.fact_snapshot,
            ),
        )
        malformed_extra_result = deepcopy(validation_result)
        malformed_commands = cast(
            "list[object]",
            malformed_extra_result["commands"],
        )
        malformed_commands.append("malformed-command")
        validation_result_path.write_text(
            json.dumps(malformed_extra_result),
            encoding="utf-8",
        )
        assert not control._ci_validation_result_has_success_evidence(
            plan_snapshot.plan,
            release_work_group_id,
            malformed_extra_result,
            assignments=assignments,
            observed_artifacts_dir=str(observed_root),
            observed_commit_sha="b" * 40,
            changed_files_snapshot=cast(
                "Mapping[str, object]",
                plan_snapshot.changed_files_snapshot,
            ),
            fact_snapshot=cast(
                "Mapping[str, object]",
                plan_snapshot.fact_snapshot,
            ),
        )
        malformed_receipt_path = scratch / "malformed-extra-receipt.json"
        assert (
            control._cmd_write_ci_validation_receipt(
                argparse.Namespace(
                    plan=str(plan_path),
                    changed_files_snapshot=str(changed_files_path),
                    fact_snapshot=str(fact_snapshot_path),
                    assignments=str(assignments_path),
                    work_group_id=release_work_group_id,
                    matrix_work_group_json=json.dumps(
                        matrix[release_work_group_id],
                        separators=(",", ":"),
                    ),
                    workflow="CI Validation",
                    job=matrix[release_work_group_id]["writer-job"],
                    observed_artifacts_dir=str(observed_root),
                    observed_commit_sha="b" * 40,
                    validation_result=str(validation_result_path),
                    validation_outcome="success",
                    created_at="2026-05-14T21:09:24Z",
                    receipt_out=str(malformed_receipt_path),
                    github_output=None,
                )
            )
            == 0
        )
        malformed_receipt = json.loads(
            malformed_receipt_path.read_text(encoding="utf-8")
        )
        assert malformed_receipt["outcome"] == "blocking-failure"
        observed_receipts = control._ci_observed_receipt_inputs(
            plan=plan_snapshot.plan,
            assignments=assignments,
            observed_artifacts_dir=str(observed_root),
            changed_files_snapshot=cast(
                "Mapping[str, object]",
                plan_snapshot.changed_files_snapshot,
            ),
            fact_snapshot=cast(
                "Mapping[str, object]",
                plan_snapshot.fact_snapshot,
            ),
        )
        assert control._ci_receipt_reusable_for_release_shape(
            receipt,
            plan_snapshot.plan,
            assignments,
            release_work_group_id,
            "b" * 40,
            observed_receipts=observed_receipts,
            changed_files_snapshot=cast(
                "Mapping[str, object]",
                plan_snapshot.changed_files_snapshot,
            ),
            fact_snapshot=cast(
                "Mapping[str, object]",
                plan_snapshot.fact_snapshot,
            ),
        )
        reused_ref = command["reused-receipt"]["artifact-ref"]
        for field, value in (
            ("observed-writer-id", None),
            ("writer-work-group-id", "wg-other"),
        ):
            tampered_observed_receipts = [
                CiValidationObservedReceiptInput(
                    manifest_entry={
                        **item.manifest_entry,
                        field: value,
                    }
                    if item.manifest_entry.get("artifact-ref") == reused_ref
                    else item.manifest_entry,
                    receipt=item.receipt,
                    raw_receipt_bytes=item.raw_receipt_bytes,
                    validation_result=item.validation_result,
                )
                for item in observed_receipts
            ]
            assert not control._ci_receipt_reusable_for_release_shape(
                receipt,
                plan_snapshot.plan,
                assignments,
                release_work_group_id,
                "b" * 40,
                observed_receipts=tampered_observed_receipts,
                changed_files_snapshot=cast(
                    "Mapping[str, object]",
                    plan_snapshot.changed_files_snapshot,
                ),
                fact_snapshot=cast(
                    "Mapping[str, object]",
                    plan_snapshot.fact_snapshot,
                ),
            )
        assert (
            _github_outputs(receipt_outputs_path)["dependency_blocked"]
            == "false"
        )
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_validation_aggregate_requires_release_shaped_source_sidecar() -> (  # noqa: PLR0915
    None
):
    """Final aggregation fails closed without observed release source proof."""
    scratch = SCRATCH / "ci-validation-release-shaped-aggregate-source"
    shutil.rmtree(scratch, ignore_errors=True)
    scratch.mkdir(parents=True)
    try:
        plan_snapshot = plan_ci_validation_from_repo(
            CiValidationPlannerInputs(
                request=_ci_validation_push_request(
                    ["src/public/lib/nbgv-python/pyproject.toml"],
                ),
                repo_root=REPO_ROOT,
                expected_run_id="25887422010",
                expected_run_attempt="1",
                created_at="2026-05-14T21:09:21Z",
            )
        )
        plan_path = scratch / "validation-plan.json"
        changed_files_path = scratch / "changed-files.json"
        fact_snapshot_path = scratch / "fact-snapshot.json"
        assignments_path = scratch / "selector-assignments.json"
        materialize_outputs_path = scratch / "materialize-outputs.txt"
        observed_root = scratch / "observed-artifacts"
        plan_path.write_text(json.dumps(plan_snapshot.plan), encoding="utf-8")
        changed_files_path.write_text(
            json.dumps(plan_snapshot.changed_files_snapshot),
            encoding="utf-8",
        )
        fact_snapshot_path.write_text(
            json.dumps(plan_snapshot.fact_snapshot),
            encoding="utf-8",
        )
        assert (
            control._cmd_materialize_ci_work_groups(
                argparse.Namespace(
                    plan=str(plan_path),
                    changed_files_snapshot=str(changed_files_path),
                    fact_snapshot=str(fact_snapshot_path),
                    workflow="CI Validation",
                    writer_job="validation-work-groups",
                    created_at="2026-05-14T21:09:21Z",
                    assignments_out=str(assignments_path),
                    github_output=str(materialize_outputs_path),
                )
            )
            == 0
        )
        assignments = json.loads(assignments_path.read_text(encoding="utf-8"))
        matrix = {
            item["work-group-id"]: item
            for item in json.loads(
                _github_outputs(materialize_outputs_path)["work_group_matrix"]
            )
        }
        release_group = next(
            group
            for group in cast(
                "Sequence[Mapping[str, object]]",
                plan_snapshot.plan["work-groups"],
            )
            if group["kind"] == "release-shaped-artifact"
        )
        release_work_group_id = str(release_group["work-group-id"])
        for group in cast(
            "Sequence[Mapping[str, object]]",
            plan_snapshot.plan["work-groups"],
        ):
            work_group_id = str(group["work-group-id"])
            if group["kind"] == "evidence-aggregation":
                continue
            if group["kind"] == "release-shaped-artifact":
                _stage_ci_release_shaped_observed_receipt(
                    scratch=scratch,
                    observed_root=observed_root,
                    plan=plan_snapshot.plan,
                    assignments=assignments,
                    matrix=matrix,
                    work_group_id=work_group_id,
                    changed_files_snapshot=cast(
                        "Mapping[str, object]",
                        plan_snapshot.changed_files_snapshot,
                    ),
                    fact_snapshot=cast(
                        "Mapping[str, object]",
                        plan_snapshot.fact_snapshot,
                    ),
                )
                continue
            _stage_ci_observed_receipt(
                scratch=scratch,
                observed_root=observed_root,
                plan=plan_snapshot.plan,
                assignments=assignments,
                matrix=matrix,
                work_group_id=work_group_id,
                outcome="success",
                changed_files_snapshot=cast(
                    "Mapping[str, object]",
                    plan_snapshot.changed_files_snapshot,
                ),
                fact_snapshot=cast(
                    "Mapping[str, object]",
                    plan_snapshot.fact_snapshot,
                ),
            )
        release_assignment = control._ci_assignment_for_work_group(
            assignments,
            release_work_group_id,
        )
        release_dir = observed_root / artifact_physical_name(
            str(release_assignment["receipt-artifact-ref"])
        )
        validation_result_path = release_dir / "validation-result.json"
        original_validation_result = validation_result_path.read_text(
            encoding="utf-8"
        )

        def aggregate(verdict_name: str) -> Mapping[str, object]:
            aggregate_path = scratch / f"{verdict_name}-aggregate.json"
            manifest_path = scratch / f"{verdict_name}-manifest.json"
            control._cmd_aggregate_ci_evidence(
                argparse.Namespace(
                    repository="hcoona/three",
                    workflow="CI Validation",
                    run_id="25887422010",
                    run_attempt="1",
                    plan=str(plan_path),
                    changed_files_snapshot=str(changed_files_path),
                    fact_snapshot=str(fact_snapshot_path),
                    assignments=str(assignments_path),
                    observed_artifacts_dir=str(observed_root),
                    created_at="2026-05-14T21:09:25Z",
                    receipt_manifest_out=str(manifest_path),
                    aggregate_out=str(aggregate_path),
                    github_output=None,
                )
            )
            return json.loads(aggregate_path.read_text(encoding="utf-8"))

        validation_result_path.unlink()
        missing_sidecar = aggregate("missing-sidecar")
        assert missing_sidecar["verdict"] == "failed"
        assert missing_sidecar["reason"]["blocking-validation-failure"] is True

        mismatched_result = json.loads(original_validation_result)
        command = mismatched_result["commands"][0]
        command["source-proof"]["artifact-digests"][0]["digest"] = "e" * 64
        validation_result_path.write_text(
            json.dumps(mismatched_result),
            encoding="utf-8",
        )
        mismatched_sidecar = aggregate("mismatched-sidecar")
        assert mismatched_sidecar["verdict"] == "failed"
        assert (
            mismatched_sidecar["reason"]["blocking-validation-failure"] is True
        )

        for index, extra_command in enumerate(
            [
                {
                    "outcome": "blocking-failure",
                    "evidence-source": "no-publish-validation",
                    "diagnostics": [],
                },
                {
                    "outcome": "success",
                    "evidence-source": "unsupported-sidecar-command",
                    "artifact-obligation-results": [],
                },
                {"outcome": "success"},
                "malformed-command",
            ],
        ):
            extra_command_result = json.loads(original_validation_result)
            extra_command_result["commands"].append(extra_command)
            validation_result_path.write_text(
                json.dumps(extra_command_result),
                encoding="utf-8",
            )
            extra_sidecar = aggregate(f"extra-sidecar-command-{index}")
            assert extra_sidecar["verdict"] == "failed"
            assert (
                extra_sidecar["reason"]["blocking-validation-failure"] is True
            )

        validation_result_path.write_text(
            original_validation_result,
            encoding="utf-8",
        )
        accepted = aggregate("accepted")
        assert accepted["verdict"] == "passed"
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_validation_materializer_fails_closed_for_extra_layers() -> None:
    """Static workflow materialization rejects unsupported dependency depth."""
    scratch = SCRATCH / "ci-validation-too-many-layers"
    shutil.rmtree(scratch, ignore_errors=True)
    scratch.mkdir(parents=True)
    try:
        request = _ci_validation_push_request(
            [
                (
                    "src/public/lib/three-workflow-release-planner/src/"
                    "three_workflow_release_planner/ci_validation_planner.py"
                )
            ],
        )
        plan_snapshot = plan_ci_validation_from_repo(
            CiValidationPlannerInputs(
                request=request,
                repo_root=REPO_ROOT,
                expected_run_id="25887422010",
                expected_run_attempt="1",
                created_at="2026-05-14T21:09:21Z",
            )
        )
        plan = cast("dict[str, object]", deepcopy(plan_snapshot.plan))
        work_groups = cast("list[dict[str, object]]", plan["work-groups"])
        executable = [
            group
            for group in work_groups
            if group["kind"] != "evidence-aggregation"
        ][:4]
        assert len(executable) == 4
        for previous, current in pairwise(executable):
            current["depends-on"] = [previous["work-group-id"]]
        plan["plan-digest"] = ci_validation_plan_digest(plan)
        plan_path = scratch / "validation-plan.json"
        changed_files_path = scratch / "changed-files.json"
        fact_snapshot_path = scratch / "fact-snapshot.json"
        assignments_path = scratch / "selector-assignments.json"
        plan_path.write_text(json.dumps(plan), encoding="utf-8")
        changed_files_path.write_text(
            json.dumps(plan_snapshot.changed_files_snapshot),
            encoding="utf-8",
        )
        fact_snapshot_path.write_text(
            json.dumps(plan_snapshot.fact_snapshot),
            encoding="utf-8",
        )

        with pytest.raises(RuntimeError, match="supports 3"):
            control._cmd_materialize_ci_work_groups(
                argparse.Namespace(
                    plan=str(plan_path),
                    changed_files_snapshot=str(changed_files_path),
                    fact_snapshot=str(fact_snapshot_path),
                    workflow="CI Validation",
                    writer_job="validation-work-groups",
                    created_at="2026-05-14T21:09:21Z",
                    assignments_out=str(assignments_path),
                    github_output=None,
                )
            )
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def _stage_ci_observed_receipt(  # noqa: PLR0913
    *,
    scratch: Path,
    observed_root: Path,
    plan: Mapping[str, object],
    assignments: Mapping[str, object],
    matrix: Mapping[str, Mapping[str, object]],
    work_group_id: str,
    outcome: ReceiptOutcome,
    changed_files_snapshot: Mapping[str, object],
    fact_snapshot: Mapping[str, object],
) -> None:
    assignment = control._ci_assignment_for_work_group(
        assignments,
        work_group_id,
    )
    diagnostics = control._ci_validation_diagnostics(
        plan,
        work_group_id,
        outcome=outcome,
    )
    evidence = control._ci_validation_evidence(
        plan,
        work_group_id,
        outcome=outcome,
        diagnostics=diagnostics,
        fact_snapshot=fact_snapshot,
    )
    receipt = freeze_ci_validation_receipt(
        plan=plan,
        selector_assignments_manifest=assignments,
        assignment=assignment,
        receipt_id=str(assignment["assignment-id"]),
        created_at="2026-05-14T21:09:22Z",
        execution_observed_commit_sha="b" * 40,
        outcome=outcome,
        evidence=evidence,
        diagnostics=diagnostics,
        changed_files_snapshot=changed_files_snapshot,
        fact_snapshot=fact_snapshot,
    )
    observation_path = scratch / f"{work_group_id}-writer-observation.json"
    metadata_path = scratch / f"{work_group_id}-receipt-metadata.json"
    assert (
        control._cmd_write_ci_validation_writer_observation(
            argparse.Namespace(
                plan=str(scratch / "validation-plan.json"),
                changed_files_snapshot=str(scratch / "changed-files.json"),
                fact_snapshot=str(scratch / "fact-snapshot.json"),
                assignments=str(scratch / "selector-assignments.json"),
                work_group_id=work_group_id,
                matrix_work_group_json=json.dumps(
                    matrix[work_group_id],
                    separators=(",", ":"),
                ),
                workflow="CI Validation",
                job=matrix[work_group_id]["writer-job"],
                artifact_instance_id=f"{work_group_id}-artifact",
                created_at="2026-05-14T21:09:23Z",
                observation_out=str(observation_path),
                metadata_out=str(metadata_path),
                github_output=None,
            )
        )
        == 0
    )
    receipt_dir = observed_root / artifact_physical_name(
        str(assignment["receipt-artifact-ref"])
    )
    observation_dir = observed_root / artifact_physical_name(
        str(assignment["writer-observation-ref"])
    )
    receipt_dir.mkdir(parents=True)
    observation_dir.mkdir(parents=True)
    (receipt_dir / "receipt.json").write_text(
        json.dumps(receipt),
        encoding="utf-8",
    )
    shutil.copyfile(
        observation_path,
        observation_dir / "writer-observation.json",
    )
    shutil.copyfile(
        metadata_path,
        observation_dir / "receipt-artifact-metadata.json",
    )


def _stage_ci_release_shaped_observed_receipt(  # noqa: PLR0913
    *,
    scratch: Path,
    observed_root: Path,
    plan: Mapping[str, object],
    assignments: Mapping[str, object],
    matrix: Mapping[str, Mapping[str, object]],
    work_group_id: str,
    changed_files_snapshot: Mapping[str, object],
    fact_snapshot: Mapping[str, object],
) -> None:
    assignment = control._ci_assignment_for_work_group(
        assignments,
        work_group_id,
    )
    obligation_results: list[Mapping[str, object]] = []
    for obligation in control._ci_plan_records_for_work_group(
        plan,
        "artifact-obligations",
        work_group_id,
    ):
        result = control._ci_artifact_obligation_success_result(obligation)
        descriptor_path = str(obligation["descriptor-path"])
        descriptor_fact = control._ci_descriptor_fact(
            fact_snapshot,
            descriptor_path,
        )
        cast("dict[str, object]", result["descriptor"])["identity"] = (
            descriptor_fact.get("descriptor-identity")
            if descriptor_fact is not None
            else None
        )
        observed_artifact = cast(
            "dict[str, object]",
            cast("dict[str, object]", result["artifact"])["observed"],
        )
        observed_artifact["digests"] = [
            {
                "artifact-ref": artifact_ref,
                "algorithm": "sha256",
                "digest": "f" * 64,
                "digest-available": True,
                "diagnostics": [],
            }
            for artifact_ref in control._ci_artifact_expected_refs(obligation)
        ]
        obligation_results.append(result)
    validation_result = {
        "work-group-id": work_group_id,
        "kind": matrix[work_group_id]["kind"],
        "runner-family": matrix[work_group_id]["runner-family"],
        "coverage-target": matrix[work_group_id]["coverage-target"],
        "observed-commit-sha": "b" * 40,
        "outcome": "success",
        "commands": [
            {
                "builtin": "release-shaped-artifact",
                "evidence-source": "no-publish-validation",
                "source-proof": _release_shaped_no_publish_source_proof(
                    work_group_id=work_group_id,
                    matrix_work_group=matrix[work_group_id],
                    obligation_results=obligation_results,
                ),
                "outcome": "success",
                "artifact-obligation-results": obligation_results,
            }
        ],
    }
    diagnostics: list[Mapping[str, object]] = []
    receipt = freeze_ci_validation_receipt(
        plan=plan,
        selector_assignments_manifest=assignments,
        assignment=assignment,
        receipt_id=str(assignment["assignment-id"]),
        created_at="2026-05-14T21:09:22Z",
        execution_observed_commit_sha="b" * 40,
        outcome="success",
        evidence=control._ci_validation_evidence(
            plan,
            work_group_id,
            outcome="success",
            diagnostics=diagnostics,
            validation_result=validation_result,
            fact_snapshot=fact_snapshot,
        ),
        diagnostics=diagnostics,
        changed_files_snapshot=changed_files_snapshot,
        fact_snapshot=fact_snapshot,
    )
    observation_path = scratch / f"{work_group_id}-writer-observation.json"
    metadata_path = scratch / f"{work_group_id}-receipt-metadata.json"
    assert (
        control._cmd_write_ci_validation_writer_observation(
            argparse.Namespace(
                plan=str(scratch / "validation-plan.json"),
                changed_files_snapshot=str(scratch / "changed-files.json"),
                fact_snapshot=str(scratch / "fact-snapshot.json"),
                assignments=str(scratch / "selector-assignments.json"),
                work_group_id=work_group_id,
                matrix_work_group_json=json.dumps(
                    matrix[work_group_id],
                    separators=(",", ":"),
                ),
                workflow="CI Validation",
                job=matrix[work_group_id]["writer-job"],
                artifact_instance_id=f"{work_group_id}-artifact",
                created_at="2026-05-14T21:09:23Z",
                observation_out=str(observation_path),
                metadata_out=str(metadata_path),
                github_output=None,
            )
        )
        == 0
    )
    receipt_dir = observed_root / artifact_physical_name(
        str(assignment["receipt-artifact-ref"])
    )
    observation_dir = observed_root / artifact_physical_name(
        str(assignment["writer-observation-ref"])
    )
    receipt_dir.mkdir(parents=True)
    observation_dir.mkdir(parents=True)
    (receipt_dir / "receipt.json").write_text(
        json.dumps(receipt),
        encoding="utf-8",
    )
    (receipt_dir / "validation-result.json").write_text(
        json.dumps(validation_result),
        encoding="utf-8",
    )
    shutil.copyfile(
        observation_path,
        observation_dir / "writer-observation.json",
    )
    shutil.copyfile(
        metadata_path,
        observation_dir / "receipt-artifact-metadata.json",
    )


def _release_shaped_no_publish_source_proof(
    *,
    work_group_id: str,
    matrix_work_group: Mapping[str, object],
    obligation_results: Sequence[Mapping[str, object]],
) -> Mapping[str, object]:
    artifact_digests: list[Mapping[str, object]] = []
    for result in obligation_results:
        artifact = cast("Mapping[str, object]", result["artifact"])
        observed = cast("Mapping[str, object]", artifact["observed"])
        digest_entries = cast(
            "Sequence[Mapping[str, object]]",
            observed["digests"],
        )
        for digest in digest_entries:
            artifact_digests.append(
                {
                    "artifact-ref": digest["artifact-ref"],
                    "algorithm": digest["algorithm"],
                    "digest": digest["digest"],
                }
            )
    return {
        "kind": "no-publish-validation-result",
        "work-group-id": work_group_id,
        "coverage-target": matrix_work_group["coverage-target"],
        "observed-commit-sha": "b" * 40,
        "artifact-digests": sorted(
            artifact_digests,
            key=lambda item: str(item["artifact-ref"]),
        ),
    }


def _clear_ci_evidence_diagnostics(value: object) -> None:
    if isinstance(value, dict):
        for key, item in value.items():
            if key == "diagnostics" and isinstance(item, list):
                item.clear()
            else:
                _clear_ci_evidence_diagnostics(item)
    elif isinstance(value, list):
        for item in value:
            _clear_ci_evidence_diagnostics(item)


def test_ci_validation_dependency_blocking_uses_declared_prerequisites() -> (  # noqa: PLR0915
    None
):
    """Unrelated prior-layer receipts do not block independent dependents."""
    scratch = SCRATCH / "ci-validation-prerequisite-specific-blocking"
    shutil.rmtree(scratch, ignore_errors=True)
    scratch.mkdir(parents=True)
    try:
        request = _ci_validation_push_request(
            ["src/public/lib/nbgv-python/pyproject.toml"],
        )
        plan_snapshot = plan_ci_validation_from_repo(
            CiValidationPlannerInputs(
                request=request,
                repo_root=REPO_ROOT,
                expected_run_id="25887422010",
                expected_run_attempt="1",
                created_at="2026-05-14T21:09:21Z",
            )
        )
        plan_path = scratch / "validation-plan.json"
        changed_files_path = scratch / "changed-files.json"
        fact_snapshot_path = scratch / "fact-snapshot.json"
        assignments_path = scratch / "selector-assignments.json"
        output_path = scratch / "outputs.txt"
        receipt_path = scratch / "dependent-receipt.json"
        observed_root = scratch / "observed-artifacts"
        plan_path.write_text(json.dumps(plan_snapshot.plan), encoding="utf-8")
        changed_files_path.write_text(
            json.dumps(plan_snapshot.changed_files_snapshot),
            encoding="utf-8",
        )
        fact_snapshot_path.write_text(
            json.dumps(plan_snapshot.fact_snapshot),
            encoding="utf-8",
        )
        assert (
            control._cmd_materialize_ci_work_groups(
                argparse.Namespace(
                    plan=str(plan_path),
                    changed_files_snapshot=str(changed_files_path),
                    fact_snapshot=str(fact_snapshot_path),
                    workflow="CI Validation",
                    writer_job="validation-work-groups",
                    created_at="2026-05-14T21:09:21Z",
                    assignments_out=str(assignments_path),
                    github_output=str(output_path),
                )
            )
            == 0
        )
        assignments = json.loads(assignments_path.read_text(encoding="utf-8"))
        matrix = {
            item["work-group-id"]: item
            for item in json.loads(
                _github_outputs(output_path)["work_group_matrix"]
            )
        }
        groups = cast(
            "Sequence[Mapping[str, object]]",
            plan_snapshot.plan["work-groups"],
        )
        release_group = next(
            group
            for group in groups
            if group["kind"] == "release-shaped-artifact"
            and group["depends-on"]
        )
        dependent_id = str(release_group["work-group-id"])
        groups_by_id = {str(group["work-group-id"]): group for group in groups}
        non_release_group = next(
            groups_by_id[dependency]
            for dependency in cast("Sequence[str]", release_group["depends-on"])
            if groups_by_id[dependency]["kind"] == "ecosystem-gate"
        )
        non_release_id = str(non_release_group["work-group-id"])
        dependencies = {
            str(item)
            for item in cast("Sequence[object]", release_group["depends-on"])
        }
        unrelated_id = next(
            work_group_id
            for work_group_id, entry in matrix.items()
            if work_group_id not in dependencies
            and work_group_id != dependent_id
        )
        for dependency in dependencies:
            _stage_ci_observed_receipt(
                scratch=scratch,
                observed_root=observed_root,
                plan=plan_snapshot.plan,
                assignments=assignments,
                matrix=matrix,
                work_group_id=dependency,
                outcome="success",
                changed_files_snapshot=plan_snapshot.changed_files_snapshot,
                fact_snapshot=plan_snapshot.fact_snapshot,
            )
        _stage_ci_observed_receipt(
            scratch=scratch,
            observed_root=observed_root,
            plan=plan_snapshot.plan,
            assignments=assignments,
            matrix=matrix,
            work_group_id=unrelated_id,
            outcome="skipped",
            changed_files_snapshot=plan_snapshot.changed_files_snapshot,
            fact_snapshot=plan_snapshot.fact_snapshot,
        )

        dependent_outputs = scratch / "dependent-outputs.txt"
        assert (
            control._cmd_write_ci_validation_receipt(
                argparse.Namespace(
                    plan=str(plan_path),
                    changed_files_snapshot=str(changed_files_path),
                    fact_snapshot=str(fact_snapshot_path),
                    assignments=str(assignments_path),
                    work_group_id=dependent_id,
                    matrix_work_group_json=json.dumps(
                        matrix[dependent_id],
                        separators=(",", ":"),
                    ),
                    workflow="CI Validation",
                    job=matrix[dependent_id]["writer-job"],
                    observed_artifacts_dir=str(observed_root),
                    observed_commit_sha="b" * 40,
                    created_at="2026-05-14T21:09:24Z",
                    receipt_out=str(receipt_path),
                    github_output=str(dependent_outputs),
                )
            )
            == 0
        )
        assert (
            _github_outputs(dependent_outputs)["dependency_blocked"] == "false"
        )
        unblocked_receipt = json.loads(receipt_path.read_text(encoding="utf-8"))
        assert unblocked_receipt["outcome"] == "blocking-failure"
        unblocked_diagnostic = unblocked_receipt["diagnostics"][0]
        assert unblocked_diagnostic["code"] == "validation-work-failed"
        assert unblocked_diagnostic["detail"] == "tooling"
        forced_outputs = scratch / "forced-dependent-outputs.txt"
        assert (
            control._cmd_write_ci_validation_receipt(
                argparse.Namespace(
                    plan=str(plan_path),
                    changed_files_snapshot=str(changed_files_path),
                    fact_snapshot=str(fact_snapshot_path),
                    assignments=str(assignments_path),
                    work_group_id=dependent_id,
                    matrix_work_group_json=json.dumps(
                        matrix[dependent_id],
                        separators=(",", ":"),
                    ),
                    workflow="CI Validation",
                    job=matrix[dependent_id]["writer-job"],
                    dependency_blocked="true",
                    observed_artifacts_dir=str(observed_root),
                    observed_commit_sha="b" * 40,
                    created_at="2026-05-14T21:09:24Z",
                    receipt_out=str(receipt_path),
                    github_output=str(forced_outputs),
                )
            )
            == 0
        )
        assert _github_outputs(forced_outputs)["dependency_blocked"] == "false"
        forced_receipt = json.loads(receipt_path.read_text(encoding="utf-8"))
        assert forced_receipt["outcome"] == "blocking-failure"
        assert forced_receipt["diagnostics"][0]["code"] == (
            "validation-work-failed"
        )
        non_release_outputs = scratch / "non-release-dependent-outputs.txt"
        non_release_validation_result = scratch / "non-release-validation.json"
        non_release_capabilities = cast(
            "Sequence[str]",
            cast(
                "Mapping[str, object]",
                non_release_group["expected-evidence"],
            )["planned-capabilities"],
        )
        non_release_validation_result.write_text(
            json.dumps(
                {
                    "work-group-id": non_release_id,
                    "kind": non_release_group["kind"],
                    "runner-family": non_release_group["runner-family"],
                    "outcome": "success",
                    "commands": [
                        {"capability": capability, "outcome": "success"}
                        for capability in non_release_capabilities
                    ],
                }
            ),
            encoding="utf-8",
        )
        assert (
            control._cmd_write_ci_validation_receipt(
                argparse.Namespace(
                    plan=str(plan_path),
                    changed_files_snapshot=str(changed_files_path),
                    fact_snapshot=str(fact_snapshot_path),
                    assignments=str(assignments_path),
                    work_group_id=non_release_id,
                    matrix_work_group_json=json.dumps(
                        matrix[non_release_id],
                        separators=(",", ":"),
                    ),
                    workflow="CI Validation",
                    job=matrix[non_release_id]["writer-job"],
                    observed_artifacts_dir=str(observed_root),
                    observed_commit_sha="b" * 40,
                    validation_result=str(non_release_validation_result),
                    validation_outcome="success",
                    created_at="2026-05-14T21:09:24Z",
                    receipt_out=str(receipt_path),
                    github_output=str(non_release_outputs),
                )
            )
            == 0
        )
        assert (
            _github_outputs(non_release_outputs)["dependency_blocked"]
            == "false"
        )
        non_release_receipt = json.loads(
            receipt_path.read_text(encoding="utf-8")
        )
        assert non_release_receipt["outcome"] == "success"
        assert non_release_receipt["diagnostics"] == []

        stale_validation_result = scratch / "stale-validation.json"
        stale_validation_result.write_text(
            json.dumps(
                {
                    "work-group-id": "wg-stale",
                    "kind": "descriptor-validation",
                    "runner-family": "windows",
                    "outcome": "success",
                    "commands": [
                        {"capability": capability, "outcome": "success"}
                        for capability in non_release_capabilities
                    ],
                }
            ),
            encoding="utf-8",
        )
        stale_outputs = scratch / "stale-validation-outputs.txt"
        assert (
            control._cmd_write_ci_validation_receipt(
                argparse.Namespace(
                    plan=str(plan_path),
                    changed_files_snapshot=str(changed_files_path),
                    fact_snapshot=str(fact_snapshot_path),
                    assignments=str(assignments_path),
                    work_group_id=non_release_id,
                    matrix_work_group_json=json.dumps(
                        matrix[non_release_id],
                        separators=(",", ":"),
                    ),
                    workflow="CI Validation",
                    job=matrix[non_release_id]["writer-job"],
                    observed_artifacts_dir=str(observed_root),
                    observed_commit_sha="b" * 40,
                    validation_result=str(stale_validation_result),
                    validation_outcome="success",
                    created_at="2026-05-14T21:09:24Z",
                    receipt_out=str(receipt_path),
                    github_output=str(stale_outputs),
                )
            )
            == 0
        )
        stale_receipt = json.loads(receipt_path.read_text(encoding="utf-8"))
        assert stale_receipt["outcome"] == "blocking-failure"
        assert stale_receipt["diagnostics"][0]["code"] == (
            "validation-work-failed"
        )

        scalar_success_outputs = scratch / "scalar-success-outputs.txt"
        assert (
            control._cmd_write_ci_validation_receipt(
                argparse.Namespace(
                    plan=str(plan_path),
                    changed_files_snapshot=str(changed_files_path),
                    fact_snapshot=str(fact_snapshot_path),
                    assignments=str(assignments_path),
                    work_group_id=non_release_id,
                    matrix_work_group_json=json.dumps(
                        matrix[non_release_id],
                        separators=(",", ":"),
                    ),
                    workflow="CI Validation",
                    job=matrix[non_release_id]["writer-job"],
                    observed_artifacts_dir=str(observed_root),
                    observed_commit_sha="b" * 40,
                    validation_outcome="success",
                    created_at="2026-05-14T21:09:24Z",
                    receipt_out=str(receipt_path),
                    github_output=str(scalar_success_outputs),
                )
            )
            == 0
        )
        scalar_success_receipt = json.loads(
            receipt_path.read_text(encoding="utf-8")
        )
        assert scalar_success_receipt["outcome"] == "blocking-failure"
        assert scalar_success_receipt["diagnostics"][0]["code"] == (
            "validation-work-failed"
        )

        malformed_validation_result = scratch / "malformed-validation.json"
        malformed_validation_result.write_text("{", encoding="utf-8")
        malformed_outputs = scratch / "malformed-validation-outputs.txt"
        assert (
            control._cmd_write_ci_validation_receipt(
                argparse.Namespace(
                    plan=str(plan_path),
                    changed_files_snapshot=str(changed_files_path),
                    fact_snapshot=str(fact_snapshot_path),
                    assignments=str(assignments_path),
                    work_group_id=non_release_id,
                    matrix_work_group_json=json.dumps(
                        matrix[non_release_id],
                        separators=(",", ":"),
                    ),
                    workflow="CI Validation",
                    job=matrix[non_release_id]["writer-job"],
                    observed_artifacts_dir=str(observed_root),
                    observed_commit_sha="b" * 40,
                    validation_result=str(malformed_validation_result),
                    validation_outcome="success",
                    created_at="2026-05-14T21:09:24Z",
                    receipt_out=str(receipt_path),
                    github_output=str(malformed_outputs),
                )
            )
            == 0
        )
        malformed_receipt = json.loads(receipt_path.read_text(encoding="utf-8"))
        assert malformed_receipt["outcome"] == "blocking-failure"
        assert malformed_receipt["diagnostics"][0]["code"] == (
            "validation-work-failed"
        )

        invalid_validation_result = scratch / "invalid-validation.json"
        invalid_validation_result.write_text(
            json.dumps({"outcome": "success", "commands": []}),
            encoding="utf-8",
        )
        invalid_outputs = scratch / "invalid-validation-outputs.txt"
        assert (
            control._cmd_write_ci_validation_receipt(
                argparse.Namespace(
                    plan=str(plan_path),
                    changed_files_snapshot=str(changed_files_path),
                    fact_snapshot=str(fact_snapshot_path),
                    assignments=str(assignments_path),
                    work_group_id=non_release_id,
                    matrix_work_group_json=json.dumps(
                        matrix[non_release_id],
                        separators=(",", ":"),
                    ),
                    workflow="CI Validation",
                    job=matrix[non_release_id]["writer-job"],
                    observed_artifacts_dir=str(observed_root),
                    observed_commit_sha="b" * 40,
                    validation_result=str(invalid_validation_result),
                    validation_outcome="success",
                    created_at="2026-05-14T21:09:24Z",
                    receipt_out=str(receipt_path),
                    github_output=str(invalid_outputs),
                )
            )
            == 0
        )
        invalid_receipt = json.loads(receipt_path.read_text(encoding="utf-8"))
        assert invalid_receipt["outcome"] == "blocking-failure"
        assert invalid_receipt["diagnostics"][0]["code"] == (
            "validation-work-failed"
        )

        staged_dependency_artifacts = [
            observed_root
            / artifact_physical_name(
                str(
                    control._ci_assignment_for_work_group(
                        assignments,
                        dependency,
                    )["receipt-artifact-ref"]
                )
            )
            for dependency in dependencies
        ]
        missing_prerequisite = next(
            path for path in staged_dependency_artifacts if path.is_dir()
        )
        shutil.rmtree(missing_prerequisite)
        dependency_gate_outputs = scratch / "dependency-gate-outputs.txt"
        assert (
            control._cmd_check_ci_validation_dependencies(
                argparse.Namespace(
                    plan=str(plan_path),
                    changed_files_snapshot=str(changed_files_path),
                    fact_snapshot=str(fact_snapshot_path),
                    assignments=str(assignments_path),
                    work_group_id=dependent_id,
                    observed_artifacts_dir=str(observed_root),
                    github_output=str(dependency_gate_outputs),
                )
            )
            == 0
        )
        assert (
            _github_outputs(dependency_gate_outputs)["dependency_blocked"]
            == "true"
        )
        blocked_outputs = scratch / "blocked-outputs.txt"
        assert (
            control._cmd_write_ci_validation_receipt(
                argparse.Namespace(
                    plan=str(plan_path),
                    changed_files_snapshot=str(changed_files_path),
                    fact_snapshot=str(fact_snapshot_path),
                    assignments=str(assignments_path),
                    work_group_id=dependent_id,
                    matrix_work_group_json=json.dumps(
                        matrix[dependent_id],
                        separators=(",", ":"),
                    ),
                    workflow="CI Validation",
                    job=matrix[dependent_id]["writer-job"],
                    observed_artifacts_dir=str(observed_root),
                    observed_commit_sha="b" * 40,
                    created_at="2026-05-14T21:09:25Z",
                    receipt_out=str(receipt_path),
                    github_output=str(blocked_outputs),
                )
            )
            == 0
        )
        assert _github_outputs(blocked_outputs)["dependency_blocked"] == "true"
        blocked_receipt = json.loads(receipt_path.read_text(encoding="utf-8"))
        assert blocked_receipt["outcome"] == "skipped"
        blocked_diagnostic = blocked_receipt["diagnostics"][0]
        assert blocked_diagnostic["code"] == "validation-work-skipped"
        assert blocked_diagnostic["detail"] == "dependency-blocked"
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_validation_placeholder_receipts_feed_aggregation() -> None:  # noqa: PLR0915
    """Observed placeholder receipts are manifested and fail the aggregate."""
    scratch = SCRATCH / "ci-validation-placeholder-receipts"
    if scratch.exists():
        shutil.rmtree(scratch)
    scratch.mkdir(parents=True)
    try:
        request = _ci_validation_push_request(
            [
                (
                    "src/public/lib/three-workflow-release-planner/src/"
                    "three_workflow_release_planner/ci_validation_planner.py"
                )
            ],
        )
        plan_snapshot = plan_ci_validation_from_repo(
            CiValidationPlannerInputs(
                request=request,
                repo_root=REPO_ROOT,
                expected_run_id="25887422010",
                expected_run_attempt="1",
                created_at="2026-05-14T21:09:21Z",
            )
        )
        plan_path = scratch / "validation-plan.json"
        changed_files_path = scratch / "changed-files.json"
        fact_snapshot_path = scratch / "fact-snapshot.json"
        assignments_path = scratch / "selector-assignments.json"
        materialize_outputs_path = scratch / "materialize-outputs.txt"
        receipt_outputs_path = scratch / "receipt-outputs.txt"
        observation_outputs_path = scratch / "observation-outputs.txt"
        receipt_path = scratch / "receipt.json"
        observation_path = scratch / "writer-observation.json"
        metadata_path = scratch / "receipt-artifact-metadata.json"
        manifest_path = scratch / "receipt-manifest.json"
        aggregate_path = scratch / "aggregate.json"
        aggregate_outputs_path = scratch / "aggregate-outputs.txt"
        observed_root = scratch / "observed-artifacts"
        plan_path.write_text(json.dumps(plan_snapshot.plan), encoding="utf-8")
        changed_files_path.write_text(
            json.dumps(plan_snapshot.changed_files_snapshot),
            encoding="utf-8",
        )
        fact_snapshot_path.write_text(
            json.dumps(plan_snapshot.fact_snapshot),
            encoding="utf-8",
        )

        assert (
            control._cmd_materialize_ci_work_groups(
                argparse.Namespace(
                    plan=str(plan_path),
                    changed_files_snapshot=str(changed_files_path),
                    fact_snapshot=str(fact_snapshot_path),
                    workflow="CI Validation",
                    writer_job="validation-work-groups",
                    created_at="2026-05-14T21:09:21Z",
                    assignments_out=str(assignments_path),
                    github_output=str(materialize_outputs_path),
                )
            )
            == 0
        )
        assignments = json.loads(assignments_path.read_text(encoding="utf-8"))
        assert len(assignments["assignments"]) >= 4
        matrix = {
            item["work-group-id"]: item
            for item in json.loads(
                _github_outputs(materialize_outputs_path)["work_group_matrix"]
            )
        }
        placeholder_receipt_assignments = [
            assignment
            for assignment in assignments["assignments"]
            if matrix[assignment["work-group-id"]]["kind"]
            in {"lightweight-preflight", "workflow-release-tooling"}
        ]
        assert len(placeholder_receipt_assignments) >= 2
        assignment = placeholder_receipt_assignments[0]
        tampered_assignment = placeholder_receipt_assignments[1]
        remaining_assignments = [
            item for item in assignments["assignments"] if item != assignment
        ]
        malformed_assignment = remaining_assignments[0]
        missing_receipt_assignment = remaining_assignments[1]
        work_group_id = assignment["work-group-id"]
        matrix_json = json.dumps(matrix[work_group_id], separators=(",", ":"))

        assert (
            control._cmd_write_ci_validation_receipt(
                argparse.Namespace(
                    plan=str(plan_path),
                    changed_files_snapshot=str(changed_files_path),
                    fact_snapshot=str(fact_snapshot_path),
                    assignments=str(assignments_path),
                    work_group_id=work_group_id,
                    matrix_work_group_json=matrix_json,
                    workflow="CI Validation",
                    job=matrix[work_group_id]["writer-job"],
                    observed_commit_sha="b" * 40,
                    created_at="2026-05-14T21:09:21Z",
                    receipt_out=str(receipt_path),
                    github_output=str(receipt_outputs_path),
                )
            )
            == 0
        )
        assert (
            control._cmd_write_ci_validation_writer_observation(
                argparse.Namespace(
                    plan=str(plan_path),
                    changed_files_snapshot=str(changed_files_path),
                    fact_snapshot=str(fact_snapshot_path),
                    assignments=str(assignments_path),
                    work_group_id=work_group_id,
                    matrix_work_group_json=matrix_json,
                    workflow="CI Validation",
                    job=matrix[work_group_id]["writer-job"],
                    artifact_instance_id="987654321",
                    created_at="2026-05-14T21:09:22Z",
                    observation_out=str(observation_path),
                    metadata_out=str(metadata_path),
                    github_output=str(observation_outputs_path),
                )
            )
            == 0
        )
        receipt_dir = observed_root / artifact_physical_name(
            assignment["receipt-artifact-ref"]
        )
        observation_dir = observed_root / artifact_physical_name(
            assignment["writer-observation-ref"]
        )
        receipt_dir.mkdir(parents=True)
        observation_dir.mkdir(parents=True)
        shutil.copyfile(receipt_path, receipt_dir / "receipt.json")
        shutil.copyfile(
            observation_path,
            observation_dir / "writer-observation.json",
        )
        shutil.copyfile(
            metadata_path,
            observation_dir / "receipt-artifact-metadata.json",
        )
        raw_receipt = json.dumps(
            json.loads(receipt_path.read_text(encoding="utf-8")),
            indent=4,
        ).encode()
        (receipt_dir / "receipt.json").write_bytes(raw_receipt)

        malformed_work_group_id = malformed_assignment["work-group-id"]
        malformed_observation_path = scratch / "malformed-observation.json"
        malformed_metadata_path = scratch / "malformed-receipt-metadata.json"
        assert (
            control._cmd_write_ci_validation_writer_observation(
                argparse.Namespace(
                    plan=str(plan_path),
                    changed_files_snapshot=str(changed_files_path),
                    fact_snapshot=str(fact_snapshot_path),
                    assignments=str(assignments_path),
                    work_group_id=malformed_work_group_id,
                    matrix_work_group_json=json.dumps(
                        matrix[malformed_work_group_id],
                        separators=(",", ":"),
                    ),
                    workflow="CI Validation",
                    job=matrix[malformed_work_group_id]["writer-job"],
                    artifact_instance_id="malformed-receipt-artifact",
                    created_at="2026-05-14T21:09:22Z",
                    observation_out=str(malformed_observation_path),
                    metadata_out=str(malformed_metadata_path),
                    github_output=None,
                )
            )
            == 0
        )
        malformed_receipt_dir = observed_root / artifact_physical_name(
            malformed_assignment["receipt-artifact-ref"]
        )
        malformed_observation_dir = observed_root / artifact_physical_name(
            malformed_assignment["writer-observation-ref"]
        )
        malformed_receipt_dir.mkdir(parents=True)
        malformed_observation_dir.mkdir(parents=True)
        malformed_bytes = b"{ malformed receipt json"
        (malformed_receipt_dir / "receipt.json").write_bytes(malformed_bytes)
        shutil.copyfile(
            malformed_observation_path,
            malformed_observation_dir / "writer-observation.json",
        )
        shutil.copyfile(
            malformed_metadata_path,
            malformed_observation_dir / "receipt-artifact-metadata.json",
        )

        tampered_work_group_id = tampered_assignment["work-group-id"]
        tampered_receipt_path = scratch / "tampered-receipt.json"
        tampered_observation_path = scratch / "tampered-observation.json"
        tampered_metadata_path = scratch / "tampered-receipt-metadata.json"
        assert (
            control._cmd_write_ci_validation_receipt(
                argparse.Namespace(
                    plan=str(plan_path),
                    changed_files_snapshot=str(changed_files_path),
                    fact_snapshot=str(fact_snapshot_path),
                    assignments=str(assignments_path),
                    work_group_id=tampered_work_group_id,
                    matrix_work_group_json=json.dumps(
                        matrix[tampered_work_group_id],
                        separators=(",", ":"),
                    ),
                    workflow="CI Validation",
                    job=matrix[tampered_work_group_id]["writer-job"],
                    observed_commit_sha="b" * 40,
                    created_at="2026-05-14T21:09:21Z",
                    receipt_out=str(tampered_receipt_path),
                    github_output=None,
                )
            )
            == 0
        )
        assert (
            control._cmd_write_ci_validation_writer_observation(
                argparse.Namespace(
                    plan=str(plan_path),
                    changed_files_snapshot=str(changed_files_path),
                    fact_snapshot=str(fact_snapshot_path),
                    assignments=str(assignments_path),
                    work_group_id=tampered_work_group_id,
                    matrix_work_group_json=json.dumps(
                        matrix[tampered_work_group_id],
                        separators=(",", ":"),
                    ),
                    workflow="CI Validation",
                    job=matrix[tampered_work_group_id]["writer-job"],
                    artifact_instance_id="claimed-receipt-artifact",
                    created_at="2026-05-14T21:09:22Z",
                    observation_out=str(tampered_observation_path),
                    metadata_out=str(tampered_metadata_path),
                    github_output=None,
                )
            )
            == 0
        )
        tampered_metadata = json.loads(
            tampered_metadata_path.read_text(encoding="utf-8")
        )
        tampered_metadata["artifact-instance-id"] = (
            "independent-receipt-artifact"
        )
        tampered_metadata_path.write_text(
            json.dumps(tampered_metadata),
            encoding="utf-8",
        )
        tampered_receipt_dir = observed_root / artifact_physical_name(
            tampered_assignment["receipt-artifact-ref"]
        )
        tampered_observation_dir = observed_root / artifact_physical_name(
            tampered_assignment["writer-observation-ref"]
        )
        tampered_receipt_dir.mkdir(parents=True)
        tampered_observation_dir.mkdir(parents=True)
        shutil.copyfile(
            tampered_receipt_path, tampered_receipt_dir / "receipt.json"
        )
        shutil.copyfile(
            tampered_observation_path,
            tampered_observation_dir / "writer-observation.json",
        )
        shutil.copyfile(
            tampered_metadata_path,
            tampered_observation_dir / "receipt-artifact-metadata.json",
        )

        missing_observation_path = scratch / "missing-receipt-observation.json"
        missing_metadata_path = scratch / "missing-receipt-metadata.json"
        missing_work_group_id = missing_receipt_assignment["work-group-id"]
        assert (
            control._cmd_write_ci_validation_writer_observation(
                argparse.Namespace(
                    plan=str(plan_path),
                    changed_files_snapshot=str(changed_files_path),
                    fact_snapshot=str(fact_snapshot_path),
                    assignments=str(assignments_path),
                    work_group_id=missing_work_group_id,
                    matrix_work_group_json=json.dumps(
                        matrix[missing_work_group_id],
                        separators=(",", ":"),
                    ),
                    workflow="CI Validation",
                    job=matrix[missing_work_group_id]["writer-job"],
                    artifact_instance_id="missing-receipt-artifact",
                    created_at="2026-05-14T21:09:22Z",
                    observation_out=str(missing_observation_path),
                    metadata_out=str(missing_metadata_path),
                    github_output=None,
                )
            )
            == 0
        )
        missing_receipt_dir = observed_root / artifact_physical_name(
            missing_receipt_assignment["receipt-artifact-ref"]
        )
        missing_observation_dir = observed_root / artifact_physical_name(
            missing_receipt_assignment["writer-observation-ref"]
        )
        missing_receipt_dir.mkdir(parents=True)
        missing_observation_dir.mkdir(parents=True)
        shutil.copyfile(
            missing_observation_path,
            missing_observation_dir / "writer-observation.json",
        )
        shutil.copyfile(
            missing_metadata_path,
            missing_observation_dir / "receipt-artifact-metadata.json",
        )

        unexpected_name = "three-ci-validation-" + ("f" * 64)
        unexpected_dir = observed_root / unexpected_name
        unexpected_dir.mkdir(parents=True)
        unexpected_bytes = b"{"
        (unexpected_dir / "receipt.json").write_bytes(unexpected_bytes)
        unassigned_ref = (
            "ci-validation/receipts/25887422010/1/"
            "unassigned-readable/receipt.json"
        )
        unassigned_name = artifact_physical_name(unassigned_ref)
        unassigned_dir = observed_root / unassigned_name
        unassigned_dir.mkdir(parents=True)
        unassigned_receipt = json.loads(
            receipt_path.read_text(encoding="utf-8")
        )
        unassigned_receipt.update(
            {
                "artifact-ref": unassigned_ref,
                "work-group-id": "unassigned-readable",
                "assignment-id": "unassigned-readable",
                "receipt-id": "unassigned-readable-receipt",
            }
        )
        unassigned_bytes = json.dumps(
            unassigned_receipt,
            sort_keys=True,
        ).encode()
        (unassigned_dir / "receipt.json").write_bytes(unassigned_bytes)
        cross_attempt_ref = (
            "ci-validation/receipts/25887422010/2/"
            "unassigned-cross-attempt/receipt.json"
        )
        cross_attempt_name = artifact_physical_name(cross_attempt_ref)
        cross_attempt_dir = observed_root / cross_attempt_name
        cross_attempt_dir.mkdir(parents=True)
        cross_attempt_receipt = dict(unassigned_receipt)
        cross_attempt_receipt.update(
            {
                "artifact-ref": cross_attempt_ref,
                "work-group-id": "unassigned-cross-attempt",
                "assignment-id": "unassigned-cross-attempt",
                "receipt-id": "unassigned-cross-attempt-receipt",
            }
        )
        cross_attempt_bytes = json.dumps(
            cross_attempt_receipt,
            sort_keys=True,
        ).encode()
        (cross_attempt_dir / "receipt.json").write_bytes(cross_attempt_bytes)
        bad_work_group_ref = (
            "ci-validation/receipts/25887422010/1/BAD/receipt.json"
        )
        bad_work_group_name = artifact_physical_name(bad_work_group_ref)
        bad_work_group_dir = observed_root / bad_work_group_name
        bad_work_group_dir.mkdir(parents=True)
        bad_work_group_receipt = dict(unassigned_receipt)
        bad_work_group_receipt.update(
            {
                "artifact-ref": bad_work_group_ref,
                "work-group-id": "BAD",
                "assignment-id": "BAD",
                "receipt-id": "bad-work-group-receipt",
            }
        )
        bad_work_group_bytes = json.dumps(
            bad_work_group_receipt,
            sort_keys=True,
        ).encode()
        (bad_work_group_dir / "receipt.json").write_bytes(bad_work_group_bytes)

        result = control._cmd_aggregate_ci_evidence(
            argparse.Namespace(
                repository="hcoona/three",
                workflow="CI Validation",
                run_id="25887422010",
                run_attempt="1",
                plan=str(plan_path),
                changed_files_snapshot=str(changed_files_path),
                fact_snapshot=str(fact_snapshot_path),
                assignments=str(assignments_path),
                observed_artifacts_dir=str(observed_root),
                created_at="2026-05-14T21:09:23Z",
                receipt_manifest_out=str(manifest_path),
                aggregate_out=str(aggregate_path),
                github_output=str(aggregate_outputs_path),
            )
        )

        assert result == 1
        aggregate = json.loads(aggregate_path.read_text(encoding="utf-8"))
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        entries_by_physical_name = {
            entry["physical-artifact-name"]: entry
            for entry in manifest["entries"]
        }
        assert len(manifest["entries"]) == 8
        assert aggregate["reason"]["invalid-plan"] is False
        assert (
            entries_by_physical_name[receipt_dir.name]["receipt-content-digest"]
            == hashlib.sha256(raw_receipt).hexdigest()
        )
        assert (
            entries_by_physical_name[malformed_receipt_dir.name][
                "receipt-content-digest"
            ]
            == hashlib.sha256(malformed_bytes).hexdigest()
        )
        assert (
            entries_by_physical_name[missing_receipt_dir.name][
                "receipt-content-digest"
            ]
            is None
        )
        assert entries_by_physical_name[unexpected_name]["artifact-ref"] is None
        assert (
            entries_by_physical_name[unexpected_name]["receipt-content-digest"]
            == hashlib.sha256(unexpected_bytes).hexdigest()
        )
        unassigned_entry = entries_by_physical_name[unassigned_name]
        assert unassigned_entry["artifact-ref"] == unassigned_ref
        assert unassigned_entry["receipt-id"] == "unassigned-readable-receipt"
        assert (
            unassigned_entry["receipt-content-digest"]
            == hashlib.sha256(unassigned_bytes).hexdigest()
        )
        assert unassigned_entry["assignment-id"] is None
        assert unassigned_entry["writer-work-group-id"] is None
        assert unassigned_entry["trusted-writer-id"] is None
        assert unassigned_entry["observed-writer-id"] is None
        assert unassigned_entry["writer-observation-ref"] is None
        cross_attempt_entry = entries_by_physical_name[cross_attempt_name]
        assert cross_attempt_entry["artifact-ref"] is None
        assert cross_attempt_entry["receipt-id"] is None
        assert (
            cross_attempt_entry["receipt-content-digest"]
            == hashlib.sha256(cross_attempt_bytes).hexdigest()
        )
        assert cross_attempt_entry["assignment-id"] is None
        assert cross_attempt_entry["writer-work-group-id"] is None
        assert cross_attempt_entry["trusted-writer-id"] is None
        assert cross_attempt_entry["observed-writer-id"] is None
        assert cross_attempt_entry["writer-observation-ref"] is None
        bad_work_group_entry = entries_by_physical_name[bad_work_group_name]
        assert bad_work_group_entry["artifact-ref"] is None
        assert bad_work_group_entry["receipt-id"] is None
        assert (
            bad_work_group_entry["receipt-content-digest"]
            == hashlib.sha256(bad_work_group_bytes).hexdigest()
        )
        assert bad_work_group_entry["assignment-id"] is None
        assert bad_work_group_entry["writer-work-group-id"] is None
        assert bad_work_group_entry["trusted-writer-id"] is None
        assert bad_work_group_entry["observed-writer-id"] is None
        assert bad_work_group_entry["writer-observation-ref"] is None
        observed_by_physical_name = {
            receipt["physical-artifact-name"]: receipt
            for receipt in aggregate["observed-receipts"]
        }
        assert (
            observed_by_physical_name[receipt_dir.name]["admissibility"]
            == "valid"
        )
        assert (
            observed_by_physical_name[malformed_receipt_dir.name][
                "admissibility"
            ]
            == "inadmissible"
        )
        assert (
            observed_by_physical_name[tampered_receipt_dir.name][
                "admissibility"
            ]
            == "inadmissible"
        )
        assert (
            observed_by_physical_name[missing_receipt_dir.name]["admissibility"]
            == "inadmissible"
        )
        assert (
            observed_by_physical_name[unexpected_name]["admissibility"]
            == "inadmissible"
        )
        assert (
            observed_by_physical_name[unassigned_name]["admissibility"]
            == "inadmissible"
        )
        assert (
            observed_by_physical_name[cross_attempt_name]["admissibility"]
            == "inadmissible"
        )
        assert (
            observed_by_physical_name[bad_work_group_name]["admissibility"]
            == "inadmissible"
        )
        assert aggregate["reason"]["required-evidence-skipped"] is False
        assert aggregate["reason"]["inadmissible-receipt"] is True
        assert aggregate["verdict"] == "failed"
        validate_ci_validation_aggregate(
            aggregate,
            plan=plan_snapshot.plan,
            receipt_manifest=manifest,
            selector_assignments_manifest=assignments,
            changed_files_snapshot=plan_snapshot.changed_files_snapshot,
            fact_snapshot=plan_snapshot.fact_snapshot,
        )
    finally:
        if scratch.exists():
            shutil.rmtree(scratch)


def test_ci_validation_release_shaped_placeholder_receipt_is_valid() -> None:
    """Release-shaped placeholders are dependency-blocked skips."""
    scratch = SCRATCH / "ci-validation-release-shaped-placeholder"
    if scratch.exists():
        shutil.rmtree(scratch)
    scratch.mkdir(parents=True)
    try:
        request = _ci_validation_push_request(
            [
                (
                    "src/public/lib/three-workflow-release-planner/src/"
                    "three_workflow_release_planner/ci_validation_planner.py"
                )
            ],
        )
        plan_snapshot = plan_ci_validation_from_repo(
            CiValidationPlannerInputs(
                request=request,
                repo_root=REPO_ROOT,
                expected_run_id="25887422010",
                expected_run_attempt="1",
                created_at="2026-05-14T21:09:21Z",
            )
        )
        plan_path = scratch / "validation-plan.json"
        changed_files_path = scratch / "changed-files.json"
        fact_snapshot_path = scratch / "fact-snapshot.json"
        assignments_path = scratch / "selector-assignments.json"
        output_path = scratch / "outputs.txt"
        receipt_path = scratch / "receipt.json"
        plan_path.write_text(json.dumps(plan_snapshot.plan), encoding="utf-8")
        changed_files_path.write_text(
            json.dumps(plan_snapshot.changed_files_snapshot),
            encoding="utf-8",
        )
        fact_snapshot_path.write_text(
            json.dumps(plan_snapshot.fact_snapshot),
            encoding="utf-8",
        )
        assert (
            control._cmd_materialize_ci_work_groups(
                argparse.Namespace(
                    plan=str(plan_path),
                    changed_files_snapshot=str(changed_files_path),
                    fact_snapshot=str(fact_snapshot_path),
                    workflow="CI Validation",
                    writer_job="validation-work-groups",
                    created_at="2026-05-14T21:09:21Z",
                    assignments_out=str(assignments_path),
                    github_output=str(output_path),
                )
            )
            == 0
        )
        assignments = json.loads(assignments_path.read_text(encoding="utf-8"))
        matrix = {
            item["work-group-id"]: item
            for item in json.loads(
                _github_outputs(output_path)["work_group_matrix"]
            )
        }
        release_assignment = next(
            assignment
            for assignment in assignments["assignments"]
            if matrix[assignment["work-group-id"]]["kind"]
            == "release-shaped-artifact"
        )
        release_work_group_id = release_assignment["work-group-id"]
        work_groups = cast(
            "Sequence[Mapping[str, object]]",
            plan_snapshot.plan["work-groups"],
        )
        release_work_group = next(
            group
            for group in work_groups
            if group["work-group-id"] == release_work_group_id
        )
        assert release_work_group["depends-on"] != []

        assert (
            control._cmd_write_ci_validation_receipt(
                argparse.Namespace(
                    plan=str(plan_path),
                    changed_files_snapshot=str(changed_files_path),
                    fact_snapshot=str(fact_snapshot_path),
                    assignments=str(assignments_path),
                    work_group_id=release_work_group_id,
                    matrix_work_group_json=json.dumps(
                        matrix[release_work_group_id],
                        separators=(",", ":"),
                    ),
                    workflow="CI Validation",
                    job=matrix[release_work_group_id]["writer-job"],
                    observed_commit_sha="b" * 40,
                    created_at="2026-05-14T21:09:22Z",
                    receipt_out=str(receipt_path),
                    github_output=None,
                )
            )
            == 0
        )
        receipt = json.loads(receipt_path.read_text(encoding="utf-8"))
        validate_ci_validation_receipt(
            receipt,
            plan=plan_snapshot.plan,
            selector_assignments_manifest=assignments,
            assignment=release_assignment,
            changed_files_snapshot=plan_snapshot.changed_files_snapshot,
            fact_snapshot=plan_snapshot.fact_snapshot,
        )
        assert receipt["outcome"] == "skipped"
        assert receipt["diagnostics"][0]["code"] == "validation-work-skipped"
        assert receipt["diagnostics"][0]["detail"] == "dependency-blocked"
        result = receipt["evidence"]["category-result"]["detail"][
            "artifact-obligation-results"
        ][0]
        assert result["outcome"] == "skipped"
        assert result["descriptor"]["identity"] is not None
    finally:
        if scratch.exists():
            shutil.rmtree(scratch)


def test_ci_validation_aggregate_writes_invalid_plan_for_malformed_plan() -> (
    None
):
    """Malformed plans still produce an invalid-plan aggregate artifact."""
    scratch = SCRATCH / "ci-validation-malformed-plan"
    shutil.rmtree(scratch, ignore_errors=True)
    scratch.mkdir(parents=True)
    try:
        plan_path = scratch / "validation-plan.json"
        manifest_path = scratch / "receipt-manifest.json"
        aggregate_path = scratch / "aggregate.json"
        plan_path.write_text("{", encoding="utf-8")

        result = control._cmd_aggregate_ci_evidence(
            argparse.Namespace(
                repository="hcoona/three",
                workflow="CI Validation",
                run_id="25887422010",
                run_attempt="1",
                plan=str(plan_path),
                changed_files_snapshot="",
                fact_snapshot="",
                assignments="",
                created_at="2026-05-14T21:09:21Z",
                receipt_manifest_out=str(manifest_path),
                aggregate_out=str(aggregate_path),
                github_output=None,
            )
        )

        assert result == 1
        aggregate = json.loads(aggregate_path.read_text(encoding="utf-8"))
        validate_ci_validation_aggregate(aggregate)
        assert aggregate["reason"]["invalid-plan"] is True
        assert aggregate["verdict"] == "failed"
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_validation_aggregate_writes_invalid_plan_for_missing_plan() -> None:
    """Missing plans produce a schema-valid invalid-plan aggregate artifact."""
    scratch = SCRATCH / "ci-validation-missing-plan"
    shutil.rmtree(scratch, ignore_errors=True)
    scratch.mkdir(parents=True)
    try:
        manifest_path = scratch / "receipt-manifest.json"
        aggregate_path = scratch / "aggregate.json"

        result = control._cmd_aggregate_ci_evidence(
            argparse.Namespace(
                repository="hcoona/three",
                workflow="CI Validation",
                run_id="25887422010",
                run_attempt="1",
                plan=str(scratch / "missing-validation-plan.json"),
                changed_files_snapshot="",
                fact_snapshot="",
                assignments="",
                created_at="2026-05-14T21:09:21Z",
                receipt_manifest_out=str(manifest_path),
                aggregate_out=str(aggregate_path),
                github_output=None,
            )
        )

        assert result == 1
        aggregate = json.loads(aggregate_path.read_text(encoding="utf-8"))
        validate_ci_validation_aggregate(aggregate)
        assert aggregate["reason"]["invalid-plan"] is True
        assert aggregate["diagnostics"][0]["detail"] == "plan-missing"
        assert "no-authoritative-plan" not in aggregate
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_validation_aggregate_writes_invalid_plan_for_bad_assignments() -> (
    None
):
    """Invalid selector assignments produce a failed invalid-plan aggregate."""
    scratch = SCRATCH / "ci-validation-invalid-assignments"
    shutil.rmtree(scratch, ignore_errors=True)
    scratch.mkdir(parents=True)
    try:
        plan_snapshot = plan_ci_validation_from_repo(
            CiValidationPlannerInputs(
                request=_ci_validation_push_request([]),
                repo_root=REPO_ROOT,
                expected_run_id="25887422010",
                expected_run_attempt="1",
                created_at="2026-05-14T21:09:21Z",
            )
        )
        plan_path = scratch / "validation-plan.json"
        changed_files_path = scratch / "changed-files.json"
        fact_snapshot_path = scratch / "fact-snapshot.json"
        assignments_path = scratch / "selector-assignments.json"
        manifest_path = scratch / "receipt-manifest.json"
        aggregate_path = scratch / "aggregate.json"
        plan_path.write_text(json.dumps(plan_snapshot.plan), encoding="utf-8")
        changed_files_path.write_text(
            json.dumps(plan_snapshot.changed_files_snapshot),
            encoding="utf-8",
        )
        fact_snapshot_path.write_text(
            json.dumps(plan_snapshot.fact_snapshot),
            encoding="utf-8",
        )
        assignments_path.write_text("{}", encoding="utf-8")

        result = control._cmd_aggregate_ci_evidence(
            argparse.Namespace(
                repository="hcoona/three",
                workflow="CI Validation",
                run_id="25887422010",
                run_attempt="1",
                plan=str(plan_path),
                changed_files_snapshot=str(changed_files_path),
                fact_snapshot=str(fact_snapshot_path),
                assignments=str(assignments_path),
                created_at="2026-05-14T21:09:21Z",
                receipt_manifest_out=str(manifest_path),
                aggregate_out=str(aggregate_path),
                github_output=None,
            )
        )

        assert result == 1
        aggregate = json.loads(aggregate_path.read_text(encoding="utf-8"))
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        changed_files = json.loads(
            changed_files_path.read_text(encoding="utf-8"),
        )
        fact_snapshot = json.loads(
            fact_snapshot_path.read_text(encoding="utf-8"),
        )
        validate_ci_validation_aggregate(
            aggregate,
            plan=plan_snapshot.plan,
            receipt_manifest=manifest,
            changed_files_snapshot=changed_files,
            fact_snapshot=fact_snapshot,
        )
        assert aggregate["reason"]["invalid-plan"] is True
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_skip_only_tag_verification_is_read_only_without_environment() -> None:
    """Skip-only tag verification must not request write scope."""
    workflow = _workflow("release-orchestrate.yml")
    verify_block_start = workflow.index("  verify-tag-without-environment:\n")
    next_job = workflow.index("\n  skip-results:\n", verify_block_start)
    verify_block = workflow[verify_block_start:next_job]
    active_block_start = workflow.index("  ensure-tag-without-environment:\n")
    active_block = workflow[active_block_start:verify_block_start]

    assert "has-active-github-release != 'true'" in verify_block
    assert "      contents: read\n" in verify_block
    assert "      contents: write\n" not in verify_block
    assert "has-active-github-release == 'true'" in active_block
    assert "      contents: write\n" in active_block


def test_live_planner_observes_remote_publications_before_final_plan() -> None:
    """Live planning observes remotes before final plan."""
    workflow = _workflow("release-orchestrate.yml")
    run_block = _step_block(workflow, "Run planner")
    gate_block = _step_block(workflow, "Apply live external OIDC gate")

    bootstrap = run_block.index("bootstrap-release-plan.json")
    observe = run_block.index("observe-remote-publications")
    final = run_block.rindex("--remote-observations")
    assert bootstrap < observe < final
    assert "GH_TOKEN: ${{ github.token }}" in run_block
    assert (
        "ENABLED_EXTERNAL_OIDC_TARGETS: "
        "${{ inputs.enabled-external-oidc-targets }}" in run_block
    )
    assert (
        "RELEASE_CANARY_OVERRIDE_NON_PUBLIC_REF: "
        "${{ inputs.canary-override-non-public-ref }}" in run_block
    )
    assert "RELEASE_ENVIRONMENT: ${{ inputs.release-environment }}" in run_block
    plan_job_start = workflow.index("  plan:\n")
    plan_steps_start = workflow.index("    steps:\n", plan_job_start)
    plan_header = workflow[plan_job_start:plan_steps_start]
    assert "      packages: read\n" in plan_header
    assert "if [ \"$RELEASE_DRY_RUN\" != 'true' ]; then" in run_block
    assert "--dry-run" in run_block[:observe]
    assert (
        "--execution-sets "
        ".three-workflow-release/plan/bootstrap-execution-sets.json"
        in run_block
    )
    assert (
        '--enabled-external-oidc-targets "$ENABLED_EXTERNAL_OIDC_TARGETS"'
        in run_block
    )
    assert (
        "--canary-override-non-public-ref "
        '"$RELEASE_CANARY_OVERRIDE_NON_PUBLIC_REF"' in run_block
    )
    assert '--release-environment "$RELEASE_ENVIRONMENT"' in run_block
    assert ".three-workflow-release/plan/remote-observations.json" in run_block
    assert (
        "--remote-observations "
        ".three-workflow-release/plan/remote-observations.json" in gate_block
    )


def test_reusable_publish_jobs_use_topology_scoped_permissions() -> None:
    """Reusable publish permissions stay scoped.

    Reusable workflow callers grant the required superset for GitHub validation.
    """
    publish = yaml.safe_load(_workflow("release-publish-node.yml"))
    orchestrate = yaml.safe_load(_workflow("release-orchestrate.yml"))

    github_release_permissions = {
        "contents": "write",
        "actions": "read",
        "id-token": "write",
        "attestations": "write",
    }
    github_packages_permissions = {
        "contents": "read",
        "packages": "write",
        "actions": "read",
    }
    external_oidc_permissions = {
        "contents": "read",
        "actions": "read",
        "id-token": "write",
    }
    expected_publish_permissions = {
        "publish-github-release-with-environment": github_release_permissions,
        "publish-github-release-without-environment": (
            github_release_permissions
        ),
        "publish-github-packages-with-environment": github_packages_permissions,
        "publish-github-packages-without-environment": (
            github_packages_permissions
        ),
        "publish-external-oidc-with-environment": external_oidc_permissions,
        "publish-external-oidc-without-environment": external_oidc_permissions,
    }

    for job_id, permissions in expected_publish_permissions.items():
        assert publish["jobs"][job_id]["permissions"] == permissions
        granted = set(permissions)
        if "github-packages" in job_id:
            assert "id-token" not in granted
            assert "attestations" not in granted
            assert all(
                step.get("uses") != "actions/attest@v4"
                for step in publish["jobs"][job_id]["steps"]
            )
        if "github-release" in job_id:
            assert "packages" not in granted
            assert any(
                step.get("uses") == "actions/attest@v4"
                for step in publish["jobs"][job_id]["steps"]
            )
        if "external-oidc" in job_id:
            assert "packages" not in granted
            assert "attestations" not in granted
            assert all(
                step.get("uses") != "actions/attest@v4"
                for step in publish["jobs"][job_id]["steps"]
            )

    reusable_caller_permissions = {
        "contents": "write",
        "packages": "write",
        "actions": "read",
        "id-token": "write",
        "attestations": "write",
    }
    expected_orchestrator_permissions = {
        "publish-reusable-github-release": reusable_caller_permissions,
        "publish-reusable-github-packages": reusable_caller_permissions,
        "publish-reusable-external-oidc": reusable_caller_permissions,
    }
    for job_id, permissions in expected_orchestrator_permissions.items():
        job = orchestrate["jobs"][job_id]
        assert job["permissions"] == permissions
        assert job["with"]["permission-class"] in {
            "github-release",
            "github-packages",
            "external-oidc",
        }


def test_write_scoped_checkout_steps_do_not_persist_credentials() -> None:
    """Jobs with write-capable tokens must not leave checkout credentials."""
    publish = yaml.safe_load(_workflow("release-publish-node.yml"))
    orchestrate = yaml.safe_load(_workflow("release-orchestrate.yml"))

    expected_jobs = {
        "release-publish-node.yml": (
            publish,
            (
                "publish-github-release-with-environment",
                "publish-github-release-without-environment",
                "publish-github-packages-with-environment",
                "publish-github-packages-without-environment",
            ),
        ),
        "release-orchestrate.yml": (
            orchestrate,
            (
                "ensure-tag-with-environment",
                "ensure-tag-without-environment",
            ),
        ),
    }

    for workflow_name, (workflow, job_ids) in expected_jobs.items():
        for job_id in job_ids:
            checkout_steps = [
                step
                for step in workflow["jobs"][job_id]["steps"]
                if step.get("uses") == "actions/checkout@v4"
            ]
            assert len(checkout_steps) == 1, (workflow_name, job_id)
            assert checkout_steps[0]["with"]["persist-credentials"] is False, (
                workflow_name,
                job_id,
            )


def test_hidden_release_artifact_uploads_are_included() -> None:
    """upload-artifact must include dot-prefixed release work directories."""
    for workflow_path in _release_workflow_paths():
        workflow = yaml.safe_load(workflow_path.read_text(encoding="utf-8"))
        for job_id, job in workflow["jobs"].items():
            for step_index, step in enumerate(job.get("steps", ())):
                if step.get("uses") != "actions/upload-artifact@v4":
                    continue
                with_inputs = step.get("with", {})
                path = str(with_inputs.get("path", ""))
                if path.startswith(".three-workflow-release/"):
                    assert with_inputs.get("include-hidden-files") is True, (
                        workflow_path.name,
                        job_id,
                        step_index,
                        step.get("name"),
                    )


def test_buddy_entry_external_oidc_publish_permissions_are_minimal() -> None:
    """Entry-hosted OIDC publish should not grant unrelated writes."""
    for workflow_name in ("release-official.yml", "release-buddy.yml"):
        workflow = yaml.safe_load(_workflow(workflow_name))

        assert workflow["jobs"]["publish-entry"]["permissions"] == {
            "contents": "read",
            "actions": "read",
            "id-token": "write",
        }


def test_official_entry_publish_sets_up_npm_trusted_runtime() -> None:
    """Official entry-hosted npm publish must not rely on runner defaults."""
    workflow = yaml.safe_load(_workflow("release-official.yml"))
    steps = workflow["jobs"]["publish-entry"]["steps"]

    setup_index, setup_step = next(
        (index, step)
        for index, step in enumerate(steps)
        if step.get("uses") == "actions/setup-node@v4"
    )
    guard_index, guard_step = next(
        (index, step)
        for index, step in enumerate(steps)
        if step.get("name") == "Ensure npm trusted publishing support"
    )
    publish_index = next(
        index
        for index, step in enumerate(steps)
        if "uv run three-workflow-release-publish publish"
        in str(step.get("run", ""))
    )

    assert setup_index < guard_index < publish_index
    assert setup_step["with"]["node-version"] == "24"
    guard_run = guard_step["run"]
    assert "npm install --global npm@^11.5.1" in guard_run
    assert "const required = [11, 5, 1];" in guard_run
    assert "process.exit(1);" in guard_run


def test_entry_publish_sets_up_nuget_trusted_publishing() -> None:
    """Entry-hosted NuGet.org publish must use NuGet trusted publishing."""
    for workflow_name in ("release-official.yml", "release-buddy.yml"):
        workflow = yaml.safe_load(_workflow(workflow_name))
        steps = workflow["jobs"]["publish-entry"]["steps"]

        detect_index, detect_step = next(
            (index, step)
            for index, step in enumerate(steps)
            if step.get("name") == "Detect NuGet.org trusted publishing"
        )
        login_index, login_step = next(
            (index, step)
            for index, step in enumerate(steps)
            if step.get("uses") == "NuGet/login@v1"
        )
        publish_index, publish_step = next(
            (index, step)
            for index, step in enumerate(steps)
            if "uv run three-workflow-release-publish publish"
            in str(step.get("run", ""))
        )

        assert detect_index < login_index < publish_index
        assert "nuget.org" in detect_step["run"]
        assert login_step["if"] == (
            "${{ steps.nuget_trusted_publishing.outputs.required == 'true' }}"
        )
        assert login_step["with"]["user"] == "${{ secrets.NUGET_USER }}"
        assert publish_step["env"]["NUGET_API_KEY"] == (
            "${{ steps.nuget_login.outputs.NUGET_API_KEY }}"
        )


def test_entry_publish_gate_ignores_reusable_publish_result() -> None:
    """Entry-hosted publish can proceed after reusable publish fails."""
    for workflow_name in ("release-official.yml", "release-buddy.yml"):
        workflow = yaml.safe_load(_workflow(workflow_name))
        gate = workflow["jobs"]["publish-entry"]["if"]

        assert "needs.orchestrate.result == 'success'" not in gate
        assert "needs.orchestrate.outputs.entry-publish-node-ids != ''" in gate
        assert (
            "needs.orchestrate.outputs.entry-publish-node-ids != '[]'" in gate
        )
        assert "needs.orchestrate.outputs.plan-artifact-name != ''" in gate
        assert (
            "needs.orchestrate.outputs."
            "entry-publish-handoff-artifact-name != ''" in gate
        )
        assert (
            "needs.orchestrate.outputs.validate-conclusion == 'success'" in gate
        )
        assert "needs.orchestrate.outputs.plan-conclusion == 'success'" in gate
        assert "needs.orchestrate.outputs.build-conclusion == 'success'" in gate
        assert "needs.orchestrate.outputs.build-conclusion == 'skipped'" in gate
        assert "needs.orchestrate.outputs.tag-conclusion == 'success'" in gate
        assert "needs.orchestrate.outputs.tag-conclusion == 'skipped'" in gate


def test_reusable_build_caller_grants_artifact_download_permission() -> None:
    """Build reusable workflow needs caller-granted artifact read permission."""
    workflow = yaml.safe_load(_workflow("release-orchestrate.yml"))

    assert workflow["jobs"]["build"]["permissions"] == {
        "contents": "read",
        "actions": "read",
    }


def test_orchestrator_exposes_reusable_publish_conclusion() -> None:
    """Reusable publish result is summarized for entry workflow reports."""
    workflow_text = _workflow("release-orchestrate.yml")
    workflow = yaml.safe_load(workflow_text)
    job = workflow["jobs"]["publish-reusable-conclusion"]

    assert (
        "publish-conclusion:\n"
        "        value: ${{ "
        "jobs.publish-reusable-conclusion.outputs.publish-conclusion }}"
        in workflow_text
    )
    assert job["needs"] == [
        "publish-reusable-github-release",
        "publish-reusable-github-packages",
        "publish-reusable-external-oidc",
    ]
    assert job["permissions"] == {}
    combine_step = job["steps"][0]
    assert combine_step["env"]["GITHUB_RELEASE_RESULT"] == (
        "${{ needs.publish-reusable-github-release.result }}"
    )
    assert combine_step["env"]["GITHUB_PACKAGES_RESULT"] == (
        "${{ needs.publish-reusable-github-packages.result }}"
    )
    assert combine_step["env"]["EXTERNAL_OIDC_RESULT"] == (
        "${{ needs.publish-reusable-external-oidc.result }}"
    )
    assert "publish_conclusion=success" in combine_step["run"]
    assert "publish_conclusion=failure" in combine_step["run"]
    assert "publish_conclusion=cancelled" in combine_step["run"]


def test_orchestrator_exposes_internal_stage_conclusions() -> None:
    """Entry reports need actual internal orchestration stage outcomes."""
    workflow_text = _workflow("release-orchestrate.yml")
    workflow = yaml.safe_load(workflow_text)
    job = workflow["jobs"]["stage-conclusions"]

    for output_name in (
        "validate-conclusion",
        "metadata-conclusion",
        "plan-conclusion",
        "build-conclusion",
        "tag-conclusion",
    ):
        assert f"      {output_name}:\n" in workflow_text
        assert f"jobs.stage-conclusions.outputs.{output_name}" in workflow_text

    assert job["needs"] == [
        "validate-authoring",
        "dotnet-metadata",
        "plan",
        "build",
        "ensure-tag-with-environment",
        "ensure-tag-without-environment",
        "verify-tag-without-environment",
    ]
    assert job["permissions"] == {}
    step = job["steps"][0]
    assert step["env"]["VALIDATE_RESULT"] == (
        "${{ needs.validate-authoring.result }}"
    )
    assert step["env"]["METADATA_RESULT"] == (
        "${{ needs.dotnet-metadata.result }}"
    )
    assert step["env"]["BUILD_RESULT"] == "${{ needs.build.result }}"
    assert "result=skipped" in step["run"]
    assert "result=failure" in step["run"]
    assert "result=cancelled" in step["run"]
    assert "result=success" in step["run"]


def test_entry_reports_use_orchestrator_stage_conclusion_outputs() -> None:
    """Final reports must not collapse all internal stages to call result."""
    for workflow_name in ("release-official.yml", "release-buddy.yml"):
        workflow = _workflow(workflow_name)
        render_block = _step_block(workflow, "Render report")

        for stage in ("validate", "metadata", "plan", "build", "tag"):
            env_name = f"RELEASE_{stage.upper()}_CONCLUSION"
            assert (
                f"{env_name}: "
                f"${{{{ needs.orchestrate.outputs.{stage}-conclusion }}}}"
                in render_block
            )
            assert f'{stage}_conclusion="${{{env_name}:-skipped}}"' in (
                render_block
            )
            assert f'--{stage}-conclusion "${stage}_conclusion" \\' in (
                render_block
            )
        assert (
            "--validate-conclusion '${{ needs.orchestrate.result }}'"
            not in render_block
        )


def test_entry_reports_combine_reusable_and_entry_publish_conclusions() -> None:
    """Final reports aggregate reusable-hosted and entry-hosted publishes."""
    for workflow_name in ("release-official.yml", "release-buddy.yml"):
        workflow = _workflow(workflow_name)
        render_block = _step_block(workflow, "Render report")

        assert (
            "RELEASE_REUSABLE_PUBLISH_CONCLUSION: "
            "${{ needs.orchestrate.outputs.publish-conclusion }}"
            in render_block
        )
        assert (
            "RELEASE_ENTRY_PUBLISH_CONCLUSION: "
            "${{ needs.publish-entry.result }}" in render_block
        )
        assert (
            'reusable_publish_conclusion="${'
            'RELEASE_REUSABLE_PUBLISH_CONCLUSION:-skipped}"' in render_block
        )
        assert (
            'entry_publish_conclusion="${'
            'RELEASE_ENTRY_PUBLISH_CONCLUSION:-skipped}"' in render_block
        )
        assert (
            'for conclusion in "$reusable_publish_conclusion" '
            '"$entry_publish_conclusion"; do' in render_block
        )
        assert '--publish-conclusion "$publish_conclusion" \\' in render_block
        assert (
            "--publish-conclusion '${{ needs.publish-entry.result }}'"
            not in render_block
        )


def test_entry_workflows_stage_and_upload_deterministic_proofs() -> None:
    """Entry-hosted publish mirrors reusable proof staging and final uploads."""
    for workflow_name in ("release-official.yml", "release-buddy.yml"):
        workflow = _workflow(workflow_name)
        assert "name: Generate proof artifacts" in workflow
        assert "workflow_release_control.py generate-proofs" in workflow
        assert (
            "name: proof-staging-${{ "
            "steps.publish_name.outputs.publish_result_artifact_name }}"
            in workflow
        )
        assert "upload-entry-proofs:" in workflow
        assert (
            "needs.orchestrate.outputs.has-entry-proofs == 'true'" in workflow
        )
        assert (
            "proof: ${{ "
            "fromJson(needs.orchestrate.outputs.entry-proof-matrix) }}"
            in workflow
        )
        assert (
            "needs.publish-entry.result == 'success' || "
            "needs.publish-entry.result == 'failure'" in workflow
        )
        assert "continue-on-error: true" in workflow
        assert "PROOF_FILE: ${{ matrix.proof.file }}" in workflow
        assert "steps.staged.outputs.present == 'true'" in workflow
        assert "name: ${{ matrix.proof.staging-artifact-name }}" in workflow
        assert "name: ${{ matrix.proof.name }}" in workflow


def test_hk_runs_focused_workflow_release_validation() -> None:
    """HK must run focused control-plane tests for release workflow changes."""
    hk_config = (REPO_ROOT / "hk.pkl").read_text(encoding="utf-8")
    pre_commit_config = (REPO_ROOT / ".pre-commit-config.yaml").read_text(
        encoding="utf-8"
    )

    assert "workflow-release-control-tests" in hk_config
    assert ".github/workflows/ci-validate.yml" in hk_config
    assert ".github/workflows/release-*.yml" in hk_config
    assert "eng/release/**" in hk_config
    assert "eng/scripts/workflow_release_control.py" in hk_config
    assert "eng/scripts/workflow_release_acceptance_gate.py" in hk_config
    assert "src/**/three.release.yml" in hk_config
    assert "src/public/lib/three-workflow-release-*/**" in hk_config
    assert "tests/test_workflow_release_control.py" in hk_config
    assert "tests/fixtures/workflow-release-acceptance-matrix.json" in hk_config
    assert (
        "tests/fixtures/workflow-release-ci-validation-acceptance-matrix.json"
        in hk_config
    )
    assert (
        "uv run python eng/scripts/workflow_release_acceptance_gate.py"
        in hk_config
    )
    assert "actionlint" in hk_config
    assert "id: workflow-release-control-tests" in pre_commit_config
    assert r"\.github/workflows/ci-validate\.yml$" in pre_commit_config
