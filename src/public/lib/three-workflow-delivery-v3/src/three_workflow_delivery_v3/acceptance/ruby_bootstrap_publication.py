"""Distinct first-project qualification, approval and mutation authority."""

from __future__ import annotations

from dataclasses import dataclass, field
from typing import TYPE_CHECKING, cast

from three_workflow_delivery_v3._ruby_native import ruby_digest, ruby_object
from three_workflow_delivery_v3.acceptance.ruby_bootstrap_contract import (
    RubyBootstrapRun,
    require_bootstrap,
    require_bootstrap_transport,
)
from three_workflow_delivery_v3.acceptance.ruby_bootstrap_runtime import (
    BOOTSTRAP_WORKFLOW,
    RubyBootstrapPhaseBudget,
)
from three_workflow_delivery_v3.adapters.ruby_project import (
    RubyGemsProjectObservation,
    observe_rubygems_project,
)
from three_workflow_delivery_v3.canonical import (
    JsonValue,
    canonical_sha256,
    canonicalize,
    parse_canonical_json,
)
from three_workflow_delivery_v3.records.artifacts import _digest
from three_workflow_delivery_v3.release.ruby_governance import (
    ruby_publisher_job,
)
from three_workflow_delivery_v3.release.ruby_publication import _instant
from three_workflow_delivery_v3.release.ruby_readback import (
    replay_ruby_observation,
)

if TYPE_CHECKING:
    from collections.abc import Callable
    from datetime import datetime

    from three_workflow_delivery_v3.acceptance import (
        ruby_bootstrap_preparation,
    )
    from three_workflow_delivery_v3.adapters.ruby import (
        RubyDistribution,
    )
    from three_workflow_delivery_v3.adapters.ruby_registry import (
        RubyRegistryObservation,
        RubyRegistryReader,
    )
    from three_workflow_delivery_v3.records.artifacts import (
        ArtifactReference,
        ArtifactTransportIdentity,
    )
    from three_workflow_delivery_v3.records.ruby import RubyArtifact


PREPARER = "prepare-ruby-bootstrap-publication"


@dataclass(frozen=True, slots=True)
class RubyBootstrapPublication:
    """One qualified original transported from this exact bootstrap run."""

    qualification: ruby_bootstrap_preparation.RubyBootstrapQualification
    qualification_reference: ArtifactReference
    qualification_transport: ArtifactTransportIdentity
    payload: bytes = field(repr=False)

    def __post_init__(self) -> None:
        """Reject incomplete qualification and substituted originals."""
        require_bootstrap(
            self.qualification.result == "passed",
            "Ruby bootstrap Qualification did not pass",
        )
        require_bootstrap_transport(
            self.run,
            self.qualification_reference,
            self.qualification_transport,
            canonicalize(self.qualification.to_document()),
            producer="finalize-ruby-bootstrap-qualification",
            payload_path="qualification.json",
        )
        require_bootstrap_transport(
            self.run,
            self.artifact.reference,
            self.artifact.transport,
            self.payload,
            producer="build-ruby",
            payload_path=self.artifact.filename,
        )
        self.artifact.inspect(self.payload)

    @property
    def run(self) -> RubyBootstrapRun:
        """Return the joined first-project request and native run."""
        return self.qualification.plan.run

    @property
    def artifact(self) -> RubyArtifact:
        """Return the one original shared by every passed obligation."""
        return self.qualification.evidence[0].artifact

    @property
    def original(self) -> RubyDistribution:
        """Reinspect original bytes without rebuilding or target evaluation."""
        return self.artifact.inspect(self.payload)

    def to_document(self) -> dict[str, JsonValue]:
        """Bind original, qualification and immutable current-run edges."""
        return {
            "schema": "workflow-delivery/v3/ruby-bootstrap-publication-v1",
            "run-binding-digest": self.run.binding_digest,
            "qualification-reference": (
                self.qualification_reference.to_document()
            ),
            "artifact": self.artifact.to_document(),
            "profile-digest": (
                self.run.inputs.configuration.registry.profile_digest
            ),
        }


@dataclass(frozen=True, slots=True)
class RubyBootstrapAbsence:
    """Whole-project admission and freshly missing native coordinate."""

    publication: RubyBootstrapPublication
    phase: str
    native: RubyRegistryObservation
    project: RubyGemsProjectObservation | None
    started_at: datetime
    observed_at: datetime

    def __post_init__(self) -> None:
        """Replay original responses; scalar missing claims cannot authorize."""
        run = self.publication.run
        run.require_current(self.started_at)
        run.require_current(self.observed_at)
        require_bootstrap(
            self.started_at <= self.observed_at,
            "Ruby bootstrap absence clock regressed",
        )
        require_bootstrap(
            self.phase in {"eligibility", "pre-marker"},
            "Invalid Ruby bootstrap absence phase",
        )
        native = replay_ruby_observation(
            self.native.to_document(),
            self.publication.original,
            run.inputs.configuration.registry,
        )
        require_bootstrap(
            native.classification == "missing"
            and native.failure_kind is None
            and native.distribution is None,
            "Ruby bootstrap coordinate is not freshly missing",
        )
        github = native.registry.name == "github-packages"
        require_bootstrap(
            (github and self.project is None)
            or (
                not github
                and self.project is not None
                and self.project.classification == "absent"
            ),
            "Ruby bootstrap whole-project absence is unavailable",
        )

    def to_document(self) -> dict[str, JsonValue]:
        """Retain independent owner attestation or public direct lookup."""
        return {
            "schema": "workflow-delivery/v3/ruby-bootstrap-absence-v1",
            "publication-digest": canonical_sha256(
                self.publication.to_document()
            ),
            "phase": self.phase,
            "first-project-inspection-digest": ruby_digest(
                self.publication.run.inputs.inspection.content
            ),
            "project": None
            if self.project is None
            else self.project.to_document(),
            "native": self.native.to_document(),
            "started-at": _instant(self.started_at),
            "observed-at": _instant(self.observed_at),
            "producer": PREPARER
            if self.phase == "eligibility"
            else ruby_publisher_job(
                self.publication.run.inputs.configuration.registry,
                bootstrap=True,
            ),
        }


def require_bootstrap_phase(
    publication: RubyBootstrapPublication,
    reader: RubyRegistryReader,
    phase: str,
) -> RubyBootstrapPhaseBudget:
    """Reject a free, different-run or wrongly allocated registry budget."""
    budget = reader.budget
    require_bootstrap(
        type(budget) is RubyBootstrapPhaseBudget
        and budget.run.binding_digest == publication.run.binding_digest
        and budget.phase == phase
        and reader.registry == publication.run.inputs.configuration.registry,
        "Ruby bootstrap registry reader lacks its exact durable partition",
    )
    return cast("RubyBootstrapPhaseBudget", budget)


def observe_ruby_bootstrap_absence(  # noqa: PLR0913 - phase authority and evidence
    publication: RubyBootstrapPublication,
    reader: RubyRegistryReader,
    *,
    phase: str,
    clock: Callable[[], datetime],
    check_authority: Callable[[], dict[str, JsonValue]],
    authorization: RubyBootstrapAuthorization | None = None,
) -> RubyBootstrapAbsence:
    """Check native authority before spending the bounded absence partition.

    check_authority is the reviewed caller's actual environment/protected-main
    guard, normally check_ruby_bootstrap_authority. A supplied digest alone is
    not a fresh authority check. GitHub's complete owner inventory remains the
    independently admitted operator evidence; native indexes do not replace it.
    """
    require_bootstrap(
        phase in {"eligibility", "pre-marker"},
        "Invalid Ruby bootstrap absence phase",
    )
    budget = require_bootstrap_phase(publication, reader, phase)
    started = clock()
    publication.run.require_current(started)
    require_bootstrap(
        (phase == "eligibility" and authorization is None)
        or (
            phase == "pre-marker"
            and authorization is not None
            and authorization.completed_at <= started
            and canonicalize(
                authorization.bundle.absence.publication.to_document()
            )
            == canonicalize(publication.to_document())
        ),
        "Ruby bootstrap pre-marker checks require current Authorization",
    )
    check_authority()
    publication.run.require_current(clock())
    project = None
    if reader.registry.name == "rubygems":
        project = observe_rubygems_project(budget)
        require_bootstrap(
            project.classification == "absent", "RubyGems project is not absent"
        )
    native = reader.observe(publication.original)
    return RubyBootstrapAbsence(
        publication, phase, native, project, started, clock()
    )


def render_ruby_bootstrap_summary(absence: RubyBootstrapAbsence) -> bytes:
    """Show the complete first-project effect before Environment approval."""
    publication = absence.publication
    run = publication.run
    return (
        "\n".join(
            [
                "Ruby first-project bootstrap approval",
                f"Target and control: {run.target}",
                f"Run: {run.run_id}; attempt: 1",
                f"Request: {run.request.digest}",
                f"Slot: {run.request.document['slot']}",
                f"Destination: {run.inputs.configuration.registry.origin}",
                f"Environment: {run.inputs.configuration.registry.environment}",
                f"Version: {publication.artifact.witness.nbgv.native_version}",
                (
                    f"Original: {publication.artifact.filename} "
                    f"{publication.artifact.reference.payload_digest}"
                ),
                (
                    "Qualification: "
                    f"{publication.qualification_reference.payload_digest}"
                ),
                f"Absence: {canonical_sha256(absence.to_document())}",
                (
                    "Create the selected gem once. RubyGems uses one "
                    "pending-publisher OIDC exchange."
                ),
                (
                    "Only HTTP 200 permits bounded original-byte "
                    "visibility checks. "
                    "Rejection or ambiguity remains failure."
                ),
                (
                    "No retry, rerun, credential fallback, deletion, rollback "
                    "or normal Live completion."
                ),
            ]
        )
        + "\n"
    ).encode()


@dataclass(frozen=True, slots=True)
class RubyBootstrapApprovalBundle:
    """Originals, absence and summary uploaded before approval."""

    absence: RubyBootstrapAbsence
    absence_reference: ArtifactReference
    absence_transport: ArtifactTransportIdentity
    summary_reference: ArtifactReference
    summary_transport: ArtifactTransportIdentity

    def __post_init__(self) -> None:
        """Bind both raw immutable current-run preparation artifacts."""
        require_bootstrap(
            self.absence.phase == "eligibility",
            "Ruby bootstrap approval requires eligibility",
        )
        run = self.absence.publication.run
        require_bootstrap_transport(
            run,
            self.absence_reference,
            self.absence_transport,
            canonicalize(self.absence.to_document()),
            producer=PREPARER,
            payload_path="absence.json",
        )
        require_bootstrap_transport(
            run,
            self.summary_reference,
            self.summary_transport,
            render_ruby_bootstrap_summary(self.absence),
            producer=PREPARER,
            payload_path="approval-summary.txt",
        )

    def to_document(self) -> dict[str, JsonValue]:
        """Keep bootstrap approval distinct from a normal Release snapshot."""
        return {
            "schema": "workflow-delivery/v3/ruby-bootstrap-approval-bundle-v1",
            "run-binding-digest": self.absence.publication.run.binding_digest,
            "publication": self.absence.publication.to_document(),
            "absence-reference": self.absence_reference.to_document(),
            "summary-reference": self.summary_reference.to_document(),
            "producer": PREPARER,
        }


@dataclass(frozen=True, slots=True)
class RubyBootstrapAuthorization:
    """Current native bootstrap approval, persisted before credential access."""

    bundle: RubyBootstrapApprovalBundle
    bundle_reference: ArtifactReference
    bundle_transport: ArtifactTransportIdentity
    approval_evidence: bytes
    completed_at: datetime

    def __post_init__(self) -> None:
        """Close exact Environment, workflow, reviewer and original lineage."""
        run = self.bundle.absence.publication.run
        run.require_current(self.completed_at)
        require_bootstrap_transport(
            run,
            self.bundle_reference,
            self.bundle_transport,
            canonicalize(self.bundle.to_document()),
            producer=PREPARER,
            payload_path="approval-bundle.json",
        )
        registry = run.inputs.configuration.registry
        config = ruby_object(
            run.inputs.configuration.document["github-controls"]
        )
        expected: dict[str, JsonValue] = {
            "repository": "hcoona/three",
            "run-id": run.run_id,
            "run-attempt": 1,
            "target": run.target,
            "workflow": BOOTSTRAP_WORKFLOW,
            "environment": registry.environment,
            "environment-id": config["environment-id"],
            "reviewer-id": 712433,
            "reviewer": "hcoona",
            "state": "approved",
            "sentinel": registry.environment + "/v1",
        }
        proof = ruby_object(
            parse_canonical_json(self.approval_evidence),
            {*expected, "deployment-id", "native-response-digest"},
        )
        require_bootstrap(
            all(
                canonicalize(proof[key]) == canonicalize(value)
                for key, value in expected.items()
            )
            and type(proof["deployment-id"]) is int
            and cast("int", proof["deployment-id"]) > 0
            and self.completed_at >= self.bundle.absence.observed_at,
            "Ruby bootstrap native approval binding or order changed",
        )
        _digest(
            proof["native-response-digest"], field="bootstrap native approval"
        )

    def to_document(self) -> dict[str, JsonValue]:
        """Retain authority without any assertion or publishing credential."""
        return {
            "schema": "workflow-delivery/v3/ruby-bootstrap-authorization-v1",
            "run-binding-digest": (
                self.bundle.absence.publication.run.binding_digest
            ),
            "bundle-reference": self.bundle_reference.to_document(),
            "approval-evidence": parse_canonical_json(self.approval_evidence),
            "completed-at": _instant(self.completed_at),
            "producer": ruby_publisher_job(
                self.bundle.absence.publication.run.inputs.configuration.registry,
                bootstrap=True,
            ),
        }


@dataclass(frozen=True, slots=True)
class RubyBootstrapMarker:
    """Postapproval missing state over one immutable Authorization."""

    authorization: RubyBootstrapAuthorization
    authorization_reference: ArtifactReference
    authorization_transport: ArtifactTransportIdentity
    absence: RubyBootstrapAbsence

    def __post_init__(self) -> None:
        """Reject a changed original or absence predating actual approval."""
        publication = self.authorization.bundle.absence.publication
        require_bootstrap_transport(
            publication.run,
            self.authorization_reference,
            self.authorization_transport,
            canonicalize(self.authorization.to_document()),
            producer=ruby_publisher_job(
                publication.run.inputs.configuration.registry, bootstrap=True
            ),
            payload_path="authorization.json",
        )
        require_bootstrap(
            self.absence.phase == "pre-marker"
            and canonicalize(self.absence.publication.to_document())
            == canonicalize(publication.to_document())
            and self.absence.started_at >= self.authorization.completed_at,
            "Ruby bootstrap pre-marker original or authority drifted",
        )

    @property
    def publication(self) -> RubyBootstrapPublication:
        """Return the exact qualified original authorized for one action."""
        return self.absence.publication

    def to_document(self) -> dict[str, JsonValue]:
        """A persisted marker without Result remains possibly mutated."""
        return {
            "schema": (
                "workflow-delivery/v3/"
                "ruby-bootstrap-mutation-may-have-started-v1"
            ),
            "run-binding-digest": self.publication.run.binding_digest,
            "authorization-reference": (
                self.authorization_reference.to_document()
            ),
            "pre-state": self.absence.to_document(),
            "producer": ruby_publisher_job(
                self.publication.run.inputs.configuration.registry,
                bootstrap=True,
            ),
        }
