"""Strict commit-6 Release identities, admission, and planning contracts."""

from __future__ import annotations

# ruff: noqa: D103
import inspect
from dataclasses import FrozenInstanceError, fields, replace
from pathlib import Path

import pytest
from three_workflow_delivery_v3.canonical import (
    canonical_sha256,
    canonicalize,
)
from three_workflow_delivery_v3.records.release import (
    OfficialExecutionIdentity,
    OfficialProductIdentity,
    ReleaseAttemptIdentity,
    ReleaseIntent,
    SimulationBinding,
    SimulationIdentity,
    admit_release_record,
)
from three_workflow_delivery_v3.release.identity import (
    derive_simulation_binding,
    normalize_official_simulation_intent,
)
from three_workflow_delivery_v3.release.planner import (
    plan_official_simulation_qualification,
)
from three_workflow_delivery_v3.repository.compiler import (
    AdmittedRepositoryModelSnapshot,
    admit_repository_model_snapshot,
)

FIXTURES = Path(__file__).resolve().parents[1] / "fixtures"


def _fixture_bytes(path: Path) -> bytes:
    return path.read_bytes()


def test_canonical_intent_and_repository_model_fixtures(
    intent: ReleaseIntent,
    admitted_repository_model: AdmittedRepositoryModelSnapshot,
) -> None:
    intent_bytes = _fixture_bytes(
        FIXTURES / "release/official-simulation-intent.json"
    )
    intent_digest = (
        (FIXTURES / "release/official-simulation-intent.sha256")
        .read_text(encoding="utf-8")
        .strip()
    )
    model_bytes = _fixture_bytes(
        FIXTURES / "repository/ready-simulation-model.json"
    )
    model_digest = (
        (FIXTURES / "repository/ready-simulation-model.sha256")
        .read_text(encoding="utf-8")
        .strip()
    )

    assert intent_bytes == canonicalize(intent.to_document())
    assert intent_digest == intent.intent_digest
    assert model_bytes == admitted_repository_model.canonical_bytes
    assert model_digest == admitted_repository_model.canonical_digest
    assert (
        admit_release_record(
            intent_bytes,
            expected=intent,
            expected_digest=intent_digest,
        )
        is intent
    )
    assert (
        admit_repository_model_snapshot(
            model_bytes,
            expected_context=admitted_repository_model.snapshot.context,
            expected_digest=model_digest,
        )
        == admitted_repository_model
    )


def test_ready_repository_model_round_trips_through_canonical_admission(
    admitted_repository_model: AdmittedRepositoryModelSnapshot,
) -> None:
    admitted = admit_repository_model_snapshot(
        admitted_repository_model.canonical_bytes,
        expected_context=admitted_repository_model.snapshot.context,
        expected_digest=admitted_repository_model.canonical_digest,
    )

    assert admitted == admitted_repository_model
    assert admitted.snapshot.ready is True


def test_repository_model_admission_rejects_noncanonical_unknown_and_tampered(
    admitted_repository_model: AdmittedRepositoryModelSnapshot,
) -> None:
    snapshot = admitted_repository_model.snapshot
    pretty = b'{"schema": "workflow-delivery/v3/repository-model-snapshot"}'
    with pytest.raises(ValueError, match="not canonical"):
        admit_repository_model_snapshot(
            pretty,
            expected_context=snapshot.context,
            expected_digest=snapshot.snapshot_digest,
        )

    unknown = snapshot.to_document()
    unknown["unexpected"] = "value"
    unknown_bytes = canonicalize(unknown)
    with pytest.raises(ValueError, match="unknown field"):
        admit_repository_model_snapshot(
            unknown_bytes,
            expected_context=snapshot.context,
            expected_digest=canonical_sha256(unknown),
        )

    not_ready = replace(snapshot, ready=False)
    with pytest.raises(ValueError, match="not a ready first-slice closure"):
        admit_repository_model_snapshot(
            canonicalize(not_ready.to_document()),
            expected_context=not_ready.context,
            expected_digest=not_ready.snapshot_digest,
        )

    assert snapshot.release_policy is not None
    tampered_policy = replace(
        snapshot,
        release_policy=replace(
            snapshot.release_policy,
            governance=replace(
                snapshot.release_policy.governance,
                repository="other/repository",
            ),
        ),
    )
    with pytest.raises(ValueError, match="policy closure mismatch"):
        admit_repository_model_snapshot(
            canonicalize(tampered_policy.to_document()),
            expected_context=tampered_policy.context,
            expected_digest=tampered_policy.snapshot_digest,
        )


def test_repository_model_admission_rejects_prior_attempt_context(
    admitted_repository_model: AdmittedRepositoryModelSnapshot,
) -> None:
    prior_context = replace(
        admitted_repository_model.snapshot.context,
        run_attempt=2,
    )

    with pytest.raises(ValueError, match="current context binding mismatch"):
        admit_repository_model_snapshot(
            admitted_repository_model.canonical_bytes,
            expected_context=prior_context,
            expected_digest=admitted_repository_model.canonical_digest,
        )


def test_simulation_identity_requires_admitted_current_model(
    intent: ReleaseIntent,
    admitted_repository_model: AdmittedRepositoryModelSnapshot,
) -> None:
    with pytest.raises(TypeError, match="admitted Repository Model"):
        derive_simulation_binding(
            intent,
            admitted_repository_model.snapshot,  # type: ignore[arg-type]
        )

    with pytest.raises(ValueError, match="admission integrity"):
        AdmittedRepositoryModelSnapshot(
            snapshot=replace(
                admitted_repository_model.snapshot,
                context=replace(
                    admitted_repository_model.snapshot.context,
                    run_attempt=2,
                ),
            ),
            canonical_digest=admitted_repository_model.canonical_digest,
            canonical_bytes=admitted_repository_model.canonical_bytes,
        )


def test_release_intent_is_stable_while_simulation_reruns_are_distinct(
    intent: ReleaseIntent,
    admitted_repository_model: AdmittedRepositoryModelSnapshot,
    binding: SimulationBinding,
) -> None:
    rerun_intent = normalize_official_simulation_intent(
        repository=intent.repository,
        selected_ref=intent.selected_ref,
        target=intent.target,
        actor=intent.actor,
        workflow_run_id=intent.workflow_run_id,
    )

    assert intent.workflow_ref == intent.selected_ref
    assert intent.workflow_sha == intent.target
    assert rerun_intent.request_id == intent.request_id
    assert rerun_intent.intent_digest == intent.intent_digest
    assert "run-attempt" not in rerun_intent.to_document()
    assert (
        derive_simulation_binding(rerun_intent, admitted_repository_model)
        == binding
    )

    rerun_attempt = binding.simulation.run_attempt + 1
    rerun_snapshot = replace(
        admitted_repository_model.snapshot,
        context=replace(
            admitted_repository_model.snapshot.context,
            run_attempt=rerun_attempt,
        ),
    )
    rerun_model = admit_repository_model_snapshot(
        canonicalize(rerun_snapshot.to_document()),
        expected_context=rerun_snapshot.context,
        expected_digest=rerun_snapshot.snapshot_digest,
    )
    rerun_binding = derive_simulation_binding(rerun_intent, rerun_model)

    assert rerun_binding.simulation.request_id == intent.request_id
    assert rerun_binding.simulation.run_attempt == rerun_attempt
    assert rerun_binding.simulation.identity != binding.simulation.identity


def test_release_records_are_exact_frozen_slotted_dataclasses(
    intent: ReleaseIntent,
    binding: SimulationBinding,
) -> None:
    records = (
        intent,
        binding.simulation,
        binding,
    )

    for record in records:
        assert fields(record)
        assert hasattr(type(record), "__slots__")
        with pytest.raises(FrozenInstanceError):
            record.__setattr__(fields(record)[0].name, "mutated")


def test_identity_field_order_and_live_identity_shapes_are_exact() -> None:
    products = (
        OfficialProductIdentity("official", "unit-b", "1.0.0"),
        OfficialProductIdentity("official", "unit-a", "2.0.0"),
        OfficialProductIdentity("official", "unit-a", "1.0.0"),
    )

    assert tuple(sorted(products)) == (
        OfficialProductIdentity("official", "unit-a", "1.0.0"),
        OfficialProductIdentity("official", "unit-a", "2.0.0"),
        OfficialProductIdentity("official", "unit-b", "1.0.0"),
    )
    execution = OfficialExecutionIdentity(products[0], "a" * 40)
    attempt = ReleaseAttemptIdentity(execution, 91)
    retry = ReleaseAttemptIdentity(execution, 92)
    assert tuple(attempt.to_document()) == (
        "schema",
        "execution",
        "workflow-run-id",
    )
    assert retry != attempt


def test_canonical_release_record_admission_rejects_tampering(
    intent: ReleaseIntent,
) -> None:
    canonical_bytes = canonicalize(intent.to_document())
    admitted = admit_release_record(
        canonical_bytes,
        expected=intent,
        expected_digest=intent.intent_digest,
    )
    assert admitted is intent

    tampered = dict(intent.to_document())
    tampered["actor"] = "other-actor"
    with pytest.raises(ValueError, match="schema or binding mismatch"):
        admit_release_record(
            canonicalize(tampered),
            expected=intent,
            expected_digest=intent.intent_digest,
        )


def test_official_simulation_plan_is_the_exact_closed_first_slice(
    qualification_snapshot,
    admitted_repository_model: AdmittedRepositoryModelSnapshot,
) -> None:
    snapshot = qualification_snapshot

    assert tuple(
        inspect.signature(plan_official_simulation_qualification).parameters
    ) == ("intent", "binding", "admitted_repository_model")
    assert admitted_repository_model.snapshot.release_policy is not None
    assert snapshot.release_policy_digest == (
        admitted_repository_model.snapshot.release_policy.policy_digest
    )
    assert len(snapshot.builds) == 1
    assert len(snapshot.variants) == 1
    assert len(snapshot.outputs) == 1
    assert snapshot.outputs[0].output_id == "npm-tarball"
    assert snapshot.nbgv.canonical_version == "1.2.3"
    assert snapshot.nbgv.npm_package_version == ("1.2.3-beta.42.ge123456")
    assert len(snapshot.destination_projections) == 1
    projection = snapshot.destination_projections[0]
    assert projection.destination_id == "npm/npmjs-public-v1"
    assert projection.registry == "https://registry.npmjs.org"
    assert projection.coordinate.channel == "official"
    assert projection.coordinate.package_name == (
        "@hcoona/hcoona-release-smoke-npm"
    )
    assert projection.coordinate.native_version == (
        snapshot.nbgv.npm_package_version
    )
    assert snapshot.potential_actions[0].capability_requirements == (
        "npmjs/trusted-publishing-oidc-v1",
    )
    assert snapshot.potential_actions[0].mutable_resource_key_basis == (
        "external-package-coordinate",
    )


def test_official_simulation_plan_has_exact_four_obligations_and_closed_dag(
    qualification_snapshot,
) -> None:
    obligations = qualification_snapshot.obligations

    assert tuple(item.obligation_id for item in obligations) == (
        "release:build:npm-package",
        "release:quality:project-test",
        "release:quality:npm-artifact-contents",
        "release:quality:npm-install-import",
    )
    assert tuple(item.definition_id for item in obligations) == (
        "node/npm-package-v1",
        "node/project-test-v1",
        "node/npm-artifact-contents-v1",
        "node/npm-install-import-v1",
    )
    assert obligations[0].prerequisites == ()
    assert obligations[1].prerequisites == ()
    assert obligations[2].prerequisites == ("release:build:npm-package",)
    assert obligations[3].prerequisites == ("release:build:npm-package",)
    assert qualification_snapshot.expected_evidence_ids == tuple(
        obligation.expected_evidence_id for obligation in obligations
    )
    serialized = canonicalize(qualification_snapshot.to_document()).decode()
    assert "repository/source-tree-conformance-v1" not in serialized
    assert "node/project-build-v1" not in serialized
    assert "release-attempt-identity" not in serialized


def test_simulation_identity_document_contains_no_live_identity(
    binding: SimulationBinding,
) -> None:
    serialized = canonicalize(binding.to_document()).decode()

    assert "official-product-identity" not in serialized
    assert "official-execution-identity" not in serialized
    assert "release-attempt-identity" not in serialized
    assert isinstance(binding.simulation, SimulationIdentity)
