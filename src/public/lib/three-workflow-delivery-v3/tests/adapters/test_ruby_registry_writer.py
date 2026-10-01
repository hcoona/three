"""One-shot credential exchange and immutable binary upload boundaries."""

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
def test_rubygems_exchange_allows_missing_gem_only_for_bootstrap(bootstrap):
    """A pending registration does not weaken subsequent established scope."""
    document = token_document()
    del document["gem"]
    writer, transport = _writer(json_response(document, 201))
    if bootstrap:
        assert (
            writer.exchange(ASSERTION, now=NOW, bootstrap=True).value
            == WRITE_TOKEN
        )
    else:
        with pytest.raises(ValueError, match="bounded publisher"):
            writer.exchange(ASSERTION, now=NOW)
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
    writer, transport = _writer(response, name=name)
    credentials = {
        "rubygems": RubyPublishingCredential(
            WRITE_TOKEN, NOW + timedelta(minutes=1)
        ),
        "github-packages": WRITE_TOKEN,
    }
    actual = writer.upload(original, credentials[name], now=NOW)
    assert actual is response
    assert transport.requests == [
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
        writer.upload(original, credentials[name], now=NOW)
    assert len(transport.requests) == 1


@pytest.mark.parametrize("name", ["rubygems", "github-packages"])
def test_ruby_upload_interruption_cannot_replay_original(
    ruby_release_original, name
):
    """An exception after send remains spent even if its outcome is unknown."""
    original = ruby_release_original[2]
    writer, transport = _writer(OSError("controlled interruption"), name=name)
    credential = {
        "rubygems": RubyPublishingCredential(
            WRITE_TOKEN, NOW + timedelta(minutes=1)
        ),
        "github-packages": WRITE_TOKEN,
    }[name]
    with pytest.raises(OSError, match="interruption"):
        writer.upload(original, credential, now=NOW)
    with pytest.raises(ValueError, match="already spent"):
        writer.upload(original, credential, now=NOW)
    assert len(transport.requests) == 1
    assert transport.requests[0][3] == original.content


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
