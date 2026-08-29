"""Strict Governance-only record contracts for Workflow Delivery v3."""

from __future__ import annotations

import re
from dataclasses import dataclass
from typing import TYPE_CHECKING, cast

from three_workflow_delivery_v3.canonical import (
    canonical_sha256,
    parse_canonical_json,
)

if TYPE_CHECKING:
    from three_workflow_delivery_v3.canonical import JsonValue

GOVERNANCE_ACCEPTANCE_EVIDENCE_SCHEMA = (
    "workflow-delivery/v3/governance-acceptance-evidence"
)
GOVERNANCE_ACCEPTANCE_EVIDENCE_PURPOSE = "destination-acceptance"
GOVERNANCE_ACCEPTANCE_EVIDENCE_RELEASE_LINEAGE = "none"
GOVERNANCE_ACCEPTANCE_REPOSITORY = "hcoona/three"
GOVERNANCE_ACCEPTANCE_WORKFLOW_PATH = (
    ".github/workflows/workflow-delivery-v3-buddy-smoke-acceptance.yml"
)
GOVERNANCE_ACCEPTANCE_REF = "refs/heads/main"
GOVERNANCE_ACCEPTANCE_PACKAGE_COORDINATE = (
    "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.1"
)
GOVERNANCE_ACCEPTANCE_ENVIRONMENT = (
    "workflow-delivery-v3-buddy-smoke-acceptance"
)
GOVERNANCE_ACCEPTANCE_PRODUCER = "capture-governance-evidence"

GOVERNANCE_ACCEPTANCE_DEPENDENCIES = (
    "validate-fixed-inputs",
    "acceptance-review",
    "probe-absent-create-readback",
    "probe-exact-and-conflict",
)
GOVERNANCE_ACCEPTANCE_PROBES = (
    "probe-absent-create-readback",
    "probe-exact-and-conflict",
)
GOVERNANCE_ACCEPTANCE_SCENARIOS = (
    "absent-create-readback",
    "exact",
    "identical-race",
    "differing-race",
    "lost-response",
)
GOVERNANCE_ACCEPTANCE_PROBE_SCENARIOS = {
    GOVERNANCE_ACCEPTANCE_PROBES[0]: ("absent-create-readback",),
    GOVERNANCE_ACCEPTANCE_PROBES[1]: (
        "exact",
        "identical-race",
        "differing-race",
        "lost-response",
    ),
}
GOVERNANCE_ACCEPTANCE_SCENARIO_COORDINATES = {
    "absent-create-readback": (
        "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.1",
        "wdv3-acceptance-1",
    ),
    "exact": (
        "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.1",
        "wdv3-acceptance-1",
    ),
    "identical-race": (
        "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.2",
        "wdv3-acceptance-2",
    ),
    "differing-race": (
        "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.3",
        "wdv3-acceptance-3",
    ),
    "lost-response": (
        "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.4",
        "wdv3-acceptance-4",
    ),
}
GOVERNANCE_RETRY_2_ACCEPTANCE_WORKFLOW_PATH = (
    ".github/workflows/workflow-delivery-v3-buddy-smoke-acceptance-retry-2.yml"
)
GOVERNANCE_RETRY_2_ACCEPTANCE_PACKAGE_COORDINATE = (
    "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.5"
)
GOVERNANCE_RETRY_2_ACCEPTANCE_ENVIRONMENT = (
    "workflow-delivery-v3-buddy-smoke-acceptance-retry-2"
)
GOVERNANCE_RETRY_2_ACCEPTANCE_SCENARIO_COORDINATES = {
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
GOVERNANCE_RETRY_3_ACCEPTANCE_WORKFLOW_PATH = (
    ".github/workflows/workflow-delivery-v3-buddy-smoke-acceptance-retry-3.yml"
)
GOVERNANCE_RETRY_3_ACCEPTANCE_PACKAGE_COORDINATE = (
    "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.9"
)
GOVERNANCE_RETRY_3_ACCEPTANCE_ENVIRONMENT = (
    "workflow-delivery-v3-buddy-smoke-acceptance-retry-3"
)
GOVERNANCE_RETRY_3_ACCEPTANCE_SCENARIO_COORDINATES = {
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
GOVERNANCE_RETRY_4_ACCEPTANCE_WORKFLOW_PATH = (
    ".github/workflows/workflow-delivery-v3-buddy-smoke-acceptance-retry-4.yml"
)
GOVERNANCE_RETRY_4_ACCEPTANCE_PACKAGE_COORDINATE = (
    "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.13"
)
GOVERNANCE_RETRY_4_ACCEPTANCE_ENVIRONMENT = (
    "workflow-delivery-v3-buddy-smoke-acceptance-retry-4"
)
GOVERNANCE_RETRY_4_ACCEPTANCE_SCENARIO_COORDINATES = {
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
GOVERNANCE_RETRY_5_ACCEPTANCE_WORKFLOW_PATH = (
    ".github/workflows/workflow-delivery-v3-buddy-smoke-acceptance-retry-5.yml"
)
GOVERNANCE_RETRY_5_ACCEPTANCE_PACKAGE_COORDINATE = (
    "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.17"
)
GOVERNANCE_RETRY_5_ACCEPTANCE_ENVIRONMENT = (
    "workflow-delivery-v3-buddy-smoke-acceptance-retry-5"
)
GOVERNANCE_RETRY_5_ACCEPTANCE_SCENARIO_COORDINATES = {
    "absent-create-readback": (
        "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.17",
        "wdv3-acceptance-17",
    ),
    "exact": (
        "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.17",
        "wdv3-acceptance-17",
    ),
    "identical-race": (
        "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.18",
        "wdv3-acceptance-18",
    ),
    "differing-race": (
        "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.19",
        "wdv3-acceptance-19",
    ),
    "lost-response": (
        "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.20",
        "wdv3-acceptance-20",
    ),
}


@dataclass(frozen=True, slots=True)
class _GovernanceAcceptanceProfile:
    package_coordinate: str
    workflow_path: str
    environment: str
    target_sha: str
    confirmation_digest: str
    scenario_coordinates: tuple[tuple[str, str, str], ...]

    def coordinates(self) -> dict[str, tuple[str, str]]:
        return {
            scenario: (coordinate, tag)
            for scenario, coordinate, tag in self.scenario_coordinates
        }


_GOVERNANCE_ACCEPTANCE_PROFILES = (
    _GovernanceAcceptanceProfile(
        package_coordinate=GOVERNANCE_ACCEPTANCE_PACKAGE_COORDINATE,
        workflow_path=GOVERNANCE_ACCEPTANCE_WORKFLOW_PATH,
        environment=GOVERNANCE_ACCEPTANCE_ENVIRONMENT,
        target_sha="5a84bebd05407e1859fe76f400dcb4f4cbcd002e",
        confirmation_digest=(
            "sha256:"
            "6ab9696b51f21083802af68d80104f65ffb844bdcd449974c881e5a8cc96ad5e"
        ),
        scenario_coordinates=tuple(
            (
                scenario,
                coordinate,
                tag,
            )
            for scenario, (coordinate, tag) in (
                GOVERNANCE_ACCEPTANCE_SCENARIO_COORDINATES.items()
            )
        ),
    ),
    _GovernanceAcceptanceProfile(
        package_coordinate=GOVERNANCE_RETRY_2_ACCEPTANCE_PACKAGE_COORDINATE,
        workflow_path=GOVERNANCE_RETRY_2_ACCEPTANCE_WORKFLOW_PATH,
        environment=GOVERNANCE_RETRY_2_ACCEPTANCE_ENVIRONMENT,
        target_sha="b031e5e0bd98a95943a03a1529b64e856e1a8aa1",
        confirmation_digest=(
            "sha256:"
            "1215f9d01cd343462c3f826ba67ebee86b6f6142b7fcfe5630572a5a808314f8"
        ),
        scenario_coordinates=tuple(
            (
                scenario,
                coordinate,
                tag,
            )
            for scenario, (coordinate, tag) in (
                GOVERNANCE_RETRY_2_ACCEPTANCE_SCENARIO_COORDINATES.items()
            )
        ),
    ),
    _GovernanceAcceptanceProfile(
        package_coordinate=GOVERNANCE_RETRY_3_ACCEPTANCE_PACKAGE_COORDINATE,
        workflow_path=GOVERNANCE_RETRY_3_ACCEPTANCE_WORKFLOW_PATH,
        environment=GOVERNANCE_RETRY_3_ACCEPTANCE_ENVIRONMENT,
        target_sha="a61f9a4e44458bfd7bc7bfd96f6db848ce047c0c",
        confirmation_digest=(
            "sha256:"
            "33e59948941f5f1111d5017ab80dd33c90dd2ac8d1a17203e7f7382a8c5b2c72"
        ),
        scenario_coordinates=tuple(
            (
                scenario,
                coordinate,
                tag,
            )
            for scenario, (coordinate, tag) in (
                GOVERNANCE_RETRY_3_ACCEPTANCE_SCENARIO_COORDINATES.items()
            )
        ),
    ),
    _GovernanceAcceptanceProfile(
        package_coordinate=GOVERNANCE_RETRY_4_ACCEPTANCE_PACKAGE_COORDINATE,
        workflow_path=GOVERNANCE_RETRY_4_ACCEPTANCE_WORKFLOW_PATH,
        environment=GOVERNANCE_RETRY_4_ACCEPTANCE_ENVIRONMENT,
        target_sha="835b81be1ff0ba7aa0ec23c9a7b518d4ade3dfaa",
        confirmation_digest=(
            "sha256:"
            "b6f94d3c13c98b0714404959dd878230f8302ee849038a536f5a18cc3a85c7ec"
        ),
        scenario_coordinates=tuple(
            (
                scenario,
                coordinate,
                tag,
            )
            for scenario, (coordinate, tag) in (
                GOVERNANCE_RETRY_4_ACCEPTANCE_SCENARIO_COORDINATES.items()
            )
        ),
    ),
    _GovernanceAcceptanceProfile(
        package_coordinate=GOVERNANCE_RETRY_5_ACCEPTANCE_PACKAGE_COORDINATE,
        workflow_path=GOVERNANCE_RETRY_5_ACCEPTANCE_WORKFLOW_PATH,
        environment=GOVERNANCE_RETRY_5_ACCEPTANCE_ENVIRONMENT,
        target_sha="66154d0bb351a0c9c13d16292ce003d7eee65077",
        confirmation_digest=(
            "sha256:"
            "71fdd8f8cbb3ab90dd94745a18337d89a893fbdaeea35fafa733bc13d75c308f"
        ),
        scenario_coordinates=tuple(
            (
                scenario,
                coordinate,
                tag,
            )
            for scenario, (coordinate, tag) in (
                GOVERNANCE_RETRY_5_ACCEPTANCE_SCENARIO_COORDINATES.items()
            )
        ),
    ),
)


def _acceptance_profile(
    package_coordinate: object,
) -> _GovernanceAcceptanceProfile:
    accepted = _string(package_coordinate, field="package-coordinate")
    for profile in _GOVERNANCE_ACCEPTANCE_PROFILES:
        if accepted == profile.package_coordinate:
            return profile
    message = "package-coordinate is not one reviewed acceptance profile"
    raise ValueError(message)


_TOP_LEVEL_FIELDS = frozenset(
    {
        "schema",
        "purpose",
        "workflow",
        "target-sha",
        "package-coordinate",
        "confirmation-digest",
        "environment",
        "reviewer",
        "recovery",
        "dependency-results",
        "probe-facts",
        "mutation-classification",
        "producer",
        "workflow-run-id",
        "run-attempt",
        "release-lineage",
    }
)
_WORKFLOW_FIELDS = frozenset({"repository", "path", "ref", "sha"})
_REVIEWER_FIELDS = frozenset({"login", "source"})
_RECOVERY_FIELDS = frozenset(
    {"workflow-run-id", "environment", "deployment", "job", "artifact-id"}
)
_DEPENDENCY_FIELDS = frozenset({"job", "result"})
_PROBE_FIELDS = frozenset(
    {
        "probe",
        "result",
        "scenario-inventory",
        "record-digest",
        "artifact-id",
        "artifact-digest",
        "scenarios",
    }
)
_SCENARIO_FIELDS = frozenset(
    {
        "scenario",
        "package-coordinate",
        "tag",
        "mutation-classification",
        "pre",
        "action",
        "response",
        "post",
    }
)
_PRE_FIELDS = frozenset({"state"})
_ACTION_FIELDS = frozenset({"operation", "executed", "mutation-started"})
_RESPONSE_FIELDS = frozenset({"result", "identity-digest", "diagnostics"})
_POST_FIELDS = frozenset({"state", "content-sha512"})
_VALIDATED_REQUEST_PROOF_FIELDS = frozenset(
    {
        "schema",
        "request-digest",
        "tarball-sha512",
        "package-coordinate",
        "tag",
        "upstream-status",
        "selected-headers",
        "response-body-digest",
        "response-identity-digest",
    }
)
_RUNNER_DIAGNOSTIC_FIELDS = frozenset(
    {
        "exit-classification",
        "upstream-status",
        "exception-category",
        "request-correlation-digest",
    }
)
_REVIEWER_SOURCES = frozenset(
    {
        "unavailable-in-job-context",
        "on-demand-read-only-inspection",
    }
)
_DEPENDENCY_RESULTS = frozenset({"success", "failure", "cancelled", "skipped"})
_PROBE_RESULTS = frozenset({"success", "incomplete", "unknown"})
_MUTATION_CLASSIFICATIONS = frozenset({"complete", "incomplete", "unknown"})
_HTTP_STATUS_MIN = 100
_HTTP_STATUS_MAX = 599
_NPM_PUBLISH_OK_STATUS = 200
_NPM_PUBLISH_CREATED_STATUS = 201
_NPM_PUBLISH_SUCCESS_STATUSES = frozenset(
    {_NPM_PUBLISH_OK_STATUS, _NPM_PUBLISH_CREATED_STATUS}
)
_SHA_PATTERN = re.compile(r"[0-9a-f]{40}\Z")
_SHA256_PATTERN = re.compile(r"sha256:[0-9a-f]{64}\Z")
_SHA512_PATTERN = re.compile(r"sha512:[0-9a-f]{128}\Z")
_RUNNER_EXIT_CLASSIFICATIONS = frozenset(
    {
        "protocol-confirmed",
        "runner-failed-before-mutation",
        "runner-failed-after-action-start",
        "runner-failed-after-mutation-start",
        "runner-malformed-before-mutation",
    }
)
_LOCAL_RUNNER_EXCEPTION_CATEGORIES = frozenset(
    {"TimeoutError", "OSError", "RuntimeError", "ValueError"}
)
_UPSTREAM_TRANSPORT_CATEGORIES = frozenset(
    {"TimeoutError", "OSError", "HTTPException"}
)
_RUNNER_EXCEPTION_CATEGORIES = (
    _LOCAL_RUNNER_EXCEPTION_CATEGORIES | _UPSTREAM_TRANSPORT_CATEGORIES
)
_COMPLETE_SCENARIO_SEMANTICS = {
    "absent-create-readback": (
        "absent",
        True,
        True,
        frozenset({"created", "protocol-confirmed"}),
        "exact",
        (),
    ),
    "exact": (
        "exact",
        False,
        False,
        "exact-no-mutation",
        "exact",
        (),
    ),
    "identical-race": (
        "absent",
        True,
        True,
        "identical-race-exact",
        "exact",
        ("identical-race-exact",),
    ),
    "differing-race": (
        "absent",
        True,
        True,
        "differing-race-conflict",
        frozenset({"exact", "conflicting"}),
        ("conflicting-remote-bytes-or-tag",),
    ),
    "lost-response": (
        "absent",
        True,
        True,
        "lost-response-exact-after-start",
        "exact",
        ("mutation-started-and-readback-exact",),
    ),
}


def _exact(value: object, expected: type[object], *, field: str) -> None:
    if type(value) is not expected:
        message = f"{field} has the wrong runtime type"
        raise TypeError(message)


def _object(value: object, *, field: str) -> dict[str, JsonValue]:
    _exact(value, dict, field=field)
    return cast("dict[str, JsonValue]", value)


def _closed(
    document: dict[str, JsonValue],
    allowed: frozenset[str],
    *,
    field: str,
) -> None:
    unexpected = sorted(set(document) - allowed)
    if unexpected:
        message = f"{field} has unknown closed fields: {unexpected}"
        raise ValueError(message)
    missing = sorted(allowed - set(document))
    if missing:
        message = f"{field} is missing required fields: {missing}"
        raise ValueError(message)


def _closed_with_optional(
    document: dict[str, JsonValue],
    required: frozenset[str],
    optional: frozenset[str],
    *,
    field: str,
) -> None:
    unexpected = sorted(set(document) - required - optional)
    if unexpected:
        message = f"{field} has unknown closed fields: {unexpected}"
        raise ValueError(message)
    missing = sorted(required - set(document))
    if missing:
        message = f"{field} is missing required fields: {missing}"
        raise ValueError(message)


def _string(value: object, *, field: str) -> str:
    _exact(value, str, field=field)
    accepted = cast("str", value)
    if not accepted or accepted != accepted.strip():
        message = f"{field} must be a nonempty exact string"
        raise ValueError(message)
    return accepted


def _exact_string(value: object, expected: str, *, field: str) -> str:
    accepted = _string(value, field=field)
    if accepted != expected:
        message = f"{field} must be {expected!r}"
        raise ValueError(message)
    return accepted


def _choice(value: object, choices: frozenset[str], *, field: str) -> str:
    accepted = _string(value, field=field)
    if accepted not in choices:
        message = f"{field} has an invalid closed value"
        raise ValueError(message)
    return accepted


def _probe_result(value: object, *, field: str) -> str:
    accepted = _string(value, field=field)
    if accepted not in _PROBE_RESULTS:
        message = (
            f"{field} has an invalid closed value for "
            "mutation-classification consistency"
        )
        raise ValueError(message)
    return accepted


def _positive(value: object, *, field: str) -> int:
    _exact(value, int, field=field)
    accepted = cast("int", value)
    if accepted <= 0:
        message = f"{field} must be positive"
        raise ValueError(message)
    return accepted


def _boolean(value: object, *, field: str) -> bool:
    _exact(value, bool, field=field)
    return cast("bool", value)


def _sha(value: object, *, field: str) -> str:
    accepted = _string(value, field=field)
    if _SHA_PATTERN.fullmatch(accepted) is None:
        message = f"{field} must be 40 lowercase hexadecimal characters"
        raise ValueError(message)
    return accepted


def _require_complete_nonzero_sha(
    value: str,
    *,
    field: str,
    mutation_classification: str,
) -> None:
    if mutation_classification == "complete" and value == "0" * 40:
        message = f"complete acceptance evidence requires non-zero {field}"
        raise ValueError(message)


def _digest(value: object, *, field: str, sha512: bool = False) -> str:
    accepted = _string(value, field=field)
    pattern = _SHA512_PATTERN if sha512 else _SHA256_PATTERN
    if pattern.fullmatch(accepted) is None:
        algorithm = "SHA-512" if sha512 else "SHA-256"
        message = f"{field} must be a prefixed lowercase {algorithm}"
        raise ValueError(message)
    return accepted


def _diagnostics(value: object, *, field: str) -> tuple[str, ...]:
    _exact(value, list, field=field)
    return tuple(
        _string(item, field=f"{field}.{index}")
        for index, item in enumerate(cast("list[JsonValue]", value))
    )


def _require_complete_scenario_semantics(  # noqa: PLR0913
    scenario: str,
    *,
    pre_state: str,
    action_executed: bool,
    mutation_started: bool,
    response_result: str,
    post_state: str,
    diagnostics: tuple[str, ...],
    field: str,
) -> None:
    (
        expected_pre,
        expected_executed,
        expected_started,
        expected_result,
        expected_post,
        expected_diagnostics,
    ) = _COMPLETE_SCENARIO_SEMANTICS[scenario]
    checks = (
        (pre_state, expected_pre, "pre.state"),
        (action_executed, expected_executed, "action.executed"),
        (mutation_started, expected_started, "action.mutation-started"),
        (response_result, expected_result, "response.result"),
        (post_state, expected_post, "post.state"),
        (diagnostics, expected_diagnostics, "response.diagnostics"),
    )
    for actual, expected, name in checks:
        if (isinstance(expected, frozenset) and actual not in expected) or (
            not isinstance(expected, frozenset) and actual != expected
        ):
            message = f"{field}.{name} contradicts complete scenario semantics"
            raise ValueError(message)


def _admit_validated_request_proof(  # noqa: PLR0913
    value: object,
    *,
    package_coordinate: str,
    tag: str,
    response_identity_digest: str,
    content_sha512: str | None,
    field: str,
) -> dict[str, JsonValue]:
    proof = _object(value, field=field)
    _closed(proof, _VALIDATED_REQUEST_PROOF_FIELDS, field=field)
    _exact_string(
        proof["schema"],
        "workflow-delivery/v3/validated-acceptance-request-proof",
        field=f"{field}.schema",
    )
    _digest(proof["request-digest"], field=f"{field}.request-digest")
    proof_tarball_sha512 = _digest(
        proof["tarball-sha512"],
        field=f"{field}.tarball-sha512",
        sha512=True,
    )
    if content_sha512 is not None and proof_tarball_sha512 != content_sha512:
        message = f"{field}.tarball-sha512 does not match exact readback"
        raise ValueError(message)
    _exact_string(
        proof["package-coordinate"],
        package_coordinate,
        field=f"{field}.package-coordinate",
    )
    _exact_string(proof["tag"], tag, field=f"{field}.tag")
    status = _positive(
        proof["upstream-status"],
        field=f"{field}.upstream-status",
    )
    if status not in _NPM_PUBLISH_SUCCESS_STATUSES:
        message = (
            f"{field}.upstream-status must be an accepted npm publish status"
        )
        raise ValueError(message)
    selected_headers = _object(
        proof["selected-headers"],
        field=f"{field}.selected-headers",
    )
    for header_name, header_value in selected_headers.items():
        if (
            header_name != header_name.lower()
            or header_name not in {"content-type", "etag", "retry-after"}
            or type(header_value) is not str
        ):
            message = f"{field}.selected-headers are not closed"
            raise ValueError(message)
    _digest(
        proof["response-body-digest"],
        field=f"{field}.response-body-digest",
    )
    proof_response_identity = _digest(
        proof["response-identity-digest"],
        field=f"{field}.response-identity-digest",
    )
    expected_response_identity = canonical_sha256(
        cast(
            "JsonValue",
            {
                "request-digest": proof["request-digest"],
                "upstream-status": status,
                "selected-headers": selected_headers,
                "response-body-digest": proof["response-body-digest"],
            },
        )
    )
    if proof_response_identity != expected_response_identity:
        message = f"{field}.response-identity-digest is not exact"
        raise ValueError(message)
    if proof_response_identity != response_identity_digest:
        message = f"{field}.response-identity-digest does not match response"
        raise ValueError(message)
    return proof


def _validate_runner_diagnostic_shape(
    *,
    exit_classification: str,
    upstream_status: object,
    exception_category: object,
    request_correlation: object,
    field: str,
) -> None:
    if (
        upstream_status is None
        and exception_category is None
        and request_correlation is None
    ):
        message = f"{field} does not contain a diagnostic arm"
        raise ValueError(message)
    if exit_classification == "protocol-confirmed":
        if (
            upstream_status not in _NPM_PUBLISH_SUCCESS_STATUSES
            or exception_category is not None
            or request_correlation is None
        ):
            message = f"{field} contradicts protocol-confirmed semantics"
            raise ValueError(message)
        return
    request_bound = request_correlation is not None
    if upstream_status is not None and exception_category is not None:
        message = f"{field} mixes status and exception facts"
        raise ValueError(message)
    if (
        request_bound
        and upstream_status is None
        and exception_category not in _UPSTREAM_TRANSPORT_CATEGORIES
    ):
        message = f"{field} request-bound category is not transport"
        raise ValueError(message)
    if not request_bound and exception_category == "HTTPException":
        message = f"{field} HTTPException requires request binding"
        raise ValueError(message)
    if (
        not request_bound
        and upstream_status is not None
        and upstream_status != _NPM_PUBLISH_CREATED_STATUS
    ):
        message = f"{field} unbound status is not historically admissible"
        raise ValueError(message)


def _admit_runner_diagnostic(
    value: object,
    *,
    field: str,
) -> dict[str, JsonValue]:
    diagnostic = _object(value, field=field)
    _closed(diagnostic, _RUNNER_DIAGNOSTIC_FIELDS, field=field)
    exit_classification = _choice(
        diagnostic["exit-classification"],
        _RUNNER_EXIT_CLASSIFICATIONS,
        field=f"{field}.exit-classification",
    )
    upstream_status = diagnostic["upstream-status"]
    if upstream_status is not None:
        _exact(upstream_status, int, field=f"{field}.upstream-status")
        exact_upstream_status = cast("int", upstream_status)
        if not (_HTTP_STATUS_MIN <= exact_upstream_status <= _HTTP_STATUS_MAX):
            message = f"{field}.upstream-status is not closed"
            raise ValueError(message)
    exception_category = diagnostic["exception-category"]
    if exception_category is not None:
        _choice(
            exception_category,
            _RUNNER_EXCEPTION_CATEGORIES,
            field=f"{field}.exception-category",
        )
    request_correlation = diagnostic["request-correlation-digest"]
    if request_correlation is not None:
        _digest(
            request_correlation,
            field=f"{field}.request-correlation-digest",
        )
    _validate_runner_diagnostic_shape(
        exit_classification=exit_classification,
        upstream_status=upstream_status,
        exception_category=exception_category,
        request_correlation=request_correlation,
        field=field,
    )
    return diagnostic


def _canonical_suite_digest(
    *,
    probe: str,
    inventory: tuple[str, ...],
    scenarios: list[dict[str, JsonValue]],
) -> str:
    suite = probe.removeprefix("probe-")
    classifications = {
        cast("str", scenario["mutation-classification"])
        for scenario in scenarios
    }
    mutation_classification = (
        "unknown"
        if "unknown" in classifications
        else "incomplete"
        if "incomplete" in classifications
        else "complete"
    )
    document: dict[str, JsonValue] = {
        "schema": "workflow-delivery/v3/fixed-acceptance-suite",
        "suite": suite,
        "scenario-inventory": cast("list[JsonValue]", list(inventory)),
        "scenarios": cast("list[JsonValue]", scenarios),
        "mutation-classification": mutation_classification,
        "result": (
            "success"
            if mutation_classification == "complete"
            else mutation_classification
        ),
    }
    return canonical_sha256(document)


@dataclass(frozen=True, slots=True)
class GovernanceAcceptanceWorkflow:
    """Governance Acceptance Evidence workflow source identity."""

    repository: str
    path: str
    ref: str
    sha: str

    def to_document(self) -> dict[str, JsonValue]:
        """Return the canonical JSON object for this workflow identity."""
        return {
            "repository": self.repository,
            "path": self.path,
            "ref": self.ref,
            "sha": self.sha,
        }


@dataclass(frozen=True, slots=True)
class GovernanceAcceptanceReviewer:
    """Recorded environment reviewer identity, when available."""

    login: str | None
    source: str

    def to_document(self) -> dict[str, JsonValue]:
        """Return the canonical JSON object for this reviewer record."""
        return {
            "login": self.login,
            "source": self.source,
        }


@dataclass(frozen=True, slots=True)
class GovernanceAcceptanceRecovery:
    """Coordinates for optional read-only reviewer recovery inspection."""

    workflow_run_id: int
    environment: str
    deployment: str
    job: str
    artifact_id: int | None

    def to_document(self) -> dict[str, JsonValue]:
        """Return the canonical JSON object for these recovery coordinates."""
        return {
            "workflow-run-id": self.workflow_run_id,
            "environment": self.environment,
            "deployment": self.deployment,
            "job": self.job,
            "artifact-id": self.artifact_id,
        }


@dataclass(frozen=True, slots=True)
class GovernanceDependencyResult:
    """Closed dependency job terminal result."""

    job: str
    result: str

    def to_document(self) -> dict[str, JsonValue]:
        """Return the canonical JSON object for this dependency result."""
        return {"job": self.job, "result": self.result}


@dataclass(frozen=True, slots=True)
class GovernanceProbeFact:
    """Closed Governance destination probe fact."""

    probe: str
    result: str
    scenario_inventory: tuple[str, ...]
    record_digest: str | None
    artifact_id: int | None
    artifact_digest: str | None
    scenarios: tuple[dict[str, JsonValue], ...]

    def to_document(self) -> dict[str, JsonValue]:
        """Return the canonical JSON object for this probe fact."""
        return {
            "probe": self.probe,
            "result": self.result,
            "scenario-inventory": cast(
                "list[JsonValue]",
                list(self.scenario_inventory),
            ),
            "record-digest": self.record_digest,
            "artifact-id": self.artifact_id,
            "artifact-digest": self.artifact_digest,
            "scenarios": cast("list[JsonValue]", list(self.scenarios)),
        }


@dataclass(frozen=True, slots=True)
class GovernanceAcceptanceEvidence:
    """Strict Governance Acceptance Evidence outside Release lineage."""

    workflow: GovernanceAcceptanceWorkflow
    target_sha: str
    package_coordinate: str
    confirmation_digest: str
    environment: str
    reviewer_record: GovernanceAcceptanceReviewer
    recovery: GovernanceAcceptanceRecovery
    dependency_results: tuple[GovernanceDependencyResult, ...]
    probe_facts: tuple[GovernanceProbeFact, ...]
    mutation_classification: str
    producer: str
    workflow_run_id: int
    run_attempt: int

    @property
    def evidence_digest(self) -> str:
        """Return the canonical SHA-256 digest of this evidence record."""
        return canonical_sha256(self.to_document())

    @property
    def reviewer(self) -> str | None:
        """Return the admitted environment reviewer login, when available."""
        return self.reviewer_record.login

    @property
    def reviewer_source(self) -> str:
        """Return the admitted reviewer source."""
        return self.reviewer_record.source

    def to_document(self) -> dict[str, JsonValue]:
        """Return the canonical JSON object for this evidence record."""
        return {
            "schema": GOVERNANCE_ACCEPTANCE_EVIDENCE_SCHEMA,
            "purpose": GOVERNANCE_ACCEPTANCE_EVIDENCE_PURPOSE,
            "workflow": self.workflow.to_document(),
            "target-sha": self.target_sha,
            "package-coordinate": self.package_coordinate,
            "confirmation-digest": self.confirmation_digest,
            "environment": self.environment,
            "reviewer": self.reviewer_record.to_document(),
            "recovery": self.recovery.to_document(),
            "dependency-results": [
                result.to_document() for result in self.dependency_results
            ],
            "probe-facts": [fact.to_document() for fact in self.probe_facts],
            "mutation-classification": self.mutation_classification,
            "producer": self.producer,
            "workflow-run-id": self.workflow_run_id,
            "run-attempt": self.run_attempt,
            "release-lineage": GOVERNANCE_ACCEPTANCE_EVIDENCE_RELEASE_LINEAGE,
        }


def _admit_workflow(
    value: object,
    *,
    profile: _GovernanceAcceptanceProfile,
) -> GovernanceAcceptanceWorkflow:
    document = _object(value, field="workflow")
    _closed(document, _WORKFLOW_FIELDS, field="workflow")
    return GovernanceAcceptanceWorkflow(
        repository=_exact_string(
            document["repository"],
            GOVERNANCE_ACCEPTANCE_REPOSITORY,
            field="workflow.repository",
        ),
        path=_exact_string(
            document["path"],
            profile.workflow_path,
            field="workflow.path",
        ),
        ref=_exact_string(
            document["ref"],
            GOVERNANCE_ACCEPTANCE_REF,
            field="workflow.ref",
        ),
        sha=_sha(document["sha"], field="workflow.sha"),
    )


def _admit_reviewer(value: object) -> GovernanceAcceptanceReviewer:
    document = _object(value, field="reviewer")
    _closed(document, _REVIEWER_FIELDS, field="reviewer")
    login_value = document["login"]
    source = _choice(
        document["source"],
        _REVIEWER_SOURCES,
        field="reviewer.source",
    )
    if source == "unavailable-in-job-context":
        if login_value is not None:
            message = "reviewer unavailable source requires null reviewer login"
            raise ValueError(message)
        login = None
    else:
        login = _string(login_value, field="reviewer.login")
    return GovernanceAcceptanceReviewer(login=login, source=source)


def _admit_recovery(
    value: object,
    *,
    allow_missing_artifact: bool,
) -> GovernanceAcceptanceRecovery:
    document = _object(value, field="recovery")
    _closed(document, _RECOVERY_FIELDS, field="recovery")
    artifact_value = document["artifact-id"]
    artifact_id = (
        None
        if artifact_value is None and allow_missing_artifact
        else _positive(artifact_value, field="recovery.artifact-id")
    )
    return GovernanceAcceptanceRecovery(
        workflow_run_id=_positive(
            document["workflow-run-id"],
            field="recovery.workflow-run-id",
        ),
        environment=_string(
            document["environment"],
            field="recovery.environment",
        ),
        deployment=_string(document["deployment"], field="recovery.deployment"),
        job=_string(document["job"], field="recovery.job"),
        artifact_id=artifact_id,
    )


def _admit_dependency_results(
    value: object,
) -> tuple[GovernanceDependencyResult, ...]:
    _exact(value, list, field="dependency-results")
    entries = cast("list[JsonValue]", value)
    if len(entries) != len(GOVERNANCE_ACCEPTANCE_DEPENDENCIES):
        message = "dependency-results has invalid required cardinality"
        raise ValueError(message)
    results: list[GovernanceDependencyResult] = []
    for index, (entry, expected_job) in enumerate(
        zip(entries, GOVERNANCE_ACCEPTANCE_DEPENDENCIES, strict=True)
    ):
        document = _object(entry, field=f"dependency-results.{index}")
        _closed(
            document,
            _DEPENDENCY_FIELDS,
            field=f"dependency-results.{index}",
        )
        job = _string(document["job"], field=f"dependency-results.{index}.job")
        if job != expected_job:
            message = (
                "dependency-results required inventory order contains "
                f"unexpected job: {job!r}"
            )
            raise ValueError(message)
        results.append(
            GovernanceDependencyResult(
                job=job,
                result=_choice(
                    document["result"],
                    _DEPENDENCY_RESULTS,
                    field=f"dependency-results.{index}.result",
                ),
            )
        )
    return tuple(results)


def _proof_content_binding(
    *,
    profile: _GovernanceAcceptanceProfile,
    response_result: str,
    post_content_sha512: str | None,
) -> str | None:
    if response_result == "protocol-confirmed":
        return post_content_sha512
    if response_result != "lost-response-exact-after-start":
        return None
    # Retry 1 predates exact lost-response request/readback byte binding.
    if profile.package_coordinate == GOVERNANCE_ACCEPTANCE_PACKAGE_COORDINATE:
        return None
    return post_content_sha512


def _admit_probe_facts(  # noqa: C901, PLR0912, PLR0915
    value: object,
    *,
    profile: _GovernanceAcceptanceProfile,
) -> tuple[GovernanceProbeFact, ...]:
    _exact(value, list, field="probe-facts")
    entries = cast("list[JsonValue]", value)
    if len(entries) != len(GOVERNANCE_ACCEPTANCE_PROBES):
        message = "probe-facts has invalid required cardinality"
        raise ValueError(message)
    results: list[GovernanceProbeFact] = []
    for index, (entry, expected_probe) in enumerate(
        zip(entries, GOVERNANCE_ACCEPTANCE_PROBES, strict=True)
    ):
        document = _object(entry, field=f"probe-facts.{index}")
        _closed(document, _PROBE_FIELDS, field=f"probe-facts.{index}")
        probe = _string(document["probe"], field=f"probe-facts.{index}.probe")
        if probe != expected_probe:
            message = (
                "probe-facts required inventory order contains "
                f"unexpected probe: {probe!r}"
            )
            raise ValueError(message)
        expected_inventory = GOVERNANCE_ACCEPTANCE_PROBE_SCENARIOS[probe]
        inventory_value = document["scenario-inventory"]
        _exact(
            inventory_value,
            list,
            field=f"probe-facts.{index}.scenario-inventory",
        )
        inventory = tuple(
            _string(
                scenario,
                field=f"probe-facts.{index}.scenario-inventory.{scenario_index}",
            )
            for scenario_index, scenario in enumerate(
                cast("list[JsonValue]", inventory_value)
            )
        )
        if inventory != expected_inventory:
            message = "probe scenario inventory is not the exact bound suite"
            raise ValueError(message)
        scenarios_value = document["scenarios"]
        _exact(scenarios_value, list, field=f"probe-facts.{index}.scenarios")
        scenario_entries = cast("list[JsonValue]", scenarios_value)
        probe_result = _probe_result(
            document["result"],
            field=f"probe-facts.{index}.result",
        )
        if len(scenario_entries) not in {0, len(expected_inventory)}:
            message = "probe scenario evidence cardinality is not exact"
            raise ValueError(message)
        if probe_result == "success" and len(scenario_entries) != len(
            expected_inventory
        ):
            message = "successful probe requires every bound scenario record"
            raise ValueError(message)
        scenarios: list[dict[str, JsonValue]] = []
        derived_probe_result: str | None = None
        scenario_pairs = (
            zip(scenario_entries, expected_inventory, strict=True)
            if scenario_entries
            else ()
        )
        for scenario_index, (scenario_value, expected_scenario) in enumerate(
            scenario_pairs
        ):
            scenario_field = f"probe-facts.{index}.scenarios.{scenario_index}"
            scenario_document = _object(scenario_value, field=scenario_field)
            _closed_with_optional(
                scenario_document,
                _SCENARIO_FIELDS,
                frozenset({"validated-request-proof", "runner-diagnostic"}),
                field=scenario_field,
            )
            _exact_string(
                scenario_document["scenario"],
                expected_scenario,
                field=f"{scenario_field}.scenario",
            )
            expected_coordinate, expected_tag = profile.coordinates()[
                expected_scenario
            ]
            _exact_string(
                scenario_document["package-coordinate"],
                expected_coordinate,
                field=f"{scenario_field}.package-coordinate",
            )
            _exact_string(
                scenario_document["tag"],
                expected_tag,
                field=f"{scenario_field}.tag",
            )
            scenario_classification = _choice(
                scenario_document["mutation-classification"],
                _MUTATION_CLASSIFICATIONS,
                field=f"{scenario_field}.mutation-classification",
            )
            pre = _object(
                scenario_document["pre"], field=f"{scenario_field}.pre"
            )
            _closed(pre, _PRE_FIELDS, field=f"{scenario_field}.pre")
            pre_state = _string(
                pre["state"], field=f"{scenario_field}.pre.state"
            )
            action = _object(
                scenario_document["action"],
                field=f"{scenario_field}.action",
            )
            _closed(action, _ACTION_FIELDS, field=f"{scenario_field}.action")
            _exact_string(
                action["operation"],
                "npm-publish-create-only",
                field=f"{scenario_field}.action.operation",
            )
            action_executed = _boolean(
                action["executed"],
                field=f"{scenario_field}.action.executed",
            )
            mutation_started = _boolean(
                action["mutation-started"],
                field=f"{scenario_field}.action.mutation-started",
            )
            response = _object(
                scenario_document["response"],
                field=f"{scenario_field}.response",
            )
            _closed(
                response,
                _RESPONSE_FIELDS,
                field=f"{scenario_field}.response",
            )
            response_result = _string(
                response["result"], field=f"{scenario_field}.response.result"
            )
            _digest(
                response["identity-digest"],
                field=f"{scenario_field}.response.identity-digest",
            )
            response_identity_digest = cast(
                "str",
                response["identity-digest"],
            )
            diagnostics = _diagnostics(
                response["diagnostics"],
                field=f"{scenario_field}.response.diagnostics",
            )
            post = _object(
                scenario_document["post"],
                field=f"{scenario_field}.post",
            )
            _closed(post, _POST_FIELDS, field=f"{scenario_field}.post")
            post_state = _string(
                post["state"], field=f"{scenario_field}.post.state"
            )
            post_content_sha512 = _digest(
                post["content-sha512"],
                field=f"{scenario_field}.post.content-sha512",
                sha512=True,
            )
            proof = scenario_document.get("validated-request-proof")
            admitted_proof: dict[str, JsonValue] | None = None
            if proof is not None:
                if response_result not in {
                    "lost-response-exact-after-start",
                    "protocol-confirmed",
                    "protocol-confirmed-readback-incomplete",
                }:
                    message = (
                        f"{scenario_field}.validated-request-proof is not "
                        "permitted for this response.result"
                    )
                    raise ValueError(message)
                admitted_proof = _admit_validated_request_proof(
                    proof,
                    package_coordinate=expected_coordinate,
                    tag=expected_tag,
                    response_identity_digest=response_identity_digest,
                    content_sha512=_proof_content_binding(
                        profile=profile,
                        response_result=response_result,
                        post_content_sha512=post_content_sha512,
                    ),
                    field=f"{scenario_field}.validated-request-proof",
                )
            elif response_result in {
                "lost-response-exact-after-start",
                "protocol-confirmed",
                "protocol-confirmed-readback-incomplete",
            }:
                message = (
                    f"{scenario_field}.validated-request-proof is required "
                    "for proof-bound completion"
                )
                raise ValueError(message)
            if response_result == "protocol-confirmed" and (
                post_state != "exact" or scenario_classification != "complete"
            ):
                message = (
                    f"{scenario_field}.response.result requires an exact "
                    "complete readback"
                )
                raise ValueError(message)
            if (
                response_result == "protocol-confirmed-readback-incomplete"
                and scenario_classification != "incomplete"
            ):
                message = (
                    f"{scenario_field}.response.result requires incomplete "
                    "mutation classification"
                )
                raise ValueError(message)
            if response_result == "protocol-confirmed-readback-incomplete" and (
                not action_executed or not mutation_started
            ):
                message = (
                    f"{scenario_field}.response.result requires admitted "
                    "execution and mutation startedness"
                )
                raise ValueError(message)
            admitted_runner_diagnostic = None
            if "runner-diagnostic" in scenario_document:
                admitted_runner_diagnostic = _admit_runner_diagnostic(
                    scenario_document["runner-diagnostic"],
                    field=f"{scenario_field}.runner-diagnostic",
                )
            if admitted_runner_diagnostic is not None:
                diagnostic_status = admitted_runner_diagnostic[
                    "upstream-status"
                ]
                diagnostic_request = admitted_runner_diagnostic[
                    "request-correlation-digest"
                ]
                if diagnostic_request is not None and (
                    not action_executed or not mutation_started
                ):
                    message = (
                        f"{scenario_field}.runner-diagnostic request binding "
                        "contradicts action"
                    )
                    raise ValueError(message)
                if (
                    admitted_runner_diagnostic["exit-classification"]
                    == "protocol-confirmed"
                    and admitted_proof is None
                ):
                    message = (
                        f"{scenario_field}.runner-diagnostic "
                        "protocol-confirmed requires "
                        "validated-request-proof"
                    )
                    raise ValueError(message)
                if admitted_proof is not None and (
                    (
                        diagnostic_status is not None
                        and diagnostic_status
                        != admitted_proof["upstream-status"]
                    )
                    or (
                        diagnostic_request is not None
                        and diagnostic_request
                        != admitted_proof["request-digest"]
                    )
                ):
                    message = (
                        f"{scenario_field}.runner-diagnostic does not bind "
                        "the validated-request-proof"
                    )
                    raise ValueError(message)
                if (
                    admitted_proof is None
                    and diagnostic_status is not None
                    and diagnostic_request is None
                ):
                    message = (
                        f"{scenario_field}.runner-diagnostic status is "
                        "not request-bound"
                    )
                    raise ValueError(message)
                if (
                    admitted_proof is None
                    and diagnostic_request is not None
                    and scenario_classification == "complete"
                ):
                    message = (
                        f"{scenario_field}.runner-diagnostic request-bound "
                        "facts are non-authoritative"
                    )
                    raise ValueError(message)
            if admitted_runner_diagnostic is not None:
                diagnostic_exception = admitted_runner_diagnostic[
                    "exception-category"
                ]
                diagnostic_startedness = {
                    "protocol-confirmed": (True, True),
                    "runner-failed-before-mutation": (False, False),
                    "runner-failed-after-action-start": (True, False),
                    "runner-failed-after-mutation-start": (True, True),
                    "runner-malformed-before-mutation": (False, False),
                }
                expected_diagnostic_startedness = diagnostic_startedness[
                    cast(
                        "str",
                        admitted_runner_diagnostic["exit-classification"],
                    )
                ]
                if (
                    action_executed,
                    mutation_started,
                ) != expected_diagnostic_startedness:
                    message = (
                        f"{scenario_field}.runner-diagnostic startedness "
                        "contradicts action"
                    )
                    raise ValueError(message)
                if (
                    admitted_proof is not None
                    and diagnostic_exception is not None
                ):
                    message = (
                        f"{scenario_field}.runner-diagnostic does not bind "
                        "the validated-request-proof"
                    )
                    raise ValueError(message)
            if mutation_started and not action_executed:
                message = (
                    f"{scenario_field}.action.executed and "
                    "action.mutation-started startedness contradict execution"
                )
                raise ValueError(message)
            runner_failure_startedness = {
                "runner-failed-before-mutation": (False, False),
                "runner-failed-after-action-start": (True, False),
                "runner-failed-after-mutation-start": (True, True),
            }
            expected_startedness = runner_failure_startedness.get(
                response_result
            )
            if (
                expected_startedness is not None
                and (
                    action_executed,
                    mutation_started,
                )
                != expected_startedness
            ):
                message = (
                    f"{scenario_field}.action startedness contradicts "
                    f"{response_result}"
                )
                raise ValueError(message)
            if (
                response_result == "runner-malformed-before-mutation"
                and mutation_started
            ):
                message = (
                    f"{scenario_field}.action startedness contradicts "
                    "runner-malformed-before-mutation"
                )
                raise ValueError(message)
            if response_result in {"timeout", "lost-response"} and (
                not action_executed or not mutation_started
            ):
                message = (
                    f"{scenario_field}.action startedness contradicts "
                    "a qualified-request ambiguous outcome"
                )
                raise ValueError(message)
            if (
                probe_result == "success"
                and scenario_classification != "complete"
            ):
                message = (
                    f"{scenario_field}.mutation-classification "
                    "contradicts successful probe semantics"
                )
                raise ValueError(message)
            if scenario_classification == "complete":
                _require_complete_scenario_semantics(
                    expected_scenario,
                    pre_state=pre_state,
                    action_executed=action_executed,
                    mutation_started=mutation_started,
                    response_result=response_result,
                    post_state=post_state,
                    diagnostics=diagnostics,
                    field=scenario_field,
                )
            scenarios.append(scenario_document)
        if scenarios:
            classifications = {
                cast("str", scenario["mutation-classification"])
                for scenario in scenarios
            }
            derived_probe_result = (
                "unknown"
                if "unknown" in classifications
                else "incomplete"
                if "incomplete" in classifications
                else "success"
            )
        record_digest_value = document["record-digest"]
        artifact_id_value = document["artifact-id"]
        artifact_digest_value = document["artifact-digest"]
        record_digest = (
            None
            if record_digest_value is None
            else _digest(
                record_digest_value,
                field=f"probe-facts.{index}.record-digest",
            )
        )
        artifact_id = (
            None
            if artifact_id_value is None
            else _positive(
                artifact_id_value,
                field=f"probe-facts.{index}.artifact-id",
            )
        )
        artifact_digest = (
            None
            if artifact_digest_value is None
            else _digest(
                artifact_digest_value,
                field=f"probe-facts.{index}.artifact-digest",
            )
        )
        if probe_result == "success" and (
            record_digest is None
            or artifact_id is None
            or artifact_digest is None
        ):
            message = (
                "complete acceptance evidence requires immutable suite "
                "record and artifact bindings"
            )
            raise ValueError(message)
        if (
            derived_probe_result is not None
            and probe_result != derived_probe_result
        ):
            artifact_outputs_missing = (
                artifact_id is None or artifact_digest is None
            )
            if not (
                probe_result == "incomplete"
                and derived_probe_result == "success"
                and artifact_outputs_missing
            ):
                message = (
                    f"probe-facts.{index}.result is not consistent with "
                    "admitted scenario classifications"
                )
                raise ValueError(message)
        if scenarios:
            expected_record_digest = _canonical_suite_digest(
                probe=probe,
                inventory=inventory,
                scenarios=scenarios,
            )
            if record_digest != expected_record_digest:
                message = (
                    f"probe-facts.{index}.record-digest does not match the "
                    "canonical scenario suite digest"
                )
                raise ValueError(message)
        results.append(
            GovernanceProbeFact(
                probe=probe,
                result=probe_result,
                scenario_inventory=inventory,
                record_digest=record_digest,
                artifact_id=artifact_id,
                artifact_digest=artifact_digest,
                scenarios=tuple(scenarios),
            )
        )
    return tuple(results)


def _derived_mutation_classification(
    dependency_results: tuple[GovernanceDependencyResult, ...],
    probe_facts: tuple[GovernanceProbeFact, ...],
    *,
    review_artifact_id: int | None,
) -> str:
    if any(
        result.job in GOVERNANCE_ACCEPTANCE_PROBES
        and result.result in {"failure", "cancelled"}
        for result in dependency_results
    ):
        return "unknown"
    if any(fact.result == "unknown" for fact in probe_facts):
        return "unknown"
    if (
        review_artifact_id is None
        or any(result.result != "success" for result in dependency_results)
        or any(fact.result != "success" for fact in probe_facts)
    ):
        return "incomplete"
    return "complete"


def _require_zero_target_rejected_dispatch(
    evidence: GovernanceAcceptanceEvidence,
) -> None:
    if evidence.target_sha != "0" * 40:
        return
    expected_dependencies = (
        ("validate-fixed-inputs", "failure"),
        ("acceptance-review", "skipped"),
        ("probe-absent-create-readback", "skipped"),
        ("probe-exact-and-conflict", "skipped"),
    )
    actual_dependencies = tuple(
        (result.job, result.result) for result in evidence.dependency_results
    )
    probes_are_empty = all(
        fact.result == "incomplete"
        and fact.record_digest is None
        and fact.artifact_id is None
        and fact.artifact_digest is None
        and not fact.scenarios
        for fact in evidence.probe_facts
    )
    if (
        evidence.mutation_classification != "incomplete"
        or actual_dependencies != expected_dependencies
        or evidence.recovery.artifact_id is not None
        or evidence.reviewer_record.login is not None
        or evidence.reviewer_record.source != "unavailable-in-job-context"
        or not probes_are_empty
    ):
        message = (
            "zero target-sha requires exact rejected fixed-input dispatch "
            "evidence"
        )
        raise ValueError(message)


def admit_governance_acceptance_evidence(  # noqa: C901, PLR0912, PLR0915
    content: bytes | bytearray,
) -> GovernanceAcceptanceEvidence:
    """Admit strict canonical Governance Acceptance Evidence bytes."""
    document = parse_canonical_json(content)
    _closed(document, _TOP_LEVEL_FIELDS, field="governance acceptance evidence")

    _exact_string(
        document["schema"],
        GOVERNANCE_ACCEPTANCE_EVIDENCE_SCHEMA,
        field="schema",
    )
    _exact_string(
        document["purpose"],
        GOVERNANCE_ACCEPTANCE_EVIDENCE_PURPOSE,
        field="purpose",
    )
    _exact_string(
        document["release-lineage"],
        GOVERNANCE_ACCEPTANCE_EVIDENCE_RELEASE_LINEAGE,
        field="release-lineage",
    )
    profile = _acceptance_profile(document["package-coordinate"])
    dependency_results = _admit_dependency_results(
        document["dependency-results"]
    )
    probe_facts = _admit_probe_facts(
        document["probe-facts"],
        profile=profile,
    )
    retained_probes = {
        fact.probe for fact in probe_facts if fact.record_digest is not None
    }
    for dependency in dependency_results:
        if dependency.result == "success":
            continue
        if dependency.job not in GOVERNANCE_ACCEPTANCE_PROBES:
            contradictory_probes = retained_probes
        elif dependency.job == GOVERNANCE_ACCEPTANCE_PROBES[0]:
            contradictory_probes = retained_probes - (
                {dependency.job}
                if dependency.result in {"failure", "cancelled"}
                else set()
            )
        else:
            contradictory_probes = (
                {dependency.job}
                if dependency.job in retained_probes
                and dependency.result == "skipped"
                else set()
            )
        if contradictory_probes:
            message = (
                f"{dependency.job} dependency result {dependency.result!r} "
                "cannot retain suite records for "
                f"{sorted(contradictory_probes)!r}"
            )
            raise ValueError(message)
    recovery = _admit_recovery(
        document["recovery"],
        allow_missing_artifact=True,
    )
    mutation_classification = _choice(
        document["mutation-classification"],
        _MUTATION_CLASSIFICATIONS,
        field="mutation-classification",
    )
    expected_classification = _derived_mutation_classification(
        dependency_results,
        probe_facts,
        review_artifact_id=recovery.artifact_id,
    )
    if mutation_classification != expected_classification:
        message = (
            "mutation-classification is not consistent with dependency "
            "and probe facts"
        )
        raise ValueError(message)
    scenario_inventory = tuple(
        scenario for fact in probe_facts for scenario in fact.scenario_inventory
    )
    if scenario_inventory != GOVERNANCE_ACCEPTANCE_SCENARIOS:
        message = "complete acceptance scenario inventory is not exact"
        raise ValueError(message)
    if mutation_classification == "complete" and any(
        fact.record_digest is None
        or fact.artifact_id is None
        or fact.artifact_digest is None
        for fact in probe_facts
    ):
        message = (
            "complete acceptance evidence requires immutable suite record "
            "and artifact bindings"
        )
        raise ValueError(message)

    admitted = GovernanceAcceptanceEvidence(
        workflow=_admit_workflow(document["workflow"], profile=profile),
        target_sha=_sha(document["target-sha"], field="target-sha"),
        package_coordinate=_exact_string(
            document["package-coordinate"],
            profile.package_coordinate,
            field="package-coordinate",
        ),
        confirmation_digest=_exact_string(
            document["confirmation-digest"],
            profile.confirmation_digest,
            field="confirmation-digest",
        ),
        environment=_exact_string(
            document["environment"],
            profile.environment,
            field="environment",
        ),
        reviewer_record=_admit_reviewer(document["reviewer"]),
        recovery=recovery,
        dependency_results=dependency_results,
        probe_facts=probe_facts,
        mutation_classification=mutation_classification,
        producer=_exact_string(
            document["producer"],
            GOVERNANCE_ACCEPTANCE_PRODUCER,
            field="producer",
        ),
        workflow_run_id=_positive(
            document["workflow-run-id"],
            field="workflow-run-id",
        ),
        run_attempt=_positive(document["run-attempt"], field="run-attempt"),
    )
    if admitted.target_sha not in {"0" * 40, profile.target_sha}:
        message = "target-sha does not match the reviewed acceptance profile"
        raise ValueError(message)
    _require_zero_target_rejected_dispatch(admitted)
    _require_complete_nonzero_sha(
        admitted.target_sha,
        field="target-sha",
        mutation_classification=admitted.mutation_classification,
    )
    _require_complete_nonzero_sha(
        admitted.workflow.sha,
        field="workflow.sha",
        mutation_classification=admitted.mutation_classification,
    )
    if admitted.run_attempt != 1:
        message = "run-attempt must be exactly 1"
        raise ValueError(message)
    if admitted.recovery.workflow_run_id != admitted.workflow_run_id:
        message = "recovery workflow-run-id must match workflow-run-id"
        raise ValueError(message)
    if admitted.recovery.environment != admitted.environment:
        message = "recovery environment must match acceptance environment"
        raise ValueError(message)
    if admitted.recovery.job != "acceptance-review":
        message = "recovery job must be acceptance-review"
        raise ValueError(message)
    expected_deployment = (
        f"run:{admitted.workflow_run_id}/environment:acceptance"
    )
    if admitted.recovery.deployment != expected_deployment:
        message = "recovery deployment must match the acceptance run"
        raise ValueError(message)
    if admitted.to_document() != document:
        message = "governance acceptance evidence contains noncanonical values"
        raise ValueError(message)
    return admitted
