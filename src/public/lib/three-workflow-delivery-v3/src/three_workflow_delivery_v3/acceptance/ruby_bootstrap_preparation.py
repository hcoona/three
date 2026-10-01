"""First-project Ruby Provider, original Build and separate Qualification."""

from __future__ import annotations

import re
from dataclasses import dataclass, field
from datetime import UTC, datetime
from subprocess import TimeoutExpired
from typing import TYPE_CHECKING, cast

from three_workflow_delivery_v3._ruby_native import ruby_object, ruby_text
from three_workflow_delivery_v3.acceptance.ruby_bootstrap_contract import (
    BOOTSTRAP_PURPOSE,
    RubyBootstrapRun,
    require_bootstrap,
    require_bootstrap_transport,
    validate_bootstrap_provider,
)
from three_workflow_delivery_v3.adapters.ruby import (
    RubyBuildRequest,
    RubyDistribution,
    RubyPackageTargetWitness,
    build_ruby_package,
    qualify_ruby_consumer,
)
from three_workflow_delivery_v3.canonical import (
    JsonValue,
    canonical_sha256,
    canonicalize,
    parse_canonical_json,
)
from three_workflow_delivery_v3.records.ruby import (
    RubyArtifact,
    ruby_artifact_from_document,
    validate_ruby_quality_detail,
)
from three_workflow_delivery_v3.repository.node_provider import (
    CheckoutMaterialization,
    _run_command,
)
from three_workflow_delivery_v3.repository.ruby_controls import (
    RUBY_ENVELOPE_PATH,
)
from three_workflow_delivery_v3.repository.ruby_model import RUBY_QUALITY
from three_workflow_delivery_v3.repository.ruby_provider import (
    RubyProviderResult,
    provide_ruby_repository_facts,
    ruby_provider_result_from_document,
)

if TYPE_CHECKING:
    from collections.abc import Callable
    from pathlib import Path

    from three_workflow_delivery_v3.records.artifacts import (
        ArtifactReference,
        ArtifactTransportIdentity,
    )


def _now() -> datetime:
    return datetime.now(UTC)


def _require_source_controls(
    run: RubyBootstrapRun, provider: RubyProviderResult
) -> None:
    inputs = dict(provider.source_input_manifest)
    binding = ruby_object(run.request.document["binding"])
    require_bootstrap(
        inputs.get(RUBY_ENVELOPE_PATH)
        == run.request.document["envelope-digest"]
        and inputs.get(ruby_text(binding["configuration-path"]))
        == run.inputs.configuration.digest,
        "Ruby bootstrap source lacks its admitted protected controls",
    )


def _target_tree(root: Path, run: RubyBootstrapRun) -> str:
    tree = _run_command(
        ("git", "rev-parse", run.target + "^{tree}"), root
    ).strip()
    require_bootstrap(
        tree == run.request.document["tree"],
        "Ruby bootstrap checkout tree differs from the request",
    )
    return tree


def provide_ruby_bootstrap(
    root: Path,
    run: RubyBootstrapRun,
    materialization: CheckoutMaterialization,
    *,
    clock: Callable[[], datetime] = _now,
) -> bytes:
    """Produce fresh native facts in the existing unprivileged Provider zone.

    The hosted job must have no package-write or OIDC capability. This function
    accepts no credential and performs no registry or publication operation.
    """
    run.require_current(clock())
    tree = _target_tree(root, run)
    provider = provide_ruby_repository_facts(
        root, run.provider_binding(), materialization
    )
    validate_bootstrap_provider(run, provider)
    _require_source_controls(run, provider)
    require_bootstrap(
        _target_tree(root, run) == tree, "Ruby bootstrap source tree changed"
    )
    run.require_current(clock())
    return canonicalize(
        {
            "schema": "workflow-delivery/v3/ruby-bootstrap-provider-v1",
            "run-binding-digest": run.binding_digest,
            "target-tree": tree,
            "provider": provider.to_document(),
            "producer": "provide-ruby-bootstrap",
        }
    )


@dataclass(frozen=True, slots=True)
class RubyBootstrapPlan:
    """Bootstrap-only frozen Build plan from an uploaded current Provider."""

    run: RubyBootstrapRun
    provider_content: bytes = field(repr=False)
    provider_reference: ArtifactReference
    provider_transport: ArtifactTransportIdentity

    def __post_init__(self) -> None:
        """Close Provider, source tree and immutable current-run transport."""
        require_bootstrap_transport(
            self.run,
            self.provider_reference,
            self.provider_transport,
            self.provider_content,
            producer="provide-ruby-bootstrap",
            payload_path="provider.json",
        )
        provider = self.provider
        validate_bootstrap_provider(self.run, provider)
        _require_source_controls(self.run, provider)

    @property
    def provider(self) -> RubyProviderResult:
        """Import strict technical data without target evaluation."""
        doc = ruby_object(
            parse_canonical_json(self.provider_content),
            {
                "schema",
                "run-binding-digest",
                "target-tree",
                "provider",
                "producer",
            },
        )
        require_bootstrap(
            doc["schema"] == "workflow-delivery/v3/ruby-bootstrap-provider-v1"
            and doc["run-binding-digest"] == self.run.binding_digest
            and doc["target-tree"] == self.run.request.document["tree"]
            and doc["producer"] == "provide-ruby-bootstrap",
            "Ruby bootstrap Provider envelope belongs to another request",
        )
        return ruby_provider_result_from_document(doc["provider"])

    def build_request(self) -> RubyBuildRequest:
        """Reuse the frozen native recipe without a normal Release Model."""
        provider = self.provider
        witness = RubyPackageTargetWitness(
            self.run.target,
            provider.nbgv,
            provider.binding.catalog_digest,
            canonical_sha256(
                {
                    "schema": "workflow-delivery/v3/control-identity",
                    "identity": provider.binding.control,
                }
            ),
            BOOTSTRAP_PURPOSE,
        )
        return RubyBuildRequest(
            witness,
            provider.source_input_manifest,
            provider.source_date_epoch,
            provider.native_profile,
        )

    def to_document(self) -> dict[str, JsonValue]:
        """Keep exact first-project purpose and current artifacts explicit."""
        return {
            "schema": "workflow-delivery/v3/ruby-bootstrap-plan-v1",
            "run": self.run.to_document(),
            "provider": parse_canonical_json(self.provider_content),
            "provider-reference": self.provider_reference.to_document(),
            "provider-transport": self.provider_transport.to_document(),
            "witness": self.build_request().witness.to_document(),
            "obligations": cast("JsonValue", list(RUBY_QUALITY)),
            "producer": "plan-ruby-bootstrap",
        }

    @property
    def plan_digest(self) -> str:
        """Identify the exact independent bootstrap Qualification basis."""
        return canonical_sha256(self.to_document())


def build_ruby_bootstrap(
    root: Path, plan: RubyBootstrapPlan, *, clock: Callable[[], datetime] = _now
) -> RubyDistribution:
    """Build a new original from frozen facts, without NBGV replay."""
    plan.run.require_current(clock())
    _target_tree(root, plan.run)
    distribution = build_ruby_package(root, plan.build_request())
    plan.run.require_current(clock())
    return distribution


def _require_artifact(plan: RubyBootstrapPlan, artifact: RubyArtifact) -> None:
    require_bootstrap(
        type(artifact) is RubyArtifact
        and artifact.witness == plan.build_request().witness
        and artifact.transport.workflow_run_id == plan.run.run_id
        and artifact.transport.run_attempt == 1
        and artifact.transport.artifact_url
        == (
            f"https://github.com/hcoona/three/actions/runs/{plan.run.run_id}"
            f"/artifacts/{artifact.reference.artifact_id}"
        )
        and artifact.reference.artifact_digest
        == artifact.reference.payload_digest,
        "Ruby bootstrap original belongs to another purpose, build or run",
    )


@dataclass(frozen=True, slots=True)
class RubyBootstrapEvidence:
    """One bootstrap-only native content or clean-consumer result."""

    plan_digest: str
    definition: str
    artifact: RubyArtifact
    result: str
    detail: bytes = field(repr=False)

    def __post_init__(self) -> None:
        """Reject normal purpose and incompatible intrinsic quality detail."""
        require_bootstrap(
            isinstance(self.plan_digest, str)
            and re.fullmatch(r"sha256:[0-9a-f]{64}", self.plan_digest)
            is not None
            and type(self.artifact) is RubyArtifact
            and self.artifact.witness.purpose == BOOTSTRAP_PURPOSE
            and self.definition in RUBY_QUALITY
            and self.result in {"passed", "failed"},
            "Ruby bootstrap Evidence purpose, obligation or result differs",
        )
        validate_ruby_quality_detail(
            self.artifact, self.definition, self.result, self.detail
        )

    def to_document(self) -> dict[str, JsonValue]:
        """Emit a distinct schema rejected by normal Live and CI parsers."""
        return {
            "schema": "workflow-delivery/v3/ruby-bootstrap-evidence-v1",
            "plan-digest": self.plan_digest,
            "definition": self.definition,
            "artifact": self.artifact.to_document(),
            "result": self.result,
            "detail": parse_canonical_json(self.detail),
            "producer": "qualify-ruby-bootstrap",
        }

    @property
    def evidence_digest(self) -> str:
        """Return the immutable current-plan Evidence identity."""
        return canonical_sha256(self.to_document())


def run_ruby_bootstrap_quality(  # noqa: PLR0913 - one independent selected obligation
    plan: RubyBootstrapPlan,
    artifact: RubyArtifact,
    payload: bytes,
    definition: str,
    *,
    clock: Callable[[], datetime] = _now,
    consumer: Callable[
        [RubyDistribution], dict[str, JsonValue]
    ] = qualify_ruby_consumer,
) -> RubyBootstrapEvidence:
    """Reinspect the uploaded original and run only the selected obligation.

    Hosted assembly can invoke each obligation in its own unprivileged job.
    Consumer failure remains failed; malformed original admission raises before
    an evidence success can be created. No registry operation is performed.
    """
    plan.run.require_current(clock())
    require_bootstrap(
        definition in RUBY_QUALITY, "Unknown Ruby bootstrap quality obligation"
    )
    _require_artifact(plan, artifact)
    require_bootstrap_transport(
        plan.run,
        artifact.reference,
        artifact.transport,
        payload,
        producer="build-ruby",
        payload_path=artifact.filename,
    )
    distribution = artifact.inspect(payload)
    result = "passed"
    if definition == RUBY_QUALITY[0]:
        detail: dict[str, JsonValue] = {"digest": distribution.digest}
    else:
        try:
            detail = consumer(distribution)
            validate_ruby_quality_detail(
                artifact, definition, result, canonicalize(detail)
            )
        except (
            OSError,
            TypeError,
            ValueError,
            RuntimeError,
            TimeoutExpired,
        ) as error:
            result = "failed"
            detail = {"error-kind": type(error).__name__}
    plan.run.require_current(clock())
    return RubyBootstrapEvidence(
        plan.plan_digest, definition, artifact, result, canonicalize(detail)
    )


@dataclass(frozen=True, slots=True)
class RubyBootstrapQualification:
    """First-project Qualification without Approval or normal completion."""

    plan: RubyBootstrapPlan
    evidence: tuple[RubyBootstrapEvidence, ...]

    def __post_init__(self) -> None:
        """Require unique obligations over one original current build."""
        definitions: set[str] = set()
        artifacts: set[str] = set()
        for item in self.evidence:
            require_bootstrap(
                type(item) is RubyBootstrapEvidence
                and item.plan_digest == self.plan.plan_digest
                and item.definition not in definitions,
                "Ruby bootstrap Evidence is duplicate or from another Plan",
            )
            _require_artifact(self.plan, item.artifact)
            definitions.add(item.definition)
            artifacts.add(item.artifact.artifact_digest)
        require_bootstrap(
            len(artifacts) <= 1,
            "Ruby bootstrap obligations describe different originals",
        )

    @property
    def result(self) -> str:
        """Missing or failed independent obligations cannot pass."""
        if any(item.result == "failed" for item in self.evidence):
            return "failed"
        return (
            "passed"
            if {item.definition for item in self.evidence} == set(RUBY_QUALITY)
            else "incomplete"
        )

    def to_document(self) -> dict[str, JsonValue]:
        """Retain a bootstrap-only verdict without publication authority."""
        return {
            "schema": "workflow-delivery/v3/ruby-bootstrap-qualification-v1",
            "plan-digest": self.plan.plan_digest,
            "purpose": BOOTSTRAP_PURPOSE,
            "result": self.result,
            "evidence-digests": cast(
                "JsonValue", [item.evidence_digest for item in self.evidence]
            ),
            "publication-authority": False,
            "producer": "finalize-ruby-bootstrap-qualification",
        }


def ruby_bootstrap_evidence_from_document(
    value: JsonValue,
) -> RubyBootstrapEvidence:
    """Admit only the distinct closed bootstrap quality record."""
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
    result = RubyBootstrapEvidence(
        ruby_text(doc["plan-digest"]),
        ruby_text(doc["definition"]),
        ruby_artifact_from_document(doc["artifact"]),
        ruby_text(doc["result"]),
        canonicalize(doc["detail"]),
    )
    require_bootstrap(
        canonicalize(doc) == canonicalize(result.to_document()),
        "Ruby bootstrap Evidence differs from its closed schema",
    )
    return result
