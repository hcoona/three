"""In-memory contracts for Node facts and current-context admission."""

from __future__ import annotations

from dataclasses import replace
from typing import Any, cast

import pytest
from three_workflow_delivery_v3.canonical import canonical_sha256
from three_workflow_delivery_v3.catalogs import catalog_digest
from three_workflow_delivery_v3.repository.compiler import (
    CompilationContext,
    FactBundleAdmissionContext,
    ProviderRequestManifest,
    admit_node_provider_fact_bundle,
    first_slice_provider_manifest,
    provider_binding,
)
from three_workflow_delivery_v3.repository.node_provider import (
    CheckoutEvidence,
    GlobalInput,
    NbgvFacts,
    NodeProviderFactBundle,
    NodeProviderResult,
    ProjectNode,
    validate_nbgv_facts,
)

TARGET = "e" * 40
NPM_VERSION = "1.2.3-beta.42.ge123456"
DIGEST = "sha256:" + "a" * 64
PRODUCT_PATH = "src/public/lib/hcoona-release-smoke-npm"


def _nbgv_facts() -> NbgvFacts:
    return NbgvFacts(
        canonical_version="1.2.3",
        sem_ver1="1.2.3-beta-0042-e123456",
        sem_ver2=NPM_VERSION,
        version_height=42,
        git_commit_id=TARGET,
        public_release=False,
        npm_package_version=NPM_VERSION,
        node_api_result_digest=DIGEST,
    )


def _checkout_evidence() -> CheckoutEvidence:
    return CheckoutEvidence(
        target=TARGET,
        head=TARGET,
        shallow=False,
        ancestry_complete=True,
        tags_complete=True,
        credentials_persisted=False,
        authoritative_remote="origin",
        authoritative_remote_url="file:///authoritative-remote.git",
        tag_refspec="refs/tags/*:refs/tags/*",
    )


def _scenario() -> tuple[
    CompilationContext, ProviderRequestManifest, NodeProviderResult
]:
    context = CompilationContext(
        request_id="release-request-42",
        purpose="live-release",
        workflow_run_id=7101,
        run_attempt=None,
        target=TARGET,
        producer="compile-model",
        control=f"workflow-delivery-v3:{TARGET}",
        catalog_digest=catalog_digest(),
    )
    manifest = first_slice_provider_manifest(
        context, provider_producer="discover-node"
    )
    global_inputs = tuple(
        GlobalInput(
            path=path,
            content_digest=DIGEST,
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
    result = NodeProviderResult(
        binding=provider_binding(manifest, "node-first-slice"),
        provider_logical_id="node/pnpm-nbgv-v1",
        provider_implementation_id="three-workflow-delivery-v3/node-pnpm-nbgv-v1",
        execution_mode="target-evaluating",
        execution_class="target-evaluation/unprivileged-v1",
        toolchain=(("node", "v24.14.0"), ("pnpm", "11.21.0")),
        manifest_digest=DIGEST,
        configuration_digest=canonical_sha256(
            {
                "schema": "workflow-delivery/v3/node-provider-configuration",
                "global-inputs": [item.to_document() for item in global_inputs],
            }
        ),
        checkout=_checkout_evidence(),
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
        nbgv=_nbgv_facts(),
        unresolved=(),
        conflicts=(),
        outcome="success",
        diagnostic_reference=None,
    )
    return context, manifest, result


def _admission_inputs(
    manifest: ProviderRequestManifest,
    result: NodeProviderResult,
) -> tuple[NodeProviderFactBundle, FactBundleAdmissionContext]:
    # Leave validation to the admission boundary under test.
    bundle = NodeProviderFactBundle(
        schema="workflow-delivery/v3/node-provider-fact-bundle",
        binding=result.binding,
        manifest_digest=manifest.manifest_digest,
        manifest_entry_id="node-first-slice",
        request_artifact_id=101,
        request_artifact_digest=DIGEST,
        provider_result=result,
        provider_result_digest=result.result_digest,
        transport_id=202,
        transport_digest=DIGEST,
    )
    admission = FactBundleAdmissionContext(
        request_artifact_id=101,
        request_artifact_digest=DIGEST,
        transport_id=202,
        transport_digest=DIGEST,
        bundle_digest=bundle.bundle_digest,
    )
    return bundle, admission


def test_node_facts_admit_with_exact_target_and_native_version() -> None:
    """Admit the exact target and unchanged native projection."""
    context, manifest, result = _scenario()
    bundle, admission = _admission_inputs(manifest, result)

    admitted = admit_node_provider_fact_bundle(
        bundle,
        context=context,
        manifest=manifest,
        admission=admission,
    )

    assert admitted.provider_result == result
    assert admitted.bundle.bundle_digest == admission.bundle_digest
    assert admitted.provider_result.nbgv.git_commit_id == TARGET
    assert admitted.provider_result.nbgv.npm_package_version == NPM_VERSION


@pytest.mark.parametrize(
    ("field", "value"),
    [
        pytest.param("target", "d" * 40, id="target"),
        pytest.param("head", "d" * 40, id="head"),
        pytest.param("shallow", True, id="shallow"),
        pytest.param("ancestry_complete", False, id="ancestry"),
        pytest.param("tags_complete", False, id="tags"),
        pytest.param("credentials_persisted", True, id="credentials"),
        pytest.param("authoritative_remote", "upstream", id="remote"),
        pytest.param("authoritative_remote_url", "", id="remote-url"),
        pytest.param(
            "tag_refspec", "refs/tags/release/*:refs/tags/*", id="tag-refspec"
        ),
    ],
)
def test_fact_bundle_admission_requires_exact_full_history_checkout(
    field: str,
    value: object,
) -> None:
    """Reject incomplete or differently bound checkout evidence at admission."""
    context, manifest, result = _scenario()
    forged = replace(
        result,
        checkout=replace(result.checkout, **{field: cast("Any", value)}),
    )
    bundle, admission = _admission_inputs(manifest, forged)

    with pytest.raises(
        (TypeError, ValueError),
        match=(
            r"(?:full-history checkout evidence"
            r"|checkout authoritative_remote_url)"
        ),
    ):
        admit_node_provider_fact_bundle(
            bundle,
            context=context,
            manifest=manifest,
            admission=admission,
        )


@pytest.mark.parametrize(
    ("field", "value", "message"),
    [
        ("canonical_version", "", r"(?:canonical|NBGV.*version)"),
        ("canonical_version", "1", r"(?:canonical|NBGV.*version)"),
        ("canonical_version", "1.2.3.4.5", r"(?:canonical|NBGV.*version)"),
        ("canonical_version", "01.2.3", r"(?:canonical|NBGV.*version)"),
        ("canonical_version", "1.02.3", r"(?:canonical|NBGV.*version)"),
        ("canonical_version", "1.-2.3", r"(?:canonical|NBGV.*version)"),
        ("canonical_version", "1.two.3", r"(?:canonical|NBGV.*version)"),
        ("canonical_version", " 1.2.3", r"(?:canonical|NBGV.*version)"),
        ("sem_ver1", "", r"(?:semVer1|sem_ver1)"),
        ("sem_ver2", "", r"(?:semVer2|sem_ver2)"),
        (
            "npm_package_version",
            "",
            r"(?:npmPackageVersion|npm_package_version)",
        ),
        (
            "npm_package_version",
            "^1.2.3",
            r"(?:npmPackageVersion|npm_package_version)",
        ),
        (
            "npm_package_version",
            "latest",
            r"(?:npmPackageVersion|npm_package_version)",
        ),
        (
            "npm_package_version",
            "https://registry.npmjs.org/package",
            r"(?:npmPackageVersion|npm_package_version)",
        ),
        (
            "npm_package_version",
            "v1.2.3",
            r"(?:npmPackageVersion|npm_package_version)",
        ),
        (
            "npm_package_version",
            " 1.2.3",
            r"(?:npmPackageVersion|npm_package_version)",
        ),
        (
            "npm_package_version",
            "1.2",
            r"(?:npmPackageVersion|npm_package_version)",
        ),
        (
            "npm_package_version",
            "01.2.3",
            r"(?:npmPackageVersion|npm_package_version)",
        ),
        (
            "npm_package_version",
            "1.2.3-01",
            r"(?:npmPackageVersion|npm_package_version)",
        ),
        (
            "npm_package_version",
            "1.2.3+",
            r"(?:npmPackageVersion|npm_package_version)",
        ),
        (
            "npm_package_version",
            "1.2.3-alpha..1",
            r"(?:npmPackageVersion|npm_package_version)",
        ),
        (
            "git_commit_id",
            "e" * 39,
            r"(?:gitCommitId|git_commit_id|compilation target|exact target)",
        ),
        (
            "git_commit_id",
            "E" * 40,
            r"(?:gitCommitId|git_commit_id|compilation target|exact target)",
        ),
        (
            "git_commit_id",
            "g" * 40,
            r"(?:gitCommitId|git_commit_id|compilation target|exact target)",
        ),
        (
            "git_commit_id",
            ("e" * 40) + " ",
            r"(?:gitCommitId|git_commit_id|compilation target|exact target)",
        ),
        (
            "git_commit_id",
            "d" * 40,
            r"(?:gitCommitId|git_commit_id|compilation target|exact target)",
        ),
        ("version_height", 0, r"(?:versionHeight|version_height)"),
        ("version_height", -1, r"(?:versionHeight|version_height)"),
        (
            "node_api_result_digest",
            "a" * 64,
            r"(?:node-api-result-digest|node_api_result_digest|digest)",
        ),
        (
            "node_api_result_digest",
            "sha256:" + ("a" * 63),
            r"(?:node-api-result-digest|node_api_result_digest|digest)",
        ),
        (
            "node_api_result_digest",
            "sha256:" + ("a" * 65),
            r"(?:node-api-result-digest|node_api_result_digest|digest)",
        ),
        (
            "node_api_result_digest",
            "sha256:" + ("A" * 64),
            r"(?:node-api-result-digest|node_api_result_digest|digest)",
        ),
        (
            "node_api_result_digest",
            "sha256:" + ("g" * 64),
            r"(?:node-api-result-digest|node_api_result_digest|digest)",
        ),
        (
            "node_api_result_digest",
            " sha256:" + ("a" * 64),
            r"(?:node-api-result-digest|node_api_result_digest|digest)",
        ),
    ],
    ids=[
        "version-empty",
        "version-one-component",
        "version-five-components",
        "version-leading-zero-major",
        "version-leading-zero-minor",
        "version-negative-component",
        "version-nonnumeric-component",
        "version-whitespace-padded",
        "semver1-empty",
        "semver2-empty",
        "npm-empty",
        "npm-range",
        "npm-tag",
        "npm-url",
        "npm-v-prefixed",
        "npm-whitespace-padded",
        "npm-malformed",
        "npm-leading-zero-major",
        "npm-leading-zero-prerelease",
        "npm-empty-build",
        "npm-empty-prerelease-identifier",
        "git-short",
        "git-uppercase",
        "git-nonhex",
        "git-whitespace-padded",
        "git-target-mismatch",
        "height-zero",
        "height-negative",
        "digest-missing-prefix",
        "digest-short",
        "digest-long",
        "digest-uppercase",
        "digest-nonhex",
        "digest-whitespace",
    ],
)
def test_nbgv_facts_reject_malformed_values(
    field: str,
    value: object,
    message: str,
) -> None:
    """Reject invalid NBGV values and a different current target."""
    facts = replace(_nbgv_facts(), **{field: cast("Any", value)})

    with pytest.raises((TypeError, ValueError), match=message):
        validate_nbgv_facts(facts, target=TARGET)
