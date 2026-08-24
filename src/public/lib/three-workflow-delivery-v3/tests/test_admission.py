"""Scenario tests for caller-authoritative transport admission."""

from __future__ import annotations

from dataclasses import replace
from typing import TYPE_CHECKING, cast

import pytest
from three_workflow_delivery_v3.canonical import canonical_sha256
from three_workflow_delivery_v3.records import (
    AdmissionMode,
    CurrentAuthorityContext,
    ExecutionHistoryContext,
    HistoryLineage,
    PlatformJobFacts,
    PlatformRunFacts,
    admit,
)

if TYPE_CHECKING:
    from three_workflow_delivery_v3.canonical import JsonValue

CURRENT_TRANSPORT_DIGEST = "sha256:" + ("a" * 64)
HISTORY_TRANSPORT_DIGEST = "sha256:" + ("b" * 64)
MISMATCHED_DIGEST = "sha256:" + ("c" * 64)
EXPECTED_RELEASE_EXECUTION = "buddy:hcoona-release-smoke-npm:" + ("e" * 40)
EXPECTED_CONTROL_IDENTITY = "control:" + ("e" * 40)
EXPECTED_HISTORY_LINEAGE = HistoryLineage(
    release_execution=EXPECTED_RELEASE_EXECUTION,
    purpose="live-release",
    target="e" * 40,
    control_identity=EXPECTED_CONTROL_IDENTITY,
)
_NOT_SUPPLIED = object()


def _current_payload() -> dict[str, JsonValue]:
    return {
        "release_execution": "buddy:hcoona-release-smoke-npm:" + ("f" * 40),
        "purpose": "live-release",
        "request": "request-17",
        "workflow_run_id": 4501,
        "run_attempt": 3,
        "attempt": "attempt-9",
        "target": "f" * 40,
        "producer": "release-planner",
        "control": "workflow-delivery-v3",
    }


def _current_context(
    payload: dict[str, JsonValue],
) -> CurrentAuthorityContext:
    return CurrentAuthorityContext(
        release_execution="buddy:hcoona-release-smoke-npm:" + ("f" * 40),
        purpose="live-release",
        request="request-17",
        workflow_run_id=4501,
        run_attempt=3,
        attempt="attempt-9",
        target="f" * 40,
        producer="release-planner",
        control="workflow-delivery-v3",
        artifact_id=7102,
        artifact_digest=CURRENT_TRANSPORT_DIGEST,
        payload_digest=canonical_sha256(payload),
    )


def _history_payload() -> dict[str, JsonValue]:
    return {
        "execution": EXPECTED_RELEASE_EXECUTION,
        "target": "e" * 40,
        "producer": "historical-finalizer",
        "run_attempt": 2,
        "reusable_workflow": "release-attempt.yml",
        "purpose": "live-release",
        "control": EXPECTED_CONTROL_IDENTITY,
    }


def _history_scenario() -> tuple[
    dict[str, JsonValue],
    HistoryLineage,
    ExecutionHistoryContext,
    PlatformRunFacts,
    PlatformJobFacts,
]:
    payload = _history_payload()
    metadata = (("artifact_created_at", "2026-08-06T18:10:00Z"),)
    context = ExecutionHistoryContext(
        lineage=EXPECTED_HISTORY_LINEAGE,
        operation="admit",
        attempt_created=False,
        artifact_id=8203,
        artifact_digest=HISTORY_TRANSPORT_DIGEST,
        payload_digest=canonical_sha256(payload),
        source_workflow_run_id=4490,
        current_workflow_run_id=4501,
        current_run_attempt=3,
        exposed_platform_metadata=metadata,
    )
    run = PlatformRunFacts(
        workflow_run_id=4490,
        head_sha="e" * 40,
        run_attempt=2,
        exposed_metadata=metadata,
    )
    job = PlatformJobFacts(
        job_id=991,
        conclusion="success",
        phase="finalized",
    )
    return payload, EXPECTED_HISTORY_LINEAGE, context, run, job


@pytest.mark.parametrize(
    "caller_mode",
    [AdmissionMode.CURRENT_AUTHORITY, AdmissionMode.EXECUTION_HISTORY],
)
def test_payload_admission_mode_cannot_override_caller_selected_mode(
    caller_mode: AdmissionMode,
) -> None:
    """Reject a payload mode claim instead of consulting it for authority."""
    if caller_mode is AdmissionMode.CURRENT_AUTHORITY:
        payload = _current_payload()
        payload["admission_mode"] = AdmissionMode.EXECUTION_HISTORY
        context = _current_context(payload)
        arguments = {
            "current": context,
        }
    else:
        payload, expected_lineage, context, run, job = _history_scenario()
        payload["admission_mode"] = AdmissionMode.CURRENT_AUTHORITY
        context = replace(context, payload_digest=canonical_sha256(payload))
        arguments = {
            "history": context,
            "expected_history_lineage": expected_lineage,
            "platform_run": run,
            "platform_job": job,
        }

    with pytest.raises(
        ValueError,
        match="schema unknown field: admission_mode",
    ):
        admit(
            mode=caller_mode,
            payload=payload,
            artifact_id=context.artifact_id,
            artifact_digest=context.artifact_digest,
            **arguments,  # type: ignore[arg-type]
        )


def test_current_authority_admits_exact_current_artifact() -> None:
    """Admit only an artifact matching every current caller binding."""
    payload = _current_payload()
    context = _current_context(payload)

    result = admit(
        mode=AdmissionMode.CURRENT_AUTHORITY,
        payload=payload,
        artifact_id=7102,
        artifact_digest=context.artifact_digest,
        current=context,
    )

    assert result.mode is AdmissionMode.CURRENT_AUTHORITY
    assert result.release_execution == context.release_execution
    assert result.purpose == "live-release"
    assert result.target == "f" * 40
    assert result.control_identity == "workflow-delivery-v3"
    assert result.platform_run is None
    assert result.platform_job is None


def test_current_authority_keeps_transport_and_payload_digests_distinct() -> (
    None
):
    """Keep external artifact integrity separate from payload content."""
    payload = _current_payload()
    context = _current_context(payload)

    result = admit(
        mode=AdmissionMode.CURRENT_AUTHORITY,
        payload=payload,
        artifact_id=context.artifact_id,
        artifact_digest=context.artifact_digest,
        current=context,
    )

    assert context.artifact_digest != context.payload_digest
    assert result.artifact_digest == CURRENT_TRANSPORT_DIGEST
    assert result.payload_digest == canonical_sha256(payload)


@pytest.mark.parametrize("digest_authority", ["transport", "payload"])
def test_current_authority_validates_digest_authorities_independently(
    digest_authority: str,
) -> None:
    """Reject a mismatch in either digest without substituting the other."""
    payload = _current_payload()
    context = _current_context(payload)
    artifact_digest = context.artifact_digest
    expected_message = "artifact_digest"
    if digest_authority == "transport":
        artifact_digest = MISMATCHED_DIGEST
    else:
        context = replace(context, payload_digest=MISMATCHED_DIGEST)
        expected_message = "payload integrity"

    with pytest.raises(ValueError, match=expected_message):
        admit(
            mode=AdmissionMode.CURRENT_AUTHORITY,
            payload=payload,
            artifact_id=context.artifact_id,
            artifact_digest=artifact_digest,
            current=context,
        )


def test_execution_history_admits_pre_attempt_live_admit() -> None:
    """Admit exact history with trusted lineage and history-only authority."""
    payload, expected_lineage, context, run, job = _history_scenario()

    result = admit(
        mode=AdmissionMode.EXECUTION_HISTORY,
        payload=payload,
        artifact_id=context.artifact_id,
        artifact_digest=context.artifact_digest,
        history=context,
        expected_history_lineage=expected_lineage,
        platform_run=run,
        platform_job=job,
    )

    assert result.mode is AdmissionMode.EXECUTION_HISTORY
    assert result.history_only
    assert result.release_execution == EXPECTED_RELEASE_EXECUTION
    assert result.purpose == "live-release"
    assert result.target == "e" * 40
    assert result.control_identity == EXPECTED_CONTROL_IDENTITY
    assert result.platform_run == run
    assert result.platform_job == job


def test_execution_history_keeps_transport_and_payload_digests_distinct() -> (
    None
):
    """Keep historical artifact transport distinct from payload integrity."""
    payload, expected_lineage, context, run, job = _history_scenario()

    result = admit(
        mode=AdmissionMode.EXECUTION_HISTORY,
        payload=payload,
        artifact_id=context.artifact_id,
        artifact_digest=context.artifact_digest,
        history=context,
        expected_history_lineage=expected_lineage,
        platform_run=run,
        platform_job=job,
    )

    assert context.artifact_digest != context.payload_digest
    assert result.artifact_digest == HISTORY_TRANSPORT_DIGEST
    assert result.payload_digest == canonical_sha256(payload)


@pytest.mark.parametrize("digest_authority", ["transport", "payload"])
def test_execution_history_validates_digest_authorities_independently(
    digest_authority: str,
) -> None:
    """Reject independent historical transport and payload mismatches."""
    payload, expected_lineage, context, run, job = _history_scenario()
    artifact_digest = context.artifact_digest
    expected_message = "artifact_digest"
    if digest_authority == "transport":
        artifact_digest = MISMATCHED_DIGEST
    else:
        context = replace(context, payload_digest=MISMATCHED_DIGEST)
        expected_message = "payload integrity"

    with pytest.raises(ValueError, match=expected_message):
        admit(
            mode=AdmissionMode.EXECUTION_HISTORY,
            payload=payload,
            artifact_id=context.artifact_id,
            artifact_digest=artifact_digest,
            history=context,
            expected_history_lineage=expected_lineage,
            platform_run=run,
            platform_job=job,
        )


def test_execution_history_rejects_non_live_caller_expectation() -> None:
    """Require the trusted caller to expect the live-release purpose."""
    payload, expected_lineage, context, run, job = _history_scenario()
    expected_lineage = replace(
        expected_lineage,
        purpose="release-simulation",
    )

    with pytest.raises(ValueError, match="pre-Attempt live admit"):
        admit(
            mode=AdmissionMode.EXECUTION_HISTORY,
            payload=payload,
            artifact_id=context.artifact_id,
            artifact_digest=context.artifact_digest,
            history=context,
            expected_history_lineage=expected_lineage,
            platform_run=run,
            platform_job=job,
        )


def _admit_one_sided_invalid_primitive(primitive_case: str) -> object:
    """Build and admit one invalid primitive opposite a valid authority."""
    if primitive_case == "purpose":
        payload = _current_payload()
        context = _current_context(payload)
        payload["purpose"] = "Live-Release"
        context = replace(
            context,
            payload_digest=canonical_sha256(payload),
        )
        return _admit_current_payload(payload, context=context)
    if primitive_case == "commit-sha":
        payload, expected, context, run, job = _history_scenario()
        run = replace(run, head_sha="E" * 40)
        return _admit_history_payload(
            payload,
            expected,
            context,
            run,
            job,
        )
    if primitive_case == "workflow-run-id":
        payload = _current_payload()
        context = replace(
            _current_context(payload),
            workflow_run_id=0,
        )
        return _admit_current_payload(payload, context=context)

    payload, expected, context, run, job = _history_scenario()
    artifact_id: object = context.artifact_id
    if primitive_case == "run-attempt":
        run = replace(run, run_attempt=cast("int", 1.5))
    elif primitive_case == "artifact-id":
        artifact_id = 0
    else:
        job = replace(job, job_id=True)
    return _admit_history_payload(
        payload,
        expected,
        context,
        run,
        job,
        artifact_id=artifact_id,
    )


@pytest.mark.parametrize(
    "candidate_lineage",
    [
        replace(
            EXPECTED_HISTORY_LINEAGE,
            release_execution="buddy:other-unit:" + ("e" * 40),
        ),
        replace(EXPECTED_HISTORY_LINEAGE, target="d" * 40),
        replace(EXPECTED_HISTORY_LINEAGE, purpose="release-simulation"),
        replace(
            EXPECTED_HISTORY_LINEAGE,
            control_identity="control:" + ("d" * 40),
        ),
    ],
    ids=["cross-execution", "cross-target", "cross-purpose", "foreign-control"],
)
def test_execution_history_rejects_foreign_trusted_lineage(
    candidate_lineage: HistoryLineage,
) -> None:
    """Reject candidates outside every trusted history lineage boundary."""
    payload, expected_lineage, context, run, job = _history_scenario()
    context = replace(context, lineage=candidate_lineage)

    with pytest.raises(
        ValueError,
        match="lineage mismatch",
    ):
        admit(
            mode=AdmissionMode.EXECUTION_HISTORY,
            payload=payload,
            artifact_id=context.artifact_id,
            artifact_digest=context.artifact_digest,
            history=context,
            expected_history_lineage=expected_lineage,
            platform_run=run,
            platform_job=job,
        )


def test_execution_history_keeps_producer_self_claims_diagnostic() -> None:
    """Do not let producer workflow or job claims replace trusted lineage."""
    payload, expected_lineage, context, run, job = _history_scenario()
    payload["producer"] = "foreign-producer"
    payload["run_attempt"] = 99
    payload["reusable_workflow"] = "foreign-workflow.yml"
    context = replace(context, payload_digest=canonical_sha256(payload))

    result = admit(
        mode=AdmissionMode.EXECUTION_HISTORY,
        payload=payload,
        artifact_id=context.artifact_id,
        artifact_digest=context.artifact_digest,
        history=context,
        expected_history_lineage=expected_lineage,
        platform_run=run,
        platform_job=job,
    )

    assert result.purpose == "live-release"
    assert result.control_identity == EXPECTED_CONTROL_IDENTITY
    assert ("producer", "foreign-producer") in result.diagnostic_claims
    assert ("run_attempt", 99) in result.diagnostic_claims
    assert (
        "reusable_workflow",
        "foreign-workflow.yml",
    ) in result.diagnostic_claims
    assert ("purpose", expected_lineage.purpose) in result.diagnostic_claims
    assert (
        "control",
        expected_lineage.control_identity,
    ) in result.diagnostic_claims


def test_execution_history_cannot_satisfy_current_authority() -> None:
    """Never upgrade history for any current-authority consumer."""
    payload, expected_lineage, context, run, job = _history_scenario()

    with pytest.raises(
        ValueError,
        match="cannot satisfy current authority",
    ):
        admit(
            mode=AdmissionMode.EXECUTION_HISTORY,
            payload=payload,
            artifact_id=context.artifact_id,
            artifact_digest=context.artifact_digest,
            history=context,
            expected_history_lineage=expected_lineage,
            platform_run=run,
            platform_job=job,
            requires_current_authority=True,
        )


def _admit_current_payload(
    payload: dict[str, JsonValue],
    *,
    context: CurrentAuthorityContext | None = None,
    artifact_id: object = _NOT_SUPPLIED,
    artifact_digest: str | None = None,
):
    """Admit a current-authority payload with overridable bindings."""
    bound_context = _current_context(payload) if context is None else context
    return admit(
        mode=AdmissionMode.CURRENT_AUTHORITY,
        payload=payload,
        artifact_id=cast(
            "int",
            (
                bound_context.artifact_id
                if artifact_id is _NOT_SUPPLIED
                else artifact_id
            ),
        ),
        artifact_digest=(
            bound_context.artifact_digest
            if artifact_digest is None
            else artifact_digest
        ),
        current=bound_context,
    )


def _admit_history_payload(  # noqa: PLR0913
    payload: dict[str, JsonValue],
    expected_lineage: HistoryLineage,
    context: ExecutionHistoryContext,
    run: PlatformRunFacts,
    job: PlatformJobFacts,
    *,
    artifact_id: object = _NOT_SUPPLIED,
    artifact_digest: str | None = None,
    requires_current_authority: bool = False,
):
    """Admit a history payload with its trusted context and platform facts."""
    return admit(
        mode=AdmissionMode.EXECUTION_HISTORY,
        payload=payload,
        artifact_id=cast(
            "int",
            (
                context.artifact_id
                if artifact_id is _NOT_SUPPLIED
                else artifact_id
            ),
        ),
        artifact_digest=(
            context.artifact_digest
            if artifact_digest is None
            else artifact_digest
        ),
        history=context,
        expected_history_lineage=expected_lineage,
        platform_run=run,
        platform_job=job,
        requires_current_authority=requires_current_authority,
    )


def test_admit_rejects_unsupported_caller_mode() -> None:
    """Reject any mode not selected from the trusted enum."""
    payload = _current_payload()
    context = _current_context(payload)

    with pytest.raises(ValueError, match="unsupported admission mode"):
        admit(
            mode="current-authority",  # type: ignore[arg-type]
            payload=payload,
            artifact_id=context.artifact_id,
            artifact_digest=context.artifact_digest,
            current=context,
        )


@pytest.mark.parametrize(
    ("mode", "field"),
    [
        *(
            ("current", field)
            for field in (
                "release_execution",
                "purpose",
                "request",
                "workflow_run_id",
                "run_attempt",
                "attempt",
                "target",
                "producer",
                "control",
            )
        ),
        ("history", "execution"),
        ("history", "target"),
    ],
    ids=[
        "current-release-execution",
        "current-purpose",
        "current-request",
        "current-workflow-run-id",
        "current-run-attempt",
        "current-attempt",
        "current-target",
        "current-producer",
        "current-control",
        "history-execution",
        "history-target",
    ],
)
def test_admission_schema_rejects_missing_required_field(
    mode: str,
    field: str,
) -> None:
    """Reject each required position instead of silently defaulting it."""
    if mode == "current":
        payload = _current_payload()
        del payload[field]
        with pytest.raises(
            ValueError,
            match=f"missing required field: {field}",
        ):
            _admit_current_payload(payload)
    else:
        payload, expected, context, run, job = _history_scenario()
        del payload[field]
        context = replace(context, payload_digest=canonical_sha256(payload))
        with pytest.raises(
            ValueError,
            match=f"missing required field: {field}",
        ):
            _admit_history_payload(payload, expected, context, run, job)


@pytest.mark.parametrize(
    ("mode", "field"),
    [
        ("current", "extension"),
        ("history", "extension"),
        ("current", "admission_mode"),
        ("history", "admission_mode"),
    ],
    ids=[
        "current-arbitrary",
        "history-arbitrary",
        "current-admission-mode",
        "history-admission-mode",
    ],
)
def test_admission_schema_rejects_unknown_field(
    mode: str,
    field: str,
) -> None:
    """Reject arbitrary and authority-selecting extensions in both schemas."""
    if mode == "current":
        payload = _current_payload()
        payload[field] = "unapproved"
        with pytest.raises(ValueError, match=f"unknown field: {field}"):
            _admit_current_payload(payload)
    else:
        payload, expected, context, run, job = _history_scenario()
        payload[field] = "unapproved"
        context = replace(context, payload_digest=canonical_sha256(payload))
        with pytest.raises(ValueError, match=f"unknown field: {field}"):
            _admit_history_payload(payload, expected, context, run, job)


@pytest.mark.parametrize(
    ("field", "wrong_value"),
    [
        ("release_execution", 1),
        ("purpose", 1),
        ("request", 1),
        ("workflow_run_id", True),
        ("run_attempt", False),
        ("attempt", 1),
        ("target", 1),
        ("producer", 1),
        ("control", 1),
    ],
    ids=[
        "release-execution",
        "purpose",
        "request",
        "workflow-run-id-bool",
        "run-attempt-bool",
        "attempt",
        "target",
        "producer",
        "control",
    ],
)
def test_current_schema_rejects_wrong_json_type(
    field: str,
    wrong_value: JsonValue,
) -> None:
    """Enforce exact current JSON kinds, excluding bool from integers."""
    payload = _current_payload()
    payload[field] = wrong_value

    with pytest.raises(TypeError, match=f"wrong JSON type: {field}"):
        _admit_current_payload(payload)


@pytest.mark.parametrize(
    ("field", "wrong_value"),
    [
        ("execution", 1),
        ("target", 1),
        ("producer", 1),
        ("run_attempt", True),
        ("reusable_workflow", 1),
        ("purpose", 1),
        ("control", 1),
    ],
    ids=[
        "execution",
        "target",
        "producer",
        "run-attempt-bool",
        "reusable-workflow",
        "purpose",
        "control",
    ],
)
def test_history_schema_rejects_wrong_json_type(
    field: str,
    wrong_value: JsonValue,
) -> None:
    """Enforce types for authoritative and optional diagnostic claims."""
    payload, expected, context, run, job = _history_scenario()
    payload[field] = wrong_value
    context = replace(context, payload_digest=canonical_sha256(payload))

    with pytest.raises(TypeError, match=f"wrong JSON type: {field}"):
        _admit_history_payload(payload, expected, context, run, job)


@pytest.mark.parametrize(
    "record",
    [
        b'{"control":"workflow-delivery-v3",}',
        b'{"control":"a","control":"b"}',
        b"\xff",
        b"[]",
        b'{ "control":"workflow-delivery-v3"}',
    ],
    ids=["malformed", "duplicate", "non-utf8", "non-object", "noncanonical"],
)
def test_invalid_record_bytes_fail_before_admission(record: bytes) -> None:
    """Keep transported bytes outside admission until canonical parsing."""
    from three_workflow_delivery_v3 import (  # noqa: PLC0415
        parse_canonical_json,
    )

    with pytest.raises((TypeError, UnicodeDecodeError, ValueError)):
        parse_canonical_json(record)


def test_current_authority_admits_canonical_fixture() -> None:
    """Admit the exact closed current fixture with non-history authority."""
    payload = _current_payload()
    context = _current_context(payload)

    result = _admit_current_payload(payload, context=context)

    assert result.mode is AdmissionMode.CURRENT_AUTHORITY
    assert result.history_only is False
    assert result.release_execution == context.release_execution
    assert result.artifact_digest != result.payload_digest


@pytest.mark.parametrize(
    "field",
    [
        "release_execution",
        "purpose",
        "request",
        "workflow_run_id",
        "run_attempt",
        "attempt",
        "target",
        "producer",
        "control",
    ],
    ids=[
        "release-execution",
        "purpose",
        "request",
        "workflow-run-id",
        "run-attempt",
        "attempt",
        "target",
        "producer",
        "control",
    ],
)
def test_current_authority_rejects_each_payload_binding_mismatch(
    field: str,
) -> None:
    """Reject each independent current binding mutation."""
    payload = _current_payload()
    context = _current_context(payload)
    value = payload[field]
    if field in {"workflow_run_id", "run_attempt"}:
        assert isinstance(value, int)
        payload[field] = value + 1
    elif field == "purpose":
        payload[field] = "release-simulation"
    elif field == "target":
        payload[field] = "e" * 40
    else:
        payload[field] = f"foreign-{value}"

    with pytest.raises(ValueError, match=f"binding mismatch: {field}"):
        _admit_current_payload(payload, context=context)


def test_current_authority_rejects_missing_context() -> None:
    """Require explicit trusted current context."""
    payload = _current_payload()

    with pytest.raises(ValueError, match="context is required"):
        admit(
            mode=AdmissionMode.CURRENT_AUTHORITY,
            payload=payload,
            artifact_id=7102,
            artifact_digest=CURRENT_TRANSPORT_DIGEST,
        )


def test_current_authority_rejects_artifact_id_substitution() -> None:
    """Reject an artifact ID outside the current trusted binding."""
    payload = _current_payload()
    context = _current_context(payload)

    with pytest.raises(ValueError, match="artifact_id"):
        _admit_current_payload(payload, context=context, artifact_id=9999)


def test_current_authority_rejects_transport_digest_mismatch() -> None:
    """Reject a mismatched current transport digest."""
    payload = _current_payload()

    with pytest.raises(ValueError, match="artifact_digest"):
        _admit_current_payload(payload, artifact_digest=MISMATCHED_DIGEST)


def test_current_authority_rejects_payload_digest_mismatch() -> None:
    """Reject a mismatched current payload digest."""
    payload = _current_payload()
    context = replace(
        _current_context(payload),
        payload_digest=MISMATCHED_DIGEST,
    )

    with pytest.raises(ValueError, match="payload integrity"):
        _admit_current_payload(payload, context=context)


@pytest.mark.parametrize(
    "direction",
    ["transport-as-payload", "payload-as-transport"],
)
def test_current_authority_rejects_cross_substituted_digest_authorities(
    direction: str,
) -> None:
    """Reject exchanging current transport and payload digest authorities."""
    payload = _current_payload()
    context = _current_context(payload)
    artifact_digest = context.artifact_digest
    if direction == "transport-as-payload":
        context = replace(context, payload_digest=context.artifact_digest)
    else:
        artifact_digest = context.payload_digest

    with pytest.raises(ValueError, match=r"digest|integrity"):
        _admit_current_payload(
            payload,
            context=context,
            artifact_digest=artifact_digest,
        )


_MALFORMED_DIGESTS = [
    ("missing-prefix", "a" * 64),
    ("uppercase-hex", "sha256:" + ("A" * 64)),
    ("wrong-length", "sha256:" + ("a" * 63)),
    ("non-hex", "sha256:" + ("g" * 64)),
]


@pytest.mark.parametrize(
    ("authority", "malformation", "digest"),
    [
        (authority, malformation, digest)
        for authority in ("transport", "payload")
        for malformation, digest in _MALFORMED_DIGESTS
    ],
    ids=[
        f"{authority}-{malformation}"
        for authority in ("transport", "payload")
        for malformation, _ in _MALFORMED_DIGESTS
    ],
)
def test_current_authority_rejects_malformed_digest_authority(
    authority: str,
    malformation: str,
    digest: str,
) -> None:
    """Reject malformed current digest authorities."""
    del malformation
    payload = _current_payload()
    context = _current_context(payload)
    artifact_digest = context.artifact_digest
    if authority == "transport":
        artifact_digest = digest
        context = replace(context, artifact_digest=digest)
    else:
        context = replace(context, payload_digest=digest)

    expected_authority = (
        "artifact_digest" if authority == "transport" else "payload_digest"
    )
    with pytest.raises(ValueError, match=f"malformed {expected_authority}"):
        _admit_current_payload(
            payload,
            context=context,
            artifact_digest=artifact_digest,
        )


def test_current_authority_rejects_prior_run_attempt() -> None:
    """Reject a current context bound to another run attempt."""
    payload = _current_payload()
    context = replace(_current_context(payload), run_attempt=4)

    with pytest.raises(ValueError, match="run_attempt"):
        _admit_current_payload(payload, context=context)


def test_current_authority_rejects_prior_attempt_identity() -> None:
    """Reject a current context bound to another attempt identity."""
    payload = _current_payload()
    context = replace(_current_context(payload), attempt="attempt-10")

    with pytest.raises(ValueError, match="attempt"):
        _admit_current_payload(payload, context=context)


def test_current_authority_rejects_history_shaped_payload() -> None:
    """Reject a history payload under current authority."""
    payload = _history_payload()

    with pytest.raises(ValueError, match="missing required field"):
        _admit_current_payload(payload)


def test_current_authority_rejects_payload_admission_mode() -> None:
    """Reject a payload-provided admission mode under current authority."""
    payload = _current_payload()
    payload["admission_mode"] = "execution-history"

    with pytest.raises(ValueError, match="unknown field: admission_mode"):
        _admit_current_payload(payload)


def test_execution_history_admits_canonical_fixture_pre_attempt_live_admit() -> (  # noqa: E501
    None
):
    """Admit a canonical history fixture before creating an attempt."""
    payload, expected, context, run, job = _history_scenario()

    result = _admit_history_payload(payload, expected, context, run, job)

    assert result.mode is AdmissionMode.EXECUTION_HISTORY
    assert result.history_only is True
    assert result.release_execution == expected.release_execution
    assert result.artifact_digest != result.payload_digest


@pytest.mark.parametrize(
    "missing",
    ["history", "expected-lineage", "platform-run", "platform-job"],
)
def test_execution_history_rejects_each_missing_trusted_input(
    missing: str,
) -> None:
    """Reject each absent trusted input required for history admission."""
    payload, expected, context, run, job = _history_scenario()
    arguments = {
        "history": context,
        "expected_history_lineage": expected,
        "platform_run": run,
        "platform_job": job,
    }
    argument_name = {
        "history": "history",
        "expected-lineage": "expected_history_lineage",
        "platform-run": "platform_run",
        "platform-job": "platform_job",
    }[missing]
    del arguments[argument_name]

    with pytest.raises(
        ValueError,
        match="separate platform facts are required",
    ):
        admit(
            mode=AdmissionMode.EXECUTION_HISTORY,
            payload=payload,
            artifact_id=context.artifact_id,
            artifact_digest=context.artifact_digest,
            **arguments,  # type: ignore[arg-type]
        )


def test_execution_history_rejects_current_authority_requirement() -> None:
    """Reject history when the consumer requires current authority."""
    payload, expected, context, run, job = _history_scenario()

    with pytest.raises(ValueError, match="cannot satisfy current authority"):
        _admit_history_payload(
            payload,
            expected,
            context,
            run,
            job,
            requires_current_authority=True,
        )


def test_execution_history_rejects_non_live_expected_purpose() -> None:
    """Reject history outside the live-release purpose."""
    payload, expected, context, run, job = _history_scenario()
    expected = replace(expected, purpose="release-simulation")

    with pytest.raises(ValueError, match="pre-Attempt live admit"):
        _admit_history_payload(payload, expected, context, run, job)


def test_execution_history_rejects_non_admit_operation() -> None:
    """Reject history outside the admit operation."""
    payload, expected, context, run, job = _history_scenario()
    context = replace(context, operation="inspect")

    with pytest.raises(ValueError, match="pre-Attempt live admit"):
        _admit_history_payload(payload, expected, context, run, job)


def test_execution_history_rejects_existing_attempt() -> None:
    """Reject history after an attempt has been created."""
    payload, expected, context, run, job = _history_scenario()
    context = replace(context, attempt_created=True)

    with pytest.raises(ValueError, match="pre-Attempt live admit"):
        _admit_history_payload(payload, expected, context, run, job)


@pytest.mark.parametrize(
    ("dimension", "candidate"),
    [
        (
            "release_execution",
            replace(
                EXPECTED_HISTORY_LINEAGE,
                release_execution="buddy:foreign:" + ("e" * 40),
            ),
        ),
        ("target", replace(EXPECTED_HISTORY_LINEAGE, target="d" * 40)),
        (
            "purpose",
            replace(EXPECTED_HISTORY_LINEAGE, purpose="release-simulation"),
        ),
        (
            "control_identity",
            replace(
                EXPECTED_HISTORY_LINEAGE,
                control_identity="control:foreign",
            ),
        ),
    ],
    ids=["execution", "target", "purpose", "control"],
)
def test_execution_history_rejects_each_foreign_candidate_lineage(
    dimension: str,
    candidate: HistoryLineage,
) -> None:
    """Reject each foreign candidate lineage dimension."""
    payload, expected, context, run, job = _history_scenario()
    context = replace(context, lineage=candidate)

    with pytest.raises(ValueError, match=f"lineage mismatch: {dimension}"):
        _admit_history_payload(payload, expected, context, run, job)


def test_execution_history_rejects_payload_execution_mismatch() -> None:
    """Reject a payload execution outside the trusted lineage."""
    payload, expected, context, run, job = _history_scenario()
    payload["execution"] = "buddy:foreign:" + ("e" * 40)
    context = replace(context, payload_digest=canonical_sha256(payload))

    with pytest.raises(ValueError, match="release_execution"):
        _admit_history_payload(payload, expected, context, run, job)


def test_execution_history_rejects_payload_target_mismatch() -> None:
    """Reject a payload target outside the trusted lineage."""
    payload, expected, context, run, job = _history_scenario()
    payload["target"] = "d" * 40
    context = replace(context, payload_digest=canonical_sha256(payload))

    with pytest.raises(ValueError, match="target"):
        _admit_history_payload(payload, expected, context, run, job)


def test_execution_history_rejects_artifact_id_substitution() -> None:
    """Reject a substituted history artifact ID."""
    payload, expected, context, run, job = _history_scenario()

    with pytest.raises(ValueError, match="artifact_id"):
        _admit_history_payload(
            payload,
            expected,
            context,
            run,
            job,
            artifact_id=9999,
        )


def test_execution_history_rejects_transport_digest_mismatch() -> None:
    """Reject a mismatched history transport digest."""
    payload, expected, context, run, job = _history_scenario()

    with pytest.raises(ValueError, match="artifact_digest"):
        _admit_history_payload(
            payload,
            expected,
            context,
            run,
            job,
            artifact_digest=MISMATCHED_DIGEST,
        )


def test_execution_history_rejects_source_workflow_run_mismatch() -> None:
    """Reject a mismatched source workflow run."""
    payload, expected, context, run, job = _history_scenario()
    run = replace(run, workflow_run_id=4491)

    with pytest.raises(ValueError, match="source_workflow_run_id"):
        _admit_history_payload(payload, expected, context, run, job)


def test_execution_history_rejects_run_head_sha_mismatch() -> None:
    """Reject a run whose head SHA differs from trusted lineage."""
    payload, expected, context, run, job = _history_scenario()
    run = replace(run, head_sha="d" * 40)

    with pytest.raises(ValueError, match="head_sha"):
        _admit_history_payload(payload, expected, context, run, job)


@pytest.mark.parametrize(
    "metadata",
    [
        (("artifact_created_at", "2026-08-06T18:11:00Z"),),
    ],
    ids=["artifact-created-at"],
)
def test_execution_history_rejects_each_exposed_metadata_mismatch(
    metadata: tuple[tuple[str, str], ...],
) -> None:
    """Reject each mismatched item of exposed platform metadata."""
    payload, expected, context, run, job = _history_scenario()
    run = replace(run, exposed_metadata=metadata)

    with pytest.raises(ValueError, match="exposed_platform_metadata"):
        _admit_history_payload(payload, expected, context, run, job)


@pytest.mark.parametrize(
    ("run_changes", "job_changes", "error_pattern"),
    [
        (
            {"run_attempt": 0},
            {},
            r"(?i)^(?=.*execution-history)(?=.*run_attempt)"
            r"(?=.*positive.*integer).*$",
        ),
        (
            {},
            {"job_id": 0},
            r"(?i)^(?=.*execution-history)(?=.*job_id)"
            r"(?=.*positive.*integer).*$",
        ),
        (
            {},
            {"conclusion": "failure"},
            r"phase fact mismatch: conclusion",
        ),
        (
            {},
            {"phase": "pending"},
            r"phase fact mismatch: phase",
        ),
    ],
    ids=["run-attempt", "job-id", "conclusion", "phase"],
)
def test_execution_history_rejects_each_queried_phase_fact_mismatch(
    run_changes: dict[str, int],
    job_changes: dict[str, int | str],
    error_pattern: str,
) -> None:
    """Reject each invalid separately queried phase fact."""
    payload, expected, context, run, job = _history_scenario()
    run = replace(run, **run_changes)  # type: ignore[bad-argument-type]
    job = replace(job, **job_changes)  # type: ignore[bad-argument-type]

    with pytest.raises(ValueError, match=error_pattern):
        _admit_history_payload(payload, expected, context, run, job)


def test_execution_history_rejects_payload_digest_mismatch() -> None:
    """Reject a mismatched history payload digest."""
    payload, expected, context, run, job = _history_scenario()
    context = replace(context, payload_digest=MISMATCHED_DIGEST)

    with pytest.raises(ValueError, match="payload integrity"):
        _admit_history_payload(payload, expected, context, run, job)


@pytest.mark.parametrize(
    "direction",
    ["transport-as-payload", "payload-as-transport"],
)
def test_execution_history_rejects_cross_substituted_digest_authorities(
    direction: str,
) -> None:
    """Reject exchanging history transport and payload digest authorities."""
    payload, expected, context, run, job = _history_scenario()
    artifact_digest = context.artifact_digest
    if direction == "transport-as-payload":
        context = replace(context, payload_digest=context.artifact_digest)
    else:
        artifact_digest = context.payload_digest

    with pytest.raises(ValueError, match=r"digest|integrity"):
        _admit_history_payload(
            payload,
            expected,
            context,
            run,
            job,
            artifact_digest=artifact_digest,
        )


@pytest.mark.parametrize(
    ("authority", "malformation", "digest"),
    [
        (authority, malformation, digest)
        for authority in ("transport", "payload")
        for malformation, digest in _MALFORMED_DIGESTS
    ],
    ids=[
        f"{authority}-{malformation}"
        for authority in ("transport", "payload")
        for malformation, _ in _MALFORMED_DIGESTS
    ],
)
def test_execution_history_rejects_malformed_digest_authority(
    authority: str,
    malformation: str,
    digest: str,
) -> None:
    """Reject malformed history digest authorities."""
    del malformation
    payload, expected, context, run, job = _history_scenario()
    artifact_digest = context.artifact_digest
    if authority == "transport":
        artifact_digest = digest
        context = replace(context, artifact_digest=digest)
    else:
        context = replace(context, payload_digest=digest)

    with pytest.raises(ValueError, match="malformed"):
        _admit_history_payload(
            payload,
            expected,
            context,
            run,
            job,
            artifact_digest=artifact_digest,
        )


def test_execution_history_rejects_current_shaped_payload() -> None:
    """Reject a current payload under history authority."""
    payload = _current_payload()
    _, expected, context, run, job = _history_scenario()
    context = replace(context, payload_digest=canonical_sha256(payload))

    with pytest.raises(ValueError, match="missing required field: execution"):
        _admit_history_payload(payload, expected, context, run, job)


def test_execution_history_rejects_payload_admission_mode() -> None:
    """Reject a payload-provided admission mode under history authority."""
    payload, expected, context, run, job = _history_scenario()
    payload["admission_mode"] = "current-authority"
    context = replace(context, payload_digest=canonical_sha256(payload))

    with pytest.raises(ValueError, match="unknown field: admission_mode"):
        _admit_history_payload(payload, expected, context, run, job)


def test_execution_history_rejects_arbitrary_unknown_field() -> None:
    """Reject an arbitrary field outside the closed history schema."""
    payload, expected, context, run, job = _history_scenario()
    payload["extension"] = "unapproved"
    context = replace(context, payload_digest=canonical_sha256(payload))

    with pytest.raises(ValueError, match="unknown field: extension"):
        _admit_history_payload(payload, expected, context, run, job)


@pytest.mark.parametrize(
    ("claim", "changed"),
    [
        ("producer", "foreign-producer"),
        ("run_attempt", 99),
        ("reusable_workflow", "foreign.yml"),
    ],
    ids=["producer", "run-attempt", "reusable-workflow"],
)
def test_execution_history_accepts_each_changed_diagnostic_self_claim(
    claim: str,
    changed: JsonValue,
) -> None:
    """Accept changed diagnostic claims without granting them authority."""
    payload, expected, context, run, job = _history_scenario()
    payload[claim] = changed
    context = replace(context, payload_digest=canonical_sha256(payload))

    result = _admit_history_payload(payload, expected, context, run, job)

    assert (claim, changed) in result.diagnostic_claims
    assert result.release_execution == expected.release_execution
    assert result.target == expected.target
    assert result.purpose == "live-release"
    assert result.control_identity == expected.control_identity


@pytest.mark.parametrize(
    "claim",
    ["producer", "run_attempt", "reusable_workflow", "purpose", "control"],
    ids=["producer", "run-attempt", "reusable-workflow", "purpose", "control"],
)
def test_execution_history_accepts_each_omitted_diagnostic_self_claim(
    claim: str,
) -> None:
    """Accept each independently omitted optional diagnostic claim."""
    payload, expected, context, run, job = _history_scenario()
    del payload[claim]
    context = replace(context, payload_digest=canonical_sha256(payload))

    result = _admit_history_payload(payload, expected, context, run, job)

    assert claim not in dict(result.diagnostic_claims)
    assert result.history_only is True
    assert result.release_execution == expected.release_execution


def test_execution_history_accepts_all_diagnostic_self_claims_omitted() -> None:
    """Accept a history payload containing no diagnostic self-claims."""
    payload, expected, context, run, job = _history_scenario()
    payload = {"execution": payload["execution"], "target": payload["target"]}
    context = replace(context, payload_digest=canonical_sha256(payload))

    result = _admit_history_payload(payload, expected, context, run, job)

    assert result.diagnostic_claims == ()
    assert result.history_only is True
    assert result.control_identity == expected.control_identity


@pytest.mark.parametrize(
    ("dimension", "lineage"),
    [
        (
            "release_execution",
            replace(
                EXPECTED_HISTORY_LINEAGE,
                release_execution="buddy:foreign:" + ("e" * 40),
            ),
        ),
        ("target", replace(EXPECTED_HISTORY_LINEAGE, target="d" * 40)),
        (
            "purpose",
            replace(EXPECTED_HISTORY_LINEAGE, purpose="release-simulation"),
        ),
        (
            "control_identity",
            replace(
                EXPECTED_HISTORY_LINEAGE,
                control_identity="foreign-control",
            ),
        ),
    ],
    ids=["execution", "target", "purpose", "control"],
)
def test_execution_history_diagnostics_cannot_replace_trusted_lineage(
    dimension: str,
    lineage: HistoryLineage,
) -> None:
    """Prevent diagnostic claims from replacing trusted lineage."""
    payload, expected, context, run, job = _history_scenario()
    payload["purpose"] = expected.purpose
    payload["control"] = expected.control_identity
    context = replace(
        context,
        lineage=lineage,
        payload_digest=canonical_sha256(payload),
    )

    with pytest.raises(ValueError, match=dimension):
        _admit_history_payload(payload, expected, context, run, job)


@pytest.mark.parametrize(
    ("fact", "run_changes", "job_changes"),
    [
        ("run_attempt", {"run_attempt": 0}, {}),
        ("job_id", {}, {"job_id": 0}),
        ("conclusion", {}, {"conclusion": "failure"}),
        ("phase", {}, {"phase": "pending"}),
    ],
    ids=["run-attempt", "job-id", "conclusion", "phase"],
)
def test_execution_history_diagnostics_cannot_replace_platform_facts(
    fact: str,
    run_changes: dict[str, int],
    job_changes: dict[str, int | str],
) -> None:
    """Prevent diagnostics from replacing separately queried platform facts."""
    payload, expected, context, run, job = _history_scenario()
    payload["run_attempt"] = 2
    context = replace(context, payload_digest=canonical_sha256(payload))
    run = replace(run, **run_changes)  # type: ignore[bad-argument-type]
    job = replace(job, **job_changes)  # type: ignore[bad-argument-type]

    with pytest.raises(ValueError, match=fact):
        _admit_history_payload(payload, expected, context, run, job)


@pytest.mark.parametrize(
    ("payload", "case"),
    [
        (None, "null"),
        (False, "boolean"),
        (0, "number"),
        ("record", "string"),
        ([], "array"),
    ],
    ids=["null", "boolean", "number", "string", "array"],
)
@pytest.mark.parametrize(
    "mode",
    [AdmissionMode.CURRENT_AUTHORITY, AdmissionMode.EXECUTION_HISTORY],
    ids=["current", "history"],
)
def test_admit_rejects_every_non_object_json_value_in_both_caller_modes(
    payload: JsonValue,
    case: str,
    mode: AdmissionMode,
) -> None:
    """Apply the object guard directly in each caller-selected branch."""
    del case

    with pytest.raises(
        TypeError,
        match="admission payload must be a JSON object",
    ):
        admit(
            mode=mode,
            payload=payload,
            artifact_id=1,
            artifact_digest=CURRENT_TRANSPORT_DIGEST,
        )


@pytest.mark.parametrize(
    ("mode", "malformed"),
    [
        (AdmissionMode.CURRENT_AUTHORITY, "sha256:" + ("A" * 64)),
        (AdmissionMode.EXECUTION_HISTORY, "sha256:" + ("A" * 64)),
    ],
    ids=["current", "history"],
)
def test_admit_rejects_malformed_trusted_artifact_digest_with_valid_transport(
    mode: AdmissionMode,
    malformed: str,
) -> None:
    """Validate the trusted digest independently of valid transport input."""
    if mode is AdmissionMode.CURRENT_AUTHORITY:
        payload = _current_payload()
        context = replace(_current_context(payload), artifact_digest=malformed)
        with pytest.raises(
            ValueError,
            match="current-authority malformed artifact_digest",
        ):
            _admit_current_payload(
                payload,
                context=context,
                artifact_digest=CURRENT_TRANSPORT_DIGEST,
            )
    else:
        payload, expected, context, run, job = _history_scenario()
        context = replace(context, artifact_digest=malformed)
        with pytest.raises(
            ValueError,
            match="execution-history malformed artifact_digest",
        ):
            _admit_history_payload(
                payload,
                expected,
                context,
                run,
                job,
                artifact_digest=HISTORY_TRANSPORT_DIGEST,
            )


def test_execution_history_admits_positive_phase_fact_lower_boundaries() -> (
    None
):
    """Admit the inclusive positive boundaries for run attempt and job ID."""
    payload, expected, context, run, job = _history_scenario()
    run = replace(run, run_attempt=1)
    job = replace(job, job_id=1)

    result = _admit_history_payload(payload, expected, context, run, job)

    assert result.platform_run == run
    assert result.platform_job == job
    assert result.history_only is True


def _load_canonical_binding_fixture(
    fixture_name: str,
) -> dict[str, JsonValue]:
    from pathlib import Path  # noqa: PLC0415

    from three_workflow_delivery_v3 import (  # noqa: PLC0415
        parse_canonical_json,
    )

    fixture = (
        Path(__file__).parent / "fixtures" / "bindings" / f"{fixture_name}.json"
    )
    return parse_canonical_json(fixture.read_bytes())


def test_current_authority_admits_parsed_canonical_binding_fixture() -> None:
    """Parse and admit the reviewed canonical current fixture."""
    payload = _load_canonical_binding_fixture("current-authority")
    context = _current_context(payload)

    result = _admit_current_payload(payload, context=context)

    assert result.mode is AdmissionMode.CURRENT_AUTHORITY
    assert result.release_execution == payload["release_execution"]
    assert result.payload_digest == canonical_sha256(payload)
    assert result.history_only is False


def test_execution_history_admits_parsed_canonical_binding_fixture() -> None:
    """Parse and admit the reviewed canonical history fixture."""
    payload = _load_canonical_binding_fixture("execution-history")
    _, expected, context, run, job = _history_scenario()
    context = replace(context, payload_digest=canonical_sha256(payload))

    result = _admit_history_payload(payload, expected, context, run, job)

    assert result.mode is AdmissionMode.EXECUTION_HISTORY
    assert result.release_execution == payload["execution"]
    assert result.diagnostic_claims == tuple(
        (name, payload[name])
        for name in (
            "producer",
            "run_attempt",
            "reusable_workflow",
            "purpose",
            "control",
        )
    )
    assert result.history_only is True


@pytest.mark.parametrize(
    "mode",
    [AdmissionMode.CURRENT_AUTHORITY, AdmissionMode.EXECUTION_HISTORY],
    ids=["current", "history"],
)
def test_closed_schema_rejection_precedes_absent_trusted_authority(
    mode: AdmissionMode,
) -> None:
    """Reject a closed-schema violation before checking absent authority."""
    payload = (
        _current_payload()
        if mode is AdmissionMode.CURRENT_AUTHORITY
        else _history_payload()
    )
    payload["unapproved"] = "value"

    with pytest.raises(
        ValueError,
        match=f"{mode.value} schema unknown field: unapproved",
    ):
        admit(
            mode=mode,
            payload=payload,
            artifact_id=1,
            artifact_digest=CURRENT_TRANSPORT_DIGEST,
            requires_current_authority=(
                mode is AdmissionMode.EXECUTION_HISTORY
            ),
        )


@pytest.mark.parametrize(
    ("mode", "authority"),
    [
        (AdmissionMode.CURRENT_AUTHORITY, "supplied-artifact-digest"),
        (AdmissionMode.CURRENT_AUTHORITY, "trusted-artifact-digest"),
        (AdmissionMode.CURRENT_AUTHORITY, "trusted-payload-digest"),
        (AdmissionMode.EXECUTION_HISTORY, "supplied-artifact-digest"),
        (AdmissionMode.EXECUTION_HISTORY, "trusted-artifact-digest"),
        (AdmissionMode.EXECUTION_HISTORY, "trusted-payload-digest"),
    ],
    ids=[
        "current-supplied-artifact-digest",
        "current-trusted-artifact-digest",
        "current-trusted-payload-digest",
        "history-supplied-artifact-digest",
        "history-trusted-artifact-digest",
        "history-trusted-payload-digest",
    ],
)
def test_admit_rejects_non_string_digest_authority_with_domain_error(
    mode: AdmissionMode,
    authority: str,
) -> None:
    """Reject every non-string digest authority before regex matching."""
    non_string_digest: JsonValue = 17
    digest_name = (
        "payload_digest"
        if authority == "trusted-payload-digest"
        else "artifact_digest"
    )

    def admit_with_non_string_digest():
        if mode is AdmissionMode.CURRENT_AUTHORITY:
            payload = _current_payload()
            context = _current_context(payload)
            if authority == "trusted-artifact-digest":
                context = replace(
                    context,
                    artifact_digest=non_string_digest,  # type: ignore[bad-argument-type]
                )
            elif authority == "trusted-payload-digest":
                context = replace(
                    context,
                    payload_digest=non_string_digest,  # type: ignore[bad-argument-type]
                )
            _admit_current_payload(
                payload,
                context=context,
                artifact_digest=(
                    non_string_digest  # type: ignore[bad-argument-type]
                    if authority == "supplied-artifact-digest"
                    else CURRENT_TRANSPORT_DIGEST
                ),
            )
        else:
            payload, expected, context, run, job = _history_scenario()
            if authority == "trusted-artifact-digest":
                context = replace(
                    context,
                    artifact_digest=non_string_digest,  # type: ignore[bad-argument-type]
                )
            elif authority == "trusted-payload-digest":
                context = replace(
                    context,
                    payload_digest=non_string_digest,  # type: ignore[bad-argument-type]
                )
            _admit_history_payload(
                payload,
                expected,
                context,
                run,
                job,
                artifact_digest=(
                    non_string_digest  # type: ignore[bad-argument-type]
                    if authority == "supplied-artifact-digest"
                    else HISTORY_TRANSPORT_DIGEST
                ),
            )

    with pytest.raises(
        ValueError,
        match=rf"^{mode.value} malformed {digest_name}$",
    ):
        admit_with_non_string_digest()


def _admit_history_with_source_attempt_facts(
    *,
    current_workflow_run_id: object,
    current_run_attempt: object,
    verified_prior_attempts: object,
    source_run_attempt: object = 2,
):
    """Admit history with trusted current-run and source-attempt facts."""
    payload, expected, context, run, job = _history_scenario()
    run = replace(
        run,
        run_attempt=source_run_attempt,  # type: ignore[bad-argument-type]
    )
    context = replace(
        context,
        current_workflow_run_id=current_workflow_run_id,  # type: ignore[bad-argument-type]
        current_run_attempt=current_run_attempt,  # type: ignore[bad-argument-type]
    )
    result = admit(
        mode=AdmissionMode.EXECUTION_HISTORY,
        payload=payload,
        artifact_id=context.artifact_id,
        artifact_digest=context.artifact_digest,
        history=context,
        expected_history_lineage=expected,
        platform_run=run,
        platform_job=job,
        verified_prior_attempts=verified_prior_attempts,  # type: ignore[bad-argument-type]
    )
    return result, payload, expected, context, run, job


@pytest.mark.parametrize(
    ("source_run_attempt", "current_run_attempt"),
    [(1, 2), (2, 3)],
    ids=["positive-lower-bound", "representative-prior-attempt"],
)
def test_execution_history_admits_verified_earlier_attempt_from_current_run(
    source_run_attempt: int,
    current_run_attempt: int,
) -> None:
    """Admit a verified positive source attempt strictly before the current."""
    result, payload, expected, context, run, job = (
        _admit_history_with_source_attempt_facts(
            current_workflow_run_id=4490,
            current_run_attempt=current_run_attempt,
            verified_prior_attempts=(source_run_attempt,),
            source_run_attempt=source_run_attempt,
        )
    )

    assert result.mode is AdmissionMode.EXECUTION_HISTORY
    assert result.history_only is True
    assert result.release_execution == expected.release_execution
    assert result.purpose == expected.purpose
    assert result.target == expected.target
    assert result.control_identity == expected.control_identity
    assert result.artifact_digest == context.artifact_digest
    assert result.payload_digest == canonical_sha256(payload)
    assert result.platform_run == run
    assert result.platform_job == job


@pytest.mark.parametrize(
    (
        "source_run_attempt",
        "current_run_attempt",
        "verified_prior_attempts",
        "error_pattern",
    ),
    [
        (3, 3, (3,), r"source.*run.attempt|run.attempt.*source"),
        (4, 3, (4,), r"source.*run.attempt|run.attempt.*source"),
        (2, 3, (), r"verified.*run.attempt|run.attempt.*verif"),
        (2, 3, (1,), r"verified.*run.attempt|run.attempt.*verif"),
        (True, 3, (True,), r"source.*run.attempt|run.attempt.*source"),
        (False, 3, (False,), r"source.*run.attempt|run.attempt.*source"),
        (None, 3, (None,), r"source.*run.attempt|run.attempt.*source"),
        (0, 3, (0,), r"source.*run.attempt|run.attempt.*source"),
        (-1, 3, (-1,), r"source.*run.attempt|run.attempt.*source"),
        (2.5, 3, (2.5,), r"source.*run.attempt|run.attempt.*source"),
        ("2", 3, ("2",), r"source.*run.attempt|run.attempt.*source"),
        (2, 3, ("malformed",), r"verified.*run.attempt|run.attempt.*verif"),
    ],
    ids=[
        "equal-attempt",
        "future-attempt",
        "missing-existence-fact",
        "unverified-source-attempt",
        "boolean-true-source-attempt",
        "boolean-false-source-attempt",
        "none-source-attempt",
        "zero-source-attempt",
        "negative-source-attempt",
        "float-source-attempt",
        "string-source-attempt",
        "malformed-existence-fact",
    ],
)
def test_execution_history_rejects_invalid_same_run_prior_attempt(
    source_run_attempt: object,
    current_run_attempt: object,
    verified_prior_attempts: object,
    error_pattern: str,
) -> None:
    """Reject source attempts not earlier, verified positive integers."""
    with pytest.raises(ValueError, match=error_pattern):
        _admit_history_with_source_attempt_facts(
            current_workflow_run_id=4490,
            current_run_attempt=current_run_attempt,
            verified_prior_attempts=verified_prior_attempts,
            source_run_attempt=source_run_attempt,
        )


@pytest.mark.parametrize(
    ("endpoint", "invalid_value"),
    [
        (endpoint, invalid)
        for endpoint in ("current-workflow-run-id", "current-run-attempt")
        for _, invalid in (
            ("boolean-true", True),
            ("boolean-false", False),
            ("none", None),
            ("zero", 0),
            ("negative", -1),
            ("positive-float", 1.5),
            ("numeric-string", "1"),
        )
    ],
    ids=[
        f"{endpoint}-{invalid_class}"
        for endpoint in ("current-workflow-run-id", "current-run-attempt")
        for invalid_class in (
            "boolean-true",
            "boolean-false",
            "none",
            "zero",
            "negative",
            "positive-float",
            "numeric-string",
        )
    ],
)
def test_execution_history_rejects_invalid_current_admission_integer(
    endpoint: str,
    invalid_value: object,
) -> None:
    """Validate trusted current run facts before same-run correlation."""
    current_workflow_run_id: object = 4490
    current_run_attempt: object = 3
    if endpoint == "current-workflow-run-id":
        current_workflow_run_id = invalid_value
        field = "current_workflow_run_id"
    else:
        current_run_attempt = invalid_value
        field = "current_run_attempt"

    with pytest.raises(
        (TypeError, ValueError),
        match=_positive_integer_error_pattern(
            "execution-history",
            field,
        ),
    ):
        _admit_history_with_source_attempt_facts(
            current_workflow_run_id=current_workflow_run_id,
            current_run_attempt=current_run_attempt,
            verified_prior_attempts=(2,),
        )


@pytest.mark.parametrize(
    "verification_fact",
    [True, 1.0],
    ids=["boolean-equals-one", "float-equals-one"],
)
def test_execution_history_rejects_wrong_but_equal_verification_fact(
    verification_fact: object,
) -> None:
    """Do not let Python equality turn an untyped fact into verification."""
    with pytest.raises(
        (TypeError, ValueError),
        match=r"verified.*run.attempt|run.attempt.*verif",
    ):
        _admit_history_with_source_attempt_facts(
            current_workflow_run_id=4490,
            current_run_attempt=2,
            verified_prior_attempts=(verification_fact,),
            source_run_attempt=1,
        )


@pytest.mark.parametrize(
    "verification_fact",
    [False, None, 0, -1, 1.5, "2"],
    ids=[
        "boolean-false",
        "none",
        "zero",
        "negative",
        "positive-float",
        "numeric-string",
    ],
)
def test_execution_history_rejects_invalid_verified_prior_attempt_fact(
    verification_fact: object,
) -> None:
    """Reject malformed entries before using the verified-attempt set."""
    with pytest.raises(
        (TypeError, ValueError),
        match=(
            r"(?i)^(?=.*execution-history)(?=.*verified)"
            r"(?=.*run_attempt)(?=.*positive.*integer).*$"
        ),
    ):
        _admit_history_with_source_attempt_facts(
            current_workflow_run_id=4490,
            current_run_attempt=3,
            verified_prior_attempts=(verification_fact,),
            source_run_attempt=2,
        )


@pytest.mark.parametrize(
    "verified_prior_attempts",
    [None, True, 2, "2"],
    ids=["none", "boolean", "integer", "string"],
)
def test_execution_history_rejects_malformed_verified_attempt_collection(
    verified_prior_attempts: object,
) -> None:
    """Require an explicit collection of verified attempt facts."""
    with pytest.raises(
        (TypeError, ValueError),
        match=r"verified.*run.attempt|run.attempt.*verif",
    ):
        _admit_history_with_source_attempt_facts(
            current_workflow_run_id=4490,
            current_run_attempt=3,
            verified_prior_attempts=verified_prior_attempts,
            source_run_attempt=2,
        )


def test_execution_history_preserves_approved_cross_run_facts() -> None:
    """Do not impose the same-run ordering or existence proof across runs."""
    result, payload, expected, context, run, job = (
        _admit_history_with_source_attempt_facts(
            current_workflow_run_id=4501,
            current_run_attempt=1,
            verified_prior_attempts=(),
            source_run_attempt=4,
        )
    )

    assert result.mode is AdmissionMode.EXECUTION_HISTORY
    assert result.history_only is True
    assert result.release_execution == expected.release_execution
    assert result.purpose == expected.purpose
    assert result.target == run.head_sha == expected.target
    assert result.control_identity == expected.control_identity
    assert result.artifact_digest == context.artifact_digest
    assert result.payload_digest == context.payload_digest
    assert result.payload_digest == canonical_sha256(payload)
    assert result.platform_run == run
    assert result.platform_job == job
    assert run.workflow_run_id == context.source_workflow_run_id
    assert run.exposed_metadata == context.exposed_platform_metadata


@pytest.mark.parametrize(
    "endpoint",
    ["current-workflow-run-id", "current-run-attempt"],
)
def test_execution_history_validates_current_context_for_cross_run(
    endpoint: str,
) -> None:
    """Validate current run primitives even when the source run differs."""
    current_workflow_run_id = 4501
    current_run_attempt = 1
    if endpoint == "current-workflow-run-id":
        current_workflow_run_id = 0
        field = "current_workflow_run_id"
    else:
        current_run_attempt = 0
        field = "current_run_attempt"

    with pytest.raises(
        ValueError,
        match=(
            rf"(?i)^(?=.*execution-history)(?=.*{field})"
            r"(?=.*positive.*integer).*$"
        ),
    ):
        _admit_history_with_source_attempt_facts(
            current_workflow_run_id=current_workflow_run_id,
            current_run_attempt=current_run_attempt,
            verified_prior_attempts=(),
            source_run_attempt=4,
        )


def test_execution_history_validates_supplied_attempt_facts_for_cross_run() -> (
    None
):
    """Reject malformed facts without requiring cross-run existence proof."""
    with pytest.raises(
        ValueError,
        match=(
            r"(?i)^(?=.*execution-history)(?=.*verified)"
            r"(?=.*run_attempt)(?=.*positive.*integer).*$"
        ),
    ):
        _admit_history_with_source_attempt_facts(
            current_workflow_run_id=4501,
            current_run_attempt=1,
            verified_prior_attempts=(0,),
            source_run_attempt=4,
        )


def test_same_run_attempt_existence_remains_separate_platform_fact() -> None:
    """Keep attempt existence separate from artifact provenance."""
    result, _, expected, _, run, job = _admit_history_with_source_attempt_facts(
        current_workflow_run_id=4490,
        current_run_attempt=3,
        verified_prior_attempts=(2,),
    )

    from dataclasses import fields  # noqa: PLC0415

    admission_fields = {field.name for field in fields(result)}
    assert result.history_only is True
    assert result.release_execution == expected.release_execution
    assert result.platform_run == run
    assert result.platform_job == job
    assert {
        "artifact_run_attempt",
        "artifact_job_id",
        "artifact_to_attempt",
        "artifact_to_job",
    }.isdisjoint(admission_fields)


@pytest.mark.parametrize(
    ("claim", "mismatched"),
    [
        ("purpose", "release-simulation"),
        ("control", "foreign-control"),
    ],
    ids=["purpose", "control"],
)
def test_execution_history_rejects_mismatched_lineage_diagnostic_claim(
    claim: str,
    mismatched: str,
) -> None:
    """Reject a present purpose or control claim outside trusted lineage."""
    payload, expected, context, run, job = _history_scenario()
    payload["purpose"] = expected.purpose
    payload["control"] = expected.control_identity
    payload[claim] = mismatched
    context = replace(context, payload_digest=canonical_sha256(payload))

    with pytest.raises(
        ValueError,
        match=rf"^execution-history .*{claim}",
    ):
        _admit_history_payload(payload, expected, context, run, job)


def test_execution_history_preserves_matching_lineage_diagnostics() -> None:
    """Preserve matching claims diagnostically while trusting lineage."""
    payload, expected, context, run, job = _history_scenario()
    payload["purpose"] = expected.purpose
    payload["control"] = expected.control_identity
    context = replace(context, payload_digest=canonical_sha256(payload))

    result = _admit_history_payload(payload, expected, context, run, job)
    diagnostics = dict(result.diagnostic_claims)

    assert diagnostics["purpose"] == expected.purpose
    assert diagnostics["control"] == expected.control_identity
    assert result.purpose == expected.purpose
    assert result.control_identity == expected.control_identity
    assert result.history_only is True


@pytest.mark.parametrize(
    "omitted",
    [
        ("purpose",),
        ("control",),
        ("purpose", "control"),
    ],
    ids=["purpose-omitted", "control-omitted", "both-omitted"],
)
def test_execution_history_allows_omitted_lineage_diagnostic_claim(
    omitted: tuple[str, ...],
) -> None:
    """Allow omitted lineage diagnostics without inventing payload claims."""
    payload, expected, context, run, job = _history_scenario()
    payload["purpose"] = expected.purpose
    payload["control"] = expected.control_identity
    for claim in omitted:
        del payload[claim]
    context = replace(context, payload_digest=canonical_sha256(payload))

    result = _admit_history_payload(payload, expected, context, run, job)
    diagnostics = dict(result.diagnostic_claims)

    assert set(omitted).isdisjoint(diagnostics)
    assert result.purpose == expected.purpose
    assert result.control_identity == expected.control_identity
    assert result.history_only is True


@pytest.mark.parametrize(
    ("claim", "changed"),
    [
        ("producer", "foreign-producer"),
        ("run_attempt", 99),
        ("reusable_workflow", "foreign-workflow.yml"),
    ],
    ids=["producer", "run-attempt", "reusable-workflow"],
)
def test_execution_history_preserves_non_authoritative_diagnostic_claim(
    claim: str,
    changed: JsonValue,
) -> None:
    """Preserve other diagnostics without upgrading historical authority."""
    payload, expected, context, run, job = _history_scenario()
    payload["purpose"] = expected.purpose
    payload["control"] = expected.control_identity
    payload[claim] = changed
    context = replace(context, payload_digest=canonical_sha256(payload))

    result = _admit_history_payload(payload, expected, context, run, job)
    diagnostics = dict(result.diagnostic_claims)

    assert diagnostics[claim] == changed
    assert result.mode is AdmissionMode.EXECUTION_HISTORY
    assert result.history_only is True
    assert result.release_execution == expected.release_execution
    assert result.target == expected.target
    assert result.purpose == expected.purpose
    assert result.control_identity == expected.control_identity
    assert result.platform_run == run
    assert result.platform_job == job


_CLOSED_COMMIT_2_PURPOSES = (
    "ci-pr-slice-shadow",
    "slice-validation",
    "live-release",
    "release-simulation",
    "destination-acceptance",
)
_INVALID_COMMIT_2_PURPOSES = (
    ("empty", ""),
    ("wrong-case", "Live-Release"),
    ("unknown", "production-release"),
    ("none", None),
    ("boolean", True),
    ("integer", 1),
)
_PURPOSE_AUTHORITY_POSITIONS = (
    "current-payload",
    "current-trusted-context",
    "history-diagnostic-payload",
    "history-candidate-lineage",
    "history-expected-lineage",
)
_INVALID_COMMIT_SHAS = (
    ("uppercase-hex", "A" * 40),
    ("39-characters", "a" * 39),
    ("41-characters", "a" * 41),
    ("non-hex", "g" * 40),
    ("prefixed", "sha1:" + ("a" * 40)),
    ("whitespace-bearing", ("a" * 40) + " "),
    ("none", None),
    ("boolean", True),
    ("integer", 1),
)
_COMMIT_SHA_AUTHORITY_POSITIONS = (
    "current-payload-target",
    "current-trusted-target",
    "history-payload-target",
    "history-candidate-target",
    "history-expected-target",
    "history-platform-head-sha",
)
_INVALID_POSITIVE_INTEGERS = (
    ("boolean-true", True),
    ("boolean-false", False),
    ("none", None),
    ("zero", 0),
    ("negative", -1),
    ("positive-float", 1.5),
    ("numeric-string", "1"),
)
_CURRENT_INTEGER_ENDPOINTS = (
    "supplied-artifact-id",
    "trusted-artifact-id",
    "payload-workflow-run-id",
    "trusted-workflow-run-id",
    "payload-run-attempt",
    "trusted-run-attempt",
)
_HISTORY_INTEGER_ENDPOINTS = (
    "supplied-artifact-id",
    "trusted-artifact-id",
    "trusted-source-workflow-run-id",
    "queried-workflow-run-id",
    "queried-source-run-attempt",
    "queried-job-id",
    "payload-diagnostic-run-attempt",
)


def _primitive_domain_error_pattern(
    mode: str,
    field: str,
    domain: str,
) -> str:
    """Require a field-specific primitive error from the selected branch."""
    return rf"(?i)^(?=.*{mode})(?=.*{field})(?=.*{domain}).*$"


def _positive_integer_error_pattern(mode: str, field: str) -> str:
    """Accept schema type errors or the stronger positive-integer domain."""
    return _primitive_domain_error_pattern(
        mode,
        field,
        (
            r"(?:positive.*(?:integer|int\b)|"
            r"(?:integer|int\b).*(?:greater than zero|> ?0)|"
            r"non.?boolean|wrong JSON type)"
        ),
    )


@pytest.mark.parametrize(
    "purpose",
    _CLOSED_COMMIT_2_PURPOSES,
    ids=_CLOSED_COMMIT_2_PURPOSES,
)
def test_current_authority_accepts_each_closed_purpose(purpose: str) -> None:
    """Accept every and only every approved commit-2 purpose value."""
    payload = _current_payload()
    payload["purpose"] = purpose
    context = replace(
        _current_context(payload),
        purpose=purpose,
        payload_digest=canonical_sha256(payload),
    )

    result = _admit_current_payload(payload, context=context)

    assert result.mode is AdmissionMode.CURRENT_AUTHORITY
    assert result.history_only is False
    assert result.purpose == purpose
    assert result.release_execution == context.release_execution
    assert result.target == context.target
    assert result.control_identity == context.control


def _admit_invalid_purpose_position(
    position: str,
    invalid_purpose: JsonValue,
) -> object:
    """Build and admit one one-sided invalid purpose scenario."""
    if position == "current-payload":
        payload = _current_payload()
        context = _current_context(payload)
        payload["purpose"] = invalid_purpose
        context = replace(
            context,
            payload_digest=canonical_sha256(payload),
        )
        return _admit_current_payload(payload, context=context)
    if position == "current-trusted-context":
        payload = _current_payload()
        context = replace(
            _current_context(payload),
            purpose=cast("str", invalid_purpose),
        )
        return _admit_current_payload(payload, context=context)

    payload, expected, context, run, job = _history_scenario()
    if position == "history-diagnostic-payload":
        payload["purpose"] = invalid_purpose
        context = replace(
            context,
            payload_digest=canonical_sha256(payload),
        )
    elif position == "history-candidate-lineage":
        context = replace(
            context,
            lineage=replace(
                context.lineage,
                purpose=cast("str", invalid_purpose),
            ),
        )
    else:
        expected = replace(expected, purpose=cast("str", invalid_purpose))
    return _admit_history_payload(payload, expected, context, run, job)


@pytest.mark.parametrize(
    ("position", "invalid_purpose"),
    [
        (position, invalid)
        for position in _PURPOSE_AUTHORITY_POSITIONS
        for _, invalid in _INVALID_COMMIT_2_PURPOSES
    ],
    ids=[
        f"{position}-{invalid_class}"
        for position in _PURPOSE_AUTHORITY_POSITIONS
        for invalid_class, _ in _INVALID_COMMIT_2_PURPOSES
    ],
)
def test_admission_rejects_invalid_purpose_before_binding_comparison(
    position: str,
    invalid_purpose: JsonValue,
) -> None:
    """Validate each payload and trusted purpose before correlation."""
    mode = (
        "current-authority"
        if position.startswith("current-")
        else "execution-history"
    )
    error_pattern = _primitive_domain_error_pattern(
        mode,
        "purpose",
        r"(?:closed|allowed|invalid|unsupported|wrong JSON type)",
    )

    with pytest.raises((TypeError, ValueError), match=error_pattern):
        _admit_invalid_purpose_position(position, invalid_purpose)


def _admit_invalid_commit_sha_position(
    position: str,
    invalid_sha: JsonValue,
) -> object:
    """Build and admit one one-sided malformed commit SHA scenario."""
    if position == "current-payload-target":
        payload = _current_payload()
        context = _current_context(payload)
        payload["target"] = invalid_sha
        context = replace(
            context,
            payload_digest=canonical_sha256(payload),
        )
        return _admit_current_payload(payload, context=context)
    if position == "current-trusted-target":
        payload = _current_payload()
        context = replace(
            _current_context(payload),
            target=cast("str", invalid_sha),
        )
        return _admit_current_payload(payload, context=context)

    payload, expected, context, run, job = _history_scenario()
    if position == "history-payload-target":
        payload["target"] = invalid_sha
        context = replace(
            context,
            payload_digest=canonical_sha256(payload),
        )
    elif position == "history-candidate-target":
        context = replace(
            context,
            lineage=replace(
                context.lineage,
                target=cast("str", invalid_sha),
            ),
        )
    elif position == "history-expected-target":
        expected = replace(expected, target=cast("str", invalid_sha))
    else:
        run = replace(run, head_sha=cast("str", invalid_sha))
    return _admit_history_payload(payload, expected, context, run, job)


@pytest.mark.parametrize(
    ("position", "invalid_sha"),
    [
        (position, invalid)
        for position in _COMMIT_SHA_AUTHORITY_POSITIONS
        for _, invalid in _INVALID_COMMIT_SHAS
    ],
    ids=[
        f"{position}-{invalid_class}"
        for position in _COMMIT_SHA_AUTHORITY_POSITIONS
        for invalid_class, _ in _INVALID_COMMIT_SHAS
    ],
)
def test_admission_rejects_invalid_commit_sha_from_each_authority(
    position: str,
    invalid_sha: str,
) -> None:
    """Reject every malformed target SHA at every authority endpoint."""
    mode = (
        "current-authority"
        if position.startswith("current-")
        else "execution-history"
    )
    field = "head_sha" if position.endswith("head-sha") else "target"
    error_pattern = _primitive_domain_error_pattern(
        mode,
        field,
        (
            r"(?:malformed|invalid|lowercase.*hex|40.*hex|hex.*40|"
            r"wrong JSON type)"
        ),
    )

    with pytest.raises((TypeError, ValueError), match=error_pattern):
        _admit_invalid_commit_sha_position(position, invalid_sha)


@pytest.mark.parametrize(
    ("mode", "boundary_sha"),
    [
        ("current-authority", "0" * 40),
        ("current-authority", "f" * 40),
        ("execution-history", "0" * 40),
        ("execution-history", "f" * 40),
    ],
    ids=[
        "current-all-zero",
        "current-all-f",
        "history-all-zero",
        "history-all-f",
    ],
)
def test_admission_accepts_commit_sha_boundaries(
    mode: str,
    boundary_sha: str,
) -> None:
    """Accept the lowercase all-zero and all-f 40-hex boundaries."""
    if mode == "current-authority":
        payload = _current_payload()
        payload["target"] = boundary_sha
        context = replace(
            _current_context(payload),
            target=boundary_sha,
            payload_digest=canonical_sha256(payload),
        )

        result = _admit_current_payload(payload, context=context)

        assert result.mode is AdmissionMode.CURRENT_AUTHORITY
        assert result.history_only is False
        assert result.target == boundary_sha
        assert result.payload_digest == canonical_sha256(payload)
    else:
        payload, expected, context, run, job = _history_scenario()
        payload["target"] = boundary_sha
        expected = replace(expected, target=boundary_sha)
        context = replace(
            context,
            lineage=expected,
            payload_digest=canonical_sha256(payload),
        )
        run = replace(run, head_sha=boundary_sha)

        result = _admit_history_payload(
            payload,
            expected,
            context,
            run,
            job,
        )

        assert result.mode is AdmissionMode.EXECUTION_HISTORY
        assert result.history_only is True
        assert result.target == boundary_sha == run.head_sha
        assert result.platform_run == run
        assert result.payload_digest == canonical_sha256(payload)


@pytest.mark.parametrize(
    ("endpoint", "invalid_value"),
    [
        (endpoint, invalid)
        for endpoint in _CURRENT_INTEGER_ENDPOINTS
        for _, invalid in _INVALID_POSITIVE_INTEGERS
    ],
    ids=[
        f"{endpoint}-{invalid_class}"
        for endpoint in _CURRENT_INTEGER_ENDPOINTS
        for invalid_class, _ in _INVALID_POSITIVE_INTEGERS
    ],
)
def test_current_authority_rejects_invalid_positive_integer(
    endpoint: str,
    invalid_value: JsonValue,
) -> None:
    """Reject all non-positive or non-integer current authority IDs."""
    payload = _current_payload()
    context = _current_context(payload)
    artifact_id: object = context.artifact_id
    field = (
        "artifact_id"
        if endpoint.endswith("artifact-id")
        else (
            "workflow_run_id"
            if endpoint.endswith("workflow-run-id")
            else "run_attempt"
        )
    )

    if endpoint == "supplied-artifact-id":
        artifact_id = invalid_value
    elif endpoint == "trusted-artifact-id":
        context = replace(context, artifact_id=cast("int", invalid_value))
        artifact_id = 7102
    elif endpoint == "payload-workflow-run-id":
        payload["workflow_run_id"] = invalid_value
        context = replace(
            context,
            payload_digest=canonical_sha256(payload),
        )
    elif endpoint == "trusted-workflow-run-id":
        context = replace(
            context,
            workflow_run_id=cast("int", invalid_value),
        )
    elif endpoint == "payload-run-attempt":
        payload["run_attempt"] = invalid_value
        context = replace(
            context,
            payload_digest=canonical_sha256(payload),
        )
    else:
        context = replace(
            context,
            run_attempt=cast("int", invalid_value),
        )

    with pytest.raises(
        (TypeError, ValueError),
        match=_positive_integer_error_pattern(
            "current-authority",
            field,
        ),
    ):
        _admit_current_payload(
            payload,
            context=context,
            artifact_id=artifact_id,
        )


@pytest.mark.parametrize(
    "primitive",
    ["artifact-id", "workflow-run-id", "run-attempt"],
)
def test_current_authority_accepts_positive_integer_lower_bound(
    primitive: str,
) -> None:
    """Accept one as the lower bound for every current integer primitive."""
    payload = _current_payload()
    context = _current_context(payload)
    artifact_id = context.artifact_id
    if primitive == "artifact-id":
        artifact_id = 1
        context = replace(context, artifact_id=1)
    elif primitive == "workflow-run-id":
        payload["workflow_run_id"] = 1
        context = replace(context, workflow_run_id=1)
    else:
        payload["run_attempt"] = 1
        context = replace(context, run_attempt=1)
    context = replace(context, payload_digest=canonical_sha256(payload))

    result = _admit_current_payload(
        payload,
        context=context,
        artifact_id=artifact_id,
    )

    assert result.mode is AdmissionMode.CURRENT_AUTHORITY
    assert result.history_only is False
    assert result.release_execution == context.release_execution
    assert result.payload_digest == canonical_sha256(payload)


@pytest.mark.parametrize(
    ("endpoint", "invalid_value"),
    [
        (endpoint, invalid)
        for endpoint in _HISTORY_INTEGER_ENDPOINTS
        for _, invalid in _INVALID_POSITIVE_INTEGERS
    ],
    ids=[
        f"{endpoint}-{invalid_class}"
        for endpoint in _HISTORY_INTEGER_ENDPOINTS
        for invalid_class, _ in _INVALID_POSITIVE_INTEGERS
    ],
)
def test_execution_history_rejects_invalid_positive_integer(
    endpoint: str,
    invalid_value: JsonValue,
) -> None:
    """Reject all non-positive or non-integer history IDs and attempts."""
    payload, expected, context, run, job = _history_scenario()
    artifact_id: object = context.artifact_id
    field_by_endpoint = {
        "supplied-artifact-id": "artifact_id",
        "trusted-artifact-id": "artifact_id",
        "trusted-source-workflow-run-id": "source_workflow_run_id",
        "queried-workflow-run-id": "workflow_run_id",
        "queried-source-run-attempt": "run_attempt",
        "queried-job-id": "job_id",
        "payload-diagnostic-run-attempt": "run_attempt",
    }

    if endpoint == "supplied-artifact-id":
        artifact_id = invalid_value
    elif endpoint == "trusted-artifact-id":
        context = replace(context, artifact_id=cast("int", invalid_value))
        artifact_id = 8203
    elif endpoint == "trusted-source-workflow-run-id":
        context = replace(
            context,
            source_workflow_run_id=cast("int", invalid_value),
        )
    elif endpoint == "queried-workflow-run-id":
        run = replace(
            run,
            workflow_run_id=cast("int", invalid_value),
        )
    elif endpoint == "queried-source-run-attempt":
        run = replace(run, run_attempt=cast("int", invalid_value))
    elif endpoint == "queried-job-id":
        job = replace(job, job_id=cast("int", invalid_value))
    else:
        payload["run_attempt"] = invalid_value
        context = replace(
            context,
            payload_digest=canonical_sha256(payload),
        )

    with pytest.raises(
        (TypeError, ValueError),
        match=_positive_integer_error_pattern(
            "execution-history",
            field_by_endpoint[endpoint],
        ),
    ):
        _admit_history_payload(
            payload,
            expected,
            context,
            run,
            job,
            artifact_id=artifact_id,
        )


@pytest.mark.parametrize(
    "primitive",
    [
        "artifact-id",
        "workflow-run-id",
        "source-run-attempt",
        "job-id",
        "payload-diagnostic-run-attempt",
    ],
)
def test_execution_history_accepts_positive_integer_lower_bound(
    primitive: str,
) -> None:
    """Accept one as the lower bound for every history integer primitive."""
    payload, expected, context, run, job = _history_scenario()
    artifact_id = context.artifact_id
    if primitive == "artifact-id":
        artifact_id = 1
        context = replace(context, artifact_id=1)
    elif primitive == "workflow-run-id":
        context = replace(context, source_workflow_run_id=1)
        run = replace(run, workflow_run_id=1)
    elif primitive == "source-run-attempt":
        run = replace(run, run_attempt=1)
    elif primitive == "job-id":
        job = replace(job, job_id=1)
    else:
        payload["run_attempt"] = 1
        context = replace(
            context,
            payload_digest=canonical_sha256(payload),
        )

    result = _admit_history_payload(
        payload,
        expected,
        context,
        run,
        job,
        artifact_id=artifact_id,
    )

    assert result.mode is AdmissionMode.EXECUTION_HISTORY
    assert result.history_only is True
    assert result.release_execution == expected.release_execution
    assert result.platform_run == run
    assert result.platform_job == job
    if primitive == "payload-diagnostic-run-attempt":
        assert dict(result.diagnostic_claims)["run_attempt"] == 1


@pytest.mark.parametrize(
    "primitive_case",
    [
        "purpose",
        "commit-sha",
        "workflow-run-id",
        "run-attempt",
        "artifact-id",
        "job-id",
    ],
    ids=[
        "purpose",
        "commit-sha",
        "workflow-run-id",
        "run-attempt",
        "artifact-id",
        "job-id",
    ],
)
def test_admission_reports_primitive_domain_error_before_binding_mismatch(
    primitive_case: str,
) -> None:
    """Report the one invalid endpoint before any correlation mismatch."""
    if primitive_case == "purpose":
        error_pattern = _primitive_domain_error_pattern(
            "current-authority",
            "purpose",
            r"(?:closed|allowed|invalid|unsupported|wrong JSON type)",
        )
    elif primitive_case == "commit-sha":
        error_pattern = _primitive_domain_error_pattern(
            "execution-history",
            "head_sha",
            (
                r"(?:malformed|invalid|lowercase.*hex|40.*hex|hex.*40|"
                r"wrong JSON type)"
            ),
        )
    elif primitive_case == "workflow-run-id":
        error_pattern = _positive_integer_error_pattern(
            "current-authority",
            "workflow_run_id",
        )
    else:
        field = {
            "run-attempt": "run_attempt",
            "artifact-id": "artifact_id",
            "job-id": "job_id",
        }[primitive_case]
        error_pattern = _positive_integer_error_pattern(
            "execution-history",
            field,
        )

    with pytest.raises(
        (TypeError, ValueError),
        match=error_pattern,
    ):
        _admit_one_sided_invalid_primitive(primitive_case)


def _admit_wrong_but_equal_primitive_pair(
    primitive_pair: str,
) -> object:
    """Build and admit one pair whose correlated copies are equally invalid."""
    if primitive_pair == "current-purpose":
        payload = _current_payload()
        payload["purpose"] = "not-approved"
        context = replace(
            _current_context(payload),
            purpose="not-approved",
            payload_digest=canonical_sha256(payload),
        )
        return _admit_current_payload(payload, context=context)
    if primitive_pair == "current-commit-sha":
        payload = _current_payload()
        payload["target"] = "A" * 40
        context = replace(
            _current_context(payload),
            target="A" * 40,
            payload_digest=canonical_sha256(payload),
        )
        return _admit_current_payload(payload, context=context)
    if primitive_pair == "current-workflow-run-id":
        payload = _current_payload()
        payload["workflow_run_id"] = 0
        context = replace(
            _current_context(payload),
            workflow_run_id=0,
            payload_digest=canonical_sha256(payload),
        )
        return _admit_current_payload(payload, context=context)
    if primitive_pair == "current-run-attempt":
        payload = _current_payload()
        payload["run_attempt"] = 0
        context = replace(
            _current_context(payload),
            run_attempt=0,
            payload_digest=canonical_sha256(payload),
        )
        return _admit_current_payload(payload, context=context)
    if primitive_pair == "current-artifact-id":
        payload = _current_payload()
        context = replace(_current_context(payload), artifact_id=0)
        return _admit_current_payload(
            payload,
            context=context,
            artifact_id=0,
        )

    payload, expected, context, run, job = _history_scenario()
    artifact_id: object = context.artifact_id
    if primitive_pair == "history-purpose":
        payload["purpose"] = "not-approved"
        expected = replace(expected, purpose="not-approved")
        context = replace(
            context,
            lineage=replace(
                context.lineage,
                purpose="not-approved",
            ),
            payload_digest=canonical_sha256(payload),
        )
    elif primitive_pair == "history-commit-sha":
        payload["target"] = "A" * 40
        expected = replace(expected, target="A" * 40)
        context = replace(
            context,
            lineage=expected,
            payload_digest=canonical_sha256(payload),
        )
        run = replace(run, head_sha="A" * 40)
    elif primitive_pair == "history-workflow-run-id":
        context = replace(context, source_workflow_run_id=0)
        run = replace(run, workflow_run_id=0)
    elif primitive_pair == "history-run-attempt":
        payload["run_attempt"] = 0
        context = replace(
            context,
            payload_digest=canonical_sha256(payload),
        )
        run = replace(run, run_attempt=0)
    else:
        context = replace(context, artifact_id=0)
        artifact_id = 0
    return _admit_history_payload(
        payload,
        expected,
        context,
        run,
        job,
        artifact_id=artifact_id,
    )


@pytest.mark.parametrize(
    "primitive_pair",
    [
        "current-purpose",
        "history-purpose",
        "current-commit-sha",
        "history-commit-sha",
        "current-workflow-run-id",
        "history-workflow-run-id",
        "current-run-attempt",
        "history-run-attempt",
        "current-artifact-id",
        "history-artifact-id",
    ],
    ids=[
        "current-purpose",
        "history-purpose",
        "current-commit-sha",
        "history-commit-sha",
        "current-workflow-run-id",
        "history-workflow-run-id",
        "current-run-attempt",
        "history-run-attempt",
        "current-artifact-id",
        "history-artifact-id",
    ],
)
def test_admission_rejects_wrong_but_equal_primitive_pairs(
    primitive_pair: str,
) -> None:
    """Reject equal invalid copies instead of letting equality mask them."""
    mode = (
        "current-authority"
        if primitive_pair.startswith("current-")
        else "execution-history"
    )
    if primitive_pair.endswith("purpose"):
        field = "purpose"
        domain = r"(?:closed|allowed|invalid|unsupported)"
    elif primitive_pair.endswith("commit-sha"):
        field = "target"
        domain = r"(?:malformed|invalid|lowercase.*hex|40.*hex|hex.*40)"
    else:
        field = {
            "current-workflow-run-id": "workflow_run_id",
            "history-workflow-run-id": "workflow_run_id",
            "current-run-attempt": "run_attempt",
            "history-run-attempt": "run_attempt",
            "current-artifact-id": "artifact_id",
            "history-artifact-id": "artifact_id",
        }[primitive_pair]
        domain = (
            r"(?:positive.*(?:integer|int\b)|"
            r"(?:integer|int\b).*(?:greater than zero|> ?0)|"
            r"non.?boolean|wrong JSON type)"
        )
    error_pattern = _primitive_domain_error_pattern(
        mode,
        field,
        domain,
    )

    with pytest.raises(
        (TypeError, ValueError),
        match=error_pattern,
    ):
        _admit_wrong_but_equal_primitive_pair(primitive_pair)


def test_admission_preserves_opaque_commit_2_identity_strings() -> None:
    """Do not apply commit-6 Release grammar to opaque commit-2 strings."""
    opaque_execution = " opaque Execution :: not/a/release#identity "
    opaque_request = " opaque request :: [not-grammar] "
    opaque_attempt = " opaque Attempt :: [not-grammar] "
    opaque_producer = " opaque producer :: [not-grammar] "
    opaque_control = " opaque control :: [not-grammar] "
    opaque_reusable_workflow = " opaque reusable-workflow :: [not-grammar] "

    current_payload = _current_payload()
    current_payload.update(
        {
            "release_execution": opaque_execution,
            "request": opaque_request,
            "attempt": opaque_attempt,
            "producer": opaque_producer,
            "control": opaque_control,
        }
    )
    current_context = replace(
        _current_context(current_payload),
        release_execution=opaque_execution,
        request=opaque_request,
        attempt=opaque_attempt,
        producer=opaque_producer,
        control=opaque_control,
        payload_digest=canonical_sha256(current_payload),
    )

    current_result = _admit_current_payload(
        current_payload,
        context=current_context,
    )

    assert current_result.mode is AdmissionMode.CURRENT_AUTHORITY
    assert current_result.history_only is False
    assert current_result.release_execution == opaque_execution
    assert current_result.control_identity == opaque_control
    with pytest.raises(
        ValueError,
        match=r"^current-authority binding mismatch: request$",
    ):
        _admit_current_payload(
            current_payload,
            context=replace(current_context, request="other opaque request"),
        )

    payload, expected, context, run, job = _history_scenario()
    payload.update(
        {
            "execution": opaque_execution,
            "producer": opaque_producer,
            "reusable_workflow": opaque_reusable_workflow,
            "control": opaque_control,
        }
    )
    expected = replace(
        expected,
        release_execution=opaque_execution,
        control_identity=opaque_control,
    )
    context = replace(
        context,
        lineage=expected,
        payload_digest=canonical_sha256(payload),
    )

    history_result = _admit_history_payload(
        payload,
        expected,
        context,
        run,
        job,
    )
    diagnostics = dict(history_result.diagnostic_claims)

    assert history_result.mode is AdmissionMode.EXECUTION_HISTORY
    assert history_result.history_only is True
    assert history_result.release_execution == opaque_execution
    assert history_result.control_identity == opaque_control
    assert diagnostics["producer"] == opaque_producer
    assert diagnostics["reusable_workflow"] == opaque_reusable_workflow
    assert diagnostics["control"] == opaque_control
