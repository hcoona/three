"""Native qualification commands over actual Git and modeled Build/Quality."""

from __future__ import annotations

# ruff: noqa: D103
import hashlib
import json
from copy import deepcopy
from dataclasses import replace
from types import SimpleNamespace
from unittest.mock import Mock

import pytest
from three_workflow_delivery_v3 import cli
from three_workflow_delivery_v3.adapters import dotnet as native
from three_workflow_delivery_v3.canonical import canonicalize
from three_workflow_delivery_v3.records.release import (
    NugetReleaseArtifact,
    QualificationDecision,
    QualificationEvidence,
    admit_release_record,
    release_artifact_transport_name,
)
from three_workflow_delivery_v3.release.identity import (
    normalize_buddy_live_intent,
)
from three_workflow_delivery_v3.release.nuget_planner import (
    NUGET_BUILD_OBLIGATION,
    NUGET_CONSUMER_OBLIGATION,
    NUGET_CONTENTS_OBLIGATION,
    plan_nuget_live_qualification,
)
from three_workflow_delivery_v3.release.nuget_qualification import (
    NugetMechanicalBuildResult,
    nuget_mechanical_build_document,
    nuget_mechanical_build_from_bytes,
)
from three_workflow_delivery_v3.repository.dotnet_provider import (
    DOTNET_TOOLCHAIN,
    NativeNuGetHelper,
)

from .release.test_nuget_qualification import _intent_and_binding, _witness
from .repository.test_dotnet_compiler import _admitted
from .test_nuget_cli import (
    _set_option,
    _uploaded,
    _write,
)
from .test_nuget_cli import (
    control_case as control_case,  # noqa: PLC0414
)
from .test_nuget_cli import (
    control_source as control_source,  # noqa: PLC0414
)


@pytest.fixture(scope="module")
def qualification_source(control_source):
    source = control_source
    _, modeled_binding = _intent_and_binding(source.context, source.model)
    binding = replace(
        modeled_binding, intent_digest=source.intent.intent_digest
    )
    provider = _admitted(source.context, source.manifest, source.result)
    snapshot = plan_nuget_live_qualification(
        source.intent, binding, source.model, provider
    )
    witness = _witness(snapshot, source.model)
    identity = snapshot.destination_projections[0].coordinate.identity
    expectation = native.DotnetArtifactExpectation(
        identity.package_name,
        identity.native_version,
        identity.normalized_package_id,
        identity.normalized_version,
        witness.canonical_bytes,
    )
    package = b"opaque modeled native archive\x00\xff; never repack"
    result = native.DotnetBuildResult(
        package,
        native.DotnetArtifactManifest(
            f"{identity.package_name}.{identity.normalized_version}.nupkg",
            (
                "Hcoona.ReleaseSmoke.GithubPackages.nuspec",
                "lib/net10.0/Hcoona.ReleaseSmoke.GithubPackages.dll",
                "workflow-delivery/provenance.json",
            ),
            "sha256:" + hashlib.sha256(package).hexdigest(),
            "sha512:" + hashlib.sha512(package).hexdigest(),
            len(package),
        ),
        expectation,
        witness.canonical_bytes,
        snapshot.build_requests[0].source_input_manifest,
        DOTNET_TOOLCHAIN,
    )
    return SimpleNamespace(
        binding=binding, snapshot=snapshot, witness=witness, result=result
    )


@pytest.fixture
def qualification_case(control_case, qualification_source, monkeypatch):
    case = control_case
    case.qualified = qualification_source
    source = qualification_source
    case.attempt_args = _uploaded(
        case.root, "attempt_binding", source.binding.to_document(), 202
    )
    case.snapshot_args = _uploaded(
        case.root, "qualification_snapshot", source.snapshot.to_document(), 301
    )
    case.witness_args = _uploaded(
        case.root, "package_witness", source.witness.to_document(), 302
    )
    case.package = case.root / "package.nupkg"
    case.mechanics = case.root / "mechanics.json"
    case.failure = case.root / "build-failure.json"
    case.artifact = case.root / "artifact.json"
    case.build_evidence = case.root / "build.json"
    case.contents_evidence = case.root / "contents.json"
    case.consumer_evidence = case.root / "consumer.json"
    case.decision = case.root / "decision.json"
    case.plan = case.root / "plan.json"
    case.planned_witness = case.root / "planned-witness.json"
    case.helper = NativeNuGetHelper(case.root / "unexecuted-helper.dll")
    case.build = Mock(return_value=source.result)
    case.contents = Mock(return_value=source.result.manifest)
    case.consumer = Mock(
        return_value=native.DotnetConsumerResult(
            source.snapshot.release_unit,
            source.snapshot.build_requests[0].witness_digest,
            source.result.manifest.sha256,
            source.result.expectation.normalized_package_id,
            source.result.expectation.normalized_version,
        )
    )
    monkeypatch.setattr(native, "build_dotnet_package", case.build)
    monkeypatch.setattr(
        native, "qualify_nuget_artifact_contents", case.contents
    )
    monkeypatch.setattr(
        native, "qualify_nuget_restore_build_invoke", case.consumer
    )
    return case


def _args(case, command, extra=()):
    return [
        "release",
        "nuget",
        command,
        *case.current,
        *case.snapshot_args,
        "--github-output",
        str(case.github_output),
        *extra,
    ]


def _plan_args(case):
    return [
        "release",
        "nuget",
        "plan-qualification",
        *case.current,
        "--repo-root",
        str(case.source.repo),
        *case.intent_args,
        *case.model_args,
        *case.provider_args,
        *case.attempt_args,
        "--output",
        str(case.plan),
        "--package-witness-output",
        str(case.planned_witness),
        "--github-output",
        str(case.github_output),
    ]


def _build_args(case):
    return _args(
        case,
        "run-build",
        [
            *case.witness_args,
            "--repo-root",
            str(case.source.repo),
            "--helper-dll",
            str(case.helper.helper_dll),
            "--evidence-directory",
            str(case.root / "native-build"),
            "--package-output",
            str(case.package),
            "--mechanical-output",
            str(case.mechanics),
            "--failure-evidence-output",
            str(case.failure),
        ],
    )


def _form_args(case):
    snapshot = case.qualified.snapshot
    return _args(
        case,
        "form-artifact",
        [
            "--package",
            str(case.package),
            "--mechanical-result",
            str(case.mechanics),
            "--package-artifact-id",
            "501",
            "--package-artifact-name",
            release_artifact_transport_name(
                repository=snapshot.repository,
                purpose="live-release",
                output=snapshot.outputs[0],
                qualification_snapshot_digest=snapshot.snapshot_digest,
                workflow_run_id=snapshot.subject.workflow_run_id,
                run_attempt=None,
                producer="build-nuget-package",
            ),
            "--package-artifact-url",
            (
                "https://github.com/hcoona/three/actions/runs/"
                f"{snapshot.subject.workflow_run_id}/artifacts/501"
            ),
            "--package-artifact-digest",
            "sha256:" + "7" * 64,
            "--artifact-output",
            str(case.artifact),
            "--evidence-output",
            str(case.build_evidence),
        ],
    )


def _artifact_args(case):
    return _uploaded(
        case.root,
        "release_artifact",
        json.loads(case.artifact.read_bytes()),
        502,
    )


def _quality_args(case, *, consumer=False):
    extra = [
        *case.witness_args,
        *_artifact_args(case),
        "--package",
        str(case.package),
        "--helper-dll",
        str(case.helper.helper_dll),
        "--output",
        str(case.consumer_evidence if consumer else case.contents_evidence),
    ]
    if consumer:
        extra += ["--evidence-directory", str(case.root / "native-consumer")]
    return _args(
        case, "restore-build-invoke" if consumer else "artifact-contents", extra
    )


def _finalize_args(case, *, omitted=None):
    extra = _artifact_args(case)
    for name, path, artifact_id in (
        ("build_evidence", case.build_evidence, 503),
        ("artifact_contents_evidence", case.contents_evidence, 504),
        ("consumer_evidence", case.consumer_evidence, 505),
    ):
        if name != omitted:
            extra += _uploaded(
                case.root, name, json.loads(path.read_bytes()), artifact_id
            )
    return _args(
        case, "finalize-qualification", [*extra, "--output", str(case.decision)]
    )


def _record(path, record_type):
    content = path.read_bytes()
    return admit_release_record(
        content,
        expected_type=record_type,
        expected_digest="sha256:" + hashlib.sha256(content).hexdigest(),
    )


def _built(case):
    assert cli.main(_build_args(case)) == 0
    assert cli.main(_form_args(case)) == 0


def _qualified(case):
    _built(case)
    assert cli.main(_quality_args(case)) == 0
    assert cli.main(_quality_args(case, consumer=True)) == 0


def test_nuget_qualification_cli_plans_matched_native_facts(qualification_case):
    case = qualification_case
    assert cli.main(_plan_args(case)) == 0
    assert json.loads(case.plan.read_bytes()) == (
        case.qualified.snapshot.to_document()
    )
    assert case.planned_witness.read_bytes() == (
        case.qualified.witness.canonical_bytes
    )
    outputs = dict(
        line.split("=", 1)
        for line in case.github_output.read_text().splitlines()
    )
    snapshot = case.qualified.snapshot
    assert outputs["qualification-snapshot-digest"] == snapshot.snapshot_digest
    assert outputs["package-witness-digest"] == (
        snapshot.build_requests[0].witness_digest
    )
    assert outputs["package-artifact-name"] == release_artifact_transport_name(
        repository=snapshot.repository,
        purpose="live-release",
        output=snapshot.outputs[0],
        qualification_snapshot_digest=snapshot.snapshot_digest,
        workflow_run_id=snapshot.subject.workflow_run_id,
        run_attempt=None,
        producer="build-nuget-package",
    )
    case.evaluate.assert_not_called()
    case.build.assert_not_called()


@pytest.mark.parametrize(
    "fault", ["attempt", "model", "provider", "provider-transport", "npm"]
)
def test_nuget_qualification_cli_rejects_substituted_plan_authority(
    qualification_case, fault
):
    case = qualification_case
    if fault == "attempt":
        binding = replace(
            case.qualified.binding, repository_model_digest="sha256:" + "9" * 64
        )
        case.attempt_args = _uploaded(
            case.root, "attempt_binding", binding.to_document(), 202
        )
    elif fault == "model":
        document = deepcopy(case.source.model.snapshot.to_document())
        document["context"]["request-id"] = "release-request:" + "9" * 64
        case.model_args = _uploaded(
            case.root, "repository_model", document, 102
        )
    elif fault == "provider":
        document = deepcopy(case.provider_document)
        document["provider-request-manifest-digest"] = "sha256:" + "9" * 64
        digest = _write(case.provider_path, document)
        _set_option(case.provider_args, "--provider-artifact-digest", digest)
    elif fault == "provider-transport":
        _set_option(
            case.provider_args,
            "--provider-artifact-digest",
            "sha256:" + "9" * 64,
        )
    else:
        native_intent = case.source.intent
        npm = normalize_buddy_live_intent(
            repository=native_intent.repository,
            selected_ref=native_intent.selected_ref,
            target=native_intent.target,
            actor=native_intent.actor,
            workflow_run_id=native_intent.workflow_run_id,
        )
        case.intent_args = _uploaded(
            case.root, "intent", npm.to_document(), 101
        )
    assert cli.main(_plan_args(case)) == 1
    assert not case.plan.exists()
    assert not case.planned_witness.exists()
    case.evaluate.assert_not_called()
    case.build.assert_not_called()


def test_nuget_qualification_cli_retains_original_package(qualification_case):
    case = qualification_case
    assert cli.main(_plan_args(case)) == 0
    case.snapshot_args = _uploaded(
        case.root,
        "qualification_snapshot",
        json.loads(case.plan.read_bytes()),
        301,
    )
    case.witness_args = _uploaded(
        case.root,
        "package_witness",
        json.loads(case.planned_witness.read_bytes()),
        302,
    )
    _built(case)
    result = case.qualified.result
    assert case.package.read_bytes() == result.package
    assert not case.failure.exists()
    request = case.build.call_args.args[0]
    contract = case.qualified.snapshot.build_requests[0]
    assert request == native.DotnetBuildRequest(
        case.source.repo,
        contract.declared_inputs,
        contract.source_input_manifest,
        case.qualified.witness,
        case.helper,
        case.root / "native-build",
    )
    artifact = _record(case.artifact, NugetReleaseArtifact)
    assert artifact.content.content_sha256 == result.manifest.sha256
    assert artifact.content.content_sha512 == result.manifest.sha512
    assert artifact.content.byte_size == len(result.package)
    assert artifact.transport.producer == "build-nuget-package"
    assert (
        artifact.transport.transport_digest != artifact.content.content_sha256
    )
    assert artifact.source_input_manifest == result.source_input_manifest
    assert artifact.toolchain == result.toolchain
    evidence = _record(case.build_evidence, QualificationEvidence)
    assert evidence.normalized_outcome == "satisfied"
    assert evidence.artifact_digests == (artifact.artifact_digest,)
    outputs = dict(
        line.split("=", 1)
        for line in case.github_output.read_text().splitlines()
    )
    assert outputs["build-status"] == "satisfied"
    assert outputs["package-content-digest"] == result.manifest.sha256
    assert (
        outputs["package-content-digest-hex"]
        == hashlib.sha256(result.package).hexdigest()
    )
    assert outputs["release-artifact-digest"] == artifact.artifact_digest
    assert outputs["build-evidence-digest"] == evidence.evidence_digest
    case.evaluate.assert_not_called()


def test_nuget_qualification_cli_build_failure_emits_only_failed_evidence(
    qualification_case,
):
    case = qualification_case
    case.build.side_effect = ValueError("modeled native pack failure")
    assert cli.main(_build_args(case)) == 0
    failure = _record(case.failure, QualificationEvidence)
    assert failure.normalized_outcome == "failed"
    assert failure.artifact_digests == ()
    assert failure.diagnostics == ("modeled native pack failure",)
    assert not case.package.exists()
    assert not case.mechanics.exists()
    assert not case.artifact.exists()
    outputs = dict(
        line.split("=", 1)
        for line in case.github_output.read_text().splitlines()
    )
    assert outputs["build-status"] == "failed"
    assert outputs["build-evidence-digest"] == failure.evidence_digest
    assert "package-content-digest" not in outputs
    evidence_args = _uploaded(
        case.root, "build_evidence", failure.to_document(), 503
    )
    assert (
        cli.main(
            _args(
                case,
                "finalize-qualification",
                [*evidence_args, "--output", str(case.decision)],
            )
        )
        == 0
    )
    decision = _record(case.decision, QualificationDecision)
    assert decision.terminal_result == "failure"
    assert tuple(item.outcome for item in decision.obligation_dispositions) == (
        "failed",
        "incomplete",
        "incomplete",
    )


def test_nuget_mechanical_result_round_trip(qualification_source):
    source = qualification_source
    mechanics = NugetMechanicalBuildResult(
        source.snapshot.snapshot_digest,
        source.snapshot.build_requests[0].request_digest,
        source.result,
    )
    document = nuget_mechanical_build_document(source.snapshot, mechanics)
    assert document["schema"] == (
        "workflow-delivery/v3/nuget-mechanical-build-result"
    )
    assert document["expectation"] == {
        "package-name": "Hcoona.ReleaseSmoke.GithubPackages",
        "nuget-package-version": source.witness.nbgv.nuget_package_version,
        "normalized-package-id": "hcoona.releasesmoke.githubpackages",
        "normalized-version": source.result.expectation.normalized_version,
        "assembly-name": "Hcoona.ReleaseSmoke.GithubPackages",
        "witness": source.witness.to_document(),
    }
    admitted = nuget_mechanical_build_from_bytes(
        canonicalize(document),
        snapshot=source.snapshot,
        package=source.result.package,
    )
    assert admitted == mechanics
    assert admitted.result.package == source.result.package
    assert "npm-package-version" not in document["expectation"]


@pytest.mark.parametrize(
    "fault",
    [
        "snapshot",
        "request",
        "bytes",
        "sha512",
        "identity",
        "assembly-name",
        "witness",
        "source",
        "toolchain",
    ],
)
def test_nuget_mechanical_result_rejects_substitution(
    qualification_source, fault
):
    source = qualification_source
    mechanics = NugetMechanicalBuildResult(
        source.snapshot.snapshot_digest,
        source.snapshot.build_requests[0].request_digest,
        source.result,
    )
    document = nuget_mechanical_build_document(source.snapshot, mechanics)
    package = source.result.package
    if fault == "snapshot":
        document["qualification-snapshot-digest"] = "sha256:" + "9" * 64
    elif fault == "request":
        document["build-request-digest"] = "sha256:" + "9" * 64
    elif fault == "bytes":
        package += b"changed"
    elif fault == "sha512":
        document["manifest"]["sha512"] = "sha512:" + "9" * 128
    elif fault == "identity":
        document["expectation"]["normalized-version"] = "99.0.0"
    elif fault == "assembly-name":
        document["expectation"]["assembly-name"] = "Another.Assembly"
    elif fault == "witness":
        document["expectation"]["witness"]["catalog-digest"] = (
            "sha256:" + "9" * 64
        )
    elif fault == "source":
        document["source-input-manifest"][0][1] = "sha256:" + "9" * 64
    elif fault == "toolchain":
        document["toolchain"][0][1] = "unaccepted"
    with pytest.raises((TypeError, ValueError)):
        nuget_mechanical_build_from_bytes(
            canonicalize(document), snapshot=source.snapshot, package=package
        )


@pytest.mark.parametrize(
    "fault",
    [
        "schema",
        "extra",
        "missing",
        "nested-extra",
        "boolean-size",
        "entries-type",
        "pair-arity",
        "noncanonical",
    ],
)
def test_nuget_mechanical_result_rejects_malformed_record(
    qualification_source, fault
):
    source = qualification_source
    mechanics = NugetMechanicalBuildResult(
        source.snapshot.snapshot_digest,
        source.snapshot.build_requests[0].request_digest,
        source.result,
    )
    document = nuget_mechanical_build_document(source.snapshot, mechanics)
    if fault == "schema":
        document["schema"] = "workflow-delivery/v3/mechanical-build-result"
    elif fault == "extra":
        document["extra"] = True
    elif fault == "missing":
        del document["source-input-manifest"]
    elif fault == "nested-extra":
        document["manifest"]["extra"] = True
    elif fault == "boolean-size":
        document["manifest"]["byte-size"] = True
    elif fault == "entries-type":
        document["manifest"]["entries"] = "not-an-array"
    elif fault == "pair-arity":
        document["toolchain"][0].append("extra")
    content = canonicalize(document)
    if fault == "noncanonical":
        content += b"\n"
    with pytest.raises((TypeError, ValueError)):
        nuget_mechanical_build_from_bytes(
            content, snapshot=source.snapshot, package=source.result.package
        )


@pytest.mark.parametrize("fault", ["bytes", "name", "url", "id", "mechanics"])
def test_nuget_qualification_cli_rejects_substituted_upload(
    qualification_case, fault
):
    case = qualification_case
    assert cli.main(_build_args(case)) == 0
    arguments = _form_args(case)
    if fault == "bytes":
        case.package.write_bytes(case.package.read_bytes() + b"changed")
    elif fault == "mechanics":
        document = json.loads(case.mechanics.read_bytes())
        document["build-request-digest"] = "sha256:" + "9" * 64
        _write(case.mechanics, document)
    else:
        _set_option(
            arguments,
            "--package-artifact-" + fault,
            "0" if fault == "id" else "wrong",
        )
    assert cli.main(arguments) == 1
    assert not case.artifact.exists()
    assert not case.build_evidence.exists()


@pytest.mark.parametrize(
    "fault", ["witness", "witness-transport", "snapshot-run"]
)
def test_nuget_qualification_cli_rejects_substituted_build_authority(
    qualification_case, fault
):
    case = qualification_case
    if fault == "witness":
        document = case.qualified.witness.to_document()
        document["catalog-digest"] = "sha256:" + "9" * 64
        case.witness_args = _uploaded(
            case.root, "package_witness", document, 302
        )
    elif fault == "witness-transport":
        _set_option(
            case.witness_args,
            "--package-witness-artifact-digest",
            "sha256:" + "9" * 64,
        )
    arguments = _build_args(case)
    if fault == "snapshot-run":
        _set_option(arguments, "--workflow-run-id", "999")
    assert cli.main(arguments) == 1
    assert not case.package.exists()
    assert not case.mechanics.exists()
    case.build.assert_not_called()


def test_nuget_qualification_cli_emits_distinct_quality_evidence(
    qualification_case,
):
    case = qualification_case
    _qualified(case)
    source = case.qualified
    case.contents.assert_called_once_with(
        source.result.package, source.result.expectation, case.helper
    )
    case.consumer.assert_called_once_with(
        source.result.package,
        source.result.expectation,
        case.helper,
        evidence_directory=case.root / "native-consumer",
    )
    contents = _record(case.contents_evidence, QualificationEvidence)
    consumer = _record(case.consumer_evidence, QualificationEvidence)
    assert contents.obligation.obligation_id == NUGET_CONTENTS_OBLIGATION
    assert consumer.obligation.obligation_id == NUGET_CONSUMER_OBLIGATION
    assert (
        contents.normalized_outcome
        == consumer.normalized_outcome
        == "satisfied"
    )
    assert contents.evidence_id != consumer.evidence_id
    assert contents.artifact_digests == consumer.artifact_digests
    assert case.package.read_bytes() == source.result.package


@pytest.mark.parametrize("consumer", [False, True])
def test_nuget_qualification_cli_preserves_quality_failure(
    qualification_case, consumer
):
    case = qualification_case
    _built(case)
    failed = case.consumer if consumer else case.contents
    failed.side_effect = ValueError("modeled native quality failure")
    assert cli.main(_quality_args(case)) == 0
    assert cli.main(_quality_args(case, consumer=True)) == 0
    assert cli.main(_finalize_args(case)) == 0
    decision = _record(case.decision, QualificationDecision)
    assert decision.terminal_result == "failure"
    assert {
        item.obligation.obligation_id
        for item in decision.obligation_dispositions
        if item.outcome == "failed"
    } == {NUGET_CONSUMER_OBLIGATION if consumer else NUGET_CONTENTS_OBLIGATION}


@pytest.mark.parametrize(
    "omitted",
    [None, "build_evidence", "artifact_contents_evidence", "consumer_evidence"],
)
def test_nuget_qualification_cli_finalizes_required_evidence(
    qualification_case, omitted
):
    case = qualification_case
    _qualified(case)
    assert cli.main(_finalize_args(case, omitted=omitted)) == 0
    decision = _record(case.decision, QualificationDecision)
    assert decision.terminal_result == (
        "success" if omitted is None else "incomplete"
    )
    states = {
        item.obligation.obligation_id: item.outcome
        for item in decision.obligation_dispositions
    }
    assert states == {
        obligation: "incomplete" if name == omitted else "satisfied"
        for name, obligation in (
            ("build_evidence", NUGET_BUILD_OBLIGATION),
            ("artifact_contents_evidence", NUGET_CONTENTS_OBLIGATION),
            ("consumer_evidence", NUGET_CONSUMER_OBLIGATION),
        )
    }
    assert (
        f"qualification-result={decision.terminal_result}"
        in case.github_output.read_text()
    )


@pytest.mark.parametrize("fault", ["producer", "run", "snapshot", "transport"])
def test_nuget_qualification_cli_rejects_substituted_evidence(
    qualification_case, fault
):
    case = qualification_case
    _qualified(case)
    arguments = _finalize_args(case)
    if fault == "transport":
        _set_option(
            arguments,
            "--consumer-evidence-artifact-digest",
            "sha256:" + "9" * 64,
        )
    else:
        document = json.loads(case.consumer_evidence.read_bytes())
        if fault == "producer":
            document["producer"] = "npm-artifact-qualification"
        elif fault == "run":
            document["workflow-run-id"] = 999
        else:
            document["qualification-snapshot-digest"] = "sha256:" + "9" * 64
        replacement = _uploaded(case.root, "consumer_evidence", document, 505)
        for option, value in zip(
            replacement[::2], replacement[1::2], strict=True
        ):
            _set_option(arguments, option, value)
    assert cli.main(arguments) == 1
    assert not case.decision.exists()


@pytest.mark.parametrize(
    "group",
    [
        "build-evidence",
        "artifact-contents-evidence",
        "consumer-evidence",
        "release-artifact",
    ],
)
def test_nuget_qualification_cli_rejects_partial_transport(
    qualification_case, group
):
    case = qualification_case
    _qualified(case)
    arguments = _finalize_args(case)
    index = arguments.index("--" + group)
    del arguments[index : index + 2]
    assert cli.main(arguments) == 1
    assert not case.decision.exists()


@pytest.mark.parametrize("fault", ["package", "producer", "run", "snapshot"])
def test_nuget_qualification_cli_rejects_unbound_consumer_inputs(
    qualification_case, fault
):
    case = qualification_case
    _built(case)
    arguments = _quality_args(case, consumer=True)
    if fault == "package":
        case.package.write_bytes(case.package.read_bytes() + b"changed")
    else:
        document = json.loads(case.artifact.read_bytes())
        if fault == "producer":
            document["transport"]["producer"] = "build-tarball"
        elif fault == "run":
            document["transport"]["workflow-run-id"] = 999
        else:
            document["qualification-snapshot-digest"] = "sha256:" + "9" * 64
        replacement = _uploaded(case.root, "release_artifact", document, 502)
        for option, value in zip(
            replacement[::2], replacement[1::2], strict=True
        ):
            _set_option(arguments, option, value)
    assert cli.main(arguments) == 1
    assert not case.consumer_evidence.exists()
    case.consumer.assert_not_called()


@pytest.mark.parametrize(
    "command", ["plan", "build", "form", "contents", "consumer", "finalize"]
)
def test_nuget_qualification_cli_rejects_reruns(qualification_case, command):
    case = qualification_case
    _qualified(case)
    arguments = {
        "plan": lambda: _plan_args(case),
        "build": lambda: _build_args(case),
        "form": lambda: _form_args(case),
        "contents": lambda: _quality_args(case),
        "consumer": lambda: _quality_args(case, consumer=True),
        "finalize": lambda: _finalize_args(case),
    }[command]()
    for effect in (case.build, case.contents, case.consumer):
        effect.reset_mock()
    _set_option(arguments, "--run-attempt", "2")
    assert cli.main(arguments) == 1
    assert not case.plan.exists()
    assert not case.decision.exists()
    case.build.assert_not_called()
    case.contents.assert_not_called()
    case.consumer.assert_not_called()
