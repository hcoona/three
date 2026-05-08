# ruff: noqa: SLF001
"""Tests for workflow-release control-plane helper script."""

from __future__ import annotations

import importlib.util
import json
import shutil
import subprocess
import sys
from copy import deepcopy
from itertools import pairwise
from pathlib import Path
from typing import TYPE_CHECKING

import pytest
import yaml
from three_workflow_release_authoring import validate_authoring
from three_workflow_release_contracts import (
    ArtifactNameInputs,
    artifact_name,
    validate_contract,
)
from three_workflow_release_planner import PlannerInputs, plan_release
from three_workflow_release_proof import (
    ProofError,
    classify_immutable_observations,
)

if TYPE_CHECKING:
    from collections.abc import Callable

REPO_ROOT = Path(__file__).parents[1]
SCRIPT = REPO_ROOT / "eng/scripts/workflow_release_control.py"
FIXTURES = (
    REPO_ROOT
    / "src/public/lib/three-workflow-release-contracts/tests/fixtures/valid"
)
ACCEPTANCE_MATRIX = (
    REPO_ROOT / "tests/fixtures/workflow-release-acceptance-matrix.json"
)
LOW_LEVEL_DESIGN = (
    REPO_ROOT / "docs/wiki/analyses/workflow-release-low-level-design.md"
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


def _release_workflow_paths() -> list[Path]:
    """Return release workflow paths in deterministic order."""
    return sorted((REPO_ROOT / ".github/workflows").glob("release-*.yml"))


def _acceptance_matrix() -> dict[str, object]:
    """Load the workflow-release acceptance matrix fixture."""
    return json.loads(ACCEPTANCE_MATRIX.read_text(encoding="utf-8"))


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
    snapshot["destination"] = {"host": "rubygems.org"}
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


def test_acceptance_matrix_test_nodeids_are_collected_by_gate() -> None:
    """HK acceptance gate must execute every matrix test evidence nodeid."""
    matrix = _acceptance_matrix()
    gate_nodeids = set(acceptance_gate._collect_test_nodeids(matrix))
    mandatory_nodeids = {
        "tests/test_workflow_release_control.py::"
        "test_acceptance_gate_rejects_option_like_nodeids_and_uses_separator",
        "tests/test_workflow_release_control.py::"
        "test_hk_runs_focused_workflow_release_validation",
        "tests/test_workflow_release_control.py::"
        "test_official_entry_publish_sets_up_npm_trusted_runtime",
    }

    assert _matrix_test_nodeids(matrix)
    for nodeid in _matrix_test_nodeids(matrix):
        assert nodeid in _all_test_nodeids()
        assert nodeid in gate_nodeids
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

        plan, execution_sets = _unsupported_rubygems_oidc_plan_and_sets()
        _run_plan_gate_case(
            scratch / "unsupported-observation",
            plan,
            execution_sets,
            "rubygems/rubygems-org#example#Example",
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
            "hcoona-release-smoke-github-packages",
            "hcoona-release-smoke-github-release",
            "hcoona-release-smoke-npm",
            "hcoona-release-smoke-nuget",
            "hcoona-release-smoke-pypi",
            "hcoona-release-smoke-rubygems",
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
    out = SCRATCH / "remote-observations.json"
    diagnostics = SCRATCH / "planner-diagnostics.json"
    shutil.rmtree(SCRATCH, ignore_errors=True)
    SCRATCH.mkdir()
    monkeypatch.setattr(control, "_remote_tag_commit", lambda *_: None)
    monkeypatch.setattr(control, "_github_release_by_tag", lambda *_: None)
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

    assert "workflow-release-control-tests" in hk_config
    assert ".github/workflows/release-*.yml" in hk_config
    assert "eng/release/**" in hk_config
    assert "eng/scripts/workflow_release_control.py" in hk_config
    assert "eng/scripts/workflow_release_acceptance_gate.py" in hk_config
    assert "src/**/three.release.yml" in hk_config
    assert "src/public/lib/three-workflow-release-*/**" in hk_config
    assert "tests/test_workflow_release_control.py" in hk_config
    assert "tests/fixtures/workflow-release-acceptance-matrix.json" in hk_config
    assert (
        "uv run python eng/scripts/workflow_release_acceptance_gate.py"
        in hk_config
    )
    assert "actionlint" in hk_config
