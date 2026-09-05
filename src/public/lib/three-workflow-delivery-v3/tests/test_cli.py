"""Tests for the bounded Workflow Delivery v3 CLI."""

from __future__ import annotations

import hashlib
import inspect
import json
import subprocess
import tomllib
from argparse import Namespace
from dataclasses import replace
from datetime import UTC, datetime
from pathlib import Path
from types import SimpleNamespace
from typing import TYPE_CHECKING, Self, cast
from urllib.request import Request

import pytest
from three_workflow_delivery_v3 import cli as cli_module
from three_workflow_delivery_v3.adapters.github_packages import (
    GitHubPackagesPublishPreflight,
)
from three_workflow_delivery_v3.canonical import (
    JsonValue,
    canonical_sha256,
    canonicalize,
)
from three_workflow_delivery_v3.ci.planner import (
    form_pull_request_candidate,
    form_slice_validation_candidate,
)
from three_workflow_delivery_v3.records.artifacts import ArtifactReference
from three_workflow_delivery_v3.records.ci import (
    CI_WORKFLOW_PATH,
    ci_qualification_snapshot_digest,
)
from three_workflow_delivery_v3.records.release import (
    BuddyExecutionIdentity,
    PublicationSnapshot,
    ReleaseAttemptBinding,
    ReleaseAttemptIdentity,
)
from three_workflow_delivery_v3.release import LiveEligibilityAdmissionMode
from three_workflow_delivery_v3.release.identity import (
    OFFICIAL_SIMULATION_PRODUCER,
    normalize_official_simulation_intent,
)
from three_workflow_delivery_v3.repository import (
    CompilationContext,
    admit_repository_model_snapshot,
    first_slice_provider_manifest,
    provider_binding,
)
from three_workflow_delivery_v3.repository.descriptors import (
    FIRST_SLICE_POLICY_PATH,
)
from three_workflow_delivery_v3.repository.node_provider import (
    AUTHORITATIVE_REMOTE,
    PROVIDER_EXECUTION_CLASS,
    PROVIDER_EXECUTION_MODE,
    PROVIDER_IMPLEMENTATION_ID,
    PROVIDER_LOGICAL_ID,
    TAG_REFSPEC,
    CheckoutEvidence,
    GlobalInput,
    NbgvFacts,
    NodeProviderResult,
    ProjectNode,
    ProviderBinding,
)

if TYPE_CHECKING:
    import argparse

REPO_ROOT = Path(__file__).resolve().parents[5]
PACKAGE_ROOT = REPO_ROOT / "src/public/lib/three-workflow-delivery-v3"
PRODUCT_PATH = "src/public/lib/hcoona-release-smoke-npm"
WORKFLOW_RUN_ID = 8101
RUN_ATTEMPT = 2
ARGPARSE_ERROR = 2
LIVE_GITHUB_PACKAGES_TRANSPORT_SELECTIONS = 2
NPM_ARTIFACT_ID = 303
NPM_ARTIFACT_DIGEST = "a" * 64
GITHUB_API_TIMEOUT_SECONDS = 10
EXPECTED_ELAPSED_SECONDS = 60


def _head() -> str:
    return subprocess.run(
        ("git", "rev-parse", "HEAD"),  # noqa: S607
        cwd=REPO_ROOT,
        check=True,
        capture_output=True,
        text=True,
    ).stdout.strip()


def _run(repo: Path, *command: str) -> str:
    return subprocess.run(  # noqa: S603
        command,
        cwd=repo,
        check=True,
        capture_output=True,
        text=True,
    ).stdout.strip()


def _initialize_repository(repo: Path) -> None:
    _run(repo, "git", "init", "--quiet")
    _run(repo, "git", "config", "user.name", "Workflow Delivery Test")
    _run(
        repo,
        "git",
        "config",
        "user.email",
        "workflow-delivery@example.invalid",
    )


def _commit_all(repo: Path) -> str:
    _run(repo, "git", "add", "--all")
    _run(repo, "git", "commit", "--quiet", "--message", "fixture")
    return _run(repo, "git", "rev-parse", "HEAD")


def _write(path: Path, content: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(content, encoding="utf-8")


def _write_canonical(path: Path, document: JsonValue) -> Path:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(canonicalize(document))
    return path


def _target_authoring_repo(
    tmp_path: Path,
    *,
    missing_authoring: str | None = None,
    malformed_authoring: str | None = None,
) -> tuple[Path, str]:
    repo = tmp_path / "repo"
    repo.mkdir()
    _initialize_repository(repo)
    source_product = REPO_ROOT / PRODUCT_PATH
    target_product = repo / PRODUCT_PATH
    for name in (
        "package.json",
        "version.json",
        "workflow-delivery.release-unit.yml",
        "workflow-delivery.quality.yml",
    ):
        authoring_kind = {
            "workflow-delivery.release-unit.yml": "descriptor",
            "workflow-delivery.quality.yml": "quality",
        }.get(name)
        if authoring_kind is not None and missing_authoring == authoring_kind:
            continue
        content = (source_product / name).read_text(encoding="utf-8")
        if authoring_kind is not None and malformed_authoring == authoring_kind:
            content = "schema: [unterminated"
        _write(
            target_product / name,
            content,
        )
    if missing_authoring != "policy":
        policy_content = (REPO_ROOT / FIRST_SLICE_POLICY_PATH).read_text(
            encoding="utf-8"
        )
        if malformed_authoring == "policy":
            policy_content = "schema: [unterminated"
        _write(repo / FIRST_SLICE_POLICY_PATH, policy_content)
    for name in (
        "package.json",
        "pnpm-lock.yaml",
        "pnpm-workspace.yaml",
        "version.json",
    ):
        _write(repo / name, (REPO_ROOT / name).read_text(encoding="utf-8"))
    return repo, _commit_all(repo)


def _provider_arguments(
    *,
    repo_root: Path,
    target: str,
    fetch_depth: int = 0,
    include_transport: bool = False,
) -> list[str]:
    arguments = [
        "--repo-root",
        str(repo_root),
        "--project-path",
        PRODUCT_PATH,
        "--request-id",
        "release-request-cli",
        "--purpose",
        "live-release",
        "--workflow-run-id",
        str(WORKFLOW_RUN_ID),
        "--run-attempt",
        str(RUN_ATTEMPT),
        "--target",
        target,
        "--compiler-producer",
        "compile-model",
        "--provider-producer",
        "discover-node",
        "--control",
        f"workflow-delivery-v3:{target}",
        "--fetch-depth",
        str(fetch_depth),
        "--no-persist-credentials",
    ]
    if include_transport:
        arguments.extend(
            [
                "--request-artifact-id",
                "101",
                "--request-artifact-digest",
                "sha256:" + ("7" * 64),
                "--transport-id",
                "202",
                "--transport-digest",
                "sha256:" + ("8" * 64),
            ]
        )
    return arguments


def _fake_provider_result(binding: ProviderBinding) -> NodeProviderResult:
    global_inputs = tuple(
        GlobalInput(
            path=path,
            content_digest=(
                "sha256:"
                + hashlib.sha256((REPO_ROOT / path).read_bytes()).hexdigest()
            ),
            project_ids=("@hcoona/hcoona-release-smoke-npm",),
        )
        for path in (
            "package.json",
            "pnpm-lock.yaml",
            "pnpm-workspace.yaml",
            f"{PRODUCT_PATH}/version.json",
            "version.json",
        )
    )
    return NodeProviderResult(
        binding=binding,
        provider_logical_id=PROVIDER_LOGICAL_ID,
        provider_implementation_id=PROVIDER_IMPLEMENTATION_ID,
        execution_mode=PROVIDER_EXECUTION_MODE,
        execution_class=PROVIDER_EXECUTION_CLASS,
        toolchain=(("node", "v24.14.0"), ("pnpm", "11.21.0")),
        manifest_digest=(
            "sha256:"
            + hashlib.sha256(
                (REPO_ROOT / PRODUCT_PATH / "package.json").read_bytes()
            ).hexdigest()
        ),
        configuration_digest=canonical_sha256(
            {
                "schema": "workflow-delivery/v3/node-provider-configuration",
                "global-inputs": [
                    global_input.to_document() for global_input in global_inputs
                ],
            }
        ),
        checkout=CheckoutEvidence(
            target=binding.target,
            head=binding.target,
            shallow=False,
            ancestry_complete=True,
            tags_complete=True,
            credentials_persisted=False,
            authoritative_remote=AUTHORITATIVE_REMOTE,
            authoritative_remote_url="file:///authoritative-remote.git",
            tag_refspec=TAG_REFSPEC,
        ),
        project_nodes=(
            ProjectNode(
                project_id="@hcoona/hcoona-release-smoke-npm",
                package_name="@hcoona/hcoona-release-smoke-npm",
                path=PRODUCT_PATH,
                manifest_path=f"{PRODUCT_PATH}/package.json",
                private=False,
                workspace_dependencies=(),
            ),
        ),
        global_inputs=global_inputs,
        build_capabilities=("node/npm-package-v1",),
        nbgv=NbgvFacts(
            canonical_version="1.2.3",
            sem_ver1="1.2.3-beta-0042-e123456",
            sem_ver2="1.2.3-beta.42.ge123456",
            version_height=42,
            git_commit_id=binding.target,
            public_release=False,
            npm_package_version="1.2.3-beta.42.ge123456",
            node_api_result_digest="sha256:" + ("a" * 64),
        ),
        unresolved=(),
        conflicts=(),
        outcome="success",
        diagnostic_reference=None,
    )


def _plan_fixture(  # noqa: PLR0913
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    *,
    event_kind: str,
    changed_paths: tuple[str, ...] = (),
    missing_authoring: str | None = None,
    malformed_authoring: str | None = None,
    expect_failure: bool = False,
) -> tuple[Path, Path, dict[str, JsonValue]]:
    monkeypatch.setattr(cli_module, "_current_epoch_seconds", lambda: 1000)
    repo, target = _target_authoring_repo(
        tmp_path,
        missing_authoring=missing_authoring,
        malformed_authoring=malformed_authoring,
    )
    request_id = "pr-17" if event_kind == "pull_request" else "slice-17"
    purpose = (
        "ci-pr-slice-shadow"
        if event_kind == "pull_request"
        else "slice-validation"
    )
    context = CompilationContext(
        request_id=request_id,
        purpose=purpose,
        workflow_run_id=WORKFLOW_RUN_ID,
        run_attempt=RUN_ATTEMPT,
        target=target,
        producer="plan",
        control=f"workflow-delivery-v3:{target}",
        catalog_digest=cli_module.catalog_digest(),
    )
    manifest = first_slice_provider_manifest(
        context,
        provider_producer="discover-node",
    )
    provider = _fake_provider_result(
        provider_binding(manifest, "node-first-slice")
    )
    provider_document = provider.to_document()
    provider_document["provider-request-manifest-digest"] = (
        manifest.manifest_digest
    )
    provider_document["result-digest"] = provider.result_digest
    provider_path = _write_canonical(
        tmp_path / f"{event_kind}-provider.json",
        provider_document,
    )
    if event_kind == "pull_request":
        candidate = form_pull_request_candidate(
            repository="hcoona/three",
            request_id=request_id,
            workflow_run_id=WORKFLOW_RUN_ID,
            run_attempt=RUN_ATTEMPT,
            selected_ref="refs/pull/17/merge",
            base_sha="1" * 40,
            head_sha="2" * 40,
            tested_merge_sha=target,
            comparison_identity=("1" * 40, "2" * 40),
        )
    else:
        candidate = form_slice_validation_candidate(
            repository="hcoona/three",
            request_id=request_id,
            workflow_run_id=WORKFLOW_RUN_ID,
            run_attempt=RUN_ATTEMPT,
            selected_ref="refs/heads/main",
            target=target,
        )
    request_path = _write_canonical(
        tmp_path / f"{event_kind}-request.json",
        {
            "schema": "workflow-delivery/v3/ci-request",
            "candidate": candidate.to_document(),
            "changed-paths": list(changed_paths),
        },
    )
    plan_path = tmp_path / f"{event_kind}-plan.json"
    context_path = tmp_path / f"{event_kind}-adapter-context.json"
    output_path = tmp_path / f"{event_kind}-github-output"

    def command_stdout(command: tuple[str, ...], cwd: Path) -> str:
        assert cwd == repo
        return "1700000000" if command[1] == "show" else "11.9.0"

    monkeypatch.setattr(cli_module, "_command_stdout", command_stdout)
    code = cli_module.main(
        [
            "ci",
            "plan",
            "--repo-root",
            str(repo),
            "--request",
            str(request_path),
            "--provider-result",
            str(provider_path),
            "--request-artifact-id",
            "101",
            "--request-artifact-digest",
            "7" * 64,
            "--provider-artifact-id",
            "202",
            "--provider-artifact-digest",
            "8" * 64,
            "--output",
            str(plan_path),
            "--adapter-context-output",
            str(context_path),
            "--github-output",
            str(output_path),
        ]
    )
    if expect_failure:
        assert code == 1
        return plan_path, context_path, {}
    assert code == 0
    plan = cast("dict[str, JsonValue]", json.loads(plan_path.read_bytes()))
    return plan_path, context_path, plan


def _mechanical_result(
    path: Path,
    plan: dict[str, JsonValue],
    lane: str,
    outcome: str,
) -> Path:
    return _write_canonical(
        path,
        {
            "schema": "workflow-delivery/v3/ci-node-adapter-result",
            "lane-id": lane,
            "plan-digest": canonical_sha256(plan),
            "repository-model-digest": plan["repository-model-digest"],
            "outcome": outcome,
            "output-digests": ["sha256:" + ("9" * 64)],
            "artifact": (
                {
                    "tarball-basename": (
                        "hcoona-hcoona-release-smoke-npm-1.2.3.tgz"
                    ),
                    "content-sha256": f"sha256:{NPM_ARTIFACT_DIGEST}",
                    "content-sha512": "sha512:" + ("b" * 128),
                    "byte-size": 1234,
                    "provenance-digest": "sha256:" + ("c" * 64),
                    "entries": [
                        "package/README.md",
                        "package/dist/index.js",
                        "package/package.json",
                        "package/workflow-delivery/provenance.json",
                    ],
                    "lifecycle-scripts": [["test", "node --test"]],
                }
                if lane == "npm-artifact-build" and outcome == "success"
                else None
            ),
            "diagnostics": [f"{lane} {outcome}"],
        },
    )


def _blocked_pr_decision_fixture(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> tuple[Path, str, Path, Path, Path]:
    plan_path, _, plan = _plan_fixture(
        tmp_path,
        monkeypatch,
        event_kind="pull_request",
        changed_paths=("unmodeled/bootstrap.txt",),
    )
    plan_digest = canonical_sha256(plan)
    lane_arguments: list[str] = []
    for lane in cli_module.CI_LANE_IDS:
        result = tmp_path / f"{lane}-bootstrap-result.json"
        assert (
            cli_module.main(
                [
                    "ci",
                    "lane-result",
                    "--plan",
                    str(plan_path),
                    "--plan-digest",
                    plan_digest,
                    "--lane-id",
                    lane,
                    "--output",
                    str(result),
                ]
            )
            == 0
        )
        lane_arguments.extend(["--lane-result", str(result)])
    candidate = cast("dict[str, JsonValue]", plan["candidate"])
    monkeypatch.setattr(
        cli_module,
        "_fetch_current_pull_request",
        lambda **_: {
            "base": {"sha": candidate["base-sha"]},
            "head": {"sha": candidate["head-sha"]},
            "merge_commit_sha": candidate["tested-merge-sha"],
        },
    )
    decision = tmp_path / "bootstrap-decision.json"
    summary = tmp_path / "bootstrap-summary.json"
    assert (
        cli_module.main(
            [
                "ci",
                "finalize",
                "--plan",
                str(plan_path),
                "--plan-digest",
                plan_digest,
                *lane_arguments,
                "--started-at",
                "940",
                "--pull-request-number",
                "17",
                "--decision-output",
                str(decision),
                "--summary-output",
                str(summary),
            ]
        )
        == 1
    )
    assert json.loads(decision.read_bytes())["failure-class"] == (
        "incomplete-model-plan"
    )
    return plan_path, plan_digest, decision, summary, tmp_path / "repo"


def _bootstrap_projection_arguments(
    *,
    records: tuple[Path, str, Path, Path, Path],
    github_summary: Path,
    base_sha: str = "1" * 40,
) -> list[str]:
    plan, plan_digest, decision, summary, repository = records
    return [
        "ci",
        "project-bootstrap-shadow",
        "--repo-root",
        str(repository),
        "--plan",
        str(plan),
        "--plan-digest",
        plan_digest,
        "--decision",
        str(decision),
        "--summary",
        str(summary),
        "--pull-request-number",
        "17",
        "--base-sha",
        base_sha,
        "--head-sha",
        "2" * 40,
        "--tested-merge-sha",
        _head(),
        "--github-step-summary",
        str(github_summary),
    ]


def test_catalog_command_emits_exact_static_catalog(
    capsys: pytest.CaptureFixture[str],
) -> None:
    """Emit every approved catalog section and its canonical digest."""
    result = cli_module.main(["catalog"])
    output = json.loads(capsys.readouterr().out)

    assert result == 0
    assert output["schema"] == "workflow-delivery/v3/static-catalog"
    assert set(output["build-definitions"]) == {"node/npm-package-v1"}
    assert set(output["quality-presets"]) == {
        "node/hcoona-release-smoke-npm-v1"
    }
    assert set(output["destination-definitions"]) == {
        "npm/github-packages-hcoona-three-v1",
        "npm/npmjs-public-v1",
    }
    assert output["catalog-digest"].startswith("sha256:")


def test_validate_authoring_command_reports_exact_first_slice(
    tmp_path: Path,
    capsys: pytest.CaptureFixture[str],
) -> None:
    """Validate the target descriptor, Quality selection, and policy."""
    repo, target = _target_authoring_repo(tmp_path)

    result = cli_module.main(
        [
            "repository",
            "validate-authoring",
            "--repo-root",
            str(repo),
            "--target",
            target,
        ]
    )
    captured = capsys.readouterr()
    output = json.loads(captured.out)

    assert result == 0
    assert captured.err == ""
    assert output["result"] == "valid"
    assert output["target"] == target
    assert output["release-unit"] == "hcoona-release-smoke-npm"
    assert output["build-definitions"] == ["node/npm-package-v1"]
    assert output["quality-presets"] == {
        "node": "node/hcoona-release-smoke-npm-v1"
    }
    assert output["release-policy-path"] == (
        "eng/workflow-delivery/v3/policies/hcoona-release-smoke-npm.yml"
    )
    assert output["governance"] == {
        "repository": "hcoona/three",
        "ref": "refs/heads/main",
        "path": (
            ".github/workflow-delivery/governance/hcoona-release-smoke-npm.json"
        ),
        "max-age-days": 90,
    }


def test_validate_authoring_command_reads_target_not_dirty_worktree(
    tmp_path: Path,
    capsys: pytest.CaptureFixture[str],
) -> None:
    """Bind CLI authoring validation to the target tree, not local edits."""
    repo, target = _target_authoring_repo(tmp_path)
    quality_path = repo / PRODUCT_PATH / "workflow-delivery.quality.yml"
    _write(
        quality_path,
        quality_path.read_text(encoding="utf-8").replace(
            "node/hcoona-release-smoke-npm-v1",
            "node/dirty-worktree-v1",
        ),
    )

    result = cli_module.main(
        [
            "repository",
            "validate-authoring",
            "--repo-root",
            str(repo),
            "--target",
            target,
        ]
    )
    captured = capsys.readouterr()
    output = json.loads(captured.out)

    assert result == 0
    assert captured.err == ""
    assert output["target"] == target
    assert output["quality-presets"] == {
        "node": "node/hcoona-release-smoke-npm-v1"
    }
    assert "node/dirty-worktree-v1" in quality_path.read_text(encoding="utf-8")


def test_validate_authoring_command_requires_target() -> None:
    """Require callers to name the exact Git tree being validated."""
    with pytest.raises(SystemExit) as error:
        cli_module.main(
            [
                "repository",
                "validate-authoring",
                "--repo-root",
                str(REPO_ROOT),
            ]
        )

    assert error.value.code == ARGPARSE_ERROR


def test_repository_compile_command_emits_bound_snapshot(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
) -> None:
    """Thread the target through Provider binding and model compilation."""
    repo, target = _target_authoring_repo(tmp_path)

    def provide_fake_node_repository_facts(
        repo_root: Path,
        project_path: str,
        binding: ProviderBinding,
        materialization: object,
    ) -> NodeProviderResult:
        assert repo_root == repo
        assert project_path == PRODUCT_PATH
        assert materialization is not None
        return _fake_provider_result(binding)

    monkeypatch.setattr(
        cli_module,
        "provide_node_repository_facts",
        provide_fake_node_repository_facts,
    )

    result = cli_module.main(
        [
            "repository",
            "compile",
            *_provider_arguments(
                repo_root=repo,
                target=target,
                include_transport=True,
            ),
        ]
    )
    captured = capsys.readouterr()
    output = json.loads(captured.out)

    assert result == 0
    assert captured.err == ""
    assert output["schema"] == (
        "workflow-delivery/v3/repository-model-snapshot"
    )
    assert output["context"]["request-id"] == "release-request-cli"
    assert output["context"]["purpose"] == "live-release"
    assert output["context"]["workflow-run-id"] == WORKFLOW_RUN_ID
    assert "run-attempt" not in output["context"]
    assert output["context"]["target"] == target
    assert output["nbgv"]["canonical"]["gitCommitId"] == target
    assert output["nbgv"]["native"]["npmPackageVersion"]
    assert output["release-units"][0]["release-unit"] == (
        "hcoona-release-smoke-npm"
    )
    assert output["ready"] is True
    assert output["snapshot-digest"].startswith("sha256:")


def test_provider_command_propagates_fail_closed_checkout_error(
    capsys: pytest.CaptureFixture[str],
) -> None:
    """Reject incomplete checkout materialization without JSON output."""
    target = _head()
    result = cli_module.main(
        [
            "repository",
            "provide-node",
            *_provider_arguments(
                repo_root=REPO_ROOT,
                target=target,
                fetch_depth=1,
            ),
        ]
    )
    captured = capsys.readouterr()

    assert result == 1
    assert captured.out == ""
    assert "checkout must use fetch-depth 0" in captured.err
    assert "npmPackageVersion" not in captured.err


def test_validate_attestation_command_reports_replacement_disabled_governance(
    capsys: pytest.CaptureFixture[str],
) -> None:
    """Validate protected replacement Governance without activation state."""
    governance_path = REPO_ROOT / (
        ".github/workflow-delivery/governance/hcoona-release-smoke-npm.json"
    )
    result = cli_module.main(
        [
            "release",
            "validate-attestation",
            "--document",
            str(governance_path),
        ]
    )
    captured = capsys.readouterr()
    output = json.loads(captured.out)

    assert result == 0
    assert captured.err == ""
    assert output["schema"] == (
        "workflow-delivery/v3/normal-live-governance-attestation-v1"
    )
    assert output["live_enabled"] is False
    assert output["activation"] == {
        "state": "blocked",
        "blockers": [
            "destination-primitive-unproven",
            "fresh-native-evidence-required",
            "repository-retention-readback-required",
        ],
    }
    assert output["release_policy"] == "hcoona-release-smoke-npm"
    assert output["package"] == "@hcoona/hcoona-release-smoke-npm"
    assert output["accepted_publisher"] == "hcoona"
    assert output["content-digest"] == (
        f"sha256:{hashlib.sha256(governance_path.read_bytes()).hexdigest()}"
    )


def test_compile_simulation_model_consumes_uploaded_provider_without_rerun(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Compile the admitted simulation model from uploaded Provider facts."""
    repo, target = _target_authoring_repo(tmp_path)
    intent = normalize_official_simulation_intent(
        repository="hcoona/three",
        selected_ref="refs/heads/feature/release",
        target=target,
        actor="release-operator",
        workflow_run_id=WORKFLOW_RUN_ID,
    )
    intent_path = _write_canonical(
        tmp_path / "release-intent.json",
        intent.to_document(),
    )
    context = CompilationContext(
        request_id=intent.request_id,
        purpose="release-simulation",
        workflow_run_id=WORKFLOW_RUN_ID,
        run_attempt=RUN_ATTEMPT,
        target=target,
        producer=OFFICIAL_SIMULATION_PRODUCER,
        control=f"workflow-delivery-v3:{target}",
        catalog_digest=cli_module.catalog_digest(),
        channel="official",
        release_unit="hcoona-release-smoke-npm",
    )
    manifest = first_slice_provider_manifest(
        context,
        provider_producer="discover-node",
    )
    provider = _fake_provider_result(
        provider_binding(manifest, "node-first-slice")
    )
    provider_document = provider.to_document()
    provider_document["provider-request-manifest-digest"] = (
        manifest.manifest_digest
    )
    provider_document["result-digest"] = provider.result_digest
    provider_path = _write_canonical(
        tmp_path / "provider-result.json",
        provider_document,
    )
    output = tmp_path / "repository-model.json"

    def reject_provider_rerun(*_arguments: object, **_keywords: object) -> None:
        message = "Provider must not rerun during compilation"
        raise AssertionError(message)

    monkeypatch.setattr(
        cli_module,
        "provide_node_repository_facts",
        reject_provider_rerun,
    )
    result = cli_module.main(
        [
            "release",
            "compile-simulation-model",
            "--repo-root",
            str(repo),
            "--workflow-run-id",
            str(WORKFLOW_RUN_ID),
            "--run-attempt",
            str(RUN_ATTEMPT),
            "--target",
            target,
            "--intent",
            str(intent_path),
            "--intent-digest",
            intent.intent_digest,
            "--intent-artifact-id",
            "101",
            "--intent-artifact-digest",
            f"sha256:{hashlib.sha256(intent_path.read_bytes()).hexdigest()}",
            "--provider-result",
            str(provider_path),
            "--provider-artifact-id",
            "102",
            "--provider-artifact-digest",
            f"sha256:{hashlib.sha256(provider_path.read_bytes()).hexdigest()}",
            "--output",
            str(output),
        ]
    )
    document: JsonValue = json.loads(output.read_bytes())
    assert isinstance(document, dict)
    admitted = admit_repository_model_snapshot(
        output.read_bytes(),
        expected_context=context,
        expected_digest=canonical_sha256(document),
    )

    assert result == 0
    assert admitted.snapshot.ready is True
    assert admitted.snapshot.context == context


@pytest.mark.parametrize(
    "arguments",
    [
        ["release", "publish"],
        ["release", "discover-execution-history"],
        ["release", "admit-history"],
        ["repository", "plan"],
        ["npm", "observe"],
    ],
    ids=[
        "publish",
        "discover-execution-history",
        "admit-history",
        "repository-plan",
        "observation",
    ],
)
def test_cli_rejects_unapproved_commands(arguments: list[str]) -> None:
    """Expose no publication or unapproved release command."""
    with pytest.raises(SystemExit) as error:
        cli_module.main(arguments)

    assert error.value.code == ARGPARSE_ERROR


@pytest.mark.parametrize(
    "command",
    [
        "normalize-simulation-request",
        "admit-intent",
        "compile-simulation-model",
        "create-simulation-identity",
        "plan-qualification",
        "run-build",
        "form-uploaded-artifact",
        "run-project-test",
        "run-artifact-contents",
        "run-install-import",
        "form-incomplete-evidence",
        "finalize-qualification",
        "observe-npmjs",
        "materialize-hypothetical-actions",
        "finalize-simulation",
    ],
)
def test_cli_exposes_only_the_commit7_release_transport_commands(
    command: str,
) -> None:
    """Expose the commit-7 Release surface while later commands stay absent."""
    with pytest.raises(SystemExit) as error:
        cli_module.main(["release", command, "--help"])

    assert error.value.code == 0


@pytest.mark.parametrize(
    ("command", "required_options"),
    [
        (
            "admit-live-eligibility",
            (
                "--intent",
                "--repository-model",
                "--live-eligibility-decision",
                "--output",
            ),
        ),
        (
            "materialize-publication",
            (
                "--selected-ref",
                "--intent",
                "--intent-digest",
                "--intent-artifact-id",
                "--intent-artifact-digest",
                "--output",
                "--summary-output",
                "--github-output",
            ),
        ),
        (
            "form-approval-bundle",
            (
                "--attempt-binding",
                "--qualification-decision",
                "--publication-snapshot",
                "--publication-snapshot-artifact-url",
                "--publication-snapshot-payload-path",
                "--reviewer-summary",
                "--reviewer-summary-digest",
                "--reviewer-summary-artifact-id",
                "--reviewer-summary-artifact-digest",
                "--reviewer-summary-artifact-url",
                "--reviewer-summary-payload-path",
                "--output",
            ),
        ),
        (
            "form-publication-authorization",
            (
                "--intent",
                "--repository-model",
                "--attempt-binding",
                "--attempt-binding-digest",
                "--attempt-binding-artifact-id",
                "--attempt-binding-artifact-digest",
                "--approval-bundle",
                "--live-eligibility-decision",
                "--output",
            ),
        ),
        (
            "prove-exact-satisfied",
            (
                "--intent",
                "--repository-model",
                "--attempt-binding",
                "--publication-snapshot",
                "--live-eligibility-decision",
                "--output",
            ),
        ),
        (
            "preflight-github-packages",
            (
                "--publication-snapshot",
                "--approval-bundle",
                "--reviewer-summary",
                "--publication-authorization",
                "--qualification-snapshot",
            ),
        ),
        (
            "mark-github-packages-mutation-start",
            ("--preflight", "--marker-output", "--publication-snapshot"),
        ),
        (
            "publish-github-packages",
            (
                "--reviewer-summary",
                "--publication-authorization",
                "--mutation-marker-artifact-id",
                "--execution-state-output",
            ),
        ),
        (
            "form-github-packages-result",
            (
                "--execution-state",
                "--mutation-marker-artifact-id",
                "--result-output",
            ),
        ),
        (
            "finalize-live",
            (
                "--attempt-binding",
                "--qualification-snapshot",
                "--qualification-decision",
                "--build-evidence",
                "--release-artifact",
                "--observation",
                "--publication-snapshot",
                "--publication-snapshot-artifact-url",
                "--publication-snapshot-payload-path",
                "--approval-bundle",
                "--approval-bundle-artifact-url",
                "--approval-bundle-payload-path",
                "--publication-authorization",
                "--exact-satisfied-governance-proof",
                "--action-result",
                "--publication-preparation-interrupted",
                "--outcome-output",
                "--summary-output",
                "--github-output",
            ),
        ),
    ],
)
def test_cli_exposes_strict_commit8_live_transport_commands(
    command: str,
    required_options: tuple[str, ...],
    capsys: pytest.CaptureFixture[str],
) -> None:
    """Expose only canonical file/artifact-bound commit-8 live commands."""
    with pytest.raises(SystemExit) as error:
        cli_module.main(["release", command, "--help"])

    assert error.value.code == 0
    help_text = capsys.readouterr().out
    assert all(option in help_text for option in required_options)


@pytest.mark.parametrize(
    ("command", "removed_option"),
    [
        ("form-publication-authorization", "--authorized-at"),
        ("prove-exact-satisfied", "--proved-at"),
    ],
)
def test_cli_authority_completion_timestamps_are_internal(
    command: str,
    removed_option: str,
    capsys: pytest.CaptureFixture[str],
) -> None:
    """Do not accept caller-supplied authority completion timestamps."""
    with pytest.raises(SystemExit) as error:
        cli_module.main(["release", command, "--help"])

    assert error.value.code == 0
    assert removed_option not in capsys.readouterr().out


@pytest.mark.parametrize(
    "removed_option",
    [
        "--repo-root",
        "--tarball",
        "--github-token",
        "--preflight-output",
        "--github-output",
    ],
)
def test_cli_unsupported_preflight_omits_unused_capability_inputs(
    removed_option: str,
    capsys: pytest.CaptureFixture[str],
) -> None:
    """Keep the disabled preflight free of unused capability inputs."""
    with pytest.raises(SystemExit) as error:
        cli_module.main(["release", "preflight-github-packages", "--help"])

    assert error.value.code == 0
    assert removed_option not in capsys.readouterr().out


def test_cli_commit8_live_outcome_status_mapping_is_closed() -> None:
    """Pin success versus every fail-closed terminal CLI status."""
    assert getattr(cli_module, "LIVE_OUTCOME_EXIT_STATUS", None) == {
        "success": 0,
        "failure": 1,
        "incomplete": 1,
        "replayable-no-side-effect": 1,
        "incomplete-possibly-mutated": 1,
    }


def test_cli_live_github_packages_paths_select_manual_redirect_transport() -> (
    None
):
    """Select the credential-safe redirect transport for both live paths."""
    source = inspect.getsource(cli_module)

    assert "_UrlopenGitHubPackagesTransport" not in source
    assert (
        source.count("transport=GitHubPackagesHttpTransport()")
        == LIVE_GITHUB_PACKAGES_TRANSPORT_SELECTIONS
    )


def test_cli_commit8_finalizer_exposes_platform_and_status_evidence_contract(
    capsys: pytest.CaptureFixture[str],
) -> None:
    """Expose explicit platform facts and both retained final status outputs."""
    with pytest.raises(SystemExit) as error:
        cli_module.main(["release", "finalize-live", "--help"])

    assert error.value.code == 0
    help_text = capsys.readouterr().out
    for option in (
        "--platform-terminated",
        "--publication-may-have-started",
        "--outcome-output",
        "--summary-output",
        "--github-step-summary",
        "--github-output",
    ):
        assert option in help_text
    assert "--capability-group-bundle" not in help_text
    assert "--receipt " not in help_text


def test_project_registers_only_the_bounded_cli() -> None:
    """Register the approved package entry point without a second tool."""
    pyproject = tomllib.loads(
        (PACKAGE_ROOT / "pyproject.toml").read_text(encoding="utf-8")
    )

    assert pyproject["project"]["scripts"] == {
        "three-workflow-delivery-v3": "three_workflow_delivery_v3.cli:main"
    }
    assert set(pyproject["project"]["dependencies"]) == {
        "PyYAML>=6.0.2",
        "rfc8785>=0.1.4",
    }


def test_ci_candidate_cli_binds_tested_merge_and_exact_range(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Use the event base/head only for paths and the tested merge as target."""
    base = "1" * 40
    head = "2" * 40
    merge = "3" * 40
    observed: tuple[str, ...] = ()

    def run(command: tuple[str, ...], **kwargs: object) -> object:
        nonlocal observed
        observed = command
        assert kwargs["check"] is True
        return subprocess.CompletedProcess(
            command,
            0,
            stdout='["README.md"]\n',
            stderr="",
        )

    monkeypatch.setattr(cli_module.subprocess, "run", run)
    output = tmp_path / "request.json"
    code = cli_module.main(
        [
            "ci",
            "candidate",
            "--event-kind",
            "pull_request",
            "--repository",
            "hcoona/three",
            "--request-id",
            "pr-17",
            "--workflow-run-id",
            str(WORKFLOW_RUN_ID),
            "--run-attempt",
            str(RUN_ATTEMPT),
            "--selected-ref",
            "refs/pull/17/merge",
            "--base-sha",
            base,
            "--head-sha",
            head,
            "--target",
            merge,
            "--output",
            str(output),
        ]
    )
    document = json.loads(output.read_bytes())

    assert code == 0
    assert observed[-4:] == ("--from-ref", base, "--to-ref", head)
    assert document["candidate"]["base-sha"] == base
    assert document["candidate"]["head-sha"] == head
    assert document["candidate"]["target"] == merge
    assert document["candidate"]["tested-merge-sha"] == merge
    assert document["changed-paths"] == ["README.md"]


def test_ci_candidate_cli_forms_scope_less_manual_request(
    tmp_path: Path,
) -> None:
    """Manual slice validation names only the selected target."""
    target = "4" * 40
    output = tmp_path / "manual-request.json"
    code = cli_module.main(
        [
            "ci",
            "candidate",
            "--event-kind",
            "workflow_dispatch",
            "--repository",
            "hcoona/three",
            "--request-id",
            "slice-17",
            "--workflow-run-id",
            str(WORKFLOW_RUN_ID),
            "--run-attempt",
            str(RUN_ATTEMPT),
            "--selected-ref",
            "refs/heads/main",
            "--target",
            target,
            "--output",
            str(output),
        ]
    )
    document = json.loads(output.read_bytes())

    assert code == 0
    assert document["candidate"]["purpose"] == "slice-validation"
    assert document["candidate"]["target"] == target
    assert document["candidate"]["base-sha"] is None
    assert document["changed-paths"] == []


def test_ci_payload_admission_requires_upload_digest_and_canonical_bytes(
    tmp_path: Path,
    capsys: pytest.CaptureFixture[str],
) -> None:
    """Bind a raw stock artifact payload to the upload action digest."""
    payload = _write_canonical(tmp_path / "payload.json", {"value": "exact"})
    digest = hashlib.sha256(payload.read_bytes()).hexdigest()

    assert (
        cli_module.main(
            [
                "ci",
                "admit-payload",
                "--input",
                str(payload),
                "--expected-digest",
                digest,
            ]
        )
        == 0
    )
    assert (
        cli_module.main(
            [
                "ci",
                "admit-payload",
                "--input",
                str(payload),
                "--expected-digest",
                "0" * 64,
            ]
        )
        == 1
    )
    assert "does not match upload output" in capsys.readouterr().err


def test_uploaded_payload_reference_rejects_reviewer_byte_substitution(
    tmp_path: Path,
) -> None:
    """Bind a raw reviewer reference only to its exact local bytes."""
    reviewer = tmp_path / "reviewer-summary.md"
    reviewer.write_bytes(b"substituted reviewer summary")
    expected_digest = (
        "sha256:" + hashlib.sha256(b"approved reviewer summary").hexdigest()
    )

    with pytest.raises(
        ValueError,
        match=r"^reviewer_summary payload digest mismatch$",
    ):
        cli_module._uploaded_payload_reference(  # noqa: SLF001
            Namespace(
                reviewer_summary=str(reviewer),
                reviewer_summary_digest=expected_digest,
                reviewer_summary_artifact_id=712,
                reviewer_summary_artifact_digest="sha256:" + ("3" * 64),
                reviewer_summary_artifact_url=(
                    "https://example.test/artifacts/712"
                ),
                reviewer_summary_payload_path="reviewer-summary.md",
            ),
            name="reviewer_summary",
        )


@pytest.mark.parametrize(
    ("event_kind", "changed_paths", "lane_selected"),
    [
        ("pull_request", ("README.md",), (True, False, False, False)),
        ("workflow_dispatch", (), (True, True, True, True)),
    ],
)
def test_ci_plan_cli_closes_repository_only_and_manual_scope(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    event_kind: str,
    changed_paths: tuple[str, ...],
    lane_selected: tuple[bool, bool, bool, bool],
) -> None:
    """Preserve empty affected lanes and complete manual slice scope."""
    plan_path, context_path, plan = _plan_fixture(
        tmp_path,
        monkeypatch,
        event_kind=event_kind,
        changed_paths=changed_paths,
    )

    assert plan_path.read_bytes() == canonicalize(plan)
    assert context_path.is_file()
    obligations = cast("list[dict[str, JsonValue]]", plan["obligations"])
    assert tuple(item["selected"] for item in obligations) == lane_selected
    selected_by_lane = dict(
        zip(cli_module.CI_LANE_IDS, lane_selected, strict=True)
    )
    assert (tmp_path / f"{event_kind}-github-output").read_text(
        encoding="utf-8"
    ) == (
        f"plan-digest={canonical_sha256(plan)}\n"
        "plan-ready=true\n"
        f"root-hk-selected={str(selected_by_lane['root-hk']).lower()}\n"
        "project-build-selected="
        f"{str(selected_by_lane['project-build']).lower()}\n"
        "project-test-selected="
        f"{str(selected_by_lane['project-test']).lower()}\n"
        "npm-artifact-build-selected="
        f"{str(selected_by_lane['npm-artifact-build']).lower()}\n"
    )
    assert plan["candidate"]["purpose"] == (  # type: ignore[index]
        "ci-pr-slice-shadow"
        if event_kind == "pull_request"
        else "slice-validation"
    )


@pytest.mark.parametrize(
    ("missing_authoring", "diagnostic"),
    [
        ("descriptor", "first-slice Release Unit descriptor is missing"),
        (
            "quality",
            (
                "Quality selection does not exist: "
                f"{PRODUCT_PATH}/workflow-delivery.quality.yml"
            ),
        ),
        (
            "policy",
            f"Release policy does not exist: {FIRST_SLICE_POLICY_PATH}",
        ),
    ],
)
def test_missing_target_authoring_closes_blocked_plan_and_decision(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    missing_authoring: str,
    diagnostic: str,
) -> None:
    """Carry semantic model incompleteness through the complete CLI contract."""
    plan_path, context_path, plan = _plan_fixture(
        tmp_path,
        monkeypatch,
        event_kind="workflow_dispatch",
        missing_authoring=missing_authoring,
    )
    plan_digest = canonical_sha256(plan)

    assert plan["ready"] is False
    assert not context_path.exists()
    assert plan["selected-project-nodes"] == []
    assert plan["selected-release-units"] == []
    assert plan["selected-variants"] == []
    assert plan["selected-outputs"] == []
    assert plan["expected-evidence-ids"] == []
    assert all(
        item["selected"] is False
        for item in cast(
            "list[dict[str, JsonValue]]",
            plan["obligations"],
        )
    )
    assert diagnostic in " ".join(
        cast("list[str]", plan["diagnostics"]),
    )
    assert (tmp_path / "workflow_dispatch-github-output").read_text(
        encoding="utf-8"
    ) == (
        f"plan-digest={plan_digest}\n"
        "plan-ready=false\n"
        "root-hk-selected=false\n"
        "project-build-selected=false\n"
        "project-test-selected=false\n"
        "npm-artifact-build-selected=false\n"
    )

    results: list[str] = []
    for lane in cli_module.CI_LANE_IDS:
        result = tmp_path / f"{lane}-blocked-result.json"
        assert (
            cli_module.main(
                [
                    "ci",
                    "lane-result",
                    "--plan",
                    str(plan_path),
                    "--plan-digest",
                    plan_digest,
                    "--lane-id",
                    lane,
                    "--output",
                    str(result),
                ]
            )
            == 0
        )
        result_document = json.loads(result.read_bytes())
        assert result_document["disposition"] == "empty"
        assert result_document["evidence"] is None
        results.extend(["--lane-result", str(result)])

    decision_path = tmp_path / "blocked-decision.json"
    summary_path = tmp_path / "blocked-summary.json"
    assert (
        cli_module.main(
            [
                "ci",
                "finalize",
                "--plan",
                str(plan_path),
                "--plan-digest",
                plan_digest,
                *results,
                "--started-at",
                "970",
                "--decision-output",
                str(decision_path),
                "--summary-output",
                str(summary_path),
            ]
        )
        == 1
    )
    decision = json.loads(decision_path.read_bytes())
    summary = json.loads(summary_path.read_bytes())
    assert decision_path.read_bytes() == canonicalize(decision)
    assert summary_path.read_bytes() == canonicalize(summary)
    assert decision["terminal-result"] == "failure"
    assert decision["failure-class"] == "incomplete-model-plan"
    assert decision["next-action"] == "fix-model-plan-and-rerun"
    assert all(
        item["outcome"] == "empty"
        for item in decision["obligation-dispositions"]
    )
    assert decision["summary"] == summary
    assert "Plan was not ready" in summary["text"]


def test_ci_bootstrap_projection_admits_records_without_rewriting_them(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Append only an explicit note after exact canonical re-admission."""
    records = _blocked_pr_decision_fixture(tmp_path, monkeypatch)
    _, _, decision, summary, repository = records
    decision_document = cast(
        "dict[str, JsonValue]",
        json.loads(decision.read_bytes()),
    )
    candidate = cast(
        "dict[str, JsonValue]",
        decision_document["candidate"],
    )
    before = (decision.read_bytes(), summary.read_bytes())
    observed: list[tuple[Path, str, str]] = []

    def contains_path(root: Path, commit: str, path: str) -> bool:
        observed.append((root, commit, path))
        return False

    monkeypatch.setattr(
        cli_module,
        "_git_commit_contains_path",
        contains_path,
    )
    github_summary = tmp_path / "github-summary.md"
    arguments = _bootstrap_projection_arguments(
        records=records,
        github_summary=github_summary,
        base_sha=cast("str", candidate["base-sha"]),
    )
    tested_merge_index = arguments.index("--tested-merge-sha") + 1
    arguments[tested_merge_index] = cast("str", candidate["tested-merge-sha"])

    assert cli_module.main(arguments) == 0
    assert observed == [
        (
            repository,
            cast("str", candidate["base-sha"]),
            CI_WORKFLOW_PATH,
        )
    ]
    assert (decision.read_bytes(), summary.read_bytes()) == before
    note = github_summary.read_text(encoding="utf-8")
    assert "Pre-coexistence bootstrap projection" in note
    assert "canonical Decision remains failure" in note
    assert CI_WORKFLOW_PATH in note


@pytest.mark.parametrize(
    "mutation",
    [
        "marker-present",
        "identity-drift",
        "decision-missing",
        "decision-noncanonical",
        "plan-mismatch",
        "summary-mismatch",
    ],
)
def test_ci_bootstrap_projection_rejects_inexact_inputs(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
    mutation: str,
) -> None:
    """Keep marker, identity, and canonical record failures red."""
    records = _blocked_pr_decision_fixture(tmp_path, monkeypatch)
    _, _, decision, summary, repository = records
    document = cast("dict[str, JsonValue]", json.loads(decision.read_bytes()))
    candidate = cast("dict[str, JsonValue]", document["candidate"])
    monkeypatch.setattr(
        cli_module,
        "_git_commit_contains_path",
        lambda *_: mutation == "marker-present",
    )
    if mutation == "decision-missing":
        decision.unlink()
    elif mutation == "decision-noncanonical":
        decision.write_text(
            json.dumps(document, indent=2),
            encoding="utf-8",
        )
    elif mutation == "plan-mismatch":
        plan_document = cast(
            "dict[str, JsonValue]",
            json.loads(records[0].read_bytes()),
        )
        plan_document["diagnostics"] = [
            "changed path is unclassified: unmodeled/other.txt"
        ]
        alternate_plan = _write_canonical(
            tmp_path / "mismatched-plan.json",
            plan_document,
        )
        records = (
            alternate_plan,
            canonical_sha256(plan_document),
            decision,
            summary,
            repository,
        )
    elif mutation == "summary-mismatch":
        summary_document = cast(
            "dict[str, JsonValue]",
            json.loads(summary.read_bytes()),
        )
        summary_document["text"] = "non-authoritative but mismatched"
        summary.write_bytes(canonicalize(summary_document))
    github_summary = tmp_path / f"{mutation}-summary.md"
    arguments = _bootstrap_projection_arguments(
        records=records,
        github_summary=github_summary,
        base_sha=(
            "f" * 40
            if mutation == "identity-drift"
            else cast("str", candidate["base-sha"])
        ),
    )
    tested_merge_index = arguments.index("--tested-merge-sha") + 1
    arguments[tested_merge_index] = cast("str", candidate["tested-merge-sha"])

    assert cli_module.main(arguments) == 1
    assert not github_summary.exists()
    expected_error = {
        "marker-present": "not eligible",
        "identity-drift": "not eligible",
        "decision-missing": str(decision),
        "decision-noncanonical": "not canonical",
        "plan-mismatch": "trusted Plan digest",
        "summary-mismatch": "Summary does not match",
    }[mutation]
    assert expected_error in capsys.readouterr().err


def test_git_commit_path_probe_uses_exact_base_tree(tmp_path: Path) -> None:
    """Distinguish marker absence, presence, and a nonexistent base commit."""
    repository = tmp_path / "marker-repository"
    repository.mkdir()
    _initialize_repository(repository)
    _write(repository / "README.md", "base\n")
    base = _commit_all(repository)
    _write(repository / CI_WORKFLOW_PATH, "name: v3\n")
    head = _commit_all(repository)

    assert not cli_module._git_commit_contains_path(  # noqa: SLF001
        repository,
        base,
        CI_WORKFLOW_PATH,
    )
    assert cli_module._git_commit_contains_path(  # noqa: SLF001
        repository,
        head,
        CI_WORKFLOW_PATH,
    )
    with pytest.raises(subprocess.CalledProcessError):
        cli_module._git_commit_contains_path(  # noqa: SLF001
            repository,
            "f" * 40,
            CI_WORKFLOW_PATH,
        )
    with pytest.raises(ValueError, match="lowercase 40-hex"):
        cli_module._git_commit_contains_path(  # noqa: SLF001
            repository,
            "HEAD",
            CI_WORKFLOW_PATH,
        )
    with pytest.raises(ValueError, match="marker path is not canonical"):
        cli_module._git_commit_contains_path(  # noqa: SLF001
            repository,
            base,
            "README.md",
        )


def test_malformed_target_authoring_remains_hard_failure(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
) -> None:
    """Reject malformed authoring without emitting a Plan-bound record."""
    plan_path, context_path, plan = _plan_fixture(
        tmp_path,
        monkeypatch,
        event_kind="workflow_dispatch",
        malformed_authoring="quality",
        expect_failure=True,
    )

    assert plan == {}
    assert not plan_path.exists()
    assert not context_path.exists()
    assert "malformed YAML authoring" in capsys.readouterr().err


def test_npm_node_adapter_emits_content_facts_without_platform_metadata(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Keep platform artifact identity out of the mechanical BuildResult."""
    plan_path, context_path, plan = _plan_fixture(
        tmp_path,
        monkeypatch,
        event_kind="workflow_dispatch",
    )
    tarball = tmp_path / "package.tgz"
    result = tmp_path / "npm-adapter.json"
    monkeypatch.setattr(
        cli_module,
        "build_node_package",
        lambda _request: SimpleNamespace(
            tarball=b"exact-tarball",
            manifest=SimpleNamespace(
                basename="hcoona-hcoona-release-smoke-npm-1.2.3.tgz",
                entries=(
                    "package/README.md",
                    "package/dist/index.js",
                    "package/package.json",
                    "package/workflow-delivery/provenance.json",
                ),
                lifecycle_scripts=(("test", "node --test"),),
                sha256="sha256:" + hashlib.sha256(b"exact-tarball").hexdigest(),
                sha512="sha512:" + hashlib.sha512(b"exact-tarball").hexdigest(),
                byte_size=len(b"exact-tarball"),
            ),
            witness=b"canonical-witness",
            source_input_manifest=(("package.json", "sha256:" + ("1" * 64)),),
        ),
    )

    assert (
        cli_module.main(
            [
                "ci",
                "node-adapter",
                "--lane-id",
                "npm-artifact-build",
                "--plan",
                str(plan_path),
                "--plan-digest",
                canonical_sha256(plan),
                "--adapter-context",
                str(context_path),
                "--repository-root",
                str(REPO_ROOT),
                "--tarball-output",
                str(tarball),
                "--output",
                str(result),
            ]
        )
        == 0
    )
    document = json.loads(result.read_bytes())
    assert tarball.read_bytes() == b"exact-tarball"
    assert document["artifact"]["tarball-basename"].endswith(".tgz")
    assert document["artifact"]["entries"][-1] == (
        "package/workflow-delivery/provenance.json"
    )
    assert document["artifact"]["lifecycle-scripts"] == [
        ["test", "node --test"]
    ]
    assert "artifact-id" not in document["artifact"]
    assert "artifact-name" not in document["artifact"]
    assert "transport-digest" not in document["artifact"]
    assert "artifact-digests" not in document

    lane_result = tmp_path / "npm-lane-result.json"
    assert (
        cli_module.main(
            [
                "ci",
                "lane-result",
                "--plan",
                str(plan_path),
                "--plan-digest",
                canonical_sha256(plan),
                "--lane-id",
                "npm-artifact-build",
                "--mechanical-result",
                str(result),
                "--artifact-id",
                str(NPM_ARTIFACT_ID),
                "--artifact-name",
                f"wdv3-{WORKFLOW_RUN_ID}-{RUN_ATTEMPT}-npm-tarball.tgz",
                "--artifact-url",
                (
                    "https://github.com/hcoona/three/actions/runs/"
                    f"{WORKFLOW_RUN_ID}/artifacts/{NPM_ARTIFACT_ID}"
                ),
                "--artifact-digest",
                hashlib.sha256(b"exact-tarball").hexdigest(),
                "--output",
                str(lane_result),
            ]
        )
        == 0
    )
    artifact = json.loads(lane_result.read_bytes())["evidence"]["artifacts"][0]
    assert (
        artifact["output-id"],
        artifact["logical-role"],
        artifact["media-kind"],
    ) == ("npm-tarball", "primary-package", "npm-tarball")


def test_project_test_failure_forms_failed_evidence_and_fails_finalizer(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Retain a completed test failure as failed Evidence, not missing work."""
    plan_path, context_path, plan = _plan_fixture(
        tmp_path,
        monkeypatch,
        event_kind="workflow_dispatch",
    )
    plan_digest = ci_qualification_snapshot_digest(
        cli_module._load_ci_plan(str(plan_path), canonical_sha256(plan))  # noqa: SLF001
    )

    def fail_tests(project_root: Path, request: object) -> None:
        del project_root, request
        raise subprocess.CalledProcessError(1, ("npm", "test"))

    monkeypatch.setattr(cli_module, "run_node_project_tests", fail_tests)
    adapter = tmp_path / "project-test-adapter.json"
    assert (
        cli_module.main(
            [
                "ci",
                "node-adapter",
                "--lane-id",
                "project-test",
                "--plan",
                str(plan_path),
                "--plan-digest",
                plan_digest,
                "--adapter-context",
                str(context_path),
                "--repository-root",
                str(REPO_ROOT),
                "--output",
                str(adapter),
            ]
        )
        == 1
    )
    assert json.loads(adapter.read_bytes())["outcome"] == "failure"

    results: list[str] = []
    for lane in cli_module.CI_LANE_IDS:
        output = tmp_path / f"{lane}-result.json"
        arguments = [
            "ci",
            "lane-result",
            "--plan",
            str(plan_path),
            "--plan-digest",
            plan_digest,
            "--lane-id",
            lane,
            "--output",
            str(output),
        ]
        if lane == "root-hk":
            arguments.extend(["--outcome", "success"])
        else:
            mechanical = (
                adapter
                if lane == "project-test"
                else _mechanical_result(
                    tmp_path / f"{lane}-mechanical.json",
                    plan,
                    lane,
                    "success",
                )
            )
            arguments.extend(["--mechanical-result", str(mechanical)])
            if lane == "npm-artifact-build":
                arguments.extend(
                    [
                        "--artifact-id",
                        str(NPM_ARTIFACT_ID),
                        "--artifact-name",
                        (
                            f"wdv3-{WORKFLOW_RUN_ID}-{RUN_ATTEMPT}-"
                            "npm-tarball.tgz"
                        ),
                        "--artifact-url",
                        (
                            "https://github.com/hcoona/three/actions/runs/"
                            f"{WORKFLOW_RUN_ID}/artifacts/{NPM_ARTIFACT_ID}"
                        ),
                        "--artifact-digest",
                        NPM_ARTIFACT_DIGEST,
                    ]
                )
        assert cli_module.main(arguments) == 0
        results.extend(["--lane-result", str(output)])

    decision = tmp_path / "decision.json"
    summary = tmp_path / "summary.json"
    code = cli_module.main(
        [
            "ci",
            "finalize",
            "--plan",
            str(plan_path),
            "--plan-digest",
            plan_digest,
            *results,
            "--started-at",
            "880",
            "--decision-output",
            str(decision),
            "--summary-output",
            str(summary),
        ]
    )
    document = json.loads(decision.read_bytes())

    assert code == 1
    assert document["terminal-result"] == "failure"
    assert document["authority"] == "non-authoritative"
    project_test = next(
        item
        for item in document["obligation-dispositions"]
        if item["obligation"]["lane-id"] == "project-test"
    )
    assert project_test["outcome"] == "failed"
    assert json.loads(summary.read_bytes())["authority"] == "non-authoritative"


def test_ci_finalizer_marks_missing_selected_work_incomplete(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Do not turn an absent selected lane result into success."""
    plan_path, _, plan = _plan_fixture(
        tmp_path,
        monkeypatch,
        event_kind="workflow_dispatch",
    )
    plan_digest = canonical_sha256(plan)
    results: list[str] = []
    for lane in ("root-hk", "project-build", "npm-artifact-build"):
        result = tmp_path / f"{lane}.json"
        arguments = [
            "ci",
            "lane-result",
            "--plan",
            str(plan_path),
            "--plan-digest",
            plan_digest,
            "--lane-id",
            lane,
            "--output",
            str(result),
        ]
        if lane == "root-hk":
            arguments.extend(["--outcome", "success"])
        else:
            mechanical = _mechanical_result(
                tmp_path / f"{lane}-mechanical.json",
                plan,
                lane,
                "success",
            )
            arguments.extend(["--mechanical-result", str(mechanical)])
            if lane == "npm-artifact-build":
                arguments.extend(
                    [
                        "--artifact-id",
                        str(NPM_ARTIFACT_ID),
                        "--artifact-name",
                        (
                            f"wdv3-{WORKFLOW_RUN_ID}-{RUN_ATTEMPT}-"
                            "npm-tarball.tgz"
                        ),
                        "--artifact-url",
                        (
                            "https://github.com/hcoona/three/actions/runs/"
                            f"{WORKFLOW_RUN_ID}/artifacts/{NPM_ARTIFACT_ID}"
                        ),
                        "--artifact-digest",
                        NPM_ARTIFACT_DIGEST,
                    ]
                )
        assert cli_module.main(arguments) == 0
        results.extend(["--lane-result", str(result)])

    decision = tmp_path / "incomplete.json"
    code = cli_module.main(
        [
            "ci",
            "finalize",
            "--plan",
            str(plan_path),
            "--plan-digest",
            plan_digest,
            *results,
            "--started-at",
            "990",
            "--decision-output",
            str(decision),
            "--summary-output",
            str(tmp_path / "incomplete-summary.json"),
        ]
    )

    assert code == 1
    assert json.loads(decision.read_bytes())["terminal-result"] == "incomplete"


@pytest.mark.parametrize(
    ("lookup", "supersession_state", "pr_slo"),
    [
        ("current", "not-superseded", "met"),
        ("superseded", "superseded", "excluded"),
        ("failure", "unsupported", "not-applicable"),
    ],
)
def test_ci_finalizer_queries_exact_current_pr_identity(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    lookup: str,
    supersession_state: str,
    pr_slo: str,
) -> None:
    """Use the public PR API and reserve unsupported for lookup failure."""
    plan_path, _, plan = _plan_fixture(
        tmp_path,
        monkeypatch,
        event_kind="pull_request",
        changed_paths=("README.md",),
    )
    plan_digest = canonical_sha256(plan)
    results: list[str] = []
    for lane in cli_module.CI_LANE_IDS:
        result = tmp_path / f"{lane}-pr-result.json"
        arguments = [
            "ci",
            "lane-result",
            "--plan",
            str(plan_path),
            "--plan-digest",
            plan_digest,
            "--lane-id",
            lane,
            "--output",
            str(result),
        ]
        if lane == "root-hk":
            arguments.extend(["--outcome", "success"])
        assert cli_module.main(arguments) == 0
        results.extend(["--lane-result", str(result)])

    candidate = cast("dict[str, JsonValue]", plan["candidate"])
    events: list[str] = []

    def fetch_current_pull_request(**kwargs: object) -> dict[str, JsonValue]:
        events.append("lookup")
        assert kwargs == {
            "api_url": "https://api.github.com",
            "repository": "hcoona/three",
            "pull_request_number": 17,
        }
        if lookup == "failure":
            raise cli_module._GitHubPullRequestLookupError  # noqa: SLF001
        head_sha = cast("str", candidate["head-sha"])
        if lookup == "superseded":
            head_sha = "f" * 40
        return {
            "base": {"sha": candidate["base-sha"]},
            "head": {"sha": head_sha},
            "merge_commit_sha": candidate["tested-merge-sha"],
        }

    monkeypatch.setattr(
        cli_module,
        "_fetch_current_pull_request",
        fetch_current_pull_request,
    )
    monkeypatch.setattr(
        cli_module,
        "_current_epoch_seconds",
        lambda: events.append("clock") or 1000,
    )
    decision = tmp_path / f"{lookup}-decision.json"
    code = cli_module.main(
        [
            "ci",
            "finalize",
            "--plan",
            str(plan_path),
            "--plan-digest",
            plan_digest,
            *results,
            "--started-at",
            "940",
            "--pull-request-number",
            "17",
            "--decision-output",
            str(decision),
            "--summary-output",
            str(tmp_path / f"{lookup}-summary.json"),
        ]
    )
    document = json.loads(decision.read_bytes())

    assert code == 0
    assert events == ["lookup", "clock"]
    assert document["elapsed-seconds"] == EXPECTED_ELAPSED_SECONDS
    assert document["supersession-state"] == supersession_state
    assert document["pr-slo"] == pr_slo


def test_public_pr_lookup_uses_exact_unauthenticated_github_endpoint(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Query only the public repository PR resource with closed headers."""
    observed: dict[str, object] = {}

    class Response:
        def __enter__(self) -> Self:
            return self

        def __exit__(self, *args: object) -> None:
            del args

        def read(self) -> bytes:
            return b'{"base":{"sha":"a"},"head":{"sha":"b"}}'

    def open_request(request: object, *, timeout: int) -> Response:
        observed["request"] = request
        observed["timeout"] = timeout
        return Response()

    monkeypatch.setattr(cli_module, "urlopen", open_request)
    document = cli_module._fetch_current_pull_request(  # noqa: SLF001
        api_url="https://api.github.com",
        repository="hcoona/three",
        pull_request_number=17,
    )
    request = observed["request"]

    assert isinstance(request, Request)
    assert document == {"base": {"sha": "a"}, "head": {"sha": "b"}}
    assert observed["timeout"] == GITHUB_API_TIMEOUT_SECONDS
    assert request.full_url == (
        "https://api.github.com/repos/hcoona/three/pulls/17"
    )
    assert request.get_header("Accept") == ("application/vnd.github+json")
    assert request.get_header("Authorization") is None


def test_ci_finalizer_rejects_pr_number_not_bound_to_plan(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Treat event-to-Plan PR-number drift as a hard binding failure."""
    plan_path, _, plan = _plan_fixture(
        tmp_path,
        monkeypatch,
        event_kind="pull_request",
        changed_paths=("README.md",),
    )
    decision = tmp_path / "wrong-pr-decision.json"

    assert (
        cli_module.main(
            [
                "ci",
                "finalize",
                "--plan",
                str(plan_path),
                "--plan-digest",
                canonical_sha256(plan),
                "--started-at",
                "940",
                "--pull-request-number",
                "18",
                "--decision-output",
                str(decision),
                "--summary-output",
                str(tmp_path / "wrong-pr-summary.json"),
            ]
        )
        == 1
    )
    assert not decision.exists()


def _current_release_attempt_binding(
    *,
    target: str = "b" * 40,
) -> ReleaseAttemptBinding:
    execution = BuddyExecutionIdentity(
        channel="buddy",
        release_unit="hcoona-release-smoke-npm",
        target=target,
    )
    return ReleaseAttemptBinding(
        intent_digest="sha256:" + ("1" * 64),
        request_id="release-request:" + ("2" * 64),
        execution=execution,
        attempt=ReleaseAttemptIdentity(
            execution=execution,
            workflow_run_id=WORKFLOW_RUN_ID,
        ),
        repository_model_digest="sha256:" + ("3" * 64),
        live_eligibility_artifact_id=701,
        live_eligibility_artifact_digest="sha256:" + ("4" * 64),
        live_eligibility_payload_digest="sha256:" + ("5" * 64),
        attestation_provenance=(
            ("blob-oid", "6" * 40),
            ("canonical-content-digest", "sha256:" + ("7" * 64)),
            ("eligibility-main-sha", "8" * 40),
            ("git-object-format", "sha1"),
            ("path", ".github/workflow-delivery/governance/policy.json"),
            ("ref", "refs/heads/main"),
            ("repository", "hcoona/three"),
        ),
    )


def test_form_approval_bundle_command_binds_current_loaded_records(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Bind the complete pre-wait bundle to currently loaded records."""
    base_binding = _current_release_attempt_binding()
    intent = cli_module.normalize_buddy_live_intent(
        repository="hcoona/three",
        selected_ref="refs/heads/release",
        target=base_binding.execution.target,
        actor="hcoona",
        workflow_run_id=base_binding.attempt.workflow_run_id,
    )
    binding = replace(
        base_binding,
        intent_digest=intent.intent_digest,
        request_id=intent.request_id,
    )
    qualification = object()
    publication = object()
    publication_reference = ArtifactReference(
        artifact_id=711,
        artifact_digest="sha256:" + ("1" * 64),
        artifact_url="https://example.test/artifacts/711",
        payload_path="publication-snapshot.json",
        payload_digest="sha256:" + ("2" * 64),
    )
    reviewer_reference = ArtifactReference(
        artifact_id=712,
        artifact_digest="sha256:" + ("3" * 64),
        artifact_url="https://example.test/artifacts/712",
        payload_path="reviewer-summary.md",
        payload_digest="sha256:" + ("4" * 64),
    )
    bundle_document: dict[str, JsonValue] = {
        "schema": "workflow-delivery/v3/approval-bundle",
        "producer": "materialize-publication",
    }
    bundle = SimpleNamespace(
        bundle_digest=canonical_sha256(bundle_document),
        to_document=lambda: bundle_document,
    )
    captured: dict[str, object] = {}

    monkeypatch.setattr(cli_module, "_load_live_intent", lambda _args: intent)
    monkeypatch.setattr(
        cli_module,
        "_load_attempt_binding",
        lambda _args: binding,
    )
    monkeypatch.setattr(
        cli_module,
        "_load_live_qualification_decision",
        lambda _args: qualification,
    )
    monkeypatch.setattr(
        cli_module,
        "_load_publication_snapshot",
        lambda _args: publication,
    )
    monkeypatch.setattr(
        cli_module,
        "_uploaded_payload_reference",
        lambda _args, *, name: {
            "publication_snapshot": publication_reference,
            "reviewer_summary": reviewer_reference,
        }[name],
    )

    def form_bundle(**kwargs: object) -> object:
        captured.update(kwargs)
        return bundle

    monkeypatch.setattr(cli_module, "form_approval_bundle", form_bundle)
    output = tmp_path / "approval-bundle.json"
    github_output = tmp_path / "github-output"
    control = f"workflow-delivery-v3:{binding.execution.target}"

    status = cli_module._release_form_approval_bundle_command(  # noqa: SLF001
        Namespace(
            control=control,
            output=str(output),
            github_output=str(github_output),
        )
    )

    assert status == 0
    assert captured == {
        "intent": intent,
        "attempt_binding": binding,
        "qualification_decision": qualification,
        "publication_snapshot": publication,
        "publication_snapshot_reference": publication_reference,
        "reviewer_summary_reference": reviewer_reference,
        "control": control,
    }
    assert json.loads(output.read_bytes()) == bundle_document
    assert github_output.read_text(encoding="utf-8").splitlines() == [
        f"approval-bundle-digest={bundle.bundle_digest}",
        f"approval-bundle-digest-hex={bundle.bundle_digest.removeprefix('sha256:')}",
    ]


def test_form_publication_authorization_command_uses_fresh_governance(  # noqa: PLR0915
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Form sole post-wait authorization from a fresh Governance read."""
    base_binding = _current_release_attempt_binding()
    intent = cli_module.normalize_buddy_live_intent(
        repository="hcoona/three",
        selected_ref="refs/heads/release",
        target=base_binding.execution.target,
        actor="hcoona",
        workflow_run_id=base_binding.attempt.workflow_run_id,
    )
    binding = replace(
        base_binding,
        intent_digest=intent.intent_digest,
        request_id=intent.request_id,
    )
    github_token = f"github-{binding.attempt.workflow_run_id}"
    model = SimpleNamespace(canonical_digest=binding.repository_model_digest)
    qualification = object()
    publication = object()
    source = SimpleNamespace(repository="hcoona/three")
    initial_governance = SimpleNamespace(
        provenance=binding.attestation_provenance,
        canonical_content_digest="sha256:" + ("7" * 64),
        attestation=SimpleNamespace(
            expires_at=datetime(2026, 10, 1, tzinfo=UTC),
            live_enabled=True,
        ),
    )
    initial_eligibility = SimpleNamespace(
        canonical_digest=binding.live_eligibility_payload_digest,
        governance=initial_governance,
        context=SimpleNamespace(selected_ref="refs/heads/release"),
    )
    bundle = SimpleNamespace(
        attempt=binding.attempt,
        bundle_digest="sha256:" + ("8" * 64),
    )
    publication_reference = ArtifactReference(
        artifact_id=711,
        artifact_digest="sha256:" + ("1" * 64),
        artifact_url="https://example.test/artifacts/711",
        payload_path="publication-snapshot.json",
        payload_digest="sha256:" + ("2" * 64),
    )
    reviewer_reference = ArtifactReference(
        artifact_id=712,
        artifact_digest="sha256:" + ("3" * 64),
        artifact_url="https://example.test/artifacts/712",
        payload_path="reviewer-summary.md",
        payload_digest="sha256:" + ("4" * 64),
    )
    bundle_reference = ArtifactReference(
        artifact_id=713,
        artifact_digest="sha256:" + ("5" * 64),
        artifact_url="https://example.test/artifacts/713",
        payload_path="approval-bundle.json",
        payload_digest=bundle.bundle_digest,
    )
    fresh_governance = object()
    authorization_document: dict[str, JsonValue] = {
        "schema": "workflow-delivery/v3/publication-authorization",
        "producer": "approve-publication",
        "result": "success",
    }
    authorization = SimpleNamespace(
        authorization_digest=canonical_sha256(authorization_document),
        to_document=lambda: authorization_document,
    )
    selected_modes: list[LiveEligibilityAdmissionMode] = []
    captured: dict[str, object] = {}
    closure: dict[str, object] = {}
    github_client = object()
    freshness_observed_at = datetime(
        2026,
        9,
        3,
        7,
        48,
        35,
        987654,
        tzinfo=UTC,
    )
    authorization_completed_at = datetime(
        2026,
        9,
        3,
        7,
        48,
        36,
        654321,
        tzinfo=UTC,
    )
    timestamps = iter((freshness_observed_at, authorization_completed_at))
    events: list[str] = []

    def now(zone: object) -> datetime:
        assert zone is UTC
        events.append("clock")
        return next(timestamps)

    def admit(
        _arguments: object,
        actual_intent: object,
        actual_model: object,
        *,
        admission_mode: LiveEligibilityAdmissionMode,
    ) -> tuple[object, object]:
        assert actual_intent is intent
        assert actual_model is model
        selected_modes.append(admission_mode)
        return initial_eligibility, SimpleNamespace(governance=source)

    def construct_client(*, repository: str, token: str) -> object:
        assert repository == source.repository
        assert token == github_token
        return github_client

    def require_fresh(
        actual_source: object,
        actual_client: object,
        **kwargs: object,
    ) -> object:
        assert actual_source is source
        assert actual_client is github_client
        assert kwargs["expected_provenance"] == (binding.attestation_provenance)
        assert kwargs["expected_canonical_content_digest"] == (
            initial_governance.canonical_content_digest
        )
        assert kwargs["expected_expires_at"] == "2026-10-01T00:00:00Z"
        assert kwargs["expected_live_enabled"] is True
        assert kwargs["now"] == freshness_observed_at
        events.append("fresh")
        return fresh_governance

    def form_authorization(**kwargs: object) -> object:
        events.append("form")
        captured.update(kwargs)
        return authorization

    for name, replacement in (
        ("datetime", SimpleNamespace(now=now)),
        ("_load_live_intent", lambda _args: intent),
        (
            "_load_live_model",
            lambda _args, actual_intent: (
                model
                if actual_intent is intent
                else pytest.fail(
                    "Publication Authorization Intent was substituted"
                )
            ),
        ),
        ("_load_attempt_binding", lambda _args: binding),
        ("_admitted_live_eligibility_decision", admit),
        ("_load_approval_bundle", lambda _args: bundle),
        (
            "_load_live_qualification_decision",
            lambda _args: qualification,
        ),
        ("_load_publication_snapshot", lambda _args: publication),
        (
            "_uploaded_payload_reference",
            lambda _args, *, name: {
                "publication_snapshot": publication_reference,
                "reviewer_summary": reviewer_reference,
                "approval_bundle": bundle_reference,
            }[name],
        ),
        (
            "validate_approval_bundle_closure",
            lambda **kwargs: closure.update(kwargs),
        ),
        ("GitHubGovernanceClient", construct_client),
        ("require_fresh_governance_identity", require_fresh),
        ("form_publication_authorization", form_authorization),
    ):
        monkeypatch.setattr(cli_module, name, replacement)
    output, github_output, control = (
        tmp_path / "publication-authorization.json",
        tmp_path / "github-output",
        f"workflow-delivery-v3:{binding.execution.target}",
    )

    status = cli_module._release_form_publication_authorization_command(  # noqa: SLF001
        Namespace(
            github_token=github_token,
            live_eligibility_artifact_id=(binding.live_eligibility_artifact_id),
            live_eligibility_artifact_digest=(
                binding.live_eligibility_artifact_digest
            ),
            approval_boundary_sentinel_result="success",
            control=control,
            output=str(output),
            github_output=str(github_output),
        )
    )

    assert status == 0
    assert events == ["clock", "fresh", "clock", "form"]
    assert selected_modes == [LiveEligibilityAdmissionMode.AUTHORIZATION_REPLAY]
    assert closure == {
        "approval_bundle": bundle,
        "intent": intent,
        "attempt_binding": binding,
        "qualification_decision": qualification,
        "publication_snapshot": publication,
        "publication_snapshot_reference": publication_reference,
        "reviewer_summary_reference": reviewer_reference,
        "control": control,
    }
    assert captured == {
        "approval_bundle": bundle,
        "approval_bundle_reference": bundle_reference,
        "approval_boundary_sentinel_result": "success",
        "governance": fresh_governance,
        "completed_at": "2026-09-03T07:48:36Z",
        "control": control,
    }
    assert json.loads(output.read_bytes()) == authorization_document
    assert github_output.read_text(encoding="utf-8").splitlines() == [
        f"publication-authorization-digest={authorization.authorization_digest}",
        (
            "publication-authorization-digest-hex="
            f"{authorization.authorization_digest.removeprefix('sha256:')}"
        ),
    ]


@pytest.mark.parametrize(
    "substitution",
    ["intent-digest", "repository-model", "eligibility-transport"],
)
def test_form_publication_authorization_rejects_substituted_attempt_binding(
    monkeypatch: pytest.MonkeyPatch,
    substitution: str,
) -> None:
    """Reject a post-wait binding substitution before reading Governance."""
    expected = _current_release_attempt_binding()
    intent = cli_module.normalize_buddy_live_intent(
        repository="hcoona/three",
        selected_ref="refs/heads/release",
        target=expected.execution.target,
        actor="hcoona",
        workflow_run_id=expected.attempt.workflow_run_id,
    )
    expected = replace(
        expected,
        intent_digest=intent.intent_digest,
        request_id=intent.request_id,
    )
    if substitution == "intent-digest":
        actual = replace(expected, intent_digest="sha256:" + ("e" * 64))
    elif substitution == "repository-model":
        actual = replace(
            expected,
            repository_model_digest="sha256:" + ("e" * 64),
        )
    else:
        actual = replace(
            expected,
            live_eligibility_artifact_id=(
                expected.live_eligibility_artifact_id + 1
            ),
        )
    model = SimpleNamespace(canonical_digest=expected.repository_model_digest)
    eligibility = SimpleNamespace(
        canonical_digest=expected.live_eligibility_payload_digest,
        governance=SimpleNamespace(provenance=expected.attestation_provenance),
    )

    monkeypatch.setattr(
        cli_module,
        "_load_live_intent",
        lambda _arguments: intent,
    )
    monkeypatch.setattr(
        cli_module,
        "_load_live_model",
        lambda _arguments, _intent: model,
    )
    monkeypatch.setattr(
        cli_module,
        "_load_attempt_binding",
        lambda _arguments: actual,
    )
    monkeypatch.setattr(
        cli_module,
        "_admitted_live_eligibility_decision",
        lambda *_arguments, **_kwargs: (
            eligibility,
            SimpleNamespace(),
        ),
    )
    monkeypatch.setattr(
        cli_module,
        "require_fresh_governance_identity",
        lambda *_arguments, **_kwargs: pytest.fail(
            "Substituted binding reached fresh Governance read"
        ),
    )

    with pytest.raises(
        ValueError,
        match=r"^Publication Authorization Attempt binding mismatch$",
    ):
        cli_module._release_form_publication_authorization_command(  # noqa: SLF001
            Namespace(
                live_eligibility_artifact_id=(
                    expected.live_eligibility_artifact_id
                ),
                live_eligibility_artifact_digest=(
                    expected.live_eligibility_artifact_digest
                ),
            )
        )


def _publication_authority_references() -> tuple[
    ArtifactReference,
    ArtifactReference,
    ArtifactReference,
]:
    return (
        ArtifactReference(
            artifact_id=711,
            artifact_digest="sha256:" + ("1" * 64),
            artifact_url="https://example.test/artifacts/711",
            payload_path="publication-snapshot.json",
            payload_digest="sha256:" + ("2" * 64),
        ),
        ArtifactReference(
            artifact_id=712,
            artifact_digest="sha256:" + ("3" * 64),
            artifact_url="https://example.test/artifacts/712",
            payload_path="reviewer-summary.md",
            payload_digest="sha256:" + ("4" * 64),
        ),
        ArtifactReference(
            artifact_id=713,
            artifact_digest="sha256:" + ("5" * 64),
            artifact_url="https://example.test/artifacts/713",
            payload_path="approval-bundle.json",
            payload_digest="sha256:" + ("6" * 64),
        ),
    )


@pytest.mark.parametrize(
    "substituted_edge",
    ["authorization-bundle", "bundle-publication", "bundle-reviewer"],
)
def test_preflight_rejects_equal_payload_different_transport(
    monkeypatch: pytest.MonkeyPatch,
    substituted_edge: str,
) -> None:
    """Reject a current-DAG edge that keeps bytes but changes transport."""
    (
        publication_reference,
        reviewer_reference,
        bundle_reference,
    ) = _publication_authority_references()
    bundle = SimpleNamespace(
        publication_snapshot_reference=(
            replace(publication_reference, artifact_id=999)
            if substituted_edge == "bundle-publication"
            else publication_reference
        ),
        reviewer_summary_reference=(
            replace(reviewer_reference, artifact_id=999)
            if substituted_edge == "bundle-reviewer"
            else reviewer_reference
        ),
    )
    authorization = SimpleNamespace(
        approval_bundle_reference=(
            replace(bundle_reference, artifact_id=999)
            if substituted_edge == "authorization-bundle"
            else bundle_reference
        ),
    )
    publication = SimpleNamespace(materialized_actions=(object(),))
    resolved = {
        "publication_snapshot": publication_reference,
        "reviewer_summary": reviewer_reference,
        "approval_bundle": bundle_reference,
    }

    monkeypatch.setattr(
        cli_module,
        "_load_publication_snapshot",
        lambda _args: publication,
    )
    monkeypatch.setattr(
        cli_module,
        "_load_approval_bundle",
        lambda _args: bundle,
    )
    monkeypatch.setattr(
        cli_module,
        "_load_publication_authorization",
        lambda _args: authorization,
    )
    monkeypatch.setattr(
        cli_module,
        "_uploaded_payload_reference",
        lambda _args, *, name: resolved[name],
    )
    monkeypatch.setattr(
        cli_module,
        "preflight_github_packages_action",
        lambda **_kwargs: pytest.fail(
            "Substituted authority reached publisher preflight"
        ),
    )

    with pytest.raises(ValueError, match="authority reference mismatch"):
        cli_module._release_preflight_github_packages_command(  # noqa: SLF001
            Namespace()
        )


def test_publish_rejects_equal_payload_different_transport(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Keep the dormant publish handler behind the same exact authority gate."""
    (
        publication_reference,
        reviewer_reference,
        bundle_reference,
    ) = _publication_authority_references()
    publication = object()
    bundle = SimpleNamespace(
        publication_snapshot_reference=replace(
            publication_reference,
            artifact_url="https://example.test/artifacts/999",
        ),
        reviewer_summary_reference=reviewer_reference,
    )
    authorization = SimpleNamespace(
        approval_bundle_reference=bundle_reference,
    )
    resolved = {
        "publication_snapshot": publication_reference,
        "reviewer_summary": reviewer_reference,
        "approval_bundle": bundle_reference,
    }

    monkeypatch.setattr(
        cli_module,
        "_load_publication_snapshot",
        lambda _arguments: publication,
    )
    monkeypatch.setattr(
        cli_module,
        "_load_approval_bundle",
        lambda _arguments: bundle,
    )
    monkeypatch.setattr(
        cli_module,
        "_load_publication_authorization",
        lambda _arguments: authorization,
    )
    monkeypatch.setattr(
        cli_module,
        "_uploaded_payload_reference",
        lambda _arguments, *, name: resolved[name],
    )
    monkeypatch.setattr(
        cli_module,
        "_load_github_packages_preflight",
        lambda *_arguments, **_kwargs: pytest.fail(
            "Substituted authority passed the publish CLI gate"
        ),
    )

    with pytest.raises(
        ValueError,
        match=r"^Publication authority reference mismatch$",
    ):
        cli_module._release_publish_github_packages_command(  # noqa: SLF001
            Namespace(
                preflight="unused",
                preflight_digest="sha256:" + ("0" * 64),
            )
        )


def test_prove_exact_satisfied_uses_post_freshness_proof_timestamp(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Derive the proof timestamp after fresh Governance completes."""
    binding = _current_release_attempt_binding()
    github_token = f"github-{binding.attempt.workflow_run_id}"
    intent = object()
    model = SimpleNamespace(
        canonical_digest=binding.repository_model_digest,
    )
    publication = SimpleNamespace(attempt=binding.attempt)
    source = SimpleNamespace(repository="hcoona/three")
    initial_governance = SimpleNamespace(
        provenance=binding.attestation_provenance,
        canonical_content_digest="sha256:" + ("7" * 64),
        attestation=SimpleNamespace(
            expires_at=datetime(2026, 10, 1, tzinfo=UTC),
            live_enabled=True,
        ),
    )
    initial_eligibility = SimpleNamespace(governance=initial_governance)
    fresh_governance = object()
    proof_document: dict[str, JsonValue] = {
        "schema": "workflow-delivery/v3/exact-satisfied-governance-proof",
        "producer": "prove-exact-satisfied",
    }
    proof = SimpleNamespace(
        proof_digest=canonical_sha256(proof_document),
        to_document=lambda: proof_document,
    )
    freshness_observed_at = datetime(
        2026,
        9,
        3,
        7,
        48,
        35,
        987654,
        tzinfo=UTC,
    )
    proof_completed_at = datetime(
        2026,
        9,
        3,
        7,
        48,
        36,
        654321,
        tzinfo=UTC,
    )
    timestamps = iter((freshness_observed_at, proof_completed_at))
    events: list[str] = []
    captured: dict[str, object] = {}
    github_client = object()

    def now(zone: object) -> datetime:
        assert zone is UTC
        events.append("clock")
        return next(timestamps)

    def construct_client(*, repository: str, token: str) -> object:
        assert repository == source.repository
        assert token == github_token
        return github_client

    def require_fresh(
        actual_source: object,
        actual_client: object,
        **kwargs: object,
    ) -> object:
        assert actual_source is source
        assert actual_client is github_client
        assert kwargs["now"] == freshness_observed_at
        assert kwargs["expected_provenance"] == binding.attestation_provenance
        assert kwargs["expected_canonical_content_digest"] == (
            initial_governance.canonical_content_digest
        )
        assert kwargs["expected_expires_at"] == "2026-10-01T00:00:00Z"
        assert kwargs["expected_live_enabled"] is True
        events.append("fresh")
        return fresh_governance

    def form_proof(**kwargs: object) -> object:
        events.append("form")
        captured.update(kwargs)
        return proof

    for name, replacement in (
        ("datetime", SimpleNamespace(now=now)),
        ("_load_live_intent", lambda _args: intent),
        (
            "_load_live_model",
            lambda _args, actual_intent: (
                model
                if actual_intent is intent
                else pytest.fail("Exact-satisfied Intent was substituted")
            ),
        ),
        ("_load_attempt_binding", lambda _args: binding),
        ("_load_publication_snapshot", lambda _args: publication),
        (
            "_admitted_live_eligibility_decision",
            lambda *_args, **_kwargs: (
                initial_eligibility,
                SimpleNamespace(governance=source),
            ),
        ),
        ("GitHubGovernanceClient", construct_client),
        ("require_fresh_governance_identity", require_fresh),
        ("form_exact_satisfied_governance_proof", form_proof),
    ):
        monkeypatch.setattr(cli_module, name, replacement)
    output = tmp_path / "exact-satisfied-governance-proof.json"
    github_output = tmp_path / "github-output"
    control = f"workflow-delivery-v3:{binding.execution.target}"

    status = cli_module._release_prove_exact_satisfied_command(  # noqa: SLF001
        Namespace(
            github_token=github_token,
            control=control,
            output=str(output),
            github_output=str(github_output),
        )
    )

    assert status == 0
    assert events == ["clock", "fresh", "clock", "form"]
    assert captured == {
        "publication_snapshot": publication,
        "governance": fresh_governance,
        "proved_at": "2026-09-03T07:48:36Z",
        "control": control,
    }
    assert json.loads(output.read_bytes()) == proof_document
    assert github_output.read_text(encoding="utf-8").splitlines() == [
        f"exact-satisfied-governance-proof-digest={proof.proof_digest}",
        (
            "exact-satisfied-governance-proof-digest-hex="
            f"{proof.proof_digest.removeprefix('sha256:')}"
        ),
    ]


def test_prove_exact_satisfied_rejects_loaded_attempt_provenance_substitution(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Reject an eligibility replay whose Governance lineage was replaced."""
    binding = _current_release_attempt_binding()
    substituted_provenance = tuple(
        (name, "9" * 40 if name == "blob-oid" else value)
        for name, value in binding.attestation_provenance
    )
    intent = object()
    model = SimpleNamespace(
        canonical_digest=binding.repository_model_digest,
    )
    publication = SimpleNamespace(attempt=binding.attempt)
    selected_modes: list[LiveEligibilityAdmissionMode] = []

    monkeypatch.setattr(cli_module, "_load_live_intent", lambda _args: intent)
    monkeypatch.setattr(
        cli_module,
        "_load_live_model",
        lambda _args, actual_intent: (
            model
            if actual_intent is intent
            else pytest.fail("Exact-satisfied Intent was substituted")
        ),
    )
    monkeypatch.setattr(
        cli_module,
        "_load_attempt_binding",
        lambda _args: binding,
    )
    monkeypatch.setattr(
        cli_module,
        "_load_publication_snapshot",
        lambda _args: publication,
    )

    def admit(
        _arguments: object,
        actual_intent: object,
        actual_model: object,
        *,
        admission_mode: LiveEligibilityAdmissionMode,
    ) -> tuple[object, object]:
        assert actual_intent is intent
        assert actual_model is model
        selected_modes.append(admission_mode)
        return (
            SimpleNamespace(
                governance=SimpleNamespace(
                    provenance=substituted_provenance,
                )
            ),
            object(),
        )

    monkeypatch.setattr(
        cli_module,
        "_admitted_live_eligibility_decision",
        admit,
    )
    output = tmp_path / "exact-satisfied-governance-proof.json"

    with pytest.raises(
        ValueError,
        match=r"^Exact-satisfied proof Attempt authority binding mismatch$",
    ):
        cli_module._release_prove_exact_satisfied_command(  # noqa: SLF001
            Namespace(output=str(output))
        )

    assert selected_modes == [LiveEligibilityAdmissionMode.AUTHORIZATION_REPLAY]
    assert not output.exists()


def test_result_cli_fails_closed_on_substituted_control(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Discard a terminal state whose control is not target-derived."""
    target = "b" * 40
    expected_control = f"workflow-delivery-v3:{target}"
    attempt = ReleaseAttemptIdentity(
        execution=BuddyExecutionIdentity(
            channel="buddy",
            release_unit="hcoona-release-smoke-npm",
            target=target,
        ),
        workflow_run_id=812,
    )
    action = SimpleNamespace(
        action_id="publish-github-packages",
        action_digest="sha256:" + ("a" * 64),
        lock_group="npm:@hcoona/hcoona-release-smoke-npm",
    )
    publication = SimpleNamespace(
        attempt=attempt,
        snapshot_digest="sha256:" + ("1" * 64),
        materialized_actions=(action,),
    )
    monkeypatch.setattr(
        cli_module,
        "_load_publication_snapshot",
        lambda _arguments: publication,
    )
    state_path = tmp_path / "execution-state.json"
    state_path.write_bytes(
        canonicalize(
            {
                "schema": "workflow-delivery/v3/deferred-publication-result",
                "action-id": action.action_id,
                "action-digest": action.action_digest,
                "lock-group": action.lock_group,
                "outcome": "failed",
                "mutation-disposition": "no-side-effect",
                "response-identity-digest": None,
                "receipt": None,
                "diagnostic-reference": "substituted-state",
                "control": f"workflow-delivery-v3:{'c' * 40}",
            }
        )
    )
    result_path = tmp_path / "action-result.json"
    status = cli_module._release_form_github_packages_result_command(  # noqa: SLF001
        cast(
            "argparse.Namespace",
            SimpleNamespace(
                execution_state=str(state_path),
                mutation_marker_artifact_id=None,
                publish_step_outcome="failure",
                target=target,
                result_output=str(result_path),
                github_output=str(tmp_path / "github-output"),
            ),
        )
    )

    result = json.loads(result_path.read_bytes())
    assert status == 1
    assert result["control"] == expected_control
    assert (
        result["diagnostic-reference"]
        == "preflight-failed-before-mutation-start"
    )


def test_acceptance_cli_persists_validated_request_proof(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Persist the admitted proof rather than reconstructed request data."""
    from three_workflow_delivery_v3.adapters.github_packages import (  # noqa: PLC0415
        FixedAcceptanceSuiteResult,
        FixedCoordinateAcceptanceProbeResult,
        ValidatedAcceptanceRequestProof,
    )

    proof = ValidatedAcceptanceRequestProof.from_validated_exchange(
        raw_request=b'{"actual":"captured-couchdb-request"}',
        tarball=b"acceptance-tarball",
        package_coordinate=cli_module.ACCEPTANCE_PACKAGE_COORDINATE,
        tag="wdv3-acceptance-1",
        upstream_status=201,
        selected_headers={"ETag": '"created"'},
        response_body=b'{"ok":true}',
    )
    result = FixedAcceptanceSuiteResult(
        suite="absent-create-readback",
        scenarios=(
            FixedCoordinateAcceptanceProbeResult(
                scenario="absent-create-readback",
                package_coordinate=cli_module.ACCEPTANCE_PACKAGE_COORDINATE,
                tag="wdv3-acceptance-1",
                pre_state="absent",
                post_state="exact",
                result="created",
                mutation_classification="complete",
                action_executed=True,
                mutation_started=True,
                response_identity_digest=proof.response_identity_digest,
                content_sha512=proof.tarball_sha512,
                diagnostics=(),
                validated_request_proof=proof,
            ),
        ),
    )
    monkeypatch.setenv("WDV3_ACCEPTANCE_GITHUB_TOKEN", "upstream-secret")
    monkeypatch.setattr(
        cli_module,
        "_build_acceptance_tarball",
        lambda root, **_kwargs: root / "unused.tgz",
    )
    monkeypatch.setattr(
        cli_module,
        "run_fixed_acceptance_suite",
        lambda **_kwargs: result,
    )
    output = tmp_path / "acceptance.json"
    arguments = cast(
        "cli_module._AcceptanceProbeArguments",  # noqa: SLF001
        SimpleNamespace(
            package_coordinate=cli_module.ACCEPTANCE_PACKAGE_COORDINATE,
            suite="absent-create-readback",
            target_sha="c" * 40,
            timeout_seconds=7.0,
            max_response_bytes=8192,
            max_output_bytes=4096,
            output=str(output),
            github_output=None,
        ),
    )

    assert (
        cli_module._governance_run_fixed_acceptance_probe_command(  # noqa: SLF001
            arguments
        )
        == 0
    )

    persisted = json.loads(output.read_bytes())
    assert persisted["scenarios"][0]["validated-request-proof"] == (
        proof.to_document()
    )
    assert persisted["scenarios"][0]["response"]["identity-digest"] == (
        proof.response_identity_digest
    )


def test_acceptance_cli_output_contains_no_request_credentials(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Keep raw request credentials out of persisted acceptance output."""
    from three_workflow_delivery_v3.adapters.github_packages import (  # noqa: PLC0415
        FixedAcceptanceSuiteResult,
        FixedCoordinateAcceptanceProbeResult,
        ValidatedAcceptanceRequestProof,
    )

    proof = ValidatedAcceptanceRequestProof.from_validated_exchange(
        raw_request=b'{"authorization":"not-retained"}',
        tarball=b"acceptance-tarball",
        package_coordinate=cli_module.ACCEPTANCE_PACKAGE_COORDINATE,
        tag="wdv3-acceptance-1",
        upstream_status=201,
        selected_headers={"Content-Type": "application/json"},
        response_body=b'{"ok":true}',
    )
    result = FixedAcceptanceSuiteResult(
        suite="absent-create-readback",
        scenarios=(
            FixedCoordinateAcceptanceProbeResult(
                scenario="absent-create-readback",
                package_coordinate=cli_module.ACCEPTANCE_PACKAGE_COORDINATE,
                tag="wdv3-acceptance-1",
                pre_state="absent",
                post_state="exact",
                result="created",
                mutation_classification="complete",
                action_executed=True,
                mutation_started=True,
                response_identity_digest=proof.response_identity_digest,
                content_sha512=proof.tarball_sha512,
                diagnostics=(),
                validated_request_proof=proof,
            ),
        ),
    )
    monkeypatch.setenv("WDV3_ACCEPTANCE_GITHUB_TOKEN", "upstream-secret")
    monkeypatch.setattr(
        cli_module,
        "_build_acceptance_tarball",
        lambda root, **_kwargs: root / "unused.tgz",
    )
    monkeypatch.setattr(
        cli_module,
        "run_fixed_acceptance_suite",
        lambda **_kwargs: result,
    )
    output = tmp_path / "acceptance.json"

    cli_module._governance_run_fixed_acceptance_probe_command(  # noqa: SLF001
        cast(
            "cli_module._AcceptanceProbeArguments",  # noqa: SLF001
            SimpleNamespace(
                package_coordinate=cli_module.ACCEPTANCE_PACKAGE_COORDINATE,
                suite="absent-create-readback",
                target_sha="c" * 40,
                timeout_seconds=7.0,
                max_response_bytes=8192,
                max_output_bytes=4096,
                output=str(output),
                github_output=None,
            ),
        )
    )

    retained = output.read_bytes()
    assert b"upstream-secret" not in retained
    assert b"not-retained" not in retained
    assert b"authorization" not in retained.lower()


def test_acceptance_cli_threads_single_deadline_through_observe_publish_and_cleanup(  # noqa: E501
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Thread one absolute deadline into the acceptance suite."""
    from three_workflow_delivery_v3.adapters.github_packages import (  # noqa: PLC0415
        FixedAcceptanceSuiteResult,
        FixedCoordinateAcceptanceProbeResult,
    )

    captured: dict[str, object] = {}
    tarball = tmp_path / "acceptance.tgz"
    tarball.write_bytes(b"acceptance")
    monkeypatch.setenv("WDV3_ACCEPTANCE_GITHUB_TOKEN", "local-test-token")
    monkeypatch.setattr(cli_module, "monotonic", lambda: 100.0)
    monkeypatch.setattr(
        cli_module,
        "_build_acceptance_tarball",
        lambda *_args, **_kwargs: tarball,
    )

    def run_suite(**kwargs: object) -> FixedAcceptanceSuiteResult:
        captured.update(kwargs)
        return FixedAcceptanceSuiteResult(
            suite="absent-create-readback",
            scenarios=(
                FixedCoordinateAcceptanceProbeResult(
                    scenario="absent-create-readback",
                    package_coordinate=cli_module.ACCEPTANCE_PACKAGE_COORDINATE,
                    tag="wdv3-acceptance-1",
                    pre_state="exact",
                    post_state="exact",
                    result="exact-no-mutation",
                    mutation_classification="complete",
                    action_executed=False,
                    mutation_started=False,
                    response_identity_digest="sha256:" + ("a" * 64),
                    content_sha512="sha512:" + ("b" * 128),
                    diagnostics=(),
                ),
            ),
        )

    monkeypatch.setattr(cli_module, "run_fixed_acceptance_suite", run_suite)
    output = tmp_path / "output.json"

    status = cli_module._governance_run_fixed_acceptance_probe_command(  # noqa: SLF001
        cast(
            "cli_module._AcceptanceProbeArguments",  # noqa: SLF001
            SimpleNamespace(
                package_coordinate=cli_module.ACCEPTANCE_PACKAGE_COORDINATE,
                suite="absent-create-readback",
                target_sha="c" * 40,
                timeout_seconds=7.0,
                max_response_bytes=8192,
                max_output_bytes=4096,
                output=str(output),
                github_output=None,
            ),
        )
    )

    assert status == 0
    expected_timeout = 7.0
    assert captured["deadline"] == 100.0 + expected_timeout
    assert captured["timeout_seconds"] == expected_timeout
    assert (
        json.loads(output.read_bytes())["mutation-classification"] == "complete"
    )


def test_acceptance_cli_persists_incomplete_result_for_partial_runner_facts(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Persist fail-closed facts when the runner omits one required fact."""

    class Transport:
        def __init__(self, *_args: object, **_kwargs: object) -> None:
            self.calls = 0

        def observe(
            self, *_args: object, **_kwargs: object
        ) -> dict[str, object]:
            self.calls += 1
            if self.calls == 1:
                return {
                    "state": "absent",
                    "response-identity-digest": "sha256:" + ("a" * 64),
                }
            return {
                "state": "exact",
                "version": "0.0.0-wdv3-acceptance.1",
                "tag": "wdv3-acceptance-1",
                "content-sha512": (
                    "sha512:" + hashlib.sha512(b"acceptance").hexdigest()
                ),
                "response-identity-digest": "sha256:" + ("b" * 64),
            }

    class Runner:
        def __init__(self, *_args: object, **_kwargs: object) -> None:
            pass

        def run(
            self,
            *_args: object,
            **_kwargs: object,
        ) -> dict[str, object]:
            return {"outcome": "created", "action-executed": True}

    tarball = tmp_path / "acceptance.tgz"
    tarball.write_bytes(b"acceptance")
    monkeypatch.setenv("WDV3_ACCEPTANCE_GITHUB_TOKEN", "local-test-token")
    monkeypatch.setattr(
        cli_module,
        "_build_acceptance_tarball",
        lambda *_args, **_kwargs: tarball,
    )
    monkeypatch.setattr(cli_module, "_AcceptanceNpmTransport", Transport)
    monkeypatch.setattr(cli_module, "_AcceptanceNpmRunner", Runner)
    output = tmp_path / "incomplete.json"

    status = cli_module._governance_run_fixed_acceptance_probe_command(  # noqa: SLF001
        cast(
            "cli_module._AcceptanceProbeArguments",  # noqa: SLF001
            SimpleNamespace(
                package_coordinate=cli_module.ACCEPTANCE_PACKAGE_COORDINATE,
                suite="absent-create-readback",
                target_sha="c" * 40,
                timeout_seconds=7.0,
                max_response_bytes=8192,
                max_output_bytes=4096,
                output=str(output),
                github_output=None,
            ),
        )
    )

    document = json.loads(output.read_bytes())
    scenario = document["scenarios"][0]
    assert status == 0
    assert scenario["mutation-classification"] == "incomplete"
    assert scenario["action"]["executed"] is False
    assert scenario["action"]["mutation-started"] is False
    assert scenario["response"]["result"] == "runner-malformed-before-mutation"


def _acceptance_parser_arguments(
    suite: str,
    *extra: str,
) -> argparse.Namespace:
    return cli_module._parser().parse_args(  # noqa: SLF001
        [
            "governance",
            "run-fixed-acceptance-probe",
            "--suite",
            suite,
            "--package-coordinate",
            cli_module.ACCEPTANCE_PACKAGE_COORDINATE,
            "--target-sha",
            "c" * 40,
            "--output",
            "acceptance.json",
            *extra,
        ]
    )


def test_acceptance_absent_create_readback_default_timeout_is_120_seconds() -> (
    None
):
    """Use the bounded default for the single-scenario acceptance suite."""
    arguments = _acceptance_parser_arguments("absent-create-readback")
    expected_timeout = 120.0

    assert arguments.timeout_seconds == expected_timeout
    assert type(arguments.timeout_seconds) is float


def test_acceptance_exact_and_conflict_default_timeout_is_at_least_300_seconds() -> (  # noqa: E501
    None
):
    """Reserve a realistic minimum budget for all four scenarios."""
    arguments = _acceptance_parser_arguments("exact-and-conflict")
    minimum_timeout = 300.0

    assert arguments.timeout_seconds >= minimum_timeout
    assert type(arguments.timeout_seconds) is float


@pytest.mark.parametrize(
    "suite",
    ["absent-create-readback", "exact-and-conflict"],
)
def test_acceptance_explicit_timeout_overrides_suite_default(
    suite: str,
) -> None:
    """Preserve an explicit operator-selected timeout for either suite."""
    explicit_timeout = 43.25
    arguments = _acceptance_parser_arguments(
        suite,
        "--timeout-seconds",
        str(explicit_timeout),
    )

    assert arguments.timeout_seconds == explicit_timeout
    assert type(arguments.timeout_seconds) is float


@pytest.mark.parametrize("_timeout_contract", [None], ids=["timeout-budget"])
def test_acceptance_cli_does_not_reset_deadline_between_scenarios(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    _timeout_contract: None,
) -> None:
    """Construct one deadline and spend it monotonically across scenarios."""
    from three_workflow_delivery_v3.adapters.github_packages import (  # noqa: PLC0415
        FixedAcceptanceSuiteResult,
        FixedCoordinateAcceptanceProbeResult,
    )

    class Clock:
        now = 200.0
        calls = 0

        def monotonic(self) -> float:
            self.calls += 1
            return self.now

    clock = Clock()
    budgets: list[float] = []
    tarball = tmp_path / "acceptance.tgz"
    tarball.write_bytes(b"acceptance")
    monkeypatch.setenv("WDV3_ACCEPTANCE_GITHUB_TOKEN", "local-test-token")
    monkeypatch.setattr(cli_module, "monotonic", clock.monotonic)
    monkeypatch.setattr(
        cli_module,
        "_build_acceptance_tarball",
        lambda *_args, **_kwargs: tarball,
    )

    def run_suite(**kwargs: object) -> FixedAcceptanceSuiteResult:
        deadline = cast("float", kwargs["deadline"])
        timeout = cast("float", kwargs["timeout_seconds"])
        assert deadline == 200.0 + timeout
        scenarios = []
        for index, scenario in enumerate(
            ("exact", "identical-race", "differing-race", "lost-response")
        ):
            budgets.append(deadline - clock.now)
            clock.now += 2.0
            scenarios.append(
                FixedCoordinateAcceptanceProbeResult(
                    scenario=scenario,
                    package_coordinate=cli_module.ACCEPTANCE_PACKAGE_COORDINATE,
                    tag=f"wdv3-acceptance-{index + 1}",
                    pre_state="unknown",
                    post_state="unknown",
                    result="timeout",
                    mutation_classification="unknown",
                    action_executed=True,
                    mutation_started=True,
                    response_identity_digest="sha256:" + ("a" * 64),
                    content_sha512=None,
                    diagnostics=("acceptance-operation-timeout",),
                )
            )
        return FixedAcceptanceSuiteResult(
            suite="exact-and-conflict",
            scenarios=tuple(scenarios),
        )

    monkeypatch.setattr(cli_module, "run_fixed_acceptance_suite", run_suite)
    output = tmp_path / "acceptance.json"

    status = cli_module._governance_run_fixed_acceptance_probe_command(  # noqa: SLF001
        cast(
            "cli_module._AcceptanceProbeArguments",  # noqa: SLF001
            SimpleNamespace(
                package_coordinate=cli_module.ACCEPTANCE_PACKAGE_COORDINATE,
                suite="exact-and-conflict",
                target_sha="c" * 40,
                timeout_seconds=300.0,
                max_response_bytes=8192,
                max_output_bytes=4096,
                output=str(output),
                github_output=None,
            ),
        )
    )

    assert status == 0
    assert clock.calls == 1
    assert budgets == [300.0, 298.0, 296.0, 294.0]
    assert json.loads(output.read_bytes())["scenario-inventory"] == [
        "exact",
        "identical-race",
        "differing-race",
        "lost-response",
    ]


def _valid_mutation_marker_fixture(
    tmp_path: Path,
) -> tuple[
    Path,
    PublicationSnapshot,
    GitHubPackagesPublishPreflight,
    cli_module.MutationMayHaveStartedMarker,
]:
    attempt = ReleaseAttemptIdentity(
        execution=BuddyExecutionIdentity(
            channel="buddy",
            release_unit="hcoona-release-smoke-npm",
            target="e" * 40,
        ),
        workflow_run_id=WORKFLOW_RUN_ID,
    )
    action = SimpleNamespace(
        action_digest="sha256:" + ("c" * 64),
        lock_group="npm:@hcoona/hcoona-release-smoke-npm",
    )
    publication = cast(
        "PublicationSnapshot",
        SimpleNamespace(
            attempt=attempt,
            snapshot_digest="sha256:" + ("b" * 64),
            materialized_actions=(action,),
        ),
    )
    preflight = GitHubPackagesPublishPreflight(
        attempt=attempt,
        publication_snapshot_digest=publication.snapshot_digest,
        action_digest=action.action_digest,
        lock_group=action.lock_group,
        tarball_sha256="sha256:" + ("1" * 64),
        tarball_sha512="sha512:" + ("2" * 128),
        npm_configuration_digest="sha256:" + ("3" * 64),
        governance_provenance=(("repository", "hcoona/three"),),
        governance_canonical_content_digest="sha256:" + ("4" * 64),
        governance_expires_at="2026-08-19T00:00:00Z",
        governance_live_enabled=True,
    )
    expected = cli_module.MutationMayHaveStartedMarker(
        attempt=attempt,
        publication_snapshot_digest=publication.snapshot_digest,
        action_digest=action.action_digest,
        lock_group=action.lock_group,
        preflight_digest=preflight.preflight_digest,
    )
    path = _write_canonical(
        tmp_path / "mutation-may-have-started.json",
        expected.to_document(),
    )
    return path, publication, preflight, expected


@pytest.mark.parametrize(
    ("artifact_id", "artifact_digest"),
    [
        pytest.param(1, NPM_ARTIFACT_DIGEST, id="v7-native-bare-digest"),
        pytest.param(
            NPM_ARTIFACT_ID,
            f"sha256:{NPM_ARTIFACT_DIGEST}",
            id="canonical-prefixed-digest",
        ),
    ],
)
def test_load_mutation_marker_accepts_supported_artifact_digest_forms(
    tmp_path: Path,
    artifact_id: int,
    artifact_digest: str,
) -> None:
    """Accept native upload-artifact v7 and canonical digest forms."""
    path, publication, preflight, expected = _valid_mutation_marker_fixture(
        tmp_path
    )

    marker = cli_module._load_mutation_marker(  # noqa: SLF001
        str(path),
        expected_digest=expected.marker_digest,
        artifact_id=artifact_id,
        artifact_digest=artifact_digest,
        publication=publication,
        preflight=preflight,
    )

    assert marker.to_document() == expected.to_document()


@pytest.mark.parametrize(
    ("artifact_id", "artifact_digest"),
    [
        pytest.param(
            NPM_ARTIFACT_ID,
            "a" * 63,
            id="short-bare",
        ),
        pytest.param(
            NPM_ARTIFACT_ID,
            "A" * 64,
            id="uppercase-bare",
        ),
        pytest.param(
            NPM_ARTIFACT_ID,
            "g" * 64,
            id="lowercase-nonhex-bare",
        ),
        pytest.param(
            NPM_ARTIFACT_ID,
            "sha256:" + ("g" * 64),
            id="lowercase-nonhex-prefixed",
        ),
        pytest.param(
            NPM_ARTIFACT_ID,
            f"sha256:sha256:{NPM_ARTIFACT_DIGEST}",
            id="duplicate-prefix",
        ),
        pytest.param(
            NPM_ARTIFACT_ID,
            f"sha512:{NPM_ARTIFACT_DIGEST}",
            id="wrong-algorithm-prefix",
        ),
        pytest.param(
            NPM_ARTIFACT_ID,
            f"{NPM_ARTIFACT_DIGEST}\n",
            id="trailing-whitespace",
        ),
        pytest.param(
            0,
            NPM_ARTIFACT_DIGEST,
            id="zero-artifact-id",
        ),
        pytest.param(
            -1,
            NPM_ARTIFACT_DIGEST,
            id="negative-artifact-id",
        ),
    ],
)
def test_load_mutation_marker_rejects_malformed_artifact_transport(
    tmp_path: Path,
    artifact_id: int,
    artifact_digest: str,
) -> None:
    """Reject malformed upload transport before returning any marker."""
    path, publication, preflight, expected = _valid_mutation_marker_fixture(
        tmp_path
    )

    with pytest.raises(
        ValueError,
        match=r"^mutation-start marker transport is malformed$",
    ):
        cli_module._load_mutation_marker(  # noqa: SLF001
            str(path),
            expected_digest=expected.marker_digest,
            artifact_id=artifact_id,
            artifact_digest=artifact_digest,
            publication=publication,
            preflight=preflight,
        )


def _run_compile_live_model_scenario(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    *,
    target: str = "a" * 40,
    malformed_authoring: str | None = None,
) -> tuple[int, Path, Path]:
    repo, actual_target = _target_authoring_repo(
        tmp_path,
        malformed_authoring=malformed_authoring,
    )
    # Keep the Phase 1 target literal while Git reads the real fixture commit.
    _write(
        repo / ".git/refs/replace" / target,
        f"{actual_target}\n",
    )
    intent = cli_module.normalize_buddy_live_intent(
        repository="hcoona/three",
        selected_ref="refs/heads/feature/release",
        target=target,
        actor="release-operator",
        workflow_run_id=WORKFLOW_RUN_ID,
    )
    intent_path = _write_canonical(
        tmp_path / "live-release-intent.json",
        intent.to_document(),
    )
    context = CompilationContext(
        request_id=intent.request_id,
        purpose="live-release",
        workflow_run_id=WORKFLOW_RUN_ID,
        run_attempt=None,
        target=target,
        producer="compile-live-model",
        control=f"workflow-delivery-v3:{target}",
        catalog_digest=cli_module.catalog_digest(),
    )
    manifest = first_slice_provider_manifest(
        context,
        provider_producer="discover-node",
    )
    provider = _fake_provider_result(
        provider_binding(manifest, "node-first-slice")
    )
    provider_document = provider.to_document()
    provider_document["provider-request-manifest-digest"] = (
        manifest.manifest_digest
    )
    provider_document["result-digest"] = provider.result_digest
    provider_path = _write_canonical(
        tmp_path / "live-provider-result.json",
        provider_document,
    )
    model_output = tmp_path / "live-repository-model.json"
    github_output = tmp_path / "github-output"

    def reject_provider_rerun(*_arguments: object, **_keywords: object) -> None:
        message = "Provider must not rerun during live compilation"
        raise AssertionError(message)

    monkeypatch.setattr(
        cli_module,
        "provide_node_repository_facts",
        reject_provider_rerun,
    )
    result = cli_module.main(
        [
            "release",
            "compile-live-model",
            "--repo-root",
            str(repo),
            "--workflow-run-id",
            str(WORKFLOW_RUN_ID),
            "--run-attempt",
            str(RUN_ATTEMPT),
            "--target",
            target,
            "--intent",
            str(intent_path),
            "--intent-digest",
            intent.intent_digest,
            "--intent-artifact-id",
            "101",
            "--intent-artifact-digest",
            f"sha256:{hashlib.sha256(intent_path.read_bytes()).hexdigest()}",
            "--provider-result",
            str(provider_path),
            "--provider-artifact-id",
            "102",
            "--provider-artifact-digest",
            f"sha256:{hashlib.sha256(provider_path.read_bytes()).hexdigest()}",
            "--output",
            str(model_output),
            "--github-output",
            str(github_output),
        ]
    )
    return result, model_output, github_output


def test_compile_live_model_emits_canonical_buddy_execution_concurrency_key(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
) -> None:
    """Emit the canonical Buddy concurrency key after successful compilation."""
    result, model_output, github_output = _run_compile_live_model_scenario(
        tmp_path,
        monkeypatch,
    )
    captured = capsys.readouterr()
    model_document = cast(
        "dict[str, JsonValue]",
        json.loads(model_output.read_bytes()),
    )
    admitted = admit_repository_model_snapshot(
        model_output.read_bytes(),
        expected_context=CompilationContext(
            request_id=cast(
                "str",
                model_document["context"]["request-id"],  # type: ignore[index]
            ),
            purpose="live-release",
            workflow_run_id=WORKFLOW_RUN_ID,
            run_attempt=None,
            target="a" * 40,
            producer="compile-live-model",
            control=f"workflow-delivery-v3:{'a' * 40}",
            catalog_digest=cli_module.catalog_digest(),
        ),
        expected_digest=canonical_sha256(model_document),
    )
    output_lines = github_output.read_text(encoding="utf-8").splitlines()

    assert result == 0
    assert captured.out == ""
    assert captured.err == ""
    assert admitted.snapshot.ready is True
    assert output_lines == [
        f"repository-model-digest={admitted.canonical_digest}",
        (
            "repository-model-digest-hex="
            f"{admitted.canonical_digest.removeprefix('sha256:')}"
        ),
        (
            "execution-concurrency-key="
            "a71c896702fc7f6869d6dc6714840eba7393c9e98eaf820d"
            "3254299d664534a6"
        ),
    ]
    assert "sha256:" not in output_lines[2]


def test_compile_live_model_does_not_emit_execution_concurrency_key_when_compilation_fails(  # noqa: E501
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
) -> None:
    """Do not emit a concurrency key when compilation fails."""
    result, model_output, github_output = _run_compile_live_model_scenario(
        tmp_path,
        monkeypatch,
        malformed_authoring="quality",
    )
    captured = capsys.readouterr()

    assert result == 1
    assert captured.out == ""
    assert "malformed YAML authoring" in captured.err
    assert not model_output.exists()
    assert not github_output.exists()


def test_compile_live_model_execution_concurrency_key_changes_with_target(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
) -> None:
    """Derive distinct canonical execution keys through the real CLI path."""
    targets = ("a" * 40, "b" * 40)
    expected_keys = (
        "a71c896702fc7f6869d6dc6714840eba7393c9e98eaf820d3254299d664534a6",
        "9eeac4fd6533b5afb39ebb70ed223833578e268b6d9b0bd46111687465778bd6",
    )
    results: list[int] = []
    model_documents: list[dict[str, JsonValue]] = []
    output_lines_by_target: list[list[str]] = []

    for index, target in enumerate(targets):
        scenario_root = tmp_path / f"target-{index}"
        scenario_root.mkdir()
        result, model_output, github_output = _run_compile_live_model_scenario(
            scenario_root,
            monkeypatch,
            target=target,
        )

        assert model_output.is_file()
        assert github_output.is_file()
        model_document = cast(
            "dict[str, JsonValue]",
            json.loads(model_output.read_bytes()),
        )
        model_digest = canonical_sha256(model_document)
        output_lines = github_output.read_text(encoding="utf-8").splitlines()

        results.append(result)
        model_documents.append(model_document)
        output_lines_by_target.append(output_lines)
        assert output_lines == [
            f"repository-model-digest={model_digest}",
            (
                "repository-model-digest-hex="
                f"{model_digest.removeprefix('sha256:')}"
            ),
            f"execution-concurrency-key={expected_keys[index]}",
        ]

    captured = capsys.readouterr()
    actual_keys = tuple(
        output_lines[2].removeprefix("execution-concurrency-key=")
        for output_lines in output_lines_by_target
    )

    assert results == [0, 0]
    assert actual_keys == expected_keys
    assert actual_keys[0] != actual_keys[1]
    assert [
        model_document["context"]["target"]  # type: ignore[index]
        for model_document in model_documents
    ] == list(targets)
    assert [
        model_document["nbgv"]["canonical"]["gitCommitId"]  # type: ignore[index]
        for model_document in model_documents
    ] == list(targets)
    assert captured.out == ""
    assert captured.err == ""


def _live_eligibility_cli_arguments() -> list[str]:
    return [
        "release",
        "evaluate-live-eligibility",
        "--github-token",
        "test-token",
        "--workflow-run-id",
        "8101",
        "--run-attempt",
        "3",
        "--target",
        "e" * 40,
        "--intent",
        "intent.json",
        "--intent-digest",
        "sha256:" + ("1" * 64),
        "--intent-artifact-id",
        "101",
        "--intent-artifact-digest",
        "sha256:" + ("2" * 64),
        "--repository-model",
        "repository-model.json",
        "--repository-model-digest",
        "sha256:" + ("3" * 64),
        "--repository-model-artifact-id",
        "202",
        "--repository-model-artifact-digest",
        "sha256:" + ("4" * 64),
        "--output",
        "live-eligibility.json",
    ]


def test_live_eligibility_cli_omits_consumer_policy_input() -> None:
    """Expose only evaluator-owned static-reference acquisition."""
    arguments = cli_module._parser().parse_args(  # noqa: SLF001
        _live_eligibility_cli_arguments()
    )

    assert (
        arguments.handler
        is cli_module._release_evaluate_live_eligibility_command  # noqa: SLF001
    )
    assert arguments.target == "e" * 40
    assert arguments.repo_root == "."
    assert not hasattr(arguments, "consumer_policy")


def test_live_eligibility_cli_rejects_consumer_policy_option(
    capsys: pytest.CaptureFixture[str],
) -> None:
    """Reject the removed caller-supplied consumer-policy route."""
    with pytest.raises(SystemExit) as error:
        cli_module._parser().parse_args(  # noqa: SLF001
            [
                *_live_eligibility_cli_arguments(),
                "--consumer-policy",
                "obsolete.json",
            ]
        )

    captured = capsys.readouterr()
    assert error.value.code == ARGPARSE_ERROR
    assert captured.out == ""
    assert "unrecognized arguments: --consumer-policy obsolete.json" in (
        captured.err
    )


def test_live_eligibility_command_forwards_resolved_root_and_current_lineage(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Forward one resolved root and current lineage without policy input."""
    target = "e" * 40
    repository_argument = tmp_path / "alias" / ".." / "repository"
    resolved_repository_root = repository_argument.resolve()
    output_path = tmp_path / "live-eligibility.json"
    github_output_path = tmp_path / "github-output.txt"
    github_token = f"token-{target[:8]}"
    arguments = Namespace(
        repo_root=str(repository_argument),
        github_token=github_token,
        workflow_run_id=WORKFLOW_RUN_ID,
        run_attempt=3,
        target=target,
        output=str(output_path),
        github_output=str(github_output_path),
    )
    intent = SimpleNamespace(
        request_id="release-request-live-root-forwarding",
        selected_ref="refs/heads/release",
    )
    control = f"workflow-delivery-v3:{target}"
    snapshot = SimpleNamespace(context=SimpleNamespace(control=control))
    model_digest = "sha256:" + ("3" * 64)
    model = SimpleNamespace(
        canonical_digest=model_digest,
        snapshot=snapshot,
    )
    policy = SimpleNamespace(
        governance=SimpleNamespace(repository="owner/repository")
    )
    client = object()
    policy_digest = "sha256:" + ("5" * 64)
    static_catalog_digest = "sha256:" + ("6" * 64)
    decision_digest = "sha256:" + ("7" * 64)
    observed_at = datetime(2026, 9, 1, 10, 24, 3, tzinfo=UTC)
    decision_document: dict[str, JsonValue] = {
        "schema": "workflow-delivery/v3/live-eligibility-decision",
        "result": "pass",
        "diagnostics": [],
    }
    decision = SimpleNamespace(
        result="pass",
        decision_digest=decision_digest,
        to_document=lambda: decision_document,
    )
    calls = SimpleNamespace(
        intent=[],
        model=[],
        authoring=[],
        client=[],
        policy_digest=[],
        catalog_digest=[],
        timezone=[],
        evaluation=[],
        writes=[],
        outputs=[],
    )

    def evaluate(  # noqa: PLR0913
        context: object,
        actual_snapshot: object,
        actual_policy: object,
        actual_client: object,
        *,
        repository_root: Path,
        now: datetime,
    ) -> object:
        calls.evaluation.append(
            (
                context,
                actual_snapshot,
                actual_policy,
                actual_client,
                repository_root,
                now,
            )
        )
        return decision

    patches = {
        "_load_live_intent": lambda value: calls.intent.append(value) or intent,
        "_load_live_model": lambda value, current_intent: (
            calls.model.append((value, current_intent)) or model
        ),
        "load_first_slice_authoring": lambda root, requested_target: (
            calls.authoring.append((root, requested_target))
            or (object(), object(), policy)
        ),
        "GitHubGovernanceClient": lambda *, repository, token: (
            calls.client.append((repository, token)) or client
        ),
        "release_policy_digest": lambda value: (
            calls.policy_digest.append(value) or policy_digest
        ),
        "catalog_digest": lambda: (
            calls.catalog_digest.append(None) or static_catalog_digest
        ),
        "evaluate_live_eligibility": evaluate,
        "_write_output": lambda path, document: calls.writes.append(
            (path, document)
        ),
        "_record_outputs": (
            lambda path, *, role, digest, extra: calls.outputs.append(
                (path, role, digest, extra)
            )
        ),
    }
    for name, replacement in patches.items():
        monkeypatch.setattr(cli_module, name, replacement)
    monkeypatch.setattr(
        cli_module,
        "datetime",
        SimpleNamespace(
            now=lambda timezone: calls.timezone.append(timezone) or observed_at
        ),
    )

    result = cli_module._release_evaluate_live_eligibility_command(  # noqa: SLF001
        arguments
    )

    expected_context = cli_module.LiveEligibilityContext(
        purpose="live-release",
        request_id="release-request-live-root-forwarding",
        workflow_run_id=WORKFLOW_RUN_ID,
        selected_ref="refs/heads/release",
        target=target,
        repository_model_digest=model_digest,
        producer="evaluate-live-eligibility",
        control=control,
        release_policy_digest=policy_digest,
        catalog_digest=static_catalog_digest,
    )
    assert result == 0
    assert arguments.repo_root != "."
    assert not hasattr(arguments, "consumer_policy")
    assert calls.intent == [arguments]
    assert calls.model == [(arguments, intent)]
    assert calls.authoring == [(resolved_repository_root, target)]
    assert calls.evaluation == [
        (
            expected_context,
            snapshot,
            policy,
            client,
            resolved_repository_root,
            observed_at,
        )
    ]
    assert calls.evaluation[0][4] is calls.authoring[0][0]
    assert calls.client == [("owner/repository", github_token)]
    assert calls.policy_digest == [policy]
    assert calls.catalog_digest == [None]
    assert calls.timezone == [UTC]
    assert calls.writes == [(str(output_path), decision_document)]
    assert calls.outputs == [
        (
            str(github_output_path),
            "live-eligibility",
            decision_digest,
            (("live-result", "admitted"),),
        )
    ]
    assert not output_path.exists()
    assert not github_output_path.exists()
