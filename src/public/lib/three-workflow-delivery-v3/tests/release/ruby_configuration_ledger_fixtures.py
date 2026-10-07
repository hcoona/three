"""Local durable fixtures; modeled evidence never creates actual authority."""

from datetime import timedelta

from three_workflow_delivery_v3.canonical import canonicalize
from three_workflow_delivery_v3.release.ruby_configuration_ledger import (
    RubyConfigurationLedger,
    RubyConfigurationPlan,
    configuration_phase_limits,
    initialize_ruby_configuration_ledger,
)

from ..ruby_integration_fixtures import NOW, TARGET
from .ruby_operation_fixtures import DIGEST, GENERATION, instant

EVIDENCE = canonicalize({"fixture": "sanitized controlled response"})
REPOSITORY = "/repos/hcoona/three"


def plan_document(directory, destination="github-packages", *, now=NOW):
    """Select exact canonical local plan inputs for the public validator."""
    return {
        "schema": "workflow-delivery/v3/ruby-configuration-plan-v1",
        "campaign": GENERATION,
        "destination": destination,
        "directory": str(directory),
        "source-commit": TARGET,
        "caller-digest": DIGEST,
        "protocol-digest": DIGEST,
        "issued-at": instant(now),
        "expires-at": instant(now + timedelta(days=7)),
        "phases": configuration_phase_limits(destination),
    }


def ledger(directory, destination="github-packages", *, now=NOW, **changes):
    """Initialize one fresh ledger through the actual plan validator."""
    doc = plan_document(directory, destination, now=now)
    doc.update(changes)
    plan = RubyConfigurationPlan(canonicalize(doc))
    initialize_ruby_configuration_ledger(plan, now=now)
    return RubyConfigurationLedger(plan)


def complete_get(phase, path="/user", *, now=NOW):
    """Retain one successful synthetic read through the actual durable API."""
    reservation = phase.spend("GET", path, None, now=now)
    phase.complete_send(reservation, EVIDENCE, successful=True, now=now)
    return reservation


def mutation(destination, role, *, update=False):
    """Declare the independently expected protocol's exact write tuples."""
    environment = "workflow-delivery-v3-ruby-" + destination
    path = REPOSITORY + "/environments/" + environment
    if role == "environment-write":
        return (
            "PUT",
            path,
            canonicalize(
                {
                    "wait_timer": 0,
                    "prevent_self_review": False,
                    "reviewers": [{"type": "User", "id": 712433}],
                    "deployment_branch_policy": {
                        "protected_branches": False,
                        "custom_branch_policies": True,
                    },
                }
            ),
        )
    if role == "branch-write":
        return (
            "POST",
            path + "/deployment-branch-policies",
            canonicalize({"name": "main", "type": "branch"}),
        )
    if update:
        return (
            "PATCH",
            path + "/variables/WDV3_APPROVAL_ENVIRONMENT_MARKER",
            canonicalize({"value": environment + "/v1"}),
        )
    return (
        "POST",
        path + "/variables",
        canonicalize(
            {
                "name": "WDV3_APPROVAL_ENVIRONMENT_MARKER",
                "value": environment + "/v1",
            }
        ),
    )
