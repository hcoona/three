"""Commit-10 Governance Acceptance Evidence contract scenarios."""

# ruff: noqa: D103, E501, FBT001, PLR0913, PLR0917, PT011, SLF001

from __future__ import annotations

import itertools
from copy import deepcopy
from typing import Any

import pytest
import three_workflow_delivery_v3.records.governance as governance_module
from three_workflow_delivery_v3.adapters.github_packages import (
    AcceptanceRunnerDiagnostic,
    FixedAcceptanceSuiteResult,
    FixedCoordinateAcceptanceProbeResult,
    ValidatedAcceptanceRequestProof,
)
from three_workflow_delivery_v3.canonical import canonical_sha256, canonicalize
from three_workflow_delivery_v3.records.governance import (
    GOVERNANCE_ACCEPTANCE_DEPENDENCIES,
    GOVERNANCE_ACCEPTANCE_PROBE_SCENARIOS,
    GOVERNANCE_ACCEPTANCE_PROBES,
    GOVERNANCE_ACCEPTANCE_SCENARIOS,
    GOVERNANCE_RETRY_3_ACCEPTANCE_ENVIRONMENT,
    GOVERNANCE_RETRY_3_ACCEPTANCE_PACKAGE_COORDINATE,
    GOVERNANCE_RETRY_3_ACCEPTANCE_SCENARIO_COORDINATES,
    GOVERNANCE_RETRY_3_ACCEPTANCE_WORKFLOW_PATH,
    admit_governance_acceptance_evidence,
)

SHA256_A = "sha256:" + ("1" * 64)
SHA256_B = "sha256:" + ("2" * 64)
SHA512_A = "sha512:" + ("3" * 128)
SHA512_B = "sha512:" + ("4" * 128)
HISTORICAL_ABSENT_CREATE_READBACK_RECORD_DIGEST = (
    "sha256:c8035ad8ff0c3f29ad21a05c54dac6147d2adf2df6a5464467470cc7e2e8462d"
)
HISTORICAL_EXACT_AND_CONFLICT_RECORD_DIGEST = (
    "sha256:a7380d429012883511f72c8540d6a97e876bd40ef3f231658616f4dd5865526a"
)
ENVIRONMENT = "workflow-delivery-v3-buddy-smoke-acceptance"
COORDINATE = "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.1"
LEGACY_TARGET_SHA = "5a84bebd05407e1859fe76f400dcb4f4cbcd002e"
LEGACY_CONFIRMATION_DIGEST = (
    "sha256:6ab9696b51f21083802af68d80104f65ffb844bdcd449974c881e5a8cc96ad5e"
)
RETRY_3_TARGET_SHA = "a61f9a4e44458bfd7bc7bfd96f6db848ce047c0c"


def _validated_request_proof() -> ValidatedAcceptanceRequestProof:
    return ValidatedAcceptanceRequestProof.from_validated_exchange(
        raw_request=b'{"_id":"@hcoona/hcoona-release-smoke-npm"}',
        tarball=b"governance-lost-response-tarball",
        package_coordinate=(
            "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.4"
        ),
        tag="wdv3-acceptance-4",
        upstream_status=201,
        selected_headers={"Content-Type": "application/json", "ETag": '"v1"'},
        response_body=b'{"ok":true}',
    )


def _normal_validated_request_proof() -> ValidatedAcceptanceRequestProof:
    return ValidatedAcceptanceRequestProof.from_validated_exchange(
        raw_request=b'{"_id":"@hcoona/hcoona-release-smoke-npm"}',
        tarball=b"governance-normal-create-tarball",
        package_coordinate=(
            "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.1"
        ),
        tag="wdv3-acceptance-1",
        upstream_status=201,
        selected_headers={"Content-Type": "application/json", "ETag": '"v1"'},
        response_body=b'{"ok":true}',
    )


LOST_RESPONSE_PROOF = _validated_request_proof()
LOST_RESPONSE_PROOF_DOCUMENT = LOST_RESPONSE_PROOF.to_document()
NORMAL_CREATE_PROOF = _normal_validated_request_proof()
NORMAL_CREATE_PROOF_DOCUMENT = NORMAL_CREATE_PROOF.to_document()
CANONICAL_SCENARIOS: dict[str, dict[str, Any]] = {
    "absent-create-readback": {
        "scenario": "absent-create-readback",
        "package-coordinate": (
            "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.1"
        ),
        "tag": "wdv3-acceptance-1",
        "mutation-classification": "complete",
        "pre": {"state": "absent"},
        "action": {
            "operation": "npm-publish-create-only",
            "executed": True,
            "mutation-started": True,
        },
        "response": {
            "result": "created",
            "identity-digest": SHA256_B,
            "diagnostics": [],
        },
        "post": {"state": "exact", "content-sha512": SHA512_A},
    },
    "exact": {
        "scenario": "exact",
        "package-coordinate": (
            "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.1"
        ),
        "tag": "wdv3-acceptance-1",
        "mutation-classification": "complete",
        "pre": {"state": "exact"},
        "action": {
            "operation": "npm-publish-create-only",
            "executed": False,
            "mutation-started": False,
        },
        "response": {
            "result": "exact-no-mutation",
            "identity-digest": SHA256_B,
            "diagnostics": [],
        },
        "post": {"state": "exact", "content-sha512": SHA512_A},
    },
    "identical-race": {
        "scenario": "identical-race",
        "package-coordinate": (
            "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.2"
        ),
        "tag": "wdv3-acceptance-2",
        "mutation-classification": "complete",
        "pre": {"state": "absent"},
        "action": {
            "operation": "npm-publish-create-only",
            "executed": True,
            "mutation-started": True,
        },
        "response": {
            "result": "identical-race-exact",
            "identity-digest": SHA256_B,
            "diagnostics": ["identical-race-exact"],
        },
        "post": {"state": "exact", "content-sha512": SHA512_A},
    },
    "differing-race": {
        "scenario": "differing-race",
        "package-coordinate": (
            "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.3"
        ),
        "tag": "wdv3-acceptance-3",
        "mutation-classification": "complete",
        "pre": {"state": "absent"},
        "action": {
            "operation": "npm-publish-create-only",
            "executed": True,
            "mutation-started": True,
        },
        "response": {
            "result": "differing-race-conflict",
            "identity-digest": SHA256_B,
            "diagnostics": ["conflicting-remote-bytes-or-tag"],
        },
        "post": {"state": "conflicting", "content-sha512": SHA512_B},
    },
    "lost-response": {
        "scenario": "lost-response",
        "package-coordinate": (
            "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.4"
        ),
        "tag": "wdv3-acceptance-4",
        "mutation-classification": "complete",
        "pre": {"state": "absent"},
        "action": {
            "operation": "npm-publish-create-only",
            "executed": True,
            "mutation-started": True,
        },
        "response": {
            "result": "lost-response-exact-after-start",
            "identity-digest": LOST_RESPONSE_PROOF.response_identity_digest,
            "diagnostics": ["mutation-started-and-readback-exact"],
        },
        "post": {
            "state": "exact",
            "content-sha512": SHA512_A,
        },
        "validated-request-proof": LOST_RESPONSE_PROOF_DOCUMENT,
    },
}


def _scenario(scenario: str) -> dict[str, Any]:
    return deepcopy(CANONICAL_SCENARIOS[scenario])


def _probe_fact(probe: str) -> dict[str, Any]:
    inventory = GOVERNANCE_ACCEPTANCE_PROBE_SCENARIOS[probe]
    scenarios = [_scenario(scenario) for scenario in inventory]
    suite = FixedAcceptanceSuiteResult(
        suite=probe.removeprefix("probe-"),
        scenarios=tuple(
            FixedCoordinateAcceptanceProbeResult(
                scenario=scenario["scenario"],
                package_coordinate=scenario["package-coordinate"],
                tag=scenario["tag"],
                pre_state=scenario["pre"]["state"],
                post_state=scenario["post"]["state"],
                result=scenario["response"]["result"],
                mutation_classification="complete",
                action_executed=scenario["action"]["executed"],
                mutation_started=scenario["action"]["mutation-started"],
                response_identity_digest=scenario["response"][
                    "identity-digest"
                ],
                content_sha512=scenario["post"]["content-sha512"],
                diagnostics=tuple(scenario["response"]["diagnostics"]),
                validated_request_proof=(
                    LOST_RESPONSE_PROOF
                    if scenario["scenario"] == "lost-response"
                    else None
                ),
            )
            for scenario in scenarios
        ),
    )
    suite_document = suite.to_document()
    return {
        "probe": probe,
        "result": "success",
        "scenario-inventory": list(inventory),
        "record-digest": suite_document["record-digest"],
        "artifact-id": 700 + GOVERNANCE_ACCEPTANCE_PROBES.index(probe),
        "artifact-digest": SHA256_B,
        "scenarios": scenarios,
    }


def _document() -> dict[str, Any]:
    return {
        "schema": "workflow-delivery/v3/governance-acceptance-evidence",
        "purpose": "destination-acceptance",
        "workflow": {
            "repository": "hcoona/three",
            "path": (
                ".github/workflows/"
                "workflow-delivery-v3-buddy-smoke-acceptance.yml"
            ),
            "ref": "refs/heads/main",
            "sha": "b" * 40,
        },
        "target-sha": LEGACY_TARGET_SHA,
        "package-coordinate": COORDINATE,
        "confirmation-digest": LEGACY_CONFIRMATION_DIGEST,
        "environment": ENVIRONMENT,
        "reviewer": {"login": None, "source": "unavailable-in-job-context"},
        "recovery": {
            "workflow-run-id": 101,
            "environment": ENVIRONMENT,
            "deployment": "run:101/environment:acceptance",
            "job": "acceptance-review",
            "artifact-id": 701,
        },
        "dependency-results": [
            {"job": job, "result": "success"}
            for job in GOVERNANCE_ACCEPTANCE_DEPENDENCIES
        ],
        "probe-facts": [
            _probe_fact(probe) for probe in GOVERNANCE_ACCEPTANCE_PROBES
        ],
        "mutation-classification": "complete",
        "producer": "capture-governance-evidence",
        "workflow-run-id": 101,
        "run-attempt": 1,
        "release-lineage": "none",
    }


def _downgrade_all_probe_facts(
    document: dict[str, Any],
    result: str,
) -> None:
    for fact in document["probe-facts"]:
        fact.update(
            {
                "result": result,
                "record-digest": None,
                "artifact-id": None,
                "artifact-digest": None,
                "scenarios": [],
            }
        )


def _admit(document: dict[str, Any]) -> Any:
    return admit_governance_acceptance_evidence(canonicalize(document))


def _remove_path(document: dict[str, Any], path: tuple[object, ...]) -> None:
    cursor: Any = document
    for key in path[:-1]:
        cursor = cursor[key]
    if isinstance(cursor, list):
        index = path[-1]
        if not isinstance(index, int):
            message = "list path component must be an integer"
            raise TypeError(message)
        cursor.pop(index)
    else:
        del cursor[path[-1]]


def _set_path(
    document: dict[str, Any],
    path: tuple[object, ...],
    value: object,
) -> None:
    cursor: Any = document
    for key in path[:-1]:
        cursor = cursor[key]
    cursor[path[-1]] = value


REQUIRED_FIELD_PATHS: tuple[tuple[object, ...], ...] = (
    ("schema",),
    ("purpose",),
    ("workflow",),
    ("workflow", "repository"),
    ("workflow", "path"),
    ("workflow", "ref"),
    ("workflow", "sha"),
    ("target-sha",),
    ("package-coordinate",),
    ("confirmation-digest",),
    ("environment",),
    ("reviewer",),
    ("reviewer", "login"),
    ("reviewer", "source"),
    ("recovery",),
    ("recovery", "workflow-run-id"),
    ("recovery", "environment"),
    ("recovery", "deployment"),
    ("recovery", "job"),
    ("recovery", "artifact-id"),
    ("dependency-results",),
    ("dependency-results", 0, "job"),
    ("dependency-results", 0, "result"),
    ("dependency-results", 1, "job"),
    ("dependency-results", 1, "result"),
    ("dependency-results", 2, "job"),
    ("dependency-results", 2, "result"),
    ("dependency-results", 3, "job"),
)


CLOSED_SCHEMA_EXTRA_PATHS: tuple[tuple[object, ...], ...] = (
    ("extra",),
    ("workflow", "extra"),
    ("reviewer", "extra"),
    ("recovery", "extra"),
    ("dependency-results", 0, "extra"),
    ("dependency-results", 1, "extra"),
    ("dependency-results", 2, "extra"),
    ("dependency-results", 3, "extra"),
    ("probe-facts", 0, "extra"),
    ("probe-facts", 1, "extra"),
    ("probe-facts", 0, "scenarios", 0, "extra"),
    ("probe-facts", 1, "scenarios", 0, "extra"),
    ("probe-facts", 1, "scenarios", 1, "extra"),
    ("probe-facts", 1, "scenarios", 2, "extra"),
    ("probe-facts", 1, "scenarios", 3, "extra"),
    ("probe-facts", 0, "scenarios", 0, "pre", "extra"),
    ("probe-facts", 0, "scenarios", 0, "action", "extra"),
    ("probe-facts", 0, "scenarios", 0, "response", "extra"),
    ("probe-facts", 0, "scenarios", 0, "post", "extra"),
    ("probe-facts", 1, "scenarios", 0, "pre", "extra"),
    ("probe-facts", 1, "scenarios", 0, "action", "extra"),
    ("probe-facts", 1, "scenarios", 0, "response", "extra"),
    ("probe-facts", 1, "scenarios", 0, "post", "extra"),
    ("probe-facts", 1, "scenarios", 1, "pre", "extra"),
    ("probe-facts", 1, "scenarios", 1, "action", "extra"),
    ("probe-facts", 1, "scenarios", 1, "response", "extra"),
    ("probe-facts", 1, "scenarios", 1, "post", "extra"),
    ("probe-facts", 1, "scenarios", 2, "pre", "extra"),
    ("probe-facts", 1, "scenarios", 2, "action", "extra"),
    ("probe-facts", 1, "scenarios", 2, "response", "extra"),
    ("probe-facts", 1, "scenarios", 2, "post", "extra"),
    ("probe-facts", 1, "scenarios", 3, "pre", "extra"),
    ("probe-facts", 1, "scenarios", 3, "action", "extra"),
    ("probe-facts", 1, "scenarios", 3, "response", "extra"),
    ("probe-facts", 1, "scenarios", 3, "post", "extra"),
    ("probe-facts", 0, "scenarios", 0, "response", "diagnostics-extra"),
    ("probe-facts", 1, "scenarios", 0, "response", "diagnostics-extra"),
    ("probe-facts", 1, "scenarios", 1, "response", "diagnostics-extra"),
    ("probe-facts", 1, "scenarios", 2, "response", "diagnostics-extra"),
    ("probe-facts", 1, "scenarios", 3, "response", "diagnostics-extra"),
)


def test_complete_evidence_binds_exact_five_scenarios_and_artifacts() -> None:
    evidence = _admit(_document())

    assert evidence.mutation_classification == "complete"
    assert (
        tuple(
            scenario
            for fact in evidence.probe_facts
            for scenario in fact.scenario_inventory
        )
        == GOVERNANCE_ACCEPTANCE_SCENARIOS
    )
    for fact in evidence.probe_facts:
        assert tuple(fact.scenarios) == tuple(
            CANONICAL_SCENARIOS[scenario]
            for scenario in GOVERNANCE_ACCEPTANCE_PROBE_SCENARIOS[fact.probe]
        )
        expected_suite = FixedAcceptanceSuiteResult(
            suite=fact.probe.removeprefix("probe-"),
            scenarios=tuple(
                FixedCoordinateAcceptanceProbeResult(
                    scenario=str(scenario["scenario"]),
                    package_coordinate=str(scenario["package-coordinate"]),
                    tag=str(scenario["tag"]),
                    pre_state=str(scenario["pre"]["state"]),
                    post_state=str(scenario["post"]["state"]),
                    result=str(scenario["response"]["result"]),
                    mutation_classification="complete",
                    action_executed=bool(scenario["action"]["executed"]),
                    mutation_started=bool(
                        scenario["action"]["mutation-started"]
                    ),
                    response_identity_digest=str(
                        scenario["response"]["identity-digest"]
                    ),
                    content_sha512=str(scenario["post"]["content-sha512"]),
                    diagnostics=tuple(scenario["response"]["diagnostics"]),
                    validated_request_proof=(
                        LOST_RESPONSE_PROOF
                        if scenario["scenario"] == "lost-response"
                        else None
                    ),
                )
                for scenario in fact.scenarios
            ),
        )
        assert (
            fact.record_digest == expected_suite.to_document()["record-digest"]
        )
    assert all(fact.artifact_id is not None for fact in evidence.probe_facts)
    assert all(
        fact.artifact_digest == SHA256_B for fact in evidence.probe_facts
    )
    assert evidence.reviewer is None
    assert evidence.reviewer_source == "unavailable-in-job-context"
    assert evidence.to_document()["release-lineage"] == "none"


@pytest.mark.parametrize(
    "field",
    ["record-digest", "artifact-id", "artifact-digest"],
)
@pytest.mark.parametrize("probe_index", [0, 1])
def test_complete_rejects_missing_or_placeholder_suite_binding(
    probe_index: int,
    field: str,
) -> None:
    document = _document()
    document["probe-facts"][probe_index][field] = None

    with pytest.raises(ValueError, match="requires immutable suite"):
        _admit(document)


@pytest.mark.parametrize("probe_index", [0, 1])
def test_complete_requires_every_scenario_record(probe_index: int) -> None:
    document = _document()
    document["probe-facts"][probe_index]["scenarios"].pop()

    with pytest.raises(ValueError):
        _admit(document)


@pytest.mark.parametrize("probe_index", [0, 1])
def test_complete_requires_exact_scenario_inventory(probe_index: int) -> None:
    document = _document()
    document["probe-facts"][probe_index]["scenario-inventory"][0] = "unexpected"

    with pytest.raises(ValueError, match="scenario inventory"):
        _admit(document)


@pytest.mark.parametrize(
    ("scenario_name", "field_path", "replacement", "message"),
    [
        ("absent-create-readback", ("pre", "state"), "unknown", "pre.state"),
        ("exact", ("pre", "state"), "unsupported", "pre.state"),
        (
            "identical-race",
            ("action", "operation"),
            "npm-dist-tag-add",
            "operation",
        ),
        ("identical-race", ("action", "executed"), False, "action.executed"),
        (
            "lost-response",
            ("action", "mutation-started"),
            False,
            "mutation-started",
        ),
        (
            "differing-race",
            ("response", "result"),
            "unknown",
            "response.result",
        ),
        (
            "lost-response",
            ("response", "result"),
            "arbitrary-success",
            "response.result",
        ),
        (
            "identical-race",
            ("response", "identity-digest"),
            SHA256_A,
            "record-digest",
        ),
        ("differing-race", ("post", "state"), "unknown", "post.state"),
        (
            "lost-response",
            ("post", "content-sha512"),
            SHA512_B,
            "record-digest",
        ),
    ],
)
def test_complete_evidence_rejects_noncanonical_scenario_semantics(
    scenario_name: str,
    field_path: tuple[str, str],
    replacement: object,
    message: str,
) -> None:
    document = _document()
    probe = next(
        probe
        for probe, scenarios in GOVERNANCE_ACCEPTANCE_PROBE_SCENARIOS.items()
        if scenario_name in scenarios
    )
    probe_index = GOVERNANCE_ACCEPTANCE_PROBES.index(probe)
    scenario_index = GOVERNANCE_ACCEPTANCE_PROBE_SCENARIOS[probe].index(
        scenario_name
    )
    scenario = document["probe-facts"][probe_index]["scenarios"][scenario_index]
    container, field = field_path
    scenario[container][field] = replacement

    with pytest.raises(ValueError, match=message):
        _admit(document)


@pytest.mark.parametrize("probe_index", [0, 1])
def test_complete_evidence_rejects_arbitrary_record_digest(
    probe_index: int,
) -> None:
    document = _document()
    document["probe-facts"][probe_index]["record-digest"] = SHA256_A

    with pytest.raises(ValueError, match="canonical scenario suite digest"):
        _admit(document)


def test_complete_lost_response_evidence_preserves_validated_request_proof() -> (
    None
):
    document = _document()

    evidence = _admit(document)
    admitted_lost_response = evidence.to_document()["probe-facts"][1][
        "scenarios"
    ][3]

    assert admitted_lost_response["validated-request-proof"] == (
        LOST_RESPONSE_PROOF_DOCUMENT
    )
    assert (
        evidence.probe_facts[1].record_digest
        == document["probe-facts"][1]["record-digest"]
    )


def test_historical_created_evidence_without_proof_remains_admissible() -> None:
    document = _document()
    document["probe-facts"][0]["record-digest"] = (
        HISTORICAL_ABSENT_CREATE_READBACK_RECORD_DIGEST
    )
    document["probe-facts"][1]["record-digest"] = (
        HISTORICAL_EXACT_AND_CONFLICT_RECORD_DIGEST
    )

    evidence = _admit(document)
    admitted = evidence.to_document()["probe-facts"][0]["scenarios"][0]

    assert admitted["response"]["result"] == "created"
    assert evidence.probe_facts[0].record_digest == (
        HISTORICAL_ABSENT_CREATE_READBACK_RECORD_DIGEST
    )
    assert evidence.probe_facts[1].record_digest == (
        HISTORICAL_EXACT_AND_CONFLICT_RECORD_DIGEST
    )
    assert "validated-request-proof" not in admitted
    assert "runner-diagnostic" not in admitted


def test_protocol_confirmed_governance_binds_proof_and_runner_diagnostic() -> (
    None
):
    document = _document()
    scenario = document["probe-facts"][0]["scenarios"][0]
    scenario["response"]["result"] = "protocol-confirmed"
    scenario["response"]["identity-digest"] = (
        NORMAL_CREATE_PROOF.response_identity_digest
    )
    scenario["post"]["content-sha512"] = NORMAL_CREATE_PROOF.tarball_sha512
    scenario["validated-request-proof"] = NORMAL_CREATE_PROOF_DOCUMENT
    scenario["runner-diagnostic"] = {
        "exit-classification": "protocol-confirmed",
        "upstream-status": 201,
        "exception-category": None,
        "request-correlation-digest": NORMAL_CREATE_PROOF.request_digest,
    }
    _refresh_probe_record_digest(document, 0)

    evidence = _admit(document)
    admitted = evidence.to_document()["probe-facts"][0]["scenarios"][0]

    assert admitted["validated-request-proof"] == NORMAL_CREATE_PROOF_DOCUMENT
    assert admitted["runner-diagnostic"] == scenario["runner-diagnostic"]


def test_protocol_confirmed_governance_does_not_require_runner_diagnostic() -> (
    None
):
    document = _document()
    scenario = document["probe-facts"][0]["scenarios"][0]
    scenario["response"]["result"] = "protocol-confirmed"
    scenario["response"]["identity-digest"] = (
        NORMAL_CREATE_PROOF.response_identity_digest
    )
    scenario["post"]["content-sha512"] = NORMAL_CREATE_PROOF.tarball_sha512
    scenario["validated-request-proof"] = NORMAL_CREATE_PROOF_DOCUMENT
    _refresh_probe_record_digest(document, 0)

    evidence = _admit(document)
    admitted = evidence.to_document()["probe-facts"][0]["scenarios"][0]

    assert admitted["response"]["result"] == "protocol-confirmed"
    assert admitted["validated-request-proof"] == NORMAL_CREATE_PROOF_DOCUMENT
    assert "runner-diagnostic" not in admitted


def test_protocol_confirmed_governance_requires_validated_request_proof() -> (
    None
):
    document = _document()
    scenario = document["probe-facts"][0]["scenarios"][0]
    scenario["response"]["result"] = "protocol-confirmed"
    scenario["response"]["identity-digest"] = (
        NORMAL_CREATE_PROOF.response_identity_digest
    )
    scenario["post"]["content-sha512"] = NORMAL_CREATE_PROOF.tarball_sha512
    _refresh_probe_record_digest(document, 0)

    with pytest.raises(ValueError, match="validated-request-proof"):
        _admit(document)


def test_protocol_confirmed_governance_diagnostic_exit_is_non_authoritative() -> (
    None
):
    document = _document()
    scenario = document["probe-facts"][0]["scenarios"][0]
    scenario["response"]["result"] = "protocol-confirmed"
    scenario["response"]["identity-digest"] = (
        NORMAL_CREATE_PROOF.response_identity_digest
    )
    scenario["post"]["content-sha512"] = NORMAL_CREATE_PROOF.tarball_sha512
    scenario["validated-request-proof"] = NORMAL_CREATE_PROOF_DOCUMENT
    scenario["runner-diagnostic"] = {
        "exit-classification": "runner-failed-after-mutation-start",
        "upstream-status": 201,
        "exception-category": None,
        "request-correlation-digest": NORMAL_CREATE_PROOF.request_digest,
    }
    _refresh_probe_record_digest(document, 0)

    evidence = _admit(document)
    admitted = evidence.to_document()["probe-facts"][0]["scenarios"][0]

    assert admitted["response"]["result"] == "protocol-confirmed"
    assert admitted["runner-diagnostic"] == scenario["runner-diagnostic"]


def test_runner_diagnostic_rejects_contradictory_action_startedness() -> None:
    document = _document()
    scenario = document["probe-facts"][0]["scenarios"][0]
    scenario["response"]["result"] = "protocol-confirmed"
    scenario["response"]["identity-digest"] = (
        NORMAL_CREATE_PROOF.response_identity_digest
    )
    scenario["post"]["content-sha512"] = NORMAL_CREATE_PROOF.tarball_sha512
    scenario["validated-request-proof"] = NORMAL_CREATE_PROOF_DOCUMENT
    scenario["runner-diagnostic"] = {
        "exit-classification": "runner-failed-before-mutation",
        "upstream-status": None,
        "exception-category": None,
        "request-correlation-digest": None,
    }
    _refresh_probe_record_digest(document, 0)

    with pytest.raises(ValueError, match="startedness contradicts action"):
        _admit(document)


def test_legacy_created_rejects_protocol_confirmed_runner_diagnostic() -> None:
    document = _document()
    scenario = document["probe-facts"][0]["scenarios"][0]
    scenario["runner-diagnostic"] = {
        "exit-classification": "protocol-confirmed",
        "upstream-status": 201,
        "exception-category": None,
        "request-correlation-digest": NORMAL_CREATE_PROOF.request_digest,
    }
    _refresh_probe_record_digest_unchecked(document, 0)

    with pytest.raises(ValueError, match="runner-diagnostic"):
        _admit(document)


def test_explicit_null_runner_diagnostic_is_rejected() -> None:
    document = _document()
    document["probe-facts"][0]["scenarios"][0]["runner-diagnostic"] = None
    _refresh_probe_record_digest_unchecked(document, 0)

    with pytest.raises(TypeError, match="runner-diagnostic"):
        _admit(document)


@pytest.mark.parametrize(
    "field",
    [
        "exit-classification",
        "upstream-status",
        "exception-category",
        "request-correlation-digest",
        "unknown",
    ],
)
def test_runner_diagnostic_is_a_closed_required_field_object(
    field: str,
) -> None:
    document = _document()
    scenario = document["probe-facts"][0]["scenarios"][0]
    diagnostic = {
        "exit-classification": "runner-failed-after-mutation-start",
        "upstream-status": None,
        "exception-category": None,
        "request-correlation-digest": None,
    }
    if field == "unknown":
        diagnostic["unknown"] = None
    else:
        del diagnostic[field]
    scenario["runner-diagnostic"] = diagnostic
    _refresh_probe_record_digest_unchecked(document, 0)

    with pytest.raises((TypeError, ValueError), match="runner-diagnostic"):
        _admit(document)


def test_protocol_confirmed_readback_incomplete_diagnostic_is_admissible() -> (
    None
):
    document = _document()
    scenario = document["probe-facts"][0]["scenarios"][0]
    scenario["mutation-classification"] = "incomplete"
    scenario["response"]["result"] = "protocol-confirmed-readback-incomplete"
    scenario["response"]["identity-digest"] = (
        NORMAL_CREATE_PROOF.response_identity_digest
    )
    scenario["post"]["state"] = "unknown"
    scenario["validated-request-proof"] = NORMAL_CREATE_PROOF_DOCUMENT
    scenario["runner-diagnostic"] = {
        "exit-classification": "protocol-confirmed",
        "upstream-status": 201,
        "exception-category": None,
        "request-correlation-digest": NORMAL_CREATE_PROOF.request_digest,
    }
    document["probe-facts"][0]["result"] = "incomplete"
    document["mutation-classification"] = "incomplete"
    _refresh_probe_record_digest_unchecked(document, 0)

    evidence = _admit(document)
    admitted = evidence.to_document()["probe-facts"][0]["scenarios"][0]

    assert (
        admitted["response"]["result"]
        == "protocol-confirmed-readback-incomplete"
    )
    assert admitted["validated-request-proof"] == NORMAL_CREATE_PROOF_DOCUMENT
    assert admitted["runner-diagnostic"] == scenario["runner-diagnostic"]


def test_protocol_confirmed_readback_incomplete_rejects_unknown_classification() -> (
    None
):
    document = _document()
    scenario = document["probe-facts"][0]["scenarios"][0]
    scenario["mutation-classification"] = "unknown"
    scenario["response"]["result"] = "protocol-confirmed-readback-incomplete"
    scenario["response"]["identity-digest"] = (
        NORMAL_CREATE_PROOF.response_identity_digest
    )
    scenario["post"]["state"] = "unknown"
    scenario["validated-request-proof"] = NORMAL_CREATE_PROOF_DOCUMENT
    scenario["runner-diagnostic"] = {
        "exit-classification": "protocol-confirmed",
        "upstream-status": 201,
        "exception-category": None,
        "request-correlation-digest": NORMAL_CREATE_PROOF.request_digest,
    }
    document["probe-facts"][0]["result"] = "unknown"
    document["mutation-classification"] = "unknown"
    _refresh_probe_record_digest_unchecked(document, 0)

    with pytest.raises(ValueError, match="incomplete mutation classification"):
        _admit(document)


def test_protocol_confirmed_readback_incomplete_requires_proof() -> None:
    document = _document()
    scenario = document["probe-facts"][0]["scenarios"][0]
    scenario["mutation-classification"] = "incomplete"
    scenario["response"]["result"] = "protocol-confirmed-readback-incomplete"
    scenario["post"]["state"] = "unknown"
    document["probe-facts"][0]["result"] = "incomplete"
    document["mutation-classification"] = "incomplete"
    _refresh_probe_record_digest_unchecked(document, 0)

    with pytest.raises(ValueError, match="validated-request-proof"):
        _admit(document)


@pytest.mark.parametrize(
    ("action_executed", "mutation_started"),
    [(False, True), (True, False)],
    ids=["action-not-executed", "mutation-not-started"],
)
def test_protocol_confirmed_readback_incomplete_requires_startedness(
    action_executed: bool,
    mutation_started: bool,
) -> None:
    document = _document()
    scenario = document["probe-facts"][0]["scenarios"][0]
    scenario["mutation-classification"] = "incomplete"
    scenario["action"]["executed"] = action_executed
    scenario["action"]["mutation-started"] = mutation_started
    scenario["response"]["result"] = "protocol-confirmed-readback-incomplete"
    scenario["response"]["identity-digest"] = (
        NORMAL_CREATE_PROOF.response_identity_digest
    )
    scenario["post"]["state"] = "unknown"
    scenario["validated-request-proof"] = NORMAL_CREATE_PROOF_DOCUMENT
    document["probe-facts"][0]["result"] = "incomplete"
    document["mutation-classification"] = "incomplete"
    _refresh_probe_record_digest_unchecked(document, 0)

    with pytest.raises(
        ValueError,
        match="requires admitted execution and mutation startedness",
    ):
        _admit(document)


def test_runner_diagnostic_cross_binds_each_present_fact_independently() -> (
    None
):
    document = _document()
    scenario = document["probe-facts"][1]["scenarios"][3]
    scenario["runner-diagnostic"] = {
        "exit-classification": "runner-failed-after-mutation-start",
        "upstream-status": 201,
        "exception-category": None,
        "request-correlation-digest": None,
    }
    _refresh_probe_record_digest(document, 1)

    evidence = _admit(document)
    admitted = evidence.to_document()["probe-facts"][1]["scenarios"][3]

    assert admitted["runner-diagnostic"] == scenario["runner-diagnostic"]


def test_runner_diagnostic_status_and_correlation_require_bound_proof() -> None:
    document = _document()
    scenario = document["probe-facts"][0]["scenarios"][0]
    scenario["runner-diagnostic"] = {
        "exit-classification": "runner-failed-after-mutation-start",
        "upstream-status": 201,
        "exception-category": None,
        "request-correlation-digest": NORMAL_CREATE_PROOF.request_digest,
    }
    _refresh_probe_record_digest_unchecked(document, 0)

    with pytest.raises(ValueError, match="validated-request-proof"):
        _admit(document)


def test_protocol_confirmed_result_requires_exact_complete_readback() -> None:
    document = _document()
    scenario = document["probe-facts"][0]["scenarios"][0]
    scenario["response"]["result"] = "protocol-confirmed"
    scenario["response"]["identity-digest"] = (
        NORMAL_CREATE_PROOF.response_identity_digest
    )
    scenario["post"]["content-sha512"] = NORMAL_CREATE_PROOF.tarball_sha512
    scenario["validated-request-proof"] = NORMAL_CREATE_PROOF_DOCUMENT
    scenario["runner-diagnostic"] = {
        "exit-classification": "protocol-confirmed",
        "upstream-status": 201,
        "exception-category": None,
        "request-correlation-digest": NORMAL_CREATE_PROOF.request_digest,
    }
    scenario["mutation-classification"] = "incomplete"
    scenario["post"]["state"] = "unknown"
    document["probe-facts"][0]["result"] = "incomplete"
    document["mutation-classification"] = "incomplete"
    _refresh_probe_record_digest_unchecked(document, 0)

    with pytest.raises(ValueError, match="exact complete readback"):
        _admit(document)


@pytest.mark.parametrize(
    ("field", "replacement"),
    [
        ("upstream-status", None),
        ("exception-category", "RuntimeError"),
        ("request-correlation-digest", SHA256_A),
    ],
)
def test_protocol_confirmed_governance_rejects_unbound_runner_diagnostic(
    field: str,
    replacement: str | None,
) -> None:
    document = _document()
    scenario = document["probe-facts"][0]["scenarios"][0]
    scenario["response"]["result"] = "protocol-confirmed"
    scenario["response"]["identity-digest"] = (
        NORMAL_CREATE_PROOF.response_identity_digest
    )
    scenario["post"]["content-sha512"] = NORMAL_CREATE_PROOF.tarball_sha512
    scenario["validated-request-proof"] = NORMAL_CREATE_PROOF_DOCUMENT
    scenario["runner-diagnostic"] = {
        "exit-classification": "protocol-confirmed",
        "upstream-status": 201,
        "exception-category": None,
        "request-correlation-digest": NORMAL_CREATE_PROOF.request_digest,
    }
    _refresh_probe_record_digest(document, 0)
    scenario["runner-diagnostic"][field] = replacement
    _refresh_probe_record_digest_unchecked(document, 0)

    with pytest.raises(ValueError, match="runner-diagnostic"):
        _admit(document)


@pytest.mark.parametrize(
    ("path", "replacement", "message"),
    [
        (("schema",), "workflow-delivery/v3/unvalidated", "schema"),
        (("upstream-status",), 202, "upstream-status"),
        (("package-coordinate",), COORDINATE, "package-coordinate"),
        (("tag",), "wdv3-acceptance-1", "tag"),
        (("response-identity-digest",), SHA256_A, "response-identity-digest"),
    ],
    ids=[
        "schema",
        "status",
        "coordinate",
        "tag",
        "identity-digest",
    ],
)
def test_complete_lost_response_evidence_closed_validates_request_proof(
    path: tuple[str, ...],
    replacement: object,
    message: str,
) -> None:
    document = _document()
    proof = deepcopy(LOST_RESPONSE_PROOF_DOCUMENT)
    _set_path(proof, path, replacement)
    document["probe-facts"][1]["scenarios"][3]["validated-request-proof"] = (
        proof
    )
    _refresh_probe_record_digest(document, 1)

    with pytest.raises(ValueError, match=message):
        _admit(document)


def test_complete_lost_response_evidence_requires_validated_request_proof() -> (
    None
):
    document = _document()
    del document["probe-facts"][1]["scenarios"][3]["validated-request-proof"]
    _refresh_probe_record_digest(document, 1)

    with pytest.raises(ValueError, match="validated-request-proof"):
        _admit(document)


def test_protocol_confirmed_proof_tarball_must_match_exact_readback() -> None:
    document = _document()
    scenario = document["probe-facts"][0]["scenarios"][0]
    scenario["response"]["result"] = "protocol-confirmed"
    scenario["response"]["identity-digest"] = (
        NORMAL_CREATE_PROOF.response_identity_digest
    )
    scenario["post"]["content-sha512"] = SHA512_A
    scenario["validated-request-proof"] = NORMAL_CREATE_PROOF_DOCUMENT
    _refresh_probe_record_digest(document, 0)

    with pytest.raises(ValueError, match="tarball-sha512"):
        _admit(document)


def test_incomplete_evidence_admits_complete_scenarios_when_artifact_outputs_missing() -> (
    None
):
    document = _document()
    fact = document["probe-facts"][1]
    fact["result"] = "incomplete"
    fact["artifact-id"] = None
    fact["artifact-digest"] = None
    document["mutation-classification"] = "incomplete"

    evidence = _admit(document)

    assert evidence.mutation_classification == "incomplete"
    assert evidence.probe_facts[1].result == "incomplete"
    assert evidence.probe_facts[1].record_digest == fact["record-digest"]
    assert len(evidence.probe_facts[1].scenarios) == len(
        GOVERNANCE_ACCEPTANCE_PROBE_SCENARIOS["probe-exact-and-conflict"]
    )


@pytest.mark.parametrize(
    ("dependency_job", "terminal_result", "expected_classification"),
    [
        ("probe-absent-create-readback", "failure", "unknown"),
        ("probe-absent-create-readback", "cancelled", "unknown"),
        ("probe-exact-and-conflict", "failure", "unknown"),
        ("probe-exact-and-conflict", "cancelled", "unknown"),
        ("probe-absent-create-readback", "skipped", "incomplete"),
        ("probe-exact-and-conflict", "skipped", "incomplete"),
    ],
)
def test_possibly_started_probe_dependencies_drive_closed_terminal_classification(
    dependency_job: str,
    terminal_result: str,
    expected_classification: str,
) -> None:
    document = _document()
    dependency_index = GOVERNANCE_ACCEPTANCE_DEPENDENCIES.index(dependency_job)
    probe_index = GOVERNANCE_ACCEPTANCE_PROBES.index(dependency_job)
    document["dependency-results"][dependency_index]["result"] = terminal_result
    document["probe-facts"][probe_index].update(
        {
            "result": expected_classification,
            "record-digest": None,
            "artifact-id": None,
            "artifact-digest": None,
            "scenarios": [],
        }
    )
    other_probe_index = 1 - probe_index
    document["probe-facts"][other_probe_index].update(
        {
            "result": expected_classification,
            "record-digest": None,
            "artifact-id": None,
            "artifact-digest": None,
            "scenarios": [],
        }
    )
    document["mutation-classification"] = expected_classification

    assert _admit(document).mutation_classification == expected_classification


def test_later_probe_failure_retains_earlier_successful_probe_evidence() -> (
    None
):
    document = _document()
    dependency_index = GOVERNANCE_ACCEPTANCE_DEPENDENCIES.index(
        "probe-exact-and-conflict"
    )
    document["dependency-results"][dependency_index]["result"] = "failure"
    document["probe-facts"][1].update(
        {
            "result": "unknown",
            "record-digest": None,
            "artifact-id": None,
            "artifact-digest": None,
            "scenarios": [],
        }
    )
    document["mutation-classification"] = "unknown"

    evidence = _admit(document)

    assert evidence.probe_facts[0].result == "success"
    assert evidence.probe_facts[1].result == "unknown"
    assert evidence.mutation_classification == "unknown"


@pytest.mark.parametrize("probe_index", [0, 1])
def test_scenario_records_require_exact_internal_coordinate_and_tag(
    probe_index: int,
) -> None:
    document = _document()
    scenario = document["probe-facts"][probe_index]["scenarios"][0]
    scenario["package-coordinate"] = (
        "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.9"
    )
    scenario["tag"] = "wdv3-acceptance-9"

    with pytest.raises(ValueError):
        _admit(document)


@pytest.mark.parametrize(
    ("dependency_result", "probe_result", "expected"),
    [
        (
            dependency,
            probe,
            "unknown"
            if probe == "unknown"
            else "incomplete"
            if dependency != "success" or probe != "success"
            else "complete",
        )
        for dependency, probe in itertools.product(
            ("success", "failure", "cancelled", "skipped"),
            ("success", "incomplete", "unknown"),
        )
    ],
)
def test_mutation_classification_is_monotone(
    dependency_result: str,
    probe_result: str,
    expected: str,
) -> None:
    document = _document()
    document["dependency-results"][0]["result"] = dependency_result
    document["probe-facts"][0]["result"] = probe_result
    document["mutation-classification"] = expected
    if probe_result != "success":
        document["probe-facts"][0]["scenarios"] = []
        document["probe-facts"][0]["record-digest"] = None
        document["probe-facts"][0]["artifact-id"] = None
        document["probe-facts"][0]["artifact-digest"] = None
    if dependency_result != "success":
        _downgrade_all_probe_facts(document, expected)

    assert _admit(document).mutation_classification == expected


@pytest.mark.parametrize(
    ("dependency_result", "probe_result", "wrong"),
    [
        ("success", "unknown", "incomplete"),
        ("failure", "unknown", "incomplete"),
        ("failure", "success", "complete"),
        ("success", "success", "unknown"),
    ],
)
def test_mutation_classification_rejects_non_monotone_values(
    dependency_result: str,
    probe_result: str,
    wrong: str,
) -> None:
    document = _document()
    document["dependency-results"][0]["result"] = dependency_result
    document["probe-facts"][0]["result"] = probe_result
    document["mutation-classification"] = wrong

    with pytest.raises(
        ValueError, match=r"not consistent|retain suite records"
    ):
        _admit(document)


@pytest.mark.parametrize(
    ("path", "replacement"),
    [
        (("dependency-results", 0, "result"), "unexpected"),
        (("dependency-results", 1, "job"), "arbitrary-job"),
        (("probe-facts", 0, "result"), "cancelled"),
        (("probe-facts", 1, "probe"), "arbitrary-probe"),
        (("probe-facts", 0, "scenario-inventory", 0), "unknown"),
        (("probe-facts", 1, "scenarios", 0, "scenario"), "unknown"),
        (("probe-facts", 1, "scenarios", 1, "response", "result"), ""),
    ],
)
def test_evidence_rejects_unknown_incomplete_or_arbitrary_fact_values(
    path: tuple[object, ...],
    replacement: object,
) -> None:
    document = _document()
    cursor: Any = document
    for key in path[:-1]:
        cursor = cursor[key]
    cursor[path[-1]] = replacement

    with pytest.raises(ValueError):
        _admit(document)


@pytest.mark.parametrize(
    "mutation",
    [
        lambda document: document["dependency-results"].pop(),
        lambda document: document["dependency-results"].append(
            deepcopy(document["dependency-results"][0])
        ),
        lambda document: document["probe-facts"].pop(),
        lambda document: document["probe-facts"].append(
            deepcopy(document["probe-facts"][0])
        ),
        lambda document: document["probe-facts"][1]["scenarios"].pop(),
    ],
)
def test_evidence_rejects_incomplete_fact_and_scenario_cardinality(
    mutation: Any,
) -> None:
    document = _document()
    mutation(document)

    with pytest.raises(ValueError):
        _admit(document)


def test_incomplete_evidence_can_retain_inventory_without_placeholder_digest() -> (
    None
):
    document = _document()
    document["dependency-results"][2]["result"] = "skipped"
    document["probe-facts"][0].update(
        {
            "result": "incomplete",
            "record-digest": None,
            "artifact-id": None,
            "artifact-digest": None,
            "scenarios": [],
        }
    )
    _downgrade_all_probe_facts(document, "incomplete")
    document["mutation-classification"] = "incomplete"

    evidence = _admit(document)

    assert evidence.probe_facts[0].scenario_inventory == (
        "absent-create-readback",
    )
    assert evidence.probe_facts[0].record_digest is None


def test_schema_is_closed_and_reviewer_never_uses_actor() -> None:
    document = _document()
    document["unexpected"] = "forged"
    with pytest.raises(ValueError, match="unknown closed fields"):
        _admit(document)

    document = _document()
    document["reviewer"] = {
        "login": "github.actor",
        "source": "unavailable-in-job-context",
    }
    with pytest.raises(ValueError, match="requires null"):
        _admit(document)


def test_complete_evidence_accepts_unavailable_reviewer_with_all_recovery_coordinates() -> (
    None
):
    evidence = _admit(_document())

    assert evidence.reviewer is None
    assert evidence.reviewer_source == "unavailable-in-job-context"
    assert evidence.to_document()["recovery"] == _document()["recovery"]


def test_missing_reviewer_alone_does_not_downgrade_complete_evidence() -> None:
    document = _document()
    document["reviewer"]["login"] = None

    assert _admit(document).mutation_classification == "complete"


def test_missing_review_artifact_keeps_successful_probe_suite_artifact_bindings() -> (
    None
):
    document = _document()
    document["recovery"]["artifact-id"] = None
    document["mutation-classification"] = "incomplete"

    evidence = _admit(document)

    assert evidence.mutation_classification == "incomplete"
    for index, fact in enumerate(evidence.probe_facts):
        expected = document["probe-facts"][index]
        assert fact.result == "success"
        assert fact.record_digest == expected["record-digest"]
        assert fact.artifact_id == expected["artifact-id"]
        assert fact.artifact_digest == expected["artifact-digest"]


@pytest.mark.parametrize(
    "path",
    [
        ("reviewer", "source"),
        ("recovery", "workflow-run-id"),
        ("recovery", "environment"),
        ("recovery", "deployment"),
        ("recovery", "job"),
        ("recovery", "artifact-id"),
    ],
)
def test_missing_reviewer_requires_unavailable_source_and_every_recovery_coordinate(
    path: tuple[object, ...],
) -> None:
    document = _document()
    _remove_path(document, path)

    with pytest.raises(ValueError):
        _admit(document)


@pytest.mark.parametrize(
    (
        "scenario_name",
        "probe_result",
        "scenario_classification",
        "executed",
        "started",
        "response_result",
        "post_state",
        "diagnostics",
    ),
    [
        (
            "absent-create-readback",
            "incomplete",
            "incomplete",
            False,
            False,
            "runner-failed-before-mutation",
            "absent",
            ["runner-did-not-prove-mutation-start"],
        ),
        (
            "lost-response",
            "unknown",
            "unknown",
            True,
            True,
            "lost-response",
            "unknown",
            [
                "mutation-may-have-started",
                "human-reconciliation-required",
            ],
        ),
        (
            "identical-race",
            "unknown",
            "unknown",
            True,
            True,
            "timeout",
            "unknown",
            [
                "mutation-may-have-started",
                "human-reconciliation-required",
            ],
        ),
    ],
)
def test_incomplete_and_unknown_scenarios_preserve_authentic_action_facts(
    scenario_name: str,
    probe_result: str,
    scenario_classification: str,
    executed: bool,
    started: bool,
    response_result: str,
    post_state: str,
    diagnostics: list[str],
) -> None:
    document = _document()
    probe = next(
        probe
        for probe, inventory in GOVERNANCE_ACCEPTANCE_PROBE_SCENARIOS.items()
        if scenario_name in inventory
    )
    probe_index = GOVERNANCE_ACCEPTANCE_PROBES.index(probe)
    scenario_index = GOVERNANCE_ACCEPTANCE_PROBE_SCENARIOS[probe].index(
        scenario_name
    )
    fact = document["probe-facts"][probe_index]
    scenario = fact["scenarios"][scenario_index]
    scenario["mutation-classification"] = scenario_classification
    scenario["action"]["executed"] = executed
    scenario["action"]["mutation-started"] = started
    scenario["response"]["result"] = response_result
    scenario["response"]["diagnostics"] = diagnostics
    scenario["post"]["state"] = post_state
    if response_result != "lost-response-exact-after-start":
        scenario.pop("validated-request-proof", None)
    fact["result"] = probe_result
    fact["record-digest"] = FixedAcceptanceSuiteResult(
        suite=probe.removeprefix("probe-"),
        scenarios=tuple(
            FixedCoordinateAcceptanceProbeResult(
                scenario=str(item["scenario"]),
                package_coordinate=str(item["package-coordinate"]),
                tag=str(item["tag"]),
                pre_state=str(item["pre"]["state"]),
                post_state=str(item["post"]["state"]),
                result=str(item["response"]["result"]),
                mutation_classification=str(item["mutation-classification"]),
                action_executed=bool(item["action"]["executed"]),
                mutation_started=bool(item["action"]["mutation-started"]),
                response_identity_digest=str(
                    item["response"]["identity-digest"]
                ),
                content_sha512=str(item["post"]["content-sha512"]),
                diagnostics=tuple(item["response"]["diagnostics"]),
                validated_request_proof=(
                    LOST_RESPONSE_PROOF
                    if item.get("validated-request-proof")
                    == LOST_RESPONSE_PROOF_DOCUMENT
                    else None
                ),
            )
            for item in fact["scenarios"]
        ),
    ).to_document()["record-digest"]
    fact["artifact-id"] = None
    fact["artifact-digest"] = None
    document["mutation-classification"] = probe_result

    evidence = _admit(document)
    preserved = evidence.to_document()["probe-facts"][probe_index]["scenarios"][
        scenario_index
    ]

    assert preserved["mutation-classification"] == scenario_classification
    assert preserved["action"] == {
        "operation": "npm-publish-create-only",
        "executed": executed,
        "mutation-started": started,
    }
    assert preserved["response"]["result"] == response_result
    assert preserved["post"]["state"] == post_state


@pytest.mark.parametrize(
    ("scenario_name", "field", "wrong"),
    [
        ("absent-create-readback", "executed", False),
        ("absent-create-readback", "mutation-started", False),
        ("exact", "executed", True),
        ("exact", "mutation-started", True),
        ("lost-response", "executed", False),
        ("lost-response", "mutation-started", False),
    ],
)
def test_complete_evidence_rejects_runner_startedness_contradictions(
    scenario_name: str,
    field: str,
    wrong: bool,
) -> None:
    document = _document()
    probe = next(
        probe
        for probe, inventory in GOVERNANCE_ACCEPTANCE_PROBE_SCENARIOS.items()
        if scenario_name in inventory
    )
    probe_index = GOVERNANCE_ACCEPTANCE_PROBES.index(probe)
    scenario_index = GOVERNANCE_ACCEPTANCE_PROBE_SCENARIOS[probe].index(
        scenario_name
    )
    document["probe-facts"][probe_index]["scenarios"][scenario_index]["action"][
        field
    ] = wrong

    with pytest.raises(ValueError, match=field):
        _admit(document)


def test_github_actor_cannot_substitute_for_environment_reviewer() -> None:
    document = _document()
    document["reviewer"] = {
        "login": "github.actor",
        "source": "unavailable-in-job-context",
    }

    with pytest.raises(ValueError, match="requires null"):
        _admit(document)


def test_unavailable_reviewer_source_requires_null_reviewer_login() -> None:
    document = _document()
    document["reviewer"]["login"] = "octocat"

    with pytest.raises(ValueError, match="requires null"):
        _admit(document)


@pytest.mark.parametrize("path", CLOSED_SCHEMA_EXTRA_PATHS)
def test_acceptance_evidence_schema_is_closed_at_every_level(
    path: tuple[object, ...],
) -> None:
    document = _document()
    _set_path(document, path, "unexpected")

    with pytest.raises(ValueError, match=r"unknown|closed|unexpected"):
        _admit(document)


@pytest.mark.parametrize("path", REQUIRED_FIELD_PATHS)
def test_acceptance_evidence_rejects_missing_required_fields(
    path: tuple[object, ...],
) -> None:
    document = _document()
    _remove_path(document, path)

    with pytest.raises(ValueError):
        _admit(document)


@pytest.mark.parametrize(
    "mutation",
    [
        lambda document: document["dependency-results"].clear(),
        lambda document: document["dependency-results"].pop(),
        lambda document: document["dependency-results"].append(
            deepcopy(document["dependency-results"][0])
        ),
        lambda document: document["dependency-results"].reverse(),
        lambda document: document["probe-facts"].clear(),
        lambda document: document["probe-facts"].pop(),
        lambda document: document["probe-facts"].append(
            deepcopy(document["probe-facts"][0])
        ),
        lambda document: document["probe-facts"].reverse(),
    ],
)
def test_acceptance_evidence_rejects_empty_or_wrong_length_fact_arrays(
    mutation: Any,
) -> None:
    document = _document()
    mutation(document)

    with pytest.raises(ValueError):
        _admit(document)


@pytest.mark.parametrize(
    ("path", "value"),
    [
        (("schema",), "other"),
        (("purpose",), "publication"),
        (("workflow", "repository"), "other/repo"),
        (("workflow", "ref"), "refs/heads/feature"),
        (("producer",), "other-job"),
        (("release-lineage",), "attempt:1"),
    ],
)
def test_acceptance_evidence_requires_exact_purpose_and_no_release_lineage(
    path: tuple[object, ...],
    value: object,
) -> None:
    document = _document()
    _set_path(document, path, value)

    with pytest.raises(ValueError):
        _admit(document)


@pytest.mark.parametrize(
    "path",
    [
        ("dependency-results", 0),
        ("dependency-results", 1),
        ("dependency-results", 2),
        ("dependency-results", 3),
        ("probe-facts", 0),
        ("probe-facts", 1),
    ],
)
def test_acceptance_evidence_retains_every_dependency_result_and_probe_fact(
    path: tuple[object, ...],
) -> None:
    document = _document()
    admitted = _admit(document).to_document()
    cursor: Any = admitted
    expected: Any = document
    for key in path:
        cursor = cursor[key]
        expected = expected[key]

    assert cursor == expected


@pytest.mark.parametrize(
    "payload",
    [
        b'{"schema":"x","schema":"y"}',
        b'{"purpose":"destination-acceptance", "schema":"workflow-delivery/v3/governance-acceptance-evidence"}',
        b'{"schema":"workflow-delivery/v3/governance-acceptance-evidence","purpose":"destination-acceptance"}\n',
    ],
)
def test_acceptance_evidence_rejects_noncanonical_or_duplicate_json(
    payload: bytes,
) -> None:
    with pytest.raises(ValueError):
        admit_governance_acceptance_evidence(payload)


@pytest.mark.parametrize(
    ("dependency_result", "probe_result", "classification"),
    [
        (
            dependency_result,
            probe_result,
            "unknown"
            if probe_result == "unknown"
            else "incomplete"
            if dependency_result != "success" or probe_result != "success"
            else "complete",
        )
        for dependency_result, probe_result in itertools.product(
            ("success", "failure", "cancelled", "skipped"),
            ("success", "incomplete", "unknown"),
        )
    ],
)
def test_mutation_classification_is_closed_and_consistent(
    dependency_result: str,
    probe_result: str,
    classification: str,
) -> None:
    document = _document()
    document["dependency-results"][0]["result"] = dependency_result
    document["probe-facts"][0]["result"] = probe_result
    document["mutation-classification"] = classification
    if probe_result != "success":
        document["probe-facts"][0].update(
            {
                "record-digest": None,
                "artifact-id": None,
                "artifact-digest": None,
                "scenarios": [],
            }
        )
    if dependency_result != "success":
        _downgrade_all_probe_facts(document, classification)

    assert _admit(document).mutation_classification == classification


@pytest.mark.parametrize(
    ("dependency_result", "probe_result", "classification"),
    [
        (
            dependency_result,
            probe_result,
            "unknown"
            if dependency_result in {"failure", "cancelled"}
            or probe_result == "unknown"
            else "incomplete"
            if dependency_result != "success" or probe_result != "success"
            else "complete",
        )
        for dependency_result, probe_result in itertools.product(
            ("success", "failure", "cancelled", "skipped"),
            ("success", "incomplete", "unknown"),
        )
    ],
)
def test_terminal_fact_matrix_derives_exact_mutation_classification(
    dependency_result: str,
    probe_result: str,
    classification: str,
) -> None:
    document = _document()
    document["dependency-results"][2]["result"] = dependency_result
    document["probe-facts"][0]["result"] = probe_result
    document["mutation-classification"] = classification
    if probe_result != "success":
        document["probe-facts"][0].update(
            {
                "record-digest": None,
                "artifact-id": None,
                "artifact-digest": None,
                "scenarios": [],
            }
        )
    if dependency_result != "success":
        _downgrade_all_probe_facts(document, classification)

    assert _admit(document).mutation_classification == classification


@pytest.mark.parametrize(
    ("dependency_result", "probe_result", "wrong"),
    [
        ("success", "success", "unsupported"),
        ("success", "success", "unknown"),
        ("success", "incomplete", "complete"),
        ("success", "unknown", "incomplete"),
        ("failure", "success", "complete"),
        ("cancelled", "success", "complete"),
        ("skipped", "success", "complete"),
    ],
)
def test_mutation_classification_rejects_inconsistent_or_open_values(
    dependency_result: str,
    probe_result: str,
    wrong: str,
) -> None:
    document = _document()
    document["dependency-results"][0]["result"] = dependency_result
    document["probe-facts"][0]["result"] = probe_result
    document["mutation-classification"] = wrong

    with pytest.raises(ValueError):
        _admit(document)


@pytest.mark.parametrize(
    ("dependency_index", "probe_index", "terminal_result", "classification"),
    [
        (dependency_index, probe_index, terminal_result, classification)
        for dependency_index in (0, 1, 2)
        for probe_index in (0, 1)
        for terminal_result, classification in (("failure", "unknown"),)
    ],
)
def test_mutation_classification_rejects_impossible_upstream_cross_products(
    dependency_index: int,
    probe_index: int,
    terminal_result: str,
    classification: str,
) -> None:
    document = _document()
    document["dependency-results"][dependency_index]["result"] = terminal_result
    document["probe-facts"][probe_index]["result"] = "success"
    document["mutation-classification"] = classification

    with pytest.raises(ValueError):
        _admit(document)


def _refresh_probe_record_digest(
    document: dict[str, Any],
    probe_index: int,
) -> None:
    fact = document["probe-facts"][probe_index]
    fact["record-digest"] = FixedAcceptanceSuiteResult(
        suite=fact["probe"].removeprefix("probe-"),
        scenarios=tuple(
            FixedCoordinateAcceptanceProbeResult(
                scenario=scenario["scenario"],
                package_coordinate=scenario["package-coordinate"],
                tag=scenario["tag"],
                pre_state=scenario["pre"]["state"],
                post_state=scenario["post"]["state"],
                result=scenario["response"]["result"],
                mutation_classification=scenario["mutation-classification"],
                action_executed=scenario["action"]["executed"],
                mutation_started=scenario["action"]["mutation-started"],
                response_identity_digest=scenario["response"][
                    "identity-digest"
                ],
                content_sha512=scenario["post"]["content-sha512"],
                diagnostics=tuple(scenario["response"]["diagnostics"]),
                validated_request_proof=(
                    LOST_RESPONSE_PROOF
                    if scenario.get("validated-request-proof")
                    == LOST_RESPONSE_PROOF_DOCUMENT
                    else NORMAL_CREATE_PROOF
                    if scenario.get("validated-request-proof")
                    == NORMAL_CREATE_PROOF_DOCUMENT
                    else None
                ),
                runner_diagnostic=(
                    AcceptanceRunnerDiagnostic(
                        exit_classification=scenario["runner-diagnostic"][
                            "exit-classification"
                        ],
                        upstream_status=scenario["runner-diagnostic"][
                            "upstream-status"
                        ],
                        exception_category=scenario["runner-diagnostic"][
                            "exception-category"
                        ],
                        request_correlation_digest=scenario[
                            "runner-diagnostic"
                        ]["request-correlation-digest"],
                    )
                    if scenario.get("runner-diagnostic") is not None
                    else None
                ),
            )
            for scenario in fact["scenarios"]
        ),
    ).to_document()["record-digest"]


def _refresh_probe_record_digest_unchecked(
    document: dict[str, Any],
    probe_index: int,
) -> None:
    fact = document["probe-facts"][probe_index]
    classifications = {
        scenario["mutation-classification"] for scenario in fact["scenarios"]
    }
    mutation_classification = (
        "unknown"
        if "unknown" in classifications
        else "incomplete"
        if "incomplete" in classifications
        else "complete"
    )
    suite_document = {
        "schema": "workflow-delivery/v3/fixed-acceptance-suite",
        "suite": fact["probe"].removeprefix("probe-"),
        "scenario-inventory": fact["scenario-inventory"],
        "scenarios": fact["scenarios"],
        "mutation-classification": mutation_classification,
        "result": (
            "success"
            if mutation_classification == "complete"
            else mutation_classification
        ),
    }
    fact["record-digest"] = canonical_sha256(suite_document)


def _retry_2_document() -> dict[str, Any]:
    document = _document()
    environment = "workflow-delivery-v3-buddy-smoke-acceptance-retry-2"
    document["workflow"]["path"] = (
        ".github/workflows/"
        "workflow-delivery-v3-buddy-smoke-acceptance-retry-2.yml"
    )
    document["target-sha"] = "0" * 40
    document["package-coordinate"] = (
        "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.5"
    )
    document["confirmation-digest"] = (
        "sha256:"
        "1215f9d01cd343462c3f826ba67ebee86b6f6142b7fcfe5630572a5a808314f8"
    )
    document["environment"] = environment
    document["recovery"]["environment"] = environment
    document["recovery"]["artifact-id"] = None
    for index, dependency in enumerate(document["dependency-results"]):
        dependency["result"] = "failure" if index == 0 else "skipped"
    document["mutation-classification"] = "incomplete"
    for fact in document["probe-facts"]:
        fact["result"] = "incomplete"
        fact["record-digest"] = None
        fact["artifact-id"] = None
        fact["artifact-digest"] = None
        fact["scenarios"] = []
    return document


def test_retry_2_profile_admits_only_exact_rejected_dispatch_sentinel_evidence() -> (
    None
):
    document = _retry_2_document()

    admitted = _admit(document)

    assert admitted.to_document() == document
    assert admitted.target_sha == "0" * 40


@pytest.mark.parametrize(
    ("path", "value", "message"),
    [
        (
            ("workflow", "path"),
            ".github/workflows/workflow-delivery-v3-buddy-smoke-acceptance.yml",
            "workflow.path",
        ),
        (
            ("environment",),
            ENVIRONMENT,
            "environment",
        ),
        (
            ("confirmation-digest",),
            LEGACY_CONFIRMATION_DIGEST,
            "confirmation-digest",
        ),
        (
            ("target-sha",),
            "c" * 40,
            "target-sha",
        ),
    ],
)
def test_retry_2_profile_rejects_cross_profile_substitution(
    path: tuple[str, ...],
    value: object,
    message: str,
) -> None:
    document = _retry_2_document()
    _set_path(document, path, value)

    with pytest.raises(ValueError, match=message):
        _admit(document)


def test_legacy_profile_rejects_unreviewed_target_and_confirmation() -> None:
    document = _document()
    document["target-sha"] = "c" * 40
    with pytest.raises(ValueError, match="target-sha"):
        _admit(document)

    document = _document()
    document["confirmation-digest"] = SHA256_A
    with pytest.raises(ValueError, match="confirmation-digest"):
        _admit(document)


@pytest.mark.parametrize(
    "mutation",
    [
        "unknown-classification",
        "successful-validation",
        "review-artifact",
        "reviewer-attribution",
        "probe-record",
    ],
)
def test_zero_target_rejects_non_rejected_dispatch_evidence(
    mutation: str,
) -> None:
    document = _retry_2_document()
    if mutation == "unknown-classification":
        document["mutation-classification"] = "unknown"
    elif mutation == "successful-validation":
        document["dependency-results"][0]["result"] = "success"
    elif mutation == "review-artifact":
        document["recovery"]["artifact-id"] = 701
    elif mutation == "reviewer-attribution":
        document["reviewer"] = {
            "login": "octocat",
            "source": "on-demand-read-only-inspection",
        }
    else:
        document["probe-facts"][0] = _probe_fact("probe-absent-create-readback")

    with pytest.raises(ValueError):
        _admit(document)


@pytest.mark.parametrize(
    ("dependency_job", "terminal_result", "classification"),
    [
        (dependency_job, terminal_result, classification)
        for dependency_job in GOVERNANCE_ACCEPTANCE_DEPENDENCIES
        for terminal_result, classification in (
            ("failure", "unknown"),
            ("cancelled", "unknown"),
            ("skipped", "incomplete"),
        )
        if dependency_job != "probe-exact-and-conflict"
        or terminal_result == "skipped"
    ],
)
def test_evidence_admission_rejects_impossible_suites_after_non_success_dependency(
    dependency_job: str,
    terminal_result: str,
    classification: str,
) -> None:
    document = _document()
    dependency_index = GOVERNANCE_ACCEPTANCE_DEPENDENCIES.index(dependency_job)
    document["dependency-results"][dependency_index]["result"] = terminal_result
    document["mutation-classification"] = classification

    with pytest.raises(ValueError) as exc_info:
        _admit(document)

    message = str(exc_info.value)
    assert dependency_job in message
    assert terminal_result in message
    assert "retain suite records" in message


@pytest.mark.parametrize("terminal_result", ["failure", "cancelled"])
def test_evidence_admission_retains_complete_suite_after_late_probe_end(
    terminal_result: str,
) -> None:
    document = _document()
    dependency_index = GOVERNANCE_ACCEPTANCE_DEPENDENCIES.index(
        "probe-exact-and-conflict"
    )
    document["dependency-results"][dependency_index]["result"] = terminal_result
    document["mutation-classification"] = "unknown"

    evidence = _admit(document)

    assert evidence.mutation_classification == "unknown"
    assert evidence.probe_facts[1].result == "success"
    assert (
        evidence.probe_facts[1].record_digest
        == (document["probe-facts"][1]["record-digest"])
    )
    assert (
        evidence.probe_facts[1].artifact_id
        == (document["probe-facts"][1]["artifact-id"])
    )
    assert (
        evidence.probe_facts[1].artifact_digest
        == (document["probe-facts"][1]["artifact-digest"])
    )
    assert evidence.probe_facts[1].scenarios


@pytest.mark.parametrize(
    ("dependency_job", "terminal_result"),
    [
        (dependency_job, terminal_result)
        for dependency_job in GOVERNANCE_ACCEPTANCE_PROBES
        for terminal_result in ("failure", "cancelled")
    ],
)
def test_failure_or_cancelled_dependency_with_possibly_started_evidence_is_unknown(
    dependency_job: str,
    terminal_result: str,
) -> None:
    document = _document()
    dependency_index = GOVERNANCE_ACCEPTANCE_DEPENDENCIES.index(dependency_job)
    document["dependency-results"][dependency_index]["result"] = terminal_result

    possibly_started = document["probe-facts"][0]["scenarios"][0]
    possibly_started["mutation-classification"] = "unknown"
    possibly_started["action"]["executed"] = True
    possibly_started["action"]["mutation-started"] = True
    possibly_started["response"]["result"] = "timeout"
    possibly_started["response"]["diagnostics"] = [
        "mutation-may-have-started",
        "human-reconciliation-required",
    ]
    possibly_started["post"]["state"] = "unknown"
    document["probe-facts"][0]["result"] = "unknown"
    document["probe-facts"][0]["artifact-id"] = None
    document["probe-facts"][0]["artifact-digest"] = None
    _refresh_probe_record_digest(document, 0)

    document["probe-facts"][1].update(
        {
            "result": "unknown",
            "record-digest": None,
            "artifact-id": None,
            "artifact-digest": None,
            "scenarios": [],
        }
    )
    document["mutation-classification"] = "unknown"

    evidence = _admit(document)
    admitted = evidence.to_document()

    assert evidence.mutation_classification == "unknown"
    assert admitted["dependency-results"][dependency_index] == {
        "job": dependency_job,
        "result": terminal_result,
    }
    assert [fact["result"] for fact in admitted["probe-facts"]] == [
        "unknown",
        "unknown",
    ]
    assert admitted["probe-facts"][0]["scenarios"][0]["action"] == {
        "operation": "npm-publish-create-only",
        "executed": True,
        "mutation-started": True,
    }


@pytest.mark.parametrize(
    "dependency_job",
    GOVERNANCE_ACCEPTANCE_DEPENDENCIES,
)
def test_skipped_dependency_with_not_started_evidence_is_incomplete(
    dependency_job: str,
) -> None:
    document = _document()
    dependency_index = GOVERNANCE_ACCEPTANCE_DEPENDENCIES.index(dependency_job)
    document["dependency-results"][dependency_index]["result"] = "skipped"
    for fact in document["probe-facts"]:
        fact.update(
            {
                "result": "incomplete",
                "record-digest": None,
                "artifact-id": None,
                "artifact-digest": None,
                "scenarios": [],
            }
        )
    document["mutation-classification"] = "incomplete"

    evidence = _admit(document)
    admitted = evidence.to_document()

    assert evidence.mutation_classification == "incomplete"
    assert admitted["dependency-results"][dependency_index] == {
        "job": dependency_job,
        "result": "skipped",
    }
    assert [fact["result"] for fact in admitted["probe-facts"]] == [
        "incomplete",
        "incomplete",
    ]
    assert all(not fact["scenarios"] for fact in admitted["probe-facts"])


@pytest.mark.parametrize(
    ("executed", "mutation_started"),
    [(True, False), (False, True), (True, True)],
)
def test_pre_request_failure_rejects_startedness_contradictions(
    executed: bool,
    mutation_started: bool,
) -> None:
    document = _document()
    fact = document["probe-facts"][0]
    scenario = fact["scenarios"][0]
    scenario["mutation-classification"] = "incomplete"
    scenario["action"]["executed"] = executed
    scenario["action"]["mutation-started"] = mutation_started
    scenario["response"]["result"] = "runner-failed-before-mutation"
    scenario["response"]["diagnostics"] = [
        "runner-did-not-prove-mutation-start"
    ]
    scenario["post"]["state"] = "absent"
    fact["result"] = "incomplete"
    fact["artifact-id"] = None
    fact["artifact-digest"] = None
    _refresh_probe_record_digest(document, 0)
    document["mutation-classification"] = "incomplete"

    with pytest.raises(ValueError, match=r"action.*started|startedness"):
        _admit(document)


@pytest.mark.parametrize(
    ("mutation_started", "response_result"),
    [
        (False, "runner-failed-after-action-start"),
        (True, "runner-failed-after-mutation-start"),
    ],
)
def test_post_start_runner_failure_is_admitted_as_incomplete(
    mutation_started: bool,
    response_result: str,
) -> None:
    document = _document()
    fact = document["probe-facts"][0]
    scenario = fact["scenarios"][0]
    scenario["mutation-classification"] = "incomplete"
    scenario["action"]["mutation-started"] = mutation_started
    scenario["response"]["result"] = response_result
    scenario["response"]["diagnostics"] = [
        "runner-did-not-prove-controlled-outcome"
    ]
    fact["result"] = "incomplete"
    _refresh_probe_record_digest(document, 0)
    document["mutation-classification"] = "incomplete"

    evidence = _admit(document)

    assert evidence.mutation_classification == "incomplete"
    admitted_scenario = evidence.to_document()["probe-facts"][0]["scenarios"][0]
    assert admitted_scenario["response"]["result"] == response_result
    assert admitted_scenario["action"] == {
        "executed": True,
        "mutation-started": mutation_started,
        "operation": "npm-publish-create-only",
    }


@pytest.mark.parametrize(
    ("response_result", "executed", "mutation_started"),
    [
        ("runner-failed-after-action-start", False, False),
        ("runner-failed-after-action-start", False, True),
        ("runner-failed-after-action-start", True, True),
        ("runner-failed-after-mutation-start", False, False),
        ("runner-failed-after-mutation-start", False, True),
        ("runner-failed-after-mutation-start", True, False),
    ],
)
def test_post_start_runner_failure_rejects_startedness_contradictions(
    response_result: str,
    executed: bool,
    mutation_started: bool,
) -> None:
    document = _document()
    fact = document["probe-facts"][0]
    scenario = fact["scenarios"][0]
    scenario["mutation-classification"] = "incomplete"
    scenario["action"]["executed"] = executed
    scenario["action"]["mutation-started"] = mutation_started
    scenario["response"]["result"] = response_result
    scenario["response"]["diagnostics"] = [
        "runner-did-not-prove-controlled-outcome"
    ]
    fact["result"] = "incomplete"
    _refresh_probe_record_digest(document, 0)
    document["mutation-classification"] = "incomplete"

    with pytest.raises(ValueError, match=r"startedness contradict"):
        _admit(document)


@pytest.mark.parametrize(
    ("executed", "mutation_started", "admitted"),
    [
        (False, False, True),
        (True, False, True),
        (False, True, False),
        (True, True, False),
    ],
)
def test_malformed_pre_mutation_result_constrains_mutation_startedness(
    executed: bool,
    mutation_started: bool,
    admitted: bool,
) -> None:
    document = _document()
    fact = document["probe-facts"][0]
    scenario = fact["scenarios"][0]
    scenario["mutation-classification"] = "incomplete"
    scenario["action"]["executed"] = executed
    scenario["action"]["mutation-started"] = mutation_started
    scenario["response"]["result"] = "runner-malformed-before-mutation"
    scenario["response"]["diagnostics"] = [
        "runner-action-facts-not-fully-admitted"
    ]
    fact["result"] = "incomplete"
    _refresh_probe_record_digest(document, 0)
    document["mutation-classification"] = "incomplete"

    if admitted:
        assert _admit(document).mutation_classification == "incomplete"
    else:
        with pytest.raises(ValueError, match=r"startedness contradict"):
            _admit(document)


def test_complete_scenario_rejects_runner_failure_when_artifact_is_missing() -> (
    None
):
    document = _document()
    fact = document["probe-facts"][0]
    scenario = fact["scenarios"][0]
    scenario["action"]["executed"] = True
    scenario["action"]["mutation-started"] = True
    scenario["response"]["result"] = "runner-failed-after-mutation-start"
    scenario["response"]["diagnostics"] = [
        "runner-did-not-prove-controlled-outcome"
    ]
    fact["result"] = "incomplete"
    fact["artifact-id"] = None
    fact["artifact-digest"] = None
    _refresh_probe_record_digest(document, 0)
    document["mutation-classification"] = "incomplete"

    with pytest.raises(ValueError, match=r"complete scenario semantics"):
        _admit(document)


@pytest.mark.parametrize(
    ("executed", "mutation_started"),
    [(False, False), (True, False), (False, True)],
)
def test_post_qualified_request_timeout_rejects_startedness_contradictions(
    executed: bool,
    mutation_started: bool,
) -> None:
    document = _document()
    probe_index = GOVERNANCE_ACCEPTANCE_PROBES.index("probe-exact-and-conflict")
    scenario_index = GOVERNANCE_ACCEPTANCE_PROBE_SCENARIOS[
        "probe-exact-and-conflict"
    ].index("lost-response")
    fact = document["probe-facts"][probe_index]
    scenario = fact["scenarios"][scenario_index]
    scenario["mutation-classification"] = "unknown"
    scenario["action"]["executed"] = executed
    scenario["action"]["mutation-started"] = mutation_started
    scenario["response"]["result"] = "timeout"
    scenario["response"]["diagnostics"] = [
        "mutation-may-have-started",
        "human-reconciliation-required",
    ]
    scenario["post"]["state"] = "unknown"
    scenario.pop("validated-request-proof", None)
    fact["result"] = "unknown"
    fact["artifact-id"] = None
    fact["artifact-digest"] = None
    _refresh_probe_record_digest(document, probe_index)
    document["mutation-classification"] = "unknown"

    with pytest.raises(ValueError, match=r"action.*started|startedness"):
        _admit(document)


def test_complete_acceptance_evidence_rejects_zero_target_sha() -> None:
    document = _document()
    document["target-sha"] = "0" * 40

    with pytest.raises(ValueError, match="zero target-sha"):
        _admit(document)


def test_complete_acceptance_evidence_rejects_zero_workflow_sha() -> None:
    document = _document()
    document["workflow"]["sha"] = "0" * 40

    with pytest.raises(ValueError, match=r"non-zero workflow\.sha"):
        _admit(document)


def test_incomplete_acceptance_evidence_preserves_zero_workflow_sha_sentinel() -> (
    None
):
    document = _document()
    document["recovery"]["artifact-id"] = None
    document["mutation-classification"] = "incomplete"
    document["workflow"]["sha"] = "0" * 40

    evidence = _admit(document)

    assert evidence.mutation_classification == "incomplete"
    assert evidence.to_document() == document


def _retry_3_document() -> dict[str, Any]:
    document = _retry_2_document()
    environment = "workflow-delivery-v3-buddy-smoke-acceptance-retry-3"
    document["workflow"]["path"] = (
        ".github/workflows/"
        "workflow-delivery-v3-buddy-smoke-acceptance-retry-3.yml"
    )
    document["package-coordinate"] = (
        "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.9"
    )
    document["confirmation-digest"] = (
        "sha256:"
        "33e59948941f5f1111d5017ab80dd33c90dd2ac8d1a17203e7f7382a8c5b2c72"
    )
    document["environment"] = environment
    document["recovery"]["environment"] = environment
    return document


def test_retry_3_finalized_profile_preserves_zero_sentinel_rejected_dispatch() -> (
    None
):
    document = _retry_3_document()
    profile = next(
        profile
        for profile in governance_module._GOVERNANCE_ACCEPTANCE_PROFILES
        if profile.package_coordinate
        == GOVERNANCE_RETRY_3_ACCEPTANCE_PACKAGE_COORDINATE
    )

    admitted = _admit(document)

    assert profile.workflow_path == GOVERNANCE_RETRY_3_ACCEPTANCE_WORKFLOW_PATH
    assert profile.environment == GOVERNANCE_RETRY_3_ACCEPTANCE_ENVIRONMENT
    assert profile.target_sha == RETRY_3_TARGET_SHA
    assert profile.confirmation_digest == document["confirmation-digest"]
    assert (
        profile.coordinates()
        == GOVERNANCE_RETRY_3_ACCEPTANCE_SCENARIO_COORDINATES
    )
    assert admitted.target_sha == "0" * 40
    assert (
        admitted.package_coordinate
        == GOVERNANCE_RETRY_3_ACCEPTANCE_PACKAGE_COORDINATE
    )
    assert GOVERNANCE_RETRY_3_ACCEPTANCE_PACKAGE_COORDINATE == (
        "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.9"
    )
    assert (
        document["workflow"]["path"]
        == GOVERNANCE_RETRY_3_ACCEPTANCE_WORKFLOW_PATH
    )
    assert GOVERNANCE_RETRY_3_ACCEPTANCE_WORKFLOW_PATH == (
        ".github/workflows/"
        "workflow-delivery-v3-buddy-smoke-acceptance-retry-3.yml"
    )
    assert (
        document["environment"]
        == document["recovery"]["environment"]
        == GOVERNANCE_RETRY_3_ACCEPTANCE_ENVIRONMENT
    )
    assert GOVERNANCE_RETRY_3_ACCEPTANCE_ENVIRONMENT == (
        "workflow-delivery-v3-buddy-smoke-acceptance-retry-3"
    )
    assert document["confirmation-digest"] == (
        "sha256:"
        "33e59948941f5f1111d5017ab80dd33c90dd2ac8d1a17203e7f7382a8c5b2c72"
    )


def test_retry_3_complete_evidence_admits_finalized_profile_round_trip() -> (
    None
):
    absent_proof = ValidatedAcceptanceRequestProof.from_validated_exchange(
        raw_request=b'{"_id":"retry-3-absent"}',
        tarball=b"retry-3-absent-tarball",
        package_coordinate=GOVERNANCE_RETRY_3_ACCEPTANCE_PACKAGE_COORDINATE,
        tag="wdv3-acceptance-9",
        upstream_status=201,
        selected_headers={"Content-Type": "application/json", "ETag": '"r3a"'},
        response_body=b'{"ok":true}',
    )
    lost_coordinate, lost_tag = (
        GOVERNANCE_RETRY_3_ACCEPTANCE_SCENARIO_COORDINATES["lost-response"]
    )
    lost_proof = ValidatedAcceptanceRequestProof.from_validated_exchange(
        raw_request=b'{"_id":"retry-3-lost"}',
        tarball=b"retry-3-lost-tarball",
        package_coordinate=lost_coordinate,
        tag=lost_tag,
        upstream_status=201,
        selected_headers={"Content-Type": "application/json", "ETag": '"r3l"'},
        response_body=b'{"ok":true}',
    )
    absent_suite = FixedAcceptanceSuiteResult(
        suite="absent-create-readback",
        scenarios=(
            FixedCoordinateAcceptanceProbeResult(
                scenario="absent-create-readback",
                package_coordinate=(
                    GOVERNANCE_RETRY_3_ACCEPTANCE_PACKAGE_COORDINATE
                ),
                tag="wdv3-acceptance-9",
                pre_state="absent",
                post_state="exact",
                result="protocol-confirmed",
                mutation_classification="complete",
                action_executed=True,
                mutation_started=True,
                response_identity_digest=(
                    absent_proof.response_identity_digest
                ),
                content_sha512=absent_proof.tarball_sha512,
                diagnostics=(),
                validated_request_proof=absent_proof,
            ),
        ),
    )
    scenario_facts = (
        (
            "exact",
            "exact",
            "exact",
            "exact-no-mutation",
            False,
            False,
            (),
            None,
        ),
        (
            "identical-race",
            "absent",
            "exact",
            "identical-race-exact",
            True,
            True,
            ("identical-race-exact",),
            None,
        ),
        (
            "differing-race",
            "absent",
            "conflicting",
            "differing-race-conflict",
            True,
            True,
            ("conflicting-remote-bytes-or-tag",),
            None,
        ),
        (
            "lost-response",
            "absent",
            "exact",
            "lost-response-exact-after-start",
            True,
            True,
            ("mutation-started-and-readback-exact",),
            lost_proof,
        ),
    )
    conflict_suite = FixedAcceptanceSuiteResult(
        suite="exact-and-conflict",
        scenarios=tuple(
            FixedCoordinateAcceptanceProbeResult(
                scenario=scenario,
                package_coordinate=(
                    GOVERNANCE_RETRY_3_ACCEPTANCE_SCENARIO_COORDINATES[
                        scenario
                    ][0]
                ),
                tag=GOVERNANCE_RETRY_3_ACCEPTANCE_SCENARIO_COORDINATES[
                    scenario
                ][1],
                pre_state=pre_state,
                post_state=post_state,
                result=result,
                mutation_classification="complete",
                action_executed=action_executed,
                mutation_started=mutation_started,
                response_identity_digest=(
                    proof.response_identity_digest if proof else SHA256_B
                ),
                content_sha512=(proof.tarball_sha512 if proof else SHA512_A),
                diagnostics=diagnostics,
                validated_request_proof=proof,
            )
            for (
                scenario,
                pre_state,
                post_state,
                result,
                action_executed,
                mutation_started,
                diagnostics,
                proof,
            ) in scenario_facts
        ),
    )
    document = _document()
    document["workflow"]["path"] = GOVERNANCE_RETRY_3_ACCEPTANCE_WORKFLOW_PATH
    document["target-sha"] = RETRY_3_TARGET_SHA
    document["package-coordinate"] = (
        GOVERNANCE_RETRY_3_ACCEPTANCE_PACKAGE_COORDINATE
    )
    document["confirmation-digest"] = (
        "sha256:"
        "33e59948941f5f1111d5017ab80dd33c90dd2ac8d1a17203e7f7382a8c5b2c72"
    )
    document["environment"] = GOVERNANCE_RETRY_3_ACCEPTANCE_ENVIRONMENT
    document["recovery"]["environment"] = (
        GOVERNANCE_RETRY_3_ACCEPTANCE_ENVIRONMENT
    )
    for fact, suite in zip(
        document["probe-facts"],
        (absent_suite, conflict_suite),
        strict=True,
    ):
        suite_document = suite.to_document()
        fact["record-digest"] = suite_document["record-digest"]
        fact["scenarios"] = suite_document["scenarios"]

    raw = canonicalize(document)
    admitted = admit_governance_acceptance_evidence(raw)
    admitted_document = admitted.to_document()

    assert admitted.target_sha == RETRY_3_TARGET_SHA
    assert raw == canonicalize(admitted_document)
    assert [
        (scenario["package-coordinate"], scenario["tag"])
        for fact in admitted.probe_facts
        for scenario in fact.scenarios
    ] == [
        GOVERNANCE_RETRY_3_ACCEPTANCE_SCENARIO_COORDINATES[scenario]
        for scenario in GOVERNANCE_ACCEPTANCE_SCENARIOS
    ]
    assert GOVERNANCE_RETRY_3_ACCEPTANCE_SCENARIO_COORDINATES == {
        "absent-create-readback": (
            "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.9",
            "wdv3-acceptance-9",
        ),
        "exact": (
            "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.9",
            "wdv3-acceptance-9",
        ),
        "identical-race": (
            "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.10",
            "wdv3-acceptance-10",
        ),
        "differing-race": (
            "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.11",
            "wdv3-acceptance-11",
        ),
        "lost-response": (
            "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.12",
            "wdv3-acceptance-12",
        ),
    }
    assert admitted.to_document() == document


@pytest.mark.parametrize(
    ("path", "value", "message"),
    [
        (
            ("workflow", "path"),
            (
                ".github/workflows/"
                "workflow-delivery-v3-buddy-smoke-acceptance-retry-2.yml"
            ),
            "workflow.path",
        ),
        (
            ("environment",),
            "workflow-delivery-v3-buddy-smoke-acceptance-retry-2",
            "environment",
        ),
        (
            ("recovery", "environment"),
            "workflow-delivery-v3-buddy-smoke-acceptance-retry-2",
            "recovery.environment",
        ),
        (
            ("confirmation-digest",),
            (
                "sha256:"
                "1215f9d01cd343462c3f826ba67ebee86b6f6142b7fcfe5630572a5a808314f8"
            ),
            "confirmation-digest",
        ),
        (("target-sha",), "c" * 40, "target-sha"),
    ],
)
def test_retry_3_profile_rejects_cross_profile_substitution(
    path: tuple[str, ...],
    value: object,
    message: str,
) -> None:
    document = _retry_3_document()
    _set_path(document, path, value)

    with pytest.raises(ValueError, match=message):
        _admit(document)


@pytest.mark.parametrize(
    ("field", "value"),
    [
        (
            "package-coordinate",
            "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.10",
        ),
        ("tag", "wdv3-acceptance-10"),
    ],
)
def test_retry_3_profile_rejects_scenario_coordinate_or_tag_mismatch(
    field: str,
    value: str,
) -> None:
    document = _retry_3_document()
    fact = _probe_fact("probe-absent-create-readback")
    scenario = fact["scenarios"][0]
    scenario["package-coordinate"] = (
        "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.9"
    )
    scenario["tag"] = "wdv3-acceptance-9"
    scenario[field] = value
    document["probe-facts"][0] = fact
    _refresh_probe_record_digest_unchecked(document, 0)

    with pytest.raises(ValueError, match=field):
        _admit(document)


def test_retry_3_profile_preserves_retry_1_and_retry_2_admission() -> None:
    retry_1 = _document()
    retry_2 = _retry_2_document()

    admitted_retry_1 = _admit(retry_1)
    admitted_retry_2 = _admit(retry_2)

    assert admitted_retry_1.to_document() == retry_1
    assert admitted_retry_2.to_document() == retry_2
    assert (
        retry_1["probe-facts"][0]["record-digest"]
        == HISTORICAL_ABSENT_CREATE_READBACK_RECORD_DIGEST
    )
    assert (
        retry_1["probe-facts"][1]["record-digest"]
        == HISTORICAL_EXACT_AND_CONFLICT_RECORD_DIGEST
    )
