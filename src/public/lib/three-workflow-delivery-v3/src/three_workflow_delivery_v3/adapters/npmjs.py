"""Credential-free npmjs observation Adapter for the first v3 slice."""

from __future__ import annotations

import hashlib
import http.client
import urllib.error
import urllib.parse
import urllib.request
from dataclasses import dataclass
from typing import TYPE_CHECKING, Any, Protocol, cast, override

from three_workflow_delivery_v3.adapters.node import (
    _PACKED_WITNESS_PATH,
    ArtifactExpectation,
    _qualify_npm_artifact_entries,
    _read_tarball,
    _TarballExpansionLimitError,
    _validate_artifact_expectation,
    package_target_witness_from_document,
)
from three_workflow_delivery_v3.canonical import (
    canonical_sha256,
    parse_canonical_json,
    parse_json_strict,
)
from three_workflow_delivery_v3.records.release import (
    NPMJS_OBSERVATION_CONTRACT_ID,
    NPMJS_OBSERVER_PRODUCER,
    ObservationRequestFacts,
    ObservationResponseFacts,
    ObservationValue,
    ProjectionObservation,
    QualificationDecision,
    QualificationSnapshot,
    ReleaseArtifact,
    SimulationBinding,
)
from three_workflow_delivery_v3.repository.descriptors import (
    FIRST_SLICE_PACKAGE,
)

if TYPE_CHECKING:
    from three_workflow_delivery_v3.canonical import JsonValue

NPMJS_REGISTRY_ORIGIN = "https://registry.npmjs.org"
NPMJS_EXACT_OWNER = "scope:@hcoona"
DEFAULT_METADATA_LIMIT_BYTES = 1_000_000
DEFAULT_TARBALL_LIMIT_BYTES = 25_000_000
DEFAULT_EXPANDED_TARBALL_LIMIT_BYTES = 100_000_000
DEFAULT_TIMEOUT_SECONDS = 20.0
DEFAULT_REDIRECT_LIMIT = 5
HTTP_OK = 200
HTTP_MULTIPLE_CHOICES = 300
HTTP_BAD_REDIRECT_LIMIT = 400
HTTP_BAD_REQUEST = 400
HTTP_UNAUTHORIZED = 401
HTTP_FORBIDDEN = 403
HTTP_NOT_FOUND = 404
HTTP_TOO_MANY_REQUESTS = 429
HTTP_SERVER_ERROR = 500
_METADATA_ACCEPT = "application/vnd.npm.install-v1+json, application/json"
_TARBALL_ACCEPT = "application/octet-stream"
_SELECTED_RESPONSE_HEADERS = frozenset(
    {
        "cache-control",
        "content-encoding",
        "content-length",
        "content-type",
        "etag",
        "last-modified",
    }
)


@dataclass(frozen=True, slots=True)
class HttpResponse:
    """Bounded HTTP response facts supplied by an injectable transport."""

    status: int
    url: str
    headers: tuple[tuple[str, str], ...]
    body: bytes
    redirects: tuple[str, ...] = ()
    truncated: bool = False


class HttpTransport(Protocol):
    """Credential-free GET transport used by npmjs observation."""

    def get(
        self,
        url: str,
        *,
        headers: tuple[tuple[str, str], ...],
        timeout: float,
        max_bytes: int,
    ) -> HttpResponse:
        """Fetch one URL with bounded time and bytes."""


class NpmjsNetworkError(RuntimeError):
    """Network transport could not return a bounded registry response."""


class NpmjsTimeoutError(NpmjsNetworkError):
    """Registry transport timed out."""


class NpmjsTruncatedResponseError(NpmjsNetworkError):
    """Registry response exceeded the caller's byte bound."""


class NpmjsPolicyError(RuntimeError):
    """Registry response violates the credential-free npmjs policy."""


class _NoRedirectHandler(urllib.request.HTTPRedirectHandler):
    @override
    def redirect_request(  # type: ignore[override]
        self,
        req: urllib.request.Request,
        fp: Any,
        code: int,
        msg: str,
        headers: Any,
        newurl: str,
    ) -> None:
        del req, fp, code, msg, headers, newurl


class StdlibHttpTransport:
    """urllib-based transport with no credentials, cookies, or npm config."""

    def get(
        self,
        url: str,
        *,
        headers: tuple[tuple[str, str], ...],
        timeout: float,
        max_bytes: int,
    ) -> HttpResponse:
        """Fetch one bounded HTTPS registry response."""
        _validate_size_limit(max_bytes, field="max_bytes")
        _validate_registry_url(url)
        request_headers = dict(headers)
        redirects: list[str] = []
        current_url = url
        opener = urllib.request.build_opener(
            urllib.request.ProxyHandler({}),
            _NoRedirectHandler,
        )
        try:
            while True:
                request = urllib.request.Request(  # noqa: S310
                    current_url,
                    headers=request_headers,
                    method="GET",
                )
                try:
                    with opener.open(request, timeout=timeout) as response:
                        _validate_registry_url(response.url)
                        body = _read_complete_bounded_body(
                            response,
                            max_bytes=max_bytes,
                        )
                        if len(body) > max_bytes:
                            message = "npmjs response exceeded size bound"
                            raise NpmjsTruncatedResponseError(message)
                        return HttpResponse(
                            status=response.status,
                            url=response.url,
                            headers=tuple(response.headers.items()),
                            body=body,
                            redirects=tuple(redirects),
                        )
                except urllib.error.HTTPError as error:
                    if _is_redirect_status(error.code):
                        location = error.headers.get("location")
                        if location is None:
                            message = "npmjs redirect omitted Location"
                            raise NpmjsPolicyError(message) from error
                        if len(redirects) >= DEFAULT_REDIRECT_LIMIT:
                            message = "npmjs redirect limit exceeded"
                            raise NpmjsPolicyError(message) from error
                        next_url = urllib.parse.urljoin(current_url, location)
                        _validate_registry_url(next_url)
                        redirects.append(next_url)
                        current_url = next_url
                        continue
                    body = _read_complete_bounded_body(
                        error,
                        max_bytes=max_bytes,
                    )
                    _validate_registry_url(error.url)
                    return HttpResponse(
                        status=error.code,
                        url=error.url,
                        headers=tuple(error.headers.items()),
                        body=body[:max_bytes],
                        redirects=tuple(redirects),
                        truncated=len(body) > max_bytes,
                    )
        except TimeoutError as error:
            message = "npmjs request timed out"
            raise NpmjsTimeoutError(message) from error
        except urllib.error.URLError as error:
            message = "npmjs request failed"
            raise NpmjsNetworkError(message) from error
        except (http.client.HTTPException, OSError) as error:
            message = "npmjs response ended prematurely"
            raise NpmjsNetworkError(message) from error


def _validate_size_limit(value: object, *, field: str) -> int:
    if type(value) is not int:
        message = f"{field} must be an exact integer"
        raise TypeError(message)
    accepted = cast("int", value)
    if accepted <= 0:
        message = f"{field} must be positive"
        raise ValueError(message)
    return accepted


def _declared_content_length(response: Any) -> int | None:  # noqa: ANN401
    value = response.headers.get("Content-Length")
    if value is None:
        return None
    try:
        length = int(value, 10)
    except ValueError as error:
        message = "npmjs response has invalid Content-Length"
        raise NpmjsNetworkError(message) from error
    if length < 0:
        message = "npmjs response has invalid Content-Length"
        raise NpmjsNetworkError(message)
    return length


def _read_complete_bounded_body(
    response: Any,  # noqa: ANN401
    *,
    max_bytes: int,
) -> bytes:
    declared_length = _declared_content_length(response)
    try:
        body = response.read(max_bytes + 1)
    except http.client.IncompleteRead as error:
        message = "npmjs response ended before Content-Length"
        raise NpmjsNetworkError(message) from error
    if (
        len(body) <= max_bytes
        and declared_length is not None
        and len(body) < declared_length
    ):
        message = "npmjs response ended before Content-Length"
        raise NpmjsNetworkError(message)
    return body


def _header(response: HttpResponse, name: str) -> str | None:
    lowered = name.lower()
    for key, value in response.headers:
        if key.lower() == lowered:
            return value
    return None


def _metadata_url(package_name: str) -> str:
    encoded = urllib.parse.quote(package_name, safe="@")
    return f"{NPMJS_REGISTRY_ORIGIN}/{encoded}"


def _exact_metadata_url(package_name: str, version: str) -> str:
    encoded_package = urllib.parse.quote(package_name, safe="@")
    encoded_version = urllib.parse.quote(version, safe="")
    return f"{NPMJS_REGISTRY_ORIGIN}/{encoded_package}/{encoded_version}"


def _request_headers(accept: str) -> tuple[tuple[str, str], ...]:
    return (
        ("Accept", accept),
        ("Accept-Encoding", "identity"),
        ("Cache-Control", "no-cache"),
    )


def _is_redirect_status(status: int) -> bool:
    return HTTP_MULTIPLE_CHOICES <= status < HTTP_BAD_REDIRECT_LIMIT


def _is_registry_url(url: str) -> bool:
    parsed = urllib.parse.urlparse(url)
    try:
        port = parsed.port
    except ValueError:
        return False
    return (
        parsed.scheme == "https"
        and parsed.hostname == "registry.npmjs.org"
        and parsed.username is None
        and parsed.password is None
        and port in {None, 443}
    )


def _validate_registry_url(url: str) -> None:
    if not _is_registry_url(url):
        message = "npmjs URL is outside approved registry origin"
        raise NpmjsPolicyError(message)


def _is_identity_encoded(response: HttpResponse) -> bool:
    encoding = _header(response, "content-encoding")
    return encoding is None or encoding.lower() == "identity"


def _response_url_policy_ok(response: HttpResponse) -> bool:
    return (
        _is_registry_url(response.url)
        and len(response.redirects) <= DEFAULT_REDIRECT_LIMIT
        and all(_is_registry_url(url) for url in response.redirects)
    )


def _body_sha256(body: bytes) -> str:
    return f"sha256:{hashlib.sha256(body).hexdigest()}"


def _desired_projection_state_digest(
    snapshot: QualificationSnapshot,
    projection_id: str,
    artifact: ReleaseArtifact,
) -> str:
    from three_workflow_delivery_v3.release.finalizer import (  # noqa: PLC0415
        desired_projection_state_digest,
    )

    return desired_projection_state_digest(snapshot, projection_id, artifact)


def _request_facts(
    snapshot: QualificationSnapshot,
    artifact: ReleaseArtifact,
) -> ObservationRequestFacts:
    projection = snapshot.destination_projections[0]
    metadata_url = _exact_metadata_url(
        projection.coordinate.package_name,
        projection.coordinate.native_version,
    )
    return ObservationRequestFacts(
        qualification_snapshot_digest=snapshot.snapshot_digest,
        projection_digest=projection.projection_digest,
        desired_state_digest=_desired_projection_state_digest(
            snapshot,
            projection.projection_id,
            artifact,
        ),
        method="GET",
        url=metadata_url,
        headers=_request_headers(_METADATA_ACCEPT),
    )


def _observation(
    snapshot: QualificationSnapshot,
    artifact: ReleaseArtifact,
    request_facts: ObservationRequestFacts,
    response_facts: ObservationResponseFacts,
    value: ObservationValue,
) -> ProjectionObservation:
    projection = snapshot.destination_projections[0]
    request_digest = request_facts.request_digest
    return ProjectionObservation(
        subject=snapshot.subject.simulation
        if isinstance(snapshot.subject, SimulationBinding)
        else snapshot.subject,
        purpose="release-simulation"
        if isinstance(snapshot.subject, SimulationBinding)
        else "live-release",
        target=snapshot.target,
        producer=NPMJS_OBSERVER_PRODUCER,
        qualification_snapshot_digest=snapshot.snapshot_digest,
        projection=projection,
        desired_state_digest=_desired_projection_state_digest(
            snapshot,
            projection.projection_id,
            artifact,
        ),
        observation_contract_id=projection.observation_contract_id,
        request_facts=request_facts,
        request_digest=request_digest,
        response_digest=canonical_sha256(
            {
                "schema": "workflow-delivery/v3/observation-response",
                "request-digest": request_digest,
                "facts": response_facts.to_document(),
                "value": value.to_document(),
            }
        ),
        response_facts=response_facts,
        value=value,
    )


def _classified(  # noqa: PLR0913
    classification: str,
    *,
    snapshot: QualificationSnapshot,
    artifact: ReleaseArtifact,
    request_facts: ObservationRequestFacts,
    response_facts: ObservationResponseFacts,
    content_sha512: str | None = None,
    witness_digest: str | None = None,
) -> ProjectionObservation:
    projection = snapshot.destination_projections[0]
    if classification == "exact-satisfied":
        value = ObservationValue(
            classification=classification,
            owner=NPMJS_EXACT_OWNER,
            coordinate=projection.coordinate,
            content_sha512=artifact.content.content_sha512,
            witness_digest=artifact.witness_digest,
            routing=(),
        )
    elif classification == "conflicting":
        value = ObservationValue(
            classification=classification,
            owner=NPMJS_EXACT_OWNER,
            coordinate=projection.coordinate,
            content_sha512=content_sha512,
            witness_digest=witness_digest,
            routing=(),
        )
    else:
        value = ObservationValue(
            classification=classification,
            owner=None,
            coordinate=None,
            content_sha512=None,
            witness_digest=None,
            routing=(),
        )
    return _observation(
        snapshot,
        artifact,
        request_facts,
        response_facts,
        value,
    )


def _validate_first_slice_basis(
    snapshot: QualificationSnapshot,
    decision: QualificationDecision,
    artifact: ReleaseArtifact,
    expectation: ArtifactExpectation,
) -> None:
    if not isinstance(snapshot.subject, SimulationBinding):
        message = "npmjs observation is implemented only for simulation"
        raise TypeError(message)
    if decision.terminal_result != "success":
        message = "npmjs observation requires successful qualification"
        raise ValueError(message)
    if (
        decision.subject != snapshot.subject.simulation
        or decision.qualification_snapshot_digest != snapshot.snapshot_digest
        or artifact.subject != snapshot.subject.simulation
        or artifact.target != snapshot.target
        or artifact.qualification_snapshot_digest != snapshot.snapshot_digest
        or artifact.artifact_digest not in decision.admitted_artifact_digests
    ):
        message = "npmjs observation basis is not current"
        raise ValueError(message)
    projection = snapshot.destination_projections[0]
    coordinate = projection.coordinate
    if (
        projection.destination_id != "npm/npmjs-public-v1"
        or projection.registry != NPMJS_REGISTRY_ORIGIN
        or projection.observation_contract_id != NPMJS_OBSERVATION_CONTRACT_ID
        or coordinate.channel != "official"
        or coordinate.package_name != FIRST_SLICE_PACKAGE
        or coordinate.native_version != snapshot.nbgv.npm_package_version
        or coordinate.native_version != expectation.npm_package_version
        or expectation.package_name != FIRST_SLICE_PACKAGE
    ):
        message = "npmjs observation coordinate is outside the first slice"
        raise ValueError(message)
    _validate_artifact_expectation(expectation)


def _metadata_version_manifest(
    document: JsonValue,
    version: str,
) -> dict[str, JsonValue]:
    if not isinstance(document, dict):
        message = "npmjs exact-version metadata must be an object"
        raise TypeError(message)
    if document.get("name") != FIRST_SLICE_PACKAGE:
        message = "npmjs metadata package identity mismatch"
        raise ValueError(message)
    if document.get("version") != version:
        message = "npmjs metadata version identity mismatch"
        raise ValueError(message)
    return document


def _tarball_url(version_document: dict[str, JsonValue]) -> str:
    dist = version_document.get("dist")
    if not isinstance(dist, dict):
        message = "npmjs metadata dist missing"
        raise TypeError(message)
    tarball = dist.get("tarball")
    if type(tarball) is not str or not _is_registry_url(tarball):
        message = "npmjs tarball URL is outside registry.npmjs.org"
        raise ValueError(message)
    return tarball


def _dist_integrity(version_document: dict[str, JsonValue]) -> str | None:
    dist = version_document.get("dist")
    if not isinstance(dist, dict):
        return None
    integrity = dist.get("integrity")
    if type(integrity) is not str:
        return None
    return integrity


@dataclass(frozen=True, slots=True)
class _RemoteTarballObservation:
    classification: str
    content_sha512: str
    byte_size: int
    witness_digest: str | None
    witness_target: str | None = None


def _parsed_witness_identity(
    entries: dict[str, bytes],
) -> tuple[str, str]:
    witness = entries[_PACKED_WITNESS_PATH]
    document = parse_canonical_json(witness)
    parsed = package_target_witness_from_document(document)
    return canonical_sha256(document), parsed.target


def _remote_tarball_observation(  # noqa: C901, PLR0911
    tarball: bytes,
    *,
    artifact: ReleaseArtifact,
    expectation: ArtifactExpectation,
    expanded_limit_bytes: int,
) -> _RemoteTarballObservation:
    remote_sha512 = f"sha512:{hashlib.sha512(tarball).hexdigest()}"
    byte_size = len(tarball)
    try:
        entries = _read_tarball(
            tarball,
            max_payload_bytes=expanded_limit_bytes,
        )
    except _TarballExpansionLimitError:
        return _RemoteTarballObservation(
            "unprovable",
            remote_sha512,
            byte_size,
            None,
        )
    except (TypeError, ValueError):
        return _RemoteTarballObservation(
            "unprovable",
            remote_sha512,
            byte_size,
            None,
        )
    try:
        _qualify_npm_artifact_entries(tarball, entries, expectation)
    except (TypeError, ValueError):
        try:
            manifest = parse_json_strict(entries["package/package.json"])
        except (KeyError, TypeError, ValueError):
            return _RemoteTarballObservation(
                "unprovable",
                remote_sha512,
                byte_size,
                None,
            )
        if not isinstance(manifest, dict):
            return _RemoteTarballObservation(
                "unprovable",
                remote_sha512,
                byte_size,
                None,
            )
        identity_mismatch = (
            manifest.get("name") != expectation.package_name
            or manifest.get("version") != expectation.npm_package_version
        )
        witness = entries.get(_PACKED_WITNESS_PATH)
        witness_document: JsonValue | None = None
        witness_digest: str | None = None
        witness_target: str | None = None
        if witness is not None:
            try:
                witness_document = parse_canonical_json(witness)
                parsed_witness = package_target_witness_from_document(
                    witness_document
                )
                witness_digest = canonical_sha256(witness_document)
                witness_target = parsed_witness.target
            except (TypeError, ValueError):
                witness_document = None
                witness_digest = None
        if identity_mismatch:
            return _RemoteTarballObservation(
                "conflicting",
                remote_sha512,
                byte_size,
                witness_digest,
                witness_target,
            )
        if witness_document is None:
            return _RemoteTarballObservation(
                "unprovable",
                remote_sha512,
                byte_size,
                None,
            )
        return _RemoteTarballObservation(
            "conflicting",
            remote_sha512,
            byte_size,
            canonical_sha256(witness_document),
            witness_target,
        )
    witness_digest, witness_target = _parsed_witness_identity(entries)
    if remote_sha512 == artifact.content.content_sha512:
        return _RemoteTarballObservation(
            "exact-satisfied",
            remote_sha512,
            byte_size,
            witness_digest,
            witness_target,
        )
    return _RemoteTarballObservation(
        "conflicting",
        remote_sha512,
        byte_size,
        witness_digest,
        witness_target,
    )


def _selected_headers(
    response: HttpResponse,
) -> tuple[tuple[str, str], ...]:
    return tuple(
        (key.lower(), value)
        for key, value in sorted(
            response.headers,
            key=lambda item: item[0].lower(),
        )
        if key.lower() in _SELECTED_RESPONSE_HEADERS
    )


@dataclass(frozen=True, slots=True)
class _MetadataResponseFacts:
    body_sha256: str
    package: str
    version: str
    dist_tarball: str
    dist_integrity: str | None


def _response_facts(  # noqa: PLR0913
    *,
    stage: str,
    requested_url: str,
    response: HttpResponse | None = None,
    status: int | str | None = None,
    body_digest: str | None = None,
    status_detail: str | None = None,
    metadata: _MetadataResponseFacts | None = None,
    tarball_content_sha512: str | None = None,
    tarball_byte_size: int | None = None,
    remote_witness_digest: str | None = None,
) -> ObservationResponseFacts:
    if response is not None:
        final_url = response.url
        redirects = response.redirects
        response_status: int | str = response.status
        selected_headers = _selected_headers(response)
        truncated: bool | None = response.truncated
    elif status is not None:
        final_url = None
        redirects = ()
        response_status = status
        selected_headers = ()
        truncated = None
    else:
        message = "observation response facts require a status"
        raise ValueError(message)
    return ObservationResponseFacts(
        stage=stage,
        requested_url=requested_url,
        final_url=final_url,
        redirects=redirects,
        status=response_status,
        selected_headers=selected_headers,
        truncated=truncated,
        body_sha256=body_digest,
        status_detail=status_detail,
        metadata_body_sha256=(
            None if metadata is None else metadata.body_sha256
        ),
        metadata_package=None if metadata is None else metadata.package,
        metadata_version=None if metadata is None else metadata.version,
        dist_tarball=None if metadata is None else metadata.dist_tarball,
        dist_integrity=None if metadata is None else metadata.dist_integrity,
        tarball_content_sha512=tarball_content_sha512,
        tarball_byte_size=tarball_byte_size,
        remote_witness_digest=remote_witness_digest,
    )


def observe_npmjs_projection(  # noqa: C901, PLR0911, PLR0912, PLR0913
    snapshot: QualificationSnapshot,
    decision: QualificationDecision,
    artifact: ReleaseArtifact,
    expectation: ArtifactExpectation,
    *,
    transport: HttpTransport | None = None,
    timeout: float = DEFAULT_TIMEOUT_SECONDS,
    metadata_limit_bytes: int = DEFAULT_METADATA_LIMIT_BYTES,
    tarball_limit_bytes: int = DEFAULT_TARBALL_LIMIT_BYTES,
    expanded_tarball_limit_bytes: int = DEFAULT_EXPANDED_TARBALL_LIMIT_BYTES,
) -> ProjectionObservation:
    """Observe exact first-slice npmjs state without credentials."""
    metadata_limit_bytes = _validate_size_limit(
        metadata_limit_bytes,
        field="metadata_limit_bytes",
    )
    tarball_limit_bytes = _validate_size_limit(
        tarball_limit_bytes,
        field="tarball_limit_bytes",
    )
    expanded_tarball_limit_bytes = _validate_size_limit(
        expanded_tarball_limit_bytes,
        field="expanded_tarball_limit_bytes",
    )
    _validate_first_slice_basis(snapshot, decision, artifact, expectation)
    transport = StdlibHttpTransport() if transport is None else transport
    request_facts = _request_facts(snapshot, artifact)
    metadata_url = _exact_metadata_url(
        FIRST_SLICE_PACKAGE,
        snapshot.nbgv.npm_package_version,
    )
    try:
        metadata = transport.get(
            metadata_url,
            headers=_request_headers(_METADATA_ACCEPT),
            timeout=timeout,
            max_bytes=metadata_limit_bytes,
        )
    except NpmjsPolicyError:
        return _classified(
            "unprovable",
            snapshot=snapshot,
            artifact=artifact,
            request_facts=request_facts,
            response_facts=_response_facts(
                stage="metadata",
                requested_url=metadata_url,
                status="off-policy",
            ),
        )
    except (NpmjsTimeoutError, NpmjsNetworkError):
        return _classified(
            "unknown",
            snapshot=snapshot,
            artifact=artifact,
            request_facts=request_facts,
            response_facts=_response_facts(
                stage="metadata",
                requested_url=metadata_url,
                status="network-error",
            ),
        )
    metadata_body_digest = _body_sha256(metadata.body)
    if metadata.truncated:
        return _classified(
            "unknown",
            snapshot=snapshot,
            artifact=artifact,
            request_facts=request_facts,
            response_facts=_response_facts(
                stage="metadata",
                requested_url=metadata_url,
                response=metadata,
                body_digest=metadata_body_digest,
                status_detail="truncated",
            ),
        )
    if not _response_url_policy_ok(metadata):
        return _classified(
            "unprovable",
            snapshot=snapshot,
            artifact=artifact,
            request_facts=request_facts,
            response_facts=_response_facts(
                stage="metadata",
                requested_url=metadata_url,
                response=metadata,
                body_digest=metadata_body_digest,
                status_detail="off-policy",
            ),
        )
    if metadata.status == HTTP_NOT_FOUND:
        return _classified(
            "absent",
            snapshot=snapshot,
            artifact=artifact,
            request_facts=request_facts,
            response_facts=_response_facts(
                stage="metadata",
                requested_url=metadata_url,
                response=metadata,
                body_digest=metadata_body_digest,
            ),
        )
    if (
        metadata.status in {HTTP_TOO_MANY_REQUESTS}
        or metadata.status >= HTTP_SERVER_ERROR
    ):
        return _classified(
            "unknown",
            snapshot=snapshot,
            artifact=artifact,
            request_facts=request_facts,
            response_facts=_response_facts(
                stage="metadata",
                requested_url=metadata_url,
                response=metadata,
                body_digest=metadata_body_digest,
            ),
        )
    if (
        metadata.status in {HTTP_UNAUTHORIZED, HTTP_FORBIDDEN}
        or HTTP_BAD_REQUEST <= metadata.status < HTTP_SERVER_ERROR
    ):
        return _classified(
            "unprovable",
            snapshot=snapshot,
            artifact=artifact,
            request_facts=request_facts,
            response_facts=_response_facts(
                stage="metadata",
                requested_url=metadata_url,
                response=metadata,
                body_digest=metadata_body_digest,
            ),
        )
    if (
        metadata.status != HTTP_OK
        or not _is_identity_encoded(metadata)
        or not _response_url_policy_ok(metadata)
    ):
        return _classified(
            "unprovable",
            snapshot=snapshot,
            artifact=artifact,
            request_facts=request_facts,
            response_facts=_response_facts(
                stage="metadata",
                requested_url=metadata_url,
                response=metadata,
                body_digest=metadata_body_digest,
            ),
        )
    try:
        version_document = _metadata_version_manifest(
            parse_json_strict(metadata.body),
            snapshot.nbgv.npm_package_version,
        )
        tarball_url = _tarball_url(version_document)
    except (TypeError, ValueError):
        return _classified(
            "unprovable",
            snapshot=snapshot,
            artifact=artifact,
            request_facts=request_facts,
            response_facts=_response_facts(
                stage="metadata",
                requested_url=metadata_url,
                response=metadata,
                body_digest=metadata_body_digest,
                status_detail="malformed",
            ),
        )
    metadata_facts = _MetadataResponseFacts(
        body_sha256=metadata_body_digest,
        package=FIRST_SLICE_PACKAGE,
        version=snapshot.nbgv.npm_package_version,
        dist_tarball=tarball_url,
        dist_integrity=_dist_integrity(version_document),
    )

    try:
        tarball_response = transport.get(
            tarball_url,
            headers=_request_headers(_TARBALL_ACCEPT),
            timeout=timeout,
            max_bytes=tarball_limit_bytes,
        )
    except NpmjsPolicyError:
        return _classified(
            "unprovable",
            snapshot=snapshot,
            artifact=artifact,
            request_facts=request_facts,
            response_facts=_response_facts(
                stage="tarball",
                requested_url=tarball_url,
                status="off-policy",
                metadata=metadata_facts,
            ),
        )
    except (NpmjsTimeoutError, NpmjsNetworkError):
        return _classified(
            "unknown",
            snapshot=snapshot,
            artifact=artifact,
            request_facts=request_facts,
            response_facts=_response_facts(
                stage="tarball",
                requested_url=tarball_url,
                status="network-error",
                metadata=metadata_facts,
            ),
        )
    if tarball_response.truncated:
        return _classified(
            "unknown",
            snapshot=snapshot,
            artifact=artifact,
            request_facts=request_facts,
            response_facts=_response_facts(
                stage="tarball",
                requested_url=tarball_url,
                response=tarball_response,
                body_digest=_body_sha256(tarball_response.body),
                metadata=metadata_facts,
                status_detail="truncated",
            ),
        )
    if not _response_url_policy_ok(tarball_response):
        return _classified(
            "unprovable",
            snapshot=snapshot,
            artifact=artifact,
            request_facts=request_facts,
            response_facts=_response_facts(
                stage="tarball",
                requested_url=tarball_url,
                response=tarball_response,
                body_digest=_body_sha256(tarball_response.body),
                metadata=metadata_facts,
                status_detail="off-policy",
            ),
        )
    if (
        tarball_response.status in {HTTP_TOO_MANY_REQUESTS}
        or tarball_response.status >= HTTP_SERVER_ERROR
    ):
        return _classified(
            "unknown",
            snapshot=snapshot,
            artifact=artifact,
            request_facts=request_facts,
            response_facts=_response_facts(
                stage="tarball",
                requested_url=tarball_url,
                response=tarball_response,
                body_digest=_body_sha256(tarball_response.body),
                metadata=metadata_facts,
            ),
        )
    if tarball_response.status != HTTP_OK or not _is_identity_encoded(
        tarball_response
    ):
        return _classified(
            "unprovable",
            snapshot=snapshot,
            artifact=artifact,
            request_facts=request_facts,
            response_facts=_response_facts(
                stage="tarball",
                requested_url=tarball_url,
                response=tarball_response,
                body_digest=_body_sha256(tarball_response.body),
                metadata=metadata_facts,
            ),
        )
    remote_observation = _remote_tarball_observation(
        tarball_response.body,
        artifact=artifact,
        expectation=expectation,
        expanded_limit_bytes=expanded_tarball_limit_bytes,
    )
    return _classified(
        remote_observation.classification,
        snapshot=snapshot,
        artifact=artifact,
        request_facts=request_facts,
        response_facts=_response_facts(
            stage="tarball",
            requested_url=tarball_url,
            response=tarball_response,
            body_digest=_body_sha256(tarball_response.body),
            metadata=metadata_facts,
            tarball_content_sha512=remote_observation.content_sha512,
            tarball_byte_size=remote_observation.byte_size,
            remote_witness_digest=remote_observation.witness_digest,
        ),
        content_sha512=remote_observation.content_sha512,
        witness_digest=remote_observation.witness_digest,
    )


__all__ = [
    "DEFAULT_EXPANDED_TARBALL_LIMIT_BYTES",
    "DEFAULT_METADATA_LIMIT_BYTES",
    "DEFAULT_REDIRECT_LIMIT",
    "DEFAULT_TARBALL_LIMIT_BYTES",
    "DEFAULT_TIMEOUT_SECONDS",
    "NPMJS_EXACT_OWNER",
    "NPMJS_OBSERVER_PRODUCER",
    "NPMJS_REGISTRY_ORIGIN",
    "HttpResponse",
    "HttpTransport",
    "NpmjsNetworkError",
    "NpmjsPolicyError",
    "NpmjsTimeoutError",
    "NpmjsTruncatedResponseError",
    "StdlibHttpTransport",
    "observe_npmjs_projection",
]
