"""Fetch the current GitHub Actions run creation time."""

from __future__ import annotations

import calendar
import email.utils
import errno
import http.client
import json
import os
import re
import socket
import ssl
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from dataclasses import dataclass
from datetime import datetime
from http import HTTPStatus
from typing import TYPE_CHECKING, override

if TYPE_CHECKING:
    from collections.abc import Mapping
    from email.message import Message
    from typing import IO, Protocol

    class _Readable(Protocol):
        def read(self, n: int = -1) -> bytes:
            """Read at most limit bytes."""


_ATTEMPTS = 3
_REQUEST_TIMEOUT_SECONDS = 10.0
_MAX_SLEEP_SECONDS = 30.0
_MAX_RESPONSE_BYTES = 1024 * 1024
_MAX_ERROR_BYTES = 4096
_MAX_DIAGNOSTIC_VALUE_CHARS = 96
_MAX_MESSAGE_CHARS = 240
_MAX_TOKEN_CHARS = 4096
_MAX_INTEGER_HEADER_CHARS = 12
_TOKEN_ENVIRONMENT_VARIABLE = "WDV3_GITHUB_TOKEN"  # noqa: S105
_SELECTED_DIAGNOSTIC_HEADERS = (
    "Retry-After",
    "X-RateLimit-Remaining",
    "X-RateLimit-Reset",
)
_CREATED_AT_PATTERN = re.compile(
    r"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,6})?Z\Z",
    re.ASCII,
)
_REPOSITORY_PATTERN = re.compile(
    r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+\Z",
    re.ASCII,
)
_RUN_ID_PATTERN = re.compile(r"[1-9]\d*\Z", re.ASCII)
_BEARER_PATTERN = re.compile(
    r"\bbearer\s+[A-Za-z0-9._~+/=-]+",
    re.IGNORECASE,
)
_AUTHORIZATION_PATTERN = re.compile(
    r"\bauthorization\b(?:\s*[:=]\s*\S+)?",
    re.IGNORECASE,
)
_RETRYABLE_HTTP_STATUSES = frozenset(
    {
        HTTPStatus.REQUEST_TIMEOUT,
        HTTPStatus.TOO_MANY_REQUESTS,
        HTTPStatus.INTERNAL_SERVER_ERROR,
        HTTPStatus.BAD_GATEWAY,
        HTTPStatus.SERVICE_UNAVAILABLE,
        HTTPStatus.GATEWAY_TIMEOUT,
    }
)
_RETRYABLE_ERRNOS = frozenset(
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


class MetadataError(ValueError):
    """Indicate invalid run metadata or helper configuration."""


class _InvalidResponseStatusError(MetadataError):
    """Indicate a response without an integer HTTP status."""


@dataclass(frozen=True, slots=True)
class _Configuration:
    url: str
    token: str
    repository: str
    run_id: int


@dataclass(frozen=True, slots=True)
class _HttpResponse:
    status: int
    headers: Mapping[str, str]
    body: bytes
    truncated: bool


class _RejectRedirects(urllib.request.HTTPRedirectHandler):
    @override
    def redirect_request(
        self,
        req: urllib.request.Request,
        fp: IO[bytes],
        code: int,
        msg: str,
        headers: http.client.HTTPMessage,
        newurl: str,
    ) -> urllib.request.Request | None:
        """Reject redirects before a bearer credential can be forwarded."""
        del req, fp, code, msg, headers, newurl


def _required_environment(name: str) -> str:
    value = os.environ.get(name)
    if value is None or not value:
        message = f"required environment variable {name} is missing"
        raise MetadataError(message)
    return value


def _configuration() -> _Configuration:
    api_url = _required_environment("GITHUB_API_URL")
    repository = _required_environment("GITHUB_REPOSITORY")
    run_id = _required_environment("GITHUB_RUN_ID")
    token = _required_environment(_TOKEN_ENVIRONMENT_VARIABLE)

    if (
        not token.isascii()
        or any(
            character.isspace() or not character.isprintable()
            for character in token
        )
        or len(token) > _MAX_TOKEN_CHARS
    ):
        message = f"{_TOKEN_ENVIRONMENT_VARIABLE} is invalid"
        raise MetadataError(message)
    if _REPOSITORY_PATTERN.fullmatch(repository) is None or any(
        part in {".", ".."} for part in repository.split("/")
    ):
        message = "GITHUB_REPOSITORY is invalid"
        raise MetadataError(message)
    if _RUN_ID_PATTERN.fullmatch(run_id) is None:
        message = "GITHUB_RUN_ID is invalid"
        raise MetadataError(message)

    try:
        parsed_api_url = urllib.parse.urlsplit(api_url)
        api_hostname = parsed_api_url.hostname
        api_username = parsed_api_url.username
        api_password = parsed_api_url.password
        _ = parsed_api_url.port
    except ValueError as error:
        message = "GITHUB_API_URL is malformed"
        raise MetadataError(message) from error

    decoded_path_parts = tuple(
        urllib.parse.unquote(part) for part in parsed_api_url.path.split("/")
    )
    if (
        parsed_api_url.scheme.lower() != "https"
        or api_hostname is None
        or api_username is not None
        or api_password is not None
        or parsed_api_url.query
        or parsed_api_url.fragment
        or any(
            character.isspace() or not character.isprintable()
            for character in api_url
        )
        or any(
            part in {".", ".."}
            or "/" in part
            or "\\" in part
            or any(
                character.isspace() or not character.isprintable()
                for character in part
            )
            for part in decoded_path_parts
        )
    ):
        message = "GITHUB_API_URL is not a safe HTTPS API origin"
        raise MetadataError(message)

    url = f"{api_url.rstrip('/')}/repos/{repository}/actions/runs/{run_id}"
    return _Configuration(
        url=url,
        token=token,
        repository=repository,
        run_id=int(run_id),
    )


def _read_limited(stream: _Readable, limit: int) -> tuple[bytes, bool]:
    data = stream.read(limit + 1)
    if not isinstance(data, bytes):
        message = "GitHub returned a non-byte response"
        raise MetadataError(message)
    return data[:limit], len(data) > limit


def _copy_headers(
    headers: Mapping[str, str] | Message | None,
) -> dict[str, str]:
    if headers is None:
        return {}
    return {
        name.lower(): value
        for name, value in headers.items()
        if isinstance(name, str) and isinstance(value, str)
    }


def _sanitize(value: str, token: str, limit: int) -> str:
    sanitized = value
    for secret in {
        token,
        urllib.parse.quote(token, safe=""),
        urllib.parse.quote_plus(token),
    }:
        sanitized = sanitized.replace(secret, "[redacted]")
    sanitized = _BEARER_PATTERN.sub("[redacted]", sanitized)
    sanitized = _AUTHORIZATION_PATTERN.sub(
        "[redacted-header]",
        sanitized,
    )
    sanitized = " ".join(
        "".join(
            character if character.isprintable() else " "
            for character in sanitized
        ).split()
    )
    if len(sanitized) > limit:
        return f"{sanitized[:limit]}..."
    return sanitized


def _error_summary(
    body: bytes,
    *,
    truncated: bool,
    token: str,
) -> str | None:
    if truncated or not body:
        return None
    try:
        decoded = body.decode("utf-8", "strict")
        document = json.loads(decoded)
    except (UnicodeDecodeError, ValueError, RecursionError):
        return None
    if not isinstance(document, dict) or not isinstance(
        document.get("message"),
        str,
    ):
        return None
    sanitized = _sanitize(document["message"], token, _MAX_MESSAGE_CHARS)
    return sanitized or None


def _header_value(headers: Mapping[str, str], name: str) -> str | None:
    value = headers.get(name.lower())
    return value if isinstance(value, str) else None


def _bounded_seconds(seconds: float) -> float:
    return min(max(seconds, 0.0), _MAX_SLEEP_SECONDS)


def _retry_after_seconds(value: str | None) -> float | None:
    candidate = "" if value is None else value.strip()
    if not candidate:
        return None
    if candidate.isascii() and candidate.isdecimal():
        if len(candidate) > _MAX_INTEGER_HEADER_CHARS:
            return _MAX_SLEEP_SECONDS
        return _bounded_seconds(float(int(candidate)))
    try:
        retry_at = email.utils.parsedate_to_datetime(candidate)
    except (OverflowError, TypeError, ValueError):
        return None
    if retry_at.tzinfo is None:
        return None
    return _bounded_seconds(retry_at.timestamp() - time.time())


def _rate_limit_reset_seconds(value: str | None) -> float | None:
    if value is None:
        return None
    candidate = value.strip()
    if not candidate.isascii() or not candidate.isdecimal():
        return None
    if len(candidate) > _MAX_INTEGER_HEADER_CHARS:
        return _MAX_SLEEP_SECONDS
    return _bounded_seconds(float(int(candidate)) - time.time())


def _retry_delay(
    status: int,
    headers: Mapping[str, str],
    attempt: int,
) -> float | None:
    retry_after = _retry_after_seconds(
        _header_value(headers, "Retry-After"),
    )
    default_delay = _bounded_seconds(float(2 ** (attempt - 1)))

    if status == HTTPStatus.FORBIDDEN:
        if retry_after is not None:
            return retry_after
        remaining = _header_value(headers, "X-RateLimit-Remaining")
        if remaining is None or re.fullmatch(r"0+", remaining.strip()) is None:
            return None
        reset_delay = _rate_limit_reset_seconds(
            _header_value(headers, "X-RateLimit-Reset"),
        )
        return default_delay if reset_delay is None else reset_delay

    if status in _RETRYABLE_HTTP_STATUSES:
        return default_delay if retry_after is None else retry_after
    return None


def _diagnose_http(
    *,
    attempt: int,
    response: _HttpResponse,
    token: str,
    disposition: str,
) -> None:
    parts = [
        "run metadata request",
        f"attempt={attempt}/{_ATTEMPTS}",
        f"status={response.status}",
    ]
    for name in _SELECTED_DIAGNOSTIC_HEADERS:
        value = _header_value(response.headers, name)
        if value is not None:
            sanitized = _sanitize(
                value,
                token,
                _MAX_DIAGNOSTIC_VALUE_CHARS,
            )
            parts.append(f"{name}={json.dumps(sanitized)}")
    summary = _error_summary(
        response.body,
        truncated=response.truncated,
        token=token,
    )
    if summary is not None:
        parts.append(f"message={json.dumps(summary)}")
    parts.append(disposition)
    sys.stderr.write(" ".join(parts) + "\n")


def _created_epoch(
    body: bytes,
    *,
    expected_repository: str,
    expected_run_id: int,
) -> int:
    try:
        document = json.loads(body)
    except (UnicodeDecodeError, ValueError, RecursionError) as error:
        message = "GitHub returned malformed run metadata JSON"
        raise MetadataError(message) from error
    if not isinstance(document, dict):
        message = "GitHub run metadata is not an object"
        raise MetadataError(message)

    run_id = document.get("id")
    if type(run_id) is not int or run_id != expected_run_id:
        message = "GitHub run metadata has mismatched run identity"
        raise MetadataError(message)
    repository = document.get("repository")
    if (
        not isinstance(repository, dict)
        or repository.get("full_name") != expected_repository
    ):
        message = "GitHub run metadata has mismatched repository identity"
        raise MetadataError(message)

    created_at = document.get("created_at")
    if (
        not isinstance(created_at, str)
        or _CREATED_AT_PATTERN.fullmatch(created_at) is None
    ):
        message = "GitHub run metadata has missing or malformed created_at"
        raise MetadataError(message)
    try:
        created = datetime.fromisoformat(f"{created_at[:-1]}+00:00")
        return calendar.timegm(created.utctimetuple())
    except (OverflowError, OSError, ValueError) as error:
        message = "GitHub run metadata has invalid created_at"
        raise MetadataError(message) from error


def _is_transient_transport(error: BaseException) -> bool:
    if isinstance(error, urllib.error.URLError):
        reason = error.reason
        return (
            isinstance(reason, BaseException)
            and reason is not error
            and _is_transient_transport(reason)
        )
    if isinstance(error, ssl.SSLError):
        return False
    if isinstance(error, socket.gaierror):
        return error.errno == getattr(socket, "EAI_AGAIN", None)
    if isinstance(
        error,
        (
            TimeoutError,
            ConnectionError,
            http.client.BadStatusLine,
            http.client.IncompleteRead,
            http.client.RemoteDisconnected,
        ),
    ):
        return True
    return isinstance(error, OSError) and error.errno in _RETRYABLE_ERRNOS


def _diagnose_transport(
    *,
    attempt: int,
    error: BaseException,
    disposition: str,
) -> None:
    sys.stderr.write(
        "run metadata request "
        f"attempt={attempt}/{_ATTEMPTS} "
        "status=transport-error "
        f"detail={type(error).__name__} {disposition}\n",
    )


def _transport_retry_delay(
    attempt: int,
    error: BaseException,
) -> float | None:
    delay: float | None = None
    if not _is_transient_transport(error):
        disposition = "not-retryable"
    elif attempt == _ATTEMPTS:
        disposition = "retries-exhausted"
    else:
        delay = _bounded_seconds(float(2 ** (attempt - 1)))
        disposition = f"retry-in={delay:.3f}s"
    _diagnose_transport(
        attempt=attempt,
        error=error,
        disposition=disposition,
    )
    return delay


def _open_once(
    opener: urllib.request.OpenerDirector,
    request: urllib.request.Request,
) -> _HttpResponse:
    try:
        with opener.open(
            request,
            timeout=_REQUEST_TIMEOUT_SECONDS,
        ) as response:
            status = response.getcode()
            headers = _copy_headers(response.headers)
            body, truncated = _read_limited(
                response,
                _MAX_RESPONSE_BYTES,
            )
    except urllib.error.HTTPError as error:
        status = error.code
        headers = _copy_headers(error.headers)
        try:
            body, truncated = _read_limited(error, _MAX_ERROR_BYTES)
        except (MetadataError, OSError, http.client.HTTPException):
            body, truncated = b"", False

    if not isinstance(status, int):
        message = "GitHub returned an invalid HTTP status"
        raise _InvalidResponseStatusError(message)
    return _HttpResponse(
        status=status,
        headers=headers,
        body=body,
        truncated=truncated,
    )


def _created_epoch_from_response(
    response: _HttpResponse,
    configuration: _Configuration,
    attempt: int,
) -> int | None:
    if response.truncated:
        message = "GitHub run metadata response exceeds the size limit"
        sys.stderr.write(
            "run metadata request "
            f"attempt={attempt}/{_ATTEMPTS} "
            f"status={response.status} "
            f"response-error={json.dumps(message)}\n",
        )
        return None
    try:
        return _created_epoch(
            response.body,
            expected_repository=configuration.repository,
            expected_run_id=configuration.run_id,
        )
    except MetadataError as error:
        sys.stderr.write(
            "run metadata request "
            f"attempt={attempt}/{_ATTEMPTS} "
            f"status={response.status} "
            f"response-error={json.dumps(str(error))}\n",
        )
        return None


def _fetch_created_epoch(configuration: _Configuration) -> int | None:
    request = urllib.request.Request(  # noqa: S310
        configuration.url,
        headers={
            "Accept": "application/vnd.github+json",
            "Authorization": f"Bearer {configuration.token}",
            "User-Agent": "workflow-delivery-v3-run-metadata",
            "X-GitHub-Api-Version": "2022-11-28",
        },
        method="GET",
    )
    opener = urllib.request.build_opener(_RejectRedirects())

    for attempt in range(1, _ATTEMPTS + 1):
        try:
            response = _open_once(opener, request)
        except _InvalidResponseStatusError:
            sys.stderr.write(
                "run metadata request "
                f"attempt={attempt}/{_ATTEMPTS} "
                "status=invalid-response not-retryable\n",
            )
            return None
        except (OSError, http.client.HTTPException) as error:
            delay = _transport_retry_delay(attempt, error)
            if delay is None:
                return None
            time.sleep(delay)
            continue
        except (TypeError, ValueError) as error:
            _diagnose_transport(
                attempt=attempt,
                error=error,
                disposition="not-retryable",
            )
            return None

        if response.status == HTTPStatus.OK:
            return _created_epoch_from_response(
                response,
                configuration,
                attempt,
            )

        delay = _retry_delay(response.status, response.headers, attempt)
        if delay is None:
            disposition = "not-retryable"
        elif attempt == _ATTEMPTS:
            disposition = "retries-exhausted"
        else:
            disposition = f"retry-in={delay:.3f}s"
        _diagnose_http(
            attempt=attempt,
            response=response,
            token=configuration.token,
            disposition=disposition,
        )
        if delay is None or attempt == _ATTEMPTS:
            return None
        time.sleep(delay)

    return None


def main() -> int:
    """Emit the current workflow run's validated creation epoch."""
    if len(sys.argv) != 1:
        sys.stderr.write(
            f"run metadata request attempt=0/{_ATTEMPTS} "
            "status=configuration-error helper accepts no arguments\n",
        )
        return 1
    try:
        configuration = _configuration()
    except MetadataError as error:
        sys.stderr.write(
            f"run metadata request attempt=0/{_ATTEMPTS} "
            f"status=configuration-error detail={json.dumps(str(error))}\n",
        )
        return 1

    created_epoch = _fetch_created_epoch(configuration)
    if created_epoch is None:
        return 1
    sys.stdout.write(f"{created_epoch}\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())
