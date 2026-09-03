"""Focused tests for GitHub protection and Governance authority clients."""

# ruff: noqa: D103

from __future__ import annotations

import json
import urllib.error
import urllib.request
from functools import partial
from http.client import HTTPMessage
from io import BytesIO
from types import SimpleNamespace

import pytest
from three_workflow_delivery_v3.platform.github import (
    GitHubGovernanceClient,
    GitHubRestClient,
    GitHubRestError,
    _NoRedirect,
)
from three_workflow_delivery_v3.release.governance_git import GovernanceGitRead

TOKEN = "real-token-sent-to-fake-transport"  # noqa: S105
DEFAULT_TIMEOUT = 20
FOUND_STATUS = 302


class _GitHubStatusError(GitHubRestError):
    """Synthetic REST status preserving authoritative HTTP identity."""

    def __init__(self, status_code: int) -> None:
        super().__init__(f"GitHub REST returned HTTP {status_code}")
        self.status_code = status_code


def test_rest_client_sends_bearer_token_to_fake_transport() -> None:
    seen: list[str | None] = []

    def opener(request, timeout: int) -> bytes:
        assert timeout == DEFAULT_TIMEOUT
        seen.append(request.get_header("Authorization"))
        return json.dumps({"protected": True}).encode()

    client = GitHubRestClient(
        repository="hcoona/three",
        token=TOKEN,
        opener=opener,
    )

    assert client.is_ref_protected("hcoona/three", "refs/heads/main")
    assert seen == [f"Bearer {TOKEN}"]


def test_rest_client_rejects_off_origin_request() -> None:
    with pytest.raises(GitHubRestError, match=r"api\.github\.com"):
        GitHubRestClient._validate_api_url(  # noqa: SLF001
            "https://example.invalid/runs"
        )


def test_no_redirect_handler_surfaces_location_without_parsing() -> None:
    headers = HTTPMessage()
    headers["Location"] = "https://[::1/path"
    handler = _NoRedirect()
    request = urllib.request.Request("https://api.github.com/repos/x/y")

    with pytest.raises(urllib.error.HTTPError) as raised:
        handler.http_error_302(
            request,
            BytesIO(),
            FOUND_STATUS,
            "Found",
            headers,
        )

    assert raised.value.code == FOUND_STATUS
    assert raised.value.headers["Location"] == "https://[::1/path"


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
                HTTPMessage(),
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
    "eligibility_main_sha",
    [
        pytest.param(None, id="initial-observation"),
        pytest.param("a" * 40, id="continuity-proof"),
    ],
)
def test_governance_client_delegates_source_to_isolated_git_authority(
    eligibility_main_sha: str | None,
) -> None:
    expected = GovernanceGitRead(
        main_sha="c" * 40,
        object_format="sha1",
        blob_oid="b" * 40,
        content=b'{"schema":"replacement-governance"}',
    )
    rest_calls: list[tuple[str, str]] = []
    git_calls: list[dict[str, object]] = []

    def is_ref_protected(repository: str, ref: str) -> bool:
        rest_calls.append((repository, ref))
        return True

    def read(**arguments: object) -> GovernanceGitRead:
        git_calls.append(arguments)
        return expected

    client = GitHubGovernanceClient(
        repository="hcoona/three",
        token=TOKEN,
        rest_client=SimpleNamespace(is_ref_protected=is_ref_protected),
        git_reader=SimpleNamespace(read=read),
    )

    path = ".github/workflow-delivery/governance/hcoona-release-smoke-npm.json"
    actual = client.read_source(
        repository="hcoona/three",
        ref="refs/heads/main",
        path=path,
        eligibility_main_sha=eligibility_main_sha,
    )

    assert actual is expected
    assert (actual.main_sha, actual.blob_oid, actual.content) == (
        "c" * 40,
        "b" * 40,
        b'{"schema":"replacement-governance"}',
    )
    assert git_calls == [
        {
            "repository": "hcoona/three",
            "ref": "refs/heads/main",
            "path": path,
            "eligibility_main_sha": eligibility_main_sha,
        }
    ]
    assert rest_calls == []


def test_governance_client_keeps_branch_protection_on_rest() -> None:
    rest_calls: list[tuple[str, str]] = []
    git_calls: list[dict[str, object]] = []

    def is_ref_protected(repository: str, ref: str) -> bool:
        rest_calls.append((repository, ref))
        return False

    def read(**arguments: object) -> GovernanceGitRead:
        git_calls.append(arguments)
        raise AssertionError

    client = GitHubGovernanceClient(
        repository="hcoona/three",
        token=TOKEN,
        rest_client=SimpleNamespace(is_ref_protected=is_ref_protected),
        git_reader=SimpleNamespace(read=read),
    )

    protected = client.is_ref_protected(
        "hcoona/three",
        "refs/heads/main",
    )

    assert protected is False
    assert rest_calls == [("hcoona/three", "refs/heads/main")]
    assert git_calls == []


@pytest.mark.parametrize("operation", ["protection", "source"])
def test_governance_client_rejects_repository_mismatch_before_delegation(
    operation: str,
) -> None:
    calls: list[object] = []

    def is_ref_protected(repository: str, ref: str) -> bool:
        calls.append((repository, ref))
        return True

    def read(**arguments: object) -> GovernanceGitRead:
        calls.append(arguments)
        raise AssertionError

    client = GitHubGovernanceClient(
        repository="hcoona/three",
        token=TOKEN,
        rest_client=SimpleNamespace(is_ref_protected=is_ref_protected),
        git_reader=SimpleNamespace(read=read),
    )

    invoke = (
        partial(
            client.is_ref_protected,
            "other/repository",
            "refs/heads/main",
        )
        if operation == "protection"
        else partial(
            client.read_source,
            "other/repository",
            "refs/heads/main",
            ".github/workflow-delivery/governance/"
            "hcoona-release-smoke-npm.json",
        )
    )
    with pytest.raises(GitHubRestError, match="repository mismatch"):
        invoke()

    assert calls == []
    assert calls == []
