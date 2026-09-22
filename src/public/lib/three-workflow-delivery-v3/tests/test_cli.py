"""Tests for the bounded Workflow Delivery v3 CLI."""

from __future__ import annotations

import hashlib
import json
import os
import socket
import subprocess
import tomllib
from argparse import Namespace
from dataclasses import replace
from datetime import UTC, datetime, timedelta
from pathlib import Path
from types import SimpleNamespace
from typing import Self, cast
from urllib.request import Request

import pytest
from three_workflow_delivery_v3 import cli as cli_module
from three_workflow_delivery_v3.canonical import (
    JsonValue,
    canonical_sha256,
    canonicalize,
)
from three_workflow_delivery_v3.ci import planner as ci_planner
from three_workflow_delivery_v3.ci.evidence import (
    form_ci_evidence,
    form_empty_lane_result,
    form_evidence_lane_result,
)
from three_workflow_delivery_v3.ci.finalizer import finalize_ci_slice
from three_workflow_delivery_v3.ci.planner import (
    form_pull_request_candidate,
    form_slice_validation_candidate,
)
from three_workflow_delivery_v3.records.artifacts import ArtifactReference
from three_workflow_delivery_v3.records.ci import (
    CI_WORKFLOW_PATH,
    admit_ci_bootstrap_projection_decision_json,
    ci_evidence_digest,
    ci_qualification_snapshot_digest,
    ci_slice_summary_text,
)
from three_workflow_delivery_v3.records.release import (
    BuddyExecutionIdentity,
    ReleaseAttemptBinding,
    ReleaseAttemptIdentity,
    ReleaseIntent,
)
from three_workflow_delivery_v3.release import eligibility
from three_workflow_delivery_v3.release.identity import (
    OFFICIAL_SIMULATION_PRODUCER,
    normalize_official_simulation_intent,
)
from three_workflow_delivery_v3.repository import (
    AdmittedRepositoryModelSnapshot,
    CompilationContext,
    admit_repository_model_snapshot,
    first_slice_provider_manifest,
    provider_binding,
)
from three_workflow_delivery_v3.repository.descriptors import (
    FIRST_SLICE_POLICY_PATH,
    ReleasePolicy,
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

from .release import test_eligibility as eligibility_fixtures
from .release.conftest import (
    live_admitted_repository_model as live_admitted_repository_model,  # noqa: PLC0414
)
from .release.conftest import (
    live_intent as live_intent,  # noqa: PLC0414
)
from .release.conftest import (
    policy as policy,  # noqa: PLC0414
)
from .release.observation_fixtures import (
    active_transport,
    current_arguments,
    publication_authority_arguments,
    uploaded_arguments,
)
from .release.test_eligibility import RecordingGovernanceClient
from .release.test_observation_admission import NOW
from .release.test_observation_admission import (
    observation_case as observation_case,  # noqa: PLC0414
)

REPO_ROOT = Path(__file__).resolve().parents[5]
PACKAGE_ROOT = REPO_ROOT / "src/public/lib/three-workflow-delivery-v3"
PRODUCT_PATH = "src/public/lib/hcoona-release-smoke-npm"
WORKFLOW_RUN_ID = 8101
RUN_ATTEMPT = 2
ARGPARSE_ERROR = 2
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


def _github_output_values(path: Path) -> dict[str, str]:
    text = path.read_text(encoding="utf-8")
    assert text.endswith("\n")
    values: dict[str, str] = {}
    for assignment in text[:-1].split("\n"):
        name, separator, value = assignment.partition("=")
        assert name
        assert separator == "="
        assert name not in values
        values[name] = value
    return values


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
    assert set(output["build-definitions"]) == {
        "node/npm-package-v1",
        "dotnet/nuget-package-v1",
    }
    assert set(output["quality-presets"]) == {
        "node/hcoona-release-smoke-npm-v1",
        "dotnet/hcoona-release-smoke-github-packages-v1",
    }
    assert set(output["destination-definitions"]) == {
        "npm/github-packages-hcoona-three-v1",
        "npm/npmjs-public-v1",
        "nuget/github-packages-hcoona-three-v1",
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


def test_validate_attestation_command_reports_current_protected_governance(
    capsys: pytest.CaptureFixture[str],
) -> None:
    """Report the current protected contract without freezing its live flag."""
    governance_path = REPO_ROOT / (
        ".github/workflow-delivery/governance/hcoona-release-smoke-npm.json"
    )
    expected = json.loads(governance_path.read_bytes())
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
        "workflow-delivery/v3/normal-live-governance-attestation-v2"
    )
    assert output["live_enabled"] is expected["live_enabled"]
    assert output["activation"] == expected["activation"]
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


def test_retired_acceptance_command_rejects_before_effects(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
) -> None:
    """Reject before consuming credentials, running tools or writing output."""
    output = tmp_path / "acceptance.json"
    github_output = tmp_path / "github-output"
    token = "unused-test-token"  # noqa: S105
    monkeypatch.setenv("WDV3_ACCEPTANCE_GITHUB_TOKEN", token)

    def unexpected_effect(*_args: object, **_kwargs: object) -> None:
        pytest.fail("retired command attempted an external operation")

    monkeypatch.setattr(subprocess, "run", unexpected_effect)
    monkeypatch.setattr(subprocess, "Popen", unexpected_effect)
    monkeypatch.setattr(cli_module, "urlopen", unexpected_effect)

    with pytest.raises(SystemExit) as error:
        cli_module.main(
            [
                "governance",
                "run-fixed-acceptance-probe",
                "--suite",
                "absent-create-readback",
                "--package-coordinate",
                "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.1",
                "--target-sha",
                "a" * 40,
                "--output",
                str(output),
                "--github-output",
                str(github_output),
            ]
        )

    assert error.value.code == ARGPARSE_ERROR
    assert (
        "invalid choice: 'run-fixed-acceptance-probe'"
        in capsys.readouterr().err
    )
    assert os.environ["WDV3_ACCEPTANCE_GITHUB_TOKEN"] == token
    assert not output.exists()
    assert not github_output.exists()


@pytest.mark.parametrize(
    "selected_ref",
    [
        "refs/heads/contributor/arbitrary-buddy-source",
        "refs/tags/arbitrary-buddy-candidate",
    ],
    ids=["branch", "tag"],
)
def test_public_cli_normalizes_arbitrary_buddy_branch_and_tag_without_codeowners_gate(  # noqa: E501
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    selected_ref: str,
) -> None:
    """Preserve arbitrary refs through the public offline CLI boundary."""

    def unexpected_network(*_args: object, **_kwargs: object) -> None:
        message = "normalization attempted network access"
        raise AssertionError(message)

    monkeypatch.setattr(cli_module, "urlopen", unexpected_network)
    monkeypatch.setattr(socket, "create_connection", unexpected_network)
    output = tmp_path / "intent.json"
    target = "1234567890abcdef1234567890abcdef12345678"

    status = cli_module.main(
        [
            "release",
            "normalize-live-request",
            "--repository",
            "hcoona/three",
            "--selected-ref",
            selected_ref,
            "--target",
            target,
            "--actor",
            "commit9-test",
            "--workflow-run-id",
            "9009",
            "--run-attempt",
            "2",
            "--output",
            str(output),
        ]
    )
    intent = json.loads(output.read_bytes())

    assert status == 0
    assert {
        field: intent[field]
        for field in (
            "workflow-ref",
            "selected-ref",
            "workflow-sha",
            "target",
            "event-kind",
            "channel",
            "mode",
            "purpose",
        )
    } == {
        "workflow-ref": selected_ref,
        "selected-ref": selected_ref,
        "workflow-sha": target,
        "target": target,
        "event-kind": "workflow_dispatch",
        "channel": "buddy",
        "mode": "live",
        "purpose": "live-release",
    }
    assert selected_ref in output.read_text(encoding="utf-8")


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
                "--qualification-snapshot",
                "--release-artifact",
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
            "prepare-publication",
            (
                "--publication-snapshot",
                "--approval-bundle",
                "--reviewer-summary",
                "--publication-authorization",
                "--qualification-snapshot",
                "--runtime-directory",
                "--toolchain-directory",
                "--tarball",
            ),
        ),
        (
            "admit-publication-terminal",
            (
                "--terminal",
                "--terminal-artifact-url",
                "--terminal-payload-path",
            ),
        ),
        (
            "execute-publication",
            (
                "--reviewer-summary",
                "--publication-authorization",
                "--publication-terminal-reference",
                "--terminal-directory",
                "--output",
            ),
        ),
        (
            "resolve-publication-terminal",
            (
                "--publication-terminal-reference",
                "--publisher-conclusion",
                "--terminal-directory",
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
                "--exact-satisfied-finalization-proof",
                "--publication-terminal-reference",
                "--publication-step-outcome",
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
    missing_options = set(required_options).difference(help_text.split())
    assert not missing_options


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


def test_cli_commit8_live_outcome_status_mapping_is_closed() -> None:
    """Pin success versus every fail-closed terminal CLI status."""
    assert getattr(cli_module, "LIVE_OUTCOME_EXIT_STATUS", None) == {
        "exact-satisfied": 0,
        "published": 0,
        "failed-before-publication": 1,
        "publication-failed": 1,
        "unknown": 1,
    }


def test_cli_commit8_finalizer_exposes_platform_and_status_evidence_contract(
    capsys: pytest.CaptureFixture[str],
) -> None:
    """Expose explicit platform facts and both retained final status outputs."""
    with pytest.raises(SystemExit) as error:
        cli_module.main(["release", "finalize-live", "--help"])

    assert error.value.code == 0
    help_text = capsys.readouterr().out
    for option in (
        "--publication-terminal-reference",
        "--publication-step-outcome",
        "--publisher-conclusion",
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
                reviewer_summary_artifact_digest=(
                    "sha256:"
                    + hashlib.sha256(reviewer.read_bytes()).hexdigest()
                ),
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
    assert _github_output_values(tmp_path / f"{event_kind}-github-output") == {
        "plan-digest": canonical_sha256(plan),
        "plan-ready": "true",
        "root-hk-selected": str(selected_by_lane["root-hk"]).lower(),
        "project-build-selected": str(
            selected_by_lane["project-build"]
        ).lower(),
        "project-test-selected": str(selected_by_lane["project-test"]).lower(),
        "npm-artifact-build-selected": str(
            selected_by_lane["npm-artifact-build"]
        ).lower(),
    }
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
    assert _github_output_values(
        tmp_path / "workflow_dispatch-github-output"
    ) == {
        "plan-digest": plan_digest,
        "plan-ready": "false",
        "root-hk-selected": "false",
        "project-build-selected": "false",
        "project-test-selected": "false",
        "npm-artifact-build-selected": "false",
    }

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


def test_ci_bootstrap_projection_rejects_foreign_candidate_before_probe(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
) -> None:
    """Reject a coherent other-run Decision before Git or summary effects."""
    records = _blocked_pr_decision_fixture(tmp_path, monkeypatch)
    plan = cli_module._load_ci_plan(str(records[0]), records[1])  # noqa: SLF001
    candidate = replace(plan.candidate, workflow_run_id=WORKFLOW_RUN_ID + 1)
    obligations = ci_planner._form_obligations(  # noqa: SLF001
        candidate=candidate,
        repository_model_digest=plan.repository_model_digest,
        selected_lanes=(),
        scope_mode=plan.scope_mode,
        changed_paths=plan.changed_paths,
        selected_project_nodes=plan.selected_project_nodes,
        selected_release_units=plan.selected_release_units,
        selected_variants=plan.selected_variants,
        selected_outputs=plan.selected_outputs,
    )
    alternate_plan = replace(
        plan,
        candidate=candidate,
        workflow_run_id=candidate.workflow_run_id,
        obligations=obligations,
    )
    decision = finalize_ci_slice(
        alternate_plan,
        tuple(
            form_empty_lane_result(alternate_plan, lane_id=lane)
            for lane in cli_module.CI_LANE_IDS
        ),
        elapsed_seconds=EXPECTED_ELAPSED_SECONDS,
        supersession_state="not-superseded",
    )
    encoded = canonicalize(decision.to_document())
    assert (
        admit_ci_bootstrap_projection_decision_json(
            encoded, expected_plan=alternate_plan
        )
        == decision
    )
    records[2].write_bytes(encoded)
    records[3].write_bytes(canonicalize(decision.summary.to_document()))
    observed: list[tuple[object, ...]] = []

    def contains_path(*arguments: object) -> bool:
        observed.append(arguments)
        return False

    monkeypatch.setattr(cli_module, "_git_commit_contains_path", contains_path)
    github_summary = tmp_path / "github-summary.md"
    _write(github_summary, "existing note\n")
    arguments = _bootstrap_projection_arguments(
        records=records,
        github_summary=github_summary,
    )

    assert cli_module.main(arguments) == 1
    assert "trusted current candidate" in capsys.readouterr().err
    assert observed == []
    assert github_summary.read_text(encoding="utf-8") == "existing note\n"
    assert records[2].read_bytes() == encoded


def test_ci_bootstrap_projection_rejects_nonempty_evidence_before_probe(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
) -> None:
    """Keep a valid Finalizer Decision with Evidence outside bootstrap."""
    plan_path, _, document = _plan_fixture(
        tmp_path,
        monkeypatch,
        event_kind="pull_request",
        changed_paths=("docs/wiki/README.md",),
    )
    plan_digest = canonical_sha256(document)
    plan = cli_module._load_ci_plan(str(plan_path), plan_digest)  # noqa: SLF001
    obligation = next(item for item in plan.obligations if item.selected)
    assert obligation.lane_id == "root-hk"
    evidence = form_ci_evidence(
        plan,
        obligation=obligation,
        producer="root-hk",
        workflow_run_id=plan.workflow_run_id,
        run_attempt=plan.run_attempt,
        runner="ubuntu-24.04",
        raw_outcome="success",
        output_digests=("sha256:" + ("9" * 64),),
        diagnostics=("root-hk completed mechanically",),
    )
    decision = finalize_ci_slice(
        plan,
        tuple(
            form_evidence_lane_result(plan, evidence)
            if item.selected
            else form_empty_lane_result(plan, lane_id=item.lane_id)
            for item in plan.obligations
        ),
        elapsed_seconds=EXPECTED_ELAPSED_SECONDS,
        supersession_state="not-superseded",
    )
    assert decision.terminal_result == "success"
    assert decision.admitted_evidence_digests == (ci_evidence_digest(evidence),)
    assert decision.admitted_artifact_digests == ()
    decision_path = _write_canonical(
        tmp_path / "ready-decision.json", decision.to_document()
    )
    summary_path = _write_canonical(
        tmp_path / "ready-summary.json", decision.summary.to_document()
    )
    observed: list[tuple[object, ...]] = []

    def contains_path(*arguments: object) -> bool:
        observed.append(arguments)
        return False

    monkeypatch.setattr(cli_module, "_git_commit_contains_path", contains_path)
    github_summary = tmp_path / "github-summary.md"
    _write(github_summary, "existing note\n")
    arguments = _bootstrap_projection_arguments(
        records=(
            plan_path,
            plan_digest,
            decision_path,
            summary_path,
            tmp_path / "repo",
        ),
        github_summary=github_summary,
    )

    assert cli_module.main(arguments) == 1
    assert "must have no admitted Evidence" in capsys.readouterr().err
    assert observed == []
    assert github_summary.read_text(encoding="utf-8") == "existing note\n"


def test_ci_bootstrap_projection_rejects_artifact_lineage_before_probe(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
) -> None:
    """Reject artifact lineage independently of Evidence and summary rules."""
    records = _blocked_pr_decision_fixture(tmp_path, monkeypatch)
    plan = cli_module._load_ci_plan(str(records[0]), records[1])  # noqa: SLF001
    decision = admit_ci_bootstrap_projection_decision_json(
        records[2].read_bytes(), expected_plan=plan
    )
    artifact_digests = ("sha256:" + ("9" * 64),)
    summary_text = ci_slice_summary_text(
        candidate=decision.candidate,
        repository_model_digest=decision.repository_model_digest,
        plan_digest=decision.plan_digest,
        scope_mode=decision.scope_mode,
        changed_paths=decision.changed_paths,
        selected_project_nodes=decision.selected_project_nodes,
        selected_release_units=decision.selected_release_units,
        selected_variants=decision.selected_variants,
        selected_outputs=decision.selected_outputs,
        plan_diagnostics=decision.plan_diagnostics,
        dispositions=decision.obligation_dispositions,
        evidence_digests=decision.admitted_evidence_digests,
        artifact_digests=artifact_digests,
        explanation=decision.explanation,
        terminal_result=decision.terminal_result,
        failure_class=decision.failure_class,
        next_action=decision.next_action,
        elapsed_seconds=decision.elapsed_seconds,
        supersession_state=decision.supersession_state,
        supersession_reason=decision.supersession_reason,
        pr_slo=decision.pr_slo,
        pr_slo_reason=decision.pr_slo_reason,
    )
    decision = replace(
        decision,
        admitted_artifact_digests=artifact_digests,
        summary=replace(decision.summary, text=summary_text),
    )
    assert decision.admitted_evidence_digests == ()
    assert decision.admitted_artifact_digests == artifact_digests
    records[2].write_bytes(canonicalize(decision.to_document()))
    records[3].write_bytes(canonicalize(decision.summary.to_document()))
    observed: list[tuple[object, ...]] = []

    def contains_path(*arguments: object) -> bool:
        observed.append(arguments)
        return False

    monkeypatch.setattr(cli_module, "_git_commit_contains_path", contains_path)
    github_summary = tmp_path / "github-summary.md"
    _write(github_summary, "existing note\n")
    arguments = _bootstrap_projection_arguments(
        records=records,
        github_summary=github_summary,
    )

    assert cli_module.main(arguments) == 1
    assert "must have no admitted artifacts" in capsys.readouterr().err
    assert observed == []
    assert github_summary.read_text(encoding="utf-8") == "existing note\n"


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
    assert 0 < cast("int", observed["timeout"]) <= GITHUB_API_TIMEOUT_SECONDS
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
    assert _github_output_values(github_output) == {
        "approval-bundle-digest": bundle.bundle_digest,
        "approval-bundle-digest-hex": bundle.bundle_digest.removeprefix(
            "sha256:"
        ),
    }


@pytest.mark.parametrize(
    "command",
    [
        "form-publication-authorization",
        "prove-exact-satisfied",
    ],
)
def test_observation_authority_uses_fresh_governance_and_post_read_time(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    observation_case,
    command: str,
) -> None:
    """Close Observation lineage before refreshing either authority path."""
    case = observation_case
    action = command == "form-publication-authorization"
    arguments = publication_authority_arguments(
        tmp_path,
        case,
        monkeypatch,
        classification="absent" if action else "exact-satisfied",
    )
    instant = [NOW + timedelta(seconds=1)]

    class Clock(datetime):
        @classmethod
        def now(cls, tz=None):
            assert tz is UTC
            return instant[0]

    client = RecordingGovernanceClient(
        canonicalize(case.eligibility.governance.attestation.to_document())
    )
    read_source = client.read_source

    def finish_read(*args, **kwargs):
        result = read_source(*args, **kwargs)
        instant[0] += timedelta(seconds=1)
        return result

    reads = []
    verify_payload = cli_module._verify_uploaded_payload  # noqa: SLF001

    def record_read(path, **kwargs):
        reads.append(Path(path).name)
        return verify_payload(path, **kwargs)

    monkeypatch.setattr(client, "read_source", finish_read)
    monkeypatch.setattr(cli_module, "datetime", Clock)
    monkeypatch.setattr(
        cli_module, "GitHubGovernanceClient", lambda **_kwargs: client
    )
    monkeypatch.setattr(cli_module, "_verify_uploaded_payload", record_read)
    if not action:
        transport = active_transport(case)
        get = transport.get

        def remote_read(*args, **kwargs):
            response = get(*args, **kwargs)
            instant[0] += timedelta(seconds=1)
            return response

        monkeypatch.setattr(transport, "get", remote_read)
        monkeypatch.setattr(
            cli_module, "GitHubPackagesHttpTransport", lambda: transport
        )
    output = tmp_path / "authority.json"
    github_output = tmp_path / "github-output"
    assert (
        cli_module.main(
            [
                "release",
                command,
                *current_arguments(case),
                *arguments,
                *(
                    ["--approval-boundary-sentinel-result", "success"]
                    if action
                    else []
                ),
                "--github-token",
                "test-only-token",
                "--control",
                case.eligibility.context.control,
                "--output",
                str(output),
                "--github-output",
                str(github_output),
            ]
        )
        == 0
    )
    document = json.loads(output.read_bytes())
    timestamp = "completed-at" if action else "proved-at"
    assert datetime.fromisoformat(document[timestamp]) == instant[0]
    governance_observed_at = document["governance-proof"]["observed-at"]
    assert governance_observed_at == "2026-08-06T12:00:01Z"
    assert client.calls == [
        ("protected", "hcoona/three", "refs/heads/main"),
        (
            "read",
            "hcoona/three",
            "refs/heads/main",
            case.policy.governance.path,
            dict(case.attempt_binding.attestation_provenance)[
                "eligibility-main-sha"
            ],
        ),
    ]
    expected_reads = {
        "intent.json",
        "repository-model.json",
        "live-eligibility.json",
        "attempt-binding.json",
        "qualification-snapshot.json",
        "qualification-decision.json",
        "release-artifact.json",
        "observation.json",
        "publication-snapshot.json",
    }
    if action:
        expected_reads |= {"approval-bundle.json", "reviewer-summary.md"}
    else:
        expected_reads.add("adapter-context.json")
        reference = document["publication-snapshot-reference"]
        assert reference == {
            "artifact-id": 109,
            "artifact-digest": canonical_sha256(
                json.loads(
                    (tmp_path / "publication-snapshot.json").read_bytes()
                )
            ),
            "artifact-url": f"https://github.com/hcoona/three/actions/runs/{case.intent.workflow_run_id}/artifacts/109",
            "payload-path": "publication-snapshot.json",
            "payload-digest": canonical_sha256(
                json.loads(
                    (tmp_path / "publication-snapshot.json").read_bytes()
                )
            ),
        }
        assert (
            document["exact-version-readback"]["content-sha256"]
            == case.artifact.content.content_sha256
        )
        assert (
            document["exact-version-readback"]["content-sha512"]
            == case.artifact.content.content_sha512
        )
        assert (
            document["package-control-proof"]["observed-at"]
            == "2026-08-06T12:00:02Z"
        )
    assert set(reads) == expected_reads
    assert len(reads) == len(expected_reads)
    role = (
        "publication-authorization"
        if action
        else "exact-satisfied-finalization-proof"
    )
    assert (
        f"{role}-digest={canonical_sha256(document)}\n"
        in github_output.read_text()
    )


@pytest.mark.parametrize(
    ("command", "substitution"),
    [
        ("form-publication-authorization", "intent-digest"),
        ("form-publication-authorization", "repository-model"),
        ("form-publication-authorization", "eligibility-transport"),
        ("form-publication-authorization", "decision-reference"),
        ("prove-exact-satisfied", "provenance"),
        ("prove-exact-satisfied", "decision-reference"),
    ],
)
def test_observation_authority_rejects_substitution_before_fresh_governance(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    observation_case,
    command: str,
    substitution: str,
) -> None:
    """Reject substituted canonical lineage without a fresh Governance read."""
    case = observation_case
    action = command == "form-publication-authorization"
    arguments = publication_authority_arguments(
        tmp_path,
        case,
        monkeypatch,
        classification="absent" if action else "exact-satisfied",
    )
    expected = case.attempt_binding
    if substitution == "intent-digest":
        actual = replace(expected, intent_digest="sha256:" + ("e" * 64))
    elif substitution == "repository-model":
        actual = replace(
            expected,
            repository_model_digest="sha256:" + ("e" * 64),
        )
    elif substitution == "eligibility-transport":
        actual = replace(
            expected,
            live_eligibility_artifact_id=(
                expected.live_eligibility_artifact_id + 1
            ),
        )
    elif substitution == "provenance":
        actual = replace(
            expected,
            attestation_provenance=tuple(
                (name, "9" * 40 if name == "blob-oid" else value)
                for name, value in expected.attestation_provenance
            ),
        )
    else:
        actual = expected
        index = arguments.index("--qualification-decision-artifact-url") + 1
        arguments[index] += "-substituted"
    arguments += uploaded_arguments(
        tmp_path / "substituted",
        "attempt_binding",
        actual.to_document(),
        901,
    )
    client = RecordingGovernanceClient(
        canonicalize(case.eligibility.governance.attestation.to_document())
    )
    monkeypatch.setattr(
        cli_module, "GitHubGovernanceClient", lambda **_kwargs: client
    )

    output = tmp_path / "authority.json"
    assert (
        cli_module.main(
            [
                "release",
                command,
                *current_arguments(case),
                *arguments,
                *(
                    ["--approval-boundary-sentinel-result", "success"]
                    if action
                    else []
                ),
                "--github-token",
                "test-only-token",
                "--control",
                case.eligibility.context.control,
                "--output",
                str(output),
            ]
        )
        == 1
    )
    assert not output.exists()
    assert client.calls == []


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


def _run_compile_live_model_scenario(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    *,
    target: str = "a" * 40,
    malformed_authoring: str | None = None,
    provider_mutation: tuple[tuple[str, str], JsonValue] | None = None,
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
    if provider_mutation is not None:
        (section, field), value = provider_mutation
        cast("dict[str, JsonValue]", provider_document[section])[field] = value
    result_digest = canonical_sha256(provider_document)
    provider_document["provider-request-manifest-digest"] = (
        manifest.manifest_digest
    )
    provider_document["result-digest"] = result_digest
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
    output_values = _github_output_values(github_output)

    assert result == 0
    assert captured.out == ""
    assert captured.err == ""
    assert admitted.snapshot.ready is True
    assert output_values == {
        "repository-model-digest": admitted.canonical_digest,
        "repository-model-digest-hex": admitted.canonical_digest.removeprefix(
            "sha256:"
        ),
        "execution-concurrency-key": (
            "a71c896702fc7f6869d6dc6714840eba7393c9e98eaf820d3254299d664534a6"
        ),
    }
    assert "sha256:" not in output_values["execution-concurrency-key"]


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


@pytest.mark.parametrize(
    ("path", "message"),
    [
        pytest.param(
            ("checkout", "authoritative-remote-url"),
            "remote URL must be a string",
            id="boolean-remote-url",
        ),
        pytest.param(
            ("binding", "workflow-run-id"),
            "run must be an integer",
            id="boolean-run",
        ),
    ],
)
def test_compile_live_model_rejects_malformed_provider_primitives(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
    path: tuple[str, str],
    message: str,
) -> None:
    """Reject malformed uploaded Provider fields before Model output."""
    result, model_output, github_output = _run_compile_live_model_scenario(
        tmp_path,
        monkeypatch,
        provider_mutation=(path, True),
    )
    captured = capsys.readouterr()

    assert result == 1
    assert captured.out == ""
    assert message in captured.err
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
    output_values_by_target: list[dict[str, str]] = []

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
        output_values = _github_output_values(github_output)

        results.append(result)
        model_documents.append(model_document)
        output_values_by_target.append(output_values)
        assert output_values == {
            "repository-model-digest": model_digest,
            "repository-model-digest-hex": model_digest.removeprefix("sha256:"),
            "execution-concurrency-key": expected_keys[index],
        }

    captured = capsys.readouterr()
    actual_keys = tuple(
        output_values["execution-concurrency-key"]
        for output_values in output_values_by_target
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
    assert "--consumer-policy" in captured.err


@pytest.mark.parametrize("enabled", [True, False], ids=["admitted", "blocked"])
def test_live_eligibility_command_persists_current_decision(  # noqa: PLR0913
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    live_intent: ReleaseIntent,
    live_admitted_repository_model: AdmittedRepositoryModelSnapshot,
    policy: ReleasePolicy,
    *,
    enabled: bool,
) -> None:
    """Persist current input admission and both workflow-facing outcomes."""
    model = live_admitted_repository_model
    target = live_intent.target
    repository_argument = tmp_path / "alias" / ".." / "repository"
    resolved_root = repository_argument.resolve()
    output_path = tmp_path / "live-eligibility.json"
    github_output = tmp_path / "github-output.txt"
    observed_at = eligibility_fixtures.NOW
    client = RecordingGovernanceClient(
        eligibility_fixtures._attestation_content(live_enabled=enabled),  # noqa: SLF001
    )
    static_result = eligibility_fixtures._static_reference(target=target)  # noqa: SLF001
    authoring_reads: list[tuple[Path, str]] = []
    scans: list[tuple[Path, str, str]] = []

    def authoring(root: Path, selected_target: str):
        authoring_reads.append((root, selected_target))
        return None, None, policy

    def scan(root: Path, *, source_kind: str, target: str):
        scans.append((root, source_kind, target))
        return static_result

    def governance_client(*, repository: str, token: str):
        assert repository == policy.governance.repository
        assert token == "test-token"  # noqa: S105
        return client

    class Clock(datetime):
        @classmethod
        def now(cls, tz=None):
            return observed_at if tz is None else observed_at.astimezone(tz)

    monkeypatch.setattr(cli_module, "datetime", Clock)
    monkeypatch.setattr(cli_module, "load_first_slice_authoring", authoring)
    monkeypatch.setattr(cli_module, "GitHubGovernanceClient", governance_client)
    monkeypatch.setattr(eligibility, "scan_bounded_static_references", scan)
    if enabled:
        eligibility_fixtures._admit_test_destination_primitive(monkeypatch)  # noqa: SLF001

    result = cli_module.main(
        [
            "release",
            "evaluate-live-eligibility",
            "--github-token",
            "test-token",
            "--workflow-run-id",
            str(live_intent.workflow_run_id),
            "--run-attempt",
            "1",
            "--target",
            target,
            "--repo-root",
            str(repository_argument),
            *uploaded_arguments(
                tmp_path / "inputs", "intent", live_intent.to_document(), 101
            ),
            *uploaded_arguments(
                tmp_path / "inputs",
                "repository-model",
                model.snapshot.to_document(),
                202,
            ),
            "--output",
            str(output_path),
            "--github-output",
            str(github_output),
        ]
    )

    content = output_path.read_bytes()
    document = json.loads(content)
    assert content == canonicalize(document)
    assert result == (0 if enabled else 1)
    assert (
        document["schema"] == "workflow-delivery/v3/live-eligibility-decision"
    )
    assert document["result"] == ("pass" if enabled else "blocked")
    assert document["diagnostics"] == (
        [] if enabled else ["governance-live-disabled"]
    )
    assert document["context"] == {
        "purpose": "live-release",
        "request-id": live_intent.request_id,
        "workflow-run-id": live_intent.workflow_run_id,
        "selected-ref": live_intent.selected_ref,
        "target": target,
        "repository-model-digest": model.canonical_digest,
        "producer": "evaluate-live-eligibility",
        "control": model.snapshot.context.control,
        "release-policy-digest": cli_module.release_policy_digest(policy),
        "catalog-digest": cli_module.catalog_digest(),
    }
    assert document["static-reference"] == static_result.to_document()
    assert document["governance"]["observed-at"] == "2026-08-06T12:00:00Z"
    assert (
        document["governance"]["admitted-attestation"]["live_enabled"]
        is enabled
    )
    digest = hashlib.sha256(content).hexdigest()
    assert github_output.read_text().splitlines() == [
        f"live-eligibility-digest=sha256:{digest}",
        f"live-eligibility-digest-hex={digest}",
        f"live-result={'admitted' if enabled else 'blocked'}",
    ]
    assert authoring_reads == [(resolved_root, target)]
    assert scans == [(resolved_root, "git-target", target)]
