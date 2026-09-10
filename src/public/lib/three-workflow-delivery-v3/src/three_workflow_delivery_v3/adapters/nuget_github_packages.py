"""Bounded NuGet observation and one-shot GitHub Packages HTTP mechanisms.

This module supplies facts, never authorization or a publication verdict. The
caller must durably record its mutation marker before invoking publication.
An exact post-failure readback cannot turn an unsuccessful invocation into a
successful one. Native service acceptance remains a separate admission gate.
"""

from __future__ import annotations

import base64
import hashlib
import http.client
import inspect
import platform
import re
import ssl
import sys
from dataclasses import dataclass, field
from http import HTTPStatus
from pathlib import Path
from typing import TYPE_CHECKING, Protocol, cast
from urllib.parse import quote, unquote, urlsplit

from three_workflow_delivery_v3.canonical import (
    canonical_sha256,
    canonicalize,
    parse_json_strict,
)

if TYPE_CHECKING:
    from three_workflow_delivery_v3.canonical import JsonValue

NUGET_DESTINATION_ID = "nuget/github-packages-hcoona-three-v1"
NUGET_PROFILE_ID = "nuget/github-packages-http-put-v1"
NUGET_PACKAGE_ID = "Hcoona.ReleaseSmoke.GithubPackages"
NUGET_SERVICE_INDEX = "https://nuget.pkg.github.com/hcoona/index.json"
NUGET_ORIGIN = "https://nuget.pkg.github.com"
NUGET_REPOSITORY = "hcoona/three"
NUGET_PYTHON_VERSION = "3.13.12"
NUGET_HTTP_CLIENT_SHA256 = (
    "9a1c011d11aaea22df4b5e837274a25ac16f7d2f91759244feafdecce3ad22a1"
)
_API_ORIGIN = "https://api.github.com"
_TIMEOUT_SECONDS = 60
_METADATA_LIMIT = 8 * 1024 * 1024
_PACKAGE_LIMIT = 64 * 1024 * 1024
_RESPONSE_LIMIT = 64 * 1024
_MAX_VERSION_PAGES = 100
_PAGE_SIZE = 100
_ASCII_GRAPHIC_MIN = 33
_ASCII_GRAPHIC_MAX = 126


class NuGetAdapterError(ValueError):
    """The adapter cannot establish the required bounded facts."""


class NuGetTransportError(RuntimeError):
    """The HTTP exchange did not yield a complete admitted response."""


class NuGetAuthority(Protocol):
    """Official NuGet interpretation via a prebuilt, trusted helper."""

    def normalize_identity(
        self, package_id: str, version: str
    ) -> dict[str, JsonValue]:
        """Return official NuGet package and version identity projection."""
        ...

    def inspect_package(self, content: bytes) -> dict[str, JsonValue]:
        """Inspect immutable archive bytes with NuGet.Packaging."""
        ...

    def service_resources(self, index: bytes) -> dict[str, JsonValue]:
        """Interpret resource versions using NuGet.Protocol."""
        ...


@dataclass(frozen=True)
class NuGetIdentity:
    """Display values and native comparison identity, from official NuGet."""

    display_package_id: str
    display_version: str
    normalized_package_id: str
    normalized_version: str

    @property
    def coordinate(self) -> str:
        """Return the shared conflict/concurrency identity."""
        return f"{self.normalized_package_id}@{self.normalized_version}"

    @property
    def resource_key(self) -> str:
        """Return the destination-specific native-equivalent resource key."""
        return f"{NUGET_DESTINATION_ID}/{self.coordinate}"


def _identity(value: dict[str, JsonValue]) -> NuGetIdentity:
    names = (
        "displayPackageId",
        "displayVersion",
        "normalizedPackageId",
        "normalizedVersion",
    )
    if set(value) != set(names):
        msg = "Official identity has unexpected fields."
        raise NuGetAdapterError(msg)
    parts = tuple(value[name] for name in names)
    if any(not isinstance(part, str) or not part for part in parts):
        msg = "Official identity has missing values."
        raise NuGetAdapterError(msg)
    result = NuGetIdentity(*cast("tuple[str, str, str, str]", parts))
    # This is a transport-safety check, not a NuGet version grammar.
    if any(
        re.fullmatch(r"[A-Za-z0-9._+-]+", part) is None
        for part in (result.normalized_package_id, result.normalized_version)
    ):
        msg = "Native identity is not a safe URL segment."
        raise NuGetAdapterError(msg)
    return result


def normalize_nuget_identity(
    authority: NuGetAuthority, package_id: str, version: str
) -> NuGetIdentity:
    """Delegate native-equivalent comparison identity to official NuGet."""
    return _identity(authority.normalize_identity(package_id, version))


@dataclass(frozen=True)
class NuGetServiceResources:
    """Exact endpoints discovered through official NuGet resources."""

    package_base_address: str
    package_publish: str
    index_sha256: str


def _safe_url(url: str, *, origin: str) -> None:
    parsed = urlsplit(url)
    if (
        not isinstance(url, str)
        or not url.startswith(origin + "/")
        or parsed.scheme != "https"
        or parsed.netloc != urlsplit(origin).netloc
        or parsed.username is not None
        or parsed.password is not None
        or parsed.fragment
        or any(ord(character) < _ASCII_GRAPHIC_MIN for character in url)
        or "\\" in url
        or any(
            part in (".", "..")
            for part in unquote(parsed.path).replace("\\", "/").split("/")
        )
    ):
        msg = "HTTP resource origin or URL is not admitted."
        raise NuGetAdapterError(msg)


def discover_nuget_resources(
    authority: NuGetAuthority, index: bytes
) -> NuGetServiceResources:
    """Admit exactly the supported discovery result and credential origin."""
    document = authority.service_resources(index)
    if set(document) != {"packageBaseAddress", "packagePublish"}:
        msg = "Unexpected NuGet service resource projection."
        raise NuGetAdapterError(msg)
    base = document["packageBaseAddress"]
    publish = document["packagePublish"]
    if not isinstance(base, str) or not isinstance(publish, str):
        msg = "NuGet service resources are missing."
        raise NuGetAdapterError(msg)
    for endpoint in (base, publish):
        _safe_url(endpoint, origin=NUGET_ORIGIN)
        if not urlsplit(endpoint).path.startswith("/hcoona/"):
            msg = "NuGet resource has another owner scope."
            raise NuGetAdapterError(msg)
        if urlsplit(endpoint).query:
            msg = "NuGet discovery resource has a query."
            raise NuGetAdapterError(msg)
    return NuGetServiceResources(
        base.rstrip("/") + "/", publish, _sha256(index)
    )


@dataclass(frozen=True)
class NuGetHttpResponse:
    """Actual response bytes from one request, with no redirect following."""

    url: str
    status: int
    headers: tuple[tuple[str, str], ...] = field(repr=False)
    body: bytes = field(repr=False)

    def header(self, name: str) -> str | None:
        """Read a singleton header, rejecting ambiguous duplicates."""
        values = [value for key, value in self.headers if key.lower() == name]
        if len(values) > 1:
            msg = "Repeated response header is ambiguous."
            raise NuGetTransportError(msg)
        return values[0] if values else None


class NuGetReadTransport(Protocol):
    """One bounded read without automatic redirects or authentication."""

    def get(
        self,
        url: str,
        *,
        headers: tuple[tuple[str, str], ...],
        timeout: float,
        max_bytes: int,
    ) -> NuGetHttpResponse:
        """Return one complete read response."""
        ...


class NuGetHttpTransport:
    """Direct CPython HTTP transport with no replay-capable handler layer."""

    def request_once(  # noqa: PLR0913
        self,
        method: str,
        url: str,
        *,
        headers: tuple[tuple[str, str], ...],
        body: bytes | None,
        timeout: float,
        max_bytes: int,
    ) -> NuGetHttpResponse:
        """Open one TLS connection and issue at most one HTTP request.

        HTTPConnection.request -> _send_request -> endheaders -> _send_output
        sends header bytes and the immutable body once. Explicit connect and
        auto_open=0 preclude reconnect during send. getresponse only reads;
        100-continue processing reads another response, not another request.
        There are no redirect/authentication/proxy handlers or retry loops.
        """
        origin = (
            _API_ORIGIN if url.startswith(_API_ORIGIN + "/") else NUGET_ORIGIN
        )
        _safe_url(url, origin=origin)
        if method not in ("GET", "PUT") or timeout <= 0 or max_bytes <= 0:
            msg = "Invalid bounded HTTP request."
            raise NuGetAdapterError(msg)
        if len({name.lower() for name, _ in headers}) != len(headers):
            msg = "Duplicate HTTP request header."
            raise NuGetAdapterError(msg)
        if any(
            "\r" in item or "\n" in item for pair in headers for item in pair
        ):
            msg = "Invalid HTTP request header."
            raise NuGetAdapterError(msg)
        parsed = urlsplit(url)
        connection = http.client.HTTPSConnection(
            cast("str", parsed.hostname),
            timeout=timeout,
            context=ssl.create_default_context(),
        )
        connection.set_debuglevel(0)
        connection.auto_open = 0
        request_target = parsed.path or "/"
        if parsed.query:
            request_target += "?" + parsed.query
        try:
            connection.connect()
            connection.request(
                method,
                request_target,
                body=body,
                headers=dict(headers),
                encode_chunked=False,
            )
            with connection.getresponse() as response:
                result = NuGetHttpResponse(
                    url,
                    response.status,
                    tuple(response.getheaders()),
                    response.read(max_bytes + 1),
                )
                _validate_response_body(result, max_bytes=max_bytes)
                return result
        except (OSError, http.client.HTTPException) as error:
            raise NuGetTransportError(type(error).__name__) from None
        finally:
            connection.close()

    def get(
        self,
        url: str,
        *,
        headers: tuple[tuple[str, str], ...],
        timeout: float,
        max_bytes: int,
    ) -> NuGetHttpResponse:
        """Issue one read; a redirect is a response, never another request."""
        return self.request_once(
            "GET",
            url,
            headers=headers,
            body=None,
            timeout=timeout,
            max_bytes=max_bytes,
        )


def _validate_response_body(
    response: NuGetHttpResponse, *, max_bytes: int
) -> None:
    if len(response.body) > max_bytes:
        msg = "Response exceeds the byte bound."
        raise NuGetTransportError(msg)
    if response.header("content-encoding") not in (None, "identity"):
        msg = "Encoded response bytes are not admitted."
        raise NuGetTransportError(msg)
    length = response.header("content-length")
    transfer = response.header("transfer-encoding")
    if transfer is not None and transfer.lower() != "chunked":
        msg = "Unknown response transfer encoding."
        raise NuGetTransportError(msg)
    if transfer is not None and length is not None:
        msg = "Ambiguous response framing."
        raise NuGetTransportError(msg)
    if length is not None and (
        not length.isascii()
        or not length.isdecimal()
        or int(length) != len(response.body)
    ):
        msg = "Response length does not match actual bytes."
        raise NuGetTransportError(msg)


def _sha256(content: bytes) -> str:
    return hashlib.sha256(content).hexdigest()


def _token(token: str) -> str:
    if not token or any(
        ord(char) < _ASCII_GRAPHIC_MIN or ord(char) > _ASCII_GRAPHIC_MAX
        for char in token
    ):
        msg = "A preconfigured repository token is required."
        raise NuGetAdapterError(msg)
    return token


def _headers(token: str, *, api: bool = False) -> tuple[tuple[str, str], ...]:
    credential = _token(token)
    common = (
        ("Accept-Encoding", "identity"),
        ("Connection", "close"),
        ("User-Agent", "workflow-delivery-v3-nuget-http-put-v1"),
    )
    if api:
        return (
            *common,
            ("Authorization", "Bearer " + credential),
            ("Accept", "application/vnd.github+json"),
            ("X-GitHub-Api-Version", "2022-11-28"),
        )
    basic = base64.b64encode(("hcoona:" + credential).encode("ascii")).decode(
        "ascii"
    )
    return (*common, ("Authorization", "Basic " + basic))


def nuget_operation_profile(
    resources: NuGetServiceResources,
) -> dict[str, JsonValue]:
    """Bind the exact running transport and immutable publication protocol.

    Native acceptance and Live compare this entire canonical document. A
    platform, TLS, code, endpoint, or runtime change requires new admission.
    Credential values and their digests never enter this document.
    """
    runtime = platform.python_version()
    source = _sha256(inspect.getsource(http.client).encode("utf-8"))
    if (
        sys.implementation.name != "cpython"
        or runtime != NUGET_PYTHON_VERSION
        or source != NUGET_HTTP_CLIENT_SHA256
    ):
        msg = "The publication transport runtime is not pinned."
        raise NuGetAdapterError(msg)
    return _nuget_profile_document(
        resources.package_publish,
        {
            "executableSha256": _sha256(Path(sys.executable).read_bytes()),
            "runtimeBuild": sys.version,
            "sslSourceSha256": _sha256(inspect.getsource(ssl).encode("utf-8")),
            "platform": platform.system(),
            "tlsLibrary": ssl.OPENSSL_VERSION,
            "adapterSha256": _sha256(Path(__file__).read_bytes()),
        },
    )


def _nuget_profile_document(
    package_publish: str, runtime_facts: dict[str, str]
) -> dict[str, JsonValue]:
    _safe_url(package_publish, origin=NUGET_ORIGIN)
    return {
        "profileId": NUGET_PROFILE_ID,
        "destinationId": NUGET_DESTINATION_ID,
        "operation": "create-active-nuget-version-once",
        "serviceIndex": NUGET_SERVICE_INDEX,
        "packagePublish": package_publish,
        "resourceType": "PackagePublish/2.0.0",
        "runtime": "CPython@" + NUGET_PYTHON_VERSION,
        "executableSha256": runtime_facts["executableSha256"],
        "runtimeBuild": runtime_facts["runtimeBuild"],
        "sslSourceSha256": runtime_facts["sslSourceSha256"],
        "platform": runtime_facts["platform"],
        "tlsLibrary": runtime_facts["tlsLibrary"],
        "httpClientSha256": NUGET_HTTP_CLIENT_SHA256,
        "adapterSha256": runtime_facts["adapterSha256"],
        "method": "PUT",
        "authentication": {
            "Authorization": "Basic base64(hcoona:GITHUB_TOKEN)",
            "X-NuGet-ApiKey": "GITHUB_TOKEN",
            "credentialOrigin": NUGET_ORIGIN,
        },
        "headers": {
            "Accept-Encoding": "identity",
            "Connection": "close",
            "User-Agent": "workflow-delivery-v3-nuget-http-put-v1",
            "Content-Type": (
                "multipart/form-data; boundary=wdv3-nuget-{archive-sha256}"
            ),
            "Content-Length": "exact multipart body byte length",
        },
        "multipart": {
            "parts": 1,
            "name": "package",
            "filename": "package.nupkg",
            "contentType": "application/octet-stream",
            "bytes": "unchanged-authorized-nupkg",
        },
        "timeoutSeconds": _TIMEOUT_SECONDS,
        "timeoutKind": "socket-operation",
        "maximumPackageBytes": _PACKAGE_LIMIT,
        "maximumResponseBytes": _RESPONSE_LIMIT,
        "transportRetries": 0,
        "applicationRetries": 0,
        "redirects": 0,
        "authenticationResubmissions": 0,
        "automaticProxy": False,
        "duplicateSkipping": False,
        "successStatus": 201,
        "failureReadback": "diagnostic-only-never-success",
        "sourceBasis": "https://github.com/python/cpython/blob/v3.13.12/Lib/http/client.py",
    }


def validate_nuget_operation_profile(document: JsonValue) -> None:
    """Validate imported profile shape without replacing retained host facts.

    This is protocol representation admission, not native or runtime admission.
    Execution must compare the complete profile with the actual running adapter.
    """
    if type(document) is not dict:
        msg = "NuGet operation profile must be an object."
        raise NuGetAdapterError(msg)
    runtime_facts: dict[str, str] = {}
    for name in (
        "executableSha256",
        "runtimeBuild",
        "sslSourceSha256",
        "platform",
        "tlsLibrary",
        "adapterSha256",
    ):
        value = document.get(name)
        if type(value) is not str or not value:
            msg = "NuGet operation profile runtime fact is incomplete."
            raise NuGetAdapterError(msg)
        if (
            name.endswith("Sha256")
            and re.fullmatch(r"[0-9a-f]{64}", value) is None
        ):
            msg = "NuGet operation profile source digest is invalid."
            raise NuGetAdapterError(msg)
        runtime_facts[name] = value
    endpoint = document.get("packagePublish")
    if type(endpoint) is not str:
        msg = "NuGet operation profile endpoint is missing."
        raise NuGetAdapterError(msg)
    expected = _nuget_profile_document(endpoint, runtime_facts)
    if canonicalize(document) != canonicalize(expected):
        msg = "NuGet operation profile is outside the closed HTTP contract."
        raise NuGetAdapterError(msg)


@dataclass(frozen=True)
class NuGetPublicationInvocation:
    """Invocation facts; definitive success still requires exact readback."""

    profile_sha256: str
    package_sha256: str
    request_body_sha256: str
    response: NuGetHttpResponse | None
    error_kind: str | None

    @property
    def definitive_success(self) -> bool:
        """Recognize only the profile's complete successful HTTP response."""
        return (
            self.error_kind is None
            and self.response is not None
            and self.response.status == HTTPStatus.CREATED
        )

    @property
    def possibly_mutated(self) -> bool:
        """Keep every non-success conservative, including explicit rejection."""
        return not self.definitive_success


def _multipart(package: bytes) -> tuple[bytes, str]:
    if not package or len(package) > _PACKAGE_LIMIT:
        msg = "The package is empty or exceeds the byte bound."
        raise NuGetAdapterError(msg)
    boundary = "wdv3-nuget-" + _sha256(package)
    if boundary.encode("ascii") in package:
        msg = "The package collides with its multipart boundary."
        raise NuGetAdapterError(msg)
    prefix = (
        f"--{boundary}\r\n"
        'Content-Disposition: form-data; name="package"; '
        'filename="package.nupkg"\r\n'
        "Content-Type: application/octet-stream\r\n\r\n"
    ).encode("ascii")
    body = prefix + package + f"\r\n--{boundary}--\r\n".encode("ascii")
    return body, boundary


def publish_nuget_once(
    *,
    resources: NuGetServiceResources,
    package: bytes,
    token: str,
    expected_profile_sha256: str,
    expected_package_sha256: str,
) -> NuGetPublicationInvocation:
    """Submit one exact archive after the caller's durable mutation marker.

    This primitive has no retry, alternate endpoint, observer, helper execution,
    or success-shaped duplicate path. The parent must inspect/qualify the
    artifact and bind identity and witness before invoking this mechanism.
    """
    profile_digest = canonical_sha256(nuget_operation_profile(resources))
    if (
        profile_digest != expected_profile_sha256
        or _sha256(package) != expected_package_sha256
    ):
        msg = "Publication profile or package bytes changed."
        raise NuGetAdapterError(msg)
    body, boundary = _multipart(package)
    headers = (
        *_headers(token),
        ("X-NuGet-ApiKey", _token(token)),
        ("Content-Type", "multipart/form-data; boundary=" + boundary),
        ("Content-Length", str(len(body))),
    )
    response = None
    error_kind = None
    try:
        response = NuGetHttpTransport().request_once(
            "PUT",
            resources.package_publish,
            headers=headers,
            body=body,
            timeout=_TIMEOUT_SECONDS,
            max_bytes=_RESPONSE_LIMIT,
        )
    except NuGetTransportError as error:
        error_kind = str(error)
    return NuGetPublicationInvocation(
        profile_digest,
        expected_package_sha256,
        _sha256(body),
        response,
        error_kind,
    )


@dataclass(frozen=True)
class NuGetPackageInspection:
    """Actual unchanged archive and officially extracted native facts."""

    identity: NuGetIdentity
    content: bytes = field(repr=False)
    witness: bytes = field(repr=False)
    official_facts: dict[str, JsonValue] = field(repr=False)

    @property
    def sha256(self) -> str:
        """Return the whole-archive SHA-256 digest."""
        return _sha256(self.content)

    @property
    def sha512(self) -> str:
        """Return the whole-archive SHA-512 digest."""
        return hashlib.sha512(self.content).hexdigest()

    @property
    def witness_sha256(self) -> str:
        """Return the digest of the actual extracted witness bytes."""
        return _sha256(self.witness)


def inspect_nuget_package(
    authority: NuGetAuthority, content: bytes
) -> NuGetPackageInspection:
    """Delegate package and witness extraction to official NuGet.Packaging."""
    if not content or len(content) > _PACKAGE_LIMIT:
        msg = "The package is empty or exceeds the byte bound."
        raise NuGetAdapterError(msg)
    facts = authority.inspect_package(content)
    identity = facts.get("identity")
    witness_base64 = facts.get("witnessBase64")
    if not isinstance(identity, dict) or not isinstance(witness_base64, str):
        msg = "Official package inspection is incomplete."
        raise NuGetAdapterError(msg)
    try:
        witness = base64.b64decode(witness_base64, validate=True)
        parsed = parse_json_strict(witness)
    except ValueError:
        msg = "Official package witness is invalid."
        raise NuGetAdapterError(msg) from None
    if not isinstance(parsed, dict):
        msg = "Official package witness is not an object."
        raise NuGetAdapterError(msg)
    return NuGetPackageInspection(_identity(identity), content, witness, facts)


@dataclass(frozen=True)
class NuGetActiveState:
    """Complete current active facts, without inferred authority or grants."""

    identity: NuGetIdentity
    resources: NuGetServiceResources
    package_control: dict[str, JsonValue]
    active_versions: tuple[NuGetIdentity, ...]
    github_versions: tuple[dict[str, JsonValue], ...]
    package: NuGetPackageInspection | None
    exchanges: tuple[NuGetHttpResponse, ...]


def _object(content: bytes) -> dict[str, JsonValue]:
    result = parse_json_strict(content)
    if not isinstance(result, dict):
        msg = "Expected an object response."
        raise NuGetAdapterError(msg)
    return result


def _package_control(
    document: dict[str, JsonValue], *, package_id: str
) -> None:
    repository = document.get("repository")
    owner = document.get("owner")
    if (
        document.get("package_type") != "nuget"
        or not isinstance(document.get("name"), str)
        or cast("str", document["name"]).lower() != package_id.lower()
        or document.get("visibility") not in ("public", "private", "internal")
        or not isinstance(repository, dict)
        or repository.get("full_name") != NUGET_REPOSITORY
        or not isinstance(document.get("id"), int)
        or isinstance(document.get("id"), bool)
    ):
        msg = "GitHub package-control facts do not match."
        raise NuGetAdapterError(msg)
    # The authenticated /users/hcoona route establishes owner when omitted.
    if owner is not None and (
        not isinstance(owner, dict)
        or owner.get("login") != "hcoona"
        or owner.get("type", "User") != "User"
    ):
        msg = "GitHub package owner does not match."
        raise NuGetAdapterError(msg)


def read_nuget_active_state(  # noqa: C901, PLR0912, PLR0915
    *,
    transport: NuGetReadTransport,
    authority: NuGetAuthority,
    token: str,
    package_id: str,
    version: str,
) -> NuGetActiveState:
    """Read supported complete inventories and the actual selected archive.

    Reads deliberately do not follow redirects. An unadmitted storage origin
    blocks byte proof; it never receives a credential. Native normalization
    reconciles the GitHub active inventory and PackageBaseAddress inventory.
    Public visibility and repository association do not prove Actions grants;
    all exposed package-control fields are retained without such inference.
    """
    if package_id.lower() != NUGET_PACKAGE_ID.lower():
        msg = "The selected NuGet package is not admitted."
        raise NuGetAdapterError(msg)
    identity = normalize_nuget_identity(authority, package_id, version)
    exchanges: list[NuGetHttpResponse] = []

    def read(
        url: str, *, api: bool = False, package: bool = False
    ) -> NuGetHttpResponse:
        _safe_url(url, origin=_API_ORIGIN if api else NUGET_ORIGIN)
        bound = _PACKAGE_LIMIT if package else _METADATA_LIMIT
        response = transport.get(
            url,
            headers=_headers(token, api=api),
            timeout=_TIMEOUT_SECONDS,
            max_bytes=bound,
        )
        if response.url != url:
            msg = "Read transport followed an unadmitted redirect."
            raise NuGetAdapterError(msg)
        exchanges.append(response)
        _validate_response_body(response, max_bytes=bound)
        if response.status != HTTPStatus.OK:
            msg = f"NuGet observation HTTP status {response.status}."
            raise NuGetAdapterError(msg)
        return response

    index = read(NUGET_SERVICE_INDEX)
    resources = discover_nuget_resources(authority, index.body)
    control_url = f"{_API_ORIGIN}/users/hcoona/packages/nuget/" + quote(
        package_id, safe=""
    )
    control = _object(read(control_url, api=True).body)
    _package_control(control, package_id=package_id)
    github_versions: list[dict[str, JsonValue]] = []
    native_ids: set[int] = set()
    api_coordinates: set[str] = set()
    for page in range(1, _MAX_VERSION_PAGES + 1):
        url = (
            f"{control_url}/versions?state=active"
            f"&per_page={_PAGE_SIZE}&page={page}"
        )
        response = read(url, api=True)
        items = parse_json_strict(response.body)
        if not isinstance(items, list) or len(items) > _PAGE_SIZE:
            msg = "Invalid GitHub active version inventory."
            raise NuGetAdapterError(msg)
        for item in items:
            if not isinstance(item, dict) or not isinstance(
                item.get("name"), str
            ):
                msg = "Invalid GitHub active version entry."
                raise NuGetAdapterError(msg)
            native_id = item.get("id")
            if (
                type(native_id) is not int
                or native_id <= 0
                or native_id in native_ids
            ):
                msg = "Duplicate or invalid active version object."
                raise NuGetAdapterError(msg)
            native_ids.add(native_id)
            native = normalize_nuget_identity(
                authority, package_id, cast("str", item["name"])
            )
            if native.coordinate in api_coordinates:
                msg = "Duplicate native-equivalent active coordinate."
                raise NuGetAdapterError(msg)
            api_coordinates.add(native.coordinate)
            github_versions.append(item)
        if len(items) < _PAGE_SIZE:
            if re.search(
                r'rel="?next"?(?:[;,]|$)', response.header("link") or ""
            ):
                msg = "Unexpected pagination after terminal page."
                raise NuGetAdapterError(msg)
            break
    else:
        msg = "GitHub active inventory exceeds the page bound."
        raise NuGetAdapterError(msg)

    package_root = (
        resources.package_base_address
        + quote(identity.normalized_package_id, safe="")
        + "/"
    )
    versions_document = _object(read(package_root + "index.json").body)
    versions = versions_document.get("versions")
    if (
        not isinstance(versions, list)
        or len(versions) > _MAX_VERSION_PAGES * _PAGE_SIZE
    ):
        msg = "Invalid NuGet package version inventory."
        raise NuGetAdapterError(msg)
    natives: list[NuGetIdentity] = []
    for item in versions:
        if not isinstance(item, str):
            msg = "NuGet inventory has a non-string version."
            raise NuGetAdapterError(msg)
        natives.append(normalize_nuget_identity(authority, package_id, item))
    coordinates = {native.coordinate for native in natives}
    if len(coordinates) != len(natives) or coordinates != api_coordinates:
        msg = "Active inventories disagree or duplicate native identity."
        raise NuGetAdapterError(msg)
    inspection = None
    if identity.coordinate in coordinates:
        native_version = quote(identity.normalized_version, safe="")
        basename = quote(
            f"{identity.normalized_package_id}.{identity.normalized_version}.nupkg",
            safe="",
        )
        package_url = f"{package_root}{native_version}/{basename}"
        inspection = inspect_nuget_package(
            authority, read(package_url, package=True).body
        )
        if inspection.identity.coordinate != identity.coordinate:
            msg = "Downloaded package has another native identity."
            raise NuGetAdapterError(msg)
    return NuGetActiveState(
        identity,
        resources,
        control,
        tuple(natives),
        tuple(github_versions),
        inspection,
        tuple(exchanges),
    )
