"""CI validation request normalization and no-plan report tests."""

from __future__ import annotations

from typing import Any, cast

import pytest
from three_workflow_release_contracts import (
    API_VERSIONS_BY_KIND,
    ArtifactAdmission,
    CiValidationKind,
    ContractValidationError,
    DiagnosticDetail,
    DiagnosticFamily,
    DiagnosticSeverity,
    DiagnosticVerdictEffect,
    GitHubActionsArtifactMetadata,
    artifact_physical_name,
    canonical_json_digest,
    ci_validation_diagnostic,
    ci_validation_plan_artifact_ref,
    ci_validation_planner_diagnostics_artifact_ref,
    ci_validation_request_artifact_ref,
    ci_validation_request_projection,
    no_authoritative_plan_report,
    normalize_ci_validation_request,
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


def test_no_plan_report_accepts_valid_supplied_diagnostic() -> None:
    """Accept diagnostics built through the closed helper surface."""
    diagnostic = ci_validation_diagnostic(
        diagnostic_id="request-invalid/001",
        code=DiagnosticFamily.REQUEST_INVALID.value,
        detail=DiagnosticDetail.REQUEST_SCHEMA_INVALID.value,
        message="request schema invalid",
        source_type="request",
        source_id=None,
        severity=DiagnosticSeverity.FAIL_CLOSED.value,
        verdict_effect=DiagnosticVerdictEffect.FAIL_CLOSED.value,
    )

    report = no_authoritative_plan_report(
        created_at="2026-05-14T21:10:21Z",
        repository_owner="hcoona",
        repository_name="three",
        workflow="CI Validation",
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
        diagnostics=[diagnostic],
    )

    report_diagnostics = cast("list[object]", report["diagnostics"])
    assert diagnostic in report_diagnostics


def test_no_plan_report_rejects_invalid_common_envelope() -> None:
    """Reject malformed aggregate common-envelope fields before returning."""
    with pytest.raises(ContractValidationError, match=r"\$\.created-at"):
        no_authoritative_plan_report(
            created_at="not-a-timestamp",
            repository_owner="hcoona",
            repository_name="three",
            workflow="CI Validation",
            run_id=RUN_ID,
            run_attempt=RUN_ATTEMPT,
        )


def test_no_plan_report_rejects_invalid_planning_conclusion() -> None:
    """Reject runtime job-conclusion values outside the closed set."""
    with pytest.raises(ContractValidationError, match="planning-conclusion"):
        no_authoritative_plan_report(
            created_at="2026-05-14T21:10:21Z",
            repository_owner="hcoona",
            repository_name="three",
            workflow="CI Validation",
            run_id=RUN_ID,
            run_attempt=RUN_ATTEMPT,
            planning_conclusion=cast("Any", "not-a-conclusion"),
        )


@pytest.mark.parametrize(
    "diagnostic",
    [
        {
            "diagnostic-id": "bad/001",
            "code": "not-registered",
            "detail": None,
            "message": None,
            "source": {"type": "request", "id": None},
            "severity": DiagnosticSeverity.FAIL_CLOSED.value,
            "verdict-effect": DiagnosticVerdictEffect.FAIL_CLOSED.value,
        },
        {
            "diagnostic-id": "bad/002",
            "code": DiagnosticFamily.REQUEST_INVALID.value,
            "detail": DiagnosticDetail.PLAN_MISSING.value,
            "message": None,
            "source": {"type": "request", "id": None},
            "severity": DiagnosticSeverity.FAIL_CLOSED.value,
            "verdict-effect": DiagnosticVerdictEffect.FAIL_CLOSED.value,
        },
        {
            "diagnostic-id": "bad/003",
            "code": DiagnosticFamily.REQUEST_INVALID.value,
            "detail": DiagnosticDetail.REQUEST_SCHEMA_INVALID.value,
            "source": {"type": "request", "id": None},
            "severity": DiagnosticSeverity.FAIL_CLOSED.value,
            "verdict-effect": DiagnosticVerdictEffect.FAIL_CLOSED.value,
        },
        {
            "diagnostic-id": "bad/004",
            "code": DiagnosticFamily.REQUEST_INVALID.value,
            "detail": DiagnosticDetail.REQUEST_SCHEMA_INVALID.value,
            "message": None,
            "source": {"type": "not-a-source", "id": None},
            "severity": DiagnosticSeverity.FAIL_CLOSED.value,
            "verdict-effect": DiagnosticVerdictEffect.FAIL_CLOSED.value,
        },
    ],
)
def test_no_plan_report_rejects_invalid_supplied_diagnostic(
    diagnostic: dict[str, object],
) -> None:
    """Reject raw diagnostics outside the closed diagnostic-record contract."""
    with pytest.raises(ContractValidationError):
        no_authoritative_plan_report(
            created_at="2026-05-14T21:10:21Z",
            repository_owner="hcoona",
            repository_name="three",
            workflow="CI Validation",
            run_id=RUN_ID,
            run_attempt=RUN_ATTEMPT,
            diagnostics=[diagnostic],
        )


def test_no_authoritative_plan_report_does_not_forge_plan() -> None:
    """Emit invalid-plan report fields with null plan identity."""
    diagnostics_ref = ci_validation_planner_diagnostics_artifact_ref(
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
    )
    diagnostics_physical_name = artifact_physical_name(diagnostics_ref)
    diagnostics_digest = "sha256:" + "0" * 64
    diagnostics_admission = ArtifactAdmission(
        logical_ref=diagnostics_ref,
        physical_name=diagnostics_physical_name,
        artifact=GitHubActionsArtifactMetadata(
            artifact_id=7005535001,
            name=diagnostics_physical_name,
            created_at="2026-05-14T21:09:30Z",
            expired=False,
            digest=diagnostics_digest,
        ),
    )
    diagnostics_evidence = {
        "artifact-id": 7005535001,
        "artifact-ref": diagnostics_ref,
        "physical-artifact-name": diagnostics_physical_name,
        "created-at": "2026-05-14T21:09:30Z",
        "digest": diagnostics_digest,
    }
    report = no_authoritative_plan_report(
        created_at="2026-05-14T21:10:21Z",
        repository_owner="hcoona",
        repository_name="three",
        workflow="CI Validation",
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
        planning_conclusion="failure",
        observed_plan_artifact_count=0,
        planner_diagnostics_artifact=diagnostics_admission,
    )

    plan_ref = ci_validation_plan_artifact_ref(
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
    )
    assert report["plan-id"] is None
    assert report["plan-digest"] is None
    assert report["mode"] == "unknown"
    assert report["verdict"] == "failed"
    assert report["reason"] == {
        "invalid-plan": True,
        "fail-closed": False,
        "required-evidence-missing": False,
        "required-evidence-skipped": False,
        "blocking-validation-failure": False,
        "inadmissible-receipt": False,
        "final-evidence-failure": False,
    }
    failures = cast("list[dict[str, Any]]", report["failures"])
    diagnostic = cast("dict[str, object]", failures[0]["diagnostic"])
    assert failures[0]["kind"] == DiagnosticFamily.INVALID_PLAN.value
    assert diagnostic["detail"] == (DiagnosticDetail.PLAN_MISSING.value)
    assert report["no-authoritative-plan"] == {
        "plan": {
            "plan-id": None,
            "artifact-ref": plan_ref,
            "physical-artifact-name": artifact_physical_name(plan_ref),
            "observed-artifact-count": 0,
        },
        "planner-diagnostics-artifact": {
            "artifact-ref": diagnostics_ref,
            "physical-artifact-name": diagnostics_physical_name,
            "evidence": diagnostics_evidence,
        },
        "jobs": {
            "plan": {"conclusion": "failure"},
            "materialize-work-groups": {"conclusion": "skipped"},
            "validation-work-groups": {"conclusion": "skipped"},
            "aggregate-evidence": {"conclusion": "failure"},
        },
        "run": {"conclusion": "failure"},
    }


def test_no_authoritative_plan_rejects_unadmitted_diagnostics_evidence() -> (
    None
):
    """Do not mark arbitrary diagnostics artifact mappings as trusted."""
    diagnostics_ref = ci_validation_planner_diagnostics_artifact_ref(
        run_id=RUN_ID,
        run_attempt=RUN_ATTEMPT,
    )

    with pytest.raises(ContractValidationError, match="exactly-one live"):
        no_authoritative_plan_report(
            created_at="2026-05-14T21:10:21Z",
            repository_owner="hcoona",
            repository_name="three",
            workflow="CI Validation",
            run_id=RUN_ID,
            run_attempt=RUN_ATTEMPT,
            planner_diagnostics_artifact=cast(
                "Any",
                {
                    "artifact-id": 7005535001,
                    "artifact-ref": diagnostics_ref,
                    "physical-artifact-name": artifact_physical_name(
                        diagnostics_ref,
                    ),
                    "created-at": "2026-05-14T21:09:30Z",
                    "digest": "sha256:" + "0" * 64,
                },
            ),
        )


def test_no_authoritative_plan_rejects_wrong_diagnostics_admission() -> None:
    """Do not trust an admitted artifact for a different diagnostics ref."""
    wrong_ref = ci_validation_planner_diagnostics_artifact_ref(
        run_id=OTHER_RUN_ID,
        run_attempt=RUN_ATTEMPT,
    )
    wrong_physical_name = artifact_physical_name(wrong_ref)

    with pytest.raises(ContractValidationError) as error:
        no_authoritative_plan_report(
            created_at="2026-05-14T21:10:21Z",
            repository_owner="hcoona",
            repository_name="three",
            workflow="CI Validation",
            run_id=RUN_ID,
            run_attempt=RUN_ATTEMPT,
            planner_diagnostics_artifact=ArtifactAdmission(
                logical_ref=wrong_ref,
                physical_name=wrong_physical_name,
                artifact=GitHubActionsArtifactMetadata(
                    artifact_id=7005535001,
                    name=wrong_physical_name,
                    created_at="2026-05-14T21:09:30Z",
                    expired=False,
                    digest="sha256:" + "0" * 64,
                ),
            ),
        )
    assert "planner-diagnostics-artifact.artifact-ref" in str(error.value)
    assert "planner-diagnostics-artifact.physical-artifact-name" in str(
        error.value,
    )
