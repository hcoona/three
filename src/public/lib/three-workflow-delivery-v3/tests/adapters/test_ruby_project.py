"""Controlled public project lookup; no registry or ownership claim."""

import http.client

import pytest
from three_workflow_delivery_v3.adapters.ruby_project import (
    RubyGemsProjectObservation,
    classify_rubygems_project,
    observe_rubygems_project,
)
from three_workflow_delivery_v3.adapters.ruby_registry import (
    RubyHttpResponse,
    RubyRequestBudget,
    ruby_response_document,
)
from three_workflow_delivery_v3.adapters.rubygems import RUBY_INDEX_LIMIT

from ..ruby_registry_fixtures import ScriptedTransport, json_response

ABSENT = RubyHttpResponse(
    404, b"This rubygem could not be found.", "text/plain"
)


@pytest.mark.parametrize("media", ["text/plain", " Text/Plain ; charset=utf-8"])
def test_exact_project_not_found_signature_admits_absence(media):
    """Exact project not found signature admits absence."""
    response = RubyHttpResponse(404, ABSENT.body, media)
    assert classify_rubygems_project(response) == "absent"


@pytest.mark.parametrize("owners", [[], [{}], [{"handle": "Raffle2282"}]])
def test_successful_owner_array_means_present_even_when_empty(owners):
    """Successful owner array means present even when empty."""
    assert classify_rubygems_project(json_response(owners)) == "present"


@pytest.mark.parametrize(
    "response",
    [
        RubyHttpResponse(404, b"not found", "text/plain"),
        RubyHttpResponse(404, ABSENT.body + b"\n", "text/plain"),
        RubyHttpResponse(404, ABSENT.body, "text/html"),
        RubyHttpResponse(404, ABSENT.body, "application/json"),
        RubyHttpResponse(404, ABSENT.body, ""),
        *[
            RubyHttpResponse(status, ABSENT.body, "text/plain")
            for status in (301, 302, 401, 403, 429, 500)
        ],
        RubyHttpResponse(200, b"[]", "text/plain"),
        RubyHttpResponse(200, b"{", "application/json"),
        RubyHttpResponse(200, b"\xff", "application/json"),
        RubyHttpResponse(200, b'[{"id":1,"id":2}]', "application/json"),
        *[
            json_response(value)
            for value in ({}, None, 1, "owners", [None], [1], [[]])
        ],
        RubyHttpResponse(
            status=True, body=b"[]", content_type="application/json"
        ),
        RubyHttpResponse("404", ABSENT.body, "text/plain"),
        RubyHttpResponse(404, bytearray(ABSENT.body), "text/plain"),
        RubyHttpResponse(404, ABSENT.body, None),
        RubyHttpResponse(
            200, b" " * (RUBY_INDEX_LIMIT + 1), "application/json"
        ),
    ],
)
def test_ambiguous_project_responses_are_unknown(response):
    """Ambiguous project responses are unknown."""
    assert classify_rubygems_project(response) == "unknown"


def test_complete_owner_array_at_byte_limit_is_present():
    """Complete owner array at byte limit is present."""
    response = RubyHttpResponse(
        200, b"[]" + b" " * (RUBY_INDEX_LIMIT - 2), "application/json"
    )
    assert classify_rubygems_project(response) == "present"


@pytest.mark.parametrize(
    "response",
    [ABSENT, json_response([]), RubyHttpResponse(403, b"", "text/plain")],
)
def test_public_project_observation_sends_once_without_credentials(response):
    """Public project observation sends once without credentials."""
    transport = ScriptedTransport(response)
    budget = RubyRequestBudget(transport)
    observation = observe_rubygems_project(budget)
    assert transport.requests == [
        (
            "GET",
            "https://rubygems.org/api/v1/gems/hcoona-release-smoke-ruby/owners.json",
            {
                "Accept": "application/json",
                "User-Agent": "three-workflow-delivery-v3",
            },
            None,
            RUBY_INDEX_LIMIT,
        )
    ]
    assert budget.used == 1
    assert observation.to_document() == {
        "schema": "workflow-delivery/v3/rubygems-project-observation-v1",
        "url": "https://rubygems.org/api/v1/gems/hcoona-release-smoke-ruby/owners.json",
        "classification": classify_rubygems_project(response),
        "failure": None,
        "response": ruby_response_document(response),
    }


@pytest.mark.parametrize(
    ("error", "kind"),
    [
        (http.client.HTTPException("private detail"), "HTTPException"),
        (http.client.RemoteDisconnected("private detail"), "HTTPException"),
        (OSError("private detail"), "OSError"),
        (ValueError("private detail"), "ValueError"),
    ],
)
def test_failed_project_send_is_charged_without_retry_or_detail(error, kind):
    """Failed project send is charged without retry or detail."""
    transport = ScriptedTransport(error)
    budget = RubyRequestBudget(transport)
    result = observe_rubygems_project(budget)
    assert budget.used == 1
    assert len(transport.requests) == 1
    assert result.classification == "unknown"
    assert result.to_document()["failure"] == kind
    assert result.to_document()["response"] is None
    assert "private detail" not in str(result.to_document())


@pytest.mark.parametrize(
    ("response", "failure"),
    [(None, None), (ABSENT, "OSError"), (None, "invented")],
)
def test_project_observation_rejects_contradictory_evidence(response, failure):
    """Project observation rejects contradictory evidence."""
    with pytest.raises(ValueError, match="contradictory evidence"):
        RubyGemsProjectObservation(response, failure)
