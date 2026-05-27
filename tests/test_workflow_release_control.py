# ruff: noqa: SLF001
"""Tests for workflow-release control-plane helper script."""

from __future__ import annotations

import argparse
import hashlib
import importlib.util
import io
import json
import shlex
import shutil
import subprocess
import sys
import tarfile
from copy import deepcopy
from itertools import pairwise
from pathlib import Path
from typing import TYPE_CHECKING, Any, cast

import pytest
import yaml
from three_workflow_release_authoring import validate_authoring
from three_workflow_release_build import execute_build
from three_workflow_release_contracts import (
    ArtifactNameInputs,
    ContractValidationError,
    GitHubActionsArtifactMetadata,
    artifact_name,
    artifact_physical_name,
    canonical_json_bytes,
    ci_validation_aggregate_evidence_manifest_payload_digest,
    ci_validation_batch_evidence_bundle_payload_digest,
    ci_validation_batch_evidence_candidate_id,
    ci_validation_execution_batch_manifest_payload_digest,
    ci_validation_execution_batch_matrix,
    ci_validation_plan_digest,
    validate_ci_validation_aggregate_evidence_manifest,
    validate_ci_validation_aggregate_summary,
    validate_ci_validation_batch_evidence_bundle,
    validate_contract,
)
from three_workflow_release_planner import (
    PlannerInputs,
    plan_release,
)
from three_workflow_release_proof import (
    ProofError,
    classify_immutable_observations,
)

from tests import ci_validation_batch_fixtures as batch_contracts

if TYPE_CHECKING:
    from collections.abc import Callable, Mapping, Sequence

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
        if not source.exists():
            continue
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


def _ci_range_derivation_python() -> str:
    """Return the embedded CI affected-range derivation Python program."""
    workflow = yaml.safe_load(_workflow("ci-validate.yml"))
    steps = workflow["jobs"]["normalize-input"]["steps"]
    run = next(
        step["run"]
        for step in steps
        if step.get("name") == "Write planner-facing request"
    )
    marker = "uv run python - <<'PY'\n"
    start = run.index(marker) + len(marker)
    end = run.index("\nPY", start)
    return run[start:end]


def _read_ci_range_args(path: Path) -> list[str]:
    """Parse the generated shell range_args assignment."""
    assignment = path.read_text(encoding="utf-8")
    return shlex.split(
        assignment.removeprefix("range_args=(").removesuffix(")\n"),
    )


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
        "test_ci_validation_workflow_exposes_control_plane_boundaries",
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


def _ci_acceptance_evidence_values(
    matrix: dict[str, object],
) -> list[tuple[str, str, str]]:
    evidence_values: list[tuple[str, str, str]] = []
    for row in matrix["rows"]:
        row_id = row["id"]
        assert isinstance(row_id, str)
        evidence = row["evidence"]
        assert isinstance(evidence, dict)
        for column, references in evidence.items():
            assert isinstance(column, str)
            assert isinstance(references, list)
            for reference in references:
                if isinstance(reference, str):
                    evidence_values.append((row_id, column, reference))
                    continue
                assert isinstance(reference, dict)
                assert isinstance(reference.get("type"), str)
                value = reference.get("value")
                assert isinstance(value, str)
                evidence_values.append((row_id, column, value))
    return evidence_values


def _string_values_in_shape(
    scope: str,
    path: str,
    value: object,
) -> list[tuple[str, str, str]]:
    string_values: list[tuple[str, str, str]] = []
    if isinstance(value, str):
        string_values.append((scope, path, value))
    elif isinstance(value, dict):
        for key, child in value.items():
            assert isinstance(key, str)
            string_values.append((scope, path, key))
            string_values.extend(
                _string_values_in_shape(scope, f"{path}.{key}", child)
            )
    elif isinstance(value, list):
        for index, child in enumerate(value):
            string_values.extend(
                _string_values_in_shape(scope, f"{path}[{index}]", child)
            )
    return string_values


def test_fail_closed_acceptance_flows_emit_diagnostics_without_outputs() -> (
    None
):
    """Real invalid commands fail before downstream outputs."""
    scratch = SCRATCH / "fail-closed-flows"
    if scratch.exists():
        shutil.rmtree(scratch)
    scratch.mkdir(parents=True)
    try:
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
        assert "REQ_PROJECT_NOT_FOUND" in _diagnostic_codes(
            unknown_project_diag
        )
        _assert_forbidden_outputs_absent(unknown_project)
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


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
    assert jobs["normalize-input"]["outputs"]["request-artifact-id"] == (
        "${{ steps.upload-request.outputs.artifact-id }}"
    )
    assert set(jobs) >= {
        "normalize-input",
        "plan",
        "materialize-execution-batches",
        "execution-batch-ubuntu-orchestrator",
        "execution-batch-windows-orchestrator",
        "aggregate-evidence",
    }
    assert not any(job.startswith("execution-batch-layer-") for job in jobs)
    assert jobs["plan"]["needs"] == "normalize-input"
    assert set(jobs["materialize-execution-batches"]["needs"]) == {
        "normalize-input",
        "plan",
    }
    assert set(jobs["aggregate-evidence"]["needs"]) >= {
        "execution-batch-ubuntu-orchestrator",
        "execution-batch-windows-orchestrator",
    }
    assert set(jobs["execution-batch-ubuntu-orchestrator"]["needs"]) == {
        "normalize-input",
        "plan",
        "materialize-execution-batches",
    }
    assert set(jobs["execution-batch-windows-orchestrator"]["needs"]) == {
        "normalize-input",
        "plan",
        "materialize-execution-batches",
    }
    assert (
        "ubuntu-execution-batch-matrix"
        in jobs["materialize-execution-batches"]["outputs"]
    )
    assert (
        "windows-execution-batch-matrix"
        in jobs["materialize-execution-batches"]["outputs"]
    )
    assert (
        "execution-batch-manifest-artifact-id"
        in jobs["materialize-execution-batches"]["outputs"]
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


def test_ci_validation_workflow_uses_current_batch_evidence_commands() -> None:
    """CI workflow must not invoke retired receipt or writer-observation."""
    workflow_text = _workflow("ci-validate.yml")
    forbidden_commands = {
        "materialize-ci-work-groups",
        "check-ci-validation-dependencies",
        "validate-ci-validation-lightweight-policy",
        "write-ci-validation-receipt",
        "write-ci-validation-observation",
        "write-ci-validation-writer-observation",
    }
    forbidden_legacy_surfaces = {
        "ci-validation-receipt",
        "receipt-manifest",
        "selector-assignment",
        "writer-observation",
    }
    required_current_commands = {
        "materialize-ci-validation-execution-batches",
        "run-ci-validation-runner-family-orchestrator-step",
        "record-ci-validation-runner-family-orchestrator-upload",
        "download-ci-validation-observed-artifacts",
        "aggregate-ci-evidence",
        "verify-ci-validation-artifact-boundaries",
    }

    for forbidden in forbidden_commands | forbidden_legacy_surfaces:
        assert forbidden not in workflow_text
    for required in required_current_commands:
        assert required in workflow_text


def test_ci_validation_workflow_downloads_only_explicit_artifacts() -> None:
    """Workflow downloads use explicit artifact IDs/API, not broad patterns."""
    workflow = yaml.safe_load(_workflow("ci-validate.yml"))
    download_steps = []

    for job in workflow["jobs"].values():
        for step in job.get("steps", []):
            if step.get("uses") != "actions/download-artifact@v4":
                continue
            download_steps.append(step)
            with_args = step["with"]
            assert "artifact-ids" in with_args
            assert with_args["artifact-ids"]
            assert with_args.get("merge-multiple") is True
            assert "pattern" not in with_args
            assert "name" not in with_args

    assert download_steps
    workflow_text = _workflow("ci-validate.yml")
    assert "pattern: three-ci-validation-*" not in workflow_text
    assert "name: three-ci-validation-*" not in workflow_text


def test_ci_validation_workflow_wires_final_aggregate_boundaries() -> None:
    """Final aggregate artifacts are redownloaded and boundary-verified."""
    workflow = yaml.safe_load(_workflow("ci-validate.yml"))
    aggregate = workflow["jobs"]["aggregate-evidence"]
    steps = aggregate["steps"]

    assert aggregate["needs"] == [
        "normalize-input",
        "plan",
        "materialize-execution-batches",
        "execution-batch-ubuntu-orchestrator",
        "execution-batch-windows-orchestrator",
    ]

    step_by_name = {step.get("name"): step for step in steps}
    manifest_upload = step_by_name["Upload aggregate evidence manifest"]
    manifest_boundary = step_by_name[
        "Verify aggregate evidence manifest producer boundary"
    ]
    summary_write = step_by_name["Write aggregate summary"]
    summary_upload = step_by_name["Upload aggregate summary"]
    manifest_download = step_by_name[
        "Download uploaded aggregate evidence manifest"
    ]
    summary_download = step_by_name["Download uploaded aggregate summary"]
    final_verify = step_by_name[
        "Verify final artifact bytes and producer boundaries"
    ]

    assert manifest_upload["id"] == "upload-aggregate-evidence-manifest"
    assert summary_upload["id"] == "upload-aggregate-summary"
    assert manifest_boundary["id"] == (
        "verify-aggregate-evidence-manifest-producer-boundary"
    )
    assert summary_write["env"]["AGGREGATE_EVIDENCE_MANIFEST_ARTIFACT_ID"] == (
        "${{ steps.upload-aggregate-evidence-manifest.outputs.artifact-id }}"
    )
    assert summary_write["env"][
        "AGGREGATE_EVIDENCE_MANIFEST_PRODUCER_VERIFIED"
    ] == (
        "${{ steps.verify-aggregate-evidence-manifest-producer-boundary."
        "outcome == 'success' }}"
    )
    assert (
        "--aggregate-evidence-manifest-producer-verified"
        in summary_write["run"]
    )

    assert manifest_download["with"]["artifact-ids"] == (
        "${{ steps.upload-aggregate-evidence-manifest.outputs.artifact-id }}"
    )
    assert manifest_download["with"]["merge-multiple"] is True
    assert manifest_download["with"]["path"] == (
        ".three-ci-validation/final-uploaded/aggregate-evidence-manifest"
    )
    assert summary_download["with"]["artifact-ids"] == (
        "${{ steps.upload-aggregate-summary.outputs.artifact-id }}"
    )
    assert summary_download["with"]["merge-multiple"] is True
    assert summary_download["with"]["path"] == (
        ".three-ci-validation/final-uploaded/aggregate-summary"
    )

    final_run = final_verify["run"]
    assert "--max-prefixed-validation-artifacts 20" in final_run
    assert "--expected-prefixed-validation-artifacts" in final_run
    assert (
        "summary['budgets']['expected-actual-validation-artifacts']"
        in final_run
    )
    assert '\\"producer-boundary\\":\\"aggregate-evidence\\"' in final_run
    assert (
        ".three-ci-validation/final-uploaded/aggregate-evidence-manifest/"
        "aggregate-evidence-manifest.json"
    ) in final_run
    assert (
        ".three-ci-validation/final-uploaded/aggregate-summary/"
        "aggregate-summary.json"
    ) in final_run
    assert (
        '\\"content-digest\\":\\"${aggregate_manifest_digest}\\"' in final_run
    )
    assert '\\"content-digest\\":\\"${aggregate_summary_digest}\\"' in final_run


def test_ci_validation_artifact_id_downloads_merge_to_consumer_paths() -> None:
    """Artifact-id downloads preserve flat paths consumed by scripts."""
    workflow = yaml.safe_load(_workflow("ci-validate.yml"))
    optional_snapshot_outputs = {
        "${{ needs.plan.outputs.changed-files-snapshot-artifact-id }}",
        "${{ needs.plan.outputs.fact-snapshot-artifact-id }}",
    }
    execution_manifest_output = (
        "${{ needs.materialize-execution-batches.outputs."
        "execution-batch-manifest-artifact-id }}"
    )

    artifact_id_downloads = []
    optional_snapshot_downloads = []
    execution_manifest_downloads = []
    for job in workflow["jobs"].values():
        for step in job.get("steps", []):
            if step.get("uses") != "actions/download-artifact@v4":
                continue
            with_args = step.get("with", {})
            artifact_ids = with_args.get("artifact-ids")
            if artifact_ids is None:
                continue

            artifact_id_downloads.append(step)
            assert with_args.get("merge-multiple") is True
            if artifact_ids in optional_snapshot_outputs:
                optional_snapshot_downloads.append(step)
                output_expr = artifact_ids[4:-3].strip()
                expected_if = f"${{{{ {output_expr} != '' }}}}"
                assert step.get("if") == expected_if
            if artifact_ids == execution_manifest_output:
                execution_manifest_downloads.append(step)
                assert step.get("if") == (
                    "${{ needs.materialize-execution-batches.outputs."
                    "execution-batch-manifest-artifact-id != '' }}"
                )

    assert artifact_id_downloads
    assert len(optional_snapshot_downloads) == 8
    assert len(execution_manifest_downloads) == 3


def test_ci_validation_aggregate_request_plan_downloads_are_guarded() -> None:
    """Aggregate required downloads avoid download-all when absent."""
    workflow = yaml.safe_load(_workflow("ci-validate.yml"))
    steps = workflow["jobs"]["aggregate-evidence"]["steps"]
    request_output = "${{ needs.normalize-input.outputs.request-artifact-id }}"
    plan_output = "${{ needs.plan.outputs.plan-artifact-id }}"

    prepare_index = next(
        index
        for index, step in enumerate(steps)
        if step.get("name") == "Prepare aggregate input directories"
    )
    request_index = next(
        index
        for index, step in enumerate(steps)
        if step.get("with", {}).get("artifact-ids") == request_output
    )
    plan_index = next(
        index
        for index, step in enumerate(steps)
        if step.get("with", {}).get("artifact-ids") == plan_output
    )
    request_step = steps[request_index]
    plan_step = steps[plan_index]
    download_observed_step = next(
        step
        for step in steps
        if step.get("name") == "Download observed batch evidence"
    )
    aggregate_step = next(
        step
        for step in steps
        if step.get("name") == "Write aggregate evidence manifest"
    )

    assert prepare_index < request_index < plan_index
    assert ".three-ci-validation/request" in steps[prepare_index]["run"]
    assert ".three-ci-validation/plan" in steps[prepare_index]["run"]
    assert request_step["if"] == (
        "${{ needs.normalize-input.outputs.request-artifact-id != '' }}"
    )
    assert request_step["with"]["path"] == ".three-ci-validation/request"
    assert plan_step["if"] == "${{ needs.plan.outputs.plan-artifact-id != '' }}"
    assert plan_step["with"]["path"] == ".three-ci-validation/plan"
    assert "if" not in aggregate_step
    assert aggregate_step["env"]["REQUEST_ARTIFACT_ID"] == request_output
    assert aggregate_step["env"]["PLAN_ARTIFACT_ID"] == plan_output
    assert "rm -rf .three-ci-validation/observed-artifacts" in (
        download_observed_step["run"]
    )
    assert "download-ci-validation-observed-artifacts" in (
        download_observed_step["run"]
    )
    assert (
        "--observed-artifacts-dir .three-ci-validation/observed-artifacts"
        in download_observed_step["run"]
    )
    assert (
        "--observed-artifacts-dir .three-ci-validation/observed-artifacts"
        in aggregate_step["run"]
    )
    assert "--expected-request-artifact-id" in aggregate_step["run"]
    assert "--expected-plan-artifact-id" in aggregate_step["run"]


def test_ci_validation_orchestrators_use_internal_dependency_state() -> None:
    """Runner-family orchestrators use local state only."""
    workflow = yaml.safe_load(_workflow("ci-validate.yml"))
    github_token_expression = "${{ github." + "token }}"

    for family in ("ubuntu", "windows"):
        steps = workflow["jobs"][f"execution-batch-{family}-orchestrator"][
            "steps"
        ]
        run_step = next(
            step
            for step in steps
            if step.get("name") == f"Run {family} orchestrator slot 00"
        )
        upload_step = next(
            step
            for step in steps
            if step.get("name") == f"Upload {family} batch evidence slot 00"
        )
        record_step = next(
            step
            for step in steps
            if step.get("name")
            == f"Record {family} batch evidence upload slot 00"
        )
        run = run_step["run"]

        assert run_step["shell"] == "bash"
        assert "uses" not in run_step
        assert "continue-on-error" not in run_step
        assert run_step["env"]["GH_TOKEN"] == github_token_expression
        assert "run-ci-validation-runner-family-orchestrator-step" in run
        assert f"--runner-family {family}" in run
        assert f'--job "execution-batch-{family}-orchestrator"' in run
        assert "--dependency-wait" not in run
        assert "--dependency-wait-timeout-seconds" not in run
        assert '--repository "$GITHUB_REPOSITORY"' in run
        assert (
            "--observed-artifacts-dir .three-ci-validation/observed-artifacts"
            in run
        )
        assert not any(
            "artifact-id state manifest" in str(step.get("name", ""))
            for step in steps
        )
        assert upload_step["uses"] == "actions/upload-artifact@v4"
        assert upload_step["with"]["name"] == (
            "${{ steps.run-slot-00.outputs."
            "batch_evidence_bundle_artifact_name }}"
        )
        record_run = record_step["run"]
        assert (
            "record-ci-validation-runner-family-orchestrator-upload"
            in record_run
        )
        assert "--artifact-id" in record_run
        assert "--artifact-name" in record_run
        assert "--name" not in run
        assert "pattern: three-ci-validation-*" not in run
        assert "orchestrator-state-manifest" not in run
        assert ".three-ci-validation/observed-artifacts" in run
        assert "batch-evidence-bundle.json" not in record_run


def test_ci_runner_family_outputs_bind_dependency_paths() -> None:
    """Runner-family rows carry exact dependency artifact names and paths."""
    plan = cast("dict[str, object]", batch_contracts.plan())
    batch_contracts.add_transitive_work_group(plan)
    context = batch_contracts.authorizing_context_kwargs()
    materialize = batch_contracts.materialize_ci_validation_execution_batches
    materialization = materialize(
        plan=plan,
        **context,
        created_at=batch_contracts.CREATED_AT,
        execution_workflow="CI Validation",
    )
    manifest = cast("dict[str, object]", materialization.manifest)
    outputs = control._ci_execution_batch_runner_family_outputs(
        manifest,
        cast("dict[str, object]", materialization.matrix),
    )

    ubuntu_rows = json.loads(outputs["ubuntu_execution_batch_matrix"])[
        "include"
    ]
    assert [row["batch-id"] for row in ubuntu_rows] == [
        batch["batch-id"]
        for batch in control._ci_execution_batches_in_dependency_order(manifest)
    ]
    assert ubuntu_rows[0]["expected-dependency-bundles"] == []
    layer_1_dependencies = ubuntu_rows[1]["expected-dependency-bundles"]
    layer_2_dependencies = ubuntu_rows[2]["expected-dependency-bundles"]

    assert len(layer_1_dependencies) == 1
    assert len(layer_2_dependencies) == 2
    assert layer_2_dependencies[0] == layer_1_dependencies[0]
    for dependency in [*layer_1_dependencies, *layer_2_dependencies]:
        artifact_ref = dependency["artifact-ref"]
        artifact_name = control.artifact_physical_name(artifact_ref)

        assert dependency["artifact-name"] == artifact_name
        assert dependency["artifact-path"] == (
            f".three-ci-validation/observed-artifacts/{artifact_name}"
        )
        assert dependency["artifact-metadata-path"] == (
            ".three-ci-validation/observed-artifacts/"
            f"{artifact_name}/artifact-metadata.json"
        )


def test_ci_execution_batch_runner_family_outputs_accept_deeper_dag() -> None:
    """Valid acyclic batch DAGs are not rejected because depth exceeds three."""
    batches: list[dict[str, object]] = []
    for index in range(6):
        batch_id = f"batch-{index}"
        artifact_ref = (
            f"ci-validation/batches/1/1/{batch_id}/batch-evidence-bundle.json"
        )
        batches.append(
            {
                "batch-id": batch_id,
                "runner-family": "ubuntu",
                "compatibility-profile": {
                    "ecosystem": "python",
                    "setup-profile": "setup-ubuntu-python",
                    "execution-profile": "exec-ecosystem-gate-python",
                },
                "depends-on-batches": [f"batch-{index - 1}"] if index else [],
                "expected-batch-evidence-bundle-ref": artifact_ref,
            }
        )
    manifest: dict[str, object] = {"batches": batches}
    matrix = {
        "include": [
            {
                "batch-id": batch["batch-id"],
                "runner-family": batch["runner-family"],
                "expected-batch-evidence-bundle-ref": batch[
                    "expected-batch-evidence-bundle-ref"
                ],
            }
            for batch in batches
        ]
    }
    outputs = control._ci_execution_batch_runner_family_outputs(
        manifest,
        matrix,
    )
    layer_by_batch = control._ci_execution_batch_dependency_layers(manifest)
    emitted_ids = [
        row["batch-id"]
        for row in json.loads(outputs["ubuntu_execution_batch_matrix"])[
            "include"
        ]
    ]

    assert max(layer_by_batch.values()) >= 5
    assert emitted_ids == [batch["batch-id"] for batch in batches]
    assert emitted_ids[-1] == "batch-5"


def test_ci_runner_family_orchestrator_readiness_avoids_layer_barrier(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Diamond DAG children become ready without waiting for unrelated peers."""
    manifest: dict[str, object] = {
        "batches": [
            {
                "batch-id": "batch-a",
                "runner-family": "ubuntu",
                "depends-on-batches": [],
                "expected-batch-evidence-bundle-ref": (
                    "ci-validation/batches/1/1/batch-a/batch-evidence-bundle.json"
                ),
            },
            {
                "batch-id": "batch-b",
                "runner-family": "ubuntu",
                "depends-on-batches": ["batch-a"],
                "expected-batch-evidence-bundle-ref": (
                    "ci-validation/batches/1/1/batch-b/batch-evidence-bundle.json"
                ),
            },
            {
                "batch-id": "batch-c",
                "runner-family": "ubuntu",
                "depends-on-batches": ["batch-a"],
                "expected-batch-evidence-bundle-ref": (
                    "ci-validation/batches/1/1/batch-c/batch-evidence-bundle.json"
                ),
            },
            {
                "batch-id": "batch-d",
                "runner-family": "ubuntu",
                "depends-on-batches": ["batch-b", "batch-c"],
                "expected-batch-evidence-bundle-ref": (
                    "ci-validation/batches/1/1/batch-d/batch-evidence-bundle.json"
                ),
            },
        ],
    }
    batches_by_id = {
        str(batch["batch-id"]): batch
        for batch in cast("list[dict[str, object]]", manifest["batches"])
    }
    state_dir = SCRATCH / "orchestrator-readiness-state"
    observed_root = SCRATCH / "orchestrator-readiness-observed"
    if state_dir.exists():
        shutil.rmtree(state_dir)
    if observed_root.exists():
        shutil.rmtree(observed_root)
    batch_a_ref = cast(
        "str",
        cast("dict[str, object]", manifest["batches"])[0][
            "expected-batch-evidence-bundle-ref"
        ],
    )
    batch_a_name = artifact_physical_name(batch_a_ref)
    control._write_json(
        control._ci_orchestrator_uploaded_state_path(state_dir, "batch-a"),
        {
            "batch-id": "batch-a",
            "artifact-name": batch_a_name,
            "artifact-ref": batch_a_ref,
            "artifact-instance-id": "batch-a-artifact",
            "run-id": "1",
            "run-attempt": "1",
            "producer-boundary": "execution-batch",
            "admission-source": (
                control._CI_ORCHESTRATOR_STATE_ADMISSION_SOURCE
            ),
        },
    )

    def fake_live_by_id(**kwargs: object) -> Mapping[str, object] | None:
        assert kwargs["artifact_id"] == "batch-a-artifact"
        return {
            "id": "batch-a-artifact",
            "name": batch_a_name,
            "expired": False,
            "workflow_run": {"id": 1, "run_attempt": 1},
        }

    def fake_download(
        _repository: str,
        _artifact_api: Mapping[str, object],
        _artifact_name_value: str,
        destination: Path,
    ) -> None:
        destination.mkdir(parents=True, exist_ok=True)
        (destination / "batch-evidence-bundle.json").write_text(
            "{}",
            encoding="utf-8",
        )

    monkeypatch.setattr(
        control,
        "_ci_live_artifact_api_instance_by_id",
        fake_live_by_id,
    )
    monkeypatch.setattr(control, "_download_artifact_by_id", fake_download)
    monkeypatch.setattr(
        control,
        "_ci_live_artifact_api_instance_by_id",
        fake_live_by_id,
    )

    assert control._ci_orchestrator_dependencies_ready(
        batches_by_id["batch-b"],
        batches_by_id=batches_by_id,
        repository="owner/repo",
        run_id="1",
        run_attempt="1",
        state_dir=state_dir,
        observed_root=observed_root,
    )
    assert control._ci_orchestrator_dependencies_ready(
        batches_by_id["batch-c"],
        batches_by_id=batches_by_id,
        repository="owner/repo",
        run_id="1",
        run_attempt="1",
        state_dir=state_dir,
        observed_root=observed_root,
    )
    assert not control._ci_orchestrator_dependencies_ready(
        batches_by_id["batch-d"],
        batches_by_id=batches_by_id,
        repository="owner/repo",
        run_id="1",
        run_attempt="1",
        state_dir=state_dir,
        observed_root=observed_root,
    )


def test_ci_runner_family_orchestrator_verifies_same_family_upload_live_id(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Same-family dependencies are refreshed from recorded live artifact id."""
    batches: list[dict[str, object]] = [
        {
            "batch-id": "upstream",
            "runner-family": "ubuntu",
            "depends-on-batches": [],
            "expected-batch-evidence-bundle-ref": (
                "ci-validation/batches/1/1/upstream/batch-evidence-bundle.json"
            ),
        },
        {
            "batch-id": "dependent",
            "runner-family": "ubuntu",
            "depends-on-batches": ["upstream"],
            "expected-batch-evidence-bundle-ref": (
                "ci-validation/batches/1/1/dependent/batch-evidence-bundle.json"
            ),
        },
    ]
    state_dir = SCRATCH / "orchestrator-same-family-live-state"
    observed_root = SCRATCH / "orchestrator-same-family-live-observed"
    shutil.rmtree(state_dir, ignore_errors=True)
    shutil.rmtree(observed_root, ignore_errors=True)
    upstream_ref = cast("str", batches[0]["expected-batch-evidence-bundle-ref"])
    upstream_name = artifact_physical_name(upstream_ref)
    upstream_dir = observed_root / upstream_name
    upstream_dir.mkdir(parents=True)
    (upstream_dir / "batch-evidence-bundle.json").write_text(
        '{"source":"stale-cache"}',
        encoding="utf-8",
    )
    stale_sentinel = upstream_dir / "stale-only-sentinel"
    stale_sentinel.write_text("remove me", encoding="utf-8")
    control._write_json(
        control._ci_orchestrator_uploaded_state_path(state_dir, "upstream"),
        {
            "batch-id": "upstream",
            "artifact-name": upstream_name,
            "artifact-ref": upstream_ref,
            "artifact-instance-id": "9001",
            "run-id": "1",
            "run-attempt": "1",
            "producer-boundary": "execution-batch",
            "admission-source": (
                control._CI_ORCHESTRATOR_STATE_ADMISSION_SOURCE
            ),
        },
    )
    live_lookups: list[str] = []
    downloads: list[str] = []

    def fake_live_by_id(**kwargs: object) -> Mapping[str, object] | None:
        live_lookups.append(str(kwargs["artifact_id"]))
        return {
            "id": "9001",
            "name": upstream_name,
            "expired": False,
            "workflow_run": {"id": 1, "run_attempt": 1},
        }

    def fake_download(
        _repository: str,
        artifact_api: Mapping[str, object],
        artifact_name_value: str,
        destination: Path,
    ) -> None:
        downloads.append(str(artifact_api["id"]))
        assert artifact_name_value == upstream_name
        destination.mkdir(parents=True, exist_ok=True)
        (destination / "batch-evidence-bundle.json").write_text(
            '{"source":"live-download"}',
            encoding="utf-8",
        )

    monkeypatch.setattr(
        control,
        "_ci_live_artifact_api_instance_by_id",
        fake_live_by_id,
    )
    monkeypatch.setattr(control, "_download_artifact_by_id", fake_download)
    admissions: dict[str, dict[str, object]] = {}

    assert not control._ci_orchestrator_dependencies_ready(
        batches[1],
        batches_by_id={batch["batch-id"]: batch for batch in batches},
        repository="",
        run_id="1",
        run_attempt="1",
        state_dir=state_dir,
        observed_root=observed_root,
        dependency_admissions=admissions,
    )
    assert control._ci_orchestrator_dependencies_ready(
        batches[1],
        batches_by_id={batch["batch-id"]: batch for batch in batches},
        repository="owner/repo",
        run_id="1",
        run_attempt="1",
        state_dir=state_dir,
        observed_root=observed_root,
        dependency_admissions=admissions,
    )

    assert live_lookups == ["9001"]
    assert downloads == ["9001"]
    assert not stale_sentinel.exists()
    assert json.loads(
        (upstream_dir / "batch-evidence-bundle.json").read_text(
            encoding="utf-8"
        )
    ) == {"source": "live-download"}
    assert admissions[upstream_name]["artifact-instance-id"] == "9001"
    assert admissions[upstream_name]["admission-source"] == (
        "orchestrator-artifact-id-state"
    )


def test_ci_runner_family_orchestrator_rejects_same_family_state_without_source():  # noqa: E501
    """Same-family recorded upload state must be artifact-id-state admitted."""
    dependency = {
        "batch-id": "upstream",
        "runner-family": "ubuntu",
        "expected-batch-evidence-bundle-ref": (
            "ci-validation/batches/1/1/upstream/batch-evidence-bundle.json"
        ),
    }
    artifact_ref = cast("str", dependency["expected-batch-evidence-bundle-ref"])
    recorded_upload = {
        "batch-id": "upstream",
        "artifact-name": artifact_physical_name(artifact_ref),
        "artifact-ref": artifact_ref,
        "artifact-instance-id": "9001",
        "run-id": "1",
        "run-attempt": "1",
        "producer-boundary": "execution-batch",
        "admission-source": "github-actions-live-api",
    }

    assert not control._ci_orchestrator_recorded_upload_matches_dependency(
        dependency,
        recorded_upload,
        run_id="1",
        run_attempt="1",
    )


def test_ci_runner_family_orchestrator_selects_later_ready_batch() -> None:
    """An earlier unready batch does not block unrelated ready family work."""
    batches: list[dict[str, object]] = [
        {
            "batch-id": "same-family-provider",
            "runner-family": "ubuntu",
            "depends-on-batches": [],
            "expected-batch-evidence-bundle-ref": (
                "ci-validation/batches/1/1/same-family-provider/batch-evidence-bundle.json"
            ),
        },
        {
            "batch-id": "blocked-ubuntu",
            "runner-family": "ubuntu",
            "depends-on-batches": ["same-family-provider"],
            "expected-batch-evidence-bundle-ref": (
                "ci-validation/batches/1/1/blocked-ubuntu/batch-evidence-bundle.json"
            ),
        },
        {
            "batch-id": "ready-ubuntu",
            "runner-family": "ubuntu",
            "depends-on-batches": [],
            "expected-batch-evidence-bundle-ref": (
                "ci-validation/batches/1/1/ready-ubuntu/batch-evidence-bundle.json"
            ),
        },
    ]
    state_dir = SCRATCH / "orchestrator-select-state"
    observed_root = SCRATCH / "orchestrator-select-observed"
    shutil.rmtree(state_dir, ignore_errors=True)
    shutil.rmtree(observed_root, ignore_errors=True)

    ready, waiting = control._ci_orchestrator_select_ready_batch(
        [batches[1], batches[2]],
        batches_by_id={str(batch["batch-id"]): batch for batch in batches},
        repository="",
        run_id="1",
        run_attempt="1",
        state_dir=state_dir,
        observed_root=observed_root,
        dependency_admissions={},
    )

    assert ready is not None
    assert ready["batch-id"] == "ready-ubuntu"
    assert waiting == ["blocked-ubuntu"]


def test_ci_orchestrator_matrix_row_includes_identity_matrix() -> None:
    """Orchestrator rows remain compatible with batch command lookup."""
    _plan, manifest = _ci_batch_contract_plan_and_manifest()
    batch = cast("list[dict[str, object]]", manifest["batches"])[0]

    row = control._ci_orchestrator_matrix_row(manifest, batch)

    assert row["identity-matrix"] == {
        "batch-id": batch["batch-id"],
        "runner-family": batch["runner-family"],
        "expected-batch-evidence-bundle-ref": batch[
            "expected-batch-evidence-bundle-ref"
        ],
    }
    assert control._ci_execution_batch_from_matrix_row(manifest, row) == batch


def test_ci_orchestrator_upload_record_writes_local_artifact_id_state() -> None:
    """Recorded uploads produce local trusted artifact-id state only."""
    scratch = _ci_batch_bundle_scratch("orchestrator-upload-state-manifest")
    try:
        _plan, manifest = _ci_batch_contract_plan_and_manifest()
        batch = cast("list[dict[str, object]]", manifest["batches"])[0]
        artifact_ref = cast("str", batch["expected-batch-evidence-bundle-ref"])
        manifest_path = scratch / "execution-batch-manifest.json"
        manifest_path.write_text(json.dumps(manifest), encoding="utf-8")
        observed_root = scratch / "observed"
        state_dir = scratch / "state"

        assert (
            control._cmd_record_ci_validation_runner_family_orchestrator_upload(
                argparse.Namespace(
                    execution_batch_manifest=str(manifest_path),
                    batch_id=batch["batch-id"],
                    artifact_id="artifact-123",
                    artifact_name=artifact_physical_name(artifact_ref),
                    expected_run_id=batch_contracts.RUN_ID,
                    expected_run_attempt=batch_contracts.RUN_ATTEMPT,
                    orchestrator_slot_index="7",
                    observed_artifacts_dir=str(observed_root),
                    state_dir=str(state_dir),
                    github_output=None,
                )
            )
            == 0
        )

        uploaded_state = json.loads(
            control._ci_orchestrator_uploaded_state_path(
                state_dir,
                cast("str", batch["batch-id"]),
            ).read_text(encoding="utf-8")
        )
        assert uploaded_state["artifact-instance-id"] == "artifact-123"
        assert uploaded_state["run-attempt"] == batch_contracts.RUN_ATTEMPT
        assert uploaded_state["orchestrator-slot-index"] == "7"
        assert "payload-digest" not in uploaded_state
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_live_artifact_rejects_conflicting_observed_attempts() -> None:
    """Every provided live run/run_attempt observation must match."""
    artifact_name_value = "three-ci-validation-1-1-example"

    assert not control._ci_live_artifact_matches_expected(
        {
            "id": "4242",
            "name": artifact_name_value,
            "expired": False,
            "workflow_run": {"id": 1, "run_attempt": 1},
            "run_id": "1",
            "run_attempt": "2",
        },
        artifact_id="4242",
        artifact_name_value=artifact_name_value,
        run_id="1",
        run_attempt="1",
    )


def test_ci_live_artifact_rejects_conflicting_observed_run_ids() -> None:
    """Top-level run observations cannot override workflow_run mismatches."""
    artifact_name_value = "three-ci-validation-1-1-example"

    assert not control._ci_live_artifact_matches_expected(
        {
            "id": "4242",
            "name": artifact_name_value,
            "expired": False,
            "workflow_run": {"id": 2, "run_attempt": 1},
            "run_id": "1",
            "run_attempt": "1",
        },
        artifact_id="4242",
        artifact_name_value=artifact_name_value,
        run_id="1",
        run_attempt="1",
    )


def test_ci_runner_family_orchestrator_fails_closed_when_waiting() -> None:
    """Cross-family batch dependencies fail closed before any peer wait."""
    plan, manifest = _ci_batch_contract_plan_and_manifest()
    batch = cast("list[dict[str, object]]", manifest["batches"])[0]
    batch["depends-on-batches"] = ["missing-cross-runner"]
    cast("list[dict[str, object]]", manifest["batches"]).insert(
        0,
        {
            "batch-id": "missing-cross-runner",
            "runner-family": "windows",
            "depends-on-batches": [],
            "expected-batch-evidence-bundle-ref": (
                "ci-validation/batches/1/1/missing-cross-runner/batch-evidence-bundle.json"
            ),
        },
    )
    scratch = _ci_batch_bundle_scratch("orchestrator-fail-closed")
    plan_path = scratch / "plan.json"
    request_path = scratch / "request.json"
    manifest_path = scratch / "execution-batch-manifest.json"
    changed_files_path = scratch / "changed-files.json"
    fact_snapshot_path = scratch / "fact-snapshot.json"
    context = batch_contracts.authorizing_context_kwargs()
    plan_path.write_text(json.dumps(plan), encoding="utf-8")
    request_path.write_text(json.dumps(context["request"]), encoding="utf-8")
    manifest_path.write_text(json.dumps(manifest), encoding="utf-8")
    changed_files_path.write_text(
        json.dumps(context["changed_files_snapshot"]),
        encoding="utf-8",
    )
    fact_snapshot_path.write_text(
        json.dumps(context["fact_snapshot"]),
        encoding="utf-8",
    )

    with pytest.raises(
        RuntimeError,
        match="does not support cross-family batch dependencies",
    ):
        control._cmd_run_ci_validation_runner_family_orchestrator_step(
            argparse.Namespace(
                plan=str(plan_path),
                request=str(request_path),
                execution_batch_manifest=str(manifest_path),
                changed_files_snapshot=str(changed_files_path),
                fact_snapshot=str(fact_snapshot_path),
                runner_family="ubuntu",
                repository="",
                workflow="CI Validation",
                job="execution-batch-ubuntu-orchestrator",
                expected_run_id=batch_contracts.RUN_ID,
                expected_run_attempt=batch_contracts.RUN_ATTEMPT,
                observed_artifacts_dir=str(scratch / "observed-artifacts"),
                state_dir=str(scratch / "state"),
                work_dir=str(scratch / "work"),
                slot_index="0",
                observed_commit_sha=batch_contracts.TREE_SHA,
                repo_root=str(REPO_ROOT),
                github_output="",
            )
        )


def test_ci_validation_batch_observation_cli_is_not_public() -> None:
    """Caller-writable producer observation sidecars are not exposed."""
    result = subprocess.run(  # noqa: S603
        [
            sys.executable,
            str(SCRIPT),
            "write-ci-validation-batch-artifact-observation",
            "--help",
        ],
        check=False,
        capture_output=True,
        text=True,
    )

    assert result.returncode != 0
    assert "invalid choice" in result.stderr


def test_ci_validation_retries_uv_setup_failures() -> None:
    """Matrix rows retry setup-uv after transient action failures."""
    workflow = yaml.safe_load(_workflow("ci-validate.yml"))
    setup_action = "astral-sh/setup-uv@08807647e7069bb48b6ef5acd8ec9567f424441b"
    setup_count = 0

    for job in workflow["jobs"].values():
        steps = job["steps"]
        for index, step in enumerate(steps):
            if step.get("name") != "Install uv":
                continue
            setup_count += 1
            retry_step = steps[index + 1]

            assert step["id"] == "setup-uv"
            assert step["continue-on-error"] is True
            assert step["uses"] == setup_action
            assert step["with"]["version"] == "0.10.9"
            assert retry_step["name"] == "Retry uv installation"
            assert (
                retry_step["if"] == "${{ steps.setup-uv.outcome == 'failure' }}"
            )
            assert retry_step["uses"] == setup_action
            assert retry_step["with"]["version"] == "0.10.9"

    assert setup_count == 6


def test_ci_validation_workflow_derives_normal_event_ranges() -> None:
    """Normal PR/push CI requests pass confirmed affected ranges."""
    workflow = _workflow("ci-validate.yml")
    normalize = _step_block(workflow, "Write planner-facing request")

    assert 'validation_commit_sha="$(git rev-parse HEAD)"' in normalize
    assert 'event_path = os.environ["GITHUB_EVENT_PATH"]' in normalize
    assert 'if [ "$CI_MODE" != "scheduled_full" ]; then' in normalize
    assert '"--range-status"' in normalize
    assert '"available"' in normalize
    assert '"--changed-files-json"' in normalize
    assert '"--base-tip-sha"' in normalize
    assert 'unavailable("incomplete")' in normalize
    assert 'unavailable("unconfirmed-provenance")' in normalize
    assert (
        "--range-status unavailable --range-diagnostic-detail incomplete"
        not in (normalize)
    )
    assert 'validation_ref="refs/pull/${CI_EVENT_NUMBER}/head"' in normalize
    assert '--event-number "$CI_EVENT_NUMBER"' in normalize
    assert '--validation-commit-sha "$validation_commit_sha"' in normalize
    assert '--validation-ref "$validation_ref"' in normalize
    assert '"${range_args[@]}"' in normalize


@pytest.mark.parametrize(
    ("mode", "event", "expected_args"),
    [
        (
            "push",
            {"before": "a" * 40, "after": "b" * 40},
            [
                "--range-status",
                "available",
                "--base-sha",
                "a" * 40,
                "--head-sha",
                "b" * 40,
                "--changed-files-json",
                '["README.md","src/public/lib/example.py"]',
            ],
        ),
        (
            "pull_request",
            {
                "pull_request": {
                    "base": {"sha": "c" * 40},
                    "head": {"sha": "b" * 40},
                },
            },
            [
                "--range-status",
                "available",
                "--base-sha",
                "a" * 40,
                "--base-tip-sha",
                "c" * 40,
                "--head-sha",
                "b" * 40,
                "--changed-files-json",
                '["README.md","src/public/lib/example.py"]',
            ],
        ),
    ],
)
def test_ci_validation_workflow_range_derivation_emits_available_args(
    monkeypatch: pytest.MonkeyPatch,
    mode: str,
    event: Mapping[str, object],
    expected_args: list[str],
) -> None:
    """Embedded derivation confirms PR/push range endpoints and file lists."""
    scratch = SCRATCH / f"ci-validation-range-available-{mode}"
    shutil.rmtree(scratch, ignore_errors=True)
    scratch.mkdir(parents=True)
    (scratch / ".three-ci-validation/normalize").mkdir(parents=True)
    event_path = scratch / "event.json"
    event_path.write_text(json.dumps(event), encoding="utf-8")
    known_commits = {"a" * 40, "b" * 40, "c" * 40}
    raw_diff = b"M\0src/public/lib/example.py\0M\0README.md\0"

    def fake_run(
        args: list[str],
        **kwargs: object,
    ) -> subprocess.CompletedProcess[bytes]:
        check = bool(kwargs.get("check", False))
        if args[:2] == ["git", "rev-parse"]:
            return subprocess.CompletedProcess(
                args,
                0,
                stdout=b"b" * 40 + b"\n",
            )
        if args[:2] == ["git", "cat-file"]:
            return subprocess.CompletedProcess(
                args,
                0 if args[3].split("^{", 1)[0] in known_commits else 1,
                stdout=b"",
            )
        if args[:2] == ["git", "merge-base"]:
            return subprocess.CompletedProcess(
                args,
                0,
                stdout=b"a" * 40 + b"\n",
            )
        if args[:4] == ["git", "diff", "--name-status", "--find-renames"]:
            return subprocess.CompletedProcess(args, 0, stdout=raw_diff)
        if check:
            raise subprocess.CalledProcessError(1, args)
        return subprocess.CompletedProcess(args, 1, stdout=b"")

    try:
        monkeypatch.chdir(scratch)
        monkeypatch.setenv("GITHUB_EVENT_PATH", str(event_path))
        monkeypatch.setenv("CI_MODE", mode)
        monkeypatch.setattr(subprocess, "run", fake_run)

        exec(_ci_range_derivation_python(), {})  # noqa: S102

        assert (
            _read_ci_range_args(
                scratch / ".three-ci-validation/normalize/range-args.sh",
            )
            == expected_args
        )
    finally:
        monkeypatch.chdir(REPO_ROOT)
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_validation_workflow_range_derivation_fails_closed_when_unconfirmed(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Unconfirmable endpoints remain unavailable instead of being guessed."""
    scratch = SCRATCH / "ci-validation-range-unconfirmed"
    shutil.rmtree(scratch, ignore_errors=True)
    scratch.mkdir(parents=True)
    (scratch / ".three-ci-validation/normalize").mkdir(parents=True)
    event_path = scratch / "event.json"
    event_path.write_text(
        json.dumps({"before": "a" * 40, "after": "b" * 40}),
        encoding="utf-8",
    )

    def fake_run(
        args: list[str],
        **kwargs: object,
    ) -> subprocess.CompletedProcess[bytes]:
        check = bool(kwargs.get("check", False))
        if args[:2] == ["git", "rev-parse"]:
            return subprocess.CompletedProcess(
                args,
                0,
                stdout=b"b" * 40 + b"\n",
            )
        if args[:2] in (["git", "cat-file"], ["git", "fetch"]):
            return subprocess.CompletedProcess(args, 1, stdout=b"")
        if check:
            raise subprocess.CalledProcessError(1, args)
        return subprocess.CompletedProcess(args, 1, stdout=b"")

    try:
        monkeypatch.chdir(scratch)
        monkeypatch.setenv("GITHUB_EVENT_PATH", str(event_path))
        monkeypatch.setenv("CI_MODE", "push")
        monkeypatch.setattr(subprocess, "run", fake_run)

        exec(_ci_range_derivation_python(), {})  # noqa: S102

        assert _read_ci_range_args(
            scratch / ".three-ci-validation/normalize/range-args.sh",
        ) == [
            "--range-status",
            "unavailable",
            "--range-diagnostic-detail",
            "incomplete",
        ]
    finally:
        monkeypatch.chdir(REPO_ROOT)
        shutil.rmtree(scratch, ignore_errors=True)


@pytest.mark.parametrize(
    ("case_id", "raw_diff"),
    [
        ("backslash", b"M\0src\\bad.py\0"),
        ("dot-prefix", b"M\0./README.md\0"),
        ("trailing-slash", b"M\0docs/\0"),
        ("empty-segment", b"M\0src//file.py\0"),
        ("dot-segment", b"M\0src/./file.py\0"),
        ("duplicate", b"M\0README.md\0M\0README.md\0"),
        ("invalid-utf8", b"M\0src/\xff.py\0"),
    ],
)
def test_ci_validation_workflow_marks_bad_changed_paths_inconsistent(
    monkeypatch: pytest.MonkeyPatch,
    case_id: str,
    raw_diff: bytes,
) -> None:
    """Bad git-diff paths fail closed as inconsistent."""
    scratch = SCRATCH / f"ci-validation-range-{case_id}"
    shutil.rmtree(scratch, ignore_errors=True)
    scratch.mkdir(parents=True)
    (scratch / ".three-ci-validation/normalize").mkdir(parents=True)
    base_sha = "a" * 40
    head_sha = "b" * 40
    event_path = scratch / "event.json"
    event_path.write_text(
        json.dumps({"before": base_sha, "after": head_sha}),
        encoding="utf-8",
    )

    def fake_run(
        args: list[str],
        **kwargs: object,
    ) -> subprocess.CompletedProcess[bytes]:
        check = bool(kwargs.get("check", False))
        if args[:2] == ["git", "rev-parse"]:
            return subprocess.CompletedProcess(
                args,
                0,
                stdout=f"{head_sha}\n".encode(),
            )
        if args[:2] == ["git", "cat-file"]:
            return subprocess.CompletedProcess(args, 0, stdout=b"")
        if args[:4] == ["git", "diff", "--name-status", "--find-renames"]:
            return subprocess.CompletedProcess(args, 0, stdout=raw_diff)
        if check:
            raise subprocess.CalledProcessError(1, args)
        return subprocess.CompletedProcess(args, 1, stdout=b"")

    try:
        monkeypatch.chdir(scratch)
        monkeypatch.setenv("GITHUB_EVENT_PATH", str(event_path))
        monkeypatch.setenv("CI_MODE", "push")
        monkeypatch.setattr(subprocess, "run", fake_run)

        exec(_ci_range_derivation_python(), {})  # noqa: S102

        assert (
            (
                scratch / ".three-ci-validation/normalize/range-args.sh"
            ).read_text(encoding="utf-8")
            == "range_args=( --range-status unavailable "
            "--range-diagnostic-detail inconsistent)\n"
        )
    finally:
        monkeypatch.chdir(REPO_ROOT)
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_validation_workflow_checks_canonical_changed_paths() -> None:
    """Embedded range derivation matches request changed-file path rules."""
    normalize = _step_block(
        _workflow("ci-validate.yml"),
        "Write planner-facing request",
    )

    assert "def is_canonical_repo_path(value):" in normalize
    assert 'value.startswith(("/", "./"))' in normalize
    assert 'value.endswith("/")' in normalize
    assert '"\\\\" in value' in normalize
    assert 'part not in {"", ".", ".."}' in normalize
    assert "NonCanonicalChangedPathError" in normalize
    assert "or value in seen" in normalize
    assert 'unavailable("inconsistent")' in normalize


def test_ci_validation_workflow_checks_out_pull_request_head() -> None:
    """PR validation executes on the confirmed head boundary, not merge refs."""
    workflow = _workflow("ci-validate.yml")
    checkout_count = workflow.count("uses: actions/checkout@v4")

    assert checkout_count > 0
    assert (
        workflow.count(
            "ref: ${{ github.event.pull_request.head.sha || github.sha }}",
        )
        == checkout_count
    )


def test_ci_validation_execution_batches_use_full_checkout_for_nbgv() -> None:
    """Execution batches need full history for NBGV version height."""
    workflow = yaml.safe_load(_workflow("ci-validate.yml"))

    for family in ("ubuntu", "windows"):
        steps = workflow["jobs"][f"execution-batch-{family}-orchestrator"][
            "steps"
        ]
        checkout_step = next(
            step for step in steps if step.get("uses") == "actions/checkout@v4"
        )

        assert checkout_step["with"]["fetch-depth"] == 0


def test_ci_validation_batch_evidence_observes_checked_out_head() -> None:
    """Batch evidence binds to the checked-out tree, not merge refs."""
    workflow = yaml.safe_load(_workflow("ci-validate.yml"))

    for family in ("ubuntu", "windows"):
        steps = workflow["jobs"][f"execution-batch-{family}-orchestrator"][
            "steps"
        ]
        run_step = next(
            step
            for step in steps
            if step.get("name") == f"Run {family} orchestrator slot 00"
        )
        run = run_step["run"]

        assert 'observed_commit_sha="$(git rev-parse HEAD)"' in run
        assert '--observed-commit-sha "$observed_commit_sha"' in run
        assert '--observed-commit-sha "$GITHUB_SHA"' not in run


def test_write_ci_validation_request_accepts_available_pr_and_push_ranges() -> (
    None
):
    """Request writer preserves available ranges for normal CI events."""
    scratch = SCRATCH / "ci-validation-available-ranges"
    shutil.rmtree(scratch, ignore_errors=True)
    scratch.mkdir(parents=True)
    try:
        cases = [
            {
                "mode": "pull_request",
                "event_number": "42",
                "validation_ref": "refs/pull/42/head",
                "base_sha": "a" * 40,
                "base_tip_sha": "c" * 40,
                "head_sha": "b" * 40,
                "changed_files": [
                    "src/public/lib/nbgv-python/pyproject.toml",
                    "README.md",
                ],
            },
            {
                "mode": "push",
                "event_number": "",
                "validation_ref": "refs/heads/main",
                "base_sha": "d" * 40,
                "base_tip_sha": "",
                "head_sha": "e" * 40,
                "changed_files": ["README.md"],
            },
        ]
        for case in cases:
            out = scratch / f"{case['mode']}.json"
            assert (
                control._cmd_write_ci_validation_request(
                    argparse.Namespace(
                        mode=case["mode"],
                        repository="hcoona/three",
                        workflow="CI Validation",
                        run_id="25887422010",
                        run_attempt="1",
                        event_name=case["mode"],
                        event_number=case["event_number"],
                        actor="octocat",
                        validation_commit_sha=case["head_sha"],
                        validation_ref=case["validation_ref"],
                        base_sha=case["base_sha"],
                        base_tip_sha=case["base_tip_sha"],
                        head_sha=case["head_sha"],
                        changed_files_json=json.dumps(
                            list(reversed(case["changed_files"])),
                        ),
                        range_status="available",
                        range_diagnostic_detail="missing",
                        created_at="2026-05-14T21:09:21Z",
                        out=str(out),
                        github_output=None,
                    ),
                )
                == 0
            )
            request = json.loads(out.read_text(encoding="utf-8"))
            affected = request["affected-range"]
            assert affected["status"] == "available"
            assert affected["base-sha"] == case["base_sha"]
            assert affected["head-sha"] == case["head_sha"]
            assert affected["base-tip-sha"] == (case["base_tip_sha"] or None)
            assert affected["changed-files"] == sorted(case["changed_files"])
            assert affected["diagnostic"] is None
            assert affected["diagnostic-detail"] is None
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


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
        assert "aggregate_artifact_name" not in outputs
        assert "aggregate_evidence_manifest_artifact_name" in outputs
        assert "aggregate_summary_artifact_name" in outputs
        assert outputs["planner_diagnostics_artifact_name"].startswith(
            "three-ci-validation-",
        )
        assert outputs["planner_diagnostics_artifact_name"] != (
            "ci-validation-planner-diagnostics-25887422010-1"
        )
    finally:
        shutil.rmtree(SCRATCH, ignore_errors=True)


def _ci_artifact_metadata(
    artifact_ref: str,
    *,
    artifact_id: int,
    run_id: int = 25887422010,
    expired: bool = False,
) -> GitHubActionsArtifactMetadata:
    return GitHubActionsArtifactMetadata(
        artifact_id=artifact_id,
        name=artifact_physical_name(artifact_ref),
        created_at="2026-05-14T21:09:21Z",
        expired=expired,
        workflow_run_id=run_id,
    )


def _ci_expected_artifact(
    artifact_ref: str,
    *,
    artifact_id: int | str,
    boundary: str,
    job: str,
) -> dict[str, object]:
    return {
        "artifact-ref": artifact_ref,
        "artifact-instance-id": str(artifact_id),
        "producer-boundary": boundary,
        "producer-job": job,
    }


def test_ci_validation_producer_boundary_accepts_expected_artifact() -> None:
    """Exact one artifact plus expected producer job output is admissible."""
    plan_ref = control.ci_validation_plan_artifact_ref(
        run_id="25887422010",
        run_attempt="1",
    )

    diagnostics = control._ci_verify_expected_artifact_producer_boundaries(
        artifacts=[_ci_artifact_metadata(plan_ref, artifact_id=7001)],
        expected_artifacts=[
            _ci_expected_artifact(
                plan_ref,
                artifact_id=7001,
                boundary="plan",
                job="plan",
            )
        ],
        workflow="CI Validation",
        run_id="25887422010",
        run_attempt="1",
    )

    assert diagnostics == []


def test_ci_validation_boundary_ignores_prior_attempt_final_count() -> None:
    """Known prior-attempt artifacts do not poison current final counts."""
    run_id = "25887422010"
    current_plan_ref = control.ci_validation_plan_artifact_ref(
        run_id=run_id,
        run_attempt="2",
    )
    prior_plan_ref = control.ci_validation_plan_artifact_ref(
        run_id=run_id,
        run_attempt="1",
    )

    diagnostics = control._ci_verify_expected_artifact_producer_boundaries(
        artifacts=[
            _ci_artifact_metadata(current_plan_ref, artifact_id=7002),
            _ci_artifact_metadata(prior_plan_ref, artifact_id=7001),
        ],
        expected_artifacts=[
            _ci_expected_artifact(
                current_plan_ref,
                artifact_id=7002,
                boundary="plan",
                job="plan",
            )
        ],
        workflow="CI Validation",
        run_id=run_id,
        run_attempt="2",
        expected_prefixed_validation_artifacts=1,
    )

    assert diagnostics == []


def test_ci_validation_boundary_fails_for_current_unknown_final_count() -> None:
    """Unknown current-attempt prefixed artifacts still fail closed."""
    run_id = "25887422010"
    current_plan_ref = control.ci_validation_plan_artifact_ref(
        run_id=run_id,
        run_attempt="2",
    )
    unknown_name = "three-ci-validation-25887422010-1-" + "f" * 64

    diagnostics = control._ci_verify_expected_artifact_producer_boundaries(
        artifacts=[
            _ci_artifact_metadata(current_plan_ref, artifact_id=7002),
            GitHubActionsArtifactMetadata(
                artifact_id=7999,
                name=unknown_name,
                created_at="2026-05-14T21:09:21Z",
                expired=False,
                workflow_run_id=int(run_id),
            ),
        ],
        expected_artifacts=[
            _ci_expected_artifact(
                current_plan_ref,
                artifact_id=7002,
                boundary="plan",
                job="plan",
            )
        ],
        workflow="CI Validation",
        run_id=run_id,
        run_attempt="2",
        expected_prefixed_validation_artifacts=1,
    )

    assert diagnostics
    assert diagnostics[0]["detail"] == "final-namespace-closure-mismatch"


def test_ci_validation_producer_boundary_accepts_execution_batch_manifest() -> (
    None
):
    """Execution-batch manifests require materialization upload ids."""
    manifest_ref = control.ci_validation_execution_batch_manifest_artifact_ref(
        run_id="25887422010",
        run_attempt="1",
    )

    diagnostics = control._ci_verify_expected_artifact_producer_boundaries(
        artifacts=[_ci_artifact_metadata(manifest_ref, artifact_id=9001)],
        expected_artifacts=[
            _ci_expected_artifact(
                manifest_ref,
                artifact_id=9001,
                boundary="materialize-execution-batches",
                job="materialize-execution-batches",
            )
        ],
        workflow="CI Validation",
        run_id="25887422010",
        run_attempt="1",
    )

    assert diagnostics == []


@pytest.mark.parametrize(
    ("artifact_ref", "artifact_kind"),
    [
        (
            control.ci_validation_aggregate_evidence_manifest_artifact_ref(
                run_id="25887422010",
                run_attempt="1",
            ),
            "aggregate-evidence-manifest",
        ),
        (
            control.ci_validation_aggregate_summary_artifact_ref(
                run_id="25887422010",
                run_attempt="1",
            ),
            "aggregate-summary",
        ),
    ],
)
def test_ci_validation_producer_boundary_accepts_current_final_refs(
    artifact_ref: str,
    artifact_kind: str,
) -> None:
    """Current final aggregate artifacts are registered control artifacts."""
    diagnostics = control._ci_verify_expected_artifact_producer_boundaries(
        artifacts=[_ci_artifact_metadata(artifact_ref, artifact_id=9901)],
        expected_artifacts=[
            _ci_expected_artifact(
                artifact_ref,
                artifact_id=9901,
                boundary="aggregate-evidence",
                job="aggregate-evidence",
            )
        ],
        workflow="CI Validation",
        run_id="25887422010",
        run_attempt="1",
    )

    assert diagnostics == [], artifact_kind


@pytest.mark.parametrize(
    ("artifact_ref", "artifacts", "expected_detail"),
    [
        (
            control.ci_validation_aggregate_evidence_manifest_artifact_ref(
                run_id="25887422010",
                run_attempt="1",
            ),
            [],
            "aggregate-evidence-manifest-missing",
        ),
        (
            control.ci_validation_aggregate_evidence_manifest_artifact_ref(
                run_id="25887422010",
                run_attempt="1",
            ),
            [9901, 9902],
            "aggregate-evidence-manifest-duplicate",
        ),
        (
            control.ci_validation_aggregate_evidence_manifest_artifact_ref(
                run_id="25887422010",
                run_attempt="1",
            ),
            [9902],
            "final-producer-unverified",
        ),
        (
            control.ci_validation_aggregate_summary_artifact_ref(
                run_id="25887422010",
                run_attempt="1",
            ),
            [],
            "final-namespace-closure-mismatch",
        ),
        (
            control.ci_validation_aggregate_summary_artifact_ref(
                run_id="25887422010",
                run_attempt="1",
            ),
            [9901, 9902],
            "final-namespace-closure-mismatch",
        ),
        (
            control.ci_validation_aggregate_summary_artifact_ref(
                run_id="25887422010",
                run_attempt="1",
            ),
            [9902],
            "final-namespace-closure-mismatch",
        ),
    ],
)
def test_ci_validation_producer_boundary_fails_closed_for_current_final_refs(
    artifact_ref: str,
    artifacts: list[int],
    expected_detail: str,
) -> None:
    """Current final aggregate refs map to their authority diagnostics."""
    diagnostics = control._ci_verify_expected_artifact_producer_boundaries(
        artifacts=[
            _ci_artifact_metadata(artifact_ref, artifact_id=item)
            for item in artifacts
        ],
        expected_artifacts=[
            _ci_expected_artifact(
                artifact_ref,
                artifact_id=9901,
                boundary="aggregate-evidence",
                job="aggregate-evidence",
            )
        ],
        workflow="CI Validation",
        run_id="25887422010",
        run_attempt="1",
    )

    assert diagnostics
    expected_code = (
        "final-producer-unverified"
        if expected_detail == "final-producer-unverified"
        else "workflow-gate-failure"
        if expected_detail == "final-namespace-closure-mismatch"
        else "final-evidence-failure"
    )
    assert diagnostics[0]["code"] == expected_code
    assert diagnostics[0]["detail"] == expected_detail


def test_ci_validation_producer_boundary_ignores_expired_batch_manifest() -> (
    None
):
    """Expired execution-batch manifest instances do not duplicate live ones."""
    manifest_ref = control.ci_validation_execution_batch_manifest_artifact_ref(
        run_id="25887422010",
        run_attempt="1",
    )
    artifact_name = artifact_physical_name(manifest_ref)

    diagnostics = control._ci_verify_expected_artifact_producer_boundaries(
        artifacts=[
            {
                "id": 9000,
                "name": artifact_name,
                "created_at": "2026-05-14T21:09:21Z",
                "expired": True,
                "workflow_run": {"id": 25887422010},
            },
            {
                "id": 9001,
                "name": artifact_name,
                "created_at": "2026-05-14T21:09:21Z",
                "expired": False,
                "workflow_run": {"id": 25887422010},
            },
        ],
        expected_artifacts=[
            _ci_expected_artifact(
                manifest_ref,
                artifact_id=9001,
                boundary="materialize-execution-batches",
                job="materialize-execution-batches",
            )
        ],
        workflow="CI Validation",
        run_id="25887422010",
        run_attempt="1",
    )

    assert diagnostics == []


def test_ci_validation_boundary_fails_closed_for_expired_batch_manifest() -> (
    None
):
    """Expired-only execution-batch manifest instances remain inadmissible."""
    manifest_ref = control.ci_validation_execution_batch_manifest_artifact_ref(
        run_id="25887422010",
        run_attempt="1",
    )
    artifact_name = artifact_physical_name(manifest_ref)

    diagnostics = control._ci_verify_expected_artifact_producer_boundaries(
        artifacts=[
            {
                "id": 9001,
                "name": artifact_name,
                "created_at": "2026-05-14T21:09:21Z",
                "expired": True,
                "workflow_run": {"id": 25887422010},
            }
        ],
        expected_artifacts=[
            _ci_expected_artifact(
                manifest_ref,
                artifact_id=9001,
                boundary="materialize-execution-batches",
                job="materialize-execution-batches",
            )
        ],
        workflow="CI Validation",
        run_id="25887422010",
        run_attempt="1",
    )

    assert diagnostics
    assert diagnostics[0]["detail"] == "execution-batch-manifest-missing"


@pytest.mark.parametrize(
    ("artifacts", "artifact_id", "expected_detail"),
    [
        ([], 9001, "execution-batch-manifest-missing"),
        ([9001, 9002], 9001, "execution-batch-manifest-duplicate"),
        ([9002], 9001, "execution-batch-manifest-malformed"),
    ],
)
def test_ci_validation_boundary_fails_closed_for_bad_batch_manifest_ids(
    artifacts: list[int],
    artifact_id: int,
    expected_detail: str,
) -> None:
    """Execution-batch manifest producer-boundary failures stay G5-specific."""
    manifest_ref = control.ci_validation_execution_batch_manifest_artifact_ref(
        run_id="25887422010",
        run_attempt="1",
    )

    diagnostics = control._ci_verify_expected_artifact_producer_boundaries(
        artifacts=[
            _ci_artifact_metadata(manifest_ref, artifact_id=item)
            for item in artifacts
        ],
        expected_artifacts=[
            _ci_expected_artifact(
                manifest_ref,
                artifact_id=artifact_id,
                boundary="materialize-execution-batches",
                job="materialize-execution-batches",
            )
        ],
        workflow="CI Validation",
        run_id="25887422010",
        run_attempt="1",
    )

    assert diagnostics
    expected_code = (
        "final-producer-unverified"
        if expected_detail == "final-producer-unverified"
        else "inadmissible-batch-evidence"
    )
    assert diagnostics[0]["code"] == expected_code
    assert diagnostics[0]["detail"] == expected_detail


@pytest.mark.parametrize(
    ("artifacts", "artifact_id", "expected_detail"),
    [
        ([], 7001, "plan-missing"),
        (
            [
                7001,
                7002,
            ],
            7001,
            "plan-duplicate",
        ),
        ([7002], 7001, "plan-producer-unverified"),
    ],
)
def test_ci_validation_producer_boundary_fails_closed_for_bad_instances(
    artifacts: list[int],
    artifact_id: int,
    expected_detail: str,
) -> None:
    """Missing, duplicate, or mismatched control artifacts fail closed."""
    plan_ref = control.ci_validation_plan_artifact_ref(
        run_id="25887422010",
        run_attempt="1",
    )

    diagnostics = control._ci_verify_expected_artifact_producer_boundaries(
        artifacts=[
            _ci_artifact_metadata(plan_ref, artifact_id=item)
            for item in artifacts
        ],
        expected_artifacts=[
            _ci_expected_artifact(
                plan_ref,
                artifact_id=artifact_id,
                boundary="plan",
                job="plan",
            )
        ],
        workflow="CI Validation",
        run_id="25887422010",
        run_attempt="1",
    )

    assert diagnostics
    assert diagnostics[0]["detail"] == expected_detail


def test_ci_validation_producer_boundary_rejects_wrong_boundary_job() -> None:
    """Artifact names and payload refs do not override producer authority."""
    request_ref = control.ci_validation_request_artifact_ref(
        run_id="25887422010",
        run_attempt="1",
    )

    diagnostics = control._ci_verify_expected_artifact_producer_boundaries(
        artifacts=[_ci_artifact_metadata(request_ref, artifact_id=7001)],
        expected_artifacts=[
            _ci_expected_artifact(
                request_ref,
                artifact_id=7001,
                boundary="plan",
                job="plan",
            )
        ],
        workflow="CI Validation",
        run_id="25887422010",
        run_attempt="1",
    )

    assert diagnostics
    assert diagnostics[0]["detail"] == "request-producer-unverified"


@pytest.mark.parametrize(
    ("artifact_ref", "case_id"),
    [
        (
            control.ci_validation_changed_files_snapshot_artifact_ref(
                run_id="25887422010",
                run_attempt="1",
            ),
            "scheduled-full-or-unavailable-changed-files",
        ),
        (
            control.ci_validation_fact_snapshot_artifact_ref(
                run_id="25887422010",
                run_attempt="1",
            ),
            "no-fact-snapshot",
        ),
    ],
)
def test_ci_validation_producer_boundary_omits_empty_optional_snapshot_ids(
    artifact_ref: str,
    case_id: str,
) -> None:
    """Empty optional snapshot upload outputs do not become requirements."""
    diagnostics = control._ci_verify_expected_artifact_producer_boundaries(
        artifacts=[],
        expected_artifacts=[
            _ci_expected_artifact(
                artifact_ref,
                artifact_id="",
                boundary="plan",
                job="plan",
            )
        ],
        workflow="CI Validation",
        run_id="25887422010",
        run_attempt="1",
    )

    assert diagnostics == [], case_id


def test_ci_validation_aggregate_expected_inputs_skip_empty_optional_ids() -> (
    None
):
    """Aggregate producer-boundary inputs only include uploaded snapshots."""
    expected = control._ci_expected_aggregate_input_artifacts(
        argparse.Namespace(
            run_id="25887422010",
            run_attempt="1",
            expected_request_artifact_id="6001",
            expected_plan_artifact_id="7001",
            expected_changed_files_snapshot_artifact_id="",
            expected_fact_snapshot_artifact_id="",
            expected_execution_batch_manifest_artifact_id="9001",
        )
    )

    refs = {item["artifact-ref"] for item in expected}
    assert (
        control.ci_validation_changed_files_snapshot_artifact_ref(
            run_id="25887422010",
            run_attempt="1",
        )
        not in refs
    )
    assert (
        control.ci_validation_fact_snapshot_artifact_ref(
            run_id="25887422010",
            run_attempt="1",
        )
        not in refs
    )
    assert len(expected) == 3
    assert any(
        item["artifact-ref"]
        == control.ci_validation_execution_batch_manifest_artifact_ref(
            run_id="25887422010",
            run_attempt="1",
        )
        and item["artifact-instance-id"] == "9001"
        and item["producer-boundary"] == "materialize-execution-batches"
        and item["producer-job"] == "materialize-execution-batches"
        for item in expected
    )


@pytest.mark.parametrize(
    ("artifact_ref", "artifacts", "artifact_id", "expected_detail"),
    [
        (
            control.ci_validation_changed_files_snapshot_artifact_ref(
                run_id="25887422010",
                run_attempt="1",
            ),
            [7001, 7002],
            7001,
            "changed-files-snapshot-duplicate",
        ),
        (
            control.ci_validation_fact_snapshot_artifact_ref(
                run_id="25887422010",
                run_attempt="1",
            ),
            [7002],
            7001,
            "fact-snapshot-producer-unverified",
        ),
    ],
)
def test_ci_validation_producer_boundary_fails_closed_for_present_optional_ids(
    artifact_ref: str,
    artifacts: list[int],
    artifact_id: int,
    expected_detail: str,
) -> None:
    """Uploaded optional snapshots still get fail-closed boundary checks."""
    diagnostics = control._ci_verify_expected_artifact_producer_boundaries(
        artifacts=[
            _ci_artifact_metadata(artifact_ref, artifact_id=item)
            for item in artifacts
        ],
        expected_artifacts=[
            _ci_expected_artifact(
                artifact_ref,
                artifact_id=artifact_id,
                boundary="plan",
                job="plan",
            )
        ],
        workflow="CI Validation",
        run_id="25887422010",
        run_attempt="1",
    )

    assert diagnostics
    assert diagnostics[0]["detail"] == expected_detail


def test_github_actions_run_artifacts_dedupes_paginated_artifact_ids(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Running workflows can repeat the same artifact across pages."""
    artifact_ref = control.ci_validation_plan_artifact_ref(
        run_id="25887422010",
        run_attempt="1",
    )
    artifact_name = control.artifact_physical_name(artifact_ref)

    monkeypatch.setattr(
        control,
        "_gh_api_paginated",
        lambda *_args, **_kwargs: [
            {
                "artifacts": [
                    {"id": 7001, "name": artifact_name},
                    {"id": 7002, "name": artifact_name},
                ],
            },
            {
                "artifacts": [
                    {"id": 7001, "name": artifact_name},
                ],
            },
        ],
    )

    artifacts = control._github_actions_run_artifacts(
        repository="hcoona/three",
        run_id="25887422010",
    )

    assert [artifact["id"] for artifact in artifacts] == [7001, 7002]


def test_github_actions_run_artifacts_bounded_prefix_stops_at_sentinel(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Bounded CI namespace enumeration stops at cap plus one sentinel."""
    cap = control._CI_VALIDATION_LIVE_NAMESPACE_ARTIFACT_CAP
    api_calls: list[str] = []

    def fake_gh_api(_repository: str, endpoint: str, **_kwargs) -> object:
        api_calls.append(endpoint)
        return {
            "artifacts": [
                {
                    "id": 8000 + index,
                    "name": (f"three-ci-validation-25887422010-1-{index:064x}"),
                }
                for index in range(cap + 7)
            ]
        }

    monkeypatch.setattr(control, "_gh_api", fake_gh_api)
    monkeypatch.setattr(
        control,
        "_gh_api_paginated",
        lambda *_args, **_kwargs: pytest.fail(
            "bounded namespace enumeration must not use --paginate --slurp"
        ),
    )

    artifacts = control._github_actions_run_artifacts(
        repository="hcoona/three",
        run_id="25887422010",
        run_attempt="1",
        prefixed_artifact_cap=cap,
    )

    assert len(artifacts) == cap + 1
    assert [artifact["id"] for artifact in artifacts] == list(
        range(8000, 8000 + cap + 1)
    )
    assert api_calls == [
        "repos/hcoona/three/actions/runs/25887422010/artifacts?per_page=100&page=1"
    ]


def test_github_actions_run_artifacts_bounded_prefix_excludes_final_refs(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Pre-final bounded enumeration does not count current final refs."""
    cap = control._CI_VALIDATION_PRE_FINAL_NAMESPACE_ARTIFACT_CAP
    run_id = "25887422010"
    run_attempt = "1"
    final_names = control._ci_current_final_aggregate_artifact_names(
        run_id=run_id,
        run_attempt=run_attempt,
    )

    def fake_gh_api(_repository: str, _endpoint: str, **_kwargs) -> object:
        return {
            "artifacts": [
                {"id": 7901 + index, "name": name}
                for index, name in enumerate(sorted(final_names))
            ]
            + [
                {
                    "id": 8000 + index,
                    "name": (f"three-ci-validation-25887422010-1-{index:064x}"),
                }
                for index in range(cap)
            ]
        }

    monkeypatch.setattr(control, "_gh_api", fake_gh_api)
    monkeypatch.setattr(
        control,
        "_gh_api_paginated",
        lambda *_args, **_kwargs: pytest.fail(
            "bounded namespace enumeration must not use --paginate --slurp"
        ),
    )

    artifacts = control._github_actions_run_artifacts(
        repository="hcoona/three",
        run_id=run_id,
        run_attempt=run_attempt,
        prefixed_artifact_cap=cap,
        excluded_prefixed_artifact_names=final_names,
    )

    assert len(artifacts) == cap + len(final_names)
    assert (
        control._ci_prefixed_artifact_count(
            artifacts,
            run_id=run_id,
            run_attempt=run_attempt,
            excluded_prefixed_artifact_names=final_names,
        )
        == cap
    )


def test_github_actions_run_artifacts_bounded_prefix_fails_closed_at_item_bound(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Unrelated artifacts cannot force unbounded namespace scans."""
    cap = control._CI_VALIDATION_LIVE_NAMESPACE_ARTIFACT_CAP
    api_calls: list[str] = []

    def fake_gh_api(_repository: str, endpoint: str, **_kwargs) -> object:
        api_calls.append(endpoint)
        return {
            "artifacts": [
                {"id": 9000 + index, "name": f"unrelated-artifact-{index:03d}"}
                for index in range(100)
            ]
        }

    monkeypatch.setattr(control, "_gh_api", fake_gh_api)
    monkeypatch.setattr(
        control,
        "_gh_api_paginated",
        lambda *_args, **_kwargs: pytest.fail(
            "bounded namespace enumeration must not use --paginate --slurp"
        ),
    )

    with pytest.raises(RuntimeError, match="enumeration unavailable"):
        control._github_actions_run_artifacts(
            repository="hcoona/three",
            run_id="25887422010",
            run_attempt="1",
            prefixed_artifact_cap=cap,
        )

    assert len(api_calls) == (
        control._CI_VALIDATION_LIVE_NAMESPACE_ENUMERATION_PAGE_CAP
    )


def test_verify_ci_validation_artifact_boundaries_uses_bounded_enumeration(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Final boundary verification does not enumerate the full run namespace."""
    plan_ref = control.ci_validation_plan_artifact_ref(
        run_id="25887422010",
        run_attempt="1",
    )
    calls: list[dict[str, object]] = []

    def fake_run_artifacts(
        **kwargs: object,
    ) -> list[GitHubActionsArtifactMetadata]:
        calls.append(dict(kwargs))
        return [_ci_artifact_metadata(plan_ref, artifact_id=7001)]

    monkeypatch.setattr(
        control, "_github_actions_run_artifacts", fake_run_artifacts
    )

    result = control._cmd_verify_ci_validation_artifact_boundaries(
        argparse.Namespace(
            repository="hcoona/three",
            workflow="CI Validation",
            run_id="25887422010",
            run_attempt="1",
            expected_artifact=[
                json.dumps(
                    _ci_expected_artifact(
                        plan_ref,
                        artifact_id=7001,
                        boundary="plan",
                        job="plan",
                    ),
                    separators=(",", ":"),
                )
            ],
        )
    )

    assert result == 0
    assert calls == [
        {
            "repository": "hcoona/three",
            "run_id": "25887422010",
            "run_attempt": "1",
            "prefixed_artifact_cap": (
                control._CI_VALIDATION_LIVE_NAMESPACE_ARTIFACT_CAP
            ),
            "excluded_prefixed_artifact_names": (
                control._ci_current_final_aggregate_artifact_names(
                    run_id="25887422010",
                    run_attempt="1",
                )
            ),
        }
    ]


def test_verify_ci_validation_artifact_boundaries_fails_on_bounded_overflow(
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
) -> None:
    """The bounded sentinel fails namespace-overflow verification."""
    plan_ref = control.ci_validation_plan_artifact_ref(
        run_id="25887422010",
        run_attempt="1",
    )
    cap = control._CI_VALIDATION_LIVE_NAMESPACE_ARTIFACT_CAP
    monkeypatch.setattr(
        control,
        "_github_actions_run_artifacts",
        lambda **_kwargs: [
            _ci_artifact_metadata(plan_ref, artifact_id=7001 + index)
            for index in range(cap + 1)
        ],
    )

    result = control._cmd_verify_ci_validation_artifact_boundaries(
        argparse.Namespace(
            repository="hcoona/three",
            workflow="CI Validation",
            run_id="25887422010",
            run_attempt="1",
            expected_artifact=[
                json.dumps(
                    _ci_expected_artifact(
                        plan_ref,
                        artifact_id=7001,
                        boundary="plan",
                        job="plan",
                    ),
                    separators=(",", ":"),
                )
            ],
        )
    )

    assert result == 1
    assert "namespace overflowed" in capsys.readouterr().err


def test_verify_ci_validation_artifact_boundaries_excludes_only_manifest(
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
) -> None:
    """Pre-summary manifest verification does not exempt early summaries."""
    run_id = "25887422010"
    run_attempt = "1"
    aggregate_manifest_ref = (
        control.ci_validation_aggregate_evidence_manifest_artifact_ref(
            run_id=run_id,
            run_attempt=run_attempt,
        )
    )
    aggregate_summary_ref = (
        control.ci_validation_aggregate_summary_artifact_ref(
            run_id=run_id,
            run_attempt=run_attempt,
        )
    )
    artifacts: list[GitHubActionsArtifactMetadata] = [
        GitHubActionsArtifactMetadata(
            artifact_id=8300 + index,
            name=f"three-ci-validation-{run_id}-{run_attempt}-{index:064x}",
            created_at="2026-05-14T21:09:21Z",
            expired=False,
            workflow_run_id=int(run_id),
        )
        for index in range(control._CI_VALIDATION_LIVE_NAMESPACE_ARTIFACT_CAP)
    ]
    artifacts.extend(
        [
            _ci_artifact_metadata(aggregate_manifest_ref, artifact_id=9901),
            _ci_artifact_metadata(aggregate_summary_ref, artifact_id=9902),
        ]
    )
    calls: list[dict[str, object]] = []

    def fake_run_artifacts(
        **kwargs: object,
    ) -> list[GitHubActionsArtifactMetadata]:
        calls.append(dict(kwargs))
        return artifacts

    monkeypatch.setattr(
        control, "_github_actions_run_artifacts", fake_run_artifacts
    )

    result = control._cmd_verify_ci_validation_artifact_boundaries(
        argparse.Namespace(
            repository="hcoona/three",
            workflow="CI Validation",
            run_id=run_id,
            run_attempt=run_attempt,
            max_prefixed_validation_artifacts=(
                control._CI_VALIDATION_PRE_FINAL_NAMESPACE_ARTIFACT_CAP
            ),
            expected_artifact=[
                json.dumps(
                    _ci_expected_artifact(
                        aggregate_manifest_ref,
                        artifact_id=9901,
                        boundary="aggregate-evidence",
                        job="aggregate-evidence",
                    ),
                    separators=(",", ":"),
                ),
            ],
        )
    )

    assert result == 1
    assert "namespace overflowed" in capsys.readouterr().err
    assert calls == [
        {
            "repository": "hcoona/three",
            "run_id": run_id,
            "run_attempt": run_attempt,
            "prefixed_artifact_cap": (
                control._CI_VALIDATION_PRE_FINAL_NAMESPACE_ARTIFACT_CAP
            ),
            "excluded_prefixed_artifact_names": {
                artifact_physical_name(aggregate_manifest_ref)
            },
        }
    ]


def test_verify_ci_validation_artifact_boundaries_admits_excluded_manifest(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Excluded manifest artifacts remain visible for producer admission."""
    run_id = "25887422010"
    run_attempt = "1"
    aggregate_manifest_ref = (
        control.ci_validation_aggregate_evidence_manifest_artifact_ref(
            run_id=run_id,
            run_attempt=run_attempt,
        )
    )
    artifacts: list[GitHubActionsArtifactMetadata] = [
        GitHubActionsArtifactMetadata(
            artifact_id=8400 + index,
            name=f"three-ci-validation-{run_id}-{run_attempt}-{index:064x}",
            created_at="2026-05-14T21:09:21Z",
            expired=False,
            workflow_run_id=int(run_id),
        )
        for index in range(control._CI_VALIDATION_LIVE_NAMESPACE_ARTIFACT_CAP)
    ]
    artifacts.append(
        _ci_artifact_metadata(aggregate_manifest_ref, artifact_id=9901)
    )
    monkeypatch.setattr(
        control,
        "_github_actions_run_artifacts",
        lambda **_kwargs: artifacts,
    )

    result = control._cmd_verify_ci_validation_artifact_boundaries(
        argparse.Namespace(
            repository="hcoona/three",
            workflow="CI Validation",
            run_id=run_id,
            run_attempt=run_attempt,
            max_prefixed_validation_artifacts=(
                control._CI_VALIDATION_PRE_FINAL_NAMESPACE_ARTIFACT_CAP
            ),
            expected_artifact=[
                json.dumps(
                    _ci_expected_artifact(
                        aggregate_manifest_ref,
                        artifact_id=9901,
                        boundary="aggregate-evidence",
                        job="aggregate-evidence",
                    ),
                    separators=(",", ":"),
                ),
            ],
        )
    )

    assert result == 0


def test_verify_ci_validation_artifact_boundaries_allows_final_total_cap(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Final aggregate checks allow the 18 pre-final plus 2 final topology."""
    run_id = "25887422010"
    run_attempt = "1"
    aggregate_manifest_ref = (
        control.ci_validation_aggregate_evidence_manifest_artifact_ref(
            run_id=run_id,
            run_attempt=run_attempt,
        )
    )
    aggregate_summary_ref = (
        control.ci_validation_aggregate_summary_artifact_ref(
            run_id=run_id,
            run_attempt=run_attempt,
        )
    )
    artifacts: list[GitHubActionsArtifactMetadata] = [
        GitHubActionsArtifactMetadata(
            artifact_id=8000 + index,
            name=f"three-ci-validation-{run_id}-{run_attempt}-{index:064x}",
            created_at="2026-05-14T21:09:21Z",
            expired=False,
            workflow_run_id=int(run_id),
        )
        for index in range(control._CI_VALIDATION_LIVE_NAMESPACE_ARTIFACT_CAP)
    ]
    artifacts.extend(
        [
            _ci_artifact_metadata(aggregate_manifest_ref, artifact_id=9901),
            _ci_artifact_metadata(aggregate_summary_ref, artifact_id=9902),
        ]
    )
    calls: list[dict[str, object]] = []

    def fake_run_artifacts(
        **kwargs: object,
    ) -> list[GitHubActionsArtifactMetadata]:
        calls.append(dict(kwargs))
        return artifacts

    monkeypatch.setattr(
        control, "_github_actions_run_artifacts", fake_run_artifacts
    )

    result = control._cmd_verify_ci_validation_artifact_boundaries(
        argparse.Namespace(
            repository="hcoona/three",
            workflow="CI Validation",
            run_id=run_id,
            run_attempt=run_attempt,
            max_prefixed_validation_artifacts=(
                control._CI_VALIDATION_TOTAL_NAMESPACE_ARTIFACT_CAP
            ),
            expected_prefixed_validation_artifacts=(
                control._CI_VALIDATION_TOTAL_NAMESPACE_ARTIFACT_CAP
            ),
            expected_artifact=[
                json.dumps(
                    _ci_expected_artifact(
                        aggregate_manifest_ref,
                        artifact_id=9901,
                        boundary="aggregate-evidence",
                        job="aggregate-evidence",
                    ),
                    separators=(",", ":"),
                ),
                json.dumps(
                    _ci_expected_artifact(
                        aggregate_summary_ref,
                        artifact_id=9902,
                        boundary="aggregate-evidence",
                        job="aggregate-evidence",
                    ),
                    separators=(",", ":"),
                ),
            ],
        )
    )

    assert result == 0
    assert calls == [
        {
            "repository": "hcoona/three",
            "run_id": run_id,
            "run_attempt": run_attempt,
            "prefixed_artifact_cap": (
                control._CI_VALIDATION_TOTAL_NAMESPACE_ARTIFACT_CAP
            ),
            "excluded_prefixed_artifact_names": set(),
        }
    ]


def test_verify_final_uploaded_bytes_rejects_noncanonical_summary(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Final summary self-artifact byte failures stay workflow-gate-only."""
    run_id = "25887422010"
    run_attempt = "1"
    aggregate_summary_ref = (
        control.ci_validation_aggregate_summary_artifact_ref(
            run_id=run_id,
            run_attempt=run_attempt,
        )
    )
    scratch = SCRATCH / "final-uploaded-noncanonical"
    shutil.rmtree(scratch, ignore_errors=True)
    try:
        scratch.mkdir(parents=True)
        summary_path = scratch / "aggregate-summary.json"
        summary_path.write_text('{"b": 1, "a": 2}', encoding="utf-8")
        monkeypatch.setattr(
            control,
            "_github_actions_run_artifacts",
            lambda **_kwargs: [
                _ci_artifact_metadata(aggregate_summary_ref, artifact_id=9902)
            ],
        )
        expected = _ci_expected_artifact(
            aggregate_summary_ref,
            artifact_id=9902,
            boundary="aggregate-evidence",
            job="aggregate-evidence",
        )
        expected["downloaded-path"] = str(summary_path)

        result = control._cmd_verify_ci_validation_artifact_boundaries(
            argparse.Namespace(
                repository="hcoona/three",
                workflow="CI Validation",
                run_id=run_id,
                run_attempt=run_attempt,
                max_prefixed_validation_artifacts=(
                    control._CI_VALIDATION_TOTAL_NAMESPACE_ARTIFACT_CAP
                ),
                expected_prefixed_validation_artifacts=1,
                expected_artifact=[
                    json.dumps(expected, separators=(",", ":")),
                ],
            )
        )

        assert result == 1
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_final_uploaded_byte_gate_recomputes_manifest_digest(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Summary manifest digest claims must match uploaded manifest bytes."""
    run_id = "25887422010"
    run_attempt = "1"
    aggregate_manifest_ref = (
        control.ci_validation_aggregate_evidence_manifest_artifact_ref(
            run_id=run_id,
            run_attempt=run_attempt,
        )
    )
    aggregate_summary_ref = (
        control.ci_validation_aggregate_summary_artifact_ref(
            run_id=run_id,
            run_attempt=run_attempt,
        )
    )
    scratch = SCRATCH / "final-uploaded-digest"
    shutil.rmtree(scratch, ignore_errors=True)
    try:
        scratch.mkdir(parents=True)
        manifest_path = scratch / "aggregate-evidence-manifest.json"
        summary_path = scratch / "aggregate-summary.json"
        manifest = {"artifact-ref": aggregate_manifest_ref, "value": "uploaded"}
        wrong_digest = "0" * 64
        summary = {
            "aggregate-evidence-manifest": {
                "artifact-ref": aggregate_manifest_ref,
                "artifact-instance-id": "9901",
                "content-digest": wrong_digest,
            },
            "final-artifacts": {
                "aggregate-evidence-manifest": {
                    "artifact-ref": aggregate_manifest_ref,
                    "artifact-instance-id": "9901",
                    "content-digest": wrong_digest,
                }
            },
        }
        manifest_path.write_bytes(canonical_json_bytes(manifest))
        summary_path.write_bytes(canonical_json_bytes(summary))
        monkeypatch.setattr(
            control,
            "validate_ci_validation_aggregate_evidence_manifest",
            lambda *_, **__: None,
        )
        monkeypatch.setattr(
            control,
            "validate_ci_validation_aggregate_summary",
            lambda *_, **__: None,
        )
        expected_manifest = _ci_expected_artifact(
            aggregate_manifest_ref,
            artifact_id=9901,
            boundary="aggregate-evidence",
            job="aggregate-evidence",
        )
        expected_manifest["downloaded-path"] = str(manifest_path)
        expected_summary = _ci_expected_artifact(
            aggregate_summary_ref,
            artifact_id=9902,
            boundary="aggregate-evidence",
            job="aggregate-evidence",
        )
        expected_summary["downloaded-path"] = str(summary_path)

        diagnostics = control._ci_verify_expected_final_artifact_uploaded_bytes(
            [expected_manifest, expected_summary],
            run_id=run_id,
            run_attempt=run_attempt,
        )

        assert diagnostics
        assert diagnostics[0]["code"] == "workflow-gate-failure"
        assert diagnostics[0]["detail"] == "final-namespace-closure-mismatch"
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_verify_ci_validation_artifact_boundaries_rejects_extra_under_cap(
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
) -> None:
    """Final verification rejects unexpected prefixed artifacts."""
    run_id = "25887422010"
    run_attempt = "1"
    aggregate_manifest_ref = (
        control.ci_validation_aggregate_evidence_manifest_artifact_ref(
            run_id=run_id,
            run_attempt=run_attempt,
        )
    )
    aggregate_summary_ref = (
        control.ci_validation_aggregate_summary_artifact_ref(
            run_id=run_id,
            run_attempt=run_attempt,
        )
    )
    artifacts: list[GitHubActionsArtifactMetadata] = [
        GitHubActionsArtifactMetadata(
            artifact_id=8100 + index,
            name=f"three-ci-validation-{run_id}-{run_attempt}-{index:064x}",
            created_at="2026-05-14T21:09:21Z",
            expired=False,
            workflow_run_id=int(run_id),
        )
        for index in range(17)
    ]
    artifacts.extend(
        [
            _ci_artifact_metadata(aggregate_manifest_ref, artifact_id=9901),
            _ci_artifact_metadata(aggregate_summary_ref, artifact_id=9902),
            GitHubActionsArtifactMetadata(
                artifact_id=9999,
                name=f"three-ci-validation-{run_id}-{run_attempt}-" + "a" * 64,
                created_at="2026-05-14T21:09:21Z",
                expired=False,
                workflow_run_id=int(run_id),
            ),
        ]
    )
    monkeypatch.setattr(
        control,
        "_github_actions_run_artifacts",
        lambda **_kwargs: artifacts,
    )

    result = control._cmd_verify_ci_validation_artifact_boundaries(
        argparse.Namespace(
            repository="hcoona/three",
            workflow="CI Validation",
            run_id=run_id,
            run_attempt=run_attempt,
            max_prefixed_validation_artifacts=(
                control._CI_VALIDATION_TOTAL_NAMESPACE_ARTIFACT_CAP
            ),
            expected_prefixed_validation_artifacts=19,
            expected_artifact=[
                json.dumps(
                    _ci_expected_artifact(
                        aggregate_manifest_ref,
                        artifact_id=9901,
                        boundary="aggregate-evidence",
                        job="aggregate-evidence",
                    ),
                    separators=(",", ":"),
                ),
                json.dumps(
                    _ci_expected_artifact(
                        aggregate_summary_ref,
                        artifact_id=9902,
                        boundary="aggregate-evidence",
                        job="aggregate-evidence",
                    ),
                    separators=(",", ":"),
                ),
            ],
        )
    )

    assert result == 1
    assert "does not match the expected final count" in capsys.readouterr().err


@pytest.mark.parametrize("aggregate_phase", ["evidence", "summary"])
def test_ci_aggregate_control_boundary_excludes_current_final_refs(
    monkeypatch: pytest.MonkeyPatch,
    aggregate_phase: str,
) -> None:
    """Pre-final boundary checks do not count current final aggregate refs."""
    run_id = "25887422010"
    run_attempt = "1"
    request_ref = control.ci_validation_request_artifact_ref(
        run_id=run_id,
        run_attempt=run_attempt,
    )
    aggregate_manifest_ref = (
        control.ci_validation_aggregate_evidence_manifest_artifact_ref(
            run_id=run_id,
            run_attempt=run_attempt,
        )
    )
    aggregate_summary_ref = (
        control.ci_validation_aggregate_summary_artifact_ref(
            run_id=run_id,
            run_attempt=run_attempt,
        )
    )
    artifacts: list[GitHubActionsArtifactMetadata] = [
        _ci_artifact_metadata(request_ref, artifact_id=7001),
        _ci_artifact_metadata(aggregate_manifest_ref, artifact_id=7901),
        _ci_artifact_metadata(aggregate_summary_ref, artifact_id=7902),
        *[
            GitHubActionsArtifactMetadata(
                artifact_id=8000 + index,
                name=f"three-ci-validation-{run_id}-{run_attempt}-{index:064x}",
                created_at="2026-05-14T21:09:21Z",
                expired=False,
                workflow_run_id=int(run_id),
            )
            for index in range(
                control._CI_VALIDATION_PRE_FINAL_NAMESPACE_ARTIFACT_CAP - 1
            )
        ],
    ]
    calls: list[dict[str, object]] = []

    def fake_run_artifacts(
        **kwargs: object,
    ) -> list[GitHubActionsArtifactMetadata]:
        calls.append(dict(kwargs))
        return artifacts

    monkeypatch.setattr(
        control, "_github_actions_run_artifacts", fake_run_artifacts
    )

    diagnostics = control._ci_aggregate_control_artifact_boundary_diagnostics(
        argparse.Namespace(
            repository="hcoona/three",
            workflow="CI Validation",
            run_id=run_id,
            run_attempt=run_attempt,
            aggregate_phase=aggregate_phase,
            expected_request_artifact_id="7001",
            expected_plan_artifact_id=None,
            expected_changed_files_snapshot_artifact_id=None,
            expected_fact_snapshot_artifact_id=None,
            expected_execution_batch_manifest_artifact_id=None,
        )
    )

    excluded_names = (
        control._ci_current_final_aggregate_artifact_names(
            run_id=run_id,
            run_attempt=run_attempt,
        )
        if aggregate_phase == "evidence"
        else {artifact_physical_name(aggregate_manifest_ref)}
    )
    expected_calls = [
        {
            "repository": "hcoona/three",
            "run_id": run_id,
            "run_attempt": run_attempt,
            "prefixed_artifact_cap": (
                control._CI_VALIDATION_PRE_FINAL_NAMESPACE_ARTIFACT_CAP
            ),
            "excluded_prefixed_artifact_names": excluded_names,
        }
    ]
    assert calls in ([], expected_calls)
    if aggregate_phase == "evidence":
        assert diagnostics == []
    else:
        assert diagnostics
        assert "namespace overflowed" in str(diagnostics[0]["message"])


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
            {
                "subject-id": "ruby-subject",
                "root": "src/public/lib/hcoona-release-smoke-rubygems",
            },
            {
                "subject-id": "python-subject",
                "root": "src/private/app/html-sm-processor",
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
    ruby_group = {
        "work-group-id": "wg-ruby",
        "kind": "ecosystem-gate",
        "coverage-target": {"type": "subject", "id": "ruby-subject"},
        "ecosystem": "ruby",
        "runner-family": "ubuntu",
        "depends-on": [],
        "expected-evidence": {
            "planned-capabilities": ["build"],
        },
    }
    python_group = {
        "work-group-id": "wg-python",
        "kind": "ecosystem-gate",
        "coverage-target": {"type": "subject", "id": "python-subject"},
        "ecosystem": "python",
        "runner-family": "ubuntu",
        "depends-on": [],
        "expected-evidence": {
            "planned-capabilities": ["lint", "type-check"],
        },
    }

    dotnet_commands = control._ci_validation_commands(plan, dotnet_group)
    fallback_commands = control._ci_validation_commands({}, dotnet_group)
    js_commands = control._ci_validation_commands(plan, js_group)
    release_commands = control._ci_validation_commands(plan, release_group)
    ruby_commands = control._ci_validation_commands(plan, ruby_group)
    python_commands = control._ci_validation_commands(plan, python_group)

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
    assert [
        "uv",
        "run",
        "ruff",
        "check",
        "--force-exclude",
        "src/private/app/html-sm-processor",
    ] in [command["argv"] for command in python_commands]
    assert [
        "uv",
        "run",
        "pyrefly",
        "check",
    ] in [command["argv"] for command in python_commands]
    assert all(
        command["argv"][-1] != "src/private/app/html-sm-processor"
        for command in python_commands
        if command["capability"] == "type-check"
    )
    assert any(
        command["capability"] == "build"
        and command["argv"] == ["dotnet", "tool", "restore"]
        for command in ruby_commands
    )
    assert any(
        command["capability"] == "build"
        and command["argv"][:2] == ["ruby", "-e"]
        and command["argv"][-2:]
        == [
            "src/public/lib/hcoona-release-smoke-rubygems",
            ".three-ci-validation/work/validation-build.gem",
        ]
        for command in ruby_commands
    )
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
        "validation-tree": {"commit-sha": "b" * 40},
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
        "validation-tree": {"commit-sha": "b" * 40},
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
        "coverage-target": {"type": "subject", "id": "python"},
        "observed-commit-sha": "b" * 40,
        "outcome": "success",
        "commands": [{"capability": "build", "outcome": "success"}],
    }

    assert (
        control._ci_validation_outcome(
            plan,
            "wg-python",
            dependency_blocked=False,
            validation_result=validation_result,
            observed_commit_sha="b" * 40,
        )
        == "success"
    )
    for field, value in (
        ("work-group-id", "wg-other"),
        ("kind", "descriptor-validation"),
        ("runner-family", "windows"),
        ("coverage-target", {"type": "subject", "id": "python.other"}),
        ("observed-commit-sha", "c" * 40),
    ):
        stale_result = {**validation_result, field: value}
        assert (
            control._ci_validation_outcome(
                plan,
                "wg-python",
                dependency_blocked=False,
                validation_result=stale_result,
                observed_commit_sha="b" * 40,
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
        assert "artifact-shape-unconfirmed" in command["error"]
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
        assert (
            evidence["category-result"]["diagnostics"][0]["code"]
            == "artifact-shape-unconfirmed"
        )
        batch_evidence = control._ci_validation_evidence(
            plan,
            "wg-release",
            outcome="blocking-failure",
            diagnostics=control._ci_validation_diagnostics(
                plan,
                "wg-release",
                outcome="blocking-failure",
            ),
            validation_result=validation_result,
            batch_bundle=True,
        )
        batch_detail = batch_evidence["category-result"]["detail"]
        obligation_result = batch_detail["artifact-obligation-results"][0]
        assert (
            obligation_result["artifact"]["observed"]["digests"][0][
                "digest-available"
            ]
            is False
        )

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


def test_ci_validation_release_shaped_no_publish_missing_mapping_blocks() -> (
    None
):
    """Missing explicit mapping blocks recursive output discovery."""
    scratch = SCRATCH / "ci-validation-release-shaped-output-ref-binding"
    shutil.rmtree(scratch, ignore_errors=True)
    scratch.mkdir(parents=True)
    try:
        artifact_refs = [
            "ci-validation/artifacts/python/example/a.whl",
            "ci-validation/artifacts/python/example/b.whl",
        ]
        plan = _release_shaped_no_publish_plan(artifact_refs)
        matrix = _release_shaped_no_publish_matrix(plan)
        build_root = scratch / ".three-ci-validation/work/validation-build"
        (build_root / "1").mkdir(parents=True)
        (build_root / "2").mkdir(parents=True)
        b_bytes = b"contents for b"
        a_bytes = b"contents for a"
        (build_root / "1/b.whl").write_bytes(b_bytes)
        (build_root / "2/a.whl").write_bytes(a_bytes)
        result = _run_release_shaped_no_publish_validation(
            scratch=scratch,
            plan=plan,
            matrix=matrix,
        )

        assert result["outcome"] == "blocking-failure"
        assert "artifact-shape-unconfirmed" in result["commands"][0]["error"]
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_validation_release_shaped_no_publish_uses_mapping() -> None:
    """Release-shaped no-publish accepts explicit ref-to-output mapping."""
    scratch = SCRATCH / "ci-validation-release-shaped-output-declared-mapping"
    shutil.rmtree(scratch, ignore_errors=True)
    scratch.mkdir(parents=True)
    try:
        artifact_refs = [
            "ci-validation/artifacts/python/example/a.whl",
            "ci-validation/artifacts/python/example/b.whl",
        ]
        plan = _release_shaped_no_publish_plan(artifact_refs)
        matrix = _release_shaped_no_publish_matrix(plan)
        build_root = scratch / ".three-ci-validation/work/validation-build"
        build_root.mkdir(parents=True)
        b_bytes = b"mapped contents for b"
        a_bytes = b"mapped contents for a"
        (build_root / "first-output.bin").write_bytes(b_bytes)
        (build_root / "second-output.bin").write_bytes(a_bytes)
        mapping_path = (
            scratch
            / ".three-ci-validation/work/validation-build-artifacts.json"
        )
        mapping_path.write_text(
            json.dumps(
                {
                    "artifacts": {
                        artifact_refs[0]: (
                            ".three-ci-validation/work/validation-build/"
                            "second-output.bin"
                        ),
                        artifact_refs[1]: (
                            ".three-ci-validation/work/validation-build/"
                            "first-output.bin"
                        ),
                    }
                },
            ),
            encoding="utf-8",
        )
        result = _run_release_shaped_no_publish_validation(
            scratch=scratch,
            plan=plan,
            matrix=matrix,
        )

        assert result["outcome"] == "success"
        digests = _release_shaped_result_digests(result)
        assert digests[artifact_refs[0]] == hashlib.sha256(a_bytes).hexdigest()
        assert digests[artifact_refs[1]] == hashlib.sha256(b_bytes).hexdigest()
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_validation_release_shaped_no_publish_malformed_mapping_blocks() -> (
    None
):
    """Malformed explicit mapping blocks instead of heuristic fallback."""
    scratch = SCRATCH / "ci-validation-release-shaped-malformed-mapping"
    shutil.rmtree(scratch, ignore_errors=True)
    scratch.mkdir(parents=True)
    try:
        artifact_refs = ["ci-validation/artifacts/python/example/a.whl"]
        plan = _release_shaped_no_publish_plan(artifact_refs)
        matrix = _release_shaped_no_publish_matrix(plan)
        build_root = scratch / ".three-ci-validation/work/validation-build"
        build_root.mkdir(parents=True)
        (build_root / "a.whl").write_bytes(b"fallback would match")
        mapping_path = (
            scratch
            / ".three-ci-validation/work/validation-build-artifacts.json"
        )
        mapping_path.write_text("{not-json", encoding="utf-8")

        result = _run_release_shaped_no_publish_validation(
            scratch=scratch,
            plan=plan,
            matrix=matrix,
        )

        assert result["outcome"] == "blocking-failure"
        assert "artifact-shape-unconfirmed" in result["commands"][0]["error"]
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_validation_release_shaped_no_publish_directory_mapping_blocks() -> (
    None
):
    """Non-regular explicit mapping blocks heuristic fallback."""
    scratch = SCRATCH / "ci-validation-release-shaped-directory-mapping"
    shutil.rmtree(scratch, ignore_errors=True)
    scratch.mkdir(parents=True)
    try:
        artifact_refs = ["ci-validation/artifacts/python/example/a.whl"]
        plan = _release_shaped_no_publish_plan(artifact_refs)
        matrix = _release_shaped_no_publish_matrix(plan)
        build_root = scratch / ".three-ci-validation/work/validation-build"
        build_root.mkdir(parents=True)
        (build_root / "a.whl").write_bytes(b"fallback would match")
        mapping_path = (
            scratch
            / ".three-ci-validation/work/validation-build-artifacts.json"
        )
        mapping_path.mkdir()

        result = _run_release_shaped_no_publish_validation(
            scratch=scratch,
            plan=plan,
            matrix=matrix,
        )

        assert result["outcome"] == "blocking-failure"
        assert "artifact-shape-unconfirmed" in result["commands"][0]["error"]
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_validation_release_shaped_no_publish_unreadable_mapping_blocks(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Unreadable explicit mapping blocks instead of heuristic fallback."""
    scratch = SCRATCH / "ci-validation-release-shaped-unreadable-mapping"
    shutil.rmtree(scratch, ignore_errors=True)
    scratch.mkdir(parents=True)
    original_read_text = Path.read_text
    try:
        artifact_refs = ["ci-validation/artifacts/python/example/a.whl"]
        plan = _release_shaped_no_publish_plan(artifact_refs)
        matrix = _release_shaped_no_publish_matrix(plan)
        build_root = scratch / ".three-ci-validation/work/validation-build"
        build_root.mkdir(parents=True)
        (build_root / "a.whl").write_bytes(b"fallback would match")
        mapping_path = (
            scratch
            / ".three-ci-validation/work/validation-build-artifacts.json"
        )
        mapping_path.write_text(
            json.dumps(
                {
                    "artifacts": {
                        artifact_refs[0]: (
                            ".three-ci-validation/work/validation-build/a.whl"
                        ),
                    },
                },
            ),
            encoding="utf-8",
        )

        def deny_mapping_read(
            path: Path,
            *args: object,
            **kwargs: object,
        ) -> str:
            if path == mapping_path:
                raise PermissionError
            return original_read_text(path, *args, **kwargs)

        monkeypatch.setattr(Path, "read_text", deny_mapping_read)
        result = _run_release_shaped_no_publish_validation(
            scratch=scratch,
            plan=plan,
            matrix=matrix,
        )

        assert result["outcome"] == "blocking-failure"
        assert "artifact-shape-unconfirmed" in result["commands"][0]["error"]
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_validation_release_shaped_no_publish_mapping_skips_enumeration(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Valid mapping uses only declared outputs instead of recursive scans."""
    scratch = SCRATCH / "ci-validation-release-shaped-mapping-no-enumeration"
    shutil.rmtree(scratch, ignore_errors=True)
    scratch.mkdir(parents=True)
    try:
        artifact_refs = ["ci-validation/artifacts/python/example/a.whl"]
        plan = _release_shaped_no_publish_plan(artifact_refs)
        matrix = _release_shaped_no_publish_matrix(plan)
        build_root = scratch / ".three-ci-validation/work/validation-build"
        build_root.mkdir(parents=True)
        output_bytes = b"declared output"
        (build_root / "mapped-output.bin").write_bytes(output_bytes)
        (build_root / "undeclared-output.bin").write_bytes(b"not evidence")
        mapping_path = (
            scratch
            / ".three-ci-validation/work/validation-build-artifacts.json"
        )
        mapping_path.write_text(
            json.dumps(
                {
                    "artifacts": {
                        artifact_refs[0]: (
                            ".three-ci-validation/work/validation-build/"
                            "mapped-output.bin"
                        ),
                    },
                },
            ),
            encoding="utf-8",
        )

        original_rglob = Path.rglob

        def fail_enumeration(
            path: Path,
            *args: object,
            **kwargs: object,
        ) -> object:
            if path == build_root:
                raise AssertionError
            return original_rglob(path, *args, **kwargs)

        monkeypatch.setattr(Path, "rglob", fail_enumeration)
        result = _run_release_shaped_no_publish_validation(
            scratch=scratch,
            plan=plan,
            matrix=matrix,
        )

        assert result["outcome"] == "success"
        digests = _release_shaped_result_digests(result)
        assert (
            digests[artifact_refs[0]]
            == hashlib.sha256(
                output_bytes,
            ).hexdigest()
        )
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_validation_release_shaped_no_publish_binds_descriptor_id() -> None:
    """No-publish release-shaped evidence binds the frozen descriptor fact."""
    scratch = SCRATCH / "ci-validation-release-shaped-descriptor-identity"
    shutil.rmtree(scratch, ignore_errors=True)
    scratch.mkdir(parents=True)
    try:
        artifact_refs = ["ci-validation/artifacts/python/example/a.whl"]
        plan = _release_shaped_no_publish_plan(artifact_refs)
        matrix = _release_shaped_no_publish_matrix(plan)
        build_root = scratch / ".three-ci-validation/work/validation-build"
        build_root.mkdir(parents=True)
        (build_root / "a.whl").write_bytes(b"contents for a")
        mapping_path = (
            scratch
            / ".three-ci-validation/work/validation-build-artifacts.json"
        )
        mapping_path.write_text(
            json.dumps(
                {
                    "artifacts": {
                        artifact_refs[0]: (
                            ".three-ci-validation/work/validation-build/a.whl"
                        ),
                    },
                },
            ),
            encoding="utf-8",
        )
        fact_snapshot = _release_shaped_no_publish_fact_snapshot(
            descriptor_identity="descriptor-sha256:" + "e" * 64,
        )

        result = _run_release_shaped_no_publish_validation(
            scratch=scratch,
            plan=plan,
            matrix=matrix,
            fact_snapshot=fact_snapshot,
        )

        command = cast("Sequence[Mapping[str, object]]", result["commands"])[0]
        obligation_result = cast(
            "Sequence[Mapping[str, object]]",
            command["artifact-obligation-results"],
        )[0]
        descriptor = cast(
            "Mapping[str, object]", obligation_result["descriptor"]
        )
        assert result["outcome"] == "success"
        assert descriptor["identity"] == "descriptor-sha256:" + "e" * 64
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


@pytest.mark.parametrize(
    ("case", "fact_snapshot", "omit_fact_snapshot"),
    [
        ("missing", None, True),
        ("missing-descriptor", {"providers": [{"descriptors": []}]}, False),
        (
            "null-identity",
            {
                "providers": [
                    {
                        "descriptors": [
                            {
                                "descriptor-path": (
                                    "src/public/lib/example/three.release.yml"
                                ),
                                "descriptor-identity": None,
                            }
                        ],
                    }
                ],
            },
            False,
        ),
        (
            "missing-identity",
            {
                "providers": [
                    {
                        "descriptors": [
                            {
                                "descriptor-path": (
                                    "src/public/lib/example/three.release.yml"
                                ),
                            }
                        ],
                    }
                ],
            },
            False,
        ),
        (
            "empty-identity",
            {
                "providers": [
                    {
                        "descriptors": [
                            {
                                "descriptor-path": (
                                    "src/public/lib/example/three.release.yml"
                                ),
                                "descriptor-identity": "",
                            }
                        ],
                    }
                ],
            },
            False,
        ),
    ],
)
def test_ci_validation_release_shaped_no_publish_blocks_invalid_descriptor_fact(
    case: str,
    fact_snapshot: Mapping[str, object] | None,
    omit_fact_snapshot: bool,
) -> None:
    """No-publish release-shaped success requires descriptor fact evidence."""
    scratch = SCRATCH / f"ci-validation-release-shaped-{case}-descriptor-fact"
    shutil.rmtree(scratch, ignore_errors=True)
    scratch.mkdir(parents=True)
    try:
        artifact_refs = ["ci-validation/artifacts/python/example/a.whl"]
        plan = _release_shaped_no_publish_plan(artifact_refs)
        matrix = _release_shaped_no_publish_matrix(plan)
        build_root = scratch / ".three-ci-validation/work/validation-build"
        build_root.mkdir(parents=True)
        (build_root / "a.whl").write_bytes(b"contents for a")
        mapping_path = (
            scratch
            / ".three-ci-validation/work/validation-build-artifacts.json"
        )
        mapping_path.write_text(
            json.dumps(
                {
                    "artifacts": {
                        artifact_refs[0]: (
                            ".three-ci-validation/work/validation-build/a.whl"
                        ),
                    },
                },
            ),
            encoding="utf-8",
        )

        result = _run_release_shaped_no_publish_validation(
            scratch=scratch,
            plan=plan,
            matrix=matrix,
            fact_snapshot=fact_snapshot,
            omit_fact_snapshot=omit_fact_snapshot,
        )

        assert result["outcome"] == "blocking-failure"
        assert "artifact-shape-unconfirmed" in result["commands"][0]["error"]
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_validation_release_shaped_incomplete_mapping_blocks() -> None:
    """Release-shaped no-publish fails closed on incomplete mapping."""
    scratch = SCRATCH / "ci-validation-release-shaped-output-unbound"
    shutil.rmtree(scratch, ignore_errors=True)
    scratch.mkdir(parents=True)
    try:
        artifact_refs = [
            "ci-validation/artifacts/python/example/a.whl",
            "ci-validation/artifacts/python/example/b.whl",
        ]
        plan = _release_shaped_no_publish_plan(artifact_refs)
        matrix = _release_shaped_no_publish_matrix(plan)
        build_root = scratch / ".three-ci-validation/work/validation-build"
        build_root.mkdir(parents=True)
        (build_root / "first-output.bin").write_bytes(b"first")
        (build_root / "second-output.bin").write_bytes(b"second")
        mapping_path = (
            scratch
            / ".three-ci-validation/work/validation-build-artifacts.json"
        )
        mapping_path.write_text(
            json.dumps(
                {
                    "artifacts": {
                        artifact_refs[0]: (
                            ".three-ci-validation/work/validation-build/"
                            "first-output.bin"
                        ),
                    },
                },
            ),
            encoding="utf-8",
        )
        result = _run_release_shaped_no_publish_validation(
            scratch=scratch,
            plan=plan,
            matrix=matrix,
        )

        assert result["outcome"] == "blocking-failure"
        assert "artifact-shape-unconfirmed" in result["commands"][0]["error"]
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def _release_shaped_no_publish_plan(
    artifact_refs: Sequence[str],
) -> dict[str, object]:
    return {
        "validation-tree": {"commit-sha": "b" * 40},
        "work-groups": [
            {
                "work-group-id": "wg-release",
                "kind": "release-shaped-artifact",
                "runner-family": "ubuntu",
                "coverage-target": {"type": "subject", "id": "python.example"},
                "depends-on": [],
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
                    "expected-artifact-refs": list(artifact_refs),
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


def _release_shaped_no_publish_matrix(
    plan: Mapping[str, object],
) -> dict[str, object]:
    work_group = cast(
        "dict[str, object]",
        cast("Sequence[object]", plan["work-groups"])[0],
    )
    return {
        **work_group,
        "validation-commands": control._ci_validation_commands(
            plan,
            work_group,
        ),
        "no-publish": True,
    }


def _release_shaped_no_publish_fact_snapshot(
    descriptor_path: str = "src/public/lib/example/three.release.yml",
    *,
    descriptor_identity: str = "descriptor-sha256:" + "d" * 64,
) -> dict[str, object]:
    return {
        "providers": [
            {
                "provider": "python",
                "descriptors": [
                    {
                        "descriptor-path": descriptor_path,
                        "descriptor-identity": descriptor_identity,
                        "owner-subject-id": "python.example",
                        "source": "ecosystem-provider",
                    }
                ],
            }
        ]
    }


def _run_release_shaped_no_publish_validation(
    *,
    scratch: Path,
    plan: Mapping[str, object],
    matrix: Mapping[str, object],
    fact_snapshot: Mapping[str, object] | None = None,
    omit_fact_snapshot: bool = False,
) -> dict[str, object]:
    plan_path = scratch / "validation-plan.json"
    fact_snapshot_path = scratch / "fact-snapshot.json"
    result_path = scratch / "validation-result.json"
    plan_path.write_text(json.dumps(plan), encoding="utf-8")
    if not omit_fact_snapshot:
        fact_snapshot_path.write_text(
            json.dumps(
                fact_snapshot or _release_shaped_no_publish_fact_snapshot(),
            ),
            encoding="utf-8",
        )
    assert (
        control._cmd_run_ci_validation_commands(
            argparse.Namespace(
                matrix_work_group_json=json.dumps(matrix),
                plan=str(plan_path),
                changed_files_snapshot="",
                fact_snapshot=str(fact_snapshot_path)
                if not omit_fact_snapshot
                else "",
                assignments="",
                observed_artifacts_dir="",
                observed_commit_sha="b" * 40,
                repo_root=str(scratch),
                result_out=str(result_path),
                github_output=None,
            )
        )
        == 0
    )
    return cast(
        "dict[str, object]",
        json.loads(result_path.read_text(encoding="utf-8")),
    )


def _release_shaped_result_digests(
    validation_result: Mapping[str, object],
) -> dict[str, str]:
    command = cast(
        "Mapping[str, object]",
        cast("Sequence[object]", validation_result["commands"])[0],
    )
    results = cast(
        "Sequence[Mapping[str, object]]",
        command["artifact-obligation-results"],
    )
    digests: dict[str, str] = {}
    for result in results:
        artifact = cast("Mapping[str, object]", result["artifact"])
        observed = cast("Mapping[str, object]", artifact["observed"])
        for item in cast("Sequence[Mapping[str, object]]", observed["digests"]):
            digests[str(item["artifact-ref"])] = str(item["digest"])
    return digests


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
            "evidence-source": "unsupported-helper-command",
            "artifact-obligation-results": [],
        },
        {"outcome": "success"},
        "malformed-command",
    ],
)
def test_ci_validation_release_shaped_source_helpers_reject_extra_commands(
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


def test_download_ci_validation_observed_artifacts_downloads_batch_metadata(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Batch mode downloads expected bundles using API metadata only."""
    plan, manifest = _ci_batch_contract_plan_and_manifest()
    row = _ci_batch_matrix_rows(plan, manifest)[0]
    batch = cast("list[dict[str, object]]", manifest["batches"])[0]
    selector = cast("list[dict[str, object]]", batch["ordered-selectors"])[0]
    bundle = _write_ci_batch_bundle(
        tmp_path,
        plan,
        manifest,
        row,
        [
            _ci_success_validation_result(
                plan,
                cast("str", selector["work-group-id"]),
            )
        ],
    )
    artifact_ref = cast("str", batch["expected-batch-evidence-bundle-ref"])
    artifact_name_value = artifact_physical_name(artifact_ref)
    manifest_path = tmp_path / "execution-batch-manifest.json"
    observed_root = tmp_path / "observed-artifacts"
    manifest_path.write_text(json.dumps(manifest), encoding="utf-8")
    (observed_root / "stale-caller-preseeded").mkdir(parents=True)

    name_downloads: list[str] = []
    id_downloads: list[str] = []

    def fake_download_artifact(
        _repository: str,
        _run_id: int,
        requested_artifact_name: str,
        destination: Path,
    ) -> None:
        name_downloads.append(requested_artifact_name)
        destination.mkdir(parents=True)

    def fake_download_artifact_by_id(
        _repository: str,
        artifact_api: Mapping[str, object],
        requested_artifact_name: str,
        destination: Path,
    ) -> None:
        assert requested_artifact_name == artifact_name_value
        assert artifact_api["id"] == 424242
        id_downloads.append(str(artifact_api["id"]))
        destination.mkdir(parents=True)
        (destination / "batch-evidence-bundle.json").write_text(
            json.dumps(bundle),
            encoding="utf-8",
        )

    monkeypatch.setattr(control, "_download_artifact", fake_download_artifact)
    monkeypatch.setattr(
        control,
        "_download_artifact_by_id",
        fake_download_artifact_by_id,
    )
    monkeypatch.setattr(
        control,
        "_github_actions_run_artifacts",
        lambda **_: [
            {
                "id": 424242,
                "name": artifact_name_value,
                "workflow_run": {
                    "id": int(batch_contracts.RUN_ID),
                    "run_attempt": int(batch_contracts.RUN_ATTEMPT),
                },
            }
        ],
    )

    result = control._cmd_download_ci_validation_observed_artifacts(
        argparse.Namespace(
            repository="hcoona/three",
            run_id=batch_contracts.RUN_ID,
            assignments="",
            execution_batch_manifest=str(manifest_path),
            observed_artifacts_dir=str(observed_root),
            github_output=None,
        )
    )

    metadata = json.loads(
        (
            observed_root / artifact_name_value / "artifact-metadata.json"
        ).read_text(encoding="utf-8")
    )
    assert result == 0
    assert name_downloads == []
    assert id_downloads == ["424242"]
    assert not (observed_root / "stale-caller-preseeded").exists()
    assert metadata == {
        "artifact-instance-id": "424242",
        "artifact-ref": artifact_ref,
        "physical-artifact-name": artifact_name_value,
        "run-id": batch_contracts.RUN_ID,
        "run-attempt": batch_contracts.RUN_ATTEMPT,
        "producer-boundary": "execution-batch",
        "admission-source": "github-actions-live-api",
    }
    observation = json.loads(
        (observed_root / control._CI_DOWNLOADER_OBSERVATION_FILE).read_text(
            encoding="utf-8"
        )
    )
    admissions = observation[
        control._CI_DOWNLOADER_ADMITTED_BATCH_ARTIFACTS_KEY
    ]
    assert admissions == [
        {
            "admission-source": "github-actions-live-api",
            "artifact-instance-id": "424242",
            "artifact-ref": artifact_ref,
            "batch-id": batch["batch-id"],
            "candidate-id": ci_validation_batch_evidence_candidate_id(
                run_id=batch_contracts.RUN_ID,
                run_attempt=batch_contracts.RUN_ATTEMPT,
                batch_id=cast("str", batch["batch-id"]),
                artifact_ref=artifact_ref,
                artifact_instance_id="424242",
                physical_artifact_name=artifact_name_value,
            ),
            "physical-artifact-name": artifact_name_value,
            "producer-boundary": "execution-batch",
            "run-attempt": batch_contracts.RUN_ATTEMPT,
            "run-id": batch_contracts.RUN_ID,
        }
    ]
    aggregate_result, _aggregate_manifest, summary = (
        _aggregate_ci_batch_evidence(
            tmp_path,
            plan,
            manifest,
            observed_root,
        )
    )
    assert aggregate_result == 0
    assert summary["verdict"] == "passed"


def test_download_ci_validation_observed_artifacts_rejects_attempt_mismatch(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Aggregate download rejects current-name artifacts with wrong attempt."""
    _plan, manifest = _ci_batch_contract_plan_and_manifest()
    batch = cast("list[dict[str, object]]", manifest["batches"])[0]
    artifact_ref = cast("str", batch["expected-batch-evidence-bundle-ref"])
    artifact_name_value = artifact_physical_name(artifact_ref)
    manifest_path = tmp_path / "execution-batch-manifest.json"
    output_path = tmp_path / "github-output.txt"
    observed_root = tmp_path / "observed-artifacts"
    manifest_path.write_text(json.dumps(manifest), encoding="utf-8")
    downloads: list[str] = []

    def fake_download_artifact_by_id(
        _repository: str,
        artifact_api: Mapping[str, object],
        requested_artifact_name: str,
        destination: Path,
    ) -> None:
        downloads.append(str(artifact_api["id"]))
        destination.mkdir(parents=True)
        (destination / "batch-evidence-bundle.json").write_text(
            "{}",
            encoding="utf-8",
        )
        assert requested_artifact_name == artifact_name_value

    monkeypatch.setattr(
        control,
        "_download_artifact_by_id",
        fake_download_artifact_by_id,
    )
    monkeypatch.setattr(
        control,
        "_github_actions_run_artifacts",
        lambda **_: [
            {
                "id": 424242,
                "name": artifact_name_value,
                "workflow_run": {
                    "id": int(batch_contracts.RUN_ID),
                    "run_attempt": int(batch_contracts.RUN_ATTEMPT) + 1,
                },
            }
        ],
    )

    result = control._cmd_download_ci_validation_observed_artifacts(
        argparse.Namespace(
            repository="hcoona/three",
            run_id=batch_contracts.RUN_ID,
            run_attempt=batch_contracts.RUN_ATTEMPT,
            plan="",
            execution_batch_manifest=str(manifest_path),
            observed_artifacts_dir=str(observed_root),
            github_output=str(output_path),
        )
    )

    output = output_path.read_text(encoding="utf-8")
    assert result == 0
    assert downloads == []
    assert "failed_artifact_count=1" in output
    assert not (
        observed_root / artifact_name_value / "artifact-metadata.json"
    ).exists()


@pytest.mark.parametrize(
    ("api_artifacts", "expected_live_count"),
    [
        ([], 0),
        ([{"id": 424242, "expired": True}], 0),
        ([{"id": 424242}, {"id": 424243}], 2),
    ],
)
def test_download_ci_validation_rejects_missing_or_duplicate_batch_api(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    api_artifacts: list[dict[str, object]],
    expected_live_count: int,
) -> None:
    """Expected batch bundles require one exact live API artifact instance."""
    _plan, manifest = _ci_batch_contract_plan_and_manifest()
    batch = cast("list[dict[str, object]]", manifest["batches"])[0]
    artifact_ref = cast("str", batch["expected-batch-evidence-bundle-ref"])
    artifact_name_value = artifact_physical_name(artifact_ref)
    manifest_path = tmp_path / "execution-batch-manifest.json"
    output_path = tmp_path / "outputs.txt"
    observed_root = tmp_path / "observed-artifacts"
    manifest_path.write_text(json.dumps(manifest), encoding="utf-8")
    calls: list[str] = []

    def fake_download_artifact(
        _repository: str,
        _run_id: int,
        requested_artifact_name: str,
        _destination: Path,
    ) -> None:
        calls.append(requested_artifact_name)

    monkeypatch.setattr(control, "_download_artifact", fake_download_artifact)
    monkeypatch.setattr(
        control,
        "_github_actions_run_artifacts",
        lambda **_: [
            {**artifact, "name": artifact_name_value}
            for artifact in api_artifacts
        ],
    )

    result = control._cmd_download_ci_validation_observed_artifacts(
        argparse.Namespace(
            repository="hcoona/three",
            run_id=batch_contracts.RUN_ID,
            assignments="",
            execution_batch_manifest=str(manifest_path),
            observed_artifacts_dir=str(observed_root),
            github_output=str(output_path),
        )
    )

    outputs = _github_outputs(output_path)
    failed_names = json.loads(outputs["failed_artifact_names"])
    assert result == 0
    assert calls == []
    assert not (observed_root / artifact_name_value).exists()
    assert outputs["downloaded_artifact_count"] == "0"
    assert outputs["failed_artifact_count"] == "1"
    assert artifact_name_value in failed_names
    assert outputs["artifact_api_metadata_available"] == "true"
    live_artifacts = [
        artifact for artifact in api_artifacts if not artifact.get("expired")
    ]
    assert len(live_artifacts) == expected_live_count


def test_download_ci_validation_accepts_live_batch_api_with_expired_match(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Expected batch bundles ignore expired API artifacts."""
    _plan, manifest = _ci_batch_contract_plan_and_manifest()
    batch = cast("list[dict[str, object]]", manifest["batches"])[0]
    artifact_ref = cast("str", batch["expected-batch-evidence-bundle-ref"])
    artifact_name_value = artifact_physical_name(artifact_ref)
    manifest_path = tmp_path / "execution-batch-manifest.json"
    output_path = tmp_path / "outputs.txt"
    observed_root = tmp_path / "observed-artifacts"
    manifest_path.write_text(json.dumps(manifest), encoding="utf-8")
    name_downloads: list[str] = []
    id_downloads: list[str] = []

    def fake_download_artifact(
        _repository: str,
        _run_id: int,
        requested_artifact_name: str,
        _destination: Path,
    ) -> None:
        name_downloads.append(requested_artifact_name)

    def fake_download_artifact_by_id(
        _repository: str,
        artifact_api: Mapping[str, object],
        requested_artifact_name: str,
        destination: Path,
    ) -> None:
        assert requested_artifact_name == artifact_name_value
        assert artifact_api == {
            "id": 424243,
            "name": artifact_name_value,
            "expired": False,
            "workflow_run": {"id": int(batch_contracts.RUN_ID)},
        }
        id_downloads.append(str(artifact_api["id"]))
        destination.mkdir(parents=True)
        (destination / "batch-evidence-bundle.json").write_text(
            "{}",
            encoding="utf-8",
        )

    monkeypatch.setattr(control, "_download_artifact", fake_download_artifact)
    monkeypatch.setattr(
        control,
        "_download_artifact_by_id",
        fake_download_artifact_by_id,
    )
    monkeypatch.setattr(
        control,
        "_github_actions_run_artifacts",
        lambda **_: [
            {"id": 424242, "name": artifact_name_value, "expired": True},
            {
                "id": 424243,
                "name": artifact_name_value,
                "expired": False,
                "workflow_run": {"id": int(batch_contracts.RUN_ID)},
            },
        ],
    )

    result = control._cmd_download_ci_validation_observed_artifacts(
        argparse.Namespace(
            repository="hcoona/three",
            run_id=batch_contracts.RUN_ID,
            assignments="",
            execution_batch_manifest=str(manifest_path),
            observed_artifacts_dir=str(observed_root),
            github_output=str(output_path),
        )
    )

    outputs = _github_outputs(output_path)
    assert result == 0
    assert name_downloads == []
    assert id_downloads == ["424243"]
    metadata_path = (
        observed_root / artifact_name_value / "artifact-metadata.json"
    )
    assert json.loads(
        metadata_path.read_text(encoding="utf-8"),
    ) == {
        "artifact-instance-id": "424243",
        "artifact-ref": artifact_ref,
        "physical-artifact-name": artifact_name_value,
        "producer-boundary": "execution-batch",
        "admission-source": "github-actions-live-api",
        "run-attempt": batch_contracts.RUN_ATTEMPT,
        "run-id": batch_contracts.RUN_ID,
    }
    assert outputs["downloaded_artifact_count"] == "1"
    assert outputs["failed_artifact_count"] == "0"
    assert outputs["artifact_api_metadata_available"] == "true"


def test_download_ci_validation_closes_live_contract_namespace(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Unexpected live CI validation artifacts are materialized fail-closed."""
    _plan, manifest = _ci_batch_contract_plan_and_manifest()
    batch = cast("list[dict[str, object]]", manifest["batches"])[0]
    artifact_ref = cast("str", batch["expected-batch-evidence-bundle-ref"])
    artifact_name_value = artifact_physical_name(artifact_ref)
    unexpected_control_name = (
        "three-ci-validation-25887422010-1-unexpected-control"
    )
    planner_diagnostics_name = artifact_physical_name(
        control.ci_validation_planner_diagnostics_artifact_ref(
            run_id=batch_contracts.RUN_ID,
            run_attempt=batch_contracts.RUN_ATTEMPT,
        )
    )
    unexpected_name = "three-ci-validation-25887422010-1-unexpected-live"
    expired_unexpected_name = "three-ci-validation-25887422010-1-expired-live"
    manifest_path = tmp_path / "execution-batch-manifest.json"
    output_path = tmp_path / "outputs.txt"
    observed_root = tmp_path / "observed-artifacts"
    manifest_path.write_text(json.dumps(manifest), encoding="utf-8")
    name_downloads: list[str] = []
    id_downloads: list[str] = []

    def fake_download_artifact(
        _repository: str,
        _run_id: int,
        requested_artifact_name: str,
        destination: Path,
    ) -> None:
        name_downloads.append(requested_artifact_name)
        destination.mkdir(parents=True)

    def fake_download_artifact_by_id(
        _repository: str,
        artifact_api: Mapping[str, object],
        requested_artifact_name: str,
        destination: Path,
    ) -> None:
        assert requested_artifact_name == artifact_name_value
        assert artifact_api == {
            "id": 424242,
            "name": artifact_name_value,
            "workflow_run": {"id": batch_contracts.RUN_ID},
        }
        id_downloads.append(str(artifact_api["id"]))
        destination.mkdir(parents=True)

    monkeypatch.setattr(control, "_download_artifact", fake_download_artifact)
    monkeypatch.setattr(
        control,
        "_download_artifact_by_id",
        fake_download_artifact_by_id,
    )
    monkeypatch.setattr(
        control,
        "_github_actions_run_artifacts",
        lambda **_: [
            {
                "id": 424242,
                "name": artifact_name_value,
                "workflow_run": {"id": batch_contracts.RUN_ID},
            },
            {"id": 424245, "name": unexpected_control_name},
            {"id": 424246, "name": planner_diagnostics_name},
            {"id": 424243, "name": unexpected_name},
            {"id": 424244, "name": expired_unexpected_name, "expired": True},
        ],
    )

    result = control._cmd_download_ci_validation_observed_artifacts(
        argparse.Namespace(
            repository="hcoona/three",
            run_id=batch_contracts.RUN_ID,
            assignments="",
            execution_batch_manifest=str(manifest_path),
            observed_artifacts_dir=str(observed_root),
            github_output=str(output_path),
        )
    )

    outputs = _github_outputs(output_path)
    assert result == 0
    assert name_downloads == []
    assert id_downloads == ["424242"]
    assert (observed_root / unexpected_control_name).is_dir()
    assert (observed_root / planner_diagnostics_name).is_dir()
    assert (observed_root / unexpected_name).is_dir()
    assert not (observed_root / expired_unexpected_name).exists()
    assert outputs["failed_artifact_count"] == "3"
    assert set(json.loads(outputs["failed_artifact_names"])) == {
        unexpected_control_name,
        planner_diagnostics_name,
        unexpected_name,
    }


@pytest.mark.parametrize(
    ("manifest_contents", "manifest_exists"),
    [
        ("", False),
        ("{ malformed manifest json", True),
    ],
)
def test_download_ci_validation_closes_live_namespace_without_manifest(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    manifest_contents: str,
    manifest_exists: bool,
) -> None:
    """Missing or malformed manifests preserve unexpected live artifacts."""
    manifest_path = tmp_path / "execution-batch-manifest.json"
    output_path = tmp_path / "outputs.txt"
    observed_root = tmp_path / "observed-artifacts"
    if manifest_exists:
        manifest_path.write_text(manifest_contents, encoding="utf-8")
    allowed_request_name = artifact_physical_name(
        control.ci_validation_request_artifact_ref(
            run_id=batch_contracts.RUN_ID,
            run_attempt=batch_contracts.RUN_ATTEMPT,
        )
    )
    unexpected_name = "three-ci-validation-25887422010-1-unexpected-live"
    calls: list[str] = []

    def fake_download_artifact(
        _repository: str,
        _run_id: int,
        requested_artifact_name: str,
        _destination: Path,
    ) -> None:
        calls.append(requested_artifact_name)

    monkeypatch.setattr(control, "_download_artifact", fake_download_artifact)
    monkeypatch.setattr(
        control,
        "_github_actions_run_artifacts",
        lambda **_: [
            {"id": 424242, "name": allowed_request_name},
            {"id": 424243, "name": unexpected_name},
        ],
    )

    result = control._cmd_download_ci_validation_observed_artifacts(
        argparse.Namespace(
            repository="hcoona/three",
            run_id=batch_contracts.RUN_ID,
            run_attempt=batch_contracts.RUN_ATTEMPT,
            assignments="",
            execution_batch_manifest=str(manifest_path),
            observed_artifacts_dir=str(observed_root),
            github_output=str(output_path),
        )
    )

    outputs = _github_outputs(output_path)
    assert result == 0
    assert calls == []
    assert not (observed_root / allowed_request_name).exists()
    unexpected_metadata = (
        observed_root / unexpected_name / "artifact-metadata.json"
    )
    assert unexpected_metadata.is_file()
    assert outputs["downloaded_artifact_count"] == "0"
    assert outputs["failed_artifact_count"] == "1"
    assert json.loads(outputs["failed_artifact_names"]) == [unexpected_name]
    observation = json.loads(
        (observed_root / control._CI_DOWNLOADER_OBSERVATION_FILE).read_text(
            encoding="utf-8",
        )
    )
    assert observation["artifact-api-metadata-available"] is True
    assert observation["namespace-enumeration"] == "available"


def test_download_ci_validation_records_namespace_overflow_bounded(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Live namespace overflow records only the cap plus sentinel."""
    cap = control._CI_VALIDATION_LIVE_NAMESPACE_ARTIFACT_CAP
    output_path = tmp_path / "outputs.txt"
    observed_root = tmp_path / "observed-artifacts"
    api_calls: list[str] = []
    unexpected_names = [
        f"three-ci-validation-25887422010-1-{index:064x}"
        for index in range(cap + 7)
    ]

    def fake_gh_api(_repository: str, endpoint: str, **_kwargs) -> object:
        api_calls.append(endpoint)
        return {
            "artifacts": [
                {"id": 9000 + index, "name": name}
                for index, name in enumerate(unexpected_names)
            ]
        }

    monkeypatch.setattr(control, "_gh_api", fake_gh_api)
    monkeypatch.setattr(
        control,
        "_gh_api_paginated",
        lambda *_args, **_kwargs: pytest.fail(
            "bounded namespace enumeration must not use --paginate --slurp"
        ),
    )
    monkeypatch.setattr(
        control,
        "_download_artifact",
        lambda *_args, **_kwargs: pytest.fail(
            "unexpected live namespace artifacts must not be downloaded"
        ),
    )

    result = control._cmd_download_ci_validation_observed_artifacts(
        argparse.Namespace(
            repository="hcoona/three",
            run_id=batch_contracts.RUN_ID,
            run_attempt=batch_contracts.RUN_ATTEMPT,
            assignments="",
            execution_batch_manifest="",
            observed_artifacts_dir=str(observed_root),
            github_output=str(output_path),
        )
    )

    outputs = _github_outputs(output_path)
    materialized_names = sorted(
        path.name for path in observed_root.iterdir() if path.is_dir()
    )
    assert result == 0
    assert api_calls == [
        (
            f"repos/hcoona/three/actions/runs/{batch_contracts.RUN_ID}"
            "/artifacts?per_page=100&page=1"
        )
    ]
    assert materialized_names == unexpected_names[: cap + 1]
    assert not (observed_root / unexpected_names[cap + 1]).exists()
    assert outputs["downloaded_artifact_count"] == "0"
    assert outputs["failed_artifact_count"] == str(cap + 1)
    assert (
        json.loads(outputs["failed_artifact_names"])
        == unexpected_names[: cap + 1]
    )
    assert outputs["namespace_overflow"] == "true"
    observation = json.loads(
        (observed_root / control._CI_DOWNLOADER_OBSERVATION_FILE).read_text(
            encoding="utf-8",
        )
    )
    assert observation["namespace-overflow"] is True


def test_download_ci_validation_records_enumeration_failure(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Downloader materializes namespace enumeration failures for aggregate."""
    output_path = tmp_path / "outputs.txt"
    observed_root = tmp_path / "observed-artifacts"
    monkeypatch.setattr(
        control,
        "_github_actions_run_artifacts",
        lambda **_: (_ for _ in ()).throw(RuntimeError("api unavailable")),
    )

    result = control._cmd_download_ci_validation_observed_artifacts(
        argparse.Namespace(
            repository="hcoona/three",
            run_id=batch_contracts.RUN_ID,
            run_attempt=batch_contracts.RUN_ATTEMPT,
            assignments="",
            plan="",
            execution_batch_manifest="",
            observed_artifacts_dir=str(observed_root),
            github_output=str(output_path),
        )
    )

    outputs = _github_outputs(output_path)
    observation = json.loads(
        (observed_root / control._CI_DOWNLOADER_OBSERVATION_FILE).read_text(
            encoding="utf-8",
        )
    )
    assert result == 0
    assert outputs["artifact_api_metadata_available"] == "false"
    assert outputs["namespace_overflow"] == "false"
    assert observation["artifact-api-metadata-available"] is False
    assert observation["namespace-enumeration"] == "unavailable"


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


def _ci_batch_bundle_scratch(test_name: str) -> Path:
    scratch = SCRATCH / test_name
    shutil.rmtree(scratch, ignore_errors=True)
    scratch.mkdir(parents=True)
    return scratch


def _ci_batch_contract_plan_and_manifest() -> tuple[
    dict[str, object], dict[str, object]
]:
    plan = cast("dict[str, object]", batch_contracts.plan())
    manifest = cast(
        "dict[str, object]",
        batch_contracts.manifest(plan),
    )
    return plan, manifest


def _ci_batch_matrix_rows(
    plan: dict[str, object],
    manifest: dict[str, object],
) -> list[dict[str, object]]:
    matrix = ci_validation_execution_batch_matrix(
        manifest,
        plan=plan,
        **batch_contracts.authorizing_context_kwargs(),
    )
    return cast("list[dict[str, object]]", matrix["include"])


def _ci_success_validation_result(
    plan: dict[str, object],
    work_group_id: str,
) -> dict[str, object]:
    group = control._ci_work_group(plan, work_group_id)
    expectation = control._ci_evidence_expectation(plan, work_group_id)
    capabilities = cast(
        "Sequence[object]",
        expectation.get("planned-capabilities", []),
    )
    return {
        "work-group-id": work_group_id,
        "kind": group["kind"],
        "runner-family": group["runner-family"],
        "coverage-target": group["coverage-target"],
        "observed-commit-sha": batch_contracts.TREE_SHA,
        "outcome": "success",
        "commands": [
            {
                "index": index,
                "label": f"validate {capability}",
                "argv": [],
                "capability": capability,
                "exit-code": 0,
                "outcome": "success",
            }
            for index, capability in enumerate(capabilities)
        ],
    }


def _ci_same_batch_manifest_fixture() -> tuple[
    dict[str, object],
    dict[str, object],
    dict[str, object],
    dict[str, object],
]:
    plan = cast("dict[str, object]", batch_contracts.plan())
    batch_contracts.add_dependent_work_group(plan)
    dependent_group = next(
        group
        for group in cast("list[dict[str, object]]", plan["work-groups"])
        if group["work-group-id"] == "wg-dependent-gate"
    )
    dependent_group["ecosystem"] = "python"
    plan["plan-digest"] = ci_validation_plan_digest(plan)
    materialization = (
        batch_contracts.materialize_ci_validation_execution_batches(
            plan=plan,
            **batch_contracts.authorizing_context_kwargs(),
            created_at=batch_contracts.CREATED_AT,
            execution_workflow="CI Validation",
        )
    )
    manifest = cast("dict[str, object]", materialization.manifest)
    batch = cast("list[dict[str, object]]", manifest["batches"])[0]
    selectors = cast("list[dict[str, object]]", batch["ordered-selectors"])
    return plan, manifest, batch, selectors[1]


def _write_ci_batch_bundle_inputs(  # noqa: PLR0913
    scratch: Path,
    plan: dict[str, object],
    manifest: dict[str, object],
    matrix_row: dict[str, object],
    validation_results: Sequence[dict[str, object]],
    authorizing_context: Mapping[str, object] | None = None,
) -> tuple[Path, Path, Path, Path, Path, Path, list[Path], Path]:
    plan_path = scratch / "plan.json"
    request_path = scratch / "request.json"
    manifest_path = scratch / "execution-batch-manifest.json"
    changed_files_path = scratch / "changed-files.json"
    fact_snapshot_path = scratch / "fact-snapshot.json"
    bundle_path = scratch / "batch-evidence-bundle.json"
    if authorizing_context is None:
        authorizing_context = batch_contracts.authorizing_context_kwargs()
    plan_path.write_text(json.dumps(plan), encoding="utf-8")
    request_path.write_text(
        json.dumps(authorizing_context["request"]),
        encoding="utf-8",
    )
    manifest_path.write_text(json.dumps(manifest), encoding="utf-8")
    changed_files_path.write_text(
        json.dumps(authorizing_context["changed_files_snapshot"]),
        encoding="utf-8",
    )
    fact_snapshot_path.write_text(
        json.dumps(authorizing_context["fact_snapshot"]),
        encoding="utf-8",
    )
    result_paths = []
    for index, result in enumerate(validation_results):
        path = scratch / f"validation-result-{index}.json"
        path.write_text(json.dumps(result), encoding="utf-8")
        result_paths.append(path)
    matrix_path = scratch / "matrix-row.json"
    matrix_path.write_text(json.dumps(matrix_row), encoding="utf-8")
    return (
        plan_path,
        request_path,
        manifest_path,
        changed_files_path,
        fact_snapshot_path,
        matrix_path,
        result_paths,
        bundle_path,
    )


def _write_ci_batch_bundle(  # noqa: PLR0913
    scratch: Path,
    plan: dict[str, object],
    manifest: dict[str, object],
    matrix_row: dict[str, object],
    validation_results: Sequence[dict[str, object]],
    *,
    job: str = "execution-batch",
    dependency_results_json: str = "",
    dependency_bundles: Sequence[Path] = (),
    assignments: Path | None = None,
    observed_artifacts_dir: Path | None = None,
    expected_run_id: str | None = None,
    expected_run_attempt: str | None = None,
    authorizing_context: Mapping[str, object] | None = None,
) -> dict[str, object]:
    (
        plan_path,
        request_path,
        manifest_path,
        changed_files_path,
        fact_snapshot_path,
        matrix_path,
        result_paths,
        bundle_path,
    ) = _write_ci_batch_bundle_inputs(
        scratch,
        plan,
        manifest,
        matrix_row,
        validation_results,
        authorizing_context,
    )
    if dependency_bundles and observed_artifacts_dir is None:
        observed_artifacts_dir = scratch / "observed-artifacts"
        dependency_bundles = _stage_dependency_bundles_by_physical_name(
            observed_artifacts_dir,
            dependency_bundles,
        )
    dependency_admissions = [
        _dependency_admission_for_staged_bundle(path)
        for path in dependency_bundles
        if (path.parent / "artifact-metadata.json").is_file()
    ]
    output_path = scratch / "github-output.txt"
    control._cmd_write_ci_validation_batch_evidence_bundle(
        argparse.Namespace(
            plan=str(plan_path),
            request=str(request_path),
            execution_batch_manifest=str(manifest_path),
            changed_files_snapshot=str(changed_files_path),
            fact_snapshot=str(fact_snapshot_path),
            matrix_row_json=matrix_path.read_text(encoding="utf-8"),
            expected_run_id=(expected_run_id or batch_contracts.RUN_ID),
            expected_run_attempt=(
                expected_run_attempt or batch_contracts.RUN_ATTEMPT
            ),
            workflow="CI Validation",
            job=job,
            assignments=str(assignments) if assignments is not None else "",
            observed_artifacts_dir=(
                str(observed_artifacts_dir)
                if observed_artifacts_dir is not None
                else ""
            ),
            observed_commit_sha=batch_contracts.TREE_SHA,
            validation_result=[str(path) for path in result_paths],
            dependency_results_json=dependency_results_json,
            dependency_bundle=[str(path) for path in dependency_bundles],
            _dependency_artifact_admissions=dependency_admissions,
            started_at=batch_contracts.CREATED_AT,
            completed_at=batch_contracts.CREATED_AT,
            created_at=batch_contracts.CREATED_AT,
            bundle_out=str(bundle_path),
            github_output=str(output_path),
        )
    )
    return json.loads(bundle_path.read_text(encoding="utf-8"))


def _dependency_admission_for_staged_bundle(path: Path) -> dict[str, object]:
    metadata = json.loads(
        (path.parent / "artifact-metadata.json").read_text(encoding="utf-8")
    )
    return cast("dict[str, object]", metadata)


def test_ci_dependency_artifact_metadata_requires_matching_admission_source(
    tmp_path: Path,
) -> None:
    """Admission source is part of trusted dependency artifact identity."""
    metadata_path = tmp_path / "artifact-metadata.json"
    artifact_ref = (
        "ci-validation/batches/1/1/upstream/batch-evidence-bundle.json"
    )
    metadata = {
        "artifact-ref": artifact_ref,
        "physical-artifact-name": (
            "three-ci-validation-1-1-"
            "ci-validation-batches-1-1-upstream-batch-evidence-bundle-json"
        ),
        "artifact-instance-id": "artifact-1",
        "run-id": "1",
        "run-attempt": "1",
        "producer-boundary": "execution-batch",
        "admission-source": (control._CI_ORCHESTRATOR_STATE_ADMISSION_SOURCE),
    }
    metadata_path.write_text(json.dumps(metadata), encoding="utf-8")
    admission = dict(metadata)
    admission["admission-source"] = "github-actions-live-api"

    assert not control._ci_dependency_artifact_metadata_matches_admission(
        metadata_path,
        admission,
    )


def test_ci_trusted_dependency_bundle_rejects_mismatched_admission_source(
    tmp_path: Path,
) -> None:
    """Dependency bundle admission must bind to the same admission source."""
    artifact_ref = (
        "ci-validation/batches/1/1/upstream/batch-evidence-bundle.json"
    )
    artifact_name_value = artifact_physical_name(artifact_ref)
    bundle_path = tmp_path / artifact_name_value / "batch-evidence-bundle.json"
    bundle_path.parent.mkdir(parents=True)
    bundle = {
        "artifact-ref": artifact_ref,
        "batch": {"batch-id": "upstream"},
    }
    metadata = {
        "artifact-ref": artifact_ref,
        "physical-artifact-name": artifact_name_value,
        "artifact-instance-id": "artifact-1",
        "run-id": "1",
        "run-attempt": "1",
        "producer-boundary": "execution-batch",
        "admission-source": (control._CI_ORCHESTRATOR_STATE_ADMISSION_SOURCE),
    }
    bundle_path.write_text(json.dumps(bundle), encoding="utf-8")
    (bundle_path.parent / "artifact-metadata.json").write_text(
        json.dumps(metadata),
        encoding="utf-8",
    )
    admission = dict(metadata)
    admission["admission-source"] = "github-actions-live-api"

    with pytest.raises(RuntimeError, match="admission-source"):
        control._ci_trusted_dependency_bundle(
            str(bundle_path),
            bundle,
            expected_run_id="1",
            expected_run_attempt="1",
            admission=admission,
        )


def _stage_dependency_bundles_by_physical_name(
    observed_artifacts_dir: Path,
    dependency_bundles: Sequence[Path],
) -> list[Path]:
    staged_paths: list[Path] = []
    for source in dependency_bundles:
        try:
            bundle = json.loads(source.read_text(encoding="utf-8"))
        except (OSError, ValueError, TypeError):
            staged_paths.append(source)
            continue
        artifact_ref = (
            bundle.get("artifact-ref") if isinstance(bundle, dict) else None
        )
        if not isinstance(artifact_ref, str):
            staged_paths.append(source)
            continue
        destination = (
            observed_artifacts_dir
            / artifact_physical_name(artifact_ref)
            / "batch-evidence-bundle.json"
        )
        destination.parent.mkdir(parents=True, exist_ok=True)
        destination.write_text(
            source.read_text(encoding="utf-8"),
            encoding="utf-8",
        )
        batch = cast("dict[str, object]", bundle["batch"])
        artifact_name_value = artifact_physical_name(artifact_ref)
        (destination.parent / "artifact-metadata.json").write_text(
            json.dumps(
                {
                    "artifact-ref": artifact_ref,
                    "physical-artifact-name": artifact_name_value,
                    "artifact-instance-id": f"{batch['batch-id']}-artifact",
                    "run-id": batch_contracts.RUN_ID,
                    "run-attempt": batch_contracts.RUN_ATTEMPT,
                    "producer-boundary": "execution-batch",
                    "admission-source": (
                        control._CI_ORCHESTRATOR_STATE_ADMISSION_SOURCE
                    ),
                }
            ),
            encoding="utf-8",
        )
        staged_paths.append(destination)
    return staged_paths


def test_ci_batch_writer_writes_single_bundle() -> None:
    """Execution-batch matrix row writes one valid batch evidence bundle."""
    scratch = _ci_batch_bundle_scratch("single-batch-bundle")
    try:
        plan, manifest = _ci_batch_contract_plan_and_manifest()
        row = _ci_batch_matrix_rows(plan, manifest)[0]
        work_group_id = cast(
            "str",
            cast("dict[str, object]", row["identity-matrix"])["batch-id"],
        )
        batch = cast("list[dict[str, object]]", manifest["batches"])[0]
        selector = cast(
            "list[dict[str, object]]",
            batch["ordered-selectors"],
        )[0]
        result = _ci_success_validation_result(
            plan,
            cast("str", selector["work-group-id"]),
        )

        bundle = _write_ci_batch_bundle(scratch, plan, manifest, row, [result])

        validate_ci_validation_batch_evidence_bundle(
            bundle,
            plan=plan,
            request=cast(
                "dict[str, object]",
                batch_contracts.authorizing_context_kwargs()["request"],
            ),
            execution_batch_manifest=manifest,
            changed_files_snapshot=cast(
                "dict[str, object]",
                batch_contracts.authorizing_context_kwargs()[
                    "changed_files_snapshot"
                ],
            ),
            fact_snapshot=cast(
                "dict[str, object]",
                batch_contracts.authorizing_context_kwargs()["fact_snapshot"],
            ),
            expected_run_id=batch_contracts.RUN_ID,
            expected_run_attempt=batch_contracts.RUN_ATTEMPT,
        )
        assert bundle["batch"] == {
            "batch-id": batch["batch-id"],
            "runner-family": batch["runner-family"],
            "compatibility-profile": batch["compatibility-profile"],
            "depends-on-batches": batch["depends-on-batches"],
        }
        writer = cast("dict[str, object]", bundle["writer"])
        assert writer["observed-workflow"] == "CI Validation"
        assert writer["observed-job"] == "execution-batch"
        assert writer["observed-matrix"] == row["identity-matrix"]
        selector_results = cast(
            "list[dict[str, object]]",
            bundle["selector-results"],
        )
        assert len(selector_results) == 1
        assert selector_results[0]["outcome"] == "success"
        evidence = cast("dict[str, object]", selector_results[0]["evidence"])
        assert evidence["capability-results"]
        assert ci_validation_batch_evidence_bundle_payload_digest(bundle) in (
            scratch / "github-output.txt"
        ).read_text(encoding="utf-8")
        assert work_group_id.startswith("batch-")
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def _stage_ci_batch_bundle_artifact(
    observed_root: Path,
    bundle: Mapping[str, object],
    *,
    metadata_override: Mapping[str, object] | None = None,
) -> dict[str, object]:
    artifact_ref = cast("str", bundle["artifact-ref"])
    artifact_dir = observed_root / artifact_physical_name(artifact_ref)
    artifact_dir.mkdir(parents=True, exist_ok=True)
    (artifact_dir / "batch-evidence-bundle.json").write_text(
        json.dumps(bundle),
        encoding="utf-8",
    )
    batch = cast("Mapping[str, object]", bundle["batch"])
    metadata = {
        "artifact-ref": artifact_ref,
        "physical-artifact-name": artifact_physical_name(artifact_ref),
        "artifact-instance-id": f"{batch['batch-id']}-artifact",
        "run-id": batch_contracts.RUN_ID,
        "run-attempt": batch_contracts.RUN_ATTEMPT,
        "producer-boundary": "execution-batch",
        "admission-source": "github-actions-live-api",
    }
    if metadata_override is not None:
        metadata.update(metadata_override)
    (artifact_dir / "artifact-metadata.json").write_text(
        json.dumps(metadata),
        encoding="utf-8",
    )
    return metadata


def _ci_staged_downloader_admissions(
    observed_root: Path,
    manifest: Mapping[str, object],
) -> list[dict[str, object]]:
    admissions: list[dict[str, object]] = []
    batches_by_ref = {
        cast("str", batch["expected-batch-evidence-bundle-ref"]): cast(
            "str",
            batch["batch-id"],
        )
        for batch in cast("Sequence[Mapping[str, object]]", manifest["batches"])
    }
    for artifact_dir in sorted(
        observed_root.iterdir(),
        key=lambda path: path.name,
    ):
        if not artifact_dir.is_dir():
            continue
        metadata_path = artifact_dir / "artifact-metadata.json"
        try:
            metadata = json.loads(metadata_path.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError):
            continue
        if not isinstance(metadata, dict):
            continue
        artifact_ref = metadata.get("artifact-ref")
        artifact_instance_id = metadata.get("artifact-instance-id")
        physical_name = metadata.get("physical-artifact-name")
        admission_source = metadata.get("admission-source")
        if not all(
            isinstance(value, str) and value
            for value in (
                artifact_ref,
                artifact_instance_id,
                physical_name,
                admission_source,
            )
        ):
            continue
        if artifact_ref not in batches_by_ref:
            continue
        batch_id = batches_by_ref[artifact_ref]
        admissions.append(
            {
                "admission-source": admission_source,
                "artifact-instance-id": artifact_instance_id,
                "artifact-ref": artifact_ref,
                "batch-id": batch_id,
                "candidate-id": ci_validation_batch_evidence_candidate_id(
                    run_id=batch_contracts.RUN_ID,
                    run_attempt=batch_contracts.RUN_ATTEMPT,
                    batch_id=batch_id,
                    artifact_ref=artifact_ref,
                    artifact_instance_id=artifact_instance_id,
                    physical_artifact_name=physical_name,
                ),
                "physical-artifact-name": physical_name,
                "producer-boundary": "execution-batch",
                "run-attempt": batch_contracts.RUN_ATTEMPT,
                "run-id": batch_contracts.RUN_ID,
            }
        )
    return admissions


def _aggregate_ci_batch_evidence(  # noqa: C901, PLR0912, PLR0913, PLR0915
    scratch: Path,
    plan: dict[str, object],
    manifest: dict[str, object],
    observed_root: Path,
    *,
    request_path_override: Path | None = None,
    plan_path_override: Path | None = None,
    manifest_path_override: Path | None = None,
    manifest_override: dict[str, object] | None = None,
    request_text_override: str | None = None,
    plan_text_override: str | None = None,
    changed_files_text_override: str | None = None,
    fact_snapshot_text_override: str | None = None,
    manifest_text_override: str | None = None,
    expected_request_artifact_id: str | None = "7001",
    expected_plan_artifact_id: str | None = "8001",
    expected_changed_files_snapshot_artifact_id: str | None = "7101",
    expected_fact_snapshot_artifact_id: str | None = "7201",
    expected_execution_batch_manifest_artifact_id: str | None = "9001",
    run_artifacts: Sequence[GitHubActionsArtifactMetadata] | None = None,
    run_artifacts_error: Exception | None = None,
    started_at: str | None = None,
    created_at: str | None = None,
    write_downloader_observation: bool = True,
    aggregate_evidence_manifest_producer_verified: bool = True,
    aggregate_phase: str = "all",
) -> tuple[int, dict[str, object], dict[str, object]]:
    context = batch_contracts.authorizing_context_kwargs()
    plan_path = scratch / "aggregate-plan.json"
    request_path = scratch / "aggregate-request.json"
    changed_files_path = scratch / "aggregate-changed-files.json"
    fact_snapshot_path = scratch / "aggregate-fact-snapshot.json"
    manifest_path = scratch / "aggregate-execution-batch-manifest.json"
    aggregate_manifest_path = scratch / "aggregate-evidence-manifest.json"
    summary_path = scratch / "aggregate-summary.json"
    output_path = scratch / "aggregate-outputs.txt"
    if plan_path_override is not None:
        plan_path = plan_path_override
    else:
        plan_text = (
            plan_text_override
            if plan_text_override is not None
            else json.dumps(plan)
        )
        plan_path.write_text(plan_text, encoding="utf-8")
    request_path.write_text(
        request_text_override
        if request_text_override is not None
        else json.dumps(context["request"]),
        encoding="utf-8",
    )
    changed_files_path.write_text(
        changed_files_text_override
        if changed_files_text_override is not None
        else json.dumps(context["changed_files_snapshot"]),
        encoding="utf-8",
    )
    fact_snapshot_path.write_text(
        fact_snapshot_text_override
        if fact_snapshot_text_override is not None
        else json.dumps(context["fact_snapshot"]),
        encoding="utf-8",
    )
    manifest_path.write_text(
        manifest_text_override
        if manifest_text_override is not None
        else json.dumps(manifest_override or manifest),
        encoding="utf-8",
    )
    downloader_observation_path = (
        observed_root / control._CI_DOWNLOADER_OBSERVATION_FILE
    )
    if (
        write_downloader_observation
        and not downloader_observation_path.exists()
    ):
        downloader_observation_path.write_text(
            json.dumps(
                {
                    control._CI_DOWNLOADER_ADMITTED_BATCH_ARTIFACTS_KEY: (
                        _ci_staged_downloader_admissions(
                            observed_root,
                            manifest,
                        )
                    ),
                    "artifact-api-metadata-available": True,
                    "namespace-enumeration": "available",
                    "namespace-overflow": False,
                    "run-id": batch_contracts.RUN_ID,
                    "run-attempt": batch_contracts.RUN_ATTEMPT,
                }
            ),
            encoding="utf-8",
        )
    original_run_artifacts = control._github_actions_run_artifacts
    if run_artifacts_error is None and run_artifacts is None:
        default_run_artifacts = []
        if expected_request_artifact_id is not None:
            default_run_artifacts.append(
                _ci_artifact_metadata(
                    control.ci_validation_request_artifact_ref(
                        run_id=batch_contracts.RUN_ID,
                        run_attempt=batch_contracts.RUN_ATTEMPT,
                    ),
                    artifact_id=int(expected_request_artifact_id),
                )
            )
        if expected_plan_artifact_id is not None:
            default_run_artifacts.append(
                _ci_artifact_metadata(
                    control.ci_validation_plan_artifact_ref(
                        run_id=batch_contracts.RUN_ID,
                        run_attempt=batch_contracts.RUN_ATTEMPT,
                    ),
                    artifact_id=int(expected_plan_artifact_id),
                )
            )
        if expected_changed_files_snapshot_artifact_id is not None:
            default_run_artifacts.append(
                _ci_artifact_metadata(
                    control.ci_validation_changed_files_snapshot_artifact_ref(
                        run_id=batch_contracts.RUN_ID,
                        run_attempt=batch_contracts.RUN_ATTEMPT,
                    ),
                    artifact_id=int(
                        expected_changed_files_snapshot_artifact_id
                    ),
                )
            )
        if expected_fact_snapshot_artifact_id is not None:
            default_run_artifacts.append(
                _ci_artifact_metadata(
                    control.ci_validation_fact_snapshot_artifact_ref(
                        run_id=batch_contracts.RUN_ID,
                        run_attempt=batch_contracts.RUN_ATTEMPT,
                    ),
                    artifact_id=int(expected_fact_snapshot_artifact_id),
                )
            )
        if expected_execution_batch_manifest_artifact_id is not None:
            default_run_artifacts.append(
                _ci_artifact_metadata(
                    control.ci_validation_execution_batch_manifest_artifact_ref(
                        run_id=batch_contracts.RUN_ID,
                        run_attempt=batch_contracts.RUN_ATTEMPT,
                    ),
                    artifact_id=int(
                        expected_execution_batch_manifest_artifact_id
                    ),
                )
            )
        run_artifacts = default_run_artifacts
    if run_artifacts_error is not None:

        def raise_run_artifacts(**_kwargs: object) -> object:
            raise run_artifacts_error

        control._github_actions_run_artifacts = raise_run_artifacts
    elif run_artifacts is not None:
        control._github_actions_run_artifacts = lambda **_kwargs: list(
            run_artifacts
        )
    try:
        result = control._cmd_aggregate_ci_evidence(
            argparse.Namespace(
                repository="hcoona/three",
                workflow="CI Validation",
                run_id=batch_contracts.RUN_ID,
                run_attempt=batch_contracts.RUN_ATTEMPT,
                plan=str(plan_path),
                request=str(request_path_override or request_path),
                execution_batch_manifest=str(
                    manifest_path_override or manifest_path
                ),
                changed_files_snapshot=str(changed_files_path),
                fact_snapshot=str(fact_snapshot_path),
                observed_artifacts_dir=str(observed_root),
                expected_request_artifact_id=expected_request_artifact_id,
                expected_plan_artifact_id=expected_plan_artifact_id,
                expected_changed_files_snapshot_artifact_id=(
                    expected_changed_files_snapshot_artifact_id
                ),
                expected_fact_snapshot_artifact_id=(
                    expected_fact_snapshot_artifact_id
                ),
                expected_execution_batch_manifest_artifact_id=(
                    expected_execution_batch_manifest_artifact_id
                ),
                aggregate_evidence_manifest_artifact_id="aggregate-manifest-upload-id",
                aggregate_evidence_manifest_producer_verified=(
                    aggregate_evidence_manifest_producer_verified
                ),
                aggregate_phase=aggregate_phase,
                batch_materialization_failed=False,
                created_at=created_at or batch_contracts.CREATED_AT,
                started_at=started_at or batch_contracts.CREATED_AT,
                aggregate_evidence_manifest_out=str(aggregate_manifest_path),
                aggregate_summary_out=str(summary_path),
                github_output=str(output_path),
            )
        )
    finally:
        control._github_actions_run_artifacts = original_run_artifacts
    try:
        aggregate_manifest_text = (
            aggregate_manifest_path.read_text(encoding="utf-8")
            if aggregate_manifest_path.exists()
            else "{}"
        )
        aggregate_manifest = json.loads(aggregate_manifest_text)
    except json.JSONDecodeError:
        aggregate_manifest = {}
    summary = (
        json.loads(summary_path.read_text(encoding="utf-8"))
        if summary_path.exists()
        else {}
    )
    expected_manifest_digest = (
        ci_validation_aggregate_evidence_manifest_payload_digest(
            aggregate_manifest
        )
    )
    if summary:
        aggregate_instance_id = summary["aggregate-evidence-manifest"][
            "artifact-instance-id"
        ]
        if aggregate_instance_id is not None:
            assert aggregate_instance_id == "aggregate-manifest-upload-id"
        summary_manifest_digest = cast(
            "dict[str, object]", summary["aggregate-evidence-manifest"]
        ).get("content-digest")
        if isinstance(summary_manifest_digest, str):
            expected_manifest_digest = summary_manifest_digest
        else:
            expected_manifest_digest = ""
    assert (
        _github_outputs(output_path)[
            "aggregate_evidence_manifest_payload_digest"
        ]
        == expected_manifest_digest
    )
    return result, aggregate_manifest, summary


def _write_empty_ci_downloader_observation(observed_root: Path) -> None:
    observed_root.mkdir(parents=True, exist_ok=True)
    (observed_root / control._CI_DOWNLOADER_OBSERVATION_FILE).write_text(
        json.dumps(
            {
                control._CI_DOWNLOADER_ADMITTED_BATCH_ARTIFACTS_KEY: [],
                "artifact-api-metadata-available": True,
                "namespace-enumeration": "available",
                "namespace-overflow": False,
                "run-id": batch_contracts.RUN_ID,
                "run-attempt": batch_contracts.RUN_ATTEMPT,
            }
        ),
        encoding="utf-8",
    )


def test_ci_batch_aggregation_materialization_missing_fails_closed(
    tmp_path: Path,
) -> None:
    """Aggregate emits G5 evidence when batch materialization is absent."""
    plan = cast("dict[str, object]", batch_contracts.plan())
    plan_path = tmp_path / "validation-plan.json"
    request_path = tmp_path / "ci-validation-request.json"
    changed_files_path = tmp_path / "changed-files.json"
    fact_snapshot_path = tmp_path / "fact-snapshot.json"
    observed_root = tmp_path / "observed-artifacts"
    aggregate_manifest_path = tmp_path / "aggregate-evidence-manifest.json"
    summary_path = tmp_path / "aggregate-summary.json"
    output_path = tmp_path / "outputs.txt"
    for path, document in (
        (plan_path, plan),
        (request_path, batch_contracts.request_document()),
        (changed_files_path, batch_contracts.changed_files_snapshot_document()),
        (fact_snapshot_path, batch_contracts.fact_snapshot_document()),
    ):
        path.write_text(json.dumps(document), encoding="utf-8")
    _write_empty_ci_downloader_observation(observed_root)

    aggregate_args = argparse.Namespace(
        repository="hcoona/three",
        workflow="CI Validation",
        run_id=batch_contracts.RUN_ID,
        run_attempt=batch_contracts.RUN_ATTEMPT,
        plan=str(plan_path),
        request=str(request_path),
        execution_batch_manifest="",
        changed_files_snapshot=str(changed_files_path),
        fact_snapshot=str(fact_snapshot_path),
        assignments="",
        observed_artifacts_dir=str(observed_root),
        expected_request_artifact_id=None,
        expected_plan_artifact_id=None,
        expected_changed_files_snapshot_artifact_id=None,
        expected_fact_snapshot_artifact_id=None,
        expected_execution_batch_manifest_artifact_id="",
        aggregate_evidence_manifest_artifact_id="aggregate-upload-id",
        aggregate_phase="evidence",
        batch_materialization_failed=True,
        created_at=batch_contracts.CREATED_AT,
        started_at=batch_contracts.CREATED_AT,
        aggregate_evidence_manifest_out=str(aggregate_manifest_path),
        aggregate_summary_out=str(summary_path),
        github_output=str(output_path),
    )
    evidence_result = control._cmd_aggregate_ci_evidence(aggregate_args)

    aggregate_manifest = json.loads(
        aggregate_manifest_path.read_text(encoding="utf-8"),
    )
    execution_input = aggregate_manifest["input-artifacts"][
        "execution-batch-manifest"
    ]
    assert evidence_result == 0
    assert execution_input["admissibility"] == "missing"
    assert not summary_path.exists()

    aggregate_args.aggregate_phase = "summary"
    result = control._cmd_aggregate_ci_evidence(aggregate_args)

    aggregate_manifest = json.loads(
        aggregate_manifest_path.read_text(encoding="utf-8"),
    )
    summary = json.loads(summary_path.read_text(encoding="utf-8"))
    execution_input = aggregate_manifest["input-artifacts"][
        "execution-batch-manifest"
    ]
    assert result == 1
    assert execution_input["admissibility"] == "missing"
    assert summary["verdict"] == "failed"
    assert summary["reason"]["required-input-artifact-failure"] is True
    assert (
        summary["aggregate-evidence-manifest"]["artifact-instance-id"]
        == "aggregate-upload-id"
    )


def test_ci_batch_missing_manifest_preserves_authority_diagnostics() -> None:
    """Missing-manifest fallback preserves stale final manifest diagnostics."""
    scratch = _ci_batch_bundle_scratch("missing-manifest-authority-diagnostics")
    plan = cast("dict[str, object]", batch_contracts.plan())
    try:
        plan_path = scratch / "validation-plan.json"
        request_path = scratch / "ci-validation-request.json"
        changed_files_path = scratch / "changed-files.json"
        fact_snapshot_path = scratch / "fact-snapshot.json"
        aggregate_manifest_path = scratch / "aggregate-evidence-manifest.json"
        summary_path = scratch / "aggregate-summary.json"
        output_path = scratch / "outputs.txt"
        observed_root = scratch / "observed-artifacts"
        for path, document in (
            (plan_path, plan),
            (request_path, batch_contracts.request_document()),
            (
                changed_files_path,
                batch_contracts.changed_files_snapshot_document(),
            ),
            (fact_snapshot_path, batch_contracts.fact_snapshot_document()),
        ):
            path.write_text(json.dumps(document), encoding="utf-8")
        _write_empty_ci_downloader_observation(observed_root)
        args = argparse.Namespace(
            repository="hcoona/three",
            workflow="CI Validation",
            run_id=batch_contracts.RUN_ID,
            run_attempt=batch_contracts.RUN_ATTEMPT,
            plan=str(plan_path),
            request=str(request_path),
            execution_batch_manifest="",
            changed_files_snapshot=str(changed_files_path),
            fact_snapshot=str(fact_snapshot_path),
            assignments="",
            observed_artifacts_dir=str(observed_root),
            expected_request_artifact_id=None,
            expected_plan_artifact_id=None,
            expected_changed_files_snapshot_artifact_id=None,
            expected_fact_snapshot_artifact_id=None,
            expected_execution_batch_manifest_artifact_id="",
            aggregate_evidence_manifest_artifact_id="aggregate-upload-id",
            aggregate_phase="evidence",
            batch_materialization_failed=True,
            created_at=batch_contracts.CREATED_AT,
            started_at=batch_contracts.CREATED_AT,
            aggregate_evidence_manifest_out=str(aggregate_manifest_path),
            aggregate_summary_out=str(summary_path),
            github_output=str(output_path),
        )
        assert control._cmd_aggregate_ci_evidence(args) == 0
        preserved_manifest = json.loads(
            aggregate_manifest_path.read_text(encoding="utf-8")
        )
        preserved_manifest["created-at"] = "2026-05-14T21:10:00Z"
        aggregate_manifest_path.write_bytes(
            canonical_json_bytes(preserved_manifest)
        )

        args.aggregate_phase = "summary"
        result = control._cmd_aggregate_ci_evidence(args)

        summary = json.loads(summary_path.read_text(encoding="utf-8"))
        final_artifacts = cast(
            "dict[str, dict[str, object]]",
            summary["final-artifacts"],
        )
        failures = cast("list[dict[str, object]]", summary["failures"])
        reason = cast("dict[str, object]", summary["reason"])
        assert result == 1
        assert (
            final_artifacts["aggregate-evidence-manifest"]["producer-verified"]
            is False
        )
        assert reason["final-evidence-failure"] is True
        assert any(
            failure["kind"] == "final-evidence-failure"
            and cast("dict[str, object]", failure["diagnostic"])["detail"]
            == "aggregate-evidence-manifest-digest-mismatch"
            for failure in failures
        )
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_batch_manifest_bundle_ref_mismatch_detail() -> None:
    """Manifest bundle-ref mismatches use the registered G5 detail."""
    scratch = _ci_batch_bundle_scratch("manifest-bundle-ref-mismatch-detail")
    try:
        plan, manifest = _ci_batch_contract_plan_and_manifest()
        bad_manifest = deepcopy(manifest)
        batch = cast("list[dict[str, object]]", bad_manifest["batches"])[0]
        batch["expected-batch-evidence-bundle-ref"] = (
            "ci-validation/batch-evidence/25887422010/1/batch-forged/"
            "batch-evidence-bundle.json"
        )

        observed_root = scratch / "observed-artifacts"
        observed_root.mkdir()
        result, _aggregate_manifest, summary = _aggregate_ci_batch_evidence(
            scratch,
            plan,
            manifest,
            observed_root,
            manifest_override=bad_manifest,
        )

        execution_input = cast(
            "dict[str, object]",
            cast("dict[str, object]", _aggregate_manifest["input-artifacts"])[
                "execution-batch-manifest"
            ],
        )
        diagnostics = cast(
            "list[dict[str, object]]", execution_input["diagnostics"]
        )
        assert result == 1
        assert summary["verdict"] == "failed"
        assert diagnostics[0]["detail"] == (
            "execution-batch-manifest-bundle-ref-mismatch"
        )
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_batch_manifest_unreadable_detail(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Unreadable manifest paths are distinct from missing manifests."""
    scratch = _ci_batch_bundle_scratch("manifest-unreadable-detail")
    try:
        plan, manifest = _ci_batch_contract_plan_and_manifest()
        original_read_json = control._read_json

        def read_json(path: Path) -> dict[str, Any]:
            if path.name == "aggregate-execution-batch-manifest.json":
                raise PermissionError(13, "permission denied", str(path))
            return original_read_json(path)

        monkeypatch.setattr(control, "_read_json", read_json)

        observed_root = scratch / "observed-artifacts"
        observed_root.mkdir()
        result, aggregate_manifest, _summary = _aggregate_ci_batch_evidence(
            scratch,
            plan,
            manifest,
            observed_root,
        )

        execution_input = cast(
            "dict[str, object]",
            cast("dict[str, object]", aggregate_manifest["input-artifacts"])[
                "execution-batch-manifest"
            ],
        )
        diagnostics = cast(
            "list[dict[str, object]]", execution_input["diagnostics"]
        )
        assert result == 1
        assert execution_input["admissibility"] == "inadmissible"
        assert diagnostics[0]["detail"] == "execution-batch-manifest-unreadable"
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_batch_aggregation_writes_final_outputs_as_canonical_json() -> None:
    """Final aggregate evidence/summary files use canonical JSON bytes."""
    scratch = _ci_batch_bundle_scratch("batch-aggregation-canonical-final-json")
    try:
        observed_root = scratch / "observed"
        observed_root.mkdir()
        plan, manifest = _ci_batch_contract_plan_and_manifest()

        _result, aggregate_manifest, summary = _aggregate_ci_batch_evidence(
            scratch,
            plan,
            manifest,
            observed_root,
        )

        aggregate_manifest_path = scratch / "aggregate-evidence-manifest.json"
        summary_path = scratch / "aggregate-summary.json"
        assert aggregate_manifest_path.read_bytes() == canonical_json_bytes(
            aggregate_manifest
        )
        assert summary_path.read_bytes() == canonical_json_bytes(summary)
        assert b"\n  " not in aggregate_manifest_path.read_bytes()
        assert b"\n  " not in summary_path.read_bytes()
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_batch_summary_uses_preserved_evidence_manifest_bytes() -> None:
    """Summary digest binds to the preserved evidence-phase manifest bytes."""
    scratch = _ci_batch_bundle_scratch("batch-aggregation-preserved-manifest")
    try:
        observed_root = scratch / "observed"
        observed_root.mkdir()
        plan, manifest = _ci_batch_contract_plan_and_manifest()

        _aggregate_ci_batch_evidence(
            scratch,
            plan,
            manifest,
            observed_root,
        )
        aggregate_manifest_path = scratch / "aggregate-evidence-manifest.json"
        preserved_manifest = json.loads(
            aggregate_manifest_path.read_text(encoding="utf-8")
        )
        preserved_manifest["created-at"] = "2026-05-14T21:10:00Z"
        preserved_bytes = canonical_json_bytes(preserved_manifest)
        aggregate_manifest_path.write_bytes(preserved_bytes)

        result, _aggregate_manifest, summary = _aggregate_ci_batch_evidence(
            scratch,
            plan,
            manifest,
            observed_root,
            aggregate_phase="summary",
        )

        failures = cast("list[dict[str, object]]", summary["failures"])
        final_artifacts = cast(
            "dict[str, dict[str, object]]", summary["final-artifacts"]
        )
        assert result == 1
        assert aggregate_manifest_path.read_bytes() == preserved_bytes
        assert (
            final_artifacts["aggregate-evidence-manifest"]["content-digest"]
            == hashlib.sha256(preserved_bytes).hexdigest()
        )
        assert any(
            failure["kind"] == "final-producer-unverified"
            and cast("dict[str, object]", failure["diagnostic"])["detail"]
            == "final-producer-unverified"
            for failure in failures
        )
        assert not any(
            failure["kind"] == "final-evidence-failure"
            and cast("dict[str, object]", failure["diagnostic"])["detail"]
            == "final-producer-unverified"
            for failure in failures
        )
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_batch_summary_recomputes_manifest_with_evidence_timestamp() -> None:
    """Two-phase summary separates manifest and completion timestamps."""
    scratch = _ci_batch_bundle_scratch("batch-aggregation-two-phase-timestamps")
    try:
        observed_root = scratch / "observed"
        observed_root.mkdir()
        plan, manifest = _ci_batch_contract_plan_and_manifest()
        aggregate_started_at = batch_contracts.CREATED_AT
        aggregate_completed_at = "2026-05-14T21:11:00Z"

        evidence_result, preserved_manifest, _evidence_summary = (
            _aggregate_ci_batch_evidence(
                scratch,
                plan,
                manifest,
                observed_root,
                aggregate_phase="evidence",
                started_at=aggregate_started_at,
                created_at=aggregate_started_at,
            )
        )
        result, aggregate_manifest, summary = _aggregate_ci_batch_evidence(
            scratch,
            plan,
            manifest,
            observed_root,
            aggregate_phase="summary",
            started_at=aggregate_started_at,
            created_at=aggregate_completed_at,
        )

        assert evidence_result == 0
        assert result == 1
        assert aggregate_manifest == preserved_manifest
        assert summary["created-at"] == aggregate_completed_at
        assert aggregate_manifest["created-at"] == aggregate_started_at
        budgets = cast("dict[str, object]", summary["budgets"])
        assert budgets["aggregate-duration-seconds"] == 99
        assert summary["reason"]["aggregate-duration-exceeded"] is False
        assert summary["reason"]["final-evidence-failure"] is False
        assert all(
            failure["kind"] != "final-evidence-failure"
            for failure in cast("list[dict[str, object]]", summary["failures"])
        )
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_batch_summary_falls_back_for_non_rfc3339_preserved_created_at() -> (
    None
):
    """Parseable non-RFC3339 manifest timestamps fail closed, not abort."""
    scratch = _ci_batch_bundle_scratch(
        "batch-aggregation-non-rfc3339-manifest-created-at"
    )
    try:
        observed_root = scratch / "observed"
        observed_root.mkdir()
        plan, manifest = _ci_batch_contract_plan_and_manifest()
        aggregate_started_at = batch_contracts.CREATED_AT
        aggregate_completed_at = "2026-05-14T21:11:00Z"

        _aggregate_ci_batch_evidence(
            scratch,
            plan,
            manifest,
            observed_root,
            aggregate_phase="evidence",
            started_at=aggregate_started_at,
            created_at=aggregate_started_at,
        )
        aggregate_manifest_path = scratch / "aggregate-evidence-manifest.json"
        preserved_manifest = json.loads(
            aggregate_manifest_path.read_text(encoding="utf-8")
        )
        preserved_manifest["created-at"] = "2026-05-14 21:10:00+00:00"
        aggregate_manifest_path.write_bytes(
            canonical_json_bytes(preserved_manifest)
        )

        result, _aggregate_manifest, summary = _aggregate_ci_batch_evidence(
            scratch,
            plan,
            manifest,
            observed_root,
            aggregate_phase="summary",
            started_at=aggregate_started_at,
            created_at=aggregate_completed_at,
        )

        budgets = cast("dict[str, object]", summary["budgets"])
        failures = cast("list[dict[str, object]]", summary["failures"])
        assert result == 1
        assert summary["verdict"] == "failed"
        assert summary["created-at"] == aggregate_completed_at
        assert budgets["aggregate-duration-seconds"] == 99
        assert any(
            failure["kind"] == "final-evidence-failure"
            and cast("dict[str, object]", failure["diagnostic"])["detail"]
            == "aggregate-evidence-manifest-malformed"
            for failure in failures
        )
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_batch_summary_ignores_invalid_started_at_for_envelopes() -> None:
    """Invalid started-at only affects duration evidence."""
    scratch = _ci_batch_bundle_scratch(
        "batch-aggregation-invalid-start-envelope"
    )
    try:
        observed_root = scratch / "observed"
        observed_root.mkdir()
        plan, manifest = _ci_batch_contract_plan_and_manifest()
        completed_at = "2026-05-14T21:10:00Z"
        _aggregate_ci_batch_evidence(
            scratch,
            plan,
            manifest,
            observed_root,
            aggregate_phase="evidence",
        )

        result, aggregate_manifest, summary = _aggregate_ci_batch_evidence(
            scratch,
            plan,
            manifest,
            observed_root,
            aggregate_phase="summary",
            started_at="not-a-timestamp",
            created_at=completed_at,
        )

        budgets = cast("dict[str, object]", summary["budgets"])
        assert result == 1
        assert aggregate_manifest["created-at"] == batch_contracts.CREATED_AT
        assert summary["created-at"] == completed_at
        assert budgets["aggregate-duration-seconds"] == 121
        assert (
            cast("dict[str, object]", summary["reason"])[
                "aggregate-duration-exceeded"
            ]
            is True
        )
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


@pytest.mark.parametrize(
    (
        "case",
        "rewrite",
        "expected_detail",
        "expected_digest",
        "expected_instance_id",
    ),
    [
        (
            "noncanonical",
            "pretty",
            "aggregate-evidence-manifest-non-canonical",
            "raw",
            "aggregate-manifest-upload-id",
        ),
        (
            "malformed",
            "{",
            "aggregate-evidence-manifest-malformed",
            "raw",
            "aggregate-manifest-upload-id",
        ),
        (
            "canonical-malformed",
            "{}",
            "aggregate-evidence-manifest-malformed",
            "raw",
            "aggregate-manifest-upload-id",
        ),
        (
            "wrong-artifact-ref",
            "wrong-artifact-ref",
            "aggregate-evidence-manifest-malformed",
            "raw",
            "aggregate-manifest-upload-id",
        ),
        ("missing", None, "aggregate-summary-without-manifest", None, None),
    ],
)
def test_ci_batch_summary_fails_closed_for_invalid_preserved_manifest(  # noqa: PLR0912, PLR0915
    case: str,
    rewrite: str | None,
    expected_detail: str,
    expected_digest: str | None,
    expected_instance_id: str | None,
) -> None:
    """Summary never binds an upload id to a recomputed manifest digest."""
    scratch = _ci_batch_bundle_scratch(f"batch-aggregation-{case}-manifest")
    try:
        observed_root = scratch / "observed"
        observed_root.mkdir()
        plan, manifest = _ci_batch_contract_plan_and_manifest()
        _aggregate_ci_batch_evidence(
            scratch,
            plan,
            manifest,
            observed_root,
            aggregate_phase="evidence",
        )
        aggregate_manifest_path = scratch / "aggregate-evidence-manifest.json"
        if rewrite == "pretty":
            preserved = json.loads(
                aggregate_manifest_path.read_text(encoding="utf-8")
            )
            raw_bytes = json.dumps(preserved, indent=2).encode()
            aggregate_manifest_path.write_bytes(raw_bytes)
        elif rewrite is None:
            raw_bytes = b""
            aggregate_manifest_path.unlink()
        elif rewrite == "wrong-artifact-ref":
            preserved = json.loads(
                aggregate_manifest_path.read_text(encoding="utf-8")
            )
            preserved["artifact-ref"] = (
                "ci-validation/aggregate/stale-run/1/"
                "aggregate-evidence-manifest.json"
            )
            raw_bytes = canonical_json_bytes(preserved)
            aggregate_manifest_path.write_bytes(raw_bytes)
        else:
            raw_bytes = rewrite.encode()
            aggregate_manifest_path.write_bytes(raw_bytes)

        result, _aggregate_manifest, summary = _aggregate_ci_batch_evidence(
            scratch,
            plan,
            manifest,
            observed_root,
            aggregate_phase="summary",
        )

        final_manifest = cast(
            "dict[str, object]",
            cast("dict[str, object]", summary["final-artifacts"])[
                "aggregate-evidence-manifest"
            ],
        )
        failures = cast("list[dict[str, object]]", summary["failures"])
        reason = cast("dict[str, object]", summary["reason"])
        authority_diagnostics = cast(
            "list[dict[str, object]]",
            final_manifest["authority-diagnostics"],
        )
        details = {
            cast("dict[str, object]", failure["diagnostic"])["detail"]
            for failure in failures
            if failure["kind"] == "final-evidence-failure"
        }
        assert result == 1
        assert summary["verdict"] == "failed"
        assert final_manifest["artifact-ref"] == (
            control.ci_validation_aggregate_evidence_manifest_artifact_ref(
                run_id=batch_contracts.RUN_ID,
                run_attempt=batch_contracts.RUN_ATTEMPT,
            )
        )
        assert final_manifest["producer-verified"] is False
        assert final_manifest["artifact-instance-id"] == expected_instance_id
        if expected_digest == "raw":
            assert (
                final_manifest["content-digest"]
                == hashlib.sha256(raw_bytes).hexdigest()
            )
        else:
            assert final_manifest["content-digest"] is None
        if expected_detail == "aggregate-summary-without-manifest":
            assert expected_detail not in details
        else:
            assert expected_detail in details
        if expected_detail == "aggregate-summary-without-manifest":
            assert reason["aggregate-summary-without-manifest"] is True
            expected_kind = "aggregate-summary-without-manifest"
        else:
            assert reason["fail-closed"] is False
            assert reason["final-evidence-failure"] is True
            expected_kind = "final-evidence-failure"
        assert any(
            failure["kind"] == expected_kind
            and cast("dict[str, object]", failure["diagnostic"])["detail"]
            == expected_detail
            for failure in failures
        )
        if rewrite is None:
            assert "aggregate-evidence-manifest-missing" in {
                diagnostic["detail"] for diagnostic in authority_diagnostics
            }
            assert not aggregate_manifest_path.exists()
        else:
            assert expected_detail in {
                diagnostic["detail"] for diagnostic in authority_diagnostics
            }
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_batch_summary_fails_closed_for_unverified_manifest() -> None:
    """Summary does not claim unverified evidence as producer-bound."""
    scratch = _ci_batch_bundle_scratch("batch-aggregation-unverified-final")
    try:
        observed_root = scratch / "observed"
        observed_root.mkdir()
        plan, manifest = _ci_batch_contract_plan_and_manifest()

        _aggregate_ci_batch_evidence(
            scratch,
            plan,
            manifest,
            observed_root,
            aggregate_phase="evidence",
        )
        result, _aggregate_manifest, summary = _aggregate_ci_batch_evidence(
            scratch,
            plan,
            manifest,
            observed_root,
            aggregate_evidence_manifest_producer_verified=False,
            aggregate_phase="summary",
        )

        reason = cast("dict[str, object]", summary["reason"])
        final_artifacts = cast(
            "dict[str, dict[str, object]]", summary["final-artifacts"]
        )
        failures = cast("list[dict[str, object]]", summary["failures"])
        assert result == 1
        assert summary["verdict"] == "failed"
        assert reason["final-producer-unverified"] is True
        assert reason["final-evidence-failure"] is False
        assert (
            final_artifacts["aggregate-evidence-manifest"]["producer-verified"]
            is False
        )
        assert any(
            failure["kind"] == "final-producer-unverified"
            and cast("dict[str, object]", failure["diagnostic"])["detail"]
            == "final-producer-unverified"
            for failure in failures
        )
        assert not any(
            failure["kind"] == "final-evidence-failure" for failure in failures
        )
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_batch_missing_execution_summary_falls_back_for_malformed_manifest() -> (  # noqa: E501
    None
):
    """Missing-execution summary ignores canonical malformed final manifests."""
    scratch = _ci_batch_bundle_scratch(
        "missing-execution-canonical-malformed-manifest"
    )
    plan = cast("dict[str, object]", batch_contracts.plan())
    try:
        plan_path = scratch / "validation-plan.json"
        request_path = scratch / "ci-validation-request.json"
        changed_files_path = scratch / "changed-files.json"
        fact_snapshot_path = scratch / "fact-snapshot.json"
        aggregate_manifest_path = scratch / "aggregate-evidence-manifest.json"
        summary_path = scratch / "aggregate-summary.json"
        output_path = scratch / "outputs.txt"
        observed_root = scratch / "observed-artifacts"
        for path, document in (
            (plan_path, plan),
            (request_path, batch_contracts.request_document()),
            (
                changed_files_path,
                batch_contracts.changed_files_snapshot_document(),
            ),
            (fact_snapshot_path, batch_contracts.fact_snapshot_document()),
        ):
            path.write_text(json.dumps(document), encoding="utf-8")
        _write_empty_ci_downloader_observation(observed_root)
        args = argparse.Namespace(
            repository="hcoona/three",
            workflow="CI Validation",
            run_id=batch_contracts.RUN_ID,
            run_attempt=batch_contracts.RUN_ATTEMPT,
            plan=str(plan_path),
            request=str(request_path),
            execution_batch_manifest="",
            changed_files_snapshot=str(changed_files_path),
            fact_snapshot=str(fact_snapshot_path),
            assignments="",
            observed_artifacts_dir=str(observed_root),
            expected_request_artifact_id=None,
            expected_plan_artifact_id=None,
            expected_changed_files_snapshot_artifact_id=None,
            expected_fact_snapshot_artifact_id=None,
            expected_execution_batch_manifest_artifact_id="",
            aggregate_evidence_manifest_artifact_id="aggregate-upload-id",
            aggregate_phase="evidence",
            batch_materialization_failed=True,
            created_at=batch_contracts.CREATED_AT,
            started_at=batch_contracts.CREATED_AT,
            aggregate_evidence_manifest_out=str(aggregate_manifest_path),
            aggregate_summary_out=str(summary_path),
            github_output=str(output_path),
        )
        assert control._cmd_aggregate_ci_evidence(args) == 0
        aggregate_manifest_path.write_bytes(canonical_json_bytes({}))

        args.aggregate_phase = "summary"
        result = control._cmd_aggregate_ci_evidence(args)

        summary = json.loads(summary_path.read_text(encoding="utf-8"))
        final_artifacts = cast(
            "dict[str, dict[str, object]]",
            summary["final-artifacts"],
        )
        failures = cast("list[dict[str, object]]", summary["failures"])
        assert result == 1
        assert summary["verdict"] == "failed"
        assert final_artifacts["aggregate-evidence-manifest"][
            "artifact-ref"
        ] == control.ci_validation_aggregate_evidence_manifest_artifact_ref(
            run_id=batch_contracts.RUN_ID,
            run_attempt=batch_contracts.RUN_ATTEMPT,
        )
        assert any(
            failure["kind"] == "final-evidence-failure"
            and cast("dict[str, object]", failure["diagnostic"])["detail"]
            == "aggregate-evidence-manifest-malformed"
            for failure in failures
        )
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_batch_aggregation_missing_plan_writes_invalid_g5_artifacts() -> (
    None
):
    """Missing batch plans still emit schema-valid fail-closed G5 artifacts."""
    scratch = _ci_batch_bundle_scratch("batch-aggregation-missing-plan")
    try:
        observed_root = scratch / "observed"
        observed_root.mkdir()
        aggregate_manifest_path = scratch / "aggregate-evidence-manifest.json"
        summary_path = scratch / "aggregate-summary.json"
        output_path = scratch / "outputs.txt"
        args = argparse.Namespace(
            repository="hcoona/three",
            workflow="CI Validation",
            run_id=batch_contracts.RUN_ID,
            run_attempt=batch_contracts.RUN_ATTEMPT,
            plan=str(scratch / "missing-validation-plan.json"),
            request="",
            execution_batch_manifest=str(
                scratch / "missing-execution-batch-manifest.json"
            ),
            changed_files_snapshot="",
            fact_snapshot="",
            assignments="",
            observed_artifacts_dir=str(observed_root),
            expected_request_artifact_id=None,
            expected_plan_artifact_id=None,
            expected_changed_files_snapshot_artifact_id=None,
            expected_fact_snapshot_artifact_id=None,
            expected_execution_batch_manifest_artifact_id=None,
            aggregate_evidence_manifest_artifact_id="aggregate-upload-id",
            aggregate_phase="evidence",
            batch_materialization_failed=False,
            created_at=batch_contracts.CREATED_AT,
            started_at=batch_contracts.CREATED_AT,
            aggregate_evidence_manifest_out=str(aggregate_manifest_path),
            aggregate_summary_out=str(summary_path),
            github_output=str(output_path),
        )

        evidence_result = control._cmd_aggregate_ci_evidence(args)

        aggregate_manifest = json.loads(
            aggregate_manifest_path.read_text(encoding="utf-8"),
        )
        validate_ci_validation_aggregate_evidence_manifest(aggregate_manifest)
        assert evidence_result == 0
        assert not summary_path.exists()
        assert _github_outputs(output_path)["verdict"] == "failed"

        args.aggregate_phase = "summary"
        result = control._cmd_aggregate_ci_evidence(args)

        aggregate_manifest = json.loads(
            aggregate_manifest_path.read_text(encoding="utf-8"),
        )
        summary = json.loads(summary_path.read_text(encoding="utf-8"))
        validate_ci_validation_aggregate_evidence_manifest(aggregate_manifest)
        validate_ci_validation_aggregate_summary(summary)
        final_manifest = cast(
            "dict[str, object]",
            cast("dict[str, object]", summary["final-artifacts"])[
                "aggregate-evidence-manifest"
            ],
        )
        assert result == 1
        assert summary["verdict"] == "failed"
        assert summary["reason"]["invalid-plan"] is True
        assert (
            summary["aggregate-evidence-manifest"]["artifact-instance-id"]
            == "aggregate-upload-id"
        )
        assert summary["aggregate-evidence-manifest"][
            "content-digest"
        ] == control.ci_validation_aggregate_evidence_manifest_payload_digest(
            aggregate_manifest
        )
        assert final_manifest["producer-verified"] is True
        assert final_manifest["authority-diagnostics"] == []
        failures = cast("list[dict[str, object]]", summary["failures"])
        assert any(
            failure["kind"] == "invalid-plan"
            and cast("dict[str, object]", failure["diagnostic"])["detail"]
            == "plan-missing"
            for failure in failures
        )
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_batch_invalid_plan_closes_authority() -> None:  # noqa: PLR0915
    """Digest-valid structural plan failures cannot admit batch evidence."""
    scratch = _ci_batch_bundle_scratch(
        "batch-aggregation-structurally-invalid-plan"
    )
    try:
        observed_root = scratch / "observed"
        observed_root.mkdir()
        valid_plan, valid_manifest = _ci_batch_contract_plan_and_manifest()
        invalid_plan = deepcopy(valid_plan)
        invalid_manifest = deepcopy(valid_manifest)
        validation_obligations = cast(
            "list[dict[str, object]]",
            invalid_plan["validation-obligations"],
        )
        validation_obligations[0]["expected-evidence-id"] = "missing-evidence"
        invalid_plan["plan-digest"] = ci_validation_plan_digest(invalid_plan)
        invalid_manifest["plan-digest"] = invalid_plan["plan-digest"]
        row = _ci_batch_matrix_rows(valid_plan, valid_manifest)[0]
        batch = cast("list[dict[str, object]]", valid_manifest["batches"])[0]
        selector = cast(
            "list[dict[str, object]]",
            batch["ordered-selectors"],
        )[0]
        result_document = _ci_success_validation_result(
            valid_plan,
            cast("str", selector["work-group-id"]),
        )
        bundle = _write_ci_batch_bundle(
            scratch,
            valid_plan,
            valid_manifest,
            row,
            [result_document],
        )
        _stage_ci_batch_bundle_artifact(observed_root, bundle)

        result, aggregate_manifest, summary = _aggregate_ci_batch_evidence(
            scratch,
            invalid_plan,
            invalid_manifest,
            observed_root,
            expected_execution_batch_manifest_artifact_id=None,
        )

        validate_ci_validation_aggregate_evidence_manifest(aggregate_manifest)
        validate_ci_validation_aggregate_summary(summary)
        inputs = cast(
            "Mapping[str, Mapping[str, object]]",
            aggregate_manifest["input-artifacts"],
        )
        plan_input = inputs["validation-plan"]
        plan_diagnostics = cast(
            "Sequence[Mapping[str, object]]",
            plan_input["diagnostics"],
        )
        execution_input = inputs["execution-batch-manifest"]
        reason = cast("Mapping[str, object]", summary["reason"])
        work_groups = cast("Mapping[str, object]", summary["work-groups"])
        budgets = cast("Mapping[str, object]", summary["budgets"])

        assert result == 1
        assert summary["verdict"] == "failed"
        assert reason["invalid-plan"] is True
        assert plan_input["admissibility"] == "inadmissible"
        assert plan_input["content-digest"] == invalid_plan["plan-digest"]
        assert plan_diagnostics[0]["code"] == "invalid-plan"
        assert plan_diagnostics[0]["detail"] == "structurally-invalid"
        assert execution_input["admissibility"] == "missing"
        assert execution_input["artifact-instance-id"] is None
        assert execution_input["content-digest"] is None
        assert aggregate_manifest["batch-bundles"] == []
        assert summary["batch-bundles"] == []
        assert summary["evidence-results"] == []
        assert budgets["actual-execution-batches"] == 0
        assert budgets["actual-total-jobs"] == 0
        assert work_groups["executable-required"] == 0
        assert work_groups["required-succeeded"] == 0
        assert work_groups["required-failed"] == 0
        assert work_groups["required-skipped"] == 0
        assert work_groups["required-missing"] == 0
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


@pytest.mark.parametrize(
    ("case", "rewrite", "expected_detail", "expected_digest"),
    [
        ("missing", None, "aggregate-evidence-manifest-missing", None),
        (
            "malformed",
            "{",
            "aggregate-evidence-manifest-malformed",
            "raw",
        ),
    ],
)
def test_ci_batch_missing_plan_summary_binds_manifest_authority_failures(
    case: str,
    rewrite: str | None,
    expected_detail: str,
    expected_digest: str | None,
) -> None:
    """No-plan summary binds preserved final manifest authority failures."""
    scratch = _ci_batch_bundle_scratch(
        f"batch-aggregation-missing-plan-{case}-manifest"
    )
    try:
        observed_root = scratch / "observed"
        observed_root.mkdir()
        aggregate_manifest_path = scratch / "aggregate-evidence-manifest.json"
        summary_path = scratch / "aggregate-summary.json"
        output_path = scratch / "outputs.txt"
        args = argparse.Namespace(
            repository="hcoona/three",
            workflow="CI Validation",
            run_id=batch_contracts.RUN_ID,
            run_attempt=batch_contracts.RUN_ATTEMPT,
            plan=str(scratch / "missing-validation-plan.json"),
            request="",
            execution_batch_manifest=str(
                scratch / "missing-execution-batch-manifest.json"
            ),
            changed_files_snapshot="",
            fact_snapshot="",
            assignments="",
            observed_artifacts_dir=str(observed_root),
            expected_request_artifact_id=None,
            expected_plan_artifact_id=None,
            expected_changed_files_snapshot_artifact_id=None,
            expected_fact_snapshot_artifact_id=None,
            expected_execution_batch_manifest_artifact_id=None,
            aggregate_evidence_manifest_artifact_id="aggregate-upload-id",
            aggregate_phase="evidence",
            batch_materialization_failed=False,
            created_at=batch_contracts.CREATED_AT,
            started_at=batch_contracts.CREATED_AT,
            aggregate_evidence_manifest_out=str(aggregate_manifest_path),
            aggregate_summary_out=str(summary_path),
            github_output=str(output_path),
        )
        assert control._cmd_aggregate_ci_evidence(args) == 0
        raw_bytes = b""
        if rewrite is None:
            aggregate_manifest_path.unlink()
        else:
            raw_bytes = rewrite.encode()
            aggregate_manifest_path.write_bytes(raw_bytes)

        args.aggregate_phase = "summary"
        result = control._cmd_aggregate_ci_evidence(args)

        summary = json.loads(summary_path.read_text(encoding="utf-8"))
        validate_ci_validation_aggregate_summary(summary)
        final_manifest = cast(
            "dict[str, object]",
            cast("dict[str, object]", summary["final-artifacts"])[
                "aggregate-evidence-manifest"
            ],
        )
        authority_diagnostics = cast(
            "list[dict[str, object]]",
            final_manifest["authority-diagnostics"],
        )
        failures = cast("list[dict[str, object]]", summary["failures"])
        reason = cast("dict[str, object]", summary["reason"])
        assert result == 1
        assert reason["invalid-plan"] is True
        assert reason["fail-closed"] is False
        assert reason["final-evidence-failure"] is True
        assert final_manifest["producer-verified"] is False
        if expected_digest == "raw":
            assert (
                final_manifest["artifact-instance-id"] == "aggregate-upload-id"
            )
            assert (
                final_manifest["content-digest"]
                == hashlib.sha256(raw_bytes).hexdigest()
            )
        else:
            assert final_manifest["artifact-instance-id"] is None
            assert final_manifest["content-digest"] is None
            assert not aggregate_manifest_path.exists()
        assert expected_detail in {
            diagnostic["detail"] for diagnostic in authority_diagnostics
        }
        assert any(
            failure["kind"] == "final-evidence-failure"
            and cast("dict[str, object]", failure["diagnostic"])["detail"]
            == expected_detail
            for failure in failures
        )
        assert not any(failure["kind"] == "fail-closed" for failure in failures)
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_batch_missing_plan_reports_unverified_manifest_no_fail_closed() -> (
    None
):
    """No-plan summaries keep fail-closed false for unverified manifest."""
    scratch = _ci_batch_bundle_scratch(
        "batch-aggregation-missing-plan-unverified-manifest"
    )
    try:
        observed_root = scratch / "observed"
        observed_root.mkdir()
        aggregate_manifest_path = scratch / "aggregate-evidence-manifest.json"
        summary_path = scratch / "aggregate-summary.json"
        output_path = scratch / "outputs.txt"
        args = argparse.Namespace(
            repository="hcoona/three",
            workflow="CI Validation",
            run_id=batch_contracts.RUN_ID,
            run_attempt=batch_contracts.RUN_ATTEMPT,
            plan=str(scratch / "missing-validation-plan.json"),
            request="",
            execution_batch_manifest=str(
                scratch / "missing-execution-batch-manifest.json"
            ),
            changed_files_snapshot="",
            fact_snapshot="",
            assignments="",
            observed_artifacts_dir=str(observed_root),
            expected_request_artifact_id=None,
            expected_plan_artifact_id=None,
            expected_changed_files_snapshot_artifact_id=None,
            expected_fact_snapshot_artifact_id=None,
            expected_execution_batch_manifest_artifact_id=None,
            aggregate_evidence_manifest_artifact_id="aggregate-upload-id",
            aggregate_evidence_manifest_producer_verified=True,
            aggregate_phase="evidence",
            batch_materialization_failed=False,
            created_at=batch_contracts.CREATED_AT,
            started_at=batch_contracts.CREATED_AT,
            aggregate_evidence_manifest_out=str(aggregate_manifest_path),
            aggregate_summary_out=str(summary_path),
            github_output=str(output_path),
        )
        assert control._cmd_aggregate_ci_evidence(args) == 0

        args.aggregate_phase = "summary"
        args.aggregate_evidence_manifest_producer_verified = False
        result = control._cmd_aggregate_ci_evidence(args)

        summary = json.loads(summary_path.read_text(encoding="utf-8"))
        validate_ci_validation_aggregate_summary(summary)
        reason = cast("dict[str, object]", summary["reason"])
        failures = cast("list[dict[str, object]]", summary["failures"])
        assert result == 1
        assert reason["invalid-plan"] is True
        assert reason["fail-closed"] is False
        assert reason["final-evidence-failure"] is False
        assert not any(
            failure["kind"] == "final-evidence-failure" for failure in failures
        )
        assert not any(failure["kind"] == "fail-closed" for failure in failures)
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


@pytest.mark.parametrize(
    ("request_case", "request_override", "request_text", "expected_detail"),
    [
        (
            "missing",
            Path("missing-request.json"),
            None,
            "request-missing",
        ),
        ("malformed", None, "{ malformed request", "request-malformed"),
    ],
)
def test_ci_batch_aggregation_invalid_request_writes_g5_outputs(
    request_case: str,
    request_override: Path | None,
    request_text: str | None,
    expected_detail: str,
) -> None:
    """Invalid requests emit schema-valid fail-closed G5 outputs."""
    scratch = _ci_batch_bundle_scratch(
        f"batch-aggregation-invalid-request-{request_case}"
    )
    try:
        observed_root = scratch / "observed"
        observed_root.mkdir()
        plan, manifest = _ci_batch_contract_plan_and_manifest()

        result, aggregate_manifest, summary = _aggregate_ci_batch_evidence(
            scratch,
            plan,
            manifest,
            observed_root,
            request_path_override=scratch / request_override
            if request_override is not None
            else None,
            request_text_override=request_text,
        )
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=plan,
            expected_run_id=batch_contracts.RUN_ID,
            expected_run_attempt=batch_contracts.RUN_ATTEMPT,
        )
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            expected_run_id=batch_contracts.RUN_ID,
            expected_run_attempt=batch_contracts.RUN_ATTEMPT,
        )
        request_input = cast(
            "dict[str, object]",
            cast("dict[str, object]", aggregate_manifest["input-artifacts"])[
                "request"
            ],
        )
        diagnostics = cast(
            "list[dict[str, object]]",
            request_input["diagnostics"],
        )
        assert result == 1
        assert request_input["admissibility"] == (
            "missing" if request_case == "missing" else "inadmissible"
        )
        assert diagnostics[0]["code"] == "request-invalid"
        assert diagnostics[0]["detail"] == expected_detail
        assert summary["verdict"] == "failed"
        assert summary["reason"]["required-input-artifact-failure"] is True
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


@pytest.mark.parametrize(
    (
        "override_name",
        "expected_input",
        "expected_admissibility",
        "expected_detail",
        "expected_instance_id",
    ),
    [
        (
            "plan",
            "validation-plan",
            "inadmissible",
            "malformed-plan",
            "8001",
        ),
        (
            "plan-schema",
            "validation-plan",
            "inadmissible",
            "schema-invalid",
            "8001",
        ),
        (
            "changed-files",
            "changed-files-snapshot",
            "inadmissible",
            "changed-files-snapshot-malformed",
            "7101",
        ),
        (
            "fact",
            "fact-snapshot",
            "inadmissible",
            "fact-snapshot-malformed",
            "7201",
        ),
        (
            "execution-manifest",
            "execution-batch-manifest",
            "inadmissible",
            "execution-batch-manifest-malformed",
            "9001",
        ),
    ],
)
def test_ci_batch_aggregation_malformed_controls_write_g5_outputs(  # noqa: PLR0915
    override_name: str,
    expected_input: str,
    expected_admissibility: str,
    expected_detail: str,
    expected_instance_id: str | None,
) -> None:
    """Malformed batch controls emit schema-valid fail-closed G5 outputs."""
    scratch = _ci_batch_bundle_scratch(
        f"batch-aggregation-malformed-{override_name}"
    )
    try:
        observed_root = scratch / "observed"
        observed_root.mkdir()
        plan, manifest = _ci_batch_contract_plan_and_manifest()
        overrides = {
            "plan_text_override": "{ malformed plan",
            "plan_schema_text_override": json.dumps({"kind": "not-a-plan"}),
            "changed_files_text_override": "{ malformed changed files",
            "fact_snapshot_text_override": "{ malformed fact snapshot",
            "manifest_text_override": "{ malformed execution manifest",
        }
        selected_overrides = {}
        for key, value in overrides.items():
            selected_key = (
                "plan_text_override"
                if key == "plan_schema_text_override"
                else key
            )
            if (
                (override_name == "plan" and key == "plan_text_override")
                or (
                    override_name == "plan-schema"
                    and key == "plan_schema_text_override"
                )
                or (
                    override_name == "changed-files"
                    and key == "changed_files_text_override"
                )
                or (
                    override_name == "execution-manifest"
                    and key == "manifest_text_override"
                )
                or (
                    override_name == "fact"
                    and key == "fact_snapshot_text_override"
                )
            ):
                selected_overrides[selected_key] = value
        result, aggregate_manifest, summary = _aggregate_ci_batch_evidence(
            scratch,
            plan,
            manifest,
            observed_root,
            plan_text_override=selected_overrides.get("plan_text_override"),
            changed_files_text_override=selected_overrides.get(
                "changed_files_text_override"
            ),
            fact_snapshot_text_override=selected_overrides.get(
                "fact_snapshot_text_override"
            ),
            manifest_text_override=selected_overrides.get(
                "manifest_text_override"
            ),
        )

        if override_name == "execution-manifest":
            validate_ci_validation_aggregate_evidence_manifest(
                aggregate_manifest,
                plan=plan,
                expected_run_id=batch_contracts.RUN_ID,
                expected_run_attempt=batch_contracts.RUN_ATTEMPT,
            )
            validate_ci_validation_aggregate_summary(
                summary,
                plan=plan,
                aggregate_evidence_manifest=aggregate_manifest,
                expected_run_id=batch_contracts.RUN_ID,
                expected_run_attempt=batch_contracts.RUN_ATTEMPT,
            )
        else:
            validate_ci_validation_aggregate_evidence_manifest(
                aggregate_manifest
            )
            validate_ci_validation_aggregate_summary(summary)
        input_artifact = cast(
            "Mapping[str, object]",
            cast("Mapping[str, object]", aggregate_manifest["input-artifacts"])[
                expected_input
            ],
        )
        assert result == 1
        assert input_artifact["admissibility"] == expected_admissibility
        reason = cast("Mapping[str, object]", summary["reason"])
        if expected_admissibility == "inadmissible":
            diagnostics = cast(
                "Sequence[Mapping[str, object]]",
                input_artifact["diagnostics"],
            )
            expected_code = (
                "inadmissible-batch-evidence"
                if override_name == "execution-manifest"
                else "invalid-plan"
            )
            assert diagnostics[0]["code"] == expected_code
            assert diagnostics[0]["detail"] == expected_detail
            assert (
                input_artifact["artifact-instance-id"] == expected_instance_id
            )
            if override_name.startswith("plan"):
                expected_digest = hashlib.sha256(
                    str(selected_overrides["plan_text_override"]).encode()
                ).hexdigest()
            elif override_name == "changed-files":
                expected_digest = cast(
                    "Mapping[str, object]", plan["affected-range"]
                )["changed-files-hash"]
            elif override_name == "execution-manifest":
                expected_digest = None
            else:
                expected_digest = cast(
                    "Mapping[str, object]", plan["fact-snapshot"]
                )["id"]
            assert input_artifact["content-digest"] == expected_digest
        assert summary["verdict"] == "failed"
        if override_name in {"changed-files", "fact"}:
            assert reason["invalid-plan"] is True
            assert reason["fail-closed"] is False
            assert summary["plan-id"] == plan["plan-id"]
            assert summary["plan-digest"] == plan["plan-digest"]
            assert summary["mode"] == plan["mode"]
            assert summary["validation-tree"] == plan["validation-tree"]
            expected_range = control._ci_summary_affected_range(plan)
            assert summary["affected-range"] == expected_range
            assert summary["scheduled-full"] == plan["scheduled-full"]
        else:
            assert any(reason.values())
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


@pytest.mark.parametrize(
    ("case", "expected_detail"),
    [
        ("unreadable", "plan-unreadable"),
        ("digest-mismatch", "plan-digest-mismatch"),
    ],
)
def test_ci_batch_aggregation_invalid_plan_inputs_write_g5_outputs(
    case: str,
    expected_detail: str,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Unreadable or digest-mismatched plans emit fail-closed G5 outputs."""
    scratch = _ci_batch_bundle_scratch(f"batch-aggregation-invalid-plan-{case}")
    try:
        observed_root = scratch / "observed"
        observed_root.mkdir()
        plan, manifest = _ci_batch_contract_plan_and_manifest()
        plan_path_override = None
        if case == "unreadable":
            plan_path_override = scratch / "unreadable-plan.json"
            plan_path_override.write_text(json.dumps(plan), encoding="utf-8")
            original_read_optional_json = control._read_optional_json

            def read_optional_json_or_raise(value: str) -> object:
                if value == str(plan_path_override):
                    raise OSError
                return original_read_optional_json(value)

            monkeypatch.setattr(
                control,
                "_read_optional_json",
                read_optional_json_or_raise,
            )
        else:
            plan = deepcopy(plan)
            plan["plan-digest"] = "0" * 64

        result, aggregate_manifest, summary = _aggregate_ci_batch_evidence(
            scratch,
            plan,
            manifest,
            observed_root,
            plan_path_override=plan_path_override,
        )

        validate_ci_validation_aggregate_evidence_manifest(aggregate_manifest)
        validate_ci_validation_aggregate_summary(summary)
        input_artifact = cast(
            "Mapping[str, object]",
            cast("Mapping[str, object]", aggregate_manifest["input-artifacts"])[
                "validation-plan"
            ],
        )
        diagnostics = cast(
            "Sequence[Mapping[str, object]]",
            input_artifact["diagnostics"],
        )
        assert result == 1
        assert input_artifact["admissibility"] == "inadmissible"
        assert diagnostics[0]["code"] == "invalid-plan"
        assert diagnostics[0]["detail"] == expected_detail
        assert summary["verdict"] == "failed"
        assert summary["reason"]["invalid-plan"] is True
        assert aggregate_manifest["batch-bundles"] == []
        assert summary["evidence-results"] == []
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


@pytest.mark.parametrize(
    ("case", "plan_artifact_ids", "expected_detail"),
    [
        ("duplicate", [8001, 8002], "plan-duplicate"),
        ("producer-unverified", [8002], "plan-producer-unverified"),
    ],
)
def test_ci_batch_aggregation_invalid_plan_artifact_authority_writes_g5_outputs(
    case: str,
    plan_artifact_ids: list[int],
    expected_detail: str,
) -> None:
    """Duplicate or unverified plan artifacts emit fail-closed G5 outputs."""
    scratch = _ci_batch_bundle_scratch(
        f"batch-aggregation-invalid-plan-authority-{case}"
    )
    try:
        observed_root = scratch / "observed"
        observed_root.mkdir()
        plan, manifest = _ci_batch_contract_plan_and_manifest()
        run_artifacts = [
            _ci_artifact_metadata(
                control.ci_validation_request_artifact_ref(
                    run_id=batch_contracts.RUN_ID,
                    run_attempt=batch_contracts.RUN_ATTEMPT,
                ),
                artifact_id=7001,
            ),
            *[
                _ci_artifact_metadata(
                    control.ci_validation_plan_artifact_ref(
                        run_id=batch_contracts.RUN_ID,
                        run_attempt=batch_contracts.RUN_ATTEMPT,
                    ),
                    artifact_id=artifact_id,
                )
                for artifact_id in plan_artifact_ids
            ],
            _ci_artifact_metadata(
                control.ci_validation_changed_files_snapshot_artifact_ref(
                    run_id=batch_contracts.RUN_ID,
                    run_attempt=batch_contracts.RUN_ATTEMPT,
                ),
                artifact_id=7101,
            ),
            _ci_artifact_metadata(
                control.ci_validation_fact_snapshot_artifact_ref(
                    run_id=batch_contracts.RUN_ID,
                    run_attempt=batch_contracts.RUN_ATTEMPT,
                ),
                artifact_id=7201,
            ),
            _ci_artifact_metadata(
                control.ci_validation_execution_batch_manifest_artifact_ref(
                    run_id=batch_contracts.RUN_ID,
                    run_attempt=batch_contracts.RUN_ATTEMPT,
                ),
                artifact_id=9001,
            ),
        ]

        result, aggregate_manifest, summary = _aggregate_ci_batch_evidence(
            scratch,
            plan,
            manifest,
            observed_root,
            run_artifacts=run_artifacts,
        )

        context = batch_contracts.authorizing_context_kwargs()
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=plan,
            execution_batch_manifest=manifest,
            request=cast("Mapping[str, object]", context["request"]),
            changed_files_snapshot=cast(
                "Mapping[str, object]", context["changed_files_snapshot"]
            ),
            fact_snapshot=cast(
                "Mapping[str, object]", context["fact_snapshot"]
            ),
            expected_run_id=batch_contracts.RUN_ID,
            expected_run_attempt=batch_contracts.RUN_ATTEMPT,
        )
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            execution_batch_manifest=manifest,
            request=cast("Mapping[str, object]", context["request"]),
            changed_files_snapshot=cast(
                "Mapping[str, object]", context["changed_files_snapshot"]
            ),
            fact_snapshot=cast(
                "Mapping[str, object]", context["fact_snapshot"]
            ),
            expected_run_id=batch_contracts.RUN_ID,
            expected_run_attempt=batch_contracts.RUN_ATTEMPT,
        )
        input_artifact = cast(
            "Mapping[str, object]",
            cast("Mapping[str, object]", aggregate_manifest["input-artifacts"])[
                "validation-plan"
            ],
        )
        diagnostics = cast(
            "Sequence[Mapping[str, object]]",
            input_artifact["diagnostics"],
        )
        assert result == 1
        assert input_artifact["admissibility"] == "inadmissible"
        assert diagnostics[0]["code"] == "invalid-plan"
        assert diagnostics[0]["detail"] == expected_detail
        assert summary["verdict"] == "failed"
        reason = cast("Mapping[str, object]", summary["reason"])
        assert reason["invalid-plan"] is True
        assert reason["fail-closed"] is False
        assert reason["final-evidence-failure"] is False
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


@pytest.mark.parametrize(
    ("case", "manifest_text"),
    [
        ("invalid-json", "{ malformed execution manifest"),
        ("non-object", "[]"),
        ("malformed-values", json.dumps({"kind": "not-a-manifest"})),
    ],
)
def test_ci_batch_aggregation_malformed_local_execution_manifest_not_missing(
    case: str,
    manifest_text: str,
) -> None:
    """Malformed local execution-batch manifest is inadmissible, not missing."""
    scratch = _ci_batch_bundle_scratch(
        f"batch-aggregation-malformed-local-execution-manifest-{case}"
    )
    try:
        observed_root = scratch / "observed"
        observed_root.mkdir()
        plan, manifest = _ci_batch_contract_plan_and_manifest()

        result, aggregate_manifest, _summary = _aggregate_ci_batch_evidence(
            scratch,
            plan,
            manifest,
            observed_root,
            manifest_text_override=manifest_text,
        )

        manifest_input = cast(
            "Mapping[str, object]",
            cast("Mapping[str, object]", aggregate_manifest["input-artifacts"])[
                "execution-batch-manifest"
            ],
        )
        diagnostics = cast(
            "Sequence[Mapping[str, object]]",
            manifest_input["diagnostics"],
        )
        assert result == 1
        assert manifest_input["admissibility"] == "inadmissible"
        assert diagnostics[0]["detail"] == "execution-batch-manifest-malformed"
        assert diagnostics[0]["detail"] != "execution-batch-manifest-missing"
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


@pytest.mark.parametrize(
    ("case", "manifest_update", "expected_detail"),
    [
        (
            "plan-id",
            {"plan-id": "ci-validation-plan-mismatch"},
            "execution-batch-manifest-plan-mismatch",
        ),
        (
            "plan-digest",
            {"plan-digest": "0" * 64},
            "execution-batch-manifest-digest-mismatch",
        ),
    ],
)
def test_ci_batch_aggregation_execution_manifest_mismatch_details(
    case: str,
    manifest_update: Mapping[str, object],
    expected_detail: str,
) -> None:
    """Plan-bound execution-manifest mismatches keep registered details."""
    scratch = _ci_batch_bundle_scratch(
        f"batch-aggregation-execution-manifest-{case}-mismatch"
    )
    try:
        observed_root = scratch / "observed"
        observed_root.mkdir()
        plan, manifest = _ci_batch_contract_plan_and_manifest()
        mismatched_manifest = {**manifest, **manifest_update}

        result, aggregate_manifest, summary = _aggregate_ci_batch_evidence(
            scratch,
            plan,
            manifest,
            observed_root,
            manifest_override=mismatched_manifest,
        )

        manifest_input = cast(
            "Mapping[str, object]",
            cast("Mapping[str, object]", aggregate_manifest["input-artifacts"])[
                "execution-batch-manifest"
            ],
        )
        diagnostics = cast(
            "Sequence[Mapping[str, object]]",
            manifest_input["diagnostics"],
        )
        assert result == 1
        assert manifest_input["admissibility"] == "inadmissible"
        assert diagnostics[0]["code"] == "inadmissible-batch-evidence"
        assert diagnostics[0]["detail"] == expected_detail
        assert summary["reason"]["required-input-artifact-failure"] is True
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


@pytest.mark.parametrize(
    ("manifest_case", "manifest_override", "manifest_text"),
    [
        ("missing", Path("missing-execution-batch-manifest.json"), None),
        ("malformed", None, "{ malformed execution manifest"),
    ],
)
def test_ci_batch_aggregation_invalid_manifest_reports_prefixed_artifacts(
    manifest_case: str,
    manifest_override: Path | None,
    manifest_text: str | None,
) -> None:
    """Invalid execution manifests report unexpected prefixed artifacts."""
    scratch = _ci_batch_bundle_scratch(
        f"batch-aggregation-invalid-manifest-prefixed-{manifest_case}"
    )
    try:
        observed_root = scratch / "observed"
        observed_root.mkdir()
        plan, manifest = _ci_batch_contract_plan_and_manifest()
        request_ref = control.ci_validation_request_artifact_ref(
            run_id=batch_contracts.RUN_ID,
            run_attempt=batch_contracts.RUN_ATTEMPT,
        )
        unexpected_name = f"{artifact_physical_name(request_ref)}#legacy"
        (observed_root / unexpected_name).mkdir()

        result, aggregate_manifest, summary = _aggregate_ci_batch_evidence(
            scratch,
            plan,
            manifest,
            observed_root,
            manifest_path_override=scratch / manifest_override
            if manifest_override is not None
            else None,
            manifest_text_override=manifest_text,
        )
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=plan,
            expected_run_id=batch_contracts.RUN_ID,
            expected_run_attempt=batch_contracts.RUN_ATTEMPT,
        )
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            expected_run_id=batch_contracts.RUN_ID,
            expected_run_attempt=batch_contracts.RUN_ATTEMPT,
        )
        unexpected = cast(
            "list[dict[str, object]]",
            aggregate_manifest["unexpected-contract-artifacts"],
        )
        assert result == 1
        assert unexpected
        assert unexpected[0]["artifact-instance-id"] == unexpected_name
        assert (
            unexpected[0]["diagnostics"][0]["detail"]
            == "unexpected-contract-artifact"
        )
        assert summary["reason"]["namespace-closure-failure"] is True
        assert summary["batch-bundles"] == []
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_batch_aggregation_writes_manifest_and_summary() -> None:
    """Batch aggregation admits authoritative bundles and passes."""
    scratch = _ci_batch_bundle_scratch("batch-aggregation-success")
    try:
        observed_root = scratch / "observed"
        observed_root.mkdir()
        plan, manifest = _ci_batch_contract_plan_and_manifest()
        batches = {
            cast("str", batch["batch-id"]): batch
            for batch in cast("list[dict[str, object]]", manifest["batches"])
        }
        for row in _ci_batch_matrix_rows(plan, manifest):
            batch_id = cast(
                "str",
                cast("dict[str, object]", row["identity-matrix"])["batch-id"],
            )
            selector = cast(
                "list[dict[str, object]]",
                batches[batch_id]["ordered-selectors"],
            )[0]
            bundle = _write_ci_batch_bundle(
                scratch,
                plan,
                manifest,
                row,
                [
                    _ci_success_validation_result(
                        plan, cast("str", selector["work-group-id"])
                    )
                ],
            )
            _stage_ci_batch_bundle_artifact(observed_root, bundle)

        result, aggregate_manifest, summary = _aggregate_ci_batch_evidence(
            scratch,
            plan,
            manifest,
            observed_root,
        )

        assert result == 0
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=plan,
            request=cast(
                "dict[str, object]",
                batch_contracts.authorizing_context_kwargs()["request"],
            ),
            execution_batch_manifest=manifest,
            changed_files_snapshot=cast(
                "dict[str, object]",
                batch_contracts.authorizing_context_kwargs()[
                    "changed_files_snapshot"
                ],
            ),
            fact_snapshot=cast(
                "dict[str, object]",
                batch_contracts.authorizing_context_kwargs()["fact_snapshot"],
            ),
            expected_run_id=batch_contracts.RUN_ID,
            expected_run_attempt=batch_contracts.RUN_ATTEMPT,
        )
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            admitted_batch_evidence_bundles=[bundle],
            execution_batch_manifest=manifest,
            request=cast(
                "dict[str, object]",
                batch_contracts.authorizing_context_kwargs()["request"],
            ),
            changed_files_snapshot=cast(
                "dict[str, object]",
                batch_contracts.authorizing_context_kwargs()[
                    "changed_files_snapshot"
                ],
            ),
            fact_snapshot=cast(
                "dict[str, object]",
                batch_contracts.authorizing_context_kwargs()["fact_snapshot"],
            ),
            expected_run_id=batch_contracts.RUN_ID,
            expected_run_attempt=batch_contracts.RUN_ATTEMPT,
        )
        assert summary["verdict"] == "passed"
        input_artifacts = cast(
            "dict[str, dict[str, object]]",
            aggregate_manifest["input-artifacts"],
        )
        assert input_artifacts["request"]["artifact-instance-id"] == "7001"
        assert (
            input_artifacts["validation-plan"]["artifact-instance-id"] == "8001"
        )
        manifest_input = cast(
            "dict[str, object]",
            input_artifacts["execution-batch-manifest"],
        )
        assert manifest_input["artifact-instance-id"] == "9001"
        assert (
            cast("list[dict[str, object]]", summary["evidence-results"])[0][
                "outcome"
            ]
            == "satisfied"
        )
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_batch_aggregation_reports_suffixed_control_artifact() -> None:
    """Suffixed control artifacts produce schema-valid unexpected rows."""
    scratch = _ci_batch_bundle_scratch("batch-aggregation-suffixed-control")
    try:
        observed_root = scratch / "observed"
        observed_root.mkdir()
        plan, manifest = _ci_batch_contract_plan_and_manifest()
        batches = {
            cast("str", batch["batch-id"]): batch
            for batch in cast("list[dict[str, object]]", manifest["batches"])
        }
        admitted_bundles = []
        for row in _ci_batch_matrix_rows(plan, manifest):
            batch_id = cast(
                "str",
                cast("dict[str, object]", row["identity-matrix"])["batch-id"],
            )
            selector = cast(
                "list[dict[str, object]]",
                batches[batch_id]["ordered-selectors"],
            )[0]
            bundle = _write_ci_batch_bundle(
                scratch,
                plan,
                manifest,
                row,
                [
                    _ci_success_validation_result(
                        plan, cast("str", selector["work-group-id"])
                    )
                ],
            )
            admitted_bundles.append(bundle)
            _stage_ci_batch_bundle_artifact(observed_root, bundle)
        canonical_name = artifact_physical_name(
            control.ci_validation_plan_artifact_ref(
                run_id=batch_contracts.RUN_ID,
                run_attempt=batch_contracts.RUN_ATTEMPT,
            )
        )
        observed_name = f"{canonical_name}#1"
        (observed_root / observed_name).mkdir()
        noncanonical_observed_name = (
            "three-ci-validation-25887422010-1-unexpected-live"
        )
        (observed_root / noncanonical_observed_name).mkdir()

        result, aggregate_manifest, summary = _aggregate_ci_batch_evidence(
            scratch,
            plan,
            manifest,
            observed_root,
        )

        context = batch_contracts.authorizing_context_kwargs()
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=plan,
            request=cast("dict[str, object]", context["request"]),
            execution_batch_manifest=manifest,
            changed_files_snapshot=cast(
                "dict[str, object]", context["changed_files_snapshot"]
            ),
            fact_snapshot=cast("dict[str, object]", context["fact_snapshot"]),
            expected_run_id=batch_contracts.RUN_ID,
            expected_run_attempt=batch_contracts.RUN_ATTEMPT,
        )
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            admitted_batch_evidence_bundles=admitted_bundles,
            execution_batch_manifest=manifest,
            request=cast("dict[str, object]", context["request"]),
            changed_files_snapshot=cast(
                "dict[str, object]", context["changed_files_snapshot"]
            ),
            fact_snapshot=cast("dict[str, object]", context["fact_snapshot"]),
            expected_run_id=batch_contracts.RUN_ID,
            expected_run_attempt=batch_contracts.RUN_ATTEMPT,
        )
        unexpected = cast(
            "list[dict[str, object]]",
            aggregate_manifest["unexpected-contract-artifacts"],
        )
        unexpected_by_observed_name = {
            str(
                item.get(
                    "observed-physical-artifact-name",
                    item["physical-artifact-name"],
                )
            ): item
            for item in unexpected
        }
        assert result == 1
        assert set(unexpected_by_observed_name) == {
            observed_name,
            noncanonical_observed_name,
        }
        suffixed_unexpected = unexpected_by_observed_name[observed_name]
        assert suffixed_unexpected == {
            "physical-artifact-name": canonical_name,
            "observed-physical-artifact-name": observed_name,
            "artifact-instance-id": observed_name,
            "classification": "unexpected",
            "diagnostics": suffixed_unexpected["diagnostics"],
        }
        noncanonical_unexpected = unexpected_by_observed_name[
            noncanonical_observed_name
        ]
        synthetic_physical_name = control._ci_canonical_observed_physical_name(
            noncanonical_observed_name,
            run_id=batch_contracts.RUN_ID,
            run_attempt=batch_contracts.RUN_ATTEMPT,
        )
        assert noncanonical_unexpected == {
            "physical-artifact-name": synthetic_physical_name,
            "observed-physical-artifact-name": noncanonical_observed_name,
            "artifact-instance-id": noncanonical_observed_name,
            "classification": "unexpected",
            "diagnostics": noncanonical_unexpected["diagnostics"],
        }
        assert summary["reason"]["namespace-closure-failure"] is True
        assert summary["verdict"] == "failed"
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_batch_aggregation_writes_g5_outputs_for_boundary_failure() -> None:
    """Batch producer-boundary failures write G5 manifest/summary outputs."""
    scratch = _ci_batch_bundle_scratch("batch-aggregation-boundary-failure")
    try:
        observed_root = scratch / "observed"
        observed_root.mkdir()
        plan, manifest = _ci_batch_contract_plan_and_manifest()
        batches = {
            cast("str", batch["batch-id"]): batch
            for batch in cast("list[dict[str, object]]", manifest["batches"])
        }
        for row in _ci_batch_matrix_rows(plan, manifest):
            batch_id = cast(
                "str",
                cast("dict[str, object]", row["identity-matrix"])["batch-id"],
            )
            selector = cast(
                "list[dict[str, object]]",
                batches[batch_id]["ordered-selectors"],
            )[0]
            bundle = _write_ci_batch_bundle(
                scratch,
                plan,
                manifest,
                row,
                [
                    _ci_success_validation_result(
                        plan, cast("str", selector["work-group-id"])
                    )
                ],
            )
            _stage_ci_batch_bundle_artifact(observed_root, bundle)
        manifest_ref = (
            control.ci_validation_execution_batch_manifest_artifact_ref(
                run_id=batch_contracts.RUN_ID,
                run_attempt=batch_contracts.RUN_ATTEMPT,
            )
        )

        result, aggregate_manifest, summary = _aggregate_ci_batch_evidence(
            scratch,
            plan,
            manifest,
            observed_root,
            expected_execution_batch_manifest_artifact_id="9001",
            run_artifacts=[
                _ci_artifact_metadata(
                    control.ci_validation_request_artifact_ref(
                        run_id=batch_contracts.RUN_ID,
                        run_attempt=batch_contracts.RUN_ATTEMPT,
                    ),
                    artifact_id=7001,
                ),
                _ci_artifact_metadata(
                    control.ci_validation_plan_artifact_ref(
                        run_id=batch_contracts.RUN_ID,
                        run_attempt=batch_contracts.RUN_ATTEMPT,
                    ),
                    artifact_id=8001,
                ),
                _ci_artifact_metadata(
                    control.ci_validation_changed_files_snapshot_artifact_ref(
                        run_id=batch_contracts.RUN_ID,
                        run_attempt=batch_contracts.RUN_ATTEMPT,
                    ),
                    artifact_id=7101,
                ),
                _ci_artifact_metadata(
                    control.ci_validation_fact_snapshot_artifact_ref(
                        run_id=batch_contracts.RUN_ID,
                        run_attempt=batch_contracts.RUN_ATTEMPT,
                    ),
                    artifact_id=7201,
                ),
                _ci_artifact_metadata(manifest_ref, artifact_id=9002),
            ],
        )

        assert result == 1
        manifest_input = cast(
            "dict[str, object]",
            cast("dict[str, object]", aggregate_manifest["input-artifacts"])[
                "execution-batch-manifest"
            ],
        )
        assert manifest_input["admissibility"] == "inadmissible"
        assert (
            cast("list[dict[str, object]]", manifest_input["diagnostics"])[0][
                "detail"
            ]
            == "execution-batch-manifest-malformed"
        )
        assert summary["kind"] == "ci-validation-aggregate-summary"
        reason = cast("dict[str, object]", summary["reason"])
        assert reason["required-input-artifact-failure"] is True
        assert reason["final-evidence-failure"] is False
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


@pytest.mark.parametrize(
    "missing_input",
    ["request", "validation-plan"],
)
def test_ci_batch_aggregation_requires_request_and_plan_artifact_ids(
    missing_input: str,
) -> None:
    """G5 batch aggregation requires authoritative request and plan ids."""
    scratch = _ci_batch_bundle_scratch(
        f"batch-aggregation-missing-{missing_input}-id"
    )
    try:
        observed_root = scratch / "observed"
        observed_root.mkdir()
        plan, manifest = _ci_batch_contract_plan_and_manifest()

        result, aggregate_manifest, summary = _aggregate_ci_batch_evidence(
            scratch,
            plan,
            manifest,
            observed_root,
            expected_request_artifact_id=None
            if missing_input == "request"
            else "7001",
            expected_plan_artifact_id=None
            if missing_input == "validation-plan"
            else "8001",
        )

        assert result == 1
        input_artifact = cast(
            "dict[str, object]",
            cast("dict[str, object]", aggregate_manifest["input-artifacts"])[
                missing_input
            ],
        )
        assert input_artifact["admissibility"] == "missing"
        assert input_artifact["artifact-instance-id"] is None
        reason = cast("dict[str, object]", summary["reason"])
        assert reason["required-input-artifact-failure"] is True
        assert reason["final-evidence-failure"] is False
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_batch_aggregation_requires_batch_manifest_artifact_id() -> None:
    """G5 batch aggregation requires an authoritative manifest upload id."""
    scratch = _ci_batch_bundle_scratch(
        "batch-aggregation-missing-execution-batch-manifest-id"
    )
    try:
        observed_root = scratch / "observed"
        observed_root.mkdir()
        plan, manifest = _ci_batch_contract_plan_and_manifest()

        result, aggregate_manifest, summary = _aggregate_ci_batch_evidence(
            scratch,
            plan,
            manifest,
            observed_root,
            expected_execution_batch_manifest_artifact_id=None,
        )

        assert result == 1
        input_artifact = cast(
            "dict[str, object]",
            cast("dict[str, object]", aggregate_manifest["input-artifacts"])[
                "execution-batch-manifest"
            ],
        )
        assert input_artifact["admissibility"] == "missing"
        assert input_artifact["artifact-instance-id"] is None
        reason = cast("dict[str, object]", summary["reason"])
        assert reason["required-input-artifact-failure"] is True
        assert reason["final-evidence-failure"] is False
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


@pytest.mark.parametrize(
    "missing_input",
    ["changed-files-snapshot", "fact-snapshot"],
)
def test_ci_batch_aggregation_requires_snapshot_artifact_ids(
    missing_input: str,
) -> None:
    """Required snapshots need real artifact ids for producer verification."""
    scratch = _ci_batch_bundle_scratch(
        f"batch-aggregation-missing-{missing_input}-id"
    )
    try:
        observed_root = scratch / "observed"
        observed_root.mkdir()
        plan, manifest = _ci_batch_contract_plan_and_manifest()

        result, aggregate_manifest, summary = _aggregate_ci_batch_evidence(
            scratch,
            plan,
            manifest,
            observed_root,
            expected_changed_files_snapshot_artifact_id=None
            if missing_input == "changed-files-snapshot"
            else "7101",
            expected_fact_snapshot_artifact_id=None
            if missing_input == "fact-snapshot"
            else "7201",
        )

        assert result == 1
        input_artifact = cast(
            "dict[str, object]",
            cast("dict[str, object]", aggregate_manifest["input-artifacts"])[
                missing_input
            ],
        )
        assert input_artifact["admissibility"] == "missing"
        assert input_artifact["artifact-instance-id"] is None
        reason = cast("dict[str, object]", summary["reason"])
        assert reason["required-input-artifact-failure"] is True
        assert reason["final-evidence-failure"] is False
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_batch_aggregation_fails_closed_for_artifact_api_failure() -> None:
    """Generic artifact API failures fail the final aggregate closed."""
    scratch = _ci_batch_bundle_scratch("batch-aggregation-artifact-api-failure")
    try:
        observed_root = scratch / "observed"
        observed_root.mkdir()
        plan, manifest = _ci_batch_contract_plan_and_manifest()
        row = _ci_batch_matrix_rows(plan, manifest)[0]
        batch = cast("list[dict[str, object]]", manifest["batches"])[0]
        selector = cast(
            "list[dict[str, object]]",
            batch["ordered-selectors"],
        )[0]
        bundle = _write_ci_batch_bundle(
            scratch,
            plan,
            manifest,
            row,
            [
                _ci_success_validation_result(
                    plan, cast("str", selector["work-group-id"])
                )
            ],
        )
        _stage_ci_batch_bundle_artifact(observed_root, bundle)

        result, aggregate_manifest, summary = _aggregate_ci_batch_evidence(
            scratch,
            plan,
            manifest,
            observed_root,
            run_artifacts_error=RuntimeError("artifact API unavailable"),
        )

        assert result == 1
        assert summary["verdict"] == "failed"
        reason = cast("dict[str, object]", summary["reason"])
        assert reason["required-input-artifact-failure"] is True
        assert reason["final-evidence-failure"] is False
        input_artifacts = cast(
            "dict[str, dict[str, object]]",
            aggregate_manifest["input-artifacts"],
        )
        assert all(
            artifact["admissibility"] == "inadmissible"
            for artifact in input_artifacts.values()
            if artifact["required"] is True
        )
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_batch_aggregation_fails_closed_for_missing_bundle() -> None:
    """Missing execution-batch bundle evidence fails closed."""
    scratch = _ci_batch_bundle_scratch("batch-aggregation-missing")
    try:
        observed_root = scratch / "observed"
        observed_root.mkdir()
        plan, manifest = _ci_batch_contract_plan_and_manifest()

        result, aggregate_manifest, summary = _aggregate_ci_batch_evidence(
            scratch,
            plan,
            manifest,
            observed_root,
        )

        assert result == 1
        assert (
            cast("dict[str, object]", summary["reason"])[
                "required-evidence-missing"
            ]
            is True
        )
        slot = cast(
            "list[dict[str, object]]", aggregate_manifest["batch-bundles"]
        )[0]
        assert slot["slot-admissibility"] == "missing"
        assert (
            cast("list[dict[str, object]]", summary["batch-bundles"])[0][
                "admissibility"
            ]
            == "missing"
        )
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


@pytest.mark.parametrize(
    "missing_input",
    ["plan", "request"],
)
def test_ci_batch_aggregation_cli_rejects_missing_required_inputs(
    missing_input: str,
) -> None:
    """Missing required CLI inputs do not traceback."""
    scratch = _ci_batch_bundle_scratch(
        f"batch-aggregation-cli-missing-{missing_input}"
    )
    try:
        observed_root = scratch / "observed"
        observed_root.mkdir()
        plan, manifest = _ci_batch_contract_plan_and_manifest()
        context = batch_contracts.authorizing_context_kwargs()
        plan_path = scratch / "plan.json"
        request_path = scratch / "request.json"
        manifest_path = scratch / "execution-batch-manifest.json"
        changed_files_path = scratch / "changed-files.json"
        fact_snapshot_path = scratch / "fact-snapshot.json"
        plan_path.write_text(json.dumps(plan), encoding="utf-8")
        request_path.write_text(
            json.dumps(context["request"]), encoding="utf-8"
        )
        manifest_path.write_text(json.dumps(manifest), encoding="utf-8")
        changed_files_path.write_text(
            json.dumps(context["changed_files_snapshot"]),
            encoding="utf-8",
        )
        fact_snapshot_path.write_text(
            json.dumps(context["fact_snapshot"]),
            encoding="utf-8",
        )
        command = [
            sys.executable,
            "eng/scripts/workflow_release_control.py",
            "aggregate-ci-evidence",
            "--repository",
            "hcoona/three",
            "--workflow",
            "CI Validation",
            "--run-id",
            batch_contracts.RUN_ID,
            "--run-attempt",
            batch_contracts.RUN_ATTEMPT,
            "--execution-batch-manifest",
            str(manifest_path),
            "--observed-artifacts-dir",
            str(observed_root),
            "--changed-files-snapshot",
            str(changed_files_path),
            "--fact-snapshot",
            str(fact_snapshot_path),
            "--aggregate-evidence-manifest-out",
            str(scratch / "aggregate-evidence-manifest.json"),
            "--aggregate-summary-out",
            str(scratch / "aggregate-summary.json"),
            "--aggregate-evidence-manifest-artifact-id",
            "aggregate-upload-id",
        ]
        if missing_input != "plan":
            command.extend(["--plan", str(plan_path)])
        if missing_input != "request":
            command.extend(["--request", str(request_path)])

        completed = subprocess.run(  # noqa: S603
            command,
            cwd=REPO_ROOT,
            text=True,
            capture_output=True,
            check=False,
        )

        assert completed.returncode == 1
        aggregate = json.loads(
            (scratch / "aggregate-summary.json").read_text(
                encoding="utf-8",
            )
        )
        if missing_input == "plan":
            validate_ci_validation_aggregate_summary(aggregate)
            assert aggregate["reason"]["invalid-plan"] is True
        else:
            assert aggregate["reason"]["fail-closed"] is True
        assert "Traceback" not in completed.stderr
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_batch_aggregation_cli_requires_explicit_batch_mode() -> None:
    """Require explicit G5 batch mode for aggregate-ci-evidence."""
    scratch = _ci_batch_bundle_scratch("batch-aggregation-cli-no-batch-mode")
    try:
        observed_root = scratch / "observed"
        observed_root.mkdir()
        plan = cast("dict[str, object]", batch_contracts.plan())
        plan_path = scratch / "plan.json"
        request_path = scratch / "request.json"
        changed_files_path = scratch / "changed-files.json"
        fact_snapshot_path = scratch / "fact-snapshot.json"
        plan_path.write_text(json.dumps(plan), encoding="utf-8")
        request_path.write_text(
            json.dumps(batch_contracts.request_document()), encoding="utf-8"
        )
        changed_files_path.write_text(
            json.dumps(batch_contracts.changed_files_snapshot_document()),
            encoding="utf-8",
        )
        fact_snapshot_path.write_text(
            json.dumps(batch_contracts.fact_snapshot_document()),
            encoding="utf-8",
        )

        completed = subprocess.run(  # noqa: S603
            [
                sys.executable,
                "eng/scripts/workflow_release_control.py",
                "aggregate-ci-evidence",
                "--repository",
                "hcoona/three",
                "--workflow",
                "CI Validation",
                "--run-id",
                batch_contracts.RUN_ID,
                "--run-attempt",
                batch_contracts.RUN_ATTEMPT,
                "--plan",
                str(plan_path),
                "--request",
                str(request_path),
                "--observed-artifacts-dir",
                str(observed_root),
                "--changed-files-snapshot",
                str(changed_files_path),
                "--fact-snapshot",
                str(fact_snapshot_path),
                "--aggregate-evidence-manifest-out",
                str(scratch / "aggregate-evidence-manifest.json"),
                "--aggregate-summary-out",
                str(scratch / "aggregate-summary.json"),
            ],
            cwd=REPO_ROOT,
            text=True,
            capture_output=True,
            check=False,
        )

        assert completed.returncode == 2
        assert "explicit G5 batch mode" in completed.stderr
        assert "Traceback" not in completed.stderr
        assert not (scratch / "aggregate-evidence-manifest.json").exists()
        assert not (scratch / "aggregate-summary.json").exists()
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_batch_aggregation_cli_requires_observed_artifacts_dir() -> None:
    """Batch aggregation requires the downloader-created observed directory."""
    scratch = _ci_batch_bundle_scratch("batch-aggregation-cli-no-observed-dir")
    try:
        completed = subprocess.run(  # noqa: S603
            [
                sys.executable,
                "eng/scripts/workflow_release_control.py",
                "aggregate-ci-evidence",
                "--repository",
                "hcoona/three",
                "--workflow",
                "CI Validation",
                "--run-id",
                batch_contracts.RUN_ID,
                "--run-attempt",
                batch_contracts.RUN_ATTEMPT,
                "--execution-batch-manifest",
                str(scratch / "execution-batch-manifest.json"),
                "--aggregate-evidence-manifest-out",
                str(scratch / "aggregate-evidence-manifest.json"),
                "--aggregate-summary-out",
                str(scratch / "aggregate-summary.json"),
            ],
            cwd=REPO_ROOT,
            text=True,
            capture_output=True,
            check=False,
        )

        assert completed.returncode == 2
        assert "--observed-artifacts-dir" in completed.stderr
        assert "Traceback" not in completed.stderr
        assert not (scratch / "aggregate-evidence-manifest.json").exists()
        assert not (scratch / "aggregate-summary.json").exists()
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_batch_aggregation_cli_rejects_invalid_manifest() -> None:
    """Invalid execution-batch manifests do not traceback."""
    scratch = _ci_batch_bundle_scratch("batch-aggregation-cli-invalid-manifest")
    try:
        observed_root = scratch / "observed"
        observed_root.mkdir()
        plan, _manifest = _ci_batch_contract_plan_and_manifest()
        context = batch_contracts.authorizing_context_kwargs()
        plan_path = scratch / "plan.json"
        request_path = scratch / "request.json"
        manifest_path = scratch / "execution-batch-manifest.json"
        changed_files_path = scratch / "changed-files.json"
        fact_snapshot_path = scratch / "fact-snapshot.json"
        aggregate_manifest_path = scratch / "aggregate-evidence-manifest.json"
        summary_path = scratch / "aggregate-summary.json"
        plan_path.write_text(json.dumps(plan), encoding="utf-8")
        request_path.write_text(
            json.dumps(context["request"]), encoding="utf-8"
        )
        manifest_path.write_text("{", encoding="utf-8")
        changed_files_path.write_text(
            json.dumps(context["changed_files_snapshot"]),
            encoding="utf-8",
        )
        fact_snapshot_path.write_text(
            json.dumps(context["fact_snapshot"]),
            encoding="utf-8",
        )

        completed = subprocess.run(  # noqa: S603
            [
                sys.executable,
                "eng/scripts/workflow_release_control.py",
                "aggregate-ci-evidence",
                "--repository",
                "hcoona/three",
                "--workflow",
                "CI Validation",
                "--run-id",
                batch_contracts.RUN_ID,
                "--run-attempt",
                batch_contracts.RUN_ATTEMPT,
                "--plan",
                str(plan_path),
                "--request",
                str(request_path),
                "--execution-batch-manifest",
                str(manifest_path),
                "--observed-artifacts-dir",
                str(observed_root),
                "--changed-files-snapshot",
                str(changed_files_path),
                "--fact-snapshot",
                str(fact_snapshot_path),
                "--aggregate-evidence-manifest-out",
                str(aggregate_manifest_path),
                "--aggregate-summary-out",
                str(summary_path),
                "--aggregate-evidence-manifest-artifact-id",
                "aggregate-upload-id",
            ],
            cwd=REPO_ROOT,
            text=True,
            capture_output=True,
            check=False,
        )

        assert completed.returncode == 1
        assert aggregate_manifest_path.exists()
        assert summary_path.exists()
        aggregate_manifest = json.loads(
            aggregate_manifest_path.read_text(encoding="utf-8")
        )
        validate_ci_validation_aggregate_evidence_manifest(
            aggregate_manifest,
            plan=plan,
            expected_run_id=batch_contracts.RUN_ID,
            expected_run_attempt=batch_contracts.RUN_ATTEMPT,
        )
        validate_ci_validation_aggregate_summary(
            json.loads(summary_path.read_text(encoding="utf-8")),
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            expected_run_id=batch_contracts.RUN_ID,
            expected_run_attempt=batch_contracts.RUN_ATTEMPT,
        )
        assert "Traceback" not in completed.stderr
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_batch_aggregation_fails_closed_for_malformed_bundle() -> None:
    """Malformed batch bundle candidates are not admitted."""
    scratch = _ci_batch_bundle_scratch("batch-aggregation-malformed")
    try:
        observed_root = scratch / "observed"
        observed_root.mkdir()
        plan, manifest = _ci_batch_contract_plan_and_manifest()
        batch = cast("list[dict[str, object]]", manifest["batches"])[0]
        artifact_ref = cast("str", batch["expected-batch-evidence-bundle-ref"])
        artifact_dir = observed_root / artifact_physical_name(artifact_ref)
        artifact_dir.mkdir()
        (artifact_dir / "batch-evidence-bundle.json").write_text(
            "{",
            encoding="utf-8",
        )

        result, aggregate_manifest, summary = _aggregate_ci_batch_evidence(
            scratch,
            plan,
            manifest,
            observed_root,
        )

        assert result == 1
        slot = cast(
            "list[dict[str, object]]", aggregate_manifest["batch-bundles"]
        )[0]
        assert slot["slot-admissibility"] == "inadmissible"
        assert slot["admitted-candidate-id"] is None
        candidates = cast(
            "list[dict[str, object]]", slot["observed-candidates"]
        )
        assert candidates[0]["admissibility"] == "inadmissible"
        assert candidates[0]["diagnostics"]
        assert (
            cast("dict[str, object]", summary["reason"])[
                "inadmissible-batch-evidence"
            ]
            is True
        )
        assert all(
            row["admissibility"] != "valid"
            for row in cast("list[dict[str, object]]", summary["batch-bundles"])
        )
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


@pytest.mark.parametrize(
    "metadata_case",
    ["missing", "unreadable", "incomplete"],
)
def test_ci_batch_aggregation_fails_closed_for_invalid_local_metadata(
    metadata_case: str,
) -> None:
    """Trusted observations still require complete local artifact metadata."""
    scratch = _ci_batch_bundle_scratch(
        f"batch-aggregation-invalid-metadata-{metadata_case}"
    )
    try:
        observed_root = scratch / "observed"
        observed_root.mkdir()
        plan, manifest = _ci_batch_contract_plan_and_manifest()
        row = _ci_batch_matrix_rows(plan, manifest)[0]
        batch = cast("list[dict[str, object]]", manifest["batches"])[0]
        selector = cast(
            "list[dict[str, object]]",
            batch["ordered-selectors"],
        )[0]
        bundle = _write_ci_batch_bundle(
            scratch,
            plan,
            manifest,
            row,
            [
                _ci_success_validation_result(
                    plan,
                    cast("str", selector["work-group-id"]),
                )
            ],
        )
        _stage_ci_batch_bundle_artifact(observed_root, bundle)
        artifact_ref = cast("str", bundle["artifact-ref"])
        metadata_path = (
            observed_root
            / artifact_physical_name(artifact_ref)
            / "artifact-metadata.json"
        )
        if metadata_case == "missing":
            metadata_path.unlink()
        elif metadata_case == "unreadable":
            metadata_path.write_text("{", encoding="utf-8")
        else:
            metadata_path.write_text(
                json.dumps(
                    {
                        "artifact-ref": artifact_ref,
                        "artifact-instance-id": f"{batch['batch-id']}-artifact",
                    }
                ),
                encoding="utf-8",
            )

        result, aggregate_manifest, _summary = _aggregate_ci_batch_evidence(
            scratch,
            plan,
            manifest,
            observed_root,
        )

        assert result == 1
        candidate = cast(
            "list[dict[str, object]]",
            cast(
                "list[dict[str, object]]",
                aggregate_manifest["batch-bundles"],
            )[0]["observed-candidates"],
        )[0]
        assert candidate["producer-verification"] == "producer-unverified"
        assert candidate["admissibility"] == "inadmissible"
        diagnostics = cast("list[dict[str, object]]", candidate["diagnostics"])
        assert any(
            "metadata-" in cast("str", item["diagnostic-id"])
            for item in diagnostics
        )
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_batch_aggregation_fails_without_downloader_admission() -> None:
    """Valid local metadata is insufficient without downloader admission."""
    scratch = _ci_batch_bundle_scratch("batch-aggregation-missing-admission")
    try:
        observed_root = scratch / "observed"
        observed_root.mkdir()
        plan, manifest = _ci_batch_contract_plan_and_manifest()
        row = _ci_batch_matrix_rows(plan, manifest)[0]
        batch = cast("list[dict[str, object]]", manifest["batches"])[0]
        selector = cast(
            "list[dict[str, object]]",
            batch["ordered-selectors"],
        )[0]
        bundle = _write_ci_batch_bundle(
            scratch,
            plan,
            manifest,
            row,
            [
                _ci_success_validation_result(
                    plan,
                    cast("str", selector["work-group-id"]),
                )
            ],
        )
        _stage_ci_batch_bundle_artifact(observed_root, bundle)

        result, aggregate_manifest, summary = _aggregate_ci_batch_evidence(
            scratch,
            plan,
            manifest,
            observed_root,
            write_downloader_observation=False,
        )

        candidate = cast(
            "list[dict[str, object]]",
            cast(
                "list[dict[str, object]]",
                aggregate_manifest["batch-bundles"],
            )[0]["observed-candidates"],
        )[0]
        assert result == 1
        assert candidate["producer-verification"] == "producer-unverified"
        assert candidate["admissibility"] == "inadmissible"
        assert any(
            "metadata-downloader-admission" in diagnostic["diagnostic-id"]
            for diagnostic in cast(
                "list[dict[str, object]]",
                candidate["diagnostics"],
            )
        )
        assert summary["reason"]["inadmissible-batch-evidence"] is True
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


@pytest.mark.parametrize(
    "admission_case",
    ["not-listed", "mismatched-artifact-instance", "duplicate"],
)
def test_ci_batch_aggregation_rejects_bad_downloader_admission(
    admission_case: str,
) -> None:
    """Aggregate binds candidates to downloader-produced admissions."""
    scratch = _ci_batch_bundle_scratch(f"batch-aggregation-{admission_case}")
    try:
        observed_root = scratch / "observed"
        observed_root.mkdir()
        plan, manifest = _ci_batch_contract_plan_and_manifest()
        row = _ci_batch_matrix_rows(plan, manifest)[0]
        batch = cast("list[dict[str, object]]", manifest["batches"])[0]
        selector = cast(
            "list[dict[str, object]]",
            batch["ordered-selectors"],
        )[0]
        bundle = _write_ci_batch_bundle(
            scratch,
            plan,
            manifest,
            row,
            [
                _ci_success_validation_result(
                    plan,
                    cast("str", selector["work-group-id"]),
                )
            ],
        )
        _stage_ci_batch_bundle_artifact(observed_root, bundle)
        admissions = _ci_staged_downloader_admissions(observed_root, manifest)
        if admission_case == "not-listed":
            admissions = []
        elif admission_case == "mismatched-artifact-instance":
            admissions[0]["artifact-instance-id"] = "wrong-artifact"
        else:
            admissions = [admissions[0], dict(admissions[0])]
        (observed_root / control._CI_DOWNLOADER_OBSERVATION_FILE).write_text(
            json.dumps(
                {
                    control._CI_DOWNLOADER_ADMITTED_BATCH_ARTIFACTS_KEY: (
                        admissions
                    ),
                    "artifact-api-metadata-available": True,
                    "namespace-enumeration": "available",
                    "namespace-overflow": False,
                    "run-id": batch_contracts.RUN_ID,
                    "run-attempt": batch_contracts.RUN_ATTEMPT,
                }
            ),
            encoding="utf-8",
        )

        result, aggregate_manifest, summary = _aggregate_ci_batch_evidence(
            scratch,
            plan,
            manifest,
            observed_root,
        )

        candidate = cast(
            "list[dict[str, object]]",
            cast(
                "list[dict[str, object]]",
                aggregate_manifest["batch-bundles"],
            )[0]["observed-candidates"],
        )[0]
        assert result == 1
        assert candidate["producer-verification"] == "producer-unverified"
        assert candidate["admissibility"] == "inadmissible"
        assert any(
            "metadata-downloader-admission" in diagnostic["diagnostic-id"]
            for diagnostic in cast(
                "list[dict[str, object]]",
                candidate["diagnostics"],
            )
        )
        assert summary["reason"]["inadmissible-batch-evidence"] is True
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_batch_aggregation_fails_closed_for_slot_mismatch() -> None:
    """A valid payload staged in the wrong slot is inadmissible."""
    scratch = _ci_batch_bundle_scratch("batch-aggregation-slot-mismatch")
    try:
        observed_root = scratch / "observed"
        observed_root.mkdir()
        plan = cast("dict[str, object]", batch_contracts.plan())
        batch_contracts.add_dependent_work_group(plan)
        manifest = batch_contracts.manifest(plan)
        rows = _ci_batch_matrix_rows(plan, manifest)
        batches = cast("list[dict[str, object]]", manifest["batches"])
        base_batch = next(
            batch for batch in batches if not batch["depends-on-batches"]
        )
        dependent_batch = next(
            batch for batch in batches if batch["depends-on-batches"]
        )
        base_row = next(
            row
            for row in rows
            if cast("dict[str, object]", row["identity-matrix"])["batch-id"]
            == base_batch["batch-id"]
        )
        base_selector = cast(
            "list[dict[str, object]]",
            base_batch["ordered-selectors"],
        )[0]
        base_dir = scratch / "base"
        base_dir.mkdir()
        base_bundle = _write_ci_batch_bundle(
            base_dir,
            plan,
            manifest,
            base_row,
            [
                _ci_success_validation_result(
                    plan,
                    cast("str", base_selector["work-group-id"]),
                )
            ],
        )
        _stage_ci_batch_bundle_artifact(observed_root, base_bundle)
        wrong_slot_dir = observed_root / artifact_physical_name(
            cast("str", dependent_batch["expected-batch-evidence-bundle-ref"])
        )
        wrong_slot_dir.mkdir()
        (wrong_slot_dir / "batch-evidence-bundle.json").write_text(
            json.dumps(base_bundle),
            encoding="utf-8",
        )
        dependent_writer = cast(
            "dict[str, object]",
            dependent_batch["batch-writer"],
        )
        (wrong_slot_dir / "artifact-metadata.json").write_text(
            json.dumps(
                {
                    "artifact-ref": dependent_batch[
                        "expected-batch-evidence-bundle-ref"
                    ],
                    "physical-artifact-name": artifact_physical_name(
                        cast(
                            "str",
                            dependent_batch[
                                "expected-batch-evidence-bundle-ref"
                            ],
                        )
                    ),
                    "artifact-instance-id": "wrong-slot-artifact",
                    "run-id": batch_contracts.RUN_ID,
                    "run-attempt": batch_contracts.RUN_ATTEMPT,
                    "producer-boundary": "execution-batch",
                    "producer-job-identity": dependent_writer[
                        "expected-job-identity"
                    ],
                }
            ),
            encoding="utf-8",
        )

        result, aggregate_manifest, summary = _aggregate_ci_batch_evidence(
            scratch,
            plan,
            manifest,
            observed_root,
        )

        assert result == 1
        slots = {
            slot["batch-id"]: slot
            for slot in cast(
                "list[dict[str, object]]",
                aggregate_manifest["batch-bundles"],
            )
        }
        dependent_slot = cast(
            "dict[str, object]",
            slots[dependent_batch["batch-id"]],
        )
        assert dependent_slot["slot-admissibility"] == "inadmissible"
        assert dependent_slot["admitted-candidate-id"] is None
        candidates = cast(
            "list[dict[str, object]]",
            dependent_slot["observed-candidates"],
        )
        assert candidates[0]["admissibility"] == "inadmissible"
        assert any(
            diagnostic["detail"]
            == "execution-batch-manifest-bundle-ref-mismatch"
            for diagnostic in cast(
                "list[dict[str, object]]", candidates[0]["diagnostics"]
            )
        )
        assert (
            cast("dict[str, object]", summary["reason"])[
                "inadmissible-batch-evidence"
            ]
            is True
        )
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_batch_aggregation_fails_closed_for_duplicate_bundle() -> None:
    """Duplicate bundle candidates are not admitted for gating."""
    scratch = _ci_batch_bundle_scratch("batch-aggregation-duplicate")
    try:
        observed_root = scratch / "observed"
        observed_root.mkdir()
        plan, manifest = _ci_batch_contract_plan_and_manifest()
        row = _ci_batch_matrix_rows(plan, manifest)[0]
        batch = cast("list[dict[str, object]]", manifest["batches"])[0]
        selector = cast(
            "list[dict[str, object]]",
            batch["ordered-selectors"],
        )[0]
        bundle = _write_ci_batch_bundle(
            scratch,
            plan,
            manifest,
            row,
            [
                _ci_success_validation_result(
                    plan,
                    cast("str", selector["work-group-id"]),
                )
            ],
        )
        _stage_ci_batch_bundle_artifact(observed_root, bundle)
        duplicate_dir = observed_root / (
            artifact_physical_name(cast("str", bundle["artifact-ref"])) + "#2"
        )
        duplicate_dir.mkdir()
        (duplicate_dir / "batch-evidence-bundle.json").write_text(
            json.dumps(bundle),
            encoding="utf-8",
        )
        batch_data = cast("dict[str, object]", bundle["batch"])
        writer = cast("dict[str, object]", bundle["writer"])
        duplicate_instance_id = f"{batch_data['batch-id']}-artifact-2"
        (duplicate_dir / "artifact-metadata.json").write_text(
            json.dumps(
                {
                    "artifact-ref": bundle["artifact-ref"],
                    "physical-artifact-name": artifact_physical_name(
                        cast("str", bundle["artifact-ref"])
                    ),
                    "artifact-instance-id": duplicate_instance_id,
                    "run-id": batch_contracts.RUN_ID,
                    "run-attempt": batch_contracts.RUN_ATTEMPT,
                    "producer-boundary": "execution-batch",
                    "producer-job-identity": writer["expected-job-identity"],
                }
            ),
            encoding="utf-8",
        )

        result, aggregate_manifest, summary = _aggregate_ci_batch_evidence(
            scratch,
            plan,
            manifest,
            observed_root,
        )

        assert result == 1
        slot = cast(
            "list[dict[str, object]]", aggregate_manifest["batch-bundles"]
        )[0]
        assert slot["slot-admissibility"] == "duplicate"
        assert (
            cast("dict[str, object]", summary["reason"])[
                "inadmissible-batch-evidence"
            ]
            is True
        )
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_batch_aggregation_fails_closed_for_unexpected_artifact() -> None:
    """Off-manifest validation artifacts are namespace closure failures."""
    scratch = _ci_batch_bundle_scratch("batch-aggregation-unexpected")
    try:
        observed_root = scratch / "observed"
        observed_root.mkdir()
        plan, manifest = _ci_batch_contract_plan_and_manifest()
        batches = {
            cast("str", batch["batch-id"]): batch
            for batch in cast("list[dict[str, object]]", manifest["batches"])
        }
        for row in _ci_batch_matrix_rows(plan, manifest):
            batch_id = cast(
                "str",
                cast("dict[str, object]", row["identity-matrix"])["batch-id"],
            )
            selector = cast(
                "list[dict[str, object]]",
                batches[batch_id]["ordered-selectors"],
            )[0]
            bundle = _write_ci_batch_bundle(
                scratch,
                plan,
                manifest,
                row,
                [
                    _ci_success_validation_result(
                        plan, cast("str", selector["work-group-id"])
                    )
                ],
            )
            _stage_ci_batch_bundle_artifact(observed_root, bundle)
        unexpected_name = f"three-ci-validation-25887422010-1-{'9' * 64}"
        (observed_root / unexpected_name).mkdir()

        result, aggregate_manifest, summary = _aggregate_ci_batch_evidence(
            scratch,
            plan,
            manifest,
            observed_root,
        )

        assert result == 1
        assert (
            cast("dict[str, object]", summary["reason"])[
                "namespace-closure-failure"
            ]
            is True
        )
        assert aggregate_manifest["unexpected-contract-artifacts"]
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


@pytest.mark.parametrize(
    ("case_name", "observation"),
    [
        (
            "unavailable",
            {
                "artifact-api-metadata-available": False,
                "namespace-enumeration": "unavailable",
                "namespace-overflow": False,
                "run-id": batch_contracts.RUN_ID,
                "run-attempt": batch_contracts.RUN_ATTEMPT,
            },
        ),
        ("missing", None),
        ("malformed-object", {}),
        (
            "run-mismatch",
            {
                "artifact-api-metadata-available": True,
                "namespace-enumeration": "available",
                "namespace-overflow": False,
                "run-id": "999999",
                "run-attempt": batch_contracts.RUN_ATTEMPT,
            },
        ),
    ],
)
def test_ci_batch_aggregation_fails_closed_for_bad_downloader_observation(
    case_name: str,
    observation: Mapping[str, object] | None,
) -> None:
    """Downloader observation gaps keep namespace closure fail-closed."""
    scratch = _ci_batch_bundle_scratch(f"batch-aggregation-{case_name}")
    try:
        observed_root = scratch / "observed"
        observed_root.mkdir()
        if observation is not None:
            observation_path = (
                observed_root / control._CI_DOWNLOADER_OBSERVATION_FILE
            )
            observation_path.write_text(
                json.dumps(observation),
                encoding="utf-8",
            )
        plan, manifest = _ci_batch_contract_plan_and_manifest()
        batches = {
            cast("str", batch["batch-id"]): batch
            for batch in cast("list[dict[str, object]]", manifest["batches"])
        }
        for row in _ci_batch_matrix_rows(plan, manifest):
            batch_id = cast(
                "str",
                cast("dict[str, object]", row["identity-matrix"])["batch-id"],
            )
            selector = cast(
                "list[dict[str, object]]",
                batches[batch_id]["ordered-selectors"],
            )[0]
            bundle = _write_ci_batch_bundle(
                scratch,
                plan,
                manifest,
                row,
                [
                    _ci_success_validation_result(
                        plan, cast("str", selector["work-group-id"])
                    )
                ],
            )
            _stage_ci_batch_bundle_artifact(observed_root, bundle)

        result, aggregate_manifest, summary = _aggregate_ci_batch_evidence(
            scratch,
            plan,
            manifest,
            observed_root,
            write_downloader_observation=False,
        )

        namespace_overflow = cast(
            "dict[str, object]",
            aggregate_manifest["namespace-overflow"],
        )
        diagnostics = cast(
            "list[dict[str, object]]",
            namespace_overflow["diagnostics"],
        )
        assert result == 1
        assert namespace_overflow["detected"] is False
        assert any(
            diagnostic["detail"] == "namespace-enumeration-unavailable"
            for diagnostic in diagnostics
        )
        assert (
            cast("dict[str, object]", summary["reason"])[
                "namespace-closure-failure"
            ]
            is True
        )
        assert (
            cast("dict[str, object]", summary["reason"])["fail-closed"] is True
        )
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_batch_aggregation_uses_downloader_namespace_overflow() -> None:
    """Live downloader overflow closes beyond local lower bounds."""
    scratch = _ci_batch_bundle_scratch("batch-aggregation-downloader-overflow")
    try:
        observed_root = scratch / "observed"
        observed_root.mkdir()
        observation_path = (
            observed_root / control._CI_DOWNLOADER_OBSERVATION_FILE
        )
        observation_path.write_text(
            json.dumps(
                {
                    "artifact-api-metadata-available": True,
                    "namespace-enumeration": "available",
                    "namespace-overflow": True,
                    "run-id": batch_contracts.RUN_ID,
                    "run-attempt": batch_contracts.RUN_ATTEMPT,
                }
            ),
            encoding="utf-8",
        )
        plan, manifest = _ci_batch_contract_plan_and_manifest()
        batches = {
            cast("str", batch["batch-id"]): batch
            for batch in cast("list[dict[str, object]]", manifest["batches"])
        }
        for row in _ci_batch_matrix_rows(plan, manifest):
            batch_id = cast(
                "str",
                cast("dict[str, object]", row["identity-matrix"])["batch-id"],
            )
            selector = cast(
                "list[dict[str, object]]",
                batches[batch_id]["ordered-selectors"],
            )[0]
            bundle = _write_ci_batch_bundle(
                scratch,
                plan,
                manifest,
                row,
                [
                    _ci_success_validation_result(
                        plan, cast("str", selector["work-group-id"])
                    )
                ],
            )
            _stage_ci_batch_bundle_artifact(observed_root, bundle)
        observation = json.loads(observation_path.read_text(encoding="utf-8"))
        observation[control._CI_DOWNLOADER_ADMITTED_BATCH_ARTIFACTS_KEY] = (
            _ci_staged_downloader_admissions(observed_root, manifest)
        )
        observation_path.write_text(json.dumps(observation), encoding="utf-8")

        result, aggregate_manifest, summary = _aggregate_ci_batch_evidence(
            scratch,
            plan,
            manifest,
            observed_root,
        )

        namespace_overflow = cast(
            "dict[str, object]",
            aggregate_manifest["namespace-overflow"],
        )
        diagnostics = cast(
            "list[dict[str, object]]",
            namespace_overflow["diagnostics"],
        )
        assert result == 1
        assert namespace_overflow["detected"] is True
        assert (
            namespace_overflow["observed-prefixed-artifact-count-lower-bound"]
            <= control._CI_VALIDATION_LIVE_NAMESPACE_ARTIFACT_CAP
        )
        assert any(
            diagnostic["detail"] == "namespace-overflow"
            for diagnostic in diagnostics
        )
        assert summary["reason"]["namespace-closure-failure"] is True
        assert summary["reason"]["fail-closed"] is True
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_batch_aggregation_treats_not_required_snapshots_as_unexpected() -> (
    None
):
    """Snapshot artifact names are allowed only when admitted as inputs."""
    scratch = _ci_batch_bundle_scratch("batch-aggregation-optional-snapshot")
    try:
        observed_root = scratch / "observed"
        observed_root.mkdir()
        run_id = batch_contracts.RUN_ID
        run_attempt = batch_contracts.RUN_ATTEMPT
        changed_ref = control.ci_validation_changed_files_snapshot_artifact_ref(
            run_id=run_id,
            run_attempt=run_attempt,
        )
        input_artifacts = {
            "changed-files-snapshot": {
                "artifact-ref": None,
                "required": False,
                "admissibility": "not-required",
            },
            "request": {
                "artifact-ref": control.ci_validation_request_artifact_ref(
                    run_id=run_id,
                    run_attempt=run_attempt,
                ),
                "required": True,
                "admissibility": "valid",
            },
        }
        allowed_refs = control._ci_aggregate_allowed_observed_refs(
            input_artifacts=input_artifacts,
            expected_batch_refs=set(),
            run_id=run_id,
            run_attempt=run_attempt,
        )
        (observed_root / artifact_physical_name(changed_ref)).mkdir()

        unexpected = control._ci_aggregate_unexpected_artifacts(
            observed_root,
            run_id=run_id,
            run_attempt=run_attempt,
            expected_refs=allowed_refs,
        )

        assert [item["physical-artifact-name"] for item in unexpected] == [
            artifact_physical_name(changed_ref)
        ]
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_live_namespace_treats_not_required_snapshots_as_unexpected() -> (
    None
):
    """Live namespace closure allows snapshot names only when plan-required."""
    run_id = batch_contracts.RUN_ID
    run_attempt = batch_contracts.RUN_ATTEMPT
    changed_ref = control.ci_validation_changed_files_snapshot_artifact_ref(
        run_id=run_id,
        run_attempt=run_attempt,
    )
    plan = {
        "affected-range": {"changed-files-hash": None},
        "fact-snapshot": {"id": None},
    }

    unexpected = control._ci_live_unexpected_contract_artifact_names(
        {
            artifact_physical_name(changed_ref): [
                {"id": "changed-files-artifact"}
            ],
        },
        execution_batch_manifest=None,
        plan=plan,
        run_id=run_id,
        run_attempt=run_attempt,
    )

    assert unexpected == [artifact_physical_name(changed_ref)]


def test_ci_live_namespace_ignores_known_prior_attempt_artifacts() -> None:
    """Known prior-attempt artifacts are outside the current namespace."""
    run_id = batch_contracts.RUN_ID
    current_attempt = "2"
    current_ref = control.ci_validation_request_artifact_ref(
        run_id=run_id,
        run_attempt=current_attempt,
    )
    prior_ref = control.ci_validation_request_artifact_ref(
        run_id=run_id,
        run_attempt="1",
    )
    unknown_current_name = "three-ci-validation-25887422010-2-" + "e" * 64

    unexpected = control._ci_live_unexpected_contract_artifact_names(
        {
            artifact_physical_name(current_ref): [{"id": "current-request"}],
            artifact_physical_name(prior_ref): [{"id": "prior-request"}],
            unknown_current_name: [{"id": "unknown-current"}],
        },
        execution_batch_manifest=None,
        plan=None,
        run_id=run_id,
        run_attempt=current_attempt,
    )

    assert unexpected == [unknown_current_name]


def test_ci_live_namespace_overflow_excludes_known_prior_attempts() -> None:
    """Known prior attempts do not consume current namespace capacity."""
    run_id = batch_contracts.RUN_ID
    current_attempt = "2"
    current_ref = control.ci_validation_request_artifact_ref(
        run_id=run_id,
        run_attempt=current_attempt,
    )
    prior_ref = control.ci_validation_request_artifact_ref(
        run_id=run_id,
        run_attempt="1",
    )
    prior_name = artifact_physical_name(prior_ref)
    excluded_names = control._ci_known_prior_attempt_artifact_names(
        [current_ref],
        run_id=run_id,
        run_attempt=current_attempt,
    )
    artifact_api_by_name = {
        f"three-ci-validation-25887422010-2-{index:064x}": [{"id": index}]
        for index in range(control._CI_VALIDATION_LIVE_NAMESPACE_ARTIFACT_CAP)
    }
    artifact_api_by_name[prior_name] = [{"id": "prior"}]

    assert prior_name in excluded_names
    assert not control._ci_live_namespace_overflow_detected(
        artifact_api_by_name,
        run_id=run_id,
        run_attempt=current_attempt,
        excluded_prefixed_artifact_names=excluded_names,
    )


def test_ci_live_namespace_allows_required_snapshots() -> None:
    """Live namespace closure preserves plan-required snapshot handling."""
    run_id = batch_contracts.RUN_ID
    run_attempt = batch_contracts.RUN_ATTEMPT
    changed_ref = control.ci_validation_changed_files_snapshot_artifact_ref(
        run_id=run_id,
        run_attempt=run_attempt,
    )
    fact_ref = control.ci_validation_fact_snapshot_artifact_ref(
        run_id=run_id,
        run_attempt=run_attempt,
    )
    plan = {
        "affected-range": {"changed-files-hash": "0" * 64},
        "fact-snapshot": {"id": "1" * 64},
    }

    unexpected = control._ci_live_unexpected_contract_artifact_names(
        {
            artifact_physical_name(changed_ref): [
                {"id": "changed-files-artifact"}
            ],
            artifact_physical_name(fact_ref): [{"id": "fact-artifact"}],
        },
        execution_batch_manifest=None,
        plan=plan,
        run_id=run_id,
        run_attempt=run_attempt,
    )

    assert unexpected == []


def test_ci_batch_aggregation_allows_current_g5_manifest_only() -> None:
    """G5 batch namespace closure allows only the G5 final manifest ref."""
    scratch = _ci_batch_bundle_scratch("batch-aggregation-final-artifacts")
    try:
        observed_root = scratch / "observed"
        observed_root.mkdir()
        run_id = batch_contracts.RUN_ID
        run_attempt = batch_contracts.RUN_ATTEMPT
        obsolete_ref = (
            f"ci-validation/aggregate/{run_id}/{run_attempt}/"
            "ci-validation-aggregate.json"
        )
        g5_ref = control.ci_validation_aggregate_evidence_manifest_artifact_ref(
            run_id=run_id,
            run_attempt=run_attempt,
        )
        input_artifacts = {
            "request": {
                "artifact-ref": control.ci_validation_request_artifact_ref(
                    run_id=run_id,
                    run_attempt=run_attempt,
                ),
                "required": True,
                "admissibility": "valid",
            },
        }
        allowed_refs = control._ci_aggregate_allowed_observed_refs(
            input_artifacts=input_artifacts,
            expected_batch_refs=set(),
            run_id=run_id,
            run_attempt=run_attempt,
        )
        assert g5_ref in allowed_refs
        assert obsolete_ref not in allowed_refs

        (observed_root / artifact_physical_name(obsolete_ref)).mkdir()
        (observed_root / artifact_physical_name(g5_ref)).mkdir()

        unexpected = control._ci_aggregate_unexpected_artifacts(
            observed_root,
            run_id=run_id,
            run_attempt=run_attempt,
            expected_refs=allowed_refs,
        )

        assert [item["physical-artifact-name"] for item in unexpected] == [
            artifact_physical_name(obsolete_ref)
        ]
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_batch_aggregation_hash_suffix_exemption_is_batch_only() -> None:
    """Only expected batch bundle names may use downloaded-artifact suffixes."""
    scratch = _ci_batch_bundle_scratch("batch-aggregation-suffix-closure")
    try:
        observed_root = scratch / "observed"
        observed_root.mkdir()
        run_id = batch_contracts.RUN_ID
        run_attempt = batch_contracts.RUN_ATTEMPT
        _plan, manifest = _ci_batch_contract_plan_and_manifest()
        batch_ref = str(
            cast("Sequence[Mapping[str, object]]", manifest["batches"])[0][
                "expected-batch-evidence-bundle-ref"
            ]
        )
        request_ref = control.ci_validation_request_artifact_ref(
            run_id=run_id,
            run_attempt=run_attempt,
        )
        (observed_root / f"{artifact_physical_name(batch_ref)}#1").mkdir()
        (observed_root / f"{artifact_physical_name(request_ref)}#1").mkdir()

        unexpected = control._ci_aggregate_unexpected_artifacts(
            observed_root,
            run_id=run_id,
            run_attempt=run_attempt,
            expected_refs={batch_ref, request_ref},
            expected_batch_refs={batch_ref},
        )

        assert unexpected == [
            {
                "physical-artifact-name": artifact_physical_name(request_ref),
                "observed-physical-artifact-name": (
                    f"{artifact_physical_name(request_ref)}#1"
                ),
                "artifact-instance-id": (
                    f"{artifact_physical_name(request_ref)}#1"
                ),
                "classification": "unexpected",
                "diagnostics": unexpected[0]["diagnostics"],
            }
        ]
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_batch_aggregation_rejects_late_bundle_after_namespace_closure() -> (
    None
):
    """Late bundles cannot extend a closed final evidence set."""
    scratch = _ci_batch_bundle_scratch("batch-aggregation-late-bundle")
    try:
        observed_root = scratch / "observed"
        observed_root.mkdir()
        plan, manifest = _ci_batch_contract_plan_and_manifest()

        result, aggregate_manifest, summary = _aggregate_ci_batch_evidence(
            scratch,
            plan,
            manifest,
            observed_root,
            expected_execution_batch_manifest_artifact_id=None,
        )

        late_dir = scratch / "late-bundle"
        late_dir.mkdir()
        row = _ci_batch_matrix_rows(plan, manifest)[0]
        batch = cast("list[dict[str, object]]", manifest["batches"])[0]
        selector = cast(
            "list[dict[str, object]]",
            batch["ordered-selectors"],
        )[0]
        late_bundle = _write_ci_batch_bundle(
            late_dir,
            plan,
            manifest,
            row,
            [
                _ci_success_validation_result(
                    plan, cast("str", selector["work-group-id"])
                )
            ],
        )
        _stage_ci_batch_bundle_artifact(observed_root, late_bundle)

        assert result == 1
        closed_slot = cast(
            "list[dict[str, object]]", aggregate_manifest["batch-bundles"]
        )[0]
        assert closed_slot["slot-admissibility"] == "missing"
        assert closed_slot["admitted-candidate-id"] is None
        assert closed_slot["observed-candidates"] == []
        assert (
            observed_root
            / artifact_physical_name(cast("str", late_bundle["artifact-ref"]))
        ).is_dir()
        reason = cast("dict[str, object]", summary["reason"])
        assert any(reason.values())
        assert reason["final-evidence-failure"] is False
        assert not any(
            failure["kind"] == "final-evidence-failure"
            for failure in cast("list[dict[str, object]]", summary["failures"])
        )

        context = batch_contracts.authorizing_context_kwargs()
        with pytest.raises(ContractValidationError) as exc_info:
            validate_ci_validation_aggregate_summary(
                summary,
                plan=plan,
                aggregate_evidence_manifest=aggregate_manifest,
                admitted_batch_evidence_bundles=[late_bundle],
                execution_batch_manifest=manifest,
                request=cast("dict[str, object]", context["request"]),
                changed_files_snapshot=cast(
                    "dict[str, object]",
                    context["changed_files_snapshot"],
                ),
                fact_snapshot=cast(
                    "dict[str, object]",
                    context["fact_snapshot"],
                ),
                expected_run_id=batch_contracts.RUN_ID,
                expected_run_attempt=batch_contracts.RUN_ATTEMPT,
            )
        assert any(
            "must be admitted by aggregate evidence manifest" in issue.message
            for issue in exc_info.value.issues
        )
        validate_ci_validation_aggregate_summary(
            summary,
            plan=plan,
            aggregate_evidence_manifest=aggregate_manifest,
            admitted_batch_evidence_bundles=[],
            execution_batch_manifest=manifest,
            request=cast("dict[str, object]", context["request"]),
            changed_files_snapshot=cast(
                "dict[str, object]",
                context["changed_files_snapshot"],
            ),
            fact_snapshot=cast("dict[str, object]", context["fact_snapshot"]),
            expected_run_id=batch_contracts.RUN_ID,
            expected_run_attempt=batch_contracts.RUN_ATTEMPT,
        )
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_batch_aggregation_admits_dependent_bundles_topologically() -> None:
    """Dependent bundles validate with admitted upstream evidence."""
    scratch = _ci_batch_bundle_scratch("batch-aggregation-dependent")
    try:
        observed_root = scratch / "observed"
        observed_root.mkdir()
        plan = cast("dict[str, object]", batch_contracts.plan())
        batch_contracts.add_transitive_work_group(plan)
        manifest = batch_contracts.manifest(plan)
        rows = _ci_batch_matrix_rows(plan, manifest)
        bundle_paths: dict[str, Path] = {}
        staged_bundles: list[dict[str, object]] = []
        for batch in batch_contracts.dependent_batches(plan):
            batch_id = cast("str", batch["batch-id"])
            row = next(
                item
                for item in rows
                if cast("dict[str, object]", item["identity-matrix"])[
                    "batch-id"
                ]
                == batch_id
            )
            selector = cast(
                "list[dict[str, object]]",
                batch["ordered-selectors"],
            )[0]
            batch_dir = scratch / batch_id
            batch_dir.mkdir()
            dependency_paths = list(bundle_paths.values())
            bundle = _write_ci_batch_bundle(
                batch_dir,
                plan,
                manifest,
                row,
                [
                    _ci_success_validation_result(
                        plan,
                        cast("str", selector["work-group-id"]),
                    )
                ],
                dependency_bundles=dependency_paths,
            )
            bundle_paths[batch_id] = batch_dir / "batch-evidence-bundle.json"
            staged_bundles.append(bundle)
            _stage_ci_batch_bundle_artifact(observed_root, bundle)

        result, aggregate_manifest, summary = _aggregate_ci_batch_evidence(
            scratch,
            plan,
            manifest,
            observed_root,
        )

        assert result == 0
        assert summary["verdict"] == "passed"
        assert {
            row["admissibility"]
            for row in cast("list[dict[str, object]]", summary["batch-bundles"])
        } == {"valid"}
        assert len(staged_bundles) == len(
            cast("list[dict[str, object]]", aggregate_manifest["batch-bundles"])
        )
        context = batch_contracts.authorizing_context_kwargs()
        (
            _slots,
            admitted_bundles,
            unexpected,
        ) = control._ci_aggregate_batch_slots(
            plan=plan,
            request=cast("dict[str, object]", context["request"]),
            execution_batch_manifest=manifest,
            changed_files_snapshot=cast(
                "dict[str, object]",
                context["changed_files_snapshot"],
            ),
            fact_snapshot=cast("dict[str, object]", context["fact_snapshot"]),
            input_artifacts=cast(
                "dict[str, object]",
                aggregate_manifest["input-artifacts"],
            ),
            observed_artifacts_dir=str(observed_root),
            run_id=batch_contracts.RUN_ID,
            run_attempt=batch_contracts.RUN_ATTEMPT,
        )
        assert unexpected == []
        for admitted_bundle in admitted_bundles:
            batch = cast("dict[str, object]", admitted_bundle["batch"])
            batch_id = cast("str", batch["batch-id"])
            artifact_ref = cast("str", admitted_bundle["artifact-ref"])
            artifact_instance_id = f"{batch_id}-artifact"
            trusted_bundle = cast("Any", admitted_bundle)
            assert trusted_bundle.artifact_instance_id == artifact_instance_id
            assert trusted_bundle.admitted_candidate_id == (
                ci_validation_batch_evidence_candidate_id(
                    run_id=batch_contracts.RUN_ID,
                    run_attempt=batch_contracts.RUN_ATTEMPT,
                    batch_id=batch_id,
                    artifact_ref=artifact_ref,
                    artifact_instance_id=artifact_instance_id,
                    physical_artifact_name=artifact_physical_name(artifact_ref),
                )
            )
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_batch_aggregation_fails_closed_for_missing_upstream() -> None:
    """Dependent bundles are inadmissible when upstream evidence is absent."""
    scratch = _ci_batch_bundle_scratch("batch-aggregation-missing-upstream")
    try:
        observed_root = scratch / "observed"
        observed_root.mkdir()
        plan = cast("dict[str, object]", batch_contracts.plan())
        batch_contracts.add_dependent_work_group(plan)
        manifest = batch_contracts.manifest(plan)
        rows = _ci_batch_matrix_rows(plan, manifest)
        base_batch = next(
            batch
            for batch in batch_contracts.dependent_batches(plan)
            if not batch["depends-on-batches"]
        )
        dependent_batch = next(
            batch
            for batch in batch_contracts.dependent_batches(plan)
            if batch["depends-on-batches"]
        )
        base_row = next(
            item
            for item in rows
            if cast("dict[str, object]", item["identity-matrix"])["batch-id"]
            == base_batch["batch-id"]
        )
        row = next(
            item
            for item in rows
            if cast("dict[str, object]", item["identity-matrix"])["batch-id"]
            == dependent_batch["batch-id"]
        )
        base_selector = cast(
            "list[dict[str, object]]",
            base_batch["ordered-selectors"],
        )[0]
        selector = cast(
            "list[dict[str, object]]",
            dependent_batch["ordered-selectors"],
        )[0]
        base_dir = scratch / "base"
        base_dir.mkdir()
        _write_ci_batch_bundle(
            base_dir,
            plan,
            manifest,
            base_row,
            [
                _ci_success_validation_result(
                    plan,
                    cast("str", base_selector["work-group-id"]),
                )
            ],
        )
        dependent_dir = scratch / "dependent"
        dependent_dir.mkdir()
        bundle = _write_ci_batch_bundle(
            dependent_dir,
            plan,
            manifest,
            row,
            [
                _ci_success_validation_result(
                    plan,
                    cast("str", selector["work-group-id"]),
                )
            ],
            dependency_bundles=[base_dir / "batch-evidence-bundle.json"],
        )
        _stage_ci_batch_bundle_artifact(observed_root, bundle)

        result, aggregate_manifest, summary = _aggregate_ci_batch_evidence(
            scratch,
            plan,
            manifest,
            observed_root,
        )

        assert result == 1
        slots = cast(
            "list[dict[str, object]]", aggregate_manifest["batch-bundles"]
        )
        assert any(slot["slot-admissibility"] == "missing" for slot in slots)
        assert any(
            slot["slot-admissibility"] == "inadmissible" for slot in slots
        )
        assert (
            cast("dict[str, object]", summary["reason"])[
                "inadmissible-batch-evidence"
            ]
            is True
        )
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_batch_aggregation_fails_closed_for_duration_overrun() -> None:
    """Aggregate duration budget overruns force final evidence failure."""
    scratch = _ci_batch_bundle_scratch("batch-aggregation-duration-overrun")
    try:
        observed_root = scratch / "observed"
        observed_root.mkdir()
        plan, manifest = _ci_batch_contract_plan_and_manifest()
        row = _ci_batch_matrix_rows(plan, manifest)[0]
        batch = cast("list[dict[str, object]]", manifest["batches"])[0]
        selector = cast(
            "list[dict[str, object]]",
            batch["ordered-selectors"],
        )[0]
        bundle = _write_ci_batch_bundle(
            scratch,
            plan,
            manifest,
            row,
            [
                _ci_success_validation_result(
                    plan, cast("str", selector["work-group-id"])
                )
            ],
        )
        _stage_ci_batch_bundle_artifact(observed_root, bundle)

        result, _aggregate_manifest, summary = _aggregate_ci_batch_evidence(
            scratch,
            plan,
            manifest,
            observed_root,
            started_at="2026-05-14T21:00:00Z",
        )

        assert result == 1
        reason = cast("dict[str, object]", summary["reason"])
        assert reason["fail-closed"] is False
        assert reason["aggregate-duration-exceeded"] is True
        assert reason["final-evidence-failure"] is False
        assert summary["verdict"] == "failed"
        failures = cast("list[dict[str, object]]", summary["failures"])
        assert any(
            failure["kind"] == "aggregate-duration-exceeded"
            and cast("dict[str, object]", failure["diagnostic"])["detail"]
            == "aggregate-duration-exceeded"
            for failure in failures
        )
        assert not any(
            failure["kind"] == "final-evidence-failure" for failure in failures
        )
        assert not any(failure["kind"] == "fail-closed" for failure in failures)
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_batch_aggregation_ceilings_fractional_duration_overrun() -> None:
    """Sub-second aggregate overruns still exceed the hard max duration."""
    scratch = _ci_batch_bundle_scratch(
        "batch-aggregation-fractional-duration-overrun"
    )
    try:
        observed_root = scratch / "observed"
        observed_root.mkdir()
        plan, manifest = _ci_batch_contract_plan_and_manifest()
        row = _ci_batch_matrix_rows(plan, manifest)[0]
        batch = cast("list[dict[str, object]]", manifest["batches"])[0]
        selector = cast(
            "list[dict[str, object]]",
            batch["ordered-selectors"],
        )[0]
        bundle = _write_ci_batch_bundle(
            scratch,
            plan,
            manifest,
            row,
            [
                _ci_success_validation_result(
                    plan, cast("str", selector["work-group-id"])
                )
            ],
        )
        _stage_ci_batch_bundle_artifact(observed_root, bundle)

        result, _aggregate_manifest, summary = _aggregate_ci_batch_evidence(
            scratch,
            plan,
            manifest,
            observed_root,
            started_at="2026-05-14T21:08:00.100000Z",
            created_at="2026-05-14T21:10:01Z",
        )

        budgets = cast("dict[str, object]", summary["budgets"])
        assert result == 1
        assert budgets["aggregate-duration-seconds"] == 121
        assert (
            cast("dict[str, object]", summary["reason"])[
                "aggregate-duration-exceeded"
            ]
            is True
        )
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


@pytest.mark.parametrize(
    ("started_at", "completed_at"),
    [
        ("2026-05-14T21:10:00", "2026-05-14T21:10:01Z"),
        ("2026-05-14T21:10:00Z", "2026-05-14T21:10:01"),
        ("2026-05-14T21:10:00", "2026-05-14T21:10:01"),
        ("2026-05-14 21:10:00Z", "2026-05-14T21:10:01Z"),
        ("2026-05-14T21:10:00Z", "2026-05-14 21:10:01Z"),
    ],
)
def test_ci_aggregate_duration_rejects_non_contract_timestamps(
    started_at: str,
    completed_at: str,
) -> None:
    """Aggregate duration evidence requires contract RFC3339 timestamps."""
    assert (
        control._ci_aggregate_duration_seconds(started_at, completed_at)
        == control._CI_INVALID_AGGREGATE_DURATION_SECONDS
    )


@pytest.mark.parametrize(
    "started_at",
    [
        "not-a-timestamp",
        "2026-05-14T21:10:00",
        "2026-05-14T21:10:22Z",
    ],
)
def test_ci_batch_aggregation_fails_closed_for_invalid_duration(
    started_at: str,
) -> None:
    """Malformed or non-monotonic aggregate timestamps cannot bypass budgets."""
    scratch = _ci_batch_bundle_scratch("batch-aggregation-invalid-duration")
    try:
        observed_root = scratch / "observed"
        observed_root.mkdir()
        plan, manifest = _ci_batch_contract_plan_and_manifest()
        row = _ci_batch_matrix_rows(plan, manifest)[0]
        batch = cast("list[dict[str, object]]", manifest["batches"])[0]
        selector = cast(
            "list[dict[str, object]]",
            batch["ordered-selectors"],
        )[0]
        bundle = _write_ci_batch_bundle(
            scratch,
            plan,
            manifest,
            row,
            [
                _ci_success_validation_result(
                    plan, cast("str", selector["work-group-id"])
                )
            ],
        )
        _stage_ci_batch_bundle_artifact(observed_root, bundle)

        result, _aggregate_manifest, summary = _aggregate_ci_batch_evidence(
            scratch,
            plan,
            manifest,
            observed_root,
            started_at=started_at,
            created_at="2026-05-14T21:10:21Z",
        )

        budgets = cast("dict[str, object]", summary["budgets"])
        failures = cast("list[dict[str, object]]", summary["failures"])
        assert result == 1
        assert budgets["aggregate-duration-seconds"] == 121
        reason = cast("dict[str, object]", summary["reason"])
        assert reason["fail-closed"] is False
        assert reason["aggregate-duration-exceeded"] is True
        assert reason["final-evidence-failure"] is False
        assert not any(
            failure["kind"] == "final-evidence-failure" for failure in failures
        )
        assert not any(failure["kind"] == "fail-closed" for failure in failures)
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_batch_aggregation_prefinal_count_excludes_not_required_inputs() -> (
    None
):
    """Pre-final counts follow the aggregate input contract predicate."""
    input_artifacts = {
        "request": {
            "artifact-ref": (
                "ci-validation/requests/25887422010/1/"
                "ci-validation-request.json"
            ),
            "artifact-instance-id": "request-1",
            "content-digest": "0" * 64,
            "required": True,
            "expected-cardinality": 1,
            "admissibility": "valid",
        },
        "changed-files-snapshot": {
            "artifact-ref": None,
            "artifact-instance-id": None,
            "content-digest": None,
            "required": False,
            "expected-cardinality": 0,
            "admissibility": "not-required",
        },
    }

    assert control._ci_aggregate_pre_final_input_count(input_artifacts) == 1


def test_ci_batch_writer_writes_category_result_bundle() -> None:
    """Planned-capabilities-null category evidence matches bundle contracts."""
    scratch = _ci_batch_bundle_scratch("category-result-batch-bundle")
    try:
        plan = cast("dict[str, object]", batch_contracts.plan())
        profile_id = "profile-lightweight"
        coverage_target = {
            "type": "lightweight-policy",
            "id": "known-non-impacting",
        }
        lightweight_group = {
            "work-group-id": "wg-lightweight",
            "kind": "lightweight-preflight",
            "coverage-target": coverage_target,
            "ecosystem": None,
            "runner-family": "ubuntu",
            "selector-variant": None,
            "depends-on": [],
            "expected-evidence": {
                "category": "lightweight-preflight",
                "planned-capabilities": None,
                "detail-profile": profile_id,
                "required": True,
            },
        }
        work_groups = cast("list[dict[str, object]]", plan["work-groups"])
        terminal_group = next(
            group
            for group in work_groups
            if group["kind"] == "evidence-aggregation"
        )
        work_groups.insert(-1, lightweight_group)
        terminal_group["depends-on"] = sorted(
            [*cast("list[str]", terminal_group["depends-on"]), "wg-lightweight"]
        )
        cast("list[dict[str, object]]", plan["evidence-expectations"]).append(
            {
                "evidence-expectation-id": "evidence-lightweight",
                "work-group-id": "wg-lightweight",
                "coverage-target": coverage_target,
                "category": "lightweight-preflight",
                "planned-capabilities": None,
                "detail-profile": profile_id,
                "required": True,
                "blocking-if-missing": True,
            }
        )
        cast("list[dict[str, object]]", plan["validation-obligations"]).append(
            {
                "validation-obligation-id": "validation-lightweight",
                "source-impact-ids": ["impact-example"],
                "kind": "lightweight-preflight",
                "coverage-target": coverage_target,
                "required": True,
                "blocking": True,
                "work-group-id": "wg-lightweight",
                "expected-evidence-id": "evidence-lightweight",
            }
        )
        plan["detail-profiles"] = [
            batch_contracts.tooling_detail_profile(
                profile_id=profile_id,
                category="lightweight-preflight",
                coverage_target=coverage_target,
            )
        ]
        work_groups.sort(key=lambda item: str(item["work-group-id"]))
        cast("list[dict[str, object]]", plan["evidence-expectations"]).sort(
            key=lambda item: str(item["evidence-expectation-id"])
        )
        cast("list[dict[str, object]]", plan["validation-obligations"]).sort(
            key=lambda item: str(item["validation-obligation-id"])
        )
        plan["plan-digest"] = ci_validation_plan_digest(plan)
        authorizing_context = batch_contracts.authorizing_context_kwargs()
        materialization = (
            batch_contracts.materialize_ci_validation_execution_batches(
                plan=plan,
                **authorizing_context,
                created_at=batch_contracts.CREATED_AT,
                execution_workflow="CI Validation",
            )
        )
        manifest = cast("dict[str, object]", materialization.manifest)
        matrix = ci_validation_execution_batch_matrix(
            manifest,
            plan=plan,
            request=cast("dict[str, object]", authorizing_context["request"]),
            changed_files_snapshot=cast(
                "dict[str, object]",
                authorizing_context["changed_files_snapshot"],
            ),
            fact_snapshot=cast(
                "dict[str, object]", authorizing_context["fact_snapshot"]
            ),
            expected_run_id=batch_contracts.RUN_ID,
            expected_run_attempt=batch_contracts.RUN_ATTEMPT,
        )
        rows = cast("list[dict[str, object]]", matrix["include"])
        row = next(
            item
            for item in rows
            if any(
                cast(
                    "dict[str, object]",
                    cast(
                        "dict[str, object]", selector["expected-evidence-slot"]
                    )["evidence"],
                )["planned-capabilities"]
                is None
                for selector in cast(
                    "list[dict[str, object]]",
                    next(
                        batch
                        for batch in cast(
                            "list[dict[str, object]]", manifest["batches"]
                        )
                        if batch["batch-id"]
                        == cast("dict[str, object]", item["identity-matrix"])[
                            "batch-id"
                        ]
                    )["ordered-selectors"],
                )
            )
        )

        bundle = _write_ci_batch_bundle(
            scratch,
            plan,
            manifest,
            row,
            [],
            authorizing_context=authorizing_context,
        )

        validate_ci_validation_batch_evidence_bundle(
            bundle,
            plan=plan,
            request=cast(
                "dict[str, object]",
                authorizing_context["request"],
            ),
            execution_batch_manifest=manifest,
            changed_files_snapshot=cast(
                "dict[str, object]",
                authorizing_context["changed_files_snapshot"],
            ),
            fact_snapshot=cast(
                "dict[str, object]",
                authorizing_context["fact_snapshot"],
            ),
            expected_run_id=batch_contracts.RUN_ID,
            expected_run_attempt=batch_contracts.RUN_ATTEMPT,
        )
        category_evidence = next(
            cast("dict[str, object]", selector["evidence"])
            for selector in cast(
                "list[dict[str, object]]", bundle["selector-results"]
            )
            if cast("dict[str, object]", selector["evidence"])[
                "planned-capabilities"
            ]
            is None
        )
        category_result = cast(
            "dict[str, object]", category_evidence["category-result"]
        )
        assert category_result["category"] == category_evidence["category"]
        assert "detail" not in category_result
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_batch_writer_cli_writes_bundle_and_outputs() -> None:
    """Public batch evidence bundle CLI writes the bundle and output digests."""
    scratch = _ci_batch_bundle_scratch("batch-bundle-cli")
    try:
        plan, manifest = _ci_batch_contract_plan_and_manifest()
        row = _ci_batch_matrix_rows(plan, manifest)[0]
        batch = cast("list[dict[str, object]]", manifest["batches"])[0]
        selector = cast(
            "list[dict[str, object]]",
            batch["ordered-selectors"],
        )[0]
        result = _ci_success_validation_result(
            plan,
            cast("str", selector["work-group-id"]),
        )
        (
            plan_path,
            request_path,
            manifest_path,
            changed_files_path,
            fact_snapshot_path,
            matrix_path,
            result_paths,
            bundle_path,
        ) = _write_ci_batch_bundle_inputs(
            scratch, plan, manifest, row, [result]
        )
        output_path = scratch / "github-output.txt"
        command = [
            sys.executable,
            "eng/scripts/workflow_release_control.py",
            "write-ci-validation-batch-evidence-bundle",
            "--plan",
            str(plan_path),
            "--request",
            str(request_path),
            "--execution-batch-manifest",
            str(manifest_path),
            "--changed-files-snapshot",
            str(changed_files_path),
            "--fact-snapshot",
            str(fact_snapshot_path),
            "--matrix-row-json",
            matrix_path.read_text(encoding="utf-8"),
            "--expected-run-id",
            batch_contracts.RUN_ID,
            "--expected-run-attempt",
            batch_contracts.RUN_ATTEMPT,
            "--workflow",
            "CI Validation",
            "--job",
            "execution-batch",
            "--observed-commit-sha",
            batch_contracts.TREE_SHA,
            "--validation-result",
            str(result_paths[0]),
            "--bundle-out",
            str(bundle_path),
            "--github-output",
            str(output_path),
        ]

        completed = subprocess.run(  # noqa: S603
            command,
            cwd=REPO_ROOT,
            text=True,
            capture_output=True,
            check=False,
        )

        assert completed.returncode == 0, completed.stderr
        bundle = json.loads(bundle_path.read_text(encoding="utf-8"))
        outputs = _github_outputs(output_path)
        assert outputs["batch_id"] == batch["batch-id"]
        assert outputs["batch_evidence_bundle_ref"] == bundle["artifact-ref"]
        assert outputs["batch_evidence_bundle_payload_digest"] == (
            ci_validation_batch_evidence_bundle_payload_digest(bundle)
        )
        assert outputs["execution_batch_manifest_payload_digest"] == (
            ci_validation_execution_batch_manifest_payload_digest(manifest)
        )
        rejected = subprocess.run(  # noqa: S603
            [*command, "--dependency-artifact-admission", "{}"],
            cwd=REPO_ROOT,
            text=True,
            capture_output=True,
            check=False,
        )
        assert rejected.returncode != 0
        assert "unrecognized arguments: --dependency-artifact-admission" in (
            rejected.stderr
        )
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_execution_batch_manifest_cli_materializes_manifest_and_outputs():
    """Public CLI materializes G5 execution-batch manifests."""
    scratch = _ci_batch_bundle_scratch("execution-batch-manifest-cli")
    try:
        plan = cast("dict[str, object]", batch_contracts.plan())
        context = batch_contracts.authorizing_context_kwargs()
        plan_path = scratch / "plan.json"
        request_path = scratch / "request.json"
        changed_files_path = scratch / "changed-files.json"
        fact_snapshot_path = scratch / "fact-snapshot.json"
        manifest_path = scratch / "execution-batch-manifest.json"
        output_path = scratch / "github-output.txt"
        plan_path.write_text(json.dumps(plan), encoding="utf-8")
        request_path.write_text(
            json.dumps(context["request"]),
            encoding="utf-8",
        )
        changed_files_path.write_text(
            json.dumps(context["changed_files_snapshot"]),
            encoding="utf-8",
        )
        fact_snapshot_path.write_text(
            json.dumps(context["fact_snapshot"]),
            encoding="utf-8",
        )

        completed = subprocess.run(  # noqa: S603
            [
                sys.executable,
                "eng/scripts/workflow_release_control.py",
                "materialize-ci-validation-execution-batches",
                "--plan",
                str(plan_path),
                "--request",
                str(request_path),
                "--changed-files-snapshot",
                str(changed_files_path),
                "--fact-snapshot",
                str(fact_snapshot_path),
                "--workflow",
                "CI Validation",
                "--expected-run-id",
                batch_contracts.RUN_ID,
                "--expected-run-attempt",
                batch_contracts.RUN_ATTEMPT,
                "--created-at",
                batch_contracts.CREATED_AT,
                "--execution-batch-manifest-out",
                str(manifest_path),
                "--github-output",
                str(output_path),
            ],
            cwd=REPO_ROOT,
            text=True,
            capture_output=True,
            check=False,
        )

        assert completed.returncode == 0, completed.stderr
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        control.validate_ci_validation_execution_batch_manifest(
            manifest,
            plan=plan,
            request=cast("dict[str, object]", context["request"]),
            changed_files_snapshot=cast(
                "dict[str, object]",
                context["changed_files_snapshot"],
            ),
            fact_snapshot=cast("dict[str, object]", context["fact_snapshot"]),
            expected_run_id=batch_contracts.RUN_ID,
            expected_run_attempt=batch_contracts.RUN_ATTEMPT,
        )
        outputs = _github_outputs(output_path)
        assert outputs["execution_batch_manifest_ref"] == (
            control.ci_validation_execution_batch_manifest_artifact_ref(
                run_id=batch_contracts.RUN_ID,
                run_attempt=batch_contracts.RUN_ATTEMPT,
            )
        )
        assert outputs["execution_batch_manifest_payload_digest"] == (
            ci_validation_execution_batch_manifest_payload_digest(manifest)
        )
        assert json.loads(outputs["execution_batch_matrix"])["include"]
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_batch_writer_cli_rejects_stale_run_attempt() -> None:
    """CLI batch evidence writing is bound to the current run attempt."""
    scratch = _ci_batch_bundle_scratch("batch-bundle-cli-stale-attempt")
    try:
        plan, manifest = _ci_batch_contract_plan_and_manifest()
        row = _ci_batch_matrix_rows(plan, manifest)[0]
        batch = cast("list[dict[str, object]]", manifest["batches"])[0]
        selector = cast(
            "list[dict[str, object]]",
            batch["ordered-selectors"],
        )[0]
        result = _ci_success_validation_result(
            plan,
            cast("str", selector["work-group-id"]),
        )
        (
            plan_path,
            request_path,
            manifest_path,
            changed_files_path,
            fact_snapshot_path,
            matrix_path,
            result_paths,
            bundle_path,
        ) = _write_ci_batch_bundle_inputs(
            scratch, plan, manifest, row, [result]
        )

        completed = subprocess.run(  # noqa: S603
            [
                sys.executable,
                "eng/scripts/workflow_release_control.py",
                "write-ci-validation-batch-evidence-bundle",
                "--plan",
                str(plan_path),
                "--request",
                str(request_path),
                "--execution-batch-manifest",
                str(manifest_path),
                "--changed-files-snapshot",
                str(changed_files_path),
                "--fact-snapshot",
                str(fact_snapshot_path),
                "--matrix-row-json",
                matrix_path.read_text(encoding="utf-8"),
                "--expected-run-id",
                batch_contracts.RUN_ID,
                "--expected-run-attempt",
                "stale-attempt",
                "--workflow",
                "CI Validation",
                "--job",
                "execution-batch",
                "--observed-commit-sha",
                batch_contracts.TREE_SHA,
                "--validation-result",
                str(result_paths[0]),
                "--bundle-out",
                str(bundle_path),
                "--github-output",
                str(scratch / "github-output.txt"),
            ],
            cwd=REPO_ROOT,
            text=True,
            capture_output=True,
            check=False,
        )

        assert completed.returncode != 0
        assert "expected run" in completed.stderr
        assert not bundle_path.exists()
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_batch_writer_rejects_stale_current_run() -> None:
    """Current run id and attempt are authoritative for batch writing."""
    scratch = _ci_batch_bundle_scratch("batch-stale-current-run")
    try:
        plan, manifest = _ci_batch_contract_plan_and_manifest()
        row = _ci_batch_matrix_rows(plan, manifest)[0]
        batch = cast("list[dict[str, object]]", manifest["batches"])[0]
        selector = cast(
            "list[dict[str, object]]",
            batch["ordered-selectors"],
        )[0]
        result = _ci_success_validation_result(
            plan,
            cast("str", selector["work-group-id"]),
        )

        with pytest.raises(ContractValidationError, match="expected run"):
            _write_ci_batch_bundle(
                scratch,
                plan,
                manifest,
                row,
                [result],
                expected_run_id="stale-run",
            )
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_batch_writer_rejects_stale_current_run_attempt() -> None:
    """Current run attempt is independently authoritative for batch writing."""
    scratch = _ci_batch_bundle_scratch("batch-stale-current-run-attempt")
    try:
        plan, manifest = _ci_batch_contract_plan_and_manifest()
        row = _ci_batch_matrix_rows(plan, manifest)[0]
        batch = cast("list[dict[str, object]]", manifest["batches"])[0]
        selector = cast(
            "list[dict[str, object]]",
            batch["ordered-selectors"],
        )[0]
        result = _ci_success_validation_result(
            plan,
            cast("str", selector["work-group-id"]),
        )

        with pytest.raises(ContractValidationError, match="expected run"):
            _write_ci_batch_bundle(
                scratch,
                plan,
                manifest,
                row,
                [result],
                expected_run_attempt="stale-attempt",
            )
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_batch_writer_rejects_stale_request() -> None:
    """The request artifact must authorize the execution-batch manifest."""
    scratch = _ci_batch_bundle_scratch("batch-stale-request")
    try:
        plan, manifest = _ci_batch_contract_plan_and_manifest()
        row = _ci_batch_matrix_rows(plan, manifest)[0]
        batch = cast("list[dict[str, object]]", manifest["batches"])[0]
        selector = cast(
            "list[dict[str, object]]",
            batch["ordered-selectors"],
        )[0]
        result = _ci_success_validation_result(
            plan,
            cast("str", selector["work-group-id"]),
        )
        (
            plan_path,
            request_path,
            manifest_path,
            changed_files_path,
            fact_snapshot_path,
            matrix_path,
            result_paths,
            bundle_path,
        ) = _write_ci_batch_bundle_inputs(
            scratch, plan, manifest, row, [result]
        )
        request = json.loads(request_path.read_text(encoding="utf-8"))
        cast("dict[str, object]", request["run"])["run-id"] = "stale-run"
        request_path.write_text(json.dumps(request), encoding="utf-8")

        with pytest.raises(ContractValidationError):
            control._cmd_write_ci_validation_batch_evidence_bundle(
                argparse.Namespace(
                    plan=str(plan_path),
                    request=str(request_path),
                    execution_batch_manifest=str(manifest_path),
                    changed_files_snapshot=str(changed_files_path),
                    fact_snapshot=str(fact_snapshot_path),
                    matrix_row_json=matrix_path.read_text(encoding="utf-8"),
                    expected_run_id=batch_contracts.RUN_ID,
                    expected_run_attempt=batch_contracts.RUN_ATTEMPT,
                    workflow="CI Validation",
                    job="execution-batch",
                    observed_commit_sha=batch_contracts.TREE_SHA,
                    validation_result=[str(path) for path in result_paths],
                    dependency_results_json="",
                    dependency_bundle=[],
                    started_at=batch_contracts.CREATED_AT,
                    completed_at=batch_contracts.CREATED_AT,
                    created_at=batch_contracts.CREATED_AT,
                    bundle_out=str(bundle_path),
                    github_output="",
                )
            )
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_batch_writer_rejects_stale_request_run_attempt() -> None:
    """The request run attempt must authorize the execution-batch manifest."""
    scratch = _ci_batch_bundle_scratch("batch-stale-request-run-attempt")
    try:
        plan, manifest = _ci_batch_contract_plan_and_manifest()
        row = _ci_batch_matrix_rows(plan, manifest)[0]
        batch = cast("list[dict[str, object]]", manifest["batches"])[0]
        selector = cast(
            "list[dict[str, object]]",
            batch["ordered-selectors"],
        )[0]
        result = _ci_success_validation_result(
            plan,
            cast("str", selector["work-group-id"]),
        )
        (
            plan_path,
            request_path,
            manifest_path,
            changed_files_path,
            fact_snapshot_path,
            matrix_path,
            result_paths,
            bundle_path,
        ) = _write_ci_batch_bundle_inputs(
            scratch, plan, manifest, row, [result]
        )
        request = json.loads(request_path.read_text(encoding="utf-8"))
        cast("dict[str, object]", request["run"])["run-attempt"] = (
            "stale-attempt"
        )
        request_path.write_text(json.dumps(request), encoding="utf-8")

        with pytest.raises(ContractValidationError):
            control._cmd_write_ci_validation_batch_evidence_bundle(
                argparse.Namespace(
                    plan=str(plan_path),
                    request=str(request_path),
                    execution_batch_manifest=str(manifest_path),
                    changed_files_snapshot=str(changed_files_path),
                    fact_snapshot=str(fact_snapshot_path),
                    matrix_row_json=matrix_path.read_text(encoding="utf-8"),
                    expected_run_id=batch_contracts.RUN_ID,
                    expected_run_attempt=batch_contracts.RUN_ATTEMPT,
                    workflow="CI Validation",
                    job="execution-batch",
                    observed_commit_sha=batch_contracts.TREE_SHA,
                    validation_result=[str(path) for path in result_paths],
                    dependency_results_json="",
                    dependency_bundle=[],
                    started_at=batch_contracts.CREATED_AT,
                    completed_at=batch_contracts.CREATED_AT,
                    created_at=batch_contracts.CREATED_AT,
                    bundle_out=str(bundle_path),
                    github_output="",
                )
            )
        assert not bundle_path.exists()
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_validation_batch_evidence_writer_isolates_matrix_legs() -> None:
    """Each matrix leg can write only the selected execution batch bundle."""
    scratch = _ci_batch_bundle_scratch("multi-batch-bundle")
    try:
        plan = cast("dict[str, object]", batch_contracts.plan())
        batch_contracts.add_dependent_work_group(plan)
        batches = batch_contracts.dependent_batches(plan)
        manifest = (
            batch_contracts.freeze_ci_validation_execution_batch_manifest(
                plan=plan,
                **batch_contracts.authorizing_context_kwargs(),
                batches=batches,
                budget=batch_contracts.budget(2),
                created_at=batch_contracts.CREATED_AT,
            )
        )
        rows = _ci_batch_matrix_rows(plan, manifest)
        rows = sorted(
            rows,
            key=lambda item: len(
                cast(
                    "list[object]",
                    next(
                        batch
                        for batch in batches
                        if batch["batch-id"]
                        == cast(
                            "dict[str, object]",
                            item["identity-matrix"],
                        )["batch-id"]
                    )["depends-on-batches"],
                )
            ),
        )
        bundles = []
        for index, row in enumerate(rows):
            leg_scratch = scratch / f"leg-{index}"
            leg_scratch.mkdir()
            batch = next(
                item
                for item in cast("list[dict[str, object]]", manifest["batches"])
                if item["batch-id"]
                == cast("dict[str, object]", row["identity-matrix"])["batch-id"]
            )
            selector = cast(
                "list[dict[str, object]]",
                batch["ordered-selectors"],
            )[0]
            depends_on = cast("list[object]", selector["depends-on"])
            dependency_results_json = ""
            if depends_on:
                dependency_results_json = json.dumps(
                    {
                        selector["work-group-id"]: [
                            {
                                "work-group-id": depends_on[0],
                                "source-batch-id": batches[0]["batch-id"],
                                "outcome": "satisfied",
                                "admitted-for-gating": True,
                            }
                        ]
                    }
                )
            dependency_bundles = (
                [scratch / "leg-0" / "batch-evidence-bundle.json"]
                if depends_on
                else []
            )
            bundles.append(
                _write_ci_batch_bundle(
                    leg_scratch,
                    plan,
                    manifest,
                    row,
                    [
                        _ci_success_validation_result(
                            plan,
                            cast("str", selector["work-group-id"]),
                        )
                    ],
                    dependency_results_json=dependency_results_json,
                    dependency_bundles=dependency_bundles,
                )
            )

        bundle_batch_ids = {
            cast("dict[str, object]", bundle["batch"])["batch-id"]
            for bundle in bundles
        }
        manifest_batch_ids = {
            batch["batch-id"]
            for batch in cast("list[dict[str, object]]", manifest["batches"])
        }
        assert bundle_batch_ids == manifest_batch_ids
        assert bundles[0]["artifact-ref"] != bundles[1]["artifact-ref"]
        assert all(
            len(cast("list[object]", bundle["selector-results"])) == 1
            for bundle in bundles
        )
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_batch_writer_requires_transitive_dependency_bundles() -> None:
    """Writer validates B against A before C can cite B as authority."""
    scratch = _ci_batch_bundle_scratch("batch-transitive-dependency-bundles")
    try:
        plan = cast("dict[str, object]", batch_contracts.plan())
        batch_contracts.add_transitive_work_group(plan)
        manifest = batch_contracts.manifest(plan)
        rows_by_batch = {
            cast("dict[str, object]", row["identity-matrix"])["batch-id"]: row
            for row in _ci_batch_matrix_rows(plan, manifest)
        }
        batch_by_group = {
            cast("str", selector["work-group-id"]): batch
            for batch in cast("list[dict[str, object]]", manifest["batches"])
            for selector in cast(
                "list[dict[str, object]]", batch["ordered-selectors"]
            )
        }
        base_batch = batch_by_group["wg-python-gate"]
        dependent_batch = batch_by_group["wg-dependent-gate"]
        transitive_batch = batch_by_group["wg-transitive-gate"]

        base_dir = scratch / "base"
        base_dir.mkdir()
        base_selector = cast(
            "list[dict[str, object]]", base_batch["ordered-selectors"]
        )[0]
        base_bundle = _write_ci_batch_bundle(
            base_dir,
            plan,
            manifest,
            rows_by_batch[base_batch["batch-id"]],
            [
                _ci_success_validation_result(
                    plan,
                    cast("str", base_selector["work-group-id"]),
                )
            ],
        )
        base_path = base_dir / "batch-evidence-bundle.json"

        dependent_dir = scratch / "dependent"
        dependent_dir.mkdir()
        dependent_selector = cast(
            "list[dict[str, object]]", dependent_batch["ordered-selectors"]
        )[0]
        dependent_dependency_rows = json.dumps(
            {
                dependent_selector["work-group-id"]: [
                    {
                        "work-group-id": "wg-python-gate",
                        "source-batch-id": base_batch["batch-id"],
                        "outcome": "satisfied",
                        "admitted-for-gating": True,
                    }
                ]
            }
        )
        dependent_bundle = _write_ci_batch_bundle(
            dependent_dir,
            plan,
            manifest,
            rows_by_batch[dependent_batch["batch-id"]],
            [
                _ci_success_validation_result(
                    plan,
                    cast("str", dependent_selector["work-group-id"]),
                )
            ],
            dependency_results_json=dependent_dependency_rows,
            dependency_bundles=[base_path],
        )
        dependent_dependency = cast(
            "list[dict[str, object]]",
            cast(
                "list[dict[str, object]]",
                dependent_bundle["selector-results"],
            )[0]["dependency-results"],
        )[0]
        base_artifact_ref = cast("str", base_bundle["artifact-ref"])
        base_instance_id = f"{base_batch['batch-id']}-artifact"
        assert (
            dependent_dependency["upstream-artifact-instance-id"]
            == base_instance_id
        )
        assert dependent_dependency[
            "upstream-admitted-candidate-id"
        ] == ci_validation_batch_evidence_candidate_id(
            run_id=batch_contracts.RUN_ID,
            run_attempt=batch_contracts.RUN_ATTEMPT,
            batch_id=cast("str", base_batch["batch-id"]),
            artifact_ref=base_artifact_ref,
            artifact_instance_id=base_instance_id,
            physical_artifact_name=artifact_physical_name(base_artifact_ref),
        )
        trusted_base_path = (
            dependent_dir
            / "observed-artifacts"
            / artifact_physical_name(base_artifact_ref)
            / "batch-evidence-bundle.json"
        )
        trusted_base = control._ci_authoritative_dependency_bundles(
            [str(trusted_base_path)],
            plan=plan,
            request=cast(
                "dict[str, object]",
                batch_contracts.authorizing_context_kwargs()["request"],
            ),
            execution_batch_manifest=manifest,
            changed_files_snapshot=cast(
                "dict[str, object]",
                batch_contracts.authorizing_context_kwargs()[
                    "changed_files_snapshot"
                ],
            ),
            fact_snapshot=cast(
                "dict[str, object]",
                batch_contracts.authorizing_context_kwargs()["fact_snapshot"],
            ),
            observed_artifacts_dir=str(dependent_dir / "observed-artifacts"),
            expected_run_id=batch_contracts.RUN_ID,
            expected_run_attempt=batch_contracts.RUN_ATTEMPT,
            dependency_artifact_admissions=[
                _dependency_admission_for_staged_bundle(trusted_base_path)
            ],
        )
        missing_identity_bundle = deepcopy(dependent_bundle)
        missing_dependency = cast(
            "list[dict[str, object]]",
            cast(
                "list[dict[str, object]]",
                missing_identity_bundle["selector-results"],
            )[0]["dependency-results"],
        )[0]
        del missing_dependency["upstream-artifact-instance-id"]
        with pytest.raises(ContractValidationError):
            validate_ci_validation_batch_evidence_bundle(
                missing_identity_bundle,
                plan=plan,
                request=cast(
                    "dict[str, object]",
                    batch_contracts.authorizing_context_kwargs()["request"],
                ),
                execution_batch_manifest=manifest,
                changed_files_snapshot=cast(
                    "dict[str, object]",
                    batch_contracts.authorizing_context_kwargs()[
                        "changed_files_snapshot"
                    ],
                ),
                fact_snapshot=cast(
                    "dict[str, object]",
                    batch_contracts.authorizing_context_kwargs()[
                        "fact_snapshot"
                    ],
                ),
                expected_run_id=batch_contracts.RUN_ID,
                expected_run_attempt=batch_contracts.RUN_ATTEMPT,
                dependency_evidence_bundles=trusted_base,
            )
        dependent_path = dependent_dir / "batch-evidence-bundle.json"

        transitive_selector = cast(
            "list[dict[str, object]]", transitive_batch["ordered-selectors"]
        )[0]
        transitive_dependency_rows = json.dumps(
            {
                transitive_selector["work-group-id"]: [
                    {
                        "work-group-id": "wg-dependent-gate",
                        "source-batch-id": dependent_batch["batch-id"],
                        "outcome": "satisfied",
                        "admitted-for-gating": True,
                    }
                ]
            }
        )
        missing_base_dir = scratch / "transitive-missing-base"
        missing_base_dir.mkdir()
        with pytest.raises(RuntimeError, match="invalid dependency bundle"):
            _write_ci_batch_bundle(
                missing_base_dir,
                plan,
                manifest,
                rows_by_batch[transitive_batch["batch-id"]],
                [
                    _ci_success_validation_result(
                        plan,
                        cast("str", transitive_selector["work-group-id"]),
                    )
                ],
                dependency_results_json=transitive_dependency_rows,
                dependency_bundles=[dependent_path],
            )

        full_dir = scratch / "transitive-full"
        full_dir.mkdir()
        transitive_bundle = _write_ci_batch_bundle(
            full_dir,
            plan,
            manifest,
            rows_by_batch[transitive_batch["batch-id"]],
            [
                _ci_success_validation_result(
                    plan,
                    cast("str", transitive_selector["work-group-id"]),
                )
            ],
            dependency_results_json=transitive_dependency_rows,
            dependency_bundles=[base_path, dependent_path],
        )

        assert base_bundle["batch"]["batch-id"] == base_batch["batch-id"]
        assert (
            dependent_bundle["batch"]["batch-id"] == dependent_batch["batch-id"]
        )
        assert (
            transitive_bundle["batch"]["batch-id"]
            == transitive_batch["batch-id"]
        )
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_batch_writer_rejects_unselected_validation_result() -> None:
    """Validation results from outside the selected batch fail closed."""
    scratch = _ci_batch_bundle_scratch("batch-extra-validation-result")
    try:
        plan = cast("dict[str, object]", batch_contracts.plan())
        batch_contracts.add_dependent_work_group(plan)
        batches = batch_contracts.dependent_batches(plan)
        manifest = (
            batch_contracts.freeze_ci_validation_execution_batch_manifest(
                plan=plan,
                **batch_contracts.authorizing_context_kwargs(),
                batches=batches,
                budget=batch_contracts.budget(2),
                created_at=batch_contracts.CREATED_AT,
            )
        )
        row = next(
            item
            for item in _ci_batch_matrix_rows(plan, manifest)
            if cast("dict[str, object]", item["identity-matrix"])["batch-id"]
            == batches[0]["batch-id"]
        )
        off_batch_selector = cast(
            "list[dict[str, object]]",
            batches[1]["ordered-selectors"],
        )[0]
        off_batch_result = _ci_success_validation_result(
            plan,
            cast("str", off_batch_selector["work-group-id"]),
        )

        with pytest.raises(RuntimeError, match="unselected work groups"):
            _write_ci_batch_bundle(
                scratch,
                plan,
                manifest,
                row,
                [off_batch_result],
            )
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_batch_writer_rejects_unselected_dependency_result() -> None:
    """Dependency results keyed to an unselected batch selector fail closed."""
    scratch = _ci_batch_bundle_scratch("batch-extra-dependency-result")
    try:
        plan = cast("dict[str, object]", batch_contracts.plan())
        batch_contracts.add_dependent_work_group(plan)
        batches = batch_contracts.dependent_batches(plan)
        manifest = (
            batch_contracts.freeze_ci_validation_execution_batch_manifest(
                plan=plan,
                **batch_contracts.authorizing_context_kwargs(),
                batches=batches,
                budget=batch_contracts.budget(2),
                created_at=batch_contracts.CREATED_AT,
            )
        )
        row = next(
            item
            for item in _ci_batch_matrix_rows(plan, manifest)
            if cast("dict[str, object]", item["identity-matrix"])["batch-id"]
            == batches[0]["batch-id"]
        )
        selected_selector = cast(
            "list[dict[str, object]]",
            batches[0]["ordered-selectors"],
        )[0]
        off_batch_selector = cast(
            "list[dict[str, object]]",
            batches[1]["ordered-selectors"],
        )[0]
        selected_result = _ci_success_validation_result(
            plan,
            cast("str", selected_selector["work-group-id"]),
        )
        dependency_results_json = json.dumps(
            {off_batch_selector["work-group-id"]: []}
        )

        with pytest.raises(RuntimeError, match="unselected work groups"):
            _write_ci_batch_bundle(
                scratch,
                plan,
                manifest,
                row,
                [selected_result],
                dependency_results_json=dependency_results_json,
            )
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_batch_writer_rejects_duplicate_dependency_result_rows() -> None:
    """Duplicate upstream dependency rows fail before normalization."""
    scratch = _ci_batch_bundle_scratch("batch-duplicate-dependency-result")
    try:
        plan = cast("dict[str, object]", batch_contracts.plan())
        batch_contracts.add_dependent_work_group(plan)
        batches = batch_contracts.dependent_batches(plan)
        manifest = (
            batch_contracts.freeze_ci_validation_execution_batch_manifest(
                plan=plan,
                **batch_contracts.authorizing_context_kwargs(),
                batches=batches,
                budget=batch_contracts.budget(2),
                created_at=batch_contracts.CREATED_AT,
            )
        )
        row = next(
            item
            for item in _ci_batch_matrix_rows(plan, manifest)
            if cast("dict[str, object]", item["identity-matrix"])["batch-id"]
            == batches[1]["batch-id"]
        )
        dependent_selector = cast(
            "list[dict[str, object]]",
            batches[1]["ordered-selectors"],
        )[0]
        selected_result = _ci_success_validation_result(
            plan,
            cast("str", dependent_selector["work-group-id"]),
        )
        upstream_work_group_id = cast(
            "list[object]", dependent_selector["depends-on"]
        )[0]
        dependency_results_json = json.dumps(
            {
                dependent_selector["work-group-id"]: [
                    {
                        "work-group-id": upstream_work_group_id,
                        "source-batch-id": batches[0]["batch-id"],
                        "outcome": "satisfied",
                        "admitted-for-gating": True,
                    },
                    {
                        "work-group-id": upstream_work_group_id,
                        "source-batch-id": "stale-batch",
                        "outcome": "failed",
                        "admitted-for-gating": False,
                    },
                ]
            }
        )

        with pytest.raises(RuntimeError, match="duplicate dependency result"):
            _write_ci_batch_bundle(
                scratch,
                plan,
                manifest,
                row,
                [selected_result],
                dependency_results_json=dependency_results_json,
            )
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_batch_writer_derives_same_batch_failed_dependency_result() -> None:
    """Same-batch blocking failures are admitted dependency rows."""
    scratch = _ci_batch_bundle_scratch("batch-same-batch-dependency-blocked")
    try:
        plan, manifest, same_batch, dependent_selector = (
            _ci_same_batch_manifest_fixture()
        )

        result = control._ci_batch_selector_result(
            plan=plan,
            execution_batch_manifest=manifest,
            batch=same_batch,
            selector=dependent_selector,
            validation_result=None,
            dependency_results=[],
            authoritative_dependency_results={},
            prior_selector_outcomes={"wg-python-gate": "blocking-failure"},
            observed_commit_sha=batch_contracts.TREE_SHA,
            fact_snapshot=batch_contracts.authorizing_context_kwargs()[
                "fact_snapshot"
            ],
        )

        assert result["outcome"] == "blocking-failure"
        assert result["skip-reason"] is None
        dependency = cast(
            "list[dict[str, object]]",
            result["dependency-results"],
        )[0]
        assert dependency["work-group-id"] == "wg-python-gate"
        assert dependency["outcome"] == "failed"
        assert dependency["admitted-for-gating"] is True
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_batch_writer_rejects_supplied_same_batch_dependency_result() -> (
    None
):
    """Same-batch dependencies must be derived from prior selector outcomes."""
    scratch = _ci_batch_bundle_scratch("batch-same-batch-unexpected-result")
    try:
        plan, manifest, same_batch, dependent_selector = (
            _ci_same_batch_manifest_fixture()
        )
        upstream_work_group_id = cast(
            "list[object]", dependent_selector["depends-on"]
        )[0]

        with pytest.raises(RuntimeError, match="unexpected dependency results"):
            control._ci_batch_selector_result(
                plan=plan,
                execution_batch_manifest=manifest,
                batch=same_batch,
                selector=dependent_selector,
                validation_result=None,
                dependency_results=[
                    {
                        "work-group-id": upstream_work_group_id,
                        "source-batch-id": same_batch["batch-id"],
                        "outcome": "satisfied",
                        "admitted-for-gating": True,
                    }
                ],
                authoritative_dependency_results={},
                prior_selector_outcomes={"wg-python-gate": "success"},
                observed_commit_sha=batch_contracts.TREE_SHA,
                fact_snapshot=batch_contracts.authorizing_context_kwargs()[
                    "fact_snapshot"
                ],
            )
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_batch_writer_public_rejects_supplied_same_batch_dependency_result() -> (  # noqa: E501
    None
):
    """Public writer rejects supplied same-batch dependency rows."""
    scratch = _ci_batch_bundle_scratch("batch-public-same-batch-row")
    try:
        plan, manifest, same_batch, dependent_selector = (
            _ci_same_batch_manifest_fixture()
        )
        row = _ci_batch_matrix_rows(plan, manifest)[0]
        selectors = cast(
            "list[dict[str, object]]",
            same_batch["ordered-selectors"],
        )
        upstream_work_group_id = cast(
            "list[object]", dependent_selector["depends-on"]
        )[0]
        dependency_results_json = json.dumps(
            {
                dependent_selector["work-group-id"]: [
                    {
                        "work-group-id": upstream_work_group_id,
                        "source-batch-id": same_batch["batch-id"],
                        "outcome": "satisfied",
                        "admitted-for-gating": True,
                    }
                ]
            }
        )

        with pytest.raises(RuntimeError, match="unexpected dependency results"):
            _write_ci_batch_bundle(
                scratch,
                plan,
                manifest,
                row,
                [
                    _ci_success_validation_result(
                        plan,
                        cast("str", selectors[0]["work-group-id"]),
                    ),
                    _ci_success_validation_result(
                        plan,
                        cast("str", selectors[1]["work-group-id"]),
                    ),
                ],
                dependency_results_json=dependency_results_json,
            )
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_batch_writer_rejects_later_same_batch_dependency() -> None:
    """Same-batch providers must have run before dependent selectors."""
    scratch = _ci_batch_bundle_scratch("batch-same-batch-later-provider")
    try:
        plan, manifest, same_batch, dependent_selector = (
            _ci_same_batch_manifest_fixture()
        )
        provider_selector = deepcopy(
            cast("list[dict[str, object]]", same_batch["ordered-selectors"])[0]
        )
        dependent_selector = deepcopy(dependent_selector)
        dependent_selector["selector-index"] = 0
        provider_selector["selector-index"] = 1
        same_batch["ordered-selectors"] = [
            dependent_selector,
            provider_selector,
        ]

        with pytest.raises(
            RuntimeError,
            match="unavailable from prior selectors",
        ):
            control._ci_batch_selector_result(
                plan=plan,
                execution_batch_manifest=manifest,
                batch=same_batch,
                selector=dependent_selector,
                validation_result=None,
                dependency_results=[],
                authoritative_dependency_results={},
                prior_selector_outcomes={},
                observed_commit_sha=batch_contracts.TREE_SHA,
                fact_snapshot=batch_contracts.authorizing_context_kwargs()[
                    "fact_snapshot"
                ],
            )
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_batch_writer_public_rejects_later_same_batch_dependency() -> None:
    """Public writer rejects same-batch providers ordered after dependents."""
    scratch = _ci_batch_bundle_scratch("batch-public-later-provider")
    try:
        plan, manifest, same_batch, dependent_selector = (
            _ci_same_batch_manifest_fixture()
        )
        row = _ci_batch_matrix_rows(plan, manifest)[0]
        provider_selector = deepcopy(
            cast("list[dict[str, object]]", same_batch["ordered-selectors"])[0]
        )
        dependent_selector = deepcopy(dependent_selector)
        dependent_selector["selector-index"] = 0
        provider_selector["selector-index"] = 1
        same_batch["ordered-selectors"] = [
            dependent_selector,
            provider_selector,
        ]

        with pytest.raises(ContractValidationError):
            _write_ci_batch_bundle(
                scratch,
                plan,
                manifest,
                row,
                [
                    _ci_success_validation_result(
                        plan,
                        cast("str", dependent_selector["work-group-id"]),
                    ),
                    _ci_success_validation_result(
                        plan,
                        cast("str", provider_selector["work-group-id"]),
                    ),
                ],
            )
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_batch_writer_admits_same_batch_blocking_failure_dependency() -> (
    None
):
    """Same-batch blocking-failure dependencies admit later selectors."""
    scratch = _ci_batch_bundle_scratch("batch-same-batch-blocking-admitted")
    try:
        plan, manifest, same_batch, _dependent_selector = (
            _ci_same_batch_manifest_fixture()
        )
        identity = control._ci_execution_batch_matrix_identity(same_batch)
        row = {
            **identity,
            "identity-matrix": identity,
            "expected-job-identity": control._ci_batch_expected_writer_id(
                manifest,
                same_batch,
            ),
        }
        selectors = cast(
            "list[dict[str, object]]",
            same_batch["ordered-selectors"],
        )
        first_result = _ci_success_validation_result(
            plan,
            cast("str", selectors[0]["work-group-id"]),
        )
        first_result["outcome"] = "blocking-failure"
        cast("list[dict[str, object]]", first_result["commands"])[0][
            "outcome"
        ] = "blocking-failure"
        second_result = _ci_success_validation_result(
            plan,
            cast("str", selectors[1]["work-group-id"]),
        )

        bundle = _write_ci_batch_bundle(
            scratch,
            plan,
            manifest,
            row,
            [first_result, second_result],
        )

        selector_results = cast(
            "list[dict[str, object]]",
            bundle["selector-results"],
        )
        assert [item["work-group-id"] for item in selector_results] == [
            selectors[0]["work-group-id"],
            selectors[1]["work-group-id"],
        ]
        assert selector_results[0]["outcome"] == "blocking-failure"
        assert selector_results[1]["outcome"] == "success"
        assert selector_results[1]["skip-reason"] is None
        dependency = cast(
            "list[dict[str, object]]",
            selector_results[1]["dependency-results"],
        )[0]
        assert dependency == {
            "work-group-id": selectors[0]["work-group-id"],
            "source-batch-id": same_batch["batch-id"],
            "outcome": "failed",
            "admitted-for-gating": True,
        }
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_batch_validator_rejects_tampered_same_batch_dependency() -> None:
    """Same-batch dependency rows must match prior selector outcomes."""
    scratch = _ci_batch_bundle_scratch("batch-tampered-same-batch-dependency")
    try:
        plan, manifest, same_batch, _dependent_selector = (
            _ci_same_batch_manifest_fixture()
        )
        identity = control._ci_execution_batch_matrix_identity(same_batch)
        row = {
            **identity,
            "identity-matrix": identity,
            "expected-job-identity": control._ci_batch_expected_writer_id(
                manifest,
                same_batch,
            ),
        }
        selectors = cast(
            "list[dict[str, object]]",
            same_batch["ordered-selectors"],
        )
        first_result = _ci_success_validation_result(
            plan,
            cast("str", selectors[0]["work-group-id"]),
        )
        first_result["outcome"] = "blocking-failure"
        cast("list[dict[str, object]]", first_result["commands"])[0][
            "outcome"
        ] = "blocking-failure"
        second_result = _ci_success_validation_result(
            plan,
            cast("str", selectors[1]["work-group-id"]),
        )
        bundle = _write_ci_batch_bundle(
            scratch,
            plan,
            manifest,
            row,
            [first_result, second_result],
        )
        selector_results = cast(
            "list[dict[str, object]]",
            bundle["selector-results"],
        )
        dependency = cast(
            "list[dict[str, object]]",
            selector_results[1]["dependency-results"],
        )[0]
        dependency["outcome"] = "satisfied"
        dependency["admitted-for-gating"] = True

        with pytest.raises(ContractValidationError) as error:
            validate_ci_validation_batch_evidence_bundle(
                bundle,
                plan=plan,
                request=batch_contracts.authorizing_context_kwargs()["request"],
                execution_batch_manifest=manifest,
                changed_files_snapshot=(
                    batch_contracts.authorizing_context_kwargs()[
                        "changed_files_snapshot"
                    ]
                ),
                fact_snapshot=batch_contracts.authorizing_context_kwargs()[
                    "fact_snapshot"
                ],
                expected_run_id=batch_contracts.RUN_ID,
                expected_run_attempt=batch_contracts.RUN_ATTEMPT,
            )

        assert any(
            issue.path.endswith(".dependency-results[0].outcome")
            for issue in error.value.issues
        )
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_batch_writer_rejects_cross_batch_dependency_without_bundle() -> (
    None
):
    """Cross-batch dependencies need authoritative upstream bundle evidence."""
    scratch = _ci_batch_bundle_scratch("batch-cross-batch-without-bundle")
    try:
        plan = cast("dict[str, object]", batch_contracts.plan())
        batch_contracts.add_dependent_work_group(plan)
        batches = batch_contracts.dependent_batches(plan)
        manifest = (
            batch_contracts.freeze_ci_validation_execution_batch_manifest(
                plan=plan,
                **batch_contracts.authorizing_context_kwargs(),
                batches=batches,
                budget=batch_contracts.budget(2),
                created_at=batch_contracts.CREATED_AT,
            )
        )
        row = next(
            item
            for item in _ci_batch_matrix_rows(plan, manifest)
            if cast("dict[str, object]", item["identity-matrix"])["batch-id"]
            == batches[1]["batch-id"]
        )
        selector = cast(
            "list[dict[str, object]]",
            batches[1]["ordered-selectors"],
        )[0]
        upstream_work_group_id = cast("list[object]", selector["depends-on"])[0]
        dependency_results_json = json.dumps(
            {
                selector["work-group-id"]: [
                    {
                        "work-group-id": upstream_work_group_id,
                        "source-batch-id": batches[0]["batch-id"],
                        "outcome": "satisfied",
                        "admitted-for-gating": True,
                    }
                ]
            }
        )

        with pytest.raises(
            ContractValidationError,
            match="authoritative upstream bundle evidence",
        ):
            _write_ci_batch_bundle(
                scratch,
                plan,
                manifest,
                row,
                [
                    _ci_success_validation_result(
                        plan,
                        cast("str", selector["work-group-id"]),
                    )
                ],
                dependency_results_json=dependency_results_json,
            )
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_batch_writer_admits_cross_batch_dependency_from_valid_bundle() -> (
    None
):
    """Cross-batch admission is derived from validated upstream evidence."""
    scratch = _ci_batch_bundle_scratch("batch-cross-batch-with-bundle")
    try:
        plan = cast("dict[str, object]", batch_contracts.plan())
        batch_contracts.add_dependent_work_group(plan)
        batches = batch_contracts.dependent_batches(plan)
        manifest = (
            batch_contracts.freeze_ci_validation_execution_batch_manifest(
                plan=plan,
                **batch_contracts.authorizing_context_kwargs(),
                batches=batches,
                budget=batch_contracts.budget(2),
                created_at=batch_contracts.CREATED_AT,
            )
        )
        rows = _ci_batch_matrix_rows(plan, manifest)
        upstream_row = next(
            item
            for item in rows
            if cast("dict[str, object]", item["identity-matrix"])["batch-id"]
            == batches[0]["batch-id"]
        )
        dependent_row = next(
            item
            for item in rows
            if cast("dict[str, object]", item["identity-matrix"])["batch-id"]
            == batches[1]["batch-id"]
        )
        upstream_selector = cast(
            "list[dict[str, object]]",
            batches[0]["ordered-selectors"],
        )[0]
        dependent_selector = cast(
            "list[dict[str, object]]",
            batches[1]["ordered-selectors"],
        )[0]
        upstream_dir = scratch / "upstream"
        upstream_dir.mkdir()
        _write_ci_batch_bundle(
            upstream_dir,
            plan,
            manifest,
            upstream_row,
            [
                _ci_success_validation_result(
                    plan, cast("str", upstream_selector["work-group-id"])
                )
            ],
        )

        dependent_dir = scratch / "dependent"
        dependent_dir.mkdir()
        bundle = _write_ci_batch_bundle(
            dependent_dir,
            plan,
            manifest,
            dependent_row,
            [
                _ci_success_validation_result(
                    plan, cast("str", dependent_selector["work-group-id"])
                )
            ],
            dependency_bundles=[upstream_dir / "batch-evidence-bundle.json"],
        )

        selector_result = cast(
            "list[dict[str, object]]", bundle["selector-results"]
        )[0]
        assert selector_result["outcome"] == "success"
        assert selector_result["skip-reason"] is None
        dependency = cast(
            "list[dict[str, object]]",
            selector_result["dependency-results"],
        )[0]
        assert dependency["admitted-for-gating"] is True
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_batch_dependency_filter_ignores_stale_retry_bundle() -> None:
    """Previous-attempt bundles do not poison current dependency validation."""
    scratch = _ci_batch_bundle_scratch("batch-stale-dependency-filter")
    try:
        plan = cast("dict[str, object]", batch_contracts.plan())
        batch_contracts.add_dependent_work_group(plan)
        batches = batch_contracts.dependent_batches(plan)
        manifest = (
            batch_contracts.freeze_ci_validation_execution_batch_manifest(
                plan=plan,
                **batch_contracts.authorizing_context_kwargs(),
                batches=batches,
                budget=batch_contracts.budget(2),
                created_at=batch_contracts.CREATED_AT,
            )
        )
        rows = _ci_batch_matrix_rows(plan, manifest)
        upstream_row = next(
            item
            for item in rows
            if cast("dict[str, object]", item["identity-matrix"])["batch-id"]
            == batches[0]["batch-id"]
        )
        dependent_row = next(
            item
            for item in rows
            if cast("dict[str, object]", item["identity-matrix"])["batch-id"]
            == batches[1]["batch-id"]
        )
        upstream_selector = cast(
            "list[dict[str, object]]",
            batches[0]["ordered-selectors"],
        )[0]
        dependent_selector = cast(
            "list[dict[str, object]]",
            batches[1]["ordered-selectors"],
        )[0]
        upstream_dir = scratch / "upstream"
        upstream_dir.mkdir()
        current_bundle = _write_ci_batch_bundle(
            upstream_dir,
            plan,
            manifest,
            upstream_row,
            [
                _ci_success_validation_result(
                    plan,
                    cast("str", upstream_selector["work-group-id"]),
                )
            ],
        )
        stale_dir = scratch / "stale-upstream"
        stale_dir.mkdir()
        stale_bundle = deepcopy(current_bundle)
        cast("dict[str, object]", stale_bundle["run"])["run-attempt"] = "0"
        stale_path = stale_dir / "batch-evidence-bundle.json"
        stale_path.write_text(json.dumps(stale_bundle), encoding="utf-8")
        observed_artifacts_dir = scratch / "observed-artifacts"
        dependency_paths = _stage_dependency_bundles_by_physical_name(
            observed_artifacts_dir,
            [
                upstream_dir / "batch-evidence-bundle.json",
            ],
        )
        dependency_admissions = [
            _dependency_admission_for_staged_bundle(path)
            for path in dependency_paths
        ]
        dependency_paths.insert(0, stale_path)

        authoritative_bundles = control._ci_authoritative_dependency_bundles(
            [str(path) for path in dependency_paths],
            plan=plan,
            request=batch_contracts.request_document(),
            execution_batch_manifest=manifest,
            changed_files_snapshot=batch_contracts.changed_files_snapshot_document(),
            fact_snapshot=batch_contracts.fact_snapshot_document(),
            observed_artifacts_dir=str(observed_artifacts_dir),
            expected_run_id=batch_contracts.RUN_ID,
            expected_run_attempt=batch_contracts.RUN_ATTEMPT,
            dependency_artifact_admissions=dependency_admissions,
        )
        dependency_results = control._ci_batch_normalized_dependency_results(
            selector=dependent_selector,
            execution_batch_manifest=manifest,
            current_batch_id=cast("str", batches[1]["batch-id"]),
            dependency_results=[],
            authoritative_dependency_results=(
                control._ci_authoritative_dependency_results(
                    authoritative_bundles,
                )
            ),
            prior_selector_outcomes={},
        )
        dependent_dir = scratch / "dependent"
        dependent_dir.mkdir()
        dependent_bundle = _write_ci_batch_bundle(
            dependent_dir,
            plan,
            manifest,
            dependent_row,
            [
                _ci_success_validation_result(
                    plan,
                    cast("str", dependent_selector["work-group-id"]),
                )
            ],
            dependency_bundles=dependency_paths,
            observed_artifacts_dir=observed_artifacts_dir,
        )

        assert len(authoritative_bundles) == 1
        assert dependency_results[0]["admitted-for-gating"] is True
        selector_result = cast(
            "list[dict[str, object]]",
            dependent_bundle["selector-results"],
        )[0]
        assert selector_result["outcome"] == "success"
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_batch_dependency_filter_ignores_wrong_physical_path() -> None:
    """Wrong physical artifact directories are ignored."""
    scratch = _ci_batch_bundle_scratch("batch-dependency-wrong-path")
    try:
        plan = cast("dict[str, object]", batch_contracts.plan())
        batch_contracts.add_dependent_work_group(plan)
        batches = batch_contracts.dependent_batches(plan)
        manifest = (
            batch_contracts.freeze_ci_validation_execution_batch_manifest(
                plan=plan,
                **batch_contracts.authorizing_context_kwargs(),
                batches=batches,
                budget=batch_contracts.budget(2),
                created_at=batch_contracts.CREATED_AT,
            )
        )
        rows = _ci_batch_matrix_rows(plan, manifest)
        upstream_row = next(
            item
            for item in rows
            if cast("dict[str, object]", item["identity-matrix"])["batch-id"]
            == batches[0]["batch-id"]
        )
        dependent_selector = cast(
            "list[dict[str, object]]",
            batches[1]["ordered-selectors"],
        )[0]
        upstream_selector = cast(
            "list[dict[str, object]]",
            batches[0]["ordered-selectors"],
        )[0]
        upstream_dir = scratch / "upstream"
        upstream_dir.mkdir()
        upstream_bundle = _write_ci_batch_bundle(
            upstream_dir,
            plan,
            manifest,
            upstream_row,
            [
                _ci_success_validation_result(
                    plan,
                    cast("str", upstream_selector["work-group-id"]),
                )
            ],
        )
        observed_artifacts_dir = scratch / "observed-artifacts"
        spoof_dir = observed_artifacts_dir / "spoofed-artifact"
        spoof_dir.mkdir(parents=True)
        spoof_path = spoof_dir / "batch-evidence-bundle.json"
        spoof_path.write_text(json.dumps(upstream_bundle), encoding="utf-8")

        authoritative_bundles = control._ci_authoritative_dependency_bundles(
            [str(spoof_path)],
            plan=plan,
            request=batch_contracts.request_document(),
            execution_batch_manifest=manifest,
            changed_files_snapshot=batch_contracts.changed_files_snapshot_document(),
            fact_snapshot=batch_contracts.fact_snapshot_document(),
            observed_artifacts_dir=str(observed_artifacts_dir),
            expected_run_id=batch_contracts.RUN_ID,
            expected_run_attempt=batch_contracts.RUN_ATTEMPT,
        )
        dependency_results = control._ci_batch_normalized_dependency_results(
            selector=dependent_selector,
            execution_batch_manifest=manifest,
            current_batch_id=cast("str", batches[1]["batch-id"]),
            dependency_results=[],
            authoritative_dependency_results=(
                control._ci_authoritative_dependency_results(
                    authoritative_bundles,
                )
            ),
            prior_selector_outcomes={},
        )

        assert authoritative_bundles == []
        assert dependency_results[0]["outcome"] == "missing"
        assert dependency_results[0]["admitted-for-gating"] is False
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_batch_dependency_filter_ignores_malformed_spoof_path() -> None:
    """Malformed bundles at non-expected physical paths are out of scope."""
    scratch = _ci_batch_bundle_scratch("batch-dependency-malformed-wrong-path")
    try:
        plan = cast("dict[str, object]", batch_contracts.plan())
        batch_contracts.add_dependent_work_group(plan)
        batches = batch_contracts.dependent_batches(plan)
        manifest = (
            batch_contracts.freeze_ci_validation_execution_batch_manifest(
                plan=plan,
                **batch_contracts.authorizing_context_kwargs(),
                batches=batches,
                budget=batch_contracts.budget(2),
                created_at=batch_contracts.CREATED_AT,
            )
        )
        rows = _ci_batch_matrix_rows(plan, manifest)
        upstream_row = next(
            item
            for item in rows
            if cast("dict[str, object]", item["identity-matrix"])["batch-id"]
            == batches[0]["batch-id"]
        )
        dependent_selector = cast(
            "list[dict[str, object]]",
            batches[1]["ordered-selectors"],
        )[0]
        upstream_selector = cast(
            "list[dict[str, object]]",
            batches[0]["ordered-selectors"],
        )[0]
        upstream_dir = scratch / "upstream"
        upstream_dir.mkdir()
        upstream_bundle = _write_ci_batch_bundle(
            upstream_dir,
            plan,
            manifest,
            upstream_row,
            [
                _ci_success_validation_result(
                    plan,
                    cast("str", upstream_selector["work-group-id"]),
                )
            ],
        )
        observed_artifacts_dir = scratch / "observed-artifacts"
        valid_dependency_path = (
            observed_artifacts_dir
            / artifact_physical_name(
                cast("str", upstream_bundle["artifact-ref"]),
            )
            / "batch-evidence-bundle.json"
        )
        valid_admission = _stage_ci_batch_bundle_artifact(
            observed_artifacts_dir,
            upstream_bundle,
        )
        malformed_path = (
            observed_artifacts_dir
            / "spoofed-artifact"
            / "batch-evidence-bundle.json"
        )
        malformed_path.parent.mkdir()
        malformed_path.write_text("{", encoding="utf-8")

        authoritative_bundles = control._ci_authoritative_dependency_bundles(
            [str(malformed_path), str(valid_dependency_path)],
            plan=plan,
            request=batch_contracts.request_document(),
            execution_batch_manifest=manifest,
            changed_files_snapshot=batch_contracts.changed_files_snapshot_document(),
            fact_snapshot=batch_contracts.fact_snapshot_document(),
            observed_artifacts_dir=str(observed_artifacts_dir),
            expected_run_id=batch_contracts.RUN_ID,
            expected_run_attempt=batch_contracts.RUN_ATTEMPT,
            dependency_artifact_admissions=[valid_admission],
        )
        dependency_results = control._ci_batch_normalized_dependency_results(
            selector=dependent_selector,
            execution_batch_manifest=manifest,
            current_batch_id=cast("str", batches[1]["batch-id"]),
            dependency_results=[],
            authoritative_dependency_results=(
                control._ci_authoritative_dependency_results(
                    authoritative_bundles,
                )
            ),
            prior_selector_outcomes={},
        )

        assert len(authoritative_bundles) == 1
        assert dependency_results[0]["outcome"] == "satisfied"
        assert dependency_results[0]["admitted-for-gating"] is True
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_batch_dependency_filter_rejects_malformed_expected_path() -> None:
    """Malformed bundles at expected physical paths fail closed."""
    scratch = _ci_batch_bundle_scratch(
        "batch-dependency-malformed-expected-path",
    )
    try:
        plan = cast("dict[str, object]", batch_contracts.plan())
        batch_contracts.add_dependent_work_group(plan)
        batches = batch_contracts.dependent_batches(plan)
        manifest = (
            batch_contracts.freeze_ci_validation_execution_batch_manifest(
                plan=plan,
                **batch_contracts.authorizing_context_kwargs(),
                batches=batches,
                budget=batch_contracts.budget(2),
                created_at=batch_contracts.CREATED_AT,
            )
        )
        rows = _ci_batch_matrix_rows(plan, manifest)
        upstream_row = next(
            item
            for item in rows
            if cast("dict[str, object]", item["identity-matrix"])["batch-id"]
            == batches[0]["batch-id"]
        )
        upstream_selector = cast(
            "list[dict[str, object]]",
            batches[0]["ordered-selectors"],
        )[0]
        upstream_dir = scratch / "upstream"
        upstream_dir.mkdir()
        upstream_bundle = _write_ci_batch_bundle(
            upstream_dir,
            plan,
            manifest,
            upstream_row,
            [
                _ci_success_validation_result(
                    plan,
                    cast("str", upstream_selector["work-group-id"]),
                )
            ],
        )
        observed_artifacts_dir = scratch / "observed-artifacts"
        expected_path = (
            observed_artifacts_dir
            / artifact_physical_name(
                cast("str", upstream_bundle["artifact-ref"]),
            )
            / "batch-evidence-bundle.json"
        )
        expected_path.parent.mkdir(parents=True)
        expected_path.write_text("{", encoding="utf-8")

        with pytest.raises(RuntimeError, match="invalid dependency bundle"):
            control._ci_authoritative_dependency_bundles(
                [str(expected_path)],
                plan=plan,
                request=batch_contracts.request_document(),
                execution_batch_manifest=manifest,
                changed_files_snapshot=(
                    batch_contracts.changed_files_snapshot_document()
                ),
                fact_snapshot=batch_contracts.fact_snapshot_document(),
                observed_artifacts_dir=str(observed_artifacts_dir),
                expected_run_id=batch_contracts.RUN_ID,
                expected_run_attempt=batch_contracts.RUN_ATTEMPT,
            )
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_batch_dependency_filter_rejects_artifact_ref_mismatch() -> None:
    """Expected physical paths do not make mismatched bundle refs ignorable."""
    scratch = _ci_batch_bundle_scratch(
        "batch-dependency-artifact-ref-mismatch",
    )
    try:
        plan = cast("dict[str, object]", batch_contracts.plan())
        batch_contracts.add_dependent_work_group(plan)
        batches = batch_contracts.dependent_batches(plan)
        manifest = (
            batch_contracts.freeze_ci_validation_execution_batch_manifest(
                plan=plan,
                **batch_contracts.authorizing_context_kwargs(),
                batches=batches,
                budget=batch_contracts.budget(2),
                created_at=batch_contracts.CREATED_AT,
            )
        )
        rows = _ci_batch_matrix_rows(plan, manifest)
        upstream_row = next(
            item
            for item in rows
            if cast("dict[str, object]", item["identity-matrix"])["batch-id"]
            == batches[0]["batch-id"]
        )
        upstream_selector = cast(
            "list[dict[str, object]]",
            batches[0]["ordered-selectors"],
        )[0]
        upstream_dir = scratch / "upstream"
        upstream_dir.mkdir()
        upstream_bundle = _write_ci_batch_bundle(
            upstream_dir,
            plan,
            manifest,
            upstream_row,
            [
                _ci_success_validation_result(
                    plan,
                    cast("str", upstream_selector["work-group-id"]),
                )
            ],
        )
        expected_artifact_ref = cast("str", upstream_bundle["artifact-ref"])
        mismatched_bundle = deepcopy(upstream_bundle)
        mismatched_bundle["artifact-ref"] = cast(
            "str",
            batches[1]["expected-batch-evidence-bundle-ref"],
        )
        observed_artifacts_dir = scratch / "observed-artifacts"
        expected_path = (
            observed_artifacts_dir
            / artifact_physical_name(expected_artifact_ref)
            / "batch-evidence-bundle.json"
        )
        expected_path.parent.mkdir(parents=True)
        expected_path.write_text(
            json.dumps(mismatched_bundle),
            encoding="utf-8",
        )

        with pytest.raises(
            RuntimeError,
            match=(
                r"invalid dependency bundle.*artifact-ref does not match "
                "expected dependency artifact path"
            ),
        ):
            control._ci_authoritative_dependency_bundles(
                [str(expected_path)],
                plan=plan,
                request=batch_contracts.request_document(),
                execution_batch_manifest=manifest,
                changed_files_snapshot=(
                    batch_contracts.changed_files_snapshot_document()
                ),
                fact_snapshot=batch_contracts.fact_snapshot_document(),
                observed_artifacts_dir=str(observed_artifacts_dir),
                expected_run_id=batch_contracts.RUN_ID,
                expected_run_attempt=batch_contracts.RUN_ATTEMPT,
            )
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_run_ci_validation_batch_commands_admits_failed_dependency() -> None:
    """Batch commands run after valid upstream blocking-failure evidence."""
    scratch = _ci_batch_bundle_scratch("batch-command-failed-dependency")
    try:
        plan = cast("dict[str, object]", batch_contracts.plan())
        batch_contracts.add_dependent_work_group(plan)
        batches = batch_contracts.dependent_batches(plan)
        manifest = (
            batch_contracts.freeze_ci_validation_execution_batch_manifest(
                plan=plan,
                **batch_contracts.authorizing_context_kwargs(),
                batches=batches,
                budget=batch_contracts.budget(2),
                created_at=batch_contracts.CREATED_AT,
            )
        )
        rows = _ci_batch_matrix_rows(plan, manifest)
        upstream_row = next(
            item
            for item in rows
            if cast("dict[str, object]", item["identity-matrix"])["batch-id"]
            == batches[0]["batch-id"]
        )
        dependent_row = next(
            item
            for item in rows
            if cast("dict[str, object]", item["identity-matrix"])["batch-id"]
            == batches[1]["batch-id"]
        )
        upstream_selector = cast(
            "list[dict[str, object]]",
            batches[0]["ordered-selectors"],
        )[0]
        upstream_result = _ci_success_validation_result(
            plan,
            cast("str", upstream_selector["work-group-id"]),
        )
        upstream_result["outcome"] = "blocking-failure"
        for command in cast(
            "list[dict[str, object]]", upstream_result["commands"]
        ):
            command["outcome"] = "blocking-failure"
            command["exit-code"] = 1
        upstream_dir = scratch / "upstream"
        upstream_dir.mkdir()
        upstream_bundle = _write_ci_batch_bundle(
            upstream_dir,
            plan,
            manifest,
            upstream_row,
            [upstream_result],
        )
        observed_artifacts_dir = scratch / "observed-artifacts"
        dependency_path = (
            observed_artifacts_dir
            / artifact_physical_name(
                cast("str", upstream_bundle["artifact-ref"])
            )
            / "batch-evidence-bundle.json"
        )
        admission = _stage_ci_batch_bundle_artifact(
            observed_artifacts_dir,
            upstream_bundle,
        )
        plan_path = scratch / "validation-plan.json"
        request_path = scratch / "ci-validation-request.json"
        manifest_path = scratch / "execution-batch-manifest.json"
        changed_files_path = scratch / "changed-files.json"
        fact_snapshot_path = scratch / "fact-snapshot.json"
        result_dir = scratch / "results"
        for path, document in (
            (plan_path, plan),
            (request_path, batch_contracts.request_document()),
            (manifest_path, manifest),
            (
                changed_files_path,
                batch_contracts.changed_files_snapshot_document(),
            ),
            (fact_snapshot_path, batch_contracts.fact_snapshot_document()),
        ):
            path.write_text(json.dumps(document), encoding="utf-8")

        assert (
            control._cmd_run_ci_validation_batch_commands(
                argparse.Namespace(
                    plan=str(plan_path),
                    request=str(request_path),
                    execution_batch_manifest=str(manifest_path),
                    changed_files_snapshot=str(changed_files_path),
                    fact_snapshot=str(fact_snapshot_path),
                    observed_artifacts_dir=str(observed_artifacts_dir),
                    expected_run_id=batch_contracts.RUN_ID,
                    expected_run_attempt=batch_contracts.RUN_ATTEMPT,
                    dependency_bundle=[str(dependency_path)],
                    _dependency_artifact_admissions=[admission],
                    observed_commit_sha=batch_contracts.TREE_SHA,
                    matrix_row_json=json.dumps(dependent_row),
                    repo_root=str(REPO_ROOT),
                    result_out_dir=str(result_dir),
                    github_output=None,
                )
            )
            == 0
        )
        validation_result = json.loads(
            (result_dir / "validation-result-000.json").read_text(
                encoding="utf-8",
            )
        )
        assert validation_result["outcome"] == "blocking-failure"
        assert (
            validation_result["commands"][0].get("error")
            != "dependency-blocked"
        )
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_run_ci_validation_batch_commands_skips_blocked_dependencies() -> None:
    """Batch commands skip selectors with absent dependency evidence."""
    scratch = _ci_batch_bundle_scratch("batch-command-dependency-blocked")
    try:
        plan = cast("dict[str, object]", batch_contracts.plan())
        batch_contracts.add_dependent_work_group(plan)
        batches = batch_contracts.dependent_batches(plan)
        manifest = (
            batch_contracts.freeze_ci_validation_execution_batch_manifest(
                plan=plan,
                **batch_contracts.authorizing_context_kwargs(),
                batches=batches,
                budget=batch_contracts.budget(2),
                created_at=batch_contracts.CREATED_AT,
            )
        )
        rows = _ci_batch_matrix_rows(plan, manifest)
        dependent_row = next(
            item
            for item in rows
            if cast("dict[str, object]", item["identity-matrix"])["batch-id"]
            == batches[1]["batch-id"]
        )
        plan_path = scratch / "validation-plan.json"
        request_path = scratch / "ci-validation-request.json"
        manifest_path = scratch / "execution-batch-manifest.json"
        changed_files_path = scratch / "changed-files.json"
        fact_snapshot_path = scratch / "fact-snapshot.json"
        result_dir = scratch / "results"
        for path, document in (
            (plan_path, plan),
            (request_path, batch_contracts.request_document()),
            (manifest_path, manifest),
            (
                changed_files_path,
                batch_contracts.changed_files_snapshot_document(),
            ),
            (fact_snapshot_path, batch_contracts.fact_snapshot_document()),
        ):
            path.write_text(json.dumps(document), encoding="utf-8")

        command = [
            sys.executable,
            "eng/scripts/workflow_release_control.py",
            "run-ci-validation-batch-commands",
            "--plan",
            str(plan_path),
            "--request",
            str(request_path),
            "--execution-batch-manifest",
            str(manifest_path),
            "--changed-files-snapshot",
            str(changed_files_path),
            "--fact-snapshot",
            str(fact_snapshot_path),
            "--observed-artifacts-dir",
            "",
            "--expected-run-id",
            batch_contracts.RUN_ID,
            "--expected-run-attempt",
            batch_contracts.RUN_ATTEMPT,
            "--observed-commit-sha",
            batch_contracts.TREE_SHA,
            "--matrix-row-json",
            json.dumps(dependent_row),
            "--repo-root",
            str(REPO_ROOT),
            "--result-out-dir",
            str(result_dir),
        ]
        completed = subprocess.run(  # noqa: S603
            command,
            cwd=REPO_ROOT,
            text=True,
            capture_output=True,
            check=False,
        )

        validation_result = json.loads(
            (result_dir / "validation-result-000.json").read_text(
                encoding="utf-8",
            )
        )
        assert completed.returncode == 0, completed.stderr
        assert validation_result["outcome"] == "skipped"
        assert validation_result["commands"][0]["error"] == "dependency-blocked"
        rejected = subprocess.run(  # noqa: S603
            [*command, "--dependency-artifact-admission", "{}"],
            cwd=REPO_ROOT,
            text=True,
            capture_output=True,
            check=False,
        )
        assert rejected.returncode != 0
        assert "unrecognized arguments: --dependency-artifact-admission" in (
            rejected.stderr
        )
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_validation_dependency_blocked_diagnostic_uses_batch_terms() -> None:
    """Dependency-blocked diagnostics describe current gating evidence."""
    plan = cast("dict[str, object]", batch_contracts.plan())
    batch_contracts.add_dependent_work_group(plan)

    diagnostic = control._ci_validation_diagnostics(
        plan,
        "wg-dependent-gate",
        outcome="skipped",
    )[0]

    assert diagnostic["detail"] == "dependency-blocked"
    assert "dependency result admitted for gating" in diagnostic["message"]
    assert "same-batch results" in diagnostic["message"]
    assert "admitted upstream batch evidence" in diagnostic["message"]
    assert (
        "admitted dependency result or admitted batch evidence"
        not in diagnostic["message"]
    )
    assert "successful dependency result" not in diagnostic["message"]
    assert "successful receipt" not in diagnostic["message"]


def test_run_ci_validation_batch_commands_propagates_transitive_block() -> None:
    """C skips when downloaded B is skipped because upstream A is missing."""
    scratch = _ci_batch_bundle_scratch("batch-command-transitive-blocked")
    try:
        plan = cast("dict[str, object]", batch_contracts.plan())
        batch_contracts.add_transitive_work_group(plan)
        batches = batch_contracts.dependent_batches(plan)
        manifest = (
            batch_contracts.freeze_ci_validation_execution_batch_manifest(
                plan=plan,
                **batch_contracts.authorizing_context_kwargs(),
                batches=batches,
                budget=batch_contracts.budget(len(batches)),
                created_at=batch_contracts.CREATED_AT,
            )
        )
        rows = _ci_batch_matrix_rows(plan, manifest)
        row_by_batch_id = {
            cast(
                "str",
                cast("dict[str, object]", row["identity-matrix"])["batch-id"],
            ): row
            for row in rows
        }
        batch_by_group: dict[str, dict[str, object]] = {}
        for batch in batches:
            selector = cast(
                "list[dict[str, object]]", batch["ordered-selectors"]
            )[0]
            batch_by_group[cast("str", selector["work-group-id"])] = batch
        dependent_batch = batch_by_group["wg-dependent-gate"]
        transitive_batch = batch_by_group["wg-transitive-gate"]
        dependent_dir = scratch / "dependent"
        dependent_dir.mkdir()
        dependent_bundle = _write_ci_batch_bundle(
            dependent_dir,
            plan,
            manifest,
            row_by_batch_id[cast("str", dependent_batch["batch-id"])],
            [],
        )
        observed_artifacts_dir = scratch / "observed-artifacts"
        admission = _stage_ci_batch_bundle_artifact(
            observed_artifacts_dir,
            dependent_bundle,
        )
        dependency_path = (
            observed_artifacts_dir
            / artifact_physical_name(
                cast("str", dependent_bundle["artifact-ref"])
            )
            / "batch-evidence-bundle.json"
        )
        plan_path = scratch / "validation-plan.json"
        request_path = scratch / "ci-validation-request.json"
        manifest_path = scratch / "execution-batch-manifest.json"
        changed_files_path = scratch / "changed-files.json"
        fact_snapshot_path = scratch / "fact-snapshot.json"
        result_dir = scratch / "results"
        for path, document in (
            (plan_path, plan),
            (request_path, batch_contracts.request_document()),
            (manifest_path, manifest),
            (
                changed_files_path,
                batch_contracts.changed_files_snapshot_document(),
            ),
            (fact_snapshot_path, batch_contracts.fact_snapshot_document()),
        ):
            path.write_text(json.dumps(document), encoding="utf-8")

        assert (
            control._cmd_run_ci_validation_batch_commands(
                argparse.Namespace(
                    plan=str(plan_path),
                    request=str(request_path),
                    execution_batch_manifest=str(manifest_path),
                    changed_files_snapshot=str(changed_files_path),
                    fact_snapshot=str(fact_snapshot_path),
                    observed_artifacts_dir=str(observed_artifacts_dir),
                    expected_run_id=batch_contracts.RUN_ID,
                    expected_run_attempt=batch_contracts.RUN_ATTEMPT,
                    dependency_bundle=[str(dependency_path)],
                    _dependency_artifact_admissions=[admission],
                    observed_commit_sha=batch_contracts.TREE_SHA,
                    matrix_row_json=json.dumps(
                        row_by_batch_id[
                            cast("str", transitive_batch["batch-id"])
                        ]
                    ),
                    repo_root=str(REPO_ROOT),
                    result_out_dir=str(result_dir),
                    github_output=None,
                )
            )
            == 0
        )
        validation_result = json.loads(
            (result_dir / "validation-result-000.json").read_text(
                encoding="utf-8",
            )
        )
        assert validation_result["outcome"] == "skipped"
        assert validation_result["commands"][0]["error"] == "dependency-blocked"
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


@pytest.mark.parametrize(
    "bundle_case",
    ["malformed", "wrong-run-id"],
)
def test_ci_batch_writer_rejects_invalid_dependency_bundle(
    bundle_case: str,
) -> None:
    """Invalid upstream dependency bundles fail closed."""
    scratch = _ci_batch_bundle_scratch(
        f"batch-invalid-dependency-{bundle_case}"
    )
    try:
        plan = cast("dict[str, object]", batch_contracts.plan())
        batch_contracts.add_dependent_work_group(plan)
        batches = batch_contracts.dependent_batches(plan)
        manifest = (
            batch_contracts.freeze_ci_validation_execution_batch_manifest(
                plan=plan,
                **batch_contracts.authorizing_context_kwargs(),
                batches=batches,
                budget=batch_contracts.budget(2),
                created_at=batch_contracts.CREATED_AT,
            )
        )
        rows = _ci_batch_matrix_rows(plan, manifest)
        upstream_row = next(
            item
            for item in rows
            if cast("dict[str, object]", item["identity-matrix"])["batch-id"]
            == batches[0]["batch-id"]
        )
        dependent_row = next(
            item
            for item in rows
            if cast("dict[str, object]", item["identity-matrix"])["batch-id"]
            == batches[1]["batch-id"]
        )
        upstream_selector = cast(
            "list[dict[str, object]]",
            batches[0]["ordered-selectors"],
        )[0]
        dependent_selector = cast(
            "list[dict[str, object]]",
            batches[1]["ordered-selectors"],
        )[0]
        upstream_dir = scratch / "upstream"
        upstream_dir.mkdir()
        upstream_bundle = _write_ci_batch_bundle(
            upstream_dir,
            plan,
            manifest,
            upstream_row,
            [
                _ci_success_validation_result(
                    plan, cast("str", upstream_selector["work-group-id"])
                )
            ],
        )
        upstream_path = upstream_dir / "batch-evidence-bundle.json"
        observed_artifacts_dir = None
        dependency_bundles = [upstream_path]
        if bundle_case == "malformed":
            observed_artifacts_dir = scratch / "observed-artifacts"
            expected_path = (
                observed_artifacts_dir
                / artifact_physical_name(
                    cast("str", upstream_bundle["artifact-ref"]),
                )
                / "batch-evidence-bundle.json"
            )
            expected_path.parent.mkdir(parents=True)
            expected_path.write_text("{", encoding="utf-8")
            dependency_bundles = [expected_path]
        else:
            run = cast("dict[str, object]", upstream_bundle["run"])
            if bundle_case == "wrong-run-id":
                run["run-id"] = "stale-run"
            else:
                run["run-attempt"] = "stale-attempt"
            upstream_path.write_text(
                json.dumps(upstream_bundle),
                encoding="utf-8",
            )

        dependent_dir = scratch / "dependent"
        dependent_dir.mkdir()
        with pytest.raises(RuntimeError, match="invalid dependency bundle"):
            _write_ci_batch_bundle(
                dependent_dir,
                plan,
                manifest,
                dependent_row,
                [
                    _ci_success_validation_result(
                        plan,
                        cast("str", dependent_selector["work-group-id"]),
                    )
                ],
                dependency_bundles=dependency_bundles,
                observed_artifacts_dir=observed_artifacts_dir,
            )
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_batch_writer_ignores_spoofed_dependency_result_json() -> None:
    """Validated upstream bundles override contradictory caller JSON rows."""
    scratch = _ci_batch_bundle_scratch("batch-spoofed-dependency-json")
    try:
        plan = cast("dict[str, object]", batch_contracts.plan())
        batch_contracts.add_dependent_work_group(plan)
        batches = batch_contracts.dependent_batches(plan)
        manifest = (
            batch_contracts.freeze_ci_validation_execution_batch_manifest(
                plan=plan,
                **batch_contracts.authorizing_context_kwargs(),
                batches=batches,
                budget=batch_contracts.budget(2),
                created_at=batch_contracts.CREATED_AT,
            )
        )
        rows = _ci_batch_matrix_rows(plan, manifest)
        upstream_row = next(
            item
            for item in rows
            if cast("dict[str, object]", item["identity-matrix"])["batch-id"]
            == batches[0]["batch-id"]
        )
        dependent_row = next(
            item
            for item in rows
            if cast("dict[str, object]", item["identity-matrix"])["batch-id"]
            == batches[1]["batch-id"]
        )
        upstream_selector = cast(
            "list[dict[str, object]]",
            batches[0]["ordered-selectors"],
        )[0]
        dependent_selector = cast(
            "list[dict[str, object]]",
            batches[1]["ordered-selectors"],
        )[0]
        upstream_dir = scratch / "upstream"
        upstream_dir.mkdir()
        _write_ci_batch_bundle(
            upstream_dir,
            plan,
            manifest,
            upstream_row,
            [
                _ci_success_validation_result(
                    plan, cast("str", upstream_selector["work-group-id"])
                )
            ],
        )
        dependency_results_json = json.dumps(
            {
                dependent_selector["work-group-id"]: [
                    {
                        "work-group-id": upstream_selector["work-group-id"],
                        "source-batch-id": batches[0]["batch-id"],
                        "outcome": "failed",
                        "admitted-for-gating": False,
                    }
                ]
            }
        )

        dependent_dir = scratch / "dependent"
        dependent_dir.mkdir()
        bundle = _write_ci_batch_bundle(
            dependent_dir,
            plan,
            manifest,
            dependent_row,
            [
                _ci_success_validation_result(
                    plan, cast("str", dependent_selector["work-group-id"])
                )
            ],
            dependency_results_json=dependency_results_json,
            dependency_bundles=[upstream_dir / "batch-evidence-bundle.json"],
        )

        selector_result = cast(
            "list[dict[str, object]]", bundle["selector-results"]
        )[0]
        dependency = cast(
            "list[dict[str, object]]",
            selector_result["dependency-results"],
        )[0]
        assert dependency["outcome"] == "satisfied"
        assert dependency["admitted-for-gating"] is True
        assert selector_result["outcome"] == "success"
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_batch_writer_rejects_stale_non_release_result_commit() -> None:
    """Non-release validation-result success is bound to the observed commit."""
    scratch = _ci_batch_bundle_scratch("batch-stale-non-release-commit")
    try:
        plan, manifest = _ci_batch_contract_plan_and_manifest()
        row = _ci_batch_matrix_rows(plan, manifest)[0]
        batch = cast("list[dict[str, object]]", manifest["batches"])[0]
        selector = cast(
            "list[dict[str, object]]",
            batch["ordered-selectors"],
        )[0]
        result = _ci_success_validation_result(
            plan,
            cast("str", selector["work-group-id"]),
        )
        result["observed-commit-sha"] = "c" * 40

        bundle = _write_ci_batch_bundle(scratch, plan, manifest, row, [result])

        selector_result = cast(
            "list[dict[str, object]]",
            bundle["selector-results"],
        )[0]
        assert selector_result["outcome"] == "blocking-failure"
        assert selector_result["skip-reason"] is None
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_batch_writer_rejects_stale_non_release_result_coverage_target() -> (
    None
):
    """Non-release validation-result success is bound to coverage target."""
    scratch = _ci_batch_bundle_scratch("batch-stale-non-release-coverage")
    try:
        plan, manifest = _ci_batch_contract_plan_and_manifest()
        row = _ci_batch_matrix_rows(plan, manifest)[0]
        batch = cast("list[dict[str, object]]", manifest["batches"])[0]
        selector = cast(
            "list[dict[str, object]]",
            batch["ordered-selectors"],
        )[0]
        result = _ci_success_validation_result(
            plan,
            cast("str", selector["work-group-id"]),
        )
        result["coverage-target"] = {"type": "subject", "id": "stale"}

        bundle = _write_ci_batch_bundle(scratch, plan, manifest, row, [result])

        selector_result = cast(
            "list[dict[str, object]]",
            bundle["selector-results"],
        )[0]
        assert selector_result["outcome"] == "blocking-failure"
        assert selector_result["skip-reason"] is None
        assert selector_result["evidence"]["capability-results"]
        assert selector_result["diagnostics"][0]["code"] == (
            "validation-work-failed"
        )
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_validation_batch_evidence_writer_fails_on_writer_mismatch() -> None:
    """Observed workflow/job/matrix writer identity is fail-closed."""
    scratch = _ci_batch_bundle_scratch("batch-writer-mismatch")
    try:
        plan, manifest = _ci_batch_contract_plan_and_manifest()
        row = _ci_batch_matrix_rows(plan, manifest)[0]
        batch = cast("list[dict[str, object]]", manifest["batches"])[0]
        selector = cast(
            "list[dict[str, object]]",
            batch["ordered-selectors"],
        )[0]
        result = _ci_success_validation_result(
            plan,
            cast("str", selector["work-group-id"]),
        )

        with pytest.raises(RuntimeError, match="writer identity"):
            _write_ci_batch_bundle(
                scratch,
                plan,
                manifest,
                row,
                [result],
                job="wrong-execution-batch",
            )
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_batch_writer_rejects_orchestrator_context_without_slot_index() -> (
    None
):
    """Producer must not omit the physical orchestrator slot identity."""
    plan, manifest = _ci_batch_contract_plan_and_manifest()
    row = _ci_batch_matrix_rows(plan, manifest)[0]
    batch = cast("list[dict[str, object]]", manifest["batches"])[0]
    orchestrator_job = f"execution-batch-{batch['runner-family']}-orchestrator"

    with pytest.raises(RuntimeError, match="orchestrator_slot_index"):
        control._ci_batch_bundle_writer(
            execution_batch_manifest=manifest,
            batch=batch,
            matrix_row=row,
            workflow="CI Validation",
            job=orchestrator_job,
        )


def test_ci_batch_writer_direct_context_does_not_require_slot_index() -> None:
    """Direct execution job writer identity remains valid without a slot."""
    plan, manifest = _ci_batch_contract_plan_and_manifest()
    row = _ci_batch_matrix_rows(plan, manifest)[0]
    batch = cast("list[dict[str, object]]", manifest["batches"])[0]

    writer = control._ci_batch_bundle_writer(
        execution_batch_manifest=manifest,
        batch=batch,
        matrix_row=row,
        workflow="CI Validation",
        job="execution-batch",
    )

    assert writer["identity-source"] == "github-actions-job-context"
    assert "observed-orchestrator-slot-index" not in writer


def test_ci_batch_writer_direct_context_ignores_slot_index() -> None:
    """Direct execution job writer identity never emits an orchestrator slot."""
    plan, manifest = _ci_batch_contract_plan_and_manifest()
    row = _ci_batch_matrix_rows(plan, manifest)[0]
    batch = cast("list[dict[str, object]]", manifest["batches"])[0]

    writer = control._ci_batch_bundle_writer(
        execution_batch_manifest=manifest,
        batch=batch,
        matrix_row=row,
        workflow="CI Validation",
        job="execution-batch",
        orchestrator_slot_index="7",
    )

    assert writer["identity-source"] == "github-actions-job-context"
    assert "observed-orchestrator-slot-index" not in writer


def test_ci_batch_writer_rejects_wrong_matrix_batch() -> None:
    """Matrix row batch identity must match the execution-batch manifest."""
    scratch = _ci_batch_bundle_scratch("batch-matrix-mismatch")
    try:
        plan, manifest = _ci_batch_contract_plan_and_manifest()
        row = deepcopy(_ci_batch_matrix_rows(plan, manifest)[0])
        identity = cast("dict[str, object]", row["identity-matrix"])
        identity["batch-id"] = "batch-does-not-exist"
        row["batch-id"] = "batch-does-not-exist"
        batch = cast("list[dict[str, object]]", manifest["batches"])[0]
        selector = cast(
            "list[dict[str, object]]",
            batch["ordered-selectors"],
        )[0]
        result = _ci_success_validation_result(
            plan,
            cast("str", selector["work-group-id"]),
        )

        with pytest.raises(RuntimeError, match="batch id"):
            _write_ci_batch_bundle(scratch, plan, manifest, row, [result])
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_batch_writer_rejects_identity_only_matrix_mismatch() -> None:
    """Identity matrix values must independently match the manifest."""
    scratch = _ci_batch_bundle_scratch("batch-identity-only-mismatch")
    try:
        plan, manifest = _ci_batch_contract_plan_and_manifest()
        row = deepcopy(_ci_batch_matrix_rows(plan, manifest)[0])
        identity = cast("dict[str, object]", row["identity-matrix"])
        identity["runner-family"] = (
            "windows" if identity["runner-family"] != "windows" else "ubuntu"
        )
        batch = cast("list[dict[str, object]]", manifest["batches"])[0]
        selector = cast(
            "list[dict[str, object]]",
            batch["ordered-selectors"],
        )[0]
        result = _ci_success_validation_result(
            plan,
            cast("str", selector["work-group-id"]),
        )

        with pytest.raises(RuntimeError, match="identity"):
            _write_ci_batch_bundle(scratch, plan, manifest, row, [result])
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_batch_writer_rejects_projection_only_matrix_mismatch() -> None:
    """Top-level matrix projection must match the identity matrix."""
    scratch = _ci_batch_bundle_scratch("batch-projection-only-mismatch")
    try:
        plan, manifest = _ci_batch_contract_plan_and_manifest()
        row = deepcopy(_ci_batch_matrix_rows(plan, manifest)[0])
        row["runner-family"] = (
            "windows" if row["runner-family"] != "windows" else "ubuntu"
        )
        batch = cast("list[dict[str, object]]", manifest["batches"])[0]
        selector = cast(
            "list[dict[str, object]]",
            batch["ordered-selectors"],
        )[0]
        result = _ci_success_validation_result(
            plan,
            cast("str", selector["work-group-id"]),
        )

        with pytest.raises(RuntimeError, match="projection"):
            _write_ci_batch_bundle(scratch, plan, manifest, row, [result])
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_batch_writer_rejects_same_batch_runner_family_mismatch() -> None:
    """Same-batch selectors cannot skew away from the batch runner family."""
    scratch = _ci_batch_bundle_scratch("batch-same-batch-runner-mismatch")
    try:
        plan = cast("dict[str, object]", batch_contracts.plan())
        batch_contracts.add_dependent_work_group(plan)
        batches = batch_contracts.dependent_batches(plan)
        same_batch = deepcopy(batches[0])
        dependent_selector = deepcopy(
            cast("list[dict[str, object]]", batches[1]["ordered-selectors"])[0]
        )
        dependent_selector["selector-index"] = 1
        slot = cast(
            "dict[str, object]",
            dependent_selector["expected-evidence-slot"],
        )
        slot["runner-family"] = (
            "windows" if same_batch["runner-family"] != "windows" else "ubuntu"
        )
        cast("list[dict[str, object]]", same_batch["ordered-selectors"]).append(
            dependent_selector
        )
        same_batch["depends-on-batches"] = []

        with pytest.raises(ContractValidationError):
            batch_contracts.freeze_ci_validation_execution_batch_manifest(
                plan=plan,
                **batch_contracts.authorizing_context_kwargs(),
                batches=[same_batch],
                budget=batch_contracts.budget(1),
                created_at=batch_contracts.CREATED_AT,
            )
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_batch_writer_rejects_same_batch_expected_bundle_ref_mismatch() -> (
    None
):
    """Matrix identity cannot project a stale expected bundle artifact ref."""
    scratch = _ci_batch_bundle_scratch("batch-same-batch-ref-mismatch")
    try:
        plan, manifest = _ci_batch_contract_plan_and_manifest()
        row = deepcopy(_ci_batch_matrix_rows(plan, manifest)[0])
        identity = cast("dict[str, object]", row["identity-matrix"])
        identity["expected-batch-evidence-bundle-ref"] = (
            "ci-validation/batches/25887422010/1/stale/batch-evidence-bundle.json"
        )
        batch = cast("list[dict[str, object]]", manifest["batches"])[0]
        selector = cast(
            "list[dict[str, object]]",
            batch["ordered-selectors"],
        )[0]
        result = _ci_success_validation_result(
            plan,
            cast("str", selector["work-group-id"]),
        )

        with pytest.raises(RuntimeError, match="identity"):
            _write_ci_batch_bundle(scratch, plan, manifest, row, [result])
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


def test_ci_batch_writer_empty_manifest_has_no_bundle() -> None:
    """Zero-work handoff has no matrix leg to write a batch bundle."""
    scratch = _ci_batch_bundle_scratch("empty-batch-manifest")
    plan, manifest = _ci_batch_contract_plan_and_manifest()
    empty_manifest = batch_contracts.zero_batch_execution_manifest(manifest)
    authorizing_context = batch_contracts.authorizing_context_kwargs()
    plan_path = scratch / "plan.json"
    request_path = scratch / "request.json"
    manifest_path = scratch / "execution-batch-manifest.json"
    changed_files_path = scratch / "changed-files.json"
    fact_snapshot_path = scratch / "fact-snapshot.json"
    bundle_path = scratch / "batch-evidence-bundle.json"
    plan_path.write_text(json.dumps(plan), encoding="utf-8")
    request_path.write_text(
        json.dumps(authorizing_context["request"]),
        encoding="utf-8",
    )
    manifest_path.write_text(json.dumps(empty_manifest), encoding="utf-8")
    changed_files_path.write_text(
        json.dumps(authorizing_context["changed_files_snapshot"]),
        encoding="utf-8",
    )
    fact_snapshot_path.write_text(
        json.dumps(authorizing_context["fact_snapshot"]),
        encoding="utf-8",
    )

    try:
        with pytest.raises(ContractValidationError):
            control._cmd_write_ci_validation_batch_evidence_bundle(
                argparse.Namespace(
                    plan=str(plan_path),
                    request=str(request_path),
                    execution_batch_manifest=str(manifest_path),
                    changed_files_snapshot=str(changed_files_path),
                    fact_snapshot=str(fact_snapshot_path),
                    matrix_row_json=json.dumps(
                        {
                            "batch-id": "batch-missing",
                            "runner-family": "ubuntu",
                            "expected-batch-evidence-bundle-ref": (
                                "ci-validation/batches/123/1/"
                                "batch-missing/batch-evidence-bundle.json"
                            ),
                            "identity-matrix": {
                                "batch-id": "batch-missing",
                                "runner-family": "ubuntu",
                                "expected-batch-evidence-bundle-ref": (
                                    "ci-validation/batches/123/1/"
                                    "batch-missing/batch-evidence-bundle.json"
                                ),
                            },
                            "expected-job-identity": (
                                "github-actions-job:" + "0" * 64
                            ),
                        }
                    ),
                    expected_run_id=batch_contracts.RUN_ID,
                    expected_run_attempt=batch_contracts.RUN_ATTEMPT,
                    workflow="CI Validation",
                    job="execution-batch",
                    observed_commit_sha=batch_contracts.TREE_SHA,
                    validation_result=[],
                    dependency_results_json="",
                    dependency_bundle=[],
                    started_at=batch_contracts.CREATED_AT,
                    completed_at=batch_contracts.CREATED_AT,
                    created_at=batch_contracts.CREATED_AT,
                    bundle_out=str(bundle_path),
                    github_output="",
                )
            )
        assert empty_manifest["batches"] == []
        assert not bundle_path.exists()
    finally:
        shutil.rmtree(scratch, ignore_errors=True)


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


def test_ci_validate_workflow_passes_actionlint_gate() -> None:
    """The focused workflow gate runs actionlint against ci-validate.yml."""
    if shutil.which("actionlint") is None:
        pytest.skip("actionlint is not installed")

    result = subprocess.run(  # noqa: S603
        [
            sys.executable,
            "eng/scripts/hk_actionlint.py",
            ".github/workflows/ci-validate.yml",
        ],
        cwd=REPO_ROOT,
        check=False,
        text=True,
        capture_output=True,
        timeout=120,
    )

    assert result.returncode == 0, result.stdout + result.stderr
