"""Focused tests for the concrete GitHub Governance REST client."""

# ruff: noqa: D103

from __future__ import annotations

import json
import urllib.error
import urllib.request
from http.client import HTTPMessage
from io import BytesIO

import pytest
from three_workflow_delivery_v3.platform.github import (
    GitHubRestClient,
    GitHubRestError,
    _NoRedirect,
)

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


@pytest.mark.parametrize(
    "line_separator",
    [
        pytest.param("\r", id="cr"),
        pytest.param("\n", id="lf"),
        pytest.param("\r\n", id="crlf"),
    ],
)
def test_read_blob_accepts_cr_lf_wrapped_base64(
    line_separator: str,
) -> None:
    expected_content = b"policy\nbytes\x00"
    encoded_content = "cG9saWN5CmJ5dGVzAA=="
    wrapped_content = line_separator.join(
        (encoded_content[:5], encoded_content[5:13], encoded_content[13:])
    )
    blob_oid = "b" * 40
    commit = "a" * 40
    path = ".github/workflow-delivery/governance/hcoona-release-smoke-npm.json"
    seen: list[str] = []

    def opener(request, timeout: int) -> bytes:
        assert timeout == DEFAULT_TIMEOUT
        seen.append(request.full_url)
        return json.dumps(
            {
                "sha": blob_oid,
                "encoding": "base64",
                "content": wrapped_content,
            }
        ).encode()

    client = GitHubRestClient(
        repository="hcoona/three",
        token=TOKEN,
        opener=opener,
    )

    blob = client.read_blob("hcoona/three", commit, path)

    assert (blob.blob_oid, blob.content) == (blob_oid, expected_content)
    assert seen == [
        f"https://api.github.com/repos/hcoona/three/contents/{path}?ref={commit}"
    ]


@pytest.mark.parametrize(
    "content",
    [
        pytest.param("cG9saW*5CmJ5dGVzAA==", id="invalid-alphabet"),
        pytest.param(
            "cG9s\r\naW*5CmJ5dGVzAA==",
            id="wrapped-invalid-alphabet",
        ),
        pytest.param("cG9s aWN5CmJ5dGVzAA==", id="space"),
        pytest.param("cG9s\taWN5CmJ5dGVzAA==", id="tab"),
        pytest.param("Zg=", id="missing-padding"),
        pytest.param("Z\r\ng=", id="wrapped-missing-padding"),
        pytest.param("Zg===", id="excess-padding"),
        pytest.param("Zg==\r\n=", id="wrapped-excess-padding"),
    ],
)
def test_read_blob_rejects_non_cr_lf_or_malformed_base64(
    content: str,
) -> None:
    commit = "a" * 40
    path = ".github/workflow-delivery/governance/hcoona-release-smoke-npm.json"
    seen: list[str] = []

    def opener(request, timeout: int) -> bytes:
        assert timeout == DEFAULT_TIMEOUT
        seen.append(request.full_url)
        return json.dumps(
            {
                "sha": "b" * 40,
                "encoding": "base64",
                "content": content,
            }
        ).encode()

    client = GitHubRestClient(
        repository="hcoona/three",
        token=TOKEN,
        opener=opener,
    )

    with pytest.raises(GitHubRestError) as raised:
        client.read_blob("hcoona/three", commit, path)

    assert str(raised.value) == "Governance content base64 is malformed"
    assert raised.value.status_code is None
    assert seen == [
        f"https://api.github.com/repos/hcoona/three/contents/{path}?ref={commit}"
    ]
