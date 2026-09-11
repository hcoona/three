"""Native observation and approval CLI over admitted records and modeled I/O."""

from __future__ import annotations

# ruff: noqa: D103, PLR2004
import hashlib
import json
from copy import deepcopy
from dataclasses import replace
from datetime import timedelta
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import Mock

import pytest
from three_workflow_delivery_v3 import cli
from three_workflow_delivery_v3.adapters import nuget_github_packages as native
from three_workflow_delivery_v3.canonical import canonicalize
from three_workflow_delivery_v3.records.release import (
    ApprovalBundle,
    NugetReleaseArtifact,
    NugetRemoteStateObservation,
    PublicationAuthorization,
    PublicationSnapshot,
    QualificationDecision,
    QualificationSnapshot,
    ReleaseAttemptBinding,
)
from three_workflow_delivery_v3.release.finalizer import finalize_qualification
from three_workflow_delivery_v3.release.governance_git import GovernanceGitRead
from three_workflow_delivery_v3.release.identity import (
    normalize_buddy_live_intent,
)
from three_workflow_delivery_v3.release.nuget_planner import (
    plan_nuget_live_qualification,
)
from three_workflow_delivery_v3.release.nuget_qualification import (
    NugetMechanicalBuildResult,
    form_uploaded_nuget_release_artifact,
    qualify_release_nuget_consumer,
    qualify_release_nuget_contents,
)

from .release.test_nuget_destination_runtime import NOW, ModeledFeed
from .release.test_nuget_qualification import _transport
from .repository.test_dotnet_compiler import _admitted
from .test_nuget_cli import (
    TOKEN,
    _clock,
    _command,
    _eligibility_args,
    _evaluate,
    _set_option,
    _uploaded,
)
from .test_nuget_cli import (
    control_case as control_case,  # noqa: PLC0414
)
from .test_nuget_cli import (
    control_source as control_source,  # noqa: PLC0414
)
from .test_nuget_cli import (
    enabled_case as enabled_case,  # noqa: PLC0414
)
from .test_nuget_qualification_cli import (
    _artifact_args,
    _record,
)
from .test_nuget_qualification_cli import (
    qualification_case as qualification_case,  # noqa: PLC0414
)
from .test_nuget_qualification_cli import (
    qualification_source as qualification_source,  # noqa: PLC0414
)

COMMANDS = (
    "observe-github-packages",
    "materialize-publication",
    "form-approval-bundle",
    "form-publication-authorization",
)


def _reference_args(case, name, path, artifact_id):
    digest = "sha256:" + hashlib.sha256(path.read_bytes()).hexdigest()
    option = "--" + name.replace("_", "-")
    return [
        option,
        str(path),
        option + "-digest",
        digest,
        option + "-artifact-id",
        str(artifact_id),
        option + "-artifact-digest",
        digest,
        option + "-artifact-url",
        (
            "https://github.com/hcoona/three/actions/runs/"
            f"{case.source.intent.workflow_run_id}/artifacts/{artifact_id}"
        ),
        option + "-payload-path",
        path.name,
    ]


@pytest.fixture(scope="module")
def prepared_records():
    # Only canonical immutable bytes are shared. Each case admits fresh records
    # and owns its files, mutable services, clock and output captures.
    return {}


def _qualified_inputs(case):
    assert _evaluate(case) == 0
    case.eligibility_args = _eligibility_args(case)
    assert (
        cli.main(
            _command(
                case,
                "bind-live-attempt",
                [*case.model_args, *case.eligibility_args],
            )
        )
        == 0
    )
    binding = _record(case.output, ReleaseAttemptBinding)
    case.attempt_args = _uploaded(
        case.root, "attempt_binding", binding.to_document(), 202
    )
    # Earlier command groups have their own integration scenarios. Form this
    # unit's qualified inputs through their real contracts without replaying
    # every producer CLI for each approval case.
    source = case.source
    provider = _admitted(source.context, source.manifest, source.result)
    snapshot = plan_nuget_live_qualification(
        source.intent, binding, source.model, provider
    )
    case.qualified = SimpleNamespace(
        binding=binding,
        snapshot=snapshot,
        witness=case.qualified.witness,
        result=case.qualified.result,
    )
    case.snapshot_args = _uploaded(
        case.root, "qualification_snapshot", snapshot.to_document(), 301
    )
    result = case.qualified.result
    artifact, build_evidence = form_uploaded_nuget_release_artifact(
        snapshot,
        NugetMechanicalBuildResult(
            snapshot.snapshot_digest,
            snapshot.build_requests[0].request_digest,
            result,
        ),
        _transport(snapshot),
    )
    contents = qualify_release_nuget_contents(
        snapshot, artifact, result.package, result.expectation, case.helper
    )
    consumer = qualify_release_nuget_consumer(
        snapshot,
        artifact,
        result.package,
        result.expectation,
        case.helper,
        evidence_directory=case.root / "native-consumer",
    )
    decision = finalize_qualification(
        snapshot, (build_evidence, contents, consumer), (artifact,)
    )
    case.artifact.write_bytes(canonicalize(artifact.to_document()))
    case.decision.write_bytes(canonicalize(decision.to_document()))
    case.artifact_record, case.decision_record = artifact, decision
    return {
        "eligibility": (case.root / "eligibility.json").read_bytes(),
        "binding": canonicalize(binding.to_document()),
        "snapshot": canonicalize(snapshot.to_document()),
        "artifact": case.artifact.read_bytes(),
        "decision": case.decision.read_bytes(),
    }


@pytest.fixture
def approval_case(
    enabled_case, qualification_case, prepared_records, monkeypatch
):
    case = enabled_case
    assert case is qualification_case
    key = (case.source.model.canonical_digest, canonicalize(case.document))
    if key not in prepared_records:
        prepared_records[key] = _qualified_inputs(case)
    inputs = prepared_records[key]
    case.output.write_bytes(inputs["eligibility"])
    case.eligibility_args = _eligibility_args(case)
    case.attempt_args = _uploaded(
        case.root, "attempt_binding", json.loads(inputs["binding"]), 202
    )
    case.snapshot_args = _uploaded(
        case.root, "qualification_snapshot", json.loads(inputs["snapshot"]), 301
    )
    binding = _record(case.root / "attempt_binding.json", ReleaseAttemptBinding)
    snapshot = _record(
        case.root / "qualification_snapshot.json", QualificationSnapshot
    )
    case.qualified = SimpleNamespace(
        binding=binding,
        snapshot=snapshot,
        witness=case.qualified.witness,
        result=case.qualified.result,
    )
    case.artifact.write_bytes(inputs["artifact"])
    case.decision.write_bytes(inputs["decision"])
    case.artifact_record = _record(case.artifact, NugetReleaseArtifact)
    case.decision_record = _record(case.decision, QualificationDecision)
    case.preconditions = prepared_records.setdefault((key, "preconditions"), {})
    case.feed = ModeledFeed(
        SimpleNamespace(
            artifact=case.artifact_record,
            scenario=SimpleNamespace(result=case.qualified.result),
        )
    )
    case.helper_factory = Mock(return_value=case.feed.authority)
    case.transport_factory = Mock(return_value=case.feed)
    monkeypatch.setattr(
        cli.dotnet_provider, "NativeNuGetHelper", case.helper_factory
    )
    monkeypatch.setattr(native, "NuGetHttpTransport", case.transport_factory)
    for role in (
        "observation",
        "publication",
        "summary",
        "bundle",
        "authorization",
    ):
        setattr(
            case,
            role,
            case.root / (role + (".md" if role == "summary" else ".json")),
        )
    case.summary = case.root / "reviewer-summary.md"
    case.github_output.unlink(missing_ok=True)
    case.client.reset_mock()
    case.client_factory.reset_mock()
    return case


def _args(case, command):
    output = {
        "observe-github-packages": case.observation,
        "materialize-publication": case.publication,
        "form-approval-bundle": case.bundle,
        "form-publication-authorization": case.authorization,
    }[command]
    arguments = [
        "release",
        "nuget",
        command,
        *case.current,
        "--repo-root",
        str(case.source.repo),
        *case.intent_args,
        *case.attempt_args,
        "--output",
        str(output),
        "--github-output",
        str(case.github_output),
    ]
    if command == "form-approval-bundle":
        arguments += _uploaded(
            case.root,
            "qualification_decision",
            case.decision_record.to_document(),
            506,
        )
    else:
        arguments += [
            *case.model_args,
            *case.eligibility_args,
            *case.snapshot_args,
            *_artifact_args(case),
            *_reference_args(
                case, "qualification_decision", case.decision, 506
            ),
        ]
    if command in {"materialize-publication", "form-publication-authorization"}:
        arguments += _uploaded(
            case.root,
            "observation",
            json.loads(case.observation.read_bytes()),
            601,
        )
    if command in {"form-approval-bundle", "form-publication-authorization"}:
        arguments += [
            *_reference_args(
                case, "publication_snapshot", case.publication, 602
            ),
            *_reference_args(case, "reviewer_summary", case.summary, 603),
            "--control",
            case.source.context.control,
        ]
    if command in {"observe-github-packages", "form-publication-authorization"}:
        arguments += ["--github-token", TOKEN]
    if command == "observe-github-packages":
        arguments += ["--helper-dll", str(case.root / "authority.dll")]
    elif command == "materialize-publication":
        arguments += [
            "--selected-ref",
            case.source.intent.selected_ref,
            "--summary-output",
            str(case.summary),
        ]
    elif command == "form-publication-authorization":
        arguments += [
            *_reference_args(case, "approval_bundle", case.bundle, 604),
            "--approval-boundary-sentinel-result",
            "success",
        ]
    return arguments


def _prepare(case, command):
    for predecessor in COMMANDS[: COMMANDS.index(command)]:
        paths = {
            COMMANDS[0]: (case.observation,),
            COMMANDS[1]: (case.publication, case.summary),
            COMMANDS[2]: (case.bundle,),
        }[predecessor]
        if predecessor not in case.preconditions:
            assert cli.main(_args(case, predecessor)) == 0
            case.preconditions[predecessor] = tuple(
                path.read_bytes() for path in paths
            )
        else:
            for path, content in zip(
                paths, case.preconditions[predecessor], strict=True
            ):
                path.write_bytes(content)
    case.github_output.unlink(missing_ok=True)
    case.feed.events.clear()
    case.client.reset_mock()
    case.client_factory.reset_mock()
    case.helper_factory.reset_mock()
    case.transport_factory.reset_mock()
    return _args(case, command)


def _state(case, state):
    case.feed.present = state != "absent"
    if state == "conflicting":
        case.feed.content += b"changed original archive"
    elif state == "unknown":
        case.feed.get = Mock(
            side_effect=native.NuGetTransportError("lost read")
        )


@pytest.mark.parametrize(
    ("state", "classification", "code"),
    [
        ("absent", "absent", 0),
        ("exact", "exact-satisfied", 0),
        ("conflicting", "conflicting", 1),
        ("unknown", "unprovable", 1),
    ],
)
def test_nuget_approval_cli_observes_native_state(
    approval_case, state, classification, code
):
    case = approval_case
    _state(case, state)
    assert cli.main(_args(case, COMMANDS[0])) == code
    observation = _record(case.observation, NugetRemoteStateObservation)
    assert observation.classification == classification
    assert observation.attempt == case.qualified.binding.attempt
    assert (
        observation.qualification_decision_reference.payload_digest
        == case.decision_record.decision_digest
    )
    assert (
        observation.desired_content_sha256
        == case.artifact_record.content.content_sha256
    )
    assert (
        f"observation-digest={observation.observation_digest}"
        in case.github_output.read_text()
    )
    case.evaluate.assert_not_called()
    case.client_factory.assert_not_called()
    if state == "exact":
        assert (
            observation.active_readback.content_sha256
            == case.artifact_record.content.content_sha256
        )
        assert (
            observation.active_readback.witness_digest
            == case.artifact_record.witness_digest
        )


@pytest.mark.parametrize(
    "fault", ["intent", "eligibility-transport", "model", "attempt", "target"]
)
def test_nuget_approval_cli_rejects_substituted_authority(
    approval_case, fault, capsys
):
    case = approval_case
    arguments = _args(case, COMMANDS[0])
    if fault == "intent":
        intent = case.source.intent
        other = normalize_buddy_live_intent(
            repository=intent.repository,
            selected_ref=intent.selected_ref,
            target=intent.target,
            actor=intent.actor,
            workflow_run_id=intent.workflow_run_id,
        )
        replacement = _uploaded(case.root, "intent", other.to_document(), 101)
        for index in range(0, len(replacement), 2):
            _set_option(arguments, replacement[index], replacement[index + 1])
    else:
        option = {
            "eligibility-transport": "--live-eligibility-artifact-id",
            "model": "--repository-model-digest",
            "attempt": "--attempt-binding-digest",
            "target": "--target",
        }[fault]
        _set_option(
            arguments,
            option,
            999
            if fault == "eligibility-transport"
            else "sha256:" + "f" * 64
            if fault != "target"
            else "f" * 40,
        )
    assert cli.main(arguments) == 1
    expected_error = {
        "intent": "exact native Buddy Intent",
        "eligibility-transport": "transport differs from Attempt binding",
        "model": "canonical digest mismatch",
        "attempt": "digest mismatch",
        "target": "current binding mismatch: target",
    }[fault]
    assert expected_error in capsys.readouterr().err
    assert case.feed.events == []
    case.helper_factory.assert_not_called()
    case.transport_factory.assert_not_called()
    assert not case.observation.exists()
    assert not case.github_output.exists()


@pytest.mark.parametrize("state", ["absent", "exact", "conflicting", "unknown"])
def test_nuget_approval_cli_materializes_only_ready_action(
    approval_case, state, capsys
):
    case = approval_case
    _state(case, state)
    assert cli.main(_args(case, COMMANDS[0])) == (
        0 if state in {"absent", "exact"} else 1
    )
    case.github_output.unlink()
    if state in {"conflicting", "unknown"}:
        assert cli.main(_args(case, COMMANDS[1])) == 1
        assert "observation is not ready" in capsys.readouterr().err
        assert not case.publication.exists()
        assert not case.summary.exists()
        assert not case.github_output.exists()
        return
    assert cli.main(_args(case, COMMANDS[1])) == 0
    publication = _record(case.publication, PublicationSnapshot)
    assert len(publication.materialized_actions) == (
        1 if state == "absent" else 0
    )
    assert (
        publication.qualification_snapshot_digest
        == case.qualified.snapshot.snapshot_digest
    )
    assert case.summary.exists() is (state == "absent")
    outputs = case.github_output.read_text()
    assert f"publish-required={str(state == 'absent').lower()}" in outputs
    if state == "absent":
        (action,) = publication.materialized_actions
        assert (
            action.destination_operation_profile_digest
            == case.profile.profile_digest
        )
        assert (
            action.nupkg_reference.payload_digest
            == case.artifact_record.content.content_sha256
        )
        assert (
            f"resource-concurrency-key={action.serialization_projection}"
            in outputs
        )
    else:
        assert "resource-concurrency-key=no-op" in outputs
        assert "reviewer-digest" not in outputs


def test_nuget_approval_cli_renders_native_reviewer_summary(approval_case):
    case = approval_case
    _prepare(case, COMMANDS[2])
    summary = case.summary.read_text()
    for value in (
        case.source.intent.target,
        case.artifact_record.identity.package_name,
        case.artifact_record.identity.normalized_version,
        case.artifact_record.content.content_sha256,
        case.artifact_record.content.content_sha512,
        case.artifact_record.witness_digest,
        case.artifact_record.transport.artifact_url,
        case.decision_record.decision_digest,
        case.profile.profile_digest,
        *case.artifact_record.entries,
        "restore/build/invoke",
        "Original publication artifact",
    ):
        assert value in summary
    assert "Lifecycle scripts" not in summary
    assert "target tag" not in summary
    assert "Tarball" not in summary


def test_nuget_approval_cli_forms_bound_approval_bundle(approval_case):
    case = approval_case
    assert cli.main(_prepare(case, COMMANDS[2])) == 0
    bundle = _record(case.bundle, ApprovalBundle)
    publication = _record(case.publication, PublicationSnapshot)
    assert (
        bundle.publication_snapshot_reference.payload_digest
        == publication.snapshot_digest
    )
    assert bundle.publication_snapshot_reference.artifact_id == 602
    assert bundle.reviewer_summary_reference.artifact_id == 603
    assert (
        bundle.reviewer_summary_reference.payload_digest
        == "sha256:" + hashlib.sha256(case.summary.read_bytes()).hexdigest()
    )
    assert (
        f"approval-bundle-digest={bundle.bundle_digest}"
        in case.github_output.read_text()
    )
    assert case.feed.events == []
    case.client_factory.assert_not_called()


def test_nuget_approval_cli_rejects_zero_action_approval(approval_case, capsys):
    case = approval_case
    case.feed.present = True
    assert cli.main(_args(case, COMMANDS[0])) == 0
    assert cli.main(_args(case, COMMANDS[1])) == 0
    assert not case.summary.exists()
    case.summary.write_text(
        "A summary cannot authorize a zero-action snapshot."
    )
    case.github_output.unlink()
    assert cli.main(_args(case, COMMANDS[2])) == 1
    assert "qualification closure mismatch" in capsys.readouterr().err
    assert not case.bundle.exists()
    assert not case.github_output.exists()


def test_nuget_approval_cli_authorizes_replayed_publication(
    approval_case, monkeypatch
):
    case = approval_case
    arguments = _prepare(case, COMMANDS[3])
    # A later Authorization retains the original Observation and action.
    # Current Governance must still be read and match before output.
    _clock(monkeypatch, NOW + timedelta(minutes=10))
    assert cli.main(arguments) == 0
    authorization = _record(case.authorization, PublicationAuthorization)
    bundle = _record(case.bundle, ApprovalBundle)
    assert (
        authorization.approval_bundle_reference.payload_digest
        == bundle.bundle_digest
    )
    assert authorization.approval_bundle_reference.artifact_id == 604
    assert authorization.governance_proof.live_enabled is True
    assert authorization.approval_boundary.sentinel_result == "success"
    assert authorization.completed_at == (NOW + timedelta(minutes=10)).strftime(
        "%Y-%m-%dT%H:%M:%SZ"
    )
    assert (
        f"publication-authorization-digest={authorization.authorization_digest}"
        in case.github_output.read_text()
    )
    case.client.read_source.assert_called_once()
    assert case.feed.events == []
    case.evaluate.assert_not_called()


@pytest.mark.parametrize(
    "fault", ["identity", "expiry", "live-enabled", "sentinel"]
)
def test_nuget_approval_cli_rejects_changed_governance(
    approval_case, fault, capsys
):
    case = approval_case
    arguments = _prepare(case, COMMANDS[3])
    document = deepcopy(case.document)
    if fault == "sentinel":
        _set_option(arguments, "--approval-boundary-sentinel-result", "failure")
    else:
        if fault == "expiry":
            document["expires_at"] = "2026-09-09T00:00:00Z"
        elif fault == "live-enabled":
            document["live_enabled"] = False
        case.client.read_source.return_value = GovernanceGitRead(
            case.source.intent.target, "sha1", "f" * 40, canonicalize(document)
        )
    assert cli.main(arguments) == 1
    error = capsys.readouterr().err
    assert ("sentinel" if fault == "sentinel" else "Governance") in error
    assert not case.authorization.exists()
    assert not case.github_output.exists()
    assert case.feed.events == []


@pytest.mark.parametrize(
    ("command", "option", "value"),
    [
        (COMMANDS[1], "--selected-ref", "refs/heads/other"),
        (COMMANDS[1], "--qualification-decision-payload-path", "other.json"),
        (
            COMMANDS[3],
            "--publication-snapshot-artifact-url",
            "https://github.com/hcoona/three/actions/runs/999/artifacts/602",
        ),
        (COMMANDS[2], "--workflow-run-id", "999"),
        (COMMANDS[2], "--reviewer-summary-digest", "sha256:" + "f" * 64),
        (COMMANDS[3], "--observation-digest", "sha256:" + "f" * 64),
        (COMMANDS[3], "--reviewer-summary-artifact-id", "999"),
        (COMMANDS[3], "--control", "workflow-delivery-v3:" + "f" * 40),
    ],
)
def test_nuget_approval_cli_rejects_substituted_references(
    approval_case, command, option, value, capsys
):
    case = approval_case
    arguments = _prepare(case, command)
    output = Path(arguments[arguments.index("--output") + 1])
    _set_option(arguments, option, value)
    assert cli.main(arguments) == 1
    expected_error = {
        "--selected-ref": "selected ref does not match",
        "--qualification-decision-payload-path": (
            "reference payload path mismatch"
        ),
        "--publication-snapshot-artifact-url": "resolved closure mismatch",
        "--workflow-run-id": "current binding mismatch",
        "--reviewer-summary-digest": "payload digest mismatch",
        "--observation-digest": "digest mismatch",
        "--reviewer-summary-artifact-id": "resolved closure mismatch",
        "--control": "control",
    }[option]
    assert expected_error in capsys.readouterr().err
    assert not output.exists()
    assert not case.github_output.exists()
    assert case.feed.events == []
    case.client_factory.assert_not_called()


@pytest.mark.parametrize("command", COMMANDS)
def test_nuget_approval_cli_rejects_reruns(approval_case, command, capsys):
    case = approval_case
    arguments = _prepare(case, command)
    output = Path(arguments[arguments.index("--output") + 1])
    _set_option(arguments, "--run-attempt", 2)
    assert cli.main(arguments) == 1
    assert "rejects GitHub reruns" in capsys.readouterr().err
    assert not output.exists()
    assert not case.github_output.exists()
    assert case.feed.events == []
    case.client_factory.assert_not_called()
    case.helper_factory.assert_not_called()
    case.transport_factory.assert_not_called()


@pytest.mark.parametrize("command", COMMANDS[:2])
def test_nuget_approval_cli_rejects_stale_observation_authority(
    approval_case, command, monkeypatch, capsys
):
    case = approval_case
    arguments = _prepare(case, command)
    output = Path(arguments[arguments.index("--output") + 1])
    _clock(monkeypatch, NOW + timedelta(days=100))
    assert cli.main(arguments) == 1
    assert "fresh" in capsys.readouterr().err
    assert not output.exists()
    assert not case.github_output.exists()
    assert case.feed.events == []
    case.helper_factory.assert_not_called()
    case.client_factory.assert_not_called()


def test_nuget_approval_cli_rejects_substituted_publication_basis(
    approval_case, capsys
):
    case = approval_case
    arguments = _prepare(case, COMMANDS[3])
    observation = _record(case.observation, NugetRemoteStateObservation)
    changed = replace(observation, response_identity="sha256:" + "9" * 64)
    replacement = _uploaded(
        case.root, "observation", changed.to_document(), 601
    )
    for index in range(0, len(replacement), 2):
        _set_option(arguments, replacement[index], replacement[index + 1])
    assert cli.main(arguments) == 1
    assert "Observation closure mismatch" in capsys.readouterr().err
    assert not case.authorization.exists()
    assert not case.github_output.exists()
    case.client_factory.assert_not_called()
    assert case.feed.events == []
