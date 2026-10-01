"""One Ruby upload lifetime with bounded visibility and durable evidence."""

import http.client
from datetime import timedelta
from pathlib import Path

import pytest
from three_workflow_delivery_v3.adapters.ruby_registry import (
    RubyHttpResponse,
    RubyRegistryWriter,
    RubyRequestBudget,
)
from three_workflow_delivery_v3.canonical import parse_canonical_json
from three_workflow_delivery_v3.release.ruby_audit import (
    audit_ruby_publication_result,
)
from three_workflow_delivery_v3.release.ruby_execution import (
    execute_ruby_publication,
)

from ..ruby_integration_fixtures import reference
from ..ruby_registry_fixtures import WRITE_TOKEN, ScriptedTransport
from . import ruby_fixtures
from .ruby_fixtures import (
    execute,
)

ruby_publication_cases = ruby_fixtures.ruby_publication_cases


@pytest.mark.parametrize("name", ["rubygems", "github-packages"])
def test_ruby_execution_publishes_one_original_and_retains_exact_evidence(
    ruby_publication_cases, tmp_path, name
):
    """One accepted binary POST plus exact native readback yields published."""
    case = ruby_publication_cases[name]
    result, transport, timeline = execute(
        case,
        tmp_path,
        [RubyHttpResponse(200, b"accepted"), *case.exact_responses()],
    )
    assert result.status == "succeeded"
    assert result.result == "published"
    assert result.mutation_classification == "mutated"
    assert len(result.visibility) == 1
    assert result.visibility[0]["exact"] is True
    offset = int(name == "rubygems")
    if offset:
        assert transport.requests[0][1].endswith("/exchange_token")
    assert transport.requests[offset][0] == "POST"
    assert transport.requests[offset][3] == case.original.content
    assert all(
        request[0] == "GET" for request in transport.requests[offset + 1 :]
    )
    assert timeline.waits == []
    assert (
        parse_canonical_json((tmp_path / "claim.json").read_bytes())
        == case.marker.to_document()
    )
    evidence = tmp_path / "claim.json-observations"
    assert sorted(path.name for path in evidence.iterdir()) == [
        "upload.json",
        "visibility-0.json",
    ]
    assert WRITE_TOKEN not in str(result.to_document())
    audit_ruby_publication_result(result, case.original.content)


@pytest.mark.parametrize("status", [201, 202, 302, 401, 409, 500])
def test_ruby_execution_non200_is_failed_without_visibility_or_retry(
    ruby_publication_cases, tmp_path, status
):
    """Rejection is not recovered through an apparently exact remote state."""
    case = ruby_publication_cases["rubygems"]
    response = RubyHttpResponse(status, b"rejected")
    result, transport, timeline = execute(case, tmp_path, [response])
    assert (result.status, result.result, result.mutation_classification) == (
        "failed",
        "failed",
        "possibly-mutated",
    )
    assert result.upload_response is response
    assert result.visibility == ()
    assert len(transport.requests) == 2  # noqa: PLR2004 - exchange plus upload
    assert timeline.waits == []
    audit_ruby_publication_result(result, case.original.content)


@pytest.mark.parametrize(
    "error",
    [
        OSError("lost response"),
        http.client.BadStatusLine("bad response"),
        http.client.IncompleteRead(b"partial", 1),
    ],
)
def test_ruby_execution_lost_upload_response_retains_unknown_without_replay(
    ruby_publication_cases, tmp_path, error
):
    """Retain sanitized possibly-mutated Results for HTTP parser failures."""
    case = ruby_publication_cases["rubygems"]
    result, transport, _timeline = execute(case, tmp_path, [error])
    assert (result.status, result.result, result.mutation_classification) == (
        "unknown",
        "failed",
        "possibly-mutated",
    )
    assert result.error_kind == type(error).__name__
    assert result.upload_response is None
    assert result.visibility == ()
    assert len(transport.requests) == 2  # noqa: PLR2004 - exchange plus upload
    with pytest.raises(FileExistsError):
        execute(case, tmp_path, [RubyHttpResponse(200, b"must not send")])
    audit_ruby_publication_result(result, case.original.content)


def test_ruby_execution_claim_cannot_be_reused_after_success(
    ruby_publication_cases, tmp_path
):
    """A new writer object cannot bypass the exclusive operation claim."""
    case = ruby_publication_cases["rubygems"]
    result, _transport, _timeline = execute(
        case,
        tmp_path,
        [RubyHttpResponse(200, b"accepted"), *case.exact_responses()],
    )
    assert result.result == "published"
    with pytest.raises(FileExistsError):
        execute(case, tmp_path, [RubyHttpResponse(200, b"must not send")])


def test_ruby_execution_original_tampering_blocks_before_claim(
    ruby_publication_cases, tmp_path
):
    """Immutable Artifact bytes must validate before durable mutation state."""
    case = ruby_publication_cases["rubygems"]
    with pytest.raises(ValueError):  # noqa: PT011 - native artifact rejection
        execute(case, tmp_path, [], payload=case.original.content + b"tamper")
    assert not (tmp_path / "claim.json").exists()


@pytest.mark.parametrize("pending", [0, 1, 5])
def test_ruby_visibility_allows_exact_within_six_reads_and_ten_second_spacing(
    ruby_publication_cases, tmp_path, pending
):
    """Only pending missing state may wait, bounded by six observations."""
    case = ruby_publication_cases["rubygems"]
    responses = [RubyHttpResponse(200, b"accepted")]
    for _ in range(pending):
        responses.extend(case.missing_responses())
    responses.extend(case.exact_responses())
    result, transport, timeline = execute(case, tmp_path, responses)
    assert result.result == "published"
    assert [item["exact"] for item in result.visibility] == [
        False
    ] * pending + [True]
    assert [item["started-monotonic"] for item in result.visibility] == [
        100.0 + 10 * i for i in range(pending + 1)
    ]
    assert timeline.waits == [10.0] * pending
    assert len(transport.requests) == pending + 5
    audit_ruby_publication_result(result, case.original.content)


def test_ruby_visibility_six_missing_reads_end_failed_without_seventh(
    ruby_publication_cases, tmp_path
):
    """Successful POST cannot become published through unbounded waiting."""
    case = ruby_publication_cases["rubygems"]
    result, transport, timeline = execute(
        case,
        tmp_path,
        [RubyHttpResponse(200, b"accepted")] + case.missing_responses() * 6,
    )
    assert result.result == "failed"
    assert result.status == "succeeded"
    assert result.mutation_classification == "mutated"
    assert len(result.visibility) == 6  # noqa: PLR2004 - operation limit
    assert timeline.waits == [10.0] * 5
    assert len(transport.requests) == 8  # noqa: PLR2004 - exchange, upload, six reads
    audit_ruby_publication_result(result, case.original.content)


@pytest.mark.parametrize("deadline", [10, 60])
def test_ruby_visibility_stops_at_outer_deadline_or_sixty_second_window(
    ruby_publication_cases, tmp_path, deadline
):
    """A slow pending read cannot admit another request at the exact limit."""
    case = ruby_publication_cases["rubygems"]
    result, transport, _timeline = execute(
        case,
        tmp_path,
        [RubyHttpResponse(200, b"accepted"), *case.missing_responses()],
        delays=[0, deadline],
        deadline_seconds={10: 10, 60: 120}[deadline],
    )
    assert result.result == "failed"
    assert len(result.visibility) == 1
    assert len(transport.requests) == 3  # noqa: PLR2004 - exchange, upload, read
    audit_ruby_publication_result(result, case.original.content)


def test_ruby_visibility_cannot_accept_exact_after_sixty_second_window(
    ruby_publication_cases, tmp_path
):
    """Exactness completed at the admission deadline remains failed."""
    case = ruby_publication_cases["rubygems"]
    result, _transport, _timeline = execute(
        case,
        tmp_path,
        [RubyHttpResponse(200, b"accepted"), *case.exact_responses()],
        delays=[0, 0, 0, 60],
    )
    assert result.result == "failed"
    assert result.visibility[-1]["exact"] is False
    audit_ruby_publication_result(result, case.original.content)


def test_ruby_visibility_unperformed_wait_cannot_create_close_spaced_reads(
    ruby_publication_cases, tmp_path
):
    """The executor verifies elapsed time instead of trusting wait callbacks."""
    case = ruby_publication_cases["rubygems"]
    result, transport, _timeline = execute(
        case,
        tmp_path,
        [RubyHttpResponse(200, b"accepted"), *case.missing_responses()],
        wait=lambda _seconds: None,
    )
    assert result.result == "failed"
    assert result.error_kind == "ValueError"
    assert len(result.visibility) == 1
    assert len(transport.requests) == 3  # noqa: PLR2004 - exchange, upload, one read


@pytest.mark.parametrize("state", ["conflicting", "unknown"])
def test_ruby_visibility_conflict_or_unknown_is_terminal(
    ruby_publication_cases, tmp_path, state
):
    """Only missing visibility may wait; failures retain original evidence."""
    from ..ruby_registry_fixtures import inventory  # noqa: PLC0415

    case = ruby_publication_cases["rubygems"]
    response = {
        "conflicting": inventory(platform="x86_64-linux"),
        "unknown": RubyHttpResponse(500, b"unavailable"),
    }[state]
    result, transport, timeline = execute(
        case, tmp_path, [RubyHttpResponse(200, b"accepted"), response]
    )
    assert result.result == "failed"
    assert result.visibility[0]["native"]["classification"] == state
    assert result.visibility[0]["exact"] is False
    assert timeline.waits == []
    assert len(transport.requests) == 3  # noqa: PLR2004 - exchange, upload, terminal read
    audit_ruby_publication_result(result, case.original.content)


@pytest.mark.parametrize("filename", ["upload.json", "visibility-0.json"])
def test_ruby_evidence_retention_failure_leaves_claim_without_result(
    ruby_publication_cases, tmp_path, monkeypatch, filename
):
    """Retention failure cannot fabricate a Result or retry upload."""
    case = ruby_publication_cases["rubygems"]
    original_open = Path.open

    def fail_selected(path, *args, **kwargs):
        if path.name == filename:
            message = "controlled evidence retention failure"
            raise OSError(message)
        return original_open(path, *args, **kwargs)

    monkeypatch.setattr(Path, "open", fail_selected)
    with pytest.raises(OSError, match="evidence retention failure"):
        execute(
            case,
            tmp_path,
            [RubyHttpResponse(200, b"accepted"), *case.exact_responses()],
        )
    assert (tmp_path / "claim.json").exists()
    assert not (tmp_path / "claim.json-observations" / filename).exists()
    with pytest.raises(FileExistsError):
        execute(case, tmp_path, [])


@pytest.mark.parametrize(
    "error",
    [
        OSError("lost inventory"),
        http.client.BadStatusLine("bad inventory"),
        http.client.IncompleteRead(b"partial", 1),
    ],
)
def test_ruby_visibility_network_failure_is_replayable_failed_evidence(
    ruby_publication_cases, tmp_path, error
):
    """Offline audit preserves unknown readback as failed evidence."""
    case = ruby_publication_cases["rubygems"]
    result, transport, timeline = execute(
        case, tmp_path, [RubyHttpResponse(200, b"accepted"), error]
    )
    assert result.result == "failed"
    assert result.mutation_classification == "mutated"
    assert result.visibility[0]["native"]["classification"] == "unknown"
    assert result.visibility[0]["native"]["failure-kind"] == (
        "HTTPException"
        if isinstance(error, http.client.HTTPException)
        else "OSError"
    )
    assert len(transport.requests) == 3  # noqa: PLR2004 - exchange, upload, failed read
    assert timeline.waits == []
    audit_ruby_publication_result(result, case.original.content)


def test_ruby_visibility_exact_just_inside_window_is_admitted(
    ruby_publication_cases, tmp_path
):
    """Admit exact completion just inside the visibility window."""
    case = ruby_publication_cases["rubygems"]
    result, _transport, _timeline = execute(
        case,
        tmp_path,
        [RubyHttpResponse(200, b"accepted"), *case.exact_responses()],
        delays=[0, 0, 0, 59.999],
    )
    assert result.result == "published"
    assert result.visibility[-1]["exact"] is True
    audit_ruby_publication_result(result, case.original.content)


@pytest.mark.parametrize("axis", ["utc", "monotonic"])
def test_ruby_visibility_requires_both_completion_clocks_inside_window(
    ruby_publication_cases, tmp_path, axis
):
    """One timely clock cannot compensate for the other exhausting admission."""
    from .ruby_fixtures import VirtualClock  # noqa: PLC0415

    class SplitClock(VirtualClock):
        """Advance one independent clock axis."""

        def advance(self, seconds):
            """Model only one clock moving during the last response."""
            if axis == "utc":
                self.utc += timedelta(seconds=seconds)
            else:
                self.tick += seconds

    case = ruby_publication_cases["rubygems"]
    result, _transport, _timeline = execute(
        case,
        tmp_path,
        [RubyHttpResponse(200, b"accepted"), *case.exact_responses()],
        delays=[0, 0, 0, 60],
        timeline=SplitClock(),
    )
    assert result.result == "failed"
    assert result.visibility[-1]["exact"] is False
    audit_ruby_publication_result(result, case.original.content)


@pytest.mark.parametrize("drift", ["deadline", "regression"])
def test_ruby_authority_drift_after_claim_blocks_before_upload(
    ruby_publication_cases, tmp_path, drift
):
    """Recheck current authority before handing bytes to the writer."""
    from .ruby_fixtures import VirtualClock  # noqa: PLC0415

    class DriftingClock(VirtualClock):
        """Model authority changing after the durable claim."""

        reads = 0

        def clock(self):
            """Change time after admission but before the binary POST."""
            self.reads += 1
            if self.reads == 2:  # noqa: PLR2004 - post-admission sample
                self.advance({"deadline": 120, "regression": -1}[drift])
            return self.utc

    result, transport, _timeline = execute(
        ruby_publication_cases["rubygems"],
        tmp_path,
        [],
        timeline=DriftingClock(),
    )
    assert result.status == "not-attempted"
    assert result.result == "failed"
    assert result.mutation_classification == "not-mutated"
    assert len(transport.requests) == 1
    assert transport.requests[0][1].endswith("/exchange_token")
    assert (tmp_path / "claim.json").exists()


def test_ruby_invalid_authority_deadline_blocks_before_claim(
    ruby_publication_cases, tmp_path
):
    """An already expired effect lifetime has no durable execution claim."""
    with pytest.raises(ValueError, match="deadline is invalid"):
        execute(
            ruby_publication_cases["rubygems"], tmp_path, [], deadline_seconds=0
        )
    assert not (tmp_path / "claim.json").exists()


@pytest.mark.parametrize("mismatch", ["reader", "writer", "budget"])
def test_ruby_execution_requires_one_destination_and_shared_budget(
    ruby_publication_cases, tmp_path, mismatch
):
    """Cross-destination clients or separate counters cannot acquire a claim."""
    case = ruby_publication_cases["rubygems"]
    foreign = ruby_publication_cases["github-packages"]
    transport = ScriptedTransport([])
    budget = RubyRequestBudget(transport)
    reader = (foreign if mismatch == "reader" else case).reader(budget)
    writer = RubyRegistryWriter(
        (foreign if mismatch == "writer" else case).registry,
        RubyRequestBudget(transport) if mismatch == "budget" else budget,
    )
    timeline = ruby_fixtures.VirtualClock()
    claim = tmp_path / "claim.json"
    with pytest.raises(
        ValueError, match="one shared destination request budget"
    ):
        execute_ruby_publication(
            case.marker,
            reference(case.marker.to_document(), 907),
            case.original.content,
            credential=WRITE_TOKEN,
            reader=reader,
            writer=writer,
            claim_path=claim,
            deadline=timeline.utc + timedelta(seconds=120),
            clock=timeline.clock,
            monotonic=timeline.monotonic,
            wait=timeline.wait,
        )
    assert not claim.exists()
    assert transport.requests == []
    assert budget.used == 0
