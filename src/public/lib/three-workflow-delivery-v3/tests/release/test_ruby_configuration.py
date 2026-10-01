"""Closed first-project state never substitutes for normal admission."""

from datetime import timedelta

import pytest
from three_workflow_delivery_v3._ruby_native import ruby_digest
from three_workflow_delivery_v3.adapters.rubygems import RubyRegistry
from three_workflow_delivery_v3.canonical import canonicalize
from three_workflow_delivery_v3.release.ruby_configuration import (
    RubyBootstrapConfiguration,
    blocked_ruby_bootstrap_configuration,
)

from ..ruby_integration_fixtures import NOW
from .ruby_configuration_fixtures import configuration, configuration_document
from .ruby_operation_fixtures import DIGEST, instant


@pytest.mark.parametrize(
    ("destination", "account", "state"),
    [
        ("github-packages", "hcoona", "associated-creation"),
        ("rubygems", "Raffle2282", "pending-publisher"),
    ],
)
@pytest.mark.parametrize("bypass", [False, True])
def test_configuration_accepts_exact_bootstrap_tuple_and_actual_bypass(
    destination, account, state, bypass
):
    """Configuration accepts exact bootstrap tuple and actual bypass."""
    doc = configuration_document(destination)
    doc["github-controls"]["can-admins-bypass"] = bypass
    config = RubyBootstrapConfiguration(
        RubyRegistry(destination), canonicalize(doc)
    )
    config.require_current(NOW)
    assert config.document == doc
    assert config.digest == ruby_digest(canonicalize(doc))
    registration = config.document["first-project-registration"]
    github = destination == "github-packages"
    assert registration == {
        "repository": "hcoona/three",
        "owner-id": 712433,
        "project": "hcoona-release-smoke-ruby",
        "registry": "https://rubygems.pkg.github.com/hcoona"
        if github
        else "https://rubygems.org",
        "authentication": "github-job-token" if github else "ruby-oidc",
        "audience": None if github else "rubygems.org",
        "workflow": "workflow-delivery-v3-ruby-bootstrap.yml",
        "environment": "workflow-delivery-v3-ruby-" + destination,
        "selected-ref": "refs/heads/main",
        "account": account,
        "state": state,
    }
    assert (
        "no existing package ownership, name reservation "
        "or normal Live admission." in config.document["scope-limitation"]
    )
    view = config.document
    view["github-controls"]["can-admins-bypass"] = not bypass
    assert config.document == doc


@pytest.mark.parametrize("destination", ["github-packages", "rubygems"])
def test_disabled_configuration_is_wholly_disabled(destination):
    """Disabled configuration is wholly disabled."""
    registry = RubyRegistry(destination)
    doc = blocked_ruby_bootstrap_configuration(registry)
    config = RubyBootstrapConfiguration(registry, canonicalize(doc))
    assert config.document == doc
    with pytest.raises(ValueError, match="unavailable or stale"):
        config.require_current(NOW)
    ready = configuration_document(destination)
    for field in (
        "source-evidence-revision",
        "protocol-digest",
        "inspected-at",
        "expires-at",
        "github-controls",
        "first-project-registration",
        "attestation-digest",
    ):
        hybrid = {**doc, field: ready[field]}
        with pytest.raises(ValueError, match="binding differs"):
            RubyBootstrapConfiguration(registry, canonicalize(hybrid))


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("schema", "other"),
        ("state", "ready"),
        ("binding", {}),
        ("operation-profile-digest", DIGEST),
        ("scope-limitation", "normal ownership"),
        ("source-evidence-revision", "main"),
        ("source-evidence-revision", "A" * 40),
        ("protocol-digest", "sha256:" + "A" * 64),
        ("attestation-digest", "bad"),
        ("first-project-registration", {}),
    ],
)
def test_configuration_rejects_authority_and_identity_substitution(
    field, value
):
    """Configuration rejects authority and identity substitution."""
    doc = configuration_document()
    doc[field] = value
    with pytest.raises(ValueError, match=r"Ruby|not canonical"):
        RubyBootstrapConfiguration(RubyRegistry("rubygems"), canonicalize(doc))


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("reviewer-id", 1),
        ("prevent-self-review", 0),
        ("wait-timer", False),
        ("protected-main-only", 1),
        ("sentinel", "foreign/v1"),
        ("secret-count", False),
        ("accepted-writers", ["hcoona", "foreign"]),
        ("control-baseline-digest", "bad"),
        *[
            (key, value)
            for key in (
                "environment-id",
                "reviewer-rule-id",
                "deployment-branch-rule-id",
            )
            for value in (0, -1, True, "101")
        ],
        *[("can-admins-bypass", value) for value in (0, 1, "false", None)],
    ],
)
def test_configuration_rejects_non_native_or_changed_controls(field, value):
    """Configuration rejects non native or changed controls."""
    doc = configuration_document()
    doc["github-controls"][field] = value
    with pytest.raises(ValueError, match=r"Ruby|not canonical"):
        RubyBootstrapConfiguration(RubyRegistry("rubygems"), canonicalize(doc))


@pytest.mark.parametrize(
    "scope", ["top", "github-controls", "first-project-registration", "binding"]
)
@pytest.mark.parametrize("mode", ["extra", "missing"])
def test_configuration_schema_is_closed_at_each_authority(scope, mode):
    """Configuration schema is closed at each authority."""
    doc = configuration_document()
    selected = doc if scope == "top" else doc[scope]
    if mode == "extra":
        selected["unknown"] = None
    else:
        selected.pop(
            "can-admins-bypass"
            if scope == "github-controls"
            else next(iter(selected))
        )
    with pytest.raises(ValueError, match=r"Ruby|not canonical"):
        RubyBootstrapConfiguration(RubyRegistry("rubygems"), canonicalize(doc))


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("account", "another-owner"),
        ("workflow", "workflow-delivery-v3-ruby-smoke.yml"),
        ("state", "owned"),
        ("repository", "hcoona/other"),
        ("owner-id", 712434),
        ("project", "other-gem"),
        ("registry", "https://rubygems.pkg.github.com/hcoona"),
        ("authentication", "github-job-token"),
        ("audience", None),
        ("environment", "workflow-delivery-v3-ruby-github-packages"),
        ("selected-ref", "refs/heads/other"),
    ],
)
def test_configuration_rejects_normal_or_foreign_registration(field, value):
    """Configuration rejects normal or foreign registration."""
    doc = configuration_document()
    doc["first-project-registration"][field] = value
    with pytest.raises(ValueError, match="binding differs"):
        RubyBootstrapConfiguration(RubyRegistry("rubygems"), canonicalize(doc))


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("inspected-at", NOW.isoformat()),
        ("inspected-at", NOW.replace(tzinfo=None).isoformat()),
        ("expires-at", instant(NOW)),
        ("expires-at", instant(NOW - timedelta(seconds=1))),
        ("expires-at", instant(NOW + timedelta(days=7, microseconds=1))),
    ],
)
def test_configuration_requires_utc_and_at_most_seven_days(field, value):
    """Configuration requires utc and at most seven days."""
    doc = configuration_document()
    doc[field] = value
    with pytest.raises(ValueError, match=r"Ruby|not canonical"):
        RubyBootstrapConfiguration(RubyRegistry("rubygems"), canonicalize(doc))


@pytest.mark.parametrize(
    "offset", [timedelta(0), timedelta(days=7, microseconds=-1)]
)
def test_configuration_current_interval_includes_start_excludes_expiry(offset):
    """Configuration current interval includes start excludes expiry."""
    config = configuration()
    before = config.content
    config.require_current(NOW + offset)
    assert config.content == before
    assert config.document["expires-at"] == instant(NOW + timedelta(days=7))


@pytest.mark.parametrize(
    "now",
    [
        NOW - timedelta(microseconds=1),
        NOW + timedelta(days=7),
        NOW.replace(tzinfo=None),
    ],
)
def test_configuration_rejects_not_yet_valid_expired_and_naive_now(now):
    """Configuration rejects not yet valid expired and naive now."""
    with pytest.raises(ValueError, match="unavailable or stale"):
        configuration().require_current(now)


def test_configuration_rejects_noncanonical_bytes():
    """Configuration rejects noncanonical bytes."""
    with pytest.raises(ValueError, match=r"Ruby|not canonical"):
        RubyBootstrapConfiguration(
            RubyRegistry("rubygems"),
            canonicalize(configuration_document()) + b"\n",
        )
