"""Bounded public Simple Index reads and one-shot Trusted Publisher HTTP."""

from __future__ import annotations

import base64
import http.client
import platform
import re
import ssl
from dataclasses import dataclass
from http import HTTPStatus
from typing import Protocol, cast
from urllib.parse import urlsplit

from packaging.utils import (
    canonicalize_name,
    parse_sdist_filename,
    parse_wheel_filename,
)
from packaging.version import Version

from three_workflow_delivery_v3.adapters.python import (
    PythonDistribution,
    PythonPackageTargetWitness,
    inspect_python_distribution,
)
from three_workflow_delivery_v3.canonical import (
    JsonValue,
    canonical_sha256,
    canonicalize,
    parse_json_strict,
)
from three_workflow_delivery_v3.repository.python_provider import (
    PYTHON_RELEASE_UNIT,
    python_digest,
    python_text,
    require_public_python_version,
)

MAX_FILE_BYTES = 8 * 1024 * 1024
MAX_INDEX_BYTES = 2 * 1024 * 1024
MAX_INDEX_FILES = 4096
MAX_RESPONSE_BYTES = 64 * 1024
HTTP_TIMEOUT_SECONDS = 30
POST_UPLOAD_OBSERVATION_POLICY: dict[str, JsonValue] = {
    "id": "python-post-upload-observation-v1",
    "maximum-index-reads": 6,
    "pending-spacing-seconds": 10,
    "admission-window-seconds": 60,
    "initial-bootstrap-pending": "404-only",
    "existing-project-pending": "unchanged-verified-inventory",
}
_DISTRIBUTION_COUNT = 2
_PYTHON_VERSION = "3.14.3"
_TLS_VERSION = "OpenSSL 3.5.5 27 Jan 2026"


@dataclass(frozen=True, slots=True)
class PythonRegistry:
    """Closed independent destination and credential bindings."""

    name: str

    def __post_init__(self) -> None:
        """Reject destination aliases or runtime origin overrides."""
        if self.name not in {"testpypi", "pypi"}:
            message = "unsupported Python registry"
            raise ValueError(message)

    @property
    def origin(self) -> str:
        """Return the public index and token-exchange origin."""
        return (
            "https://test.pypi.org"
            if self.name == "testpypi"
            else "https://pypi.org"
        )

    @property
    def upload_url(self) -> str:
        """Return the exact upload endpoint."""
        return (
            self.origin
            if self.name == "testpypi"
            else "https://upload.pypi.org"
        ) + "/legacy/"

    @property
    def file_host(self) -> str:
        """Return the independently closed public storage host."""
        return (
            "test-files.pythonhosted.org"
            if self.name == "testpypi"
            else "files.pythonhosted.org"
        )

    @property
    def channel(self) -> str:
        """Return this destination's only admitted channel."""
        return "buddy" if self.name == "testpypi" else "official"

    @property
    def environment(self) -> str:
        """Return the exact proposed GitHub Environment."""
        return f"workflow-delivery-v3-python-{self.name}"

    @property
    def index_url(self) -> str:
        """Return the supported normalized project Simple Index endpoint."""
        return f"{self.origin}/simple/{PYTHON_RELEASE_UNIT}/"

    @property
    def profile(self) -> dict[str, JsonValue]:
        """Close transport, TLS, origins and finite effect bounds."""
        return {
            "schema": (
                "workflow-delivery/v3/python-destination-operation-profile"
            ),
            "destination": f"python/{self.name}-v1",
            "project": PYTHON_RELEASE_UNIT,
            "channel": self.channel,
            "index": self.index_url,
            "file-host": self.file_host,
            "upload": self.upload_url,
            "mint-token": f"{self.origin}/_/oidc/mint-token",
            "oidc-audience": self.name,
            "environment": self.environment,
            "client": "stdlib/http.client.HTTPSConnection",
            "python": _PYTHON_VERSION,
            "tls": _TLS_VERSION,
            "tls-minimum": "TLSv1.2",
            "verify-certificates": True,
            "proxy": False,
            "redirects": 0,
            "retries": 0,
            "timeout-seconds": HTTP_TIMEOUT_SECONDS,
            "index-byte-budget": MAX_INDEX_BYTES,
            "index-file-budget": MAX_INDEX_FILES,
            "file-byte-budget": MAX_FILE_BYTES,
            "response-byte-budget": MAX_RESPONSE_BYTES,
            "upload-posts-per-file": 1,
            "post-upload-observation": POST_UPLOAD_OBSERVATION_POLICY,
            "multipart-mapping": "python-smoke-metadata-v1",
        }

    @property
    def profile_digest(self) -> str:
        """Return the exact operation identity required by native admission."""
        return canonical_sha256(self.profile)


def _https_url(url: str, *, host: str, prefix: str) -> None:
    parsed = urlsplit(url)
    if (
        parsed.scheme != "https"
        or parsed.netloc != host
        or parsed.username is not None
        or parsed.password is not None
        or parsed.query
        or parsed.fragment
        or not parsed.path.startswith(prefix)
        or any(c in url for c in "\r\n\\")
    ):
        message = "Python HTTP URL is outside the admitted origin policy"
        raise ValueError(message)


@dataclass(frozen=True, slots=True)
class PythonHttpResponse:
    """Bounded response facts; credential-bearing bodies stay local."""

    status: int
    body: bytes
    content_type: str
    started: float | None = None
    finished: float | None = None


class PythonHttpTransport(Protocol):
    """One request per call; implementations must not follow or resend."""

    def request(
        self,
        method: str,
        url: str,
        headers: dict[str, str],
        body: bytes | None,
        maximum_bytes: int,
    ) -> PythonHttpResponse:
        """Return one bounded response without replaying the request."""
        ...


class PythonHttpsTransport:
    """Pinned stdlib TLS transport without redirects, proxies or retries."""

    def __init__(self) -> None:
        """Require the runtime closed by the operation profile."""
        if (
            platform.python_version() != _PYTHON_VERSION
            or ssl.OPENSSL_VERSION != _TLS_VERSION
        ):
            message = "Python HTTP runtime differs from the admitted profile"
            raise ValueError(message)

    def request(
        self,
        method: str,
        url: str,
        headers: dict[str, str],
        body: bytes | None,
        maximum_bytes: int,
    ) -> PythonHttpResponse:
        """Execute once with verified TLS and bounded response storage."""
        parsed = urlsplit(url)
        if (
            parsed.scheme != "https"
            or parsed.hostname is None
            or parsed.username
            or parsed.password
            or parsed.fragment
        ):
            message = "invalid HTTPS request"
            raise ValueError(message)
        context = ssl.create_default_context()
        context.minimum_version = ssl.TLSVersion.TLSv1_2
        connection = http.client.HTTPSConnection(
            parsed.hostname,
            parsed.port or 443,
            timeout=HTTP_TIMEOUT_SECONDS,
            context=context,
        )
        try:
            path = parsed.path + ("?" + parsed.query if parsed.query else "")
            connection.request(method, path, body=body, headers=headers)
            response = connection.getresponse()
            content = response.read(maximum_bytes + 1)
            if len(content) > maximum_bytes:
                message = "Python HTTP response exceeds its byte budget"
                raise ValueError(message)
            return PythonHttpResponse(
                response.status, content, response.getheader("Content-Type", "")
            )
        finally:
            connection.close()


@dataclass(frozen=True, slots=True)
class PythonIndexObservation:
    """Complete bounded version observation and downloaded exact files."""

    registry: PythonRegistry
    version: str
    index_digest: str
    files: tuple[PythonDistribution, ...]
    classification: str
    index_response: PythonHttpResponse | None = None

    def to_document(self) -> dict[str, JsonValue]:
        """Retain the raw public index and exact download identities."""
        from three_workflow_delivery_v3.adapters.python_observation import (  # noqa: PLC0415 - response serialization avoids module cycle
            response_document,
        )

        return {
            "index-response": None
            if self.index_response is None
            else response_document(self.index_response),
            "registry": self.registry.name,
            "version": self.version,
            "index-digest": self.index_digest,
            "classification": self.classification,
            "files": [
                {
                    "variant": item.variant,
                    "filename": item.filename,
                    "digest": item.digest,
                }
                for item in self.files
            ],
        }


def read_python_index(  # noqa: C901, PLR0912, PLR0915
    registry: PythonRegistry,
    witness: PythonPackageTargetWitness,
    transport: PythonHttpTransport,
) -> PythonIndexObservation:
    """Resolve the entire native-equivalent version and inspect actual bytes.

    Partial is observable for wheel readback only; Release rejects it before
    action formation. Unknown or extra entries fail closed rather than being
    filtered into an apparently absent or complete version.
    """
    version = witness.nbgv.pep440_version
    require_public_python_version(witness.nbgv)
    response = transport.request(
        "GET",
        registry.index_url,
        {"Accept": "application/vnd.pypi.simple.v1+json"},
        None,
        MAX_INDEX_BYTES,
    )
    digest = python_digest(response.body)
    if response.status == HTTPStatus.NOT_FOUND:
        return PythonIndexObservation(
            registry, version, digest, (), "absent", response
        )
    if (
        response.status != HTTPStatus.OK
        or response.content_type.split(";", 1)[0].strip()
        != "application/vnd.pypi.simple.v1+json"
    ):
        message = "Python Simple Index response is unavailable or unsupported"
        raise ValueError(message)
    document = parse_json_strict(response.body)
    if (
        not isinstance(document, dict)
        or not isinstance(document.get("meta"), dict)
        or document["meta"].get("api-version")
        not in {"1.0", "1.1", "1.2", "1.3", "1.4"}
        or document.get("name") != PYTHON_RELEASE_UNIT
    ):
        message = "Python Simple Index project or API identity mismatch"
        raise ValueError(message)
    files = document.get("files")
    if not isinstance(files, list) or len(files) > MAX_INDEX_FILES:
        message = (
            "Python Simple Index file inventory is unavailable or excessive"
        )
        raise ValueError(message)
    selected: list[tuple[str, str, dict[str, JsonValue]]] = []
    seen: set[str] = set()
    for item in files:
        if not isinstance(item, dict):
            message = "invalid Python Simple Index entry"
            raise TypeError(message)
        filename = python_text(item.get("filename"))
        if filename in seen:
            message = "duplicate Python Simple Index filename"
            raise ValueError(message)
        seen.add(filename)
        variant = "wheel" if filename.endswith(".whl") else "sdist"
        identity = (
            parse_wheel_filename(filename)
            if variant == "wheel"
            else parse_sdist_filename(filename)
        )
        if identity[0] != canonicalize_name(PYTHON_RELEASE_UNIT):
            message = "foreign or unclassifiable Python Simple Index file"
            raise ValueError(message)
        if identity[1] == Version(version):
            selected.append((variant, filename, item))
    if not selected:
        return PythonIndexObservation(
            registry, version, digest, (), "absent", response
        )
    if len(selected) > _DISTRIBUTION_COUNT or len(
        {s[0] for s in selected}
    ) != len(selected):
        message = "Python version contains extra native files"
        raise ValueError(message)
    downloaded: list[PythonDistribution] = []
    for variant, filename, item in sorted(
        selected, key=lambda s: s[0] != "wheel"
    ):
        if item.get("yanked") is not False:
            message = "Python version has yanked or unknown state"
            raise ValueError(message)
        url = python_text(item.get("url"))
        _https_url(url, host=registry.file_host, prefix="/packages/")
        hashes = item.get("hashes")
        if (
            not isinstance(hashes, dict)
            or not isinstance(hashes.get("sha256"), str)
            or not re.fullmatch(r"[0-9a-f]{64}", cast("str", hashes["sha256"]))
        ):
            message = "Python index file lacks an exact SHA-256"
            raise ValueError(message)
        file_response = transport.request("GET", url, {}, None, MAX_FILE_BYTES)
        if file_response.status != HTTPStatus.OK or python_digest(
            file_response.body
        ) != "sha256:" + cast("str", hashes["sha256"]):
            message = "Python public download differs from the index"
            raise ValueError(message)
        downloaded.append(
            inspect_python_distribution(
                filename, file_response.body, variant, witness
            )
        )
    return PythonIndexObservation(
        registry,
        version,
        digest,
        tuple(downloaded),
        "complete" if len(downloaded) == _DISTRIBUTION_COUNT else "partial",
        response,
    )


def mint_python_token(
    registry: PythonRegistry,
    assertion: str,
    transport: PythonHttpTransport,
) -> str:
    """Exchange one in-memory GitHub assertion without retries or fallback."""
    python_text(assertion)
    response = transport.request(
        "POST",
        f"{registry.origin}/_/oidc/mint-token",
        {"Content-Type": "application/json"},
        canonicalize({"token": assertion}),
        MAX_RESPONSE_BYTES,
    )
    if response.status != HTTPStatus.OK:
        message = "Python Trusted Publisher token exchange failed"
        raise ValueError(message)
    document = parse_json_strict(response.body)
    if not isinstance(document, dict):
        message = "invalid Python token response"
        raise TypeError(message)
    token = python_text(document.get("token"))
    if not token.startswith("pypi-") or any(c in token for c in "\r\n"):
        message = "invalid Python short-lived token"
        raise ValueError(message)
    return token


@dataclass(frozen=True, slots=True)
class PythonUploadResponse:
    """Sanitized single-send response facts, never request credentials."""

    classification: str
    status: int | None
    response_digest: str | None
    finished: float | None = None


def upload_python_once(
    registry: PythonRegistry,
    distribution: PythonDistribution,
    token: str,
    transport: PythonHttpTransport,
) -> PythonUploadResponse:
    """POST one original file once; duplicates and redirects are failures."""
    require_public_python_version(distribution.witness.nbgv)
    inspect_python_distribution(
        distribution.filename,
        distribution.content,
        distribution.variant,
        distribution.witness,
    )
    if not token.startswith("pypi-") or any(c in token for c in "\r\n"):
        message = "Python upload requires a short-lived registry token"
        raise ValueError(message)
    boundary = "wdv3-" + distribution.digest.removeprefix("sha256:")
    fields = {
        ":action": "file_upload",
        "protocol_version": "1",
        "metadata_version": "2.4",
        "name": PYTHON_RELEASE_UNIT,
        "version": distribution.witness.nbgv.pep440_version,
        "filetype": "bdist_wheel"
        if distribution.variant == "wheel"
        else "sdist",
        "pyversion": "py3" if distribution.variant == "wheel" else "source",
        "requires_python": ">=3.14",
        "sha256_digest": distribution.digest.removeprefix("sha256:"),
    }
    parts = [
        (
            f"--{boundary}\r\nContent-Disposition: form-data; "
            f'name="{name}"\r\n\r\n{value}\r\n'
        ).encode()
        for name, value in fields.items()
    ]
    parts.extend(
        (
            (
                f"--{boundary}\r\nContent-Disposition: form-data; "
                f'name="content"; filename="{distribution.filename}"\r\n'
                "Content-Type: application/octet-stream\r\n\r\n"
            ).encode(),
            distribution.content,
            f"\r\n--{boundary}--\r\n".encode(),
        )
    )
    credentials = base64.b64encode(f"__token__:{token}".encode()).decode(
        "ascii"
    )
    try:
        response = transport.request(
            "POST",
            registry.upload_url,
            {
                "Authorization": f"Basic {credentials}",
                "Content-Type": f"multipart/form-data; boundary={boundary}",
            },
            b"".join(parts),
            MAX_RESPONSE_BYTES,
        )
    except Exception:  # noqa: BLE001 - a lost response may follow mutation
        return PythonUploadResponse("ambiguous", None, None)
    return PythonUploadResponse(
        "definitive-success"
        if response.status == HTTPStatus.OK
        else "definitive-non-success",
        response.status,
        python_digest(response.body),
        response.finished,
    )
