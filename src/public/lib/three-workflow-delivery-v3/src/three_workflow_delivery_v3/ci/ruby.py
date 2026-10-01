"""Ruby CI Plan, independent Evidence and conservative slice Decision."""

from __future__ import annotations

from dataclasses import dataclass
from pathlib import PurePosixPath
from subprocess import TimeoutExpired
from typing import TYPE_CHECKING, cast

from three_workflow_delivery_v3._ruby_native import (
    RUBY_RELEASE_UNIT,
    ruby_object,
    ruby_text,
)
from three_workflow_delivery_v3.adapters.ruby import qualify_ruby_consumer
from three_workflow_delivery_v3.canonical import (
    JsonValue,
    canonical_sha256,
    canonicalize,
    parse_canonical_json,
)
from three_workflow_delivery_v3.ci.path_admission import is_repository_only_path
from three_workflow_delivery_v3.records.ruby import (
    RubyArtifact,
    validate_ruby_artifact,
)
from three_workflow_delivery_v3.repository.ruby_model import (
    RUBY_QUALITY,
    RubyRepositoryModelSnapshot,
)

if TYPE_CHECKING:
    from collections.abc import Callable

    from three_workflow_delivery_v3.adapters.ruby import RubyDistribution
    from three_workflow_delivery_v3.records.artifacts import ArtifactReference

_CI_PURPOSES = {"ci-pr-slice-shadow", "slice-validation"}


def ruby_ci_affected(
    model: RubyRepositoryModelSnapshot,
    changed_paths: tuple[str, ...] | None,
) -> bool:
    """Select full scope when comparison is missing; include deleted inputs."""
    if changed_paths is None:
        return True
    for path in changed_paths:
        if (
            not path
            or PurePosixPath(path).as_posix() != path
            or path.startswith("/")
            or ".." in path.split("/")
            or "\\" in path
        ):
            message = "invalid Ruby CI comparison path"
            raise ValueError(message)
    sources = dict(model.provider.source_input_manifest)
    prefixes = (
        "src/public/lib/hcoona-release-smoke-ruby/",
        "src/public/lib/three-workflow-delivery-v3/",
        ".github/actions/workflow-delivery-v3",
        ".github/workflows/workflow-delivery-v3",
        ".github/workflow-delivery/",
        "eng/workflow-delivery/v3/",
    )
    affected = False
    for path in changed_paths:
        if path in sources or path.startswith(prefixes):
            affected = True
        elif is_repository_only_path(path) or path.startswith(
            "src/public/lib/hcoona-release-smoke-npm/"
        ):
            continue
        else:
            message = f"Ruby CI changed path is unclassified: {path}"
            raise ValueError(message)
    return affected


@dataclass(frozen=True, slots=True)
class RubyCiPlan:
    """Current same-revision CI selection over a frozen Ruby Model."""

    model: RubyRepositoryModelSnapshot
    model_reference: ArtifactReference
    changed_paths: tuple[str, ...] | None

    def __post_init__(self) -> None:
        """Reject Release identity or a foreign Model transport."""
        context = self.model.context
        if (
            context.purpose not in _CI_PURPOSES
            or context.control != f"workflow-delivery-v3:{context.target}"
            or self.model_reference.payload_digest != self.model.snapshot_digest
        ):
            message = "Ruby CI Plan requires its current same-revision Model"
            raise ValueError(message)
        ruby_ci_affected(self.model, self.changed_paths)

    @property
    def selected(self) -> bool:
        """Return the affected-system selection."""
        return ruby_ci_affected(self.model, self.changed_paths)

    def to_document(self) -> dict[str, JsonValue]:
        """Close the Plan and exact evidence obligations."""
        return {
            "schema": "workflow-delivery/v3/ruby-ci-plan",
            "model": self.model.to_document(),
            "model-reference": self.model_reference.to_document(),
            "changed-paths": None
            if self.changed_paths is None
            else cast("list[JsonValue]", list(self.changed_paths)),
            "selected": self.selected,
            "obligations": cast(
                "list[JsonValue]", list(RUBY_QUALITY) if self.selected else []
            ),
            "producer": "plan-ruby-ci",
        }

    @property
    def plan_digest(self) -> str:
        """Return the immutable Plan identity."""
        return canonical_sha256(self.to_document())


@dataclass(frozen=True, slots=True)
class RubyCiEvidence:
    """One selected quality obligation with exact current original artifacts."""

    plan_digest: str
    definition: str
    artifact: RubyArtifact
    result: str
    detail: bytes

    def __post_init__(self) -> None:
        """Require a closed obligation outcome and retained canonical detail."""
        if self.definition not in RUBY_QUALITY or self.result not in {
            "passed",
            "failed",
        }:
            message = "invalid Ruby CI Evidence obligation or result"
            raise ValueError(message)
        parse_canonical_json(self.detail)

    def to_document(self) -> dict[str, JsonValue]:
        """Serialize a CI-only Evidence identity, never Release evidence."""
        return {
            "schema": "workflow-delivery/v3/ruby-ci-evidence",
            "plan-digest": self.plan_digest,
            "definition": self.definition,
            "artifact": self.artifact.to_document(),
            "result": self.result,
            "detail": parse_canonical_json(self.detail),
            "producer": "qualify-ruby-ci",
        }

    @property
    def evidence_digest(self) -> str:
        """Return this Evidence's canonical identity."""
        return canonical_sha256(self.to_document())


def run_ruby_ci_quality(
    plan: RubyCiPlan,
    artifact: RubyArtifact,
    payload: bytes,
    *,
    consumer: Callable[
        [RubyDistribution], dict[str, JsonValue]
    ] = qualify_ruby_consumer,
) -> tuple[RubyCiEvidence, ...]:
    """Inspect the original gem and run its separate clean native consumer."""
    if not plan.selected:
        message = "unselected Ruby CI Plan cannot execute quality"
        raise ValueError(message)
    validate_ruby_artifact(
        artifact,
        witness=plan.model.build_request().witness,
        context=plan.model.context,
    )
    inspected = artifact.inspect(payload)
    evidence = [
        RubyCiEvidence(
            plan.plan_digest,
            RUBY_QUALITY[0],
            artifact,
            "passed",
            canonicalize({"digest": inspected.digest}),
        )
    ]
    try:
        detail: dict[str, JsonValue] = consumer(inspected)
        if (
            detail.get("schema")
            != "workflow-delivery/v3/ruby-consumer-evidence"
            or detail.get("artifact-digest") != inspected.digest
            or detail.get("witness-digest")
            != canonical_sha256(inspected.witness.to_document())
            or detail.get("project-id") != RUBY_RELEASE_UNIT
            or detail.get("version") != inspected.witness.nbgv.native_version
        ):
            message = "Ruby consumer returned foreign artifact evidence"
            raise ValueError(message)  # noqa: TRY301 - retain failed evidence
        outcome = "passed"
    except (OSError, ValueError, RuntimeError, TimeoutExpired) as error:
        detail = {"error-kind": type(error).__name__}
        outcome = "failed"
    evidence.append(
        RubyCiEvidence(
            plan.plan_digest,
            RUBY_QUALITY[1],
            artifact,
            outcome,
            canonicalize(detail),
        )
    )
    return tuple(evidence)


@dataclass(frozen=True, slots=True)
class RubyCiDecision:
    """Finalizer result joined only from the current Plan and Evidence."""

    plan: RubyCiPlan
    evidence: tuple[RubyCiEvidence, ...]

    def __post_init__(self) -> None:
        """Reject duplicates, unplanned evidence and cross-purpose artifacts."""
        seen: set[str] = set()
        artifact_ids: set[str] = set()
        for item in self.evidence:
            if (
                not self.plan.selected
                or item.plan_digest != self.plan.plan_digest
                or item.definition in seen
            ):
                message = "Ruby CI Evidence differs from current Plan"
                raise ValueError(message)
            seen.add(item.definition)
            validate_ruby_artifact(
                item.artifact,
                witness=self.plan.model.build_request().witness,
                context=self.plan.model.context,
            )
            artifact_ids.add(item.artifact.artifact_digest)
        if len(artifact_ids) > 1:
            message = "Ruby CI obligations refer to different original builds"
            raise ValueError(message)

    @property
    def result(self) -> str:
        """Missing or failing selected obligations cannot become successful."""
        if not self.plan.selected:
            return "empty"
        if any(e.result == "failed" for e in self.evidence):
            return "failed"
        return (
            "passed"
            if {e.definition for e in self.evidence} == set(RUBY_QUALITY)
            else "incomplete"
        )

    def to_document(self) -> dict[str, JsonValue]:
        """Emit CI status without granting publication or artifact promotion."""
        return {
            "schema": "workflow-delivery/v3/ruby-ci-decision",
            "plan-digest": self.plan.plan_digest,
            "release-unit": RUBY_RELEASE_UNIT,
            "result": self.result,
            "evidence-digests": cast(
                "list[JsonValue]", [e.evidence_digest for e in self.evidence]
            ),
            "authority": "non-authoritative",
            "producer": "finalize-ruby-ci",
        }


def ruby_ci_plan_from_document(value: JsonValue) -> RubyCiPlan:
    """Admit a strict CI Plan; Release shapes have no compatibility path."""
    from three_workflow_delivery_v3.records.artifacts import (  # noqa: PLC0415
        artifact_reference_from_document,
    )
    from three_workflow_delivery_v3.repository.ruby_model import (  # noqa: PLC0415
        ruby_repository_model_from_document,
    )

    doc = ruby_object(
        value,
        {
            "schema",
            "model",
            "model-reference",
            "changed-paths",
            "selected",
            "obligations",
            "producer",
        },
    )
    paths = doc["changed-paths"]
    if paths is not None and not isinstance(paths, list):
        message = "Ruby CI changed paths must be a list or null"
        raise TypeError(message)
    plan = RubyCiPlan(
        ruby_repository_model_from_document(doc["model"]),
        artifact_reference_from_document(doc["model-reference"]),
        None if paths is None else tuple(ruby_text(p) for p in paths),
    )
    if canonicalize(plan.to_document()) != canonicalize(doc):
        message = "Ruby CI Plan differs from its closed contract"
        raise ValueError(message)
    return plan


def ruby_ci_evidence_from_document(value: JsonValue) -> RubyCiEvidence:
    """Admit one closed CI-only Evidence record."""
    from three_workflow_delivery_v3.records.ruby import (  # noqa: PLC0415
        ruby_artifact_from_document,
    )

    doc = ruby_object(
        value,
        {
            "schema",
            "plan-digest",
            "definition",
            "artifact",
            "result",
            "detail",
            "producer",
        },
    )
    evidence = RubyCiEvidence(
        ruby_text(doc["plan-digest"]),
        ruby_text(doc["definition"]),
        ruby_artifact_from_document(doc["artifact"]),
        ruby_text(doc["result"]),
        canonicalize(doc["detail"]),
    )
    if canonicalize(evidence.to_document()) != canonicalize(doc):
        message = "Ruby CI Evidence differs from its closed contract"
        raise ValueError(message)
    return evidence
