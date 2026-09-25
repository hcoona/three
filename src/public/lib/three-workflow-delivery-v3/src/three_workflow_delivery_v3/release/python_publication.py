"""One Python set action, two ordered one-shot uploads and scalar terminal."""

from __future__ import annotations

import time
from dataclasses import dataclass
from datetime import datetime
from typing import TYPE_CHECKING, cast

from three_workflow_delivery_v3.adapters.pypi import (
    PythonHttpTransport,
    PythonIndexObservation,
    PythonRegistry,
    PythonUploadResponse,
    read_python_index,
    upload_python_once,
)
from three_workflow_delivery_v3.adapters.python_observation import (
    ExpectedAddition,
    IndexPhase,
    ObservationBasis,
    index_inventory,
    number,
    replay_failed_index_phase,
    replay_index_phase,
)
from three_workflow_delivery_v3.canonical import (
    JsonValue,
    canonical_sha256,
    canonicalize,
    parse_canonical_json,
)
from three_workflow_delivery_v3.records.artifacts import (
    ArtifactReference,
    _digest,
)
from three_workflow_delivery_v3.records.release import (
    PUBLICATION_RESULT_SCHEMA,
    ReleaseAttemptIdentity,
)
from three_workflow_delivery_v3.release.python_governance import (
    PYTHON_PUBLISHER,
    PythonGovernance,
)
from three_workflow_delivery_v3.release.python_readback import (
    PublicationTransport,
    ReadbackTransport,
    replay_readback,
    validate_observation_document,
)
from three_workflow_delivery_v3.repository.python_provider import (
    python_digest,
    python_object,
    python_text,
)

if TYPE_CHECKING:
    from collections.abc import Callable
    from pathlib import Path

    from three_workflow_delivery_v3.release.python_qualification import (
        PythonQualificationDecision,
    )


def _instant(value: datetime) -> str:
    if value.tzinfo is None:
        message = "Python publication requires timezone-aware time"
        raise ValueError(message)
    return value.isoformat().replace("+00:00", "Z")


def _reference(
    reference: ArtifactReference, document: dict[str, JsonValue]
) -> None:
    if type(
        reference
    ) is not ArtifactReference or reference.payload_digest != canonical_sha256(
        document
    ):
        message = "Python immutable record reference differs from its payload"
        raise ValueError(message)


def _exact_files(
    observation: PythonIndexObservation,
    decision: PythonQualificationDecision,
    *,
    wheel_only: bool = False,
) -> bool:
    originals = decision.artifacts[:1] if wheel_only else decision.artifacts
    return (
        observation.registry == decision.snapshot.governance.registry
        and observation.version
        == decision.snapshot.model.provider.nbgv.pep440_version
        and tuple(
            (d.variant, d.filename, d.digest, d.witness)
            for d in observation.files
        )
        == tuple(
            (a.variant, a.filename, a.reference.payload_digest, a.witness)
            for a in originals
        )
        and observation.classification
        == ("partial" if wheel_only else "complete")
    )


@dataclass(frozen=True, slots=True)
class PythonRemoteObservation:
    """Current-Attempt credential-free complete destination observation."""

    decision: PythonQualificationDecision
    decision_reference: ArtifactReference
    native: PythonIndexObservation
    observed_at: datetime

    def __post_init__(self) -> None:
        """Bind exact qualified bytes and distinguish blocked/absent/exact."""
        _reference(self.decision_reference, self.decision.to_document())
        _ = self.decision.artifacts
        self.decision.snapshot.governance.require_live(self.observed_at)
        if (
            self.native.registry != self.decision.snapshot.governance.registry
            or self.native.version
            != self.decision.snapshot.model.provider.nbgv.pep440_version
        ):
            message = "Python observation destination or version mismatch"
            raise ValueError(message)
        _instant(self.observed_at)

    @property
    def classification(self) -> str:
        """Partial or conflicting state never authorizes completion uploads."""
        if self.native.classification == "absent" and not self.native.files:
            return "absent"
        return (
            "exact-satisfied"
            if _exact_files(self.native, self.decision)
            else "blocked"
        )

    def to_document(self) -> dict[str, JsonValue]:
        """Retain sanitized index/download facts under the current Attempt."""
        return {
            "schema": "workflow-delivery/v3/python-remote-state-observation",
            "attempt": self.decision.snapshot.attempt.to_document(),
            "decision-reference": self.decision_reference.to_document(),
            "native": self.native.to_document(),
            "observed-at": _instant(self.observed_at),
            "classification": self.classification,
            "producer": "observe-python",
        }


@dataclass(frozen=True, slots=True)
class PythonPublicationSnapshot:
    """One fixed set action or zero actions, never a generic action graph."""

    observation: PythonRemoteObservation
    observation_reference: ArtifactReference

    def __post_init__(self) -> None:
        """Materialize only qualified absent or exactly complete state."""
        _reference(self.observation_reference, self.observation.to_document())
        if self.observation.classification not in {"absent", "exact-satisfied"}:
            message = (
                "Python partial, conflicting or unknown set blocks publication"
            )
            raise ValueError(message)

    @property
    def action_required(self) -> bool:
        """Absence alone is a candidate, not a service success guarantee."""
        return self.observation.classification == "absent"

    @property
    def attempt(self) -> ReleaseAttemptIdentity:
        """Return the existing current-Attempt identity."""
        return self.observation.decision.snapshot.attempt

    @property
    def registry(self) -> PythonRegistry:
        """Return the independently bound destination profile."""
        return self.observation.decision.snapshot.governance.registry

    def to_document(self) -> dict[str, JsonValue]:
        """Close ordered originals, profile and physical registry resource."""
        action: JsonValue = None
        if self.action_required:
            action = {
                "kind": "python-distribution-set",
                "profile-digest": self.registry.profile_digest,
                "mutable-resource": (
                    f"{self.registry.origin}/project/"
                    "hcoona-release-smoke-python"
                ),
                "operations": [
                    {
                        "ordinal": i,
                        "variant": a.variant,
                        "filename": a.filename,
                        "artifact-reference": a.reference.to_document(),
                    }
                    for i, a in enumerate(self.observation.decision.artifacts)
                ],
            }
        return {
            "schema": "workflow-delivery/v3/python-publication-snapshot",
            "attempt": self.attempt.to_document(),
            "observation-reference": self.observation_reference.to_document(),
            "action": action,
            "producer": "prepare-python-publication",
        }


def render_python_approval_summary(
    snapshot: PythonPublicationSnapshot,
) -> bytes:
    """Present the complete ordered set before approval transport exists."""
    decision = snapshot.observation.decision
    lines = [
        "Python distribution set approval",
        f"Target: {snapshot.attempt.execution.target}",
        f"Run: {snapshot.attempt.workflow_run_id}",
        f"Version: {decision.snapshot.model.provider.nbgv.pep440_version}",
        f"Destination: {snapshot.registry.origin}",
        f"Profile: {snapshot.registry.profile_digest}",
    ]
    lines.extend(
        f"{i}: {a.filename} {a.reference.payload_digest}"
        for i, a in enumerate(decision.artifacts)
    )
    lines.append(
        "Upload wheel once, verify exact bytes, then upload sdist once "
        "and verify the complete set. Partial success remains failure. "
        "No retry, completion, rollback or deletion."
    )
    return ("\n".join(lines) + "\n").encode()


@dataclass(frozen=True, slots=True)
class PythonApprovalBundle:
    """Reviewable complete set authority before Environment approval."""

    snapshot: PythonPublicationSnapshot
    snapshot_reference: ArtifactReference
    summary_reference: ArtifactReference

    def __post_init__(self) -> None:
        """Bind the exact reviewer summary and one action-bearing Snapshot."""
        _reference(self.snapshot_reference, self.snapshot.to_document())
        if (
            not self.snapshot.action_required
            or self.summary_reference.payload_digest
            != python_digest(self.summary)
        ):
            message = (
                "Python Approval Bundle must bind the complete set summary"
            )
            raise ValueError(message)

    @property
    def summary(self) -> bytes:
        """Present digests, operation order and partial-failure bounds."""
        return render_python_approval_summary(self.snapshot)

    def to_document(self) -> dict[str, JsonValue]:
        """Retain exact current Snapshot and reviewer payload references."""
        return {
            "schema": "workflow-delivery/v3/python-approval-bundle",
            "attempt": self.snapshot.attempt.to_document(),
            "snapshot-reference": self.snapshot_reference.to_document(),
            "summary-reference": self.summary_reference.to_document(),
            "environment": self.snapshot.registry.environment,
            "producer": "prepare-python-publication",
        }


@dataclass(frozen=True, slots=True)
class PythonPublicationAuthorization:
    """Current Environment approval, persisted before OIDC acquisition."""

    bundle: PythonApprovalBundle
    bundle_reference: ArtifactReference
    approval_evidence: bytes
    completed_at: datetime

    def __post_init__(self) -> None:
        """Close native deployment, reviewer and Environment evidence."""
        _reference(self.bundle_reference, self.bundle.to_document())
        snapshot = self.bundle.snapshot
        proof = python_object(
            parse_canonical_json(self.approval_evidence),
            {
                "repository",
                "run-id",
                "run-attempt",
                "target",
                "environment",
                "environment-id",
                "deployment-id",
                "reviewer-id",
                "reviewer",
                "state",
                "native-response-digest",
                "sentinel",
            },
        )
        configuration = cast(
            "dict[str, JsonValue]",
            snapshot.observation.decision.snapshot.governance.document[
                "configuration"
            ],
        )
        expected: dict[str, JsonValue] = {
            "repository": "hcoona/three",
            "run-id": snapshot.attempt.workflow_run_id,
            "run-attempt": 1,
            "target": snapshot.attempt.execution.target,
            "environment": snapshot.registry.environment,
            "environment-id": configuration["environment-id"],
            "reviewer-id": 712433,
            "reviewer": "hcoona",
            "state": "approved",
            "sentinel": snapshot.registry.environment + "/v1",
        }
        if (
            any(
                canonicalize(proof[k]) != canonicalize(v)
                for k, v in expected.items()
            )
            or type(proof["deployment-id"]) is not int
            or cast("int", proof["deployment-id"]) <= 0
        ):
            message = "Python publisher native current-run approval mismatch"
            raise ValueError(message)

        _digest(
            proof["native-response-digest"], field="approval response digest"
        )
        if self.completed_at < snapshot.observation.observed_at:
            message = "Python Authorization predates its Snapshot"
            raise ValueError(message)
        snapshot.observation.decision.snapshot.governance.require_live(
            self.completed_at
        )

    def to_document(self) -> dict[str, JsonValue]:
        """Persist authority before obtaining assertion or registry token."""
        return {
            "schema": "workflow-delivery/v3/python-publication-authorization",
            "attempt": self.bundle.snapshot.attempt.to_document(),
            "bundle-reference": self.bundle_reference.to_document(),
            "approval-evidence": parse_canonical_json(self.approval_evidence),
            "completed-at": _instant(self.completed_at),
            "producer": PYTHON_PUBLISHER,
        }


@dataclass(frozen=True, slots=True)
class PythonMutationMarker:
    """Durable one-set authority and fresh proofs before operation zero."""

    authorization: PythonPublicationAuthorization
    authorization_reference: ArtifactReference
    fresh_governance: PythonGovernance
    absence: PythonIndexObservation
    observed_at: datetime

    def __post_init__(self) -> None:
        """Reject changed authority, nonempty state or stale preparation."""
        _reference(
            self.authorization_reference, self.authorization.to_document()
        )
        snapshot = self.authorization.bundle.snapshot
        snapshot.observation.decision.snapshot.governance.require_same_live(
            self.fresh_governance, self.observed_at
        )
        if (
            self.absence.registry != snapshot.registry
            or self.absence.version != snapshot.observation.native.version
            or self.absence.classification != "absent"
            or self.absence.files
            or self.observed_at < self.authorization.completed_at
            or self.fresh_governance.observed_at
            < self.authorization.completed_at
        ):
            message = "Python pre-marker state or authority drifted"
            raise ValueError(message)

    @property
    def attempt(self) -> ReleaseAttemptIdentity:
        """Expose the existing Attempt identity for scalar finalization."""
        return self.authorization.bundle.snapshot.attempt

    def to_document(self) -> dict[str, JsonValue]:
        """Bind one marker to both approved operations through Authorization."""
        return {
            "schema": "workflow-delivery/v3/python-mutation-may-have-started",
            "attempt": self.attempt.to_document(),
            "authorization-reference": (
                self.authorization_reference.to_document()
            ),
            "governance-digest": self.fresh_governance.digest,
            "governance-source-commit": self.fresh_governance.source_commit,
            "governance-observed-at": _instant(
                self.fresh_governance.observed_at
            ),
            "profile-digest": self.absence.registry.profile_digest,
            "absence": self.absence.to_document(),
            "observed-at": _instant(self.observed_at),
            "producer": PYTHON_PUBLISHER,
        }


@dataclass(frozen=True, slots=True)
class PythonOperationResult:
    """One fixed action ordinal's sanitized invocation and readback facts."""

    ordinal: int
    status: str
    response_digest: str | None
    readback_digest: str | None
    readback_exact: bool
    observation: JsonValue = None

    def __post_init__(self) -> None:
        """Reject unsupported operation states or invented response evidence."""
        if (
            type(self.ordinal) is not int
            or self.ordinal not in {0, 1}
            or self.status
            not in {"not-attempted", "succeeded", "failed", "unknown"}
            or type(self.readback_exact) is not bool
        ):
            message = "invalid Python operation Result"
            raise ValueError(message)

        for digest in (self.response_digest, self.readback_digest):
            if digest is not None:
                _digest(digest, field="Python operation response")
        if (
            (
                self.status == "not-attempted"
                and (
                    self.response_digest is not None
                    or self.readback_digest is not None
                    or self.readback_exact
                )
            )
            or (
                self.status in {"succeeded", "failed"}
                and self.response_digest is None
            )
            or (self.readback_exact and self.readback_digest is None)
        ):
            message = "Python operation state lacks its exact evidence"
            raise ValueError(message)

    def to_document(self) -> dict[str, JsonValue]:
        """Refer to an ordinal, without repeating requested coordinates."""
        return {
            "ordinal": self.ordinal,
            "status": self.status,
            "response-digest": self.response_digest,
            "readback-digest": self.readback_digest,
            "readback-exact": self.readback_exact,
            "observation": self.observation,
        }


@dataclass(frozen=True, slots=True)
class PythonPublicationResult:
    """Strict Python variant of the shared Publication Result wire schema."""

    attempt: ReleaseAttemptIdentity
    mutation_marker_reference: ArtifactReference
    operations: tuple[PythonOperationResult, PythonOperationResult]
    final_readback_digest: str | None
    final_readback_exact: bool

    def __post_init__(self) -> None:
        """Close both ordinals and forbid sdist after unsafe wheel outcome."""
        if (
            type(self.attempt) is not ReleaseAttemptIdentity
            or type(self.mutation_marker_reference) is not ArtifactReference
            or type(self.operations) is not tuple
            or tuple(o.ordinal for o in self.operations) != (0, 1)
            or type(self.final_readback_exact) is not bool
        ):
            message = (
                "Python Publication Result requires exactly two operations"
            )
            raise ValueError(message)
        wheel, sdist = self.operations
        if sdist.status != "not-attempted" and (
            wheel.status != "succeeded" or not wheel.readback_exact
        ):
            message = "Python sdist invocation lacks definitive wheel success"
            raise ValueError(message)
        if self.final_readback_digest is not None:
            _digest(self.final_readback_digest, field="Python final readback")
        if self.final_readback_exact and (
            self.final_readback_digest is None
            or any(
                o.status != "succeeded" or not o.readback_exact
                for o in self.operations
            )
            or self.final_readback_digest != sdist.readback_digest
        ):
            message = "Python final exactness requires both exact readbacks"
            raise ValueError(message)
        if (
            not self.final_readback_exact
            and self.final_readback_digest is not None
        ):
            message = (
                "Python inexact final readback cannot retain an exact digest"
            )
            raise ValueError(message)

    @property
    def result(self) -> str:
        """Require two definitive successes and exact whole-set readback."""
        return (
            "published"
            if self.final_readback_exact
            and all(o.status == "succeeded" for o in self.operations)
            else "failed"
        )

    @property
    def mutation_classification(self) -> str:
        """Preserve partial effects and conservative ambiguity."""
        states = {o.status for o in self.operations}
        if states == {"not-attempted"}:
            return "not-mutated"
        if states & {"unknown", "failed"}:
            return "possibly-mutated"
        return "mutated"

    def to_document(self) -> dict[str, JsonValue]:
        """Keep one scalar Result lineage with a closed Python discriminator."""
        return {
            "schema": PUBLICATION_RESULT_SCHEMA,
            "variant": "python-distribution-set",
            "attempt": self.attempt.to_document(),
            "mutation-marker-reference": (
                self.mutation_marker_reference.to_document()
            ),
            "operations": [o.to_document() for o in self.operations],
            "final-readback-digest": self.final_readback_digest,
            "final-readback-exact": self.final_readback_exact,
            "result": self.result,
            "mutation-classification": self.mutation_classification,
            "producer": PYTHON_PUBLISHER,
        }

    @property
    def result_digest(self) -> str:
        """Return the immutable Result payload digest."""
        return canonical_sha256(self.to_document())


def execute_python_publication(  # noqa: PLR0913, PLR0915
    marker: PythonMutationMarker,
    marker_reference: ArtifactReference,
    payloads: tuple[bytes, bytes],
    *,
    token: str,
    transport: PythonHttpTransport,
    claim_path: Path,
    clock: Callable[[], datetime],
    monotonic: Callable[[], float] = time.monotonic,
    wait: Callable[[float], None] = time.sleep,
) -> PythonPublicationResult:
    """Consume a read-admitted durable marker, then send each original once."""
    _reference(marker_reference, marker.to_document())
    snapshot = marker.authorization.bundle.snapshot
    decision = snapshot.observation.decision
    admitted_utc = clock()
    marker.fresh_governance.require_live(admitted_utc)
    admitted_monotonic = number(monotonic())
    expiry = datetime.fromisoformat(
        python_text(marker.fresh_governance.document["expires-at"])
    )
    deadline = admitted_monotonic + (expiry - admitted_utc).total_seconds()
    authority: dict[str, JsonValue] = {
        "utc": _instant(admitted_utc),
        "monotonic": admitted_monotonic,
        "deadline": deadline,
    }
    originals = tuple(
        a.inspect(p) for a, p in zip(decision.artifacts, payloads, strict=True)
    )
    previous = marker.absence.index_response
    if (
        previous is None
        or python_digest(previous.body) != marker.absence.index_digest
        or index_inventory(snapshot.registry, previous, marker.absence.version)
    ):
        message = (
            "Python publication requires verified existing-project absence"
        )
        raise ValueError(message)
    timed = PublicationTransport(
        transport,
        token,
        monotonic,
        deadline=deadline,
        admit=lambda: marker.fresh_governance.require_live(clock()),
    )
    # Platform current-run admission and concurrency own cross-process replay;
    # this exclusive task-owned claim prevents accidental reuse in this job.
    with claim_path.open("xb"):
        pass
    entries = [
        PythonOperationResult(
            i, "not-attempted", None, None, readback_exact=False
        )
        for i in (0, 1)
    ]
    evidence_root = claim_path.with_name(claim_path.name + "-observations")
    evidence_root.mkdir(exist_ok=False)

    def retain(name: str, content: bytes) -> None:
        path = evidence_root / name
        path.parent.mkdir(parents=True, exist_ok=True)
        with path.open("xb") as stream:
            stream.write(content)

    final_digest = None
    final_exact = False
    for ordinal, distribution in enumerate(originals):
        invocation = upload_python_once(
            snapshot.registry, distribution, token, timed
        )
        status = {
            "definitive-success": "succeeded",
            "definitive-non-success": "failed",
            "ambiguous": "unknown",
        }[invocation.classification]
        readback_digest = None
        exact = False
        observation: JsonValue = None
        if status == "succeeded":
            phase = IndexPhase(
                ObservationBasis(
                    snapshot.registry,
                    f"normal-{ordinal}",
                    distribution,
                    previous,
                    invocation,
                    deadline,
                )
            )
            downloads: list[JsonValue] = []
            observation = {
                "authority": authority,
                "phase": phase.trace,
                "downloads": downloads,
                "upload-started": timed.upload_started,
            }
            prefix = f"operation-{ordinal}/"
            retain(
                prefix + "admission.json",
                canonicalize(
                    {
                        "authority": authority,
                        "upload-started": timed.upload_started,
                        "basis": phase.basis.document(),
                    }
                ),
            )
            try:
                final_index = phase.run(
                    timed,
                    retain=lambda name, content, prefix=prefix: retain(
                        prefix + name, content
                    ),
                    clock=monotonic,
                    wait=wait,
                )
                readback = read_python_index(
                    snapshot.registry,
                    distribution.witness,
                    ReadbackTransport(
                        snapshot.registry,
                        final_index,
                        timed,
                        downloads,
                        retain=lambda name, content, prefix=prefix: retain(
                            prefix + name, content
                        ),
                    ),
                )
                readback_digest = canonical_sha256(readback.to_document())
                exact = _exact_files(
                    readback, decision, wheel_only=ordinal == 0
                )
                previous = final_index
            except (OSError, ValueError, TypeError):
                # Failure is retained; no readback can authorize a resend.
                exact = False
        entries[ordinal] = PythonOperationResult(
            ordinal,
            status,
            invocation.response_digest,
            readback_digest,
            exact,
            observation,
        )
        if status != "succeeded" or not exact:
            break
        if ordinal == 1:
            final_digest, final_exact = readback_digest, exact
    return PythonPublicationResult(
        marker.attempt,
        marker_reference,
        (entries[0], entries[1]),
        final_digest,
        final_exact,
    )


def python_publication_result_from_document(
    value: JsonValue,
) -> PythonPublicationResult:
    """Admit only the two-operation Python variant of Publication Result."""
    from three_workflow_delivery_v3.records.artifacts import (  # noqa: PLC0415
        artifact_reference_from_document,
    )
    from three_workflow_delivery_v3.records.release_transport import (  # noqa: PLC0415
        _release_attempt,
    )
    from three_workflow_delivery_v3.repository.python_provider import (  # noqa: PLC0415
        python_text,
    )

    doc = python_object(
        value,
        {
            "schema",
            "variant",
            "attempt",
            "mutation-marker-reference",
            "operations",
            "final-readback-digest",
            "final-readback-exact",
            "result",
            "mutation-classification",
            "producer",
        },
    )
    operations = doc["operations"]
    if not isinstance(operations, list) or len(operations) != len((0, 1)):
        message = "Python Publication Result requires two serialized operations"
        raise ValueError(message)
    entries = []
    for operation_value in operations:
        operation = python_object(
            operation_value,
            {
                "ordinal",
                "status",
                "response-digest",
                "readback-digest",
                "readback-exact",
                "observation",
            },
        )
        if operation["status"] == "succeeded":
            validate_observation_document(operation["observation"])
        elif operation["observation"] is not None:
            message = "Python Result has observation without upload success"
            raise ValueError(message)
        entries.append(
            PythonOperationResult(
                cast("int", operation["ordinal"]),
                python_text(operation["status"]),
                cast("str | None", operation["response-digest"]),
                cast("str | None", operation["readback-digest"]),
                cast("bool", operation["readback-exact"]),
                operation["observation"],
            )
        )
    result = PythonPublicationResult(
        _release_attempt(doc["attempt"]),
        artifact_reference_from_document(doc["mutation-marker-reference"]),
        (entries[0], entries[1]),
        cast("str | None", doc["final-readback-digest"]),
        cast("bool", doc["final-readback-exact"]),
    )
    if canonicalize(result.to_document()) != canonicalize(doc):
        message = (
            "Python Publication Result is not its strict normalized variant"
        )
        raise ValueError(message)
    return result


def audit_python_publication_result(  # noqa: C901, PLR0912, PLR0915 - strict ordered evidence replay
    result: PythonPublicationResult, marker: PythonMutationMarker
) -> None:
    """Verify immutable observations against the approved artifacts."""
    decision = marker.authorization.bundle.snapshot.observation.decision
    previous = marker.absence.index_response
    if (
        previous is None
        or python_digest(previous.body) != marker.absence.index_digest
        or index_inventory(
            marker.absence.registry, previous, marker.absence.version
        )
    ):
        message = "Python Result lacks verified previous inventory"
        raise ValueError(message)
    previous_finish = 0.0
    original_authority = None
    for operation, artifact in zip(
        result.operations, decision.artifacts, strict=True
    ):
        if operation.status != "succeeded":
            if (
                operation.observation is not None
                or operation.readback_exact
                or operation.readback_digest is not None
            ):
                message = "Python Result has readback without upload success"
                raise ValueError(message)
            continue
        evidence = python_object(
            operation.observation,
            {"phase", "downloads", "upload-started", "authority"},
        )
        phase = cast("dict[str, JsonValue]", evidence["phase"])
        if not isinstance(phase, dict):
            message = "Python Result lacks complete observation phase"
            raise ValueError(message)  # noqa: TRY004 - invalid phase document
        authority = python_object(
            evidence["authority"], {"utc", "monotonic", "deadline"}
        )
        admitted_utc = datetime.fromisoformat(python_text(authority["utc"]))
        marker.fresh_governance.require_live(admitted_utc)
        expiry = datetime.fromisoformat(
            python_text(marker.fresh_governance.document["expires-at"])
        )
        deadline = (
            number(authority["monotonic"])
            + (expiry - admitted_utc).total_seconds()
        )
        if (
            admitted_utc < marker.observed_at
            or number(authority["deadline"]) != deadline
            or (
                original_authority is not None
                and original_authority != authority
            )
        ):
            message = "Python Result authority window differs"
            raise ValueError(message)
        original_authority = authority
        upload_start = number(evidence["upload-started"])
        if not number(authority["monotonic"]) <= upload_start < deadline:
            message = "Python Result upload authority expired"
            raise ValueError(message)
        if (
            not previous_finish
            <= upload_start
            <= number(phase.get("upload-finished"))
            <= upload_start + 30
        ):
            message = "Python Result upload order differs"
            raise ValueError(message)
        basis = ObservationBasis(
            marker.absence.registry,
            f"normal-{operation.ordinal}",
            ExpectedAddition(
                artifact.filename,
                artifact.reference.payload_digest,
                artifact.witness,
            ),
            previous,
            PythonUploadResponse(
                "definitive-success",
                200,
                operation.response_digest,
                cast("float", phase.get("upload-finished")),
            ),
            deadline,
        )
        if phase.get("terminal") != "exact":
            replay_failed_index_phase(phase, basis)
            if (
                operation.readback_exact
                or operation.readback_digest is not None
                or evidence["downloads"] != []
            ):
                message = "Python Result failed phase has exact readback"
                raise ValueError(message)
            continue
        final_index = replay_index_phase(phase, basis)
        readback = replay_readback(
            marker.absence.registry,
            artifact.witness,
            final_index,
            evidence["downloads"],
            after=number(phase["stopped-at"]),
            deadline=deadline,
        )
        if readback is None:
            if (
                operation.readback_exact
                or operation.readback_digest is not None
            ):
                message = "Python Result exact claim lacks downloaded originals"
                raise ValueError(message) from None
            continue
        exact = _exact_files(
            readback, decision, wheel_only=operation.ordinal == 0
        )
        if (
            operation.readback_digest
            != canonical_sha256(readback.to_document())
            or operation.readback_exact is not exact
        ):
            message = "Python Result readback differs from original evidence"
            raise ValueError(message)
        previous = final_index
        if any(
            number(cast("dict", item)["start"]) >= deadline
            for item in cast("list", evidence["downloads"])
        ):
            message = "Python Result download authority expired"
            raise ValueError(message)
        previous_finish = max(
            number(cast("dict", item)["finish"])
            for item in cast("list", evidence["downloads"])
        )
