"""Tests for the bounded Workflow Delivery v3 CLI."""

from __future__ import annotations

import hashlib
import json
import subprocess
import tomllib
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
from three_workflow_delivery_v3.ci.planner import (
    form_pull_request_candidate,
    form_slice_validation_candidate,
)
from three_workflow_delivery_v3.records.ci import (
    ci_qualification_snapshot_digest,
)
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

REPO_ROOT = Path(__file__).resolve().parents[5]
PACKAGE_ROOT = REPO_ROOT / "src/public/lib/three-workflow-delivery-v3"
FIXTURES = PACKAGE_ROOT / "tests/fixtures/release"
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
        ("git", "rev-parse", "HEAD"),
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
        toolchain=(("node", "v24.14.0"), ("pnpm", "11.17.0")),
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
    assert output["context"]["run-attempt"] == RUN_ATTEMPT
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


def test_validate_attestation_command_reports_disabled_fixture(
    capsys: pytest.CaptureFixture[str],
) -> None:
    """Validate the canonical fixture without creating activation state."""
    result = cli_module.main(
        [
            "release",
            "validate-attestation",
            "--document",
            str(FIXTURES / "governance-disabled.json"),
        ]
    )
    captured = capsys.readouterr()
    output = json.loads(captured.out)

    assert result == 0
    assert captured.err == ""
    assert output["schema"] == ("workflow-delivery/v3/governance-attestation")
    assert output["live_enabled"] is False
    assert output["release_policy"] == "hcoona-release-smoke-npm"
    assert output["package"] == "@hcoona/hcoona-release-smoke-npm"
    assert output["content-digest"].startswith("sha256:")


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
        run_attempt=RUN_ATTEMPT,
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
        ["repository", "plan"],
        ["npm", "observe"],
    ],
    ids=["publish", "repository-plan", "observation"],
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
            "Quality selection does not exist: "
            f"{PRODUCT_PATH}/workflow-delivery.quality.yml",
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
