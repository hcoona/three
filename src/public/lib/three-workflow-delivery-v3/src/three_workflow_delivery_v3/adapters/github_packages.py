"""Strict GitHub Packages npm observation and publication Adapter."""

from __future__ import annotations

import hashlib
import http.client
import re
import stat
import urllib.error
import urllib.parse
import urllib.request
from dataclasses import dataclass
from typing import TYPE_CHECKING, Protocol, cast

from three_workflow_delivery_v3.adapters.node import (
    ArtifactExpectation,
    _qualify_npm_artifact_entries,
    _read_tarball,
    _validate_artifact_expectation,
)
from three_workflow_delivery_v3.adapters.npmjs import (
    DEFAULT_EXPANDED_TARBALL_LIMIT_BYTES,
    _remote_tarball_observation,
)
from three_workflow_delivery_v3.canonical import (
    canonical_sha256,
    parse_json_strict,
)
from three_workflow_delivery_v3.records.release import (
    BuddyExecutionIdentity,
    DestinationOperationProfile,
    DestinationProjection,
    DestinationReadback,
    PackageControlProof,
    PackageControlSubject,
    PublicationAction,
    PublicationDiagnostics,
    QualificationDecision,
    QualificationSnapshot,
    ReleaseArtifact,
    ReleaseAttemptIdentity,
    validate_publication_action_instantiation,
)
from three_workflow_delivery_v3.repository.node_provider import NbgvFacts

if TYPE_CHECKING:
    from collections.abc import Callable
    from pathlib import Path

    from three_workflow_delivery_v3.canonical import JsonValue

GITHUB_PACKAGES_DESTINATION_ID = "npm/github-packages-hcoona-three-v1"
GITHUB_PACKAGES_REGISTRY = "https://npm.pkg.github.com"
GITHUB_PACKAGES_PACKAGE = "@hcoona/hcoona-release-smoke-npm"
GITHUB_PACKAGES_OBSERVATION_CONTRACT_ID = "npm/github-packages-observation-v1"
GITHUB_PACKAGES_OPERATION = "conditional-create-npm-version-and-target-tag"
GITHUB_PACKAGES_DESTINATION_OPERATION_PROFILE_ID = (
    "npm/github-packages-hcoona-three-standard-publish-v1"
)
GITHUB_PACKAGES_NODE_VERSION = "24.19.0"
GITHUB_PACKAGES_NPM_VERSION = "11.17.0"
GITHUB_PACKAGES_OWNER = "hcoona"
GITHUB_PACKAGES_OBSERVER_PRODUCER = "observe-github-packages"
GITHUB_PACKAGES_PUBLISHER_PRODUCER = "publish-github-packages"
GITHUB_API_ORIGIN = "https://api.github.com"

_GITHUB_PACKAGES_DESTINATION_OPERATION_PROFILE = DestinationOperationProfile(
    profile_id=GITHUB_PACKAGES_DESTINATION_OPERATION_PROFILE_ID,
    registry=GITHUB_PACKAGES_REGISTRY,
    access_mode="existing-public-package/no-access-mutation",
    node_version=GITHUB_PACKAGES_NODE_VERSION,
    npm_version=GITHUB_PACKAGES_NPM_VERSION,
    command_template=(
        "npm",
        "publish",
        "{tarball-path}",
        "--registry",
        GITHUB_PACKAGES_REGISTRY,
        "--tag",
        "{tag}",
        "--ignore-scripts",
        "--fetch-retries=0",
    ),
    operand_slots=(
        (
            "package",
            "npm-package-name/equal-tarball-manifest-name",
        ),
        (
            "version",
            "npm-version/equal-tarball-manifest-version",
        ),
        (
            "tarball-reference",
            "artifact-reference/exact-payload-path",
        ),
        ("tag", "npm-dist-tag/buddy-sha-target"),
    ),
    configuration_precedence=(
        (
            "authentication",
            "temporary-user-config/current-repository-github-token",
        ),
        ("fetch-retries", "command-line/0"),
        ("ignore-scripts", "command-line/true"),
        ("project-config", "disabled"),
        ("registry", f"command-line/{GITHUB_PACKAGES_REGISTRY}"),
        ("tag", "command-line/publication-action"),
    ),
    request_generation=(
        ("client", "pinned-standard-npm"),
        ("hand-built-publish-request", "forbidden"),
        ("wrapper-registry-protocol", "forbidden"),
    ),
    mutation_retry="forbidden-after-request-initiation",
)


def github_packages_destination_operation_profile() -> (
    DestinationOperationProfile
):
    """Resolve the sole first-slice GitHub Packages mutation profile."""
    return _GITHUB_PACKAGES_DESTINATION_OPERATION_PROFILE


def validate_github_packages_publication_action(
    *,
    action: PublicationAction,
    projection: DestinationProjection,
    artifact: ReleaseArtifact,
) -> None:
    """Admit only an exact instance of the first-slice destination profile."""
    profile = github_packages_destination_operation_profile()
    if (
        projection.coordinate.channel != "buddy"
        or projection.destination_id != GITHUB_PACKAGES_DESTINATION_ID
        or projection.operation != GITHUB_PACKAGES_OPERATION
    ):
        message = "GitHub Packages Publication Action projection is unsupported"
        raise ValueError(message)
    validate_publication_action_instantiation(
        action,
        destination_operation_profile=profile,
        projection=projection,
        artifact=artifact,
    )


DEFAULT_TIMEOUT_SECONDS = 20
DEFAULT_METADATA_LIMIT_BYTES = 1_000_000
DEFAULT_TARBALL_LIMIT_BYTES = 25_000_000
DEFAULT_MAX_PAGES = 100
GITHUB_PAGE_SIZE = 100
PAIR_SIZE = 2
MAX_REDIRECTS = 5
HTTP_OK = 200
HTTP_MULTIPLE_CHOICES = 300
HTTP_PERMANENT_REDIRECT = 308
HTTP_UNAUTHORIZED = 401
HTTP_FORBIDDEN = 403
HTTP_NOT_FOUND = 404
HTTP_CONFLICT = 409
HTTP_TOO_MANY_REQUESTS = 429
HTTP_SERVER_ERROR = 500
_REDACTED = "******"
_TOKEN_PATTERNS = (
    re.compile(r"(?i)(authorization\s*:\s*)(?:bearer\s+)?\S+"),
    re.compile(r"(?i)(//npm\.pkg\.github\.com/:_authToken=)\S+"),
    re.compile(r"(?i)(npm_token\s*=\s*)\S+"),
)
_SELECTED_HEADERS = frozenset(
    {
        "cache-control",
        "content-length",
        "content-type",
        "etag",
        "last-modified",
        "link",
        "retry-after",
    }
)
_ALLOWED_TARBALL_HOSTS = frozenset(
    {
        "npm.pkg.github.com",
        "pkg-npm.githubusercontent.com",
        "objects.githubusercontent.com",
        "github-registry-files.githubusercontent.com",
    }
)
_CREDENTIAL_HOSTS = frozenset({"api.github.com", "npm.pkg.github.com"})


@dataclass(frozen=True, slots=True)
class GitHubPackagesHttpResponse:
    """Bounded response returned by an injectable authenticated GET seam."""

    status: int
    url: str
    headers: tuple[tuple[str, str], ...]
    body: bytes
    redirects: tuple[str, ...] = ()
    truncated: bool = False
    complete: bool = True

    def __post_init__(self) -> None:
        """Reject malformed transport facts before classification."""
        if type(self.status) is not int or self.status < 0:
            message = "HTTP response status must be a nonnegative exact integer"
            raise TypeError(message)
        if type(self.url) is not str or not self.url:
            message = "HTTP response URL must be a nonempty exact string"
            raise TypeError(message)
        if type(self.headers) is not tuple or any(
            type(item) is not tuple
            or len(item) != PAIR_SIZE
            or any(type(value) is not str for value in item)
            for item in self.headers
        ):
            message = "HTTP response headers must be exact string pairs"
            raise TypeError(message)
        if type(self.body) is not bytes:
            message = "HTTP response body must be exact bytes"
            raise TypeError(message)
        if type(self.redirects) is not tuple or any(
            type(value) is not str for value in self.redirects
        ):
            message = "HTTP response redirects must be exact strings"
            raise TypeError(message)
        if type(self.truncated) is not bool or type(self.complete) is not bool:
            message = "HTTP response completion facts must be exact Booleans"
            raise TypeError(message)


class GitHubPackagesTransport(Protocol):
    """Injectable bounded GitHub REST/npm registry GET transport."""

    def get(
        self,
        url: str,
        *,
        headers: tuple[tuple[str, str], ...],
        timeout: float,
        max_bytes: int,
    ) -> GitHubPackagesHttpResponse:
        """Fetch bounded data; strip credentials on cross-origin redirects."""


class GitHubPackagesHttpTransport:
    """Concrete bounded HTTPS transport with manual safe redirects."""

    _REDIRECT_STATUSES = frozenset({301, 302, 303, 307, 308})

    def __init__(
        self,
        opener: Callable[
            [urllib.request.Request, float, int],
            GitHubPackagesHttpResponse,
        ]
        | None = None,
        *,
        max_redirects: int = MAX_REDIRECTS,
    ) -> None:
        self._opener = opener or self._open_once
        self._max_redirects = _positive_exact_int(
            max_redirects,
            field="max_redirects",
        )

    def _open_once(
        self,
        request: urllib.request.Request,
        timeout: float,
        max_bytes: int,
    ) -> GitHubPackagesHttpResponse:
        class _NoRedirect(urllib.request.HTTPRedirectHandler):
            def redirect_request(
                self,
                *_args: object,
                **_kwargs: object,
            ) -> None:
                return None

        opener = urllib.request.build_opener(_NoRedirect)
        try:
            try:
                response = opener.open(request, timeout=timeout)
                status = response.status
            except urllib.error.HTTPError as error:
                response = error
                status = error.code
            with response:
                body = response.read(max_bytes + 1)
                return GitHubPackagesHttpResponse(
                    status=status,
                    url=response.geturl(),
                    headers=tuple(response.headers.items()),
                    body=body[:max_bytes],
                    truncated=len(body) > max_bytes,
                    complete=len(body) <= max_bytes,
                )
        except TimeoutError as error:
            raise GitHubPackagesTimeoutError(str(error)) from error
        except (http.client.HTTPException, OSError) as error:
            raise GitHubPackagesNetworkError(str(error)) from error

    def get(  # noqa: C901
        self,
        url: str,
        *,
        headers: tuple[tuple[str, str], ...],
        timeout: float,
        max_bytes: int,
    ) -> GitHubPackagesHttpResponse:
        """Fetch one bounded response after validating each credentialed hop."""
        _positive_exact_int(max_bytes, field="max_bytes")
        if type(timeout) not in {float, int} or timeout <= 0:
            message = "timeout must be positive"
            raise ValueError(message)
        if type(headers) is not tuple or any(
            type(pair) is not tuple
            or len(pair) != PAIR_SIZE
            or any(type(value) is not str for value in pair)
            for pair in headers
        ):
            message = "headers must be exact string pairs"
            raise TypeError(message)

        redirects: list[str] = []
        current_url = url
        current_headers = headers
        seen: set[str] = set()
        for _hop in range(self._max_redirects + 1):
            credentialed = any(
                name.lower() == "authorization" for name, _ in current_headers
            )
            _validate_transport_request(current_url, credentialed=credentialed)
            if current_url in seen:
                message = "redirect cycle rejected before credentialed request"
                raise GitHubPackagesPolicyError(message)
            seen.add(current_url)
            request = urllib.request.Request(  # noqa: S310
                current_url,
                headers=dict(current_headers),
                method="GET",
            )
            response = self._opener(request, float(timeout), max_bytes)
            if type(response) is not GitHubPackagesHttpResponse:
                message = "HTTP opener returned a malformed response"
                raise TypeError(message)
            if response.status not in self._REDIRECT_STATUSES:
                return GitHubPackagesHttpResponse(
                    status=response.status,
                    url=response.url,
                    headers=response.headers,
                    body=response.body,
                    redirects=tuple(redirects),
                    truncated=response.truncated,
                    complete=response.complete,
                )
            location = _header(response, "location")
            if location is None:
                message = "redirect location is missing"
                raise GitHubPackagesPolicyError(message)
            try:
                target_url = urllib.parse.urljoin(current_url, location)
            except ValueError as error:
                message = "redirect location is malformed"
                raise GitHubPackagesPolicyError(message) from error
            if len(redirects) >= self._max_redirects:
                message = "redirect limit exceeded before credentialed request"
                raise GitHubPackagesPolicyError(message)
            target_headers = redirect_headers(
                source_url=current_url,
                target_url=target_url,
                headers=current_headers,
            )
            if any(
                name.lower() == "authorization" for name, _ in target_headers
            ):
                _validate_transport_request(target_url, credentialed=True)
            redirects.append(target_url)
            current_url = target_url
            current_headers = target_headers
        message = "redirect limit exceeded"
        raise GitHubPackagesPolicyError(message)


@dataclass(frozen=True, slots=True)
class _Exchange:
    stage: str
    requested_url: str
    final_url: str | None
    redirects: tuple[str, ...]
    status: int | str
    selected_headers: tuple[tuple[str, str], ...]
    truncated: bool | None
    complete: bool | None
    body_sha256: str | None
    detail: str | None = None

    def to_document(self) -> dict[str, JsonValue]:
        return cast(
            "dict[str, JsonValue]",
            {
                "stage": self.stage,
                "requested-url": self.requested_url,
                "final-url": self.final_url,
                "redirects": list(self.redirects),
                "status": self.status,
                "selected-headers": [
                    [name, value] for name, value in self.selected_headers
                ],
                "truncated": self.truncated,
                "complete": self.complete,
                "body-sha256": self.body_sha256,
                "detail": self.detail,
            },
        )


class GitHubPackagesNetworkError(RuntimeError):
    """A bounded transport failed before a complete response."""


class GitHubPackagesTimeoutError(GitHubPackagesNetworkError):
    """A bounded transport timed out."""


class GitHubPackagesPolicyError(RuntimeError):
    """A response violated the approved HTTPS origin policy."""


def _positive_exact_int(value: object, *, field: str) -> int:
    if type(value) is not int:
        message = f"{field} must be a positive exact integer"
        raise TypeError(message)
    accepted = cast("int", value)
    if accepted <= 0:
        message = f"{field} must be positive"
        raise ValueError(message)
    return accepted


def validate_observation_bounds(
    *,
    timeout: int,
    max_bytes: int,
    max_pages: int,
) -> None:
    """Validate exact positive first-slice observation limits."""
    _positive_exact_int(timeout, field="timeout")
    _positive_exact_int(max_bytes, field="max_bytes")
    pages = _positive_exact_int(max_pages, field="max_pages")
    if pages > DEFAULT_MAX_PAGES:
        message = f"max_pages must be at most {DEFAULT_MAX_PAGES}"
        raise ValueError(message)


def github_package_versions_url(
    *,
    owner: str,
    package_name: str,
    page: int,
    per_page: int,
) -> str:
    """Return the exact escaped user-owned npm package versions URL."""
    if (
        owner != GITHUB_PACKAGES_OWNER
        or package_name != GITHUB_PACKAGES_PACKAGE
    ):
        message = (
            "GitHub Packages owner/package identity is outside first slice"
        )
        raise ValueError(message)
    _positive_exact_int(page, field="page")
    size = _positive_exact_int(per_page, field="per_page")
    if size != GITHUB_PAGE_SIZE:
        message = f"per_page must be exactly {GITHUB_PAGE_SIZE}"
        raise ValueError(message)
    resource_name = package_name.removeprefix(f"@{owner}/")
    encoded = urllib.parse.quote(resource_name, safe="")
    return (
        f"{GITHUB_API_ORIGIN}/users/{owner}/packages/npm/{encoded}/versions"
        f"?per_page={size}&page={page}"
    )


def _npm_package_path(package_name: str) -> str:
    if package_name != GITHUB_PACKAGES_PACKAGE:
        message = "npm package identity is outside first slice"
        raise ValueError(message)
    return urllib.parse.quote(package_name, safe="@")


def npm_package_metadata_url(package_name: str) -> str:
    """Return the supported GitHub Packages npm package metadata URL."""
    return f"{GITHUB_PACKAGES_REGISTRY}/{_npm_package_path(package_name)}"


def github_api_headers(token: str) -> tuple[tuple[str, str], ...]:
    """Return exact credential-bearing GitHub REST transport headers."""
    _token(token)
    return (
        ("Accept", "application/vnd.github+json"),
        ("Authorization", f"Bearer {token}"),
        ("X-GitHub-Api-Version", "2022-11-28"),
    )


def _github_transport_headers(token: str) -> tuple[tuple[str, str], ...]:
    return github_api_headers(token)


def _npm_transport_headers(
    token: str,
    *,
    tarball: bool = False,
) -> tuple[tuple[str, str], ...]:
    _token(token)
    return (
        (
            "Accept",
            "application/octet-stream"
            if tarball
            else "application/vnd.npm.install-v1+json, application/json",
        ),
        ("Accept-Encoding", "identity"),
        ("Authorization", f"Bearer {token}"),
        ("Cache-Control", "no-cache"),
    )


def _token(token: object) -> str:
    if type(token) is not str or not token:
        message = "GitHub Packages token must be a nonempty exact string"
        raise TypeError(message)
    return cast("str", token)


def redact_diagnostic(value: str, *, secrets: tuple[str, ...] = ()) -> str:
    """Redact credentials from retained command, response, and diagnostics."""
    redacted = value
    for secret in secrets:
        if secret:
            redacted = redacted.replace(secret, _REDACTED)
    for pattern in _TOKEN_PATTERNS:
        redacted = pattern.sub(rf"\1{_REDACTED}", redacted)
    return redacted


def _origin(url: str) -> tuple[str, str, int]:
    try:
        parsed = urllib.parse.urlparse(url)
        port = parsed.port
    except ValueError as error:
        message = "URL is malformed or has an invalid port"
        raise GitHubPackagesPolicyError(message) from error
    if (
        parsed.scheme != "https"
        or parsed.hostname is None
        or parsed.username is not None
        or parsed.password is not None
        or port not in {None, 443}
        or parsed.fragment
    ):
        message = "URL is outside the approved HTTPS origin policy"
        raise GitHubPackagesPolicyError(message)
    return ("https", parsed.hostname.lower(), 443)


def _validate_transport_request(url: str, *, credentialed: bool) -> None:
    _scheme, host, _port = _origin(url)
    if credentialed and host not in _CREDENTIAL_HOSTS:
        message = "credentialed request host is outside approved origins"
        raise GitHubPackagesPolicyError(message)


def redirect_headers(
    *,
    source_url: str,
    target_url: str,
    headers: tuple[tuple[str, str], ...],
) -> tuple[tuple[str, str], ...]:
    """Strip credentials whenever a redirect crosses an HTTPS origin."""
    if _origin(source_url) == _origin(target_url):
        return headers
    return tuple(
        (name, value)
        for name, value in headers
        if name.lower() not in {"authorization", "cookie", "npm-token"}
    )


def _allowed_url(url: str, *, stage: str) -> bool:
    try:
        _scheme, host, _port = _origin(url)
    except GitHubPackagesPolicyError:
        return False
    if stage == "rest":
        return host == "api.github.com"
    if stage in {"npm-metadata", "npm-tags"}:
        return host == "npm.pkg.github.com"
    return host in _ALLOWED_TARBALL_HOSTS


def _response_policy_ok(
    response: GitHubPackagesHttpResponse,
    *,
    requested_url: str,
    stage: str,
) -> bool:
    chain = (requested_url, *response.redirects, response.url)
    return all(_allowed_url(url, stage=stage) for url in chain)


def _header(
    response: GitHubPackagesHttpResponse,
    name: str,
) -> str | None:
    lowered = name.lower()
    for key, value in response.headers:
        if key.lower() == lowered:
            return value
    return None


def _identity_encoding(response: GitHubPackagesHttpResponse) -> bool:
    encoding = _header(response, "content-encoding")
    return encoding is None or encoding.lower() == "identity"


def _selected_headers(
    response: GitHubPackagesHttpResponse,
    *,
    secrets: tuple[str, ...] = (),
) -> tuple[tuple[str, str], ...]:
    return tuple(
        (name.lower(), redact_diagnostic(value, secrets=secrets))
        for name, value in sorted(
            response.headers,
            key=lambda item: item[0].lower(),
        )
        if name.lower() in _SELECTED_HEADERS
    )


def _body_digest(body: bytes) -> str:
    return f"sha256:{hashlib.sha256(body).hexdigest()}"


def _exchange(
    stage: str,
    requested_url: str,
    response: GitHubPackagesHttpResponse,
    *,
    detail: str | None = None,
    secrets: tuple[str, ...] = (),
) -> _Exchange:
    return _Exchange(
        stage=stage,
        requested_url=redact_diagnostic(requested_url, secrets=secrets),
        final_url=redact_diagnostic(response.url, secrets=secrets),
        redirects=tuple(
            redact_diagnostic(url, secrets=secrets)
            for url in response.redirects
        ),
        status=response.status,
        selected_headers=_selected_headers(response, secrets=secrets),
        truncated=response.truncated,
        complete=response.complete,
        body_sha256=_body_digest(response.body),
        detail=detail,
    )


def _synthetic_exchange(
    stage: str,
    requested_url: str,
    status: str,
) -> _Exchange:
    return _Exchange(
        stage=stage,
        requested_url=requested_url,
        final_url=None,
        redirects=(),
        status=status,
        selected_headers=(),
        truncated=None,
        complete=None,
        body_sha256=None,
    )


def _json(response: GitHubPackagesHttpResponse, *, field: str) -> JsonValue:
    try:
        return parse_json_strict(response.body)
    except (TypeError, ValueError) as error:
        message = f"{field} is malformed"
        raise ValueError(message) from error


def _status_classification(response: GitHubPackagesHttpResponse) -> str | None:
    if response.truncated or not response.complete:
        return "unknown"
    if response.status == HTTP_TOO_MANY_REQUESTS or (
        response.status >= HTTP_SERVER_ERROR
    ):
        return "unknown"
    if response.status in {HTTP_UNAUTHORIZED, HTTP_FORBIDDEN}:
        return "unprovable"
    return None


def _target_tag(target: str) -> str:
    if type(target) is not str or re.fullmatch(r"[0-9a-f]{40}", target) is None:
        message = "target must be a 40-character lowercase SHA"
        raise ValueError(message)
    return f"buddy-sha-{target}"


@dataclass(frozen=True, slots=True)
class GitHubPackagesActiveState:
    """Read-only facts; callers own Governance and desired-state admission."""

    package_control: PackageControlProof | None
    readback: DestinationReadback
    response_identity: str
    diagnostics: PublicationDiagnostics


def _active_get(  # noqa: PLR0913
    url: str,
    *,
    stage: str,
    token: str,
    transport: GitHubPackagesTransport,
    timeout: int,
    max_bytes: int,
) -> tuple[GitHubPackagesHttpResponse | None, _Exchange]:
    headers = (
        (
            *_github_transport_headers(token),
            ("Accept-Encoding", "identity"),
            ("Cache-Control", "no-cache"),
        )
        if stage == "rest"
        else _npm_transport_headers(token, tarball=stage == "tarball")
    )
    headers = tuple(
        (name, "Bearer " + token if name == "Authorization" else value)
        for name, value in headers
    )
    headers = redirect_headers(
        source_url=GITHUB_API_ORIGIN
        if stage == "rest"
        else GITHUB_PACKAGES_REGISTRY,
        target_url=url,
        headers=headers,
    )
    try:
        response = transport.get(
            url,
            headers=headers,
            timeout=float(timeout),
            max_bytes=max_bytes,
        )
    except GitHubPackagesPolicyError:
        return None, _synthetic_exchange(
            stage, redact_diagnostic(url, secrets=(token,)), "off-policy"
        )
    except (GitHubPackagesNetworkError, OSError):
        return None, _synthetic_exchange(
            stage, redact_diagnostic(url, secrets=(token,)), "network-error"
        )
    if type(response) is not GitHubPackagesHttpResponse:
        message = "GitHub Packages transport returned a malformed response"
        raise TypeError(message)
    failure = _status_classification(response)
    if len(response.body) > max_bytes:
        failure = "unknown"
    if (
        not _response_policy_ok(
            response,
            requested_url=url,
            stage=stage,
        )
        or not _identity_encoding(response)
        or (
            stage != "tarball"
            and any(hop != url for hop in (*response.redirects, response.url))
        )
    ):
        failure = "unprovable"
    if failure is None and response.status not in {HTTP_OK, HTTP_NOT_FOUND}:
        failure = "unprovable"
    exchange = _exchange(stage, url, response, detail=failure, secrets=(token,))
    return (response if failure is None else None), exchange


def _active_metadata(
    response: GitHubPackagesHttpResponse | None,
    exchange: _Exchange,
) -> tuple[dict[str, JsonValue] | None, str]:
    if response is None:
        return None, _active_read_failure(exchange)
    if response.status == HTTP_NOT_FOUND:
        return None, "absent"
    try:
        document = _json(response, field=exchange.stage)
    except ValueError:
        return None, "unprovable"
    if not isinstance(document, dict):
        return None, "unprovable"
    return document, "present"


def _active_read_failure(exchange: _Exchange) -> str:
    return (
        "unknown"
        if exchange.status == "network-error" or exchange.detail == "unknown"
        else "unprovable"
    )


def _active_package_control(
    document: dict[str, JsonValue] | None,
    *,
    observed_at: str,
    exchange: _Exchange,
    token: str,
) -> PackageControlProof | None:
    resource = GITHUB_PACKAGES_PACKAGE.removeprefix(
        f"@{GITHUB_PACKAGES_OWNER}/"
    )
    if (
        document is None
        or document.get("package_type") != "npm"
        or document.get("name") not in (resource, GITHUB_PACKAGES_PACKAGE)
    ):
        return None
    # The USER route establishes ownership when the nullable owner is absent.
    owner = GITHUB_PACKAGES_OWNER
    owner_document = document.get("owner")
    if owner_document is not None:
        if not isinstance(owner_document, dict):
            return None
        owner = owner_document.get("login")
        if (
            not isinstance(owner, str)
            or re.fullmatch(r"[A-Za-z0-9-]+", owner) is None
            or owner_document.get("type", "User") != "User"
        ):
            return None
    repository = document.get("repository")
    if not isinstance(repository, dict):
        return None
    full_name = repository.get("full_name")
    visibility = document.get("visibility")
    if (
        not isinstance(full_name, str)
        or re.fullmatch(r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+", full_name) is None
        or visibility not in ("public", "private", "internal")
        or any(
            token in value
            for value in (owner, full_name, cast("str", visibility))
        )
    ):
        return None
    return PackageControlProof(
        subject=PackageControlSubject(
            destination_id=GITHUB_PACKAGES_DESTINATION_ID,
            registry=GITHUB_PACKAGES_REGISTRY,
            normalized_package=GITHUB_PACKAGES_PACKAGE,
        ),
        observed_at=observed_at,
        endpoints=(exchange.requested_url,),
        facts=(
            ("exposed-access", ()),
            ("owner", (owner.lower(),)),
            ("repository-association", (full_name.lower(),)),
            ("visibility", (cast("str", visibility),)),
        ),
        response_digests=(
            (exchange.requested_url, canonical_sha256(exchange.to_document())),
        ),
    )


def _active_version_metadata(
    document: dict[str, JsonValue] | None,
    classification: str,
    version: str,
) -> tuple[dict[str, JsonValue] | None, str]:
    if document is None:
        return None, "unknown" if classification == "unknown" else "unprovable"
    versions = document.get("versions")
    if document.get("name") != GITHUB_PACKAGES_PACKAGE or not isinstance(
        versions, dict
    ):
        return None, "unprovable"
    if version not in versions:
        return None, "absent"
    manifest = versions[version]
    if not isinstance(manifest, dict):
        return None, "unprovable"
    return manifest, "present"


def _active_tag(
    document: dict[str, JsonValue] | None,
    tag: str,
    token: str,
) -> tuple[str, str | None]:
    if document is None or document.get("name") != GITHUB_PACKAGES_PACKAGE:
        return "unreadable", None
    tags = document.get("dist-tags")
    if not isinstance(tags, dict):
        return "unreadable", None
    if tag not in tags:
        return "absent", None
    version = tags[tag]
    if not isinstance(version, str) or not version or token in version:
        return "unreadable", None
    return "present", version


def read_github_packages_active_state(  # noqa: C901, PLR0913
    artifact: ReleaseArtifact,
    expectation: ArtifactExpectation,
    *,
    token: str,
    transport: GitHubPackagesTransport,
    observed_at: str,
    timeout: int = DEFAULT_TIMEOUT_SECONDS,
    metadata_limit_bytes: int = DEFAULT_METADATA_LIMIT_BYTES,
    tarball_limit_bytes: int = DEFAULT_TARBALL_LIMIT_BYTES,
    expanded_tarball_limit_bytes: int = DEFAULT_EXPANDED_TARBALL_LIMIT_BYTES,
) -> GitHubPackagesActiveState:
    """Read active package control, exact bytes/witness, and independent tag.

    The caller supplies qualified desired inputs and owns authority admission.
    Missing control proof is blocking; differing control facts are preserved.
    No active absence claims anything about deleted or restorable versions.
    """
    _token(token)
    for field, value in (
        ("timeout", timeout),
        ("metadata_limit_bytes", metadata_limit_bytes),
        ("tarball_limit_bytes", tarball_limit_bytes),
        ("expanded_tarball_limit_bytes", expanded_tarball_limit_bytes),
    ):
        _positive_exact_int(value, field=field)
    witness = _validate_artifact_expectation(expectation)
    if type(artifact) is not ReleaseArtifact:
        message = "active readback requires a ReleaseArtifact"
        raise TypeError(message)
    if (
        expectation.package_name != GITHUB_PACKAGES_PACKAGE
        or artifact.target != witness.target
        or artifact.witness_digest != _body_digest(expectation.witness_bytes)
    ):
        message = "active readback desired artifact/witness binding mismatch"
        raise ValueError(message)

    def read(
        url: str, stage: str
    ) -> tuple[GitHubPackagesHttpResponse | None, _Exchange]:
        return _active_get(
            url,
            stage=stage,
            token=token,
            transport=transport,
            timeout=timeout,
            max_bytes=tarball_limit_bytes
            if stage == "tarball"
            else metadata_limit_bytes,
        )

    package_url = (
        f"{GITHUB_API_ORIGIN}/users/{GITHUB_PACKAGES_OWNER}/packages/npm/"
        "hcoona-release-smoke-npm"
    )
    control_response, control_exchange = read(package_url, "rest")
    control_document, control_class = _active_metadata(
        control_response, control_exchange
    )
    control = _active_package_control(
        control_document,
        observed_at=observed_at,
        exchange=control_exchange,
        token=token,
    )
    diagnostics: list[str] = []
    if control is None:
        control_failure = (
            "unknown" if control_class == "unknown" else "unprovable"
        )
        diagnostics.append(f"package-control: {control_failure}")

    metadata_response, selected = read(
        npm_package_metadata_url(expectation.package_name), "npm-metadata"
    )
    package_document, metadata_class = _active_metadata(
        metadata_response, selected
    )
    exact_document, classification = _active_version_metadata(
        package_document, metadata_class, expectation.npm_package_version
    )
    exchanges = [selected]
    sha256 = sha512 = witness_digest = witness_target = None
    if exact_document is not None:
        classification = "unprovable"
        dist = exact_document.get("dist")
        tarball_url = dist.get("tarball") if isinstance(dist, dict) else None
        if (
            exact_document.get("name") == expectation.package_name
            and exact_document.get("version") == expectation.npm_package_version
            and isinstance(tarball_url, str)
            and _allowed_url(tarball_url, stage="tarball")
        ):
            tarball_response, selected = read(tarball_url, "tarball")
            exchanges.append(selected)
            if tarball_response is None:
                classification = _active_read_failure(selected)
            elif tarball_response.status == HTTP_OK:
                remote = _remote_tarball_observation(
                    tarball_response.body,
                    artifact=artifact,
                    expectation=expectation,
                    expanded_limit_bytes=expanded_tarball_limit_bytes,
                )
                sha256 = _body_digest(tarball_response.body)
                sha512 = remote.content_sha512
                witness_digest = remote.witness_digest
                witness_target = remote.witness_target
                classification = remote.classification
                if witness_digest is None or witness_target is None:
                    classification = "unprovable"
                elif classification == "exact-satisfied" and (
                    sha256 != artifact.content.content_sha256
                    or sha512 != artifact.content.content_sha512
                    or remote.byte_size != artifact.content.byte_size
                    or witness_digest != artifact.witness_digest
                    or witness_target != artifact.target
                ):
                    classification = "conflicting"
    if classification not in {"absent", "exact-satisfied"}:
        diagnostics.append(f"exact-version: {classification}")

    tag = _target_tag(artifact.target)
    tag_state, tag_version = _active_tag(package_document, tag, token)
    if tag_state == "unreadable":
        diagnostics.append("target-tag: unreadable")
    return GitHubPackagesActiveState(
        package_control=control,
        readback=DestinationReadback(
            package=expectation.package_name,
            version=expectation.npm_package_version,
            classification=classification,
            content_sha256=sha256,
            content_sha512=sha512,
            witness_digest=witness_digest,
            witness_target=witness_target,
            tag=tag,
            tag_state=tag_state,
            tag_version=tag_version,
            observed_at=observed_at,
            response_digests=tuple(
                sorted(
                    (
                        exchange.stage,
                        canonical_sha256(exchange.to_document()),
                    )
                    for exchange in exchanges
                )
            ),
        ),
        response_identity=canonical_sha256(selected.to_document()),
        diagnostics=PublicationDiagnostics(
            entries=tuple(diagnostics), truncated=False
        ),
    )


def _validate_first_slice_basis(
    snapshot: QualificationSnapshot,
    decision: QualificationDecision,
    artifact: ReleaseArtifact,
    expectation: ArtifactExpectation,
) -> tuple[ReleaseAttemptIdentity, DestinationProjection]:
    if type(snapshot) is not QualificationSnapshot:
        message = "GitHub Packages observation basis Snapshot is not exact"
        raise ValueError(message)
    if type(snapshot.subject) is not ReleaseAttemptIdentity:
        message = "GitHub Packages observation basis is not live"
        raise TypeError(message)
    attempt = snapshot.subject
    if (
        type(decision) is not QualificationDecision
        or type(artifact) is not ReleaseArtifact
        or decision.terminal_result != "success"
        or decision.subject != attempt
        or decision.qualification_snapshot_digest != snapshot.snapshot_digest
        or artifact.subject != attempt
        or artifact.purpose != "live-release"
        or artifact.target != snapshot.target
        or artifact.qualification_snapshot_digest != snapshot.snapshot_digest
        or artifact.artifact_digest not in decision.admitted_artifact_digests
    ):
        message = "GitHub Packages observation basis is not current"
        raise ValueError(message)
    if len(snapshot.destination_projections) != 1:
        message = "GitHub Packages observation basis projection is not exact"
        raise ValueError(message)
    projection = snapshot.destination_projections[0]
    coordinate = projection.coordinate
    if type(snapshot.nbgv) is not NbgvFacts:
        message = "GitHub Packages npm observation requires npm version facts"
        raise TypeError(message)
    if (
        projection.destination_id != GITHUB_PACKAGES_DESTINATION_ID
        or projection.registry != GITHUB_PACKAGES_REGISTRY
        or projection.observation_contract_id
        != GITHUB_PACKAGES_OBSERVATION_CONTRACT_ID
        or projection.operation != GITHUB_PACKAGES_OPERATION
        or coordinate.channel != "buddy"
        or coordinate.package_name != GITHUB_PACKAGES_PACKAGE
        or coordinate.native_version != snapshot.nbgv.npm_package_version
        or expectation.package_name != GITHUB_PACKAGES_PACKAGE
        or expectation.npm_package_version != coordinate.native_version
        or snapshot.target != attempt.execution.target
        or type(attempt.execution) is not BuddyExecutionIdentity
    ):
        message = "GitHub Packages observation basis is outside first slice"
        raise ValueError(message)
    _validate_artifact_expectation(expectation)
    return attempt, projection


def _validate_local_tarball_preconditions(
    *,
    tarball: Path,
    artifact: ReleaseArtifact,
    expectation: ArtifactExpectation,
    expanded_tarball_limit_bytes: int,
) -> None:
    _positive_exact_int(
        expanded_tarball_limit_bytes,
        field="expanded_tarball_limit_bytes",
    )
    try:
        status = tarball.lstat()
    except OSError as error:
        message = "publication tarball cannot be statted"
        raise ValueError(message) from error
    if not stat.S_ISREG(status.st_mode):
        message = "publication tarball must be a safe ordinary file"
        raise ValueError(message)
    content = tarball.read_bytes()
    if (
        len(content) != artifact.content.byte_size
        or status.st_size != artifact.content.byte_size
    ):
        message = "publication tarball size binding mismatch"
        raise ValueError(message)
    if f"sha256:{hashlib.sha256(content).hexdigest()}" != (
        artifact.content.content_sha256
    ):
        message = "publication tarball SHA-256 binding mismatch"
        raise ValueError(message)
    if f"sha512:{hashlib.sha512(content).hexdigest()}" != (
        artifact.content.content_sha512
    ):
        message = "publication tarball SHA-512 binding mismatch"
        raise ValueError(message)
    manifest = _qualify_npm_artifact_entries(
        content,
        _read_tarball(
            content,
            max_payload_bytes=expanded_tarball_limit_bytes,
        ),
        expectation,
    )
    if (
        manifest.basename != artifact.content.basename
        or manifest.byte_size != artifact.content.byte_size
        or manifest.sha256 != artifact.content.content_sha256
        or manifest.sha512 != artifact.content.content_sha512
        or f"sha256:{hashlib.sha256(expectation.witness_bytes).hexdigest()}"
        != artifact.witness_digest
    ):
        message = "publication tarball packed identity binding mismatch"
        raise ValueError(message)


__all__ = [  # noqa: RUF022
    "DEFAULT_MAX_PAGES",
    "DEFAULT_METADATA_LIMIT_BYTES",
    "DEFAULT_TARBALL_LIMIT_BYTES",
    "DEFAULT_TIMEOUT_SECONDS",
    "GITHUB_PACKAGES_DESTINATION_ID",
    "GITHUB_PACKAGES_DESTINATION_OPERATION_PROFILE_ID",
    "GITHUB_PACKAGES_NODE_VERSION",
    "GITHUB_PACKAGES_NPM_VERSION",
    "GITHUB_PACKAGES_OBSERVATION_CONTRACT_ID",
    "GITHUB_PACKAGES_OPERATION",
    "GITHUB_PACKAGES_PACKAGE",
    "GITHUB_PACKAGES_REGISTRY",
    "GitHubPackagesHttpResponse",
    "GitHubPackagesNetworkError",
    "GitHubPackagesPolicyError",
    "GitHubPackagesTimeoutError",
    "GitHubPackagesTransport",
    "github_api_headers",
    "github_packages_destination_operation_profile",
    "github_package_versions_url",
    "npm_package_metadata_url",
    "GitHubPackagesActiveState",
    "read_github_packages_active_state",
    "redact_diagnostic",
    "redirect_headers",
    "validate_observation_bounds",
    "validate_github_packages_publication_action",
]
