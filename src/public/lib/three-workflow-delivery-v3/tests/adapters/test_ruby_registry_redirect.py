"""One admitted storage continuation and capability-free retained evidence."""

import base64
from dataclasses import replace

import pytest
from three_workflow_delivery_v3._ruby_native import ruby_digest
from three_workflow_delivery_v3.adapters.ruby_registry import (
    RubyHttpResponse,
    RubyRegistryReader,
    RubyRequestBudget,
)
from three_workflow_delivery_v3.adapters.rubygems import RubyRegistry
from three_workflow_delivery_v3.canonical import canonicalize

from ..ruby_registry_fixtures import (
    READ_TOKEN,
    VERSION,
    ScriptedTransport,
    inventory,
    json_response,
    metadata,
)
from . import test_ruby_registry_reader as reader_cases

indexes = reader_cases.indexes
STORAGE_ORIGIN = "https://rubygemsregistryv2prod.blob.core.windows.net"
STORAGE_PATH = (
    "/rubygemsregistryv2prod/blobs/712433/hcoona-release-smoke-ruby/"
    + VERSION
    + "/12345678-1234-1234-1234-123456789abc"
)
CAPABILITY = "synthetic-capability-never-retain"
LOCATION = STORAGE_ORIGIN + STORAGE_PATH + "?sig=" + CAPABILITY
REDIRECT_BODY = ("<a href='" + LOCATION + "'>signed</a>").encode()


def redirected_reader(indexes, *tail, location=LOCATION):
    """Use the actual reader and budget over exact original native indexes."""
    responses = [
        RubyHttpResponse(200, indexes["empty"]),
        RubyHttpResponse(200, indexes["exact"]),
        RubyHttpResponse(302, REDIRECT_BODY, "text/html", location=location),
        *tail,
    ]
    transport = ScriptedTransport(*responses)
    budget = RubyRequestBudget(transport)
    reader = RubyRegistryReader(
        RubyRegistry("github-packages"), budget, github_read_token=READ_TOKEN
    )
    return reader, transport


def assert_screened(value):
    """Check both literal and encoded capability representations."""
    retained = canonicalize(value.to_document()) + repr(value).encode()
    for secret in (CAPABILITY.encode(), LOCATION.encode(), REDIRECT_BODY):
        assert secret not in retained
        assert base64.b64encode(secret) not in retained
    assert READ_TOKEN.encode() not in retained


@pytest.mark.parametrize("content_type", ["application/octet-stream", LOCATION])
def test_github_one_redirect_preserves_exact_original_and_witness(
    ruby_release_original, indexes, monkeypatch, content_type
):
    """Matching originals stay exact and omit hostile storage headers."""
    original = ruby_release_original[2]
    monkeypatch.setenv("HTTP_PROXY", "http://ambient.invalid")
    monkeypatch.setenv("HTTPS_PROXY", "http://ambient.invalid")
    reader, transport = redirected_reader(
        indexes, RubyHttpResponse(200, original.content, content_type)
    )
    observed = reader.observe(original)
    assert observed.classification == "exact"
    assert observed.distribution == original
    assert observed.distribution.witness == original.witness
    assert observed.failure_kind is None
    assert reader.budget.used == 4  # noqa: PLR2004
    assert transport.responses == []
    assert len(transport.requests) == 4  # noqa: PLR2004
    assert transport.requests[-1][0:4] == ("GET", LOCATION, {}, None)
    assert all(
        set(request[2]) == {"Authorization"}
        for request in transport.requests[:-1]
    )
    doc = observed.to_document()
    assert doc["schema"] == "workflow-delivery/v3/ruby-registry-observation-v2"
    assert doc["artifact-digest"] == ruby_digest(original.content)
    assert doc["responses"][-2]["response"]["kind"] == "screened"
    assert "body-base64" not in doc["responses"][-2]["response"]
    assert doc["responses"][-1]["response"]["kind"] == "original"
    assert (
        base64.b64decode(doc["responses"][-1]["response"]["body-base64"])
        == original.content
    )
    assert_screened(observed)


@pytest.mark.parametrize(
    "location",
    [
        None,
        "",
        " " + LOCATION,
        LOCATION + " ",
        LOCATION.replace("https:", "http:"),
        LOCATION.replace(STORAGE_ORIGIN, STORAGE_ORIGIN.upper()),
        LOCATION.replace(STORAGE_ORIGIN, STORAGE_ORIGIN + ".evil.invalid"),
        LOCATION.replace(".net/", ".net:444/"),
        LOCATION.replace("https://", "https://user@"),
        LOCATION + "#fragment",
        LOCATION + "\r\nInjected: yes",
        LOCATION.replace("/blobs/", "/blobs\\"),
        LOCATION.replace("/blobs/", "/blobs//"),
        LOCATION.replace("/blobs/", "/blobs/../blobs/"),
        LOCATION.replace("/blobs/", "/%62lobs/"),
        LOCATION.replace("/712433/", "/712434/"),
        LOCATION.replace("/hcoona-release-smoke-ruby/", "/foreign-gem/"),
        LOCATION.replace("/" + VERSION + "/", "/9.9.9/"),
        LOCATION.replace("123456789abc", "123456789ABC"),
        LOCATION.replace("12345678-1234", "not-a-uuid"),
        LOCATION.split("?")[0],
        LOCATION + "\u00a0",
    ],
)
def test_github_redirect_route_rejects_unadmitted_coordinate_or_uri(
    ruby_release_original, indexes, location
):
    """URI normalization cannot turn an unadmitted route into a storage send."""
    reader, transport = redirected_reader(indexes, location=location)
    observed = reader.observe(ruby_release_original[2])
    assert observed.classification == "unknown"
    assert observed.distribution is None
    assert len(transport.requests) == 3  # noqa: PLR2004
    assert reader.budget.used == 3  # noqa: PLR2004
    assert transport.responses == []
    assert_screened(observed)


@pytest.mark.parametrize("status", [301, 303, 307, 308])
def test_only_exact_github_302_may_continue(
    ruby_release_original, indexes, status
):
    """Other redirect statuses retain failure evidence without another send."""
    reader, transport = redirected_reader(indexes)
    transport.responses[-1] = RubyHttpResponse(
        status, REDIRECT_BODY, "text/html", location=LOCATION
    )
    observed = reader.observe(ruby_release_original[2])
    assert observed.classification == "unknown"
    assert len(transport.requests) == 3  # noqa: PLR2004
    assert_screened(observed)


@pytest.mark.parametrize("status", [302, 307, 401, 403, 404, 429, 500])
def test_storage_error_or_second_redirect_is_terminal_and_screened(
    ruby_release_original, indexes, status
):
    """Errors and another redirect cannot leak bytes or trigger a third hop."""
    reply = RubyHttpResponse(
        status,
        REDIRECT_BODY,
        LOCATION,
        location=LOCATION if status in {302, 307} else None,
    )
    reader, transport = redirected_reader(indexes, reply)
    observed = reader.observe(ruby_release_original[2])
    assert observed.classification == "unknown"
    assert observed.distribution is None
    assert len(transport.requests) == 4  # noqa: PLR2004
    assert reader.budget.used == 4  # noqa: PLR2004
    evidence = observed.to_document()["responses"][-1]["response"]
    assert evidence["kind"] == "screened"
    assert "body-base64" not in evidence
    assert evidence["body-digest"] == ruby_digest(REDIRECT_BODY)
    assert_screened(observed)


def test_storage_mismatch_retains_only_safe_nonexact_evidence(
    ruby_release_original, indexes
):
    """A successful status carrying foreign bytes cannot establish exactness."""
    reader, transport = redirected_reader(
        indexes, RubyHttpResponse(200, REDIRECT_BODY, LOCATION)
    )
    observed = reader.observe(ruby_release_original[2])
    assert observed.classification == "conflicting"
    assert observed.distribution is None
    assert len(transport.requests) == 4  # noqa: PLR2004
    evidence = observed.to_document()["responses"][-1]["response"]
    assert evidence["kind"] == "screened"
    assert evidence["body-digest"] == ruby_digest(REDIRECT_BODY)
    assert "body-base64" not in evidence
    assert_screened(observed)


def test_storage_equal_bytes_still_require_native_witness(
    ruby_release_original, indexes
):
    """A matching payload cannot excuse a foreign expected target witness."""
    original = ruby_release_original[2]
    expected = replace(
        original,
        witness=replace(original.witness, control_digest="sha256:" + "f" * 64),
    )
    reader, transport = redirected_reader(
        indexes, RubyHttpResponse(200, original.content)
    )
    observed = reader.observe(expected)
    assert observed.classification == "unknown"
    assert observed.distribution is None
    assert len(transport.requests) == 4  # noqa: PLR2004
    assert (
        observed.to_document()["responses"][-1]["response"]["kind"]
        == "screened"
    )
    assert_screened(observed)


@pytest.mark.parametrize("index", [0, 1])
def test_github_index_location_never_opens_storage(
    ruby_release_original, indexes, index
):
    """A successful index with Location is unsafe, not a downloadable gem."""
    reader, transport = redirected_reader(indexes)
    transport.responses[index] = RubyHttpResponse(
        200, REDIRECT_BODY, LOCATION, location=LOCATION
    )
    observed = reader.observe(ruby_release_original[2])
    assert observed.classification == "unknown"
    assert len(transport.requests) == index + 1
    assert (
        observed.to_document()["responses"][-1]["response"]["kind"]
        == "screened"
    )
    assert_screened(observed)


@pytest.mark.parametrize("stage", ["inventory", "metadata", "gem"])
@pytest.mark.parametrize("status", [200, 302])
def test_rubygems_location_never_inherits_github_continuation(
    ruby_release_original, stage, status
):
    """Every RubyGems response carrying Location is screened and terminal."""
    original = ruby_release_original[2]
    replies = [
        inventory(),
        json_response(metadata(original)),
        RubyHttpResponse(200, original.content),
    ]
    index = {"inventory": 0, "metadata": 1, "gem": 2}[stage]
    replies[index] = RubyHttpResponse(
        status, REDIRECT_BODY, LOCATION, location=LOCATION
    )
    transport = ScriptedTransport(*replies)
    reader = RubyRegistryReader(
        RubyRegistry("rubygems"), RubyRequestBudget(transport)
    )
    observed = reader.observe(original)
    assert observed.classification == "unknown"
    assert observed.distribution is None
    assert len(transport.requests) == index + 1
    assert all(request[2] == {} for request in transport.requests)
    assert (
        observed.to_document()["responses"][-1]["response"]["kind"]
        == "screened"
    )
    assert_screened(observed)


@pytest.mark.parametrize("excess", [0, 1])
def test_github_exact_location_byte_bound_is_inclusive(
    ruby_release_original, indexes, excess
):
    """The complete admitted route still requires the 8KiB Location ceiling."""
    original = ruby_release_original[2]
    location = LOCATION + "x" * (8192 - len(LOCATION) + excess)
    reader, transport = redirected_reader(
        indexes, RubyHttpResponse(200, original.content), location=location
    )
    observed = reader.observe(original)
    assert observed.classification == ("unknown" if excess else "exact")
    assert len(transport.requests) == (3 if excess else 4)
    assert_screened(observed)


def test_invalid_location_flag_cannot_be_bypassed_by_valid_remaining_value(
    ruby_release_original, indexes
):
    """A valid URI cannot excuse duplicate or malformed native headers."""
    reader, transport = redirected_reader(indexes)
    transport.responses[-1] = RubyHttpResponse(
        302,
        REDIRECT_BODY,
        "text/html",
        location=LOCATION,
        location_invalid=True,
    )
    observed = reader.observe(ruby_release_original[2])
    assert observed.classification == "unknown"
    assert observed.distribution is None
    assert len(transport.requests) == 3  # noqa: PLR2004
    assert (
        observed.to_document()["responses"][-1]["response"]["reason"]
        == "invalid-location"
    )
    assert_screened(observed)
