"""Supported native active-state observation after authority admission."""

from __future__ import annotations

import hashlib
import re
from datetime import datetime
from typing import TYPE_CHECKING, Literal, cast

from three_workflow_delivery_v3.adapters import nuget_github_packages as native
from three_workflow_delivery_v3.canonical import (
    canonical_sha256,
    parse_json_strict,
)
from three_workflow_delivery_v3.records.release import (
    NugetDestinationReadback,
    NugetExternalPackageCoordinate,
    NugetRemoteStateObservation,
    PackageControlProof,
    PackageControlSubject,
    PublicationDiagnostics,
)
from three_workflow_delivery_v3.release.nuget_eligibility import (
    AdmittedNugetLiveEligibilityDecision,
)
from three_workflow_delivery_v3.release.observation import (
    _native_acceptance_expiry,
    _require_action_freshness,
    _validate_basis,
    validate_remote_state_observation_basis,
)
from three_workflow_delivery_v3.repository.descriptors import (
    NUGET_PACKAGE,
    NUGET_RELEASE_UNIT,
)

if TYPE_CHECKING:
    from three_workflow_delivery_v3.canonical import JsonValue
    from three_workflow_delivery_v3.records.artifacts import ArtifactReference
    from three_workflow_delivery_v3.records.release import (
        NugetReleaseArtifact,
        QualificationDecision,
        QualificationSnapshot,
        ReleaseAttemptBinding,
        ReleaseIntent,
    )
    from three_workflow_delivery_v3.repository.descriptors import ReleasePolicy

NUGET_CONTROL_ENDPOINT = (
    f"https://api.github.com/users/hcoona/packages/nuget/{NUGET_PACKAGE}"
)


def validate_nuget_package_subject(
    subject: PackageControlSubject,
    eligibility: AdmittedNugetLiveEligibilityDecision,
) -> None:
    """Require the native subject and its Governance principal."""
    attestation = eligibility.governance.attestation
    if (
        subject
        != PackageControlSubject(
            native.NUGET_DESTINATION_ID,
            native.NUGET_SERVICE_INDEX,
            NUGET_PACKAGE.lower(),
        )
        or attestation.release_policy != NUGET_RELEASE_UNIT
        or attestation.package != NUGET_PACKAGE
        or attestation.package_principal.intended_coordinate != NUGET_PACKAGE
        or attestation.package_principal.repository != "hcoona/three"
    ):
        message = "NuGet package subject differs from its Governance"
        raise ValueError(message)


def classify_nuget_package_control(
    proof: PackageControlProof | None,
    *,
    subject: PackageControlSubject,
    eligibility: AdmittedNugetLiveEligibilityDecision,
) -> Literal["ready", "conflicting", "unprovable"]:
    """Compare actual supported package facts; empty access is not no grants."""
    validate_nuget_package_subject(subject, eligibility)
    if proof is None:
        return "unprovable"
    if proof.subject != subject:
        return "conflicting"
    if proof.endpoints != (NUGET_CONTROL_ENDPOINT,):
        return "unprovable"
    expected = (
        ("exposed-access", ()),
        ("owner", ("hcoona",)),
        ("repository-association", ("hcoona/three",)),
        ("visibility", ("public",)),
    )
    return "ready" if proof.facts == expected else "conflicting"


def nuget_readback_from_state(
    state: native.NuGetActiveState,
    *,
    artifact: NugetReleaseArtifact,
    observed_at: str,
) -> tuple[PackageControlProof, NugetDestinationReadback]:
    """Project actual archive and supported active-state facts."""
    identity = artifact.identity
    if (
        state.identity.normalized_package_id,
        state.identity.normalized_version,
    ) != (identity.normalized_package_id, identity.normalized_version):
        message = "NuGet active state belongs to another native coordinate"
        raise ValueError(message)
    responses = {response.url: response for response in state.exchanges}
    control_response = responses.get(NUGET_CONTROL_ENDPOINT)
    if control_response is None:
        message = "NuGet package-control response is missing"
        raise ValueError(message)
    control = state.package_control
    repository = cast("dict[str, JsonValue]", control["repository"])
    # The admitted authenticated user endpoint establishes its owner when the
    # response omits owner. The adapter rejects a conflicting explicit owner.
    proof = PackageControlProof(
        PackageControlSubject(
            native.NUGET_DESTINATION_ID,
            native.NUGET_SERVICE_INDEX,
            identity.normalized_package_id,
        ),
        observed_at,
        (NUGET_CONTROL_ENDPOINT,),
        (
            ("exposed-access", ()),
            ("owner", ("hcoona",)),
            ("repository-association", (cast("str", repository["full_name"]),)),
            ("visibility", (cast("str", control["visibility"]),)),
        ),
        (
            (
                NUGET_CONTROL_ENDPOINT,
                "sha256:" + hashlib.sha256(control_response.body).hexdigest(),
            ),
        ),
    )
    package = state.package
    sha256 = sha512 = witness_digest = witness_target = None
    classification = "absent"
    if package is not None:
        sha256, sha512 = "sha256:" + package.sha256, "sha512:" + package.sha512
        witness_digest = "sha256:" + package.witness_sha256
        witness = parse_json_strict(package.witness)
        target = witness.get("target") if isinstance(witness, dict) else None
        witness_target = (
            target
            if isinstance(target, str) and re.fullmatch(r"[0-9a-f]{40}", target)
            else None
        )
        classification = (
            "unprovable" if witness_target is None else "conflicting"
        )
        if (
            package.identity.normalized_package_id,
            package.identity.normalized_version,
            sha256,
            sha512,
            witness_digest,
            witness_target,
        ) == (
            identity.normalized_package_id,
            identity.normalized_version,
            artifact.content.content_sha256,
            artifact.content.content_sha512,
            artifact.witness_digest,
            artifact.target,
        ):
            classification = "exact-satisfied"
    readback = NugetDestinationReadback(
        identity,
        classification,
        sha256,
        sha512,
        witness_digest,
        witness_target,
        observed_at,
        tuple(
            sorted(
                (url, "sha256:" + hashlib.sha256(response.body).hexdigest())
                for url, response in responses.items()
            )
        ),
    )
    return proof, readback


def admit_nuget_remote_state_observation(  # noqa: PLR0913
    observation: NugetRemoteStateObservation,
    *,
    intent: ReleaseIntent,
    attempt_binding: ReleaseAttemptBinding,
    eligibility: AdmittedNugetLiveEligibilityDecision,
    policy: ReleasePolicy,
    snapshot: QualificationSnapshot,
    decision: QualificationDecision,
    decision_reference: ArtifactReference,
    artifact: NugetReleaseArtifact,
    action_creation_at: datetime | None = None,
) -> NugetRemoteStateObservation:
    """Close native desired state and the original current-Attempt authority."""
    if (
        type(observation) is not NugetRemoteStateObservation
        or type(eligibility) is not AdmittedNugetLiveEligibilityDecision
    ):
        message = "NuGet Observation requires native admitted variants"
        raise TypeError(message)
    projection = _validate_basis(
        intent=intent,
        attempt_binding=attempt_binding,
        eligibility=eligibility,
        policy=policy,
        snapshot=snapshot,
        decision=decision,
        decision_reference=decision_reference,
        artifact=artifact,
    )
    if type(projection.coordinate) is not NugetExternalPackageCoordinate:
        message = "NuGet Observation requires a native projection"
        raise TypeError(message)
    subject = PackageControlSubject(
        projection.destination_id,
        projection.registry,
        projection.coordinate.identity.normalized_package_id,
    )
    if (
        observation.attempt != attempt_binding.attempt
        or observation.qualification_decision_reference != decision_reference
        or observation.desired_subject != subject
        or observation.desired_identity != artifact.identity
        or observation.desired_identity != projection.coordinate.identity
        or observation.desired_content_sha256 != artifact.content.content_sha256
        or observation.desired_content_sha512 != artifact.content.content_sha512
        or observation.desired_witness_digest != artifact.witness_digest
    ):
        message = "NuGet Observation differs from qualified desired state"
        raise ValueError(message)
    control = classify_nuget_package_control(
        observation.package_control, subject=subject, eligibility=eligibility
    )
    if (
        observation.classification in {"absent", "exact-satisfied"}
        and control != "ready"
    ):
        message = "Ready NuGet Observation package control is not admitted"
        raise ValueError(message)
    if observation.classification == "absent":
        proof = cast("PackageControlProof", observation.package_control)
        readback = cast("NugetDestinationReadback", observation.active_readback)
        if action_creation_at is not None:
            _require_action_freshness(eligibility, action_creation_at)
        for timestamp in (proof.observed_at, readback.observed_at):
            instant = datetime.fromisoformat(timestamp)
            _require_action_freshness(eligibility, instant)
            if action_creation_at is not None and instant > action_creation_at:
                message = "NuGet action creation precedes Observation evidence"
                raise ValueError(message)
    return observation


def observe_nuget_remote_state(  # noqa: PLR0913
    *,
    intent: ReleaseIntent,
    attempt_binding: ReleaseAttemptBinding,
    eligibility: AdmittedNugetLiveEligibilityDecision,
    policy: ReleasePolicy,
    snapshot: QualificationSnapshot,
    decision: QualificationDecision,
    decision_reference: ArtifactReference,
    artifact: NugetReleaseArtifact,
    authority: native.NuGetAuthority,
    token: str,
    transport: native.NuGetReadTransport,
    now: datetime,
) -> NugetRemoteStateObservation:
    """Read only after authority admission; incomplete reads remain blocked."""
    validate_remote_state_observation_basis(
        intent=intent,
        attempt_binding=attempt_binding,
        eligibility=eligibility,
        policy=policy,
        snapshot=snapshot,
        decision=decision,
        decision_reference=decision_reference,
        artifact=artifact,
        now=now,
    )
    proof = readback = None
    response_identity = None
    diagnostics: tuple[str, ...] = ()
    classification = "unprovable"
    try:
        state = native.read_nuget_active_state(
            transport=transport,
            authority=authority,
            token=token,
            package_id=artifact.identity.package_name,
            version=artifact.identity.native_version,
        )
        if (
            state.resources.package_publish
            != eligibility.profile.to_document()["packagePublish"]
        ):
            message = (
                "NuGet discovered publication resource "
                "differs from admitted profile"
            )
            raise ValueError(message)  # noqa: TRY301 - local failure cleanup/classification
        proof, readback = nuget_readback_from_state(
            state,
            artifact=artifact,
            observed_at=now.isoformat().replace("+00:00", "Z"),
        )
        response_identity = canonical_sha256(
            {"responses": [list(pair) for pair in readback.response_digests]}
        )
        control = classify_nuget_package_control(
            proof, subject=proof.subject, eligibility=eligibility
        )
        classification = (
            readback.classification if control == "ready" else control
        )
        if classification == "absent" and now > _native_acceptance_expiry(
            eligibility
        ):
            classification = "unprovable"
            diagnostics = ("NuGet native acceptance expired",)
    except (
        native.NuGetAdapterError,
        native.NuGetTransportError,
        OSError,
        ValueError,
    ) as error:
        diagnostics = (f"NuGet active readback failed: {type(error).__name__}",)
    observation = NugetRemoteStateObservation(
        attempt_binding.attempt,
        decision_reference,
        PackageControlSubject(
            native.NUGET_DESTINATION_ID,
            native.NUGET_SERVICE_INDEX,
            artifact.identity.normalized_package_id,
        ),
        artifact.identity,
        artifact.content.content_sha256,
        cast("str", artifact.content.content_sha512),
        artifact.witness_digest,
        classification,
        proof,
        readback,
        response_identity,
        PublicationDiagnostics(entries=diagnostics, truncated=False),
        "observe-github-packages",
        eligibility.context.control,
        intent.workflow_run_id,
    )
    return admit_nuget_remote_state_observation(
        observation,
        intent=intent,
        attempt_binding=attempt_binding,
        eligibility=eligibility,
        policy=policy,
        snapshot=snapshot,
        decision=decision,
        decision_reference=decision_reference,
        artifact=artifact,
    )
