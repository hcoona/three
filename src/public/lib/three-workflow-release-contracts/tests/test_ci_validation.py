"""CI affected-validation contract foundation tests."""

from __future__ import annotations

import hashlib
import re

import pytest
from three_workflow_release_contracts import (
    API_VERSIONS_BY_KIND,
    ARTIFACT_PHYSICAL_NAME_LENGTH,
    ARTIFACT_PHYSICAL_NAME_PREFIX,
    DETAILS_BY_DIAGNOSTIC_CODE,
    REGISTERED_CI_VALIDATION_DIAGNOSTIC_CODES,
    REGISTERED_CI_VALIDATION_DIAGNOSTIC_DETAILS,
    REGISTERED_CI_VALIDATION_DIAGNOSTIC_FAMILIES,
    REGISTERED_CI_VALIDATION_KINDS,
    CiValidationKind,
    ContractValidationError,
    DiagnosticDetail,
    DiagnosticFamily,
    DiagnosticSeverity,
    DiagnosticVerdictEffect,
    artifact_physical_name,
    artifact_physical_ref,
    canonical_json_bytes,
    canonical_json_digest,
    validate_artifact_logical_ref,
    validate_artifact_physical_name,
    validate_common_envelope,
)


def test_canonical_json_digest_is_stable_across_member_order() -> None:
    """Hash semantically equal objects to the same canonical digest."""
    first = {
        "z": [3, {"b": False, "a": None}],
        "a": "value",
        "nested": {"two": 2, "one": 1},
    }
    second = {
        "nested": {"one": 1, "two": 2},
        "a": "value",
        "z": [3, {"a": None, "b": False}],
    }

    assert canonical_json_bytes(first) == canonical_json_bytes(second)
    assert canonical_json_digest(first) == canonical_json_digest(second)
    assert canonical_json_bytes(first).decode("utf-8") == (
        '{"a":"value","nested":{"one":1,"two":2},"z":[3,{"a":null,"b":false}]}'
    )


def test_canonical_json_rejects_unsupported_numbers() -> None:
    """Do not pretend to support RFC 8785 float serialization."""
    with pytest.raises(TypeError):
        canonical_json_bytes({"float": 1.25})
    with pytest.raises(ValueError, match="I-JSON safe range"):
        canonical_json_bytes({"too-large": 9_007_199_254_740_992})


def test_artifact_physical_name_format_length_and_determinism() -> None:
    """Map logical refs to attempt-visible deterministic physical names."""
    logical_ref = "ci-validation/planning/123/4/validation-plan.json"
    physical_name = artifact_physical_name(logical_ref)
    expected_digest = hashlib.sha256(logical_ref.encode("utf-8")).hexdigest()

    assert physical_name == (
        f"{ARTIFACT_PHYSICAL_NAME_PREFIX}123-4-{expected_digest}"
    )
    assert ARTIFACT_PHYSICAL_NAME_LENGTH is None
    assert re.fullmatch(
        r"three-ci-validation-123-4-[0-9a-f]{64}",
        physical_name,
    )
    assert artifact_physical_name(logical_ref) == physical_name
    assert artifact_physical_ref(logical_ref).physical_name == physical_name
    validate_artifact_physical_name(physical_name)


@pytest.mark.parametrize(
    "logical_ref",
    [
        "",
        "release/planning/123/4/validation-plan.json",
        "ci-validation/planning//4/validation-plan.json",
        "ci-validation/planning/../4/validation-plan.json",
        "ci-validation/planning/123/4/validation plan.json",
        "ci-validation/planning/123/4/",
    ],
)
def test_artifact_logical_ref_rejects_noncanonical_refs(
    logical_ref: str,
) -> None:
    """Keep logical refs canonical before deriving physical names."""
    with pytest.raises(ContractValidationError):
        validate_artifact_logical_ref(logical_ref)
    with pytest.raises(ContractValidationError):
        artifact_physical_name(logical_ref)


@pytest.mark.parametrize(
    "physical_name",
    [
        "three-ci-validation-abc",
        "three-ci-validation-" + "A" * 64,
        "release-ci-validation-" + "0" * 64,
        "three-ci-validation-" + "0" * 63,
        "three-ci-validation-" + "0" * 64,
        "three-ci-validation-123-4-" + "0" * 63,
        "three-ci-validation-.-4-" + "0" * 64,
        "three-ci-validation-..-4-" + "0" * 64,
        "three-ci-validation-123-.-" + "0" * 64,
        "three-ci-validation-123-..-" + "0" * 64,
    ],
)
def test_artifact_physical_name_rejects_bad_names(
    physical_name: str,
) -> None:
    """Validate the exact prefixed lowercase SHA-256 physical format."""
    with pytest.raises(ContractValidationError):
        validate_artifact_physical_name(physical_name)


@pytest.mark.parametrize(
    ("run_id", "run_attempt"),
    [
        (".", "4"),
        ("..", "4"),
        ("123", "."),
        ("123", ".."),
    ],
)
def test_artifact_physical_name_rejects_dot_attempt_segments(
    run_id: str,
    run_attempt: str,
) -> None:
    """Reject non-normalized physical-name attempt segments."""
    with pytest.raises(ContractValidationError):
        artifact_physical_name(
            "ci-validation/planning/123/4/validation-plan.json",
            run_id=run_id,
            run_attempt=run_attempt,
        )


def test_diagnostic_vocabularies_are_closed_and_exported() -> None:
    """Expose one source of truth for CI validation diagnostics."""
    assert {
        item.value for item in CiValidationKind.__members__.values()
    } == REGISTERED_CI_VALIDATION_KINDS
    assert (
        "ci-validation-selector-assignments"
        not in REGISTERED_CI_VALIDATION_KINDS
    )
    assert (
        "ci-validation-selector-assignment"
        not in REGISTERED_CI_VALIDATION_KINDS
    )
    assert (
        "ci-validation-writer-observation" not in REGISTERED_CI_VALIDATION_KINDS
    )
    assert "ci-validation-writer-observation" not in API_VERSIONS_BY_KIND
    assert "WRITER_OBSERVATION" not in CiValidationKind.__members__
    assert "ci-validation-selector-assignments" not in API_VERSIONS_BY_KIND
    assert "ci-validation-receipt" not in API_VERSIONS_BY_KIND
    assert "ci-validation-receipt-manifest" not in API_VERSIONS_BY_KIND
    assert "ci-validation-aggregate" not in API_VERSIONS_BY_KIND
    assert "ci-validation-aggregate" not in REGISTERED_CI_VALIDATION_KINDS
    assert "AGGREGATE" not in CiValidationKind.__members__
    assert {
        item.value for item in DiagnosticFamily.__members__.values()
    } == REGISTERED_CI_VALIDATION_DIAGNOSTIC_FAMILIES
    assert (
        REGISTERED_CI_VALIDATION_DIAGNOSTIC_CODES
        == REGISTERED_CI_VALIDATION_DIAGNOSTIC_FAMILIES
    )
    assert {
        item.value for item in DiagnosticDetail.__members__.values()
    } == REGISTERED_CI_VALIDATION_DIAGNOSTIC_DETAILS
    assert "malformed-artifact-ref" not in DiagnosticDetail.__members__.values()
    assert (
        "malformed-artifact-ref"
        not in REGISTERED_CI_VALIDATION_DIAGNOSTIC_DETAILS
    )
    assert (
        "required-evidence-missing" not in DiagnosticDetail.__members__.values()
    )
    assert (
        "required-evidence-missing"
        not in REGISTERED_CI_VALIDATION_DIAGNOSTIC_DETAILS
    )
    assert "REQUIRED_EVIDENCE_SKIPPED" not in DiagnosticDetail.__members__
    assert (
        "required-evidence-skipped"
        not in REGISTERED_CI_VALIDATION_DIAGNOSTIC_DETAILS
    )
    assert (
        DiagnosticFamily.REQUIRED_EVIDENCE_SKIPPED.value
        in REGISTERED_CI_VALIDATION_DIAGNOSTIC_FAMILIES
    )
    assert (
        DiagnosticFamily.REQUIRED_EVIDENCE_SKIPPED.value
        in REGISTERED_CI_VALIDATION_DIAGNOSTIC_CODES
    )
    assert (
        "required-evidence-skipped"
        not in DETAILS_BY_DIAGNOSTIC_CODE[
            DiagnosticFamily.REQUIRED_EVIDENCE_SKIPPED.value
        ]
    )
    assert (
        DiagnosticDetail.DEPENDENCY_BLOCKED.value
        in REGISTERED_CI_VALIDATION_DIAGNOSTIC_DETAILS
    )
    assert "request-invalid" in REGISTERED_CI_VALIDATION_DIAGNOSTIC_FAMILIES
    assert "request-ref-mismatch" in REGISTERED_CI_VALIDATION_DIAGNOSTIC_DETAILS
    assert not any(
        detail.startswith("selector-assignment-")
        for detail in REGISTERED_CI_VALIDATION_DIAGNOSTIC_DETAILS
    )
    assert (
        "namespace-enumeration-unavailable"
        in REGISTERED_CI_VALIDATION_DIAGNOSTIC_DETAILS
    )
    assert (
        "namespace-closure-failure"
        not in REGISTERED_CI_VALIDATION_DIAGNOSTIC_DETAILS
    )
    assert (
        "final-namespace-closure-mismatch"
        not in REGISTERED_CI_VALIDATION_DIAGNOSTIC_DETAILS
    )
    assert (
        "FINAL_NAMESPACE_CLOSURE_MISMATCH" not in DiagnosticDetail.__members__
    )
    assert "unknown-change" in REGISTERED_CI_VALIDATION_DIAGNOSTIC_CODES
    assert "final-evidence-failure" in REGISTERED_CI_VALIDATION_DIAGNOSTIC_CODES
    assert "missing-evidence" not in REGISTERED_CI_VALIDATION_DIAGNOSTIC_CODES
    assert (
        "inadmissible-receipt" not in REGISTERED_CI_VALIDATION_DIAGNOSTIC_CODES
    )
    assert (
        "inadmissible-receipt"
        not in REGISTERED_CI_VALIDATION_DIAGNOSTIC_FAMILIES
    )
    assert (
        "malformed-receipt" not in REGISTERED_CI_VALIDATION_DIAGNOSTIC_DETAILS
    )
    assert (
        "duplicate-receipt" not in REGISTERED_CI_VALIDATION_DIAGNOSTIC_DETAILS
    )
    assert (
        "unexpected-receipt" not in REGISTERED_CI_VALIDATION_DIAGNOSTIC_DETAILS
    )
    assert (
        DiagnosticDetail.REQUEST_REF_MISMATCH.value
        in (DETAILS_BY_DIAGNOSTIC_CODE[DiagnosticFamily.REQUEST_INVALID.value])
    )
    assert DETAILS_BY_DIAGNOSTIC_CODE[
        DiagnosticFamily.REQUIRED_EVIDENCE_MISSING.value
    ] == {DiagnosticDetail.MISSING_BUNDLE.value}
    assert DETAILS_BY_DIAGNOSTIC_CODE[
        DiagnosticFamily.REQUIRED_EVIDENCE_SKIPPED.value
    ] == {DiagnosticDetail.DEPENDENCY_BLOCKED.value}
    assert {item.value for item in DiagnosticSeverity.__members__.values()} == {
        "info",
        "warning",
        "fail-closed",
        "blocking-failure",
    }
    verdict_effects = {
        item.value for item in DiagnosticVerdictEffect.__members__.values()
    }
    assert verdict_effects == {"none", "fail-closed", "failed"}


def _valid_common_envelope_document() -> dict[str, object]:
    """Return a valid CI validation common-envelope document."""
    return {
        "api-version": API_VERSIONS_BY_KIND[CiValidationKind.REQUEST.value],
        "kind": CiValidationKind.REQUEST.value,
        "created-at": "2026-01-01T00:00:00Z",
        "repository": {"owner": "hcoona", "name": "three"},
        "run": {
            "workflow": "CI Validation",
            "run-id": "123",
            "run-attempt": "4",
        },
        "schema-diagnostics": [
            {
                "diagnostic-id": "schema/001",
                "code": DiagnosticFamily.REQUEST_INVALID.value,
                "detail": DiagnosticDetail.REQUEST_REF_MISMATCH.value,
                "message": "schema-compatible request warning",
                "source": {"type": "request", "id": None},
                "severity": "warning",
                "verdict-effect": "none",
            },
            {
                "diagnostic-id": "schema/002",
                "code": DiagnosticFamily.KNOWN_NON_IMPACTING.value,
                "detail": None,
                "message": None,
                "source": {"type": "aggregation", "id": "aggregate"},
                "severity": "info",
                "verdict-effect": "none",
            },
        ],
    }


def test_common_envelope_validation_accepts_minimum_shape() -> None:
    """Validate the shared api-version/kind/common-envelope fields."""
    document = _valid_common_envelope_document()

    envelope = validate_common_envelope(
        document,
        api_version=API_VERSIONS_BY_KIND[CiValidationKind.REQUEST.value],
        kind=CiValidationKind.REQUEST,
    )

    assert envelope.repository_owner == "hcoona"
    assert envelope.run_id == "123"


def test_common_envelope_rejects_caller_matched_unknown_kind() -> None:
    """Reject caller-supplied bogus kind/API pairs."""
    document = _valid_common_envelope_document()
    document["api-version"] = "three.ci.validation.bogus/v1alpha1"
    document["kind"] = "ci-validation-bogus"

    with pytest.raises(ContractValidationError) as error:
        validate_common_envelope(
            document,
            api_version="three.ci.validation.bogus/v1alpha1",
            kind="ci-validation-bogus",
        )

    assert "$.kind" in str(error.value)


@pytest.mark.parametrize(
    ("kind", "api_version"),
    [
        (
            "ci-validation-selector-assignments",
            "three.ci.validation.selector-assignments/v1alpha1",
        ),
        ("ci-validation-receipt", "three.ci.validation.receipt/v1alpha1"),
        (
            "ci-validation-receipt-manifest",
            "three.ci.validation.receipt-manifest/v1alpha1",
        ),
        (
            "ci-validation-aggregate",
            "three.ci.validation.aggregate/v1alpha1",
        ),
        (
            "ci-validation-writer-observation",
            "three.ci.validation.writer-observation/v1alpha1",
        ),
    ],
)
def test_common_envelope_rejects_legacy_kinds(
    kind: str,
    api_version: str,
) -> None:
    """Legacy G5 pre-batch artifacts are not current public kinds."""
    document = _valid_common_envelope_document()
    document["api-version"] = api_version
    document["kind"] = kind

    with pytest.raises(ContractValidationError) as error:
        validate_common_envelope(
            document,
            api_version=api_version,
            kind=kind,
        )

    assert "$.kind" in str(error.value)


def test_common_envelope_rejects_wrong_registered_api_version() -> None:
    """Require the document API version registered for its kind."""
    document = _valid_common_envelope_document()
    document["api-version"] = "three.ci.validation.plan/v1alpha1"

    with pytest.raises(ContractValidationError) as error:
        validate_common_envelope(
            document,
            api_version=API_VERSIONS_BY_KIND[CiValidationKind.REQUEST.value],
            kind=CiValidationKind.REQUEST,
        )

    assert "three.ci.validation.request/v1alpha1" in str(error.value)


@pytest.mark.parametrize(
    "created_at",
    [
        "2026-01-01T00:00:00Z",
        "2026-01-01T00:00:00.123456Z",
        "2026-01-01T00:00:00+05:30",
    ],
)
def test_common_envelope_accepts_rfc3339_created_at(created_at: str) -> None:
    """Accept supported RFC 3339 timestamp syntax."""
    document = _valid_common_envelope_document()
    document["created-at"] = created_at

    validate_common_envelope(
        document,
        api_version=API_VERSIONS_BY_KIND[CiValidationKind.REQUEST.value],
        kind=CiValidationKind.REQUEST,
    )


@pytest.mark.parametrize(
    "created_at",
    [
        "2026-01-01 00:00:00Z",
        "2026-13-01T00:00:00Z",
        "2026-01-01T00:00:00",
        "not-a-timestamp",
    ],
)
def test_common_envelope_rejects_invalid_created_at(created_at: str) -> None:
    """Reject non-RFC 3339 common-envelope timestamps."""
    document = _valid_common_envelope_document()
    document["created-at"] = created_at

    with pytest.raises(ContractValidationError) as error:
        validate_common_envelope(
            document,
            api_version=API_VERSIONS_BY_KIND[CiValidationKind.REQUEST.value],
            kind=CiValidationKind.REQUEST,
        )

    assert "$.created-at" in str(error.value)


def test_schema_diagnostic_detail_must_match_code_family() -> None:
    """Reject globally registered details from the wrong diagnostic family."""
    document = _valid_common_envelope_document()
    diagnostics = document["schema-diagnostics"]
    assert isinstance(diagnostics, list)
    diagnostic = diagnostics[0]
    assert isinstance(diagnostic, dict)
    diagnostic["code"] = DiagnosticFamily.RANGE_UNCONFIRMED.value
    diagnostic["detail"] = DiagnosticDetail.REQUEST_REF_MISMATCH.value

    with pytest.raises(ContractValidationError) as error:
        validate_common_envelope(
            document,
            api_version=API_VERSIONS_BY_KIND[CiValidationKind.REQUEST.value],
            kind=CiValidationKind.REQUEST,
        )

    assert "not valid for this diagnostic code" in str(error.value)


def test_schema_diagnostic_accepts_detail_for_matching_code_family() -> None:
    """Accept a registered detail for its owning diagnostic family."""
    document = _valid_common_envelope_document()
    diagnostics = document["schema-diagnostics"]
    assert isinstance(diagnostics, list)
    diagnostic = diagnostics[0]
    assert isinstance(diagnostic, dict)
    diagnostic["code"] = DiagnosticFamily.RANGE_UNCONFIRMED.value
    diagnostic["detail"] = DiagnosticDetail.MISSING.value

    validate_common_envelope(
        document,
        api_version=API_VERSIONS_BY_KIND[CiValidationKind.REQUEST.value],
        kind=CiValidationKind.REQUEST,
    )


def test_schema_diagnostic_rejects_legacy_namespace_self_detail() -> None:
    """Do not accept the legacy namespace-closure self-detail."""
    document = _valid_common_envelope_document()
    diagnostics = document["schema-diagnostics"]
    assert isinstance(diagnostics, list)
    diagnostic = diagnostics[0]
    assert isinstance(diagnostic, dict)
    diagnostic["code"] = DiagnosticFamily.NAMESPACE_CLOSURE_FAILURE.value
    diagnostic["detail"] = "namespace-closure-failure"

    with pytest.raises(ContractValidationError) as error:
        validate_common_envelope(
            document,
            api_version=API_VERSIONS_BY_KIND[CiValidationKind.REQUEST.value],
            kind=CiValidationKind.REQUEST,
        )

    assert "not registered" in str(error.value)


@pytest.mark.parametrize(
    "detail",
    [
        "missing",
        "incomplete",
        "inconsistent",
        "unconfirmed-provenance",
    ],
)
def test_schema_diagnostic_rejects_stale_required_evidence_details(
    detail: str,
) -> None:
    """Current required-evidence-missing diagnostics only expose bundles."""
    document = _valid_common_envelope_document()
    diagnostics = document["schema-diagnostics"]
    assert isinstance(diagnostics, list)
    diagnostic = diagnostics[0]
    assert isinstance(diagnostic, dict)
    diagnostic["code"] = DiagnosticFamily.REQUIRED_EVIDENCE_MISSING.value
    diagnostic["detail"] = detail

    with pytest.raises(ContractValidationError) as error:
        validate_common_envelope(
            document,
            api_version=API_VERSIONS_BY_KIND[CiValidationKind.REQUEST.value],
            kind=CiValidationKind.REQUEST,
        )

    assert "is not valid for this diagnostic code" in str(error.value)


@pytest.mark.parametrize(
    "detail",
    [
        "missing",
        "incomplete",
        "inconsistent",
        "unconfirmed-provenance",
    ],
)
def test_schema_diagnostic_rejects_stale_required_evidence_skipped_details(
    detail: str,
) -> None:
    """Current required-evidence-skipped diagnostics only expose deps."""
    document = _valid_common_envelope_document()
    diagnostics = document["schema-diagnostics"]
    assert isinstance(diagnostics, list)
    diagnostic = diagnostics[0]
    assert isinstance(diagnostic, dict)
    diagnostic["code"] = DiagnosticFamily.REQUIRED_EVIDENCE_SKIPPED.value
    diagnostic["detail"] = detail

    with pytest.raises(ContractValidationError) as error:
        validate_common_envelope(
            document,
            api_version=API_VERSIONS_BY_KIND[CiValidationKind.REQUEST.value],
            kind=CiValidationKind.REQUEST,
        )

    assert "is not valid for this diagnostic code" in str(error.value)


def test_schema_diagnostic_rejects_generic_skipped_detail() -> None:
    """The skipped family does not expose a generic self-detail."""
    document = _valid_common_envelope_document()
    diagnostics = document["schema-diagnostics"]
    assert isinstance(diagnostics, list)
    diagnostic = diagnostics[0]
    assert isinstance(diagnostic, dict)
    diagnostic["code"] = DiagnosticFamily.REQUIRED_EVIDENCE_SKIPPED.value
    diagnostic["detail"] = DiagnosticFamily.REQUIRED_EVIDENCE_SKIPPED.value

    with pytest.raises(ContractValidationError) as error:
        validate_common_envelope(
            document,
            api_version=API_VERSIONS_BY_KIND[CiValidationKind.REQUEST.value],
            kind=CiValidationKind.REQUEST,
        )

    assert "is not registered" in str(error.value)


def test_schema_diagnostic_accepts_skipped_dependency_blocked() -> None:
    """The skipped family still accepts the public dependency-blocked detail."""
    document = _valid_common_envelope_document()
    diagnostics = document["schema-diagnostics"]
    assert isinstance(diagnostics, list)
    diagnostic = diagnostics[0]
    assert isinstance(diagnostic, dict)
    diagnostic["code"] = DiagnosticFamily.REQUIRED_EVIDENCE_SKIPPED.value
    diagnostic["detail"] = DiagnosticDetail.DEPENDENCY_BLOCKED.value

    validate_common_envelope(
        document,
        api_version=API_VERSIONS_BY_KIND[CiValidationKind.REQUEST.value],
        kind=CiValidationKind.REQUEST,
    )


@pytest.mark.parametrize(
    "detail",
    [
        "malformed-artifact-ref",
        "required-evidence-missing",
        "required-evidence-skipped",
    ],
)
def test_schema_diagnostic_rejects_orphan_unregistered_details(
    detail: str,
) -> None:
    """Orphan detail spellings are not part of the public detail registry."""
    document = _valid_common_envelope_document()
    diagnostics = document["schema-diagnostics"]
    assert isinstance(diagnostics, list)
    diagnostic = diagnostics[0]
    assert isinstance(diagnostic, dict)
    diagnostic["detail"] = detail

    with pytest.raises(ContractValidationError) as error:
        validate_common_envelope(
            document,
            api_version=API_VERSIONS_BY_KIND[CiValidationKind.REQUEST.value],
            kind=CiValidationKind.REQUEST,
        )

    assert "is not registered" in str(error.value)


@pytest.mark.parametrize(
    "missing_field",
    ["detail", "message", "source.id"],
)
def test_schema_diagnostics_require_nullable_fields(
    missing_field: str,
) -> None:
    """Require nullable diagnostic-record fields."""
    document = _valid_common_envelope_document()
    diagnostics = document["schema-diagnostics"]
    assert isinstance(diagnostics, list)
    diagnostic = diagnostics[1]
    assert isinstance(diagnostic, dict)
    if missing_field == "source.id":
        source = diagnostic["source"]
        assert isinstance(source, dict)
        source.pop("id")
    else:
        diagnostic.pop(missing_field)

    with pytest.raises(ContractValidationError) as error:
        validate_common_envelope(
            document,
            api_version=API_VERSIONS_BY_KIND[CiValidationKind.REQUEST.value],
            kind=CiValidationKind.REQUEST,
        )

    assert "is required" in str(error.value)


def test_schema_diagnostics_must_be_sorted_by_diagnostic_id() -> None:
    """Reject schema diagnostics that are not sorted by diagnostic-id."""
    document = _valid_common_envelope_document()
    diagnostics = document["schema-diagnostics"]
    assert isinstance(diagnostics, list)
    diagnostics.reverse()

    with pytest.raises(ContractValidationError) as error:
        validate_common_envelope(
            document,
            api_version=API_VERSIONS_BY_KIND[CiValidationKind.REQUEST.value],
            kind=CiValidationKind.REQUEST,
        )

    message = str(error.value)
    assert "$.schema-diagnostics" in message
    assert "sorted by diagnostic-id" in message


def test_common_envelope_rejects_bad_types_and_schema_diagnostics() -> None:
    """Reject malformed common envelope and non-closed schema diagnostics."""
    document = {
        "api-version": "three.ci.validation.request/v1alpha1",
        "kind": "ci-validation-request",
        "created-at": "2026-01-01T00:00:00Z",
        "repository": {"owner": "hcoona", "name": "three"},
        "run": {"workflow": "CI Validation", "run-id": "123"},
        "schema-diagnostics": [
            {
                "diagnostic-id": "schema/001",
                "code": "unknown",
                "detail": "unknown",
                "message": "invalid schema diagnostic",
                "source": {"type": "not-a-source", "id": None},
                "severity": "fail-closed",
                "verdict-effect": "fail-closed",
            },
        ],
    }

    with pytest.raises(ContractValidationError) as error:
        validate_common_envelope(
            document,
            api_version=API_VERSIONS_BY_KIND[CiValidationKind.REQUEST.value],
            kind=CiValidationKind.REQUEST,
        )

    message = str(error.value)
    assert "$.run.run-attempt" in message
    assert "$.schema-diagnostics[0].code" in message
    assert "$.schema-diagnostics[0].verdict-effect" in message
