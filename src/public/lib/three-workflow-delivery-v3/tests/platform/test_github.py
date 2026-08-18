"""Focused tests for the concrete GitHub REST platform client."""

# ruff: noqa: D103

from __future__ import annotations

import http.client
import json
import urllib.error
import urllib.parse
import urllib.request
from http.client import HTTPMessage
from io import BytesIO

import pytest
from three_workflow_delivery_v3.platform.github import (
    GitHubRestClient,
    GitHubRestError,
    _NoRedirect,
    iter_all_runs,
)

TOKEN = "real-token-sent-to-fake-transport"  # noqa: S105
TOTAL_RUNS = 201
FINAL_PAGE = 3
DEFAULT_TIMEOUT = 20


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


_ARTIFACT_API_URL = (
    "https://api.github.com/repos/octo/example/actions/artifacts/17/zip"
)
_BLOB_URL = (
    "https://productionresultssa1.blob.core.windows.net/"
    "actions-results/signed/archive.zip?sig=temporary-signature"
)
_FOLLOWED_REQUEST_COUNT = 2
_FOUND_STATUS = 302


class _ArtifactArchiveOpener:
    """Record a deterministic sequence of artifact transport outcomes."""

    def __init__(self, outcomes: list[bytes | Exception]) -> None:
        """Initialize the scripted transport."""
        self._outcomes = iter(outcomes)
        self.calls: list[tuple[str, str, tuple[tuple[str, str], ...], int]] = []

    def open(self, request, *, timeout: int) -> BytesIO:
        """Record one request and return or raise its scripted outcome."""
        self.calls.append(
            (
                request.get_method(),
                request.full_url,
                tuple(request.header_items()),
                timeout,
            )
        )
        try:
            outcome = next(self._outcomes)
        except StopIteration:
            pytest.fail("artifact transport received an unexpected request")
        if isinstance(outcome, Exception):
            raise outcome
        return BytesIO(outcome)


def _artifact_http_error(
    status: int,
    *,
    request_url: str,
    location: str | None = None,
) -> urllib.error.HTTPError:
    headers = HTTPMessage()
    if location is not None:
        headers["Location"] = location
    return urllib.error.HTTPError(
        request_url,
        status,
        "request failed",
        headers,
        BytesIO(),
    )


def _zip_payload(files: tuple[tuple[str, bytes], ...]) -> bytes:
    payload = BytesIO()
    with __import__("zipfile").ZipFile(payload, "w") as archive:
        for name, content in files:
            archive.writestr(name, content)
    return payload.getvalue()


def _client_with_artifact_transport(
    monkeypatch: pytest.MonkeyPatch,
    transport: _ArtifactArchiveOpener,
    *,
    workflow_path: str | None = None,
) -> GitHubRestClient:
    def build_opener(*handlers: object) -> _ArtifactArchiveOpener:
        assert len(handlers) == 1
        handler = handlers[0]
        assert isinstance(handler, type)
        assert issubclass(handler, urllib.request.HTTPRedirectHandler)
        redirect_handler = handler()
        assert (
            redirect_handler.redirect_request(
                urllib.request.Request(_ARTIFACT_API_URL),  # noqa: S310
                BytesIO(),
                _FOUND_STATUS,
                "Found",
                HTTPMessage(),
                _BLOB_URL,
            )
            is None
        )
        return transport

    monkeypatch.setattr(
        urllib.request,
        "build_opener",
        build_opener,
    )
    return GitHubRestClient(
        repository="octo/example",
        token=TOKEN,
        workflow_path=workflow_path,
        timeout=7,
    )


def _headers(
    call: tuple[str, str, tuple[tuple[str, str], ...], int],
) -> dict[str, str]:
    return {name.casefold(): value for name, value in call[2]}


def _assert_initial_artifact_request(
    call: tuple[str, str, tuple[tuple[str, str], ...], int],
) -> None:
    method, url, _raw_headers, timeout = call
    headers = _headers(call)
    assert (method, url, timeout) == ("GET", _ARTIFACT_API_URL, 7)
    assert headers == {
        "accept": "application/vnd.github+json",
        "authorization": f"Bearer {TOKEN}",
        "user-agent": "three-workflow-delivery-v3",
        "x-github-api-version": "2022-11-28",
    }


def _assert_credential_free_blob_request(
    call: tuple[str, str, tuple[tuple[str, str], ...], int],
) -> None:
    method, url, raw_headers, timeout = call
    assert (method, url, timeout) == ("GET", _BLOB_URL, 7)
    assert _headers(call).get("authorization") is None
    assert raw_headers == ()
    assert all(TOKEN not in part for header in raw_headers for part in header)


def test_download_artifact_follows_one_off_origin_https_302_without_credentials(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    expected = b'{"runs":[]}'
    transport = _ArtifactArchiveOpener(
        [
            _artifact_http_error(
                302,
                request_url=_ARTIFACT_API_URL,
                location=_BLOB_URL,
            ),
            _zip_payload((("history.json", expected),)),
        ]
    )
    client = _client_with_artifact_transport(monkeypatch, transport)

    actual = client.download_artifact(17)

    assert actual == expected
    assert len(transport.calls) == _FOLLOWED_REQUEST_COUNT
    _assert_initial_artifact_request(transport.calls[0])
    _assert_credential_free_blob_request(transport.calls[1])
    assert TOKEN not in transport.calls[1][1]


def test_download_artifact_rejects_initial_success_without_redirect(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    transport = _ArtifactArchiveOpener(
        [_zip_payload((("history.json", b'{"runs":[]}'),))]
    )
    client = _client_with_artifact_transport(monkeypatch, transport)

    with pytest.raises(
        GitHubRestError,
        match="artifact archive response did not redirect",
    ):
        client.download_artifact(17)

    assert len(transport.calls) == 1
    _assert_initial_artifact_request(transport.calls[0])


def test_no_redirect_handler_surfaces_malformed_location_without_parsing() -> (
    None
):
    headers = HTTPMessage()
    headers["Location"] = "https://[::1/archive.zip"
    handler = _NoRedirect()
    request = urllib.request.Request(_ARTIFACT_API_URL)  # noqa: S310

    with pytest.raises(urllib.error.HTTPError) as raised:
        handler.http_error_302(
            request,
            BytesIO(),
            _FOUND_STATUS,
            "Found",
            headers,
        )

    assert raised.value.code == _FOUND_STATUS
    assert raised.value.headers["Location"] == "https://[::1/archive.zip"


@pytest.mark.parametrize(
    "location",
    [
        pytest.param(
            "http://objects.example.invalid/archive.zip",
            id="plain-http",
        ),
        pytest.param(
            "ftp://objects.example.invalid/archive.zip",
            id="ftp",
        ),
        pytest.param(
            "//objects.example.invalid/archive.zip",
            id="scheme-relative",
        ),
        pytest.param("/temporary/archive.zip", id="relative"),
        pytest.param(
            "https://api.github.com/temporary/archive.zip",
            id="same-origin",
        ),
        pytest.param(
            "https://api.github.com./temporary/archive.zip",
            id="same-origin-trailing-dot",
        ),
        pytest.param(
            "https://redirect-user:redirect-password@"
            "objects.example.invalid/archive.zip",
            id="userinfo",
        ),
        pytest.param(
            "https://objects.example.invalid/archive.zip#fragment",
            id="fragment",
        ),
        pytest.param(
            "https://objects.example.invalid:not-a-port/archive.zip",
            id="malformed-port",
        ),
        pytest.param(
            "https://objects.example.invalid:8443/archive.zip",
            id="nonstandard-port",
        ),
        pytest.param(
            "https://%61pi.github.com/archive.zip",
            id="encoded-api-host",
        ),
        pytest.param(
            "https://objects.example.invalid%3a8443/archive.zip",
            id="encoded-port",
        ),
        pytest.param(
            "https://user%3Apass%40objects.example.invalid/archive.zip",
            id="encoded-userinfo",
        ),
        pytest.param("https://./archive.zip", id="empty-host-label"),
        pytest.param(
            "https://objects..example.invalid/archive.zip",
            id="empty-middle-host-label",
        ),
        pytest.param("https://localhost/archive.zip", id="localhost"),
        pytest.param("https://127.0.0.1/archive.zip", id="ipv4-loopback"),
        pytest.param("https://127.1/archive.zip", id="legacy-short-ipv4"),
        pytest.param(
            "https://2130706433/archive.zip", id="legacy-decimal-ipv4"
        ),
        pytest.param(
            "https://0x7f.0.0.1/archive.zip",
            id="legacy-hex-ipv4",
        ),
        pytest.param(
            "https://productionresultssa.blob.core.windows.net/archive.zip",
            id="missing-storage-shard",
        ),
        pytest.param(
            "https://otherstorage1.blob.core.windows.net/archive.zip",
            id="unrelated-azure-storage",
        ),
        pytest.param(
            "https://[::1/archive.zip",
            id="malformed-ipv6",
        ),
        pytest.param(
            " https://objects.example.invalid/archive.zip",
            id="leading-space",
        ),
        pytest.param(
            "https://objects.example.invalid/archive.zip\n",
            id="trailing-control",
        ),
        pytest.param(
            "https://objects.example.invalid/archive.zip\x80",
            id="non-ascii-control",
        ),
    ],
)
def test_download_artifact_rejects_unsafe_or_non_off_origin_location_before_follow_up(  # noqa: E501
    monkeypatch: pytest.MonkeyPatch,
    location: str,
) -> None:
    transport = _ArtifactArchiveOpener(
        [
            _artifact_http_error(
                302,
                request_url=_ARTIFACT_API_URL,
                location=location,
            )
        ]
    )
    client = _client_with_artifact_transport(monkeypatch, transport)

    with pytest.raises(
        GitHubRestError,
        match="artifact archive redirect target",
    ):
        client.download_artifact(17)

    assert len(transport.calls) == 1
    _assert_initial_artifact_request(transport.calls[0])


def test_list_runs_does_not_use_the_artifact_redirect_exception(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    workflow_path = ".github/workflows/reusable.yml"
    transport = _ArtifactArchiveOpener(
        [
            _artifact_http_error(
                302,
                request_url=(
                    "https://api.github.com/repos/octo/example/actions/"
                    "workflows/.github%2Fworkflows%2Freusable.yml/runs"
                    "?per_page=100&page=1"
                ),
                location=_BLOB_URL,
            )
        ]
    )
    client = _client_with_artifact_transport(
        monkeypatch,
        transport,
        workflow_path=workflow_path,
    )

    with pytest.raises(GitHubRestError, match=r"api\.github\.com"):
        client.list_runs(None)

    assert len(transport.calls) == 1
    method, url, _headers_seen, timeout = transport.calls[0]
    assert (method, url, timeout) == (
        "GET",
        "https://api.github.com/repos/octo/example/actions/workflows/"
        ".github%2Fworkflows%2Freusable.yml/runs?per_page=100&page=1",
        7,
    )
    assert _headers(transport.calls[0])["authorization"] == (f"Bearer {TOKEN}")


@pytest.mark.parametrize("status", [301, 303, 307, 308])
def test_download_artifact_rejects_non_302_initial_redirect(
    monkeypatch: pytest.MonkeyPatch,
    status: int,
) -> None:
    transport = _ArtifactArchiveOpener(
        [
            _artifact_http_error(
                status,
                request_url=_ARTIFACT_API_URL,
                location=_BLOB_URL,
            )
        ]
    )
    client = _client_with_artifact_transport(monkeypatch, transport)

    with pytest.raises(
        GitHubRestError,
        match="artifact archive redirect must use HTTP 302",
    ) as raised:
        client.download_artifact(17)

    assert raised.value.status_code == status
    assert len(transport.calls) == 1
    _assert_initial_artifact_request(transport.calls[0])


@pytest.mark.parametrize(
    ("status", "second_location"),
    [
        pytest.param(
            302,
            "https://api.github.com/temporary/archive.zip",
            id="back-to-api",
        ),
        pytest.param(302, _BLOB_URL, id="same-blob-cycle"),
        pytest.param(
            302,
            "https://other-objects.example.invalid/archive.zip",
            id="another-blob",
        ),
        pytest.param(
            307,
            "https://other-objects.example.invalid/archive.zip",
            id="non-302",
        ),
    ],
)
def test_download_artifact_rejects_any_redirect_from_the_blob_without_a_third_request(  # noqa: E501
    monkeypatch: pytest.MonkeyPatch,
    status: int,
    second_location: str,
) -> None:
    transport = _ArtifactArchiveOpener(
        [
            _artifact_http_error(
                302,
                request_url=_ARTIFACT_API_URL,
                location=_BLOB_URL,
            ),
            _artifact_http_error(
                status,
                request_url=_BLOB_URL,
                location=second_location,
            ),
        ]
    )
    client = _client_with_artifact_transport(monkeypatch, transport)

    with pytest.raises(
        GitHubRestError,
        match="artifact archive redirect limit exceeded",
    ) as raised:
        client.download_artifact(17)

    assert raised.value.status_code == status
    assert len(transport.calls) == _FOLLOWED_REQUEST_COUNT
    _assert_initial_artifact_request(transport.calls[0])
    _assert_credential_free_blob_request(transport.calls[1])


def test_download_artifact_rejects_302_without_location(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    transport = _ArtifactArchiveOpener(
        [
            _artifact_http_error(
                302,
                request_url=_ARTIFACT_API_URL,
            )
        ]
    )
    client = _client_with_artifact_transport(monkeypatch, transport)

    with pytest.raises(
        GitHubRestError,
        match="artifact archive redirect location is missing",
    ) as raised:
        client.download_artifact(17)

    assert raised.value.status_code == _FOUND_STATUS
    assert len(transport.calls) == 1
    _assert_initial_artifact_request(transport.calls[0])


@pytest.mark.parametrize(
    ("stage", "failure_kind", "status", "message"),
    [
        pytest.param(
            "api",
            "http",
            503,
            "HTTP Error 503: request failed",
            id="api-http",
        ),
        pytest.param(
            "blob",
            "http",
            404,
            "HTTP Error 404: request failed",
            id="blob-http",
        ),
        pytest.param(
            "api",
            "timeout",
            None,
            "initial request timed out",
            id="api-timeout",
        ),
        pytest.param(
            "blob",
            "network",
            None,
            "blob network failed",
            id="blob-network",
        ),
    ],
)
def test_download_artifact_redirect_preserves_http_and_network_errors(
    monkeypatch: pytest.MonkeyPatch,
    stage: str,
    failure_kind: str,
    status: int | None,
    message: str,
) -> None:
    if failure_kind == "http":
        failure: Exception = _artifact_http_error(
            status if status is not None else 500,
            request_url=(_ARTIFACT_API_URL if stage == "api" else _BLOB_URL),
        )
    elif failure_kind == "timeout":
        failure = TimeoutError(message)
    else:
        failure = OSError(message)
    outcomes: list[bytes | Exception] = [failure]
    if stage == "blob":
        outcomes.insert(
            0,
            _artifact_http_error(
                302,
                request_url=_ARTIFACT_API_URL,
                location=_BLOB_URL,
            ),
        )
    transport = _ArtifactArchiveOpener(outcomes)
    client = _client_with_artifact_transport(monkeypatch, transport)

    with pytest.raises(GitHubRestError) as raised:
        client.download_artifact(17)

    assert str(raised.value) == message
    assert raised.value.status_code == status
    assert len(transport.calls) == (1 if stage == "api" else 2)
    _assert_initial_artifact_request(transport.calls[0])
    if stage == "blob":
        _assert_credential_free_blob_request(transport.calls[1])


def test_download_artifact_normalizes_invalid_followup_request_error(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    transport = _ArtifactArchiveOpener(
        [
            _artifact_http_error(
                302,
                request_url=_ARTIFACT_API_URL,
                location=_BLOB_URL,
            ),
            http.client.InvalidURL("invalid follow-up URL"),
        ]
    )
    client = _client_with_artifact_transport(monkeypatch, transport)

    with pytest.raises(
        GitHubRestError,
        match="artifact archive redirect target is unsafe",
    ) as raised:
        client.download_artifact(17)

    assert raised.value.status_code is None
    assert len(transport.calls) == _FOLLOWED_REQUEST_COUNT
    _assert_initial_artifact_request(transport.calls[0])
    _assert_credential_free_blob_request(transport.calls[1])


@pytest.mark.parametrize(
    ("payload", "message"),
    [
        pytest.param(
            b"PK-not-a-zip",
            "history artifact ZIP is malformed",
            id="malformed",
        ),
        pytest.param(
            _zip_payload(()),
            "history artifact must contain exactly one file",
            id="empty",
        ),
        pytest.param(
            _zip_payload(
                (
                    ("history.json", b'{"runs":[]}'),
                    ("extra.json", b"{}"),
                )
            ),
            "history artifact must contain exactly one file",
            id="multiple-files",
        ),
    ],
)
def test_download_artifact_redirect_preserves_archive_validation(
    monkeypatch: pytest.MonkeyPatch,
    payload: bytes,
    message: str,
) -> None:
    transport = _ArtifactArchiveOpener(
        [
            _artifact_http_error(
                302,
                request_url=_ARTIFACT_API_URL,
                location=_BLOB_URL,
            ),
            payload,
        ]
    )
    client = _client_with_artifact_transport(monkeypatch, transport)

    with pytest.raises(GitHubRestError) as raised:
        client.download_artifact(17)

    assert str(raised.value) == message
    assert raised.value.status_code is None
    assert len(transport.calls) == _FOLLOWED_REQUEST_COUNT
    _assert_initial_artifact_request(transport.calls[0])
    _assert_credential_free_blob_request(transport.calls[1])
