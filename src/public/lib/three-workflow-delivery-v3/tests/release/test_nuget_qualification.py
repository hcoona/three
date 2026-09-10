"""Native Release contracts with modeled mechanics and real Git admission."""

from __future__ import annotations

# Modeled transport and eligibility records here never grant external authority.
# ruff: noqa: D103
import hashlib
from dataclasses import dataclass, replace
from typing import TYPE_CHECKING

import pytest
from three_workflow_delivery_v3.adapters import dotnet as native
from three_workflow_delivery_v3.canonical import (
    canonical_sha256,
    canonicalize,
    parse_canonical_json,
)
from three_workflow_delivery_v3.catalogs import catalog_digest
from three_workflow_delivery_v3.records.artifacts import (
    ArtifactTransportIdentity,
)
from three_workflow_delivery_v3.records.release import (
    ArtifactVariantIdentity,
    BuddyExecutionIdentity,
    DestinationProjection,
    NugetExternalPackageCoordinate,
    NugetPackageIdentity,
    NugetReleaseArtifact,
    NugetReleaseBuildRequest,
    QualificationSnapshot,
    ReleaseArtifact,
    ReleaseAttemptBinding,
    ReleaseAttemptIdentity,
    ReleaseBuildIdentity,
    ReleaseIntent,
    ReleaseOutputIdentity,
    admit_release_record,
    publication_mutable_resource_keys,
    publication_serialization_projection,
    release_artifact_transport_name,
)
from three_workflow_delivery_v3.records.release_transport import (
    ReleaseAdmissionBindings,
)
from three_workflow_delivery_v3.release.finalizer import finalize_qualification
from three_workflow_delivery_v3.release.nuget_planner import (
    NUGET_BUILD_OBLIGATION,
    NUGET_CONSUMER_OBLIGATION,
    NUGET_CONTENTS_OBLIGATION,
    plan_nuget_live_qualification,
)
from three_workflow_delivery_v3.release.nuget_qualification import (
    execute_nuget_release_build,
    form_uploaded_nuget_release_artifact,
    qualify_release_nuget_consumer,
    qualify_release_nuget_contents,
)
from three_workflow_delivery_v3.release.qualification import (
    validate_qualification_artifacts,
)
from three_workflow_delivery_v3.repository import compiler
from three_workflow_delivery_v3.repository.descriptors import (
    NUGET_GOVERNANCE_PATH,
)
from three_workflow_delivery_v3.repository.dotnet_provider import (
    DOTNET_TOOLCHAIN,
    NativeNuGetHelper,
    provide_dotnet_repository_facts,
)
from three_workflow_delivery_v3.repository.node_provider import (
    CheckoutMaterialization,
)

from ..adapters.test_dotnet import (
    frozen_package as frozen_package,  # noqa: PLC0414
)
from ..adapters.test_dotnet import (
    native_helper as native_helper,  # noqa: PLC0414
)
from ..repository.test_dotnet_compiler import (
    _admitted,
)
from ..repository.test_dotnet_compiler import (
    native_scenario as native_scenario,  # noqa: PLC0414
)

if TYPE_CHECKING:
    from pathlib import Path

DIGEST = "sha256:" + "a" * 64
OTHER_DIGEST = "sha256:" + "b" * 64


@dataclass(frozen=True)
class NugetScenario:
    """Admitted native facts with modeled immutable transport and mechanics."""

    intent: ReleaseIntent
    binding: ReleaseAttemptBinding
    model: compiler.AdmittedRepositoryModelSnapshot
    provider: compiler.AdmittedDotnetProviderFactBundle
    snapshot: QualificationSnapshot
    request: native.DotnetBuildRequest
    result: native.DotnetBuildResult
    helper: NativeNuGetHelper


def _intent_and_binding(context, model):
    intent = ReleaseIntent(
        repository="hcoona/three",
        workflow_path=".github/workflows/nuget-qualification-fixture.yml",
        workflow_ref="refs/heads/main",
        workflow_sha=context.target,
        request_id=context.request_id,
        actor="hcoona",
        workflow_run_id=context.workflow_run_id,
        event_kind="workflow_dispatch",
        selected_ref="refs/heads/main",
        target=context.target,
        channel="buddy",
        mode="live",
        purpose="live-release",
        release_unit="hcoona-release-smoke-github-packages",
    )
    execution = BuddyExecutionIdentity(
        "buddy", intent.release_unit, intent.target
    )
    provenance = tuple(
        sorted(
            {
                "repository": "hcoona/three",
                "ref": "refs/heads/main",
                "path": NUGET_GOVERNANCE_PATH,
                "eligibility-main-sha": context.target,
                "git-object-format": "sha1",
                "blob-oid": "d" * 40,
                "canonical-content-digest": DIGEST,
            }.items()
        )
    )
    binding = ReleaseAttemptBinding(
        intent_digest=intent.intent_digest,
        request_id=intent.request_id,
        execution=execution,
        attempt=ReleaseAttemptIdentity(execution, intent.workflow_run_id),
        repository_model_digest=model.canonical_digest,
        live_eligibility_artifact_id=301,
        live_eligibility_artifact_digest=DIGEST,
        live_eligibility_payload_digest=OTHER_DIGEST,
        attestation_provenance=provenance,
    )
    return intent, binding


def _witness(snapshot, model):
    return native.DotnetPackageTargetWitness(
        target=snapshot.target,
        release_unit=snapshot.release_unit,
        nbgv=snapshot.nbgv,
        build_definition="dotnet/nuget-package-v1",
        catalog_digest=model.snapshot.context.catalog_digest,
        control_digest=canonical_sha256(
            {
                "schema": "workflow-delivery/v3/control-identity",
                "identity": model.snapshot.context.control,
            }
        ),
        purpose="live-release",
    )


def _plan_from_provider(repo, context, manifest, result):
    provider = _admitted(context, manifest, result)
    repository = compiler.compile_dotnet_repository_model(
        repo, context, manifest, [provider]
    )
    model = compiler.admit_repository_model_snapshot(
        canonicalize(repository.to_document()),
        expected_context=context,
        expected_digest=repository.snapshot_digest,
    )
    intent, binding = _intent_and_binding(context, model)
    snapshot = plan_nuget_live_qualification(intent, binding, model, provider)
    return intent, binding, model, provider, snapshot


@pytest.fixture
def nuget_scenario(native_scenario, tmp_path: Path) -> NugetScenario:
    repo, old_context, _, old_result = native_scenario
    context = replace(old_context, request_id="release-request:" + "4" * 64)
    manifest = compiler.nuget_provider_manifest(
        context, provider_producer="discover-dotnet"
    )
    result = replace(
        old_result,
        binding=compiler.provider_binding(manifest, "dotnet-nuget-slice"),
    )
    intent, binding, model, provider, snapshot = _plan_from_provider(
        repo, context, manifest, result
    )
    contract = snapshot.build_requests[0]
    witness = _witness(snapshot, model)
    helper = NativeNuGetHelper(tmp_path / "unexecuted-native-helper.dll")
    request = native.DotnetBuildRequest(
        repo,
        contract.declared_inputs,
        contract.source_input_manifest,
        witness,
        helper,
        tmp_path / "build-evidence",
    )
    identity = snapshot.destination_projections[0].coordinate.identity
    expectation = native.DotnetArtifactExpectation(
        identity.package_name,
        identity.native_version,
        identity.normalized_package_id,
        identity.normalized_version,
        witness.canonical_bytes,
    )
    package = b"modeled package bytes for binding tests"
    build_result = native.DotnetBuildResult(
        package,
        native.DotnetArtifactManifest(
            "Hcoona.ReleaseSmoke.GithubPackages.1.2.3-beta.42.nupkg",
            ("lib/net10.0/Smoke.dll", "workflow-delivery/provenance.json"),
            "sha256:" + hashlib.sha256(package).hexdigest(),
            "sha512:" + hashlib.sha512(package).hexdigest(),
            len(package),
        ),
        expectation,
        witness.canonical_bytes,
        contract.source_input_manifest,
        DOTNET_TOOLCHAIN,
    )
    return NugetScenario(
        intent,
        binding,
        model,
        provider,
        snapshot,
        request,
        build_result,
        helper,
    )


def _transport(snapshot):
    return ArtifactTransportIdentity(
        artifact_id=401,
        artifact_name=release_artifact_transport_name(
            repository=snapshot.repository,
            purpose="live-release",
            output=snapshot.outputs[0],
            qualification_snapshot_digest=snapshot.snapshot_digest,
            workflow_run_id=snapshot.subject.workflow_run_id,
            run_attempt=None,
            producer="build-nuget-package",
        ),
        artifact_url=f"https://github.com/hcoona/three/actions/runs/{snapshot.subject.workflow_run_id}/artifacts/401",
        transport_digest=OTHER_DIGEST,
        producer="build-nuget-package",
        workflow_run_id=snapshot.subject.workflow_run_id,
        run_attempt=None,
    )


def _qualified(scenario, monkeypatch, tmp_path):
    calls = []

    def build(request):
        assert request is scenario.request
        calls.append("build")
        return scenario.result

    def contents(package, expectation, helper):
        assert package is scenario.result.package
        assert expectation == scenario.result.expectation
        assert helper is scenario.helper
        calls.append("contents")
        return scenario.result.manifest

    def consumer(package, expectation, helper, *, evidence_directory):
        assert package is scenario.result.package
        assert expectation == scenario.result.expectation
        assert helper is scenario.helper
        assert evidence_directory == tmp_path / "consumer"
        calls.append("consumer")
        return native.DotnetConsumerResult(
            scenario.snapshot.release_unit,
            scenario.snapshot.build_requests[0].witness_digest,
            scenario.result.manifest.sha256,
            expectation.normalized_package_id,
            expectation.normalized_version,
        )

    monkeypatch.setattr(native, "build_dotnet_package", build)
    monkeypatch.setattr(native, "qualify_nuget_artifact_contents", contents)
    monkeypatch.setattr(native, "qualify_nuget_restore_build_invoke", consumer)
    mechanics, failure = execute_nuget_release_build(
        scenario.snapshot, scenario.request
    )
    assert failure is None
    assert mechanics.result.package is scenario.result.package
    artifact, build_evidence = form_uploaded_nuget_release_artifact(
        scenario.snapshot, mechanics, _transport(scenario.snapshot)
    )
    content_evidence = qualify_release_nuget_contents(
        scenario.snapshot,
        artifact,
        scenario.result.package,
        scenario.result.expectation,
        scenario.helper,
    )
    consumer_evidence = qualify_release_nuget_consumer(
        scenario.snapshot,
        artifact,
        scenario.result.package,
        scenario.result.expectation,
        scenario.helper,
        evidence_directory=tmp_path / "consumer",
    )
    return (
        artifact,
        (build_evidence, content_evidence, consumer_evidence),
        calls,
    )


def test_nuget_plan_freezes_native_inputs_and_separate_obligations(
    nuget_scenario,
):
    scenario = nuget_scenario
    snapshot = scenario.snapshot
    request = snapshot.build_requests[0]
    assert type(request) is NugetReleaseBuildRequest
    assert (
        request.source_input_manifest
        == scenario.provider.provider_result.source_input_manifest
    )
    assert (
        request.nuget_package_version
        == scenario.provider.provider_result.nbgv.nuget_package_version
    )
    assert "npm-package-version" not in request.to_document()
    assert tuple(item.obligation_id for item in snapshot.obligations) == (
        NUGET_BUILD_OBLIGATION,
        NUGET_CONTENTS_OBLIGATION,
        NUGET_CONSUMER_OBLIGATION,
    )
    assert all(item.runner == "windows-latest" for item in snapshot.obligations)
    assert all(
        item.dimensions == (("framework", "net10.0"), ("os", "windows"))
        for item in snapshot.obligations
    )
    assert all(
        item.prerequisites == (NUGET_BUILD_OBLIGATION,)
        for item in snapshot.obligations[1:]
    )
    assert snapshot.channel == "buddy"
    keys = publication_mutable_resource_keys(
        snapshot.destination_projections[0]
    )
    assert len(keys) == 1
    assert keys[0].startswith("external-package-coordinate:")
    assert snapshot.potential_actions[0].mutable_resource_key_basis == (
        "external-package-coordinate",
    )
    assert (
        admit_release_record(
            canonicalize(snapshot.to_document()),
            expected_type=QualificationSnapshot,
            expected_digest=snapshot.snapshot_digest,
        )
        == snapshot
    )


def test_nuget_qualification_retains_original_artifact_bytes(
    nuget_scenario, monkeypatch, tmp_path
):
    artifact, evidence, calls = _qualified(
        nuget_scenario, monkeypatch, tmp_path
    )
    assert calls == ["build", "contents", "consumer"]
    assert type(artifact) is NugetReleaseArtifact
    assert "lifecycle-scripts" not in artifact.to_document()
    assert artifact.transport.producer == "build-nuget-package"
    assert artifact.transport.artifact_name.endswith(".nupkg")
    assert (
        artifact.content.content_sha256 == nuget_scenario.result.manifest.sha256
    )
    assert (
        artifact.content.content_sha512 == nuget_scenario.result.manifest.sha512
    )
    decision = finalize_qualification(
        nuget_scenario.snapshot, evidence, (artifact,)
    )
    assert decision.terminal_result == "success"
    assert decision.admitted_artifact_digests == (artifact.artifact_digest,)
    assert decision.admitted_evidence_digests == tuple(
        item.evidence_digest for item in evidence
    )
    assert (
        admit_release_record(
            canonicalize(artifact.to_document()),
            expected_type=NugetReleaseArtifact,
            expected_digest=artifact.artifact_digest,
            expected_bindings=ReleaseAdmissionBindings(
                "live-release",
                artifact.transport.workflow_run_id,
                None,
                artifact.target,
                "build-nuget-package",
            ),
        )
        == artifact
    )
    with pytest.raises((ValueError, TypeError)):
        admit_release_record(
            canonicalize(artifact.to_document()),
            expected_type=ReleaseArtifact,
            expected_digest=artifact.artifact_digest,
        )


@pytest.mark.parametrize(
    "omitted", [NUGET_CONTENTS_OBLIGATION, NUGET_CONSUMER_OBLIGATION]
)
def test_nuget_qualification_requires_both_artifact_obligations(
    nuget_scenario, monkeypatch, tmp_path, omitted
):
    artifact, evidence, _ = _qualified(nuget_scenario, monkeypatch, tmp_path)
    retained = tuple(
        item for item in evidence if item.obligation.obligation_id != omitted
    )
    decision = finalize_qualification(
        nuget_scenario.snapshot, retained, (artifact,)
    )
    assert decision.terminal_result != "success"
    missing = next(
        item
        for item in decision.obligation_dispositions
        if item.obligation.obligation_id == omitted
    )
    assert missing.outcome == "incomplete"


@pytest.mark.parametrize(
    "substitution", ["source", "witness", "native-version"]
)
def test_nuget_build_rejects_frozen_input_substitution(
    nuget_scenario, monkeypatch, substitution
):
    request = nuget_scenario.request
    if substitution == "source":
        first, *rest = request.source_input_manifest
        request = replace(
            request, source_input_manifest=((first[0], OTHER_DIGEST), *rest)
        )
    else:
        witness = request.witness
        witness = (
            replace(witness, target="f" * 40)
            if substitution == "witness"
            else replace(
                witness,
                nbgv=replace(witness.nbgv, nuget_package_version="9.0.0"),
            )
        )
        request = replace(request, witness=witness)
    calls = []
    monkeypatch.setattr(
        native, "build_dotnet_package", lambda *_: calls.append("build")
    )
    with pytest.raises(ValueError, match="frozen Snapshot"):
        execute_nuget_release_build(nuget_scenario.snapshot, request)
    assert calls == []


@pytest.mark.parametrize(
    "substitution",
    ["unknown", "npm-field", "producer", "purpose", "run", "identity", "media"],
)
def test_nuget_transport_rejects_variant_and_context_substitution(
    nuget_scenario, monkeypatch, tmp_path, substitution
):
    artifact, _, _ = _qualified(nuget_scenario, monkeypatch, tmp_path)
    document = artifact.to_document()
    if substitution == "unknown":
        document["unknown"] = True
    elif substitution == "npm-field":
        document["lifecycle-scripts"] = []
    elif substitution == "producer":
        document["transport"]["producer"] = "build-tarball"
    elif substitution == "purpose":
        document["purpose"] = "release-simulation"
    elif substitution == "run":
        document["transport"]["workflow-run-id"] += 1
    elif substitution == "media":
        document["content"]["media-kind"] = "npm-package"
    else:
        document["nuget-identity"]["normalized-version"] = "9.0.0"
    with pytest.raises((ValueError, TypeError)):
        admit_release_record(
            canonicalize(document),
            expected_type=NugetReleaseArtifact,
            expected_digest=canonical_sha256(document),
            expected_bindings=ReleaseAdmissionBindings(
                "live-release",
                artifact.transport.workflow_run_id,
                None,
                artifact.target,
                "build-nuget-package",
            ),
        )


@pytest.mark.parametrize(
    "substitution", ["actor", "ref", "provider", "model", "binding"]
)
def test_nuget_plan_rejects_stale_or_cross_context_facts(
    nuget_scenario, substitution
):
    intent, binding, model, provider = (
        nuget_scenario.intent,
        nuget_scenario.binding,
        nuget_scenario.model,
        nuget_scenario.provider,
    )
    if substitution == "actor":
        intent = replace(intent, actor="another-writer")
    elif substitution == "ref":
        intent = replace(
            intent,
            selected_ref="refs/heads/feature",
            workflow_ref="refs/heads/feature",
        )
    elif substitution == "provider":
        provider = replace(
            provider,
            bundle=replace(
                provider.bundle, provider_result_digest=OTHER_DIGEST
            ),
        )
    elif substitution == "model":
        context = replace(
            model.snapshot.context, request_id="release-request:" + "5" * 64
        )
        other = replace(model.snapshot, context=context)
        model = compiler.admit_repository_model_snapshot(
            canonicalize(other.to_document()),
            expected_context=context,
            expected_digest=other.snapshot_digest,
        )
    else:
        binding = replace(binding, repository_model_digest=OTHER_DIGEST)
    if substitution in {"actor", "ref"}:
        binding = replace(binding, intent_digest=intent.intent_digest)
    with pytest.raises(
        ValueError,
        match=r"NuGet (planning input integrity|qualification requires)",
    ):
        plan_nuget_live_qualification(intent, binding, model, provider)


def test_actual_native_release_qualification_uses_one_original_archive(
    native_helper, frozen_package, tmp_path
):
    """Exercise real native tools locally with explicitly modeled transport."""
    prior, fixture_root = frozen_package
    repo = fixture_root / "source"
    target = native.dotnet_package_target_witness_from_document(
        parse_canonical_json(prior.witness)
    ).target
    context = compiler.CompilationContext(
        request_id="release-request:" + "6" * 64,
        purpose="live-release",
        workflow_run_id=7901,
        run_attempt=None,
        target=target,
        producer="compile-nuget-model",
        control=f"workflow-delivery-v3:{target}",
        catalog_digest=catalog_digest(),
    )
    manifest = compiler.nuget_provider_manifest(
        context, provider_producer="discover-dotnet"
    )
    result = provide_dotnet_repository_facts(
        repo,
        compiler.provider_binding(manifest, "dotnet-nuget-slice"),
        CheckoutMaterialization(0, credentials_persisted=False),
        helper=native_helper,
        evidence_directory=tmp_path / "provider",
    )
    _, _, model, _, snapshot = _plan_from_provider(
        repo, context, manifest, result
    )
    contract = snapshot.build_requests[0]
    request = native.DotnetBuildRequest(
        repo,
        contract.declared_inputs,
        contract.source_input_manifest,
        _witness(snapshot, model),
        native_helper,
        tmp_path / "build",
    )
    mechanics, failure = execute_nuget_release_build(snapshot, request)
    assert failure is None
    assert mechanics is not None
    archive = mechanics.result.package
    artifact, build_evidence = form_uploaded_nuget_release_artifact(
        snapshot, mechanics, _transport(snapshot)
    )
    content_evidence = qualify_release_nuget_contents(
        snapshot, artifact, archive, mechanics.result.expectation, native_helper
    )
    consumer_evidence = qualify_release_nuget_consumer(
        snapshot,
        artifact,
        archive,
        mechanics.result.expectation,
        native_helper,
        evidence_directory=tmp_path / "consumer",
    )
    decision = finalize_qualification(
        snapshot,
        (build_evidence, content_evidence, consumer_evidence),
        (artifact,),
    )
    assert decision.terminal_result == "success"
    assert (
        artifact.content.content_sha256
        == "sha256:" + hashlib.sha256(archive).hexdigest()
    )
    assert artifact.identity.native_version == result.nbgv.nuget_package_version
    facts = dict(consumer_evidence.result_facts)
    assert facts["package-sha256"] == artifact.content.content_sha256
    assert facts["witness-sha256"] == artifact.witness_digest
    assert facts["marker"] == snapshot.release_unit
    assert (
        content_evidence.obligation.obligation_id
        != consumer_evidence.obligation.obligation_id
    )


@pytest.mark.parametrize("substitution", ["contents", "consumer"])
def test_nuget_quality_rejects_substituted_mechanical_evidence(
    nuget_scenario, monkeypatch, tmp_path, substitution
):
    artifact, _, _ = _qualified(nuget_scenario, monkeypatch, tmp_path)
    scenario = nuget_scenario
    if substitution == "contents":
        monkeypatch.setattr(
            native,
            "qualify_nuget_artifact_contents",
            lambda *_: replace(
                scenario.result.manifest, entries=("substituted",)
            ),
        )
        with pytest.raises(ValueError, match="substituted manifest"):
            qualify_release_nuget_contents(
                scenario.snapshot,
                artifact,
                scenario.result.package,
                scenario.result.expectation,
                scenario.helper,
            )
    else:
        monkeypatch.setattr(
            native,
            "qualify_nuget_restore_build_invoke",
            lambda *_args, **_kwargs: native.DotnetConsumerResult(
                scenario.snapshot.release_unit,
                artifact.witness_digest,
                OTHER_DIGEST,
                artifact.identity.normalized_package_id,
                artifact.identity.normalized_version,
            ),
        )
        with pytest.raises(ValueError, match="substituted identity"):
            qualify_release_nuget_consumer(
                scenario.snapshot,
                artifact,
                scenario.result.package,
                scenario.result.expectation,
                scenario.helper,
                evidence_directory=tmp_path / "other-consumer",
            )


def test_nuget_build_failure_emits_failed_evidence_without_artifact(
    nuget_scenario, monkeypatch
):
    calls = []

    def failure(_request):
        calls.append("build")
        message = "locked native build failed"
        raise ValueError(message)

    monkeypatch.setattr(native, "build_dotnet_package", failure)
    mechanics, evidence = execute_nuget_release_build(
        nuget_scenario.snapshot, nuget_scenario.request
    )
    assert mechanics is None
    assert calls == ["build"]
    assert evidence.normalized_outcome == "failed"
    assert evidence.artifact_digests == ()
    assert evidence.diagnostics == ("locked native build failed",)
    decision = finalize_qualification(nuget_scenario.snapshot, (evidence,), ())
    assert decision.terminal_result == "failure"
    assert tuple(item.outcome for item in decision.obligation_dispositions) == (
        "failed",
        "incomplete",
        "incomplete",
    )


@pytest.mark.parametrize(
    "substitution",
    ["identity", "source", "toolchain", "request", "witness", "target"],
)
def test_nuget_finalizer_rejects_self_consistent_artifact_substitution(
    nuget_scenario, monkeypatch, tmp_path, substitution
):
    artifact, _, _ = _qualified(nuget_scenario, monkeypatch, tmp_path)
    provenance = artifact.provenance_document()
    if substitution == "identity":
        provenance["nuget-identity"]["normalized-version"] = "9.0.0"
    elif substitution == "source":
        provenance["source-input-manifest"][0][1] = OTHER_DIGEST
    elif substitution == "toolchain":
        provenance["toolchain"][0][1] = "0.0.0"
    elif substitution == "target":
        provenance["target"] = "f" * 40
    else:
        field = (
            "build-request-digest"
            if substitution == "request"
            else "witness-digest"
        )
        provenance[field] = OTHER_DIGEST
    document = artifact.to_document() | provenance
    document["schema"] = artifact.to_document()["schema"]
    document["provenance-digest"] = canonical_sha256(provenance)
    substituted = admit_release_record(
        canonicalize(document),
        expected_type=NugetReleaseArtifact,
        expected_digest=canonical_sha256(document),
    )
    assert substituted.artifact_digest != artifact.artifact_digest
    with pytest.raises(
        ValueError, match=r"(frozen native request|current Snapshot)"
    ):
        validate_qualification_artifacts(
            nuget_scenario.snapshot, (substituted,)
        )


@pytest.mark.parametrize("substitution", ["npm-version", "unknown", "source"])
def test_nuget_snapshot_rejects_mixed_or_open_build_request(
    nuget_scenario, substitution
):
    document = nuget_scenario.snapshot.to_document()
    request = document["build-requests"][0]
    if substitution == "npm-version":
        request["npm-package-version"] = request.pop("nuget-package-version")
    elif substitution == "unknown":
        request["unknown"] = True
    else:
        request["source-input-manifest"] = request["source-input-manifest"][1:]
    with pytest.raises((ValueError, TypeError)):
        admit_release_record(
            canonicalize(document),
            expected_type=QualificationSnapshot,
            expected_digest=canonical_sha256(document),
        )


@pytest.mark.parametrize(
    ("left_version", "right_version"),
    [("01.2.3+first", "1.2.3+second"), ("1.2.3-BETA", "1.2.3-beta")],
)
def test_native_equivalent_coordinates_share_release_resource_keys(
    native_helper, left_version, right_version
):
    build = ReleaseBuildIdentity(
        "hcoona-release-smoke-github-packages",
        "package",
        "dotnet/nuget-package-v1",
        "hcoona-release-smoke-github-packages",
    )
    output = ReleaseOutputIdentity(
        ArtifactVariantIdentity(build, "nuget-package/net10.0-windows", ()),
        "nuget-package",
        "primary-package",
        "nuget-package",
    )
    destination = "nuget/github-packages-hcoona-three-v1"

    def native_projection(package_id, version):
        facts = native_helper.normalize_identity(package_id, version)
        return DestinationProjection(
            "projection:nuget:github-packages",
            destination,
            "https://nuget.pkg.github.com/hcoona/index.json",
            NugetExternalPackageCoordinate(
                "buddy",
                destination,
                NugetPackageIdentity(
                    facts["displayPackageId"],
                    facts["displayVersion"],
                    facts["normalizedPackageId"],
                    facts["normalizedVersion"],
                ),
            ),
            output,
            "nuget-publish-create-only",
            "nuget/github-packages-observation-v1",
            "publish-nuget-github-packages",
        )

    left = native_projection("Example.Smoke", left_version)
    right = native_projection("example.smoke", right_version)
    distinct = native_projection("example.smoke", "2.0.0")
    assert left.coordinate.package_name == "Example.Smoke"
    assert left.coordinate.native_version == left_version
    assert left.coordinate.to_document() != right.coordinate.to_document()
    assert publication_mutable_resource_keys(
        left
    ) == publication_mutable_resource_keys(right)
    assert publication_mutable_resource_keys(
        left
    ) != publication_mutable_resource_keys(distinct)
    assert (
        publication_serialization_projection(left)
        == publication_serialization_projection(right)
        == publication_serialization_projection(distinct)
    )


@pytest.mark.parametrize("failed_quality", ["contents", "consumer"])
def test_nuget_quality_failure_remains_a_separate_failed_obligation(
    nuget_scenario, monkeypatch, tmp_path, failed_quality
):
    artifact, evidence, _ = _qualified(nuget_scenario, monkeypatch, tmp_path)

    def failure(*_args, **_kwargs):
        message = "native qualification failed"
        raise ValueError(message)

    scenario = nuget_scenario
    if failed_quality == "contents":
        monkeypatch.setattr(native, "qualify_nuget_artifact_contents", failure)
        failed = qualify_release_nuget_contents(
            scenario.snapshot,
            artifact,
            scenario.result.package,
            scenario.result.expectation,
            scenario.helper,
        )
        current_evidence = (evidence[0], failed, evidence[2])
    else:
        monkeypatch.setattr(
            native, "qualify_nuget_restore_build_invoke", failure
        )
        failed = qualify_release_nuget_consumer(
            scenario.snapshot,
            artifact,
            scenario.result.package,
            scenario.result.expectation,
            scenario.helper,
            evidence_directory=tmp_path / "failed-consumer",
        )
        current_evidence = (evidence[0], evidence[1], failed)
    assert failed.normalized_outcome == "failed"
    assert failed.artifact_digests == (artifact.artifact_digest,)
    decision = finalize_qualification(
        scenario.snapshot, current_evidence, (artifact,)
    )
    assert decision.terminal_result == "failure"
    assert {
        item.obligation.obligation_id
        for item in decision.obligation_dispositions
        if item.outcome == "failed"
    } == {failed.obligation.obligation_id}
