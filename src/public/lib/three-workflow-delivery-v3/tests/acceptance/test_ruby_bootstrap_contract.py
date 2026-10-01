"""Exact bootstrap run admission and immutable current-run contract tests."""

from dataclasses import replace
from datetime import timedelta

import pytest
from three_workflow_delivery_v3._ruby_native import ruby_digest
from three_workflow_delivery_v3.acceptance.ruby_bootstrap_contract import (
    admit_ruby_bootstrap,
    require_bootstrap_transport,
    ruby_bootstrap_source_digest,
    validate_bootstrap_provider,
)
from three_workflow_delivery_v3.canonical import (
    canonical_sha256,
    canonicalize,
    parse_canonical_json,
)
from three_workflow_delivery_v3.repository.ruby_provider import RubyNbgvFacts

from ..release.ruby_configuration_fixtures import configuration
from ..release.ruby_operation_fixtures import request
from .ruby_bootstrap_fixtures import CURRENT, EXPIRED, modeled_plan


@pytest.fixture
def plan(tmp_path):
    """Use fresh modeled admission and a real local sole-use reservation."""
    return modeled_plan(tmp_path)


@pytest.mark.parametrize("destination", ["github-packages", "rubygems"])
def test_run_binds_bootstrap_chain_and_screened_provider_identity(
    tmp_path, destination
):
    """Bind the complete chain, selected destination and actual run."""
    run = modeled_plan(tmp_path, destination).run
    doc = run.to_document()
    assert doc == {
        "schema": "workflow-delivery/v3/ruby-bootstrap-run-v1",
        "purpose": "destination-bootstrap",
        "request-digest": run.request.digest,
        "configuration-digest": run.inputs.configuration.digest,
        "inspection-digest": ruby_digest(run.inputs.inspection.content),
        "review-digest": ruby_digest(run.inputs.review),
        "admission-digest": ruby_digest(run.inputs.admission),
        "reservation-digest": ruby_digest(run.inputs.reservation),
        "run-join-digest": ruby_digest(run.run_join),
        "target": "a" * 40,
        "control": "a" * 40,
        "workflow-run-id": 991,
        "run-attempt": 1,
    }
    binding = run.provider_binding()
    assert (
        binding.request_id
        == "ruby-bootstrap:" + destination + "-bootstrap:" + "c" * 32
    )
    assert (
        binding.purpose,
        binding.producer,
        binding.workflow_run_id,
        binding.run_attempt,
    ) == ("destination-bootstrap", "provide-ruby-bootstrap", 991, 1)
    assert binding.request_digest == run.request.digest
    assert binding.control == "workflow-delivery-v3:" + run.target
    assert run.binding_digest == canonical_sha256(doc)


def test_admission_copies_required_native_fields_without_ambient_secrets(plan):
    """Caller mutation and ambient credential fields cannot enter run bytes."""
    environment = parse_canonical_json(plan.run.native_environment)
    environment["RUBYGEMS_API_KEY"] = "secret-fixture-value"
    run = admit_ruby_bootstrap(
        plan.run.inputs, plan.run.run_join, environment, CURRENT
    )
    environment["GITHUB_RUN_ID"] = "992"
    assert run.native_environment == plan.run.native_environment
    assert b"secret-fixture-value" not in canonicalize(run.to_document())
    assert run.run_id == 991  # noqa: PLR2004


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("GITHUB_REPOSITORY", "other/three"),
        ("GITHUB_REF", "refs/heads/topic"),
        ("GITHUB_EVENT_NAME", "push"),
        ("GITHUB_ACTOR", "other"),
        ("GITHUB_ACTOR_ID", "123"),
        ("GITHUB_TRIGGERING_ACTOR", "other"),
        ("GITHUB_RUN_ATTEMPT", "2"),
        ("GITHUB_SHA", "e" * 40),
        ("GITHUB_WORKFLOW_SHA", "e" * 40),
        (
            "GITHUB_WORKFLOW_REF",
            "hcoona/three/.github/workflows/workflow-delivery-v3-ruby-smoke.yml@refs/heads/main",
        ),
        ("GITHUB_RUN_ID", "992"),
        ("GITHUB_RUN_ID", "0"),
        ("GITHUB_RUN_ID", True),
        ("GITHUB_RUN_ID", None),
    ],
)
def test_admission_rejects_noncurrent_native_identity(plan, field, value):
    """One contradictory native observation invalidates the complete join."""
    environment = parse_canonical_json(plan.run.native_environment)
    environment[field] = value
    with pytest.raises(ValueError, match=r"Ruby|canonical|schema|keys"):
        admit_ruby_bootstrap(
            plan.run.inputs, plan.run.run_join, environment, CURRENT
        )


@pytest.mark.parametrize("mode", ["missing", "extra", "noncanonical"])
def test_direct_run_requires_closed_canonical_native_environment(plan, mode):
    """Direct constructors cannot evade the screened native boundary."""
    doc = parse_canonical_json(plan.run.native_environment)
    if mode == "missing":
        doc.pop("GITHUB_RUN_ID")
    elif mode == "extra":
        doc["RUBYGEMS_API_KEY"] = "forbidden"
    payload = canonicalize(doc) + (b"\n" if mode == "noncanonical" else b"")
    with pytest.raises(ValueError, match=r"Ruby|canonical|schema|keys"):
        replace(plan.run, native_environment=payload)


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("schema", "normal-run"),
        ("request-digest", "sha256:" + "e" * 64),
        ("reservation-digest", "sha256:" + "e" * 64),
        ("run-id", 992),
        ("run-id", "991"),
        ("run-attempt", True),
        ("run-attempt", 2),
        ("authority", True),
    ],
)
def test_run_join_is_exact_and_does_not_coerce_primitives(plan, field, value):
    """Canonical comparison rejects both foreign runs and Boolean aliases."""
    doc = parse_canonical_json(plan.run.run_join)
    doc[field] = value
    with pytest.raises(ValueError, match=r"Ruby|canonical|schema|keys"):
        replace(plan.run, run_join=canonicalize(doc))


@pytest.mark.parametrize(
    "mode",
    [
        "normal-request",
        "configuration",
        "review",
        "admission",
        "reservation",
        "future-reservation",
    ],
)
def test_inputs_cannot_substitute_any_admitted_chain_original(plan, mode):
    """Admission rechecks semantic joins rather than trusting a wrapper type."""
    run = plan.run
    if mode == "normal-request":
        inputs = replace(run.inputs, request=request())
    elif mode == "configuration":
        inputs = replace(
            run.inputs, configuration=configuration("github-packages")
        )
    else:
        field = "reservation" if mode == "future-reservation" else mode
        doc = parse_canonical_json(getattr(run.inputs, field))
        if mode == "future-reservation":
            doc["reserved-at"] = (
                (CURRENT + timedelta(seconds=1))
                .isoformat()
                .replace("+00:00", "Z")
            )
        else:
            doc["request-digest"] = "sha256:" + "e" * 64
        inputs = replace(run.inputs, **{field: canonicalize(doc)})
    with pytest.raises(ValueError, match=r"Ruby|canonical|schema|keys"):
        replace(run, inputs=inputs)


@pytest.mark.parametrize(
    "now",
    [
        CURRENT - timedelta(microseconds=1),
        CURRENT.replace(tzinfo=None),
        EXPIRED,
    ],
)
def test_run_rechecks_earliest_expiry_and_admission_time(plan, now):
    """A phase cannot start before admission or at the inspection deadline."""
    plan.run.require_current(EXPIRED - timedelta(microseconds=1))
    with pytest.raises(ValueError, match=r"Ruby|canonical|schema|keys"):
        plan.run.require_current(now)


@pytest.mark.parametrize(
    "mode", ["purpose", "run", "producer", "request", "nbgv-extra", "source"]
)
def test_provider_requires_complete_current_facts_not_only_native_version(
    plan, mode
):
    """Same native version cannot hide altered NBGV output or source bytes."""
    provider = plan.provider
    if mode == "nbgv-extra":
        raw = parse_canonical_json(provider.nbgv.raw_bytes)
        raw["OtherField"] = "changed"
        provider = replace(
            provider,
            nbgv=RubyNbgvFacts(canonicalize(raw), provider.nbgv.native_version),
        )
    elif mode == "source":
        source = tuple(
            (path, "sha256:" + "e" * 64 if index == 0 else digest)
            for index, (path, digest) in enumerate(
                provider.source_input_manifest
            )
        )
        provider = replace(provider, source_input_manifest=source)
    else:
        field, value = {
            "purpose": ("purpose", "slice-validation"),
            "run": ("workflow_run_id", 992),
            "producer": ("producer", "discover-ruby"),
            "request": ("request_digest", "sha256:" + "e" * 64),
        }[mode]
        provider = replace(
            provider, binding=replace(provider.binding, **{field: value})
        )
    with pytest.raises(ValueError, match=r"Ruby|canonical|schema|keys"):
        validate_bootstrap_provider(plan.run, provider)


def test_source_digest_identifies_the_full_ordered_input_manifest(plan):
    """Digest commits all path/digest pairs in their canonical sequence."""
    pairs = [
        [path, digest] for path, digest in plan.provider.source_input_manifest
    ]
    assert ruby_bootstrap_source_digest(plan.provider) == canonical_sha256(
        pairs
    )
    assert ruby_bootstrap_source_digest(plan.provider) != canonical_sha256(
        list(reversed(pairs))
    )
    validate_bootstrap_provider(plan.run, plan.provider)


@pytest.mark.parametrize(
    ("side", "field", "value"),
    [
        ("reference", "payload_path", "other.json"),
        ("reference", "payload_digest", "sha256:" + "e" * 64),
        ("reference", "artifact_digest", "sha256:" + "e" * 64),
        ("reference", "artifact_id", 202),
        ("reference", "artifact_url", "https://example.invalid/artifacts/201"),
        ("transport", "artifact_id", 202),
        ("transport", "transport_digest", "sha256:" + "e" * 64),
        ("transport", "artifact_url", "https://example.invalid/artifacts/201"),
        ("transport", "producer", "discover-ruby"),
        ("transport", "workflow_run_id", 992),
        ("transport", "run_attempt", 2),
    ],
)
def test_immutable_transport_rejects_cross_run_or_archive_substitution(
    plan, side, field, value
):
    """Payload equality alone cannot replace the current artifact provenance."""
    ref, identity = plan.provider_reference, plan.provider_transport
    if side == "reference":
        ref = replace(ref, **{field: value})
    else:
        identity = replace(identity, **{field: value})
    with pytest.raises(ValueError, match="immutable transport"):
        require_bootstrap_transport(
            plan.run,
            ref,
            identity,
            plan.provider_content,
            producer="provide-ruby-bootstrap",
            payload_path="provider.json",
        )
