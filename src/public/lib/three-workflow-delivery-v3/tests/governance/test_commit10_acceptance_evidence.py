"""Commit-10 Governance Acceptance Evidence contract scenarios."""

# ruff: noqa: D103, E501, FBT001, PLR0913, PLR0917, PT011, SLF001

from __future__ import annotations

import itertools
from copy import deepcopy
from typing import Any, cast

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
        "exception-category": "RuntimeError",
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


def test_transport_diagnostic_cannot_bind_validated_response_proof() -> None:
    document = _document()
    scenario = document["probe-facts"][1]["scenarios"][3]
    scenario["runner-diagnostic"] = {
        "exit-classification": "runner-failed-after-mutation-start",
        "upstream-status": None,
        "exception-category": "TimeoutError",
        "request-correlation-digest": LOST_RESPONSE_PROOF.request_digest,
    }
    _refresh_probe_record_digest_unchecked(document, 1)

    with pytest.raises(ValueError, match="does not bind"):
        _admit(document)


def test_runner_diagnostic_request_facts_require_non_authoritative_result() -> (
    None
):
    document = _document()
    scenario = document["probe-facts"][0]["scenarios"][0]
    scenario["runner-diagnostic"] = {
        "exit-classification": "runner-failed-after-mutation-start",
        "upstream-status": 201,
        "exception-category": None,
        "request-correlation-digest": NORMAL_CREATE_PROOF.request_digest,
    }
    _refresh_probe_record_digest_unchecked(document, 0)

    with pytest.raises(ValueError, match="non-authoritative"):
        _admit(document)


@pytest.mark.parametrize(
    "exception_category",
    ["TimeoutError", "OSError", "RuntimeError", "ValueError"],
)
def test_governance_preserves_historical_local_runner_diagnostic_without_request(
    exception_category: str,
) -> None:
    diagnostic = {
        "exit-classification": "runner-failed-after-mutation-start",
        "upstream-status": None,
        "exception-category": exception_category,
        "request-correlation-digest": None,
    }
    document = _diagnostic_only_incomplete_document(diagnostic)

    admitted = _admit(document).to_document()

    assert admitted == document
    assert (
        admitted["probe-facts"][0]["scenarios"][0]["runner-diagnostic"]
        == diagnostic
    )


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
    lost_scenario = document["probe-facts"][1]["scenarios"][3]
    lost_proof = lost_scenario["validated-request-proof"]
    assert (
        lost_scenario["post"]["content-sha512"] == lost_proof["tarball-sha512"]
    )
    lost_scenario["post"]["content-sha512"] = SHA512_A
    assert (
        lost_scenario["post"]["content-sha512"] != lost_proof["tarball-sha512"]
    )
    _refresh_probe_record_digest_unchecked(document, 1)

    with pytest.raises(ValueError, match="tarball-sha512"):
        _admit(document)


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


def _diagnostic_only_incomplete_document(
    diagnostic: dict[str, Any],
) -> dict[str, Any]:
    document = _document()
    fact = document["probe-facts"][0]
    scenario = fact["scenarios"][0]
    scenario["mutation-classification"] = "incomplete"
    scenario["response"]["result"] = "runner-failed-after-mutation-start"
    scenario["response"]["diagnostics"] = [
        "runner-did-not-prove-controlled-outcome"
    ]
    scenario["runner-diagnostic"] = deepcopy(diagnostic)
    scenario.pop("validated-request-proof", None)
    fact["result"] = "incomplete"
    document["mutation-classification"] = "incomplete"
    _refresh_probe_record_digest_unchecked(document, 0)
    return document


@pytest.mark.parametrize(
    ("upstream_status", "exception_category"),
    [
        (200, None),
        (201, None),
        (202, None),
        (409, None),
        (500, None),
        (None, "TimeoutError"),
        (None, "OSError"),
        (None, "HTTPException"),
    ],
    ids=[
        "status-200",
        "status-201",
        "status-202",
        "status-409",
        "status-500",
        "transport-timeout",
        "transport-os-error",
        "transport-http-exception",
    ],
)
def test_governance_admits_and_round_trips_canonical_upstream_diagnostic(
    upstream_status: int | None,
    exception_category: str | None,
) -> None:
    diagnostic = {
        "exit-classification": "runner-failed-after-mutation-start",
        "upstream-status": upstream_status,
        "exception-category": exception_category,
        "request-correlation-digest": SHA256_A,
    }
    document = _diagnostic_only_incomplete_document(diagnostic)

    evidence = None
    rejection = None
    try:
        evidence = _admit(document)
    except (TypeError, ValueError) as error:
        rejection = str(error)
    if rejection is not None:
        pytest.fail(
            f"canonical upstream diagnostic was rejected: {rejection}",
            pytrace=False,
        )
    assert evidence is not None
    admitted_document = evidence.to_document()
    admitted_scenario = admitted_document["probe-facts"][0]["scenarios"][0]

    assert admitted_document == document
    assert evidence.mutation_classification == "incomplete"
    assert admitted_scenario["runner-diagnostic"] == diagnostic
    assert set(admitted_scenario["runner-diagnostic"]) == {
        "exit-classification",
        "upstream-status",
        "exception-category",
        "request-correlation-digest",
    }
    assert admitted_scenario["action"] == {
        "operation": "npm-publish-create-only",
        "executed": True,
        "mutation-started": True,
    }
    assert admitted_scenario["mutation-classification"] == "incomplete"
    assert admitted_scenario["response"] == {
        "result": "runner-failed-after-mutation-start",
        "identity-digest": SHA256_B,
        "diagnostics": ["runner-did-not-prove-controlled-outcome"],
    }
    assert "validated-request-proof" not in admitted_scenario

    boundary_probe_status = 200
    if upstream_status == boundary_probe_status:
        for boundary_status in (100, 599):
            boundary_diagnostic = {
                **diagnostic,
                "upstream-status": boundary_status,
            }
            boundary_document = _diagnostic_only_incomplete_document(
                boundary_diagnostic
            )
            boundary_admitted = _admit(boundary_document).to_document()
            assert boundary_admitted == boundary_document
            assert (
                boundary_admitted["probe-facts"][0]["scenarios"][0][
                    "runner-diagnostic"
                ]
                == boundary_diagnostic
            )


@pytest.mark.parametrize(
    (
        "exit_classification",
        "executed",
        "mutation_started",
        "upstream_status",
        "exception_category",
    ),
    [
        ("runner-failed-before-mutation", False, False, 500, None),
        (
            "runner-failed-after-action-start",
            True,
            False,
            None,
            "TimeoutError",
        ),
        ("runner-malformed-before-mutation", False, False, 500, None),
    ],
    ids=[
        "status-before-mutation",
        "transport-after-action-start",
        "status-malformed-before-mutation",
    ],
)
def test_governance_rejects_request_bound_diagnostic_before_mutation_started(
    exit_classification: str,
    executed: bool,
    mutation_started: bool,
    upstream_status: int | None,
    exception_category: str | None,
) -> None:
    diagnostic = {
        "exit-classification": exit_classification,
        "upstream-status": upstream_status,
        "exception-category": exception_category,
        "request-correlation-digest": SHA256_A,
    }
    document = _diagnostic_only_incomplete_document(diagnostic)
    fact = document["probe-facts"][0]
    scenario = fact["scenarios"][0]
    scenario["action"]["executed"] = executed
    scenario["action"]["mutation-started"] = mutation_started
    scenario["response"]["result"] = exit_classification
    if exit_classification == "runner-failed-before-mutation":
        scenario["post"]["state"] = "absent"
        scenario["response"]["diagnostics"] = [
            "runner-did-not-prove-mutation-start"
        ]
        fact["artifact-id"] = None
        fact["artifact-digest"] = None
    elif exit_classification == "runner-malformed-before-mutation":
        scenario["response"]["diagnostics"] = [
            "runner-action-facts-not-fully-admitted"
        ]
    _refresh_probe_record_digest_unchecked(document, 0)

    with pytest.raises(ValueError, match="runner-diagnostic"):
        _admit(document)


@pytest.mark.parametrize(
    "malformation",
    [
        "status-below-range",
        "status-above-range",
        "status-bool",
        "status-without-request",
        "transport-without-request",
        "local-runtime-error-with-request",
        "local-value-error-with-request",
        "empty-diagnostic",
        "protocol-confirmed-without-proof",
        "status-and-transport",
        "request-without-status-or-transport",
        "unknown-transport-category",
        "malformed-request-digest",
        "unknown-field",
    ],
    ids=[
        "status-below-range",
        "status-above-range",
        "status-bool",
        "status-without-request",
        "transport-without-request",
        "local-runtime-error-with-request",
        "local-value-error-with-request",
        "empty-diagnostic",
        "protocol-confirmed-without-proof",
        "status-and-transport",
        "request-without-status-or-transport",
        "unknown-transport-category",
        "malformed-request-digest",
        "unknown-field",
    ],
)
def test_governance_rejects_malformed_or_unbound_upstream_diagnostic(  # noqa: C901, PLR0912
    malformation: str,
) -> None:
    diagnostic: dict[str, Any] = {
        "exit-classification": "runner-failed-after-mutation-start",
        "upstream-status": 500,
        "exception-category": None,
        "request-correlation-digest": SHA256_A,
    }
    if malformation == "status-below-range":
        diagnostic["upstream-status"] = 99
    elif malformation == "status-above-range":
        diagnostic["upstream-status"] = 600
    elif malformation == "status-bool":
        diagnostic["upstream-status"] = True
    elif malformation == "status-without-request":
        diagnostic["upstream-status"] = 201
        diagnostic["request-correlation-digest"] = None
    elif malformation == "transport-without-request":
        diagnostic["upstream-status"] = None
        diagnostic["exception-category"] = "HTTPException"
        diagnostic["request-correlation-digest"] = None
    elif malformation == "local-runtime-error-with-request":
        diagnostic["upstream-status"] = None
        diagnostic["exception-category"] = "RuntimeError"
    elif malformation == "local-value-error-with-request":
        diagnostic["upstream-status"] = None
        diagnostic["exception-category"] = "ValueError"
    elif malformation == "empty-diagnostic":
        diagnostic["upstream-status"] = None
        diagnostic["exception-category"] = None
        diagnostic["request-correlation-digest"] = None
    elif malformation == "protocol-confirmed-without-proof":
        diagnostic["exit-classification"] = "protocol-confirmed"
        diagnostic["upstream-status"] = 201
    elif malformation == "status-and-transport":
        diagnostic["upstream-status"] = 201
        diagnostic["exception-category"] = "OSError"
    elif malformation == "request-without-status-or-transport":
        diagnostic["upstream-status"] = None
        diagnostic["exception-category"] = None
    elif malformation == "unknown-transport-category":
        diagnostic["upstream-status"] = None
        diagnostic["exception-category"] = "ConnectionError"
    elif malformation == "malformed-request-digest":
        diagnostic["request-correlation-digest"] = "sha256:not-a-digest"
    else:
        diagnostic["raw-message"] = "must-not-be-admitted"
    document = _diagnostic_only_incomplete_document(diagnostic)

    with pytest.raises(
        (TypeError, ValueError),
        match="runner-diagnostic",
    ):
        _admit(document)


@pytest.mark.parametrize(
    "completion",
    [
        "protocol-confirmed-complete",
        "protocol-confirmed-readback-incomplete",
    ],
    ids=[
        "protocol-confirmed-complete",
        "protocol-confirmed-readback-incomplete",
    ],
)
def test_governance_proof_required_completion_rejects_diagnostic_only_authority(
    completion: str,
) -> None:
    document = _document()
    fact = document["probe-facts"][0]
    scenario = fact["scenarios"][0]
    diagnostic = {
        "exit-classification": "protocol-confirmed",
        "upstream-status": 201,
        "exception-category": None,
        "request-correlation-digest": SHA256_A,
    }
    scenario["response"]["result"] = (
        "protocol-confirmed"
        if completion == "protocol-confirmed-complete"
        else "protocol-confirmed-readback-incomplete"
    )
    scenario["runner-diagnostic"] = diagnostic
    scenario.pop("validated-request-proof", None)
    if completion == "protocol-confirmed-readback-incomplete":
        scenario["mutation-classification"] = "incomplete"
        scenario["post"]["state"] = "unknown"
        scenario["response"]["diagnostics"] = ["exact-readback-not-observed"]
        fact["result"] = "incomplete"
        document["mutation-classification"] = "incomplete"
    _refresh_probe_record_digest_unchecked(document, 0)

    assert "validated-request-proof" not in scenario
    assert scenario["runner-diagnostic"] == diagnostic
    with pytest.raises(ValueError, match="validated-request-proof"):
        _admit(document)


TEST_LOCAL_RETRY_4_PACKAGE_COORDINATE = (
    "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.13"
)
TEST_LOCAL_RETRY_4_WORKFLOW_PATH = (
    ".github/workflows/workflow-delivery-v3-buddy-smoke-acceptance-retry-4.yml"
)
TEST_LOCAL_RETRY_4_ENVIRONMENT = (
    "workflow-delivery-v3-buddy-smoke-acceptance-retry-4"
)
TEST_LOCAL_RETRY_4_CONFIRMATION = (
    "I_ACCEPT_DISPOSABLE_GITHUB_PACKAGES_PROBES_RETRY_4"
)
TEST_LOCAL_RETRY_4_CONFIRMATION_DIGEST = (
    "sha256:b6f94d3c13c98b0714404959dd878230f8302ee849038a536f5a18cc3a85c7ec"
)
TEST_LOCAL_RETRY_4_PREPARATION_TARGET = "0" * 40
TEST_ONLY_RETRY_4_FINALIZED_TARGET_SHA = "d" * 40
TEST_LOCAL_RETRY_4_SCENARIO_COORDINATES = {
    "absent-create-readback": (
        "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.13",
        "wdv3-acceptance-13",
    ),
    "exact": (
        "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.13",
        "wdv3-acceptance-13",
    ),
    "identical-race": (
        "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.14",
        "wdv3-acceptance-14",
    ),
    "differing-race": (
        "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.15",
        "wdv3-acceptance-15",
    ),
    "lost-response": (
        "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.16",
        "wdv3-acceptance-16",
    ),
}
TEST_LOCAL_RETRY_2_SCENARIO_COORDINATES = {
    "absent-create-readback": (
        "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.5",
        "wdv3-acceptance-5",
    ),
    "exact": (
        "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.5",
        "wdv3-acceptance-5",
    ),
    "identical-race": (
        "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.6",
        "wdv3-acceptance-6",
    ),
    "differing-race": (
        "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.7",
        "wdv3-acceptance-7",
    ),
    "lost-response": (
        "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.8",
        "wdv3-acceptance-8",
    ),
}
TEST_LOCAL_RETRY_3_SCENARIO_COORDINATES = {
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
TEST_LOCAL_HISTORICAL_FINALIZED_SUITE_DIGESTS = {
    "retry-1": (
        HISTORICAL_ABSENT_CREATE_READBACK_RECORD_DIGEST,
        HISTORICAL_EXACT_AND_CONFLICT_RECORD_DIGEST,
    ),
    "retry-2": (
        "sha256:7c19112cbadf98ea9b0fa4b2fc936ac35974dbb87fe08910ff4e4fec0be35ed9",
        "sha256:5f7c871d72a61e08550bca6c39d9821f9dd386439719bb0980f618fa2e77b732",
    ),
    "retry-3": (
        "sha256:0ca1c4578e8102918cc2014a24d5a1510953ed7ece5afd5f575e3451c9fc40d7",
        "sha256:4756bbc634b750d62caf8f2edcec4a251dd0195ffbad4ec17ff379bed94ffc0f",
    ),
}


def _test_local_retry_4_governance_profile(
    *,
    target_sha: str,
) -> Any:
    return governance_module._GovernanceAcceptanceProfile(
        package_coordinate=TEST_LOCAL_RETRY_4_PACKAGE_COORDINATE,
        workflow_path=TEST_LOCAL_RETRY_4_WORKFLOW_PATH,
        environment=TEST_LOCAL_RETRY_4_ENVIRONMENT,
        target_sha=target_sha,
        confirmation_digest=TEST_LOCAL_RETRY_4_CONFIRMATION_DIGEST,
        scenario_coordinates=tuple(
            (
                scenario,
                *TEST_LOCAL_RETRY_4_SCENARIO_COORDINATES[scenario],
            )
            for scenario in GOVERNANCE_ACCEPTANCE_SCENARIOS
        ),
    )


def _registered_retry_4_governance_profile() -> Any:
    matches = tuple(
        profile
        for profile in governance_module._GOVERNANCE_ACCEPTANCE_PROFILES
        if profile.package_coordinate == TEST_LOCAL_RETRY_4_PACKAGE_COORDINATE
    )
    if not matches:
        pytest.fail(
            "E-GOVERNANCE-PROFILE-ABSENT: the fourth reviewed Governance "
            "acceptance profile is not registered",
            pytrace=False,
        )
    assert len(matches) == 1, (
        "the fourth reviewed Governance acceptance profile must be unique"
    )
    return matches[0]


def _retry_4_preparation_document() -> dict[str, Any]:
    document = _retry_3_document()
    document["workflow"]["path"] = TEST_LOCAL_RETRY_4_WORKFLOW_PATH
    document["target-sha"] = TEST_LOCAL_RETRY_4_PREPARATION_TARGET
    document["package-coordinate"] = TEST_LOCAL_RETRY_4_PACKAGE_COORDINATE
    document["confirmation-digest"] = TEST_LOCAL_RETRY_4_CONFIRMATION_DIGEST
    document["environment"] = TEST_LOCAL_RETRY_4_ENVIRONMENT
    document["recovery"]["environment"] = TEST_LOCAL_RETRY_4_ENVIRONMENT
    return document


def _test_local_proof_document(
    *,
    package_coordinate: str,
    tag: str,
    label: str,
) -> dict[str, Any]:
    template = ValidatedAcceptanceRequestProof.from_validated_exchange(
        raw_request=(f'{{"_id":"{label}"}}').encode(),
        tarball=f"{label}-tarball".encode(),
        package_coordinate=COORDINATE,
        tag="wdv3-acceptance-1",
        upstream_status=201,
        selected_headers={
            "Content-Type": "application/json",
            "ETag": f'"{label}"',
        },
        response_body=(f'{{"ok":true,"proof":"{label}"}}').encode(),
    ).to_document()
    template["package-coordinate"] = package_coordinate
    template["tag"] = tag
    return template


def _test_local_finalized_document(
    *,
    workflow_path: str,
    target_sha: str,
    package_coordinate: str,
    confirmation_digest: str,
    environment: str,
    scenario_coordinates: dict[str, tuple[str, str]],
    proof_namespace: str,
) -> dict[str, Any]:
    document = _document()
    document["workflow"]["path"] = workflow_path
    document["target-sha"] = target_sha
    document["package-coordinate"] = package_coordinate
    document["confirmation-digest"] = confirmation_digest
    document["environment"] = environment
    document["recovery"]["environment"] = environment
    proof_documents = {
        scenario: _test_local_proof_document(
            package_coordinate=scenario_coordinates[scenario][0],
            tag=scenario_coordinates[scenario][1],
            label=f"{proof_namespace}-{scenario}",
        )
        for scenario in ("absent-create-readback", "lost-response")
    }
    for probe_index, fact in enumerate(document["probe-facts"]):
        for scenario_document in fact["scenarios"]:
            scenario = scenario_document["scenario"]
            coordinate, tag = scenario_coordinates[scenario]
            scenario_document["package-coordinate"] = coordinate
            scenario_document["tag"] = tag
            scenario_document.pop("validated-request-proof", None)
            if scenario == "absent-create-readback":
                proof = proof_documents[scenario]
                scenario_document["response"]["result"] = "protocol-confirmed"
                scenario_document["response"]["identity-digest"] = proof[
                    "response-identity-digest"
                ]
                scenario_document["response"]["diagnostics"] = []
                scenario_document["post"]["content-sha512"] = proof[
                    "tarball-sha512"
                ]
                scenario_document["validated-request-proof"] = proof
            elif scenario == "lost-response":
                proof = proof_documents[scenario]
                scenario_document["response"]["identity-digest"] = proof[
                    "response-identity-digest"
                ]
                scenario_document["post"]["content-sha512"] = proof[
                    "tarball-sha512"
                ]
                scenario_document["validated-request-proof"] = proof
        _refresh_probe_record_digest_unchecked(document, probe_index)
    return document


def test_retry_4_governance_profiles_have_stable_historical_order_and_unique_base_coordinates() -> (
    None
):
    retry_4_profile = _registered_retry_4_governance_profile()
    profiles = governance_module._GOVERNANCE_ACCEPTANCE_PROFILES
    base_coordinates = tuple(profile.package_coordinate for profile in profiles)

    assert base_coordinates == (
        "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.1",
        "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.5",
        "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.9",
        "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.13",
    )
    assert len(base_coordinates) == len(set(base_coordinates)) == len(profiles)
    assert profiles[-1] is retry_4_profile


def test_retry_4_governance_profile_binds_exact_workflow_environment_confirmation_digest_and_scenarios() -> (
    None
):
    profile = _registered_retry_4_governance_profile()

    assert profile.package_coordinate == TEST_LOCAL_RETRY_4_PACKAGE_COORDINATE
    assert profile.workflow_path == TEST_LOCAL_RETRY_4_WORKFLOW_PATH
    assert profile.environment == TEST_LOCAL_RETRY_4_ENVIRONMENT
    assert profile.confirmation_digest == (
        TEST_LOCAL_RETRY_4_CONFIRMATION_DIGEST
    )
    assert TEST_LOCAL_RETRY_4_CONFIRMATION == (
        "I_ACCEPT_DISPOSABLE_GITHUB_PACKAGES_PROBES_RETRY_4"
    )
    assert (
        ValidatedAcceptanceRequestProof._sha256(
            TEST_LOCAL_RETRY_4_CONFIRMATION.encode("ascii")
        )
        == TEST_LOCAL_RETRY_4_CONFIRMATION_DIGEST
    )
    assert tuple(profile.coordinates().items()) == tuple(
        TEST_LOCAL_RETRY_4_SCENARIO_COORDINATES.items()
    )
    assert tuple(profile.coordinates()) == GOVERNANCE_ACCEPTANCE_SCENARIOS
    assert profile.target_sha == "0" * 40
    assert profile.target_sha.encode("ascii") == b"0" * 40


def test_retry_4_governance_admits_exact_zero_target_rejected_dispatch() -> (
    None
):
    _registered_retry_4_governance_profile()
    document = _retry_4_preparation_document()

    admitted = _admit(document)
    admitted_document = admitted.to_document()

    assert document["target-sha"].encode("ascii") == b"0" * 40
    assert admitted.target_sha == TEST_LOCAL_RETRY_4_PREPARATION_TARGET
    assert [
        (result.job, result.result) for result in admitted.dependency_results
    ] == [
        ("validate-fixed-inputs", "failure"),
        ("acceptance-review", "skipped"),
        ("probe-absent-create-readback", "skipped"),
        ("probe-exact-and-conflict", "skipped"),
    ]
    assert admitted.mutation_classification == "incomplete"
    assert admitted.recovery.artifact_id is None
    assert admitted.reviewer is None
    assert admitted.reviewer_source == "unavailable-in-job-context"
    assert [
        (
            fact.result,
            fact.record_digest,
            fact.artifact_id,
            fact.artifact_digest,
            fact.scenarios,
        )
        for fact in admitted.probe_facts
    ] == [
        ("incomplete", None, None, None, ()),
        ("incomplete", None, None, None, ()),
    ]
    assert admitted_document == document
    assert canonicalize(admitted_document) == canonicalize(document)


@pytest.mark.parametrize(
    ("target_sha", "message"),
    [
        pytest.param(
            "0" * 39,
            "40 lowercase hexadecimal",
            id="39-ascii-zeroes",
        ),
        pytest.param(
            "0" * 41,
            "40 lowercase hexadecimal",
            id="41-ascii-zeroes",
        ),
        pytest.param(
            "\uff10" * 40,
            "40 lowercase hexadecimal",
            id="40-non-ascii-zeroes",
        ),
        pytest.param(
            ("0" * 39) + "1",
            "reviewed acceptance profile",
            id="40-hex-with-nonzero-nibble",
        ),
    ],
)
def test_retry_4_governance_rejects_non_exact_zero_targets(
    target_sha: str,
    message: str,
) -> None:
    profile = _registered_retry_4_governance_profile()
    if target_sha == profile.target_sha:
        target_sha = ("0" * 39) + "2"
    document = _retry_4_preparation_document()
    document["target-sha"] = target_sha

    with pytest.raises(ValueError, match=message):
        _admit(document)


@pytest.mark.parametrize(
    ("path", "value", "message"),
    [
        pytest.param(
            ("dependency-results", 1, "result"),
            "success",
            "zero target-sha requires exact rejected",
            id="environment-review-ran",
        ),
        pytest.param(
            ("dependency-results", 2, "result"),
            "success",
            "zero target-sha requires exact rejected",
            id="absent-create-probe-ran",
        ),
        pytest.param(
            ("dependency-results", 3, "result"),
            "success",
            "zero target-sha requires exact rejected",
            id="exact-and-conflict-probe-ran",
        ),
        pytest.param(
            ("probe-facts", 0, "record-digest"),
            SHA256_A,
            "retain suite records",
            id="probe-record-digest-retained",
        ),
        pytest.param(
            ("probe-facts", 0, "scenarios"),
            "test-local-retained-scenario",
            "record-digest",
            id="probe-scenario-retained",
        ),
        pytest.param(
            ("probe-facts", 0, "artifact-id"),
            799,
            "zero target-sha requires exact rejected",
            id="probe-artifact-id-retained",
        ),
        pytest.param(
            ("probe-facts", 0, "artifact-digest"),
            SHA256_A,
            "zero target-sha requires exact rejected",
            id="probe-artifact-digest-retained",
        ),
        pytest.param(
            ("recovery", "artifact-id"),
            701,
            "zero target-sha requires exact rejected",
            id="review-artifact-retained",
        ),
        pytest.param(
            ("reviewer",),
            {
                "login": "octocat",
                "source": "on-demand-read-only-inspection",
            },
            "zero target-sha requires exact rejected",
            id="reviewer-attributed",
        ),
        pytest.param(
            ("mutation-classification",),
            "unknown",
            "mutation-classification",
            id="possible-mutation-claimed",
        ),
    ],
)
def test_retry_4_zero_target_rejects_review_probe_record_artifact_reviewer_or_mutation_claims(
    path: tuple[object, ...],
    value: object,
    message: str,
) -> None:
    _registered_retry_4_governance_profile()
    document = _retry_4_preparation_document()
    if value == "test-local-retained-scenario":
        retained_scenario = _scenario("absent-create-readback")
        retained_scenario["package-coordinate"], retained_scenario["tag"] = (
            TEST_LOCAL_RETRY_4_SCENARIO_COORDINATES["absent-create-readback"]
        )
        value = [retained_scenario]
    _set_path(document, path, value)

    with pytest.raises(ValueError, match=message):
        _admit(document)


def test_retry_4_finalized_placeholder_round_trips_canonically_with_exact_bindings(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    profile = _test_local_retry_4_governance_profile(
        target_sha=TEST_ONLY_RETRY_4_FINALIZED_TARGET_SHA
    )
    historical_profiles = tuple(
        existing
        for existing in governance_module._GOVERNANCE_ACCEPTANCE_PROFILES
        if existing.package_coordinate != TEST_LOCAL_RETRY_4_PACKAGE_COORDINATE
    )
    assert tuple(
        existing.package_coordinate for existing in historical_profiles
    ) == (
        "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.1",
        "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.5",
        "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.9",
    )
    monkeypatch.setattr(
        governance_module,
        "_GOVERNANCE_ACCEPTANCE_PROFILES",
        (*historical_profiles, profile),
    )
    document = _test_local_finalized_document(
        workflow_path=TEST_LOCAL_RETRY_4_WORKFLOW_PATH,
        target_sha=TEST_ONLY_RETRY_4_FINALIZED_TARGET_SHA,
        package_coordinate=TEST_LOCAL_RETRY_4_PACKAGE_COORDINATE,
        confirmation_digest=TEST_LOCAL_RETRY_4_CONFIRMATION_DIGEST,
        environment=TEST_LOCAL_RETRY_4_ENVIRONMENT,
        scenario_coordinates=TEST_LOCAL_RETRY_4_SCENARIO_COORDINATES,
        proof_namespace="test-only-retry-4-placeholder",
    )

    raw = canonicalize(document)
    admitted = admit_governance_acceptance_evidence(raw)
    admitted_document = admitted.to_document()
    probe_facts = cast("list[dict[str, Any]]", admitted_document["probe-facts"])
    scenarios = {
        scenario["scenario"]: scenario
        for fact in probe_facts
        for scenario in cast("list[dict[str, Any]]", fact["scenarios"])
    }

    assert TEST_ONLY_RETRY_4_FINALIZED_TARGET_SHA == "d" * 40
    assert TEST_ONLY_RETRY_4_FINALIZED_TARGET_SHA != (
        TEST_LOCAL_RETRY_4_PREPARATION_TARGET
    )
    assert admitted.target_sha == TEST_ONLY_RETRY_4_FINALIZED_TARGET_SHA
    assert raw == canonicalize(admitted_document)
    assert admitted_document == document
    assert admitted.evidence_digest == canonical_sha256(document)
    assert [
        (
            scenario,
            scenarios[scenario]["package-coordinate"],
            scenarios[scenario]["tag"],
        )
        for scenario in GOVERNANCE_ACCEPTANCE_SCENARIOS
    ] == [
        (scenario, *TEST_LOCAL_RETRY_4_SCENARIO_COORDINATES[scenario])
        for scenario in GOVERNANCE_ACCEPTANCE_SCENARIOS
    ]
    for scenario in ("absent-create-readback", "lost-response"):
        scenario_document = scenarios[scenario]
        proof = scenario_document["validated-request-proof"]
        assert (
            proof["package-coordinate"],
            proof["tag"],
        ) == TEST_LOCAL_RETRY_4_SCENARIO_COORDINATES[scenario]
        assert (
            scenario_document["response"]["identity-digest"]
            == proof["response-identity-digest"]
        )
        assert (
            scenario_document["post"]["content-sha512"]
            == proof["tarball-sha512"]
        )


@pytest.mark.parametrize(
    ("document_profile", "field"),
    [
        pytest.param(
            "retry-4",
            "workflow",
            id="retry-4-document-with-retry-3-workflow",
        ),
        pytest.param(
            "retry-4",
            "environment",
            id="retry-4-document-with-retry-3-environment",
        ),
        pytest.param(
            "retry-4",
            "recovery-environment",
            id="retry-4-document-with-retry-3-recovery-environment",
        ),
        pytest.param(
            "retry-4",
            "confirmation-digest",
            id="retry-4-document-with-retry-3-confirmation-digest",
        ),
        pytest.param(
            "retry-4",
            "target",
            id="retry-4-document-with-retry-3-target",
        ),
        pytest.param(
            "retry-4",
            "coordinate",
            id="retry-4-document-with-retry-3-coordinate",
        ),
        pytest.param(
            "retry-4",
            "tag",
            id="retry-4-document-with-retry-3-tag",
        ),
        pytest.param(
            "retry-3",
            "workflow",
            id="retry-3-document-with-retry-4-workflow",
        ),
        pytest.param(
            "retry-3",
            "environment",
            id="retry-3-document-with-retry-4-environment",
        ),
        pytest.param(
            "retry-3",
            "recovery-environment",
            id="retry-3-document-with-retry-4-recovery-environment",
        ),
        pytest.param(
            "retry-3",
            "confirmation-digest",
            id="retry-3-document-with-retry-4-confirmation-digest",
        ),
        pytest.param(
            "retry-3",
            "target",
            id="retry-3-document-with-retry-4-target",
        ),
        pytest.param(
            "retry-3",
            "coordinate",
            id="retry-3-document-with-retry-4-coordinate",
        ),
        pytest.param(
            "retry-3",
            "tag",
            id="retry-3-document-with-retry-4-tag",
        ),
    ],
)
def test_retry_4_governance_rejects_cross_profile_field_substitutions(
    document_profile: str,
    field: str,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    registered_retry_4_profile = _registered_retry_4_governance_profile()
    assert (
        registered_retry_4_profile.target_sha
        == TEST_LOCAL_RETRY_4_PREPARATION_TARGET
    )
    paths = {
        "workflow": ("workflow", "path"),
        "environment": ("environment",),
        "recovery-environment": ("recovery", "environment"),
        "confirmation-digest": ("confirmation-digest",),
        "target": ("target-sha",),
        "coordinate": (
            "probe-facts",
            0,
            "scenarios",
            0,
            "package-coordinate",
        ),
        "tag": ("probe-facts", 0, "scenarios", 0, "tag"),
    }
    messages = {
        "workflow": "workflow.path",
        "environment": "environment",
        "recovery-environment": "recovery environment",
        "confirmation-digest": "confirmation-digest",
        "target": "target-sha",
        "coordinate": "package-coordinate",
        "tag": "tag",
    }
    retry_3_values = {
        "workflow": GOVERNANCE_RETRY_3_ACCEPTANCE_WORKFLOW_PATH,
        "environment": GOVERNANCE_RETRY_3_ACCEPTANCE_ENVIRONMENT,
        "recovery-environment": GOVERNANCE_RETRY_3_ACCEPTANCE_ENVIRONMENT,
        "confirmation-digest": (
            "sha256:"
            "33e59948941f5f1111d5017ab80dd33c90dd2ac8d1a17203e7f7382a8c5b2c72"
        ),
        "target": RETRY_3_TARGET_SHA,
        "coordinate": TEST_LOCAL_RETRY_3_SCENARIO_COORDINATES[
            "absent-create-readback"
        ][0],
        "tag": TEST_LOCAL_RETRY_3_SCENARIO_COORDINATES[
            "absent-create-readback"
        ][1],
    }
    retry_4_values = {
        "workflow": registered_retry_4_profile.workflow_path,
        "environment": registered_retry_4_profile.environment,
        "recovery-environment": registered_retry_4_profile.environment,
        "confirmation-digest": registered_retry_4_profile.confirmation_digest,
        "target": registered_retry_4_profile.target_sha,
        "coordinate": registered_retry_4_profile.coordinates()[
            "absent-create-readback"
        ][0],
        "tag": registered_retry_4_profile.coordinates()[
            "absent-create-readback"
        ][1],
    }
    if document_profile == "retry-4":
        if field in {"coordinate", "tag"}:
            finalized_retry_4_profile = _test_local_retry_4_governance_profile(
                target_sha=TEST_ONLY_RETRY_4_FINALIZED_TARGET_SHA
            )
            historical_profiles = tuple(
                existing
                for existing in governance_module._GOVERNANCE_ACCEPTANCE_PROFILES
                if existing.package_coordinate
                != TEST_LOCAL_RETRY_4_PACKAGE_COORDINATE
            )
            monkeypatch.setattr(
                governance_module,
                "_GOVERNANCE_ACCEPTANCE_PROFILES",
                (*historical_profiles, finalized_retry_4_profile),
            )
            document = _test_local_finalized_document(
                workflow_path=finalized_retry_4_profile.workflow_path,
                target_sha=finalized_retry_4_profile.target_sha,
                package_coordinate=finalized_retry_4_profile.package_coordinate,
                confirmation_digest=(
                    finalized_retry_4_profile.confirmation_digest
                ),
                environment=finalized_retry_4_profile.environment,
                scenario_coordinates=finalized_retry_4_profile.coordinates(),
                proof_namespace="test-only-retry-4-cross-profile",
            )
            assert (
                finalized_retry_4_profile.target_sha
                == TEST_ONLY_RETRY_4_FINALIZED_TARGET_SHA
            )
            assert (
                finalized_retry_4_profile.target_sha
                != registered_retry_4_profile.target_sha
            )
        else:
            document = _retry_4_preparation_document()
        replacement = retry_3_values[field]
    else:
        document = _test_local_finalized_document(
            workflow_path=GOVERNANCE_RETRY_3_ACCEPTANCE_WORKFLOW_PATH,
            target_sha=RETRY_3_TARGET_SHA,
            package_coordinate=GOVERNANCE_RETRY_3_ACCEPTANCE_PACKAGE_COORDINATE,
            confirmation_digest=(
                "sha256:"
                "33e59948941f5f1111d5017ab80dd33c90dd2ac8d1a17203e7f7382a8c5b2c72"
            ),
            environment=GOVERNANCE_RETRY_3_ACCEPTANCE_ENVIRONMENT,
            scenario_coordinates=TEST_LOCAL_RETRY_3_SCENARIO_COORDINATES,
            proof_namespace="historical-retry-3-control",
        )
        replacement = retry_4_values[field]

    admitted_control = _admit(document)
    assert admitted_control.to_document() == document
    if document_profile == "retry-4" and field not in {"coordinate", "tag"}:
        assert (
            admitted_control.target_sha == TEST_LOCAL_RETRY_4_PREPARATION_TARGET
        )
        assert admitted_control.mutation_classification == "incomplete"
    else:
        expected_target = (
            TEST_ONLY_RETRY_4_FINALIZED_TARGET_SHA
            if document_profile == "retry-4"
            else RETRY_3_TARGET_SHA
        )
        assert admitted_control.target_sha == expected_target
        assert admitted_control.mutation_classification == "complete"
    mutated = deepcopy(document)
    _set_path(mutated, paths[field], replacement)
    assert mutated != document
    with pytest.raises(ValueError, match=messages[field]):
        _admit(mutated)


def test_retry_4_governance_preserves_historical_profiles_digests_and_replay_evidence() -> (
    None
):
    historical_profile_tuples = tuple(
        (
            profile.package_coordinate,
            profile.workflow_path,
            profile.environment,
            profile.target_sha,
            profile.confirmation_digest,
            profile.scenario_coordinates,
        )
        for profile in governance_module._GOVERNANCE_ACCEPTANCE_PROFILES[:3]
    )
    assert historical_profile_tuples == (
        (
            "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.1",
            (
                ".github/workflows/"
                "workflow-delivery-v3-buddy-smoke-acceptance.yml"
            ),
            "workflow-delivery-v3-buddy-smoke-acceptance",
            "5a84bebd05407e1859fe76f400dcb4f4cbcd002e",
            (
                "sha256:"
                "6ab9696b51f21083802af68d80104f65ffb844bdcd449974c881e5a8cc96ad5e"
            ),
            (
                (
                    "absent-create-readback",
                    "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.1",
                    "wdv3-acceptance-1",
                ),
                (
                    "exact",
                    "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.1",
                    "wdv3-acceptance-1",
                ),
                (
                    "identical-race",
                    "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.2",
                    "wdv3-acceptance-2",
                ),
                (
                    "differing-race",
                    "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.3",
                    "wdv3-acceptance-3",
                ),
                (
                    "lost-response",
                    "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.4",
                    "wdv3-acceptance-4",
                ),
            ),
        ),
        (
            "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.5",
            (
                ".github/workflows/"
                "workflow-delivery-v3-buddy-smoke-acceptance-retry-2.yml"
            ),
            "workflow-delivery-v3-buddy-smoke-acceptance-retry-2",
            "b031e5e0bd98a95943a03a1529b64e856e1a8aa1",
            (
                "sha256:"
                "1215f9d01cd343462c3f826ba67ebee86b6f6142b7fcfe5630572a5a808314f8"
            ),
            (
                (
                    "absent-create-readback",
                    "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.5",
                    "wdv3-acceptance-5",
                ),
                (
                    "exact",
                    "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.5",
                    "wdv3-acceptance-5",
                ),
                (
                    "identical-race",
                    "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.6",
                    "wdv3-acceptance-6",
                ),
                (
                    "differing-race",
                    "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.7",
                    "wdv3-acceptance-7",
                ),
                (
                    "lost-response",
                    "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.8",
                    "wdv3-acceptance-8",
                ),
            ),
        ),
        (
            "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.9",
            (
                ".github/workflows/"
                "workflow-delivery-v3-buddy-smoke-acceptance-retry-3.yml"
            ),
            "workflow-delivery-v3-buddy-smoke-acceptance-retry-3",
            RETRY_3_TARGET_SHA,
            (
                "sha256:"
                "33e59948941f5f1111d5017ab80dd33c90dd2ac8d1a17203e7f7382a8c5b2c72"
            ),
            (
                (
                    "absent-create-readback",
                    "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.9",
                    "wdv3-acceptance-9",
                ),
                (
                    "exact",
                    "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.9",
                    "wdv3-acceptance-9",
                ),
                (
                    "identical-race",
                    "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.10",
                    "wdv3-acceptance-10",
                ),
                (
                    "differing-race",
                    "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.11",
                    "wdv3-acceptance-11",
                ),
                (
                    "lost-response",
                    "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.12",
                    "wdv3-acceptance-12",
                ),
            ),
        ),
    )
    documents = {
        "retry-1": _document(),
        "retry-2": _test_local_finalized_document(
            workflow_path=(
                ".github/workflows/"
                "workflow-delivery-v3-buddy-smoke-acceptance-retry-2.yml"
            ),
            target_sha="b031e5e0bd98a95943a03a1529b64e856e1a8aa1",
            package_coordinate=(
                "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.5"
            ),
            confirmation_digest=(
                "sha256:"
                "1215f9d01cd343462c3f826ba67ebee86b6f6142b7fcfe5630572a5a808314f8"
            ),
            environment=("workflow-delivery-v3-buddy-smoke-acceptance-retry-2"),
            scenario_coordinates=TEST_LOCAL_RETRY_2_SCENARIO_COORDINATES,
            proof_namespace="historical-retry-2",
        ),
        "retry-3": _test_local_finalized_document(
            workflow_path=GOVERNANCE_RETRY_3_ACCEPTANCE_WORKFLOW_PATH,
            target_sha=RETRY_3_TARGET_SHA,
            package_coordinate=GOVERNANCE_RETRY_3_ACCEPTANCE_PACKAGE_COORDINATE,
            confirmation_digest=(
                "sha256:"
                "33e59948941f5f1111d5017ab80dd33c90dd2ac8d1a17203e7f7382a8c5b2c72"
            ),
            environment=GOVERNANCE_RETRY_3_ACCEPTANCE_ENVIRONMENT,
            scenario_coordinates=TEST_LOCAL_RETRY_3_SCENARIO_COORDINATES,
            proof_namespace="historical-retry-3",
        ),
    }

    for profile_name, document in documents.items():
        admitted = _admit(document)
        admitted_document = admitted.to_document()
        scenarios = {
            scenario["scenario"]: scenario
            for fact in admitted_document["probe-facts"]
            for scenario in fact["scenarios"]
        }

        assert admitted_document == document
        assert canonicalize(admitted_document) == canonicalize(document)
        assert admitted.evidence_digest == canonical_sha256(document)
        assert (
            tuple(
                fact["record-digest"]
                for fact in admitted_document["probe-facts"]
            )
            == TEST_LOCAL_HISTORICAL_FINALIZED_SUITE_DIGESTS[profile_name]
        )
        assert tuple(scenarios) == GOVERNANCE_ACCEPTANCE_SCENARIOS
        for scenario_name, scenario_document in scenarios.items():
            expected = historical_profile_tuples[
                ("retry-1", "retry-2", "retry-3").index(profile_name)
            ][5][GOVERNANCE_ACCEPTANCE_SCENARIOS.index(scenario_name)]
            assert (
                scenario_document["scenario"],
                scenario_document["package-coordinate"],
                scenario_document["tag"],
            ) == expected

    retry_1_scenarios = {
        scenario["scenario"]: scenario
        for fact in documents["retry-1"]["probe-facts"]
        for scenario in fact["scenarios"]
    }
    assert (
        "validated-request-proof"
        not in retry_1_scenarios["absent-create-readback"]
    )
    for profile_name in ("retry-2", "retry-3"):
        scenarios = {
            scenario["scenario"]: scenario
            for fact in documents[profile_name]["probe-facts"]
            for scenario in fact["scenarios"]
        }
        for scenario_name in ("absent-create-readback", "lost-response"):
            scenario_document = scenarios[scenario_name]
            proof = scenario_document["validated-request-proof"]
            assert (
                scenario_document["response"]["identity-digest"]
                == proof["response-identity-digest"]
            )
            assert (
                scenario_document["post"]["content-sha512"]
                == proof["tarball-sha512"]
            )
