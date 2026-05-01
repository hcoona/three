# ruff: noqa: SLF001
"""Tests for workflow-release control-plane helper script."""

from __future__ import annotations

import importlib.util
import json
import shutil
from copy import deepcopy
from itertools import pairwise
from pathlib import Path

import pytest
import yaml
from three_workflow_release_contracts import (
    ArtifactNameInputs,
    artifact_name,
    validate_contract,
)

REPO_ROOT = Path(__file__).parents[1]
SCRIPT = REPO_ROOT / "eng/scripts/workflow_release_control.py"
FIXTURES = (
    REPO_ROOT
    / "src/public/lib/three-workflow-release-contracts/tests/fixtures/valid"
)
SCRATCH = REPO_ROOT / ".pytest-workflow-release-control"
SHA_B = "b" * 64
SHA_C = "c" * 64
SIGNER_WORKFLOW = "hcoona/three/.github/workflows/release-publish-node.yml"

spec = importlib.util.spec_from_file_location(
    "workflow_release_control", SCRIPT
)
assert spec is not None
control = importlib.util.module_from_spec(spec)
assert spec.loader is not None
spec.loader.exec_module(control)


def _load(name: str) -> dict[str, object]:
    """Load one valid workflow-release contract fixture."""
    return json.loads((FIXTURES / name).read_text(encoding="utf-8"))


def _workflow(name: str) -> str:
    """Read one workflow file as text for structural assertions."""
    return (REPO_ROOT / ".github/workflows" / name).read_text(encoding="utf-8")


def _release_workflow_paths() -> list[Path]:
    """Return release workflow paths in deterministic order."""
    return sorted((REPO_ROOT / ".github/workflows").glob("release-*.yml"))


def _step_block(workflow: str, step_name: str) -> str:
    """Return the YAML block for one named workflow step."""
    start = workflow.index(f"      - name: {step_name}\n")
    end = workflow.find("\n      - ", start + 1)
    if end == -1:
        end = len(workflow)
    return workflow[start:end]


def test_normalize_project_ids_trims_splits_deduplicates_and_sorts() -> None:
    """Normalize UI project input into a stable planner filter list."""
    assert control._normalize_project_ids(" beta,alpha\n beta ,,gamma ") == [
        "alpha",
        "beta",
        "gamma",
    ]


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

        assert request["kind"] == "publish-request"
        assert request["publish-node-id"] == "publish-node/gh"
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
        "Execute build unit",
    ):
        block = _step_block(workflow, step_name)
        assert "        shell: bash\n" in block
        shell_index = block.index("        shell: bash\n")
        run_index = block.index("        run: |")
        assert shell_index < run_index


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
            '--requested-project-ids "$RELEASE_REQUESTED_PROJECT_IDS" \\'
            in run_script
        )
        assert '--dry-run "$RELEASE_DRY_RUN" \\' in run_script
        assert '--validation-build "$RELEASE_VALIDATION_BUILD" \\' in run_script
        assert '--repository "$RELEASE_REPOSITORY" \\' in run_script
        assert '--actor "$RELEASE_ACTOR" \\' in run_script
        assert '--ref "$RELEASE_REF" \\' in run_script


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


def test_reusable_publish_jobs_use_topology_scoped_permissions() -> None:
    """Reusable publish classes must not receive unrelated write grants."""
    publish = yaml.safe_load(_workflow("release-publish-node.yml"))
    orchestrate = yaml.safe_load(_workflow("release-orchestrate.yml"))

    github_release_permissions = {
        "contents": "write",
        "actions": "read",
        "id-token": "write",
        "attestations": "write",
        "artifact-metadata": "write",
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

    expected_orchestrator_permissions = {
        "publish-reusable-github-release": github_release_permissions,
        "publish-reusable-github-packages": github_packages_permissions,
        "publish-reusable-external-oidc": external_oidc_permissions,
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
