"""One-shot credential exchange and immutable binary upload boundaries."""

from copy import copy
from dataclasses import replace
from datetime import timedelta

import pytest
from three_workflow_delivery_v3.adapters.ruby_registry import (
    RubyHttpResponse,
    RubyPublishingCredential,
    RubyRegistryReader,
    RubyRegistryWriter,
    RubyRequestBudget,
)
from three_workflow_delivery_v3.adapters.rubygems import RubyRegistry

from ..ruby_integration_fixtures import NOW
from ..ruby_registry_fixtures import (
    ASSERTION,
    WRITE_TOKEN,
    ScriptedTransport,
    inventory,
    json_response,
    metadata,
    token_document,
)

HTTP_RESPONSE_LIMIT = 64 * 1024


def _writer(*responses, name="rubygems"):
    transport = ScriptedTransport(*responses)
    return RubyRegistryWriter(
        RubyRegistry(name), RubyRequestBudget(transport)
    ), transport


@pytest.mark.parametrize("bootstrap", [False, True])
def test_rubygems_exchange_accepts_bounded_token_and_hides_secrets(bootstrap):
    """Exchange sends one assertion and validates the exact lifetime limit."""
    writer, transport = _writer(json_response(token_document(), 201))
    credential = writer.exchange(ASSERTION, now=NOW, bootstrap=bootstrap)
    assert credential.value == WRITE_TOKEN
    assert credential.expires_at == NOW + timedelta(minutes=15)
    assert WRITE_TOKEN not in repr(credential)
    assert ASSERTION not in repr(writer)
    assert WRITE_TOKEN not in repr(writer)
    assert transport.requests[0][:3] == (
        "POST",
        "https://rubygems.org/api/v1/oidc/trusted_publisher/exchange_token",
        {"Content-Type": "application/json", "Accept": "application/json"},
    )
    assert transport.json_body() == {"jwt": ASSERTION}
    assert transport.requests[0][4] == HTTP_RESPONSE_LIMIT
    with pytest.raises(ValueError, match="already spent"):
        writer.exchange(ASSERTION, now=NOW)
    assert len(transport.requests) == 1


@pytest.mark.parametrize("bootstrap", [False, True])
def test_rubygems_exchange_allows_omitted_gem_for_both_modes(bootstrap):
    """Optional response scope does not replace the reviewed registration."""
    document = token_document()
    del document["gem"]
    writer, transport = _writer(json_response(document, 201))
    credential = writer.exchange(ASSERTION, now=NOW, bootstrap=bootstrap)
    assert credential.value == WRITE_TOKEN
    assert credential.expires_at == NOW + timedelta(minutes=15)
    assert writer.exchange_receipt["result"] == "accepted"
    with pytest.raises(ValueError, match="already spent"):
        writer.exchange(ASSERTION, now=NOW, bootstrap=bootstrap)
    assert len(transport.requests) == 1


@pytest.mark.parametrize("bootstrap", [False, True])
@pytest.mark.parametrize(
    "gem",
    [
        None,
        {},
        [],
        "hcoona-release-smoke-ruby",
        {"name": None},
        {"name": "another-gem"},
    ],
)
def test_rubygems_exchange_rejects_present_invalid_gem_in_both_modes(
    bootstrap, gem
):
    """Omission is distinct from every malformed present optional member."""
    document = token_document()
    document["gem"] = gem
    writer, transport = _writer(json_response(document, 201))
    with pytest.raises(
        ValueError, match=r"invalid Ruby record|bounded publisher"
    ):
        writer.exchange(ASSERTION, now=NOW, bootstrap=bootstrap)
    assert writer.exchange_receipt["result"] == "gem-invalid"
    with pytest.raises(ValueError, match="already spent"):
        writer.exchange(ASSERTION, now=NOW, bootstrap=bootstrap)
    assert len(transport.requests) == 1


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("name", ""),
        ("rubygems_api_key", ""),
        ("rubygems_api_key", "bad\ncredential"),
        ("scopes", []),
        ("scopes", ["push_rubygem", "yank_rubygem"]),
        ("scopes", "push_rubygem"),
        ("expires_at", NOW.isoformat()),
        ("expires_at", (NOW - timedelta(seconds=1)).isoformat()),
        ("expires_at", (NOW + timedelta(minutes=15, seconds=1)).isoformat()),
        ("expires_at", "2026-09-30T12:01:00"),
        ("expires_at", "invalid-date"),
        ("gem", {"name": "another-gem"}),
        ("gem", "hcoona-release-smoke-ruby"),
    ],
)
def test_rubygems_rejected_token_response_leaves_exchange_spent(field, value):
    """Rejected exchange cannot obtain another credential in this lifetime."""
    document = token_document()
    document[field] = value
    writer, transport = _writer(json_response(document, 201))
    with pytest.raises(
        ValueError,
        match=r"invalid|malformed|bounded publisher|Invalid isoformat",
    ):
        writer.exchange(ASSERTION, now=NOW)
    with pytest.raises(ValueError, match="already spent"):
        writer.exchange(ASSERTION, now=NOW)
    assert len(transport.requests) == 1


@pytest.mark.parametrize(
    "response",
    [
        json_response(token_document(), 200),
        json_response(token_document(), 302),
        json_response(token_document(), 401),
        json_response(token_document(), 500),
        RubyHttpResponse(201, b"{}", "text/html"),
        RubyHttpResponse(201, b"not-json", "application/json"),
        OSError("controlled exchange failure"),
    ],
)
def test_rubygems_exchange_failure_has_no_retry_or_static_fallback(response):
    """A failed exchange stays consumed and never silently returns a token."""
    writer, transport = _writer(response)
    with pytest.raises((ValueError, OSError)):
        writer.exchange(ASSERTION, now=NOW)
    with pytest.raises(ValueError, match="already spent"):
        writer.exchange(ASSERTION, now=NOW)
    assert len(transport.requests) == 1


@pytest.mark.parametrize(
    ("assertion", "now"),
    [
        ("", NOW),
        ("bad value", NOW),
        ("bad\r\nvalue", NOW),
        (ASSERTION, NOW.replace(tzinfo=None)),
    ],
)
def test_rubygems_exchange_invalid_input_never_sends(assertion, now):
    """Invalid assertion or validation time blocks before transmission."""
    writer, transport = _writer()
    with pytest.raises(ValueError, match=r"malformed|aware"):
        writer.exchange(assertion, now=now)
    assert transport.requests == []


def test_github_registry_cannot_exchange_rubygems_assertion():
    """OIDC assertions cannot cross into the GitHub job-token destination."""
    writer, transport = _writer(name="github-packages")
    with pytest.raises(ValueError, match="unsupported"):
        writer.exchange(ASSERTION, now=NOW)
    assert transport.requests == []


@pytest.mark.parametrize("name", ["rubygems", "github-packages"])
@pytest.mark.parametrize("status", [200, 201, 202, 302, 401, 409, 500])
def test_ruby_writer_uploads_original_once_and_preserves_raw_status(
    ruby_release_original, name, status
):
    """Preserve raw status for Release without inventing adapter success."""
    original = ruby_release_original[2]
    response = RubyHttpResponse(status, b"controlled result")
    responses = (
        [json_response(token_document(), 201)] if name == "rubygems" else []
    )
    writer, transport = _writer(*responses, response, name=name)
    credential = (
        writer.exchange(ASSERTION, now=NOW) if responses else WRITE_TOKEN
    )
    actual = writer.upload(original, credential, now=NOW)
    assert actual is response
    assert transport.requests[len(responses) :] == [
        (
            "POST",
            RubyRegistry(name).upload_url,
            {
                "Authorization": WRITE_TOKEN,
                "Content-Type": "application/octet-stream",
            },
            original.content,
            65536,
        )
    ]
    with pytest.raises(ValueError, match="already spent"):
        writer.upload(original, credential, now=NOW)
    assert len(transport.requests) == len(responses) + 1


@pytest.mark.parametrize("name", ["rubygems", "github-packages"])
def test_ruby_upload_interruption_cannot_replay_original(
    ruby_release_original, name
):
    """An exception after send remains spent even if its outcome is unknown."""
    original = ruby_release_original[2]
    responses = (
        [json_response(token_document(), 201)] if name == "rubygems" else []
    )
    writer, transport = _writer(
        *responses, OSError("controlled interruption"), name=name
    )
    credential = (
        writer.exchange(ASSERTION, now=NOW) if responses else WRITE_TOKEN
    )
    with pytest.raises(OSError, match="interruption"):
        writer.upload(original, credential, now=NOW)
    with pytest.raises(ValueError, match="already spent"):
        writer.upload(original, credential, now=NOW)
    assert len(transport.requests) == len(responses) + 1
    assert transport.requests[-1][3] == original.content


@pytest.mark.parametrize(
    ("name", "credential", "now"),
    [
        ("rubygems", WRITE_TOKEN, NOW),
        ("rubygems", RubyPublishingCredential(WRITE_TOKEN, NOW), NOW),
        (
            "rubygems",
            RubyPublishingCredential(WRITE_TOKEN, NOW + timedelta(minutes=1)),
            NOW.replace(tzinfo=None),
        ),
        (
            "rubygems",
            RubyPublishingCredential("bad\nvalue", NOW + timedelta(minutes=1)),
            NOW,
        ),
        (
            "github-packages",
            RubyPublishingCredential(WRITE_TOKEN, NOW + timedelta(minutes=1)),
            NOW,
        ),
        ("github-packages", "", NOW),
        ("github-packages", "bad value", NOW),
    ],
)
def test_ruby_upload_rejects_invalid_or_cross_origin_credentials(
    ruby_release_original, name, credential, now
):
    """Credential errors block before binary bytes are handed to transport."""
    writer, transport = _writer(name=name)
    with pytest.raises(ValueError, match=r"credential|job token"):
        writer.upload(ruby_release_original[2], credential, now=now)
    assert transport.requests == []


@pytest.mark.parametrize(
    "content", [b"", b"x" * 2097153], ids=["empty", "over-limit"]
)
def test_ruby_upload_invalid_size_never_sends_original(
    ruby_release_original, content
):
    """Zero or excessive original bytes cannot reach the upload transport."""
    original = replace(ruby_release_original[2], content=content)
    writer, transport = _writer(name="github-packages")
    with pytest.raises(ValueError, match="byte budget"):
        writer.upload(original, WRITE_TOKEN, now=NOW)
    assert transport.requests == []


def test_ruby_adapters_share_budget_and_keep_exchange_out_of_read_evidence(
    ruby_release_original,
):
    """Share one budget across a local missing/upload/exact scenario."""
    original = ruby_release_original[2]
    exchange = json_response(token_document(), 201)
    upload = RubyHttpResponse(200, b"accepted")
    transport = ScriptedTransport(
        json_response([]),
        exchange,
        upload,
        inventory(),
        json_response(metadata(original)),
        RubyHttpResponse(200, original.content),
    )
    budget = RubyRequestBudget(transport)
    registry = RubyRegistry("rubygems")
    reader = RubyRegistryReader(registry, budget)
    writer = RubyRegistryWriter(registry, budget)
    missing = reader.observe(original)
    assert missing.classification == "missing"
    credential = writer.exchange(ASSERTION, now=NOW)
    assert writer.upload(original, credential, now=NOW) is upload
    exact = reader.observe(original)
    assert exact.classification == "exact"
    assert exact.distribution == original
    assert budget.used == len(transport.requests) == 6  # noqa: PLR2004 - bounded scenario
    assert [request[0] for request in transport.requests] == [
        "GET",
        "POST",
        "POST",
        "GET",
        "GET",
        "GET",
    ]
    assert all(
        "Authorization" not in request[2]
        for request in transport.requests
        if request[0] == "GET"
    )
    assert all(
        response is not exchange
        for _, response in missing.responses + exact.responses
    )
    assert WRITE_TOKEN not in str(exact.to_document())
    assert ASSERTION not in str(exact.to_document())


@pytest.mark.parametrize("expiry_seconds", [901, 902])
def test_rubygems_exchange_measures_lifetime_from_response_receipt(
    expiry_seconds,
):
    """Ordinary server issue-time expiry survives a nonzero request duration."""
    document = token_document()
    expires = NOW + timedelta(seconds=expiry_seconds)
    document["expires_at"] = expires.isoformat()
    writer, transport = _writer(json_response(document, 201))
    ticks = iter([100.0, 102.0])
    credential = writer.exchange(
        ASSERTION, now=NOW, monotonic=lambda: next(ticks)
    )
    assert credential.expires_at == expires
    assert credential.value == WRITE_TOKEN
    assert len(transport.requests) == 1


@pytest.mark.parametrize("expiry_seconds", [1, 2, 903])
def test_rubygems_exchange_rejects_expired_or_overlong_at_receipt(
    expiry_seconds,
):
    """Receipt-time validation neither revives nor clamps server expiry."""
    document = token_document()
    document["expires_at"] = (
        NOW + timedelta(seconds=expiry_seconds)
    ).isoformat()
    writer, transport = _writer(json_response(document, 201))
    ticks = iter([100.0, 102.0])
    with pytest.raises(ValueError, match="bounded publisher"):
        writer.exchange(ASSERTION, now=NOW, monotonic=lambda: next(ticks))
    with pytest.raises(ValueError, match="already spent"):
        writer.exchange(ASSERTION, now=NOW)
    assert len(transport.requests) == 1


@pytest.mark.parametrize("completed", [99.0, float("nan"), float("inf"), True])
def test_rubygems_exchange_invalid_receipt_clock_stays_spent(completed):
    """A completed exchange cannot be replayed after an invalid clock sample."""
    writer, transport = _writer(json_response(token_document(), 201))
    ticks = iter([100.0, completed])
    with pytest.raises(ValueError, match="clock"):
        writer.exchange(ASSERTION, now=NOW, monotonic=lambda: next(ticks))
    with pytest.raises(ValueError, match="already spent"):
        writer.exchange(ASSERTION, now=NOW)
    assert len(transport.requests) == 1
    assert writer.exchange_receipt == {
        "schema": "workflow-delivery/v3/ruby-exchange-receipt-v1",
        "started-at": NOW.isoformat(),
        "elapsed-ms": None,
        "response-received": True,
        "http-status": 201,
        "result": "clock-invalid",
    }


@pytest.mark.parametrize(
    "origin", ["constructed", "copy", "replace", "other-writer"]
)
def test_rubygems_upload_requires_exact_same_writer_exchange_object(
    ruby_release_original, origin
):
    """Equal token values cannot substitute for the validated issued object."""
    writer, transport = _writer(json_response(token_document(), 201))
    issued = writer.exchange(ASSERTION, now=NOW)
    if origin == "constructed":
        credential = RubyPublishingCredential(issued.value, issued.expires_at)
    elif origin == "copy":
        credential = copy(issued)
    elif origin == "replace":
        credential = replace(issued)
    else:
        other, other_transport = _writer(json_response(token_document(), 201))
        credential = other.exchange(ASSERTION, now=NOW)
        assert len(other_transport.requests) == 1
    assert credential == issued
    assert credential is not issued
    with pytest.raises(ValueError, match="credential"):
        writer.upload(ruby_release_original[2], credential, now=NOW)
    assert len(transport.requests) == 1
    assert transport.requests[0][1].endswith("/exchange_token")


def test_rubygems_constructed_credential_cannot_skip_exchange(
    ruby_release_original,
):
    """A plausible long-lived token cannot enter the binary upload API."""
    writer, transport = _writer()
    credential = RubyPublishingCredential(
        WRITE_TOKEN, NOW + timedelta(days=365)
    )
    with pytest.raises(ValueError, match="credential"):
        writer.upload(ruby_release_original[2], credential, now=NOW)
    assert transport.requests == []


@pytest.mark.parametrize(
    "response",
    [json_response(token_document(), 401), OSError("private-exchange-failure")],
)
def test_rubygems_failed_exchange_cannot_be_followed_by_static_upload(
    ruby_release_original, response
):
    """Failed exchange never authorizes a directly constructed fallback key."""
    writer, transport = _writer(response)
    with pytest.raises((ValueError, OSError)):
        writer.exchange(ASSERTION, now=NOW)
    with pytest.raises(ValueError, match="credential"):
        writer.upload(
            ruby_release_original[2],
            RubyPublishingCredential(WRITE_TOKEN, NOW + timedelta(minutes=1)),
            now=NOW,
        )
    assert len(transport.requests) == 1
    assert transport.requests[0][1].endswith("/exchange_token")


@pytest.mark.parametrize("when", ["expiry", "after-expiry", "naive"])
def test_rubygems_issued_credential_still_requires_current_aware_time(
    ruby_release_original, when
):
    """Provenance cannot bypass expiry or timezone admission."""
    writer, transport = _writer(json_response(token_document(), 201))
    credential = writer.exchange(ASSERTION, now=NOW)
    now = {
        "expiry": credential.expires_at,
        "after-expiry": credential.expires_at + timedelta(seconds=1),
        "naive": NOW.replace(tzinfo=None),
    }[when]
    with pytest.raises(ValueError, match="credential"):
        writer.upload(ruby_release_original[2], credential, now=now)
    assert len(transport.requests) == 1


@pytest.mark.parametrize(
    ("field", "value", "result"),
    [
        ("name", "", "name-invalid"),
        ("expires_at", "invalid-date", "expiry-invalid"),
        ("scopes", ["push_rubygem", "yank_rubygem"], "scope-invalid"),
        ("gem", {"name": "wrong-gem"}, "gem-invalid"),
        ("rubygems_api_key", "bad\ncredential", "credential-invalid"),
        ("unrecognized", "arbitrary-private-response", "accepted"),
    ],
)
def test_rubygems_exchange_receipt_is_closed_and_stage_specific(
    field, value, result
):
    """Only fixed outcomes survive the credential-bearing response."""
    document = token_document()
    document[field] = value
    writer, transport = _writer(json_response(document, 201))
    ticks = iter([100.0, 100.1259])
    if result == "accepted":
        assert (
            writer.exchange(
                ASSERTION, now=NOW, monotonic=lambda: next(ticks)
            ).value
            == WRITE_TOKEN
        )
    else:
        with pytest.raises(
            ValueError,
            match=r"invalid|malformed|bounded publisher|Invalid isoformat",
        ):
            writer.exchange(ASSERTION, now=NOW, monotonic=lambda: next(ticks))
    assert writer.exchange_receipt == {
        "schema": "workflow-delivery/v3/ruby-exchange-receipt-v1",
        "started-at": NOW.isoformat(),
        "elapsed-ms": 125,
        "response-received": True,
        "http-status": 201,
        "result": result,
    }
    assert next(ticks, None) is None
    receipt = dict(writer.exchange_receipt)
    external_view = writer.exchange_receipt
    external_view["result"] = "caller-tampering"
    assert writer.exchange_receipt == receipt
    with pytest.raises(ValueError, match="already spent"):
        writer.exchange(ASSERTION, now=NOW)
    assert writer.exchange_receipt == receipt
    assert len(transport.requests) == 1
    assert all(
        secret not in str(receipt)
        for secret in (
            ASSERTION,
            WRITE_TOKEN,
            "arbitrary-private-response",
            "bad\ncredential",
        )
    )


@pytest.mark.parametrize(
    ("response", "result"),
    [
        (
            RubyHttpResponse(401, b"private-response", "application/json"),
            "http-invalid",
        ),
        (
            RubyHttpResponse(201, b"private-response", "application/json"),
            "json-invalid",
        ),
        (RubyHttpResponse(201, b"[]", "application/json"), "json-invalid"),
        (RubyHttpResponse(201, b"{}", "text/private-media"), "http-invalid"),
        (
            RubyHttpResponse(
                201,
                b"{}",
                "application/json",
                location="https://private.invalid/token",
            ),
            "http-invalid",
        ),
    ],
)
def test_rubygems_exchange_receipt_screens_http_and_json_rejections(
    response, result
):
    """Response receipt and safe status survive rejected headers or bodies."""
    writer, transport = _writer(response)
    ticks = iter([100.0, 100.0])
    with pytest.raises(
        ValueError, match=r"exchange failed|invalid|Expecting value"
    ):
        writer.exchange(ASSERTION, now=NOW, monotonic=lambda: next(ticks))
    assert writer.exchange_receipt == {
        "schema": "workflow-delivery/v3/ruby-exchange-receipt-v1",
        "started-at": NOW.isoformat(),
        "elapsed-ms": 0,
        "response-received": True,
        "http-status": response.status,
        "result": result,
    }
    assert len(transport.requests) == 1


def test_rubygems_exchange_receipt_preserves_transport_exception_and_spending():
    """An absent HTTP response is not a received rejection or token issuance."""
    failure = OSError("private-exchange-failure " + WRITE_TOKEN)
    writer, transport = _writer(failure)
    assert writer.exchange_receipt is None
    ticks = iter([100.0])
    with pytest.raises(OSError, match="private-exchange-failure") as caught:
        writer.exchange(ASSERTION, now=NOW, monotonic=lambda: next(ticks))
    assert caught.value is failure
    assert writer.exchange_receipt == {
        "schema": "workflow-delivery/v3/ruby-exchange-receipt-v1",
        "started-at": NOW.isoformat(),
        "elapsed-ms": None,
        "response-received": False,
        "http-status": None,
        "result": "request-failed",
    }
    receipt = dict(writer.exchange_receipt)
    external_view = writer.exchange_receipt
    external_view["result"] = "caller-tampering"
    assert writer.exchange_receipt == receipt
    with pytest.raises(ValueError, match="already spent"):
        writer.exchange(ASSERTION, now=NOW)
    assert writer.exchange_receipt == receipt
    assert len(transport.requests) == 1


@pytest.mark.parametrize(
    ("elapsed", "expected_ms"), [(30.0, 30000), (30.001, None)]
)
def test_rubygems_exchange_receipt_bounds_elapsed_without_extra_clock_reads(
    elapsed,
    expected_ms,
):
    """Long durations do not grow an unbounded diagnostic numeric field."""
    document = token_document()
    document["expires_at"] = (
        NOW + timedelta(seconds=elapsed, minutes=1)
    ).isoformat()
    writer, transport = _writer(json_response(document, 201))
    ticks = iter([100.0, 100.0 + elapsed])
    assert (
        writer.exchange(ASSERTION, now=NOW, monotonic=lambda: next(ticks)).value
        == WRITE_TOKEN
    )
    assert writer.exchange_receipt["elapsed-ms"] == expected_ms
    assert writer.exchange_receipt["result"] == "accepted"
    assert len(transport.requests) == 1
