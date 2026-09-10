"""Protected-main NuGet eligibility without first-slice npm scan evidence."""

from __future__ import annotations

# Shared private parsers retain the same source and authority contracts.
# ruff: noqa: SLF001
from dataclasses import dataclass
from datetime import datetime, timedelta
from typing import TYPE_CHECKING, cast

from three_workflow_delivery_v3.canonical import (
    canonical_sha256,
    canonicalize,
    parse_canonical_json,
)
from three_workflow_delivery_v3.catalogs import catalog_digest
from three_workflow_delivery_v3.records.release import (
    NugetDestinationOperationProfile,
    ReleaseIntent,
)
from three_workflow_delivery_v3.release import eligibility as shared
from three_workflow_delivery_v3.release.identity import BUDDY_LIVE_WORKFLOW_PATH
from three_workflow_delivery_v3.release.nuget_governance import (
    NuGetPlatformFacts,
    nuget_destination_primitive_is_admitted,
    require_nuget_live_platform,
)
from three_workflow_delivery_v3.repository.compiler import (
    AdmittedRepositoryModelSnapshot,
    compile_release_policy,
    validate_nuget_repository_model_snapshot,
)
from three_workflow_delivery_v3.repository.descriptors import (
    NUGET_GOVERNANCE_PATH,
    NUGET_RELEASE_UNIT,
    ReleasePolicy,
)

if TYPE_CHECKING:
    from three_workflow_delivery_v3.canonical import JsonValue

NUGET_ELIGIBILITY_SCHEMA = (
    "workflow-delivery/v3/nuget-live-eligibility-decision-v1"
)


def _context_document(
    context: shared.LiveEligibilityContext,
) -> dict[str, JsonValue]:
    return {
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


def _validate_context(
    context: shared.LiveEligibilityContext,
    intent: ReleaseIntent,
    model: AdmittedRepositoryModelSnapshot,
    policy: ReleasePolicy,
) -> None:
    if (
        type(intent) is not ReleaseIntent
        or type(model) is not AdmittedRepositoryModelSnapshot
        or type(policy) is not ReleasePolicy
        or type(context) is not shared.LiveEligibilityContext
    ):
        message = "NuGet eligibility requires exact admitted inputs"
        raise TypeError(message)
    snapshot = model.snapshot
    validate_nuget_repository_model_snapshot(snapshot)
    if (
        model.canonical_bytes != canonicalize(snapshot.to_document())
        or model.canonical_digest != snapshot.snapshot_digest
    ):
        message = "NuGet eligibility Model integrity mismatch"
        raise ValueError(message)
    expected = shared.LiveEligibilityContext(
        "live-release",
        intent.request_id,
        intent.workflow_run_id,
        "refs/heads/main",
        intent.target,
        model.canonical_digest,
        shared.LIVE_ELIGIBILITY_PRODUCER,
        f"workflow-delivery-v3:{intent.target}",
        shared.release_policy_digest(policy),
        catalog_digest(),
    )
    if (
        context != expected
        or intent.repository != "hcoona/three"
        or intent.actor != "hcoona"
        or intent.selected_ref != context.selected_ref
        or intent.workflow_path != BUDDY_LIVE_WORKFLOW_PATH
        or intent.workflow_sha != intent.target
        or intent.release_unit != NUGET_RELEASE_UNIT
        or intent.channel != "buddy"
        or intent.mode != "live"
        or intent.purpose != context.purpose
        or policy.release_unit != NUGET_RELEASE_UNIT
        or policy.governance.path != NUGET_GOVERNANCE_PATH
        or snapshot.release_policy != compile_release_policy(policy)
        or (
            snapshot.context.purpose,
            snapshot.context.request_id,
            snapshot.context.target,
            snapshot.context.workflow_run_id,
            snapshot.context.control,
            snapshot.context.catalog_digest,
            snapshot.context.run_attempt,
        )
        != (
            context.purpose,
            context.request_id,
            context.target,
            context.workflow_run_id,
            context.control,
            context.catalog_digest,
            None,
        )
    ):
        message = (
            "NuGet eligibility requires exact protected-main native context"
        )
        raise ValueError(message)


@dataclass(frozen=True, slots=True)
class NugetControlReadback:
    """Retained initial platform collection identity, not a review verdict.

    The protected collector admits current main, run and reviewed-tree facts.
    Its response digest retains that source. Later freshness follows the shared
    protected-path rule and does not require unrelated main to remain frozen.
    """

    target: str
    workflow_run_id: int
    reviewed_head: str
    reviewed_tree: str
    pull_request: int
    observed_at: datetime
    response_digest: str

    def __post_init__(self) -> None:
        """Reject malformed source identities and timestamps."""
        for name in ("target", "reviewed_head", "reviewed_tree"):
            shared._decision_sha(
                getattr(self, name), field=f"NuGet control.{name}"
            )
        shared._decision_integer(
            self.workflow_run_id, field="NuGet control.run"
        )
        shared._decision_integer(self.pull_request, field="NuGet control.PR")
        shared._decision_digest(
            self.response_digest, field="NuGet control.responses"
        )
        shared._utc_now(self.observed_at)

    def to_document(self) -> dict[str, JsonValue]:
        """Return the closed collector projection and source identity."""
        return {
            "target": self.target,
            "workflow-run-id": self.workflow_run_id,
            "reviewed-head": self.reviewed_head,
            "reviewed-tree": self.reviewed_tree,
            "pull-request": self.pull_request,
            "observed-at": shared._format_instant(self.observed_at),
            "response-digest": self.response_digest,
        }


def _control_from_document(value: JsonValue) -> NugetControlReadback:
    document = shared._object(value, context="NuGet control")
    shared._closed(
        document,
        required=frozenset(
            {
                "target",
                "workflow-run-id",
                "reviewed-head",
                "reviewed-tree",
                "pull-request",
                "observed-at",
                "response-digest",
            }
        ),
        context="NuGet control",
    )
    return NugetControlReadback(
        shared._decision_sha(document["target"], field="NuGet control.target"),
        shared._decision_integer(
            document["workflow-run-id"], field="NuGet control.run"
        ),
        shared._decision_sha(
            document["reviewed-head"], field="NuGet control.head"
        ),
        shared._decision_sha(
            document["reviewed-tree"], field="NuGet control.tree"
        ),
        shared._decision_integer(
            document["pull-request"], field="NuGet control.PR"
        ),
        shared._parse_instant(
            document["observed-at"], context="NuGet control.time"
        ),
        shared._decision_digest(
            document["response-digest"], field="NuGet control.digest"
        ),
    )


@dataclass(frozen=True, slots=True)
class NugetLiveEligibilityDecision:
    """Native Decision, including a truthful state-only blocked report."""

    context: shared.LiveEligibilityContext
    governance: shared.LiveEligibilityGovernanceBinding
    platform: NugetControlReadback | None
    profile: NugetDestinationOperationProfile | None
    result: shared.EligibilityResult
    diagnostics: tuple[str, ...]

    def to_document(self) -> dict[str, JsonValue]:
        """Return native evidence with no placeholder npm scanner result."""
        return {
            "schema": NUGET_ELIGIBILITY_SCHEMA,
            "context": _context_document(self.context),
            "governance": self.governance.to_document(),
            "platform": None
            if self.platform is None
            else self.platform.to_document(),
            "profile": None
            if self.profile is None
            else self.profile.to_document(),
            "result": self.result.value,
            "diagnostics": cast("list[JsonValue]", list(self.diagnostics)),
        }

    @property
    def decision_digest(self) -> str:
        """Return the canonical native eligibility identity."""
        return canonical_sha256(self.to_document())


@dataclass(frozen=True, slots=True)
class AdmittedNugetLiveEligibilityDecision:
    """Parser-admitted passing native Decision with complete frozen profile."""

    context: shared.LiveEligibilityContext
    governance: shared.LiveEligibilityGovernanceBinding
    platform: NugetControlReadback
    profile: NugetDestinationOperationProfile
    canonical_digest: str
    canonical_bytes: bytes

    def __post_init__(self) -> None:
        """Check wrapper integrity before contextual admission."""
        if (
            type(self.context) is not shared.LiveEligibilityContext
            or type(self.governance)
            is not shared.LiveEligibilityGovernanceBinding
            or type(self.platform) is not NugetControlReadback
            or type(self.profile) is not NugetDestinationOperationProfile
            or type(self.canonical_bytes) is not bytes
        ):
            message = "NuGet eligibility admission integrity failed"
            raise TypeError(message)
        if (
            parse_canonical_json(self.canonical_bytes) != self.to_document()
            or canonical_sha256(self.to_document()) != self.canonical_digest
        ):
            message = "NuGet eligibility admission integrity failed"
            raise ValueError(message)

    def to_document(self) -> dict[str, JsonValue]:
        """Return exactly the admitted passing payload."""
        return NugetLiveEligibilityDecision(
            self.context,
            self.governance,
            self.platform,
            self.profile,
            shared.EligibilityResult.PASS,
            (),
        ).to_document()

    @property
    def decision_digest(self) -> str:
        """Return the canonical native eligibility identity."""
        return self.canonical_digest


def admit_nuget_live_eligibility_decision(  # noqa: PLR0913
    canonical_bytes: bytes,
    *,
    intent: ReleaseIntent,
    repository_model: AdmittedRepositoryModelSnapshot,
    policy: ReleasePolicy,
    expected_digest: str,
    admission_mode: shared.LiveEligibilityAdmissionMode,
    now: datetime,
) -> AdmittedNugetLiveEligibilityDecision:
    """Admit native evidence under the caller-selected context and phase."""
    if (
        type(admission_mode) is not shared.LiveEligibilityAdmissionMode
        or type(canonical_bytes) is not bytes
    ):
        message = (
            "NuGet eligibility needs exact bytes and caller-selected phase"
        )
        raise TypeError(message)
    now = shared._utc_now(now)
    document = parse_canonical_json(canonical_bytes)
    shared._closed(
        document,
        required=frozenset(
            {
                "schema",
                "context",
                "governance",
                "platform",
                "profile",
                "result",
                "diagnostics",
            }
        ),
        context="NuGet eligibility",
    )
    if (
        document["schema"] != NUGET_ELIGIBILITY_SCHEMA
        or canonical_sha256(document) != expected_digest
        or document["result"] != shared.EligibilityResult.PASS.value
        or document["diagnostics"] != []
    ):
        message = "NuGet eligibility is not an exact passing Decision"
        raise ValueError(message)
    context = shared._decision_context(document["context"])
    _validate_context(context, intent, repository_model, policy)
    governance = shared._decision_governance(document["governance"])
    profile = NugetDestinationOperationProfile(
        canonicalize(document["profile"])
    )
    platform = _control_from_document(document["platform"])
    if (
        governance.source != policy.governance
        or governance.eligibility_main_sha != intent.target
        or platform.target != intent.target
        or platform.workflow_run_id != intent.workflow_run_id
        or not timedelta(0)
        <= governance.observed_at - platform.observed_at
        <= timedelta(minutes=5)
        or not governance.attestation.inspected_at
        <= governance.observed_at
        < governance.attestation.expires_at
        or governance.observed_at > now
        or not nuget_destination_primitive_is_admitted(governance.attestation)
    ):
        message = (
            "NuGet eligibility protected source or native authority mismatch"
        )
        raise ValueError(message)
    effective_at = (
        now
        if admission_mode
        is shared.LiveEligibilityAdmissionMode.CURRENT_FRESHNESS
        else governance.observed_at
    )
    shared.require_action_governance(
        governance.attestation,
        now=effective_at,
        destination_operation_profile_digest=profile.profile_digest,
    )
    return AdmittedNugetLiveEligibilityDecision(
        context, governance, platform, profile, expected_digest, canonical_bytes
    )


def evaluate_nuget_live_eligibility(  # noqa: PLR0913
    context: shared.LiveEligibilityContext,
    *,
    intent: ReleaseIntent,
    repository_model: AdmittedRepositoryModelSnapshot,
    policy: ReleasePolicy,
    client: shared.GovernanceSourceClient,
    now: datetime,
    platform: NuGetPlatformFacts | None = None,
    profile: NugetDestinationOperationProfile | None = None,
) -> NugetLiveEligibilityDecision:
    """Evaluate authority while preserving the state-only blocked case."""
    _validate_context(context, intent, repository_model, policy)
    observation = shared.observe_governance_source(
        policy.governance, client, now=now
    )
    governance = shared.LiveEligibilityGovernanceBinding.from_observation(
        observation
    )
    diagnostics: list[str] = []
    control = None
    if not observation.attestation.live_enabled:
        diagnostics.append("governance-live-disabled")
    else:
        if observation.current_main_sha != intent.target:
            diagnostics.append("initial-protected-main-target-changed")
        if platform is None:
            diagnostics.append("protected-main-platform-readback-missing")
        else:
            require_nuget_live_platform(
                observation,
                platform,
                target=intent.target,
                workflow_run_id=intent.workflow_run_id,
                now=now,
            )
            if (
                platform.current_main_sha != intent.target
                or not platform.review_carriers
            ):
                diagnostics.append("protected-main-review-readback-incomplete")
            else:
                control = NugetControlReadback(
                    platform.target,
                    platform.workflow_run_id,
                    platform.reviewed_head_sha,
                    platform.reviewed_tree_sha,
                    platform.pull_request_number,
                    platform.observed_at,
                    platform.readback_digest,
                )
        if profile is None:
            diagnostics.append("native-operation-profile-missing")
        else:
            try:
                shared.require_action_governance(
                    observation.attestation,
                    now=now,
                    destination_operation_profile_digest=profile.profile_digest,
                )
            except shared.GovernanceRejectionError:
                diagnostics.append("native-operation-not-admitted")
    return NugetLiveEligibilityDecision(
        context,
        governance,
        control,
        profile,
        shared.EligibilityResult.BLOCKED
        if diagnostics
        else shared.EligibilityResult.PASS,
        tuple(diagnostics),
    )
