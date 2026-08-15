"""Red-first contracts for logical commit 11 legacy Buddy retirement."""

from __future__ import annotations

import ast
import fnmatch
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
ACTIVE_V1_DOCS = (
    "docs/wiki/analyses/workflow-release-low-level-design.md",
    "docs/wiki/analyses/workflow-release-operator-rollout.md",
    "docs/wiki/analyses/workflow-release-oidc-publish-topology.md",
    ".github/workflows/docs/MEMORY.md",
    "src/public/lib/hexo-renderer-asciidoc/README.md",
    "docs/wiki/analyses/workflow-release-workflow-executor-boundaries.md",
)
PRESERVED_OFFICIAL_CI_WORKFLOWS = frozenset(
    {
        ".github/workflows/ci.yml",
        ".github/workflows/official.yml",
        ".github/workflows/release-official.yml",
        ".github/workflows/release-orchestrate.yml",
        ".github/workflows/release-resolve.yml",
        ".github/workflows/release-create-github-release.yml",
        ".github/workflows/release-prepare-release-notes.yml",
    }
)
PRESERVED_V2_FILES = frozenset(
    {
        ".github/workflows/docs/DESIGN.v2.md",
        "docs/wiki/analyses/workflow-delivery/v2/README.md",
    }
)
PRESERVED_V3_BUDDY_WORKFLOW_JOBS = {
    ".github/workflows/workflow-delivery-v3-buddy-smoke.yml": frozenset(
        {
            "request",
            "discover-node",
            "compile-model",
            "evaluate-live-eligibility",
            "run-live-attempt",
        }
    ),
    ".github/workflows/workflow-delivery-v3-buddy-smoke-acceptance.yml": (
        frozenset(
            {
                "validate-fixed-inputs",
                "acceptance-review",
                "probe-absent-create-readback",
                "probe-exact-and-conflict",
                "capture-governance-evidence",
            }
        )
    ),
}
PRESERVED_DESCRIPTORS = frozenset(
    {
        "src/private/app/qidian-novel-downloader/three.release.yml",
        "src/private/app/vscode-copilot-telegram-hook/three.release.yml",
        "src/public/app/ImageOcclusionEditor/three.release.yml",
        "src/public/app/PhiFailureDetector.Console/three.release.yml",
        "src/public/app/markdown-hybrid-search-mcp/three.release.yml",
        "src/public/lib/CircularList/three.release.yml",
        "src/public/lib/Hjg.Pngcs/three.release.yml",
        "src/public/lib/Memoization.Generators/three.release.yml",
        "src/public/lib/Memoization/three.release.yml",
        "src/public/lib/MicrosoftExtensions.Logging.MSTest/three.release.yml",
        "src/public/lib/MicrosoftExtensions.Logging.Xunit/three.release.yml",
        "src/public/lib/MicrosoftExtensions.Options.DedupChangeExtensions/three.release.yml",
        "src/public/lib/PhiFailureDetector/three.release.yml",
        "src/public/lib/WebHdfs.Extensions.FileProviders/three.release.yml",
        "src/public/lib/asciidoctor-latexmath/three.release.yml",
        "src/public/lib/hcoona-release-smoke-dotnet-executable/three.release.yml",
        "src/public/lib/hcoona-release-smoke-github-packages/three.release.yml",
        "src/public/lib/hcoona-release-smoke-github-release/three.release.yml",
        "src/public/lib/hcoona-release-smoke-inno/three.release.yml",
        "src/public/lib/hcoona-release-smoke-npm-dual/three.release.yml",
        "src/public/lib/hcoona-release-smoke-npm/three.release.yml",
        "src/public/lib/hcoona-release-smoke-nuget/three.release.yml",
        "src/public/lib/hcoona-release-smoke-pypi/three.release.yml",
        "src/public/lib/hcoona-release-smoke-rubygems/three.release.yml",
        "src/public/lib/hcoona-release-smoke-wxt/three.release.yml",
        "src/public/lib/hcoona-release-smoke/three.release.yml",
        "src/public/lib/hexo-renderer-asciidoc/three.release.yml",
        "src/public/lib/nbgv-python/three.release.yml",
        "src/public/lib/steam-account-history-to-csv/three.release.yml",
    }
)
REQUIRED_OFFICIAL_TESTS = frozenset(
    {
        "test_official_default_rejects_non_public_release_ref",
        "test_official_tag_push_allows_branch_only_project_spec",
        "test_official_manual_dispatch_allows_display_name_alias",
        "test_official_tag_push_rejects_mismatched_project_identity",
        "test_official_canary_override_allows_allowlisted_project",
        "test_official_canary_override_rejects_non_allowlisted_project",
        "test_release_shell_steps_use_env_for_workflow_inputs_and_vars",
        "test_release_workflows_generate_final_release_reports",
        "test_ci_release_pipeline_architecture_detects_active_split_topology",
        "test_ci_acceptance_matrix_fixture_tracks_lld_scenarios",
        "test_ci_acceptance_matrix_rows_are_actionable",
    }
)
RETIRED_BUDDY_TEST_NAMES = frozenset(
    {
        "test_buddy_entry_is_not_restricted_by_public_release_ref",
        "test_buddy_force_github_release_tag_mismatch_stays_conflicting",
        "test_ensure_tags_buddy_force_does_not_retarget_existing_active_tag",
        "test_buddy_github_release_deactivation_blocks_publish_handoff",
        "test_buddy_target_ref_policy_authorizes_default_branch",
        "test_buddy_target_ref_policy_rejects_unsafe_refs",
        "test_buddy_reusable_orchestrate_call_grants_publish_upper_bound",
        "test_buddy_entry_authorizes_resolved_targets_by_reachability",
        "test_buddy_workflow_rejects_rerun_attempts_before_authorization",
        "test_buddy_github_release_without_attestations_fails_before_mutation",
    }
)
RETIRED_MIXED_BUDDY_TEST_NAMES = frozenset(
    {"test_acceptance_gate_pins_r41_release_completion_and_buddy_regressions"}
)
RETIRED_ACCEPTANCE_ROW_IDS = frozenset(
    {
        "buddy-to-official-promotion",
        "buddy-force-rejected-after-official-freeze",
    }
)
REMOVED_LIVE_GATE_IDS = frozenset({"buddy-github-packages-live-publication"})
RETIRED_MATRIX_TEST_NODEIDS = frozenset(
    {
        "src/public/lib/three-workflow-release-planner/tests/test_planner.py::"
        "test_buddy_force_official_frozen_version_fails_closed",
        "src/public/lib/three-workflow-release-planner/tests/test_planner.py::"
        "test_buddy_github_release_only_target_fails_closed_when_deactivated",
        "src/public/lib/three-workflow-release-planner/tests/test_planner.py::"
        "test_buddy_smoke_projects_plan_github_packages_publish",
        "tests/test_workflow_release_control.py::"
        "test_buddy_entry_authorizes_resolved_targets_by_reachability",
        "tests/test_workflow_release_control.py::"
        "test_buddy_github_release_without_attestations_fails_before_mutation",
        "tests/test_workflow_release_control.py::"
        "test_buddy_target_ref_policy_authorizes_default_branch",
        "tests/test_workflow_release_control.py::"
        "test_buddy_target_ref_policy_rejects_unsafe_refs",
    }
)
RETIRED_GATE_TEST_NODEIDS = frozenset(
    {
        "src/public/lib/three-workflow-release-planner/tests/test_planner.py::"
        "test_buddy_mixed_github_release_fails_closed_when_deactivated",
        "tests/test_workflow_release_control.py::"
        "test_acceptance_gate_pins_r41_release_completion_and_buddy_regressions",
        "tests/test_workflow_release_control.py::"
        "test_buddy_entry_is_not_restricted_by_public_release_ref",
        "tests/test_workflow_release_control.py::"
        "test_buddy_github_release_deactivation_blocks_publish_handoff",
    }
)
PRESERVED_GATE_TEST_NODEIDS = frozenset(
    {
        "tests/test_workflow_release_control.py::"
        "test_ci_acceptance_matrix_fixture_tracks_lld_scenarios",
        "tests/test_workflow_release_control.py::"
        "test_ci_acceptance_matrix_rows_are_actionable",
        "tests/test_workflow_release_control.py::"
        "test_ci_release_pipeline_architecture_detects_active_split_topology",
        "tests/test_workflow_release_control.py::"
        "test_official_default_rejects_non_public_release_ref",
        "tests/test_workflow_release_control.py::"
        "test_official_tag_push_allows_branch_only_project_spec",
        "tests/test_workflow_release_control.py::"
        "test_official_manual_dispatch_allows_display_name_alias",
    }
)
PRESERVED_BOOTSTRAP_GOVERNANCE_EXACT_PATHS = frozenset(
    {
        ".github/CODEOWNERS",
        ".github/actionlint.yaml",
        ".github/workflows/ci.yml",
        ".github/workflows/release-orchestrate.yml",
        ".github/workflows/release-resolve.yml",
        ".github/workflows/release-build-python.yml",
        ".github/workflows/release-build-node-pack.yml",
        ".github/workflows/release-build-dotnet.yml",
        ".github/workflows/release-build-ruby-gem.yml",
        ".github/workflows/release-build-wxt.yml",
        ".github/workflows/release-create-github-release.yml",
        ".github/workflows/release-prepare-release-notes.yml",
        ".github/workflows/official.yml",
        ".github/workflows/docs/DESIGN.v2.md",
        "eng/release/target-instances.yml",
        "eng/release/buddy-target-refs.yml",
    }
)
FORBIDDEN_ACTIVE_MATRIX_EVIDENCE_PATHS = frozenset(LEGACY_ENTRY_PATHS)


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
        with_inputs = job.get("with", {})
        if (
            isinstance(uses, str)
            and Path(uses).name
            in {"release-buddy.yml", "release-orchestrate.yml"}
            and isinstance(with_inputs, dict)
            and with_inputs.get("channel", "buddy") == "buddy"
        ):
            return True
    return False


def _legacy_buddy_routes(root: Path) -> tuple[tuple[str, ...], ...]:
    documents = _workflow_documents(root)
    routes: set[tuple[str, ...]] = set()
    for name, document in documents.items():
        if Path(name).name in FORBIDDEN_COMPATIBILITY_BASENAMES:
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


def _write_workflow(root: Path, name: str, document: str) -> None:
    path = root / name
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(document, encoding="utf-8")


def _test_function_names(path: Path) -> frozenset[str]:
    tree = ast.parse(path.read_text(encoding="utf-8"), filename=str(path))
    return frozenset(
        node.name
        for node in ast.walk(tree)
        if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef))
        and node.name.startswith("test_")
    )


def _test_nodeids(value: object) -> frozenset[str]:
    nodeids: set[str] = set()
    if isinstance(value, dict):
        if value.get("type") == "test" and isinstance(value.get("value"), str):
            nodeids.add(value["value"])
        for child in value.values():
            nodeids.update(_test_nodeids(child))
    elif isinstance(value, list):
        for child in value:
            nodeids.update(_test_nodeids(child))
    return frozenset(nodeids)


def _evidence_path_values(value: object) -> frozenset[str]:
    paths: set[str] = set()
    if isinstance(value, dict):
        if value.get("type") == "path" and isinstance(value.get("value"), str):
            paths.add(value["value"])
        for child in value.values():
            paths.update(_evidence_path_values(child))
    elif isinstance(value, list):
        for child in value:
            paths.update(_evidence_path_values(child))
    return frozenset(paths)


def _ast_tuple_assignment(path: Path, name: str) -> tuple[str, ...]:
    tree = ast.parse(path.read_text(encoding="utf-8"), filename=str(path))
    for node in tree.body:
        if not isinstance(node, ast.Assign):
            continue
        if any(
            isinstance(target, ast.Name) and target.id == name
            for target in node.targets
        ):
            value = ast.literal_eval(node.value)
            assert isinstance(value, tuple), name
            assert all(isinstance(item, str) for item in value), name
            return value
    pytest.fail(f"{name} assignment not found in {path}")


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
    rules: tuple[tuple[str, tuple[str, ...]], ...], path: str
) -> tuple[str, ...]:
    owners: tuple[str, ...] = ()
    for pattern, candidate in rules:
        if _codeowners_pattern_matches(pattern, path):
            owners = candidate
    return owners


def _hk_workflow_release_globs() -> tuple[str, ...]:
    content = (REPO_ROOT / "hk.pkl").read_text(encoding="utf-8")
    block = content.split("local workflow_release_files =", 1)[1].split(
        '["workflow-release-control-tests"]', 1
    )[0]
    return tuple(re.findall(r'"([^"]+)"', block))


def _hk_selected_paths(
    changes: tuple[tuple[str, str, str | None], ...],
) -> tuple[str, ...]:
    globs = _hk_workflow_release_globs()
    paths = {
        path
        for _kind, new_path, old_path in changes
        for path in (old_path, new_path)
        if path is not None
        and any(fnmatch.fnmatchcase(path, pattern) for pattern in globs)
    }
    return tuple(sorted(paths))


def test_legacy_buddy_entry_files_are_exactly_retired() -> None:
    """Require only the two named legacy entries to disappear."""
    assert {
        path for path in LEGACY_ENTRY_PATHS if (REPO_ROOT / path).exists()
    } == set()
    assert not (WORKFLOWS / "legacy-buddy.yml").exists()
    assert (WORKFLOWS / "official.yml").is_file()
    assert (WORKFLOWS / "ci.yml").is_file()


def test_real_workflow_topology_has_no_legacy_buddy_route() -> None:
    """Traverse the real workflow call topology from dispatch entries."""
    documents = _workflow_documents(WORKFLOWS)

    assert {"official.yml", "ci.yml"} <= set(documents)
    assert _legacy_buddy_routes(WORKFLOWS) == ()


@pytest.mark.parametrize(
    ("name", "documents", "expected"),
    [
        (
            "renamed direct route",
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
            "renamed direct allowlist route",
            {
                "compatibility.yml": """
on: {workflow_dispatch: {}}
jobs:
  publish:
    uses: ./.github/workflows/release-orchestrate.yml
    with:
      channel: buddy
      channel_allowlist: buddy
""",
                "release-orchestrate.yml": (
                    "on: {workflow_call: {}}\njobs: {}\n"
                ),
            },
            (("compatibility.yml",),),
        ),
        (
            "new indirect route",
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
            "forbidden compatibility name",
            {"legacy-buddy.yml": "on: {workflow_dispatch: {}}\njobs: {}\n"},
            (("legacy-buddy.yml",),),
        ),
    ],
)
def test_synthetic_renamed_and_new_compatibility_routes_are_rejected(
    tmp_path: Path,
    name: str,
    documents: dict[str, str],
    expected: tuple[tuple[str, ...], ...],
) -> None:
    """Prove semantic detection catches renamed and indirect routes."""
    for filename, content in documents.items():
        _write_workflow(tmp_path, filename, content)

    assert _legacy_buddy_routes(tmp_path) == expected, name


def test_buddy_only_acceptance_rows_nodeids_and_live_gates_are_removed() -> (
    None
):
    """Require exact retired matrix Buddy evidence to disappear."""
    matrix_path = (
        REPO_ROOT / "tests/fixtures/workflow-release-acceptance-matrix.json"
    )
    matrix = yaml.safe_load(matrix_path.read_text(encoding="utf-8"))
    rows = matrix["rows"]
    live_gates = matrix["live-gates"]
    active_rows = [
        row for row in rows if row["id"] not in RETIRED_ACCEPTANCE_ROW_IDS
    ]
    active_live_gates = {
        key: value
        for key, value in live_gates.items()
        if key not in REMOVED_LIVE_GATE_IDS
    }

    assert {row["id"] for row in rows}.isdisjoint(RETIRED_ACCEPTANCE_ROW_IDS)
    assert set(live_gates).isdisjoint(REMOVED_LIVE_GATE_IDS)
    assert _test_nodeids(rows).isdisjoint(RETIRED_MATRIX_TEST_NODEIDS)
    assert _evidence_path_values(active_rows).isdisjoint(
        FORBIDDEN_ACTIVE_MATRIX_EVIDENCE_PATHS
    )
    assert _evidence_path_values(active_live_gates).isdisjoint(
        FORBIDDEN_ACTIVE_MATRIX_EVIDENCE_PATHS
    )
    assert "official-github-packages-live-publication" not in live_gates
    dispatch_row = next(
        row for row in rows if row["id"] == "dispatch-sha-pinning"
    )
    assert dispatch_row["scenario"] == "Dispatch SHA pinning"
    assert "eng/release/buddy-target-refs.yml" not in _evidence_path_values(
        [dispatch_row]
    )
    for row_id in (
        "node-package-build-and-github-release",
        "ruby-gem-build-and-github-release",
    ):
        row = next(row for row in rows if row["id"] == row_id)
        assert "official-github-packages-live-publication" not in {
            reference.get("value")
            for references in row["evidence"].values()
            for reference in references
        }


def test_acceptance_gate_drops_buddy_nodes_and_retains_official_ci() -> None:
    """Pin exact gate inventory changes without blanket token scans."""
    nodeids = frozenset(
        _ast_tuple_assignment(
            REPO_ROOT / "eng/scripts/workflow_release_acceptance_gate.py",
            "MANDATORY_TEST_NODEIDS",
        )
    )

    assert nodeids.isdisjoint(RETIRED_GATE_TEST_NODEIDS)
    assert nodeids >= PRESERVED_GATE_TEST_NODEIDS


def test_legacy_v1_buddy_only_and_mixed_test_nodes_are_retired_or_split() -> (
    None
):
    """Retire Buddy tests while retaining named Official and CI evidence."""
    path = REPO_ROOT / "tests/test_workflow_release_control.py"
    names = _test_function_names(path)

    assert names.isdisjoint(RETIRED_BUDDY_TEST_NAMES)
    assert names.isdisjoint(RETIRED_MIXED_BUDDY_TEST_NAMES)
    assert names >= REQUIRED_OFFICIAL_TESTS


@pytest.mark.parametrize("relative_path", ACTIVE_V1_DOCS)
def test_active_v1_docs_describe_retirement_not_an_active_buddy_route(
    relative_path: str,
) -> None:
    """Allow legacy workflow references only in explicit retirement context."""
    content = (REPO_ROOT / relative_path).read_text(encoding="utf-8")
    lowered = content.casefold()
    retirement_words = ("retired", "removed", "no longer", "legacy")
    references = []
    for filename in ("buddy.yml", "release-buddy.yml"):
        references.extend(
            match.start() for match in re.finditer(re.escape(filename), lowered)
        )

    for position in references:
        context = lowered[max(0, position - 240) : position + 240]
        assert any(word in context for word in retirement_words), (
            relative_path,
            context,
        )


def test_release_orchestrate_caller_completeness_is_official_only(
    tmp_path: Path,
) -> None:
    """Execute the caller completeness helper without any legacy Buddy file."""
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


def test_bootstrap_governance_exact_paths_drop_only_legacy_buddy_entries() -> (
    None
):
    """Parse bootstrap paths and preserve non-entry Buddy policy."""
    paths = frozenset(
        _ast_tuple_assignment(
            REPO_ROOT / "eng/scripts/workflow_release_control.py",
            "_BOOTSTRAP_GOVERNANCE_EXACT_PATHS",
        )
    )

    assert paths == PRESERVED_BOOTSTRAP_GOVERNANCE_EXACT_PATHS
    assert paths.isdisjoint(LEGACY_ENTRY_PATHS)


def test_actionlint_drops_deleted_buddy_override_and_keeps_active() -> None:
    """Parse actionlint path overrides instead of scanning for Buddy text."""
    document = yaml.safe_load(
        (REPO_ROOT / ".github/actionlint.yaml").read_text(encoding="utf-8")
    )
    paths = document["paths"]

    assert set(paths).isdisjoint(LEGACY_ENTRY_PATHS)
    assert ".github/workflows/official.yml" in paths
    assert ".github/workflows/release-orchestrate.yml" in paths


def test_official_ci_v2_and_all_release_descriptors_are_preserved() -> None:
    """Pin every protected preservation inventory against deletion."""
    for path in PRESERVED_OFFICIAL_CI_WORKFLOWS | PRESERVED_V2_FILES:
        assert (REPO_ROOT / path).is_file(), path
    actual_descriptors = frozenset(
        path.relative_to(REPO_ROOT).as_posix()
        for path in (REPO_ROOT / "src").glob("**/three.release.yml")
    )

    assert actual_descriptors == PRESERVED_DESCRIPTORS
    assert len(actual_descriptors) == len(PRESERVED_DESCRIPTORS)


def test_preserved_v2_and_reusable_api_are_explicitly_superseded() -> None:
    """Keep v2 history without advertising a live legacy Buddy route."""
    design = (REPO_ROOT / ".github/workflows/docs/DESIGN.v2.md").read_text(
        encoding="utf-8"
    )
    normalized_design = " ".join(design.split()).replace("> ", "")
    assert "Archived and superseded" in normalized_design
    assert "Workflow Delivery v3 is active and normative" in normalized_design
    assert "retired with no compatibility" in normalized_design

    orchestrate = _workflow_documents(WORKFLOWS)["release-orchestrate.yml"]
    workflow_call = _triggers(orchestrate)["workflow_call"]
    inputs = workflow_call["inputs"]
    channel_description = inputs["channel"]["description"]
    force_description = inputs["force_update_tag"]["description"]
    assert "'buddy' is reserved and rejected" in channel_description
    assert "v3 Buddy uses separate workflows" in channel_description
    assert "Legacy Buddy entry callers are retired and rejected" in (
        force_description
    )
    workflow_text = (
        REPO_ROOT / ".github/workflows/release-orchestrate.yml"
    ).read_text(encoding="utf-8")
    assert "Legacy 'release-buddy' is not an active" in workflow_text
    assert "prerequisite after commit 11" in workflow_text
    normalized_workflow = " ".join(workflow_text.split())
    assert "channel=buddy has no active caller route after commit 11" in (
        normalized_workflow
    )


def test_v3_buddy_workflows_keep_their_exact_topology() -> None:
    """Keep v3 Buddy workflows outside the retired v1 entry inventory."""
    inventory = PRESERVED_V3_BUDDY_WORKFLOW_JOBS.items()
    for relative_path, expected_jobs in inventory:
        document = yaml.safe_load(
            (REPO_ROOT / relative_path).read_text(encoding="utf-8")
        )

        assert "workflow_dispatch" in _triggers(document), relative_path
        assert frozenset(document["jobs"]) == expected_jobs, relative_path


def test_official_and_ci_workflows_keep_real_parseable_topology() -> None:
    """Parse preserved entries and pin their required triggers and jobs."""
    documents = _workflow_documents(WORKFLOWS)
    official = documents["official.yml"]
    ci = documents["ci.yml"]

    assert "workflow_dispatch" in _triggers(official)
    assert "push" in _triggers(official)
    assert {"authorize-entry", "orchestrate", "report"} <= set(official["jobs"])
    assert "pull_request" in _triggers(ci)
    assert ci["jobs"]


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
        (
            "rename",
            ".github/workflows/legacy-buddy.yml",
            ".github/workflows/release-buddy.yml",
        ),
        ("add", ".github/workflows/new-buddy-compatibility.yml", None),
    ],
)
def test_root_hk_selects_deletions_and_renamed_or_new_compatibility_attempts(
    kind: str,
    new_path: str,
    old_path: str | None,
) -> None:
    """Evaluate actual HK globs against delete, rename, and add histories."""
    selected = _hk_selected_paths(((kind, new_path, old_path),))

    assert new_path in selected
    if old_path is not None:
        assert old_path in selected


def test_codeowners_covers_deleted_and_future_buddy_routes() -> None:
    """Apply ordered final-match ownership to absent and synthetic paths."""
    rules = _codeowners_rules(
        (REPO_ROOT / ".github/CODEOWNERS").read_text(encoding="utf-8")
    )
    paths = {
        *LEGACY_ENTRY_PATHS,
        ".github/workflows/legacy-buddy.yml",
        ".github/workflows/compatibility.yml",
        ".github/workflows/new-buddy-compatibility.yml",
    }

    actual = {path: _final_owners(rules, path) for path in paths}

    assert actual == dict.fromkeys(paths, ("@hcoona",))
