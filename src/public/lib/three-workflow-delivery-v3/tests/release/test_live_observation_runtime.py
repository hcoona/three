"""Atomic authority-first Observation and current-DAG consumer scenarios."""

# ruff: noqa: D103

from __future__ import annotations

import json
from dataclasses import replace
from datetime import datetime, timedelta

import pytest
from three_workflow_delivery_v3 import cli
from three_workflow_delivery_v3.adapters.github_packages import (
    GitHubPackagesHttpResponse,
    GitHubPackagesNetworkError,
    github_packages_destination_operation_profile,
)
from three_workflow_delivery_v3.canonical import canonical_sha256
from three_workflow_delivery_v3.records.artifacts import ArtifactReference
from three_workflow_delivery_v3.records.release import (
    PublicationSnapshot,
    RemoteStateObservation,
    admit_release_record,
)
from three_workflow_delivery_v3.release.finalizer import (
    materialize_publication_snapshot,
)
from three_workflow_delivery_v3.release.observation import observe_remote_state

from .observation_fixtures import (
    authority_arguments,
    current_arguments,
    materialization_arguments,
    qualification_arguments,
    uploaded_arguments,
)
from .test_observation_admission import (
    ENDPOINT,
    NOW,
)
from .test_observation_admission import (
    observation_case as observation_case,  # noqa: PLC0414
)

TOKEN = "observation-test-only-token"  # noqa: S105
TAGS_URL = "https://npm.pkg.github.com/@hcoona%2Fhcoona-release-smoke-npm"
TARBALL_URL = (
    "https://npm.pkg.github.com/@hcoona/hcoona-release-smoke-npm/-/package.tgz"
)


class Transport:
    """Serve only declared active GETs, rejecting any other operation."""

    def __init__(self, responses):
        """Retain the exact allowed URLs and their read-only responses."""
        self.responses = responses
        self.requests = []

    def get(self, url, *, headers, timeout, max_bytes):
        """Record a bounded authenticated request without network access."""
        self.requests.append(url)
        assert TOKEN in dict(headers)["Authorization"]
        assert timeout > 0
        assert max_bytes > 0
        response = self.responses[url]
        if isinstance(response, Exception):
            raise response
        return response


def response(url, document=None, *, status=200, body=None):
    return GitHubPackagesHttpResponse(
        url=url,
        status=status,
        headers=(),
        body=json.dumps(document).encode() if body is None else body,
    )


def responses(case, *, version_state="exact-satisfied", tag_state="absent"):
    exact_url = TAGS_URL + "/" + case.snapshot.nbgv.npm_package_version
    tags = {}
    if tag_state in {"desired", "other"}:
        tags[f"buddy-sha-{case.intent.target}"] = (
            case.snapshot.nbgv.npm_package_version
            if tag_state == "desired"
            else "9.9.9"
        )
    return {
        ENDPOINT: response(
            ENDPOINT,
            {
                "name": "hcoona-release-smoke-npm",
                "package_type": "npm",
                "visibility": "public",
                "owner": None,
                "repository": {
                    "full_name": "hcoona/three",
                    "permissions": {"admin": True},
                },
            },
        ),
        exact_url: (
            response(exact_url, status=404)
            if version_state == "absent"
            else response(
                exact_url,
                {
                    "name": case.expectation.package_name,
                    "version": case.expectation.npm_package_version,
                    "dist": {"tarball": TARBALL_URL},
                },
            )
        ),
        TARBALL_URL: response(TARBALL_URL, body=case.tarball),
        TAGS_URL: (
            GitHubPackagesNetworkError("unreadable target-tag")
            if tag_state == "unreadable"
            else response(
                TAGS_URL,
                {"name": case.expectation.package_name, "dist-tags": tags},
            )
        ),
    }


def observe(case, transport):
    return observe_remote_state(
        **case.arguments(),
        expectation=case.expectation,
        token=TOKEN,
        transport=transport,
        now=NOW,
    )


@pytest.mark.parametrize(
    "substitution", ["eligibility", "decision", "artifact"]
)
def test_live_observation_rejects_ancestry_before_any_http(
    observation_case, substitution
):
    case = observation_case
    arguments = case.arguments()
    if substitution == "eligibility":
        arguments["eligibility"] = case.eligibility.to_document()
    elif substitution == "decision":
        arguments["decision_reference"] = replace(
            case.decision_reference, payload_digest="sha256:" + "0" * 64
        )
    else:
        provenance = case.artifact.provenance_document()
        provenance["witness-digest"] = "sha256:" + "0" * 64
        arguments["artifact"] = replace(
            case.artifact,
            witness_digest=provenance["witness-digest"],
            provenance_digest=canonical_sha256(provenance),
        )
    transport = Transport({})
    with pytest.raises((TypeError, ValueError)):
        observe_remote_state(
            **arguments,
            expectation=case.expectation,
            token=TOKEN,
            transport=transport,
            now=NOW,
        )
    assert transport.requests == []


@pytest.mark.parametrize(
    "tag_state", ["absent", "desired", "other", "unreadable"]
)
def test_live_cli_observes_exact_bytes_then_materializes_zero_actions(
    observation_case, tmp_path, monkeypatch, tag_state
):
    case = observation_case
    transport = Transport(responses(case, tag_state=tag_state))
    monkeypatch.setattr(cli, "GitHubPackagesHttpTransport", lambda: transport)
    authority = authority_arguments(tmp_path, case, monkeypatch)
    qualification = qualification_arguments(tmp_path, case)
    observation_path = tmp_path / "observation.json"
    assert (
        cli.main(
            [
                "release",
                "observe-github-packages",
                *current_arguments(case),
                *authority,
                *qualification,
                *uploaded_arguments(
                    tmp_path, "adapter_context", case.context.to_document(), 107
                ),
                "--github-token",
                TOKEN,
                "--output",
                str(observation_path),
            ]
        )
        == 0
    )
    observation = admit_release_record(
        observation_path.read_bytes(),
        expected_type=RemoteStateObservation,
        expected_digest=canonical_sha256(
            json.loads(observation_path.read_bytes())
        ),
    )
    assert isinstance(observation, RemoteStateObservation)
    assert observation.classification == "exact-satisfied"
    assert (
        observation.qualification_decision_reference == case.decision_reference
    )
    assert (
        observation.active_readback.content_sha256
        == case.artifact.content.content_sha256
    )
    assert (
        observation.active_readback.witness_digest
        == case.artifact.witness_digest
    )
    assert dict(observation.package_control.facts)["exposed-access"] == ()
    assert transport.requests == [
        ENDPOINT,
        TAGS_URL + "/" + case.expectation.npm_package_version,
        TARBALL_URL,
        TAGS_URL,
    ]
    assert TOKEN not in observation_path.read_text()
    publication_path = tmp_path / "publication.json"
    summary = tmp_path / "reviewer-summary.md"
    assert (
        cli.main(
            [
                "release",
                "materialize-publication",
                *current_arguments(case),
                *authority,
                *qualification,
                *uploaded_arguments(
                    tmp_path, "observation", observation.to_document(), 108
                ),
                "--selected-ref",
                case.intent.selected_ref,
                "--output",
                str(publication_path),
                "--summary-output",
                str(summary),
            ]
        )
        == 0
    )
    publication = admit_release_record(
        publication_path.read_bytes(),
        expected_type=PublicationSnapshot,
        expected_digest=canonical_sha256(
            json.loads(publication_path.read_bytes())
        ),
    )
    assert publication.materialized_actions == ()
    assert (
        publication.observation_references[0].observation_digest
        == observation.observation_digest
    )
    assert not summary.exists()


def test_live_absence_creates_only_current_profile_action(observation_case):
    case = observation_case
    observation = observe(
        case, Transport(responses(case, version_state="absent"))
    )
    assert observation.classification == "absent"
    publication = materialize_publication_snapshot(
        case.snapshot,
        case.decision,
        (observation,),
        (case.artifact,),
        **materialization_arguments(case),
        destination_operation_profile=github_packages_destination_operation_profile(),
    )
    (action,) = publication.materialized_actions
    assert action.package == case.expectation.package_name
    assert action.version == case.expectation.npm_package_version
    assert (
        action.tarball_reference.payload_digest
        == case.artifact.content.content_sha256
    )
    assert action.tag == f"buddy-sha-{case.intent.target}"
    with pytest.raises(TypeError, match="requires action-creation time"):
        materialize_publication_snapshot(
            case.snapshot,
            case.decision,
            (observation,),
            (case.artifact,),
            **(materialization_arguments(case) | {"action_creation_at": None}),
            destination_operation_profile=github_packages_destination_operation_profile(),
        )
    expired = materialization_arguments(case) | {
        "action_creation_at": case.eligibility.governance.attestation.expires_at
    }
    with pytest.raises(ValueError, match="currently fresh"):
        materialize_publication_snapshot(
            case.snapshot,
            case.decision,
            (observation,),
            (case.artifact,),
            **expired,
            destination_operation_profile=github_packages_destination_operation_profile(),
        )


@pytest.mark.parametrize(
    "observation_case", [NOW - timedelta(days=90, seconds=1)], indirect=True
)
def test_expired_native_acceptance_is_blocking_evidence_not_lost_observation(
    observation_case,
):
    case = observation_case
    observation = observe(
        case, Transport(responses(case, version_state="absent"))
    )
    assert observation.classification == "unprovable"
    assert observation.active_readback.classification == "absent"
    assert (
        "absent-version native acceptance: expired"
        in observation.diagnostics.entries
    )
    assert (
        observe(
            case, Transport(responses(case, tag_state="unreadable"))
        ).classification
        == "exact-satisfied"
    )


@pytest.mark.parametrize(
    ("failure", "expected"),
    [
        ("tag-present", "conflicting"),
        ("tag-unreadable", "unprovable"),
        ("version-unknown", "unknown"),
        ("control-conflict", "conflicting"),
        ("mixed-blockers", "unprovable"),
    ],
)
def test_blocking_remote_facts_persist_and_finalize_with_exact_ancestry(
    observation_case, tmp_path, monkeypatch, failure, expected
):
    case = observation_case
    remote = responses(
        case,
        version_state="absent",
        tag_state="other"
        if failure == "tag-present"
        else "unreadable"
        if failure == "tag-unreadable"
        else "absent",
    )
    if failure in {"version-unknown", "mixed-blockers"}:
        remote[TAGS_URL + "/" + case.expectation.npm_package_version] = (
            GitHubPackagesNetworkError("exact version unavailable")
        )
    if failure in {"control-conflict", "mixed-blockers"}:
        remote[ENDPOINT] = response(
            ENDPOINT,
            {
                "name": "hcoona-release-smoke-npm",
                "package_type": "npm",
                "visibility": "private",
                "repository": {"full_name": "hcoona/three"},
            },
        )
    monkeypatch.setattr(
        cli, "GitHubPackagesHttpTransport", lambda: Transport(remote)
    )
    authority = authority_arguments(tmp_path, case, monkeypatch)
    qualification = qualification_arguments(tmp_path, case)
    observation_path = tmp_path / "observation.json"
    assert (
        cli.main(
            [
                "release",
                "observe-github-packages",
                *current_arguments(case),
                *authority,
                *qualification,
                *uploaded_arguments(
                    tmp_path, "adapter_context", case.context.to_document(), 107
                ),
                "--github-token",
                TOKEN,
                "--output",
                str(observation_path),
            ]
        )
        == 1
    )
    document = json.loads(observation_path.read_bytes())
    assert document["classification"] == expected
    evidence_arguments = []
    for evidence in case.evidence:
        role = {
            "release:build:npm-package": "build_evidence",
            "release:quality:project-test": "project_test_evidence",
            "release:quality:npm-artifact-contents": (
                "artifact_contents_evidence"
            ),
            "release:quality:npm-install-import": "install_import_evidence",
        }[evidence.obligation.obligation_id]
        evidence_arguments += uploaded_arguments(
            tmp_path,
            role,
            evidence.to_document(),
            200 + len(evidence_arguments),
        )
    outcome = tmp_path / "outcome.json"
    observation_digest = canonical_sha256(document)
    observation_reference = ArtifactReference(
        artifact_id=108,
        artifact_digest=observation_digest,
        artifact_url=(
            "https://github.com/hcoona/three/actions/runs/"
            f"{case.intent.workflow_run_id}/artifacts/108"
        ),
        payload_path="observation.json",
        payload_digest=observation_digest,
    )
    finalizer = [
        "release",
        "finalize-live",
        *current_arguments(case),
        *authority,
        *qualification,
        *evidence_arguments,
        *uploaded_arguments(
            tmp_path,
            "observation",
            document,
            108,
            reference=observation_reference,
        ),
        "--publisher-conclusion",
        "skipped",
        "--publication-terminal-reference",
        "null",
        "--observation-conclusion",
        "failure",
        "--outcome-output",
        str(outcome),
        "--summary-output",
        str(tmp_path / "outcome.md"),
    ]
    assert cli.main(finalizer) == 1
    outcome_document = json.loads(outcome.read_bytes())
    assert outcome_document["disposition"] == "failed-before-publication"
    assert outcome_document["possibly-mutated"] is False
    assert outcome_document["direct-predecessor"] == {
        "kind": "blocking-observation",
        "reference": observation_reference.to_document(),
    }
    if failure == "version-unknown":
        retained_outcome = outcome.read_bytes()

        class HistoricalClock(datetime):
            @classmethod
            def now(cls, tz=None):
                assert tz is not None
                return (
                    case.eligibility.governance.attestation.expires_at
                    + timedelta(days=1)
                )

        monkeypatch.setattr(cli, "datetime", HistoricalClock)
        outcome.unlink()
        assert cli.main(finalizer) == 1
        assert outcome.read_bytes() == retained_outcome
    outcome.unlink()
    decision_index = finalizer.index("--qualification-decision-artifact-id") + 1
    finalizer[decision_index] = "803"
    url_index = finalizer.index("--qualification-decision-artifact-url") + 1
    finalizer[url_index] = case.decision_reference.artifact_url.replace(
        "/802", "/803"
    )
    assert cli.main(finalizer) == 1
    assert not outcome.exists()


@pytest.mark.parametrize(
    "field", ["artifact-id", "artifact-digest", "payload-digest"]
)
def test_eligibility_transport_must_match_exact_attempt_authority(
    observation_case, tmp_path, monkeypatch, field
):
    case = observation_case
    authority = authority_arguments(tmp_path, case, monkeypatch)
    authority[authority.index(f"--live-eligibility-{field}") + 1] = (
        "7002" if field == "artifact-id" else "sha256:" + "0" * 64
    )
    transport = Transport({})
    monkeypatch.setattr(cli, "GitHubPackagesHttpTransport", lambda: transport)
    output = tmp_path / "observation.json"
    assert (
        cli.main(
            [
                "release",
                "observe-github-packages",
                *current_arguments(case),
                *authority,
                *qualification_arguments(tmp_path, case),
                *uploaded_arguments(
                    tmp_path, "adapter_context", case.context.to_document(), 107
                ),
                "--github-token",
                TOKEN,
                "--output",
                str(output),
            ]
        )
        == 1
    )
    assert transport.requests == []
    assert not output.exists()
