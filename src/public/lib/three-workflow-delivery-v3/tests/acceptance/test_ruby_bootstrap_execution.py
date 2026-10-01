"""Single bootstrap mutation lifetime with offline native adapters."""

from dataclasses import replace
from datetime import timedelta
from http.client import BadStatusLine

import pytest
from three_workflow_delivery_v3.acceptance import (
    ruby_bootstrap_execution as execution,
)
from three_workflow_delivery_v3.acceptance.ruby_bootstrap_execution import (
    execute_ruby_bootstrap_publication,
    verify_ruby_bootstrap_remote,
)
from three_workflow_delivery_v3.adapters.ruby_registry import RubyHttpResponse
from three_workflow_delivery_v3.canonical import (
    canonical_sha256,
    canonicalize,
    parse_canonical_json,
)
from three_workflow_delivery_v3.release.ruby_readback import (
    ruby_response_from_document,
)

from ..adapters import test_ruby_registry_redirect as redirect_cases
from ..release.ruby_fixtures import TimedTransport, VirtualClock
from ..ruby_integration_fixtures import consumer
from ..ruby_registry_fixtures import (
    ASSERTION,
    WRITE_TOKEN,
    ScriptedTransport,
    json_response,
    token_document,
)
from . import ruby_bootstrap_fixtures
from .ruby_bootstrap_fixtures import CURRENT, EXPIRED, transport

bootstrap_cases = ruby_bootstrap_fixtures.bootstrap_cases


def execute_case(  # noqa: PLR0913 - controlled execution scenario
    case,
    root,
    responses,
    *,
    delays=(),
    timeline=None,
    deadline_seconds=120,
    wait=None,
    authority=None,
    assertion=None,
    events=None,
    exchange_response=None,
):
    """Use actual exchange/upload logic with modeled durable callbacks."""
    timeline = VirtualClock() if timeline is None else timeline
    timeline.utc = CURRENT
    events = [] if events is None else events
    prefix = (
        [
            json_response(token_document(), 201)
            if exchange_response is None
            else exchange_response
        ]
        if case.registry.name == "rubygems"
        else []
    )
    native = TimedTransport(timeline, [*prefix, *responses], delays)
    claim = root / "claim.json"
    deadline = CURRENT + timedelta(seconds=deadline_seconds)

    def reserve(phase, ordinal):
        assert claim.is_file()
        events.append((phase, ordinal))

    def check():
        assert (
            parse_canonical_json(claim.read_bytes())
            == case.marker.to_document()
        )
        events.append("authority")
        return {"checked": True}

    def assertion_callback():
        assert claim.is_file()
        events.append("assertion")
        return ASSERTION

    reader = case.reader(
        "execute",
        native,
        clock=timeline.clock,
        reserve=reserve,
        deadline=deadline,
    )
    ref, identity = transport(
        case.publication.run,
        canonicalize(case.marker.to_document()),
        artifact_id=405,
        producer=case.publisher,
        path="mutation-marker.json",
    )
    result = execute_ruby_bootstrap_publication(
        case.marker,
        ref,
        identity,
        reader=reader,
        claim_path=claim,
        deadline=deadline,
        clock=timeline.clock,
        check_authority=check if authority is None else authority,
        assertion=(assertion_callback if assertion is None else assertion)
        if case.registry.name == "rubygems"
        else None,
        github_token=WRITE_TOKEN
        if case.registry.name == "github-packages"
        else None,
        monotonic=timeline.monotonic,
        wait=timeline.wait if wait is None else wait,
    )
    return result, native, timeline, events


@pytest.mark.parametrize("destination", ["rubygems", "github-packages"])
def test_one_original_uses_one_writer_after_marker_and_retains_bound_result(
    bootstrap_cases, tmp_path, monkeypatch, destination
):
    """Real exchange credentials are accepted only by the same real writer."""
    case = bootstrap_cases[destination]
    calls = []
    writer_class = execution.RubyRegistryWriter

    class ObservedWriter(writer_class):
        def exchange(self, *args, **kwargs):
            calls.append(("exchange", id(self)))
            return super().exchange(*args, **kwargs)

        def upload(self, *args, **kwargs):
            calls.append(("upload", id(self)))
            return super().upload(*args, **kwargs)

    monkeypatch.setattr(execution, "RubyRegistryWriter", ObservedWriter)
    result, native, timeline, events = execute_case(
        case,
        tmp_path,
        [RubyHttpResponse(200, b"accepted"), *case.exact_responses()],
    )
    assert (result.status, result.result) == ("succeeded", "published")
    assert [name for name, _ in calls] == (
        ["exchange", "upload"] if destination == "rubygems" else ["upload"]
    )
    assert len({identity for _, identity in calls}) == 1
    offset = int(destination == "rubygems")
    assert native.requests[offset][0] == "POST"
    assert native.requests[offset][3] == case.original.content
    assert events[:5] == (
        ["authority", "assertion", ("execute", 1), "authority", ("execute", 2)]
        if destination == "rubygems"
        else [
            "authority",
            "authority",
            ("execute", 1),
            ("execute", 2),
            ("execute", 3),
        ]
    )
    assert result.visibility[0]["exact"] is True
    assert timeline.waits == []
    assert result.to_document()["producer"] == case.publisher
    assert result.to_document()["normal-live-completion"] is False
    directory = tmp_path / "claim.json-observations"
    assert sorted(path.name for path in directory.iterdir()) == [
        "credential-authority.json",
        "result.json",
        "upload-authority.json",
        "upload.json",
        "visibility-0.json",
    ]
    assert (
        parse_canonical_json((directory / "result.json").read_bytes())
        == result.to_document()
    )
    retained = b"".join(path.read_bytes() for path in directory.iterdir())
    assert WRITE_TOKEN.encode() not in retained
    assert ASSERTION.encode() not in retained


@pytest.mark.parametrize("status", [201, 202, 302, 401, 409, 500])
def test_non200_upload_is_failed_without_visibility_or_recovery(
    bootstrap_cases, tmp_path, status
):
    """Registry rejection never initiates readback or another upload."""
    case = bootstrap_cases["rubygems"]
    result, native, timeline, events = execute_case(
        case, tmp_path, [RubyHttpResponse(status, b"rejected")]
    )
    assert (result.status, result.result) == ("failed", "failed")
    assert result.response.status == status
    assert result.visibility == ()
    assert timeline.waits == []
    assert [entry for entry in events if isinstance(entry, tuple)] == [
        ("execute", 1),
        ("execute", 2),
    ]
    assert len(native.requests) == 2  # noqa: PLR2004 - exchange and singular upload
    assert result.to_document()["mutation-classification"] == "possibly-mutated"


@pytest.mark.parametrize(
    "failure",
    [
        OSError("private network error"),
        BadStatusLine("private HTTP error"),
        ValueError("private credential error"),
        RuntimeError("private writer error"),
    ],
)
def test_ambiguous_upload_is_retained_without_retry_and_claim_remains_spent(
    bootstrap_cases, tmp_path, failure
):
    """Lost upload evidence remains possibly mutated and cannot resend."""
    case = bootstrap_cases["rubygems"]
    result, native, _, _ = execute_case(case, tmp_path, [failure])
    assert (result.status, result.result) == ("unknown", "failed")
    assert result.visibility == ()
    assert result.response is None
    assert len(native.requests) == 2  # noqa: PLR2004
    assert "private" not in str(result.to_document())
    assert result.error_kind == (
        "HTTPException"
        if isinstance(failure, BadStatusLine)
        else type(failure).__name__
    )
    with pytest.raises(FileExistsError):
        execute_case(case, tmp_path, [RubyHttpResponse(200, b"must not send")])


@pytest.mark.parametrize(
    "stage", ["credential-authority", "assertion", "upload-authority"]
)
def test_preupload_authority_or_assertion_failure_prevents_upload(
    bootstrap_cases, tmp_path, stage
):
    """Persist the claim before guards and never fall back to another token."""
    case = bootstrap_cases["rubygems"]
    calls = []

    def authority():
        assert (tmp_path / "claim.json").is_file()
        calls.append("authority")
        if stage == "credential-authority" or (
            stage == "upload-authority" and len(calls) == 2  # noqa: PLR2004 - second authority check
        ):
            message = "private failed authority"
            raise ValueError(message)
        return {"checked": True}

    def assertion():
        calls.append("assertion")
        message = "private failed assertion"
        raise RuntimeError(message)

    result, native, _, _ = execute_case(
        case,
        tmp_path,
        [],
        authority=authority,
        assertion=assertion if stage == "assertion" else None,
    )
    assert (result.status, result.result) == ("not-attempted", "failed")
    assert result.visibility == ()
    assert len(native.requests) == int(stage == "upload-authority")
    assert "private" not in str(result.to_document())
    assert result.to_document()["mutation-classification"] == "not-mutated"


@pytest.mark.parametrize("pending", [0, 1, 5])
def test_visibility_requires_exact_original_within_six_ten_second_checks(
    bootstrap_cases, tmp_path, pending
):
    """Wait only on missing state; stop when the exact original appears."""
    case = bootstrap_cases["rubygems"]
    result, native, timeline, _ = execute_case(
        case,
        tmp_path,
        [
            RubyHttpResponse(200, b"accepted"),
            *case.missing_responses() * pending,
            *case.exact_responses(),
        ],
    )
    assert result.result == "published"
    assert [entry["exact"] for entry in result.visibility] == [
        False
    ] * pending + [True]
    assert [entry["started-monotonic"] for entry in result.visibility] == [
        100.0 + 10 * index for index in range(pending + 1)
    ]
    assert timeline.waits == [10.0] * pending
    assert len(native.requests) == pending + 5


def test_six_missing_observations_end_failed_without_seventh(
    bootstrap_cases, tmp_path
):
    """Definitive upload success cannot authorize indefinite polling."""
    case = bootstrap_cases["rubygems"]
    result, native, timeline, _ = execute_case(
        case,
        tmp_path,
        [RubyHttpResponse(200, b"accepted"), *case.missing_responses() * 6],
    )
    assert (result.status, result.result) == ("succeeded", "failed")
    assert len(result.visibility) == 6  # noqa: PLR2004
    assert timeline.waits == [10.0] * 5
    assert len(native.requests) == 8  # noqa: PLR2004


@pytest.mark.parametrize(("duration", "deadline"), [(60, 120), (10, 10)])
def test_visibility_exact_at_window_or_phase_deadline_cannot_pass(
    bootstrap_cases, tmp_path, duration, deadline
):
    """An exact final archive is still too late at either strict deadline."""
    case = bootstrap_cases["rubygems"]
    result, native, _, _ = execute_case(
        case,
        tmp_path,
        [RubyHttpResponse(200, b"accepted"), *case.exact_responses()],
        delays=[0, 0, 0, 0, duration],
        deadline_seconds=deadline,
    )
    assert (result.status, result.result) == ("succeeded", "failed")
    assert result.visibility[-1]["exact"] is False
    assert result.error_kind == "ValueError"
    assert len(native.requests) == 5  # noqa: PLR2004


def test_unperformed_wait_cannot_create_unspaced_visibility(
    bootstrap_cases, tmp_path
):
    """Elapsed monotonic time is checked independently of a wait callback."""
    case = bootstrap_cases["rubygems"]
    result, native, _, _ = execute_case(
        case,
        tmp_path,
        [RubyHttpResponse(200, b"accepted"), *case.missing_responses()],
        wait=lambda _: None,
    )
    assert result.result == "failed"
    assert result.error_kind == "ValueError"
    assert len(result.visibility) == 1
    assert len(native.requests) == 3  # noqa: PLR2004


@pytest.mark.parametrize("edge", ["monotonic", "utc"])
def test_clock_regression_after_marker_stops_before_credential_access(
    bootstrap_cases, tmp_path, edge
):
    """Either clock going backwards blocks assertion and upload."""
    case = bootstrap_cases["rubygems"]
    timeline = VirtualClock()
    calls = []

    def authority():
        calls.append("authority")
        if edge == "utc":
            timeline.utc -= timedelta(microseconds=1)
        else:
            timeline.tick -= 1
        return {}

    result, native, _, events = execute_case(
        case, tmp_path, [], timeline=timeline, authority=authority
    )
    assert result.status == "not-attempted"
    assert result.error_kind == "ValueError"
    assert native.requests == []
    assert calls == ["authority"]
    assert "assertion" not in events


@pytest.mark.parametrize(
    "mode", ["credential-path", "marker-producer", "wrong-phase", "expired"]
)
def test_execution_invalid_admission_cannot_create_claim_or_obtain_assertion(
    bootstrap_cases, tmp_path, mode
):
    """Effect preconditions fail before the exclusive mutation claim."""
    case = bootstrap_cases["rubygems"]
    native = ScriptedTransport()
    reader = case.reader(
        "eligibility" if mode == "wrong-phase" else "execute", native
    )
    ref, identity = transport(
        case.publication.run,
        canonicalize(case.marker.to_document()),
        artifact_id=405,
        producer="publish-ruby-bootstrap"
        if mode == "marker-producer"
        else case.publisher,
        path="mutation-marker.json",
    )
    calls = []
    with pytest.raises(ValueError, match="Ruby"):
        execute_ruby_bootstrap_publication(
            case.marker,
            ref,
            identity,
            reader=reader,
            claim_path=tmp_path / "claim.json",
            deadline=CURRENT + timedelta(minutes=1),
            clock=lambda: EXPIRED if mode == "expired" else CURRENT,
            check_authority=dict,
            assertion=None
            if mode == "credential-path"
            else lambda: calls.append("assertion"),
            github_token=WRITE_TOKEN if mode == "credential-path" else None,
        )
    assert calls == []
    assert native.requests == []
    assert not (tmp_path / "claim.json").exists()


@pytest.mark.parametrize(
    "change",
    [
        "status",
        "exact",
        "ordinal",
        "spacing",
        "native",
        "late",
        "late-utc",
        "producer",
    ],
)
def test_result_replays_original_evidence_and_rejects_contradictory_success(
    bootstrap_cases, tmp_path, change
):
    """Reject Result scalars that contradict native bytes or valid timing."""
    case = bootstrap_cases["rubygems"]
    result, _, _, _ = execute_case(
        case,
        tmp_path,
        [
            RubyHttpResponse(200, b"accepted"),
            *case.missing_responses(),
            *case.exact_responses(),
        ],
    )
    changes = {}
    if change == "status":
        changes["status"] = "failed"
    elif change == "producer":
        changes["marker_transport"] = replace(
            result.marker_transport, producer="publish-ruby-bootstrap"
        )
    else:
        entries = [dict(item) for item in result.visibility]
        if change == "exact":
            entries[-1]["exact"] = False
        elif change == "ordinal":
            entries[-1]["ordinal"] = True
        elif change == "spacing":
            entries[-1]["started-monotonic"] = (
                entries[0]["started-monotonic"] + 9.99
            )
        elif change == "late":
            entries[-1]["completed-monotonic"] = result.completed_tick + 60
        elif change == "late-utc":
            entries[-1]["completed-at"] = (
                result.completed_at + timedelta(seconds=60)
            ).isoformat()
        else:
            entries[-1]["native"] = entries[0]["native"]
        changes["visibility"] = tuple(entries)
    with pytest.raises(ValueError, match="Ruby"):
        replace(result, **changes)


@pytest.mark.parametrize("destination", ["rubygems", "github-packages"])
def test_separate_final_consumer_downloads_exact_original_using_three_sends(
    bootstrap_cases, tmp_path, destination
):
    """A separate unprivileged reader consumes only the verified original."""
    case = bootstrap_cases[destination]
    result, _, _, _ = execute_case(
        case,
        tmp_path,
        [RubyHttpResponse(200, b"accepted"), *case.exact_responses()],
    )
    native = ScriptedTransport(*case.exact_responses())
    spent = []
    seen = []
    reader = case.reader(
        "remote-consumer", native, reserve=lambda *args: spent.append(args)
    )

    def consume(original):
        seen.append((original.content, original.witness))
        return consumer(original)

    evidence = verify_ruby_bootstrap_remote(
        result,
        reader,
        clock=lambda: CURRENT,
        check_authority=lambda: {"fresh": True},
        consumer=consume,
    )
    assert spent == [
        ("remote-consumer", 1),
        ("remote-consumer", 2),
        ("remote-consumer", 3),
    ]
    assert seen == [(case.original.content, case.original.witness)]
    assert evidence["result-digest"] == canonical_sha256(result.to_document())
    assert evidence["normal-live-completion"] is False
    assert evidence["native"]["classification"] == "exact"
    assert evidence["consumer"]["artifact-digest"] == case.original.digest
    assert len(native.requests) == 3  # noqa: PLR2004


@pytest.mark.parametrize(
    "mode",
    [
        "failed-result",
        "missing",
        "foreign-byte",
        "bad-consumer",
        "expired-completion",
    ],
)
def test_final_consumer_cannot_repair_failure_or_accept_changed_original(
    bootstrap_cases, tmp_path, mode
):
    """Final consumption cannot promote failed or foreign publication."""
    case = bootstrap_cases["rubygems"]
    responses = (
        [RubyHttpResponse(409, b"rejected")]
        if mode == "failed-result"
        else [RubyHttpResponse(200, b"accepted"), *case.exact_responses()]
    )
    result, _, _, _ = execute_case(case, tmp_path, responses)
    reads = (
        case.missing_responses()
        if mode == "missing"
        else case.exact_responses()
    )
    if mode == "foreign-byte":
        reads[-1] = RubyHttpResponse(200, b"wrong gem")
    native = ScriptedTransport(*reads)
    reader = case.reader("remote-consumer", native)
    seen = []
    times = iter(
        [CURRENT, CURRENT, EXPIRED]
        if mode == "expired-completion"
        else [CURRENT] * 3
    )

    def consume(original):
        seen.append(original.digest)
        return {"bad": True} if mode == "bad-consumer" else consumer(original)

    with pytest.raises(ValueError, match=r"Ruby|bootstrap"):
        verify_ruby_bootstrap_remote(
            result,
            reader,
            clock=lambda: next(times),
            check_authority=dict,
            consumer=consume,
        )
    if mode == "failed-result":
        assert native.requests == []
    assert seen == (
        [case.original.digest]
        if mode in {"bad-consumer", "expired-completion"}
        else []
    )


@pytest.mark.parametrize(
    "response",
    [
        RubyHttpResponse(401, b"denied"),
        RubyHttpResponse(201, b"{}", "application/json"),
        OSError("private exchange failure"),
    ],
)
def test_exchange_failure_prevents_upload_without_credential_fallback(
    bootstrap_cases, tmp_path, response
):
    """Failed pending-publisher exchange leaves one spent non-upload attempt."""
    case = bootstrap_cases["rubygems"]
    result, native, _, events = execute_case(
        case, tmp_path, [], exchange_response=response
    )
    assert (result.status, result.result) == ("not-attempted", "failed")
    assert result.visibility == ()
    assert events.count("assertion") == 1
    assert len(native.requests) == 1
    assert native.requests[0][1].endswith("/exchange_token")
    assert "private" not in str(result.to_document())
    assert (tmp_path / "claim.json").is_file()


def test_claim_flush_failure_never_reaches_authority_or_assertion(
    bootstrap_cases, tmp_path, monkeypatch
):
    """Stop on ambiguous claim persistence before credential access."""
    case = bootstrap_cases["rubygems"]
    events = []

    def failed(_descriptor):
        message = "controlled claim flush failure"
        raise OSError(message)

    with monkeypatch.context() as patch:
        patch.setattr(execution.os, "fsync", failed)
        with pytest.raises(OSError, match="claim flush"):
            execute_case(case, tmp_path, [], events=events)
    assert events == []
    assert (
        parse_canonical_json((tmp_path / "claim.json").read_bytes())
        == case.marker.to_document()
    )
    with pytest.raises(FileExistsError):
        execute_case(case, tmp_path, [])


def test_expired_exchange_credential_is_not_sent_to_upload(
    bootstrap_cases, tmp_path
):
    """A delayed second authority check cannot reuse an expired short token."""
    case = bootstrap_cases["rubygems"]
    timeline = VirtualClock()
    checked = []

    def authority():
        checked.append("authority")
        if len(checked) == 2:  # noqa: PLR2004 - second authority check
            timeline.advance(13 * 60)
        return {"checked": True}

    result, native, _, _ = execute_case(
        case,
        tmp_path,
        [],
        timeline=timeline,
        deadline_seconds=20 * 60,
        authority=authority,
    )
    assert result.result == "failed"
    assert result.error_kind == "ValueError"
    assert len(native.requests) == 1
    assert native.requests[0][1].endswith("/exchange_token")
    assert result.visibility == ()


def test_nonmissing_invalid_visibility_stops_without_another_observation(
    bootstrap_cases, tmp_path
):
    """An invalid archive stops without another visibility round."""
    case = bootstrap_cases["rubygems"]
    reads = case.exact_responses()
    reads[-1] = RubyHttpResponse(200, b"invalid remote archive")
    result, native, timeline, _ = execute_case(
        case, tmp_path, [RubyHttpResponse(200, b"accepted"), *reads]
    )
    assert (result.status, result.result) == ("succeeded", "failed")
    assert len(result.visibility) == 1
    assert result.visibility[0]["exact"] is False
    assert len(native.requests) == 5  # noqa: PLR2004 - exchange/upload/one observation
    assert timeline.waits == []


@pytest.mark.parametrize("destination", ["github-packages", "rubygems"])
@pytest.mark.parametrize("status", [200, 302])
def test_bootstrap_unexpected_upload_location_is_screened_and_terminal(
    bootstrap_cases, tmp_path, destination, status
):
    """Bootstrap retains the response but rejects upload Location."""
    case = bootstrap_cases[destination]
    result, native, timeline, _events = execute_case(
        case,
        tmp_path,
        [
            RubyHttpResponse(
                status,
                redirect_cases.REDIRECT_BODY,
                redirect_cases.LOCATION,
                location=redirect_cases.LOCATION,
            )
        ],
    )
    assert result.result == "failed"
    assert result.response.status == status
    assert result.visibility == ()
    assert timeline.waits == []
    assert len(native.requests) == (2 if destination == "rubygems" else 1)
    doc = result.to_document()
    assert result.status == "failed"
    assert doc["mutation-classification"] == "possibly-mutated"
    parsed = replace(
        result, response=ruby_response_from_document(doc["response"])
    )
    assert parsed.to_document() == doc
    with pytest.raises(ValueError, match="HTTP response"):
        replace(parsed, status="succeeded")
    assert doc["normal-live-completion"] is False
    retained = canonicalize(doc) + b"".join(
        path.read_bytes()
        for path in (tmp_path / "claim.json-observations").iterdir()
    )
    assert redirect_cases.CAPABILITY.encode() not in retained
    assert WRITE_TOKEN.encode() not in retained
