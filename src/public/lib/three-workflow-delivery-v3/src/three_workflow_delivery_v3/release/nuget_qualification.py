"""Release-owned NuGet package construction and separate quality Evidence."""

from __future__ import annotations

import hashlib
from dataclasses import dataclass
from typing import TYPE_CHECKING

from three_workflow_delivery_v3.adapters import dotnet as native
from three_workflow_delivery_v3.canonical import (
    canonical_sha256,
    parse_canonical_json,
)
from three_workflow_delivery_v3.records.artifacts import (
    ArtifactContentIdentity,
    ArtifactTransportIdentity,
)
from three_workflow_delivery_v3.records.release import (
    NugetExternalPackageCoordinate,
    NugetPackageIdentity,
    NugetReleaseArtifact,
    NugetReleaseBuildRequest,
    QualificationEvidence,
    QualificationSnapshot,
    ReleaseAttemptIdentity,
)
from three_workflow_delivery_v3.release.nuget_planner import (
    NUGET_BUILD_OBLIGATION,
    NUGET_CONSUMER_OBLIGATION,
    NUGET_CONTENTS_OBLIGATION,
)
from three_workflow_delivery_v3.release.qualification import (
    _MECHANICAL_FAILURES,
    _evidence,
    _obligation,
    _provenance_document,
    validate_qualification_artifacts,
)
from three_workflow_delivery_v3.repository.dotnet_provider import (
    DOTNET_TOOLCHAIN,
    NativeNuGetHelper,
)

if TYPE_CHECKING:
    from pathlib import Path


@dataclass(frozen=True, slots=True)
class NugetMechanicalBuildResult:
    """Original native bytes bound before immutable transport is available."""

    qualification_snapshot_digest: str
    build_request_digest: str
    result: native.DotnetBuildResult


def _contract(snapshot: QualificationSnapshot) -> NugetReleaseBuildRequest:
    if (
        type(snapshot) is not QualificationSnapshot
        or type(snapshot.subject) is not ReleaseAttemptIdentity
        or len(snapshot.build_requests) != 1
        or type(snapshot.build_requests[0]) is not NugetReleaseBuildRequest
    ):
        message = "NuGet qualification requires its exact live native Snapshot"
        raise TypeError(message)
    return snapshot.build_requests[0]


def _identity(snapshot: QualificationSnapshot) -> NugetPackageIdentity:
    coordinate = snapshot.destination_projections[0].coordinate
    if type(coordinate) is not NugetExternalPackageCoordinate:
        message = "NuGet qualification lacks its native coordinate"
        raise TypeError(message)
    return coordinate.identity


def _validate_expectation(
    snapshot: QualificationSnapshot,
    expectation: native.DotnetArtifactExpectation,
) -> None:
    contract = _contract(snapshot)
    identity = _identity(snapshot)
    if type(expectation) is not native.DotnetArtifactExpectation or (
        expectation.package_name,
        expectation.nuget_package_version,
        expectation.normalized_package_id,
        expectation.normalized_version,
    ) != (
        identity.package_name,
        identity.native_version,
        identity.normalized_package_id,
        identity.normalized_version,
    ):
        message = "NuGet package expectation substitutes native identity"
        raise ValueError(message)
    witness = native.dotnet_package_target_witness_from_document(
        parse_canonical_json(expectation.witness_bytes)
    )
    if (
        witness.canonical_bytes != expectation.witness_bytes
        or canonical_sha256(witness.to_document()) != contract.witness_digest
        or witness.target != snapshot.target
        or witness.nbgv != snapshot.nbgv
        or witness.release_unit != snapshot.release_unit
        or witness.build_definition != contract.build.definition_id
        or witness.purpose != "live-release"
    ):
        message = "NuGet package expectation substitutes the frozen witness"
        raise ValueError(message)


def _validate_result(
    snapshot: QualificationSnapshot,
    result: native.DotnetBuildResult,
) -> None:
    contract = _contract(snapshot)
    if type(result) is not native.DotnetBuildResult:
        message = "NuGet Build Adapter returned another result type"
        raise TypeError(message)
    _validate_expectation(snapshot, result.expectation)
    if (
        type(result.package) is not bytes
        or not result.package
        or type(result.manifest) is not native.DotnetArtifactManifest
        or result.manifest.byte_size != len(result.package)
        or result.manifest.sha256
        != f"sha256:{hashlib.sha256(result.package).hexdigest()}"
        or result.manifest.sha512
        != f"sha512:{hashlib.sha512(result.package).hexdigest()}"
        or result.witness != result.expectation.witness_bytes
        or result.source_input_manifest != contract.source_input_manifest
        or result.toolchain != DOTNET_TOOLCHAIN
    ):
        message = "NuGet Build Adapter returned substituted artifact facts"
        raise ValueError(message)


def execute_nuget_release_build(
    snapshot: QualificationSnapshot,
    request: native.DotnetBuildRequest,
) -> tuple[NugetMechanicalBuildResult | None, QualificationEvidence | None]:
    """Execute only frozen native Build mechanics without publication access."""
    contract = _contract(snapshot)
    obligation = _obligation(snapshot, NUGET_BUILD_OBLIGATION)
    if type(request) is not native.DotnetBuildRequest:
        message = "NuGet Release build requires an exact native Build Request"
        raise TypeError(message)
    if (
        request.declared_inputs != contract.declared_inputs
        or request.source_input_manifest != contract.source_input_manifest
        or canonical_sha256(request.witness.to_document())
        != contract.witness_digest
        or request.witness.nbgv != snapshot.nbgv
        or request.witness.target != snapshot.target
        or request.witness.release_unit != snapshot.release_unit
        or request.witness.purpose != "live-release"
    ):
        message = "NuGet Build Request does not match the frozen Snapshot"
        raise ValueError(message)
    try:
        result = native.build_dotnet_package(request)
    except _MECHANICAL_FAILURES as error:
        return None, _evidence(
            snapshot,
            obligation,
            raw_result="failure",
            normalized_outcome="failed",
            diagnostics=(str(error) or type(error).__name__,),
        )
    _validate_result(snapshot, result)
    return NugetMechanicalBuildResult(
        snapshot.snapshot_digest, contract.request_digest, result
    ), None


def form_uploaded_nuget_release_artifact(
    snapshot: QualificationSnapshot,
    mechanics: NugetMechanicalBuildResult,
    transport: ArtifactTransportIdentity,
) -> tuple[NugetReleaseArtifact, QualificationEvidence]:
    """Bind original archive bytes to immutable upload metadata."""
    contract = _contract(snapshot)
    if type(mechanics) is not NugetMechanicalBuildResult or (
        mechanics.qualification_snapshot_digest != snapshot.snapshot_digest
        or mechanics.build_request_digest != contract.request_digest
    ):
        message = "NuGet mechanics do not match the current Snapshot"
        raise ValueError(message)
    _validate_result(snapshot, mechanics.result)
    result = mechanics.result
    manifest = result.manifest
    output = contract.output
    content = ArtifactContentIdentity(
        output.output_id,
        output.logical_role,
        output.media_kind,
        manifest.basename,
        manifest.byte_size,
        manifest.sha256,
        manifest.sha512,
    )
    provenance = _provenance_document(
        subject=snapshot.subject,
        repository=snapshot.repository,
        snapshot=snapshot,
        output_document=output.to_document(),
        build_request_digest=contract.request_digest,
        transport=transport,
        content=content,
        witness_digest=contract.witness_digest,
        source_input_manifest=result.source_input_manifest,
        toolchain=result.toolchain,
    )
    identity = _identity(snapshot)
    provenance["nuget-identity"] = identity.to_document()
    artifact = NugetReleaseArtifact(
        subject=snapshot.subject,
        repository=snapshot.repository,
        qualification_snapshot_digest=snapshot.snapshot_digest,
        repository_model_digest=snapshot.repository_model_digest,
        target=snapshot.target,
        purpose="live-release",
        output=output,
        build_request_digest=contract.request_digest,
        transport=transport,
        content=content,
        entries=manifest.entries,
        identity=identity,
        witness_digest=contract.witness_digest,
        source_input_manifest=result.source_input_manifest,
        toolchain=result.toolchain,
        provenance_digest=canonical_sha256(provenance),
    )
    validate_qualification_artifacts(snapshot, (artifact,))
    return artifact, _evidence(
        snapshot,
        _obligation(snapshot, NUGET_BUILD_OBLIGATION),
        raw_result="success",
        normalized_outcome="satisfied",
        artifact_digests=(artifact.artifact_digest,),
        result_facts=(
            ("content-sha256", manifest.sha256),
            ("content-sha512", manifest.sha512),
        ),
    )


def _validate_artifact(
    snapshot: QualificationSnapshot,
    artifact: NugetReleaseArtifact,
    package: bytes,
    expectation: native.DotnetArtifactExpectation,
) -> None:
    _contract(snapshot)
    if type(artifact) is not NugetReleaseArtifact or type(package) is not bytes:
        message = "NuGet qualification requires exact native artifact and bytes"
        raise TypeError(message)
    validate_qualification_artifacts(snapshot, (artifact,))
    _validate_expectation(snapshot, expectation)
    if (
        artifact.content.byte_size != len(package)
        or artifact.content.content_sha256
        != f"sha256:{hashlib.sha256(package).hexdigest()}"
        or artifact.content.content_sha512
        != f"sha512:{hashlib.sha512(package).hexdigest()}"
        or artifact.toolchain != DOTNET_TOOLCHAIN
    ):
        message = (
            "NuGet qualification bytes or toolchain do not match the artifact"
        )
        raise ValueError(message)


def qualify_release_nuget_contents(
    snapshot: QualificationSnapshot,
    artifact: NugetReleaseArtifact,
    package: bytes,
    expectation: native.DotnetArtifactExpectation,
    helper: NativeNuGetHelper,
) -> QualificationEvidence:
    """Qualify actual package contents as one independent obligation."""
    _validate_artifact(snapshot, artifact, package, expectation)
    obligation = _obligation(snapshot, NUGET_CONTENTS_OBLIGATION)
    try:
        manifest = native.qualify_nuget_artifact_contents(
            package, expectation, helper
        )
    except _MECHANICAL_FAILURES as error:
        return _evidence(
            snapshot,
            obligation,
            raw_result="failure",
            normalized_outcome="failed",
            artifact_digests=(artifact.artifact_digest,),
            diagnostics=(str(error) or type(error).__name__,),
        )
    if (
        manifest.sha256 != artifact.content.content_sha256
        or manifest.sha512 != artifact.content.content_sha512
        or manifest.byte_size != artifact.content.byte_size
        or manifest.basename != artifact.content.basename
        or manifest.entries != artifact.entries
    ):
        message = "NuGet content Adapter returned substituted manifest facts"
        raise ValueError(message)
    return _evidence(
        snapshot,
        obligation,
        raw_result="success",
        normalized_outcome="satisfied",
        artifact_digests=(artifact.artifact_digest,),
        result_facts=(
            ("content-sha256", manifest.sha256),
            ("content-sha512", manifest.sha512),
        ),
    )


def qualify_release_nuget_consumer(  # noqa: PLR0913
    snapshot: QualificationSnapshot,
    artifact: NugetReleaseArtifact,
    package: bytes,
    expectation: native.DotnetArtifactExpectation,
    helper: NativeNuGetHelper,
    *,
    evidence_directory: Path,
) -> QualificationEvidence:
    """Bind fresh exact-version restore/build/marker behavior separately."""
    _validate_artifact(snapshot, artifact, package, expectation)
    obligation = _obligation(snapshot, NUGET_CONSUMER_OBLIGATION)
    try:
        result = native.qualify_nuget_restore_build_invoke(
            package, expectation, helper, evidence_directory=evidence_directory
        )
    except _MECHANICAL_FAILURES as error:
        return _evidence(
            snapshot,
            obligation,
            raw_result="failure",
            normalized_outcome="failed",
            artifact_digests=(artifact.artifact_digest,),
            diagnostics=(str(error) or type(error).__name__,),
        )
    if (
        result.project_id != snapshot.release_unit
        or result.package_sha256 != artifact.content.content_sha256
        or result.witness_sha256 != artifact.witness_digest
        or result.normalized_package_id
        != artifact.identity.normalized_package_id
        or result.normalized_version != artifact.identity.normalized_version
    ):
        message = (
            "NuGet consumer Adapter returned substituted identity or behavior"
        )
        raise ValueError(message)
    return _evidence(
        snapshot,
        obligation,
        raw_result="success",
        normalized_outcome="satisfied",
        artifact_digests=(artifact.artifact_digest,),
        result_facts=(
            ("marker", result.project_id),
            ("normalized-package-id", result.normalized_package_id),
            ("normalized-version", result.normalized_version),
            ("package-sha256", result.package_sha256),
            ("witness-sha256", result.witness_sha256),
        ),
    )
