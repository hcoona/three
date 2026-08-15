"""Fresh fixed-source Governance and pre-Attempt live eligibility."""

from __future__ import annotations

import hashlib
import re
from dataclasses import dataclass
from datetime import UTC, datetime, timedelta
from enum import StrEnum
from pathlib import PurePosixPath
from typing import TYPE_CHECKING, Protocol

from three_workflow_delivery_v3.canonical import (
    JsonValue,
    canonical_sha256,
    parse_canonical_json,
)
from three_workflow_delivery_v3.catalogs import catalog_digest
from three_workflow_delivery_v3.repository.compiler import (
    validate_compilation_context,
    validate_first_slice_repository_model_snapshot,
)
from three_workflow_delivery_v3.repository.descriptors import (
    FIRST_SLICE_PACKAGE,
    FIRST_SLICE_RELEASE_UNIT,
    GOVERNANCE_MAX_AGE_DAYS,
    GOVERNANCE_PATH,
    GOVERNANCE_REF,
    GOVERNANCE_REPOSITORY,
    GovernanceSource,
    ReleasePolicy,
)

if TYPE_CHECKING:
    from collections.abc import Mapping

    from three_workflow_delivery_v3.repository.compiler import (
        RepositoryModelSnapshot,
    )

ATTESTATION_SCHEMA = "workflow-delivery/v3/governance-attestation"
CONSUMER_POLICY_ID = "release/no-smoke-package-consumers-v1"
_RELEASE_POLICY_BINDING = FIRST_SLICE_RELEASE_UNIT
_WRITER_ROLES = frozenset({"Write", "Maintain", "Admin"})
_ACCESS_CATEGORIES = ("repository", "package", "manage_actions")
_ALLOWED_EXCEPTION_PATHS = frozenset(
    {
        "src/public/lib/hcoona-release-smoke-npm/package.json",
        (
            "src/public/lib/three-workflow-delivery-v3/tests/fixtures/"
            "release/consumer-policy-acceptance.json"
        ),
        (
            "src/public/lib/three-workflow-delivery-v3/tests/fixtures/"
            "acceptance/npm-publish-request/package/package.json"
        ),
    }
)
_SHA_PATTERN = re.compile(r"[0-9a-f]{40}\Z")
_OBJECT_ID_PATTERN = re.compile(r"(?:[0-9a-f]{40}|[0-9a-f]{64})\Z")
_DIGEST_PATTERN = re.compile(r"sha256:[0-9a-f]{64}\Z")
_SELECTED_REF_PREFIXES = ("refs/heads/", "refs/tags/")
_REF_FORBIDDEN_CHARACTERS = frozenset(" ~^:?*[\\")
_ASCII_CONTROL_END = 32
_ASCII_DELETE = 127


class EligibilityResult(StrEnum):
    """Closed pre-Attempt live eligibility result."""

    PASS = "pass"  # noqa: S105
    BLOCKED = "blocked"


@dataclass(frozen=True, slots=True)
class WriterInventoryEntry:
    """One accepted repository writer in the human attestation."""

    login: str
    role: str


@dataclass(frozen=True, slots=True)
class AccessGrant:
    """One human-inspected package/repository/Manage Actions grant."""

    subject: str
    access: str


def _grant_documents(grants: tuple[AccessGrant, ...]) -> list[JsonValue]:
    documents: list[JsonValue] = []
    for grant in grants:
        document: dict[str, JsonValue] = {
            "subject": grant.subject,
            "access": grant.access,
        }
        documents.append(document)
    return documents


@dataclass(frozen=True, slots=True)
class AccessInventory:
    """Explicit human-inspected access inventory."""

    repository: tuple[AccessGrant, ...]
    package: tuple[AccessGrant, ...]
    manage_actions: tuple[AccessGrant, ...]

    def to_document(self) -> dict[str, JsonValue]:
        """Return the strict access inventory payload."""
        repository = _grant_documents(self.repository)
        package = _grant_documents(self.package)
        manage_actions = _grant_documents(self.manage_actions)
        return {
            "repository": repository,
            "package": package,
            "manage_actions": manage_actions,
        }


@dataclass(frozen=True, slots=True)
class GovernanceAttestation:
    """Strict non-executable protected-source human attestation."""

    release_policy: str
    package: str
    issuer: str
    inspected_at: datetime
    expires_at: datetime
    accepted_writers: tuple[WriterInventoryEntry, ...]
    access_inventory: AccessInventory | None
    access_evidence_digest: str | None
    limitations: tuple[str, ...]
    live_enabled: bool

    def to_document(self) -> dict[str, JsonValue]:
        """Return canonical attestation content."""
        accepted_writers: list[JsonValue] = []
        for writer in self.accepted_writers:
            writer_document: dict[str, JsonValue] = {
                "login": writer.login,
                "role": writer.role,
            }
            accepted_writers.append(writer_document)
        limitations: list[JsonValue] = list(self.limitations)
        document: dict[str, JsonValue] = {
            "schema": ATTESTATION_SCHEMA,
            "release_policy": self.release_policy,
            "package": self.package,
            "issuer": self.issuer,
            "inspected_at": _format_instant(self.inspected_at),
            "expires_at": _format_instant(self.expires_at),
            "accepted_writers": accepted_writers,
            "limitations": limitations,
            "live_enabled": self.live_enabled,
        }
        if self.access_inventory is not None:
            document["access_inventory"] = self.access_inventory.to_document()
        if self.access_evidence_digest is not None:
            document["access_evidence_digest"] = self.access_evidence_digest
        return document

    @property
    def content_digest(self) -> str:
        """Return the canonical attestation content digest."""
        return canonical_sha256(self.to_document())


@dataclass(frozen=True, slots=True)
class GovernanceBlob:
    """One contents-read-compatible fixed-path blob response."""

    blob_oid: str
    content: bytes


class GovernanceSourceClient(Protocol):
    """Read-only protocol required for a fresh protected-source observation."""

    def is_ref_protected(self, repository: str, ref: str) -> bool:
        """Return whether the exact fully qualified ref is protected."""
        ...

    def resolve_ref(self, repository: str, ref: str) -> str:
        """Resolve the exact ref to one full commit SHA."""
        ...

    def read_blob(
        self,
        repository: str,
        commit: str,
        path: str,
    ) -> GovernanceBlob:
        """Read the exact path at the already resolved commit."""
        ...


class GovernanceRejectionError(ValueError):
    """Authoritatively observed Governance state definitively rejected."""


class GovernanceFreshnessRejectionError(GovernanceRejectionError):
    """Exact fresh Governance identity was validly read but rejected."""


@dataclass(frozen=True, slots=True)
class GovernanceObservation:
    """Fresh source provenance and validated canonical attestation."""

    source: GovernanceSource
    resolved_commit: str
    blob_oid: str
    content_sha256: str
    observed_at: datetime
    attestation: GovernanceAttestation


@dataclass(frozen=True, slots=True)
class SurfaceDigest:
    """One exact target dependency surface and its content digest."""

    path: str
    content_digest: str

    def to_document(self) -> dict[str, JsonValue]:
        """Return the canonical surface binding."""
        return {
            "path": self.path,
            "content-digest": self.content_digest,
        }


@dataclass(frozen=True, slots=True)
class ConsumerPolicyResult:
    """Permanent target-bound input abstraction for consumer policy."""

    policy_id: str
    policy_digest: str
    target: str
    scanned_surfaces: tuple[SurfaceDigest, ...]
    admitted_exceptions: tuple[SurfaceDigest, ...]
    consumers: tuple[str, ...]

    def to_document(self) -> dict[str, JsonValue]:
        """Return the canonical consumer-policy result."""
        scanned_surfaces: list[JsonValue] = [
            surface.to_document() for surface in self.scanned_surfaces
        ]
        admitted_exceptions: list[JsonValue] = [
            surface.to_document() for surface in self.admitted_exceptions
        ]
        consumers: list[JsonValue] = list(self.consumers)
        return {
            "schema": "workflow-delivery/v3/consumer-policy-result",
            "policy-id": self.policy_id,
            "policy-digest": self.policy_digest,
            "target": self.target,
            "scanned-surfaces": scanned_surfaces,
            "admitted-exceptions": admitted_exceptions,
            "consumers": consumers,
        }

    @property
    def result_digest(self) -> str:
        """Return the complete target-bound consumer-policy digest."""
        return canonical_sha256(self.to_document())


@dataclass(frozen=True, slots=True)
class LiveEligibilityContext:
    """Current pre-Attempt authority for one live eligibility evaluation."""

    purpose: str
    request_id: str
    workflow_run_id: int
    run_attempt: int
    selected_ref: str
    target: str
    repository_model_digest: str
    producer: str
    control: str
    release_policy_digest: str
    catalog_digest: str


@dataclass(frozen=True, slots=True)
class LiveEligibilityDecision:
    """Immutable exact-target pre-Attempt live eligibility Decision."""

    context: LiveEligibilityContext
    consumer_policy: ConsumerPolicyResult
    governance: GovernanceObservation
    result: EligibilityResult
    diagnostics: tuple[str, ...]

    def to_document(self) -> dict[str, JsonValue]:
        """Return the complete canonical Decision payload."""
        context: dict[str, JsonValue] = {
            "purpose": self.context.purpose,
            "request-id": self.context.request_id,
            "workflow-run-id": self.context.workflow_run_id,
            "run-attempt": self.context.run_attempt,
            "selected-ref": self.context.selected_ref,
            "target": self.context.target,
            "repository-model-digest": self.context.repository_model_digest,
            "producer": self.context.producer,
            "control": self.context.control,
            "release-policy-digest": self.context.release_policy_digest,
            "catalog-digest": self.context.catalog_digest,
        }
        scanned_surfaces: list[JsonValue] = [
            surface.to_document()
            for surface in self.consumer_policy.scanned_surfaces
        ]
        admitted_exceptions: list[JsonValue] = [
            surface.to_document()
            for surface in self.consumer_policy.admitted_exceptions
        ]
        consumers: list[JsonValue] = list(self.consumer_policy.consumers)
        consumer_policy: dict[str, JsonValue] = {
            "policy-id": self.consumer_policy.policy_id,
            "policy-digest": self.consumer_policy.policy_digest,
            "result-digest": self.consumer_policy.result_digest,
            "target": self.consumer_policy.target,
            "scanned-surfaces": scanned_surfaces,
            "admitted-exceptions": admitted_exceptions,
            "consumers": consumers,
        }
        governance: dict[str, JsonValue] = {
            "repository": self.governance.source.repository,
            "ref": self.governance.source.ref,
            "resolved-commit": self.governance.resolved_commit,
            "path": self.governance.source.path,
            "blob-oid": self.governance.blob_oid,
            "content-sha256": self.governance.content_sha256,
            "observed-at": _format_instant(self.governance.observed_at),
            "max-age-days": self.governance.source.max_age_days,
            "live-enabled": self.governance.attestation.live_enabled,
            "issuer": self.governance.attestation.issuer,
            "inspected-at": _format_instant(
                self.governance.attestation.inspected_at
            ),
            "expires-at": _format_instant(
                self.governance.attestation.expires_at
            ),
            "attestation-content-digest": (
                self.governance.attestation.content_digest
            ),
        }
        diagnostics: list[JsonValue] = list(self.diagnostics)
        return {
            "schema": "workflow-delivery/v3/live-eligibility-decision",
            "context": context,
            "consumer-policy": consumer_policy,
            "governance": governance,
            "result": self.result.value,
            "diagnostics": diagnostics,
        }

    @property
    def decision_digest(self) -> str:
        """Return the complete canonical Decision digest."""
        return canonical_sha256(self.to_document())


def _closed(
    document: Mapping[str, JsonValue],
    *,
    required: frozenset[str],
    optional: frozenset[str] = frozenset(),
    context: str,
) -> None:
    missing = required - document.keys()
    if missing:
        message = f"{context} missing required field: {sorted(missing)[0]}"
        raise ValueError(message)
    unknown = document.keys() - required - optional
    if unknown:
        message = f"{context} unknown field: {sorted(unknown)[0]}"
        raise ValueError(message)


def _object(value: JsonValue, *, context: str) -> dict[str, JsonValue]:
    if not isinstance(value, dict):
        message = f"{context} must be an object"
        raise TypeError(message)
    return value


def _array(value: JsonValue, *, context: str) -> list[JsonValue]:
    if not isinstance(value, list):
        message = f"{context} must be an array"
        raise TypeError(message)
    return value


def _string(value: JsonValue, *, context: str) -> str:
    if not isinstance(value, str) or not value.strip():
        message = f"{context} must be a nonempty string"
        raise TypeError(message)
    return value


def _parse_instant(value: JsonValue, *, context: str) -> datetime:
    text = _string(value, context=context)
    try:
        instant = datetime.strptime(text, "%Y-%m-%dT%H:%M:%SZ").replace(
            tzinfo=UTC
        )
    except ValueError as error:
        message = f"{context} must be a UTC second-precision instant"
        raise ValueError(message) from error
    return instant


def _format_instant(value: datetime) -> str:
    return value.astimezone(UTC).strftime("%Y-%m-%dT%H:%M:%SZ")


def _writer_inventory(value: JsonValue) -> tuple[WriterInventoryEntry, ...]:
    writers: list[WriterInventoryEntry] = []
    for index, item in enumerate(
        _array(value, context="accepted_writers"),
    ):
        document = _object(
            item,
            context=f"accepted_writers[{index}]",
        )
        _closed(
            document,
            required=frozenset({"login", "role"}),
            context=f"accepted_writers[{index}]",
        )
        writer = WriterInventoryEntry(
            login=_string(
                document["login"],
                context=f"accepted_writers[{index}].login",
            ),
            role=_string(
                document["role"],
                context=f"accepted_writers[{index}].role",
            ),
        )
        if writer.role not in _WRITER_ROLES:
            message = f"accepted_writers[{index}].role is not accepted"
            raise ValueError(message)
        writers.append(writer)
    if not writers:
        message = "accepted_writers must be nonempty"
        raise ValueError(message)
    if len({writer.login for writer in writers}) != len(writers):
        message = "accepted_writers contains duplicate logins"
        raise ValueError(message)
    return tuple(writers)


def _grants(value: JsonValue, *, category: str) -> tuple[AccessGrant, ...]:
    grants: list[AccessGrant] = []
    for index, item in enumerate(_array(value, context=category)):
        document = _object(item, context=f"{category}[{index}]")
        _closed(
            document,
            required=frozenset({"subject", "access"}),
            context=f"{category}[{index}]",
        )
        grants.append(
            AccessGrant(
                subject=_string(
                    document["subject"],
                    context=f"{category}[{index}].subject",
                ),
                access=_string(
                    document["access"],
                    context=f"{category}[{index}].access",
                ),
            )
        )
    if not grants:
        message = f"{category} access inventory must be nonempty"
        raise ValueError(message)
    if len({(grant.subject, grant.access) for grant in grants}) != len(grants):
        message = f"{category} access inventory contains duplicate grants"
        raise ValueError(message)
    return tuple(grants)


def _access_inventory(value: JsonValue) -> AccessInventory:
    document = _object(value, context="access_inventory")
    _closed(
        document,
        required=frozenset(_ACCESS_CATEGORIES),
        context="access_inventory",
    )
    return AccessInventory(
        repository=_grants(
            document["repository"],
            category="access_inventory.repository",
        ),
        package=_grants(
            document["package"],
            category="access_inventory.package",
        ),
        manage_actions=_grants(
            document["manage_actions"],
            category="access_inventory.manage_actions",
        ),
    )


def parse_governance_attestation(
    content: bytes | bytearray,
) -> GovernanceAttestation:
    """Parse and validate one canonical non-executable human attestation."""
    document = parse_canonical_json(content)
    required = frozenset(
        {
            "schema",
            "release_policy",
            "package",
            "issuer",
            "inspected_at",
            "expires_at",
            "accepted_writers",
            "limitations",
            "live_enabled",
        }
    )
    optional = frozenset({"access_inventory", "access_evidence_digest"})
    _closed(
        document,
        required=required,
        optional=optional,
        context="Governance attestation",
    )
    if document["schema"] != ATTESTATION_SCHEMA:
        message = "Governance attestation has the wrong schema"
        raise ValueError(message)
    release_policy = _string(
        document["release_policy"],
        context="release_policy",
    )
    package = _string(document["package"], context="package")
    if (
        release_policy != _RELEASE_POLICY_BINDING
        or package != FIRST_SLICE_PACKAGE
    ):
        message = "Governance attestation policy/package binding mismatch"
        raise ValueError(message)
    live_enabled = document["live_enabled"]
    if not isinstance(live_enabled, bool):
        message = "Governance attestation live_enabled must be Boolean"
        raise TypeError(message)
    inspected_at = _parse_instant(
        document["inspected_at"],
        context="inspected_at",
    )
    expires_at = _parse_instant(
        document["expires_at"],
        context="expires_at",
    )
    lifetime = expires_at - inspected_at
    if lifetime <= timedelta(0) or lifetime > timedelta(
        days=GOVERNANCE_MAX_AGE_DAYS
    ):
        message = "Governance attestation expiry must be within 90 days"
        raise ValueError(message)
    has_inventory = "access_inventory" in document
    has_evidence = "access_evidence_digest" in document
    if has_inventory == has_evidence:
        message = (
            "Governance attestation requires exactly one access inventory "
            "or evidence digest"
        )
        raise ValueError(message)
    access_inventory = (
        _access_inventory(document["access_inventory"])
        if has_inventory
        else None
    )
    evidence_digest = (
        _string(
            document["access_evidence_digest"],
            context="access_evidence_digest",
        )
        if has_evidence
        else None
    )
    if (
        evidence_digest is not None
        and _DIGEST_PATTERN.fullmatch(evidence_digest) is None
    ):
        message = "access_evidence_digest must be a prefixed SHA-256"
        raise ValueError(message)
    limitations = tuple(
        _string(item, context="limitations")
        for item in _array(document["limitations"], context="limitations")
    )
    if not limitations:
        message = "Governance attestation limitations must be nonempty"
        raise ValueError(message)
    if len(set(limitations)) != len(limitations):
        message = "Governance attestation limitations contain duplicates"
        raise ValueError(message)
    attestation = GovernanceAttestation(
        release_policy=release_policy,
        package=package,
        issuer=_string(document["issuer"], context="issuer"),
        inspected_at=inspected_at,
        expires_at=expires_at,
        accepted_writers=_writer_inventory(document["accepted_writers"]),
        access_inventory=access_inventory,
        access_evidence_digest=evidence_digest,
        limitations=limitations,
        live_enabled=live_enabled,
    )
    if attestation.to_document() != document:
        message = "Governance attestation is not in normalized schema order"
        raise ValueError(message)
    return attestation


def _validate_source(source: GovernanceSource) -> None:
    expected = GovernanceSource(
        repository=GOVERNANCE_REPOSITORY,
        ref=GOVERNANCE_REF,
        path=GOVERNANCE_PATH,
        max_age_days=GOVERNANCE_MAX_AGE_DAYS,
    )
    if source != expected:
        message = "Governance source is not the exact fixed contract"
        raise ValueError(message)


def _utc_now(now: datetime) -> datetime:
    if now.tzinfo is None or now.utcoffset() != timedelta(0):
        message = "eligibility observation time must be UTC-aware"
        raise ValueError(message)
    return now.astimezone(UTC)


def observe_governance_source(
    source: GovernanceSource,
    client: GovernanceSourceClient,
    *,
    now: datetime,
) -> GovernanceObservation:
    """Freshly resolve and read the exact protected Governance source."""
    _validate_source(source)
    observed_at = _utc_now(now)
    protected = client.is_ref_protected(source.repository, source.ref)
    if type(protected) is not bool:
        message = "Governance ref protection response is malformed"
        raise ValueError(message)
    if not protected:
        message = "Governance ref is not protected"
        raise GovernanceRejectionError(message)
    resolved_commit = client.resolve_ref(source.repository, source.ref)
    if _SHA_PATTERN.fullmatch(resolved_commit) is None:
        message = "Governance ref did not resolve to a full commit SHA"
        raise ValueError(message)
    blob = client.read_blob(
        source.repository,
        resolved_commit,
        source.path,
    )
    if _OBJECT_ID_PATTERN.fullmatch(blob.blob_oid) is None:
        message = "Governance blob OID is malformed"
        raise ValueError(message)
    try:
        attestation = parse_governance_attestation(blob.content)
    except (TypeError, ValueError, UnicodeError) as error:
        raise GovernanceRejectionError(str(error)) from error
    content_sha256 = f"sha256:{hashlib.sha256(blob.content).hexdigest()}"
    if content_sha256 != attestation.content_digest:
        message = "Governance canonical content digest mismatch"
        raise GovernanceRejectionError(message)
    return GovernanceObservation(
        source=source,
        resolved_commit=resolved_commit,
        blob_oid=blob.blob_oid,
        content_sha256=content_sha256,
        observed_at=observed_at,
        attestation=attestation,
    )


def governance_observation_provenance(
    observation: GovernanceObservation,
) -> tuple[tuple[str, str], ...]:
    """Return the canonical fixed-source provenance comparison."""
    if type(observation) is not GovernanceObservation:
        message = "Governance observation has the wrong runtime type"
        raise TypeError(message)
    return tuple(
        sorted(
            (
                ("repository", observation.source.repository),
                ("ref", observation.source.ref),
                ("path", observation.source.path),
                ("resolved-commit", observation.resolved_commit),
                ("blob-oid", observation.blob_oid),
                ("content-sha256", observation.content_sha256),
            )
        )
    )


def require_fresh_governance_identity(  # noqa: PLR0913
    source: GovernanceSource,
    client: GovernanceSourceClient,
    *,
    now: datetime,
    expected_provenance: tuple[tuple[str, str], ...],
    expected_content_sha256: str,
    expected_expires_at: str,
    expected_live_enabled: bool,
) -> GovernanceObservation:
    """Require current fixed-source Governance identity and validity."""
    observation = observe_governance_source(source, client, now=now)
    provenance = governance_observation_provenance(observation)
    if (
        provenance != expected_provenance
        or observation.content_sha256 != expected_content_sha256
        or _format_instant(observation.attestation.expires_at)
        != expected_expires_at
        or observation.attestation.live_enabled is not expected_live_enabled
        or not observation.attestation.live_enabled
        or observation.attestation.inspected_at > observation.observed_at
        or observation.attestation.expires_at <= observation.observed_at
    ):
        message = "Governance freshness comparison failed"
        raise GovernanceFreshnessRejectionError(message)
    return observation


def _normalized_path(value: str, *, field: str) -> str:
    path = PurePosixPath(value)
    if (
        not value
        or path.is_absolute()
        or ".." in path.parts
        or "\\" in value
        or path.as_posix() != value
    ):
        message = f"{field} must be a normalized repository-relative path"
        raise ValueError(message)
    return value


def _validate_surfaces(
    surfaces: tuple[SurfaceDigest, ...],
    *,
    field: str,
) -> None:
    if not surfaces:
        message = f"{field} must be nonempty"
        raise ValueError(message)
    expected_order = tuple(sorted(surfaces, key=lambda surface: surface.path))
    if surfaces != expected_order:
        message = f"{field} must be sorted by path"
        raise ValueError(message)
    paths: set[str] = set()
    for surface in surfaces:
        _normalized_path(surface.path, field=f"{field}.path")
        if surface.path in paths:
            message = f"{field} contains a duplicate path"
            raise ValueError(message)
        paths.add(surface.path)
        if _DIGEST_PATTERN.fullmatch(surface.content_digest) is None:
            message = f"{field}.content_digest must be SHA-256"
            raise ValueError(message)


def validate_consumer_policy_result(  # noqa: C901
    result: ConsumerPolicyResult,
) -> None:
    """Validate the permanent target-bound consumer-policy input."""
    if result.policy_id != CONSUMER_POLICY_ID:
        message = "consumer-policy ID is not the static first-slice policy"
        raise ValueError(message)
    if _DIGEST_PATTERN.fullmatch(result.policy_digest) is None:
        message = "consumer-policy digest must be SHA-256"
        raise ValueError(message)
    if _SHA_PATTERN.fullmatch(result.target) is None:
        message = "consumer-policy target must be a full commit SHA"
        raise ValueError(message)
    _validate_surfaces(result.scanned_surfaces, field="scanned_surfaces")
    scanned = {
        surface.path: surface.content_digest
        for surface in result.scanned_surfaces
    }
    if result.admitted_exceptions:
        _validate_surfaces(
            result.admitted_exceptions,
            field="admitted_exceptions",
        )
    for exception in result.admitted_exceptions:
        if (
            exception.path not in _ALLOWED_EXCEPTION_PATHS
            or scanned.get(exception.path) != exception.content_digest
        ):
            message = (
                "consumer-policy exception is not digest-bound/allowlisted"
            )
            raise ValueError(message)
    if result.consumers != tuple(sorted(result.consumers)):
        message = "consumer-policy consumers must be sorted"
        raise ValueError(message)
    if len(set(result.consumers)) != len(result.consumers):
        message = "consumer-policy consumers contain duplicates"
        raise ValueError(message)
    for consumer in result.consumers:
        _normalized_path(consumer, field="consumer-policy consumer")
        if consumer not in scanned:
            message = "consumer-policy consumer was not in scanned surfaces"
            raise ValueError(message)


def release_policy_digest(policy: ReleasePolicy) -> str:
    """Return a canonical digest for the normalized Release policy."""
    document: dict[str, JsonValue] = {
        "schema": "workflow-delivery/v3/release-policy",
        "release-unit": policy.release_unit,
        "governance": {
            "attestation": {
                "repository": policy.governance.repository,
                "ref": policy.governance.ref,
                "path": policy.governance.path,
                "max-age-days": policy.governance.max_age_days,
            },
        },
        "channels": {
            name: {
                "quality": list(channel.quality),
                "projections": [
                    {
                        "destination": projection.destination,
                        "artifact": projection.artifact,
                        "package": projection.package,
                    }
                    for projection in channel.projections
                ],
            }
            for name, channel in policy.channels
        },
    }
    return canonical_sha256(document)


def _nonempty_exact_string(value: object, *, field: str) -> str:
    if type(value) is not str or not value or value != value.strip():
        message = f"live eligibility {field} must be a nonempty exact string"
        raise TypeError(message)
    return value


def _validate_selected_ref(value: object) -> str:
    selected_ref = _nonempty_exact_string(value, field="selected_ref")
    prefix = next(
        (
            candidate
            for candidate in _SELECTED_REF_PREFIXES
            if selected_ref.startswith(candidate)
        ),
        None,
    )
    if prefix is None:
        message = "live eligibility selected_ref has an unsupported namespace"
        raise ValueError(message)
    suffix = selected_ref.removeprefix(prefix)
    components = suffix.split("/")
    if (
        not suffix
        or suffix.endswith(("/", "."))
        or ".." in suffix
        or "@{" in suffix
        or any(
            not component
            or component.startswith(".")
            or component.endswith(".lock")
            for component in components
        )
        or any(
            ord(character) < _ASCII_CONTROL_END
            or ord(character) == _ASCII_DELETE
            or character in _REF_FORBIDDEN_CHARACTERS
            for character in suffix
        )
    ):
        message = "live eligibility selected_ref is not a valid Git ref"
        raise ValueError(message)
    return selected_ref


def _validate_live_context(  # noqa: C901
    context: LiveEligibilityContext,
    snapshot: RepositoryModelSnapshot,
    policy: ReleasePolicy,
) -> None:
    if type(context) is not LiveEligibilityContext:
        message = "live eligibility context has the wrong runtime type"
        raise TypeError(message)
    _nonempty_exact_string(context.purpose, field="purpose")
    if context.purpose != "live-release":
        message = "live eligibility requires live-release purpose"
        raise ValueError(message)
    _nonempty_exact_string(context.request_id, field="request_id")
    _validate_selected_ref(context.selected_ref)
    for field, value in (
        ("workflow_run_id", context.workflow_run_id),
        ("run_attempt", context.run_attempt),
    ):
        if type(value) is not int or value <= 0:
            message = f"live eligibility {field} must be a positive integer"
            raise ValueError(message)
    if (
        type(context.target) is not str
        or _SHA_PATTERN.fullmatch(context.target) is None
    ):
        message = "live eligibility target must be a full commit SHA"
        raise ValueError(message)
    _nonempty_exact_string(context.producer, field="producer")
    _nonempty_exact_string(context.control, field="control")
    for field, value in (
        ("repository_model_digest", context.repository_model_digest),
        ("release_policy_digest", context.release_policy_digest),
        ("catalog_digest", context.catalog_digest),
    ):
        if type(value) is not str or _DIGEST_PATTERN.fullmatch(value) is None:
            message = f"live eligibility {field} must be SHA-256"
            raise ValueError(message)
    if context.catalog_digest != catalog_digest():
        message = "live eligibility catalog digest mismatch"
        raise ValueError(message)
    if context.release_policy_digest != release_policy_digest(policy):
        message = "live eligibility Release policy digest mismatch"
        raise ValueError(message)
    validate_first_slice_repository_model_snapshot(snapshot)
    if context.repository_model_digest != snapshot.snapshot_digest:
        message = "live eligibility Repository Model is not exact and ready"
        raise ValueError(message)
    expected_snapshot = (
        context.request_id,
        context.purpose,
        context.workflow_run_id,
        context.run_attempt,
        context.target,
        context.control,
        context.catalog_digest,
    )
    actual_snapshot = (
        snapshot.context.request_id,
        snapshot.context.purpose,
        snapshot.context.workflow_run_id,
        snapshot.context.run_attempt,
        snapshot.context.target,
        snapshot.context.control,
        snapshot.context.catalog_digest,
    )
    if actual_snapshot != expected_snapshot:
        message = "live eligibility Repository Model binding mismatch"
        raise ValueError(message)
    validate_compilation_context(snapshot.context)


def evaluate_live_eligibility(  # noqa: PLR0913
    context: LiveEligibilityContext,
    snapshot: RepositoryModelSnapshot,
    consumer_policy: ConsumerPolicyResult,
    policy: ReleasePolicy,
    client: GovernanceSourceClient,
    *,
    now: datetime,
) -> LiveEligibilityDecision:
    """Evaluate current exact-target eligibility before Attempt creation."""
    _validate_source(policy.governance)
    _validate_live_context(context, snapshot, policy)
    validate_consumer_policy_result(consumer_policy)
    if consumer_policy.target != context.target:
        message = "consumer-policy target does not match live eligibility"
        raise ValueError(message)
    governance = observe_governance_source(
        policy.governance,
        client,
        now=now,
    )
    diagnostics: list[str] = []
    if consumer_policy.consumers:
        diagnostics.append("consumer-policy-found-consumers")
    if not governance.attestation.live_enabled:
        diagnostics.append("governance-live-disabled")
    if now < governance.attestation.inspected_at:
        diagnostics.append("governance-attestation-not-yet-valid")
    if now >= governance.attestation.expires_at:
        diagnostics.append("governance-attestation-expired")
    result = (
        EligibilityResult.BLOCKED if diagnostics else EligibilityResult.PASS
    )
    return LiveEligibilityDecision(
        context=context,
        consumer_policy=consumer_policy,
        governance=governance,
        result=result,
        diagnostics=tuple(diagnostics),
    )
