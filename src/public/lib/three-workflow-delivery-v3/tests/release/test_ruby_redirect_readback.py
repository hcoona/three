"""Offline replay joins and real publication visibility with a screened hop."""

from copy import deepcopy
from http import HTTPStatus

import pytest
from three_workflow_delivery_v3.adapters.ruby_registry import (
    RubyHttpResponse,
    ruby_response_document,
)
from three_workflow_delivery_v3.canonical import canonical_sha256, canonicalize
from three_workflow_delivery_v3.release.ruby_audit import (
    audit_ruby_publication_result,
    ruby_publication_result_from_document,
)
from three_workflow_delivery_v3.release.ruby_readback import (
    replay_ruby_observation,
    ruby_response_from_document,
)

from ..adapters import test_ruby_registry_redirect as redirects
from . import ruby_fixtures
from .ruby_fixtures import execute

ruby_publication_cases = ruby_fixtures.ruby_publication_cases
indexes = redirects.indexes


def capture(original, indexes, response=None):
    """Capture with the actual reader before independent offline replay."""
    reader, transport = redirects.redirected_reader(
        indexes, response or RubyHttpResponse(200, original.content)
    )
    observed = reader.observe(original)
    assert len(transport.requests) == 4  # noqa: PLR2004
    return observed, reader.registry


def test_github_redirect_observation_replays_safe_route_and_exact_bytes_offline(
    ruby_release_original, indexes, monkeypatch
):
    """Logical joins reproduce exactness without signed URLs or sends."""
    original = ruby_release_original[2]
    observed, registry = capture(original, indexes)
    document = observed.to_document()
    request = document["responses"][-1]["request"]
    assert request["method"] == "GET"
    assert request["origin"] == redirects.STORAGE_ORIGIN
    assert request["path"] == redirects.STORAGE_PATH
    assert request["credentials"] == "none"
    assert request["redirect-response-digest"] == canonical_sha256(
        document["responses"][-2]["response"]
    )
    assert (
        request["location-digest"]
        == document["responses"][-2]["response"]["location-digest"]
    )
    calls = []
    monkeypatch.setattr(
        redirects.ScriptedTransport, "request", lambda *args: calls.append(args)
    )
    replayed = replay_ruby_observation(document, original, registry)
    assert replayed.to_document() == document
    assert replayed.classification == "exact"
    assert replayed.distribution == original
    assert calls == []
    redirects.assert_screened(replayed)


@pytest.mark.parametrize(
    "change",
    [
        "predecessor",
        "location",
        "origin",
        "path",
        "method",
        "credentials",
        "extra-request",
        "remove-redirect",
        "remove-storage",
        "reorder",
        "extra-response",
        "old-schema",
        "screened-as-original",
        "body-digest",
    ],
)
def test_redirect_replay_rejects_forged_missing_reordered_or_extra_evidence(
    ruby_release_original, indexes, change
):
    """Valid surrounding evidence cannot excuse a forged continuation join."""
    original = ruby_release_original[2]
    observed, registry = capture(original, indexes)
    document = observed.to_document()
    entries = document["responses"]
    request = entries[-1]["request"]
    mutations = {
        "predecessor": lambda: request.update(
            {"redirect-response-digest": "sha256:" + "f" * 64}
        ),
        "location": lambda: request.update(
            {"location-digest": "sha256:" + "f" * 64}
        ),
        "origin": lambda: request.update(origin="https://foreign.invalid"),
        "path": lambda: request.update(
            path=redirects.STORAGE_PATH.replace("/712433/", "/712434/")
        ),
        "method": lambda: request.update(method="POST"),
        "credentials": lambda: request.update(credentials="github-job-token"),
        "extra-request": lambda: request.update(url=redirects.LOCATION),
        "remove-redirect": lambda: entries.pop(-2),
        "remove-storage": entries.pop,
        "reorder": entries.reverse,
        "extra-response": lambda: entries.append(deepcopy(entries[-1])),
        "old-schema": lambda: document.update(
            schema="workflow-delivery/v3/ruby-registry-observation-v1"
        ),
        "screened-as-original": lambda: entries[-2]["response"].update(
            kind="original"
        ),
        "body-digest": lambda: entries[-1]["response"].update(
            {"body-digest": "sha256:" + "f" * 64}
        ),
    }
    mutations[change]()
    with pytest.raises(ValueError):  # noqa: PT011 - replay closes several evidence layers
        replay_ruby_observation(document, original, registry)


@pytest.mark.parametrize("status", [200, 302, 403])
def test_screened_metadata_only_observation_never_establishes_exactness(
    ruby_release_original, indexes, status
):
    """Rebound status, hashes and scalar success cannot create missing bytes."""
    original = ruby_release_original[2]
    response = RubyHttpResponse(
        status, redirects.REDIRECT_BODY, redirects.LOCATION
    )
    observed, registry = capture(original, indexes, response)
    replayed = replay_ruby_observation(
        observed.to_document(), original, registry
    )
    assert replayed.classification == (
        "conflicting" if status == HTTPStatus.OK else "unknown"
    )
    redirects.assert_screened(replayed)
    document = observed.to_document()
    document.update(
        classification="exact",
        **{"artifact-digest": original.digest, "failure-kind": None},
    )
    with pytest.raises(ValueError):  # noqa: PT011 - no original payload supports exactness
        replay_ruby_observation(document, original, registry)


def redirect_tail(case, response=None):
    """Keep native index bytes while replacing only the gem's storage route."""
    return [
        *case.exact_responses()[:-1],
        RubyHttpResponse(
            302,
            redirects.REDIRECT_BODY,
            "text/html",
            location=redirects.LOCATION,
        ),
        response or RubyHttpResponse(200, case.original.content),
    ]


@pytest.mark.parametrize("storage_status", [200, 403])
def test_redirect_visibility_fifth_missing_sixth_exact_uses_fifteen_sends(
    ruby_publication_cases, tmp_path, storage_status
):
    """The sixth observation spends its storage debit even when it fails."""
    case = ruby_publication_cases["github-packages"]
    storage = RubyHttpResponse(
        storage_status,
        case.original.content
        if storage_status == HTTPStatus.OK
        else redirects.REDIRECT_BODY,
    )
    result, transport, timeline = execute(
        case,
        tmp_path,
        [
            RubyHttpResponse(200, b"accepted"),
            *case.missing_responses() * 5,
            *redirect_tail(case, storage),
        ],
    )
    assert result.result == (
        "published" if storage_status == HTTPStatus.OK else "failed"
    )
    assert len(transport.requests) == 15  # noqa: PLR2004
    assert len(result.visibility) == 6  # noqa: PLR2004
    assert timeline.waits == [10.0] * 5
    assert [item["started-monotonic"] for item in result.visibility] == [
        100.0 + 10 * i for i in range(6)
    ]
    assert sum(request[0] == "POST" for request in transport.requests) == 1
    assert transport.requests[-1][2] == {}
    assert redirects.CAPABILITY.encode() not in canonicalize(
        result.to_document()
    )
    audit_ruby_publication_result(result, case.original.content)


def test_six_missing_visibility_uses_thirteen_sends_without_seventh(
    ruby_publication_cases, tmp_path
):
    """Upload acceptance cannot outlive the unchanged visibility ceiling."""
    case = ruby_publication_cases["github-packages"]
    result, transport, timeline = execute(
        case,
        tmp_path,
        [RubyHttpResponse(200, b"accepted"), *case.missing_responses() * 6],
    )
    assert result.result == "failed"
    assert len(transport.requests) == 13  # noqa: PLR2004
    assert len(result.visibility) == 6  # noqa: PLR2004
    assert timeline.waits == [10.0] * 5
    assert sum(request[0] == "POST" for request in transport.requests) == 1
    audit_ruby_publication_result(result, case.original.content)


def test_rubygems_visibility_worst_case_retains_ten_send_contract(
    ruby_publication_cases, tmp_path
):
    """RubyGems retains exchange, upload, five missing and three exact sends."""
    case = ruby_publication_cases["rubygems"]
    result, transport, timeline = execute(
        case,
        tmp_path,
        [
            RubyHttpResponse(200, b"accepted"),
            *case.missing_responses() * 5,
            *case.exact_responses(),
        ],
    )
    assert result.result == "published"
    assert len(transport.requests) == 10  # noqa: PLR2004
    assert len(result.visibility) == 6  # noqa: PLR2004
    assert timeline.waits == [10.0] * 5
    assert sum(request[0] == "POST" for request in transport.requests) == 2  # noqa: PLR2004
    assert all(
        request[1].startswith("https://rubygems.org/")
        for request in transport.requests
    )
    audit_ruby_publication_result(result, case.original.content)


@pytest.mark.parametrize(
    "change",
    [
        "kind",
        "raw-body",
        "arbitrary-media",
        "size",
        "size-bool",
        "digest",
        "reason",
        "unsafe-origin",
        "partial-route",
        "extra",
    ],
)
def test_screened_http_evidence_cannot_impersonate_an_original(change):
    """The closed screened union cannot smuggle original or unsafe fields."""
    document = ruby_response_document(
        RubyHttpResponse(
            302,
            redirects.REDIRECT_BODY,
            "text/html",
            location=redirects.LOCATION,
        )
    )
    mutations = {
        "kind": lambda: document.update(kind="original"),
        "raw-body": lambda: document.update({"body-base64": "c2VjcmV0"}),
        "arbitrary-media": lambda: document.update(
            {"content-type": redirects.LOCATION}
        ),
        "size": lambda: document.update({"body-size": -1}),
        "size-bool": lambda: document.update({"body-size": True}),
        "digest": lambda: document.update({"body-digest": "invalid"}),
        "reason": lambda: document.update(reason=redirects.LOCATION),
        "unsafe-origin": lambda: document.update(
            {
                "safe-origin": "https://foreign.invalid",
                "safe-path": redirects.STORAGE_PATH,
            }
        ),
        "partial-route": lambda: document.update(
            {"safe-origin": redirects.STORAGE_ORIGIN}
        ),
        "extra": lambda: document.update(location=redirects.LOCATION),
    }
    mutations[change]()
    with pytest.raises((ValueError, TypeError)):
        ruby_response_from_document(document)


@pytest.mark.parametrize("status", [200, 302])
@pytest.mark.parametrize("destination", ["github-packages", "rubygems"])
def test_unexpected_upload_location_is_screened_and_not_accepted(
    ruby_publication_cases, tmp_path, status, destination
):
    """Upload screening grants neither visibility nor retry."""
    case = ruby_publication_cases[destination]
    result, transport, _timeline = execute(
        case,
        tmp_path,
        [
            RubyHttpResponse(
                status,
                redirects.REDIRECT_BODY,
                redirects.LOCATION,
                location=redirects.LOCATION,
            )
        ],
    )
    assert result.result == "failed"
    assert result.status == "failed"
    assert result.upload_response.status == status
    assert result.mutation_classification == "possibly-mutated"
    parsed = ruby_publication_result_from_document(
        result.to_document(), case.marker, case.original.content
    )
    assert parsed.to_document() == result.to_document()
    forged = result.to_document()
    forged["status"] = "succeeded"
    with pytest.raises(ValueError):  # noqa: PT011 - unsupported response is never accepted
        ruby_publication_result_from_document(
            forged, case.marker, case.original.content
        )
    assert result.visibility == ()
    assert len(transport.requests) == (2 if destination == "rubygems" else 1)
    assert (
        len(
            [
                request
                for request in transport.requests
                if request[1].endswith("/api/v1/gems")
            ]
        )
        == 1
    )
    assert redirects.CAPABILITY.encode() not in canonicalize(
        result.to_document()
    )
    audit_ruby_publication_result(result, case.original.content)


def test_late_storage_result_cannot_extend_visibility(
    ruby_publication_cases, tmp_path
):
    """A late fourth exact send stays failed and spent."""
    case = ruby_publication_cases["github-packages"]
    replies = [
        RubyHttpResponse(200, b"accepted"),
        *case.missing_responses() * 5,
        *redirect_tail(case),
    ]
    delays = [0] * (len(replies) - 1) + [11]
    result, transport, _timeline = execute(
        case, tmp_path, replies, delays=delays
    )
    assert result.result == "failed"
    assert len(transport.requests) == 15  # noqa: PLR2004
    assert len(result.visibility) == 6  # noqa: PLR2004
    assert result.visibility[-1]["exact"] is False
    assert result.visibility[-1]["native"]["classification"] == "exact"
    audit_ruby_publication_result(result, case.original.content)


def test_failed_storage_send_retains_safe_logical_request_and_replays(
    ruby_release_original, indexes
):
    """A lost reply retains a safe attempted send without secret error text."""
    original = ruby_release_original[2]
    observed, registry = capture(original, indexes, OSError(redirects.LOCATION))
    document = observed.to_document()
    assert document["classification"] == "unknown"
    assert document["failure-stage"] == "request"
    assert document["responses"][-1]["response"] is None
    assert document["responses"][-1]["request"]["credentials"] == "none"
    replayed = replay_ruby_observation(document, original, registry)
    assert replayed.to_document() == document
    redirects.assert_screened(replayed)


def test_historical_raw_redirect_cannot_supply_current_original_evidence():
    """Old raw redirect bodies cannot enter the new original response arm."""
    import base64  # noqa: PLC0415

    from three_workflow_delivery_v3._ruby_native import (  # noqa: PLC0415
        ruby_digest,
    )

    historical = {
        "kind": "original",
        "status": 302,
        "content-type": "text/html",
        "body-base64": base64.b64encode(redirects.REDIRECT_BODY).decode(),
        "body-digest": ruby_digest(redirects.REDIRECT_BODY),
    }
    with pytest.raises(ValueError, match="digest or status"):
        ruby_response_from_document(historical)
