"""Reconstruct hosted Ruby records through strict domain constructors."""

from __future__ import annotations

from datetime import datetime
from typing import TYPE_CHECKING, cast

from three_workflow_delivery_v3._ruby_native import ruby_object, ruby_text
from three_workflow_delivery_v3.acceptance.ruby_bootstrap_contract import (
    require_bootstrap,
)
from three_workflow_delivery_v3.acceptance.ruby_bootstrap_execution import (
    RubyBootstrapResult,
)
from three_workflow_delivery_v3.acceptance.ruby_bootstrap_preparation import (
    RubyBootstrapPlan,
    RubyBootstrapQualification,
    ruby_bootstrap_evidence_from_document,
)
from three_workflow_delivery_v3.acceptance.ruby_bootstrap_publication import (
    RubyBootstrapAbsence,
    RubyBootstrapApprovalBundle,
    RubyBootstrapAuthorization,
    RubyBootstrapMarker,
    RubyBootstrapPublication,
)
from three_workflow_delivery_v3.adapters.ruby_project import (
    RubyGemsProjectObservation,
)
from three_workflow_delivery_v3.canonical import JsonValue, canonicalize
from three_workflow_delivery_v3.records.ruby import (
    RubyArtifact,
    ruby_artifact_from_document,
)
from three_workflow_delivery_v3.release.ruby_audit import (
    ruby_approval_bundle_from_document,
    ruby_authorization_from_document,
    ruby_marker_from_document,
    ruby_publication_result_from_document,
    ruby_publication_snapshot_from_document,
    ruby_remote_observation_from_document,
)
from three_workflow_delivery_v3.release.ruby_governance import RubyGovernance
from three_workflow_delivery_v3.release.ruby_qualification import (
    RubyQualificationDecision,
    RubyQualificationSnapshot,
    admit_ruby_qualification_decision,
    ruby_qualification_evidence_from_document,
    ruby_qualification_snapshot_from_document,
)
from three_workflow_delivery_v3.release.ruby_readback import (
    replay_ruby_observation,
    ruby_response_from_document,
)
from three_workflow_delivery_v3.repository.ruby_model import (
    RubyRepositoryModelSnapshot,
    ruby_repository_model_from_document,
)

if TYPE_CHECKING:
    from three_workflow_delivery_v3.release.ruby_execution import (
        RubyPublicationResult,
    )
    from three_workflow_delivery_v3.release.ruby_publication import (
        RubyApprovalBundle,
        RubyMutationMarker,
        RubyPublicationAuthorization,
        RubyPublicationSnapshot,
        RubyRemoteObservation,
    )
    from three_workflow_delivery_v3.ruby_operation_host import (
        HostedRubyArtifacts,
    )


def same(actual: JsonValue, expected: JsonValue) -> None:
    """Reject additional fields and modified deterministic record content."""
    require_bootstrap(
        canonicalize(actual) == canonicalize(expected),
        "Ruby hosted record differs from its strict predecessors",
    )


def instant(value: JsonValue) -> datetime:
    """Parse explicit aware record time."""
    result = datetime.fromisoformat(ruby_text(value))
    require_bootstrap(
        result.tzinfo is not None, "Ruby record timestamp lacks a timezone"
    )
    return result


class RubyOperationRecords:
    """Reconstruct domain records over exact current artifact edges."""

    def __init__(self, artifacts: HostedRubyArtifacts) -> None:
        """Require complete downloaded input before importing any record."""
        artifacts.validate_all()
        self.inputs = artifacts
        self.operation = artifacts.historical_operation()

    def artifact(self) -> RubyArtifact:
        """Close the original upload against this build's actual producer."""
        result = ruby_artifact_from_document(self.inputs.document("artifacts"))
        require_bootstrap(
            result.reference == self.inputs.reference("gem")
            and result.transport == self.inputs.transport("gem"),
            "Ruby original transport differs from the hosted Build",
        )
        result.inspect(self.inputs.content("gem"))
        return result

    def bootstrap_plan(self) -> RubyBootstrapPlan:
        """Reconstruct frozen first-project plan from its uploaded Provider."""
        inputs = self.inputs
        result = RubyBootstrapPlan(
            self.operation.bootstrap_run(self.operation.admitted_at),
            inputs.content("provider"),
            inputs.reference("provider"),
            inputs.transport("provider"),
        )
        if "plan" in inputs.references:
            same(inputs.document("plan"), result.to_document())
        return result

    def model(self) -> RubyRepositoryModelSnapshot:
        """Reject a normal Model from another request or actual run."""
        model = ruby_repository_model_from_document(
            self.inputs.document("model")
        )
        require_bootstrap(
            model.context == self.operation.context()
            and model.request_reference
            == self.inputs.reference("provider-request")
            and model.provider_reference == self.inputs.reference("provider"),
            "Ruby Model differs from the admitted native request or Provider",
        )
        same(model.provider.to_document(), self.inputs.document("provider"))
        return model

    def normal_plan(self) -> RubyQualificationSnapshot:
        """Reconstruct only normal Release Qualification authority."""
        plan = ruby_qualification_snapshot_from_document(
            self.inputs.document("plan")
        )
        require_bootstrap(
            plan.model == self.model()
            and plan.model_reference == self.inputs.reference("model"),
            "Ruby Qualification plan differs from current Model",
        )
        plan.governance.require_same_live(
            self.operation.governance(plan.governance.observed_at),
            plan.governance.observed_at,
        )
        return plan

    def quality(self) -> list[JsonValue]:
        """Read the exact bootstrap or normal obligation evidence set."""
        doc = ruby_object(self.inputs.document("quality"), {"schema", "items"})
        require_bootstrap(
            doc["schema"] == "workflow-delivery/v3/ruby-hosted-quality-set-v1"
            and isinstance(doc["items"], list),
            "Ruby quality set is malformed",
        )
        return cast("list[JsonValue]", doc["items"])

    def bootstrap_qualification(self) -> RubyBootstrapQualification:
        """Replay every first-project obligation and deterministic verdict."""
        result = RubyBootstrapQualification(
            self.bootstrap_plan(),
            tuple(
                ruby_bootstrap_evidence_from_document(item)
                for item in self.quality()
            ),
        )
        if "qualification" in self.inputs.references:
            same(self.inputs.document("qualification"), result.to_document())
        require_bootstrap(
            all(e.artifact == self.artifact() for e in result.evidence),
            "Bootstrap quality uses a different original",
        )
        return result

    def normal_qualification(self) -> RubyQualificationDecision:
        """Replay normal evidence and its current qualification verdict."""
        evidence = tuple(
            ruby_qualification_evidence_from_document(item)
            for item in self.quality()
        )
        require_bootstrap(
            all(item.artifact == self.artifact() for item in evidence),
            "Ruby normal quality uses a different original",
        )
        if "qualification" in self.inputs.references:
            return admit_ruby_qualification_decision(
                self.inputs.document("qualification"),
                self.normal_plan(),
                evidence,
            )
        return RubyQualificationDecision(self.normal_plan(), evidence)

    def bootstrap_publication(self) -> RubyBootstrapPublication:
        """Select the one independently qualified bootstrap original."""
        return RubyBootstrapPublication(
            self.bootstrap_qualification(),
            self.inputs.reference("qualification"),
            self.inputs.transport("qualification"),
            self.inputs.content("gem"),
        )

    def bootstrap_absence(
        self, value: JsonValue | None = None
    ) -> RubyBootstrapAbsence:
        """Replay project and version absence from their retained responses."""
        doc = ruby_object(
            self.inputs.document("absence") if value is None else value
        )
        publication = self.bootstrap_publication()
        project = None
        if doc["project"] is not None:
            evidence = ruby_object(doc["project"])
            response = evidence["response"]
            project = RubyGemsProjectObservation(
                None
                if response is None
                else ruby_response_from_document(response),
                None
                if evidence["failure"] is None
                else ruby_text(evidence["failure"]),
            )
            same(evidence, project.to_document())
        result = RubyBootstrapAbsence(
            publication,
            ruby_text(doc["phase"]),
            replay_ruby_observation(
                doc["native"], publication.original, self.operation.registry
            ),
            project,
            instant(doc["started-at"]),
            instant(doc["observed-at"]),
        )
        same(doc, result.to_document())
        return result

    def bootstrap_bundle(self) -> RubyBootstrapApprovalBundle:
        """Bind the actual uploaded summary and first-project absence."""
        inputs = self.inputs
        result = RubyBootstrapApprovalBundle(
            self.bootstrap_absence(),
            inputs.reference("absence"),
            inputs.transport("absence"),
            inputs.reference("summary"),
            inputs.transport("summary"),
        )
        if "bundle" in inputs.references:
            same(inputs.document("bundle"), result.to_document())
        return result

    def bootstrap_authorization(self) -> RubyBootstrapAuthorization:
        """Import exact current native approval over this bundle."""
        inputs = self.inputs
        doc = inputs.document("authorization")
        result = RubyBootstrapAuthorization(
            self.bootstrap_bundle(),
            inputs.reference("bundle"),
            inputs.transport("bundle"),
            canonicalize(doc["approval-evidence"]),
            instant(doc["completed-at"]),
        )
        same(doc, result.to_document())
        return result

    def bootstrap_marker(self) -> RubyBootstrapMarker:
        """Reconstruct postapproval absence and persisted Authorization."""
        inputs = self.inputs
        doc = inputs.document("marker")
        result = RubyBootstrapMarker(
            self.bootstrap_authorization(),
            inputs.reference("authorization"),
            inputs.transport("authorization"),
            self.bootstrap_absence(doc["pre-state"]),
        )
        same(doc, result.to_document())
        return result

    def bootstrap_result(self) -> RubyBootstrapResult:
        """Replay exact original visibility before admitting publication."""
        inputs = self.inputs
        doc = inputs.document("result")
        visibility = doc["visibility"]
        require_bootstrap(
            isinstance(visibility, list),
            "Ruby bootstrap visibility is malformed",
        )
        response = doc["response"]
        result = RubyBootstrapResult(
            self.bootstrap_marker(),
            inputs.reference("marker"),
            inputs.transport("marker"),
            ruby_text(doc["status"]),
            None if response is None else ruby_response_from_document(response),
            tuple(
                ruby_object(item)
                for item in cast("list[JsonValue]", visibility)
            ),
            instant(doc["started-at"]),
            cast("float", doc["started-monotonic"]),
            instant(doc["deadline"]),
            None
            if doc["upload-completed-at"] is None
            else instant(doc["upload-completed-at"]),
            cast("float | None", doc["upload-completed-monotonic"]),
            None if doc["error-kind"] is None else ruby_text(doc["error-kind"]),
        )
        same(doc, result.to_document())
        return result

    def normal_observation(self) -> RubyRemoteObservation:
        """Admit exact native state against the qualified normal original."""
        result = ruby_remote_observation_from_document(
            self.inputs.document("observation"),
            self.normal_qualification(),
            self.inputs.content("gem"),
        )
        require_bootstrap(
            result.decision_reference == self.inputs.reference("qualification"),
            "Ruby observation differs from the actual Qualification artifact",
        )
        return result

    def normal_publication(self) -> RubyPublicationSnapshot:
        """Replay the normal zero-or-one-action decision."""
        result = ruby_publication_snapshot_from_document(
            self.inputs.document("publication"), self.normal_observation()
        )
        require_bootstrap(
            result.observation_reference
            == self.inputs.reference("observation"),
            "Ruby publication differs from the actual Observation artifact",
        )
        return result

    def normal_bundle(self) -> RubyApprovalBundle:
        """Reconstruct the approval summary over the singular action."""
        result = ruby_approval_bundle_from_document(
            self.inputs.document("bundle"), self.normal_publication()
        )
        require_bootstrap(
            result.snapshot_reference == self.inputs.reference("publication")
            and result.summary_reference == self.inputs.reference("summary")
            and result.summary == self.inputs.content("summary"),
            "Ruby bundle differs from its actual publication or summary",
        )
        return result

    def normal_authorization(self) -> RubyPublicationAuthorization:
        """Replay strict normal native current-run approval."""
        result = ruby_authorization_from_document(
            self.inputs.document("authorization"), self.normal_bundle()
        )
        require_bootstrap(
            result.bundle_reference == self.inputs.reference("bundle"),
            "Ruby authorization differs from the actual approval bundle",
        )
        return result

    def fresh_governance(self) -> RubyGovernance:
        """Import retained protected-main bytes without pretending freshness."""
        doc = ruby_object(
            self.inputs.document("fresh-governance"),
            {"content", "source-commit", "observed-at"},
        )
        return RubyGovernance(
            self.operation.registry,
            canonicalize(doc["content"]),
            ruby_text(doc["source-commit"]),
            instant(doc["observed-at"]),
        )

    def normal_marker(self) -> RubyMutationMarker:
        """Replay missing pre-state and its exact current approval."""
        result = ruby_marker_from_document(
            self.inputs.document("marker"),
            self.normal_authorization(),
            self.fresh_governance(),
            self.inputs.content("gem"),
        )
        require_bootstrap(
            result.authorization_reference
            == self.inputs.reference("authorization"),
            "Ruby marker differs from the actual authorization artifact",
        )
        return result

    def normal_result(self) -> RubyPublicationResult:
        """Use the existing strict normal terminal audit, not scalar JSON."""
        result = ruby_publication_result_from_document(
            self.inputs.document("result"),
            self.normal_marker(),
            self.inputs.content("gem"),
        )
        require_bootstrap(
            result.marker_reference == self.inputs.reference("marker"),
            "Ruby result differs from the actual marker artifact",
        )
        return result
