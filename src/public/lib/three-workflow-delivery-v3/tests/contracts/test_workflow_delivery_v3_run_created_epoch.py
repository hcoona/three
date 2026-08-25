"""Contracts for the Workflow Delivery v3 run-created-epoch helper."""

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
from http.client import HTTPMessage, RemoteDisconnected
from pathlib import Path
from typing import TYPE_CHECKING, cast

import pytest

if TYPE_CHECKING:
    from collections.abc import Mapping, Sequence
    from types import ModuleType
    from typing import BinaryIO, Self

REPO_ROOT = Path(__file__).resolve().parents[6]
HELPER_PATH = (
    REPO_ROOT / "eng/scripts/workflow_delivery_v3_run_created_epoch.py"
)
MODULE_NAME = "_workflow_delivery_v3_run_created_epoch_under_test"
API_URL = "https://api.github.com"
REPOSITORY = "octo/example"
RUN_ID = 4242
TOKEN = "synthetic+/=._~wdv3-token"  # noqa: S105
RUN_URL = f"{API_URL}/repos/{REPOSITORY}/actions/runs/{RUN_ID}"
FIXED_NOW = 1_700_000_000.0
EXPECTED_EPOCH = 1_709_210_096
MAX_ATTEMPTS = 3
REQUEST_TIMEOUT_SECONDS = 10.0
MAX_RESPONSE_BYTES = 1024 * 1024
MAX_ERROR_BYTES = 4096
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
    def __init__(self, payload: bytes) -> None:
        self._payload = payload
        self._offset = 0
        self.read_sizes: list[int] = []

    def read(self, size: int = -1) -> bytes:
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
        pass


class _Response:
    def __init__(
        self,
        payload: bytes,
        *,
        status: object = 200,
        headers: Mapping[str, str] | None = None,
    ) -> None:
        self.stream = _RecordingStream(payload)
        self._status = status
        self.headers = _headers(headers)

    def __enter__(self) -> Self:
        return self

    def __exit__(
        self,
        exception_type: object,
        exception: object,
        traceback: object,
    ) -> None:
        del exception_type, exception, traceback
        self.stream.close()

    def getcode(self) -> object:
        return self._status

    def read(self, size: int = -1) -> bytes:
        return self.stream.read(size)


class _FakeOpener:
    def __init__(self, outcomes: Sequence[_Response | BaseException]) -> None:
        self._outcomes = iter(outcomes)
        self.calls: list[tuple[urllib.request.Request, float]] = []

    def open(
        self,
        request: urllib.request.Request,
        *,
        timeout: float,
    ) -> _Response:
        self.calls.append((request, timeout))
        try:
            outcome = next(self._outcomes)
        except StopIteration:
            pytest.fail("helper made an unexpected extra request")
        if isinstance(outcome, BaseException):
            raise outcome
        return outcome


def _headers(values: Mapping[str, str] | None = None) -> HTTPMessage:
    headers = HTTPMessage()
    for name, value in (values or {}).items():
        headers[name] = value
    return headers


def _metadata_body(
    *,
    run_id: object = RUN_ID,
    repository: object = REPOSITORY,
    created_at: object = "2024-02-29T12:34:56Z",
) -> bytes:
    return json.dumps(
        {
            "id": run_id,
            "repository": {"full_name": repository},
            "created_at": created_at,
        },
        separators=(",", ":"),
    ).encode()


def _http_error(
    status: int,
    *,
    headers: Mapping[str, str] | None = None,
    body: bytes = b"",
) -> tuple[urllib.error.HTTPError, _RecordingStream]:
    stream = _RecordingStream(body)
    error = urllib.error.HTTPError(
        RUN_URL,
        status,
        "scripted failure",
        _headers(headers),
        cast("BinaryIO", stream),
    )
    return error, stream


def _set_valid_environment(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv("GITHUB_API_URL", API_URL)
    monkeypatch.setenv("GITHUB_REPOSITORY", REPOSITORY)
    monkeypatch.setenv("GITHUB_RUN_ID", str(RUN_ID))
    monkeypatch.setenv("WDV3_GITHUB_TOKEN", TOKEN)
    monkeypatch.setattr(HELPER.sys, "argv", [str(HELPER_PATH)])


def _install_transport(
    monkeypatch: pytest.MonkeyPatch,
    outcomes: Sequence[_Response | BaseException],
) -> tuple[_FakeOpener, list[float], list[object]]:
    opener = _FakeOpener(outcomes)
    sleeps: list[float] = []
    handlers: list[object] = []

    def build_opener(*received_handlers: object) -> _FakeOpener:
        handlers.extend(received_handlers)
        return opener

    monkeypatch.setattr(HELPER.urllib.request, "build_opener", build_opener)
    monkeypatch.setattr(HELPER.time, "sleep", sleeps.append)
    monkeypatch.setattr(HELPER.time, "time", lambda: FIXED_NOW)
    return opener, sleeps, handlers


def _run_main(
    capsys: pytest.CaptureFixture[str],
) -> tuple[int, str, str]:
    result = HELPER.main()
    captured = capsys.readouterr()
    return result, captured.out, captured.err


@pytest.mark.parametrize(
    "created_at",
    [
        "2024-02-29T12:34:56Z",
        "2024-02-29T12:34:56.123456Z",
    ],
)
def test_main_performs_one_authenticated_bounded_request(
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
    created_at: str,
) -> None:
    _set_valid_environment(monkeypatch)
    response = _Response(_metadata_body(created_at=created_at))
    opener, sleeps, handlers = _install_transport(monkeypatch, [response])

    result, stdout, stderr = _run_main(capsys)

    assert (result, stdout, stderr) == (0, f"{EXPECTED_EPOCH}\n", "")
    assert len(opener.calls) == 1
    request, timeout = opener.calls[0]
    assert request.full_url == RUN_URL
    assert request.method == "GET"
    assert request.get_header("Authorization") == f"Bearer {TOKEN}"
    assert TOKEN not in request.full_url
    assert timeout == REQUEST_TIMEOUT_SECONDS
    assert response.stream.read_sizes == [MAX_RESPONSE_BYTES + 1]
    assert sleeps == []
    assert [type(handler).__name__ for handler in handlers] == [
        "_RejectRedirects"
    ]


def test_enterprise_api_path_prefix_is_preserved(
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
) -> None:
    _set_valid_environment(monkeypatch)
    monkeypatch.setenv("GITHUB_API_URL", "https://github.example/api/v3/")
    opener, _sleeps, _handlers = _install_transport(
        monkeypatch,
        [_Response(_metadata_body())],
    )

    result, _stdout, _stderr = _run_main(capsys)

    assert result == 0
    assert opener.calls[0][0].full_url == (
        "https://github.example/api/v3/"
        f"repos/{REPOSITORY}/actions/runs/{RUN_ID}"
    )


def test_redirect_handler_never_reuses_credentials() -> None:
    handler = HELPER._RejectRedirects()  # noqa: SLF001

    assert (
        handler.redirect_request(
            object(),
            object(),
            302,
            "redirect",
            object(),
            "https://redirect.example",
        )
        is None
    )


@pytest.mark.parametrize(
    ("payload", "expected"),
    [
        pytest.param(b"{", "malformed run metadata JSON", id="invalid-json"),
        pytest.param(b"[]", "run metadata is not an object", id="not-object"),
        pytest.param(
            _metadata_body(run_id=True),
            "mismatched run identity",
            id="boolean-run-id",
        ),
        pytest.param(
            _metadata_body(run_id=RUN_ID + 1),
            "mismatched run identity",
            id="wrong-run-id",
        ),
        pytest.param(
            _metadata_body(repository="other/repository"),
            "mismatched repository identity",
            id="wrong-repository",
        ),
        pytest.param(
            _metadata_body(created_at="2024-02-29T12:34:56+00:00"),
            "missing or malformed created_at",
            id="noncanonical-time",
        ),
        pytest.param(
            _metadata_body(created_at="2024-02-30T12:34:56Z"),
            "invalid created_at",
            id="invalid-time",
        ),
        pytest.param(
            (
                b'{"id":'
                + (b"9" * 5_000)
                + b',"repository":{"full_name":"octo/example"},'
                b'"created_at":"2024-02-29T12:34:56Z"}'
            ),
            "malformed run metadata JSON",
            id="oversized-integer",
        ),
        pytest.param(
            (b"[" * 10_000) + b"0" + (b"]" * 10_000),
            ("malformed run metadata JSON", "run metadata is not an object"),
            id="deeply-nested",
        ),
    ],
)
def test_main_rejects_invalid_schema_or_identity_without_retry(
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
    payload: bytes,
    expected: str | tuple[str, ...],
) -> None:
    _set_valid_environment(monkeypatch)
    opener, sleeps, _handlers = _install_transport(
        monkeypatch,
        [_Response(payload)],
    )

    result, stdout, stderr = _run_main(capsys)

    expected_messages = (expected,) if isinstance(expected, str) else expected
    assert (result, stdout) == (1, "")
    assert len(opener.calls) == 1
    assert sleeps == []
    assert any(message in stderr for message in expected_messages)
    assert len(stderr.splitlines()) == 1
    assert "Traceback" not in stderr
    assert TOKEN not in stderr


def test_success_and_error_response_reads_are_bounded(
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
) -> None:
    _set_valid_environment(monkeypatch)
    oversized = _Response(b"x" * (MAX_RESPONSE_BYTES + 1))
    opener, sleeps, _handlers = _install_transport(
        monkeypatch,
        [oversized],
    )

    result, _stdout, stderr = _run_main(capsys)

    assert result == 1
    assert len(opener.calls) == 1
    assert oversized.stream.read_sizes == [MAX_RESPONSE_BYTES + 1]
    assert sleeps == []
    assert "exceeds the size limit" in stderr

    error, stream = _http_error(
        401,
        body=json.dumps(
            {
                "message": (
                    f"{TOKEN} "
                    f"{urllib.parse.quote(TOKEN, safe='')} "
                    f"Bearer {TOKEN} Authorization: {TOKEN}"
                )
            }
        ).encode()
        + (b"x" * MAX_ERROR_BYTES),
    )
    opener, sleeps, _handlers = _install_transport(monkeypatch, [error])

    result, _stdout, stderr = _run_main(capsys)

    assert result == 1
    assert len(opener.calls) == 1
    assert stream.read_sizes == [MAX_ERROR_BYTES + 1]
    assert sleeps == []
    assert TOKEN not in stderr
    assert "Authorization" not in stderr


@pytest.mark.parametrize("status", [408, 429, 500, 502, 503, 504])
def test_main_retries_only_allowlisted_http_statuses(
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
    status: int,
) -> None:
    _set_valid_environment(monkeypatch)
    outcomes = [_http_error(status)[0] for _ in range(MAX_ATTEMPTS)]
    opener, sleeps, _handlers = _install_transport(monkeypatch, outcomes)

    result, stdout, stderr = _run_main(capsys)

    assert (result, stdout) == (1, "")
    assert len(opener.calls) == MAX_ATTEMPTS
    assert sleeps == [1.0, 2.0]
    assert len(stderr.splitlines()) == MAX_ATTEMPTS
    assert "retries-exhausted" in stderr.splitlines()[-1]
    assert TOKEN not in stderr


@pytest.mark.parametrize(
    ("headers", "attempts", "sleeps"),
    [
        pytest.param(
            {"retry-after": "0"},
            3,
            [0.0, 0.0],
            id="retry-after",
        ),
        pytest.param(
            {
                "X-RATELIMIT-REMAINING": "0",
                "x-ratelimit-reset": "1700000035",
            },
            3,
            [30.0, 30.0],
            id="rate-limit-reset",
        ),
        pytest.param({}, 1, [], id="no-proof"),
        pytest.param(
            {"X-RateLimit-Remaining": "1"},
            1,
            [],
            id="remaining",
        ),
    ],
)
def test_403_retries_only_with_rate_limit_proof(
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
    headers: dict[str, str],
    attempts: int,
    sleeps: list[float],
) -> None:
    _set_valid_environment(monkeypatch)
    outcomes = [
        _http_error(403, headers=headers)[0] for _ in range(MAX_ATTEMPTS)
    ]
    opener, actual_sleeps, _handlers = _install_transport(
        monkeypatch,
        outcomes,
    )

    result, _stdout, stderr = _run_main(capsys)

    assert result == 1
    assert len(opener.calls) == attempts
    assert actual_sleeps == sleeps
    assert len(stderr.splitlines()) == attempts


def test_retry_delay_is_capped(
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
) -> None:
    _set_valid_environment(monkeypatch)
    outcomes = [
        _http_error(429, headers={"Retry-After": "999999"})[0]
        for _ in range(MAX_ATTEMPTS)
    ]
    opener, sleeps, _handlers = _install_transport(monkeypatch, outcomes)

    result, _stdout, _stderr = _run_main(capsys)

    assert result == 1
    assert len(opener.calls) == MAX_ATTEMPTS
    assert sleeps == [30.0, 30.0]


@pytest.mark.parametrize(
    "error",
    [
        pytest.param(TimeoutError(TOKEN), id="timeout"),
        pytest.param(
            urllib.error.URLError(
                socket.gaierror(getattr(socket, "EAI_AGAIN", -3), TOKEN)
            ),
            id="temporary-dns",
        ),
        pytest.param(
            ConnectionResetError(errno.ECONNRESET, TOKEN),
            id="connection-reset",
        ),
        pytest.param(RemoteDisconnected(TOKEN), id="remote-disconnected"),
    ],
)
def test_transient_transport_failures_retry_three_times(
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
    error: BaseException,
) -> None:
    _set_valid_environment(monkeypatch)
    opener, sleeps, _handlers = _install_transport(
        monkeypatch,
        [error, error, error],
    )

    result, _stdout, stderr = _run_main(capsys)

    assert result == 1
    assert len(opener.calls) == MAX_ATTEMPTS
    assert sleeps == [1.0, 2.0]
    assert len(stderr.splitlines()) == MAX_ATTEMPTS
    assert TOKEN not in stderr


def test_transient_failure_can_recover(
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
) -> None:
    _set_valid_environment(monkeypatch)
    opener, sleeps, _handlers = _install_transport(
        monkeypatch,
        [TimeoutError(TOKEN), _Response(_metadata_body())],
    )

    result, stdout, stderr = _run_main(capsys)

    assert (result, stdout) == (0, f"{EXPECTED_EPOCH}\n")
    assert sleeps == [1.0]
    assert len(opener.calls) == len(sleeps) + 1
    assert len(stderr.splitlines()) == 1
    assert TOKEN not in stderr


@pytest.mark.parametrize(
    "outcome",
    [
        pytest.param(ssl.SSLError(TOKEN), id="tls"),
        pytest.param(
            urllib.error.URLError(ssl.SSLError(TOKEN)),
            id="wrapped-tls",
        ),
        pytest.param(
            socket.gaierror(getattr(socket, "EAI_NONAME", -2), TOKEN),
            id="permanent-dns",
        ),
        pytest.param(OSError(errno.EACCES, TOKEN), id="os-error"),
        pytest.param(_http_error(302)[0], id="redirect"),
        pytest.param(_http_error(401)[0], id="authentication"),
        pytest.param(_http_error(422)[0], id="client"),
        pytest.param(_http_error(501)[0], id="server-not-allowlisted"),
        pytest.param(_Response(_metadata_body(), status=None), id="bad-status"),
    ],
)
def test_terminal_failures_stop_after_one_attempt(
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
    outcome: _Response | BaseException,
) -> None:
    _set_valid_environment(monkeypatch)
    opener, sleeps, handlers = _install_transport(monkeypatch, [outcome])

    result, stdout, stderr = _run_main(capsys)

    assert (result, stdout) == (1, "")
    assert len(opener.calls) == 1
    assert sleeps == []
    assert len(stderr.splitlines()) == 1
    assert TOKEN not in stderr
    assert [type(handler).__name__ for handler in handlers] == [
        "_RejectRedirects"
    ]


@pytest.mark.parametrize(
    ("name", "value"),
    [
        pytest.param("WDV3_GITHUB_TOKEN", MISSING, id="missing-token"),
        pytest.param("WDV3_GITHUB_TOKEN", "bad\ntoken", id="unsafe-token"),
        pytest.param("GITHUB_API_URL", "http://api.github.com", id="http"),
        pytest.param(
            "GITHUB_API_URL",
            "https://user@api.github.com",
            id="userinfo",
        ),
        pytest.param(
            "GITHUB_API_URL",
            "https://api.github.com?query=1",
            id="query",
        ),
        pytest.param("GITHUB_REPOSITORY", "../repo", id="repository"),
        pytest.param("GITHUB_RUN_ID", "0", id="run-id"),
        pytest.param(
            "GITHUB_RUN_ID",
            "9" * 5_000,
            id="oversized-run-id",
        ),
    ],
)
def test_invalid_configuration_fails_before_network_access(
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
    name: str,
    value: object,
) -> None:
    _set_valid_environment(monkeypatch)
    if value is MISSING:
        monkeypatch.delenv(name)
    else:
        assert isinstance(value, str)
        monkeypatch.setenv(name, value)
    opener, sleeps, _handlers = _install_transport(monkeypatch, [])

    result, stdout, stderr = _run_main(capsys)

    assert (result, stdout) == (1, "")
    assert opener.calls == []
    assert sleeps == []
    assert len(stderr.splitlines()) == 1
    assert "configuration-error" in stderr
    assert TOKEN not in stderr


def test_main_rejects_arguments_before_network_access(
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
) -> None:
    _set_valid_environment(monkeypatch)
    monkeypatch.setattr(HELPER.sys, "argv", [str(HELPER_PATH), "unexpected"])
    opener, sleeps, _handlers = _install_transport(monkeypatch, [])

    result, stdout, stderr = _run_main(capsys)

    assert (result, stdout) == (1, "")
    assert opener.calls == []
    assert sleeps == []
    assert "helper accepts no arguments" in stderr
