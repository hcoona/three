"""Current-DAG raw artifact transport for Live observation scenarios."""

from __future__ import annotations

import hashlib
from datetime import datetime, timedelta
from typing import TYPE_CHECKING

from three_workflow_delivery_v3 import cli
from three_workflow_delivery_v3.canonical import canonical_sha256, canonicalize
from three_workflow_delivery_v3.records.artifacts import ArtifactReference
from three_workflow_delivery_v3.records.release import (
    PublicationSnapshot,
    admit_release_record,
)
from three_workflow_delivery_v3.release.exact_satisfied import (
    prove_exact_satisfied,
)
from three_workflow_delivery_v3.release.finalizer import (
    materialize_publication_snapshot,
)
from three_workflow_delivery_v3.release.live import form_approval_bundle

from .test_eligibility import RecordingGovernanceClient
from .test_observation_admission import NOW, ObservationCase, _observation

if TYPE_CHECKING:
    from pathlib import Path

    import pytest


def uploaded_arguments(
    root: Path, name: str, document, artifact_id: int, *, reference=None
) -> list[str]:
    """Persist one canonical raw upload and retain its exact transport tuple."""
    root.mkdir(parents=True, exist_ok=True)
    basename = name.replace("_", "-") + ".json"
    if reference is not None:
        basename = reference.payload_path
    path = root / basename
    path.write_bytes(canonicalize(document))
    digest = canonical_sha256(document)
    option = name.replace("_", "-")
    arguments = [
        f"--{option}",
        str(path),
        f"--{option}-digest",
        digest,
        f"--{option}-artifact-id",
        str(artifact_id),
        f"--{option}-artifact-digest",
        digest,
    ]
    if reference is not None:
        assert reference.artifact_id == artifact_id
        assert reference.payload_digest == digest
        assert reference.artifact_digest == digest
        arguments += [
            f"--{option}-artifact-url",
            reference.artifact_url,
            f"--{option}-payload-path",
            reference.payload_path,
        ]
    return arguments


def authority_arguments(
    root: Path, case: ObservationCase, monkeypatch: pytest.MonkeyPatch
) -> list[str]:
    """Load canonical Intent/Model/Eligibility/Attempt through CLI loaders."""

    class Clock(datetime):
        @classmethod
        def now(cls, tz=None):
            return NOW if tz is None else NOW.astimezone(tz)

    monkeypatch.setattr(cli, "datetime", Clock)
    monkeypatch.setattr(
        cli,
        "load_first_slice_authoring",
        lambda _root, _target: (None, None, case.policy),
    )
    arguments = []
    for name, record, artifact_id in (
        ("intent", case.intent, 101),
        ("repository_model", case.model.snapshot, 102),
        ("attempt_binding", case.attempt_binding, 103),
    ):
        arguments += uploaded_arguments(
            root, name, record.to_document(), artifact_id
        )
    eligibility_path = root / "live-eligibility.json"
    eligibility_path.write_bytes(canonicalize(case.eligibility.to_document()))
    return [
        *arguments,
        "--live-eligibility-decision",
        str(eligibility_path),
        "--live-eligibility-artifact-id",
        str(case.attempt_binding.live_eligibility_artifact_id),
        "--live-eligibility-artifact-digest",
        case.attempt_binding.live_eligibility_artifact_digest,
        "--live-eligibility-payload-digest",
        case.eligibility.canonical_digest,
    ]


def qualification_arguments(root: Path, case: ObservationCase) -> list[str]:
    """Persist the exact successful Qualification closure for a consumer."""
    return [
        *uploaded_arguments(
            root, "qualification_snapshot", case.snapshot.to_document(), 104
        ),
        *uploaded_arguments(
            root,
            "qualification_decision",
            case.decision.to_document(),
            case.decision_reference.artifact_id,
            reference=case.decision_reference,
        ),
        *uploaded_arguments(
            root, "release_artifact", case.artifact.to_document(), 106
        ),
    ]


def current_arguments(case: ObservationCase) -> list[str]:
    """Supply the current platform guard without adding run-attempt identity."""
    return [
        "--workflow-run-id",
        str(case.intent.workflow_run_id),
        "--run-attempt",
        "1",
        "--target",
        case.intent.target,
    ]


def materialization_arguments(case: ObservationCase) -> dict:
    """Supply required contextual authority to the pure materializer."""
    return {
        key: value
        for key, value in case.arguments().items()
        if key not in {"snapshot", "decision", "artifact"}
    } | {"action_creation_at": NOW}


def publication_authority_arguments(
    root: Path,
    case: ObservationCase,
    monkeypatch: pytest.MonkeyPatch,
    *,
    classification: str,
) -> list[str]:
    """Materialize real payloads for Approval or exact-satisfied admission."""
    authority = authority_arguments(root, case, monkeypatch)
    qualification = qualification_arguments(root, case)
    observation = uploaded_arguments(
        root,
        "observation",
        _observation(case, classification=classification).to_document(),
        108,
    )
    publication_path = root / "publication-snapshot.json"
    reviewer_path = root / "reviewer-summary.md"
    assert (
        cli.main(
            [
                "release",
                "materialize-publication",
                *current_arguments(case),
                *authority,
                *qualification,
                *observation,
                "--selected-ref",
                case.intent.selected_ref,
                "--output",
                str(publication_path),
                "--summary-output",
                str(reviewer_path),
            ]
        )
        == 0
    )
    publication_digest = (
        "sha256:" + hashlib.sha256(publication_path.read_bytes()).hexdigest()
    )
    publication = admit_release_record(
        publication_path.read_bytes(),
        expected_type=PublicationSnapshot,
        expected_digest=publication_digest,
    )
    publication_reference = ArtifactReference(
        artifact_id=109,
        artifact_digest=publication_digest,
        artifact_url=f"https://github.com/hcoona/three/actions/runs/{case.intent.workflow_run_id}/artifacts/109",
        payload_path=publication_path.name,
        payload_digest=publication_digest,
    )
    arguments = [
        *authority,
        *qualification,
        *observation,
        *uploaded_arguments(
            root,
            "publication_snapshot",
            publication.to_document(),
            109,
            reference=publication_reference,
        ),
    ]
    if classification == "exact-satisfied":
        return [
            *arguments,
            *uploaded_arguments(
                root, "adapter_context", case.context.to_document(), 112
            ),
            "--publisher-conclusion",
            "skipped",
        ]
    reviewer_digest = (
        "sha256:" + hashlib.sha256(reviewer_path.read_bytes()).hexdigest()
    )
    reviewer_reference = ArtifactReference(
        artifact_id=110,
        artifact_digest=reviewer_digest,
        artifact_url=f"https://github.com/hcoona/three/actions/runs/{case.intent.workflow_run_id}/artifacts/110",
        payload_path=reviewer_path.name,
        payload_digest=reviewer_digest,
    )
    bundle = form_approval_bundle(
        intent=case.intent,
        attempt_binding=case.attempt_binding,
        qualification_decision=case.decision,
        publication_snapshot=publication,
        publication_snapshot_reference=publication_reference,
        reviewer_summary_reference=reviewer_reference,
        control=case.eligibility.context.control,
    )
    bundle_reference = ArtifactReference(
        artifact_id=111,
        artifact_digest=bundle.bundle_digest,
        artifact_url=f"https://github.com/hcoona/three/actions/runs/{case.intent.workflow_run_id}/artifacts/111",
        payload_path="approval-bundle.json",
        payload_digest=bundle.bundle_digest,
    )
    return [
        *arguments,
        *uploaded_arguments(
            root,
            "approval_bundle",
            bundle.to_document(),
            111,
            reference=bundle_reference,
        ),
        "--reviewer-summary",
        str(reviewer_path),
        "--reviewer-summary-digest",
        reviewer_digest,
        "--reviewer-summary-artifact-id",
        str(reviewer_reference.artifact_id),
        "--reviewer-summary-artifact-digest",
        reviewer_reference.artifact_digest,
        "--reviewer-summary-artifact-url",
        reviewer_reference.artifact_url,
        "--reviewer-summary-payload-path",
        reviewer_reference.payload_path,
    ]


def active_transport(case: ObservationCase):
    """Serve actual qualified bytes through the read-only adapter contract."""
    from ..adapters.test_github_packages_active_state import (  # noqa: PLC0415
        CONTROL_URL,
        TAGS_URL,
        TARBALL_URL,
        ScenarioTransport,
        _control,
        _response,
    )

    exact_url = TAGS_URL + "/" + case.expectation.npm_package_version
    return ScenarioTransport(
        {
            CONTROL_URL: _response(CONTROL_URL, _control()),
            exact_url: _response(
                exact_url,
                {
                    "name": case.expectation.package_name,
                    "version": case.expectation.npm_package_version,
                    "dist": {"tarball": TARBALL_URL},
                },
            ),
            TARBALL_URL: _response(TARBALL_URL, body=case.tarball),
            TAGS_URL: _response(
                TAGS_URL,
                {
                    "name": case.expectation.package_name,
                    "dist-tags": {},
                },
            ),
        }
    )


def exact_finalization_arguments(case: ObservationCase) -> dict:
    """Create a real-byte proof and its full read-only Finalizer closure."""
    observation = _observation(case)
    publication = materialize_publication_snapshot(
        case.snapshot,
        case.decision,
        (observation,),
        (case.artifact,),
        **materialization_arguments(case),
    )
    reference = ArtifactReference(
        artifact_id=109,
        artifact_digest=publication.snapshot_digest,
        artifact_url=f"https://github.com/hcoona/three/actions/runs/{case.intent.workflow_run_id}/artifacts/109",
        payload_path="publication-snapshot.json",
        payload_digest=publication.snapshot_digest,
    )
    proof = prove_exact_satisfied(
        **case.arguments(),
        publication_snapshot=publication,
        publication_snapshot_reference=reference,
        observation=observation,
        publisher_conclusion="skipped",
        expectation=case.expectation,
        governance_client=RecordingGovernanceClient(
            canonicalize(case.eligibility.governance.attestation.to_document())
        ),
        transport=active_transport(case),
        token="test-only-token",  # noqa: S106
        clock=lambda: NOW + timedelta(seconds=1),
    )
    return {
        "attempt": case.attempt_binding.attempt,
        "qualification_decision": case.decision,
        "publication_snapshot": publication,
        "publication_snapshot_reference": reference,
        "exact_satisfied_finalization_proof": proof,
        "approval_bundle": None,
        "publication_authorization": None,
        "action_results": (),
        "publisher_conclusion": "skipped",
        "qualification_snapshot": case.snapshot,
        "release_artifact": case.artifact,
        "observations": (observation,),
        "intent": case.intent,
        "attempt_binding": case.attempt_binding,
        "eligibility": case.eligibility,
        "policy": case.policy,
        "decision_reference": case.decision_reference,
    }
