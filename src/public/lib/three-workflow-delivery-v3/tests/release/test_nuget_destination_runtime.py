"""Modeled native runtime scenarios without registry or admission evidence."""

from __future__ import annotations

# ruff: noqa: D103, SLF001
import base64
import hashlib
import json
import sys
from dataclasses import replace
from datetime import datetime, timedelta
from platform import python_version
from types import SimpleNamespace
from unittest.mock import Mock

import pytest
from three_workflow_delivery_v3.adapters import dotnet as dotnet_adapter
from three_workflow_delivery_v3.adapters import nuget_github_packages as native
from three_workflow_delivery_v3.canonical import canonical_sha256, canonicalize
from three_workflow_delivery_v3.catalogs import catalog_digest
from three_workflow_delivery_v3.records.release import (
    NugetDestinationOperationProfile,
    PublicationSnapshot,
    admit_release_record,
)
from three_workflow_delivery_v3.records.release_transport import (
    ReleaseAdmissionBindings,
)
from three_workflow_delivery_v3.release import eligibility as shared
from three_workflow_delivery_v3.release import nuget_governance as governance
from three_workflow_delivery_v3.release.attempt_finalizer import (
    FinalizationInputs,
    finalize_attempt_outcome,
)
from three_workflow_delivery_v3.release.exact_satisfied import (
    prove_nuget_exact_satisfied,
)
from three_workflow_delivery_v3.release.finalizer import (
    finalize_qualification,
    materialize_publication_snapshot,
)
from three_workflow_delivery_v3.release.governance_git import (
    GovernanceGitRead,
    GovernanceGitReadError,
)
from three_workflow_delivery_v3.release.identity import (
    NUGET_BUDDY_LIVE_WORKFLOW_PATH,
    derive_buddy_execution_identity,
    derive_release_attempt_binding,
)
from three_workflow_delivery_v3.release.live import (
    form_approval_bundle,
    form_publication_authorization,
)
from three_workflow_delivery_v3.release.nuget_eligibility import (
    admit_nuget_live_eligibility_decision,
    evaluate_nuget_live_eligibility,
)
from three_workflow_delivery_v3.release.nuget_observation import (
    observe_nuget_remote_state,
)
from three_workflow_delivery_v3.release.nuget_planner import (
    plan_nuget_live_qualification,
)
from three_workflow_delivery_v3.release.nuget_publication import (
    execute_nuget_publication,
    prepare_nuget_publication,
)
from three_workflow_delivery_v3.release.publication import PublicationInputs
from three_workflow_delivery_v3.repository.descriptors import (
    NUGET_PACKAGE,
    NUGET_POLICY_PATH,
    load_release_policy,
)

from ..repository.test_dotnet_compiler import _native_scenario
from .test_eligibility import _ready_activation
from .test_nuget_destination import (
    CONTROL_URL,
    DIGEST,
    NOW_TEXT,
    RESOURCES,
    _modeled_profile_document,
    _reference,
)
from .test_nuget_governance import _attested_document, _client
from .test_nuget_qualification import _nuget_scenario, _qualified

NOW = datetime.fromisoformat(NOW_TEXT)
LATER = NOW + timedelta(minutes=1)
TOKEN = "modeled-current-repository-token"  # noqa: S105


@pytest.fixture(scope="module")
def nuget_scenario(tmp_path_factory):
    # Runtime scenarios read this frozen source/Model; per-case mutable state
    # remains in native_case and tmp_path. Compiler/qualification tests retain
    # their own independent repositories and mutable fixtures.
    root = tmp_path_factory.mktemp("nuget-destination-model")
    with pytest.MonkeyPatch.context() as patch:
        return _nuget_scenario(_native_scenario(root, patch), root)


def _ready_document(profile, monkeypatch):
    document = _attested_document()
    activation = _ready_activation(
        primitive_id="workflow-delivery-v3/native-nuget-suite/v1",
        captured_at="2026-09-09T00:00:00Z",
    )
    primitive = activation["destination_primitive"]
    primitive["destination_operation_profile_digest"] = profile.profile_digest
    primitive["disposable_package_preconditions"]["package"] = NUGET_PACKAGE
    primitive["lower_layer_contract_revision"] = (
        "modeled/nuget-atomic-creation-v1"
    )
    key = (
        profile.profile_digest,
        primitive["native_acceptance_suite_version"],
        NUGET_PACKAGE,
        primitive["github_api_version"],
        primitive["lower_layer_contract_revision"],
        primitive["evidence_digest"],
    )
    monkeypatch.setattr(
        governance, "_ADMITTED_NUGET_NATIVE_GENERATIONS", frozenset({key})
    )
    monkeypatch.setattr(
        governance,
        "_ADMITTED_NUGET_ATOMIC_CONTRACTS",
        frozenset({primitive["lower_layer_contract_revision"]}),
    )
    document.update(activation=activation, live_enabled=True)
    return document


@pytest.fixture
def native_case(nuget_scenario, monkeypatch, tmp_path):
    scenario = nuget_scenario
    intent = replace(
        scenario.intent, workflow_path=NUGET_BUDDY_LIVE_WORKFLOW_PATH
    )
    policy = load_release_policy(
        scenario.request.source_root / NUGET_POLICY_PATH,
        _target_path=NUGET_POLICY_PATH,
    )
    if (
        sys.implementation.name != "cpython"
        or python_version() != native.NUGET_PYTHON_VERSION
    ):
        # Higher-layer scenarios model the adapter on unpinned test hosts.
        # The pinned HK host retains actual runtime/source profile checks.
        monkeypatch.setattr(
            native, "nuget_operation_profile", _modeled_profile_document
        )
    profile = NugetDestinationOperationProfile(
        canonicalize(native.nuget_operation_profile(RESOURCES))
    )
    document = _ready_document(profile, monkeypatch)
    client = _client(document)
    client.read_source.return_value = GovernanceGitRead(
        intent.target, "sha1", "e" * 40, canonicalize(document)
    )
    attestation = shared.parse_governance_attestation(canonicalize(document))
    response = native.NuGetHttpResponse(
        "https://api.github.com/modeled-reviewed-control",
        200,
        (),
        b'{"modeled":true}',
    )
    platform = governance.NuGetPlatformFacts(
        intent.target,
        intent.target,
        intent.target,
        intent.workflow_run_id,
        intent.target,
        "c" * 40,
        "d" * 40,
        123,
        NOW,
        attestation.activation.approval_environment,
        attestation.activation.artifact_retention,
        {},
        ("hcoona",),
        (response,),
        (response,),
    )
    context = shared.LiveEligibilityContext(
        "live-release",
        intent.request_id,
        intent.workflow_run_id,
        intent.selected_ref,
        intent.target,
        scenario.model.canonical_digest,
        shared.LIVE_ELIGIBILITY_PRODUCER,
        f"workflow-delivery-v3:{intent.target}",
        shared.release_policy_digest(policy),
        catalog_digest(),
    )
    produced = evaluate_nuget_live_eligibility(
        context,
        intent=intent,
        repository_model=scenario.model,
        policy=policy,
        client=client,
        now=NOW,
        platform=platform,
        profile=profile,
    )
    eligibility = admit_nuget_live_eligibility_decision(
        canonicalize(produced.to_document()),
        intent=intent,
        repository_model=scenario.model,
        policy=policy,
        expected_digest=produced.decision_digest,
        admission_mode=shared.LiveEligibilityAdmissionMode.CURRENT_FRESHNESS,
        now=NOW,
    )
    binding = derive_release_attempt_binding(
        intent=intent,
        execution=derive_buddy_execution_identity(intent),
        repository_model_digest=scenario.model.canonical_digest,
        live_eligibility_artifact_id=301,
        live_eligibility_artifact_digest=DIGEST,
        live_eligibility_payload_digest=eligibility.canonical_digest,
        attestation_provenance=eligibility.governance.provenance,
    )
    snapshot = plan_nuget_live_qualification(
        intent, binding, scenario.model, scenario.provider
    )
    scenario = replace(
        scenario, intent=intent, binding=binding, snapshot=snapshot
    )
    artifact, evidence, _calls = _qualified(scenario, monkeypatch, tmp_path)
    decision = finalize_qualification(snapshot, evidence, (artifact,))
    return SimpleNamespace(
        intent=intent,
        policy=policy,
        eligibility=eligibility,
        produced=produced,
        platform=platform,
        context=context,
        profile=profile,
        client=client,
        scenario=scenario,
        snapshot=snapshot,
        attempt_binding=binding,
        artifact=artifact,
        evidence=evidence,
        decision=decision,
        decision_reference=_reference(
            decision.decision_digest, intent.workflow_run_id, 501
        ),
        current=ReleaseAdmissionBindings(
            "live-release", intent.workflow_run_id, None, intent.target
        ),
    )


def _arguments(case):
    return {
        name: getattr(case, name)
        for name in (
            "intent",
            "attempt_binding",
            "eligibility",
            "policy",
            "snapshot",
            "decision",
            "decision_reference",
            "artifact",
        )
    }


class ModeledFeed:
    """Supported read surfaces with a controlled in-memory active archive."""

    def __init__(self, case, *, present=False):
        """Initialize one modeled active lifecycle and official-parser seam."""
        self.case, self.present = case, present
        self.content = case.scenario.result.package
        self.events = []
        identity = case.artifact.identity
        self.authority = Mock(spec=native.NuGetAuthority)

        def normalize(package_id, version):
            return {
                "displayPackageId": package_id,
                "displayVersion": version,
                "normalizedPackageId": identity.normalized_package_id,
                "normalizedVersion": identity.normalized_version,
            }

        self.authority.normalize_identity.side_effect = normalize
        self.authority.service_resources.return_value = {
            "packageBaseAddress": RESOURCES.package_base_address,
            "packagePublish": RESOURCES.package_publish,
        }
        self.authority.inspect_package.return_value = {
            "identity": normalize(
                identity.package_name, identity.native_version
            ),
            "witnessBase64": base64.b64encode(
                case.scenario.result.witness
            ).decode(),
        }

    def get(self, url, **_kwargs):
        """Return one supported response from the current modeled state."""
        self.events.append(("GET", url))
        identity = self.case.artifact.identity
        root = (
            RESOURCES.package_base_address
            + identity.normalized_package_id
            + "/"
        )
        if url == native.NUGET_SERVICE_INDEX:
            document = {"resources": []}
        elif url == CONTROL_URL:
            document = {
                "id": 12024661,
                "name": identity.package_name,
                "package_type": "nuget",
                "visibility": "public",
                "owner": {"login": "hcoona", "type": "User"},
                "repository": {"full_name": "hcoona/three"},
            }
        elif url.startswith(CONTROL_URL + "/versions?"):
            document = (
                [{"id": 71, "name": identity.native_version}]
                if self.present
                else []
            )
        elif url == root + "index.json":
            document = {
                "versions": [identity.native_version] if self.present else []
            }
        elif (
            url
            == root
            + identity.normalized_version
            + "/"
            + identity.normalized_package_id
            + "."
            + identity.normalized_version
            + ".nupkg"
        ):
            return native.NuGetHttpResponse(url, 200, (), self.content)
        else:
            pytest.fail(f"Unexpected modeled read: {url}")
        return native.NuGetHttpResponse(
            url, 200, (), json.dumps(document).encode()
        )


def _observe(case, feed):
    return observe_nuget_remote_state(
        **_arguments(case),
        authority=feed.authority,
        token=TOKEN,
        transport=feed,
        now=NOW,
    )


def _snapshot(case, observation):
    args = _arguments(case)
    return materialize_publication_snapshot(
        args.pop("snapshot"),
        args.pop("decision"),
        (observation,),
        (args.pop("artifact"),),
        **args,
        action_creation_at=NOW,
        destination_operation_profile=case.profile,
    )


def _publication(case, feed):
    observation = _observe(case, feed)
    publication = _snapshot(case, observation)
    publication_ref = _reference(
        publication.snapshot_digest, case.intent.workflow_run_id, 502
    )
    summary = b"Modeled review of exact native artifact and one action."
    summary_ref = replace(
        _reference(
            "sha256:" + hashlib.sha256(summary).hexdigest(),
            case.intent.workflow_run_id,
            503,
        ),
        payload_path="reviewer-summary.md",
    )
    bundle = form_approval_bundle(
        intent=case.intent,
        attempt_binding=case.attempt_binding,
        qualification_decision=case.decision,
        publication_snapshot=publication,
        publication_snapshot_reference=publication_ref,
        reviewer_summary_reference=summary_ref,
        control=case.context.control,
    )
    bundle_ref = _reference(
        bundle.bundle_digest, case.intent.workflow_run_id, 504
    )
    fresh = shared.observe_governance_source(
        case.policy.governance, case.client, now=NOW
    )
    authorization = form_publication_authorization(
        approval_bundle=bundle,
        approval_bundle_reference=bundle_ref,
        approval_boundary_sentinel_result="success",
        governance=fresh,
        destination_operation_profile_digest=case.profile.profile_digest,
        completed_at=NOW_TEXT,
        control=case.context.control,
    )
    return PublicationInputs(
        **_arguments(case),
        observation=observation,
        publication_snapshot=publication,
        publication_snapshot_reference=publication_ref,
        approval_bundle=bundle,
        approval_bundle_reference=bundle_ref,
        reviewer_summary=summary,
        reviewer_summary_reference=summary_ref,
        authorization=authorization,
        authorization_reference=_reference(
            authorization.authorization_digest, case.intent.workflow_run_id, 505
        ),
    )


def _finalization(case, inputs=None):
    result = FinalizationInputs(
        **{k: v for k, v in _arguments(case).items() if k != "artifact"},
        evidence=case.evidence,
        artifacts=(case.artifact,),
    )
    if inputs is not None:
        result = replace(
            result,
            observations=(
                (
                    inputs.observation,
                    _reference(
                        inputs.observation.observation_digest,
                        case.intent.workflow_run_id,
                        506,
                    ),
                ),
            ),
            publication=(
                inputs.publication_snapshot,
                inputs.publication_snapshot_reference,
            ),
            bundle=(inputs.approval_bundle, inputs.approval_bundle_reference),
            reviewer_summary=(
                inputs.reviewer_summary,
                inputs.reviewer_summary_reference,
            ),
            authorization=(
                inputs.authorization,
                inputs.authorization_reference,
            ),
        )
    return result


def _finish(
    case, records, *, publisher="skipped", step="skipped", terminal=None
):
    return finalize_attempt_outcome(
        records,
        current=case.current,
        run_attempt=1,
        publisher_conclusion=publisher,
        publication_step_outcome=step,
        publication_terminal_reference=canonicalize(
            terminal.to_document()
        ).decode()
        if terminal
        else None,
        observation_conclusion="success",
    )


@pytest.mark.parametrize("supply_runtime_facts", [False, True])
def test_native_eligibility_keeps_blocked_source_state_only(
    native_case, supply_runtime_facts
):
    case = native_case
    blocked = evaluate_nuget_live_eligibility(
        case.context,
        intent=case.intent,
        repository_model=case.scenario.model,
        policy=case.policy,
        client=_client(),
        now=NOW,
        platform=case.platform if supply_runtime_facts else None,
        profile=case.profile if supply_runtime_facts else None,
    )
    assert blocked.result is shared.EligibilityResult.BLOCKED
    assert blocked.diagnostics == ("governance-live-disabled",)
    assert blocked.platform is None
    assert blocked.profile is None
    document = blocked.to_document()
    assert document["platform"] is None
    assert document["profile"] is None
    retained = document["governance"]["admitted-attestation"]
    assert "inspected_at" not in retained
    assert "access_inventory" not in retained
    assert "static-reference" not in document
    with pytest.raises(ValueError, match="passing Decision"):
        admit_nuget_live_eligibility_decision(
            canonicalize(document),
            intent=case.intent,
            repository_model=case.scenario.model,
            policy=case.policy,
            expected_digest=blocked.decision_digest,
            admission_mode=shared.LiveEligibilityAdmissionMode.CURRENT_FRESHNESS,
            now=NOW,
        )


@pytest.mark.parametrize(
    "substitution",
    [
        "request",
        "profile",
        "source",
        "platform-run",
        "platform-time",
        "scan",
        "schema",
    ],
)
def test_native_eligibility_rejects_self_consistent_substitution(
    native_case, substitution
):
    case = native_case
    document = case.produced.to_document()
    if substitution == "request":
        document["context"]["request-id"] = "release-request:" + "9" * 64
    elif substitution == "profile":
        document["profile"]["executableSha256"] = "9" * 64
    elif substitution == "source":
        document["governance"]["eligibility-main-sha"] = "9" * 40
    elif substitution == "platform-run":
        document["platform"]["workflow-run-id"] += 1
    elif substitution == "platform-time":
        document["platform"]["observed-at"] = (
            (NOW - timedelta(minutes=6)).isoformat().replace("+00:00", "Z")
        )
    elif substitution == "scan":
        document["static-reference"] = {"result": "clean"}
    else:
        document["schema"] = shared.LIVE_ELIGIBILITY_DECISION_SCHEMA
    with pytest.raises((TypeError, ValueError)):
        admit_nuget_live_eligibility_decision(
            canonicalize(document),
            intent=case.intent,
            repository_model=case.scenario.model,
            policy=case.policy,
            expected_digest=canonical_sha256(document),
            admission_mode=shared.LiveEligibilityAdmissionMode.CURRENT_FRESHNESS,
            now=NOW,
        )


@pytest.mark.parametrize("state", ["absent", "exact", "conflicting", "unknown"])
def test_native_observation_materializes_only_zero_or_one_ready_action(
    native_case, state
):
    case = native_case
    feed = ModeledFeed(case, present=state != "absent")
    if state == "conflicting":
        feed.content += b"changed actual archive"
    elif state == "unknown":
        feed.get = Mock(
            side_effect=native.NuGetTransportError("modeled lost read")
        )
    observation = _observe(case, feed)
    expected = {
        "absent": "absent",
        "exact": "exact-satisfied",
        "conflicting": "conflicting",
        "unknown": "unprovable",
    }[state]
    assert observation.classification == expected
    if state in {"absent", "exact"}:
        publication = _snapshot(case, observation)
        assert len(publication.materialized_actions) == (
            1 if state == "absent" else 0
        )
        assert (
            admit_release_record(
                canonicalize(publication.to_document()),
                expected_type=PublicationSnapshot,
                expected_digest=publication.snapshot_digest,
            )
            == publication
        )
    else:
        with pytest.raises(ValueError, match="not ready"):
            _snapshot(case, observation)
        outcome = _finish(
            case,
            replace(
                _finalization(case),
                observations=(
                    (
                        observation,
                        _reference(
                            observation.observation_digest,
                            case.intent.workflow_run_id,
                            506,
                        ),
                    ),
                ),
            ),
        )
        assert outcome.possibly_mutated is False
        assert outcome.direct_predecessor.kind == "blocking-observation"


def test_native_zero_action_requires_fresh_exact_proof(native_case):
    case = native_case
    feed = ModeledFeed(case, present=True)
    observation = _observe(case, feed)
    publication = _snapshot(case, observation)
    reference = _reference(
        publication.snapshot_digest, case.intent.workflow_run_id, 502
    )
    records = replace(
        _finalization(case),
        observations=(
            (
                observation,
                _reference(
                    observation.observation_digest,
                    case.intent.workflow_run_id,
                    506,
                ),
            ),
        ),
        publication=(publication, reference),
    )
    before = _finish(case, records)
    assert before.disposition != "exact-satisfied"
    proof = prove_nuget_exact_satisfied(
        **_arguments(case),
        observation=observation,
        publication_snapshot=publication,
        publication_snapshot_reference=reference,
        publisher_conclusion="skipped",
        authority=feed.authority,
        governance_client=case.client,
        transport=feed,
        token=TOKEN,
        clock=lambda: LATER,
    )
    outcome = _finish(
        case,
        replace(
            records,
            exact_proof=(
                proof,
                _reference(
                    proof.proof_digest, case.intent.workflow_run_id, 507
                ),
            ),
        ),
    )
    assert outcome.disposition == "exact-satisfied"
    assert not outcome.possibly_mutated
    assert (
        outcome.direct_predecessor.kind == "exact-satisfied-finalization-proof"
    )
    case.client.read_source.assert_called_with(
        "hcoona/three",
        "refs/heads/main",
        case.policy.governance.path,
        eligibility_main_sha=case.intent.target,
    )
    feed.content += b"later replacement"
    with pytest.raises(ValueError, match="not exact"):
        prove_nuget_exact_satisfied(
            **_arguments(case),
            observation=observation,
            publication_snapshot=publication,
            publication_snapshot_reference=reference,
            publisher_conclusion="skipped",
            authority=feed.authority,
            governance_client=case.client,
            transport=feed,
            token=TOKEN,
            clock=lambda: LATER,
        )


@pytest.mark.parametrize(
    ("invocation", "expected"),
    [
        ("created", "published"),
        ("conflict", "publication-failed"),
        ("lost-response", "publication-failed"),
    ],
)
def test_native_publisher_uses_durable_marker_once_and_conservative_result(
    native_case, monkeypatch, tmp_path, invocation, expected
):
    case = native_case
    feed = ModeledFeed(case)
    inputs = _publication(case, feed)
    nupkg = tmp_path / "downloaded.nupkg"
    nupkg.write_bytes(case.scenario.result.package)
    runtime = tmp_path / "publisher"
    marker = prepare_nuget_publication(
        inputs,
        current=case.current,
        run_attempt=1,
        nupkg=nupkg,
        runtime_directory=runtime,
        authority=feed.authority,
        governance_client=case.client,
        transport=feed,
        token=TOKEN,
        clock=lambda: LATER,
    )
    reference = _reference(
        marker.marker_digest, case.intent.workflow_run_id, 508
    )
    retained = admit_release_record(
        canonicalize(marker.to_document()),
        expected_type=type(marker),
        expected_digest=reference.payload_digest,
        expected_bindings=case.current,
    )
    feed.events.append(("marker-service-admitted", reference.artifact_id))
    calls = []
    for name in (
        "build_dotnet_package",
        "qualify_nuget_artifact_contents",
        "qualify_nuget_restore_build_invoke",
    ):
        monkeypatch.setattr(
            dotnet_adapter,
            name,
            Mock(
                side_effect=AssertionError("target code executed by publisher")
            ),
        )

    def publish(**kwargs):
        assert feed.events[-1] == (
            "marker-service-admitted",
            reference.artifact_id,
        )
        assert (runtime / "command-started").is_file()
        assert (
            kwargs["package"]
            == nupkg.read_bytes()
            == case.scenario.result.package
        )
        assert kwargs["expected_profile_sha256"] == case.profile.profile_digest
        assert kwargs[
            "expected_package_sha256"
        ] == case.artifact.content.content_sha256.removeprefix("sha256:")
        calls.append(kwargs)
        feed.present = (
            True  # Exact diagnostic readback even for failed invocation.
        )
        response = (
            None
            if invocation == "lost-response"
            else native.NuGetHttpResponse(
                RESOURCES.package_publish,
                201 if invocation == "created" else 409,
                (),
                b"modeled-response",
            )
        )
        return native.NuGetPublicationInvocation(
            case.profile.profile_digest,
            kwargs["expected_package_sha256"],
            "f" * 64,
            response,
            "response-lost" if response is None else None,
        )

    monkeypatch.setattr(native, "publish_nuget_once", publish)
    result = execute_nuget_publication(
        inputs,
        current=case.current,
        run_attempt=1,
        durable_marker=retained,
        marker_reference=reference,
        runtime_directory=runtime,
        read_resources=lambda: RESOURCES,
        authority=feed.authority,
        transport=feed,
        token=TOKEN,
        clock=lambda: LATER,
    )
    assert len(calls) == 1
    assert not runtime.exists()
    assert result.post_action_readback.classification == "exact-satisfied"
    assert result.result == (
        "published" if invocation == "created" else "failed"
    )
    result_ref = _reference(
        result.result_digest, case.intent.workflow_run_id, 509
    )
    records = replace(
        _finalization(case, inputs),
        terminal=(result, result_ref),
        result_marker=(retained, reference),
    )
    outcome = _finish(
        case, records, publisher="success", step="success", terminal=result_ref
    )
    assert outcome.disposition == expected
    assert outcome.possibly_mutated is (invocation != "created")
    assert outcome.direct_predecessor.reference == result_ref
    with pytest.raises(FileNotFoundError):
        execute_nuget_publication(
            inputs,
            current=case.current,
            run_attempt=1,
            durable_marker=retained,
            marker_reference=reference,
            runtime_directory=runtime,
            read_resources=lambda: RESOURCES,
            authority=feed.authority,
            transport=feed,
            token=TOKEN,
            clock=lambda: LATER,
        )
    assert len(calls) == 1


def _prepared(case, tmp_path):
    feed = ModeledFeed(case)
    inputs = _publication(case, feed)
    path = tmp_path / "original.nupkg"
    path.write_bytes(case.scenario.result.package)
    runtime = tmp_path / "publisher"
    marker = prepare_nuget_publication(
        inputs,
        current=case.current,
        run_attempt=1,
        nupkg=path,
        runtime_directory=runtime,
        authority=feed.authority,
        governance_client=case.client,
        transport=feed,
        token=TOKEN,
        clock=lambda: LATER,
    )
    reference = _reference(
        marker.marker_digest, case.intent.workflow_run_id, 508
    )
    marker = admit_release_record(
        canonicalize(marker.to_document()),
        expected_type=type(marker),
        expected_digest=reference.payload_digest,
        expected_bindings=case.current,
    )
    return inputs, feed, runtime, marker, reference


def test_native_publication_rejects_unadmitted_marker_reference(
    native_case, monkeypatch, tmp_path
):
    case = native_case
    inputs, feed, runtime, marker, reference = _prepared(case, tmp_path)
    publish = Mock(side_effect=AssertionError("unadmitted mutation"))
    monkeypatch.setattr(native, "publish_nuget_once", publish)
    with pytest.raises(ValueError, match="durable marker"):
        execute_nuget_publication(
            inputs,
            current=case.current,
            run_attempt=1,
            durable_marker=marker,
            marker_reference=SimpleNamespace(
                payload_digest=reference.payload_digest
            ),
            runtime_directory=runtime,
            read_resources=lambda: RESOURCES,
            authority=feed.authority,
            transport=feed,
            token=TOKEN,
            clock=lambda: LATER,
        )
    publish.assert_not_called()
    assert not (runtime / "command-started").exists()
    assert (runtime / case.artifact.content.basename).read_bytes() == (
        case.scenario.result.package
    )


def test_native_publication_rejects_changed_archive_before_invocation(
    native_case, monkeypatch, tmp_path
):
    case = native_case
    inputs, feed, runtime, marker, reference = _prepared(case, tmp_path)
    (runtime / case.artifact.content.basename).write_bytes(
        b"substituted after marker"
    )
    publish = Mock(side_effect=AssertionError("must not publish changed bytes"))
    monkeypatch.setattr(native, "publish_nuget_once", publish)
    result = execute_nuget_publication(
        inputs,
        current=case.current,
        run_attempt=1,
        durable_marker=marker,
        marker_reference=reference,
        runtime_directory=runtime,
        read_resources=lambda: RESOURCES,
        authority=feed.authority,
        transport=feed,
        token=TOKEN,
        clock=lambda: LATER,
    )
    publish.assert_not_called()
    assert result.command_classification == "not-initiated"
    assert result.mutation_classification == "not-mutated"
    assert result.result == "failed"
    assert result.post_action_readback is None
    assert not runtime.exists()


@pytest.mark.parametrize("platform_conclusion", ["failure", "cancelled"])
def test_native_missing_result_preserves_marker_as_unknown(
    native_case, tmp_path, platform_conclusion
):
    case = native_case
    inputs, _feed, _runtime, marker, reference = _prepared(case, tmp_path)
    records = replace(_finalization(case, inputs), terminal=(marker, reference))
    outcome = _finish(
        case,
        records,
        publisher=platform_conclusion,
        step=platform_conclusion,
        terminal=reference,
    )
    assert outcome.disposition == "unknown"
    assert outcome.possibly_mutated
    assert outcome.direct_predecessor.kind == "mutation-marker"
    assert outcome.direct_predecessor.reference == reference


def test_native_success_without_complete_readback_stays_failed(
    native_case, monkeypatch, tmp_path
):
    case = native_case
    inputs, feed, runtime, marker, reference = _prepared(case, tmp_path)

    def publish(**_kwargs):
        feed.get = Mock(
            side_effect=native.NuGetTransportError("lost post-push readback")
        )
        return native.NuGetPublicationInvocation(
            case.profile.profile_digest,
            case.artifact.content.content_sha256.removeprefix("sha256:"),
            "f" * 64,
            native.NuGetHttpResponse(
                RESOURCES.package_publish, 201, (), b"created"
            ),
            None,
        )

    invocation = Mock(side_effect=publish)
    monkeypatch.setattr(native, "publish_nuget_once", invocation)
    result = execute_nuget_publication(
        inputs,
        current=case.current,
        run_attempt=1,
        durable_marker=marker,
        marker_reference=reference,
        runtime_directory=runtime,
        read_resources=lambda: RESOURCES,
        authority=feed.authority,
        transport=feed,
        token=TOKEN,
        clock=lambda: LATER,
    )
    invocation.assert_called_once()
    assert result.command_classification == "definitive-success"
    assert result.result == "failed"
    assert result.mutation_classification == "possibly-mutated"
    assert result.post_action_readback is None


@pytest.mark.parametrize(
    "change",
    [
        "source-touch",
        "attempt-two",
        "authorization",
        "action-archive",
        "inspection-identity",
        "inspection-witness",
        "runtime-profile",
    ],
)
def test_native_publisher_rejects_broken_authority_before_mutation(
    native_case, monkeypatch, tmp_path, change
):
    case = native_case
    feed = ModeledFeed(case)
    inputs = _publication(case, feed)
    if change == "source-touch":
        case.client.read_source.side_effect = GovernanceGitReadError(
            "protected path touched"
        )
    elif change == "runtime-profile":
        profile = case.profile.to_document()
        profile["executableSha256"] = "f" * 64
        monkeypatch.setattr(
            native, "nuget_operation_profile", lambda _resources: profile
        )
    elif change == "authorization":
        inputs = replace(
            inputs,
            authorization_reference=replace(
                inputs.authorization_reference, payload_digest=DIGEST
            ),
        )
    elif change == "action-archive":
        action = inputs.publication_snapshot.materialized_actions[0]
        altered = replace(
            inputs.publication_snapshot,
            materialized_actions=(
                replace(
                    action,
                    nupkg_reference=replace(
                        action.nupkg_reference, payload_digest=DIGEST
                    ),
                ),
            ),
        )
        inputs = replace(
            inputs,
            publication_snapshot=altered,
            publication_snapshot_reference=replace(
                inputs.publication_snapshot_reference,
                payload_digest=altered.snapshot_digest,
            ),
        )
    if change == "inspection-identity":
        feed.authority.inspect_package.return_value["identity"][
            "normalizedVersion"
        ] = "999.0.0"
    elif change == "inspection-witness":
        feed.authority.inspect_package.return_value["witnessBase64"] = (
            base64.b64encode(canonicalize({"target": "9" * 40})).decode()
        )
    publish = Mock(side_effect=AssertionError("unadmitted mutation"))
    monkeypatch.setattr(native, "publish_nuget_once", publish)
    path = tmp_path / "original.nupkg"
    path.write_bytes(case.scenario.result.package)
    runtime = tmp_path / "publisher"
    with pytest.raises((ValueError, TypeError)):
        prepare_nuget_publication(
            inputs,
            current=case.current,
            run_attempt=2 if change == "attempt-two" else 1,
            nupkg=path,
            runtime_directory=runtime,
            authority=feed.authority,
            governance_client=case.client,
            transport=feed,
            token=TOKEN,
            clock=lambda: LATER,
        )
    publish.assert_not_called()
    assert not runtime.exists()


def test_native_eligibility_replay_does_not_grant_fresh_action(native_case):
    case = native_case
    expired = case.eligibility.governance.attestation.expires_at + timedelta(
        seconds=1
    )
    arguments = {
        "intent": case.intent,
        "repository_model": case.scenario.model,
        "policy": case.policy,
        "expected_digest": case.eligibility.canonical_digest,
        "now": expired,
    }
    with pytest.raises(shared.GovernanceFreshnessRejectionError):
        admit_nuget_live_eligibility_decision(
            case.eligibility.canonical_bytes,
            **arguments,
            admission_mode=shared.LiveEligibilityAdmissionMode.CURRENT_FRESHNESS,
        )
    replay = admit_nuget_live_eligibility_decision(
        case.eligibility.canonical_bytes,
        **arguments,
        admission_mode=shared.LiveEligibilityAdmissionMode.AUTHORIZATION_REPLAY,
    )
    assert replay == case.eligibility
    observation = _observe(case, ModeledFeed(case))
    args = _arguments(case)
    args["eligibility"] = replay
    with pytest.raises(ValueError, match="currently fresh"):
        materialize_publication_snapshot(
            args.pop("snapshot"),
            args.pop("decision"),
            (observation,),
            (args.pop("artifact"),),
            **args,
            action_creation_at=expired,
            destination_operation_profile=case.profile,
        )


def test_native_production_admission_stays_empty():
    assert frozenset() == governance._ADMITTED_NUGET_NATIVE_GENERATIONS
    assert frozenset() == governance._ADMITTED_NUGET_ATOMIC_CONTRACTS


@pytest.mark.parametrize("field", ["actor", "selected_ref", "workflow_sha"])
def test_native_eligibility_rejects_unreviewed_control_before_reads(
    native_case, field
):
    case = native_case
    values = {
        "actor": "another-writer",
        "selected_ref": "refs/heads/topic",
        "workflow_sha": "9" * 40,
    }
    client = Mock(spec=shared.GovernanceSourceClient)
    with pytest.raises(ValueError, match=r"protected-main|Intent"):
        evaluate_nuget_live_eligibility(
            case.context,
            intent=replace(case.intent, **{field: values[field]}),
            repository_model=case.scenario.model,
            policy=case.policy,
            client=client,
            now=NOW,
            platform=case.platform,
            profile=case.profile,
        )
    client.is_ref_protected.assert_not_called()
    client.read_source.assert_not_called()


@pytest.mark.parametrize(
    ("age_seconds", "admitted"), [(300, True), (301, False), (-1, False)]
)
def test_native_initial_control_freshness_boundary(
    native_case, age_seconds, admitted
):
    case = native_case
    produced = replace(
        case.produced,
        platform=replace(
            case.produced.platform,
            observed_at=NOW - timedelta(seconds=age_seconds),
        ),
    )
    arguments = {
        "intent": case.intent,
        "repository_model": case.scenario.model,
        "policy": case.policy,
        "expected_digest": produced.decision_digest,
        "now": NOW,
        "admission_mode": shared.LiveEligibilityAdmissionMode.CURRENT_FRESHNESS,
    }
    if admitted:
        result = admit_nuget_live_eligibility_decision(
            canonicalize(produced.to_document()), **arguments
        )
        assert result.platform.observed_at == NOW - timedelta(seconds=300)
    else:
        with pytest.raises(ValueError, match="authority mismatch"):
            admit_nuget_live_eligibility_decision(
                canonicalize(produced.to_document()), **arguments
            )


def test_native_finalizer_rejects_substituted_marker_profile(
    native_case, tmp_path
):
    case = native_case
    inputs, _feed, _runtime, marker, _reference_before = _prepared(
        case, tmp_path
    )
    altered_profile = case.profile.to_document()
    altered_profile["executableSha256"] = "f" * 64
    altered = replace(
        marker,
        profile_match=replace(
            marker.profile_match,
            actual_profile=NugetDestinationOperationProfile(
                canonicalize(altered_profile)
            ),
        ),
    )
    reference = _reference(
        altered.marker_digest, case.intent.workflow_run_id, 508
    )
    records = replace(
        _finalization(case, inputs), terminal=(altered, reference)
    )
    with pytest.raises(ValueError, match="native marker profile"):
        _finish(
            case,
            records,
            publisher="cancelled",
            step="cancelled",
            terminal=reference,
        )
