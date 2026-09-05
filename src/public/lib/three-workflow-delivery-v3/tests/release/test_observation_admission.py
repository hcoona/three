"""Contextual authority scenarios, not HTTP or record-shape permutations."""

# ruff: noqa: D103

from __future__ import annotations

import hashlib
from dataclasses import dataclass, replace
from datetime import timedelta
from typing import TYPE_CHECKING, Any

import pytest
from three_workflow_delivery_v3.canonical import canonical_sha256, canonicalize
from three_workflow_delivery_v3.records.artifacts import (
    ArtifactContentIdentity,
    ArtifactReference,
    ArtifactTransportIdentity,
)
from three_workflow_delivery_v3.records.release import (
    DestinationReadback,
    PackageControlProof,
    PackageControlSubject,
    PublicationDiagnostics,
    RemoteStateObservation,
    admit_release_record,
    release_artifact_transport_name,
    release_record_digest,
)
from three_workflow_delivery_v3.release.eligibility import (
    AdmittedLiveEligibilityDecision,
    EnabledGovernanceActivation,
    LiveEligibilityAdmissionMode,
    admit_live_eligibility_decision,
)
from three_workflow_delivery_v3.release.finalizer import finalize_qualification
from three_workflow_delivery_v3.release.identity import (
    derive_buddy_execution_identity,
    derive_release_attempt_binding,
    normalize_buddy_live_intent,
)
from three_workflow_delivery_v3.release.observation import (
    admit_remote_state_observation,
    classify_package_control,
    validate_remote_state_observation_basis,
)
from three_workflow_delivery_v3.release.planner import plan_live_qualification
from three_workflow_delivery_v3.release.qualification import (
    MechanicalBuildResult,
    form_uploaded_release_artifact,
)

from . import test_eligibility as eligibility_fixtures

if TYPE_CHECKING:
    from three_workflow_delivery_v3.records.release import (
        QualificationDecision,
        QualificationSnapshot,
        ReleaseArtifact,
        ReleaseAttemptBinding,
        ReleaseIntent,
        ReleaseRecord,
    )
    from three_workflow_delivery_v3.repository.compiler import (
        AdmittedRepositoryModelSnapshot,
    )
    from three_workflow_delivery_v3.repository.descriptors import ReleasePolicy

NOW = eligibility_fixtures.NOW
OBSERVED_AT = NOW.isoformat().replace("+00:00", "Z")
DIGEST = "sha256:" + "a" * 64
OTHER_DIGEST = "sha256:" + "b" * 64
ENDPOINT = (
    "https://api.github.com/users/hcoona/packages/npm/hcoona-release-smoke-npm"
)


def _parsed[T: ReleaseRecord](record: T) -> T:
    admitted = admit_release_record(
        canonicalize(record.to_document()),
        expected_type=type(record),
        expected_digest=release_record_digest(record),
    )
    assert isinstance(admitted, type(record))
    return admitted


@dataclass(frozen=True)
class ObservationCase:
    """One canonical current lineage without synthetic provenance shortcuts."""

    intent: ReleaseIntent
    model: AdmittedRepositoryModelSnapshot
    policy: ReleasePolicy
    attempt_binding: ReleaseAttemptBinding
    eligibility: AdmittedLiveEligibilityDecision
    snapshot: QualificationSnapshot
    artifact: ReleaseArtifact
    decision: QualificationDecision
    decision_reference: ArtifactReference

    def arguments(self) -> dict[str, Any]:
        """Supply the same authority inputs to producer and consumer."""
        return {
            "intent": self.intent,
            "attempt_binding": self.attempt_binding,
            "eligibility": self.eligibility,
            "policy": self.policy,
            "snapshot": self.snapshot,
            "artifact": self.artifact,
            "decision": self.decision,
            "decision_reference": self.decision_reference,
        }


@pytest.fixture
def observation_case(
    request: pytest.FixtureRequest,
    monkeypatch: pytest.MonkeyPatch,
    live_intent: ReleaseIntent,
    live_admitted_repository_model: AdmittedRepositoryModelSnapshot,
    policy: ReleasePolicy,
) -> ObservationCase:
    # This existing test-only registry seam is not native acceptance evidence.
    eligibility_fixtures._admit_test_destination_primitive(monkeypatch)  # noqa: SLF001
    produced = eligibility_fixtures._transport_decision(  # noqa: SLF001
        live_intent, live_admitted_repository_model, policy
    )
    attestation = produced.governance.attestation
    activation = attestation.activation
    assert isinstance(activation, EnabledGovernanceActivation)
    attestation = replace(
        attestation,
        activation=replace(
            activation,
            destination_primitive=replace(
                activation.destination_primitive,
                captured_at=getattr(request, "param", attestation.inspected_at),
            ),
        ),
    )
    produced = replace(
        produced,
        governance=replace(
            produced.governance,
            attestation=attestation,
            canonical_content_digest=attestation.content_digest,
        ),
    )
    eligibility = admit_live_eligibility_decision(
        canonicalize(produced.to_document()),
        intent=live_intent,
        repository_model=live_admitted_repository_model,
        policy=policy,
        expected_digest=produced.decision_digest,
        admission_mode=LiveEligibilityAdmissionMode.CURRENT_FRESHNESS,
        now=NOW,
    )
    binding = _parsed(
        derive_release_attempt_binding(
            intent=live_intent,
            execution=derive_buddy_execution_identity(live_intent),
            repository_model_digest=live_admitted_repository_model.canonical_digest,
            live_eligibility_artifact_id=7001,
            live_eligibility_artifact_digest=DIGEST,
            live_eligibility_payload_digest=eligibility.canonical_digest,
            attestation_provenance=eligibility.governance.provenance,
        )
    )
    snapshot = _parsed(
        plan_live_qualification(
            live_intent, binding, live_admitted_repository_model
        )
    )
    build_request = snapshot.build_requests[0]
    # Only the qualified byte identity is consumed here; Node execution and
    # tarball-content qualification have their own Adapter tests.
    tarball = b"observation-qualified-live-tarball"
    content = ArtifactContentIdentity(
        output_id=build_request.output.output_id,
        logical_role=build_request.output.logical_role,
        media_kind=build_request.output.media_kind,
        basename="hcoona-release-smoke-npm.tgz",
        byte_size=len(tarball),
        content_sha256="sha256:" + hashlib.sha256(tarball).hexdigest(),
        content_sha512="sha512:" + hashlib.sha512(tarball).hexdigest(),
    )
    mechanics = MechanicalBuildResult(
        subject=binding.attempt,
        repository=snapshot.repository,
        qualification_snapshot_digest=snapshot.snapshot_digest,
        repository_model_digest=snapshot.repository_model_digest,
        target=snapshot.target,
        purpose="live-release",
        output=build_request.output,
        build_request_digest=build_request.request_digest,
        tarball=tarball,
        content=content,
        entries=("package/workflow-delivery/provenance.json",),
        lifecycle_scripts=(),
        witness_digest=build_request.witness_digest,
        source_input_manifest=tuple(
            (path, canonical_sha256(path))
            for path in build_request.declared_inputs
        ),
        toolchain=(("node", "v24.14.0"), ("npm", "11.9.0")),
        raw_result="success",
        normalized_outcome="satisfied",
    )
    run_id = binding.attempt.workflow_run_id
    transport = ArtifactTransportIdentity(
        artifact_id=801,
        artifact_name=release_artifact_transport_name(
            repository=snapshot.repository,
            purpose="live-release",
            output=build_request.output,
            qualification_snapshot_digest=snapshot.snapshot_digest,
            workflow_run_id=run_id,
            run_attempt=None,
            producer="build-tarball",
        ),
        artifact_url=(
            f"https://github.com/hcoona/three/actions/runs/{run_id}/artifacts/801"
        ),
        transport_digest=DIGEST,
        producer="build-tarball",
        workflow_run_id=run_id,
        run_attempt=None,
    )
    artifact, build_evidence = form_uploaded_release_artifact(
        snapshot, mechanics, transport
    )
    artifact = _parsed(artifact)
    evidence = tuple(
        _parsed(
            replace(
                build_evidence,
                evidence_id=obligation.expected_evidence_id,
                obligation=obligation,
                producer=(
                    "project-test"
                    if obligation.obligation_id
                    == "release:quality:project-test"
                    else "npm-artifact-qualification"
                ),
                artifact_digests=(
                    ()
                    if obligation.obligation_id
                    == "release:quality:project-test"
                    else (artifact.artifact_digest,)
                ),
                result_facts=(),
            )
        )
        for obligation in snapshot.obligations
        if obligation != build_evidence.obligation
    )
    decision = _parsed(
        finalize_qualification(
            snapshot, (_parsed(build_evidence), *evidence), (artifact,)
        )
    )
    assert decision.terminal_result == "success"
    reference = ArtifactReference(
        artifact_id=802,
        artifact_digest=DIGEST,
        artifact_url=(
            f"https://github.com/hcoona/three/actions/runs/{run_id}/artifacts/802"
        ),
        payload_path="qualification-decision.json",
        payload_digest=decision.decision_digest,
    )
    return ObservationCase(
        live_intent,
        live_admitted_repository_model,
        policy,
        binding,
        eligibility,
        snapshot,
        artifact,
        decision,
        reference,
    )


def _observation(
    case: ObservationCase,
    *,
    classification: str = "exact-satisfied",
    tag_state: str = "absent",
) -> RemoteStateObservation:
    projection = case.snapshot.destination_projections[0]
    artifact = case.artifact
    subject = PackageControlSubject(
        projection.destination_id,
        projection.registry,
        projection.coordinate.package_name,
    )
    control = PackageControlProof(
        subject=subject,
        observed_at=OBSERVED_AT,
        endpoints=(ENDPOINT,),
        facts=(
            ("exposed-access", ()),
            ("owner", ("hcoona",)),
            ("repository-association", ("hcoona/three",)),
            ("visibility", ("public",)),
        ),
        response_digests=((ENDPOINT, DIGEST),),
    )
    exact = classification == "exact-satisfied"
    readback = DestinationReadback(
        package=subject.normalized_package,
        version=projection.coordinate.native_version,
        classification=classification,
        content_sha256=artifact.content.content_sha256 if exact else None,
        content_sha512=artifact.content.content_sha512 if exact else None,
        witness_digest=artifact.witness_digest if exact else None,
        witness_target=case.snapshot.target if exact else None,
        tag=f"buddy-sha-{case.snapshot.target}",
        tag_state=(
            "present" if tag_state in {"desired", "other"} else tag_state
        ),
        tag_version=(
            projection.coordinate.native_version
            if tag_state == "desired"
            else "0.0.1"
            if tag_state == "other"
            else None
        ),
        observed_at=OBSERVED_AT,
        response_digests=(("version", DIGEST),),
    )
    assert artifact.content.content_sha512 is not None
    return _parsed(
        RemoteStateObservation(
            attempt=case.attempt_binding.attempt,
            qualification_decision_reference=case.decision_reference,
            desired_subject=subject,
            desired_version=projection.coordinate.native_version,
            desired_content_sha256=artifact.content.content_sha256,
            desired_content_sha512=artifact.content.content_sha512,
            desired_witness_digest=artifact.witness_digest,
            classification=classification,
            package_control=control,
            active_readback=readback,
            response_identity=DIGEST,
            diagnostics=PublicationDiagnostics(entries=(), truncated=False),
            producer="observe-github-packages",
            control=case.eligibility.context.control,
            workflow_run_id=case.intent.workflow_run_id,
        )
    )


@pytest.mark.parametrize(
    "tag_state", ["absent", "desired", "other", "unreadable"]
)
def test_exact_basis_and_observation_ignore_tag_routing(
    observation_case: ObservationCase, tag_state: str
) -> None:
    case = observation_case
    assert (
        validate_remote_state_observation_basis(**case.arguments(), now=NOW)
        == case.snapshot.destination_projections[0]
    )
    observation = _observation(case, tag_state=tag_state)
    assert (
        classify_package_control(
            observation.package_control,
            subject=observation.desired_subject,
            eligibility=case.eligibility,
        )
        == "ready"
    )
    assert (
        admit_remote_state_observation(observation, **case.arguments())
        is observation
    )


@pytest.mark.parametrize(
    "evidence_field", ["active_readback", "package_control"]
)
def test_absence_admits_only_current_action_creation(
    observation_case: ObservationCase,
    evidence_field: str,
) -> None:
    case = observation_case
    observation = _observation(case, classification="absent")
    assert (
        admit_remote_state_observation(
            observation, **case.arguments(), action_creation_at=NOW
        )
        is observation
    )
    expiry = case.eligibility.governance.attestation.expires_at
    with pytest.raises(ValueError, match="currently fresh"):
        admit_remote_state_observation(
            observation, **case.arguments(), action_creation_at=expiry
        )
    evidence = {
        "active_readback": observation.active_readback,
        "package_control": observation.package_control,
    }[evidence_field]
    assert evidence is not None
    future_evidence = _parsed(
        replace(
            observation,
            **{
                evidence_field: replace(
                    evidence,
                    observed_at=(NOW + timedelta(seconds=1)).strftime(
                        "%Y-%m-%dT%H:%M:%SZ"
                    ),
                )
            },
        )
    )
    with pytest.raises(ValueError, match="cannot precede"):
        admit_remote_state_observation(
            future_evidence, **case.arguments(), action_creation_at=NOW
        )


@pytest.mark.parametrize(
    ("fact", "value"),
    [
        ("owner", ("outsider",)),
        ("repository-association", ("hcoona/other",)),
        ("visibility", ("private",)),
        ("exposed-access", ("admin",)),
    ],
)
def test_control_conflicts_block_ready_but_preserve_observed_facts(
    observation_case: ObservationCase, fact: str, value: tuple[str, ...]
) -> None:
    case = observation_case
    observation = _observation(case)
    assert observation.package_control is not None
    proof = replace(
        observation.package_control,
        facts=tuple(
            (name, value if name == fact else values)
            for name, values in observation.package_control.facts
        ),
    )
    assert (
        classify_package_control(
            proof,
            subject=observation.desired_subject,
            eligibility=case.eligibility,
        )
        == "conflicting"
    )
    with pytest.raises(ValueError, match="package control"):
        admit_remote_state_observation(
            _parsed(replace(observation, package_control=proof)),
            **case.arguments(),
        )
    blocking = _parsed(
        replace(observation, package_control=proof, classification="unknown")
    )
    admitted = admit_remote_state_observation(blocking, **case.arguments())
    assert admitted is blocking
    assert admitted.package_control == proof


def test_missing_and_unsupported_control_are_unprovable(
    observation_case: ObservationCase,
) -> None:
    case = observation_case
    observation = _observation(case)
    assert observation.package_control is not None
    unsupported = replace(
        observation.package_control,
        endpoints=("https://api.github.com/unsupported",),
        response_digests=(("https://api.github.com/unsupported", DIGEST),),
    )
    for proof in (None, unsupported):
        assert (
            classify_package_control(
                proof,
                subject=observation.desired_subject,
                eligibility=case.eligibility,
            )
            == "unprovable"
        )
        blocking = _parsed(
            replace(
                observation,
                classification="unprovable",
                package_control=proof,
                active_readback=None,
            )
        )
        assert (
            admit_remote_state_observation(blocking, **case.arguments())
            is blocking
        )
    with pytest.raises(ValueError, match="package control"):
        admit_remote_state_observation(
            replace(observation, package_control=unsupported),
            **case.arguments(),
        )


def test_decision_reference_resolves_payload_and_exact_transport_tuple(
    observation_case: ObservationCase,
) -> None:
    case = observation_case
    with pytest.raises(ValueError, match="reference payload digest"):
        validate_remote_state_observation_basis(
            **(
                case.arguments()
                | {
                    "decision_reference": replace(
                        case.decision_reference, payload_digest=OTHER_DIGEST
                    )
                }
            ),
            now=NOW,
        )
    observation = _observation(case)
    foreign_reference = replace(case.decision_reference, artifact_id=803)
    assert foreign_reference.payload_digest == case.decision.decision_digest
    with pytest.raises(ValueError, match="qualified desired state"):
        admit_remote_state_observation(
            _parsed(
                replace(
                    observation,
                    qualification_decision_reference=foreign_reference,
                )
            ),
            **case.arguments(),
        )


def test_unsuccessful_or_foreign_decision_cannot_supply_read_basis(
    observation_case: ObservationCase,
) -> None:
    case = observation_case
    unsuccessful = _parsed(
        finalize_qualification(case.snapshot, (), (case.artifact,))
    )
    foreign = _parsed(
        replace(case.decision, qualification_snapshot_digest=OTHER_DIGEST)
    )
    for decision, message in (
        (unsuccessful, "successful Qualification"),
        (foreign, "does not match the Snapshot"),
    ):
        with pytest.raises(ValueError, match=message):
            validate_remote_state_observation_basis(
                **(
                    case.arguments()
                    | {
                        "decision": decision,
                        "decision_reference": replace(
                            case.decision_reference,
                            payload_digest=decision.decision_digest,
                        ),
                    }
                ),
                now=NOW,
            )


@pytest.mark.parametrize("field", ["model", "policy", "repository"])
def test_snapshot_must_match_admitted_eligibility(
    observation_case: ObservationCase, field: str
) -> None:
    case = observation_case
    if field == "model":
        snapshot = replace(
            case.snapshot,
            repository_model_digest=OTHER_DIGEST,
            build_requests=tuple(
                replace(request, repository_model_digest=OTHER_DIGEST)
                for request in case.snapshot.build_requests
            ),
        )
    elif field == "policy":
        snapshot = replace(case.snapshot, release_policy_digest=OTHER_DIGEST)
    else:
        snapshot = replace(case.snapshot, repository="hcoona/other")
    with pytest.raises(ValueError, match="Snapshot differs"):
        validate_remote_state_observation_basis(
            **(case.arguments() | {"snapshot": _parsed(snapshot)}), now=NOW
        )


@pytest.mark.parametrize(
    "field",
    [
        "intent_digest",
        "live_eligibility_payload_digest",
        "repository_model_digest",
        "attestation_provenance",
        "attempt",
    ],
)
def test_attempt_binding_must_close_current_eligibility(
    observation_case: ObservationCase, field: str
) -> None:
    case = observation_case
    binding = case.attempt_binding
    value = (
        tuple(
            (key, "c" * 40 if key == "blob-oid" else fact)
            for key, fact in binding.attestation_provenance
        )
        if field == "attestation_provenance"
        else replace(binding.attempt, workflow_run_id=9000)
        if field == "attempt"
        else OTHER_DIGEST
    )
    substituted = _parsed(replace(binding, **{field: value}))
    with pytest.raises(ValueError, match="Attempt binding differs"):
        validate_remote_state_observation_basis(
            **(case.arguments() | {"attempt_binding": substituted}), now=NOW
        )


def test_alternate_canonically_admitted_eligibility_is_not_current_authority(
    observation_case: ObservationCase,
) -> None:
    case = observation_case
    document = case.eligibility.to_document()
    governance = document["governance"]
    assert isinstance(governance, dict)
    governance["blob-oid"] = "c" * 40
    alternate = admit_live_eligibility_decision(
        canonicalize(document),
        intent=case.intent,
        repository_model=case.model,
        policy=case.policy,
        expected_digest=canonical_sha256(document),
        admission_mode=LiveEligibilityAdmissionMode.CURRENT_FRESHNESS,
        now=NOW,
    )
    with pytest.raises(ValueError, match="Attempt binding differs"):
        validate_remote_state_observation_basis(
            **(case.arguments() | {"eligibility": alternate}), now=NOW
        )


@pytest.mark.parametrize("field", ["witness_digest", "build_request_digest"])
def test_admitted_artifact_must_still_match_planned_witness_and_build(
    observation_case: ObservationCase, field: str
) -> None:
    case = observation_case
    provenance = case.artifact.provenance_document()
    provenance[field.replace("_", "-")] = OTHER_DIGEST
    artifact = _parsed(
        replace(
            case.artifact,
            **{
                field: OTHER_DIGEST,
                "provenance_digest": canonical_sha256(provenance),
            },
        )
    )
    decision = _parsed(
        replace(
            case.decision, admitted_artifact_digests=(artifact.artifact_digest,)
        )
    )
    # Rebind Decision and transport deliberately so the planned build/witness
    # check, rather than an earlier unrelated digest mismatch, is exercised.
    with pytest.raises(ValueError, match="planned build or witness"):
        validate_remote_state_observation_basis(
            **(
                case.arguments()
                | {
                    "artifact": artifact,
                    "decision": decision,
                    "decision_reference": replace(
                        case.decision_reference,
                        payload_digest=decision.decision_digest,
                    ),
                }
            ),
            now=NOW,
        )


def test_decision_must_admit_the_supplied_artifact(
    observation_case: ObservationCase,
) -> None:
    case = observation_case
    decision = _parsed(
        replace(case.decision, admitted_artifact_digests=(OTHER_DIGEST,))
    )
    with pytest.raises(ValueError, match="artifact binding mismatch"):
        validate_remote_state_observation_basis(
            **(
                case.arguments()
                | {
                    "decision": decision,
                    "decision_reference": replace(
                        case.decision_reference,
                        payload_digest=decision.decision_digest,
                    ),
                }
            ),
            now=NOW,
        )


@pytest.mark.parametrize(
    "field",
    [
        "desired_subject",
        "desired_version",
        "desired_content_sha256",
        "desired_content_sha512",
        "desired_witness_digest",
        "attempt",
    ],
)
def test_completed_observation_cannot_substitute_desired_basis(
    observation_case: ObservationCase, field: str
) -> None:
    case = observation_case
    observation = replace(
        _observation(case),
        classification="unknown",
        package_control=None,
        active_readback=None,
    )
    values = {
        "desired_subject": replace(
            observation.desired_subject, normalized_package="@hcoona/other"
        ),
        "desired_version": "0.0.1",
        "desired_content_sha256": OTHER_DIGEST,
        "desired_content_sha512": "sha512:" + "b" * 128,
        "desired_witness_digest": OTHER_DIGEST,
        "attempt": replace(observation.attempt, workflow_run_id=9000),
    }
    changes = {field: values[field]}
    if field == "attempt":
        changes["workflow_run_id"] = 9000
    with pytest.raises(ValueError, match="qualified desired state"):
        admit_remote_state_observation(
            _parsed(replace(observation, **changes)), **case.arguments()
        )


@pytest.mark.parametrize("classification", ["absent", "exact-satisfied"])
def test_replay_allows_historical_evidence_but_not_fresh_reads_or_actions(
    observation_case: ObservationCase, classification: str
) -> None:
    case = observation_case
    later = case.eligibility.governance.attestation.expires_at + timedelta(
        days=1
    )
    replay = admit_live_eligibility_decision(
        case.eligibility.canonical_bytes,
        intent=case.intent,
        repository_model=case.model,
        policy=case.policy,
        expected_digest=case.eligibility.canonical_digest,
        admission_mode=LiveEligibilityAdmissionMode.AUTHORIZATION_REPLAY,
        now=later,
    )
    arguments = case.arguments() | {"eligibility": replay}
    observation = _observation(case, classification=classification)
    assert (
        admit_remote_state_observation(observation, **arguments) is observation
    )
    with pytest.raises(ValueError, match="currently fresh"):
        validate_remote_state_observation_basis(**arguments, now=later)
    if classification == "absent":
        with pytest.raises(ValueError, match="currently fresh"):
            admit_remote_state_observation(
                observation, **arguments, action_creation_at=later
            )
        assert observation.active_readback is not None
        assert observation.package_control is not None
        before_governance = (
            replay.governance.observed_at - timedelta(seconds=1)
        ).strftime("%Y-%m-%dT%H:%M:%SZ")
        premature = _parsed(
            replace(
                observation,
                active_readback=replace(
                    observation.active_readback,
                    observed_at=before_governance,
                ),
                package_control=replace(
                    observation.package_control,
                    observed_at=before_governance,
                ),
            )
        )
        with pytest.raises(ValueError, match="currently fresh"):
            admit_remote_state_observation(premature, **arguments)


def test_absence_cannot_be_recorded_after_eligibility_expiry(
    observation_case: ObservationCase,
) -> None:
    case = observation_case
    observation = _observation(case, classification="absent")
    assert observation.active_readback is not None
    expired_at = case.eligibility.governance.attestation.expires_at.strftime(
        "%Y-%m-%dT%H:%M:%SZ"
    )
    stale = _parsed(
        replace(
            observation,
            active_readback=replace(
                observation.active_readback,
                observed_at=expired_at,
            ),
        )
    )
    with pytest.raises(ValueError, match="currently fresh"):
        admit_remote_state_observation(stale, **case.arguments())


@pytest.mark.parametrize(
    "observation_case", [NOW - timedelta(days=90)], indirect=True
)
def test_expired_native_acceptance_blocks_absence_not_exact_state(
    observation_case: ObservationCase,
) -> None:
    case = observation_case
    assert (
        validate_remote_state_observation_basis(**case.arguments(), now=NOW)
        == case.snapshot.destination_projections[0]
    )
    exact = _observation(case)
    assert admit_remote_state_observation(exact, **case.arguments()) is exact
    absent = _observation(case, classification="absent")
    with pytest.raises(ValueError, match="unexpired native acceptance"):
        admit_remote_state_observation(absent, **case.arguments())
    with pytest.raises(ValueError, match="unexpired native acceptance"):
        admit_remote_state_observation(
            absent,
            **case.arguments(),
            action_creation_at=NOW,
        )


@pytest.mark.parametrize(
    "observation_case", [NOW - timedelta(days=89)], indirect=True
)
def test_acceptance_must_remain_fresh_at_action_creation(
    observation_case: ObservationCase,
) -> None:
    case = observation_case
    observation = _observation(case, classification="absent")
    assert (
        admit_remote_state_observation(
            observation, **case.arguments(), action_creation_at=NOW
        )
        is observation
    )
    with pytest.raises(ValueError, match="unexpired native acceptance"):
        admit_remote_state_observation(
            observation,
            **case.arguments(),
            action_creation_at=NOW + timedelta(days=1),
        )


def test_ready_observation_cannot_substitute_package_target(
    observation_case: ObservationCase,
) -> None:
    case = observation_case
    observation = _observation(case)
    assert observation.package_control is not None
    assert observation.active_readback is not None
    subject = replace(
        observation.desired_subject, normalized_package="@hcoona/other"
    )
    foreign_proof = replace(observation.package_control, subject=subject)
    assert (
        classify_package_control(
            foreign_proof,
            subject=observation.desired_subject,
            eligibility=case.eligibility,
        )
        == "conflicting"
    )
    substituted = _parsed(
        replace(
            observation,
            desired_subject=subject,
            package_control=foreign_proof,
            active_readback=replace(
                observation.active_readback, package=subject.normalized_package
            ),
        )
    )
    with pytest.raises(ValueError, match="qualified desired state"):
        admit_remote_state_observation(substituted, **case.arguments())


def test_qualified_artifact_cannot_reference_a_different_snapshot(
    observation_case: ObservationCase,
) -> None:
    case = observation_case
    artifact = case.artifact
    transport = replace(
        artifact.transport,
        artifact_name=release_artifact_transport_name(
            repository=artifact.repository,
            purpose=artifact.purpose,
            output=artifact.output,
            qualification_snapshot_digest=OTHER_DIGEST,
            workflow_run_id=case.intent.workflow_run_id,
            run_attempt=None,
            producer=artifact.transport.producer,
        ),
    )
    provenance = artifact.provenance_document() | {
        "qualification-snapshot-digest": OTHER_DIGEST,
        "transport": transport.to_document(),
    }
    substituted = _parsed(
        replace(
            artifact,
            qualification_snapshot_digest=OTHER_DIGEST,
            transport=transport,
            provenance_digest=canonical_sha256(provenance),
        )
    )
    with pytest.raises(ValueError, match="current Snapshot"):
        validate_remote_state_observation_basis(
            **(case.arguments() | {"artifact": substituted}), now=NOW
        )


def test_current_intent_and_normalized_policy_are_not_lookup_hints(
    observation_case: ObservationCase,
) -> None:
    case = observation_case
    other_intent = normalize_buddy_live_intent(
        repository=case.intent.repository,
        selected_ref="refs/heads/other",
        target=case.intent.target,
        actor=case.intent.actor,
        workflow_run_id=case.intent.workflow_run_id,
    )
    for substitutions in (
        {"intent": _parsed(other_intent)},
        {"policy": replace(case.policy, release_unit="other")},
    ):
        with pytest.raises(ValueError, match="current Intent"):
            validate_remote_state_observation_basis(
                **(case.arguments() | substitutions), now=NOW
            )
