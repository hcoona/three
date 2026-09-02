"""Thin workflow mechanics around the commit-6 Release core."""

from __future__ import annotations

from typing import TYPE_CHECKING

from three_workflow_delivery_v3.adapters.node import (
    ArtifactExpectation,
    BuildRequest,
    PackageTargetWitness,
    RuntimeRequest,
)
from three_workflow_delivery_v3.canonical import (
    canonical_sha256,
    parse_canonical_json,
)
from three_workflow_delivery_v3.records.artifacts import (
    ArtifactContentIdentity,
)
from three_workflow_delivery_v3.records.release import (
    QualificationSnapshot,
    ReleaseArtifact,
    ReleaseAttemptIdentity,
    SimulationBinding,
    SimulationIdentity,
)
from three_workflow_delivery_v3.release.qualification import (
    MechanicalBuildResult,
)
from three_workflow_delivery_v3.release.simulation import (
    ReleaseAdapterContext,
)

if TYPE_CHECKING:
    from pathlib import Path

    from three_workflow_delivery_v3.canonical import JsonValue
    from three_workflow_delivery_v3.repository.compiler import (
        AdmittedRepositoryModelSnapshot,
    )

MECHANICAL_BUILD_RESULT_SCHEMA = "workflow-delivery/v3/mechanical-build-result"
_FILES_ALLOWLIST = (
    "dist",
    "README.md",
    "workflow-delivery/provenance.json",
)


def _subject(
    snapshot: QualificationSnapshot,
) -> SimulationIdentity | ReleaseAttemptIdentity:
    if isinstance(snapshot.subject, SimulationBinding):
        return snapshot.subject.simulation
    return snapshot.subject


def _purpose(subject: SimulationIdentity | ReleaseAttemptIdentity) -> str:
    return (
        "release-simulation"
        if isinstance(subject, SimulationIdentity)
        else "live-release"
    )


def form_release_adapter_context(  # noqa: PLR0913
    snapshot: QualificationSnapshot,
    repository_model: AdmittedRepositoryModelSnapshot,
    *,
    source_date_epoch: int,
    node_version: str,
    pnpm_version: str,
    npm_version: str,
) -> ReleaseAdapterContext:
    """Freeze the exact Node Adapter inputs selected by the Release Plan."""
    subject = _subject(snapshot)
    purpose = _purpose(subject)
    model = repository_model.snapshot
    model_binding_mismatch = (
        repository_model.canonical_digest != snapshot.repository_model_digest
        or model.context.target != snapshot.target
        or model.context.purpose != purpose
        or model.context.workflow_run_id != subject.workflow_run_id
    )
    if isinstance(subject, SimulationIdentity):
        model_binding_mismatch = (
            model_binding_mismatch
            or model.context.run_attempt != subject.run_attempt
        )
    if model_binding_mismatch:
        message = "Release Adapter context model binding mismatch"
        raise ValueError(message)
    control_digest = canonical_sha256(
        {
            "schema": "workflow-delivery/v3/control-identity",
            "identity": model.context.control,
        }
    )
    witness = PackageTargetWitness(
        target=snapshot.target,
        release_unit=snapshot.release_unit,
        nbgv=snapshot.nbgv,
        build_definition=snapshot.build_requests[0].build.definition_id,
        catalog_digest=model.context.catalog_digest,
        control_digest=control_digest,
        purpose=purpose,
    )
    if (
        canonical_sha256(witness.to_document())
        != snapshot.build_requests[0].witness_digest
    ):
        message = "Release Adapter context witness does not match Plan"
        raise ValueError(message)
    return ReleaseAdapterContext(
        subject=subject,
        qualification_snapshot_digest=snapshot.snapshot_digest,
        repository_model_digest=snapshot.repository_model_digest,
        project_path=model.project_nodes[0].path,
        source_date_epoch=source_date_epoch,
        node_version=node_version,
        pnpm_version=pnpm_version,
        npm_version=npm_version,
        witness=witness,
    )


def node_build_request(
    repository_root: Path,
    snapshot: QualificationSnapshot,
    context: ReleaseAdapterContext,
) -> BuildRequest:
    """Materialize the exact Node Build Request selected by the Snapshot."""
    _validate_context(snapshot, context)
    contract = snapshot.build_requests[0]
    return BuildRequest(
        source_root=repository_root / context.project_path,
        declared_inputs=contract.declared_inputs,
        npm_package_version=contract.npm_package_version,
        witness=context.witness,
        source_date_epoch=context.source_date_epoch,
        node_version=context.node_version,
        pnpm_version=context.pnpm_version,
        npm_version=context.npm_version,
    )


def runtime_request(
    snapshot: QualificationSnapshot,
    context: ReleaseAdapterContext,
) -> RuntimeRequest:
    """Return the exact runtime versions frozen in the Adapter context."""
    _validate_context(snapshot, context)
    return RuntimeRequest(
        node_version=context.node_version,
        npm_version=context.npm_version,
    )


def artifact_expectation(
    snapshot: QualificationSnapshot,
    context: ReleaseAdapterContext,
    artifact: ReleaseArtifact,
) -> ArtifactExpectation:
    """Reconstruct the exact shared tarball qualification expectation."""
    _validate_context(snapshot, context)
    if (
        artifact.qualification_snapshot_digest != snapshot.snapshot_digest
        or artifact.output != snapshot.outputs[0]
        or artifact.witness_digest
        != canonical_sha256(context.witness.to_document())
    ):
        message = "Release Artifact does not match Adapter expectation"
        raise ValueError(message)
    return ArtifactExpectation(
        package_name=(
            snapshot.destination_projections[0].coordinate.package_name
        ),
        npm_package_version=snapshot.nbgv.npm_package_version,
        files_allowlist=_FILES_ALLOWLIST,
        lifecycle_scripts=artifact.lifecycle_scripts,
        entry_allowlist=artifact.entries,
        witness_bytes=context.witness.canonical_bytes,
    )


def mechanical_build_document(
    mechanics: MechanicalBuildResult,
) -> dict[str, JsonValue]:
    """Return canonical mechanical facts with tarball bytes kept separate."""
    document: dict[str, JsonValue] = {
        "schema": MECHANICAL_BUILD_RESULT_SCHEMA,
        "subject": mechanics.subject.to_document(),
        "repository": mechanics.repository,
        "qualification-snapshot-digest": (
            mechanics.qualification_snapshot_digest
        ),
        "repository-model-digest": mechanics.repository_model_digest,
        "target": mechanics.target,
        "purpose": mechanics.purpose,
        "output": mechanics.output.to_document(),
        "build-request-digest": mechanics.build_request_digest,
        "content": mechanics.content.to_document(),
        "entries": _string_array(mechanics.entries),
        "lifecycle-scripts": _pair_array(mechanics.lifecycle_scripts),
        "witness-digest": mechanics.witness_digest,
        "source-input-manifest": _pair_array(mechanics.source_input_manifest),
        "toolchain": _pair_array(mechanics.toolchain),
        "raw-result": mechanics.raw_result,
        "normalized-outcome": mechanics.normalized_outcome,
    }
    return document


def mechanical_build_from_bytes(
    content: bytes,
    *,
    snapshot: QualificationSnapshot,
    tarball: bytes,
) -> MechanicalBuildResult:
    """Admit canonical mechanical facts and their separately uploaded bytes."""
    document = parse_canonical_json(content)
    expected_fields = {
        "schema",
        "subject",
        "repository",
        "qualification-snapshot-digest",
        "repository-model-digest",
        "target",
        "purpose",
        "output",
        "build-request-digest",
        "content",
        "entries",
        "lifecycle-scripts",
        "witness-digest",
        "source-input-manifest",
        "toolchain",
        "raw-result",
        "normalized-outcome",
    }
    if document.keys() != expected_fields:
        message = "Mechanical Build Result closed schema mismatch"
        raise ValueError(message)
    if document["schema"] != MECHANICAL_BUILD_RESULT_SCHEMA:
        message = "Mechanical Build Result schema mismatch"
        raise ValueError(message)
    expected_subject = _subject(snapshot)
    subject_document = _object(document["subject"], field="subject")
    output_document = _object(document["output"], field="output")
    content_document = _object(document["content"], field="content")
    if subject_document != expected_subject.to_document():
        message = "Mechanical Build Result subject binding mismatch"
        raise ValueError(message)
    subject = expected_subject
    if output_document != snapshot.outputs[0].to_document():
        message = "Mechanical Build Result output binding mismatch"
        raise ValueError(message)
    expected_content_fields = {
        "output-id",
        "logical-role",
        "media-kind",
        "basename",
        "byte-size",
        "content-sha256",
        "content-sha512",
    }
    if content_document.keys() != expected_content_fields:
        message = "Mechanical Build Result content schema mismatch"
        raise ValueError(message)
    content_identity = ArtifactContentIdentity(
        output_id=_string(content_document["output-id"], field="output-id"),
        logical_role=_string(
            content_document["logical-role"],
            field="logical-role",
        ),
        media_kind=_string(
            content_document["media-kind"],
            field="media-kind",
        ),
        basename=_string(content_document["basename"], field="basename"),
        byte_size=_integer(content_document["byte-size"], field="byte-size"),
        content_sha256=_string(
            content_document["content-sha256"],
            field="content-sha256",
        ),
        content_sha512=_nullable_string(
            content_document["content-sha512"],
            field="content-sha512",
        ),
    )
    mechanics = MechanicalBuildResult(
        subject=subject,
        repository=_string(document["repository"], field="repository"),
        qualification_snapshot_digest=_string(
            document["qualification-snapshot-digest"],
            field="qualification-snapshot-digest",
        ),
        repository_model_digest=_string(
            document["repository-model-digest"],
            field="repository-model-digest",
        ),
        target=_string(document["target"], field="target"),
        purpose=_string(document["purpose"], field="purpose"),
        output=snapshot.outputs[0],
        build_request_digest=_string(
            document["build-request-digest"],
            field="build-request-digest",
        ),
        tarball=tarball,
        content=content_identity,
        entries=_strings(document["entries"], field="entries"),
        lifecycle_scripts=_pairs(
            document["lifecycle-scripts"],
            field="lifecycle-scripts",
        ),
        witness_digest=_string(
            document["witness-digest"],
            field="witness-digest",
        ),
        source_input_manifest=_pairs(
            document["source-input-manifest"],
            field="source-input-manifest",
        ),
        toolchain=_pairs(document["toolchain"], field="toolchain"),
        raw_result=_string(document["raw-result"], field="raw-result"),
        normalized_outcome=_string(
            document["normalized-outcome"],
            field="normalized-outcome",
        ),
    )
    if mechanical_build_document(mechanics) != document:
        message = "Mechanical Build Result is not normalized"
        raise ValueError(message)
    if (
        subject != expected_subject
        or mechanics.repository != snapshot.repository
        or mechanics.qualification_snapshot_digest != snapshot.snapshot_digest
        or mechanics.repository_model_digest != snapshot.repository_model_digest
        or mechanics.target != snapshot.target
        or mechanics.purpose != _purpose(expected_subject)
        or mechanics.output != snapshot.outputs[0]
        or mechanics.build_request_digest
        != snapshot.build_requests[0].request_digest
        or mechanics.witness_digest != snapshot.build_requests[0].witness_digest
    ):
        message = "Mechanical Build Result does not match current Snapshot"
        raise ValueError(message)
    return mechanics


def _validate_context(
    snapshot: QualificationSnapshot,
    context: ReleaseAdapterContext,
) -> None:
    subject = _subject(snapshot)
    if (
        context.subject != subject
        or context.qualification_snapshot_digest != snapshot.snapshot_digest
        or context.repository_model_digest != snapshot.repository_model_digest
        or context.witness.target != snapshot.target
        or context.witness.release_unit != snapshot.release_unit
        or context.witness.nbgv != snapshot.nbgv
        or context.witness.purpose != _purpose(subject)
        or canonical_sha256(context.witness.to_document())
        != snapshot.build_requests[0].witness_digest
    ):
        message = "Release Adapter context does not match current Snapshot"
        raise ValueError(message)


def _string(value: JsonValue, *, field: str) -> str:
    if type(value) is not str:
        message = f"{field} must be a string"
        raise TypeError(message)
    return value


def _string_array(values: tuple[str, ...]) -> list[JsonValue]:
    result: list[JsonValue] = []
    result.extend(values)
    return result


def _pair_array(
    values: tuple[tuple[str, str], ...],
) -> list[JsonValue]:
    result: list[JsonValue] = []
    for first, second in values:
        pair: list[JsonValue] = [first, second]
        result.append(pair)
    return result


def _object(
    value: JsonValue,
    *,
    field: str,
) -> dict[str, JsonValue]:
    if not isinstance(value, dict):
        message = f"{field} must be an object"
        raise TypeError(message)
    return value


def _integer(value: JsonValue, *, field: str) -> int:
    if type(value) is not int:
        message = f"{field} must be an integer"
        raise TypeError(message)
    return value


def _array(value: JsonValue, *, field: str) -> list[JsonValue]:
    if not isinstance(value, list):
        message = f"{field} must be an array"
        raise TypeError(message)
    return value


def _strings(value: JsonValue, *, field: str) -> tuple[str, ...]:
    return tuple(
        _string(item, field=f"{field}[{index}]")
        for index, item in enumerate(_array(value, field=field))
    )


def _pairs(value: JsonValue, *, field: str) -> tuple[tuple[str, str], ...]:
    result: list[tuple[str, str]] = []
    for index, item in enumerate(_array(value, field=field)):
        pair = _array(item, field=f"{field}[{index}]")
        if len(pair) != 2:  # noqa: PLR2004
            message = f"{field}[{index}] must contain two strings"
            raise ValueError(message)
        result.append(
            (
                _string(pair[0], field=f"{field}[{index}][0]"),
                _string(pair[1], field=f"{field}[{index}][1]"),
            )
        )
    return tuple(result)


def _nullable_string(value: JsonValue, *, field: str) -> str | None:
    return None if value is None else _string(value, field=field)


__all__ = [
    "MECHANICAL_BUILD_RESULT_SCHEMA",
    "artifact_expectation",
    "form_release_adapter_context",
    "mechanical_build_document",
    "mechanical_build_from_bytes",
    "node_build_request",
    "runtime_request",
]
