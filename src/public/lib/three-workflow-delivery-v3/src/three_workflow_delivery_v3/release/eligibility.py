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
    canonicalize,
    parse_canonical_json,
)
from three_workflow_delivery_v3.catalogs import catalog_digest
from three_workflow_delivery_v3.records.release import ReleaseIntent
from three_workflow_delivery_v3.release.governance_git import (
    GovernanceGitRead,
    GovernanceGitReadError,
)
from three_workflow_delivery_v3.release.identity import (
    BUDDY_LIVE_WORKFLOW_PATH,
)
from three_workflow_delivery_v3.release.static_reference_model import (
    BoundedStaticReferenceResult,
    parse_bounded_static_reference_result,
)
from three_workflow_delivery_v3.release.static_reference_policy import (
    scan_bounded_static_references,
    validate_live_static_reference_result,
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
    from pathlib import Path

    from three_workflow_delivery_v3.repository.compiler import (
        RepositoryModelSnapshot,
    )
ATTESTATION_SCHEMA = (
    "workflow-delivery/v3/normal-live-governance-attestation-v2"
)
LIVE_ELIGIBILITY_DECISION_SCHEMA = (
    "workflow-delivery/v3/live-eligibility-decision"
)
LIVE_ELIGIBILITY_PRODUCER = "evaluate-live-eligibility"
_RELEASE_POLICY_BINDING = FIRST_SLICE_RELEASE_UNIT
_ACCESS_CATEGORIES = ("repository", "package", "manage_actions")
_ACCEPTED_OPERATOR = "hcoona"
_APPROVAL_ENVIRONMENT = "workflow-delivery-v3-buddy-approval"
_APPROVAL_JOB = "approve-publication"
_APPROVAL_SENTINEL_NAME = "WDV3_APPROVAL_ENVIRONMENT_MARKER"
_APPROVAL_SENTINEL_VALUE = "workflow-delivery-v3-buddy-approval/v1"
_ARTIFACT_RETENTION_ENDPOINT = (
    "GET /repos/hcoona/three/actions/permissions/artifact-and-log-retention"
)
_DESTINATION_PRIMITIVE_UNPROVEN = "destination-primitive-unproven"
# Target-admitted profile, suite, disposable package, API and contract.
# Governance, not target code, attests the captured successful generation.
# No production contract is admitted before its native acceptance.
_ADMITTED_DESTINATION_PRIMITIVE_IDS: frozenset[
    tuple[str, str, str, str, str]
] = frozenset()
_KNOWN_WIDER_PACKAGE_REACH = (
    "@hcoona/hexo-renderer-asciidoc",
    "disposable-smoke-packages",
)
_GIT_OBJECT_ID_LENGTHS = {"sha1": 40, "sha256": 64}
_MIN_ARTIFACT_RETENTION_DAYS = 45
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
    AUTHORIZATION_REPLAY = "authorization-replay"


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
class PackagePrincipalAttestation:
    """Repository-principal package reach accepted for this slice."""

    repository: str
    intended_coordinate: str
    known_wider_reach: tuple[str, ...]

    def to_document(self) -> dict[str, JsonValue]:
        """Return the exact package-principal statement."""
        known_wider_reach: list[JsonValue] = list(self.known_wider_reach)
        return {
            "repository": self.repository,
            "intended_coordinate": self.intended_coordinate,
            "known_wider_reach": known_wider_reach,
        }


@dataclass(frozen=True, slots=True)
class NativeEvidence:
    """One authenticated native readback identity."""

    endpoint: str
    captured_at: datetime
    response_digest: str

    def to_document(self) -> dict[str, JsonValue]:
        """Return the normalized readback identity."""
        return {
            "endpoint": self.endpoint,
            "captured_at": _format_instant(self.captured_at),
            "response_digest": self.response_digest,
        }


@dataclass(frozen=True, slots=True)
class ApprovalEnvironmentReviewer:
    """One required native Environment reviewer."""

    login: str
    reviewer_id: int

    def to_document(self) -> dict[str, JsonValue]:
        """Return the normalized reviewer identity."""
        return {"login": self.login, "id": self.reviewer_id}


@dataclass(frozen=True, slots=True)
class ApprovalEnvironmentVariable:
    """One normalized Environment-scoped variable."""

    name: str
    value: str
    scope: str

    def to_document(self) -> dict[str, JsonValue]:
        """Return the normalized variable identity."""
        return {
            "name": self.name,
            "value": self.value,
            "scope": self.scope,
        }


@dataclass(frozen=True, slots=True)
class ApprovalEnvironmentAttestation:
    """Normalized native Approval Environment facts."""

    name: str
    environment_id: int
    required_reviewers: tuple[ApprovalEnvironmentReviewer, ...]
    prevent_self_review: bool
    can_admins_bypass: bool
    wait_timer_minutes: int
    deployment_policy: str
    secret_count: int
    variables: tuple[ApprovalEnvironmentVariable, ...]
    same_name_repository_variable_absent: bool
    same_name_organization_variable: str
    evidence: tuple[NativeEvidence, ...]

    def to_document(self) -> dict[str, JsonValue]:
        """Return the normalized Approval Environment facts."""
        return {
            "name": self.name,
            "environment_id": self.environment_id,
            "required_reviewers": [
                reviewer.to_document() for reviewer in self.required_reviewers
            ],
            "prevent_self_review": self.prevent_self_review,
            "can_admins_bypass": self.can_admins_bypass,
            "wait_timer_minutes": self.wait_timer_minutes,
            "deployment_policy": self.deployment_policy,
            "secret_count": self.secret_count,
            "variables": [
                variable.to_document() for variable in self.variables
            ],
            "same_name_repository_variable_absent": (
                self.same_name_repository_variable_absent
            ),
            "same_name_organization_variable": (
                self.same_name_organization_variable
            ),
            "evidence": [item.to_document() for item in self.evidence],
        }


@dataclass(frozen=True, slots=True)
class ArtifactRetentionAttestation:
    """Authenticated repository artifact-retention readback."""

    endpoint: str
    captured_at: datetime
    days: int
    response_digest: str

    def to_document(self) -> dict[str, JsonValue]:
        """Return the normalized retention evidence."""
        return {
            "endpoint": self.endpoint,
            "captured_at": _format_instant(self.captured_at),
            "days": self.days,
            "response_digest": self.response_digest,
        }


@dataclass(frozen=True, slots=True)
class DisposablePackagePreconditions:
    """Issuer-attested preconditions for the acceptance-only package."""

    package: str
    preexisting_container: bool
    operator_controlled: bool
    production_dependency: bool

    def __post_init__(self) -> None:
        """Reject incomplete or non-passing disposable-package facts."""
        _exact_string(
            self.package, context="disposable_package_preconditions.package"
        )
        if (
            self.preexisting_container is not True
            or self.operator_controlled is not True
            or self.production_dependency is not False
        ):
            message = "disposable_package_preconditions must be passing"
            raise ValueError(message)

    def to_document(self) -> dict[str, JsonValue]:
        """Return only the closed approved preconditions."""
        return {
            "package": self.package,
            "preexisting_container": self.preexisting_container,
            "operator_controlled": self.operator_controlled,
            "production_dependency": self.production_dependency,
        }


@dataclass(frozen=True, slots=True)
class DestinationPrimitiveAttestation:
    """Identity of one successful native acceptance generation, not its data."""

    destination_operation_profile_digest: str
    native_acceptance_suite_version: str
    disposable_package_preconditions: DisposablePackagePreconditions
    github_api_version: str
    lower_layer_contract_revision: str
    captured_at: datetime
    evidence_digest: str

    def __post_init__(self) -> None:
        """Validate the closed successful-acceptance statement."""
        _sha256(
            self.destination_operation_profile_digest,
            context="destination_primitive.destination_operation_profile_digest",
        )
        _sha256(
            self.evidence_digest,
            context="destination_primitive.evidence_digest",
        )
        for field in (
            "native_acceptance_suite_version",
            "github_api_version",
            "lower_layer_contract_revision",
        ):
            _exact_string(
                getattr(self, field), context=f"destination_primitive.{field}"
            )
        if (
            type(self.disposable_package_preconditions)
            is not DisposablePackagePreconditions
        ):
            message = (
                "destination_primitive requires "
                "disposable_package_preconditions"
            )
            raise TypeError(message)
        _validate_instant(self.captured_at)

    def to_document(self) -> dict[str, JsonValue]:
        """Return the exact acceptance identity without privileged inputs."""
        return {
            "destination_operation_profile_digest": (
                self.destination_operation_profile_digest
            ),
            "native_acceptance_suite_version": (
                self.native_acceptance_suite_version
            ),
            "disposable_package_preconditions": (
                self.disposable_package_preconditions.to_document()
            ),
            "github_api_version": self.github_api_version,
            "lower_layer_contract_revision": self.lower_layer_contract_revision,
            "captured_at": _format_instant(self.captured_at),
            "evidence_digest": self.evidence_digest,
        }

    @property
    def admission_key(self) -> tuple[str, str, str, str, str]:
        """Return the exact target-code contract, excluding issuer evidence."""
        return (
            self.destination_operation_profile_digest,
            self.native_acceptance_suite_version,
            self.disposable_package_preconditions.package,
            self.github_api_version,
            self.lower_layer_contract_revision,
        )


@dataclass(frozen=True, slots=True)
class DisabledGovernanceActivation:
    """State-only blocked activation, carrying no native evidence."""

    def to_document(self) -> dict[str, JsonValue]:
        """Return the closed disabled activation state."""
        return {"state": "blocked"}


@dataclass(frozen=True, slots=True)
class EnabledGovernanceActivation:
    """Complete evidence required before normal Live may be enabled."""

    approval_environment: ApprovalEnvironmentAttestation
    artifact_retention: ArtifactRetentionAttestation
    destination_primitive: DestinationPrimitiveAttestation

    def __post_init__(self) -> None:
        """Require complete pass-only native statements, even while disabled."""
        if (
            type(self.approval_environment)
            is not ApprovalEnvironmentAttestation
            or type(self.artifact_retention) is not ArtifactRetentionAttestation
            or type(self.destination_primitive)
            is not DestinationPrimitiveAttestation
        ):
            message = "ready activation requires complete native evidence"
            raise TypeError(message)
        _approval_environment(self.approval_environment.to_document())
        _artifact_retention(self.artifact_retention.to_document())
        for evidence in self.approval_environment.evidence:
            _validate_instant(evidence.captured_at)
        _validate_instant(self.artifact_retention.captured_at)

    def to_document(self) -> dict[str, JsonValue]:
        """Return the closed activation-ready evidence."""
        return {
            "state": "ready",
            "approval_environment": self.approval_environment.to_document(),
            "artifact_retention": self.artifact_retention.to_document(),
            "destination_primitive": self.destination_primitive.to_document(),
        }


GovernanceActivation = (
    DisabledGovernanceActivation | EnabledGovernanceActivation
)


@dataclass(frozen=True, slots=True)
class GovernanceAttestation:
    """Strict non-executable protected-source human attestation."""

    release_policy: str
    package: str
    issuer: str
    inspected_at: datetime
    expires_at: datetime
    accepted_writers: tuple[WriterInventoryEntry, ...]
    accepted_publisher: str
    access_inventory: AccessInventory
    package_principal: PackagePrincipalAttestation
    limitations: tuple[str, ...]
    activation: GovernanceActivation
    live_enabled: bool

    def __post_init__(self) -> None:
        """Keep constructor and parser states equally strict."""
        if type(self.activation) not in (
            DisabledGovernanceActivation,
            EnabledGovernanceActivation,
        ):
            message = "Governance activation has the wrong runtime type"
            raise TypeError(message)
        _boolean(
            self.live_enabled, context="Governance attestation live_enabled"
        )
        if self.live_enabled and isinstance(
            self.activation, DisabledGovernanceActivation
        ):
            message = "Governance activation state and live_enabled disagree"
            raise ValueError(message)
        _validate_instant(self.inspected_at)
        _validate_instant(self.expires_at)
        lifetime = self.expires_at - self.inspected_at
        if (
            not timedelta(0)
            < lifetime
            <= timedelta(days=GOVERNANCE_MAX_AGE_DAYS)
        ):
            message = "Governance attestation expiry must be within 90 days"
            raise ValueError(message)
        if isinstance(self.activation, EnabledGovernanceActivation):
            evidence_times = (
                *(
                    item.captured_at
                    for item in self.activation.approval_environment.evidence
                ),
                self.activation.artifact_retention.captured_at,
                self.activation.destination_primitive.captured_at,
            )
            if any(
                captured_at > self.inspected_at
                for captured_at in evidence_times
            ):
                message = "Governance evidence was captured after inspection"
                raise ValueError(message)
        document = self.to_document()
        if (
            self.release_policy != _RELEASE_POLICY_BINDING
            or self.package != FIRST_SLICE_PACKAGE
        ):
            message = "Governance attestation policy/package binding mismatch"
            raise ValueError(message)
        if (
            self.issuer != _ACCEPTED_OPERATOR
            or self.accepted_publisher != _ACCEPTED_OPERATOR
        ):
            message = "Governance attestation issuer/publisher is not hcoona"
            raise ValueError(message)
        _writer_inventory(document["accepted_writers"])
        _access_inventory(document["access_inventory"])
        _package_principal(document["package_principal"])
        _strings(document["limitations"], context="limitations")

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
            "accepted_publisher": self.accepted_publisher,
            "access_inventory": self.access_inventory.to_document(),
            "package_principal": self.package_principal.to_document(),
            "limitations": limitations,
            "activation": self.activation.to_document(),
            "live_enabled": self.live_enabled,
        }
        return document

    @property
    def content_digest(self) -> str:
        """Return the canonical attestation content digest."""
        return canonical_sha256(self.to_document())


def _destination_primitive_is_admitted(
    attestation: GovernanceAttestation,
) -> bool:
    activation = attestation.activation
    if not isinstance(activation, EnabledGovernanceActivation):
        return False
    primitive = activation.destination_primitive
    if primitive.admission_key not in _ADMITTED_DESTINATION_PRIMITIVE_IDS:
        return False
    from three_workflow_delivery_v3.adapters.github_packages import (  # noqa: PLC0415
        github_packages_destination_operation_profile,
    )

    return primitive.destination_operation_profile_digest == (
        github_packages_destination_operation_profile().profile_digest
    )


def require_action_governance(
    attestation: GovernanceAttestation,
    *,
    now: datetime,
    destination_operation_profile_digest: str,
) -> None:
    """Admit a fresh action, never derive new authority from replay."""
    observed_at = _utc_now(now)
    if (
        not attestation.live_enabled
        or not attestation.inspected_at <= observed_at < attestation.expires_at
    ):
        message = "Action requires fresh enabled Governance"
        raise GovernanceFreshnessRejectionError(message)
    activation = attestation.activation
    if not isinstance(
        activation, EnabledGovernanceActivation
    ) or not _destination_primitive_is_admitted(attestation):
        message = "Action destination primitive is not implemented"
        raise GovernanceRejectionError(message)
    primitive = activation.destination_primitive
    if (
        primitive.destination_operation_profile_digest
        != destination_operation_profile_digest
    ):
        message = "Action profile differs from current Governance"
        raise GovernanceRejectionError(message)
    if observed_at > primitive.captured_at + timedelta(
        days=GOVERNANCE_MAX_AGE_DAYS
    ):
        message = "Action requires unexpired native acceptance"
        raise GovernanceFreshnessRejectionError(message)


class GovernanceSourceClient(Protocol):
    """Read-only protocol required for a fresh protected-source observation."""

    def is_ref_protected(self, repository: str, ref: str) -> bool:
        """Return whether the exact fully qualified ref is protected."""
        ...

    def read_source(
        self,
        repository: str,
        ref: str,
        path: str,
        *,
        eligibility_main_sha: str | None = None,
    ) -> GovernanceGitRead:
        """Read the exact path and optionally prove eligibility continuity."""
        ...


class GovernanceRejectionError(ValueError):
    """Authoritatively observed Governance state definitively rejected."""


class GovernanceFreshnessRejectionError(GovernanceRejectionError):
    """Exact fresh Governance identity was validly read but rejected."""


@dataclass(frozen=True, slots=True)
class GovernanceObservation:
    """Fresh source provenance and validated canonical attestation."""

    source: GovernanceSource
    eligibility_main_sha: str
    current_main_sha: str
    object_format: str
    blob_oid: str
    canonical_content_digest: str
    observed_at: datetime
    attestation: GovernanceAttestation


@dataclass(frozen=True, slots=True)
class LiveEligibilityContext:
    """Current pre-Attempt authority for one live eligibility evaluation."""

    purpose: str
    request_id: str
    workflow_run_id: int
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
    eligibility_main_sha: str
    object_format: str
    blob_oid: str
    canonical_content_digest: str
    observed_at: datetime
    attestation: GovernanceAttestation

    @classmethod
    def from_observation(
        cls,
        observation: GovernanceObservation,
    ) -> LiveEligibilityGovernanceBinding:
        """Project the exact evaluator observation into transport fields."""
        return cls(
            source=observation.source,
            eligibility_main_sha=observation.eligibility_main_sha,
            object_format=observation.object_format,
            blob_oid=observation.blob_oid,
            canonical_content_digest=(observation.canonical_content_digest),
            observed_at=observation.observed_at,
            attestation=observation.attestation,
        )

    def to_document(self) -> dict[str, JsonValue]:
        """Return the closed Governance Decision binding."""
        return {
            "repository": self.source.repository,
            "ref": self.source.ref,
            "eligibility-main-sha": self.eligibility_main_sha,
            "path": self.source.path,
            "git-object-format": self.object_format,
            "blob-oid": self.blob_oid,
            "canonical-content-digest": self.canonical_content_digest,
            "observed-at": _format_instant(self.observed_at),
            "max-age-days": self.source.max_age_days,
            "admitted-attestation": self.attestation.to_document(),
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
                    ("eligibility-main-sha", self.eligibility_main_sha),
                    ("git-object-format", self.object_format),
                    ("blob-oid", self.blob_oid),
                    (
                        "canonical-content-digest",
                        self.canonical_content_digest,
                    ),
                )
            )
        )


@dataclass(frozen=True, slots=True)
class LiveEligibilityDecision:
    """Immutable exact-target pre-Attempt live eligibility Decision."""

    context: LiveEligibilityContext
    static_reference: BoundedStaticReferenceResult
    governance: GovernanceObservation
    result: EligibilityResult
    diagnostics: tuple[str, ...]

    def to_document(self) -> dict[str, JsonValue]:
        """Return the complete canonical Decision payload."""
        return _live_eligibility_document(
            context=self.context,
            static_reference=self.static_reference,
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
    static_reference: BoundedStaticReferenceResult
    governance: LiveEligibilityGovernanceBinding
    result: EligibilityResult
    diagnostics: tuple[str, ...]
    canonical_digest: str
    canonical_bytes: bytes

    def __post_init__(self) -> None:
        """Reject an internally inconsistent admitted transport wrapper."""
        if (
            type(self.context) is not LiveEligibilityContext
            or type(self.static_reference) is not BoundedStaticReferenceResult
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
            static_reference=self.static_reference,
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


def _validate_instant(value: datetime) -> None:
    if (
        not isinstance(value, datetime)
        or value.tzinfo is None
        or value.utcoffset() != timedelta(0)
        or value.microsecond
    ):
        message = "Governance evidence must use UTC second-precision instants"
        raise ValueError(message)


def _live_eligibility_document(
    *,
    context: LiveEligibilityContext,
    static_reference: BoundedStaticReferenceResult,
    governance: LiveEligibilityGovernanceBinding,
    result: EligibilityResult,
    diagnostics: tuple[str, ...],
) -> dict[str, JsonValue]:
    context_document: dict[str, JsonValue] = {
        "purpose": context.purpose,
        "request-id": context.request_id,
        "workflow-run-id": context.workflow_run_id,
        "selected-ref": context.selected_ref,
        "target": context.target,
        "repository-model-digest": context.repository_model_digest,
        "producer": context.producer,
        "control": context.control,
        "release-policy-digest": context.release_policy_digest,
        "catalog-digest": context.catalog_digest,
    }
    diagnostic_documents: list[JsonValue] = list(diagnostics)
    return {
        "schema": LIVE_ELIGIBILITY_DECISION_SCHEMA,
        "context": context_document,
        "static-reference": static_reference.to_document(),
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
        writers.append(writer)
    result = tuple(writers)
    if result != (WriterInventoryEntry(_ACCEPTED_OPERATOR, "Admin"),):
        message = "accepted_writers must contain only hcoona as Admin"
        raise ValueError(message)
    return result


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
    inventory = AccessInventory(
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
    expected = AccessInventory(
        repository=(AccessGrant(subject=_ACCEPTED_OPERATOR, access="admin"),),
        package=(AccessGrant(subject=_ACCEPTED_OPERATOR, access="write"),),
        manage_actions=(
            AccessGrant(subject=_ACCEPTED_OPERATOR, access="allowed"),
        ),
    )
    if inventory != expected:
        message = "Governance access inventory is not the exact accepted set"
        raise ValueError(message)
    return inventory


def _exact_string(value: JsonValue, *, context: str) -> str:
    text = _string(value, context=context)
    if text != text.strip():
        message = f"{context} must be an exact string"
        raise ValueError(message)
    return text


def _boolean(value: JsonValue, *, context: str) -> bool:
    if type(value) is not bool:
        message = f"{context} must be Boolean"
        raise TypeError(message)
    return value


def _nonnegative_integer(value: JsonValue, *, context: str) -> int:
    if type(value) is not int or value < 0:
        message = f"{context} must be a nonnegative integer"
        raise TypeError(message)
    return value


def _positive_integer(value: JsonValue, *, context: str) -> int:
    result = _nonnegative_integer(value, context=context)
    if result == 0:
        message = f"{context} must be a positive integer"
        raise ValueError(message)
    return result


def _sha256(value: JsonValue, *, context: str) -> str:
    digest = _exact_string(value, context=context)
    if _DIGEST_PATTERN.fullmatch(digest) is None:
        message = f"{context} must be a prefixed SHA-256"
        raise ValueError(message)
    return digest


def _strings(
    value: JsonValue,
    *,
    context: str,
    nonempty: bool = True,
) -> tuple[str, ...]:
    result = tuple(
        _exact_string(item, context=f"{context}[{index}]")
        for index, item in enumerate(_array(value, context=context))
    )
    if nonempty and not result:
        message = f"{context} must be nonempty"
        raise ValueError(message)
    if len(set(result)) != len(result):
        message = f"{context} contains duplicates"
        raise ValueError(message)
    if result != tuple(sorted(result)):
        message = f"{context} must be sorted"
        raise ValueError(message)
    return result


def _package_principal(value: JsonValue) -> PackagePrincipalAttestation:
    document = _object(value, context="package_principal")
    _closed(
        document,
        required=frozenset(
            {"repository", "intended_coordinate", "known_wider_reach"}
        ),
        context="package_principal",
    )
    result = PackagePrincipalAttestation(
        repository=_exact_string(
            document["repository"],
            context="package_principal.repository",
        ),
        intended_coordinate=_exact_string(
            document["intended_coordinate"],
            context="package_principal.intended_coordinate",
        ),
        known_wider_reach=_strings(
            document["known_wider_reach"],
            context="package_principal.known_wider_reach",
        ),
    )
    if (
        result.repository != GOVERNANCE_REPOSITORY
        or result.intended_coordinate != FIRST_SLICE_PACKAGE
        or result.known_wider_reach != _KNOWN_WIDER_PACKAGE_REACH
    ):
        message = "package_principal is not the exact accepted blast radius"
        raise ValueError(message)
    return result


def _native_evidence(value: JsonValue) -> tuple[NativeEvidence, ...]:
    evidence: list[NativeEvidence] = []
    for index, item in enumerate(
        _array(value, context="approval_environment.evidence")
    ):
        document = _object(
            item,
            context=f"approval_environment.evidence[{index}]",
        )
        _closed(
            document,
            required=frozenset({"endpoint", "captured_at", "response_digest"}),
            context=f"approval_environment.evidence[{index}]",
        )
        evidence.append(
            NativeEvidence(
                endpoint=_exact_string(
                    document["endpoint"],
                    context=f"approval_environment.evidence[{index}].endpoint",
                ),
                captured_at=_parse_instant(
                    document["captured_at"],
                    context=(
                        f"approval_environment.evidence[{index}].captured_at"
                    ),
                ),
                response_digest=_sha256(
                    document["response_digest"],
                    context=(
                        "approval_environment."
                        f"evidence[{index}].response_digest"
                    ),
                ),
            )
        )
    result = tuple(evidence)
    if not result:
        message = "approval_environment.evidence must be nonempty"
        raise ValueError(message)
    endpoints = tuple(item.endpoint for item in result)
    if len(set(endpoints)) != len(endpoints):
        message = "approval_environment.evidence repeats an endpoint"
        raise ValueError(message)
    if endpoints != tuple(sorted(endpoints)):
        message = "approval_environment.evidence must be sorted by endpoint"
        raise ValueError(message)
    return result


def _approval_environment(value: JsonValue) -> ApprovalEnvironmentAttestation:
    document = _object(value, context="activation.approval_environment")
    _closed(
        document,
        required=frozenset(
            {
                "name",
                "environment_id",
                "required_reviewers",
                "prevent_self_review",
                "can_admins_bypass",
                "wait_timer_minutes",
                "deployment_policy",
                "secret_count",
                "variables",
                "same_name_repository_variable_absent",
                "same_name_organization_variable",
                "evidence",
            }
        ),
        context="activation.approval_environment",
    )
    reviewer_values = _array(
        document["required_reviewers"],
        context="approval_environment.required_reviewers",
    )
    reviewers: list[ApprovalEnvironmentReviewer] = []
    for index, item in enumerate(reviewer_values):
        reviewer = _object(
            item,
            context=f"approval_environment.required_reviewers[{index}]",
        )
        _closed(
            reviewer,
            required=frozenset({"login", "id"}),
            context=f"approval_environment.required_reviewers[{index}]",
        )
        reviewers.append(
            ApprovalEnvironmentReviewer(
                login=_exact_string(
                    reviewer["login"],
                    context=(
                        "approval_environment."
                        f"required_reviewers[{index}].login"
                    ),
                ),
                reviewer_id=_positive_integer(
                    reviewer["id"],
                    context=(
                        f"approval_environment.required_reviewers[{index}].id"
                    ),
                ),
            )
        )
    variable_values = _array(
        document["variables"],
        context="approval_environment.variables",
    )
    variables: list[ApprovalEnvironmentVariable] = []
    for index, item in enumerate(variable_values):
        variable = _object(
            item,
            context=f"approval_environment.variables[{index}]",
        )
        _closed(
            variable,
            required=frozenset({"name", "value", "scope"}),
            context=f"approval_environment.variables[{index}]",
        )
        variables.append(
            ApprovalEnvironmentVariable(
                name=_exact_string(
                    variable["name"],
                    context=f"approval_environment.variables[{index}].name",
                ),
                value=_exact_string(
                    variable["value"],
                    context=f"approval_environment.variables[{index}].value",
                ),
                scope=_exact_string(
                    variable["scope"],
                    context=f"approval_environment.variables[{index}].scope",
                ),
            )
        )
    result = ApprovalEnvironmentAttestation(
        name=_exact_string(
            document["name"],
            context="approval_environment.name",
        ),
        environment_id=_positive_integer(
            document["environment_id"],
            context="approval_environment.environment_id",
        ),
        required_reviewers=tuple(reviewers),
        prevent_self_review=_boolean(
            document["prevent_self_review"],
            context="approval_environment.prevent_self_review",
        ),
        can_admins_bypass=_boolean(
            document["can_admins_bypass"],
            context="approval_environment.can_admins_bypass",
        ),
        wait_timer_minutes=_nonnegative_integer(
            document["wait_timer_minutes"],
            context="approval_environment.wait_timer_minutes",
        ),
        deployment_policy=_exact_string(
            document["deployment_policy"],
            context="approval_environment.deployment_policy",
        ),
        secret_count=_nonnegative_integer(
            document["secret_count"],
            context="approval_environment.secret_count",
        ),
        variables=tuple(variables),
        same_name_repository_variable_absent=_boolean(
            document["same_name_repository_variable_absent"],
            context=(
                "approval_environment.same_name_repository_variable_absent"
            ),
        ),
        same_name_organization_variable=_exact_string(
            document["same_name_organization_variable"],
            context="approval_environment.same_name_organization_variable",
        ),
        evidence=_native_evidence(document["evidence"]),
    )
    if (
        result.name != _APPROVAL_ENVIRONMENT
        or result.required_reviewers
        != (ApprovalEnvironmentReviewer(_ACCEPTED_OPERATOR, 712433),)
        or result.prevent_self_review
        or result.can_admins_bypass
        or result.wait_timer_minutes != 0
        or result.deployment_policy != "all"
        or result.secret_count != 0
        or result.variables
        != (
            ApprovalEnvironmentVariable(
                name=_APPROVAL_SENTINEL_NAME,
                value=_APPROVAL_SENTINEL_VALUE,
                scope="environment",
            ),
        )
        or not result.same_name_repository_variable_absent
        or result.same_name_organization_variable != "not-applicable-user-owner"
    ):
        message = "Approval Environment attestation is not the exact contract"
        raise ValueError(message)
    return result


def _artifact_retention(value: JsonValue) -> ArtifactRetentionAttestation:
    document = _object(value, context="activation.artifact_retention")
    _closed(
        document,
        required=frozenset(
            {"endpoint", "captured_at", "days", "response_digest"}
        ),
        context="activation.artifact_retention",
    )
    result = ArtifactRetentionAttestation(
        endpoint=_exact_string(
            document["endpoint"],
            context="artifact_retention.endpoint",
        ),
        captured_at=_parse_instant(
            document["captured_at"],
            context="artifact_retention.captured_at",
        ),
        days=_positive_integer(
            document["days"],
            context="artifact_retention.days",
        ),
        response_digest=_sha256(
            document["response_digest"],
            context="artifact_retention.response_digest",
        ),
    )
    if (
        result.endpoint != _ARTIFACT_RETENTION_ENDPOINT
        or result.days < _MIN_ARTIFACT_RETENTION_DAYS
    ):
        message = "artifact_retention does not prove at least 45 days"
        raise ValueError(message)
    return result


def _destination_primitive(
    value: JsonValue,
) -> DestinationPrimitiveAttestation:
    document = _object(value, context="activation.destination_primitive")
    _closed(
        document,
        required=frozenset(
            {
                "destination_operation_profile_digest",
                "native_acceptance_suite_version",
                "disposable_package_preconditions",
                "github_api_version",
                "lower_layer_contract_revision",
                "captured_at",
                "evidence_digest",
            }
        ),
        context="activation.destination_primitive",
    )
    preconditions = _object(
        document["disposable_package_preconditions"],
        context="disposable_package_preconditions",
    )
    _closed(
        preconditions,
        required=frozenset(
            {
                "package",
                "preexisting_container",
                "operator_controlled",
                "production_dependency",
            }
        ),
        context="disposable_package_preconditions",
    )
    return DestinationPrimitiveAttestation(
        destination_operation_profile_digest=_sha256(
            document["destination_operation_profile_digest"],
            context="destination_primitive.destination_operation_profile_digest",
        ),
        native_acceptance_suite_version=_exact_string(
            document["native_acceptance_suite_version"],
            context="destination_primitive.native_acceptance_suite_version",
        ),
        disposable_package_preconditions=DisposablePackagePreconditions(
            package=_exact_string(
                preconditions["package"],
                context="disposable_package_preconditions.package",
            ),
            preexisting_container=_boolean(
                preconditions["preexisting_container"],
                context="disposable_package_preconditions.preexisting_container",
            ),
            operator_controlled=_boolean(
                preconditions["operator_controlled"],
                context="disposable_package_preconditions.operator_controlled",
            ),
            production_dependency=_boolean(
                preconditions["production_dependency"],
                context="disposable_package_preconditions.production_dependency",
            ),
        ),
        github_api_version=_exact_string(
            document["github_api_version"],
            context="destination_primitive.github_api_version",
        ),
        lower_layer_contract_revision=_exact_string(
            document["lower_layer_contract_revision"],
            context="destination_primitive.lower_layer_contract_revision",
        ),
        captured_at=_parse_instant(
            document["captured_at"],
            context="destination_primitive.captured_at",
        ),
        evidence_digest=_sha256(
            document["evidence_digest"],
            context="destination_primitive.evidence_digest",
        ),
    )


def _activation(value: JsonValue) -> GovernanceActivation:
    document = _object(value, context="activation")
    state = _exact_string(document.get("state"), context="activation.state")
    if state == "blocked":
        _closed(
            document,
            required=frozenset({"state"}),
            context="activation",
        )
        return DisabledGovernanceActivation()
    if state == "ready":
        _closed(
            document,
            required=frozenset(
                {
                    "state",
                    "approval_environment",
                    "artifact_retention",
                    "destination_primitive",
                }
            ),
            context="activation",
        )
        return EnabledGovernanceActivation(
            approval_environment=_approval_environment(
                document["approval_environment"]
            ),
            artifact_retention=_artifact_retention(
                document["artifact_retention"]
            ),
            destination_primitive=_destination_primitive(
                document["destination_primitive"]
            ),
        )
    message = "activation.state is invalid"
    raise ValueError(message)


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
            "accepted_publisher",
            "access_inventory",
            "package_principal",
            "limitations",
            "activation",
            "live_enabled",
        }
    )
    _closed(
        document,
        required=required,
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
    issuer = _exact_string(document["issuer"], context="issuer")
    if issuer != _ACCEPTED_OPERATOR:
        message = "Governance attestation issuer is not hcoona"
        raise ValueError(message)
    accepted_publisher = _exact_string(
        document["accepted_publisher"],
        context="accepted_publisher",
    )
    if accepted_publisher != _ACCEPTED_OPERATOR:
        message = "Governance attestation publisher is not hcoona"
        raise ValueError(message)
    live_enabled = _boolean(
        document["live_enabled"],
        context="Governance attestation live_enabled",
    )
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
    activation = _activation(document["activation"])
    limitations = _strings(
        document["limitations"],
        context="limitations",
    )
    attestation = GovernanceAttestation(
        release_policy=release_policy,
        package=package,
        issuer=issuer,
        inspected_at=inspected_at,
        expires_at=expires_at,
        accepted_writers=_writer_inventory(document["accepted_writers"]),
        accepted_publisher=accepted_publisher,
        access_inventory=_access_inventory(document["access_inventory"]),
        package_principal=_package_principal(document["package_principal"]),
        limitations=limitations,
        activation=activation,
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


def _git_object_id(
    value: str,
    *,
    object_format: str,
    context: str,
) -> str:
    expected_length = _GIT_OBJECT_ID_LENGTHS.get(object_format)
    if (
        expected_length is None
        or type(value) is not str
        or len(value) != expected_length
        or any(character not in "0123456789abcdef" for character in value)
    ):
        message = f"{context} is malformed"
        raise ValueError(message)
    return value


def _read_governance_source(
    source: GovernanceSource,
    client: GovernanceSourceClient,
    *,
    now: datetime,
    eligibility_main_sha: str | None,
) -> GovernanceObservation:
    _validate_source(source)
    observed_at = _utc_now(now)
    protected = client.is_ref_protected(source.repository, source.ref)
    if type(protected) is not bool:
        message = "Governance ref protection response is malformed"
        raise ValueError(message)
    if not protected:
        message = "Governance ref is not protected"
        raise GovernanceRejectionError(message)
    try:
        read = client.read_source(
            source.repository,
            source.ref,
            source.path,
            eligibility_main_sha=eligibility_main_sha,
        )
    except GovernanceGitReadError as error:
        raise GovernanceRejectionError(str(error)) from error
    if type(read) is not GovernanceGitRead:
        message = "Governance Git read response is malformed"
        raise TypeError(message)
    main_sha = _git_object_id(
        read.main_sha,
        object_format=read.object_format,
        context="Governance main SHA",
    )
    blob_oid = _git_object_id(
        read.blob_oid,
        object_format=read.object_format,
        context="Governance blob OID",
    )
    lineage_sha = eligibility_main_sha or main_sha
    _git_object_id(
        lineage_sha,
        object_format=read.object_format,
        context="Governance eligibility main SHA",
    )
    try:
        attestation = parse_governance_attestation(read.content)
    except (TypeError, ValueError, UnicodeError) as error:
        raise GovernanceRejectionError(str(error)) from error
    content_digest = f"sha256:{hashlib.sha256(read.content).hexdigest()}"
    if content_digest != attestation.content_digest:
        message = "Governance canonical content digest mismatch"
        raise GovernanceRejectionError(message)
    return GovernanceObservation(
        source=source,
        eligibility_main_sha=lineage_sha,
        current_main_sha=main_sha,
        object_format=read.object_format,
        blob_oid=blob_oid,
        canonical_content_digest=content_digest,
        observed_at=observed_at,
        attestation=attestation,
    )


def observe_governance_source(
    source: GovernanceSource,
    client: GovernanceSourceClient,
    *,
    now: datetime,
) -> GovernanceObservation:
    """Freshly resolve and read the exact protected Governance source."""
    return _read_governance_source(
        source,
        client,
        now=now,
        eligibility_main_sha=None,
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
                (
                    "eligibility-main-sha",
                    observation.eligibility_main_sha,
                ),
                ("git-object-format", observation.object_format),
                ("blob-oid", observation.blob_oid),
                (
                    "canonical-content-digest",
                    observation.canonical_content_digest,
                ),
            )
        )
    )


def _provenance_value(
    provenance: tuple[tuple[str, str], ...],
    *,
    field: str,
) -> str:
    if (
        type(provenance) is not tuple
        or provenance != tuple(sorted(provenance))
        or len(dict(provenance)) != len(provenance)
    ):
        message = "Governance provenance is malformed"
        raise ValueError(message)
    values = dict(provenance)
    value = values.get(field)
    if type(value) is not str or not value:
        message = f"Governance provenance is missing {field}"
        raise ValueError(message)
    return value


def require_fresh_governance_identity(  # noqa: PLR0913
    source: GovernanceSource,
    client: GovernanceSourceClient,
    *,
    now: datetime,
    expected_provenance: tuple[tuple[str, str], ...],
    expected_canonical_content_digest: str,
    expected_expires_at: str,
    expected_live_enabled: bool,
) -> GovernanceObservation:
    """Require current fixed-source Governance identity and validity."""
    eligibility_main_sha = _provenance_value(
        expected_provenance,
        field="eligibility-main-sha",
    )
    observation = _read_governance_source(
        source,
        client,
        now=now,
        eligibility_main_sha=eligibility_main_sha,
    )
    provenance = governance_observation_provenance(observation)
    if (
        provenance != expected_provenance
        or observation.canonical_content_digest
        != expected_canonical_content_digest
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
    if type(context.workflow_run_id) is not int or context.workflow_run_id <= 0:
        message = "live eligibility workflow_run_id must be a positive integer"
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
        context.target,
        context.control,
        context.catalog_digest,
    )
    actual_snapshot = (
        snapshot.context.request_id,
        snapshot.context.purpose,
        snapshot.context.workflow_run_id,
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


def _decision_context(value: JsonValue) -> LiveEligibilityContext:
    document = _object(value, context="Live Eligibility Decision.context")
    _closed(
        document,
        required=frozenset(
            {
                "purpose",
                "request-id",
                "workflow-run-id",
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


def _decision_static_reference(
    value: JsonValue,
) -> BoundedStaticReferenceResult:
    document = _object(
        value,
        context="Live Eligibility Decision.static-reference",
    )
    result = parse_bounded_static_reference_result(canonicalize(document))
    validate_live_static_reference_result(result)
    return result


def _validate_governance_binding(
    binding: LiveEligibilityGovernanceBinding,
) -> None:
    if type(binding) is not LiveEligibilityGovernanceBinding:
        message = "Live Eligibility Decision Governance binding type mismatch"
        raise TypeError(message)
    _validate_source(binding.source)
    _git_object_id(
        binding.eligibility_main_sha,
        object_format=binding.object_format,
        context="Live Eligibility Decision Governance eligibility main SHA",
    )
    _git_object_id(
        binding.blob_oid,
        object_format=binding.object_format,
        context="Live Eligibility Decision Governance blob OID",
    )
    if (
        type(binding.canonical_content_digest) is not str
        or _DIGEST_PATTERN.fullmatch(binding.canonical_content_digest) is None
    ):
        message = (
            "Live Eligibility Decision Governance canonical content digest "
            "is malformed"
        )
        raise ValueError(message)
    if type(binding.attestation) is not GovernanceAttestation:
        message = (
            "Live Eligibility Decision Governance attestation type mismatch"
        )
        raise TypeError(message)
    if binding.canonical_content_digest != binding.attestation.content_digest:
        message = (
            "Live Eligibility Decision Governance attestation identity mismatch"
        )
        raise ValueError(message)
    observed_at = _utc_now(binding.observed_at)
    inspected_at = _utc_now(binding.attestation.inspected_at)
    expires_at = _utc_now(binding.attestation.expires_at)
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
                "eligibility-main-sha",
                "path",
                "git-object-format",
                "blob-oid",
                "canonical-content-digest",
                "observed-at",
                "max-age-days",
                "admitted-attestation",
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
        eligibility_main_sha=_nonempty_exact_string(
            document["eligibility-main-sha"],
            field="governance.eligibility-main-sha",
        ),
        object_format=_nonempty_exact_string(
            document["git-object-format"],
            field="governance.git-object-format",
        ),
        blob_oid=_nonempty_exact_string(
            document["blob-oid"],
            field="governance.blob-oid",
        ),
        canonical_content_digest=_decision_digest(
            document["canonical-content-digest"],
            field="governance.canonical-content-digest",
        ),
        observed_at=_parse_instant(
            document["observed-at"],
            context="governance.observed-at",
        ),
        attestation=parse_governance_attestation(
            canonicalize(
                _object(
                    document["admitted-attestation"],
                    context=(
                        "Live Eligibility Decision.governance."
                        "admitted-attestation"
                    ),
                )
            )
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
                "static-reference",
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
    static_reference = _decision_static_reference(document["static-reference"])
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
    if (
        static_reference.source_kind != "git-target"
        or static_reference.target != context.target
    ):
        message = "Live Eligibility Decision static-reference target mismatch"
        raise ValueError(message)
    if (
        result is not EligibilityResult.PASS
        or static_reference.result != "clean"
        or diagnostics
    ):
        message = "Live Eligibility Decision is not a closed passing decision"
        raise ValueError(message)
    original_observation_valid = (
        governance.attestation.inspected_at
        <= governance.observed_at
        < governance.attestation.expires_at
    )
    requires_current_freshness = (
        admission_mode is LiveEligibilityAdmissionMode.CURRENT_FRESHNESS
    )
    if (
        not governance.attestation.live_enabled
        or not original_observation_valid
        or governance.observed_at > admitted_at
        or (
            requires_current_freshness
            and governance.attestation.expires_at <= admitted_at
        )
    ):
        message = (
            "Live Eligibility Decision Governance is not fresh and enabled"
        )
        raise ValueError(message)
    if not _destination_primitive_is_admitted(governance.attestation):
        message = (
            "Live Eligibility Decision destination primitive is not implemented"
        )
        raise ValueError(message)
    admitted = AdmittedLiveEligibilityDecision(
        context=context,
        static_reference=static_reference,
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
    policy: ReleasePolicy,
    client: GovernanceSourceClient,
    *,
    repository_root: Path,
    now: datetime,
) -> LiveEligibilityDecision:
    """Evaluate current exact-target eligibility before Attempt creation."""
    _validate_source(policy.governance)
    _validate_live_context(context, snapshot, policy)
    static_reference = scan_bounded_static_references(
        repository_root,
        source_kind="git-target",
        target=context.target,
    )
    validate_live_static_reference_result(static_reference)
    governance = observe_governance_source(
        policy.governance,
        client,
        now=now,
    )
    diagnostics: list[str] = []
    if static_reference.result == "findings":
        diagnostics.append("static-reference-findings")
    elif static_reference.result == "error":
        diagnostics.append(f"static-reference-{static_reference.error_kind}")
    if not governance.attestation.live_enabled:
        diagnostics.append("governance-live-disabled")
    elif not _destination_primitive_is_admitted(governance.attestation):
        diagnostics.append(_DESTINATION_PRIMITIVE_UNPROVEN)
    if now < governance.attestation.inspected_at:
        diagnostics.append("governance-attestation-not-yet-valid")
    if now >= governance.attestation.expires_at:
        diagnostics.append("governance-attestation-expired")
    result = (
        EligibilityResult.BLOCKED if diagnostics else EligibilityResult.PASS
    )
    return LiveEligibilityDecision(
        context=context,
        static_reference=static_reference,
        governance=governance,
        result=result,
        diagnostics=tuple(diagnostics),
    )
