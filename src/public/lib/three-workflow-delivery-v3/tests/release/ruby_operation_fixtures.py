"""Modeled independent admission and current-run facts; no hosted authority."""

from copy import deepcopy
from datetime import timedelta

from three_workflow_delivery_v3._ruby_native import ruby_digest
from three_workflow_delivery_v3.canonical import canonicalize
from three_workflow_delivery_v3.release.ruby_operation import (
    RubyOperationEnvelope,
    RubyOperationRequest,
    disabled_ruby_operation_envelope,
    ruby_operation_binding,
)
from three_workflow_delivery_v3.repository.ruby_provider import RubyNbgvFacts

from ..ruby_integration_fixtures import NOW, TARGET

DIGEST = "sha256:" + "b" * 64
GENERATION = "c" * 32
REVIEW = b"Synthetic independent test verdict; not actual admission."
SLOT = "rubygems-normal01"


def instant(value):
    """Encode the contract's exact UTC timestamp form."""
    return value.isoformat().replace("+00:00", "Z")


def envelope_document(slot=SLOT, *, version=1):
    """Enable one finite slot only within a local modeled envelope."""
    doc = deepcopy(disabled_ruby_operation_envelope(version=version))
    doc["slots"][slot] = {
        "binding": ruby_operation_binding(slot),
        "generation": GENERATION,
        "not-before": instant(NOW),
        "expires-at": instant(NOW + timedelta(days=7)),
        "configuration-digest": DIGEST,
        "caller-digest": DIGEST,
        "protocol-digest": DIGEST,
        "github-budget-digest": DIGEST,
    }
    return doc


def request_document(envelope, slot=SLOT):
    """Provide internally coherent canonical request fixture fields."""
    selected = envelope.document["slots"][slot]
    facts = RubyNbgvFacts(
        canonicalize(
            {
                "SemVer2": "0.1.0-beta.7",
                "GitCommitId": TARGET,
                "VersionHeight": 7,
                "PublicRelease": True,
            }
        ),
        "0.1.0.pre.beta.7",
    )
    return {
        "schema": "workflow-delivery/v3/ruby-operation-request-v1",
        "slot": slot,
        **{
            key: selected[key]
            for key in (
                "generation",
                "binding",
                "configuration-digest",
                "caller-digest",
                "protocol-digest",
                "github-budget-digest",
            )
        },
        "target": TARGET,
        "control": TARGET,
        "tree": "d" * 40,
        "nbgv": facts.to_document(),
        "source-manifest-digest": DIGEST,
        "envelope-digest": ruby_digest(envelope.content),
        "issued-at": instant(NOW),
        "expires-at": instant(NOW + timedelta(hours=4)),
    }


def request(slot=SLOT):
    """Create a validated fixture request for either fixed destination mode."""
    envelope = RubyOperationEnvelope(canonicalize(envelope_document(slot)))
    return RubyOperationRequest(
        canonicalize(request_document(envelope, slot)), envelope
    )


def admission_document(selected, ledger_digest=DIGEST, review=REVIEW):
    """Bind two distinct modeled review identities and exact local bytes."""
    return {
        "schema": "workflow-delivery/v3/ruby-operation-admission-v1",
        "request-digest": selected.digest,
        "caller-digest": selected.document["caller-digest"],
        "review-digest": ruby_digest(review),
        "reviewer": "fixture-independent-reviewer",
        "author": "fixture-request-author",
        "verdict": "admitted",
        "carrier": "https://github.com/hcoona/three/issues/954#issuecomment-123",
        "reviewed-at": instant(NOW + timedelta(minutes=1)),
        "ledger-digest": ledger_digest,
    }


def current_environment(selected):
    """Model platform observations independently from mutable ambient env."""
    return {
        "GITHUB_REPOSITORY": "hcoona/three",
        "GITHUB_REF": "refs/heads/main",
        "GITHUB_EVENT_NAME": "workflow_dispatch",
        "GITHUB_ACTOR": "hcoona",
        "GITHUB_ACTOR_ID": "712433",
        "GITHUB_TRIGGERING_ACTOR": "hcoona",
        "GITHUB_RUN_ATTEMPT": "1",
        "GITHUB_RUN_ID": "991",
        "GITHUB_SHA": TARGET,
        "GITHUB_WORKFLOW_SHA": TARGET,
        "GITHUB_WORKFLOW_REF": "hcoona/three/.github/workflows/"
        + selected.document["binding"]["workflow"]
        + "@refs/heads/main",
    }
