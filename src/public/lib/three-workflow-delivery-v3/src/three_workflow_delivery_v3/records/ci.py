"""Closed immutable records for the Workflow Delivery v3 CI shadow slice."""

from __future__ import annotations

import re
from dataclasses import dataclass
from typing import TYPE_CHECKING, cast
from urllib.parse import urlsplit

from three_workflow_delivery_v3.canonical import (
    JsonValue,
    canonical_sha256,
    parse_canonical_json,
)
from three_workflow_delivery_v3.catalogs import QUALITY_DEFINITIONS
from three_workflow_delivery_v3.ci.path_admission import (
    is_repository_only_path,
    is_static_reference_control_path,
)

if TYPE_CHECKING:
    from collections.abc import Mapping

    from three_workflow_delivery_v3.catalogs import QualityDefinition

CI_CANDIDATE_SCHEMA = "workflow-delivery/v3/ci-candidate"
CI_OBLIGATION_SCHEMA = "workflow-delivery/v3/ci-obligation"
CI_QUALIFICATION_SNAPSHOT_SCHEMA = (
    "workflow-delivery/v3/ci-qualification-snapshot"
)
CI_ARTIFACT_SCHEMA = "workflow-delivery/v3/ci-artifact"
CI_EVIDENCE_SCHEMA = "workflow-delivery/v3/ci-evidence"
CI_LANE_RESULT_SCHEMA = "workflow-delivery/v3/ci-lane-result"
CI_OBLIGATION_DISPOSITION_SCHEMA = (
    "workflow-delivery/v3/ci-obligation-disposition"
)
CI_SLICE_SUMMARY_SCHEMA = "workflow-delivery/v3/ci-slice-summary"
CI_SLICE_DECISION_SCHEMA = "workflow-delivery/v3/ci-slice-decision"

CI_WORKFLOW_PATH = ".github/workflows/workflow-delivery-v3-ci.yml"
CI_LANE_IDS = (
    "root-hk",
    "project-build",
    "project-test",
    "npm-artifact-build",
)
CI_ROOT_HK_DEFINITION = "repository/source-tree-conformance-v1"

type CiOutputIdentity = tuple[str, str, str]

_CI_LANE_ID_SET = frozenset(CI_LANE_IDS)
_CI_LANE_DEFINITIONS = {
    "root-hk": CI_ROOT_HK_DEFINITION,
    "project-build": "node/project-build-v1",
    "project-test": "node/project-test-v1",
    "npm-artifact-build": "node/npm-artifact-v1",
}
_CI_LANE_PREREQUISITES = {
    "root-hk": (),
    "project-build": (),
    "project-test": (),
    "npm-artifact-build": (),
}
_EVENT_PURPOSES = {
    "pull_request": "ci-pr-slice-shadow",
    "workflow_dispatch": "slice-validation",
}
_EVENT_SCOPE_MODES = {
    "pull_request": "incremental",
    "workflow_dispatch": "slice-validation",
}
_RAW_TO_NORMALIZED_OUTCOME = {
    "success": "satisfied",
    "failure": "failed",
    "skipped": "skipped",
    "timed-out": "timed-out",
    "unknown": "unknown",
}
_EVIDENCE_OUTCOMES = frozenset(_RAW_TO_NORMALIZED_OUTCOME.values())
_LANE_RESULT_OUTCOMES = _EVIDENCE_OUTCOMES | frozenset({"empty"})
_FINAL_DISPOSITION_OUTCOMES = _EVIDENCE_OUTCOMES | frozenset(
    {"empty", "incomplete"}
)
_TERMINAL_RESULTS = frozenset({"success", "failure", "incomplete"})
_PR_SLO_RESULTS = frozenset({"met", "missed", "excluded", "not-applicable"})
_PR_SLO_REASONS = frozenset(
    {
        "ordinary-pull-request",
        "broad-change",
        "superseded-candidate",
        "supersession-unavailable",
        "not-pull-request",
    }
)
_SUPERSESSION_STATES = frozenset(
    {"not-superseded", "superseded", "unsupported", "not-applicable"}
)
_SUPERSESSION_REASONS = {
    "not-superseded": "trusted-current-candidate",
    "superseded": "trusted-superseded-candidate",
    "unsupported": "platform-proof-unavailable",
    "not-applicable": "not-pull-request",
}
_PR_SLO_SECONDS = 12 * 60
_PAIR_FIELD_COUNT = 2
_FAILURE_CLASSES = frozenset(
    {
        "none",
        "incomplete-model-plan",
        "incomplete-qualification",
        "quality-failure",
    }
)
_NEXT_ACTIONS = frozenset(
    {
        "none",
        "fix-model-plan-and-rerun",
        "fix-quality-failure-and-rerun",
        "rerun-candidate",
    }
)
_FIRST_SLICE_PROJECT_NODES = ("@hcoona/hcoona-release-smoke-npm",)
_FIRST_SLICE_RELEASE_UNITS = ("hcoona-release-smoke-npm",)
_FIRST_SLICE_VARIANTS = ("npm-package",)
_FIRST_SLICE_OUTPUTS = (("npm-tarball", "primary-package", "npm-tarball"),)
_FIRST_SLICE_PROJECT_PATH = "src/public/lib/hcoona-release-smoke-npm"
_FIRST_SLICE_AFFECTING_PATHS = frozenset(
    {
        ".github/workflows/workflow-delivery-v3-ci.yml",
        "eng/workflow-delivery/v3/policies/hcoona-release-smoke-npm.yml",
        "mise.lock",
        "mise.toml",
        "package.json",
        "pnpm-lock.yaml",
        "pnpm-workspace.yaml",
        "src/public/lib/version.json",
        "src/public/version.json",
        "src/version.json",
        "version.json",
    }
)
_FIRST_SLICE_AFFECTING_PREFIXES = (
    ".github/actions/workflow-delivery-v3",
    "eng/scripts/workflow_delivery_v3_",
    "src/public/lib/three-workflow-delivery-v3/",
)
_SLO_GOVERNANCE_PREFIXES = (
    ".github/workflow-delivery/governance/",
    "eng/workflow-delivery/v3/policies/",
)
_SLO_BROAD_CONTROL_PREFIXES = (
    ".github/actions/workflow-delivery-v3",
    "eng/scripts/workflow_delivery_v3_",
    "src/public/lib/three-workflow-delivery-v3/",
)
_SLO_BROAD_CONTROL_PATHS = frozenset(
    {
        ".github/CODEOWNERS",
        CI_WORKFLOW_PATH,
        "global.pkl",
        "hk.pkl",
    }
)
_SLO_ROOT_TOOLCHAIN_PATHS = frozenset(
    {
        "Directory.Build.props",
        "Directory.Build.targets",
        "global.json",
        "mise.lock",
        "mise.toml",
        "nuget.config",
        "package.json",
        "pnpm-lock.yaml",
        "pnpm-workspace.yaml",
        "pyproject.toml",
        "uv.lock",
    }
)
_SHA_PATTERN = re.compile(r"[0-9a-f]{40}\Z")
_DIGEST_PATTERN = re.compile(r"sha256:[0-9a-f]{64}\Z")
_SHA512_DIGEST_PATTERN = re.compile(r"sha512:[0-9a-f]{128}\Z")


@dataclass(frozen=True, slots=True)
class CiCandidate:
    """Exact event and same-revision control identity for one CI request."""

    event_kind: str
    purpose: str
    repository: str
    workflow_path: str
    workflow_sha: str
    request_id: str
    producer: str
    workflow_run_id: int
    run_attempt: int
    selected_ref: str
    target: str
    base_sha: str | None
    head_sha: str | None
    tested_merge_sha: str | None

    def __post_init__(self) -> None:
        """Reject an invalid or open candidate at construction."""
        _validate_ci_candidate(self)

    def to_document(self) -> dict[str, JsonValue]:
        """Return the complete canonical candidate document."""
        return {
            "schema": CI_CANDIDATE_SCHEMA,
            "event-kind": self.event_kind,
            "purpose": self.purpose,
            "repository": self.repository,
            "workflow-path": self.workflow_path,
            "workflow-sha": self.workflow_sha,
            "request-id": self.request_id,
            "producer": self.producer,
            "workflow-run-id": self.workflow_run_id,
            "run-attempt": self.run_attempt,
            "selected-ref": self.selected_ref,
            "target": self.target,
            "base-sha": self.base_sha,
            "head-sha": self.head_sha,
            "tested-merge-sha": self.tested_merge_sha,
        }


@dataclass(frozen=True, slots=True)
class CiObligation:
    """One required-only static-lane obligation in the immutable CI Plan."""

    obligation_id: str
    lane_id: str
    request_digest: str
    definition_id: str
    definition_digest: str
    prerequisites: tuple[str, ...]
    selected: bool
    required: bool
    expected_evidence_id: str

    def __post_init__(self) -> None:
        """Reject an invalid or advisory obligation at construction."""
        _validate_ci_obligation(self)

    def to_document(self) -> dict[str, JsonValue]:
        """Return the complete canonical obligation document."""
        prerequisites: list[JsonValue] = list(self.prerequisites)
        return {
            "schema": CI_OBLIGATION_SCHEMA,
            "obligation-id": self.obligation_id,
            "lane-id": self.lane_id,
            "request-digest": self.request_digest,
            "definition-id": self.definition_id,
            "definition-digest": self.definition_digest,
            "prerequisites": prerequisites,
            "selected": self.selected,
            "required": self.required,
            "expected-evidence-id": self.expected_evidence_id,
        }


@dataclass(frozen=True, slots=True)
class CiQualificationSnapshot:
    """Complete immutable semantic Plan for one CI candidate."""

    candidate: CiCandidate
    producer: str
    workflow_run_id: int
    run_attempt: int
    repository_model_digest: str
    root_hk_definition: str
    root_hk_definition_digest: str
    scope_mode: str
    changed_paths: tuple[str, ...]
    selected_project_nodes: tuple[str, ...]
    selected_release_units: tuple[str, ...]
    selected_variants: tuple[str, ...]
    selected_outputs: tuple[CiOutputIdentity, ...]
    obligations: tuple[CiObligation, ...]
    expected_evidence_ids: tuple[str, ...]
    ready: bool
    diagnostics: tuple[str, ...]

    def __post_init__(self) -> None:
        """Reject an invalid or incomplete Plan closure at construction."""
        _validate_ci_qualification_snapshot(self)

    def to_document(self) -> dict[str, JsonValue]:
        """Return the complete canonical Qualification Snapshot document."""
        changed_paths: list[JsonValue] = list(self.changed_paths)
        selected_project_nodes: list[JsonValue] = list(
            self.selected_project_nodes
        )
        selected_release_units: list[JsonValue] = list(
            self.selected_release_units
        )
        selected_variants: list[JsonValue] = list(self.selected_variants)
        selected_outputs: list[JsonValue] = [
            _output_identity_document(output)
            for output in self.selected_outputs
        ]
        obligations: list[JsonValue] = [
            obligation.to_document() for obligation in self.obligations
        ]
        expected_evidence_ids: list[JsonValue] = list(
            self.expected_evidence_ids
        )
        diagnostics: list[JsonValue] = list(self.diagnostics)
        return {
            "schema": CI_QUALIFICATION_SNAPSHOT_SCHEMA,
            "candidate": self.candidate.to_document(),
            "producer": self.producer,
            "workflow-run-id": self.workflow_run_id,
            "run-attempt": self.run_attempt,
            "repository-model-digest": self.repository_model_digest,
            "root-hk-definition": self.root_hk_definition,
            "root-hk-definition-digest": self.root_hk_definition_digest,
            "scope-mode": self.scope_mode,
            "changed-paths": changed_paths,
            "selected-project-nodes": selected_project_nodes,
            "selected-release-units": selected_release_units,
            "selected-variants": selected_variants,
            "selected-outputs": selected_outputs,
            "obligations": obligations,
            "expected-evidence-ids": expected_evidence_ids,
            "ready": self.ready,
            "diagnostics": diagnostics,
        }


@dataclass(frozen=True, slots=True)
class CiArtifact:
    """Immutable retained npm artifact bound to one current CI candidate."""

    candidate: CiCandidate
    producer: str
    workflow_run_id: int
    run_attempt: int
    output_id: str
    logical_role: str
    media_kind: str
    artifact_id: int
    artifact_name: str
    artifact_url: str
    transport_digest: str
    tarball_basename: str
    content_sha256: str
    content_sha512: str
    byte_size: int
    provenance_digest: str
    entries: tuple[str, ...]
    lifecycle_scripts: tuple[tuple[str, str], ...]

    def __post_init__(self) -> None:
        """Reject an artifact outside the exact current npm lane binding."""
        _validate_ci_artifact(self)

    def to_document(self) -> dict[str, JsonValue]:
        """Return the complete canonical CI artifact document."""
        entries: list[JsonValue] = list(self.entries)
        lifecycle_scripts: list[JsonValue] = [
            [name, command] for name, command in self.lifecycle_scripts
        ]
        return {
            "schema": CI_ARTIFACT_SCHEMA,
            "candidate": self.candidate.to_document(),
            "producer": self.producer,
            "workflow-run-id": self.workflow_run_id,
            "run-attempt": self.run_attempt,
            "output-id": self.output_id,
            "logical-role": self.logical_role,
            "media-kind": self.media_kind,
            "artifact-id": self.artifact_id,
            "artifact-name": self.artifact_name,
            "artifact-url": self.artifact_url,
            "transport-digest": self.transport_digest,
            "tarball-basename": self.tarball_basename,
            "content-sha256": self.content_sha256,
            "content-sha512": self.content_sha512,
            "byte-size": self.byte_size,
            "provenance-digest": self.provenance_digest,
            "entries": entries,
            "lifecycle-scripts": lifecycle_scripts,
        }


@dataclass(frozen=True, slots=True)
class CiEvidence:
    """Mechanical output bound to one exact selected Plan obligation."""

    evidence_id: str
    plan_digest: str
    candidate: CiCandidate
    obligation: CiObligation
    producer: str
    workflow_run_id: int
    run_attempt: int
    runner: str
    raw_outcome: str
    output_digests: tuple[str, ...]
    artifacts: tuple[CiArtifact, ...]
    normalized_outcome: str
    diagnostics: tuple[str, ...]

    def __post_init__(self) -> None:
        """Reject Evidence outside its exact planned binding."""
        _validate_ci_evidence(self)

    def to_document(self) -> dict[str, JsonValue]:
        """Return the complete canonical Evidence document."""
        output_digests: list[JsonValue] = list(self.output_digests)
        artifacts: list[JsonValue] = [
            artifact.to_document() for artifact in self.artifacts
        ]
        diagnostics: list[JsonValue] = list(self.diagnostics)
        return {
            "schema": CI_EVIDENCE_SCHEMA,
            "evidence-id": self.evidence_id,
            "plan-digest": self.plan_digest,
            "candidate": self.candidate.to_document(),
            "obligation": self.obligation.to_document(),
            "producer": self.producer,
            "workflow-run-id": self.workflow_run_id,
            "run-attempt": self.run_attempt,
            "runner": self.runner,
            "raw-outcome": self.raw_outcome,
            "output-digests": output_digests,
            "artifacts": artifacts,
            "normalized-outcome": self.normalized_outcome,
            "diagnostics": diagnostics,
        }


@dataclass(frozen=True, slots=True)
class CiLaneResult:
    """Always-emitted static-lane result for one exact Plan."""

    plan_digest: str
    candidate: CiCandidate
    lane_id: str
    producer: str
    workflow_run_id: int
    run_attempt: int
    disposition: str
    evidence: CiEvidence | None

    def __post_init__(self) -> None:
        """Reject a lane result outside its closed disposition."""
        _validate_ci_lane_result(self)

    def to_document(self) -> dict[str, JsonValue]:
        """Return the complete canonical lane-result document."""
        evidence: JsonValue = (
            None if self.evidence is None else self.evidence.to_document()
        )
        return {
            "schema": CI_LANE_RESULT_SCHEMA,
            "plan-digest": self.plan_digest,
            "candidate": self.candidate.to_document(),
            "lane-id": self.lane_id,
            "producer": self.producer,
            "workflow-run-id": self.workflow_run_id,
            "run-attempt": self.run_attempt,
            "disposition": self.disposition,
            "evidence": evidence,
        }


@dataclass(frozen=True, slots=True)
class CiObligationDisposition:
    """Final closed outcome for one planned obligation."""

    obligation: CiObligation
    outcome: str
    evidence_digests: tuple[str, ...]
    explanation: str

    def __post_init__(self) -> None:
        """Reject an invalid final obligation disposition."""
        _validate_ci_obligation_disposition(self)

    def to_document(self) -> dict[str, JsonValue]:
        """Return the canonical obligation-disposition document."""
        evidence_digests: list[JsonValue] = list(self.evidence_digests)
        return {
            "schema": CI_OBLIGATION_DISPOSITION_SCHEMA,
            "obligation": self.obligation.to_document(),
            "outcome": self.outcome,
            "evidence-digests": evidence_digests,
            "explanation": self.explanation,
        }


@dataclass(frozen=True, slots=True)
class CiSliceSummary:
    """Explicitly non-authoritative human-readable CI slice summary."""

    authority: str
    terminal_result: str
    text: str

    def __post_init__(self) -> None:
        """Reject an authoritative or inconsistent summary."""
        _validate_ci_slice_summary(self)

    def to_document(self) -> dict[str, JsonValue]:
        """Return the canonical summary document."""
        return {
            "schema": CI_SLICE_SUMMARY_SCHEMA,
            "authority": self.authority,
            "terminal-result": self.terminal_result,
            "text": self.text,
        }


@dataclass(frozen=True, slots=True)
class CiSliceDecision:
    """Immutable, digest-bound, non-authoritative CI slice Decision."""

    plan_digest: str
    repository_model_digest: str
    candidate: CiCandidate
    producer: str
    workflow_run_id: int
    run_attempt: int
    scope_mode: str
    changed_paths: tuple[str, ...]
    selected_project_nodes: tuple[str, ...]
    selected_release_units: tuple[str, ...]
    selected_variants: tuple[str, ...]
    selected_outputs: tuple[CiOutputIdentity, ...]
    plan_diagnostics: tuple[str, ...]
    obligation_dispositions: tuple[CiObligationDisposition, ...]
    admitted_evidence_digests: tuple[str, ...]
    admitted_artifact_digests: tuple[str, ...]
    explanation: str
    terminal_result: str
    failure_class: str
    next_action: str
    authority: str
    elapsed_seconds: int
    supersession_state: str
    supersession_reason: str
    pr_slo: str
    pr_slo_reason: str
    summary: CiSliceSummary

    def __post_init__(self) -> None:
        """Reject an invalid or authoritative CI Slice Decision."""
        _validate_ci_slice_decision(self)

    def to_document(self) -> dict[str, JsonValue]:
        """Return the complete canonical CI Slice Decision document."""
        changed_paths: list[JsonValue] = list(self.changed_paths)
        selected_project_nodes: list[JsonValue] = list(
            self.selected_project_nodes
        )
        selected_release_units: list[JsonValue] = list(
            self.selected_release_units
        )
        selected_variants: list[JsonValue] = list(self.selected_variants)
        selected_outputs: list[JsonValue] = [
            _output_identity_document(output)
            for output in self.selected_outputs
        ]
        plan_diagnostics: list[JsonValue] = list(self.plan_diagnostics)
        obligation_dispositions: list[JsonValue] = [
            disposition.to_document()
            for disposition in self.obligation_dispositions
        ]
        admitted_evidence_digests: list[JsonValue] = list(
            self.admitted_evidence_digests
        )
        admitted_artifact_digests: list[JsonValue] = list(
            self.admitted_artifact_digests
        )
        return {
            "schema": CI_SLICE_DECISION_SCHEMA,
            "plan-digest": self.plan_digest,
            "repository-model-digest": self.repository_model_digest,
            "candidate": self.candidate.to_document(),
            "producer": self.producer,
            "workflow-run-id": self.workflow_run_id,
            "run-attempt": self.run_attempt,
            "scope-mode": self.scope_mode,
            "changed-paths": changed_paths,
            "selected-project-nodes": selected_project_nodes,
            "selected-release-units": selected_release_units,
            "selected-variants": selected_variants,
            "selected-outputs": selected_outputs,
            "plan-diagnostics": plan_diagnostics,
            "obligation-dispositions": obligation_dispositions,
            "admitted-evidence-digests": admitted_evidence_digests,
            "admitted-artifact-digests": admitted_artifact_digests,
            "explanation": self.explanation,
            "terminal-result": self.terminal_result,
            "failure-class": self.failure_class,
            "next-action": self.next_action,
            "authority": self.authority,
            "elapsed-seconds": self.elapsed_seconds,
            "supersession-state": self.supersession_state,
            "supersession-reason": self.supersession_reason,
            "pr-slo": self.pr_slo,
            "pr-slo-reason": self.pr_slo_reason,
            "summary": self.summary.to_document(),
        }


def _require_exact_type(
    value: object,
    expected: type[object],
    *,
    field: str,
) -> None:
    if type(value) is not expected:
        message = f"{field} has the wrong runtime type"
        raise TypeError(message)


def _require_nonempty_string(value: object, *, field: str) -> None:
    _require_exact_type(value, str, field=field)
    if not value:
        message = f"{field} must be nonempty"
        raise ValueError(message)


def _require_choice(
    value: object,
    choices: frozenset[str] | set[str] | dict[str, str],
    *,
    field: str,
) -> None:
    _require_nonempty_string(value, field=field)
    if value not in choices:
        message = f"{field} has an invalid closed value"
        raise ValueError(message)


def _require_positive_integer(value: object, *, field: str) -> None:
    _require_exact_type(value, int, field=field)
    if cast("int", value) <= 0:
        message = f"{field} must be positive"
        raise ValueError(message)


def _require_nonnegative_integer(value: object, *, field: str) -> None:
    _require_exact_type(value, int, field=field)
    if cast("int", value) < 0:
        message = f"{field} must be nonnegative"
        raise ValueError(message)


def _require_sha(value: object, *, field: str) -> None:
    _require_exact_type(value, str, field=field)
    if _SHA_PATTERN.fullmatch(cast("str", value)) is None:
        message = f"{field} must be 40 lowercase hexadecimal characters"
        raise ValueError(message)


def _require_optional_sha(value: object, *, field: str) -> None:
    if value is not None:
        _require_sha(value, field=field)


def _require_digest(value: object, *, field: str) -> None:
    _require_exact_type(value, str, field=field)
    if _DIGEST_PATTERN.fullmatch(cast("str", value)) is None:
        message = f"{field} must be sha256:<64 lowercase hex>"
        raise ValueError(message)


def _require_sha512_digest(value: object, *, field: str) -> None:
    _require_exact_type(value, str, field=field)
    if _SHA512_DIGEST_PATTERN.fullmatch(cast("str", value)) is None:
        message = f"{field} must be sha512:<128 lowercase hex>"
        raise ValueError(message)


def _require_string_tuple(
    value: object,
    *,
    field: str,
    unique: bool = False,
) -> None:
    _require_exact_type(value, tuple, field=field)
    items = cast("tuple[object, ...]", value)
    accepted: list[str] = []
    for index, item in enumerate(items):
        _require_nonempty_string(item, field=f"{field}[{index}]")
        accepted.append(cast("str", item))
    if unique and len(set(accepted)) != len(accepted):
        message = f"{field} contains duplicate values"
        raise ValueError(message)


def _require_string_pairs(
    value: object,
    *,
    field: str,
) -> None:
    _require_exact_type(value, tuple, field=field)
    pairs = cast("tuple[object, ...]", value)
    accepted: list[tuple[str, str]] = []
    for index, pair in enumerate(pairs):
        _require_exact_type(pair, tuple, field=f"{field}[{index}]")
        values = cast("tuple[object, ...]", pair)
        if len(values) != _PAIR_FIELD_COUNT:
            message = f"{field}[{index}] must contain exactly two strings"
            raise ValueError(message)
        name, command = values
        _require_nonempty_string(name, field=f"{field}[{index}][0]")
        _require_nonempty_string(command, field=f"{field}[{index}][1]")
        accepted.append((cast("str", name), cast("str", command)))
    if len({name for name, _ in accepted}) != len(accepted):
        message = f"{field} contains duplicate script names"
        raise ValueError(message)
    if tuple(sorted(accepted)) != tuple(accepted):
        message = f"{field} must use canonical sorted order"
        raise ValueError(message)


def _require_output_identity_tuple(
    value: object,
    *,
    field: str,
) -> None:
    _require_exact_type(value, tuple, field=field)
    outputs = cast("tuple[object, ...]", value)
    accepted: list[CiOutputIdentity] = []
    for index, output in enumerate(outputs):
        _require_exact_type(output, tuple, field=f"{field}[{index}]")
        values = cast("tuple[object, ...]", output)
        if len(values) != 3:  # noqa: PLR2004
            message = (
                f"{field}[{index}] must contain output ID, role, and media kind"
            )
            raise ValueError(message)
        output_id, role, media_kind = values
        _require_nonempty_string(output_id, field=f"{field}[{index}][0]")
        _require_nonempty_string(role, field=f"{field}[{index}][1]")
        _require_nonempty_string(media_kind, field=f"{field}[{index}][2]")
        accepted.append(
            (
                cast("str", output_id),
                cast("str", role),
                cast("str", media_kind),
            )
        )
    if len({output_id for output_id, _, _ in accepted}) != len(accepted):
        message = f"{field} contains duplicate output IDs"
        raise ValueError(message)
    if tuple(sorted(accepted)) != tuple(accepted):
        message = f"{field} must use canonical sorted order"
        raise ValueError(message)


def _require_digest_tuple(
    value: object,
    *,
    field: str,
    unique: bool = True,
) -> None:
    _require_exact_type(value, tuple, field=field)
    digests = cast("tuple[object, ...]", value)
    accepted: list[str] = []
    for index, digest in enumerate(digests):
        _require_digest(digest, field=f"{field}[{index}]")
        accepted.append(cast("str", digest))
    if unique and len(set(accepted)) != len(accepted):
        message = f"{field} contains duplicate values"
        raise ValueError(message)


def _validate_ci_candidate(  # noqa: C901, PLR0915
    candidate: CiCandidate,
) -> None:
    _require_exact_type(candidate, CiCandidate, field="candidate")
    _require_choice(
        candidate.event_kind,
        _EVENT_PURPOSES,
        field="candidate.event_kind",
    )
    expected_purpose = _EVENT_PURPOSES[candidate.event_kind]
    if candidate.purpose != expected_purpose:
        message = "candidate purpose does not match event kind"
        raise ValueError(message)
    _require_exact_type(candidate.purpose, str, field="candidate.purpose")
    _require_nonempty_string(candidate.repository, field="candidate.repository")
    owner, separator, repository = candidate.repository.partition("/")
    if not separator or not owner or not repository or "/" in repository:
        message = "candidate.repository must be owner/repository"
        raise ValueError(message)
    _require_exact_type(
        candidate.workflow_path,
        str,
        field="candidate.workflow_path",
    )
    if candidate.workflow_path != CI_WORKFLOW_PATH:
        message = "candidate.workflow_path is not the canonical CI workflow"
        raise ValueError(message)
    _require_sha(candidate.workflow_sha, field="candidate.workflow_sha")
    _require_nonempty_string(
        candidate.request_id,
        field="candidate.request_id",
    )
    _require_exact_type(candidate.producer, str, field="candidate.producer")
    if candidate.producer != "request":
        message = "candidate producer must be the request job"
        raise ValueError(message)
    _require_positive_integer(
        candidate.workflow_run_id,
        field="candidate.workflow_run_id",
    )
    _require_positive_integer(
        candidate.run_attempt,
        field="candidate.run_attempt",
    )
    _require_nonempty_string(
        candidate.selected_ref,
        field="candidate.selected_ref",
    )
    if not candidate.selected_ref.startswith("refs/"):
        message = "candidate.selected_ref must be fully qualified"
        raise ValueError(message)
    _require_sha(candidate.target, field="candidate.target")
    _require_optional_sha(candidate.base_sha, field="candidate.base_sha")
    _require_optional_sha(candidate.head_sha, field="candidate.head_sha")
    _require_optional_sha(
        candidate.tested_merge_sha,
        field="candidate.tested_merge_sha",
    )
    if candidate.workflow_sha != candidate.target:
        message = "candidate control SHA must equal the selected target"
        raise ValueError(message)

    if candidate.event_kind == "pull_request":
        if (
            candidate.base_sha is None
            or candidate.head_sha is None
            or candidate.tested_merge_sha is None
        ):
            message = "pull_request candidate requires its comparison identity"
            raise ValueError(message)
        if candidate.base_sha == candidate.head_sha:
            message = "pull_request comparison base and head must differ"
            raise ValueError(message)
        if candidate.tested_merge_sha != candidate.target:
            message = "pull_request target must be the tested merge SHA"
            raise ValueError(message)
        if not (
            candidate.selected_ref.startswith("refs/pull/")
            and candidate.selected_ref.endswith("/merge")
        ):
            message = "pull_request selected_ref must be its tested merge ref"
            raise ValueError(message)
    elif any(
        value is not None
        for value in (
            candidate.base_sha,
            candidate.head_sha,
            candidate.tested_merge_sha,
        )
    ):
        message = "workflow_dispatch candidate has no comparison range"
        raise ValueError(message)


def _validate_ci_obligation(obligation: CiObligation) -> None:
    _require_exact_type(obligation, CiObligation, field="obligation")
    _require_nonempty_string(
        obligation.obligation_id,
        field="obligation.obligation_id",
    )
    _require_choice(
        obligation.lane_id,
        _CI_LANE_ID_SET,
        field="obligation.lane_id",
    )
    _require_digest(
        obligation.request_digest,
        field="obligation.request_digest",
    )
    _require_nonempty_string(
        obligation.definition_id,
        field="obligation.definition_id",
    )
    _require_digest(
        obligation.definition_digest,
        field="obligation.definition_digest",
    )
    _require_string_tuple(
        obligation.prerequisites,
        field="obligation.prerequisites",
        unique=True,
    )
    _require_exact_type(obligation.selected, bool, field="obligation.selected")
    _require_exact_type(obligation.required, bool, field="obligation.required")
    if obligation.required is not obligation.selected:
        message = "CI obligations are selected required work or unselected"
        raise ValueError(message)
    _require_nonempty_string(
        obligation.expected_evidence_id,
        field="obligation.expected_evidence_id",
    )


def _validate_obligation_dag(  # noqa: C901
    obligations: tuple[CiObligation, ...],
) -> None:
    by_id = {item.obligation_id: item for item in obligations}
    if len(by_id) != len(obligations):
        message = "snapshot obligations contain duplicate obligation IDs"
        raise ValueError(message)
    lane_ids = [item.lane_id for item in obligations]
    if len(set(lane_ids)) != len(lane_ids):
        message = "snapshot obligations contain duplicate lane IDs"
        raise ValueError(message)
    if set(lane_ids) != _CI_LANE_ID_SET:
        message = "snapshot obligations must close all four static lanes"
        raise ValueError(message)
    for obligation in obligations:
        for prerequisite in obligation.prerequisites:
            if prerequisite not in by_id:
                message = (
                    f"obligation {obligation.obligation_id} has an unknown "
                    "prerequisite"
                )
                raise ValueError(message)
            if prerequisite == obligation.obligation_id:
                message = "obligation DAG contains a self-cycle"
                raise ValueError(message)
            if obligation.selected and not by_id[prerequisite].selected:
                message = "selected obligation has an unselected prerequisite"
                raise ValueError(message)

    visited: set[str] = set()
    active: set[str] = set()

    def visit(obligation_id: str) -> None:
        if obligation_id in active:
            message = "obligation DAG contains a cycle"
            raise ValueError(message)
        if obligation_id in visited:
            return
        active.add(obligation_id)
        for prerequisite in by_id[obligation_id].prerequisites:
            visit(prerequisite)
        active.remove(obligation_id)
        visited.add(obligation_id)

    for obligation_id in by_id:
        visit(obligation_id)


def _quality_definition_document(
    definition: QualityDefinition,
) -> dict[str, JsonValue]:
    capability_requirements = cast(
        "list[JsonValue]",
        list(definition.capability_requirements),
    )
    return {
        "schema": "workflow-delivery/v3/quality-definition",
        "logical-id": definition.logical_id,
        "subject": definition.subject,
        "operation": definition.operation,
        "implementation-id": definition.implementation_id,
        "execution-class": definition.execution_class,
        "capability-requirements": capability_requirements,
    }


def _fixed_definition_digest(definition_id: str) -> str:
    return canonical_sha256(
        _quality_definition_document(QUALITY_DEFINITIONS[definition_id]),
    )


def _expected_obligation_request_digest(  # noqa: PLR0913
    snapshot: CiQualificationSnapshot,
    *,
    lane_id: str,
    definition_id: str,
    definition_digest: str,
    prerequisites: tuple[str, ...],
    selected: bool,
) -> str:
    return canonical_sha256(
        {
            "schema": "workflow-delivery/v3/ci-obligation-request",
            "candidate-digest": ci_candidate_digest(snapshot.candidate),
            "repository-model-digest": snapshot.repository_model_digest,
            "lane-id": lane_id,
            "definition-id": definition_id,
            "definition-digest": definition_digest,
            "prerequisites": list(prerequisites),
            "selected": selected,
            "required": selected,
            "scope-mode": snapshot.scope_mode,
            "changed-paths": list(snapshot.changed_paths),
            "selected-project-nodes": list(snapshot.selected_project_nodes),
            "selected-release-units": list(snapshot.selected_release_units),
            "selected-variants": list(snapshot.selected_variants),
            "selected-outputs": [
                _output_identity_document(output)
                for output in snapshot.selected_outputs
            ],
        }
    )


def _validate_fixed_plan_obligations(
    snapshot: CiQualificationSnapshot,
) -> None:
    if tuple(item.lane_id for item in snapshot.obligations) != CI_LANE_IDS:
        message = "snapshot obligations are not in fixed static-lane order"
        raise ValueError(message)
    for lane_id, obligation in zip(
        CI_LANE_IDS,
        snapshot.obligations,
        strict=True,
    ):
        definition_id = _CI_LANE_DEFINITIONS[lane_id]
        definition_digest = _fixed_definition_digest(definition_id)
        prerequisites = _CI_LANE_PREREQUISITES[lane_id]
        request_digest = _expected_obligation_request_digest(
            snapshot,
            lane_id=lane_id,
            definition_id=definition_id,
            definition_digest=definition_digest,
            prerequisites=prerequisites,
            selected=obligation.selected,
        )
        expected_evidence_id = (
            f"evidence:{lane_id}:{request_digest.removeprefix('sha256:')}"
        )
        expected = (
            f"ci:{lane_id}",
            lane_id,
            request_digest,
            definition_id,
            definition_digest,
            prerequisites,
            obligation.selected,
            obligation.selected,
            expected_evidence_id,
        )
        actual = (
            obligation.obligation_id,
            obligation.lane_id,
            obligation.request_digest,
            obligation.definition_id,
            obligation.definition_digest,
            obligation.prerequisites,
            obligation.selected,
            obligation.required,
            obligation.expected_evidence_id,
        )
        if actual != expected:
            message = (
                f"snapshot {lane_id} obligation does not match fixed "
                "definition, request, prerequisites, or Evidence identity"
            )
            raise ValueError(message)


def _is_first_slice_affecting_path(path: str) -> bool:
    if is_static_reference_control_path(path):
        return True
    if path == _FIRST_SLICE_PROJECT_PATH or path.startswith(
        f"{_FIRST_SLICE_PROJECT_PATH}/"
    ):
        return True
    return path in _FIRST_SLICE_AFFECTING_PATHS or path.startswith(
        _FIRST_SLICE_AFFECTING_PREFIXES
    )


def _validate_plan_shape(snapshot: CiQualificationSnapshot) -> None:
    selected_lanes = tuple(
        obligation.lane_id
        for obligation in snapshot.obligations
        if obligation.selected
    )
    scope = (
        snapshot.selected_project_nodes,
        snapshot.selected_release_units,
        snapshot.selected_variants,
        snapshot.selected_outputs,
    )
    empty_scope = ((), (), (), ())
    first_slice_scope = (
        _FIRST_SLICE_PROJECT_NODES,
        _FIRST_SLICE_RELEASE_UNITS,
        _FIRST_SLICE_VARIANTS,
        _FIRST_SLICE_OUTPUTS,
    )

    if not snapshot.ready:
        if selected_lanes or scope != empty_scope:
            message = "blocked snapshot must select no lanes or affected scope"
            raise ValueError(message)
        return
    if snapshot.scope_mode == "slice-validation":
        if selected_lanes != CI_LANE_IDS or scope != first_slice_scope:
            message = (
                "slice-validation snapshot must select the complete "
                "first-slice scope"
            )
            raise ValueError(message)
        return
    if selected_lanes == ("root-hk",) and scope == empty_scope:
        if not all(
            is_repository_only_path(path)
            and not _is_first_slice_affecting_path(path)
            for path in snapshot.changed_paths
        ):
            message = (
                "root-hk-only snapshot requires repository-only changed paths"
            )
            raise ValueError(message)
        return
    if selected_lanes == CI_LANE_IDS and scope == first_slice_scope:
        if not any(
            _is_first_slice_affecting_path(path)
            for path in snapshot.changed_paths
        ) or not all(
            _is_first_slice_affecting_path(path)
            or is_repository_only_path(path)
            for path in snapshot.changed_paths
        ):
            message = (
                "complete first-slice snapshot requires classified affected "
                "paths"
            )
            raise ValueError(message)
        return
    message = "ready incremental snapshot has an invalid partial scope"
    raise ValueError(message)


def _validate_ci_qualification_snapshot(  # noqa: C901, PLR0915
    snapshot: CiQualificationSnapshot,
) -> None:
    _require_exact_type(
        snapshot,
        CiQualificationSnapshot,
        field="qualification_snapshot",
    )
    _validate_ci_candidate(snapshot.candidate)
    _require_exact_type(snapshot.producer, str, field="snapshot.producer")
    if snapshot.producer != "plan":
        message = "Qualification Snapshot producer must be the plan job"
        raise ValueError(message)
    _require_positive_integer(
        snapshot.workflow_run_id,
        field="snapshot.workflow_run_id",
    )
    _require_positive_integer(
        snapshot.run_attempt,
        field="snapshot.run_attempt",
    )
    if (
        snapshot.workflow_run_id != snapshot.candidate.workflow_run_id
        or snapshot.run_attempt != snapshot.candidate.run_attempt
    ):
        message = (
            "Qualification Snapshot workflow run or attempt is not current"
        )
        raise ValueError(message)
    _require_digest(
        snapshot.repository_model_digest,
        field="snapshot.repository_model_digest",
    )
    _require_nonempty_string(
        snapshot.root_hk_definition,
        field="snapshot.root_hk_definition",
    )
    _require_digest(
        snapshot.root_hk_definition_digest,
        field="snapshot.root_hk_definition_digest",
    )
    if (
        snapshot.root_hk_definition != CI_ROOT_HK_DEFINITION
        or snapshot.root_hk_definition_digest
        != _fixed_definition_digest(CI_ROOT_HK_DEFINITION)
    ):
        message = "Qualification Snapshot root-HK definition is not current"
        raise ValueError(message)
    _require_choice(
        snapshot.scope_mode,
        set(_EVENT_SCOPE_MODES.values()),
        field="snapshot.scope_mode",
    )
    expected_scope_mode = _EVENT_SCOPE_MODES[snapshot.candidate.event_kind]
    if snapshot.scope_mode != expected_scope_mode:
        message = "snapshot scope mode does not match candidate event kind"
        raise ValueError(message)
    _require_string_tuple(
        snapshot.changed_paths,
        field="snapshot.changed_paths",
        unique=True,
    )
    for path in snapshot.changed_paths:
        if path.startswith("/") or ".." in path.split("/"):
            message = "snapshot.changed_paths must be relative repository paths"
            raise ValueError(message)
    if snapshot.scope_mode == "slice-validation" and snapshot.changed_paths:
        message = "slice-validation snapshot has no changed-path range"
        raise ValueError(message)
    _require_string_tuple(
        snapshot.selected_project_nodes,
        field="snapshot.selected_project_nodes",
        unique=True,
    )
    _require_string_tuple(
        snapshot.selected_release_units,
        field="snapshot.selected_release_units",
        unique=True,
    )
    _require_string_tuple(
        snapshot.selected_variants,
        field="snapshot.selected_variants",
        unique=True,
    )
    _require_output_identity_tuple(
        snapshot.selected_outputs,
        field="snapshot.selected_outputs",
    )
    _require_exact_type(
        snapshot.obligations,
        tuple,
        field="snapshot.obligations",
    )
    for obligation in snapshot.obligations:
        _validate_ci_obligation(obligation)
    _validate_obligation_dag(snapshot.obligations)
    _require_string_tuple(
        snapshot.expected_evidence_ids,
        field="snapshot.expected_evidence_ids",
        unique=True,
    )
    expected_evidence_ids = tuple(
        obligation.expected_evidence_id
        for obligation in snapshot.obligations
        if obligation.selected
    )
    if snapshot.expected_evidence_ids != expected_evidence_ids:
        message = "snapshot expected Evidence identities do not match its Plan"
        raise ValueError(message)
    _require_exact_type(snapshot.ready, bool, field="snapshot.ready")
    if not snapshot.ready and (
        any(obligation.selected for obligation in snapshot.obligations)
        or snapshot.expected_evidence_ids
    ):
        message = "blocked snapshot cannot expose runnable partial work"
        raise ValueError(message)
    _validate_plan_shape(snapshot)
    _require_string_tuple(
        snapshot.diagnostics,
        field="snapshot.diagnostics",
    )
    if not snapshot.ready and not snapshot.diagnostics:
        message = "blocked snapshot requires actionable diagnostics"
        raise ValueError(message)
    _validate_fixed_plan_obligations(snapshot)


def _validate_ci_artifact_url(artifact: CiArtifact) -> None:
    _require_nonempty_string(
        artifact.artifact_url,
        field="artifact.artifact_url",
    )
    artifact_url = urlsplit(artifact.artifact_url)
    expected_url_path = (
        f"/{artifact.candidate.repository}/actions/runs/"
        f"{artifact.workflow_run_id}/artifacts/{artifact.artifact_id}"
    )
    if (
        artifact_url.scheme != "https"
        or artifact_url.netloc != "github.com"
        or artifact_url.path != expected_url_path
        or artifact_url.query
        or artifact_url.fragment
    ):
        message = (
            "CI artifact URL does not bind repository, run, and artifact ID"
        )
        raise ValueError(message)


def _validate_ci_artifact(artifact: CiArtifact) -> None:
    _require_exact_type(artifact, CiArtifact, field="artifact")
    _validate_ci_candidate(artifact.candidate)
    _require_exact_type(artifact.producer, str, field="artifact.producer")
    if artifact.producer != "npm-artifact-build":
        message = "CI artifact producer must be npm-artifact-build"
        raise ValueError(message)
    _require_positive_integer(
        artifact.workflow_run_id,
        field="artifact.workflow_run_id",
    )
    _require_positive_integer(
        artifact.run_attempt,
        field="artifact.run_attempt",
    )
    if (
        artifact.workflow_run_id != artifact.candidate.workflow_run_id
        or artifact.run_attempt != artifact.candidate.run_attempt
    ):
        message = "CI artifact workflow run or attempt is not current"
        raise ValueError(message)
    _require_nonempty_string(
        artifact.output_id,
        field="artifact.output_id",
    )
    _require_nonempty_string(
        artifact.logical_role,
        field="artifact.logical_role",
    )
    _require_nonempty_string(
        artifact.media_kind,
        field="artifact.media_kind",
    )
    _require_positive_integer(
        artifact.artifact_id,
        field="artifact.artifact_id",
    )
    _require_nonempty_string(
        artifact.artifact_name,
        field="artifact.artifact_name",
    )
    expected_name = (
        f"wdv3-{artifact.workflow_run_id}-{artifact.run_attempt}-"
        f"{artifact.output_id}.tgz"
    )
    if artifact.artifact_name != expected_name:
        message = (
            "CI artifact name does not bind current run, attempt, and output ID"
        )
        raise ValueError(message)
    _validate_ci_artifact_url(artifact)
    _require_digest(
        artifact.transport_digest,
        field="artifact.transport_digest",
    )
    _require_nonempty_string(
        artifact.tarball_basename,
        field="artifact.tarball_basename",
    )
    if (
        "/" in artifact.tarball_basename
        or "\\" in artifact.tarball_basename
        or not artifact.tarball_basename.endswith(".tgz")
    ):
        message = "CI artifact tarball basename is invalid"
        raise ValueError(message)
    _require_digest(artifact.content_sha256, field="artifact.content_sha256")
    if artifact.transport_digest != artifact.content_sha256:
        message = "raw CI artifact upload digest must equal content SHA-256"
        raise ValueError(message)
    _require_sha512_digest(
        artifact.content_sha512,
        field="artifact.content_sha512",
    )
    _require_positive_integer(artifact.byte_size, field="artifact.byte_size")
    _require_digest(
        artifact.provenance_digest,
        field="artifact.provenance_digest",
    )
    _require_string_tuple(
        artifact.entries,
        field="artifact.entries",
        unique=True,
    )
    if (
        not artifact.entries
        or tuple(sorted(artifact.entries)) != artifact.entries
    ):
        message = "CI artifact entries must be nonempty and canonically sorted"
        raise ValueError(message)
    for entry in artifact.entries:
        if (
            entry.startswith("/")
            or "\\" in entry
            or ".." in entry.split("/")
            or entry.endswith("/")
        ):
            message = "CI artifact entries must be safe relative file paths"
            raise ValueError(message)
    _require_string_pairs(
        artifact.lifecycle_scripts,
        field="artifact.lifecycle_scripts",
    )


def _validate_ci_evidence(  # noqa: C901, PLR0912, PLR0915
    evidence: CiEvidence,
) -> None:
    _require_exact_type(evidence, CiEvidence, field="evidence")
    _require_nonempty_string(evidence.evidence_id, field="evidence.evidence_id")
    _require_digest(evidence.plan_digest, field="evidence.plan_digest")
    _validate_ci_candidate(evidence.candidate)
    _validate_ci_obligation(evidence.obligation)
    if not evidence.obligation.selected or not evidence.obligation.required:
        message = "Evidence requires a selected required obligation"
        raise ValueError(message)
    if evidence.evidence_id != evidence.obligation.expected_evidence_id:
        message = "Evidence identity does not match the planned obligation"
        raise ValueError(message)
    _require_nonempty_string(evidence.producer, field="evidence.producer")
    if evidence.producer != evidence.obligation.lane_id:
        message = "Evidence producer does not match its static lane"
        raise ValueError(message)
    _require_positive_integer(
        evidence.workflow_run_id,
        field="evidence.workflow_run_id",
    )
    _require_positive_integer(
        evidence.run_attempt,
        field="evidence.run_attempt",
    )
    if (
        evidence.workflow_run_id != evidence.candidate.workflow_run_id
        or evidence.run_attempt != evidence.candidate.run_attempt
    ):
        message = "Evidence workflow run or attempt is not current"
        raise ValueError(message)
    _require_nonempty_string(evidence.runner, field="evidence.runner")
    _require_choice(
        evidence.raw_outcome,
        _RAW_TO_NORMALIZED_OUTCOME,
        field="evidence.raw_outcome",
    )
    _require_choice(
        evidence.normalized_outcome,
        _EVIDENCE_OUTCOMES,
        field="evidence.normalized_outcome",
    )
    expected_outcome = _RAW_TO_NORMALIZED_OUTCOME[evidence.raw_outcome]
    if evidence.normalized_outcome != expected_outcome:
        message = "Evidence normalized outcome does not match raw mechanics"
        raise ValueError(message)
    _require_digest_tuple(
        evidence.output_digests,
        field="evidence.output_digests",
    )
    _require_exact_type(evidence.artifacts, tuple, field="evidence.artifacts")
    artifact_ids: set[int] = set()
    artifact_digests: set[str] = set()
    for artifact in evidence.artifacts:
        _validate_ci_artifact(artifact)
        digest = ci_artifact_digest(artifact)
        if artifact.artifact_id in artifact_ids or digest in artifact_digests:
            message = "Evidence contains duplicate CI artifacts"
            raise ValueError(message)
        artifact_ids.add(artifact.artifact_id)
        artifact_digests.add(digest)
        if (
            artifact.candidate != evidence.candidate
            or artifact.producer != evidence.producer
            or artifact.workflow_run_id != evidence.workflow_run_id
            or artifact.run_attempt != evidence.run_attempt
        ):
            message = (
                "Evidence artifact does not match its current lane binding"
            )
            raise ValueError(message)
    if (
        evidence.obligation.lane_id != "npm-artifact-build"
        and evidence.artifacts
    ):
        message = "non-artifact Evidence cannot claim CI artifacts"
        raise ValueError(message)
    if evidence.normalized_outcome == "satisfied":
        if not evidence.output_digests:
            message = "satisfied Evidence requires an output digest"
            raise ValueError(message)
        if (
            evidence.obligation.lane_id == "npm-artifact-build"
            and len(evidence.artifacts) != 1
        ):
            message = (
                "satisfied npm artifact Evidence requires exactly one "
                "complete CI artifact record and output provenance"
            )
            raise ValueError(message)
    elif evidence.artifacts:
        message = "unsatisfied Evidence cannot claim CI artifacts"
        raise ValueError(message)
    _require_string_tuple(
        evidence.diagnostics,
        field="evidence.diagnostics",
    )


def _validate_ci_lane_result(result: CiLaneResult) -> None:
    _require_exact_type(result, CiLaneResult, field="lane_result")
    _require_digest(result.plan_digest, field="lane_result.plan_digest")
    _validate_ci_candidate(result.candidate)
    _require_choice(
        result.lane_id,
        _CI_LANE_ID_SET,
        field="lane_result.lane_id",
    )
    _require_nonempty_string(result.producer, field="lane_result.producer")
    if result.producer != result.lane_id:
        message = "lane-result producer does not match its static lane"
        raise ValueError(message)
    _require_positive_integer(
        result.workflow_run_id,
        field="lane_result.workflow_run_id",
    )
    _require_positive_integer(
        result.run_attempt,
        field="lane_result.run_attempt",
    )
    if (
        result.workflow_run_id != result.candidate.workflow_run_id
        or result.run_attempt != result.candidate.run_attempt
    ):
        message = "lane result workflow run or attempt is not current"
        raise ValueError(message)
    _require_choice(
        result.disposition,
        _LANE_RESULT_OUTCOMES,
        field="lane_result.disposition",
    )
    if result.disposition == "empty":
        if result.evidence is not None:
            message = "empty lane result must not carry Evidence"
            raise ValueError(message)
        return
    if type(result.evidence) is not CiEvidence:
        message = "selected lane result must carry exact CiEvidence"
        raise TypeError(message)
    _validate_ci_evidence(result.evidence)
    if (
        result.evidence.plan_digest != result.plan_digest
        or result.evidence.candidate != result.candidate
        or result.evidence.obligation.lane_id != result.lane_id
        or result.evidence.producer != result.producer
        or result.evidence.workflow_run_id != result.workflow_run_id
        or result.evidence.run_attempt != result.run_attempt
    ):
        message = "lane result Evidence does not match its exact lane binding"
        raise ValueError(message)
    if result.disposition != result.evidence.normalized_outcome:
        message = "lane result disposition does not match Evidence"
        raise ValueError(message)


def _disposition_explanation(
    obligation: CiObligation,
    outcome: str,
) -> str:
    if not obligation.selected:
        return f"{obligation.lane_id} was not selected"
    if outcome == "incomplete":
        return f"{obligation.lane_id} selected work did not emit Evidence"
    return f"{obligation.lane_id} {outcome}"


def _terminal_result(
    dispositions: tuple[CiObligationDisposition, ...],
) -> str:
    selected_outcomes = tuple(
        disposition.outcome
        for disposition in dispositions
        if disposition.obligation.selected
    )
    if "incomplete" in selected_outcomes:
        return "incomplete"
    if selected_outcomes and all(
        outcome == "satisfied" for outcome in selected_outcomes
    ):
        return "success"
    return "failure"


def _decision_explanation(
    dispositions: tuple[CiObligationDisposition, ...],
    terminal_result: str,
) -> str:
    if terminal_result == "success":
        return "all selected CI slice obligations were satisfied"
    incomplete = tuple(
        disposition.obligation.lane_id
        for disposition in dispositions
        if disposition.obligation.selected
        and disposition.outcome == "incomplete"
    )
    if incomplete:
        return "selected CI slice obligations are incomplete: " + ", ".join(
            incomplete
        )
    failed = tuple(
        f"{disposition.obligation.lane_id}={disposition.outcome}"
        for disposition in dispositions
        if disposition.obligation.selected
        and disposition.outcome != "satisfied"
    )
    if failed:
        return "selected CI slice obligations were not satisfied: " + ", ".join(
            failed
        )
    return "CI slice Plan was not ready for required work"


def _is_broad_pr_slo_change(path: str) -> bool:
    return (
        path in _SLO_BROAD_CONTROL_PATHS
        or path in _SLO_ROOT_TOOLCHAIN_PATHS
        or path.startswith(_SLO_GOVERNANCE_PREFIXES)
        or path.startswith(_SLO_BROAD_CONTROL_PREFIXES)
    )


def derive_ci_pr_slo(
    candidate: CiCandidate,
    changed_paths: tuple[str, ...],
    elapsed_seconds: int,
    supersession_state: str,
) -> tuple[str, str]:
    """Derive the closed ordinary-PR SLO result and cohort reason."""
    if candidate.event_kind != "pull_request":
        if supersession_state != "not-applicable":
            message = "non-PR SLO requires not-applicable supersession state"
            raise ValueError(message)
        return "not-applicable", "not-pull-request"
    if supersession_state not in {
        "not-superseded",
        "superseded",
        "unsupported",
    }:
        message = "pull-request SLO has an invalid supersession state"
        raise ValueError(message)
    if supersession_state == "superseded":
        return "excluded", "superseded-candidate"
    if any(_is_broad_pr_slo_change(path) for path in changed_paths):
        return "excluded", "broad-change"
    if supersession_state == "unsupported":
        return "not-applicable", "supersession-unavailable"
    if elapsed_seconds <= _PR_SLO_SECONDS:
        return "met", "ordinary-pull-request"
    return "missed", "ordinary-pull-request"


def derive_ci_failure(
    dispositions: tuple[CiObligationDisposition, ...],
) -> tuple[str, str]:
    """Derive the closed CI failure class and allowed next action."""
    selected = tuple(
        item.outcome for item in dispositions if item.obligation.selected
    )
    if not selected:
        return "incomplete-model-plan", "fix-model-plan-and-rerun"
    if "incomplete" in selected or any(
        outcome in {"skipped", "timed-out", "unknown"} for outcome in selected
    ):
        return "incomplete-qualification", "rerun-candidate"
    if "failed" in selected:
        return "quality-failure", "fix-quality-failure-and-rerun"
    return "none", "none"


def ci_slice_summary_text(  # noqa: PLR0913
    *,
    candidate: CiCandidate,
    repository_model_digest: str,
    plan_digest: str,
    scope_mode: str,
    changed_paths: tuple[str, ...],
    selected_project_nodes: tuple[str, ...],
    selected_release_units: tuple[str, ...],
    selected_variants: tuple[str, ...],
    selected_outputs: tuple[CiOutputIdentity, ...],
    plan_diagnostics: tuple[str, ...],
    dispositions: tuple[CiObligationDisposition, ...],
    evidence_digests: tuple[str, ...],
    artifact_digests: tuple[str, ...],
    explanation: str,
    terminal_result: str,
    failure_class: str,
    next_action: str,
    elapsed_seconds: int,
    supersession_state: str,
    supersession_reason: str,
    pr_slo: str,
    pr_slo_reason: str,
) -> str:
    """Render the deterministic mode-specific CI human summary."""

    def joined(values: tuple[str, ...]) -> str:
        return ",".join(values) if values else "none"

    comparison = (
        f"{candidate.base_sha}..{candidate.head_sha}"
        if candidate.event_kind == "pull_request"
        else "not-applicable"
    )
    label = (
        "non-authoritative shadow result"
        if candidate.event_kind == "pull_request"
        else "non-authoritative slice-validation result"
    )
    obligation_summary = tuple(
        f"{item.obligation.lane_id}={item.outcome}" for item in dispositions
    )
    output_summary = tuple(":".join(output) for output in selected_outputs)
    return (
        f"{label}: candidate-digest={ci_candidate_digest(candidate)}; "
        f"target={candidate.target}; comparison={comparison}; "
        f"mode={scope_mode}; changed-paths={joined(changed_paths)}; "
        f"project-nodes={joined(selected_project_nodes)}; "
        f"release-units={joined(selected_release_units)}; "
        f"variants={joined(selected_variants)}; "
        f"outputs={joined(output_summary)}; "
        f"repository-model-digest={repository_model_digest}; "
        f"plan-digest={plan_digest}; "
        f"plan-diagnostics={joined(plan_diagnostics)}; "
        f"obligations={joined(obligation_summary)}; "
        f"evidence-digests={joined(evidence_digests)}; "
        f"artifact-digests={joined(artifact_digests)}; "
        f"explanation={explanation}; "
        f"terminal-result={terminal_result}; "
        f"failure-class={failure_class}; next-action={next_action}; "
        f"elapsed={elapsed_seconds}s; "
        f"supersession-state={supersession_state}; "
        f"supersession-reason={supersession_reason}; "
        f"pr-12-minute-slo={pr_slo}; "
        f"pr-slo-reason={pr_slo_reason}"
    )


def _validate_ci_obligation_disposition(
    disposition: CiObligationDisposition,
) -> None:
    _require_exact_type(
        disposition,
        CiObligationDisposition,
        field="obligation_disposition",
    )
    _validate_ci_obligation(disposition.obligation)
    _require_choice(
        disposition.outcome,
        _FINAL_DISPOSITION_OUTCOMES,
        field="obligation_disposition.outcome",
    )
    _require_digest_tuple(
        disposition.evidence_digests,
        field="obligation_disposition.evidence_digests",
    )
    _require_nonempty_string(
        disposition.explanation,
        field="obligation_disposition.explanation",
    )
    evidence_count = len(disposition.evidence_digests)
    if disposition.outcome in _EVIDENCE_OUTCOMES and evidence_count != 1:
        message = "mechanical disposition requires exactly one Evidence digest"
        raise ValueError(message)
    if disposition.outcome in {"empty", "incomplete"} and evidence_count:
        message = f"{disposition.outcome} disposition has no Evidence"
        raise ValueError(message)
    if disposition.obligation.selected:
        if disposition.outcome == "empty":
            message = "selected obligation cannot disappear as empty"
            raise ValueError(message)
    elif disposition.outcome != "empty":
        message = "unselected obligation must have empty disposition"
        raise ValueError(message)
    expected_explanation = _disposition_explanation(
        disposition.obligation,
        disposition.outcome,
    )
    if disposition.explanation != expected_explanation:
        message = "obligation disposition explanation is not deterministic"
        raise ValueError(message)


def _validate_ci_slice_summary(summary: CiSliceSummary) -> None:
    _require_exact_type(summary, CiSliceSummary, field="slice_summary")
    _require_exact_type(summary.authority, str, field="slice_summary.authority")
    if summary.authority != "non-authoritative":
        message = "CI Slice Summary authority must be non-authoritative"
        raise ValueError(message)
    _require_choice(
        summary.terminal_result,
        _TERMINAL_RESULTS,
        field="slice_summary.terminal_result",
    )
    _require_nonempty_string(summary.text, field="slice_summary.text")
    if "non-authoritative" not in summary.text:
        message = "CI Slice Summary text must state non-authoritative status"
        raise ValueError(message)


def _validate_ci_slice_decision(  # noqa: C901, PLR0912, PLR0915
    decision: CiSliceDecision,
) -> None:
    _require_exact_type(decision, CiSliceDecision, field="slice_decision")
    _require_digest(decision.plan_digest, field="slice_decision.plan_digest")
    _require_digest(
        decision.repository_model_digest,
        field="slice_decision.repository_model_digest",
    )
    _validate_ci_candidate(decision.candidate)
    _require_exact_type(decision.producer, str, field="slice_decision.producer")
    if decision.producer != "required-finalizer":
        message = "CI Slice Decision producer must be required-finalizer"
        raise ValueError(message)
    _require_positive_integer(
        decision.workflow_run_id,
        field="slice_decision.workflow_run_id",
    )
    _require_positive_integer(
        decision.run_attempt,
        field="slice_decision.run_attempt",
    )
    if (
        decision.workflow_run_id != decision.candidate.workflow_run_id
        or decision.run_attempt != decision.candidate.run_attempt
    ):
        message = "CI Slice Decision workflow run or attempt is not current"
        raise ValueError(message)
    _require_choice(
        decision.scope_mode,
        set(_EVENT_SCOPE_MODES.values()),
        field="slice_decision.scope_mode",
    )
    expected_scope_mode = _EVENT_SCOPE_MODES[decision.candidate.event_kind]
    if decision.scope_mode != expected_scope_mode:
        message = "Slice Decision scope mode does not match candidate event"
        raise ValueError(message)
    _require_string_tuple(
        decision.changed_paths,
        field="slice_decision.changed_paths",
        unique=True,
    )
    if decision.scope_mode == "slice-validation" and decision.changed_paths:
        message = "slice-validation Decision has no changed-path range"
        raise ValueError(message)
    _require_string_tuple(
        decision.selected_project_nodes,
        field="slice_decision.selected_project_nodes",
        unique=True,
    )
    _require_string_tuple(
        decision.selected_release_units,
        field="slice_decision.selected_release_units",
        unique=True,
    )
    _require_string_tuple(
        decision.selected_variants,
        field="slice_decision.selected_variants",
        unique=True,
    )
    _require_output_identity_tuple(
        decision.selected_outputs,
        field="slice_decision.selected_outputs",
    )
    _require_string_tuple(
        decision.plan_diagnostics,
        field="slice_decision.plan_diagnostics",
    )
    _require_exact_type(
        decision.obligation_dispositions,
        tuple,
        field="slice_decision.obligation_dispositions",
    )
    for disposition in decision.obligation_dispositions:
        _validate_ci_obligation_disposition(disposition)
    obligations = [
        disposition.obligation
        for disposition in decision.obligation_dispositions
    ]
    if len({item.obligation_id for item in obligations}) != len(obligations):
        message = "Slice Decision contains duplicate obligation IDs"
        raise ValueError(message)
    lane_ids = [item.lane_id for item in obligations]
    if len(set(lane_ids)) != len(lane_ids):
        message = "Slice Decision contains duplicate lane IDs"
        raise ValueError(message)
    if set(lane_ids) != _CI_LANE_ID_SET:
        message = "Slice Decision must close all four static lanes"
        raise ValueError(message)
    selected_lanes = tuple(
        item.lane_id for item in obligations if item.selected
    )
    if not selected_lanes and not decision.plan_diagnostics:
        message = "blocked Slice Decision requires Plan diagnostics"
        raise ValueError(message)
    scope = (
        decision.selected_project_nodes,
        decision.selected_release_units,
        decision.selected_variants,
        decision.selected_outputs,
    )
    empty_scope = ((), (), (), ())
    first_slice_scope = (
        _FIRST_SLICE_PROJECT_NODES,
        _FIRST_SLICE_RELEASE_UNITS,
        _FIRST_SLICE_VARIANTS,
        _FIRST_SLICE_OUTPUTS,
    )
    if not selected_lanes:
        if scope != empty_scope:
            message = "blocked Slice Decision must carry empty selected scope"
            raise ValueError(message)
    elif decision.scope_mode == "slice-validation":
        if selected_lanes != CI_LANE_IDS or scope != first_slice_scope:
            message = (
                "slice-validation Decision must carry complete first-slice "
                "scope"
            )
            raise ValueError(message)
    elif selected_lanes == ("root-hk",):
        if scope != empty_scope or not all(
            is_repository_only_path(path)
            and not _is_first_slice_affecting_path(path)
            for path in decision.changed_paths
        ):
            message = (
                "root-hk-only Decision requires repository-only changed paths"
            )
            raise ValueError(message)
    elif selected_lanes == CI_LANE_IDS:
        if (
            scope != first_slice_scope
            or not any(
                _is_first_slice_affecting_path(path)
                for path in decision.changed_paths
            )
            or not all(
                _is_first_slice_affecting_path(path)
                or is_repository_only_path(path)
                for path in decision.changed_paths
            )
        ):
            message = (
                "complete first-slice Decision requires classified affected "
                "paths"
            )
            raise ValueError(message)
    else:
        message = "Slice Decision has an invalid partial selected scope"
        raise ValueError(message)
    _require_digest_tuple(
        decision.admitted_evidence_digests,
        field="slice_decision.admitted_evidence_digests",
    )
    expected_evidence_digests = tuple(
        digest
        for disposition in decision.obligation_dispositions
        for digest in disposition.evidence_digests
    )
    if decision.admitted_evidence_digests != expected_evidence_digests:
        message = "Slice Decision admitted Evidence digests are not exact"
        raise ValueError(message)
    _require_digest_tuple(
        decision.admitted_artifact_digests,
        field="slice_decision.admitted_artifact_digests",
    )
    _require_nonempty_string(
        decision.explanation,
        field="slice_decision.explanation",
    )
    _require_choice(
        decision.terminal_result,
        _TERMINAL_RESULTS,
        field="slice_decision.terminal_result",
    )
    expected_terminal = _terminal_result(decision.obligation_dispositions)
    if decision.terminal_result != expected_terminal:
        message = "Slice Decision terminal result contradicts dispositions"
        raise ValueError(message)
    expected_explanation = _decision_explanation(
        decision.obligation_dispositions,
        expected_terminal,
    )
    if decision.explanation != expected_explanation:
        message = "Slice Decision explanation is not deterministic"
        raise ValueError(message)
    _require_choice(
        decision.failure_class,
        _FAILURE_CLASSES,
        field="slice_decision.failure_class",
    )
    _require_choice(
        decision.next_action,
        _NEXT_ACTIONS,
        field="slice_decision.next_action",
    )
    expected_failure_class, expected_next_action = derive_ci_failure(
        decision.obligation_dispositions
    )
    if (
        decision.failure_class != expected_failure_class
        or decision.next_action != expected_next_action
    ):
        message = "Slice Decision failure class or next action is not exact"
        raise ValueError(message)
    _require_exact_type(
        decision.authority,
        str,
        field="slice_decision.authority",
    )
    if decision.authority != "non-authoritative":
        message = "CI Slice Decision authority must be non-authoritative"
        raise ValueError(message)
    _require_nonnegative_integer(
        decision.elapsed_seconds,
        field="slice_decision.elapsed_seconds",
    )
    _require_choice(
        decision.supersession_state,
        _SUPERSESSION_STATES,
        field="slice_decision.supersession_state",
    )
    _require_choice(
        decision.supersession_reason,
        set(_SUPERSESSION_REASONS.values()),
        field="slice_decision.supersession_reason",
    )
    expected_supersession_reason = _SUPERSESSION_REASONS[
        decision.supersession_state
    ]
    if decision.supersession_reason != expected_supersession_reason:
        message = "CI Slice Decision supersession reason is not deterministic"
        raise ValueError(message)
    _require_choice(
        decision.pr_slo,
        _PR_SLO_RESULTS,
        field="slice_decision.pr_slo",
    )
    _require_choice(
        decision.pr_slo_reason,
        _PR_SLO_REASONS,
        field="slice_decision.pr_slo_reason",
    )
    expected_pr_slo, expected_pr_slo_reason = derive_ci_pr_slo(
        decision.candidate,
        decision.changed_paths,
        decision.elapsed_seconds,
        decision.supersession_state,
    )
    if (
        decision.pr_slo != expected_pr_slo
        or decision.pr_slo_reason != expected_pr_slo_reason
    ):
        message = "CI Slice Decision PR SLO result is not deterministic"
        raise ValueError(message)
    _validate_ci_slice_summary(decision.summary)
    if (
        decision.summary.authority != decision.authority
        or decision.summary.terminal_result != decision.terminal_result
    ):
        message = "CI Slice Summary does not match its Decision"
        raise ValueError(message)
    expected_summary = ci_slice_summary_text(
        candidate=decision.candidate,
        repository_model_digest=decision.repository_model_digest,
        plan_digest=decision.plan_digest,
        scope_mode=decision.scope_mode,
        changed_paths=decision.changed_paths,
        selected_project_nodes=decision.selected_project_nodes,
        selected_release_units=decision.selected_release_units,
        selected_variants=decision.selected_variants,
        selected_outputs=decision.selected_outputs,
        plan_diagnostics=decision.plan_diagnostics,
        dispositions=decision.obligation_dispositions,
        evidence_digests=decision.admitted_evidence_digests,
        artifact_digests=decision.admitted_artifact_digests,
        explanation=decision.explanation,
        terminal_result=decision.terminal_result,
        failure_class=decision.failure_class,
        next_action=decision.next_action,
        elapsed_seconds=decision.elapsed_seconds,
        supersession_state=decision.supersession_state,
        supersession_reason=decision.supersession_reason,
        pr_slo=decision.pr_slo,
        pr_slo_reason=decision.pr_slo_reason,
    )
    if decision.summary.text != expected_summary:
        message = "CI Slice Summary text is not deterministic"
        raise ValueError(message)


def _closed(
    document: Mapping[str, JsonValue],
    *,
    required: frozenset[str],
    context: str,
) -> None:
    missing = required - document.keys()
    if missing:
        message = f"{context} missing required field: {sorted(missing)[0]}"
        raise ValueError(message)
    unknown = document.keys() - required
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
    if type(value) is not str:
        message = f"{context} must be a string"
        raise TypeError(message)
    return value


def _integer(value: JsonValue, *, context: str) -> int:
    if type(value) is not int:
        message = f"{context} must be an integer"
        raise TypeError(message)
    return value


def _boolean(value: JsonValue, *, context: str) -> bool:
    if type(value) is not bool:
        message = f"{context} must be a Boolean"
        raise TypeError(message)
    return value


def _nullable_string(value: JsonValue, *, context: str) -> str | None:
    if value is None:
        return None
    return _string(value, context=context)


def _string_tuple(value: JsonValue, *, context: str) -> tuple[str, ...]:
    return tuple(
        _string(item, context=f"{context}[{index}]")
        for index, item in enumerate(_array(value, context=context))
    )


def _string_pair_tuple(
    value: JsonValue,
    *,
    context: str,
) -> tuple[tuple[str, str], ...]:
    pairs: list[tuple[str, str]] = []
    for index, item in enumerate(_array(value, context=context)):
        pair = _array(item, context=f"{context}[{index}]")
        if len(pair) != _PAIR_FIELD_COUNT:
            message = f"{context}[{index}] must contain exactly two strings"
            raise ValueError(message)
        pairs.append(
            (
                _string(pair[0], context=f"{context}[{index}][0]"),
                _string(pair[1], context=f"{context}[{index}][1]"),
            )
        )
    return tuple(pairs)


def _schema(
    document: Mapping[str, JsonValue],
    expected: str,
    context: str,
) -> None:
    actual = _string(document["schema"], context=f"{context}.schema")
    if actual != expected:
        message = f"{context} has the wrong schema"
        raise ValueError(message)


def _output_identity_document(
    output: CiOutputIdentity,
) -> dict[str, JsonValue]:
    output_id, logical_role, media_kind = output
    return {
        "output-id": output_id,
        "logical-role": logical_role,
        "media-kind": media_kind,
    }


def _output_identity_tuple(
    value: JsonValue,
    *,
    context: str,
) -> tuple[CiOutputIdentity, ...]:
    outputs: list[CiOutputIdentity] = []
    for index, item in enumerate(_array(value, context=context)):
        item_context = f"{context}[{index}]"
        document = _object(item, context=item_context)
        _closed(
            document,
            required=frozenset({"output-id", "logical-role", "media-kind"}),
            context=item_context,
        )
        outputs.append(
            (
                _string(
                    document["output-id"],
                    context=f"{item_context}.output-id",
                ),
                _string(
                    document["logical-role"],
                    context=f"{item_context}.logical-role",
                ),
                _string(
                    document["media-kind"],
                    context=f"{item_context}.media-kind",
                ),
            )
        )
    return tuple(outputs)


_CANDIDATE_FIELDS = frozenset(
    {
        "schema",
        "event-kind",
        "purpose",
        "repository",
        "workflow-path",
        "workflow-sha",
        "request-id",
        "producer",
        "workflow-run-id",
        "run-attempt",
        "selected-ref",
        "target",
        "base-sha",
        "head-sha",
        "tested-merge-sha",
    }
)
_OBLIGATION_FIELDS = frozenset(
    {
        "schema",
        "obligation-id",
        "lane-id",
        "request-digest",
        "definition-id",
        "definition-digest",
        "prerequisites",
        "selected",
        "required",
        "expected-evidence-id",
    }
)
_SNAPSHOT_FIELDS = frozenset(
    {
        "schema",
        "candidate",
        "producer",
        "workflow-run-id",
        "run-attempt",
        "repository-model-digest",
        "root-hk-definition",
        "root-hk-definition-digest",
        "scope-mode",
        "changed-paths",
        "selected-project-nodes",
        "selected-release-units",
        "selected-variants",
        "selected-outputs",
        "obligations",
        "expected-evidence-ids",
        "ready",
        "diagnostics",
    }
)
_ARTIFACT_FIELDS = frozenset(
    {
        "schema",
        "candidate",
        "producer",
        "workflow-run-id",
        "run-attempt",
        "output-id",
        "logical-role",
        "media-kind",
        "artifact-id",
        "artifact-name",
        "artifact-url",
        "transport-digest",
        "tarball-basename",
        "content-sha256",
        "content-sha512",
        "byte-size",
        "provenance-digest",
        "entries",
        "lifecycle-scripts",
    }
)
_EVIDENCE_FIELDS = frozenset(
    {
        "schema",
        "evidence-id",
        "plan-digest",
        "candidate",
        "obligation",
        "producer",
        "workflow-run-id",
        "run-attempt",
        "runner",
        "raw-outcome",
        "output-digests",
        "artifacts",
        "normalized-outcome",
        "diagnostics",
    }
)
_LANE_RESULT_FIELDS = frozenset(
    {
        "schema",
        "plan-digest",
        "candidate",
        "lane-id",
        "producer",
        "workflow-run-id",
        "run-attempt",
        "disposition",
        "evidence",
    }
)
_OBLIGATION_DISPOSITION_FIELDS = frozenset(
    {
        "schema",
        "obligation",
        "outcome",
        "evidence-digests",
        "explanation",
    }
)
_SUMMARY_FIELDS = frozenset({"schema", "authority", "terminal-result", "text"})
_DECISION_FIELDS = frozenset(
    {
        "schema",
        "plan-digest",
        "repository-model-digest",
        "candidate",
        "producer",
        "workflow-run-id",
        "run-attempt",
        "scope-mode",
        "changed-paths",
        "selected-project-nodes",
        "selected-release-units",
        "selected-variants",
        "selected-outputs",
        "plan-diagnostics",
        "obligation-dispositions",
        "admitted-evidence-digests",
        "admitted-artifact-digests",
        "explanation",
        "terminal-result",
        "failure-class",
        "next-action",
        "authority",
        "elapsed-seconds",
        "supersession-state",
        "supersession-reason",
        "pr-slo",
        "pr-slo-reason",
        "summary",
    }
)


def _ci_candidate_from_document(
    value: JsonValue,
    *,
    context: str,
) -> CiCandidate:
    document = _object(value, context=context)
    _closed(document, required=_CANDIDATE_FIELDS, context=context)
    _schema(document, CI_CANDIDATE_SCHEMA, context)
    return CiCandidate(
        event_kind=_string(
            document["event-kind"],
            context=f"{context}.event-kind",
        ),
        purpose=_string(document["purpose"], context=f"{context}.purpose"),
        repository=_string(
            document["repository"],
            context=f"{context}.repository",
        ),
        workflow_path=_string(
            document["workflow-path"],
            context=f"{context}.workflow-path",
        ),
        workflow_sha=_string(
            document["workflow-sha"],
            context=f"{context}.workflow-sha",
        ),
        request_id=_string(
            document["request-id"],
            context=f"{context}.request-id",
        ),
        producer=_string(
            document["producer"],
            context=f"{context}.producer",
        ),
        workflow_run_id=_integer(
            document["workflow-run-id"],
            context=f"{context}.workflow-run-id",
        ),
        run_attempt=_integer(
            document["run-attempt"],
            context=f"{context}.run-attempt",
        ),
        selected_ref=_string(
            document["selected-ref"],
            context=f"{context}.selected-ref",
        ),
        target=_string(document["target"], context=f"{context}.target"),
        base_sha=_nullable_string(
            document["base-sha"],
            context=f"{context}.base-sha",
        ),
        head_sha=_nullable_string(
            document["head-sha"],
            context=f"{context}.head-sha",
        ),
        tested_merge_sha=_nullable_string(
            document["tested-merge-sha"],
            context=f"{context}.tested-merge-sha",
        ),
    )


def _ci_obligation_from_document(
    value: JsonValue,
    *,
    context: str,
) -> CiObligation:
    document = _object(value, context=context)
    _closed(document, required=_OBLIGATION_FIELDS, context=context)
    _schema(document, CI_OBLIGATION_SCHEMA, context)
    return CiObligation(
        obligation_id=_string(
            document["obligation-id"],
            context=f"{context}.obligation-id",
        ),
        lane_id=_string(
            document["lane-id"],
            context=f"{context}.lane-id",
        ),
        request_digest=_string(
            document["request-digest"],
            context=f"{context}.request-digest",
        ),
        definition_id=_string(
            document["definition-id"],
            context=f"{context}.definition-id",
        ),
        definition_digest=_string(
            document["definition-digest"],
            context=f"{context}.definition-digest",
        ),
        prerequisites=_string_tuple(
            document["prerequisites"],
            context=f"{context}.prerequisites",
        ),
        selected=_boolean(
            document["selected"],
            context=f"{context}.selected",
        ),
        required=_boolean(
            document["required"],
            context=f"{context}.required",
        ),
        expected_evidence_id=_string(
            document["expected-evidence-id"],
            context=f"{context}.expected-evidence-id",
        ),
    )


def _ci_qualification_snapshot_from_document(
    value: JsonValue,
    *,
    context: str,
) -> CiQualificationSnapshot:
    document = _object(value, context=context)
    _closed(document, required=_SNAPSHOT_FIELDS, context=context)
    _schema(document, CI_QUALIFICATION_SNAPSHOT_SCHEMA, context)
    obligations = tuple(
        _ci_obligation_from_document(
            item,
            context=f"{context}.obligations[{index}]",
        )
        for index, item in enumerate(
            _array(
                document["obligations"],
                context=f"{context}.obligations",
            )
        )
    )
    return CiQualificationSnapshot(
        candidate=_ci_candidate_from_document(
            document["candidate"],
            context=f"{context}.candidate",
        ),
        producer=_string(
            document["producer"],
            context=f"{context}.producer",
        ),
        workflow_run_id=_integer(
            document["workflow-run-id"],
            context=f"{context}.workflow-run-id",
        ),
        run_attempt=_integer(
            document["run-attempt"],
            context=f"{context}.run-attempt",
        ),
        repository_model_digest=_string(
            document["repository-model-digest"],
            context=f"{context}.repository-model-digest",
        ),
        root_hk_definition=_string(
            document["root-hk-definition"],
            context=f"{context}.root-hk-definition",
        ),
        root_hk_definition_digest=_string(
            document["root-hk-definition-digest"],
            context=f"{context}.root-hk-definition-digest",
        ),
        scope_mode=_string(
            document["scope-mode"],
            context=f"{context}.scope-mode",
        ),
        changed_paths=_string_tuple(
            document["changed-paths"],
            context=f"{context}.changed-paths",
        ),
        selected_project_nodes=_string_tuple(
            document["selected-project-nodes"],
            context=f"{context}.selected-project-nodes",
        ),
        selected_release_units=_string_tuple(
            document["selected-release-units"],
            context=f"{context}.selected-release-units",
        ),
        selected_variants=_string_tuple(
            document["selected-variants"],
            context=f"{context}.selected-variants",
        ),
        selected_outputs=_output_identity_tuple(
            document["selected-outputs"],
            context=f"{context}.selected-outputs",
        ),
        obligations=obligations,
        expected_evidence_ids=_string_tuple(
            document["expected-evidence-ids"],
            context=f"{context}.expected-evidence-ids",
        ),
        ready=_boolean(document["ready"], context=f"{context}.ready"),
        diagnostics=_string_tuple(
            document["diagnostics"],
            context=f"{context}.diagnostics",
        ),
    )


def _ci_evidence_from_document(
    value: JsonValue,
    *,
    context: str,
) -> CiEvidence:
    document = _object(value, context=context)
    _closed(document, required=_EVIDENCE_FIELDS, context=context)
    _schema(document, CI_EVIDENCE_SCHEMA, context)
    return CiEvidence(
        evidence_id=_string(
            document["evidence-id"],
            context=f"{context}.evidence-id",
        ),
        plan_digest=_string(
            document["plan-digest"],
            context=f"{context}.plan-digest",
        ),
        candidate=_ci_candidate_from_document(
            document["candidate"],
            context=f"{context}.candidate",
        ),
        obligation=_ci_obligation_from_document(
            document["obligation"],
            context=f"{context}.obligation",
        ),
        producer=_string(
            document["producer"],
            context=f"{context}.producer",
        ),
        workflow_run_id=_integer(
            document["workflow-run-id"],
            context=f"{context}.workflow-run-id",
        ),
        run_attempt=_integer(
            document["run-attempt"],
            context=f"{context}.run-attempt",
        ),
        runner=_string(document["runner"], context=f"{context}.runner"),
        raw_outcome=_string(
            document["raw-outcome"],
            context=f"{context}.raw-outcome",
        ),
        output_digests=_string_tuple(
            document["output-digests"],
            context=f"{context}.output-digests",
        ),
        artifacts=tuple(
            _ci_artifact_from_document(
                item,
                context=f"{context}.artifacts[{index}]",
            )
            for index, item in enumerate(
                _array(document["artifacts"], context=f"{context}.artifacts")
            )
        ),
        normalized_outcome=_string(
            document["normalized-outcome"],
            context=f"{context}.normalized-outcome",
        ),
        diagnostics=_string_tuple(
            document["diagnostics"],
            context=f"{context}.diagnostics",
        ),
    )


def _ci_artifact_from_document(
    value: JsonValue,
    *,
    context: str,
) -> CiArtifact:
    document = _object(value, context=context)
    _closed(document, required=_ARTIFACT_FIELDS, context=context)
    _schema(document, CI_ARTIFACT_SCHEMA, context)
    return CiArtifact(
        candidate=_ci_candidate_from_document(
            document["candidate"],
            context=f"{context}.candidate",
        ),
        producer=_string(
            document["producer"],
            context=f"{context}.producer",
        ),
        workflow_run_id=_integer(
            document["workflow-run-id"],
            context=f"{context}.workflow-run-id",
        ),
        run_attempt=_integer(
            document["run-attempt"],
            context=f"{context}.run-attempt",
        ),
        output_id=_string(
            document["output-id"],
            context=f"{context}.output-id",
        ),
        logical_role=_string(
            document["logical-role"],
            context=f"{context}.logical-role",
        ),
        media_kind=_string(
            document["media-kind"],
            context=f"{context}.media-kind",
        ),
        artifact_id=_integer(
            document["artifact-id"],
            context=f"{context}.artifact-id",
        ),
        artifact_name=_string(
            document["artifact-name"],
            context=f"{context}.artifact-name",
        ),
        artifact_url=_string(
            document["artifact-url"],
            context=f"{context}.artifact-url",
        ),
        transport_digest=_string(
            document["transport-digest"],
            context=f"{context}.transport-digest",
        ),
        tarball_basename=_string(
            document["tarball-basename"],
            context=f"{context}.tarball-basename",
        ),
        content_sha256=_string(
            document["content-sha256"],
            context=f"{context}.content-sha256",
        ),
        content_sha512=_string(
            document["content-sha512"],
            context=f"{context}.content-sha512",
        ),
        byte_size=_integer(
            document["byte-size"],
            context=f"{context}.byte-size",
        ),
        provenance_digest=_string(
            document["provenance-digest"],
            context=f"{context}.provenance-digest",
        ),
        entries=_string_tuple(
            document["entries"],
            context=f"{context}.entries",
        ),
        lifecycle_scripts=_string_pair_tuple(
            document["lifecycle-scripts"],
            context=f"{context}.lifecycle-scripts",
        ),
    )


def _ci_lane_result_from_document(
    value: JsonValue,
    *,
    context: str,
) -> CiLaneResult:
    document = _object(value, context=context)
    _closed(document, required=_LANE_RESULT_FIELDS, context=context)
    _schema(document, CI_LANE_RESULT_SCHEMA, context)
    evidence_value = document["evidence"]
    evidence = (
        None
        if evidence_value is None
        else _ci_evidence_from_document(
            evidence_value,
            context=f"{context}.evidence",
        )
    )
    return CiLaneResult(
        plan_digest=_string(
            document["plan-digest"],
            context=f"{context}.plan-digest",
        ),
        candidate=_ci_candidate_from_document(
            document["candidate"],
            context=f"{context}.candidate",
        ),
        lane_id=_string(
            document["lane-id"],
            context=f"{context}.lane-id",
        ),
        producer=_string(
            document["producer"],
            context=f"{context}.producer",
        ),
        workflow_run_id=_integer(
            document["workflow-run-id"],
            context=f"{context}.workflow-run-id",
        ),
        run_attempt=_integer(
            document["run-attempt"],
            context=f"{context}.run-attempt",
        ),
        disposition=_string(
            document["disposition"],
            context=f"{context}.disposition",
        ),
        evidence=evidence,
    )


def _ci_obligation_disposition_from_document(
    value: JsonValue,
    *,
    context: str,
) -> CiObligationDisposition:
    document = _object(value, context=context)
    _closed(
        document,
        required=_OBLIGATION_DISPOSITION_FIELDS,
        context=context,
    )
    _schema(document, CI_OBLIGATION_DISPOSITION_SCHEMA, context)
    return CiObligationDisposition(
        obligation=_ci_obligation_from_document(
            document["obligation"],
            context=f"{context}.obligation",
        ),
        outcome=_string(
            document["outcome"],
            context=f"{context}.outcome",
        ),
        evidence_digests=_string_tuple(
            document["evidence-digests"],
            context=f"{context}.evidence-digests",
        ),
        explanation=_string(
            document["explanation"],
            context=f"{context}.explanation",
        ),
    )


def _ci_slice_summary_from_document(
    value: JsonValue,
    *,
    context: str,
) -> CiSliceSummary:
    document = _object(value, context=context)
    _closed(document, required=_SUMMARY_FIELDS, context=context)
    _schema(document, CI_SLICE_SUMMARY_SCHEMA, context)
    return CiSliceSummary(
        authority=_string(
            document["authority"],
            context=f"{context}.authority",
        ),
        terminal_result=_string(
            document["terminal-result"],
            context=f"{context}.terminal-result",
        ),
        text=_string(document["text"], context=f"{context}.text"),
    )


def _ci_slice_decision_from_document(
    value: JsonValue,
    *,
    context: str,
) -> CiSliceDecision:
    document = _object(value, context=context)
    _closed(document, required=_DECISION_FIELDS, context=context)
    _schema(document, CI_SLICE_DECISION_SCHEMA, context)
    obligation_dispositions = tuple(
        _ci_obligation_disposition_from_document(
            item,
            context=f"{context}.obligation-dispositions[{index}]",
        )
        for index, item in enumerate(
            _array(
                document["obligation-dispositions"],
                context=f"{context}.obligation-dispositions",
            )
        )
    )
    return CiSliceDecision(
        plan_digest=_string(
            document["plan-digest"],
            context=f"{context}.plan-digest",
        ),
        repository_model_digest=_string(
            document["repository-model-digest"],
            context=f"{context}.repository-model-digest",
        ),
        candidate=_ci_candidate_from_document(
            document["candidate"],
            context=f"{context}.candidate",
        ),
        producer=_string(
            document["producer"],
            context=f"{context}.producer",
        ),
        workflow_run_id=_integer(
            document["workflow-run-id"],
            context=f"{context}.workflow-run-id",
        ),
        run_attempt=_integer(
            document["run-attempt"],
            context=f"{context}.run-attempt",
        ),
        scope_mode=_string(
            document["scope-mode"],
            context=f"{context}.scope-mode",
        ),
        changed_paths=_string_tuple(
            document["changed-paths"],
            context=f"{context}.changed-paths",
        ),
        selected_project_nodes=_string_tuple(
            document["selected-project-nodes"],
            context=f"{context}.selected-project-nodes",
        ),
        selected_release_units=_string_tuple(
            document["selected-release-units"],
            context=f"{context}.selected-release-units",
        ),
        selected_variants=_string_tuple(
            document["selected-variants"],
            context=f"{context}.selected-variants",
        ),
        selected_outputs=_output_identity_tuple(
            document["selected-outputs"],
            context=f"{context}.selected-outputs",
        ),
        plan_diagnostics=_string_tuple(
            document["plan-diagnostics"],
            context=f"{context}.plan-diagnostics",
        ),
        obligation_dispositions=obligation_dispositions,
        admitted_evidence_digests=_string_tuple(
            document["admitted-evidence-digests"],
            context=f"{context}.admitted-evidence-digests",
        ),
        admitted_artifact_digests=_string_tuple(
            document["admitted-artifact-digests"],
            context=f"{context}.admitted-artifact-digests",
        ),
        explanation=_string(
            document["explanation"],
            context=f"{context}.explanation",
        ),
        terminal_result=_string(
            document["terminal-result"],
            context=f"{context}.terminal-result",
        ),
        failure_class=_string(
            document["failure-class"],
            context=f"{context}.failure-class",
        ),
        next_action=_string(
            document["next-action"],
            context=f"{context}.next-action",
        ),
        authority=_string(
            document["authority"],
            context=f"{context}.authority",
        ),
        elapsed_seconds=_integer(
            document["elapsed-seconds"],
            context=f"{context}.elapsed-seconds",
        ),
        supersession_state=_string(
            document["supersession-state"],
            context=f"{context}.supersession-state",
        ),
        supersession_reason=_string(
            document["supersession-reason"],
            context=f"{context}.supersession-reason",
        ),
        pr_slo=_string(
            document["pr-slo"],
            context=f"{context}.pr-slo",
        ),
        pr_slo_reason=_string(
            document["pr-slo-reason"],
            context=f"{context}.pr-slo-reason",
        ),
        summary=_ci_slice_summary_from_document(
            document["summary"],
            context=f"{context}.summary",
        ),
    )


def _require_expected_candidate(
    actual: CiCandidate,
    expected: CiCandidate,
    *,
    context: str,
) -> None:
    _validate_ci_candidate(expected)
    if actual != expected:
        message = f"{context} does not match the trusted current candidate"
        raise ValueError(message)


def admit_ci_candidate_json(
    document: bytes | bytearray,
    *,
    expected_candidate: CiCandidate,
) -> CiCandidate:
    """Admit one canonical UTF-8 CI Candidate document."""
    parsed = parse_canonical_json(document)
    candidate = _ci_candidate_from_document(parsed, context="CI Candidate")
    _require_expected_candidate(
        candidate,
        expected_candidate,
        context="CI Candidate",
    )
    return candidate


def admit_ci_qualification_snapshot_json(  # noqa: PLR0913
    document: bytes | bytearray,
    *,
    expected_candidate: CiCandidate,
    expected_repository_model_digest: str,
    expected_root_hk_definition: str,
    expected_root_hk_definition_digest: str,
    expected_plan_digest: str,
) -> CiQualificationSnapshot:
    """Admit one canonical UTF-8 CI Qualification Snapshot document."""
    _require_digest(
        expected_repository_model_digest,
        field="expected_repository_model_digest",
    )
    _require_nonempty_string(
        expected_root_hk_definition,
        field="expected_root_hk_definition",
    )
    _require_digest(
        expected_root_hk_definition_digest,
        field="expected_root_hk_definition_digest",
    )
    if (
        expected_root_hk_definition != CI_ROOT_HK_DEFINITION
        or expected_root_hk_definition_digest
        != _fixed_definition_digest(CI_ROOT_HK_DEFINITION)
    ):
        message = "trusted root-HK definition inputs are not current"
        raise ValueError(message)
    _require_digest(expected_plan_digest, field="expected_plan_digest")
    parsed = parse_canonical_json(document)
    snapshot = _ci_qualification_snapshot_from_document(
        parsed,
        context="CI Qualification Snapshot",
    )
    _require_expected_candidate(
        snapshot.candidate,
        expected_candidate,
        context="CI Qualification Snapshot",
    )
    if snapshot.repository_model_digest != expected_repository_model_digest:
        message = (
            "CI Qualification Snapshot does not match the trusted "
            "Repository Model digest"
        )
        raise ValueError(message)
    if (
        snapshot.root_hk_definition != expected_root_hk_definition
        or snapshot.root_hk_definition_digest
        != expected_root_hk_definition_digest
    ):
        message = (
            "CI Qualification Snapshot does not match trusted root-HK inputs"
        )
        raise ValueError(message)
    if ci_qualification_snapshot_digest(snapshot) != expected_plan_digest:
        message = "CI Qualification Snapshot does not match trusted Plan digest"
        raise ValueError(message)
    _validate_fixed_plan_obligations(snapshot)
    return snapshot


def admit_ci_evidence_json(
    document: bytes | bytearray,
    *,
    expected_candidate: CiCandidate,
    expected_plan_digest: str,
    expected_obligation: CiObligation,
) -> CiEvidence:
    """Admit one canonical UTF-8 CI Evidence document."""
    _require_digest(expected_plan_digest, field="expected_plan_digest")
    _validate_ci_obligation(expected_obligation)
    parsed = parse_canonical_json(document)
    evidence = _ci_evidence_from_document(parsed, context="CI Evidence")
    _require_expected_candidate(
        evidence.candidate,
        expected_candidate,
        context="CI Evidence",
    )
    if evidence.plan_digest != expected_plan_digest:
        message = "CI Evidence does not match the trusted Plan digest"
        raise ValueError(message)
    if evidence.obligation != expected_obligation:
        message = "CI Evidence does not match the trusted obligation"
        raise ValueError(message)
    return evidence


def admit_ci_artifact_json(  # noqa: PLR0913
    document: bytes | bytearray,
    *,
    expected_candidate: CiCandidate,
    expected_artifact_id: int,
    expected_artifact_name: str,
    expected_artifact_url: str,
    expected_transport_digest: str,
    expected_output_id: str,
    expected_logical_role: str,
    expected_media_kind: str,
) -> CiArtifact:
    """Admit one canonical current-candidate CI npm artifact record."""
    _require_positive_integer(
        expected_artifact_id,
        field="expected_artifact_id",
    )
    _require_nonempty_string(
        expected_artifact_name,
        field="expected_artifact_name",
    )
    _require_nonempty_string(
        expected_artifact_url,
        field="expected_artifact_url",
    )
    _require_digest(
        expected_transport_digest,
        field="expected_transport_digest",
    )
    _require_nonempty_string(expected_output_id, field="expected_output_id")
    _require_nonempty_string(
        expected_logical_role,
        field="expected_logical_role",
    )
    _require_nonempty_string(expected_media_kind, field="expected_media_kind")
    parsed = parse_canonical_json(document)
    artifact = _ci_artifact_from_document(parsed, context="CI Artifact")
    _require_expected_candidate(
        artifact.candidate,
        expected_candidate,
        context="CI Artifact",
    )
    if (
        artifact.artifact_id != expected_artifact_id
        or artifact.artifact_name != expected_artifact_name
        or artifact.artifact_url != expected_artifact_url
        or artifact.transport_digest != expected_transport_digest
        or artifact.output_id != expected_output_id
        or artifact.logical_role != expected_logical_role
        or artifact.media_kind != expected_media_kind
    ):
        message = "CI Artifact does not match trusted platform metadata"
        raise ValueError(message)
    return artifact


def admit_ci_lane_result_json(
    document: bytes | bytearray,
    *,
    expected_candidate: CiCandidate,
    expected_plan_digest: str,
    expected_lane_id: str,
) -> CiLaneResult:
    """Admit one canonical UTF-8 CI Lane Result document."""
    _require_digest(expected_plan_digest, field="expected_plan_digest")
    _require_choice(
        expected_lane_id,
        _CI_LANE_ID_SET,
        field="expected_lane_id",
    )
    parsed = parse_canonical_json(document)
    result = _ci_lane_result_from_document(parsed, context="CI Lane Result")
    _require_expected_candidate(
        result.candidate,
        expected_candidate,
        context="CI Lane Result",
    )
    if result.plan_digest != expected_plan_digest:
        message = "CI Lane Result does not match the trusted Plan digest"
        raise ValueError(message)
    if result.lane_id != expected_lane_id:
        message = "CI Lane Result does not match the trusted static lane"
        raise ValueError(message)
    return result


def admit_ci_slice_decision_json(  # noqa: C901
    document: bytes | bytearray,
    *,
    expected_plan: CiQualificationSnapshot,
    expected_evidence: tuple[CiEvidence, ...],
    expected_elapsed_seconds: int,
    expected_supersession_state: str,
) -> CiSliceDecision:
    """Admit one canonical UTF-8 non-authoritative CI Slice Decision."""
    _validate_ci_qualification_snapshot(expected_plan)
    _require_nonnegative_integer(
        expected_elapsed_seconds,
        field="expected_elapsed_seconds",
    )
    _require_choice(
        expected_supersession_state,
        _SUPERSESSION_STATES,
        field="expected_supersession_state",
    )
    expected_plan_digest = ci_qualification_snapshot_digest(expected_plan)
    _require_exact_type(
        expected_evidence,
        tuple,
        field="expected_evidence",
    )
    expected_evidence_by_obligation: dict[str, CiEvidence] = {}
    for evidence in expected_evidence:
        _validate_ci_evidence(evidence)
        obligation_id = evidence.obligation.obligation_id
        if obligation_id in expected_evidence_by_obligation:
            message = "trusted Plan Evidence contains duplicate obligations"
            raise ValueError(message)
        expected_obligation = next(
            (
                obligation
                for obligation in expected_plan.obligations
                if obligation.obligation_id == obligation_id
            ),
            None,
        )
        if (
            expected_obligation is None
            or evidence.obligation != expected_obligation
            or evidence.plan_digest != expected_plan_digest
            or evidence.candidate != expected_plan.candidate
            or evidence.workflow_run_id != expected_plan.workflow_run_id
            or evidence.run_attempt != expected_plan.run_attempt
        ):
            message = "trusted Evidence does not match the trusted Plan"
            raise ValueError(message)
        expected_evidence_by_obligation[obligation_id] = evidence
    parsed = parse_canonical_json(document)
    decision = _ci_slice_decision_from_document(
        parsed,
        context="CI Slice Decision",
    )
    _require_expected_candidate(
        decision.candidate,
        expected_plan.candidate,
        context="CI Slice Decision",
    )
    if decision.plan_digest != expected_plan_digest:
        message = "CI Slice Decision does not match the trusted Plan digest"
        raise ValueError(message)
    if (
        decision.repository_model_digest
        != expected_plan.repository_model_digest
        or decision.scope_mode != expected_plan.scope_mode
        or decision.changed_paths != expected_plan.changed_paths
        or decision.selected_project_nodes
        != expected_plan.selected_project_nodes
        or decision.selected_release_units
        != expected_plan.selected_release_units
        or decision.selected_variants != expected_plan.selected_variants
        or decision.selected_outputs != expected_plan.selected_outputs
        or decision.plan_diagnostics != expected_plan.diagnostics
    ):
        message = "CI Slice Decision does not match trusted Plan scope"
        raise ValueError(message)
    if decision.elapsed_seconds != expected_elapsed_seconds:
        message = "CI Slice Decision does not match trusted elapsed time"
        raise ValueError(message)
    if decision.supersession_state != expected_supersession_state:
        message = "CI Slice Decision does not match trusted supersession state"
        raise ValueError(message)
    if (
        tuple(
            disposition.obligation
            for disposition in decision.obligation_dispositions
        )
        != expected_plan.obligations
    ):
        message = "CI Slice Decision dispositions do not match the trusted Plan"
        raise ValueError(message)
    for disposition in decision.obligation_dispositions:
        evidence = expected_evidence_by_obligation.get(
            disposition.obligation.obligation_id,
        )
        expected_digests = (
            () if evidence is None else (ci_evidence_digest(evidence),)
        )
        if disposition.evidence_digests != expected_digests:
            message = (
                "CI Slice Decision Evidence does not match trusted admitted "
                "Evidence"
            )
            raise ValueError(message)
        if (
            evidence is not None
            and disposition.outcome != evidence.normalized_outcome
        ):
            message = (
                "CI Slice Decision outcome does not match trusted admitted "
                "Evidence"
            )
            raise ValueError(message)
    expected_artifact_digests = tuple(
        ci_artifact_digest(artifact)
        for obligation in expected_plan.obligations
        if (
            evidence := expected_evidence_by_obligation.get(
                obligation.obligation_id
            )
        )
        is not None
        for artifact in evidence.artifacts
    )
    if decision.admitted_artifact_digests != expected_artifact_digests:
        message = (
            "CI Slice Decision artifacts do not match trusted admitted Evidence"
        )
        raise ValueError(message)
    return decision


def ci_candidate_digest(candidate: CiCandidate) -> str:
    """Return the canonical digest of one validated CI Candidate."""
    _validate_ci_candidate(candidate)
    return canonical_sha256(candidate.to_document())


def ci_qualification_snapshot_digest(
    snapshot: CiQualificationSnapshot,
) -> str:
    """Return the canonical digest of one validated Qualification Snapshot."""
    _validate_ci_qualification_snapshot(snapshot)
    return canonical_sha256(snapshot.to_document())


def ci_artifact_digest(artifact: CiArtifact) -> str:
    """Return the canonical digest of one validated CI artifact record."""
    _validate_ci_artifact(artifact)
    return canonical_sha256(artifact.to_document())


def ci_evidence_digest(evidence: CiEvidence) -> str:
    """Return the canonical digest of one validated CI Evidence record."""
    _validate_ci_evidence(evidence)
    return canonical_sha256(evidence.to_document())


def ci_lane_result_digest(result: CiLaneResult) -> str:
    """Return the canonical digest of one validated CI Lane Result."""
    _validate_ci_lane_result(result)
    return canonical_sha256(result.to_document())


def ci_slice_decision_digest(decision: CiSliceDecision) -> str:
    """Return the canonical digest of one validated CI Slice Decision."""
    _validate_ci_slice_decision(decision)
    return canonical_sha256(decision.to_document())


__all__ = [
    "CI_ARTIFACT_SCHEMA",
    "CI_CANDIDATE_SCHEMA",
    "CI_EVIDENCE_SCHEMA",
    "CI_LANE_IDS",
    "CI_LANE_RESULT_SCHEMA",
    "CI_OBLIGATION_DISPOSITION_SCHEMA",
    "CI_OBLIGATION_SCHEMA",
    "CI_QUALIFICATION_SNAPSHOT_SCHEMA",
    "CI_SLICE_DECISION_SCHEMA",
    "CI_SLICE_SUMMARY_SCHEMA",
    "CI_WORKFLOW_PATH",
    "CiArtifact",
    "CiCandidate",
    "CiEvidence",
    "CiLaneResult",
    "CiObligation",
    "CiObligationDisposition",
    "CiOutputIdentity",
    "CiQualificationSnapshot",
    "CiSliceDecision",
    "CiSliceSummary",
    "admit_ci_artifact_json",
    "admit_ci_candidate_json",
    "admit_ci_evidence_json",
    "admit_ci_lane_result_json",
    "admit_ci_qualification_snapshot_json",
    "admit_ci_slice_decision_json",
    "ci_artifact_digest",
    "ci_candidate_digest",
    "ci_evidence_digest",
    "ci_lane_result_digest",
    "ci_qualification_snapshot_digest",
    "ci_slice_decision_digest",
    "ci_slice_summary_text",
    "derive_ci_failure",
    "derive_ci_pr_slo",
]
