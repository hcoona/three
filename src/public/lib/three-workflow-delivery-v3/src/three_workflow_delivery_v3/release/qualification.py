"""Release qualification mechanics wrapping the unprivileged Node Adapters."""

from __future__ import annotations

import hashlib
import re
import subprocess
from dataclasses import dataclass
from typing import TYPE_CHECKING

from three_workflow_delivery_v3.adapters import node as node_adapter
from three_workflow_delivery_v3.canonical import canonical_sha256
from three_workflow_delivery_v3.records.artifacts import (
    ArtifactContentIdentity,
    ArtifactTransportIdentity,
)
from three_workflow_delivery_v3.records.release import (
    QualificationEvidence,
    QualificationSnapshot,
    ReleaseArtifact,
    ReleaseAttemptIdentity,
    ReleaseObligation,
    ReleaseOutputIdentity,
    SimulationBinding,
    SimulationIdentity,
)

if TYPE_CHECKING:
    from pathlib import Path

    from three_workflow_delivery_v3.canonical import JsonValue

_BUILD_OBLIGATION = "release:build:npm-package"
_PROJECT_TEST_OBLIGATION = "release:quality:project-test"
_CONTENTS_OBLIGATION = "release:quality:npm-artifact-contents"
_INSTALL_OBLIGATION = "release:quality:npm-install-import"
_MECHANICAL_FAILURES = (
    OSError,
    subprocess.CalledProcessError,
    TypeError,
    ValueError,
)
_SHA_PATTERN = re.compile(r"[0-9a-f]{40}\Z")
_SHA256_PATTERN = re.compile(r"sha256:[0-9a-f]{64}\Z")
_PAIR_SIZE = 2


@dataclass(frozen=True, slots=True)
class MechanicalBuildResult:
    """Verified build bytes before GitHub artifact upload metadata exists."""

    subject: SimulationIdentity | ReleaseAttemptIdentity
    repository: str
    qualification_snapshot_digest: str
    repository_model_digest: str
    target: str
    purpose: str
    output: ReleaseOutputIdentity
    build_request_digest: str
    tarball: bytes
    content: ArtifactContentIdentity
    entries: tuple[str, ...]
    lifecycle_scripts: tuple[tuple[str, str], ...]
    witness_digest: str
    source_input_manifest: tuple[tuple[str, str], ...]
    toolchain: tuple[tuple[str, str], ...]
    raw_result: str
    normalized_outcome: str

    def __post_init__(self) -> None:  # noqa: C901, PLR0912
        """Reject nonmechanical or internally substituted build results."""
        if type(self.subject) not in {
            SimulationIdentity,
            ReleaseAttemptIdentity,
        }:
            message = "Mechanical Build Result subject has the wrong type"
            raise TypeError(message)
        if (
            type(self.repository) is not str
            or not self.repository
            or self.repository != self.repository.strip()
            or self.repository.count("/") != 1
        ):
            message = "Mechanical Build Result repository is malformed"
            raise TypeError(message)
        for field, digest in (
            (
                "qualification_snapshot_digest",
                self.qualification_snapshot_digest,
            ),
            ("repository_model_digest", self.repository_model_digest),
            ("build_request_digest", self.build_request_digest),
            ("witness_digest", self.witness_digest),
        ):
            if (
                type(digest) is not str
                or _SHA256_PATTERN.fullmatch(digest) is None
            ):
                message = f"Mechanical Build Result {field} is malformed"
                raise ValueError(message)
        if (
            type(self.target) is not str
            or _SHA_PATTERN.fullmatch(self.target) is None
        ):
            message = "Mechanical Build Result target is malformed"
            raise ValueError(message)
        if type(self.purpose) is not str or self.purpose not in {
            "live-release",
            "release-simulation",
        }:
            message = "Mechanical Build Result purpose is malformed"
            raise ValueError(message)
        if type(self.tarball) is not bytes or not self.tarball:
            message = "Mechanical Build Result tarball must be exact bytes"
            raise TypeError(message)
        if type(self.content) is not ArtifactContentIdentity:
            message = "Mechanical Build Result content has the wrong type"
            raise TypeError(message)
        if type(self.output) is not ReleaseOutputIdentity:
            message = "Mechanical Build Result output has the wrong type"
            raise TypeError(message)
        if (
            self.content.output_id != self.output.output_id
            or self.content.logical_role != self.output.logical_role
            or self.content.media_kind != self.output.media_kind
        ):
            message = "Mechanical Build Result content/output mismatch"
            raise ValueError(message)
        if (
            self.content.byte_size != len(self.tarball)
            or self.content.content_sha256
            != f"sha256:{hashlib.sha256(self.tarball).hexdigest()}"
            or self.content.content_sha512
            != f"sha512:{hashlib.sha512(self.tarball).hexdigest()}"
        ):
            message = "Mechanical Build Result content does not match bytes"
            raise ValueError(message)
        for field, values in (
            ("entries", self.entries),
            ("lifecycle_scripts", self.lifecycle_scripts),
            ("source_input_manifest", self.source_input_manifest),
            ("toolchain", self.toolchain),
        ):
            if type(values) is not tuple:
                message = f"Mechanical Build Result {field} must be a tuple"
                raise TypeError(message)
        if any(
            type(entry) is not str or not entry for entry in self.entries
        ) or self.entries != tuple(sorted(self.entries)):
            message = "Mechanical Build Result entries are not canonical"
            raise ValueError(message)
        for field, pairs in (
            ("lifecycle_scripts", self.lifecycle_scripts),
            ("source_input_manifest", self.source_input_manifest),
            ("toolchain", self.toolchain),
        ):
            if any(
                type(pair) is not tuple
                or len(pair) != _PAIR_SIZE
                or any(type(item) is not str or not item for item in pair)
                for pair in pairs
            ):
                message = f"Mechanical Build Result {field} is malformed"
                raise TypeError(message)
        if any(
            _SHA256_PATTERN.fullmatch(digest) is None
            for _, digest in self.source_input_manifest
        ):
            message = "Mechanical Build Result source input digest is malformed"
            raise ValueError(message)
        if self.raw_result != "success":
            message = "Mechanical Build Result raw outcome must be success"
            raise ValueError(message)
        if self.normalized_outcome != "satisfied":
            message = (
                "Mechanical Build Result normalized outcome must be satisfied"
            )
            raise ValueError(message)


def _subject(
    snapshot: QualificationSnapshot,
) -> SimulationIdentity | ReleaseAttemptIdentity:
    if isinstance(snapshot.subject, SimulationBinding):
        return snapshot.subject.simulation
    return snapshot.subject


def _obligation(
    snapshot: QualificationSnapshot,
    obligation_id: str,
) -> ReleaseObligation:
    if type(snapshot) is not QualificationSnapshot:
        message = "qualification requires an exact QualificationSnapshot"
        raise TypeError(message)
    matches = tuple(
        obligation
        for obligation in snapshot.obligations
        if obligation.obligation_id == obligation_id
    )
    if len(matches) != 1:
        message = (
            f"qualification obligation is not uniquely planned: {obligation_id}"
        )
        raise ValueError(message)
    return matches[0]


def _producer(obligation_id: str) -> str:
    return {
        _BUILD_OBLIGATION: "build-tarball",
        _PROJECT_TEST_OBLIGATION: "project-test",
        _CONTENTS_OBLIGATION: "npm-artifact-qualification",
        _INSTALL_OBLIGATION: "npm-artifact-qualification",
    }[obligation_id]


def _evidence(  # noqa: PLR0913
    snapshot: QualificationSnapshot,
    obligation: ReleaseObligation,
    *,
    raw_result: str,
    normalized_outcome: str,
    artifact_digests: tuple[str, ...] = (),
    result_facts: tuple[tuple[str, str], ...] = (),
    diagnostics: tuple[str, ...] = (),
) -> QualificationEvidence:
    subject = _subject(snapshot)
    workflow_run_id = subject.workflow_run_id
    run_attempt = (
        subject.run_attempt if isinstance(subject, SimulationIdentity) else None
    )
    evidence = QualificationEvidence(
        evidence_id=obligation.expected_evidence_id,
        subject=subject,
        qualification_snapshot_digest=snapshot.snapshot_digest,
        obligation=obligation,
        producer=_producer(obligation.obligation_id),
        workflow_run_id=workflow_run_id,
        run_attempt=run_attempt,
        raw_result=raw_result,
        normalized_outcome=normalized_outcome,
        artifact_digests=artifact_digests,
        result_facts=result_facts,
        diagnostics=diagnostics,
    )
    return admit_evidence_for_snapshot(snapshot, evidence)


def admit_evidence_for_snapshot(  # noqa: C901
    snapshot: QualificationSnapshot,
    evidence: QualificationEvidence,
) -> QualificationEvidence:
    """Admit Evidence only at its exact current Snapshot position."""
    if type(snapshot) is not QualificationSnapshot:
        message = "Evidence admission requires an exact Snapshot"
        raise TypeError(message)
    if type(evidence) is not QualificationEvidence:
        message = "Evidence admission requires exact QualificationEvidence"
        raise TypeError(message)
    planned = _obligation(snapshot, evidence.obligation.obligation_id)
    subject = _subject(snapshot)
    if (
        evidence.subject != subject
        or evidence.qualification_snapshot_digest != snapshot.snapshot_digest
        or evidence.obligation != planned
        or evidence.evidence_id != planned.expected_evidence_id
        or evidence.producer != _producer(planned.obligation_id)
        or evidence.workflow_run_id != subject.workflow_run_id
        or evidence.run_attempt
        != (
            subject.run_attempt
            if isinstance(subject, SimulationIdentity)
            else None
        )
    ):
        message = "Qualification Evidence does not match the current Snapshot"
        raise ValueError(message)
    if planned.obligation_id == _BUILD_OBLIGATION:
        if evidence.normalized_outcome == "satisfied":
            if len(evidence.artifact_digests) != 1:
                message = "satisfied build Evidence requires one artifact"
                raise ValueError(message)
        elif evidence.artifact_digests:
            message = "unsatisfied build Evidence cannot claim an artifact"
            raise ValueError(message)
    elif planned.obligation_id in {_CONTENTS_OBLIGATION, _INSTALL_OBLIGATION}:
        if evidence.normalized_outcome == "incomplete":
            if evidence.artifact_digests:
                message = (
                    "incomplete tarball qualification cannot claim an artifact"
                )
                raise ValueError(message)
        elif len(evidence.artifact_digests) != 1:
            message = "tarball qualification Evidence requires one artifact"
            raise ValueError(message)
    elif evidence.artifact_digests:
        message = "project-test Evidence cannot claim an artifact"
        raise ValueError(message)
    return evidence


def _validate_node_build_request(
    snapshot: QualificationSnapshot,
    request: node_adapter.BuildRequest,
) -> None:
    contract = snapshot.build_requests[0]
    if type(request) is not node_adapter.BuildRequest:
        message = "Release build requires an exact Node BuildRequest"
        raise TypeError(message)
    if (
        request.declared_inputs != contract.declared_inputs
        or request.npm_package_version != contract.npm_package_version
        or canonical_sha256(request.witness.to_document())
        != contract.witness_digest
        or request.witness.target != snapshot.target
        or request.witness.release_unit != snapshot.release_unit
        or request.witness.nbgv != snapshot.nbgv
    ):
        message = "Node BuildRequest does not match the Qualification Snapshot"
        raise ValueError(message)
    expected_purpose = (
        "release-simulation"
        if isinstance(snapshot.subject, SimulationBinding)
        else "live-release"
    )
    if request.witness.purpose != expected_purpose:
        message = "Node BuildRequest has a cross-purpose witness"
        raise ValueError(message)


def _provenance_document(  # noqa: PLR0913
    *,
    subject: SimulationIdentity | ReleaseAttemptIdentity,
    repository: str,
    snapshot: QualificationSnapshot,
    output_document: dict[str, JsonValue],
    build_request_digest: str,
    transport: ArtifactTransportIdentity,
    content: ArtifactContentIdentity,
    witness_digest: str,
    source_input_manifest: tuple[tuple[str, str], ...],
    toolchain: tuple[tuple[str, str], ...],
) -> dict[str, JsonValue]:
    purpose = (
        "release-simulation"
        if isinstance(subject, SimulationIdentity)
        else "live-release"
    )
    return {
        "schema": "workflow-delivery/v3/release-artifact-provenance",
        "subject": subject.to_document(),
        "repository": repository,
        "qualification-snapshot-digest": snapshot.snapshot_digest,
        "repository-model-digest": snapshot.repository_model_digest,
        "target": snapshot.target,
        "purpose": purpose,
        "output": output_document,
        "build-request-digest": build_request_digest,
        "transport": transport.to_document(),
        "content": content.to_document(),
        "witness-digest": witness_digest,
        "source-input-manifest": [
            [path, digest] for path, digest in source_input_manifest
        ],
        "toolchain": [[name, version] for name, version in toolchain],
    }


def execute_release_build(
    snapshot: QualificationSnapshot,
    request: node_adapter.BuildRequest,
) -> tuple[MechanicalBuildResult | None, QualificationEvidence | None]:
    """Run build mechanics without requiring post-upload transport metadata."""
    obligation = _obligation(snapshot, _BUILD_OBLIGATION)
    _validate_node_build_request(snapshot, request)
    try:
        result = node_adapter.build_node_package(request)
    except _MECHANICAL_FAILURES as error:
        evidence = _evidence(
            snapshot,
            obligation,
            raw_result="failure",
            normalized_outcome="failed",
            diagnostics=(str(error) or type(error).__name__,),
        )
        return None, evidence

    contract = snapshot.build_requests[0]
    if (
        result.manifest.byte_size != len(result.tarball)
        or result.manifest.sha256
        != f"sha256:{hashlib.sha256(result.tarball).hexdigest()}"
        or result.manifest.sha512
        != f"sha512:{hashlib.sha512(result.tarball).hexdigest()}"
        or result.witness != request.witness.canonical_bytes
    ):
        message = "Node Build Adapter returned substituted artifact facts"
        raise ValueError(message)
    output = contract.output
    content = ArtifactContentIdentity(
        output_id=output.output_id,
        logical_role=output.logical_role,
        media_kind=output.media_kind,
        basename=result.manifest.basename,
        byte_size=result.manifest.byte_size,
        content_sha256=result.manifest.sha256,
        content_sha512=result.manifest.sha512,
    )
    subject = _subject(snapshot)
    purpose = (
        "release-simulation"
        if isinstance(subject, SimulationIdentity)
        else "live-release"
    )
    mechanics = MechanicalBuildResult(
        subject=subject,
        repository=snapshot.repository,
        qualification_snapshot_digest=snapshot.snapshot_digest,
        repository_model_digest=snapshot.repository_model_digest,
        target=snapshot.target,
        purpose=purpose,
        output=output,
        build_request_digest=contract.request_digest,
        tarball=result.tarball,
        content=content,
        entries=result.manifest.entries,
        lifecycle_scripts=result.manifest.lifecycle_scripts,
        witness_digest=contract.witness_digest,
        source_input_manifest=result.source_input_manifest,
        toolchain=result.toolchain,
        raw_result="success",
        normalized_outcome="satisfied",
    )
    return mechanics, None


def _validate_mechanical_build_result(
    snapshot: QualificationSnapshot,
    mechanics: MechanicalBuildResult,
) -> None:
    if type(mechanics) is not MechanicalBuildResult:
        message = "artifact formation requires exact MechanicalBuildResult"
        raise TypeError(message)
    subject = _subject(snapshot)
    expected_purpose = (
        "release-simulation"
        if isinstance(subject, SimulationIdentity)
        else "live-release"
    )
    if (
        mechanics.subject != subject
        or mechanics.repository != snapshot.repository
        or mechanics.qualification_snapshot_digest != snapshot.snapshot_digest
        or mechanics.repository_model_digest != snapshot.repository_model_digest
        or mechanics.target != snapshot.target
        or mechanics.purpose != expected_purpose
        or mechanics.output != snapshot.outputs[0]
        or mechanics.build_request_digest
        != snapshot.build_requests[0].request_digest
        or mechanics.witness_digest != snapshot.build_requests[0].witness_digest
    ):
        message = "Mechanical Build Result does not match the Snapshot"
        raise ValueError(message)


def form_uploaded_release_artifact(
    snapshot: QualificationSnapshot,
    mechanics: MechanicalBuildResult,
    transport: ArtifactTransportIdentity,
) -> tuple[ReleaseArtifact, QualificationEvidence]:
    """Bind exact post-upload metadata without rebuilding artifact bytes."""
    obligation = _obligation(snapshot, _BUILD_OBLIGATION)
    _validate_mechanical_build_result(snapshot, mechanics)
    if type(transport) is not ArtifactTransportIdentity:
        message = "artifact formation requires exact transport identity"
        raise TypeError(message)
    provenance_document = _provenance_document(
        subject=mechanics.subject,
        repository=mechanics.repository,
        snapshot=snapshot,
        output_document=mechanics.output.to_document(),
        build_request_digest=mechanics.build_request_digest,
        transport=transport,
        content=mechanics.content,
        witness_digest=mechanics.witness_digest,
        source_input_manifest=mechanics.source_input_manifest,
        toolchain=mechanics.toolchain,
    )
    artifact = ReleaseArtifact(
        subject=mechanics.subject,
        repository=mechanics.repository,
        qualification_snapshot_digest=(mechanics.qualification_snapshot_digest),
        repository_model_digest=mechanics.repository_model_digest,
        target=mechanics.target,
        purpose=mechanics.purpose,
        output=mechanics.output,
        build_request_digest=mechanics.build_request_digest,
        transport=transport,
        content=mechanics.content,
        entries=mechanics.entries,
        lifecycle_scripts=mechanics.lifecycle_scripts,
        witness_digest=mechanics.witness_digest,
        source_input_manifest=mechanics.source_input_manifest,
        toolchain=mechanics.toolchain,
        provenance_digest=canonical_sha256(provenance_document),
    )
    content_sha512 = mechanics.content.content_sha512
    if content_sha512 is None:
        message = "Mechanical Build Result lacks required SHA-512"
        raise ValueError(message)
    evidence = _evidence(
        snapshot,
        obligation,
        raw_result="success",
        normalized_outcome="satisfied",
        artifact_digests=(artifact.artifact_digest,),
        result_facts=(
            ("content-sha256", mechanics.content.content_sha256),
            ("content-sha512", content_sha512),
        ),
    )
    return artifact, evidence


def execute_project_test(
    snapshot: QualificationSnapshot,
    project_root: Path,
    request: node_adapter.RuntimeRequest,
) -> QualificationEvidence:
    """Run the independent project test obligation and form Evidence."""
    obligation = _obligation(snapshot, _PROJECT_TEST_OBLIGATION)
    try:
        node_adapter.run_node_project_tests(project_root, request)
    except _MECHANICAL_FAILURES as error:
        return _evidence(
            snapshot,
            obligation,
            raw_result="failure",
            normalized_outcome="failed",
            diagnostics=(str(error) or type(error).__name__,),
        )
    return _evidence(
        snapshot,
        obligation,
        raw_result="success",
        normalized_outcome="satisfied",
        result_facts=(("operation", "node/project-test-v1"),),
    )


def _validate_artifact_bytes(
    snapshot: QualificationSnapshot,
    artifact: ReleaseArtifact,
    tarball: bytes,
) -> None:
    if type(artifact) is not ReleaseArtifact:
        message = "tarball qualification requires an exact ReleaseArtifact"
        raise TypeError(message)
    if type(tarball) is not bytes:
        message = "tarball qualification requires exact bytes"
        raise TypeError(message)
    subject = _subject(snapshot)
    if (
        artifact.subject != subject
        or artifact.repository != snapshot.repository
        or artifact.qualification_snapshot_digest != snapshot.snapshot_digest
        or artifact.repository_model_digest != snapshot.repository_model_digest
        or artifact.target != snapshot.target
        or artifact.output != snapshot.outputs[0]
        or artifact.build_request_digest
        != snapshot.build_requests[0].request_digest
        or artifact.witness_digest != snapshot.build_requests[0].witness_digest
        or artifact.content.byte_size != len(tarball)
        or artifact.content.content_sha256
        != f"sha256:{hashlib.sha256(tarball).hexdigest()}"
        or artifact.content.content_sha512
        != f"sha512:{hashlib.sha512(tarball).hexdigest()}"
    ):
        message = "Release Artifact or tarball binding mismatch"
        raise ValueError(message)


def qualify_release_artifact_contents(
    snapshot: QualificationSnapshot,
    artifact: ReleaseArtifact,
    tarball: bytes,
    expectation: node_adapter.ArtifactExpectation,
) -> QualificationEvidence:
    """Run the tarball-content Adapter as separate exact Evidence."""
    obligation = _obligation(snapshot, _CONTENTS_OBLIGATION)
    _validate_artifact_bytes(snapshot, artifact, tarball)
    try:
        manifest = node_adapter.qualify_npm_artifact_contents(
            tarball,
            expectation,
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
        or manifest.entries != artifact.entries
        or manifest.lifecycle_scripts != artifact.lifecycle_scripts
    ):
        message = "tarball-content Adapter returned substituted manifest facts"
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


def qualify_release_install_import(
    snapshot: QualificationSnapshot,
    artifact: ReleaseArtifact,
    tarball: bytes,
    expectation: node_adapter.ArtifactExpectation,
    request: node_adapter.RuntimeRequest,
) -> QualificationEvidence:
    """Run install/import separately from tarball-content Evidence."""
    obligation = _obligation(snapshot, _INSTALL_OBLIGATION)
    _validate_artifact_bytes(snapshot, artifact, tarball)
    try:
        result = node_adapter.qualify_npm_install_import(
            tarball,
            expectation,
            request,
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
    return _evidence(
        snapshot,
        obligation,
        raw_result="success",
        normalized_outcome="satisfied",
        artifact_digests=(artifact.artifact_digest,),
        result_facts=(
            ("smoke-message", result.smoke_message),
            ("witness-sha256", result.witness_sha256),
        ),
    )


def form_incomplete_evidence(
    snapshot: QualificationSnapshot,
    obligation_id: str,
    *,
    reason: str,
) -> QualificationEvidence:
    """Form explicit failure-continuation Evidence for work not started."""
    if reason not in {
        "blocked-by-prerequisite",
        "aborted-after-failure",
    }:
        message = "incomplete qualification reason is not closed"
        raise ValueError(message)
    obligation = _obligation(snapshot, obligation_id)
    if reason == "blocked-by-prerequisite" and not obligation.prerequisites:
        message = "only dependent work may be blocked by prerequisite"
        raise ValueError(message)
    return _evidence(
        snapshot,
        obligation,
        raw_result=reason,
        normalized_outcome="incomplete",
        diagnostics=(reason,),
    )


__all__ = [
    "MechanicalBuildResult",
    "admit_evidence_for_snapshot",
    "execute_project_test",
    "execute_release_build",
    "form_incomplete_evidence",
    "form_uploaded_release_artifact",
    "qualify_release_artifact_contents",
    "qualify_release_install_import",
]
