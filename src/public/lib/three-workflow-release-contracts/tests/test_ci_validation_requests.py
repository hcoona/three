"""CI validation request normalization and no-plan report tests."""

from __future__ import annotations

import pytest
from three_workflow_release_contracts import (
    API_VERSIONS_BY_KIND,
    CiValidationKind,
    DiagnosticDetail,
    DiagnosticFamily,
    DiagnosticSeverity,
    DiagnosticVerdictEffect,
    canonical_json_digest,
    ci_validation_request_artifact_ref,
    ci_validation_request_projection,
    normalize_ci_validation_request,
)
from three_workflow_release_contracts import (
    ci_validation_requests as requests_module,
)

RUN_ID = "25887422010"
RUN_ATTEMPT = "1"
OTHER_RUN_ID = "25887422011"
OTHER_RUN_ATTEMPT = "2"


def _normalize(
    document: object,
    *,
    expected_run_id: str = RUN_ID,
    expected_run_attempt: str = RUN_ATTEMPT,
):
    return normalize_ci_validation_request(
        document,
        expected_run_id=expected_run_id,
        expected_run_attempt=expected_run_attempt,
    )


def _request() -> dict[str, object]:
    document: dict[str, object] = {
        "api-version": API_VERSIONS_BY_KIND[CiValidationKind.REQUEST.value],
        "kind": CiValidationKind.REQUEST.value,
        "created-at": "2026-05-14T21:09:21Z",
        "repository": {"owner": "hcoona", "name": "three"},
        "run": {
            "workflow": "CI Validation",
            "run-id": RUN_ID,
            "run-attempt": RUN_ATTEMPT,
        },
        "schema-diagnostics": [],
        "artifact-ref": ci_validation_request_artifact_ref(
            run_id=RUN_ID,
            run_attempt=RUN_ATTEMPT,
        ),
        "request-digest": "0" * 64,
        "mode": "pull_request",
        "validation-tree": {
            "commit-sha": "b" * 40,
            "ref": "refs/pull/42/merge",
        },
        "event": {
            "name": "pull_request",
            "number": "42",
            "actor": "octocat",
            "run-id": RUN_ID,
            "run-attempt": RUN_ATTEMPT,
        },
        "affected-range": {
            "status": "available",
            "base-sha": "a" * 40,
            "base-tip-sha": "c" * 40,
            "head-sha": "b" * 40,
            "changed-files": [
                "src/public/lib/example.py",
                "tests/example_test.py",
            ],
            "source": "pull_request",
            "diagnostic": None,
            "diagnostic-detail": None,
        },
    }
    document["request-digest"] = canonical_json_digest(
        ci_validation_request_projection(document),
    )
    return document


def _push_request(*, validation_tree_sha: str = "b" * 40) -> dict[str, object]:
    document = _request()
    document["mode"] = "push"
    document["validation-tree"] = {
        "commit-sha": validation_tree_sha,
        "ref": "refs/heads/main",
    }
    document["event"] = {
        "name": "push",
        "number": None,
        "actor": "octocat",
        "run-id": RUN_ID,
        "run-attempt": RUN_ATTEMPT,
    }
    document["affected-range"] = {
        "status": "available",
        "base-sha": "a" * 40,
        "base-tip-sha": None,
        "head-sha": "b" * 40,
        "changed-files": ["src/public/lib/example.py"],
        "source": "push",
        "diagnostic": None,
        "diagnostic-detail": None,
    }
    document["request-digest"] = canonical_json_digest(
        ci_validation_request_projection(document),
    )
    return document


def _redigest(document: dict[str, object]) -> None:
    document["request-digest"] = canonical_json_digest(
        ci_validation_request_projection(document),
    )


def test_request_normalization_digest_is_deterministic() -> None:
    """Normalize equivalent requests to the same canonical request digest."""
    first = _request()
    second = _request()
    second["event"] = {
        "actor": "octocat",
        "run-attempt": RUN_ATTEMPT,
        "run-id": RUN_ID,
        "number": "42",
        "name": "pull_request",
    }
    second["request-digest"] = canonical_json_digest(
        ci_validation_request_projection(second),
    )

    first_result = _normalize(first)
    second_result = _normalize(second)

    assert first_result.is_valid
    assert second_result.is_valid
    assert first_result.request is not None
    assert second_result.request is not None
    assert (
        first_result.request.request_digest
        == second_result.request.request_digest
    )
    assert first_result.request.projection == second_result.request.projection


def test_request_normalization_fails_closed_for_digest_mismatch() -> None:
    """Represent invalid request identity as request-invalid diagnostics."""
    document = _request()
    document["request-digest"] = "1" * 64

    result = _normalize(document)

    assert result.request is None
    assert result.diagnostics == (
        {
            "diagnostic-id": "request-invalid/001",
            "code": DiagnosticFamily.REQUEST_INVALID.value,
            "detail": DiagnosticDetail.REQUEST_DIGEST_MISMATCH.value,
            "message": "CI validation request is not replayable",
            "source": {"type": "request", "id": None},
            "severity": DiagnosticSeverity.FAIL_CLOSED.value,
            "verdict-effect": DiagnosticVerdictEffect.FAIL_CLOSED.value,
        },
    )


def test_request_normalization_fails_closed_for_noncanonical_projection() -> (
    None
):
    """Canonical JSON errors become request-invalid diagnostics."""
    document = _request()
    event = document["event"]
    assert isinstance(event, dict)
    event["actor"] = "\ud800"

    result = _normalize(document)

    assert result.request is None
    assert (
        result.diagnostics[0]["code"] == DiagnosticFamily.REQUEST_INVALID.value
    )
    assert result.diagnostics[0]["detail"] == (
        DiagnosticDetail.REQUEST_SCHEMA_INVALID.value
    )


def test_request_normalization_rejects_unnormalized_changed_files() -> None:
    """Do not repair unsorted changed-file arrays before planner input."""
    document = _request()
    affected = document["affected-range"]
    assert isinstance(affected, dict)
    affected["changed-files"] = [
        "tests/example_test.py",
        "src/public/lib/example.py",
    ]
    document["request-digest"] = canonical_json_digest(
        ci_validation_request_projection(document),
    )

    result = _normalize(document)

    assert result.request is None
    assert (
        result.diagnostics[0]["code"] == DiagnosticFamily.REQUEST_INVALID.value
    )
    assert result.diagnostics[0]["detail"] == (
        DiagnosticDetail.REQUEST_SCHEMA_INVALID.value
    )


def test_request_normalization_rejects_surrogate_changed_file() -> None:
    """Malformed changed-file strings fail closed before UTF-8 sorting."""
    document = _request()
    affected = document["affected-range"]
    assert isinstance(affected, dict)
    affected["changed-files"] = ["src/public/lib/example.py", "\ud800"]

    result = _normalize(document)

    assert result.request is None
    assert (
        result.diagnostics[0]["code"] == DiagnosticFamily.REQUEST_INVALID.value
    )
    assert result.diagnostics[0]["detail"] == (
        DiagnosticDetail.REQUEST_SCHEMA_INVALID.value
    )


def test_request_normalization_uses_expected_run_context() -> None:
    """Reject stale request envelopes even when payload internals agree."""
    document = _request()
    document["run"] = {
        "workflow": "CI Validation",
        "run-id": OTHER_RUN_ID,
        "run-attempt": RUN_ATTEMPT,
    }

    result = _normalize(document)

    assert result.request is None
    assert result.diagnostics[0]["detail"] == (
        DiagnosticDetail.REQUEST_WRONG_RUN_ATTEMPT.value
    )


def test_request_normalization_uses_expected_run_attempt_context() -> None:
    """Reject request artifacts from a different attempt in the same run."""
    document = _request()
    document["run"] = {
        "workflow": "CI Validation",
        "run-id": RUN_ID,
        "run-attempt": OTHER_RUN_ATTEMPT,
    }

    result = _normalize(document)

    assert result.request is None
    assert result.diagnostics[0]["detail"] == (
        DiagnosticDetail.REQUEST_WRONG_RUN_ATTEMPT.value
    )


def test_request_normalization_uses_expected_artifact_ref_context() -> None:
    """Reject payloads not bound to the current expected artifact ref."""
    document = _request()
    document["artifact-ref"] = ci_validation_request_artifact_ref(
        run_id=OTHER_RUN_ID,
        run_attempt=RUN_ATTEMPT,
    )

    result = _normalize(document)

    assert result.request is None
    assert result.diagnostics[0]["detail"] == (
        DiagnosticDetail.REQUEST_REF_MISMATCH.value
    )


def test_scheduled_full_request_has_no_affected_range() -> None:
    """Validate the scheduled-full no-affected-range shape."""
    document = _request()
    document["mode"] = "scheduled_full"
    document["event"] = {
        "name": "schedule",
        "number": None,
        "actor": "github-actions[bot]",
        "run-id": RUN_ID,
        "run-attempt": RUN_ATTEMPT,
    }
    document.pop("affected-range")
    document["scheduled-full"] = {"enabled": True}
    document["request-digest"] = canonical_json_digest(
        ci_validation_request_projection(document),
    )

    result = _normalize(document)

    assert result.is_valid
    assert result.request is not None
    assert result.request.mode == "scheduled_full"


@pytest.mark.parametrize(
    ("mode", "event_name"),
    [
        ("scheduled_full", "pull_request"),
        ("push", "schedule"),
        ("push", "pull_request"),
        ("pull_request", "push"),
    ],
)
def test_request_mode_must_match_event_name(
    mode: str,
    event_name: str,
) -> None:
    """Reject request mode/event trigger mismatches."""
    if mode == "push":
        document = _push_request()
    elif mode == "scheduled_full":
        document = _request()
        document["mode"] = "scheduled_full"
        document.pop("affected-range")
        document["scheduled-full"] = {"enabled": True}
    else:
        document = _request()
    event = document["event"]
    assert isinstance(event, dict)
    event["name"] = event_name
    document["request-digest"] = canonical_json_digest(
        ci_validation_request_projection(document),
    )

    result = _normalize(document)

    assert result.request is None
    assert result.diagnostics[0]["detail"] == (
        DiagnosticDetail.REQUEST_SCHEMA_INVALID.value
    )


def test_push_request_requires_validation_tree_to_equal_head() -> None:
    """Reject digest-valid push requests checked out at the wrong commit."""
    result = _normalize(_push_request(validation_tree_sha="c" * 40))

    assert result.request is None
    assert result.diagnostics[0]["detail"] == (
        DiagnosticDetail.REQUEST_SCHEMA_INVALID.value
    )


def test_push_request_accepts_validation_tree_matching_head() -> None:
    """Accept available push requests checked out at affected head."""
    result = _normalize(_push_request(validation_tree_sha="b" * 40))

    assert result.is_valid
    assert result.request is not None
    assert result.request.mode == "push"


@pytest.mark.parametrize("endpoint", ["base-sha", "head-sha"])
def test_push_request_rejects_available_all_zero_endpoint(
    endpoint: str,
) -> None:
    """Reject all-zero endpoints in confirmed available push ranges."""
    document = _push_request(
        validation_tree_sha="0" * 40 if endpoint == "head-sha" else "b" * 40,
    )
    affected = document["affected-range"]
    assert isinstance(affected, dict)
    affected[endpoint] = "0" * 40
    _redigest(document)

    result = _normalize(document)

    assert result.request is None
    assert result.diagnostics[0]["detail"] == (
        DiagnosticDetail.REQUEST_SCHEMA_INVALID.value
    )


def test_request_normalization_requires_nullable_affected_range_keys() -> None:
    """Nullable affected-range fields must be explicit request members."""
    document = _request()
    affected = document["affected-range"]
    assert isinstance(affected, dict)
    del affected["diagnostic"]
    document["request-digest"] = canonical_json_digest(
        ci_validation_request_projection(document),
    )

    result = _normalize(document)

    assert result.request is None
    assert result.diagnostics[0]["detail"] == (
        DiagnosticDetail.REQUEST_SCHEMA_INVALID.value
    )


def test_unavailable_range_accepts_explicit_nullable_fields() -> None:
    """Explicit null changed-files is valid for unavailable affected ranges."""
    document = _request()
    document["affected-range"] = {
        "status": "unavailable",
        "base-sha": None,
        "base-tip-sha": None,
        "head-sha": None,
        "changed-files": None,
        "source": "pull_request",
        "diagnostic": DiagnosticFamily.RANGE_UNCONFIRMED.value,
        "diagnostic-detail": DiagnosticDetail.MISSING.value,
    }
    document["request-digest"] = canonical_json_digest(
        ci_validation_request_projection(document),
    )

    result = _normalize(document)

    assert result.is_valid


def test_no_authoritative_plan_report_is_not_public_surface() -> None:
    """Do not expose the legacy aggregate/receipt no-plan report helper."""
    assert not hasattr(requests_module, "no_authoritative_plan_report")
