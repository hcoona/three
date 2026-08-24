"""Fresh fixed-source Governance and pre-Attempt live eligibility."""

from __future__ import annotations

import hashlib
import re
from dataclasses import dataclass
from datetime import UTC, datetime, timedelta
from enum import StrEnum
from typing import TYPE_CHECKING, Protocol

from three_workflow_delivery_v3.canonical import (
    JsonValue,
    canonical_sha256,
    parse_canonical_json,
)
from three_workflow_delivery_v3.catalogs import catalog_digest
from three_workflow_delivery_v3.records.release import ReleaseIntent
from three_workflow_delivery_v3.release.consumer_policy import (
    CONSUMER_POLICY_ID as _CONSUMER_POLICY_ID,
)
from three_workflow_delivery_v3.release.consumer_policy import (
    ConsumerPolicyResult,
    SurfaceDigest,
    validate_consumer_policy_result,
)
from three_workflow_delivery_v3.release.identity import (
    BUDDY_LIVE_WORKFLOW_PATH,
)
from three_workflow_delivery_v3.repository.compiler import (
    AdmittedRepositoryModelSnapshot,
    compile_release_policy,
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

CONSUMER_POLICY_ID = _CONSUMER_POLICY_ID

ATTESTATION_SCHEMA = "workflow-delivery/v3/governance-attestation"
LIVE_ELIGIBILITY_DECISION_SCHEMA = (
    "workflow-delivery/v3/live-eligibility-decision"
)
LIVE_ELIGIBILITY_PRODUCER = "evaluate-live-eligibility"
_RELEASE_POLICY_BINDING = FIRST_SLICE_RELEASE_UNIT
_WRITER_ROLES = frozenset({"Write", "Maintain", "Admin"})
_ACCESS_CATEGORIES = ("repository", "package", "manage_actions")
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


class LiveEligibilityAdmissionMode(StrEnum):
    """Freshness branch fixed by the trusted lifecycle caller."""

    CURRENT_FRESHNESS = "current-freshness"
    CAPABILITY_REPLAY = "capability-replay"


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
class LiveEligibilityGovernanceBinding:
    """Complete Governance projection in the eligibility Decision."""

    source: GovernanceSource
    resolved_commit: str
    blob_oid: str
    content_sha256: str
    observed_at: datetime
    live_enabled: bool
    issuer: str
    inspected_at: datetime
    expires_at: datetime
    attestation_content_digest: str

    @classmethod
    def from_observation(
        cls,
        observation: GovernanceObservation,
    ) -> LiveEligibilityGovernanceBinding:
        """Project the exact evaluator observation into transport fields."""
        return cls(
            source=observation.source,
            resolved_commit=observation.resolved_commit,
            blob_oid=observation.blob_oid,
            content_sha256=observation.content_sha256,
            observed_at=observation.observed_at,
            live_enabled=observation.attestation.live_enabled,
            issuer=observation.attestation.issuer,
            inspected_at=observation.attestation.inspected_at,
            expires_at=observation.attestation.expires_at,
            attestation_content_digest=(observation.attestation.content_digest),
        )

    def to_document(self) -> dict[str, JsonValue]:
        """Return the closed Governance Decision binding."""
        return {
            "repository": self.source.repository,
            "ref": self.source.ref,
            "resolved-commit": self.resolved_commit,
            "path": self.source.path,
            "blob-oid": self.blob_oid,
            "content-sha256": self.content_sha256,
            "observed-at": _format_instant(self.observed_at),
            "max-age-days": self.source.max_age_days,
            "live-enabled": self.live_enabled,
            "issuer": self.issuer,
            "inspected-at": _format_instant(self.inspected_at),
            "expires-at": _format_instant(self.expires_at),
            "attestation-content-digest": (self.attestation_content_digest),
        }

    @property
    def provenance(self) -> tuple[tuple[str, str], ...]:
        """Return the complete fixed-source provenance comparison."""
        return tuple(
            sorted(
                (
                    ("repository", self.source.repository),
                    ("ref", self.source.ref),
                    ("path", self.source.path),
                    ("resolved-commit", self.resolved_commit),
                    ("blob-oid", self.blob_oid),
                    ("content-sha256", self.content_sha256),
                )
            )
        )


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
        return _live_eligibility_document(
            context=self.context,
            consumer_policy=self.consumer_policy,
            governance=LiveEligibilityGovernanceBinding.from_observation(
                self.governance
            ),
            result=self.result,
            diagnostics=self.diagnostics,
        )

    @property
    def decision_digest(self) -> str:
        """Return the complete canonical Decision digest."""
        return canonical_sha256(self.to_document())


@dataclass(frozen=True, slots=True)
class AdmittedLiveEligibilityDecision:
    """Strict canonical current-attempt Live Eligibility transport."""

    context: LiveEligibilityContext
    consumer_policy: ConsumerPolicyResult
    governance: LiveEligibilityGovernanceBinding
    result: EligibilityResult
    diagnostics: tuple[str, ...]
    canonical_digest: str
    canonical_bytes: bytes

    def __post_init__(self) -> None:
        """Reject an internally inconsistent admitted transport wrapper."""
        if (
            type(self.context) is not LiveEligibilityContext
            or type(self.consumer_policy) is not ConsumerPolicyResult
            or type(self.governance) is not LiveEligibilityGovernanceBinding
            or type(self.result) is not EligibilityResult
            or type(self.diagnostics) is not tuple
            or type(self.canonical_digest) is not str
            or _DIGEST_PATTERN.fullmatch(self.canonical_digest) is None
            or type(self.canonical_bytes) is not bytes
        ):
            message = "Live Eligibility Decision admission integrity failed"
            raise TypeError(message)
        document = parse_canonical_json(self.canonical_bytes)
        if (
            self.to_document() != document
            or canonical_sha256(document) != self.canonical_digest
        ):
            message = "Live Eligibility Decision admission integrity failed"
            raise ValueError(message)

    def to_document(self) -> dict[str, JsonValue]:
        """Return the exact admitted Decision document."""
        return _live_eligibility_document(
            context=self.context,
            consumer_policy=self.consumer_policy,
            governance=self.governance,
            result=self.result,
            diagnostics=self.diagnostics,
        )

    @property
    def decision_digest(self) -> str:
        """Return the admitted canonical Decision digest."""
        return self.canonical_digest


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


def _live_eligibility_document(
    *,
    context: LiveEligibilityContext,
    consumer_policy: ConsumerPolicyResult,
    governance: LiveEligibilityGovernanceBinding,
    result: EligibilityResult,
    diagnostics: tuple[str, ...],
) -> dict[str, JsonValue]:
    context_document: dict[str, JsonValue] = {
        "purpose": context.purpose,
        "request-id": context.request_id,
        "workflow-run-id": context.workflow_run_id,
        "run-attempt": context.run_attempt,
        "selected-ref": context.selected_ref,
        "target": context.target,
        "repository-model-digest": context.repository_model_digest,
        "producer": context.producer,
        "control": context.control,
        "release-policy-digest": context.release_policy_digest,
        "catalog-digest": context.catalog_digest,
    }
    scanned_surfaces: list[JsonValue] = [
        surface.to_document() for surface in consumer_policy.scanned_surfaces
    ]
    admitted_exceptions: list[JsonValue] = [
        surface.to_document() for surface in consumer_policy.admitted_exceptions
    ]
    consumers: list[JsonValue] = list(consumer_policy.consumers)
    consumer_policy_document: dict[str, JsonValue] = {
        "policy-id": consumer_policy.policy_id,
        "policy-digest": consumer_policy.policy_digest,
        "result-digest": consumer_policy.result_digest,
        "target": consumer_policy.target,
        "scanned-surfaces": scanned_surfaces,
        "admitted-exceptions": admitted_exceptions,
        "consumers": consumers,
    }
    diagnostic_documents: list[JsonValue] = list(diagnostics)
    return {
        "schema": LIVE_ELIGIBILITY_DECISION_SCHEMA,
        "context": context_document,
        "consumer-policy": consumer_policy_document,
        "governance": governance.to_document(),
        "result": result.value,
        "diagnostics": diagnostic_documents,
    }


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


def _decision_integer(value: JsonValue, *, field: str) -> int:
    if type(value) is not int or value <= 0:
        message = (
            f"Live Eligibility Decision {field} must be a positive integer"
        )
        raise TypeError(message)
    return value


def _decision_boolean(value: JsonValue, *, field: str) -> bool:
    if type(value) is not bool:
        message = f"Live Eligibility Decision {field} must be Boolean"
        raise TypeError(message)
    return value


def _decision_digest(value: JsonValue, *, field: str) -> str:
    digest = _nonempty_exact_string(value, field=field)
    if _DIGEST_PATTERN.fullmatch(digest) is None:
        message = f"Live Eligibility Decision {field} must be SHA-256"
        raise ValueError(message)
    return digest


def _decision_sha(value: JsonValue, *, field: str) -> str:
    sha = _nonempty_exact_string(value, field=field)
    if _SHA_PATTERN.fullmatch(sha) is None:
        message = f"Live Eligibility Decision {field} must be a full commit SHA"
        raise ValueError(message)
    return sha


def _decision_strings(
    value: JsonValue,
    *,
    field: str,
) -> tuple[str, ...]:
    return tuple(
        _nonempty_exact_string(item, field=f"{field}[{index}]")
        for index, item in enumerate(_array(value, context=field))
    )


def _decision_surfaces(
    value: JsonValue,
    *,
    field: str,
) -> tuple[SurfaceDigest, ...]:
    surfaces: list[SurfaceDigest] = []
    for index, item in enumerate(_array(value, context=field)):
        document = _object(item, context=f"{field}[{index}]")
        _closed(
            document,
            required=frozenset({"path", "content-digest"}),
            context=f"{field}[{index}]",
        )
        surfaces.append(
            SurfaceDigest(
                path=_nonempty_exact_string(
                    document["path"],
                    field=f"{field}[{index}].path",
                ),
                content_digest=_decision_digest(
                    document["content-digest"],
                    field=f"{field}[{index}].content-digest",
                ),
            )
        )
    return tuple(surfaces)


def _decision_context(value: JsonValue) -> LiveEligibilityContext:
    document = _object(value, context="Live Eligibility Decision.context")
    _closed(
        document,
        required=frozenset(
            {
                "purpose",
                "request-id",
                "workflow-run-id",
                "run-attempt",
                "selected-ref",
                "target",
                "repository-model-digest",
                "producer",
                "control",
                "release-policy-digest",
                "catalog-digest",
            }
        ),
        context="Live Eligibility Decision.context",
    )
    return LiveEligibilityContext(
        purpose=_nonempty_exact_string(
            document["purpose"],
            field="context.purpose",
        ),
        request_id=_nonempty_exact_string(
            document["request-id"],
            field="context.request-id",
        ),
        workflow_run_id=_decision_integer(
            document["workflow-run-id"],
            field="context.workflow-run-id",
        ),
        run_attempt=_decision_integer(
            document["run-attempt"],
            field="context.run-attempt",
        ),
        selected_ref=_nonempty_exact_string(
            document["selected-ref"],
            field="context.selected-ref",
        ),
        target=_decision_sha(
            document["target"],
            field="context.target",
        ),
        repository_model_digest=_decision_digest(
            document["repository-model-digest"],
            field="context.repository-model-digest",
        ),
        producer=_nonempty_exact_string(
            document["producer"],
            field="context.producer",
        ),
        control=_nonempty_exact_string(
            document["control"],
            field="context.control",
        ),
        release_policy_digest=_decision_digest(
            document["release-policy-digest"],
            field="context.release-policy-digest",
        ),
        catalog_digest=_decision_digest(
            document["catalog-digest"],
            field="context.catalog-digest",
        ),
    )


def _decision_consumer_policy(value: JsonValue) -> ConsumerPolicyResult:
    document = _object(
        value,
        context="Live Eligibility Decision.consumer-policy",
    )
    _closed(
        document,
        required=frozenset(
            {
                "policy-id",
                "policy-digest",
                "result-digest",
                "target",
                "scanned-surfaces",
                "admitted-exceptions",
                "consumers",
            }
        ),
        context="Live Eligibility Decision.consumer-policy",
    )
    claimed_result_digest = _decision_digest(
        document["result-digest"],
        field="consumer-policy.result-digest",
    )
    result = ConsumerPolicyResult(
        policy_id=_nonempty_exact_string(
            document["policy-id"],
            field="consumer-policy.policy-id",
        ),
        policy_digest=_decision_digest(
            document["policy-digest"],
            field="consumer-policy.policy-digest",
        ),
        target=_decision_sha(
            document["target"],
            field="consumer-policy.target",
        ),
        scanned_surfaces=_decision_surfaces(
            document["scanned-surfaces"],
            field="consumer-policy.scanned-surfaces",
        ),
        admitted_exceptions=_decision_surfaces(
            document["admitted-exceptions"],
            field="consumer-policy.admitted-exceptions",
        ),
        consumers=_decision_strings(
            document["consumers"],
            field="consumer-policy.consumers",
        ),
    )
    validate_consumer_policy_result(result)
    if claimed_result_digest != result.result_digest:
        message = (
            "Live Eligibility Decision consumer-policy result digest mismatch"
        )
        raise ValueError(message)
    return result


def _validate_governance_binding(
    binding: LiveEligibilityGovernanceBinding,
) -> None:
    if type(binding) is not LiveEligibilityGovernanceBinding:
        message = "Live Eligibility Decision Governance binding type mismatch"
        raise TypeError(message)
    _validate_source(binding.source)
    if _SHA_PATTERN.fullmatch(binding.resolved_commit) is None:
        message = "Live Eligibility Decision Governance commit is malformed"
        raise ValueError(message)
    if _OBJECT_ID_PATTERN.fullmatch(binding.blob_oid) is None:
        message = "Live Eligibility Decision Governance blob OID is malformed"
        raise ValueError(message)
    for field, value in (
        ("content-sha256", binding.content_sha256),
        ("attestation-content-digest", binding.attestation_content_digest),
    ):
        if type(value) is not str or _DIGEST_PATTERN.fullmatch(value) is None:
            message = (
                f"Live Eligibility Decision Governance {field} is malformed"
            )
            raise ValueError(message)
    if binding.content_sha256 != binding.attestation_content_digest:
        message = (
            "Live Eligibility Decision Governance attestation identity mismatch"
        )
        raise ValueError(message)
    if type(binding.live_enabled) is not bool:
        message = (
            "Live Eligibility Decision Governance live-enabled must be Boolean"
        )
        raise TypeError(message)
    _string(binding.issuer, context="governance.issuer")
    observed_at = _utc_now(binding.observed_at)
    inspected_at = _utc_now(binding.inspected_at)
    expires_at = _utc_now(binding.expires_at)
    lifetime = expires_at - inspected_at
    if lifetime <= timedelta(0) or lifetime > timedelta(
        days=binding.source.max_age_days
    ):
        message = (
            "Live Eligibility Decision Governance attestation lifetime mismatch"
        )
        raise ValueError(message)
    if (
        observed_at.microsecond
        or inspected_at.microsecond
        or expires_at.microsecond
    ):
        message = (
            "Live Eligibility Decision Governance instants must use "
            "second precision"
        )
        raise ValueError(message)


def _decision_governance(
    value: JsonValue,
) -> LiveEligibilityGovernanceBinding:
    document = _object(
        value,
        context="Live Eligibility Decision.governance",
    )
    _closed(
        document,
        required=frozenset(
            {
                "repository",
                "ref",
                "resolved-commit",
                "path",
                "blob-oid",
                "content-sha256",
                "observed-at",
                "max-age-days",
                "live-enabled",
                "issuer",
                "inspected-at",
                "expires-at",
                "attestation-content-digest",
            }
        ),
        context="Live Eligibility Decision.governance",
    )
    binding = LiveEligibilityGovernanceBinding(
        source=GovernanceSource(
            repository=_nonempty_exact_string(
                document["repository"],
                field="governance.repository",
            ),
            ref=_nonempty_exact_string(
                document["ref"],
                field="governance.ref",
            ),
            path=_nonempty_exact_string(
                document["path"],
                field="governance.path",
            ),
            max_age_days=_decision_integer(
                document["max-age-days"],
                field="governance.max-age-days",
            ),
        ),
        resolved_commit=_decision_sha(
            document["resolved-commit"],
            field="governance.resolved-commit",
        ),
        blob_oid=_nonempty_exact_string(
            document["blob-oid"],
            field="governance.blob-oid",
        ),
        content_sha256=_decision_digest(
            document["content-sha256"],
            field="governance.content-sha256",
        ),
        observed_at=_parse_instant(
            document["observed-at"],
            context="governance.observed-at",
        ),
        live_enabled=_decision_boolean(
            document["live-enabled"],
            field="governance.live-enabled",
        ),
        issuer=_string(
            document["issuer"],
            context="governance.issuer",
        ),
        inspected_at=_parse_instant(
            document["inspected-at"],
            context="governance.inspected-at",
        ),
        expires_at=_parse_instant(
            document["expires-at"],
            context="governance.expires-at",
        ),
        attestation_content_digest=_decision_digest(
            document["attestation-content-digest"],
            field="governance.attestation-content-digest",
        ),
    )
    _validate_governance_binding(binding)
    return binding


def admit_live_eligibility_decision(  # noqa: C901, PLR0912, PLR0913, PLR0915
    canonical_bytes: bytes,
    *,
    intent: ReleaseIntent,
    repository_model: AdmittedRepositoryModelSnapshot,
    policy: ReleasePolicy,
    expected_digest: str,
    admission_mode: LiveEligibilityAdmissionMode,
    now: datetime,
) -> AdmittedLiveEligibilityDecision:
    """Admit one canonical passing Decision for a caller-fixed phase."""
    if type(canonical_bytes) is not bytes:
        message = "Live Eligibility Decision transport must be exact bytes"
        raise TypeError(message)
    if type(intent) is not ReleaseIntent:
        message = (
            "Live Eligibility Decision requires an admitted Release Intent"
        )
        raise TypeError(message)
    if type(repository_model) is not AdmittedRepositoryModelSnapshot:
        message = (
            "Live Eligibility Decision requires an admitted Repository Model"
        )
        raise TypeError(message)
    if type(policy) is not ReleasePolicy:
        message = "Live Eligibility Decision requires an exact Release policy"
        raise TypeError(message)
    if type(admission_mode) is not LiveEligibilityAdmissionMode:
        message = (
            "Live Eligibility Decision admission mode must be caller-selected"
        )
        raise TypeError(message)
    admitted_at = _utc_now(now)
    normalized_expected_digest = _decision_digest(
        expected_digest,
        field="expected-digest",
    )
    document = parse_canonical_json(canonical_bytes)
    _closed(
        document,
        required=frozenset(
            {
                "schema",
                "context",
                "consumer-policy",
                "governance",
                "result",
                "diagnostics",
            }
        ),
        context="Live Eligibility Decision",
    )
    if document["schema"] != LIVE_ELIGIBILITY_DECISION_SCHEMA:
        message = "Live Eligibility Decision has the wrong schema"
        raise ValueError(message)
    actual_digest = canonical_sha256(document)
    if actual_digest != normalized_expected_digest:
        message = "Live Eligibility Decision canonical digest mismatch"
        raise ValueError(message)
    context = _decision_context(document["context"])
    consumer_policy = _decision_consumer_policy(document["consumer-policy"])
    governance = _decision_governance(document["governance"])
    result_value = _nonempty_exact_string(
        document["result"],
        field="result",
    )
    try:
        result = EligibilityResult(result_value)
    except ValueError as error:
        message = "Live Eligibility Decision result is invalid"
        raise ValueError(message) from error
    diagnostics = _decision_strings(
        document["diagnostics"],
        field="diagnostics",
    )
    if len(set(diagnostics)) != len(diagnostics):
        message = "Live Eligibility Decision diagnostics contain duplicates"
        raise ValueError(message)

    expected_intent_shape = (
        GOVERNANCE_REPOSITORY,
        BUDDY_LIVE_WORKFLOW_PATH,
        "buddy",
        "live",
        "live-release",
        FIRST_SLICE_RELEASE_UNIT,
    )
    actual_intent_shape = (
        intent.repository,
        intent.workflow_path,
        intent.channel,
        intent.mode,
        intent.purpose,
        intent.release_unit,
    )
    if actual_intent_shape != expected_intent_shape:
        message = "Live Eligibility Decision Release Intent is not exact live"
        raise ValueError(message)
    expected_context = (
        intent.purpose,
        intent.request_id,
        intent.workflow_run_id,
        intent.run_attempt,
        intent.selected_ref,
        intent.target,
        repository_model.canonical_digest,
        LIVE_ELIGIBILITY_PRODUCER,
        repository_model.snapshot.context.control,
        release_policy_digest(policy),
        catalog_digest(),
    )
    actual_context = (
        context.purpose,
        context.request_id,
        context.workflow_run_id,
        context.run_attempt,
        context.selected_ref,
        context.target,
        context.repository_model_digest,
        context.producer,
        context.control,
        context.release_policy_digest,
        context.catalog_digest,
    )
    if actual_context != expected_context:
        message = "Live Eligibility Decision current lineage mismatch"
        raise ValueError(message)
    _validate_live_context(context, repository_model.snapshot, policy)
    if repository_model.snapshot.release_policy != compile_release_policy(
        policy
    ):
        message = (
            "Live Eligibility Decision exact-target Release policy mismatch"
        )
        raise ValueError(message)
    if consumer_policy.target != context.target:
        message = "Live Eligibility Decision consumer-policy target mismatch"
        raise ValueError(message)
    if (
        result is not EligibilityResult.PASS
        or consumer_policy.consumers
        or diagnostics
    ):
        message = "Live Eligibility Decision is not a closed passing decision"
        raise ValueError(message)
    original_observation_valid = (
        governance.inspected_at
        <= governance.observed_at
        < governance.expires_at
    )
    requires_current_freshness = (
        admission_mode is LiveEligibilityAdmissionMode.CURRENT_FRESHNESS
    )
    if (
        not governance.live_enabled
        or not original_observation_valid
        or governance.observed_at > admitted_at
        or (requires_current_freshness and governance.expires_at <= admitted_at)
    ):
        message = (
            "Live Eligibility Decision Governance is not fresh and enabled"
        )
        raise ValueError(message)
    admitted = AdmittedLiveEligibilityDecision(
        context=context,
        consumer_policy=consumer_policy,
        governance=governance,
        result=result,
        diagnostics=diagnostics,
        canonical_digest=actual_digest,
        canonical_bytes=canonical_bytes,
    )
    if admitted.to_document() != document:
        message = "Live Eligibility Decision is not normalized"
        raise ValueError(message)
    return admitted


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
