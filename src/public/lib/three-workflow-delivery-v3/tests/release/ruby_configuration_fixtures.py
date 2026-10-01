"""Synthetic configuration and independent-review inputs, never actual trust."""

from datetime import timedelta

from three_workflow_delivery_v3._ruby_native import ruby_digest
from three_workflow_delivery_v3.adapters.rubygems import RubyRegistry
from three_workflow_delivery_v3.canonical import canonicalize
from three_workflow_delivery_v3.release.ruby_configuration import (
    RubyBootstrapConfiguration,
    RubyFirstProjectInspection,
    blocked_ruby_bootstrap_configuration,
    ruby_bootstrap_registration,
)
from three_workflow_delivery_v3.release.ruby_operation import (
    RubyOperationEnvelope,
    RubyOperationRequest,
)

from ..ruby_integration_fixtures import NOW, TARGET
from .ruby_operation_fixtures import (
    DIGEST,
    GENERATION,
    admission_document,
    envelope_document,
    instant,
    request_document,
)


def configuration_document(destination="rubygems"):
    """Model seven-day controls and registration without observing a service."""
    registry = RubyRegistry(destination)
    return {
        **blocked_ruby_bootstrap_configuration(registry),
        "state": "first-project-ready",
        "source-evidence-revision": TARGET,
        "protocol-digest": DIGEST,
        "inspected-at": instant(NOW),
        "expires-at": instant(NOW + timedelta(days=7)),
        "github-controls": {
            "reviewer-id": 712433,
            "prevent-self-review": False,
            "wait-timer": 0,
            "protected-main-only": True,
            "sentinel": registry.environment + "/v1",
            "secret-count": 0,
            "accepted-writers": ["hcoona"],
            "environment-id": 101,
            "reviewer-rule-id": 102,
            "deployment-branch-rule-id": 103,
            "can-admins-bypass": False,
            "control-baseline-digest": DIGEST,
        },
        "first-project-registration": ruby_bootstrap_registration(registry),
        "attestation-digest": DIGEST,
    }


def configuration(destination="rubygems"):
    """Validate coherent synthetic configuration through the public API."""
    return RubyBootstrapConfiguration(
        RubyRegistry(destination),
        canonicalize(configuration_document(destination)),
    )


def inspection_document(config):
    """Model exact target and finite absence facts without a request hash."""
    github = config.registry.name == "github-packages"
    return {
        "schema": "workflow-delivery/v3/ruby-first-project-inspection-v1",
        "destination": config.registry.name,
        "slot": config.registry.name + "-bootstrap",
        "generation": GENERATION,
        "target": TARGET,
        "configuration-digest": config.digest,
        "control-baseline-digest": DIGEST,
        "operator-principal": {"login": "hcoona", "id": 712433}
        if github
        else {"kind": "unauthenticated-public-endpoint"},
        "absence-kind": "github-owner-package-inventory"
        if github
        else "rubygems-project-owners",
        "absence-verdict": "absent",
        "evidence-manifest-digest": DIGEST,
        "observed-at": instant(NOW),
        "dispatch-by": instant(NOW + timedelta(minutes=10)),
        "expires-at": instant(NOW + timedelta(hours=2)),
    }


def inspection(config):
    """Bind modeled evidence to its validated configuration."""
    return RubyFirstProjectInspection(
        canonicalize(inspection_document(config)), config
    )


def bootstrap_request(
    config, *, envelope_changes=None, request_changes=None, slot=None
):
    """Build a coherent envelope and request before testing their joins."""
    selected_slot = slot or config.registry.name + "-bootstrap"
    envelope_doc = envelope_document(selected_slot)
    envelope_doc["slots"][selected_slot].update(
        {"configuration-digest": config.digest, **(envelope_changes or {})}
    )
    envelope = RubyOperationEnvelope(canonicalize(envelope_doc))
    doc = request_document(envelope, selected_slot)
    doc.update(
        {
            "expires-at": instant(NOW + timedelta(hours=2)),
            **(request_changes or {}),
        }
    )
    return RubyOperationRequest(canonicalize(doc), envelope)


def review_document(request, config, inspected):
    """Model separate review metadata without claiming reviewer provenance."""
    admission = admission_document(request)
    return {
        "schema": "workflow-delivery/v3/ruby-operation-admission-review-v1",
        "request-digest": request.digest,
        "configuration-digest": config.digest,
        "first-project-inspection-digest": ruby_digest(inspected.content),
        "caller-digest": request.document["caller-digest"],
        "protocol-digest": request.document["protocol-digest"],
        **{
            key: admission[key]
            for key in (
                "reviewer",
                "author",
                "reviewed-at",
                "verdict",
                "carrier",
            )
        },
    }
