"""Query-only observation for the approved Platform-Orphan exception."""

# ruff: noqa: BLE001, C901, EM101, EM102, ISC004, PLR0912, PLR0913, PLR2004, TRY003

from __future__ import annotations

import base64
import hashlib
import http.client
import re
import stat
import urllib.error
import urllib.parse
import urllib.request
from copy import deepcopy
from dataclasses import dataclass
from datetime import UTC, datetime
from pathlib import Path
from typing import TYPE_CHECKING, Protocol, cast

from three_workflow_delivery_v3.adapters.github_packages import (
    ACCEPTANCE_PACKAGE_NAME,
    ACCEPTANCE_REPOSITORY_URL,
    ACCEPTANCE_WITNESS_PATH,
    GitHubPackagesHttpResponse,
    inspect_fixed_acceptance_tarball,
    redirect_headers,
)
from three_workflow_delivery_v3.adapters.node import _read_tarball
from three_workflow_delivery_v3.canonical import (
    JsonValue,
    canonical_sha256,
    canonicalize,
    parse_canonical_json,
    parse_json_strict,
)
from three_workflow_delivery_v3.platform.github import admit_artifact_download
from three_workflow_delivery_v3.records.governance import (
    GovernanceAcceptanceEvidence,
    admit_governance_acceptance_evidence,
)
from three_workflow_delivery_v3.records.platform_orphan import (
    PLATFORM_ORPHAN_ACTIVE_AUTHORITY_SHA256,
    PLATFORM_ORPHAN_AUTHORITY_PATH,
    PLATFORM_ORPHAN_REF,
    PLATFORM_ORPHAN_REPOSITORY,
    PlatformOrphanActiveAuthority,
    admit_platform_orphan_active_authority,
)

if TYPE_CHECKING:
    from collections.abc import Callable, Mapping

API_ORIGIN = "https://api.github.com"
NPM_ORIGIN = "https://npm.pkg.github.com"
ELIGIBLE_AFTER = datetime(2026, 9, 8, 4, 35, 59, tzinfo=UTC)
RUN_ID = 32809578776
WORKFLOW_ID = 341728447
WORKFLOW_PATH = (
    ".github/workflows/workflow-delivery-v3-buddy-smoke-acceptance-retry-2.yml"
)
ENVIRONMENT_NAME = "workflow-delivery-v3-buddy-smoke-acceptance-retry-2"
TRANSITION_REF = "refs/heads/workflow-delivery-v3-acceptance-retry-2-transition"
ACCEPTANCE_RUN_ID = 32805739095
ACCEPTANCE_TARGET = "b031e5e0bd98a95943a03a1529b64e856e1a8aa1"
ACCEPTANCE_WORKFLOW = "953c1db0712f6ff4d41b7e6a35767d71a2b19c4d"
PACKAGE_VERSION = "0.0.0-wdv3-acceptance.5"
PACKAGE_TAG = "wdv3-acceptance-5"
PACKAGE_COORDINATE = f"{ACCEPTANCE_PACKAGE_NAME}@{PACKAGE_VERSION}"
EXPECTED_TARBALL_SHA512 = (
    "sha512:"
    "080c3d828a30d73d1febc3b6773015fafb529cf3a2be81fe597e83a83a589d32"
    "c1be62e933fb38ac4a77f9cb561c6399d3b2e6fe9179b3e4aed93087007140f2"
)
_ARTIFACT_DIGESTS = {
    "review": (
        9548188898,
        "sha256:"
        "b7386651bea7c441a038c61c7d143596490a985eca33efaee7d1ede8d9701bc4",
    ),
    "probe": (
        9548197128,
        "sha256:"
        "c2153d565cb1380fdf9d86fbe777fb104b6a9a4de9ecc181bbc1b84ba12ca75c",
    ),
    "governance": (
        9548202666,
        "sha256:"
        "9e1aaf6701d166db0188ad7a9dce784bdaed4034e6a276545dd2a6351b3dab37",
    ),
}
_SHA = re.compile(r"[0-9a-f]{40}")
_BLOB_OID = re.compile(r"(?:[0-9a-f]{40}|[0-9a-f]{64})")
_MAX_PAGES = 100
_PAGE_SIZE = 100
_METADATA_LIMIT = 1_000_000
_TARBALL_LIMIT = 25_000_000
_TIMEOUT = 20.0
_USER_AGENT = "three-workflow-delivery-v3-platform-orphan"


class PlatformOrphanObservationError(ValueError):
    """The query-only invocation could not prove the approved state."""


class QueryOnlyPlatformOrphanTransport(Protocol):
    """GET-only transport with an explicit redirect policy."""

    def get(
        self,
        url: str,
        *,
        headers: tuple[tuple[str, str], ...],
        timeout: float,
        max_bytes: int,
        redirect_policy: str,
    ) -> GitHubPackagesHttpResponse:
        """Perform one GET under deny or bounded-manual-tarball redirects."""
        ...


class InjectedPlatformOrphanGetTransport:
    """Query-only manual redirect transport around an injected one-hop GET."""

    _REDIRECT_STATUSES = frozenset({301, 302, 303, 307, 308})
    _TARBALL_HOSTS = frozenset(
        {
            "npm.pkg.github.com",
            "objects.githubusercontent.com",
            "github-registry-files.githubusercontent.com",
        }
    )

    def __init__(
        self,
        opener: Callable[
            [
                str,
                tuple[tuple[str, str], ...],
                float,
                int,
            ],
            GitHubPackagesHttpResponse,
        ],
    ) -> None:
        """Bind the required injected one-hop query-only opener."""
        if not callable(opener):
            raise TypeError("one-hop GET opener must be callable")
        self._opener = opener

    @classmethod
    def _validate_tarball_url(cls, url: str) -> None:
        parsed = urllib.parse.urlsplit(url)
        try:
            port = parsed.port
        except ValueError:
            raise PlatformOrphanObservationError(
                "tarball redirect policy was denied"
            ) from None
        if (
            parsed.scheme != "https"
            or parsed.hostname not in cls._TARBALL_HOSTS
            or parsed.username is not None
            or parsed.password is not None
            or port not in {None, 443}
            or parsed.fragment
        ):
            raise PlatformOrphanObservationError(
                "tarball redirect policy was denied"
            )

    @staticmethod
    def _location(response: GitHubPackagesHttpResponse) -> str:
        matches = [
            value
            for name, value in response.headers
            if name.lower() == "location"
        ]
        if len(matches) != 1 or not matches[0]:
            raise PlatformOrphanObservationError(
                "tarball redirect location is malformed"
            )
        return matches[0]

    def get(
        self,
        url: str,
        *,
        headers: tuple[tuple[str, str], ...],
        timeout: float,
        max_bytes: int,
        redirect_policy: str,
    ) -> GitHubPackagesHttpResponse:
        """Perform one metadata GET or a bounded validated tarball chain."""
        if redirect_policy not in {"deny", "tarball"}:
            raise PlatformOrphanObservationError(
                "redirect policy is not allowed"
            )
        if redirect_policy == "deny":
            response = self._opener(url, headers, timeout, max_bytes)
            if (
                type(response) is not GitHubPackagesHttpResponse
                or response.url != url
                or response.redirects
            ):
                raise PlatformOrphanObservationError(
                    "one-hop metadata response is malformed"
                )
            if response.truncated or not response.complete:
                raise PlatformOrphanObservationError(
                    "one-hop metadata response is incomplete"
                )
            if response.status in self._REDIRECT_STATUSES:
                raise PlatformOrphanObservationError(
                    "metadata redirect was denied"
                )
            return response

        current = url
        current_headers = headers
        redirects: list[str] = []
        seen: set[str] = set()
        for _hop in range(6):
            self._validate_tarball_url(current)
            if current in seen:
                raise PlatformOrphanObservationError(
                    "tarball redirect cycle was denied"
                )
            seen.add(current)
            response = self._opener(
                current,
                current_headers,
                timeout,
                max_bytes,
            )
            if (
                type(response) is not GitHubPackagesHttpResponse
                or response.url != current
                or response.redirects
            ):
                raise PlatformOrphanObservationError(
                    "one-hop tarball response is malformed"
                )
            if response.truncated or not response.complete:
                raise PlatformOrphanObservationError(
                    "one-hop tarball response is incomplete"
                )
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
            if len(redirects) == 5:
                raise PlatformOrphanObservationError(
                    "tarball redirect limit was exceeded"
                )
            target = urllib.parse.urljoin(current, self._location(response))
            self._validate_tarball_url(target)
            current_headers = redirect_headers(
                source_url=current,
                target_url=target,
                headers=current_headers,
            )
            redirects.append(target)
            current = target
        raise PlatformOrphanObservationError(
            "tarball redirect limit was exceeded"
        )


class UrllibPlatformOrphanOneHopGet:
    """Concrete bounded GET that never follows redirects automatically."""

    class _NoRedirect(urllib.request.HTTPRedirectHandler):
        def redirect_request(
            self,
            *_args: object,
            **_kwargs: object,
        ) -> None:
            return None

    def __call__(
        self,
        url: str,
        headers: tuple[tuple[str, str], ...],
        timeout: float,
        max_bytes: int,
    ) -> GitHubPackagesHttpResponse:
        """Perform one bounded GET and surface any redirect response."""
        if (
            type(url) is not str
            or type(headers) is not tuple
            or any(
                type(pair) is not tuple
                or len(pair) != 2
                or any(type(item) is not str for item in pair)
                for pair in headers
            )
            or type(timeout) not in {int, float}
            or timeout <= 0
            or type(max_bytes) is not int
            or max_bytes <= 0
        ):
            raise PlatformOrphanObservationError(
                "one-hop GET inputs are malformed"
            )
        request = urllib.request.Request(  # noqa: S310
            url,
            headers=dict(headers),
            method="GET",
        )
        opener = urllib.request.build_opener(
            urllib.request.ProxyHandler({}),
            self._NoRedirect,
        )
        try:
            with opener.open(request, timeout=float(timeout)) as response:
                body, truncated, complete = _read_bounded_http_body(
                    response,
                    max_bytes=max_bytes,
                )
                return GitHubPackagesHttpResponse(
                    status=response.status,
                    url=response.geturl(),
                    headers=tuple(response.headers.items()),
                    body=body,
                    truncated=truncated,
                    complete=complete,
                )
        except urllib.error.HTTPError as error:
            body, truncated, complete = _read_bounded_http_body(
                error,
                max_bytes=max_bytes,
            )
            return GitHubPackagesHttpResponse(
                status=error.code,
                url=error.geturl(),
                headers=tuple(error.headers.items()),
                body=body,
                truncated=truncated,
                complete=complete,
            )
        except (OSError, TimeoutError):
            raise PlatformOrphanObservationError("one-hop GET failed") from None


def _read_bounded_http_body(
    response: object,
    *,
    max_bytes: int,
) -> tuple[bytes, bool, bool]:
    """Read one response body while retaining truncation and completeness."""
    headers = getattr(response, "headers", None)
    raw_length = None if headers is None else headers.get("Content-Length")
    declared_length: int | None = None
    if raw_length is not None:
        if (
            type(raw_length) is not str
            or re.fullmatch(r"(?:0|[1-9][0-9]*)", raw_length) is None
        ):
            raise PlatformOrphanObservationError(
                "one-hop GET Content-Length is malformed"
            )
        declared_length = int(raw_length)
    try:
        body = response.read(max_bytes + 1)  # type: ignore[attr-defined]
        incomplete_read = False
    except http.client.IncompleteRead as error:
        body = error.partial
        incomplete_read = True
    if type(body) is not bytes:
        raise PlatformOrphanObservationError("one-hop GET body is malformed")
    truncated = len(body) > max_bytes
    if (
        declared_length is not None
        and not truncated
        and len(body) < declared_length
    ):
        raise PlatformOrphanObservationError(
            "one-hop GET body is shorter than Content-Length"
        )
    retained = body[:max_bytes]
    complete = not truncated and not incomplete_read
    return retained, truncated, complete


@dataclass(frozen=True, slots=True)
class RequestLedgerEntry:
    """Sanitized request facts suitable for the phase-1 result record."""

    sequence: int
    phase: str
    origin: str
    path: str
    page_cursor: str | None
    page_index: int
    http_status: int

    def to_document(self) -> dict[str, JsonValue]:
        """Return the closed phase-1 request-ledger projection."""
        return {
            "sequence": self.sequence,
            "phase": self.phase,
            "method": "GET",
            "origin": self.origin,
            "path": self.path,
            "page_cursor": self.page_cursor,
            "page_index": self.page_index,
            "http_status": self.http_status,
            "complete": True,
        }


@dataclass(frozen=True, slots=True)
class SourceObservation:
    """One protected-main and canonical authority observation."""

    commit: str
    blob_oid: str
    content_sha256: str


@dataclass(frozen=True, slots=True)
class AcceptanceArtifactObservation:
    """Only admitted artifact identities and canonical record digests."""

    review_artifact_id: int
    review_artifact_sha256: str
    probe_artifact_id: int
    probe_artifact_sha256: str
    probe_record_sha256: str
    governance_artifact_id: int
    governance_artifact_sha256: str
    governance_record_sha256: str

    def to_document(self) -> dict[str, JsonValue]:
        """Return fields consumed by the phase-1 candidate schema."""
        return {
            "run_id": ACCEPTANCE_RUN_ID,
            "run_attempt": 1,
            "target_sha": ACCEPTANCE_TARGET,
            "workflow_sha": ACCEPTANCE_WORKFLOW,
            "review_artifact_id": self.review_artifact_id,
            "review_artifact_sha256": self.review_artifact_sha256,
            "probe_artifact_id": self.probe_artifact_id,
            "probe_artifact_sha256": self.probe_artifact_sha256,
            "probe_record_sha256": self.probe_record_sha256,
            "governance_artifact_id": self.governance_artifact_id,
            "governance_artifact_sha256": self.governance_artifact_sha256,
            "governance_record_sha256": self.governance_record_sha256,
        }


@dataclass(frozen=True, slots=True)
class ObservationEnvelope:
    """One canonical state projection and observation instant."""

    phase: str
    observed_at: str
    state: dict[str, JsonValue]

    def to_document(self) -> dict[str, JsonValue]:
        """Return the closed phase-1 observation envelope."""
        return {
            "phase": self.phase,
            "observed_at": self.observed_at,
            "state": deepcopy(self.state),
            "state_sha256": canonical_sha256(self.state),
        }


@dataclass(frozen=True, slots=True)
class PlatformOrphanObservationData:
    """Validated same-invocation data for a later candidate coordinator."""

    started_at: str
    completed_at: str
    initial_source: SourceObservation
    final_source: SourceObservation
    acceptance: AcceptanceArtifactObservation
    requests: tuple[RequestLedgerEntry, ...]
    platform_observations: tuple[ObservationEnvelope, ObservationEnvelope]
    destination_observations: tuple[ObservationEnvelope, ObservationEnvelope]

    @property
    def control_commit(self) -> str:
        """Return the one protected-main control commit."""
        return self.initial_source.commit

    def request_documents(self) -> list[dict[str, JsonValue]]:
        """Return detached phase-1 request ledger documents."""
        return [request.to_document() for request in self.requests]

    def platform_documents(self) -> list[dict[str, JsonValue]]:
        """Return detached phase-1 platform observation documents."""
        return [item.to_document() for item in self.platform_observations]

    def destination_documents(self) -> list[dict[str, JsonValue]]:
        """Return detached phase-1 destination observation documents."""
        return [item.to_document() for item in self.destination_observations]


def _instant(clock: Callable[[], datetime]) -> datetime:
    value = clock()
    if type(value) is not datetime or value.tzinfo is None:
        raise PlatformOrphanObservationError("clock did not return UTC time")
    return value.astimezone(UTC).replace(microsecond=0)


def _format_instant(value: datetime) -> str:
    return value.strftime("%Y-%m-%dT%H:%M:%SZ")


def _git_blob_oid(content: bytes) -> str:
    header = f"blob {len(content)}\0".encode()
    return hashlib.sha1(header + content, usedforsecurity=False).hexdigest()


def _source_observation(
    transport: QueryOnlyPlatformOrphanTransport,
    *,
    phase: str,
    token: str,
    ledger: list[RequestLedgerEntry],
) -> tuple[SourceObservation, PlatformOrphanActiveAuthority]:
    branch = _object(
        _json(
            _get(
                transport,
                phase=phase,
                origin=API_ORIGIN,
                path="/repos/hcoona/three/branches/main",
                token=token,
                ledger=ledger,
                status=frozenset({200}),
            )
        ),
        context="protected branch response",
    )
    branch_commit = _object(
        branch.get("commit"),
        context="protected branch commit",
    )
    if (
        branch.get("name") != "main"
        or branch.get("protected") is not True
        or type(branch_commit.get("sha")) is not str
        or _SHA.fullmatch(cast("str", branch_commit["sha"])) is None
    ):
        raise PlatformOrphanObservationError(
            "protected main identity could not be proved"
        )
    ref = _object(
        _json(
            _get(
                transport,
                phase=phase,
                origin=API_ORIGIN,
                path="/repos/hcoona/three/git/ref/heads/main",
                token=token,
                ledger=ledger,
                status=frozenset({200}),
            )
        ),
        context="protected ref response",
    )
    target = _object(ref.get("object"), context="protected ref object")
    commit = target.get("sha")
    if (
        ref.get("ref") != PLATFORM_ORPHAN_REF
        or target.get("type") != "commit"
        or type(commit) is not str
        or _SHA.fullmatch(commit) is None
        or branch_commit["sha"] != commit
    ):
        raise PlatformOrphanObservationError(
            "protected main identity could not be proved"
        )
    content_document = _object(
        _json(
            _get(
                transport,
                phase=phase,
                origin=API_ORIGIN,
                path=(
                    "/repos/hcoona/three/contents/.github/"
                    "workflow-delivery/governance/"
                    "platform-orphan-run-32809578776.json"
                ),
                query=f"ref={commit}",
                token=token,
                ledger=ledger,
                status=frozenset({200}),
            )
        ),
        context="authority content response",
    )
    blob_oid = content_document.get("sha")
    encoded = content_document.get("content")
    if (
        content_document.get("type") != "file"
        or content_document.get("path") != PLATFORM_ORPHAN_AUTHORITY_PATH
        or type(blob_oid) is not str
        or _BLOB_OID.fullmatch(blob_oid) is None
        or content_document.get("encoding") != "base64"
        or type(encoded) is not str
    ):
        raise PlatformOrphanObservationError(
            "authority blob response is malformed"
        )
    try:
        content = base64.b64decode(
            encoded.replace("\r", "").replace("\n", ""),
            validate=True,
        )
    except ValueError:
        raise PlatformOrphanObservationError(
            "authority blob response is malformed"
        ) from None
    if len(blob_oid) != 40 or _git_blob_oid(content) != blob_oid:
        raise PlatformOrphanObservationError("authority blob OID is unproved")
    try:
        authority = admit_platform_orphan_active_authority(content)
    except (TypeError, ValueError):
        raise PlatformOrphanObservationError(
            "active authority bytes were not admitted"
        ) from None
    return (
        SourceObservation(
            commit=commit,
            blob_oid=blob_oid,
            content_sha256=canonical_sha256(authority.to_document()),
        ),
        authority,
    )


def _read_retained(path: Path, expected_digest: str) -> bytes:
    if not isinstance(path, Path):
        raise PlatformOrphanObservationError(
            "retained artifact path is invalid"
        )
    try:
        metadata = path.lstat()
        if path.is_symlink() or not stat.S_ISREG(metadata.st_mode):
            raise PlatformOrphanObservationError(
                "retained artifact path is invalid"
            )
        content = path.read_bytes()
    except OSError:
        raise PlatformOrphanObservationError(
            "retained artifact could not be read"
        ) from None
    try:
        admitted = admit_artifact_download(
            content,
            expected_digest=expected_digest,
        )
    except (TypeError, RuntimeError):
        raise PlatformOrphanObservationError(
            "retained artifact bytes failed digest admission"
        ) from None
    return admitted.payload


def _probe_document(
    evidence: GovernanceAcceptanceEvidence,
) -> dict[str, JsonValue]:
    fact = evidence.probe_facts[0]
    return {
        "schema": "workflow-delivery/v3/fixed-acceptance-suite",
        "suite": fact.probe.removeprefix("probe-"),
        "scenario-inventory": cast(
            "list[JsonValue]",
            list(fact.scenario_inventory),
        ),
        "scenarios": cast(
            "list[JsonValue]",
            deepcopy(list(fact.scenarios)),
        ),
        "mutation-classification": (
            "unknown"
            if any(
                scenario["mutation-classification"] == "unknown"
                for scenario in fact.scenarios
            )
            else "incomplete"
            if any(
                scenario["mutation-classification"] == "incomplete"
                for scenario in fact.scenarios
            )
            else "complete"
        ),
        "result": fact.result,
        "record-digest": fact.record_digest,
    }


def _admit_artifacts(
    *,
    review_artifact: Path,
    probe_artifact: Path,
    governance_artifact: Path,
) -> AcceptanceArtifactObservation:
    review_id, review_digest = _ARTIFACT_DIGESTS["review"]
    probe_id, probe_digest = _ARTIFACT_DIGESTS["probe"]
    governance_id, governance_digest = _ARTIFACT_DIGESTS["governance"]
    _read_retained(review_artifact, review_digest)
    probe = _read_retained(probe_artifact, probe_digest)
    governance = _read_retained(governance_artifact, governance_digest)
    try:
        evidence = admit_governance_acceptance_evidence(governance)
        parsed_probe = parse_canonical_json(probe)
    except (TypeError, ValueError):
        raise PlatformOrphanObservationError(
            "retained retry-2 records were not admitted"
        ) from None
    if (
        evidence.workflow_run_id != ACCEPTANCE_RUN_ID
        or evidence.run_attempt != 1
        or evidence.target_sha != ACCEPTANCE_TARGET
        or evidence.workflow.sha != ACCEPTANCE_WORKFLOW
        or evidence.package_coordinate != PACKAGE_COORDINATE
        or evidence.mutation_classification != "unknown"
        or evidence.recovery.artifact_id != review_id
        or len(evidence.probe_facts) != 2
        or evidence.probe_facts[0].artifact_id != probe_id
        or evidence.probe_facts[0].artifact_digest != probe_digest
        or parsed_probe != _probe_document(evidence)
    ):
        raise PlatformOrphanObservationError(
            "retained retry-2 records have conflicting bindings"
        )
    return AcceptanceArtifactObservation(
        review_artifact_id=review_id,
        review_artifact_sha256=review_digest,
        probe_artifact_id=probe_id,
        probe_artifact_sha256=probe_digest,
        probe_record_sha256=canonical_sha256(parsed_probe),
        governance_artifact_id=governance_id,
        governance_artifact_sha256=governance_digest,
        governance_record_sha256=canonical_sha256(evidence.to_document()),
    )


def _headers(token: str, *, npm: bool) -> tuple[tuple[str, str], ...]:
    if type(token) is not str or not token:
        raise PlatformOrphanObservationError("read credential is missing")
    authorization = ("Authorization", "Bearer " + token)
    if npm:
        return (
            ("Accept", "application/vnd.npm.install-v1+json"),
            ("User-Agent", _USER_AGENT),
            authorization,
        )
    return (
        ("Accept", "application/vnd.github+json"),
        ("X-GitHub-Api-Version", "2022-11-28"),
        ("User-Agent", _USER_AGENT),
        authorization,
    )


def _get(
    transport: QueryOnlyPlatformOrphanTransport,
    *,
    phase: str,
    origin: str,
    path: str,
    token: str,
    ledger: list[RequestLedgerEntry],
    status: frozenset[int],
    query: str | None = None,
    ledger_cursor: str | None = None,
    page_index: int = 1,
    tarball: bool = False,
) -> GitHubPackagesHttpResponse:
    url = f"{origin}{path}"
    if query is not None:
        url = f"{url}?{query}"
    try:
        response = transport.get(
            url,
            headers=_headers(token, npm=origin == NPM_ORIGIN),
            timeout=_TIMEOUT,
            max_bytes=_TARBALL_LIMIT if tarball else _METADATA_LIMIT,
            redirect_policy="tarball" if tarball else "deny",
        )
    except Exception:
        raise PlatformOrphanObservationError(
            "query-only observation transport failed"
        ) from None
    if type(response) is not GitHubPackagesHttpResponse:
        raise PlatformOrphanObservationError("transport response is malformed")
    if response.truncated or not response.complete:
        raise PlatformOrphanObservationError("transport response is incomplete")
    if response.status not in status:
        raise PlatformOrphanObservationError("endpoint status is not allowed")
    requested = urllib.parse.urlsplit(url)
    if tarball:
        chain = (*response.redirects, response.url)
        if len(response.redirects) > 5:
            raise PlatformOrphanObservationError(
                "tarball redirect limit was exceeded"
            )
        allowed_hosts = {
            "npm.pkg.github.com",
            "objects.githubusercontent.com",
            "github-registry-files.githubusercontent.com",
        }
        for item in chain:
            parsed = urllib.parse.urlsplit(item)
            if (
                parsed.scheme != "https"
                or parsed.hostname not in allowed_hosts
                or parsed.username is not None
                or parsed.password is not None
                or parsed.fragment
            ):
                raise PlatformOrphanObservationError(
                    "tarball redirect policy was denied"
                )
    elif response.redirects or urllib.parse.urlsplit(response.url) != requested:
        raise PlatformOrphanObservationError("metadata redirect was denied")
    ledger.append(
        RequestLedgerEntry(
            sequence=len(ledger) + 1,
            phase=phase,
            origin=origin,
            path=path,
            page_cursor=ledger_cursor,
            page_index=page_index,
            http_status=response.status,
        )
    )
    return response


def _json(response: GitHubPackagesHttpResponse) -> JsonValue:
    try:
        return parse_json_strict(response.body)
    except (UnicodeDecodeError, TypeError, ValueError):
        raise PlatformOrphanObservationError(
            "endpoint response JSON is malformed"
        ) from None


def _object(value: JsonValue, *, context: str) -> dict[str, JsonValue]:
    if type(value) is not dict:
        raise PlatformOrphanObservationError(f"{context} is malformed")
    return cast("dict[str, JsonValue]", value)


def _integer(value: JsonValue, *, context: str) -> int:
    if type(value) is not int or value < 0:
        raise PlatformOrphanObservationError(f"{context} is malformed")
    return cast("int", value)


def _platform_collection(
    transport: QueryOnlyPlatformOrphanTransport,
    *,
    phase: str,
    token: str,
    ledger: list[RequestLedgerEntry],
    kind: str,
) -> tuple[int, int]:
    path = f"/repos/hcoona/three/actions/runs/{RUN_ID}/{kind}"
    key = "jobs" if kind == "jobs" else "artifacts"
    query_prefix = "filter=all&" if kind == "jobs" else ""
    total: int | None = None
    items: list[dict[str, JsonValue]] = []
    pages = 0
    while pages < _MAX_PAGES:
        pages += 1
        query = f"{query_prefix}per_page={_PAGE_SIZE}&page={pages}"
        response = _get(
            transport,
            phase=phase,
            origin=API_ORIGIN,
            path=path,
            token=token,
            ledger=ledger,
            status=frozenset({200}),
            query=query,
            ledger_cursor=query,
            page_index=pages,
        )
        document = _object(_json(response), context=f"{kind} response")
        page_total = _integer(
            document.get("total_count"),
            context=f"{kind} total count",
        )
        raw_items = document.get(key)
        if type(raw_items) is not list:
            raise PlatformOrphanObservationError(
                f"{kind} collection is malformed"
            )
        if total is None:
            total = page_total
        elif total != page_total:
            raise PlatformOrphanObservationError(
                f"{kind} pagination state drifted"
            )
        for raw in raw_items:
            item = _object(raw, context=f"{kind} item")
            _integer(item.get("id"), context=f"{kind} item id")
            if kind == "jobs":
                if (
                    item.get("run_id") != RUN_ID
                    or item.get("run_attempt") != 1
                    or type(item.get("status")) is not str
                    or (
                        item.get("conclusion") is not None
                        and type(item.get("conclusion")) is not str
                    )
                ):
                    raise PlatformOrphanObservationError(
                        "jobs item is malformed"
                    )
            elif (
                type(item.get("name")) is not str
                or not item.get("name")
                or type(item.get("size_in_bytes")) is not int
                or cast("int", item["size_in_bytes"]) < 0
                or type(item.get("expired")) is not bool
                or (
                    item.get("digest") is not None
                    and type(item.get("digest")) is not str
                )
            ):
                raise PlatformOrphanObservationError(
                    "artifacts item is malformed"
                )
            items.append(item)
        if len(items) >= total:
            break
        if len(raw_items) != _PAGE_SIZE:
            raise PlatformOrphanObservationError(
                f"{kind} pagination is incomplete"
            )
    else:
        raise PlatformOrphanObservationError(f"{kind} pagination is incomplete")
    if total is None or len(items) != total:
        raise PlatformOrphanObservationError(f"{kind} pagination is incomplete")
    ids = [cast("int", item["id"]) for item in items]
    if len(ids) != len(set(ids)):
        raise PlatformOrphanObservationError(f"{kind} contains duplicate IDs")
    return total, pages


def _platform_observation(
    transport: QueryOnlyPlatformOrphanTransport,
    *,
    phase: str,
    token: str,
    ledger: list[RequestLedgerEntry],
    control_commit: str,
) -> dict[str, JsonValue]:
    source = _get(
        transport,
        phase=phase,
        origin=API_ORIGIN,
        path=f"/repos/hcoona/three/contents/{WORKFLOW_PATH}",
        query=f"ref={control_commit}",
        token=token,
        ledger=ledger,
        status=frozenset({404}),
    )
    if source.body and _json(source) is None:
        raise PlatformOrphanObservationError(
            "workflow source absence is malformed"
        )
    run = _object(
        _json(
            _get(
                transport,
                phase=phase,
                origin=API_ORIGIN,
                path=f"/repos/hcoona/three/actions/runs/{RUN_ID}",
                token=token,
                ledger=ledger,
                status=frozenset({200}),
            )
        ),
        context="run response",
    )
    expected_run: Mapping[str, JsonValue] = {
        "id": RUN_ID,
        "workflow_id": WORKFLOW_ID,
        "run_attempt": 1,
        "event": "workflow_dispatch",
        "run_number": 2,
        "status": "queued",
        "conclusion": None,
        "head_branch": TRANSITION_REF.removeprefix("refs/heads/"),
        "head_sha": ACCEPTANCE_WORKFLOW,
        "created_at": "2026-08-25T04:35:59Z",
        "run_started_at": "2026-08-25T04:35:59Z",
        "updated_at": "2026-08-25T04:35:59Z",
        "path": WORKFLOW_PATH,
    }
    if any(run.get(name) != value for name, value in expected_run.items()):
        raise PlatformOrphanObservationError("run identity or state drifted")
    repository = _object(run.get("repository"), context="run repository")
    if repository.get("full_name") != PLATFORM_ORPHAN_REPOSITORY:
        raise PlatformOrphanObservationError("run repository drifted")
    for identity in ("node_id", "check_suite_id"):
        if identity not in run or (
            type(run[identity]) not in {str, int} or not run[identity]
        ):
            raise PlatformOrphanObservationError("run identity is malformed")
    workflow = _object(
        _json(
            _get(
                transport,
                phase=phase,
                origin=API_ORIGIN,
                path=f"/repos/hcoona/three/actions/workflows/{WORKFLOW_ID}",
                token=token,
                ledger=ledger,
                status=frozenset({200}),
            )
        ),
        context="workflow response",
    )
    if (
        workflow.get("id") != WORKFLOW_ID
        or workflow.get("path") != WORKFLOW_PATH
        or workflow.get("state") != "disabled_manually"
    ):
        raise PlatformOrphanObservationError(
            "workflow identity or state drifted"
        )
    jobs, job_pages = _platform_collection(
        transport,
        phase=phase,
        token=token,
        ledger=ledger,
        kind="jobs",
    )
    artifacts, artifact_pages = _platform_collection(
        transport,
        phase=phase,
        token=token,
        ledger=ledger,
        kind="artifacts",
    )
    pending = _json(
        _get(
            transport,
            phase=phase,
            origin=API_ORIGIN,
            path=(
                f"/repos/hcoona/three/actions/runs/{RUN_ID}/pending_deployments"
            ),
            token=token,
            ledger=ledger,
            status=frozenset({200}),
        )
    )
    if type(pending) is not list:
        raise PlatformOrphanObservationError(
            "pending deployments response is malformed"
        )
    for raw in pending:
        deployment = _object(raw, context="pending deployment")
        environment = _object(
            deployment.get("environment"),
            context="pending deployment environment",
        )
        if (
            type(deployment.get("id")) is not int
            or type(deployment.get("state")) is not str
            or type(environment.get("id")) is not int
            or type(environment.get("name")) is not str
        ):
            raise PlatformOrphanObservationError(
                "pending deployment is malformed"
            )
    _get(
        transport,
        phase=phase,
        origin=API_ORIGIN,
        path=f"/repos/hcoona/three/environments/{ENVIRONMENT_NAME}",
        token=token,
        ledger=ledger,
        status=frozenset({404}),
    )
    _get(
        transport,
        phase=phase,
        origin=API_ORIGIN,
        path=(
            "/repos/hcoona/three/git/ref/heads/"
            "workflow-delivery-v3-acceptance-retry-2-transition"
        ),
        token=token,
        ledger=ledger,
        status=frozenset({404}),
    )
    if jobs != 0 or artifacts != 0 or pending:
        raise PlatformOrphanObservationError(
            "platform orphan execution state is no longer empty"
        )
    return {
        "run_id": RUN_ID,
        "run_attempt": 1,
        "run_status": "queued",
        "run_conclusion": None,
        "run_updated_at": cast("str", run["updated_at"]),
        "workflow_id": WORKFLOW_ID,
        "workflow_state": "disabled_manually",
        "job_count": jobs,
        "pending_deployment_count": len(pending),
        "artifact_count": artifacts,
        "workflow_source_absent": True,
        "environment_absent": True,
        "transition_ref_absent": True,
        "jobs_page_count": job_pages,
        "artifact_page_count": artifact_pages,
    }


def _repository_from_manifest(value: JsonValue) -> str | None:
    if value == ACCEPTANCE_REPOSITORY_URL:
        return PLATFORM_ORPHAN_REPOSITORY
    if type(value) is dict and value == {
        "type": "git",
        "url": ACCEPTANCE_REPOSITORY_URL,
    }:
        return PLATFORM_ORPHAN_REPOSITORY
    return None


def _integrity_hex(value: JsonValue) -> str | None:
    if type(value) is not str or not value.startswith("sha512-"):
        return None
    try:
        raw = base64.b64decode(value.removeprefix("sha512-"), validate=True)
    except ValueError:
        return None
    if len(raw) != hashlib.sha512().digest_size:
        return None
    return f"sha512:{raw.hex()}"


def _manifest_tarball_path(value: JsonValue) -> str:
    if type(value) is not str or not value.isascii():
        raise PlatformOrphanObservationError("exact manifest tarball is unsafe")
    try:
        parsed = urllib.parse.urlsplit(value)
        port = parsed.port
    except ValueError:
        raise PlatformOrphanObservationError(
            "exact manifest tarball is unsafe"
        ) from None
    if (
        parsed.scheme != "https"
        or parsed.hostname != "npm.pkg.github.com"
        or parsed.username is not None
        or parsed.password is not None
        or port not in {None, 443}
        or parsed.query
        or parsed.fragment
        or not parsed.path.startswith("/")
        or re.fullmatch(
            r"/(?:[A-Za-z0-9._~!$&'()*+,;=:@/-]|%[0-9A-Fa-f]{2})*",
            parsed.path,
        )
        is None
    ):
        raise PlatformOrphanObservationError("exact manifest tarball is unsafe")
    if (
        re.fullmatch(
            (
                r"(?:/download/@hcoona/hcoona-release-smoke-npm/"
                r"0\.0\.0-wdv3-acceptance\.5/"
                r"[A-Za-z0-9][A-Za-z0-9._~-]*"
                r"|/@hcoona/hcoona-release-smoke-npm/-/"
                r"hcoona-release-smoke-npm-"
                r"0\.0\.0-wdv3-acceptance\.5\.tgz)"
            ),
            parsed.path,
        )
        is None
    ):
        raise PlatformOrphanObservationError(
            "exact manifest tarball is outside fixed .5"
        )
    return parsed.path


def _destination_observation(
    transport: QueryOnlyPlatformOrphanTransport,
    *,
    phase: str,
    token: str,
    ledger: list[RequestLedgerEntry],
) -> dict[str, JsonValue]:
    package = _object(
        _json(
            _get(
                transport,
                phase=phase,
                origin=API_ORIGIN,
                path="/users/hcoona/packages/npm/hcoona-release-smoke-npm",
                token=token,
                ledger=ledger,
                status=frozenset({200}),
            )
        ),
        context="package association response",
    )
    associated = _object(
        package.get("repository"),
        context="package repository association",
    )
    if (
        package.get("package_type") != "npm"
        or package.get("name") != "hcoona-release-smoke-npm"
        or associated.get("full_name") != PLATFORM_ORPHAN_REPOSITORY
    ):
        raise PlatformOrphanObservationError(
            "package repository association is unproved"
        )
    manifest_response = _get(
        transport,
        phase=phase,
        origin=NPM_ORIGIN,
        path=("/@hcoona%2Fhcoona-release-smoke-npm/0.0.0-wdv3-acceptance.5"),
        token=token,
        ledger=ledger,
        status=frozenset({200, 404}),
    )
    tags = _json(
        _get(
            transport,
            phase=phase,
            origin=NPM_ORIGIN,
            path="/-/package/@hcoona%2Fhcoona-release-smoke-npm/dist-tags",
            token=token,
            ledger=ledger,
            status=frozenset({200}),
        )
    )
    if type(tags) is not dict:
        raise PlatformOrphanObservationError("target tag is unreadable")
    if PACKAGE_TAG not in tags:
        tag_projection = "missing"
    else:
        target = tags[PACKAGE_TAG]
        if type(target) is not str:
            raise PlatformOrphanObservationError("target tag is unreadable")
        tag_projection = "match" if target == PACKAGE_VERSION else "mismatch"

    if manifest_response.status == 404:
        classification = (
            "absent" if tag_projection == "missing" else "conflicting"
        )
        return {
            "package_coordinate": PACKAGE_COORDINATE,
            "repository_association": PLATFORM_ORPHAN_REPOSITORY,
            "expected_tag": PACKAGE_TAG,
            "target_sha": ACCEPTANCE_TARGET,
            "classification": classification,
            "manifest_version": None,
            "tag_projection": tag_projection,
            "tarball_sha512": None,
            "manifest_digest": None,
            "package_target_witness_digest": None,
        }

    manifest = _object(_json(manifest_response), context="exact manifest")
    dist = _object(manifest.get("dist"), context="exact manifest dist")
    advertised_integrity = _integrity_hex(dist.get("integrity"))
    if advertised_integrity is None:
        raise PlatformOrphanObservationError(
            "exact manifest integrity is malformed"
        )
    tarball_path = _manifest_tarball_path(dist.get("tarball"))
    if (
        manifest.get("name") != ACCEPTANCE_PACKAGE_NAME
        or manifest.get("version") != PACKAGE_VERSION
        or _repository_from_manifest(manifest.get("repository"))
        != PLATFORM_ORPHAN_REPOSITORY
    ):
        raise PlatformOrphanObservationError("exact manifest is unproved")
    tarball = _get(
        transport,
        phase=phase,
        origin=NPM_ORIGIN,
        path=tarball_path,
        token=token,
        ledger=ledger,
        status=frozenset({200}),
        tarball=True,
    ).body
    try:
        inspected = inspect_fixed_acceptance_tarball(
            tarball,
            package_coordinate=PACKAGE_COORDINATE,
            tag=PACKAGE_TAG,
            observed_version=PACKAGE_VERSION,
            observed_tag_version=PACKAGE_VERSION,
            target_sha=ACCEPTANCE_TARGET,
        )
        entries = _read_tarball(tarball)
        packed_manifest = parse_json_strict(entries["package/package.json"])
        witness = parse_canonical_json(entries[ACCEPTANCE_WITNESS_PATH])
    except (KeyError, TypeError, ValueError):
        raise PlatformOrphanObservationError(
            "exact tarball evidence is unproved"
        ) from None
    actual_sha512 = cast("str", inspected["content-sha512"])
    if actual_sha512 != advertised_integrity:
        raise PlatformOrphanObservationError(
            "tarball bytes do not match manifest integrity"
        )
    classification = (
        "conflicting"
        if actual_sha512 != EXPECTED_TARBALL_SHA512
        else "exact"
        if tag_projection == "match"
        else "partial"
        if tag_projection == "missing"
        else "conflicting"
    )
    return {
        "package_coordinate": PACKAGE_COORDINATE,
        "repository_association": PLATFORM_ORPHAN_REPOSITORY,
        "expected_tag": PACKAGE_TAG,
        "target_sha": ACCEPTANCE_TARGET,
        "classification": classification,
        "manifest_version": PACKAGE_VERSION,
        "tag_projection": tag_projection,
        "tarball_sha512": actual_sha512,
        "manifest_digest": canonical_sha256(cast("JsonValue", packed_manifest)),
        "package_target_witness_digest": canonical_sha256(witness),
    }


def observe_platform_orphan_32809578776(
    *,
    transport: QueryOnlyPlatformOrphanTransport,
    clock: Callable[[], datetime],
    token: str,
    review_artifact: Path,
    probe_artifact: Path,
    governance_artifact: Path,
    initial_source_validator: Callable[[SourceObservation], None] | None = None,
) -> PlatformOrphanObservationData:
    """Perform the complete query-only same-invocation observation.

    This phase-2 API deliberately returns validated inputs rather than emitting
    or writing the phase-1 candidate.
    """
    started = _instant(clock)
    if started < ELIGIBLE_AFTER:
        raise PlatformOrphanObservationError(
            "Platform-Orphan cooling-off period has not elapsed"
        )
    ledger: list[RequestLedgerEntry] = []
    initial_source, _authority = _source_observation(
        transport,
        phase="initial",
        token=token,
        ledger=ledger,
    )
    if initial_source.content_sha256 != PLATFORM_ORPHAN_ACTIVE_AUTHORITY_SHA256:
        raise PlatformOrphanObservationError("active authority digest drifted")
    if initial_source_validator is not None:
        initial_source_validator(initial_source)
    acceptance = _admit_artifacts(
        review_artifact=review_artifact,
        probe_artifact=probe_artifact,
        governance_artifact=governance_artifact,
    )
    initial_platform_state = _platform_observation(
        transport,
        phase="initial",
        token=token,
        ledger=ledger,
        control_commit=initial_source.commit,
    )
    initial_platform_at = _instant(clock)
    initial_platform = ObservationEnvelope(
        "initial", _format_instant(initial_platform_at), initial_platform_state
    )
    initial_destination_state = _destination_observation(
        transport,
        phase="initial",
        token=token,
        ledger=ledger,
    )
    initial_destination_at = _instant(clock)
    initial_destination = ObservationEnvelope(
        "initial",
        _format_instant(initial_destination_at),
        initial_destination_state,
    )
    final_platform_state = _platform_observation(
        transport,
        phase="final",
        token=token,
        ledger=ledger,
        control_commit=initial_source.commit,
    )
    final_platform_at = _instant(clock)
    final_platform = ObservationEnvelope(
        "final", _format_instant(final_platform_at), final_platform_state
    )
    final_destination_state = _destination_observation(
        transport,
        phase="final",
        token=token,
        ledger=ledger,
    )
    final_destination_at = _instant(clock)
    final_destination = ObservationEnvelope(
        "final",
        _format_instant(final_destination_at),
        final_destination_state,
    )
    if canonicalize(initial_platform_state) != canonicalize(
        final_platform_state
    ):
        raise PlatformOrphanObservationError("platform state drifted")
    if canonicalize(initial_destination_state) != canonicalize(
        final_destination_state
    ):
        raise PlatformOrphanObservationError("destination state drifted")
    final_source, _final_authority = _source_observation(
        transport,
        phase="final",
        token=token,
        ledger=ledger,
    )
    if final_source != initial_source:
        raise PlatformOrphanObservationError(
            "protected control or authority source drifted"
        )
    completed = _instant(clock)
    if not (
        started
        <= initial_platform_at
        <= initial_destination_at
        <= final_platform_at
        <= final_destination_at
        <= completed
    ):
        raise PlatformOrphanObservationError(
            "clock samples do not preserve observation order"
        )
    return PlatformOrphanObservationData(
        started_at=_format_instant(started),
        completed_at=_format_instant(completed),
        initial_source=initial_source,
        final_source=final_source,
        acceptance=acceptance,
        requests=tuple(ledger),
        platform_observations=(initial_platform, final_platform),
        destination_observations=(initial_destination, final_destination),
    )


__all__ = [
    "AcceptanceArtifactObservation",
    "InjectedPlatformOrphanGetTransport",
    "ObservationEnvelope",
    "PlatformOrphanObservationData",
    "PlatformOrphanObservationError",
    "QueryOnlyPlatformOrphanTransport",
    "RequestLedgerEntry",
    "SourceObservation",
    "UrllibPlatformOrphanOneHopGet",
    "observe_platform_orphan_32809578776",
]
