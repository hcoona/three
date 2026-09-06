"""Read-only exact finalization with actual downloaded qualified bytes."""

# ruff: noqa: D103

from __future__ import annotations

import io
import json
import tarfile
from dataclasses import replace
from datetime import datetime, timedelta

import pytest
from three_workflow_delivery_v3 import cli
from three_workflow_delivery_v3.canonical import canonical_sha256, canonicalize
from three_workflow_delivery_v3.records.release import (
    AttemptOutcome,
    ExactSatisfiedFinalizationProof,
    admit_release_record,
)
from three_workflow_delivery_v3.release import exact_satisfied

from ..adapters.test_github_packages_active_state import (
    CONTROL_URL,
    TAGS_URL,
    TARBALL_URL,
    _control,
    _response,
)
from ..adapters.test_npmjs import _make_tarball
from .observation_fixtures import (
    active_transport,
    current_arguments,
    exact_finalization_arguments,
    publication_authority_arguments,
)
from .test_attempt_finalizer import (
    exact_inputs,
    finalization_cli_arguments,
    finalize,
    pair,
)
from .test_eligibility import RecordingGovernanceClient
from .test_observation_admission import NOW
from .test_observation_admission import (
    observation_case as observation_case,  # noqa: PLC0414
)

SNAPSHOT_ARTIFACT_ID = 109


def _proving_arguments(case):
    finalized = exact_finalization_arguments(case)
    return {
        **case.arguments(),
        "publication_snapshot": finalized["publication_snapshot"],
        "publication_snapshot_reference": finalized[
            "publication_snapshot_reference"
        ],
        "observation": finalized["observations"][0],
        "publisher_conclusion": "skipped",
        "expectation": case.expectation,
        "governance_client": RecordingGovernanceClient(
            canonicalize(case.eligibility.governance.attestation.to_document())
        ),
        "transport": active_transport(case),
        "token": "test-only-token",
        "clock": lambda: NOW + timedelta(seconds=2),
    }


@pytest.mark.parametrize("tag_state", ["absent", "elsewhere", "unreadable"])
def test_fresh_exact_proof_requires_download_but_ignores_tag(
    observation_case,
    tag_state,
):
    case = observation_case
    arguments = _proving_arguments(case)
    transport = arguments["transport"]
    if tag_state == "elsewhere":
        transport.responses[TAGS_URL] = _response(
            TAGS_URL,
            {
                "name": case.expectation.package_name,
                "dist-tags": {f"buddy-sha-{case.intent.target}": "9.9.9"},
            },
        )
    elif tag_state == "unreadable":
        transport.responses[TAGS_URL] = _response(TAGS_URL, status=503)
    proof = exact_satisfied.prove_exact_satisfied(**arguments)
    assert isinstance(proof, ExactSatisfiedFinalizationProof)
    assert (
        proof.exact_version_readback.content_sha256
        == case.artifact.content.content_sha256
    )
    assert (
        proof.exact_version_readback.content_sha512
        == case.artifact.content.content_sha512
    )
    assert (
        proof.exact_version_readback.witness_digest
        == case.artifact.witness_digest
    )
    assert proof.exact_version_readback.witness_target == case.intent.target
    assert [request[0] for request in transport.requests] == [
        CONTROL_URL,
        TAGS_URL + "/" + case.expectation.npm_package_version,
        TARBALL_URL,
        TAGS_URL,
    ]
    outcome = finalize(
        replace(
            exact_inputs(case), exact_proof=pair(proof, "exact-proof.json", 115)
        )
    )
    assert outcome.disposition == "exact-satisfied"


@pytest.mark.parametrize(
    "change", ["bytes", "witness", "package-control", "download-unreadable"]
)
def test_fresh_nonexact_remote_state_cannot_form_success_proof(
    observation_case,
    change,
):
    case = observation_case
    arguments = _proving_arguments(case)
    transport = arguments["transport"]
    if change in {"bytes", "witness"}:
        with tarfile.open(
            fileobj=io.BytesIO(case.tarball), mode="r:gz"
        ) as archive:
            files = {
                member.name: archive.extractfile(member).read()
                for member in archive.getmembers()
                if member.isfile()
            }
        if change == "bytes":
            files["package/README.md"] = b"Changed after Observation\n"
        else:
            witness_path = "package/workflow-delivery/provenance.json"
            witness = json.loads(files[witness_path])
            witness["target"] = "f" * 40
            files[witness_path] = canonicalize(witness)
        transport.responses[TARBALL_URL] = _response(
            TARBALL_URL, body=_make_tarball(files)
        )
    elif change == "package-control":
        transport.responses[CONTROL_URL] = _response(
            CONTROL_URL, _control(visibility="private")
        )
    else:
        transport.responses[TARBALL_URL] = _response(TARBALL_URL, status=503)
        exact_url = TAGS_URL + "/" + case.expectation.npm_package_version
        # Even an authoritative-looking registry integrity field is not bytes.
        document = json.loads(transport.responses[exact_url].body)
        document["dist"]["shasum"] = case.artifact.content.content_sha256
        document["dist"]["integrity"] = case.artifact.content.content_sha512
        transport.responses[exact_url] = _response(exact_url, document)
    with pytest.raises(ValueError, match="Fresh exact-satisfied state"):
        exact_satisfied.prove_exact_satisfied(**arguments)
    assert TARBALL_URL in [request[0] for request in transport.requests]


@pytest.mark.parametrize(
    "substitution",
    [
        "snapshot-upload",
        "qualified-digest",
        "package-control",
        "stale-evidence",
        "remote-before-fresh-governance",
    ],
)
def test_readonly_finalizer_rejects_misbound_or_stale_proof(
    observation_case,
    monkeypatch,
    substitution,
):
    inputs = exact_inputs(observation_case)
    proof = inputs.exact_proof[0]
    if substitution == "snapshot-upload":
        proof = replace(
            proof,
            publication_snapshot_reference=replace(
                proof.publication_snapshot_reference, artifact_id=9109
            ),
        )
    elif substitution == "qualified-digest":
        proof = replace(
            proof,
            exact_version_readback=replace(
                proof.exact_version_readback,
                content_sha512="sha512:" + "f" * 128,
            ),
        )
    elif substitution == "package-control":
        proof = replace(
            proof,
            package_control_proof=replace(
                proof.package_control_proof,
                facts=tuple(
                    (name, ("private",) if name == "visibility" else values)
                    for name, values in proof.package_control_proof.facts
                ),
            ),
        )
    elif substitution == "stale-evidence":
        proof = replace(
            proof,
            exact_version_readback=replace(
                proof.exact_version_readback,
                observed_at=(NOW - timedelta(seconds=1))
                .isoformat()
                .replace("+00:00", "Z"),
            ),
        )
    else:
        proof = replace(
            proof,
            exact_version_readback=replace(
                proof.exact_version_readback,
                observed_at=NOW.isoformat().replace("+00:00", "Z"),
            ),
        )
    monkeypatch.setattr(
        exact_satisfied,
        "require_fresh_governance_identity",
        lambda *_args, **_kwargs: pytest.fail("Finalizer performed remote IO"),
    )
    monkeypatch.setattr(
        cli,
        "GitHubPackagesHttpTransport",
        lambda: pytest.fail("Finalizer constructed remote transport"),
    )
    with pytest.raises(ValueError, match=r"(mismatch|stale|predates)"):
        finalize(
            replace(inputs, exact_proof=pair(proof, "exact-proof.json", 115))
        )


@pytest.mark.parametrize(
    "invalid", ["decision-reference", "publisher", "expectation"]
)
def test_prover_rejects_invalid_authority_before_remote_reads(
    observation_case,
    invalid,
):
    arguments = _proving_arguments(observation_case)
    if invalid == "decision-reference":
        arguments["decision_reference"] = replace(
            arguments["decision_reference"], artifact_id=9999
        )
    elif invalid == "publisher":
        arguments["publisher_conclusion"] = "success"
    else:
        arguments["expectation"] = replace(
            arguments["expectation"], npm_package_version="9.9.9"
        )
    with pytest.raises(ValueError, match=r"(desired state|publisher skipped)"):
        exact_satisfied.prove_exact_satisfied(**arguments)
    assert arguments["governance_client"].calls == []
    assert arguments["transport"].requests == []


def test_fresh_governance_revocation_blocks_destination_reads(observation_case):
    arguments = _proving_arguments(observation_case)
    revoked = replace(
        observation_case.eligibility.governance.attestation,
        live_enabled=False,
    )
    arguments["governance_client"] = RecordingGovernanceClient(
        canonicalize(revoked.to_document())
    )
    with pytest.raises(
        ValueError, match="Governance freshness comparison failed"
    ):
        exact_satisfied.prove_exact_satisfied(**arguments)
    assert arguments["governance_client"].calls
    assert arguments["transport"].requests == []


def test_governance_expiry_during_download_prevents_proof(observation_case):
    arguments = _proving_arguments(observation_case)
    expiry = observation_case.eligibility.governance.attestation.expires_at
    instants = iter(
        [expiry - timedelta(seconds=2), expiry - timedelta(seconds=1), expiry]
    )
    arguments["clock"] = lambda: next(instants)
    with pytest.raises(ValueError, match="must precede Governance expiry"):
        exact_satisfied.prove_exact_satisfied(**arguments)
    assert TARBALL_URL in [
        request[0] for request in arguments["transport"].requests
    ]


def test_governance_only_schema_has_no_alias(observation_case):
    proof = exact_finalization_arguments(observation_case)[
        "exact_satisfied_finalization_proof"
    ]
    document = proof.to_document()
    document["schema"] = "workflow-delivery/v3/exact-satisfied-governance-proof"
    with pytest.raises(ValueError, match="schema"):
        admit_release_record(
            canonicalize(document),
            expected_type=ExactSatisfiedFinalizationProof,
            expected_digest=canonical_sha256(document),
        )


def test_cli_fresh_proof_is_current_artifact_and_finalizer_replays_without_io(
    observation_case,
    tmp_path,
    monkeypatch,
):
    case = observation_case
    arguments = publication_authority_arguments(
        tmp_path, case, monkeypatch, classification="exact-satisfied"
    )
    client = RecordingGovernanceClient(
        canonicalize(case.eligibility.governance.attestation.to_document())
    )
    read_source = client.read_source
    monkeypatch.setattr(
        client,
        "read_source",
        lambda *args, **kwargs: replace(
            read_source(*args, **kwargs), main_sha="d" * 40
        ),
    )
    monkeypatch.setattr(cli, "GitHubGovernanceClient", lambda **_kwargs: client)
    transport = active_transport(case)
    monkeypatch.setattr(cli, "GitHubPackagesHttpTransport", lambda: transport)
    output = tmp_path / "fresh-proof.json"
    assert (
        cli.main(
            [
                "release",
                "prove-exact-satisfied",
                *current_arguments(case),
                *arguments,
                "--github-token",
                "test-only-token",
                "--control",
                case.eligibility.context.control,
                "--output",
                str(output),
            ]
        )
        == 0
    )
    document = json.loads(output.read_bytes())
    proof = admit_release_record(
        output.read_bytes(),
        expected_type=ExactSatisfiedFinalizationProof,
        expected_digest=canonical_sha256(document),
    )
    assert proof.governance_proof.current_main_sha == "d" * 40
    assert (
        proof.publication_snapshot_reference.artifact_id == SNAPSHOT_ARTIFACT_ID
    )
    assert proof.package_control_proof.response_digests
    assert (
        proof.exact_version_readback.witness_digest
        == case.artifact.witness_digest
    )
    inputs = replace(
        exact_inputs(case), exact_proof=pair(proof, "exact-proof.json", 130)
    )
    arguments = finalization_cli_arguments(
        tmp_path / "finalizer", case, inputs, monkeypatch
    )

    class ReplayClock(datetime):
        @classmethod
        def now(cls, tz=None):
            replay_at = NOW + timedelta(days=200)
            return replay_at if tz is None else replay_at.astimezone(tz)

    monkeypatch.setattr(cli, "datetime", ReplayClock)
    monkeypatch.setattr(
        cli,
        "GitHubGovernanceClient",
        lambda **_kwargs: pytest.fail("Finalizer read Governance"),
    )
    monkeypatch.setattr(
        cli,
        "GitHubPackagesHttpTransport",
        lambda: pytest.fail("Finalizer read destination"),
    )
    outcome_path = tmp_path / "outcome.json"
    assert (
        cli.main(
            [
                "release",
                "finalize-live",
                *arguments,
                "--publisher-conclusion",
                "skipped",
                "--publication-terminal-reference",
                "null",
                "--outcome-output",
                str(outcome_path),
                "--summary-output",
                str(tmp_path / "summary.md"),
            ]
        )
        == 0
    )
    outcome = admit_release_record(
        outcome_path.read_bytes(),
        expected_type=AttemptOutcome,
        expected_digest=canonical_sha256(json.loads(outcome_path.read_bytes())),
    )
    assert outcome.disposition == "exact-satisfied"
    assert outcome.possibly_mutated is False
    assert (
        outcome.direct_predecessor.reference.payload_digest
        == proof.proof_digest
    )
