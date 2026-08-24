"""Direct contracts for the Workflow Delivery v3 run-created-epoch helper."""

# ruff: noqa: D103

from __future__ import annotations

import errno
import importlib.util
import json
import socket
import ssl
import sys
import urllib.error
import urllib.parse
import urllib.request
from http.client import (
    BadStatusLine,
    HTTPException,
    HTTPMessage,
    IncompleteRead,
    RemoteDisconnected,
)
from io import BytesIO
from pathlib import Path
from typing import TYPE_CHECKING, cast

import pytest

if TYPE_CHECKING:
    from collections.abc import Callable, Mapping
    from types import ModuleType
    from typing import BinaryIO, Protocol, Self

    class _RedirectHandler(Protocol):
        redirect_request: Callable[..., None]


REPO_ROOT = Path(__file__).resolve().parents[6]
HELPER_PATH = (
    REPO_ROOT / "eng/scripts/workflow_delivery_v3_run_created_epoch.py"
)
MODULE_NAME = "_workflow_delivery_v3_run_created_epoch_under_test"
API_URL = "https://api.github.com"
REPOSITORY = "octo/example"
RUN_ID = 4242
TOKEN = "synthetic+/=._~wdv3-token"  # noqa: S105
AUTH_SCHEME = "Bearer"
RUN_URL = f"{API_URL}/repos/{REPOSITORY}/actions/runs/{RUN_ID}"
FIXED_NOW = 1_700_000_000.0
MAX_ATTEMPTS = 3
MAX_SLEEPS = 2
MAX_SLEEP_SECONDS = 30.0
REQUEST_TIMEOUT_SECONDS = 10.0
MAX_DIAGNOSTIC_CHARS = 512
MAX_AUTHORIZATION_CHARS = 4103
MAX_HEADER_DIAGNOSTIC_CHARS = 99
MAX_MESSAGE_DIAGNOSTIC_CHARS = 243
MAX_TRANSPORT_DIAGNOSTIC_CHARS = 128
MISSING = object()


def _load_helper() -> ModuleType:
    spec = importlib.util.spec_from_file_location(MODULE_NAME, HELPER_PATH)
    assert spec is not None
    assert spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


HELPER = _load_helper()


class _RecordingStream:
    """Return scripted data while recording every requested read size."""

    def __init__(self, payload: bytes | str) -> None:
        self._payload = payload
        self._offset = 0
        self.read_sizes: list[int] = []

    def read(self, size: int = -1) -> bytes | str:
        """Read at most ``size`` scripted units."""
        self.read_sizes.append(size)
        end = (
            len(self._payload)
            if size < 0
            else min(self._offset + size, len(self._payload))
        )
        chunk = self._payload[self._offset : end]
        self._offset = end
        return chunk

    def close(self) -> None:
        """Provide the file-like close operation expected by HTTPError."""


class _Response:
    """A context-managed fake urllib response."""

    def __init__(
        self,
        payload: bytes | str,
        *,
        status: object = 200,
        headers: Mapping[str, str] | None = None,
    ) -> None:
        self.stream = _RecordingStream(payload)
        self._status = status
        self.headers = HTTPMessage()
        for name, value in (headers or {}).items():
            self.headers[name] = value

    def __enter__(self) -> Self:
        """Enter the fake response context."""
        return self

    def __exit__(
        self,
        exception_type: object,
        exception: object,
        traceback: object,
    ) -> None:
        """Leave the fake response context."""
        del exception_type, exception, traceback
        self.stream.close()

    def getcode(self) -> object:
        """Return the scripted response status."""
        return self._status

    def read(self, size: int = -1) -> bytes | str:
        """Delegate bounded reads to the recording stream."""
        return self.stream.read(size)


class _FakeOpener:
    """Record requests and return or raise scripted outcomes."""

    def __init__(self, outcomes: list[_Response | BaseException]) -> None:
        self._outcomes = iter(outcomes)
        self.calls: list[tuple[urllib.request.Request, float]] = []

    def open(
        self,
        request: urllib.request.Request,
        *,
        timeout: float,
    ) -> _Response:
        """Record one bounded request and consume one outcome."""
        self.calls.append((request, timeout))
        try:
            outcome = next(self._outcomes)
        except StopIteration:
            pytest.fail("helper made an unexpected extra request")
        if isinstance(outcome, BaseException):
            raise outcome
        return outcome


def _set_valid_environment(
    monkeypatch: pytest.MonkeyPatch,
    *,
    token: str = TOKEN,
) -> None:
    monkeypatch.setenv("GITHUB_API_URL", API_URL)
    monkeypatch.setenv("GITHUB_REPOSITORY", REPOSITORY)
    monkeypatch.setenv("GITHUB_RUN_ID", str(RUN_ID))
    monkeypatch.setenv("WDV3_GITHUB_TOKEN", token)
    monkeypatch.setattr(HELPER.sys, "argv", [str(HELPER_PATH)])


def _install_transport(
    monkeypatch: pytest.MonkeyPatch,
    outcomes: list[_Response | BaseException],
) -> tuple[_FakeOpener, list[float], list[_RedirectHandler]]:
    opener = _FakeOpener(outcomes)
    sleeps: list[float] = []
    handlers: list[_RedirectHandler] = []

    def build_opener(*received_handlers: _RedirectHandler) -> _FakeOpener:
        handlers.extend(received_handlers)
        return opener

    monkeypatch.setattr(
        HELPER.urllib.request,
        "build_opener",
        build_opener,
    )
    monkeypatch.setattr(HELPER.time, "sleep", sleeps.append)
    monkeypatch.setattr(HELPER.time, "time", lambda: FIXED_NOW)
    return opener, sleeps, handlers


def _metadata_document(
    *,
    created_at: object = "2024-02-29T12:34:56Z",
) -> dict[str, object]:
    return {
        "id": RUN_ID,
        "repository": {"full_name": REPOSITORY},
        "created_at": created_at,
    }


def _metadata_body(
    document: object | None = None,
    *,
    created_at: object = "2024-02-29T12:34:56Z",
) -> bytes:
    value = (
        _metadata_document(created_at=created_at)
        if document is None
        else document
    )
    return json.dumps(value, separators=(",", ":")).encode()


def _headers(values: Mapping[str, str] | None = None) -> HTTPMessage:
    headers = HTTPMessage()
    for name, value in (values or {}).items():
        headers[name] = value
    return headers


def _http_error(
    status: int,
    *,
    headers: Mapping[str, str] | None = None,
    body: bytes = b"",
    reason: str = "scripted failure",
) -> tuple[urllib.error.HTTPError, _RecordingStream]:
    stream = _RecordingStream(body)
    error = urllib.error.HTTPError(
        RUN_URL,
        status,
        reason,
        _headers(headers),
        cast("BinaryIO", stream),
    )
    return error, stream


def _http_failures(
    status: int,
    *,
    count: int = 3,
    headers: Mapping[str, str] | None = None,
    body: bytes = b"",
) -> list[_Response | BaseException]:
    return [
        _http_error(status, headers=headers, body=body)[0] for _ in range(count)
    ]


def _transient_transport_error(kind: str) -> BaseException:
    factories = {
        "url-timeout": lambda: urllib.error.URLError(TimeoutError(TOKEN)),
        "timeout": lambda: TimeoutError(TOKEN),
        "connection-error": lambda: ConnectionError(TOKEN),
        "connection-aborted": lambda: ConnectionAbortedError(
            errno.ECONNABORTED,
            TOKEN,
        ),
        "connection-refused": lambda: ConnectionRefusedError(
            errno.ECONNREFUSED,
            TOKEN,
        ),
        "connection-reset": lambda: ConnectionResetError(
            errno.ECONNRESET,
            TOKEN,
        ),
        "host-unreachable": lambda: OSError(errno.EHOSTUNREACH, TOKEN),
        "network-down": lambda: OSError(errno.ENETDOWN, TOKEN),
        "network-unreachable": lambda: OSError(errno.ENETUNREACH, TOKEN),
        "timed-out-errno": lambda: OSError(errno.ETIMEDOUT, TOKEN),
        "temporary-dns": lambda: socket.gaierror(
            getattr(socket, "EAI_AGAIN", -3),
            TOKEN,
        ),
        "bad-status-line": lambda: BadStatusLine(TOKEN),
        "incomplete-read": lambda: IncompleteRead(b"", 1),
        "remote-disconnected": lambda: RemoteDisconnected(TOKEN),
    }
    try:
        return factories[kind]()
    except KeyError as error:
        raise AssertionError(kind) from error


def _run_main(
    capsys: pytest.CaptureFixture[str],
) -> tuple[int, str, str]:
    result = HELPER.main()
    captured = capsys.readouterr()
    return result, captured.out, captured.err


def _request_headers(request: urllib.request.Request) -> dict[str, str]:
    return {name.casefold(): value for name, value in request.header_items()}


def _diagnostic_json_value(line: str, name: str) -> str:
    marker = f"{name}="
    start = line.index(marker) + len(marker)
    value, _end = json.JSONDecoder().raw_decode(line[start:])
    assert isinstance(value, str)
    return value


def _secret_forms(token: str) -> set[str]:
    return {
        token,
        urllib.parse.quote(token, safe=""),
        urllib.parse.quote_plus(token),
    }


def test_main_emits_epoch_after_one_bounded_authenticated_get(
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
) -> None:
    _set_valid_environment(monkeypatch)
    opener, sleeps, handlers = _install_transport(
        monkeypatch,
        [_Response(_metadata_body())],
    )

    result, stdout, stderr = _run_main(capsys)

    assert (result, stdout, stderr) == (0, "1709210096\n", "")
    assert sleeps == []
    assert len(opener.calls) == 1
    request, timeout = opener.calls[0]
    assert (request.get_method(), request.full_url, timeout) == (
        "GET",
        RUN_URL,
        REQUEST_TIMEOUT_SECONDS,
    )
    assert urllib.parse.urlsplit(request.full_url).scheme == "https"
    assert _request_headers(request) == {
        "accept": "application/vnd.github+json",
        "authorization": f"{AUTH_SCHEME} {TOKEN}",
        "user-agent": "workflow-delivery-v3-run-metadata",
        "x-github-api-version": "2022-11-28",
    }
    assert len(handlers) == 1
    assert type(handlers[0]).__name__ == "_RejectRedirects"
    assert all(secret not in stdout + stderr for secret in _secret_forms(TOKEN))


@pytest.mark.parametrize(
    ("created_at", "expected_epoch"),
    [
        pytest.param("1970-01-01T00:00:00Z", 0, id="unix-epoch"),
        pytest.param(
            "2024-02-29T12:34:56Z",
            1_709_210_096,
            id="leap-day",
        ),
        pytest.param(
            "2024-02-29T12:34:56.999999Z",
            1_709_210_096,
            id="fractional-seconds",
        ),
    ],
)
def test_main_accepts_strict_utc_created_at(
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
    created_at: str,
    expected_epoch: int,
) -> None:
    _set_valid_environment(monkeypatch)
    opener, sleeps, _handlers = _install_transport(
        monkeypatch,
        [_Response(_metadata_body(created_at=created_at))],
    )

    result, stdout, stderr = _run_main(capsys)

    assert (result, stdout, stderr) == (
        0,
        f"{expected_epoch}\n",
        "",
    )
    assert len(opener.calls) == 1
    assert sleeps == []


def test_retry_classification_allowlists_are_exact() -> None:
    assert HELPER._ATTEMPTS == MAX_ATTEMPTS  # noqa: SLF001
    assert {
        int(status)
        for status in HELPER._RETRYABLE_HTTP_STATUSES  # noqa: SLF001
    } == {
        408,
        429,
        500,
        502,
        503,
        504,
    }
    assert (
        frozenset(
            {
                errno.ECONNABORTED,
                errno.ECONNREFUSED,
                errno.ECONNRESET,
                errno.EHOSTUNREACH,
                errno.ENETDOWN,
                errno.ENETUNREACH,
                errno.ETIMEDOUT,
            }
        )
        == HELPER._RETRYABLE_ERRNOS  # noqa: SLF001
    )


@pytest.mark.parametrize(
    "failure",
    [
        pytest.param("url-timeout", id="transient-transport"),
        pytest.param(408, id="request-timeout"),
        pytest.param(429, id="too-many-requests"),
        pytest.param(500, id="internal-server-error"),
        pytest.param(502, id="bad-gateway"),
        pytest.param(503, id="service-unavailable"),
        pytest.param(504, id="gateway-timeout"),
    ],
)
def test_main_retries_only_transient_failures(
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
    failure: int | str,
) -> None:
    _set_valid_environment(monkeypatch)
    if failure == "url-timeout":
        outcomes: list[_Response | BaseException] = [
            urllib.error.URLError(TimeoutError(TOKEN)) for _ in range(3)
        ]
    else:
        assert isinstance(failure, int)
        outcomes = _http_failures(failure)
    opener, sleeps, _handlers = _install_transport(monkeypatch, outcomes)

    result, stdout, stderr = _run_main(capsys)

    assert (result, stdout) == (1, "")
    assert len(opener.calls) == MAX_ATTEMPTS
    assert sleeps == [1.0, 2.0]
    lines = stderr.splitlines()
    assert len(lines) == MAX_ATTEMPTS
    assert "retries-exhausted" in lines[-1]
    assert "retry-in=" not in lines[-1]
    assert TOKEN not in stderr


@pytest.mark.parametrize(
    "failure",
    [
        pytest.param("url-timeout", id="wrapped-timeout"),
        pytest.param("timeout", id="timeout"),
        pytest.param("connection-error", id="connection-error"),
        pytest.param("connection-aborted", id="connection-aborted"),
        pytest.param("connection-refused", id="connection-refused"),
        pytest.param("connection-reset", id="connection-reset"),
        pytest.param("host-unreachable", id="host-unreachable"),
        pytest.param("network-down", id="network-down"),
        pytest.param("network-unreachable", id="network-unreachable"),
        pytest.param("timed-out-errno", id="timed-out-errno"),
        pytest.param("temporary-dns", id="temporary-dns"),
        pytest.param("bad-status-line", id="bad-status-line"),
        pytest.param("incomplete-read", id="incomplete-read"),
        pytest.param("remote-disconnected", id="remote-disconnected"),
    ],
)
def test_main_retries_each_supported_transient_transport_class(
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
    failure: str,
) -> None:
    _set_valid_environment(monkeypatch)
    opener, sleeps, _handlers = _install_transport(
        monkeypatch,
        [
            _transient_transport_error(failure)
            for _attempt in range(MAX_ATTEMPTS)
        ],
    )

    result, stdout, stderr = _run_main(capsys)

    assert (result, stdout) == (1, "")
    assert len(opener.calls) == MAX_ATTEMPTS
    assert sleeps == [1.0, 2.0]
    assert [call[1] for call in opener.calls] == [
        REQUEST_TIMEOUT_SECONDS
    ] * MAX_ATTEMPTS
    lines = stderr.splitlines()
    assert len(lines) == MAX_ATTEMPTS
    assert all("status=transport-error" in line for line in lines)
    assert all("retry-in=" in line for line in lines[:-1])
    assert "retries-exhausted" in lines[-1]
    assert "retry-in=" not in lines[-1]
    assert TOKEN not in stderr


def test_main_can_succeed_after_a_transient_failure(
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
) -> None:
    _set_valid_environment(monkeypatch)
    opener, sleeps, _handlers = _install_transport(
        monkeypatch,
        [
            ConnectionResetError(errno.ECONNRESET, TOKEN),
            _Response(_metadata_body()),
        ],
    )

    result, stdout, stderr = _run_main(capsys)

    assert (result, stdout) == (0, "1709210096\n")
    assert len(opener.calls) == MAX_SLEEPS
    assert sleeps == [1.0]
    assert stderr.count("\n") == 1
    assert "retry-in=1.000s" in stderr
    assert TOKEN not in stderr


@pytest.mark.parametrize(
    ("headers", "expected_attempts", "expected_sleeps"),
    [
        pytest.param(
            {"Retry-After": "0"},
            3,
            [0.0, 0.0],
            id="valid-retry-after",
        ),
        pytest.param(
            {"retry-after": "0"},
            3,
            [0.0, 0.0],
            id="lowercase-retry-after",
        ),
        pytest.param(
            {"X-RateLimit-Remaining": "0"},
            3,
            [1.0, 2.0],
            id="zero-remaining",
        ),
        pytest.param(
            {"x-ratelimit-remaining": "0"},
            3,
            [1.0, 2.0],
            id="lowercase-zero-remaining",
        ),
        pytest.param(
            {
                "X-RateLimit-Remaining": "000",
                "X-RateLimit-Reset": "1700000035",
            },
            3,
            [30.0, 30.0],
            id="zero-remaining-bounded-reset",
        ),
        pytest.param(
            {
                "X-RATELIMIT-REMAINING": "000",
                "x-RaTeLiMiT-ReSeT": "1700000035",
            },
            3,
            [30.0, 30.0],
            id="mixed-case-zero-remaining-bounded-reset",
        ),
        pytest.param({}, 1, [], id="missing-proof"),
        pytest.param(
            {"Retry-After": "later"},
            1,
            [],
            id="malformed-retry-after",
        ),
        pytest.param(
            {"X-RateLimit-Remaining": "zero"},
            1,
            [],
            id="malformed-remaining",
        ),
        pytest.param(
            {"X-RateLimit-Remaining": "1"},
            1,
            [],
            id="nonzero-remaining",
        ),
    ],
)
def test_main_retries_403_only_with_rate_limit_proof(
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
    headers: dict[str, str],
    expected_attempts: int,
    expected_sleeps: list[float],
) -> None:
    _set_valid_environment(monkeypatch)
    opener, sleeps, _handlers = _install_transport(
        monkeypatch,
        _http_failures(403, headers=headers),
    )

    result, stdout, stderr = _run_main(capsys)

    assert (result, stdout) == (1, "")
    assert len(opener.calls) == expected_attempts
    assert sleeps == expected_sleeps
    assert len(sleeps) <= MAX_SLEEPS
    assert all(0.0 <= delay <= MAX_SLEEP_SECONDS for delay in sleeps)
    assert len(stderr.splitlines()) == expected_attempts
    assert TOKEN not in stderr


@pytest.mark.parametrize(
    "outcome",
    [
        pytest.param(ssl.SSLError(TOKEN), id="tls"),
        pytest.param(
            ssl.SSLCertVerificationError(1, TOKEN),
            id="certificate",
        ),
        pytest.param(
            urllib.error.URLError(ssl.SSLError(TOKEN)),
            id="url-error-wrapped-tls",
        ),
        pytest.param(
            _http_error(401, reason=TOKEN)[0],
            id="authentication",
        ),
        pytest.param(
            _http_error(422, reason=TOKEN)[0],
            id="ordinary-4xx",
        ),
        pytest.param(
            _http_error(501, reason=TOKEN)[0],
            id="unsupported-5xx",
        ),
        pytest.param(
            _Response(_metadata_body(), status=None),
            id="invalid-status",
        ),
        pytest.param(TypeError(TOKEN), id="type-error"),
        pytest.param(ValueError(TOKEN), id="value-error"),
        pytest.param(
            HTTPException(TOKEN),
            id="unsupported-http-protocol-error",
        ),
        pytest.param(
            OSError(errno.EACCES, TOKEN),
            id="unsupported-os-error",
        ),
        pytest.param(
            urllib.error.URLError(TOKEN),
            id="url-error-with-nonexception-reason",
        ),
        pytest.param(
            socket.gaierror(
                getattr(socket, "EAI_NONAME", -2),
                TOKEN,
            ),
            id="permanent-dns",
        ),
    ],
)
def test_main_fails_closed_without_retry_for_terminal_errors(
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
    outcome: _Response | BaseException,
) -> None:
    _set_valid_environment(monkeypatch)
    opener, sleeps, _handlers = _install_transport(
        monkeypatch,
        [outcome],
    )

    result, stdout, stderr = _run_main(capsys)

    assert (result, stdout) == (1, "")
    assert len(opener.calls) == 1
    assert sleeps == []
    assert len(stderr.splitlines()) == 1
    assert 0 < len(stderr) <= MAX_DIAGNOSTIC_CHARS
    assert TOKEN not in stderr
    assert "not-retryable" in stderr


@pytest.mark.parametrize(
    "updates",
    [
        pytest.param({"GITHUB_API_URL": None}, id="missing-api-url"),
        pytest.param({"GITHUB_API_URL": ""}, id="empty-api-url"),
        pytest.param(
            {"GITHUB_REPOSITORY": None},
            id="missing-repository",
        ),
        pytest.param(
            {"GITHUB_REPOSITORY": ""},
            id="empty-repository",
        ),
        pytest.param({"GITHUB_RUN_ID": None}, id="missing-run-id"),
        pytest.param({"GITHUB_RUN_ID": ""}, id="empty-run-id"),
        pytest.param(
            {"WDV3_GITHUB_TOKEN": None},
            id="missing-token",
        ),
        pytest.param({"WDV3_GITHUB_TOKEN": ""}, id="empty-token"),
        pytest.param(
            {"GITHUB_REPOSITORY": "octo"},
            id="repository-without-owner",
        ),
        pytest.param(
            {"GITHUB_REPOSITORY": "octo/../example"},
            id="repository-extra-segment",
        ),
        pytest.param(
            {"GITHUB_REPOSITORY": "./example"},
            id="repository-dot-owner",
        ),
        pytest.param(
            {"GITHUB_REPOSITORY": "octo/.."},
            id="repository-dot-dot-name",
        ),
        pytest.param(
            {"GITHUB_REPOSITORY": "octo/example name"},
            id="repository-whitespace",
        ),
        pytest.param({"GITHUB_RUN_ID": "abc"}, id="nondecimal-run-id"),
        pytest.param(
            {"GITHUB_RUN_ID": "true"},
            id="boolean-like-run-id",
        ),
        pytest.param({"GITHUB_RUN_ID": "-1"}, id="negative-run-id"),
        pytest.param({"GITHUB_RUN_ID": " 1"}, id="spaced-run-id"),
        pytest.param({"GITHUB_RUN_ID": "0"}, id="nonpositive-run-id"),
        pytest.param({"GITHUB_RUN_ID": "01"}, id="leading-zero-run-id"),
        pytest.param(
            {"WDV3_GITHUB_TOKEN": "t" * 4097},
            id="overlong-token",
        ),
        pytest.param(
            {"WDV3_GITHUB_TOKEN": "token with space"},
            id="token-whitespace",
        ),
        pytest.param(
            {"WDV3_GITHUB_TOKEN": "token\twith-control"},
            id="token-control-character",
        ),
        pytest.param(
            {"WDV3_GITHUB_TOKEN": "tökén"},
            id="token-nonascii",
        ),
        pytest.param(
            {"GITHUB_API_URL": "http://api.github.com"},
            id="non-https-api",
        ),
        pytest.param(
            {"GITHUB_API_URL": "https:///api/v3"},
            id="api-missing-host",
        ),
        pytest.param(
            {"GITHUB_API_URL": "https://api.github.com?preview=1"},
            id="api-query",
        ),
        pytest.param(
            {"GITHUB_API_URL": "https://api.github.com#fragment"},
            id="api-fragment",
        ),
        pytest.param(
            {"GITHUB_API_URL": "https://user:pass@api.github.com"},
            id="api-userinfo",
        ),
        pytest.param(
            {"GITHUB_API_URL": "https://api.github.com/%2e%2e"},
            id="api-path-traversal",
        ),
        pytest.param(
            {"GITHUB_API_URL": "https://api.github.com/%2Fescape"},
            id="api-encoded-slash",
        ),
        pytest.param(
            {"GITHUB_API_URL": "https://api.github.com/%5Cescape"},
            id="api-encoded-backslash",
        ),
        pytest.param(
            {"GITHUB_API_URL": "https://api.github.com/%20"},
            id="api-encoded-whitespace",
        ),
        pytest.param(
            {"GITHUB_API_URL": "https://api.github.com/\n"},
            id="api-control-character",
        ),
        pytest.param(
            {"GITHUB_API_URL": "https://api.github.com:not-a-port"},
            id="malformed-api-port",
        ),
        pytest.param({"__argv__": "unexpected"}, id="extra-argument"),
    ],
)
def test_main_rejects_malformed_configuration_before_opening(
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
    updates: dict[str, str | None],
) -> None:
    _set_valid_environment(monkeypatch)
    for name, value in updates.items():
        if name == "__argv__":
            monkeypatch.setattr(
                HELPER.sys,
                "argv",
                [str(HELPER_PATH), str(value)],
            )
        elif value is None:
            monkeypatch.delenv(name, raising=False)
        else:
            monkeypatch.setenv(name, value)
    opener, sleeps, _handlers = _install_transport(monkeypatch, [])

    result, stdout, stderr = _run_main(capsys)

    assert (result, stdout) == (1, "")
    assert opener.calls == []
    assert sleeps == []
    assert len(stderr.splitlines()) == 1
    assert "attempt=0/3 status=configuration-error" in stderr
    assert 0 < len(stderr) <= MAX_DIAGNOSTIC_CHARS
    assert TOKEN not in stderr
    candidate_token = updates.get("WDV3_GITHUB_TOKEN")
    if candidate_token:
        assert candidate_token not in stderr


def test_main_accepts_exact_token_length_boundary(
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
) -> None:
    boundary_token = "t" * 4096
    _set_valid_environment(monkeypatch, token=boundary_token)
    opener, sleeps, _handlers = _install_transport(
        monkeypatch,
        [_Response(_metadata_body())],
    )

    result, stdout, stderr = _run_main(capsys)

    assert (result, stdout, stderr) == (0, "1709210096\n", "")
    assert len(opener.calls) == 1
    assert sleeps == []
    authorization = _request_headers(opener.calls[0][0])["authorization"]
    assert authorization == f"{AUTH_SCHEME} {boundary_token}"
    assert len(authorization) == MAX_AUTHORIZATION_CHARS


def test_main_preserves_a_safe_enterprise_api_path_prefix(
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
) -> None:
    api_url = "https://github.example/api/v3/"
    _set_valid_environment(monkeypatch)
    monkeypatch.setenv("GITHUB_API_URL", api_url)
    opener, sleeps, _handlers = _install_transport(
        monkeypatch,
        [_Response(_metadata_body())],
    )

    result, stdout, stderr = _run_main(capsys)

    assert (result, stdout, stderr) == (0, "1709210096\n", "")
    assert len(opener.calls) == 1
    request, timeout = opener.calls[0]
    assert request.full_url == (
        f"https://github.example/api/v3/repos/{REPOSITORY}/"
        f"actions/runs/{RUN_ID}"
    )
    assert timeout == REQUEST_TIMEOUT_SECONDS
    assert _request_headers(request)["authorization"] == (
        f"{AUTH_SCHEME} {TOKEN}"
    )
    assert sleeps == []


@pytest.mark.parametrize(
    "scenario",
    [
        pytest.param("success-at-limit", id="success-at-limit"),
        pytest.param("success-over-limit", id="success-over-limit"),
        pytest.param("http-error-over-limit", id="http-error-over-limit"),
    ],
)
def test_main_bounds_success_and_error_body_reads(
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
    scenario: str,
) -> None:
    _set_valid_environment(monkeypatch)
    if scenario == "http-error-over-limit":
        error, stream = _http_error(401, body=b"x" * 4097)
        outcome: _Response | BaseException = error
        expected_read_size = 4097
        expected_result = 1
        expected_stdout = ""
    else:
        base = _metadata_body()
        body_size = 1_048_576 + (scenario == "success-over-limit")
        body = base + (b" " * (body_size - len(base)))
        response = _Response(body)
        stream = response.stream
        outcome = response
        expected_read_size = 1_048_577
        expected_result = 0 if scenario == "success-at-limit" else 1
        expected_stdout = (
            "1709210096\n" if scenario == "success-at-limit" else ""
        )
    opener, sleeps, _handlers = _install_transport(
        monkeypatch,
        [outcome],
    )

    result, stdout, stderr = _run_main(capsys)

    assert (result, stdout) == (expected_result, expected_stdout)
    assert len(opener.calls) == 1
    assert sleeps == []
    assert stream.read_sizes == [expected_read_size]
    assert len(stderr) <= MAX_DIAGNOSTIC_CHARS
    if scenario == "success-at-limit":
        assert stderr == ""
    else:
        assert stderr


@pytest.mark.parametrize(
    ("retry_after", "expected_sleeps"),
    [
        pytest.param("0", [0.0, 0.0], id="zero"),
        pytest.param("30", [30.0, 30.0], id="at-cap"),
        pytest.param("31", [30.0, 30.0], id="above-cap"),
        pytest.param(
            "9999999999999",
            [30.0, 30.0],
            id="overlong-integer",
        ),
        pytest.param("-1", [1.0, 2.0], id="negative"),
        pytest.param("later", [1.0, 2.0], id="malformed"),
        pytest.param(None, [1.0, 2.0], id="absent"),
    ],
)
def test_main_bounds_retry_delays_and_never_sleeps_after_final_attempt(
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
    retry_after: str | None,
    expected_sleeps: list[float],
) -> None:
    _set_valid_environment(monkeypatch)
    headers = {} if retry_after is None else {"Retry-After": retry_after}
    opener, sleeps, _handlers = _install_transport(
        monkeypatch,
        _http_failures(429, headers=headers),
    )

    result, stdout, stderr = _run_main(capsys)

    assert (result, stdout) == (1, "")
    assert len(opener.calls) == MAX_ATTEMPTS
    assert sleeps == expected_sleeps
    assert len(sleeps) == MAX_SLEEPS
    assert all(0.0 <= delay <= MAX_SLEEP_SECONDS for delay in sleeps)
    lines = stderr.splitlines()
    assert len(lines) == MAX_ATTEMPTS
    assert "retries-exhausted" in lines[-1]
    assert "retry-in=" not in lines[-1]


@pytest.mark.parametrize(
    ("status", "headers", "expected_sleeps"),
    [
        pytest.param(
            429,
            {"Retry-After": "Tue, 14 Nov 2023 22:13:55 GMT"},
            [30.0, 30.0],
            id="retry-after-future-http-date-capped",
        ),
        pytest.param(
            429,
            {"Retry-After": "Tue, 14 Nov 2023 22:13:10 GMT"},
            [0.0, 0.0],
            id="retry-after-past-http-date-floored",
        ),
        pytest.param(
            429,
            {"Retry-After": "Tue, 14 Nov 2023 22:13:55"},
            [1.0, 2.0],
            id="retry-after-naive-http-date-rejected",
        ),
        pytest.param(
            403,
            {"Retry-After": "Tue, 14 Nov 2023 22:13:55 GMT"},
            [30.0, 30.0],
            id="forbidden-http-date-is-rate-limit-proof",
        ),
        pytest.param(
            403,
            {
                "X-RateLimit-Remaining": "0",
                "X-RateLimit-Reset": "1699999990",
            },
            [0.0, 0.0],
            id="rate-limit-reset-past-is-floored",
        ),
        pytest.param(
            403,
            {
                "X-RateLimit-Remaining": "0",
                "X-RateLimit-Reset": "not-an-epoch",
            },
            [1.0, 2.0],
            id="malformed-rate-limit-reset-uses-backoff",
        ),
    ],
)
def test_main_bounds_standard_retry_time_headers(
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
    status: int,
    headers: dict[str, str],
    expected_sleeps: list[float],
) -> None:
    _set_valid_environment(monkeypatch)
    opener, sleeps, _handlers = _install_transport(
        monkeypatch,
        _http_failures(status, headers=headers),
    )

    result, stdout, stderr = _run_main(capsys)

    assert (result, stdout) == (1, "")
    assert len(opener.calls) == MAX_ATTEMPTS
    assert sleeps == expected_sleeps
    assert all(0.0 <= delay <= MAX_SLEEP_SECONDS for delay in sleeps)
    lines = stderr.splitlines()
    assert len(lines) == MAX_ATTEMPTS
    assert all("retry-in=" in line for line in lines[:-1])
    assert "retries-exhausted" in lines[-1]
    assert "retry-in=" not in lines[-1]
    assert TOKEN not in stderr


def test_main_truncates_and_redacts_all_secret_forms(
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
) -> None:
    _set_valid_environment(monkeypatch)
    encoded_forms = sorted(_secret_forms(TOKEN))
    bearer_shape = f"{AUTH_SCHEME} unrelated-sensitive-value"
    authorization_shape = "Authorization: unrelated-sensitive-value"
    sensitive_text = (
        "diagnostic "
        + " | ".join(
            [
                *encoded_forms,
                bearer_shape,
                authorization_shape,
            ]
        )
        + " "
        + ("x" * 400)
    )
    reason = f"reason-only-marker {TOKEN}"
    error, _stream = _http_error(
        401,
        headers={
            "Retry-After": sensitive_text,
            "X-RateLimit-Remaining": sensitive_text,
            "X-RateLimit-Reset": sensitive_text,
            "X-Unselected-Sensitive": "unselected-header-marker",
        },
        body=json.dumps({"message": sensitive_text}).encode(),
        reason=reason,
    )
    opener, sleeps, _handlers = _install_transport(
        monkeypatch,
        [error],
    )

    result, stdout, stderr = _run_main(capsys)

    assert (result, stdout) == (1, "")
    assert len(opener.calls) == 1
    assert sleeps == []
    for secret in {
        *encoded_forms,
        "unrelated-sensitive-value",
        "reason-only-marker",
        "unselected-header-marker",
    }:
        assert secret not in stdout + stderr
    assert AUTH_SCHEME.casefold() not in stderr.casefold()
    assert "authorization" not in stderr.casefold()
    assert "[redacted]" in stderr
    assert "[redacted-header]" in stderr
    for name in (
        "Retry-After",
        "X-RateLimit-Remaining",
        "X-RateLimit-Reset",
    ):
        value = _diagnostic_json_value(stderr, name)
        assert len(value) <= MAX_HEADER_DIAGNOSTIC_CHARS
        assert value.endswith("...")
    summary = _diagnostic_json_value(stderr, "message")
    assert len(summary) <= MAX_MESSAGE_DIAGNOSTIC_CHARS
    assert summary.endswith("...")

    transport_message = (
        "transport-only-marker "
        + " ".join(encoded_forms)
        + f" {bearer_shape} {authorization_shape}"
    )
    transport, transport_sleeps, _transport_handlers = _install_transport(
        monkeypatch,
        [OSError(errno.EACCES, transport_message)],
    )
    result, stdout, stderr = _run_main(capsys)

    assert (result, stdout) == (1, "")
    assert len(transport.calls) == 1
    assert transport_sleeps == []
    assert "transport-only-marker" not in stderr
    assert all(secret not in stderr for secret in encoded_forms)
    assert len(stderr) <= MAX_TRANSPORT_DIAGNOSTIC_CHARS


@pytest.mark.parametrize(
    "status",
    [
        pytest.param(301, id="moved-permanently"),
        pytest.param(302, id="found"),
        pytest.param(303, id="see-other"),
        pytest.param(307, id="temporary-redirect"),
        pytest.param(308, id="permanent-redirect"),
    ],
)
def test_main_does_not_follow_redirects_or_reuse_credentials(
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
    status: int,
) -> None:
    _set_valid_environment(monkeypatch)
    attacker_url = "https://attacker.example.invalid/collect"
    error, _stream = _http_error(
        status,
        headers={"Location": attacker_url},
    )
    original_request = HELPER.urllib.request.Request
    constructed: list[urllib.request.Request] = []

    def recording_request(
        url: str,
        *args: object,
        **kwargs: object,
    ) -> urllib.request.Request:
        request = original_request(url, *args, **kwargs)
        constructed.append(request)
        return request

    monkeypatch.setattr(
        HELPER.urllib.request,
        "Request",
        recording_request,
    )
    opener, sleeps, handlers = _install_transport(monkeypatch, [error])

    result, stdout, stderr = _run_main(capsys)

    assert (result, stdout) == (1, "")
    assert len(opener.calls) == 1
    assert sleeps == []
    assert [request.full_url for request in constructed] == [RUN_URL]
    assert all(call[0].full_url != attacker_url for call in opener.calls)
    assert len(handlers) == 1
    assert (
        handlers[0].redirect_request(
            opener.calls[0][0],
            BytesIO(),
            status,
            "redirect",
            _headers({"Location": attacker_url}),
            attacker_url,
        )
        is None
    )
    assert _request_headers(opener.calls[0][0])["authorization"] == (
        f"{AUTH_SCHEME} {TOKEN}"
    )
    assert attacker_url not in stderr
    assert TOKEN not in stderr


@pytest.mark.parametrize(
    ("field", "value", "diagnostic"),
    [
        pytest.param(
            "id",
            True,
            "mismatched run identity",
            id="boolean-id",
        ),
        pytest.param(
            "id",
            str(RUN_ID),
            "mismatched run identity",
            id="numeric-string-id",
        ),
        pytest.param(
            "id",
            float(RUN_ID),
            "mismatched run identity",
            id="float-id",
        ),
        pytest.param(
            "id",
            MISSING,
            "mismatched run identity",
            id="missing-id",
        ),
        pytest.param(
            "id",
            RUN_ID + 1,
            "mismatched run identity",
            id="different-id",
        ),
        pytest.param(
            "repository",
            MISSING,
            "mismatched repository identity",
            id="missing-repository",
        ),
        pytest.param(
            "repository",
            [],
            "mismatched repository identity",
            id="non-object-repository",
        ),
        pytest.param(
            "repository.full_name",
            MISSING,
            "mismatched repository identity",
            id="missing-full-name",
        ),
        pytest.param(
            "repository.full_name",
            RUN_ID,
            "mismatched repository identity",
            id="non-string-full-name",
        ),
        pytest.param(
            "repository.full_name",
            "Octo/example",
            "mismatched repository identity",
            id="case-mismatched-full-name",
        ),
    ],
)
def test_main_requires_exact_run_and_repository_identity(
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
    field: str,
    value: object,
    diagnostic: str,
) -> None:
    _set_valid_environment(monkeypatch)
    document = _metadata_document()
    if field == "id":
        if value is MISSING:
            del document["id"]
        else:
            document["id"] = value
    elif field == "repository":
        if value is MISSING:
            del document["repository"]
        else:
            document["repository"] = value
    else:
        repository = document["repository"]
        assert isinstance(repository, dict)
        if value is MISSING:
            del repository["full_name"]
        else:
            repository["full_name"] = value
    opener, sleeps, _handlers = _install_transport(
        monkeypatch,
        [_Response(_metadata_body(document))],
    )

    result, stdout, stderr = _run_main(capsys)

    assert (result, stdout) == (1, "")
    assert len(opener.calls) == 1
    assert sleeps == []
    assert diagnostic in stderr
    assert len(stderr.splitlines()) == 1


@pytest.mark.parametrize(
    ("payload", "status", "diagnostic"),
    [
        pytest.param(
            b"{",
            200,
            "malformed run metadata JSON",
            id="malformed-json",
        ),
        pytest.param(
            (
                b'{"id":'
                + (b"9" * 5_000)
                + b',"repository":{"full_name":"octo/example"},'
                b'"created_at":"2024-02-29T12:34:56Z"}'
            ),
            200,
            "malformed run metadata JSON",
            id="oversized-json-integer",
        ),
        pytest.param(
            (b"[" * 10_000) + b"0" + (b"]" * 10_000),
            200,
            "malformed run metadata JSON",
            id="excessive-json-nesting",
        ),
        pytest.param(
            _metadata_body([]),
            200,
            "run metadata is not an object",
            id="array-json",
        ),
        pytest.param(
            _metadata_body("metadata"),
            200,
            "run metadata is not an object",
            id="string-json",
        ),
        pytest.param(
            _metadata_body().decode(),
            200,
            "detail=MetadataError",
            id="non-byte-body",
        ),
        pytest.param(
            _metadata_body(),
            None,
            "status=invalid-response",
            id="missing-status",
        ),
        pytest.param(
            _metadata_body(),
            "200",
            "status=invalid-response",
            id="string-status",
        ),
    ],
)
def test_main_rejects_invalid_response_schema(
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
    payload: bytes | str,
    status: object,
    diagnostic: str,
) -> None:
    _set_valid_environment(monkeypatch)
    opener, sleeps, _handlers = _install_transport(
        monkeypatch,
        [_Response(payload, status=status)],
    )

    result, stdout, stderr = _run_main(capsys)

    assert (result, stdout) == (1, "")
    assert len(opener.calls) == 1
    assert sleeps == []
    assert diagnostic in stderr
    assert len(stderr.splitlines()) == 1


@pytest.mark.parametrize(
    ("created_at", "diagnostic"),
    [
        pytest.param(
            "2024-02-29T12:34:56+00:00",
            "missing or malformed created_at",
            id="utc-offset",
        ),
        pytest.param(
            "2024-02-29T12:34:56z",
            "missing or malformed created_at",
            id="lowercase-z",
        ),
        pytest.param(
            "2024-02-29T12:34:56",
            "missing or malformed created_at",
            id="missing-z",
        ),
        pytest.param(
            " 2024-02-29T12:34:56Z",
            "missing or malformed created_at",
            id="leading-space",
        ),
        pytest.param(
            "2024-02-29T12:34:56Z ",
            "missing or malformed created_at",
            id="trailing-space",
        ),
        pytest.param(
            "2024-02-29T12:34:56.1234567Z",
            "missing or malformed created_at",
            id="excess-fractional-precision",
        ),
        pytest.param(
            "2024-02-30T12:34:56Z",
            "invalid created_at",
            id="impossible-date",
        ),
        pytest.param(
            None,
            "missing or malformed created_at",
            id="null",
        ),
        pytest.param(
            1_709_210_096,
            "missing or malformed created_at",
            id="non-string",
        ),
    ],
)
def test_main_rejects_noncanonical_or_invalid_created_at(
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
    created_at: object,
    diagnostic: str,
) -> None:
    _set_valid_environment(monkeypatch)
    opener, sleeps, _handlers = _install_transport(
        monkeypatch,
        [_Response(_metadata_body(created_at=created_at))],
    )

    result, stdout, stderr = _run_main(capsys)

    assert (result, stdout) == (1, "")
    assert len(opener.calls) == 1
    assert sleeps == []
    assert diagnostic in stderr
    assert len(stderr.splitlines()) == 1
