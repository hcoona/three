"""Focused tests for the concrete GitHub REST platform client."""

# ruff: noqa: D103

from __future__ import annotations

import json
import urllib.error
import urllib.parse
import urllib.request
from email.message import Message
from io import BytesIO

import pytest
from three_workflow_delivery_v3.platform.github import (
    GitHubRestClient,
    GitHubRestError,
    iter_all_runs,
)

TOKEN = "real-token-sent-to-fake-transport"  # noqa: S105
TOTAL_RUNS = 201
FINAL_PAGE = 3


class _GitHubStatusError(GitHubRestError):
    """Synthetic REST status preserving authoritative HTTP identity."""

    def __init__(self, status_code: int) -> None:
        super().__init__(f"GitHub REST returned HTTP {status_code}")
        self.status_code = status_code


def _run(index: int) -> dict[str, object]:
    return {
        "id": index,
        "run_attempt": 1,
        "head_sha": "a" * 40,
        "node_id": f"WFR_{index}",
        "conclusion": "success",
        "status": "completed",
    }


def test_rest_client_sends_real_bearer_token_to_fake_transport() -> None:
    seen: list[str | None] = []

    def opener(request, timeout: int) -> bytes:
        del timeout
        seen.append(request.get_header("Authorization"))
        return json.dumps({"total_count": 0, "workflow_runs": []}).encode()

    client = GitHubRestClient(
        repository="hcoona/three",
        token=TOKEN,
        workflow_path=".github/workflows/reusable.yml",
        opener=opener,
    )

    assert iter_all_runs(client) == ()
    assert seen == [f"Bearer {TOKEN}"]


def test_rest_client_paginates_full_pages_to_exact_final_count() -> None:
    def opener(request, timeout: int) -> bytes:
        del timeout
        parsed = urllib.parse.urlparse(request.full_url)
        query = urllib.parse.parse_qs(parsed.query)
        page = int(query["page"][0])
        start = (page - 1) * 100
        count = 1 if page == FINAL_PAGE else 100
        return json.dumps(
            {
                "total_count": TOTAL_RUNS,
                "workflow_runs": [
                    _run(index) for index in range(start, start + count)
                ],
            }
        ).encode()

    client = GitHubRestClient(
        repository="hcoona/three",
        token=TOKEN,
        workflow_path=".github/workflows/reusable.yml",
        opener=opener,
    )

    assert len(iter_all_runs(client)) == TOTAL_RUNS


@pytest.mark.parametrize(
    ("first_count", "second_total", "message"),
    [
        (99, 201, "non-final page is short"),
        (100, 202, "total_count changed"),
    ],
)
def test_rest_client_rejects_unstable_or_inexact_pagination(
    first_count: int,
    second_total: int,
    message: str,
) -> None:
    def opener(request, timeout: int) -> bytes:
        del timeout
        parsed = urllib.parse.urlparse(request.full_url)
        query = urllib.parse.parse_qs(parsed.query)
        page = int(query["page"][0])
        total = TOTAL_RUNS if page == 1 else second_total
        count = first_count if page == 1 else 100
        start = (page - 1) * 100
        return json.dumps(
            {
                "total_count": total,
                "workflow_runs": [
                    _run(index) for index in range(start, start + count)
                ],
            }
        ).encode()

    client = GitHubRestClient(
        repository="hcoona/three",
        token=TOKEN,
        workflow_path=".github/workflows/reusable.yml",
        opener=opener,
    )

    with pytest.raises(ValueError, match=message):
        iter_all_runs(client)


def test_rest_client_rejects_off_origin_redirect_before_followup_request() -> (
    None
):
    with pytest.raises(GitHubRestError, match=r"api\.github\.com"):
        GitHubRestClient._validate_api_url(  # noqa: SLF001
            "https://example.invalid/runs"
        )


def test_rest_client_reads_exact_artifact_independent_run_attempt() -> None:
    seen: list[str] = []

    def opener(request, timeout: int) -> bytes:
        del timeout
        seen.append(request.full_url)
        return json.dumps(
            {
                "id": 41,
                "node_id": "WFR_41",
                "head_sha": "a" * 40,
                "run_attempt": 3,
                "status": "completed",
                "conclusion": "success",
                "event": "workflow_dispatch",
                "workflow_id": 7,
            }
        ).encode()

    client = GitHubRestClient(
        repository="hcoona/three",
        token=TOKEN,
        opener=opener,
    )

    fact = client.get_run_attempt(41, 3)

    assert seen == [
        "https://api.github.com/repos/hcoona/three/actions/runs/41/attempts/3"
    ]
    assert (
        fact.run_id,
        fact.node_id,
        fact.head_sha,
        fact.run_attempt,
        fact.status,
        fact.conclusion,
    ) == (41, "WFR_41", "a" * 40, 3, "completed", "success")


@pytest.mark.parametrize(
    "changed",
    [
        {"id": "41"},
        {"node_id": None},
        {"head_sha": None},
        {"run_attempt": "3"},
        {"status": None},
        {"conclusion": 1},
    ],
)
def test_rest_client_rejects_malformed_exact_run_attempt(
    changed: dict[str, object],
) -> None:
    def opener(request, timeout: int) -> bytes:
        del request, timeout
        return json.dumps(
            {
                "id": 41,
                "node_id": "WFR_41",
                "head_sha": "a" * 40,
                "run_attempt": 3,
                "status": "completed",
                "conclusion": "success",
                **changed,
            }
        ).encode()

    client = GitHubRestClient(
        repository="hcoona/three",
        token=TOKEN,
        opener=opener,
    )

    with pytest.raises(GitHubRestError, match="exact run attempt"):
        client.get_run_attempt(41, 3)


def test_ref_protection_false_is_authoritative(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    client = GitHubRestClient(repository="hcoona/three", token=TOKEN)

    def not_protected(path: str) -> dict[str, object]:
        assert path == "/repos/hcoona/three/branches/main"
        return {"protected": False}

    monkeypatch.setattr(client, "_json", not_protected)

    assert client.is_ref_protected("hcoona/three", "refs/heads/main") is False


def test_ref_protection_success_is_authoritative_true() -> None:
    def opener(request, timeout: int) -> bytes:
        del timeout
        assert request.full_url.endswith("/repos/hcoona/three/branches/main")
        return json.dumps({"protected": True}).encode()

    client = GitHubRestClient(
        repository="hcoona/three",
        token=TOKEN,
        opener=opener,
    )

    assert client.is_ref_protected("hcoona/three", "refs/heads/main") is True


@pytest.mark.parametrize(
    "failure",
    [
        pytest.param(_GitHubStatusError(404), id="not-found"),
        pytest.param(_GitHubStatusError(403), id="permission"),
        pytest.param(_GitHubStatusError(503), id="server"),
        pytest.param(GitHubRestError("network unavailable"), id="network"),
    ],
)
def test_ref_protection_transport_unknowns_raise(
    monkeypatch: pytest.MonkeyPatch,
    failure: GitHubRestError,
) -> None:
    client = GitHubRestClient(repository="hcoona/three", token=TOKEN)

    def fail(_path: str) -> dict[str, object]:
        raise failure

    monkeypatch.setattr(client, "_json", fail)

    with pytest.raises(GitHubRestError) as raised:
        client.is_ref_protected("hcoona/three", "refs/heads/main")

    assert raised.value is failure


@pytest.mark.parametrize("status", [403, 404, 503])
def test_ref_protection_http_failures_are_unknown(
    monkeypatch: pytest.MonkeyPatch,
    status: int,
) -> None:
    class ErrorOpener:
        def open(self, request, *, timeout: int):
            del timeout
            raise urllib.error.HTTPError(
                request.full_url,
                status,
                "request failed",
                Message(),
                BytesIO(),
            )

    monkeypatch.setattr(
        urllib.request,
        "build_opener",
        lambda *_handlers: ErrorOpener(),
    )
    client = GitHubRestClient(repository="hcoona/three", token=TOKEN)

    with pytest.raises(GitHubRestError) as raised:
        client.is_ref_protected("hcoona/three", "refs/heads/main")

    assert raised.value.status_code == status


@pytest.mark.parametrize(
    "payload",
    [
        pytest.param({}, id="empty-object"),
        pytest.param(
            {"message": "not a protection response"}, id="error-shape"
        ),
    ],
)
def test_ref_protection_malformed_success_response_is_unknown(
    payload: dict[str, object],
) -> None:
    def opener(request, timeout: int) -> bytes:
        del request, timeout
        return json.dumps(payload).encode()

    client = GitHubRestClient(
        repository="hcoona/three",
        token=TOKEN,
        opener=opener,
    )

    with pytest.raises(GitHubRestError, match="protection"):
        client.is_ref_protected("hcoona/three", "refs/heads/main")


@pytest.mark.parametrize(
    ("payload", "message"),
    [
        pytest.param(
            b"{", "GitHub REST returned malformed JSON", id="api-json"
        ),
        pytest.param(
            json.dumps(
                {
                    "sha": "b" * 40,
                    "encoding": "base64",
                    "content": "***",
                }
            ).encode(),
            "Governance content base64 is malformed",
            id="base64",
        ),
        pytest.param(
            json.dumps(
                {
                    "sha": "b" * 40,
                    "encoding": "hex",
                    "content": "00",
                }
            ).encode(),
            "Governance content response is malformed",
            id="protocol",
        ),
    ],
)
def test_governance_content_transport_failures_remain_rest_errors(
    payload: bytes,
    message: str,
) -> None:
    def opener(request, timeout: int) -> bytes:
        del request, timeout
        return payload

    client = GitHubRestClient(
        repository="hcoona/three",
        token=TOKEN,
        opener=opener,
    )

    with pytest.raises(GitHubRestError) as raised:
        client.read_blob(
            "hcoona/three",
            "a" * 40,
            ".github/workflow-delivery/governance/"
            "hcoona-release-smoke-npm.json",
        )

    assert str(raised.value) == message
    assert type(raised.value).__name__ != "GovernanceRejectionError"
