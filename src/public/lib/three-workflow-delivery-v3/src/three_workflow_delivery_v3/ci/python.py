"""Python CI Plan, independent Evidence and conservative slice Decision."""

from __future__ import annotations

from dataclasses import dataclass
from pathlib import PurePosixPath
from typing import TYPE_CHECKING, cast

from three_workflow_delivery_v3.adapters.python import qualify_python_consumer
from three_workflow_delivery_v3.canonical import (
    JsonValue,
    canonical_sha256,
    canonicalize,
    parse_canonical_json,
)
from three_workflow_delivery_v3.ci.path_admission import is_repository_only_path
from three_workflow_delivery_v3.records.python import (
    PythonArtifact,
    validate_python_artifact_pair,
)
from three_workflow_delivery_v3.repository.python_model import (
    PYTHON_QUALITY,
    PythonRepositoryModelSnapshot,
)
from three_workflow_delivery_v3.repository.python_provider import (
    PYTHON_RELEASE_UNIT,
    python_digest,
)

if TYPE_CHECKING:
    from collections.abc import Callable

    from three_workflow_delivery_v3.adapters.python import (
        PythonConsumerResult,
        PythonDistribution,
    )
    from three_workflow_delivery_v3.records.artifacts import ArtifactReference

_CI_PURPOSES = {"ci-pr-slice-shadow", "slice-validation"}


def python_ci_affected(
    model: PythonRepositoryModelSnapshot,
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
            message = "invalid Python CI comparison path"
            raise ValueError(message)
    sources = dict(model.provider.source_input_manifest)
    prefixes = (
        "src/public/lib/hcoona-release-smoke-python/",
        "src/public/lib/nbgv-python/",
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
            message = f"Python CI changed path is unclassified: {path}"
            raise ValueError(message)
    return affected


@dataclass(frozen=True, slots=True)
class PythonCiPlan:
    """Current same-revision CI selection over a frozen Python Model."""

    model: PythonRepositoryModelSnapshot
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
            message = "Python CI Plan requires its current same-revision Model"
            raise ValueError(message)
        python_ci_affected(self.model, self.changed_paths)

    @property
    def selected(self) -> bool:
        """Return the affected-system selection."""
        return python_ci_affected(self.model, self.changed_paths)

    def to_document(self) -> dict[str, JsonValue]:
        """Close the Plan and exact evidence obligations."""
        return {
            "schema": "workflow-delivery/v3/python-ci-plan",
            "model": self.model.to_document(),
            "model-reference": self.model_reference.to_document(),
            "changed-paths": None
            if self.changed_paths is None
            else cast("list[JsonValue]", list(self.changed_paths)),
            "selected": self.selected,
            "obligations": cast(
                "list[JsonValue]", list(PYTHON_QUALITY) if self.selected else []
            ),
            "producer": "plan-python-ci",
        }

    @property
    def plan_digest(self) -> str:
        """Return the immutable Plan identity."""
        return canonical_sha256(self.to_document())


@dataclass(frozen=True, slots=True)
class PythonCiEvidence:
    """One selected quality obligation with exact current original artifacts."""

    plan_digest: str
    definition: str
    artifacts: tuple[PythonArtifact, ...]
    result: str
    detail: bytes

    def __post_init__(self) -> None:
        """Require a closed obligation outcome and retained canonical detail."""
        if self.definition not in PYTHON_QUALITY or self.result not in {
            "passed",
            "failed",
        }:
            message = "invalid Python CI Evidence obligation or result"
            raise ValueError(message)
        parse_canonical_json(self.detail)

    def to_document(self) -> dict[str, JsonValue]:
        """Serialize a CI-only Evidence identity, never Release evidence."""
        return {
            "schema": "workflow-delivery/v3/python-ci-evidence",
            "plan-digest": self.plan_digest,
            "definition": self.definition,
            "artifacts": [a.to_document() for a in self.artifacts],
            "result": self.result,
            "detail": parse_canonical_json(self.detail),
            "producer": "qualify-python-ci",
        }

    @property
    def evidence_digest(self) -> str:
        """Return this Evidence's canonical identity."""
        return canonical_sha256(self.to_document())


def run_python_ci_quality(
    plan: PythonCiPlan,
    artifacts: tuple[PythonArtifact, ...],
    payloads: tuple[bytes, bytes],
    *,
    consumer: Callable[
        [PythonDistribution], PythonConsumerResult
    ] = qualify_python_consumer,
) -> tuple[PythonCiEvidence, ...]:
    """Run archive inspection and both credential-free consumers."""
    if not plan.selected:
        message = "unselected Python CI Plan cannot execute quality"
        raise ValueError(message)
    validate_python_artifact_pair(
        artifacts,
        witness=plan.model.build_request().witness,
        context=plan.model.context,
    )
    if len(payloads) != len(artifacts):
        message = "Python CI requires both original payloads"
        raise ValueError(message)
    inspected = tuple(
        a.inspect(p) for a, p in zip(artifacts, payloads, strict=True)
    )
    evidence = [
        PythonCiEvidence(
            plan.plan_digest,
            PYTHON_QUALITY[0],
            artifacts,
            "passed",
            canonicalize({"digests": [d.digest for d in inspected]}),
        )
    ]
    for definition, distribution in zip(
        PYTHON_QUALITY[1:], inspected, strict=True
    ):
        try:
            result = consumer(distribution)
            if (
                result.variant != distribution.variant
                or result.original_digest != distribution.digest
            ):
                message = "Python consumer returned foreign artifact evidence"
                raise ValueError(message)  # noqa: TRY301 - retain failed evidence
            detail: dict[str, JsonValue] = {
                "installed": parse_canonical_json(result.installed),
                "commands": [
                    parse_canonical_json(c) for c in result.command_evidence
                ],
                "rebuilt-wheel-digest": None
                if result.rebuilt_wheel is None
                else python_digest(result.rebuilt_wheel),
            }
            outcome = "passed"
        except (OSError, ValueError, RuntimeError) as error:
            detail = {"error-kind": type(error).__name__}
            outcome = "failed"
        evidence.append(
            PythonCiEvidence(
                plan.plan_digest,
                definition,
                artifacts,
                outcome,
                canonicalize(detail),
            )
        )
    return tuple(evidence)


@dataclass(frozen=True, slots=True)
class PythonCiDecision:
    """Finalizer result joined only from the current Plan and Evidence."""

    plan: PythonCiPlan
    evidence: tuple[PythonCiEvidence, ...]

    def __post_init__(self) -> None:
        """Reject duplicates, unplanned evidence and cross-purpose artifacts."""
        seen: set[str] = set()
        artifact_sets: set[tuple[str, ...]] = set()
        for item in self.evidence:
            if (
                not self.plan.selected
                or item.plan_digest != self.plan.plan_digest
                or item.definition in seen
            ):
                message = "Python CI Evidence differs from current Plan"
                raise ValueError(message)
            seen.add(item.definition)
            validate_python_artifact_pair(
                item.artifacts,
                witness=self.plan.model.build_request().witness,
                context=self.plan.model.context,
            )
            artifact_sets.add(tuple(a.artifact_digest for a in item.artifacts))
        if len(artifact_sets) > 1:
            message = "Python CI obligations refer to different original builds"
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
            if {e.definition for e in self.evidence} == set(PYTHON_QUALITY)
            else "incomplete"
        )

    def to_document(self) -> dict[str, JsonValue]:
        """Emit CI status without granting publication or artifact promotion."""
        return {
            "schema": "workflow-delivery/v3/python-ci-decision",
            "plan-digest": self.plan.plan_digest,
            "release-unit": PYTHON_RELEASE_UNIT,
            "result": self.result,
            "evidence-digests": cast(
                "list[JsonValue]", [e.evidence_digest for e in self.evidence]
            ),
            "authority": "non-authoritative",
            "producer": "finalize-python-ci",
        }


def python_ci_plan_from_document(value: JsonValue) -> PythonCiPlan:
    """Admit a strict CI Plan; Release shapes have no compatibility path."""
    from three_workflow_delivery_v3.records.artifacts import (  # noqa: PLC0415
        artifact_reference_from_document,
    )
    from three_workflow_delivery_v3.repository.python_model import (  # noqa: PLC0415
        python_repository_model_from_document,
    )
    from three_workflow_delivery_v3.repository.python_provider import (  # noqa: PLC0415
        python_object,
        python_text,
    )

    doc = python_object(
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
        message = "Python CI changed paths must be a list or null"
        raise TypeError(message)
    plan = PythonCiPlan(
        python_repository_model_from_document(doc["model"]),
        artifact_reference_from_document(doc["model-reference"]),
        None if paths is None else tuple(python_text(p) for p in paths),
    )
    if canonicalize(plan.to_document()) != canonicalize(doc):
        message = "Python CI Plan differs from its closed contract"
        raise ValueError(message)
    return plan


def python_ci_evidence_from_document(value: JsonValue) -> PythonCiEvidence:
    """Admit one closed CI-only Evidence record."""
    from three_workflow_delivery_v3.records.python import (  # noqa: PLC0415
        python_artifact_from_document,
    )
    from three_workflow_delivery_v3.repository.python_provider import (  # noqa: PLC0415
        python_object,
        python_text,
    )

    doc = python_object(
        value,
        {
            "schema",
            "plan-digest",
            "definition",
            "artifacts",
            "result",
            "detail",
            "producer",
        },
    )
    if not isinstance(doc["artifacts"], list):
        message = "Python CI artifacts must be a list"
        raise TypeError(message)
    evidence = PythonCiEvidence(
        python_text(doc["plan-digest"]),
        python_text(doc["definition"]),
        tuple(python_artifact_from_document(a) for a in doc["artifacts"]),
        python_text(doc["result"]),
        canonicalize(doc["detail"]),
    )
    if canonicalize(evidence.to_document()) != canonicalize(doc):
        message = "Python CI Evidence differs from its closed contract"
        raise ValueError(message)
    return evidence
