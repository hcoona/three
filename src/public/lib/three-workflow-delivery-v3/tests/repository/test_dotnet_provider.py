"""Immutable native fact admission contracts for the NuGet slice."""

import os
from dataclasses import replace

import pytest
from three_workflow_delivery_v3.adapters.dotnet import (
    BUILD_DEFINITION,
    DotnetPackageTargetWitness,
    dotnet_package_target_witness_from_document,
)
from three_workflow_delivery_v3.canonical import JsonValue
from three_workflow_delivery_v3.catalogs import catalog_digest
from three_workflow_delivery_v3.repository import compiler, dotnet_provider
from three_workflow_delivery_v3.repository.dotnet_provider import (
    DOTNET_RELEASE_UNIT,
    DotnetNbgvFacts,
    dotnet_nbgv_facts_from_document,
    neutral_dotnet_environment,
    validate_dotnet_nbgv_facts,
)
from three_workflow_delivery_v3.repository.node_provider import (
    AUTHORITATIVE_REMOTE,
    TAG_REFSPEC,
    CheckoutEvidence,
    GlobalInput,
)


def _facts() -> DotnetNbgvFacts:
    return DotnetNbgvFacts(
        "1.2.3",
        "1.2.3-beta-0007",
        "1.2.3-beta.7",
        7,
        "a" * 40,
        False,  # noqa: FBT003
        "1.2.3-beta.7",
        "1.2.0.0",
        "1.2.3.0",
        "1.2.3-beta.7+abc",
        "sha256:" + "b" * 64,
    )


def _admission_scenario():
    """Supply coherent immutable facts without claiming a real Git checkout."""
    target = "a" * 40
    context = compiler.CompilationContext(
        request_id="nuget-fact-admission-request",
        purpose="live-release",
        workflow_run_id=7101,
        run_attempt=None,
        target=target,
        producer="compile-nuget-model",
        control=f"workflow-delivery-v3:{target}",
        catalog_digest=catalog_digest(),
    )
    manifest = compiler.nuget_provider_manifest(
        context, provider_producer="discover-dotnet"
    )
    inputs = (
        ("Directory.Build.props", "sha256:" + "1" * 64),
        (dotnet_provider.DOTNET_ENTRY_POINT, "sha256:" + "2" * 64),
    )
    manifest_digest, configuration_digest = (
        dotnet_provider.dotnet_input_digests(inputs)
    )
    result = dotnet_provider.DotnetProviderResult(
        binding=compiler.provider_binding(
            manifest, manifest.requests[0].entry_id
        ),
        provider_logical_id=dotnet_provider.DOTNET_PROVIDER_LOGICAL_ID,
        provider_implementation_id=dotnet_provider.DOTNET_PROVIDER_IMPLEMENTATION_ID,
        execution_mode=dotnet_provider.DOTNET_PROVIDER_EXECUTION_MODE,
        execution_class=dotnet_provider.DOTNET_PROVIDER_EXECUTION_CLASS,
        toolchain=dotnet_provider.DOTNET_TOOLCHAIN,
        manifest_digest=manifest_digest,
        configuration_digest=configuration_digest,
        checkout=CheckoutEvidence(
            target=target,
            head=target,
            shallow=False,
            ancestry_complete=True,
            tags_complete=True,
            credentials_persisted=False,
            authoritative_remote=AUTHORITATIVE_REMOTE,
            authoritative_remote_url="file:///authoritative.git",
            tag_refspec=TAG_REFSPEC,
        ),
        project_nodes=(
            dotnet_provider.DotnetProjectNode(
                project_id=DOTNET_RELEASE_UNIT,
                package_name=dotnet_provider.DOTNET_PACKAGE,
                path=dotnet_provider.DOTNET_PROJECT_ROOT,
                manifest_path=dotnet_provider.DOTNET_ENTRY_POINT,
                target_framework="net10.0",
                packable=True,
                include_symbols=False,
                project_references=(),
                normalized_package_id=dotnet_provider.DOTNET_PACKAGE.lower(),
                normalized_version="1.2.3-beta.7",
            ),
        ),
        global_inputs=tuple(
            GlobalInput(path, digest, (DOTNET_RELEASE_UNIT,))
            for path, digest in inputs
        ),
        build_capabilities=("dotnet/nuget-package-v1",),
        nbgv=_facts(),
        unresolved=(),
        conflicts=(),
        outcome="success",
        diagnostic_reference=None,
        source_input_manifest=inputs,
        native_evaluation_digest="sha256:" + "3" * 64,
    )
    return context, manifest, result


def _bound_facts(manifest, result):
    """Bind the supplied result without validating its selected fact rules."""
    bundle = dotnet_provider.DotnetProviderFactBundle(
        schema=dotnet_provider.DOTNET_PROVIDER_FACT_BUNDLE_SCHEMA,
        binding=result.binding,
        provider_result=result,
        provider_result_digest=result.result_digest,
        manifest_digest=manifest.manifest_digest,
        manifest_entry_id=manifest.requests[0].entry_id,
        request_artifact_id=101,
        request_artifact_digest="sha256:" + "4" * 64,
        transport_id=202,
        transport_digest="sha256:" + "5" * 64,
    )
    admission = compiler.FactBundleAdmissionContext(
        request_artifact_id=bundle.request_artifact_id,
        request_artifact_digest=bundle.request_artifact_digest,
        transport_id=bundle.transport_id,
        transport_digest=bundle.transport_digest,
        bundle_digest=bundle.bundle_digest,
    )
    return bundle, admission


@pytest.mark.parametrize("field", ["source-input-manifest", "global-inputs"])
def test_dotnet_fact_admission_rejects_inconsistent_source_attribution(field):
    """Reject inconsistent supplied attribution during fact admission."""
    context, manifest, result = _admission_scenario()
    bundle, admission = _bound_facts(manifest, result)
    compiler.admit_dotnet_provider_fact_bundle(
        bundle, context=context, manifest=manifest, admission=admission
    )
    if field == "source-input-manifest":
        changed = replace(
            result,
            source_input_manifest=tuple(
                (
                    path,
                    "sha256:" + "6" * 64
                    if path == dotnet_provider.DOTNET_ENTRY_POINT
                    else digest,
                )
                for path, digest in result.source_input_manifest
            ),
        )
    else:
        changed = replace(result, global_inputs=result.global_inputs[:-1])
    bundle, admission = _bound_facts(manifest, changed)
    with pytest.raises(
        ValueError, match="input digests do not match source attribution"
    ):
        compiler.admit_dotnet_provider_fact_bundle(
            bundle, context=context, manifest=manifest, admission=admission
        )


@pytest.mark.parametrize(
    ("field", "value", "diagnostic"),
    [
        pytest.param(
            "target_framework", "net8.0", "closure", id="target-framework"
        ),
        pytest.param("include_symbols", True, "closure", id="symbols"),
        pytest.param("packable", False, "closure", id="not-packable"),
        pytest.param(
            "project_references",
            ("other.csproj",),
            "closure",
            id="project-references",
        ),
        pytest.param(
            "normalized_package_id", "other.package", "closure", id="package-id"
        ),
        pytest.param("normalized_version", "", "nonempty", id="empty-version"),
    ],
)
def test_dotnet_fact_admission_rejects_incomplete_native_scope(
    field, value, diagnostic
):
    """Reject expanded or missing project scope at supplied-fact admission."""
    context, manifest, result = _admission_scenario()
    bundle, admission = _bound_facts(manifest, result)
    compiler.admit_dotnet_provider_fact_bundle(
        bundle, context=context, manifest=manifest, admission=admission
    )
    project = replace(result.project_nodes[0], **{field: value})
    bundle, admission = _bound_facts(
        manifest, replace(result, project_nodes=(project,))
    )
    with pytest.raises(ValueError, match=diagnostic):
        compiler.admit_dotnet_provider_fact_bundle(
            bundle, context=context, manifest=manifest, admission=admission
        )


def test_dotnet_facts_and_witness_reject_substitution() -> None:
    """Native facts reject target and ecosystem substitutions."""
    facts = _facts()
    assert dotnet_nbgv_facts_from_document(facts.to_document()) == facts
    with pytest.raises(ValueError, match="target binding"):
        validate_dotnet_nbgv_facts(facts, target="c" * 40)
    witness = DotnetPackageTargetWitness(
        facts.git_commit_id,
        DOTNET_RELEASE_UNIT,
        facts,
        BUILD_DEFINITION,
        "sha256:" + "c" * 64,
        "sha256:" + "d" * 64,
        "release-simulation",
    )
    assert (
        dotnet_package_target_witness_from_document(witness.to_document())
        == witness
    )
    substituted = witness.to_document()
    substituted["release-unit"] = "hcoona-release-smoke-npm"
    with pytest.raises(ValueError, match="unsupported NuGet"):
        dotnet_package_target_witness_from_document(substituted)
    with pytest.raises(ValueError, match="unsupported NuGet"):
        _ = replace(witness, purpose="borrowed-approval").canonical_bytes
    native = facts.to_document()
    native["native"] = {"npmPackageVersion": facts.nuget_package_version}
    with pytest.raises(ValueError, match="schema"):
        dotnet_nbgv_facts_from_document(native)


@pytest.mark.parametrize("height", [True, -1, 2.0, "2"])
def test_dotnet_facts_reject_noncanonical_height(height: JsonValue) -> None:
    """Numeric equality cannot substitute a malformed native fact."""
    document = _facts().to_document()
    canonical = document["canonical"]
    assert isinstance(canonical, dict)
    canonical["versionHeight"] = height
    with pytest.raises(ValueError, match="canonical NBGV"):
        dotnet_nbgv_facts_from_document(document)


def test_native_environment_discards_authority_and_ambient_ref(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Target execution discards write credentials and CI ref selection."""
    for key in (
        "GITHUB_TOKEN",
        "GH_TOKEN",
        "GITHUB_REF",
        "GITHUB_SHA",
        "CI",
        "NUGET_AUTH_TOKEN",
        "NuGetPackageVersion",
    ):
        monkeypatch.setenv(key, "must-not-survive")
    monkeypatch.setenv("PATH", "/controlled/tools")
    environment = neutral_dotnet_environment()
    assert environment["PATH"] == "/controlled/tools"
    assert environment["IGNORE_GITHUB_REF"] == "true"
    assert "must-not-survive" not in environment.values()


@pytest.mark.parametrize(
    "roots",
    [
        {
            "ProgramFiles(x86)": r"C:\Program Files (x86)",
            "ProgramFiles": r"C:\Program Files",
        },
        {"programfiles": r"C:\Program Files"},
        {"ProgramFiles(x86)": "", "ProgramFiles": r"C:\Program Files"},
    ],
    ids=["mixed-case", "fallback-only", "empty-x86"],
)
def test_native_environment_preserves_windows_nuget_settings_roots(
    monkeypatch: pytest.MonkeyPatch, roots: dict[str, str]
) -> None:
    """NuGet's Windows settings roots survive the credential-free boundary."""
    directory_keys = {"PROGRAMFILES(X86)", "PROGRAMFILES"}
    for key in tuple(os.environ):
        if key.upper() in directory_keys:
            monkeypatch.delenv(key)
    for key, value in roots.items():
        monkeypatch.setenv(key, value)
    monkeypatch.setenv("GITHUB_TOKEN", "parent-only-token")
    monkeypatch.setenv("GITHUB_REF", "refs/heads/ambient")
    monkeypatch.setenv("IGNORE_GITHUB_REF", "false")
    parent_values = (
        os.environ["GITHUB_TOKEN"],
        os.environ["IGNORE_GITHUB_REF"],
    )

    environment = neutral_dotnet_environment()

    assert {
        key.upper(): value
        for key, value in environment.items()
        if key.upper() in directory_keys
    } == {key.upper(): value for key, value in roots.items()}
    assert "GITHUB_TOKEN" not in environment
    assert "GITHUB_REF" not in environment
    assert environment["IGNORE_GITHUB_REF"] == "true"
    assert (
        os.environ["GITHUB_TOKEN"],
        os.environ["IGNORE_GITHUB_REF"],
    ) == parent_values
